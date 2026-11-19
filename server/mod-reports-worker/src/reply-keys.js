// In tool replies: the per report reply key (docs/design/in-tool-replies.md, The key).
//
// The tool makes 32 random bytes, base64url without padding, and sends them once as summary.replyKey
// on POST /crash. The worker never stores the key: rr:<code> in VOTES holds a random salt per key and
// the hex HMAC-SHA256(secret, "reply:" + code + ":" + salt + ":" + key), so a key only opens its own
// report. The secret is REPLY_SALT, or "reply:" + VOTER_SALT when that is not set.
//
// rr:<code> = { repo, issue, tracked, created, keys: [{ salt, hash }] }, kept 30 days like the zip. It is
// written only when the issue (or the tracking comment) was filed; repo and issue come from the worker's
// own filing, never from the tool. A duplicate report adds its key as its own entry
// rrk:<code>:<sha256 of code and key> = { salt, hash }, up to MAX_REPLY_KEYS in all. Each key has its
// own KV entry, so two duplicates at once never overwrite each other's key.

export const REPLY_KEY = /^[A-Za-z0-9_-]{43}$/;
export const MAX_REPLY_KEYS = 4;
export const REPLY_TTL = 30 * 24 * 3600;
const RECORD_REPO = /^[A-Za-z0-9_.-]{1,100}\/[A-Za-z0-9_.-]{1,100}$/;
// Hashed on a miss, so a wrong key, an unknown code and an expired report take the same work.
const DUMMY_SALT = '00000000000000000000000000000000';

/** True when the worker can take reply keys: Coodsz's GitHub id is set (no answer can be served without it) and a secret exists. */
export function repliesOn(env) {
  return /^\d{1,15}$/.test(String(env.COODSZ_GITHUB_ID ?? '').trim()) && !!(env.REPLY_SALT || env.VOTER_SALT);
}

/** The key from the summary, or null when there is none or it is not the 43 character form (the report still goes through). */
export function parseReplyKey(value) {
  return typeof value === 'string' && REPLY_KEY.test(value) ? value : null;
}

const hex = bytes => [...new Uint8Array(bytes)].map(b => b.toString(16).padStart(2, '0')).join('');

/** An HMAC signer for the reply secret, imported once per request. */
export async function replySigner(env) {
  const secret = env.REPLY_SALT || 'reply:' + (env.VOTER_SALT ?? '');
  const key = await crypto.subtle.importKey('raw', new TextEncoder().encode(secret), { name: 'HMAC', hash: 'SHA-256' }, false, ['sign']);
  return async (code, salt, replyKey) =>
    hex(await crypto.subtle.sign('HMAC', key, new TextEncoder().encode(`reply:${code}:${salt}:${replyKey}`)));
}

/** A new { salt, hash } entry for a key. */
export async function keyEntry(sign, code, replyKey) {
  const salt = hex(crypto.getRandomValues(new Uint8Array(16)));
  return { salt, hash: await sign(code, salt, replyKey) };
}

/** Compares two hex hashes in constant time for their length. */
export function sameHash(a, b) {
  if (typeof a !== 'string' || typeof b !== 'string') return false;
  let diff = a.length ^ b.length;
  for (let i = 0; i < 64; i++) diff |= (a.charCodeAt(i) || 0) ^ (b.charCodeAt(i) || 0);
  return diff === 0;
}

/** The KV name of a duplicate's extra key: found from the key itself, so no list is needed to check one. */
export async function extraKeyName(code, replyKey) {
  const digest = await crypto.subtle.digest('SHA-256', new TextEncoder().encode(`rrk:${code}:${replyKey}`));
  return `rrk:${code}:${hex(digest).slice(0, 32)}`;
}

/**
 * The HMACs openReport runs for every key it checks: one per key place in the record (older records could
 * hold all MAX_REPLY_KEYS there) and one for the duplicate's extra entry, against the dummy salt for the
 * empty places and a missing record, so a miss looks like a hit with a wrong key.
 */
export const REPLY_HMACS = MAX_REPLY_KEYS + 1;

