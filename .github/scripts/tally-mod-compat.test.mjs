// node --test .github/scripts/   (Node 18+, no packages)
import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import { fileURLToPath } from 'node:url';
import { collectReports, applyTally, serialize, parseJsonc, renderModsPage, coopStatusText, coopPlayersCell, NOT_CHECKED, REPORT_FORM_URL } from './tally-mod-compat.mjs';

let number = 0;

/** An anonymous report as the worker files it (approved unless pending is passed). */
function anon(reporter, modId, result, mods = [], extra = {}) {
  number++;
  const list = mods.length ? '```\n' + mods.map(id => `${id} | 1.0`).join('\n') + '\n```' : '_No response_';
  return {
    number,
    user: { login: 'coodsz', type: 'User' },
    created_at: `2026-10-${String(10 + number).padStart(2, '0')}T00:00:00Z`,
    updated_at: extra.updated_at,
    labels: [{ name: 'mod-compat' }, { name: 'mod-report' }, ...(extra.labels ?? []).map(name => ({ name }))],
    body: `<!-- coodsz-mod-report v1 -->\n### Mod id\n\n${modId}\n\n### Mod name\n\n${modId}\n\n### Result\n\n${result}\n\n### Works only with\n\n${extra.with ?? '_No response_'}\n\n### Mod list\n\n${list}\n\n### Reporter\n\n${reporter}\n`,
  };
}

/** A report from the GitHub issue form, as before. */
function form(login, modId, result) {
  number++;
  return {
    number, user: { login, type: 'User' }, created_at: '2026-10-01T00:00:00Z', labels: [{ name: 'mod-compat' }],
    body: `### Mod id\n\n${modId}\n\n### Result\n\n${result}\n\n### Coop version\n\nv0.1.6\n`,
  };
}

const tally = (mods, issues) => {
  const { reports, skipped } = collectReports(issues);
  applyTally(mods, reports);
  return { mods, skipped };
};

test('pending reports do not count until Coodsz removes the label', () => {
  const { mods, skipped } = tally([{ id: 'A', status: 'Unknown', works: 0, broken: 0 }], [anon('anon-aaaaaaaa', 'A', 'works', [], { labels: ['pending'] })]);
  assert.equal(mods[0].works, 0);
  assert.equal(skipped[0][1], 'pending');
});

test('one anonymous reporter counts once per mod, the latest vote wins', () => {
  const issues = [
    anon('anon-aaaaaaaa', 'A', 'works'),
    anon('anon-aaaaaaaa', 'A', 'broken'),
    anon('anon-bbbbbbbb', 'A', 'works'),
  ];
  const { mods } = tally([{ id: 'A', status: 'Unknown', works: 0, broken: 0 }], issues);
  assert.deepEqual([mods[0].works, mods[0].broken], [1, 1]);
});

test('approving an older version report later does not beat a newer vote', () => {
  // The 1.0 report is filed first, then 1.1 (a new issue, since the worker keeps one per version).
  // Coodsz removes pending from the 1.0 issue last, which bumps its updated_at.
  const older = anon('anon-cccccccc', 'B', 'broken', [], { updated_at: '2026-12-01T00:00:00Z' });
  const newer = anon('anon-cccccccc', 'B', 'works', [], { updated_at: '2026-11-01T00:00:00Z' });
  const { mods } = tally([{ id: 'B', status: 'Unknown', works: 0, broken: 0 }], [older, newer]);
  assert.deepEqual([mods[0].works, mods[0].broken], [1, 0]);
});

test('a reporter line only counts on worker issues; a form issue goes by its login', () => {
  const fake = anon('anon-cccccccc', 'A', 'works');
  fake.labels = [{ name: 'mod-compat' }];
  fake.user = { login: 'someone', type: 'User' };
  const { mods } = tally([{ id: 'A', status: 'Unknown', works: 0, broken: 0 }], [fake, form('someone', 'A', 'works')]);
  assert.equal(mods[0].works, 1, 'the same login twice is one vote');
});

