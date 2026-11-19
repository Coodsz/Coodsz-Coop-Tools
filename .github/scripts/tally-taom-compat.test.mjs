// node --test .github/scripts/   (Node 18+, no packages)
import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import { fileURLToPath } from 'node:url';
import { collectReports, AD1259_LABEL } from './tally-mod-compat.mjs';
import { applyTaomTally, serializeTaom, renderModsPage, statusText, playersCell, checkedCell, NOT_CHECKED, LISTS, listOf, AD1259_STATUS, AD1259_REPORT_FORM_URL, BE_REPORT_FORM_URL, ROT_REPORT_FORM_URL, STATUS } from './tally-taom-compat.mjs';

let number = 0;

/** A TAOM report as the worker files it in Coodsz-TAOM (approved unless pending is passed). */
function anon(reporter, modId, result, extra = {}) {
  number++;
  return {
    number,
    user: { login: 'coodsz', type: 'User' },
    created_at: extra.created ?? `2026-10-${String(10 + (number % 18)).padStart(2, '0')}T00:00:00Z`,
    labels: ['mod-compat', 'mod-report', 'taom', ...(extra.labels ?? [])].map(name => ({ name })),
    body: `<!-- coodsz-mod-report v1 -->\n### Mod id\n\n${modId}\n\n### Mod name\n\n${extra.name ?? modId}\n\n### Result\n\n${result}\n\n### Reporter\n\n${reporter}\n`,
  };
}

/** A report from the TAOM issue form (labels mod-compat and taom). */
function form(login, modId, result, created = '2026-10-01T00:00:00Z') {
  number++;
  return {
    number, user: { login, type: 'User' }, created_at: created, labels: [{ name: 'mod-compat' }, { name: 'taom' }],
    body: `### Mod id\n\n${modId}\n\n### Mod name\n\n_No response_\n\n### Result\n\n${result}\n\n### TAOM version\n\n2.0.29.5\n`,
  };
}

const hand = () => ({
  kind: 'CoodszTaomCompat', schemaVersion: 2, taom: '2.0.29.5', game: 'v1.4.8', coop: 'v0.1.6', updated: '2026-10-18',
  mods: [
    { id: 'RTSCamera', name: 'RTS Camera', workshopId: '3747725551', tag: 'safe', class: 'D', source: 'coodsz-coop', checked: '2026-10-18', note: 'Camera and battle orders.' },
    { id: 'Fourberie', name: 'Fourberie', workshopId: '', nexusId: '13207', tag: 'risky', class: 'D', source: 'server-loads', checked: '2026-10-18', note: 'Changes battles | one patch.' },
    { id: 'Bad.Mod', name: 'Bad Mod', workshopId: '', tag: 'broken', class: 'D', source: 'taom-discord', checked: '2026-10-17', note: 'Crashes.' },
  ],
});

const tally = (doc, issues) => {
  const { reports, skipped } = collectReports(issues, { leaveOut: [AD1259_LABEL] });
  const result = applyTaomTally(doc, reports);
  return { doc, skipped, ...result };
};

test('the tally keeps every hand field and never changes the tag', () => {
  const before = hand();
  const issues = ['a', 'b', 'c', 'd'].map(r => anon(`anon-${r.repeat(8)}`, 'RTSCamera', 'broken'));
  const { doc } = tally(hand(), issues);
  const rts = doc.mods[0];
  for (const [k, v] of Object.entries(before.mods[0])) assert.equal(rts[k], v, k);
  assert.equal(rts.tag, 'safe', 'four Broken reports do not move a hand checked tag');
  assert.deepEqual([rts.works, rts.broken], [0, 4]);
  assert.deepEqual(doc.mods.map(m => m.id), before.mods.map(m => m.id), 'the hand order stays');
  for (const k of ['kind', 'schemaVersion', 'taom', 'game', 'coop', 'updated']) assert.equal(doc[k], before[k], k);
});

