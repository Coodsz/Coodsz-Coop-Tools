// node --test test/   (Node 20+, no packages). The report status badge, Yes it's fixed (POST /replies/solved)
// and No, still broken (POST /crash with summary.followUp), with a fake GitHub and a fake KV.
import test from 'node:test';
import assert from 'node:assert/strict';
import { handle } from '../src/index.js';
import { makeReplyCache, makeReadCap, answerState, statusFor, SOLVED_LABEL, resetSkippedMarkerWarning } from '../src/replies.js';
import { storeReplyRecord } from '../src/reply-keys.js';
import { parseAnswer } from '../src/reply-text.js';
import { solvedAction, validateSolved, DEFAULT_SOLVED_ACTION } from '../src/solved.js';
import { validateFollowUp, followUpComment, FOLLOW_UP_MARKER } from '../src/followup.js';

const COODSZ = 4242;
const INSTALL = '0123456789abcdef0123456789abcdef';
const KEY = 'A'.repeat(42) + 'b';
const KEY2 = 'Z'.repeat(42) + '_';

function kv() {
  const map = new Map();
  return {
    map,
    get: async k => (map.has(k) ? map.get(k) : null),
    put: async (k, v) => { map.set(k, v); },
    delete: async k => { map.delete(k); },
    list: async ({ prefix }) => ({ keys: [...map.keys()].filter(k => k.startsWith(prefix)).map(name => ({ name })), list_complete: true }),
  };
}

const env = (extra = {}) => ({
  GITHUB_TOKEN: 'test-token', VOTER_SALT: 'salt', VOTES: kv(), COODSZ_GITHUB_ID: String(COODSZ),
  REPLY_CACHE: makeReplyCache(), REPLY_READ_CAP: makeReadCap(), ...extra,
});

async function withRecord(e, code, { tracked = false, issue = 41, key = KEY } = {}) {
  assert.ok(await storeReplyRecord(e, code, key, { repo: 'Coodsz/Coodsz-Crash-Reports', issue, tracked, created: new Date().toISOString() }));
}

// A fake GitHub that records every call and answers comments, the issue and writes.
function github({ comments = [], issue = { number: 41, state: 'open', labels: [] }, status = 200 } = {}) {
  const calls = [];
  const fetcher = async (url, init = {}) => {
    const path = new URL(url).pathname;
    calls.push({ path, method: init.method ?? 'GET', body: init.body === undefined ? undefined : JSON.parse(init.body) });
    if (url.startsWith('https://discord.com/')) return new Response(null, { status: 204 });
    if (status !== 200) return new Response('{}', { status });
    if ((init.method ?? 'GET') === 'GET' && path.endsWith('/comments')) return new Response(JSON.stringify(comments), { status: 200 });
    if ((init.method ?? 'GET') === 'GET') return new Response(JSON.stringify(issue), { status: 200 });
    return new Response(JSON.stringify({ id: 1 }), { status: 200 });
  };
  return { calls, fetcher, writes: () => calls.filter(c => c.method !== 'GET') };
}

let nextId = 5000;
const comment = (body, { id = COODSZ, at = '2026-11-02T10:00:00Z' } = {}) =>
  ({ id: nextId++, body, user: { id, login: 'Coodsz', type: 'User' }, created_at: at, updated_at: at });

const check = code => new Request('https://crash.example/replies', {
  method: 'POST',
  body: JSON.stringify({ kind: 'CoodszReplyCheck', schema: 1, reports: [{ code, key: KEY }] }),
  headers: { 'CF-Connecting-IP': '203.0.113.9' },
});

const solved = (code, key = KEY, extra = {}) => new Request('https://crash.example/replies/solved', {
  method: 'POST',
  body: JSON.stringify({ kind: 'CoodszReplySolved', schema: 1, code, key, ...extra }),
  headers: { 'CF-Connecting-IP': '203.0.113.9' },
});

// --- The status badge ---

