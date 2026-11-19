# Publishing the mod compatibility list

The tools read the community list and open the report form in the public repository
[Coodsz/Coodsz-Coop-and-Friends](https://github.com/Coodsz/Coodsz-Coop-and-Friends)
(default branch `main`, issues enabled). See `ModCompatDatabase.Repository`.

At release, copy these files from this repository to the same paths there:

- `compat-list/mods.json` (the first time in full; after that the tally workflow there keeps the counts, so carry hand edits over instead of overwriting the file)
- `compat-list/crash-signatures.json` (the known crash patterns of the crash report screens, `CrashSignatureFile.DefaultUrl`; the tools download it when a crash screen opens and fall back to their kept or built in copy. A new pattern only needs this file copied over, no tool release. Bump `version` with every change)
- `compat-list/announcement.json` (the notice at the top of every tool window, like Coodsz is on vacation, `AnnouncementFile.DefaultUrl`; the tools download it when a window opens and keep a copy. Set `enabled` to false or delete the file to take the notice down, no tool release needed. A new `id` or new words show it again to players who dismissed it. `tools` lists the families that show it: coop, taom, ad1259, be)
- `.github/ISSUE_TEMPLATE/mod-compat.yml`
- `.github/scripts/tally-mod-compat.mjs`
- `.github/scripts/compat-records.mjs` (the tally imports it; it writes `compat-list/compat-records.json`, the approved results as ModderLords compat records)
- `compat-list/compat-records.json` (the first time; after that the tally workflow keeps it)
- `.github/workflows/mod-compat-tally.yml` (it can also be called by the auto approval below)
- `.github/scripts/auto-approve-reports.mjs` and `.github/workflows/auto-approve-reports.yml` (the hourly auto approval of the one click reports)

`MODS.md` in the root there is the readable page of the whole list (status in plain words, the
works, broken and client only player counts, the last report day and the report form link), which
the **Full mod list** link in Coodsz Coop and Friends and Coodsz Host opens
(`ModCompatDatabase.ModsPageUrl`). It is not copied: the tally writes it on every run and commits it
with `compat-list/mods.json`, and the workflow also runs when `compat-list/mods.json` or the script
is pushed to `main`, so it shows up a minute after the copy. To run it by hand: Actions, Mod
compatibility tally, Run workflow. To look at it here first:
`node .github/scripts/tally-mod-compat.mjs --page-only --page <somewhere>/MODS.md`.
The tally also keeps `clientOnly` and `lastReport` on the reported entries; the tools skip both.

Label to create there: `mod-compat` (the form adds it, and the workflow only counts issues that have it).
The labels `invalid` and `spam` are optional; a report with either one drops out of the count.

For the one click reports (`server/mod-reports-worker`, see its README) also create `mod-report`
(the worker adds it; only issues with it may use the anonymous Reporter line), `pending` (every
worker report starts with it and does not count until it is removed), `taom` (older TAOM reports from before they moved to Coodsz-TAOM, which the tally leaves out) and `ad1259`
(older AD1259 client reports from before they moved to Coodsz-AD1259, which the tally leaves out too), and `suspicious` (the worker adds it to reports
that look odd, and the auto approval leaves those for Coodsz). The `.test.mjs` files are only needed here.

The three `.github` files point to `THIRD-PARTY-NOTICES.md` for the CoopKeep MIT notice, so that file should be in that repository too.

## Approving the one click reports

Every worker report starts with `pending`. The workflow `auto-approve-reports.yml` runs every hour
and removes `pending` from the open reports nobody changed for `WAIT_HOURS` (24, the one setting at
the top of that file) that have none of `suspicious`, `spam` or `invalid`, then runs the tally. It
uses only the built in `GITHUB_TOKEN` (issues write for the labels, contents write for the tally
commit), no secrets. A label removed with that token starts no other workflow, which is why it calls
the tally itself. Run it by hand from the Actions tab (Auto approve mod reports, Run workflow) to try it.

Held reports: open the issue list filtered by `label:suspicious`
(`https://github.com/Coodsz/Coodsz-Coop-and-Friends/issues?q=is%3Aopen+label%3Asuspicious+label%3Apending`).
Each one ends with a line saying why it was held (the rules are in `server/mod-reports-worker/README.md`,
Held for review). Remove `pending` to approve one; add `spam` or `invalid` to drop it. A normal report
can be dropped the same way during its first day.

## TAOM community list (Coodsz/Coodsz-TAOM)

The TAOM tools file their one click reports in [Coodsz/Coodsz-TAOM](https://github.com/Coodsz/Coodsz-TAOM)
(the worker's `TAOM_GITHUB_REPO`), not here. The same auto approval runs there, and a TAOM tally
counts the approved reports into `compat/taom-mod-compat.json` and writes `MODS.md`, the readable
mod list in that repository's root.

**How the counts sit next to the hand list.** The tally (`.github/scripts/tally-taom-compat.mjs`)
reads the reports like the Coop and Friends tally (`collectReports`, latest vote per person and mod;
`pending`, `invalid` and `spam` do not count) and only sets four fields on each entry: `works`,
`broken`, `clientOnly` and `lastReport` (the day of the newest counted report). It never changes
`tag`, `class`, `source`, `checked`, `note` or any other hand field, and the tag never moves on its
own. An entry nobody reported has no count fields. Reported mods that are not on the list go into a
separate `community` array next to `mods`, which the tools do not read, and show on `MODS.md` as
Not checked yet. Every run counts everything again, so the counts come back after the hand file is
copied over.

**Schema.** `schemaVersion` stays 2. The TAOM tools read the file with System.Text.Json, which skips
unknown properties (old and new tools, `TaomCompatList.Parse`), and none of the new names clash
with an existing one, so released tools read the counted file as before.

**Files to copy there** (from this repository, same paths unless noted):

- `.github/scripts/tally-taom-compat.mjs`
- `.github/scripts/tally-mod-compat.mjs` and `.github/scripts/compat-records.mjs` (the TAOM tally imports them; nothing else of the Coop and Friends tally runs there)
- `.github/scripts/auto-approve-reports.mjs`
- `.github/workflows/auto-approve-reports.yml` (unchanged; it calls `mod-compat-tally.yml`, which there is the TAOM tally)
- `taom-compat/.github/workflows/mod-compat-tally.yml` to `.github/workflows/mod-compat-tally.yml`
- `taom-compat/.github/ISSUE_TEMPLATE/mod-compat.yml` to `.github/ISSUE_TEMPLATE/mod-compat.yml` (the manual report form, labels `mod-compat` and `taom`; the tally counts it too)
- `taom-compat/MODS.md` to `MODS.md` (the first time; after that the tally writes it)
- `THIRD-PARTY-NOTICES.md` (the scripts and the form point to it for the CoopKeep MIT notice)

`compat/taom-mod-compat.json` is copied as before (docs/taom-modlist-publish.md). The tally
workflow also runs on a push of that file, so the counts and `MODS.md` are back a minute later.

**Labels to create there:** `mod-compat`, `mod-report`, `pending`, `suspicious`, `invalid`
(exists already), `spam` and `taom`.

**Token:** the worker's `GITHUB_TOKEN` needs **Issues: Read and write** on Coodsz/Coodsz-TAOM too
(edit the fine grained token's repository access). The workflows there use only the built in
`GITHUB_TOKEN`; under Settings, Actions, General, Workflow permissions must allow read and write
(or the per job permissions in the files are enough when the default is not restricted further).

Held TAOM reports:
`https://github.com/Coodsz/Coodsz-TAOM/issues?q=is%3Aopen+label%3Asuspicious+label%3Apending`.
To regenerate `MODS.md` here from the hand file without reports:
`node .github/scripts/tally-taom-compat.mjs --page-only --file src/TaomCoop.Core/Compat/taom-mod-compat.json --page taom-compat/MODS.md`.

## AD1259 community list (Coodsz/Coodsz-AD1259)

The AD1259 Client Launcher files its one click reports in
[Coodsz/Coodsz-AD1259](https://github.com/Coodsz/Coodsz-AD1259) (the worker's `AD1259_GITHUB_REPO`).
It works like the TAOM list, with the same script: `tally-taom-compat.mjs --list ad1259` counts the
approved reports into `compat/ad1259-mod-compat.json` and writes `MODS.md` there, the readable list
that the **Full AD1259 mod list** link in the AD1259 client and server tools opens
(`AD1259Stack.ModsPageUrl`).

**The list.** `compat/ad1259-mod-compat.json` (kind `CoodszAd1259Compat`, schema 1) holds the fixed
pack mods players rate, seeded from `AD1259Stack.ClientMods` without the base game modules and Coop,
in load order: `id`, `name`, `version`, `workshopId` or `nexusId`, `tag` (`pack`; `safe`, `risky` or
`broken` for an extra mod Coodsz checked with the pack) and `note`. The tally only adds `works`,
`broken`, `clientOnly` and `lastReport`, and a `community` array for reported mods that are not on
the list, the same as the TAOM list. No tool reads this file; the AD1259 count chips keep reading the
Coop and Friends list as before. When the pack changes, change `AD1259Stack.ClientMods` and the seed
here together (a test checks they match), then copy the seed and regenerate the page:
`node .github/scripts/tally-taom-compat.mjs --list ad1259 --page-only --file ad1259-compat/compat/ad1259-mod-compat.json --page ad1259-compat/MODS.md`.

**Files to copy there** (from this repository, same paths unless noted):

- `.github/scripts/tally-taom-compat.mjs`
- `.github/scripts/tally-mod-compat.mjs` and `.github/scripts/compat-records.mjs` (imported)
- `.github/scripts/auto-approve-reports.mjs`
- `.github/workflows/auto-approve-reports.yml` (unchanged; it calls `mod-compat-tally.yml`, which there is the AD1259 tally)
- `ad1259-compat/.github/workflows/mod-compat-tally.yml` to `.github/workflows/mod-compat-tally.yml`
- `ad1259-compat/.github/ISSUE_TEMPLATE/mod-compat.yml` to `.github/ISSUE_TEMPLATE/mod-compat.yml` (labels `mod-compat` and `ad1259`)
- `ad1259-compat/compat/ad1259-mod-compat.json` to `compat/ad1259-mod-compat.json` (the first time in full; after that carry hand edits over, the tally keeps the counts)
- `ad1259-compat/MODS.md` to `MODS.md` (the first time; after that the tally writes it)
- `THIRD-PARTY-NOTICES.md` (the scripts and the form point to it for the CoopKeep MIT notice)

**Labels to create there:** `mod-compat`, `mod-report`, `pending`, `suspicious`, `ad1259`, `invalid`
and `spam` (`invalid` is one of GitHub's default labels, so it may be there already).

**Token and Actions:** the worker's `GITHUB_TOKEN` needs **Issues: Read and write** on
Coodsz/Coodsz-AD1259 too (edit the fine grained token's repository access). Under Settings, Actions,
General, Workflow permissions must allow read and write, as in Coodsz-TAOM.

Held AD1259 reports:
`https://github.com/Coodsz/Coodsz-AD1259/issues?q=is%3Aopen+label%3Asuspicious+label%3Apending`.
AD1259 reports sent before the move stay in Coodsz-Coop-and-Friends with the `ad1259` label.
