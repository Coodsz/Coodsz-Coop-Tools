// Coodsz Server Tools: the server rules (coop.unstuck for players, offline XP freeze, player kingdom
// protection) without the game, so the launcher tests can run them (tests\BannerlordCoopLauncher.Tests links this file).
// Parts adapted from ModderLords by Ryvius (@PlueRyvius), used with permission.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Coodsz.ServerRules;

/// <summary>Who may use coop.unstuck on this server.</summary>
public enum UnstuckMode
{
    /// <summary>Coop's own behaviour: any player may unstick themselves at any time.</summary>
    Allowed,
    /// <summary>Players may, but not while a prisoner, in a fight or siege, with enemies near, or again within the cooldown.</summary>
    NotInFights,
    /// <summary>Only the host's console (<c>coop.unstuck &lt;controllerId&gt;</c>) may unstick a player.</summary>
    HostOnly,
}

/// <summary>How AI kingdoms are kept from going to war with the kingdoms players are in.</summary>
public enum KingdomProtectionMode
{
    /// <summary>
    /// Coop's own modOptions.blockAiWarDeclarationsOnOfflinePlayers, on. Also what a missing or unknown key
    /// reads as, where Coodsz Host leaves Coop's option as the host already has it. The module adds nothing.
    /// </summary>
    OfflineOnly,
    /// <summary>The module stops AI wars on every kingdom a player rules or serves as a vassal, online or offline.</summary>
    Always,
    /// <summary>No protection: Coodsz Host turns Coop's option off and the module adds nothing.</summary>
    Off,
}

/// <summary>
/// The server rules Coodsz Host writes to <c>coodsz-server-tools.json</c>, next to the clan cap:
/// <c>{ "unstuck": "allowed", "pauseOnJoin": true, "freezeOfflineXp": true, "kingdomProtection": "offlineOnly" }</c>.
/// A missing key or file keeps the default: unstuck allowed (Coop's own behaviour), pause on join and the
/// XP freeze on, and kingdom protection left to Coop's own option.
/// </summary>
public sealed class ServerRulesSettings
{
    public const string UnstuckKey = "unstuck";
    public const string PauseOnJoinKey = "pauseOnJoin";
    public const string FreezeXpKey = "freezeOfflineXp";
    public const string KingdomProtectionKey = "kingdomProtection";

    public const string AllowedValue = "allowed";
    public const string NotInFightsValue = "notInFights";
    public const string HostOnlyValue = "hostOnly";

    public const string OfflineOnlyValue = "offlineOnly";
    public const string AlwaysValue = "always";
    public const string OffValue = "off";

    public static readonly ServerRulesSettings Defaults = new ServerRulesSettings(UnstuckMode.Allowed, true, true);

    public ServerRulesSettings(UnstuckMode unstuck, bool pauseOnJoin, bool freezeOfflineXp,
        KingdomProtectionMode kingdomProtection = KingdomProtectionMode.OfflineOnly)
    {
        Unstuck = unstuck;
        PauseOnJoin = pauseOnJoin;
        FreezeOfflineXp = freezeOfflineXp;
        KingdomProtection = kingdomProtection;
    }

    public UnstuckMode Unstuck { get; }

    /// <summary>Read by Coodsz Host, not the module: the host pauses the campaign while a player joins.</summary>
    public bool PauseOnJoin { get; }

    public bool FreezeOfflineXp { get; }

    /// <summary>Only <see cref="KingdomProtectionMode.Always"/> makes the module do anything.</summary>
    public KingdomProtectionMode KingdomProtection { get; }

    public static string ValueOf(UnstuckMode mode) => mode switch
    {
        UnstuckMode.NotInFights => NotInFightsValue,
        UnstuckMode.HostOnly => HostOnlyValue,
        _ => AllowedValue,
    };

    public static UnstuckMode ModeOf(string? value)
    {
        if (string.Equals(value, NotInFightsValue, StringComparison.OrdinalIgnoreCase)) return UnstuckMode.NotInFights;
        if (string.Equals(value, HostOnlyValue, StringComparison.OrdinalIgnoreCase)) return UnstuckMode.HostOnly;
        return UnstuckMode.Allowed;
    }

    public static string ValueOf(KingdomProtectionMode mode) => mode switch
    {
        KingdomProtectionMode.Always => AlwaysValue,
        KingdomProtectionMode.Off => OffValue,
        _ => OfflineOnlyValue,
    };

    public static KingdomProtectionMode KingdomModeOf(string? value)
    {
        if (string.Equals(value, AlwaysValue, StringComparison.OrdinalIgnoreCase)) return KingdomProtectionMode.Always;
        if (string.Equals(value, OffValue, StringComparison.OrdinalIgnoreCase)) return KingdomProtectionMode.Off;
        return KingdomProtectionMode.OfflineOnly;
    }