test('the status: looking, answered, solved, fixed in a version, and a withdrawn answer is no answer', () => {
  const e = { COODSZ_GITHUB_ID: String(COODSZ) };
  assert.deepEqual(statusFor(null, false), { status: 'looking' });
  assert.deepEqual(statusFor({ labels: [] }, true), { status: 'answered' });
  assert.deepEqual(statusFor({ labels: [{ name: SOLVED_LABEL }] }, true), { status: 'solved' });
  assert.deepEqual(statusFor({ labels: [{ name: 'player-solved' }, { name: 'fixed-in-v1.3.3' }] }, true), { status: 'fixed', fixedIn: '1.3.3' });
  assert.deepEqual(statusFor({ labels: ['fixed-in-1.2.76.0'] }, false), { status: 'fixed', fixedIn: '1.2.76.0' });
  assert.deepEqual(statusFor({ labels: [{ name: 'fixed-in-vnext' }] }, false), { status: 'looking' }, 'no version, no fixed');
  assert.deepEqual(statusFor({ labels: [{ name: 'fixed-in-v1.3.4' }] }, true, true), { status: 'fixed', fixedIn: '1.3.4' },
    'a release label wins even over a newer No, still broken, without a new /reply');
  assert.deepEqual(statusFor({ labels: [{ name: 'player-solved' }] }, true, true), { status: 'looking' }, 'a newer No beats player-solved');

  const retracted = answerState([comment('/reply try this'), comment('/retract')], 'CR-7KQ2M', false, e);
  assert.equal(retracted.retracted, true);
  assert.equal(retracted.items.length, 1, 'the withdrawn note is still served');
});

test('a No, still broken newer than the answer puts the report back to looking, a newer answer to answered', () => {
  const e = { COODSZ_GITHUB_ID: String(COODSZ) };
  const answer = comment('/reply turn off the map mod');
  // The worker posts with the crash token, which is Coodsz's account.
  const followUp = comment('<!-- coodsz-followup CR-7KQ2M -->\n**Still broken: the player answered on CR-7KQ2M**');
  const other = comment('<!-- coodsz-followup CR-AAAAA -->\n**Still broken: the player answered on CR-AAAAA**');
  const collaborator = comment('<!-- coodsz-followup CR-7KQ2M -->\n**Still broken: the player answered on CR-7KQ2M** (new logs CR-AAAAA)', { id: 77 });
  const warnings = [];
  const warn = console.warn;
  console.warn = m => warnings.push(m);
  try {
    resetSkippedMarkerWarning();
    assert.equal(answerState([answer, collaborator], 'CR-7KQ2M', false, e).waiting, false, 'a collaborator\'s copy of the marker is ignored');
    answerState([answer, collaborator], 'CR-7KQ2M', false, e);
    assert.equal(warnings.filter(w => w.includes('WORKER_GITHUB_ID')).length, 1, 'a skipped marker is said once, so a missing WORKER_GITHUB_ID shows in the log');
  } finally {
    console.warn = warn;
  }
  assert.equal(answerState([answer, collaborator], 'CR-7KQ2M', false, { ...e, WORKER_GITHUB_ID: '77' }).waiting, true, 'WORKER_GITHUB_ID names a bot account');
  // Coodsz's account, but only the marker: a hand written comment, not the worker's.
  const handWritten = comment('<!-- coodsz-followup CR-7KQ2M -->\nlooking at it');
  assert.equal(answerState([answer, handWritten], 'CR-7KQ2M', false, e).waiting, false, 'the marker alone is not the worker\'s comment');
  assert.equal(answerState([answer, followUp], 'CR-7KQ2M', false, e).waiting, true);
  assert.equal(answerState([answer, other], 'CR-7KQ2M', false, e).waiting, false, 'another report on a tracking issue');
  assert.equal(answerState([answer, followUp, comment('/reply try this then')], 'CR-7KQ2M', false, e).waiting, false);
  assert.deepEqual(answerState([answer, followUp], 'CR-7KQ2M', false, e).items.map(i => i.text), ['turn off the map mod'],
    'the follow up is never served as an answer');
});

test('POST /replies gives each report its status from the comments and the issue labels', async () => {
  const e = env();
  await withRecord(e, 'CR-7KQ2M');
  const answered = await (await handle(check('CR-7KQ2M'), e, github({ comments: [comment('/reply hi')] }).fetcher)).json();
  assert.equal(answered.replies[0].status, 'answered');
  assert.equal('fixedIn' in answered.replies[0], false);

  const f = env();
  await withRecord(f, 'CR-7KQ2M');
  const gh = github({ comments: [comment('/reply hi')], issue: { number: 41, state: 'closed', labels: [{ name: 'fixed-in-v1.3.3' }] } });
  const fixed = await (await handle(check('CR-7KQ2M'), f, gh.fetcher)).json();
  assert.equal(fixed.replies[0].status, 'fixed');
  assert.equal(fixed.replies[0].fixedIn, '1.3.3');
  assert.ok(gh.calls.some(c => c.path === '/repos/Coodsz/Coodsz-Crash-Reports/issues/41' && c.method === 'GET'), 'the issue is read for its labels');
  // The status is cached with the answer: no second read.
  const before = gh.calls.length;
  assert.equal((await (await handle(check('CR-7KQ2M'), f, gh.fetcher)).json()).replies[0].status, 'fixed');
  assert.equal(gh.calls.length, before);

  const n = env();
  await withRecord(n, 'CR-7KQ2M');
  assert.equal((await (await handle(check('CR-7KQ2M'), n, github().fetcher)).json()).replies[0].status, 'looking');
});

