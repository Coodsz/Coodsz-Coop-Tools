<#
.SYNOPSIS
Builds the release files for the Coodsz Burning Empires tools.

.DESCRIPTION
Run from the repository root in PowerShell 7 (pwsh) or Windows PowerShell:

    .\tools\Make-BurningEmpires-Release.ps1

It reads the release number from ReleaseNotice.BeVersion, checks that both
Burning Empires project versions match, checks the bundled UIExtenderEx 2.13.3
DLL, runs the tests, publishes both programs as single self-contained files,
refuses a build that holds a GitHub token (github_pat_, ghp_, gho_, ghu_, ghs_ or
ghr_) or a Discord webhook link, and writes these
files to artifacts\be-release-<version>:

    Coodsz-Burning-Empires-Tools-v<version>.zip   (Coodsz Burning Empires Tools folder: the server
                                                   tool and the client launcher, nothing else)
    LICENSE.txt
    README.-.Instructions.txt                      (Coodsz-BurningEmpires-Instructions.txt)
    SHA256SUMS.txt

Burning Empires was never released before the two exe release, so there are no
legacy zips. Updated tools pick the zip that starts with
Coodsz-Burning-Empires-Tools-v (ToolNames).

The folder also gets release-body.md (the downloadLine of tools\release-assets.json
first, then docs\release-notes\v<version>-burningempires.md without its leading
comment) and release-labels.json (empty, since no zip needs a label). The script
prints the commands that publish with that body, set the labels
(tools\Set-ReleaseLabels.ps1) and run tools\Check-ReleaseAssets.ps1.

No mod is packed: players get Burning Empires from Nexus Mods and the BUTR
modules and Coop from the Steam Workshop. The server exe carries Burning Empires
Coop Compat and the official UIExtenderEx 2.13.3 DLL with its LGPL v3 and GPL v3
texts as its ServerAddOns payload, and the client exe carries Burning Empires
Coop Compat and Coodsz Join Password as its ClientAddOns payload. Each exe writes
its modules out to %LOCALAPPDATA%\Coodsz\BurningEmpires on first use
(src\CoodszPayload.targets), so nothing ships beside the exes.

