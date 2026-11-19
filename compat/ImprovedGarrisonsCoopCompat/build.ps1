#requires -Version 5.1
<#
.SYNOPSIS
    Builds DedicatedServer.IGCoopCompat.dll against the dedicated server's own game DLLs and copies
    the module into Coodsz Host's payload (src\CoodszHost\PatchModules\DedicatedServer.IGCoopCompat).
.PARAMETER ServerRoot
    The Coop DedicatedServer folder (contains engine\bin\Win64_Shipping_Server).
#>
[CmdletBinding()]
param([string]$ServerRoot)

$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$arguments = @('build', (Join-Path $here 'ImprovedGarrisonsCoopCompat.csproj'), '-c', 'Release', '-o', (Join-Path $here 'bin\Release'))
if (-not [string]::IsNullOrWhiteSpace($ServerRoot)) { $arguments += "-p:ServerRoot=$ServerRoot" }
& dotnet @arguments
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }

$payload = Join-Path $here '..\..\src\CoodszHost\PatchModules\DedicatedServer.IGCoopCompat'
$bin = Join-Path $payload 'bin\Win64_Shipping_Server'
New-Item -ItemType Directory -Force $bin | Out-Null
Copy-Item -LiteralPath (Join-Path $here 'bin\Release\DedicatedServer.IGCoopCompat.dll') -Destination $bin -Force
Copy-Item -LiteralPath (Join-Path $here 'SubModule.xml') -Destination $payload -Force
Copy-Item -LiteralPath (Join-Path $here 'README.txt') -Destination $payload -Force
Copy-Item -LiteralPath (Join-Path $here 'LICENSE.txt') -Destination $payload -Force
Write-Host "Copied the module to $payload"
