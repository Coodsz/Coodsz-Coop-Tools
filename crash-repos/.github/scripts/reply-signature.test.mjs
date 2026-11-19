// node --test crash-repos/.github/scripts/reply-signature.test.mjs   (Node 20+, no packages, fake GitHub)
import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import {
  candidateFor, crashLineOf, patternOf, hasSensitive, insertSignature, signatureText, openPullRequest, pullRequestBody, shortName,
} from './reply-signature.mjs';

const COODSZ = 4242;
const LINE = '[12:00:01] ERROR System.ArgumentOutOfRangeException: Index was out of range. at TaleWorlds.CampaignSystem.Roster.TroopRoster.AddToCountsAtIndex(Int32 index, Int32 count) in C:\\build\\TroopRoster.cs:line 120';
const issueBody = line => [
  '<!-- coodsz-crash-report v1 -->', 'Report code **CR-7KQ2M**', '',
  'In tool replies: on (comment /reply to answer the player)', '',
  '<details><summary>Technical details</summary>', '', 'Code: CR-7KQ2M', '',
  'Crash line:', '', ...(line ? ['```', line, '```'] : ['_none_']), '', '</details>',
].join('\n');

const event = ({ body = '/reply Turn off Better Time. Then start the game again.', userId = COODSZ, type = 'User',
  labels = ['crash-report', 'client', 'coop', 'new', 'unknown'], line = LINE,
  title = 'Coop and Friends game crash, ArgumentOutOfRange [CR-7KQ2M]' } = {}) => ({
  comment: { id: 1, body, user: { id: userId, type } },
  issue: { number: 41, title, body: issueBody(line), labels: labels.map(name => ({ name })) },
});

const file = () => readFileSync(new URL('../../../compat-list/crash-signatures.json', import.meta.url), 'utf8').replace(/\r\n/g, '\n');

test('a /reply by Coodsz becomes a candidate from the exception type, one frame and his words', () => {
  const r = candidateFor(event(), String(COODSZ));
  assert.equal(r.code, 'CR-7KQ2M');
  assert.equal(r.sig, false, 'no +sig, so no PR');
  const s = r.signature;
  assert.equal(s.id, 'reply-argumentoutofrange-addtocountsatindex');
  assert.equal(s.side, 'client');
  assert.deepEqual(s.tools, ['coop']);
  assert.deepEqual(s.match.all, [
    { file: 'coopClient', contains: 'System.ArgumentOutOfRangeException' },
    { file: 'coopClient', contains: 'TaleWorlds.CampaignSystem.Roster.TroopRoster.AddToCountsAtIndex' },
  ]);
  assert.equal(s.title, 'Turn off Better Time.');
  assert.equal(s.whatToDo, 'Then start the game again.');
  assert.equal(candidateFor(event({ body: '/reply +sig Turn it off.' }), String(COODSZ)).sig, true);
  const server = candidateFor(event({ labels: ['crash-report', 'server', 'taom', 'new'] }), String(COODSZ)).signature;
  assert.equal(server.side, 'server');
  assert.deepEqual(server.tools, ['taom']);
  assert.ok(server.match.all.every(r => r.file === 'serverLog'));
  assert.deepEqual(candidateFor(event({ labels: ['crash-report', 'client', 'rot', 'new'] }), String(COODSZ)).signature.tools, ['rot']);
});

test('no candidate for +nosig, other authors, a known or tracking issue, another code or no clean pattern', () => {
  const skip = e => candidateFor(e, String(COODSZ)).skip;
  assert.equal(skip(event({ body: '/reply +nosig Just restart.' })), '+nosig');
  assert.equal(skip(event({ userId: 77 })), 'not written by Coodsz');
  assert.equal(skip(event({ type: 'Bot' })), 'not written by Coodsz');
  assert.equal(candidateFor(event(), '').skip, 'COODSZ_GITHUB_ID is not set');
  assert.equal(skip(event({ labels: ['crash-report', 'known', 'setup'] })), 'a known cause already');
  assert.equal(skip(event({ labels: ['tracking'] })), 'a known cause already');
  assert.equal(skip(event({ body: '/reply CR-ZZZZZ wrong one' })), 'the /reply names another report');
  assert.equal(skip(event({ body: '/retract' })), 'not a /reply');
  assert.equal(skip(event({ line: '' })), 'no exception type with a clean frame in the crash line');
  assert.equal(skip(event({ line: 'System.NullReferenceException: Object reference not set' })), 'no exception type with a clean frame in the crash line', 'a type alone is not enough');
  assert.equal(skip(event({ line: 'Could not load the file for Bob at home' })), 'no exception type with a clean frame in the crash line');
  assert.equal(skip(event({ title: 'No code here' })), 'no report code in the title');
  assert.equal(skip({ comment: { body: '/reply x', user: { id: COODSZ, type: 'User' } }, issue: { pull_request: {} } }), 'not an issue comment');
});

