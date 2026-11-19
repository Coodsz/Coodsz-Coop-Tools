# Coodsz TAOM tools history

This file lists every published version of the Coodsz TAOM tools (the TAOM Server Tool and Client Launcher, for The Age of Middle earth overhaul in Coop), newest first. It is for contributors who want to catch up on work from before the repo used PRs. The releases live in the public repo Coodsz/Coodsz-TAOM. The source is in this repository. Work from 2026-11-19 on goes through PRs, so this file ends at v1.2.16, published that day.

Each version has a release page link, a compare link to the version before it, and the source commit that set the version when a commit clearly names it. The compare link shows the public repo (the guide and the mod lists), not the source code. The source commits only start in October 2026, and a few versions have no commit that names them, so those have no source commit line. Versions up to v1.1.55 are numbered with the old tool names. From v1.2.0 the tools are the Server Tool and Client Launcher in one zip.

## v1.2.16 (2026-11-19)

- Server rules are on by default. The game pauses while a player joins, and offline players gain no skill XP. Turn them off in Advanced server rules on the Settings page, where you can also limit unstuck and protect player kingdoms from AI wars.
- Players who join use the host's MCM settings by themselves. Prepare server and Start server copy the MCM values from your game to the server, except the ones you changed in the tool. Change them in Advanced mod settings for players.
- The full TAOM download for Bannerlord 1.5.7 has its own TAOM.CoopCompat, so Install or repair TAOM and Start server leave it alone. These tools run TAOM 2.0.29.5 on Bannerlord 1.4.8 with Coop 0.1.6.
- Start server puts back TAOM.CoopCompat 0.3.25 files from the host kit when changed or missing. Console commands go one at a time and in order.
- The Saves page shows why Prepare server or Choose Coop 0.1.6 server stopped, and Windows blocked DLL notes open Settings. Start server and Test mods on the server stop with a plain line when War Sails is on.

