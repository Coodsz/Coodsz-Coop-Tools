#requires -Version 5.1
<#
.SYNOPSIS
    Builds DedicatedServer.CoodszTools.dll against the dedicated server's own game DLLs and copies
    the module into Coodsz Host's payload (src\CoodszHost\ServerTools\DedicatedServer.CoodszTools).
.PARAMETER ServerRoot
    The Coop DedicatedServer folder (contains engine\bin\Win64_Shipping_Server).
#>
[CmdletBinding()]
param([string]$ServerRoot)

$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$arguments = @('build', (Join-Path $here 'CoodszServerTools.csproj'), '-c', 'Release', '-o', (Join-Path $here 'bin\Release'))
if (-not [string]::IsNullOrWhiteSpace($ServerRoot)) { $arguments += "-p:ServerRoot=$ServerRoot" }
& dotnet @arguments
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }

# Settings sync: the server build of CoodszSettingsSync.Coop.dll (compat\CoodszSettingsSync), which the
# module loads after its probe finds the Coop types it is compiled against.
$sync = Join-Path $here '..\CoodszSettingsSync'
$syncArguments = @('build', (Join-Path $sync 'CoodszSettingsSync.Server.csproj'), '-c', 'Release', '-o', (Join-Path $sync 'bin\Server'))
if (-not [string]::IsNullOrWhiteSpace($ServerRoot)) { $syncArguments += "-p:ServerRoot=$ServerRoot" }
& dotnet @syncArguments
if ($LASTEXITCODE -ne 0) { throw 'Settings sync build failed.' }

$payload = Join-Path $here '..\..\src\CoodszHost\ServerTools\DedicatedServer.CoodszTools'
$bin = Join-Path $payload 'bin\Win64_Shipping_Server'
New-Item -ItemType Directory -Force $bin | Out-Null
Copy-Item -LiteralPath (Join-Path $here 'bin\Release\DedicatedServer.CoodszTools.dll') -Destination $bin -Force
Copy-Item -LiteralPath (Join-Path $sync 'bin\Server\CoodszSettingsSync.Coop.dll') -Destination $bin -Force
Copy-Item -LiteralPath (Join-Path $here 'SubModule.xml') -Destination $payload -Force
Copy-Item -LiteralPath (Join-Path $here 'README.md') -Destination (Join-Path $payload 'README.txt') -Force
Copy-Item -LiteralPath (Join-Path $here 'LICENSE.txt') -Destination $payload -Force
Write-Host "Copied the module to $payload"
# The TAOM server tool's Coodsz War Sync ships the same file; keep the two copies byte equal (a test checks it).
$warSync = Join-Path $here '..\..\src\TaomCoopHost\WarSync'
if (Test-Path -LiteralPath $warSync) { Copy-Item -LiteralPath (Join-Path $sync 'bin\Server\CoodszSettingsSync.Coop.dll') -Destination $warSync -Force }
