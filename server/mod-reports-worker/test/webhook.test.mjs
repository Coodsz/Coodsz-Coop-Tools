// node --test test/   (Node 20+, no packages). Reply push: the GitHub webhook and POST /replies reading
// rr:<code>.push, with a fake GitHub and a fake KV. Nothing is sent anywhere.
import test from 'node:test';
import assert from 'node:assert/strict';
import { createHmac } from 'node:crypto';
import { handle } from '../src/index.js';
import { makeReplyCache, makeReadCap, mergeEvents, NEXT_CHECK, PUSH_NEXT, SYNC_INTERVAL, SYNC_RETRY, MAX_PUSH_EVENTS } from '../src/replies.js';
import { storeReplyRecord } from '../src/reply-keys.js';
import { MAX_WEBHOOK_BODY, candidateCodes, applyComment } from '../src/webhook.js';
import { makeRequestCounter, makePushWriteCounter, MAX_PUSH_WRITES } from '../src/budget.js';

const COODSZ = 4242;
const SECRET = 'webhook-test-secret';
const KEY = 'A'.repeat(42) + 'b';
const REPO = 'Coodsz/Coodsz-Crash-Reports';
const CODE = 'CR-7KQ2M';

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
  GITHUB_WEBHOOK_SECRET: SECRET, REPLY_CACHE: makeReplyCache(), REPLY_READ_CAP: makeReadCap(), REQUEST_COUNTER: makeRequestCounter(), PUSH_WRITES: makePushWriteCounter(), ...extra,
});

const sign = (body, secret = SECRET) => 'sha256=' + createHmac('sha256', secret).update(body).digest('hex');

function hook(payload, { event = 'issue_comment', signature, headers = {} } = {}) {
  const body = typeof payload === 'string' ? payload : JSON.stringify(payload);
  return new Request('https://crash.example/github/webhook', {
    method: 'POST', body,
    headers: { 'content-type': 'application/json', 'X-GitHub-Event': event, ...(signature === null ? {} : { 'X-Hub-Signature-256': signature ?? sign(body) }), ...headers },
  });
}

const user = (id = COODSZ, type = 'User') => ({ id, login: id === COODSZ ? 'Coodsz' : 'someone', type });
let nextId = 5000;
const comment = (body, { id = nextId++, author = user(), at = '2026-11-02T10:00:00Z', updated = at } = {}) =>
  ({ id, body, user: author, created_at: at, updated_at: updated });
const ownIssue = (number = 41, code = CODE, labels = []) => ({
  number, title: `Coop and Friends game crash, NullReferenceException [${code}]`,
  body: `<!-- coodsz-crash-report v1 -->\nReport code **${code}**\n\n## What happened`, labels: labels.map(name => ({ name })),
});
const delivery = (action, c, { issue = ownIssue(), repo = REPO, changes } = {}) =>
  ({ action, comment: c, issue, repository: { full_name: repo }, sender: user(), ...(changes ? { changes } : {}) });

const check = (reports, extra = {}) => new Request('https://crash.example/replies', {
  method: 'POST',
  body: JSON.stringify({ kind: 'CoodszReplyCheck', schema: 1, reports, ...extra }),
  headers: { 'CF-Connecting-IP': '203.0.113.9', 'content-type': 'application/json' },
});

async function withRecord(e, code = CODE, { tracked = false, issue = 41 } = {}) {
  assert.ok(await storeReplyRecord(e, code, KEY, { repo: REPO, issue, tracked, created: new Date().toISOString() }));
}

// A fake GitHub that counts every call; comments and labels can change between calls.
function github(comments = [], labels = []) {
  const state = { comments, labels, calls: [] };
  state.fetcher = async url => {
    state.calls.push(url);
    if (/\/issues\/\d+$/.test(new URL(url).pathname))
      return new Response(JSON.stringify({ number: 41, labels: state.labels.map(name => ({ name })) }), { status: 200 });
    return new Response(JSON.stringify(state.comments), { status: 200 });
  };
  return state;
}
const noGithub = async url => assert.fail(`GitHub was called: ${url}`);

const repliesOf = async (e, fetcher, extra = { fast: true }, code = CODE) => (await handle(check([{ code, key: KEY }], extra), e, fetcher)).json();
const state = (e, code = CODE) => JSON.parse(e.VOTES.map.get('rr:' + code) ?? 'null')?.push ?? null;

// The reply cache with its puts recorded. A new one stands for the sync marker being gone: the interval
// passed, or the check ran on another running copy of the worker.
function spyCache() {
  const inner = makeReplyCache();
  const puts = [];
  return {
    puts,
    get: (k, now) => inner.get(k, now),
    put: (k, v, now, seconds) => { puts.push({ k, seconds }); return inner.put(k, v, now, seconds); },
  };
}
const lastMarker = cache => cache.puts.filter(p => p.k.startsWith('sync/')).at(-1);

// Counts KV calls by key.
function counted(e) {
  const gets = [];
  const puts = [];
  const get = e.VOTES.get;
  const put = e.VOTES.put;
  e.VOTES.get = async k => { gets.push(k); return get(k); };
  e.VOTES.put = async (k, v, o) => { puts.push(k); return put(k, v, o); };
  return { gets, puts, reset: () => { gets.length = 0; puts.length = 0; } };
}

// --- The signature and the body ---

test('a good signature is taken; a wrong, missing or odd one is 401, and no secret is 503', async () => {
  const e = env();
  const ping = await handle(hook({ zen: 'hi' }, { event: 'ping' }), e);
  assert.equal(ping.status, 200);
  assert.deepEqual(await ping.json(), { ok: true, pong: true });

  const body = JSON.stringify(delivery('created', comment('/reply hello')));
  for (const signature of [null, sign(body, 'another secret'), 'sha256=' + 'a'.repeat(64), 'sha1=' + 'a'.repeat(40), sign(body).toUpperCase().replace('SHA256', 'sha256').slice(0, 70), 'nonsense']) {
    const res = await handle(hook(body, { signature }), e);
    assert.equal(res.status, 401, String(signature));
    assert.equal((await res.json()).ok, false);
  }
  // The signature covers the exact bytes: one changed byte fails.
  assert.equal((await handle(hook(body.replace('hello', 'hellO'), { signature: sign(body) }), e)).status, 401);
  // Upper case hex is still the same signature.
  assert.equal((await handle(hook(body, { signature: 'sha256=' + sign(body).slice(7).toUpperCase() }), e)).status, 200);

  for (const off of [env({ GITHUB_WEBHOOK_SECRET: undefined }), env({ GITHUB_WEBHOOK_SECRET: '' })])
    assert.equal((await handle(hook(body), off)).status, 503);
  assert.equal((await handle(new Request('https://crash.example/github/webhook'), e)).status, 405);
});

