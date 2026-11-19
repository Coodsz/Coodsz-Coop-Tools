// Coodsz mod reports: the endpoint behind the Works, Broken and Works only with buttons in the
// Coodsz tools (ModReports.Endpoint in src/BannerlordCoopLauncher.Core/Modules/ModReports.cs).
//
// A Cloudflare Worker. It takes a small anonymous report, checks every field, limits how often
// one address and one install may send, keeps one vote per install per mod version, and files the
// report as a GitHub issue in the public list repository with the labels mod-compat, mod-report
// and pending. The tally (.github/scripts/tally-mod-compat.mjs) skips pending reports. The hourly
// workflow .github/workflows/auto-approve-reports.yml removes pending after a day unless the report
// also has suspicious, spam or invalid. A report this worker finds odd (flagsFor below) gets the
// suspicious label and a reason line and waits for Coodsz: he approves it by removing pending, or
// drops it with invalid or spam.
//
// Privacy: nothing about the player is stored. The install id the tool sends is a random number
// the tool made; the worker keeps only an HMAC of it (VOTER_SALT is a secret), and the issue shows
// only that hash. The address is used only for the rate limiter binding, whose counters Cloudflare
// keeps in memory for a minute; it is never written anywhere. No Steam ids, names or paths are
// accepted (a value that does not fit the plain patterns below is refused).
//
// Bindings and settings (wrangler.toml.template, README.md):
//   VOTES            KV namespace: votes, per install and global day counters
//   REPORT_LIMITER   rate limiting binding, keyed by the caller's address (optional)
//   GITHUB_TOKEN     secret: fine grained token, Issues read and write on GITHUB_REPO, TAOM_GITHUB_REPO, AD1259_GITHUB_REPO, BE_GITHUB_REPO and ROT_GITHUB_REPO
//   VOTER_SALT       secret: a long random string
//   GITHUB_REPO      var, default Coodsz/Coodsz-Coop-and-Friends (Coop and Friends reports)
//   TAOM_GITHUB_REPO var, default Coodsz/Coodsz-TAOM (TAOM reports; the TAOM tally there counts them)
//   AD1259_GITHUB_REPO var, default Coodsz/Coodsz-AD1259 (AD1259 reports; the AD1259 tally there counts them)
//   BE_GITHUB_REPO   var, default Coodsz/Coodsz-BurningEmpires (Burning Empires reports; the BE tally there counts them)
//   ROT_GITHUB_REPO  var, default Coodsz/Coodsz-RealmOfThrones (Realm of Thrones reports)
//   MAX_PER_INSTALL_DAY, MAX_REPORT_WRITES_DAY   vars, defaults 60 and 150 (KV writes a day for /report, so Send my logs keeps its share)
//   SUSPICIOUS_PER_INSTALL_DAY, BURST_BROKEN, BURST_MIN_WORKS   vars, defaults 30, 5 and 10
//   FLIP_THRESHOLD, NEW_INSTALL_MIN_WORKS   vars, defaults 3 and 10
//
// Crash reports (src/crash.js): POST /crash and GET /crash/<code>, with
//   ADMIN_KEY           secret: the key GET /crash/<code> needs in its X-Admin-Key header
//   CRASH_GITHUB_TOKEN  secret (optional): Issues read and write on CRASH_REPO; GITHUB_TOKEN when unset
//   CRASH_REPO          var, default Coodsz/Coodsz-Crash-Reports (private)
//   TAOM_CRASH_REPO, AD1259_CRASH_REPO, BE_CRASH_REPO, ROT_CRASH_REPO   vars, the private repositories of those tools' crash reports
//   MAX_CRASH_PER_INSTALL_DAY, MAX_CRASH_REPORTS_DAY   vars, defaults 10 and 100 (100 keeps the free plan's KV writes, README.md)
//
// In tool replies (src/replies.js, src/reply-keys.js, src/reply-text.js): POST /replies, with
//   COODSZ_GITHUB_ID    var: Coodsz's numeric GitHub user id; only his comments are served. Unset = replies off
//   REPLY_SALT          secret (optional): the HMAC secret of the reply keys; "reply:" + VOTER_SALT when unset
//   CRASH_PING_WEBHOOK  secret (optional): a Discord webhook for the ping on unknown and Coop bug reports
//   REPLY_READ_LIMITER  rate limiting binding (optional): the worker wide cap on GitHub reads for replies
// Yes, it's fixed (src/solved.js): POST /replies/solved, with
//   SOLVED_ACTION       var: close (the default: label player-solved and close the issue) or label (only the label)
//   WORKER_GITHUB_ID    var (optional): the bot account id when the crash token is not Coodsz's own
// No, still broken (src/followup.js): POST /crash with summary.followUp, a comment on the report's own issue
// Reply push (src/webhook.js): POST /github/webhook, GitHub's issue comment webhook on each crash repository, with
//   GITHUB_WEBHOOK_SECRET  secret (optional): the webhook's secret. Unset = no push, POST /replies reads GitHub on request
//   WEBHOOK_LIMITER        rate limiting binding (optional): deliveries per address; REPORT_LIMITER is used without it
// Free plan brake (src/budget.js): rq:<UTC date> in VOTES counts the day's requests in batches; a high count makes
//   POST /replies answer with a longer next. Nothing else is slowed.

