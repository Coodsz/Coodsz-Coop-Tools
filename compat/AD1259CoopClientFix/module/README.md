# AD1259 - Coop Client Stability

Version 1.0.1 for Bannerlord 1.4.8, AD1259 1.4.6 and Coop 0.1.6.

Install the `DedicatedServer.AD1259CoopClientFix` folder in the PLAYER game's
`Modules` folder. Enable **AD1259 - Coop Client Stability** after **AD1259** and
**Coop** in the singleplayer launcher. Keep the existing server compatibility mod.
No PowerShell script is needed for normal play.

The internal `DedicatedServer.` prefix uses Coop 0.1.6's existing exclusion for
optional compatibility modules during module matching. This is a client fix;
each player who needs protection installs it locally. It does not require a
server profile change or a new campaign. It is separate from the server module
because the dedicated bootstrap can probe either standard binary directory.

The captured map-loading crash occurs when `MapCameraView.UpdateMapCamera` calls
`MBMapScene.TickAmbientSounds` with a custom terrain index outside the engine's
26-entry audio table. This module skips that sound call for IDs outside 0-25.
It preserves navigation, terrain movement rules, save data and valid ambient sounds.

When Women in Calradia 10.7.7 is loaded, the module also prevents its daily companion
spawner from creating heroes locally during an active Coop client campaign.
Coop rejects that client-side creation and it repeatedly breaks Game.OnTick.
Existing companions and mod assets remain available. The guard permits spawning
in offline singleplayer and on a server; it checks Coop's active session state.

Diagnostic log: Documents/ Mount and Blade II Bannerlord/Client Diagnostics/
`AD1259CoopClientFix.log`. Look for the installation line and skipped terrain IDs.
Set `AD1259COOP_DISABLE_AMBIENT_GUARD=1` before launching to disable it for diagnosis.

The audio guard caught terrain ID 40 in a live join and passed the usual native
crash point. The updated companion guard still needs live verification.
This targets the demonstrated map-camera crash and client companion tick error;
separate startup crashes may require further investigation.