// --- Hidden markers on top of an answer ---

test('an answer with <!-- report-replies --> on top still counts, and the marker never reaches the player', () => {
  const answer = parseAnswer('<!-- report-replies -->\n/reply Hey, thanks for the logs.\n1. Turn off Europe 1100\n2. Pick Vanilla');
  assert.equal(answer.kind, 'reply');
  assert.equal(answer.text, 'Hey, thanks for the logs.\n1. Turn off Europe 1100\n2. Pick Vanilla');
  const e = { COODSZ_GITHUB_ID: String(COODSZ) };
  const [item] = answerState([comment('<!-- report-replies -->\r\n/reply +logs Hi\n1. Step one\n2. Step two')], 'CR-7KQ2M', false, e).items;
  assert.equal(item.text, 'Hi\n1. Step one\n2. Step two');
  assert.deepEqual(item.buttons, ['send-logs']);
  assert.equal(parseAnswer('<!-- report-replies -->\nthanks, looking into it'), null, 'a marker alone makes no answer');
  assert.equal(parseAnswer('<!-- coodsz-followup CR-7KQ2M -->\n/reply sneaky')?.kind, 'reply',
    'the command line counts as before; the follow up comment the worker writes never has one');
});

// --- Yes, it's fixed ---

test('SOLVED_ACTION: close by default, label when set, anything else is close', () => {
  assert.equal(DEFAULT_SOLVED_ACTION, 'close');
  assert.equal(solvedAction({}), 'close');
  assert.equal(solvedAction({ SOLVED_ACTION: 'label' }), 'label');
  assert.equal(solvedAction({ SOLVED_ACTION: ' Close ' }), 'close');
  const warn = console.warn;
  console.warn = () => {};
  try {
    assert.equal(solvedAction({ SOLVED_ACTION: 'delete' }), 'close');
  } finally {
    console.warn = warn;
  }
  assert.deepEqual(validateSolved({ kind: 'CoodszReplySolved', schema: 1, code: 'CR-7KQ2M', key: KEY }), { code: 'CR-7KQ2M', key: KEY });
  assert.equal(validateSolved({ kind: 'CoodszReplySolved', schema: 1, code: 'CR-7KQ2M', key: 'short' }), 'key');
  assert.equal(validateSolved({ kind: 'CoodszReplyCheck', schema: 1, code: 'CR-7KQ2M', key: KEY }), 'unknown kind or schema');
});

test('Yes, it\'s fixed with the default closes the issue with the player-solved label, once', async () => {
  const e = env();
  await withRecord(e, 'CR-7KQ2M');
  const gh = github();
  const res = await handle(solved('CR-7KQ2M'), e, gh.fetcher);
  assert.equal(res.status, 200);
  assert.deepEqual(await res.json(), { ok: true, solved: true, action: 'closed' });
  assert.deepEqual(gh.writes(), [
    { path: '/repos/Coodsz/Coodsz-Crash-Reports/issues/41/labels', method: 'POST', body: { labels: ['player-solved'] } },
    { path: '/repos/Coodsz/Coodsz-Crash-Reports/issues/41', method: 'PATCH', body: { state: 'closed', state_reason: 'completed' } },
  ]);
  // A second click does nothing new on GitHub.
  assert.deepEqual(await (await handle(solved('CR-7KQ2M'), e, gh.fetcher)).json(), { ok: true, solved: true, action: 'closed' });
  assert.equal(gh.writes().length, 2);
});

