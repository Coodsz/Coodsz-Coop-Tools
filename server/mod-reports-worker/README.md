# Coodsz mod reports worker

The endpoint behind the one click **Works**, **Broken** and **Works only with...** buttons in
Coodsz Coop and Friends, the Coodsz TAOM Client Launcher, the Coodsz AD1259 Client Launcher and the Coodsz
Burning Empires tools. Players report how a mod worked
without a GitHub account; the worker files each report as a GitHub issue in
[Coodsz/Coodsz-Coop-and-Friends](https://github.com/Coodsz/Coodsz-Coop-and-Friends), where the
existing tally workflow counts it into `compat-list/mods.json` once it is approved. TAOM reports
go to [Coodsz/Coodsz-TAOM](https://github.com/Coodsz/Coodsz-TAOM) instead (`TAOM_GITHUB_REPO`),
where the TAOM tally counts them into `compat/taom-mod-compat.json` and `MODS.md`, and AD1259
reports to [Coodsz/Coodsz-AD1259](https://github.com/Coodsz/Coodsz-AD1259) (`AD1259_GITHUB_REPO`),
where the AD1259 tally counts them into `compat/ad1259-mod-compat.json` and `MODS.md`, and Burning
Empires reports to Coodsz/Coodsz-BurningEmpires (`BE_GITHUB_REPO`),
where the BE tally counts them into `compat/be-mod-compat.json` and `MODS.md`. Normal reports
are approved on their own after a day; odd ones are held for Coodsz.

Until the worker is deployed and its address is in `ModReports.Endpoint`
(`src/BannerlordCoopLauncher.Core/Modules/ModReports.cs`), the tools keep opening the GitHub
issue form as before. Nothing breaks while the endpoint is empty.

## How it works

1. The player clicks a community badge, **Report a mod** at the top of the tool, **Report how a
   mod worked** on the mods page, **Mod crashed? Report it** under Connect, **Tell everyone which
   mod broke** on the crash bar, or **Report** on the bar after a game (or, if they turned it on,
   the **Share how your mods worked?** window opens once per run after a joined game closed or
   crashed) and picks Works, Broken or Works only with for each mod. After a crash the mods the
   crash log names are already marked Broken; the player checks them before sending.
2. Before the first send the window shows the exact JSON that will be sent. The second click sends it.
3. The tool posts `POST /report` with:
   `kind`, `schema`, `install` (a random 32 hex digit number the tool made on that PC, kept in
   `%AppData%\Coodsz\mod-reports.json`), `tool` (`coop`, `taom`, `ad1259` or `be`), `toolVersion`, `coop`, `game`,
   `reports` (`id`, `name`, `version`, `result` = `works` | `broken` | `works-with`, `with`) and
   `mods` (the setup's mod ids and versions in load order).
4. The worker checks every field (plain mod ids and versions only, no paths, no Steam ids, at
   most 25 rated mods and 250 listed mods, at most 32 KB), applies the limits below, and files or
   updates one GitHub issue per player per mod version with the labels `mod-compat`,
   `mod-report` and `pending` (plus `taom` for TAOM reports, `ad1259` for AD1259 reports and `be` for Burning Empires reports).
   A report that looks odd (see Held for review below) also gets `suspicious` and a line at the
   end of the issue that says why.
5. **Auto approval:** the tally skips every issue labelled `pending`. Every hour the workflow
   `.github/workflows/auto-approve-reports.yml` in Coodsz/Coodsz-Coop-and-Friends removes `pending`
   from the open reports nobody changed for 24 hours (`WAIT_HOURS` at the top of that file) that
   have none of `suspicious`, `spam` or `invalid`, and then runs the tally. So a normal report
   counts a day after it came in, and Coodsz can drop it with `spam` or `invalid` in that day.
6. **Review the held ones:** open
   `https://github.com/Coodsz/Coodsz-Coop-and-Friends/issues?q=is%3Aopen+label%3Asuspicious+label%3Apending`
   (the issue list filtered by `label:suspicious`). Read the reason line at the end of each one,
   then either remove `pending` (select the issues, Label, untick pending) to count it, or add
   `spam` or `invalid` to drop it. Removing the label starts the tally workflow, which updates
   `compat-list/mods.json` within a minute or two. Leaving `suspicious` on an approved report is
   fine; it only stops the auto approval.
7. A player who changes their vote for the same mod version gets their issue edited (not a new
   one) and it gets the `pending` label again, so it waits another day (and is held again if it
   looks odd).

TAOM reports (label `taom`) are filed in Coodsz/Coodsz-TAOM (`TAOM_GITHUB_REPO`, default
`Coodsz/Coodsz-TAOM`) with the same labels, limits and held for review rules. The auto approval and
the TAOM tally there count them into `compat/taom-mod-compat.json` (player counts next to the hand
checked tag, which stays Coodsz's call) and write `MODS.md` (docs/compat-list-publish.md, TAOM
community list). Review held TAOM reports at
`https://github.com/Coodsz/Coodsz-TAOM/issues?q=is%3Aopen+label%3Asuspicious+label%3Apending`.
TAOM votes sent before this change sit in Coodsz-Coop-and-Friends; the worker never edits those
again. When that player votes on the mod again it files a new issue in Coodsz-TAOM.

AD1259 reports (label `ad1259`) are filed in Coodsz/Coodsz-AD1259 (`AD1259_GITHUB_REPO`, default
`Coodsz/Coodsz-AD1259`) the same way. AD1259 is a fixed pack, so these reports mostly say which pack
mod crashed. The auto approval and the AD1259 tally there count them into
`compat/ad1259-mod-compat.json` and write `MODS.md` (docs/compat-list-publish.md, AD1259 community
list). Review held AD1259 reports at
`https://github.com/Coodsz/Coodsz-AD1259/issues?q=is%3Aopen+label%3Asuspicious+label%3Apending`.
AD1259 votes sent before this change sit in Coodsz-Coop-and-Friends, where the Coop and Friends tally
leaves them out; the worker never edits those again and files the player's next vote in Coodsz-AD1259.

Burning Empires reports (label `be`, from the Coodsz Burning Empires Server Tool and Client Launcher) are filed in
Coodsz/Coodsz-BurningEmpires (`BE_GITHUB_REPO`, default `Coodsz/Coodsz-BurningEmpires`) the same way.
The worker token needs Issues read and write on that repository (see Turn on Burning Empires reports below).
The auto approval and the
BE tally there (`tally-taom-compat.mjs --list be`) count them into `compat/be-mod-compat.json` and
write `MODS.md` (docs/be-modlist-publish.md). Review held BE reports at
`https://github.com/Coodsz/Coodsz-BurningEmpires/issues?q=is%3Aopen+label%3Asuspicious+label%3Apending`.
BE votes always had the tool in their key, so no BE vote sits in Coodsz-Coop-and-Friends.

## Privacy

- No Steam id, player name, folder path or IP address is ever stored or put in an issue. (Crash
  reports from newer tools carry the Discord name the player typed, see Crash reports below.)
- The install id is random and only for these reports. The worker keeps an HMAC of it
  (secret `VOTER_SALT`), and the issue shows only `anon-` plus 24 hex digits of that HMAC. Without
  the salt nobody can tie the hash to the install id, and the install id says nothing about the player.
- The caller's address is used only as the key of Cloudflare's rate limiting binding, whose
  counters live in memory for 60 seconds. The worker never writes it anywhere.
- KV holds only: per vote `{ result, with, list hash, issue number }` under a key made of the
  reporter hash and a hash of the mod id and version, plus day counters that expire after two days.

## Spam protection

- At most 10 requests a minute per address (rate limiting binding `REPORT_LIMITER`).
- At most `MAX_PER_INSTALL_DAY` (default 60) rated mods per install per day.
- At most `MAX_REPORT_WRITES_DAY` (default 150) KV writes a day for mod reports, for everyone together.
  The free plan has 1,000 KV writes a day for the whole account and Send my logs needs up to 600 of them,
  so a busy report day no longer uses up the day for crash reports. A new vote costs 2 writes (the mod's
  stats before its issue, the vote with the issue number after it), a changed vote 2 (the vote, the
  stats), and each request 2 more (this budget and the install's day count) plus 1 the first time an
  install is seen. A 25 mod report from a new install is 53, so 150 is about two full reports and a few
  small ones a day for all tools together. A request books all its writes before it writes anything;
  past the budget, or when the booking fails, it files nothing and answers 429 or 503. A request that
  stops early (GitHub fails) gives the unused writes back. The same vote again writes nothing. The
  booking is a KV read and write, not an atomic add: requests in the same moment can book a little past
  the cap, and a data centre that sees the count up to a minute late can book its own full cap in that window.
- KV must take a write before an issue is filed (the mod's stats go first), so when KV takes no more
  writes the answer is 503 and no issue is filed that no vote points to. Each key is written once per
  request, clear of KV's one write a second per key (only the give back writes the budget a second time,
  a second after the booking).
- One vote per install per mod version and tool (the same vote again changes nothing). The TAOM,
  AD1259 and BE tools keep their votes apart from Coop and Friends on the same PC, so each repository
  keeps one issue per player per mod version.
- Every report waits a day (`pending`) before it counts, and the tally keeps only the latest
  vote per reporter per mod.
- Odd reports are held for Coodsz (below).
- Someone who makes a new random install id for every report is still limited by the address
  limit, the global cap and the review step. KV is eventually consistent, so two requests in the
  same second can rarely file two issues for one vote; the tally still counts that reporter once.

### Held for review (`suspicious`)

The worker adds `suspicious` when any of these is true for a report. The answer to the tool is
the same either way, so a spammer cannot see what was held.

- **Many reports from one install:** it is past the `SUSPICIOUS_PER_INSTALL_DAY`th (default 30)
  rated mod from that install today. The day cap stays `MAX_PER_INSTALL_DAY` (60); a player who
  rates a big list once stays under 30.
- **A mod id nobody reported before, with a Broken or client only verdict:** the first such report for a mod id
  (KV keeps a `m:<hash of the id>` entry per reported mod with its Works count, per tool: Coop and Friends under
  `m:<hash>`, the other tools under `m:<tool>:<hash>`; a tool without its own entry yet starts from the `m:<hash>` counts,
  which every tool wrote before the stats were per tool). A first Works report is
  not held, it is approved after a day like any other. A changed vote on an older issue counts as known.
- **A tool version no release has:** `toolVersion` must be `X.Y.Z` from the first release with
  one click reports up to two minors ahead: Coop and Friends 1.2.61 to below 1.4.0, the TAOM
  client 1.1.41 to below 1.3.0, the AD1259 client 1.0.20 to below 1.2.0, the BE tools 1.0.0 to below 1.2.0 (`TOOL_VERSIONS` in
  `src/index.js`; raise the upper end before a release passes it).
- **A burst of Broken:** a Broken report when the mod got more than `BURST_BROKEN` (default 5)
  Broken reports in the last 3 hours (this one included) and already has at least
  `BURST_MIN_WORKS` (default 10) Works reports. The `m:` entry keeps the Broken count of each of
  the last 3 hours, so a report writes one stats entry. (The old `b:<hash>:<hour>` keys are still read; that
  read can be removed after 2026-12-01.)
- **A flip to Broken:** a Broken report that, together with the Broken reports of the last 3
  hours, takes a mod whose Works led before those hours to `FLIP_THRESHOLD` (default 3, the
  tally's THRESHOLD) more Broken than Works, so the mod would turn Broken on its own. The
  `m:` entry keeps the mod's Broken count next to its Works for this.
- **A new install on a quiet mod:** a Broken report from an install first seen today for a mod
  with fewer than `NEW_INSTALL_MIN_WORKS` (default 10) Works. KV keeps one `i:<reporter hash>`
  entry per install with the day it was first seen (it expires after a year).

Held reports keep `pending` until Coodsz decides, as in step 6 above.

## Crash reports

The same worker takes the crash reports behind **Send crash report** (the crash bar in the three
clients), **Send server crash report** (the bar in the server tools after the server crashed or
stopped by itself) and **Send my logs** (always on the main page of every tool). The tool packs the
cleaned logs into one zip, shows the player every file with its size and the summary, and sends
nothing until the player clicks **Send**. The player gets back a short code like `CR-7KQ2M` to post
in Discord instead of the log files.

Both windows also offer two optional extras. A player can type up to 2000 characters of "what happened"
(cleaned like the logs, packed as `player-notes.txt`) and add up to 5 screenshots (PNG or JPG, at most
4 MB together, packed under `screenshots/`). `report.json` in the zip marks them `fromPlayer`. The tool
shortens the logs further when pictures need the room, so the zip stays under 7.5 MB.

1. The tool posts `POST /crash` as `multipart/form-data` with two parts:
   - `summary`: JSON with `kind` (`CoodszCrashReport`), `schema` (1), `install` (the same random
     install id as the mod reports), `tool` (`coop`, `taom`, `ad1259` or `be`), `side` (`client` or
     `server`), `reason` (`crash`, `server-crash` or `logs`), `problem` (for Send my logs: `join`,
     `desync`, `lag` or `other`), `toolVersion`, `coop`, `game`, `crashLine` (the one line of the
     crash the tool found), `suspectedMod`, `files` (`name` and `size` of every file in the zip)
     and `contact` (optional, the player's Discord name, see Privacy below).
   - `zip`: the zip, at most 8 MB. The tools shorten long logs to their end so it fits. While the
     player leaves Add the crash dump ticked, it also holds the one newest crash dump of this crash
     in `crash-dumps/` (at most 512 MB raw and 5 MB compressed, a budget it shares with the
     screenshots). `report.json` marks it with `dump: true`. The worker stores it like any other
     file and never opens it.
2. The worker checks every field, the zip's first bytes and the size, applies the limits below,
   keeps the zip in the `VOTES` KV namespace under `crash:<code>` for 30 days and files an issue in
   the private repository `CRASH_REPO` (default `Coodsz/Coodsz-Crash-Reports`) with the labels
   `crash-report`, `client` or `server`, `coop`, `taom`, `ad1259` or `be`, and `new`. TAOM, AD1259
   and Burning Empires reports go to their own private repositories instead (`TAOM_CRASH_REPO`,
   `AD1259_CRASH_REPO` and `BE_CRASH_REPO`, see `crashRepo` in `src/crash.js`). The only log text in
   it is the one crash line and the triage evidence, cleaned again. The player's note is read from
   `player-notes.txt` in the zip and quoted in the issue, cleaned (see Privacy).

   The issue reads top to bottom like a bug report:
   - Title: what happened in plain words, the tool and the side, and the code at the end, like
     `The graphics card ran out of room for textures (Coop and Friends, client) [CR-7KQ2M]`. A known
     crash uses the signature title. Otherwise it is the reason (`The game crashed`, `The server
     crashed or stopped by itself`, `Logs sent, no crash`) and the first words of the crash line or
     the problem the player picked.
   - The body starts with the hidden marker `<!-- coodsz-crash-report v1 -->` and `Report code **CR-7KQ2M**`,
     then these sections: **What happened** (the triage text, or the crash line with a plain words
     line for common .NET errors), **Known cause?** (`Yes.` with the signature title and whether it
     needs a report, or `Not yet, this one needs a look.`), **What the player said** (the problem they
     picked, their note as a quote block, and any screenshots, else `No note.`), **Severity** (see below),
     **Player and setup** (tool, side and version, Coop, Bannerlord, suspected mod, whether
     `modules.json` is in the zip, `Contact: <name> (Discord)` when the tool sent one, the reporter
     hash), and a folded **Technical details** block with
     the code, the crash line, the files in the zip, the zip size and keep until date, the download
     line, the evidence lines and the triage and signatures version.
   - Severity is set by the worker: High for a crash without a known cause, Average for a known Coop
     bug or a known cause that still wants a report (and for Send my logs), Low for a known cause that
     needs no report or for not-a-crash.
3. It answers `{ "ok": true, "code": "CR-7KQ2M", "expires": "2026-11-21", "filed": true, "replies": false }`. `replies` is true when the tool sent a reply key and the worker stored it (see In tool replies). When
   GitHub fails the zip is still kept and the code still works (`filed` is false).

Codes use only `23456789ABCDEFGHJKMNPQRSTUVWXYZ` (no 0, O, 1, I or L).

**Auto triage.** Every tool (Coop and Friends, Coodsz Host, AD1259, TAOM and Burning Empires) runs
`compat-list/crash-signatures.json` over the packed logs of a crash report before the player sends,
and puts the result in `summary.triage` (and in `report.json` inside the zip): `signature` (the id, or null for an
unknown crash), `category`, `signaturesVersion`, `title`, `whatHappened`, `whatToDo` (optional, shown
under Known cause when a tool sends it), `reportNeeded`,
`trackingIssue`, `labels` (at most 3) and `evidence` (at most 10 log lines that matched, cleaned
again here). Send my logs runs only the signatures marked `sendMyLogs` (setup problems, no exit
code), shown as a hint while Send stays as it is, and sends the triage the same way. Older tools send
no `triage` and are filed exactly as before.

- Labels: `unknown`, or `known` plus the category (`not-a-crash`, `setup`, `mod-assets`,
  `mod-missing-patch`, `coop-bug`, `silent-exit`, `mod-mismatch`) and the signature's own labels.
- The triage goes in the issue body itself (What happened, Known cause?, the evidence in Technical
  details), no second comment. `not-a-crash` is closed right away (not planned); the zip and the code stay.
- `reportNeeded` with a `trackingIssue`: no new issue. The report becomes a comment on that issue in
  the same shape, shorter (code, what the player said, severity, setup, and folded technical details
  with the evidence and download line), but only when that issue is open and has the label
  `tracking`. Otherwise a normal issue is filed. The answer then has `"tracked": 8`.
- The same reporter and the same signature within 10 minutes (`DEDUPE_TTL`) gets the earlier code
  back with `"duplicate": true`. Nothing is stored or filed and no daily count is used. Unknown
  crashes and Send my logs reports are never folded (a player who sends logs after a hint may add a
  note). A Send my logs report that matched a hint is Average.

**Limits.** The address limiter counts crash reports under their own key. At most
`MAX_CRASH_PER_INSTALL_DAY` (default 10) per install per day and `MAX_CRASH_REPORTS_DAY` (default
100, so a busy day stays under the free plan's 1,000 KV writes) for everyone per day.

**Privacy.** The tools take Windows user names, the PC name, Steam ids, IPv4 and IPv6 addresses,
emails, server and join passwords, tokens and the password the tool knows out of every text file
before packing (`LogZipBuilder.Clean`), and leave out saves. A crash dump is only added while Add
the crash dump is ticked, and it is not cleaned. It is a snapshot of the game's memory and can hold
the Windows user name, a Steam id, the join password and player addresses. Only Coodsz can open the
report. The worker keeps only the reporter hash, never the install id or the address.

The player's note (`player-notes.txt`, cleaned by the tool like the logs) now appears in the issue.
The worker reads it straight from the zip (stored or deflated, at most 64 KB packed and 16 KB of text,
anything odd means `No note.`), cuts it to 1500 characters and cleans every line again: addresses,
Steam ids, user folders and passwords are taken out, backticks, `<`, `>` and `@` become `'`, markdown
is escaped, `&` is written as `&amp;`, bare links and issue refs (`https://`, `www.`, `#12`, `GH-12`)
are broken with a zero width space, and a line that starts like a list, quote or rule is escaped. So
a note cannot add links, images, mentions, issue refs, headings, HTML or break out of the quote.
The same link and ref breaking applies to the triage text and `whatToDo`, and the crash line gist in
What happened sits in a code span.

The player's Discord name (`contact`) is the one thing in a crash report that names the player on
purpose. Newer tools ask for it before Send (the window says people who can read the report can see
it) and keep it on the PC for the next report; Save a copy never needs it. Older tools send none and
are filed as before. The worker refuses a `contact` that is not a string or is longer than 40
characters after trim, takes out control and format characters and keeps only letters, numbers,
spaces and `_ . - ' + ?`, then cleans it like the crash line, escapes markdown and breaks links and
refs. It shows only as `- Contact: <name> (Discord)` under Player and setup (and in a tracked
comment). It is never in the zip.

### Fetch a report

Someone posts a code in Discord. Open the issue with that code in
[Coodsz/Coodsz-Crash-Reports](https://github.com/Coodsz/Coodsz-Crash-Reports) to read the summary,
then download the zip in a terminal:

```
curl.exe -H "X-Admin-Key: <your ADMIN_KEY>" -o CR-7KQ2M.zip https://coodsz-mod-reports.coodsz.workers.dev/crash/CR-7KQ2M
```

The zip you download holds the player's words and screenshots too. Screenshots are sent as they are,
so look at them with care.

The code works in any case. A wrong or missing key answers 401, a code that is not there (or older
than 30 days) answers 404. Move the issue from `new` to whatever label you use once you looked at it.

## In tool replies

You can answer a report on its GitHub issue, and the player's tool shows your answer the next time
it checks. Older tools see nothing, so Discord stays the way to reach them. The design is
docs/design/in-tool-replies.md.

- **Which reports.** Only reports from newer tools. Their issue (or their comment on a tracking issue)
  has the line `In tool replies: on (comment /reply to answer the player)`. No line means answer in Discord.
- **Answer.** Comment `/reply <your answer>` on the report's issue. On a tracking issue name the
  report: `/reply CR-7KQ2M <your answer>`. Add `+logs` (`/reply +logs <text>`) to give the player a
  "Send my logs again" link. Only the first line is read as the command; your text can go on below it.
- **Change or take back.** A newer `/reply` replaces the older one. Editing your comment shows it to
  the player once more. `/retract` replaces your newest answer with "That answer was withdrawn.
  Coodsz is looking into it." On a tracking issue name the report there too: `/retract CR-7KQ2M`.
- **Only you.** The worker serves only comments by your GitHub account (by its number,
  `COODSZ_GITHUB_ID`). Comments by anyone else, collaborators too, never reach a player.
- **What the player sees.** Plain text, at most 1500 characters. Links (except discord.gg, Discord
  invites, the Coodsz tool release pages and Steam Workshop pages), pictures, code blocks, HTML,
  Steam ids, addresses, user folders, passwords, emails, @mentions, issue numbers and other report
  codes are taken out (versions like 1.2.76.0 stay). Your answer is not translated.
- **How the tool asks.** `POST /replies` with its report codes and the reply keys it made for them.
  v1.3.5 tools ask every 30 seconds for 10 minutes after a report or a No, still broken, then every 2 to
  3 minutes while a report younger than 30 days has an answer the player has not read, or is Looking into it
  with a send or No, still broken in the last day, and every 15 to 30 minutes while the only reports left are
  answered and read, or Looking into it and quiet for a day. They ask nothing while the
  tool's window is minimized or no report is open, and ask again within seconds when it is restored or a
  report is sent. They send `"fast": true` and only go that fast when the worker has reply push on (Turn on
  reply push below); older tools and a worker without push stay at about every 30 minutes. A longer `next`
  from the worker always wins, which is how the free plan brake below slows everyone down. Without push
  the worker reads GitHub as below. The worker keeps only a salted hash of each key in `rr:<code>` (30 days,
  like the zip; a duplicate report's key gets its own `rrk:<code>:<hash>` entry), checks it, reads
  the issue's comments from GitHub (the newest 4 pages of 100 on a very long issue) and keeps the
  cleaned answers for 5 minutes. A wrong key, an unknown code and an old report all get the same empty
  answer. `+sig` and `+nosig` on the `/reply` only matter to the signature candidate (crash-repos/README.md).
- **GitHub budget.** The crash token also files the reports, so the worker reads gently: one key and
  code at most once every 10 minutes (it gets its last answer in between), and at most 30 GitHub page
  reads a minute for the whole worker (the `REPLY_READ_LIMITER` binding in wrangler.toml; without it
  each running copy of the worker counts on its own). Over that, a check gets nothing new and the tool
  asks again 30 minutes later.
- **When the token runs out** (2027-01-20), the worker log says GitHub answered 401 or 403 and the
  tools wait 6 hours between checks. Renewing the token fixes both reports and replies.
- **The status badge.** Each report in the tool shows Looking into it, Answered, Solved or Fixed in a
  version. Add the label `fixed-in-v1.3.3` (any version) to the issue and the tool shows Fixed in v1.3.3,
  even after the player's last No, still broken. On a tracking issue that label marks every report on
  it. The `player-solved` label (Yes, it's fixed) shows Solved, not Fixed. Closing or reopening the
  issue does not change the badge. The check reads the issue once more for its labels (one more page
  read under the same cap).
- **Yes, it's fixed.** The player's tool posts `POST /replies/solved` with the report's code and reply
  key, checked the same way as `/replies`. The `SOLVED_ACTION` var in wrangler.toml decides what
  happens: `close` (the default) adds the `player-solved` label and closes the issue as completed (an
  issue you closed already keeps its reason), `label` only adds the label. A report on a tracking issue never closes it: that issue gets a one
  line comment instead. A second click does nothing new (`rs:<code>` in KV, one write per report).
- **No, still broken.** The player picks whether they tried the steps, writes what happens now (up to
  1500 characters) and can add the new logs from the last run and one screenshot. The tool packs it
  like any report (cleaned like the logs, the picture stripped of its metadata) and sends it to
  `POST /crash` with `summary.followUp` (the code, the reply key and the steps answer). The key must
  open the report like on `/replies`, and each key has its own limit on top of the crash limits. The
  zip gets a new code as usual, but no new issue: the report's own issue gets a comment with their
  words quoted and cleaned, the steps answer and the download line. It loses `player-solved` and opens
  again only when it was closed as completed or by Yes, it's fixed; an issue you closed as not planned
  stays closed. The badge goes back to Looking into it until your next `/reply` or a `fixed-in` label.
  When GitHub takes no comment the worker answers 502 and the tool asks the player to try again. Only a
  follow up comment written by the worker counts: by your account (the crash token is yours) or by
  `WORKER_GITHUB_ID` when the token belongs to a bot account.
- **Hidden markers.** An HTML comment on top of your answer, like `<!-- report-replies -->`, is skipped:
  the `/reply` line under it still counts, and the marker never reaches the player.
- **Reply push.** With the `GITHUB_WEBHOOK_SECRET` secret set and the webhook on each crash repository,
  GitHub tells the worker about every new, edited or deleted comment (`POST /github/webhook`). The worker
  checks GitHub's signature, keeps only your `/reply` and `/retract` comments and its own No, still broken
  comments, cleans them exactly like the GitHub read, and stores them in the report's own `rr:<code>`
  record (only when they change; a write reads the record once more first). A check reads that record anyway
  to open the key, so it costs no extra KV read and asks GitHub nothing, and your answer shows in the player's tool within about a minute. Behind
  it, each report is still read from GitHub at most once every 30 minutes per running copy of the worker
  while a tool asks about it (when the next read is due is kept in the cache, never in KV), so a
  missed webhook still arrives. The webhook also takes the Issues event, so a `fixed-in-v...` or
  `player-solved` label added to or taken off a report's own issue reaches the badge at once. On a tracking
  issue (no code of its own) a label change shows at your next comment there or the 30 minute read. GitHub
  does not promise the order of label deliveries, so one that arrives late can leave the badge a step behind
  until that read. Comment deliveries that arrive late or twice never bring back older text or a deleted
  answer (an edit from the same second as the stored text waits for the read too), and v1.3.5 tools set aside
  an answer you deleted (it shows as replaced) once the worker knows the full list: only in the answer to a
  check that read that report on GitHub and stored the read (about every 30 minutes), never from what KV or
  the worker's memory holds. An issue past 5 pages of 100 comments (a busy tracking issue) is read only on
  its newest pages, so it never gives a full list, and the stored answers from the pages it skipped are kept.
  When the worker serves a set aside answer again, the tool shows it again.
- **When KV is slow or busy.** A KV read in another Cloudflare data centre can be 60 seconds or more out of
  date, and one key takes about one write a second. Two deliveries close together can still lose one answer,
  and a burst past one write a second gets a 503 from the webhook (it says the next GitHub read heals it).
  Both are healed by that report's next 30 minute GitHub read.
  On an issue of one page (up to 100 comments), a delete whose webhook never came (a 503, downtime, or the push
  write budget) is caught by that read too: a stored answer GitHub no longer lists is dropped, unless the webhook
  stored it while the read ran. On an issue of more pages a page can shift between two requests (a comment
  deleted on an earlier page) and hide a live answer, so there a missed delete stays served, never as a full list.

### The Discord ping

When a report needs you (no signature matched, Send my logs, or a known Coop bug), the worker posts one
short line to a Discord channel you pick: the game, the code, the cleaned crash line and the issue
link. No Steam ids, names, logs or mod lists, and it can never mention anyone. Each report pings once,
at most 10 every 10 minutes (a burst at the same moment can pass that by a few); past that the next
ping says how many more are waiting on GitHub. Without the `CRASH_PING_WEBHOOK` secret nothing is posted.

### KV writes per crash report

The free Workers plan allows 1,000 KV writes a day for the whole account, the mod reports included.
A crash report writes:

| Report | Writes |
| --- | --- |
| Older tool, no reply key, no ping | 3 (the zip, the install's day count, the global day count) |
| Plus a known signature | +1 (the 10 minute duplicate check) |
| Plus a reply key (filed) | +1 (`rr:<code>`) |
| Plus a Discord ping that was sent | +1 (`ping:<time>:<code>`, only after Discord took it) |
| A duplicate within 10 minutes | 0, or 1 when it brings a new reply key |

So an unknown crash from a new tool with the ping on is 5 writes, a known Coop bug 6. At the old default
`MAX_CRASH_REPORTS_DAY` of 200 that is up to about 1,200 writes on a busy day, over the free 1,000. The
template now says 100 (Reply push budget below has the full sum). Without reply push `POST /replies` writes nothing to KV,
and That fixed it writes one `rs:<code>` per report.

### Reply push budget

The account is on the free Workers plan: 100,000 requests, 100,000 KV reads and 1,000 KV writes a day for
everything (mod reports, crash reports and replies together). Reply push is sized to it.

**The cadence.** One request per check, whatever the number of reports in it (up to 10). Per v1.3.5 tool:

| What the tool has | Check every | About |
| --- | --- | --- |
| A report sent or a No, still broken in the last 10 minutes | 30 to 35 seconds | 18 checks, once per send |
| An answer not read yet, or a report Looking into it with a send or No in the last day | 2 to 3 minutes | 24 an hour |
| Only answered reports with every answer read, or Looking into it and quiet for a day | 15 to 30 minutes | 3 an hour |
| Only Solved or Fixed reports, no reports, or the window minimized | never | 0 |

Older tools ask every 30 minutes (2 an hour). The Server Tool and the Client Launcher on one PC count as two.

**Requests a day**, one open report per player (sent that day), the window open (not minimized):

| Players with an open report | Window open 4 hours a day | Window open all day |
| --- | --- | --- |
| 50 | 5,500 | 29,500 |
| 200 | 22,000 | about 77,000 (118,000 without the brake) |
| 500 | about 54,000 | about 96,000 (295,000 without the brake) |

Players whose reports are all answered and read, or quiet for a day, add about 11 a day each (4 hours) or
64 (all day). KV reads are about the same as requests: one `rr:<code>` per report per check, plus per running
copy of the worker one `rq:` and one `pw:` counter read a minute at most, one per webhook delivery, and on
every push write one more `rr:<code>` read right before the put plus one per counter batch. Webhook
deliveries are requests too: one per comment on a crash issue, and a few Issues deliveries per filed report (opened and its labels),
a few hundred a day.

`/replies` shares `REPORT_LIMITER` (10 a minute per address) under its own `replies:<ip>` key, so about 5
v1.3.5 tools in their 30 second phase behind one shared address (a household or a mobile network) get a 429
and back off for 15 minutes.

**The brake** (`src/budget.js`). When the day's count (UTC) passes a step, every `/replies` answer asks for a
longer `next`, which old and new tools both honour, the 30 second phase too:

| Day's count passes | `next` at least |
| --- | --- |
| 50,000 | 5 minutes |
| 75,000 | 15 minutes |
| 90,000 | 30 minutes |
| 95,000 | 6 hours (the safety net, so the day's last requests stay for crash and mod reports) |

The count is every request the worker gets, plus one per extra report in a check (each is a KV read). It is
counted before routing, so junk sent to any path counts too, at no extra KV cost (each batch is one read and
one write, the same either way). It is kept in memory and added to `rq:<date>` in KV in batches, so it runs a little late and a little
low; the 95,000 step leaves the margin. A batch write that fails waits a minute before the next try. Mod
reports, crash reports and everything else are never refused or slowed by it.

**Worst cases.** 200 players with the window open all day reach the 5 minute step after about 10 hours and
the 15 minute step near the end of the day. 500 reach 50,000 after about 4 hours, 75,000 after about 9,
90,000 after about 16 and the 6 hour step after about 22. Many reports sent at once (everyone in the 30
second phase) reach 50,000 within the hour and are slowed from there. Past about 1,000 players left open all
day, or with a lot of other traffic, the late steps start earlier in the day.

**KV writes a day, at most:**

| From | Writes |
| --- | --- |
| A plain check | 0 |
| The brake's counter (`rq:`) | about 200 at worst (a batch per 1,000 counted, and extra reports in a check count too, plus up to 50 smaller batches after 10 quiet minutes) |
| Reply push (`rr:<code>.push`) | 1 per change to your answers: each `/reply`, `/retract`, edit or delete, each No, still broken, each `fixed-in` or `player-solved` label added or taken off, and once per report when its first GitHub read marks the webhook's state as read. A delivery that changes nothing writes nothing. **Capped at 100 a day**: past that a delivery gets 200 with `ignored: "budget"` and the 30 minute GitHub read serves the answer |
| The push cap's counter (`pw:`) | about 30 (a batch per 10 push writes, and up to 20 smaller ones) |
| Crash reports | 3 to 6 each (KV writes per crash report above), so up to 600 at `MAX_CRASH_REPORTS_DAY` 100. Reply keys are part of them and never wait for the push cap |
| Mod reports | **Capped at `MAX_REPORT_WRITES_DAY` (150)**: 2 per new or changed vote, booked before any is written (Spam protection above) |

To stay under 1,000 on a busy day with push on, set `MAX_CRASH_REPORTS_DAY = "100"` in wrangler.toml:
600 + 200 + 100 + 30 leaves about 70 for the mod reports on the worst day of everything at once. Most crash
reports are 4 or 5 writes, so a busy day with all 100 crash reports is closer to 500 + 330, and the 150 mod
report writes fit beside it. A normal day is far below (20 crash reports and 30 answers are about 200 writes
in all). If crash reports often reach their cap, lower `MAX_CRASH_REPORTS_DAY` to 80. Keep `MAX_REPORT_WRITES_DAY` at 53 or more:
below that one full 25 mod report from a new install never fits, and the tool cannot split it.

**To lower it,** raise `ReplySchedule.OpenWait` in `src/BannerlordCoopLauncher.Core/Support/ReportReplies.cs`
(2 minutes now, plus up to a minute): at 5 minutes a tool asks about 11 times an hour instead of 24, so the
open window numbers drop to under half. That needs a tool release. Without one, raise `PUSH_NEXT` in
`src/replies.js` or lower the `BRAKES` steps in `src/budget.js` and deploy: the tools already out never ask
sooner than the worker's `next` (`PUSH_NEXT` also slows the first 10 minutes after a report).

## Deploy (Coodsz does this himself)

You need a free Cloudflare account and Node.js 18 or newer on your PC.

1. **GitHub token.** On github.com: Settings, Developer settings, Personal access tokens,
   Fine-grained tokens, Generate new token.
   - Resource owner: Coodsz. Repository access: Only select repositories, `Coodsz/Coodsz-Coop-and-Friends`,
     `Coodsz/Coodsz-TAOM` (TAOM reports are filed there) and `Coodsz/Coodsz-AD1259` (AD1259 reports).
     A token made before those reports moved covers only the older repositories: edit it on github.com
     (Fine-grained tokens, the token, Repository access, add `Coodsz/Coodsz-TAOM` and
     `Coodsz/Coodsz-AD1259`, keep **Issues: Read and write**) before deploying this version, or those
     reports fail with "could not file the report".
   - Repository permissions: **Issues: Read and write**. Nothing else.
   - Expiration: pick a date and put a reminder in your calendar to renew it.
   - Copy the token. It goes only into the worker's secret below, never into the tools or this repository.
   (The issues will show your account as the author. If you prefer, make a separate GitHub
   account for this and create the token there after giving it triage access to the repository.)
2. **Labels.** In Coodsz/Coodsz-Coop-and-Friends, Issues, Labels, create `mod-report`,
   `pending`, `suspicious`, `taom` and `ad1259` (`mod-compat`, `invalid` and `spam` should exist
   already). Copy the updated scripts and workflows there too (docs/compat-list-publish.md).
   In Coodsz/Coodsz-TAOM create `mod-compat`, `mod-report`, `pending`, `suspicious`, `taom` and
   `spam` (`invalid` exists already), and copy its files (docs/compat-list-publish.md, TAOM community list).
   In Coodsz/Coodsz-AD1259 create `mod-compat`, `mod-report`, `pending`, `suspicious`, `ad1259`,
   `invalid` and `spam`, and copy its files (docs/compat-list-publish.md, AD1259 community list).
3. **Cloudflare.** In a terminal in this folder (`server/mod-reports-worker`):
   ```
   npx wrangler login
   copy wrangler.toml.template wrangler.toml
   npx wrangler kv namespace create VOTES
   ```
   Paste the id it prints into `wrangler.toml` (`PASTE_THE_KV_NAMESPACE_ID_HERE`).
4. **Secrets.**
   ```
   npx wrangler secret put GITHUB_TOKEN
   npx wrangler secret put VOTER_SALT
   npx.cmd wrangler secret put ADMIN_KEY
   ```
   For `GITHUB_TOKEN` paste the token from step 1. For `VOTER_SALT` paste a long random string,
   for example the output of `node -e "console.log(require('crypto').randomBytes(32).toString('hex'))"`.
   Keep a copy of the salt in your password manager: a new salt makes every player a new reporter.
   For `ADMIN_KEY` paste another long random string made the same way. It is the key for
   downloading crash reports (`GET /crash/<code>`). Keep it in your password manager too.
5. **Deploy.**
   ```
   npx wrangler deploy
   ```
   It prints the address, for example `https://coodsz-mod-reports.<your-subdomain>.workers.dev`.
   Check it with `https://coodsz-mod-reports.<your-subdomain>.workers.dev/health` in a browser
   (it answers `{"ok":true,...}`).
6. **Turn it on in the tools.** Put the report address, ending in `/report`, into
   `ModReports.Endpoint` in `src/BannerlordCoopLauncher.Core/Modules/ModReports.cs`:
   ```csharp
   public const string Endpoint = "https://coodsz-mod-reports.<your-subdomain>.workers.dev/report";
   ```
   Build, live test one report (it shows up as a pending issue), then release as usual.

### Turn on crash reports (once, on the worker that already runs)

1. **Token for the crash repository.** The `GITHUB_TOKEN` from step 1 only covers
   Coodsz/Coodsz-Coop-and-Friends. Either edit that fine grained token on github.com (Settings,
   Developer settings, Fine-grained tokens, the token, Repository access, add
   `Coodsz/Coodsz-Crash-Reports`, keep **Issues: Read and write**), or make a second token for only
   `Coodsz/Coodsz-Crash-Reports` with Issues read and write and run
   `npx.cmd wrangler secret put CRASH_GITHUB_TOKEN`. The worker uses `CRASH_GITHUB_TOKEN` when it is
   set and `GITHUB_TOKEN` otherwise.
2. **The admin key.** `npx.cmd wrangler secret put ADMIN_KEY` and paste a long random string
   (`node -e "console.log(require('crypto').randomBytes(32).toString('hex'))"`). Keep it in your
   password manager. Without it `GET /crash/<code>` answers 503.
3. **The var.** `wrangler.toml` needs `CRASH_REPO = "Coodsz/Coodsz-Crash-Reports"`, `TAOM_CRASH_REPO` and `AD1259_CRASH_REPO` (TAOM and AD1259 reports file there; the token needs Issues read and write on all three) (and the two
   `MAX_CRASH_...` caps) under `[vars]`, as in `wrangler.toml.template`. The labels `crash-report`,
   `client`, `server`, `coop`, `taom`, `ad1259` and `new` must exist in that repository. Auto
   triage adds `known`, `unknown`, `tracking`, the seven category labels (`not-a-crash`, `setup`,
   `mod-assets`, `mod-missing-patch`, `coop-bug`, `silent-exit`, `mod-mismatch`) and the labels the
   signatures list (`siege`, `assets`, `coopmodpatch`, `diplomacy`, `join`, `exit-code-1`). Put
   `tracking` on every issue a signature names as its `trackingIssue` (now #8 and #12).
4. **Deploy.** `npx.cmd wrangler deploy`, then check `/health` in a browser.
5. Live test one **Send my logs** from a tool: it shows a code, an issue with that code shows up in
   the crash repository, and the `curl.exe` line under Fetch a report downloads the zip.

### Turn on Burning Empires reports

The Coodsz Burning Empires Server Tool and Client Launcher send `tool` `be`. The code and `wrangler.toml.template` send those reports
to `Coodsz/Coodsz-BurningEmpires` (mod reports) and `Coodsz/Coodsz-BurningEmpires-Crash-Reports` (crash logs) by default.
These steps are the checklist. Do each one that is not done yet, and check each one on GitHub, because the code cannot tell
whether the repositories and the token access are already set up.
Do not redeploy without Coodsz's OK.

1. **Repositories.** Make the public `Coodsz/Coodsz-BurningEmpires` (mod reports and the BE list,
   its files come from `be-compat/` in this repository, see docs/be-modlist-publish.md) and the private
   `Coodsz/Coodsz-BurningEmpires-Crash-Reports`.
2. **Token.** Edit the fine grained token (Repository access, add both, keep **Issues: Read and
   write**). With a separate `CRASH_GITHUB_TOKEN`, add the crash repository to that one instead.
3. **Labels.** In Coodsz/Coodsz-BurningEmpires create `mod-compat`, `mod-report`, `pending`,
   `suspicious`, `be`, `invalid` and `spam`. In Coodsz/Coodsz-BurningEmpires-Crash-Reports create
   the same crash labels as in the other crash repositories, with `be` in place of `coop`.
4. **The vars.** `wrangler.toml` needs `BE_GITHUB_REPO` and `BE_CRASH_REPO` under `[vars]`, as in
   `wrangler.toml.template` (the code has the same defaults, so a missing line still works).
5. **Deploy and test** as above: one BE mod report shows up as a pending issue with the `be` label, and
   one Send my logs from a BE tool shows up in the BE crash repository.

### Turn on Realm of Thrones reports

The Coodsz Realm of Thrones Server Tool and Client Launcher send `tool` `rot`. The code and `wrangler.toml.template` send those reports
to `Coodsz/Coodsz-RealmOfThrones` (mod reports) and `Coodsz/Coodsz-RealmOfThrones-Crash-Reports` (crash logs) by default.
`RotRelease.Repo` and `RotRelease.CrashRepo` (src/RealmOfThrones.Core/RotRelease.cs) name the same two repositories, and a
launcher test checks they match the defaults in `repoFor` (src/index.js) and `crashRepo` (src/crash.js).
These steps are the checklist. Do each one that is not done yet, and check each one on GitHub, because the code cannot tell
whether the repositories and the token access are already set up.
Do not redeploy without Coodsz's OK.

1. **Repositories.** Make the public `Coodsz/Coodsz-RealmOfThrones` (releases and mod reports) and the private
   `Coodsz/Coodsz-RealmOfThrones-Crash-Reports`.
2. **Token.** Edit the fine grained token (Repository access, add both, keep **Issues: Read and
   write**). With a separate `CRASH_GITHUB_TOKEN`, add the crash repository to that one instead.
3. **Labels.** In Coodsz/Coodsz-RealmOfThrones create `mod-compat`, `mod-report`, `pending`,
   `suspicious`, `rot`, `invalid` and `spam`. In Coodsz/Coodsz-RealmOfThrones-Crash-Reports create
   the same crash labels as in the other crash repositories, with `rot` in place of `coop`.
4. **The vars.** `wrangler.toml` needs `ROT_GITHUB_REPO` and `ROT_CRASH_REPO` under `[vars]`, as in
   `wrangler.toml.template` (the code has the same defaults, so a missing line still works).
5. **The tools.** Release both tools. They already name both repositories in `RotRelease`.
6. **Deploy and test** as above: one Send my logs from each RoT tool shows up in the RoT crash repository with the
   `rot` label.

### Turn on in tool replies (before the deploy that has them)

Do these in Git Bash in `server/mod-reports-worker`. `printf %s` keeps a line ending out of a secret,
and `read -rs` keeps it off the screen and out of the shell history. Do not redeploy without Coodsz's OK.

1. **Your GitHub id.** Open `https://api.github.com/users/Coodsz` in a browser and copy the number
   after `"id"`. Put it in `wrangler.toml` under `[vars]`: `COODSZ_GITHUB_ID = "<that number>"`.
   While it is empty, replies stay off: the tools get `"replies": false` and keep no keys.
   Copy the `REPLY_READ_LIMITER` block from `wrangler.toml.template` into `wrangler.toml` too (the cap on GitHub reads).
   Copy `SOLVED_ACTION = "close"` too, or set it to `"label"` to keep issues open after Yes, it's fixed.
   **WORKER_GITHUB_ID must be right at this deploy.** The worker posts the No, still broken comments with
   the crash token. When that token is your own fine grained token (as now), leave `WORKER_GITHUB_ID = ""`.
   When the token belongs to a bot account, set `WORKER_GITHUB_ID` to that account's number: open
   `https://api.github.com/users/<bot login>` and copy the number after `"id"`. Without it every follow up
   is skipped, the badge never goes back to Looking into it after a No, and the worker log says
   "a coodsz-followup comment by GitHub user ... was skipped" once.
   Check the KV write budget first (KV writes per crash report above).
2. **The reply secret (optional).** Without it the worker uses `VOTER_SALT` with a `reply:` prefix,
   which is fine. To keep them apart, make one:
   ```
   printf %s "$(node -e "console.log(require('crypto').randomBytes(32).toString('hex'))")" | npx.cmd wrangler secret put REPLY_SALT
   ```
   Changing it later makes every reply key from before stop working, so set it once, before the first deploy with replies.
3. **The Discord ping (optional).** In your private Discord channel: Edit Channel, Integrations,
   Webhooks, New Webhook, Copy Webhook URL. Then:
   ```
   read -rs HOOK && printf %s "$HOOK" | npx.cmd wrangler secret put CRASH_PING_WEBHOOK; unset HOOK
   ```
   Paste the address after the first line (nothing shows) and press Enter. Never paste it anywhere
   else. Without it the worker just skips the ping.
4. **The signature candidate (optional).** In each crash repository, copy the files and set
   `COODSZ_GITHUB_ID` as crash-repos/README.md says. Pull requests stay off until you set `SIGNATURE_PRS`.
5. **Deploy and test.** `npx.cmd wrangler deploy`, send a Send my logs from a tool that has replies,
   check the issue has the In tool replies line, comment `/reply test`, and wait for the tool's next check.

### Turn on reply push (v1.3.5)

Your `/reply` then reaches the player's tool within about a minute instead of up to 30 minutes. Do it in
this order: the budget, the limit, the secret, the deploy, then the webhooks. Each step works on its own, so
there is no rush between them. Use Git Bash in `server/mod-reports-worker`: PowerShell's pipe adds a line
ending to the secret, and then every delivery fails with 401. Do not redeploy without Coodsz's OK.

1. **The budget and the webhook limit.** Read Reply push budget above and set
   `MAX_CRASH_REPORTS_DAY = "100"` in `wrangler.toml`. Copy the `WEBHOOK_LIMITER` block from
   `wrangler.toml.template` into `wrangler.toml` too (60 deliveries a minute per address, checked before the
   body is read). Without it the worker uses
   `REPORT_LIMITER` at 10 a minute, which a burst of comments from GitHub can pass; the 30 minute read still
   catches what was refused.
2. **Make the secret and give it to the worker.** In your password manager, make a new entry named
   `Coodsz webhook secret` with a generated password of 40 or more letters and digits (no spaces), and save
   it. Copy it from there, then in Git Bash:
   ```
   read -rs S && printf %s "$S" | npx.cmd wrangler secret put GITHUB_WEBHOOK_SECRET; unset S
   ```
   Paste it after the first line (nothing shows) and press Enter. It never shows on screen and never goes
   into the shell history. The worker that runs now ignores it.
3. **Deploy.** `npx.cmd wrangler deploy`. From now on the worker reads answers from KV and each report from
   GitHub at most once every 30 minutes, so until step 4 is done an answer can take up to 30 minutes.
4. **The webhook, once for each crash repository**: `Coodsz/Coodsz-Crash-Reports`,
   `Coodsz/Coodsz-TAOM-Crash-Reports`, `Coodsz/Coodsz-AD1259-Crash-Reports`,
   `Coodsz/Coodsz-BurningEmpires-Crash-Reports` and `Coodsz/Coodsz-RealmOfThrones-Crash-Reports`.
   1. On github.com open the repository, then Settings, Webhooks, Add webhook.
   2. Payload URL: `https://coodsz-mod-reports.coodsz.workers.dev/github/webhook`
   3. Content type: `application/json`
   4. Secret: copy it from the `Coodsz webhook secret` entry in your password manager and paste it here.
   5. SSL verification: Enable SSL verification (the default).
   6. Which events: Let me select individual events. Tick **Issue comments** and **Issues**, and untick
      everything else (Pushes is ticked by default).
   7. Tick **Active**, then Add webhook.
   8. Open the new webhook, Recent Deliveries: the first one is GitHub's ping, and the worker answers it
      with 200 `{"ok":true,"pong":true}`. A 401 means the secret differs: edit the webhook and paste it again.
5. **Clean up.** Windows keeps copied text in its clipboard history: press Win+V and pick Clear all (or
   copy something else, if clipboard history is off).
6. **Test.** Send a Send my logs from a v1.3.5 tool, comment `/reply test` on its issue, and check Recent
   Deliveries shows 200 `{"ok":true,"stored":1}`. The answer shows in the tool within about a minute.
   Other comments and issue changes get 200 with `ignored`, which is fine. Add the label `fixed-in-v1.3.5`
   to the issue: Recent Deliveries shows an `issues` delivery with `{"ok":true,"stored":1}`, and the tool shows
   Fixed in v1.3.5 at its next check (take the label off again after).

To change the secret later, run step 2 again and paste the new one into each webhook (Edit, Secret).
To turn push off, delete the secret (`npx.cmd wrangler secret delete GITHUB_WEBHOOK_SECRET`): the worker
reads GitHub on request again, and the webhooks get 503 until you delete them too.

## Tests

```
npm test
```

or the same files by hand:

```
node --test test/index.test.mjs test/crash.test.mjs test/replies.test.mjs test/followup.test.mjs test/webhook.test.mjs test/budget.test.mjs ../../crash-repos/.github/scripts/reply-signature.test.mjs ../../.github/scripts/tally-mod-compat.test.mjs ../../.github/scripts/tally-taom-compat.test.mjs ../../.github/scripts/compat-records.test.mjs ../../.github/scripts/auto-approve-reports.test.mjs
```

No packages are needed. The tests use a fake GitHub and a fake KV, so nothing is sent anywhere.
