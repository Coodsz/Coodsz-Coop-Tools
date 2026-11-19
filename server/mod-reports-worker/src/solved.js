// Yes, it's fixed: POST /replies/solved, where a player's tool says the answer to a report solved it.
//
// Request:  {"kind":"CoodszReplySolved","schema":1,"code":"CR-7KQ2M","key":"<43 chars>"}, at most 1 KB.
//           The key is the report's reply key, checked the same way POST /replies checks it
//           (reply-keys.js openReport), so only the PC that sent the report can mark it.
// Response: 200 {"ok":true,"solved":true,"action":"closed"|"labeled"|"noted"} when it was done (or was
//           done before), 200 {"ok":true,"solved":false} for an unknown, expired or unfiled report or a
//           wrong key. 400 is a malformed request, 429 {ok:false,error,next}, 502 a GitHub failure.
//
// What it does on GitHub is one setting, SOLVED_ACTION:
//   close (the default)  adds the player-solved label and closes the issue as completed (an issue that
//                        is closed already keeps its own reason)
//   label                only adds the player-solved label, the issue stays open for Coodsz
// A report filed as a comment on a tracking issue never closes or labels that shared issue: it gets a
// short comment there instead (action noted). rs:<code> in VOTES remembers a done report until the
// report's 30 days end, so a second click does nothing on GitHub.

import { CODE, underAddressLimit } from './crash.js';
import { REPLY_KEY, replySigner, openReport, REPLY_TTL } from './reply-keys.js';
import { SOLVED_LABEL } from './replies.js';

export const MAX_SOLVED_BODY = 1024;
export const SOLVED_ACTIONS = ['close', 'label'];
export const DEFAULT_SOLVED_ACTION = 'close';
const NEXT_AFTER_LIMIT = 900;

const json = (status, body) => new Response(JSON.stringify(body), {
  status,
  headers: { 'content-type': 'application/json; charset=utf-8', 'cache-control': 'no-store' },
});

/** The SOLVED_ACTION setting: close or label. Unset or anything else is the default, close. */
export function solvedAction(env) {
  const value = String(env.SOLVED_ACTION ?? '').trim().toLowerCase();
  if (SOLVED_ACTIONS.includes(value)) return value;
  if (value) console.warn(`SOLVED_ACTION "${value.slice(0, 20)}" is not close or label, so close is used`);
  return DEFAULT_SOLVED_ACTION;
}

/** The checked request: { code, key }, or a string that says what is wrong. */
export function validateSolved(body) {
  if (!body || typeof body !== 'object' || Array.isArray(body)) return 'not an object';
  if (body.kind !== 'CoodszReplySolved' || body.schema !== 1) return 'unknown kind or schema';
  if (typeof body.code !== 'string' || !CODE.test(body.code)) return 'code';
  if (typeof body.key !== 'string' || !REPLY_KEY.test(body.key)) return 'key';
  return { code: body.code, key: body.key };
}

/** The comment a tracking issue gets. It never starts with /reply, so it is never served as an answer. */
export const solvedComment = code => `The player says the answer fixed report **${code}** (Yes, it's fixed in the tool).`;

const hexOf = bytes => [...bytes].map(b => b.toString(16).padStart(2, '0')).join('');

