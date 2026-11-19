<#
.SYNOPSIS
Builds the release zips for the Coodsz AD1259 tools.

.DESCRIPTION
Run from the repository root in PowerShell 7 (pwsh) or Windows PowerShell:

    .\tools\Make-AD1259-Release.ps1

It reads the release number from ReleaseNotice.AD1259Version, checks that the
AD1259Host project version matches, runs the tests, publishes both programs
as single self-contained files, and writes these files to
artifacts\ad1259-release-<version>:

    Coodsz-AD1259-Tools-v<version>.zip            (Coodsz AD1259 Tools folder: the server
                                                   tool and the client launcher, nothing else)
    Coodsz-AD1259-Server-v<version>.zip           (TEMPORARY legacy zip, Coodsz AD1259 Server folder)
    Coodsz-AD1259-Client-Launcher-v<version>.zip  (TEMPORARY legacy zip, Coodsz AD1259 Client Launcher folder)
    SHA256SUMS.txt

The two exe zip is the one players download. The legacy zips keep today's names
and layout with the new builds under the old exe names, because the updater of a
tool installed before the two exe release picks "AD1259-Server" or
"AD1259-Client-Launcher" and needs its own exe name inside. The server tool's new
build renames itself on its first start (ToolRename). Drop the legacy zips once
the old versions are gone.

The folder also gets release-body.md (the downloadLine of tools\release-assets.json
first, then docs\release-notes\v<version>-ad1259.md without its leading comment) and
release-labels.json (the label of each legacy zip). The script prints the commands
that publish with that body, set the labels (tools\Set-ReleaseLabels.ps1) and run
tools\Check-ReleaseAssets.ps1.

No mod is packed: players and hosts get every mod from the Steam Workshop or
Nexus Mods. The server exe carries SkyApolo's AD1259 - Coop Compatibility module
(bundled with permission) as its ServerAddOns payload and Coodsz Server Tools (host
rules and MCM settings sync) as its ServerTools payload, and each exe writes its
modules out to %LOCALAPPDATA%\Coodsz\AD1259 on first use
(src\CoodszPayload.targets), so nothing ships beside the exes.

