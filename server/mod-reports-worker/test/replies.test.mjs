// node --test test/   (Node 20+, no packages). In tool replies and the Discord ping, with a fake
// GitHub, a fake Discord and a fake KV: nothing is sent anywhere.
import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { handle } from '../src/index.js';
import { REPLIES_LINE, needsPing, pingCoodsz, pingText, PING_BURST } from '../src/crash.js';
import { validateReplyCheck, makeReplyCache, makeReadCap, answerItems, lastPage, NEXT_CHECK, NEXT_AFTER_TOKEN_ERROR, MAX_COMMENT_PAGES, MAX_GITHUB_READS_MINUTE } from '../src/replies.js';
import { REPLY_KEY, MAX_REPLY_KEYS, REPLY_HMACS, parseReplyKey, repliesOn, storeReplyRecord, replySigner, openReport, addReplyKey, keyEntry } from '../src/reply-keys.js';
import { parseAnswer, cleanReply, RETRACTED_TEXT, RETRACTED_AUTO_TEXT, MAX_REPLY_TEXT } from '../src/reply-text.js';

const COODSZ = 4242;
const INSTALL = '0123456789abcdef0123456789abcdef';
const EMPTY_ZIP = new Uint8Array([0x50, 0x4b, 0x05, 0x06, ...new Array(18).fill(0)]);
const KEY = 'A'.repeat(42) + 'b';
const KEY2 = 'Z'.repeat(42) + '_';
const WEBHOOK = 'https://discord.com/api/webhooks/123456/secret-token_x';

function kv() {
  const map = new Map();
  const options = new Map();
  return {
    map, options,
    get: async k => (map.has(k) ? map.get(k) : null),
    put: async (k, v, opts) => { map.set(k, v); options.set(k, opts); },
    list: async ({ prefix }) => ({ keys: [...map.keys()].filter(k => k.startsWith(prefix)).map(name => ({ name })), list_complete: true }),
  };
}

const env = (extra = {}) => ({
  GITHUB_TOKEN: 'test-token', VOTER_SALT: 'salt', VOTES: kv(), COODSZ_GITHUB_ID: String(COODSZ),
  REPLY_CACHE: makeReplyCache(), REPLY_READ_CAP: makeReadCap(), ...extra,
});

const summary = (over = {}) => ({
  kind: 'CoodszCrashReport', schema: 1, install: INSTALL, tool: 'coop', side: 'client', reason: 'crash',
  toolVersion: '1.3.1', coop: 'v0.1.6', game: 'v1.4.8',
  crashLine: 'System.NullReferenceException: Object reference not set to an instance of an object.',
  files: [{ name: 'game/Coop_client.log', size: 1200 }], ...over,
});

function postCrash(body) {
  const form = new FormData();
  form.set('summary', JSON.stringify(body));
  form.set('zip', new Blob([EMPTY_ZIP], { type: 'application/zip' }), 'logs.zip');
  return new Request('https://crash.example/crash', { method: 'POST', body: form, headers: { 'CF-Connecting-IP': '203.0.113.9' } });
}

const check = (reports, extra = {}) => new Request('https://crash.example/replies', {
  method: 'POST',
  body: typeof reports === 'string' ? reports : JSON.stringify({ kind: 'CoodszReplyCheck', schema: 1, reports, ...extra }),
  headers: { 'CF-Connecting-IP': '203.0.113.9', 'content-type': 'application/json' },
});

// The fake GitHub for /crash: issues are #41, a tracking issue GET answers open with the tracking label.
function crashGithub(status = 201) {
  const calls = [];
  const fetcher = async (url, init) => {
    calls.push({ url, method: init.method, body: init.body === undefined ? undefined : JSON.parse(init.body) });
    if (url.startsWith('https://discord.com/')) return new Response(null, { status: 204 });
    if (init.method === 'GET' && /\/issues\/8$/.test(url))
      return new Response(JSON.stringify({ number: 8, state: 'open', labels: [{ name: 'tracking' }] }), { status: 200 });
    return new Response(JSON.stringify({ number: 41, id: 999 }), { status });
  };
  return { calls, fetcher };
}

// The fake GitHub for /replies: pages of comments, or a fixed status. The issue itself (read for its
// labels, the status badge) is counted apart in issueCalls, so calls stays the comment pages.
function commentsGithub(comments, status = 200, issue = { number: 41, state: 'open', labels: [] }) {
  const calls = [];
  const issueCalls = [];
  const fetcher = async url => {
    if (status !== 200) {
      calls.push(url);
      return new Response('{}', { status });
    }
    if (/\/issues\/\d+$/.test(new URL(url).pathname)) {
      issueCalls.push(url);
      return new Response(JSON.stringify(issue), { status: 200 });
    }
    calls.push(url);
    const page = Number(new URL(url).searchParams.get('page') ?? '1');
    const last = Math.max(1, Math.ceil(comments.length / 100));
    const headers = last > 1 ? { link: `<https://api.github.com/x?per_page=100&page=${Math.min(page + 1, last)}>; rel="next", <https://api.github.com/x?per_page=100&page=${last}>; rel="last"` } : {};
    return new Response(JSON.stringify(comments.slice((page - 1) * 100, page * 100)), { status: 200, headers });
  };
  return { calls, issueCalls, fetcher };
}

let nextId = 1000;
const comment = (body, { id = COODSZ, type = 'User', login = 'Coodsz', at = '2026-11-02T10:00:00Z', association = 'OWNER' } = {}) =>
  ({ id: nextId++, body, user: { id, login, type }, author_association: association, created_at: at, updated_at: at });

async function withRecord(e, code, { tracked = false, issue = 41, created = new Date().toISOString(), key = KEY } = {}) {
  assert.ok(await storeReplyRecord(e, code, key, { repo: 'Coodsz/Coodsz-Crash-Reports', issue, tracked, created }));
}

const ctx = () => {
  const waits = [];
  return { waits, waitUntil: p => waits.push(p), done: () => Promise.all(waits) };
};

// --- The key and the code ---