test('a body over 1 MB is 413, by its length or by what is read, and is never stored', async () => {
  const e = env();
  await withRecord(e);
  const big = JSON.stringify(delivery('created', comment('/reply ' + 'x'.repeat(MAX_WEBHOOK_BODY))));
  assert.equal((await handle(hook(big), e)).status, 413);
  // No length header: the stream stops at the cap.
  const stream = new ReadableStream({
    start(c) {
      for (let i = 0; i < 20; i++) c.enqueue(new Uint8Array(64 * 1024).fill(0x20));
      c.close();
    },
  });
  const res = await handle(new Request('https://crash.example/github/webhook', {
    method: 'POST', body: stream, duplex: 'half', headers: { 'X-GitHub-Event': 'issue_comment', 'X-Hub-Signature-256': 'sha256=' + '0'.repeat(64) },
  }), e);
  assert.equal(res.status, 413);
  assert.equal(state(e), null);
  // Just under the cap is read and checked.
  const fits = JSON.stringify(delivery('created', comment('/reply ok'))).padEnd(MAX_WEBHOOK_BODY - 10, ' ');
  assert.equal((await handle(hook(fits), e)).status, 200);
});

test('the webhook limit answers 429 before the body is read', async () => {
  const body = () => new ReadableStream({ pull(c) { c.enqueue(new Uint8Array(10)); c.close(); } });
  const sent = [];
  const asked = [];
  const refuse = { limit: async ({ key }) => { asked.push(key); return { success: false }; } };
  const req = () => sent[sent.push(new Request('https://crash.example/github/webhook', {
    method: 'POST', body: body(), duplex: 'half', headers: { 'CF-Connecting-IP': '198.51.100.7', 'X-GitHub-Event': 'issue_comment' },
  })) - 1];
  // WEBHOOK_LIMITER when it is bound.
  let res = await handle(req(), env({ WEBHOOK_LIMITER: refuse, REPORT_LIMITER: { limit: async () => assert.fail('REPORT_LIMITER asked') } }));
  assert.equal(res.status, 429);
  assert.deepEqual(asked, ['webhook:198.51.100.7']);
  // Without it, REPORT_LIMITER under the webhook: key.
  res = await handle(req(), env({ REPORT_LIMITER: refuse }));
  assert.equal(res.status, 429);
  assert.deepEqual(asked, ['webhook:198.51.100.7', 'webhook:198.51.100.7']);
  assert.ok(sent.every(r => !r.bodyUsed && !r.body.locked), 'the body is never read');
  // Under the limit the delivery goes on as usual.
  res = await handle(hook({ zen: 'hi' }, { event: 'ping' }), env({ WEBHOOK_LIMITER: { limit: async () => ({ success: true }) } }));
  assert.equal(res.status, 200);
});

// --- What is ignored ---

test('other events, actions, repositories, authors, pull requests and non /reply comments are ignored with a 200', async () => {
  const e = env();
  await withRecord(e);
  const cases = [
    [hook(delivery('created', comment('/reply hi')), { event: 'push' }), 'event'],
    [hook(delivery('created', comment('/reply hi')), { event: 'issues' }), 'action'],
    [hook(delivery('pinned', comment('/reply hi'))), 'action'],
    [hook(delivery('created', comment('/reply hi'), { repo: 'Coodsz/Coodsz-Coop-and-Friends' })), 'repository'],
    [hook(delivery('created', comment('/reply hi'), { repo: 'Someone/Coodsz-Crash-Reports' })), 'repository'],
    [hook(delivery('created', comment('/reply hi', { author: user(77) }))), 'author'],
    [hook(delivery('created', comment('/reply hi', { author: user(COODSZ, 'Bot') }))), 'author'],
    [hook(delivery('created', comment('/reply hi', { author: { id: String(COODSZ), type: 'User' } }))), 'author'],
    [hook(delivery('created', comment('/reply hi'), { issue: { ...ownIssue(), pull_request: { url: 'x' } } })), 'issue'],
    [hook(delivery('created', comment('Looking at it now, will /reply soon'))), 'no answer'],
    [hook(delivery('created', comment('reply: try again'))), 'no answer'],
    [hook('not json'), 'not JSON'],
  ];
  for (const [req, why] of cases) {
    const res = await handle(req, e);
    assert.equal(res.status, 200, why);
    assert.deepEqual(await res.json(), { ok: true, ignored: why });
  }
  assert.equal(state(e), null);
});

test('a comment is stored only for a report whose rr: record points at this very issue', async () => {
  const e = env();
  await withRecord(e, CODE, { issue: 41 });
  await withRecord(e, 'CR-ABCDE', { issue: 99 });
  // A /reply naming a report that lives on another issue does nothing (the issue's own report is not it either).
  let res = await handle(hook(delivery('created', comment('/reply CR-ABCDE hi'))), e);
  assert.deepEqual(await res.json(), { ok: true, ignored: 'unchanged' });
  // An issue that claims a code it was not filed for does nothing either.
  res = await handle(hook(delivery('created', comment('/reply hi'), { issue: ownIssue(42, CODE) })), e);
  assert.deepEqual(await res.json(), { ok: true, ignored: 'no report' });
  // An unknown code.
  res = await handle(hook(delivery('created', comment('/reply hi'), { issue: ownIssue(41, 'CR-ZZZZZ') })), e);
  assert.deepEqual(await res.json(), { ok: true, ignored: 'no report' });
  assert.equal(state(e), null);
  assert.equal(state(e, 'CR-ABCDE'), null);
});

// --- Created, edited, deleted ---

test('created, edited and deleted: the answer is stored cleaned, replaced and taken out, and /replies serves it with no GitHub call', async () => {
  const e = env();
  await withRecord(e);
  // The first check reads GitHub once (no answer yet) and starts the interval.
  const gh = github();
  let body = await repliesOf(e, gh.fetcher);
  assert.equal(gh.calls.length, 2);
  assert.deepEqual(body.replies[0].items, []);
  assert.equal(body.next, PUSH_NEXT);

  const c = comment('/reply +logs Update the tool to 1.3.5, password: hunter2 and mail me at a@b.com');
  let res = await handle(hook(delivery('created', c)), e);
  assert.deepEqual(await res.json(), { ok: true, stored: 1 });
  body = await repliesOf(e, noGithub);
  assert.equal(body.replies[0].status, 'answered');
  assert.deepEqual(body.replies[0].items, [{ id: String(c.id), at: c.updated_at, text: 'Update the tool to 1.3.5, password: and mail me at', buttons: ['send-logs'] }]);

  // Edited: the same id with the new text and date.
  const edited = { ...c, body: '/reply Turn off RBM, then try again.', updated_at: '2026-11-02T11:00:00Z' };
  res = await handle(hook(delivery('edited', edited, { changes: { body: { from: c.body } } })), e);
  assert.deepEqual(await res.json(), { ok: true, stored: 1 });
  body = await repliesOf(e, noGithub);
  assert.deepEqual(body.replies[0].items, [{ id: String(c.id), at: '2026-11-02T11:00:00Z', text: 'Turn off RBM, then try again.', buttons: [] }]);

  // Edited into something that is no answer: it goes.
  const plain = { ...edited, body: 'never mind', updated_at: '2026-11-02T11:05:00Z' };
  await handle(hook(delivery('edited', plain, { changes: { body: { from: edited.body } } })), e);
  assert.deepEqual((await repliesOf(e, noGithub)).replies[0].items, []);

  // The 11:00 edit delivered late, after the 11:05 one: it stays out (the tombstone is newer).
  res = await handle(hook(delivery('edited', edited, { changes: { body: { from: c.body } } })), e);
  assert.deepEqual(await res.json(), { ok: true, ignored: 'unchanged' });
  assert.deepEqual((await repliesOf(e, noGithub)).replies[0].items, []);

  // Back to an answer, then deleted.
  const again = { ...edited, body: '/reply Turn off RBM and Diplomacy.', updated_at: '2026-11-02T11:10:00Z' };
  await handle(hook(delivery('edited', again, { changes: { body: { from: plain.body } } })), e);
  assert.equal((await repliesOf(e, noGithub)).replies[0].items[0].text, 'Turn off RBM and Diplomacy.');
  // An older edit delivered late does not show older text.
  res = await handle(hook(delivery('edited', edited, { changes: { body: { from: c.body } } })), e);
  assert.deepEqual(await res.json(), { ok: true, ignored: 'unchanged' });
  assert.equal((await repliesOf(e, noGithub)).replies[0].items[0].text, 'Turn off RBM and Diplomacy.');
  res = await handle(hook(delivery('deleted', again)), e);
  assert.deepEqual(await res.json(), { ok: true, stored: 1 });
  body = await repliesOf(e, noGithub);
  assert.deepEqual(body.replies[0].items, []);
  assert.equal(body.replies[0].status, 'looking');
  // GitHub sends the created and an edit again after the delete: the deleted answer does not come back.
  for (const late of [delivery('created', c), delivery('edited', { ...again, updated_at: '2026-11-03T09:00:00Z' }, { changes: { body: { from: plain.body } } })]) {
    res = await handle(hook(late), e);
    assert.deepEqual(await res.json(), { ok: true, ignored: 'unchanged' });
  }
  assert.deepEqual((await repliesOf(e, noGithub)).replies[0].items, []);
  assert.deepEqual(state(e).gone, [{ id: String(c.id), at: '9999-12-31T23:59:59Z' }]);
  // The record keeps its keys and the report's 30 days, no more.
  const opts = e.VOTES.options.get('rr:' + CODE);
  assert.equal(JSON.parse(e.VOTES.map.get('rr:' + CODE)).keys.length, 1);
  const created = JSON.parse(e.VOTES.map.get('rr:' + CODE)).created;
  assert.equal(opts.expiration, Math.floor(Date.parse(created) / 1000) + 30 * 24 * 3600);
});

