// Reply push: POST /github/webhook, where GitHub tells the worker about comments on the crash issues, so
// Coodsz's /reply reaches the player's tool within a minute instead of at its next GitHub read
// (README.md, Turn on reply push).
//
// GitHub signs each delivery with GITHUB_WEBHOOK_SECRET (X-Hub-Signature-256, an HMAC-SHA256 of the raw
// body). Without the secret the endpoint answers 503 and the replies stay as they were (read on request).
// A missing or wrong signature is 401, a body over MAX_WEBHOOK_BODY is 413. Everything else that is signed
// gets a 200, also what is ignored, since a failed delivery only shows red on GitHub. A KV read or write
// that fails is 503 (a missing record is not a failure), so the delivery shows red and can be sent again
// from the webhook's Recent Deliveries.
//
// What counts: the issue_comment event (created, edited or deleted) on an issue (not a pull request) in
// one of the crash repositories (crashRepo), by Coodsz or the worker (replies.js byWorker; answerEvent
// then lets only Coodsz's /reply and /retract and the worker's follow up markers through). The report is
// the code the comment names or the issue's own code (its body and title), and only when rr:<code> says
// that report lives on this very issue: the same record POST /replies opens, so a comment can never put
// an answer on a report of another issue. The comment goes through answerEvent, the cleaning POST /replies
// uses, and lands in rr:<code>.push (replies.js, Push), written only when the answers or status labels
// change. An edit replaces the stored answer and a delete takes it out, by comment id and date: a delivery
// older than what is stored for that comment, or after its delete, changes nothing (tombstones in the same
// push field). An edit that changed the code moves it: the old body (changes.body.from) is read too.
// The Issues event (labeled or unlabeled) on a report's own issue brings its status labels the same way
// (labelsChanged); a tracking issue's labels come with the next comment or the 30 minute GitHub read.
// Before the body is read, each address may send WEBHOOK_LIMITER's 60 deliveries a minute (REPORT_LIMITER's
// 10 under a webhook: key when that binding is missing), so a stranger cannot make the worker read 1 MB and
// run an HMAC as often as they like. GitHub sends a few a minute at most. Past the day's MAX_PUSH_WRITES
// (budget.js) a delivery is answered 200 with ignored "budget" and nothing is written: the 30 minute GitHub
// read serves a skipped answer, and on an issue of one page (up to 100 comments) also drops a skipped delete. On
// an issue of more pages a skipped delete stays served (never as a full list), since a page can shift between two
// requests and hide a live answer. Nothing from a delivery is logged.

import { CODE, CRASH_TOOLS, crashRepo, underAddressLimit } from './crash.js';
import { checkRecord, sameHash } from './reply-keys.js';
import { parseAnswer } from './reply-text.js';
import { FOLLOW_UP_MARKER } from './followup.js';
import { byWorker, answerEvent, labelNames, pushOn, pushState, storePush, statusLabels, sameEvent, GONE_FOR_GOOD } from './replies.js';

// GitHub caps a delivery at 25 MB, but a comment event is the issue and the comment (each at most 65,536
// characters) and the old body of an edit, so 1 MB holds every real one.
export const MAX_WEBHOOK_BODY = 1024 * 1024;
export const WEBHOOK_EVENT = 'issue_comment';
export const WEBHOOK_ACTIONS = ['created', 'edited', 'deleted'];
// The Issues event, for the status badge: a fixed-in-v<version> or player-solved label added or taken off.
export const LABEL_EVENT = 'issues';
export const LABEL_ACTIONS = ['labeled', 'unlabeled'];
// A comment names one code at most; with the issue's own code (body and title) and the old body of an
// edit that is a handful. Each one costs a KV read.
const MAX_CODES = 4;
const SIGNATURE = /^sha256=([0-9a-f]{64})$/i;
const ISSUE_BODY_CODE = /^<!-- coodsz-crash-report v1 -->\s*\nReport code \*\*(CR-[0-9A-Z]{5})\*\*/;
const TITLE_CODE = /\[(CR-[0-9A-Z]{5})\]\s*$/;