test('key and code checks: 43 base64url characters, real codes, 1 to 10 reports, 4 KB at most', async () => {
  assert.ok(REPLY_KEY.test(KEY));
  for (const bad of ['A'.repeat(42), 'A'.repeat(44), 'A'.repeat(42) + '=', 'A'.repeat(42) + '+', 'A'.repeat(42) + '/'])
    assert.equal(parseReplyKey(bad), null, bad);
  assert.equal(parseReplyKey(123), null);
  const ok = { code: 'CR-7KQ2M', key: KEY };
  assert.deepEqual(validateReplyCheck({ kind: 'CoodszReplyCheck', schema: 1, reports: [ok] }), [ok]);
  assert.equal(validateReplyCheck({ kind: 'CoodszReplyCheck', schema: 2, reports: [ok] }), 'unknown kind or schema');
  assert.equal(validateReplyCheck({ kind: 'CoodszReplyCheck', schema: 1, reports: [] }), 'reports');
  assert.equal(validateReplyCheck({ kind: 'CoodszReplyCheck', schema: 1, reports: new Array(11).fill(ok) }), 'reports');
  for (const code of ['CR-7KQ2', 'cr-7kq2m', 'CR-7KQ0M', 'CR-7KQ2MX', 'XX-7KQ2M'])
    assert.equal(validateReplyCheck({ kind: 'CoodszReplyCheck', schema: 1, reports: [{ code, key: KEY }] }), 'code', code);
  assert.equal(validateReplyCheck({ kind: 'CoodszReplyCheck', schema: 1, reports: [{ code: 'CR-7KQ2M', key: 'short' }] }), 'key');

  const e = env();
  for (const req of [check([{ code: 'CR-7KQ2M', key: 'nope' }]), check('not json'), check([ok], { pad: 'x'.repeat(4096) })]) {
    const res = await handle(req, e, commentsGithub([]).fetcher);
    assert.equal(res.status, 400);
    assert.equal((await res.json()).ok, false);
  }
  assert.equal((await handle(new Request('https://crash.example/replies'), e)).status, 405);
});

test('replies are on only with COODSZ_GITHUB_ID and a secret', () => {
  assert.equal(repliesOn({ VOTER_SALT: 's' }), false);
  assert.equal(repliesOn({ VOTER_SALT: 's', COODSZ_GITHUB_ID: 'Coodsz' }), false, 'a login is not an id');
  assert.equal(repliesOn({ VOTER_SALT: 's', COODSZ_GITHUB_ID: '4242' }), true);
  assert.equal(repliesOn({ REPLY_SALT: 'r', COODSZ_GITHUB_ID: '4242' }), true);
  assert.equal(repliesOn({ COODSZ_GITHUB_ID: '4242' }), false);
});

test('a filed report with a key stores only a salted hash in rr:, says replies true and marks the issue', async () => {
  const e = env();
  const gh = crashGithub();
  const res = await (await handle(postCrash(summary({ replyKey: KEY })), e, gh.fetcher)).json();
  assert.equal(res.filed, true);
  assert.equal(res.replies, true);
  const record = JSON.parse(e.VOTES.map.get('rr:' + res.code));
  assert.equal(record.repo, 'Coodsz/Coodsz-Crash-Reports');
  assert.equal(record.issue, 41, 'the number from GitHub\'s answer');
  assert.equal(record.tracked, false);
  assert.ok(Date.parse(record.created) > 0);
  assert.equal(record.keys.length, 1);
  assert.match(record.keys[0].salt, /^[0-9a-f]{32}$/);
  assert.match(record.keys[0].hash, /^[0-9a-f]{64}$/);
  assert.equal(e.VOTES.options.get('rr:' + res.code).expirationTtl, 30 * 24 * 3600);
  const body = gh.calls[0].body.body;
  assert.ok(body.includes(REPLIES_LINE));
  for (const where of [JSON.stringify(gh.calls), JSON.stringify([...e.VOTES.map.entries()].map(([k, v]) => [k, typeof v === 'string' ? v : '']))])
    assert.ok(!where.includes(KEY), 'the key itself is never stored or filed');
  const meta = JSON.stringify([...e.VOTES.options.values()]);
  assert.ok(!meta.includes(KEY), 'nor in the zip metadata');
});

test('rr: is written only when the issue was filed; older tools and odd keys get replies false', async () => {
  const failed = env();
  const res = await (await handle(postCrash(summary({ replyKey: KEY })), failed, crashGithub(500).fetcher)).json();
  assert.equal(res.filed, false);
  assert.equal(res.replies, false);
  assert.ok(![...failed.VOTES.map.keys()].some(k => k.startsWith('rr:')));

  const noToken = env({ GITHUB_TOKEN: undefined });
  const r2 = await (await handle(postCrash(summary({ replyKey: KEY })), noToken, crashGithub().fetcher)).json();
  assert.equal(r2.replies, false);

  const old = env();
  const gh = crashGithub();
  const r3 = await (await handle(postCrash(summary()), old, gh.fetcher)).json();
  assert.equal(r3.replies, false);
  assert.ok(!gh.calls[0].body.body.includes(REPLIES_LINE), 'no "In tool replies: on" line, so Coodsz answers in Discord');

  const odd = env();
  const r4 = await handle(postCrash(summary({ replyKey: 'too-short' })), odd, crashGithub().fetcher);
  assert.equal(r4.status, 200, 'an odd key never refuses the report');
  assert.equal((await r4.json()).replies, false);

  const off = env({ COODSZ_GITHUB_ID: undefined });
  const r5 = await (await handle(postCrash(summary({ replyKey: KEY })), off, crashGithub().fetcher)).json();
  assert.equal(r5.replies, false, 'no answers can be served without Coodsz\'s id');
});

test('a tracked report stores the tracking issue and the comment names the code to use', async () => {
  const e = env();
  const gh = crashGithub();
  const triage = { signature: 'coop-siege-teams-loop', category: 'coop-bug', signaturesVersion: '1', reportNeeded: true, trackingIssue: 8, labels: ['siege'] };
  const res = await (await handle(postCrash(summary({ replyKey: KEY, triage })), e, gh.fetcher)).json();
  assert.equal(res.tracked, 8);
  assert.equal(res.replies, true);
  const record = JSON.parse(e.VOTES.map.get('rr:' + res.code));
  assert.equal(record.issue, 8);
  assert.equal(record.tracked, true);
  const posted = gh.calls.find(c => c.method === 'POST' && c.url.endsWith('/issues/8/comments'));
  assert.ok(posted.body.body.includes(`comment /reply ${res.code} to answer the player`));
});

