// In tool replies: POST /replies, where the tools ask for Coodsz's answers to the reports they sent
// (docs/design/in-tool-replies.md, Worker endpoints).
//
// Request:  {"kind":"CoodszReplyCheck","schema":1,"reports":[{"code":"CR-7KQ2M","key":"<43 chars>"}]}
//           1 to 10 reports, at most 4 KB. POST so the key never shows in a URL or an access log.
//           v1.3.5 tools add "fast": true (they follow their own schedule, and next 30 is fine for them).
// Response: always 200 for a well formed request, one entry per requested code in request order:
//           {"ok":true,"next":1800,"replies":[{"code":"CR-7KQ2M","status":"answered",
//            "items":[{"id":"123456","at":"...","text":"...","buttons":["send-logs"]}]}]}
//           with "fixedIn":"1.3.3" next to "status":"fixed". An unknown, expired, unfiled or old report
//           and a wrong key all give items: [] and status looking. 400 is a malformed request only;
//           429 is {ok:false,error,next}.
//
// status (statusFor), the first that fits:
//   fixed     the issue has a fixed-in-v<version> label ("fixedIn" holds the version)
//   looking   the player's No, still broken (followup.js) is newer than Coodsz's newest answer
//   solved    the player-solved label (Yes, it's fixed, solved.js)
//   answered  an answer is served
//   looking   otherwise
// Only labels count, not the open or closed state: Coodsz closes answered issues too. On a tracking
// issue the labels are the tracking issue's, so fixed-in-v1.3.3 there marks every report on it.
//
// Without GITHUB_WEBHOOK_SECRET the answers are read from the report's GitHub issue on request: the
// comments since the report was filed, newest pages first, at most 5 pages of 100. With it, the webhook
// (webhook.js) keeps them in rr:<code> and a check reads that, with a slow GitHub read behind it (Push,
// below). Only comments by COODSZ_GITHUB_ID (a User, checked by id because logins can be renamed) count,
// every other author is ignored, collaborators too. The newest answer comment wins (reply-text.js says
// which comments are answers); a /retract serves a withdrawn note in its place.
//
// GitHub budget: the crash token also files reports, so reads are kept small. One key and code is read
// from GitHub at most once per KEY_INTERVAL (later checks get the remembered answer), every answer is
// cached for CACHE_SECONDS, and the whole worker makes at most MAX_GITHUB_READS_MINUTE page reads a
// minute (the REPLY_READ_LIMITER binding, or a per instance count without it).

import { CODE, underAddressLimit } from './crash.js';
import { REPLY_KEY, REPLY_TTL, replySigner, openReport, checkRecord } from './reply-keys.js';
import { parseAnswer, answerFor, cleanReply, RETRACTED_TEXT, RETRACTED_AUTO_TEXT, MAX_REPLY_TEXT } from './reply-text.js';
import { FOLLOW_UP_MARKER } from './followup.js';
import { brakeFor, defaultCounter, defaultPushWrites, MAX_PUSH_WRITES } from './budget.js';

export const MAX_REPLIES_BODY = 4096;
export const MAX_REPLY_CODES = 10;
// Seconds until the tool should ask again.
export const NEXT_CHECK = 1800;
export const NEXT_AFTER_LIMIT = 900;
export const NEXT_AFTER_TOKEN_ERROR = 6 * 3600;
export const CACHE_SECONDS = 300;
// One key and code asks GitHub at most this often; the tools check every 30 minutes.
export const KEY_INTERVAL = 600;
export const MAX_COMMENT_PAGES = 5;
export const MAX_GITHUB_READS_MINUTE = 30;
export const SEND_LOGS_BUTTON = 'send-logs';
// The report status the tools show as a badge: Looking into it, Answered, Solved or Fixed in v1.3.3.
export const STATUS_LOOKING = 'looking';
export const STATUS_ANSWERED = 'answered';
export const STATUS_SOLVED = 'solved';
export const STATUS_FIXED = 'fixed';
// The label That fixed it adds (solved.js), and the label Coodsz adds when a release fixes it.
export const SOLVED_LABEL = 'player-solved';
const FIXED_IN_LABEL = /^fixed-in-v?(\d{1,4}(?:\.\d{1,4}){1,3})$/i;

const json = (status, body) => new Response(JSON.stringify(body), {
  status,
  headers: { 'content-type': 'application/json; charset=utf-8', 'cache-control': 'no-store' },
});

/** The checked request: a list of { code, key }, or a string that says what is wrong. */
export function validateReplyCheck(body) {
  if (!body || typeof body !== 'object' || Array.isArray(body)) return 'not an object';
  if (body.kind !== 'CoodszReplyCheck' || body.schema !== 1) return 'unknown kind or schema';
  if (!Array.isArray(body.reports) || body.reports.length < 1 || body.reports.length > MAX_REPLY_CODES) return 'reports';
  const reports = [];
  for (const r of body.reports) {
    if (!r || typeof r !== 'object' || typeof r.code !== 'string' || !CODE.test(r.code)) return 'code';
    if (typeof r.key !== 'string' || !REPLY_KEY.test(r.key)) return 'key';
    reports.push({ code: r.code, key: r.key });
  }
  return reports;
}