test('only approved reports count: pending, invalid and spam are skipped, the latest vote per person wins', () => {
  const issues = [
    anon('anon-aaaaaaaa', 'RTSCamera', 'broken', { created: '2026-10-11T00:00:00Z' }),
    anon('anon-aaaaaaaa', 'RTSCamera', 'works', { created: '2026-10-12T00:00:00Z' }),
    anon('anon-bbbbbbbb', 'RTSCamera', 'works', { labels: ['pending'] }),
    anon('anon-cccccccc', 'RTSCamera', 'works', { labels: ['invalid'] }),
    anon('anon-dddddddd', 'RTSCamera', 'works', { labels: ['spam'] }),
    anon('anon-eeeeeeee', 'RTSCamera', 'works', { labels: ['pending', 'suspicious'] }),
    form('player1', 'rtscamera', 'client-only', '2026-10-20T08:00:00Z'),
    form('player2', 'RTSCamera', 'works'),
  ];
  const { doc, skipped } = tally(hand(), issues);
  const rts = doc.mods[0];
  assert.deepEqual([rts.works, rts.broken, rts.clientOnly, rts.lastReport], [2, 0, 1, '2026-10-20']);
  assert.deepEqual(skipped.map(s => s[1]).sort(), ['invalid/spam', 'invalid/spam', 'pending', 'pending']);
});

test('TAOM labelled reports count here, AD1259 ones do not', () => {
  const ad = anon('anon-ffffffff', 'Bad.Mod', 'broken', { labels: ['ad1259'] });
  const { doc, skipped } = tally(hand(), [anon('anon-aaaaaaaa', 'Bad.Mod', 'broken'), ad]);
  assert.equal(doc.mods[2].broken, 1);
  assert.deepEqual(skipped, [[ad.number, 'AD1259 report']]);
});

test('reported mods that are not on the list go into community, never into mods', () => {
  const { doc, added } = tally(hand(), [anon('anon-aaaaaaaa', 'New.Mod', 'works', { name: 'A New Mod' }), anon('anon-bbbbbbbb', 'New.Mod', 'broken')]);
  assert.equal(doc.mods.length, 3);
  assert.deepEqual(added, ['New.Mod']);
  assert.deepEqual(doc.community, [{ id: 'New.Mod', name: 'A New Mod', works: 1, broken: 1, lastReport: doc.community[0].lastReport }]);
  // Once nobody reports it any more (both dropped as spam), the community array goes away.
  const again = tally(doc, []);
  assert.equal(again.doc.community, undefined);
});

test('a mod with no counted reports has no count fields, and running twice gives the same file', () => {
  const issues = [anon('anon-aaaaaaaa', 'Fourberie', 'works'), anon('anon-bbbbbbbb', 'Other.Mod', 'works')];
  const first = serializeTaom(tally(hand(), issues).doc);
  const second = serializeTaom(tally(JSON.parse(first), issues).doc);
  assert.equal(second, first);
  const doc = JSON.parse(first);
  assert.ok(!('works' in doc.mods[0]) && !('broken' in doc.mods[0]));
  assert.equal(doc.mods[1].works, 1);
  // Counts go last on the line, after the hand fields.
  assert.match(first, /"note": "Changes battles \| one patch\.", "works": 1, "broken": 0, "lastReport": "2026-10-\d\d" \}/);
  const cleared = JSON.parse(serializeTaom(tally(doc, []).doc));
  assert.deepEqual(cleared, hand(), 'with no reports the file is the hand list again');
});

test('the shipped list goes through the tally unchanged byte for byte', () => {
  const path = fileURLToPath(new URL('../../src/TaomCoop.Core/Compat/taom-mod-compat.json', import.meta.url));
  const text = fs.readFileSync(path, 'utf8');
  const doc = JSON.parse(text);
  applyTaomTally(doc, []);
  assert.equal(serializeTaom(doc, text.includes('\r\n') ? '\r\n' : '\n'), text);
});