test('Yes, it\'s fixed with SOLVED_ACTION label only labels, and a tracking issue only gets a comment', async () => {
  const e = env({ SOLVED_ACTION: 'label' });
  await withRecord(e, 'CR-7KQ2M');
  const gh = github();
  assert.deepEqual(await (await handle(solved('CR-7KQ2M'), e, gh.fetcher)).json(), { ok: true, solved: true, action: 'labeled' });
  assert.deepEqual(gh.writes().map(c => c.method + ' ' + c.path), ['POST /repos/Coodsz/Coodsz-Crash-Reports/issues/41/labels']);

  const t = env();
  await withRecord(t, 'CR-P2D22', { tracked: true, issue: 8 });
  const tg = github();
  assert.deepEqual(await (await handle(solved('CR-P2D22'), t, tg.fetcher)).json(), { ok: true, solved: true, action: 'noted' });
  const [only, ...rest] = tg.writes();
  assert.equal(rest.length, 0, 'the shared tracking issue is never closed or labeled');
  assert.equal(only.path, '/repos/Coodsz/Coodsz-Crash-Reports/issues/8/comments');
  assert.ok(only.body.body.includes('CR-P2D22'));
  assert.ok(!only.body.body.startsWith('/reply'));
});

test('Yes, it\'s fixed needs the report\'s own key: a wrong key, another code or an unknown report change nothing', async () => {
  const e = env();
  await withRecord(e, 'CR-7KQ2M');
  await withRecord(e, 'CR-AAAAA', { key: KEY2 });
  const gh = github();
  for (const [code, key] of [['CR-7KQ2M', KEY2], ['CR-AAAAA', KEY], ['CR-ZZZZZ', KEY]])
    assert.deepEqual(await (await handle(solved(code, key), e, gh.fetcher)).json(), { ok: true, solved: false });
  assert.equal(gh.calls.length, 0);
  for (const bad of [solved('CR-7KQ2M', 'nope'), new Request('https://crash.example/replies/solved', { method: 'POST', body: 'x' })])
    assert.equal((await handle(bad, e, gh.fetcher)).status, 400);
  assert.equal((await handle(new Request('https://crash.example/replies/solved'), e)).status, 405);
});

test('Yes, it\'s fixed is rate limited by address and by key, and a GitHub failure says so', async () => {
  for (const blocked of ['solved:', 'solvedkey:']) {
    const keys = [];
    const e = env({ REPORT_LIMITER: { limit: async ({ key }) => { keys.push(key); return { success: !key.startsWith(blocked) }; } } });
    await withRecord(e, 'CR-7KQ2M');
    const gh = github();
    const res = await handle(solved('CR-7KQ2M'), e, gh.fetcher);
    assert.equal(res.status, 429);
    assert.equal(gh.calls.length, 0);
    assert.ok(keys.every(k => !k.includes(KEY)), 'the limiter key holds a hash prefix, not the key');
  }
  const e = env();
  await withRecord(e, 'CR-7KQ2M');
  const warn = console.warn;
  console.warn = () => {};
  try {
    const res = await handle(solved('CR-7KQ2M'), e, github({ status: 500 }).fetcher);
    assert.equal(res.status, 502);
    assert.equal(e.VOTES.map.has('rs:CR-7KQ2M'), false, 'not marked done, so the player can try again');
  } finally {
    console.warn = warn;
  }
});

// --- No, still broken ---

/** A stored zip with one note ({ name, text }), CRCs left at 0 (the worker does not check them). */
function noteZip(text) {
  const enc = new TextEncoder();
  const name = enc.encode('player-notes.txt');
  const data = enc.encode(text);
  const local = new Uint8Array(30 + name.length);
  const lv = new DataView(local.buffer);
  lv.setUint32(0, 0x04034b50, true);
  lv.setUint32(18, data.length, true); lv.setUint32(22, data.length, true); lv.setUint16(26, name.length, true);
  local.set(name, 30);
  const central = new Uint8Array(46 + name.length);
  const cv = new DataView(central.buffer);
  cv.setUint32(0, 0x02014b50, true);
  cv.setUint32(20, data.length, true); cv.setUint32(24, data.length, true); cv.setUint16(28, name.length, true);
  central.set(name, 46);
  const end = new Uint8Array(22);
  const ev = new DataView(end.buffer);
  ev.setUint32(0, 0x06054b50, true); ev.setUint16(8, 1, true); ev.setUint16(10, 1, true);
  ev.setUint32(12, central.length, true); ev.setUint32(16, local.length + data.length, true);
  const out = new Uint8Array(local.length + data.length + central.length + end.length);
  out.set(local, 0); out.set(data, local.length); out.set(central, local.length + data.length); out.set(end, local.length + data.length + central.length);
  return out;
}

