<#
.SYNOPSIS
Builds the RoT build of Coop: one pinned Coop client module and the matching dedicated server overlay.

.DESCRIPTION
Run in Windows PowerShell or pwsh:

    .\tools\Build-RotCoop.ps1 -CoopCommit <sha> -DsCommit <sha>

It checks out -CoopCommit in a local clone of BannerlordCoop and -DsCommit in a local clone of
BannerlordCoop.DedicatedServer (detached, after git clean), and builds both the way Coop's own
nightly does:

- Client: the nightly's restore and build (.github\workflows\nightly-release.yml, "Restore .NET
  Framework build inputs" and "Build release module") with SourceRevisionId = the Coop commit, so
  Common.dll's ProductVersion reads 0.1.8+<sha>. Staged like the nightly's "Stage module" step,
  id CoopNightly and version v0.1.8, only the Name is "Bannerlord Co-op, RoT build <date>".
- Server: build-release.ps1 as written (pins of our own client bin, Shipping build, obfuscar), the
  BannerlordCoopServer.exe publish, and the Coop parts of windows\run-windows.ps1 and
  scripts\release.ps1 (the server Coop module, release-info.txt, blank saves, mod-config seed).

Only Coop's own files go into the zips. Game files are never packed: no TaleWorlds.*, SandBox*,
StoryMode*, NavalDLC*, Steamworks.NET.dll, steam_api64.dll, engine bin, Native, Main_map or
DsAssetPackages. The host's Workshop Coop server and game supply those, so the server zip is an
overlay laid OVER the Workshop server folder, not a whole server. The Electron server manager is
left out too.

Outputs in -OutDir:

    Coop-RoT-Client-<date>-<sha7>.zip   (Coop\ the module as the nightly stages it, LICENSE and
                                        NOTICE.md beside it)
    Coop-RoT-Server-<date>-<sha7>.zip   (the server overlay: BannerlordCoopServer.exe, EULA.md,
                                        LICENSE.txt, release-info.txt, engine\..., server-data\...)
    <zip>.sha256 for each, and Coop-RoT-<date>-<sha7>.txt with the build string, the commits,
    the hashes and the list of files left out.

The build string is read back from Common.dll in both zips' stages and must be equal. The client
zip is byte for byte the same on a rebuild; the server zip is not, because obfuscar renames
DedicatedServer.Core differently each run (the pins and the build string stay the same).
Each run leaves its stage (about 150 MB) in a new -WorkDir subfolder; clear old ones by hand.

The game folder (-GameRoot) and the Workshop server bin (-ServerBin) are only read. The script
refuses to write anywhere under the game folder, any Steam library (the Steam folder and every
library in its steamapps\libraryfolders.vdf, and any path with \steamapps\ in it) or
Documents\Mount and Blade II Bannerlord, starts no game, server or tool, and runs at below normal
priority so a live test on this PC keeps its CPU. Nothing is pushed or published.

-CoopRepo and -DsRepo must be build clones only, since the script runs git checkout and
git clean -xdf in them, which wipes untracked files. A clone counts as a build clone when every
remote's push URL is DISABLED (git remote set-url --push origin DISABLED) or when it has the file
.git\coodsz-build-clone (make it by hand once). Any other clone is refused.
#>
[CmdletBinding()]
param(
    # BannerlordCoop commit to build (our merge commit on rot-coop/<date>). Its full sha is the build string.
    [Parameter(Mandatory = $true)] [string] $CoopCommit,

    # BannerlordCoop.DedicatedServer commit to build (our merge commit on rot-ds/<date>).
    [Parameter(Mandatory = $true)] [string] $DsCommit,

    [string] $CoopRepo = 'C:\Users\Admin\Projects\rotcoop\BannerlordCoop',
    [string] $DsRepo = 'C:\Users\Admin\Projects\rotcoop\ds',
    [string] $OutDir = 'C:\Users\Admin\Projects\rotcoop\out',

    # Scratch folder; each run gets a new subfolder, so nothing is deleted.
    [string] $WorkDir = 'C:\Users\Admin\Projects\rotcoop\work',

    # Bannerlord 1.4.8 WITH War Sails, read only. Without NavalDLC the naval projects build empty.
    [string] $GameRoot = $(if ($env:BANNERLORD_DIR) { $env:BANNERLORD_DIR } else { 'C:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord' }),

    # A TaleWorlds Win64_Shipping_Server bin, read only: the Workshop Coop server's engine bin.
    # The server build compiles against it, and the server Coop bin leaves out what it already carries.
    [string] $ServerBin = 'C:\Program Files (x86)\Steam\steamapps\workshop\content\261550\3770450698\DedicatedServer\engine\bin\Win64_Shipping_Server',

    # The label date in the module Name. Default: the Coop commit's date, so a rebuild gives the same Name.
    [string] $LabelDate = ''
)