/**
 * The cache of cleaned answers. A small in memory map in front of the Cache API: the Cache API does
 * nothing on a workers.dev address, so the map is what saves GitHub calls there. storage is
 * caches.default in the worker; tests pass none. put takes the seconds to keep it.
 */
export function makeReplyCache(storage = null, maxEntries = 2000) {
  const memory = new Map();
  const url = key => 'https://coodsz-replies.invalid/v1/' + encodeURIComponent(key);
  return {
    async get(key, now = Date.now()) {
      const hit = memory.get(key);
      if (hit && hit.until > now) return hit.value;
      memory.delete(key);
      if (!storage) return null;
      try {
        const res = await storage.match(url(key));
        return res ? await res.json() : null;
      } catch {
        return null;
      }
    },
    async put(key, value, now = Date.now(), seconds = CACHE_SECONDS) {
      if (memory.size >= maxEntries) memory.delete(memory.keys().next().value);
      memory.set(key, { value, until: now + seconds * 1000 });
      if (!storage) return;
      try {
        await storage.put(url(key), new Response(JSON.stringify(value), {
          headers: { 'content-type': 'application/json', 'cache-control': `max-age=${seconds}` },
        }));
      } catch {
        // The memory copy is enough.
      }
    },
  };
}

const defaultCache = makeReplyCache(globalThis.caches?.default ?? null);

/**
 * The worker wide cap on GitHub page reads: the REPLY_READ_LIMITER binding when it is bound, else a
 * count per minute in this instance. makeReadCap(clock) gives a fresh one (tests).
 */
export function makeReadCap(clock = () => Date.now()) {
  let minute = -1;
  let used = 0;
  return async env => {
    if (env.REPLY_READ_LIMITER) {
      try {
        const { success } = await env.REPLY_READ_LIMITER.limit({ key: 'github-reads' });
        return success !== false;
      } catch (err) {
        // On purpose: a failing binding falls back to the count in this instance, never to no cap.
        console.warn(`the GitHub read limiter failed: ${err?.message ?? err}`);
      }
    }
    const now = Math.floor(clock() / 60000);
    if (now !== minute) { minute = now; used = 0; }
    if (used >= MAX_GITHUB_READS_MINUTE) return false;
    used++;
    return true;
  };
}

const defaultReadCap = makeReadCap();

/** True for a comment Coodsz wrote: his numeric id and a User account, never a login match. */
export function byCoodsz(comment, env) {
  const id = Number(String(env.COODSZ_GITHUB_ID ?? '').trim());
  return Number.isSafeInteger(id) && id > 0 && comment?.user?.id === id && comment.user.type === 'User';
}

/**
 * True for a comment the worker itself posted: the crash token is Coodsz's own, so that is his account
 * (byCoodsz), or WORKER_GITHUB_ID when the token belongs to a bot account. Never a collaborator.
 */
export function byWorker(comment, env) {
  if (byCoodsz(comment, env)) return true;
  const id = Number(String(env.WORKER_GITHUB_ID ?? '').trim());
  return Number.isSafeInteger(id) && id > 0 && comment?.user?.id === id;
}

// Said once per running copy: a marker skipped for its author is either a collaborator's copy, or the
// crash token belongs to a bot account and WORKER_GITHUB_ID was not set at the deploy (then no badge
// ever goes back to Looking into it after a No).
let skippedMarkerWarned = false;
export function warnSkippedMarker(comment) {
  if (skippedMarkerWarned) return;
  skippedMarkerWarned = true;
  console.warn(`a coodsz-followup comment by GitHub user ${Number(comment?.user?.id) || 'unknown'} was skipped: only Coodsz's account or WORKER_GITHUB_ID counts. If the crash token is a bot account, set WORKER_GITHUB_ID to its id`);
}

/** Lets the one time warning show again (tests). */
export function resetSkippedMarkerWarning() {
  skippedMarkerWarned = false;
}

/**
 * The answer items for one report from its issue comments (oldest first): the newest answer comment
 * for this code, or a withdrawn note when that is a /retract after an answer. [] when there is none.
 */
export function answerItems(comments, code, tracked, env) {
  return answerState(comments, code, tracked, env).items;
}

/** answerItems plus whether the item served is a withdrawn note (a /retract), which counts as no answer for the status. */
export function answerState(comments, code, tracked, env) {
  return foldAnswers(comments.map(c => answerEvent(c, code, tracked, env)).filter(Boolean));
}

/**
 * What one comment means for one report, already cleaned, or null when it means nothing: a follow up
 * marker the worker wrote ({ kind: 'followup' }), Coodsz's /retract ({ kind: 'retract' }) or his answer
 * ({ kind: 'reply', text, logs, auto }), each with the comment's id and newest date. The polling read and
 * the webhook (webhook.js) both go through here, so both clean and check a comment the same way.
 */