test('mod lists give tested with, conflicts and works only with', () => {
  const issues = [
    anon('anon-00000001', 'A', 'works', ['Bannerlord.Harmony', 'A', 'B', 'C']),
    anon('anon-00000002', 'A', 'works', ['A', 'B']),
    anon('anon-00000003', 'A', 'broken', ['A', 'X', 'B']),
    anon('anon-00000004', 'A', 'broken', ['A', 'X']),
    anon('anon-00000005', 'A', 'works-only-with', ['A', 'B'], { with: 'B' }),
    anon('anon-00000006', 'D', 'broken', ['D', 'X']),
    anon('anon-00000007', 'D', 'broken', ['D', 'X']),
  ];
  const { mods } = tally([{ id: 'A', status: 'Unknown', works: 0, broken: 0 }, { id: 'D', status: 'Unknown', works: 0, broken: 0 }], issues);
  const a = mods.find(m => m.id === 'A');
  assert.deepEqual([a.works, a.broken], [3, 2], 'works only with counts as works');
  assert.deepEqual(a.testedWith, ['B', 'C'], 'Harmony and the mod itself are left out');
  assert.deepEqual(a.conflicts, ['X'], 'X was in two broken lists and no works list; B was in a works list too');
  assert.deepEqual(a.needs, ['B']);
  const d = mods.find(m => m.id === 'D');
  assert.equal(d.conflicts, undefined, 'a mod nobody got working has no conflicts to blame');
});

test('TAOM reports are left for the hand kept TAOM list', () => {
  const { mods, skipped } = tally([{ id: 'A', status: 'Unknown', works: 0, broken: 0 }], [anon('anon-aaaaaaaa', 'A', 'works', [], { labels: ['taom'] })]);
  assert.equal(mods[0].works, 0);
  assert.equal(skipped[0][1], 'TAOM report');
});

test('form reports still count as before and an old list keeps its shape', () => {
  const original = '{\n  // comment\n  "mods": [\n    { "id": "A", "name": "A", "status": "Unknown", "works": 0, "broken": 0 }\n  ]\n}\n';
  const doc = parseJsonc(original);
  const issues = [form('p1', 'A', 'works'), form('p2', 'A', 'works'), form('p3', 'A', 'works')];
  const { reports } = collectReports(issues);
  applyTally(doc.mods, reports);
  assert.equal(doc.mods[0].status, 'Works');
  const text = serialize(original, doc.mods);
  assert.ok(!text.includes('testedWith') && !text.includes('conflicts') && !text.includes('needs'), 'no empty new fields');
  assert.ok(text.startsWith('{\n  // comment'));
});

test('the tally is the same when run twice', () => {
  const issues = [anon('anon-00000001', 'A', 'works', ['A', 'B']), anon('anon-00000002', 'A', 'works', ['A', 'C'])];
  const once = tally([{ id: 'A', status: 'Unknown', works: 0, broken: 0 }], issues).mods;
  const twice = tally(structuredClone(once), issues).mods;
  assert.deepEqual(twice, once);
});

test('AD1259 reports are left out of the count like the TAOM ones', () => {
  const { mods, skipped } = tally([{ id: 'A', status: 'Unknown', works: 0, broken: 0 }], [anon('anon-aaaaaaaa', 'A', 'broken', [], { labels: ['ad1259'] })]);
  assert.equal(mods[0].broken, 0);
  assert.equal(skipped[0][1], 'AD1259 report');
});

test('Burning Empires reports filed here by mistake are left out of the count too', () => {
  const { mods, skipped } = tally([{ id: 'A', status: 'Unknown', works: 0, broken: 0 }], [anon('anon-bbbbbbbb', 'A', 'broken', [], { labels: ['be'] })]);
  assert.equal(mods[0].broken, 0);
  assert.equal(skipped[0][1], 'Burning Empires report');
});

