// node --test test/   (Node 18+, no packages)
import test from 'node:test';
import assert from 'node:assert/strict';
import { handle } from '../src/index.js';
import {
  validateSummary, cleanCrashLine, newCode, parseCode, sameSecret, isZip, crashTitle, crashShort, crashLabels,
  CODE, CODE_ALPHABET, MAX_CRASH_ZIP, CRASH_TTL, playerExtras, validateTriage, DEDUPE_TTL, MAX_EVIDENCE, SIGNATURE_LABELS, CATEGORIES, crashRepo,
  crashSeverity, crashGist, readZipEntry, readPlayerNote, quoteNote, defang, escapeLineStart, prose, MAX_NOTE_CHARS, noteText,
  validateContact, contactText, MAX_CONTACT,
} from '../src/crash.js';
import { readFileSync } from 'node:fs';
import { deflateRawSync } from 'node:zlib';

/** A zip with the given entries ({ name, text or bytes, method 0 or 8 }), CRCs left at 0 (the worker does not check them). */
function makeZip(entries) {
  const enc = new TextEncoder();
  const locals = [], centrals = [];
  let offset = 0;
  for (const e of entries) {
    const raw = e.bytes ?? enc.encode(e.text);
    const data = e.method === 8 ? new Uint8Array(deflateRawSync(raw)) : raw;
    const name = enc.encode(e.name);
    const local = new Uint8Array(30 + name.length);
    const lv = new DataView(local.buffer);
    lv.setUint32(0, 0x04034b50, true); lv.setUint16(8, e.method, true);
    lv.setUint32(18, data.length, true); lv.setUint32(22, raw.length, true); lv.setUint16(26, name.length, true);
    local.set(name, 30);
    const central = new Uint8Array(46 + name.length);
    const cv = new DataView(central.buffer);
    cv.setUint32(0, 0x02014b50, true); cv.setUint16(10, e.method, true);
    cv.setUint32(20, data.length, true); cv.setUint32(24, raw.length, true); cv.setUint16(28, name.length, true);
    cv.setUint32(42, offset, true);
    central.set(name, 46);
    locals.push(local, data);
    centrals.push(central);
    offset += local.length + data.length;
  }
  const dirSize = centrals.reduce((n, c) => n + c.length, 0);
  const end = new Uint8Array(22);
  const ev = new DataView(end.buffer);
  ev.setUint32(0, 0x06054b50, true); ev.setUint16(8, entries.length, true); ev.setUint16(10, entries.length, true);
  ev.setUint32(12, dirSize, true); ev.setUint32(16, offset, true);
  const parts = [...locals, ...centrals, end];
  const out = new Uint8Array(parts.reduce((n, p) => n + p.length, 0));
  let o = 0;
  for (const p of parts) { out.set(p, o); o += p.length; }
  return out;
}

const INSTALL = '0123456789abcdef0123456789abcdef';
// An empty zip: just the end of central directory record.
const EMPTY_ZIP = new Uint8Array([0x50, 0x4b, 0x05, 0x06, ...new Array(18).fill(0)]);

function kv() {
  const map = new Map();
  const options = new Map();
  return {
    map, options,
    get: async (k, opts) => {
      if (!map.has(k)) return null;
      const v = map.get(k);
      return opts?.type === 'arrayBuffer' && v instanceof Uint8Array ? v.buffer.slice(v.byteOffset, v.byteOffset + v.byteLength) : v;
    },
    put: async (k, v, opts) => { map.set(k, v); options.set(k, opts); },
  };
}

// answer(url, method) may return { status, body } for a call (the tracking issue GET); the rest get status.
function github(status = 201, answer = () => null) {
  const calls = [];
  const fetcher = async (url, init) => {
    calls.push({ url, method: init.method, body: init.body === undefined ? undefined : JSON.parse(init.body), auth: init.headers.Authorization });
    const special = answer(url, init.method);
    if (special) return new Response(JSON.stringify(special.body), { status: special.status });
    return new Response(JSON.stringify({ number: 41 }), { status });
  };
  return { calls, fetcher };
}

const env = (extra = {}) => ({ GITHUB_TOKEN: 'test-token', VOTER_SALT: 'salt', VOTES: kv(), ADMIN_KEY: 'admin-secret', ...extra });

const summary = (over = {}) => ({
  kind: 'CoodszCrashReport', schema: 1, install: INSTALL, tool: 'coop', side: 'client', reason: 'crash',
  toolVersion: '1.2.62', coop: 'v0.1.6', game: 'v1.4.8',
  crashLine: 'System.NullReferenceException: Object reference not set to an instance of an object.',
  suspectedMod: 'Bannerlord.Diplomacy',
  files: [{ name: 'game/Coop_client.log', size: 1200 }, { name: 'NOTES.txt', size: 300 }],
  ...over,
});

function post(body, zip = EMPTY_ZIP, headers = {}) {
  const form = new FormData();
  form.set('summary', typeof body === 'string' ? body : JSON.stringify(body));
  if (zip) form.set('zip', new Blob([zip], { type: 'application/zip' }), 'logs.zip');
  return new Request('https://crash.example/crash', { method: 'POST', body: form, headers: { 'CF-Connecting-IP': '203.0.113.9', ...headers } });
}

const get = (code, key) => new Request('https://crash.example/crash/' + code, { headers: key === undefined ? {} : { 'X-Admin-Key': key } });

test('a crash report keeps the zip for 30 days, files a private issue and answers with a short code', async () => {
  const e = env({ CRASH_REPO: 'Coodsz/Coodsz-Crash-Reports' });
  const gh = github();
  const res = await handle(post(summary()), e, gh.fetcher);
  assert.equal(res.status, 200);
  const body = await res.json();
  assert.equal(body.ok, true);
  assert.equal(body.filed, true);
  assert.match(body.code, CODE);

  const key = 'crash:' + body.code;
  assert.ok(e.VOTES.map.get(key) instanceof Uint8Array);
  assert.equal(e.VOTES.options.get(key).expirationTtl, CRASH_TTL);
  assert.equal(CRASH_TTL, 30 * 24 * 3600);

  const issue = gh.calls[0];
  assert.equal(issue.url, 'https://api.github.com/repos/Coodsz/Coodsz-Crash-Reports/issues');
  assert.deepEqual(issue.body.labels, ['crash-report', 'client', 'coop', 'new']);
  assert.equal(issue.body.title, `Coop and Friends game crash, NullReferenceException [${body.code}]`);
  assert.ok(issue.body.body.includes('game/Coop_client.log'));
  assert.ok(issue.body.body.includes('Bannerlord.Diplomacy'));
  assert.ok(!issue.body.body.includes(INSTALL), 'the raw install id never reaches the issue');
  assert.ok(!issue.body.body.includes('203.0.113.9'), 'the address never reaches the issue');
  assert.ok(![...e.VOTES.map.keys()].some(k => k.includes(INSTALL)));
});

test('server reports get the server label and their tool label', async () => {
  const gh = github();
  await handle(post(summary({ tool: 'taom', side: 'server', reason: 'server-crash', crashLine: '' })), env(), gh.fetcher);
  assert.deepEqual(gh.calls[0].body.labels, ['crash-report', 'server', 'taom', 'new']);
  assert.match(gh.calls[0].body.title, /^TAOM server crash, no error line in the logs \[CR-[A-Z2-9]{5}\]$/);
  const ad = github();
  await handle(post(summary({ tool: 'ad1259', reason: 'logs', problem: 'join', crashLine: null })), env(), ad.fetcher);
  assert.deepEqual(ad.calls[0].body.labels, ['crash-report', 'client', 'ad1259', 'new']);
  assert.match(ad.calls[0].body.title, /^AD1259 game logs, stuck joining \[CR-[A-Z2-9]{5}\]$/);
});