test('dedupe adds the new key to the earlier report, up to 4 keys, and keeps the 30 days', async () => {
  const e = env();
  const triage = { signature: 'clean-exit', category: 'setup', signaturesVersion: '1', reportNeeded: false, labels: [] };
  const first = await (await handle(postCrash(summary({ replyKey: KEY, triage })), e, crashGithub().fetcher)).json();
  const again = await (await handle(postCrash(summary({ replyKey: KEY2, triage })), e, crashGithub().fetcher)).json();
  assert.equal(again.duplicate, true);
  assert.equal(again.code, first.code);
  assert.equal(again.replies, true);
  const extras = () => [...e.VOTES.map.keys()].filter(k => k.startsWith(`rrk:${first.code}:`));
  assert.equal(JSON.parse(e.VOTES.map.get('rr:' + first.code)).keys.length, 1, 'the record itself is never rewritten');
  assert.equal(extras().length, 1, 'the new key is its own entry');
  assert.ok(e.VOTES.options.get(extras()[0]).expiration > Date.now() / 1000);
  assert.ok(!JSON.stringify([...e.VOTES.map.entries()]).includes(KEY2), 'only a hash of the key');
  const sign = await replySigner(e);
  assert.ok(await openReport(e, sign, first.code, KEY));
  assert.ok(await openReport(e, sign, first.code, KEY2));
  assert.equal(await openReport(e, sign, first.code, '7'.repeat(43)), null);

  // The same key again is not added twice; a full report says replies false.
  await handle(postCrash(summary({ replyKey: KEY2, triage })), e, crashGithub().fetcher);
  assert.equal(extras().length, 1);
  for (let i = 0; i < 2; i++) await handle(postCrash(summary({ replyKey: String(i).repeat(43), triage })), e, crashGithub().fetcher);
  assert.equal(1 + extras().length, MAX_REPLY_KEYS);
  const full = await (await handle(postCrash(summary({ replyKey: '9'.repeat(43), triage })), e, crashGithub().fetcher)).json();
  assert.equal(full.replies, false);

  // An earlier report without a record (an older tool) gives replies false and writes nothing.
  const e2 = env();
  const old = await (await handle(postCrash(summary({ triage })), e2, crashGithub().fetcher)).json();
  const dup = await (await handle(postCrash(summary({ replyKey: KEY, triage })), e2, crashGithub().fetcher)).json();
  assert.equal(dup.code, old.code);
  assert.equal(dup.replies, false);
  assert.ok(!e2.VOTES.map.has('rr:' + old.code));
});

test('keys added at the same moment are all kept, none overwrites another', async () => {
  const e = env();
  await withRecord(e, 'CR-7KQ2M');
  const keys = ['1', '2', '3'].map(c => c.repeat(43));
  const added = await Promise.all(keys.map(k => addReplyKey(e, 'CR-7KQ2M', k)));
  assert.deepEqual(added, [true, true, true]);
  const sign = await replySigner(e);
  for (const k of [KEY, ...keys]) assert.ok(await openReport(e, sign, 'CR-7KQ2M', k), k);
});

test('dedupe is per tool and side, so a key never lands on another tool\'s report', async () => {
  const e = env();
  const triage = { signature: 'clean-exit', category: 'setup', signaturesVersion: '1', reportNeeded: false, labels: [] };
  const coop = await (await handle(postCrash(summary({ replyKey: KEY, triage })), e, crashGithub().fetcher)).json();
  const ad = await (await handle(postCrash(summary({ tool: 'ad1259', replyKey: KEY2, triage })), e, crashGithub().fetcher)).json();
  const server = await (await handle(postCrash(summary({ side: 'server', replyKey: KEY2, triage })), e, crashGithub().fetcher)).json();
  assert.notEqual(ad.code, coop.code);
  assert.notEqual(server.code, coop.code);
  assert.ok(!ad.duplicate && !server.duplicate);
  const sign = await replySigner(e);
  assert.equal(await openReport(e, sign, coop.code, KEY2), null, 'the other tool\'s key does not open the Coop report');

  // A stored duplicate record from another scope (written by hand here) never takes the key.
  const cd = [...e.VOTES.map.keys()].find(k => k.startsWith('cd:') && k.includes(':coop:client:'));
  e.VOTES.map.set(cd, JSON.stringify({ code: coop.code, expires: '2099-01-01', filed: true, tool: 'taom', side: 'client' }));
  const odd = await (await handle(postCrash(summary({ replyKey: '5'.repeat(43), triage })), e, crashGithub().fetcher)).json();
  assert.equal(odd.duplicate, true);
  assert.equal(odd.replies, false);
  assert.equal(await openReport(e, sign, coop.code, '5'.repeat(43)), null);
});

test('an older record with all four keys inside it still opens for every one of them', async () => {
  const e = env();
  const sign = await replySigner(e);
  const keys = [KEY, KEY2, '3'.repeat(43), '4'.repeat(43)];
  const record = { repo: 'Coodsz/Coodsz-Crash-Reports', issue: 41, tracked: false, created: new Date().toISOString(), keys: [] };
  for (const k of keys) record.keys.push(await keyEntry(sign, 'CR-7KQ2M', k));
  e.VOTES.map.set('rr:CR-7KQ2M', JSON.stringify(record));
  for (const k of keys) assert.ok(await openReport(e, sign, 'CR-7KQ2M', k), k);
  assert.equal(await openReport(e, sign, 'CR-7KQ2M', '5'.repeat(43)), null);
});

// --- POST /replies ---

test('a /reply from Coodsz reaches the right key, in request order, with the shape the tools read', async () => {
  const e = env();
  await withRecord(e, 'CR-7KQ2M');
  const gh = commentsGithub([comment('/reply +logs Update the tool to 1.3.1, then send your logs again.')]);
  const res = await handle(check([{ code: 'CR-AAAAA', key: KEY }, { code: 'CR-7KQ2M', key: KEY }]), e, gh.fetcher);
  assert.equal(res.status, 200);
  const body = await res.json();
  assert.deepEqual(Object.keys(body), ['ok', 'next', 'replies']);
  assert.equal(body.next, NEXT_CHECK);
  assert.deepEqual(body.replies.map(r => r.code), ['CR-AAAAA', 'CR-7KQ2M']);
  assert.deepEqual(body.replies[0].items, []);
  const [item] = body.replies[1].items;
  assert.deepEqual(Object.keys(item), ['id', 'at', 'text', 'buttons']);
  assert.match(item.id, /^\d+$/);
  assert.equal(item.at, '2026-11-02T10:00:00Z');
  assert.equal(item.text, 'Update the tool to 1.3.1, then send your logs again.');
  assert.deepEqual(item.buttons, ['send-logs']);
  assert.match(gh.calls[0], /^https:\/\/api\.github\.com\/repos\/Coodsz\/Coodsz-Crash-Reports\/issues\/41\/comments\?per_page=100&since=.+&page=1$/);
});

