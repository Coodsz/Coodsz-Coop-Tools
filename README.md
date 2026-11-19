# Coodsz Coop and Friends (source)

Windows tools for Mount & Blade II: Bannerlord Coop: the Coop client launcher and server tool, the TAOM client launcher and server tool, the AD1259 client launcher and server tool, the Burning Empires client and server tools, and the Realm of Thrones client and server tools. Each game's release is one zip with its two exes. Player and host instructions are in `README.txt` (Coop and Friends), `Coodsz-TAOM-Instructions.txt` (TAOM), `Coodsz-BurningEmpires-Instructions.txt` (Burning Empires) and `Coodsz-RealmOfThrones-Instructions.txt` (Realm of Thrones). Releases: [Coodsz-Coop-and-Friends](https://github.com/Coodsz/Coodsz-Coop-and-Friends/releases), Coodsz-TAOM, Coodsz-BurningEmpires and Coodsz-RealmOfThrones.

Unzip each Coodsz zip into its own folder.

This is an independent project. It is not affiliated with TaleWorlds Entertainment, the Bannerlord Coop team, the TAOM team, the Burning Empires team, or the Realm of Thrones team. You need a legal Steam copy of Bannerlord.

## Programs

| Project | Program | What it is |
| --- | --- | --- |
| `src/CoodszCoopAndFriends` | Coodsz Coop Client Launcher.exe (was Coodsz Coop and Friends.exe and Coodsz and Friends.exe) | Client launcher for the vanilla map and More Nations Remastered. Builds the `BannerlordCoopLauncher` sources. Its **More Nations** switch (`LauncherSettings.MoreNations`, under Campaign map) took the place of the Coodsz and Friends build: on, a setup with no saved map starts on More Nations Remastered Map. A Coodsz and Friends install updates to this exe with the switch on, and `MoreNationsFoldIn` brings its settings into the client's folder once (a setup of the same name that differs comes in as `Name (More Nations)`, and the whole file is kept as `launcher.more-nations.json`). |
| `src/BannerlordCoopLauncher` | (sources) | The client launcher's sources, built by `src/CoodszCoopAndFriends`. |
| `src/CoodszHost` | Coodsz Coop Server Tool.exe (was CoodszHost.exe) | Dedicated server host: setups, saves, mods, MCM, players, cheats, player profiles, logs, Rescue save. Carries `DedicatedServerRepair`, `SaveRescue`, `ServerTools`, `PatchModules` and `ImageExporter` inside the exe and writes them out to `%LOCALAPPDATA%\Coodsz\Coop\payload\<version>` on first use. Every tool keeps its settings in `%LOCALAPPDATA%\Coodsz\<Product>`. |
| `compat/CoodszServerTools` | DedicatedServer.CoodszTools.dll | Server only module with the `coodsz.*` console commands behind the Cheats page, player profiles and Items window (hero, skills, equipment, inventory, army, gold, troops, items), and the read only `coodsz.world` and `coodsz.map` behind the Encyclopedia and Map pages. Coodsz Coop Server Tool adds it to every start; players do not need it. Build with `build.ps1`. |
| `compat/CoodszImageExporter` | CoodszImageExporter.dll | Game module for **Build pictures**: the player's own Bannerlord draws the item, troop and lord pictures into `%AppData%\Coodsz\images`. Nothing is shipped or downloaded. Build with `build.ps1`. |
| `compat/CoopSaveRescue` | CoopSaveRescue.dll | Server module (DedicatedServer.CoopSaveRescue, loaded on every start) that repairs saves that crash on load or freeze the map (saved crime menu, broken battle, hostile town, killed lord still alive in the save). Start server runs its safe fixes every time and the manual **Run Rescue save** runs all of them; `vanilla/` has install and remove scripts for a plain Coop server. |
| `src/TaomCoopLauncher` | Coodsz TAOM Client Launcher.exe | Client for TAOM-Coop 0.3.25 (TAOM 2.0.29.5, Workshop Coop 0.1.6): the sealed Plain TAOM list, the locked TAOM plus set, or an own mod setup. |
| `src/TaomCoopHost` | Coodsz TAOM Server Tool.exe (was Coodsz Dedicated TAOM Server Tool.exe) | Dedicated TAOM server tool. |
| `src/AD1259Host` | Coodsz AD1259 Server Tool.exe (was Coodsz AD1259 Server.exe) | The Coodsz Coop Server Tool source built with `AD1259` for Anno Domini 1259. |
| `src/AD1259Launcher` | Coodsz AD1259 Client Launcher.exe | Client launcher for Anno Domini 1259. |
| `src/BurningEmpiresLauncher` | Coodsz Burning Empires Client Launcher.exe | Client for Burning Empires 2.0.2 (Workshop Coop and the BUTR Workshop modules): the locked Burning Empires setup (Elephants included) or an own setup, and Install or repair Burning Empires from the player's own Nexus Mods downloads. |
| `src/BurningEmpiresHost` | Coodsz Burning Empires Server Tool.exe | Burning Empires server tool on the Workshop Coop dedicated server. Ships Burning Empires Coop Compat and UIExtenderEx 2.13.3 in `ServerAddOns`. |
| `compat/BurningEmpiresCoopCompat` | BurningEmpires.CoopCompat.dll | Game and server module that makes Burning Empires work in Coop. Both Burning Empires tools ship its committed `module` folder; rebuild it with `Build.cmd`. |
| `src/RealmOfThronesLauncher` | Coodsz Realm of Thrones Client Launcher.exe | Client for Realm of Thrones 8.1.2 (modules 8.1.8, map 8.1.8.2) with War Sails: **Connect** puts the RoT build of Coop it carries into Modules, with the locked Plain Realm of Thrones setup or an own setup. Realm of Thrones itself comes from Nexus Mods. |
| `src/RealmOfThronesHost` | Coodsz Realm of Thrones Server Tool.exe | Realm of Thrones server tool: **Prepare server** builds `C:\RotServer` from the Workshop Coop server, War Sails and the RoT build of Coop. Ships Realm of Thrones Coop Compat in `ServerAddOns`. |
| `compat/RealmOfThronesCoopCompat` | RealmOfThrones.CoopCompat.dll | Game and server module that makes Realm of Thrones work in Coop. Both Realm of Thrones tools ship its committed `module` folder; rebuild it with `Build.cmd`. |
| `src/BannerlordCoopLauncher.Core` | library | Shared logic: profiles, module resolving, Steam Workshop, launch, log zips, update check. |
| `src/TaomCoop.Core` | library | TAOM-only logic, shared by both TAOM tools and Coodsz Coop Server Tool. |
| `src/BurningEmpires.Core` | library | Burning Empires only logic (stack, load order, setups, server staging, install, saves), shared by both Burning Empires tools. |
| `src/RealmOfThrones.Core` | library | Realm of Thrones only logic (stack, the RoT build of Coop, setups, server build, mod list, releases), shared by both Realm of Thrones tools. |

