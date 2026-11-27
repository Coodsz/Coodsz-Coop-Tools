// Coodsz Server Tools: the world as the running campaign holds it, for Coodsz Host's Encyclopedia
// page. Read only. The sections (kingdoms, clans, lords, settlements, troops) follow CoopKeep's
// encyclopedia (https://github.com/Aderimo/bannerlord-coopkeep, src/CoopKeep.Core/Encyclopedia),
// which reads them from the game's XML; here they come from the live campaign instead.

using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.ObjectSystem;
using static CoodszServerTools.Text;

namespace CoodszServerTools;

/// <summary>Reads the campaign and writes it to a file; it never changes anything.</summary>
internal static class WorldCommands
{
    /// <summary>
    /// <c>coodsz.world</c>: kingdoms, clans, living lords and wanderers, settlements and troop details,
    /// written to <c>out\world.txt</c>. Lists inside a field are split by <c>;</c>.
    /// <code>
    /// KINGDOM|id|name|culture id|culture|ruler id|ruler|war ids|text
    /// CLAN|id|name|culture id|culture|kingdom id|kingdom|leader id|leader|tier|renown|minor|bandit|player|text
    /// LORD|id|name|clan id|clan|kingdom id|kingdom|culture id|culture|age|female|level|gold|hp|max hp|place id|place|state|player|spouse id|father id|mother id|kind|text
    /// SETTLEMENT|id|name|kind|culture id|culture|owner clan id|owner clan|kingdom id|kingdom|prosperity|hearth|loyalty|security|food|garrison|militia|bound id|text
    /// TROOP|id|name|culture id|culture|tier|level|class|mounted|ranged|upgrade ids|skills (id=value;)|item ids
    /// </code>
    /// </summary>
    public static string World(List<string> args)
    {
        if (Campaign.Current is null) return NoCampaign;
        var lines = new List<string>();

        foreach (var k in NotNull(Campaign.Current.Kingdoms))
        {
            if (k.IsEliminated) continue;
            var wars = NotNull(Campaign.Current.Kingdoms).Where(o => o != k && !o.IsEliminated && k.IsAtWarWith(o)).Select(o => o.StringId);
            lines.Add(Line("KINGDOM", k.StringId, Clean(k.Name?.ToString()), k.Culture?.StringId ?? "", Clean(k.Culture?.Name?.ToString()),
                k.Leader?.StringId ?? "", Clean(k.Leader?.Name?.ToString()), string.Join(";", wars), Clean(k.EncyclopediaText?.ToString())));
        }

        foreach (var c in NotNull(Clan.All))
        {
            if (c.IsEliminated || string.IsNullOrEmpty(c.StringId)) continue;
            lines.Add(Line("CLAN", c.StringId, Clean(c.Name?.ToString()), c.Culture?.StringId ?? "", Clean(c.Culture?.Name?.ToString()),
                c.Kingdom?.StringId ?? "", Clean(c.Kingdom?.Name?.ToString()), c.Leader?.StringId ?? "", Clean(c.Leader?.Name?.ToString()),
                I(c.Tier), I((int)Math.Round(c.Renown)), B(c.IsMinorFaction), B(c.IsBanditFaction),
                B(c.Leader != null && HeroCommands.IsPlayerLike(c.Leader)), Clean(c.EncyclopediaText?.ToString())));
        }

        foreach (var h in NotNull(Hero.AllAliveHeroes))
        {
            var player = HeroCommands.IsPlayerLike(h);
            var kind = player ? "player" : h.IsLord ? "lord" : h.IsWanderer ? "wanderer" : "";
            if (kind.Length == 0 || h.IsChild) continue;
            var place = h.CurrentSettlement ?? h.PartyBelongedTo?.CurrentSettlement ?? h.StayingInSettlement;
            lines.Add(Line("LORD", h.StringId, Clean(h.Name?.ToString()), h.Clan?.StringId ?? "", Clean(h.Clan?.Name?.ToString()),
                h.Clan?.Kingdom?.StringId ?? "", Clean(h.Clan?.Kingdom?.Name?.ToString()), h.Culture?.StringId ?? "", Clean(h.Culture?.Name?.ToString()),
                I((int)h.Age), B(h.IsFemale), I(h.Level), I(h.Gold), I(h.HitPoints), I(h.MaxHitPoints),
                place?.StringId ?? "", Clean(place?.Name?.ToString()), h.HeroState.ToString(), B(player),
                h.Spouse?.StringId ?? "", h.Father?.StringId ?? "", h.Mother?.StringId ?? "", kind, Clean(h.EncyclopediaText?.ToString())));
        }

        foreach (var s in NotNull(Settlement.All))
        {
            var kind = s.IsTown ? "town" : s.IsCastle ? "castle" : s.IsVillage ? "village" : s.IsHideout ? "hideout" : "";
            if (kind.Length == 0) continue;
            var town = s.Town;
            var village = s.Village;
            var faction = s.MapFaction;
            lines.Add(Line("SETTLEMENT", s.StringId, Clean(s.Name?.ToString()), kind, s.Culture?.StringId ?? "", Clean(s.Culture?.Name?.ToString()),
                s.OwnerClan?.StringId ?? "", Clean(s.OwnerClan?.Name?.ToString()),
                faction is Kingdom ? faction.StringId : "", faction is Kingdom ? Clean(faction.Name?.ToString()) : "",
                I((int)Math.Round(town?.Prosperity ?? 0)), I((int)Math.Round(village?.Hearth ?? 0)),
                I((int)Math.Round(town?.Loyalty ?? 0)), I((int)Math.Round(town?.Security ?? 0)), I((int)Math.Round(town?.FoodStocks ?? 0)),
                I(town?.GarrisonParty?.MemberRoster.TotalManCount ?? 0), I((int)Math.Round(s.Militia)),
                village?.Bound?.StringId ?? "", Clean(s.EncyclopediaText?.ToString())));
        }

        foreach (var t in MBObjectManager.Instance.GetObjectTypeList<CharacterObject>())
        {
            if (t is null || t.IsHero || t.IsTemplate || string.IsNullOrEmpty(t.StringId)) continue;
            if (t.Occupation != Occupation.Soldier && t.Occupation != Occupation.Mercenary && t.Occupation != Occupation.Bandit) continue;
            var upgrades = (t.UpgradeTargets ?? Array.Empty<CharacterObject>()).Where(u => u != null).Select(u => u.StringId);
            var skills = MBObjectManager.Instance.GetObjectTypeList<SkillObject>().Where(k => k != null).Select(k => (k.StringId, Value: t.GetSkillValue(k))).Where(k => k.Value > 0).Select(k => k.StringId + "=" + I(k.Value));
            var gear = new List<string>();
            var equipment = t.FirstBattleEquipment;
            if (equipment != null)
                for (var i = 0; i < 12; i++)
                    if (equipment[i].Item is { } item && !gear.Contains(item.StringId))
                        gear.Add(item.StringId);
            lines.Add(Line("TROOP", t.StringId, Clean(t.Name?.ToString()), t.Culture?.StringId ?? "", Clean(t.Culture?.Name?.ToString()),
                I(t.Tier), I(t.Level), t.DefaultFormationClass.ToString(), B(t.IsMounted), B(t.IsRanged),
                string.Join(";", upgrades), string.Join(";", skills), string.Join(";", gear)));
        }

        return ToFile("world", lines);
    }
}
