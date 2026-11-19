// node --test test/   (Node 18+, no packages)
import test from 'node:test';
import assert from 'node:assert/strict';
import { handle, validate, issueBody, reporterOf, modKey, voteKey, isKnownToolVersion, flagsFor, nextStats, DEFAULT_REPORT_WRITES_DAY } from '../src/index.js';
import { parseForm, normalizeResult, collectReports } from '../../../.github/scripts/tally-mod-compat.mjs';
import { LISTS } from '../../../.github/scripts/tally-taom-compat.mjs';

const INSTALL = '0123456789abcdef0123456789abcdef';

function kv() {
  const map = new Map();
  return { map, get: async k => map.get(k) ?? null, put: async (k, v) => { map.set(k, v); } };
}

function github() {
  const calls = [];
  let next = 100;
  const fetcher = async (url, init) => {
    calls.push({ url, method: init.method, body: init.body ? JSON.parse(init.body) : undefined, auth: init.headers.Authorization });
    const number = init.method === 'POST' && url.endsWith('/issues') ? next++ : 0;
    return new Response(JSON.stringify({ number }), { status: 201 });
  };
  return { calls, fetcher };
}

// Bannerlord.Diplomacy was reported before, so the tests below are not held as a first report.
const KNOWN = { [await modKey('Bannerlord.Diplomacy')]: JSON.stringify({ works: 1 }) };

// The test install was seen before today, so the new install rule only applies where a test asks.
const seenBefore = async install => [`i:${await reporterOf(install, 'salt')}`, '2026-01-01'];
const OLD_INSTALL = Object.fromEntries([await seenBefore(INSTALL)]);

const env = (extra = {}, known = KNOWN) => {
  const votes = kv();
  for (const [k, v] of Object.entries({ ...OLD_INSTALL, ...known })) votes.map.set(k, v);
  return { GITHUB_TOKEN: 'test-token', VOTER_SALT: 'salt', VOTES: votes, ...extra };
};

const report = (over = {}) => ({
  kind: 'CoodszModReport', schema: 1, install: INSTALL, tool: 'coop', toolVersion: '1.2.61', coop: 'v0.1.6', game: 'v1.4.8',
  reports: [{ id: 'Bannerlord.Diplomacy', name: 'Diplomacy', version: 'v1.2.3', result: 'works' }],
  mods: [{ id: 'Bannerlord.Harmony', version: '2.3' }, { id: 'Bannerlord.Diplomacy', version: 'v1.2.3' }, { id: 'ImprovedGarrisons', version: '1.0' }],
  ...over,
});

const post = body => new Request('https://reports.example/report', {
  method: 'POST', body: JSON.stringify(body), headers: { 'content-type': 'application/json', 'CF-Connecting-IP': '203.0.113.9' },
});

test('a report is filed as a pending issue with the mod list and only a hashed reporter', async () => {
  const e = env();
  const gh = github();
  const res = await handle(post(report()), e, gh.fetcher);
  assert.equal(res.status, 200);
  assert.deepEqual(await res.json(), { ok: true, filed: 1, updated: 0, unchanged: 0 });
  const issue = gh.calls[0];
  assert.equal(issue.method, 'POST');
  assert.deepEqual(issue.body.labels, ['mod-compat', 'mod-report', 'pending']);
  assert.equal(issue.auth, 'Bearer test-token');
  assert.ok(!issue.body.body.includes(INSTALL), 'the raw install id never reaches the issue');
  assert.ok(!issue.body.body.includes('203.0.113.9'), 'the address never reaches the issue');
  assert.ok(![...e.VOTES.map.keys(), ...e.VOTES.map.values()].some(v => v.includes(INSTALL) || v.includes('203.0.113.9')));
  const fields = parseForm(issue.body.body);
  assert.equal(fields.modId, 'Bannerlord.Diplomacy');
  assert.equal(normalizeResult(fields.result), 'works');
  assert.deepEqual(fields.mods, ['Bannerlord.Harmony', 'Bannerlord.Diplomacy', 'ImprovedGarrisons']);
  assert.match(fields.reporter, /^anon-[0-9a-f]{24}$/);
});

test('one vote per install per mod version: the same vote again changes nothing, a new vote edits the issue', async () => {
  const e = env();
  const gh = github();
  await handle(post(report()), e, gh.fetcher);
  const again = await (await handle(post(report()), e, gh.fetcher)).json();
  assert.deepEqual([again.filed, again.unchanged], [0, 1]);
  assert.equal(gh.calls.length, 1);

  const changed = await (await handle(post(report({ reports: [{ id: 'Bannerlord.Diplomacy', version: 'v1.2.3', result: 'broken' }] })), e, gh.fetcher)).json();
  assert.deepEqual([changed.filed, changed.updated], [0, 1]);
  // pending goes on before the body changes, so the new vote is never counted unreviewed.
  assert.deepEqual(gh.calls[1].body, { labels: ['pending'] }, 'a changed vote goes back to review');
  assert.ok(gh.calls[1].url.endsWith('/issues/100/labels'));
  assert.equal(gh.calls[2].method, 'PATCH');
  assert.ok(gh.calls[2].url.endsWith('/issues/100'));

  const newVersion = await (await handle(post(report({ reports: [{ id: 'Bannerlord.Diplomacy', version: 'v1.3.0', result: 'works' }] })), e, gh.fetcher)).json();
  assert.equal(newVersion.filed, 1, 'a new mod version is a new vote');

  const other = await (await handle(post(report({ install: 'f'.repeat(32) })), e, gh.fetcher)).json();
  assert.equal(other.filed, 1, 'another install is another vote');
});

test('private or odd values are refused', () => {
  assert.equal(validate(report({ install: 'not-hex' })), 'install');
  assert.equal(validate(report({ reports: [{ id: 'C:\\Users\\Pat\\Mods', result: 'works' }] })), 'report');
  assert.equal(validate(report({ reports: [{ id: 'Mod', result: 'works', name: '76561198000000000' }] })), 'report name');
  assert.equal(validate(report({ mods: [{ id: '../../etc', version: '1' }] })), 'mod list entry');
  assert.equal(validate(report({ reports: [{ id: 'Mod', result: 'works-with' }] })), 'works-with needs a mod');
  assert.equal(validate(report({ reports: [] })), 'reports');
  assert.equal(validate(report({ tool: 'other' })), 'tool');
  const ok = validate(report({ steamName: 'Pat', reports: [{ id: 'Mod', result: 'works-with', with: 'Other', extra: 'x' }] }));
  assert.equal(typeof ok, 'object');
  assert.ok(!JSON.stringify(ok).includes('Pat'), 'unknown fields are dropped');
});

