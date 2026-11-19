#requires -Version 5.1
<#
.SYNOPSIS
    Builds AD1259CoopCompat.dll against the dedicated server's own game assemblies.
.PARAMETER ServerRoot
    The Coop DedicatedServer folder (contains BannerlordCoopServer.exe and engine\).
#>
[CmdletBinding()]
param(
    [string]$ServerRoot,
    [string]$ModuleRoot
)

$ErrorActionPreference = 'Stop'
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
if ([string]::IsNullOrWhiteSpace($ModuleRoot)) { $ModuleRoot = Join-Path $scriptDir '..\AD1259-Coop-CompatPatch' }
$ModuleRoot = [IO.Path]::GetFullPath($ModuleRoot)
if ([string]::IsNullOrWhiteSpace($ServerRoot)) {
    $ServerRoot = Join-Path $scriptDir '..\Bannerlord-coop\steamapps\workshop\content\261550\3770450698\DedicatedServer'
}
$ServerRoot = [System.IO.Path]::GetFullPath($ServerRoot)
$serverBin = Join-Path $ServerRoot 'engine\bin\Win64_Shipping_Server'
$harmony = Join-Path $ServerRoot 'engine\Modules\Bannerlord.Harmony\bin\Win64_Shipping_Server\0Harmony.dll'
$sandBox = Join-Path $ServerRoot 'engine\Modules\SandBox\bin\Win64_Shipping_Server\SandBox.dll'
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'

$csc = @(
    "${env:ProgramFiles(x86)}\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\Roslyn\csc.exe",
    "${env:ProgramFiles}\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\Roslyn\csc.exe",
    "${env:ProgramFiles}\Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\Roslyn\csc.exe",
    "${env:ProgramFiles}\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\Roslyn\csc.exe"
) | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
$compilerHost = $null
if (-not $csc) {
    $dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($dotnet) {
        $sdk = @(& $dotnet.Source --list-sdks) | Select-Object -Last 1
        if ($sdk -match '^(\S+)\s+\[(.+)\]$') {
            $candidate = Join-Path $Matches[2] ($Matches[1] + '\Roslyn\bincore\csc.dll')
            if (Test-Path -LiteralPath $candidate) { $csc = $candidate; $compilerHost = $dotnet.Source }
        }
    }
}
if (-not $csc) { throw 'Install a .NET SDK or Visual Studio 2022 Build Tools to rebuild. The release already includes the compiled DLL.' }
foreach ($required in @($serverBin, $harmony, $sandBox, $framework)) {
    if (-not (Test-Path -LiteralPath $required)) { throw "Missing build input: $required" }
}

$references = @(
    'mscorlib.dll', 'System.dll', 'System.Core.dll', 'System.Xml.dll', 'netstandard.dll'
) | ForEach-Object { Join-Path $framework $_ }
$references += Get-ChildItem -LiteralPath $serverBin -Filter 'TaleWorlds.*.dll' |
    Where-Object { $_.Name -notmatch '\.(Starter|DotNetCore|Diamond\.AccessProvider\.Test)' } |
    ForEach-Object { $_.FullName }
$references += $harmony
# SandBox (SandBoxGameManager, SandBoxSaveHelper) is the server's own copy, the one the engine loads.
$references += $sandBox

$sources = @(Get-ChildItem -LiteralPath (Join-Path $scriptDir 'src') -Filter '*.cs' | Sort-Object Name |
    ForEach-Object { $_.FullName })
if ($sources.Count -eq 0) { throw "No source files in $(Join-Path $scriptDir 'src')" }

$outDir = Join-Path $scriptDir 'bin'
New-Item -ItemType Directory -Path $outDir -Force | Out-Null
$output = Join-Path $outDir 'AD1259CoopCompat.dll'

$arguments = @(
    '-nologo', '-noconfig', '-nostdlib+', '-target:library', '-optimize+', '-debug-', '-deterministic',
    '-langversion:9', '-platform:anycpu', '-warnaserror-', '-nowarn:1701,1702',
    "-out:$output"
) + ($references | ForEach-Object { "-reference:$_" }) + $sources

if ($compilerHost) { & $compilerHost $csc @arguments } else { & $csc @arguments }
if ($LASTEXITCODE -ne 0) { throw "csc failed with exit code $LASTEXITCODE" }
Write-Host "Built $output from $($sources.Count) source file(s)"

$moduleDir = $ModuleRoot
$manifest = Join-Path $moduleDir 'SubModule.xml'
if (-not (Test-Path -LiteralPath $manifest)) { throw "Missing module manifest: $manifest" }
# Include both standard bin names: the server bootstrap can probe either directory.
foreach ($binName in @('Win64_Shipping_Server', 'Win64_Shipping_Client')) {
    $moduleBin = Join-Path $moduleDir ('bin\' + $binName)
    New-Item -ItemType Directory -Path $moduleBin -Force | Out-Null
    Copy-Item -LiteralPath $output -Destination $moduleBin -Force
}
$inputs = @($harmony, $sandBox, (Join-Path $serverBin 'TaleWorlds.CampaignSystem.dll'),
    (Join-Path $serverBin 'TaleWorlds.MountAndBlade.dll')) | ForEach-Object {
    [ordered]@{ File = [IO.Path]::GetFileName($_); SHA256 = (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash }
}
[xml]$moduleXml = Get-Content -LiteralPath $manifest -Raw
$info = [ordered]@{ ModuleId = 'DedicatedServer.AD1259CoopCompat'; Version = [string]$moduleXml.Module.Version.value;
    BuiltUtc = [DateTime]::UtcNow.ToString('o'); DllSHA256 = (Get-FileHash -LiteralPath $output).Hash; References = @($inputs) }
[IO.File]::WriteAllText((Join-Path $scriptDir 'build-info.json'), ($info | ConvertTo-Json -Depth 5),
    (New-Object Text.UTF8Encoding($false)))
Write-Host "Packaged standalone module: $moduleDir"
