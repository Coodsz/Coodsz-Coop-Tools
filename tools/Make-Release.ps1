<#
.SYNOPSIS
Builds the release zips for the Coodsz Coop tools and the TAOM tools.

.DESCRIPTION
Run from the repository root in PowerShell 7 (pwsh) or Windows PowerShell:

    .\tools\Make-Release.ps1 -BundledMods "D:\Coodsz\bundled-mods"

It reads the release number from ReleaseNotice.FriendsVersion, checks that the
Coodsz Coop Server Tool project version matches, runs the tests, publishes every
program as one self contained file, and writes these files to
artifacts\release-<version>:

    Coodsz-Coop-Tools-v<version>.zip         (Coodsz Coop Tools folder: the server
                                              tool and the client launcher, nothing else)
    Coodsz-Coop-and-Friends-v<version>.zip   (TEMPORARY legacy zip, see below)
    SHA256SUMS.txt

With -IncludeTaom it also writes the separate Coodsz-TAOM release to
artifacts\taom-release-<taom> (ReleaseNotice.TaomVersion):

    Coodsz-TAOM-Tools-v<taom>.zip                  (Coodsz TAOM Tools folder: both exes, nothing else)
    Coodsz-Dedicated-TAOM-Server-Tool-v<taom>.zip  (TEMPORARY legacy zip)
    Coodsz-TAOM-Client-Launcher-v<taom>.zip        (TEMPORARY legacy zip)
    the TAOM-Coop CompatOnly CLIENT and HOST-OPERATOR zips (also carried inside the TAOM tools)
    LICENSE.txt
    README.-.Instructions.txt
    SHA256SUMS.txt

The legacy zips keep today's asset names and layout with the new builds under
the old exe names, because the updater of a tool installed before the two exe
release picks its zip by those names and needs its own exe name inside. Each
new build renames itself on its first start (ToolRename). Coodsz and Friends.exe
became the client launcher's More Nations switch, so the Coop legacy zip ships
the client build under both old client names. Drop the legacy zips once the old
versions are gone.

The in-app updater finds the tool zips by name, so the version suffix is fine.
The updater every release since v1.2.23 ships removes every exe and dll of the
install folder that its payload root does not carry, so each TAOM legacy zip
also carries the other TAOM tool, and Assert-PayloadRoots checks it. The Coop
legacy zip ships each tool in its own folder, and the guides say to keep them
that way. The two exe zips keep exactly their two exes.
Every file a release must carry is listed in tools\release-assets.json. The
CompatOnly zips are not built here: they come from -AssetCache when the file is
there, otherwise from the newest Coodsz-TAOM release that has them. The script
stops when one is missing or its SHA256 differs from the json. The TAOM client
launcher also carries the CLIENT zip inside its exe and the server tool the
HOST-OPERATOR zip (src\TaomCompatZip.targets), and the release still attaches both.

Each release folder also gets release-body.md (the json's downloadLine first, then
docs\release-notes\v<version>-<product>.md without its leading comment) and
release-labels.json (the label of each legacy zip). The script prints the commands
that publish with that body, set the labels (tools\Set-ReleaseLabels.ps1) and then
run tools\Check-ReleaseAssets.ps1 on the published release.

Everything printed is also written to artifacts\make-release-last.log.

The client launcher only copies the mods listed in BundledModules.ModuleIds,
so the script stages exactly those folders from -BundledMods and stops when
one is missing. Workshop mods (Coop, Improved Garrisons) are never packed: a
second copy of them makes the game crash. COOP Server Settings Lock left the
setup in v1.3.4; the guard still refuses it, so an old copy never gets packed.