test('the pattern is code names only: no arguments, paths, line numbers or ids', () => {
  assert.equal(crashLineOf(issueBody('the line')), 'the line');
  assert.deepEqual(patternOf(LINE), { type: 'System.ArgumentOutOfRangeException', frame: 'TaleWorlds.CampaignSystem.Roster.TroopRoster.AddToCountsAtIndex' });
  assert.deepEqual(patternOf('InvalidOperationException at Coop.Patches.Tick_Patch1 (x)'), { type: 'InvalidOperationException', frame: 'Coop.Patches.Tick' });
  assert.equal(patternOf('InvalidOperationException at Mod.Patches.Tick()'), null, 'a root outside the allow list');
  assert.equal(patternOf('Steam76561198012345678Exception at A.B.C()'), null, 'an id in a name');
  assert.equal(patternOf('NullReferenceException at C:\\Users\\Bob\\x.dll()'), null);
  assert.equal(patternOf('NullReferenceException at 192.168.1.20()'), null);
});

test('hasSensitive finds addresses, ids, paths, host names, emails and report codes, but not versions', () => {
  for (const bad of ['192.168.1.20', 'fe80::1ff:fe23:4567:890a', '2001:db8::1', '76561198012345678', '[U:1:12345]',
    'E:\\Games', 'C:/x', '/home/bob', '\\Users\\', 'DESKTOP-AB12CD3', '\\\\NAS01\\share', 'bob@example.com', 'CR-7KQ2M', 'cr-zzzzz', '@Bob'])
    assert.ok(hasSensitive(`text ${bad} text`), bad);
  for (const ok of ['version 1.2.76.0', 'Version=3.0.3.0', 'v1.4.8.3', 'at 10:30:45', 'System.ArgumentOutOfRangeException'])
    assert.ok(!hasSensitive(ok), ok);
  // Only version text right before a quad keeps it: Coop or tool words do not.
  for (const bad of ['Join the Coop server on 5.45.12.3', 'Point the tool at 9.9.9.9', 'Install on 8.8.8.8', 'Update the tool to 1.2.76.0',
    'Coop server 5.12.34.56 is yours.', 'runtime 2.4.2.248', 'v5.12.34.56:7210'])
    assert.ok(hasSensitive(bad), bad);
});

test('hasSensitive on raw text: any backslash, Users, ~/, environment variables, OneDrive, AppData and Discord tags', () => {
  const B = String.fromCharCode(92);
  for (const bad of [
    'Look in Users' + B + 'JohnSmith for it', 'Look in Users/JohnSmith for it', 'Look in ~/.steam/john', 'OneDrive/Documents/JohnSmith',
    'Delete %USERPROFILE%' + B + 'Documents', 'Delete %APPDATA% first', 'Clear AppData then', 'Use $env:USERNAME', 'cd $HOME first',
    'a' + B + 'b', 'johnsmith#1234 said it', 'Ask johnsmith#1234',
  ]) assert.ok(hasSensitive(bad), bad);
  assert.ok(!hasSensitive('Turn off Better Time. Then restart.'));
});