export function answerEvent(c, code, tracked, env) {
  // The player's No, still broken (followup.js). Only a comment the worker wrote counts (byWorker); a
  // collaborator's copy of the marker is ignored.
  const followUp = FOLLOW_UP_MARKER.exec(typeof c?.body === 'string' ? c.body.replace(/^\s+/, '') : '');
  if (followUp && followUp[1] === code) {
    if (byWorker(c, env)) return { id: String(c.id), kind: 'followup' };
    warnSkippedMarker(c);
    return null;
  }
  if (!byCoodsz(c, env)) return null;
  const answer = parseAnswer(c.body);
  if (!answerFor(answer, code, tracked)) return null;
  const at = c.updated_at || c.created_at || '';
  if (answer.kind === 'retract') return { id: String(c.id), at, kind: 'retract' };
  const text = cleanReply(answer.text, code);
  if (!text) return null;
  return { id: String(c.id), at, kind: 'reply', text, logs: !!answer.logs, auto: !!answer.auto };
}

/** answerState from the events of one report (answerEvent), oldest first. */
export function foldAnswers(events) {
  let served = null;
  // A follow up after the answer: the report waits for Coodsz again.
  let waiting = false;
  for (const e of events) {
    if (e.kind === 'followup') {
      waiting = true;
      continue;
    }
    if (e.kind === 'retract') {
      if (served) served = { id: e.id, at: e.at, text: served.auto ? RETRACTED_AUTO_TEXT : RETRACTED_TEXT, buttons: [], auto: served.auto, retracted: true };
      continue;
    }
    served = { id: e.id, at: e.at, text: e.text, buttons: e.logs ? [SEND_LOGS_BUTTON] : [], auto: !!e.auto, retracted: false };
    waiting = false;
  }
  return served
    ? { items: [{ id: served.id, at: served.at, text: served.text, buttons: served.buttons }], retracted: served.retracted, waiting }
    : { items: [], retracted: false, waiting };
}

/**
 * The status of one report: { status, fixedIn? }. issue is GitHub's issue (or null when it could not be
 * read); answered is true when a real answer (not a withdrawn note) is served; waiting is true when the
 * player's No, still broken came after it, which wins over every label except fixed-in-v<version>: Coodsz
 * adds that when a release fixes it, so it counts even without a new /reply.
 */
export function statusFor(issue, answered, waiting = false) {
  const labels = Array.isArray(issue?.labels) ? issue.labels.map(l => (typeof l === 'string' ? l : l?.name)).filter(l => typeof l === 'string') : [];
  for (const label of labels) {
    const fixed = FIXED_IN_LABEL.exec(label);
    if (fixed) return { status: STATUS_FIXED, fixedIn: fixed[1] };
  }
  if (waiting) return { status: STATUS_LOOKING };
  if (labels.includes(SOLVED_LABEL)) return { status: STATUS_SOLVED };
  return { status: answered ? STATUS_ANSWERED : STATUS_LOOKING };
}

/** The last page number from GitHub's Link header, or 1 when there is no rel="last". */
export function lastPage(link) {
  const m = /<[^>]*[?&]page=(\d+)[^>]*>;\s*rel="last"/.exec(link ?? '');
  return m ? Number(m[1]) : 1;
}

class TokenError extends Error {}
class ReadCapped extends Error {}

/**
 * The comments to read for one report, oldest first. Up to MAX_COMMENT_PAGES pages: all of them when
 * they fit, else only the newest pages in a row (page 1 is read only to find the last page and then left
 * out), so a newer /reply or /retract is never missed. A gap only hides older answers, which a newer
 * one replaces anyway, and a /retract whose answer fell in the gap serves nothing.
 */
async function githubGet(env, fetcher, readCap, url) {
  if (!(await readCap(env))) throw new ReadCapped();
  const res = await fetcher(url, {
    method: 'GET',
    headers: {
      Authorization: `Bearer ${env.CRASH_GITHUB_TOKEN || env.GITHUB_TOKEN}`,
      Accept: 'application/vnd.github+json',
      'X-GitHub-Api-Version': '2022-11-28',
      'User-Agent': 'coodsz-mod-reports-worker',
    },
  });
  if (res.status === 401 || res.status === 403) throw new TokenError(String(res.status));
  if (!res.ok) throw new Error(`GitHub answered ${res.status}`);
  return res;
}

async function readComments(env, fetcher, record, readCap) {
  const page = async n => {
    const res = await githubGet(env, fetcher, readCap,
      `https://api.github.com/repos/${record.repo}/issues/${record.issue}/comments?per_page=100&since=${encodeURIComponent(record.created)}&page=${n}`);
    const list = await res.json();
    if (!Array.isArray(list)) throw new Error('GitHub sent no list');
    return { list, last: lastPage(res.headers.get('link')) };
  };
  const first = await page(1);
  const last = Math.max(1, first.last);
  if (last <= MAX_COMMENT_PAGES) {
    const rest = [];
    for (let n = 2; n <= last; n++) rest.push(...(await page(n)).list);
    return { comments: [...first.list, ...rest], complete: true, gap: null, pages: last };
  }
  const newest = [];
  for (let n = last - (MAX_COMMENT_PAGES - 2); n <= last; n++) newest.push(...(await page(n)).list);
  // Not every comment was read: everything older than the newest pages is a gap (page 1 only gave the page
  // count). A stored answer in it is kept (mergeEvents), and the read is never a full list.
  const oldest = Math.min(Number.MAX_SAFE_INTEGER, ...newest.map(c => c?.id).filter(Number.isSafeInteger));
  return { comments: newest, complete: false, gap: { after: 0, before: oldest }, pages: last };
}

