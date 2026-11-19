# Copy marks (private)

Every Coodsz compat module and every tool exe carries a few harmless build marks, so a copied, renamed or
reuploaded DLL can be recognised later. They are in `CopyMark.cs` in each project. Nothing reads them at run
time, and none holds personal data. The compiler keeps them: an assembly attribute, and an internal class whose
two const strings stay in the DLL's metadata even though no code uses them. A plain decompile (dnSpy, ILSpy)
shows all three.

To check a suspect DLL or exe:

    powershell -ExecutionPolicy Bypass -File tools\Find-CopyMarks.ps1 "C:\path\to\Suspect.dll"

It prints every mark below that is in the file and which module it belongs to. The script reads this table,
so a new mark only needs a new row here. `CopyMarksTests` checks that each row matches its `CopyMark.cs`.

Triage of a report's coodsz-modules.txt: never trust its "our build" label. A report comes from the player's PC and
can be edited, so compare each SHA256 with KnownModuleHashes.txt and the module DLLs on master yourself, and look up
an unknown DLL with this script. `tools\Check-KnownModuleHashes.ps1` checks that every line of that list matches a
DLL build in git history (Workshop package builds excepted).

Kinds: attribute = `[assembly: AssemblyMetadata("CoodszBuild", ...)]` (UTF-8 in the attribute blob), type =
the odd class name (UTF-8 in the type names), seal and ledger = the two const strings (UTF-16 in the constants).
Someone who strips the attribute usually misses the class, and the class name survives a rename of the DLL.

Not marked on purpose: the AD1259 modules (AD1259CoopCompat, AD1259CoopClientFix) are SkyApolo's code, bundled
with permission, and UIExtenderEx, Harmony, MCM and the HexTool server submodules are third party.

