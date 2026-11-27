// Coodsz Server Tools: army roster, give and take troops, heal wounded, troop XP and the troop
// type list. The roster dump is ported from CoopKeep (https://github.com/Aderimo/bannerlord-coopkeep),
// src/CoopKeep.ServerTools/HeroAndWorldCommands.cs.

using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.Core;
using TaleWorlds.ObjectSystem;
using static CoodszServerTools.Text;

namespace CoodszServerTools;

internal static class TroopCommands
{
    public const int MaxTroops = 1000;

    private static bool TryParty(List<string> args, string usage, out Hero? hero, out MobileParty? party, out string error)
    {
        party = null;
        if (!TryHero(args, usage, out hero, out error)) return false;
        party = hero!.PartyBelongedTo;
        if (party is null) { error = "COODSZ_ERROR hero '" + hero.StringId + "' has no party."; return false; }
        return true;
    }

    /// <summary>
    /// <c>coodsz.roster &lt;id&gt;</c>: <c>PARTY|party id|troops|wounded</c> (heroes left out) and
    /// <c>TROOP|members|troop id|name|count|wounded|xp|hero|tier|culture</c> (<c>prisoners</c> for prisoners).
    /// </summary>
    public static string Roster(List<string> args)
    {
        if (!TryParty(args, "coodsz.roster <hero_id>", out _, out var party, out var error)) return error;
        var members = party!.MemberRoster;
        var lines = new List<string>
        {
            Line("PARTY", party.StringId, I(members.TotalManCount - members.TotalHeroes), I(members.TotalWounded - members.TotalWoundedHeroes)),
        };

        void Dump(string kind, TroopRoster roster)
        {
            foreach (var e in roster.GetTroopRoster())
            {
                if (e.Character is null) continue;
                lines.Add(Line("TROOP", kind, e.Character.StringId, Clean(e.Character.Name?.ToString()), I(e.Number), I(e.WoundedNumber),
                    I(e.Xp), B(e.Character.IsHero), I(e.Character.Tier), e.Character.Culture?.StringId ?? ""));
            }
        }

        Dump("members", members);
        Dump("prisoners", party.PrisonRoster);
        return Lines(lines);
    }

    /// <summary>
    /// <c>coodsz.troops &lt;id&gt; &lt;troop id&gt; &lt;n&gt;</c>: adds troops of one kind, or takes them
    /// with a negative count (at most what the party has; wounded ones go first).
    /// </summary>
    public static string Troops(List<string> args)
    {
        if (!TryParty(args, "coodsz.troops <hero_id> <troop_id> <count>", out var hero, out var party, out var error)) return error;
        var troop = args.Count > 1 ? MBObjectManager.Instance.GetObject<CharacterObject>(args[1]) : null;
        if (troop is null) return "COODSZ_ERROR troop '" + Clean(args.Count > 1 ? args[1] : "") + "' not found.";
        if (troop.IsHero) return "COODSZ_ERROR heroes cannot be given or taken as troops.";
        if (!TryInt(args, 2, -MaxTroops, MaxTroops, out var n) || n == 0)
            return "COODSZ_ERROR count must be between -" + I(MaxTroops) + " and " + I(MaxTroops) + ", not 0";

        var roster = party!.MemberRoster;
        var old = roster.GetTroopRoster().Where(e => e.Character == troop).Sum(e => e.Number);
        if (n > 0) roster.AddToCounts(troop, n);
        else
        {
            var take = Math.Min(-n, old);
            var wounded = roster.GetTroopRoster().Where(e => e.Character == troop).Sum(e => e.WoundedNumber);
            if (take > 0) roster.AddToCounts(troop, -take, false, -Math.Min(take, wounded));
        }

        var now = roster.GetTroopRoster().Where(e => e.Character == troop).Sum(e => e.Number);
        return Set(hero!, "troop:" + troop.StringId, old, now);
    }

    /// <summary><c>coodsz.heal_troops &lt;id&gt;</c>: heals every wounded troop in the party (heroes left out).</summary>
    public static string HealTroops(List<string> args)
    {
        if (!TryParty(args, "coodsz.heal_troops <hero_id>", out var hero, out var party, out var error)) return error;
        var roster = party!.MemberRoster;
        var old = roster.TotalWounded - roster.TotalWoundedHeroes;
        foreach (var e in roster.GetTroopRoster().ToList())
        {
            if (e.Character is null || e.Character.IsHero || e.WoundedNumber <= 0) continue;
            roster.AddToCounts(e.Character, 0, false, -e.WoundedNumber);
        }

        return Set(hero!, "wounded", old, roster.TotalWounded - roster.TotalWoundedHeroes);
    }

    /// <summary><c>coodsz.troop_xp &lt;id&gt; &lt;xp&gt;</c>: gives every troop kind in the party this much XP, so they can be upgraded.</summary>
    public static string TroopXp(List<string> args)
    {
        if (!TryParty(args, "coodsz.troop_xp <hero_id> <xp>", out var hero, out var party, out var error)) return error;
        if (!TryInt(args, 1, 1, 1_000_000, out var xp)) return "COODSZ_ERROR xp must be between 1 and 1000000";

        var roster = party!.MemberRoster;
        int Total() => roster.GetTroopRoster().Where(e => e.Character != null && !e.Character.IsHero).Sum(e => e.Xp);
        var old = Total();
        foreach (var e in roster.GetTroopRoster().ToList())
        {
            if (e.Character is null || e.Character.IsHero) continue;
            roster.AddXpToTroop(e.Character, xp);
        }

        return Set(hero!, "troop_xp", old, Total());
    }

    /// <summary>
    /// <c>coodsz.troop_types</c>: every soldier, mercenary and bandit troop kind, written to a file:
    /// <c>TT|id|name|culture id|culture|tier|class|mounted|ranged</c>.
    /// </summary>
    public static string TroopTypes(List<string> args)
    {
        var lines = new List<string>();
        foreach (var c in MBObjectManager.Instance.GetObjectTypeList<CharacterObject>())
        {
            if (c is null || c.IsHero || c.IsTemplate || string.IsNullOrEmpty(c.StringId)) continue;
            if (c.Occupation != Occupation.Soldier && c.Occupation != Occupation.Mercenary && c.Occupation != Occupation.Bandit
                && c.Occupation != Occupation.CaravanGuard && c.Occupation != Occupation.Gangster) continue;
            lines.Add(Line("TT", c.StringId, Clean(c.Name?.ToString()), c.Culture?.StringId ?? "", Clean(c.Culture?.Name?.ToString()),
                I(c.Tier), c.DefaultFormationClass.ToString(), B(c.IsMounted), B(c.IsRanged)));
        }

        return ToFile("troop_types", lines);
    }
}
