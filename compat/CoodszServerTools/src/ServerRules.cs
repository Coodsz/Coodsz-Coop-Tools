// Coodsz Server Tools: the host's server rules. Who may use coop.unstuck, the skill XP freeze
// for players who are offline, player kingdom protection (KingdomProtection.cs) and the PvP rules (PvpRules.cs). The rules are in
// ServerRulesRules.cs; this file reads the settings file and patches Coop and the game on the dedicated server.
// Parts adapted from ModderLords by Ryvius (@PlueRyvius), used with permission.

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Party;
using Coodsz.ServerRules;

namespace CoodszServerTools;

/// <summary>
/// The rules Coodsz Host's Settings page (Advanced: server rules) writes to coodsz-server-tools.json.
/// </summary>
/// <remarks>
/// <para>
/// coop.unstuck: a prefix on Coop's PlayerUnstuckHandler.ApplyServerUnstuck, which only a player's
/// own request (NetworkRequestPlayerUnstuck) reaches. The host's console command
/// (<c>coop.unstuck &lt;controllerId&gt;</c>) calls ServerPlayerUnstuck straight away, so it is never
/// checked. A refused player gets one System line in Coop's chat saying why. The cooldown starts in a
/// postfix, so only an unstuck that Coop really ran counts.
/// </para>
/// <para>
/// Offline XP freeze: a prefix on HeroDeveloper.AddSkillXp skips XP for the hero of a player who is
/// not connected. The offline set is rebuilt from Coop's player manager every two seconds. Gold is
/// not frozen: skipping gold changes would make gold from nothing when GiveGoldAction pays an offline
/// player's hero (the payer still loses it), and Coop already freezes a disconnected player's economy.
/// </para>
/// </remarks>
internal static class ServerRules
{
    private static readonly object Gate = new object();
    private static ServerRulesSettings _settings = ServerRulesSettings.Defaults;
    private static PvpRulesSettings _pvp = PvpRulesSettings.Off;
    private static DateTime _fileTime = DateTime.MinValue;
    private static int _checkedAt = int.MinValue;
    private static bool _readOnce;
    private static readonly UnstuckCooldowns Cooldowns = new UnstuckCooldowns();
    private static Harmony? _harmony;
    private static bool _gamePatched;
    private static bool _coopPatched;

    // The offline XP freeze.
    private static volatile HashSet<Hero> _offline = new HashSet<Hero>();
    private static float _sinceRefresh = float.MaxValue;
    private static int _skippedXp;
    [ThreadStatic] private static int _allow;

    public static string SettingsPath => ClanCap.SettingsPath;

