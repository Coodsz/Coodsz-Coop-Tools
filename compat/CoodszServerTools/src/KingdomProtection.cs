// Coodsz Server Tools: player kingdom protection, the Always choice of Coodsz Host's server rules.
// The rule is in ServerRulesRules.cs (KingdomProtectionRule, PlayerKingdoms); this file hooks the game.
// Parts adapted from ModderLords by Ryvius (@PlueRyvius), used with permission.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Election;
using Coodsz.ServerRules;

namespace CoodszServerTools;

/// <summary>
/// Always: AI kingdoms do not propose or declare war on a kingdom a player rules or serves as a vassal,
/// whether that player is online or not.
/// </summary>
/// <remarks>
/// <para>
/// "Only while their players are offline" and "Off" are Coop's own modOptions.blockAiWarDeclarationsOnOfflinePlayers,
/// which Coodsz Host writes into mod-config.json; this class does nothing for them.
/// </para>
/// <para>
/// Three hooks, all on the server only, all on the AI's own war path:
/// a prefix on KingdomDecisionProposalBehavior.ConsiderWar(Clan, Kingdom, IFaction) answers false for a
/// protected target, so the AI clan never proposes it; a postfix on GetRandomWarDecision(Clan) drops a
/// DeclareWarDecision against a protected target that still comes out (a prefix there cannot see the target
/// it picks inside); and a prefix on DeclareWarAction.ApplyByKingdomDecision skips a war decision that
/// passed anyway (one proposed before Always was chosen, or by another mod). Coop calls
/// GetRandomWarDecision from its own DailyTickClan, so the hooks reach it too.
/// </para>
/// <para>
/// Never touched: wars a player's own kingdom declares, ApplyByPlayerHostility (a player attacked first),
/// ApplyByRebellion, crime, kingdom creation, claims on the throne and calls to war. There is no forced
/// peace afterwards: a war that appears and is then undone looks broken to players. Anything that does not
/// resolve lets the war go ahead, with one line in the server log.
/// </para>
/// <para>
/// Ryvius's PlayerKingdomProtection in ModderLords.CompatSync.Coop gave the proposal hooks. His peace
/// backstop is not used.
/// </para>
/// </remarks>
internal static class KingdomProtection
{
    private static bool _patched;
    private static volatile HashSet<Kingdom> _playerKingdoms = new HashSet<Kingdom>();
    private static float _sinceRefresh = float.MaxValue;
    private static float _sinceReport;
    private static int _proposalsStopped;
    private static bool _readFailed;
    private static bool _checkFailed;

    /// <summary>Patched once, when the module loads on the server, with the server rules' own Harmony.</summary>
    public static void Patch(Harmony harmony)
    {
        if (_patched) return;
        _patched = true;
        var resolved = new List<string>();
        var missing = new List<string>();
        try
        {
            var behavior = AccessTools.TypeByName("TaleWorlds.CampaignSystem.CampaignBehaviors.KingdomDecisionProposalBehavior");

            var considerWar = behavior is null ? null : AccessTools.Method(behavior, "ConsiderWar", new[] { typeof(Clan), typeof(Kingdom), typeof(IFaction) });
            if (considerWar is null || considerWar.ReturnType != typeof(bool))
                missing.Add("ConsiderWar(Clan, Kingdom, IFaction)");
            else
            {
                harmony.Patch(considerWar, prefix: new HarmonyMethod(typeof(KingdomProtection), nameof(ConsiderWarPrefix)) { priority = Priority.First });
                resolved.Add("ConsiderWar");
            }

            var randomWar = behavior is null ? null : AccessTools.Method(behavior, "GetRandomWarDecision", new[] { typeof(Clan) });
            if (randomWar is null || !typeof(KingdomDecision).IsAssignableFrom(randomWar.ReturnType))
                missing.Add("GetRandomWarDecision(Clan)");
            else
            {
                harmony.Patch(randomWar, postfix: new HarmonyMethod(typeof(KingdomProtection), nameof(RandomWarPostfix)) { priority = Priority.Last });
                resolved.Add("GetRandomWarDecision");
            }

            var apply = AccessTools.Method(typeof(DeclareWarAction), nameof(DeclareWarAction.ApplyByKingdomDecision), new[] { typeof(IFaction), typeof(IFaction) });
            if (apply is null || !apply.IsStatic)
                missing.Add("DeclareWarAction.ApplyByKingdomDecision(IFaction, IFaction)");
            else
            {
                harmony.Patch(apply, prefix: new HarmonyMethod(typeof(KingdomProtection), nameof(ApplyByDecisionPrefix)) { priority = Priority.First });
                resolved.Add("ApplyByKingdomDecision");
            }
        }
        catch (Exception e)
        {
            missing.Add(e.GetType().Name + ": " + e.Message);
        }

        CoopBridge.Log(missing.Count == 0
            ? "Player kingdom protection: the hooks resolved (" + string.Join(", ", resolved) + ")."
            : "Player kingdom protection: some hooks did not resolve (" + string.Join("; ", missing) + "), so Always lets those wars go ahead." +
              (resolved.Count > 0 ? " Resolved: " + string.Join(", ", resolved) + "." : ""));
    }