const json = (status, body) => new Response(JSON.stringify(body), {
  status,
  headers: { 'content-type': 'application/json; charset=utf-8', 'cache-control': 'no-store' },
});
// KV did not answer, or took no more writes for this key (about one a second): the delivery shows red and can
// be sent again, and the report's next 30 minute GitHub read brings the answer anyway.
const retry = () => json(503, { ok: false, error: 'try again', heals: 'the next GitHub read of this report brings it' });

/** The body as bytes, or null when it is over max (the stream stops there, it is never read in full). */
export async function readCapped(request, max = MAX_WEBHOOK_BODY) {
  const length = parseInt(request.headers.get('content-length') ?? '', 10);
  if (Number.isFinite(length) && length > max) return null;
  if (!request.body) return new Uint8Array(0);
  const reader = request.body.getReader();
  const chunks = [];
  let total = 0;
  for (;;) {
    const { done, value } = await reader.read();
    if (done) break;
    total += value.byteLength;
    if (total > max) {
      await reader.cancel().catch(() => {});
      return null;
    }
    chunks.push(value);
  }
  const bytes = new Uint8Array(total);
  let at = 0;
  for (const c of chunks) {
    bytes.set(c, at);
    at += c.byteLength;
  }
  return bytes;
}

const hex = bytes => [...new Uint8Array(bytes)].map(b => b.toString(16).padStart(2, '0')).join('');

/** True when the header is "sha256=" and the HMAC-SHA256 of the raw body with the secret, compared in constant time. */
export async function signatureOk(secret, header, bytes) {
  const given = SIGNATURE.exec(typeof header === 'string' ? header.trim() : '');
  if (!given || typeof secret !== 'string' || secret.length === 0) return false;
  const key = await crypto.subtle.importKey('raw', new TextEncoder().encode(secret), { name: 'HMAC', hash: 'SHA-256' }, false, ['sign']);
  return sameHash(hex(await crypto.subtle.sign('HMAC', key, bytes)), given[1].toLowerCase());
}

/** The crash repositories, lower case (GitHub names are not case sensitive). */
export function crashRepos(env) {
  return new Set(CRASH_TOOLS.map(tool => crashRepo(env, tool).toLowerCase()));
}

/** The codes a comment could be for: the code it names (its answer or its follow up marker) and the issue's own. */
export function candidateCodes(bodies, issue) {
  const codes = new Set();
  let answers = false;
  for (const body of bodies) {
    if (typeof body !== 'string') continue;
    const answer = parseAnswer(body);
    const followUp = FOLLOW_UP_MARKER.exec(body.replace(/^\s+/, ''));
    if (answer) answers = true;
    if (answer?.code) codes.add(answer.code);
    if (followUp) codes.add(followUp[1]);
  }
  // A /reply without a code is for the issue's own report.
  if (answers) for (const code of ownCodes(issue)) codes.add(code);
  return [...codes].filter(code => CODE.test(code)).slice(0, MAX_CODES);
}

/** The issue's own report code, from its body and its title (a tracking issue has none). */
export function ownCodes(issue) {
  const own = [ISSUE_BODY_CODE.exec(typeof issue?.body === 'string' ? issue.body.slice(0, 400).replace(/\r\n?/g, '\n') : ''),
    TITLE_CODE.exec(typeof issue?.title === 'string' ? issue.title : '')];
  return [...new Set(own.filter(Boolean).map(m => m[1]))].filter(code => CODE.test(code));
}

/**
 * rr:<code> when it is the report filed on this very issue, null when not, or undefined when KV did not
 * answer (the caller answers 503 so GitHub can send it again).
 */