test('newest answer wins, /retract, the player\'s follow up and labels through the webhook, like the GitHub read', async () => {
  const e = env();
  await withRecord(e);
  await repliesOf(e, github().fetcher);
  const first = comment('/reply First answer.');
  const second = comment('/reply Second answer.', { at: '2026-11-02T12:00:00Z' });
  await handle(hook(delivery('created', second)), e);
  // An older comment arriving late still sorts before the newer one.
  await handle(hook(delivery('created', first)), e);
  assert.equal((await repliesOf(e, noGithub)).replies[0].items[0].text, 'Second answer.');

  await handle(hook(delivery('created', comment('/retract'))), e);
  let r = (await repliesOf(e, noGithub)).replies[0];
  assert.equal(r.items[0].text, 'That answer was withdrawn. Coodsz is looking into it.');
  assert.equal(r.status, 'looking');

  await handle(hook(delivery('created', comment('/reply Third answer.'))), e);
  // The worker's own follow up marker (by Coodsz's account, the crash token is his) puts it back to looking.
  await handle(hook(delivery('created', comment(`<!-- coodsz-followup ${CODE} -->\n**Still broken: the player answered on ${CODE}**\nThe player tried all the steps.`))), e);
  r = (await repliesOf(e, noGithub)).replies[0];
  assert.equal(r.items[0].text, 'Third answer.');
  assert.equal(r.status, 'looking');

  // A comment brings the issue's labels: fixed-in wins.
  await handle(hook(delivery('created', comment('Released, see the notes.'), { issue: ownIssue(41, CODE, ['crash-report', 'fixed-in-v1.3.5']) })), e);
  // A plain comment changes no answer and no state is written for it ...
  assert.equal((await repliesOf(e, noGithub)).replies[0].status, 'looking');
  // ... but an answer carries the labels in.
  await handle(hook(delivery('created', comment('/reply Fixed now.'), { issue: ownIssue(41, CODE, ['crash-report', 'fixed-in-v1.3.5']) })), e);
  r = (await repliesOf(e, noGithub)).replies[0];
  assert.equal(r.status, 'fixed');
  assert.equal(r.fixedIn, '1.3.5');
});

test('a tracking issue needs the code; an edit that changes the code moves the answer', async () => {
  const e = env();
  const tracking = { number: 8, title: 'Known bug: siege crash', body: 'Tracking issue', labels: [{ name: 'tracking' }] };
  await withRecord(e, CODE, { issue: 8, tracked: true });
  await withRecord(e, 'CR-ABCDE', { issue: 8, tracked: true });
  for (const code of [CODE, 'CR-ABCDE']) await repliesOf(e, github().fetcher, { fast: true }, code);

  let res = await handle(hook(delivery('created', comment('/reply No code here.'), { issue: tracking })), e);
  assert.deepEqual(await res.json(), { ok: true, ignored: 'no answer' });

  const c = comment(`/reply ${CODE} For you.`);
  res = await handle(hook(delivery('created', c, { issue: tracking })), e);
  assert.deepEqual(await res.json(), { ok: true, stored: 1 });
  assert.equal((await repliesOf(e, noGithub)).replies[0].items[0].text, `For you.`);

  const moved = { ...c, body: '/reply CR-ABCDE For you.', updated_at: '2026-11-02T13:00:00Z' };
  res = await handle(hook(delivery('edited', moved, { issue: tracking, changes: { body: { from: c.body } } })), e);
  assert.deepEqual(await res.json(), { ok: true, stored: 2 });
  assert.deepEqual((await repliesOf(e, noGithub)).replies[0].items, []);
  assert.equal((await repliesOf(e, noGithub, { fast: true }, 'CR-ABCDE')).replies[0].items[0].text, 'For you.');
});

test('the codes a comment can be for: the named one, the marker\'s, and the issue\'s own only for an answer', () => {
  const issue = ownIssue(41, CODE);
  assert.deepEqual(candidateCodes(['/reply hi'], issue), [CODE]);
  assert.deepEqual(candidateCodes(['/reply CR-ABCDE hi'], issue), ['CR-ABCDE', CODE]);
  assert.deepEqual(candidateCodes(['just talking'], issue), []);
  assert.deepEqual(candidateCodes([`<!-- coodsz-followup CR-ABCDE -->\n**Still broken: the player answered on CR-ABCDE**`], issue), ['CR-ABCDE']);
  assert.deepEqual(candidateCodes(['plain now', '/reply old answer'], issue), [CODE]);
  assert.deepEqual(candidateCodes(['/reply hi'], { title: 'no code', body: 'none' }), []);
});

// --- The slow GitHub read behind it ---