    /// <summary>Reads the keys; anything missing or broken keeps its default.</summary>
    public static ServerRulesSettings Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Defaults;
        var unstuck = Regex.Match(json, "\"" + UnstuckKey + "\"\\s*:\\s*\"([A-Za-z]+)\"", RegexOptions.IgnoreCase);
        var kingdoms = Regex.Match(json, "\"" + KingdomProtectionKey + "\"\\s*:\\s*\"([A-Za-z]+)\"", RegexOptions.IgnoreCase);
        return new ServerRulesSettings(
            unstuck.Success ? ModeOf(unstuck.Groups[1].Value) : UnstuckMode.Allowed,
            Flag(json, PauseOnJoinKey, true),
            Flag(json, FreezeXpKey, true),
            kingdoms.Success ? KingdomModeOf(kingdoms.Groups[1].Value) : KingdomProtectionMode.OfflineOnly);
    }

    private static bool Flag(string json, string key, bool fallback)
    {
        var match = Regex.Match(json, "\"" + key + "\"\\s*:\\s*\"?(true|false)", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value.Equals("true", StringComparison.OrdinalIgnoreCase) : fallback;
    }

    public string Describe() =>
        "unstuck " + ValueOf(Unstuck) + ", offline XP freeze " + (FreezeOfflineXp ? "on" : "off") +
        ", player kingdom protection " + KingdomProtection switch
        {
            KingdomProtectionMode.Always => "always",
            KingdomProtectionMode.Off => "off",
            _ => "left to Coop's own option",
        };
}

/// <summary>Why a player's unstuck request is refused.</summary>
public enum UnstuckBlock
{
    None,
    HostOnly,
    Prisoner,
    InFight,
    InSiege,
    ArmyInFight,
    EnemiesNear,
    Cooldown,
}

/// <summary>What the server sees about the player's party when the request comes in.</summary>
public readonly struct UnstuckFacts
{
    public UnstuckFacts(bool prisoner, bool inFight, bool inSiege, bool armyInFight, bool enemiesNear)
    {
        Prisoner = prisoner;
        InFight = inFight;
        InSiege = inSiege;
        ArmyInFight = armyInFight;
        EnemiesNear = enemiesNear;
    }

    public bool Prisoner { get; }
    public bool InFight { get; }
    public bool InSiege { get; }
    public bool ArmyInFight { get; }
    public bool EnemiesNear { get; }
}

/// <summary>The coop.unstuck rule for a player's own request. The host's console is never checked.</summary>
public static class UnstuckRule
{
    /// <summary>
    /// Real time, not campaign time: campaign time stops while the game is paused (and while a player
    /// joins) and runs twice as fast at 2x, so a game time cooldown could last forever or almost nothing.
    /// Five real minutes is about how long one fight takes, so unstuck cannot be used to hop out of the
    /// next fight right after the last one.
    /// </summary>
    public static readonly TimeSpan Cooldown = TimeSpan.FromMinutes(5);

    /// <summary>
    /// How near a party at war with the player blocks unstuck, in map units. About two hours of
    /// travel for a normal party, so an enemy that could still catch the player counts, and one far
    /// across the map does not. Parties inside a settlement do not count.
    /// </summary>
    public const float EnemyRadius = 10f;

    public static UnstuckBlock Decide(UnstuckMode mode, UnstuckFacts facts, DateTime? lastUsed, DateTime now)
    {
        if (mode == UnstuckMode.Allowed) return UnstuckBlock.None;
        if (mode == UnstuckMode.HostOnly) return UnstuckBlock.HostOnly;
        if (facts.Prisoner) return UnstuckBlock.Prisoner;
        if (facts.InFight) return UnstuckBlock.InFight;
        if (facts.InSiege) return UnstuckBlock.InSiege;
        if (facts.ArmyInFight) return UnstuckBlock.ArmyInFight;
        if (facts.EnemiesNear) return UnstuckBlock.EnemiesNear;
        if (lastUsed is DateTime used && now - used < Cooldown && now >= used) return UnstuckBlock.Cooldown;
        return UnstuckBlock.None;
    }

    /// <summary>How long until the cooldown ends, rounded up to whole minutes (at least 1).</summary>
    public static int MinutesLeft(DateTime lastUsed, DateTime now)
    {
        var left = Cooldown - (now - lastUsed);
        return Math.Max(1, (int)Math.Ceiling(left.TotalMinutes));
    }

    /// <summary>The short line the player sees in Coop's chat.</summary>
    public static string Message(UnstuckBlock block, int minutesLeft = 0) => block switch
    {
        UnstuckBlock.HostOnly => "Unstuck is only for the host on this server. Ask the host if you are stuck.",
        UnstuckBlock.Prisoner => "You can't use unstuck while you are a prisoner.",
        UnstuckBlock.InFight => "You can't use unstuck while in a fight.",
        UnstuckBlock.InSiege => "You can't use unstuck during a siege.",
        UnstuckBlock.ArmyInFight => "You can't use unstuck while your army is in a fight.",
        UnstuckBlock.EnemiesNear => "You can't use unstuck with enemies nearby.",
        UnstuckBlock.Cooldown => "You can use unstuck again in " + minutesLeft.ToString(CultureInfo.InvariantCulture) +
                                 (minutesLeft == 1 ? " minute." : " minutes."),
        _ => "",
    };
}