Everything printed is also written to artifacts\make-ad1259-release-last.log.
#>
[CmdletBinding()]
param(
    # Where the zips go. Default: artifacts\ad1259-release-<version>
    [string] $Output = "",

    # Skip dotnet test (only for a quick local rebuild).
    [switch] $SkipTests,

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

$logFolder = if ($ArtifactsPath) { $ArtifactsPath } else { Join-Path $root 'artifacts' }
New-Item -ItemType Directory -Force -Path $logFolder | Out-Null
Start-Transcript -LiteralPath (Join-Path $logFolder 'make-ad1259-release-last.log') -Force | Out-Null

function Fail([string] $message) {
    throw $message
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
$versionMatch = [regex]::Match($notice, 'AD1259Version\s*=\s*"v(?<v>[0-9.]+)"')
if (-not $versionMatch.Success) { Fail 'Could not read AD1259Version from ReleaseNotice.cs.' }
$version = $versionMatch.Groups['v'].Value
$hostProject = Get-Content -Raw -LiteralPath 'src\AD1259Host\AD1259Host.csproj'
$hostVersion = [regex]::Match($hostProject, '<Version>(?<v>[^<]+)</Version>').Groups['v'].Value
if ($hostVersion -ne $version) {
    Fail ("ReleaseNotice.AD1259Version is v$version but AD1259Host.csproj says $hostVersion. Make them the same first.")
}
Write-Host "AD1259 tools v$version" -ForegroundColor Green
# The release body is made from these notes at the end, so stop now when they are not written yet.
Get-ReleaseNotesFile 'Coodsz/Coodsz-AD1259' $version | Out-Null

# Code signing (Microsoft Artifact Signing): check az login and restore the pinned tools now, so a missing
# login stops the release before the long build. -SkipSigning is for test builds only.
if ($SkipSigning) { Write-UnsignedWarning } else { Initialize-Signing }

if ([string]::IsNullOrWhiteSpace($Output)) { $Output = Join-Path $root "artifacts\ad1259-release-$version" }
$work = Join-Path $Output 'work'
if (Test-Path -LiteralPath $work) { Remove-Item -LiteralPath $work -Recurse -Force }
New-Item -ItemType Directory -Force -Path $work | Out-Null

# dotnet builds into -ArtifactsPath when it is given.
$buildPath = if ($ArtifactsPath) { @('--artifacts-path', $ArtifactsPath) } else { @() }

# 2. Tests
if (-not $SkipTests) {
    Run 'dotnet' (@('test', 'tests\BannerlordCoopLauncher.Tests\BannerlordCoopLauncher.Tests.csproj', '-c', 'Release') + $buildPath)
}

# Sign the Coodsz module DLLs before the build packs them, so the payloads hold signed DLLs and the known hash list
# the Core build makes (Support\ModuleFingerprints) has their signed hashes. The release flow signs and commits them
# first (tools\Sign-ModuleDlls.ps1), so here every one is found signed. Every tool's DLLs are signed together, so a
# byte equal copy another tool carries never stays unsigned.
Invoke-ModuleDllSigning (Get-AllModuleDllPaths)

# 3. Publish
$single = @('-c', 'Release', '-r', 'win-x64', '--self-contained', 'true', '-p:PublishSingleFile=true', '-p:IncludeNativeLibrariesForSelfExtract=true', '-p:DebugType=None') + $buildPath
$publish = Join-Path $work 'publish'
$payloads = Join-Path $work 'payloads'
Run 'dotnet' (@('publish', 'src\AD1259Host\AD1259Host.csproj', '-o', "$publish\server", "-p:CoodszPayloadCopyTo=$payloads\server") + $single)
Run 'dotnet' (@('publish', 'src\AD1259Launcher\AD1259Launcher.csproj', '-o', "$publish\client", "-p:CoodszPayloadCopyTo=$payloads\client") + $single)

# 4. Each payload inside each exe must be this build's, file for file (tools\PayloadCheck.ps1).
foreach ($file in @('SubModule.xml', 'bin\Win64_Shipping_Server\AD1259CoopCompat.dll')) {
    if (-not (Test-Path -LiteralPath (Join-Path 'compat\AD1259CoopCompat\module' $file))) { Fail "The compatibility module is missing $file." }
}
if (-not (Test-Path -LiteralPath 'compat\AD1259CoopClientFix\module\bin\Win64_Shipping_Client\AD1259CoopClientFix.dll')) {
    Fail 'The client stability module is missing its DLL.'
}
$server = "$publish\server\Coodsz AD1259 Server Tool.exe"
$client = "$publish\client\Coodsz AD1259 Client Launcher.exe"
Assert-Payload $server "$payloads\server" 'DedicatedServerRepair' @(@('', 'src\CoodszHost\DedicatedServerRepair'), @('', (Join-Path $privateRoot 'DedicatedServerRepair')))
Assert-Payload $server "$payloads\server" 'SaveRescue' @(, @('', 'src\CoodszHost\SaveRescue'))
Assert-Payload $server "$payloads\server" 'ImageExporter' @(, @('', 'src\CoodszHost\ImageExporter'))
# Coodsz Server Tools: the host rules and the MCM settings sync for players, the same module the Coop Server Tool ships.
Assert-Payload $server "$payloads\server" 'ServerTools' @(, @('', 'src\CoodszHost\ServerTools'))
Assert-Payload $server "$payloads\server" 'ServerAddOns' @(, @('DedicatedServer.AD1259CoopCompat/', 'compat\AD1259CoopCompat\module'))
Assert-Payload $client "$payloads\client" 'ClientAddOns' @(
    @('DedicatedServer.AD1259CoopClientFix/', 'compat\AD1259CoopClientFix\module'),
    @('DedicatedServer.CoodszJoinPassword/', 'compat\CoodszJoinPassword\module'))
# Every Coodsz module folder in the payloads carries its LICENSE.txt, and the module DLLs are signed (ReleaseAssets.ps1).
Assert-ModulePayloads 'Coodsz/Coodsz-AD1259' @($server, $client)

# 4b. Sign both exes, after the payload checks and before any copy or zip. The legacy named copies are
# copies of these signed files, and Assert-StagedExesSigned verifies each one again before its zip.
if (-not $SkipSigning) { Invoke-ReleaseSigning @($server, $client) }

# 5. The two exe zip: one folder with both programs and nothing else. LICENSE.txt and
# THIRD-PARTY-NOTICES.md ride inside the exes.
$adTools = @('Coodsz AD1259 Server Tool.exe', 'Coodsz AD1259 Client Launcher.exe')
$toolsStage = Join-Path $work 'tools'
$toolsFolder = Join-Path $toolsStage 'Coodsz AD1259 Tools'
New-Item -ItemType Directory -Force -Path $toolsFolder | Out-Null
Copy-Item -LiteralPath $server -Destination $toolsFolder
Copy-Item -LiteralPath $client -Destination $toolsFolder
$toolsZip = Join-Path $Output "Coodsz-AD1259-Tools-v$version.zip"
Assert-StagedExesSigned $toolsStage
New-ReleaseZip $toolsStage $toolsZip
Assert-ToolsZip $toolsZip 'Coodsz AD1259 Tools' $adTools
$zips = @($toolsZip)

# 6. TEMPORARY legacy zips: today's names and layout, one folder per program with the licenses beside the
# exe, and the new builds under the old exe names.
if (-not (Test-Path -LiteralPath 'THIRD-PARTY-NOTICES.md')) { Fail 'THIRD-PARTY-NOTICES.md is missing from the repository root.' }
if (-not (Test-Path -LiteralPath 'LICENSE')) { Fail 'LICENSE is missing from the repository root.' }
foreach ($legacy in @(
    @($server, 'Coodsz AD1259 Server', 'Coodsz AD1259 Server.exe', "Coodsz-AD1259-Server-v$version.zip"),
    @($client, 'Coodsz AD1259 Client Launcher', 'Coodsz AD1259 Client Launcher.exe', "Coodsz-AD1259-Client-Launcher-v$version.zip"))) {
    $legacyStage = Join-Path $work ('legacy-' + [System.IO.Path]::GetFileNameWithoutExtension($legacy[3]))
    $legacyFolder = Join-Path $legacyStage $legacy[1]
    New-Item -ItemType Directory -Force -Path $legacyFolder | Out-Null
    Copy-Item -LiteralPath $legacy[0] -Destination (Join-Path $legacyFolder $legacy[2])
    Copy-Item -LiteralPath 'THIRD-PARTY-NOTICES.md' -Destination $legacyFolder
    Copy-Item -LiteralPath 'LICENSE' -Destination (Join-Path $legacyFolder 'LICENSE.txt')
    $zip = Join-Path $Output $legacy[3]
    Assert-StagedExesSigned $legacyStage
    New-ReleaseZip $legacyStage $zip
    Assert-LegacyZip $zip @($legacy[1] + '/' + $legacy[2])
    $zips += $zip
}

# 7. Checksums
$sums = foreach ($zip in $zips) {
    (Get-FileHash -Algorithm SHA256 -LiteralPath $zip).Hash.ToLowerInvariant() + '  ' + (Split-Path -Leaf $zip)
}
# LF line ends, like sha256sum writes, so sha256sum -c reads the file on any system.
[System.IO.File]::WriteAllText((Join-Path $Output 'SHA256SUMS.txt'), (($sums -join "`n") + "`n"), [System.Text.Encoding]::ASCII)
# Every file the guides send players to (tools\release-assets.json).
Assert-ReleaseFolder 'Coodsz/Coodsz-AD1259' $version $Output
Remove-Item -LiteralPath $work -Recurse -Force

# 8. The release page: release-body.md starts with the download line, release-labels.json names
# the label each legacy zip gets, so new players take the Tools zip.
Write-ReleasePageFiles 'Coodsz/Coodsz-AD1259' $version $Output -Unsigned:$SkipSigning

Write-Host ''
Write-Host "Done. Upload these to the v$version release on Coodsz/Coodsz-AD1259:" -ForegroundColor Green
foreach ($zip in $zips) { Write-Host ('  ' + $zip + '  (' + [math]::Round((Get-Item -LiteralPath $zip).Length / 1MB, 1) + ' MB)') }
Write-Host ('  ' + (Join-Path $Output 'SHA256SUMS.txt'))
Write-PublishCommands 'Coodsz/Coodsz-AD1259' $version $Output ($zips + (Join-Path $Output 'SHA256SUMS.txt')) -Unsigned:$SkipSigning
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