test('a wrong key, an unknown code and an expired report give identical answers', async () => {
  const e = env();
  await withRecord(e, 'CR-7KQ2M');
  await withRecord(e, 'CR-P2D22', { created: new Date(Date.now() - 31 * 24 * 3600 * 1000).toISOString() });
  const gh = commentsGithub([comment('/reply hello')]);
  const texts = [];
  for (const r of [{ code: 'CR-7KQ2M', key: KEY2 }, { code: 'CR-ZZZZZ', key: KEY }, { code: 'CR-P2D22', key: KEY }]) {
    const res = await handle(check([r]), e, gh.fetcher);
    assert.equal(res.status, 200);
    texts.push((await res.text()).replace(r.code, 'CODE'));
  }
  assert.equal(new Set(texts).size, 1, texts.join('\n'));
  assert.equal(gh.calls.length, 0, 'GitHub is never asked for a miss');
});

test('the dummy hash runs on a miss: a missing record costs the same HMACs as a wrong key', async () => {
  const e = env();
  await withRecord(e, 'CR-7KQ2M');
  const subtle = globalThis.crypto.subtle;
  const original = subtle.sign;
  let signs = 0;
  subtle.sign = function (...args) { signs++; return original.apply(this, args); };
  try {
    signs = 0;
    await handle(check([{ code: 'CR-ZZZZZ', key: KEY }]), e, commentsGithub([]).fetcher);
    const miss = signs;
    signs = 0;
    await handle(check([{ code: 'CR-7KQ2M', key: KEY2 }]), e, commentsGithub([]).fetcher);
    const wrong = signs;
    assert.equal(miss, REPLY_HMACS);
    assert.equal(wrong, REPLY_HMACS);
  } finally {
    delete subtle.sign;
    if (subtle.sign !== original) subtle.sign = original;
  }
});

test('only Coodsz counts: other authors, collaborators, a renamed login and bots are ignored', async () => {
  const e = env();
  await withRecord(e, 'CR-7KQ2M');
  const gh = commentsGithub([
    comment('/reply from Coodsz', { at: '2026-11-01T00:00:00Z' }),
    comment('/reply from a collaborator', { id: 77, login: 'Haterade', association: 'COLLABORATOR' }),
    comment('/reply someone named Coodsz', { id: 78, login: 'Coodsz' }),
    comment('/reply a bot with his id', { type: 'Bot' }),
    comment('/reply a stranger', { id: 79, association: 'NONE' }),
  ]);
  const body = await (await handle(check([{ code: 'CR-7KQ2M', key: KEY }]), e, gh.fetcher)).json();
  assert.equal(body.replies[0].items.length, 1);
  assert.equal(body.replies[0].items[0].text, 'from Coodsz');
});

test('a tracking issue needs the matching code; the own issue ignores a /reply for another code', async () => {
  const comments = [
    comment('/reply for everyone on the tracking issue'),
    comment('/reply CR-ZZZZZ for another report'),
    comment('/reply CR-7KQ2M fixed in the next update'),
    comment('/reply CR-ZZZZZ another one later'),
  ];
  const tracked = env();
  await withRecord(tracked, 'CR-7KQ2M', { tracked: true, issue: 8 });
  const t = await (await handle(check([{ code: 'CR-7KQ2M', key: KEY }]), tracked, commentsGithub(comments).fetcher)).json();
  assert.equal(t.replies[0].items[0].text, 'fixed in the next update');

  const own = env();
  await withRecord(own, 'CR-7KQ2M');
  const o = await (await handle(check([{ code: 'CR-7KQ2M', key: KEY }]), own, commentsGithub(comments.slice(0, 2)).fetcher)).json();
  assert.equal(o.replies[0].items[0].text, 'for everyone on the tracking issue', 'no code on the own issue is fine, another code is not');

  const none = env();
  await withRecord(none, 'CR-7KQ2M', { tracked: true, issue: 8 });
  const n = await (await handle(check([{ code: 'CR-7KQ2M', key: KEY }]), none, commentsGithub(comments.slice(0, 2)).fetcher)).json();
  assert.deepEqual(n.replies[0].items, []);
});

test('the newest answer wins, /retract withdraws it and a newer /reply replaces the note', () => {
  const e = { COODSZ_GITHUB_ID: String(COODSZ) };
  const a = comment('/reply first answer');
  const b = comment('/reply second answer');
  const r = comment('/retract');
  const c = comment('/reply third answer');
  assert.equal(answerItems([a, b], 'CR-7KQ2M', false, e)[0].text, 'second answer');
  const withdrawn = answerItems([a, b, r], 'CR-7KQ2M', false, e);
  assert.equal(withdrawn[0].text, RETRACTED_TEXT);
  assert.equal(withdrawn[0].id, String(r.id), 'a new id, so the tool shows the note once');
  assert.equal(answerItems([a, b, r, c], 'CR-7KQ2M', false, e)[0].text, 'third answer');
  assert.deepEqual(answerItems([r], 'CR-7KQ2M', false, e), [], 'nothing to withdraw');
  assert.deepEqual(answerItems([comment('/reply')], 'CR-7KQ2M', false, e), [], 'an empty /reply is no answer');
  assert.deepEqual(answerItems([comment('Thanks, looking at it. /reply later')], 'CR-7KQ2M', false, e), [], 'only the first line is a command');
  assert.deepEqual(answerItems([comment('/replying soon')], 'CR-7KQ2M', false, e), []);

  // Phase 2: the hidden marker with auto is served like a /reply, and its retract says automatic.
  const auto = comment('<!-- coodsz-answer CR-7KQ2M auto -->\nVerify game files in Steam.');
  assert.equal(answerItems([auto], 'CR-7KQ2M', true, e)[0].text, 'Verify game files in Steam.');
  assert.equal(answerItems([auto, comment('/retract CR-7KQ2M')], 'CR-7KQ2M', true, e)[0].text, RETRACTED_AUTO_TEXT);
  assert.deepEqual(answerItems([comment('<!-- coodsz-answer CR-ZZZZZ -->\nnot yours')], 'CR-7KQ2M', false, e), []);
  assert.deepEqual(answerItems([comment('<!-- coodsz-answer CR-7KQ2M auto -->\nby a bot', { type: 'Bot' })], 'CR-7KQ2M', false, e), []);
});