`archive/` holds old experiments that are not built or shipped.

`assets/icons` holds each tool's banner icon (red Coop server tool, blue Coop client launcher, green TAOM, purple AD1259) as an SVG and the `.ico` the project sets as `ApplicationIcon`. After changing an SVG, run `dotnet run --project tools/MakeIcons -- assets/icons` to make the `.ico` files again (16, 32, 48 and 256 px; the 16 px image leaves out the rod so the C stays readable).

## What the client launchers do

- Find Bannerlord through Steam (app 261550), or use a folder you pick.
- Load a saved setup (mod list, order, campaign map). Locked setups keep a known working list.
- Check every enabled mod. Workshop mods are read from `steamapps/workshop/content/261550`; missing ones are subscribed and downloaded through Steam itself (the game's `steam_api64.dll`), with the Workshop page as a fallback.
- Park duplicate copies of a Workshop mod in the game Modules folder (`SubModule.xml` -> `SubModule.xml.coodsz-off`) so only one loads.
- Start the game to join a server (Steam lobby or direct address), or **Test to main menu** without joining, and explain a startup crash from the crash report and engine log.
- Send your logs to Coodsz with **Send my logs**, or **Send crash report** after a crash: your Windows name, PC name, Steam ids, addresses and passwords are taken out (logs can still show in game player names), you see every file first, and you get a short code like `CR-7KQ2M` to post in Discord. **Save a copy instead** in that window saves the same zip. In both windows you can also add a short **Tell us what happened** note (cleaned like the logs) and up to 5 screenshots (PNG or JPG, Ctrl+V pastes one; they are sent as they are, so check them for anything private).
- Fill in `127.0.0.1` and the server's port when the dedicated server runs on the same PC, hint at the host's address when Radmin VPN or Hamachi is installed, and open the shared join guide (`JoinGuideWindow`, text in `Core/Joining/JoinGuide.cs`) from the **?** next to Steam lobby and Direct. Connect in Direct mode first checks that the server answers (`Core/Joining/JoinPreflight.cs`) and warns when the address is this PC, a VPN gateway or the router (`OwnAddressCheck.cs`).
- Show a badge per mod: the community list (Works, Broken, Client only, Unknown) in Coop and Friends, the TAOM mod list in the TAOM client, plus this PC's own results (`%AppData%\Coodsz\mod-results.json`). The AD1259 tools leave the mod badges out.
- TAOM client only: mod setups (locked Plain TAOM and TAOM plus, own setups with Add mods and the Mod list window), Auto sort with the TAOM load order rules, the dependency fix for mods that ask for Harmony, ButterLib, UIExtenderEx or MCM, unblocking DLLs before every launch, Find the crashing mod and Safe mode.

## What the server tools do

- Coodsz Coop Server Tool (and the AD1259 server built from the same source) and the TAOM server tool list the addresses friends can use with Copy and **Copy invite**, map the server's UDP port on the router with UPnP or NAT-PMP while the server runs (`Core/Joining/RouterPort.cs`), and add the Windows Firewall rule after a confirm.
- They read the server phase from its `@DS@` state events (`Core/Server`), back up a save after every "world saved to" line (`Core/Saves`, restored with **Load backup** or **Restore save backup**), stop with the server's own `stop` after it saved, warn players before **Restart**, and show a CPU and RAM line.
- Coodsz Coop Server Tool and the AD1259 server: opt in **Restart the server if it crashes**, every Coop mod-config setting from one catalogue (`Core/Configuration`) kept per campaign save, and a SteamID64 only ban file.
- **Test mods on the server** (Coodsz Coop Server Tool and the TAOM server tool, not AD1259) runs each mod alone on a throwaway campaign or a copy of the save and keeps the results in `Data\modtest-results.json` (`TaomCoop.Core/ModLoadTest`).
- The TAOM server tool starts the Coodsz TAOM server for setups with extra mods (`TaomCoop.Core/TaomServerPath`); Plain TAOM keeps the sealed TAOM-Coop start.

Code ported from CoopKeep (MIT, Copyright (c) 2026 aderimo) keeps its header in every ported file; see `THIRD-PARTY-NOTICES.md`, which every exe carries inside it and shows under the license.

## How the host talks to the server

Commands go through a file queue that the dedicated server reads in both server builds: the host writes `server-data\console-queue\<id>.cmd` and the server answers in `server-data\console-results\<id>.result` with `OK|` or `ERROR|`. This works for any running server, so a reopened host window can still send commands, save, and kick. What the server prints is kept in `logs\host-console-<date>.log`.

## Build and test

```powershell
dotnet test tests/BannerlordCoopLauncher.Tests/BannerlordCoopLauncher.Tests.csproj -c Release
dotnet build BannerlordCoopLauncher.sln -c Release
```

The tests run on Windows in CI (`.github/workflows/tests.yml`). Tests marked `WindowsFact` are skipped elsewhere.

## Release

Set `ReleaseNotice.FriendsVersion` and the `<Version>` in `src/CoodszHost/CoodszHost.csproj` to the new number, then double-click `Make-Release.cmd`, or run:

```powershell
.\tools\Make-Release.ps1 -BundledMods "<folder with the non-Workshop mod folders>"
```

`Make-Release.cmd` takes the bundled mods from `artifacts\bundled-mods`, or from the v1.2.23 release folder when that is missing. The script runs the tests, publishes every program as one self-contained file, and writes `Coodsz-Coop-Tools-v<version>.zip` (a `Coodsz Coop Tools` folder with `Coodsz Coop Server Tool.exe` and `Coodsz Coop Client Launcher.exe`, nothing else), the temporary legacy `Coodsz-Coop-and-Friends-v<version>.zip` and `SHA256SUMS.txt` to `artifacts\release-<version>`. The legacy zips (here and for TAOM and AD1259) keep the old asset names and layout with the new builds under the old exe names, so tools installed before the two exe release can still update; each new build renames itself on its first start. The Coop legacy zip also ships the client build as `Coodsz and Friends.exe`, so Coodsz and Friends installs update to the client launcher, and each of its folders holds only its own exe. Add `-ArtifactsPath <folder>` to build and test into that folder. The client launcher only gets the mods listed in `BundledModules.ModuleIds`; Workshop mods are never packed, because a second copy crashes the game. Each run's output is saved to `artifacts\make-release-last.log`.

Add `-IncludeTaom` to also build the Coodsz-TAOM release in `artifacts\taom-release-<taom version>` (`ReleaseNotice.TaomVersion`): `Coodsz-TAOM-Tools-v<taom>.zip` (self-contained, single file, so players need no .NET install) with the two temporary legacy TAOM zips, both TAOM-Coop CompatOnly zips (the TAOM tools also carry them inside their exes), `LICENSE.txt` and its own `SHA256SUMS.txt`. They go on the Coodsz-TAOM release, not this one. The CompatOnly zips are not built: the script takes them from `artifacts\release-assets-cache` (or `-AssetCache`) and downloads any that are missing from the newest Coodsz-TAOM release that has them.

### Required release files

`tools/release-assets.json` lists every file each release must carry: the files the player guides (`Coodsz-TAOM-Instructions.txt`, `README.txt`, and the public repository READMEs, the AD1259 one included) send players to, with the exact SHA256 where the file is not built here. TAOM v1.1.41 to v1.1.44 shipped without the CompatOnly zips, so new players could not install. To stop that happening again:

1. `Make-Release.ps1`, `Make-AD1259-Release.ps1` and `Make-BurningEmpires-Release.ps1` stop with a list of what is missing or has the wrong SHA256 when a required file is not in the output folder.
2. The scripts also write `release-body.md` and `release-labels.json` to the output folder and print the three publish commands. `release-body.md` is the release text: the json's bold `downloadLine` naming the `Coodsz-<Product>-Tools` zip first, then `docs/release-notes/v<version>-<product>.md` without its leading comment. Publish with the printed `gh release create ... --notes-file release-body.md` command, which uploads every zip and `SHA256SUMS.txt` but not those two files.
3. Run `.\tools\Set-ReleaseLabels.ps1 -Repo <owner/name> -Tag <tag>` (add `-WhatIf` to only print). GitHub sorts files by name, so the legacy zips show above the Tools zip; their label from the json tells new players to skip them. It only sets labels, never names, because the updaters find their zip by name. Running it twice is safe.
4. After publishing and labelling, run `.\tools\Check-ReleaseAssets.ps1`. With no arguments it checks the latest release of all three repositories; `-Repo Coodsz/Coodsz-TAOM -Tag v1.1.45` checks one release. It needs the GitHub CLI (`gh`). It exits 1 and prints MISSING or WRONG for each problem, and a release does not count as done until it says **Release check passed**. Besides the files it fails when a legacy zip lacks its label, when the body does not start with the download line, or when the download picture (`images/Coodsz-<Product>-new-download.png`) does not show both the Server Tool and the Client Launcher banner colour from the icons.

The two exe zips (`Coodsz-Coop-Tools`, `Coodsz-TAOM-Tools`, `Coodsz-AD1259-Tools`) carry an `exactly` list in the json: the release scripts and `Check-ReleaseAssets.ps1` open them and fail when they hold anything but their two exes. The legacy zips carry `legacy: true` and a `holds` list with the old exe names the installed updaters look for. They are temporary: remove them from the json and the release scripts once players have moved off the old versions.

When a guide starts pointing players to a new release file, or the CompatOnly zips change, update `tools/release-assets.json` in the same change. A CompatOnly change also needs the new name and SHA256 in `TaomStack` and in `CoodszTaomCompatName` and `CoodszTaomCompatSha256` of both TAOM tool projects: the TAOM client launcher carries the CLIENT zip inside its exe and the server tool the HOST-OPERATOR zip (`src/TaomCompatZip.targets`), and a single file build stops when the zip is missing or has another hash. `TaomCompatZipTests` checks that all of them match. `ReleaseAssetsTests` fails when the TAOM guide names a CompatOnly zip the list does not have.

### Compatibility and mod lists

The community mod compatibility list (`compat-list/mods.json`), its issue form and its tally workflow run from the public [Coodsz-Coop-and-Friends](https://github.com/Coodsz/Coodsz-Coop-and-Friends) repository (branch `main`), not from this one. `docs/compat-list-publish.md` lists the files to copy there. The TAOM mod list is read from the Coodsz-TAOM repository; `docs/taom-modlist-publish.md` says which file to copy there.

### One click mod reports

Coop and Friends and the TAOM client can send mod reports without a GitHub account: a click on a community badge opens **Works**, **Broken** and **Works only with...** buttons, and the opt in **Share how your mods worked?** window (off until the player ticks *Ask me after a game closes or crashes*) opens at most once per run after a joined game closed or crashed. The reports go to a small Cloudflare Worker (`server/mod-reports-worker`, deploy steps in its README) whose address is the one constant `ModReports.Endpoint` in `src/BannerlordCoopLauncher.Core/Modules/ModReports.cs`. While that constant is empty, the badges open the GitHub issue form as before.

What a report holds: the mods the player rates (id, name, version, result, and for Works only with the other mod), the Coop and Bannerlord versions, the tool and its version, the mod ids and versions of the setup in load order, and a random install id made on that PC (`%AppData%\Coodsz\mod-reports.json`). No Steam id, name, folder path or address is sent; the tool drops any value that looks like one, and the worker refuses it. The window shows the exact JSON from the moment it opens and keeps it up to date with each pick, so one Send click sends what the player sees. The worker keeps only an HMAC of the install id, so each PC has one vote per mod version, and files each report as a GitHub issue labelled `pending`; it counts in the tally only after Coodsz removes that label. The tally then adds `testedWith`, `conflicts` and `needs` to the list entries, and the badges read like *Works for 12 players, conflicts with X*. TAOM reports are labelled `taom` and are left for the hand kept TAOM list.

The AD1259 tools release on their own with `.\tools\Make-AD1259-Release.ps1` (`ReleaseNotice.AD1259Version` and the `<Version>` in `src/AD1259Host/AD1259Host.csproj`). Every version bump adds an entry at the top of its list in `Core/Updates/WhatsNew.cs`, translated into the nine languages (`dotnet run --project tools/ExtractUiText -- <repo root> <keys.json>` must report 0 missing). Draft release notes and Discord posts go in `docs/release-notes`.

The Burning Empires tools release on their own with `.\tools\Make-BurningEmpires-Release.ps1` (`ReleaseNotice.BeVersion` and the `<Version>` in both `src/BurningEmpiresHost` and `src/BurningEmpiresLauncher` csproj files, framework dependent like the TAOM tools). It refuses to build without the official UIExtenderEx 2.13.3 DLL in `compat/BurningEmpiresCoopCompat/server-extra/UIExtenderEx-2.13.3`, and writes both zips, `LICENSE.txt`, `README.-.Instructions.txt` (from `Coodsz-BurningEmpires-Instructions.txt`) and `SHA256SUMS.txt` to `artifacts\be-release-<version>`. Since v1.0.0 is out, `Check-ReleaseAssets.ps1` without `-Repo` checks it with the others.

## Files not built from this repository

- `BannerlordCoop.RuntimeBootstrap.dll` (net6.0, SHA-256 `cff7a2456f4fcd6fdc6f6b430fbb315cad69fd425059882a875508e4f793441d`) is a prebuilt helper the dedicated server loads at start (`Start-ModdedServer.ps1`). It isn't in this repository: release builds take it from the private folder, `private/DedicatedServerRepair/BannerlordCoop.RuntimeBootstrap.dll`, and pack it into the DedicatedServerRepair payload next to the files from `src/CoodszHost/DedicatedServerRepair` (see Private files in `CONTRIBUTING.md`). It adds headless-module and campaign-weather safeguards and resolves managed DLLs from `BANNERLORD_DS_MANAGED_SEARCH_DIRECTORIES`. It was built from the RuntimeBootstrap project of the HexTool compatibility work for the coop server; its source is not in this repository. Replace it only with a build from that source.
- Coop 0.1.6 added a server EULA. `DedicatedServer.Core.dll` exits with code 6 until `CoopData\DedicatedServer\eula.txt` holds an acceptance. Coodsz Coop Server Tool shows `EULA.md` and `LICENSE.txt` from the server folder, and only after the operator presses I accept does it start that run with `DS_ACCEPT_EULA=true`; the server then writes `eula.txt` itself. The accepted terms hash is kept in the host settings (`AcceptedEulaSha256`), so the host asks again when the terms change. Never accept on the operator's behalf.
- `DedicatedServer.LauncherCheats.dll` (SHA-256 `42c1336bc0e3a9a65fd697b43a9cf2ba2ee1ea0d95919f5a2667da01ae5ab069`) and `DedicatedServer.LauncherBattleSize.dll` are prebuilt server submodules from the same HexTool server work. They aren't in this repository either: release builds take them from `private/DedicatedServerRepair/engine/Modules/DedicatedServer.Windows/bin/Win64_Shipping_Server/` and pack them into the DedicatedServerRepair payload at that path. The release scripts refuse to build when any of the three DLLs is missing or isn't the build whose SHA-256 is listed here. The Steam Workshop server does not include them. Coodsz Coop Server Tool copies them into the server and adds them to `DedicatedServer.Windows/SubModule.xml` on Start, because the console queue, live player list, bans, pause/resume and battle size all run inside `LauncherCheats`. `DedicatedServer.LauncherBattleSize.dll` is edited: when `BANNERLORD_DS_AUTO_PAUSE=1` (set by `Start-ModdedServer.ps1` from the host's Auto pause setting) it keeps the game's settlement and battle pause instead of removing it. The edit is made with `tools/patches/LauncherBattleSizeAutoPause.cs` from the original (SHA-256 `0468c55d7fc8100b42078ea8e3483a0b4a6cfda26c036fc096d06befe5ac20d7`); the edited file is `ea46daa410802b1c9afe327496a9cf2173d35c455f807f51c8ad2d20a384ac4f`. Its fast-forward cap is unchanged and only applies while client time controls are off.
- `src/CoodszHost/SaveRescue/DedicatedServer.CoopSaveRescue/bin/Win64_Shipping_Server/CoopSaveRescue.dll` is built from `compat/CoopSaveRescue` with `Build.sh`. Rebuild it from there after a source change.
- `src/CoodszHost/ServerTools/DedicatedServer.CoodszTools/bin/Win64_Shipping_Server/DedicatedServer.CoodszTools.dll` and `src/CoodszHost/ImageExporter/CoodszImageExporter/bin/Win64_Shipping_Client/CoodszImageExporter.dll` are built from `compat/CoodszServerTools` and `compat/CoodszImageExporter` with their `build.ps1`, which copies them there. Rebuild them after a source change.
- `compat/BurningEmpiresCoopCompat/server-extra/UIExtenderEx-2.13.3/Bannerlord.UIExtenderEx.dll` is BUTR's unchanged official UIExtenderEx 2.13.3 (LGPL v3, SHA-256 `4f2782840e51391d66b7d701962cb6202969d34c0949b8ec1591964f9c479f99`), which Burning Empires 2.0.2 needs on the dedicated server. Its license texts and source link are next to it (see `THIRD-PARTY-NOTICES.md`).
- The dedicated server, Coop, TAOM, Burning Empires and the bundled mods belong to their own authors and are not in this repository.

## License

The tools are free software under the GNU General Public License version 3, in `LICENSE`. Copyright (c) 2026 Coodsz. Every exe carries the text inside it, and the releases attach it as `LICENSE.txt`. The tools never ask anyone to agree to it. The License link at the bottom of each tool's sidebar shows it read only, with the third party notices under it (`Core/Legal/ToolLicense`, `UiLanguage.ShowLicense`). Code from other people keeps its own license (`THIRD-PARTY-NOTICES.md`).
