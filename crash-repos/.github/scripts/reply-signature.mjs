// Turns Coodsz's /reply on a crash report into a crash signature candidate
// (docs/design/in-tool-replies.md, Build order item 5). No AI: the pattern comes from the report's
// crash line, the cause and the fix from his /reply text. "/reply ... +nosig" skips it.
//
// Runs from .github/workflows/reply-signature.yml in each private crash repository, on a new comment.
// It reads only the event (the comment, the issue title, body and labels); no GitHub call is needed
// to build the candidate.
//
// No free text from the player's log goes into a candidate. The pattern is built only from an allow
// list of shapes: the .NET exception type (System.ArgumentOutOfRangeException) plus one stable frame
// (Namespace.Type.Method, no arguments, paths or line numbers) or a phrase from KNOWN_PHRASES. When the
// crash line has neither cleanly, there is no candidate. The rest is Coodsz's cleaned reply, and a last
// check refuses any candidate that still holds an address, an id, a path, a host name or a report code.
//
// By default the candidate goes into the job summary, which only people with access to the crash
// repository can read. A draft PR to the public repository that holds crash-signatures.json
// (SIGNATURE_REPO, default Coodsz/Coodsz-Coop-and-Friends) is opened only when all three hold: the
// reply says +sig, the repository variable SIGNATURE_PRS is on, and the secret SIGNATURE_PR_TOKEN is set
// (Contents and Pull requests read and write on that repository only). SIGNATURE_PRS stays off until a
// security review has passed this step.
//
// Usage: node reply-signature.mjs   (GITHUB_EVENT_PATH, COODSZ_GITHUB_ID, GITHUB_STEP_SUMMARY and the
// optional SIGNATURE_PRS, SIGNATURE_REPO and SIGNATURE_PR_TOKEN). No dependencies; Node 20+.

import fs from 'node:fs';
import { fileURLToPath } from 'node:url';
import { createHash } from 'node:crypto';
import { parseAnswer, answerFor, cleanReply, HOST_NAME } from './reply-text.mjs';

export const SIGNATURES_PATH = 'compat-list/crash-signatures.json';
export const DEFAULT_SIGNATURE_REPO = 'Coodsz/Coodsz-Coop-and-Friends';
// Stable log phrases a candidate may use in place of a frame. Add one only after checking it holds no player data.
export const KNOWN_PHRASES = [];
const TOOLS = ['coop', 'taom', 'ad1259', 'be', 'rot'];
// The one log file a candidate looks in, per side (the file kinds crash-signatures.json knows).
const FILE = { client: 'coopClient', server: 'serverLog' };
// A code name: starts with a letter or _, at most 60 characters, at most 2 digits, and not a web name.
// Web names are lowercase only, so namespaces like System.Net and System.IO stay.
const NAME = /^[A-Za-z_][A-Za-z0-9_]{0,59}$/;
const WEB = /^(?:www|com|net|org|io|gg|dev|http|https)$/;
const okName = part => NAME.test(part) && (part.match(/\d/g) ?? []).length <= 2 && !WEB.test(part);
// The only roots a pattern may start with: the game, .NET, Coop and Harmony.
export const ALLOWED_ROOTS = ['System', 'TaleWorlds', 'SandBox', 'StoryMode', 'Coop', 'GameInterface', 'HarmonyLib', 'Bannerlord', 'Mono'];
// .NET exceptions often logged without their namespace.
const BARE_EXCEPTIONS = new Set(['NullReference', 'ArgumentOutOfRange', 'Argument', 'ArgumentNull', 'IndexOutOfRange', 'InvalidOperation',
  'InvalidCast', 'KeyNotFound', 'MissingMethod', 'MissingField', 'TypeLoad', 'FileNotFound', 'DirectoryNotFound', 'FileLoad',
  'StackOverflow', 'OutOfMemory', 'AccessViolation', 'NotSupported', 'NotImplemented', 'ObjectDisposed', 'DivideByZero',
  'Format', 'Overflow', 'TargetInvocation', 'TypeInitialization'].map(n => n + 'Exception'));

