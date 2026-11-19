# AD1259CoopCompat source

Made by SkyApolo; kept here with permission for the planned Coodsz AD1259 server tool
(not shipped in Coodsz Host). The ready module is in the module folder.

Rebuild after a Coop or Bannerlord update, against the dedicated server the module runs on:

    .\build.ps1 -ServerRoot "<Steam>\steamapps\workshop\content\261550\3770450698\DedicatedServer" -ModuleRoot "module"

The 1.1.1 DLL in the module folder was rebuilt from this source against the Coop 0.1.7 server's
game 1.4.8 files (SandBox, CampaignSystem, MountAndBlade and the server's Harmony). 1.1.0 was built
with -deterministic against the Coop 0.1.6 server.

The original developer notes follow.

---

# AD1259 Coop — development and maintenance

The playable module is the sibling **AD1259-Coop-CompatPatch** folder.
These files are development tools and one-time setup helpers; normal play uses the
compiled DLL through Bannerlord's module loader.

```powershell
.\build.ps1
.\Install-AD1259CoopCompat.ps1 -HideRawAssets -SkipHostJson
.\tests\Test-CompatLogic.ps1
.\tests\Test-RbmCampaignPolicy.ps1
.\tests\Test-ModuleInstall.ps1
.\tests\Test-ModulePackage.ps1 -CheckInstalled
.\Package-Release.ps1
```

The build uses the supplied server's actual assemblies and requires a .NET SDK or
Visual Studio Roslyn. The shipped module does not require either.

The installer supports an explicit `-ServerRoot`, `-ModuleRoot`, `-HostJson`,
`-WhatIf` and `-Uninstall`. Close Coodsz Host and stop the server before installation.
For an initial setup omit `-SkipHostJson` so it enables the module in the AD1259
profile. Previously configured servers can retain their existing profile.

`RUN-Server-Test.cmd` tests fresh campaign → save → quit → reload on the server
computer. It uses a throwaway save and restores launch configuration. It refuses
UNC installs so it cannot accidentally use another computer's Documents.

`build-info.json` records compiler input hashes. `bin` holds build output/symbols;
`Modules` preserves the old package layout. `previous-backups` contains backups
from the 1.0 release; `backups` holds current setup and configuration backups.
`TECHNICAL-NOTES-v1.0.md` preserves the earlier detailed explanation. `review`
contains conversation evidence and validation logs.

RBM policy uses the installed mod's Campaign enable switch. It overrides that
switch after `RBMConfig.LoadConfig()` before RBM registers campaign behaviors,
without changing Combat/AI switches or modifying RBM's DLLs or save flags.
