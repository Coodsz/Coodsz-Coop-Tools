// No, still broken: a player's answer to Coodsz's reply, sent through POST /crash with summary.followUp.
//
// The tool packs it like any report (the cleaned logs from the last run when the box is ticked, the
// player's words as player-notes.txt and at most one screenshot without its metadata, all checked by
// the same code as a normal report) and adds
//   "followUp": {"code":"CR-7KQ2M","key":"<43 chars>","steps":"all"|"some"|"none"}
// The key is the earlier report's reply key, checked like POST /replies (reply-keys.js openReport), so
// only the PC that sent the report can add to it. The zip is kept under a new code as usual, but no new
// issue is filed: the earlier report's issue gets a comment (followUpComment) with the player's words,
// cleaned and quoted like a note, the steps answer and the new code's download line. That issue loses
// the player-solved label and is opened again only when it was closed as completed (Yes, it's fixed
// closes that way too), so It broke again after Yes undoes it; an issue Coodsz closed for any other
// reason (not planned, duplicate, none) stays closed.
// rs:<code> is dropped so a later Yes, it's fixed works again. On a tracking issue only the comment is
// added. When the comment cannot be posted the tool gets a 502 and says to try again (crash.js).
//
// The comment starts with <!-- coodsz-followup CR-XXXXX -->. replies.js reads it: a follow up newer
// than Coodsz's newest answer puts the report back to Looking into it. It never starts with /reply,
// so it is never served as an answer.

import { CODE, defang, quoteNote, playerExtras, NOTES_FILE } from './crash.js';
import { REPLY_KEY } from './reply-keys.js';
import { SOLVED_LABEL } from './replies.js';

export const FOLLOW_UP_STEPS = ['all', 'some', 'none'];
const STEPS_TEXT = { all: 'tried all the steps', some: 'tried some of the steps', none: 'could not try the steps' };
// The marker and the worker's own second line, both: a hand written comment with only the marker does not count.
export const FOLLOW_UP_MARKER = /^<!--\s*coodsz-followup\s+(CR-[0-9A-Z]{5})\s*-->\r?\n\*\*Still broken: the player answered on \1\*\*/;
// What a pack carries besides the logs: the generated files (CrashReports.AddGenerated, and the crash
// dump's stack summary crash-stack.txt) and the player's own extras.
// The note name is written out: crash.js imports this file, so its NOTES_FILE is not set yet when this line runs.
const NOT_LOGS = new Set(['player-notes.txt', 'report.json', 'modules.json', 'environment.json', 'crash-stack.txt']);

/** True when the zip holds logs from the last run, not only the files every pack gets and the player's extras. */
export function hasNewLogs(files) {
  return files.some(f => !NOT_LOGS.has(f.name) && !f.name.startsWith('screenshots/'));
}

/** The checked followUp: { code, key, steps }, null when there is none, or a string that says what is wrong. */
export function validateFollowUp(value) {
  if (value === undefined || value === null) return null;
  if (typeof value !== 'object' || Array.isArray(value)) return 'followUp';
  if (typeof value.code !== 'string' || !CODE.test(value.code)) return 'followUp code';
  if (typeof value.key !== 'string' || !REPLY_KEY.test(value.key)) return 'followUp key';
  if (!FOLLOW_UP_STEPS.includes(value.steps)) return 'followUp steps';
  return { code: value.code, key: value.key, steps: value.steps };
}

const size = n => (n >= 1024 * 1024 ? (n / 1024 / 1024).toFixed(1) + ' MB' : n >= 1024 ? Math.round(n / 1024) + ' KB' : n + ' B');