Every program carries its modules and bundled mods inside the exe (the
CoodszPayload items, src\CoodszPayload.targets) and writes them out to
%LOCALAPPDATA%\Coodsz on first use, so nothing ships in folders beside the exe.
#>
[CmdletBinding()]
param(
    # Folder that holds the non-Workshop mod folders the client installs
    # (PSCoopBridge, BannerFix, AliveScenes, ...).
    [Parameter(Mandatory = $true)]
    [string] $BundledMods,

    # Where the zips go. Default: artifacts\release-<version>
    [string] $Output = "",

    # Skip dotnet test (only for a quick local rebuild).
    [switch] $SkipTests,

    # Also build the TAOM release. It goes on the Coodsz-TAOM repo with its own
    # version (ReleaseNotice.TaomVersion), so it is off by default.
    [switch] $IncludeTaom,

    # Where the TAOM release goes. Default: artifacts\taom-release-<taom version>
    [string] $TaomOutput = "",

    # Folder with release files that are not built here (the CompatOnly zips).
    # Missing ones are downloaded into it. Default: artifacts\release-assets-cache
    [string] $AssetCache = "",

    # Builds and tests into this folder (dotnet --artifacts-path) instead of each
    # project's bin and obj, and keeps the log there. For a test run beside other work.
    [string] $ArtifactsPath = "",

    # Build even though the live test lock is held. Only when the lock holder agreed.
    [switch] $IgnoreLock,

    # TEST BUILDS ONLY: do not sign the exes. Prints a big warning, puts UNSIGNED TEST BUILD at the top of
    # release-body.md, prints no publish commands, and Check-ReleaseAssets fails a release made from it.
    [switch] $SkipSigning
)

Set-StrictMode -Version 3.0
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem

$root = Split-Path -Parent $PSScriptRoot
Set-Location $root
. (Join-Path $PSScriptRoot 'ReleaseAssets.ps1')
Assert-LiveTestLockFree -IgnoreLock:$IgnoreLock
# The real Realm reward key and Coodsz Host's prebuilt server DLLs (CONTRIBUTING.md, Private files).
$privateRoot = Assert-PrivateFiles -RepairDlls
. (Join-Path $PSScriptRoot 'PayloadCheck.ps1')

# Keep a copy of everything this run prints, for checking afterwards.
$logFolder = if ($ArtifactsPath) { $ArtifactsPath } else { Join-Path $root 'artifacts' }
New-Item -ItemType Directory -Force -Path $logFolder | Out-Null
Start-Transcript -LiteralPath (Join-Path $logFolder 'make-release-last.log') -Force | Out-Null

function Fail([string] $message) {
    throw $message
}

# The TAOM plus mods with Workshop id 0 ("Id|Name|0" in TaomPlus.cs) ship next to the TAOM tools.
# TaomPlusTests checks this pattern against TaomPlus.BundledIds.
function Get-TaomBundledIds([string] $source) {
    [regex]::Matches($source, '(?<=[";])(?<id>[A-Za-z0-9._]+)\|[^|;"]+\|0(?=[;"])') | ForEach-Object { $_.Groups['id'].Value }
}

