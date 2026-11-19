#requires -Version 5.1
<#
.SYNOPSIS
    Builds Coodsz War Sync (CoodszWarSync.dll) and the server build of CoodszSettingsSync.Coop.dll against the
    dedicated server's own DLLs, and copies both into the TAOM server tool's payload (src\TaomCoopHost\WarSync).
.PARAMETER ServerRoot
    The Coop DedicatedServer folder (contains engine\bin\Win64_Shipping_Server).
#>
[CmdletBinding()]
param([string]$ServerRoot)

$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$arguments = @('build', (Join-Path $here 'CoodszWarSync.csproj'), '-c', 'Release', '-o', (Join-Path $here 'bin\Release'))
if (-not [string]::IsNullOrWhiteSpace($ServerRoot)) { $arguments += "-p:ServerRoot=$ServerRoot" }
& dotnet @arguments
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }

# The same server build Coodsz Server Tools ships (compat\CoodszServerTools\build.ps1 builds it too).
$sync = Join-Path $here '..\CoodszSettingsSync'
$syncArguments = @('build', (Join-Path $sync 'CoodszSettingsSync.Server.csproj'), '-c', 'Release', '-o', (Join-Path $sync 'bin\Server'))
if (-not [string]::IsNullOrWhiteSpace($ServerRoot)) { $syncArguments += "-p:ServerRoot=$ServerRoot" }
& dotnet @syncArguments
if ($LASTEXITCODE -ne 0) { throw 'Settings sync build failed.' }

$payload = Join-Path $here '..\..\src\TaomCoopHost\WarSync'
New-Item -ItemType Directory -Force $payload | Out-Null
Copy-Item -LiteralPath (Join-Path $here 'bin\Release\CoodszWarSync.dll') -Destination $payload -Force
Copy-Item -LiteralPath (Join-Path $sync 'bin\Server\CoodszSettingsSync.Coop.dll') -Destination $payload -Force
# Coodsz Host ships the same file; both copies come from this one build so they stay byte equal (a test checks it).
$toolsBin = Join-Path $here '..\..\src\CoodszHost\ServerTools\DedicatedServer.CoodszTools\bin\Win64_Shipping_Server'
if (Test-Path -LiteralPath $toolsBin) { Copy-Item -LiteralPath (Join-Path $sync 'bin\Server\CoodszSettingsSync.Coop.dll') -Destination $toolsBin -Force }
Write-Host "Copied Coodsz War Sync to $payload"