/** The comment on the earlier report's issue. The player's words are cleaned, escaped and quoted like a note. */
export function followUpComment(earlier, code, summary, zipBytes, origin, expires, steps) {
  const logs = hasNewLogs(summary.files);
  const extras = playerExtras(summary.files.filter(f => f.name !== NOTES_FILE));
  return [
    `<!-- coodsz-followup ${earlier} -->`,
    `**Still broken: the player answered on ${earlier}** (${logs ? 'new logs' : 'only their words,'} ${code})`, '',
    `They ${STEPS_TEXT[steps]}.`, '',
    '**What happens now, in their words:**', '',
    summary.note ? quoteNote(summary.note) : '_no text_', '',
    ...(extras ? [`They added ${extras}, in the zip.`, ''] : []),
    logs ? `New logs from the last run: ${code}, ${size(zipBytes)}, kept until ${expires}.` : `No new logs, only their words (${code}).`, '',
    '```', `curl.exe -H "X-Admin-Key: <ADMIN_KEY>" -o ${code}.zip ${origin}/crash/${code}`, '```', '',
    `Answer with /reply ${earlier} as before.`,
  ].map(line => (line.startsWith('<!--') || line.startsWith('curl') || line === '```' ? line : defang(line))).join('\n');
}

/**
 * Adds the follow up to the earlier report's issue. record is the earlier report's rr: record, opened
 * with its key. True when the comment was posted. Reopening and the label are best effort.
 */
export async function fileFollowUp(env, fetcher, record, { earlier, code, summary, zipBytes, origin, expires, steps }) {
  const token = env.CRASH_GITHUB_TOKEN || env.GITHUB_TOKEN;
  if (!token) return false;
  const gh = (path, method, body) => fetcher(`https://api.github.com/repos/${record.repo}/issues/${record.issue}${path}`, {
    method,
    headers: {
      Authorization: `Bearer ${token}`,
      Accept: 'application/vnd.github+json',
      'X-GitHub-Api-Version': '2022-11-28',
      'User-Agent': 'coodsz-mod-reports-worker',
      'content-type': 'application/json',
    },
    ...(body === undefined ? {} : { body: JSON.stringify(body) }),
  });
  let posted = false;
  try {
    const res = await gh('/comments', 'POST', { body: followUpComment(earlier, code, summary, zipBytes, origin, expires, steps) });
    posted = res.ok;
  } catch (err) {
    console.warn(`the follow up comment failed: ${err?.message ?? err}`);
  }
  if (!posted) return false;
  if (record.tracked !== true) {
    try {
      const res = await gh('', 'GET');
      if (!res.ok) console.warn(`the report could not be read, so player-solved stays and it is not opened again: GitHub answered ${res.status}`);
      const issue = res.ok ? await res.json() : null;
      const labels = Array.isArray(issue?.labels) ? issue.labels.map(l => (typeof l === 'string' ? l : l?.name)) : [];
      const solved = labels.includes(SOLVED_LABEL);
      if (solved) {
        const removed = await gh(`/labels/${encodeURIComponent(SOLVED_LABEL)}`, 'DELETE');
        // A label that is gone already answers 404, which is fine.
        if (!removed.ok && removed.status !== 404) console.warn(`the player-solved label could not be removed: GitHub answered ${removed.status}`);
      }
      // Only a report closed as completed opens again: Coodsz's own fix or Yes, it's fixed, which always
      // closes as completed. Any other reason stays closed, even when Yes put player-solved on it later.
      if (issue?.state === 'closed' && issue.state_reason === 'completed') {
        const opened = await gh('', 'PATCH', { state: 'open' });
        // The comment is on the issue, so the answer is not lost; Coodsz sees it there and in the ping.
        if (!opened.ok) console.warn(`the report could not be opened again: GitHub answered ${opened.status}`);
      }
    } catch (err) {
      console.warn(`the report could not be read, have player-solved removed or be opened again: ${err?.message ?? err}`);
    }
  }
  try {
    if (typeof env.VOTES.delete === 'function') await env.VOTES.delete('rs:' + earlier);
  } catch {
    // A kept mark only means a later That fixed it answers "done before" without closing again.
  }
  return true;
}