test('the fallback reads GitHub once per interval, catches a missed webhook, and retries sooner after a passing failure', async () => {
  const e = env({ REPLY_CACHE: spyCache() });
  await withRecord(e);
  const kv = counted(e);
  const gh = github();
  await repliesOf(e, gh.fetcher);
  assert.equal(gh.calls.length, 2);
  // The next read is due in the cache, not in KV, and nothing changed, so nothing was written.
  assert.deepEqual(lastMarker(e.REPLY_CACHE), { k: `sync/${REPO}/41/${CODE}`, seconds: SYNC_INTERVAL });
  assert.deepEqual(kv.puts, []);
  // Inside the interval: no GitHub call, however often the tool asks.
  for (let i = 0; i < 5; i++) await repliesOf(e, noGithub);

  // A /reply whose webhook never came, and a label: found once the marker is gone, and written once.
  gh.comments = [comment('/reply Missed by the webhook.')];
  gh.labels = ['crash-report', 'fixed-in-v1.3.5'];
  assert.deepEqual((await repliesOf(e, noGithub)).replies[0].items, []);
  e.REPLY_CACHE = spyCache();
  let r = (await repliesOf(e, gh.fetcher)).replies[0];
  assert.equal(gh.calls.length, 4);
  assert.equal(r.items[0].text, 'Missed by the webhook.');
  assert.equal(r.status, 'fixed');
  assert.deepEqual(kv.puts, ['rr:' + CODE]);
  assert.deepEqual(state(e).labels, ['fixed-in-v1.3.5'], 'only the labels the status uses are kept');
  // The same again from GitHub: no write.
  e.REPLY_CACHE = spyCache();
  await repliesOf(e, gh.fetcher);
  assert.deepEqual(kv.puts, ['rr:' + CODE]);

  // GitHub fails for a passing reason: what the record has is served, and the next read comes after SYNC_RETRY.
  e.REPLY_CACHE = spyCache();
  const down = async () => new Response('{}', { status: 502 });
  r = (await repliesOf(e, down)).replies[0];
  assert.equal(r.items[0].text, 'Missed by the webhook.');
  assert.equal(lastMarker(e.REPLY_CACHE).seconds, SYNC_RETRY);
  // A token error waits a whole interval; the webhook needs no token.
  e.REPLY_CACHE = spyCache();
  const body = await repliesOf(e, async () => new Response('{}', { status: 401 }));
  assert.equal(body.next, PUSH_NEXT);
  assert.equal(body.replies[0].items[0].text, 'Missed by the webhook.');
  assert.equal(lastMarker(e.REPLY_CACHE).seconds, SYNC_INTERVAL);
  assert.deepEqual(kv.puts, ['rr:' + CODE], 'a failed read writes nothing');
});

test('a plain check reads one KV key per report and writes nothing; a delivery that changes nothing writes nothing', async () => {
  const e = env();
  const OTHER = 'CR-ABCDE';
  await withRecord(e);
  await withRecord(e, OTHER, { issue: 42 });
  const c = comment('/reply Stored.');
  await handle(hook(delivery('created', c)), e);
  const kv = counted(e);
  const reports = [{ code: CODE, key: KEY }, { code: OTHER, key: KEY }];
  // The first check reads GitHub for each report. It finds what the webhook stored, so the only write marks that
  // state as read from GitHub (synced, once per report). The other report has nothing, and writes nothing.
  const onlyOwn = async url => (new URL(url).pathname.includes('/issues/41') ? github([c]) : github()).fetcher(url);
  await handle(check(reports, { fast: true }), e, onlyOwn);
  assert.deepEqual(kv.puts, ['rr:' + CODE]);
  assert.equal(state(e).synced, true);
  assert.equal(state(e, OTHER), null);
  kv.reset();
  for (let i = 0; i < 3; i++) {
    const body = await (await handle(check(reports, { fast: true }), e, noGithub)).json();
    assert.equal(body.replies[0].items[0].text, 'Stored.');
  }
  assert.deepEqual(kv.gets, ['rr:' + CODE, 'rr:' + OTHER, 'rr:' + CODE, 'rr:' + OTHER, 'rr:' + CODE, 'rr:' + OTHER]);
  assert.deepEqual(kv.puts, []);

  // GitHub sends the same delivery again, and an edit that only changes labels the status does not use.
  kv.reset();
  let res = await handle(hook(delivery('created', c)), e);
  assert.deepEqual(await res.json(), { ok: true, ignored: 'unchanged' });
  res = await handle(hook(delivery('edited', c, { issue: ownIssue(41, CODE, ['crash-report', 'known']), changes: { body: { from: c.body } } })), e);
  assert.deepEqual(await res.json(), { ok: true, ignored: 'unchanged' });
  assert.deepEqual(kv.puts, []);
  // The delivery reads only rr: for the codes it can be for.
  assert.ok(kv.gets.length > 0 && kv.gets.every(k => k === 'rr:' + CODE));

  // A wrong key costs the same reads as a missing report: rr: and the duplicate key entry.
  kv.reset();
  await handle(check([{ code: CODE, key: 'Q'.repeat(43) }, { code: 'CR-ZZZZZ', key: 'Q'.repeat(43) }], { fast: true }), e, noGithub);
  assert.equal(kv.gets.length, 4);
  assert.equal(kv.gets.filter(k => k.startsWith('rrk:')).length, 2);
  assert.deepEqual(kv.puts, []);
});

test('a webhook before the first check still reads GitHub once, and keeps a comment newer than what that read saw', async () => {
  const e = env();
  await withRecord(e);
  const early = comment('/reply Early answer.');
  await handle(hook(delivery('created', early)), e);
  assert.equal(state(e).events.length, 1);
  // GitHub has not listed it yet: the webhook's newer comment stays.
  const older = comment('/reply Older one.');
  older.id = early.id - 1;
  const gh = github([older]);
  const r = (await repliesOf(e, gh.fetcher)).replies[0];
  assert.equal(gh.calls.length, 2);
  assert.equal(r.items[0].text, 'Early answer.');

  // mergeEvents on its own: older webhook events are GitHub's to decide, newer ones stay; ids compare as numbers.
  const ev = id => ({ id, at: 'x', kind: 'reply', text: 't' + id });
  assert.deepEqual(mergeEvents([ev('100')], [ev('99'), ev('100'), ev('1000')], 100).map(x => x.id), ['100', '1000']);
  const many = Array.from({ length: 30 }, (_, i) => ev(String(i + 1)));
  assert.equal(mergeEvents(many, [], 30).length, MAX_PUSH_EVENTS);
  assert.equal(mergeEvents(many, [], 30).at(-1).id, '30');
});

test('junk in rr:<code>.push is dropped, never served', async () => {
  const e = env();
  await withRecord(e);
  const record = JSON.parse(e.VOTES.map.get('rr:' + CODE));
  e.VOTES.map.set('rr:' + CODE, JSON.stringify({
    ...record,
    push: {
      labels: ['fixed-in-v1.3.5', 5],
      events: [{ id: 'x', kind: 'reply', at: 'a', text: 'bad id' }, { id: '7', kind: 'other' }, { id: '8', kind: 'reply', at: 'a', text: 'y'.repeat(1501) }, { id: '9', kind: 'reply', at: 'a', text: 'Good.' }],
    },
  }));
  await e.REPLY_CACHE.put(`sync/${REPO}/41/${CODE}`, { at: 1 });
  const r = (await repliesOf(e, noGithub)).replies[0];
  assert.deepEqual(r.items.map(i => i.text), ['Good.']);
  assert.equal(r.status, 'fixed');
});

// --- Older tools and an older setup ---