import { handleCrashPost, handleCrashGet, underAddressLimit } from './crash.js';
import { handleRepliesPost } from './replies.js';
import { handleSolvedPost } from './solved.js';
import { handleGithubWebhook } from './webhook.js';
import { defaultCounter } from './budget.js';

export const MAX_BODY = 32 * 1024;
export const MAX_REPORTS = 25;
export const MAX_MODS = 250;
export const RESULTS = ['works', 'broken', 'works-with'];
// The tools that send reports: Coop and Friends (coop), the TAOM client (taom), the AD1259 client (ad1259)
// and the Burning Empires client and server (be).
export const TOOLS = ['coop', 'taom', 'ad1259', 'be', 'rot'];

// A report gets this label (and stays pending until Coodsz looks) when flagsFor finds a reason.
export const SUSPICIOUS_LABEL = 'suspicious';
// The tool versions that can send reports: from the first release with the report endpoint, up to
// (not including) a version two minors ahead. Anything else is flagged, not refused.
export const TOOL_VERSIONS = {
  coop: { min: '1.2.61', below: '1.4.0' },
  taom: { min: '1.1.41', below: '1.3.0' },
  ad1259: { min: '1.0.20', below: '1.2.0' },
  be: { min: '1.0.0', below: '1.2.0' },
  rot: { min: '0.1.0', below: '1.2.0' },
};
// Broken reports for one mod are counted per hour; a burst is more than BURST_BROKEN in this many hours.
export const BURST_HOURS = 3;

