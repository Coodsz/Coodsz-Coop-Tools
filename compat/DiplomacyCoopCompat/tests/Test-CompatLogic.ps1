#requires -Version 5.1
<#
.SYNOPSIS
    Offline checks for Diplomacy.CoopCompat.dll against the real Coop and Diplomacy DLLs, no game needed.
    - Coop's session state resolves on the installed Coop (0.1.7 renamed ContainerProvider._lifetimeScope to _active).
    - A Diplomacy type that needs a DLL which can't load counts as missing instead of throwing, which is what a
      dedicated server without UI DLLs sees.
    Run in Windows PowerShell 5.1 (the compat targets .NET Framework 4.7.2).
#>
[CmdletBinding()]
param(
    [string]$CompatDll,
    [string]$CoopBin = 'C:\Program Files (x86)\Steam\steamapps\workshop\content\261550\3770450698\bin\Win64_Shipping_Client',
    [string]$DiplomacyDll = 'C:\Program Files (x86)\Steam\steamapps\workshop\content\261550\2881380744\bin\Win64_Shipping_Client\Bannerlord.Diplomacy.1.4.8.dll',
    [string]$GameBin = 'C:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord\bin\Win64_Shipping_Client',
    [string]$HarmonyDll = 'C:\Program Files (x86)\Steam\steamapps\workshop\content\261550\2859188632\bin\Win64_Shipping_Client\0Harmony.dll'
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($CompatDll)) {
    $CompatDll = Join-Path (Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)) 'bin\Release\Diplomacy.CoopCompat.dll'
}
$script:failures = 0
function Assert-True([bool]$Condition, [string]$Name) {
    if ($Condition) { Write-Host "PASS  $Name" } else { Write-Host "FAIL  $Name"; $script:failures++ }
}

# Only the compat's own dependencies resolve: game TaleWorlds DLLs, Harmony and Coop. Diplomacy's UI
# dependencies (UIExtenderEx, MCM, Gauntlet view models) are left unresolved on purpose, like on a server.
$known = @{}
$known['0Harmony'] = $HarmonyDll
foreach ($dir in @($GameBin, $CoopBin)) {
    foreach ($file in [IO.Directory]::GetFiles($dir, '*.dll')) {
        $simpleName = [IO.Path]::GetFileNameWithoutExtension($file)
        # A dedicated server has no Gauntlet UI or view model DLLs; hide them here too.
        if ($simpleName -match '^TaleWorlds\.(CampaignSystem\.ViewModelCollection|GauntletUI|MountAndBlade\.GauntletUI|Core\.ViewModelCollection)') { continue }
        if (-not $known.ContainsKey($simpleName)) { $known[$simpleName] = $file }
    }
}
# No cmdlets in here: a cmdlet that needs an assembly would ask this resolver again.
$resolver = [ResolveEventHandler] {
    param($sender, $eventArgs)
    $comma = $eventArgs.Name.IndexOf(',')
    $simple = if ($comma -ge 0) { $eventArgs.Name.Substring(0, $comma) } else { $eventArgs.Name }
    if ($simple.EndsWith('.resources') -or -not $known.ContainsKey($simple)) { return $null }
    return [Reflection.Assembly]::LoadFrom($known[$simple])
}
[AppDomain]::CurrentDomain.add_AssemblyResolve($resolver)
try {
    $all = [Reflection.BindingFlags]'Public,NonPublic,Static,Instance'
    $compat = [Reflection.Assembly]::LoadFrom($CompatDll)

    # 1. Coop's session state on the installed Coop.
    [void][Reflection.Assembly]::LoadFrom((Join-Path $CoopBin 'GameInterface.dll'))
    [void][Reflection.Assembly]::LoadFrom((Join-Path $CoopBin 'Common.dll'))
    $coop = $compat.GetType('DiplomacyCoopCompat.Coop', $true)
    $inSession = [bool]$coop.GetProperty('InSession', $all).GetValue($null, $null)
    $scope = $coop.GetField('_scope', $all).GetValue($null)
    $isServer = $coop.GetField('_isServer', $all).GetValue($null)
    Assert-True ($null -ne $scope) "Coop's session field is found ($(if ($scope) { $scope.Name } else { 'none' }))"
    Assert-True ($null -ne $isServer) 'Common.ModInformation.IsServer is found'
    Assert-True (-not $inSession) 'no session is running outside a game, so the compat stays out of the way'

    # 2. A Diplomacy type whose base type lives in a DLL that can't load.
    $diplomacy = [Reflection.Assembly]::LoadFrom($DiplomacyDll)
    $typeIn = $compat.GetType('DiplomacyCoopCompat.SubModule', $true).GetMethod('TypeIn', $all)
    Assert-True ($null -ne $typeIn) 'type lookups go through the catching SubModule.TypeIn'
    if ($null -eq $typeIn) { throw "$($script:failures + 1) check(s) failed." }
    $threw = $false
    try { $direct = $diplomacy.GetType('Diplomacy.ViewModelMixin.KingdomTruceItemVMMixin', $false) } catch { $threw = $true }
    $safe = $null; $safeThrew = $false
    try { $safe = $typeIn.Invoke($null, [object[]]@($diplomacy, 'Diplomacy.ViewModelMixin.KingdomTruceItemVMMixin')) } catch { $safeThrew = $true }
    if ($threw) {
        Assert-True (-not $safeThrew -and $null -eq $safe) 'a type that needs a missing DLL counts as missing (Assembly.GetType throws for it)'
    }
    else {
        Write-Host "SKIP  Assembly.GetType did not throw for KingdomTruceItemVMMixin on this runtime (returned $(if ($direct) { 'the type' } else { 'null' })); the server's .NET runtime is not exercised here"
    }
    Assert-True ($null -eq $typeIn.Invoke($null, [object[]]@($diplomacy, 'Diplomacy.NoSuchType'))) 'an unknown type name counts as missing'
}
finally {
    [AppDomain]::CurrentDomain.remove_AssemblyResolve($resolver)
}

if ($script:failures -gt 0) { throw "$script:failures check(s) failed." }
Write-Host 'All checks passed.'
