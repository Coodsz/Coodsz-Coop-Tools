Limitless Coop Compat v1.1.0 (by Coodsz)

Makes the Limitless mod work in Bannerlord Coop, including big battles with
thousands of soldiers on the field at once.

Big battles
- Coop normally caps a battle at 1000 soldiers on the field and 2000 agents
  on each PC. With Limitless and this patch in the mod list, the host's
  battle size can go up to 12000, so whole armies fight at once.
- Set the size with Battle size in the Coodsz server tools (or battleSize in
  mod-config.json). Armies bigger than the setting still fight, the extra
  troops come in as reinforcements.
- Every player's PC shows the whole battle, so pick a size your friends'
  PCs can handle. Rough guide on a fast gaming PC: 2000 soldiers runs
  smooth, 4000 is a big battle for a strong PC, 6000 and up expect low FPS.
  Each player needs about 4.5 GB of RAM for the game plus about 0.5 GB per
  1000 soldiers.

Safety fixes (only while a co-op game is running, single player is untouched)
- Coop looks soldiers up by number to send hits to the right player and to
  remove troops that died on another PC. Limitless answers that from a
  shortcut list that can miss a soldier added and removed in the same
  moment, so the game's own exact lookup is used instead.
- Coop's agent limit counts corpses too. Limitless keeps every corpse, so on
  a Limitless PC the limit counts living soldiers, and corpses use
  Limitless's extra slots.
- Limitless's own battle size and riderless horse cleanup are off. Coop sizes
  co-op battles itself, and a horse removed on one PC would still stand on
  everyone else's.

Still works: Limitless's extra agent slots, unlimited corpses, distant
animation sampling and its AI and navmesh speedups.

Install
1. Everyone (players AND the server): Bannerlord.Harmony, Limitless and
   Limitless.CoopCompat, with the same versions. Coop only lets players join
   when the server lists the same mods.
2. Load order: Bannerlord.Harmony first, then Native and the official
   modules, Coop, Limitless, then Limitless.CoopCompat.
3. On a dedicated server Limitless itself does not run, it is only listed.
   This patch does load there, to allow the bigger battle size.

Made for Limitless 1.0.1 (game 1.4.8) and Coop 0.1.6.
