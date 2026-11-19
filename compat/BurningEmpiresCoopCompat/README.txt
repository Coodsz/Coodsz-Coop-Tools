Burning Empires Coop Compat v1.0.0 (by Coodsz)

Makes the Burning Empires overhaul work in Bannerlord Coop.
This is the first release. Expect some rough edges and please report them.

In Coop the server runs the campaign. Burning Empires keeps a lot of its own
data (populations, religions, relations) and was built for one player, so on a
player's PC many of its features only changed that PC. This module makes the
server the boss and switches off what cannot work yet.

Only while a Coop game is running. Single player is not changed.

Who needs it
The server and every player. Everyone needs the same module list and the same
versions, like any other Coop mod. Coop's join check refuses players who differ.

Required modules and load order
1. Harmony
2. ButterLib
3. UIExtenderEx
4. MCM (Mod Configuration Menu)
5. Bannerlord Coop
6. Burning Empires Coop Compat (this module)
7. Burning Empires
8. Burning Empires Elephants (it comes with Burning Empires and always loads)
This module loads after Bannerlord Coop and right before Burning Empires,
because it has to fix one thing before Burning Empires starts.
Made for Burning Empires 2.0.2.

Setting up the dedicated server
Burning Empires only ships files for the game itself, not for the dedicated
server. On the PC that runs the server, copy these once:
- Modules\BurningEmpires\bin\Win64_Shipping_Client\BurningEmpires.dll and
  BurningEmpires.DistanceCacheProbe.dll into
  Modules\BurningEmpires\bin\Win64_Shipping_Server (make the folder if it is
  missing).
- UIExtenderEx 2.13.3 (the version Burning Empires was built with) into the
  same Win64_Shipping_Server folder. A newer UIExtenderEx does not work there.
- TaleWorlds.MountAndBlade.CustomBattle.dll from the game's
  Modules\CustomBattle\bin\Win64_Shipping_Client into the same folder.
These are your own copies of files you already have. Linux hosted servers cannot
do this, so they cannot run Burning Empires.

Hosting rules
- On a new server, Start server begins a fresh Burning Empires campaign by
  itself. After its first save it loads that save like any other. You can also
  host a single player Burning Empires save.
- Everyone uses the same Burning Empires version.
- Time speed is set by the server. Burning Empires' own time multiplier is
  turned off in Coop, so the game runs at normal speed on every PC, and
  Ctrl+Space speed up does nothing. The normal Space pause still works.
- Every server save keeps the Burning Empires data inside the save itself, and
  that copy is what loads after a restart. A single player save you host brings
  its data in the Burning Empires data folder (Modules\BurningEmpires\
  BESavedata), and the server only reads that folder until its first save of
  the campaign. Keep the folder until then if your host wipes files between
  runs, and keep the save's name if you copy the save over by hand.

Greyed out on players' PCs
These only changed your own PC, so they are greyed out, with the reason in the
tooltip or a message.
- Settlement rename.
- Moving population between settlements by hand.
- Starting armor crafting. Smithing, smelting and refining still work.
- Opening town management from the encyclopedia. Visit the town and use
  Manage town instead.
- Proposing a state religion or mainstream culture. These stay greyed out only
  if your Coop is too old to sync them. With a newer Coop they work as votes.

Switched off in Coop
- Champion duels.
- Burning Empires quests from its XML quest files (no quest givers, no quest
  parties).
- The wonder stash.
- Temple conversion, hero religion conversion and the "have a child" dialogue
  line.
- Armor crafting is off for everyone, so saved projects stay as they are.
- The population console commands that change data (add, remove, convert,
  migrate and so on). Players are told to run them on the server. Print
  commands still work.

What works differently
- The server decides populations, religions, wanderers, bandits, champions and
  recruits. Your PC does not run those rolls.
- You get the server's Burning Empires data when you join. After that it is not
  sent live, so population and religion screens can be out of date until you
  rejoin.
- Volunteers at notables are sent to players after the server rerolls them each
  day. Recruiting takes one adult from the town's population per troop.
- Kingdom religion votes (when Coop supports them) are decided by the server,
  and players see the result after it sends it.
- A new kingdom gets the religion most of its fief people follow, picked by the
  server. Nobody gets the founding religion pop up.
- Captives after a raid or siege are sold by default for players.
- Female players keep their parties when a state religion changes.
- The server has no screen, so Burning Empires pop ups there are written to the
  log instead.
- Nobody rides an elephant in a tournament, and elephant armor is never a prize.

Known limits
- Linux hosted servers cannot run Burning Empires, because it needs UIExtenderEx
  and MCM.
- The War Sails variant is not supported.
- The Historical campaign start is not supported in Coop.
- Champions and some tables can look stale on players' PCs until a rejoin.

Known issues
- On Coop 0.1.6, a hideout fight where your hero is knocked down never ends.
  This is a Coop bug, not Burning Empires and not this module. A Coop fix is on
  the way.
- On Coop 0.1.6, accepting some quests can freeze the dialog. This is a Coop bug
  too. Coop's development version replaced that quest system.
- The campaign map ground can look black on some PCs. This comes from Burning
  Empires itself, which is missing a terrain shader cache file. It happens in
  single player without Coop too. The Burning Empires author is looking into it.
- The Burning Empires population on your PC is a snapshot from the moment you
  join. It updates when you rejoin.

Report a problem
Tell Coodsz on the Coop Discord and send your game log. Say which Burning
Empires version you use and what you did before it broke.

Credits
Burning Empires is made by mumahutuo and the Burning Empires team, with project
lead Levente (Nexus Mods 7750, "Burning Empires"). Thank you for giving
permission for this Coop compat and the tools, and for adding Coodsz to the team.
Bannerlord Coop is made by the Bannerlord Coop team.
This module is made by Coodsz.