async function recordOn(env, code, repo, number) {
  let raw;
  try {
    raw = await env.VOTES.get('rr:' + code);
  } catch {
    return undefined;
  }
  let record = null;
  try {
    record = checkRecord(JSON.parse(raw ?? 'null'));
  } catch {
    record = null;
  }
  return record && record.repo.toLowerCase() === repo && record.issue === number ? record : null;
}

/**
 * Puts one comment into one report's rr:<code>.push: the event in place of the comment's old one, or the
 * old one taken out (event null) with a tombstone, with the issue's status labels. A delivery older than
 * what is stored for that comment (a stored answer or tombstone with a newer date, any delete) changes
 * nothing, so GitHub sending deliveries late or twice cannot show older text or bring a deleted answer back.
 * action is the delivery's. KV is written only when the record changes. Gives stored, unchanged or failed.
 */
export async function applyComment(env, code, record, comment, action, event, labels, now = Date.now()) {
  const state = pushState(record) ?? { labels: [], events: [], gone: [] };
  const id = String(comment.id);
  const at = action === 'deleted' ? GONE_FOR_GOOD : String(comment.updated_at || comment.created_at || '');
  const stored = state.events.find(e => e.id === id);
  const tomb = state.gone.find(g => g.id === id);
  // updated_at counts whole seconds: a delivery from the same second as the stored one is not taken as newer,
  // so an older edit from that second never writes over newer text (the 30 minute GitHub read settles a tie).
  if ((tomb && tomb.at >= at) || (stored?.at && stored.at >= at)) return 'unchanged';
  // The same event again (a follow up marker has no date to compare): the old stamp stays, and only a change of
  // the issue's status labels it brings is written.
  if (event && stored && sameEvent(stored, event)) {
    if (labels === null || JSON.stringify(statusLabels(labels)) === JSON.stringify(state.labels)) return 'unchanged';
    return (await storePush(env, code, record, { labels: statusLabels(labels), events: state.events, gone: state.gone }, now)).result;
  }
  const events = state.events.filter(e => e.id !== id);
  const gone = state.gone.filter(g => g.id !== id);
  // Stamped with when it was stored, so a complete GitHub read that does not list it can tell a comment that
  // came in while it ran from one deleted while the webhook was missed (replies.js mergeEvents).
  if (event) events.push({ ...event, storedAt: now });
  // A created comment that is no answer needs no tombstone: nothing older can come for it.
  else if (stored || action !== 'created') gone.push({ id, at });
  return (await storePush(env, code, record, { labels: labels === null ? state.labels : statusLabels(labels), events, gone }, now)).result;
}

/**
 * True while this address is under the webhook limit: WEBHOOK_LIMITER when it is bound, else
 * REPORT_LIMITER under its own webhook: key. A failing binding lets the delivery through, like the others.
 */
export async function underWebhookLimit(env, address) {
  if (!env.WEBHOOK_LIMITER) return underAddressLimit(env, 'webhook:' + address);
  try {
    const { success } = await env.WEBHOOK_LIMITER.limit({ key: 'webhook:' + address });
    return success !== false;
  } catch (err) {
    console.warn(`the webhook limiter failed: ${err?.message ?? err}`);
    return true;
  }
}