    /// <summary>The settings file, read again within a few seconds of a change.</summary>
    public static ServerRulesSettings Settings
    {
        get
        {
            lock (Gate)
            {
                var now = Environment.TickCount;
                if (_checkedAt != int.MinValue && unchecked(now - _checkedAt) < 5000) return _settings;
                _checkedAt = now;
                var wasRead = _readOnce;
                _readOnce = true;
                try
                {
                    var path = SettingsPath;
                    var time = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
                    if (time == _fileTime) return _settings;
                    _fileTime = time;
                    var text = time == DateTime.MinValue ? null : File.ReadAllText(path);
                    var read = text is null ? ServerRulesSettings.Defaults : ServerRulesSettings.Parse(text);
                    var pvp = PvpRulesSettings.Parse(text);
                    if (wasRead && read.Describe() != _settings.Describe())
                        CoopBridge.Log("Server rules: " + read.Describe() + ".");
                    if (wasRead && pvp.Describe() != _pvp.Describe())
                        CoopBridge.Log("PvP rules: " + pvp.Describe() + ".");
                    _settings = read;
                    _pvp = pvp;
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

    /// <summary>The PvP rules from the same file, read with <see cref="Settings"/>.</summary>
    public static PvpRulesSettings Pvp
    {
        get
        {
            _ = Settings;
            lock (Gate) return _pvp;
        }
    }

    // ---- patches ---------------------------------------------------------------------------

    /// <summary>The game's skill XP: patched once, when the module loads on the server.</summary>
    public static void PatchGame()
    {
        if (_gamePatched) return;
        _gamePatched = true;
        try
        {
            _harmony ??= new Harmony(ToolsSubModule.HarmonyId);
            _harmony.Patch(AccessTools.Method(typeof(HeroDeveloper), nameof(HeroDeveloper.AddSkillXp)),
                prefix: new HarmonyMethod(typeof(ServerRules), nameof(XpPrefix)) { priority = Priority.First });
        }
        catch (Exception e)
        {
            CoopBridge.Log("Offline XP freeze could not patch the game: " + e.GetType().Name + ": " + e.Message);
        }

        try
        {
            if (_harmony != null) KingdomProtection.Patch(_harmony);
        }
        catch (Exception e)
        {
            CoopBridge.Log("Player kingdom protection could not patch the game, so AI wars go ahead: " + e.GetType().Name + ": " + e.Message);
        }
    }

    /// <summary>Coop's unstuck handler: patched once every module is loaded.</summary>
    public static void PatchCoop()
    {
        if (_coopPatched || _harmony is null) return;
        const string handler = "GameInterface.Services.MobileParties.Handlers.PlayerUnstuckHandler";
        if (AccessTools.TypeByName(handler) is null) return;
        _coopPatched = true;
        try
        {
            // Coop (development and the Workshop GameInterface.dll): an instance method,
            // private void ApplyServerUnstuck(Player player), on a handler with the fields objectManager,
            // playerManager and network. Anything else and the rule is not applied, said once here.
            var target = AccessTools.Method(handler + ":ApplyServerUnstuck");
            var why = UnstuckHookProblem(target);
            if (why != null)
            {
                CoopBridge.Log("Server rules: the coop.unstuck hook did not resolve (" + why + "), so players' coop.unstuck is not limited.");
            }
            else
            {
                _harmony.Patch(target,
                    prefix: new HarmonyMethod(typeof(ServerRules), nameof(UnstuckPrefix)),
                    postfix: new HarmonyMethod(typeof(ServerRules), nameof(UnstuckPostfix)));
                CoopBridge.Log("Server rules: the coop.unstuck hook resolved (PlayerUnstuckHandler.ApplyServerUnstuck(Player)).");
            }

            CoopBridge.Log("Server rules: " + Settings.Describe() + ".");
        }
        catch (Exception e)
        {
            CoopBridge.Log("Server rules could not patch Coop: " + e.GetType().Name + ": " + e.Message);
        }

        try
        {
            PvpRules.Patch(_harmony);
        }
        catch (Exception e)
        {
            CoopBridge.Log("PvP rules could not patch Coop, so they are off: " + e.GetType().Name + ": " + e.Message);
        }
    }

    // ---- coop.unstuck --------------------------------------------------------------------------

    /// <summary>Why ApplyServerUnstuck is not the method the prefix is written for, or null when it is.</summary>
    private static string? UnstuckHookProblem(MethodInfo? target)
    {
        if (target is null) return "ApplyServerUnstuck was not found";
        if (target.IsStatic) return "ApplyServerUnstuck is static";
        var parameters = target.GetParameters();
        if (parameters.Length != 1 || parameters[0].ParameterType.Name != "Player")
            return "ApplyServerUnstuck takes (" + string.Join(", ", parameters.Select(p => p.ParameterType.Name)) + "), not (Player)";
        var playerType = parameters[0].ParameterType;
        foreach (var name in new[] { "ControllerId", "HeroId", "MobilePartyId" })
            if (AccessTools.Field(playerType, name) is null && AccessTools.Property(playerType, name) is null)
                return "Player has no " + name;
        foreach (var name in new[] { "objectManager", "playerManager", "network" })
            if (AccessTools.Field(target.DeclaringType, name) is null)
                return "PlayerUnstuckHandler has no field " + name;
        return null;
    }

    // Coop: a player asked to be unstuck (the client's coop.unstuck). Skipped when the rule says no.
    // __state is the controller whose cooldown starts once Coop's handler really ran (UnstuckPostfix).
    private static bool UnstuckPrefix(object __instance, object[] __args, out string? __state)
    {
        __state = null;
        try
        {
            var mode = Settings.Unstuck;
            if (mode == UnstuckMode.Allowed) return true;
            var player = __args.Length > 0 ? __args[0] : null;
            if (player is null) return true;
            var controller = CoopBridge.Field(player, "ControllerId") as string ?? "";
            var hero = CoopBridge.CoopObject<Hero>(__instance, CoopBridge.Field(player, "HeroId") as string ?? "");
            var party = CoopBridge.CoopObject<MobileParty>(__instance, CoopBridge.Field(player, "MobilePartyId") as string ?? "") ?? hero?.PartyBelongedTo;

            var now = DateTime.UtcNow;
            var last = Cooldowns.LastUsed(controller);
            var block = UnstuckRule.Decide(mode, mode == UnstuckMode.NotInFights ? Facts(hero, party) : default, last, now);
            if (block == UnstuckBlock.None)
            {
                // The cooldown starts in the postfix, which Harmony runs only when Coop's handler did not throw.
                __state = controller;
                return true;
            }

            var text = UnstuckRule.Message(block, last is DateTime used ? UnstuckRule.MinutesLeft(used, now) : 0);
            CoopBridge.Log("Refused coop.unstuck for " + controller + " (" + hero?.Name + "): " + text);
            CoopBridge.TellController(__instance, controller, text);
            return false;
        }
        catch (Exception e)
        {
            // A broken check must never keep a really stuck player stuck.
            CoopBridge.Log("Server rules: the unstuck check failed, so it was allowed: " + e.GetType().Name + ": " + e.Message);
            return true;
        }
    }

    // Coop's ApplyServerUnstuck finished without an exception: this player's cooldown starts now.
    private static void UnstuckPostfix(string? __state)
    {
        try
        {
            if (__state != null) Cooldowns.Used(__state, DateTime.UtcNow);
        }
        catch (Exception) { }
    }

    private static UnstuckFacts Facts(Hero? hero, MobileParty? party)
    {
        var army = party?.Army;
        var leader = army?.LeaderParty;
        return new UnstuckFacts(
            prisoner: hero?.IsPrisoner == true,
            inFight: party?.MapEvent != null,
            inSiege: party != null && (party.SiegeEvent != null || party.BesiegedSettlement != null || party.CurrentSettlement?.IsUnderSiege == true),
            armyInFight: leader != null && (leader.MapEvent != null || leader.SiegeEvent != null || leader.BesiegedSettlement != null),
            enemiesNear: party != null && EnemiesNear(party));
    }

    /// <summary>An active party at war with the player's, outside a settlement, within <see cref="UnstuckRule.EnemyRadius"/>.</summary>
    private static bool EnemiesNear(MobileParty party)
    {
        var faction = party.MapFaction;
        if (faction is null || party.CurrentSettlement != null) return false;
        var at = party.GetPosition2D;
        foreach (var other in MobileParty.All.ToList())
        {
            if (other is null || other == party || !other.IsActive || other.CurrentSettlement != null) continue;
            if (other.MemberRoster is null || other.MemberRoster.TotalHealthyCount <= 0) continue;
            var otherFaction = other.MapFaction;
            if (otherFaction is null || !FactionManager.IsAtWarAgainstFaction(faction, otherFaction)) continue;
            if (other.GetPosition2D.Distance(at) <= UnstuckRule.EnemyRadius) return true;
        }

        return false;
    }

    // ---- offline XP freeze ---------------------------------------------------------------------

    /// <summary>Runs a host edit that must reach an offline player's hero (coodsz.skill_xp).</summary>
    public static T Allow<T>(Func<T> action)
    {
        _allow++;
        try { return action(); }
        finally { _allow--; }
    }

    /// <summary>From the module's application tick: rebuilds who is offline every two seconds.</summary>
    public static void Tick(float dt)
    {
        KingdomProtection.Tick(dt, Registered);
        PvpRules.Tick(dt);
        _sinceRefresh += dt;
        if (_sinceRefresh < 2f) return;
        _sinceRefresh = 0f;
        try
        {
            if (!_gamePatched || Campaign.Current is null || !Settings.FreezeOfflineXp)
            {
                if (_offline.Count > 0) _offline = new HashSet<Hero>();
                return;
            }

            // A read that throws keeps the last good set: an empty set would let every offline hero gain XP.
            var offline = OfflineHeroes.Refresh(_offline, Registered, out var error);
            if (error != null)
            {
                CoopBridge.Log("Offline XP freeze could not read the players, so the last list is kept: " + error.GetType().Name + ": " + error.Message);
                _sinceRefresh = -60f; // try again in about a minute, not every tick
                return;
            }

            if (_skippedXp > 0)
            {
                CoopBridge.Log("Offline XP freeze: skipped " + _skippedXp + " skill XP gain(s) for " + offline.Count + " offline player(s).");
                _skippedXp = 0;
            }

            _offline = offline;
        }
        catch (Exception e)
        {
            CoopBridge.Log("Offline XP freeze could not read the players, so the last list is kept: " + e.GetType().Name + ": " + e.Message);
            _sinceRefresh = -60f;
        }
    }

    /// <summary>Every registered player's hero with whether it is connected, from Coop's IPlayerManager.</summary>
    private static List<(Hero? Hero, bool Online)> Registered()
    {
        var result = new List<(Hero? Hero, bool Online)>();
        var players = CoopBridge.Resolve("GameInterface.Services.Players.IPlayerManager");
        var objects = CoopBridge.Resolve("GameInterface.Services.ObjectManager.IObjectManager");
        if (players is null || objects is null) return result;
        var isConnected = AccessTools.TypeByName("GameInterface.Services.Players.IPlayerManager")?.GetMethod("IsConnected");
        if (!(CoopBridge.Field(players, "Players") is IEnumerable all) || isConnected is null) return result;
        // Coop's list can change on its network thread: copy it before going through it.
        foreach (var player in OfflineHeroes.Snapshot(all))
        {
            if (player is null) continue;
            var hero = CoopBridge.FromManager<Hero>(objects, CoopBridge.Field(player, "HeroId") as string);
            result.Add((hero, isConnected.Invoke(players, new[] { player }) is true));
        }

        return result;
    }

    private static bool XpPrefix(HeroDeveloper __instance)
    {
        try
        {
            if (_allow > 0) return true;
            var offline = _offline;
            if (offline.Count == 0) return true;
            var hero = __instance?.Hero;
            if (hero is null || !offline.Contains(hero)) return true;
            _skippedXp++;
            return false;
        }
        catch (Exception) { return true; }
    }
}