test('Burning Empires crash reports and logs are accepted and filed in the BE crash repository with the be label', async () => {
  assert.equal(validateSummary(summary({ tool: 'be' })).tool, 'be');
  assert.equal(validateSummary(summary({ tool: 'burning-empires' })), 'tool');
  const server = github();
  const res = await handle(post(summary({ tool: 'be', side: 'server', reason: 'server-crash', crashLine: '' })), env(), server.fetcher);
  assert.equal(res.status, 200);
  assert.equal(server.calls[0].url, 'https://api.github.com/repos/Coodsz/Coodsz-BurningEmpires-Crash-Reports/issues');
  assert.deepEqual(server.calls[0].body.labels, ['crash-report', 'server', 'be', 'new']);
  assert.match(server.calls[0].body.title, /^Burning Empires server crash, no error line in the logs \[CR-[A-Z2-9]{5}\]$/);
  assert.ok(server.calls[0].body.body.includes('- Tool: Burning Empires server'));
  const logs = github();
  await handle(post(summary({ tool: 'be', reason: 'logs', problem: 'join', crashLine: null })), env({ BE_CRASH_REPO: 'Me/BE-Crash-Test' }), logs.fetcher);
  assert.equal(logs.calls[0].url, 'https://api.github.com/repos/Me/BE-Crash-Test/issues');
  assert.match(logs.calls[0].body.title, /^Burning Empires game logs, stuck joining \[CR-[A-Z2-9]{5}\]$/);
});

test('Realm of Thrones crash reports and logs are accepted and filed in the RoT crash repository with the rot label', async () => {
  assert.equal(validateSummary(summary({ tool: 'rot' })).tool, 'rot');
  assert.equal(validateSummary(summary({ tool: 'realm-of-thrones' })), 'tool');
  const server = github();
  const res = await handle(post(summary({ tool: 'rot', side: 'server', reason: 'server-crash', crashLine: '' })), env(), server.fetcher);
  assert.equal(res.status, 200);
  assert.equal(server.calls[0].url, 'https://api.github.com/repos/Coodsz/Coodsz-RealmOfThrones-Crash-Reports/issues');
  assert.deepEqual(server.calls[0].body.labels, ['crash-report', 'server', 'rot', 'new']);
  assert.match(server.calls[0].body.title, /^Realm of Thrones server crash, no error line in the logs \[CR-[A-Z2-9]{5}\]$/);
  assert.ok(server.calls[0].body.body.includes('- Tool: Realm of Thrones server'));
  const logs = github();
  await handle(post(summary({ tool: 'rot', reason: 'logs', problem: 'join', crashLine: null })), env({ ROT_CRASH_REPO: 'Me/RoT-Crash-Test' }), logs.fetcher);
  assert.equal(logs.calls[0].url, 'https://api.github.com/repos/Me/RoT-Crash-Test/issues');
  assert.match(logs.calls[0].body.title, /^Realm of Thrones game logs, stuck joining \[CR-[A-Z2-9]{5}\]$/);
});

test('the issue never holds raw log text beyond the one cleaned crash line', async () => {
  const gh = github();
  const line = 'Crash at C:\\Users\\Pat\\Documents from 192.168.1.20 steam 76561198000000001 password=hunter2 `code`';
  await handle(post(summary({ crashLine: line })), env(), gh.fetcher);
  const { title, body } = gh.calls[0].body;
  for (const secret of ['Pat', '192.168.1.20', '76561198000000001', 'hunter2', '`code`'])
    assert.ok(!title.includes(secret) && !body.includes(secret), secret + ' was left in');
  assert.ok(body.includes('[address]') && body.includes('[steam id]'));
});

test('the zip is still kept and the code returned when GitHub fails or no token is set', async () => {
  const e = env();
  const res = await (await handle(post(summary()), e, github(500).fetcher)).json();
  assert.equal(res.ok, true);
  assert.equal(res.filed, false);
  assert.ok(e.VOTES.map.has('crash:' + res.code));

  const noToken = await (await handle(post(summary()), env({ GITHUB_TOKEN: undefined }), github().fetcher)).json();
  assert.equal(noToken.ok, true);
  assert.equal(noToken.filed, false);

  const own = github();
  await handle(post(summary()), env({ CRASH_GITHUB_TOKEN: 'crash-token' }), own.fetcher);
  assert.equal(own.calls[0].auth, 'Bearer crash-token', 'the crash token wins over the mod report token');
});

test('bad uploads are refused', async () => {
  const gh = github();
  assert.equal((await handle(post(summary(), new Uint8Array([1, 2, 3, 4])), env(), gh.fetcher)).status, 400, 'not a zip');
  assert.equal((await handle(post(summary(), null), env(), gh.fetcher)).status, 400, 'no zip');
  assert.equal((await handle(post('{'), env(), gh.fetcher)).status, 400, 'summary not JSON');
  assert.equal((await handle(post(summary({ tool: 'other' })), env(), gh.fetcher)).status, 400);
  assert.equal((await handle(new Request('https://crash.example/crash', { method: 'POST', body: 'plain' }), env(), gh.fetcher)).status, 400);
  assert.equal((await handle(new Request('https://crash.example/crash'), env(), gh.fetcher)).status, 405);
  assert.equal((await handle(post(summary()), { VOTES: kv() }, gh.fetcher)).status, 503, 'no salt, no reports');

  const big = new Uint8Array(MAX_CRASH_ZIP + 1);
  big.set([0x50, 0x4b, 0x03, 0x04]);
  assert.equal((await handle(post(summary(), big), env(), gh.fetcher)).status, 413, 'over the 8 MB cap');
  const huge = post(summary(), EMPTY_ZIP, { 'content-length': String(20 * 1024 * 1024) });
  assert.equal((await handle(huge, env(), gh.fetcher)).status, 413, 'a too large content-length is refused before reading');
  assert.equal(gh.calls.length, 0);
});

test('summary checks keep only known fields and refuse private or odd values', () => {
  assert.equal(validateSummary(summary({ install: 'nope' })), 'install');
  assert.equal(validateSummary(summary({ side: 'both' })), 'side');
  assert.equal(validateSummary(summary({ reason: 'bored' })), 'reason');
  assert.equal(validateSummary(summary({ problem: 'boss' })), 'problem');
  assert.equal(validateSummary(summary({ suspectedMod: 'C:\\Users\\Pat' })), 'suspectedMod');
  assert.equal(validateSummary(summary({ suspectedMod: '76561198000000001' })), 'suspectedMod');
  assert.equal(validateSummary(summary({ files: [{ name: '../etc/passwd', size: 1 }] })), 'file name');
  assert.equal(validateSummary(summary({ files: [{ name: 'C:\\Users\\Pat\\x.log', size: 1 }] })), 'file name');
  assert.equal(validateSummary(summary({ files: [{ name: 'a.log', size: -1 }] })), 'file size');
  assert.equal(validateSummary(summary({ toolVersion: '76561198000000001' })), 'toolVersion');
  const ok = validateSummary(summary({ steamName: 'Pat', extra: 1 }));
  assert.equal(typeof ok, 'object');
  assert.ok(!JSON.stringify(ok).includes('Pat'), 'unknown fields are dropped');
});

