// Coodsz Server Tools: Max clans per kingdom. Keeps one kingdom from taking every clan on a big
// server. The rule is in ClanCapRules.cs; this file reads the settings file and patches the
// places where a clan can join a kingdom on the dedicated server.

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.BarterSystem.Barterables;
using TaleWorlds.CampaignSystem.Party;
using Coodsz.ServerRules;
using static CoodszServerTools.Text;

namespace CoodszServerTools;

/// <summary>
/// Refuses a clan joining a kingdom that already has the most clans the host allows.
/// </summary>
/// <remarks>
/// <para>
/// Capped: players joining as vassals (Coop's VassalServiceHandler and the player to player
/// vassal offer), a player ruler recruiting an AI lord (Coop's lord barter, refused with a reason
/// the player sees), the AI's own join and defection barters (their value is made hopeless, so the
/// AI simply does not pick that kingdom), and with countMercenaries also mercenary contracts.
/// Any other <c>ChangeKingdomAction</c> join (cheats, debug commands) is skipped as a last guard.
/// </para>
/// <para>
/// Not capped: creating a kingdom, rebellions, clans leaving, marriages (heroes move, clans do not)
/// and a ruler making a companion into a new clan. Clans already in a kingdom always stay.
/// </para>
/// </remarks>
internal static class ClanCap
{
    private const int Hopeless = -1000000;
    private static readonly object Gate = new object();
    private static ClanCapSettings _settings = ClanCapSettings.Off;
    private static DateTime _fileTime = DateTime.MinValue;
    private static int _checkedAt = int.MinValue;
    private static bool _readOnce;
    private static readonly HashSet<string> LoggedScores = new HashSet<string>();
    private static Harmony? _harmony;
    private static bool _gamePatched;
    private static bool _coopPatched;