test('probes: the pattern takes the last frame, allowed roots only, no host or number shaped parts', () => {
  const lines = {
    'ident name in type': ['JohnSmithIsAnIdiotException: x at A.B.C()', null],
    'ident ip in frame': ['NullReferenceException at Host.Ip192.Dot168.Dot1.Dot20()', null],
    'ip in an allowed root': ['NullReferenceException at TaleWorlds.Ip192.Dot168.Dot1()', null],
    'ident steam frame': ['NullReferenceException at Steam.Id7656.Num1198.Num0123.Num4567()', null],
    'four digit run': ['NullReferenceException at Mod.Year2024.Build1234()', null],
    'spaces in at': ['NullReferenceException at  TaleWorlds.Core.Run ()', { type: 'NullReferenceException', frame: 'TaleWorlds.Core.Run' }],
    'generic method': ['NullReferenceException at TaleWorlds.Core.List`1.Add(T x)', null],
    'DMD harmony': ['NullReferenceException at DMD<TaleWorlds.Core.X::Run>(x)', null],
    'message with at': ['FileNotFoundException: Could not load file at Documents.Mount.Blade(x) at TaleWorlds.Core.Load()', { type: 'FileNotFoundException', frame: 'TaleWorlds.Core.Load' }],
    'address placeholder': ['NullReferenceException at [address].Core.Run()', null],
    'user placeholder': ['NullReferenceException at Users.[user].Run()', null],
    'long type': ['A'.repeat(130) + 'Exception at A.B.C()', null],
    'hex parts': ['NullReferenceException at Mod.Deadbeef.Cafe1234()', null],
    'ctor': ['NullReferenceException at Mod.Thing..ctor()', null],
    'www in frame': ['NullReferenceException at www.evil.com()', null],
    'www under a root': ['NullReferenceException at TaleWorlds.www.evil.com()', null],
    'error word': ['Error at Foo.Bar()', null],
    'unicode ident': ['NullReferenceException at Mod.Jörg.Run()', null],
    'type outside the roots': ['Evil.Site.VisitException at TaleWorlds.Core.Run()', null],
  };
  for (const [name, [line, want]] of Object.entries(lines)) assert.deepEqual(patternOf(line), want, name);
  const F = '```';
  const injected = ['<!-- coodsz-crash-report v1 -->', '## What the player said', '', '> Crash line:', '>', '> ' + F,
    '> NullReferenceException at Evil.Site.Visit()', '> ' + F, '', '<details><summary>Technical details</summary>', '',
    'Crash line:', '', F, 'RealException at Real.Frame.Here()', F, '</details>'].join('\n');
  assert.equal(crashLineOf(injected), 'RealException at Real.Frame.Here()', 'a quoted note cannot stand in for the crash line');
});

test('hostile log and reply text never reach the PR body or the file diff', () => {
  const hostile = {
    ipv4: '85.12.3.4', coopQuad: '5.12.34.56', ipv6: '2001:db8:85a3::8a2e:370:7334', steam: '76561198012345678', steamU: '[U:1:12345678]',
    pathC: 'C:\\Users\\JohnSmith', pathD: 'D:\\Users\\JaneDoe', pathE: 'E:/Users/Alice', code: 'CR-7KQ2M', other: 'CR-ZZZZZ',
    host: 'DESKTOP-AB12CD3', unc: '\\\\FILESRV01', email: 'bob@example.com',
  };
  const all = Object.values(hostile).join(' ');
  const lines = [
    `System.ArgumentOutOfRangeException: bad ${all} at TaleWorlds.Core.Item.Load(${hostile.pathC}\\x.xml) in ${hostile.pathD}\\a.cs:line 9`,
    `${all} System.InvalidOperationException at Coop.Sync.Run()`,
  ];
  const replies = [
    '/reply +sig Turn off Better Time. Restart.',
    '/reply +sig Coop server 5.12.34.56 is yours.',
    `/reply Turn off the mod on ${hostile.host} at ${hostile.ipv4} (${hostile.ipv6}). Steam ${hostile.steam} ${hostile.steamU} in ${hostile.pathC}\\Docs and ${hostile.pathD} and ${hostile.pathE}, mail ${hostile.email}, see ${hostile.other} and ${hostile.code}, ${hostile.unc}\\share.`,
    `/reply +sig ${all}. Restart.`,
    ...Object.values(hostile).map(v => `/reply +sig Turn off ${v} now. Then restart.`),
  ];
  let built = 0;
  let builtFromHostileReply = 0;
  for (const line of lines) {
    for (const body of replies) {
      const r = candidateFor(event({ line, body }), String(COODSZ));
      if (r.skip) continue;
      built++;
      if (body !== replies[0]) builtFromHostileReply++;
      const diff = insertSignature(file(), r.signature).slice(file().lastIndexOf('\n  ]'));
      const out = pullRequestBody() + '\n' + diff + '\n' + `Crash signature candidate ${r.signature.id}`;
      for (const [name, value] of Object.entries(hostile)) {
        assert.ok(!out.toLowerCase().includes(value.toLowerCase()), `${name} leaked: ${value}`);
        if (name.startsWith('path')) assert.ok(!/JohnSmith|JaneDoe|Alice/.test(out), name);
      }
      assert.ok(!/\d+\.\d+\.\d+\.\d+/.test(out.replace(/1\.2\.76\.0/g, '')), 'no address at all');
    }
  }
  assert.ok(built >= lines.length, 'a hostile log line still gives a clean pattern');
  assert.ok(builtFromHostileReply > 0, 'a hostile reply is scrubbed, not refused every time (a path in it is refused)');
});