/** The label names of an issue (GitHub sends objects, older tests strings), at most 100. */
export function labelNames(issue) {
  return Array.isArray(issue?.labels)
    ? issue.labels.map(l => (typeof l === 'string' ? l : l?.name)).filter(l => typeof l === 'string' && l.length <= 100).slice(0, 100)
    : null;
}

/**
 * One report read from GitHub: { events, labels, newest } (events from answerEvent, labels null when the
 * issue itself could not be read, newest the highest comment id read), or { fail: <next> } with
 * skipped when it was a passing problem. GitHub 401 and 403 fail with a 6 hour next.
 */
async function readGitHub(env, fetcher, readCap, code, record) {
  if (!(env.CRASH_GITHUB_TOKEN || env.GITHUB_TOKEN)) {
    console.warn('no GitHub token is set, so no replies can be read');
    return { fail: NEXT_AFTER_TOKEN_ERROR, token: true };
  }
  const tokenError = err => {
    console.warn(`GitHub answered ${err.message} when reading replies: renew the crash report token (it expires 2027-01-20)`);
    return { fail: NEXT_AFTER_TOKEN_ERROR, token: true };
  };
  let comments;
  let complete;
  let gap;
  let pages;
  try {
    ({ comments, complete, gap, pages } = await readComments(env, fetcher, record, readCap));
  } catch (err) {
    if (err instanceof TokenError) return tokenError(err);
    if (err instanceof ReadCapped) console.warn('the GitHub read cap for replies is reached, this check waits');
    else console.warn(`the replies could not be read: ${err?.message ?? err}`);
    return { fail: NEXT_CHECK, skipped: true };
  }
  const events = comments.map(c => answerEvent(c, code, record.tracked === true, env)).filter(Boolean);
  const newest = comments.reduce((max, c) => (Number.isSafeInteger(c?.id) && c.id > max ? c.id : max), 0);
  try {
    const res = await githubGet(env, fetcher, readCap, `https://api.github.com/repos/${record.repo}/issues/${record.issue}`);
    const issue = await res.json();
    return { events, newest, complete, gap, pages, labels: labelNames(issue && typeof issue === 'object' && !Array.isArray(issue) ? issue : null) ?? [] };
  } catch (err) {
    if (err instanceof TokenError) return tokenError(err);
    console.warn(`the report status could not be read: ${err?.message ?? err}`);
    return { events, newest, complete, gap, pages, labels: null, skipped: true };
  }
}

/** { items, status } served from a report's events and labels (labels null: the issue was not read). */
function served(events, labels) {
  const { items, retracted, waiting } = foldAnswers(events);
  return { items, status: statusFor(labels === null ? null : { labels }, items.length > 0 && !retracted, waiting) };
}

/**
 * { items, status, next } for one report whose key opened it, read from GitHub on request: the comments,
 * then the issue for its labels (statusFor). GitHub 401 and 403 give items [] and a 6 hour next. A check
 * that could not read everything is skipped: what it has is served, but nothing is cached.
 */
async function readAnswers(env, fetcher, cache, readCap, code, record) {
  const cacheKey = `${record.repo}/${record.issue}/${code}`;
  const cached = await cache.get(cacheKey);
  // A cached list is no full list: it is one copy's memory, and another copy may have served a newer answer since.
  if (Array.isArray(cached?.items)) return { items: cached.items, status: cached.status, next: NEXT_CHECK };
  const read = await readGitHub(env, fetcher, readCap, code, record);
  if (read.fail) return { items: [], status: { status: STATUS_LOOKING }, next: read.fail, ...(read.skipped ? { skipped: true } : {}) };
  const answer = served(read.events, read.labels);
  if (read.skipped) return { ...answer, next: NEXT_CHECK, skipped: true };
  await cache.put(cacheKey, answer);
  // A read of a long issue that skipped pages is no full list, and neither is one of several pages: a comment
  // deleted on an earlier page between two page requests moves the next one onto a page already read.
  return { ...answer, next: NEXT_CHECK, ...(read.complete && read.pages <= 1 ? { full: true } : {}) };
}

// ----- Push (webhook.js) -----
//
// With GITHUB_WEBHOOK_SECRET set, GitHub tells the worker about each new, edited or deleted comment on a
// crash issue (POST /github/webhook), and the worker keeps each report's answers inside its own reply
// record: rr:<code>.push = { labels, events }, the events of answerEvent, cleaned, oldest first, at most
// MAX_PUSH_EVENTS, and only the labels the status uses (statusLabels). A check reads rr:<code> anyway to
// open the key, so the answers cost no extra KV read and GitHub is not asked.
//
// KV budget: rr:<code> is written only when the stored answers or labels really change, never on a plain
// check. When the next GitHub read is due lives in the reply cache (the Cache API, and this running copy's
// memory, which is all there is on a workers.dev address), not in KV: each report is read from GitHub at
// most once per SYNC_INTERVAL per running copy of the worker. A missed marker only costs one more GitHub
// read under the same REPLY_READ_LIMITER cap. That read catches a missed webhook and a label change.

