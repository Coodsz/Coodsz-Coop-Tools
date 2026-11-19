# Discord announcement, Coop and Friends v1.2.57

Paste the text inside the block as one message. It stays under 2000 characters, has no colons and no hyphens between words.

```
**Coodsz Coop and Friends v1.2.57 is out**

Click "Install update" in Coop and Friends and in Coodsz Host.

**Plain Coop with no mods**
New locked setup "Plain Coop (no mods)" in both tools. The host picks it and starts a Coop server with no mods at all, and friends pick the same setup to join. Handy for a quick vanilla game, or to see if a problem comes from a mod.

**Max clans per kingdom**
New setting in Coodsz Host on the "Settings" page, in the "Kingdoms" group. It caps how many clans one kingdom can have, ruling clan included. A full kingdom can't take more clans from players or the AI, so one kingdom can't swallow every lord on a big server. Clans already in it stay. Tick "Count mercenaries" if mercenary clans should count too.
It's saved in coodsz-server-tools.json next to server-config.json (Documents\Mount and Blade II Bannerlord\CoopData\DedicatedServer) with the keys maxClansPerKingdom and countMercenaries. The server picks it up within a few seconds, no restart. 0 means no limit and that's the default, so nothing changes unless you set it.

**Adding mods to a campaign**
The "Add mods to this campaign" box lists the new mods by name and says a backup of the save is made first. Harmony and UIExtenderEx don't trigger it anymore, they keep nothing in the save. Saves from an older patch of the same game version load now too.

**Other stuff**
The two working setups are now called "Working stable mods for Coop 0.1.6", your setup and its settings carry over. Lots of translation fixes. The dropdowns say "On", "Off" and the difficulty names in your language, the filter buttons on the "Modules" page are translated, the "Cheats" page shows real hero names, and the number settings say what higher or lower does.

Let me know in this thread if anything acts up.
```
