Coodsz Server Tools v1.8.1 (by Coodsz) - server only module for Coodsz Host

Module id DedicatedServer.CoodszTools. Coop leaves every DedicatedServer.* module out of the
client module check, so players do not need it and it does not change what a save needs.
Coodsz Host copies it into DedicatedServer\engine\Modules and adds it to the server's module
list on every Start. It never touches Coop's own folder or files.

It adds console commands that only the host can run (through server-data\console-queue):

  coodsz.ping                               COODSZ_TOOLS v1
  coodsz.heroes [name]                      H|id|name|clan id|clan|kingdom|gold|hp|max hp|level|party|place|player
  coodsz.hero <hero>                        HERO|id|name|level|gold|hp|max hp|age|influence|renown|clan tier|troops|wounded|...
  coodsz.skills <hero>                      DEV|..., ATTR|..., SKILL|id|name|level|focus|xp|attribute
  coodsz.equipment <hero>                   EQ|battle|slot|item|name|value and EQ|civilian|...
  coodsz.inventory <hero>                   INV|item|name|count|value
  coodsz.roster <hero>                      PARTY|id|troops|wounded and TROOP|members|id|name|count|wounded|xp|hero|tier|culture
  coodsz.gold <hero> <+/-amount>            coodsz.set_gold <hero> <gold>
  coodsz.influence <hero> <+/-amount>       coodsz.renown <hero> <+/-amount>     coodsz.clan_tier <hero> <tier>
  coodsz.health <hero> <hp>                 coodsz.age <hero> <age>
  coodsz.skill <hero> <skill> <+/-levels>   coodsz.skill_xp <hero> <skill> <xp>  coodsz.focus <hero> <skill> <+/-n>
  coodsz.attribute <hero> <attr> <+/-n>     coodsz.unspent <hero> attribute|focus <+/-n>
  coodsz.troops <hero> <troop> <+/-count>   coodsz.heal_troops <hero>            coodsz.troop_xp <hero> <xp>
  coodsz.give_item <hero> <item> <count>    coodsz.take_item <hero> <item> <count>
  coodsz.items                              every item, written to out\items.txt
  coodsz.troop_types                        every troop kind, written to out\troop_types.txt
  coodsz.world                              kingdoms, clans, lords, settlements and troop details for
                                            the Encyclopedia page, written to out\world.txt (read only)
  coodsz.map [parties]                      kingdom colours, settlement and party positions for the
                                            live map page, written to out\map.txt (read only)
  coodsz.terrain [columns] [row] [rows]     the terrain under a grid over the whole map (land, sea,
                                            rivers, forests, mountains) from the map scene, for the
                                            live map background, written to out\terrain.txt (read only)
  coodsz.clancap [clan kingdom]             the Max clans per kingdom cap and each kingdom's count; with
                                            a clan and a kingdom, whether that clan may join (read only)

Every change answers COODSZ_SET hero=... what=... old=... new=... with the value read back after
the change. A refusal answers COODSZ_ERROR .... Lists end with COODSZ_END.

Changes use the game's own methods on the server (HeroDeveloper, ItemRoster, TroopRoster, gold,
influence and renown actions), so Coop sends them to the players like any other campaign change.

Max clans per kingdom: Coodsz Host's Settings page writes
Documents\Mount and Blade II Bannerlord\CoopData\DedicatedServer\coodsz-server-tools.json:
  { "maxClansPerKingdom": 12, "countMercenaries": false }
0 or no file means no limit. The ruling clan counts; mercenaries count only with countMercenaries.
The module reads the file again within a few seconds of a change. When a kingdom is full, the
server refuses players joining it as vassals (and as mercenaries when they count), a ruler
recruiting a lord into it, and the AI's own join and defection choices (Harmony patches on the
server; players need nothing). Clans already in a kingdom stay. New kingdoms, rebellions,
marriages and companions made into clans are not capped. Each refusal is one server log line.

Server rules: the same file holds the rules from Coodsz Host's Settings page (Advanced: server rules):
  { "unstuck": "allowed", "pauseOnJoin": true, "freezeOfflineXp": true, "kingdomProtection": "offlineOnly" }
