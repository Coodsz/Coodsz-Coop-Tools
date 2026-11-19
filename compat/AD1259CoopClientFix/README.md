# AD1259CoopClientFix

AD1259 - Coop Client Stability 1.0.1, made by SkyApolo and bundled with the Coodsz AD1259 Client
Launcher with their permission. The ready module is in the module folder; the launcher copies it into
the player's Modules folder on Connect and loads it right after Coop. Coop leaves DedicatedServer.*
modules out of the join check, so the server does not need it.

What it does (src\ClientAmbientGuard.cs): two Harmony prefixes, one skipping
MBMapScene.TickAmbientSounds for terrain IDs outside 0-25 (AD1259's custom terrain crashed the map
camera's ambient-sound lookup) and one stopping Women in Calradia companion spawns on an active Coop
client, plus a log in Documents\Mount and Blade II Bannerlord\Client Diagnostics. No network or other
file access. SkyApolo's review notes are in review.

The shipped DLL is SkyApolo's own build, tested in a live join (8,192 bytes, SHA-256
c207a136b387e11ac5277ac688601e6576433ea9c80de4b458dbc750992f8bca, see client-build-info.json). The
source matches it. Rebuild after a Bannerlord, Coop or Harmony update:

    .\build.ps1 -GameRoot "<Steam>\steamapps\common\Mount & Blade II Bannerlord" -ModuleRoot "module"

Without -ModuleRoot the build goes to build\ and the shipped DLL is left alone.
