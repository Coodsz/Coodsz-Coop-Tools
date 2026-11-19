Coodsz War Sync v1.0.0 (by Coodsz) - the TAOM server tool's War of the Ring sender

TAOM keeps its War of the Ring phase, outcome and momentum only on the server, so a joining player's TAOM map
bar never showed until a rejoin and then stayed frozen. This small server part sends that state to players who
have the Coodsz client add on (DedicatedServer.CoodszJoinPassword), once when they join and again whenever it
changes. For the war sync it does nothing else: no settings sync, no server rules, no commands, no setting.

It also carries the Improved Garrisons party size fix (compat\ImprovedGarrisonsCoopCompat\PartySizeHandBack.cs):
with Improved Garrisons on, its party size model now builds on TAOM's instead of the game's, so TAOM's party
sizes and the tool's party settings count on the server too. Players get the same fix in the client add on.

The TAOM server tool copies CoodszWarSync.dll and CoodszSettingsSync.Coop.dll into
engine\Modules\DedicatedServer.Windows\bin\Win64_Shipping_Server and adds a "Coodsz War Sync" SubModule entry to
DedicatedServer.Windows\SubModule.xml at every Start, the same way it adds Coop Save Rescue. The TAOM module list
and the saves stay the same, and Coop's join check never sees it.

It loads the server build of CoodszSettingsSync.Coop.dll (the same file Coodsz Server Tools ships) in War of the
Ring only mode (ServerSync.StartWarOnly): it puts the settings sync mark in Coop's module check answer so the add
on asks, answers only the War of the Ring part of that request, and leaves every player's mod settings alone. A
player's add on may write one "nothing came yet" settings line to its log after 30 seconds; that is expected.

When Coodsz Server Tools (DedicatedServer.CoodszTools) is loaded in the same server it already sends the war
state, so Coodsz War Sync stays off and logs one line. Without TAOM it logs one line and does nothing. Server
lines in the console start with "[Coodsz War Sync]".

Build: powershell -File build.ps1 [-ServerRoot <DedicatedServer folder>]
It copies both DLLs into src\TaomCoopHost\WarSync, which the TAOM server tool ships.