test('MODS.md lists the mods by status then name with plain words, links, player counts and checks', () => {
  const { doc } = tally(hand(), [
    anon('anon-aaaaaaaa', 'RTSCamera', 'works'), anon('anon-bbbbbbbb', 'RTSCamera', 'broken'),
    anon('anon-cccccccc', 'Zeta.Mod', 'works', { name: 'Zeta Mod' }),
  ]);
  doc.mods.push({ id: 'Alpha', name: 'Alpha', tag: 'loads', source: 'server-loads', checked: '2026-10-18', note: 'Loads.\nJoining not tested.' });
  const page = renderModsPage(doc);
  const rows = page.split('\n').filter(l => l.startsWith('| ') && !l.startsWith('| Mod') && !l.startsWith('| ---'));
  assert.deepEqual(rows, [
    '| [RTS Camera](https://steamcommunity.com/sharedfiles/filedetails/?id=3747725551) | Works | 1 works, 1 broken | 2026-10-18 | Camera and battle orders. |',
    '| Alpha | Loads |  | 2026-10-18 (server load test) | Loads. Joining not tested. |',
    '| [Fourberie](https://www.nexusmods.com/mountandblade2bannerlord/mods/13207) | Risky |  | 2026-10-18 (server load test) | Changes battles \\| one patch. |',
    '| Bad Mod | Broken |  |  | Crashes. |',
    `| Zeta Mod | ${NOT_CHECKED} | 1 works, 0 broken |  |  |`,
  ]);
  assert.match(page, /^# TAOM mod list/);
  assert.match(page, /Report a mod/);
  assert.match(page, /\| Mod \| Status \| Players \| Checked by Coodsz \| Note \|/);
  assert.equal(renderModsPage(doc), page, 'the page only depends on the file');
});

test('the status words for every tag', () => {
  assert.deepEqual(['safe', 'everyone', 'loads', 'reported', 'risky', 'broken', '', 'odd'].map(statusText),
    ['Works', 'Everyone needs it', 'Loads', 'Reported working', 'Risky', 'Broken', NOT_CHECKED, NOT_CHECKED]);
  assert.equal(playersCell({}), '');
  assert.equal(playersCell({ works: 2, broken: 0, clientOnly: 1 }), '2 works, 0 broken, 1 client only');
  assert.equal(checkedCell({ source: 'taom-discord', checked: '2026-10-18' }), '', 'only checks by Coodsz show');
  assert.equal(checkedCell({ source: 'coodsz-coop', checked: 'soon' }), '');
});

test('names cannot break the table or add HTML', () => {
  const page = renderModsPage({ kind: 'CoodszTaomCompat', mods: [{ id: 'x', name: 'A | B [c] <script>', tag: 'risky', workshopId: '12', note: 'n <b>' }] });
  assert.ok(page.includes('| [A \\| B \\[c\\] &lt;script>](https://steamcommunity.com/sharedfiles/filedetails/?id=12) | Risky |  |  | n &lt;b> |'));
});

// ---- The AD1259 list (--list ad1259, in Coodsz-AD1259) ----------------------

const repoFile = rel => fileURLToPath(new URL(`../../${rel}`, import.meta.url));

const pack = () => ({
  kind: 'CoodszAd1259Compat', schemaVersion: 1, ad1259: '1.4.6', game: 'v1.4.8', coop: 'v0.1.6', tested: '2026-10-13', updated: '2026-10-24',
  mods: [
    { id: 'RBM', name: 'Realistic Battle Mod', version: 'v4.5.4', workshopId: '2859251492', tag: 'pack' },
    { id: 'AD1259', name: 'Anno Domini 1259', version: 'v1.4.6', nexusId: '6723', tag: 'pack', note: 'The map.' },
    { id: 'Extra.Mod', name: 'An Extra', tag: 'safe' },
  ],
});

/** An AD1259 report as the worker files it in Coodsz-AD1259. */
const adReport = (reporter, modId, result, labels = []) => anon(reporter, modId, result, { labels: ['ad1259', ...labels] });

const tallyAd = (doc, issues) => {
  const { reports, skipped } = collectReports(issues, { leaveOut: LISTS.ad1259.leaveOut });
  return { doc, skipped, ...applyTaomTally(doc, reports) };
};

test('the AD1259 list counts AD1259 reports and leaves TAOM ones out, never moving the pack tag', () => {
  // anon() adds the taom label, so the AD1259 reports here are built without it.
  const strip = issue => ({ ...issue, labels: issue.labels.filter(l => l.name !== 'taom') });
  const issues = [
    strip(adReport('anon-aaaaaaaa', 'RBM', 'broken')),
    strip(adReport('anon-bbbbbbbb', 'RBM', 'broken')),
    strip(adReport('anon-cccccccc', 'AD1259', 'works')),
    strip(adReport('anon-dddddddd', 'AD1259', 'works', ['pending'])),
    strip(adReport('anon-eeeeeeee', 'Other.Mod', 'works')),
    anon('anon-ffffffff', 'RBM', 'works'), // a TAOM report never counts here
  ];
  const { doc, skipped, added } = tallyAd(pack(), issues);
  assert.deepEqual(doc.mods.map(m => [m.id, m.tag, m.works, m.broken]), [['RBM', 'pack', 0, 2], ['AD1259', 'pack', 1, 0], ['Extra.Mod', 'safe', undefined, undefined]]);
  assert.deepEqual(added, ['Other.Mod']);
  assert.deepEqual(skipped.map(s => s[1]).sort(), ['TAOM report', 'pending']);
  assert.equal(doc.mods[0].version, 'v4.5.4', 'the hand fields stay');
});

test('the AD1259 MODS.md keeps the pack in load order with versions, players and the last report', () => {
  const { doc } = tallyAd(pack(), [{ ...adReport('anon-aaaaaaaa', 'RBM', 'broken'), labels: [{ name: 'mod-compat' }, { name: 'mod-report' }, { name: 'ad1259' }], created_at: '2026-10-22T05:00:00Z' }]);
  const page = renderModsPage(doc);
  assert.equal(listOf(doc), LISTS.ad1259);
  const rows = page.split('\n').filter(l => l.startsWith('| ') && !l.startsWith('| Mod | Status') && !l.startsWith('| ---'));
  assert.deepEqual(rows, [
    '| [Realistic Battle Mod](https://steamcommunity.com/sharedfiles/filedetails/?id=2859251492) | In the pack | v4.5.4 | 0 works, 1 broken | 2026-10-22 |  |',
    '| [Anno Domini 1259](https://www.nexusmods.com/mountandblade2bannerlord/mods/6723) | In the pack | v1.4.6 |  |  | The map. |',
    '| An Extra | Works |  |  |  |  |',
  ]);
  assert.match(page, /^# AD1259 mod list/);
  assert.match(page, /\| Mod \| Status \| Version \| Players \| Last report \| Note \|\n\| --- \| --- \| --- \| --- \| --- \| --- \|/);
  assert.ok(page.includes(`[report form](${AD1259_REPORT_FORM_URL})`));
  assert.ok(page.includes('(2026-10-13)'));
  assert.deepEqual(['pack', 'safe', 'risky', 'broken', 'loads'].map(t => statusText(t, AD1259_STATUS)), ['In the pack', 'Works', 'Risky', 'Broken', NOT_CHECKED]);
});

test('the seeded AD1259 list goes through the tally unchanged, and the MODS.md files in the launcher repository match their lists', () => {
  const text = fs.readFileSync(repoFile('ad1259-compat/compat/ad1259-mod-compat.json'), 'utf8');
  const doc = JSON.parse(text);
  assert.equal(doc.kind, LISTS.ad1259.kind);
  assert.ok(doc.mods.length > 0 && doc.mods.every(m => m.tag === 'pack' && m.version));
  applyTaomTally(doc, []);
  assert.equal(serializeTaom(doc, text.includes('\r\n') ? '\r\n' : '\n'), text);
  assert.equal(fs.readFileSync(repoFile('ad1259-compat/MODS.md'), 'utf8'), renderModsPage(doc));
  const taom = JSON.parse(fs.readFileSync(repoFile('src/TaomCoop.Core/Compat/taom-mod-compat.json'), 'utf8'));
  assert.equal(fs.readFileSync(repoFile('taom-compat/MODS.md'), 'utf8'), renderModsPage(taom));
});

test('a reported mod named like a web address is shown as text, not as a link', () => {
  const { doc } = tally(hand(), [anon('anon-aaaaaaaa', 'www.scam-site.example', 'works', { name: 'www.scam-site.example' })]);
  const row = renderModsPage(doc).split('\n').find(l => l.includes('scam-site'));
  assert.equal(row, `| www\\.scam-site.example | ${NOT_CHECKED} | 1 works, 0 broken |  |  |`);
});

// ---- The Burning Empires list (--list be, in Coodsz-BurningEmpires) ----------------------

const beHand = () => ({
  kind: 'CoodszBeCompat', schemaVersion: 2, be: '2.0.2', game: 'v1.4.8', coop: 'v0.1.6', updated: '2026-10-26',
  mods: [
    { id: 'BurningEmpires', name: 'Burning Empires', workshopId: '', nexusId: '7750', tag: 'everyone', source: 'coodsz-notes', checked: '2026-10-26', note: 'The overhaul itself.' },
    { id: 'TAOM', name: 'TAOM', workshopId: '', tag: 'broken', source: 'coodsz-notes', checked: '2026-10-26', note: 'Another overhaul.' },
  ],
});

/** A BE report as the worker files it in Coodsz-BurningEmpires (anon() adds taom, so it is taken off). */
const beReport = (reporter, modId, result, labels = []) => {
  const issue = anon(reporter, modId, result, { labels: ['be', ...labels] });
  return { ...issue, labels: issue.labels.filter(l => l.name !== 'taom') };
};

/** A report from the BE issue form (labels mod-compat and be). */
const beForm = (login, modId, result) => ({
  ...form(login, modId, result),
  labels: [{ name: 'mod-compat' }, { name: 'be' }],
});

const tallyBe = (doc, issues) => {
  const { reports, skipped } = collectReports(issues, { leaveOut: LISTS.be.leaveOut });
  return { doc, skipped, ...applyTaomTally(doc, reports) };
};

test('the BE list counts BE reports and the BE form, and leaves TAOM and AD1259 ones out, never moving a tag', () => {
  const issues = [
    beReport('anon-aaaaaaaa', 'BurningEmpires', 'works'),
    beReport('anon-bbbbbbbb', 'BurningEmpires', 'broken'),
    beReport('anon-cccccccc', 'BurningEmpires', 'works', ['pending']),
    beReport('anon-dddddddd', 'Extra.Mod', 'works'),
    beForm('player1', 'TAOM', 'works'),
    anon('anon-eeeeeeee', 'BurningEmpires', 'works'), // a TAOM report never counts here
    // An AD1259 report never counts here either.
    { ...adReport('anon-ffffffff', 'BurningEmpires', 'works'), labels: [{ name: 'mod-compat' }, { name: 'mod-report' }, { name: 'ad1259' }] },
  ];
  const { doc, skipped, added } = tallyBe(beHand(), issues);
  assert.deepEqual(doc.mods.map(m => [m.id, m.tag, m.works, m.broken]), [['BurningEmpires', 'everyone', 1, 1], ['TAOM', 'broken', 1, 0]]);
  assert.deepEqual(added, ['Extra.Mod']);
  assert.deepEqual(skipped.map(s => s[1]).sort(), ['AD1259 report', 'TAOM report', 'pending']);
  assert.equal(doc.mods[0].note, 'The overhaul itself.', 'the hand fields stay');
});

test('the BE MODS.md uses the TAOM tags with Burning Empires words and the BE report form', () => {
  const { doc } = tallyBe(beHand(), [beReport('anon-aaaaaaaa', 'BurningEmpires', 'works', [])]);
  const page = renderModsPage(doc);
  assert.equal(listOf(doc), LISTS.be);
  assert.equal(LISTS.be.statuses, STATUS);
  assert.match(page, /^# Burning Empires mod list/);
  assert.ok(page.includes('(Burning Empires 2.0.2, Bannerlord v1.4.8, Coop v0.1.6)'));
  assert.ok(page.includes('Coodsz Burning Empires Client Launcher'));
  assert.ok(page.includes(`[report form](${BE_REPORT_FORM_URL})`));
  assert.equal(BE_REPORT_FORM_URL, 'https://github.com/Coodsz/Coodsz-BurningEmpires/issues/new?template=mod-compat.yml');
  assert.ok(!/TAOM (server|singleplayer|Client)/.test(page), 'no TAOM wording is left on the BE page');
  const rows = page.split('\n').filter(l => l.startsWith('| ') && !l.startsWith('| Mod | Status') && !l.startsWith('| ---'));
  assert.deepEqual(rows, [
    '| [Burning Empires](https://www.nexusmods.com/mountandblade2bannerlord/mods/7750) | Everyone needs it | 1 works, 0 broken |  | The overhaul itself. |',
    '| TAOM | Broken |  |  | Another overhaul. |',
  ]);
});

test('the seeded BE list goes through the tally unchanged, knows the BE core, refuses other overhauls, and its MODS.md matches', () => {
  const text = fs.readFileSync(repoFile('be-compat/compat/be-mod-compat.json'), 'utf8');
  const doc = JSON.parse(text);
  assert.equal(doc.kind, LISTS.be.kind);
  assert.equal(doc.schemaVersion, 2);
  const tag = id => doc.mods.find(m => m.id === id)?.tag;
  // The repository seed is the list the tools have built in (src/BurningEmpires.Core/Compat), byte for byte.
  const builtIn = fs.readFileSync(repoFile('src/BurningEmpires.Core/Compat/be-mod-compat.json'), 'utf8');
  assert.equal(text.replace(/\r\n/g, '\n'), builtIn.replace(/\r\n/g, '\n'));
  for (const id of ['BurningEmpires', 'BurningEmpires_Elephants', 'BurningEmpires.CoopCompat']) assert.ok(['safe', 'everyone'].includes(tag(id)), id);
  for (const id of ['TAOM', 'AD1259']) assert.equal(tag(id), 'broken', id);
  assert.ok(doc.mods.every(m => STATUS.some(([t]) => t === m.tag)), 'every tag is one the BE tools know');
  applyTaomTally(doc, []);
  assert.equal(serializeTaom(doc, text.includes('\r\n') ? '\r\n' : '\n'), text);
  // A CRLF checkout (no .gitattributes line yet) still compares the same text.
  assert.equal(fs.readFileSync(repoFile('be-compat/MODS.md'), 'utf8').replace(/\r\n/g, '\n'), renderModsPage(doc));
});

// ---- The Realm of Thrones list (--list rot, in Coodsz-RealmOfThrones) ----------------------

/** A RoT report as the worker files it in Coodsz-RealmOfThrones (anon() adds taom, so it is taken off). */
const rotReport = (reporter, modId, result, labels = []) => {
  const issue = anon(reporter, modId, result, { labels: ['rot', ...labels] });
  return { ...issue, labels: issue.labels.filter(l => l.name !== 'taom') };
};

test('the RoT list counts RoT reports and leaves TAOM, AD1259 and BE ones out, and its MODS.md uses Realm of Thrones words', () => {
  const doc = {
    kind: 'CoodszRotCompat', schemaVersion: 2, rot: '8.1.2', game: 'v1.4.8', coop: 'v0.1.8', updated: '2026-11-15',
    mods: [{ id: 'ROT-Core', name: 'Realm of Thrones Core', workshopId: '', nexusId: '2907', tag: 'safe', source: 'coodsz-notes', checked: '2026-11-15', note: 'The core.' }],
  };
  const issues = [
    rotReport('anon-aaaaaaaa', 'ROT-Core', 'works'),
    rotReport('anon-bbbbbbbb', 'Extra.Mod', 'broken'),
    anon('anon-cccccccc', 'ROT-Core', 'works'), // a TAOM report never counts here
    beReport('anon-dddddddd', 'ROT-Core', 'works'), // nor a BE one
    // nor an AD1259 one; without the taom label anon() adds, so only AD1259_LABEL in leaveOut keeps it out
    { ...adReport('anon-eeeeeeee', 'ROT-Core', 'works'), labels: [{ name: 'mod-compat' }, { name: 'mod-report' }, { name: 'ad1259' }] },
  ];
  const { reports, skipped } = collectReports(issues, { leaveOut: LISTS.rot.leaveOut });
  const { added } = applyTaomTally(doc, reports);
  assert.deepEqual(doc.mods.map(m => [m.id, m.tag, m.works]), [['ROT-Core', 'safe', 1]]);
  assert.deepEqual(added, ['Extra.Mod']);
  assert.deepEqual(skipped.map(s => s[1]).sort(), ['AD1259 report', 'Burning Empires report', 'TAOM report']);
  const page = renderModsPage(doc);
  assert.equal(listOf(doc), LISTS.rot);
  assert.match(page, /^# Realm of Thrones mod list/);
  assert.ok(page.includes('(Realm of Thrones 8.1.2, Bannerlord v1.4.8, Coop v0.1.8)'));
  assert.ok(page.includes(`[report form](${ROT_REPORT_FORM_URL})`));
  assert.equal(ROT_REPORT_FORM_URL, 'https://github.com/Coodsz/Coodsz-RealmOfThrones/issues/new?template=mod-compat.yml');
  assert.ok(!/(TAOM|Burning Empires) (server|singleplayer|Client)/.test(page), 'no other tool wording is left on the RoT page');
});

test('the seeded RoT list goes through the tally unchanged, matches the built in copy, and its MODS.md matches', () => {
  const text = fs.readFileSync(repoFile('rot-compat/compat/rot-mod-compat.json'), 'utf8');
  const doc = JSON.parse(text);
  assert.equal(doc.kind, LISTS.rot.kind);
  const builtIn = fs.readFileSync(repoFile('src/RealmOfThrones.Core/Compat/rot-mod-compat.json'), 'utf8');
  assert.equal(text.replace(/\r\n/g, '\n'), builtIn.replace(/\r\n/g, '\n'));
  const tag = id => doc.mods.find(m => m.id === id)?.tag;
  for (const id of ['Bannerlord.Harmony', 'Bannerlord.ButterLib', 'Bannerlord.UIExtenderEx', 'Bannerlord.MBOptionScreen', 'ROT-Core', 'ROT-Content', 'ROT_Map', 'ROT-Dragon', 'RealmOfThrones.CoopCompat'])
    assert.equal(tag(id), 'safe', id);
  for (const id of ['TAOM', 'AD1259', 'BurningEmpires']) assert.equal(tag(id), 'broken', id);
  assert.ok(doc.mods.every(m => STATUS.some(([t]) => t === m.tag)), 'every tag is one the RoT tools know');
  applyTaomTally(doc, []);
  assert.equal(serializeTaom(doc, text.includes('\r\n') ? '\r\n' : '\n'), text);
  assert.equal(fs.readFileSync(repoFile('rot-compat/MODS.md'), 'utf8').replace(/\r\n/g, '\n'), renderModsPage(doc));
});
