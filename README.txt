Paste each part as its own Discord message. A line that starts with # is a big heading.

# I made these tools with the help of the community and other sources for ease of access. I am not affiliated with the TAOM team in any way. They are here so hosting and playing with friends is easier.

Coodsz Coop tools players: see PART 4.

PART 1 — players

# Coodsz TAOM

Tools version: **v1.2.16** (Client Launcher and Server Tool), for **TAOM-Coop 0.3.25**.

Pick your language with the 🌐 button at the bottom of the sidebar in each tool. Arabic, German, French, Portuguese, Russian, Turkish, Simplified Chinese, Korean and Japanese are there. Log zips stay in English so anyone helping you can read them. After each update a What's new window shows once and says what changed.

You need Bannerlord **1.4.8** on Steam. If Steam updated it to a newer version, right click Bannerlord in Steam, then Properties, Betas, and pick v1.4.8.

Same set for everyone: TAOM, TAOM_Map, TAOM.Dependencies and LOTRLOME_Armory **2.0.29.5**, Workshop Coop **0.1.6**, TAOM.CoopCompat **0.3.25**.

TAOM 2.0.27 campaigns do not load on 2.0.29.5. Everyone moves to the new set together, and the host starts a new campaign.

# Download
https://github.com/Coodsz/Coodsz-TAOM/releases/latest

1. Download **Coodsz-TAOM-Tools**. It holds both programs, the client launcher and the server tool. Unzip it before you open anything, and give each Coodsz zip its own folder.
2. You don't need to download the CompatOnly zips anymore. The client launcher has `TAOM-Coop-v0.3.25-CompatOnly-CLIENT-PLAYTEST-1675bea7-a2ac41c2-r2.zip` built in, and the server tool has `TAOM-Coop-v0.3.25-CompatOnly-HOST-OPERATOR-1675bea7-a2ac41c2-r2.zip`. If you already have them on this PC, the tools use those first.
3. Subscribe to **Bannerlord Coop** on the Steam Workshop (item 3770450698) and stay subscribed. TAOM.CoopCompat 0.3.25 needs Coop **0.1.6**. Steam now has Coop 0.1.7, so get the Coop 0.1.6 client from Coop's GitHub release (`BannerlordCoop-v0.1.6.zip`) and unzip it into the game's Modules folder, so `SubModule.xml` sits in `Modules\Coop`. That Coop loads before the Workshop one. The Client Launcher says so too, and its **Get Coop 0.1.6** button opens that release:
https://github.com/Bannerlord-Coop-Team/BannerlordCoop/releases/tag/v0.1.6
Hosting a TAOM server needs the Coop 0.1.6 dedicated server, see PART 2.
4. Download TAOM **2.0.29.5** (`TAOM_Public_2_0_29_5.zip`, about 18 GB) and leave that zip anywhere on this PC:
https://www.moddb.com/mods/tales-from-the-age-of-men/downloads/taom-public-2-0-29-5

# If Windows warns you

If SmartScreen says "Unknown publisher", choose **More info**, then **Run anyway**. If Defender says virus, delete it and tell me.

# Do this
1. Close Bannerlord.
2. Open `Coodsz TAOM Client Launcher.exe` in that folder.
3. On **Play**, click **Check mods**. Then on **Settings**, click **Install or repair TAOM** (Play shows it too when TAOM needs it). When TAOM or TAOM.CoopCompat is missing or the wrong version, it installs all four TAOM 2.0.29.5 modules from the zip and TAOM.CoopCompat from the CompatOnly zip it has built in. Otherwise it repairs them.
4. On **Settings**, click **Use Workshop Coop 0.1.6**. It checks that the Workshop Coop is 0.1.6 and sets any other Coop copy in Modules (like the old 0.1.5 playtest copy) aside. It puts that copy back when you close the launcher, or when Bannerlord closes if the game is still open then. If Coop is missing, it subscribes in Steam first. While the Workshop has Coop 0.1.7, it stops there, says to get the Coop 0.1.6 client from step 3, and sets nothing aside. If your own Coop 0.1.6 is already in Modules, it keeps that copy and sets any other Coop aside.
5. Pick the same mod setup as the host in the setup box at the top of the window. **Plain TAOM** is the sealed TAOM-Coop list. It works when the host picked Plain TAOM too. **TAOM plus** is a locked set of mods tested in Coop with TAOM. For your own setup, click **New setup** or **Copy** on **Settings**, then **Add mods** or **Mod list** on **Mods**, then **Save mods** and **Install mods**. **Share mod list** (in the More menu) makes a file the host and your friends can import.
6. **Direct** (recommended): the host's IP and UDP port `4200`. **Steam lobby**: only if the host turned on Steam. Use the host's Steam profile name (the name friends see in Steam, not the login name), with Steam running on both PCs. Put a password only if the host has set one. If the server runs on this PC, the launcher fills in 127.0.0.1 for you. The **?** next to Steam lobby and Direct opens a short guide for each way to join.
7. To test the address first, click **Check server**. When the Coop server answers, click **Connect**. When nothing answers, the tool says what to check. **Connect** checks too: when nothing answers, or the address is your own PC, VPN or router, it says so before the game starts.
8. Click **Connect**. Before every launch the launcher clears the Windows download block on the DLLs in Modules, and fixes extra mods that ask for Harmony, ButterLib, UIExtenderEx or MCM so they use TAOM.Dependencies. The mods are put back when the game closes. If the game still says it cannot load a DLL, open **Settings** and click **Unblock DLLs**, then **Connect** again.