/// <summary>When each player last used unstuck, by Coop controller id.</summary>
public sealed class UnstuckCooldowns
{
    private readonly Dictionary<string, DateTime> _used = new Dictionary<string, DateTime>(StringComparer.Ordinal);
    private readonly object _gate = new object();

    public DateTime? LastUsed(string controllerId)
    {
        lock (_gate) return _used.TryGetValue(controllerId ?? "", out var at) ? at : (DateTime?)null;
    }

    public void Used(string controllerId, DateTime now)
    {
        lock (_gate)
        {
            if (_used.Count > 1000) _used.Clear();
            _used[controllerId ?? ""] = now;
        }
    }
}

/// <summary>The heroes whose skill XP is frozen: every player hero with no connected registration.</summary>
public static class OfflineHeroes
{
    public static HashSet<T> Build<T>(IEnumerable<(T? Hero, bool Online)> players) where T : class
    {
        var online = new HashSet<T>();
        var offline = new HashSet<T>();
        foreach (var (hero, isOnline) in players)
        {
            if (hero is null) continue;
            if (isOnline) online.Add(hero);
            else offline.Add(hero);
        }

        // One hero registered twice (a reconnect) is online when any of its connections is.
        offline.ExceptWith(online);
        return offline;
    }

    /// <summary>
    /// The new offline set from <paramref name="read"/>, read in full before it is used. When reading
    /// throws (Coop changed its player list meanwhile), the last good set is kept, so the freeze does
    /// not stop for everyone until the next good read.
    /// </summary>
    public static HashSet<T> Refresh<T>(HashSet<T> last, Func<IEnumerable<(T? Hero, bool Online)>> read, out Exception? error) where T : class
    {
        try
        {
            var players = new List<(T? Hero, bool Online)>(read());
            error = null;
            return Build(players);
        }
        catch (Exception e)
        {
            error = e;
            return last;
        }
    }

    /// <summary>A copy of a collection another thread may change, taken before it is enumerated.</summary>
    public static List<object> Snapshot(System.Collections.IEnumerable items)
    {
        if (items is System.Collections.ICollection collection)
        {
            var copy = new object[collection.Count];
            collection.CopyTo(copy, 0);
            return new List<object>(copy);
        }

        var list = new List<object>();
        foreach (var item in items) list.Add(item);
        return list;
    }
}

/// <summary>How a war against a player kingdom came about, as far as the protection cares.</summary>
public enum WarCause
{
    /// <summary>An AI clan thinking about proposing a war (KingdomDecisionProposalBehavior.ConsiderWar, GetRandomWarDecision).</summary>
    AiProposal,
    /// <summary>A kingdom decision to declare war that passed (DeclareWarAction.ApplyByKingdomDecision).</summary>
    KingdomDecision,
    /// <summary>A player attacked first (DeclareWarAction.ApplyByPlayerHostility). Never blocked.</summary>
    PlayerHostility,
    /// <summary>A clan rebelled (DeclareWarAction.ApplyByRebellion). Never blocked.</summary>
    Rebellion,
    /// <summary>Anything else (crime, a new kingdom, a claim on the throne, a call to war). Never blocked.</summary>
    Other,
}

/// <summary>
/// The Always rule of player kingdom protection. An AI kingdom may not propose or declare war on a kingdom
/// a player is in. Wars a player's own kingdom declares, wars a player started by attacking, and
/// rebellions always go ahead. There is no forced peace afterwards: a war that starts some other way stays.
/// </summary>
public static class KingdomProtectionRule
{
    public static bool Blocks(KingdomProtectionMode mode, WarCause cause, bool declarerIsPlayers, bool targetIsPlayers)
    {
        if (mode != KingdomProtectionMode.Always) return false;
        if (cause != WarCause.AiProposal && cause != WarCause.KingdomDecision) return false;
        return targetIsPlayers && !declarerIsPlayers;
    }
}

/// <summary>The kingdoms players are in, built from every registered player, online or offline.</summary>
public static class PlayerKingdoms
{
    /// <summary>
    /// Each player's clan kingdom. A ruler and a vassal both count; a mercenary does not, since a mercenary
    /// has no say in the kingdom's wars (the same as Coop's offline protection). A player in no kingdom
    /// adds nothing.
    /// </summary>
    public static HashSet<T> Build<T>(IEnumerable<(T? Kingdom, bool Mercenary)> players) where T : class
    {
        var kingdoms = new HashSet<T>();
        foreach (var (kingdom, mercenary) in players)
            if (kingdom != null && !mercenary) kingdoms.Add(kingdom);
        return kingdoms;
    }
}
