Diplomacy Coop Compat v1.1.1 (by Coodsz)

Makes the Diplomacy mod safe in Bannerlord Coop.

In co-op the server runs the campaign. Players' PCs can't change the world
themselves: they ask the server through the game's normal buttons (Propose,
Gift fief, votes). Diplomacy's extra buttons change the world directly, so on
a player's PC they only changed that PC (lost influence, wars that never
started, pacts only you could see).

Only while a co-op game is running (single player is untouched):
- Off everywhere: war exhaustion, civil wars and rebel factions, kingdoms
  being wiped out by peace deals, "keep fief after siege", messengers and
  Return Fief (Diplomacy 1.5.4 and newer).
- Greyed out on players' PCs, with the reason in the tooltip: Diplomacy's
  Declare War / Make Peace / Break Alliance / Non-Aggression Pact buttons,
  Grant Fief, Donate Gold and Factions. Use the game's own Propose buttons and
  Kingdom > Fiefs > Gift instead; those go through the server.
- Diplomacy's background logic only runs on the server, so every player sees
  the same thing.
- The server answers Diplomacy pop-ups nobody can click: forced peace is
  accepted, AI peace offers to a player's kingdom are declined (propose peace
  yourself with the normal button), AI pact offers are declined.

Still works: Diplomacy's smarter AI war/peace choices, its influence rules,
and its fixes for kingdoms without a ruler (run by the server).

Install (server and every player):
1. Diplomacy and its requirements (Harmony, ButterLib, UIExtenderEx, MCM).
2. Put the Diplomacy.CoopCompat folder in Modules (players: the game's Modules
   folder; host: the dedicated server's Modules folder too).
3. Load order: Diplomacy.CoopCompat goes right after Diplomacy.
4. Everyone needs the same mods, versions and order, like any other coop mod.
   Diplomacy's MCM settings that matter are the server's.

Made for Diplomacy 1.5.3 to 1.5.5 (game 1.4.8) and Coop 0.1.6 and 0.1.7.
