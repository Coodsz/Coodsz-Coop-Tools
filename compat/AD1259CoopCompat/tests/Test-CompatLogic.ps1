#requires -Version 5.1
<#
.SYNOPSIS
    Offline checks for AD1259CoopCompat.dll: verifies the patch targets exist in the server's game
    assemblies and exercises the world-check, cache-redirect and fresh-campaign logic against the
    real module data and the server's own default_new_game template.
#>
[CmdletBinding()]
param([string]$ServerRoot, [string]$ModuleRoot)

$ErrorActionPreference = 'Stop'
$patchRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
if ([string]::IsNullOrWhiteSpace($ServerRoot)) {
    $ServerRoot = Join-Path $patchRoot '..\Bannerlord-coop\steamapps\workshop\content\261550\3770450698\DedicatedServer'
}
$ServerRoot = [System.IO.Path]::GetFullPath($ServerRoot)
if ([string]::IsNullOrWhiteSpace($ModuleRoot)) { $ModuleRoot = Join-Path $patchRoot '..\AD1259-Coop-CompatPatch' }
$ModuleRoot = [IO.Path]::GetFullPath($ModuleRoot)
$serverBin = Join-Path $ServerRoot 'engine\bin\Win64_Shipping_Server'
$modules = Join-Path $ServerRoot 'engine\Modules'

$script:failures = 0
function Assert-True([bool]$Condition, [string]$Name) {
    if ($Condition) { Write-Host "PASS  $Name" } else { Write-Host "FAIL  $Name"; $script:failures++ }
}

# Resolve the game's own assemblies (SandBox, Coop) from the server's folders while reflecting.
$probeDirs = @(
    $serverBin,
    (Join-Path $modules 'SandBox\bin\Win64_Shipping_Server'),
    (Join-Path $modules 'Coop\bin\Win64_Shipping_Server'),
    (Join-Path $modules 'Bannerlord.Harmony\bin\Win64_Shipping_Server')
)
$resolver = [ResolveEventHandler] {
    param($sender, $eventArgs)
    $simpleName = (New-Object Reflection.AssemblyName $eventArgs.Name).Name
    foreach ($dir in $probeDirs) {
        $candidate = Join-Path $dir ($simpleName + '.dll')
        if (Test-Path -LiteralPath $candidate) { return [Reflection.Assembly]::LoadFrom($candidate) }
    }
    return $null
}
[AppDomain]::CurrentDomain.add_AssemblyResolve($resolver)

