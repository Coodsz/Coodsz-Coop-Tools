# Coodsz Burning Empires tools history

This file lists every published version of the Coodsz Burning Empires tools (the Burning Empires Server Tool and Client Launcher, for the Burning Empires overhaul in Coop), newest first. It is for contributors who want to catch up on work from before the repo used PRs. The releases live in the public repo Coodsz/Coodsz-BurningEmpires. The source is in this repository. Work from 2026-11-19 on goes through PRs, so this file ends at v1.0.15.

Each version has a release page link, a compare link to the version before it, and the source commit that set the version when a commit clearly names it. The compare link shows the public repo (the guide and the mod lists), not the source code. v1.0.0 has no source commit line, because no commit subject clearly sets that number.

## v1.0.15 (2026-11-13)

- The license says exactly what counts as a paid server and rules out streams or videos that earn money, unless Coodsz says yes. The tools ask you to accept it once more.
- Every Coodsz module folder the tools put in place has a `LICENSE.txt`. Send my logs and crash reports list your Coodsz module versions.

[Release page](https://github.com/Coodsz/Coodsz-BurningEmpires/releases/tag/v1.0.15) · [Compare with v1.0.14](https://github.com/Coodsz/Coodsz-BurningEmpires/compare/v1.0.14...v1.0.15) · Source commit 4f317d8f

## v1.0.14 (2026-11-12)

- Both tools come in Simplified Chinese, Korean and Japanese too.
- When Bannerlord is newer than 1.4.8, the tools warn in the Made for card and under Connect or Start server. It turns nothing off.
- The I accept button in the Coop server terms window shows in your language. Start server turns on players' time buttons (pause and 2x) when an older `server-config.json` has no setting for them.
- A Coop folder the Client Launcher sets aside (a Coop nightly too) goes back into Modules when you close the launcher or on its next start after a crash.

[Release page](https://github.com/Coodsz/Coodsz-BurningEmpires/releases/tag/v1.0.14) · [Compare with v1.0.13](https://github.com/Coodsz/Coodsz-BurningEmpires/compare/v1.0.13...v1.0.14) · Source commit d6f37266

## v1.0.13 (2026-11-08)

- Save Rescue keeps the map moving when the server's own hero is a prisoner.
- The Client Launcher only got a new version number.

[Release page](https://github.com/Coodsz/Coodsz-BurningEmpires/releases/tag/v1.0.13) · [Compare with v1.0.12](https://github.com/Coodsz/Coodsz-BurningEmpires/compare/v1.0.12...v1.0.13) · Source commit cf0ac402

## v1.0.12 (2026-11-07)

- Client Launcher fix for a crash on the campaign map caused by old map shader files Bannerlord can't remove. Connect moves them aside, and asks once if Windows needs permission.
- Settings says why some difficulty boxes are greyed out (`mod-config.json` lacks them until the server has started once), and Save settings says when they were not saved.

[Release page](https://github.com/Coodsz/Coodsz-BurningEmpires/releases/tag/v1.0.12) · [Compare with v1.0.11](https://github.com/Coodsz/Coodsz-BurningEmpires/compare/v1.0.11...v1.0.12) · Source commit 62c3ab92

## v1.0.11 (2026-11-06)

- Joining your campaign no longer crashes while the game checks the campaign map. When one party, army, battle or settlement is broken, the game skips it and logs it to `CoodszJoinPassword.log`.
- Start server no longer adds Launcher Cheats and Launcher Battle Size to the server's `DedicatedServer.Windows` module over and over. It keeps one of each, removes repeats and backs up the file as `SubModule.xml.coodsz-original`.

[Release page](https://github.com/Coodsz/Coodsz-BurningEmpires/releases/tag/v1.0.11) · [Compare with v1.0.10](https://github.com/Coodsz/Coodsz-BurningEmpires/compare/v1.0.10...v1.0.11) · Source commit ad05f126

## v1.0.10 (2026-11-05)

- On Coop 0.1.7 the Client Launcher status no longer stays on Joining, and Send my logs packs the newest three numbered Coop client logs.

[Release page](https://github.com/Coodsz/Coodsz-BurningEmpires/releases/tag/v1.0.10) · [Compare with v1.0.9](https://github.com/Coodsz/Coodsz-BurningEmpires/compare/v1.0.9...v1.0.10) · Source commit ed6b6a36

## v1.0.9 (2026-11-03)

- Coop v0.1.7 is on the Steam Workshop, so the tools no longer send you to Coop's GitHub release.
- The Workshop item's own `DedicatedServer` folder is the server to use, and Prepare server copies it. A server picked with Choose Coop server is used instead, unless the Workshop server has a newer Coop.

[Release page](https://github.com/Coodsz/Coodsz-BurningEmpires/releases/tag/v1.0.9) · [Compare with v1.0.8](https://github.com/Coodsz/Coodsz-BurningEmpires/compare/v1.0.8...v1.0.9) · Source commit d668354c

## v1.0.8 (2026-11-03)

- The tools can show a notice from Coodsz at the top, in your language. Dismiss hides it until its words change.
- A Download Coop server button under Advanced server folder opens Coop's dedicated server download, and the Prepare server message names it when this PC has no Workshop Coop.

[Release page](https://github.com/Coodsz/Coodsz-BurningEmpires/releases/tag/v1.0.8) · [Compare with v1.0.7](https://github.com/Coodsz/Coodsz-BurningEmpires/compare/v1.0.7...v1.0.8) · Source commit 6577e959

## v1.0.7 (2026-11-01)

- A new look. Both tools have a sidebar. The Server Tool opens on a Dashboard with Mods and Made for cards, and the Client Launcher opens on Play.
- The Server Tool asks before Name server copy or Start server hosts a save that is earlier in the campaign than the one the server last ran. The save list shows each save's campaign day.
- The Mods page is locked while the server runs, and Prepare server says to stop the server first.
- The crash report bar waits for an automatic retry. The community mod list is for Coop v0.1.7.

[Release page](https://github.com/Coodsz/Coodsz-BurningEmpires/releases/tag/v1.0.7) · [Compare with v1.0.6](https://github.com/Coodsz/Coodsz-BurningEmpires/compare/v1.0.6...v1.0.7) · Source commit 2701eb61

## v1.0.6 (2026-10-31)

- A Choose Coop server button for when the Workshop Coop server is missing or older. Prepare server and Start server copy that server in, and ask before putting an older Coop over a newer one.
- Coop v0.1.7 installed by hand as `Modules\Coop` works, and Check mods says what to rename or move when Coop sits in the wrong folder.
- The Client Launcher unblocks Coop's downloaded files on Check mods and before the game starts. Join errors from Coop v0.1.7 are explained again.

[Release page](https://github.com/Coodsz/Coodsz-BurningEmpires/releases/tag/v1.0.6) · [Compare with v1.0.5](https://github.com/Coodsz/Coodsz-BurningEmpires/compare/v1.0.5...v1.0.6) · Source commit ee11294e

## v1.0.5 (2026-10-30)

- My reports is a real button that glows gold with a red count when Coodsz answers, and answers reach the tool much sooner.
- The crash screen and Send my logs spot five common setup problems (Smart App Control, leftover Common.dll or 0Harmony.dll, a DNS changer, Documents in OneDrive) and show the fix.

[Release page](https://github.com/Coodsz/Coodsz-BurningEmpires/releases/tag/v1.0.5) · [Compare with v1.0.4](https://github.com/Coodsz/Coodsz-BurningEmpires/compare/v1.0.4...v1.0.5) · Source commit ed523f7f

## v1.0.4 (2026-10-30)

- A red badge on My reports counts new answers, each report shows its status, and Did this fix it? buttons sit under each answer.

[Release page](https://github.com/Coodsz/Coodsz-BurningEmpires/releases/tag/v1.0.4) · [Compare with v1.0.3](https://github.com/Coodsz/Coodsz-BurningEmpires/compare/v1.0.3...v1.0.4) · Source commit c85288c5

## v1.0.3 (2026-10-30)

- Start server prepares the server folder by itself, and Prepare server checks the Coop Workshop download first and gives clearer messages.
- A Fix antivirus button, and a Donate button next to What's new. Play stays on until you press Pause.
- With no campaign yet, the Server Tool tells you to type a name and click New campaign.
- Install update works without a hidden PowerShell script. Crash reports add a text crash stack, and save backups work for a campaign that lost its .json or .sav.

[Release page](https://github.com/Coodsz/Coodsz-BurningEmpires/releases/tag/v1.0.3) · [Compare with v1.0.2](https://github.com/Coodsz/Coodsz-BurningEmpires/compare/v1.0.2...v1.0.3) · Source commit c67c78d0

## v1.0.2 (2026-10-29)

- Prepare server works with a fresh Workshop download of Coop. It no longer says the Coop dedicated server was not found because the Workshop copy lacks the start script the tool was looking for.

[Release page](https://github.com/Coodsz/Coodsz-BurningEmpires/releases/tag/v1.0.2) · [Compare with v1.0.1](https://github.com/Coodsz/Coodsz-BurningEmpires/compare/v1.0.1...v1.0.2) · Source commit 4d25a2bb

## v1.0.1 (2026-10-29)

- Install or repair Burning Empires asks before it opens your browser, only needs the hotfix when you copied Burning Empires yourself, and waits while files are still copying.
- With Steam closed, the tools tell you to open Steam. A Find Bannerlord button lets you pick the game folder.
- Answers to your reports show in the tool. A campaign that failed to load is never saved over.

[Release page](https://github.com/Coodsz/Coodsz-BurningEmpires/releases/tag/v1.0.1) · [Compare with v1.0.0](https://github.com/Coodsz/Coodsz-BurningEmpires/compare/v1.0.0...v1.0.1) · Source commit 5bbb09f1

## v1.0.0 (2026-10-28)

- The first release. One zip holds the Coodsz Burning Empires Server Tool and Client Launcher, with the modules inside and their own .NET.
- The Server Tool runs a Burning Empires 2.0.2 Coop server. Prepare server once, type a name and click New campaign. Install or repair Burning Empires puts in the main file and the hotfix from your Nexus download.
- The Client Launcher uses one locked setup, Burning Empires, and compares Harmony, ButterLib, UIExtenderEx and MCM v5.12.3 or newer with the host before the game starts.
- Burning Empires Coop Compat 1.0.0 makes the server decide populations, religions, wanderers, bandits, champions and recruits, and greys out or switches off single player only features. Windows only, Bannerlord 1.4.8 and Coop 0.1.6.

[Release page](https://github.com/Coodsz/Coodsz-BurningEmpires/releases/tag/v1.0.0)
