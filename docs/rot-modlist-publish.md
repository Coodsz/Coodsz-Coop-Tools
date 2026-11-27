# Publishing the Realm of Thrones mod list

The Coodsz Realm of Thrones Client Launcher and Server Tool read the Realm of Thrones mod list (the list
status chip, the green and red count chips and the known broken prompt) from the public repository
Coodsz/Coodsz-RealmOfThrones (default branch `main`):

    https://raw.githubusercontent.com/Coodsz/Coodsz-RealmOfThrones/main/compat/rot-mod-compat.json

When that file cannot be read they use the last good download next to the tool, then the copy built
into the tool. A list whose `updated` date is older than the built in one is not used. See
`RotCompatList` in `src/RealmOfThrones.Core`.

Until the file is in the repository the download fails and the tools use their built in copy, so nothing breaks.

## The repository's files

Its first files come from `rot-compat/` in this repository. Copy, the same paths unless noted:

- `.github/scripts/tally-taom-compat.mjs`
- `.github/scripts/tally-mod-compat.mjs` and `.github/scripts/compat-records.mjs` (imported)
- `.github/scripts/auto-approve-reports.mjs`
- `.github/workflows/auto-approve-reports.yml` (unchanged; it calls `mod-compat-tally.yml`, which there is the RoT tally)
- `rot-compat/.github/workflows/mod-compat-tally.yml` to `.github/workflows/mod-compat-tally.yml`
- `rot-compat/.github/ISSUE_TEMPLATE/mod-compat.yml` to `.github/ISSUE_TEMPLATE/mod-compat.yml` (labels `mod-compat` and `rot`)
- `rot-compat/compat/rot-mod-compat.json` to `compat/rot-mod-compat.json` (the first time in full; after that carry hand edits over, the tally keeps the counts)
- `rot-compat/MODS.md` to `MODS.md` (the first time; after that the tally writes it)
- `rot-compat/README.md` to `README.md` (merge it with the README there, if any)
- `THIRD-PARTY-NOTICES.md`

**Labels to create there:** `mod-compat`, `mod-report`, `pending`, `suspicious`, `rot`, `invalid`
and `spam`.

**Token and Actions:** the report worker's `GITHUB_TOKEN` needs **Issues: Read and write** on
Coodsz/Coodsz-RealmOfThrones too. The worker files tool `rot` reports there by default
(`DEFAULT_ROT_REPO` in server/mod-reports-worker/src/index.js, `ROT_GITHUB_REPO` overrides it). Under
Settings, Actions, General, Workflow permissions must allow read and write, as in Coodsz-TAOM.

## At release

Copy the built in list to the repository:

- `src/RealmOfThrones.Core/Compat/rot-mod-compat.json` to `compat/rot-mod-compat.json`

Raise `updated` (YYYY-MM-DD) whenever the list changes, or tools with a newer built in copy keep theirs.
Keep `rot-compat/compat/rot-mod-compat.json` the same as the built in list, then write the page again:
`node .github/scripts/tally-taom-compat.mjs --list rot --page-only --file rot-compat/compat/rot-mod-compat.json --page rot-compat/MODS.md`.

The copy there also carries the player counts (`works`, `broken`, `clientOnly`, `lastReport`, and the
`community` array) that its tally workflow writes. They are not kept here: copy the hand file over
as usual, and the tally (it runs on that push) counts every report again and writes them and
`MODS.md` back.