// Seconds until a tool that follows the push schedule may ask again (the tools have their own schedule;
// this is the shortest wait the worker allows, and older tools wait 10 minutes at least). Raising it is
// the one change that slows every v1.3.5 tool down without a tool update (README.md, Reply push budget).
export const PUSH_NEXT = 30;
export const SYNC_INTERVAL = 30 * 60;
// A GitHub read that failed for a passing reason (the read cap, a 5xx) is tried again this much later.
export const SYNC_RETRY = 2 * 60;
export const MAX_PUSH_EVENTS = 20;
const EVENT_KINDS = ['reply', 'retract', 'followup'];
const EVENT_ID = /^\d{1,20}$/;

/** True when the webhook can keep answers: its secret is set. Without it the worker reads GitHub on request as before. */
export function pushOn(env) {
  return typeof env.GITHUB_WEBHOOK_SECRET === 'string' && env.GITHUB_WEBHOOK_SECRET.length > 0;
}

const byId = (a, b) => (a.id.length - b.id.length) || (a.id < b.id ? -1 : a.id > b.id ? 1 : 0);

/** Only events in the shape answerEvent makes, sorted by comment id, the newest MAX_PUSH_EVENTS. */
export function checkEvents(list) {
  if (!Array.isArray(list)) return [];
  const seen = new Set();
  const kept = [];
  for (const e of list) {
    if (!e || typeof e !== 'object' || typeof e.id !== 'string' || !EVENT_ID.test(e.id) || !EVENT_KINDS.includes(e.kind) || seen.has(e.id)) continue;
    if (e.kind !== 'followup' && (typeof e.at !== 'string' || e.at.length > 40)) continue;
    if (e.kind === 'reply' && (typeof e.text !== 'string' || e.text.length === 0 || e.text.length > MAX_REPLY_TEXT)) continue;
    seen.add(e.id);
    const event = e.kind === 'reply' ? { id: e.id, at: e.at, kind: e.kind, text: e.text, logs: !!e.logs, auto: !!e.auto }
      : e.kind === 'retract' ? { id: e.id, at: e.at, kind: e.kind } : { id: e.id, kind: e.kind };
    // When the webhook stored it (mergeEvents keeps an unlisted event only when it is newer than the read).
    if (Number.isSafeInteger(e.storedAt) && e.storedAt > 0) event.storedAt = e.storedAt;
    kept.push(event);
  }
  return kept.sort(byId).slice(-MAX_PUSH_EVENTS);
}

/** True when two events say the same thing, whenever each was stored. */
export function sameEvent(a, b) {
  const { storedAt: x, ...left } = a ?? {};
  const { storedAt: y, ...right } = b ?? {};
  return JSON.stringify(left) === JSON.stringify(right);
}

/** The labels statusFor uses (fixed-in-v<version> and player-solved), sorted, so other label changes write nothing. */
export function statusLabels(labels) {
  if (!Array.isArray(labels)) return [];
  return [...new Set(labels.filter(l => typeof l === 'string' && (FIXED_IN_LABEL.test(l) || l === SOLVED_LABEL)))].sort();
}

// Tombstones: comments the webhook took out (deleted, or edited into no answer), with the date of that
// change, so a delivery that arrives late or again cannot bring the old answer back. A delete has GONE_FOR_GOOD,
// which no edit passes. The next GitHub read drops the tombstones of comments it no longer lists as answers.
export const MAX_GONE = 20;
export const GONE_FOR_GOOD = '9999-12-31T23:59:59Z';

/** Only tombstones in the shape { id, at }, sorted by comment id, the newest MAX_GONE. */
export function checkGone(list) {
  if (!Array.isArray(list)) return [];
  const seen = new Set();
  const kept = [];
  for (const g of list) {
    if (!g || typeof g.id !== 'string' || !EVENT_ID.test(g.id) || typeof g.at !== 'string' || g.at.length > 40 || seen.has(g.id)) continue;
    seen.add(g.id);
    kept.push({ id: g.id, at: g.at });
  }
  return kept.sort(byId).slice(-MAX_GONE);
}

/**
 * The push state kept in a report's record: { labels, events, gone, synced }, or null when there is none.
 * synced says a complete GitHub read was stored in it. Nothing decides on it any more (full comes only from a
 * fresh complete read in the same request); it is kept so the record keeps its shape.
 */
export function pushState(record) {
  const push = record?.push;
  if (!push || typeof push !== 'object' || Array.isArray(push)) return null;
  return { labels: statusLabels(push.labels), events: checkEvents(push.events), gone: checkGone(push.gone), synced: push.synced === true };
}

const samePush = (a, b) => JSON.stringify({ labels: a?.labels ?? [], events: a?.events ?? [], gone: a?.gone ?? [], synced: a?.synced === true })
  === JSON.stringify({ labels: b?.labels ?? [], events: b?.events ?? [], gone: b?.gone ?? [], synced: b?.synced === true });

const EMPTY_PUSH = { labels: [], events: [], gone: [], synced: false };
const sameJson = (a, b) => JSON.stringify(a) === JSON.stringify(b);

/**
 * Lays one change (ours, made from the snapshot base) over what another writer stored since (theirs), by
 * comment id: what changed in theirs since base comes in unless ours has the same comment at a newer or
 * equal date, a tombstone beats every answer no newer than it, an answer newer than its tombstone clears
 * it, and labels another writer changed win when ours left them as they were. synced stays once either
 * side has it.
 *
 * Not healed here: when another writer dropped an event without a tombstone (a GitHub read that no longer
 * lists it) while this change still has it, it comes back. That needs two writers at the same moment, and
 * the next GitHub read drops it again.
 */