# If something goes wrong
- **Open client logs** (on **Logs and help**) opens Coop_client.log and a dated client-action.log copy for Discord. Passwords, addresses, and Steam IDs are removed from the action log.
- If the game closes, click **Send crash report** on the red bar, or **Send my logs** any time. It sends your cleaned Coop_client.log, client-action.log, the activity log, crash reports, engine logs with the watchdog log of each session, TAOM.CoopCompat logs, TAOM stall reports (taom_crash zips from the last 3 days) and the logs of a TAOM server on the same PC to Coodsz. You see every file first and nothing is sent until you click **Send**, then you get a short code to post in Discord instead of the files. Your Windows name, PC name, Steam ids, addresses and passwords (also the join password on the game command line) are taken out, and logs can still show in game player names. Saves and crash dumps are left out. **Save a copy instead** keeps the same cleaned zip on your PC. Both windows also have two optional extras: **Tell us what happened** (up to 2000 characters, cleaned like the logs, so names, Steam ids, addresses and passwords are taken out) and **Add screenshots** (up to 5 PNG or JPG, or press Ctrl+V to paste one). Screenshots are sent as they are, so check them for anything private before sending. Send with nothing added works as before, and **Save a copy instead** includes them.
- A yellow line by **Check server** warns when the PC is low on memory, or when a dedicated server runs on the same PC with less than 24 GB. TAOM can freeze when Windows runs out of memory. Close heavy apps, run the server on another PC if you can, and let Windows manage the page file. Connect still works.
- If a join fails, or the game closes before it joins, the log says what to change. Follow that line.
- If the game is choppy after you connect, turn **Low FPS fix** to On (on **Logs and help**). Close Bannerlord if it is open, then click **Connect** again. Turn it Off to put the patch shield back if that click was a mistake.
- If the game crashes with extra mods, click **Find the crashing mod** on **Logs and help**. It starts the game to the main menu with half of your extra mods at a time until one mod is left. **Safe mode** joins once with Plain TAOM, so it only works when the host runs Plain TAOM too.
- Each mod shows a badge from the TAOM mod list, with the note on hover, and your own result next to it (Worked for you, Crashed for you, Passed on your server). Mods no list knows show Unknown, not tested.
- **Clear shader cache** (on **Settings**) deletes only the shader folder in ProgramData. Close Bannerlord first. It does not touch mods or saves. There is no undo. The game builds the shaders again the next time it starts.

# Do not
- Do not unzip either compat zip, and do not copy them into Modules yourself.
- Do not mix versions. TAOM 2.0.27 with 0.3.25, or Coop 0.1.5 with 0.3.25, does not work.
- When another Coop is in Modules (like Coop 0.1.7 for the Coop or AD1259 tools), the launcher asks before it sets it aside, and puts it back when you close the launcher, or when Bannerlord closes if the game is still open then. Click **Restore parked Coop** on **Settings** only when that Coop did not come back. While the Workshop has Coop 0.1.7 and you have no Coop 0.1.6 of your own in Modules, the launcher doesn't ask or set anything aside, since that would not help.
- Do not turn on Harmony, ButterLib, or War Sails. War Sails does not work with TAOM 2.0.29.5.
- Do not use the TAOM.CoopCompat from the new TAOM download for Bannerlord 1.5.7 with these tools. They run TAOM 2.0.29.5 on Bannerlord 1.4.8 with Coop 0.1.6, so **Install or repair TAOM** and **Start server** leave that one alone and say so.
- Do not enlist or make a camp.
- Do not click **Install or repair TAOM** while Bannerlord is open. Repair is only for a damaged module.


PART 2 — host

# Coodsz TAOM server tool

Do the player steps first, so Bannerlord has the same modules. The server tool is in the same Coodsz-TAOM-Tools zip as the client launcher. The server tool has the CompatOnly-HOST-OPERATOR zip built in, so **Prepare server** needs no extra download.

Coming from 0.3.17? The tool moves to a new server folder (`Server-v0325-Coop-v016-r2`) and a new Data folder (`Data-v0325`) by itself. The old folders are not changed. Your old 2.0.27 campaign does not load, so make a new one.

**Coop 0.1.6 server.** Steam's Workshop has Coop 0.1.7, and TAOM.CoopCompat 0.3.25 needs Coop 0.1.6. So download the Coop 0.1.6 dedicated server yourself. Two places have it.
Coop's GitHub release, Windows dedicated server (`BannerlordCoop-DedicatedServer-Win64-v0.1.6.7z`, about 4.4 GB):
https://github.com/Bannerlord-Coop-Team/BannerlordCoop/releases/tag/v0.1.6
Nexus Mods, the archived DedicatedServer 0.1.6 file (needs a Nexus login):
https://www.nexusmods.com/mountandblade2bannerlord/mods/11879?tab=files&category=archived
Unpack it with 7-Zip into a folder outside Steam. On **Saves**, click **Choose Coop 0.1.6 server** and pick that folder. **Prepare server** and **Start server** copy from it and never change it. While the Workshop still has Coop 0.1.6, the tool uses the Workshop server as before.

