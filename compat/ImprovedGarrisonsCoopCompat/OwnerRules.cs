// Improved Garrisons Coop Compat: the plain rules, without game types, so Coodsz Host's tests can run them.

using System;
using System.Collections.Generic;
using System.Linq;

namespace ImprovedGarrisonsCoopCompat;

internal static class OwnerRules
{
    /// <summary>
    /// Whether a town's Improved Garrisons settings go when it changes owner: only when it moves to
    /// another clan. A clan member taking it keeps them; with neither clan known nothing changes.
    /// </summary>
    public static bool DropsSettings(object? newClan, object? oldClan) =>
        (newClan != null || oldClan != null) && !ReferenceEquals(newClan, oldClan);

    /// <summary>The settlement ids of a CopySpecific request's StrArg, which CoopModPatch splits on ';'.</summary>
    public static List<string> SplitIds(string? ids) =>
        string.IsNullOrEmpty(ids) ? new List<string>() : ids.Split(';').Where(id => id.Length > 0).ToList();

    /// <summary>
    /// The first target id that is a real town or castle not owned by the requester's clan, or null when
    /// every known target is the clan's own. Unknown ids are left out, CoopModPatch skips them too.
    /// </summary>
    public static string? FirstForeignTarget(IEnumerable<string> ids, Func<string, (bool Known, object? Owner)> lookup, object clan)
    {
        foreach (var id in ids)
        {
            var (known, owner) = lookup(id);
            if (known && !ReferenceEquals(owner, clan))
                return id;
        }
        return null;
    }

    /// <summary>
    /// The targets a CopyAll request becomes on the server: the requester clan's castles when the mode is
    /// "castles", else its towns, without the source. CoopModPatch sends "castles" or "towns".
    /// </summary>
    public static List<string> CopyAllTargets(IEnumerable<(string Id, bool IsCastle, object? Owner)> settlements, string sourceId, string? mode, object clan)
    {
        var castles = mode == "castles";
        return settlements
            .Where(s => s.IsCastle == castles && ReferenceEquals(s.Owner, clan) && s.Id != sourceId)
            .Select(s => s.Id)
            .ToList();
    }
}
