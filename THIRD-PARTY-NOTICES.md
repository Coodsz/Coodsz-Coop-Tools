# Third party notices

The Coodsz tools are under the GNU General Public License version 3 (LICENSE).
They include code from the projects below. Each one keeps its own license. This
file ships in every release zip.

The pictures in the tools (the backdrops and Realm pictures under `src/*/Assets`, such as
`ad1259-backdrop.jpg`, `be-client-backdrop.png`, `rot-backdrop-ember-green.jpg`, `realm-*.jpg` and
`redesign-backdrop-*.jpg`) are Coodsz's own work and are under the GNU General Public License version 3
with the rest of the tools.

## ModderLords

- Project: https://github.com/PlueRyvius/ModderLords
- Copyright: PlueRyvius (Ryvius, @PlueRyvius)
- License: Ryvius relicensed the parts below under the GNU General Public License version 3 for the
  Coodsz tools, the same license as the rest of this repository. ModderLords itself is under the
  PolyForm Noncommercial License 1.0.0 (https://polyformproject.org/licenses/noncommercial/1.0.0).

Ryvius gave permission for his settings sync and server rules code to be reused in the Coodsz tools. Parts of it
were adapted to fit them, and every file that reuses it says so in its header. The adapted parts
are:

- `compat/CoodszSettingsSync/`: the settings sync the server module (DedicatedServer.CoodszTools)
  and the client add on (DedicatedServer.CoodszJoinPassword) load. The MCM reflection and the
  capture and apply of values (McmSettings), the value text (SettingsWire), the request and snapshot
  messages (SettingsSyncMessages), the Coop probe and the loader (CoopProbe), the server and client
  handlers (ServerSync, ClientSync), the live settings folder with host overrides (LiveChannel) and
  the reading of the players keep their own list (SettingsFiles) follow his ModderLords.CompatSync
  and ModderLords.CompatSync.Coop. The players keep their own settings list is his recipes.json
  `PlayersOwnSettings`, in his file shape.
- `src/CoodszHost/Services/ModSettingsLive.cs`, `McmKeepOwn.cs` and `MainWindow.ModSettings.cs`: the
  Mod settings area, after his live settings client, host overrides store and Mod settings tab.
- The matching tests in `tests/BannerlordCoopLauncher.Tests/SettingsSyncTests.cs`.
- `compat/CoodszServerTools/src/ServerRules.cs` and `ServerRulesRules.cs`: the offline XP freeze
  (a HeroDeveloper.AddSkillXp prefix for the heroes of offline players, the offline set rebuilt from
  Coop's player manager, and the pass for host edits) follows his OfflineFreeze in
  ModderLords.CompatSync.Coop. His gold prefix is not used.
- `compat/CoodszServerTools/src/KingdomProtection.cs` and the kingdom protection rule in
  `ServerRulesRules.cs`: the Always choice of player kingdom protection (a ConsiderWar prefix and a
  GetRandomWarDecision postfix on KingdomDecisionProposalBehavior, and the player kingdoms rebuilt from
  Coop's player manager) follows his PlayerKingdomProtection in ModderLords.CompatSync.Coop. His peace
  backstop is not used. The matching tests are in `tests/BannerlordCoopLauncher.Tests/ServerRulesTests.cs`.

He also gave permission to use his compat record format and his curated compatibility data, with
credit:

- Data source: the tools download his curated `src/ModderLords.Core/compat-db.json`
  (https://raw.githubusercontent.com/PlueRyvius/ModderLords/main/src/ModderLords.Core/compat-db.json)
  and show its verdicts on the Mods pages, marked "From ModderLords by Ryvius". Coodsz Host stages
  its `DefaultSettings` as host defaults. The reader is `src/BannerlordCoopLauncher.Core/Modules/CompatRecords.cs`.
- Format: `.github/scripts/compat-records.mjs` writes the Coodsz community results in his compat
  record format (`compat-list/compat-records.json`), and `.github/scripts/compat-record.schema.json`
  is a copy of his `docs/compat-record.schema.json`, used by `compat-records.test.mjs`.

Nothing was taken from his TAOM folder.

```
Required Notice: Copyright PlueRyvius (https://github.com/PlueRyvius/ModderLords)
```

## Bannerlord.UIExtenderEx 2.13.3 (Coodsz Burning Empires Server Tool only)

- Project: https://github.com/BUTR/Bannerlord.UIExtenderEx
- Copyright: BUTR (Bannerlord's Unofficial Tools & Resources)
- License: GNU Lesser General Public License v3.0, https://www.gnu.org/licenses/lgpl-3.0.html

Coodsz Burning Empires Server Tool carries the unchanged official `Bannerlord.UIExtenderEx.dll`
2.13.3 from https://github.com/BUTR/Bannerlord.UIExtenderEx/releases/tag/v2.13.3 inside the exe and
writes it out to `%LOCALAPPDATA%\Coodsz\BurningEmpires\payload\<version>\ServerAddOns\UIExtenderEx-2.13.3`,
next to the LGPL v3 text (`LICENSE-UIExtenderEx.txt`), the GPL v3
text it refers to (`GPL-3.0.txt`) and a link to the source (`SOURCE.txt`). The source code is at
https://github.com/BUTR/Bannerlord.UIExtenderEx/tree/v2.13.3. The tool does not link to it; it only
copies the file into the dedicated server's `Modules\BurningEmpires\bin\Win64_Shipping_Server`, because
Burning Empires 2.0.2 is built against that version. You may put your own build of the same version
from that source in its place. The repository copy is in
`compat/BurningEmpiresCoopCompat/server-extra/UIExtenderEx-2.13.3`.

## Harmony 2.4.2 (0Harmony.dll)

- Project: https://github.com/pardeike/Harmony
- Copyright: Copyright (c) 2017 Andreas Pardeike
- License: MIT, https://github.com/pardeike/Harmony/blob/master/LICENSE

The compat modules build against the Lib.Harmony 2.4.2 package, and Coodsz Coop Server Tool carries the
unchanged `0Harmony.dll` 2.4.2 in `src/CoodszHost/DedicatedServerRepair/engine/bin/Win64_Shipping_Server`
for the dedicated server.

## Mod Configuration Menu v5 5.12.3 (MCMv5.dll)

- Project: https://github.com/Aragas/Bannerlord.MBOptionScreen
- Copyright: Copyright (c) 2020 Vitaly Mikhailov, Liam
- License: MIT, https://github.com/Aragas/Bannerlord.MBOptionScreen/blob/dev/LICENSE.txt

Coodsz Coop Server Tool carries the unchanged `MCMv5.dll` 5.12.3 in
`src/CoodszHost/DedicatedServerRepair/engine/bin/Win64_Shipping_Server` for the dedicated server.

Both are under this MIT License:

```
Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```