/** POST /github/webhook. */
export async function handleGithubWebhook(request, env) {
  if (!pushOn(env) || !env.VOTES) return json(503, { ok: false, error: 'not configured' });
  // The address only keys the in memory rate limiter; it is not kept.
  if (!(await underWebhookLimit(env, request.headers.get('CF-Connecting-IP') ?? 'unknown')))
    return json(429, { ok: false, error: 'too many deliveries, try again later' });
  const bytes = await readCapped(request);
  if (bytes === null) return json(413, { ok: false, error: 'too large' });
  if (!(await signatureOk(env.GITHUB_WEBHOOK_SECRET, request.headers.get('X-Hub-Signature-256'), bytes))) {
    console.warn('a webhook delivery had a missing or wrong signature');
    return json(401, { ok: false, error: 'bad signature' });
  }
  const ignored = why => json(200, { ok: true, ignored: why });
  const event = request.headers.get('X-GitHub-Event');
  if (event === 'ping') return json(200, { ok: true, pong: true });
  if (event !== WEBHOOK_EVENT && event !== LABEL_EVENT) return ignored('event');
  let payload;
  try {
    payload = JSON.parse(new TextDecoder().decode(bytes));
  } catch {
    return ignored('not JSON');
  }
  if (!payload || typeof payload !== 'object' || !(event === LABEL_EVENT ? LABEL_ACTIONS : WEBHOOK_ACTIONS).includes(payload.action)) return ignored('action');
  const repo = typeof payload.repository?.full_name === 'string' ? payload.repository.full_name.toLowerCase() : '';
  if (!crashRepos(env).has(repo)) return ignored('repository');
  const issue = payload.issue;
  if (!issue || typeof issue !== 'object' || issue.pull_request || !Number.isSafeInteger(issue.number) || issue.number < 1) return ignored('issue');
  if (event === LABEL_EVENT) return labelsChanged(env, repo, issue);
  const comment = payload.comment;
  if (!comment || typeof comment !== 'object' || !Number.isSafeInteger(comment.id) || comment.id < 1) return ignored('comment');
  if (!byWorker(comment, env)) return ignored('author');

  const oldBody = payload.action === 'edited' && typeof payload.changes?.body?.from === 'string' ? payload.changes.body.from : null;
  const codes = candidateCodes([comment.body, oldBody], issue);
  if (codes.length === 0) return ignored('no answer');
  const labels = labelNames(issue);
  let stored = 0;
  let matched = false;
  let skipped = false;
  for (const code of codes) {
    // The report must live on this issue: rr:<code> is written from the worker's own filing. KV that does not
    // answer is a 503, so the delivery shows red and can be sent again, instead of a lost comment.
    const record = await recordOn(env, code, repo, issue.number);
    if (record === undefined) return retry();
    if (!record) continue;
    matched = true;
    const answer = payload.action === 'deleted' ? null : answerEvent(comment, code, record.tracked === true, env);
    const done = await applyComment(env, code, record, comment, payload.action, answer, labels);
    if (done === 'failed') return retry();
    if (done === 'skipped') skipped = true;
    if (done === 'stored') stored++;
  }
  if (stored > 0) return json(200, { ok: true, stored });
  return skipped ? ignored('budget') : ignored(matched ? 'unchanged' : 'no report');
}

/**
 * The Issues event (labeled or unlabeled) on a report's own issue: its status labels (fixed-in-v<version>,
 * player-solved) into rr:<code>.push, written only when they change. Anyone who can label the issue counts,
 * as with the GitHub read. Label deliveries carry no order GitHub guarantees: one that arrives late can leave
 * the badge one change behind until the next comment or the 30 minute GitHub read. A tracking issue has no
 * own code, so its reports get label changes at the next
 * 30 minute GitHub read or the next comment.
 */
async function labelsChanged(env, repo, issue) {
  const labels = labelNames(issue);
  if (labels === null) return json(200, { ok: true, ignored: 'labels' });
  let stored = 0;
  let matched = false;
  let skipped = false;
  for (const code of ownCodes(issue)) {
    const record = await recordOn(env, code, repo, issue.number);
    if (record === undefined) return retry();
    if (!record) continue;
    matched = true;
    const state = pushState(record) ?? { events: [], gone: [] };
    const done = (await storePush(env, code, record, { labels: statusLabels(labels), events: state.events, gone: state.gone })).result;
    if (done === 'failed') return retry();
    if (done === 'skipped') skipped = true;
    if (done === 'stored') stored++;
  }
  if (stored > 0) return json(200, { ok: true, stored });
  return json(200, { ok: true, ignored: skipped ? 'budget' : matched ? 'unchanged' : 'no report' });
}
