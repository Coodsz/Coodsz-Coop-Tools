// node --test test/   (Node 20+, no packages). The free plan brake: the batched request counter and the
// replies' next it slows down, with a fake clock and a fake KV. Nothing is sent anywhere.
import test from 'node:test';
import assert from 'node:assert/strict';
import { handle } from '../src/index.js';
import { makeRequestCounter, makePushWriteCounter, brakeFor, BRAKES, FLUSH_EVERY, FLUSH_AFTER, FLUSH_BACKOFF, MAX_TIME_FLUSHES, MAX_PUSH_WRITES, READ_EVERY } from '../src/budget.js';
import { makeReplyCache, makeReadCap, NEXT_CHECK, PUSH_NEXT } from '../src/replies.js';
import { storeReplyRecord } from '../src/reply-keys.js';

const KEY = 'A'.repeat(42) + 'b';
const CODE = 'CR-7KQ2M';

function kv() {
  const map = new Map();
  const calls = { get: 0, put: 0 };
  return {
    map, calls,
    get: async k => { calls.get++; return map.has(k) ? map.get(k) : null; },
    put: async (k, v) => { calls.put++; map.set(k, v); },
    list: async () => ({ keys: [], list_complete: true }),
  };
}

const clockAt = start => {
  let now = start;
  const clock = () => now;
  clock.add = ms => { now += ms; };
  return clock;
};
const DAY = Date.parse('2026-11-05T08:00:00Z');

test('the brake thresholds: none up to 50,000, then 5, 15 and 30 minutes, and 6 hours past 95,000', () => {
  assert.deepEqual(BRAKES, [[95000, 21600], [90000, 1800], [75000, 900], [50000, 300]]);
  assert.equal(brakeFor(0), 0);
  assert.equal(brakeFor(50000), 0);
  assert.equal(brakeFor(50001), 300);
  assert.equal(brakeFor(75001), 900);
  assert.equal(brakeFor(90001), 1800);
  assert.equal(brakeFor(95001), 21600);
});

test('the counter writes KV once per 1,000 requests, not per request', async () => {
  const counter = makeRequestCounter(clockAt(DAY));
  const env = { VOTES: kv() };
  for (let i = 0; i < FLUSH_EVERY * 3 + 10; i++) {
    const flushing = counter.note(env);
    if (flushing) await flushing;
  }
  assert.equal(env.VOTES.calls.put, 3);
  assert.deepEqual(JSON.parse(env.VOTES.map.get('rq:2026-11-05')), { n: FLUSH_EVERY * 3, t: 0 });
  // Its own 10 not written yet still count here.
  assert.equal(await counter.count(env), FLUSH_EVERY * 3 + 10);
});

test('after 10 minutes a smaller batch is written, up to 50 time flushes a day for the whole worker', async () => {
  const clock = clockAt(DAY);
  const counter = makeRequestCounter(clock);
  const env = { VOTES: kv() };
  assert.equal(counter.note(env), null);
  clock.add(FLUSH_AFTER - 1);
  assert.equal(counter.note(env), null);
  clock.add(1);
  await counter.note(env);
  assert.deepEqual(JSON.parse(env.VOTES.map.get('rq:2026-11-05')), { n: 3, t: 1 });
  // Other copies used up the day's time flushes: this one waits for a full batch.
  env.VOTES.map.set('rq:2026-11-05', JSON.stringify({ n: 40000, t: MAX_TIME_FLUSHES }));
  clock.add(READ_EVERY);
  await counter.count(env);
  clock.add(FLUSH_AFTER);
  const puts = env.VOTES.calls.put;
  assert.equal(counter.note(env), null);
  assert.equal(env.VOTES.calls.put, puts);
});

test('the counter is read at most once a minute and starts again each UTC day', async () => {
  const clock = clockAt(DAY);
  const counter = makeRequestCounter(clock);
  const env = { VOTES: kv() };
  env.VOTES.map.set('rq:2026-11-05', JSON.stringify({ n: 60000, t: 4 }));
  assert.equal(await counter.count(env), 60000);
  env.VOTES.map.set('rq:2026-11-05', JSON.stringify({ n: 80000, t: 4 }));
  for (let i = 0; i < 20; i++) assert.equal(await counter.count(env), 60000);
  assert.equal(env.VOTES.calls.get, 1);
  clock.add(READ_EVERY);
  assert.equal(await counter.count(env), 80000);
  assert.equal(env.VOTES.calls.get, 2);
  // A new day reads its own key.
  clock.add(24 * 3600 * 1000);
  assert.equal(await counter.count(env), 0);
});

test('a failing KV never stops a request; the count waits for the next flush', async () => {
  const counter = makeRequestCounter(clockAt(DAY));
  const env = { VOTES: { get: async () => { throw new Error('down'); }, put: async () => { throw new Error('down'); } } };
  for (let i = 0; i < FLUSH_EVERY; i++) {
    const flushing = counter.note(env);
    if (flushing) await flushing;
  }
  assert.equal(await counter.count(env), FLUSH_EVERY);
});

// --- Through the worker ---

