#requires -Version 5.1
<#
.SYNOPSIS
Builds the Coodsz Join Password module from src\JoinPassword.cs into module\bin\Win64_Shipping_Client.

.DESCRIPTION
    .\build.ps1 -GameRoot "<Steam>\steamapps\common\Mount & Blade II Bannerlord"

Harmony comes from Coop's own bin folder (Workshop 3770450698), because the TAOM list does not load the
Bannerlord.Harmony module and every Coop install ships 0Harmony.dll. The module touches Coop only by
reflection, so it needs no Coop reference.
#>
[CmdletBinding()]
param(
    [string] $GameRoot = 'C:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord',
    [string] $HarmonyPath = ''
)
$ErrorActionPreference = 'Stop'

$gameBin = Join-Path $GameRoot 'bin\Win64_Shipping_Client'
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
if ([string]::IsNullOrWhiteSpace($HarmonyPath)) {
    $steamApps = Split-Path -Parent (Split-Path -Parent $GameRoot)
    $HarmonyPath = Join-Path $steamApps 'workshop\content\261550\3770450698\bin\Win64_Shipping_Client\0Harmony.dll'
}

$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
$dotnetPath = if ($dotnet) { $dotnet.Source } else { Join-Path $env:ProgramFiles 'dotnet\dotnet.exe' }
if (-not (Test-Path -LiteralPath $dotnetPath)) { throw 'A .NET SDK is needed to build the module.' }
$sdk = @(& $dotnetPath --list-sdks) | Select-Object -Last 1
if ($sdk -notmatch '^(\S+)\s+\[(.+)\]$') { throw 'No .NET SDK found.' }
$csc = Join-Path $Matches[2] ($Matches[1] + '\Roslyn\bincore\csc.dll')

$references = @('mscorlib.dll', 'System.dll', 'System.Core.dll', 'netstandard.dll') | ForEach-Object { Join-Path $framework $_ }
$references += @('TaleWorlds.MountAndBlade.dll', 'TaleWorlds.Library.dll', 'TaleWorlds.DotNet.dll', 'TaleWorlds.Core.dll',
    'TaleWorlds.CampaignSystem.dll', 'TaleWorlds.Localization.dll', 'TaleWorlds.ObjectSystem.dll', 'TaleWorlds.SaveSystem.dll') |
    ForEach-Object { Join-Path $gameBin $_ }
$references += $HarmonyPath
foreach ($inputFile in @($csc) + $references) {
    if (-not (Test-Path -LiteralPath $inputFile -PathType Leaf)) { throw "Missing build input: $inputFile" }
}

$sync = Join-Path $PSScriptRoot '..\CoodszSettingsSync'
# The Improved Garrisons party size hand back; the server modules build the same two files.
$igCompat = Join-Path $PSScriptRoot '..\ImprovedGarrisonsCoopCompat'
$outputDir = Join-Path $PSScriptRoot 'module\bin\Win64_Shipping_Client'
New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
$output = Join-Path $outputDir 'CoodszJoinPassword.dll'
$arguments = @('-nologo', '-noconfig', '-nostdlib+', '-target:library', '-optimize+', '-deterministic', '-langversion:9',
    '-platform:anycpu', '-nowarn:1701,1702', "-out:$output") +
    @($references | ForEach-Object { "-reference:$_" }) + (Join-Path $PSScriptRoot 'src\JoinPassword.cs') +
    (Join-Path $PSScriptRoot 'src\JoinBaselineFix.cs') + (Join-Path $PSScriptRoot 'src\TaomEnlistGuard.cs') +
    (Join-Path $PSScriptRoot 'src\TaomEnlistGuardRules.cs') + (Join-Path $PSScriptRoot 'src\MapCheckGuard.cs') +
    (Join-Path $PSScriptRoot 'src\MapCheckGuardRules.cs') + (Join-Path $sync 'src\Shared\CoopProbe.cs') +
    (Join-Path $igCompat 'PartySizeHandBack.cs') + (Join-Path $igCompat 'PartySizeHandBackRules.cs') +
    (Join-Path $PSScriptRoot 'src\CopyMark.cs')
& $dotnetPath $csc @arguments
if ($LASTEXITCODE -ne 0) { throw "Compiler failed: $LASTEXITCODE" }
Write-Host ("Built " + $output + " (SHA-256 " + (Get-FileHash -LiteralPath $output).Hash + ")")

# Settings sync (compat\CoodszSettingsSync): CoodszSettingsSync.Coop.dll holds the Coop handler, so it is
# compiled against Coop's own client DLLs. It is not a submodule; JoinPassword.dll loads it after its probe
# finds those Coop types. The server build of the same assembly is in compat\CoodszServerTools\build.ps1.
$coopBin = Split-Path -Parent $HarmonyPath
$syncReferences = @('mscorlib.dll', 'System.dll', 'System.Core.dll', 'netstandard.dll') | ForEach-Object { Join-Path $framework $_ }
$syncReferences += @('Common.dll', 'Coop.Core.dll', 'GameInterface.dll', 'LiteNetLib.dll', 'protobuf-net.dll', 'protobuf-net.Core.dll') |
    ForEach-Object { Join-Path $coopBin $_ }
$syncReferences += $HarmonyPath
# The PvP countdown's screen (src\Client\PvpCountdownClient.cs) is a Gauntlet global layer; its prefab is in
# module\GUI\Prefabs\CoodszPvpCountdown.xml.
$syncReferences += @('TaleWorlds.Library.dll', 'TaleWorlds.Localization.dll', 'TaleWorlds.ScreenSystem.dll', 'TaleWorlds.Engine.GauntletUI.dll',
    'TaleWorlds.GauntletUI.dll', 'TaleWorlds.GauntletUI.Data.dll', 'TaleWorlds.Engine.dll', 'TaleWorlds.InputSystem.dll', 'TaleWorlds.TwoDimension.dll') |
    ForEach-Object { Join-Path $gameBin $_ }
foreach ($inputFile in $syncReferences) {
    if (-not (Test-Path -LiteralPath $inputFile -PathType Leaf)) { throw "Missing build input: $inputFile" }
}
$syncOutput = Join-Path $outputDir 'CoodszSettingsSync.Coop.dll'
$syncSources = @(Get-ChildItem -LiteralPath (Join-Path $sync 'src\Shared') -Filter '*.cs' | Sort-Object Name | ForEach-Object { $_.FullName }) +
    @(Get-ChildItem -LiteralPath (Join-Path $sync 'src\Client') -Filter '*.cs' | Sort-Object Name | ForEach-Object { $_.FullName })
$syncArguments = @('-nologo', '-noconfig', '-nostdlib+', '-target:library', '-optimize+', '-deterministic', '-langversion:9',
    '-platform:anycpu', '-nowarn:1701,1702', "-out:$syncOutput") +
    @($syncReferences | ForEach-Object { "-reference:$_" }) + $syncSources
& $dotnetPath $csc @syncArguments
if ($LASTEXITCODE -ne 0) { throw "Compiler failed: $LASTEXITCODE" }
Write-Host ("Built " + $syncOutput + " (SHA-256 " + (Get-FileHash -LiteralPath $syncOutput).Hash + ")")