test('parseAnswer reads the command, the code and the flags from the first line only', () => {
  assert.deepEqual(parseAnswer('/reply CR-7kq2m +logs +nosig Do this.\nAnd that.'),
    { kind: 'reply', code: 'CR-7KQ2M', logs: true, sig: false, nosig: true, auto: false, text: 'Do this.\nAnd that.' });
  assert.deepEqual(parseAnswer('  /REPLY +sig\r\nOn the next line.'), { kind: 'reply', code: null, logs: false, sig: true, nosig: false, auto: false, text: 'On the next line.' });
  assert.equal(parseAnswer('/reply CR-7KQ2M CR-ZZZZZ two codes'), null);
  assert.equal(parseAnswer('/reply CR-7KQ2Mxx text').text, 'CR-7KQ2Mxx text', 'a code glued to text is text');
  assert.equal(parseAnswer('/retract CR-7KQ2M').kind, 'retract');
  assert.equal(parseAnswer('/retract and some words'), null);
  assert.equal(parseAnswer('hello /reply'), null);
  assert.equal(parseAnswer(null), null);
});

test('cleanReply keeps plain words and takes out everything private, linked or marked up', () => {
  const cases = [
    ['Update **the tool** and `restart`.', 'Update the tool and restart.'],
    ['See [the guide](https://evil.example/x) now.', 'See the guide now.'],
    ['Join [our Discord](https://discord.gg/bannerlordcoop).', 'Join our Discord (https://discord.gg/bannerlordcoop).'],
    ['Go to https://discord.gg/bannerlordcoop or https://evil.example/a?b=c.', 'Go to https://discord.gg/bannerlordcoop or.'],
    ['Get v0.1.0 at https://github.com/Coodsz/Coodsz-RealmOfThrones/releases/latest now', 'Get v0.1.0 at https://github.com/Coodsz/Coodsz-RealmOfThrones/releases/latest now'],
    ['Pic ![x](https://i.example/a.png) here', 'Pic here'],
    ['Before\n```\nC:\\Users\\Bob\\secret\n```\nAfter', 'Before\n\nAfter'],
    ['<b>Bold</b> <script>alert(1)</script> <!-- hidden --> text', 'Bold alert(1) text'],
    ['Your id 76561198012345678 and [U:1:12345] are out', 'Your id and are out'],
    ['Server 192.168.1.20:7777 and fe80::1ff:fe23:4567:890a are out', 'Server:7777 and are out'],
    ['Version 1.2.3 stays at 10:30:45', 'Version 1.2.3 stays at 10:30:45'],
    ['Update the tool to 1.2.76.0 first', 'Update the tool to 1.2.76.0 first'],
    ['The join password is hunter2, use it.', 'The join password is, use it.'],
    ['The password was "my secret" before', 'The password was before'],
    ['hunter2 is the password.', 'is the password.'],
    ['“hunter 2” is the join code', 'is the join code'],
    ['passphrase is “open sesame” ok', 'passphrase is ok'],
    ['pass: abc and pw=xyz, join code is 4411', 'pass: and pw=, join code is'],
    ['Look in Steam/userdata/123456789/config', 'Look in Steam/userdata/config'],
    ['johnsmith#1234 on Discord said it, ask jane.doe#0001', 'on Discord said it, ask'],
    ['Coop v1.4.8.3 and Version=3.0.3.0 stay', 'Coop v1.4.8.3 and Version=3.0.3.0 stay'],
    ['Update, then join 85.12.3.4 again', 'Update, then join again'],
    ['Your PC DESKTOP-AB12CD3 and \\\\NAS01\\share are out', 'Your PC and \\share are out'],
    ['Look in D:\\Users\\Bob\\Games', 'Look in C:\\Users\\[user]\\Games'],
    ['Look in C:\\Users\\Bob\\Documents\\Mount and Blade', 'Look in C:\\Users\\[user]\\Documents\\Mount and Blade'],
    ['password: hunter2 and token=ghp_abcdefghijklmnopqrstuvwxyz123456', 'password: and token='],
    ['Mail me at bob@example.com, @Haterade will help', 'Mail me at, will help'],
    ['Same as #12 and Coodsz/Coodsz-TAOM#3 and GH-4', 'Same as and and'],
    ['Like CR-ZZZZZ, your CR-7KQ2M is fixed', 'Like, your CR-7KQ2M is fixed'],
    ['# Heading\n> quoted', 'Heading\nquoted'],
    ['a\u0000b\u202Ec\u0007', 'abc'],
    ['one\n\n\n\n\ntwo', 'one\n\ntwo'],
  ];
  for (const [input, want] of cases) assert.equal(cleanReply(input, 'CR-7KQ2M'), want, input);
  const long = cleanReply('word '.repeat(600), 'CR-7KQ2M');
  assert.ok(long.length <= MAX_REPLY_TEXT);
  assert.ok(long.endsWith('...'));
  assert.equal(cleanReply(undefined), '');
});

test('our own 429s: the address and the key limits answer {ok:false,error,next}', async () => {
  for (const blocked of ['replies:', 'replykey:']) {
    const keys = [];
    const e = env({ REPORT_LIMITER: { limit: async ({ key }) => { keys.push(key); return { success: !key.startsWith(blocked) }; } } });
    await withRecord(e, 'CR-7KQ2M');
    const gh = commentsGithub([comment('/reply hi')]);
    const res = await handle(check([{ code: 'CR-7KQ2M', key: KEY }]), e, gh.fetcher);
    assert.equal(res.status, 429);
    const body = await res.json();
    assert.deepEqual(Object.keys(body), ['ok', 'error', 'next']);
    assert.equal(body.ok, false);
    assert.ok(body.next > 0);
    assert.equal(gh.calls.length, 0);
    assert.ok(keys.every(k => !k.includes(KEY)), 'the limiter key holds a hash prefix, not the key');
  }
});