test('old tools: the same response shape and a 30 minute next; push off keeps the GitHub read and its memory', async () => {
  const e = env();
  await withRecord(e);
  await handle(hook(delivery('created', comment('/reply For an old tool.'))), e);
  const gh = github([comment('/reply For an old tool.')]);
  // A v1.3.4 tool sends no "fast": it gets NEXT_CHECK, and the shape it reads.
  const res = await handle(check([{ code: CODE, key: KEY }]), e, gh.fetcher);
  const body = await res.json();
  assert.deepEqual(Object.keys(body), ['ok', 'next', 'replies']);
  assert.equal(body.next, NEXT_CHECK);
  // "full" is new in v1.3.5; v1.3.4 tools read only the fields they know.
  assert.deepEqual(Object.keys(body.replies[0]), ['code', 'items', 'status', 'full']);
  assert.deepEqual(Object.keys(body.replies[0].items[0]), ['id', 'at', 'text', 'buttons']);
  // A new tool gets the short wait.
  assert.equal((await repliesOf(e, noGithub)).next, PUSH_NEXT);

  // Push off (no secret, the worker before this change): "fast" changes nothing, GitHub is read as before.
  const off = env({ GITHUB_WEBHOOK_SECRET: undefined });
  await withRecord(off);
  const gh2 = github([comment('/reply Read on request.')]);
  const offBody = await repliesOf(off, gh2.fetcher);
  assert.equal(offBody.next, NEXT_CHECK);
  assert.equal(offBody.replies[0].items[0].text, 'Read on request.');
  assert.equal(gh2.calls.length, 2);
  assert.equal(state(off), null, 'nothing is written to KV without push');
  await repliesOf(off, noGithub);
});

// --- Security review fixes ---

test('a GitHub read drops the tombstones it confirms and keeps the ones GitHub still lists, at most 20', async () => {
  const e = env({ REPLY_CACHE: spyCache() });
  await withRecord(e);
  const kept = comment('/reply Edited away.', { at: '2026-11-02T10:00:00Z' });
  const deleted = comment('/reply Deleted.', { at: '2026-11-02T10:00:00Z' });
  await handle(hook(delivery('created', kept)), e);
  await handle(hook(delivery('created', deleted)), e);
  await handle(hook(delivery('edited', { ...kept, body: 'nothing', updated_at: '2026-11-02T10:30:00Z' }, { changes: { body: { from: kept.body } } })), e);
  await handle(hook(delivery('deleted', deleted)), e);
  assert.equal(state(e).gone.length, 2);
  // GitHub's list lags: it still has both old answers. They stay out, and so do their tombstones.
  let r = (await repliesOf(e, github([kept, deleted]).fetcher)).replies[0];
  assert.deepEqual(r.items, []);
  assert.equal(state(e).gone.length, 2);
  // GitHub lists neither any more: both tombstones are confirmed and go, and that complete read says full.
  e.REPLY_CACHE = spyCache();
  r = (await repliesOf(e, github([]).fetcher)).replies[0];
  assert.deepEqual(r.items, []);
  assert.equal(r.full, true);
  assert.deepEqual(state(e).gone, []);
  // At most MAX_GONE are kept, the newest.
  for (let i = 0; i < 25; i++) await handle(hook(delivery('deleted', comment(`/reply number ${i}`))), e);
  assert.equal(state(e).gone.length, 20);
});

test('a KV read that fails is a 503 so GitHub can send it again; a missing record is a 200', async () => {
  const e = env();
  await withRecord(e);
  const get = e.VOTES.get;
  e.VOTES.get = async k => { if (k.startsWith('rr:')) throw new Error('KV is down'); return get(k); };
  let res = await handle(hook(delivery('created', comment('/reply hi'))), e);
  assert.equal(res.status, 503);
  e.VOTES.get = get;
  res = await handle(hook(delivery('created', comment('/reply hi'), { issue: ownIssue(41, 'CR-ZZZZZ') })), e);
  assert.deepEqual(await res.json(), { ok: true, ignored: 'no report' });
  // A failed write is a 503 too.
  e.VOTES.put = async () => { throw new Error('KV is down'); };
  res = await handle(hook(delivery('created', comment('/reply hi'))), e);
  assert.equal(res.status, 503);
});

// --- Hub review fixes: out of order deliveries, writes that cross, a deleted answer, the Issues event ---

test('deliveries out of order: a delete that lands before the edit wins, and an old edit after a new one changes nothing', async () => {
  const e = env();
  await withRecord(e);
  const c = comment('/reply A', { at: '2026-11-02T10:00:00Z' });
  await handle(hook(delivery('created', c)), e);
  const b = { ...c, body: '/reply B', updated_at: '2026-11-02T10:05:00Z' };
  // GitHub sent edit A to B, then the delete; the delete arrives first.
  let res = await handle(hook(delivery('deleted', b)), e);
  assert.deepEqual(await res.json(), { ok: true, stored: 1 });
  res = await handle(hook(delivery('edited', b, { changes: { body: { from: c.body } } })), e);
  assert.deepEqual(await res.json(), { ok: true, ignored: 'unchanged' });
  assert.deepEqual((await repliesOf(e, noGithub)).replies[0].items, []);

  // An answer edited twice, the newer edit delivered first.
  const d = comment('/reply First', { at: '2026-11-02T11:00:00Z' });
  await handle(hook(delivery('created', d)), e);
  const newer = { ...d, body: '/reply Third', updated_at: '2026-11-02T11:20:00Z' };
  const older = { ...d, body: '/reply Second', updated_at: '2026-11-02T11:10:00Z' };
  await handle(hook(delivery('edited', newer, { changes: { body: { from: older.body } } })), e);
  res = await handle(hook(delivery('edited', older, { changes: { body: { from: d.body } } })), e);
  assert.deepEqual(await res.json(), { ok: true, ignored: 'unchanged' });
  assert.equal((await repliesOf(e, noGithub)).replies[0].items[0].text, 'Third');
});

test('a write that lands between the snapshot and the put: both answers and every key survive', async () => {
  const e = env();
  await withRecord(e);
  const snapshot = JSON.parse(e.VOTES.map.get('rr:' + CODE));
  // Meanwhile: another delivery stores answer B, and a duplicate report's key lands in the record.
  const b = comment('/reply B', { at: '2026-11-02T10:05:00Z' });
  await handle(hook(delivery('created', b)), e);
  const now = JSON.parse(e.VOTES.map.get('rr:' + CODE));
  now.keys.push({ salt: 'f'.repeat(32), hash: 'e'.repeat(64) });
  e.VOTES.map.set('rr:' + CODE, JSON.stringify(now));
  // This delivery was made from the old snapshot: answer A.
  const a = comment('/reply A', { at: '2026-11-02T10:00:00Z' });
  const done = await applyComment(e, CODE, snapshot, a, 'created', { id: String(a.id), at: a.updated_at, kind: 'reply', text: 'A', logs: false, auto: false }, null);
  assert.equal(done, 'stored');
  const stored = JSON.parse(e.VOTES.map.get('rr:' + CODE));
  assert.equal(stored.keys.length, 2, 'the key added meanwhile is kept');
  assert.deepEqual(stored.push.events.map(x => x.text).sort(), ['A', 'B']);
});

