using BannerlordCoopLauncher.Core.Parsing;
using TaomCoop.Core;

namespace RealmOfThrones.Core;

/// <summary>
/// Auto sort for the extra mods of a Realm of Thrones setup, from each mod's SubModule.xml. The core
/// never moves and every extra stays in the one place before the compat module. Inside it, a mod loads
/// after the mods it needs and before the mods that ask to load after it. Mods that ask for each other
/// first keep their old order. The mod facts come from TaomModFacts.Gather, whose library list (Harmony,
/// ButterLib, UIExtenderEx, MCM) is the Realm of Thrones library list too.
/// </summary>
public static class RotAutoSort
{
    // Mods players found work only at the very bottom of the load order. Auto sort puts them last.
    public static readonly IReadOnlySet<string> LoadsLast = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "RTSCamera", "RTSCamera.CommandSystem" };

    /// <summary>Sorts the setup in place. Returns plain lines that say what changed.</summary>
    public static IReadOnlyList<string> Sort(TaomSetup setup, IReadOnlyDictionary<string, ModuleManifest> manifests)
    {
        if (setup.Locked)
            throw new InvalidOperationException(setup.Name + " is locked. Click New setup or Copy to make a setup you can add mods to.");
        RotLoadOrder.Normalize(setup);
        var lines = new List<string>();
        var before = setup.Extras.Select(mod => mod.ModuleId).ToList();
        var ids = new HashSet<string>(before, StringComparer.OrdinalIgnoreCase);
        ModuleManifest? M(string id) => manifests.TryGetValue(id, out var manifest) ? manifest : null;
        IEnumerable<string> Needs(string id) => M(id)?.Needs.Select(link => link.Id) ?? Enumerable.Empty<string>();
        IEnumerable<string> LoadsBefore(string id) => M(id)?.LoadsBefore ?? (IEnumerable<string>)Array.Empty<string>();

        var index = before.Select((id, i) => (id, i)).ToDictionary(item => item.id, item => item.i, StringComparer.OrdinalIgnoreCase);
        var incoming = before.ToDictionary(id => id, _ => 0, StringComparer.OrdinalIgnoreCase);
        var next = before.ToDictionary(id => id, _ => new List<string>(), StringComparer.OrdinalIgnoreCase);
        void Edge(string first, string second)
        {
            if (first.Equals(second, StringComparison.OrdinalIgnoreCase) || next[first].Contains(second, StringComparer.OrdinalIgnoreCase))
                return;
            next[first].Add(second);
            incoming[second]++;
        }

        foreach (var id in before)
        {
            foreach (var need in Needs(id).Where(ids.Contains))
                Edge(need, id);
            foreach (var later in LoadsBefore(id).Where(ids.Contains))
                Edge(id, later);
        }

        var ready = new SortedSet<string>(before.Where(id => incoming[id] == 0),
            Comparer<string>.Create((a, b) => index[a].CompareTo(index[b])));
        var placed = new List<string>();
        while (ready.Count > 0)
        {
            var id = ready.Min!;
            ready.Remove(id);
            placed.Add(id);
            foreach (var child in next[id])
            {
                if (--incoming[child] == 0)
                    ready.Add(child);
            }
        }

        var stuck = before.Where(id => !placed.Contains(id, StringComparer.OrdinalIgnoreCase)).OrderBy(id => index[id]).ToList();
        if (stuck.Count > 1)
        {
            var labels = stuck.Select(id => setup.Extras.First(mod => mod.ModuleId.Equals(id, StringComparison.OrdinalIgnoreCase)).Label);
            lines.Add(string.Join(", ", labels) + " each ask to load first. Their order was left as it was.");
        }

        placed.AddRange(stuck);
        // RTS Camera works at the very bottom of the extras.
        placed = placed.OrderBy(id => LoadsLast.Contains(id) ? 1 : 0).ToList();
        setup.Extras = placed.Select(id => setup.Extras.First(mod => mod.ModuleId.Equals(id, StringComparison.OrdinalIgnoreCase))).ToList();
        if (lines.Count == 0 && !before.SequenceEqual(placed, StringComparer.OrdinalIgnoreCase))
            lines.Add("Sorted the extra mods so each one loads after the mods it needs.");
        return lines;
    }
}