test('GitHub 401 and 403 give items [] and a 6 hour next, with a warning, and are not cached', async () => {
  for (const status of [401, 403]) {
    const e = env();
    await withRecord(e, 'CR-7KQ2M');
    const warn = console.warn;
    const warnings = [];
    console.warn = m => warnings.push(m);
    try {
      const gh = commentsGithub([], status);
      const body = await (await handle(check([{ code: 'CR-7KQ2M', key: KEY }]), e, gh.fetcher)).json();
      assert.deepEqual(body, { ok: true, next: NEXT_AFTER_TOKEN_ERROR, replies: [{ code: 'CR-7KQ2M', items: [], status: 'looking' }] });
      assert.ok(warnings.some(w => w.includes(String(status)) && w.includes('token')));
      await handle(check([{ code: 'CR-7KQ2M', key: KEY }]), e, gh.fetcher);
      assert.equal(gh.calls.length, 2, 'asked again next time');
    } finally {
      console.warn = warn;
    }
  }
  // A 500 is no token problem: the normal next.
  const e = env();
  await withRecord(e, 'CR-7KQ2M');
  const warn = console.warn;
  console.warn = () => {};
  try {
    const body = await (await handle(check([{ code: 'CR-7KQ2M', key: KEY }]), e, commentsGithub([], 500).fetcher)).json();
    assert.equal(body.next, NEXT_CHECK);
  } finally {
    console.warn = warn;
  }
});

test('answers are cached for 5 minutes and comments are read over up to 5 pages', async () => {
  const e = env();
  await withRecord(e, 'CR-7KQ2M');
  const filler = n => Array.from({ length: n }, () => comment('a player comment', { id: 5 }));
  const gh = commentsGithub([...filler(230), comment('/reply on page three')]);
  const first = await (await handle(check([{ code: 'CR-7KQ2M', key: KEY }]), e, gh.fetcher)).json();
  assert.equal(first.replies[0].items[0].text, 'on page three');
  assert.equal(gh.calls.length, 3);
  // A second key for the same report (a duplicate's), so this check meets the 5 minute cache, not the key's memory.
  assert.ok(await addReplyKey(e, 'CR-7KQ2M', KEY2));
  const again = await (await handle(check([{ code: 'CR-7KQ2M', key: KEY2 }]), e, gh.fetcher)).json();
  // No full either time: three pages could shift between requests (a deleted comment), and a cached list is one
  // copy's memory.
  assert.equal(first.replies[0].full, undefined);
  assert.equal(again.replies[0].full, undefined);
  assert.deepEqual(again, first);
  assert.equal(gh.calls.length, 3, 'served from the cache');

  // A one page issue: the fresh read is full, the cached copy is not.
  const short = env();
  await withRecord(short, 'CR-7KQ2M');
  await addReplyKey(short, 'CR-7KQ2M', KEY2);
  const one = commentsGithub([comment('/reply on page one')]);
  const fresh = await (await handle(check([{ code: 'CR-7KQ2M', key: KEY }]), short, one.fetcher)).json();
  const cached = await (await handle(check([{ code: 'CR-7KQ2M', key: KEY2 }]), short, one.fetcher)).json();
  assert.equal(fresh.replies[0].full, true);
  assert.equal(cached.replies[0].full, undefined);
  assert.equal(cached.replies[0].items[0].text, 'on page one');
  assert.equal(lastPage('<https://api.github.com/x?page=2>; rel="next", <https://api.github.com/x?per_page=100&page=9>; rel="last"'), 9);
  assert.equal(lastPage(null), 1);

  // The cache runs out after 5 minutes.
  const cache = makeReplyCache();
  await cache.put('k', [1], 0);
  assert.deepEqual(await cache.get('k', 299_000), [1]);
  assert.equal(await cache.get('k', 301_000), null);

  // Never more than 5 pages.
  const many = env();
  await withRecord(many, 'CR-7KQ2M');
  const big = commentsGithub(filler(800));
  await handle(check([{ code: 'CR-7KQ2M', key: KEY }]), many, big.fetcher);
  assert.equal(big.calls.length, MAX_COMMENT_PAGES);
});

test('a long issue is read newest first, so a newer /reply or /retract past 500 comments is never missed', async () => {
  const filler = n => Array.from({ length: n }, () => comment('a player comment', { id: 5 }));
  // 801 comments: an old answer on page 1, a newer one on the last page (9).
  const newer = env();
  await withRecord(newer, 'CR-7KQ2M');
  const gh = commentsGithub([comment('/reply old answer'), ...filler(799), comment('/reply newer answer')]);
  const body = await (await handle(check([{ code: 'CR-7KQ2M', key: KEY }]), newer, gh.fetcher)).json();
  assert.equal(body.replies[0].items[0].text, 'newer answer');
  assert.equal(body.replies[0].full, undefined, 'a read that skipped pages is no full list');
  assert.deepEqual(gh.calls.map(u => new URL(u).searchParams.get('page')), ['1', '6', '7', '8', '9']);

  // An answer on page 1 and a /retract past page 5: the retracted answer is never served again.
  const retracted = env();
  await withRecord(retracted, 'CR-7KQ2M');
  const r = commentsGithub([comment('/reply old answer'), ...filler(598), comment('/retract'), ...filler(200)]);
  const rb = await (await handle(check([{ code: 'CR-7KQ2M', key: KEY }]), retracted, r.fetcher)).json();
  assert.deepEqual(rb.replies[0].items, []);
});

