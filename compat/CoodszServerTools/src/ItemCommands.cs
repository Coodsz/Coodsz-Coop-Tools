// Coodsz Server Tools: give and take items, the party inventory and the item list. Ported from
// CoopKeep (https://github.com/Aderimo/bannerlord-coopkeep), src/CoopKeep.ServerTools/ServerToolsSubModule.cs,
// translated from Turkish and changed for Coodsz Host.

using System;
using System.Collections.Generic;
using System.Globalization;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.Core;
using TaleWorlds.ObjectSystem;
using static CoodszServerTools.Text;

namespace CoodszServerTools;

/// <summary>Items go into the hero's party inventory (ItemRoster); Coop sends inventory changes to the players.</summary>
internal static class ItemCommands
{
    public const int MaxCount = 10_000;

    /// <summary>
    /// <c>coodsz.items</c>: every item in the game, written to a file:
    /// <c>ITEM|id|name|type|value|tier|culture id|culture|civilian|food|trade good|weapon class|weight</c>.
    /// </summary>
    /// <remarks>Food and trade goods are not in the game's item XML, so the full list comes from the game itself.</remarks>
    public static string Items(List<string> args)
    {
        var lines = new List<string>();
        foreach (var item in MBObjectManager.Instance.GetObjectTypeList<ItemObject>())
        {
            if (item is null || string.IsNullOrEmpty(item.StringId)) continue;
            lines.Add(Line("ITEM", item.StringId, Clean(item.Name?.ToString()), item.ItemType.ToString(), I(item.Value),
                I((int)item.Tier), item.Culture?.StringId ?? "", Clean(item.Culture?.Name?.ToString()), B(item.IsCivilian), B(item.IsFood),
                B(item.IsTradeGood), item.PrimaryWeapon?.WeaponClass.ToString() ?? "", item.Weight.ToString("0.##", CultureInfo.InvariantCulture)));
        }

        return ToFile("items", lines);
    }

    /// <summary><c>coodsz.inventory &lt;id&gt;</c>: <c>INV|item id|name|count|value</c>.</summary>
    public static string Inventory(List<string> args)
    {
        if (FindRoster(args, "coodsz.inventory <hero_id>", out _, out var roster) is { } error) return error;

        var lines = new List<string>();
        for (var i = 0; i < roster!.Count; i++)
        {
            var element = roster.GetElementCopyAtIndex(i);
            if (element.EquipmentElement.Item is { } item && element.Amount > 0)
                lines.Add(Line("INV", item.StringId, Clean(item.Name?.ToString()), I(element.Amount), I(item.Value)));
        }

        return Lines(lines);
    }

    /// <summary>
    /// <c>coodsz.give_item &lt;id&gt; &lt;item id&gt; &lt;count&gt;</c> and <c>coodsz.take_item</c> (takes at most what the party has).
    /// </summary>
    public static string ChangeItem(List<string> args, int sign)
    {
        var usage = "coodsz." + (sign > 0 ? "give_item" : "take_item") + " <hero_id> <item_id> <count>";
        if (args.Count < 3) return "Usage: " + usage;
        if (!TryInt(args, 2, 1, MaxCount, out var count)) return "COODSZ_ERROR count must be between 1 and " + I(MaxCount);
        if (FindRoster(args, usage, out var hero, out var roster) is { } error) return error;

        var item = MBObjectManager.Instance.GetObject<ItemObject>(args[1]);
        if (item is null) return "COODSZ_ERROR item '" + Clean(args[1]) + "' not found.";

        var old = roster!.GetItemNumber(item);
        var delta = sign > 0 ? count : -Math.Min(count, old);
        if (delta != 0) roster.AddToCounts(item, delta);
        return Set(hero!, "item:" + item.StringId, old, roster.GetItemNumber(item));
    }

    private static string? FindRoster(List<string> args, string usage, out Hero? hero, out ItemRoster? roster)
    {
        roster = null;
        if (!TryHero(args, usage, out hero, out var error)) return error;
        if (hero!.PartyBelongedTo is not { } party) return "COODSZ_ERROR hero '" + hero.StringId + "' has no party.";
        roster = party.ItemRoster;
        return null;
    }
}