test('limits: the address limiter with its own key, the per install day cap and the global day cap', async () => {
  const keys = [];
  const limited = await handle(post(summary()), env({ REPORT_LIMITER: { limit: async ({ key }) => { keys.push(key); return { success: false }; } } }), github().fetcher);
  assert.equal(limited.status, 429);
  assert.deepEqual(keys, ['crash:203.0.113.9'], 'crash reports count separately from mod reports');

  const e = env({ MAX_CRASH_PER_INSTALL_DAY: '1' });
  assert.equal((await handle(post(summary()), e, github().fetcher)).status, 200);
  assert.equal((await handle(post(summary()), e, github().fetcher)).status, 429);
  assert.equal((await handle(post(summary({ install: 'f'.repeat(32) })), e, github().fetcher)).status, 200, 'another install has its own cap');

  const g = env({ MAX_CRASH_REPORTS_DAY: '1' });
  await handle(post(summary()), g, github().fetcher);
  assert.equal((await handle(post(summary({ install: 'e'.repeat(32) })), g, github().fetcher)).status, 429);
});

test('codes use only unambiguous characters and a taken code is never reused', async () => {
  for (const c of '01OIL') assert.ok(!CODE_ALPHABET.includes(c));
  for (let i = 0; i < 200; i++) assert.match(newCode(), CODE);
  assert.equal(parseCode(' cr-7kq2m '), 'CR-7KQ2M');
  assert.equal(parseCode('CR-0OIL1'), null);
  assert.equal(parseCode('CR-7KQ2'), null);

  // The first two codes drawn are taken, the third is free.
  const queue = [new Uint8Array(8).fill(0), new Uint8Array(8).fill(0), new Uint8Array(8).fill(1)];
  const e = env({ CODE_RANDOM: b => { b.set(queue.length > 1 ? queue.shift() : queue[0]); return b; } });
  e.VOTES.map.set('crash:CR-22222', new Uint8Array(1));
  const res = await (await handle(post(summary()), e, github().fetcher)).json();
  assert.equal(res.code, 'CR-33333');
});

test('GET /crash/<code> returns the zip only with the right admin key', async () => {
  const e = env();
  const { code } = await (await handle(post(summary()), e, github().fetcher)).json();

  assert.equal((await handle(get(code), e)).status, 401, 'no key');
  assert.equal((await handle(get(code, 'wrong'), e)).status, 401, 'wrong key');
  assert.equal((await handle(get(code, 'admin-secret'), env({ ADMIN_KEY: undefined, VOTES: e.VOTES }))).status, 503, 'no ADMIN_KEY set');
  assert.equal((await handle(get('CR-22222', 'admin-secret'), e)).status, 404);
  assert.equal((await handle(get('nonsense', 'admin-secret'), e)).status, 400);

  const res = await handle(get(code.toLowerCase(), 'admin-secret'), e);
  assert.equal(res.status, 200);
  assert.equal(res.headers.get('content-type'), 'application/zip');
  assert.ok(res.headers.get('content-disposition').includes(code + '.zip'));
  assert.deepEqual(new Uint8Array(await res.arrayBuffer()), EMPTY_ZIP);
});

test('helpers: constant time compare, zip magic, the crash line cleaner, title and labels', async () => {
  assert.equal(await sameSecret('abc', 'abc'), true);
  assert.equal(await sameSecret('abd', 'abc'), false);
  assert.equal(await sameSecret('', ''), false, 'an empty key never matches');
  assert.equal(isZip(new Uint8Array([0x50, 0x4b, 0x03, 0x04])), true);
  assert.equal(isZip(new Uint8Array([0x50, 0x4b])), false);
  assert.equal(cleanCrashLine('a\r\nb\tc'), 'a b c');
  assert.equal(cleanCrashLine('x'.repeat(400)).length, 300);
  assert.equal(cleanCrashLine('token: abc123'), 'token: [password]');
  assert.equal(cleanCrashLine("assembly 'Bannerlord.UIExtenderEx, Version=2.14.0.0, Culture=neutral'"), "assembly 'Bannerlord.UIExtenderEx, Version=2.14.0.0, Culture=neutral'");
  assert.equal(cleanCrashLine('Harmony: server runtime 2.4.2.248 matches'), 'Harmony: server runtime 2.4.2.248 matches');
  assert.equal(cleanCrashLine('Router internet address 81.2.3.4'), 'Router internet address [address]');
  assert.equal(cleanCrashLine('on "192.168.1.20:53295"'), 'on "[address]:53295"');
  assert.equal(cleanCrashLine('Server version 10.0.0.5:7210 is up'), 'Server version [address]:7210 is up');
  assert.equal(cleanCrashLine('conversion 10.0.0.5'), 'conversion [address]');
  assert.equal(cleanCrashLine('FileVersion: 1.2.3.4'), 'FileVersion: 1.2.3.4');
  const s = validateSummary(summary({ crashLine: 'y'.repeat(400) }));
  assert.ok(crashTitle('CR-23456', s).length <= 200);
  assert.deepEqual(crashLabels(s), ['crash-report', 'client', 'coop', 'new']);
});

test('cleanCrashLine: redacts identifiers, paths, secrets and unsafe characters', () => {
  assert.equal(cleanCrashLine('76561198012345678'), '[steam id]');
  assert.equal(cleanCrashLine('[U:1:12345]'), '[steam id]');
  assert.equal(cleanCrashLine('C:\\Users\\Bob\\AppData\\x'), 'C:\\Users\\[user]\\AppData\\x');
  assert.equal(cleanCrashLine('C:/Users/Bob/AppData/x'), 'C:/Users/[user]/AppData/x');
  assert.equal(cleanCrashLine('password=hunter2'), 'password=[password]');
  assert.equal(cleanCrashLine('a\u200Bb\u202Ec'), 'abc');
  assert.equal(cleanCrashLine('`<>@'), "''''");
  assert.equal(cleanCrashLine(undefined), '');
  assert.equal(cleanCrashLine(null), '');
  assert.equal(cleanCrashLine(42), '');
  assert.equal(cleanCrashLine({}), '');
});

test('a malformed escape in the code is a 400, not a 500', async () => {
  assert.equal(parseCode('%E0%A4%A'), null);
  assert.equal(parseCode('%'), null);
  const res = await handle(get('%E0%A4%A', 'admin-secret'), env());
  assert.equal(res.status, 400);
});

test('a failing KV counter or a missing address limiter never turns into a 500', async () => {
  // Counter keys (cq: and cg:) fail to read and write; the zip itself is still stored.
  const store = kv();
  const flaky = {
    ...store,
    get: async (k, opts) => { if (/^c[qg]:/.test(k)) throw new Error('KV get failed'); return store.get(k, opts); },
    put: async (k, v, opts) => { if (/^c[qg]:/.test(k)) throw new Error('KV put limit'); return store.put(k, v, opts); },
  };
  const warnings = [];
  const warn = console.warn;
  console.warn = line => warnings.push(String(line));
  try {
    const res = await handle(post(summary()), env({ VOTES: flaky }), github().fetcher);
    assert.equal(res.status, 200);
    assert.match((await res.json()).code, CODE);
    assert.ok(warnings.some(line => line.includes('REPORT_LIMITER is not bound')), 'the missing binding is logged');
    assert.ok(warnings.some(line => line.includes('could not be updated')), 'the failed counter is logged');

    // A limiter that throws lets the report through too.
    const broken = { limit: async () => { throw new Error('limiter down'); } };
    assert.equal((await handle(post(summary()), env({ REPORT_LIMITER: broken }), github().fetcher)).status, 200);

    // A zip that cannot be stored is a 503 the tool shows as try again, never a 500.
    const full = { ...store, put: async () => { throw new Error('KV put failed'); } };
    assert.equal((await handle(post(summary()), env({ VOTES: full }), github().fetcher)).status, 503);
  } finally {
    console.warn = warn;
  }
});