test('GitHub reads are capped for the whole worker and each key and code asks GitHub once per interval', async () => {
  let t = 0;
  const cap = makeReadCap(() => t);
  const e0 = {};
  let ok = 0;
  for (let i = 0; i < MAX_GITHUB_READS_MINUTE + 5; i++) if (await cap(e0)) ok++;
  assert.equal(ok, MAX_GITHUB_READS_MINUTE);
  t += 60_000;
  assert.equal(await cap(e0), true, 'a new minute');
  assert.equal(await makeReadCap()({ REPLY_READ_LIMITER: { limit: async ({ key }) => ({ success: key !== 'github-reads' }) } }), false, 'the binding decides when bound');

  // Over the cap: nothing new, nothing remembered, GitHub not asked.
  const e = env({ REPLY_READ_CAP: async () => false });
  await withRecord(e, 'CR-7KQ2M');
  const gh = commentsGithub([comment('/reply hi')]);
  const warn = console.warn;
  console.warn = () => {};
  try {
    const capped = await (await handle(check([{ code: 'CR-7KQ2M', key: KEY }]), e, gh.fetcher)).json();
    assert.deepEqual(capped.replies[0].items, []);
    assert.equal(gh.calls.length, 0);
  } finally {
    console.warn = warn;
  }
  e.REPLY_READ_CAP = makeReadCap();
  const later = await (await handle(check([{ code: 'CR-7KQ2M', key: KEY }]), e, gh.fetcher)).json();
  assert.equal(later.replies[0].items[0].text, 'hi', 'a capped check is not remembered as empty');

  // The same key and code again, even after the answer cache ran out, is the remembered answer.
  const remember = env({ REPLY_CACHE: makeReplyCache() });
  await withRecord(remember, 'CR-7KQ2M');
  const g2 = commentsGithub([comment('/reply hi')]);
  const realNow = Date.now;
  try {
    await handle(check([{ code: 'CR-7KQ2M', key: KEY }]), remember, g2.fetcher);
    const at = realNow();
    Date.now = () => at + 6 * 60 * 1000;
    const again = await (await handle(check([{ code: 'CR-7KQ2M', key: KEY }]), remember, g2.fetcher)).json();
    assert.equal(again.replies[0].items[0].text, 'hi');
    assert.equal(g2.calls.length, 1, 'no second GitHub read within 10 minutes');
  } finally {
    Date.now = realNow;
  }
});

test('without COODSZ_GITHUB_ID nothing is served and GitHub is not asked', async () => {
  const e = env({ COODSZ_GITHUB_ID: '' });
  await withRecord(e, 'CR-7KQ2M');
  const warn = console.warn;
  console.warn = () => {};
  try {
    const gh = commentsGithub([comment('/reply hi')]);
    const body = await (await handle(check([{ code: 'CR-7KQ2M', key: KEY }]), e, gh.fetcher)).json();
    assert.deepEqual(body.replies, [{ code: 'CR-7KQ2M', items: [], status: 'looking' }]);
    assert.equal(gh.calls.length, 0);
  } finally {
    console.warn = warn;
  }
});

// --- The Discord ping ---

const discordCalls = gh => gh.calls.filter(c => c.url.startsWith('https://discord.com/'));

test('an unknown report and a known Coop bug ping once; a known cause, not-a-crash and a dedupe hit do not', async () => {
  const unknown = env({ CRASH_PING_WEBHOOK: WEBHOOK });
  const gh = crashGithub();
  const c = ctx();
  const res = await (await handle(postCrash(summary({ triage: { signature: null, signaturesVersion: '1' } })), unknown, gh.fetcher, c)).json();
  await c.done();
  const [ping] = discordCalls(gh);
  assert.ok(ping, 'an unknown report pings');
  assert.equal(ping.url, WEBHOOK);
  assert.deepEqual(ping.body.allowed_mentions, { parse: [] });
  assert.ok(ping.body.content.startsWith(`**Coop** ${res.code}, unknown crash: System.NullReferenceException`));
  assert.ok(ping.body.content.includes('https://github.com/Coodsz/Coodsz-Crash-Reports/issues/41'));
  assert.ok([...unknown.VOTES.map.keys()].some(k => /^ping:\d+:/.test(k) && k.endsWith(':' + res.code)), 'one entry, written after Discord took it');

  const bug = env({ CRASH_PING_WEBHOOK: WEBHOOK });
  const bg = crashGithub();
  const bc = ctx();
  await handle(postCrash(summary({ triage: { signature: 'coop-siege-teams-loop', category: 'coop-bug', signaturesVersion: '1', reportNeeded: true, trackingIssue: 8, labels: [] } })), bug, bg.fetcher, bc);
  await bc.done();
  assert.equal(discordCalls(bg).length, 1, 'a Coop bug pings');
  assert.ok(discordCalls(bg)[0].body.content.includes('known Coop bug coop-siege-teams-loop'));
  assert.ok(discordCalls(bg)[0].body.content.includes('/issues/8'));

  for (const triage of [
    { signature: 'clean-exit', category: 'setup', signaturesVersion: '1', reportNeeded: false, labels: [] },
    { signature: 'closed-normally', category: 'not-a-crash', signaturesVersion: '1', reportNeeded: false, labels: [] },
  ]) {
    const e = env({ CRASH_PING_WEBHOOK: WEBHOOK });
    const g = crashGithub();
    const k = ctx();
    await handle(postCrash(summary({ triage })), e, g.fetcher, k);
    await k.done();
    assert.equal(discordCalls(g).length, 0, triage.category);
  }

  const dedupe = env({ CRASH_PING_WEBHOOK: WEBHOOK });
  const triage = { signature: 'coop-siege-teams-loop', category: 'coop-bug', signaturesVersion: '1', reportNeeded: false, labels: [] };
  const d1 = ctx();
  await handle(postCrash(summary({ triage })), dedupe, crashGithub().fetcher, d1);
  await d1.done();
  const dg = crashGithub();
  const d2 = ctx();
  const dup = await (await handle(postCrash(summary({ triage })), dedupe, dg.fetcher, d2)).json();
  await d2.done();
  assert.equal(dup.duplicate, true);
  assert.equal(dg.calls.length, 0, 'a dedupe hit never pings');
  assert.equal(needsPing(summary({ triage: null })), true, 'Send my logs and tools without triage count as unknown');
  // Send my logs with a setup hint still pings: the player tried the fix and sent the logs.
  const hint = { signature: 'smart-app-control-blocked', category: 'setup', signaturesVersion: '1', reportNeeded: false, labels: [] };
  assert.equal(needsPing(summary({ reason: 'logs', triage: hint })), true);
  assert.equal(needsPing(summary({ reason: 'crash', triage: hint })), false, 'a crash report with a known fix still does not ping');
});