# Do this
1. Open `Coodsz TAOM Server Tool.exe` in the Coodsz TAOM Tools folder.
2. Start a new TAOM 2.0.29.5 campaign in single player and save it. Pick that save on the **Saves** page. A vanilla new game and `default_new_game` are refused. **Start server** only loads a save already in the server Game Saves folder. If the line says Single player copy, click **Name server copy** first. Leave the name box blank to use that campaign's name.
3. On **Saves**, click **Prepare server** once. If that folder is already prepared, leave it.
4. Pick the mod setup your players use in the **Mod setup** box at the top of the window. **Plain TAOM** starts the sealed TAOM-Coop server like before. **TAOM plus** or a setup with extra mods starts the Coodsz TAOM server with those mods in the same order players load them. On the **Mods** page, **Import mod list** in **More** takes a player's shared list, and **Install mods** gets missing Workshop mods. To make or change your own setup here, use **New setup** or **Copy**, then **Add mods**, move or switch off the extra mods, and click **Save mods**. Plain TAOM and TAOM plus show a Locked tag. After a change, click **Share mod list** in **More** and send it to your players. The page says which mods every player has to get from Nexus Mods or ModDB. Picking a save made with another TAOM version, or with other mods, shows a warning and asks first.
5. On the **Dashboard**, click **Start server**. The first time, it shows the Coop server terms. Read them and click **I accept**, or the server will not start. It only asks again when Coop changes the terms.
6. Wait until the log says **SERVING**. A gold loading bar shows while it starts. The first start of a new campaign builds the map cache first and can take up to an hour. The status at the top says "Building the map cache: 36 of 221" while it works. When a save does not load, the status says **Crashed** with the reason under it. Do not stop the server then, or it starts over. Closing the host and opening it again still shows **Server online** when that server is still running. **Open server logs** on the **Logs** page opens the logs folder and selects the newest coop-server log plus a dated host-action-*.log copy for Discord (not the plain host-action.log).
7. If the server tool says it cannot load a DLL and the message includes the folder path, click **Unblock DLLs** in the server tool. That button turns red when the server log says Windows blocked a DLL. It clears the Windows download mark in the server folder. A startup line that only says `Cannot load: SandBox.dll` is not a blocked file, and it does not replace **SERVING**.
8. Give players your port-forwarded IP address and UDP `4200`. That is the recommended way. The **Dashboard** lists the addresses friends can use (Steam lobby, Radmin VPN or Hamachi, your public address, same Wi Fi, 127.0.0.1), each with **Copy**. **Copy invite** puts a short Discord message with the address and port on the clipboard, never the password. When your internet provider shares your address, the invite sends friends to Steam lobby first. **Open the port on my router automatically** opens UDP 4200 on your router while the server runs. **Allow through firewall** adds the Windows Firewall rule after you confirm. **How will friends join?** opens the join guide. To play on the same PC, use 127.0.0.1 in the client.
9. Steam lobby is optional. Tick **Advertise through Steam** on **Settings**, under **Join**, before you click **Start server**, keep Steam running, and give friends your Steam profile name. If a Steam join fails, use direct connect.

# While the server runs
- On the **Dashboard**, **Start server** turns into **Stop server** while the server runs, with **Save game**, **Pause**, **Play**, **Restart** and **Last good save** beside it. **Last good save** stops the server without saving, puts back the newest save from before the hero lost their party, and starts again.
- **Start server** and **Restart** load Coop Save Rescue every time. It fixes the known save problems (a saved open menu, a broken or empty battle, parties stuck in an ended battle, a killed lord still alive), keeps a backup from before the fixes, saves, and shows a note at the top until you press **Dismiss**. Players can join while it is on. If the server crashes while the save loads, it starts once more with the deeper fixes, never more. After **Stop server** the tool reads the log and says when the save has a problem the next Start fixes. **Run Rescue save** on the Logs page, with a **?** that shows the steps, runs every fix once (lords stuck with an empty party too) for support tickets. Press **Stop server** first, then **Run Rescue save**, and do not press **Start server** yourself. It starts the server for you. Wait about a minute until the log says Report. The game saves by itself. Then press **Stop server** and **Start server** to play normally.
- Campaign difficulty is on **Settings**. Pick Very easy, Easy or Realistic for each of the 9 options, click **Save settings**, then restart the server.
- **Stop server** and **Restart** first save and wait for the server's own "world saved" line, then stop the server, so the next start does not go back to an older save. **Restart** warns the players in chat 30 and 10 seconds before.
- After every save the tool zips a backup of the campaign into save-backups. **Restore save backup** on the **Saves** page puts one back. A copy is also kept before a save first runs with another mod list.
- **Test mods on the server** (**More** on the **Mods** page) tries each ticked mod alone with the TAOM core on a copy of the picked save named save-modtest, and says if it loads. Real saves are not written. **Stop** in that window ends the running test.
- A small line under the uptime shows the server's CPU and RAM use while it runs.
- **Delete server copy** on **Saves** removes the server's copy of the selected save. The single-player save stays.
- On the **Dashboard**, under online players: **Refresh**, **Kick player**, **Ban player** (red), and **Unban player**. Banned names stay on the list. A banned player is kicked again while this tool is open.
- Fast forward is on **Settings**. When it is off, the fast forward button stays at normal speed for players and for the host. When it is on, that button works for everyone. Save the setting, then restart the server from this tool. A server that is already running keeps the old setting until that restart.
- **Advanced: server rules** on **Settings** pauses the game while a player joins and freezes the skill XP of offline players. Both are on by default. Turn them off there if you don't want them. There you can also limit who may use unstuck and protect player kingdoms from AI wars. **Advanced: mod settings for players** sets your MCM values for everyone who joins. Looks mods stay each player's own. Players need nothing new.
- **Save game** keeps the server up and players connected. If the campaign looks broken, the tool will not write over the last playable save. That last good copy stays in the server save folder. Leave autosave off while you are checking a world that just broke.
- On **Cheats**, pick the player from the list. The list comes from the live server log. Console and Cheats send commands to the running server, also after you close this tool and open it again. **Save game** and the automatic ban kicks work the same way. Console has **Copy** and **Clear**.
- What the server prints in this tool is also kept in the logs folder as host-console-<date>.log, so it is still there after the tool closes.
- **Send my logs** (in the sidebar on every page and on the Logs page, and **Something went wrong? Send your logs** under Start server) sends the cleaned server logs, the host-console logs, the TAOM debug log, the engine logs and the server crash reports to Coodsz. You see every file first and nothing is sent until you click **Send**, then you get a short code to post in Discord instead of the files. Your Windows name, PC name, Steam ids, addresses and passwords are taken out, and logs can still show in game player names. Saves and crash dumps are left out. After a crash, or a stop nobody asked for, a red bar offers **Send server crash report**. **Save a copy instead** keeps the same cleaned zip on your PC. Both windows also have two optional extras: **Tell us what happened** (up to 2000 characters, cleaned like the logs, so names, Steam ids, addresses and passwords are taken out) and **Add screenshots** (up to 5 PNG or JPG, or press Ctrl+V to paste one). Screenshots are sent as they are, so check them for anything private before sending. Send with nothing added works as before, and **Save a copy instead** includes them.

