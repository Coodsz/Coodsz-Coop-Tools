[CmdletBinding()]
param(
    [switch]$DryRun,
    [switch]$SkipAutoDetect,
    [switch]$IgnoreRunningProcesses,
    [switch]$VerifyPackageOnly,
    [string]$ServerPath,
    [string]$ModulePath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

$packageRoot = [System.IO.Path]::GetFullPath($PSScriptRoot)
$clientPayload = Join-Path $packageRoot 'EVERY PLAYER - COOP CLIENT UPDATE'
$legacyCleanupRoot = Join-Path $packageRoot 'legacy-cleanup-manifests'
$payloadManifestPath = Join-Path $packageRoot 'payload-sha256.json'

function ConvertTo-ExtendedFileSystemPath {
    param([Parameter(Mandatory = $true)][string] $Path)

    if ($Path.StartsWith('\\?\', [StringComparison]::Ordinal)) {
        return $Path
    }
    $fullPath = [System.IO.Path]::GetFullPath($Path)
    if ($fullPath.StartsWith('\\', [StringComparison]::Ordinal)) {
        return '\\?\UNC\' + $fullPath.Substring(2)
    }
    return '\\?\' + $fullPath
}

function Test-LongFile {
    param([Parameter(Mandatory = $true)][string] $Path)
    return [System.IO.File]::Exists(
        (ConvertTo-ExtendedFileSystemPath -Path $Path))
}

function Get-LongFileLength {
    param([Parameter(Mandatory = $true)][string] $Path)

    $stream = $null
    try {
        $stream = [System.IO.File]::Open(
            (ConvertTo-ExtendedFileSystemPath -Path $Path),
            [System.IO.FileMode]::Open,
            [System.IO.FileAccess]::Read,
            [System.IO.FileShare]::ReadWrite)
        return [long]$stream.Length
    }
    finally {
        if ($null -ne $stream) { $stream.Dispose() }
    }
}

function Get-LongFileSha256 {
    param([Parameter(Mandatory = $true)][string] $Path)

    $stream = $null
    $sha256 = $null
    try {
        $stream = [System.IO.File]::Open(
            (ConvertTo-ExtendedFileSystemPath -Path $Path),
            [System.IO.FileMode]::Open,
            [System.IO.FileAccess]::Read,
            [System.IO.FileShare]::ReadWrite)
        $sha256 = [System.Security.Cryptography.SHA256]::Create()
        return ([System.BitConverter]::ToString(
            $sha256.ComputeHash($stream))).Replace('-', '')
    }
    finally {
        if ($null -ne $sha256) { $sha256.Dispose() }
        if ($null -ne $stream) { $stream.Dispose() }
    }
}

function Get-LongFileBuildVersion {
    param([Parameter(Mandatory = $true)][string] $Path)

    $text = [System.Text.Encoding]::ASCII.GetString(
        [System.IO.File]::ReadAllBytes(
            (ConvertTo-ExtendedFileSystemPath -Path $Path)))
    $buildPattern = [regex]::Escape(
        $pinnedCoopVersion.TrimStart('v')) + '[+-][0-9a-f]{40}'
    $matches = @(
        [regex]::Matches(
            $text,
            $buildPattern,
            [System.Text.RegularExpressions.RegexOptions]::IgnoreCase) |
            ForEach-Object { $_.Value } |
            Select-Object -Unique
    )
    if ($matches.Count -ne 1) {
        throw "Could not resolve one exact Coop build identity from $Path"
    }
    return [string]$matches[0]
}
$requiredPayloadRelative = @(
    'BannerlordCoop.RuntimeBootstrap.dll',
    'BannerlordCoopLauncher.exe',
    'release-info.txt',
    'engine\bin\Win64_Shipping_Server\0Harmony.dll',
    'engine\Modules\Coop\bin\Win64_Shipping_Server\Common.dll',
    'engine\Modules\Coop\bin\Win64_Shipping_Server\Coop.Core.dll',
    'engine\Modules\Coop\bin\Win64_Shipping_Server\Coop.dll',
    'engine\Modules\Coop\bin\Win64_Shipping_Server\Coop.Steam.dll',
    'engine\Modules\Coop\bin\Win64_Shipping_Server\GameInterface.dll',
    'engine\Modules\Coop\bin\Win64_Shipping_Server\Missions.dll',
    'engine\Modules\Coop\mod-config.default.json',
    'engine\Modules\Coop\SubModule.xml',
    'engine\Modules\DedicatedServer.Windows\bin\Win64_Shipping_Server\DedicatedServer.Core.dll',
    'engine\Modules\DedicatedServer.Windows\bin\Win64_Shipping_Server\DedicatedServer.LauncherBattleSize.dll',
    'engine\Modules\DedicatedServer.Windows\bin\Win64_Shipping_Server\DedicatedServer.LauncherCheats.dll',
    'engine\Modules\DedicatedServer.Windows\bin\Win64_Shipping_Server\DedicatedServer.Windows.dll',
    'engine\Modules\DedicatedServer.Windows\SubModule.xml',
    'IMPORTANT - INSTALL.txt',
    'INSTALL-UPDATE.bat',
    'Install-Update.ps1',
    'legacy-cleanup-manifests\legacy-runtime-projections.json',
    'modded-server-modules.txt',
    'modded-server-selection.json',
    'server-module-policy.json',
    'SERVER-CONSOLE.bat',
    'Server-Console.ps1',
    'Start-ModdedServer.cmd',
    'Start-ModdedServer.ps1'
)

foreach ($relativePath in $requiredPayloadRelative) {
    $path = Join-Path $packageRoot $relativePath
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "The update is incomplete. Required file is missing: $path"
    }
}
if (-not (Test-Path -LiteralPath $payloadManifestPath -PathType Leaf)) {
    throw "The update is incomplete. Payload manifest is missing: $payloadManifestPath"
}
if (-not (Test-Path -LiteralPath $legacyCleanupRoot -PathType Container)) {
    throw "The update is incomplete. Legacy cleanup manifests are missing: $legacyCleanupRoot"
}

$payloadManifest = Get-Content -LiteralPath $payloadManifestPath -Raw |
    ConvertFrom-Json
if ([int]$payloadManifest.SchemaVersion -ne 1 -or
    $null -eq $payloadManifest.Files) {
    throw "The update payload manifest is invalid: $payloadManifestPath"
}
$allowedPackageTypes = @('DedicatedServerOnly', 'ServerAndClient')
$packageType = [string]$payloadManifest.PackageType
if ($packageType -cnotin $allowedPackageTypes) {
    throw "Unsupported package type: $packageType"
}
$isServerOnlyPackage = $packageType -ceq 'DedicatedServerOnly'
$packagePayloadSet = New-Object 'System.Collections.Generic.HashSet[string]' `
    ([System.StringComparer]::OrdinalIgnoreCase)
foreach ($packageFile in Get-ChildItem -LiteralPath $packageRoot -Recurse -File -Force) {
    $relativePath = $packageFile.FullName.Substring($packageRoot.Length).TrimStart('\')
    if ($relativePath -ieq 'payload-sha256.json') {
        continue
    }
    [void]$packagePayloadSet.Add($relativePath)
}
$manifestPayloadSet = New-Object 'System.Collections.Generic.HashSet[string]' `
    ([System.StringComparer]::OrdinalIgnoreCase)
$manifestEntryCount = @($payloadManifest.Files).Count
$manifestEntryIndex = 0
Write-Host "Verifying $manifestEntryCount extracted update file(s)..." -ForegroundColor Cyan
foreach ($entry in $payloadManifest.Files) {
    $manifestEntryIndex++
    $relativePath = [string]$entry.Path
    $sha256 = [string]$entry.Sha256
    $length = [long]$entry.Length
    if ([string]::IsNullOrWhiteSpace($relativePath) -or
        [System.IO.Path]::IsPathRooted($relativePath) -or
        $sha256 -notmatch '^[0-9A-Fa-f]{64}$' -or
        $length -lt 0 -or
        -not $manifestPayloadSet.Add($relativePath)) {
        throw "Invalid payload manifest entry: $relativePath"
    }
    $payloadPath = [System.IO.Path]::GetFullPath(
        (Join-Path $packageRoot $relativePath))
    if (-not $payloadPath.StartsWith(
            $packageRoot.TrimEnd('\') + '\',
            [System.StringComparison]::OrdinalIgnoreCase) -or
        -not (Test-LongFile -Path $payloadPath)) {
        throw "Unsafe or missing payload manifest path: $relativePath"
    }
    $actualLength = Get-LongFileLength -Path $payloadPath
    $actualHash = Get-LongFileSha256 -Path $payloadPath
    if ($actualLength -ne $length -or $actualHash -ne $sha256) {
        throw "Payload integrity check failed: $relativePath"
    }
    if (($manifestEntryIndex % 200) -eq 0 -or
        $manifestEntryIndex -eq $manifestEntryCount) {
        Write-Host "  Verified $manifestEntryIndex / $manifestEntryCount package file(s)."
    }
}
if (-not $packagePayloadSet.IsSupersetOf($manifestPayloadSet)) {
    throw 'The extracted update is missing one or more manifest files.'
}

# Windows/Chrome/archive tools and users commonly leave desktop.ini, the RAR,
# screenshots, or notes beside an extracted update. Those unrelated root files
# must not invalidate 974 payload files that have already passed length + SHA-256
# verification. Unmanifested files inside managed payload directories remain a
# hard failure because Copy-DirectoryContents installs the complete engine tree.
$extraPackageFiles = @(
    $packagePayloadSet |
        Where-Object { -not $manifestPayloadSet.Contains($_) }
)
$unsafeExtraFiles = @(
    $extraPackageFiles |
        Where-Object {
            $_.StartsWith('engine\', [System.StringComparison]::OrdinalIgnoreCase) -or
            $_.StartsWith('legacy-cleanup-manifests\', [System.StringComparison]::OrdinalIgnoreCase) -or
            $_.StartsWith('EVERY PLAYER - COOP CLIENT UPDATE\', [System.StringComparison]::OrdinalIgnoreCase)
        }
)
if ($unsafeExtraFiles.Count -gt 0) {
    throw ('Unverified file(s) were found inside the managed update payload: ' +
        ($unsafeExtraFiles -join ', '))
}
if ($extraPackageFiles.Count -gt 0) {
    Write-Host ("Ignoring {0} unrelated extra file(s) beside the verified update payload." -f `
        $extraPackageFiles.Count) -ForegroundColor Yellow
}
$pinnedCoopVersion = 'v0.1.4'
$pinnedCoopModuleId = 'Coop'
$installableCoopModuleIds = @('Coop', 'CoopNightly')
$pinnedCoopHashes = @{
    'Common.dll' = 'D31409D82B93FBB2B5560AA48248EAA3BB93019E3AF9C580F8F383599C0EFDB7'
    'Coop.Core.dll' = '45924C8D5794D39EBF2A2E5E6206394837F25DFBFBD739A84C7163D191EABFB2'
    'Coop.dll' = '7590C896E96040AE586AEFA2E37DC4BDA14EA38121E166E21485F26AA6423510'
    'Coop.Steam.dll' = '6DE91B34DE89A16632E53908FF7AF11E28B15FF624F6E85A7AB777F471E1E97C'
    'GameInterface.dll' = 'FF1736FA6F76C7C1239A148112892C7B7F7FC6510701BFC5588DB377BDA76440'
    'Missions.dll' = 'FA039E5AB1AD80BA51E56F8CB8E469034EEE99CC7F792ECFCD095D56387ADD82'
}

function Get-BaseGameClientAssemblies {
    param([Parameter(Mandatory = $true)][string]$ModuleRoot)

    $binRoot = Join-Path $ModuleRoot 'bin\Win64_Shipping_Client'
    if (-not (Test-Path -LiteralPath $binRoot -PathType Container)) {
        return @()
    }
    return @(
        Get-ChildItem -LiteralPath $binRoot -File -Force |
            Where-Object {
                $_.Name -like 'TaleWorlds.*.dll' -or
                $_.Name -like 'SandBox*.dll'
            }
    )
}

$packagedBaseGameAssemblies = @(
    Get-BaseGameClientAssemblies -ModuleRoot $clientPayload
)
if (-not $isServerOnlyPackage -and $packagedBaseGameAssemblies.Count -gt 0) {
    throw ('The client update must not contain base-game TaleWorlds/SandBox ' +
        'assemblies: ' +
        (($packagedBaseGameAssemblies | Select-Object -ExpandProperty Name) -join ', '))
}

function Assert-PinnedCoopModule {
    param(
        [Parameter(Mandatory = $true)][string]$ModuleRoot,
        [Parameter(Mandatory = $true)][string]$BinFolder,
        [Parameter(Mandatory = $true)][string]$Description
    )

    $subModulePath = Join-Path $ModuleRoot 'SubModule.xml'
    if (-not (Test-Path -LiteralPath $subModulePath -PathType Leaf)) {
        throw "$Description is missing SubModule.xml: $subModulePath"
    }
    [xml]$moduleXml = [System.IO.File]::ReadAllText($subModulePath)
    $moduleId = [string]$moduleXml.Module.Id.value
    $moduleVersion = [string]$moduleXml.Module.Version.value
    if ($moduleId -ine $pinnedCoopModuleId -or
        $moduleVersion -ine $pinnedCoopVersion) {
        throw "$Description must be Coop $pinnedCoopVersion; found '$moduleId' '$moduleVersion'."
    }

    $binRoot = Join-Path $ModuleRoot $BinFolder
    foreach ($fileName in $pinnedCoopHashes.Keys) {
        $assemblyPath = Join-Path $binRoot $fileName
        if (-not (Test-Path -LiteralPath $assemblyPath -PathType Leaf)) {
            throw "$Description is missing ${fileName}: $assemblyPath"
        }
        $actualHash = (Get-LongFileSha256 -Path $assemblyPath)
        if ($actualHash -ine $pinnedCoopHashes[$fileName]) {
            throw "$Description contains the wrong $pinnedCoopVersion ${fileName}: $assemblyPath"
        }
    }
}

Assert-PinnedCoopModule `
    -ModuleRoot (Join-Path $packageRoot 'engine\Modules\Coop') `
    -BinFolder 'bin\Win64_Shipping_Server' `
    -Description 'Packaged server Coop module'
if (-not $isServerOnlyPackage) {
    Assert-PinnedCoopModule `
        -ModuleRoot $clientPayload `
        -BinFolder 'bin\Win64_Shipping_Client' `
        -Description 'Packaged client Coop module'

    $serverBuildVersion = Get-LongFileBuildVersion -Path (
        Join-Path $packageRoot `
            'engine\Modules\Coop\bin\Win64_Shipping_Server\Common.dll')
    $clientBuildVersion = Get-LongFileBuildVersion -Path (
        Join-Path $clientPayload 'bin\Win64_Shipping_Client\Common.dll')
    if ($serverBuildVersion -cne $clientBuildVersion) {
        throw "Packaged server/client Coop build mismatch: $serverBuildVersion != $clientBuildVersion"
    }
}
if ($VerifyPackageOnly) {
    Write-Host 'Package verification passed.' -ForegroundColor Green
    exit 0
}

$steamRoots = New-Object 'System.Collections.Generic.HashSet[string]' `
    ([System.StringComparer]::OrdinalIgnoreCase)
$moduleTargets = New-Object 'System.Collections.Generic.HashSet[string]' `
    ([System.StringComparer]::OrdinalIgnoreCase)
$serverTargets = New-Object 'System.Collections.Generic.HashSet[string]' `
    ([System.StringComparer]::OrdinalIgnoreCase)

function Add-NormalizedPath {
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyCollection()]
        [System.Collections.Generic.HashSet[string]]$Set,
        [string]$Path
    )

    if ([string]::IsNullOrWhiteSpace($Path)) {
        return
    }
    try {
        $fullPath = [System.IO.Path]::GetFullPath(
            [Environment]::ExpandEnvironmentVariables($Path.Trim().Trim('"'))
        ).TrimEnd('\')
        if (Test-Path -LiteralPath $fullPath -PathType Container) {
            [void]$Set.Add($fullPath)
        }
    }
    catch {
        # A stale Steam library entry is safe to ignore.
    }
}

function Get-CoopModuleId {
    param([string]$Path)

    if ([string]::IsNullOrWhiteSpace($Path)) {
        return $null
    }
    $subModulePath = Join-Path $Path 'SubModule.xml'
    if (-not (Test-Path -LiteralPath $subModulePath -PathType Leaf)) {
        return $null
    }
    try {
        [xml]$moduleXml = [System.IO.File]::ReadAllText($subModulePath)
        $moduleId = [string]$moduleXml.Module.Id.value
        if ($moduleId -in $installableCoopModuleIds) {
            return $moduleId
        }
        return $null
    }
    catch {
        return $null
    }
}

function Test-CoopModuleRoot {
    param([string]$Path)

    return $null -ne (Get-CoopModuleId -Path $Path)
}

function Select-CoopModuleTarget {
    param(
        [AllowEmptyCollection()][string[]]$Candidates,
        [string]$ExplicitModulePath
    )

    if (-not [string]::IsNullOrWhiteSpace($ExplicitModulePath)) {
        $explicitPath = [System.IO.Path]::GetFullPath(
            [Environment]::ExpandEnvironmentVariables(
                $ExplicitModulePath.Trim().Trim('"'))).TrimEnd('\')
        if (-not (Test-CoopModuleRoot -Path $explicitPath)) {
            throw "The explicit Coop module target is invalid: $explicitPath"
        }
        return $explicitPath
    }

    $rankedCandidates = @(
        foreach ($candidate in @($Candidates)) {
            $moduleId = Get-CoopModuleId -Path $candidate
            if ($null -eq $moduleId) {
                continue
            }
            [pscustomobject]@{
                Path = [System.IO.Path]::GetFullPath($candidate).TrimEnd('\')
                Priority = if ($moduleId -ieq 'Coop') { 0 } else { 1 }
            }
        }
    )
    $selected = @(
        $rankedCandidates |
            Sort-Object -Property Priority, Path |
            Select-Object -First 1
    )
    if ($selected.Count -eq 0) {
        return $null
    }
    return [string]$selected[0].Path
}

function Test-DedicatedServerRoot {
    param([string]$Path)

    return -not [string]::IsNullOrWhiteSpace($Path) -and
        (Test-Path -LiteralPath (Join-Path $Path 'BannerlordCoopServer.exe') -PathType Leaf) -and
        (Test-Path -LiteralPath (Join-Path $Path 'engine') -PathType Container) -and
        (Test-Path -LiteralPath (Join-Path $Path `
            'engine\bin\Win64_Shipping_Server\TaleWorlds.Starter.DotNetCore.dll') -PathType Leaf)
}

function Assert-ExistingServerRuntime148 {
    param([Parameter(Mandatory = $true)][string]$TargetRoot)

    foreach ($moduleName in @('Native', 'SandBoxCore', 'SandBox')) {
        $descriptorPath = Join-Path $TargetRoot `
            "engine\Modules\$moduleName\SubModule.xml"
        if (-not (Test-Path -LiteralPath $descriptorPath -PathType Leaf)) {
            throw "The existing server is missing $moduleName. Install/update the Coop dedicated server first: $descriptorPath"
        }
        [xml]$descriptor = [System.IO.File]::ReadAllText($descriptorPath)
        $version = ([string]$descriptor.Module.Version.value).Trim()
        if ($version -ine 'v1.4.8') {
            throw "This updater requires Bannerlord v1.4.8; $moduleName is $version in $TargetRoot"
        }
    }

    foreach ($requiredPath in @(
            'engine\bin\Win64_Shipping_Server\TaleWorlds.Starter.DotNetCore.dll',
            'engine\bin\Win64_Shipping_Server\TaleWorlds.CampaignSystem.dll',
            'engine\Modules\Coop\SubModule.xml'
        )) {
        $fullPath = Join-Path $TargetRoot $requiredPath
        if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) {
            throw "The existing Coop 1.4.8 server is incomplete: $fullPath"
        }
    }
}
function Add-ModuleTarget {
    param([string]$Path)

    if (Test-CoopModuleRoot -Path $Path) {
        if (-not $isServerOnlyPackage) {
            Add-NormalizedPath -Set $moduleTargets -Path $Path
        }
        $dedicatedServer = Join-Path $Path 'DedicatedServer'
        if (Test-DedicatedServerRoot -Path $dedicatedServer) {
            Add-NormalizedPath -Set $serverTargets -Path $dedicatedServer
        }
    }
}

function Add-ManualTarget {
    param([string]$Path)

    if ([string]::IsNullOrWhiteSpace($Path)) {
        return
    }
    $candidate = $Path.Trim().Trim('"')
    if (Test-DedicatedServerRoot -Path $candidate) {
        Add-NormalizedPath -Set $serverTargets -Path $candidate
        $parent = Split-Path -Parent ([System.IO.Path]::GetFullPath($candidate))
        Add-ModuleTarget -Path $parent
        return
    }
    if (Test-CoopModuleRoot -Path $candidate) {
        Add-ModuleTarget -Path $candidate
        return
    }
    throw "Not a valid Coop module or DedicatedServer folder: $candidate"
}

function Add-SteamRoot {
    param([string]$Path)
    Add-NormalizedPath -Set $steamRoots -Path $Path
}

# Preferred install workflow: the complete extracted update folder is moved
# inside DedicatedServer and the BAT is run there. Resolve the nearest valid
# containing server before broad Steam/Workshop discovery. A wrapper folder
# produced by an archive tool is supported as well.
$containingServerTarget = $null
$packageAncestor = [System.IO.DirectoryInfo]::new($packageRoot).Parent
while ($null -ne $packageAncestor) {
    if (Test-DedicatedServerRoot -Path $packageAncestor.FullName) {
        $containingServerTarget = $packageAncestor.FullName
        Add-NormalizedPath -Set $serverTargets -Path $containingServerTarget
        break
    }
    $packageAncestor = $packageAncestor.Parent
}

Add-SteamRoot -Path 'C:\Program Files (x86)\Steam'
Add-SteamRoot -Path 'C:\Program Files\Steam'

foreach ($registryLocation in @(
    @{ Path = 'HKCU:\Software\Valve\Steam'; Name = 'SteamPath' },
    @{ Path = 'HKLM:\SOFTWARE\WOW6432Node\Valve\Steam'; Name = 'InstallPath' },
    @{ Path = 'HKLM:\SOFTWARE\Valve\Steam'; Name = 'InstallPath' }
)) {
    try {
        $value = Get-ItemPropertyValue -LiteralPath $registryLocation.Path `
            -Name $registryLocation.Name -ErrorAction Stop
        Add-SteamRoot -Path ([string]$value)
    }
    catch {
        # Registry location is optional.
    }
}

$ancestor = New-Object System.IO.DirectoryInfo($packageRoot)
while ($null -ne $ancestor) {
    if ([string]::Equals($ancestor.Name, 'steamapps',
            [System.StringComparison]::OrdinalIgnoreCase) -and
        $null -ne $ancestor.Parent) {
        Add-SteamRoot -Path $ancestor.Parent.FullName
        break
    }
    $ancestor = $ancestor.Parent
}

# Steam stores extra library roots in either of these VDF locations depending
# on the client version. Parse every root already known, then scan the additions.
$rootsToInspect = @($steamRoots)
for ($rootIndex = 0; $rootIndex -lt $rootsToInspect.Count; $rootIndex++) {
    $steamRoot = $rootsToInspect[$rootIndex]
    foreach ($vdfPath in @(
        (Join-Path $steamRoot 'steamapps\libraryfolders.vdf'),
        (Join-Path $steamRoot 'config\libraryfolders.vdf')
    )) {
        if (-not (Test-Path -LiteralPath $vdfPath -PathType Leaf)) {
            continue
        }
        $vdfText = [System.IO.File]::ReadAllText($vdfPath)
        foreach ($match in [regex]::Matches(
                $vdfText,
                '"path"\s*"(?<path>[^"]+)"',
                [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)) {
            $libraryPath = $match.Groups['path'].Value.Replace('\\', '\')
            $oldCount = $steamRoots.Count
            Add-SteamRoot -Path $libraryPath
            if ($steamRoots.Count -gt $oldCount) {
                $rootsToInspect = @($steamRoots)
            }
        }
    }
}

if ($SkipAutoDetect) {
    $steamRoots.Clear()
}

foreach ($steamRoot in @($steamRoots)) {
    $workshopModule = Join-Path $steamRoot `
        'steamapps\workshop\content\261550\3770450698'
    Add-ModuleTarget -Path $workshopModule

    $modulesRoot = Join-Path $steamRoot `
        'steamapps\common\Mount & Blade II Bannerlord\Modules'
    if (Test-Path -LiteralPath $modulesRoot -PathType Container) {
        foreach ($directory in Get-ChildItem -LiteralPath $modulesRoot `
                -Directory -ErrorAction SilentlyContinue) {
            Add-ModuleTarget -Path $directory.FullName
        }
    }
}

Add-ManualTarget -Path $ServerPath
Add-ManualTarget -Path $ModulePath

if ($isServerOnlyPackage) {
    $moduleTargets.Clear()
}
else {
    # Install into exactly one client module. An explicit -ModulePath wins;
    # otherwise prefer an existing stable Coop module over CoopNightly, then use
    # a deterministic path order. Updating every discovery result can otherwise
    # leave both a local and Workshop folder declaring the normalized Coop ID.
    $selectedModuleTarget = Select-CoopModuleTarget `
        -Candidates @($moduleTargets) `
        -ExplicitModulePath $ModulePath
    $moduleTargets.Clear()
    if ($null -ne $selectedModuleTarget) {
        Add-NormalizedPath -Set $moduleTargets -Path $selectedModuleTarget
    }
}

# An explicitly supplied target (including the BAT's containing-folder target)
# or a containing DedicatedServer always wins. Do not silently update every
# other Steam/Workshop server that happens to exist on the same machine.
$preferredServerTarget = $null
if (-not [string]::IsNullOrWhiteSpace($ServerPath)) {
    $preferredServerTarget = [System.IO.Path]::GetFullPath(
        [Environment]::ExpandEnvironmentVariables(
            $ServerPath.Trim().Trim('"'))).TrimEnd('\')
}
elseif (-not [string]::IsNullOrWhiteSpace($containingServerTarget)) {
    $preferredServerTarget = [System.IO.Path]::GetFullPath(
        $containingServerTarget).TrimEnd('\')
}
if ($null -ne $preferredServerTarget) {
    if (-not (Test-DedicatedServerRoot -Path $preferredServerTarget)) {
        throw "The preferred DedicatedServer target is invalid: $preferredServerTarget"
    }
    $serverTargets.Clear()
    Add-NormalizedPath -Set $serverTargets -Path $preferredServerTarget
}

if (-not $DryRun -and $serverTargets.Count -eq 0 -and
        [string]::IsNullOrWhiteSpace($ModulePath)) {
    Write-Host
    Write-Host 'No DedicatedServer installation was found automatically.' -ForegroundColor Yellow
    $manualServer = Read-Host 'Paste the DedicatedServer folder path'
    Add-ManualTarget -Path $manualServer
}

if (-not $isServerOnlyPackage -and -not $DryRun -and
        $moduleTargets.Count -eq 0 -and
        [string]::IsNullOrWhiteSpace($ServerPath)) {
    Write-Host
    Write-Host 'No Coop client module was found automatically.' -ForegroundColor Yellow
    $manualModule = Read-Host 'Paste the Coop module folder path'
    Add-ManualTarget -Path $manualModule
}

Write-Host
Write-Host 'Detected DedicatedServer installation(s):' -ForegroundColor Cyan
if ($serverTargets.Count -eq 0) {
    Write-Host '  None (client-only update)'
}
else {
    foreach ($target in @($serverTargets)) {
        Write-Host "  $target"
    }
}

if (-not $isServerOnlyPackage) {
    Write-Host
    Write-Host 'Detected Coop client module installation(s):' -ForegroundColor Cyan
    if ($moduleTargets.Count -eq 0) {
        Write-Host '  None'
    }
    else {
        foreach ($target in @($moduleTargets)) {
            $kind = if ($target -match '[\\/]workshop[\\/]content[\\/]261550[\\/]3770450698$') {
                'Workshop'
            }
            else {
                'Bannerlord Modules folder'
            }
            Write-Host "  [$kind] $target"
        }
    }
}

if ($DryRun) {
    Write-Host
    Write-Host 'Dry run complete. No files were changed.' -ForegroundColor Green
    exit 0
}

$runningProcesses = @(
    Get-Process -ErrorAction SilentlyContinue |
        Where-Object {
            $_.ProcessName -in @(
                'Bannerlord',
                'Bannerlord.Native',
                'BannerlordCoopLauncher',
                'BannerlordCoopServer'
            )
        }
)
if (-not $IgnoreRunningProcesses -and $runningProcesses.Count -gt 0) {
    $names = @($runningProcesses | Select-Object -ExpandProperty ProcessName -Unique)
    throw "Close these programs before updating: $($names -join ', ')"
}

function Copy-DirectoryContents {
    param(
        [Parameter(Mandatory = $true)][string]$Source,
        [Parameter(Mandatory = $true)][string]$Destination
    )

    if (-not (Test-Path -LiteralPath $Source -PathType Container)) {
        throw "Update source folder is missing: $Source"
    }
    $sourceFullPath = [System.IO.Path]::GetFullPath($Source).TrimEnd('\')
    $destinationFullPath = [System.IO.Path]::GetFullPath($Destination).TrimEnd('\')
    if ($destinationFullPath.StartsWith(
            $sourceFullPath + '\',
            [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing recursive update copy from '$Source' into '$Destination'."
    }
    if (-not (Test-Path -LiteralPath $destinationFullPath -PathType Container)) {
        [void](New-Item -ItemType Directory -Path $destinationFullPath -Force)
    }

    $sourceFiles = @(Get-ChildItem -LiteralPath $sourceFullPath `
        -Recurse -File -Force)
    $totalFiles = $sourceFiles.Count
    Write-Host "  Scanning $totalFiles update file(s)..."
    $copied = 0
    $unchanged = 0
    $processed = 0
    foreach ($sourceFile in $sourceFiles) {
        $processed++
        $relativePath = $sourceFile.FullName.Substring(
            $sourceFullPath.Length).TrimStart('\')
        $destinationPath = Join-Path $destinationFullPath $relativePath
        if (Test-FilesMatch -Source $sourceFile.FullName `
                -Destination $destinationPath) {
            $unchanged++
        }
        else {
            Copy-FileWithProgress -Source $sourceFile.FullName `
                -Destination $destinationPath -Operation 'Installing update'
            $copied++
        }
        if (($processed % 100) -eq 0 -or $processed -eq $totalFiles) {
            Write-Host "  Processed $processed / $totalFiles file(s)..."
        }
    }
    Write-Host "  Installed $copied changed file(s); skipped $unchanged unchanged file(s)." `
        -ForegroundColor Green
}

function Sync-PackageOwnedManagedFiles {
    param(
        [Parameter(Mandatory = $true)][string]$SourceEngineRoot,
        [Parameter(Mandatory = $true)][string]$TargetEngineRoot,
        [string]$BackupRoot,
        [switch]$VerifyOnly
    )

    $scopes = @(
        @{
            RelativePath = 'Modules\Coop'
            Filter = '*'
            Recursive = $true
        },
        @{
            RelativePath = 'Modules\DedicatedServer.Windows\bin\Win64_Shipping_Server'
            Filter = '*.dll'
            Recursive = $false
        }
    )
    $staleFiles = [System.Collections.Generic.List[object]]::new()
    foreach ($scope in $scopes) {
        $sourceScope = [System.IO.Path]::GetFullPath(
            (Join-Path $SourceEngineRoot $scope.RelativePath)).TrimEnd('\')
        $targetScope = [System.IO.Path]::GetFullPath(
            (Join-Path $TargetEngineRoot $scope.RelativePath)).TrimEnd('\')
        if (-not (Test-Path -LiteralPath $sourceScope -PathType Container)) {
            throw "Package-owned source directory is missing: $sourceScope"
        }
        if (-not (Test-Path -LiteralPath $targetScope -PathType Container)) {
            continue
        }

        $sourceSet = New-Object 'System.Collections.Generic.HashSet[string]' `
            ([System.StringComparer]::OrdinalIgnoreCase)
        foreach ($sourceFile in Get-ChildItem -LiteralPath $sourceScope `
                     -Filter $scope.Filter -File -Force `
                     -Recurse:([bool]$scope.Recursive)) {
            [void]$sourceSet.Add(
                $sourceFile.FullName.Substring($sourceScope.Length).TrimStart('\'))
        }
        foreach ($targetFile in Get-ChildItem -LiteralPath $targetScope `
                     -Filter $scope.Filter -File -Force `
                     -Recurse:([bool]$scope.Recursive)) {
            $relativeInScope =
                $targetFile.FullName.Substring($targetScope.Length).TrimStart('\')
            if (-not $sourceSet.Contains($relativeInScope)) {
                $relativeInEngine =
                    $targetFile.FullName.Substring(
                        [System.IO.Path]::GetFullPath($TargetEngineRoot).TrimEnd('\').Length).
                        TrimStart('\')
                $staleFiles.Add([pscustomobject]@{
                    FullName = $targetFile.FullName
                    RelativePath = $relativeInEngine
                })
            }
        }
    }

    if ($staleFiles.Count -eq 0) {
        return
    }
    if ($VerifyOnly) {
        throw ('Stale package-owned managed file(s) remain after installation: ' +
            (($staleFiles | ForEach-Object RelativePath) -join ', '))
    }
    if ([string]::IsNullOrWhiteSpace($BackupRoot)) {
        throw 'A backup root is required before stale managed files can be removed.'
    }
    foreach ($staleFile in $staleFiles) {
        Copy-FileWithProgress -Source $staleFile.FullName `
            -Destination (Join-Path $BackupRoot $staleFile.RelativePath) `
            -Operation 'Backing up stale package-owned file'
        [System.IO.File]::Delete(
            (ConvertTo-ExtendedFileSystemPath -Path $staleFile.FullName))
    }
    Write-Host "  Removed $($staleFiles.Count) stale package-owned managed file(s)." `
        -ForegroundColor Green
}

function Test-FilesMatch {
    param(
        [Parameter(Mandatory = $true)][string]$Source,
        [Parameter(Mandatory = $true)][string]$Destination
    )

    if (-not (Test-LongFile -Path $Destination)) {
        return $false
    }
    if ((Get-LongFileLength -Path $Source) -ne
        (Get-LongFileLength -Path $Destination)) {
        return $false
    }
    return (Get-LongFileSha256 -Path $Source) -eq
        (Get-LongFileSha256 -Path $Destination)
}

function Copy-FileWithProgress {
    param(
        [Parameter(Mandatory = $true)][string]$Source,
        [Parameter(Mandatory = $true)][string]$Destination,
        [Parameter(Mandatory = $true)][string]$Operation
    )

    $sourceFullPath = [System.IO.Path]::GetFullPath($Source)
    $destinationFullPath = [System.IO.Path]::GetFullPath($Destination)
    if ([string]::Equals($sourceFullPath, $destinationFullPath,
            [System.StringComparison]::OrdinalIgnoreCase)) {
        return
    }

    $destinationDirectory = Split-Path -Parent $destinationFullPath
    [void][System.IO.Directory]::CreateDirectory(
        (ConvertTo-ExtendedFileSystemPath -Path $destinationDirectory))
    $temporaryPath = $destinationFullPath + ".coop-update-$PID.tmp"
    if (Test-LongFile -Path $temporaryPath) {
        [System.IO.File]::Delete(
            (ConvertTo-ExtendedFileSystemPath -Path $temporaryPath))
    }

    $totalBytes = Get-LongFileLength -Path $sourceFullPath
    $displayName = [System.IO.Path]::GetFileName($sourceFullPath)
    Write-Host ("  {0}: {1} ({2:N1} MB)" -f `
        $Operation, $displayName, ($totalBytes / 1MB))

    $sourceStream = $null
    $destinationStream = $null
    try {
        $sourceStream = [System.IO.File]::Open(
            (ConvertTo-ExtendedFileSystemPath -Path $sourceFullPath),
            [System.IO.FileMode]::Open,
            [System.IO.FileAccess]::Read,
            [System.IO.FileShare]::ReadWrite)
        $destinationStream = [System.IO.File]::Open(
            (ConvertTo-ExtendedFileSystemPath -Path $temporaryPath),
            [System.IO.FileMode]::CreateNew,
            [System.IO.FileAccess]::Write,
            [System.IO.FileShare]::None)
        $buffer = New-Object byte[] (1MB)
        $copiedBytes = [long]0
        while (($read = $sourceStream.Read($buffer, 0, $buffer.Length)) -gt 0) {
            $destinationStream.Write($buffer, 0, $read)
            $copiedBytes += $read
            $percent = if ($totalBytes -eq 0) {
                100
            }
            else {
                [Math]::Min(100, [int](($copiedBytes * 100) / $totalBytes))
            }
            Write-Progress -Id 41 -Activity $Operation `
                -Status ("{0}: {1:N1} / {2:N1} MB" -f `
                    $displayName, ($copiedBytes / 1MB), ($totalBytes / 1MB)) `
                -PercentComplete $percent
        }
        $destinationStream.Flush($true)
        $destinationStream.Dispose()
        $destinationStream = $null
        $sourceStream.Dispose()
        $sourceStream = $null

        if ((Get-LongFileSha256 -Path $sourceFullPath) -ne
            (Get-LongFileSha256 -Path $temporaryPath)) {
            throw "Copy verification failed: $destinationFullPath"
        }
        $extendedTemporary =
            ConvertTo-ExtendedFileSystemPath -Path $temporaryPath
        $extendedDestination =
            ConvertTo-ExtendedFileSystemPath -Path $destinationFullPath
        # File.Replace rejects extended-length paths on Windows PowerShell's
        # .NET Framework runtime. File.Copy supports them and can safely
        # overwrite after the temporary copy has passed its SHA-256 check.
        [System.IO.File]::Copy(
            $extendedTemporary, $extendedDestination, $true)
        [System.IO.File]::Delete($extendedTemporary)
        Write-Progress -Id 41 -Activity $Operation -Completed
    }
    finally {
        if ($null -ne $destinationStream) {
            $destinationStream.Dispose()
        }
        if ($null -ne $sourceStream) {
            $sourceStream.Dispose()
        }
        if (Test-LongFile -Path $temporaryPath) {
            try {
                [System.IO.File]::Delete(
                    (ConvertTo-ExtendedFileSystemPath -Path $temporaryPath))
            }
            catch {
            }
        }
        Write-Progress -Id 41 -Activity $Operation -Completed
    }
}

function Backup-PayloadFiles {
    param(
        [Parameter(Mandatory = $true)][string]$SourceRoot,
        [Parameter(Mandatory = $true)][string]$DestinationRoot,
        [Parameter(Mandatory = $true)][string]$BackupRoot
    )

    $sourceFullPath = [System.IO.Path]::GetFullPath($SourceRoot).TrimEnd('\')
    foreach ($sourceFile in Get-ChildItem -LiteralPath $sourceFullPath `
            -Recurse -File -Force) {
        $relativePath = $sourceFile.FullName.Substring(
            $sourceFullPath.Length).TrimStart('\')
        $destinationPath = Join-Path $DestinationRoot $relativePath
        if (-not (Test-Path -LiteralPath $destinationPath -PathType Leaf)) {
            continue
        }

        if (Test-FilesMatch -Source $sourceFile.FullName `
                -Destination $destinationPath) {
            continue
        }

        $backupPath = Join-Path $BackupRoot $relativePath
        [void](New-Item -ItemType Directory `
            -Path (Split-Path -Parent $backupPath) -Force)
        Copy-FileWithProgress -Source $destinationPath `
            -Destination $backupPath -Operation 'Backing up changed file'
    }
}

function Assert-PayloadMatches {
    param(
        [Parameter(Mandatory = $true)][string]$SourceRoot,
        [Parameter(Mandatory = $true)][string]$DestinationRoot,
        [Parameter(Mandatory = $true)][string]$Description
    )

    $sourceFullPath = [System.IO.Path]::GetFullPath($SourceRoot).TrimEnd('\')
    $verified = 0
    foreach ($sourceFile in Get-ChildItem -LiteralPath $sourceFullPath `
            -Recurse -File -Force) {
        $relativePath = $sourceFile.FullName.Substring(
            $sourceFullPath.Length).TrimStart('\')
        $destinationPath = Join-Path $DestinationRoot $relativePath
        if (-not (Test-LongFile -Path $destinationPath)) {
            throw "$Description verification failed; missing file: $destinationPath"
        }

        $sourceHash = Get-LongFileSha256 -Path $sourceFile.FullName
        $destinationHash = Get-LongFileSha256 -Path $destinationPath
        if ($sourceHash -ine $destinationHash) {
            throw "$Description verification failed; hash mismatch: $destinationPath"
        }
        $verified++
    }
    Write-Host "  Verified $verified $Description file(s)." -ForegroundColor Green
}

function Get-ClientBackupRoot {
    param(
        [Parameter(Mandatory = $true)][string]$Target,
        [Parameter(Mandatory = $true)][string]$Timestamp
    )

    $sha256 = [System.Security.Cryptography.SHA256]::Create()
    try {
        $bytes = [System.Text.Encoding]::UTF8.GetBytes(
            [System.IO.Path]::GetFullPath($Target).ToUpperInvariant())
        $pathId = ([System.BitConverter]::ToString(
            $sha256.ComputeHash($bytes))).Replace('-', '').Substring(0, 12)
    }
    finally {
        $sha256.Dispose()
    }
    return (Join-Path $env:LOCALAPPDATA `
        "BannerlordCoopLauncher\UpdateBackups\$Timestamp\$pathId")
}

function Backup-AndRemoveRetiredFile {
    param(
        [Parameter(Mandatory = $true)][string]$TargetRoot,
        [Parameter(Mandatory = $true)][string]$RelativePath,
        [Parameter(Mandatory = $true)][string]$BackupRoot
    )

    $retiredPath = Join-Path $TargetRoot $RelativePath
    if (-not (Test-Path -LiteralPath $retiredPath -PathType Leaf)) {
        return $false
    }
    $backupPath = Join-Path $BackupRoot ('retired-addons\' + $RelativePath)
    # These generated AutoSync paths can exceed MAX_PATH once they are nested
    # beneath the timestamped backup directory. Use the installer's verified
    # extended-path copy routine instead of Copy-Item, then remove the original
    # with the same extended-length path support.
    Copy-FileWithProgress `
        -Source $retiredPath `
        -Destination $backupPath `
        -Operation 'Backing up retired file'
    [System.IO.File]::Delete(
        (ConvertTo-ExtendedFileSystemPath -Path $retiredPath))
    return $true
}

function Backup-AndRemoveBaseGameClientAssemblies {
    param(
        [Parameter(Mandatory = $true)][string]$TargetRoot,
        [Parameter(Mandatory = $true)][string]$BackupRoot
    )

    $removed = 0
    foreach ($assembly in @(Get-BaseGameClientAssemblies -ModuleRoot $TargetRoot)) {
        $relativePath = 'bin\Win64_Shipping_Client\' + $assembly.Name
        $backupPath = Join-Path $BackupRoot `
            ('retired-v019-stock-runtime\' + $relativePath)
        Copy-FileWithProgress `
            -Source $assembly.FullName `
            -Destination $backupPath `
            -Operation 'Backing up V0.2.19 stock runtime copy'
        [System.IO.File]::Delete(
            (ConvertTo-ExtendedFileSystemPath -Path $assembly.FullName))
        $removed++
    }
    return $removed
}

function Backup-AndRemoveRetiredDirectory {
    param(
        [Parameter(Mandatory = $true)][string]$TargetRoot,
        [Parameter(Mandatory = $true)][string]$RelativePath,
        [Parameter(Mandatory = $true)][string]$BackupRoot
    )

    $targetFullPath = [System.IO.Path]::GetFullPath(
        (Join-Path $TargetRoot $RelativePath))
    $allowedRoot = [System.IO.Path]::GetFullPath(
        (Join-Path $TargetRoot 'engine\Modules')).TrimEnd('\') + '\'
    if (-not $targetFullPath.StartsWith(
            $allowedRoot,
            [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing retired-module cleanup outside engine\Modules: $targetFullPath"
    }
    if (-not (Test-Path -LiteralPath $targetFullPath -PathType Container)) {
        return $false
    }

    $backupPath = Join-Path $BackupRoot ('retired-addons\' + $RelativePath)
    [void](New-Item -ItemType Directory `
        -Path (Split-Path -Parent $backupPath) -Force)
    Copy-Item -LiteralPath $targetFullPath -Destination $backupPath -Recurse
    Remove-Item -LiteralPath $targetFullPath -Recurse -Force
    return $true
}

function Remove-ObsoleteClientModuleViews {
    param(
        [Parameter(Mandatory = $true)][string]$TargetRoot,
        [Parameter(Mandatory = $true)][string]$BackupRoot
    )

    $manifestPath = Join-Path $TargetRoot `
        'server-data\client-module-server-views.json'
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
        return 0
    }

    $removed = 0
    $manifestFile = Get-Item -LiteralPath $manifestPath
    if ($manifestFile.Length -gt 0) {
        try {
            # Assign directly: Windows PowerShell 5.1 can otherwise wrap a
            # top-level JSON array in an additional Object[] layer.
            $records = Get-Content -LiteralPath $manifestPath -Raw |
                ConvertFrom-Json
            $allowedRoot = [System.IO.Path]::GetFullPath(
                (Join-Path $TargetRoot 'engine\Modules')).TrimEnd('\') + '\'
            foreach ($record in $records) {
                $serverView = [string]$record.ServerView
                $clientBin = [string]$record.ClientBin
                if ([string]::IsNullOrWhiteSpace($serverView) -or
                    [string]::IsNullOrWhiteSpace($clientBin)) {
                    continue
                }
                $viewPath = [System.IO.Path]::GetFullPath($serverView)
                if (-not $viewPath.StartsWith(
                        $allowedRoot,
                        [System.StringComparison]::OrdinalIgnoreCase) -or
                    -not (Test-Path -LiteralPath $viewPath)) {
                    continue
                }
                $viewItem = Get-Item -LiteralPath $viewPath -Force
                if (($viewItem.Attributes -band
                        [System.IO.FileAttributes]::ReparsePoint) -eq 0) {
                    continue
                }
                $linkTarget = [string](@($viewItem.Target)[0])
                if ([string]::IsNullOrWhiteSpace($linkTarget)) {
                    continue
                }
                if (-not [System.IO.Path]::IsPathRooted($linkTarget)) {
                    $linkTarget = Join-Path $viewItem.Parent.FullName $linkTarget
                }
                $resolvedLinkTarget = [System.IO.Path]::GetFullPath($linkTarget)
                $recordedClientBin = [System.IO.Path]::GetFullPath($clientBin)
                if (-not [string]::Equals(
                        $resolvedLinkTarget.TrimEnd('\'),
                        $recordedClientBin.TrimEnd('\'),
                        [System.StringComparison]::OrdinalIgnoreCase)) {
                    continue
                }

                # Directory.Delete removes the junction itself, never its target.
                [System.IO.Directory]::Delete($viewPath, $false)
                $removed++
            }
        }
        catch {
            Write-Warning "Could not parse the obsolete client-view manifest; its junctions were left untouched: $($_.Exception.Message)"
        }
    }

    $manifestBackup = Join-Path $BackupRoot `
        'retired-runtime\client-module-server-views.json'
    [void](New-Item -ItemType Directory `
        -Path (Split-Path -Parent $manifestBackup) -Force)
    Copy-Item -LiteralPath $manifestPath -Destination $manifestBackup -Force
    Remove-Item -LiteralPath $manifestPath -Force
    return $removed
}

function Remove-ObsoleteBaseMapJunction {
    param(
        [Parameter(Mandatory = $true)][string]$TargetRoot,
        [Parameter(Mandatory = $true)][string]$BackupRoot
    )

    $mapPath = Join-Path $TargetRoot `
        'engine\Modules\SandBox\SceneObj\Main_map'
    if (-not (Test-Path -LiteralPath $mapPath)) {
        return $false
    }
    $mapItem = Get-Item -LiteralPath $mapPath -Force
    if (($mapItem.Attributes -band
            [System.IO.FileAttributes]::ReparsePoint) -eq 0) {
        # A physical directory may be part of a real Bannerlord installation.
        # It is deliberately never removed by this updater.
        return $false
    }

    $recordPath = Join-Path $BackupRoot `
        'retired-runtime\removed-main-map-junction.txt'
    [void](New-Item -ItemType Directory `
        -Path (Split-Path -Parent $recordPath) -Force)
    $linkTarget = [string](@($mapItem.Target)[0])
    [System.IO.File]::WriteAllText(
        $recordPath,
        "Junction: $mapPath`r`nTarget: $linkTarget`r`n",
        [System.Text.UTF8Encoding]::new($false))

    # Remove only the reparse point. The linked client map is not traversed.
    [System.IO.Directory]::Delete($mapPath, $false)
    return $true
}

$timestamp = Get-Date -Format 'yyyyMMdd-HHmmss'
foreach ($target in @($serverTargets)) {
    if (-not (Test-DedicatedServerRoot -Path $target)) {
        throw "Server validation failed immediately before copy: $target"
    }
    Assert-ExistingServerRuntime148 -TargetRoot $target
    Write-Host
    Write-Host "Updating server: $target" -ForegroundColor Cyan

    $packageAlreadyInServer = [string]::Equals(
        [System.IO.Path]::GetFullPath($target).TrimEnd('\'),
        $packageRoot.TrimEnd('\'),
        [System.StringComparison]::OrdinalIgnoreCase
    )
    $backupRoot = $null
    if ($packageAlreadyInServer) {
        Write-Host '  Update files are already extracted directly into this server.'
    }
    else {
        $backupRoot = Join-Path $target `
            "server-data\launcher-backups\$timestamp-before-automatic-update"
        [void](New-Item -ItemType Directory -Path $backupRoot -Force)
        Write-Host '  Checking current files and backing up changed copies...'
        foreach ($relativePath in @(
            'BannerlordCoopLauncher.exe',
            'BannerlordCoop.RuntimeBootstrap.dll',
            'release-info.txt',
            'SERVER-CONSOLE.bat',
            'Server-Console.ps1',
            'Start-ModdedServer.cmd',
            'Start-ModdedServer.ps1'
        )) {
            $existingPath = Join-Path $target $relativePath
            $sourcePath = Join-Path $packageRoot $relativePath
            if ((Test-Path -LiteralPath $existingPath -PathType Leaf) -and
                -not (Test-FilesMatch -Source $sourcePath `
                    -Destination $existingPath)) {
                Copy-FileWithProgress -Source $existingPath `
                    -Destination (Join-Path $backupRoot $relativePath) `
                    -Operation 'Backing up changed file'
            }
        }

        Backup-PayloadFiles `
            -SourceRoot (Join-Path $packageRoot 'engine') `
            -DestinationRoot (Join-Path $target 'engine') `
            -BackupRoot (Join-Path $backupRoot 'engine')
        Sync-PackageOwnedManagedFiles `
            -SourceEngineRoot (Join-Path $packageRoot 'engine') `
            -TargetEngineRoot (Join-Path $target 'engine') `
            -BackupRoot (Join-Path $backupRoot 'engine')
        Write-Host '  Installing changed launcher and server files...'
        foreach ($fileName in @(
            'BannerlordCoopLauncher.exe',
            'BannerlordCoop.RuntimeBootstrap.dll',
            'release-info.txt',
            'SERVER-CONSOLE.bat',
            'Server-Console.ps1',
            'Start-ModdedServer.cmd',
            'Start-ModdedServer.ps1',
            'Install-Update.ps1',
            'INSTALL-UPDATE.bat',
            'IMPORTANT - INSTALL.txt'
        )) {
            $sourcePath = Join-Path $packageRoot $fileName
            $destinationPath = Join-Path $target $fileName
            if (-not (Test-FilesMatch -Source $sourcePath `
                    -Destination $destinationPath)) {
                Copy-FileWithProgress -Source $sourcePath `
                    -Destination $destinationPath -Operation 'Installing update'
            }
        }
        Copy-DirectoryContents -Source (Join-Path $packageRoot 'engine') `
            -Destination (Join-Path $target 'engine')

        foreach ($defaultFile in @(
            'modded-server-modules.txt',
            'modded-server-selection.json',
            'server-module-policy.json'
        )) {
            $destination = Join-Path $target $defaultFile
            if (-not (Test-Path -LiteralPath $destination -PathType Leaf)) {
                Copy-Item -LiteralPath (Join-Path $packageRoot $defaultFile) `
                    -Destination $destination
            }
        }
    }

    # Several retired builds projected module/client assemblies into the
    # central server resolver. Those copies can override the freshly installed
    # module DLLs and make two otherwise identical PCs load different code.
    # Clean every projection we shipped, even when its original tracking
    # manifest is absent or the update was extracted directly into the server.
    $centralBin = [System.IO.Path]::GetFullPath(
        (Join-Path $target 'engine\bin\Win64_Shipping_Server'))
    $cleanupBackupRoot = $backupRoot
    if ($null -eq $cleanupBackupRoot) {
        $cleanupBackupRoot = Join-Path $target `
            "server-data\launcher-backups\$timestamp-before-runtime-cleanup"
    }
    $removedLegacyRuntime = 0
    $alwaysObsoleteCentralNames = @(
        '0Harmony-probe-copy.dll',
        'Bannerlord.Harmony.dll',
        'Bannerlord.Harmony.Server.dll',
        'Common.dll',
        'Coop.Core.dll',
        'Coop.dll',
        'Coop.Steam.dll',
        'Coop.TimePacingCompatibility.dll',
        'CustomSpawns.dll',
        'DedicatedServer.LauncherCheats.dll',
        'DedicatedServer.Windows.dll',
        'GameInterface.dll',
        'ItemCategoryAddons.dll',
        'MCMv5.dll',
        'Missions.dll',
        'TaleWorlds.MountAndBlade.CustomBattle.dll'
    )

    $legacyHashKeys = New-Object 'System.Collections.Generic.HashSet[string]' `
        ([System.StringComparer]::OrdinalIgnoreCase)
    $legacyNames = New-Object 'System.Collections.Generic.HashSet[string]' `
        ([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($cleanupManifest in Get-ChildItem -LiteralPath $legacyCleanupRoot `
            -Filter '*.json' -File) {
        # Windows PowerShell 5.1 returns a top-level JSON array as one Object[]
        # pipeline item. Assign it directly so foreach enumerates its entries;
        # wrapping the pipeline in @() incorrectly creates a nested array there.
        $entries = Get-Content -LiteralPath $cleanupManifest.FullName -Raw |
            ConvertFrom-Json
        foreach ($entry in $entries) {
            $fileName = [string]$entry.fileName
            $sha256 = [string]$entry.sha256
            if ([string]::IsNullOrWhiteSpace($fileName) -or
                $fileName -ne [System.IO.Path]::GetFileName($fileName) -or
                $sha256 -notmatch '^[0-9A-Fa-f]{64}$') {
                throw "Invalid legacy cleanup entry in $($cleanupManifest.FullName)"
            }
            [void]$legacyNames.Add($fileName)
            [void]$legacyHashKeys.Add($fileName + '|' + $sha256)
        }
    }

    $centralCandidates = New-Object 'System.Collections.Generic.HashSet[string]' `
        ([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($fileName in $alwaysObsoleteCentralNames) {
        [void]$centralCandidates.Add($fileName)
    }
    foreach ($fileName in $legacyNames) {
        [void]$centralCandidates.Add($fileName)
    }

    foreach ($fileName in $centralCandidates) {
        # The packaged .NET 6 Harmony runtime is the one intentional central
        # projection. Older Harmony hashes are overwritten by the engine payload.
        if ($fileName -ieq '0Harmony.dll') {
            continue
        }
        $runtimePath = Join-Path $centralBin $fileName
        if (-not (Test-Path -LiteralPath $runtimePath -PathType Leaf)) {
            continue
        }
        $currentHash = (Get-LongFileSha256 -Path $runtimePath)
        $removeRegardless = $fileName -in $alwaysObsoleteCentralNames
        $knownLegacyHash = $legacyHashKeys.Contains(
            $fileName + '|' + $currentHash)
        if (-not $removeRegardless -and -not $knownLegacyHash) {
            continue
        }
        $removedBackup = Join-Path $cleanupBackupRoot `
            ('removed-legacy-central-runtime\' + $fileName)
        [void](New-Item -ItemType Directory `
            -Path (Split-Path -Parent $removedBackup) -Force)
        Copy-Item -LiteralPath $runtimePath -Destination $removedBackup -Force
        Remove-Item -LiteralPath $runtimePath -Force
        $removedLegacyRuntime++
    }

    $obsoleteRuntimeManifest = Join-Path $target `
        'server-data\launcher-managed-runtime-files.json'
    if (Test-Path -LiteralPath $obsoleteRuntimeManifest -PathType Leaf) {
        $manifestBackup = Join-Path $cleanupBackupRoot `
            'removed-legacy-central-runtime\launcher-managed-runtime-files.json'
        [void](New-Item -ItemType Directory `
            -Path (Split-Path -Parent $manifestBackup) -Force)
        Copy-Item -LiteralPath $obsoleteRuntimeManifest `
            -Destination $manifestBackup -Force
        Remove-Item -LiteralPath $obsoleteRuntimeManifest -Force
    }
    if ($removedLegacyRuntime -gt 0) {
        Write-Host "  Removed $removedLegacyRuntime stale central runtime file(s)." `
            -ForegroundColor Green
    }

    $retiredServerAddons = @(
        'engine\Modules\Coop\bin\Win64_Shipping_Server\0Harmony.dll',
        'engine\Modules\Coop\bin\Win64_Shipping_Server\AutoSyncExport\CraftingCampaignBehavior\CraftingCampaignBehavior__townOrderCount_SetLocalMessage.cs',
        'engine\Modules\Coop\bin\Win64_Shipping_Server\AutoSyncExport\CraftingCampaignBehavior\CraftingCampaignBehavior__townOrderCount_SetNetworkMessage.cs',
        'engine\Modules\Coop\bin\Win64_Shipping_Server\AutoSyncExport\CraftingCampaignBehavior\CraftingCampaignBehavior_DynamicPatches.cs',
        'engine\Modules\Coop\bin\Win64_Shipping_Server\AutoSyncExport\CraftingCampaignBehavior\CraftingCampaignBehavior_Handler.cs',
        'engine\bin\Win64_Shipping_Server\Coop.TimePacingCompatibility.dll',
        'engine\Modules\Coop\bin\Win64_Shipping_Server\Coop.TimePacingCompatibility.dll',
        'engine\Modules\DedicatedServer.Windows\bin\Win64_Shipping_Server\Coop.TimePacingCompatibility.dll'
    )
    $removedRetiredServerAddons = 0
    foreach ($relativePath in $retiredServerAddons) {
        if (Backup-AndRemoveRetiredFile -TargetRoot $target `
                -RelativePath $relativePath -BackupRoot $cleanupBackupRoot) {
            $removedRetiredServerAddons++
        }
    }
    if ($removedRetiredServerAddons -gt 0) {
        Write-Host "  Removed $removedRetiredServerAddons retired sync add-on file(s)." `
            -ForegroundColor Green
    }

    $removedClientViews = Remove-ObsoleteClientModuleViews `
        -TargetRoot $target -BackupRoot $cleanupBackupRoot
    if ($removedClientViews -gt 0) {
        Write-Host "  Removed $removedClientViews obsolete client-module junction(s)." `
            -ForegroundColor Green
    }
    if (Remove-ObsoleteBaseMapJunction `
            -TargetRoot $target -BackupRoot $cleanupBackupRoot) {
        Write-Host '  Removed the obsolete base-map junction (linked map data was preserved).' `
            -ForegroundColor Green
    }

    foreach ($fileName in @(
        'BannerlordCoopLauncher.exe',
        'BannerlordCoop.RuntimeBootstrap.dll',
        'release-info.txt',
        'SERVER-CONSOLE.bat',
        'Server-Console.ps1',
        'Start-ModdedServer.cmd',
        'Start-ModdedServer.ps1',
        'Install-Update.ps1',
        'INSTALL-UPDATE.bat',
        'IMPORTANT - INSTALL.txt'
    )) {
        $sourcePath = Join-Path $packageRoot $fileName
        $destinationPath = Join-Path $target $fileName
        if (-not (Test-Path -LiteralPath $destinationPath -PathType Leaf) -or
            (Get-LongFileSha256 -Path $sourcePath) -ine
            (Get-LongFileSha256 -Path $destinationPath)) {
            throw "Server root verification failed: $destinationPath"
        }
    }
    Write-Host '  Verifying installed server payload...'
    Assert-PayloadMatches `
        -SourceRoot (Join-Path $packageRoot 'engine') `
        -DestinationRoot (Join-Path $target 'engine') `
        -Description 'server engine payload'
    Sync-PackageOwnedManagedFiles `
        -SourceEngineRoot (Join-Path $packageRoot 'engine') `
        -TargetEngineRoot (Join-Path $target 'engine') `
        -VerifyOnly
    Assert-PinnedCoopModule `
        -ModuleRoot (Join-Path $target 'engine\Modules\Coop') `
        -BinFolder 'bin\Win64_Shipping_Server' `
        -Description 'Installed server Coop module'

    $obsoleteFiles = @(
        Get-ChildItem -LiteralPath $target -File -ErrorAction SilentlyContinue |
            Where-Object {
                $_.Name -eq 'BannerlordCoopLauncher.pending.exe' -or
                $_.Name -like 'BannerlordCoopLauncher.winforms-backup*.exe' -or
                $_.Name -eq 'IMPORTANT - BATTLE SYNC INSTALL.txt' -or
                $_.Name -eq 'README - Bannerlord UI Update.txt'
            }
    )
    foreach ($obsoleteFile in $obsoleteFiles) {
        Remove-Item -LiteralPath $obsoleteFile.FullName -Force
    }
    if ($null -ne $backupRoot -and
        (Test-Path -LiteralPath $backupRoot -PathType Container) -and
        @((Get-ChildItem -LiteralPath $backupRoot -Recurse -File `
            -ErrorAction SilentlyContinue)).Count -eq 0) {
        Remove-Item -LiteralPath $backupRoot -Recurse -Force
        $backupRoot = $null
    }
    if ($null -eq $backupRoot) {
        Write-Host '  Server update confirmed.' -ForegroundColor Green
    }
    else {
        Write-Host "  Server updated. Backup: $backupRoot" -ForegroundColor Green
    }
}

foreach ($target in @($moduleTargets)) {
    if (-not (Test-CoopModuleRoot -Path $target)) {
        throw "Client module validation failed immediately before copy: $target"
    }
    Write-Host
    Write-Host "Updating client module: $target" -ForegroundColor Cyan
    $clientBackupRoot = Get-ClientBackupRoot `
        -Target $target `
        -Timestamp $timestamp
    if (Backup-AndRemoveRetiredFile -TargetRoot $target `
            -RelativePath 'bin\Win64_Shipping_Client\Coop.TimePacingCompatibility.dll' `
            -BackupRoot $clientBackupRoot) {
        Write-Host '  Removed retired client time/sync add-on.' -ForegroundColor Green
    }
    $removedBaseGameAssemblies = Backup-AndRemoveBaseGameClientAssemblies `
        -TargetRoot $target `
        -BackupRoot $clientBackupRoot
    if ($removedBaseGameAssemblies -gt 0) {
        Write-Host ("  Removed $removedBaseGameAssemblies stale V0.2.19 " +
            'TaleWorlds/SandBox client DLL copy/copies.') -ForegroundColor Green
    }
    Backup-PayloadFiles `
        -SourceRoot $clientPayload `
        -DestinationRoot $target `
        -BackupRoot $clientBackupRoot
    Copy-DirectoryContents -Source $clientPayload -Destination $target
    Assert-PayloadMatches `
        -SourceRoot $clientPayload `
        -DestinationRoot $target `
        -Description 'Coop client payload'
    Assert-PinnedCoopModule `
        -ModuleRoot $target `
        -BinFolder 'bin\Win64_Shipping_Client' `
        -Description 'Installed client Coop module'
    $remainingBaseGameAssemblies = @(
        Get-BaseGameClientAssemblies -ModuleRoot $target
    )
    if ($remainingBaseGameAssemblies.Count -gt 0) {
        throw ('Client cleanup failed; base-game DLL copies remain in the Coop ' +
            'module: ' +
            (($remainingBaseGameAssemblies | Select-Object -ExpandProperty Name) -join ', '))
    }
    if (Test-Path -LiteralPath $clientBackupRoot -PathType Container) {
        Write-Host "  Client module updated. Backup: $clientBackupRoot" `
            -ForegroundColor Green
    }
    else {
        Write-Host '  Client module already current; no backup was needed.' `
            -ForegroundColor Green
    }
}

if ($serverTargets.Count -eq 0 -and $moduleTargets.Count -eq 0) {
    throw 'No valid installation targets were selected.'
}

Write-Host
Write-Host 'Update completed successfully.' -ForegroundColor Green
Write-Host 'Matched server and client Coop files were installed where detected.'
exit 0