const env = (count, extra = {}) => ({
  GITHUB_TOKEN: 'test-token', VOTER_SALT: 'salt', VOTES: kv(), COODSZ_GITHUB_ID: '4242', GITHUB_WEBHOOK_SECRET: 'secret',
  REPLY_CACHE: makeReplyCache(), REPLY_READ_CAP: makeReadCap(),
  REQUEST_COUNTER: { note: () => null, count: async () => count }, ...extra,
});
const check = (extra = {}) => new Request('https://crash.example/replies', {
  method: 'POST', body: JSON.stringify({ kind: 'CoodszReplyCheck', schema: 1, reports: [{ code: CODE, key: KEY }], ...extra }),
  headers: { 'CF-Connecting-IP': '203.0.113.9', 'content-type': 'application/json' },
});
const github = async url => new Response(/\/issues\/\d+$/.test(new URL(url).pathname) ? '{"labels":[]}' : '[]', { status: 200 });
const record = e => storeReplyRecord(e, CODE, KEY, { repo: 'Coodsz/Coodsz-Crash-Reports', issue: 41, tracked: false, created: new Date().toISOString() });

test('a busy day slows the replies next for new and old tools, the fast phase too', async () => {
  for (const [count, fast, old] of [[1000, PUSH_NEXT, NEXT_CHECK], [60000, 300, NEXT_CHECK], [80000, 900, NEXT_CHECK], [92000, 1800, 1800], [96000, 21600, 21600]]) {
    const e = env(count);
    await record(e);
    assert.equal((await (await handle(check({ fast: true }), e, github)).json()).next, fast, `fast at ${count}`);
    assert.equal((await (await handle(check(), e, github)).json()).next, old, `old at ${count}`);
  }
  // Push off too: the brake is on the replies, not on push.
  const off = env(95000, { GITHUB_WEBHOOK_SECRET: undefined });
  await record(off);
  assert.equal((await (await handle(check({ fast: true }), off, github)).json()).next, 1800);
});

test('the brake never refuses or slows anything else: health, mod reports and crash reports go on', async () => {
  const e = env(99999);
  assert.equal((await handle(new Request('https://crash.example/health'), e)).status, 200);
  // A mod report and a crash report reach their own checks (a bad body is their 400, not a brake answer).
  const report = await handle(new Request('https://crash.example/report', { method: 'POST', body: '{}', headers: { 'CF-Connecting-IP': '203.0.113.9' } }), e, github);
  assert.equal(report.status, 400);
  const crash = await handle(new Request('https://crash.example/crash', { method: 'POST', body: 'x', headers: { 'CF-Connecting-IP': '203.0.113.9', 'content-type': 'text/plain' } }), e, github);
  assert.equal(crash.status, 400);
});

test('every request is counted, whatever the route', async () => {
  const counter = makeRequestCounter(clockAt(DAY));
  const e = env(0, { REQUEST_COUNTER: counter });
  await record(e);
  await handle(new Request('https://crash.example/health'), e);
  await handle(new Request('https://crash.example/nothing'), e);
  await handle(check({ fast: true }), e, github);
  assert.equal(await counter.count({}), 3);
  // A check with 3 reports counts 3: each report is a KV read.
  const three = new Request('https://crash.example/replies', {
    method: 'POST', body: JSON.stringify({ kind: 'CoodszReplyCheck', schema: 1, fast: true, reports: ['CR-7KQ2M', 'CR-ABCDE', 'CR-23456'].map(code => ({ code, key: KEY })) }),
    headers: { 'CF-Connecting-IP': '203.0.113.9', 'content-type': 'application/json' },
  });
  await handle(three, e, github);
  assert.equal(await counter.count({}), 6);
});

test('after a failed flush the counter waits a minute before it asks KV again', async () => {
  const clock = clockAt(DAY);
  const counter = makeRequestCounter(clock);
  let calls = 0;
  const env = { VOTES: { get: async () => { calls++; throw new Error('down'); }, put: async () => { calls++; throw new Error('down'); } } };
  const run = async n => { for (let i = 0; i < n; i++) { const f = counter.note(env); if (f) await f; } };
  await run(FLUSH_EVERY);
  assert.equal(calls, 1);
  // The next requests, also past the batch size and the 10 minute mark, ask nothing for a minute.
  await run(FLUSH_EVERY * 2);
  assert.equal(calls, 1);
  clock.add(FLUSH_BACKOFF);
  await run(1);
  assert.equal(calls, 2);
  assert.equal(await counter.count({}), FLUSH_EVERY * 3 + 1);
});

test('the push write counter flushes every 10 writes and keeps its own key', async () => {
  const counter = makePushWriteCounter(clockAt(DAY));
  const env = { VOTES: kv() };
  for (let i = 0; i < 25; i++) { const f = counter.note(env); if (f) await f; }
  assert.equal(env.VOTES.calls.put, 2);
  assert.deepEqual(JSON.parse(env.VOTES.map.get('pw:2026-11-05')), { n: 20, t: 0 });
  assert.equal(await counter.count(env), 25);
  assert.equal(MAX_PUSH_WRITES, 100);
});
