# TAOM tools guide for new contributors

Read `CONTRIBUTING.md` first. It has the rules (branches, PRs, never list). This
guide only explains how the TAOM tools work, so your agent knows where to look. Every path below exists in the
repo. If something here disagrees with the code, the code wins.

## 1. Map of the folders

| Path | What it is |
| --- | --- |
| `src/TaomCoop.Core` | Plain logic for both TAOM tools, no UI. Stack versions (`TaomStack.cs`), mod lists and rules (`TaomModList.cs`, `TaomModRules.cs`, `TaomLoadOrder.cs`), server paths (`TaomServerPath.cs`), save handling (`SaveHeader.cs`, `SaveSlot.cs`), startup checks (`TaomStartupCheck.cs`), server repair (`ServerProgramRepair.cs`), log packing (`HostLogPack.cs`, `ClientLogPack.cs`). Most testable changes belong here. |
| `src/TaomCoopHost` | The Server Tool (WPF, .NET 8). `MainWindow` is split into many partial files by topic, for example `MainWindow.ServerControl.cs`, `MainWindow.ServerLog.cs`, `MainWindow.Rescue.cs`, `MainWindow.Reports.cs`, `MainWindow.CrashReports.cs`. |
| `src/TaomCoopLauncher` | The Client Launcher (WPF, .NET 8). `MainWindow.xaml.cs` holds Connect. `MainWindow.Checks.cs` has the mod checks, `MainWindow.Setups.cs` the mod setups, `MainWindow.Crash.cs` Safe mode and Find the crashing mod. |
| `src/TaomFastForward` | Only build output (`obj`) is in this checkout, no source. Don't plan work around it until you have asked Coodsz. |
| `taom-compat/` | Not the module source. It holds `MODS.md` (the public TAOM mod list) and the GitHub issue form and tally workflow for it. The TAOM.CoopCompat module itself ships as the "CompatOnly" zips, which the tools carry inside their exes. `TaomStack.cs` pins their names and hashes. |
| `src/BannerlordCoopLauncher.Core` | Shared code for every Coodsz tool. Steam and game finding, launching, saves, the translator, reports (`Support/CrashReports.cs`, `Support/CrashSignatures.cs`, `Modules/ModReports.cs`) and What's new (`Updates/WhatsNew.cs`, `Updates/ReleaseNotice.cs`). |
| `compat/` | Modules that run inside the game or server. TAOM uses `compat/CoodszJoinPassword` (join password), `compat/CoodszServerTools`, `compat/CoodszWarSync` (the war bar follows the server), `compat/CoodszSettingsSync` (host settings reach players) and `compat/CoopSaveRescue`. |
| `tests/BannerlordCoopLauncher.Tests` | One test project for every tool. TAOM tests start with `Taom`, for example `TaomStartupCheckTests.cs`, `TaomServerProgramRepairTests.cs`, `TaomWarSyncTests.cs`, `TaomModRulesTests.cs`. |
| `tools/` | Scripts. `ExtractUiText` for translations, `Make-Release.ps1` and friends for releases. Not yours to change (see the rules file). |

The solution is `BannerlordCoopLauncher.sln` in the repo root.

## 2. What happens when the host presses Prepare server and Start server

All of this is in `src/TaomCoopHost/MainWindow.ServerControl.cs` (`Prepare_Click`, `StartServerCoreAsync`).

**Prepare server** (once per server folder)

1. Refuses to run while the tool is busy or a server runs.
2. Finds Bannerlord, then checks the save is a real TAOM `.sav` (`TaomServerPath.IsTaomCampaign`). A vanilla save is refused.
3. Picks the Coop 0.1.6 dedicated server. It is the Workshop copy, or the folder the host chose with **Choose Coop 0.1.6 server** (`KitCoopServer.Pick`). Hashes are checked against pinned values.
4. Refuses if the server folder already has files in it. That protects the campaign. Start server puts missing files back instead.
5. Finds or unpacks the host kit (`EnsureHostKit`, folder `HostKit-<version>` under `%LocalAppData%\TAOM-Coop`). The kit comes from the HOST-OPERATOR zip built into the exe (`TaomCompatZip`).
6. `RunPrepare` runs the kit's `Tools\Prepare-Host.ps1` with PowerShell. It copies the server, the game modules and the save into the server folder and data folder. Output lines go to the tool's log.
7. `ServerFramework.Ensure` checks the .NET the server needs. Then `WriteConfigFile` fills `server-config.json` in the data folder (save name, password, autosave minutes, Steam relay, log file) through `HostConfigEditor`.