export function mergePush(base, ours, theirs) {
  base = base ?? EMPTY_PUSH;
  theirs = theirs ?? EMPTY_PUSH;
  const baseEvents = new Map(base.events.map(e => [e.id, JSON.stringify(e)]));
  const baseGone = new Map(base.gone.map(g => [g.id, g.at]));
  const events = new Map(ours.events.map(e => [e.id, e]));
  const gone = new Map(ours.gone.map(g => [g.id, g.at]));
  for (const g of theirs.gone)
    if (baseGone.get(g.id) !== g.at && !(gone.get(g.id) >= g.at)) gone.set(g.id, g.at);
  for (const e of theirs.events) {
    if (baseEvents.get(e.id) === JSON.stringify(e)) continue;
    const mine = events.get(e.id);
    if (mine && (mine.at ?? '') >= (e.at ?? '')) continue;
    events.set(e.id, e);
  }
  for (const [id, at] of gone) {
    const e = events.get(id);
    if (!e) continue;
    if (e.at && e.at > at) gone.delete(id);
    else events.delete(id);
  }
  const labels = !sameJson(theirs.labels, base.labels) && sameJson(ours.labels, base.labels) ? theirs.labels : ours.labels;
  return {
    labels, events: checkEvents([...events.values()]), gone: checkGone([...gone].map(([id, at]) => ({ id, at }))),
    synced: ours.synced === true || theirs.synced === true,
  };
}

/**
 * Keeps { labels, events, gone, synced } in rr:<code> when it differs from the snapshot record it was made
 * from. Gives { result, push, held }: result is stored, unchanged, skipped (the day's MAX_PUSH_WRITES are
 * used, budget.js) or failed, push is what this worker serves now, and held is true only when KV holds that
 * push (stored, or already the same). A report in its last two minutes is not written (KV takes no
 * expiration closer than 60 seconds).
 *
 * KV has no compare and set, so the record is read again right before the put and the change is laid over
 * what another delivery or check stored since the snapshot (mergePush). The keys and every other field come
 * from that fresh read, never from the snapshot, so a push write cannot drop a reply key. That read is not a
 * lock: a KV read in another data centre can be 60 seconds or more stale, so two writers close together can
 * still lose one answer, and the next 30 minute GitHub read heals it. One key takes about one write a second;
 * a burst past that fails (KV answers 429), which is failed here (the webhook answers 503), healed the same way.
 */
export async function storePush(env, code, record, push, now = Date.now()) {
  const base = pushState(record) ?? EMPTY_PUSH;
  const ours = { labels: statusLabels(push.labels), events: checkEvents(push.events), gone: checkGone(push.gone), synced: push.synced === true || base.synced };
  if (samePush(base, ours)) return { result: 'unchanged', push: base, held: true };
  const expiration = Math.floor(Date.parse(record.created) / 1000) + REPLY_TTL;
  if (!Number.isFinite(expiration) || expiration < Math.floor(now / 1000) + 120) return { result: 'unchanged', push: ours, held: false };
  const writes = env.PUSH_WRITES ?? defaultPushWrites;
  if ((await writes.count(env)) >= MAX_PUSH_WRITES) {
    console.warn('the day\'s reply push writes are used up: the 30 minute GitHub read serves the answers until midnight UTC');
    return { result: 'skipped', push: ours, held: false };
  }
  let raw;
  try {
    raw = await env.VOTES.get('rr:' + code);
  } catch (err) {
    console.warn(`the reply state could not be read again: ${err?.message ?? err}`);
    return { result: 'failed', push: ours, held: false };
  }
  let fresh = null;
  try {
    fresh = checkRecord(JSON.parse(raw ?? 'null'));
  } catch {
    fresh = null;
  }
  // The report expired or went in between: nothing to keep.
  if (!fresh) return { result: 'unchanged', push: ours, held: false };
  const merged = mergePush(base, ours, pushState(fresh));
  if (samePush(pushState(fresh), merged)) return { result: 'unchanged', push: merged, held: true };
  try {
    await env.VOTES.put('rr:' + code, JSON.stringify({ ...fresh, push: merged }), { expiration });
  } catch (err) {
    console.warn(`the reply state could not be stored: ${err?.message ?? err}`);
    return { result: 'failed', push: ours, held: false };
  }
  const flushing = writes.note(env);
  if (flushing) await flushing;
  return { result: 'stored', push: merged, held: true };
}

// Clock and KV lag allowed between the webhook stamping an event and a read that started after it.
export const READ_SLACK = 2 * 60 * 1000;

/**
 * The events after a GitHub read: what GitHub lists, plus stored events it did not list that are still live.
 * After a complete read (gap null) with its start time (readStart), a stored event GitHub did not list stays only
 * when the webhook stored it at or after the read started, less READ_SLACK: it came in while the read ran. Any
 * other unlisted event is gone (a delete whose webhook was missed: a 503, downtime, the push write budget), and an
 * event with no stamp (an older record) counts as old. What GitHub lists always comes from the read, so this can
 * never drop a live answer. After a read of a long issue that skipped pages (gap: ids above after and below
 * before), the stored events newer than the newest comment read and those in the gap stay, as before.
 */