test('limits: the address limiter, the per install day cap and the global write budget', async () => {
  const limited = await handle(post(report()), env({ REPORT_LIMITER: { limit: async () => ({ success: false }) } }), github().fetcher);
  assert.equal(limited.status, 429);

  const e = env({ MAX_PER_INSTALL_DAY: '1' });
  const gh = github();
  assert.equal((await handle(post(report()), e, gh.fetcher)).status, 200);
  assert.equal((await handle(post(report()), e, gh.fetcher)).status, 429);

  // One new vote from an install seen before books 4 writes; a new install's first vote books 5.
  const g = env({ MAX_REPORT_WRITES_DAY: '8' });
  const capped = github();
  assert.equal((await handle(post(report()), g, capped.fetcher)).status, 200);
  assert.equal((await handle(post(report({ install: 'e'.repeat(32) })), g, capped.fetcher)).status, 429);
  assert.equal(capped.calls.length, 1, 'no issue past the write budget of the day');

  const big = new Request('https://reports.example/report', { method: 'POST', body: 'x'.repeat(40000) });
  assert.equal((await handle(big, env(), gh.fetcher)).status, 413);
  assert.equal((await handle(new Request('https://reports.example/report', { method: 'POST', body: '{' }), env(), gh.fetcher)).status, 400);
  assert.equal((await handle(post(report()), { VOTES: kv() }, gh.fetcher)).status, 503, 'no secrets, no reports');
});

test('TAOM reports get the taom label, and the tally skips them and pending ones', async () => {
  const gh = github();
  await handle(post(report({ tool: 'taom' })), env(), gh.fetcher);
  assert.ok(gh.calls[0].body.labels.includes('taom'));
  const issue = n => ({ number: n, user: { login: 'Coodsz', type: 'User' }, created_at: '2026-10-22', body: gh.calls[0].body.body, labels: gh.calls[0].body.labels.map(name => ({ name })) });
  const { reports, skipped } = collectReports([issue(1)]);
  assert.equal(reports.length, 0);
  assert.equal(skipped[0][1], 'pending');
});

test('TAOM reports are filed in Coodsz/Coodsz-TAOM, AD1259 reports in Coodsz/Coodsz-AD1259, Coop and Friends reports stay in GITHUB_REPO', async () => {
  const taom = github();
  await handle(post(report({ tool: 'taom', toolVersion: '1.1.42' })), env(), taom.fetcher);
  assert.equal(taom.calls[0].url, 'https://api.github.com/repos/Coodsz/Coodsz-TAOM/issues');
  assert.deepEqual(taom.calls[0].body.labels, ['mod-compat', 'mod-report', 'pending', 'taom']);

  const custom = github();
  await handle(post(report({ tool: 'taom', toolVersion: '1.1.42' })), env({ TAOM_GITHUB_REPO: 'Me/Taom-Test', GITHUB_REPO: 'Me/Coop-Test' }), custom.fetcher);
  assert.equal(custom.calls[0].url, 'https://api.github.com/repos/Me/Taom-Test/issues');

  const ad = github();
  await handle(post(report({ tool: 'ad1259', toolVersion: '1.0.21' })), env(), ad.fetcher);
  assert.equal(ad.calls[0].url, 'https://api.github.com/repos/Coodsz/Coodsz-AD1259/issues');
  assert.deepEqual(ad.calls[0].body.labels, ['mod-compat', 'mod-report', 'pending', 'ad1259']);
  const adCustom = github();
  await handle(post(report({ tool: 'ad1259' })), env({ AD1259_GITHUB_REPO: 'Me/AD-Test', GITHUB_REPO: 'Me/Coop-Test' }), adCustom.fetcher);
  assert.equal(adCustom.calls[0].url, 'https://api.github.com/repos/Me/AD-Test/issues');

  const gh = github();
  await handle(post(report()), env(), gh.fetcher);
  assert.equal(gh.calls[0].url, 'https://api.github.com/repos/Coodsz/Coodsz-Coop-and-Friends/issues');
  const set = github();
  await handle(post(report()), env({ GITHUB_REPO: 'Me/Coop-Test', TAOM_GITHUB_REPO: 'Me/Taom-Test', AD1259_GITHUB_REPO: 'Me/AD-Test' }), set.fetcher);
  assert.equal(set.calls[0].url, 'https://api.github.com/repos/Me/Coop-Test/issues');
});

/** The saved vote of the only report in this env, without its repo, as the worker kept it before. */
function forgetRepo(e, issue) {
  const [key] = [...e.VOTES.map.keys()].filter(k => k.startsWith('v:'));
  const { repo, ...vote } = JSON.parse(e.VOTES.map.get(key));
  e.VOTES.map.set(key, JSON.stringify({ ...vote, ...(issue ? { issue } : {}) }));
  return { key, repo };
}

test('a TAOM vote saved before the move (its issue in Coop and Friends) is filed again in the TAOM repository', async () => {
  const taomReport = (result = 'works') => report({ tool: 'taom', toolVersion: '1.1.42', reports: [{ id: 'Bannerlord.Diplomacy', version: 'v1.2.3', result }] });
  const e = env();
  await handle(post(taomReport()), e, github().fetcher);
  const { key, repo } = forgetRepo(e, 55);
  assert.equal(repo, 'Coodsz/Coodsz-TAOM', 'new votes keep their repository');

  // The same vote again is filed in the TAOM repository instead of counting as unchanged, and
  // issue 55 (in Coop and Friends) is never touched.
  const again = github();
  const same = await (await handle(post(taomReport()), e, again.fetcher)).json();
  assert.deepEqual([same.filed, same.updated, same.unchanged], [1, 0, 0]);
  assert.deepEqual(again.calls.map(c => [c.method, c.url]), [['POST', 'https://api.github.com/repos/Coodsz/Coodsz-TAOM/issues']]);
  assert.deepEqual(JSON.parse(e.VOTES.map.get(key)).issue, 100);

  // From then on a change edits that TAOM issue.
  const change = github();
  assert.equal((await (await handle(post(taomReport('broken')), e, change.fetcher)).json()).updated, 1);
  assert.equal(change.calls[1].url, 'https://api.github.com/repos/Coodsz/Coodsz-TAOM/issues/100');

  // A Coop and Friends vote saved without repo still edits its issue there.
  const c = env();
  await handle(post(report()), c, github().fetcher);
  forgetRepo(c);
  const coop = github();
  const changed = await (await handle(post(report({ reports: [{ id: 'Bannerlord.Diplomacy', version: 'v1.2.3', result: 'broken' }] })), c, coop.fetcher)).json();
  assert.equal(changed.updated, 1);
  assert.equal(coop.calls[1].url, 'https://api.github.com/repos/Coodsz/Coodsz-Coop-and-Friends/issues/100');
});