test('a /reply that lands during the 30 minute GitHub read is kept', async () => {
  const e = env({ REPLY_CACHE: spyCache() });
  await withRecord(e);
  const late = comment('/reply Landed during the read.');
  // GitHub answers before it has the comment; the webhook for it lands while the read runs.
  const fetcher = async url => {
    if (!/\/issues\/\d+$/.test(new URL(url).pathname)) await handle(hook(delivery('created', late)), e);
    return github().fetcher(url);
  };
  await repliesOf(e, fetcher);
  // The read stored nothing over it, and the next check serves it from KV with no GitHub call.
  assert.equal(state(e).events.length, 1);
  assert.equal((await repliesOf(e, noGithub)).replies[0].items[0].text, 'Landed during the read.');
  assert.equal(JSON.parse(e.VOTES.map.get('rr:' + CODE)).keys.length, 1);
});

test('"full" is set only by a good GitHub read in the same request, never from the record or the marker', async () => {
  const e = env({ REPLY_CACHE: spyCache() });
  await withRecord(e);
  // The first read fails and nothing was ever stored: no "full", the tool keeps what it has.
  let r = (await repliesOf(e, async () => new Response('{}', { status: 502 }))).replies[0];
  assert.equal(r.full, undefined);
  // Inside the retry wait, still not.
  r = (await repliesOf(e, noGithub)).replies[0];
  assert.equal(r.full, undefined);
  // A good read: full, with no answer (Coodsz deleted it).
  e.REPLY_CACHE = spyCache();
  r = (await repliesOf(e, github().fetcher)).replies[0];
  assert.equal(r.full, true);
  assert.deepEqual(r.items, []);
  // Served from the marker: another copy may have served an answer it could not store, so no full.
  r = (await repliesOf(e, noGithub)).replies[0];
  assert.equal(r.full, undefined);
  // Without push: a good GitHub read is full, a failed one is not.
  const off = env({ GITHUB_WEBHOOK_SECRET: undefined });
  await withRecord(off);
  assert.equal((await repliesOf(off, async () => new Response('{}', { status: 502 }))).replies[0].full, undefined);
  assert.equal((await repliesOf(off, github().fetcher)).replies[0].full, true);
  // An unknown report or a wrong key never says full.
  const wrong = await (await handle(check([{ code: CODE, key: 'Q'.repeat(43) }], { fast: true }), e, noGithub)).json();
  assert.equal(wrong.replies[0].full, undefined);
});

test('the Issues event brings a fixed-in or player-solved label to the badge with no comment', async () => {
  const e = env();
  await withRecord(e);
  await repliesOf(e, github().fetcher);
  const labeled = labels => ({ action: 'labeled', issue: ownIssue(41, CODE, labels), repository: { full_name: REPO }, sender: user(77) });
  let res = await handle(hook(labeled(['crash-report', 'fixed-in-v1.3.5']), { event: 'issues' }), e);
  assert.deepEqual(await res.json(), { ok: true, stored: 1 });
  let r = (await repliesOf(e, noGithub)).replies[0];
  assert.equal(r.status, 'fixed');
  assert.equal(r.fixedIn, '1.3.5');
  // A label the badge does not use writes nothing.
  const kv = counted(e);
  res = await handle(hook(labeled(['crash-report', 'fixed-in-v1.3.5', 'known']), { event: 'issues' }), e);
  assert.deepEqual(await res.json(), { ok: true, ignored: 'unchanged' });
  assert.deepEqual(kv.puts, []);
  // Taken off again.
  res = await handle(hook({ ...labeled(['crash-report']), action: 'unlabeled' }, { event: 'issues' }), e);
  assert.deepEqual(await res.json(), { ok: true, stored: 1 });
  r = (await repliesOf(e, noGithub)).replies[0];
  assert.equal(r.status, 'looking');
  // Other actions, a tracking issue (no own code) and another issue's code are ignored.
  res = await handle(hook({ ...labeled(['fixed-in-v1.3.5']), action: 'opened' }, { event: 'issues' }), e);
  assert.deepEqual(await res.json(), { ok: true, ignored: 'action' });
  res = await handle(hook({ ...labeled(['fixed-in-v1.3.5']), issue: { number: 8, title: 'Known bug', body: 'x', labels: [] } }, { event: 'issues' }), e);
  assert.deepEqual(await res.json(), { ok: true, ignored: 'no report' });
  res = await handle(hook({ ...labeled(['fixed-in-v1.3.5']), issue: ownIssue(42, CODE, ['fixed-in-v1.3.5']) }, { event: 'issues' }), e);
  assert.deepEqual(await res.json(), { ok: true, ignored: 'no report' });
  // KV that does not answer is a 503 here too.
  e.VOTES.get = async () => { throw new Error('down'); };
  res = await handle(hook(labeled(['player-solved']), { event: 'issues' }), e);
  assert.equal(res.status, 503);
});

// --- Round 3: full only when KV really holds a GitHub read, the push write cap, ties, KV 429 ---

test('a store that fails, or a report too close to its end to write, never claims a full list', async () => {
  // The read itself is fine, but the write fails (KV down, or past its one write a second).
  const e = env({ REPLY_CACHE: spyCache() });
  await withRecord(e);
  const put = e.VOTES.put;
  e.VOTES.put = async (k, v, o) => { if (k.startsWith('rr:')) throw new Error('KV 429'); return put(k, v, o); };
  let r = (await repliesOf(e, github([comment('/reply An answer.')]).fetcher)).replies[0];
  assert.equal(r.items[0].text, 'An answer.');
  assert.equal(r.full, undefined);
  assert.equal(lastMarker(e.REPLY_CACHE).seconds, SYNC_RETRY);
  // Every later check inside the wait: the record is still empty, so no full (this used to say full with items []).
  r = (await repliesOf(e, noGithub)).replies[0];
  assert.deepEqual(r.items, []);
  assert.equal(r.full, undefined);

  // A report in its last two minutes is not written: no full either.
  const old = env({ REPLY_CACHE: spyCache() });
  await storeReplyRecord(old, CODE, KEY, { repo: REPO, issue: 41, tracked: false, created: new Date(Date.now() - (30 * 24 * 3600 - 60) * 1000).toISOString() });
  r = (await repliesOf(old, github([comment('/reply Late.')]).fetcher)).replies[0];
  assert.equal(r.full, undefined);
  assert.equal((await repliesOf(old, noGithub)).replies[0].full, undefined);
});

test('a state the webhook alone built is no full list until a GitHub read is stored in it', async () => {
  const e = env({ REPLY_CACHE: spyCache() });
  await withRecord(e);
  // The player pressed No on an answer the tool already holds: the webhook stores only the follow up marker.
  await handle(hook(delivery('created', comment(`<!-- coodsz-followup ${CODE} -->\n**Still broken: the player answered on ${CODE}**\nStill broken.`))), e);
  assert.equal(state(e).synced, false);
  // The first check hits the read cap (or a 5xx, or a token error): items [], and no full.
  let r = (await repliesOf(e, async () => new Response('{}', { status: 502 }))).replies[0];
  assert.deepEqual(r.items, []);
  assert.equal(r.full, undefined);
  r = (await repliesOf(e, noGithub)).replies[0];
  assert.equal(r.full, undefined);
  // A good read is stored with synced, and its own answer says full.
  e.REPLY_CACHE = spyCache();
  const answer = comment('/reply The answer.', { at: '2026-11-01T09:00:00Z' });
  answer.id = 10;
  r = (await repliesOf(e, github([answer]).fetcher)).replies[0];
  assert.equal(r.full, true);
  assert.equal(state(e).synced, true);
  // A later failed read serves the record, never as full: another copy may hold an answer it could not store.
  e.REPLY_CACHE = spyCache();
  r = (await repliesOf(e, async () => new Response('{}', { status: 502 }))).replies[0];
  assert.equal(r.full, undefined);
  assert.equal(r.items[0].text, 'The answer.');
  // A webhook write keeps synced.
  await handle(hook(delivery('created', comment('/reply Newer.'))), e);
  assert.equal(state(e).synced, true);
});

