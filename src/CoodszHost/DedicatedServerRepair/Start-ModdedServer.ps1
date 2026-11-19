[CmdletBinding()]
param(
    [ValidateRange(1, 65535)]
    [int]$Port = 7210,

    [ValidatePattern('^[A-Za-z]{2,8}$')]
    [string]$Region = 'EU',

    [switch]$NoPrompt,

    [string]$SaveName,

    [string]$SaveFile,

    [string]$NewSaveName,

    [ValidateSet('Freebooter', 'Warrior', 'Bannerlord')]
    [string]$Difficulty,

    [int]$BattleSize = 0,

    # Accept a save made with fewer mods (the server only adds mods; the save's
    # own mods keep their versions). Coodsz Host passes it after the
    # host agreed and a backup of the save was made.
    [switch]$AllowAddedModules
)

$ErrorActionPreference = 'Stop'

function Test-IsRecoverySaveName {
    param([string]$Name)

    if ([string]::IsNullOrWhiteSpace($Name)) {
        return $false
    }
    $saveName = [System.IO.Path]::GetFileName($Name.Trim())
    if ($saveName.EndsWith('.sav', [StringComparison]::OrdinalIgnoreCase)) {
        $saveName = $saveName.Substring(0, $saveName.Length - 4)
    }
    return $saveName -match '(?i)\.backup[12]$'
}

function Get-PrimarySaveName {
    param([string]$Name)

    $saveName = [System.IO.Path]::GetFileName($Name.Trim())
    if ($saveName.EndsWith('.sav', [StringComparison]::OrdinalIgnoreCase)) {
        $saveName = $saveName.Substring(0, $saveName.Length - 4)
    }
    while ($saveName -match '(?i)\.backup[12]$') {
        $saveName = $saveName.Substring(0, $saveName.Length - 8)
    }
    return $saveName
}

function Test-IsSelectablePrimarySaveName {
    param([string]$Name)

    $saveName = Get-PrimarySaveName -Name $Name
    return -not (Test-IsRecoverySaveName -Name $Name) -and
        -not [string]::Equals(
            $saveName,
            'default_new_game',
            [StringComparison]::OrdinalIgnoreCase)
}

function Resolve-ConfiguredPrimarySaveName {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ConfiguredSaveName,

        [Parameter(Mandatory = $true)]
        [string]$SaveRoot
    )

    if (-not (Test-IsRecoverySaveName -Name $ConfiguredSaveName)) {
        return $ConfiguredSaveName
    }

    $primaryName = Get-PrimarySaveName -Name $ConfiguredSaveName
    $primarySave = Join-Path $SaveRoot ($primaryName + '.sav')
    $primaryJson = Join-Path $SaveRoot ($primaryName + '.json')
    if (-not (Test-Path -LiteralPath $primarySave -PathType Leaf) -or
        -not (Test-Path -LiteralPath $primaryJson -PathType Leaf)) {
        throw "Configured save '$ConfiguredSaveName' is a recovery generation, but the primary pair '$primaryName.sav/.json' is incomplete. Stop and restore the intended recovery pair to the primary name before launch."
    }
    return $primaryName
}

# Coop clamps the battle size to 1,000 and Launcher Battle Size takes 300 to 2,000. With Limitless and
# Limitless.CoopCompat both in the module list, that patch lets the Coop server use up to 10,000.
function Get-BattleSizeMaximum {
    param([string[]]$ModuleIds)

    if (('Limitless' -in $ModuleIds) -and ('Limitless.CoopCompat' -in $ModuleIds)) {
        return 10000
    }
    return 2000
}

foreach ($directSaveReference in @($SaveFile, $SaveName)) {
    if (-not [string]::IsNullOrWhiteSpace($directSaveReference) -and
        -not (Test-IsSelectablePrimarySaveName -Name $directSaveReference)) {
        throw "Recovery generations and default_new_game cannot be hosted directly: '$directSaveReference'. Restore a chosen recovery pair to a primary campaign name first."
    }
}

# The module list is read later; the exact top for it is checked there.
if ($BattleSize -ne 0 -and ($BattleSize -lt 300 -or $BattleSize -gt 10000)) {
    throw 'Battle size must be between 300 and 10,000.'
}

if (-not ('BannerlordRglDialogGuard' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

public static class BannerlordRglDialogGuard
{
    private const uint TH32CS_SNAPPROCESS = 0x00000002;
    private const uint WM_COMMAND = 0x0111;
    private const int IDYES = 6;
    private const uint SMTO_ABORTIFHUNG = 0x0002;
    private static volatile bool _running;
    private static int _rootProcessId;
    private static Thread _thread;

    public static void Start(int rootProcessId)
    {
        if (_running) return;
        _rootProcessId = rootProcessId;
        _running = true;
        _thread = new Thread(Monitor) { IsBackground = true, Name = "RGL dialog guard" };
        _thread.Start();
    }

    public static void Stop()
    {
        _running = false;
        if (_thread != null && _thread.IsAlive)
            _thread.Join(1000);
        _thread = null;
    }

    private static void Monitor()
    {
        while (_running)
        {
            EnumWindows(InspectWindow, IntPtr.Zero);
            Thread.Sleep(100);
        }
    }

    private static bool InspectWindow(IntPtr window, IntPtr state)
    {
        StringBuilder title = new StringBuilder(256);
        GetWindowText(window, title, title.Capacity);
        if (!string.Equals(title.ToString(), "Always Ignore?", StringComparison.Ordinal))
            return true;

        uint processId;
        GetWindowThreadProcessId(window, out processId);
        if (!IsDescendantOfRoot((int)processId))
            return true;

        IntPtr result;
        SendMessageTimeout(
            window, WM_COMMAND, new IntPtr(IDYES), IntPtr.Zero,
            SMTO_ABORTIFHUNG, 1000, out result);
        Console.WriteLine(
            "[DedicatedServer Engine] Automatically accepted a blocking RGL warning dialog.");
        return true;
    }

    private static bool IsDescendantOfRoot(int processId)
    {
        Dictionary<int, int> parents = new Dictionary<int, int>();
        IntPtr snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
        if (snapshot == new IntPtr(-1))
            return false;

        try
        {
            PROCESSENTRY32 entry = new PROCESSENTRY32();
            entry.dwSize = (uint)Marshal.SizeOf(typeof(PROCESSENTRY32));
            if (Process32First(snapshot, ref entry))
            {
                do
                {
                    parents[(int)entry.th32ProcessID] = (int)entry.th32ParentProcessID;
                    entry.dwSize = (uint)Marshal.SizeOf(typeof(PROCESSENTRY32));
                }
                while (Process32Next(snapshot, ref entry));
            }
        }
        finally
        {
            CloseHandle(snapshot);
        }

        int current = processId;
        for (int depth = 0; depth < 32 && parents.ContainsKey(current); depth++)
        {
            if (current == _rootProcessId)
                return true;
            int parent = parents[current];
            if (parent == current || parent == 0)
                break;
            current = parent;
        }
        return current == _rootProcessId;
    }

    private delegate bool EnumWindowsProc(IntPtr window, IntPtr state);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr state);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr window, StringBuilder text, int maximum);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(
        IntPtr window, uint message, IntPtr wParam, IntPtr lParam,
        uint flags, uint timeout, out IntPtr result);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool Process32First(IntPtr snapshot, ref PROCESSENTRY32 entry);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool Process32Next(IntPtr snapshot, ref PROCESSENTRY32 entry);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct PROCESSENTRY32
    {
        public uint dwSize;
        public uint cntUsage;
        public uint th32ProcessID;
        public IntPtr th32DefaultHeapID;
        public uint th32ModuleID;
        public uint cntThreads;
        public uint th32ParentProcessID;
        public int pcPriClassBase;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szExeFile;
    }
}
'@ | Out-Null
}

$serverRoot = $PSScriptRoot
$engineRoot = Join-Path $serverRoot 'engine'
$modulesRoot = Join-Path $engineRoot 'Modules'
$serverBin = Join-Path $engineRoot 'bin\Win64_Shipping_Server'
$dotnetPath = Join-Path $engineRoot 'dotnet\dotnet.exe'
$starterDll = Join-Path $serverBin 'TaleWorlds.Starter.DotNetCore.dll'
$moduleListPath = Join-Path $serverRoot 'modded-server-modules.txt'
$selectionPath = Join-Path $serverRoot 'modded-server-selection.json'
$modulePolicyPath = Join-Path $serverRoot 'server-module-policy.json'
$documentsRoot = [Environment]::GetFolderPath('MyDocuments')
if ([string]::IsNullOrWhiteSpace($documentsRoot)) {
    $documentsRoot = Join-Path $env:USERPROFILE 'Documents'
}
$coopDataRoot = Join-Path $documentsRoot 'Mount and Blade II Bannerlord\CoopData'
$dedicatedDataRoot = Join-Path $coopDataRoot 'DedicatedServer'
$serverConfigPath = Join-Path $dedicatedDataRoot 'server-config.json'
$serverSaveRoot = Join-Path $dedicatedDataRoot 'Game Saves'
$modConfigPath = Join-Path $coopDataRoot 'mod-config.json'
$legacyServerConfigPath = Join-Path $serverRoot 'server-config.json'
$legacyServerSaveRoot = Join-Path $serverRoot 'server-data\Game Saves'
$runtimeBootstrapPath = Join-Path $serverRoot 'BannerlordCoop.RuntimeBootstrap.dll'
$strictUtf8NoBom = New-Object System.Text.UTF8Encoding($false, $true)
$serverDataRoot = Join-Path $serverRoot 'server-data'
New-Item -ItemType Directory -Path $serverDataRoot -Force | Out-Null
$instanceLockPath = Join-Path $serverDataRoot 'server-instance.lock'
try {
    $serverInstanceLock = [System.IO.File]::Open(
        $instanceLockPath,
        [System.IO.FileMode]::OpenOrCreate,
        [System.IO.FileAccess]::ReadWrite,
        [System.IO.FileShare]::None
    )
}
catch [System.IO.IOException] {
    throw "Another Bannerlord Coop server is already starting or running from '$serverRoot'. Stop that instance before starting another."
}

$portOwner = [System.Net.NetworkInformation.IPGlobalProperties]::
    GetIPGlobalProperties().GetActiveUdpListeners() |
    Where-Object { $_.Port -eq $Port } |
    Select-Object -First 1
if ($null -ne $portOwner) {
    $serverInstanceLock.Dispose()
    throw "UDP port $Port is already in use. Stop the existing server or choose another port."
}

function Read-SafeXmlFile {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    $settings = New-Object System.Xml.XmlReaderSettings
    $settings.DtdProcessing = [System.Xml.DtdProcessing]::Prohibit
    $settings.XmlResolver = $null
    $settings.MaxCharactersInDocument = 4194304
    $reader = [System.Xml.XmlReader]::Create($Path, $settings)
    try {
        $document = New-Object System.Xml.XmlDocument
        $document.XmlResolver = $null
        $document.Load($reader)
        return ,$document
    }
    finally {
        $reader.Dispose()
    }
}

function Write-Utf8TextAtomically {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [AllowEmptyString()]
        [string]$Content
    )

    $directory = [System.IO.Path]::GetDirectoryName($Path)
    if ([string]::IsNullOrWhiteSpace($directory)) {
        throw "The file has no parent directory: $Path"
    }
    $temporaryPath = Join-Path $directory (
        '.' + [System.IO.Path]::GetFileName($Path) + '.' +
        $PID + '.' + [Guid]::NewGuid().ToString('N') + '.tmp'
    )
    try {
        [System.IO.File]::WriteAllText($temporaryPath, $Content, $strictUtf8NoBom)
        Move-Item -LiteralPath $temporaryPath -Destination $Path -Force
    }
    finally {
        if (Test-Path -LiteralPath $temporaryPath) {
            Remove-Item -LiteralPath $temporaryPath -Force
        }
    }
}

