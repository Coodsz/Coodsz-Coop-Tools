Improved Garrisons Coop Compat v1.0.0 (by Coodsz)

A server only module for Bannerlord Coop dedicated servers that run Improved
Garrisons with CoopModPatch. Coodsz Host adds it on every Start when
ImprovedGarrisons is in the server's mods. Players do not need it, and it
stores nothing in the save.

What it fixes
- Players keep their Improved Garrisons town settings. On load, and when a
  town changes hands, Improved Garrisons throws away the settings of every
  town the server's own hero does not own. On a dedicated server that hero
  owns nothing, so every player's settings were lost, and CoopModPatch then
  saved the empty list over its settings file. On the dedicated server the load
  clean up is skipped, and a town that changes hands loses its settings only
  when it goes to another clan (a clan member taking it keeps them).
- CoopModPatch puts the saved town settings back when the server starts,
  but Improved Garrisons can load its own save data a moment later and wipe
  them again, and CoopModPatch then saved the empty list. After Improved
  Garrisons loads its save data, CoopModPatch's saved town settings are put
  back again, with one line in the server log.
- Only a town's owner can change its garrison. CoopModPatch runs any
  player's Improved Garrisons request for any town. A request for a town
  owned by another clan is refused, with one line in the server log and a
  message to the player. Copy to specific garrisons must only name the
  player's own clan's garrisons. Copy to all towns or castles only copies to
  the player's own clan's towns or castles. A request from someone who is
  not a known player with a clan is refused. Only if Coop's player list
  cannot be reached at all do requests run without the check.
  Starting a transfer and the patrol, fortify and return orders only move
  the player's own troops, so they only need the player to own the town
  they start from.
- Another mod's party sizes count again. Improved Garrisons' party size
  model starts from the game's own one, so a mod loaded before it (TAOM)
  lost its party sizes. Now it builds on that mod's model and keeps its own
  special parties and multipliers. With no such mod nothing changes.
  Players get the same fix in the Coodsz client add on.

Every hook is optional. If a future Improved Garrisons or CoopModPatch
renames something, that part is left alone and the server log says so. The
log shows one ready line, for example: [IGCoopCompat] ready (4 of 4 hooks)

Made for Improved Garrisons 4.2.0.7 and CoopModPatch 0.7.1 (Coop 0.1.6).
It only calls their code by name; none of their code is copied here.
