// In tool replies: reading Coodsz's answer comments and cleaning their text for the player's tool
// (docs/design/in-tool-replies.md). No imports and no Workers APIs, so the crash repositories'
// signature Action uses the same file: crash-repos/.github/scripts/reply-text.mjs is a byte for
// byte copy (a test checks that), copy it there again after a change here.
//
// An answer comment is one of these, written by Coodsz:
//   /reply [CR-XXXXX] [+logs] [+sig] <text>     his answer, +logs adds a "Send my logs again" button,
//                                               +sig asks the crash repository Action for a signature PR
//   /retract [CR-XXXXX]                         withdraws the newest answer
//   <!-- coodsz-answer CR-XXXXX [auto] -->      the hidden marker, on its own line at the top. A /reply
//                                               counts as if it had one; phase 2's automatic answers carry it
// On a tracking issue the code is needed, on the report's own issue it may be left out.

export const ANSWER_CODE = /^CR-[23456789ABCDEFGHJKMNPQRSTUVWXYZ]{5}$/;
export const MAX_REPLY_TEXT = 1500;
export const RETRACTED_TEXT = 'That answer was withdrawn. Coodsz is looking into it.';
export const RETRACTED_AUTO_TEXT = 'That automatic answer was withdrawn. Coodsz is looking into it.';

const MARKER = /^<!--\s*coodsz-answer\s+(CR-[0-9A-Za-z]{5})(\s+auto)?\s*-->[ \t]*(?:\n|$)/i;
const REPLY = /^\/reply(?=\s|$)((?:[ \t]+(?:CR-[0-9A-Za-z]{5}|\+[A-Za-z]+)(?=\s|$))*)[ \t]*(.*)$/i;
const OTHER_MARKER = /^<!--(?:(?!-->)[^\n]){0,200}-->[ \t]*(?:\n|$)/;
const RETRACT = /^\/retract(?=\s|$)(?:[ \t]+(CR-[0-9A-Za-z]{5}))?[ \t]*$/i;

/**
 * What a comment body says, or null when it is no answer comment: { kind: 'reply' | 'retract',
 * code (null when none was named), logs, nosig, auto, text (raw, cleaned later) }. Only the first
 * line counts as the command, so a /reply quoted further down is just text.
 */
export function parseAnswer(body) {
  if (typeof body !== 'string') return null;
  let text = body.replace(/\r\n?/g, '\n').replace(/^\s+/, '');
  // Other hidden markers on top, like <!-- report-replies --> from the Report Replies drafts, are skipped,
  // so the /reply line under them still counts. Only the coodsz-answer marker means something here.
  while (OTHER_MARKER.test(text) && !MARKER.test(text)) text = text.replace(OTHER_MARKER, '').replace(/^\s+/, '');
  let code = null;
  let auto = false;
  let marked = false;
  const marker = MARKER.exec(text);
  if (marker) {
    code = marker[1].toUpperCase();
    auto = !!marker[2];
    marked = true;
    text = text.slice(marker[0].length).replace(/^\s+/, '');
  }
  const newline = text.indexOf('\n');
  const first = newline < 0 ? text : text.slice(0, newline);
  const rest = newline < 0 ? '' : text.slice(newline + 1);

  const retract = RETRACT.exec(first);
  if (retract) {
    const named = retract[1]?.toUpperCase() ?? null;
    if (named && code && named !== code) return null;
    return { kind: 'retract', code: named ?? code, logs: false, sig: false, nosig: false, auto, text: '' };
  }
  const reply = REPLY.exec(first);
  if (reply) {
    let named = null;
    const flags = new Set();
    for (const token of reply[1].trim().split(/[ \t]+/).filter(Boolean)) {
      if (token.startsWith('+')) flags.add(token.slice(1).toLowerCase());
      else if (named === null) named = token.toUpperCase();
      else return null; // two codes: not clear which report it is for
    }
    if (named && code && named !== code) return null;
    return {
      kind: 'reply', code: named ?? code, logs: flags.has('logs'), sig: flags.has('sig'), nosig: flags.has('nosig'), auto,
      text: [reply[2], rest].filter(s => s.length > 0).join('\n'),
    };
  }
  // A marker without a command is an answer as it stands (phase 2 posts these).
  return marked ? { kind: 'reply', code, logs: false, sig: false, nosig: true, auto, text } : null;
}

