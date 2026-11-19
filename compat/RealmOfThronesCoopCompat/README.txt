Realm of Thrones Coop Compat v0.1.0 (by Coodsz)

Lets you play Realm of Thrones in Bannerlord Coop. The server runs Realm of
Thrones' campaign, its story events and its rules, and every player sees the
same world. It does nothing in single player.

The easiest way is the Coodsz Realm of Thrones tools. They carry this module
and set everything up for you:
https://github.com/Coodsz/Coodsz-RealmOfThrones/releases/latest

What you need
- Bannerlord 1.4.8 on Steam with the War Sails DLC (NavalDLC).
- Realm of Thrones 8.1.2 from Nexus Mods (mods/2907). Its modules say v8.1.8,
  and ROT_Map says v8.1.8.2.
- Harmony, ButterLib and UIExtenderEx from the Steam Workshop, and MCM from
  Nexus Mods (mods/612).
- The Realm of Thrones build of Coop, 0.1.8+01846e13 (made from Coop commit
  01846e136). It is not the Coop from the Steam Workshop. The tools carry it.
The host and every player need the same versions of all of these.

Where each part goes
- Player: put this RealmOfThrones.CoopCompat folder in the game's Modules
  folder and load it last, after ROT-Dragon.
- Host: put it in the server's engine\Modules folder and add
  RealmOfThrones.CoopCompat last in the "modules" list of server-config.json.
The server needs no Harmony copy. The module uses the one in Coop's folder.

Make the server map (host only)
The server cannot read Realm of Thrones' full map, so it needs a lighter copy.
The Server Tool does this for you. By hand:
1. On a PC with Realm of Thrones, run Stage-RotServerMap.ps1 from this folder:
   powershell -ExecutionPolicy Bypass -File Stage-RotServerMap.ps1 -RotMap "<game>\Modules\ROT-Map" -OutDir "<new empty folder>"
2. Copy the new ROT_Map\SceneObj\Main_map folder over the one in the
   server's copy of ROT_Map (engine\Modules\ROT_Map\SceneObj\Main_map).
3. Copy the new RealmOfThrones.CoopCompat\ModuleData\rot_map.txt over the one
   in the server's copy of this module. The one in this folder already fits
   ROT_Map v8.1.8.2.
4. Keep ROT_Map's own ModuleData\DistanceCaches folder in the server's copy.

Host rules and settings
Host rules and sending the host's settings to every player are part of the
Coodsz Realm of Thrones Server Tool, not this module.

Known limits
- Sieges are still being tested.
- In a Coop game the White Walker start is not offered in character creation.
- Enlisting in a lord's party, changing the storyline wars and sending ravens
  are off in Coop. The game says so when you try.
- A few details, like militia sizes, can show the old state on a player's PC.
- A player's first join prepares the map for several minutes. Wait for it.

Log files
- Game: Documents\Mount and Blade II Bannerlord\Logs\RealmOfThrones.CoopCompat\coopcompat.log
- Server: <BANNERLORD_USER_DIR>\logs\RealmOfThrones.CoopCompat\coopcompat.log,
  or the game path above when that folder is not set. Every line also shows
  in the server console, starting with [RealmOfThrones.CoopCompat].
