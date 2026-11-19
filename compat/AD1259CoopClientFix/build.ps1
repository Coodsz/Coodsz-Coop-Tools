#requires -Version 5.1
<#
.SYNOPSIS
Rebuilds SkyApolo's AD1259 - Coop Client Stability DLL from src\ClientAmbientGuard.cs.

.DESCRIPTION
Adapted from SkyApolo's Build-ClientFix.ps1. Run it after a Bannerlord, Coop or Harmony update:

    .\build.ps1 -GameRoot "<Steam>\steamapps\common\Mount & Blade II Bannerlord" -ModuleRoot "module"

Without -ModuleRoot it writes to build\bin\Win64_Shipping_Client so the shipped DLL is untouched.
Harmony is taken from the game's Modules folder, or else from Workshop item 2859188632.
#>
[CmdletBinding()]
param(
    [string] $GameRoot = 'C:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord',
    [string] $ModuleRoot = '',
    [string] $HarmonyPath = ''
)
$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($ModuleRoot)) { $ModuleRoot = Join-Path $PSScriptRoot 'build' }
elseif (-not [IO.Path]::IsPathRooted($ModuleRoot)) { $ModuleRoot = Join-Path $PSScriptRoot $ModuleRoot }

$gameBin = Join-Path $GameRoot 'bin\Win64_Shipping_Client'
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
if ([string]::IsNullOrWhiteSpace($HarmonyPath)) {
    $HarmonyPath = Join-Path $GameRoot 'Modules\Bannerlord.Harmony\bin\Win64_Shipping_Client\0Harmony.dll'
    if (-not (Test-Path -LiteralPath $HarmonyPath)) {
        $steamApps = Split-Path -Parent (Split-Path -Parent $GameRoot)
        $HarmonyPath = Join-Path $steamApps 'workshop\content\261550\2859188632\bin\Win64_Shipping_Client\0Harmony.dll'
    }
}

$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
$dotnetPath = if ($dotnet) { $dotnet.Source } else { Join-Path $env:ProgramFiles 'dotnet\dotnet.exe' }
if (-not (Test-Path -LiteralPath $dotnetPath)) { throw 'A .NET SDK is needed to build the module.' }
$sdk = @(& $dotnetPath --list-sdks) | Select-Object -Last 1
if ($sdk -notmatch '^(\S+)\s+\[(.+)\]$') { throw 'No .NET SDK found.' }
$csc = Join-Path $Matches[2] ($Matches[1] + '\Roslyn\bincore\csc.dll')

$references = @('mscorlib.dll', 'System.dll', 'System.Core.dll', 'netstandard.dll') | ForEach-Object { Join-Path $framework $_ }
$references += @('TaleWorlds.MountAndBlade.dll', 'TaleWorlds.Engine.dll', 'TaleWorlds.Library.dll',
    'TaleWorlds.DotNet.dll', 'TaleWorlds.Core.dll') | ForEach-Object { Join-Path $gameBin $_ }
$references += $HarmonyPath
foreach ($inputFile in @($csc) + $references) {
    if (-not (Test-Path -LiteralPath $inputFile -PathType Leaf)) { throw "Missing build input: $inputFile" }
}

$outputDir = Join-Path $ModuleRoot 'bin\Win64_Shipping_Client'
New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
$output = Join-Path $outputDir 'AD1259CoopClientFix.dll'
$arguments = @('-nologo', '-noconfig', '-nostdlib+', '-target:library', '-optimize+', '-deterministic', '-langversion:9',
    '-platform:anycpu', '-nowarn:1701,1702', "-out:$output") +
    @($references | ForEach-Object { "-reference:$_" }) + (Join-Path $PSScriptRoot 'src\ClientAmbientGuard.cs')
& $dotnetPath $csc @arguments
if ($LASTEXITCODE -ne 0) { throw "Client fix compiler failed: $LASTEXITCODE" }
Write-Host ("Built " + $output + " (SHA-256 " + (Get-FileHash -LiteralPath $output).Hash + ")")