/** True when the answer is for this report: the code must match when named, and is required on a tracking issue. */
export function answerFor(answer, code, tracked) {
  if (!answer) return false;
  if (answer.code === null) return !tracked;
  return answer.code === code;
}

// Links that may stay in a reply: the Coop Discord invite and the public Coodsz tool pages.
// Every other link is taken out. The tool shows the text plain anyway, nothing is clickable.
const ALLOWED_LINKS = [
  /^https?:\/\/discord\.gg\/[\w-]+\/?$/i,
  /^https?:\/\/(?:www\.)?discord\.com\/invite\/[\w-]+\/?$/i,
  /^https?:\/\/(?:www\.)?discord\.com\/channels\/709516043332354119(?:\/\d+)*\/?$/i,
  /^https?:\/\/github\.com\/Coodsz\/Coodsz-(?:Coop-and-Friends|TAOM|AD1259|BurningEmpires|RealmOfThrones)\/releases(?:\/[\w./-]*)?$/i,
  /^https?:\/\/steamcommunity\.com\/sharedfiles\/filedetails\/\?id=\d+$/i,
];

// Like ProblemReport.Redact in the tools (crash.js uses these too): a dotted quad stays when a part is
// over 255 or version text comes right before it (Version=3.0.3.0, runtime 2.4.2.248), unless a :port follows.
export const IPV4 = /\b(?<!\d\.)(\d{1,3})\.(\d{1,3})\.(\d{1,3})\.(\d{1,3})\b(?!\.\d)/g;
const VERSION_BEFORE = /(?:\b(?:file|product|assembly)?version|\bruntime|\bbuild|\bv)[ \t]*[=:]?[ \t]*$/i;
export function isVersionQuad(all, at, quad, parts) {
  if (/^:\d/.test(all.slice(at + quad.length, at + quad.length + 2))) return false;
  if (parts.some(part => Number(part) > 255)) return true;
  return VERSION_BEFORE.test(all.slice(Math.max(0, at - 40), at));
}
// In an answer Coodsz also writes "update the tool to 1.2.76.0": a quad that starts with one digit and
// has update words shortly before it stays too. Addresses rarely start with one digit.
const REPLY_VERSION_BEFORE = /\b(?:version|update[sd]?|updating|upgrade[sd]?|tool|launcher|release|coop|bannerlord|install(?:ed)?)\b[^.\n]{0,24}$/i;
export const isReplyVersion = (all, at, quad, parts) => isVersionQuad(all, at, quad, parts)
  || (/^\d\./.test(quad) && !/^:\d/.test(all.slice(at + quad.length, at + quad.length + 2))
    && REPLY_VERSION_BEFORE.test(all.slice(Math.max(0, at - 40), at)));
// Windows host names the way Windows makes them, and \\host\share paths.
export const HOST_NAME = /\b(?:DESKTOP|LAPTOP|WIN|PC)-[A-Z0-9]{4,15}\b|\\\\[\w.-]+/gi;
// The full form, or a short form with :: in it, so a time like 10:30:45 stays.
const IPV6 = /\b(?:[0-9a-f]{1,4}:){7}[0-9a-f]{1,4}\b|\b(?:[0-9a-f]{1,4}:){1,6}:(?:[0-9a-f]{1,4}(?::[0-9a-f]{1,4}){0,5})?(?![\w:])/gi;

/**
 * The text of an answer as the player's tool may show it: plain text, at most MAX_REPLY_TEXT
 * characters. Markdown and HTML are taken down to their words, every link except the allow list
 * goes, and Steam ids, addresses, Windows user paths, passwords and tokens, emails, @mentions,
 * issue refs and other report codes are taken out. code is this report's own code, which may stay.
 */
