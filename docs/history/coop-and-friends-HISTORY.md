# Coodsz Coop and Friends history

This file lists every published version of the Coodsz Coop tools (first Coodsz Coop and Friends, from v1.3.0 the Coop Server Tool and Coop Client Launcher), newest first. It is for contributors who want to catch up on work from before the repo used PRs. The releases live in the public repo Coodsz/Coodsz-Coop-and-Friends. The source is in this repository. Work from 2026-11-19 on goes through PRs, so this file stops at v1.3.15.

Each version has a release page link, a compare link to the version before it, and the source commit that set the version when a commit clearly names it. The compare link shows the public repo (the guide and the mod lists), not the source code. The source commits only start at v1.2.23, so older versions have none. Three standalone module releases sit in the same repo (Coop Save Rescue v1.0.0 and v1.1.0, and Menu Guard v1.0.0). They are listed by date among the tool versions, and their compare links go to the earlier release of the same module.

## v1.3.15 (2026-11-13)

- The license now says exactly what counts as a paid server and rules out streams or videos that earn money, unless Coodsz says yes. Both tools ask you to accept it once more.
- Every Coodsz module folder the tools put in place now has a `LICENSE.txt`.
- Send my logs and crash reports now list the versions of your Coodsz modules.

[Release page](https://github.com/Coodsz/Coodsz-Coop-and-Friends/releases/tag/v1.3.15) · [Compare with v1.3.14](https://github.com/Coodsz/Coodsz-Coop-and-Friends/compare/v1.3.14...v1.3.15) · Source commit 4f317d8f

## v1.3.14 (2026-11-12)

- Both tools come in Simplified Chinese, Korean and Japanese too.
- When Bannerlord is newer than 1.4.8, the tools warn under Connect or Start server and in the Server Tool's Versions card, with how to switch back in Steam. It turns nothing off.
- The I accept button in the Coop server terms window shows in your language.
- Show more Coop settings has the kingdom decision vote time, and on a Coop nightly server also the AI party peace time after a battle, clans when a kingdom is destroyed, and Preferred battle hosts. Settings the server's Coop is too old for are greyed out and never written to `mod-config.json`.

[Release page](https://github.com/Coodsz/Coodsz-Coop-and-Friends/releases/tag/v1.3.14) · [Compare with v1.3.13](https://github.com/Coodsz/Coodsz-Coop-and-Friends/compare/v1.3.13...v1.3.14) · Source commit d6f37266

## v1.3.13 (2026-11-11)

- Players can recruit their own My Little Warband troops in Coop. The server and every player need CoopModPatch and My Little Warband. Gold is charged at the normal troop's price.
- The tip on the Copy setup button now says it copies the mods, ports and MCM, not the save and map.
- The Client Launcher only got a new version number.

[Release page](https://github.com/Coodsz/Coodsz-Coop-and-Friends/releases/tag/v1.3.13) · [Compare with v1.3.12](https://github.com/Coodsz/Coodsz-Coop-and-Friends/compare/v1.3.12...v1.3.13) · Source commit ab4773fd

## v1.3.12 (2026-11-08)

- A campaign played on the Coop nightly no longer disappears from the saves after its first stop.
- The Logs page names the mod and both versions when a mod's version changed since the save, and shows its What happened and What to do lines in your language.
- Save Rescue keeps the map moving when the server's own hero is a prisoner (seen with TAOM).
- The Client Launcher only got a new version number.

[Release page](https://github.com/Coodsz/Coodsz-Coop-and-Friends/releases/tag/v1.3.12) · [Compare with v1.3.11](https://github.com/Coodsz/Coodsz-Coop-and-Friends/compare/v1.3.11...v1.3.12) · Source commit 8ac57111

## v1.3.11 (2026-11-07)

- Client Launcher fix for a crash on the campaign map caused by old map shader files Bannerlord can't remove. Connect moves them aside, and asks once if Windows needs permission.
- The Server Tool only got a new version number.

[Release page](https://github.com/Coodsz/Coodsz-Coop-and-Friends/releases/tag/v1.3.11) · [Compare with v1.3.10](https://github.com/Coodsz/Coodsz-Coop-and-Friends/compare/v1.3.10...v1.3.11) · Source commit 62c3ab92

## v1.3.10 (2026-11-05)

- When the dedicated server stops with a FATAL line, the Server Tool quotes that line.
- Start server warns when a game Coop was copied into the server's own `DedicatedServer\engine\Modules\Coop` folder.
- A Use the Steam server button on the Mods page goes back to the Workshop server when your picked server folder has an older Coop.
- The map box shows a map with "(map files missing on the server)" instead of staying blank.
- On Coop 0.1.7 the Client Launcher status no longer stays on Joining, and Send my logs packs the newest three numbered Coop client logs.

[Release page](https://github.com/Coodsz/Coodsz-Coop-and-Friends/releases/tag/v1.3.10) · [Compare with v1.3.9](https://github.com/Coodsz/Coodsz-Coop-and-Friends/compare/v1.3.9...v1.3.10) · Source commit ed6b6a36

## v1.3.9 (2026-11-03)

- Coop v0.1.7 is on the Steam Workshop, so the tools no longer send you to Coop's GitHub release.
- The Workshop item's own `DedicatedServer` folder is the server to use, and the Server Tool finds it by itself.
- Start server copies `StoryMode.dll` into a Coop 0.1.7 server that leaves it out, which Improved Garrisons needs on a new campaign.

[Release page](https://github.com/Coodsz/Coodsz-Coop-and-Friends/releases/tag/v1.3.9) · [Compare with v1.3.8](https://github.com/Coodsz/Coodsz-Coop-and-Friends/compare/v1.3.8...v1.3.9) · Source commit d668354c

## v1.3.8 (2026-11-03)

- The tools can show a notice from Coodsz at the top, in your language. Dismiss hides it until its words change.
- A Download Coop server button under Advanced server folder on the Settings page opens Coop's dedicated server download, or the release page if the link can't be read.
- The Mods page warns under Active Coop when the game has a newer Coop than the server runs.

[Release page](https://github.com/Coodsz/Coodsz-Coop-and-Friends/releases/tag/v1.3.8) · [Compare with v1.3.7](https://github.com/Coodsz/Coodsz-Coop-and-Friends/compare/v1.3.7...v1.3.8) · Source commit 6577e959

## v1.3.7 (2026-11-01)

- A new look. Both tools have a sidebar. The Server Tool opens on a Dashboard, the Client Launcher on Play.
- The mod list is locked while the server runs, so the server keeps the mods the players joined with.
- Dashboard cards for Mods and Versions, and clearer chip words (Always on, Needs update, Wrong version, Missing on Workshop, Coodsz module).
- The Send server crash report bar waits for the automatic retry of a load crash.
- Smaller fixes for hosts with no mod settings to share, and for My reports after the PC clock was set back.

[Release page](https://github.com/Coodsz/Coodsz-Coop-and-Friends/releases/tag/v1.3.7) · [Compare with v1.3.6](https://github.com/Coodsz/Coodsz-Coop-and-Friends/compare/v1.3.6...v1.3.7) · Source commit 2701eb61

## v1.3.6 (2026-10-31)

- The tools work with Coop v0.1.7 installed by hand as `Modules\Coop`, and the v0.1.7 dedicated server package.
- Check mods says what to rename or move when Coop sits in the wrong folder.
- The Client Launcher unblocks Coop's downloaded files on Check mods and before the game starts, and the Server Tool does the same for the server's files on Start server.
- Steam advertise and Write server log start ticked, and Start server writes the Server password and Steam advertise into the server's settings.
- A new campaign stays new after a crash before its first save. Smithing stamina recovery starts at 0.6 on a Coop v0.1.7 server. The two movement limit boxes are gone.

[Release page](https://github.com/Coodsz/Coodsz-Coop-and-Friends/releases/tag/v1.3.6) · [Compare with v1.3.5](https://github.com/Coodsz/Coodsz-Coop-and-Friends/compare/v1.3.5...v1.3.6) · Source commit ee11294e

## v1.3.5 (2026-10-30)

- My reports is a real button next to Send my logs. It glows gold with a red count when Coodsz answers, and answers reach the tool much sooner.
- Players' pause, play and fast forward buttons work on servers whose settings file came from an older Coop.
- Console commands like save, status, players, say, kick and help work on the Console page.
- The crash screen and Send my logs spot five common setup problems (Smart App Control, leftover Common.dll or 0Harmony.dll, a DNS changer, Documents in OneDrive) and show the fix.
- A server without CoopModPatch shares the host's MCM settings. Start server stops before adding a map mod to a campaign made on another map.

[Release page](https://github.com/Coodsz/Coodsz-Coop-and-Friends/releases/tag/v1.3.5) · [Compare with v1.3.4](https://github.com/Coodsz/Coodsz-Coop-and-Friends/compare/v1.3.4...v1.3.5) · Source commit ed523f7f

## v1.3.4 (2026-10-30)

- A PvP countdown for the caught player and the player who caught them when the host turns on the answer timer.
- My reports gets a red badge, a status per report, and Did this fix it? buttons under each answer.
- COOP Server Settings Lock is retired. The tools tell you to turn it off, and the Server Tool gives a few mods the values it used.

[Release page](https://github.com/Coodsz/Coodsz-Coop-and-Friends/releases/tag/v1.3.4) · [Compare with v1.3.3](https://github.com/Coodsz/Coodsz-Coop-and-Friends/compare/v1.3.3...v1.3.4) · Source commit c85288c5

## v1.3.3 (2026-10-30)

- The game keeps running after Resume game until you press Pause game.
- Antivirus help. A new Fix antivirus button lists the folders to skip and opens Windows Security.
- Install update works without a hidden PowerShell script and puts the old version back when an update fails.
- PvP rules on the Settings page, both off by default. A Donate button next to What's new.
- Crash reports add a text crash stack, and save backups work for a campaign that lost its .json or .sav. Start server warns before a new campaign on a map mod like Europe1100.

[Release page](https://github.com/Coodsz/Coodsz-Coop-and-Friends/releases/tag/v1.3.3) · [Compare with v1.3.2](https://github.com/Coodsz/Coodsz-Coop-and-Friends/compare/v1.3.2...v1.3.3) · Source commit aa6e1a87

## v1.3.2 (2026-10-29)

- A Find Bannerlord button lets you pick the game folder when a tool can't find it.
- Answers to your reports show in the tool, and My reports keeps every sent report and answer.
- A campaign that failed to load is never saved over. Web and Workshop links Windows can't open show a message instead of closing the tool.

[Release page](https://github.com/Coodsz/Coodsz-Coop-and-Friends/releases/tag/v1.3.2) · [Compare with v1.3.1](https://github.com/Coodsz/Coodsz-Coop-and-Friends/compare/v1.3.1...v1.3.2) · Source commit 5bbb09f1

## v1.3.1 (2026-10-28)

- A child born to a player's character no longer stops a Coop 0.1.6 server. Coop Save Rescue fills in what the game needs.
- With Improved Garrisons and another mod that changes party sizes, both count now. Players need the new Client Launcher.
- A server command the server turned down no longer shows as done.

[Release page](https://github.com/Coodsz/Coodsz-Coop-and-Friends/releases/tag/v1.3.1) · [Compare with v1.3.0](https://github.com/Coodsz/Coodsz-Coop-and-Friends/compare/v1.3.0...v1.3.1) · Source commit ca18a03b

## v1.3.0 (2026-10-28)

- The two exe release. Coodsz Host is now the Coodsz Coop Server Tool, and Coodsz Coop and Friends is now the Coodsz Coop Client Launcher. Both come in one zip with the modules and bundled mods inside.
- Coodsz and Friends became the More Nations switch under Campaign map in the Client Launcher.
- Settings move by themselves to `%LOCALAPPDATA%\Coodsz\Coop`. Each tool has its own icon. A damaged Steam library file no longer stops the tools from finding Bannerlord.
- Install update renames the program, moves shortcuts and removes the old exe.

[Release page](https://github.com/Coodsz/Coodsz-Coop-and-Friends/releases/tag/v1.3.0) · [Compare with v1.2.79](https://github.com/Coodsz/Coodsz-Coop-and-Friends/compare/v1.2.79...v1.3.0) · Source commit 1b0b811d

## v1.2.79 (2026-10-27)

- Install update removes only the files that belong to the tool you update, so another Coodsz tool or your own files in the same folder stay. The update that installs this version still runs the old rule.

[Release page](https://github.com/Coodsz/Coodsz-Coop-and-Friends/releases/tag/v1.2.79) · [Compare with v1.2.78](https://github.com/Coodsz/Coodsz-Coop-and-Friends/compare/v1.2.78...v1.2.79) · Source commit 3bc9aa74

## v1.2.78 (2026-10-27)

- A new campaign name in Coodsz Host can use letters from any language, numbers, spaces, hyphens and underscores. The tool checks the name against the server folder's own start script and says so in plain words when an older script only takes A to Z.

[Release page](https://github.com/Coodsz/Coodsz-Coop-and-Friends/releases/tag/v1.2.78) · [Compare with v1.2.77](https://github.com/Coodsz/Coodsz-Coop-and-Friends/compare/v1.2.77...v1.2.78) · Source commit 2f1be998

## v1.2.77 (2026-10-26)

- The launcher uses the Nexus copy of Mod Configuration Menu (MCM) in the game's Modules folder over the old download from the removed Workshop item, so joining no longer fails on an MCM version the host doesn't run. Connect turns the old Workshop download off while the Nexus copy is used.
- Letters like Ü or İ show right in the server window in Coodsz Host.
- The crash report window mentions the crash dump only when there is one.

[Release page](https://github.com/Coodsz/Coodsz-Coop-and-Friends/releases/tag/v1.2.77) · [Compare with v1.2.76](https://github.com/Coodsz/Coodsz-Coop-and-Friends/compare/v1.2.76...v1.2.77) · Source commit 62465132

## v1.2.76 (2026-10-26)

- Automatic restart works in Coodsz Host. It saves, stops and starts the same save again after the hours you set, with a 30 second warning. Restart no longer freezes the window.
- MCM comes from Nexus Mods now, because the official Workshop item was removed. Start server copies a newer MCM from the game's Modules folder onto the server.
- Crash reports can add the newest crash dump, in a box that starts ticked.
- Each mod row in Coodsz Host shows player reports with a Report link. Save backups wait until the server has finished writing the save.
- Clearer crash and status messages, and a three line status box when you answer No to adding mods to a campaign.

[Release page](https://github.com/Coodsz/Coodsz-Coop-and-Friends/releases/tag/v1.2.76) · [Compare with v1.2.75](https://github.com/Coodsz/Coodsz-Coop-and-Friends/compare/v1.2.75...v1.2.76) · Source commit fcf17503

## v1.2.75 (2026-10-26)

- Messages that said Refresh now say Check mods, because that window has no Refresh button.
- Install update checks that the download comes from the Coodsz GitHub page and matches the release before it replaces any file.

[Release page](https://github.com/Coodsz/Coodsz-Coop-and-Friends/releases/tag/v1.2.75) · [Compare with v1.2.74](https://github.com/Coodsz/Coodsz-Coop-and-Friends/compare/v1.2.74...v1.2.75) · Source commit 339ac086

## v1.2.74 (2026-10-25)

- Start server finds an extra campaign map inside the SandBox module and offers to move it aside to `DedicatedServer\removed-mods`. Before, a map mod server stopped with "Multiple active modules provide SceneObj\Main_map".

[Release page](https://github.com/Coodsz/Coodsz-Coop-and-Friends/releases/tag/v1.2.74) · [Compare with v1.2.73](https://github.com/Coodsz/Coodsz-Coop-and-Friends/compare/v1.2.73...v1.2.74) · Source commit 6ed0f980

## v1.2.73 (2026-10-25)

- Connect asks the server for an answer first with Direct. If nobody answers in 5 seconds, or the address is your own PC, router or VPN, it offers Steam lobby.
- Coodsz Host turns Steam lobby on once when the internet provider shares one address between homes.
- Connect asks to close a Bannerlord that is still running. A warning names a mod that sits in two Workshop items.
- Logs from a silent exit code 1 now include Windows event log entries.

[Release page](https://github.com/Coodsz/Coodsz-Coop-and-Friends/releases/tag/v1.2.73) · [Compare with v1.2.72](https://github.com/Coodsz/Coodsz-Coop-and-Friends/compare/v1.2.72...v1.2.73) · Source commit 3382c009

## v1.2.72 (2026-10-25)

- A male child being born no longer stops a Coop 0.1.6 server. Coop Save Rescue fills in the missing name first.
- Start server stops before the game loads when MCM is older than v5.12, which crashes the dedicated server.
- Crash reports and log zips keep version numbers like 3.0.3.0 readable while real addresses are still taken out.

[Release page](https://github.com/Coodsz/Coodsz-Coop-and-Friends/releases/tag/v1.2.72) · [Compare with v1.2.71](https://github.com/Coodsz/Coodsz-Coop-and-Friends/compare/v1.2.71...v1.2.72) · Source commit 6f571615

## v1.2.71 (2026-10-24)

- A Full mod list link on the mods page opens the community list on GitHub.
- The built in mod list marks big overhauls as Broken, because only TAOM and AD1259 work in Coop, through their own Coodsz tools.

[Release page](https://github.com/Coodsz/Coodsz-Coop-and-Friends/releases/tag/v1.2.71) · [Compare with v1.2.70](https://github.com/Coodsz/Coodsz-Coop-and-Friends/compare/v1.2.70...v1.2.71) · Source commit eacb35dc

## v1.2.70 (2026-10-24)

- Send crash report and Send my logs have a box for your Discord name above Send. The tool remembers it on your PC. Save a copy still works without a name.

[Release page](https://github.com/Coodsz/Coodsz-Coop-and-Friends/releases/tag/v1.2.70) · [Compare with v1.2.69](https://github.com/Coodsz/Coodsz-Coop-and-Friends/compare/v1.2.69...v1.2.70) · Source commit df232286

## v1.2.69 (2026-10-24)

- Improved Garrisons town settings stay after a server restart. Before, they were deleted at server start and CoopModPatch saved the empty list.
- Settings stay when a town goes to a member of the same clan, and reset for another clan.
- Only a town's owner can change its Improved Garrisons settings. Coodsz Host adds the new server module DedicatedServer.IGCoopCompat by itself.

[Release page](https://github.com/Coodsz/Coodsz-Coop-and-Friends/releases/tag/v1.2.69) · [Compare with v1.2.68](https://github.com/Coodsz/Coodsz-Coop-and-Friends/compare/v1.2.68...v1.2.69) · Source commit 039dab94

## v1.2.68 (2026-10-24)

- The server log pack carries CoopModPatch's logs and the Improved Garrisons settings file.
- The crash screen names a graphics driver error while the map terrain loads, and says to update the driver and try another Terrain quality.

[Release page](https://github.com/Coodsz/Coodsz-Coop-and-Friends/releases/tag/v1.2.68) · [Compare with v1.2.67](https://github.com/Coodsz/Coodsz-Coop-and-Friends/compare/v1.2.67...v1.2.68) · Source commit f08bac41

## v1.2.67 (2026-10-24)

- Big battles with Limitless. With Limitless and Limitless Coop Compat in the server's mods, Battle size goes up to 10000, and the Settings page shows how heavy the battle is and a suggested size.
- The crash screen checks the logs against a list of known crash causes and says what happened and how to fix it.
- Changing the mod load order no longer hides your campaign save. Servers no longer crash on mods that ship a raw Assets folder, which is hidden on every server start.
- CoopModPatch's first setup stop shows as Setup finished, not a crash. Exit code 1 with no error and Diplomacy without Diplomacy Coop Compat are explained.

[Release page](https://github.com/Coodsz/Coodsz-Coop-and-Friends/releases/tag/v1.2.67) · [Compare with v1.2.66](https://github.com/Coodsz/Coodsz-Coop-and-Friends/compare/v1.2.66...v1.2.67) · Source commit 755c0071

## v1.2.66 (2026-10-24)

- Players see TAOM's War of the Ring bar live on a TAOM server run with Coodsz Host, and their game follows the server's war phase.
- Reports get optional Tell us what happened and Add screenshots (up to 5 pictures).
- Save settings no longer stops on an empty number box, the launcher window opens inside small screens, and Coodsz Host stays smooth in a burst of server log lines.
- The old More cheats section on the Cheats page is gone. Reporting a mod is easier to find, with a Report link on each mod.

[Release page](https://github.com/Coodsz/Coodsz-Coop-and-Friends/releases/tag/v1.2.66) · [Compare with v1.2.65](https://github.com/Coodsz/Coodsz-Coop-and-Friends/compare/v1.2.65...v1.2.66) · Source commit b9ec071a

## v1.2.65 (2026-10-23)

- Coodsz Host can protect player kingdoms from AI wars under Advanced server rules. The choices are Only while their players are offline (Coop's own protection), Always, and Off. The Always choice is adapted from the ModderLords mod with its author's permission.

[Release page](https://github.com/Coodsz/Coodsz-Coop-and-Friends/releases/tag/v1.2.65) · [Compare with v1.2.64](https://github.com/Coodsz/Coodsz-Coop-and-Friends/compare/v1.2.64...v1.2.65) · Source commit 62410c58

## v1.2.64 (2026-10-23)

- When TAOM is in the game, joining players can no longer enlist under a lord and end up fighting him. The client fix greys out the enlist option with a note that it is not supported in Coop yet.
- Settings sync checks less often for a mod you don't have (up to once a minute, giving up after 10 minutes), which removes game stutter.

[Release page](https://github.com/Coodsz/Coodsz-Coop-and-Friends/releases/tag/v1.2.64) · [Compare with v1.2.63](https://github.com/Coodsz/Coodsz-Coop-and-Friends/compare/v1.2.63...v1.2.64) · Source commit 53e12f3c

## v1.2.63 (2026-10-23)

- Players use the host's MCM settings by themselves when they join a Coodsz Host server. The settings only live in memory. Hosts get an Advanced area on the Settings page to change values or let players keep their own.
- Connect shows one box with a Get them and connect button for missing or outdated mods.
- Start server shows a tidy card with one button when a save needs mods your setup leaves out.
- The Mods page shows ModderLords tested verdicts, and a calmer Your mods card.
- Server rules in Coodsz Host. The game pauses while a player joins, offline players stop gaining skill XP, and hosts can limit coop.unstuck. Coop Save Rescue also fixes a server crash when a war is declared and a kingdom has no leader.

[Release page](https://github.com/Coodsz/Coodsz-Coop-and-Friends/releases/tag/v1.2.63) · [Compare with v1.2.62](https://github.com/Coodsz/Coodsz-Coop-and-Friends/compare/v1.2.62...v1.2.63) · Source commit 96082ea8

## v1.2.62 (2026-10-23)

- Coop Save Rescue runs on every Start server and Restart as the server only module DedicatedServer.CoopSaveRescue. Coodsz Host starts once more with deeper save fixes after a crash on load, and checks the log after Stop.
- Custom commands moved from the Cheats page to the Console page. Join a kingdom says up front that Coop refuses a clan below tier 2.
- Report a mod in one click, with Works or Broken per mod, a bar after a crash that asks which mod broke, and report counts on every mod row.
- Crash reports replace log zips. You see every file, click Send, and get a short code to post in Discord. Save a copy instead keeps the zip on your PC.

[Release page](https://github.com/Coodsz/Coodsz-Coop-and-Friends/releases/tag/v1.2.62) · [Compare with v1.2.61](https://github.com/Coodsz/Coodsz-Coop-and-Friends/compare/v1.2.61...v1.2.62) · Source commit 0f4bef36

## v1.2.61 (2026-10-22)

- Joining no longer gets stuck on Final Synchronization. The client fix puts back the parties the server says are there and logs them to `CoodszJoinPassword.log`.
- The server no longer stalls behind the Coodsz Host window. Log lines are read right away and drawn in batches.
- The update bar stays at the top in a smaller window.

[Release page](https://github.com/Coodsz/Coodsz-Coop-and-Friends/releases/tag/v1.2.61) · [Compare with v1.2.60](https://github.com/Coodsz/Coodsz-Coop-and-Friends/compare/v1.2.60...v1.2.61) · Source commit fe7bf53b

## v1.2.60 (2026-10-21)

- Rescue save fixes killed lords still marked alive, which crashed the server on every load (0xC0000005 right after "weather behavior unregistered"). One bad hero can no longer stop the whole save from loading.

[Release page](https://github.com/Coodsz/Coodsz-Coop-and-Friends/releases/tag/v1.2.60) · [Compare with v1.2.59](https://github.com/Coodsz/Coodsz-Coop-and-Friends/compare/v1.2.59...v1.2.60) · Source commit e047032a

## v1.2.59 (2026-10-20)

- The tools have a license. It shows once, you click I agree, and the full text is in `LICENSE.txt` next to the program.
- Check mods says which file UIExtenderEx is missing, and Connect turns off its Compiled Prefabs for that game until it is back.
- Coodsz Host Home says the Steam lobby did not start when Steam was not open or logged in.

[Release page](https://github.com/Coodsz/Coodsz-Coop-and-Friends/releases/tag/v1.2.59) · [Compare with v1.2.58](https://github.com/Coodsz/Coodsz-Coop-and-Friends/compare/v1.2.58...v1.2.59) · Source commit c0760fa7

## v1.2.58 (2026-10-20)

- A new Cheats page in Coodsz Host with player cards, categories, forms with dropdowns, undo where it can, and an admin log. Every cheat sends a Coop command.
- A new Console page lists all 414 Coop commands with a How to use guide, examples and live server output.
- Steam lobby joins wait up to 30 seconds. The server log says when two players share one Steam ID, and the launcher notes files from a non Steam copy.
- The RBM box in the server tool is text only, because RBM runs in each player's own game.

[Release page](https://github.com/Coodsz/Coodsz-Coop-and-Friends/releases/tag/v1.2.58) · [Compare with v1.2.57](https://github.com/Coodsz/Coodsz-Coop-and-Friends/compare/v1.2.57...v1.2.58) · Source commit 3c67c71d

## v1.2.57 (2026-10-19)

- A new locked setup, Plain Coop (no mods), in Coodsz Host and Coop and Friends.
- A Max clans per kingdom setting in the Kingdoms group, with a Count mercenaries tick. It is kept in `coodsz-server-tools.json` and applies live. 0 means no limit and is the default.
- A clearer Add mods to this campaign box. Harmony and UIExtenderEx no longer trigger it. Saves from an older patch of the same game version load.
- The working setups are named after Coop 0.1.6. Many translation fixes on the Settings page, and the Cheats page shows hero names.

[Release page](https://github.com/Coodsz/Coodsz-Coop-and-Friends/releases/tag/v1.2.57) · [Compare with v1.2.56](https://github.com/Coodsz/Coodsz-Coop-and-Friends/compare/v1.2.56...v1.2.57) · Source commit f6ec809b

## v1.2.56 (2026-10-18)

- Save logs for help takes your join password out of the logs, including quoted passwords, settings files and zips in crash report folders, in Coop and Friends and Coodsz Host.

[Release page](https://github.com/Coodsz/Coodsz-Coop-and-Friends/releases/tag/v1.2.56) · [Compare with v1.2.55](https://github.com/Coodsz/Coodsz-Coop-and-Friends/compare/v1.2.55...v1.2.56) · Source commit 714e1f08

## v1.2.55 (2026-10-18)

- RBM's campaign rules run in each player's own game, not on the Coop server. Connect turns off RBM's Lords buy equipment in your own RBM settings, with a Keep RBM coop friendly box that is on by default.
- Coodsz Host gets an RBM settings card for the host PC's game, and Start server no longer changes RBM's settings.

[Release page](https://github.com/Coodsz/Coodsz-Coop-and-Friends/releases/tag/v1.2.55) · [Compare with v1.2.54](https://github.com/Coodsz/Coodsz-Coop-and-Friends/compare/v1.2.54...v1.2.55) · Source commit cc2f2081

## v1.2.54 (2026-10-18)

- Hover tips on every button, switch, check box, list and menu item.
- Delete setup in Coodsz Host and Remove on a saved cheat command ask before deleting.
- A new RBM settings file carries the version RBM expects, so its Campaign part stays off as you set it.

[Release page](https://github.com/Coodsz/Coodsz-Coop-and-Friends/releases/tag/v1.2.54) · [Compare with v1.2.53](https://github.com/Coodsz/Coodsz-Coop-and-Friends/compare/v1.2.53...v1.2.54) · Source commit 68621389

## v1.2.53 (2026-10-18)

- New Coodsz Host pages. Home, Players, a player Profile, Cheats, Map, Encyclopedia and Guides, plus a Create a world wizard, world presets and Streamer mode.
- Easier joining. A card lists the addresses friends can use, Copy invite writes a Discord message without the password, and the router port can open automatically.
- A safer server. Optional restart after a crash, a backup after every save, Stop and Restart let the server save first, and Restart warns players 30 and 10 seconds ahead.
- Every Coop setting is on the Settings page and is kept per campaign save. A Game speed card on the Server page.
- Community badges on mods, Test mods on the server, adding mods to a running campaign and a Locked tag on locked setups.

[Release page](https://github.com/Coodsz/Coodsz-Coop-and-Friends/releases/tag/v1.2.53) · [Compare with v1.2.52](https://github.com/Coodsz/Coodsz-Coop-and-Friends/compare/v1.2.52...v1.2.53) · Source commit 518c3611

## v1.2.52 (2026-10-18)

- Battle size on the Settings page now sets coop battles for everyone. It is written to `mod-config.json`, goes from 200 to 1000, and takes effect after a server restart.
- Start server fixes True or False written with capitals in `mod-config.json`, which made the Coop server ignore the whole file. Other unreadable lines get a warning with the line number.
- Settings Coodsz Host saves now go inside the right block of the file, not the top where the server never looks.

[Release page](https://github.com/Coodsz/Coodsz-Coop-and-Friends/releases/tag/v1.2.52) · [Compare with v1.2.51](https://github.com/Coodsz/Coodsz-Coop-and-Friends/compare/v1.2.51...v1.2.52) · Source commit 9df39d79

## v1.2.51 (2026-10-17)

- Install mods no longer keeps asking for an update Steam has not offered. The launcher reads what Steam itself knows and uses your copy when Steam says it is the newest.
- The Report a problem button is gone. Use Save all logs to one zip.

[Release page](https://github.com/Coodsz/Coodsz-Coop-and-Friends/releases/tag/v1.2.51) · [Compare with v1.2.50](https://github.com/Coodsz/Coodsz-Coop-and-Friends/compare/v1.2.50...v1.2.51) · Source commit ce035bae

## v1.2.50 (2026-10-17)

- Check server gets a real answer from the Coop server. Before, every working server showed No UDP reply. Now it knocks the way a joining player does and shows Server online, without using a slot.
- When nothing answers, it says what to check (server stopped, wrong address or port, firewall, port not forwarded).

[Release page](https://github.com/Coodsz/Coodsz-Coop-and-Friends/releases/tag/v1.2.50) · [Compare with v1.2.49](https://github.com/Coodsz/Coodsz-Coop-and-Friends/compare/v1.2.49...v1.2.50) · Source commit 4819f73c

## v1.2.49 (2026-10-17)

- Install mods no longer gets stuck when Steam won't download a Workshop update. It asks Steam to download every mod it picks and then uses the copy on your PC.
- Mods removed from the Workshop (COOP Server Settings Lock, More Nations Remastered and its COOP Fix) now tell you to get them from Nexus Mods or ModDB. A mod put in Modules by hand is used as it is.

[Release page](https://github.com/Coodsz/Coodsz-Coop-and-Friends/releases/tag/v1.2.49) · [Compare with v1.2.48](https://github.com/Coodsz/Coodsz-Coop-and-Friends/compare/v1.2.48...v1.2.49) · Source commit b65e9011

## v1.2.48 (2026-10-16)

- Rescue save is easier to follow. Coodsz Host shows five short steps and the ? button next to it shows them.

[Release page](https://github.com/Coodsz/Coodsz-Coop-and-Friends/releases/tag/v1.2.48) · [Compare with v1.2.47](https://github.com/Coodsz/Coodsz-Coop-and-Friends/compare/v1.2.47...v1.2.48) · Source commit 507ae3da

## v1.2.47 (2026-10-15)

- Fixed an empty mod list ("The mod list could not be read yet") on new installs and reinstalls, caused by a changed online profile the launcher reads.

[Release page](https://github.com/Coodsz/Coodsz-Coop-and-Friends/releases/tag/v1.2.47) · [Compare with v1.2.46](https://github.com/Coodsz/Coodsz-Coop-and-Friends/compare/v1.2.46...v1.2.47) · Source commit 28b9c55c

## v1.2.46 (2026-10-15)

- Import mod list does the whole job. It makes a new setup, adds the mods, downloads Workshop mods you don't have and shows a summary.
- Share mod list saves each mod's Workshop link, so share old lists again after updating.

[Release page](https://github.com/Coodsz/Coodsz-Coop-and-Friends/releases/tag/v1.2.46) · [Compare with v1.2.45](https://github.com/Coodsz/Coodsz-Coop-and-Friends/compare/v1.2.45...v1.2.46)

## v1.2.45 (2026-10-15)

- Fixed a village and town menu freeze when Coop sends a party without all its data. The client launcher adds a small Coodsz module for this, so the game rebuilds its shaders once.

[Release page](https://github.com/Coodsz/Coodsz-Coop-and-Friends/releases/tag/v1.2.45) · [Compare with v1.2.44](https://github.com/Coodsz/Coodsz-Coop-and-Friends/compare/v1.2.44...v1.2.45) · Source commit dd81493d

## Coodsz Menu Guard v1.0.0 (2026-10-15)

- A standalone module for players on the plain Coop client. It fixes the game freezing when you walk into a village or town because Coop sent a party without all its data. The menu opens and that one entry stays blank.
- Only players need it. The first start with it rebuilds shaders once. Coop and Friends v1.2.45, TAOM v1.1.27 and AD1259 v1.0.6 already include it.

[Release page](https://github.com/Coodsz/Coodsz-Coop-and-Friends/releases/tag/menu-guard-1.0.0) · First release of this module, so no compare link.

## Coop Save Rescue v1.1.0 (2026-10-15)

- Standalone zips (VANILLA for the plain Coop server, MODDED for Start-ModdedServer.cmd) get the ended battle and empty lord fixes. Battles where one side has no men left are closed, parties tied to an ended battle are let go, and lords with an empty party are reset. No lord is killed.

[Release page](https://github.com/Coodsz/Coodsz-Coop-and-Friends/releases/tag/save-rescue-1.1.0) · [Compare with save-rescue-1.0.0](https://github.com/Coodsz/Coodsz-Coop-and-Friends/compare/save-rescue-1.0.0...save-rescue-1.1.0) · Source commit a2698151

## v1.2.44 (2026-10-15)

- Rescue save frees parties tied to a battle that already ended, and resets lords left with an empty party. No lord is killed.
- A What's new window shows once after each update, in your language.

[Release page](https://github.com/Coodsz/Coodsz-Coop-and-Friends/releases/tag/v1.2.44) · [Compare with v1.2.43](https://github.com/Coodsz/Coodsz-Coop-and-Friends/compare/v1.2.43...v1.2.44)

## v1.2.43 (2026-10-15)

- The tools speak 6 more languages. Arabic, German, French, Portuguese, Russian and Turkish. The first start asks once, and the 🌐 button changes it. Arabic shows the tool right to left.
- Log zips and problem reports stay in English.

[Release page](https://github.com/Coodsz/Coodsz-Coop-and-Friends/releases/tag/v1.2.43) · [Compare with v1.2.42](https://github.com/Coodsz/Coodsz-Coop-and-Friends/compare/v1.2.42...v1.2.43) · Source commit 442501a3

## v1.2.42 (2026-10-15)

- Rescue save starts again. It used to stop with a module mismatch because the save check counted the rescue module as a missing mod.
- Rescue save finds battles where one side has no men left, gives the win to the other side and closes them.
- A How Rescue save works button next to Rescue save.

[Release page](https://github.com/Coodsz/Coodsz-Coop-and-Friends/releases/tag/v1.2.42) · [Compare with v1.2.41](https://github.com/Coodsz/Coodsz-Coop-and-Friends/compare/v1.2.41...v1.2.42) · Source commit 7fc7a842

## v1.2.41 (2026-10-14)

- Auto pause works. The server used to switch the pause off no matter what the setting said.
- Fast forward keeps the setting you pick instead of resetting on every Start.
- The movement outgoing and incoming limits show and save Coop's default of 10 when empty.

[Release page](https://github.com/Coodsz/Coodsz-Coop-and-Friends/releases/tag/v1.2.41) · [Compare with v1.2.40](https://github.com/Coodsz/Coodsz-Coop-and-Friends/compare/v1.2.40...v1.2.41) · Source commit 6ad90489

## v1.2.40 (2026-10-14)

- The server control buttons in Coodsz Host wrap onto a second row in a smaller window instead of overlapping.

[Release page](https://github.com/Coodsz/Coodsz-Coop-and-Friends/releases/tag/v1.2.40) · [Compare with v1.2.39](https://github.com/Coodsz/Coodsz-Coop-and-Friends/compare/v1.2.39...v1.2.40) · Source commit 4d985208

## v1.2.39 (2026-10-14)

- A new RBM box in the Coodsz Host Settings (Combat, AI, Campaign, Tournament), saved per setup. Campaign starts off because RBM's economy empties town shops on a Coop server.
- Server only DedicatedServer.* mods no longer make your saves look incompatible.

[Release page](https://github.com/Coodsz/Coodsz-Coop-and-Friends/releases/tag/v1.2.39) · [Compare with v1.2.38](https://github.com/Coodsz/Coodsz-Coop-and-Friends/compare/v1.2.38...v1.2.39) · Source commit 021b1dc5

## v1.2.38 (2026-10-12)

- A Rescue save button in Coodsz Host fixes saves that crash on load or freeze the map. Save game works through the server console.

[Release page](https://github.com/Coodsz/Coodsz-Coop-and-Friends/releases/tag/v1.2.38) · [Compare with v1.2.37](https://github.com/Coodsz/Coodsz-Coop-and-Friends/compare/v1.2.37...v1.2.38) · Source commit 7eea10dd

## Coop Save Rescue v1.0.0 (2026-10-12)

- First standalone release. It fixes a save that crashes the server after loading, or a map that stays frozen (stuck town crime menu, party stuck in a hostile town, broken battles). Two zips, VANILLA for the plain Coop server and MODDED for Start-ModdedServer.cmd.

[Release page](https://github.com/Coodsz/Coodsz-Coop-and-Friends/releases/tag/save-rescue-1.0.0) · First release of this module, so no compare link.

## v1.2.37 (2026-10-12)

- Save game works again. The October Coop server update removed what it used, so Coodsz Host now uses the server's own save command.
- The Client cheats setting in `mod-config.json` is no longer forced off when the server starts.

[Release page](https://github.com/Coodsz/Coodsz-Coop-and-Friends/releases/tag/v1.2.37) · [Compare with v1.2.36](https://github.com/Coodsz/Coodsz-Coop-and-Friends/compare/v1.2.36...v1.2.37) · Source commit 7863c063

## v1.2.36 (2026-10-12)

- The Client cheats setting works on modded dedicated servers. The start script forced it off, so Coodsz Host uses its own copy without that step.

[Release page](https://github.com/Coodsz/Coodsz-Coop-and-Friends/releases/tag/v1.2.36) · [Compare with v1.2.35](https://github.com/Coodsz/Coodsz-Coop-and-Friends/compare/v1.2.35...v1.2.36) · Source commit 6bec2a4d

## v1.2.35 (2026-10-11)

- Refresh on the Modules page updates mods that have a newer version. A different Workshop version replaces the server copy, and a Nexus or ModDB copy only replaces it when newer. Old copies go to `DedicatedServer\removed-mods`.

[Release page](https://github.com/Coodsz/Coodsz-Coop-and-Friends/releases/tag/v1.2.35) · [Compare with v1.2.34](https://github.com/Coodsz/Coodsz-Coop-and-Friends/compare/v1.2.34...v1.2.35) · Source commit 6c26890a

## v1.2.34 (2026-10-09)

- Save game and Stop save the world again with Coop 0.1.6, using the dedicated server's own save command.

[Release page](https://github.com/Coodsz/Coodsz-Coop-and-Friends/releases/tag/v1.2.34) · [Compare with v1.2.33](https://github.com/Coodsz/Coodsz-Coop-and-Friends/compare/v1.2.33...v1.2.34) · Source commit 7cc644ff

## v1.2.33 (2026-10-09)

- PSCoopBridge is turned off automatically when Player Settlement isn't on, because it made the game run at about 10 fps. Hosts and players should both update.

[Release page](https://github.com/Coodsz/Coodsz-Coop-and-Friends/releases/tag/v1.2.33) · [Compare with v1.2.32](https://github.com/Coodsz/Coodsz-Coop-and-Friends/compare/v1.2.32...v1.2.33) · Source commit b6024d92

## v1.2.32 (2026-10-08)

- The free starter reward gives a reward code tied to your Steam account, to paste into a ticket on The Realm Discord. Staff can check codes in Coodsz Host under Join my server.

[Release page](https://github.com/Coodsz/Coodsz-Coop-and-Friends/releases/tag/v1.2.32) · [Compare with v1.2.31](https://github.com/Coodsz/Coodsz-Coop-and-Friends/compare/v1.2.31...v1.2.32) · Source commit 88f7c53b

## v1.2.31 (2026-10-08)

- A Join my server tab for The Realm, with new Realm art and a free starter reward claimed through a Discord ticket.

[Release page](https://github.com/Coodsz/Coodsz-Coop-and-Friends/releases/tag/v1.2.31) · [Compare with v1.2.30](https://github.com/Coodsz/Coodsz-Coop-and-Friends/compare/v1.2.30...v1.2.31) · Source commit d1d4572c

## v1.2.30 (2026-10-07)

- Works with Coop 0.1.6. The first Start server shows the Coop server terms and only starts after you press I accept.
- default_new_game is no longer offered as a save, and the host no longer suggests moving files out of Coop's folder.

[Release page](https://github.com/Coodsz/Coodsz-Coop-and-Friends/releases/tag/v1.2.30) · [Compare with v1.2.29](https://github.com/Coodsz/Coodsz-Coop-and-Friends/compare/v1.2.29...v1.2.30) · Source commit 130ebc1d

## v1.2.29 (2026-10-07)

- Works with Coop 0.1.6, with the Coop server terms shown on the first Start server. Fixed an old Coop backup file being kept after the Steam update.

[Release page](https://github.com/Coodsz/Coodsz-Coop-and-Friends/releases/tag/v1.2.29) · [Compare with v1.2.28](https://github.com/Coodsz/Coodsz-Coop-and-Friends/compare/v1.2.28...v1.2.29) · Source commit e9841906

## v1.2.28 (2026-10-06)

- Hosting and playing on the same PC no longer makes Start server fail with COOP MODULE VERIFICATION FAILED. The client no longer touches the server and Coodsz Host repairs the file.
- Steam lobby accepts any Steam profile name, with symbols like | ! #.

[Release page](https://github.com/Coodsz/Coodsz-Coop-and-Friends/releases/tag/v1.2.28) · [Compare with v1.2.27](https://github.com/Coodsz/Coodsz-Coop-and-Friends/compare/v1.2.27...v1.2.28) · Source commit 20fd5998

## v1.2.27 (2026-10-06)

- After Steam updates a mod, Start server updates the server copy to the Workshop version and keeps the old one in `DedicatedServer\removed-mods`. Before, joins failed.
- Also includes 1.2.26.

[Release page](https://github.com/Coodsz/Coodsz-Coop-and-Friends/releases/tag/v1.2.27) · [Compare with v1.2.26](https://github.com/Coodsz/Coodsz-Coop-and-Friends/compare/v1.2.26...v1.2.27) · Source commit 257d70b9

## v1.2.26 (2026-10-05)

- The server starts on Turkish Windows again (it failed with "Invalid policy identifier ImprovedGarrisons").
- Leave settlement and other controllerId cheats send the player's SteamID. Save game and the save before stop make the server write the save even when time is stopped.

[Release page](https://github.com/Coodsz/Coodsz-Coop-and-Friends/releases/tag/v1.2.26) · [Compare with v1.2.25](https://github.com/Coodsz/Coodsz-Coop-and-Friends/compare/v1.2.25...v1.2.26) · Source commit 85fc1aa9

## v1.2.25 (2026-10-04)

- Cheats, Console, player list, bans and Pause/Resume work on Steam Workshop servers. Start server adds the server's missing Launcher Cheats and Launcher Battle Size.
- Pause/Resume use the server's real time command. Kick, Apply speed and Scheduled broadcasts were removed because Coop has no commands for them.

[Release page](https://github.com/Coodsz/Coodsz-Coop-and-Friends/releases/tag/v1.2.25) · [Compare with v1.2.24](https://github.com/Coodsz/Coodsz-Coop-and-Friends/compare/v1.2.24...v1.2.25) · Source commit 1ab4cc3d

## v1.2.24 (2026-10-04)

- Install mods subscribes to missing Workshop mods through Steam and reports items Steam refuses within about 30 seconds. Workshop mods are no longer bundled in the zip.
- Fixed a launcher crash when setting aside duplicate Workshop mod copies, and clearer startup crash messages.
- Host and client got small button changes, and Save all logs to one zip asks before adding crash dumps.

[Release page](https://github.com/Coodsz/Coodsz-Coop-and-Friends/releases/tag/v1.2.24) · [Compare with v1.2.23](https://github.com/Coodsz/Coodsz-Coop-and-Friends/compare/v1.2.23...v1.2.24) · Source commit 109608da

## v1.2.23 (2026-10-01)

- ServerSettingsLock is pinned in the Working stable module set and stays Ready when Steam's file check is flaky.
- Clear unused MCM on the client and Coodsz Host deletes leftover optional MCM folders for mods that are off.
- Join text for Player Settlement or PSCoopBridge failures says whether to turn them off or install them on the server.

[Release page](https://github.com/Coodsz/Coodsz-Coop-and-Friends/releases/tag/v1.2.23) · [Compare with v1.2.22](https://github.com/Coodsz/Coodsz-Coop-and-Friends/compare/v1.2.22...v1.2.23) · Source commit 0c54c1cf

## v1.2.22 (2026-09-30)

- Coodsz Host repairs thin Steam DedicatedServer installs by copying missing starter files (`Start-ModdedServer.ps1`, `0Harmony.dll` and others). It no longer starts the bare server exe, which only loaded 5 core mods.

[Release page](https://github.com/Coodsz/Coodsz-Coop-and-Friends/releases/tag/v1.2.22) · [Compare with v1.2.21](https://github.com/Coodsz/Coodsz-Coop-and-Friends/compare/v1.2.21...v1.2.22)

## v1.2.21 (2026-09-30)

- The client launchers keep `client-action.log`, and Save All Logs To One Zip packs the full support set, with passwords, addresses and Steam IDs removed.

[Release page](https://github.com/Coodsz/Coodsz-Coop-and-Friends/releases/tag/v1.2.21) · [Compare with v1.2.20](https://github.com/Coodsz/Coodsz-Coop-and-Friends/compare/v1.2.20...v1.2.21)

## v1.2.20 (2026-09-30)

- The host writes Harmony, ButterLib, UIExtenderEx, MCM and CoopModPatch into the server module list, so friends who join with MCM are no longer rejected.
- Open server logs is highlighted, and `host-action.log` records host button clicks.

[Release page](https://github.com/Coodsz/Coodsz-Coop-and-Friends/releases/tag/v1.2.20) · [Compare with v1.2.19](https://github.com/Coodsz/Coodsz-Coop-and-Friends/compare/v1.2.19...v1.2.20)

## v1.2.19 (2026-09-30)

- Locked client setups send Story Mode and Custom Battle after SandBox and before Coop. The dedicated server still leaves those off.
- On the Remastered map the client says the host has to load a single player More Nations save, because a server made campaign leaves the ground black.
- Unblock DLLs turns red when a log says Windows blocked a DLL. Closing and reopening the host shows SERVING when the server is still running.

[Release page](https://github.com/Coodsz/Coodsz-Coop-and-Friends/releases/tag/v1.2.19) · [Compare with v1.2.18](https://github.com/Coodsz/Coodsz-Coop-and-Friends/compare/v1.2.18...v1.2.19)

## v1.2.18 (2026-09-29)

- Load single player save copies a campaign .sav into the server's Game Saves folder, and Load single player MCM copies single player MCM settings into the selected setup.
- When two enabled mods contain different copies of the same DLL, Start server names both files and says which to keep. The server still starts.

[Release page](https://github.com/Coodsz/Coodsz-Coop-and-Friends/releases/tag/v1.2.18) · [Compare with v1.2.17](https://github.com/Coodsz/Coodsz-Coop-and-Friends/compare/v1.2.17...v1.2.18)

## v1.2.17 (2026-09-29)

- Start server on a locked setup writes the full module list, so players on that setup can join.
- A failed join says what to do, including updating Workshop item 3770450698 when the dedicated server's Bannerlord build does not match the game.

[Release page](https://github.com/Coodsz/Coodsz-Coop-and-Friends/releases/tag/v1.2.17) · [Compare with v1.2.16](https://github.com/Coodsz/Coodsz-Coop-and-Friends/compare/v1.2.16...v1.2.17)

## v1.2.16 (2026-09-29)

- Add mods explains when a Workshop mod already belongs to the locked setup, and a missing mod tells you to subscribe in Steam.

[Release page](https://github.com/Coodsz/Coodsz-Coop-and-Friends/releases/tag/v1.2.16) · [Compare with v1.2.15](https://github.com/Coodsz/Coodsz-Coop-and-Friends/compare/v1.2.15...v1.2.16)

## v1.2.15 (2026-09-29)

- Saving a number no longer makes the dedicated server ignore the save name and password. Start repairs `server-config.json` and `mod-config.json` before the server launches.

[Release page](https://github.com/Coodsz/Coodsz-Coop-and-Friends/releases/tag/v1.2.15) · [Compare with v1.2.14](https://github.com/Coodsz/Coodsz-Coop-and-Friends/compare/v1.2.14...v1.2.15)

## v1.2.14 (2026-09-29)

- Save settings works on the locked setups, the save reminder is easier to see, and Set server folder explains what it does.

[Release page](https://github.com/Coodsz/Coodsz-Coop-and-Friends/releases/tag/v1.2.14) · [Compare with v1.2.13](https://github.com/Coodsz/Coodsz-Coop-and-Friends/compare/v1.2.13...v1.2.14)

## v1.2.13 (2026-09-29)

- A new campaign name reaches the server. A missing autosave setting is written into the server config, so Start no longer stops with "Could not find autosaveMinutes".
- A broken mod options block in `mod-config.json` is repaired. When Coop and CoopNightly are both installed, Start leaves Coop loaded.
- Region is a choice (EU, NA, AS, OC, SA, AF) that Steam lobby joins use.

[Release page](https://github.com/Coodsz/Coodsz-Coop-and-Friends/releases/tag/v1.2.13) · [Compare with v1.2.12](https://github.com/Coodsz/Coodsz-Coop-and-Friends/compare/v1.2.12...v1.2.13)

## v1.2.12 (2026-09-29)

- New setup starts on the default mod order with the vanilla campaign map and an empty save. Changing a setting pulses Save settings and says to restart the server.

[Release page](https://github.com/Coodsz/Coodsz-Coop-and-Friends/releases/tag/v1.2.12) · [Compare with v1.2.11](https://github.com/Coodsz/Coodsz-Coop-and-Friends/compare/v1.2.11...v1.2.12)

## v1.2.11 (2026-09-28)

- Choosing a saved setup switches Campaign map to the map that setup saved.

[Release page](https://github.com/Coodsz/Coodsz-Coop-and-Friends/releases/tag/v1.2.11) · [Compare with v1.2.10](https://github.com/Coodsz/Coodsz-Coop-and-Friends/compare/v1.2.10...v1.2.11)

## v1.2.10 (2026-09-28)

- The update removes the previous program files after the new copy is in place. Saved paths, cheats and mod folders stay.

[Release page](https://github.com/Coodsz/Coodsz-Coop-and-Friends/releases/tag/v1.2.10) · [Compare with v1.2.9](https://github.com/Coodsz/Coodsz-Coop-and-Friends/compare/v1.2.9...v1.2.10)

## v1.2.9 (2026-09-28)

- Install update finishes and opens the tool again. If the program file is still locked, it is moved aside and the new copy starts.

[Release page](https://github.com/Coodsz/Coodsz-Coop-and-Friends/releases/tag/v1.2.9) · [Compare with v1.2.8](https://github.com/Coodsz/Coodsz-Coop-and-Friends/compare/v1.2.8...v1.2.9)

## v1.2.8 (2026-09-28)

- Install update shows a progress bar and the button says Installing while the download finishes.

[Release page](https://github.com/Coodsz/Coodsz-Coop-and-Friends/releases/tag/v1.2.8) · [Compare with v1.2.7](https://github.com/Coodsz/Coodsz-Coop-and-Friends/compare/v1.2.7...v1.2.8)

## v1.2.7 (2026-09-28)

- Coloured buttons keep their words when the mouse is over them, and the ready count beside the mod list is larger.
- Install update replaces the program files and leaves your saved paths and cheats. A running dedicated server stays up.

[Release page](https://github.com/Coodsz/Coodsz-Coop-and-Friends/releases/tag/v1.2.7) · [Compare with v1.2.6](https://github.com/Coodsz/Coodsz-Coop-and-Friends/compare/v1.2.6...v1.2.7)

## v1.2.6 (2026-09-28)

- The first version with an Install update button, which replaces the program files, closes and opens again.
- Fast forward is a setting on the Settings page. Save keeps the server up and will not write over the last playable save when a campaign looks broken.
- Cheats lists players from the live server log, and says so when this window did not start the server.

[Release page](https://github.com/Coodsz/Coodsz-Coop-and-Friends/releases/tag/v1.2.6) · [Compare with v1.2.5](https://github.com/Coodsz/Coodsz-Coop-and-Friends/compare/v1.2.5...v1.2.6)

## v1.2.5 (2026-09-28)

- Steam lobby join uses the lobby, and a typed server address is sent with the connect.
- If the Coop server folder is missing server files, Start server restores them from the Workshop client folder.

[Release page](https://github.com/Coodsz/Coodsz-Coop-and-Friends/releases/tag/v1.2.5) · [Compare with v1.2.4](https://github.com/Coodsz/Coodsz-Coop-and-Friends/compare/v1.2.4...v1.2.5)

## v1.2.4 (2026-09-27)

- A new setup starts on the default mod order, and Add mods has a search box.
- An Auto dependency order button sorts the extra mods. Before the game starts, the launcher sets aside an old shader file inside a mod.
- The activity log says when a newer release is on GitHub.

[Release page](https://github.com/Coodsz/Coodsz-Coop-and-Friends/releases/tag/v1.2.4) · [Compare with v1.2.3](https://github.com/Coodsz/Coodsz-Coop-and-Friends/compare/v1.2.3...v1.2.4)

## v1.2.3 (2026-09-27)

- The mod list shows one Bannerlord Coop row, and the server tool does not replace the dedicated server's Coop folder with the game's client copy.
- Share list saves `coodsz-mod-list.json`, and Open shared list on the other tool adds the extra mods.
- The server tool shows a save as the character, level, campaign day and mods written in the save.

[Release page](https://github.com/Coodsz/Coodsz-Coop-and-Friends/releases/tag/v1.2.3) · [Compare with v1.2.2](https://github.com/Coodsz/Coodsz-Coop-and-Friends/compare/v1.2.2...v1.2.3)

## v1.2.2 (2026-09-26)

- Connect leaves terrain quality as you set it.
- Unblock DLLs on the client and the server tool clear the Windows download mark. Clear shader cache deletes only the shader folder in ProgramData.
- The server tool explains a DLL load error from the server log and points to Unblock DLLs.

[Release page](https://github.com/Coodsz/Coodsz-Coop-and-Friends/releases/tag/v1.2.2) · [Compare with v1.2.1](https://github.com/Coodsz/Coodsz-Coop-and-Friends/compare/v1.2.1...v1.2.2)

## v1.2.1 (2026-09-26)

- Added Unblock DLLs, Clear shader cache and a DLL load explanation in the server log, and Connect raises terrain quality from Low to Medium. Both tools write one log line when a newer release is published.

[Release page](https://github.com/Coodsz/Coodsz-Coop-and-Friends/releases/tag/v1.2.1) · [Compare with v1.2.0](https://github.com/Coodsz/Coodsz-Coop-and-Friends/compare/v1.2.0...v1.2.1)

## v1.2.0 (2026-09-26)

- The server tool looks through Steam's library folders for `BannerlordCoopServer.exe`, and Set server folder lets you pick it. The client finds the game and mods from the same libraries.
- Direct connect accepts an IP address or a custom address, and Check server explains a quiet port.
- Start server only loads a save already in the dedicated server's Game Saves folder, and copies missing .NET 6 Desktop files into the server folder.

[Release page](https://github.com/Coodsz/Coodsz-Coop-and-Friends/releases/tag/v1.2.0) · [Compare with v1.1.0](https://github.com/Coodsz/Coodsz-Coop-and-Friends/compare/v1.1.0...v1.2.0)

## v1.1.0 (2026-09-26)

- The client and the server tool with the first public features. Locked setups for the Vanilla campaign map and the More Nations Remastered map, Add mods, Test to main menu, and Server Logs with What happened, What to do and What not to do.

[Release page](https://github.com/Coodsz/Coodsz-Coop-and-Friends/releases/tag/v1.1.0) · [Compare with v1.0.0](https://github.com/Coodsz/Coodsz-Coop-and-Friends/compare/v1.0.0...v1.1.0)

## v1.0.0 (2026-09-25)

- The first release. A download with the client folder and the host folder, with no one's saved settings in it.

[Release page](https://github.com/Coodsz/Coodsz-Coop-and-Friends/releases/tag/v1.0.0)