test('Realm of Thrones reports filed here by mistake are left out of the count too', () => {
  const { mods, skipped } = tally([{ id: 'A', status: 'Unknown', works: 0, broken: 0 }], [anon('anon-bbbbbbbb', 'A', 'broken', [], { labels: ['rot'] })]);
  assert.equal(mods[0].broken, 0);
  assert.equal(skipped[0][1], 'Realm of Thrones report');
});

test('client only reports and the day of the newest report are kept next to the counts, never moving the status', () => {
  const issues = [form('p1', 'A', 'client-only'), form('p2', 'A', 'client-only'), form('p3', 'A', 'client-only'), anon('anon-aaaaaaaa', 'A', 'works')];
  const { mods } = tally([{ id: 'A', status: 'Unknown', works: 0, broken: 0 }, { id: 'B', status: 'Unknown', works: 0, broken: 0 }], issues);
  const [a, b] = mods;
  assert.deepEqual([a.status, a.works, a.broken, a.clientOnly], ['Unknown', 1, 0, 3]);
  assert.equal(a.lastReport, issues[3].created_at.slice(0, 10));
  assert.ok(!('clientOnly' in b) && !('lastReport' in b), 'nothing for a mod nobody reported');
  const text = serialize('{\n  "mods": [\n  ]\n}\n', mods);
  assert.match(text, /"works": 1, "broken": 0, "clientOnly": 3, "lastReport": "2026-10-\d\d" \}/);
  // Once the reports drop out, both fields go away again.
  const cleared = tally(mods, []).mods;
  assert.ok(!('clientOnly' in cleared[0]) && !('lastReport' in cleared[0]));
});

const page = mods => renderModsPage({ mods });
const rowsOf = text => text.split('\n').filter(l => l.startsWith('| ') && !l.startsWith('| Mod | Status') &&!l.startsWith('| ---'));