# Password

Type a password in the server tool if you want one, and give players that same password. An empty password is fine. The client passes that password when you click **Connect**. It does not edit `Coop.Core.dll`. A password edit on that file crashes TAOM.CoopCompat. If that edit is already there, the client puts the released Coop 0.1.6 file back. The server tool does the same for the server copy when you click **Start server**.

# Do not
- Do not prepare the server a second time over a folder that already has a host.
- Do not start `BannerlordCoopServer.exe` yourself while also using this tool. Start it from the tool so the save and the log stay where the tool expects them.
- Do not load a TAOM 2.0.27 campaign on the new server. It does not load.
- Do not delete or rename single-player campaign saves. The tool copies the one save the server needs.
- Do not send anyone your Documents\Mount and Blade II Bannerlord\Game Saves folder.
- Do not share `taom-server.json`.
- Do not give friends port `7210`. Players use `4200`.
- Do not use the client **Unblock DLLs** button for the server folder. The server tool has its own button.

I will try my best to keep these updated and working with the latest release. Any bugs, challenges, feature ideas, or if you need help in any way, please let me know in this thread.


PART 3 — already have the tools

# Update the tools

When a newer release is out, the tool shows a line and an **Install update** button. The button stays highlighted until you click it. Click it. The button says **Installing**, and a progress bar moves while the download runs. When it is done, the tool closes and opens again on the new copy. The previous program files are removed after the new copy is in place. Your saved paths, password, and cheats stay. They live in %LOCALAPPDATA%\Coodsz\TAOM. A dedicated server that is already running stays up. The tool closes. The server does not.

From v1.2.0 the server tool is called `Coodsz TAOM Server Tool.exe`. When **Install update** updates `Coodsz Dedicated TAOM Server Tool.exe`, the new program takes the new name on its first start, moves your shortcuts to itself and removes the old exe. The client launcher keeps its name.

The first time you move to a copy that has this button, replace the programs once by hand. After that, use **Install update**.

# Do this by hand, if the button is not there yet

1. Close the client launcher and the server tool. Close Bannerlord too.
2. Download the new Coodsz-TAOM-Tools zip:
https://github.com/Coodsz/Coodsz-TAOM/releases/latest
3. Copy each program from its Coodsz TAOM Tools folder into the folder where you already have that tool, so it finds your settings. When Windows asks, choose **Replace**. The new program removes the old one with the old name.
4. Open the tools again.

Dragging the new files onto the old ones is the same thing. Close the tools first, or Windows will not replace a file that is still open.

# Moving from 0.3.17 to 0.3.25 (one time)

This update also changes the mods, so do these once after the tools are updated:
1. Nothing to download for TAOM-Coop 0.3.25. The tools have the CLIENT and HOST-OPERATOR zips built in.
2. Download TAOM 2.0.29.5 from the ModDB link in PART 1. Leave it zipped.
3. Stay subscribed to Bannerlord Coop on the Workshop. TAOM.CoopCompat 0.3.25 needs Coop 0.1.6. Steam now has Coop 0.1.7, so unzip the Coop 0.1.6 client from Coop's GitHub release into `Modules\Coop` (PART 1, step 3).
4. In the client: **Check mods**, then **Install or repair TAOM**. While the Workshop has Coop 0.1.7, **Use Workshop Coop 0.1.6** on **Settings** only says to get the Coop 0.1.6 client.
5. Host: make a new 2.0.29.5 campaign, pick it, **Prepare server**, then **Start server**.

# Do not

- Do not delete the old folder before the new version has started once. Your saved paths and password now live in %LOCALAPPDATA%\Coodsz\TAOM, and the new version copies them there from the old folder on its first start. Deleting the folder before that makes you type them in again.
- Do not delete the TAOM-Coop-Host folder or anything in Documents.
- Do not rename the old 0.3.17 compat zips to look new. The tools only pick up the 0.3.25 zips, so the old ones can stay where they are.

