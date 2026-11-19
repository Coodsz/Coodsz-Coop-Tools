Stuck Battle Fix v1.2.0 (by Coodsz) - one-time save repair for the Coop dedicated server

For a co-op save with a battle that never ends:
- a battle that crashes every tick, so the whole map stops moving
  (server log full of: Failed to run "Game.OnTick" ... MapEvent.Update), or
- a battle left behind after a desync or a crash: lords sit at 0 troops on
  the map, the battle icon stays, and the town or castle can't be entered,
  or players only get an empty screen when they try to enter it.

What it does on the server:
- Keeps the map ticking when a battle crashes its update, and closes that
  battle. If closing fails too, it takes the parties out of that battle and
  drops it.
- 5 seconds after the save is loaded it ends every battle where one side has
  no healthy troops left and nobody won, the same way the game ends a siege
  battle after the fight. It also takes parties, towns and castles out of
  battles that already ended. Battles with a player's party are left to Coop.
- It also ends sieges nobody is besieging anymore (an empty siege camp, or
  besiegers with no healthy troops), so the town opens again.
- Writes every step to the server log with [StuckBattleFix].

How to use (server only, never on players):
1. Back up the save (.sav and .json).
2. Put the StuckBattleFix folder in the dedicated server Modules folder and turn it
   on for one start (after Bannerlord.Harmony and Coop).
3. Start the server with the broken save. Wait until the log says "Ended it",
   "Closed it" or "Load sweep found nothing stuck".
4. Save (Save game in Coodsz Host, or type save in the server window).
5. Stop the server, turn the mod off / remove it, and play on the saved file.