unstuck says who may use coop.unstuck. "allowed" (the default) is Coop's own behaviour. "notInFights"
refuses a player who is a prisoner, in a battle or siege, whose army is in a fight, who has a party at
war with them within 10 map units (outside settlements), or who used it in the last 5 real minutes.
"hostOnly" refuses every player request. The host's console coop.unstuck <controllerId> is never
checked. A refused player gets one System line in Coop's chat saying why, and the server log one line.
This is a prefix on Coop's PlayerUnstuckHandler.ApplyServerUnstuck, which only a player's own request
reaches. freezeOfflineXp (on by default) skips HeroDeveloper.AddSkillXp for the hero of a player who
is not connected (rebuilt from Coop's player manager every 2 seconds); coodsz.skill_xp still applies.
Gold is not frozen (Coop already freezes a disconnected player's economy). pauseOnJoin is read by
Coodsz Host, which pauses the game while a player joins.
kingdomProtection: "offlineOnly" and "off" are Coop's own
modOptions.blockAiWarDeclarationsOnOfflinePlayers, which Coodsz Host turns on or off in mod-config.json;
with no key (the default) Coodsz Host leaves that option as the host has it. The module does nothing for
any of these. "always" stops AI kingdoms from proposing or declaring war on any
kingdom a player rules or serves as a vassal (not as a mercenary), online or offline: a prefix on
KingdomDecisionProposalBehavior.ConsiderWar, a postfix on GetRandomWarDecision and a prefix on
DeclareWarAction.ApplyByKingdomDecision. Wars a player's kingdom declares, a player attacking first,
rebellions, crime, claims on the throne and calls to war go ahead. There is no forced peace. A hook that
does not resolve lets the war go ahead, with one server log line. Parts adapted from ModderLords by Ryvius
(@PlueRyvius), used with permission.

PvP rules: the same file holds the PvP rules card on Coodsz Host's Settings page, both off by default:
  { "pvpBlockLeave": false, "pvpResponseTimer": false, "pvpResponseSeconds": 30 }
pvpBlockLeave ignores the caught player's Leave ("I must leave now.") in a player vs player encounter when
the two are at war, and takes the line out of that player's dialog: prefixes on Coop's
PlayerPartyInteractionHandler.HandleResponderOption and SendResponderState. Coop's own war check is asked
again each time, so after peace made mid meeting Leave is no longer refused and shows again within a
second, even though Coop sends no new options for peace. A trade can always be left,
since Coop's barter Cancel is the same Leave. The caught player gets one System line in Coop's chat saying
why (English only), sent the way Coop's own chat sends System lines.
An encounter with no state change for pvpResponseSeconds (10 to 300) ends through Coop's own EndSession,
by whose turn it is. When the caught player has a demand or proposal to answer (pvpResponseTimer on), the
battle starts like a Refuse if the two are still at war, otherwise both part ways. When the attacker does
nothing (pvpResponseTimer or pvpBlockLeave on), the meeting ends and the caught player goes free. The
timer never starts a war, and trades are never timed. Turning pvpBlockLeave off while a meeting is open gives
the caught player Leave back (Coop's own options are sent again, or the meeting ends when they can't be). A
hook that does not resolve turns off only its rule, with one server log line.
With pvpResponseTimer on, players who have the DedicatedServer.CoodszJoinPassword add on see the seconds left
while the caught player must answer: "Answer in 12 s" for the caught player and "They have 12 s to answer" for
the other. The server sends one start (their game counts down by itself) and a stop on an answer, Leave, a
timeout or any other end of the meeting, peace made during a meeting at war, the turn passing to the attacker,
or the host turning the timer off. It rides the settings sync channel, and the add on asks for it inside its settings
request, so a player without it, or with an older one, gets nothing new and the timer works the same.

My Little Warband recruits: with CoopModPatch, a player's recruit screen shows custom troops from their own
My Little Warband tree, but Coop's server turned every one of them down ("Skipped 6 of 6 recruitment cart
entries"), since on the server those slots still hold the normal troops. This module now lets them through:
each custom troop the player picked is recruited as the normal troop in that slot by Coop's own recruit code,
and then swapped for the custom troop in the party. Gold is charged at the normal troop's price, not the
custom price the recruit screen shows. Recruit xp from perks (Famous Commander) moves with the custom troop.
If a slot changed after the recruit screen opened, the custom troop is recruited in place of the normal troop
now in that slot, at that troop's price.
It is on with no setting and needs CoopModPatch (with its My Little Warband adapter) and My Little Warband on
the server and on every player's game. In single player, or without those mods, it does nothing; when a cart
asks for a troop the server does not know that looks like a custom troop (most likely CoopModPatch is missing
on the server), the server log says so in one line. A Harmony prefix and postfix on Coop's
TroopRosterInterface.HandleOnRecruitmentDone.

Settings sync: players who join with a Coodsz launcher (the DedicatedServer.CoodszJoinPassword add on)
use this server's MCM settings for that session, in memory only; their own files never change. It is
on with no setup. Hotkeys, interface, sound and camera settings always stay each player's own.
CoodszSettingsSync.Coop.dll next to this module holds it (source compat\CoodszSettingsSync) and loads
only when Coop's types are found; otherwise one "Settings sync disabled" line. When ModderLords.Compat
is loaded it stays off and leaves the sync to ModderLords. recipes.json next to SubModule.xml lists the
mods whose settings players keep ("PlayersOwnSettings", ModderLords' format). The live folder holds the
host overrides Coodsz Host stages at Start and the settings the server reports to it.

War of the Ring sync: TAOM keeps its War of the Ring phase, outcome and momentum only on the server, so a
joining player's TAOM top bar never showed until a rejoin and then stayed frozen. This module sends that save
data to players who have the DedicatedServer.CoodszJoinPassword add on, once when they join and again whenever
it changes. It rides the settings sync channel (CoodszSettingsSync.Coop.dll), and the new add on asks for it
inside its settings request, so an older add on never gets the message. It is automatic with no setting, needs
TAOM on the server, and is off when ModderLords.Compat is loaded because the settings sync channel is off
then. Server lines in the console start with "[Coodsz Server Tools]". Coodsz Host, the AD1259 Server Tool and the Realm of Thrones Server Tool ship this module. The TAOM server
tool sends the same state with its small Coodsz War Sync (compatCoodszWarSync), which stays off when this
module is loaded. The AD1259 and Realm of Thrones servers run no TAOM, so this sync never starts there.

Build: powershell -File build.ps1 [-ServerRoot <DedicatedServer folder>]

Parts are ported from CoopKeep (https://github.com/Aderimo/bannerlord-coopkeep, MIT,
Copyright (c) 2026 aderimo); see THIRD-PARTY-NOTICES.md. The settings sync adapts parts of ModderLords
by Ryvius (@PlueRyvius), used with permission; see THIRD-PARTY-NOTICES.md.
