param(
    [string]$Configuration = "Release",
    [string]$BannerlordGameRoot = "C:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord",
    [string]$DedicatedServerRoot = "C:\DedicatedServer\engine",
    [string]$HarmonyReferencePath = ""
)

$ErrorActionPreference = "Stop"
$project = Join-Path $PSScriptRoot "Source\CoopMarriage.csproj"
$buildRoot = Join-Path $PSScriptRoot "Source\_build"
$distRoot = Join-Path $PSScriptRoot "dist\CoopMarriage"

if ([string]::IsNullOrWhiteSpace($HarmonyReferencePath)) {
    $HarmonyReferencePath = Join-Path $BannerlordGameRoot "Modules\Coop\bin\Win64_Shipping_Client\0Harmony.dll"
}

dotnet build $project -c $Configuration -p:BuildFlavor=Client `
    -p:BannerlordGameRoot="$BannerlordGameRoot" `
    -p:DedicatedServerRoot="$DedicatedServerRoot" `
    -p:HarmonyReferencePath="$HarmonyReferencePath" `
    -p:OutputPath="$(Join-Path $buildRoot 'Client')"
if ($LASTEXITCODE -ne 0) { throw "Client build failed." }

dotnet build $project -c $Configuration -p:BuildFlavor=Server `
    -p:BannerlordGameRoot="$BannerlordGameRoot" `
    -p:DedicatedServerRoot="$DedicatedServerRoot" `
    -p:HarmonyReferencePath="$HarmonyReferencePath" `
    -p:OutputPath="$(Join-Path $buildRoot 'Server')"
if ($LASTEXITCODE -ne 0) { throw "Dedicated-server build failed." }

$resolvedDist = [System.IO.Path]::GetFullPath($distRoot)
$expectedParent = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot "dist"))
if (-not $resolvedDist.StartsWith($expectedParent + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Unsafe distribution path: $resolvedDist"
}
if (Test-Path -LiteralPath $resolvedDist) {
    Remove-Item -LiteralPath $resolvedDist -Recurse
}

New-Item -ItemType Directory -Path $resolvedDist | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot "SubModule.xml") -Destination $resolvedDist
Copy-Item -LiteralPath (Join-Path $PSScriptRoot "ModuleData") -Destination $resolvedDist -Recurse

$clientPlatforms = @("Win64_Shipping_Client", "Gaming.Desktop.x64_Shipping_Client")
foreach ($platform in $clientPlatforms) {
    $destination = Join-Path $resolvedDist "bin\$platform"
    New-Item -ItemType Directory -Path $destination -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $buildRoot "Client\CoopMarriage.dll") -Destination $destination
}
$serverDestination = Join-Path $resolvedDist "bin\Win64_Shipping_Server"
New-Item -ItemType Directory -Path $serverDestination -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $buildRoot "Server\CoopMarriage.dll") -Destination $serverDestination

$forbidden = Get-ChildItem -LiteralPath $resolvedDist -Recurse -File | Where-Object {
    $_.Name -in @("Common.dll", "GameInterface.dll", "Coop.dll", "Coop.Core.dll", "SandBox.dll", "StoryMode.dll", "Native.dll") -or
    $_.Name -like "TaleWorlds.*.dll"
}
if ($forbidden) {
    throw "Forbidden environment-owned assemblies found in distribution: $($forbidden.FullName -join ', ')"
}

Write-Host "CoopMarriage client/server build and clean distribution completed: $resolvedDist"
