<#
.SYNOPSIS
Writes LICENSE.txt into every Coodsz compat module folder that is zipped or packed.

.DESCRIPTION
    .\tools\Write-ModuleLicenses.ps1

compat\MODULE-LICENSE.txt is the one text. Each module that carries code from other people gets their
license notice after it (CoopKeep under MIT, ModderLords, which Ryvius relicensed under GPL-3.0 for this repository, see
THIRD-PARTY-NOTICES.md). Run it after changing the text or adding a module, and commit the copies.
CopyMarksTests checks that each copy starts with the text and carries its notices
(Every_packed_module_folder_carries_the_module_license...), and the release scripts and
Check-ReleaseAssets.ps1 fail a module payload without its LICENSE.txt.

Left out on purpose: the AD1259 modules (SkyApolo's code, bundled with permission), UIExtenderEx (LGPL,
ships its own license texts), Coodsz War Sync (two DLLs that go inside the TAOM server's
DedicatedServer.Windows module, not a module folder of their own), and the bare CoopSaveRescue.dll the TAOM and
Burning Empires Server Tools carry without its module folder.
#>
[CmdletBinding()]
param()

Set-StrictMode -Version 3.0
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

$coopKeep = @'
CoopKeep
Some code in this module was ported from CoopKeep (https://github.com/Aderimo/bannerlord-coopkeep) and stays under its MIT License:

MIT License

Copyright (c) 2026 aderimo

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
'@

$modderLords = @'
ModderLords
The settings sync and server rules code was adapted, with Ryvius's permission, from ModderLords (https://github.com/PlueRyvius/ModderLords). Ryvius relicensed that code under the GNU General Public License version 3 for the Coodsz tools, so it is under the same license as the rest of this module.

Required Notice: Copyright PlueRyvius (https://github.com/PlueRyvius/ModderLords)
'@

# Each folder that is zipped or packed, with the notices of the code from other people inside it. The
# compat source folders hold the copy their build script puts into the packed folder.
$modules = @(
    @('compat\BurningEmpiresCoopCompat', @()),
    @('compat\BurningEmpiresCoopCompat\module', @()),
    @('compat\RealmOfThronesCoopCompat', @()),
    @('compat\RealmOfThronesCoopCompat\module', @()),
    @('compat\CoodszJoinPassword\module', @($modderLords)),
    @('compat\CoodszMenuGuard\module', @()),
    @('compat\CoodszServerTools', @($coopKeep, $modderLords)),
    @('src\CoodszHost\ServerTools\DedicatedServer.CoodszTools', @($coopKeep, $modderLords)),
    @('compat\CoodszImageExporter', @($coopKeep)),
    @('src\CoodszHost\ImageExporter\CoodszImageExporter', @($coopKeep)),
    @('compat\ImprovedGarrisonsCoopCompat', @()),
    @('src\CoodszHost\PatchModules\DedicatedServer.IGCoopCompat', @()),
    @('compat\CoopSaveRescue', @()),
    @('src\CoodszHost\SaveRescue\DedicatedServer.CoopSaveRescue', @()),
    @('compat\DiplomacyCoopCompat', @()),
    @('compat\LimitlessCoopCompat', @()),
    @('compat\StuckBattleFix', @())
)

$base = [System.IO.File]::ReadAllText((Join-Path $root 'compat\MODULE-LICENSE.txt')).Replace("`r`n", "`n").TrimEnd()
foreach ($module in $modules) {
    $text = $base
    if ($module[1].Count -gt 0) {
        $text += "`n`n`nCode from other people in this module`n"
        foreach ($notice in $module[1]) { $text += "`n" + $notice.Replace("`r`n", "`n").TrimEnd() + "`n" }
    }
    $path = Join-Path (Join-Path $root $module[0]) 'LICENSE.txt'
    [System.IO.File]::WriteAllText($path, ($text.TrimEnd() + "`n").Replace("`n", "`r`n"), (New-Object System.Text.UTF8Encoding($false)))
    Write-Host "Wrote $path"
}
