<#
.SYNOPSIS
Builds the release files for the Coodsz Realm of Thrones tools.

.DESCRIPTION
Run from the repository root in PowerShell 7 (pwsh) or Windows PowerShell:

    .\tools\Make-RealmOfThrones-Release.ps1

It reads the release number from ReleaseNotice.RotVersion and the release repo from
ReleaseNotice.RotRepo (src\BannerlordCoopLauncher.Core\Updates\ReleaseNotice.cs, which RotRelease
reads), checks that both Realm of Thrones project versions match, checks the compat module and
the RoT build of Coop the exes carry, runs the tests, publishes both programs as single self-contained files, refuses a build that holds a GitHub token (github_pat_, ghp_,
gho_, ghu_, ghs_ or ghr_) or a Discord webhook link, and writes these files to artifacts\rot-release-<version>:

    Coodsz-Realm-of-Thrones-Tools-v<version>.zip  (Coodsz Realm of Thrones Tools folder: the server
                                                   tool and the client launcher, nothing else)
    LICENSE.txt
    README.-.Instructions.txt                      (Coodsz-RealmOfThrones-Instructions.txt)
    SHA256SUMS.txt

Realm of Thrones was never released before, so there are no legacy zips. Updated tools pick the zip
that starts with Coodsz-Realm-of-Thrones-Tools-v (ToolNames).

The folder also gets release-body.md (the downloadLine of tools\release-assets.json first, then
docs\release-notes\v<version>-realmofthrones.md without its leading comment) and release-labels.json
(empty, since every release file is one players need). A zip in the folder that is not one of these
files stops the build, so false-positive-submission.md lists only this release. The script prints the
commands that publish with that body, set the labels (tools\Set-ReleaseLabels.ps1) and run tools\Check-ReleaseAssets.ps1.

No mod is packed: players get Realm of Thrones from Nexus Mods and the BUTR modules from the Steam
Workshop. The server exe carries Realm of Thrones Coop Compat as its ServerAddOns payload and both
zips of the RoT build of Coop; the client exe carries Realm of Thrones Coop Compat and Coodsz Join
Password as its ClientAddOns payload and the client zip of the RoT build of Coop
(src\CoodszPayload.targets, src\RotCoopBuild.targets). The two Coop zips are kept files, never built
here: they come from -AssetCache (artifacts\release-assets-cache), pinned by name and SHA256 in
RotStack.CoopBuild. They ride only inside the exes and are not release files.

The release repo is Coodsz/Coodsz-RealmOfThrones. If ReleaseNotice.RotRepo is ever a PLACEHOLDER-ROT value
again, only an unsigned test build runs (-SkipSigning).

