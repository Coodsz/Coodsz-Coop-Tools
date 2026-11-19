# CoodszJoinPassword

Client module shipped in the ClientAddOns folder of the Coodsz Coop and Friends, AD1259, TAOM, Burning Empires and Realm of Thrones client launchers.
It also carries Coodsz Menu Guard (compat\CoodszMenuGuard, second SubModule): keeping it inside this module
keeps the active mod list the same, because Bannerlord wipes its shader cache whenever that list changes
(tested 2026-10-15: adding the second SubModule to this id showed no "Mod change detected").

Coop 0.1.6 builds its /autoconnect client config from the address alone, so the first connect to a
password-protected server sends an empty password and the server answers "incorrect password" (Coop
then gets in on a later attempt). The launchers pass the password in COODSZ_COOP_PASSWORD; this module
fills an empty Config.Token from it in a Harmony prefix on CoopClient.Start. It does not edit
Coop.Core.dll, which TAOM.CoopCompat needs unchanged. The Coop and Friends launcher keeps its older
Coop.Core.dll edit (CoopPasswordPatch), which does the same thing.

It also carries the join baseline fix (src\JoinBaselineFix.cs). Coop refuses the join when the client has
fewer active parties than the server ("party count mismatch"), retries 16 times and leaves the player on
"Final Synchronization". Seen 2026-10-22 on a TAOM save where 5 parties came out of the client's load
inactive and outside the campaign party list ("baseline object outside local collection"). A Harmony prefix
on MobilePartyBehaviorSnapshot.TryApplyJoinBaseline puts every party the server's baseline lists, and this
client knows, back in the campaign lists and makes it active (IsActive is auto-synced, so the backing field
is written). A garrison gets its town back only when the town has no live garrison. Each one is logged as
"FIXED: party ...".

It also carries the TAOM enlist guard (src\TaomEnlistGuard.cs, rules in src\TaomEnlistGuardRules.cs). TAOM
2.0.29.5 enlists only on the authority, so on a Coop client "I swear it" (taom_enlist_oath_swear) does nothing,
the encounter stays open and the vanilla encounter menu starts a battle with the lord. After each campaign
load, on a Coop client (CoopClient.Start ran in this game, not a Coop server) with TAOM.dll loaded, the guard
sets the clickable condition of "I wish to enlist under your command" (taom_enlist_offer) so it is greyed out
with the hint "Enlisting is not supported in co-op yet", and wraps the oath line's consequence so it sets
PlayerEncounter.LeaveEncounter afterwards. TAOM's own code is not patched. It logs one line when it is on, and
one line when TAOM is loaded but the ids are not found. Without TAOM, on a server and in single player it does
nothing.

It also carries the War of the Ring sync (client side). TAOM keeps its War of the Ring phase and momentum only
on the server, so a joining player's TAOM top bar never appeared until a rejoin and then stayed frozen. The
add on asks for it inside the settings sync request. When the server runs Coodsz Server Tools v1.7.0 or newer (Coodsz Host) or Coodsz War Sync (the TAOM server tool)
with TAOM, it sends TAOM's phase, outcome and momentum save data once on join and whenever it changes. The
client puts it into TAOM's own objects by reflection and refreshes TAOM's map meter at once. It is automatic
with no setting, never turns anything on in TAOM, and does nothing without TAOM or if TAOM's members changed
(one log line). It is off when ModderLords.Compat is loaded, because the settings sync channel is off then.
Log lines start with "War of the Ring sync".

It also shows the PvP response timer's countdown. When the host turned on the response timer (Coodsz Server
Tools in Coodsz Host), the add on asks for it inside the settings sync request, and the server sends a start
when the caught player must answer. A small gold line at the bottom center of the screen then counts down by
itself in the game's language (English, Arabic, German, French, Portuguese, Russian or Turkish, else English):
"Answer in 12 s" for the caught player, "They have 12 s to answer" for the player who caught them. It goes at 0,
on the server's stop, or when Coop's meeting closes. The screen is module\GUI\Prefabs\CoodszPvpCountdown.xml.
Log lines start with "PvP countdown".

It also carries the Improved Garrisons party size fix (compat\ImprovedGarrisonsCoopCompat\PartySizeHandBack.cs).
Improved Garrisons' party size model starts from the game's own one, so TAOM's party sizes never counted. With
another mod's model loaded before it, Improved Garrisons now builds on that one and keeps its own special parties
and multipliers. Every PC works out party sizes by itself, so the servers run the same file (Coodsz War Sync and
Improved Garrisons Coop Compat). Log lines start with "Improved Garrisons party size".

It also carries the map check guard (src\MapCheckGuard.cs, rules in src\MapCheckGuardRules.cs). Bannerlord 1.4.8
runs Campaign.CheckMapUpdate while it loads a save made on a different map scene, which a Coop client loading the
dedicated server's save hits. A NullReferenceException in it crashed clients before the map (Burning Empires reports
CR-EH6KT, CR-A9XVH, CR-PM83C, CR-8ZXDY and CR-XG5NC, two players on one server and an older save). Harmony
finalizers on the five per object calls (Settlement, MapEvent and Army CheckPositionsForMapChangeAndUpdateIfNeeded,
and MobileParty's CheckPositions and CheckAi ones) skip a broken object and log it as "Map check: skipped ...", with
the party's id, its DefaultBehavior and the null target where it can tell. The first one also logs the full error.
A finalizer on CheckMapUpdate itself catches a null anywhere else in the check, logs the full error with its stack
and lets the load go on; the save's map CRC fields are then left as they were (the log says so). That is safe on a
Coop client because the server runs the campaign and sends positions. The per object finalizers are best effort,
since a caller the JIT compiled with one of them inlined skips the Harmony detour; the CheckMapUpdate finalizer is
the real safety net. Only a NullReferenceException is swallowed, only inside CheckMapUpdate and only while the Coop
client this game started is connected (Coop's CoopClient.isConnected), so a failed or refused join does not leave it
on for a later single player load. On a server and in single player it does nothing. Each Coop client load also logs
whether the save's map CRCs match this game's map, since only a mismatch runs the per object checks.

The id starts with DedicatedServer., which Coop leaves out of the join check, so servers need nothing.
It logs to Documents\Mount and Blade II Bannerlord\Client Diagnostics\CoodszJoinPassword.log and never
writes the password itself. Rebuild with .\build.ps1 after a Coop or Harmony update.

Tested 2026-10-14 on a password-protected AD1259 server: no "incorrect password" rejection, straight to the map.