export function mergeEvents(fromGitHub, kept, newest, gap = null, readStart = null) {
  const above = (id, n) => id.length > String(n).length || (id.length === String(n).length && id > String(n));
  if (gap === null && readStart !== null) {
    const listed = new Set(fromGitHub.map(e => e.id));
    const during = (kept ?? []).filter(e => !listed.has(e.id) && Number.isSafeInteger(e.storedAt) && e.storedAt >= readStart - READ_SLACK);
    return checkEvents([...fromGitHub, ...during]);
  }
  const inGap = id => gap !== null && above(id, gap.after) && !above(id, gap.before - 1);
  const later = (kept ?? []).filter(e => newest === 0 || above(e.id, newest) || inGap(e.id));
  return checkEvents([...fromGitHub, ...later]);
}

/**
 * { items, status, full } for one report with push on: what its record holds while the sync marker is there,
 * else one GitHub read, stored only when it changed something. A failed read serves what the record has
 * and tries again after SYNC_RETRY (a token error after SYNC_INTERVAL: the webhook needs no token, so the
 * tools keep their short next). full says the items are every answer the worker knows for the report, so
 * the tool may set aside one it holds that is not there: only for a GitHub read made by this very request and
 * stored. Never from the record or the marker: another copy may have served an answer it read but could not
 * store (a failed write, or the push write cap), so the record can lack an answer a tool holds, and the marker
 * lives in one copy's memory. A deleted answer is set aside at the next read instead.
 */
async function pushedAnswers(env, fetcher, cache, readCap, code, record, now = Date.now()) {
  const state = pushState(record);
  const marker = `sync/${record.repo}/${record.issue}/${code}`;
  const synced = await cache.get(marker, now);
  if (synced) return served(state?.events ?? [], state?.labels ?? []);
  const readStart = Date.now();
  const read = await readGitHub(env, fetcher, readCap, code, record);
  if (read.fail) {
    await cache.put(marker, { at: now }, now, read.token ? SYNC_INTERVAL : SYNC_RETRY);
    return served(state?.events ?? [], state?.labels ?? null);
  }
  // After a complete read a tombstone stays only while GitHub still lists that comment as an answer no newer
  // than the change the webhook saw (GitHub's list can lag behind); its answer stays out. The others are
  // confirmed and go. A read that skipped pages confirms nothing it did not read: its tombstones stay, unless
  // the read has that comment as an answer newer than the tombstone. Accepted limit: on a complete read of several
  // pages, a tombstone whose comment shifted onto a page already read (a comment deleted on an earlier page between
  // two requests) is taken as confirmed and dropped; the comment's deliveries are older than it, and the next read
  // settles it.
  const gone = read.complete
    ? (state?.gone ?? []).filter(g => read.events.some(e => e.id === g.id && !(e.at && e.at > g.at)))
    : (state?.gone ?? []).filter(g => !read.events.some(e => e.id === g.id && e.at && e.at > g.at));
  const goneIds = new Set(gone.map(g => g.id));
  // A listed event the record already holds keeps its stamp, so a read that finds nothing new writes nothing.
  const listed = read.events.filter(e => !goneIds.has(e.id)).map(e => {
    const kept = state?.events.find(x => x.id === e.id);
    return kept?.storedAt && sameEvent(kept, e) ? { ...e, storedAt: kept.storedAt } : e;
  });
  // Only a read of one page drops a stored event it did not list. On several pages, a comment deleted on an earlier
  // page between two page requests moves the next one onto a page already read, so an unlisted event may be live:
  // it stays. Such a read is never a full list either (ok below): the moved answer may be one KV never held (a
  // fresh read served it but could not store it).
  const listedIds = new Set(listed.map(e => e.id));
  const unlisted = (state?.events ?? []).filter(e => !listedIds.has(e.id));
  const events = !read.complete
    ? mergeEvents(listed, state?.events, read.newest, read.gap)
    : read.pages <= 1
      ? mergeEvents(listed, state?.events, read.newest, null, readStart)
      : checkEvents([...listed, ...unlisted]);
  const labels = read.labels === null ? (state?.labels ?? null) : statusLabels(read.labels);
  // synced only after a complete read with the labels (a read that could not get them is tried again soon), and
  // only on a state that holds something: no state at all already means no answers, so an empty read writes nothing.
  const reconciled = read.complete && read.labels !== null && (events.length > 0 || gone.length > 0 || (labels ?? []).length > 0 || state !== null);
  const stored = await storePush(env, code, record, { labels: labels ?? [], events, gone, synced: reconciled }, now);
  // full only for a complete read of one page with its labels that KV now holds (stored, or already the same), as
  // without push. The marker carries nothing; its wait is a full interval when KV holds the read (a read that
  // skipped pages, or one of several pages where a page may have shifted, gets no surer by asking again soon) or
  // the store was skipped for the day's push write budget, else SYNC_RETRY.
  const ok = stored.held && read.labels !== null && read.complete && read.pages <= 1;
  const wait = stored.result === 'skipped' || (stored.held && read.labels !== null) ? SYNC_INTERVAL : SYNC_RETRY;
  await cache.put(marker, { at: now }, now, wait);
  return { ...served(stored.push.events, labels === null ? null : stored.push.labels), full: ok };
}

