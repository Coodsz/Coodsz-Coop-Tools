#requires -Version 5.1
<#
.SYNOPSIS
Builds the Coodsz Menu Guard module from src\MenuGuard.cs into module\bin\Win64_Shipping_Client.

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
$references += @('TaleWorlds.MountAndBlade.dll', 'TaleWorlds.Library.dll', 'TaleWorlds.DotNet.dll', 'TaleWorlds.Core.dll') |
    ForEach-Object { Join-Path $gameBin $_ }
$references += $HarmonyPath
foreach ($inputFile in @($csc) + $references) {
    if (-not (Test-Path -LiteralPath $inputFile -PathType Leaf)) { throw "Missing build input: $inputFile" }
}

$outputDir = Join-Path $PSScriptRoot 'module\bin\Win64_Shipping_Client'
New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
$output = Join-Path $outputDir 'CoodszMenuGuard.dll'
$arguments = @('-nologo', '-noconfig', '-nostdlib+', '-target:library', '-optimize+', '-deterministic', '-langversion:9',
    '-platform:anycpu', '-nowarn:1701,1702', "-out:$output") +
    @($references | ForEach-Object { "-reference:$_" }) + (Join-Path $PSScriptRoot 'src\MenuGuard.cs') +
    (Join-Path $PSScriptRoot 'src\CopyMark.cs')
& $dotnetPath $csc @arguments
if ($LASTEXITCODE -ne 0) { throw "Compiler failed: $LASTEXITCODE" }
# The launchers ship the guard inside the join-password module (same id, so Bannerlord sees no mod
# change and keeps its shader cache); the standalone zip uses this module folder.
Copy-Item -LiteralPath $output -Destination (Join-Path $PSScriptRoot '..\CoodszJoinPassword\module\bin\Win64_Shipping_Client\CoodszMenuGuard.dll') -Force
Write-Host ("Built " + $output + " (SHA-256 " + (Get-FileHash -LiteralPath $output).Hash + ")")