// The same patterns the tools use (ModReports.CleanId and CleanVersion).
const ID = /^[\p{L}\p{N}_.\-' ()+&!]{1,100}$/u;
const VERSION = /^[\p{L}\p{N}_.\-+ ]{0,40}$/u;
const INSTALL = /^[0-9a-f]{32}$/;
const STEAM_ID = /7656119\d{10}/;

const json = (status, body) => new Response(JSON.stringify(body), {
  status,
  headers: { 'content-type': 'application/json; charset=utf-8', 'cache-control': 'no-store' },
});

const isId = v => typeof v === 'string' && ID.test(v) && !STEAM_ID.test(v);
const isVersion = v => typeof v === 'string' && VERSION.test(v) && !STEAM_ID.test(v);

/** The checked report, or a string that says what is wrong. Only known fields are kept. */
export function validate(body) {
  if (!body || typeof body !== 'object' || Array.isArray(body)) return 'not an object';
  if (body.kind !== 'CoodszModReport' || body.schema !== 1) return 'unknown kind or schema';
  if (!INSTALL.test(body.install ?? '')) return 'install';
  if (!TOOLS.includes(body.tool)) return 'tool';
  for (const key of ['toolVersion', 'coop', 'game'])
    if (body[key] !== undefined && !isVersion(body[key])) return key;
  if (!Array.isArray(body.reports) || body.reports.length === 0 || body.reports.length > MAX_REPORTS) return 'reports';
  if (body.mods !== undefined && (!Array.isArray(body.mods) || body.mods.length > MAX_MODS)) return 'mods';

  const mods = [];
  for (const m of body.mods ?? []) {
    if (!m || !isId(m.id) || (m.version !== undefined && !isVersion(m.version))) return 'mod list entry';
    mods.push({ id: m.id, version: m.version ?? '' });
  }
  const reports = [];
  const seen = new Set();
  for (const r of body.reports) {
    if (!r || !isId(r.id) || !RESULTS.includes(r.result)) return 'report';
    if (r.version !== undefined && !isVersion(r.version)) return 'report version';
    if (r.name !== undefined && r.name !== null && !isId(r.name)) return 'report name';
    if (r.with !== undefined && r.with !== null && !isId(r.with)) return 'report with';
    if (r.result === 'works-with' && !r.with) return 'works-with needs a mod';
    const key = r.id.toLowerCase();
    if (seen.has(key)) return 'the same mod twice';
    seen.add(key);
    reports.push({ id: r.id, name: r.name ?? null, version: r.version ?? '', result: r.result, with: r.result === 'works-with' ? r.with : null });
  }
  return {
    install: body.install, tool: body.tool,
    toolVersion: body.toolVersion ?? '', coop: body.coop ?? '', game: body.game ?? '',
    reports, mods,
  };
}

async function hmacHex(secret, text) {
  const key = await crypto.subtle.importKey('raw', new TextEncoder().encode(secret), { name: 'HMAC', hash: 'SHA-256' }, false, ['sign']);
  const sig = await crypto.subtle.sign('HMAC', key, new TextEncoder().encode(text));
  return [...new Uint8Array(sig)].map(b => b.toString(16).padStart(2, '0')).join('');
}

async function sha256Hex(text) {
  const digest = await crypto.subtle.digest('SHA-256', new TextEncoder().encode(text));
  return [...new Uint8Array(digest)].map(b => b.toString(16).padStart(2, '0')).join('');
}

/** The public reporter name: an HMAC of the install id, so the same PC is the same reporter. */
export async function reporterOf(install, salt) {
  return 'anon-' + (await hmacHex(salt, 'install:' + install)).slice(0, 24);
}

/**
 * One vote per install per mod version and tool. The install id is shared by every Coodsz tool on a
 * PC, so TAOM, AD1259 and BE votes get the tool in the key: a player who rates one mod in Coop and
 * Friends and in the TAOM, AD1259 or BE tools keeps one vote (and one issue) in each repository. Coop and
 * Friends keys stay as they were, so its saved votes keep editing their issues.
 */
export async function voteKey(reporter, modId, version, tool = 'coop') {
  const scope = tool === 'coop' ? '' : tool + ':';
  return 'v:' + reporter + ':' + scope + (await sha256Hex(modId.toLowerCase() + '|' + (version ?? '').toLowerCase())).slice(0, 32);
}

/**
 * The KV key of a mod's report stats per tool: { works, broken, h: { <hour>: Broken that hour } }, and by
 * being there, "someone reported it before in this tool". Like voteKey, Coop and Friends keeps the key it
 * had, so its stats carry on; the other tools get the tool in the key and start their own stats.
 */
export async function modKey(modId, tool = 'coop') {
  const scope = tool === 'coop' ? '' : tool + ':';
  return 'm:' + scope + (await sha256Hex(modId.toLowerCase())).slice(0, 32);
}

const parseVersion = v => (/^v?(\d{1,4})\.(\d{1,4})\.(\d{1,5})$/.exec(v ?? '') ?? []).slice(1).map(Number);
const compareVersions = (a, b) => a[0] - b[0] || a[1] - b[1] || a[2] - b[2];

/** True when the version looks like a release of that tool that can send reports. */
export function isKnownToolVersion(tool, version) {
  const range = TOOL_VERSIONS[tool];
  const v = parseVersion(version);
  if (!range || v.length !== 3) return false;
  return compareVersions(v, parseVersion(range.min)) >= 0 && compareVersions(v, parseVersion(range.below)) < 0;
}

/**
 * The reasons to hold one report for Coodsz (empty when it looks normal). It only reads; the
 * counters are written after the issue is filed. ordinal is this report's number today for the
 * install, stats the mod's stats (null when nobody reported it before), recentBroken the Broken
 * reports for the mod in the last BURST_HOURS hours before this one.
 */
export function flagsFor({ report, item, ordinal, stats, recentBroken, newInstall = false }, limits) {
  const reasons = [];
  if (ordinal > limits.perInstall) reasons.push(`this install sent ${ordinal} reports today, more than ${limits.perInstall}`);
  // A first Works report is approved like any other after a day; only a first Broken or client only report waits for Coodsz.
  if (!stats && item.result !== 'works') reasons.push('the first report anyone sent for this mod id');
  if (!isKnownToolVersion(report.tool, report.toolVersion))
    reasons.push(`tool version ${report.toolVersion || 'missing'} is not a known ${report.tool} version`);
  const works = stats?.works ?? 0;
  if (item.result === 'broken') {
    if (recentBroken + 1 > limits.burstBroken && works >= limits.burstMinWorks)
      reasons.push(`${recentBroken + 1} Broken reports for this mod in the last ${BURST_HOURS} hours while it has ${works} Works`);
    // A flip: Works led before the last BURST_HOURS hours, and with this report Broken leads by
    // the tally's THRESHOLD (the mod would turn Broken on its own).
    const broken = stats?.broken ?? 0;
    const before = works - Math.max(0, broken - recentBroken);
    const after = works - (broken + 1);
    if (stats && before > 0 && after <= -limits.flipThreshold)
      reasons.push(`Broken reports in the last ${BURST_HOURS} hours would turn this mod from ${before} more Works than Broken to Broken`);
    if (newInstall && works < limits.newInstallMinWorks)
      reasons.push(`an install first seen today sent Broken for a mod with ${works} Works`);
  }
  return reasons;
}

const RESULT_TEXT = { works: 'works', broken: 'broken', 'works-with': 'works-only-with' };

/** The issue text, with the same "### Heading" sections as the issue form, so the tally reads both. */
export function issueBody(report, item, reporter, reasons = []) {
  const none = '_No response_';
  const list = report.mods.length ? report.mods.map(m => (m.version ? `${m.id} | ${m.version}` : m.id)).join('\n') : '';
  return [
    '<!-- coodsz-mod-report v1 -->',
    '### Mod id', '', item.id, '',
    '### Mod name', '', item.name ?? item.id, '',
    '### Mod version', '', item.version || none, '',
    '### Result', '', RESULT_TEXT[item.result], '',
    '### Works only with', '', item.with ?? none, '',
    '### Coop version', '', report.coop || none, '',
    '### Bannerlord version', '', report.game || none, '',
    '### Tool', '', `${report.tool} ${report.toolVersion}`.trim(), '',
    '### Mod list', '', list ? '```\n' + list + '\n```' : none, '',
    '### Reporter', '', reporter, '',
    '---',
    'Sent anonymously from the Coodsz tools. It counts once the `pending` label is removed, which the auto approval does after a day unless it is labelled `suspicious`, `spam` or `invalid`. Add `invalid` or `spam` to drop it.',
    ...(reasons.length ? ['', '**Held for review (`suspicious`):** ' + reasons.join('; ') + '.'] : []),
    '',
  ].join('\n');
}

export const DEFAULT_REPO = 'Coodsz/Coodsz-Coop-and-Friends';
export const DEFAULT_TAOM_REPO = 'Coodsz/Coodsz-TAOM';
export const DEFAULT_AD1259_REPO = 'Coodsz/Coodsz-AD1259';
export const DEFAULT_BE_REPO = 'Coodsz/Coodsz-BurningEmpires';
export const DEFAULT_ROT_REPO = 'Coodsz/Coodsz-RealmOfThrones';

/**
 * The repository a tool's reports are filed in. TAOM reports go to the TAOM repository, AD1259
 * reports to the AD1259 repository, Burning Empires reports to the BE repository and Realm of Thrones
 * reports to the RoT repository. Each repository's own tally counts them (compat/taom-mod-compat.json,
 * compat/ad1259-mod-compat.json, compat/be-mod-compat.json and compat/rot-mod-compat.json). Coop and
 * Friends reports go to GITHUB_REPO.
 */
export function repoFor(env, tool) {
  if (tool === 'taom') return env.TAOM_GITHUB_REPO || DEFAULT_TAOM_REPO;
  if (tool === 'ad1259') return env.AD1259_GITHUB_REPO || DEFAULT_AD1259_REPO;
  if (tool === 'be') return env.BE_GITHUB_REPO || DEFAULT_BE_REPO;
  if (tool === 'rot') return env.ROT_GITHUB_REPO || DEFAULT_ROT_REPO;
  return env.GITHUB_REPO || DEFAULT_REPO;
}

export function labelsFor(report, suspicious = false) {
  // TAOM, AD1259 and BE reports get their own label. The Coop and Friends tally leaves TAOM,
  // AD1259 and BE out of compat-list/mods.json; the TAOM, AD1259 and BE repositories' tallies count their own.
  return ['mod-compat', 'mod-report', 'pending', ...(report.tool === 'coop' ? [] : [report.tool]), ...(suspicious ? [SUSPICIOUS_LABEL] : [])];
}

const day = () => new Date().toISOString().slice(0, 10);
const hour = () => Math.floor(Date.now() / 3600000);

// The free Workers plan has 1,000 KV writes a day for the whole account, and Send my logs (crash.js)
// needs its share (README.md, KV writes per crash report). /report spends at most MAX_REPORT_WRITES_DAY
// of them: each request books its writes in w:<UTC date> before it writes anything, and when the day's
// writes are booked or the booking fails, nothing is written and no issue is filed.
export const DEFAULT_REPORT_WRITES_DAY = 150;
// Writes per rated mod: a new vote (the mod's stats before the issue, the vote with the issue number after
// it) or a changed vote (the vote, the stats). Per request: the booking and the install's day count, and
// the first seen day of a new install.
export const WRITES_NEW = 2;
export const WRITES_CHANGED = 2;
export const WRITES_REQUEST = 2;

/** MAX_REPORT_WRITES_DAY as a number; a value that is not a whole number of 0 or more falls back to the default. */
export function reportWritesLimit(env) {
  const raw = String(env.MAX_REPORT_WRITES_DAY ?? '').trim();
  const n = /^\d{1,7}$/.test(raw) ? parseInt(raw, 10) : NaN;
  return Number.isSafeInteger(n) ? n : DEFAULT_REPORT_WRITES_DAY;
}

/**
 * Books by writes in the day's write budget: true when booked, false when it would pass the limit.
 * Fails closed: a failed KV read or write throws, so the request writes nothing and files nothing.
 * KV has no atomic add, so this is a read and a write: requests in the same moment can book past the
 * limit by about one request each, and a data centre that sees the count up to a minute late can book its
 * own full limit in that window. Chosen on purpose (it takes deliberate effort from many places); a
 * Durable Object counter would be exact. Gives { key, at } for giveBack, or null when it would pass the limit.
 */
async function bookWrites(kv, by, limit) {
  const key = `w:${day()}`;
  const now = parseInt((await kv.get(key)) ?? '0', 10) || 0;
  if (now + by > limit) return null;
  await kv.put(key, String(now + by), { expirationTtl: 2 * 24 * 3600 });
  return { key, at: Date.now() };
}

/**
 * Gives back booked writes a request did not make (it stopped early: GitHub failed or KV refused a write),
 * so a GitHub outage does not use up the day. Best effort, and only when it gives back more than its own
 * write, which it counts. It is the one key written twice in a request, so it first waits out the rest of
 * the second since the booking (KV takes about one write a second per key); only failed requests wait.
 */
async function giveBack(kv, booking, unused) {
  if (unused <= 2) return;
  try {
    const wait = booking.at + 1100 - Date.now();
    if (wait > 0) await new Promise(resolve => setTimeout(resolve, wait));
    const now = parseInt((await kv.get(booking.key)) ?? '0', 10) || 0;
    await kv.put(booking.key, String(Math.max(0, now - (unused - 1))), { expirationTtl: 2 * 24 * 3600 });
  } catch (err) {
    console.warn(`unused report writes could not be given back: ${err?.message ?? err}`);
  }
}

/** A KV counter; a failed read counts as 0. */
async function count(kv, key) {
  try { return parseInt((await kv.get(key)) ?? '0', 10) || 0; } catch { return 0; }
}
// Before the hourly Broken counts moved into the stats record, they were one b:<mod hash>:<hour> key per
// mod (all tools together) that expired after BURST_HOURS + 1 hours. Nothing writes them now; they are
// still read so a burst that started before the update is seen. Remove this read after 2026-12-01.
const legacyBrokenKey = (modHash, h) => `b:${modHash}:${h}`;

/** The Broken reports for a mod in the last BURST_HOURS hours: the stats' hourly counts plus the old keys. */
async function recentBrokenOf(kv, stats, modId) {
  const legacy = (await modKey(modId)).slice(2);
  let sum = 0;
  for (let i = 0; i < BURST_HOURS; i++) {
    const h = hour() - i;
    sum += Number.isSafeInteger(stats?.h?.[h]) ? stats.h[h] : 0;
    sum += await count(kv, legacyBrokenKey(legacy, h));
  }
  return sum;
}

/**
 * The day an install (its reporter hash) was first seen: '' when it is new, null when the read failed
 * (counted as seen before, so a KV hiccup never holds a report, and nothing is written for it).
 */
async function firstSeenDay(kv, reporter) {
  try { return (await kv.get(`i:${reporter}`)) ?? ''; } catch { return null; }
}

const whole = n => (Number.isSafeInteger(n) && n >= 0 ? n : 0);

/**
 * The stats after one more report: Works and Broken counted, a Broken in this hour's count, hours that
 * left the burst window dropped. One record, so one KV write per rated mod.
 */
export function nextStats(stats, result, now = hour()) {
  const works = whole(stats?.works) + (result === 'broken' ? 0 : 1);
  const broken = whole(stats?.broken) + (result === 'broken' ? 1 : 0);
  const h = {};
  for (const [at, n] of Object.entries(stats?.h ?? {}))
    if (Number(at) > now - BURST_HOURS && Number(at) <= now && Number.isSafeInteger(n)) h[at] = n;
  if (result === 'broken') h[now] = (h[now] ?? 0) + 1;
  return Object.keys(h).length ? { works, broken, h } : { works, broken };
}

/**
 * A mod's stats for one tool: { stats, read }. read is false when KV failed (the stats are then unknown and
 * must not be written over). A TAOM, AD1259, BE or RoT mod without its own stats yet starts from the shared
 * m:<hash> record (Works and Broken only), which all tools wrote before stats were kept per tool, so the
 * burst and flip holds keep working for well rated mods after the update. Like the old vote keys.
 */
async function statsOf(kv, modId, tool) {
  try {
    const own = await kv.get(await modKey(modId, tool));
    if (own !== null && own !== undefined) return { stats: JSON.parse(own), read: true };
    if (tool === 'coop') return { stats: null, read: true };
    const shared = JSON.parse((await kv.get(await modKey(modId))) ?? 'null');
    return { stats: shared ? { works: whole(shared.works), broken: whole(shared.broken) } : null, read: true };
  } catch {
    return { stats: null, read: false };
  }
}

async function github(env, fetcher, repo, method, path, body) {
  const res = await fetcher(`https://api.github.com/repos/${repo}${path}`, {
    method,
    headers: {
      Authorization: `Bearer ${env.GITHUB_TOKEN}`,
      Accept: 'application/vnd.github+json',
      'X-GitHub-Api-Version': '2022-11-28',
      'User-Agent': 'coodsz-mod-reports-worker',
      'content-type': 'application/json',
    },
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  if (!res.ok) throw new Error(`GitHub ${method} ${path}: ${res.status}`);
  return res.status === 204 ? null : res.json();
}

/** The request handler. fetcher is the fetch used for GitHub (tests pass their own); ctx is the worker's, for waitUntil. */
export async function handle(request, env, fetcher = fetch, ctx = null) {
  // Every request counts toward the free plan brake (budget.js); only the replies' next uses it.
  const flushing = (env.REQUEST_COUNTER ?? defaultCounter).note(env);
  if (flushing) {
    if (ctx?.waitUntil) ctx.waitUntil(flushing);
    else await flushing;
  }
  const url = new URL(request.url);
  if (request.method === 'GET' && (url.pathname === '/' || url.pathname === '/health'))
    return json(200, { ok: true, service: 'coodsz-mod-reports' });
  if (url.pathname === '/crash') {
    if (request.method !== 'POST') return json(405, { ok: false, error: 'POST only' });
    return handleCrashPost(request, env, fetcher, reporterOf, ctx);
  }
  if (url.pathname === '/replies') {
    if (request.method !== 'POST') return json(405, { ok: false, error: 'POST only' });
    return handleRepliesPost(request, env, fetcher);
  }
  if (url.pathname === '/replies/solved') {
    if (request.method !== 'POST') return json(405, { ok: false, error: 'POST only' });
    return handleSolvedPost(request, env, fetcher);
  }
  if (url.pathname === '/github/webhook') {
    if (request.method !== 'POST') return json(405, { ok: false, error: 'POST only' });
    return handleGithubWebhook(request, env);
  }
  if (url.pathname.startsWith('/crash/')) {
    if (request.method !== 'GET') return json(405, { ok: false, error: 'GET only' });
    return handleCrashGet(request, env, url.pathname.slice('/crash/'.length));
  }
  if (url.pathname !== '/report') return json(404, { ok: false, error: 'not found' });
  if (request.method !== 'POST') return json(405, { ok: false, error: 'POST only' });
  if (!env.GITHUB_TOKEN || !env.VOTER_SALT || !env.VOTES) return json(503, { ok: false, error: 'not configured' });

  // The address only keys the in memory rate limiter; it is not kept.
  const address = request.headers.get('CF-Connecting-IP') ?? 'unknown';
  if (!(await underAddressLimit(env, address)))
    return json(429, { ok: false, error: 'too many reports, try again later' });

  const length = parseInt(request.headers.get('content-length') ?? '0', 10);
  if (length > MAX_BODY) return json(413, { ok: false, error: 'too large' });
  const text = await request.text();
  if (text.length > MAX_BODY) return json(413, { ok: false, error: 'too large' });
  let parsed;
  try { parsed = JSON.parse(text); } catch { return json(400, { ok: false, error: 'not JSON' }); }
  const report = validate(parsed);
  if (typeof report === 'string') return json(400, { ok: false, error: 'invalid ' + report });

  const reporter = await reporterOf(report.install, env.VOTER_SALT);
  const perInstall = parseInt(env.MAX_PER_INSTALL_DAY ?? '60', 10);
  const installCount = `q:${reporter}:${day()}`;
  // A failed read counts as 0 for the cap, and the count is then not written (it would replace the real one).
  let sentBefore = null;
  try { sentBefore = parseInt((await env.VOTES.get(installCount)) ?? '0', 10) || 0; } catch { sentBefore = null; }
  if ((sentBefore ?? 0) + report.reports.length > perInstall) return json(429, { ok: false, error: 'too many reports from this install today' });
  const limits = {
    perInstall: parseInt(env.SUSPICIOUS_PER_INSTALL_DAY ?? '30', 10),
    burstBroken: parseInt(env.BURST_BROKEN ?? '5', 10),
    burstMinWorks: parseInt(env.BURST_MIN_WORKS ?? '10', 10),
    flipThreshold: parseInt(env.FLIP_THRESHOLD ?? '3', 10),
    newInstallMinWorks: parseInt(env.NEW_INSTALL_MIN_WORKS ?? '10', 10),
  };
  const firstSeen = await firstSeenDay(env.VOTES, reporter);
  const newInstall = firstSeen === '' || firstSeen === day();

  const repo = repoFor(env, report.tool);
  const listHash = (await sha256Hex(JSON.stringify(report.mods))).slice(0, 16);
  const result = { ok: true, filed: 0, updated: 0, unchanged: 0 };

  // First only reads: which votes are new, changed or the same. The same vote again writes nothing.
  const plans = [];
  try {
    for (const [index, item] of report.reports.entries()) {
      const key = await voteKey(reporter, item.id, item.version, report.tool);
      // A TAOM or AD1259 vote saved before the key had the tool sits under the Coop and Friends key.
      // It is read from there once; the sameRepo check below decides whether its issue is still the one.
      const old = JSON.parse((await env.VOTES.get(key))
        ?? (report.tool === 'coop' ? null : await env.VOTES.get(await voteKey(reporter, item.id, item.version)))
        ?? 'null');
      const vote = { result: item.result, with: item.with, list: listHash };
      // Votes saved before TAOM reports moved to their own repository have no repo; they were all
      // filed in GITHUB_REPO. A vote whose issue is in another repository is filed again here (TAOM
      // votes from before that move, and AD1259 votes from before they moved to Coodsz-AD1259).
      const sameRepo = !!old && Number.isSafeInteger(old.issue) && old.issue > 0 && (old.repo || env.GITHUB_REPO || DEFAULT_REPO) === repo;
      if (sameRepo && old.result === vote.result && old.with === vote.with && old.list === vote.list) {
        result.unchanged++;
        continue;
      }
      plans.push({ index, item, key, old: sameRepo ? old : null, filedBefore: !!old, vote });
    }
  } catch {
    return json(503, { ...result, ok: false, error: 'try again later' });
  }
  if (plans.length === 0) return json(200, result);

  // Then the writes are booked, all at once and before any of them, so a busy day stops here and
  // leaves the rest of the free plan's KV writes to Send my logs.
  const writes = WRITES_REQUEST - (sentBefore === null ? 1 : 0) + (firstSeen === '' ? 1 : 0)
    + plans.reduce((sum, p) => sum + (p.old ? WRITES_CHANGED : WRITES_NEW), 0);
  let booking;
  try {
    booking = await bookWrites(env.VOTES, writes, reportWritesLimit(env));
    if (!booking)
      return json(429, { ...result, ok: false, error: 'the daily report limit is reached, try again tomorrow' });
  } catch (err) {
    console.warn(`the report write budget could not be booked: ${err?.message ?? err}`);
    return json(503, { ...result, ok: false, error: 'try again later' });
  }
  // Every put below is one of the booked writes; a request that stops early gives the rest back.
  let used = 1;
  const put = (key, value, options) => { used++; return env.VOTES.put(key, value, options); };
  const stop = async (status, error) => {
    await giveBack(env.VOTES, booking, writes - used);
    return json(status, { ...result, ok: false, error });
  };
  try {
    if (sentBefore !== null) await put(installCount, String(sentBefore + report.reports.length), { expirationTtl: 2 * 24 * 3600 });
    if (firstSeen === '') await put(`i:${reporter}`, day(), { expirationTtl: 365 * 24 * 3600 });
  } catch (err) {
    console.warn(`the install counters could not be updated: ${err?.message ?? err}`);
  }

  for (const { index, item, key, old, filedBefore, vote } of plans) {
    const mod = await modKey(item.id, report.tool);
    // A failed read is not "no stats": without them the holds cannot be checked and the stats would be
    // written over with a fresh record, so the request stops before any GitHub call (503, sent again later).
    const { stats, read } = await statsOf(env.VOTES, item.id, report.tool);
    if (!read) {
      console.warn('the mod stats could not be read');
      return stop(503, 'try again later');
    }
    const recentBroken = item.result === 'broken' ? await recentBrokenOf(env.VOTES, stats, item.id) : 0;
    // A vote filed before the stats existed still means someone reported the mod before.
    const known = stats ?? (filedBefore ? { works: 0 } : null);
    const reasons = flagsFor({ report, item, ordinal: (sentBefore ?? 0) + index + 1, stats: known, recentBroken, newInstall }, limits);
    const body = issueBody(report, item, reporter, reasons);
    if (old) {
      try {
        // A changed vote replaces the old one and goes back to review (held again when it looks odd).
        // The labels go on first: when that call fails, github() throws and the body is not changed,
        // so a new vote is never counted without review.
        await github(env, fetcher, repo, 'POST', `/issues/${old.issue}/labels`, { labels: ['pending', ...(reasons.length ? [SUSPICIOUS_LABEL] : [])] });
        await github(env, fetcher, repo, 'PATCH', `/issues/${old.issue}`, { body, state: 'open' });
      } catch {
        return stop(502, 'could not file the report');
      }
      try {
        await put(key, JSON.stringify({ ...vote, issue: old.issue, repo }));
      } catch (err) {
        // The issue has the new vote but KV the old one: a 503 makes the tool send it again, which edits
        // the same issue and stores the vote (with a 200 the old vote stays, and sending that one again
        // would look unchanged).
        console.warn(`a changed vote could not be stored: ${err?.message ?? err}`);
        return stop(503, 'try again later');
      }
      try {
        await put(mod, JSON.stringify(nextStats(stats, item.result)));
      } catch (err) {
        console.warn(`mod stats could not be updated: ${err?.message ?? err}`);
      }
      result.updated++;
    } else {
      // KV must take a write before an issue is filed: the mod's stats go first, so when KV takes no
      // more writes the answer is 503 and no issue is filed that no vote points to. The vote with its issue
      // number is then the first write to its key in this request, clear of KV's one write a second per key.
      // The cost: when GitHub then fails, the stats already count the report, so sending it again counts it
      // twice, and a first Broken report for a mod is no longer held as a first report on that second send.
      try {
        await put(mod, JSON.stringify(nextStats(stats, item.result)));
      } catch (err) {
        console.warn(`a report could not be stored: ${err?.message ?? err}`);
        return stop(503, 'try again later');
      }
      let issue;
      try {
        const title = `[Mod] ${item.id}${item.version ? ' ' + item.version : ''}`;
        issue = await github(env, fetcher, repo, 'POST', '/issues', { title, body, labels: labelsFor(report, reasons.length > 0) });
      } catch {
        return stop(502, 'could not file the report');
      }
      try {
        await put(key, JSON.stringify({ ...vote, issue: issue.number, repo }));
      } catch (err) {
        // KV took a write a moment ago, so this is rare; a second send would file the vote again.
        console.warn(`a vote could not be stored: ${err?.message ?? err}`);
      }
      result.filed++;
    }
  }
  return json(200, result);
}

export default {
  fetch: (request, env, ctx) => handle(request, env, fetch, ctx),
};
