# Publishing the Burning Empires mod list

The Coodsz Burning Empires Client Launcher and the Coodsz Burning Empires Server Tool read the
Burning Empires mod list (badges, the Mod list window, the server load test) from the public repository
Coodsz/Coodsz-BurningEmpires (default branch `main`):

    https://raw.githubusercontent.com/Coodsz/Coodsz-BurningEmpires/main/compat/be-mod-compat.json

When that file cannot be read they use the last good download next to the tool, then the copy built
into the tool. There is no bannerlord.eu fallback. A list whose `updated` date is older than the built
in one is not used. See `BeCompatList` in `src/BurningEmpires.Core`.

Until the repository exists the download fails and the tools use their built in copy, so nothing breaks.

## The repository's files

Its first files come from `be-compat/` in this repository. Copy, the same paths unless noted:

- `.github/scripts/tally-taom-compat.mjs`
- `.github/scripts/tally-mod-compat.mjs` and `.github/scripts/compat-records.mjs` (imported)
- `.github/scripts/auto-approve-reports.mjs`
- `.github/workflows/auto-approve-reports.yml` (unchanged; it calls `mod-compat-tally.yml`, which there is the BE tally)
- `be-compat/.github/workflows/mod-compat-tally.yml` to `.github/workflows/mod-compat-tally.yml`
- `be-compat/.github/ISSUE_TEMPLATE/mod-compat.yml` to `.github/ISSUE_TEMPLATE/mod-compat.yml` (labels `mod-compat` and `be`)
- `be-compat/compat/be-mod-compat.json` to `compat/be-mod-compat.json` (the first time in full; after that carry hand edits over, the tally keeps the counts)
- `be-compat/MODS.md` to `MODS.md` (the first time; after that the tally writes it)
- `THIRD-PARTY-NOTICES.md`

**Labels to create there:** `mod-compat`, `mod-report`, `pending`, `suspicious`, `be`, `invalid`
and `spam` (`invalid` is one of GitHub's default labels, so it may be there already).

**Token and Actions:** the report worker's `GITHUB_TOKEN` needs **Issues: Read and write** on
Coodsz/Coodsz-BurningEmpires too (edit the fine grained token's repository access), and the worker
needs `BE_GITHUB_REPO` (server/mod-reports-worker/README.md, Turn on Burning Empires reports). Under
Settings, Actions, General, Workflow permissions must allow read and write, as in Coodsz-TAOM.

Held BE reports:
`https://github.com/Coodsz/Coodsz-BurningEmpires/issues?q=is%3Aopen+label%3Asuspicious+label%3Apending`.

## At release

Copy the built in list to the repository:

- `src/BurningEmpires.Core/Compat/be-mod-compat.json` to `compat/be-mod-compat.json`

Raise `updated` (YYYY-MM-DD) whenever the list changes, or tools with a newer built in copy keep theirs.
Keep `be-compat/compat/be-mod-compat.json` the same as the built in list, then write the page again:
`node .github/scripts/tally-taom-compat.mjs --list be --page-only --file be-compat/compat/be-mod-compat.json --page be-compat/MODS.md`.

The copy there also carries the player counts (`works`, `broken`, `clientOnly`, `lastReport`, and the
`community` array) that its tally workflow writes. They are not kept here: copy the hand file over
as usual, and the tally (it runs on that push) counts every report again and writes them and
`MODS.md` back.

## Results from the server load test

The Burning Empires server tool's "Test mods on the server" window keeps its results in
`Data\modtest-results.json`. "Copy results into the mod list file" writes
`Data\modtest-be-mod-compat.json`: the current list with every passed mod set to source
`server-loads` (a mod Coodsz tested in Coop keeps its entry). Read it over, then copy it over
`src/BurningEmpires.Core/Compat/be-mod-compat.json` here and `compat/be-mod-compat.json` there.

## Fields (kind `CoodszBeCompat`, schema 2)

The same fields as the TAOM list (docs/taom-modlist-publish.md), with `be` (the Burning Empires
version) in place of `taom`.

- `source`: `coodsz-coop` (tested in Coop by Coodsz), `server-loads` (passed the automatic server
  load test), `coodsz-notes`, and the Burning Empires Discord and developer sources `BeCompatList` knows.
- `checked`: the day it was checked.
- `tag`: `safe` (only for coodsz-coop), `everyone`, `risky`, `broken`, `reported` (works in Burning
  Empires singleplayer, not tested in Coop) or `loads` (loads on the server). Risky and broken keep
  the reason in `note`.

Burning Empires itself, Elephants and the Burning Empires Coop Compat module are the core and are on
the list as `safe`. Every other overhaul is `broken`, because it cannot run together with Burning Empires.
Notes are plain English with no colons.