Everything printed is also written to artifacts\make-be-release-last.log.
#>
[CmdletBinding()]
param(
    # Where the release files go. Default: artifacts\be-release-<version>
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
Start-Transcript -LiteralPath (Join-Path $logFolder 'make-be-release-last.log') -Force | Out-Null

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

# The files of a source folder as Assert-Payload pairs, each under $prefix plus its own subfolder,
# leaving out the names in $leaveOut (paths inside the folder, the same ones the csproj excludes).
function Get-PayloadPairs([string] $folder, [string] $prefix, [string[]] $leaveOut = @()) {
    $full = (Resolve-Path -LiteralPath $folder).Path.TrimEnd('\') + '\'
    $pairs = @()
    foreach ($file in Get-ChildItem -LiteralPath $folder -Recurse -File -Force) {
        $relative = $file.FullName.Substring($full.Length)
        if ($leaveOut -contains $relative) { continue }
        $inside = Split-Path -Parent $relative
        $at = if ($inside) { $prefix + $inside.Replace('\', '/') + '/' } else { $prefix }
        $pairs += , @($at, $file.FullName)
    }
    return , $pairs
}

# 1. Version: ReleaseNotice.BeVersion and both csproj files
$notice = Get-Content -Raw -LiteralPath 'src\BannerlordCoopLauncher.Core\Updates\ReleaseNotice.cs'
$versionMatch = [regex]::Match($notice, 'BeVersion\s*=\s*"v(?<v>[0-9.]+)"')
if (-not $versionMatch.Success) { Fail 'Could not read BeVersion from ReleaseNotice.cs.' }
$version = $versionMatch.Groups['v'].Value
foreach ($project in @('src\BurningEmpiresHost\BurningEmpiresHost.csproj', 'src\BurningEmpiresLauncher\BurningEmpiresLauncher.csproj')) {
    $projectVersion = [regex]::Match((Get-Content -Raw -LiteralPath $project), '<Version>(?<v>[^<]+)</Version>').Groups['v'].Value
    if ($projectVersion -ne $version) {
        Fail ("ReleaseNotice.BeVersion is v$version but $project says $projectVersion. Make them the same first.")
    }
}
Write-Host "Burning Empires tools v$version" -ForegroundColor Green
# The release body is made from these notes at the end, so stop now when they are not written yet.
Get-ReleaseNotesFile 'Coodsz/Coodsz-BurningEmpires' $version | Out-Null

# 2. UIExtenderEx 2.13.3: the build only prints a note when it is missing, so the release refuses here.
# The size and SHA256 come from BeStack, the same values the server tool checks on every start.
$stack = Get-Content -Raw -LiteralPath 'src\BurningEmpires.Core\BeStack.cs'
$uiSha = [regex]::Match($stack, 'ServerUiExtenderExSha256\s*=\s*"(?<h>[0-9A-Fa-f]{64})"').Groups['h'].Value
$uiBytes = [regex]::Match($stack, 'ServerUiExtenderExBytes\s*=\s*(?<n>[0-9]+)').Groups['n'].Value
if (-not $uiSha -or -not $uiBytes) { Fail 'Could not read the UIExtenderEx 2.13.3 SHA256 and size from BeStack.cs.' }
$uiFolder = 'compat\BurningEmpiresCoopCompat\server-extra\UIExtenderEx-2.13.3'
$uiDll = Join-Path $uiFolder 'Bannerlord.UIExtenderEx.dll'
if (-not (Test-Path -LiteralPath $uiDll)) {
    Fail "UIExtenderEx 2.13.3 is missing: $uiDll. The Burning Empires server does not start without it. See the README.txt in that folder."
}
if ((Get-Item -LiteralPath $uiDll).Length -ne [long] $uiBytes -or (Get-Sha256 $uiDll) -ne $uiSha.ToLowerInvariant()) {
    Fail "$uiDll is not the official UIExtenderEx 2.13.3 ($uiBytes bytes, SHA256 $uiSha). See the README.txt in that folder."
}
# The LGPL v3 asks for the license texts and a way to the source next to the DLL; the payload carries them.
foreach ($text in @('LICENSE-UIExtenderEx.txt', 'GPL-3.0.txt', 'SOURCE.txt')) {
    if (-not (Test-Path -LiteralPath (Join-Path $uiFolder $text))) { Fail "$text is missing from $uiFolder. UIExtenderEx ships with its license texts and source link." }
}

# Code signing (Microsoft Artifact Signing): check az login and restore the pinned tools now, so a missing
# login stops the release before the long build. -SkipSigning is for test builds only.
if ($SkipSigning) { Write-UnsignedWarning } else { Initialize-Signing }

if ([string]::IsNullOrWhiteSpace($Output)) { $Output = Join-Path $root "artifacts\be-release-$version" }
$work = Join-Path $Output 'work'
if (Test-Path -LiteralPath $work) { Remove-Item -LiteralPath $work -Recurse -Force }
New-Item -ItemType Directory -Force -Path $work | Out-Null

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
Run 'dotnet' (@('publish', 'src\BurningEmpiresHost\BurningEmpiresHost.csproj', '-o', "$publish\server", "-p:CoodszPayloadCopyTo=$payloads\server") + $single)
Run 'dotnet' (@('publish', 'src\BurningEmpiresLauncher\BurningEmpiresLauncher.csproj', '-o', "$publish\client", "-p:CoodszPayloadCopyTo=$payloads\client") + $single)

# 5. Each payload inside each exe must be this build's, file for file (tools\PayloadCheck.ps1).
$repair = 'src\CoodszHost\DedicatedServerRepair'
# Coodsz Host's own start script, laid over the server folder on Prepare and Start with the launcher
# submodules from the private folder (Assert-PrivateFiles checked those).
if (-not (Test-Path -LiteralPath "$repair\Start-ModdedServer.ps1")) { Fail "DedicatedServerRepair\Start-ModdedServer.ps1 is missing from src\CoodszHost." }
$rescueDll = 'src\CoodszHost\SaveRescue\DedicatedServer.CoopSaveRescue\bin\Win64_Shipping_Server\CoopSaveRescue.dll'
if (-not (Test-Path -LiteralPath $rescueDll)) { Fail 'SaveRescue (Coop Save Rescue) is missing its DLL.' }
$compatModule = 'compat\BurningEmpiresCoopCompat\module'
$compatVersion = [regex]::Match($stack, 'CompatVersion\s*=\s*"(?<v>[^"]+)"').Groups['v'].Value
foreach ($file in @('SubModule.xml', 'bin\Win64_Shipping_Server\BurningEmpires.CoopCompat.dll', 'bin\Win64_Shipping_Client\BurningEmpires.CoopCompat.dll')) {
    if (-not (Test-Path -LiteralPath (Join-Path $compatModule $file))) { Fail "Burning Empires Coop Compat is missing $file." }
}
$moduleVersion = [regex]::Match((Get-Content -Raw -LiteralPath (Join-Path $compatModule 'SubModule.xml')), '<Version\s+value="(?<v>[^"]+)"').Groups['v'].Value
if ($moduleVersion -ne $compatVersion) { Fail "Burning Empires Coop Compat is $moduleVersion, but BeStack.CompatVersion is $compatVersion. Run compat\BurningEmpiresCoopCompat\Build.cmd and commit module\." }
$joinPassword = 'compat\CoodszJoinPassword\module\bin\Win64_Shipping_Client'
foreach ($dll in @('CoodszJoinPassword.dll', 'CoodszMenuGuard.dll', 'CoodszSettingsSync.Coop.dll')) {
    if (-not (Test-Path -LiteralPath (Join-Path $joinPassword $dll))) { Fail "Coodsz Join Password lacks $dll." }
}

$server = "$publish\server\Coodsz Burning Empires Server Tool.exe"
$client = "$publish\client\Coodsz Burning Empires Client Launcher.exe"
# The csproj leaves Coodsz Host's module policy and update installer out, and the compat module's README.
Assert-Payload $server "$payloads\server" 'DedicatedServerRepair' ((Get-PayloadPairs $repair '' @('server-module-policy.json', 'Install-Update.ps1', 'INSTALL-UPDATE.bat')) + (Get-PayloadPairs (Join-Path $privateRoot 'DedicatedServerRepair') ''))
Assert-Payload $server "$payloads\server" 'SaveRescue' @(, @('', $rescueDll))
Assert-Payload $server "$payloads\server" 'ServerAddOns' ((Get-PayloadPairs $compatModule 'BurningEmpires.CoopCompat/' @('README.txt')) + (Get-PayloadPairs $uiFolder 'UIExtenderEx-2.13.3/'))
Assert-Payload $client "$payloads\client" 'ClientAddOns' ((Get-PayloadPairs 'compat\CoodszJoinPassword\module' 'DedicatedServer.CoodszJoinPassword/') + (Get-PayloadPairs $compatModule 'BurningEmpires.CoopCompat/' @('README.txt')))
# Every Coodsz module folder in the payloads carries its LICENSE.txt, and the module DLLs are signed (ReleaseAssets.ps1).
Assert-ModulePayloads 'Coodsz/Coodsz-BurningEmpires' @($server, $client)

# No GitHub token may ship. A token in C# code sits in the exe as UTF-16, so the zero bytes are
# dropped before the search and both encodings are caught. The payloads are zipped inside the exe,
# so each file of each payload zip is searched too. Only the file name is printed.
$latin1 = [System.Text.Encoding]::GetEncoding(28591)
$searched = @($server, $client) + @(Get-ChildItem -LiteralPath $payloads -Recurse -File | ForEach-Object { $_.FullName })
foreach ($file in $searched) {
    $texts = @()
    if ($file.EndsWith('.zip', [StringComparison]::OrdinalIgnoreCase)) {
        $zip = [System.IO.Compression.ZipFile]::OpenRead($file)
        try {
            foreach ($entry in $zip.Entries) {
                if ($entry.Name.Length -eq 0) { continue }
                $stream = $entry.Open(); $memory = New-Object System.IO.MemoryStream
                try { $stream.CopyTo($memory); $texts += , @("$file ($($entry.FullName))", $latin1.GetString($memory.ToArray())) }
                finally { $stream.Dispose(); $memory.Dispose() }
            }
        }
        finally { $zip.Dispose() }
    }
    else { $texts += , @($file, $latin1.GetString([System.IO.File]::ReadAllBytes($file))) }
    foreach ($pair in $texts) {
        $text = $pair[1].Replace([string][char]0, '')
        # GitHub tokens (github_pat_, ghp_, gho_, ghu_, ghs_, ghr_) by their shape, as reply-text.js finds them, so a
        # bare prefix in compressed bytes is no false alarm; and Discord webhook links.
        $found = [regex]::Match($text, 'github_pat_[A-Za-z0-9_]{20,}|gh[pousr]_[A-Za-z0-9]{20,}|(?i:discord(?:app)?\.com/api/webhooks/)')
        if ($found.Success) { Fail "$($pair[0]) holds something that looks like a GitHub token or Discord webhook ($([regex]::Match($found.Value, '^(github_pat_|gh._|.*webhooks/)').Value)). Remove it before a release." }
    }
}
Write-Host 'No GitHub token or Discord webhook in either exe or its payloads.' -ForegroundColor Green

# 5b. Sign both exes, after the payload and token checks and before any copy or zip.
if (-not $SkipSigning) { Invoke-ReleaseSigning @($server, $client) }

# 6. The two exe zip: one folder with both programs and nothing else. LICENSE.txt and
# THIRD-PARTY-NOTICES.md ride inside the exes, and LICENSE.txt is its own release file too.
if (-not (Test-Path -LiteralPath 'THIRD-PARTY-NOTICES.md')) { Fail 'THIRD-PARTY-NOTICES.md is missing from the repository root.' }
$beTools = @('Coodsz Burning Empires Server Tool.exe', 'Coodsz Burning Empires Client Launcher.exe')
$toolsStage = Join-Path $work 'tools'
$toolsFolder = Join-Path $toolsStage 'Coodsz Burning Empires Tools'
New-Item -ItemType Directory -Force -Path $toolsFolder | Out-Null
Copy-Item -LiteralPath $server -Destination $toolsFolder
Copy-Item -LiteralPath $client -Destination $toolsFolder
$toolsZip = Join-Path $Output "Coodsz-Burning-Empires-Tools-v$version.zip"
Assert-StagedExesSigned $toolsStage
New-ReleaseZip $toolsStage $toolsZip
Assert-ToolsZip $toolsZip 'Coodsz Burning Empires Tools' $beTools
$files = @($toolsZip)

# The files the guide sends players to that come from the repository (LICENSE.txt and the instructions).
foreach ($asset in (Get-ReleaseAssetSpec 'Coodsz/Coodsz-BurningEmpires' $version)) {
    if ($asset.Source -ne 'repo') { continue }
    if (-not (Test-Path -LiteralPath $asset.Path)) { Fail "$($asset.Path) is missing from the repository, and the release needs it as $($asset.Name)." }
    Copy-Item -LiteralPath $asset.Path -Destination (Join-Path $Output $asset.Name) -Force
    $files += (Join-Path $Output $asset.Name)
}

# 7. Checksums, then the check against tools\release-assets.json
$sums = foreach ($file in $files) { (Get-Sha256 $file) + '  ' + (Split-Path -Leaf $file) }
# LF line ends, like sha256sum writes, so sha256sum -c reads the file on any system.
[System.IO.File]::WriteAllText((Join-Path $Output 'SHA256SUMS.txt'), (($sums -join "`n") + "`n"), [System.Text.Encoding]::ASCII)
Assert-ReleaseFolder 'Coodsz/Coodsz-BurningEmpires' $version $Output
Remove-Item -LiteralPath $work -Recurse -Force

# 8. The release page: release-body.md starts with the download line. release-labels.json is
# empty, since Burning Empires has no legacy zips.
Write-ReleasePageFiles 'Coodsz/Coodsz-BurningEmpires' $version $Output -Unsigned:$SkipSigning

Write-Host ''
Write-Host "Done. Upload these to the v$version release on Coodsz/Coodsz-BurningEmpires:" -ForegroundColor Green
foreach ($file in ($files + (Join-Path $Output 'SHA256SUMS.txt'))) { Write-Host ('  ' + (Split-Path -Leaf $file) + '  (' + [math]::Round((Get-Item -LiteralPath $file).Length / 1MB, 1) + ' MB)') }
Write-PublishCommands 'Coodsz/Coodsz-BurningEmpires' $version $Output ($files + (Join-Path $Output 'SHA256SUMS.txt')) -Unsigned:$SkipSigning
Write-ModuleDllCommitReminder
Write-Host ''
Write-Host 'A release only counts as done when Check-ReleaseAssets passes on the published release.' -ForegroundColor Green
}
catch {
    Write-Host $_.Exception.Message -ForegroundColor Red
    $exitCode = 1
}
finally {
    Stop-Transcript | Out-Null
}
exit $exitCode
