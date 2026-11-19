// Coodsz crash reports: the endpoint behind Send crash report, Send server crash report and Send
// my logs in the Coodsz tools (CrashReports.Endpoint in src/BannerlordCoopLauncher.Core/Support/CrashReports.cs).
//
// POST /crash takes a multipart form with two parts: "summary" (a small JSON object, checked field
// by field like the mod reports) and "zip" (the cleaned logs the tool packed, at most MAX_CRASH_ZIP).
// The zip is kept in the VOTES KV namespace under crash:<code> for 30 days, and a GitHub issue with
// the summary, the file list and the player's own note (never the log text, only the one crash line) is filed in the
// PRIVATE repository CRASH_REPO. The player gets back a short code like CR-7KQ2M to post in Discord.
//
// Auto triage: newer tools match crash-signatures.json before sending and put the match in
// summary.triage. The issue then gets known or unknown plus the category label; the issue body says
// the known cause and holds the evidence lines, and not-a-crash is closed at once. A known bug
// with a tracking issue (an open issue labeled tracking) gets a comment there instead of a new
// issue. The same reporter and the same signature within 10 minutes gets the earlier code back.
//
// GET /crash/<code> returns that zip, only with the X-Admin-Key header equal to the ADMIN_KEY secret.
//
// Privacy: the tools scrub the Windows user and PC names, Steam ids, addresses and passwords before
// packing (LogZipBuilder.Clean). In game player names can still be in the logs.
// The worker keeps only an HMAC of the install id (the same reporter hash as the mod reports), and
// the caller's address only keys the in memory rate limiter. Newer tools send the player's Discord
// name (summary.contact) so Coodsz can reach them; it shows in the issue, cleaned like player text.
//
// In tool replies (reply-keys.js, replies.js): newer tools send a per report summary.replyKey. Once the
// issue is filed the worker keeps only a salted HMAC of it in rr:<code> and answers "replies": true; the
// tool then asks POST /replies for Coodsz's /reply comments. Older tools send no key and get "replies": false.
// A summary.followUp (followup.js, No, still broken in the tool) carries the earlier report's code and key:
// the zip is kept under a new code as usual, and the earlier report's issue gets a comment instead of a new issue.
//
// The Discord ping (pingCoodsz): an unknown report or a known Coop bug posts one short line to the
// CRASH_PING_WEBHOOK secret after the answer to the tool is decided. No secret, no ping.

import { repliesOn, parseReplyKey, storeReplyRecord, addReplyKey, openReport, replySigner } from './reply-keys.js';
import { validateFollowUp, fileFollowUp } from './followup.js';
import { IPV4, isVersionQuad } from './reply-text.js';

export const MAX_CRASH_ZIP = 8 * 1024 * 1024;
export const MAX_CRASH_BODY = MAX_CRASH_ZIP + 64 * 1024;
export const MAX_SUMMARY = 32 * 1024;
export const MAX_FILES = 500;
export const MAX_CRASH_LINE = 300;
export const CRASH_TTL = 30 * 24 * 3600;
export const SIDES = ['client', 'server'];
// crash: the game crashed. server-crash: the server crashed or stopped by itself. logs: Send my logs.
export const REASONS = ['crash', 'server-crash', 'logs'];
// What went wrong, for Send my logs.
export const PROBLEMS = ['join', 'desync', 'lag', 'other'];
// No 0, O, 1, I or L, so a code read aloud or typed from a screenshot is never ambiguous.
export const CODE_ALPHABET = '23456789ABCDEFGHJKMNPQRSTUVWXYZ';
export const CODE = /^CR-[23456789ABCDEFGHJKMNPQRSTUVWXYZ]{5}$/;
// The tools that send crash reports, each with its own private repository (crashRepo).
export const CRASH_TOOLS = ['coop', 'taom', 'ad1259', 'be', 'rot'];