The new programs do the new checks themselves. The client raises Low terrain to Medium when you click **Connect**. The server tool copies missing .NET 6 files into the server folder when you click **Start server**. **Start server** only loads a save already in the server Game Saves folder. **Name server copy** makes that copy, and a blank name uses the campaign name. **Use Workshop Coop 0.1.6** checks the Workshop Coop, subscribes in Steam first if it is missing, and moves any other Coop folder out of Modules without deleting it. While the Workshop has Coop 0.1.7, it stops before it moves anything, unless your own Coop 0.1.6 is already in Modules. The server tool asks you to accept the Coop server terms on the first **Start server**. **Open client logs** selects Coop_client.log and client-action.log. **Send my logs** sends your cleaned Coop_client.log, client-action.log, the activity log, crash reports, engine logs and TAOM.CoopCompat logs to Coodsz after you see every file, and gives you a short code to post in Discord. Saves and crash dumps are left out. **Low FPS fix** On turns off TAOM's patch shield. Close Bannerlord if it is open, then click **Connect** again. Off puts the patch shield back. **Send my logs** on the server tool does the same with the host logs and the host-console logs. **Unblock DLLs** clears the Windows download mark. On the client it is on the **Settings** page, and the launcher also runs it on Modules before every launch. On the server tool that folder is the server folder. That button turns red when Windows blocked a DLL. It does not change the mod files. **Clear shader cache** deletes only the shader folder in ProgramData. Close Bannerlord first. It does not touch mods or saves. If `Coop.Core.dll` was edited for a password, the client puts the released Coop 0.1.6 file back, and the server tool does the same on **Start server**. That edit crashes TAOM.CoopCompat. **Install update** replaces the program only. It does not change the game, the mods, the server, or the save. After the new copy opens, the line and the button are gone until the next release. **Check server** on the client asks the Coop server for an answer, so a working server shows as online. **Start server** on the server tool loads Coop Save Rescue every time, which fixes a save that crashes on load, a frozen map and lords stuck in a battle. **Run Rescue save** on the Logs page also frees lords stuck with an empty party. Press **Stop server** first, then **Run Rescue save**, and it starts the server for you. Campaign difficulty is on the server tool's **Settings** page. Since v1.1.33 both tools have mod setups (Plain TAOM, TAOM plus, or your own with **Add mods** and **Mod list**), the dependency fix and **Auto sort**, **Find the crashing mod** and **Safe mode** on the client, and **Test mods on the server**, a backup after every save, a Stop that waits for the whole save, and the joining card with **Copy invite** on the server tool. **Send my logs** on the client sends the cleaned logs.

I will try my best to keep these updated and working with the latest release. Any bugs, challenges, feature ideas, or if you need help in any way, please let me know in this thread.


PART 4 — Coodsz Coop tools

# Coodsz Coop tools players start here

These are the tools for a normal Coop game, and for a TAOM map game that is not using the dedicated TAOM server tool above. Same Bannerlord **1.4.8**. These programs carry their own .NET, so you do not need to install the .NET 8 Desktop Runtime for them.

Tools version **v1.3.15**. The 🌐 button at the bottom of the sidebar picks the language, the same as in the TAOM tools.

# Download
https://github.com/Coodsz/Coodsz-Coop-and-Friends/releases/latest

Download `Coodsz-Coop-Tools-v1.3.15.zip` and unzip it before you open anything. Its `Coodsz Coop Tools` folder holds the two programs:

- `Coodsz Coop Client Launcher.exe` — players on the vanilla map and on More Nations Remastered.
- `Coodsz Coop Server Tool.exe` — the host.

Coodsz and Friends is now the **More Nations** switch in the client launcher, under **Campaign map**. When it is on, a setup with no saved map starts on More Nations Remastered Map. If you used `Coodsz and Friends.exe`, its next update turns it into `Coodsz Coop Client Launcher.exe` with the switch on, and your setups, mod list and settings come along. If you also used the Coop client launcher, you keep its setups too, and a More Nations setup with the same name shows up with (More Nations) after its name.

Unzip each Coodsz zip into its own folder.

