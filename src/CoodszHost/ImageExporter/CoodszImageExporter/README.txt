Coodsz Image Exporter v1.0.0 (by Coodsz) - draws the game's pictures for Coodsz Host

The Cheats page, the player profile and the Items window show the game's own item and troop
pictures. They belong to TaleWorlds, so the Coodsz tools neither ship nor download them. Instead
"Build pictures" in Coodsz Host starts your installed game once with this module:

1. Coodsz Host copies the module into the game's Modules folder and starts Bannerlord with only
   Native, SandBoxCore, Sandbox and this module (COODSZ_EXPORT_AUTOSTART=1).
2. The module starts a plain sandbox game from the main menu, waits until the game data is loaded
   (the character creation screen), then draws every item, troop and lord with the game's own
   thumbnail renderer (the one the inventory screen uses) and saves them as PNG files:
     %AppData%\Coodsz\images\item\<item id>.png
     %AppData%\Coodsz\images\troop\<troop id>.png
     %AppData%\Coodsz\images\lord\<hero id>.png
3. It writes export-done.txt and closes the game. Coodsz Host removes the module folder again.

Nothing leaves your PC, nothing is saved in a campaign. While export-done.txt is there the module
does nothing; Build pictures deletes it to draw again (for example after adding mods).

Build: powershell -File build.ps1 [-GameDir <Bannerlord folder>]

Ported from CoopKeep's ImageExporter (https://github.com/Aderimo/bannerlord-coopkeep).