function Ensure-OfficialDataLayout {
    New-Item -ItemType Directory -Path $coopDataRoot -Force | Out-Null
    New-Item -ItemType Directory -Path $dedicatedDataRoot -Force | Out-Null
    New-Item -ItemType Directory -Path $serverSaveRoot -Force | Out-Null

    if (-not (Test-Path -LiteralPath $serverConfigPath -PathType Leaf)) {
        if (Test-Path -LiteralPath $legacyServerConfigPath -PathType Leaf) {
            Copy-Item -LiteralPath $legacyServerConfigPath `
                -Destination $serverConfigPath
            Write-Host "Migrated server configuration to: $serverConfigPath"
        }
        else {
            $defaultServerConfig = @'
{
  "saveName": "default_new_game",
  "autosaveMinutes": 5,
  "password": "",
  "port": 4200,
  "logFile": true,
  "steam": true,
  "autoRestart": true,
  "autoRestartHours": 3,
  "allowClientTimeControls": true,
  "battleSize": 1000
}
'@
            Write-Utf8TextAtomically `
                -Path $serverConfigPath `
                -Content ($defaultServerConfig + [Environment]::NewLine)
            Write-Host "Created server configuration: $serverConfigPath"
        }
    }

    if (-not (Test-Path -LiteralPath $modConfigPath -PathType Leaf)) {
        $packagedModConfig = Join-Path $modulesRoot 'Coop\mod-config.default.json'
        if (Test-Path -LiteralPath $packagedModConfig -PathType Leaf) {
            Copy-Item -LiteralPath $packagedModConfig -Destination $modConfigPath
        }
        else {
            Write-Utf8TextAtomically `
                -Path $modConfigPath `
                -Content ('{"modOptions":{}}' +
                    [Environment]::NewLine)
        }
        Write-Host "Created Coop gameplay configuration: $modConfigPath"
    }

    if (Test-Path -LiteralPath $legacyServerSaveRoot -PathType Container) {
        foreach ($legacyFile in Get-ChildItem -LiteralPath $legacyServerSaveRoot -File) {
            if ($legacyFile.Extension -notin @('.sav', '.json')) {
                continue
            }
            $destination = Join-Path $serverSaveRoot $legacyFile.Name
            if (-not (Test-Path -LiteralPath $destination)) {
                Copy-Item -LiteralPath $legacyFile.FullName -Destination $destination
            }
        }
    }

    if (-not (Test-Path -LiteralPath $modulePolicyPath -PathType Leaf)) {
        $defaultModulePolicy = @'
{
  "policySchemaVersion": 3,
  "allowUnsupportedMods": true,
  "forceAllSelectedGameplayModulesHeadless": true,
  "blockManagedAssemblyCollisions": false,
  "forceHeadlessModules": [],
  "rollingSaveVersionModules": [],
  "strictDependencyValidation": false,
  "softDependencyModules": [
    "CustomBattle",
    "StoryMode",
    "BirthAndDeath"
  ],
  "allowedAssemblyCollisions": []
}
'@
        Write-Utf8TextAtomically `
            -Path $modulePolicyPath `
            -Content ($defaultModulePolicy + [Environment]::NewLine)
    }
}

function Get-Sha256Hex {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    $stream = [System.IO.File]::OpenRead($Path)
    $sha256 = [System.Security.Cryptography.SHA256]::Create()
    try {
        return [System.BitConverter]::ToString(
            $sha256.ComputeHash($stream)
        ).Replace('-', '')
    }
    finally {
        $sha256.Dispose()
        $stream.Dispose()
    }
}

function Install-CentralRuntimeAssembly {
    param(
        [Parameter(Mandatory = $true)]
        [string]$SourcePath
    )

    if (-not (Test-Path -LiteralPath $SourcePath)) {
        throw "Central runtime source is missing: $SourcePath"
    }

    $fileName = [System.IO.Path]::GetFileName($SourcePath)
    $targetPath = Join-Path $serverBin $fileName
    $sourceHash = Get-Sha256Hex -Path $SourcePath
    if (Test-Path -LiteralPath $targetPath) {
        $targetHash = Get-Sha256Hex -Path $targetPath
        if ($targetHash -ne $sourceHash) {
            $backupRoot = Join-Path $serverRoot `
                'server-data\launcher-backups\central-runtime-replaced'
            [System.IO.Directory]::CreateDirectory($backupRoot) | Out-Null
            $backupName = [System.IO.Path]::GetFileNameWithoutExtension($fileName) +
                '.' + $targetHash.Substring(0, 12) +
                [System.IO.Path]::GetExtension($fileName)
            Copy-Item -LiteralPath $targetPath `
                -Destination (Join-Path $backupRoot $backupName) -Force
        }
    }
    Copy-Item -LiteralPath $SourcePath -Destination $targetPath -Force
}

function Get-ServerMcmProblem {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    # Old manual MCM copies (v5.11 and older) build MCMv5.dll against
    # Bannerlord.UIExtenderEx, which the headless server never loads, so the
    # engine dies on a ReflectionTypeLoadException while loading submodules.
    # The Workshop builds (5.12.x) have no such reference. The version is read
    # without loading the assembly, and the reference by a plain byte scan.
    # A file that cannot be read now (locked by the game or a Steam update)
    # starts as before.
    try {
        $mcmVersion = [System.Reflection.AssemblyName]::GetAssemblyName($Path).Version
        $mcmText = [System.Text.Encoding]::ASCII.GetString([System.IO.File]::ReadAllBytes($Path))
    }
    catch {
        return $null
    }
    if ($mcmVersion -ge [version]'5.12.0.0' -and
        -not $mcmText.Contains('Bannerlord.UIExtenderEx')) {
        return $null
    }
    $mcmLabel = 'v' + $mcmVersion.ToString(3)
    return ("Mod Configuration Menu $mcmLabel is too old for the dedicated server and crashes it. " +
        "Get MCM v5.12.3 or newer from Nexus Mods (mod 612) and put its Bannerlord.MBOptionScreen folder in the game's " +
        'Modules folder, then press Start server again. The tool copies it to the server.')
}

function Initialize-HeadlessFrameworkRuntime {
    param(
        [Parameter(Mandatory = $true)]
        [hashtable]$InstalledModules,

        [Parameter(Mandatory = $true)]
        [string[]]$ActiveModuleIds
    )

    if ('Bannerlord.Harmony' -in $ActiveModuleIds) {
        # The update owns one deterministic server build of Harmony in the
        # central resolver directory. Do not depend on a manually added DLL in
        # Coop or load the interchangeable client Harmony module headlessly.
        $harmonyPath = Join-Path $serverBin '0Harmony.dll'
        if (-not (Test-Path -LiteralPath $harmonyPath -PathType Leaf)) {
            throw "The packaged server Harmony runtime is missing: $harmonyPath"
        }
        $harmonyRoot = [string]$InstalledModules['Bannerlord.Harmony']
        $installedHarmonyPath = @(
            (Join-Path $harmonyRoot 'bin\Gaming.Desktop.x64_Shipping_Client\0Harmony.dll'),
            (Join-Path $harmonyRoot 'bin\Win64_Shipping_Server\0Harmony.dll'),
            (Join-Path $harmonyRoot 'bin\Win64_Shipping_Client\0Harmony.dll')
        ) | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } |
            Select-Object -First 1
        $serverHarmonyVersion =
            [System.Diagnostics.FileVersionInfo]::GetVersionInfo($harmonyPath).FileVersion
        if ([string]::IsNullOrWhiteSpace([string]$installedHarmonyPath)) {
            Write-Warning ("Harmony is active but its module-local 0Harmony.dll was not " +
                "found under '$harmonyRoot'. HexTool will still use the packaged server " +
                "runtime $serverHarmonyVersion and attempt startup.")
        }
        else {
            $installedHarmonyVersion =
                [System.Diagnostics.FileVersionInfo]::GetVersionInfo($installedHarmonyPath).FileVersion
            if ([string]::IsNullOrWhiteSpace($serverHarmonyVersion) -or
                [string]::IsNullOrWhiteSpace($installedHarmonyVersion) -or
                $serverHarmonyVersion -ne $installedHarmonyVersion) {
                Write-Warning ("Installed Harmony is $installedHarmonyVersion, while " +
                    "the packaged server runtime is $serverHarmonyVersion. HexTool will " +
                    "prefer its packaged runtime and attempt startup; compatibility is " +
                    "the mod pack operator's responsibility.")
            }
            else {
                Write-Host "Harmony: server runtime $serverHarmonyVersion matches the installed client module."
            }
        }
    }

    if ('Bannerlord.MBOptionScreen' -in $ActiveModuleIds) {
        # MCM's UI cannot run in a headless process. Publish MCMv5.dll for
        # compatible gameplay modules; the startup hook permits only MCM's
        # non-UI settings core and continues to suppress graphical submodules.
        $mcmRoot = [string]$InstalledModules['Bannerlord.MBOptionScreen']
        $mcmCandidates = @(
            (Join-Path $mcmRoot 'bin\Win64_Shipping_Server\MCMv5.dll'),
            (Join-Path $mcmRoot 'bin\Win64_Shipping_Client\MCMv5.dll'),
            (Join-Path $mcmRoot 'bin\Gaming.Desktop.x64_Shipping_Client\MCMv5.dll')
        )
        $mcmPath = $mcmCandidates |
            Where-Object { Test-Path -LiteralPath $_ } |
            Select-Object -First 1
        if ([string]::IsNullOrWhiteSpace([string]$mcmPath)) {
            Write-Warning ("MCM is active but MCMv5.dll was not found under '$mcmRoot'. " +
                "HexTool will still attempt startup.")
        }
        else {
            $mcmProblem = Get-ServerMcmProblem -Path $mcmPath
            if ($null -ne $mcmProblem) {
                # Name the server copy that is too old; the newer MCM goes in the game's Modules folder (crash reports #32, #34).
                throw ($mcmProblem + " The old copy on the server is in this folder: " + $mcmRoot)
            }
            Install-CentralRuntimeAssembly -SourcePath $mcmPath
            Write-Host 'MCM: headless settings core enabled; standalone UI remains excluded.'
        }
    }
}

function Assert-ManagedAssemblyCompatibility {
    param(
        [Parameter(Mandatory = $true)][hashtable]$InstalledModules,
        [Parameter(Mandatory = $true)][string[]]$ActiveModuleIds,
        [AllowEmptyCollection()][string[]]$CommunityModuleIds = @(),
        [AllowEmptyCollection()][string[]]$AllowedCollisions = @(),
        [bool]$BlockCollisions = $false,
        [Parameter(Mandatory = $true)][string]$ServerBin
    )

    $communitySet = [System.Collections.Generic.HashSet[string]]::new(
        [StringComparer]::OrdinalIgnoreCase)
    foreach ($moduleId in $CommunityModuleIds) {
        [void]$communitySet.Add($moduleId)
    }
    $records = [System.Collections.Generic.List[object]]::new()

    $locations = [System.Collections.Generic.List[object]]::new()
    $locations.Add([pscustomobject]@{
        Owner = '<engine>'
        IsCommunity = $false
        Path = [System.IO.Path]::GetFullPath($ServerBin)
    })
    foreach ($moduleId in $ActiveModuleIds) {
        if (-not $InstalledModules.ContainsKey($moduleId)) {
            continue
        }
        foreach ($relativeBin in @(
            'bin\Win64_Shipping_Server',
            'bin\Win64_Shipping_Client',
            'bin\Gaming.Desktop.x64_Shipping_Client'
        )) {
            $candidateBin = Join-Path $InstalledModules[$moduleId] $relativeBin
            if (Test-Path -LiteralPath $candidateBin -PathType Container) {
                $locations.Add([pscustomobject]@{
                    Owner = $moduleId
                    IsCommunity = $communitySet.Contains($moduleId)
                    Path = [System.IO.Path]::GetFullPath($candidateBin)
                })
            }
        }
    }

    foreach ($location in $locations) {
        foreach ($file in Get-ChildItem -LiteralPath $location.Path `
                     -Filter '*.dll' -File -ErrorAction SilentlyContinue) {
            try {
                $identity = [System.Reflection.AssemblyName]::GetAssemblyName(
                    $file.FullName)
                if ([string]::IsNullOrWhiteSpace($identity.Name)) {
                    continue
                }
                $records.Add([pscustomobject]@{
                    Name = $identity.Name
                    Identity = $identity.FullName
                    Sha256 = Get-Sha256Hex -Path $file.FullName
                    Owner = $location.Owner
                    IsCommunity = [bool]$location.IsCommunity
                    Path = $file.FullName
                })
            }
            catch [System.BadImageFormatException] {
                # Native DLLs commonly share the .dll extension.
            }
            catch [System.IO.FileLoadException] {
                Write-Warning "Could not inspect managed assembly identity: $($file.FullName)"
            }
        }
    }

    $blockingCollisions = [System.Collections.Generic.List[string]]::new()
    foreach ($group in @($records | Group-Object Name)) {
        $variants = @(
            $group.Group |
                Select-Object Identity, Sha256 -Unique
        )
        $owners = @($group.Group.Owner | Select-Object -Unique)
        # Platform variants within one module directory are requester-local and
        # only one platform is used in a given process. Across owners, however,
        # a shared AssemblyLoadContext cannot isolate either different assembly
        # identities or byte-different implementations with the same identity.
        if ($variants.Count -le 1 -or $owners.Count -le 1) {
            continue
        }
        $communityOwners = @(
            $group.Group |
                Where-Object IsCommunity |
                Select-Object -ExpandProperty Owner -Unique
        )
        if ($communityOwners.Count -eq 0) {
            continue
        }

        $details = @(
            $group.Group |
                Sort-Object Owner, Identity, Path |
                ForEach-Object {
                    "    $($_.Owner): $($_.Identity) SHA256=$($_.Sha256) [$($_.Path)]"
                }
        ) -join [Environment]::NewLine
        if ($group.Name -in $AllowedCollisions) {
            Write-Warning ("Allowed managed assembly collision '$($group.Name)':" +
                [Environment]::NewLine + $details)
            continue
        }
        $variantIdentities = @($variants.Identity | Select-Object -Unique)
        $variantHashes = @($variants.Sha256 | Select-Object -Unique)
        $pinnedNewtonsoftHashes = @(
            '72CF291D4BAB0EDD08A9B07C6173E1E7AD1ABB7AB727FD7044BF6305D7515661'
            'B624949DF8B0E3A6153FDFB730A7C6F4990B6592EE0D922E1788433D276610F3'
        )
        $isPinnedEuropeNewtonsoftPair =
            $group.Name -ieq 'Newtonsoft.Json' -and
            $variantIdentities.Count -eq 1 -and
            $variantIdentities[0] -ieq
                'Newtonsoft.Json, Version=13.0.0.0, Culture=neutral, PublicKeyToken=30ad4fe6b2a6aeed' -and
            $variantHashes.Count -eq 2 -and
            @($variantHashes | Where-Object { $_ -notin $pinnedNewtonsoftHashes }).Count -eq 0 -and
            $owners.Count -eq 2 -and
            '<engine>' -in $owners -and
            'Europe1100' -in $owners
        if ($isPinnedEuropeNewtonsoftPair) {
            Write-Warning ("Allowed the pinned engine/Europe1100 Newtonsoft.Json " +
                "13.0.1 platform pair:" + [Environment]::NewLine + $details)
            continue
        }
        $collision =
            "  $($group.Name) has incompatible identity/content variants across " +
            "owners $($owners -join ', '):" + [Environment]::NewLine +
            $details
        if (-not $BlockCollisions) {
            Write-Warning ($collision + [Environment]::NewLine +
                "HexTool compatibility mode will attempt startup. The shared CLR " +
                "context may select only one variant; a later mod/server failure is " +
                "the operator's compatibility responsibility.")
            continue
        }
        $blockingCollisions.Add($collision)
    }

    if ($blockingCollisions.Count -gt 0) {
        throw ("Selected modules bundle incompatible identity/content variants of " +
            "the same managed assembly. The shared Bannerlord load context cannot " +
            "isolate them safely." +
            [Environment]::NewLine + ($blockingCollisions -join [Environment]::NewLine) +
            [Environment]::NewLine +
            "Disable blockManagedAssemblyCollisions to attempt this unsupported mod " +
            "combination, or add a proven-safe assembly name to " +
            "allowedAssemblyCollisions in server-module-policy.json.")
    }
}

function Get-NativeGameVersionPrefix {
    $fallback = 'v1.4.8'
    try {
        $nativeManifestPath = Join-Path $modulesRoot 'Native\SubModule.xml'
        $nativeManifest = Read-SafeXmlFile -Path $nativeManifestPath
        $manifestVersion = [string]$nativeManifest.Module.Version.value
        if ($manifestVersion -match '^v?(\d+\.\d+\.\d+)') {
            return 'v' + $Matches[1]
        }
    }
    catch {
        # The current dedicated build version remains the compatibility fallback.
    }
    return $fallback
}