# Players
1. Close Bannerlord.
2. Open `Coodsz Coop Client Launcher.exe`. It covers both maps.
3. Under **Saved setup**, pick your setup. The **Campaign map** box switches to the map that setup saved. Default and custom setups load their own map. The locked Vanilla setup loads the vanilla map. The locked More Nations setup loads the Remastered map. The locked **Plain Coop (no mods)** setup loads Coop alone on the vanilla map, for a server with no mods. Saved paths and cheats stay.
4. Steam lobby: put the host's Steam profile name (the name friends see in Steam, not the login name) in the Steam lobby box and connect from the lobby.
5. Direct connect: put the host's address and UDP port in the direct boxes. A home address only works on the same network. A port-forwarded address is what friends off that network use. If the server runs on this PC, the launcher fills in 127.0.0.1 and the server's port for you. The **?** next to Steam lobby and Direct opens a short guide for each way to join.
6. To test a direct address first, click **Check server**. When the Coop server answers, it shows as online. When nothing answers, the launcher says what to check. A Steam name cannot be checked this way. **Connect** checks too: when nothing answers, or the address is your own PC, VPN or router, it says so before the game starts.
7. Click connect. If Windows says Unknown publisher, choose More info, then Run anyway.
8. If the game says it cannot load a DLL, click **Unblock DLLs**. That button turns red when Windows blocks a DLL. Click it, then connect again.
9. **Add mods** opens mods already on this PC (Workshop, Nexus, ModDB folders). It does not download anything. Locked setups refuse Add mods. **Install mods** subscribes in Steam to the Workshop mods this setup is missing and waits for the downloads. **Import mod list** does the whole job. It makes a new setup when needed, downloads the Workshop mods from the list that you do not have, turns them on, and shows what it did. **Share mod list** saves each mod's Workshop link, so a friend's import can download it. Steam shows Bannerlord as running for that time. If Steam cannot be reached, it opens each Workshop page instead. If Steam will not download a Workshop update, the copy on your PC is used for that session so you can still connect. More Nations Remastered and its COOP Fix were removed from the Workshop, so Steam can not download them. Get them from Nexus Mods or ModDB, put their folders in the game's Modules folder, then click Refresh. A copy Steam downloaded before still works. COOP Server Settings Lock is no longer part of the setup and is not needed. A setup that still has it on shows a note each time the tool starts. Turn it off, and the host and every player turn it off together, since Coop checks that both sides load the same mods. The host now picks the settings it used to fix (Xorberax's Legacy, Diplomacy, Settlement Culture Drift and Agriculture Estate) on the Server Tool's MCM page, starting from the same values, and players get the host's values while they are on the server. Saves made with it still start without it. Each mod in the list shows a community badge (Works, Broken, Client only or Unknown) with the note on hover. Click it to report how the mod worked for you. Next to it you see your own result, like Worked for you or Crashed for you.
10. After you change mods, click **Save mods**. **Test to main menu** starts the game at the main menu without joining. If the game closes while starting, the activity log names the mod to turn off, or tells you what to change.
11. If a Workshop mod is also copied into the game Modules folder, the launcher uses the Workshop copy and parks the extra one: its SubModule.xml is renamed SubModule.xml.coodsz-off, so the game skips it. Nothing is deleted. Rename it back to use that copy again. Coop is the exception. A Coop in Modules\Coop is the one the game uses, so the launcher leaves it in place.
12. **Open client logs** is in the small arrow menu next to **Send my logs**. That click opens the game log folder and selects Coop_client.log and a dated client-action.log copy for Discord, or Coop Crash Reports if the game folder is missing. client-action.log sits in %LOCALAPPDATA%\Coodsz\Coop and records Connect, Unblock, Open client logs, Send my logs, and the other key clicks. Passwords, addresses, and Steam IDs are removed.
13. **Send my logs** is highlighted. It sends your cleaned logs to Coodsz: Coop_client.log, client-action.log, crash reports and engine logs. You see every file first and nothing is sent until you click **Send**, then you get a short code to post in Discord instead of the files. Your Windows name, PC name, Steam ids, addresses and passwords are taken out, and logs can still show in game player names. Saves and crash dumps are left out. After a crash the red bar offers **Send crash report**, and **Save a copy instead** keeps the same cleaned zip on your PC. Both windows also have two optional extras: **Tell us what happened** (up to 2000 characters, cleaned like the logs, so names, Steam ids, addresses and passwords are taken out) and **Add screenshots** (up to 5 PNG or JPG, or press Ctrl+V to paste one). Screenshots are sent as they are, so check them for anything private before sending. Send with nothing added works as before, and **Save a copy instead** includes them.