// The v1.2.62 live test: Coodsz Host's server crash report with 25 files was refused with 400.
// The summary was fine; the Cloudflare runtime refused the form because .NET wrote name=summary
// without quotes. The tools now quote every part name, exactly like the body below.
const LIVE_SERVER_FILES = [
  'server-logs/coop-server-20261012-143749.log', 'server-logs/coop-server-20261012-142550.log', 'server-logs/coop-server-20261012-141306.log',
  'host-console/host-console-20261022-230234.log', 'host-console/host-console-20261019-002609.log', 'host-console/host-console-20261019-002049.log',
  'host/host-action.log', 'settings/server-config.json', 'server-logs/ServerSettingsLock.log', 'settings/mod-config.json',
  'server-folder/release-info.txt', 'server-folder/modded-server-selection.json', 'server-folder/invalid-module-versions.log',
  'engine-logs/rgl_log_22960.txt', 'engine-logs/rgl_log_errors_32216.txt', 'engine-logs/rgl_log_32216.txt', 'engine-logs/rgl_log_31088.txt',
  'engine-logs/watchdog_log_32216.txt', 'engine-logs/watchdog_log_22960.txt', 'host/server-window.txt', 'host/console-window.txt',
  'NOTES.txt', 'modules.json', 'environment.json', 'report.json',
].map((name, i) => ({ name, size: 1000 + i }));

function dotnetForm(summaryText, zip) {
  const boundary = 'cded3524-7e07-47dd-be9a-04f5c202eb4a';
  const head = `--${boundary}\r\nContent-Type: application/json; charset=utf-8\r\nContent-Disposition: form-data; name="summary"\r\n\r\n${summaryText}\r\n`
    + `--${boundary}\r\nContent-Type: application/zip\r\nContent-Disposition: form-data; name="zip"; filename="logs.zip"\r\n\r\n`;
  const tail = `\r\n--${boundary}--\r\n`;
  const body = new Uint8Array([...new TextEncoder().encode(head), ...zip, ...new TextEncoder().encode(tail)]);
  return new Request('https://crash.example/crash', {
    method: 'POST', body,
    headers: { 'content-type': `multipart/form-data; boundary="${boundary}"`, 'CF-Connecting-IP': '203.0.113.9' },
  });
}

test('the live server crash report summary, sent the way the tools send it now, gets a code', async () => {
  const live = summary({ side: 'server', reason: 'server-crash', coop: '', game: '', crashLine: '', suspectedMod: undefined, files: LIVE_SERVER_FILES });
  delete live.suspectedMod;
  const checked = validateSummary(live);
  assert.equal(typeof checked, 'object', `refused: ${checked}`);
  assert.equal(checked.files.length, 25);
  const res = await handle(dotnetForm(JSON.stringify(live, null, 2), EMPTY_ZIP), env(), github().fetcher);
  assert.equal(res.status, 200);
  assert.match((await res.json()).code, CODE);
});

test('a form the parser refuses says why in the 400', async () => {
  const res = await handle(new Request('https://crash.example/crash', {
    method: 'POST', body: 'not multipart at all',
    headers: { 'content-type': 'multipart/form-data; boundary="nope"', 'CF-Connecting-IP': '203.0.113.9' },
  }), env(), github().fetcher);
  assert.equal(res.status, 400);
  const answer = await res.json();
  assert.match(answer.error, /^not a form: .+/);
  assert.ok(answer.error.length <= 'not a form: '.length + 200);
});

test('a report with the player\'s explanation and screenshots says so in the issue, without their words, and is accepted like any other', async () => {
  const gh = github();
  const files = [
    { name: 'game/Coop_client.log', size: 1200 },
    { name: 'player-notes.txt', size: 420 },
    { name: 'screenshots/screenshot-1.png', size: 900000 },
    { name: 'screenshots/screenshot-2.jpg', size: 500000 },
    { name: 'report.json', size: 2000 },
  ];
  const res = await handle(post(summary({ reason: 'logs', problem: 'other', crashLine: '', files })), env(), gh.fetcher);
  assert.equal(res.status, 200);
  const { body } = gh.calls[0].body;
  // The list names a note but this zip holds none, so the issue points at the zip instead.
  assert.ok(body.includes('## What the player said\n\nThey picked: Something else.\n\nThey added a note (player-notes.txt) and 2 screenshots (screenshots/), in the zip.'));
  assert.ok(body.includes('screenshots/screenshot-1.png'));
});

test('older tools without extras say No note', async () => {
  const gh = github();
  await handle(post(summary()), env(), gh.fetcher);
  assert.ok(gh.calls[0].body.body.includes('## What the player said\n\nNo note.'));
  assert.equal(playerExtras([{ name: 'screenshots/screenshot-1.jpg', size: 1 }]), '1 screenshot (screenshots/)');
  assert.equal(playerExtras([{ name: 'NOTES.txt', size: 1 }]), '');
  assert.equal(playerExtras([{ name: 'crash-dumps/2026-10-26_11.55-dump.dmp', size: 1 }]), 'a crash dump (crash-dumps/, raw memory, not cleaned)');
});

test('an issue with a crash dump says the dump is raw memory and not cleaned', async () => {
  const gh = github();
  const s = summary();
  s.files = [...s.files, { name: 'crash-dumps/2026-10-26_11.55-dump.dmp', size: 3_000_000 }];
  await handle(post(s), env(), gh.fetcher);
  const body = gh.calls[0].body.body;
  assert.ok(body.includes('a crash dump (crash-dumps/, raw memory, not cleaned)'));
  assert.ok(body.includes('This report includes a crash dump (raw memory, not cleaned)'));

  const plain = github();
  await handle(post(summary()), env(), plain.fetcher);
  assert.ok(!plain.calls[0].body.body.includes('crash dump'));
});

// ----- Crash auto triage -----

const siege = (over = {}) => ({
  signature: 'coop-siege-teams-loop', category: 'coop-bug', signaturesVersion: '2026.10.24',
  title: 'Endless loading after joining a running siege battle',
  whatHappened: 'You joined a town siege battle that was already running.',
  reportNeeded: true, trackingIssue: 8, labels: ['siege'],
  evidence: ['TaleWorlds.Core.MBIllegalValueException: Number of teams is not 0.', 'at TaleWorlds.MountAndBlade.DeploymentPoint.SetBreachSideDeploymentPoint()'],
  ...over,
});
const cleanExit = (over = {}) => ({
  signature: 'client-clean-exit', category: 'not-a-crash', signaturesVersion: '2026.10.24',
  title: 'The game closed normally', whatHappened: 'You quit the game the normal way.',
  reportNeeded: false, labels: [], evidence: ['Exit code -1', '[20:54:02.156] Managed Interface deleted.'],
  ...over,
});
const trackingIssue = (labels = ['tracking'], state = 'open') =>
  (url, method) => (method === 'GET' && url.endsWith('/issues/8') ? { status: 200, body: { number: 8, state, labels: labels.map(name => ({ name })) } } : null);