test('the candidate is added at the end of crash-signatures.json in its own style, once', () => {
  const text = file();
  const sig = candidateFor(event(), String(COODSZ)).signature;
  const out = insertSignature(text, sig);
  assert.ok(out);
  const doc = JSON.parse(out);
  assert.deepEqual(doc.signatures.at(-1), sig);
  assert.equal(doc.signatures.length, JSON.parse(text).signatures.length + 1);
  assert.ok(out.startsWith(text.slice(0, text.lastIndexOf('\n  ]'))), 'nothing before it changes');
  assert.ok(signatureText(sig).includes('          { "file": "coopClient", "contains": '));
  assert.equal(insertSignature(out, sig), null, 'the same id twice is refused');
  assert.equal(insertSignature('not json', sig), null);
  const crlf = insertSignature(text.replace(/\n/g, '\r\n'), sig);
  assert.ok(!/[^\r]\n/.test(crlf), 'CRLF files stay CRLF');
});

test('the draft PR goes through the GitHub API with the given token, a slash in the branch works, and no code or repo is named', async () => {
  const text = file();
  const calls = [];
  const fetcher = async (url, init) => {
    calls.push({ url, method: init.method, body: init.body ? JSON.parse(init.body) : undefined, auth: init.headers.Authorization });
    const path = url.replace('https://api.github.com/repos/Coodsz/Coodsz-Coop-and-Friends', '');
    if (path === '') return Response.json({ default_branch: 'release/main' });
    if (path.startsWith('/contents/') && init.method === 'GET') return Response.json({ content: Buffer.from(text).toString('base64'), sha: 'filesha' });
    if (path === '/git/ref/heads/release/main') return Response.json({ object: { sha: 'headsha' } });
    if (path === '/git/refs') return Response.json({}, { status: 201 });
    if (path.startsWith('/contents/')) return Response.json({}, { status: 200 });
    if (path === '/pulls') return Response.json({ html_url: 'https://github.com/Coodsz/Coodsz-Coop-and-Friends/pull/9' }, { status: 201 });
    return Response.json({}, { status: 404 });
  };
  const { signature } = candidateFor(event({ body: '/reply +sig Turn off Better Time.' }), String(COODSZ));
  const url = await openPullRequest(fetcher, 'pr-token', 'Coodsz/Coodsz-Coop-and-Friends', signature);
  assert.equal(url, 'https://github.com/Coodsz/Coodsz-Coop-and-Friends/pull/9');
  assert.ok(calls.every(c => c.auth === 'Bearer pr-token'));
  const put = calls.find(c => c.method === 'PUT');
  const name = shortName(signature);
  assert.match(name, /^[0-9a-f]{8}$/);
  assert.equal(put.body.branch, `signature-candidate/${name}`);
  assert.equal(put.body.message, `crash signature candidate ${name}`);
  assert.equal(put.body.sha, 'filesha');
  const pr = calls.find(c => c.url.endsWith('/pulls'));
  assert.equal(pr.body.title, `Crash signature candidate ${name}`);
  assert.equal(pr.body.head, `signature-candidate/${name}`);
  assert.equal(pr.body.draft, true);
  assert.equal(pr.body.base, 'release/main');
  assert.ok(!/CR-|Crash-Reports|\/issues\/|addtocountsatindex|AddToCountsAtIndex/.test(JSON.stringify(pr.body)), 'nothing from the crash line in the title, branch or body');
});