| Module | DLL | Kind | Mark |
|---|---|---|---|
| Burning Empires Coop Compat | BurningEmpires.CoopCompat.dll | attribute | `czm-be-5RACZG9Y` |
| Burning Empires Coop Compat | BurningEmpires.CoopCompat.dll | type | `Kxt9uThrush` |
| Burning Empires Coop Compat | BurningEmpires.CoopCompat.dll | seal | `WPJU-7aee7b3a-thr` |
| Burning Empires Coop Compat | BurningEmpires.CoopCompat.dll | ledger | `cz.d9444d95ad` |
| Coodsz Coop Link | Coodsz.CoopLink.dll | attribute | `czm-lnk-XUNKEUG5` |
| Coodsz Coop Link | Coodsz.CoopLink.dll | type | `Kxks3Tern` |
| Coodsz Coop Link | Coodsz.CoopLink.dll | seal | `4DQC-9b72db7c-ter` |
| Coodsz Coop Link | Coodsz.CoopLink.dll | ledger | `cz.bdb0a2a75a` |
| Coodsz Image Exporter | CoodszImageExporter.dll | attribute | `czm-img-XMEKSKGP` |
| Coodsz Image Exporter | CoodszImageExporter.dll | type | `Kxgb4Wren` |
| Coodsz Image Exporter | CoodszImageExporter.dll | seal | `5SHG-ce2ad988-wre` |
| Coodsz Image Exporter | CoodszImageExporter.dll | ledger | `cz.5c68a0ddb1` |
| Coodsz Join Password | CoodszJoinPassword.dll | attribute | `czm-jp-8L5YJ84S` |
| Coodsz Join Password | CoodszJoinPassword.dll | type | `Kxkh2Plover` |
| Coodsz Join Password | CoodszJoinPassword.dll | seal | `WN4L-ac3de9c7-plo` |
| Coodsz Join Password | CoodszJoinPassword.dll | ledger | `cz.a26fbf026d` |
| Coodsz Menu Guard | CoodszMenuGuard.dll | attribute | `czm-mg-4W2N8829` |
| Coodsz Menu Guard | CoodszMenuGuard.dll | type | `Kx4b4Linnet` |
| Coodsz Menu Guard | CoodszMenuGuard.dll | seal | `48GS-16f6710d-lin` |
| Coodsz Menu Guard | CoodszMenuGuard.dll | ledger | `cz.2f09dc43b6` |
| Coodsz Server Tools | DedicatedServer.CoodszTools.dll | attribute | `czm-st-DG8NCHEW` |
| Coodsz Server Tools | DedicatedServer.CoodszTools.dll | type | `Kx32sDunlin` |
| Coodsz Server Tools | DedicatedServer.CoodszTools.dll | seal | `ZGCD-1ac7bb35-dun` |
| Coodsz Server Tools | DedicatedServer.CoodszTools.dll | ledger | `cz.a5a6427192` |
| Coodsz Settings Sync (client and server builds) | CoodszSettingsSync.Coop.dll | attribute | `czm-ss-4N5BRLW5` |
| Coodsz Settings Sync (client and server builds) | CoodszSettingsSync.Coop.dll | type | `Kxb44Merlin` |
| Coodsz Settings Sync (client and server builds) | CoodszSettingsSync.Coop.dll | seal | `5UY8-202ba1bd-mer` |
| Coodsz Settings Sync (client and server builds) | CoodszSettingsSync.Coop.dll | ledger | `cz.5c2cb1a240` |
| Coodsz War Sync | CoodszWarSync.dll | attribute | `czm-ws-J6ZURJSH` |
| Coodsz War Sync | CoodszWarSync.dll | type | `Kx4lqShrike` |
| Coodsz War Sync | CoodszWarSync.dll | seal | `MHFK-b10b221d-shr` |
| Coodsz War Sync | CoodszWarSync.dll | ledger | `cz.2ec21aa012` |
| Coop Save Rescue | CoopSaveRescue.dll | attribute | `czm-sr-B6HDTQ32` |
| Coop Save Rescue | CoopSaveRescue.dll | type | `Kxc37Bittern` |
| Coop Save Rescue | CoopSaveRescue.dll | seal | `B67R-be08baa1-bit` |
| Coop Save Rescue | CoopSaveRescue.dll | ledger | `cz.61233d6d9f` |
| Diplomacy Coop Compat | Diplomacy.CoopCompat.dll | attribute | `czm-dip-V733TMMZ` |
| Diplomacy Coop Compat | Diplomacy.CoopCompat.dll | type | `Kx84gSiskin` |
| Diplomacy Coop Compat | Diplomacy.CoopCompat.dll | seal | `XYCW-b768aea5-sis` |
| Diplomacy Coop Compat | Diplomacy.CoopCompat.dll | ledger | `cz.852afb032d` |
| Improved Garrisons Coop Compat | DedicatedServer.IGCoopCompat.dll | attribute | `czm-ig-U3FX5QUB` |
| Improved Garrisons Coop Compat | DedicatedServer.IGCoopCompat.dll | type | `KxantAvocet` |
| Improved Garrisons Coop Compat | DedicatedServer.IGCoopCompat.dll | seal | `JNMU-4e21b32a-avo` |
| Improved Garrisons Coop Compat | DedicatedServer.IGCoopCompat.dll | ledger | `cz.0fd02e7ac5` |
| Limitless Coop Compat | Limitless.CoopCompat.dll | attribute | `czm-lim-WFCYHK2R` |
| Limitless Coop Compat | Limitless.CoopCompat.dll | type | `Kx949Pipit` |
| Limitless Coop Compat | Limitless.CoopCompat.dll | seal | `2VQG-b45e8c37-pip` |
| Limitless Coop Compat | Limitless.CoopCompat.dll | ledger | `cz.5b2e9ccda1` |
| Realm of Thrones Coop Compat | RealmOfThrones.CoopCompat.dll | attribute | `czm-rot-VTP4ZZ4K` |
| Realm of Thrones Coop Compat | RealmOfThrones.CoopCompat.dll | type | `Kx46xHeron` |
| Realm of Thrones Coop Compat | RealmOfThrones.CoopCompat.dll | seal | `5TWJ-833389f5-her` |
| Realm of Thrones Coop Compat | RealmOfThrones.CoopCompat.dll | ledger | `cz.308d686c79` |
| Stuck Battle Fix | StuckBattleFix.dll | attribute | `czm-sbf-FBKAQSRZ` |
| Stuck Battle Fix | StuckBattleFix.dll | type | `Kx4nqGodwit` |
| Stuck Battle Fix | StuckBattleFix.dll | seal | `Z4RE-4dc0d9ad-god` |
| Stuck Battle Fix | StuckBattleFix.dll | ledger | `cz.24d7e004e9` |
| Coodsz Coop Client Launcher | Coodsz Coop Client Launcher.exe | attribute | `czm-ccf-63YBBQTX` |
| Coodsz Coop Client Launcher | Coodsz Coop Client Launcher.exe | type | `Kx9qjWhimbrel` |
| Coodsz Coop Client Launcher | Coodsz Coop Client Launcher.exe | seal | `RS2R-5ea7d613-whi` |
| Coodsz Coop Client Launcher | Coodsz Coop Client Launcher.exe | ledger | `cz.4d34f217ac` |
| Coodsz Coop Server Tool | Coodsz Coop Server Tool.exe | attribute | `czm-host-A7U4T5Q8` |
| Coodsz Coop Server Tool | Coodsz Coop Server Tool.exe | type | `KxllaTwite` |
| Coodsz Coop Server Tool | Coodsz Coop Server Tool.exe | seal | `C7MT-9ff55a55-twi` |
| Coodsz Coop Server Tool | Coodsz Coop Server Tool.exe | ledger | `cz.36b900ef0e` |
| Coodsz AD1259 Server Tool | Coodsz AD1259 Server Tool.exe | attribute | `czm-adh-GHQZ5X4B` |
| Coodsz AD1259 Server Tool | Coodsz AD1259 Server Tool.exe | type | `KxszvOuzel` |
| Coodsz AD1259 Server Tool | Coodsz AD1259 Server Tool.exe | seal | `LNE7-3b7d7050-ouz` |
| Coodsz AD1259 Server Tool | Coodsz AD1259 Server Tool.exe | ledger | `cz.70c2dd1b42` |
| Coodsz AD1259 Client Launcher | Coodsz AD1259 Client Launcher.exe | attribute | `czm-adl-2ZWEKSUN` |
| Coodsz AD1259 Client Launcher | Coodsz AD1259 Client Launcher.exe | type | `Kx89hBrambling` |
| Coodsz AD1259 Client Launcher | Coodsz AD1259 Client Launcher.exe | seal | `EV23-fe849dda-bra` |
| Coodsz AD1259 Client Launcher | Coodsz AD1259 Client Launcher.exe | ledger | `cz.60d3ef07d8` |
| Coodsz Burning Empires Server Tool | Coodsz Burning Empires Server Tool.exe | attribute | `czm-beh-SU2DULU5` |
| Coodsz Burning Empires Server Tool | Coodsz Burning Empires Server Tool.exe | type | `KxjkwRedpoll` |
| Coodsz Burning Empires Server Tool | Coodsz Burning Empires Server Tool.exe | seal | `2TWK-65a7d31b-red` |
| Coodsz Burning Empires Server Tool | Coodsz Burning Empires Server Tool.exe | ledger | `cz.a00b465198` |
| Coodsz Burning Empires Client Launcher | Coodsz Burning Empires Client Launcher.exe | attribute | `czm-bel-BLQNX7Z5` |
| Coodsz Burning Empires Client Launcher | Coodsz Burning Empires Client Launcher.exe | type | `Kxd7pSmew` |
| Coodsz Burning Empires Client Launcher | Coodsz Burning Empires Client Launcher.exe | seal | `T979-fc8f674a-sme` |
| Coodsz Burning Empires Client Launcher | Coodsz Burning Empires Client Launcher.exe | ledger | `cz.e6e0f6ed4c` |
| Coodsz Realm of Thrones Server Tool | Coodsz Realm of Thrones Server Tool.exe | attribute | `czm-roth-SHUV8XFU` |
| Coodsz Realm of Thrones Server Tool | Coodsz Realm of Thrones Server Tool.exe | type | `Kxp6bOsprey` |
| Coodsz Realm of Thrones Server Tool | Coodsz Realm of Thrones Server Tool.exe | seal | `Y3YJ-9bde138b-osp` |
| Coodsz Realm of Thrones Server Tool | Coodsz Realm of Thrones Server Tool.exe | ledger | `cz.0611257eba` |
| Coodsz Realm of Thrones Client Launcher | Coodsz Realm of Thrones Client Launcher.exe | attribute | `czm-rotl-CQJXEEQL` |
| Coodsz Realm of Thrones Client Launcher | Coodsz Realm of Thrones Client Launcher.exe | type | `Kx1pxKestrel` |
| Coodsz Realm of Thrones Client Launcher | Coodsz Realm of Thrones Client Launcher.exe | seal | `VUFU-179f40a1-kes` |
| Coodsz Realm of Thrones Client Launcher | Coodsz Realm of Thrones Client Launcher.exe | ledger | `cz.3d446d0e57` |
| Coodsz TAOM Server Tool | Coodsz TAOM Server Tool.exe | attribute | `czm-tah-HB3VMQ9E` |
| Coodsz TAOM Server Tool | Coodsz TAOM Server Tool.exe | type | `Kx8cyGarganey` |
| Coodsz TAOM Server Tool | Coodsz TAOM Server Tool.exe | seal | `H22Z-6ed0f055-gar` |
| Coodsz TAOM Server Tool | Coodsz TAOM Server Tool.exe | ledger | `cz.46925263ce` |
| Coodsz TAOM Client Launcher | Coodsz TAOM Client Launcher.exe | attribute | `czm-tal-4HS5GGQ5` |
| Coodsz TAOM Client Launcher | Coodsz TAOM Client Launcher.exe | type | `KxtdcRuff` |
| Coodsz TAOM Client Launcher | Coodsz TAOM Client Launcher.exe | seal | `V8BK-e0fdbb4c-ruf` |
| Coodsz TAOM Client Launcher | Coodsz TAOM Client Launcher.exe | ledger | `cz.5d22a0abb0` |

The exes are single file builds: the managed program sits uncompressed inside the exe, so the script finds the
marks in the exe itself.