# Host
1. Do the player steps first so Bannerlord has the same modules.
2. Open `Coodsz Coop Server Tool.exe`.
3. Pick the save the server should load. Start server only loads a save the server already has. For More Nations Remastered, make the campaign in single player with that locked setup, then click Load single-player save and pick that .sav. The single-player file stays where it is. A campaign the server creates itself leaves the ground black. A vanilla or other-mod save stays that map. A server that is already running keeps its current campaign until you stop it and press Start server.
4. The host has its own **Campaign map** box on the Saves page. Choosing a saved setup switches it to the map that setup saved. Default and custom setups load their own map. The locked Vanilla setup loads the vanilla map. The locked More Nations setup loads the Remastered map. The locked **Plain Coop (no mods)** setup loads Coop alone on the vanilla map, for a server with no mods. Saved paths and cheats stay.
5. **Region** is on the Settings page, under Join: EU, NA, AS, OC, SA, or AF. Pick the region that matches your players.
6. **Set server folder** (Settings page, Advanced: server folder) chooses which BannerlordCoopServer.exe this tool starts. Use it when the dedicated server was not found, or this tool found the wrong copy. The file is in Modules\Bannerlord Coop\DedicatedServer, or in Workshop folder 3770450698\DedicatedServer.
7. Click **Start server**. Wait until it is serving. The status follows the server's own messages, so Server online only shows when the server is really ready, and a crash after that is told as a crash with its exit code. Closing the host and opening it again still shows Server online when the server is running. A small line under the Server status card on the Dashboard shows the server's CPU and RAM use.
   - **Restart the server if it crashes** (off by default, kept per setup) starts the server again after a crash. It waits 10 seconds, then longer after each crash, and gives up after more than 3 crashes in 10 minutes. It does not restart when the save does not load, the Coop files fail their check, or the server terms need accepting.
   - Coop 0.1.6 and newer: the first Start server shows the Coop server terms (EULA). The server only starts after you press **I accept**. Accept only if you agree. Host asks again only when Coop changes the terms.
   - Everyone needs the same Coop version. After a Coop update in Steam, players restart Steam so it downloads the update too. If it still doesn't, unsubscribe from Workshop item 3770450698 and subscribe again.
   - On Start server, Host copies any missing starter files into the DedicatedServer folder from its own DedicatedServerRepair pack (Start-ModdedServer.ps1, 0Harmony.dll, and the other starter files). Files you already have are left alone.
   - When Steam updates a Workshop mod (for example UIExtenderEx), players get the new version but the server keeps its own copy. Start server now copies the new Workshop version into the server so both match, and moves the old copy to DedicatedServer\removed-mods. Locked setups get this too.
   - The Steam Workshop server comes without the launcher add-ons that Cheats, Console, the player list, bans, Pause/Resume and battle size need. Start server copies them in and turns them on in DedicatedServer.Windows (the original SubModule.xml is kept as SubModule.xml.coodsz-original). A Workshop update can undo that, so Start server checks it every time.
   - Host will not start bare BannerlordCoopServer.exe when Start-ModdedServer.ps1 is missing. That bare start only loaded 5 core mods and broke joins.
   - To take a mod out of the server, stop the server, open the Mods page, and click **Delete** on that mod's row. Its folder moves to DedicatedServer\removed-mods, so you can move it back. Required mods, the working Coop mods, and locked setups have no Delete button. Click **Save module config** after that.
   - The Mods page shows how many mods are on. That start writes the locked setup's module list, including MCM and the framework mods when those folders are present. If a player is told the server does not support their modules, pick that same locked setup and press **Start server** again.
   - Locked setups keep CoopNightly off and use stable Coop. On a normal setup, Bannerlord Coop can be Coop or CoopNightly. Host parks the extra copy so only one loads.
   - If two enabled mods ship different copies of the same DLL (for example 0Harmony.dll), Start server prints a duplicate-DLL warning. Keep one copy, move the extra out of the other mod's bin folder, then Start server again. Do not delete the whole mod folder.
   - If the join says the Bannerlord builds do not match, update Steam Workshop item 3770450698. Do not copy the game folder onto the dedicated server.
   - If Start server says COOP MODULE VERIFICATION FAILED, a Coop file in the dedicated server is not the Coop build that server was released with. In Steam, open Bannerlord Properties, Installed Files, click Verify integrity of game files, wait for Workshop item 3770450698, then press Start server again. If it comes back, unsubscribe from 3770450698, subscribe again, and wait for the download. Do not copy Coop files from the game or the Workshop bin folder into the server.
   - If the server cannot load a DLL, click **Unblock DLLs** in `Coodsz Coop Server Tool.exe`. That button turns red when the server log says Windows blocked a DLL.
   - **Open server logs** is on the Logs page. That click opens the logs folder and selects the newest coop-server log, the newest host-console log, and a dated host-action-*.log copy for Discord (not the plain host-action.log). The host-console log is what the server printed in this tool, so it also has errors from a server that stopped before writing its own log.
   - **Send my logs** is in the sidebar on every page and on the Logs page, with **My reports** beside it once you sent a report, and **Something went wrong? Send your logs** sits under Start server. It sends the cleaned newest server logs, the host-console logs, host-action.log, both windows, the server settings without the password, a check of the server's Coop files, and the server's crash reports to Coodsz. You see every file first and nothing is sent until you click **Send**, then you get a short code to post in Discord. Your Windows name, PC name, Steam ids, addresses and passwords are taken out, and logs can still show in game player names. Saves and crash dumps are left out. After a crash, or a stop nobody asked for, a red bar offers **Send server crash report**. Both windows also have two optional extras: **Tell us what happened** (up to 2000 characters, cleaned like the logs, so names, Steam ids, addresses and passwords are taken out) and **Add screenshots** (up to 5 PNG or JPG, or press Ctrl+V to paste one). Screenshots are sent as they are, so check them for anything private before sending. Send with nothing added works as before, and **Save a copy instead** includes them.
8. Give players your address and port, or your Steam name for the lobby. The Dashboard lists the addresses friends can use (Steam lobby, Radmin VPN or Hamachi, your public address, same Wi Fi, 127.0.0.1), each with **Copy**. **Copy invite** puts a short Discord message with the address and port on the clipboard, never the password. When your internet provider shares your address, the invite sends friends to Steam lobby first. **Open the port on my router automatically** (on by default) opens the server's UDP port on your router while the server runs and closes it after. **Allow through firewall** adds the Windows Firewall rule after you confirm. **How will friends join?** opens the join guide.
   - **Test mods on the server** in the More menu of the Mods page tries each ticked mod alone with the working Coop core on a throwaway campaign and says which ones load. Your real saves and module lists are put back. Each mod shows a community badge and your own result, the same as in the launcher.
