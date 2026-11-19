using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

namespace VanillaJoinGuard;

public sealed class SubModule : MBSubModuleBase
{
    protected override void OnSubModuleLoad()
    {
        base.OnSubModuleLoad();
        if (!ClientOnly.IsClient())
            return;
        new Harmony("coodsz.vanilla.joinguard").PatchAll(typeof(SubModule).Assembly);
        TaomFastForward.FastForwardPatch.Install();
        CameraPageFilter.Install();
    }

    protected override void OnGameStart(Game game, IGameStarter gameStarter)
    {
        base.OnGameStart(game, gameStarter);
        if (ClientOnly.IsClient())
            CameraPageFilter.Install();
    }

    protected override void OnBeforeInitialModuleScreenSetAsRoot()
    {
        base.OnBeforeInitialModuleScreenSetAsRoot();
        if (ClientOnly.IsClient())
            CameraPageFilter.Install();
    }
}

internal static class CameraPageFilter
{
    private static bool _installed;

    public static void Install()
    {
        var method = AccessTools.Method(AccessTools.TypeByName("MCM.UI.Utils.SettingPropertyDefinitionCache"), "GetSettingPropertyGroups");
        if (method == null)
            return;

        new Harmony("sermed.serversettingslock").Unpatch(method, HarmonyPatchType.Postfix, "sermed.serversettingslock");
        if (_installed)
            return;

        new Harmony("coodsz.vanilla.joinguard.camera").Patch(method, postfix: new HarmonyMethod(typeof(CameraPageFilter), nameof(Postfix)));
        _installed = true;
    }

    public static void Postfix(object settings, ref IEnumerable __result)
    {
        if (settings == null || __result == null)
            return;
        var settingsName = settings.GetType().FullName ?? "";
        if (settingsName.IndexOf("XorberaxLegacy", StringComparison.Ordinal) < 0)
            return;

        var elementType = ElementType(__result.GetType());
        if (elementType == null)
            return;

        var kept = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(elementType));
        foreach (var item in __result)
        {
            if (item == null || !elementType.IsInstanceOfType(item))
                continue;
            var type = item.GetType();
            var name = Read(type, item, "GroupName");
            var raw = Read(type, item, "GroupNameRaw");
            if (IsCamera(name) || IsCamera(raw))
                kept.Add(item);
        }

        if (kept.Count > 0)
            __result = (IEnumerable)kept;
    }

    private static Type ElementType(Type enumerableType)
    {
        if (enumerableType.IsGenericType && enumerableType.GetGenericTypeDefinition() == typeof(List<>))
            return enumerableType.GetGenericArguments()[0];
        foreach (var iface in enumerableType.GetInterfaces())
        {
            if (iface.IsGenericType && iface.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                return iface.GetGenericArguments()[0];
        }

        return null;
    }

    private static string Read(Type type, object group, string property)
    {
        return (type.GetProperty(property, BindingFlags.Instance | BindingFlags.Public)?.GetValue(group) as string) ?? "";
    }

    private static bool IsCamera(string name)
    {
        return name.IndexOf("Shoulder Camera", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("sc_ModuleName", StringComparison.Ordinal) >= 0;
    }
}

internal static class ClientOnly
{
    public static bool IsClient()
    {
        var args = Environment.GetCommandLineArgs();
        for (var i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], "/dedicatedcustomserver", StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }
}

[HarmonyPatch(typeof(InitialChildGenerationCampaignBehavior), "OnNewGameCreatedPartialFollowUp")]
public static class SkipClientChildGeneration
{
    public static bool Prefix() => !VanillaMap.IsActive();
}

[HarmonyPatch(typeof(HeroSpawnCampaignBehavior), "TrySpawnHeroesAndParties")]
public static class SkipKingdomPartySpawn
{
    public static bool Prefix(Clan clan) => !VanillaMap.IsActive() || clan.IsMinorFaction;
}

internal static class VanillaMap
{
    public static bool IsActive()
    {
        var value = Environment.GetEnvironmentVariable("COODSZ_CAMPAIGN_MAP");
        return !string.Equals(value, "MoreNations", StringComparison.OrdinalIgnoreCase);
    }
}
