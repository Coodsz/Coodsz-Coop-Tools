# Coodsz AD1259 tools history

This file lists every published version of the Coodsz AD1259 tools (the AD1259 Server Tool and Client Launcher, for the AD1259 overhaul in Coop), newest first. It is for contributors who want to catch up on work from before the repo used PRs. The releases live in the public repo Coodsz/Coodsz-AD1259. The source is in this repository. Work from 2026-11-19 on goes through PRs, so this file ends at v1.1.16.

Each version has a release page link, a compare link to the version before it, and the source commit that set the version when a commit clearly names it. The compare link shows the public repo (the guide and the mod lists), not the source code. The source commits only start in October 2026, and a few versions have no commit that names them, so those have no source commit line. Versions up to v1.0.34 are numbered with the old tool names. From v1.1.0 the tools are the Server Tool and Client Launcher in one zip.

## v1.1.16 (2026-11-13)

- The license says exactly what counts as a paid server and rules out streams or videos that earn money, unless Coodsz says yes. The tools ask you to accept it once more.
- Every Coodsz module folder the tools put in place has a `LICENSE.txt`. Send my logs and crash reports list your Coodsz module versions.

[Release page](https://github.com/Coodsz/Coodsz-AD1259/releases/tag/v1.1.16) · [Compare with v1.1.15](https://github.com/Coodsz/Coodsz-AD1259/compare/v1.1.15...v1.1.16) · Source commit 4f317d8f

## v1.1.15 (2026-11-12)

- Both tools come in Simplified Chinese, Korean and Japanese too.
- When Bannerlord is newer than 1.4.8, the tools warn in the Made for card and under Connect or Start server, with how to switch back in Steam. The Client Launcher still keeps Connect off on a newer game.
- The I accept button in the Coop server terms window shows in your language, and Show more Coop settings has the kingdom decision vote time.
- The tip on Copy setup says it copies the mods, ports and MCM, not the save and map.

[Release page](https://github.com/Coodsz/Coodsz-AD1259/releases/tag/v1.1.15) · [Compare with v1.1.14](https://github.com/Coodsz/Coodsz-AD1259/compare/v1.1.14...v1.1.15) · Source commit d6f37266

## v1.1.14 (2026-11-08)

- The tools take MCM v5.13.1 from the Steam Workshop and still take v5.12.3 from Nexus Mods, so Connect is no longer kept off with "Newer than this tool supports". Start server gives the server the MCM version your game loads, and every player needs that same version.
- The Logs page names the mod and both versions when a mod's version changed since the save, and shows its What happened, What to do and What not to do lines in your language.
- Save Rescue keeps the map moving when the server's own hero is a prisoner.

[Release page](https://github.com/Coodsz/Coodsz-AD1259/releases/tag/v1.1.14) · [Compare with v1.1.13](https://github.com/Coodsz/Coodsz-AD1259/compare/v1.1.13...v1.1.14) · Source commit 8ac57111

## v1.1.13 (2026-11-07)

- Client Launcher fix for crashes on the AD1259 map caused by old map shader files Bannerlord can't remove. Connect moves them aside, and asks once if Windows needs permission.
- The Server Tool only got a new version number.

[Release page](https://github.com/Coodsz/Coodsz-AD1259/releases/tag/v1.1.13) · [Compare with v1.1.12](https://github.com/Coodsz/Coodsz-AD1259/compare/v1.1.12...v1.1.13) · Source commit 62c3ab92

## v1.1.12 (2026-11-06)

- A new AD1259 campaign on Coop 0.1.7 no longer stops the server. The fixed AD1259 Coop Compatibility module v1.1.1 picks from a companion type that has companions, or skips the companion. Start server puts it in place of v1.1.0 on its own.
- The Client Launcher only got a new version number.

[Release page](https://github.com/Coodsz/Coodsz-AD1259/releases/tag/v1.1.12) · [Compare with v1.1.11](https://github.com/Coodsz/Coodsz-AD1259/compare/v1.1.11...v1.1.12)

## v1.1.11 (2026-11-05)

- The older Open Source set works too. Both tools take Armory 1.18, Saddlery 1.8 and Weaponry 1.8 from Nexus Mods next to the 2.0 set. The host and every player must use the same set, and the tools warn about a mix.
- When the dedicated server stops with a FATAL line, the Server Tool quotes it. Start server warns about a game Coop copied into the server's `DedicatedServer\engine\Modules\Coop` folder.
- A Use the Steam server button goes back to the Workshop server when a picked folder has an older Coop. The map box shows "(map files missing on the server)".
- On Coop 0.1.7 the Client Launcher status no longer stays on Joining, and Send my logs packs the newest three numbered Coop client logs.

[Release page](https://github.com/Coodsz/Coodsz-AD1259/releases/tag/v1.1.11) · [Compare with v1.1.10](https://github.com/Coodsz/Coodsz-AD1259/compare/v1.1.10...v1.1.11) · Source commit ed6b6a36

## v1.1.10 (2026-11-03)

- Coop v0.1.7 is on the Steam Workshop, so the tools no longer send you to Coop's GitHub release.
- The Workshop item's own `DedicatedServer` folder is the server to use, and the Server Tool finds it by itself.
- Start server copies `StoryMode.dll` into a Coop 0.1.7 server that leaves it out, which Improved Garrisons needs on a new campaign.

[Release page](https://github.com/Coodsz/Coodsz-AD1259/releases/tag/v1.1.10) · [Compare with v1.1.9](https://github.com/Coodsz/Coodsz-AD1259/compare/v1.1.9...v1.1.10) · Source commit d668354c

## v1.1.9 (2026-11-03)

- The tools can show a notice from Coodsz at the top, in your language. Dismiss hides it until its words change.
- A setup made with New setup starts again. Start server adds the AD1259 Coop Compatibility v1.1.0 module itself, and a new setup starts on the AD1259 map.
- A Download Coop server button under Advanced server folder, and a warning on the Mods page when the server's Coop is older than the game's.

[Release page](https://github.com/Coodsz/Coodsz-AD1259/releases/tag/v1.1.9) · [Compare with v1.1.8](https://github.com/Coodsz/Coodsz-AD1259/compare/v1.1.8...v1.1.9) · Source commit 6577e959

## v1.1.8 (2026-11-01)

- A new look. Both tools have a sidebar. The Server Tool opens on a Dashboard with Mods and Made for cards (Bannerlord, AD1259, RBM and Coop versions). The Client Launcher opens on Play.
- The mod list is locked while the server runs. Chip words are clearer (Always on, Wrong version, Bundled).
- The crash report bar waits for an automatic retry. The AD1259 community mod list covers Coop v0.1.7 as well as v0.1.6.

[Release page](https://github.com/Coodsz/Coodsz-AD1259/releases/tag/v1.1.8) · [Compare with v1.1.7](https://github.com/Coodsz/Coodsz-AD1259/compare/v1.1.7...v1.1.8) · Source commit 2701eb61

## v1.1.7 (2026-10-31)

- Start server checks the AD1259 set mods on the server and copies a mod again when server files are missing and this PC has the same version.
- A missing campaign map stops the start with a plain message. Before, the server started a plain Calradia world and every player who joined crashed.

[Release page](https://github.com/Coodsz/Coodsz-AD1259/releases/tag/v1.1.7) · [Compare with v1.1.6](https://github.com/Coodsz/Coodsz-AD1259/compare/v1.1.6...v1.1.7)

## v1.1.6 (2026-10-31)

- The AD1259 set takes Coop v0.1.7 and still takes v0.1.6. Check mods says what to rename or move when Coop sits in the wrong folder, and the tools unblock downloaded files.
- The Server Tool works with the Coop v0.1.7 dedicated server package. Steam advertise and Write server log start ticked, and Start server writes the Server password and Steam advertise into the server's settings.
- A new campaign stays new after a crash before its first save. Smithing stamina recovery starts at 0.6 on Coop v0.1.7. The two movement limit boxes are gone.

[Release page](https://github.com/Coodsz/Coodsz-AD1259/releases/tag/v1.1.6) · [Compare with v1.1.5](https://github.com/Coodsz/Coodsz-AD1259/compare/v1.1.5...v1.1.6) · Source commit ee11294e

## v1.1.5 (2026-10-30)

- When the AD1259 set is not complete, the Server Tool shows the version each mod needs, the one on the server and the one on this PC.
- Start server stops before adding a map mod to a campaign made on another map.
- My reports is a real button that glows gold with a red count when Coodsz answers, and answers reach the tool much sooner.
- Players' time buttons work on servers with an older settings file, console commands like save and status work, and the crash screen shows the fix for five common setup problems.

[Release page](https://github.com/Coodsz/Coodsz-AD1259/releases/tag/v1.1.5) · [Compare with v1.1.4](https://github.com/Coodsz/Coodsz-AD1259/compare/v1.1.4...v1.1.5) · Source commit ed523f7f

## v1.1.4 (2026-10-30)

- A red badge on My reports counts new answers, each report shows its status, and Did this fix it? buttons sit under each answer.

[Release page](https://github.com/Coodsz/Coodsz-AD1259/releases/tag/v1.1.4) · [Compare with v1.1.3](https://github.com/Coodsz/Coodsz-AD1259/compare/v1.1.3...v1.1.4) · Source commit c85288c5

## v1.1.3 (2026-10-30)

- The game keeps running after Resume game until you press Pause game.
- A Fix antivirus button, and Install update works without a hidden PowerShell script and puts the old version back on failure.
- Crash reports add a text crash stack, and save backups work for a campaign that lost its .json or .sav. A Donate button next to What's new.

[Release page](https://github.com/Coodsz/Coodsz-AD1259/releases/tag/v1.1.3) · [Compare with v1.1.2](https://github.com/Coodsz/Coodsz-AD1259/compare/v1.1.2...v1.1.3) · Source commit aa6e1a87

## v1.1.2 (2026-10-29)

- A Find Bannerlord button lets you pick the game folder.
- Answers to your reports show in the tool, and My reports keeps every sent report.
- A campaign that failed to load is never saved over, and the cheats link no longer closes the Server Tool.

[Release page](https://github.com/Coodsz/Coodsz-AD1259/releases/tag/v1.1.2) · [Compare with v1.1.1](https://github.com/Coodsz/Coodsz-AD1259/compare/v1.1.1...v1.1.2) · Source commit 5bbb09f1

## v1.1.1 (2026-10-28)

- When an AD1259 mod is newer than the tools support, they say so and tell you to update the tools first.
- A child born to a player's character no longer stops the server, and a server command the server turned down no longer shows as done.

[Release page](https://github.com/Coodsz/Coodsz-AD1259/releases/tag/v1.1.1) · [Compare with v1.1.0](https://github.com/Coodsz/Coodsz-AD1259/compare/v1.1.0...v1.1.1) · Source commit ca18a03b

## v1.1.0 (2026-10-28)

- The two exe release. Coodsz AD1259 Server is now the Coodsz AD1259 Server Tool and the client launcher keeps its name. Both come in one zip with the AD1259 modules inside.
- Settings move to `%LOCALAPPDATA%\Coodsz\AD1259` and each tool has its own icon. A damaged Steam library file no longer stops the tools from finding Bannerlord.
- Install update renames the server program, moves shortcuts and removes the old exe.

[Release page](https://github.com/Coodsz/Coodsz-AD1259/releases/tag/v1.1.0) · [Compare with v1.0.34](https://github.com/Coodsz/Coodsz-AD1259/compare/v1.0.34...v1.1.0) · Source commit 1b0b811d

## v1.0.34 (2026-10-27)

- Install update removes only the files that belong to the tool you update, so another Coodsz tool or your own files in the same folder stay. The update that installs this version still runs the old rule.

[Release page](https://github.com/Coodsz/Coodsz-AD1259/releases/tag/v1.0.34) · [Compare with v1.0.33](https://github.com/Coodsz/Coodsz-AD1259/compare/v1.0.33...v1.0.34) · Source commit 3bc9aa74

## v1.0.33 (2026-10-27)

- A new campaign name can use letters from any language. The tool checks it against the server folder's own start script and says so when an older script only takes A to Z.
- Start server no longer stops on an old server MCM when the Coop and Friends launcher on the same PC turned off the old Workshop MCM download.

[Release page](https://github.com/Coodsz/Coodsz-AD1259/releases/tag/v1.0.33) · [Compare with v1.0.32](https://github.com/Coodsz/Coodsz-AD1259/compare/v1.0.32...v1.0.33) · Source commit 2f1be998

## v1.0.32 (2026-10-26)

- Letters like Ü or İ show right in the server window. The crash report window mentions the crash dump only when there is one.

[Release page](https://github.com/Coodsz/Coodsz-AD1259/releases/tag/v1.0.32) · [Compare with v1.0.31](https://github.com/Coodsz/Coodsz-AD1259/compare/v1.0.31...v1.0.32) · Source commit 62465132

## v1.0.31 (2026-10-26)

- Automatic restart works. It saves, stops and starts the same save again after the hours you set, with a 30 second warning, and Restart no longer freezes the window.
- MCM comes from Nexus Mods (mod 612) now, because the official Workshop item was removed. Start server copies the MCM version the pack needs onto the server.
- Crash reports can add the newest crash dump. Each mod row shows player reports with a Report link. Save backups wait until the server has finished writing the save.

[Release page](https://github.com/Coodsz/Coodsz-AD1259/releases/tag/v1.0.31) · [Compare with v1.0.30](https://github.com/Coodsz/Coodsz-AD1259/compare/v1.0.30...v1.0.31) · Source commit fcf17503

## v1.0.30 (2026-10-26)

- Start server turns the whole AD1259 set back on. An extra SandBox map had turned off RBM, the armouries and other pack mods, so a new world started with 10 mods and could crash.
- Install update checks the download comes from the Coodsz GitHub page and matches the release before it replaces any file.

[Release page](https://github.com/Coodsz/Coodsz-AD1259/releases/tag/v1.0.30) · [Compare with v1.0.29](https://github.com/Coodsz/Coodsz-AD1259/compare/v1.0.29...v1.0.30) · Source commit 339ac086

## v1.0.29 (2026-10-25)

- Start server finds an extra campaign map inside the SandBox module and offers to move it aside to `DedicatedServer\removed-mods`. Before, an AD1259 server stopped with "Multiple active modules provide SceneObj\Main_map".

[Release page](https://github.com/Coodsz/Coodsz-AD1259/releases/tag/v1.0.29) · [Compare with v1.0.28](https://github.com/Coodsz/Coodsz-AD1259/compare/v1.0.28...v1.0.29) · Source commit 6ed0f980

## v1.0.28 (2026-10-25)

- Connect asks the server for an answer first with Direct, and offers Steam lobby if nobody answers in 5 seconds.
- The server tool turns Steam lobby on once when the internet provider shares one address between homes.
- Connect asks to close a Bannerlord that is still running, and a warning names a mod in two Workshop items. Logs from a silent exit code 1 include Windows event log entries.

[Release page](https://github.com/Coodsz/Coodsz-AD1259/releases/tag/v1.0.28) · [Compare with v1.0.27](https://github.com/Coodsz/Coodsz-AD1259/compare/v1.0.27...v1.0.28) · Source commit 3382c009

## v1.0.27 (2026-10-25)

- A male child being born no longer stops the server. Start server stops before the game loads when MCM is older than v5.12. Crash reports and log zips keep version numbers like 3.0.3.0 readable.

[Release page](https://github.com/Coodsz/Coodsz-AD1259/releases/tag/v1.0.27) · [Compare with v1.0.26](https://github.com/Coodsz/Coodsz-AD1259/compare/v1.0.26...v1.0.27) · Source commit 6f571615

## v1.0.26 (2026-10-24)

- A Full AD1259 mod list link on the mods page opens the AD1259 mod list on GitHub.

[Release page](https://github.com/Coodsz/Coodsz-AD1259/releases/tag/v1.0.26) · [Compare with v1.0.25](https://github.com/Coodsz/Coodsz-AD1259/compare/v1.0.25...v1.0.26) · Source commit eacb35dc

## v1.0.25 (2026-10-24)

- Send crash report and Send my logs have a box for your Discord name above Send. The tool remembers it, and Save a copy still works without a name.

[Release page](https://github.com/Coodsz/Coodsz-AD1259/releases/tag/v1.0.25) · [Compare with v1.0.24](https://github.com/Coodsz/Coodsz-AD1259/compare/v1.0.24...v1.0.25) · Source commit df232286

## v1.0.24 (2026-10-24)

- The crash screen checks the logs against a list of known crash causes and says what happened and how to fix it. Known causes that need no report show No need to report this.
- Exit code 1 with no error is explained as most likely closed from outside, and the crash report adds the Windows event log entries.

[Release page](https://github.com/Coodsz/Coodsz-AD1259/releases/tag/v1.0.24) · [Compare with v1.0.23](https://github.com/Coodsz/Coodsz-AD1259/compare/v1.0.23...v1.0.24) · Source commit 755c0071

## v1.0.23 (2026-10-24)

- Reports get optional Tell us what happened and Add screenshots (up to 5 pictures).
- Save settings no longer stops on an empty number box, the client launcher opens inside small screens, and the server tool stays smooth in a burst of log lines.
- The old More cheats section is gone from the Cheats page. Reporting a mod is easier to find, with a Report link on each pack mod.

[Release page](https://github.com/Coodsz/Coodsz-AD1259/releases/tag/v1.0.23) · [Compare with v1.0.22](https://github.com/Coodsz/Coodsz-AD1259/compare/v1.0.22...v1.0.23) · Source commit b9ec071a

## v1.0.22 (2026-10-23)

- Get all missing in the launcher subscribes to the missing Workshop mods of the pack and downloads them by itself. Nexus mods still open their page.
- Start server shows a tidy card with one button when a save needs mods your setup leaves out.
- The Mods page shows ModderLords tested verdicts, and the Your mods card is calmer.
- A kingdom with no leader no longer crashes the server when a war is declared. Coop Save Rescue lets the war go ahead.

[Release page](https://github.com/Coodsz/Coodsz-AD1259/releases/tag/v1.0.22) · [Compare with v1.0.21](https://github.com/Coodsz/Coodsz-AD1259/compare/v1.0.21...v1.0.22) · Source commit 96082ea8

## v1.0.21 (2026-10-23)

- Coop Save Rescue runs on every Start server and Restart as a server only module, with one more start after a crash on load and a check after Stop. Run Rescue save moved to the Logs page.
- Custom commands moved from the Cheats page to the Console page. Join a kingdom says up front that Coop refuses a clan below tier 2.
- Report a mod in one click, with a red bar after a crash that asks which mod broke, and report counts on every mod row.
- Crash reports replace log zips. You see every file, click Send, and get a short code to post in Discord.

[Release page](https://github.com/Coodsz/Coodsz-AD1259/releases/tag/v1.0.21) · [Compare with v1.0.20](https://github.com/Coodsz/Coodsz-AD1259/compare/v1.0.20...v1.0.21) · Source commit 0f4bef36

## v1.0.20 (2026-10-22)

- Joining no longer gets stuck on Final Synchronization. The client fix puts back the parties the server says are there and logs them to `CoodszJoinPassword.log`.
- The server no longer stalls behind the server window. Log lines are read right away and drawn in batches.

[Release page](https://github.com/Coodsz/Coodsz-AD1259/releases/tag/v1.0.20) · [Compare with v1.0.19](https://github.com/Coodsz/Coodsz-AD1259/compare/v1.0.19...v1.0.20) · Source commit fe7bf53b

## v1.0.19 (2026-10-21)

- Rescue save fixes killed lords still marked alive, which crashed the server on every load. One bad hero no longer stops the whole save from loading.

[Release page](https://github.com/Coodsz/Coodsz-AD1259/releases/tag/v1.0.19) · [Compare with v1.0.18](https://github.com/Coodsz/Coodsz-AD1259/compare/v1.0.18...v1.0.19) · Source commit e047032a

## v1.0.18 (2026-10-20)

- A new Cheats page in the server tool. Every cheat sends Coop's own command and shows the exact line, with Undo where it can.
- The tools have a license, shown once, and one I agree covers all Coodsz tools.
- Check mods names a missing UIExtenderEx file, and the server tool says when the Steam lobby did not start because Steam was not open or logged in.

[Release page](https://github.com/Coodsz/Coodsz-AD1259/releases/tag/v1.0.18) · [Compare with v1.0.17](https://github.com/Coodsz/Coodsz-AD1259/compare/v1.0.17...v1.0.18) · Source commit c0760fa7

## v1.0.17 (2026-10-20)

- The RBM text says RBM runs in each player's own game, not on the Coop server. The server tool no longer writes RBM settings on Start.
- A new Console page lists every Coop command with a How to use guide, Use example and Find ids.
- Steam lobby joins wait up to 30 seconds. The server log says when two players share one Steam ID, and the launcher notes files from a non Steam copy.

[Release page](https://github.com/Coodsz/Coodsz-AD1259/releases/tag/v1.0.17) · [Compare with v1.0.16](https://github.com/Coodsz/Coodsz-AD1259/compare/v1.0.16...v1.0.17) · Source commit 3c67c71d

## v1.0.16 (2026-10-19)

- Translation fixes only. The Settings dropdowns and their notes show in your language (the files the server reads are not changed), the number settings under Other explain what a higher or lower value does, and the mods page wording is clearer.

[Release page](https://github.com/Coodsz/Coodsz-AD1259/releases/tag/v1.0.16) · [Compare with v1.0.15](https://github.com/Coodsz/Coodsz-AD1259/compare/v1.0.15...v1.0.16) · Source commit f6ec809b

## v1.0.15 (2026-10-18)

- Save logs for help takes your join password out of the logs, including quoted passwords, settings files and zips in crash report folders, in the client launcher and the server tool.

[Release page](https://github.com/Coodsz/Coodsz-AD1259/releases/tag/v1.0.15) · [Compare with v1.0.14](https://github.com/Coodsz/Coodsz-AD1259/compare/v1.0.14...v1.0.15) · Source commit 714e1f08

## v1.0.14 (2026-10-18)

- RBM's campaign rules run in each player's own game, not on the Coop server. Connect also turns off RBM's Lords buy equipment in your own RBM settings, with a Keep RBM coop friendly box that is on by default.
- The server tool is unchanged apart from the version number.

[Release page](https://github.com/Coodsz/Coodsz-AD1259/releases/tag/v1.0.14) · [Compare with v1.0.13](https://github.com/Coodsz/Coodsz-AD1259/compare/v1.0.13...v1.0.14) · Source commit cc2f2081

## v1.0.13 (2026-10-18)

- Hover tips on every button, switch, check box, list and menu item. Remove on a saved cheat command asks first.
- A new RBM settings file carries the version RBM expects, so its Campaign part stays off. The server tool's campaign map texts name the AD1259 client launcher.

[Release page](https://github.com/Coodsz/Coodsz-AD1259/releases/tag/v1.0.13) · [Compare with v1.0.12](https://github.com/Coodsz/Coodsz-AD1259/compare/v1.0.12...v1.0.13) · Source commit 68621389

## v1.0.12 (2026-10-18)

- Easier joining. The server page lists the addresses friends can use, Copy invite writes a Discord message without the password, the router port can open automatically, and a guide covers each way to join.
- The server tool restarts the server if it crashes (optional), makes a backup after every save, lets Stop and Restart save first, and shows a CPU and memory line.
- Every Coop setting is on the Settings page and is kept for each campaign save. The client launcher gets Save logs for help.

[Release page](https://github.com/Coodsz/Coodsz-AD1259/releases/tag/v1.0.12) · [Compare with v1.0.11](https://github.com/Coodsz/Coodsz-AD1259/compare/v1.0.11...v1.0.12) · Source commit 518c3611

## v1.0.11 (2026-10-18)

- Battle size on the Settings page sets coop battles for everyone, from 200 to 1000, written to `mod-config.json` and applied after a restart.
- Start server fixes True or False written with capitals in `mod-config.json`, and settings now land inside the right block of the file.

[Release page](https://github.com/Coodsz/Coodsz-AD1259/releases/tag/v1.0.11) · [Compare with v1.0.10](https://github.com/Coodsz/Coodsz-AD1259/compare/v1.0.10...v1.0.11) · Source commit 9df39d79

## v1.0.10 (2026-10-17)

- The Report a problem button is gone from the client launcher. Use Save all logs to one zip.

[Release page](https://github.com/Coodsz/Coodsz-AD1259/releases/tag/v1.0.10) · [Compare with v1.0.9](https://github.com/Coodsz/Coodsz-AD1259/compare/v1.0.9...v1.0.10) · Source commit ce035bae

## v1.0.9 (2026-10-17)

- Check server gets a real answer from the Coop server, so a running server shows as online. When nothing answers, it says what to check.

[Release page](https://github.com/Coodsz/Coodsz-AD1259/releases/tag/v1.0.9) · [Compare with v1.0.8](https://github.com/Coodsz/Coodsz-AD1259/compare/v1.0.8...v1.0.9) · Source commit 4819f73c

## v1.0.8 (2026-10-16)

- Rescue save is easier to follow, with five short steps and a ? button.

[Release page](https://github.com/Coodsz/Coodsz-AD1259/releases/tag/v1.0.8) · [Compare with v1.0.7](https://github.com/Coodsz/Coodsz-AD1259/compare/v1.0.7...v1.0.8) · Source commit 507ae3da

## v1.0.7 (2026-10-15)

- Share mod list in the server tool saves each mod's Workshop link, so a player's import can download it.

[Release page](https://github.com/Coodsz/Coodsz-AD1259/releases/tag/v1.0.7) · [Compare with v1.0.6](https://github.com/Coodsz/Coodsz-AD1259/compare/v1.0.6...v1.0.7)

## v1.0.6 (2026-10-15)

- Fixed a village and town menu freeze when Coop sends a party without all its data. It rides in the module the launcher already adds, so shaders are not rebuilt.

[Release page](https://github.com/Coodsz/Coodsz-AD1259/releases/tag/v1.0.6) · [Compare with v1.0.5](https://github.com/Coodsz/Coodsz-AD1259/compare/v1.0.5...v1.0.6) · Source commit dd81493d

## v1.0.5 (2026-10-15)

- Rescue save frees parties tied to a battle that already ended and resets lords left with an empty party. No lord is killed.
- A What's new window shows once after each update, in your language.

[Release page](https://github.com/Coodsz/Coodsz-AD1259/releases/tag/v1.0.5) · [Compare with v1.0.4](https://github.com/Coodsz/Coodsz-AD1259/compare/v1.0.4...v1.0.5)

## v1.0.4 (2026-10-15)

- The tools speak 6 more languages. Arabic, German, French, Portuguese, Russian and Turkish, picked on the first start and changed with the 🌐 button. Log zips and problem reports stay in English.

[Release page](https://github.com/Coodsz/Coodsz-AD1259/releases/tag/v1.0.4) · [Compare with v1.0.3](https://github.com/Coodsz/Coodsz-AD1259/compare/v1.0.3...v1.0.4) · Source commit 442501a3

## v1.0.3 (2026-10-15)

- Rescue save starts again (it stopped with a module mismatch), finds battles where one side has no men left and closes them, and gets a How Rescue save works button.

[Release page](https://github.com/Coodsz/Coodsz-AD1259/releases/tag/v1.0.3) · [Compare with v1.0.2](https://github.com/Coodsz/Coodsz-AD1259/compare/v1.0.2...v1.0.3) · Source commit 7fc7a842

## v1.0.2 (2026-10-14)

- Server tool fixes. Auto pause works, Fast forward keeps the setting you pick, and the movement limits show and save Coop's default of 10 when empty.

[Release page](https://github.com/Coodsz/Coodsz-AD1259/releases/tag/v1.0.2) · [Compare with v1.0.1](https://github.com/Coodsz/Coodsz-AD1259/compare/v1.0.1...v1.0.2) · Source commit 5e166c48

## v1.0.1 (2026-10-14)

- Fixes the "incorrect password" popup when joining a server that has a password. The launcher adds a small client module (Coodsz Join Password) that sends the right password. The server does not need it.

[Release page](https://github.com/Coodsz/Coodsz-AD1259/releases/tag/v1.0.1) · [Compare with v1.0.0](https://github.com/Coodsz/Coodsz-AD1259/compare/v1.0.0...v1.0.1) · Source commit e30b4376

## v1.0.0 (2026-10-14)

- The first release. A client launcher and a dedicated server tool for Anno Domini 1259 1.4.6 with Workshop Coop 0.1.6 on Bannerlord 1.4.8. The launcher checks every mod and links each one, writes RBM's settings (Campaign off), and shows a box while the game loads. The first start can take 20 minutes or more.
- Bundles two AD1259 coop fix modules, AD1259 Coop Compatibility (server) and AD1259 Coop Client Stability (players), with their author's permission.

[Release page](https://github.com/Coodsz/Coodsz-AD1259/releases/tag/v1.0.0)



