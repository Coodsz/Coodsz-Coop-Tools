// The free plan budget (README.md, Reply push budget). The Workers free plan allows 100,000 requests and
// 1,000 KV writes a day for the whole account.
//
// The brake: when the day's request count gets high, POST /replies answers with a longer "next" and every
// tool, old and new, asks less often. Only the replies' next changes: mod reports, crash reports and every
// other route are never refused or slowed by it.
//
// The push write cap: past MAX_PUSH_WRITES reply push writes in a day (rr:<code>.push, from the webhook and
// the 30 minute GitHub read), push writes are skipped and the GitHub read serves the answers instead, so
// reply keys and crash reports keep the rest of the 1,000.
//
// KV writes are scarce, so neither is counted in KV one by one. Each running copy of the worker counts in
// memory and adds its count to <prefix><UTC date> = { n, t } in VOTES once it has flushEvery, or after
// FLUSH_AFTER with at least one, while that day's time flushes (t) are under maxTimeFlushes. A flush that
// fails waits FLUSH_BACKOFF before the next try. The counter is read at most once per READ_EVERY per running
// copy. Two copies writing at the same moment can lose one batch, and a copy that stops loses what it had not
// written, so the counts run a little low: both are guides.
//
// The request counter (rq:) counts before routing, so every request counts, junk to any path too: that
// costs nothing extra per request, the batch write is the same. POST /replies adds one per extra report in
// a check (each is a KV read), so the count can pass 100,000 before the requests do: at most about 130
// count flushes and 50 time flushes, about 200 writes a day at worst. The push write counter (pw:) flushes
// every 10 writes or after 10 minutes, up to 20 time flushes: about 30 writes a day.

export const FLUSH_EVERY = 1000;
export const FLUSH_AFTER = 10 * 60 * 1000;
export const FLUSH_BACKOFF = 60 * 1000;
export const MAX_TIME_FLUSHES = 50;
export const READ_EVERY = 60 * 1000;
// [requests that day, seconds of next]: the first that the count passes. The last step (6 hours from
// 95,000) is the safety net that keeps the day's last requests for crash and mod reports.
export const BRAKES = [[95000, 6 * 3600], [90000, 1800], [75000, 900], [50000, 300]];
// Reply push writes a day before they are skipped (the 30 minute GitHub read serves the answers then).
export const MAX_PUSH_WRITES = 100;
export const PUSH_FLUSH_EVERY = 10;
export const PUSH_MAX_TIME_FLUSHES = 20;

function parse(raw) {
  try {
    const v = JSON.parse(raw ?? 'null');
    return { n: Number.isSafeInteger(v?.n) && v.n >= 0 ? v.n : 0, t: Number.isSafeInteger(v?.t) && v.t >= 0 ? v.t : 0 };
  } catch {
    return { n: 0, t: 0 };
  }
}

/** The next the brake asks for at this day's count, or 0 below the first threshold. */
export function brakeFor(count) {
  for (const [above, seconds] of BRAKES) if (count > above) return seconds;
  return 0;
}

/** A day counter for one running copy, kept in KV under prefix + UTC date. clock gives the time (tests pass their own). */
export function makeDayCounter({ prefix, flushEvery, maxTimeFlushes, name }, clock = () => Date.now()) {
  let day = null;
  let pending = 0;
  let lastFlush = 0;
  let retryAt = -Infinity;
  let stored = { n: 0, t: 0 };
  let readAt = -Infinity;
  const key = () => prefix + day;

  const roll = () => {
    const today = new Date(clock()).toISOString().slice(0, 10);
    if (today === day) return;
    day = today;
    pending = 0;
    lastFlush = clock();
    retryAt = -Infinity;
    stored = { n: 0, t: 0 };
    readAt = -Infinity;
  };

  async function flush(env, byTime) {
    const add = pending;
    pending = 0;
    lastFlush = clock();
    try {
      const now = parse(await env.VOTES.get(key()));
      const next = { n: now.n + add, t: now.t + (byTime ? 1 : 0) };
      await env.VOTES.put(key(), JSON.stringify(next), { expirationTtl: 2 * 24 * 3600 });
      stored = next;
      readAt = clock();
    } catch (err) {
      // On purpose: the count waits, the request itself is never affected, and no flush is tried for a while,
      // so a KV that fails is not asked on every request.
      pending += add;
      retryAt = clock() + FLUSH_BACKOFF;
      console.warn(`the ${name} could not be stored: ${err?.message ?? err}`);
    }
  }

  return {
    /** Counts weight units (1 by default). A promise when this flushes the count to KV, else null. Never throws. */
    note(env, weight = 1) {
      try {
        roll();
        pending += Number.isSafeInteger(weight) && weight > 0 ? weight : 0;
        if (!env?.VOTES || clock() < retryAt) return null;
        if (pending >= flushEvery) return flush(env, false);
        if (pending > 0 && clock() - lastFlush >= FLUSH_AFTER && stored.t < maxTimeFlushes) return flush(env, true);
      } catch {
        // A counter that fails never stops a request.
      }
      return null;
    },
    /** This day's count as far as this copy knows: KV's (read at most once per READ_EVERY) plus its own. */
    async count(env) {
      roll();
      if (env?.VOTES && clock() - readAt >= READ_EVERY) {
        readAt = clock();
        try {
          stored = parse(await env.VOTES.get(key()));
        } catch {
          // The last count stays.
        }
      }
      return stored.n + pending;
    },
  };
}

/** The request counter of the brake (rq:). */
export const makeRequestCounter = (clock = () => Date.now()) =>
  makeDayCounter({ prefix: 'rq:', flushEvery: FLUSH_EVERY, maxTimeFlushes: MAX_TIME_FLUSHES, name: 'request counter' }, clock);

/** The reply push write counter (pw:). */
export const makePushWriteCounter = (clock = () => Date.now()) =>
  makeDayCounter({ prefix: 'pw:', flushEvery: PUSH_FLUSH_EVERY, maxTimeFlushes: PUSH_MAX_TIME_FLUSHES, name: 'push write counter' }, clock);

export const defaultCounter = makeRequestCounter();
export const defaultPushWrites = makePushWriteCounter();
