using System;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.MountAndBlade;
using TaomCoop.Core;

namespace TaomFastForward;

public sealed class SubModule : MBSubModuleBase
{
    protected override void OnSubModuleLoad()
    {
        base.OnSubModuleLoad();
        FastForwardPatch.Install();
    }
}

public static class FastForwardPatch
{
    private static bool _installed;

    public static void Install()
    {
        if (_installed)
            return;
        var tick = AccessTools.Method("TAOM.Features.TimeAcceleration.TimeAccelerationService:OnTick");
        if (tick == null)
            return;
        new Harmony("taom.coop.fastforward").Patch(tick, prefix: new HarmonyMethod(typeof(FastForwardPatch), nameof(Prefix)));
        _installed = true;
    }

    public static bool Prefix(object __instance)
    {
        try
        {
            var coop = Read(__instance, "_coop");
            var session = CoopSession(coop);
            var server = ThisProcessIsServer(coop);
            var allowed = HostAllowsFastForward();
            if (FastForwardPress.SendButtonToServer(session, server, ButtonPressed(__instance)))
                SetSpeed(__instance, 2);
            return FastForwardPress.LeaveTaomTickAlone(session, allowed, server);
        }
        catch (Exception)
        {
            return true;
        }
    }

    private static bool CoopSession(object coop)
    {
        return Flag(coop, "ShouldDeferToHost") || Flag(coop, "IsCoopClient") || Flag(coop, "IsSessionActive");
    }

    private static bool ThisProcessIsServer(object coop)
    {
        return Flag(coop, "IsAuthority");
    }

    private static bool HostAllowsFastForward()
    {
        var options = AccessTools.TypeByName("GameInterface.Configuration.ModOptions");
        var property = options?.GetProperty("FastForwardEnabled", BindingFlags.Public | BindingFlags.Static);
        if (property == null || property.PropertyType != typeof(bool))
            return true;
        return (bool)property.GetValue(null);
    }

    private static bool ButtonPressed(object service)
    {
        var input = Read(service, "_input");
        return Flag(input, "IsFastForwardPressed") || Flag(input, "IsExtraFastForwardPressed") || Flag(input, "IsTurboPressed");
    }

    private static void SetSpeed(object service, int speed)
    {
        var time = Read(service, "_timeControl");
        var method = time?.GetType().GetMethod("SetTimeSpeed", BindingFlags.Instance | BindingFlags.Public);
        if (method == null)
            return;
        method.Invoke(time, new object[] { speed });
    }

    private static object Read(object instance, string field)
    {
        if (instance == null)
            return null;
        return AccessTools.Field(instance.GetType(), field)?.GetValue(instance);
    }

    private static bool Flag(object instance, string property)
    {
        if (instance == null)
            return false;
        var value = instance.GetType().GetProperty(property, BindingFlags.Instance | BindingFlags.Public)?.GetValue(instance);
        return value is bool flag && flag;
    }
}