Everything printed is also written to artifacts\make-rot-release-last.log.
#>
[CmdletBinding()]
param(
    # Where the release files go. Default: artifacts\rot-release-<version>
    [string] $Output = "",

    # Skip dotnet test (only for a quick local rebuild).
    [switch] $SkipTests,

    # Builds and tests into this folder (dotnet --artifacts-path) instead of each
    # project's bin and obj, and keeps the log there. For a test run beside other work.
    [string] $ArtifactsPath = "",

    # Folder with the two zips of the RoT build of Coop (tools\Build-RotCoop.ps1). Default: artifacts\release-assets-cache
    [string] $AssetCache = "",

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
# The real Realm reward key (CONTRIBUTING.md, Private files). The RoT tools carry no DedicatedServerRepair.
Assert-PrivateFiles | Out-Null
. (Join-Path $PSScriptRoot 'PayloadCheck.ps1')

$logFolder = if ($ArtifactsPath) { $ArtifactsPath } else { Join-Path $root 'artifacts' }
New-Item -ItemType Directory -Force -Path $logFolder | Out-Null
Start-Transcript -LiteralPath (Join-Path $logFolder 'make-rot-release-last.log') -Force | Out-Null

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

# The exact bytes of each zip must be inside the program under its resource name (Coodsz.RotCoop.<zip>,
# src\RotCoopBuild.targets), and the zips in $leftOut must not be. Single file publishes keep the managed dll
# uncompressed, so the zip shows as is.
function Assert-CarriedCoopZips([string] $program, [string[]] $zips, [string[]] $leftOut = @()) {
    $latin = [System.Text.Encoding]::GetEncoding(28591)
    $programText = $latin.GetString([System.IO.File]::ReadAllBytes($program))
    foreach ($zip in $zips) {
        $name = Split-Path -Leaf $zip
        if ($programText.IndexOf("Coodsz.RotCoop.$name", [StringComparison]::Ordinal) -lt 0) { Fail "$(Split-Path -Leaf $program) does not carry $name (no Coodsz.RotCoop.$name resource)." }
        if ($programText.IndexOf($latin.GetString([System.IO.File]::ReadAllBytes($zip)), [StringComparison]::Ordinal) -lt 0) { Fail "$(Split-Path -Leaf $program) carries another $name than the pinned one." }
        Write-Host ("  " + (Split-Path -Leaf $program) + " carries " + $name) -ForegroundColor DarkGray
    }
    foreach ($name in $leftOut) {
        if ($programText.IndexOf("Coodsz.RotCoop.$name", [StringComparison]::Ordinal) -ge 0) { Fail "$(Split-Path -Leaf $program) carries $name, which only the other tool needs." }
    }
}

# 1. Version and repo: ReleaseNotice.RotVersion, ReleaseNotice.RotRepo and both csproj files
$release = Get-Content -Raw -LiteralPath 'src\BannerlordCoopLauncher.Core\Updates\ReleaseNotice.cs'
$versionMatch = [regex]::Match($release, 'RotVersion\s*=\s*"v(?<v>[0-9.]+)"')
if (-not $versionMatch.Success) { Fail 'Could not read RotVersion from ReleaseNotice.cs.' }
$version = $versionMatch.Groups['v'].Value
$repoMatch = [regex]::Match($release, 'RotRepo\s*=\s*"(?<r>[^"]+)"')
if (-not $repoMatch.Success) { Fail 'Could not read RotRepo from ReleaseNotice.cs.' }
$repo = $repoMatch.Groups['r'].Value
$projects = @('src\RealmOfThronesHost\RealmOfThronesHost.csproj', 'src\RealmOfThronesLauncher\RealmOfThronesLauncher.csproj')
foreach ($project in $projects) {
    $projectVersion = [regex]::Match((Get-Content -Raw -LiteralPath $project), '<Version>(?<v>[^<]+)</Version>').Groups['v'].Value
    if ($projectVersion -ne $version) {
        Fail ("ReleaseNotice.RotVersion is v$version but $project says $projectVersion. Make them the same first.")
    }
}
Write-Host "Realm of Thrones tools v$version" -ForegroundColor Green
# A placeholder repo cannot be published to or updated from, so with one only a test build runs.
if ($repo.StartsWith('PLACEHOLDER-ROT', [StringComparison]::Ordinal) -and -not $SkipSigning) {
    Fail "ReleaseNotice.RotRepo is still $repo. Make the GitHub repo first, then put its owner/name in ReleaseNotice.RotRepo and in the Realm of Thrones entry of tools\release-assets.json. Until then only an unsigned test build runs (-SkipSigning)."
}
# The release body is made from these notes at the end, so stop now when they are not written yet.
$notesFile = Get-ReleaseNotesFile $repo $version
# Players read the instructions and the notes (without their leading comment, as Get-ReleaseBody makes it), so
# a real release stops while either still holds a <RELEASE-...> placeholder.
if (-not $SkipSigning) {
    $notesText = [regex]::Replace((Get-Content -Raw -Encoding UTF8 -LiteralPath $notesFile), '^\s*<!--.*?-->\s*', '', [System.Text.RegularExpressions.RegexOptions]::Singleline)
    foreach ($check in @(@('Coodsz-RealmOfThrones-Instructions.txt', (Get-Content -Raw -Encoding UTF8 -LiteralPath 'Coodsz-RealmOfThrones-Instructions.txt')), @($notesFile, $notesText))) {
        if ($check[1].Contains('<RELEASE-')) {
            Fail "$($check[0]) still has a <RELEASE-...> placeholder. Put the release repo and its /releases/latest link in first."
        }
    }
}

# 2. The RoT build of Coop: RotStack.CoopBuild pins both zips by name and SHA256. Both csproj files and
# tools\release-assets.json must say the same, and the kept files must be in the cache with that SHA256.
$stack = Get-Content -Raw -LiteralPath 'src\RealmOfThrones.Core\RotStack.cs'
$build = [regex]::Match($stack, 'CoopBuild\s*=\s*new\(\s*"[^"]+",\s*"[^"]+",\s*"(?<cn>[^"]+)",\s*"(?<cs>[0-9A-Fa-f]{64})",\s*"(?<sn>[^"]+)",\s*"(?<ss>[0-9A-Fa-f]{64})"')
if (-not $build.Success) { Fail 'Could not read the two zips and their SHA256 from RotStack.CoopBuild.' }
$coopZips = [ordered]@{
    Client = @{ Name = $build.Groups['cn'].Value; Sha256 = $build.Groups['cs'].Value.ToLowerInvariant() }
    Server = @{ Name = $build.Groups['sn'].Value; Sha256 = $build.Groups['ss'].Value.ToLowerInvariant() }
}
foreach ($project in $projects) {
    $text = Get-Content -Raw -LiteralPath $project
    foreach ($kind in $coopZips.Keys) {
        $name = [regex]::Match($text, "<CoodszRotCoop${kind}Name>(?<v>[^<]+)<").Groups['v'].Value
        $sha = [regex]::Match($text, "<CoodszRotCoop${kind}Sha256>(?<v>[^<]+)<").Groups['v'].Value
        # The client launcher carries no server zip.
        if ($kind -eq 'Server' -and $project -like '*Launcher*') {
            if ($name) { Fail "$project names a Coop server zip, but only the Server Tool carries it." }
            continue
        }
        if ($name -ne $coopZips[$kind].Name -or $sha.ToLowerInvariant() -ne $coopZips[$kind].Sha256) {
            Fail "$project carries $name ($sha), but RotStack.CoopBuild says $($coopZips[$kind].Name) ($($coopZips[$kind].Sha256)). Make them the same first."
        }
    }
}
# The Coop zips ride only inside the exes. The release has no kept files, so release-assets.json lists none.
$kept = @(Get-ReleaseAssetSpec $repo $version | Where-Object { $_.Source -eq 'release' })
if ($kept.Count -gt 0) { Fail "tools\release-assets.json lists kept files for Realm of Thrones ($(@($kept | ForEach-Object { $_.Name }) -join ', ')). The Coop-RoT zips are built into the tools and are not release files." }
if ([string]::IsNullOrWhiteSpace($AssetCache)) { $AssetCache = Join-Path $root 'artifacts\release-assets-cache' }
elseif (-not [System.IO.Path]::IsPathRooted($AssetCache)) { $AssetCache = Join-Path $root $AssetCache }
foreach ($kind in $coopZips.Keys) {
    $cached = Join-Path $AssetCache $coopZips[$kind].Name
    if (-not (Test-Path -LiteralPath $cached)) {
        Fail "$($coopZips[$kind].Name) is not in $AssetCache. Put the Coop $kind zip that tools\Build-RotCoop.ps1 made there and run again. Never build the server zip again for this: it changes on every build."
    }
    if ((Get-Sha256 $cached) -ne $coopZips[$kind].Sha256) {
        Fail "$cached has SHA256 $(Get-Sha256 $cached), but RotStack.CoopBuild pins $($coopZips[$kind].Sha256). Put the pinned file there."
    }
    $coopZips[$kind].Path = $cached
}

# 3. Realm of Thrones Coop Compat: the module the build embeds must be the version RotStack names.
$compatModule = 'compat\RealmOfThronesCoopCompat\module'
$compatVersion = [regex]::Match($stack, 'CompatVersion\s*=\s*"(?<v>[^"]+)"').Groups['v'].Value
foreach ($file in @('SubModule.xml', 'bin\Win64_Shipping_Server\RealmOfThrones.CoopCompat.dll', 'bin\Win64_Shipping_Client\RealmOfThrones.CoopCompat.dll')) {
    if (-not (Test-Path -LiteralPath (Join-Path $compatModule $file))) { Fail "Realm of Thrones Coop Compat is missing $file." }
}
$moduleVersion = [regex]::Match((Get-Content -Raw -LiteralPath (Join-Path $compatModule 'SubModule.xml')), '<Version\s+value="(?<v>[^"]+)"').Groups['v'].Value
if ($moduleVersion -ne $compatVersion) { Fail "Realm of Thrones Coop Compat is $moduleVersion, but RotStack.CompatVersion is $compatVersion. Run compat\RealmOfThronesCoopCompat\Build.cmd and commit module\." }
$joinPassword = 'compat\CoodszJoinPassword\module\bin\Win64_Shipping_Client'
foreach ($dll in @('CoodszJoinPassword.dll', 'CoodszMenuGuard.dll', 'CoodszSettingsSync.Coop.dll')) {
    if (-not (Test-Path -LiteralPath (Join-Path $joinPassword $dll))) { Fail "Coodsz Join Password lacks $dll." }
}

# Code signing (Microsoft Artifact Signing): check az login and restore the pinned tools now, so a missing
# login stops the release before the long build. -SkipSigning is for test builds only.
if ($SkipSigning) { Write-UnsignedWarning } else { Initialize-Signing }

if ([string]::IsNullOrWhiteSpace($Output)) { $Output = Join-Path $root "artifacts\rot-release-$version" }
elseif (-not [System.IO.Path]::IsPathRooted($Output)) { $Output = Join-Path $root $Output }
# A zip left from an earlier build (an older Coop-RoT zip) would land in false-positive-submission.md, so only this
# release's zips may be in the folder.
$releaseNames = @(Get-ReleaseAssetSpec $repo $version | ForEach-Object { $_.Name })
if (Test-Path -LiteralPath $Output) {
    $stray = @(Get-ChildItem -LiteralPath $Output -Filter '*.zip' -File | Where-Object { $releaseNames -notcontains $_.Name } | ForEach-Object { $_.Name })
    if ($stray.Count -gt 0) { Fail "$Output holds zips this release does not have: $($stray -join ', '). Delete them or pick another -Output, then run again." }
}
$work = Join-Path $Output 'work'
if (Test-Path -LiteralPath $work) { Remove-Item -LiteralPath $work -Recurse -Force }
New-Item -ItemType Directory -Force -Path $work | Out-Null

# dotnet builds into -ArtifactsPath when it is given.
$buildPath = if ($ArtifactsPath) { @('--artifacts-path', $ArtifactsPath) } else { @() }

# 4. Tests
if (-not $SkipTests) {
    Run 'dotnet' (@('test', 'tests\BannerlordCoopLauncher.Tests\BannerlordCoopLauncher.Tests.csproj', '-c', 'Release') + $buildPath)
}

# Sign the Coodsz module DLLs before the build packs them, so the payloads hold signed DLLs and the known hash list
# the Core build makes (Support\ModuleFingerprints) has their signed hashes. The release flow signs and commits them
# first (tools\Sign-ModuleDlls.ps1), so here every one is found signed. Every tool's DLLs are signed together, so a
# byte equal copy another tool carries never stays unsigned.
Invoke-ModuleDllSigning (Get-AllModuleDllPaths)

# 5. Publish. The Coop zips come from the checked cache files (RotCoopBuild.targets checks them again).
$single = @('-c', 'Release', '-r', 'win-x64', '--self-contained', 'true', '-p:PublishSingleFile=true', '-p:IncludeNativeLibrariesForSelfExtract=true', '-p:DebugType=None') + $buildPath
$publish = Join-Path $work 'publish'
$payloads = Join-Path $work 'payloads'
$clientZip = "-p:CoodszRotCoopClientZip=$($coopZips.Client.Path)"
Run 'dotnet' (@('publish', 'src\RealmOfThronesHost\RealmOfThronesHost.csproj', '-o', "$publish\server", "-p:CoodszPayloadCopyTo=$payloads\server", $clientZip, "-p:CoodszRotCoopServerZip=$($coopZips.Server.Path)") + $single)
Run 'dotnet' (@('publish', 'src\RealmOfThronesLauncher\RealmOfThronesLauncher.csproj', '-o', "$publish\client", "-p:CoodszPayloadCopyTo=$payloads\client", $clientZip) + $single)

# 6. Each payload inside each exe must be this build's, file for file (tools\PayloadCheck.ps1), and each
# exe carries the pinned Coop zips and no payload beyond these.
$server = "$publish\server\Coodsz Realm of Thrones Server Tool.exe"
$client = "$publish\client\Coodsz Realm of Thrones Client Launcher.exe"
# The csproj leaves the compat module's README out. The server payload also carries Coodsz Server Tools (RotServerTools).
Assert-Payload $server "$payloads\server" 'ServerAddOns' ((Get-PayloadPairs $compatModule 'RealmOfThrones.CoopCompat/' @('README.txt')) + (Get-PayloadPairs 'src\CoodszHost\ServerTools\DedicatedServer.CoodszTools' 'DedicatedServer.CoodszTools/'))
Assert-Payload $client "$payloads\client" 'ClientAddOns' ((Get-PayloadPairs 'compat\CoodszJoinPassword\module' 'DedicatedServer.CoodszJoinPassword/') + (Get-PayloadPairs $compatModule 'RealmOfThrones.CoopCompat/' @('README.txt')))
foreach ($pair in @(@('server', 'ServerAddOns.zip'), @('client', 'ClientAddOns.zip'))) {
    $made = @(Get-ChildItem -LiteralPath (Join-Path $payloads $pair[0]) -File | ForEach-Object { $_.Name })
    if (($made -join '|') -ne $pair[1]) { Fail "The $($pair[0]) build made the payloads $($made -join ', '); the release checks only $($pair[1])." }
}
Assert-CarriedCoopZips $server @($coopZips.Client.Path, $coopZips.Server.Path)
Assert-CarriedCoopZips $client @($coopZips.Client.Path) @($coopZips.Server.Name)
# Every Coodsz module folder in the payloads carries its LICENSE.txt, and the module DLLs are signed (ReleaseAssets.ps1).
Assert-ModulePayloads $repo @($server, $client)

# No GitHub token may ship. A token in C# code sits in the exe as UTF-16, so the zero bytes are
# dropped before the search and both encodings are caught. The payloads and the Coop zips are zipped
# inside the exe, so each file of each of those zips is searched too. Only the file name is printed.
$latin1 = [System.Text.Encoding]::GetEncoding(28591)
$searched = @($server, $client, $coopZips.Client.Path, $coopZips.Server.Path) + @(Get-ChildItem -LiteralPath $payloads -Recurse -File | ForEach-Object { $_.FullName })
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
Write-Host 'No GitHub token or Discord webhook in either exe, its payloads or the Coop zips.' -ForegroundColor Green

# 6b. Sign both exes, after the payload and token checks and before any copy or zip.
if (-not $SkipSigning) { Invoke-ReleaseSigning @($server, $client) }

# 7. The two exe zip: one folder with both programs and nothing else. LICENSE.txt and
# THIRD-PARTY-NOTICES.md ride inside the exes, and LICENSE.txt is its own release file too.
if (-not (Test-Path -LiteralPath 'THIRD-PARTY-NOTICES.md')) { Fail 'THIRD-PARTY-NOTICES.md is missing from the repository root.' }
$rotTools = @('Coodsz Realm of Thrones Server Tool.exe', 'Coodsz Realm of Thrones Client Launcher.exe')
$toolsStage = Join-Path $work 'tools'
$toolsFolder = Join-Path $toolsStage 'Coodsz Realm of Thrones Tools'
New-Item -ItemType Directory -Force -Path $toolsFolder | Out-Null
Copy-Item -LiteralPath $server -Destination $toolsFolder
Copy-Item -LiteralPath $client -Destination $toolsFolder
$toolsZip = Join-Path $Output "Coodsz-Realm-of-Thrones-Tools-v$version.zip"
Assert-StagedExesSigned $toolsStage
New-ReleaseZip $toolsStage $toolsZip
Assert-ToolsZip $toolsZip 'Coodsz Realm of Thrones Tools' $rotTools
$files = @($toolsZip)
# The two Coop zips stay inside the exes only; they are not release files.

# The files the guide sends players to that come from the repository (LICENSE.txt and the instructions).
foreach ($asset in (Get-ReleaseAssetSpec $repo $version)) {
    if ($asset.Source -ne 'repo') { continue }
    if (-not (Test-Path -LiteralPath $asset.Path)) { Fail "$($asset.Path) is missing from the repository, and the release needs it as $($asset.Name)." }
    Copy-Item -LiteralPath $asset.Path -Destination (Join-Path $Output $asset.Name) -Force
    $files += (Join-Path $Output $asset.Name)
}

# 8. Checksums, then the check against tools\release-assets.json
$sums = foreach ($file in $files) { (Get-Sha256 $file) + '  ' + (Split-Path -Leaf $file) }
# LF line ends, like sha256sum writes, so sha256sum -c reads the file on any system.
[System.IO.File]::WriteAllText((Join-Path $Output 'SHA256SUMS.txt'), (($sums -join "`n") + "`n"), [System.Text.Encoding]::ASCII)
Assert-ReleaseFolder $repo $version $Output
Remove-Item -LiteralPath $work -Recurse -Force

# 9. The release page: release-body.md starts with the download line. release-labels.json
# is empty: every Realm of Thrones release file is one players need.
Write-ReleasePageFiles $repo $version $Output -Unsigned:$SkipSigning

Write-Host ''
Write-Host "Done. Upload these to the v$version release on ${repo}:" -ForegroundColor Green
foreach ($file in ($files + (Join-Path $Output 'SHA256SUMS.txt'))) { Write-Host ('  ' + (Split-Path -Leaf $file) + '  (' + [math]::Round((Get-Item -LiteralPath $file).Length / 1MB, 1) + ' MB)') }
Write-PublishCommands $repo $version $Output ($files + (Join-Path $Output 'SHA256SUMS.txt')) -Unsigned:$SkipSigning
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