// Every pack carries report.json, modules.json and environment.json (CrashReports.AddGenerated) next to the logs.
const PACK_FILES = [{ name: 'report.json', size: 300 }, { name: 'modules.json', size: 900 }, { name: 'environment.json', size: 200 }];
const followUpSummary = (followUp, files = [{ name: 'game/Coop_client.log', size: 1200 }, { name: 'player-notes.txt', size: 80 }, ...PACK_FILES]) => ({
  kind: 'CoodszCrashReport', schema: 1, install: INSTALL, tool: 'coop', side: 'server', reason: 'logs',
  toolVersion: '1.3.3', coop: 'v0.1.6', game: 'v1.4.8', files, followUp,
});

function postFollowUp(summary, zip) {
  const form = new FormData();
  form.set('summary', JSON.stringify(summary));
  form.set('zip', new Blob([zip], { type: 'application/zip' }), 'logs.zip');
  return new Request('https://crash.example/crash', { method: 'POST', body: form, headers: { 'CF-Connecting-IP': '203.0.113.9' } });
}

const NOTE = 'Turned off Europe 1100 and picked Vanilla, but it still closes at 192.168.1.20 after "Loading campaign" #12';

test('validateFollowUp checks the code, the key and the steps answer', () => {
  assert.equal(validateFollowUp(undefined), null);
  assert.deepEqual(validateFollowUp({ code: 'CR-7KQ2M', key: KEY, steps: 'some' }), { code: 'CR-7KQ2M', key: KEY, steps: 'some' });
  assert.equal(validateFollowUp({ code: 'CR-7KQ2M', key: KEY, steps: 'maybe' }), 'followUp steps');
  assert.equal(validateFollowUp({ code: 'CR-7KQ0M', key: KEY, steps: 'all' }), 'followUp code');
  assert.equal(validateFollowUp({ code: 'CR-7KQ2M', key: 'x', steps: 'all' }), 'followUp key');
  assert.equal(validateFollowUp('CR-7KQ2M'), 'followUp');
});