    /// <summary>From the server rules' tick: rebuilds the player kingdoms every two seconds while Always is chosen.</summary>
    public static void Tick(float dt, Func<List<(Hero? Hero, bool Online)>> registered)
    {
        _sinceRefresh += dt;
        _sinceReport += dt;
        if (_sinceRefresh < 2f) return;
        _sinceRefresh = 0f;
        try
        {
            if (!_patched || Campaign.Current is null || ServerRules.Settings.KingdomProtection != KingdomProtectionMode.Always)
            {
                if (_playerKingdoms.Count > 0) _playerKingdoms = new HashSet<Kingdom>();
                return;
            }

            // Online and offline players alike: Always protects both.
            var players = registered();
            _playerKingdoms = PlayerKingdoms.Build(players.Select(p =>
            {
                var clan = p.Hero?.Clan;
                return (Kingdom: clan?.Kingdom, Mercenary: clan?.IsUnderMercenaryService == true);
            }));
            _readFailed = false;

            if (_proposalsStopped > 0 && _sinceReport >= 60f)
            {
                CoopBridge.Log("Player kingdom protection: stopped " + _proposalsStopped + " AI war proposal(s) against " +
                               string.Join(", ", _playerKingdoms.Select(k => k.Name?.ToString())) + ".");
                _proposalsStopped = 0;
                _sinceReport = 0f;
            }
        }
        catch (Exception e)
        {
            // Fail safe: with no list, nothing is protected and every war goes ahead.
            _playerKingdoms = new HashSet<Kingdom>();
            if (!_readFailed)
                CoopBridge.Log("Player kingdom protection could not read the players, so AI wars go ahead until it can: " + e.GetType().Name + ": " + e.Message);
            _readFailed = true;
            _sinceRefresh = -60f;
        }
    }

    /// <summary>True when an AI kingdom going to war with <paramref name="target"/> must be stopped.</summary>
    private static bool Blocks(IFaction? declarer, IFaction? target, WarCause cause)
    {
        try
        {
            var mode = ServerRules.Settings.KingdomProtection;
            if (mode != KingdomProtectionMode.Always) return false;
            var kingdoms = _playerKingdoms;
            if (kingdoms.Count == 0) return false;
            return KingdomProtectionRule.Blocks(mode, cause,
                declarer is Kingdom from && kingdoms.Contains(from),
                target is Kingdom to && kingdoms.Contains(to));
        }
        catch (Exception e)
        {
            if (!_checkFailed)
                CoopBridge.Log("Player kingdom protection: a check failed, so the war goes ahead: " + e.GetType().Name + ": " + e.Message);
            _checkFailed = true;
            return false;
        }
    }

    // ConsiderWar(Clan clan, Kingdom kingdom, IFaction otherFaction): "no" for a protected target.
    private static bool ConsiderWarPrefix(object[] __args, ref bool __result)
    {
        var kingdom = __args.Length > 1 ? __args[1] as Kingdom : null;
        var target = __args.Length > 2 ? __args[2] as IFaction : null;
        if (!Blocks(kingdom, target, WarCause.AiProposal)) return true;
        _proposalsStopped++;
        __result = false;
        return false;
    }

    // GetRandomWarDecision(Clan clan): a war decision against a protected target is dropped.
    private static void RandomWarPostfix(ref KingdomDecision? __result)
    {
        try
        {
            if (__result is DeclareWarDecision war && Blocks(war.Kingdom, war.FactionToDeclareWarOn, WarCause.AiProposal))
            {
                _proposalsStopped++;
                __result = null;
            }
        }
        catch (Exception) { }
    }

    // DeclareWarAction.ApplyByKingdomDecision(IFaction faction1, IFaction faction2): a passed AI war decision
    // against a protected kingdom does not declare the war.
    private static bool ApplyByDecisionPrefix(object[] __args)
    {
        var declarer = __args.Length > 0 ? __args[0] as IFaction : null;
        var target = __args.Length > 1 ? __args[1] as IFaction : null;
        if (!Blocks(declarer, target, WarCause.KingdomDecision)) return true;
        try { CoopBridge.Log("Player kingdom protection: kept " + declarer?.Name + " from declaring war on " + target?.Name + ", a player kingdom."); }
        catch (Exception) { }
        return false;
    }
}
