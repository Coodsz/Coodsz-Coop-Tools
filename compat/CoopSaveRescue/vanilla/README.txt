Coop Save Rescue v1.3.0 (by Coodsz) - for the plain Coop dedicated server

Use it when your Coop dedicated server save crashes right after loading,
or the whole map is frozen (everyone stands still).

It fixes these known problems:
- the save was made while a game menu was open (server crashes on load,
  last line: [GAME MENU] fortification_crime_rating)
- the host party is stuck inside a town of a kingdom it is at war with
- a broken battle that freezes the map
- a battle where one side has no men left, so it never ends and the lords
  in it stay stuck (seen after sieges)
- parties still tied to a battle that already ended (you see "field battle in
  progress" over them, but nothing happens and they never move)
- lords stuck on the map with an empty party: the empty party goes and the lord
  comes back in a town or castle later to raise a new one. No lord is killed.
- a lord who was killed but still counts as alive in the save crashes the
  server while it loads. The rescue finishes the death the game started.
- the server's own hero is a prisoner in the save, so the map stands still.
  Save Rescue keeps the map moving only while it is on (the hero stays a
  prisoner), so after step 8 it stops again. For this one, use the Coodsz
  Coop Server Tool, which loads Save Rescue on every start, or wait for
  Coop's own fix.

HOW TO USE
0. Back up your save: Documents\Mount and Blade II Bannerlord\CoopData\DedicatedServer\Game Saves
   (copy the .sav and .json of your save somewhere else)
1. Close the server.
2. Copy ALL files from this zip into your DedicatedServer folder
   (the folder with BannerlordCoopServer.exe in it).
3. Double-click Install-SaveRescue.cmd
4. Start the server like always.
5. Wait for this line in the server window (about a minute after SERVING):
      [CoopSaveRescue] Report: ...
6. Type  save  and press Enter.
7. Press F10 to stop the server.
8. Double-click Remove-SaveRescue.cmd
9. Start the server normally and play.

Players cannot join while Save Rescue is on. Always do step 8.
If the Report says "nothing to fix" and says nothing about the server's own
hero being a prisoner, your save has a different problem: make a support
ticket and send your server log. If it says the hero is a prisoner, see the
last problem in the list above.
