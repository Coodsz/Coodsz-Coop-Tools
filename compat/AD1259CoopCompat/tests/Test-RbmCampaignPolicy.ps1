#requires -Version 7.0
<#
.SYNOPSIS
    Checks the installed RBM hook contract and tests the economy override without
    starting Bannerlord, calling RBM LoadConfig, or touching any configuration file.
#>
[CmdletBinding()]
param(
    [string]$ServerRoot,
    [string]$CompatDll
)

$ErrorActionPreference = 'Stop'
$patchRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($ServerRoot)) {
    $ServerRoot = Join-Path $patchRoot '..\Bannerlord-coop\steamapps\workshop\content\261550\3770450698\DedicatedServer'
}
$ServerRoot = (Get-Item -LiteralPath $ServerRoot).FullName
if ([string]::IsNullOrWhiteSpace($CompatDll)) {
    $CompatDll = @(
        (Join-Path $patchRoot '..\AD1259-Coop-CompatPatch\bin\Win64_Shipping_Server\AD1259CoopCompat.dll'),
        (Join-Path $patchRoot 'bin\AD1259CoopCompat.dll'),
        (Join-Path $patchRoot 'bin\Win64_Shipping_Server\AD1259CoopCompat.dll')
    ) | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
}
if (-not $CompatDll) { throw 'Build AD1259CoopCompat first, or pass -CompatDll with the built DLL path.' }
$CompatDll = (Get-Item -LiteralPath $CompatDll).FullName

$script:failures = 0
function Assert-True([bool]$Condition, [string]$Name) {
    if ($Condition) { Write-Host "PASS  $Name" }
    else { Write-Host "FAIL  $Name"; $script:failures++ }
}

# Cecil reads metadata without loading RBMConfig or running its static initializer.
$coopBin = Join-Path $ServerRoot 'engine\Modules\Coop\bin\Win64_Shipping_Server'
$cecilPath = Join-Path $coopBin 'Mono.Cecil.dll'
$null = [Reflection.Assembly]::LoadFrom($cecilPath)
$rbmConfigPath = Get-ChildItem -LiteralPath (Join-Path $ServerRoot 'engine\Modules\RBM\bin') -Recurse -File -Filter RBMConfig.dll |
    Select-Object -First 1 -ExpandProperty FullName
if (-not $rbmConfigPath) { throw 'Installed RBMConfig.dll was not found.' }
$rbmDefinition = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($rbmConfigPath)
try {
    $config = $rbmDefinition.MainModule.Types | Where-Object FullName -eq 'RBMConfig.RBMConfig'
    Assert-True ($null -ne $config) 'installed RBMConfig.RBMConfig type exists'
    $enabled = $config.Fields | Where-Object Name -eq 'rbmCampaignEnabled'
    Assert-True ($null -ne $enabled -and $enabled.IsPublic -and $enabled.IsStatic -and -not $enabled.IsInitOnly -and
        $enabled.FieldType.FullName -eq 'System.Boolean') 'RBM Campaign setting is a writable public static bool'
    $load = @($config.Methods | Where-Object { $_.Name -eq 'LoadConfig' -and $_.Parameters.Count -eq 0 })
    Assert-True ($load.Count -eq 1 -and $load[0].IsStatic -and $load[0].ReturnType.FullName -eq 'System.Void') 'RBM LoadConfig() hook is exactly static void with no arguments'
}
finally { $rbmDefinition.Dispose() }

$probeDirs = @(
    (Join-Path $ServerRoot 'engine\bin\Win64_Shipping_Server'),
    (Join-Path $ServerRoot 'engine\Modules\Bannerlord.Harmony\bin\Win64_Shipping_Server'),
    (Join-Path $ServerRoot 'engine\Modules\SandBox\bin\Win64_Shipping_Server'),
    $coopBin
)
$resolver = [ResolveEventHandler] {
    param($sender, $eventArgs)
    $simpleName = ([Reflection.AssemblyName]::new($eventArgs.Name)).Name
    foreach ($dir in $probeDirs) {
        $candidate = Join-Path $dir ($simpleName + '.dll')
        if (Test-Path -LiteralPath $candidate) { return [Reflection.Assembly]::LoadFrom($candidate) }
    }
    return $null
}
[AppDomain]::CurrentDomain.add_AssemblyResolve($resolver)
$policyField = $null
$originalField = $null
$fieldReplaced = $false
try {
    $assembly = [Reflection.Assembly]::LoadFrom($CompatDll)
    $policy = $assembly.GetType('AD1259CoopCompat.RbmCampaignPolicy', $true)
    $privateStatic = [Reflection.BindingFlags]'NonPublic,Static'
    $policyField = $policy.GetField('_campaignEnabled', $privateStatic)
    $apply = $policy.GetMethod('Apply', $privateStatic)
    Assert-True ($null -ne $policyField -and $policyField.FieldType -eq [Reflection.FieldInfo]) 'policy stores the reflected Campaign field'
    Assert-True ($null -ne $apply -and $apply.ReturnType -eq [void] -and $apply.GetParameters().Count -eq 0) 'policy override can be exercised without its native lifecycle'
    if ($null -eq $policyField -or $null -eq $apply) { throw 'Policy fixture contract is missing.' }

    if (-not ('AD1259PolicyOffline.Flags' -as [type])) {
        Add-Type -TypeDefinition @'
namespace AD1259PolicyOffline
{
    public static class Flags
    {
        public static bool Campaign = true;
        public static bool Combat = false;
        public static bool AI = true;
        public static void SimulateConfigReload() { Campaign = true; }
    }
}
'@
    }
    [AD1259PolicyOffline.Flags]::Campaign = $true
    [AD1259PolicyOffline.Flags]::Combat = $false
    [AD1259PolicyOffline.Flags]::AI = $true
    $fixtureField = [AD1259PolicyOffline.Flags].GetField('Campaign')
    $originalField = $policyField.GetValue($null)
    $policyField.SetValue($null, $fixtureField)
    $fieldReplaced = $true

    $null = $apply.Invoke($null, $null)
    Assert-True (-not [AD1259PolicyOffline.Flags]::Campaign) 'policy disables Campaign in the fixture'
    Assert-True (-not [AD1259PolicyOffline.Flags]::Combat -and [AD1259PolicyOffline.Flags]::AI) 'Combat and AI sentinels remain unchanged'

    [AD1259PolicyOffline.Flags]::SimulateConfigReload()
    Assert-True ([AD1259PolicyOffline.Flags]::Campaign) 'synthetic reload re-enables Campaign before override'
    $null = $apply.Invoke($null, $null)
    Assert-True (-not [AD1259PolicyOffline.Flags]::Campaign) 'policy reapplies the override after synthetic reload'
    Assert-True (-not [AD1259PolicyOffline.Flags]::Combat -and [AD1259PolicyOffline.Flags]::AI) 'reapplication also preserves Combat and AI'
}
finally {
    if ($fieldReplaced) {
        $policyField.SetValue($null, $originalField)
        Assert-True ([object]::ReferenceEquals($policyField.GetValue($null), $originalField)) 'policy reflected field is restored after the fixture'
    }
    [AppDomain]::CurrentDomain.remove_AssemblyResolve($resolver)
}

if ($script:failures -gt 0) { throw "$script:failures RBM policy check(s) failed." }
Write-Host 'All offline RBM policy checks passed. No native lifecycle or RBM config loader was invoked.'
