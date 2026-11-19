# Publishing the TAOM mod list

The TAOM client launcher and the TAOM server tool read the TAOM mod list (badges, the Mod list
window, the server load test) from the public repository
[Coodsz/Coodsz-TAOM](https://github.com/Coodsz/Coodsz-TAOM) (default branch `main`):

    https://raw.githubusercontent.com/Coodsz/Coodsz-TAOM/main/compat/taom-mod-compat.json

When that file cannot be read they try `https://bannerlord.eu/download/taom-mod-compat.json`,
then the last good download next to the tool, then the copy built into the tool. A list whose
`updated` date is older than the built in one is not used. See `TaomCompatList.OnlineUrl`.

At release, copy this file from this repository to the same name there:

- `src/TaomCoop.Core/Compat/taom-mod-compat.json` to `compat/taom-mod-compat.json`

Raise `updated` (YYYY-MM-DD) whenever the list changes, or tools with a newer built in copy keep theirs.

The copy there also carries the player counts (`works`, `broken`, `clientOnly`, `lastReport`, and the
`community` array) that its tally workflow writes. They are not kept here: copy the hand file over
as usual, and the tally (it runs on that push) counts every report again and writes them and
`MODS.md` back. See docs/compat-list-publish.md, TAOM community list.

## Results from the server load test

The TAOM server tool's "Test mods on the server" window keeps its results in
`Data\modtest-results.json`. "Copy results into the mod list file" writes
`Data\modtest-taom-mod-compat.json`: the current list with every passed mod set to source
`server-loads` (a mod Coodsz tested in Coop keeps its entry). Read it over, then copy it over
`src/TaomCoop.Core/Compat/taom-mod-compat.json` here and `compat/taom-mod-compat.json` there.

## Fields (schema 2)

- `source`: `coodsz-coop` (tested in Coop by Coodsz), `server-loads` (passed the automatic server
  load test), `taom-discord` (reported in TAOM singleplayer on the TAOM Discord or a TAOM community
  list, not tested in Coop), `taom-dev` (said by the TAOM developer Theboyys), `coodsz-notes`.
- `checked`: the day it was checked.
- `tag`: `safe` (only for coodsz-coop), `everyone`, `risky`, `broken`, `reported` (works in TAOM,
  not tested in Coop) or `loads` (loads on the server). Risky and broken keep the reason in `note`.

Tools from before schema 2 read the file too: they skip `source` and `checked` and show no badge
for `reported` and `loads`. Notes are plain English with no colons.