$exitCode = 0
try {

function Run([string] $file, [string[]] $arguments) {
    Write-Host ("> " + $file + " " + ($arguments -join ' ')) -ForegroundColor DarkGray
    & $file @arguments
    if ($LASTEXITCODE -ne 0) { Fail ($file + " failed with exit code " + $LASTEXITCODE + ".") }
}

# 1. Version
$notice = Get-Content -Raw -LiteralPath 'src\BannerlordCoopLauncher.Core\Updates\ReleaseNotice.cs'
$versionMatch = [regex]::Match($notice, 'FriendsVersion\s*=\s*"v(?<v>[0-9.]+)"')
if (-not $versionMatch.Success) { Fail 'Could not read FriendsVersion from ReleaseNotice.cs.' }
$version = $versionMatch.Groups['v'].Value
$hostProject = Get-Content -Raw -LiteralPath 'src\CoodszHost\CoodszHost.csproj'
$hostVersion = [regex]::Match($hostProject, '<Version>(?<v>[^<]+)</Version>').Groups['v'].Value
if ($hostVersion -ne $version) {
    Fail ("ReleaseNotice.FriendsVersion is v$version but CoodszHost.csproj says $hostVersion. Make them the same first.")
}
Write-Host "Release v$version" -ForegroundColor Green
$taomVersion = ''
if ($IncludeTaom) {
    $taomMatch = [regex]::Match($notice, 'TaomVersion\s*=\s*"v(?<v>[0-9.]+)"')
    if (-not $taomMatch.Success) { Fail 'Could not read TaomVersion from ReleaseNotice.cs.' }
    $taomVersion = $taomMatch.Groups['v'].Value
    Write-Host "TAOM tools v$taomVersion (separate Coodsz-TAOM release)" -ForegroundColor Green
}
# The release body is made from these notes at the end, so stop now when they are not written yet.
Get-ReleaseNotesFile 'Coodsz/Coodsz-Coop-and-Friends' $version | Out-Null
if ($IncludeTaom) { Get-ReleaseNotesFile 'Coodsz/Coodsz-TAOM' $taomVersion | Out-Null }

# Code signing (Microsoft Artifact Signing): check az login and restore the pinned tools now, so a missing
# login stops the release before the long build. -SkipSigning is for test builds only.
if ($SkipSigning) { Write-UnsignedWarning } else { Initialize-Signing }

if ([string]::IsNullOrWhiteSpace($Output)) { $Output = Join-Path $root "artifacts\release-$version" }
$work = Join-Path $Output 'work'
if (Test-Path -LiteralPath $work) { Remove-Item -LiteralPath $work -Recurse -Force }
New-Item -ItemType Directory -Force -Path $work | Out-Null
if (-not (Test-Path -LiteralPath $BundledMods)) { Fail "The bundled mods folder does not exist: $BundledMods" }

# 2. Bundled mods: exactly the list the launcher installs
$bundledSource = Get-Content -Raw -LiteralPath 'src\BannerlordCoopLauncher.Core\Launch\BundledModules.cs'
$listBlock = [regex]::Match($bundledSource, 'ModuleIds\s*=\s*\[(?<list>[^\]]*)\]').Groups['list'].Value
$bundledIds = [regex]::Matches($listBlock, '"(?<id>[^"]+)"') | ForEach-Object { $_.Groups['id'].Value }
if (-not $bundledIds) { Fail 'Could not read BundledModules.ModuleIds.' }
$workshopOnly = @('Coop', 'CoopNightly', 'ImprovedGarrisons', 'ServerSettingsLock')
foreach ($id in $bundledIds) {
    if ($workshopOnly -contains $id) { Fail "$id is a Workshop mod and must not be bundled." }
    if (-not (Test-Path -LiteralPath (Join-Path $BundledMods "$id\SubModule.xml"))) {
        Fail "Bundled mod $id is missing: $(Join-Path $BundledMods $id)\SubModule.xml"
    }
}
# One folder per program that carries them, with exactly its mods. The build packs it into the exe
# (-p:CoodszBundledMods), and Connect or Install mods writes the mods out before copying them.
$bundledCoop = Join-Path $work 'bundled-coop'
New-Item -ItemType Directory -Force -Path $bundledCoop | Out-Null
foreach ($id in $bundledIds) { Copy-Item -LiteralPath (Join-Path $BundledMods $id) -Destination (Join-Path $bundledCoop $id) -Recurse }
foreach ($id in $workshopOnly) {
    if (Test-Path -LiteralPath (Join-Path $bundledCoop $id)) { Fail "$id ended up in the client's bundled mods. Remove it." }
}
$bundledTaom = Join-Path $work 'bundled-taom'
if ($IncludeTaom) {
    # The TAOM plus mods with no Workshop item: the client copies them into Modules on Install mods,
    # the server tool copies them into the server for the Coodsz TAOM server.
    $taomBundled = Get-TaomBundledIds (Get-Content -Raw -LiteralPath 'src\TaomCoop.Core\TaomPlus.cs')
    if (-not $taomBundled) { Fail 'Could not read the bundled TAOM plus mods from TaomPlus.cs.' }
    New-Item -ItemType Directory -Force -Path $bundledTaom | Out-Null
    foreach ($id in $taomBundled) {
        if ($workshopOnly -contains $id) { Fail "$id is a Workshop mod and must not be bundled." }
        if ($bundledIds -notcontains $id) { Fail "$id is in TAOM plus but not in BundledModules.ModuleIds." }
        Copy-Item -LiteralPath (Join-Path $BundledMods $id) -Destination (Join-Path $bundledTaom $id) -Recurse
    }
}

# The modules each program carries inside it (CoodszPayload items) come from these source files.
foreach ($file in @(
    'compat\CoodszJoinPassword\module\bin\Win64_Shipping_Client\CoodszMenuGuard.dll',
    'compat\CoodszJoinPassword\module\bin\Win64_Shipping_Client\CoodszSettingsSync.Coop.dll',
    'src\CoodszHost\ServerTools\DedicatedServer.CoodszTools\bin\Win64_Shipping_Server\DedicatedServer.CoodszTools.dll',
    'src\CoodszHost\ServerTools\DedicatedServer.CoodszTools\bin\Win64_Shipping_Server\CoodszSettingsSync.Coop.dll',
    'src\CoodszHost\PatchModules\DedicatedServer.IGCoopCompat\bin\Win64_Shipping_Server\DedicatedServer.IGCoopCompat.dll',
    'src\CoodszHost\ImageExporter\CoodszImageExporter\bin\Win64_Shipping_Client\CoodszImageExporter.dll',
    'src\CoodszHost\SaveRescue\DedicatedServer.CoopSaveRescue\bin\Win64_Shipping_Server\CoopSaveRescue.dll')) {
    if (-not (Test-Path -LiteralPath $file)) { Fail "$file is missing, and a program carries it inside its exe." }
}
if ($IncludeTaom) {
    # Coodsz War Sync rides in the TAOM server tool (src\TaomCoopHost\WarSync, built by compat\CoodszWarSync\build.ps1).
    foreach ($dll in @('CoodszWarSync.dll', 'CoodszSettingsSync.Coop.dll')) {
        if (-not (Test-Path -LiteralPath (Join-Path 'src\TaomCoopHost\WarSync' $dll))) { Fail "$dll is missing from the TAOM server tool's WarSync folder." }
    }
    # The client launcher carries the CompatOnly CLIENT zip and the server tool the HOST-OPERATOR zip (TaomCompatZip):
    # the exact kept release files from -AssetCache. Copy-KeptReleaseAsset stops here when one is missing or has
    # the wrong SHA256, and src\TaomCompatZip.targets checks the file again, so no exe ships without its zip.
    # On a kit bump (a new HOST-OPERATOR zip, TaomStack.KitVersion), also update ServerProgramRepair.CompatFiles:
    # the length and SHA256 of the kit's own TAOM.CoopCompat.dll and TAOM.CoopCompat.Bootstrap.dll (the hashes are in
    # its Artifacts\TAOM.CoopCompat\<version>\ARTIFACT.json). Start server puts those back when antivirus takes them
    # (CR-HZYX5), and Compat_pins_match_the_host_kit fails until they match the new kit.
    if ([string]::IsNullOrWhiteSpace($AssetCache)) { $AssetCache = Join-Path $root 'artifacts\release-assets-cache' }
    # dotnet gets a full path, so a relative -Output or -AssetCache still works (.NET resolves against the
    # process folder, not this script's location, hence the join with the repository root first).
    $taomCompat = Join-Path $work 'taom-compat'
    if (-not [System.IO.Path]::IsPathRooted($taomCompat)) { $taomCompat = Join-Path $root $taomCompat }
    $taomCompat = [System.IO.Path]::GetFullPath($taomCompat)
    New-Item -ItemType Directory -Force -Path $taomCompat | Out-Null
    $taomCompatZip = @{}
    foreach ($kit in @('CLIENT', 'HOST-OPERATOR')) {
        $asset = @(Get-ReleaseAssetSpec 'Coodsz/Coodsz-TAOM' $taomVersion | Where-Object { $_.Source -eq 'release' -and $_.Name -like "*-CompatOnly-$kit-*" })
        if ($asset.Count -ne 1) { Fail "release-assets.json needs exactly one TAOM-Coop CompatOnly $kit zip." }
        Copy-KeptReleaseAsset $asset[0] 'Coodsz/Coodsz-TAOM' $AssetCache $taomCompat
        $taomCompatZip[$kit] = Join-Path $taomCompat $asset[0].Name
    }
}

# dotnet builds into -ArtifactsPath when it is given.
$buildPath = if ($ArtifactsPath) { @('--artifacts-path', $ArtifactsPath) } else { @() }

# 3. Tests
if (-not $SkipTests) {
    Run 'dotnet' (@('test', 'tests\BannerlordCoopLauncher.Tests\BannerlordCoopLauncher.Tests.csproj', '-c', 'Release') + $buildPath)
}

# Sign the Coodsz module DLLs before the build packs them, so the payloads hold signed DLLs and the known hash list
# the Core build makes (Support\ModuleFingerprints) has their signed hashes. The release flow signs and commits them
# first (tools\Sign-ModuleDlls.ps1), so here every one is found signed. Every tool's DLLs are signed together, so a
# byte equal copy another tool carries never stays unsigned.
Invoke-ModuleDllSigning (Get-AllModuleDllPaths)

# 4. Publish
$single = @('-c', 'Release', '-r', 'win-x64', '--self-contained', 'true', '-p:PublishSingleFile=true', '-p:IncludeNativeLibrariesForSelfExtract=true', '-p:DebugType=None') + $buildPath
$publish = Join-Path $work 'publish'
$payloads = Join-Path $work 'payloads'
Run 'dotnet' (@('publish', 'src\CoodszCoopAndFriends\CoodszCoopAndFriends.csproj', '-o', "$publish\coop", "-p:CoodszBundledMods=$bundledCoop", "-p:CoodszPayloadCopyTo=$payloads\coop") + $single)
Run 'dotnet' (@('publish', 'src\CoodszHost\CoodszHost.csproj', '-o', "$publish\host", "-p:CoodszPayloadCopyTo=$payloads\host") + $single)
# Each payload inside each program must be this build's, file for file (tools\PayloadCheck.ps1).
$joinPassword = @('DedicatedServer.CoodszJoinPassword/', 'compat\CoodszJoinPassword\module')
Assert-Payload "$publish\coop\Coodsz Coop Client Launcher.exe" "$payloads\coop" 'ClientAddOns' @(, $joinPassword)
Assert-Payload "$publish\coop\Coodsz Coop Client Launcher.exe" "$payloads\coop" 'BundledMods' @(, @('', $bundledCoop))
foreach ($name in @('DedicatedServerRepair', 'SaveRescue', 'ServerTools', 'PatchModules', 'ImageExporter')) {
    $sources = @(, @('', "src\CoodszHost\$name"))
    if ($name -eq 'DedicatedServerRepair') { $sources += , @('', (Join-Path $privateRoot 'DedicatedServerRepair')) }
    Assert-Payload "$publish\host\Coodsz Coop Server Tool.exe" "$payloads\host" $name $sources
}
# Every module folder in the payloads carries its LICENSE.txt, and the module DLLs are signed (ReleaseAssets.ps1).
Assert-ModulePayloads 'Coodsz/Coodsz-Coop-and-Friends' @("$publish\coop\Coodsz Coop Client Launcher.exe", "$publish\host\Coodsz Coop Server Tool.exe")
if ($IncludeTaom) {
    # Self contained single files like the Coop and AD1259 tools, so players need no .NET install.
    Run 'dotnet' (@('publish', 'src\TaomCoopHost\TaomCoopHost.csproj', '-o', "$publish\taom-host", "-p:CoodszBundledMods=$bundledTaom", "-p:CoodszPayloadCopyTo=$payloads\taom-host", "-p:CoodszTaomCompatZip=$($taomCompatZip['HOST-OPERATOR'])") + $single)
    Run 'dotnet' (@('publish', 'src\TaomCoopLauncher\TaomCoopLauncher.csproj', '-o', "$publish\taom-client", "-p:CoodszBundledMods=$bundledTaom", "-p:CoodszPayloadCopyTo=$payloads\taom-client", "-p:CoodszTaomCompatZip=$($taomCompatZip['CLIENT'])") + $single)
    $taomHost = "$publish\taom-host\Coodsz TAOM Server Tool.exe"
    Assert-Payload $taomHost "$payloads\taom-host" 'SaveRescue' @(, @('', 'src\CoodszHost\SaveRescue\DedicatedServer.CoopSaveRescue\bin\Win64_Shipping_Server\CoopSaveRescue.dll'))
    Assert-Payload $taomHost "$payloads\taom-host" 'WarSync' @(, @('', 'src\TaomCoopHost\WarSync'))
    # Coodsz Server Tools for the server rules and the mod settings for players (TaomServerTools), the Coop Server Tool's build.
    Assert-Payload $taomHost "$payloads\taom-host" 'ServerTools' @(@('', 'src\CoodszHost\ServerTools\DedicatedServer.CoodszTools\bin\Win64_Shipping_Server\DedicatedServer.CoodszTools.dll'), @('', 'src\CoodszHost\ServerTools\DedicatedServer.CoodszTools\bin\Win64_Shipping_Server\CoodszSettingsSync.Coop.dll'))
    Assert-Payload $taomHost "$payloads\taom-host" 'BundledMods' @(, @('', $bundledTaom))
    $taomClient = "$publish\taom-client\Coodsz TAOM Client Launcher.exe"
    Assert-Payload $taomClient "$payloads\taom-client" 'ClientAddOns' @(, $joinPassword)
    Assert-Payload $taomClient "$payloads\taom-client" 'BundledMods' @(, @('', $bundledTaom))
    Assert-ModulePayloads 'Coodsz/Coodsz-TAOM' @($taomHost, $taomClient)
}

# 4b. Sign every shipped exe, after the payload checks and before any copy or zip. The legacy named copies
# are copies of these signed files, and Assert-StagedExesSigned verifies each one again before its zip.
if (-not $SkipSigning) {
    $toSign = @("$publish\coop\Coodsz Coop Client Launcher.exe", "$publish\host\Coodsz Coop Server Tool.exe")
    if ($IncludeTaom) { $toSign += @("$publish\taom-host\Coodsz TAOM Server Tool.exe", "$publish\taom-client\Coodsz TAOM Client Launcher.exe") }
    Invoke-ReleaseSigning $toSign
}

# 5. The two exe zip: one folder with the server tool and the client launcher, and nothing else. The
# modules, bundled mods, LICENSE.txt and THIRD-PARTY-NOTICES.md all ride inside the exes.
$coopTools = @('Coodsz Coop Server Tool.exe', 'Coodsz Coop Client Launcher.exe')
$toolsStage = Join-Path $work 'tools'
$toolsFolder = Join-Path $toolsStage 'Coodsz Coop Tools'
New-Item -ItemType Directory -Force -Path $toolsFolder | Out-Null
Copy-Item -LiteralPath "$publish\host\Coodsz Coop Server Tool.exe" -Destination $toolsFolder
Copy-Item -LiteralPath "$publish\coop\Coodsz Coop Client Launcher.exe" -Destination $toolsFolder
$toolsZip = Join-Path $Output "Coodsz-Coop-Tools-v$version.zip"
Assert-StagedExesSigned $toolsStage
New-ReleaseZip $toolsStage $toolsZip
Assert-ToolsZip $toolsZip 'Coodsz Coop Tools' $coopTools

# 6. TEMPORARY legacy zip, for the tools already installed under the old names: their updater picks the
# zip with Coop-and-Friends in its name and needs its own old exe name inside it. It holds the same new
# builds under the old names, one per folder like before, and each renames itself on its first start
# (ToolRename). Coodsz and Friends.exe is the client build too, since Coodsz and Friends became its More
# Nations switch. Each folder holds only its own exe, so each tool keeps its own folder.
$stage = Join-Path $work 'friends\Coodsz Launcher and Server'
$coop = Join-Path $stage 'Coodsz Coop and Friends'
$and = Join-Path $stage 'Coodsz and Friends'
$hostFolder = Join-Path $stage 'Coodsz Host'
New-Item -ItemType Directory -Force -Path $coop, $and, $hostFolder | Out-Null
Copy-Item -LiteralPath "$publish\coop\Coodsz Coop Client Launcher.exe" -Destination (Join-Path $coop 'Coodsz Coop and Friends.exe')
Copy-Item -LiteralPath "$publish\coop\Coodsz Coop Client Launcher.exe" -Destination (Join-Path $and 'Coodsz and Friends.exe')
Copy-Item -LiteralPath "$publish\host\Coodsz Coop Server Tool.exe" -Destination (Join-Path $hostFolder 'CoodszHost.exe')
foreach ($folder in @($coop, $and, $hostFolder)) {
    $exes = @(Get-ChildItem -LiteralPath $folder -File -Filter '*.exe')
    if ($exes.Count -ne 1) { Fail "$folder must hold its own exe only, not $($exes.Count) exes." }
}
# Licenses of code ported from other projects and the Coodsz Coop Tools License. The exes
# carry both; the legacy zip keeps the files too, beside the program folders as before.
if (-not (Test-Path -LiteralPath 'THIRD-PARTY-NOTICES.md')) { Fail 'THIRD-PARTY-NOTICES.md is missing from the repository root.' }
if (-not (Test-Path -LiteralPath 'LICENSE')) { Fail 'LICENSE is missing from the repository root.' }
Copy-Item -LiteralPath 'THIRD-PARTY-NOTICES.md' -Destination $stage
Copy-Item -LiteralPath 'LICENSE' -Destination (Join-Path $stage 'LICENSE.txt')

$friendsZip = Join-Path $Output "Coodsz-Coop-and-Friends-v$version.zip"
Assert-StagedExesSigned (Join-Path $work 'friends')
New-ReleaseZip (Join-Path $work 'friends') $friendsZip
Assert-LegacyZip $friendsZip @('Coodsz Launcher and Server/Coodsz Host/CoodszHost.exe', 'Coodsz Launcher and Server/Coodsz Coop and Friends/Coodsz Coop and Friends.exe', 'Coodsz Launcher and Server/Coodsz and Friends/Coodsz and Friends.exe')
$zips = @($toolsZip, $friendsZip)

# 7. TAOM zips, in their own release folder. The bundled TAOM plus mods ride inside both programs.
if ($IncludeTaom) {
    if ([string]::IsNullOrWhiteSpace($TaomOutput)) { $TaomOutput = Join-Path $root "artifacts\taom-release-$taomVersion" }
    elseif (-not [System.IO.Path]::IsPathRooted($TaomOutput)) { $TaomOutput = Join-Path $root $TaomOutput }
    if ([string]::IsNullOrWhiteSpace($AssetCache)) { $AssetCache = Join-Path $root 'artifacts\release-assets-cache' }
    # The TAOM folder is emptied below, so it must be a folder of its own.
    $full = { param($p) if (-not [System.IO.Path]::IsPathRooted($p)) { $p = Join-Path $root $p }; [System.IO.Path]::GetFullPath($p).TrimEnd('\') + '\' }
    $taomFull = & $full $TaomOutput
    foreach ($other in @($root, $Output, $AssetCache, (Join-Path $root 'artifacts'))) {
        # Same folder, or a folder above one of these: emptying it would delete them.
        if ((& $full $other).StartsWith($taomFull, [StringComparison]::OrdinalIgnoreCase)) { Fail "-TaomOutput $TaomOutput is or contains $other. Pick a folder of its own." }
    }
    if (Test-Path -LiteralPath $taomOutput) { Remove-Item -LiteralPath $taomOutput -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $taomOutput | Out-Null
    # The two exe zip: one folder, both programs, nothing else.
    $taomTools = @('Coodsz TAOM Server Tool.exe', 'Coodsz TAOM Client Launcher.exe')
    $taomStage = Join-Path $work 'taom-tools'
    $taomFolder = Join-Path $taomStage 'Coodsz TAOM Tools'
    New-Item -ItemType Directory -Force -Path $taomFolder | Out-Null
    Copy-Item -LiteralPath "$publish\taom-host\Coodsz TAOM Server Tool.exe" -Destination $taomFolder
    Copy-Item -LiteralPath "$publish\taom-client\Coodsz TAOM Client Launcher.exe" -Destination $taomFolder
    $taomZip = Join-Path $taomOutput "Coodsz-TAOM-Tools-v$taomVersion.zip"
    Assert-StagedExesSigned $taomStage
    New-ReleaseZip $taomStage $taomZip
    Assert-ToolsZip $taomZip 'Coodsz TAOM Tools' $taomTools
    $taomFiles = @($taomZip)

    # TEMPORARY legacy zips under today's names and layout (the program files at the zip root), for the
    # tools already installed: their updater picks Server-Tool or Client-Launcher and needs its own exe
    # name inside. The server tool's new build goes in under its old name and renames itself on start.
    # Players often unzip both TAOM tools into one folder, and that updater removes every top level exe
    # and dll its payload root does not carry, so each legacy zip carries both tools under their old
    # names (Add-SiblingPayload in ReleaseAssets.ps1). The old loose dlls may go, both exes are single files now.
    $taomLegacy = @(
        @("$publish\taom-host\Coodsz TAOM Server Tool.exe", 'Coodsz Dedicated TAOM Server Tool.exe', "Coodsz-Dedicated-TAOM-Server-Tool-v$taomVersion.zip"),
        @("$publish\taom-client\Coodsz TAOM Client Launcher.exe", 'Coodsz TAOM Client Launcher.exe', "Coodsz-TAOM-Client-Launcher-v$taomVersion.zip"))
    $taomLegacyExes = @($taomLegacy | ForEach-Object { $_[1] })
    foreach ($legacy in $taomLegacy) {
        $legacyStage = Join-Path $work ('legacy-' + [System.IO.Path]::GetFileNameWithoutExtension($legacy[2]))
        New-Item -ItemType Directory -Force -Path $legacyStage | Out-Null
        foreach ($tool in $taomLegacy) { Copy-Item -LiteralPath $tool[0] -Destination (Join-Path $legacyStage $tool[1]) }
        Copy-Item -LiteralPath 'THIRD-PARTY-NOTICES.md' -Destination $legacyStage
        Copy-Item -LiteralPath 'LICENSE' -Destination (Join-Path $legacyStage 'LICENSE.txt')
        $zip = Join-Path $taomOutput $legacy[2]
        Assert-StagedExesSigned $legacyStage
        New-ReleaseZip $legacyStage $zip
        Assert-LegacyZip $zip @($legacy[1])
        Assert-PayloadRoots $zip @(@{ Exe = $legacy[1]; Root = ''; Holds = $taomLegacyExes })
        $taomFiles += $zip
    }

    # Files the TAOM release carries that are not built here (tools\release-assets.json).
    foreach ($asset in (Get-ReleaseAssetSpec 'Coodsz/Coodsz-TAOM' $taomVersion)) {
        if ($asset.Source -eq 'release') {
            Copy-KeptReleaseAsset $asset 'Coodsz/Coodsz-TAOM' $AssetCache $taomOutput
        }
        elseif ($asset.Source -eq 'repo') {
            if (-not (Test-Path -LiteralPath $asset.Path)) { Fail "$($asset.Path) is missing from the repository, and the TAOM release needs it as $($asset.Name)." }
            Copy-Item -LiteralPath $asset.Path -Destination (Join-Path $taomOutput $asset.Name) -Force
        }
        else { continue }
        $taomFiles += (Join-Path $taomOutput $asset.Name)
    }
}

# 8. Checksums, one file per release, then the check against tools\release-assets.json
function Write-Sums([string[]] $files, [string] $folder) {
    $sums = foreach ($file in $files) { (Get-Sha256 $file) + '  ' + (Split-Path -Leaf $file) }
    # LF line ends, like sha256sum writes, so sha256sum -c reads the file on any system.
    [System.IO.File]::WriteAllText((Join-Path $folder 'SHA256SUMS.txt'), (($sums -join "`n") + "`n"), [System.Text.Encoding]::ASCII)
}
Write-Sums $zips $Output
Assert-ReleaseFolder 'Coodsz/Coodsz-Coop-and-Friends' $version $Output
if ($IncludeTaom) {
    Write-Sums $taomFiles $taomOutput
    Assert-ReleaseFolder 'Coodsz/Coodsz-TAOM' $taomVersion $taomOutput
}
Remove-Item -LiteralPath $work -Recurse -Force

# 9. The release page: release-body.md starts with the download line, release-labels.json names
# the label each legacy zip gets, so new players take the Tools zip.
Write-ReleasePageFiles 'Coodsz/Coodsz-Coop-and-Friends' $version $Output -Unsigned:$SkipSigning
if ($IncludeTaom) { Write-ReleasePageFiles 'Coodsz/Coodsz-TAOM' $taomVersion $taomOutput -Unsigned:$SkipSigning }

Write-Host ''
Write-Host "Done. Upload these to the v$version GitHub release:" -ForegroundColor Green
foreach ($zip in $zips) { Write-Host ('  ' + $zip + '  (' + [math]::Round((Get-Item -LiteralPath $zip).Length / 1MB, 1) + ' MB)') }
Write-Host ('  ' + (Join-Path $Output 'SHA256SUMS.txt'))
Write-PublishCommands 'Coodsz/Coodsz-Coop-and-Friends' $version $Output ($zips + (Join-Path $Output 'SHA256SUMS.txt')) -Unsigned:$SkipSigning
if ($IncludeTaom) {
    Write-Host ''
    Write-Host "Upload EVERY file in $taomOutput to the v$taomVersion Coodsz-TAOM release, not this one:" -ForegroundColor Yellow
    foreach ($file in ($taomFiles + (Join-Path $taomOutput 'SHA256SUMS.txt'))) { Write-Host ('  ' + (Split-Path -Leaf $file) + '  (' + [math]::Round((Get-Item -LiteralPath $file).Length / 1MB, 1) + ' MB)') }
    Write-PublishCommands 'Coodsz/Coodsz-TAOM' $taomVersion $taomOutput ($taomFiles + (Join-Path $taomOutput 'SHA256SUMS.txt')) -Unsigned:$SkipSigning
}
Write-ModuleDllCommitReminder
Write-Host ''
Write-Host 'A release only counts as done when Check-ReleaseAssets passes on the published release, after the labels are set.' -ForegroundColor Green
}
catch {
    Write-Host $_.Exception.Message -ForegroundColor Red
    $exitCode = 1
}
finally {
    Stop-Transcript | Out-Null
}
exit $exitCode
