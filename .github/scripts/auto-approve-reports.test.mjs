// node --test .github/scripts/   (Node 18+, no packages)
import test from 'node:test';
import assert from 'node:assert/strict';
import { autoApprove, waitReason, waitHoursFrom, DEFAULT_WAIT_HOURS } from './auto-approve-reports.mjs';

const NOW = Date.parse('2026-10-23T12:00:00Z');
const hoursAgo = h => new Date(NOW - h * 3600 * 1000).toISOString();

let number = 0;
/** A worker report as the issues API lists it. */
function issue(hours, labels = [], extra = {}) {
  number++;
  return {
    number, state: 'open', created_at: hoursAgo(hours + 1), updated_at: hoursAgo(hours),
    labels: ['mod-compat', 'mod-report', 'pending', ...labels].map(name => ({ name })), ...extra,
  };
}

/** A fake GitHub: serves the issues (100 a page) and records every call. */
function github(issues, { deleteStatus = 200, now = {} } = {}) {
  const calls = [];
  const fetcher = async (url, init) => {
    calls.push({ url, method: init.method, auth: init.headers.Authorization });
    const one = url.match(/\/issues\/(\d+)$/);
    if (init.method === 'GET' && one) {
      // The issue as it is now: changed by the test (now), gone (null), or as listed.
      const n = Number(one[1]);
      const current = n in now ? now[n] : issues.find(i => i.number === n);
      return current ? new Response(JSON.stringify(current), { status: 200 }) : new Response('{}', { status: 404 });
    }
    if (init.method === 'GET') {
      const page = Number(new URL(url).searchParams.get('page'));
      return new Response(JSON.stringify(issues.slice((page - 1) * 100, page * 100)), { status: 200 });
    }
    return new Response('[]', { status: deleteStatus });
  };
  return { calls, fetcher, deletes: () => calls.filter(c => c.method === 'DELETE').map(c => Number(c.url.match(/issues\/(\d+)\/labels/)[1])) };
}

const run = (gh, extra = {}) => autoApprove({ repo: 'Coodsz/Coodsz-Coop-and-Friends', token: 'test-token', fetcher: gh.fetcher, now: NOW, waitHours: 24, ...extra });

test('a normal report is approved after the wait, a newer one waits', async () => {
  const old = issue(25), fresh = issue(23);
  const gh = github([old, fresh]);
  const { approved, left } = await run(gh);
  assert.deepEqual(approved, [old.number]);
  assert.deepEqual(left, [[fresh.number, 'waiting']]);
  assert.deepEqual(gh.deletes(), [old.number]);
  const del = gh.calls.find(c => c.method === 'DELETE');
  assert.equal(del.url, `https://api.github.com/repos/Coodsz/Coodsz-Coop-and-Friends/issues/${old.number}/labels/pending`);
  assert.equal(del.auth, 'Bearer test-token');
  const list = new URL(gh.calls[0].url);
  assert.equal(list.searchParams.get('labels'), 'mod-report,pending');
  assert.equal(list.searchParams.get('state'), 'open');
});

test('suspicious, spam and invalid reports stay pending for Coodsz, whatever their age', async () => {
  const issues = [issue(100, ['suspicious']), issue(100, ['spam']), issue(100, ['Invalid'])];
  const gh = github(issues);
  const { approved, left } = await run(gh);
  assert.deepEqual(approved, []);
  assert.deepEqual(left.map(l => l[1]), ['held (suspicious)', 'held (spam)', 'held (invalid)']);
  assert.equal(gh.deletes().length, 0);
});

test('the wait counts from the last change, so a changed vote waits again', () => {
  const changed = issue(2, [], { created_at: hoursAgo(200) });
  assert.equal(waitReason(changed, NOW, 24), 'waiting');
  assert.equal(waitReason(changed, NOW, 1), null);
  assert.equal(waitReason({ ...changed, updated_at: undefined }, NOW, 24), null, 'created_at when there is no updated_at');
});