test('a known crash gets the known and category labels, the triage in the issue body, no second comment, and not-a-crash is closed', async () => {
  const gh = github();
  const res = await (await handle(post(summary({ triage: cleanExit() })), env(), gh.fetcher)).json();
  assert.equal(res.ok, true);
  assert.equal(res.filed, true);
  const [issue, close] = gh.calls;
  assert.deepEqual(issue.body.labels, ['crash-report', 'client', 'coop', 'new', 'known', 'not-a-crash']);
  assert.equal(issue.body.title, `Coop and Friends game, no crash, the game closed normally [${res.code}]`);
  const body = issue.body.body;
  assert.ok(body.includes('Triage: known, client-clean-exit (not-a-crash), signatures 2026.10.24'));
  assert.ok(body.includes('## What happened\n\nYou quit the game the normal way.'));
  assert.ok(body.includes('## Known cause?\n\nYes. The game closed normally. This was not a crash'));
  assert.ok(body.includes('## Severity\n\nLow.'));
  assert.ok(body.includes('Managed Interface deleted.'));
  assert.equal(close.method, 'PATCH');
  assert.equal(close.url, 'https://api.github.com/repos/Coodsz/Coodsz-Crash-Reports/issues/41');
  assert.deepEqual(close.body, { state: 'closed', state_reason: 'not_planned' });
  assert.equal(gh.calls.length, 2, 'the issue and the close, no comment');

  // Another known category is labeled, not commented and not closed.
  const assets = github();
  await handle(post(summary({ side: 'server', reason: 'server-crash', triage: { ...cleanExit(), signature: 'server-raw-assets', category: 'mod-assets', labels: ['assets'] } })), env(), assets.fetcher);
  assert.deepEqual(assets.calls[0].body.labels, ['crash-report', 'server', 'coop', 'new', 'known', 'mod-assets', 'assets']);
  assert.equal(assets.calls.length, 1);
});

test('an unknown crash is labeled unknown, High severity and needs a look; older tools get no triage label at all', async () => {
  const gh = github();
  await handle(post(summary({ triage: { signature: null, signaturesVersion: '2026.10.24', evidence: [] } })), env(), gh.fetcher);
  assert.deepEqual(gh.calls[0].body.labels, ['crash-report', 'client', 'coop', 'new', 'unknown']);
  const body = gh.calls[0].body.body;
  assert.ok(body.includes('Triage: unknown, signatures 2026.10.24'));
  assert.ok(body.includes('## Known cause?\n\nNot yet, this one needs a look.'));
  assert.ok(body.includes('## Severity\n\nHigh.'));
  assert.ok(body.includes('In plain words, something the code expected was missing'));
  assert.equal(gh.calls.length, 1);

  const old = github();
  await handle(post(summary()), env(), old.fetcher);
  assert.deepEqual(old.calls[0].body.labels, ['crash-report', 'client', 'coop', 'new']);
  assert.ok(!old.calls[0].body.body.includes('Triage:'));
  assert.ok(!old.calls[0].body.body.includes('coodsz-crash-triage'));
});

test('the issue reads top to bottom: the marker, the code, the sections in order and the technical details folded', async () => {
  const gh = github();
  const res = await (await handle(post(summary()), env(), gh.fetcher)).json();
  const body = gh.calls[0].body.body;
  assert.ok(body.startsWith(`<!-- coodsz-crash-report v1 -->\nReport code **${res.code}**\n`));
  const order = ['## What happened', '## Known cause?', '## What the player said', '## Severity', '## Player and setup', '<details><summary>Technical details</summary>', '</details>'];
  const at = order.map(h => body.indexOf(h));
  assert.ok(at.every(i => i >= 0), 'every section is there');
  assert.deepEqual([...at].sort((a, b) => a - b), at, 'in this order');
  const details = body.slice(at[5]);
  for (const part of ['```\nSystem.NullReferenceException', 'game/Coop_client.log', 'kept until', `curl.exe -H "X-Admin-Key: <ADMIN_KEY>" -o ${res.code}.zip https://crash.example/crash/${res.code}`])
    assert.ok(details.includes(part), part + ' is in the technical details');
  for (const line of ['- Tool: Coop and Friends client 1.2.62', '- Coop: v0.1.6', '- Bannerlord: v1.4.8', '- Suspected mod: Bannerlord.Diplomacy'])
    assert.ok(body.includes(line), line);
  assert.ok(body.includes('## Known cause?\n\nNot checked'), 'an older tool says triage did not run');
});

test('severity: High unknown, Average known bug or known cause that wants a report, Low no report needed or not a crash', () => {
  const sev = over => crashSeverity(validateSummary(summary(over))).level;
  assert.equal(sev({}), 'High');
  assert.equal(sev({ triage: { signature: null, signaturesVersion: '1' } }), 'High');
  assert.equal(sev({ triage: siege() }), 'Average');
  assert.equal(sev({ triage: siege({ category: 'setup', reportNeeded: true }) }), 'Average');
  assert.equal(sev({ triage: siege({ category: 'setup', reportNeeded: false, trackingIssue: null }) }), 'Low');
  assert.equal(sev({ triage: cleanExit() }), 'Low');
  assert.equal(sev({ reason: 'logs', problem: 'desync', crashLine: '' }), 'Average');
  // Send my logs that matched a setup hint the player can fix: the player sent the logs anyway, so never Low.
  const hint = { reason: 'logs', crashLine: '', triage: siege({ signature: 'smart-app-control-blocked', category: 'setup', reportNeeded: false, trackingIssue: null }) };
  assert.equal(sev(hint), 'Average');
  assert.match(crashSeverity(validateSummary(summary(hint))).why, /known fix/);
});