9. On the **Dashboard**, **Start server** and **Stop server** share the big button, with **Save game**, **Restart** and **Last good save** next to it. Start server and Restart load Coop Save Rescue every time: it fixes the known save problems, keeps a backup from before the fixes, saves, and shows a note until you dismiss it, and players can join while it is on. **Save game** keeps the server up and players connected. **Last good save** stops the server without saving, puts back the newest save from before the hero lost their party, and starts the server again. **Run Rescue save** on the Logs page, with a **?** that shows the steps, is for support tickets or a save Start did not fix. Press **Stop server** first, then **Run Rescue save**, and do not press Start server yourself: it starts the server once with every save fix (stuck town menu, broken battle, hostile town), and saves when it reports. Lords stuck on the map with an empty party lose that party and come back in a town or castle to raise a new one. No lord is killed. Then press **Stop server** and **Start server** normally. **Stop server** and **Restart** send the server its own stop, so it saves and closes by itself, and only end it after 2 minutes. **Restart** warns the players in chat 30 and 10 seconds before. After every save the tool zips a backup of the campaign into Save Backups and keeps the last 10 per save. **Open save folder** and **Load backup** sit next to the campaign save. **Load backup** lists those backups and Coop's own while the server is stopped, and keeps a copy of the current save before it puts one back. On the **Players** page, **Ban** on a player's row bans that player. Bans only take real SteamID64 numbers. The Coop server has no kick command, so a ban keeps that player from joining again; restart the server to remove someone who is already in. **Pause game** and **Resume game** stop and start campaign time. **Add SteamID** under Banned players on the same page bans someone who is not online. A setup is stored with **Save setup**.
10. Fast forward and the other Settings values use **Save settings**, then restart the server. Every Coop setting is on **Settings**, the rarer ones under **Show more Coop settings**. Empty boxes show Coop's own default, and a number outside the allowed range is refused. The settings are kept for the picked save, and **Start server** writes that save's settings into mod-config.json. **Save settings** works on locked setups too. It writes the difficulty and server options. It does not unlock or change the locked mod list. A running server keeps the old values until that restart. **Max clans per kingdom** in the **Kingdoms** group is the one exception. It is the most clans one kingdom can have, the ruling clan included (0 means no limit, **Count mercenaries** counts mercenary clans too). A full kingdom cannot take more clans from players or the AI, and the clans already in it stay. It is saved in DocumentsMount and Blade II BannerlordCoopDataDedicatedServerodsz-server-tools.json (maxClansPerKingdom and countMercenaries), and the running server uses it within a few seconds.
11. **Load single-player MCM** copies single-player MCM settings into this setup. The single-player files stay. A server that is already running keeps its current MCM until you stop it and press Start server.
12. If the campaign looks broken, the tool will not write over the last playable save. Leave autosave off while you are checking that world.
13. On **Cheats**, pick the player from the list. Commands go to the running server, also after you close this tool and open it again. **Run** next to List connected players reads the list again and asks the server who is online.
14. If RBM (Realistic Battle Mod) is on in this setup's mods, **Settings** shows the **RBM settings for this PC's game** card. It has RBM's four parts (Combat, AI, Tournament, Campaign) and the main campaign options, read from RBM's own config.xml in Documents\Mount and Blade II Bannerlord\Configs\RBM. **Coop friendly** turns off Lords buy equipment, which empties the town markets in coop. **Save RBM settings** changes only those values in the file and keeps a config.xml.bak the first time. It is the file your own game reads when you play from this PC. The Coop server does not run RBM's code (RBM's SubModule.xml has DedicatedServerType none), so RBM's campaign rules run in each player's own game and every player should use the same values. **Start server** no longer writes RBM's settings. Coop and Friends turns off Lords buy equipment in each player's own RBM config.xml on **Connect** while RBM is in the setup (the **Keep RBM coop friendly** box, on by default; it changes only that value and keeps a config.xml.bak the first time). If the card says RBM has not made its config.xml yet, play once with RBM on this PC.

# Update
When a newer release is out, the tool shows a line and an **Install update** button. The button stays highlighted until you click it. Click it. The button says **Installing**, and a progress bar moves while the download runs. When it is done, the tool closes and opens again on the new copy. If a program file is still locked, it is moved aside and the new copy starts. The previous program files are removed after the new copy is in place. Your saved paths, cheats, and mod folders stay. A dedicated server that is already running stays up.

From v1.3.0 the programs are called `Coodsz Coop Server Tool.exe` and `Coodsz Coop Client Launcher.exe`. When **Install update** updates `CoodszHost.exe`, `Coodsz Coop and Friends.exe` or `Coodsz and Friends.exe`, the new program takes the new name on its first start, moves your shortcuts to itself and removes the old exe.

After you update from v1.2.36 or older, press **Stop server** and **Start server** once so **Save game** works on that server.

After you update from v1.2.44 or older, the game rebuilds its shaders once, because the launcher now adds a small Coodsz module. After that it keeps them.

The first time you move to a copy that has this button, replace the programs once by hand. Close the tools first. Copy `Coodsz Coop Server Tool.exe` into the folder of `CoodszHost.exe` and `Coodsz Coop Client Launcher.exe` into the folder of `Coodsz Coop and Friends.exe`, and choose Replace. The new programs remove the old ones. Do not delete the old folder before the new copy has started once. On its first start it copies your settings to %LOCALAPPDATA%\Coodsz\Coop.

# Do not
- Do not delete the game, the mods, or the server save to update the tool.
- Do not start the Coop server exe yourself while also using this tool.
- Do not share your settings json. It sits in %LOCALAPPDATA%\Coodsz\Coop and holds your paths.

I will try my best to keep these updated and working with the latest release. Any bugs, challenges, feature ideas, or if you need help in any way, please let me know in this thread.

# License
The tools are free software under the GNU General Public License version 3 (LICENSE.txt). You may use, change and share them, as long as every copy you share keeps the same license and comes with its source code. Bundled mods and code from other people keep their own licenses (THIRD-PARTY-NOTICES.md). Each tool shows the full text once at the first start, and you press **I agree** to use the tools.