[Release page](https://github.com/Coodsz/Coodsz-TAOM/releases/tag/v1.2.16) · [Compare with v1.2.15](https://github.com/Coodsz/Coodsz-TAOM/compare/v1.2.15...v1.2.16)

## v1.2.15 (2026-11-13)

- Clicking Connect again while the game starts no longer opens a second Bannerlord.
- Each Safe mode click joins with Plain TAOM, even two quick clicks in a row, and Safe mode says before the game starts that the host has to run Plain TAOM too.
- After a crash, the Client Launcher no longer blames Better Exception Window just because it loaded last.

[Release page](https://github.com/Coodsz/Coodsz-TAOM/releases/tag/v1.2.15) · [Compare with v1.2.14](https://github.com/Coodsz/Coodsz-TAOM/compare/v1.2.14...v1.2.15)

## v1.2.14 (2026-11-13)

- The license says exactly what counts as a paid server and rules out streams or videos that earn money, unless Coodsz says yes. The tools ask you to accept it once more.
- Every Coodsz module folder the Client Launcher puts in place has a `LICENSE.txt`. Send my logs and crash reports list your Coodsz module versions.

[Release page](https://github.com/Coodsz/Coodsz-TAOM/releases/tag/v1.2.14) · [Compare with v1.2.13](https://github.com/Coodsz/Coodsz-TAOM/compare/v1.2.13...v1.2.14) · Source commit 4f317d8f

## v1.2.13 (2026-11-12)

- Both tools come in Simplified Chinese, Korean and Japanese too.
- When Bannerlord is newer than 1.4.8, the tools warn in the Made for card and under Connect or Start server. It turns nothing off.
- The I accept button in the Coop server terms window shows in your language.
- A Coop folder the Client Launcher sets aside (a Coop nightly too) goes back into Modules when you close the launcher or on its next start after a crash.

[Release page](https://github.com/Coodsz/Coodsz-TAOM/releases/tag/v1.2.13) · [Compare with v1.2.12](https://github.com/Coodsz/Coodsz-TAOM/compare/v1.2.12...v1.2.13) · Source commit d6f37266

## v1.2.12 (2026-11-08)

- TAOM players can join again while Steam has Coop 0.1.7. A Get Coop 0.1.6 button on the Play page opens Coop's v0.1.6 release, and the Client Launcher leaves a Coop 0.1.6 in Modules alone.
- The Server Tool no longer says TAOM waits for a new TAOM.CoopCompat. Choose Coop 0.1.6 server says when the picked folder has the client instead of the dedicated server.
- Save Rescue keeps the map moving when the server's own hero is a prisoner.

[Release page](https://github.com/Coodsz/Coodsz-TAOM/releases/tag/v1.2.12) · [Compare with v1.2.11](https://github.com/Coodsz/Coodsz-TAOM/compare/v1.2.11...v1.2.12) · Source commit 8ac57111

## v1.2.11 (2026-11-07)

- Client Launcher fix for a crash on the campaign map caused by old map shader files. Connect moves them aside, and asks once if Windows needs permission.
- Settings says why some difficulty boxes are greyed out (`mod-config.json` lacks them until the server has started once), and Save settings says when they were not saved.

[Release page](https://github.com/Coodsz/Coodsz-TAOM/releases/tag/v1.2.11) · [Compare with v1.2.10](https://github.com/Coodsz/Coodsz-TAOM/compare/v1.2.10...v1.2.11) · Source commit 62c3ab92

## v1.2.10 (2026-11-05)

- The Server Tool can host TAOM again while Steam has Coop 0.1.7. On the Saves page, Choose Coop 0.1.6 server takes a Coop 0.1.6 dedicated server you downloaded and unpacked. Prepare server and Start server copy from it and never change it.
- Check mods shows the Coop line on the Play page when Coop is the only problem.
- On Coop 0.1.7 the status no longer stays on Joining, and Send my logs packs the newest three numbered Coop client logs.

[Release page](https://github.com/Coodsz/Coodsz-TAOM/releases/tag/v1.2.10) · [Compare with v1.2.9](https://github.com/Coodsz/Coodsz-TAOM/compare/v1.2.9...v1.2.10) · Source commit ed6b6a36

## v1.2.9 (2026-11-03)

- The tools say plainly that TAOM.CoopCompat 0.3.25 needs Coop v0.1.6, and that Steam's Coop v0.1.7 (since October 30) means TAOM in Coop waits for a TAOM.CoopCompat made for it. TAOM.CoopCompat comes from its own author.

[Release page](https://github.com/Coodsz/Coodsz-TAOM/releases/tag/v1.2.9) · [Compare with v1.2.8](https://github.com/Coodsz/Coodsz-TAOM/compare/v1.2.8...v1.2.9) · Source commit d668354c

## v1.2.8 (2026-11-03)

- The tools can show a notice from Coodsz at the top, in your language. Dismiss hides it until its words change.
- The search for the TAOM-Coop CompatOnly zips skips a folder Windows can't read instead of stopping with an error.

[Release page](https://github.com/Coodsz/Coodsz-TAOM/releases/tag/v1.2.8) · [Compare with v1.2.7](https://github.com/Coodsz/Coodsz-TAOM/compare/v1.2.7...v1.2.8) · Source commit 6577e959

## v1.2.7 (2026-11-01)

- A new look. Both tools have a sidebar with their pages. The Server Tool opens on a Dashboard with Mods and Made for cards. The Client Launcher opens on Play.
- The Client Launcher asks before setting another Coop aside (Set aside and play, Cancel, Don't ask again) and puts it back when you close the launcher.
- Start server puts back a TAOM.CoopCompat file that antivirus removed. The Server Tool asks before hosting a save that is earlier in the campaign than the one the server last ran.
- The Mods page is locked while the server runs, and the crash report bar waits for an automatic retry.

[Release page](https://github.com/Coodsz/Coodsz-TAOM/releases/tag/v1.2.7) · [Compare with v1.2.6](https://github.com/Coodsz/Coodsz-TAOM/compare/v1.2.6...v1.2.7) · Source commit 2701eb61

## v1.2.6 (2026-10-31)

- TAOM stays on Coop v0.1.6, because TAOM-Coop 0.3.25 is made for it. A newer Coop in Modules gets a note pointing to More tools and Use Workshop Coop 0.1.6.
- When the Workshop Coop is newer than v0.1.6, the tools say so instead of telling you to wait for Steam. The Coop row says whether Coop comes from Modules or the Workshop.

[Release page](https://github.com/Coodsz/Coodsz-TAOM/releases/tag/v1.2.6) · [Compare with v1.2.5](https://github.com/Coodsz/Coodsz-TAOM/compare/v1.2.5...v1.2.6) · Source commit ee11294e

## v1.2.5 (2026-10-30)

- My reports is a real button next to Send my logs that glows gold with a red count when Coodsz answers, and answers reach the tool much sooner.
- The crash screen and Send my logs spot five common setup problems (Smart App Control, leftover Common.dll or 0Harmony.dll, a DNS changer, Documents in OneDrive) and show the fix. Connect checks the bin folder for the two leftover files.
- Long sidebar button names fit in every language.

[Release page](https://github.com/Coodsz/Coodsz-TAOM/releases/tag/v1.2.5) · [Compare with v1.2.4](https://github.com/Coodsz/Coodsz-TAOM/compare/v1.2.4...v1.2.5) · Source commit ed523f7f

## v1.2.4 (2026-10-30)

- A red badge on My reports counts new answers, each report shows its status, and Did this fix it? buttons sit under each answer.

[Release page](https://github.com/Coodsz/Coodsz-TAOM/releases/tag/v1.2.4) · [Compare with v1.2.3](https://github.com/Coodsz/Coodsz-TAOM/compare/v1.2.3...v1.2.4) · Source commit c85288c5

## v1.2.3 (2026-10-30)

- Play stays on until you press Pause, also when every player is in a town or a battle.
- A Fix antivirus button, and a message that your antivirus most likely removed a missing file.
- Install update works without a hidden PowerShell script and puts the old version back on failure.
- Crash reports add a text crash stack. Save backups work for a campaign that lost its .json or .sav. A Donate button next to What's new.

[Release page](https://github.com/Coodsz/Coodsz-TAOM/releases/tag/v1.2.3) · [Compare with v1.2.2](https://github.com/Coodsz/Coodsz-TAOM/compare/v1.2.2...v1.2.3) · Source commit aa6e1a87

## v1.2.2 (2026-10-29)

- A Find Bannerlord button lets you pick the game folder.
- Answers to your reports show in the tool, with My reports keeping every sent report.
- A campaign that failed to load is never saved over. Links Windows can't open show a message instead of closing the tool.

[Release page](https://github.com/Coodsz/Coodsz-TAOM/releases/tag/v1.2.2) · [Compare with v1.2.1](https://github.com/Coodsz/Coodsz-TAOM/compare/v1.2.1...v1.2.2) · Source commit 5bbb09f1

## v1.2.1 (2026-10-28)

- TAOM's own party sizes and the Party settings count again with Improved Garrisons. The fix runs on each player's PC too, so players need the new Client Launcher.
- The CompatOnly zips are built into the tools, so there is nothing more to download.
- A child born to a player's character no longer stops the server. Buttons ask you to wait while Start server is getting ready, and a server command the server turned down no longer shows as done.

[Release page](https://github.com/Coodsz/Coodsz-TAOM/releases/tag/v1.2.1) · [Compare with v1.2.0](https://github.com/Coodsz/Coodsz-TAOM/compare/v1.2.0...v1.2.1) · Source commit ca18a03b

## v1.2.0 (2026-10-28)

- The two exe release. The server tool is now the Coodsz TAOM Server Tool and the client launcher keeps its name. Both come in one zip with the modules inside, and no .NET install is needed.
- Start server puts back StoryMode.dll, MCMv5.dll and other module files removed by antivirus. A save that is not a TAOM campaign is refused with one plain message.
- A new Advanced switch, Heavy troops count more toward party size (TAOM troop weight), off by default.
- Settings move to `%LOCALAPPDATA%\Coodsz\TAOM`, and each tool has its own icon.

[Release page](https://github.com/Coodsz/Coodsz-TAOM/releases/tag/v1.2.0) · [Compare with v1.1.55](https://github.com/Coodsz/Coodsz-TAOM/compare/v1.1.55...v1.2.0) · Source commit 1b0b811d

## v1.1.55 (2026-10-27)

- Install update removes only the files that belong to the tool you update. Each TAOM zip holds both TAOM tools, so updating one keeps the other.

[Release page](https://github.com/Coodsz/Coodsz-TAOM/releases/tag/v1.1.55) · [Compare with v1.1.54](https://github.com/Coodsz/Coodsz-TAOM/compare/v1.1.54...v1.1.55) · Source commit 3bc9aa74

## v1.1.54 (2026-10-27)

- Start server puts back `0Harmony.dll` or the GauntletUI files in the server's engine folder when antivirus removed them, after checking each against its known hash. If it can't, it names the missing files.
- A crash from missing files is no longer treated as a save load crash, so there is no load retry or Coop Save Rescue run on your save.

[Release page](https://github.com/Coodsz/Coodsz-TAOM/releases/tag/v1.1.54) · [Compare with v1.1.53](https://github.com/Coodsz/Coodsz-TAOM/compare/v1.1.53...v1.1.54) · Source commit 2f1be998

## v1.1.53 (2026-10-26)

- Start server puts back `BannerlordCoopServer.exe` and other server program files from the TAOM host kit after a hash check, and never touches the Data folder or your saves. Prepare server points you to Start server instead of leaving you stuck.
- Commands you send from the TAOM server tool keep letters like Ü or İ.
- The crash report window mentions the crash dump only when there is one.

[Release page](https://github.com/Coodsz/Coodsz-TAOM/releases/tag/v1.1.53) · [Compare with v1.1.52](https://github.com/Coodsz/Coodsz-TAOM/compare/v1.1.52...v1.1.53) · Source commit 62465132

## v1.1.52 (2026-10-26)

- Crash reports can add the newest crash dump, in a box that starts ticked.
- Mod rows on the Modules page show player reports with a Report link.
- Save backups wait until the server has finished writing the save, and a save with letters like Ü or İ in its name gets backed up too.
- Clearer crash messages, and a join password that is also a common word no longer mangles mod ids in the logs.

[Release page](https://github.com/Coodsz/Coodsz-TAOM/releases/tag/v1.1.52) · [Compare with v1.1.51](https://github.com/Coodsz/Coodsz-TAOM/compare/v1.1.51...v1.1.52) · Source commit fcf17503

## v1.1.51 (2026-10-25)

- Connect asks the server for an answer first with Direct. If nobody answers in 5 seconds, or the address is your own PC, router or VPN, it offers Steam lobby.
- The server tool turns Steam lobby on once when the internet provider shares one address between homes.
- Connect asks to close a Bannerlord that is still running, and a warning names a mod that sits in two Workshop items.

[Release page](https://github.com/Coodsz/Coodsz-TAOM/releases/tag/v1.1.51) · [Compare with v1.1.50](https://github.com/Coodsz/Coodsz-TAOM/compare/v1.1.50...v1.1.51) · Source commit 3382c009

## v1.1.50 (2026-10-25)

- A male child being born no longer stops the server. Coop Save Rescue fills in the missing name first.
- Crash reports and log zips keep version numbers like 3.0.3.0 readable while real addresses are still taken out.

[Release page](https://github.com/Coodsz/Coodsz-TAOM/releases/tag/v1.1.50) · [Compare with v1.1.49](https://github.com/Coodsz/Coodsz-TAOM/compare/v1.1.49...v1.1.50) · Source commit 6f571615

## v1.1.49 (2026-10-24)

- The server no longer stops when a clan is destroyed and one of its heroes can't be taken off a party's troop list. This happened about one game day after loading some TAOM saves. Coop Save Rescue skips that one change and logs it.

[Release page](https://github.com/Coodsz/Coodsz-TAOM/releases/tag/v1.1.49) · [Compare with v1.1.48](https://github.com/Coodsz/Coodsz-TAOM/compare/v1.1.48...v1.1.49) · Source commit 40038a25

## v1.1.48 (2026-10-24)

- A Full TAOM mod list link on the mods page opens the TAOM community list on GitHub.

[Release page](https://github.com/Coodsz/Coodsz-TAOM/releases/tag/v1.1.48) · [Compare with v1.1.47](https://github.com/Coodsz/Coodsz-TAOM/compare/v1.1.47...v1.1.48) · Source commit eacb35dc

## v1.1.47 (2026-10-24)

- Send crash report and Send my logs have a box for your Discord name above Send. The tool remembers it. Save a copy still works without a name.

[Release page](https://github.com/Coodsz/Coodsz-TAOM/releases/tag/v1.1.47) · [Compare with v1.1.46](https://github.com/Coodsz/Coodsz-TAOM/compare/v1.1.46...v1.1.47) · Source commit df232286

## v1.1.46 (2026-10-24)

- The TAOM tools get the crash screen the Coop tools have. It checks the logs against known crash causes, including a graphics driver error while the map terrain loads.
- A stop by TAOM.CoopCompat's startup check is no load crash. The tool says the save is fine and to press Start server again.
- Send my logs in the server tool also sends the CoopModPatch logs and the Improved Garrisons settings file.
- Smaller fixes. A warning on PCs with less than 16 GB of RAM when joining a big battles server, Save settings works with an empty Autosave minutes box, and popups open inside the screen.

[Release page](https://github.com/Coodsz/Coodsz-TAOM/releases/tag/v1.1.46) · [Compare with v1.1.45](https://github.com/Coodsz/Coodsz-TAOM/compare/v1.1.45...v1.1.46) · Source commit f08bac41

## v1.1.45 (2026-10-24)

- Players see TAOM's War of the Ring bar live. The server tool adds a small Coodsz War Sync to the server at every Start, which sends the war phase and momentum to players.
- Reports get optional Tell us what happened and Add screenshots (up to 5 pictures).
- Once the server is serving, the note under Start server says Server is ready. Players can join now.
- Save settings no longer stops on an empty number box, the client launcher opens inside small screens, and the old More cheats section is gone. Reporting a mod is easier to find.

[Release page](https://github.com/Coodsz/Coodsz-TAOM/releases/tag/v1.1.45) · [Compare with v1.1.44](https://github.com/Coodsz/Coodsz-TAOM/compare/v1.1.44...v1.1.45) · Source commit b9ec071a

## v1.1.44 (2026-10-23)

- Joining players can no longer attack a lord by trying to enlist. The client fix greys out the enlist option with a note that it is not supported in Coop yet.
- Settings sync checks less often for a mod you don't have, which removes game stutter.

[Release page](https://github.com/Coodsz/Coodsz-TAOM/releases/tag/v1.1.44) · [Compare with v1.1.43](https://github.com/Coodsz/Coodsz-TAOM/compare/v1.1.43...v1.1.44) · Source commit 53e12f3c

## v1.1.43 (2026-10-23)

- Connect shows one box with a Get them and connect button for missing mods. Start server shows a tidy card with one button when a save needs mods your setup leaves out.
- The Mods page shows ModderLords tested verdicts, and the Your mods card is calmer.
- A kingdom with no leader no longer crashes the server when a war is declared (seen on TAOM's War of the Ring day). Coop Save Rescue lets the war go ahead.

[Release page](https://github.com/Coodsz/Coodsz-TAOM/releases/tag/v1.1.43) · [Compare with v1.1.42](https://github.com/Coodsz/Coodsz-TAOM/compare/v1.1.42...v1.1.43) · Source commit 96082ea8

## v1.1.42 (2026-10-23)

- Coop Save Rescue runs on every Start server and Restart, with one more start after a crash on load and a check after Stop. Run Rescue save moved to the Logs page.
- Custom commands moved from the Cheats page to the Console page.
- Report a mod in one click, with a red bar after a crash that asks which mod broke, and report counts on every mod row.
- Crash reports replace log zips. You see every file, click Send, and get a short code to post in Discord. Send my logs is always on the main page.

[Release page](https://github.com/Coodsz/Coodsz-TAOM/releases/tag/v1.1.42) · [Compare with v1.1.41](https://github.com/Coodsz/Coodsz-TAOM/compare/v1.1.41...v1.1.42) · Source commit 0f4bef36

## v1.1.41 (2026-10-22)

- No more crash at launch after using Coop and Friends. The TAOM client puts back the Workshop copy of `Coop.Core.dll` too, since TAOM.CoopCompat refuses an edited file.
- Joining no longer gets stuck on Final Synchronization, and the server no longer stalls behind the server tool during a burst of log lines.
- The status note says Server is ready once the server is serving. A note says BattleSizeResized overrides the server's battle size.

[Release page](https://github.com/Coodsz/Coodsz-TAOM/releases/tag/v1.1.41) · [Compare with v1.1.40](https://github.com/Coodsz/Coodsz-TAOM/compare/v1.1.40...v1.1.41) · Source commit fe7bf53b

## v1.1.40 (2026-10-21)

- Rescue save fixes killed lords still marked alive, which crashed the server on every load. It marks them dead with the game's own function, and one bad hero no longer stops the whole save from loading.
- The release works with Bannerlord 1.4.8, TAOM 2.0.29.5, Workshop Coop 0.1.6 and TAOM.CoopCompat 0.3.25. TAOM 2.0.27 campaigns do not load on 2.0.29.5.

[Release page](https://github.com/Coodsz/Coodsz-TAOM/releases/tag/v1.1.40) · [Compare with v1.1.39](https://github.com/Coodsz/Coodsz-TAOM/compare/v1.1.39...v1.1.40) · Source commit e047032a

## v1.1.39 (2026-10-20)

- A new Cheats page in the server tool. Every cheat sends Coop's own command and shows the exact line.
- A much bigger Console page with live server output, recent commands and every Coop command by category, each with a How to use guide.
- The tools have a license. It shows once and one I agree covers all Coodsz tools.
- Check mods names a missing UIExtenderEx file, and Connect turns off its Compiled Prefabs for that game.

[Release page](https://github.com/Coodsz/Coodsz-TAOM/releases/tag/v1.1.39) · [Compare with v1.1.38](https://github.com/Coodsz/Coodsz-TAOM/compare/v1.1.38...v1.1.39) · Source commit c0760fa7

## v1.1.38 (2026-10-20)

- CoopModPatch works with Improved Garrisons on the server. Start server makes the server copy of Improved Garrisons from the client files when CoopModPatch is in your setup.
- Save logs for help unpacks and cleans the newest crash report zip. Steam lobby joins wait up to 30 seconds.
- The server tool's activity log says when two players join with the same Steam ID, and the client launcher notes files from a non Steam copy.

[Release page](https://github.com/Coodsz/Coodsz-TAOM/releases/tag/v1.1.38) · [Compare with v1.1.37](https://github.com/Coodsz/Coodsz-TAOM/compare/v1.1.37...v1.1.38) · Source commit 3c67c71d

## v1.1.37 (2026-10-19)

- Start server copies Improved Garrisons again when its server DLL is missing, so players no longer see "IG: no response from the server to the configuration request".

[Release page](https://github.com/Coodsz/Coodsz-TAOM/releases/tag/v1.1.37) · [Compare with v1.1.36](https://github.com/Coodsz/Coodsz-TAOM/compare/v1.1.36...v1.1.37) · Source commit 900aec72

## v1.1.36 (2026-10-19)

- A clearer Add mods to this campaign box that lists the new mods by name and says a backup is made first.
- Plainer wording in every language for the mods page, Save module config and Auto dependency order.

[Release page](https://github.com/Coodsz/Coodsz-TAOM/releases/tag/v1.1.36) · [Compare with v1.1.35](https://github.com/Coodsz/Coodsz-TAOM/compare/v1.1.35...v1.1.36) · Source commit f6ec809b

## v1.1.35 (2026-10-18)

- Save logs for help packs more of what support needs. TAOM's own stall reports, the logs of a TAOM server on the same PC, and every log of each game and server session, with sizes capped.
- The join password is taken out of the engine and watchdog logs.
- A yellow low memory warning under Connect, and a note in the server tool's log at Start server.

[Release page](https://github.com/Coodsz/Coodsz-TAOM/releases/tag/v1.1.35) · [Compare with v1.1.34](https://github.com/Coodsz/Coodsz-TAOM/compare/v1.1.34...v1.1.35) · Source commit 3689a212

## v1.1.34 (2026-10-18)

- Hover tips on every button, switch, check box, list and menu item. Remove on a saved cheat command asks before it deletes.

[Release page](https://github.com/Coodsz/Coodsz-TAOM/releases/tag/v1.1.34) · [Compare with v1.1.33](https://github.com/Coodsz/Coodsz-TAOM/compare/v1.1.33...v1.1.34) · Source commit 68621389

## v1.1.33 (2026-10-18)

- Mod setups. Plain TAOM stays the locked default, TAOM plus is a locked set of mods tested in Coop with TAOM, and you can make your own setup with Add mods. A Mod list opens the TAOM mod list in four groups with Add to setup.
- The launcher does the fiddly parts. A dependency fix before launch, Unblock DLLs by itself, load order sorting, and badges on each mod. Find the crashing mod and Safe mode help after a crash.
- One Install or repair TAOM button, a Low FPS fix switch, and a More tools menu.
- The server tool edits setups and runs a server with your extra mods, with Test mods on the server, Stop and Restart that wait for the whole save, a backup after every save, a loading bar, and a CPU and memory line.
- Easier joining with an address list, Copy invite, automatic router port and a Game speed card.

[Release page](https://github.com/Coodsz/Coodsz-TAOM/releases/tag/v1.1.33) · [Compare with v1.1.32](https://github.com/Coodsz/Coodsz-TAOM/compare/v1.1.32...v1.1.33) · Source commit 518c3611

## v1.1.32 (2026-10-17)

- The Report a problem button is gone from the client launcher. Use Save all logs to one zip.

[Release page](https://github.com/Coodsz/Coodsz-TAOM/releases/tag/v1.1.32) · [Compare with v1.1.31](https://github.com/Coodsz/Coodsz-TAOM/compare/v1.1.31...v1.1.32) · Source commit ce035bae

## v1.1.31 (2026-10-17)

- Check server in the client launcher gets a real answer from the Coop server, so a running server shows as online. When nothing answers, it says what to check.

[Release page](https://github.com/Coodsz/Coodsz-TAOM/releases/tag/v1.1.31) · [Compare with v1.1.30](https://github.com/Coodsz/Coodsz-TAOM/compare/v1.1.30...v1.1.31) · Source commit 4819f73c

## v1.1.30 (2026-10-17)

- Getting Coop 0.1.6 through Steam is more reliable. The client launcher always asks Steam to download it and gives Steam a few seconds to start an update.

[Release page](https://github.com/Coodsz/Coodsz-TAOM/releases/tag/v1.1.30) · [Compare with v1.1.29](https://github.com/Coodsz/Coodsz-TAOM/compare/v1.1.29...v1.1.30) · Source commit b65e9011

## v1.1.29 (2026-10-16)

- Rescue save is easier to follow, with five short steps and a ? button.

[Release page](https://github.com/Coodsz/Coodsz-TAOM/releases/tag/v1.1.29) · [Compare with v1.1.28](https://github.com/Coodsz/Coodsz-TAOM/compare/v1.1.28...v1.1.29) · Source commit 507ae3da

## v1.1.28 (2026-10-15)

- Rescue save is on the TAOM server tool next to Stop. It fixes a save that crashes on load, a frozen map, lords stuck in a battle that never ends, and lords stuck with an empty party.

[Release page](https://github.com/Coodsz/Coodsz-TAOM/releases/tag/v1.1.28) · [Compare with v1.1.27](https://github.com/Coodsz/Coodsz-TAOM/compare/v1.1.27...v1.1.28)

## v1.1.27 (2026-10-15)

- Fixed a village and town menu freeze when Coop sends a party without all its data. It rides in the module the launcher already adds, so shaders are not rebuilt.

[Release page](https://github.com/Coodsz/Coodsz-TAOM/releases/tag/v1.1.27) · [Compare with v1.1.26](https://github.com/Coodsz/Coodsz-TAOM/compare/v1.1.26...v1.1.27) · Source commit dd81493d

## v1.1.26 (2026-10-15)

- After each update, each tool shows a What's new window once, in your language.

[Release page](https://github.com/Coodsz/Coodsz-TAOM/releases/tag/v1.1.26) · [Compare with v1.1.25](https://github.com/Coodsz/Coodsz-TAOM/compare/v1.1.25...v1.1.26)

## v1.1.25 (2026-10-15)

- Campaign difficulty on the server tool's Settings page. Pick Very easy, Easy or Realistic for each of the 9 options, and players who join get the same values.

[Release page](https://github.com/Coodsz/Coodsz-TAOM/releases/tag/v1.1.25) · [Compare with v1.1.24](https://github.com/Coodsz/Coodsz-TAOM/compare/v1.1.24...v1.1.25) · Source commit 85213cf5

## v1.1.24 (2026-10-15)

- The tools speak 6 more languages. Arabic, German, French, Portuguese, Russian and Turkish, picked on the first start and changed with the 🌐 button. Log zips and problem reports stay in English.

[Release page](https://github.com/Coodsz/Coodsz-TAOM/releases/tag/v1.1.24) · [Compare with v1.1.23](https://github.com/Coodsz/Coodsz-TAOM/compare/v1.1.23...v1.1.24) · Source commit 442501a3

## v1.1.23 (2026-10-14)

- No more "incorrect password" popup when you Connect to a server with a password. The client launcher adds a small client module (Coodsz Join Password) that sends the right password, without changing `Coop.Core.dll`. The server does not need it.

[Release page](https://github.com/Coodsz/Coodsz-TAOM/releases/tag/v1.1.23) · [Compare with v1.1.22](https://github.com/Coodsz/Coodsz-TAOM/compare/v1.1.22...v1.1.23) · Source commit e30b4376

## v1.1.22 (2026-10-14)

- The server tool shows how far the map cache build is while the server starts. The first start of a new campaign can take up to an hour.

[Release page](https://github.com/Coodsz/Coodsz-TAOM/releases/tag/v1.1.22) · [Compare with v1.1.21](https://github.com/Coodsz/Coodsz-TAOM/compare/v1.1.21...v1.1.22) · Source commit a1ef304e

## v1.1.21 (2026-10-13)

- The server tool no longer closes right away when you open it after Prepare server. The 0.3.25 kit adds settings the tool did not expect.

[Release page](https://github.com/Coodsz/Coodsz-TAOM/releases/tag/v1.1.21) · [Compare with v1.1.20](https://github.com/Coodsz/Coodsz-TAOM/compare/v1.1.20...v1.1.21) · Source commit bb04d193

## v1.1.20 (2026-10-12)

- Tools for TAOM-Coop 0.3.25. Works with Bannerlord 1.4.8, TAOM 2.0.29.5, Workshop Coop 0.1.6 and TAOM.CoopCompat 0.3.25. TAOM 2.0.27 campaigns do not load on 2.0.29.5, so start a new campaign.

[Release page](https://github.com/Coodsz/Coodsz-TAOM/releases/tag/v1.1.20) · [Compare with v1.1.19](https://github.com/Coodsz/Coodsz-TAOM/compare/v1.1.19...v1.1.20) · Source commit c9570381

## v1.1.19 (2026-10-08)

- The free starter reward gives a reward code tied to your Steam account, to paste into a ticket on The Realm Discord.

[Release page](https://github.com/Coodsz/Coodsz-TAOM/releases/tag/v1.1.19) · [Compare with v1.1.18](https://github.com/Coodsz/Coodsz-TAOM/compare/v1.1.18...v1.1.19) · Source commit 88f7c53b

## v1.1.18 (2026-10-08)

- A Join my server page for The Realm in both tools, with the free starter reward claimed through a Discord ticket.

[Release page](https://github.com/Coodsz/Coodsz-TAOM/releases/tag/v1.1.18) · [Compare with v1.1.17](https://github.com/Coodsz/Coodsz-TAOM/compare/v1.1.17...v1.1.18)

## v1.1.17 (2026-10-04)

- Server tool. Save game, and Save all logs to one zip now includes the server console log and asks before adding crash dumps. Console Copy and Clear. The Add custom mod button is gone.
- Client. Report a problem, and an explanation of why the game closed before joining. Switch to Coop 0.1.5 subscribes to the Workshop item by itself. Buttons renamed to Direct, Switch to Coop 0.1.5 and Restore parked Coop.

[Release page](https://github.com/Coodsz/Coodsz-TAOM/releases/tag/v1.1.17) · [Compare with v1.1.16](https://github.com/Coodsz/Coodsz-TAOM/compare/v1.1.16...v1.1.17)

## v1.1.16 (2026-10-01)

- The client launcher Setup row lines up again, with the same button height and spacing.
- When a join fails because of Player Settlement or PSCoopBridge, the client says what to turn off or install.

[Release page](https://github.com/Coodsz/Coodsz-TAOM/releases/tag/v1.1.16) · [Compare with v1.1.15](https://github.com/Coodsz/Coodsz-TAOM/compare/v1.1.15...v1.1.16)

## v1.1.15 (2026-09-30)

- The client launcher keeps `client-action.log`, and Save All Logs To One Zip packs the full support set (client logs, crash reports, engine logs, TAOM.CoopCompat logs and the crash dump when present). Saves are left out.

[Release page](https://github.com/Coodsz/Coodsz-TAOM/releases/tag/v1.1.15) · [Compare with v1.1.14](https://github.com/Coodsz/Coodsz-TAOM/compare/v1.1.14...v1.1.15)

## v1.1.14 (2026-09-30)

- The server tool keeps `host-action.log`, and Open server logs is highlighted and selects the newest coop-server log plus that file.

[Release page](https://github.com/Coodsz/Coodsz-TAOM/releases/tag/v1.1.14) · [Compare with v1.1.13](https://github.com/Coodsz/Coodsz-TAOM/compare/v1.1.13...v1.1.14)

## v1.1.13 (2026-09-30)

- Closing the server tool and opening it again still shows a TAOM server that is running (SERVING or RUNNING with uptime).
- Unblock DLLs turns red until you click it when Windows blocked a DLL. The Modules tab shows how many mods are in the sealed list.

[Release page](https://github.com/Coodsz/Coodsz-TAOM/releases/tag/v1.1.13) · [Compare with v1.1.12](https://github.com/Coodsz/Coodsz-TAOM/compare/v1.1.12...v1.1.13)

## v1.1.12 (2026-09-29)

- A failed join says what to change. If the server rejects the module list, select the same locked setup and press Start server. If the Bannerlord build differs, update Workshop item 3770450698.

[Release page](https://github.com/Coodsz/Coodsz-TAOM/releases/tag/v1.1.12) · [Compare with v1.1.11](https://github.com/Coodsz/Coodsz-TAOM/compare/v1.1.11...v1.1.12)

## v1.1.11 (2026-09-29)

- Start repairs a server config whose saved numbers the server cannot read, so the save name and password are kept.

[Release page](https://github.com/Coodsz/Coodsz-TAOM/releases/tag/v1.1.11) · [Compare with v1.1.10](https://github.com/Coodsz/Coodsz-TAOM/compare/v1.1.10...v1.1.11)

## v1.1.10 (2026-09-29)

- Start and Save settings no longer stop when a server setting is missing, and a broken `mod-config.json` is repaired. Two Coop copies are still reduced to one.

[Release page](https://github.com/Coodsz/Coodsz-TAOM/releases/tag/v1.1.10) · [Compare with v1.1.9](https://github.com/Coodsz/Coodsz-TAOM/compare/v1.1.9...v1.1.10)

## v1.1.9 (2026-09-28)

- The update removes the previous program files after the new copy is in place. Saved paths and the password stay in the folder.

[Release page](https://github.com/Coodsz/Coodsz-TAOM/releases/tag/v1.1.9) · [Compare with v1.1.8](https://github.com/Coodsz/Coodsz-TAOM/compare/v1.1.8...v1.1.9)

## v1.1.8 (2026-09-28)

- Install update shows a progress bar, and the button says Installing.

[Release page](https://github.com/Coodsz/Coodsz-TAOM/releases/tag/v1.1.8) · [Compare with v1.1.7](https://github.com/Coodsz/Coodsz-TAOM/compare/v1.1.7...v1.1.8)

## v1.1.7 (2026-09-28)

- Unblock DLLs is the first Setup button, gold and pulsing until you click it. The first Connect waits for that click.
- Share mod list and Import mod list are gone from the TAOM client. Fix low FPS is green and Undo FPS fix is rose.

[Release page](https://github.com/Coodsz/Coodsz-TAOM/releases/tag/v1.1.7) · [Compare with v1.1.6](https://github.com/Coodsz/Coodsz-TAOM/compare/v1.1.6...v1.1.7)

## v1.1.6 (2026-09-28)

- The first version with an Install update button that replaces the program files and keeps saved paths and the password.
- Fast forward is a setting on Settings. Save keeps the server up and will not write over the last playable save. Cheats lists players from the live server log.

[Release page](https://github.com/Coodsz/Coodsz-TAOM/releases/tag/v1.1.6) · [Compare with v1.1.5](https://github.com/Coodsz/Coodsz-TAOM/compare/v1.1.5...v1.1.6)

## v1.1.5 (2026-09-28)

- Steam lobby join uses the lobby, and a typed server address is sent with the connect. Logs and Console tabs stay responsive.
- Players can be allowed to fast forward, with a second switch to keep normal speed during a battle.
- Cheats and Console say when a command was not sent because this window did not start the server.

[Release page](https://github.com/Coodsz/Coodsz-TAOM/releases/tag/v1.1.5) · [Compare with v1.1.4](https://github.com/Coodsz/Coodsz-TAOM/compare/v1.1.4...v1.1.5)

## v1.1.4 (2026-09-27)

- The server page is four steps, and step 3 builds the host folder once on this PC. The save shows the character, level, campaign day and mods.
- The server tool keeps one Bannerlord Coop folder. Start server does not start when UDP 4200 is already in use.

[Release page](https://github.com/Coodsz/Coodsz-TAOM/releases/tag/v1.1.4) · [Compare with v1.1.3](https://github.com/Coodsz/Coodsz-TAOM/compare/v1.1.3...v1.1.4)

## v1.1.3 (2026-09-27)

- Fix low FPS turns off TAOM's patch shield, and Undo FPS fix puts it back.
- Save All Logs To One Zip on the client and Save engine logs on the server tool. Unblock DLLs, Clear shader cache and a DLL load explanation in the server log.
- Use Coop 0.1.5 and Use latest Coop switch Coop folders. The client puts `Coop.Core.dll` back to the pinned 0.1.5 file if a password edit changed it.

[Release page](https://github.com/Coodsz/Coodsz-TAOM/releases/tag/v1.1.3) · [Compare with v1.1.2](https://github.com/Coodsz/Coodsz-TAOM/compare/v1.1.2...v1.1.3)

## v1.1.2 (2026-09-26)

- Use Coop 0.1.5 puts Coop 0.1.5 back into `Modules\Coop` when that folder is another version, and Use latest Coop puts the saved folder back.
- Save All Logs To One Zip under Check mods, Unblock DLLs, Clear shader cache, and a DLL load explanation in the server log.

[Release page](https://github.com/Coodsz/Coodsz-TAOM/releases/tag/v1.1.2) · [Compare with v1.1.1](https://github.com/Coodsz/Coodsz-TAOM/compare/v1.1.1...v1.1.2)

## v1.1.1 (2026-09-26)

- Start server only loads a save already in the server Game Saves folder. A single player save is not started until you click Name server copy.
- Use Coop 0.1.5 checks that it is already installed. The server tool reads less of the log, so the window hitches less.

[Release page](https://github.com/Coodsz/Coodsz-TAOM/releases/tag/v1.1.1) · [Compare with v1.1.0](https://github.com/Coodsz/Coodsz-TAOM/compare/v1.1.0...v1.1.1)

## v1.1.0 (2026-09-26)

- The client refuses a name in the address box and explains a home address, an address that is not port forwarded and 127.0.0.1. Connect raises Low terrain to Medium.
- The server tool copies missing .NET 6 Desktop files into the server folder on Start server.

[Release page](https://github.com/Coodsz/Coodsz-TAOM/releases/tag/v1.1.0) · [Compare with v1.0.0](https://github.com/Coodsz/Coodsz-TAOM/compare/v1.0.0...v1.1.0)

## v1.0.0 (2026-09-26)

- The first release. Made for TAOM 2.0.27 and Coop 0.1.5, and it did not work with the latest TAOM update yet. Players download the client launcher zip and the host also downloads the server tool zip. You also need Bannerlord 1.4.8, the TAOM 2.0.27 modules, Workshop Coop 0.1.5 and TAOM.CoopCompat 0.3.17.

[Release page](https://github.com/Coodsz/Coodsz-TAOM/releases/tag/v1.0.0)