test('past the day\'s push writes a delivery writes nothing and says budget, and the GitHub read serves the answer', async () => {
  const full = { note: () => null, count: async () => MAX_PUSH_WRITES };
  const e = env({ PUSH_WRITES: full, REPLY_CACHE: spyCache() });
  await withRecord(e);
  const kv = counted(e);
  const res = await handle(hook(delivery('created', comment('/reply Over the budget.'))), e);
  assert.deepEqual(await res.json(), { ok: true, ignored: 'budget' });
  const r = (await repliesOf(e, github([comment('/reply Over the budget.')]).fetcher)).replies[0];
  assert.equal(r.items[0].text, 'Over the budget.');
  assert.equal(r.full, undefined);
  assert.deepEqual(kv.puts, []);
  // The skip waits a full interval, not the 2 minute retry.
  assert.equal(lastMarker(e.REPLY_CACHE).seconds, SYNC_INTERVAL);
  // Reply keys are never held back by it.
  assert.ok(await storeReplyRecord(e, 'CR-ABCDE', KEY, { repo: REPO, issue: 42, tracked: false, created: new Date().toISOString() }));
});

test('a synced record that lacks an answer a read served but could not store is never a full list', async () => {
  const e = env({ REPLY_CACHE: spyCache() });
  await withRecord(e);
  const first = comment('/reply First.', { at: '2026-11-01T09:00:00Z' });
  first.id = 10;
  assert.equal((await repliesOf(e, github([first]).fetcher)).replies[0].full, true);
  assert.equal(state(e).synced, true);
  // The day's push writes run out; the next read serves the new answer but can't store it.
  e.PUSH_WRITES = { note: () => null, count: async () => MAX_PUSH_WRITES };
  e.REPLY_CACHE = spyCache();
  const newer = comment('/reply Newer.', { at: '2026-11-01T10:00:00Z' });
  newer.id = 11;
  let r = (await repliesOf(e, github([first, newer]).fetcher)).replies[0];
  assert.deepEqual(r.items.map(i => i.text), ['Newer.']);
  assert.equal(r.full, undefined);
  // Inside the marker window the record still holds only the first answer: no full, so the tool keeps Newer.
  r = (await repliesOf(e, noGithub)).replies[0];
  assert.equal(r.full, undefined);
});

test('every stored push write is counted, in batches', async () => {
  const writes = makePushWriteCounter();
  const e = env({ PUSH_WRITES: writes });
  await withRecord(e);
  for (let i = 0; i < 12; i++) await handle(hook(delivery('created', comment(`/reply number ${i}`))), e);
  assert.equal(await writes.count({}), 12);
  assert.deepEqual(JSON.parse(e.VOTES.map.get('pw:' + new Date().toISOString().slice(0, 10))), { n: 10, t: 0 });
});

test('a KV write that fails (one key takes about one a second) is a 503 that says the next read heals it', async () => {
  const e = env();
  await withRecord(e);
  const put = e.VOTES.put;
  e.VOTES.put = async (k, v, o) => { if (k.startsWith('rr:')) throw new Error('KV 429 Too Many Requests'); return put(k, v, o); };
  const res = await handle(hook(delivery('created', comment('/reply hi'))), e);
  assert.equal(res.status, 503);
  assert.deepEqual(await res.json(), { ok: false, error: 'try again', heals: 'the next GitHub read of this report brings it' });
});

test('an edit from the same second as the stored text never writes over it', async () => {
  const e = env();
  await withRecord(e);
  const c = comment('/reply First.', { at: '2026-11-02T10:00:00Z' });
  await handle(hook(delivery('created', c)), e);
  const newer = { ...c, body: '/reply Newer.', updated_at: '2026-11-02T10:00:05Z' };
  const older = { ...c, body: '/reply Older, same second.', updated_at: '2026-11-02T10:00:05Z' };
  await handle(hook(delivery('edited', newer, { changes: { body: { from: c.body } } })), e);
  const res = await handle(hook(delivery('edited', older, { changes: { body: { from: c.body } } })), e);
  assert.deepEqual(await res.json(), { ok: true, ignored: 'unchanged' });
  assert.equal((await repliesOf(e, noGithub)).replies[0].items[0].text, 'Newer.');
});

// --- Round 4: a busy issue that runs past 5 pages ---

// A GitHub with pages of 100 and a Link header, like the real one.
function pagedGithub(comments) {
  const calls = [];
  const fetcher = async url => {
    calls.push(url);
    if (/\/issues\/\d+$/.test(new URL(url).pathname)) return new Response(JSON.stringify({ number: 8, labels: [] }), { status: 200 });
    const page = Number(new URL(url).searchParams.get('page') ?? '1');
    const last = Math.max(1, Math.ceil(comments.length / 100));
    const headers = last > 1 ? { link: `<https://api.github.com/x?per_page=100&page=${last}>; rel="last"` } : {};
    return new Response(JSON.stringify(comments.slice((page - 1) * 100, page * 100)), { status: 200, headers });
  };
  return { calls, fetcher };
}

test('an issue past 5 pages: the old /reply in the skipped pages is kept and the read is never full', async () => {
  const e = env({ REPLY_CACHE: spyCache() });
  const tracking = { number: 8, title: 'Known bug', body: 'Tracking issue', labels: [] };
  await withRecord(e, CODE, { issue: 8, tracked: true });
  // The answer came in through the webhook while the issue was short.
  const old = comment(`/reply ${CODE} The old answer.`, { at: '2026-11-01T09:00:00Z' });
  old.id = 100;
  await handle(hook(delivery('created', old, { issue: tracking })), e);
  // Since then the issue grew to 7 pages of other reports' comments; the read takes page 1 and the newest 4.
  const filler = Array.from({ length: 650 }, (_, i) => ({ ...comment('another report'), id: 1000 + i }));
  const gh = pagedGithub([old, ...filler]);
  let r = (await repliesOf(e, gh.fetcher)).replies[0];
  assert.deepEqual(gh.calls.filter(u => u.includes('/comments')).map(u => new URL(u).searchParams.get('page')), ['1', '4', '5', '6', '7']);
  assert.equal(r.items[0].text, 'The old answer.');
  assert.equal(r.full, undefined, 'a read that skipped pages is no full list');
  // The record keeps the answer, and is not marked as read from GitHub.
  assert.deepEqual(state(e).events.map(x => x.id), ['100']);
  assert.equal(state(e).synced, false);
  // The next check, and the next read after the interval, still serve it.
  assert.equal((await repliesOf(e, noGithub)).replies[0].items[0].text, 'The old answer.');
  assert.equal(lastMarker(e.REPLY_CACHE).seconds, SYNC_INTERVAL, 'a skipped page read is not asked again soon');
  e.REPLY_CACHE = spyCache();
  r = (await repliesOf(e, pagedGithub([old, ...filler]).fetcher)).replies[0];
  assert.equal(r.items[0].text, 'The old answer.');
  assert.equal(r.full, undefined);

  // A tombstone in the skipped pages stays: the read could not confirm the comment is gone.
  await handle(hook(delivery('deleted', old, { issue: tracking })), e);
  e.REPLY_CACHE = spyCache();
  r = (await repliesOf(e, pagedGithub(filler).fetcher)).replies[0];
  assert.deepEqual(r.items, []);
  assert.deepEqual(state(e).gone.map(g => g.id), ['100']);
});

