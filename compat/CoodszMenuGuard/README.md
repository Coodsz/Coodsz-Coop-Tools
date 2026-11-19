# CoodszMenuGuard

The launchers ship this DLL inside the DedicatedServer.CoodszJoinPassword module (a second SubModule;
build.ps1 copies it there), so the mod list stays the same and Bannerlord keeps its shader cache. This
folder is the standalone module for players on the plain Coop client (zip with INSTRUCTIONS). Tested in
game 2026-10-15: both guards install on Bannerlord 1.4.8 with Coop 0.1.6.

Coop 0.1.6 sometimes syncs a party (in the ticket, an AI party in the village, not a player) to a
client without its full data. Entering a village or town while that party is inside makes `GameMenuPartyItemVM.RefreshProperties` throw a NullReferenceException. The settlement
overlay rebuilds its party list every frame, so it throws thousands of times and the screen freezes:
Escape and Enter do nothing and settlement names "ghost" across the map. Seen in a ticket on
2026-10-15 (village_B2_2, right after "Failed to get MobileParty using network handle").

The module puts a Harmony finalizer on `GameMenuPartyItemVM.RefreshProperties` and
`SettlementMenuOverlayVM.UpdatePartyList` that swallows only NullReferenceException: the broken entry
stays blank and the rest of the menu works. Any other exception still reaches the game.

The id starts with DedicatedServer., which Coop leaves out of the join check, so servers need nothing.
It logs to Documents\Mount and Blade II Bannerlord\Client Diagnostics\CoodszMenuGuard.log.
Rebuild with .\build.ps1 after a Bannerlord, Coop or Harmony update.