test('only open pending mod reports are touched', () => {
  assert.equal(waitReason(issue(30, [], { state: 'closed' }), NOW, 24), 'not an open issue');
  assert.equal(waitReason(issue(30, [], { pull_request: {} }), NOW, 24), 'not an open issue');
  const form = { number: 1, state: 'open', updated_at: hoursAgo(30), labels: [{ name: 'mod-compat' }, { name: 'pending' }] };
  assert.equal(waitReason(form, NOW, 24), 'not a pending mod report');
  const approved = { number: 2, state: 'open', updated_at: hoursAgo(30), labels: ['mod-compat', 'mod-report'] };
  assert.equal(waitReason(approved, NOW, 24), 'not a pending mod report');
});

test('TAOM and AD1259 reports are approved too (the tally leaves them out anyway)', async () => {
  const issues = [issue(30, ['taom']), issue(30, ['ad1259'])];
  const gh = github(issues);
  assert.deepEqual((await run(gh)).approved, issues.map(i => i.number));
});

test('every page is read', async () => {
  const issues = Array.from({ length: 150 }, () => issue(30));
  const gh = github(issues);
  const { approved } = await run(gh);
  assert.equal(approved.length, 150);
  assert.equal(gh.calls.filter(c => c.method === 'GET' && c.url.includes('page=')).length, 2);
});

test('a dry run removes nothing', async () => {
  const gh = github([issue(30)]);
  const { approved } = await run(gh, { dryRun: true });
  assert.equal(approved.length, 1);
  assert.equal(gh.deletes().length, 0);
});

test('a label Coodsz already removed is fine, another error stops the run', async () => {
  assert.equal((await run(github([issue(30)], { deleteStatus: 404 }))).approved.length, 1);
  await assert.rejects(run(github([issue(30)], { deleteStatus: 403 })), /403/);
  const failing = async () => new Response('nope', { status: 500 });
  await assert.rejects(autoApprove({ repo: 'r/r', token: 't', fetcher: failing, now: NOW }), /500/);
});

test('each report is read again just before approving, so a label added since the list holds it', async () => {
  const spam = issue(30), suspicious = issue(30), changed = issue(30), gone = issue(30), fine = issue(30);
  const gh = github([spam, suspicious, changed, gone, fine], {
    now: {
      [spam.number]: { ...spam, labels: [...spam.labels, { name: 'spam' }] },
      [suspicious.number]: { ...suspicious, labels: [...suspicious.labels, { name: 'suspicious' }] },
      // The worker changed the vote a moment ago, which bumps updated_at.
      [changed.number]: { ...changed, updated_at: hoursAgo(0.1) },
      [gone.number]: null,
    },
  });
  const { approved, left } = await run(gh);
  assert.deepEqual(approved, [fine.number]);
  assert.deepEqual(left, [
    [spam.number, 'held (spam)'], [suspicious.number, 'held (suspicious)'], [changed.number, 'waiting'], [gone.number, 'gone'],
  ]);
  assert.deepEqual(gh.deletes(), [fine.number]);
  // The read comes before the delete.
  const read = gh.calls.findIndex(c => c.method === 'GET' && c.url.endsWith(`/issues/${fine.number}`));
  const del = gh.calls.findIndex(c => c.method === 'DELETE');
  assert.ok(read >= 0 && read < del);
});

test('a failed read of one issue stops the run instead of approving it', async () => {
  const one = issue(30);
  const fetcher = async (url, init) => url.endsWith(`/issues/${one.number}`)
    ? new Response('boom', { status: 502 })
    : init.method === 'GET' ? new Response(JSON.stringify([one]), { status: 200 }) : new Response('[]', { status: 200 });
  await assert.rejects(autoApprove({ repo: 'r/r', token: 't', fetcher, now: NOW, waitHours: 24 }), /502/);
});

test('the wait time comes from WAIT_HOURS, with 24 as the default', () => {
  assert.equal(DEFAULT_WAIT_HOURS, 24);
  assert.equal(waitHoursFrom('48'), 48);
  assert.equal(waitHoursFrom(undefined), 24);
  assert.equal(waitHoursFrom(''), 24);
  assert.equal(waitHoursFrom('soon'), 24);
  assert.equal(waitHoursFrom('0'), 24, 'no wait at all is not allowed');
});