const labelsOf = issue => (issue?.labels ?? []).map(l => (typeof l === 'string' ? l : l?.name ?? '').toLowerCase());

/** The crash line from the issue's Technical details, or '' when the report has none. */
export function crashLineOf(body) {
  const m = /(?:^|\n)Crash line:\r?\n\r?\n```\r?\n([^\r\n]+)\r?\n```/.exec(body ?? '');
  return m ? m[1].trim() : '';
}

/**
 * The pattern parts from a crash line: { type, frame } (frame may be a known phrase), or null. Only
 * dotted code names under ALLOWED_ROOTS pass: the exception type (with an allowed namespace, or a bare
 * well known .NET exception) and the LAST "at X.Y.Z(" frame on the line, so an "at" in the message text
 * before it cannot choose the frame. Text from the
 * player's PC can only get in when it is shaped like such a code name, so check the pattern in the draft.
 */
export function patternOf(line) {
  const text = String(line ?? '');
  // The last part may be a bare Exception (System.Exception); without a namespace that is not in BARE_EXCEPTIONS.
  const typeMatch = /(?:^|[^\w.])((?:[A-Za-z_]\w*\.)*(?:[A-Za-z_]\w*)?(?:Exception|Error))\b/.exec(text);
  if (!typeMatch) return null;
  const type = typeMatch[1];
  const typeParts = type.split('.');
  if (type.length > 120 || !typeParts.every(okName)) return null;
  if (typeParts.length > 1 ? !ALLOWED_ROOTS.includes(typeParts[0]) : !BARE_EXCEPTIONS.has(type)) return null;
  const frames = [...text.matchAll(/\bat\s+((?:[A-Za-z_]\w*\.)+[A-Za-z_]\w*?)(?:_Patch\d+)?\s*\(/g)];
  let frame = null;
  const last = frames.at(-1)?.[1];
  if (last) {
    const parts = last.split('.');
    if (parts.length >= 2 && last.length <= 160 && parts.every(okName) && ALLOWED_ROOTS.includes(parts[0])) frame = last;
  }
  if (!frame) frame = KNOWN_PHRASES.find(p => text.includes(p)) ?? null;
  return frame ? { type, frame } : null;
}

const SENSITIVE = [
  /7656\d{10,}/, /\[U:\d:\d+\]/i, /STEAM_\d:\d:\d+/i,
  /\\/, /\b[A-Za-z]:[\\/]/, /\bUsers[\\/]/i, /[\\/]home[\\/]/i, /(?:^|\s)~\//, /\bOneDrive[\\/]/i, /\bAppData\b/i,
  /%[A-Za-z_][\w()]*%/, /\$env:/i, /\$HOME\b/,
  /\b(?:[0-9a-f]{1,4}:){7}[0-9a-f]{1,4}\b/i, /\b(?:[0-9a-f]{1,4}:){1,6}:/i,
  /\bCR-[0-9A-Z]{5}\b/i, /[\w.+-]+@[\w-]+\.[\w.-]+/, /@/, /\b[\w.]{2,32}#\d{4}\b/,
  // A quad with a :port is an address whatever comes before it, a "v" too.
  /\d{1,3}(?:\.\d{1,3}){3}:\d/,
  // A Steam userdata folder names the account.
  /userdata[\\/]+\d+/i,
  // A password or join code with its value, either way round.
  /\b(?:pass(?:word|phrase|wd)?|pwd|pw|join\s*code)\s*(?:[:=]|\s(?:is|was|will be)\s)\s*["'\u201c\u2018]?[A-Za-z0-9]/i,
  /["'\u201c\u2018]?[A-Za-z0-9][^\s"'\u201d\u2019]*["'\u201d\u2019]?\s+(?:is|was)\s+(?:the|your|our|my)\s+(?:join\s+)?(?:password|passphrase|pass|pw|join\s*code)\b/i,
];

/**
 * True when the text still holds an address, a Steam id or userdata folder, a path (any backslash, Users,
 * home, ~/, OneDrive, AppData or an environment variable), a host name, an email, a Discord tag, a
 * password or join code with its value, or a report code. Every run of four dotted numbers counts as an
 * address (also glued to a word, ip85.12.3.4, or inside a longer run, 1.85.12.3.4) unless "v" or
 * "version" is right before it: write a version as v1.2.3.4 or "version 1.2.3.4".
 */
export function hasSensitive(text) {
  const t = String(text ?? '');
  if (SENSITIVE.some(re => re.test(t))) return true;
  if (new RegExp(HOST_NAME.source, 'i').test(t)) return true;
  for (const m of t.matchAll(/\d{1,3}(?:\.\d{1,3}){3}/g)) {
    const before = t.slice(Math.max(0, m.index - 12), m.index);
    if (!/(?:(?:^|[^A-Za-z])v|\bversion[ \t]*[=:]?[ \t]*)$/i.test(before)) return true;
  }
  return false;
}

/** A neutral short name for the PR title and branch, so nothing from the crash line is public before Coodsz reads it. */
export function shortName(sig) {
  return createHash('sha256').update(sig.id).digest('hex').slice(0, 8);
}

/** A signature id from the pattern: reply-<error>-<method>, lowercase. */
function idOf(pattern) {
  const short = pattern.type.split('.').at(-1).replace(/(?:Exception|Error)$/, '') || 'error';
  const method = pattern.frame.split('.').at(-1);
  return ('reply-' + `${short}-${method}`.toLowerCase().replace(/[^a-z0-9]+/g, '-')).replace(/-+$/, '').slice(0, 64);
}

/**
 * The candidate for this comment event: { code, sig, signature }, or { skip } that says why there is
 * none. Only a /reply by Coodsz (his numeric id, a User) on a report's own issue that no signature knows yet.
 */
export function candidateFor(event, coodszId) {
  const comment = event?.comment;
  const issue = event?.issue;
  if (!comment || !issue || issue.pull_request) return { skip: 'not an issue comment' };
  const id = Number(String(coodszId ?? '').trim());
  if (!Number.isSafeInteger(id) || id <= 0) return { skip: 'COODSZ_GITHUB_ID is not set' };
  if (comment.user?.id !== id || comment.user?.type !== 'User') return { skip: 'not written by Coodsz' };
  const answer = parseAnswer(comment.body);
  if (!answer || answer.kind !== 'reply') return { skip: 'not a /reply' };
  if (answer.nosig) return { skip: '+nosig' };
  const labels = labelsOf(issue);
  if (labels.includes('tracking') || labels.includes('known')) return { skip: 'a known cause already' };
  const code = /\[(CR-[23456789ABCDEFGHJKMNPQRSTUVWXYZ]{5})\]\s*$/.exec(issue.title ?? '')?.[1];
  if (!code) return { skip: 'no report code in the title' };
  if (!answerFor(answer, code, false)) return { skip: 'the /reply names another report' };
  const pattern = patternOf(crashLineOf(issue.body));
  if (!pattern) return { skip: 'no exception type with a clean frame in the crash line' };
  // No report code at all in the text, not even this one: the PR is public.
  const text = cleanReply(answer.text, null).replace(/\s*\n\s*/g, ' ').trim();
  if (!text) return { skip: 'no reply text' };

  const sentences = text.split(/(?<=[.!?])\s+/);
  const first = sentences[0];
  const side = labels.includes('server') ? 'server' : 'client';
  const tool = TOOLS.find(t => labels.includes(t));
  const signature = {
    id: idOf(pattern),
    side,
    ...(tool ? { tools: [tool] } : {}),
    priority: 50,
    category: 'setup',
    match: { all: [{ file: FILE[side], contains: pattern.type }, { file: FILE[side], contains: pattern.frame }] },
    title: first.length > 80 ? first.slice(0, 77).trimEnd() + '...' : first,
    whatHappened: first,
    whatToDo: sentences.length > 1 ? sentences.slice(1).join(' ') : first,
    reportNeeded: false,
  };
  // The raw fields, not the JSON text (where a quote turns into a backslash).
  const fields = [signature.id, signature.title, signature.whatHappened, signature.whatToDo, ...signature.match.all.map(r => r.contains), pullRequestBody()];
  if (fields.some(hasSensitive)) return { skip: 'the candidate still holds a sensitive value' };
  return { code, sig: answer.sig, signature };
}

/** The signature as crash-signatures.json writes one: 4 spaces in, rules on one line each. */
export function signatureText(sig, eol = '\n') {
  const j = JSON.stringify;
  const lines = ['    {', `      "id": ${j(sig.id)},`, `      "side": ${j(sig.side)},`];
  if (sig.tools) lines.push(`      "tools": [${sig.tools.map(t => j(t)).join(', ')}],`);
  lines.push(
    `      "priority": ${sig.priority},`,
    `      "category": ${j(sig.category)},`,
    '      "match": {',
    '        "all": [',
    sig.match.all.map(r => `          { "file": ${j(r.file)}, "contains": ${j(r.contains)} }`).join(',' + eol),
    '        ]',
    '      },',
    `      "title": ${j(sig.title)},`,
    `      "whatHappened": ${j(sig.whatHappened)},`,
    `      "whatToDo": ${j(sig.whatToDo)},`,
    `      "reportNeeded": ${sig.reportNeeded}`,
    '    }',
  );
  return lines.join(eol);
}

/** The file with the signature added at the end of the list, or null when that id is there already or the file is odd. */
export function insertSignature(fileText, sig) {
  let doc;
  try { doc = JSON.parse(fileText); } catch { return null; }
  if (!Array.isArray(doc.signatures) || doc.signatures.some(s => s.id === sig.id)) return null;
  const eol = fileText.includes('\r\n') ? '\r\n' : '\n';
  const end = fileText.lastIndexOf(eol + '  ]');
  if (end < 0) return null;
  const before = fileText.slice(0, end);
  const out = before + (doc.signatures.length ? ',' : '') + eol + signatureText(sig, eol) + fileText.slice(end);
  try {
    const check = JSON.parse(out);
    return check.signatures.at(-1)?.id === sig.id ? out : null;
  } catch {
    return null;
  }
}

/** The draft PR text. The repository is public: no report code, crash repository, issue or log text. */
export function pullRequestBody() {
  return [
    'A crash signature candidate from one of Coodsz\'s answers to a crash report, built by the crash repository\'s reply-signature Action.',
    '',
    'Check before merging:',
    '- the pattern (an exception type and one frame) matches the crash and nothing else',
    '- the category (setup is only a placeholder), the side, the tools and reportNeeded',
    '- the title and the player text, then add the 9 translations',
    '- bump the file version',
  ].join('\n');
}

/** Opens the draft PR on repo with token. Returns the PR address, or a reason it did not. */
export async function openPullRequest(fetcher, token, repo, sig) {
  const call = async (method, path, body) => {
    const res = await fetcher(`https://api.github.com/repos/${repo}${path}`, {
      method,
      headers: {
        Authorization: `Bearer ${token}`,
        Accept: 'application/vnd.github+json',
        'X-GitHub-Api-Version': '2022-11-28',
        'User-Agent': 'coodsz-reply-signature',
        ...(body === undefined ? {} : { 'content-type': 'application/json' }),
      },
      ...(body === undefined ? {} : { body: JSON.stringify(body) }),
    });
    return { status: res.status, ok: res.ok, data: res.status === 204 ? null : await res.json().catch(() => null) };
  };
  const info = await call('GET', '');
  if (!info.ok) return `could not read ${repo} (${info.status})`;
  const base = info.data.default_branch;
  const file = await call('GET', `/contents/${SIGNATURES_PATH}?ref=${encodeURIComponent(base)}`);
  if (!file.ok || typeof file.data?.content !== 'string') return `could not read ${SIGNATURES_PATH} (${file.status})`;
  const text = Buffer.from(file.data.content, 'base64').toString('utf8');
  const updated = insertSignature(text, sig);
  if (updated === null) return `${sig.id} is in ${SIGNATURES_PATH} already, or the file is odd`;
  // A branch name can hold slashes: each part is encoded on its own.
  const head = await call('GET', `/git/ref/heads/${base.split('/').map(encodeURIComponent).join('/')}`);
  if (!head.ok) return `could not read ${base} (${head.status})`;
  // The title, branch and commit carry only a short hash; the id is inside the file, read in the review.
  const name = shortName(sig);
  const branch = `signature-candidate/${name}`;
  const ref = await call('POST', '/git/refs', { ref: `refs/heads/${branch}`, sha: head.data.object.sha });
  if (!ref.ok) return `could not make the branch ${branch} (${ref.status}), maybe a candidate for this crash is open already`;
  const put = await call('PUT', `/contents/${SIGNATURES_PATH}`, {
    message: `crash signature candidate ${name}`,
    content: Buffer.from(updated, 'utf8').toString('base64'),
    sha: file.data.sha,
    branch,
  });
  if (!put.ok) {
    // No orphan branch: it is deleted again when the file could not be written.
    const removed = await call('DELETE', `/git/refs/heads/${branch.split('/').map(encodeURIComponent).join('/')}`);
    return `could not write the candidate (${put.status})${removed.ok ? '' : `, and the branch ${branch} could not be deleted (${removed.status})`}`;
  }
  const pr = await call('POST', '/pulls', {
    title: `Crash signature candidate ${name}`,
    head: branch, base, body: pullRequestBody(), draft: true,
  });
  if (!pr.ok) return `could not open the PR (${pr.status})`;
  return pr.data.html_url;
}

async function main() {
  const event = JSON.parse(fs.readFileSync(process.env.GITHUB_EVENT_PATH, 'utf8'));
  const result = candidateFor(event, process.env.COODSZ_GITHUB_ID);
  const summary = text => process.env.GITHUB_STEP_SUMMARY && fs.appendFileSync(process.env.GITHUB_STEP_SUMMARY, text + '\n');
  if (result.skip) {
    console.log(`No signature candidate: ${result.skip}.`);
    summary(`### No signature candidate\n\n${result.skip}.`);
    return;
  }
  const json = signatureText(result.signature);
  const token = process.env.SIGNATURE_PR_TOKEN;
  const prsOn = (process.env.SIGNATURE_PRS ?? '').trim().toLowerCase() === 'on' && !!token;
  if (!result.sig || !prsOn) {
    const why = !result.sig ? 'the reply has no +sig' : 'pull requests are off (repository variable SIGNATURE_PRS)';
    console.log(`Signature candidate for ${result.code} is in the job summary (${why}).`);
    summary(`### Signature candidate for ${result.code}\n\nNo PR: ${why}. Add this to ${SIGNATURES_PATH} by hand if it is right:\n\n\`\`\`json\n${json}\n\`\`\``);
    return;
  }
  const repo = (process.env.SIGNATURE_REPO ?? '').trim() || DEFAULT_SIGNATURE_REPO;
  if (!/^[\w.-]+\/[\w.-]+$/.test(repo)) {
    console.log('SIGNATURE_REPO is not owner/name, so no PR.');
    summary('### No signature PR\n\nSIGNATURE_REPO is not owner/name.');
    process.exitCode = 1;
    return;
  }
  const outcome = await openPullRequest(fetch, token, repo, result.signature);
  console.log(`Signature candidate for ${result.code}: ${outcome}`);
  summary(`### Signature candidate for ${result.code}\n\n${outcome}\n\n\`\`\`json\n${json}\n\`\`\``);
}

if (process.argv[1] && fileURLToPath(import.meta.url) === fs.realpathSync(process.argv[1])) {
  main().catch(err => {
    console.log(`The signature candidate failed: ${err?.message ?? err}`);
    process.exitCode = 1;
  });
}