    public static string SettingsPath => ClanCapSettings.PathIn(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), Environment.GetEnvironmentVariable(ClanCapSettings.ServerDataDirVariable));

    /// <summary>The settings file, read again within a few seconds of a change.</summary>
    public static ClanCapSettings Settings
    {
        get
        {
            lock (Gate)
            {
                var now = Environment.TickCount;
                if (_checkedAt != int.MinValue && unchecked(now - _checkedAt) < 5000) return _settings;
                _checkedAt = now;
                // The first read is told by the start up line; later changes get their own line.
                var wasRead = _readOnce;
                _readOnce = true;
                try
                {
                    var path = SettingsPath;
                    var time = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
                    if (time == _fileTime) return _settings;
                    _fileTime = time;
                    var read = time == DateTime.MinValue ? ClanCapSettings.Off : ClanCapSettings.Parse(File.ReadAllText(path));
                    if (wasRead && (read.MaxClans != _settings.MaxClans || read.CountMercenaries != _settings.CountMercenaries))
                        Log("Max clans per kingdom: " + read.Describe() + ".");
                    _settings = read;
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
                {
                    // A file being written is read on the next check.
                    _fileTime = DateTime.MinValue;
                }

                return _settings;
            }
        }
    }

    public static void Log(string line)
    {
        // One line in the server log (Debug.Print would print it a second time).
        try { Console.WriteLine("[Coodsz Server Tools] " + line); } catch { }
    }

    /// <summary>
    /// True while the cap is off (maxClansPerKingdom 0, or no file). Every patch checks it first and
    /// then returns at once, so a server that does not use the cap runs exactly as before.
    /// </summary>
    public static bool Inert => !Settings.Enabled;

    /// <summary>The clans that count toward a kingdom's size.</summary>
    public static int Count(Kingdom kingdom, ClanCapSettings settings) =>
        kingdom.Clans.Count(c => c != null && !c.IsEliminated && settings.Counts(c.IsUnderMercenaryService));

    /// <summary>Why <paramref name="clan"/> may not join <paramref name="kingdom"/>, or null when it may.</summary>
    public static string? Refusal(Clan? clan, Kingdom? kingdom, bool asMercenary)
    {
        var settings = Settings;
        if (!settings.Enabled || clan is null || kingdom is null) return null;
        var already = clan.Kingdom == kingdom && settings.Counts(clan.IsUnderMercenaryService);
        var count = Count(kingdom, settings);
        if (!settings.Refuses(count, settings.Counts(asMercenary), already)) return null;
        return kingdom.Name + " already has " + I(count) + " clans, the most this server allows (" + I(settings.MaxClans) + ").";
    }

    private static string? RefuseAndLog(Clan? clan, Kingdom? kingdom, bool asMercenary, string how)
    {
        var reason = Refusal(clan, kingdom, asMercenary);
        if (reason != null)
            Log("Blocked " + clan?.Name + " (" + clan?.StringId + ") joining " + kingdom?.Name + " (" + kingdom?.StringId + ") " + how + ": " + reason);
        return reason;
    }

    // ---- patches ---------------------------------------------------------------------------

    /// <summary>The game's own joins: patched once, when the module loads on the server.</summary>
    public static void PatchGame()
    {
        if (_gamePatched) return;
        _gamePatched = true;
        try
        {
            _harmony ??= new Harmony(ToolsSubModule.HarmonyId);
            _harmony.Patch(AccessTools.Method(typeof(ChangeKingdomAction), "ApplyInternal"),
                prefix: new HarmonyMethod(typeof(ClanCap), nameof(ApplyInternalPrefix)));
            // A refused defection skips the whole action, so OnClanDefected does not fire for nothing.
            _harmony.Patch(AccessTools.Method(typeof(ChangeKingdomAction), nameof(ChangeKingdomAction.ApplyByJoinToKingdomByDefection)),
                prefix: new HarmonyMethod(typeof(ClanCap), nameof(DefectionPrefix)));
            _harmony.Patch(AccessTools.Method(typeof(JoinKingdomAsClanBarterable), nameof(JoinKingdomAsClanBarterable.GetUnitValueForFaction)),
                postfix: new HarmonyMethod(typeof(ClanCap), nameof(JoinValuePostfix)));
            _harmony.Patch(AccessTools.Method(typeof(MercenaryJoinKingdomBarterable), nameof(MercenaryJoinKingdomBarterable.GetUnitValueForFaction)),
                postfix: new HarmonyMethod(typeof(ClanCap), nameof(MercenaryValuePostfix)));
        }
        catch (Exception e)
        {
            Log("Max clans per kingdom could not patch the game: " + e.GetType().Name + ": " + e.Message);
        }
    }

    /// <summary>Coop's server handlers: patched once every module is loaded.</summary>
    public static void PatchCoop()
    {
        if (_coopPatched || _harmony is null) return;
        // Coop loads before this module; until its GameInterface is there, there is nothing to patch.
        if (AccessTools.TypeByName("GameInterface.Services.Clans.Handlers.VassalServiceHandler") is null) return;
        _coopPatched = true;
        var harmony = _harmony;
        var patched = new List<string>();
        void Patch(string type, string method, string prefix, string? postfix = null)
        {
            var target = AccessTools.Method(type + ":" + method);
            if (target is null) { Log("Max clans per kingdom: " + type + "." + method + " was not found, so that join is only guarded by the last check."); return; }
            harmony.Patch(target,
                prefix: postfix is null ? new HarmonyMethod(typeof(ClanCap), prefix) : null,
                postfix: postfix is null ? null : new HarmonyMethod(typeof(ClanCap), postfix));
            patched.Add(type.Substring(type.LastIndexOf('.') + 1) + "." + method);
        }

        try
        {
            Patch("GameInterface.Services.Clans.Handlers.VassalServiceHandler", "ApplyVassalage", nameof(VassalPrefix));
            Patch("GameInterface.Services.Clans.Handlers.MercenaryServiceHandler", "ApplyMercenaryService", nameof(MercenaryPrefix));
            Patch("GameInterface.Services.MapEvents.PlayerPartyInteractions.PlayerPartyInteractionOutcomeHandler", "ApplyVassalage", nameof(PartyVassalPrefix));
            Patch("GameInterface.Services.MapEvents.PlayerPartyInteractions.PlayerPartyInteractionOutcomeHandler", "ApplyMercenaryJoin", nameof(PartyMercenaryPrefix));
            Patch("GameInterface.Services.Barters.Handlers.LordBarterHandler", "CanAuthorizeKind", "", nameof(LordBarterPostfix));
            Log("Max clans per kingdom: " + Settings.Describe() + ". Watching " + string.Join(", ", patched) + ".");
        }
        catch (Exception e)
        {
            Log("Max clans per kingdom could not patch Coop: " + e.GetType().Name + ": " + e.Message);
        }
    }

    private static bool DefectionPrefix(Clan clan, Kingdom newKingdom)
    {
        try { if (Inert) return true; return RefuseAndLog(clan, newKingdom, asMercenary: false, "by defection") is null; }
        catch (Exception) { return true; }
    }

    // Last guard for any join the patches below did not see (cheats, debug commands).
    private static bool ApplyInternalPrefix(Clan clan, Kingdom newKingdom, ChangeKingdomAction.ChangeKingdomActionDetail detail)
    {
        try
        {
            if (Inert) return true;
            if (detail != ChangeKingdomAction.ChangeKingdomActionDetail.JoinKingdom &&
                detail != ChangeKingdomAction.ChangeKingdomActionDetail.JoinKingdomByDefection &&
                detail != ChangeKingdomAction.ChangeKingdomActionDetail.JoinAsMercenary)
                return true;
            var mercenary = detail == ChangeKingdomAction.ChangeKingdomActionDetail.JoinAsMercenary;
            return RefuseAndLog(clan, newKingdom, mercenary, mercenary ? "as a mercenary" : "as a vassal") is null;
        }
        catch (Exception) { return true; }
    }

    // The AI weighs a join with these values; a full kingdom is never worth it, to either side.
    private static void JoinValuePostfix(JoinKingdomAsClanBarterable __instance, ref int __result)
    {
        try
        {
            if (Inert) return;
            var clan = __instance.OriginalOwner?.Clan;
            var mercenary = clan != null && clan.IsMinorFaction && clan != Clan.PlayerClan;
            if (Refusal(clan, __instance.TargetKingdom, mercenary) is null) return;
            __result = Hopeless;
            LogScoreOnce(clan, __instance.TargetKingdom);
        }
        catch (Exception) { }
    }

    private static readonly AccessTools.FieldRef<MercenaryJoinKingdomBarterable, Kingdom>? MercenaryTarget =
        AccessTools.FieldRefAccess<MercenaryJoinKingdomBarterable, Kingdom>("_targetKingdom");

    private static void MercenaryValuePostfix(MercenaryJoinKingdomBarterable __instance, ref int __result)
    {
        try
        {
            if (Inert) return;
            var kingdom = MercenaryTarget?.Invoke(__instance);
            var clan = __instance.OriginalOwner?.Clan;
            if (Refusal(clan, kingdom, asMercenary: true) is null) return;
            __result = Hopeless;
            LogScoreOnce(clan, kingdom);
        }
        catch (Exception) { }
    }

    private static void LogScoreOnce(Clan? clan, Kingdom? kingdom)
    {
        lock (Gate)
        {
            if (LoggedScores.Count > 500) LoggedScores.Clear();
            if (!LoggedScores.Add(clan?.StringId + "|" + kingdom?.StringId)) return;
        }

        Log("Blocked " + clan?.Name + " (" + clan?.StringId + ") from choosing to join " + kingdom?.Name + " (" + kingdom?.StringId + "): " + Refusal(clan, kingdom, false));
    }

    // Coop: a player asks a kingdom's ruler to become a vassal. Refused before anything changes;
    // Coop then tells the player the agreement could not be completed.
    private static bool VassalPrefix(object __instance, string clanId, string heroId, string kingdomId, ref bool __result)
    {
        try
        {
            if (Inert) return true;
            var clan = CoopObject<Clan>(__instance, clanId);
            var kingdom = CoopObject<Kingdom>(__instance, kingdomId);
            var reason = RefuseAndLog(clan, kingdom, asMercenary: false, "as a vassal");
            if (reason is null) return true;
            Tell(__instance, heroId, reason);
            __result = false;
            return false;
        }
        catch (Exception) { return true; }
    }

    // Coop: a player signs on as a mercenary, or a player ruler hires a mercenary clan.
    private static bool MercenaryPrefix(object __instance, string clanId, string heroId, string kingdomId)
    {
        try
        {
            if (Inert) return true;
            var reason = RefuseAndLog(CoopObject<Clan>(__instance, clanId), CoopObject<Kingdom>(__instance, kingdomId), asMercenary: true, "as a mercenary");
            if (reason is null) return true;
            Tell(__instance, heroId, reason);
            return false;
        }
        catch (Exception) { return true; }
    }

    // Coop: one player accepts another player's clan as a vassal or mercenary.
    private static bool PartyVassalPrefix(object __instance, object[] __args, ref bool __result) => PartyJoin(__instance, __args, false, ref __result);
    private static bool PartyMercenaryPrefix(object __instance, object[] __args, ref bool __result) => PartyJoin(__instance, __args, true, ref __result);

    private static bool PartyJoin(object handler, object[] args, bool mercenary, ref bool result)
    {
        try
        {
            if (Inert) return true;
            var outcome = args.Length > 0 ? args[0] : null;
            if (outcome is null) return true;
            var partyId = Field(outcome, "InitiatorPartyId") as string;
            var party = partyId is null ? null : CoopObject<PartyBase>(handler, partyId);
            var clan = party?.LeaderHero?.Clan ?? party?.MobileParty?.ActualClan;
            if (RefuseAndLog(clan, Field(outcome, "TargetKingdom") as Kingdom, mercenary, mercenary ? "as a mercenary" : "as a vassal") is null) return true;
            result = false;
            return false;
        }
        catch (Exception) { return true; }
    }

    // Coop: a player ruler recruits an AI lord through a barter. The player sees the reason.
    // Only the check made while building the barter (peer null) refuses, so the barter screen opens
    // and the refusal comes with the result instead of a silent missing authorization. Coop only
    // passes a target kingdom for a JoinKingdomAsClan barter.
    private static void LordBarterPostfix(object peer, Hero targetHero, Kingdom targetKingdom, ref bool __result, ref string reason)
    {
        try
        {
            if (Inert) return;
            if (!__result || peer != null || targetKingdom is null) return;
            var refusal = RefuseAndLog(targetHero?.Clan, targetKingdom, asMercenary: false, "through a lord barter");
            if (refusal is null) return;
            __result = false;
            reason = refusal;
        }
        catch (Exception) { }
    }

    private static object? Field(object target, string name) => CoopBridge.Field(target, name);

    private static T? CoopObject<T>(object handler, string id) where T : class => CoopBridge.CoopObject<T>(handler, id);

    /// <summary>Sends the player a System line in Coop's chat. Best effort: Coop's own refusal still shows.</summary>
    private static void Tell(object handler, string heroId, string text) => CoopBridge.TellHero(handler, heroId, text);

    // ---- coodsz.clancap ----------------------------------------------------------------------

    /// <summary>
    /// <c>coodsz.clancap [clan kingdom]</c>: the cap and each kingdom's count. With a clan and a
    /// kingdom it also says whether that clan may join, and what the AI's join barter is worth.
    /// <code>
    /// CLANCAP|max|count mercenaries|settings file|file found
    /// KINGDOM|id|name|counted|clans|mercenaries|full
    /// TRY|clan id|kingdom id|allowed or the reason|AI value for the clan|AI value for the kingdom
    /// </code>
    /// </summary>
    public static string Command(List<string> args)
    {
        if (Campaign.Current is null) return NoCampaign;
        var settings = Settings;
        var lines = new List<string>
        {
            Line("CLANCAP", I(settings.MaxClans), B(settings.CountMercenaries), SettingsPath, B(File.Exists(SettingsPath)))
        };
        foreach (var k in NotNull(Campaign.Current.Kingdoms))
        {
            if (k.IsEliminated) continue;
            var counted = Count(k, settings);
            var mercenaries = k.Clans.Count(c => c != null && !c.IsEliminated && c.IsUnderMercenaryService);
            lines.Add(Line("KINGDOM", k.StringId, Clean(k.Name?.ToString()), I(counted), I(k.Clans.Count(c => c != null && !c.IsEliminated)),
                I(mercenaries), B(settings.Enabled && counted >= settings.MaxClans)));
        }

        if (args.Count >= 2)
        {
            var clan = NotNull(Clan.All).FirstOrDefault(c => string.Equals(c.StringId, args[0], StringComparison.OrdinalIgnoreCase));
            var kingdom = NotNull(Campaign.Current.Kingdoms).FirstOrDefault(k => string.Equals(k.StringId, args[1], StringComparison.OrdinalIgnoreCase));
            if (clan is null || kingdom is null) return "COODSZ_ERROR clan '" + Clean(args[0]) + "' or kingdom '" + Clean(args[1]) + "' not found.";
            var barter = clan.Leader is null ? null : new JoinKingdomAsClanBarterable(clan.Leader, kingdom);
            lines.Add(Line("TRY", clan.StringId, kingdom.StringId, Clean(Refusal(clan, kingdom, clan.IsMinorFaction) ?? "allowed"),
                barter is null ? "" : I(barter.GetUnitValueForFaction(clan)), barter is null ? "" : I(barter.GetUnitValueForFaction(kingdom))));
        }

        return Lines(lines);
    }
}