const ID = /^[\p{L}\p{N}_.\-' ()+&!]{1,100}$/u;
const VERSION = /^[\p{L}\p{N}_.\-+ ]{0,40}$/u;
const INSTALL = /^[0-9a-f]{32}$/;
const STEAM_ID = /7656119\d{10}/;
// A file name in the zip: plain names and folders, no drive, no backslash, no .. part.
const FILE = /^[\p{L}\p{N}_.\-+ ()[\]/]{1,200}$/u;

const json = (status, body) => new Response(JSON.stringify(body), {
  status,
  headers: { 'content-type': 'application/json; charset=utf-8', 'cache-control': 'no-store' },
});

// The Discord name newer tools send so Coodsz can reach the player. Older tools send none.
export const MAX_CONTACT = 40;
// Only letters, marks, numbers, spaces and a few name characters stay in it. Everything else is dropped.
const CONTACT_DROP = /[^\p{L}\p{M}\p{N} _.\-'+?]/gu;

/**
 * The checked Discord name: { contact: null } when absent or empty, { contact: name } with control and
 * format characters and anything odd taken out, or { error } when it is not a string or longer than
 * MAX_CONTACT after trim.
 */
export function validateContact(value) {
  if (value === undefined || value === null) return { contact: null };
  if (typeof value !== 'string' || value.length > 400) return { error: 'contact' };
  const trimmed = value.replace(/[\u0000-\u001f\u007f-\u009f]+/g, ' ').replace(/\p{Cf}+/gu, '').replace(/\s+/g, ' ').trim();
  if (trimmed.length > MAX_CONTACT) return { error: 'contact' };
  const name = trimmed.replace(CONTACT_DROP, '').replace(/\s+/g, ' ').trim();
  return { contact: name.length > 0 ? name : null };
}

/** The Discord name as it goes into the issue: cleaned like the crash line, no markdown, links or refs. */
export function contactText(name) {
  return defang(noMarkdown(cleanCrashLine(name, MAX_CONTACT)));
}

const isVersion = v => typeof v === 'string' && VERSION.test(v) && !STEAM_ID.test(v);

// The address cleaner (IPV4, isVersionQuad) is shared with the reply cleaner in reply-text.js.

/** The crash line as it may go into the issue title and body: one line, cleaned again, at most 300 characters (or max). */
export function cleanCrashLine(text, max = MAX_CRASH_LINE) {
  if (typeof text !== 'string') return '';
  let line = text.replace(/[\u0000-\u001f\u007f]+/g, ' ').replace(/\p{Cf}+/gu, '');
  line = line.replace(IPV4, (quad, a, b, c, d, at, all) => isVersionQuad(all, at, quad, [a, b, c, d]) ? quad : '[address]');
  line = line.replace(/\b7656\d{10,}\b/g, '[steam id]');
  line = line.replace(/\[U:\d:\d+\]/gi, '[steam id]');
  line = line.replace(/(Users[\\/]+)[^\\/\s"']+/gi, '$1[user]');
  line = line.replace(/((?:password|token)\w*\s*[:=]\s*)\S+/gi, '$1[password]');
  line = line.replace(/[`<>@]/g, "'").replace(/\s+/g, ' ').trim();
  return line.length > max ? line.slice(0, max - 3) + '...' : line;
}

// Crash auto triage (newer tools): the tool runs crash-signatures.json over the logs before
// sending and puts the match in summary.triage (and in report.json inside the zip).
export const CATEGORIES = ['not-a-crash', 'setup', 'mod-assets', 'mod-missing-patch', 'coop-bug', 'silent-exit', 'mod-mismatch'];
export const MAX_EVIDENCE = 10;
export const DEDUPE_TTL = 10 * 60;
// Only an open issue Coodsz labeled this way takes tracked reports as comments.
export const TRACKING_LABEL = 'tracking';
const SIGNATURE = /^[a-z0-9][a-z0-9-]{0,63}$/;
const LABEL = /^[a-z0-9][a-z0-9-]{0,29}$/;
const SIGNATURES_VERSION = /^[A-Za-z0-9_.-]{0,40}$/;
// The only labels a report may add: the ones crash-signatures.json uses. Control labels like tracking, new or the
// categories stay the worker's own. A label that isn't here is dropped, so a newer signatures file never breaks a send.
export const SIGNATURE_LABELS = new Set(['siege', 'assets', 'coopmodpatch', 'diplomacy', 'join', 'exit-code-1', 'taom-startup-check', 'gpu']);

/** Text from a report that ends up as markdown outside a code fence: no links, images or headings. */
export function noMarkdown(text) {
  return text.replace(/[[\]()!#*_|~]/g, ch => '\\' + ch);
}

/**
 * The checked triage, null when the tool sent none (older tools, and Send my logs from tools before v1.3.5), or a string
 * that says what is wrong. An unknown crash keeps only the signatures version. Free text is
 * cleaned like the crash line.
 */
export function validateTriage(t) {
  if (t === undefined || t === null) return null;
  if (typeof t !== 'object' || Array.isArray(t)) return 'triage';
  const version = t.signaturesVersion ?? '';
  if (typeof version !== 'string' || !SIGNATURES_VERSION.test(version)) return 'triage version';
  const signature = t.signature ?? null;
  if (signature === null)
    return { signature: null, category: null, signaturesVersion: version, title: '', whatHappened: '', whatToDo: '', reportNeeded: null, trackingIssue: null, labels: [], evidence: [] };
  if (typeof signature !== 'string' || !SIGNATURE.test(signature)) return 'triage signature';
  if (!CATEGORIES.includes(t.category)) return 'triage category';
  for (const key of ['title', 'whatHappened', 'whatToDo'])
    if (t[key] !== undefined && t[key] !== null && (typeof t[key] !== 'string' || t[key].length > 2000)) return 'triage ' + key;
  if (t.reportNeeded !== undefined && t.reportNeeded !== null && typeof t.reportNeeded !== 'boolean') return 'triage reportNeeded';
  const tracking = t.trackingIssue ?? null;
  if (tracking !== null && (!Number.isInteger(tracking) || tracking < 1 || tracking > 999999)) return 'triage trackingIssue';
  const labels = t.labels ?? [];
  if (!Array.isArray(labels) || labels.length > 3 || labels.some(l => typeof l !== 'string' || !LABEL.test(l))) return 'triage labels';
  const evidence = t.evidence ?? [];
  if (!Array.isArray(evidence) || evidence.length > MAX_EVIDENCE || evidence.some(l => typeof l !== 'string' || l.length > 2000)) return 'triage evidence';
  return {
    signature, category: t.category, signaturesVersion: version,
    title: noMarkdown(cleanCrashLine(t.title ?? '', 200)), whatHappened: noMarkdown(cleanCrashLine(t.whatHappened ?? '', 1000)),
    // Optional: tools that send it get the player's own advice in the issue too.
    whatToDo: noMarkdown(cleanCrashLine(t.whatToDo ?? '', 1000)),
    reportNeeded: t.reportNeeded ?? null, trackingIssue: tracking,
    labels: [...new Set(labels)].filter(l => SIGNATURE_LABELS.has(l)),
    evidence: evidence.map(l => cleanCrashLine(l)).filter(l => l.length > 0),
  };
}

/** The checked summary, or a string that says what is wrong. Only known fields are kept. */
export function validateSummary(body) {
  if (!body || typeof body !== 'object' || Array.isArray(body)) return 'not an object';
  if (body.kind !== 'CoodszCrashReport' || body.schema !== 1) return 'unknown kind or schema';
  if (!INSTALL.test(body.install ?? '')) return 'install';
  if (!CRASH_TOOLS.includes(body.tool)) return 'tool';
  if (!SIDES.includes(body.side)) return 'side';
  const reason = body.reason ?? 'crash';
  if (!REASONS.includes(reason)) return 'reason';
  if (body.problem !== undefined && body.problem !== null && !PROBLEMS.includes(body.problem)) return 'problem';
  for (const key of ['toolVersion', 'coop', 'game'])
    if (body[key] !== undefined && body[key] !== null && !isVersion(body[key])) return key;
  if (body.crashLine !== undefined && body.crashLine !== null && (typeof body.crashLine !== 'string' || body.crashLine.length > 2000)) return 'crashLine';
  const mod = body.suspectedMod;
  if (mod !== undefined && mod !== null && (typeof mod !== 'string' || !ID.test(mod) || STEAM_ID.test(mod))) return 'suspectedMod';
  if (!Array.isArray(body.files) || body.files.length > MAX_FILES) return 'files';
  const files = [];
  for (const f of body.files) {
    if (!f || typeof f.name !== 'string' || !FILE.test(f.name) || f.name.startsWith('/') || f.name.split('/').includes('..') || STEAM_ID.test(f.name)) return 'file name';
    if (!Number.isInteger(f.size) || f.size < 0 || f.size > 2_000_000_000) return 'file size';
    files.push({ name: f.name, size: f.size });
  }
  const triage = validateTriage(body.triage);
  if (typeof triage === 'string') return triage;
  const contact = validateContact(body.contact);
  if (contact.error) return contact.error;
  return {
    install: body.install, tool: body.tool, side: body.side, reason, problem: body.problem ?? null,
    toolVersion: body.toolVersion ?? '', coop: body.coop ?? '', game: body.game ?? '',
    crashLine: cleanCrashLine(body.crashLine ?? ''), suspectedMod: mod ?? null, files, triage,
    contact: contact.contact,
  };
}

/** True for the start of a zip file (a local file header, or an empty zip's end record). */
export function isZip(bytes) {
  return bytes.length >= 4 && bytes[0] === 0x50 && bytes[1] === 0x4b
    && ((bytes[2] === 0x03 && bytes[3] === 0x04) || (bytes[2] === 0x05 && bytes[3] === 0x06));
}

/** A new random code, CR- and five characters of CODE_ALPHABET. random fills a Uint8Array (tests pass their own). */
export function newCode(random = b => crypto.getRandomValues(b)) {
  let code = 'CR-';
  while (code.length < 8) {
    const bytes = random(new Uint8Array(8));
    for (const b of bytes) {
      // 248 is 31 * 8: bytes above it are dropped, so every character is equally likely.
      if (b < 248 && code.length < 8) code += CODE_ALPHABET[b % CODE_ALPHABET.length];
    }
  }
  return code;
}

/** The code as typed (any case, spaces around it), or null when it is not one. */
export function parseCode(text) {
  let code;
  // A malformed escape like %E0%A4%A makes decodeURIComponent throw: that is not a code either.
  try { code = decodeURIComponent(text ?? '').trim().toUpperCase(); } catch { return null; }
  return CODE.test(code) ? code : null;
}

async function digest(text) {
  return new Uint8Array(await crypto.subtle.digest('SHA-256', new TextEncoder().encode(text)));
}

/** Compares two secrets in constant time: both are hashed first, so the length does not leak either. */
export async function sameSecret(given, expected) {
  if (typeof given !== 'string' || typeof expected !== 'string' || expected.length === 0) return false;
  const [a, b] = await Promise.all([digest(given), digest(expected)]);
  let diff = 0;
  for (let i = 0; i < a.length; i++) diff |= a[i] ^ b[i];
  return diff === 0;
}

const TOOL_NAME = { coop: 'Coop and Friends', taom: 'TAOM', ad1259: 'AD1259', be: 'Burning Empires', rot: 'Realm of Thrones' };

/** Each tool family files into its own private repository, so a repo can be shared with that game's people only. */
export function crashRepo(env, tool) {
  if (tool === 'taom') return env.TAOM_CRASH_REPO || 'Coodsz/Coodsz-TAOM-Crash-Reports';
  if (tool === 'ad1259') return env.AD1259_CRASH_REPO || 'Coodsz/Coodsz-AD1259-Crash-Reports';
  if (tool === 'be') return env.BE_CRASH_REPO || 'Coodsz/Coodsz-BurningEmpires-Crash-Reports';
  if (tool === 'rot') return env.ROT_CRASH_REPO || 'Coodsz/Coodsz-RealmOfThrones-Crash-Reports';
  return env.CRASH_REPO || 'Coodsz/Coodsz-Crash-Reports';
}
const REASON_TEXT = { crash: 'The game crashed', 'server-crash': 'The server crashed or stopped by itself', logs: 'Logs sent, no crash' };
const PROBLEM_TEXT = { join: 'Stuck joining', desync: 'Desync', lag: 'Lag', other: 'Something else' };
export const MAX_TITLE_WHAT = 70;
export const MAX_TITLE_SHORT = 60;

// A few .NET errors in plain words, for an unknown crash line. The first that appears in the line wins.
const PLAIN_ERRORS = [
  [/NullReferenceException/, 'something the code expected was missing (a null reference)'],
  [/KeyNotFoundException/, 'the code looked up something that was not there'],
  [/(?:IndexOutOfRange|ArgumentOutOfRange)Exception/, 'the code read past the end of a list'],
  [/InvalidCastException/, 'the code got a different kind of object than it expected'],
  [/(?:MissingMethod|MissingField|TypeLoad)Exception/, 'a mod was built for another version of the game or of another mod'],
  [/(?:FileNotFound|DirectoryNotFound|FileLoad)Exception/, 'a file the game or a mod needs could not be found or loaded'],
  [/InvalidOperationException/, 'the code did something at a moment it was not allowed to'],
  [/StackOverflowException/, 'the code called itself over and over until it ran out of room'],
  [/OutOfMemoryException/, 'the game ran out of memory'],
  [/AccessViolationException/, 'the game read memory it was not allowed to (often a driver or native mod problem)'],
];

/** Text that was escaped by noMarkdown, back to plain text, for the issue title (GitHub shows titles as plain text). */
const unescape = text => text.replace(/\\([[\]()!#*_|~])/g, '$1');

const ZWSP = '​';

/**
 * Breaks what GitHub turns into a link or a reference even without markdown: bare URLs (https://x,
 * www.x), issue refs (#12, Owner/Repo#12, GH-12). A zero width space keeps the text readable.
 */
export function defang(text) {
  return text
    .replace(/:\/\//g, ':' + ZWSP + '//')
    .replace(/\bwww\./gi, m => m.slice(0, 3) + ZWSP + '.')
    .replace(/#(?=\d)/g, '#' + ZWSP)
    .replace(/\b(GH)-(?=\d)/gi, '$1-' + ZWSP);
}

/** A line of prose that starts like a list, a quote, a heading underline or a rule gets that first character escaped. */
export function escapeLineStart(line) {
  if (/^\s*\d+[.)](\s|$)/.test(line)) return line.replace(/^(\s*\d+)([.)])/, '$1\\$2');
  return /^\s*(?:[-+>=]|\\?[*_]\\?[*_]\\?[*_])/.test(line) ? line.replace(/^(\s*)/, '$1\\') : line;
}

/** Text from a report (already cleaned and run through noMarkdown) as safe prose, line by line. */
export function prose(text) {
  return text.split('\n').map(line => escapeLineStart(defang(line))).join('\n');
}

/** Text for the issue title: no brackets, exclamation marks or backticks, so nothing in it can look like a link or image. */
const titleSafe = text => defang(unescape(text).replace(/[[\]!`]/g, ' ').replace(/\s+/g, ' ').trim());

/** The first meaningful words of a crash line: no leading time stamp or log level, the exception without its namespace. */
export function crashGist(line, max = MAX_TITLE_WHAT) {
  let gist = (line || '').replace(/^(?:\s*(?:\[[^\]]*\]|\d{1,2}:\d{2}(?::\d{2}(?:\.\d+)?)?|(?:ERROR|ERR|WARN(?:ING)?|WRN|FATAL|FTL|INFO|INF|DEBUG|DBG)(?=[\s:|-]))\s*[:\-|]?\s*)+/, '');
  gist = gist.replace(/^(?:[A-Za-z_]\w*\.)+(\w+(?:Exception|Error))\b/, '$1').trim();
  return gist.length > max ? gist.slice(0, max - 3).trimEnd() + '...' : gist;
}

// .NET's own stock messages, which add nothing to the error's name.
const GENERIC_MESSAGE = /^(?:Object reference not set|Index was outside the bounds|The given key|Specified argument was out of the range|Exception of type)/;

/**
 * The few words of a crash line a person needs in a list: the error and where it happened, like
 * "IndexOutOfRange in TroopRoster.AddToCountsAtIndex". The DedicatedServer host's 'DS'{json} wrapper
 * and its "threw before Coop robustness suppression" lead are dropped, so is Harmony's _PatchN.
 */
export function crashShort(line, max = MAX_TITLE_SHORT) {
  let text = line || '';
  const detail = /"detail"\s*:\s*"((?:[^"\\]|\\.)*)/.exec(text);
  if (detail) text = detail[1].replace(/\\r|\\n|\\t/g, ' ').replace(/\\(.)/g, '$1');
  text = text.replace(/^.*?threw before Coop robustness suppression:\s*/, '');
  const error = /(?:^|[\s:(])(?:[A-Za-z_]\w*\.)*([A-Za-z_]\w*?)(Exception|Error)\b/.exec(text);
  const frame = /\bat\s+(?:[\w`]+\.)*?([\w`]+)\.(\.?[\w`]+?)(?:_Patch\d+)?\s*\(/.exec(text);
  let short;
  if (error && frame) short = `${error[1]} in ${frame[1]}${frame[2] === '.ctor' ? ' constructor' : '.' + frame[2]}`;
  else if (error) {
    const message = text.slice(error.index + error[0].length).replace(/^[\s:]+/, '').replace(/\s+at\s[\s\S]*$/, '').trim();
    short = message && !GENERIC_MESSAGE.test(message) ? `${error[1]}, ${message}` : error[1] + error[2];
  } else short = crashGist(text.replace(/^Note:?\s+/i, '').split(/(?<=\.)\s/)[0], max); // a host Note: its first sentence
  short = short.replace(/\s+/g, ' ').trim();
  return short.length > max ? short.slice(0, max - 3).trimEnd() + '...' : short;
}

// "The game closed normally" reads as "the game closed normally" after the comma; "TAOM.CoopCompat ..." keeps its capital.
const lowerFirst = text => /^[A-Z][a-z]/.test(text) ? text[0].toLowerCase() + text.slice(1) : text;

/**
 * The issue title, short enough to read in the issue list: the tool, game or server, crash or logs,
 * then the known cause or the error in a few words, and the code at the end so a search by code finds it.
 * "TAOM server crash, IndexOutOfRange in TroopRoster.AddToCountsAtIndex [CR-PWW5Q]"
 */
export function crashTitle(code, summary) {
  const t = summary.triage;
  const kind = summary.reason === 'logs' ? ' logs' : t?.signature && t.category === 'not-a-crash' ? ', no crash' : ' crash';
  const lead = `${TOOL_NAME[summary.tool]} ${summary.side === 'server' ? 'server' : 'game'}${kind}`;
  let what;
  if (t?.signature && t.title) what = lowerFirst(titleSafe(t.title));
  else if (summary.crashLine) what = titleSafe(crashShort(summary.crashLine));
  else if (summary.reason === 'logs') what = summary.problem ? lowerFirst(PROBLEM_TEXT[summary.problem]) : '';
  else what = 'no error line in the logs';
  if (what.length > MAX_TITLE_WHAT) what = what.slice(0, MAX_TITLE_WHAT - 3).trimEnd() + '...';
  return `${lead}${what ? ', ' + what : ''} [${code}]`;
}

/** The issue labels. A triaged report adds known or unknown, and for a known one its category and the signature's own labels. */
export function crashLabels(summary) {
  const labels = ['crash-report', summary.side, summary.tool, 'new'];
  const t = summary.triage;
  if (!t) return labels;
  if (!t.signature) return [...labels, 'unknown'];
  return [...new Set([...labels, 'known', t.category, ...t.labels.filter(l => SIGNATURE_LABELS.has(l))])];
}

/** The triage line of the technical details, or null for a report without triage. */
export function triageRow(t) {
  if (!t) return null;
  const version = t.signaturesVersion ? `, signatures ${t.signaturesVersion}` : '';
  return t.signature ? `Triage: known, ${t.signature} (${t.category})${version}` : `Triage: unknown${version}`;
}

/** High for a crash nobody knows yet, Average for a known cause that still needs a report, Low for one that needs none. */
export function crashSeverity(summary) {
  const t = summary.triage;
  // Send my logs that matched a setup hint: the player saw the fix and sent the logs anyway, so it is never Low.
  if (summary.reason === 'logs' && t?.signature) return { level: 'Average', why: 'Logs sent after the tool showed a known fix, it may not have helped.' };
  if (t?.signature) {
    if (t.category === 'not-a-crash') return { level: 'Low', why: 'The tool says this was not a crash.' };
    if (t.reportNeeded === false) return { level: 'Low', why: 'A known cause the player can fix, no report needed.' };
    if (t.category === 'coop-bug') return { level: 'Average', why: 'A known Coop bug, this report adds to it.' };
    return { level: 'Average', why: 'A known cause that still wants a report.' };
  }
  if (summary.reason === 'logs') return { level: 'Average', why: 'Logs sent for a problem, no crash.' };
  return { level: 'High', why: t ? 'An unknown crash, no known cause matched.' : 'A crash from a tool without auto triage, nobody has looked yet.' };
}

/** The What happened section: the triage text, or the crash line in plain words. */
export function whatHappened(summary) {
  const t = summary.triage;
  if (t?.signature && t.whatHappened) return prose(t.whatHappened);
  const lead = REASON_TEXT[summary.reason] + (summary.reason === 'logs' && summary.problem ? `. The player picked: ${PROBLEM_TEXT[summary.problem]}` : '');
  if (!summary.crashLine) return `${lead}. No crash line was found in the logs.`;
  const plain = PLAIN_ERRORS.find(([pattern]) => pattern.test(summary.crashLine));
  // A code span: cleanCrashLine took every backtick out, so the gist cannot break out of it, and nothing in it links.
  return `${lead}. The last error in the logs was \`${crashGist(summary.crashLine, 200)}\``
    + (plain ? `\n\nIn plain words, ${plain[1]}.` : '');
}

/** The Known cause section. */
export function knownCause(t) {
  if (!t) return 'Not checked, this tool version sends no auto triage. It needs a look.';
  if (!t.signature) return 'Not yet, this one needs a look.';
  const name = t.title || t.signature;
  const kind = t.category === 'not-a-crash' ? 'This was not a crash, so the issue is closed automatically. Reopen it if that is wrong.'
    : t.reportNeeded === false ? 'It needs no report, the player can fix it.'
    : t.category === 'coop-bug' ? 'It is a known Coop bug, this report adds to it.'
    : 'It still wants a report.';
  return `Yes. ${defang(name)}. ${kind}` + (t.whatToDo ? `\n\nWhat the player was told to do: ${defang(t.whatToDo)}` : '');
}

/** The What the player said section: the problem they picked, their note as a quote, and what else they added. */
export function playerSaid(summary) {
  const parts = [];
  if (summary.reason === 'logs' && summary.problem) parts.push(`They picked: ${PROBLEM_TEXT[summary.problem]}.`);
  if (summary.note) parts.push(quoteNote(summary.note));
  // A note the file list names but the worker could not read still shows below as "a note (player-notes.txt)".
  else if (!summary.files.some(f => f.name === NOTES_FILE)) parts.push('No note.');
  const extras = playerExtras(summary.note ? summary.files.filter(f => f.name !== NOTES_FILE) : summary.files);
  if (extras) parts.push(`They added ${extras}, in the zip.`);
  return parts.join('\n\n');
}

// The player's note (player-notes.txt at the root of the zip, deflated by the tool): read by the worker,
// cleaned and quoted in the issue.
export const MAX_NOTE_ZIPPED = 64 * 1024;
export const MAX_NOTE_BYTES = 16 * 1024;
export const MAX_NOTE_CHARS = 1500;

/** The note as it goes in the issue: every line cleaned like the crash line, escaped, defanged and quoted. */
export function quoteNote(note) {
  return note.split('\n').map(line => {
    const clean = cleanCrashLine(line, 2000).replace(/&/g, '&amp;');
    return clean ? '> ' + escapeLineStart(defang(noMarkdown(clean))) : '>';
  }).join('\n');
}

/** The note text from the zip: UTF-8 (bad bytes replaced), trimmed, blank runs folded, at most MAX_NOTE_CHARS. '' when there is none. */
export function noteText(bytes) {
  if (!bytes || bytes.length === 0) return '';
  let text = new TextDecoder('utf-8', { fatal: false }).decode(bytes).replace(/\p{Cf}+/gu, '');
  text = text.replace(/\r\n?/g, '\n').replace(/\n{3,}/g, '\n\n').trim();
  return text.length > MAX_NOTE_CHARS ? text.slice(0, MAX_NOTE_CHARS - 3).toWellFormed().trimEnd() + '...' : text;
}

const u16 = (b, i) => b[i] | (b[i + 1] << 8);
const u32 = (b, i) => (b[i] | (b[i + 1] << 8) | (b[i + 2] << 16) | (b[i + 3] << 24)) >>> 0;

/**
 * One entry of a zip, found through its central directory: stored or deflated, not encrypted, at most
 * maxZipped bytes packed. Gives at most maxBytes of it (the rest is cut), or null for anything odd. Never throws.
 */
export async function readZipEntry(bytes, name, maxZipped = MAX_NOTE_ZIPPED, maxBytes = MAX_NOTE_BYTES) {
  try {
    const n = bytes.length;
    let end = -1;
    for (let i = n - 22; i >= Math.max(0, n - 22 - 65535); i--)
      if (u32(bytes, i) === 0x06054b50) { end = i; break; }
    if (end < 0) return null;
    const count = u16(bytes, end + 10);
    const dirSize = u32(bytes, end + 12);
    let at = u32(bytes, end + 16);
    if (count > MAX_FILES + 10 || at + dirSize > end) return null;
    const wanted = new TextEncoder().encode(name);
    for (let k = 0; k < count; k++) {
      if (at + 46 > end || u32(bytes, at) !== 0x02014b50) return null;
      const flags = u16(bytes, at + 8), method = u16(bytes, at + 10);
      const zipped = u32(bytes, at + 20), size = u32(bytes, at + 24);
      const nameLength = u16(bytes, at + 28), extra = u16(bytes, at + 30), comment = u16(bytes, at + 32);
      const local = u32(bytes, at + 42);
      if (at + 46 + nameLength > end) return null;
      const entryName = bytes.subarray(at + 46, at + 46 + nameLength);
      at += 46 + nameLength + extra + comment;
      if (entryName.length !== wanted.length || entryName.some((b, i) => b !== wanted[i])) continue;
      if ((flags & 1) || zipped > maxZipped || size === 0xffffffff) return null;
      if (local + 30 > n || u32(bytes, local) !== 0x04034b50) return null;
      const start = local + 30 + u16(bytes, local + 26) + u16(bytes, local + 28);
      if (start + zipped > n) return null;
      const data = bytes.subarray(start, start + zipped);
      if (method === 0) return data.slice(0, Math.min(maxBytes, data.length));
      return method === 8 ? await inflate(data, maxBytes) : null;
    }
    return null;
  } catch {
    return null;
  }
}

/** Raw deflate, cut after maxBytes. null when the data is broken. */
async function inflate(data, maxBytes) {
  const reader = new Blob([data]).stream().pipeThrough(new DecompressionStream('deflate-raw')).getReader();
  const chunks = [];
  let total = 0;
  try {
    while (total < maxBytes) {
      const { done, value } = await reader.read();
      if (done) break;
      chunks.push(value);
      total += value.length;
    }
  } catch {
    return null;
  } finally {
    reader.cancel().catch(() => {});
  }
  const out = new Uint8Array(Math.min(total, maxBytes));
  let o = 0;
  for (const c of chunks) {
    if (o >= out.length) break;
    const part = c.subarray(0, out.length - o);
    out.set(part, o);
    o += part.length;
  }
  return out;
}

/** The player's note from the zip, '' when there is none or the zip is odd. */
export async function readPlayerNote(zipBytes) {
  return noteText(await readZipEntry(zipBytes, NOTES_FILE));
}

/** The Player and setup lines. */
function setupLines(summary, reporter) {
  const none = '_none_';
  const modules = summary.files.some(f => f.name === 'modules.json');
  return [
    `- Tool: ${TOOL_NAME[summary.tool]} ${summary.side}${summary.toolVersion ? ' ' + summary.toolVersion : ''}`,
    `- Coop: ${summary.coop || none}`,
    `- Bannerlord: ${summary.game || none}`,
    `- Suspected mod: ${summary.suspectedMod ? noMarkdown(summary.suspectedMod) : none}`,
    `- Mod list: ${modules ? 'modules.json in the zip' : none}`,
    ...(summary.contact ? [`- Contact: ${contactText(summary.contact)} (Discord)`] : []),
    `- Reporter: ${reporter}`,
  ];
}

/** The evidence lines the tool matched, in a code block. */
const evidenceBlock = t => (t?.evidence?.length ? ['Evidence the tool matched:', '', '```', ...t.evidence, '```', ''] : []);

const downloadLine = (code, origin) => `curl.exe -H "X-Admin-Key: <ADMIN_KEY>" -o ${code}.zip ${origin}/crash/${code}`;

/** The comment a tracked report adds to its tracking issue instead of a new issue: the same shape, shorter. */
export function trackedComment(code, summary, reporter, zipBytes, origin, expires, replies = false) {
  const t = summary.triage;
  const severity = crashSeverity(summary);
  return [
    '<!-- coodsz-crash-report v1 tracked -->',
    `**New report ${code}** (${TOOL_NAME[summary.tool]}, ${summary.side})`, '',
    ...(replies ? [`In tool replies: on (comment /reply ${code} to answer the player)`, ''] : []),
    '**What the player said:**', '', playerSaid(summary), '',
    `**Severity:** ${severity.level}. ${severity.why}`, '',
    ...setupLines(summary, reporter), '',
    '<details><summary>Technical details</summary>', '',
    `Code: ${code}`, '',
    ...(summary.crashLine ? ['Crash line:', '', '```', summary.crashLine, '```', ''] : []),
    ...evidenceBlock(t),
    `Zip: ${size(zipBytes)}, kept until ${expires}`, '',
    '```', downloadLine(code, origin), '```', '',
    triageRow(t), '',
    '</details>', '',
  ].join('\n');
}

// The line that tells Coodsz the player's tool can show his /reply (older tools and reports have none).
export const REPLIES_LINE = 'In tool replies: on (comment /reply to answer the player)';

const size = n => (n >= 1024 * 1024 ? (n / 1024 / 1024).toFixed(1) + ' MB' : n >= 1024 ? Math.round(n / 1024) + ' KB' : n + ' B');

// What the player added in the tool (newer tool versions): their own words and screenshots, inside the zip.
export const NOTES_FILE = 'player-notes.txt';
export const SCREENSHOT_FOLDER = 'screenshots/';
// Newer tools can add the crash's memory dump (crash-dumps/...dmp) when the player leaves the box ticked.
// A dump is raw memory and is not cleaned, so the issue says so.
export const DUMP_FOLDER = 'crash-dumps/';

/** True when the zip holds a crash dump. */
export function hasDump(files) {
  return files.some(f => f.name.toLowerCase().endsWith('.dmp'));
}

/** What the player added, from the file list, like "a note (player-notes.txt), 2 screenshots (screenshots/)". '' when nothing was added. */
export function playerExtras(files) {
  const notes = files.some(f => f.name === NOTES_FILE);
  const shots = files.filter(f => f.name.startsWith(SCREENSHOT_FOLDER)).length;
  const parts = [];
  if (notes) parts.push(`a note (${NOTES_FILE})`);
  if (shots) parts.push(`${shots} screenshot${shots === 1 ? '' : 's'} (${SCREENSHOT_FOLDER})`);
  if (hasDump(files)) parts.push(`a crash dump (${DUMP_FOLDER}, raw memory, not cleaned)`);
  return parts.join(' and ');
}

/**
 * The issue text, read top to bottom like a bug report: what happened, the known cause, what the player
 * said, the severity and the setup, with the technical details folded away. No log text beyond the one
 * crash line and the triage evidence. The player's note is quoted, cleaned and escaped.
 */
export function crashBody(code, summary, reporter, zipBytes, origin, expires, replies = false) {
  const t = summary.triage;
  const severity = crashSeverity(summary);
  const files = summary.files.map(f => `${f.name}  (${size(f.size)})`);
  return [
    '<!-- coodsz-crash-report v1 -->',
    `Report code **${code}**`, '',
    ...(replies ? [REPLIES_LINE, ''] : []),
    '## What happened', '', whatHappened(summary), '',
    ...(t?.signature ? ['<!-- coodsz-crash-triage v1 -->'] : []),
    '## Known cause?', '', knownCause(t), '',
    '## What the player said', '', playerSaid(summary), '',
    '## Severity', '', `${severity.level}. ${severity.why}`, '',
    '## Player and setup', '', ...setupLines(summary, reporter), '',
    '<details><summary>Technical details</summary>', '',
    `Code: ${code}`, '',
    'Crash line:', '', ...(summary.crashLine ? ['```', summary.crashLine, '```'] : ['_none_']), '',
    `Files in the zip (${summary.files.length}):`, '', ...(files.length ? ['```', ...files, '```'] : ['_none_']), '',
    `Zip: ${size(zipBytes)}, kept until ${expires}`, '',
    'Download:', '', '```', downloadLine(code, origin), '```', '',
    ...evidenceBlock(t),
    ...(triageRow(t) ? [triageRow(t), ''] : []),
    '</details>', '',
    '---',
    'Sent from the Coodsz tools after the player saw this file list and clicked Send. The Windows user and PC names, Steam ids, addresses and passwords were taken out of the logs before packing. In game player names can still be in them.'
      + (hasDump(summary.files) ? ' This report includes a crash dump (raw memory, not cleaned): it can hold the Windows user name, Steam id, the join password and addresses.' : ''),
    '',
  ].join('\n');
}

const day = () => new Date().toISOString().slice(0, 10);

/** Drops a stored zip nobody can reach and gives its day counts back. Best effort, never throws. */
export async function forget(kv, code, counts) {
  try {
    await kv.delete?.('crash:' + code);
    for (const key of counts) {
      const now = parseInt((await kv.get(key)) ?? '0', 10) || 0;
      if (now > 0) await kv.put(key, String(now - 1), { expirationTtl: 2 * 24 * 3600 });
    }
  } catch (err) {
    console.warn(`the unused zip could not be dropped: ${err?.message ?? err}`);
  }
}

/**
 * Adds one to a day counter in KV; false when that passes the limit. A KV read or write that fails
 * (KV allows about one write a second per key) lets the report through and logs it, instead of
 * throwing a 500. The address limiter still guards the endpoint.
 */
export async function bump(kv, key, limit, by = 1) {
  try {
    const now = parseInt((await kv.get(key)) ?? '0', 10) || 0;
    if (now + by > limit) return false;
    await kv.put(key, String(now + by), { expirationTtl: 2 * 24 * 3600 });
  } catch (err) {
    // On purpose: a broken counter lets the report through, losing a player's report is worse than a few extra.
    console.warn(`counter ${key.split(':')[0]} could not be updated: ${err?.message ?? err}`);
  }
  return true;
}

/**
 * The in memory address limiter. Without the REPORT_LIMITER binding, or when it fails, the request
 * goes on and a warning is logged, so a missing binding is seen in the worker logs.
 */
export async function underAddressLimit(env, key) {
  if (!env.REPORT_LIMITER) {
    // On purpose: a missing or failing limiter lets the request through (and logs it), never a 500.
    console.warn('REPORT_LIMITER is not bound, so reports are not limited by address');
    return true;
  }
  try {
    const { success } = await env.REPORT_LIMITER.limit({ key });
    return success !== false;
  } catch (err) {
    console.warn(`the address limiter failed: ${err?.message ?? err}`);
    return true;
  }
}

/**
 * POST /crash. reporterOf is the mod reports' install hash; fetcher is the fetch used for GitHub and
 * Discord; ctx.waitUntil runs the Discord ping after the answer (tests pass their own ctx).
 */
export async function handleCrashPost(request, env, fetcher, reporterOf, ctx = null) {
  if (!env.VOTER_SALT || !env.VOTES) return json(503, { ok: false, error: 'not configured' });

  // The address only keys the in memory rate limiter, with its own counter for crash reports.
  const address = request.headers.get('CF-Connecting-IP') ?? 'unknown';
  if (!(await underAddressLimit(env, 'crash:' + address)))
    return json(429, { ok: false, error: 'too many reports, try again later' });

  const length = parseInt(request.headers.get('content-length') ?? '0', 10);
  if (length > MAX_CRASH_BODY) return json(413, { ok: false, error: 'too large' });
  const raw = await request.arrayBuffer();
  if (raw.byteLength > MAX_CRASH_BODY) return json(413, { ok: false, error: 'too large' });

  let form;
  try {
    form = await new Response(raw, { headers: { 'content-type': request.headers.get('content-type') ?? '' } }).formData();
  } catch (err) {
    // The parser's reason goes back in the answer, so the tool's log says why (an unquoted part
    // name, the form .NET writes by default, is "missing a name" to the Cloudflare runtime).
    return json(400, { ok: false, error: 'not a form: ' + String(err?.message ?? err).slice(0, 200) });
  }
  const summaryText = form.get('summary');
  const zip = form.get('zip');
  if (typeof summaryText !== 'string' || summaryText.length > MAX_SUMMARY) return json(400, { ok: false, error: 'summary' });
  if (!zip || typeof zip === 'string') return json(400, { ok: false, error: 'zip' });
  const zipBytes = new Uint8Array(await zip.arrayBuffer());
  if (zipBytes.byteLength > MAX_CRASH_ZIP) return json(413, { ok: false, error: 'the zip is too large' });
  if (!isZip(zipBytes)) return json(400, { ok: false, error: 'not a zip' });

  let parsed;
  try { parsed = JSON.parse(summaryText); } catch { return json(400, { ok: false, error: 'summary is not JSON' }); }
  const summary = validateSummary(parsed);
  if (typeof summary === 'string') return json(400, { ok: false, error: 'invalid ' + summary });

  const reporter = await reporterOf(summary.install, env.VOTER_SALT);
  // The reply key is never kept in the summary, so it cannot reach the issue, the zip metadata or a log.
  const replyKey = repliesOn(env) ? parseReplyKey(parsed.replyKey) : null;
  // The player's own note, cleaned and quoted in the issue. A broken zip only means no note.
  summary.note = await readPlayerNote(zipBytes);

  // No, still broken (followup.js): the earlier report's key must open it before anything is stored.
  const followUp = validateFollowUp(parsed.followUp);
  if (typeof followUp === 'string') return json(400, { ok: false, error: 'invalid ' + followUp });
  let followUpRecord = null;
  if (followUp) {
    if (!repliesOn(env) || !(env.CRASH_GITHUB_TOKEN || env.GITHUB_TOKEN)) return json(503, { ok: false, error: 'not configured' });
    const digest = new Uint8Array(await crypto.subtle.digest('SHA-256', new TextEncoder().encode(followUp.key)));
    const prefix = [...digest.slice(0, 8)].map(b => b.toString(16).padStart(2, '0')).join('');
    if (!(await underAddressLimit(env, 'followup:' + prefix)))
      return json(429, { ok: false, error: 'too many reports, try again later' });
    followUpRecord = await openReport(env, await replySigner(env), followUp.code, followUp.key);
    if (!followUpRecord) return json(404, { ok: false, error: 'unknown or expired report' });
  }

  // The same player and the same known crash within 10 minutes: the earlier code, nothing new is
  // stored or filed, and no daily count is used. Unknown crashes are never folded together.
  const triage = summary.triage;
  // Scoped by tool and side, so the same crash sent from another tool or the server is its own report
  // (and its reply key never lands on a report from another tool). A follow up is never folded, and neither is
  // Send my logs: since v1.3.5 it carries setup hints, and a player who sends logs after a hint may add a note.
  const dedupeKey = triage?.signature && !followUp && summary.reason !== 'logs' ? `cd:${reporter}:${summary.tool}:${summary.side}:${triage.signature}` : null;
  if (dedupeKey) {
    try {
      const earlier = JSON.parse((await env.VOTES.get(dedupeKey)) ?? 'null');
      if (earlier && CODE.test(earlier.code ?? '')) {
        const sameScope = earlier.tool === summary.tool && earlier.side === summary.side;
        const replies = replyKey && sameScope ? await addReplyKey(env, earlier.code, replyKey) : false;
        return json(200, { ok: true, code: earlier.code, expires: earlier.expires, filed: earlier.filed === true, duplicate: true, replies });
      }
    } catch (err) {
      console.warn(`the duplicate check failed: ${err?.message ?? err}`);
    }
  }

  const installCount = `cq:${reporter}:${day()}`;
  const dayCount = `cg:${day()}`;
  if (!(await bump(env.VOTES, installCount, parseInt(env.MAX_CRASH_PER_INSTALL_DAY ?? '10', 10))))
    return json(429, { ok: false, error: 'too many crash reports from this install today' });
  if (!(await bump(env.VOTES, dayCount, parseInt(env.MAX_CRASH_REPORTS_DAY ?? '100', 10))))
    return json(429, { ok: false, error: 'the daily crash report limit is reached, try again tomorrow' });

  let code = null;
  const expires = new Date(Date.now() + CRASH_TTL * 1000).toISOString().slice(0, 10);
  try {
    for (let i = 0; i < 6 && code === null; i++) {
      const next = newCode(env.CODE_RANDOM);
      if ((await env.VOTES.get('crash:' + next)) === null) code = next;
    }
    if (code === null) return json(503, { ok: false, error: 'try again' });
    await env.VOTES.put('crash:' + code, zipBytes, {
      expirationTtl: CRASH_TTL,
      metadata: { tool: summary.tool, side: summary.side, reason: summary.reason, size: zipBytes.byteLength, reporter },
    });
  } catch (err) {
    console.warn(`the crash zip could not be stored: ${err?.message ?? err}`);
    return json(503, { ok: false, error: 'try again' });
  }

  // A follow up goes on the earlier report's issue, never a new one, and Coodsz gets the ping.
  if (followUp) {
    const origin = new URL(request.url).origin;
    const filed = await fileFollowUp(env, fetcher, followUpRecord,
      { earlier: followUp.code, code, summary, zipBytes: zipBytes.byteLength, origin, expires, steps: followUp.steps });
    // Without the comment Coodsz never sees the answer, so the tool is told to try again (the zip stays
    // under its code until it expires, nothing points at it).
    if (!filed) {
      // Nothing points at the new zip, so it goes, and the try again does not use up the player's day.
      await forget(env.VOTES, code, [installCount, dayCount]);
      return json(502, { ok: false, error: 'the answer could not be added to the report, try again later', filed: false });
    }
    // Keyed by the new code, so a second No on the same report is never taken for a ping sent before.
    const ping = pingCoodsz(env, fetcher, { code, summary, repo: followUpRecord.repo, issue: followUpRecord.issue, followUpOf: followUp.code })
      .catch(err => console.warn(`the Discord ping failed: ${err?.name ?? 'error'}`));
    if (ctx?.waitUntil) ctx.waitUntil(ping);
    return json(200, { ok: true, code, expires, filed: true, followUp: followUp.code });
  }

  // The zip is kept even when GitHub fails: Coodsz can still fetch it with the code the player posts.
  let filed = false;
  let tracked = null;
  let issueNumber = null;
  // Comments from this moment on are read for replies, so it is taken before filing.
  const filedAt = new Date().toISOString();
  const repo = crashRepo(env, summary.tool);
  const token = env.CRASH_GITHUB_TOKEN || env.GITHUB_TOKEN;
  if (token) {
    const origin = new URL(request.url).origin;
    const gh = (path, method, body) => fetcher(`https://api.github.com/repos/${repo}/issues${path}`, {
      method,
      headers: {
        Authorization: `Bearer ${token}`,
        Accept: 'application/vnd.github+json',
        'X-GitHub-Api-Version': '2022-11-28',
        'User-Agent': 'coodsz-mod-reports-worker',
        'content-type': 'application/json',
      },
      ...(body === undefined ? {} : { body: JSON.stringify(body) }),
    });

    // A known bug with a tracking issue: the report becomes a comment there, not a new issue. Only
    // an open issue Coodsz labeled tracking takes it, so a tool cannot point it at any issue.
    // Tracking issue numbers are issues in the Coop and Friends repository, so only Coop reports use them.
    if (summary.tool === 'coop' && triage?.signature && triage.reportNeeded && triage.trackingIssue) {
      try {
        const res = await gh(`/${triage.trackingIssue}`, 'GET');
        const issue = res.ok ? await res.json() : null;
        const labels = (issue?.labels ?? []).map(l => (typeof l === 'string' ? l : l?.name));
        if (issue && issue.state === 'open' && !issue.pull_request && labels.includes(TRACKING_LABEL)) {
          const comment = await gh(`/${triage.trackingIssue}/comments`, 'POST', { body: trackedComment(code, summary, reporter, zipBytes.byteLength, origin, expires, !!replyKey) });
          if (comment.ok) {
            filed = true;
            tracked = triage.trackingIssue;
            issueNumber = triage.trackingIssue;
          }
        }
      } catch {
        // Falls back to a new issue below.
      }
    }

    if (tracked === null) {
      try {
        const res = await gh('', 'POST', {
          title: crashTitle(code, summary),
          body: crashBody(code, summary, reporter, zipBytes.byteLength, origin, expires, !!replyKey),
          labels: crashLabels(summary),
        });
        filed = res.ok;
        if (filed) {
          // The issue number comes from GitHub's own answer, never from the tool.
          const number = (await res.json().catch(() => null))?.number;
          if (Number.isInteger(number) && number > 0) issueNumber = number;
        }
        // The triage is in the issue body itself. A clear non crash is closed right away; the zip and the code stay.
        if (filed && issueNumber !== null && triage?.signature && triage.category === 'not-a-crash')
          await gh(`/${issueNumber}`, 'PATCH', { state: 'closed', state_reason: 'not_planned' });
      } catch {
        // The issue itself may be filed; a failed close only leaves it open.
      }
    }
  }

  if (dedupeKey) {
    try {
      await env.VOTES.put(dedupeKey, JSON.stringify({ code, expires, filed, tool: summary.tool, side: summary.side }), { expirationTtl: DEDUPE_TTL });
    } catch (err) {
      console.warn(`the duplicate key could not be stored: ${err?.message ?? err}`);
    }
  }
  // rr:<code> only for a filed report, so a key never opens a report nobody can answer.
  const replies = !!replyKey && filed && issueNumber !== null
    && await storeReplyRecord(env, code, replyKey, { repo, issue: issueNumber, tracked: tracked !== null, created: filedAt });

  if (filed && issueNumber !== null && needsPing(summary)) {
    const ping = pingCoodsz(env, fetcher, { code, summary, repo, issue: issueNumber })
      .catch(err => console.warn(`the Discord ping failed: ${err?.name ?? 'error'}`));
    if (ctx?.waitUntil) ctx.waitUntil(ping);
  }
  return json(200, { ok: true, code, expires, filed, replies, ...(tracked === null ? {} : { tracked }) });
}

// The Discord ping to Coodsz (docs/design/in-tool-replies.md, Addition 3).
export const PING_BURST = 10;
export const PING_WINDOW = 10 * 60;
export const PING_LINE = 150;
const PING_PRODUCT = { coop: 'Coop', taom: 'TAOM', ad1259: 'AD1259', be: 'Burning Empires', rot: 'Realm of Thrones' };
const WEBHOOK = /^https:\/\/(?:discord|discordapp)\.com\/api\/webhooks\/\d+\/[\w-]+$/;

/**
 * True when a filed report needs Coodsz: no signature matched (an unknown crash, Send my logs, or a
 * tool without auto triage) or the match is a known Coop bug. Send my logs always pings, a setup hint
 * included, since the player sent the logs after seeing the fix. A known cause the player can fix and
 * not-a-crash never ping from a crash report. Duplicates never get here.
 */
export function needsPing(summary) {
  const t = summary.triage;
  return summary.reason === 'logs' || !t?.signature || t.category === 'coop-bug';
}

/** The ping text: the product, the code, one cleaned line and the issue link. No ids, names, logs or mod list. */
export function pingText(code, summary, link, waiting = 0, followUpOf = null) {
  const t = summary.triage;
  const kind = followUpOf ? `still broken after the answer to ${followUpOf}`
    // Send my logs pings with a setup hint too, so it is never called a Coop bug.
    : summary.reason === 'logs' ? (t?.signature ? `logs after the ${t.signature} hint` : 'logs')
    : t?.signature ? `known Coop bug ${t.signature}` : 'unknown crash';
  const line = summary.crashLine ? defang(noMarkdown(cleanCrashLine(summary.crashLine, PING_LINE)))
    : summary.reason === 'logs' ? 'Logs sent' : 'No crash line';
  return [
    `**${PING_PRODUCT[summary.tool]}** ${code}, ${kind}: ${line}`,
    link,
    ...(waiting > 0 ? [`and ${waiting} more waiting on GitHub`] : []),
  ].join('\n');
}

/**
 * Posts the ping, at most PING_BURST per PING_WINDOW for the whole worker; the ones over the cap are
 * counted and named by the next ping. Every sent ping is its own KV entry ping:<seconds>:<code>, written
 * only after Discord took it, and the cap counts them with one list. So no two pings write the same key
 * (no lost counts, no one write a second limit), a failed post uses no slot, and it is one write per
 * ping. KV list is eventually consistent, so a burst can pass the cap by a few. One ping per code comes
 * from the code itself: a new code pings once, a duplicate never gets here.
 * Never throws into the report, and never logs the webhook address.
 */
export async function pingCoodsz(env, fetcher, { code, summary, repo, issue, followUpOf = null }) {
  const url = env.CRASH_PING_WEBHOOK;
  if (!url) return 'off';
  if (typeof url !== 'string' || !WEBHOOK.test(url.trim())) {
    console.warn('CRASH_PING_WEBHOOK is not a Discord webhook address, so the ping is skipped');
    return 'bad secret';
  }
  const kv = env.VOTES;
  const now = Math.floor(Date.now() / 1000);
  let recent = [];
  try {
    const listed = await kv.list({ prefix: 'ping:' });
    recent = (listed?.keys ?? []).map(k => /^ping:(\d+):(CR-\w{5})$/.exec(k.name)).filter(Boolean);
  } catch (err) {
    // On purpose: a failed list lets the ping through, a missed ping is worse than one too many.
    console.warn(`the ping cap could not be read: ${err?.message ?? err}`);
  }
  if (recent.some(m => m[2] === code)) return 'sent before';
  const waiting = parseInt((await kv.get('pingwait')) ?? '0', 10) || 0;
  // A burst at the same moment: the rate limit binding (key ping:all, 10 a minute) counts at once,
  // where the KV list can lag. A missing or failing binding leaves only the KV count, on purpose.
  let burstOk = true;
  if (env.REPORT_LIMITER) {
    try {
      burstOk = (await env.REPORT_LIMITER.limit({ key: 'ping:all' }))?.success !== false;
    } catch {
      burstOk = true;
    }
  }
  if (!burstOk || recent.filter(m => now - Number(m[1]) < PING_WINDOW).length >= PING_BURST) {
    await kv.put('pingwait', String(waiting + 1), { expirationTtl: 24 * 3600 });
    return 'capped';
  }
  const res = await fetcher(url.trim(), {
    method: 'POST',
    headers: { 'content-type': 'application/json', 'User-Agent': 'coodsz-mod-reports-worker' },
    body: JSON.stringify({
      content: pingText(code, summary, `https://github.com/${repo}/issues/${issue}`, waiting, followUpOf),
      allowed_mentions: { parse: [] },
    }),
  });
  if (!res.ok) {
    console.warn(`the Discord ping failed with ${res.status}`);
    return 'failed';
  }
  await kv.put(`ping:${now}:${code}`, '1', { expirationTtl: PING_WINDOW + 60 });
  if (waiting > 0) await kv.put('pingwait', '0', { expirationTtl: 24 * 3600 });
  return 'sent';
}

/** GET /crash/<code>: the zip, for Coodsz only (the X-Admin-Key header). */
export async function handleCrashGet(request, env, codeText) {
  if (!env.ADMIN_KEY || !env.VOTES) return json(503, { ok: false, error: 'not configured' });
  if (!(await sameSecret(request.headers.get('X-Admin-Key') ?? '', env.ADMIN_KEY)))
    return json(401, { ok: false, error: 'wrong or missing X-Admin-Key' });
  const code = parseCode(codeText);
  if (!code) return json(400, { ok: false, error: 'not a crash report code' });
  const zip = await env.VOTES.get('crash:' + code, { type: 'arrayBuffer' });
  if (zip === null || zip === undefined) return json(404, { ok: false, error: 'no report with this code (reports are kept 30 days)' });
  return new Response(zip, {
    status: 200,
    headers: {
      'content-type': 'application/zip',
      'content-disposition': `attachment; filename="${code}.zip"`,
      'cache-control': 'no-store',
    },
  });
}