test('MODS.md lists every mod by status then name with plain words, links, player counts and the Coop version', () => {
  const text = page([
    { id: 'Z', name: 'Zed', status: 'Unknown', works: 1, broken: 1, lastReport: '2026-10-20' },
    { id: 'Fix', name: 'Fixer', status: 'Works', coop: 'v0.1.6', note: 'Works with issues. Remove the Assets folder.', works: 0, broken: 0 },
    { id: 'Bad', name: 'Bad Mod', status: 'Broken', coop: 'v0.1.6', note: 'Crashes on join.', workshop: '', nexus: 4600, works: 0, broken: 2 },
    { id: 'Good', name: 'Good Mod', status: 'Works', coop: 'v0.1.6', workshop: '2880158010', works: 3, broken: 0, clientOnly: 1, lastReport: '2026-10-21' },
    { id: 'Crowd', name: 'Crowd Pick', status: 'Works', works: 4, broken: 0 },
    { id: 'Crowd.Bad', name: 'Crowd Bad', status: 'Broken', works: 0, broken: 3 },
    { id: 'Looks', name: 'Looks', status: 'ClientOnly', coop: 'v0.1.6', works: 0, broken: 0 },
    { id: 'Alpha', name: 'Alpha', status: 'Unknown', works: 0, broken: 0, idGuess: true },
  ]);
  assert.deepEqual(rowsOf(text), [
    '| [Good Mod](https://steamcommunity.com/sharedfiles/filedetails/?id=2880158010) | Works | 3 works, 0 broken, 1 client only | 2026-10-21 | v0.1.6 |  |',
    '| Fixer | Works with issues |  |  | v0.1.6 | Works with issues. Remove the Assets folder. |',
    '| Looks | Client only |  |  | v0.1.6 |  |',
    '| Crowd Pick | Reported working | 4 works, 0 broken |  |  |  |',
    '| Crowd Bad | Reported broken | 0 works, 3 broken |  |  |  |',
    '| [Bad Mod](https://www.nexusmods.com/mountandblade2bannerlord/mods/4600) | Broken | 0 works, 2 broken |  | v0.1.6 | Crashes on join. |',
    `| Alpha | ${NOT_CHECKED} |  |  |  |  |`,
    `| Zed | ${NOT_CHECKED} | 1 works, 1 broken | 2026-10-20 |  |  |`,
  ]);
  assert.match(text, /^# Coop and Friends mod list/);
  assert.match(text, /\| Mod \| Status \| Players \| Last report \| Checked with Coop \| Note \|\n\| --- \| --- \| --- \| --- \| --- \| --- \|/);
  assert.ok(text.includes(`[report form](${REPORT_FORM_URL})`));
  assert.ok(text.includes('Report a mod'));
  assert.ok(text.endsWith('8 mods.\n'));
});

test('the status words and the players cell', () => {
  assert.equal(coopStatusText({ status: 'Works' }), 'Reported working');
  assert.equal(coopStatusText({ status: 'Works', coop: ' ' }), 'Reported working', 'an empty coop is not hand kept');
  assert.equal(coopStatusText({ status: 'Works', coop: 'v0.1.6', note: 'works with issues. x' }), 'Works with issues');
  assert.equal(coopStatusText({ status: 'Odd' }), NOT_CHECKED);
  assert.equal(coopStatusText({}), NOT_CHECKED);
  assert.equal(coopPlayersCell({ works: 0, broken: 0 }), '');
  assert.equal(coopPlayersCell({ works: 0, broken: 0, clientOnly: 2 }), '0 works, 0 broken, 2 client only');
});

test('names and notes cannot break the MODS.md table or add HTML', () => {
  const text = page([{ id: 'x', name: 'A | B [c] <script>', status: 'Broken', coop: 'v0.1.6', workshop: '12', note: 'n <b>\nnext' }]);
  assert.deepEqual(rowsOf(text), ['| [A \\| B \\[c\\] &lt;script>](https://steamcommunity.com/sharedfiles/filedetails/?id=12) | Broken |  |  | v0.1.6 | n &lt;b> next |']);
});

test('the shipped list renders a row for every entry, and the tally keeps its comment block', () => {
  const path = fileURLToPath(new URL('../../compat-list/mods.json', import.meta.url));
  const original = fs.readFileSync(path, 'utf8');
  const doc = parseJsonc(original);
  assert.equal(rowsOf(renderModsPage(doc)).length, doc.mods.length);
  applyTally(doc.mods, []);
  const text = serialize(original, doc.mods);
  assert.ok(text.startsWith(original.slice(0, original.indexOf('"mods"'))));
  assert.equal(parseJsonc(text).mods.length, parseJsonc(original).mods.length);
});

test('reported ids and names that look like web addresses do not become links on MODS.md', () => {
  const issues = [anon('anon-aaaaaaaa', 'www.scam-site.example', 'works'), anon('anon-bbbbbbbb', 'WWW.Other.example', 'broken')];
  const { mods } = tally([{ id: 'Real', name: 'Real Mod', status: 'Works', coop: 'v0.1.6', workshop: '12', works: 0, broken: 0 }], issues);
  const rows = rowsOf(page(mods));
  assert.ok(rows.includes('| Real Mod | Works |  |  | v0.1.6 |  |'.replace('Real Mod', '[Real Mod](https://steamcommunity.com/sharedfiles/filedetails/?id=12)')), 'a hand link stays a link');
  assert.ok(rows.some(r => r.startsWith('| www\\.scam-site.example | Reported working') || r.startsWith(`| www\\.scam-site.example | ${NOT_CHECKED}`)));
  assert.ok(rows.some(r => r.startsWith('| WWW\\.Other.example |')));
  assert.ok(rows.every(r => !/(^|[\s|(])www\./i.test(r)), 'no bare www. left');
  assert.equal(rowsOf(page([{ id: 'x', status: 'Unknown', note: 'see https://evil.example now' }]))[0], `| x | ${NOT_CHECKED} |  |  |  | see https:\\/\\/evil.example now |`);
});