test('No, still broken goes on the same issue: a comment with the cleaned words, the steps and the new logs, opened again', async () => {
  const e = env();
  await withRecord(e, 'CR-7KQ2M');
  await e.VOTES.put('rs:CR-7KQ2M', JSON.stringify({ action: 'closed' }));
  // Closed by Yes, it's fixed: the label goes and the issue opens again.
  const gh = github({ issue: { number: 41, state: 'closed', state_reason: 'completed', labels: [{ name: 'player-solved' }] } });
  const res = await handle(postFollowUp(followUpSummary({ code: 'CR-7KQ2M', key: KEY, steps: 'all' }), noteZip(NOTE)), e, gh.fetcher);
  assert.equal(res.status, 200);
  const body = await res.json();
  assert.equal(body.followUp, 'CR-7KQ2M');
  assert.equal(body.filed, true);
  assert.notEqual(body.code, 'CR-7KQ2M', 'the new logs have their own code');
  assert.ok(e.VOTES.map.has('crash:' + body.code), 'the new zip is kept like any report');
  assert.equal(e.VOTES.map.has('rr:' + body.code), false, 'no reply record for the follow up itself');

  const writes = gh.writes().filter(c => !c.path.startsWith('/api/webhooks'));
  assert.equal(writes.some(c => c.method === 'POST' && c.path.endsWith('/issues')), false, 'no new issue');
  const posted = writes.find(c => c.path === '/repos/Coodsz/Coodsz-Crash-Reports/issues/41/comments');
  assert.ok(posted, 'the comment is on the earlier report\'s issue');
  const text = posted.body.body;
  assert.match(text, FOLLOW_UP_MARKER);
  assert.ok(text.includes('They tried all the steps.'));
  assert.ok(text.includes('> Turned off Europe 1100 and picked Vanilla'));
  assert.ok(!text.includes('192.168.1.20'), 'the address is cleaned out');
  assert.ok(!/#12\b/.test(text), 'issue refs are defanged');
  assert.ok(text.includes(`New logs from the last run: ${body.code}`));
  assert.ok(!text.includes(KEY), 'the key never reaches GitHub');
  assert.deepEqual(writes.filter(c => c.path === '/repos/Coodsz/Coodsz-Crash-Reports/issues/41/labels/player-solved').map(c => c.method), ['DELETE']);
  assert.deepEqual(writes.find(c => c.method === 'PATCH').body, { state: 'open' });
  assert.equal(e.VOTES.map.has('rs:CR-7KQ2M'), false, 'a later Yes, it\'s fixed works again');
});

test('No, still broken on a tracking issue only comments; without the key nothing is stored', async () => {
  const t = env();
  await withRecord(t, 'CR-P2D22', { tracked: true, issue: 8 });
  const tg = github();
  const ok = await (await handle(postFollowUp(followUpSummary({ code: 'CR-P2D22', key: KEY, steps: 'none' }), noteZip('still broken')), t, tg.fetcher)).json();
  assert.equal(ok.filed, true);
  assert.deepEqual(tg.writes().map(c => c.method + ' ' + c.path), ['POST /repos/Coodsz/Coodsz-Crash-Reports/issues/8/comments']);
  assert.ok(tg.writes()[0].body.body.includes('could not try the steps'));

  const e = env();
  await withRecord(e, 'CR-7KQ2M');
  const gh = github();
  const before = e.VOTES.map.size;
  for (const followUp of [{ code: 'CR-7KQ2M', key: KEY2, steps: 'all' }, { code: 'CR-ZZZZZ', key: KEY, steps: 'all' }]) {
    const res = await handle(postFollowUp(followUpSummary(followUp), noteZip(NOTE)), e, gh.fetcher);
    assert.equal(res.status, 404);
  }
  assert.equal(e.VOTES.map.size, before, 'no zip and no day count');
  assert.equal(gh.calls.length, 0);
  assert.equal((await handle(postFollowUp(followUpSummary({ code: 'CR-7KQ2M', key: KEY, steps: 'x' }), noteZip(NOTE)), e, gh.fetcher)).status, 400);
});

test('No, still broken is rate limited by key on top of the crash report limits', async () => {
  const keys = [];
  const e = env({ REPORT_LIMITER: { limit: async ({ key }) => { keys.push(key); return { success: !key.startsWith('followup:') }; } } });
  await withRecord(e, 'CR-7KQ2M');
  const gh = github();
  const res = await handle(postFollowUp(followUpSummary({ code: 'CR-7KQ2M', key: KEY, steps: 'all' }), noteZip(NOTE)), e, gh.fetcher);
  assert.equal(res.status, 429);
  assert.equal(gh.calls.length, 0);
  assert.ok(keys.some(k => k.startsWith('crash:')), 'the crash address limit runs too');
  assert.ok(keys.every(k => !k.includes(KEY)));
});

test('the follow up comment says when only words were sent', () => {
  // Only the files every pack gets, the crash dump's stack summary and a screenshot: no logs.
  const only = [{ name: 'player-notes.txt', size: 9 }, ...PACK_FILES, { name: 'crash-stack.txt', size: 9 }, { name: 'screenshots/screenshot-1.png', size: 9 }];
  const text = followUpComment('CR-7KQ2M', 'CR-AAAAA', { files: only, note: 'hi' }, 300, 'https://w', '2026-12-01', 'some');
  assert.ok(text.includes('No new logs, only their words (CR-AAAAA).'));
  assert.ok(text.includes('(only their words, CR-AAAAA)'), 'the title line says it too');
  assert.match(text, FOLLOW_UP_MARKER, 'still the worker\'s marker');
  const withLogs = followUpComment('CR-7KQ2M', 'CR-AAAAA', { files: [{ name: 'game/Coop_client.log', size: 9 }, ...PACK_FILES], note: 'hi' }, 300, 'https://w', '2026-12-01', 'all');
  assert.ok(withLogs.includes('(new logs CR-AAAAA)'));
  assert.ok(withLogs.includes('New logs from the last run: CR-AAAAA'));
  assert.match(withLogs, FOLLOW_UP_MARKER);
  assert.ok(text.includes('They tried some of the steps.'));
});

test('No, still broken never opens an issue Coodsz closed as not planned, and opens one closed as done', async () => {
  for (const [issue, opens] of [
    [{ number: 41, state: 'closed', state_reason: 'not_planned', labels: [] }, false],
    [{ number: 41, state: 'closed', state_reason: 'completed', labels: [] }, true],
    [{ number: 41, state: 'open', labels: [] }, false],
  ]) {
    const e = env();
    await withRecord(e, 'CR-7KQ2M');
    const gh = github({ issue });
    assert.equal((await handle(postFollowUp(followUpSummary({ code: 'CR-7KQ2M', key: KEY, steps: 'some' }), noteZip(NOTE)), e, gh.fetcher)).status, 200);
    const writes = gh.writes().map(c => c.method + ' ' + c.path);
    assert.equal(writes.includes('PATCH /repos/Coodsz/Coodsz-Crash-Reports/issues/41'), opens, JSON.stringify(issue));
    assert.equal(writes.some(w => w.startsWith('DELETE')), false, 'no label to take off');
  }
});

test('No, still broken whose comment cannot be posted answers 502, so the tool says try again', async () => {
  for (const status of [403, 500]) {
    const e = env();
    await withRecord(e, 'CR-7KQ2M');
    const calls = [];
    const fetcher = async url => {
      calls.push(url);
      return new Response('{}', { status });
    };
    const warn = console.warn;
    console.warn = () => {};
    try {
      const res = await handle(postFollowUp(followUpSummary({ code: 'CR-7KQ2M', key: KEY, steps: 'all' }), noteZip(NOTE)), e, fetcher);
      assert.equal(res.status, 502);
      const body = await res.json();
      assert.equal(body.ok, false);
      assert.equal(body.filed, false);
      assert.equal('code' in body, false, 'no code the tool could take as sent');
      assert.ok(!calls.some(u => u.startsWith('https://discord.com/')), 'no ping for an answer Coodsz cannot see');
      assert.deepEqual([...e.VOTES.map.keys()].filter(k => k.startsWith('crash:')), [], 'the unused zip is dropped');
      for (const [k, v] of e.VOTES.map) if (/^c[qg]:/.test(k)) assert.equal(v, '0', 'the day counts are given back: ' + k);
    } finally {
      console.warn = warn;
    }
  }
});

test('Yes, it\'s fixed on an issue that is closed already keeps its reason', async () => {
  const e = env();
  await withRecord(e, 'CR-7KQ2M');
  const gh = github({ issue: { number: 41, state: 'closed', state_reason: 'not_planned', labels: [] } });
  assert.deepEqual(await (await handle(solved('CR-7KQ2M'), e, gh.fetcher)).json(), { ok: true, solved: true, action: 'closed' });
  assert.deepEqual(gh.writes().map(c => c.method + ' ' + c.path), ['POST /repos/Coodsz/Coodsz-Crash-Reports/issues/41/labels'], 'no PATCH, so not planned stays');
});

test('No, still broken still counts when GitHub refuses to open the issue again, and says so in the log', async () => {
  const e = env();
  await withRecord(e, 'CR-7KQ2M');
  const calls = [];
  const fetcher = async (url, init = {}) => {
    const method = init.method ?? 'GET';
    calls.push(method + ' ' + new URL(url).pathname);
    if (method === 'GET') return new Response(JSON.stringify({ number: 41, state: 'closed', state_reason: 'completed', labels: [{ name: 'player-solved' }] }), { status: 200 });
    if (method === 'DELETE') return new Response('{}', { status: 404 });
    if (method === 'PATCH') return new Response('{}', { status: 422 });
    return new Response('{}', { status: 201 });
  };
  const warnings = [];
  const warn = console.warn;
  console.warn = m => warnings.push(m);
  try {
    const res = await handle(postFollowUp(followUpSummary({ code: 'CR-7KQ2M', key: KEY, steps: 'all' }), noteZip(NOTE)), e, fetcher);
    assert.equal(res.status, 200, 'the comment landed, so the answer is not lost');
    assert.ok(calls.includes('PATCH /repos/Coodsz/Coodsz-Crash-Reports/issues/41'));
    assert.ok(warnings.some(w => w.includes('could not be opened again') && w.includes('422')));
    assert.ok(!warnings.some(w => w.includes('label could not be removed')), 'a label that is gone already (404) is fine');
  } finally {
    console.warn = warn;
  }
});

test('every No, still broken on the same report pings Coodsz, under its own new code', async () => {
  const e = env({ CRASH_PING_WEBHOOK: 'https://discord.com/api/webhooks/123456/secret-token_x' });
  await withRecord(e, 'CR-7KQ2M');
  const pings = [];
  const fetcher = async (url, init = {}) => {
    if (url.startsWith('https://discord.com/')) {
      pings.push(JSON.parse(init.body).content);
      return new Response(null, { status: 204 });
    }
    if ((init.method ?? 'GET') === 'GET') return new Response(JSON.stringify({ number: 41, state: 'open', labels: [] }), { status: 200 });
    return new Response('{}', { status: 201 });
  };
  const codes = [];
  for (let i = 0; i < 2; i++) {
    const waits = [];
    const res = await handle(postFollowUp(followUpSummary({ code: 'CR-7KQ2M', key: KEY, steps: 'all' }), noteZip(NOTE)), e, fetcher, { waitUntil: p => waits.push(p) });
    codes.push((await res.json()).code);
    await Promise.all(waits);
  }
  assert.equal(pings.length, 2, 'the second one is not taken for a ping sent before');
  assert.notEqual(codes[0], codes[1]);
  for (const [i, text] of pings.entries()) {
    assert.ok(text.includes(codes[i]));
    assert.ok(text.includes('still broken after the answer to CR-7KQ2M'));
  }
});

test('Yes, it\'s fixed does not close when the issue cannot be read, so a not planned close is never turned into completed', async () => {
  const e = env();
  await withRecord(e, 'CR-7KQ2M');
  const writes = [];
  const fetcher = async (url, init = {}) => {
    const method = init.method ?? 'GET';
    if (method !== 'GET') writes.push(method + ' ' + new URL(url).pathname);
    if (method === 'GET') return new Response('{}', { status: 502 });
    return new Response('{}', { status: 200 });
  };
  const warn = console.warn;
  console.warn = () => {};
  try {
    const res = await handle(solved('CR-7KQ2M'), e, fetcher);
    assert.equal(res.status, 502, 'the player is asked to try again');
    assert.equal(writes.some(w => w.startsWith('PATCH')), false);
    assert.equal(e.VOTES.map.has('rs:CR-7KQ2M'), false, 'not marked done');
  } finally {
    console.warn = warn;
  }
});

for (const reason of ['not_planned', 'duplicate', null]) test(`Yes then No on an issue Coodsz closed with reason ${reason}: the label comes and goes, the issue stays closed`, async () => {
  const e = env();
  await withRecord(e, 'CR-7KQ2M');
  // A GitHub that keeps the issue's state, so Yes and No see what the other did.
  const issue = { number: 41, state: 'closed', state_reason: reason, labels: [] };
  const writes = [];
  const fetcher = async (url, init = {}) => {
    const method = init.method ?? 'GET';
    const path = new URL(url).pathname;
    if (url.startsWith('https://discord.com/')) return new Response(null, { status: 204 });
    if (method === 'GET') return new Response(JSON.stringify(issue), { status: 200 });
    writes.push(method + ' ' + path);
    if (method === 'POST' && path.endsWith('/labels')) issue.labels.push({ name: JSON.parse(init.body).labels[0] });
    if (method === 'DELETE') issue.labels = issue.labels.filter(l => !path.endsWith('/' + l.name));
    if (method === 'PATCH') Object.assign(issue, JSON.parse(init.body));
    return new Response('{}', { status: 200 });
  };
  assert.equal((await (await handle(solved('CR-7KQ2M'), e, fetcher)).json()).solved, true);
  assert.deepEqual(issue.labels.map(l => l.name), ['player-solved']);
  assert.equal((await handle(postFollowUp(followUpSummary({ code: 'CR-7KQ2M', key: KEY, steps: 'all' }), noteZip(NOTE)), e, fetcher)).status, 200);
  assert.deepEqual(issue.labels, [], 'No takes the label off');
  assert.equal(issue.state, 'closed');
  assert.equal(issue.state_reason, reason);
  assert.equal(writes.some(w => w.startsWith('PATCH')), false, 'never opened again, never closed as completed');
});

test('No, still broken logs when the issue cannot be read, and leaves it as it is', async () => {
  const e = env();
  await withRecord(e, 'CR-7KQ2M');
  const writes = [];
  const fetcher = async (url, init = {}) => {
    const method = init.method ?? 'GET';
    if (method === 'GET') return new Response('{}', { status: 503 });
    writes.push(method + ' ' + new URL(url).pathname);
    return new Response('{}', { status: 201 });
  };
  const warnings = [];
  const warn = console.warn;
  console.warn = m => warnings.push(m);
  try {
    assert.equal((await handle(postFollowUp(followUpSummary({ code: 'CR-7KQ2M', key: KEY, steps: 'all' }), noteZip(NOTE)), e, fetcher)).status, 200);
    assert.deepEqual(writes, ['POST /repos/Coodsz/Coodsz-Crash-Reports/issues/41/comments']);
    assert.ok(warnings.some(w => w.includes('could not be read') && w.includes('503')));
  } finally {
    console.warn = warn;
  }
});
