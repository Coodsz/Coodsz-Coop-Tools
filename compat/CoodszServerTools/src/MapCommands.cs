// Coodsz Server Tools: where everything is on the campaign map, for Coodsz Host's live map page.
// Read only. The idea (settlements and lord and player parties with their map position, read
// from the running campaign every few seconds) follows CoopKeep's live map
// (https://github.com/Aderimo/bannerlord-coopkeep, coopkeep.parties and coopkeep.owners); the
// command and its lines are new.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using static CoodszServerTools.Text;

namespace CoodszServerTools;

/// <summary>Reads positions on the campaign map and writes them to a file; it never changes anything.</summary>
internal static class MapCommands
{
    /// <summary>
    /// <c>coodsz.map [parties]</c>: kingdoms with their colours, every town, castle and village with
    /// its map position and owner, and the parties of players and lords, written to
    /// <c>out\map.txt</c>. With <c>parties</c> the settlements are left out (they do not move, so
    /// Coodsz Host reads them less often). Positions are the game's own map coordinates.
    /// <code>
    /// MAPKINGDOM|id|name|colour|colour 2
    /// MAPSETTLEMENT|id|name|kind|x|y|owner clan id|owner clan|kingdom id|colour|bound id|under siege
    /// MAPPARTY|party id|hero id|name|clan id|clan|kingdom id|kind|x|y|troops|wounded|settlement id|army leader id|army troops|battle|siege|target id|colour
    /// </code>
    /// Colours are ARGB in hex. <c>kind</c> of a party is player or lord; a party inside a settlement
    /// has its settlement id; a party in an army names the army leader's hero id.
    /// </summary>
    public static string Map(List<string> args)
    {
        if (Campaign.Current is null) return NoCampaign;
        var partiesOnly = args.Count > 0 && string.Equals(args[0], "parties", StringComparison.OrdinalIgnoreCase);
        var lines = new List<string>();

        foreach (var k in NotNull(Campaign.Current.Kingdoms))
        {
            if (k.IsEliminated) continue;
            lines.Add(Line("MAPKINGDOM", k.StringId, Clean(k.Name?.ToString()), Hex(k.Color), Hex(k.Color2)));
        }

        if (!partiesOnly)
        {
            foreach (var s in NotNull(Settlement.All))
            {
                var kind = s.IsTown ? "town" : s.IsCastle ? "castle" : s.IsVillage ? "village" : "";
                if (kind.Length == 0) continue;
                var at = s.GetPosition2D;
                var faction = s.MapFaction;
                lines.Add(Line("MAPSETTLEMENT", s.StringId, Clean(s.Name?.ToString()), kind, F(at.x), F(at.y),
                    s.OwnerClan?.StringId ?? "", Clean(s.OwnerClan?.Name?.ToString()), faction is Kingdom ? faction.StringId : "",
                    Hex(faction?.Color ?? 0), s.Village?.Bound?.StringId ?? "", B(s.IsUnderSiege)));
            }
        }

        foreach (var p in NotNull(MobileParty.All))
        {
            var hero = p.LeaderHero;
            if (!p.IsActive || hero is null) continue;
            var player = HeroCommands.IsPlayerLike(hero);
            if (!player && !p.IsLordParty) continue;
            var at = p.GetPosition2D;
            var army = p.Army;
            var armyLeader = army?.LeaderParty?.LeaderHero?.StringId ?? "";
            var armyTroops = army != null && army.LeaderParty == p
                ? NotNull(army.Parties).Sum(x => x.MemberRoster?.TotalManCount ?? 0)
                : 0;
            lines.Add(Line("MAPPARTY", p.StringId, hero.StringId, Clean(hero.Name?.ToString()), hero.Clan?.StringId ?? "",
                Clean(hero.Clan?.Name?.ToString()), hero.Clan?.Kingdom?.StringId ?? "", player ? "player" : "lord",
                F(at.x), F(at.y), I(p.MemberRoster?.TotalManCount ?? 0), I(p.MemberRoster?.TotalWounded ?? 0),
                p.CurrentSettlement?.StringId ?? "", armyLeader, I(armyTroops), B(p.MapEvent != null),
                B(p.BesiegedSettlement != null), p.TargetSettlement?.StringId ?? "", Hex(p.MapFaction?.Color ?? 0)));
        }

        return ToFile("map", lines);
    }

    private static string Hex(uint colour) => colour.ToString("X8", CultureInfo.InvariantCulture);
}