export function cleanReply(text, code = null) {
  if (typeof text !== 'string') return '';
  let t = text.replace(/\r\n?/g, '\n').replace(/\p{Cf}+/gu, '');
  t = t.replace(/<!--[\s\S]*?(?:-->|$)/g, '');
  t = t.replace(/^[ \t]*(`{3,}|~{3,})[\s\S]*?(?:^[ \t]*\1[ \t]*$|(?![\s\S]))/gm, '');
  t = t.replace(/<\/?[A-Za-z][^>]*>/g, '');
  t = t.replace(/!\[[^\]\n]*\]\([^)\n]*\)/g, '');
  t = t.replace(/\[([^\]\n]*)\]\(([^)\n]*)\)/g, (all, words, url) => (isAllowedLink(url.trim()) ? `${words} (${url.trim()})` : words));
  t = t.replace(/^\s*\[[^\]\n]+\]:\s*\S+.*$/gm, '');
  t = t.replace(/\b(?:https?:\/\/|www\.)[^\s<>()"']+/gi, url => {
    const end = /[.,;:!?]*$/.exec(url)[0];
    const bare = url.slice(0, url.length - end.length);
    return (isAllowedLink(bare) ? bare : '') + end;
  });
  t = t.replace(/\b7656\d{10,}\b/g, '').replace(/\[U:\d:\d+\]/gi, '').replace(/\bSTEAM_\d:\d:\d+\b/gi, '');
  t = t.replace(/\b(?:gh[pousr]_[A-Za-z0-9]{20,}|github_pat_[A-Za-z0-9_]{20,})\b/g, '');
  t = t.replace(/userdata[\\/]+\d+/gi, 'userdata');
  // Passwords and join codes, "password: x", "password is x" and "x is the password", in straight or curly quotes.
  const SECRET_WORD = '(?:pass(?:word|phrase|wd)?|pass word|pwd|pw|join\\s*code|token|secret|api[_ -]?key)';
  const QUOTED = '"[^"\\n]*"|\'[^\'\\n]*\'|\u201c[^\u201d\\n]*\u201d|\u2018[^\u2019\\n]*\u2019';
  t = t.replace(new RegExp(`(\\b${SECRET_WORD}\\w*\\s*[:=]\\s*)(?:${QUOTED}|[^\\s,;]+)`, 'gi'), '$1');
  t = t.replace(new RegExp(`(\\b${SECRET_WORD}\\s+(?:is|was|will be)\\s+)(?:${QUOTED}|[^\\s,.;!?]+)`, 'gi'), '$1');
  t = t.replace(new RegExp(`(?:${QUOTED}|[^\\s,.;!?"'\u201c\u2018]+)(\\s+(?:is|was)\\s+(?:the|your|our|my)\\s+(?:join\\s+)?(?:password|passphrase|pass|pw|join\\s*code)\\b)`, 'gi'), '$1');
  // Legacy Discord tags (name#1234), before issue refs are taken out.
  t = t.replace(/(^|[^\w#])[\w.]{2,32}#\d{4}\b/gm, '$1');
  t = t.replace(/[\w.+-]+@[\w-]+(?:\.[\w-]+)+/g, '');
  t = t.replace(IPV4, (quad, a, b, c, d, at, all) => (isReplyVersion(all, at, quad, [a, b, c, d]) ? quad : '')).replace(IPV6, '');
  t = t.replace(HOST_NAME, '');
  t = t.replace(/\b[A-Za-z]:[\\/]+Users[\\/]+[^\\/\s"']+/gi, 'C:\\Users\\[user]').replace(/(?<![A-Za-z:])([\\/])Users[\\/]+[^\\/\s"']+/g, '$1Users$1[user]');
  t = t.replace(/(^|[^\w`])@[A-Za-z0-9](?:[A-Za-z0-9-]{0,38})(?:\/[\w.-]+)?/g, '$1');
  t = t.replace(/\b[\w.-]+\/[\w.-]+#\d+\b/g, '').replace(/(^|[^\w&])#\d+\b/g, '$1').replace(/\bGH-\d+\b/gi, '');
  t = t.replace(/\bCR-[0-9A-Z]{5}\b/gi, c => (code && c.toUpperCase() === code ? c.toUpperCase() : ''));
  t = t.replace(/`+/g, '').replace(/(\*\*|__|~~)(?=\S)([\s\S]*?\S)\1/g, '$2');
  t = t.replace(/^[ \t]*#{1,6}[ \t]+/gm, '').replace(/^[ \t]*>[ \t]?/gm, '');
  t = t.replace(/\t/g, ' ').replace(/[\u0000-\u0009\u000b-\u001f\u007f-\u009f]/g, '');
  t = t.split('\n').map(line => line.replace(/ {2,}/g, ' ').replace(/ +([.,;:!?])/g, '$1').trim()).join('\n');
  t = t.replace(/\n{3,}/g, '\n\n').trim();
  return t.length > MAX_REPLY_TEXT ? t.slice(0, MAX_REPLY_TEXT - 3).toWellFormed().trimEnd() + '...' : t;
}

function isAllowedLink(url) {
  return ALLOWED_LINKS.some(re => re.test(url));
}