test('one ping per code, the burst cap names the ones waiting, and no secret means no call', async () => {
  const e = env({ CRASH_PING_WEBHOOK: WEBHOOK });
  const gh = crashGithub();
  const s = summary();
  assert.equal(await pingCoodsz(e, gh.fetcher, { code: 'CR-7KQ2M', summary: s, repo: 'Coodsz/Coodsz-Crash-Reports', issue: 41 }), 'sent');
  assert.equal(await pingCoodsz(e, gh.fetcher, { code: 'CR-7KQ2M', summary: s, repo: 'Coodsz/Coodsz-Crash-Reports', issue: 41 }), 'sent before');
  assert.equal(discordCalls(gh).length, 1);

  const codes = 'ABCDEFGHJKMNPQRSTUVW'.split('').map(c => `CR-2222${c}`);
  const results = [];
  for (const code of codes.slice(0, PING_BURST + 2)) results.push(await pingCoodsz(e, gh.fetcher, { code, summary: s, repo: 'R/r', issue: 1 }));
  assert.equal(results.filter(r => r === 'sent').length, PING_BURST - 1);
  assert.equal(results.filter(r => r === 'capped').length, 3);
  // The next window: the first ping says how many waited.
  const realNow = Date.now;
  Date.now = () => realNow() + 11 * 60 * 1000;
  try {
    await pingCoodsz(e, gh.fetcher, { code: 'CR-33333', summary: s, repo: 'R/r', issue: 1 });
  } finally {
    Date.now = realNow;
  }
  assert.ok(discordCalls(gh).at(-1).body.content.endsWith('and 3 more waiting on GitHub'));

  const quiet = env();
  const q = crashGithub();
  assert.equal(await pingCoodsz(quiet, q.fetcher, { code: 'CR-7KQ2M', summary: s, repo: 'R/r', issue: 1 }), 'off');
  const qc = ctx();
  await handle(postCrash(summary()), quiet, q.fetcher, qc);
  await qc.done();
  assert.equal(discordCalls(q).length, 0);

  const warn = console.warn;
  const warnings = [];
  console.warn = m => warnings.push(m);
  try {
    const odd = env({ CRASH_PING_WEBHOOK: 'https://evil.example/hook' });
    assert.equal(await pingCoodsz(odd, q.fetcher, { code: 'CR-7KQ2M', summary: s, repo: 'R/r', issue: 1 }), 'bad secret');
    assert.ok(!warnings.join(' ').includes('evil.example'), 'the address is never logged');
  } finally {
    console.warn = warn;
  }
});

test('pings at the same moment: the address limiter caps the burst and only sent pings are counted', async () => {
  const counts = new Map();
  const limiter = { limit: async ({ key }) => { counts.set(key, (counts.get(key) ?? 0) + 1); return { success: counts.get(key) <= PING_BURST }; } };
  const e = env({ CRASH_PING_WEBHOOK: WEBHOOK, REPORT_LIMITER: limiter });
  const gh = crashGithub();
  const codes = 'ABCDEFGHJKMN'.split('').map(c => `CR-4444${c}`);
  const results = await Promise.all(codes.map(code => pingCoodsz(e, gh.fetcher, { code, summary: summary(), repo: 'R/r', issue: 1 })));
  assert.equal(results.filter(r => r === 'sent').length, PING_BURST);
  assert.equal(discordCalls(gh).length, PING_BURST);
  assert.equal([...e.VOTES.map.keys()].filter(k => k.startsWith('ping:')).length, PING_BURST, 'one entry per sent ping, none lost');
});

test('the ping holds no Steam id, name, note or log text, and allowed_mentions is empty', async () => {
  const text = pingText('CR-7KQ2M', summary({
    crashLine: 'Crash for 76561198012345678 at C:\\Users\\Bob\\x @everyone <@123> [link](https://x.example) ' + 'y'.repeat(300),
    contact: 'BobTheBuilder', suspectedMod: 'Secret.Mod',
  }), 'https://github.com/R/r/issues/1');
  assert.ok(!text.includes('76561198012345678'));
  assert.ok(!text.includes('Bob\\'));
  assert.ok(!text.includes('BobTheBuilder'));
  assert.ok(!text.includes('Secret.Mod'));
  assert.ok(!text.includes('@'));
  assert.ok(!text.includes('https://x.example'));
  assert.ok(text.split('\n')[0].length < 220, 'one short line');
  assert.equal(pingText('CR-7KQ2M', summary({ crashLine: '' }), 'L').split('\n')[0], '**Coop** CR-7KQ2M, unknown crash: No crash line');
  assert.equal(pingText('CR-7KQ2M', summary({ reason: 'logs', crashLine: '', tool: 'taom' }), 'L').split('\n')[0], '**TAOM** CR-7KQ2M, logs: Logs sent');
  // Send my logs after a setup hint pings too, and is never called a Coop bug.
  const hint = { signature: 'smart-app-control-blocked', category: 'setup', signaturesVersion: '1', reportNeeded: false, labels: [] };
  assert.equal(pingText('CR-7KQ2M', summary({ reason: 'logs', crashLine: '', triage: hint }), 'L').split('\n')[0],
    '**Coop** CR-7KQ2M, logs after the smart-app-control-blocked hint: Logs sent');
});

test('a Discord failure still returns the code to the tool and stores no ping mark', async () => {
  const e = env({ CRASH_PING_WEBHOOK: WEBHOOK });
  const calls = [];
  const fetcher = async (url, init) => {
    calls.push(url);
    if (url.startsWith('https://discord.com/')) return new Response('{}', { status: 500 });
    return new Response(JSON.stringify({ number: 41 }), { status: 201 });
  };
  const throwing = async url => { if (url.startsWith('https://discord.com/')) throw new Error('boom ' + url); return fetcher(url, {}); };
  const warn = console.warn;
  const warnings = [];
  console.warn = m => warnings.push(m);
  try {
    for (const f of [fetcher, throwing]) {
      const c = ctx();
      const res = await handle(postCrash(summary()), e, f, c);
      await c.done();
      assert.equal(res.status, 200);
      const body = await res.json();
      assert.equal(body.filed, true);
      assert.ok(![...e.VOTES.map.keys()].some(k => k.startsWith('ping:')), 'a failed post uses no slot');
    }
    assert.ok(!warnings.join(' ').includes('secret-token'), 'the webhook is never logged');
  } finally {
    console.warn = warn;
  }
});

test('the crash repositories carry an exact copy of reply-text.js', () => {
  const worker = readFileSync(new URL('../src/reply-text.js', import.meta.url));
  const copy = readFileSync(new URL('../../../crash-repos/.github/scripts/reply-text.mjs', import.meta.url));
  assert.ok(worker.equals(copy), 'copy server/mod-reports-worker/src/reply-text.js to crash-repos/.github/scripts/reply-text.mjs');
});
