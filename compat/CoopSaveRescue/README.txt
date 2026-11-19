Coop Save Rescue v1.3.0 (by Coodsz) - save repair for the Coop dedicated server

Use it when a Coop dedicated server save crashes on load, or the whole map is frozen
(everyone stands still). Works on vanilla and modded saves: start the server with the
same mods the save uses, plus this module.

Known problems it fixes (each one is logged with [CoopSaveRescue]):
- Save made while a game menu was open: the server crashes reopening it
  (last line before the crash: [GAME MENU] fortification_crime_rating). Closes it.
- Main party inside a town or castle of a kingdom it is at war with. Moves it outside.
- A broken battle that freezes the map. Closes that battle.
- A battle where one side has no men left never ends, and the lords in it stay stuck
  (seen after sieges). Gives it to the side that still has men and closes it.
- Parties still tied to a battle that already ended (players still see "field battle in
  progress" over them, but the server no longer lists it). Lets them go.
- A lord whose party has no healthy men left, outside any battle, stands still for good.
  Resets him the way the game does after a lost battle: the empty party goes and the lord
  comes back in a settlement later to raise a new one. No lord is killed.
- A lord who was killed but still counts as alive in the save (his hero data is already
  gone) crashes the server while the save loads. Finishes the death the game started.
- A kingdom whose ruling clan has no leader crashes the server when a war is declared and
  its allies are asked to join (seen with TAOM's War of the Ring: "Failed to run Game.OnTick"
  after DefaultDiplomacyModel.GetRelationScore). That relation counts as neutral, and a call
  to war that still crashes is skipped. The war itself is declared. Only acts when the game
  really crashed there.
- A clan is destroyed and one of its dead heroes is not on his party's troop list any more:
  the server stops every time that day comes (IndexOutOfRangeException in
  TroopRoster.AddToCountsAtIndex, after KillCharacterAction.MakeDead; seen with TAOM). There
  is nothing to take off that list, so that one change is skipped and the death and the end
  of the clan go on. Only acts while a hero is being made dead and the game really crashed there.
- A child is born (seen with TAOM when a boy is born) and the server stops every time that day
  comes ("Game.OnTick threw ... NullReferenceException ... HeroChangeNameData", Coop #4104):
  Coop 0.1.6 sends the new name to the players and crashes when the first name is missing.
  The missing name is filled with the other one before Coop sends it, as the game does when a
  save loads, so the server and the players get the same name. A boy whose father is a player
  starts with no name at all (his father's character can have none, seen in CR-36XTG), so he gets an
  empty one for that moment; the game names him right after and Coop sends that real name.
  Only on the server, and only on a Coop build that still has that crash.
- A boy whose father is a player is born and the server stops right after the name fix above
  (NullReferenceException in CharacterObject.get_StealthEquipments, Coop #4104): in Coop 0.1.6
  a player's character has no culture of its own, and a boy is copied from his father's. The
  boy is copied from a lord template of his father's culture instead (then his mother's, then
  any culture), and the game gives him his culture, name and looks right after as usual.
  Only on the server, and only in a birth where the parent's character really is like that.
- The same birth then stops the server when the boy's looks are made (NullReferenceException in
  DefaultHeroCreationModel.GetStaticBodyProperties, Coop #4104): the player father's character
  has no body shape either. Before the birth a parent whose character has no culture or no body
  shape gets them back the way Coop fixed it on development (#3858): the hero's own culture, and
  the body shape of the character it was made from, else main_hero's, else a lord template's.
  The repair is kept after the birth. A parent that has both is never touched.
- The server's own hero is a prisoner in the save: the captivity menu keeps coming back and the
  whole map stands still. Keeps that menu shut so the world moves again, for as long as Save
  Rescue loads (the Coodsz server tools load it on every start). The hero stays a prisoner,
  so without Save Rescue the map stops again until Coop's own fix is out. Server only, never
  a player's hero.
About a minute after the campaign is up it prints:
  [CoopSaveRescue] Report: fixed N problem(s) ...   or   Report: nothing to fix was found ...

The module id is DedicatedServer.CoopSaveRescue. Coop leaves DedicatedServer.* modules out
of the join check, so players can join while it is on, and a save made with it still loads
without it (it stores nothing in the save).

Modes (environment variable COODSZ_SAVE_RESCUE, or a CoopSaveRescue.mode file next to the DLL):
- auto (default): only the fixes for data that is already broken. A broken battle is closed
  after it failed 3 updates in a row. Lords with an empty party are left alone.
- load: auto, plus a hero that fails to load is skipped so the rest of the save loads.
- full: everything above, a broken battle is closed on its first failure, and the empty
  lord party reset runs.

Coodsz Host, Coodsz AD1259 Server and the TAOM server tool load it on every start in auto
mode, show what it fixed, and save. After a crash while the save loads they start once more
in load mode. "Run Rescue save" on the Logs page starts the server once in full mode.

By hand (any dedicated server, SERVER ONLY, never on players):
1. Back up the save (.sav and .json).
2. Put the DedicatedServer.CoopSaveRescue folder in ...\DedicatedServer\engine\Modules\
3. Add a line  DedicatedServer.CoopSaveRescue  at the bottom of ...\DedicatedServer\modded-server-modules.txt
4. Start the server. Wait for the [CoopSaveRescue] Report line.
5. Type  save  in the server window and press Enter.