function Assert-CoreCampaignRuntimeVersions {
    $versions = [ordered]@{}
    foreach ($moduleName in @('Native', 'SandBoxCore', 'SandBox')) {
        $descriptorPath = Join-Path $modulesRoot "$moduleName\SubModule.xml"
        if (-not (Test-Path -LiteralPath $descriptorPath -PathType Leaf)) {
            throw "Required core module descriptor is missing: $descriptorPath"
        }
        $descriptor = Read-SafeXmlFile -Path $descriptorPath
        $version = ([string]$descriptor.Module.Version.value).Trim()
        if ($version -notmatch '^v\d+\.\d+\.\d+(?:\.\d+)?$') {
            throw "Invalid $moduleName core module version: '$version'"
        }
        $versions[$moduleName] = $version
    }

    $distinctVersions = @($versions.Values | Select-Object -Unique)
    if ($distinctVersions.Count -ne 1) {
        $details = @($versions.GetEnumerator() | ForEach-Object {
            "$($_.Key)=$($_.Value)"
        }) -join ', '
        throw ("The dedicated server contains a mixed native/campaign runtime " +
            "($details). Install a server update built from the matching " +
            "Bannerlord client before launching; mixed runtimes can disconnect " +
            "players while unpacking their hero.")
    }

    $campaignAssembly = Join-Path $serverBin 'TaleWorlds.CampaignSystem.dll'
    $sandBoxAssembly = Join-Path $modulesRoot `
        'SandBox\bin\Win64_Shipping_Server\SandBox.dll'
    $versionDescriptor = Join-Path $engineRoot 'Parameters\Version.xml'
    foreach ($requiredPath in @(
            $campaignAssembly,
            $sandBoxAssembly,
            $versionDescriptor
        )) {
        if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
            throw "The campaign runtime is incomplete: $requiredPath"
        }
    }
    [xml]$versionXml = [System.IO.File]::ReadAllText($versionDescriptor)
    $singleplayerVersion = ([string]$versionXml.Version.Singleplayer.Value).Trim()
    if ($singleplayerVersion -ine $distinctVersions[0]) {
        throw ("The staged campaign version does not match the core modules: " +
            "Parameters=$singleplayerVersion, modules=$($distinctVersions[0])")
    }

    Write-Host ("Verified one core campaign runtime: " +
        "$($distinctVersions[0]) (Native + SandBoxCore + SandBox).")
}

function Get-BannerlordVersionIdentity {
    param([string]$Version)

    if ([string]::IsNullOrWhiteSpace($Version)) {
        return $null
    }
    $match = [regex]::Match(
        $Version.Trim(),
        '^(?<type>[abedv])(?<major>\d+)\.(?<minor>\d+)\.(?<revision>\d+)(?:\.(?:\d+|\*))?$',
        [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)
    if (-not $match.Success) {
        return $null
    }
    return ('{0}{1}.{2}.{3}' -f
        $match.Groups['type'].Value.ToLowerInvariant(),
        $match.Groups['major'].Value,
        $match.Groups['minor'].Value,
        $match.Groups['revision'].Value)
}

function Get-BannerlordSaveInfo {
    param(
        [Parameter(Mandatory = $true)]
        [System.IO.FileInfo]$File,

        [Parameter(Mandatory = $true)]
        [string]$Source
    )

    $stream = $null
    $reader = $null
    try {
        $stream = [System.IO.File]::Open(
            $File.FullName,
            [System.IO.FileMode]::Open,
            [System.IO.FileAccess]::Read,
            [System.IO.FileShare]::ReadWrite
        )
        $reader = New-Object System.IO.BinaryReader($stream, $strictUtf8NoBom)
        $headerLength = $reader.ReadInt32()
        if ($headerLength -lt 2 -or $headerLength -gt 1048576) {
            throw "Invalid Bannerlord save metadata length: $headerLength"
        }

        $headerBytes = $reader.ReadBytes($headerLength)
        if ($headerBytes.Length -ne $headerLength) {
            throw 'The save metadata header is incomplete.'
        }

        $headerJson = $strictUtf8NoBom.GetString($headerBytes).
            TrimStart([char]0xFEFF).
            TrimEnd([char]0)
        $metadata = (ConvertFrom-Json -InputObject $headerJson).List
        $saveModules = @(
            ([string]$metadata.Modules).Split(';') |
                ForEach-Object { $_.Trim() } |
                Where-Object { $_ }
        )
        $gameVersion = [string]$metadata.ApplicationVersion
        $launcherDifficulty = $null
        $launcherModuleVersions = @{}
        $hasTrustedModuleOrder = $false
        $launcherMetadataPath =
            [System.IO.Path]::ChangeExtension($File.FullName, '.launcher.json')
        if (Test-Path -LiteralPath $launcherMetadataPath) {
            try {
                $launcherMetadataFile = Get-Item -LiteralPath $launcherMetadataPath
                if ($launcherMetadataFile.Length -le 0 -or
                    $launcherMetadataFile.Length -gt 1048576) {
                    throw 'Launcher save metadata has an invalid length.'
                }
                $launcherMetadataText = [System.IO.File]::ReadAllText(
                    $launcherMetadataPath,
                    $strictUtf8NoBom
                )
                $launcherMetadata = ConvertFrom-Json -InputObject $launcherMetadataText
                $metadataMatchesSave =
                    [long]$launcherMetadata.SaveLength -eq $File.Length -and
                    [long]$launcherMetadata.SaveLastWriteTimeUtcTicks -eq
                        $File.LastWriteTimeUtc.Ticks
                if ($metadataMatchesSave) {
                    # Schema 1 sidecars were sometimes written after failed or
                    # interrupted starts. They remain useful only for difficulty;
                    # module identity/order stays sourced from the save header.
                    if ([int]$launcherMetadata.SchemaVersion -eq 2) {
                        $launcherModules = @(
                            $launcherMetadata.Modules |
                                ForEach-Object { ([string]$_).Trim() } |
                                Where-Object { $_ }
                        )
                        $versionProperties = @(
                            $launcherMetadata.ModuleVersions.PSObject.Properties)
                        foreach ($property in $versionProperties) {
                            $versionValue = ([string]$property.Value).Trim()
                            if (-not [string]::IsNullOrWhiteSpace($versionValue)) {
                                $launcherModuleVersions[$property.Name] = $versionValue
                            }
                        }
                        if ($launcherModules.Count -gt 0 -and
                            @($launcherModules | Where-Object {
                                -not $launcherModuleVersions.ContainsKey($_)
                            }).Count -eq 0) {
                            $saveModules = $launcherModules
                            $hasTrustedModuleOrder = $true
                        }
                        if (-not [string]::IsNullOrWhiteSpace(
                                [string]$launcherMetadata.GameVersion)) {
                            $gameVersion = [string]$launcherMetadata.GameVersion
                        }
                    }
                    $difficultyValue = [string]$launcherMetadata.Difficulty
                    $launcherDifficulty = switch ($difficultyValue) {
                        'Freebooter' { 'Freebooter' }
                        'Warrior' { 'Warrior' }
                        'Bannerlord' { 'Bannerlord' }
                        # Migrate metadata produced by the short-lived
                        # level-based launcher build.
                        'VeryEasy' { 'Freebooter' }
                        'Easy' { 'Warrior' }
                        'Realistic' { 'Bannerlord' }
                        default { $null }
                    }
                }
            }
            catch {
                # A stale or partial launcher manifest is never authoritative;
                # fall back to Bannerlord's embedded save header.
            }
        }

        [pscustomobject]@{
            Name = $File.BaseName
            File = $File
            Source = $Source
            Character = if ($metadata.CharacterName) { [string]$metadata.CharacterName } else { 'Unknown' }
            Day = if ($metadata.DayLong) { [math]::Floor([double]$metadata.DayLong) } else { 0 }
            GameVersion = $gameVersion
            Difficulty = $launcherDifficulty
            Modules = $saveModules
            ModuleVersions = $launcherModuleVersions
            HasTrustedModuleOrder = $hasTrustedModuleOrder
            AddedModules = @()
            ModuleOrderChanged = $false
            SidecarPath = [System.IO.Path]::ChangeExtension($File.FullName, '.json')
            LauncherMetadataPath = $launcherMetadataPath
        }
    }
    catch {
        Write-Warning "Skipping unreadable save '$($File.FullName)': $($_.Exception.Message)"
    }
    finally {
        if ($reader) {
            $reader.Dispose()
        }
        elseif ($stream) {
            $stream.Dispose()
        }
    }
}

function Write-LauncherSaveMetadata {
    param(
        [Parameter(Mandatory = $true)]
        [string]$SaveName,

        [Parameter(Mandatory = $true)]
        [string[]]$LoadedModuleIds,

        [string]$Difficulty
    )

    $savePath = Join-Path $serverSaveRoot ($SaveName + '.sav')
    if (-not (Test-Path -LiteralPath $savePath)) {
        return
    }

    $gameVersion = $nativeGameVersionPrefix
    $nativeManifestPath = Join-Path $modulesRoot 'Native\SubModule.xml'
    try {
        $nativeManifest = Read-SafeXmlFile -Path $nativeManifestPath
        $manifestVersion = [string]$nativeManifest.Module.Version.value
        if (-not [string]::IsNullOrWhiteSpace($manifestVersion)) {
            $gameVersion = $manifestVersion
        }
    }
    catch {
        # The dedicated build version above is the safe compatibility fallback.
    }

    $saveFile = Get-Item -LiteralPath $savePath
    $moduleVersions = [ordered]@{}
    foreach ($moduleId in $LoadedModuleIds) {
        if (-not $installedModuleVersions.ContainsKey($moduleId) -or
            [string]::IsNullOrWhiteSpace(
                [string]$installedModuleVersions[$moduleId])) {
            throw "Could not record the installed version for module '$moduleId'."
        }
        $moduleVersions[$moduleId] = [string]$installedModuleVersions[$moduleId]
    }
    $metadata = [ordered]@{
        SchemaVersion = 2
        SaveName = $SaveName
        GameVersion = $gameVersion
        Modules = @($LoadedModuleIds)
        ModuleVersions = $moduleVersions
        SaveLength = $saveFile.Length
        SaveLastWriteTimeUtcTicks = $saveFile.LastWriteTimeUtc.Ticks
        WrittenUtc = [DateTime]::UtcNow.ToString('O')
    }
    if (-not [string]::IsNullOrWhiteSpace($Difficulty)) {
        $metadata['Difficulty'] = $Difficulty
    }
    $metadataPath =
        [System.IO.Path]::ChangeExtension($saveFile.FullName, '.launcher.json')
    Write-Utf8TextAtomically `
        -Path $metadataPath `
        -Content ($metadata | ConvertTo-Json -Depth 4)
}

function Update-RollingSaveModuleVersion {
    param(
        [Parameter(Mandatory = $true)]
        [string]$SavePath,

        [Parameter(Mandatory = $true)]
        [string]$ModuleId,

        [Parameter(Mandatory = $true)]
        [string]$InstalledVersion
    )

    if (-not (Test-Path -LiteralPath $SavePath -PathType Leaf)) {
        return $false
    }

    $resolvedSaveRoot = [System.IO.Path]::GetFullPath($serverSaveRoot).
        TrimEnd([System.IO.Path]::DirectorySeparatorChar) +
        [System.IO.Path]::DirectorySeparatorChar
    $resolvedSavePath = [System.IO.Path]::GetFullPath($SavePath)
    if (-not $resolvedSavePath.StartsWith(
            $resolvedSaveRoot,
            [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to migrate a save outside the dedicated save directory: $resolvedSavePath"
    }

    $versionMatch = [regex]::Match(
        $InstalledVersion.Trim(),
        '^(?<type>[abedv])(?<major>\d+)\.(?<minor>\d+)\.(?<revision>\d+)(?:\.(?:\d+|\*))?$',
        [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)
    if (-not $versionMatch.Success) {
        throw "Installed module '$ModuleId' has an invalid Bannerlord version: $InstalledVersion"
    }
    $targetSaveVersion = ('{0}{1}.{2}.{3}.0' -f
        $versionMatch.Groups['type'].Value.ToLowerInvariant(),
        $versionMatch.Groups['major'].Value,
        $versionMatch.Groups['minor'].Value,
        $versionMatch.Groups['revision'].Value)

    $sourceStream = $null
    $reader = $null
    $targetStream = $null
    $writer = $null
    $temporaryPath = Join-Path `
        ([System.IO.Path]::GetDirectoryName($resolvedSavePath)) `
        ('.' + [System.IO.Path]::GetFileName($resolvedSavePath) +
            '.module-version-' + [Guid]::NewGuid().ToString('N') + '.tmp')
    try {
        $sourceStream = [System.IO.File]::Open(
            $resolvedSavePath,
            [System.IO.FileMode]::Open,
            [System.IO.FileAccess]::Read,
            [System.IO.FileShare]::Read)
        $sourceLength = $sourceStream.Length
        $reader = [System.IO.BinaryReader]::new(
            $sourceStream, $strictUtf8NoBom, $true)
        $headerLength = $reader.ReadInt32()
        if ($headerLength -lt 2 -or $headerLength -gt 1048576) {
            throw "Invalid Bannerlord save metadata length: $headerLength"
        }
        $headerBytes = $reader.ReadBytes($headerLength)
        if ($headerBytes.Length -ne $headerLength) {
            throw 'The save metadata header is incomplete.'
        }

        $headerJson = $strictUtf8NoBom.GetString($headerBytes)
        $propertyName = 'Module_' + $ModuleId
        $propertyPattern = '(?<prefix>"' +
            [regex]::Escape($propertyName) +
            '"\s*:\s*)"(?<value>(?:\\.|[^"\\])*)"'
        $propertyRegex = New-Object System.Text.RegularExpressions.Regex(
            $propertyPattern,
            [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)
        $propertyMatch = $propertyRegex.Match($headerJson)
        if (-not $propertyMatch.Success) {
            throw "The save header does not contain a version for module '$ModuleId'."
        }

        $savedVersion = $propertyMatch.Groups['value'].Value
        if ((Get-BannerlordVersionIdentity -Version $savedVersion) -eq
            (Get-BannerlordVersionIdentity -Version $targetSaveVersion)) {
            return $false
        }

        $quotedTargetVersion = ConvertTo-Json $targetSaveVersion -Compress
        $updatedHeaderJson = $propertyRegex.Replace(
            $headerJson,
            {
                param($match)
                $match.Groups['prefix'].Value + $quotedTargetVersion
            },
            1)
        $updatedHeaderBytes = $strictUtf8NoBom.GetBytes($updatedHeaderJson)
        if ($updatedHeaderBytes.Length -lt 2 -or
            $updatedHeaderBytes.Length -gt 1048576) {
            throw 'The migrated save metadata length is invalid.'
        }

        $targetStream = [System.IO.File]::Open(
            $temporaryPath,
            [System.IO.FileMode]::CreateNew,
            [System.IO.FileAccess]::ReadWrite,
            [System.IO.FileShare]::None)
        $writer = [System.IO.BinaryWriter]::new(
            $targetStream, $strictUtf8NoBom, $true)
        $writer.Write([int]$updatedHeaderBytes.Length)
        $writer.Write($updatedHeaderBytes)
        $sourceStream.CopyTo($targetStream)
        $writer.Flush()
        $targetStream.Flush($true)

        $expectedLength = [long]4 + $updatedHeaderBytes.Length +
            ($sourceLength - 4 - $headerLength)
        if ($targetStream.Length -ne $expectedLength) {
            throw 'The migrated save payload length failed validation.'
        }

        $writer.Dispose()
        $writer = $null
        $targetStream.Dispose()
        $targetStream = $null
        $reader.Dispose()
        $reader = $null
        $sourceStream.Dispose()
        $sourceStream = $null

        $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
        # Keep File.Replace's recovery file beside the save so the atomic
        # replacement also works when Documents is on another drive.
        $backupRoot = Join-Path $serverSaveRoot (
            '.module-version-backups\' +
            [System.IO.Path]::GetFileNameWithoutExtension($resolvedSavePath) +
            '-' + $stamp + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
        New-Item -ItemType Directory -Path $backupRoot -Force | Out-Null
        $backupPath = Join-Path `
            $backupRoot `
            ([System.IO.Path]::GetFileName($resolvedSavePath))

        [System.IO.File]::Replace(
            $temporaryPath,
            $resolvedSavePath,
            $backupPath,
            $true)
        $temporaryPath = $null
        Write-Host "Migrated $ModuleId save metadata from $savedVersion to $targetSaveVersion."
        Write-Host "Original save backup: $backupPath"
        return $true
    }
    finally {
        if ($writer) { $writer.Dispose() }
        if ($targetStream) { $targetStream.Dispose() }
        if ($reader) { $reader.Dispose() }
        if ($sourceStream) { $sourceStream.Dispose() }
        if (-not [string]::IsNullOrWhiteSpace($temporaryPath) -and
            (Test-Path -LiteralPath $temporaryPath -PathType Leaf)) {
            Remove-Item -LiteralPath $temporaryPath -Force
        }
    }
}

function Test-SaveGameVersionLoads {
    param([string]$SaveVersion, [string]$ServerVersion)

    # Bannerlord loads a save from an earlier patch of the same game version
    # (Coop's own default_new_game.sav is v1.4.6 on a v1.4.8 server), never a newer one.
    $save = [regex]::Match([string]$SaveVersion, '^v?(\d+)\.(\d+)\.(\d+)')
    $server = [regex]::Match([string]$ServerVersion, '^v?(\d+)\.(\d+)\.(\d+)')
    if (-not $save.Success -or -not $server.Success) {
        return $false
    }
    return [int]$save.Groups[1].Value -eq [int]$server.Groups[1].Value -and
        [int]$save.Groups[2].Value -eq [int]$server.Groups[2].Value -and
        [int]$save.Groups[3].Value -le [int]$server.Groups[3].Value
}

function Get-SaveCandidates {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$LoadedModuleIds
    )

    $saveRoots = @(
        [pscustomobject]@{ Path = $serverSaveRoot; Source = 'Server' },
        [pscustomobject]@{
            Path = Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'Mount and Blade II Bannerlord\Game Saves'
            Source = 'Base game'
        },
        [pscustomobject]@{
            Path = Join-Path $env:USERPROFILE 'Documents\Mount and Blade II Bannerlord\Game Saves'
            Source = 'Base game'
        }
    )

    if ($env:OneDrive) {
        $saveRoots += [pscustomobject]@{
            Path = Join-Path $env:OneDrive 'Documents\Mount and Blade II Bannerlord\Game Saves'
            Source = 'Base game'
        }
    }

    $seenFiles = @{}
    $allCandidates = @()
    foreach ($root in $saveRoots) {
        if (-not (Test-Path -LiteralPath $root.Path)) {
            continue
        }

        foreach ($file in Get-ChildItem -LiteralPath $root.Path -File -Filter '*.sav' |
            Where-Object { Test-IsSelectablePrimarySaveName -Name $_.Name }) {
            $canonicalPath = $file.FullName.ToLowerInvariant()
            if ($seenFiles.ContainsKey($canonicalPath)) {
                continue
            }
            $seenFiles[$canonicalPath] = $true

            $info = Get-BannerlordSaveInfo -File $file -Source $root.Source
            if ($null -ne $info) {
                $allCandidates += $info
            }
        }
    }

    # Official modules are supplied by the dedicated build and are allowed to
    # differ from a graphical client. Community modules must match exactly.
    $officialModules = @('Native', 'SandBoxCore', 'Sandbox', 'CustomBattle', 'StoryMode', 'BirthAndDeath')
    # Server-only modules (DedicatedServer.Windows and add-ons such as
    # DedicatedServer.AD1259CoopCompat) take part in the live module handshake
    # but do not persist campaign data, so existing saves remain valid. Coop
    # also leaves every DedicatedServer.* module out of the client comparison.
    $nonSaveModulePattern = 'DedicatedServer.*'
    # Coop Save Rescue loads on every start as DedicatedServer.CoopSaveRescue (left out by the pattern
    # above). Older Rescue save starts loaded it as CoopSaveRescue. It stores nothing of its own, but a
    # save made during that start lists it, so it is left out on both sides like the add-ons.
    # The Coodsz patch modules (Diplomacy.CoopCompat, StuckBattleFix, Limitless.CoopCompat) only patch code and store
    # nothing either, so adding one to a running campaign, or removing it, keeps the save usable.
    # Harmony and UIExtenderEx are code libraries with no save types or campaign data. ButterLib
    # (save type definers) and MCM (per save settings) do store data, so they stay compared.
    # COOP Server Settings Lock left the setup in v1.3.4. It only patches MCM values and keeps nothing in a
    # save (no campaign or save system types), so a campaign saved with it still starts without it.
    # SaveModChange.NoSaveDataModules in C# is the same list.
    $nonSaveModuleIds = @('CoopSaveRescue', 'Diplomacy.CoopCompat', 'StuckBattleFix', 'Limitless.CoopCompat', 'Bannerlord.Harmony', 'Bannerlord.UIExtenderEx', 'ServerSettingsLock')
    $serverCommunityModules = @(
        $LoadedModuleIds |
            Where-Object { $_ -notin $officialModules -and $_ -notlike $nonSaveModulePattern -and $_ -notin $nonSaveModuleIds } |
            ForEach-Object { if ($_ -eq 'CoopNightly') { 'Coop' } else { $_ } }
    )

    $compatible = @()
    $skippedCount = 0
    foreach ($candidate in $allCandidates) {
        $saveCommunityModules = @(
            $candidate.Modules |
                Where-Object { $_ -notin $officialModules -and $_ -notlike $nonSaveModulePattern -and $_ -notin $nonSaveModuleIds } |
                ForEach-Object { if ($_ -eq 'CoopNightly') { 'Coop' } else { $_ } }
        )

        $moduleIdsMatch = $serverCommunityModules.Count -eq
            $saveCommunityModules.Count -and
            @($serverCommunityModules | Where-Object {
                $_ -notin $saveCommunityModules
            }).Count -eq 0
        # -AllowAddedModules (Coodsz Host asks the host first and keeps a backup):
        # the server may only ADD mods to a campaign. Every mod the save was made
        # with must still load. A save missing a mod the server
        # removed stays hidden, because removing a mod can break the campaign.
        $addedModules = @()
        if (-not $moduleIdsMatch -and $AllowAddedModules -and
            $serverCommunityModules.Count -gt $saveCommunityModules.Count -and
            @($saveCommunityModules | Where-Object {
                $_ -notin $serverCommunityModules
            }).Count -eq 0) {
            $addedModules = @($serverCommunityModules | Where-Object {
                $_ -notin $saveCommunityModules
            })
            $moduleIdsMatch = $addedModules.Count -gt 0
        }
        # Every mod the save was made with must load. Bannerlord itself only checks for added,
        # removed and changed mods when it loads a save, never their order, so a moved mod keeps
        # the save usable. The start only says so (ModuleOrderChanged).
        $serverSaveOrder = @($serverCommunityModules | Where-Object {
            $_ -in $saveCommunityModules
        })
        $saveModulesLoad = $moduleIdsMatch -and
            $serverSaveOrder.Count -eq $saveCommunityModules.Count
        $moduleOrderChanged = $false
        if ($saveModulesLoad -and $candidate.HasTrustedModuleOrder) {
            for ($moduleIndex = 0;
                 $moduleIndex -lt $serverSaveOrder.Count;
                 $moduleIndex++) {
                if (-not [string]::Equals(
                        [string]$serverSaveOrder[$moduleIndex],
                        [string]$saveCommunityModules[$moduleIndex],
                        [StringComparison]::OrdinalIgnoreCase)) {
                    $moduleOrderChanged = $true
                    break
                }
            }
        }
        $moduleVersionsMatch = $true
        $versionChange = $null
        if ($candidate.ModuleVersions.Count -gt 0) {
            foreach ($moduleId in $saveCommunityModules) {
                # Coop and CoopNightly count as one mod above, but the save's
                # metadata and the installed list keep each version under the id
                # that really loaded (CR-P4984 hid every nightly campaign).
                $saveVersionId = $moduleId
                $serverVersionId = $moduleId
                if ($moduleId -eq 'Coop') {
                    if ('CoopNightly' -in $candidate.Modules) { $saveVersionId = 'CoopNightly' }
                    if ('CoopNightly' -in $LoadedModuleIds) { $serverVersionId = 'CoopNightly' }
                }
                if ($moduleId -in $rollingSaveVersionModules -or
                    $serverVersionId -in $rollingSaveVersionModules) {
                    continue
                }
                if (-not $candidate.ModuleVersions.ContainsKey($saveVersionId) -or
                    -not $installedModuleVersions.ContainsKey($serverVersionId) -or
                    -not [string]::Equals(
                        [string]$candidate.ModuleVersions[$saveVersionId],
                        [string]$installedModuleVersions[$serverVersionId],
                        [StringComparison]::OrdinalIgnoreCase)) {
                    $moduleVersionsMatch = $false
                    if ($candidate.ModuleVersions.ContainsKey($saveVersionId) -and
                        $installedModuleVersions.ContainsKey($serverVersionId)) {
                        $serverVersionText = [string]$installedModuleVersions[$serverVersionId]
                        if ($serverVersionId -ne $saveVersionId) {
                            $serverVersionText = "$serverVersionId $serverVersionText"
                        }
                        $versionChange = "$saveVersionId $($candidate.ModuleVersions[$saveVersionId]) and this server has $serverVersionText"
                    }
                    break
                }
            }
        }
        $gameVersionMatches = Test-SaveGameVersionLoads `
            -SaveVersion $candidate.GameVersion `
            -ServerVersion $nativeGameVersionPrefix

        if ($gameVersionMatches -and $saveModulesLoad -and $moduleVersionsMatch) {
            $candidate.AddedModules = $addedModules
            $candidate.ModuleOrderChanged = $moduleOrderChanged
            $compatible += $candidate
        }
        else {
            $skippedCount++
            # Same mods, but one changed version: say which, so the host
            # is not told the mods differ (ServerCrashExplain reads this line).
            if ($gameVersionMatches -and $saveModulesLoad -and $null -ne $versionChange) {
                Write-Host "Save '$($candidate.Name)' was made with $versionChange, so it is hidden."
            }
        }
    }

    if ($skippedCount -gt 0) {
        Write-Host "Hidden incompatible saves: $skippedCount"
        Write-Host
    }

    return @($compatible | Sort-Object @{ Expression = { $_.File.LastWriteTime }; Descending = $true })
}

function Read-SaveSelection {
    param(
        [Parameter(Mandatory = $true)]
        [object[]]$Candidates
    )

    if ($Candidates.Count -eq 0) {
        throw 'No compatible Bannerlord Coop saves were found.'
    }

    Write-Host 'Choose a campaign save to host:'
    Write-Host
    for ($index = 0; $index -lt $Candidates.Count; $index++) {
        $candidate = $Candidates[$index]
        $sidecarState = if (Test-Path -LiteralPath $candidate.SidecarPath) { 'coop data ready' } else { 'no coop data' }
        Write-Host ('  [{0,2}] {1,-22} {2,-14} day {3,-5} {4,-9} {5}' -f
            ($index + 1),
            $candidate.Name,
            $candidate.Character,
            $candidate.Day,
            $candidate.Source,
            $sidecarState)
    }

    Write-Host
    while ($true) {
        $answer = (Read-Host 'Enter a save number, or Q to quit').Trim()
        if ($answer -match '^(?i:q|quit|exit)$') {
            return $null
        }

        $selectedNumber = 0
        if ([int]::TryParse($answer, [ref]$selectedNumber) -and
            $selectedNumber -ge 1 -and
            $selectedNumber -le $Candidates.Count) {
            return $Candidates[$selectedNumber - 1]
        }

        Write-Host "Please enter a number from 1 to $($Candidates.Count), or Q."
    }
}

function Import-SelectedSave {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Selection
    )

    if ($Selection.Source -eq 'Server') {
        return $Selection.Name
    }

    New-Item -ItemType Directory -Path $serverSaveRoot -Force | Out-Null
    $targetSave = Join-Path $serverSaveRoot ($Selection.Name + '.sav')
    $targetSidecar = Join-Path $serverSaveRoot ($Selection.Name + '.json')
    $targetLauncherMetadata =
        [System.IO.Path]::ChangeExtension($targetSave, '.launcher.json')
    $sourceSidecarExists = Test-Path -LiteralPath $Selection.SidecarPath
    $sourceLauncherMetadataExists =
        Test-Path -LiteralPath $Selection.LauncherMetadataPath

    $needsBackup = Test-Path -LiteralPath $targetSave
    if ($needsBackup) {
        $sourceHash = (Get-FileHash -LiteralPath $Selection.File.FullName -Algorithm SHA256).Hash
        $targetHash = (Get-FileHash -LiteralPath $targetSave -Algorithm SHA256).Hash
        $needsBackup = $sourceHash -ne $targetHash
    }

    if ($needsBackup) {
        $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
        $backupRoot = Join-Path $serverRoot "server-data\save-import-backups\$($Selection.Name)-$stamp"
        New-Item -ItemType Directory -Path $backupRoot -Force | Out-Null
        Copy-Item -LiteralPath $targetSave -Destination (Join-Path $backupRoot ($Selection.Name + '.sav'))
        if (Test-Path -LiteralPath $targetSidecar) {
            Copy-Item -LiteralPath $targetSidecar -Destination (Join-Path $backupRoot ($Selection.Name + '.json'))
        }
        if (Test-Path -LiteralPath $targetLauncherMetadata) {
            Copy-Item `
                -LiteralPath $targetLauncherMetadata `
                -Destination (Join-Path $backupRoot ($Selection.Name + '.launcher.json'))
        }
        Write-Host "Backed up the previous server copy to: $backupRoot"
    }

    Copy-Item -LiteralPath $Selection.File.FullName -Destination $targetSave -Force
    if ($sourceSidecarExists) {
        Copy-Item -LiteralPath $Selection.SidecarPath -Destination $targetSidecar -Force
    }
    elseif (Test-Path -LiteralPath $targetSidecar) {
        # Do not associate an unrelated server-side player mapping with a newly
        # imported campaign. The previous copy was preserved above if needed.
        Remove-Item -LiteralPath $targetSidecar -Force
    }
    if ($sourceLauncherMetadataExists) {
        Copy-Item `
            -LiteralPath $Selection.LauncherMetadataPath `
            -Destination $targetLauncherMetadata `
            -Force
    }
    elseif (Test-Path -LiteralPath $targetLauncherMetadata) {
        Remove-Item -LiteralPath $targetLauncherMetadata -Force
    }

    Write-Host "Imported '$($Selection.Name)' from the base game saves."
    return $Selection.Name
}