/**
 * The record when the key opens it, else null. Always runs REPLY_HMACS HMACs. rr:<code> is read first;
 * the key's rrk: entry only when no key in the record matched, so the tool that filed the report costs
 * one KV read per check (reply push keeps the answers in the same record). Every miss, a wrong key or a
 * missing or expired report, reads both, so a miss looks the same whether the report exists or not.
 */
export async function openReport(env, sign, code, replyKey) {
  let record = null;
  try {
    record = checkRecord(JSON.parse((await env.VOTES.get('rr:' + code)) ?? 'null'));
  } catch {
    record = null;
  }
  const own = Array.isArray(record?.keys) ? record.keys.slice(0, MAX_REPLY_KEYS) : [];
  let match = false;
  for (let i = 0; i < MAX_REPLY_KEYS; i++) {
    const entry = own[i];
    const hash = await sign(code, typeof entry?.salt === 'string' ? entry.salt : DUMMY_SALT, replyKey);
    if (sameHash(hash, typeof entry?.hash === 'string' ? entry.hash : '') && entry) match = true;
  }
  let extra = null;
  if (!match) {
    try {
      extra = JSON.parse((await env.VOTES.get(await extraKeyName(code, replyKey))) ?? 'null');
    } catch {
      extra = null;
    }
  }
  // The last HMAC runs either way: against the duplicate's entry, or the dummy salt after a match.
  const hash = await sign(code, typeof extra?.salt === 'string' ? extra.salt : DUMMY_SALT, replyKey);
  if (extra && sameHash(hash, typeof extra.hash === 'string' ? extra.hash : '')) match = true;
  return match && record ? record : null;
}

/** The record read from KV, or null when it is missing, odd or older than 30 days. */
export function checkRecord(record, now = Date.now()) {
  if (!record || typeof record !== 'object') return null;
  if (typeof record.repo !== 'string' || !RECORD_REPO.test(record.repo)) return null;
  if (!Number.isInteger(record.issue) || record.issue < 1) return null;
  const created = Date.parse(record.created ?? '');
  if (!Number.isFinite(created) || now - created > REPLY_TTL * 1000) return null;
  return record;
}

/** Writes rr:<code> after the report was filed. True when it was stored. */
export async function storeReplyRecord(env, code, replyKey, { repo, issue, tracked, created }) {
  try {
    const sign = await replySigner(env);
    const record = { repo, issue, tracked: !!tracked, created, keys: [await keyEntry(sign, code, replyKey)] };
    await env.VOTES.put('rr:' + code, JSON.stringify(record), { expirationTtl: REPLY_TTL });
    return true;
  } catch (err) {
    console.warn(`the reply record could not be stored: ${err?.message ?? err}`);
    return false;
  }
}

/**
 * A duplicate report: its key gets its own rrk: entry next to the earlier report's rr: record, up to
 * MAX_REPLY_KEYS in all. True when the key opens that report now (added, or there already). False when
 * the earlier report has no record or is full. The count uses a KV list (only on a duplicate with a key,
 * which is rare); KV list is eventually consistent, so two duplicates at once can pass the cap by one.
 */
export async function addReplyKey(env, code, replyKey) {
  try {
    const sign = await replySigner(env);
    if (await openReport(env, sign, code, replyKey)) return true;
    const record = checkRecord(JSON.parse((await env.VOTES.get('rr:' + code)) ?? 'null'));
    if (!record) return false;
    const own = Array.isArray(record.keys) ? Math.min(record.keys.length, MAX_REPLY_KEYS - 1) : 0;
    const listed = await env.VOTES.list({ prefix: `rrk:${code}:` });
    if (own + (listed?.keys?.length ?? 0) >= MAX_REPLY_KEYS) return false;
    // The extra key keeps the report's first 30 days; it does not stretch them.
    const expiration = Math.floor(Date.parse(record.created) / 1000) + REPLY_TTL;
    await env.VOTES.put(await extraKeyName(code, replyKey), JSON.stringify(await keyEntry(sign, code, replyKey)), { expiration });
    return true;
  } catch (err) {
    console.warn(`the reply key could not be added: ${err?.message ?? err}`);
    return false;
  }
}