Set-StrictMode -Version 3.0
$ErrorActionPreference = 'Stop'
try { [Diagnostics.Process]::GetCurrentProcess().PriorityClass = 'BelowNormal' } catch { }
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem

function Get-FullPath([string] $path) { [IO.Path]::GetFullPath($path).TrimEnd('\') }

# Refuses a write target inside the game, any Steam library or the game's Documents folder.
$steamRoots = @('C:\Program Files (x86)\Steam')
try { $steamRoots += (Get-ItemProperty -Path 'HKCU:\Software\Valve\Steam' -Name SteamPath -ErrorAction Stop).SteamPath } catch { }
$protected = [Collections.Generic.List[string]]::new()
$protected.Add((Get-FullPath $GameRoot))
$protected.Add((Get-FullPath (Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'Mount and Blade II Bannerlord')))
foreach ($steam in $steamRoots) {
    $protected.Add((Get-FullPath $steam))
    $vdf = Join-Path $steam 'steamapps\libraryfolders.vdf'
    if (Test-Path -LiteralPath $vdf) {
        foreach ($match in [regex]::Matches((Get-Content -LiteralPath $vdf -Raw), '"path"\s+"([^"]+)"')) {
            $protected.Add((Get-FullPath ($match.Groups[1].Value -replace '\\\\', '\')))
        }
    }
}
function Assert-Writable([string] $path) {
    $full = Get-FullPath $path
    if (($full + '\').IndexOf('\steamapps\', [StringComparison]::OrdinalIgnoreCase) -ge 0) {
        throw "Refusing to write to $full (inside a Steam library)."
    }
    foreach ($p in $protected) {
        if ($full -eq $p -or $full.StartsWith($p + '\', [StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to write to $full (inside $p)."
        }
    }
}

function Invoke-Git([string] $repo) {
    $out = & git -C $repo @args
    if ($LASTEXITCODE -ne 0) { throw "git $($args -join ' ') failed in $repo" }
    $out
}

# Checks out one commit detached in a clean clone and wipes build leftovers (git clean stays inside the clone).
# Only in a build clone: every remote's push URL DISABLED, or .git\coodsz-build-clone, so a working clone's
# untracked files are never wiped.
function Set-RepoCommit([string] $repo, [string] $commit) {
    Assert-Writable $repo
    if (-not (Test-Path -LiteralPath (Join-Path $repo '.git'))) { throw "No git clone at $repo." }
    $remotes = @(Invoke-Git $repo remote)
    $pushOff = $remotes.Count -gt 0 -and -not ($remotes | Where-Object { (Invoke-Git $repo remote get-url --push $_).Trim() -ne 'DISABLED' })
    if (-not $pushOff -and -not (Test-Path -LiteralPath (Join-Path $repo '.git\coodsz-build-clone'))) {
        throw "$repo is not a build clone, so it was left alone (the script runs git clean -xdf there). Use a clone with every push URL set to DISABLED, or create $repo\.git\coodsz-build-clone in a clone kept only for this script."
    }
    if (Invoke-Git $repo status --porcelain --untracked-files=no) { throw "$repo has uncommitted changes; commit them first." }
    $sha = (Invoke-Git $repo rev-parse --verify "$commit^{commit}").Trim()
    Invoke-Git $repo checkout -q --detach $sha | Out-Null
    Invoke-Git $repo clean -xdfq | Out-Null
    $sha
}

function Invoke-Checked([string] $what, [scriptblock] $block) {
    & $block
    if ($LASTEXITCODE -ne 0) { throw "$what failed (exit $LASTEXITCODE)." }
}

function Copy-Tree([string] $source, [string] $target, [string[]] $extra = @()) {
    robocopy $source $target /E @extra /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "robocopy $source -> $target failed: $LASTEXITCODE" }
}

# Zips a folder's contents with forward slash entry names in a fixed order, every entry stamped with
# the commit's time, so the same build gives the same zip bytes. Then checks the PK header.
function New-Zip([string] $folder, [string] $zip, [datetime] $stamp) {
    Assert-Writable $zip
    if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip -Force }
    $base = (Get-FullPath $folder) + '\'
    $files = @(Get-ChildItem -LiteralPath $folder -Recurse -Force -File | ForEach-Object { $_.FullName.Substring($base.Length) } | Sort-Object { $_.ToLowerInvariant() })
    $archive = [IO.Compression.ZipFile]::Open($zip, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($relative in $files) {
            $entry = $archive.CreateEntry($relative.Replace('\', '/'), [IO.Compression.CompressionLevel]::Optimal)
            $entry.LastWriteTime = $stamp
            $in = [IO.File]::OpenRead((Join-Path $folder $relative))
            $out = $entry.Open()
            try { $in.CopyTo($out) } finally { $out.Dispose(); $in.Dispose() }
        }
    } finally { $archive.Dispose() }
    $head = New-Object byte[] 2
    $fs = [IO.File]::OpenRead($zip)
    try { [void]$fs.Read($head, 0, 2) } finally { $fs.Dispose() }
    if ($head[0] -ne 0x50 -or $head[1] -ne 0x4B) { throw "$zip is not a zip." }
}

function Write-Sha256([string] $file) {
    $hash = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant()
    [IO.File]::WriteAllText("$file.sha256", "$hash  $(Split-Path $file -Leaf)`n", (New-Object Text.UTF8Encoding($false)))
    $hash
}

# Game files the zips must never carry, matched on file names.
$gameFilePatterns = @('TaleWorlds.*', 'SandBox*', 'StoryMode*', 'NavalDLC*', 'Steamworks.NET.dll', 'steam_api64.dll')
function Test-GameFile([string] $name) {
    foreach ($pattern in $gameFilePatterns) { if ($name -like $pattern) { return $true } }
    $false
}
function Assert-NoGameFiles([string] $root) {
    $bad = @(Get-ChildItem -LiteralPath $root -Recurse -Force | Where-Object {
        (-not $_.PSIsContainer -and (Test-GameFile $_.Name)) -or
        ($_.PSIsContainer -and $_.Name -in @('Native', 'SandBox', 'SandBoxCore', 'NavalDLC', 'Main_map', 'DsAssetPackages', 'SceneObj', 'dotnet'))
    })
    if ($bad.Count -gt 0) { throw "Game files in $root`: $(($bad | ForEach-Object { $_.FullName }) -join ', ')" }
    if (Test-Path -LiteralPath (Join-Path $root 'engine\bin\Win64_Shipping_Server\TaleWorlds.Starter.DotNetCore.dll')) { throw "The engine bin is in $root." }
}

function Get-BuildString([string] $commonDll) {
    if (-not (Test-Path -LiteralPath $commonDll)) { throw "Common.dll not found: $commonDll" }
    [Diagnostics.FileVersionInfo]::GetVersionInfo($commonDll).ProductVersion
}

# --- Inputs ---------------------------------------------------------------------------------------
if (-not (Test-Path -LiteralPath (Join-Path $GameRoot 'bin\Win64_Shipping_Client\TaleWorlds.CampaignSystem.dll'))) { throw "No Bannerlord install at $GameRoot." }
if (-not (Test-Path -LiteralPath (Join-Path $GameRoot 'Modules\NavalDLC\bin\Win64_Shipping_Client\NavalDLC.dll'))) { throw "$GameRoot has no War Sails; the naval projects would build empty." }
if (-not (Test-Path -LiteralPath (Join-Path $ServerBin 'TaleWorlds.Library.dll'))) { throw "No TaleWorlds server bin at $ServerBin." }
$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue).Source
if (-not $dotnet) { throw 'The .NET SDK is required.' }
foreach ($p in $OutDir, $WorkDir) { Assert-Writable $p; New-Item -ItemType Directory -Force -Path $p | Out-Null }

$coopSha = Set-RepoCommit $CoopRepo $CoopCommit
$dsSha = Set-RepoCommit $DsRepo $DsCommit
$coopShort = $coopSha.Substring(0, 9)
if (-not $LabelDate) { $LabelDate = (Invoke-Git $CoopRepo log -1 --format=%cs $coopSha).Trim() }
if ($LabelDate -notmatch '^\d{4}-\d{2}-\d{2}$') { throw "LabelDate must be yyyy-MM-dd, not $LabelDate." }
$run = Join-Path $WorkDir ('{0:yyyyMMdd-HHmmss}-{1}' -f (Get-Date), $coopShort)
New-Item -ItemType Directory -Force -Path $run | Out-Null
Write-Host "Coop $coopSha, server $dsSha, label $LabelDate, work $run"

# --- Client: restore (nightly "Restore .NET Framework build inputs") -------------------------------
$cache = Join-Path $WorkDir 'nuget-cache'
New-Item -ItemType Directory -Force -Path $cache | Out-Null
function Get-NuGetPackage([string] $id, [string] $version, [string] $expectedSha256 = '') {
    $lower = $id.ToLowerInvariant()
    $file = Join-Path $cache "$lower.$version.nupkg"
    if (-not (Test-Path -LiteralPath $file)) {
        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
        Invoke-WebRequest -UseBasicParsing -Uri "https://api.nuget.org/v3-flatcontainer/$lower/$version/$lower.$version.nupkg" -OutFile "$file.part"
        Move-Item -LiteralPath "$file.part" -Destination $file -Force
    }
    if ($expectedSha256 -and (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -ne $expectedSha256) { throw "$file does not match its pinned SHA256." }
    $file
}
$refNupkg = Get-NuGetPackage 'Microsoft.NETFramework.ReferenceAssemblies.net472' '1.0.3' 'FFA0A5570A39F911399164D0581FFDDEF99B5E3DFBAA5F220E5CE22969BCF57C'
$refRoot = Join-Path $run 'net472'
[IO.Compression.ZipFile]::ExtractToDirectory($refNupkg, $refRoot)
$packagesConfig = [xml](Get-Content -Raw -LiteralPath (Join-Path $CoopRepo 'source\Coop\packages.config'))
foreach ($package in $packagesConfig.packages.package) {
    $target = Join-Path $CoopRepo "source\packages\$($package.id).$($package.version)"
    if (Test-Path -LiteralPath $target) { continue }
    [IO.Compression.ZipFile]::ExtractToDirectory((Get-NuGetPackage $package.id $package.version), $target)
}

# --- Client: build (nightly "Build release module") ------------------------------------------------
# -p:ModName= turns Deploy.targets off, so nothing is copied into the game's Modules folder.
$env:BANNERLORD_DIR = $GameRoot
Push-Location (Join-Path $CoopRepo 'source')
try {
    Invoke-Checked 'Coop client build' {
        & $dotnet build Coop/Coop.csproj -c Release --nologo `
            "-p:CustomAfterMicrosoftCommonTargets=$refRoot\build\Microsoft.NETFramework.ReferenceAssemblies.net472.targets" `
            "-p:SourceRevisionId=$coopSha" -p:IncludeSourceRevisionInInformationalVersion=true `
            -p:ModName= -p:DeployFromWorktree=false -p:CoopTesterBuild=true -p:NuGetAudit=false `
            --consoleLoggerParameters:ErrorsOnly
    }
} finally { Pop-Location }

# --- Client: stage (nightly "Stage module") --------------------------------------------------------
$clientRoot = Join-Path $run 'client'
$module = Join-Path $clientRoot 'Coop'
$clientBin = Join-Path $module 'bin\Win64_Shipping_Client'
New-Item -ItemType Directory -Force -Path $clientBin, (Join-Path $module 'GUI\Prefabs') | Out-Null
$buildOut = Join-Path $CoopRepo 'source\Coop\bin\Release'
$clientLeftOut = @()
foreach ($dll in Get-ChildItem -LiteralPath $buildOut -Filter *.dll) {
    if ($dll.Name -like 'TaleWorlds.*' -or $dll.Name -like 'SandBox.*' -or $dll.Name -like 'StoryMode.*') { $clientLeftOut += $dll.Name; continue }
    Copy-Item -LiteralPath $dll.FullName -Destination $clientBin
}
Copy-Item -LiteralPath (Join-Path $buildOut 'Coop.CrashReporter.exe') -Destination $clientBin
foreach ($required in 'Coop.dll', 'Common.dll', 'Coop.Core.dll', 'Coop.Steam.dll', 'GameInterface.dll', 'Missions.dll', 'Coop.Naval.dll', 'Missions.Naval.dll', 'Coop.CrashReporter.exe') {
    $f = Join-Path $clientBin $required
    if (-not (Test-Path -LiteralPath $f) -or (Get-Item -LiteralPath $f).Length -eq 0) { throw "The client build has no $required." }
}
$deploy = Join-Path $CoopRepo 'deploy'
Copy-Item -LiteralPath (Join-Path $deploy 'Server Instructions.md'), (Join-Path $deploy 'mod-config.default.json') -Destination $module
foreach ($folder in 'ModuleData', 'ThirdPartyNotices', 'GUI') { Copy-Tree (Join-Path $deploy $folder) (Join-Path $module $folder) }
Copy-Tree (Join-Path $CoopRepo 'UIMovies') (Join-Path $module 'GUI\Prefabs')

$coopVersion = ([regex]::Match((Get-Content -Raw -LiteralPath (Join-Path $CoopRepo 'source\Directory.Build.props')), '<CoopVersion>([^<]+)</CoopVersion>')).Groups[1].Value
$gameVersion = ([regex]::Match((Get-Content -Raw -LiteralPath (Join-Path $CoopRepo 'source\Coop\Coop.csproj')), '<GameVersion>([^<]+)</GameVersion>')).Groups[1].Value
if (-not $coopVersion -or -not $gameVersion) { throw 'CoopVersion or GameVersion not found.' }
$moduleName = "Bannerlord Co-op, RoT build $LabelDate"
$templateBytes = [IO.File]::ReadAllBytes((Join-Path $deploy 'SubModule.xml'))
$hasBom = $templateBytes.Length -ge 3 -and $templateBytes[0] -eq 0xEF -and $templateBytes[1] -eq 0xBB -and $templateBytes[2] -eq 0xBF
$xmlText = (New-Object Text.UTF8Encoding($false)).GetString($templateBytes).TrimStart([char]0xFEFF)
$xmlText = $xmlText.Replace('<Name value="Bannerlord Co-op"/>', "<Name value=`"$moduleName`"/>")
$xmlText = ([regex]'<Id value="Coop"/>').Replace($xmlText, '<Id value="CoopNightly"/>', 1)
$xmlText = $xmlText.Replace('<Module Id="CoopNightly"/>', '<Module Id="Coop"/>')
$xmlText = $xmlText.Replace('${main_class}', 'CoopMod').Replace('${version}', "v$coopVersion").Replace('${game_version}', $gameVersion)
[IO.File]::WriteAllText((Join-Path $module 'SubModule.xml'), $xmlText, (New-Object Text.UTF8Encoding($hasBom)))
# The nightly's own checks on the written SubModule.xml.
$normalized = $xmlText.Replace("`r", '')
if (([regex]::Matches($normalized, [regex]::Escape("<Name value=`"$moduleName`"/>"))).Count -ne 2) { throw 'SubModule.xml must name the module twice.' }
if ($normalized -notmatch '(?m)^    <Id value="CoopNightly"/>$') { throw 'SubModule.xml id is not CoopNightly.' }
if ($normalized -notmatch '(?m)^        <Module Id="Coop"/>$') { throw 'SubModule.xml must list Coop as incompatible.' }
if ($normalized.Contains('<Module Id="CoopNightly"/>') -or $normalized.Contains('${')) { throw 'SubModule.xml was not fully written.' }

# Coop's LICENSE and NOTICE go beside the module, not inside it.
foreach ($legal in 'LICENSE', 'NOTICE.md') {
    $f = Join-Path $CoopRepo $legal
    if (Test-Path -LiteralPath $f) { Copy-Item -LiteralPath $f -Destination $clientRoot }
}
Assert-NoGameFiles $clientRoot
$clientBuild = Get-BuildString (Join-Path $clientBin 'Common.dll')
if ($clientBuild -ne "$coopVersion+$coopSha") { throw "Client build string is $clientBuild, expected $coopVersion+$coopSha." }

# --- Server: pins, Shipping build and obfuscation (build-release.ps1 as written) -------------------
Push-Location $DsRepo
try {
    Invoke-Checked 'dotnet tool restore' { & $dotnet tool restore }
    Invoke-Checked 'release dependency restore' { & $dotnet restore (Join-Path $DsRepo 'source\ReleaseDependencies\ReleaseDependencies.csproj') --nologo --verbosity quiet }
    & (Join-Path $DsRepo 'build-release.ps1') -CoopBin $clientBin -Target windows -ServerBin $ServerBin -GameRoot $GameRoot
    Invoke-Checked 'BannerlordCoopServer publish' {
        & $dotnet publish (Join-Path $DsRepo 'source\DedicatedServer.Windows.Launcher\DedicatedServer.Windows.Launcher.csproj') -c Release -v q /nodeReuse:false -p:UseSharedCompilation=false
    }
} finally { Pop-Location }
$distBin = Join-Path $DsRepo 'dist\Modules\DedicatedServer.Windows\bin\Win64_Shipping_Server'
foreach ($f in 'DedicatedServer.Windows.dll', 'DedicatedServer.Core.dll') { if (-not (Test-Path -LiteralPath (Join-Path $distBin $f))) { throw "build-release.ps1 wrote no $f." } }

# --- Server: the overlay (Coop's update layer of scripts\release.ps1, Coop's own files only) -------
$serverRoot = Join-Path $run 'server'
$engine = Join-Path $serverRoot 'engine'
$svr = 'Win64_Shipping_Server'
$serverLeftOut = New-Object System.Collections.Generic.List[string]
New-Item -ItemType Directory -Force -Path $serverRoot | Out-Null

Copy-Item -LiteralPath (Join-Path $DsRepo 'source\DedicatedServer.Windows.Launcher\bin\Release\net6.0\win-x64\publish\BannerlordCoopServer.exe') -Destination $serverRoot
Copy-Item -LiteralPath (Join-Path $DsRepo 'EULA.txt') -Destination (Join-Path $serverRoot 'EULA.md')
Copy-Item -LiteralPath (Join-Path $DsRepo 'LICENSE.txt') -Destination $serverRoot

$dsModule = Join-Path $engine "Modules\DedicatedServer.Windows"
New-Item -ItemType Directory -Force -Path (Join-Path $dsModule "bin\$svr"), (Join-Path $engine "bin\$svr") | Out-Null
Copy-Item -LiteralPath (Join-Path $DsRepo 'dist\Modules\DedicatedServer.Windows\SubModule.xml') -Destination $dsModule
Copy-Item -LiteralPath (Join-Path $distBin 'DedicatedServer.Windows.dll'), (Join-Path $distBin 'DedicatedServer.Core.dll') -Destination (Join-Path $dsModule "bin\$svr")
Copy-Item -LiteralPath (Join-Path $distBin 'DedicatedServer.Core.dll') -Destination (Join-Path $engine "bin\$svr")

# The server Coop module (run-windows.ps1 "Coop server module"): the module root without bin, the
# client bin moved to Win64_Shipping_Server minus what the server bin already carries at the same or a
# newer version, SubModule.xml through Copy-SubModuleXml (no category, no depends, no submodules).
$coopStage = Join-Path $engine 'Modules\Coop'
Copy-Tree $module $coopStage @('/XD', (Join-Path $module 'bin'), '/XF', 'SubModule.xml')
$coopServerBin = Join-Path $coopStage "bin\$svr"
New-Item -ItemType Directory -Force -Path $coopServerBin | Out-Null
$sameAsServer = @('SandBox.dll')
foreach ($dll in Get-ChildItem -LiteralPath $ServerBin -Filter *.dll) {
    $twin = Join-Path $clientBin $dll.Name
    if (-not (Test-Path -LiteralPath $twin)) { continue }
    try {
        $mainVersion = [Reflection.AssemblyName]::GetAssemblyName($dll.FullName).Version
        $coopDllVersion = [Reflection.AssemblyName]::GetAssemblyName($twin).Version
        if ($mainVersion -ge $coopDllVersion) { $sameAsServer += $dll.Name }
    } catch { $sameAsServer += $dll.Name }
}
# run-windows.ps1 puts these in the engine bin (its TPA unify list) and takes them out of the Coop bin.
$unified = @('System.Diagnostics.DiagnosticSource', 'System.Threading.Channels', 'System.Collections.Immutable', 'System.Text.Json',
    'System.Reflection.Metadata', 'System.Text.Encoding.CodePages', 'System.IO.Pipelines', 'System.Text.Encodings.Web', 'Microsoft.Bcl.AsyncInterfaces')
$runWindows = Get-Content -Raw -LiteralPath (Join-Path $DsRepo 'windows\run-windows.ps1')
foreach ($name in $unified) { if (-not $runWindows.Contains("Name = '$name'")) { throw "run-windows.ps1 no longer unifies $name; recheck this list." } }
foreach ($file in Get-ChildItem -LiteralPath $clientBin -File) {
    $base = [IO.Path]::GetFileNameWithoutExtension($file.Name)
    if (Test-GameFile $file.Name) { $serverLeftOut.Add("engine\Modules\Coop\bin\$svr\$($file.Name) (game file)"); continue }
    if ($file.Name -eq '0Harmony.dll') { continue }
    if ($sameAsServer -contains $file.Name) { $serverLeftOut.Add("engine\Modules\Coop\bin\$svr\$($file.Name) (the server bin has it)"); continue }
    if ($unified -contains $base) { $serverLeftOut.Add("engine\Modules\Coop\bin\$svr\$($file.Name) (engine bin copy, from the Workshop base)"); continue }
    Copy-Item -LiteralPath $file.FullName -Destination $coopServerBin
}
# The net6.0 0Harmony (the net472 one calls an API .NET 6 removed).
Copy-Item -LiteralPath (Join-Path $DsRepo 'source\DedicatedServer.Core\bin\Release\0Harmony.dll') -Destination $coopServerBin -Force
$coopXml = [xml](Get-Content -Raw -LiteralPath (Join-Path $module 'SubModule.xml'))
$coopNode = $coopXml.SelectSingleNode('Module')
foreach ($drop in 'ModuleCategory', 'DependedModules') {
    $n = $coopNode.SelectSingleNode($drop)
    if ($null -ne $n) { [void]$coopNode.RemoveChild($n) }
}
$subs = $coopNode.SelectSingleNode('SubModules')
if ($null -ne $subs) { foreach ($n in @($subs.SelectNodes('SubModule'))) { [void]$subs.RemoveChild($n) } }
$coopXml.Save((Join-Path $coopStage 'SubModule.xml'))
$serverLeftOut.Add("engine\Modules\Coop\bin\$svr\ view and GauntletUI layer, its closure, Steamworks.NET.dll and steam_api64.dll (game files run-windows.ps1 stages from the game; the Workshop base has them)")

# Blank saves and the mod-config seed (release.ps1 "Refresh small release-owned seeds").
foreach ($target in (Join-Path $engine "bin\$svr"), (Join-Path $serverRoot 'server-data\Game Saves')) {
    New-Item -ItemType Directory -Force -Path $target | Out-Null
    foreach ($save in 'default_new_game.sav', 'default_new_game_warsails.sav') { Copy-Item -LiteralPath (Join-Path $DsRepo "docker\$save") -Destination $target }
}
Copy-Item -LiteralPath (Join-Path $module 'mod-config.default.json') -Destination (Join-Path $serverRoot 'server-data\mod-config.json')
$serverLeftOut.Add('engine\bin\ (TaleWorlds engine), the TPA unify System.* DLLs and the generated TaleWorlds.Starter.DotNetCore.deps.json (the Workshop base has them)')
$serverLeftOut.Add('engine\Modules\Native, SandBox, SandBoxCore, NavalDLC, engine\dotnet, Parameters, XmlSchemas (game and Microsoft files)')
$serverLeftOut.Add('engine\Modules\DedicatedServer.Windows\SceneObj (Main_map) and DsAssetPackages (stripped game assets)')
$serverLeftOut.Add('BannerlordCoopServerManager.exe and its Electron files (not needed)')

# release-info.txt in Coop's format (scripts\release.ps1); the Paired Coop line and the pins are what readers parse.
$pinLines = foreach ($name in 'Coop.Core', 'GameInterface', 'Common', 'Coop.Steam') {
    '  {0,-18} {1}' -f "$name.dll", (Get-FileHash -LiteralPath (Join-Path $clientBin "$name.dll") -Algorithm SHA256).Hash.ToLowerInvariant()
}
$gameVersionLine = ([regex]::Match((Get-Content -Raw -LiteralPath (Join-Path $GameRoot 'bin\Win64_Shipping_Client\Version.xml')), 'v\d+\.\d+\.\d+')).Value
$releaseInfo = @"
Bannerlord Coop Dedicated Server (Windows x64)
==============================================

Built:        $LabelDate
Paired Coop:  BannerlordCoop commit $coopSha
Game version: Mount & Blade II: Bannerlord $gameVersionLine
Server:       BannerlordCoop.DedicatedServer commit $dsSha

RoT build of Coop: an unofficial local build of Coop's own code for Realm of Thrones.
This folder is an overlay on the Workshop Coop server, not a whole server.

This server only hosts the exact Coop module build it was released with (SHA-256
verified at boot and again before the listen socket opens). If the Coop module
under engine\Modules\Coop is modified or replaced, the server refuses to start
with exit code 4. Clients must run the matching Coop mod build.

Pinned Coop assemblies (SHA-256):
$($pinLines -join "`r`n")
"@
[IO.File]::WriteAllText((Join-Path $serverRoot 'release-info.txt'), $releaseInfo.Replace("`r`n", "`n").Replace("`n", "`r`n") + "`r`n", (New-Object Text.UTF8Encoding($true)))

Assert-NoGameFiles $serverRoot
$serverBuild = Get-BuildString (Join-Path $coopServerBin 'Common.dll')
if ($serverBuild -ne $clientBuild) { throw "Server build string $serverBuild differs from the client's $clientBuild." }

# --- Zip, hash, summary ------------------------------------------------------------------------------
$tag = "$LabelDate-$($coopSha.Substring(0, 7))"
$clientZip = Join-Path $OutDir "Coop-RoT-Client-$tag.zip"
$serverZip = Join-Path $OutDir "Coop-RoT-Server-$tag.zip"
$stamp = [datetime]::Parse((Invoke-Git $CoopRepo log -1 --format=%cI $coopSha).Trim())
New-Zip $clientRoot $clientZip $stamp
New-Zip $serverRoot $serverZip $stamp
$clientHash = Write-Sha256 $clientZip
$serverHash = Write-Sha256 $serverZip

$summary = @(
    "RoT build of Coop $tag",
    "Build string (Common.dll ProductVersion, client and server): $clientBuild",
    "Module: id CoopNightly, version v$coopVersion, name $moduleName",
    "BannerlordCoop commit: $coopSha",
    "DedicatedServer commit: $dsSha",
    "Game root read: $GameRoot ($gameVersionLine)",
    "Server reference bin read: $ServerBin",
    '',
    "$(Split-Path $clientZip -Leaf)  $((Get-Item -LiteralPath $clientZip).Length) bytes  sha256 $clientHash",
    "$(Split-Path $serverZip -Leaf)  $((Get-Item -LiteralPath $serverZip).Length) bytes  sha256 $serverHash",
    '',
    'Left out of the client zip (nightly filter):'
) + @($clientLeftOut | ForEach-Object { "  bin\Win64_Shipping_Client\$_" }) + @('', 'Left out of the server overlay:') + @($serverLeftOut | ForEach-Object { "  $_" }) +
@('', 'DLLs and the server exe are unsigned; Smart App Control may block BannerlordCoopServer.exe.')
$summaryFile = Join-Path $OutDir "Coop-RoT-$tag.txt"
[IO.File]::WriteAllText($summaryFile, ($summary -join "`r`n") + "`r`n", (New-Object Text.UTF8Encoding($false)))
$summary | ForEach-Object { Write-Host $_ }