function Repair-OversizedServerConfig {
    $configFile = Get-Item -LiteralPath $serverConfigPath
    if ($configFile.Length -le 1048576) {
        return
    }

    # Older launch scripts decoded this BOM-less UTF-8 file as the Windows
    # ANSI code page and then wrote it back as UTF-8. Each launch expanded the
    # first non-ASCII comment again. Recover the intact settings block from the
    # final 1 MiB without loading the corrupt file into memory.
    $stream = [System.IO.File]::Open(
        $serverConfigPath,
        [System.IO.FileMode]::Open,
        [System.IO.FileAccess]::Read,
        [System.IO.FileShare]::ReadWrite
    )
    try {
        $bytesToRead = [int][Math]::Min(1048576, $stream.Length)
        [void]$stream.Seek(-$bytesToRead, [System.IO.SeekOrigin]::End)
        $tailBytes = New-Object byte[] $bytesToRead
        $read = $stream.Read($tailBytes, 0, $tailBytes.Length)
        if ($read -ne $bytesToRead) {
            throw 'Could not read the server configuration recovery window.'
        }
    }
    finally {
        $stream.Dispose()
    }

    $tailText = [System.Text.Encoding]::UTF8.GetString($tailBytes)
    $saveNameMarker = $tailText.IndexOf('"saveName"', [System.StringComparison]::Ordinal)
    $closingBrace = $tailText.LastIndexOf('}')
    if ($saveNameMarker -lt 0 -or $closingBrace -le $saveNameMarker) {
        throw 'server-config.json is oversized and its settings block could not be recovered.'
    }

    $lineStart = $tailText.LastIndexOf("`n", $saveNameMarker)
    if ($lineStart -lt 0) {
        $lineStart = 0
    }
    else {
        $lineStart++
    }

    $settingsBlock = $tailText.Substring(
        $lineStart,
        $closingBrace - $lineStart + 1
    ).TrimStart()
    $repairedConfig = @"
{
  // Recovered automatically after legacy UTF-8 comment corruption.
$settingsBlock
"@
    if ($repairedConfig -notmatch '(?m)^\s*"saveName"\s*:' -or
        $repairedConfig -notmatch '(?m)^\s*"password"\s*:') {
        throw 'The recovered server configuration is missing required settings.'
    }

    $strictUtf8 = New-Object System.Text.UTF8Encoding($false, $true)
    $temporaryConfig = $serverConfigPath + '.recovery.tmp'
    $corruptConfig = $serverConfigPath + '.legacy-encoding-corruption'
    [System.IO.File]::WriteAllText($temporaryConfig, $repairedConfig, $strictUtf8)
    Move-Item -LiteralPath $serverConfigPath -Destination $corruptConfig -Force
    try {
        Move-Item -LiteralPath $temporaryConfig -Destination $serverConfigPath -Force
        $verified = [System.IO.File]::ReadAllText($serverConfigPath, $strictUtf8)
        if ($verified -notmatch '(?m)^\s*"saveName"\s*:') {
            throw 'The repaired server configuration failed validation.'
        }
        Remove-Item -LiteralPath $corruptConfig -Force
        Write-Host "Repaired legacy UTF-8 corruption in server-config.json ($($configFile.Length) bytes)."
    }
    catch {
        if (Test-Path -LiteralPath $corruptConfig) {
            Move-Item -LiteralPath $corruptConfig -Destination $serverConfigPath -Force
        }
        throw
    }
}

function Set-ConfiguredSaveName {
    param(
        [Parameter(Mandatory = $true)]
        [string]$SaveName
    )

    if (-not (Test-Path -LiteralPath $serverConfigPath)) {
        throw "Server configuration is missing: $serverConfigPath"
    }

    Repair-OversizedServerConfig
    $configFile = Get-Item -LiteralPath $serverConfigPath
    if ($configFile.Length -gt 1048576) {
        throw "Server configuration is unexpectedly large ($($configFile.Length) bytes). Refusing to rewrite a potentially corrupted file: $serverConfigPath"
    }

    # Windows PowerShell 5.1 treats BOM-less UTF-8 as the system ANSI code
    # page when Get-Content is used without an encoding. Rewriting that text
    # repeatedly expands non-ASCII comments into exponentially growing
    # mojibake. .NET's UTF-8 reader keeps the file byte-stable on every launch.
    $strictUtf8 = New-Object System.Text.UTF8Encoding($false, $true)
    $configText = [System.IO.File]::ReadAllText($serverConfigPath, $strictUtf8)
    $jsonSaveName = ConvertTo-Json $SaveName -Compress
    $pattern = '(?m)^(\s*"saveName"\s*:\s*)"(?:\\.|[^"\\])*"(\s*,?)'
    $regex = New-Object System.Text.RegularExpressions.Regex($pattern)
    if (-not $regex.IsMatch($configText)) {
        throw "Could not find the saveName setting in $serverConfigPath"
    }

    $updatedConfig = $regex.Replace(
        $configText,
        {
            param($match)
            $match.Groups[1].Value + $jsonSaveName + $match.Groups[2].Value
        },
        1
    )

    Write-Utf8TextAtomically -Path $serverConfigPath -Content $updatedConfig
}

function Set-ConfiguredDifficultyPreset {
    param(
        [Parameter(Mandatory = $true)]
        [ValidateSet('Freebooter', 'Warrior', 'Bannerlord')]
        [string]$Preset
    )

    $configFile = Get-Item -LiteralPath $modConfigPath
    if ($configFile.Length -gt 1048576) {
        throw "mod-config.json is unexpectedly large ($($configFile.Length) bytes): $modConfigPath"
    }
    $configText = [System.IO.File]::ReadAllText(
        $modConfigPath,
        $strictUtf8NoBom
    )
    $originalConfigText = $configText
    $standardLevel = switch ($Preset) {
        'Freebooter' { 'VeryEasy' }
        'Warrior' { 'Easy' }
        'Bannerlord' { 'Realistic' }
    }
    $difficultyKeys = @(
        'playerReceivedDamage',
        'playerTroopsReceivedDamage',
        'combatAIDifficulty',
        'recruitmentDifficulty',
        'playerMapMovementSpeed',
        'stealthAndDisguiseDifficulty',
        'persuasionSuccessChance',
        'clanMemberDeathChance',
        'battleDeath'
    )

    $difficultyMatch = [regex]::Match(
        $configText,
        '(?i)"difficulty"\s*:\s*\{'
    )
    if (-not $difficultyMatch.Success) {
        $openingBrace = $configText.IndexOf('{')
        if ($openingBrace -lt 0) {
            throw 'mod-config.json has no root object.'
        }
        $difficultyBlock = [Environment]::NewLine +
            '  "difficulty": {' + [Environment]::NewLine +
            '  },'
        $configText = $configText.Insert($openingBrace + 1, $difficultyBlock)
        $difficultyMatch = [regex]::Match(
            $configText,
            '(?i)"difficulty"\s*:\s*\{'
        )
    }

    foreach ($key in $difficultyKeys) {
        # Bannerlord's own Bannerlord preset keeps stealth/disguise at Easy even
        # though its other preset-related settings use Realistic.
        $level = if ($Preset -eq 'Bannerlord' -and
            $key -eq 'stealthAndDisguiseDifficulty') {
            'Easy'
        }
        else {
            $standardLevel
        }
        $jsonLevel = ConvertTo-Json $level -Compress
        $pattern = '(?m)^(\s*"' + [regex]::Escape($key) + '"\s*:\s*)"(?:\\.|[^"\\])*"(\s*,?)'
        $regex = New-Object System.Text.RegularExpressions.Regex($pattern)
        if ($regex.IsMatch($configText)) {
            $configText = $regex.Replace(
                $configText,
                {
                    param($match)
                    $match.Groups[1].Value + $jsonLevel + $match.Groups[2].Value
                },
                1
            )
        }
        else {
            $line = [Environment]::NewLine + '    "' + $key + '": ' +
                $jsonLevel + ','
            $configText = $configText.Insert(
                $difficultyMatch.Index + $difficultyMatch.Length,
                $line
            )
        }
    }

    Write-Utf8TextAtomically -Path $modConfigPath -Content $configText
    return $originalConfigText
}

function Get-CoopBooleanModOption {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Key,

        [Parameter(Mandatory = $true)]
        [bool]$Default
    )

    if (-not (Test-Path -LiteralPath $modConfigPath -PathType Leaf)) {
        return $Default
    }
    $configText = [System.IO.File]::ReadAllText($modConfigPath, $strictUtf8NoBom)
    $match = [regex]::Match(
        $configText,
        '(?m)^\s*"' + [regex]::Escape($Key) + '"\s*:\s*(?<value>true|false)',
        [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)
    if (-not $match.Success) {
        return $Default
    }
    return $match.Groups['value'].Value -ieq 'true'
}

function Set-CoopBooleanModOption {
    param(
        [Parameter(Mandatory = $true)]
        [ValidateSet('fastForwardEnabled', 'clientsCanUseCheats')]
        [string]$Key,

        [Parameter(Mandatory = $true)]
        [bool]$Enabled
    )

    $configFile = Get-Item -LiteralPath $modConfigPath
    if ($configFile.Length -gt 1048576) {
        throw "mod-config.json is unexpectedly large ($($configFile.Length) bytes): $modConfigPath"
    }

    $configText = [System.IO.File]::ReadAllText(
        $modConfigPath,
        $strictUtf8NoBom)
    $setting = if ($Enabled) { 'true' } else { 'false' }
    $pattern = '(?m)^(?<prefix>\s*"' + [regex]::Escape($Key) +
        '"\s*:\s*)(?<value>true|false)(?<suffix>\s*,?)'
    $regex = New-Object System.Text.RegularExpressions.Regex(
        $pattern,
        [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)
    if ($regex.IsMatch($configText)) {
        $updated = $regex.Replace(
            $configText,
            {
                param($match)
                $match.Groups['prefix'].Value + $setting +
                    $match.Groups['suffix'].Value
            },
            1)
    }
    else {
        $modOptions = [regex]::Match(
            $configText,
            '(?i)"modOptions"\s*:\s*\{')
        if (-not $modOptions.Success) {
            throw 'mod-config.json has no modOptions object.'
        }
        $updated = $configText.Insert(
            $modOptions.Index + $modOptions.Length,
            [Environment]::NewLine +
                '    "' + $Key + '": ' + $setting + ',')
    }

    if ($updated -ne $configText) {
        Write-Utf8TextAtomically -Path $modConfigPath -Content $updated
    }
}

Ensure-OfficialDataLayout

foreach ($requiredPath in @(
    $modulesRoot,
    $serverBin,
    $dotnetPath,
    $starterDll,
    $moduleListPath,
    $modulePolicyPath,
    $serverConfigPath,
    $modConfigPath,
    $serverSaveRoot,
    $runtimeBootstrapPath
)) {
    if (-not (Test-Path -LiteralPath $requiredPath)) {
        throw "Required server file or directory is missing: $requiredPath"
    }
}

try {
    $modulePolicyText = [System.IO.File]::ReadAllText(
        $modulePolicyPath,
        $strictUtf8NoBom)
    $modulePolicy = ConvertFrom-Json -InputObject $modulePolicyText
}
catch {
    throw "Could not read server module policy '$modulePolicyPath': $($_.Exception.Message)"
}
function Get-PolicyStringArray {
    param([Parameter(Mandatory = $true)][string]$PropertyName)

    $property = $modulePolicy.PSObject.Properties[$PropertyName]
    if ($null -eq $property -or $null -eq $property.Value) {
        return @()
    }
    if ($property.Value -is [string] -or
        $property.Value -isnot [System.Collections.IEnumerable]) {
        throw "server-module-policy.json $PropertyName must be an array of strings."
    }
    return @(
        $property.Value |
            ForEach-Object {
                if ($_ -isnot [string]) {
                    throw "server-module-policy.json $PropertyName must contain only strings."
                }
                ([string]$_).Trim()
            } |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
            Select-Object -Unique
    )
}

$policySchemaVersion = 0
$policySchemaProperty = $modulePolicy.PSObject.Properties['policySchemaVersion']
if ($null -ne $policySchemaProperty) {
    if ($policySchemaProperty.Value -isnot [int] -and
        $policySchemaProperty.Value -isnot [long]) {
        throw 'server-module-policy.json policySchemaVersion must be an integer.'
    }
    $policySchemaVersion = [int]$policySchemaProperty.Value
    if ($policySchemaVersion -lt 1 -or $policySchemaVersion -gt 3) {
        throw "Unsupported server-module-policy.json policySchemaVersion: $policySchemaVersion"
    }
}

function Write-ManagedRuntimeIdentity {
    param(
        [Parameter(Mandatory = $true)][string]$Label,
        [Parameter(Mandatory = $true)][string]$Path
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        Write-Warning "${Label}: assembly is missing at '$Path'."
        return
    }

    $versionInfo = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($Path)
    $version = [string]$versionInfo.ProductVersion
    if ([string]::IsNullOrWhiteSpace($version)) {
        $version = [string]$versionInfo.FileVersion
    }
    if ([string]::IsNullOrWhiteSpace($version)) {
        $version = 'unknown'
    }
    $hash = Get-Sha256Hex -Path $Path
    Write-Host "${Label}: $version (SHA256 $($hash.Substring(0, 12)))"
}

$allowUnsupportedMods = $true
$allowUnsupportedProperty =
    $modulePolicy.PSObject.Properties['allowUnsupportedMods']
if ($null -ne $allowUnsupportedProperty) {
    if ($allowUnsupportedProperty.Value -isnot [bool]) {
        throw 'server-module-policy.json allowUnsupportedMods must be true or false.'
    }
    $allowUnsupportedMods = [bool]$allowUnsupportedProperty.Value
}

$forceAllSelectedGameplayModulesHeadless = $allowUnsupportedMods
$forceAllProperty =
    $modulePolicy.PSObject.Properties['forceAllSelectedGameplayModulesHeadless']
if ($null -ne $forceAllProperty) {
    if ($forceAllProperty.Value -isnot [bool]) {
        throw 'server-module-policy.json forceAllSelectedGameplayModulesHeadless must be true or false.'
    }
    if ($policySchemaVersion -ge 2) {
        $forceAllSelectedGameplayModulesHeadless =
            $allowUnsupportedMods -or [bool]$forceAllProperty.Value
    }
    elseif ([bool]$forceAllProperty.Value) {
        Write-Warning 'Ignored the legacy force-all-headless default. Add policySchemaVersion 2 only if every selected module is intentionally safe to force headless.'
    }
}
$blockManagedAssemblyCollisions = $false
$blockCollisionsProperty =
    $modulePolicy.PSObject.Properties['blockManagedAssemblyCollisions']
if ($null -ne $blockCollisionsProperty) {
    if ($blockCollisionsProperty.Value -isnot [bool]) {
        throw 'server-module-policy.json blockManagedAssemblyCollisions must be true or false.'
    }
    $blockManagedAssemblyCollisions = [bool]$blockCollisionsProperty.Value
}
$forceHeadlessModuleIds = @(Get-PolicyStringArray 'forceHeadlessModules')
$rollingSaveVersionModules = @(Get-PolicyStringArray 'rollingSaveVersionModules')
$strictDependencyValidation = $false
$strictDependencyProperty =
    $modulePolicy.PSObject.Properties['strictDependencyValidation']
if ($null -ne $strictDependencyProperty) {
    if ($strictDependencyProperty.Value -isnot [bool]) {
        throw 'server-module-policy.json strictDependencyValidation must be true or false.'
    }
    $strictDependencyValidation = [bool]$strictDependencyProperty.Value
}
$configuredSoftDependencyModules = @(Get-PolicyStringArray 'softDependencyModules')
$allowedAssemblyCollisions = @(Get-PolicyStringArray 'allowedAssemblyCollisions')
foreach ($policyModuleId in @(
        $forceHeadlessModuleIds + $rollingSaveVersionModules +
        $configuredSoftDependencyModules + $allowedAssemblyCollisions)) {
    if ($policyModuleId -cnotmatch '^[A-Za-z0-9._-]+$') {
        throw "Invalid policy identifier '$policyModuleId' in $modulePolicyPath"
    }
}

Assert-CoreCampaignRuntimeVersions
$nativeGameVersionPrefix = Get-NativeGameVersionPrefix

$moduleIds = @(
    [System.IO.File]::ReadAllLines($moduleListPath, $strictUtf8NoBom) |
        ForEach-Object { $_.Trim() } |
        Where-Object { $_ -and -not $_.StartsWith('#') }
)

if ($moduleIds.Count -eq 0) {
    throw "No module IDs were found in $moduleListPath"
}

# DedicatedServer.Windows is launcher infrastructure rather than a selectable
# gameplay mod. Its manifest requires Native, while its constructor must run
# before Sandbox/gameplay/map submodules. Pin it immediately after Native so the
# UI, saved list, validation, and final engine token all use one order.
$serverHostModuleId = 'DedicatedServer.Windows'
if ($serverHostModuleId -in $moduleIds) {
    $normalizedOrder = [System.Collections.Generic.List[string]]::new()
    foreach ($moduleId in $moduleIds) {
        if ($moduleId -ne $serverHostModuleId) {
            $normalizedOrder.Add($moduleId)
        }
    }
    $nativeIndex = $normalizedOrder.IndexOf('Native')
    if ($nativeIndex -lt 0) {
        throw 'DedicatedServer.Windows requires Native, but Native is not in the load order.'
    }
    $normalizedOrder.Insert($nativeIndex + 1, $serverHostModuleId)
    $moduleIds = @($normalizedOrder)
}

$duplicates = @($moduleIds | Group-Object | Where-Object Count -gt 1)
if ($duplicates.Count -gt 0) {
    throw "Duplicate module IDs: $($duplicates.Name -join ', ')"
}

$requiredModuleIds = @(
    'Native'
    'SandBoxCore'
    'Sandbox'
    'Coop'
    'DedicatedServer.Windows'
)

$headlessFrameworkModuleIds = @(
    'Bannerlord.Harmony'
    'Bannerlord.ButterLib'
    'Bannerlord.UIExtenderEx'
    'Bannerlord.MBOptionScreen'
)
$providedHarmonyPath = Join-Path $serverBin '0Harmony.dll'
if ('Bannerlord.Harmony' -in $moduleIds -and
    (Test-Path -LiteralPath $providedHarmonyPath)) {
    $requiredModuleIds += 'Bannerlord.Harmony'
}

$moduleLabels = @{
    'Bannerlord.Harmony'        = 'Harmony'
    'Bannerlord.ButterLib'      = 'ButterLib'
    'Bannerlord.UIExtenderEx'   = 'UI Extender'
    'Bannerlord.MBOptionScreen' = 'MCM'
}
# Client game-mode modules that workshop mods often list as required even though
# they are not part of the dedicated-server engine. Treat them as soft metadata
# only so Europe1100/Empire1100-style packs do not force StoryMode/CustomBattle
# into the headless load and crash (exit 4 / native -5324-class failures).
$nonServerOfficialModuleIds = @('CustomBattle', 'StoryMode', 'BirthAndDeath')
$softDependencyModuleIds = @(
    $nonServerOfficialModuleIds + $configuredSoftDependencyModules |
        Select-Object -Unique
)

$installedModules = @{}
$installedModuleVersions = @{}
$moduleDependencies = @{}
$moduleRequiredRelations = @{}
$moduleLoadAfter = @{}
$moduleIncompatibilities = @{}
$moduleVersionRequirements = @{}
foreach ($moduleDirectory in Get-ChildItem -LiteralPath $modulesRoot -Directory) {
    $manifestPath = Join-Path $moduleDirectory.FullName 'SubModule.xml'
    if (-not (Test-Path -LiteralPath $manifestPath)) {
        continue
    }

    try {
        $manifest = Read-SafeXmlFile -Path $manifestPath
        $moduleId = ([string]$manifest.Module.Id.value).Trim()
        if ([string]::IsNullOrWhiteSpace($moduleId)) {
            continue
        }
        if ($installedModules.ContainsKey($moduleId)) {
            throw "Duplicate installed module ID '$moduleId' in '$($installedModules[$moduleId])' and '$($moduleDirectory.FullName)'."
        }

        $installedModules[$moduleId] = $moduleDirectory.FullName
        $installedModuleVersions[$moduleId] =
            ([string]$manifest.Module.Version.value).Trim()
        $requiredDependencies = @(
            @($manifest.SelectNodes(
                    "/*[local-name()='Module']/*[local-name()='DependedModules']/*[local-name()='DependedModule']")) |
                Where-Object {
                    -not [string]::Equals(
                        [string]$_.GetAttribute('Optional'),
                        'true',
                        [StringComparison]::OrdinalIgnoreCase)
                } |
                ForEach-Object { ([string]$_.GetAttribute('Id')).Trim() } |
                Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
                Select-Object -Unique
        )
        $versionRequirements = @(
            @($manifest.SelectNodes(
                    "/*[local-name()='Module']/*[local-name()='DependedModules']/*[local-name()='DependedModule']")) |
                ForEach-Object {
                    $dependencyId = ([string]$_.GetAttribute('Id')).Trim()
                    $requiredVersion =
                        ([string]$_.GetAttribute('DependentVersion')).Trim()
                    if (-not [string]::IsNullOrWhiteSpace($dependencyId) -and
                        -not [string]::IsNullOrWhiteSpace(
                            (Get-BannerlordVersionIdentity `
                                -Version $requiredVersion))) {
                        [pscustomobject]@{
                            ModuleId = $dependencyId
                            RequiredVersion = $requiredVersion
                        }
                    }
                }
        )
        # Keep ordering metadata for client game-modes, but never treat their
        # absence as a hard dedicated-server dependency.
        $requiredRelations = @(
            $requiredDependencies | Where-Object {
                $_ -notin $softDependencyModuleIds
            })
        $loadAfterIds = @(
            @($manifest.SelectNodes(
                    "/*[local-name()='Module']/*[local-name()='ModulesToLoadAfterThis']/*[local-name()='Module']")) |
                ForEach-Object {
                    $value = [string]$_.GetAttribute('Id')
                    if ([string]::IsNullOrWhiteSpace($value)) {
                        $value = [string]$_.GetAttribute('id')
                    }
                    $value.Trim()
                } |
                Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
        )
        foreach ($metadata in @($manifest.SelectNodes(
                     "/*[local-name()='Module']/*[local-name()='DependedModuleMetadatas']/*[local-name()='DependedModuleMetadata']"))) {
            $dependencyId = [string]$metadata.GetAttribute('id')
            if ([string]::IsNullOrWhiteSpace($dependencyId)) {
                $dependencyId = [string]$metadata.GetAttribute('Id')
            }
            $dependencyId = $dependencyId.Trim()
            if ([string]::IsNullOrWhiteSpace($dependencyId)) {
                continue
            }
            $optional = [string]::Equals(
                [string]$metadata.GetAttribute('optional'),
                'true',
                [StringComparison]::OrdinalIgnoreCase)
            if (-not $optional -and
                $dependencyId -notin $softDependencyModuleIds) {
                $requiredRelations += $dependencyId
            }
            $order = [string]$metadata.GetAttribute('order')
            if ($order -eq 'LoadBeforeThis') {
                $requiredDependencies += $dependencyId
            }
            elseif ($order -eq 'LoadAfterThis') {
                $loadAfterIds += $dependencyId
            }
        }
        $incompatibleIds = @(
            @($manifest.SelectNodes(
                    "/*[local-name()='Module']/*[local-name()='IncompatibleModules']/*[local-name()='Module']")) |
                ForEach-Object {
                    $value = [string]$_.GetAttribute('Id')
                    if ([string]::IsNullOrWhiteSpace($value)) {
                        $value = [string]$_.GetAttribute('id')
                    }
                    $value.Trim()
                } |
                Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
        )
        if ($requiredDependencies.Count -gt 0) {
            $moduleDependencies[$moduleId] = @(
                $requiredDependencies | Select-Object -Unique)
        }
        if ($requiredRelations.Count -gt 0) {
            $moduleRequiredRelations[$moduleId] = @(
                $requiredRelations | Select-Object -Unique)
        }
        if ($loadAfterIds.Count -gt 0) {
            $moduleLoadAfter[$moduleId] = @(
                $loadAfterIds | Select-Object -Unique)
        }
        if ($incompatibleIds.Count -gt 0) {
            $moduleIncompatibilities[$moduleId] = @(
                $incompatibleIds | Select-Object -Unique)
        }
        if ($versionRequirements.Count -gt 0) {
            $moduleVersionRequirements[$moduleId] = @($versionRequirements)
        }
    }
    catch {
        throw "Could not read module manifest '$manifestPath': $($_.Exception.Message)"
    }
}

$selection = [ordered]@{}
if (Test-Path -LiteralPath $selectionPath) {
    try {
        $selectionText = [System.IO.File]::ReadAllText(
            $selectionPath,
            $strictUtf8NoBom
        )
        $savedSelection = ConvertFrom-Json -InputObject $selectionText
        foreach ($property in $savedSelection.PSObject.Properties) {
            $selection[$property.Name] = [bool]$property.Value
        }
    }
    catch {
        Write-Warning "Could not read $selectionPath; selectable modules will default to Enabled."
    }
}

$selectableModuleIds = @($moduleIds | Where-Object {
        $_ -notin $requiredModuleIds -and
        $_ -notin $nonServerOfficialModuleIds
    })
foreach ($moduleId in $selectableModuleIds) {
    if (-not $selection.Contains($moduleId)) {
        $selection[$moduleId] = $true
    }
}
# Never load client-only official game modes on the dedicated server, even if an
# older selection.json or workshop copy left them enabled.
foreach ($moduleId in $nonServerOfficialModuleIds) {
    if ($selection.Contains($moduleId)) {
        $selection[$moduleId] = $false
    }
}

function Get-ModuleLabel {
    param([string]$ModuleId)

    if ($moduleLabels.ContainsKey($ModuleId)) {
        return $moduleLabels[$ModuleId]
    }
    return $ModuleId
}

function Format-ModuleState {
    param(
        [string]$ModuleId,
        [bool]$Enabled
    )

    $label = Get-ModuleLabel -ModuleId $ModuleId
    $state = if ($Enabled) { 'Enabled' } else { 'Disabled' }
    return "$label [Yes] [No] - $state"
}

$shouldPromptForModules = -not $NoPrompt -and -not [Console]::IsInputRedirected
if ($shouldPromptForModules) {
    Clear-Host
    Write-Host 'Bannerlord Coop - choose modules to load'
    Write-Host 'Type Y or N, or press Enter to keep the current setting.'
    Write-Host

    foreach ($moduleId in $selectableModuleIds) {
        $current = [bool]$selection[$moduleId]

        while ($true) {
            Write-Host (Format-ModuleState -ModuleId $moduleId -Enabled $current)
            $defaultChoice = if ($current) { 'Y' } else { 'N' }
            $answer = (Read-Host "Choose Y/N (Enter keeps $defaultChoice)").Trim()

            if ([string]::IsNullOrEmpty($answer)) {
                break
            }
            if ($answer -match '^(y|yes)$') {
                $current = $true
                break
            }
            if ($answer -match '^(n|no)$') {
                $current = $false
                break
            }

            Write-Host 'Please enter Y, N, Yes, or No.' -ForegroundColor Yellow
        }

        $selection[$moduleId] = $current
        Write-Host
    }
}

# Strict mode mirrors the stock launcher by enabling every manifest dependency.
# Compatibility mode leaves the operator's module selection unchanged because
# many client-oriented mods over-declare Story/UI/tournament dependencies.
if ($strictDependencyValidation) {
    $dependencyChanges = [System.Collections.Generic.List[string]]::new()
    $changed = $true
    while ($changed) {
        $changed = $false
        foreach ($moduleId in $selectableModuleIds) {
            if (-not [bool]$selection[$moduleId] -or
                -not $moduleRequiredRelations.ContainsKey($moduleId)) {
                continue
            }

            foreach ($dependencyId in $moduleRequiredRelations[$moduleId]) {
                if ($dependencyId -in $selectableModuleIds -and
                    -not [bool]$selection[$dependencyId]) {
                    $selection[$dependencyId] = $true
                    $dependencyChanges.Add(
                        "$dependencyId (required by $moduleId)")
                    $changed = $true
                }
            }
        }
    }

    if ($dependencyChanges.Count -gt 0) {
        Write-Host 'Required dependencies automatically enabled:' -ForegroundColor Yellow
        foreach ($change in $dependencyChanges) {
            Write-Host "  $change"
        }
        Write-Host
    }
}

Write-Utf8TextAtomically `
    -Path $selectionPath `
    -Content ($selection | ConvertTo-Json)

$activeModuleIds = @(
    $moduleIds | Where-Object {
        ($_ -in $requiredModuleIds -or [bool]$selection[$_]) -and
        $_ -notin $nonServerOfficialModuleIds
    }
)

$missingModules = @($activeModuleIds | Where-Object {
        -not $installedModules.ContainsKey($_)
    })
if ($missingModules.Count -gt 0) {
    throw "Modules missing from engine\Modules: $($missingModules -join ', ')"
}

$dependencyCompatibilityWarnings =
    [System.Collections.Generic.HashSet[string]]::new(
        [StringComparer]::OrdinalIgnoreCase)
function Write-ModuleDependencyCompatibilityWarning {
    param([Parameter(Mandatory = $true)][string]$Message)

    if ($dependencyCompatibilityWarnings.Add($Message)) {
        Write-Warning "Compatibility mode: $Message"
    }
}

foreach ($moduleId in $activeModuleIds) {
    if (-not $moduleDependencies.ContainsKey($moduleId)) {
        continue
    }
    foreach ($dependencyId in $moduleDependencies[$moduleId]) {
        $dependencyIsRequired =
            $moduleRequiredRelations.ContainsKey($moduleId) -and
            $dependencyId -in $moduleRequiredRelations[$moduleId]
        if (-not $installedModules.ContainsKey($dependencyId) -or
            $dependencyId -notin $activeModuleIds) {
            if ($dependencyIsRequired) {
                $warning =
                    "Module '$moduleId' declares missing or disabled module '$dependencyId'."
                if ($strictDependencyValidation) {
                    throw $warning
                }
                Write-ModuleDependencyCompatibilityWarning -Message $warning
            }
            continue
        }
        if ([array]::IndexOf($activeModuleIds, $dependencyId) -gt
            [array]::IndexOf($activeModuleIds, $moduleId)) {
            $warning = "Module '$dependencyId' declares that it must load before '$moduleId'."
            if ($strictDependencyValidation) {
                throw $warning
            }
            Write-ModuleDependencyCompatibilityWarning -Message $warning
        }
    }
}

foreach ($moduleId in $activeModuleIds) {
    if ($moduleRequiredRelations.ContainsKey($moduleId)) {
        foreach ($requiredId in $moduleRequiredRelations[$moduleId]) {
            if (-not $installedModules.ContainsKey($requiredId)) {
                $warning =
                    "Module '$moduleId' declares missing installed module '$requiredId'."
                if ($strictDependencyValidation) {
                    throw $warning
                }
                Write-ModuleDependencyCompatibilityWarning -Message $warning
            }
            elseif ($requiredId -notin $activeModuleIds) {
                $warning =
                    "Module '$moduleId' declares disabled module '$requiredId'."
                if ($strictDependencyValidation) {
                    throw $warning
                }
                Write-ModuleDependencyCompatibilityWarning -Message $warning
            }
        }
    }
    if ($moduleLoadAfter.ContainsKey($moduleId)) {
        foreach ($afterId in $moduleLoadAfter[$moduleId]) {
            if ($afterId -in $activeModuleIds -and
                [array]::IndexOf($activeModuleIds, $afterId) -lt
                [array]::IndexOf($activeModuleIds, $moduleId)) {
                $warning =
                    "Module '$moduleId' declares that it must load before '$afterId'."
                if ($strictDependencyValidation) {
                    throw $warning
                }
                Write-ModuleDependencyCompatibilityWarning -Message $warning
            }
        }
    }
    if ($moduleIncompatibilities.ContainsKey($moduleId)) {
        foreach ($incompatibleId in $moduleIncompatibilities[$moduleId]) {
            if ($incompatibleId -in $activeModuleIds) {
                $warning =
                    "Module '$moduleId' declares incompatibility with active module '$incompatibleId'."
                if ($strictDependencyValidation) {
                    throw $warning
                }
                Write-ModuleDependencyCompatibilityWarning -Message $warning
            }
        }
    }
}

# Bannerlord's launcher warns (but does not block) when a dependency's
# type/major/minor/revision differs; the final changeset component is ignored.
# Keep the same behavior so compatible newer framework builds are not silently
# rejected while operators still see the exact compatibility risk.
foreach ($moduleId in $activeModuleIds) {
    if (-not $moduleVersionRequirements.ContainsKey($moduleId)) {
        continue
    }
    foreach ($requirement in $moduleVersionRequirements[$moduleId]) {
        $dependencyId = [string]$requirement.ModuleId
        if ($dependencyId -notin $activeModuleIds -or
            -not $installedModuleVersions.ContainsKey($dependencyId)) {
            continue
        }
        $requiredIdentity = Get-BannerlordVersionIdentity `
            -Version ([string]$requirement.RequiredVersion)
        $installedIdentity = Get-BannerlordVersionIdentity `
            -Version ([string]$installedModuleVersions[$dependencyId])
        if (-not [string]::IsNullOrWhiteSpace($requiredIdentity) -and
            -not [string]::IsNullOrWhiteSpace($installedIdentity) -and
            $requiredIdentity -ne $installedIdentity) {
            Write-Warning "Module '$moduleId' targets '$dependencyId' $($requirement.RequiredVersion); installed is $($installedModuleVersions[$dependencyId]). Bannerlord permits this mismatch, but the mod must support that dependency build."
        }
    }
}

Initialize-HeadlessFrameworkRuntime `
    -InstalledModules $installedModules `
    -ActiveModuleIds $activeModuleIds

# Framework modules must stay in the engine token so the server advertises the
# same module IDs and manifest versions as joining clients. Their graphical
# startup submodules are suppressed in memory by DedicatedServer.LauncherCheats;
# the module folders and manifests remain untouched.
$dependencyOnlyModuleIds = @(
    $activeModuleIds | Where-Object { $_ -in $headlessFrameworkModuleIds }
)
$selectedGameplayModuleIds = @(
    $activeModuleIds | Where-Object {
        $_ -notin $requiredModuleIds -and
        $_ -notin $dependencyOnlyModuleIds
    }
)
$engineModuleIds = @($activeModuleIds)
$collisionCommunityModuleIds = @(
    $activeModuleIds | Where-Object {
        $_ -notin @(
            'Native',
            'SandBoxCore',
            'Sandbox',
            'Coop',
            'DedicatedServer.Windows')
    }
)

$assemblyCompatibilityArguments = @{
    InstalledModules = $installedModules
    ActiveModuleIds = $engineModuleIds
    CommunityModuleIds = $collisionCommunityModuleIds
    BlockCollisions = $blockManagedAssemblyCollisions
    ServerBin = $serverBin
}
if (@($allowedAssemblyCollisions).Count -gt 0) {
    $assemblyCompatibilityArguments.AllowedCollisions =
        $allowedAssemblyCollisions
}
Assert-ManagedAssemblyCompatibility @assemblyCompatibilityArguments

# Coodsz tools: a campaign map provider brings SceneObj\Main_map\scene.xscene. An empty or cut short
# Main_map folder brings no map, and the engine would fall back to the plain Calradia map.
function Test-CampaignMapProvider {
    param([string]$ModuleRoot)

    return (Test-Path -LiteralPath (Join-Path $ModuleRoot 'SceneObj\Main_map\scene.xscene'))
}

$customMapModuleIds = @(
    $engineModuleIds |
        Where-Object {
            $_ -ne $serverHostModuleId -and
            (Test-CampaignMapProvider -ModuleRoot $installedModules[$_])
        }
)
if ($customMapModuleIds.Count -gt 1) {
    $multipleMapMessage =
        "Multiple active modules provide SceneObj\Main_map: " +
        "$($customMapModuleIds -join ', '). Their load order will decide which " +
        "assets win and the combination may fail."
    if ($allowUnsupportedMods) {
        Write-Warning ($multipleMapMessage + ' HexTool will attempt startup.')
    }
    else {
        throw ($multipleMapMessage + ' Enable exactly one campaign map provider.')
    }
}

# Coodsz tools: the map mod the host picked (COODSZ_CAMPAIGN_MAP). Without its SceneObj\Main_map on this
# server the engine quietly makes a plain Calradia world, and every player who loads the mod map crashes
# on join (AD1259 crash reports #13 to #15). Returns the refusal line, or $null when the map is there or
# no map mod was asked for.
function Get-MissingCampaignMapProblem {
    param(
        [string]$ExpectedModuleId,
        [string[]]$CustomMapModuleIds
    )

    if ([string]::IsNullOrWhiteSpace($ExpectedModuleId)) {
        return $null
    }
    $expected = $ExpectedModuleId.Trim()
    if (@($CustomMapModuleIds) -contains $expected) {
        return $null
    }
    return ("Campaign map missing: the Coodsz tool picked the $expected map, but no active module has " +
        "SceneObj\Main_map\scene.xscene on this server (its files are missing or the module is turned off), " +
        "so it would start a plain Calradia world. " +
        "Copy $expected into engine\Modules again or turn it on, then start the server.")
}

$campaignMapProblem = Get-MissingCampaignMapProblem `
    -ExpectedModuleId $env:COODSZ_CAMPAIGN_MAP `
    -CustomMapModuleIds $customMapModuleIds
if ($null -ne $campaignMapProblem) {
    Write-Host $campaignMapProblem
    exit 20
}

$requestedSaveModes = @($SaveFile, $SaveName, $NewSaveName) |
    Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
if ($requestedSaveModes.Count -gt 1) {
    throw 'Choose only one of -SaveFile, -SaveName, or -NewSaveName.'
}

if (-not [string]::IsNullOrWhiteSpace($NewSaveName)) {
    $selectedSaveName = $NewSaveName.Trim()
    # Any letter or digit in any language (Turkish, Cyrillic, Arabic). Path characters stay refused.
    if ($selectedSaveName -cnotmatch '^[\p{L}\p{Nd}][\p{L}\p{Nd} _-]{0,63}$') {
        throw 'New save names must contain 1-64 letters, numbers, spaces, hyphens, or underscores and start with a letter or number.'
    }

    $reservedSaveNames = @(
        'CON', 'PRN', 'AUX', 'NUL',
        'COM1', 'COM2', 'COM3', 'COM4', 'COM5', 'COM6', 'COM7', 'COM8', 'COM9',
        'LPT1', 'LPT2', 'LPT3', 'LPT4', 'LPT5', 'LPT6', 'LPT7', 'LPT8', 'LPT9',
        'default_new_game'
    )
    if ($selectedSaveName -in $reservedSaveNames) {
        throw "The new save name '$selectedSaveName' is reserved."
    }

    $templateSave = Join-Path $serverSaveRoot 'default_new_game.sav'
    if (-not (Test-Path -LiteralPath $templateSave)) {
        throw "The fresh-campaign template is missing: $templateSave"
    }
    foreach ($extension in @('.sav', '.json', '.launcher.json')) {
        $existingPath = Join-Path $serverSaveRoot ($selectedSaveName + $extension)
        if (Test-Path -LiteralPath $existingPath) {
            throw "A server campaign named '$selectedSaveName' already exists. Choose another name."
        }
    }
}
else {
    $saveCandidates = @(Get-SaveCandidates -LoadedModuleIds $activeModuleIds)
}

if (-not [string]::IsNullOrWhiteSpace($NewSaveName)) {
    $selectedSave = $null
}
elseif (-not [string]::IsNullOrWhiteSpace($SaveFile)) {
    $requestedSavePath = [System.IO.Path]::GetFullPath($SaveFile)
    $selectedSave = $saveCandidates |
        Where-Object { $_.File.FullName -ieq $requestedSavePath } |
        Select-Object -First 1

    # The launcher uses the real Documents path while this script can discover
    # the same save through server-data's directory junction.  If the literal
    # paths differ, prove file identity before accepting the compatible entry.
    if ($null -eq $selectedSave -and
        (Test-Path -LiteralPath $requestedSavePath -PathType Leaf)) {
        $requestedFile = Get-Item -LiteralPath $requestedSavePath
        $identityCandidates = @($saveCandidates | Where-Object {
            $_.File.Name -ieq $requestedFile.Name -and
            $_.File.Length -eq $requestedFile.Length
        })
        if ($identityCandidates.Count -gt 0) {
            $requestedHash =
                (Get-FileHash -LiteralPath $requestedSavePath -Algorithm SHA256).Hash
            $selectedSave = $identityCandidates |
                Where-Object {
                    (Get-FileHash -LiteralPath $_.File.FullName -Algorithm SHA256).Hash `
                        -ieq $requestedHash
                } |
                Select-Object -First 1
        }
    }

    if ($null -eq $selectedSave) {
        throw "Save file '$SaveFile' was not found or is incompatible with the selected module set."
    }
}
elseif ([string]::IsNullOrWhiteSpace($SaveName)) {
    if ($NoPrompt) {
        $configuredText =
            [System.IO.File]::ReadAllText(
                $serverConfigPath,
                $strictUtf8NoBom)
        $configuredMatch = [regex]::Match(
            $configuredText,
            '"saveName"\s*:\s*"(?<name>[^"]+)"',
            [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)
        if (-not $configuredMatch.Success) {
            throw 'server-config.json does not contain a saveName for noninteractive startup.'
        }
        $originalConfiguredSaveName = $configuredMatch.Groups['name'].Value
        $configuredSaveName = Resolve-ConfiguredPrimarySaveName `
            -ConfiguredSaveName $originalConfiguredSaveName `
            -SaveRoot $serverSaveRoot
        $configuredSavePath = [System.IO.Path]::GetFullPath(
            (Join-Path $serverSaveRoot ($configuredSaveName + '.sav')))
        $selectedSave = $saveCandidates |
            Where-Object {
                [System.IO.Path]::GetFullPath($_.File.FullName) -ieq
                    $configuredSavePath
            } |
            Select-Object -First 1
        if ($null -eq $selectedSave) {
            throw "Configured save '$configuredSaveName' was not found or is incompatible with the selected module set."
        }
        if (-not [string]::Equals(
                $configuredSaveName,
                $originalConfiguredSaveName,
                [StringComparison]::OrdinalIgnoreCase)) {
            Set-ConfiguredSaveName -SaveName $configuredSaveName
            Write-Host "Normalized configured recovery generation '$originalConfiguredSaveName' to primary campaign '$configuredSaveName'."
        }
    }
    else {
        $selectedSave = Read-SaveSelection -Candidates $saveCandidates
    }
}
else {
    $selectedSave = $saveCandidates |
        Where-Object { $_.Name -ieq $SaveName } |
        Select-Object -First 1

    if ($null -eq $selectedSave) {
        throw "Save '$SaveName' was not found or is incompatible with the selected module set."
    }
}

if ([string]::IsNullOrWhiteSpace($NewSaveName) -and $null -eq $selectedSave) {
    Write-Host 'Server startup cancelled.'
    exit 0
}

if ($null -ne $selectedSave -and @($selectedSave.AddedModules).Count -gt 0) {
    Write-Host ('Adding mods to an existing campaign: ' +
        (@($selectedSave.AddedModules) -join ', ') +
        '. Removing them later can break the save.')
}

if ($null -ne $selectedSave -and $selectedSave.ModuleOrderChanged) {
    Write-Host ('Mod load order changed since this save was made. Saved order: ' +
        (@($selectedSave.Modules) -join ', ') +
        '. Bannerlord does not check the order, so the save still loads.')
}

$selectedDifficulty = if (-not [string]::IsNullOrWhiteSpace($NewSaveName)) {
    $Difficulty
}
else {
    $null
}

if ([string]::IsNullOrWhiteSpace($NewSaveName)) {
    $selectedSaveName = Import-SelectedSave -Selection $selectedSave
}
$selectedSavePath = Join-Path $serverSaveRoot ($selectedSaveName + '.sav')
if (Test-Path -LiteralPath $selectedSavePath -PathType Leaf) {
    $saveVersionMigrated = $false
    foreach ($rollingModuleId in $rollingSaveVersionModules) {
        if ($rollingModuleId -notin $activeModuleIds -or
            -not $installedModuleVersions.ContainsKey($rollingModuleId) -or
            ($null -ne $selectedSave -and
                $rollingModuleId -in @($selectedSave.AddedModules))) {
            # A mod just added to this campaign has no version in the save yet.
            continue
        }
        $saveVersionMigrated =
            (Update-RollingSaveModuleVersion `
                -SavePath $selectedSavePath `
                -ModuleId $rollingModuleId `
                -InstalledVersion ([string]$installedModuleVersions[$rollingModuleId])) -or
            $saveVersionMigrated
    }
    if ($saveVersionMigrated) {
        Write-LauncherSaveMetadata `
            -SaveName $selectedSaveName `
            -LoadedModuleIds $activeModuleIds `
            -Difficulty $selectedDifficulty
    }
}
Set-ConfiguredSaveName -SaveName $selectedSaveName

$configuredText = [System.IO.File]::ReadAllText(
    $serverConfigPath,
    $strictUtf8NoBom)

$coopJoinPort = 4200
$coopJoinPortMatch = [regex]::Match(
    $configuredText,
    '(?m)^\s*"port"\s*:\s*(?<value>\d+)\s*,?',
    [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)
if ($coopJoinPortMatch.Success) {
    $coopJoinPort = [int]$coopJoinPortMatch.Groups['value'].Value
}
if ($coopJoinPort -lt 1 -or $coopJoinPort -gt 65535) {
    throw "Configured Coop client UDP port must be between 1 and 65,535; found $coopJoinPort."
}
if ($coopJoinPort -eq $Port) {
    throw "The Coop client UDP port and engine port are both $Port. Choose distinct ports so both listeners can start."
}
$coopPortOwner = [System.Net.NetworkInformation.IPGlobalProperties]::
    GetIPGlobalProperties().GetActiveUdpListeners() |
    Where-Object { $_.Port -eq $coopJoinPort } |
    Select-Object -First 1
if ($null -ne $coopPortOwner) {
    throw "Coop client UDP port $coopJoinPort is already in use. Stop the existing server or choose another port in Settings."
}

$selectedBattleSize = $BattleSize
if ($selectedBattleSize -eq 0) {
    $battleSizeMatch = [regex]::Match(
        $configuredText,
        '(?m)^\s*"battleSize"\s*:\s*(?<value>\d+)\s*,?',
        [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)
    if ($battleSizeMatch.Success) {
        $selectedBattleSize = [int]$battleSizeMatch.Groups['value'].Value
    }
    else {
        $selectedBattleSize = 1000
    }
}
$maxBattleSize = Get-BattleSizeMaximum -ModuleIds $engineModuleIds
$maxBattleSizeText = if ($maxBattleSize -eq 10000) { '10,000' } else { '2,000' }
if ($selectedBattleSize -lt 300 -or $selectedBattleSize -gt $maxBattleSize) {
    throw "Configured battle size must be between 300 and $maxBattleSizeText; found $selectedBattleSize."
}

$allowClientTimeControls = $false
$clientTimeControlsMatch = [regex]::Match(
    $configuredText,
    '(?m)^\s*"allowClientTimeControls"\s*:\s*(?<value>true|false)\s*,?',
    [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)
if ($clientTimeControlsMatch.Success) {
    $allowClientTimeControls =
        $clientTimeControlsMatch.Groups['value'].Value -ieq 'true'
}
# fastForwardEnabled and autoPauseEnabled are the host's own Coodsz Host settings; they are read, never
# overwritten. The launcher submodule caps fast forward while client time controls are off and, unless
# BANNERLORD_DS_AUTO_PAUSE is 1, stops settlements and battles from pausing campaign time.
$fastForwardSetting = Get-CoopBooleanModOption -Key 'fastForwardEnabled' -Default $true
$autoPauseSetting = Get-CoopBooleanModOption -Key 'autoPauseEnabled' -Default $false
Set-CoopBooleanModOption `
    -Key 'clientsCanUseCheats' `
    -Enabled $false

$moduleToken = '_MODULES_' + (($engineModuleIds | ForEach-Object { "*$_" }) -join '') + '*_MODULES_'

Write-Host 'Bannerlord Coop modded dedicated server'
Write-ManagedRuntimeIdentity `
    -Label 'Dedicated-server runtime' `
    -Path (Join-Path $installedModules[$serverHostModuleId] `
        'bin\Win64_Shipping_Server\DedicatedServer.Core.dll')
$coopRuntimeRoot = if ($installedModules.ContainsKey('Coop')) { $installedModules['Coop'] } else { $installedModules['CoopNightly'] }
Write-ManagedRuntimeIdentity `
    -Label 'Paired Coop runtime' `
    -Path (Join-Path $coopRuntimeRoot `
        'bin\Win64_Shipping_Server\Common.dll')
if ([string]::IsNullOrWhiteSpace($NewSaveName)) {
    Write-Host "Selected save: $selectedSaveName"
}
else {
    Write-Host "New campaign: $selectedSaveName"
}
Write-Host "Coop client port: $coopJoinPort"
Write-Host "Engine port/region: $Port / $Region"
Write-Host "Battle size: $selectedBattleSize"
Write-Host "Client time controls: $(if ($allowClientTimeControls) { 'enabled' } else { 'disabled' })"
Write-Host "Fast forward: $(if (-not $fastForwardSetting) { 'off' } elseif ($allowClientTimeControls) { 'on' } else { 'capped at 1x (turn on client time controls to use it)' })"
Write-Host "Auto pause in settlements and battles: $(if ($autoPauseSetting) { 'on' } else { 'off' })"
Write-Host 'Client cheats: ALWAYS blocked by the authoritative server'
Write-Host "Dependency checks: $(if ($strictDependencyValidation) { 'strict enforcement' } else { 'compatible/advisory' })"
Write-Host "Unsupported selected mods: $(if ($allowUnsupportedMods) { 'attempt startup' } else { 'respect module headless exclusions' })"
Write-Host "Managed assembly collisions: $(if ($blockManagedAssemblyCollisions) { 'block before launch' } else { 'warn and attempt startup' })"
Write-Host "Difficulty: $(if ([string]::IsNullOrWhiteSpace($selectedDifficulty)) { 'current server settings' } else { $selectedDifficulty })"
Write-Host "Server data: $dedicatedDataRoot"
Write-Host "Gameplay config: $modConfigPath"
Write-Host
Write-Host 'Selectable modules:'
foreach ($moduleId in $selectableModuleIds) {
    Write-Host ('  ' + (Format-ModuleState -ModuleId $moduleId -Enabled ([bool]$selection[$moduleId])))
}
Write-Host
Write-Host 'Active load order:'
for ($index = 0; $index -lt $engineModuleIds.Count; $index++) {
    Write-Host ('  {0,2}. {1}' -f ($index + 1), $engineModuleIds[$index])
}
if ($dependencyOnlyModuleIds.Count -gt 0) {
    Write-Host
    Write-Host "Advertised headless framework modules: $($dependencyOnlyModuleIds -join ', ')"
    Write-Host 'Their installed IDs/versions remain in the server handshake; only client startup classes are suppressed.'
}
if ($customMapModuleIds.Count -gt 0) {
    Write-Host
    Write-Host "Custom campaign map provider(s): $($customMapModuleIds -join ', ')"
    Write-Host 'DedicatedServer.Windows was placed before them so their Main_map assets take priority.'
}
Write-Host
Write-Host 'Starting the native engine. The server is ready when the log shows:'
Write-Host '  [DedicatedServer] SERVING'
Write-Host

$env:DOTNET_ROOT = Join-Path $engineRoot 'dotnet'
$env:DOTNET_MULTILEVEL_LOOKUP = '0'
$env:BANNERLORD_USER_DIR = $dedicatedDataRoot
$env:COOP_DATA_DIR = $coopDataRoot
# Launcher Battle Size ignores sizes above 2,000; above that Limitless.CoopCompat sizes coop battles from mod-config.json.
$env:BANNERLORD_DS_BATTLE_SIZE = [string][Math]::Min($selectedBattleSize, 2000)
$env:BANNERLORD_DS_ALLOW_CLIENT_TIME_CONTROLS =
    if ($allowClientTimeControls) { '1' } else { '0' }
$env:BANNERLORD_DS_AUTO_PAUSE =
    if ($autoPauseSetting) { '1' } else { '0' }
$env:BANNERLORD_DS_INITIALIZE_ACTIVE_CAMPAIGN_XML =
    if (-not [string]::IsNullOrWhiteSpace($NewSaveName) -and
        $customMapModuleIds.Count -gt 0) {
        '1'
    }
    else {
        '0'
    }
$env:BANNERLORD_DS_CUSTOM_MAP_MODULES = $customMapModuleIds -join ';'
$env:BANNERLORD_DS_CUSTOM_MAP_DIRECTORIES = @(
    $customMapModuleIds |
        ForEach-Object { $installedModules[$_] }
) -join ';'
# Compatibility mode deliberately attempts every selected gameplay module,
# including submodules whose manifests opt out of dedicated-server/no-render
# loading. The override applies to every submodule in each selected directory;
# unsafe client/UI code may fail later inside the engine.
$effectiveForceHeadlessModuleIds = @(
    $(if ($forceAllSelectedGameplayModulesHeadless) {
        $selectedGameplayModuleIds
    })
    $forceHeadlessModuleIds
) | Where-Object { $_ -in $activeModuleIds } | Select-Object -Unique
if ($effectiveForceHeadlessModuleIds.Count -gt 0) {
    Write-Warning ("Unsupported headless loading enabled for: " +
        ($effectiveForceHeadlessModuleIds -join ', ') +
        '. HexTool will attempt them; client/UI submodules may still crash.')
}
$forceHeadlessDirectories = @(
    $effectiveForceHeadlessModuleIds |
        Where-Object { $installedModules.ContainsKey($_) } |
        ForEach-Object {
            [System.IO.Path]::GetFullPath($installedModules[$_])
        }
)
$env:BANNERLORD_DS_FORCE_HEADLESS_MODULE_DIRECTORIES =
    ($forceHeadlessDirectories -join ';')
$env:BANNERLORD_DS_DEPENDENCY_ONLY_MODULE_DIRECTORIES = @(
    $dependencyOnlyModuleIds |
        ForEach-Object { $installedModules[$_] }
) -join ';'
$env:BANNERLORD_DS_ALLOWED_DEPENDENCY_SUBMODULE_TYPES =
    if ('Bannerlord.MBOptionScreen' -in $activeModuleIds) {
        @(
            'MCM.MCMSubModule'
            'MCM.Internal.MCMImplementationSubModule'
        ) -join ';'
    }
    else {
        ''
    }

$managedSearchDirectories = [System.Collections.Generic.List[string]]::new()
$managedSearchDirectories.Add([System.IO.Path]::GetFullPath($serverBin))
foreach ($moduleId in $engineModuleIds) {
    foreach ($relativeBin in @(
        'bin\Win64_Shipping_Server',
        'bin\Win64_Shipping_Client',
        'bin\Gaming.Desktop.x64_Shipping_Client'
    )) {
        $candidateBin = Join-Path $installedModules[$moduleId] $relativeBin
        if ((Test-Path -LiteralPath $candidateBin -PathType Container) -and
            $candidateBin -notin $managedSearchDirectories) {
            $managedSearchDirectories.Add(
                [System.IO.Path]::GetFullPath($candidateBin)
            )
        }
    }
}
$env:BANNERLORD_DS_MANAGED_SEARCH_DIRECTORIES =
    $managedSearchDirectories -join ';'
$env:DOTNET_STARTUP_HOOKS =
    [System.IO.Path]::GetFullPath($runtimeBootstrapPath)

$launchedSavePath = Join-Path $serverSaveRoot ($selectedSaveName + '.sav')
$preLaunchSaveLength = $null
$preLaunchSaveWriteTicks = $null
if (Test-Path -LiteralPath $launchedSavePath -PathType Leaf) {
    $preLaunchSaveFile = Get-Item -LiteralPath $launchedSavePath
    $preLaunchSaveLength = [long]$preLaunchSaveFile.Length
    $preLaunchSaveWriteTicks = [long]$preLaunchSaveFile.LastWriteTimeUtc.Ticks
}

if (-not [string]::IsNullOrWhiteSpace($selectedDifficulty)) {
    # A new-campaign preset updates the same global configuration edited by the
    # Settings tab. Existing saves never overwrite those operator settings from
    # stale launcher metadata.
    $null = Set-ConfiguredDifficultyPreset -Preset $selectedDifficulty
}

Push-Location -LiteralPath $serverBin
[BannerlordRglDialogGuard]::Start($PID)
try {
    & $dotnetPath 'TaleWorlds.Starter.DotNetCore.dll' $moduleToken '/dedicatedcustomserver' $Port $Region '0'
    $serverExitCode = $LASTEXITCODE
    if ($serverExitCode -eq 0 -and
        (Test-Path -LiteralPath $launchedSavePath -PathType Leaf)) {
        $postLaunchSaveFile = Get-Item -LiteralPath $launchedSavePath
        $saveWasWrittenBySuccessfulRun =
            $null -eq $preLaunchSaveLength -or
            $null -eq $preLaunchSaveWriteTicks -or
            [long]$postLaunchSaveFile.Length -ne $preLaunchSaveLength -or
            [long]$postLaunchSaveFile.LastWriteTimeUtc.Ticks -ne $preLaunchSaveWriteTicks
        if ($saveWasWrittenBySuccessfulRun) {
            try {
                Write-LauncherSaveMetadata `
                    -SaveName $selectedSaveName `
                    -LoadedModuleIds $activeModuleIds `
                    -Difficulty $selectedDifficulty
            }
            catch {
                Write-Warning "Could not update launcher metadata for '$selectedSaveName': $($_.Exception.Message)"
            }
        }
    }
    exit $serverExitCode
}
finally {
    [BannerlordRglDialogGuard]::Stop()
    Pop-Location
    if ($null -ne $serverInstanceLock) {
        $serverInstanceLock.Dispose()
    }
}