test('an AD1259 vote saved before the move (its issue in Coop and Friends, with or without repo) is filed again in the AD1259 repository', async () => {
  const adReport = (result = 'works') => report({ tool: 'ad1259', toolVersion: '1.0.21', reports: [{ id: 'RBM', version: 'v4.5.4', result }] });
  for (const oldRepo of [undefined, 'Coodsz/Coodsz-Coop-and-Friends']) {
    const e = env();
    await handle(post(adReport()), e, github().fetcher);
    const { key, repo } = forgetRepo(e, 66);
    assert.equal(repo, 'Coodsz/Coodsz-AD1259', 'new votes keep their repository');
    // An AD1259 vote saved between the TAOM move and this one carries GITHUB_REPO as its repo.
    if (oldRepo) e.VOTES.map.set(key, JSON.stringify({ ...JSON.parse(e.VOTES.map.get(key)), repo: oldRepo }));

    const again = github();
    const same = await (await handle(post(adReport()), e, again.fetcher)).json();
    assert.deepEqual([same.filed, same.updated, same.unchanged], [1, 0, 0], String(oldRepo));
    assert.deepEqual(again.calls.map(c => [c.method, c.url]), [['POST', 'https://api.github.com/repos/Coodsz/Coodsz-AD1259/issues']]);
    const saved = JSON.parse(e.VOTES.map.get(key));
    assert.deepEqual([saved.issue, saved.repo], [100, 'Coodsz/Coodsz-AD1259']);

    // From then on the same vote is unchanged and a change edits that AD1259 issue.
    const quiet = github();
    assert.equal((await (await handle(post(adReport()), e, quiet.fetcher)).json()).unchanged, 1);
    assert.equal(quiet.calls.length, 0);
    const change = github();
    assert.equal((await (await handle(post(adReport('broken')), e, change.fetcher)).json()).updated, 1);
    assert.equal(change.calls[1].url, 'https://api.github.com/repos/Coodsz/Coodsz-AD1259/issues/100');
  }
});

test('one PC rating the same mod in Coop and Friends and in the TAOM and AD1259 clients keeps one issue per repository', async () => {
  const send = tool => report({ tool, ...(tool === 'coop' ? {} : { toolVersion: tool === 'taom' ? '1.1.42' : '1.0.21' }), reports: [{ id: 'Bannerlord.Diplomacy', version: 'v1.2.3', result: 'works' }] });
  const e = env();
  const first = github();
  for (const tool of ['coop', 'taom', 'ad1259']) await handle(post(send(tool)), e, first.fetcher);
  assert.deepEqual(first.calls.map(c => c.url), ['Coodsz/Coodsz-Coop-and-Friends', 'Coodsz/Coodsz-TAOM', 'Coodsz/Coodsz-AD1259'].map(r => `https://api.github.com/repos/${r}/issues`));
  // Sending the same votes again, alternating between the tools, files nothing new.
  const again = github();
  for (const tool of ['taom', 'coop', 'ad1259', 'coop', 'taom']) {
    const res = await (await handle(post(send(tool)), e, again.fetcher)).json();
    assert.deepEqual([res.filed, res.updated, res.unchanged], [0, 0, 1], tool);
  }
  assert.equal(again.calls.length, 0);
  assert.equal([...e.VOTES.map.keys()].filter(k => k.startsWith('v:')).length, 3);
});

test('a TAOM vote saved under the old key without the tool still edits its TAOM issue', async () => {
  const taom = (result = 'works') => report({ tool: 'taom', toolVersion: '1.1.42', reports: [{ id: 'Bannerlord.Diplomacy', version: 'v1.2.3', result }] });
  const e = env();
  await handle(post(taom()), e, github().fetcher);
  const reporter = await reporterOf(INSTALL, e.VOTER_SALT);
  const newKey = await voteKey(reporter, 'Bannerlord.Diplomacy', 'v1.2.3', 'taom');
  const oldKey = await voteKey(reporter, 'Bannerlord.Diplomacy', 'v1.2.3');
  assert.notEqual(newKey, oldKey);
  assert.equal(oldKey, await voteKey(reporter, 'Bannerlord.Diplomacy', 'v1.2.3', 'coop'), 'coop keys stay as they were');
  // Move the saved vote to where the worker kept it before.
  e.VOTES.map.set(oldKey, e.VOTES.map.get(newKey));
  e.VOTES.map.delete(newKey);

  const same = github();
  assert.equal((await (await handle(post(taom()), e, same.fetcher)).json()).unchanged, 1);
  assert.equal(same.calls.length, 0);
  const change = github();
  assert.equal((await (await handle(post(taom('broken')), e, change.fetcher)).json()).updated, 1);
  assert.deepEqual(change.calls.map(c => [c.method, c.url]), [['POST', 'https://api.github.com/repos/Coodsz/Coodsz-TAOM/issues/100/labels'], ['PATCH', 'https://api.github.com/repos/Coodsz/Coodsz-TAOM/issues/100']]);
  assert.equal(JSON.parse(e.VOTES.map.get(newKey)).issue, 100, 'saved under the new key from then on');
});

test('the issue text works with the tally parser for works only with', async () => {
  const reporter = await reporterOf(INSTALL, 'salt');
  const body = issueBody(validate(report()), { id: 'A', name: null, version: '', result: 'works-with', with: 'B' }, reporter);
  const fields = parseForm(body);
  assert.equal(normalizeResult(fields.result), 'workswith');
  assert.equal(fields.with, 'B');
});

test('AD1259 reports are accepted with the ad1259 label, coop reports get no tool label, and an unknown tool is refused', async () => {
  assert.equal(validate(report({ tool: 'ad1259' })).tool, 'ad1259');
  assert.equal(validate(report({ tool: 'other' })), 'tool');

  const gh = github();
  assert.equal((await handle(post(report({ tool: 'ad1259' })), env(), gh.fetcher)).status, 200);
  const labels = gh.calls[0].body.labels;
  assert.ok(labels.includes('ad1259'));
  assert.ok(labels.includes('pending'));
  assert.ok(!labels.includes('taom'));

  const coop = github();
  await handle(post(report()), env(), coop.fetcher);
  assert.deepEqual(coop.calls[0].body.labels, ['mod-compat', 'mod-report', 'pending']);

  // Approved (pending removed), the tally still leaves an AD1259 report out.
  const issue = { number: 7, user: { login: 'Coodsz', type: 'User' }, created_at: '2026-10-22', body: gh.calls[0].body.body,
    labels: labels.filter(name => name !== 'pending').map(name => ({ name })) };
  const { reports, skipped } = collectReports([issue]);
  assert.equal(reports.length, 0);
  assert.equal(skipped[0][1], 'AD1259 report');
  // In Coodsz-AD1259, where the worker files it, the AD1259 tally counts it.
  const ad = collectReports([issue], { leaveOut: LISTS.ad1259.leaveOut });
  assert.deepEqual(ad.reports.map(r => [r.modId, r.result]), [['Bannerlord.Diplomacy', 'works']]);
});

// ---- Burning Empires reports (tool be) ----------------------------------