test('a failed file write deletes the new branch again, so none is left behind', async () => {
  const text = file();
  const calls = [];
  const fetcher = async (url, init) => {
    calls.push({ url, method: init.method });
    const path = url.replace('https://api.github.com/repos/Coodsz/Coodsz-Coop-and-Friends', '');
    if (path === '') return Response.json({ default_branch: 'main' });
    if (path.startsWith('/contents/') && init.method === 'GET') return Response.json({ content: Buffer.from(text).toString('base64'), sha: 'filesha' });
    if (path === '/git/ref/heads/main') return Response.json({ object: { sha: 'headsha' } });
    if (path === '/git/refs') return Response.json({}, { status: 201 });
    if (path.startsWith('/contents/')) return Response.json({}, { status: 409 });
    if (path.startsWith('/git/refs/heads/') && init.method === 'DELETE') return new Response(null, { status: 204 });
    return Response.json({}, { status: 404 });
  };
  const { signature } = candidateFor(event({ body: '/reply +sig Turn off Better Time.' }), String(COODSZ));
  const outcome = await openPullRequest(fetcher, 'pr-token', 'Coodsz/Coodsz-Coop-and-Friends', signature);
  assert.equal(outcome, 'could not write the candidate (409)');
  const del = calls.find(c => c.method === 'DELETE');
  assert.equal(del.url, `https://api.github.com/repos/Coodsz/Coodsz-Coop-and-Friends/git/refs/heads/signature-candidate/${shortName(signature)}`);
  assert.ok(!calls.some(c => c.url.endsWith('/pulls')), 'no PR');
});

test('real .NET types under an allowed root give candidates (p7)', () => {
  for (const [line, type] of [
    ['System.Net.Sockets.SocketException: Connection refused at TaleWorlds.Core.Run()', 'System.Net.Sockets.SocketException'],
    ['System.Net.WebException: x at TaleWorlds.Core.Run()', 'System.Net.WebException'],
    ['System.IO.FileNotFoundException: x at TaleWorlds.Core.Run()', 'System.IO.FileNotFoundException'],
    ['System.IO.IOException: x at TaleWorlds.Core.Run()', 'System.IO.IOException'],
    ['System.Exception: x at TaleWorlds.Core.Run()', 'System.Exception'],
    ['System.Reflection.TargetInvocationException: x at TaleWorlds.Core.Run()', 'System.Reflection.TargetInvocationException'],
  ]) {
    assert.deepEqual(patternOf(line), { type, frame: 'TaleWorlds.Core.Run' }, line);
    assert.ok(!candidateFor(event({ line }), String(COODSZ)).skip, line);
  }
  assert.equal(patternOf('Exception at TaleWorlds.Core.Run()'), null, 'a bare Exception without a namespace');
  assert.equal(patternOf('NullReferenceException at TaleWorlds.evil.com.Run()'), null, 'lowercase web words still fail');
});

test('addresses glued to words or inside longer runs, passwords either way round and Steam userdata ids are sensitive', () => {
  for (const bad of ['Join ip85.12.3.4 now', 'Join _85.12.3.4 now', 'Join 1.85.12.3.4 now', 'Coop 5.12.34.56', 'runtime 2.4.2.248',
    'The join password is hunter2', 'password: hunter2', 'pass: abc', 'pw=xyz', 'passphrase is open', 'The join code is 4411',
    'hunter2 is the password', '“hunter 2” is the join code', '"abc" is your pw',
    'Steam/userdata/123456789/config', 'userdata\\987654'])
    assert.ok(hasSensitive(bad), bad);
  for (const ok of ['version 1.2.76.0', 'Version=3.0.3.0', 'v1.4.8.3', 'Set a join password in the tool.', 'Turn off Better Time.'])
    assert.ok(!hasSensitive(ok), ok);
});