**Start server**

1. `ConfirmHostModChanges` saves or drops unsaved mod changes on the Mods page.
2. `EnsureServerProgramAsync` is the repair. If `BannerlordCoopServer.exe` or other server files went missing (antivirus is the usual cause), `ServerProgramRepair` puts back only missing files from the host kit, the Workshop server and the game Modules folder, all checked against pinned hashes. It never touches saves or configs.
3. `InstallRescue`, `InstallWarSync` and `InstallServerTools` copy Coop Save Rescue, Coodsz War Sync and Coodsz Server Tools (the host rules and the mod settings for players, `TaomServerTools`) into the server's DedicatedServer.Windows on every start. Prepare server copies Coodsz Server Tools too.
4. Save checks. The save must sit in the server Game Saves folder, be a TAOM campaign, match the last hosted save (or the host confirms the switch), match the TAOM version and match the chosen mod setup. UDP 4200 must be free.
5. Config repair. `RepairModConfig` and `RepairServerConfig` (`MainWindow.Settings.cs`) fix the config files. `WarSailsCheck` can refuse a War Sails mix. `WriteConfig` writes `server-config.json` again.
6. Mods and load order. Plain TAOM starts the sealed TAOM Coop server. A setup with extra mods starts the Coodsz TAOM server with the setup's list (`TaomServerPath.Needed`, `PrepareCoodszServerAsync`, order from `TaomLoadOrder.ServerOrder`).
7. The host must accept the Coop server terms in `ConfirmWindow`. The tool never accepts for them (`AskServerTerms`).
8. The process starts with output and input redirected. Every line goes to a console log in the data folder's `logs` folder and to `CaptureLine`.

**Reading the log** (`MainWindow.ServerLog.cs`, `MainWindow.State.cs`)

- The server is ready when the log shows `[DedicatedServer] SERVING`, or the `@DS@` serving state event.
- `TaomStartupCheck` (in `TaomCoop.Core`) spots the lines where TAOM.CoopCompat stops its own start (the pre serving attestation, or a missing compat DLL that includes `FATAL` in the line). That is not a load crash, so the tool gives a plain message and no Save Rescue retry.
- Other exits go through `DecideLoadCrash` and `OnRescueExit` (`MainWindow.Rescue.cs`), which may retry once in a deeper save fix mode. A crash that stays offers `OfferServerCrashReport` (`MainWindow.CrashReports.cs`).

## 3. What happens when a player presses Connect

`ConnectOnceAsync` in `src/TaomCoopLauncher/MainWindow.xaml.cs`, in order.

1. Finds Bannerlord and checks a hand unzipped Coop folder (`CoopInstall.CheckModulesCoop`).
2. Keeps or drops unsaved setup changes, then `GetServerModsFirstAsync` offers to get any extra mods the server's setup needs.
3. Mod check. `ModuleProblems()` lists missing modules and the wrong Coop version. `ModChecksPass` (`MainWindow.Checks.cs`) runs the per mod checks and the broken mod question.
4. For Direct joins it checks the address and port, and `CheckDirectJoinAsync` makes sure the server answers. For Steam lobby joins it looks for the lobby for up to 30 seconds.
5. Before launch. It asks to close a running Bannerlord, warns about memory, unblocks DLLs, and clears map shaders the game can't remove.
6. The module list comes from `TaomLoadOrder.LaunchModules(setup, InstallJoinPassword())`. Plain TAOM gives the sealed TAOM order. The setup's extra mods sit in their places. The join password module (`ClientAddOns.JoinPasswordId`) goes last.
7. `TaomLaunch.Arguments` builds the game command line, and the game starts from `GameRoot.ExecutablePath` with the password in an environment variable.

