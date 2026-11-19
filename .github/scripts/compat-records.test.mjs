// node --test .github/scripts/   (Node 18+, no packages)
// The export is checked against Ryvius' compat-record.schema.json (copied next to this file) with a
// small validator for the keywords that schema uses, plus the rules his format page says a schema
// cannot express (no control or invisible characters, the same Id once).
import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import { buildRecords, verdictFor, notesFor, serializeRecords, RECORD_THRESHOLD } from './compat-records.mjs';
import { collectReports, applyTally } from './tally-mod-compat.mjs';

const schema = JSON.parse(fs.readFileSync(new URL('./compat-record.schema.json', import.meta.url), 'utf8'));

function typeOf(v) {
  if (v === null) return 'null';
  if (Array.isArray(v)) return 'array';
  if (Number.isInteger(v)) return 'integer';
  return typeof v;
}

/** Errors of value against a draft 2020-12 schema (the keywords compat-record.schema.json uses). */
export function validate(value, node = schema, path = '$', errors = []) {
  if (node.$ref) node = node.$ref.replace(/^#\//, '').split('/').reduce((n, k) => n[k], schema);
  const t = typeOf(value);
  if (node.type) {
    const types = [].concat(node.type);
    if (!types.some(x => x === t || (x === 'number' && t === 'integer'))) errors.push(`${path}: type ${t}, wanted ${types}`);
  }
  if ('const' in node && value !== node.const) errors.push(`${path}: not ${node.const}`);
  if (node.enum && !node.enum.includes(value)) errors.push(`${path}: ${value} not in ${node.enum}`);
  if (t === 'string') {
    if (node.minLength != null && [...value].length < node.minLength) errors.push(`${path}: too short`);
    if (node.maxLength != null && [...value].length > node.maxLength) errors.push(`${path}: too long`);
    if (node.pattern && !new RegExp(node.pattern, 'u').test(value)) errors.push(`${path}: does not match ${node.pattern}`);
    if (node.format === 'date-time' && !/^\d{4}-\d\d-\d\dT\d\d:\d\d:\d\d(\.\d+)?(Z|[+-]\d\d:\d\d)$/.test(value)) errors.push(`${path}: not a date-time`);
  }
  if (t === 'array') {
    if (node.maxItems != null && value.length > node.maxItems) errors.push(`${path}: too many items`);
    if (node.items) value.forEach((v, i) => validate(v, node.items, `${path}[${i}]`, errors));
  }
  if (t === 'object') {
    const keys = Object.keys(value);
    for (const k of node.required ?? []) if (!(k in value)) errors.push(`${path}: ${k} missing`);
    if (node.maxProperties != null && keys.length > node.maxProperties) errors.push(`${path}: too many properties`);
    for (const k of keys) {
      if (node.properties && k in node.properties) validate(value[k], node.properties[k], `${path}.${k}`, errors);
      else if (node.additionalProperties === false) errors.push(`${path}: unknown property ${k}`);
      else if (typeof node.additionalProperties === 'object') validate(value[k], node.additionalProperties, `${path}.${k}`, errors);
    }
  }
  return errors;
}

/** The checks his intake tool makes that the schema leaves out. */
function intakeErrors(file) {
  const errors = [];
  const bad = /[\p{Cc}\p{Cf}\p{Zl}\p{Zp}]/u;
  const walk = (v, p) => {
    if (typeof v === 'string' && bad.test(v)) errors.push(`${p}: control or invisible character`);
    else if (v && typeof v === 'object') for (const [k, x] of Object.entries(v)) { if (bad.test(k)) errors.push(`${p}: bad key`); walk(x, `${p}.${k}`); }
  };
  walk(file, '$');
  const ids = file.Records.map(r => r.Id.toLowerCase());
  if (new Set(ids).size !== ids.length) errors.push('the same Id twice');
  return errors;
}

let number = 0;
function anon(reporter, modId, result, version, coop, day) {
  number++;
  return {
    number,
    user: { login: 'coodsz', type: 'User' },
    created_at: `2026-10-${String(day).padStart(2, '0')}T08:30:00Z`,
    labels: [{ name: 'mod-compat' }, { name: 'mod-report' }],
    body: `### Mod id\n\n${modId}\n\n### Mod name\n\n${modId}\n\n### Mod version\n\n${version}\n\n### Result\n\n${result}\n\n### Works only with\n\n_No response_\n\n### Coop version\n\n${coop}\n\n### Mod list\n\n_No response_\n\n### Reporter\n\n${reporter}\n`,
  };
}

const sample = () => {
  const mods = [
    { id: 'GoodMod', name: 'Good Mod', status: 'Unknown', workshop: '123456', works: 0, broken: 0 },
    { id: 'BadMod', status: 'Unknown', works: 0, broken: 0 },
    { id: 'CloseCall', status: 'Unknown', works: 0, broken: 0 },
    { id: 'Guess Name', status: 'Unknown', idGuess: true, works: 0, broken: 0 },
  ];
  const issues = [
    anon('anon-00000001', 'GoodMod', 'Works', 'v1.2.0', 'v0.1.6', 11),
    anon('anon-00000002', 'GoodMod', 'Works', 'v1.3.0', 'v0.1.6', 12),
    anon('anon-00000003', 'GoodMod', 'Works', 'v1.3.0', 'v0.1.5', 13),
    anon('anon-00000004', 'GoodMod', 'Works', 'v1.3.0\u202e', 'v0.1.6', 14),
    anon('anon-00000001', 'BadMod', 'Broken', 'e2.0', 'v0.1.6', 11),
    anon('anon-00000002', 'BadMod', 'Broken', 'e2.0', 'v0.1.6', 12),
    anon('anon-00000003', 'BadMod', 'Broken', 'e2.0', 'v0.1.6', 15),
    anon('anon-00000001', 'CloseCall', 'Works', 'v1', 'v0.1.6', 11),
    anon('anon-00000002', 'CloseCall', 'Works', 'v1', 'v0.1.6', 11),
    anon('anon-00000003', 'CloseCall', 'Broken', 'v1', 'v0.1.6', 11),
  ];
  const { reports } = collectReports(issues);
  const { reportsByEntry } = applyTally(mods, reports);
  return buildRecords(mods, reportsByEntry);
};

test('the verdict needs works - broken to reach the threshold', () => {
  assert.equal(RECORD_THRESHOLD, 3);
  assert.equal(verdictFor(3, 0), 'Works');
  assert.equal(verdictFor(5, 3), null);
  assert.equal(verdictFor(1, 4), 'Broken');
  assert.equal(verdictFor(0, 0), null);
});

test('notes say how many players reported what', () => {
  assert.equal(notesFor(12, 2), '12 players reported works, 2 broken, with the Coodsz tools.');
  assert.equal(notesFor(1, 0), '1 player reported works, 0 broken, with the Coodsz tools.');
});

test('the export has the agreed mods with versions, Coop version, date and notes', () => {
  const file = sample();
  assert.deepEqual(file.Records.map(r => r.Id), ['BadMod', 'GoodMod']);
  const good = file.Records.find(r => r.Id === 'GoodMod');
  assert.equal(good.Verdict, 'Works');
  // Newest first; the version with an invisible character is dropped.
  assert.deepEqual(good.TestedVersions, ['v1.3.0', 'v1.2.0']);
  assert.equal(good.TestedCoopVersion, 'v0.1.6');
  assert.equal(good.UpdatedAt, '2026-10-14T08:30:00Z');
  assert.equal(good.Notes, '4 players reported works, 0 broken, with the Coodsz tools.');
  assert.equal(good.Url, 'https://steamcommunity.com/sharedfiles/filedetails/?id=123456');
  const bad = file.Records.find(r => r.Id === 'BadMod');
  assert.equal(bad.Verdict, 'Broken');
  assert.deepEqual(bad.TestedVersions, ['e2.0']);
});

test('a hand kept entry gets a record only when the counts agree with the status Coodsz set', () => {
  const mods = [
    { id: 'HandWorks', status: 'Works', coop: 'v0.1.6', works: 0, broken: 4 },
    { id: 'HandAgrees', status: 'Broken', coop: 'v0.1.6', works: 0, broken: 4 },
    { id: 'HandPartial', status: 'ClientOnly', coop: 'v0.1.6', works: 5, broken: 0 },
    { id: 'Counted', status: 'Works', works: 5, broken: 0 },
  ];
  const file = buildRecords(mods);
  assert.deepEqual(file.Records.map(r => [r.Id, r.Verdict]), [['Counted', 'Works'], ['HandAgrees', 'Broken']]);
  for (const r of file.Records) assert.equal(r.Verdict, mods.find(m => m.id === r.Id).status, 'never says something else than mods.json');
});

test('UpdatedAt changes only when the record content changes', () => {
  const first = sample();
  const good = first.Records.find(r => r.Id === 'GoodMod');
  // The same content later keeps the date from before.
  const pinned = { ...first, Records: first.Records.map(r => (r.Id === 'GoodMod' ? { ...r, UpdatedAt: '2026-10-20T00:00:00Z' } : r)) };
  const mods = [{ id: 'GoodMod', name: 'Good Mod', status: 'Works', workshop: '123456', works: 4, broken: 0 }];
  const entry = new Map([[mods[0], [
    { result: 'works', version: 'v1.3.0', coop: 'v0.1.6', created: '2026-10-12T08:30:00Z' },
    { result: 'works', version: 'v1.2.0', coop: 'v0.1.6', created: '2026-10-11T08:30:00Z' },
    { result: 'works', version: 'v1.3.0', coop: 'v0.1.5', created: '2026-10-13T08:30:00Z' },
    { result: 'works', version: '', coop: 'v0.1.6', created: '2026-10-14T08:30:00Z' },
  ]]]);
  const now = new Date('2026-10-30T10:00:00Z');
  const same = buildRecords(mods, entry, { previous: pinned, now }).Records[0];
  assert.deepEqual({ ...same, UpdatedAt: undefined }, { ...good, UpdatedAt: undefined });
  assert.equal(same.UpdatedAt, '2026-10-20T00:00:00Z');

  // A new Broken report changes the notes: the record changed, so the date moves on (now, as no
  // report is newer than the old date).
  mods[0].broken = 1;
  const changed = buildRecords(mods, entry, { previous: pinned, now }).Records[0];
  assert.equal(changed.Notes, '4 players reported works, 1 broken, with the Coodsz tools.');
  assert.equal(changed.UpdatedAt, '2026-10-30T10:00:00Z');

  // Without a file from before, the newest report gives the date as before.
  assert.equal(good.UpdatedAt, '2026-10-14T08:30:00Z');
});

test('the export is the strict form of the ModderLords schema', () => {
  const file = JSON.parse(serializeRecords(sample()));
  assert.deepEqual(validate(file), []);
  assert.deepEqual(intakeErrors(file), []);
  assert.deepEqual(Object.keys(file), ['SchemaVersion', 'Records']);
});

test('the validator catches what the strict form refuses', () => {
  assert.notDeepEqual(validate({ SchemaVersion: 1, Records: [{ Id: 'A', verdict: 'Works' }] }), []);
  assert.notDeepEqual(validate({ SchemaVersion: 2, Records: [] }), []);
  assert.notDeepEqual(validate({ Records: [{ Id: '../x' }] }), []);
  assert.notDeepEqual(validate({ Records: [{ Id: 'A', Verdict: 'Fine' }] }), []);
  assert.notDeepEqual(intakeErrors({ Records: [{ Id: 'A' }, { Id: 'a' }] }), []);
});

test('the live list file in compat-list is valid too', () => {
  const path = new URL('../../compat-list/compat-records.json', import.meta.url);
  const file = JSON.parse(fs.readFileSync(path, 'utf8'));
  assert.deepEqual(validate(file), []);
  assert.deepEqual(intakeErrors(file), []);
});