const hexOf = bytes => [...bytes].map(b => b.toString(16).padStart(2, '0')).join('');

// One entry of the answer: the code, the items and the status, with fixedIn only when a version is known.
// full (v1.3.5): items are every answer the worker knows for the report, so the tool may set aside one it
// holds that is not in them (Coodsz deleted it). Only sent when the worker really knows.
const entry = (code, items, status, full = false) => ({
  code, items, status: status?.status ?? STATUS_LOOKING, ...(status?.fixedIn ? { fixedIn: status.fixedIn } : {}), ...(full ? { full: true } : {}),
});
// A miss looks like a report with no answer yet: items [] and status looking.
const nothing = code => entry(code, [], null);

/**
 * POST /replies. fetcher is the fetch used for GitHub; env.REPLY_CACHE and env.REPLY_READ_CAP replace
 * the cache and the read cap in tests.
 */
export async function handleRepliesPost(request, env, fetcher) {
  const address = request.headers.get('CF-Connecting-IP') ?? 'unknown';
  if (!(await underAddressLimit(env, 'replies:' + address)))
    return json(429, { ok: false, error: 'too many checks, try again later', next: NEXT_AFTER_LIMIT });

  const length = parseInt(request.headers.get('content-length') ?? '0', 10);
  if (length > MAX_REPLIES_BODY) return json(400, { ok: false, error: 'too large' });
  const text = await request.text();
  if (new TextEncoder().encode(text).length > MAX_REPLIES_BODY) return json(400, { ok: false, error: 'too large' });
  let parsed;
  try { parsed = JSON.parse(text); } catch { return json(400, { ok: false, error: 'not JSON' }); }
  const reports = validateReplyCheck(parsed);
  if (typeof reports === 'string') return json(400, { ok: false, error: 'invalid ' + reports });
  // Each report past the first is one more KV read: the brake counts those too (the request itself is counted in index.js).
  const counter = env.REQUEST_COUNTER ?? defaultCounter;
  if (reports.length > 1) {
    const flushing = counter.note(env, reports.length - 1);
    if (flushing) await flushing;
  }

  // Per key, so one leaked key cannot be used to hammer GitHub through the worker.
  const keyIds = [];
  for (const r of reports) {
    const digest = new Uint8Array(await crypto.subtle.digest('SHA-256', new TextEncoder().encode(r.key)));
    const prefix = hexOf(digest.slice(0, 8));
    keyIds.push(prefix);
    if (!(await underAddressLimit(env, 'replykey:' + prefix)))
      return json(429, { ok: false, error: 'too many checks, try again later', next: NEXT_AFTER_LIMIT });
  }

  const empty = next => json(200, { ok: true, next, replies: reports.map(r => nothing(r.code)) });
  const id = String(env.COODSZ_GITHUB_ID ?? '').trim();
  if (!env.VOTES || !/^\d{1,15}$/.test(id) || !(env.REPLY_SALT || env.VOTER_SALT)) {
    console.warn('replies are not configured (VOTES, COODSZ_GITHUB_ID and REPLY_SALT or VOTER_SALT)');
    return empty(NEXT_AFTER_TOKEN_ERROR);
  }

  const sign = await replySigner(env);
  const cache = env.REPLY_CACHE ?? defaultCache;
  const readCap = env.REPLY_READ_CAP ?? defaultReadCap;
  const push = pushOn(env);
  // A tool that follows the push schedule says "fast": true and may ask again after PUSH_NEXT. Older
  // tools keep the 30 minutes they had, so they ask no more often than before.
  const fast = push && parsed.fast === true;
  let next = fast ? PUSH_NEXT : NEXT_CHECK;
  const replies = [];
  for (const [i, r] of reports.entries()) {
    // openReport reads and hashes the same way for a missing report, so every miss costs the same.
    const record = await openReport(env, sign, r.code, r.key);
    if (!record) {
      replies.push(nothing(r.code));
      continue;
    }
    if (push) {
      // No per key memory here: it would hold back an answer the webhook just stored.
      const answer = await pushedAnswers(env, fetcher, cache, readCap, r.code, record);
      replies.push(entry(r.code, answer.items, answer.status, answer.full));
      continue;
    }
    // The same key and code asked again within KEY_INTERVAL gets the answer it got last time.
    const recentKey = `key/${keyIds[i]}/${r.code}`;
    const recent = await cache.get(recentKey);
    if (Array.isArray(recent?.items)) {
      replies.push(entry(r.code, recent.items, recent.status, false));
      continue;
    }
    const answer = await readAnswers(env, fetcher, cache, readCap, r.code, record);
    if (!answer.skipped && answer.next === NEXT_CHECK)
      await cache.put(recentKey, { items: answer.items, status: answer.status }, Date.now(), KEY_INTERVAL);
    next = Math.max(next, answer.next);
    replies.push(entry(r.code, answer.items, answer.status, answer.full === true));
  }
  // The free plan brake (budget.js): a busy day slows every tool down, the fast phase and older tools too.
  next = Math.max(next, brakeFor(await counter.count(env)));
  return json(200, { ok: true, next, replies });
}
