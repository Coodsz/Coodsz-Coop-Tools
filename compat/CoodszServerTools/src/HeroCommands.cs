// Coodsz Server Tools: hero list, profile and the gold, influence, renown, health and age
// commands. The equipment dump is ported from CoopKeep (https://github.com/Aderimo/bannerlord-coopkeep),
// src/CoopKeep.ServerTools/HeroAndWorldCommands.cs.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using static CoodszServerTools.Text;

namespace CoodszServerTools;

internal static class HeroCommands
{
    public const int MaxGold = 10_000_000;
    public const int MaxInfluence = 10_000;
    public const int MaxRenown = 100_000;

    /// <summary>
    /// <c>coodsz.heroes [name]</c>: without a name the heroes that look like players (Coop's player
    /// heroes, the main hero), with a name every living hero whose name contains it (at most 100).
    /// <c>H|id|name|clan id|clan|kingdom|gold|hp|max hp|level|party id|place|player</c>.
    /// </summary>
    public static string Heroes(List<string> args)
    {
        if (Campaign.Current is null) return NoCampaign;
        var query = string.Join(" ", args).Trim().Trim('"');
        IEnumerable<Hero> heroes = TaleWorlds.CampaignSystem.Hero.AllAliveHeroes;
        heroes = query.Length == 0
            ? heroes.Where(IsPlayerLike)
            : heroes.Where(h => (h.Name?.ToString() ?? "").IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                .OrderBy(h => string.Equals(h.Name?.ToString(), query, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .Take(100);

        var lines = heroes.Select(h => Line("H", h.StringId, Clean(h.Name?.ToString()), h.Clan?.StringId ?? "", Clean(h.Clan?.Name?.ToString()),
            Clean(h.Clan?.Kingdom?.Name?.ToString()), I(h.Gold), I(h.HitPoints), I(h.MaxHitPoints), I(h.Level),
            h.PartyBelongedTo?.StringId ?? "", Clean(Place(h)?.Name?.ToString()), B(IsPlayerLike(h)))).ToList();
        return Lines(lines);
    }

    /// <summary>
    /// Coop names its player heroes and clans after the player (<c>Player2863</c>); the host's
    /// list also takes the names from the save's .json and the live player list.
    /// </summary>
    internal static bool IsPlayerLike(Hero h) =>
        h.IsHumanPlayerCharacter
        || h == TaleWorlds.CampaignSystem.Hero.MainHero
        || h.StringId.IndexOf("player", StringComparison.OrdinalIgnoreCase) >= 0
        || (h.Clan != null && h.Clan.Leader == h && h.Clan.StringId.IndexOf("player", StringComparison.OrdinalIgnoreCase) >= 0);

    private static Settlement? Place(Hero h) => h.CurrentSettlement ?? h.PartyBelongedTo?.CurrentSettlement ?? h.StayingInSettlement;

    /// <summary>
    /// <c>coodsz.hero &lt;id&gt;</c>:
    /// <c>HERO|id|name|level|gold|hp|max hp|age|influence|renown|clan tier|troops|wounded|clan id|clan|kingdom id|kingdom|culture id|culture|place id|place|party id</c>.
    /// </summary>
    public static string Profile(List<string> args)
    {
        if (!TryHero(args, "coodsz.hero <hero_id>", out var hero, out var error)) return error;
        var h = hero!;
        var clan = h.Clan;
        var party = h.PartyBelongedTo;
        var place = Place(h);
        var troops = party?.MemberRoster.TotalManCount - party?.MemberRoster.TotalHeroes ?? 0;
        var wounded = party?.MemberRoster.TotalWounded - party?.MemberRoster.TotalWoundedHeroes ?? 0;
        return Lines(new List<string>
        {
            Line("HERO", h.StringId, Clean(h.Name?.ToString()), I(h.Level), I(h.Gold), I(h.HitPoints), I(h.MaxHitPoints), F(h.Age),
                F(clan?.Influence ?? 0), F(clan?.Renown ?? 0), I(clan?.Tier ?? 0), I(troops), I(wounded),
                clan?.StringId ?? "", Clean(clan?.Name?.ToString()), clan?.Kingdom?.StringId ?? "", Clean(clan?.Kingdom?.Name?.ToString()),
                h.Culture?.StringId ?? "", Clean(h.Culture?.Name?.ToString()), place?.StringId ?? "", Clean(place?.Name?.ToString()),
                party?.StringId ?? ""),
        });
    }

    // EquipmentIndex has two names for one value (Head = NumAllWeaponSlots), so the names are written out.
    private static readonly string[] SlotNames =
        { "Weapon0", "Weapon1", "Weapon2", "Weapon3", "Banner", "Head", "Body", "Leg", "Gloves", "Cape", "Horse", "HorseHarness" };

    /// <summary><c>coodsz.equipment &lt;id&gt;</c>: <c>EQ|battle|slot|item id|name|value</c> and <c>EQ|civilian|...</c>.</summary>
    public static string Equipment(List<string> args)
    {
        if (!TryHero(args, "coodsz.equipment <hero_id>", out var hero, out var error)) return error;

        var lines = new List<string>();
        void Dump(string set, Equipment? eq)
        {
            if (eq is null) return;
            for (var i = 0; i < SlotNames.Length; i++)
            {
                var item = eq[i].Item;
                if (item is null) continue;
                lines.Add(Line("EQ", set, SlotNames[i], item.StringId, Clean(item.Name?.ToString()), I(item.Value)));
            }
        }

        Dump("battle", hero!.BattleEquipment);
        Dump("civilian", hero.CivilianEquipment);
        return Lines(lines);
    }

    /// <summary><c>coodsz.gold &lt;id&gt; &lt;amount&gt;</c>: gives gold, or takes it with a negative amount (never below 0).</summary>
    public static string Gold(List<string> args)
    {
        if (!TryHero(args, "coodsz.gold <hero_id> <amount>", out var hero, out var error)) return error;
        if (!TryInt(args, 1, -MaxGold, MaxGold, out var amount) || amount == 0)
            return "COODSZ_ERROR amount must be between -" + I(MaxGold) + " and " + I(MaxGold) + ", not 0";

        var old = hero!.Gold;
        var change = Math.Max(amount, -old);
        if (change != 0) hero.ChangeHeroGold(change);
        return Set(hero, "gold", old, hero.Gold);
    }

    /// <summary><c>coodsz.set_gold &lt;id&gt; &lt;gold&gt;</c>.</summary>
    public static string SetGold(List<string> args)
    {
        if (!TryHero(args, "coodsz.set_gold <hero_id> <gold>", out var hero, out var error)) return error;
        if (!TryInt(args, 1, 0, MaxGold, out var gold)) return "COODSZ_ERROR gold must be between 0 and " + I(MaxGold);

        var old = hero!.Gold;
        if (gold != old) hero.ChangeHeroGold(gold - old);
        return Set(hero, "gold", old, hero.Gold);
    }

    /// <summary><c>coodsz.influence &lt;id&gt; &lt;amount&gt;</c>: the hero's clan gains or loses influence.</summary>
    public static string Influence(List<string> args)
    {
        if (!TryHero(args, "coodsz.influence <hero_id> <amount>", out var hero, out var error)) return error;
        if (!TryInt(args, 1, -MaxInfluence, MaxInfluence, out var amount) || amount == 0)
            return "COODSZ_ERROR amount must be between -" + I(MaxInfluence) + " and " + I(MaxInfluence) + ", not 0";
        if (hero!.Clan is not { } clan) return "COODSZ_ERROR hero '" + hero.StringId + "' has no clan.";

        var old = (int)Math.Round(clan.Influence);
        ChangeClanInfluenceAction.Apply(clan, amount);
        return Set(hero, "influence", old, (int)Math.Round(clan.Influence));
    }

    /// <summary>
    /// <c>coodsz.renown &lt;id&gt; &lt;amount&gt;</c>: the hero's clan gains renown, or loses it with a
    /// negative amount (never below 0; the clan tier follows the renown).
    /// </summary>
    public static string Renown(List<string> args)
    {
        if (!TryHero(args, "coodsz.renown <hero_id> <amount>", out var hero, out var error)) return error;
        if (!TryInt(args, 1, -MaxRenown, MaxRenown, out var amount) || amount == 0)
            return "COODSZ_ERROR amount must be between -" + I(MaxRenown) + " and " + I(MaxRenown) + ", not 0";
        if (hero!.Clan is not { } clan) return "COODSZ_ERROR hero '" + hero.StringId + "' has no clan.";

        var old = (int)Math.Round(clan.Renown);
        SetRenown(clan, Math.Max(0, clan.Renown + amount));
        return Set(hero, "renown", old, (int)Math.Round(clan.Renown));
    }

    /// <summary><c>coodsz.clan_tier &lt;id&gt; &lt;tier&gt;</c>: sets the clan's renown to what that tier needs.</summary>
    public static string ClanTier(List<string> args)
    {
        if (!TryHero(args, "coodsz.clan_tier <hero_id> <tier>", out var hero, out var error)) return error;
        if (hero!.Clan is not { } clan) return "COODSZ_ERROR hero '" + hero.StringId + "' has no clan.";
        var model = Campaign.Current.Models.ClanTierModel;
        if (!TryInt(args, 1, model.MinClanTier, model.MaxClanTier, out var tier))
            return "COODSZ_ERROR tier must be between " + I(model.MinClanTier) + " and " + I(model.MaxClanTier);

        var old = clan.Tier;
        var required = model.GetRequiredRenownForTier(tier);
        // The next tier's threshold minus one keeps the clan at this tier when going down.
        var target = tier < old ? Math.Max(required, Math.Min(clan.Renown, model.GetRequiredRenownForTier(tier + 1) - 1)) : Math.Max(required, clan.Renown);
        SetRenown(clan, target);
        return Set(hero, "clan_tier", old, clan.Tier);
    }

    /// <summary>
    /// Clan.AddRenown only adds; the renown setter is not public. Going down writes the property
    /// through reflection and lets the clan recount its tier.
    /// </summary>
    private static void SetRenown(Clan clan, float value)
    {
        var change = value - clan.Renown;
        if (change > 0)
        {
            clan.AddRenown(change, false);
            return;
        }

        if (change == 0) return;
        var property = typeof(Clan).GetProperty("Renown", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        var setter = property?.GetSetMethod(true);
        if (setter != null) setter.Invoke(clan, new object[] { value });
        else typeof(Clan).GetField("_renown", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(clan, value);
        var update = typeof(Clan).GetMethod("UpdateTier", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
        update?.Invoke(clan, null);
        if (update == null && clan.Tier > Campaign.Current.Models.ClanTierModel.CalculateTier(clan))
            typeof(Clan).GetProperty("Tier", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetSetMethod(true)
                ?.Invoke(clan, new object[] { Campaign.Current.Models.ClanTierModel.CalculateTier(clan) });
    }

    /// <summary><c>coodsz.health &lt;id&gt; &lt;hit points&gt;</c>: 1 up to the hero's own maximum.</summary>
    public static string Health(List<string> args)
    {
        if (!TryHero(args, "coodsz.health <hero_id> <hit_points>", out var hero, out var error)) return error;
        if (!TryInt(args, 1, 1, 10_000, out var hp)) return "COODSZ_ERROR hit points must be between 1 and the hero's maximum";

        var old = hero!.HitPoints;
        hero.HitPoints = Clamp(hp, 1, Math.Max(1, hero.MaxHitPoints));
        return Set(hero, "health", old, hero.HitPoints);
    }

    /// <summary><c>coodsz.age &lt;id&gt; &lt;age&gt;</c>: 18 to 90.</summary>
    public static string Age(List<string> args)
    {
        if (!TryHero(args, "coodsz.age <hero_id> <age>", out var hero, out var error)) return error;
        if (!TryInt(args, 1, 18, 90, out var age)) return "COODSZ_ERROR age must be between 18 and 90";

        var old = (int)hero!.Age;
        hero.SetBirthDay(CampaignTime.YearsFromNow(-age));
        return Set(hero, "age", old, (int)hero.Age);
    }
}