/// <summary>
/// Checks before Connect or Start server. Hard problems (a missing or turned off dependency, two
/// mods that do not work together) block; the rest are warnings: a dependency that loads too late,
/// a mod made for another game version, a mod that brings its own copy of a library mod, a mod that
/// is on this PC more than once, a mod with blocked DLLs.
/// </summary>
public static class RotModChecks
{
    // Optional TaleWorlds modules a mod may name; the game starts without them.
    private static readonly HashSet<string> Soft = new(StringComparer.OrdinalIgnoreCase) { "CustomBattle", "StoryMode", "BirthAndDeath", "NavalDLC" };

    public static IReadOnlyList<TaomModIssue> Run(TaomSetup setup, IReadOnlyDictionary<string, TaomModFact> facts, string gameVersion = RotStack.GameVersion)
    {
        var issues = new List<TaomModIssue>();
        var order = RotLoadOrder.ClientOrder(setup).ToList();
        var position = order.Select((id, i) => (id, i)).ToDictionary(item => item.id, item => item.i, StringComparer.OrdinalIgnoreCase);
        var labels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var extra in setup.Extras)
            labels.TryAdd(extra.ModuleId.Trim(), extra.Label);
        string Label(string id) => labels.TryGetValue(id, out var label) ? label : id;
        var disabled = new HashSet<string>(setup.Extras.Where(mod => !mod.Enabled).Select(mod => mod.ModuleId), StringComparer.OrdinalIgnoreCase);
        var pairs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var mod in setup.EnabledExtras.Where(mod => position.ContainsKey(mod.ModuleId)))
        {
            var id = mod.ModuleId;
            facts.TryGetValue(id, out var fact);
            var manifest = fact?.Manifest;
            if (fact != null && fact.Copies.Count > 1)
                issues.Add(new TaomModIssue(false, mod.Label + " is on this PC " + fact.Copies.Count + " times. The game can load the wrong copy. Keep one copy."));
            if (fact is { BlockedDlls: > 0 })
                issues.Add(new TaomModIssue(false, "Unblock DLLs. " + mod.Label + " has DLL files Windows marked as downloaded from the internet. The launcher unblocks them before the game starts."));
            if (manifest == null)
                continue;

            foreach (var need in manifest.RequiredIds.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                // A mod made for the Workshop Coop or the nightly means whichever Coop the set runs.
                var target = need.Equals("Coop", StringComparison.OrdinalIgnoreCase) || need.Equals("CoopNightly", StringComparison.OrdinalIgnoreCase)
                    ? RotStack.CoopModuleId
                    : need;
                if (Soft.Contains(target))
                    continue;
                if (!position.TryGetValue(target, out var at))
                {
                    issues.Add(new TaomModIssue(true, disabled.Contains(target)
                        ? mod.Label + " needs " + Label(target) + ", which is turned off."
                        : mod.Label + " needs " + Label(target) + ", which is not on this setup."));
                    continue;
                }

                if (at > position[id])
                    issues.Add(new TaomModIssue(false, mod.Label + " loads before " + Label(target) + ", which it needs. Click Auto sort."));
            }

            foreach (var other in manifest.Incompatible)
            {
                if (!position.ContainsKey(other) || other.Equals(id, StringComparison.OrdinalIgnoreCase))
                    continue;
                var key = string.Compare(id, other, StringComparison.OrdinalIgnoreCase) < 0 ? id + "|" + other : other + "|" + id;
                if (pairs.Add(key))
                    issues.Add(new TaomModIssue(true, mod.Label + " does not work together with " + Label(other) + ". Turn one of them off."));
            }

            var native = manifest.NativeVersion;
            if (native != null && !TaomModChecks.SameGame(native, gameVersion))
                issues.Add(new TaomModIssue(false, mod.Label + " was made for Bannerlord " + native.Trim() + ". Realm of Thrones runs on " + gameVersion.TrimStart('v') + ", so it may not work."));

            // The core already loads Harmony, ButterLib, UIExtenderEx and MCM from their own modules.
            if (fact!.BundledLibs.Count > 0)
                issues.Add(new TaomModIssue(false, mod.Label + " brings its own " + string.Join(", ", fact.BundledLibs) + ". The Realm of Thrones core already loads them, and two copies can clash."));
        }

        return issues;
    }
}