const beReport = (over = {}) => report({ tool: 'be', toolVersion: '1.0.0', ...over });

test('Burning Empires reports are filed in Coodsz/Coodsz-BurningEmpires with the be label, and BE_GITHUB_REPO moves only them', async () => {
  assert.equal(validate(beReport()).tool, 'be');

  const be = github();
  assert.equal((await handle(post(beReport()), env(), be.fetcher)).status, 200);
  assert.equal(be.calls[0].url, 'https://api.github.com/repos/Coodsz/Coodsz-BurningEmpires/issues');
  assert.deepEqual(be.calls[0].body.labels, ['mod-compat', 'mod-report', 'pending', 'be']);
  assert.match(be.calls[0].body.body, /### Tool\n\nbe 1\.0\.0\n/);

  const custom = github();
  await handle(post(beReport()), env({ BE_GITHUB_REPO: 'Me/BE-Test', GITHUB_REPO: 'Me/Coop-Test', TAOM_GITHUB_REPO: 'Me/Taom-Test', AD1259_GITHUB_REPO: 'Me/AD-Test' }), custom.fetcher);
  assert.equal(custom.calls[0].url, 'https://api.github.com/repos/Me/BE-Test/issues');

  // The BE var never moves the other tools' reports.
  for (const [tool, toolVersion, repo] of [['coop', '1.2.61', 'Coodsz/Coodsz-Coop-and-Friends'], ['taom', '1.1.42', 'Coodsz/Coodsz-TAOM'], ['ad1259', '1.0.21', 'Coodsz/Coodsz-AD1259']]) {
    const gh = github();
    await handle(post(report({ tool, toolVersion })), env({ BE_GITHUB_REPO: 'Me/BE-Test' }), gh.fetcher);
    assert.equal(gh.calls[0].url, `https://api.github.com/repos/${repo}/issues`, tool);
  }
});

test('a BE tool version from 1.0.0 to below 1.2.0 is known, anything else is held', async () => {
  assert.ok(isKnownToolVersion('be', '1.0.0'));
  assert.ok(isKnownToolVersion('be', '1.1.9'));
  assert.ok(!isKnownToolVersion('be', '0.9.9'));
  assert.ok(!isKnownToolVersion('be', '1.2.0'), 'too far ahead');
  assert.ok(!isKnownToolVersion('be', '1.2.61'), 'a coop version is not a BE version');

  const gh = github();
  await handle(post(beReport({ toolVersion: '1.2.61' })), env(), gh.fetcher);
  assert.ok(gh.calls[0].body.labels.includes('suspicious'));
  assert.match(gh.calls[0].body.body, /tool version 1\.2\.61 is not a known be version/);
  const ok = github();
  await handle(post(beReport()), env(), ok.fetcher);
  assert.ok(!ok.calls[0].body.labels.includes('suspicious'));
});

test('Realm of Thrones reports are filed in Coodsz/Coodsz-RealmOfThrones with the rot label, and ROT_GITHUB_REPO moves only them', async () => {
  const rotReport = (over = {}) => report({ tool: 'rot', toolVersion: '0.1.0', ...over });
  assert.equal(validate(rotReport()).tool, 'rot');

  const rot = github();
  assert.equal((await handle(post(rotReport()), env(), rot.fetcher)).status, 200);
  assert.equal(rot.calls[0].url, 'https://api.github.com/repos/Coodsz/Coodsz-RealmOfThrones/issues');
  assert.deepEqual(rot.calls[0].body.labels, ['mod-compat', 'mod-report', 'pending', 'rot']);

  const custom = github();
  await handle(post(rotReport()), env({ ROT_GITHUB_REPO: 'Me/RoT-Test' }), custom.fetcher);
  assert.equal(custom.calls[0].url, 'https://api.github.com/repos/Me/RoT-Test/issues');
  const be = github();
  await handle(post(beReport()), env({ ROT_GITHUB_REPO: 'Me/RoT-Test' }), be.fetcher);
  assert.equal(be.calls[0].url, 'https://api.github.com/repos/Coodsz/Coodsz-BurningEmpires/issues', 'the RoT var never moves other reports');

  assert.ok(isKnownToolVersion('rot', '0.1.0'));
  assert.ok(!isKnownToolVersion('rot', '0.0.9'));
  assert.ok(!isKnownToolVersion('rot', '1.2.0'), 'too far ahead');
});

test('one PC rating the same mod in all four tools keeps one issue per repository, and the BE tally counts the BE one', async () => {
  const versions = { coop: '1.2.61', taom: '1.1.42', ad1259: '1.0.21', be: '1.0.0' };
  const send = tool => report({ tool, toolVersion: versions[tool], reports: [{ id: 'Bannerlord.Diplomacy', version: 'v1.2.3', result: 'works' }] });
  const e = env();
  const first = github();
  for (const tool of ['coop', 'taom', 'ad1259', 'be']) await handle(post(send(tool)), e, first.fetcher);
  assert.deepEqual(first.calls.map(c => c.url), ['Coodsz/Coodsz-Coop-and-Friends', 'Coodsz/Coodsz-TAOM', 'Coodsz/Coodsz-AD1259', 'Coodsz/Coodsz-BurningEmpires'].map(r => `https://api.github.com/repos/${r}/issues`));
  const again = github();
  for (const tool of ['be', 'coop', 'be', 'taom', 'ad1259']) {
    const res = await (await handle(post(send(tool)), e, again.fetcher)).json();
    assert.deepEqual([res.filed, res.updated, res.unchanged], [0, 0, 1], tool);
  }
  assert.equal(again.calls.length, 0);
  assert.equal([...e.VOTES.map.keys()].filter(k => k.startsWith('v:')).length, 4);

  // A changed BE vote edits its BE issue and nothing else.
  const change = github();
  const changed = await (await handle(post(report({ tool: 'be', toolVersion: '1.0.0', reports: [{ id: 'Bannerlord.Diplomacy', version: 'v1.2.3', result: 'broken' }] })), e, change.fetcher)).json();
  assert.equal(changed.updated, 1);
  assert.deepEqual(change.calls.map(c => [c.method, c.url]), [['POST', 'https://api.github.com/repos/Coodsz/Coodsz-BurningEmpires/issues/103/labels'], ['PATCH', 'https://api.github.com/repos/Coodsz/Coodsz-BurningEmpires/issues/103']]);

  // Approved, the BE tally in Coodsz-BurningEmpires counts it.
  const body = first.calls[3].body;
  const issue = { number: 9, user: { login: 'Coodsz', type: 'User' }, created_at: '2026-10-26', body: body.body,
    labels: body.labels.filter(name => name !== 'pending').map(name => ({ name })) };
  const be = collectReports([issue], { leaveOut: LISTS.be.leaveOut });
  assert.deepEqual(be.reports.map(r => [r.modId, r.result]), [['Bannerlord.Diplomacy', 'works']]);
});

// ---- Suspicious reports -------------------------------------------------

const labelsOf = call => call.body.labels;
const held = call => labelsOf(call).includes('suspicious');

test('a normal report is not held and the answer never says whether a report was held', async () => {
  const gh = github();
  const res = await (await handle(post(report()), env(), gh.fetcher)).json();
  assert.deepEqual(res, { ok: true, filed: 1, updated: 0, unchanged: 0 });
  assert.ok(!held(gh.calls[0]));
  assert.ok(!gh.calls[0].body.body.includes('Held for review'));
});

test('a first Works report for a mod id is not held', async () => {
  const gh = github();
  await handle(post(report()), env({}, {}), gh.fetcher);
  assert.ok(!held(gh.calls[0]), 'a first Works report is approved after a day like any other');
  assert.ok(labelsOf(gh.calls[0]).includes('pending'));
});

test('the first Broken report anyone sends for a mod id is held, the next one is not', async () => {
  const e = env({}, {});
  const gh = github();
  await handle(post(report({ reports: [{ id: 'Bannerlord.Diplomacy', name: 'Diplomacy', version: 'v1.2.3', result: 'broken' }] })), e, gh.fetcher);
  assert.ok(held(gh.calls[0]));
  assert.ok(labelsOf(gh.calls[0]).includes('pending'), 'a held report is still pending');
  assert.match(gh.calls[0].body.body, /\*\*Held for review \(`suspicious`\):\*\* the first report anyone sent for this mod id\./);
  assert.equal(parseForm(gh.calls[0].body.body).modId, 'Bannerlord.Diplomacy', 'the reason line does not break the tally parser');

  await handle(post(report({ install: 'a'.repeat(32) })), e, gh.fetcher);
  assert.ok(!held(gh.calls[1]), 'the mod is known now');
});

test('a vote filed before the stats existed counts as a known mod when it changes', async () => {
  const e = env({}, {});
  const gh = github();
  await handle(post(report()), e, gh.fetcher);
  for (const k of [...e.VOTES.map.keys()]) if (k.startsWith('m:')) e.VOTES.map.delete(k);
  await handle(post(report({ reports: [{ id: 'Bannerlord.Diplomacy', version: 'v1.2.3', result: 'broken' }] })), e, gh.fetcher);
  assert.deepEqual(gh.calls[1].body, { labels: ['pending'] });
});

test('a tool version that no release has is held', async () => {
  assert.ok(isKnownToolVersion('coop', '1.2.61'));
  assert.ok(isKnownToolVersion('coop', '1.3.4'));
  assert.ok(isKnownToolVersion('taom', '1.1.42'));
  assert.ok(isKnownToolVersion('ad1259', '1.0.21'));
  assert.ok(!isKnownToolVersion('coop', '1.2.60'), 'older than the first release with reports');
  assert.ok(!isKnownToolVersion('coop', '1.4.0'), 'too far ahead');
  assert.ok(!isKnownToolVersion('coop', '9.9.9'));
  assert.ok(!isKnownToolVersion('ad1259', '1.2.61'), 'a coop version is not an AD1259 version');
  assert.ok(!isKnownToolVersion('coop', ''));
  assert.ok(!isKnownToolVersion('coop', 'dev build'));

  const gh = github();
  await handle(post(report({ toolVersion: '7.0.0' })), env(), gh.fetcher);
  assert.ok(held(gh.calls[0]));
  assert.match(gh.calls[0].body.body, /tool version 7\.0\.0 is not a known coop version/);
  const missing = github();
  await handle(post(report({ toolVersion: undefined })), env(), missing.fetcher);
  assert.match(missing.calls[0].body.body, /tool version missing is not a known coop version/);
});

test('reports past the day threshold of one install are held, the ones before are not', async () => {
  const known = { ...KNOWN };
  for (const id of ['A', 'B', 'C']) known[await modKey(id)] = JSON.stringify({ works: 1 });
  const e = env({ SUSPICIOUS_PER_INSTALL_DAY: '2' }, known);
  const gh = github();
  const reports = ['A', 'B', 'C'].map(id => ({ id, result: 'works' }));
  assert.equal((await handle(post(report({ reports })), e, gh.fetcher)).status, 200);
  assert.deepEqual(gh.calls.map(held), [false, false, true]);
  assert.match(gh.calls[2].body.body, /this install sent 3 reports today, more than 2/);

  await handle(post(report({ reports: [{ id: 'Bannerlord.Diplomacy', version: 'v9', result: 'works' }] })), e, gh.fetcher);
  assert.ok(held(gh.calls[3]), 'the count goes on over the day');
  assert.match(gh.calls[3].body.body, /this install sent 4 reports today/);
});

test('a burst of Broken reports for a mod with many Works is held; a mod with few Works is not', async () => {
  const many = await modKey('Popular');
  const few = await modKey('Rare');
  const installOf = n => String(n).repeat(32).slice(0, 32);
  const old = Object.fromEntries(await Promise.all([1, 2, 3, 4, 5, 6].map(n => seenBefore(installOf(n)))));
  const e = env({ BURST_BROKEN: '2', BURST_MIN_WORKS: '10' }, { ...old, [many]: JSON.stringify({ works: 10 }), [few]: JSON.stringify({ works: 3 }) });
  const gh = github();
  const broken = (id, n) => post(report({ install: installOf(n), reports: [{ id, result: 'broken' }] }));
  for (const n of [1, 2, 3]) await handle(broken('Popular', n), e, gh.fetcher);
  for (const n of [4, 5, 6]) await handle(broken('Rare', n), e, gh.fetcher);
  assert.deepEqual(gh.calls.map(held), [false, false, true, false, false, false]);
  assert.match(gh.calls[2].body.body, /3 Broken reports for this mod in the last 3 hours while it has 10 Works/);

  // Works reports raise the count of Works.
  await handle(post(report({ install: '7'.repeat(32), reports: [{ id: 'Rare', result: 'works' }] })), e, gh.fetcher);
  const stats = JSON.parse(e.VOTES.map.get(few));
  assert.deepEqual([stats.works, stats.broken], [4, 3]);
});

const LIMITS = { perInstall: 30, burstBroken: 5, burstMinWorks: 10, flipThreshold: 3, newInstallMinWorks: 10 };

test('Broken reports that would turn a working mod Broken within a few hours are held', () => {
  const base = { report: { tool: 'coop', toolVersion: '1.2.62' }, ordinal: 1, item: { result: 'broken' } };
  const flip = /would turn this mod from 2 more Works than Broken to Broken/;
  // 4 Works, 2 Broken from before and 3 in the last hours: with this one 4 - 6 = -2, not Broken yet.
  assert.deepEqual(flagsFor({ ...base, stats: { works: 4, broken: 5 }, recentBroken: 3 }, LIMITS), []);
  // ...the next one makes it -3, Broken by the tally, while Works led by 2 before the window.
  const reasons = flagsFor({ ...base, stats: { works: 4, broken: 6 }, recentBroken: 4 }, LIMITS);
  assert.equal(reasons.length, 1);
  assert.match(reasons[0], flip);
  // Broken that built up slowly (before the window) is not a flip.
  assert.deepEqual(flagsFor({ ...base, stats: { works: 4, broken: 6 }, recentBroken: 0 }, LIMITS), []);
  // Works reports never flip, and old stats without a Broken count still work.
  assert.deepEqual(flagsFor({ ...base, item: { result: 'works' }, stats: { works: 4, broken: 6 }, recentBroken: 4 }, LIMITS), []);
  assert.deepEqual(flagsFor({ ...base, stats: { works: 4 }, recentBroken: 0 }, LIMITS), []);
});

test('Broken from an install first seen today is held for a mod with few Works', async () => {
  const base = { report: { tool: 'coop', toolVersion: '1.2.62' }, ordinal: 1, item: { result: 'broken' }, recentBroken: 0 };
  assert.match(flagsFor({ ...base, stats: { works: 9 }, newInstall: true }, LIMITS)[0], /an install first seen today sent Broken for a mod with 9 Works/);
  assert.deepEqual(flagsFor({ ...base, stats: { works: 10 }, newInstall: true }, LIMITS), [], 'a mod with 10 Works is not low traffic');
  assert.deepEqual(flagsFor({ ...base, stats: { works: 2 }, newInstall: false }, LIMITS), []);
  assert.deepEqual(flagsFor({ ...base, item: { result: 'works' }, stats: { works: 2 }, newInstall: true }, LIMITS), []);

  // Through the worker: a brand new install's Broken is held, and it stays new for the day.
  const fresh = 'f'.repeat(32);
  const e = env();
  const gh = github();
  const broken = version => post(report({ install: fresh, reports: [{ id: 'Bannerlord.Diplomacy', version, result: 'broken' }] }));
  await handle(broken('v1'), e, gh.fetcher);
  await handle(broken('v2'), e, gh.fetcher);
  assert.deepEqual(gh.calls.map(held), [true, true]);
  assert.match(gh.calls[0].body.body, /an install first seen today/);
  assert.equal(e.VOTES.map.get(`i:${await reporterOf(fresh, 'salt')}`), new Date().toISOString().slice(0, 10));
  // The usual test install was seen before: not held.
  await handle(post(report({ reports: [{ id: 'Bannerlord.Diplomacy', version: 'v3', result: 'broken' }] })), e, gh.fetcher);
  assert.ok(!held(gh.calls[2]));
});

test('a changed vote is not edited when the review labels cannot be added', async () => {
  const e = env();
  const gh = github();
  await handle(post(report()), e, gh.fetcher);
  const calls = [];
  const failing = async (url, init) => {
    calls.push({ url, method: init.method });
    return url.endsWith('/labels') ? new Response('no', { status: 500 }) : new Response('{}', { status: 200 });
  };
  const res = await handle(post(report({ reports: [{ id: 'Bannerlord.Diplomacy', version: 'v1.2.3', result: 'broken' }] })), e, failing);
  assert.equal(res.status, 502);
  assert.deepEqual(calls.map(c => c.method), ['POST'], 'no PATCH after the labels failed');
  // The vote is not stored, so sending it again tries again.
  const again = github();
  assert.equal((await (await handle(post(report({ reports: [{ id: 'Bannerlord.Diplomacy', version: 'v1.2.3', result: 'broken' }] })), e, again.fetcher)).json()).updated, 1);
});

test('the burst rule on its own', () => {
  const limits = LIMITS;
  const base = { report: { tool: 'coop', toolVersion: '1.2.62' }, ordinal: 1, stats: { works: 12 } };
  assert.deepEqual(flagsFor({ ...base, item: { result: 'broken' }, recentBroken: 4 }, limits), [], 'five in the window is not more than five');
  assert.equal(flagsFor({ ...base, item: { result: 'broken' }, recentBroken: 5 }, limits).length, 1);
  assert.deepEqual(flagsFor({ ...base, item: { result: 'works' }, recentBroken: 50 }, limits), [], 'only Broken reports make a burst');
});

test('a changed vote that looks odd goes back to review with the suspicious label', async () => {
  const e = env();
  const gh = github();
  await handle(post(report()), e, gh.fetcher);
  await handle(post(report({ toolVersion: '0.0.1', reports: [{ id: 'Bannerlord.Diplomacy', version: 'v1.2.3', result: 'broken' }] })), e, gh.fetcher);
  assert.deepEqual(gh.calls[1].body, { labels: ['pending', 'suspicious'] });
  assert.match(gh.calls[2].body.body, /Held for review/);
});

test('a held report counts once Coodsz removes pending, and spam drops it', () => {
  const body = issueBody(validate(report()), { id: 'Held', name: null, version: '', result: 'works', with: null }, 'anon-' + 'a'.repeat(24), ['the first report anyone sent for this mod id']);
  const issue = labels => ({ number: 1, user: { login: 'Coodsz', type: 'User' }, created_at: '2026-10-22', body, labels: labels.map(name => ({ name })) });
  assert.equal(collectReports([issue(['mod-compat', 'mod-report', 'pending', 'suspicious'])]).reports.length, 0);
  assert.equal(collectReports([issue(['mod-compat', 'mod-report', 'suspicious'])]).reports.length, 1);
  assert.equal(collectReports([issue(['mod-compat', 'mod-report', 'suspicious', 'spam'])]).reports.length, 0);
});

test('mod reports: a failing KV counter or a missing address limiter never turns into a 500', async () => {
  const store = kv();
  const flaky = {
    ...store,
    get: async k => { if (/^[qi]:/.test(k)) throw new Error('KV get failed'); return store.get(k); },
    put: async (k, v) => { if (/^[qi]:/.test(k)) throw new Error('KV put limit'); return store.put(k, v); },
  };
  const warn = console.warn;
  const warnings = [];
  console.warn = line => warnings.push(String(line));
  try {
    const res = await handle(post(report()), env({ VOTES: flaky }), github().fetcher);
    assert.equal(res.status, 200);
    assert.equal((await res.json()).filed, 1);
    assert.ok(warnings.some(line => line.includes('REPORT_LIMITER is not bound')));
  } finally {
    console.warn = warn;
  }
});

// ---- The free plan's KV writes ------------------------------------------

/** A KV that counts puts by key, and can make some keys fail. */
function countingKv(e, failGet = () => false, failPut = () => false) {
  const store = e.VOTES;
  const puts = [];
  e.VOTES = {
    map: store.map,
    puts,
    get: async k => { if (failGet(k)) throw new Error('KV get failed'); return store.get(k); },
    put: async (k, v, o) => { if (failPut(k, v)) throw new Error('KV put limit'); puts.push(k); return store.put(k, v, o); },
  };
  return e;
}
/** A KV like Cloudflare's: a second write to one key within a second fails with a 429. */
function oneWriteASecond(e) {
  const store = e.VOTES;
  const last = new Map();
  e.VOTES = {
    map: store.map, get: store.get,
    put: async (k, v, o) => {
      if (last.has(k) && Date.now() - last.get(k) < 1000) throw new Error('KV PUT failed: 429 Too Many Requests');
      last.set(k, Date.now());
      return store.put(k, v, o);
    },
  };
  return e;
}
const today = () => new Date().toISOString().slice(0, 10);
const many = (n, result) => Array.from({ length: n }, (_, i) => ({ id: `Mod${i}`, version: '1.0', result }));
const quiet = async fn => {
  const warn = console.warn;
  console.warn = () => {};
  try { return await fn(); } finally { console.warn = warn; }
};
const brokenDiplomacy = () => post(report({ reports: [{ id: 'Bannerlord.Diplomacy', version: 'v1.2.3', result: 'broken' }] }));

test('a 25 mod report from a new install books exactly the writes it makes, and Broken costs no more than Works', async () => {
  for (const result of ['works', 'broken']) {
    const e = countingKv(env());
    const res = await handle(post(report({ install: 'c'.repeat(32), reports: many(25, result) })), e, github().fetcher);
    assert.equal(res.status, 200);
    assert.equal((await res.json()).filed, 25);
    // The booking, the install's day count, its first seen day, and per mod the stats and the vote with its number.
    assert.equal(e.VOTES.puts.length, 2 + 1 + 25 * 2, result);
    assert.equal(e.VOTES.map.get(`w:${today()}`), String(e.VOTES.puts.length), 'the booking matches the writes');
    assert.ok(!e.VOTES.puts.some(k => k.startsWith('b:')), 'no hourly Broken keys any more');
    assert.equal(new Set(e.VOTES.puts).size, e.VOTES.puts.length, 'no key is written twice');
  }
  // A changed vote: the vote and the stats, plus the booking and the day count.
  const e = env();
  await handle(post(report()), e, github().fetcher);
  countingKv(e);
  await handle(brokenDiplomacy(), e, github().fetcher);
  assert.equal(e.VOTES.puts.length, 4);
  // The same vote again writes nothing at all.
  countingKv(e);
  assert.equal((await (await handle(brokenDiplomacy(), e, github().fetcher)).json()).unchanged, 1);
  assert.deepEqual(e.VOTES.puts, []);
});

test('with KV taking one write a second per key, a report from a new install still stores every vote with its issue', async () => {
  const e = oneWriteASecond(env());
  const res = await handle(post(report({ install: 'c'.repeat(32), reports: many(25, 'broken') })), e, github().fetcher);
  assert.equal(res.status, 200);
  const reporter = await reporterOf('c'.repeat(32), 'salt');
  for (let i = 0; i < 25; i++)
    assert.ok(JSON.parse(e.VOTES.map.get(await voteKey(reporter, `Mod${i}`, '1.0'))).issue >= 100);
});

test('the write budget default leaves Send my logs its share, and a big report past it files nothing', async () => {
  assert.ok(DEFAULT_REPORT_WRITES_DAY <= 150, 'crash reports, the brake and reply push keep the rest of the 1,000 (README.md)');
  assert.ok(DEFAULT_REPORT_WRITES_DAY >= 2 + 1 + 25 * 2, 'one full report from a new install always fits');
  const e = env();
  e.VOTES.map.set(`w:${today()}`, String(DEFAULT_REPORT_WRITES_DAY - 10));
  const gh = github();
  const res = await handle(post(report({ reports: many(5, 'works') })), e, gh.fetcher);
  assert.equal(res.status, 429);
  assert.deepEqual(gh.calls, []);
  assert.equal(e.VOTES.map.get(`w:${today()}`), String(DEFAULT_REPORT_WRITES_DAY - 10), 'nothing booked');
  // A small one still fits.
  assert.equal((await handle(post(report()), e, gh.fetcher)).status, 200);
});

test('a write budget that does not parse falls back to the default instead of turning the cap off', async () => {
  for (const bad of ['', 'abc', '-5', '1e9', ' ']) {
    const e = env({ MAX_REPORT_WRITES_DAY: bad });
    e.VOTES.map.set(`w:${today()}`, String(DEFAULT_REPORT_WRITES_DAY));
    const gh = github();
    assert.equal((await handle(post(report()), e, gh.fetcher)).status, 429, JSON.stringify(bad));
    assert.deepEqual(gh.calls, []);
  }
  const zero = env({ MAX_REPORT_WRITES_DAY: '0' });
  assert.equal((await handle(post(report()), zero, github().fetcher)).status, 429, '0 turns mod reports off');
});

test('the write budget fails closed: when it cannot be read or booked, no issue is filed and nothing is written', async () => {
  const cases = [['read', k => k.startsWith('w:'), () => false], ['write', () => false, k => k.startsWith('w:')]];
  for (const [name, failGet, failPut] of cases) {
    const e = countingKv(env(), failGet, failPut);
    const gh = github();
    const res = await quiet(() => handle(post(report()), e, gh.fetcher));
    assert.equal(res.status, 503, name);
    assert.deepEqual(gh.calls, [], name);
    assert.deepEqual(e.VOTES.puts, [], name);
  }
});

test('KV must take a write before an issue is filed: when it does not, the answer is 503 and no issue is filed', async () => {
  const e = countingKv(env(), () => false, k => k.startsWith('m:'));
  const gh = github();
  const res = await quiet(() => handle(post(report()), e, gh.fetcher));
  assert.equal(res.status, 503);
  assert.equal((await res.json()).filed, 0);
  assert.deepEqual(gh.calls, []);

  // The order on a good day: the stats, the issue, the vote with its number.
  const ok = env();
  const order = [];
  const store = ok.VOTES;
  ok.VOTES = { map: store.map, get: store.get, put: async (k, v) => { order.push(k[0] === 'm' ? 'stats' : k[0] === 'v' ? JSON.parse(v).issue : k[0]); return store.put(k, v); } };
  const gh2 = github();
  const fetcher = async (url, init) => { order.push('issue'); return gh2.fetcher(url, init); };
  await handle(post(report()), ok, fetcher);
  assert.deepEqual(order, ['w', 'q', 'stats', 'issue', 100]);
});

test('a request that stops early gives back the writes it booked but did not make, even when GitHub fails fast', async () => {
  // KV refuses a second write to w: within a second, and GitHub answers at once.
  const e = env();
  const plain = e.VOTES;
  oneWriteASecond(e);
  const failing = async () => new Response('no', { status: 500 });
  // 25 new votes book 2 + 50; GitHub fails on the first, after the booking, the day count and one stats
  // write. The give back is one more write, so 4 stay booked.
  assert.equal((await quiet(() => handle(post(report({ reports: many(25, 'works') })), e, failing))).status, 502);
  assert.equal(e.VOTES.map.get(`w:${today()}`), '4');
  // The vote was not stored, so sending it again (a moment later) files it.
  e.VOTES = plain;
  const gh = github();
  const again = await (await handle(post(report({ reports: many(25, 'works') })), e, gh.fetcher)).json();
  assert.equal(again.filed, 25);
});

test('a changed vote that cannot be stored answers 503, and sending it again edits the same issue', async () => {
  const e = env();
  await handle(post(report()), e, github().fetcher);
  const store = e.VOTES;
  e.VOTES = { map: store.map, get: store.get, put: async (k, v, o) => { if (k.startsWith('v:')) throw new Error('KV put limit'); return store.put(k, v, o); } };
  assert.equal((await quiet(() => handle(brokenDiplomacy(), e, github().fetcher))).status, 503);
  e.VOTES = store;
  const gh = github();
  const again = await (await handle(brokenDiplomacy(), e, gh.fetcher)).json();
  assert.equal(again.updated, 1);
  assert.match(gh.calls[1].url, /\/issues\/100$/);
});

test('a failed stats read never writes over the stats or skips the holds: new and changed votes wait (503)', async () => {
  const mod = await modKey('Bannerlord.Diplomacy');
  const e = env({}, { [mod]: JSON.stringify({ works: 40, broken: 2 }) });
  const store = e.VOTES;
  const failing = { map: store.map, put: store.put, get: async k => { if (k === mod) throw new Error('KV get failed'); return store.get(k); } };
  e.VOTES = failing;
  const gh = github();
  assert.equal((await quiet(() => handle(brokenDiplomacy(), e, gh.fetcher))).status, 503);
  assert.deepEqual(gh.calls, []);
  assert.deepEqual(JSON.parse(store.map.get(mod)), { works: 40, broken: 2 });

  e.VOTES = store;
  await handle(post(report()), e, github().fetcher);
  const statsNow = store.map.get(mod);
  e.VOTES = failing;
  const changed = github();
  assert.equal((await quiet(() => handle(brokenDiplomacy(), e, changed.fetcher))).status, 503);
  assert.deepEqual(changed.calls, [], 'the issue is not edited without the holds');
  assert.equal(store.map.get(mod), statsNow, 'a changed vote does not touch stats it could not read');
});

test('a failed read of the install day count does not write over it', async () => {
  const e = env();
  const reporter = await reporterOf(INSTALL, 'salt');
  const q = `q:${reporter}:${today()}`;
  e.VOTES.map.set(q, '55');
  const store = e.VOTES;
  e.VOTES = { map: store.map, put: store.put, get: async k => { if (k === q) throw new Error('KV get failed'); return store.get(k); } };
  assert.equal((await handle(post(report()), e, github().fetcher)).status, 200);
  assert.equal(store.map.get(q), '55');
});

test('mod stats are kept per tool, start from the shared stats, and a TAOM report never touches the Coop and Friends stats', async () => {
  assert.equal(await modKey('Mod'), await modKey('Mod', 'coop'), 'Coop and Friends keeps its key');
  assert.notEqual(await modKey('Mod', 'taom'), await modKey('Mod'));
  assert.match(await modKey('Mod', 'rot'), /^m:rot:[0-9a-f]{32}$/);

  const h = Math.floor(Date.now() / 3600000);
  const coopKey = await modKey('Bannerlord.Diplomacy');
  const e = env({}, { [coopKey]: JSON.stringify({ works: 15, broken: 1, h: { [h]: 1 } }) });
  const before = e.VOTES.map.get(coopKey);
  const gh = github();
  await handle(post(report({ tool: 'taom', toolVersion: '1.1.41', reports: [{ id: 'Bannerlord.Diplomacy', version: 'v1.2.3', result: 'broken' }] })), e, gh.fetcher);
  assert.equal(e.VOTES.map.get(coopKey), before);
  // Seeded from the shared record (without its hours), so the mod is known and not held as a first report.
  assert.deepEqual(JSON.parse(e.VOTES.map.get(await modKey('Bannerlord.Diplomacy', 'taom'))), { works: 15, broken: 2, h: { [h]: 1 } });
  assert.ok(!held(gh.calls[0]));
  // A mod nobody reported in any tool is still held on its first TAOM Broken.
  await handle(post(report({ tool: 'taom', toolVersion: '1.1.41', reports: [{ id: 'Brand.New', result: 'broken' }] })), e, gh.fetcher);
  assert.ok(held(gh.calls[1]));
});

test('a burst of TAOM Broken for a mod well rated before stats were per tool is still held', async () => {
  const installOf = n => String(n).repeat(32).slice(0, 32);
  const old = Object.fromEntries(await Promise.all([1, 2, 3].map(n => seenBefore(installOf(n)))));
  const e = env({ BURST_BROKEN: '2', BURST_MIN_WORKS: '10' }, { ...old, [await modKey('Popular')]: JSON.stringify({ works: 15 }) });
  const gh = github();
  for (const n of [1, 2, 3])
    await handle(post(report({ tool: 'taom', toolVersion: '1.1.41', install: installOf(n), reports: [{ id: 'Popular', result: 'broken' }] })), e, gh.fetcher);
  assert.deepEqual(gh.calls.map(held), [false, false, true]);
});

test('the hourly Broken counts live in the stats record, old b: keys still count, and old hours drop out', async () => {
  const h = Math.floor(Date.now() / 3600000);
  assert.deepEqual(nextStats(null, 'works', h), { works: 1, broken: 0 });
  assert.deepEqual(nextStats({ works: 2, broken: 1, h: { [h - 5]: 4, [h - 1]: 1 } }, 'broken', h), { works: 2, broken: 2, h: { [h - 1]: 1, [h]: 1 } });
  assert.deepEqual(nextStats({ works: 2, broken: 1, h: { [h - 5]: 4 } }, 'works', h), { works: 3, broken: 1 });
  assert.deepEqual(nextStats({ works: '5', broken: -1 }, 'works', h), { works: 1, broken: 0 }, 'odd counts start over');

  // One Broken in the stats this hour and one under the old key make this one the third, past BURST_BROKEN 2.
  const mod = await modKey('Popular');
  const e = env({ BURST_BROKEN: '2', BURST_MIN_WORKS: '10' }, {
    [mod]: JSON.stringify({ works: 10, broken: 2, h: { [h]: 1 } }),
    [`b:${mod.slice(2)}:${h - 1}`]: '1',
  });
  const gh = github();
  await handle(post(report({ reports: [{ id: 'Popular', result: 'broken' }] })), e, gh.fetcher);
  assert.match(gh.calls[0].body.body, /3 Broken reports for this mod in the last 3 hours while it has 10 Works/);
});