try {
    # 1. Patch targets exist in the server's own assemblies.
    $campaign = [Reflection.Assembly]::LoadFrom((Join-Path $serverBin 'TaleWorlds.CampaignSystem.dll'))
    $settlementType = $campaign.GetType('TaleWorlds.CampaignSystem.Settlements.Settlement')
    $openCache = $campaign.GetType('TaleWorlds.CampaignSystem.Map.DistanceCache.NavigationCache`1')
    $deserialize = $openCache.MakeGenericType($settlementType).GetMethod(
        'Deserialize', [Reflection.BindingFlags]'Public,NonPublic,Instance', $null, [type[]]@([string]), $null)
    Assert-True ($null -ne $deserialize) 'NavigationCache<Settlement>.Deserialize(string) exists'
    $mapScene = $campaign.GetType('TaleWorlds.CampaignSystem.Map.IMapScene')
    Assert-True ($null -ne $mapScene.GetMethod('GetFaceTerrainType')) 'IMapScene.GetFaceTerrainType exists'
    $companions = $campaign.GetType('TaleWorlds.CampaignSystem.CampaignBehaviors.CompanionsCampaignBehavior')
    $instanceFlags = [Reflection.BindingFlags]'Public,NonPublic,Instance'
    $toSpawn = $companions.GetMethod('GetCompanionTemplateToSpawn', $instanceFlags, $null, [type[]]@(), $null)
    Assert-True ($null -ne $toSpawn -and $toSpawn.ReturnType.Name -eq 'CharacterObject') 'CompanionsCampaignBehavior.GetCompanionTemplateToSpawn() exists'
    Assert-True ($null -ne $companions.GetMethod('GetCompanionTemplateTypeToSpawn', $instanceFlags, $null, [type[]]@(), $null)) 'CompanionsCampaignBehavior.GetCompanionTemplateTypeToSpawn() exists'
    Assert-True ($null -ne $companions.GetField('_companionsOfTemplates', $instanceFlags)) 'CompanionsCampaignBehavior._companionsOfTemplates exists'
    $gameInterface = Join-Path $modules 'Coop\bin\Win64_Shipping_Server\GameInterface.dll'
    if (Test-Path -LiteralPath $gameInterface) {
        try {
            $coopPatches = [Reflection.Assembly]::LoadFrom($gameInterface).GetType(
                'GameInterface.Services.Companions.Patches.Disable.CompanionsCampaignBehaviorPatches', $false)
            $coopPostfix = if ($coopPatches) { $coopPatches.GetMethod('GetCompanionTemplateToSpawnPostfix') } else { $null }
            Assert-True ($null -ne $coopPostfix) "Coop's GetCompanionTemplateToSpawnPostfix (the one the guard runs before) exists"
        }
        catch {
            Write-Host "SKIP  Coop's companion patch could not be read here: $($_.Exception.GetBaseException().Message)"
        }
    }
    else {
        Write-Host "SKIP  no Coop GameInterface.dll under $modules"
    }

    # 2. Engine-independent logic.
    $compat = [Reflection.Assembly]::LoadFrom((Join-Path $ModuleRoot 'bin\Win64_Shipping_Server\AD1259CoopCompat.dll'))
    $logic = $compat.GetType('AD1259CoopCompat.CompatLogic')
    function Invoke-Logic([string]$Method, [object[]]$Arguments) {
        $raw = New-Object 'object[]' $Arguments.Length
        for ($i = 0; $i -lt $Arguments.Length; $i++) {
            $value = $Arguments[$i]
            $raw[$i] = if ($value -is [System.Management.Automation.PSObject]) { $value.psobject.BaseObject } else { $value }
        }
        $logic.GetMethod($Method).Invoke($null, $raw)
    }

    $adSettlements = Invoke-Logic 'ReadModuleXmlIds' @((Join-Path $modules 'AD1259'), 'Settlements', 'Settlement')
    $adKingdoms = Invoke-Logic 'ReadModuleXmlIds' @((Join-Path $modules 'AD1259'), 'Kingdoms', 'Kingdom')
    $vanillaSettlements = Invoke-Logic 'ReadModuleXmlIds' @((Join-Path $modules 'SandBox'), 'Settlements', 'Settlement')
    $vanillaKingdoms = Invoke-Logic 'ReadModuleXmlIds' @((Join-Path $modules 'SandBox'), 'Kingdoms', 'Kingdom')
    Write-Host ("      AD1259: {0} settlements, {1} kingdoms; SandBox: {2} settlements, {3} kingdoms" -f
        $adSettlements.Count, $adKingdoms.Count, $vanillaSettlements.Count, $vanillaKingdoms.Count)
    Assert-True ($adSettlements.Count -gt 1000) 'AD1259 settlement ids parsed'
    Assert-True ($adKingdoms.Count -gt 20) 'AD1259 kingdom ids parsed (folder-form XmlNode)'
    Assert-True ($vanillaSettlements.Count -gt 100) 'SandBox settlement ids parsed'

    function Invoke-Evaluate([string[]]$WorldSettlements, [int]$Broken, [string[]]$WorldKingdoms) {
        $argv = New-Object 'object[]' 6
        $argv[0] = (New-Object 'System.Collections.Generic.List[string]' (,$WorldSettlements)).psobject.BaseObject
        $argv[1] = $Broken
        $argv[2] = (New-Object 'System.Collections.Generic.List[string]' (,$WorldKingdoms)).psobject.BaseObject
        $argv[3] = (New-Object 'System.Collections.Generic.List[string]' (,[string[]]$adSettlements)).psobject.BaseObject
        $argv[4] = (New-Object 'System.Collections.Generic.List[string]' (,[string[]]$adKingdoms)).psobject.BaseObject
        $argv[5] = 'AD1259'
        $logic.GetMethod('EvaluateWorld').Invoke($null, $argv)
    }
    $vanillaWorld = Invoke-Evaluate $vanillaSettlements 120 $vanillaKingdoms
    Assert-True ($vanillaWorld.Verdict.ToString() -eq 'Fatal') "vanilla template world on AD1259 -> Fatal ($($vanillaWorld.Summary))"
    Assert-True ($vanillaWorld.Message -match 'Coodsz Host') 'the Fatal message points at a new campaign in Coodsz Host'
    $adWorld = Invoke-Evaluate $adSettlements 0 $adKingdoms
    Assert-True ($adWorld.Verdict.ToString() -eq 'Ok') 'AD1259-created world on AD1259 -> Ok'
    $mixedWorld = Invoke-Evaluate ([string[]](@($adSettlements) + @($vanillaSettlements))) 0 $adKingdoms
    Assert-True ($mixedWorld.Verdict.ToString() -ne 'Ok') "mixed vanilla+AD1259 world is flagged ($($mixedWorld.Verdict))"

    # 3. Distance-cache redirect, resolved relative to the server's working directory.
    $previous = [Environment]::CurrentDirectory
    [Environment]::CurrentDirectory = $serverBin
    try {
        $original = '..\..\Modules\SandBox/ModuleData\DistanceCaches\settlements_distance_cache_Default.bin'
        $redirect = Invoke-Logic 'ComputeCacheRedirect' @($original, '..\..\Modules\AD1259/')
        Assert-True ($redirect -eq '..\..\Modules\AD1259/ModuleData/DistanceCaches/settlements_distance_cache_Default.bin') "SandBox cache -> AD1259 cache ($redirect)"
        Assert-True ($null -eq (Invoke-Logic 'ComputeCacheRedirect' @('..\..\Modules\AD1259/ModuleData/DistanceCaches/x.bin', '..\..\Modules\AD1259/'))) 'non-SandBox path is left alone'
        Assert-True ($null -eq (Invoke-Logic 'ComputeCacheRedirect' @($original, '..\..\Modules\RBM/'))) 'provider without a cache is left alone'
        Assert-True ([bool](Invoke-Logic 'IsSandBoxDistanceCachePath' @('C:/x/Modules/SandBox/ModuleData/DistanceCaches/a.bin'))) 'forward-slash SandBox path recognised'
    }
    finally {
        [Environment]::CurrentDirectory = $previous
    }

    # 4. Fresh-campaign decision, using the server's shipped default_new_game template.
    $freshLogic = $compat.GetType('AD1259CoopCompat.FreshCampaignLogic')
    $scratchRoot = if ($env:KIROCREW_SCRATCH) { $env:KIROCREW_SCRATCH } else { [IO.Path]::GetTempPath() }
    $scratch = Join-Path $scratchRoot ('ad1259coop-test-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $scratch | Out-Null
    try {
        $template = Join-Path $ServerRoot 'server-data\Game Saves\default_new_game.sav'
        $templateCopy = Join-Path $scratch 'default_new_game.sav'
        Copy-Item -LiteralPath $template -Destination $templateCopy
        Copy-Item -LiteralPath $template -Destination (Join-Path $scratch 'Campaign_copy.sav')
        $bytes = [IO.File]::ReadAllBytes($template)
        $bytes[$bytes.Length - 1] = $bytes[$bytes.Length - 1] -bxor 0xFF
        [IO.File]::WriteAllBytes((Join-Path $scratch 'Campaign_real.sav'), $bytes)
        function Invoke-Decide([string]$Name) {
            $argv = New-Object 'object[]' 4
            $argv[0] = $Name
            $argv[1] = [string]$scratch
            $argv[2] = [string[]]@([string](Join-Path $scratch 'missing\default_new_game.sav'), [string]$templateCopy)
            $argv[3] = 'AD1259'
            $freshLogic.GetMethod('Decide').Invoke($null, $argv)
        }
        Assert-True ((Invoke-Decide 'Campaign_copy').StartFresh) 'untouched copy of default_new_game -> new AD1259 campaign'
        Assert-True (-not (Invoke-Decide 'Campaign_real').StartFresh) 'a save that differs by one byte is loaded normally'
        Assert-True (-not (Invoke-Decide 'default_new_game').StartFresh) 'the template itself is never replaced'
        Assert-True (-not (Invoke-Decide 'Missing').StartFresh) 'a missing save is left to the server'
        $candidates = $freshLogic.GetMethod('TemplateCandidates').Invoke($null, [object[]]@('C:\data', 'C:\bin\'))
        Assert-True ($candidates.Count -eq 2 -and $candidates[0] -eq 'C:\data\Game Saves\default_new_game.sav' -and
            $candidates[1] -eq 'C:\bin\default_new_game.sav') 'template looked up where the DS core copies it from'
    }
    finally {
        $resolvedScratch = [IO.Path]::GetFullPath($scratch)
        $resolvedScratchRoot = [IO.Path]::GetFullPath($scratchRoot).TrimEnd('\') + '\'
        if (-not $resolvedScratch.StartsWith($resolvedScratchRoot, [StringComparison]::OrdinalIgnoreCase) -or
            [IO.Path]::GetFileName($resolvedScratch) -notmatch '^ad1259coop-test-[0-9a-f]{32}$') {
            throw "Refusing test cleanup outside its allocated scratch directory: $resolvedScratch"
        }
        Remove-Item -LiteralPath $resolvedScratch -Recurse -Force
    }

    # 5. New-campaign hook targets in the server's SandBox.dll, CampaignSystem and Coop's GameInterface.dll.
    $all = [Reflection.BindingFlags]'Public,NonPublic,Static,Instance'
    try {
        $sandBoxAsm = [Reflection.Assembly]::LoadFrom((Join-Path $modules 'SandBox\bin\Win64_Shipping_Server\SandBox.dll'))
        $helper = $sandBoxAsm.GetType('SandBox.SandBoxSaveHelper')
        $load = @($helper.GetMethods($all) | Where-Object Name -eq 'LoadGameAction')
        Assert-True ($load.Count -eq 1 -and $load[0].GetParameters()[0].ParameterType.Name -eq 'SaveGameFileInfo') 'SandBoxSaveHelper.LoadGameAction(SaveGameFileInfo, ...) exists'
        $manager = $sandBoxAsm.GetType('SandBox.SandBoxGameManager')
        Assert-True ($null -ne $manager.GetMethod('LaunchSandboxCharacterCreation', $all)) 'SandBoxGameManager.LaunchSandboxCharacterCreation exists'
        Assert-True ($null -ne $manager.GetMethod('OnLoadFinished', $all)) 'SandBoxGameManager.OnLoadFinished exists'
        $subModule = $sandBoxAsm.GetType('SandBox.SandBoxSubModule')
        Assert-True ($null -ne $subModule -and $null -ne $subModule.GetMethod('RegisterSubModuleObjects', $all)) 'SandBoxSubModule.RegisterSubModuleObjects exists'
        $coreAsm = [Reflection.Assembly]::LoadFrom((Join-Path $serverBin 'TaleWorlds.Core.dll'))
        $banner = $coreAsm.GetType('TaleWorlds.Core.BannerManager')
        Assert-True ($null -ne $banner -and $null -ne $banner.GetMethod('Initialize', $all) -and
            @($banner.GetMethods($all) | Where-Object { $_.Name -eq 'LoadBannerIcons' -and $_.GetParameters().Count -eq 0 }).Count -eq 1 -and
            $null -ne $banner.GetField('ReadOnlyColorPalette', $all) -and $null -ne $banner.GetProperty('BannerIconGroups', $all)) 'BannerManager.Initialize / LoadBannerIcons() / palette and icon groups exist'
        $mountAndBlade = [Reflection.Assembly]::LoadFrom((Join-Path $serverBin 'TaleWorlds.MountAndBlade.dll'))
        $isLoaded = $mountAndBlade.GetType('TaleWorlds.MountAndBlade.MBGameManager').GetProperty('IsLoaded', $all)
        Assert-True ($null -ne $isLoaded -and $null -ne $isLoaded.GetSetMethod($true)) 'MBGameManager.IsLoaded has a setter'
        $ccManager = $campaign.GetType('TaleWorlds.CampaignSystem.CharacterCreationContent.CharacterCreationManager')
        foreach ($m in 'NextStage', 'StartNarrativeStage', 'GetSuitableNarrativeMenuOptions', 'OnNarrativeMenuOptionSelected',
                       'TrySwitchToNextMenu', 'ResetMenuOptions', 'GetTotalStagesCount') {
            Assert-True ($null -ne $ccManager.GetMethod($m, $all)) "CharacterCreationManager.$m exists"
        }
        $ccContent = $campaign.GetType('TaleWorlds.CampaignSystem.CharacterCreationContent.CharacterCreationContent')
        foreach ($m in 'SetSelectedCulture', 'GetCultures', 'AddCharacterCreationCulture', 'SetMainCharacterName') {
            Assert-True ($null -ne $ccContent.GetMethod($m, $all)) "CharacterCreationContent.$m exists"
        }
        $gameInterface = [Reflection.Assembly]::LoadFrom((Join-Path $modules 'Coop\bin\Win64_Shipping_Server\GameInterface.dll'))
        $coopPatch = $gameInterface.GetType('GameInterface.Services.Heroes.Patches.ChangePlayerCharacterActionPatches')
        $prefix = if ($coopPatch) { $coopPatch.GetMethod('Prefix', $all) } else { $null }
        Assert-True ($null -ne $prefix -and $prefix.ReturnType -eq [bool]) "Coop's ChangePlayerCharacterAction prefix exists and returns bool"
        $stateInterface = $gameInterface.GetType('GameInterface.Services.GameState.Interfaces.GameStateInterface')
        Assert-True ($null -ne $stateInterface -and $null -ne $stateInterface.GetMethod('ForceLoadSave', $all)) "Coop loads saves through ForceLoadSave (the hooked path)"
        $navCache = $campaign.GetType('TaleWorlds.CampaignSystem.Map.DistanceCache.SandBoxNavigationCache')
        Assert-True ($null -ne $navCache -and $null -ne $navCache.GetMethod('GetRealDistanceAndLandRatioBetweenSettlements', $all)) 'SandBoxNavigationCache.GetRealDistanceAndLandRatioBetweenSettlements exists (distance-miss guard)'
        $campaignType = $campaign.GetType('TaleWorlds.CampaignSystem.Campaign')
        $clanType = $campaign.GetType('TaleWorlds.CampaignSystem.Clan')
        Assert-True ($null -ne $campaignType.GetMethod('OnNewGameCreated', $all) -and
            $null -ne $clanType.GetMethod('SetInitialHomeSettlement', $all) -and
            $null -ne $clanType.GetMethod('CalculateMidSettlement', $all)) 'Campaign.OnNewGameCreated / Clan.SetInitialHomeSettlement / Clan.CalculateMidSettlement exist (placeholder-home fix)'
        # Coop's own minor faction: a home that AD1259 does not define is re-homed when a new campaign starts.
        $coopFactions = Join-Path $modules 'Coop\ModuleData\coop_minor_factions.xml'
        if (Test-Path -LiteralPath $coopFactions) {
            [xml]$factionXml = Get-Content -LiteralPath $coopFactions -Raw
            $homes = @($factionXml.Factions.Faction | Where-Object { $_.initial_home_settlement } |
                ForEach-Object { $_.initial_home_settlement -replace '^Settlement\.', '' })
            $missingHomes = @($homes | Where-Object { $adSettlements -notcontains $_ })
            Write-Host "INFO  Coop minor-faction homes AD1259 does not define (re-homed on a new campaign): $(if ($missingHomes.Count) { $missingHomes -join ', ' } else { 'none' })"
        }
    }
    catch {
        Write-Host "FAIL  hook-target reflection: $($_.Exception.GetBaseException().Message)"
        $script:failures++
    }

    # 5b. Companion template pick (crash CR-U2W9J: Coop read an empty template list).
    $companionLogic = $compat.GetType('AD1259CoopCompat.CompanionTemplateLogic')
    function Invoke-Pick([int[]]$Counts, [int]$Preferred, [int]$Roll) {
        $companionLogic.GetMethod('Pick').Invoke($null, [object[]]@((, $Counts) + @($Preferred, $Roll)))
    }
    $pick = Invoke-Pick @(0, 3, 0) 0 7
    Assert-True ($null -ne $pick -and $pick[0] -eq 1 -and $pick[1] -lt 3) 'an empty preferred type picks from a type that has templates'
    $pick = Invoke-Pick @(2, 3, 0) 0 5
    Assert-True ($null -ne $pick -and $pick[0] -eq 0 -and $pick[1] -eq 1) 'a preferred type with templates is used'
    Assert-True ($null -eq (Invoke-Pick @(0, 0, 0) 1 4)) 'no type with templates leaves the game''s null'
    Assert-True ($null -eq (Invoke-Pick @() -1 4)) 'no template types at all leaves the game''s null'
    $pick = Invoke-Pick @(0, 2, 0, 1) -1 2
    Assert-True ($null -ne $pick -and $pick[0] -eq 3 -and $pick[1] -eq 0) 'the pick spans every type that has templates'
    $pick = Invoke-Pick @(0, 2) 5 ([int]::MinValue)
    Assert-True ($null -ne $pick -and $pick[0] -eq 1) 'an out of range preferred type and a negative roll still pick safely'

    # 6. The DLL's own lookups. They go through the server's Harmony, which is a .NET 6 build, so this
    #    only runs under PowerShell 7+; Windows PowerShell 5.1 reports SKIP.
    try {
        $headless = $compat.GetType('AD1259CoopCompat.HeadlessCharacterCreation')
        Assert-True ([bool]$headless.GetProperty('IsSupported', $all).GetValue($null, $null)) 'the DLL itself resolves LaunchSandboxCharacterCreation and IsLoaded'
    }
    catch {
        Write-Host "SKIP  the DLL's own lookups need the server's .NET 6 runtime: $($_.Exception.GetBaseException().Message)"
    }
}
finally {
    [AppDomain]::CurrentDomain.remove_AssemblyResolve($resolver)
}

if ($script:failures -gt 0) { throw "$script:failures check(s) failed." }
Write-Host 'All checks passed.'
