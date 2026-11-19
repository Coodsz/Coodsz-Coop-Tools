#requires -Version 5.1
<#
.SYNOPSIS
    Builds CoodszImageExporter.dll against the installed game's DLLs and copies the module into
    Coodsz Host's payload (src\CoodszHost\ImageExporter\CoodszImageExporter).
.PARAMETER GameDir
    The Mount & Blade II Bannerlord folder.
#>
[CmdletBinding()]
param([string]$GameDir)

$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$arguments = @('build', (Join-Path $here 'CoodszImageExporter.csproj'), '-c', 'Release', '-o', (Join-Path $here 'bin\Release'))
if (-not [string]::IsNullOrWhiteSpace($GameDir)) { $arguments += "-p:GameDir=$GameDir" }
& dotnet @arguments
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }

$payload = Join-Path $here '..\..\src\CoodszHost\ImageExporter\CoodszImageExporter'
$bin = Join-Path $payload 'bin\Win64_Shipping_Client'
New-Item -ItemType Directory -Force $bin | Out-Null
Copy-Item -LiteralPath (Join-Path $here 'bin\Release\CoodszImageExporter.dll') -Destination $bin -Force
Copy-Item -LiteralPath (Join-Path $here 'SubModule.xml') -Destination $payload -Force
Copy-Item -LiteralPath (Join-Path $here 'README.md') -Destination (Join-Path $payload 'README.txt') -Force
Copy-Item -LiteralPath (Join-Path $here 'LICENSE.txt') -Destination $payload -Force
Write-Host "Copied the module to $payload"
