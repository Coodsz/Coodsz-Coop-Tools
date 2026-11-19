<#
.SYNOPSIS
Signs the Coodsz module DLLs before a release, or the DLLs of a Workshop module package, and records their hashes.

.DESCRIPTION
Before a release (the first step):

    powershell -ExecutionPolicy Bypass -File tools\Sign-ModuleDlls.ps1

signs every module DLL of every tool (moduleDlls in tools\release-assets.json) and copies each signed file over
the byte equal copies in the repository. Commit the DLLs and src\BannerlordCoopLauncher.Core\Support\KnownModuleHashes.txt,
get the tests green, then run the release scripts: they find the DLLs signed and leave them as they are.

The Workshop modules (Burning Empires, Diplomacy and Limitless Coop Compat, Stuck Battle Fix) are packed by their
own Build.cmd, which runs this script on the packed folder before the zip is made:

    compat\<Module>\Build.cmd sign    signs the DLLs (az login needed, like a release), then records them
    compat\<Module>\Build.cmd         only records the hashes (-RecordOnly)

Recording adds each DLL's SHA256 to KnownModuleHashes.txt (for a signed DLL both the hash before and after
signing), so the tools' reports list that build. Commit that file with the upload.

What it takes, and it stops on anything else:
- a -Folder inside this repository that is a module package folder: compat\<Module>\module, artifacts\compat\<Module>,
  a module folder in Coodsz Host's payloads (src\CoodszHost\ServerTools, PatchModules, ImageExporter, SaveRescue)
  or src\TaomCoopHost\WarSync (Test-ModulePackageFolder);
- DLL names in moduleDlls or workshopModuleDlls;
- DLLs that hold every build mark docs\design\copy-marks.md lists for their name (Get-ModuleDllMarkProblem), so a
  renamed copy of someone else's DLL is never signed or recorded.
#>
[CmdletBinding()]
param(
    # A module package folder (see above). Empty: every tool's module DLLs.
    [string] $Folder = '',

    # Only add the hashes to KnownModuleHashes.txt, sign nothing (a Build.cmd run without sign).
    [switch] $RecordOnly
)

Set-StrictMode -Version 3.0
$ErrorActionPreference = 'Stop'
try {
    . (Join-Path $PSScriptRoot 'ReleaseAssets.ps1')
    if ($Folder) {
        # Resolved once, so the check and the listing look at the same folder (a relative path would otherwise be
        # read against the process folder by one and the PowerShell location by the other).
        $Folder = (Resolve-Path -LiteralPath $Folder).ProviderPath
        if ((Get-Item -LiteralPath $Folder -Force).Attributes -band [System.IO.FileAttributes]::ReparsePoint) { throw "$Folder is a link or junction, so nothing is signed or recorded." }
        if (-not (Test-ModulePackageFolder $Folder)) { throw "$Folder is not a module package folder of this repository (compat\<Module>\module, artifacts\compat\<Module> or a module payload folder under src), so nothing is signed or recorded." }
        $dlls = @(Get-ChildItem -LiteralPath $Folder -Recurse -File -Filter '*.dll' | ForEach-Object { $_.FullName })
        if ($dlls.Count -eq 0) { throw "$Folder has no DLL." }
    }
    else { $dlls = Get-AllModuleDllPaths }
    $allowed = Get-SignableModuleDllNames
    $rows = @(Get-CopyMarkRows)
    foreach ($dll in $dlls) {
        if ($allowed -notcontains (Split-Path -Leaf $dll)) { throw "$(Split-Path -Leaf $dll) is not a Coodsz module DLL (moduleDlls or workshopModuleDlls in tools\release-assets.json), so it is not signed or recorded." }
        $problem = Get-ModuleDllMarkProblem $dll $rows
        if ($problem) { throw $problem }
    }
    if ($RecordOnly) {
        $added = Add-KnownModuleHashes @($dlls | ForEach-Object { (Get-Sha256 $_) + '  ' + (Split-Path -Leaf $_) })
        Write-Host "Recorded $added new module DLL hashes in KnownModuleHashes.txt."
    }
    else {
        Initialize-Signing
        Invoke-ModuleDllSigning $dlls
    }
}
catch {
    Write-Host $_.Exception.Message -ForegroundColor Red
    exit 1
}
exit 0