/** POST /replies/solved. fetcher is the fetch used for GitHub. */
export async function handleSolvedPost(request, env, fetcher) {
  const address = request.headers.get('CF-Connecting-IP') ?? 'unknown';
  if (!(await underAddressLimit(env, 'solved:' + address)))
    return json(429, { ok: false, error: 'too many tries, try again later', next: NEXT_AFTER_LIMIT });

  const length = parseInt(request.headers.get('content-length') ?? '0', 10);
  if (length > MAX_SOLVED_BODY) return json(400, { ok: false, error: 'too large' });
  const text = await request.text();
  if (new TextEncoder().encode(text).length > MAX_SOLVED_BODY) return json(400, { ok: false, error: 'too large' });
  let parsed;
  try { parsed = JSON.parse(text); } catch { return json(400, { ok: false, error: 'not JSON' }); }
  const ask = validateSolved(parsed);
  if (typeof ask === 'string') return json(400, { ok: false, error: 'invalid ' + ask });

  // Per key too, like /replies, and the limiter key holds a hash prefix, never the key.
  const digest = new Uint8Array(await crypto.subtle.digest('SHA-256', new TextEncoder().encode(ask.key)));
  if (!(await underAddressLimit(env, 'solvedkey:' + hexOf(digest.slice(0, 8)))))
    return json(429, { ok: false, error: 'too many tries, try again later', next: NEXT_AFTER_LIMIT });

  const id = String(env.COODSZ_GITHUB_ID ?? '').trim();
  const token = env.CRASH_GITHUB_TOKEN || env.GITHUB_TOKEN;
  if (!env.VOTES || !/^\d{1,15}$/.test(id) || !(env.REPLY_SALT || env.VOTER_SALT) || !token) {
    console.warn('Yes, it is fixed is not configured (VOTES, COODSZ_GITHUB_ID, REPLY_SALT or VOTER_SALT, and a GitHub token)');
    return json(503, { ok: false, error: 'not configured' });
  }

  const record = await openReport(env, await replySigner(env), ask.code, ask.key);
  if (!record) return json(200, { ok: true, solved: false });

  const doneKey = 'rs:' + ask.code;
  try {
    const done = JSON.parse((await env.VOTES.get(doneKey)) ?? 'null');
    if (done && typeof done.action === 'string') return json(200, { ok: true, solved: true, action: done.action });
  } catch {
    // An unreadable mark is done again; GitHub takes the same label and close twice.
  }

  const gh = (path, method, body) => fetcher(`https://api.github.com/repos/${record.repo}/issues/${record.issue}${path}`, {
    method,
    headers: {
      Authorization: `Bearer ${token}`,
      Accept: 'application/vnd.github+json',
      'X-GitHub-Api-Version': '2022-11-28',
      'User-Agent': 'coodsz-mod-reports-worker',
      'content-type': 'application/json',
    },
    body: JSON.stringify(body),
  });

  let action;
  try {
    if (record.tracked === true) {
      const res = await gh('/comments', 'POST', { body: solvedComment(ask.code) });
      if (!res.ok) throw new Error(`GitHub answered ${res.status} to the comment`);
      action = 'noted';
    } else {
      const res = await gh('/labels', 'POST', { labels: [SOLVED_LABEL] });
      if (!res.ok) throw new Error(`GitHub answered ${res.status} to the label`);
      action = 'labeled';
      if (solvedAction(env) === 'close') {
        // An issue Coodsz already closed keeps its own reason (not planned stays not planned).
        const now = await gh('', 'GET');
        // Without the issue its close reason is unknown, so nothing is closed: the player tries again.
        if (!now.ok) throw new Error(`GitHub answered ${now.status} to reading the issue`);
        const issue = await now.json().catch(() => null);
        if (!issue || typeof issue !== 'object') throw new Error('GitHub sent no issue');
        if (issue?.state !== 'closed') {
          const closed = await gh('', 'PATCH', { state: 'closed', state_reason: 'completed' });
          if (!closed.ok) throw new Error(`GitHub answered ${closed.status} to the close`);
        }
        action = 'closed';
      }
    }
  } catch (err) {
    console.warn(`Yes, it's fixed could not be done on GitHub: ${err?.message ?? err}`);
    return json(502, { ok: false, error: 'GitHub could not be reached, try again later' });
  }

  try {
    // Kept as long as the report: the 30 days from when it was filed.
    const expiration = Math.max(Math.floor(Date.now() / 1000) + 60, Math.floor(Date.parse(record.created) / 1000) + REPLY_TTL);
    await env.VOTES.put(doneKey, JSON.stringify({ action }), { expiration });
  } catch (err) {
    console.warn(`the solved mark could not be stored: ${err?.message ?? err}`);
  }
  return json(200, { ok: true, solved: true, action });
}