## 4. Reports

- Both tools have **Send my logs**. After a crash they offer **Send crash report**. The window shows every file before it goes, and the tool takes out Windows names, PC names, Steam ids, addresses and passwords.
- Packing is `HostLogPack` and `ClientLogPack` in `TaomCoop.Core`. Sending is in `src/BannerlordCoopLauncher.Core/Support/CrashReports.cs` to `ModReports.CrashEndpoint`, a Cloudflare worker (`server/mod-reports-worker`, crash code in `src/crash.js`).
- The worker files the report as an issue in a private repo per game. TAOM goes to a private repository that only Coodsz and the helpers he invites can see. Reports hold players' logs, so never copy them anywhere else or quote player names, Steam ids or addresses in a PR.
- `compat-list/crash-signatures.json` holds the known crash patterns. `CrashSignatures.cs` matches a report against it, so the player sees a plain explanation and the worker can link a known cause. A new signature is added there with a test (see `TaomCrashTriageTests.cs`).
- Coodsz can reply to a report from GitHub, and the tool shows the reply in a window (`ReportReplies.cs`, `ReportReplyWindow.cs`).
- Mod badge reports ("Works", "Broken") go to the same worker through `ModReports.Endpoint`. TAOM ones are labelled `taom`.

## 5. Building and testing

From the repo root (these are the commands in `README.md`):

```powershell
dotnet build BannerlordCoopLauncher.sln -c Release
dotnet test tests/BannerlordCoopLauncher.Tests/BannerlordCoopLauncher.Tests.csproj -c Release
```

- Windows only. WPF tools won't build elsewhere. CI runs the same tests (`.github/workflows/tests.yml`).
- To run only TAOM tests use a filter, for example `--filter "FullyQualifiedName~Taom"`. Run the full suite before the PR, because shared code is tested together.
- Tests link source files from `src` and `compat` straight into the test project, so a new file a test needs may have to be added to `BannerlordCoopLauncher.Tests.csproj`.
- Player text goes through `Translator.T` and lives in nine files in `src/BannerlordCoopLauncher.Core/Localization/Languages` (`ar`, `de`, `fr`, `ja`, `ko`, `pt`, `ru`, `tr`, `zh-CN`). Run `dotnet run --project tools/ExtractUiText -- <repo root> <keys.json>`. It writes `keys.missing-<code>.json` for strings a language lacks. The release check wants 0 missing.

**Live testing safely**

- Never point the tool at Coodsz's real game or his real server folder. Use your own copy of Bannerlord and your own scratch folder for the server root and the data root (both are settings in the Server Tool).
- Test on this PC only. Host and client both on `127.0.0.1`, UDP port 4200 free. Don't open firewall or router rules for a test.
- Use a fresh TAOM campaign save, not a save you care about. Check that no Bannerlord or dedicated server is already running before you start.
- Prepare server, Start server and Connect change files in the game's `Modules` folder and the tool's data. Back up the `Modules` folder first and put it back after.
- If you can't live test a change that touches the game, say so in the PR. Coodsz will test it.

## 6. After Coodsz merges

You stop here. Coodsz does the rest.

1. Version numbers go up (`ReleaseNotice.TaomVersion` in `Updates/ReleaseNotice.cs`) and a What's new entry is added at the top of the list in `Updates/WhatsNew.cs`, translated into all nine languages.
2. `tools/Make-Release.ps1 -IncludeTaom` runs the tests and builds the TAOM release (`Coodsz-TAOM-Tools-v<version>.zip` with both exes, plus the CompatOnly zips and `SHA256SUMS.txt`).
3. `tools/release-assets.json` lists the files each release must carry, and `tools/Check-ReleaseAssets.ps1` checks the published release.
4. Signing and publishing to the public `Coodsz/Coodsz-TAOM` repo are Coodsz only. Players then get the update notice inside the tools.

## What to read first for your first fix

1. The partial `MainWindow.*.cs` file for the screen you are changing.
2. The matching `TaomCoop.Core` class, and the `Taom*Tests.cs` file next to it.
3. `README.md` for the host console queue and the release details.