test('titles: tool, game or server, crash or logs, then the known cause or the error in a few words, and the code at the end', () => {
  const s = over => validateSummary(summary(over));
  assert.equal(crashTitle('CR-23456', s({ side: 'server', reason: 'server-crash', triage: siege({ title: 'Server would not open the save after a mod order change' }) })),
    'Coop and Friends server crash, server would not open the save after a mod order change [CR-23456]');
  // The DedicatedServer host's 'DS'{json} line comes down to the error and where it happened.
  const ds = String.raw`'DS'{"ev":"state","phase":"fatal","save":"MP","detail":"Game.OnTick threw before Coop robustness suppression: System.IndexOutOfRangeException: Index was outside the bounds of the array.
 at TaleWorlds.CampaignSystem.Roster.TroopRoster.AddToCountsAtIndex_Patch1(TroopRoster this, Int32 index, In...`;
  assert.equal(crashTitle('CR-23456', s({ tool: 'taom', side: 'server', reason: 'server-crash', crashLine: ds })),
    'TAOM server crash, IndexOutOfRange in TroopRoster.AddToCountsAtIndex [CR-23456]');
  assert.equal(crashShort('System.NullReferenceException: Object reference not set to an instance of an object. at Coop.Core.HeroChangeNameData..ctor(Hero hero)'),
    'NullReference in HeroChangeNameData constructor');
  assert.equal(crashShort('[20:54:02.156] ERROR: TaleWorlds.Core.MBIllegalValueException: Number of teams is not 0.'), 'MBIllegalValue, Number of teams is not 0.');
  assert.equal(crashShort('Server does not support module X'), 'Server does not support module X');
  assert.equal(crashShort('Note The server crashed while loading the save. It starts once more with the deeper save.'), 'The server crashed while loading the save.');
  assert.equal(crashTitle('CR-23456', s({ reason: 'logs', problem: 'join', crashLine: '' })), 'Coop and Friends game logs, stuck joining [CR-23456]');
  assert.equal(crashGist('[20:54:02.156] ERROR: TaleWorlds.Core.MBIllegalValueException: Number of teams is not 0.'), 'MBIllegalValueException: Number of teams is not 0.');
  const long = crashTitle('CR-23456', s({ crashLine: 'System.Exception: ' + 'word '.repeat(100) }));
  assert.ok(long.length <= 160, long);
  assert.ok(long.endsWith('... [CR-23456]') && long.length <= 110, long);
  // Escaped triage text reads plain in the title, and nothing in it can form a link or an image.
  const evil = crashTitle('CR-23456', s({ triage: siege({ title: 'Open ![p](https://evil.example/p.png) [x](https://evil.example)' }) }));
  assert.ok(!/[[\]!`\\]/.test(evil.replace(/\[CR-23456\]$/, '')), evil);
  const line = crashTitle('CR-23456', s({ crashLine: 'Bad [x](https://evil.example) ![i](https://evil.example/i.png)' }));
  assert.ok(!/[[!]/.test(line.replace(/\[CR-23456\]$/, '')), line);
});

test('the body escapes player text outside code blocks, and a whatToDo the tool sends goes under Known cause', async () => {
  const issue = github();
  await handle(post(summary({ crashLine: 'Error [x](https://evil.example) # head', triage: siege({ trackingIssue: null, whatToDo: 'Close the game and join again.' }) })), env(), issue.fetcher);
  const b = issue.calls[0].body.body;
  assert.ok(b.includes('What the player was told to do: Close the game and join again.'));
  assert.ok(b.includes('It is a known Coop bug, this report adds to it.'));
  assert.ok(!/(^|[^\\])\]\(http/.test(b.replace(/```[\s\S]*?```/g, '')), 'no link outside code blocks');
});

test('a known coop bug with a tracking issue becomes a comment there instead of a new issue', async () => {
  const e = env();
  const gh = github(201, trackingIssue());
  const res = await (await handle(post(summary({ triage: siege() })), e, gh.fetcher)).json();
  assert.equal(res.ok, true);
  assert.equal(res.filed, true);
  assert.equal(res.tracked, 8);
  assert.ok(e.VOTES.map.has('crash:' + res.code), 'the zip is kept as always');
  assert.equal(gh.calls.length, 2);
  assert.equal(gh.calls[0].method, 'GET');
  const comment = gh.calls[1];
  assert.equal(comment.url, 'https://api.github.com/repos/Coodsz/Coodsz-Crash-Reports/issues/8/comments');
  assert.ok(comment.body.body.includes(res.code));
  assert.ok(comment.body.body.includes('1.2.62'), 'the tool version');
  assert.ok(comment.body.body.includes('Number of teams is not 0'));
  assert.ok(comment.body.body.startsWith(`<!-- coodsz-crash-report v1 tracked -->\n**New report ${res.code}** (Coop and Friends, client)`));
  for (const part of ['**What the player said:**\n\nNo note.', '**Severity:** Average.', '- Tool: Coop and Friends client 1.2.62', '<details><summary>Technical details</summary>'])
    assert.ok(comment.body.body.includes(part), part);
  assert.ok(comment.body.body.includes(`curl.exe -H "X-Admin-Key: <ADMIN_KEY>" -o ${res.code}.zip https://crash.example/crash/${res.code}`));
  assert.ok(!gh.calls.some(c => c.url.endsWith('/issues') && c.method === 'POST'), 'no new issue');
});

test('the tracking comment only goes to an open issue labeled tracking, else a normal issue is filed', async () => {
  for (const answer of [trackingIssue([]), trackingIssue(['tracking'], 'closed'), () => ({ status: 404, body: {} })]) {
    const gh = github(201, answer);
    const res = await (await handle(post(summary({ triage: siege() })), env(), gh.fetcher)).json();
    assert.equal(res.tracked, undefined);
    const issue = gh.calls.find(c => c.url.endsWith('/issues') && c.method === 'POST');
    assert.ok(issue, 'a new issue is filed');
    assert.deepEqual(issue.body.labels, ['crash-report', 'client', 'coop', 'new', 'known', 'coop-bug', 'siege']);
  }
  // A report that needs no report never goes to a tracking issue, even with a number.
  const gh = github(201, trackingIssue());
  await handle(post(summary({ triage: cleanExit({ trackingIssue: 8 }) })), env(), gh.fetcher);
  assert.ok(!gh.calls.some(c => c.method === 'GET'));
});

test('the same player and the same signature within 10 minutes gets the earlier code and files nothing', async () => {
  const e = env();
  const first = await (await handle(post(summary({ triage: cleanExit() })), e, github().fetcher)).json();
  const gh = github();
  const again = await (await handle(post(summary({ triage: cleanExit() })), e, gh.fetcher)).json();
  assert.equal(again.code, first.code);
  assert.equal(again.duplicate, true);
  assert.equal(gh.calls.length, 0, 'nothing filed');
  assert.equal([...e.VOTES.map.keys()].filter(k => k.startsWith('crash:')).length, 1, 'nothing new stored');
  const key = [...e.VOTES.map.keys()].find(k => k.startsWith('cd:'));
  assert.equal(e.VOTES.options.get(key).expirationTtl, DEDUPE_TTL);
  assert.equal(DEDUPE_TTL, 600);
  assert.ok(!key.includes(INSTALL), 'the key holds the reporter hash, not the install id');

  // Another signature, another player, or an unknown crash are new reports.
  const other = await (await handle(post(summary({ triage: siege() })), e, github().fetcher)).json();
  assert.notEqual(other.code, first.code);
  const player = await (await handle(post(summary({ install: 'f'.repeat(32), triage: cleanExit() })), e, github().fetcher)).json();
  assert.notEqual(player.code, first.code);
  const u1 = await (await handle(post(summary({ triage: { signature: null, signaturesVersion: '1' } })), e, github().fetcher)).json();
  const u2 = await (await handle(post(summary({ triage: { signature: null, signaturesVersion: '1' } })), e, github().fetcher)).json();
  assert.notEqual(u1.code, u2.code);
});

test('triage checks refuse odd values and clean the free text', () => {
  assert.equal(validateSummary(summary({ triage: 'x' })), 'triage');
  assert.equal(validateSummary(summary({ triage: siege({ signature: 'Bad Id!' }) })), 'triage signature');
  assert.equal(validateSummary(summary({ triage: siege({ category: 'whatever' }) })), 'triage category');
  assert.equal(validateSummary(summary({ triage: siege({ trackingIssue: -1 }) })), 'triage trackingIssue');
  assert.equal(validateSummary(summary({ triage: siege({ trackingIssue: 1.5 }) })), 'triage trackingIssue');
  assert.equal(validateSummary(summary({ triage: siege({ labels: ['Has Space'] }) })), 'triage labels');
  assert.equal(validateSummary(summary({ triage: siege({ labels: ['a', 'b', 'c', 'd'] }) })), 'triage labels');
  assert.equal(validateSummary(summary({ triage: siege({ evidence: new Array(MAX_EVIDENCE + 1).fill('x') }) })), 'triage evidence');
  assert.equal(validateSummary(summary({ triage: siege({ evidence: [1] }) })), 'triage evidence');
  assert.equal(validateSummary(summary({ triage: siege({ signaturesVersion: 'a b' }) })), 'triage version');
  assert.equal(validateSummary(summary({ triage: siege({ title: 'x'.repeat(2001) }) })), 'triage title');
  assert.equal(validateSummary(summary({ triage: siege({ reportNeeded: 'yes' }) })), 'triage reportNeeded');

  const t = validateTriage(siege({
    title: 'Hi @everyone `x`',
    evidence: ['Crash at C:\\Users\\Pat\\Documents from 192.168.1.20 steam 76561198000000001 password=hunter2'],
    extra: 'dropped',
  }));
  assert.ok(!t.title.includes('@') && !t.title.includes('`'));
  for (const secret of ['Pat', '192.168.1.20', '76561198000000001', 'hunter2'])
    assert.ok(!t.evidence[0].includes(secret), secret + ' was left in');
  assert.ok(!('extra' in t));
  assert.equal(validateTriage(undefined), null);
  assert.deepEqual(validateTriage({ signature: null, signaturesVersion: 'v1', title: 'ignored', evidence: ['ignored'] }).evidence, []);
});

test('report text cannot add links or images, and only signature labels pass', () => {
  const t = validateTriage(siege({
    title: 'Open [download](https://evil.example/x)',
    whatHappened: 'See ![p](https://evil.example/p.png) and # heading',
    labels: ['tracking', 'siege', 'pending'],
  }));
  for (const text of [t.title, t.whatHappened])
    assert.ok(!/(^|[^\\])\]\(http/.test(text) && !/(^|[^\\])!\[/.test(text), 'link survived in ' + text);
  assert.deepEqual(t.labels, ['siege']);
  assert.ok(!crashLabels(summary({ triage: siege({ labels: ['tracking', 'new', 'spam'] }) })).includes('spam'));
});

test('every label and category crash-signatures.json uses passes, including the TAOM startup check and gpu', () => {
  const file = JSON.parse(readFileSync(new URL('../../../compat-list/crash-signatures.json', import.meta.url), 'utf8'));
  for (const signature of file.signatures) {
    assert.ok(CATEGORIES.includes(signature.category), signature.id + ' category');
    for (const label of signature.labels ?? []) assert.ok(SIGNATURE_LABELS.has(label), signature.id + ' label ' + label);
  }
  const taom = validateTriage(siege({ signature: 'taom-coopcompat-startup-check', category: 'setup', labels: ['taom-startup-check'] }));
  assert.deepEqual(taom.labels, ['taom-startup-check']);
  const gpu = validateTriage(siege({ signature: 'gpu-create-texture-array', category: 'setup', reportNeeded: false, labels: ['gpu'] }));
  assert.deepEqual(gpu.labels, ['gpu']);
});

test('each tool family files into its own crash repository', () => {
  assert.equal(crashRepo({}, 'coop'), 'Coodsz/Coodsz-Crash-Reports');
  assert.equal(crashRepo({}, 'taom'), 'Coodsz/Coodsz-TAOM-Crash-Reports');
  assert.equal(crashRepo({}, 'ad1259'), 'Coodsz/Coodsz-AD1259-Crash-Reports');
  assert.equal(crashRepo({ CRASH_REPO: 'a/b', TAOM_CRASH_REPO: 'c/d', AD1259_CRASH_REPO: 'e/f' }, 'taom'), 'c/d');
  assert.equal(crashRepo({ CRASH_REPO: 'a/b' }, 'coop'), 'a/b');
  assert.equal(crashRepo({}, 'be'), 'Coodsz/Coodsz-BurningEmpires-Crash-Reports');
  assert.equal(crashRepo({}, 'rot'), 'Coodsz/Coodsz-RealmOfThrones-Crash-Reports');
  const all = { CRASH_REPO: 'a/b', TAOM_CRASH_REPO: 'c/d', AD1259_CRASH_REPO: 'e/f', BE_CRASH_REPO: 'g/h', ROT_CRASH_REPO: 'i/j' };
  assert.deepEqual(['coop', 'taom', 'ad1259', 'be', 'rot'].map(tool => crashRepo(all, tool)), ['a/b', 'c/d', 'e/f', 'g/h', 'i/j']);
  assert.equal(crashRepo({ BE_CRASH_REPO: 'g/h' }, 'coop'), 'Coodsz/Coodsz-Crash-Reports', 'the BE var never moves other reports');
  assert.equal(crashRepo({ ROT_CRASH_REPO: 'i/j' }, 'be'), 'Coodsz/Coodsz-BurningEmpires-Crash-Reports', 'the RoT var never moves other reports');
});

// ----- The player's note, read from the zip -----

const NOTE_FILES = [{ name: 'game/Coop_client.log', size: 10 }, { name: 'player-notes.txt', size: 40 }];
const noteIssue = async (zip, over = {}) => {
  const gh = github();
  await handle(post(summary({ files: NOTE_FILES, ...over }), zip), env(), gh.fetcher);
  return gh.calls[0].body.body;
};
const said = body => body.slice(body.indexOf('## What the player said'), body.indexOf('## Severity'));
const ZW = '​';

test('a stored note and a deflated note are quoted under What the player said', async () => {
  for (const method of [0, 8]) {
    const zip = makeZip([{ name: 'game/Coop_client.log', text: 'log', method }, { name: 'player-notes.txt', text: 'I joined the siege.\r\nThen it froze.', method }]);
    assert.equal(await readPlayerNote(zip), 'I joined the siege.\nThen it froze.');
    const section = said(await noteIssue(zip, { reason: 'logs', problem: 'desync', crashLine: '' }));
    assert.equal(section, '## What the player said\n\nThey picked: Desync.\n\n> I joined the siege.\n> Then it froze.\n\n', 'method ' + method);
  }
});

test('an oversized note is cut to about 1500 characters with an ellipsis', async () => {
  const zip = makeZip([{ name: 'player-notes.txt', text: 'abc '.repeat(5000), method: 8 }]);
  const note = await readPlayerNote(zip);
  assert.ok(note.length <= MAX_NOTE_CHARS && note.endsWith('...'));
  const stored = makeZip([{ name: 'player-notes.txt', text: 'x'.repeat(70000), method: 0 }]);
  assert.equal(await readZipEntry(stored, 'player-notes.txt'), null, 'over the packed size cap is skipped');
  assert.ok(said(await noteIssue(zip)).includes('> abc abc'));
});

test('a malicious note cannot add links, mentions, issue refs, HTML, headings, lists or break a fence', async () => {
  const text = [
    'See [here](https://evil.example/x) and ![p](https://evil.example/p.png) https://evil.example www.evil.example',
    'Hi @everyone and @Coodsz, fixes #123 and Owner/Repo#12 and GH-7',
    '<details><summary>x</summary><img src=x></details>',
    '```', '# Heading', '- list', '1. step', '> quote', '---', '===', 'a & b &#64;x',
  ].join('\n');
  const section = said(await noteIssue(makeZip([{ name: 'player-notes.txt', text, method: 8 }])));
  const quoted = section.split('\n').filter(l => l.startsWith('>'));
  assert.equal(quoted.length, 11, 'one quote line per note line');
  for (const line of quoted) assert.ok(/^>( |$)/.test(line));
  const inner = quoted.map(l => l.slice(2)).join('\n');
  assert.ok(!/(^|[^\\])\]\(/.test(inner) && !/(^|[^\\])!\[/.test(inner), 'no link or image');
  assert.ok(!inner.includes('://') && !/www\./.test(inner), 'no bare url');
  assert.ok(!/#\d/.test(inner) && !/GH-\d/.test(inner), 'no issue refs');
  assert.ok(!/[@<>`]/.test(inner), 'no mention, HTML or fence');
  for (const start of ['\\#', '\\-', '1\\.', "'", '\\---', '\\==='])
    assert.ok(quoted.some(l => l.startsWith('> ' + start)), start);
  assert.ok(inner.includes('a &amp; b &amp;') && !inner.includes('&#'), 'entities cannot sneak an @ back in');
});

test('no note entry says No note, and a corrupt zip only means no note', async () => {
  const noNote = makeZip([{ name: 'game/Coop_client.log', text: 'log', method: 8 }]);
  assert.equal(await readPlayerNote(noNote), '');
  assert.ok(said(await noteIssue(noNote, { files: [{ name: 'game/Coop_client.log', size: 3 }] })).includes('No note.'));

  const good = makeZip([{ name: 'player-notes.txt', text: 'hello there', method: 8 }]);
  const broken = [
    good.slice(0, good.length - 10),                                   // no end record
    (() => { const z = good.slice(); z[z.length - 6] = 0xff; return z; })(),  // directory offset out of range
    (() => { const z = good.slice(); z.fill(0xee, 46, 52); return z; })(),    // garbage deflate data
    (() => { const z = good.slice(); z[0] = 0; return z; })(),                // bad local header
    EMPTY_ZIP,
  ];
  for (const z of broken) assert.equal(await readPlayerNote(z), '');
  const corrupt = broken[2];
  const res = await handle(post(summary({ files: NOTE_FILES }), corrupt), env(), github().fetcher);
  assert.equal(res.status, 200, 'a broken note never fails the report');
});

test('the tracked comment quotes the note too', async () => {
  const gh = github(201, trackingIssue());
  const zip = makeZip([{ name: 'player-notes.txt', text: 'Stuck at the siege again', method: 8 }]);
  await handle(post(summary({ files: NOTE_FILES, triage: siege() }), zip), env(), gh.fetcher);
  assert.ok(gh.calls[1].body.body.includes('**What the player said:**\n\n> Stuck at the siege again\n'));
});

test('prose fields break bare urls, issue refs and leading markers; the gist sits in a code span', async () => {
  assert.equal(defang('https://x.example www.y.example #12 o/r#3 GH-4'), `https:${ZW}//x.example www${ZW}.y.example #${ZW}12 o/r#${ZW}3 GH-${ZW}4`);
  assert.equal(escapeLineStart('- a'), '\\- a');
  assert.equal(escapeLineStart('+ a'), '\\+ a');
  assert.equal(escapeLineStart('> a'), '\\> a');
  assert.equal(escapeLineStart('12. a'), '12\\. a');
  assert.equal(escapeLineStart('---'), '\\---');
  assert.equal(escapeLineStart('\\*\\*\\*'), '\\\\*\\*\\*');
  assert.equal(escapeLineStart('Fine line - ok'), 'Fine line - ok');
  assert.equal(prose('a\n- b'), 'a\n\\- b');

  const gh = github();
  await handle(post(summary({ crashLine: 'Error see https://evil.example #5' })), env(), gh.fetcher);
  assert.ok(gh.calls[0].body.body.includes('The last error in the logs was `Error see https://evil.example #5`'));
  const t = github();
  await handle(post(summary({ triage: siege({ trackingIssue: null, whatHappened: '- See https://evil.example and #9', whatToDo: 'Read www.evil.example #10' }) })), env(), t.fetcher);
  const body = t.calls[0].body.body;
  const prosePart = body.replace(/```[\s\S]*?```/g, '').replace(/`[^`]*`/g, '');
  assert.ok(!prosePart.includes('://') && !/www\./.test(prosePart) && !/#\d/.test(prosePart), 'no bare url or ref in prose');
  assert.ok(body.includes('## What happened\n\n\\- See'));
});

test('the gist strips only real log level tokens', () => {
  assert.equal(crashGist('[ERR] boom'), 'boom');
  assert.equal(crashGist('ERR | boom'), 'boom');
  assert.equal(crashGist('INF boom'), 'boom');
  assert.equal(crashGist('Error loading module X'), 'Error loading module X');
  assert.equal(crashGist('Info panel failed'), 'Info panel failed');
  assert.equal(crashGist('ERRORS happened'), 'ERRORS happened');
});

test('format characters are stripped and a cut never splits a surrogate pair', () => {
  assert.equal(cleanCrashLine('ab‮cd⁦ef​g'), 'abcdefg');
  const cut = noteText(new TextEncoder().encode('x'.repeat(1496) + '\u{1F600}'.repeat(10)));
  assert.equal(cut.isWellFormed(), true);
  assert.ok(cut.endsWith('...'));
});

// ----- The player's Discord name (newer tools) -----

const setup = body => body.slice(body.indexOf('## Player and setup'), body.indexOf('<details>'));

test('a Discord name the tool sends shows under Player and setup and in the tracked comment', async () => {
  assert.equal(validateSummary(summary({ contact: '  pat.the_great  ' })).contact, 'pat.the_great');
  const gh = github();
  await handle(post(summary({ contact: 'pat.the_great' })), env(), gh.fetcher);
  assert.ok(setup(gh.calls[0].body.body).includes('- Contact: pat.the\\_great (Discord)\n- Reporter: '));
  const tracked = github(201, trackingIssue());
  await handle(post(summary({ contact: 'Pat', triage: siege() })), env(), tracked.fetcher);
  assert.ok(tracked.calls[1].body.body.includes('- Contact: Pat (Discord)'));
});

test('no Discord name (older tools, empty or blank) leaves the issue as before', async () => {
  for (const contact of [undefined, null, '', '   ', '​‮']) {
    const checked = validateSummary(summary({ contact }));
    assert.equal(typeof checked, 'object');
    assert.equal(checked.contact, null);
  }
  const gh = github();
  await handle(post(summary()), env(), gh.fetcher);
  assert.ok(!gh.calls[0].body.body.includes('Contact:'));
});

test('a Discord name that is too long or not text is refused', async () => {
  assert.equal(validateSummary(summary({ contact: 'x'.repeat(MAX_CONTACT + 1) })), 'contact');
  assert.equal(typeof validateSummary(summary({ contact: '  ' + 'x'.repeat(MAX_CONTACT) + '  ' })), 'object', '40 after trim is fine');
  for (const contact of [42, true, ['Pat'], { name: 'Pat' }])
    assert.equal(validateSummary(summary({ contact })), 'contact');
  assert.equal((await handle(post(summary({ contact: 'y'.repeat(500) })), env(), github().fetcher)).status, 400);
});

test('a malicious Discord name never becomes a link, mention, ref, markdown or HTML', async () => {
  const cases = ['[x](https://e.vil)', '@everyone', '<img src=x>', '#12 **b**', 'www.evil.example', 'a\nb\r# head',
    'ab‮cd⁦ef', '`code` ![i](j)', 'a\\[x](y)', 'a\u0007b\u0085c'];
  for (const contact of cases) {
    const checked = validateSummary(summary({ contact }));
    assert.equal(typeof checked, 'object', contact);
    if (checked.contact === null) continue;
    const line = '- Contact: ' + contactText(checked.contact) + ' (Discord)';
    assert.ok(!/[<>@`\\\n\r]/.test(line.replace(/\\[_]/g, '')), line);
    assert.ok(!/\]\(|!\[|\*\*|:\/\/|www\.|#\d/.test(line), line);
    assert.ok(!/[\p{Cc}\p{Cf}]/u.test(line.replace(/​/g, '')), line);
  }
  assert.equal(validateSummary(summary({ contact: 'ab‮cd⁦ef' })).contact, 'abcdef');
  assert.equal(validateSummary(summary({ contact: 'a\nb' })).contact, 'a b');
  assert.equal(validateSummary(summary({ contact: 'a\\[x](y)' })).contact, 'axy');
  const gh = github();
  await handle(post(summary({ contact: '@everyone [x](https://e.vil)' })), env(), gh.fetcher);
  const part = setup(gh.calls[0].body.body);
  assert.ok(part.includes('- Contact: everyone xhttpse.vil (Discord)'), part);
});

test('Send my logs is never folded into an earlier report with the same signature', async () => {
  // Since v1.3.5 Send my logs carries setup hints, and a player who sends logs after a hint may add a note.
  const e = env();
  const crash = await (await handle(post(summary({ triage: cleanExit() })), e, github().fetcher)).json();
  const gh = github();
  const logs = await (await handle(post(summary({ reason: 'logs', problem: 'join', crashLine: null, triage: cleanExit() })), e, gh.fetcher)).json();
  assert.notEqual(logs.code, crash.code);
  assert.ok(!logs.duplicate);
  assert.ok(gh.calls.length > 0, 'the logs report is filed');
  const again = await (await handle(post(summary({ reason: 'logs', problem: 'join', crashLine: null, triage: cleanExit() })), e, github().fetcher)).json();
  assert.notEqual(again.code, logs.code, 'a second Send my logs is its own report too');
});