// --- A delete whose webhook was missed ---

const age = (e, ms, code = CODE) => {
  const record = JSON.parse(e.VOTES.map.get('rr:' + code));
  record.push.events = record.push.events.map(x => (ms === null ? (({ storedAt, ...rest }) => rest)(x) : { ...x, storedAt: x.storedAt - ms }));
  e.VOTES.map.set('rr:' + code, JSON.stringify(record));
};

test('a complete read drops a stored answer GitHub no longer lists (a missed delete) and says full with items []', async () => {
  for (const stamp of [10 * 60 * 1000, null]) {
    const e = env({ REPLY_CACHE: spyCache() });
    await withRecord(e);
    const a = comment('/reply Deleted while the webhook was missed.');
    await handle(hook(delivery('created', a)), e);
    assert.ok(state(e).events[0].storedAt > 0, 'the webhook stamps what it stores');
    // Stored 10 minutes ago, or by a worker before the stamp (no storedAt): the delete delivery never came.
    age(e, stamp);
    const r = (await repliesOf(e, github([]).fetcher)).replies[0];
    assert.deepEqual(r.items, []);
    assert.equal(r.full, true);
    assert.deepEqual(state(e).events, []);
  }
});

test('a complete read keeps an answer pushed while it ran, and a listed answer always comes from the read', async () => {
  const e = env({ REPLY_CACHE: spyCache() });
  await withRecord(e);
  const during = comment('/reply Pushed during the read.');
  const fetcher = async url => {
    if (!/\/issues\/\d+$/.test(new URL(url).pathname)) await handle(hook(delivery('created', during)), e);
    return github().fetcher(url);
  };
  await repliesOf(e, fetcher);
  assert.equal(state(e).events.length, 1);
  assert.equal((await repliesOf(e, noGithub)).replies[0].items[0].text, 'Pushed during the read.');

  // A stored answer GitHub still lists stays, also when it has no stamp, and a read that finds nothing new writes nothing.
  const listed = env({ REPLY_CACHE: spyCache() });
  await withRecord(listed);
  const b = comment('/reply Still on GitHub.');
  await handle(hook(delivery('created', b)), listed);
  age(listed, null);
  let r = (await repliesOf(listed, github([b]).fetcher)).replies[0];
  assert.equal(r.items[0].text, 'Still on GitHub.');
  assert.equal(r.full, true);
  const kv = counted(listed);
  listed.REPLY_CACHE = spyCache();
  r = (await repliesOf(listed, github([b]).fetcher)).replies[0];
  assert.equal(r.items[0].text, 'Still on GitHub.');
  assert.deepEqual(kv.puts, []);
});

test('the same follow up marker delivered again writes nothing and keeps its stamp', async () => {
  const e = env();
  await withRecord(e);
  const marker = comment(`<!-- coodsz-followup ${CODE} -->\n**Still broken: the player answered on ${CODE}**\nStill broken.`);
  await handle(hook(delivery('created', marker)), e);
  const first = state(e).events[0].storedAt;
  const kv = counted(e);
  const res = await handle(hook(delivery('created', marker)), e);
  assert.deepEqual(await res.json(), { ok: true, ignored: 'unchanged' });
  assert.deepEqual(kv.puts, []);
  assert.equal(state(e).events[0].storedAt, first);
});

// --- Codex round 2: pages that shift during a read, labels on a repeated marker ---

test('a read of several pages keeps a stored answer it did not list (a page shifted) and sends no full', async () => {
  const e = env({ REPLY_CACHE: spyCache() });
  const tracking = { number: 8, title: 'Known bug', body: 'Tracking issue', labels: [] };
  await withRecord(e, CODE, { issue: 8, tracked: true });
  const newest = comment(`/reply ${CODE} The newest answer.`);
  newest.id = 5000;
  await handle(hook(delivery('created', newest, { issue: tracking })), e);
  age(e, 10 * 60 * 1000);
  // Two pages, but a comment deleted on page 1 between the requests moved the newest answer onto page 1, already read:
  // neither page lists it.
  const filler = Array.from({ length: 150 }, (_, i) => ({ ...comment('another report'), id: 1000 + i }));
  let r = (await repliesOf(e, pagedGithub(filler).fetcher)).replies[0];
  assert.equal(r.items[0].text, 'The newest answer.');
  assert.equal(r.full, undefined);
  assert.deepEqual(state(e).events.map(x => x.id), ['5000']);
  // On one page the same missing answer is a missed delete: dropped, full with items [].
  e.REPLY_CACHE = spyCache();
  r = (await repliesOf(e, pagedGithub(filler.slice(0, 50)).fetcher)).replies[0];
  assert.deepEqual(r.items, []);
  assert.equal(r.full, true);
});

test('the same follow up marker again still brings a change of the status labels', async () => {
  const e = env();
  await withRecord(e);
  const marker = comment(`<!-- coodsz-followup ${CODE} -->\n**Still broken: the player answered on ${CODE}**\nStill broken.`);
  await handle(hook(delivery('created', marker)), e);
  const first = state(e).events[0].storedAt;
  const res = await handle(hook(delivery('created', marker, { issue: ownIssue(41, CODE, ['fixed-in-v1.3.5']) })), e);
  assert.deepEqual(await res.json(), { ok: true, stored: 1 });
  assert.deepEqual(state(e).labels, ['fixed-in-v1.3.5']);
  assert.equal(state(e).events[0].storedAt, first);
});

test('a read of several pages is never full, also when KV never held the answer a page shift hid', async () => {
  // An earlier check served answer 101 from a fresh read but could not store it (the day's push writes were used).
  const usedUp = { note: () => null, count: async () => MAX_PUSH_WRITES };
  const e = env({ REPLY_CACHE: spyCache(), PUSH_WRITES: usedUp });
  await withRecord(e, CODE, { issue: 8, tracked: true });
  const answer = comment(`/reply ${CODE} The live answer.`);
  answer.id = 101;
  const filler = Array.from({ length: 150 }, (_, i) => ({ ...comment('another report'), id: 1000 + i }));
  let r = (await repliesOf(e, pagedGithub([answer, ...filler.slice(0, 20)]).fetcher)).replies[0];
  assert.equal(r.items[0].text, 'The live answer.');
  assert.equal(state(e), null, 'nothing was stored');
  // Later the issue has two pages, and a comment deleted on page 1 between the requests hid 101: KV holds nothing
  // unlisted, so only the page count can say this is no full list.
  e.REPLY_CACHE = spyCache();
  r = (await repliesOf(e, pagedGithub(filler).fetcher)).replies[0];
  assert.deepEqual(r.items, []);
  assert.equal(r.full, undefined);
});
