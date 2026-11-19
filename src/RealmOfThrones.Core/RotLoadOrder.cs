using BannerlordCoopLauncher.Core.Launch;
using BannerlordCoopLauncher.Core.Modules;
using TaomCoop.Core;

namespace RealmOfThrones.Core;

/// <summary>
/// The locked Realm of Thrones core and the rules for extra mods in it.
///
/// The core is RotStack.ClientModules (RotStack.ServerModules, the server-config.json list, on the
/// host) in its sealed order. Extras load in one place only: after ROT-Dragon and before Realm of
/// Thrones Coop Compat, on both sides, so the compat module always loads last. There is no "before
/// Coop" place: the dedicated server loads Coop and NavalDLC before every module of its list, so it
/// could not mirror one. Client add-ons (DedicatedServer.*) still load after the compat module.
///
/// The setups use the TAOM setup model. Every extra is saved as ExtraSlot.AfterTaom; a saved extra
/// with any other slot is read as that place.
/// </summary>
public static class RotLoadOrder
{
    /// <summary>After ROT-Dragon, before the compat module. The only place.</summary>
    public const ExtraSlot SlotAfterRot = ExtraSlot.AfterTaom;

    public static IReadOnlyList<string> CoreBeforeExtras(IReadOnlyList<string> core) =>
        core.Where(id => !id.Equals(RotStack.CompatModuleId, StringComparison.OrdinalIgnoreCase)).ToArray();

    private static readonly HashSet<string> NotForCampaign = new(StringComparer.OrdinalIgnoreCase)
    {
        "Multiplayer",
        "DedicatedCustomServer"
    };

    /// <summary>The official modules of Bannerlord 1.4.8 (ModuleType Official or OfficialOptional in the
    /// game's Modules folder). The ones outside the core are never extras, so none lands in the server's
    /// "modules" list.</summary>
    public static IReadOnlyCollection<string> OfficialModules { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Native", "SandBoxCore", "Sandbox", "CustomBattle", "StoryMode", "NavalDLC", "BirthAndDeath", "FastMode", "Multiplayer"
    };

    /// <summary>The TAOM core modules. TAOM is another overhaul and never loads with Realm of Thrones.</summary>
    public static IReadOnlyCollection<string> TaomCoreIds { get; } =
        TaomStack.ContentVersions.Keys.Append(TaomStack.CompatModuleId).ToHashSet(StringComparer.OrdinalIgnoreCase);

    // The Burning Empires core modules (BeStack in BurningEmpires.Core, which this library does not load).
    public static IReadOnlyCollection<string> BeCoreIds { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "BurningEmpires", "BurningEmpires_Elephants", "BurningEmpires.CoopCompat" };

    public static bool IsTaomCore(string? moduleId) =>
        moduleId != null && TaomCoreIds.Contains(moduleId.Trim());

    public static bool IsBeCore(string? moduleId) =>
        moduleId != null && BeCoreIds.Contains(moduleId.Trim());

    /// <summary>The overhaul a core module of TAOM or Burning Empires belongs to, or null.</summary>
    public static string? OtherOverhaul(string? moduleId) =>
        IsTaomCore(moduleId) ? "TAOM" : IsBeCore(moduleId) ? "Burning Empires" : null;

    // The Chaos in Calradia mod and its compat module, which have tools of their own. No other tool's list has them.
    public static IReadOnlyCollection<string> ChaosIds { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "ChaosInCalradia", "ChaosInCalradia.CoopCompat" };

    /// <summary>The mod with tools of its own (Chaos in Calradia) a module belongs to, or null.</summary>
    public static string? OtherTools(string? moduleId) =>
        moduleId != null && ChaosIds.Contains(moduleId.Trim()) ? "Chaos in Calradia" : null;

    /// <summary>The other mod a module belongs to, an overhaul or a mod with tools of its own, or null.</summary>
    public static string? OtherGame(string? moduleId) => OtherOverhaul(moduleId) ?? OtherTools(moduleId);

    /// <summary>True for the modules of the Realm of Thrones core, client or server side.</summary>
    public static bool IsCore(string? moduleId) =>
        !string.IsNullOrWhiteSpace(moduleId)
        && (RotStack.ClientModules.Contains(moduleId.Trim(), StringComparer.OrdinalIgnoreCase)
            || RotStack.ServerModules.Contains(moduleId.Trim(), StringComparer.OrdinalIgnoreCase));

    /// <summary>The Realm of Thrones module this id is a second copy of (ROT_Core, ROT-Map, rot content),
    /// or null. Ids are compared on their letters and digits only, without case.</summary>
    public static string? SecondCopyOf(string? moduleId)
    {
        var key = Key(moduleId);
        return key.Length == 0 ? null : RotStack.RotModules.FirstOrDefault(id => Key(id) == key);
    }

    private static string Key(string? id) =>
        new string((id ?? "").Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();

    /// <summary>Why this module cannot be an extra mod on a Realm of Thrones setup, or null when it can.</summary>
    public static string? RefusalReason(string? moduleId)
    {
        var id = (moduleId ?? "").Trim();
        if (!id.Any(char.IsLetterOrDigit) || id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || id.Contains('*') || id.Contains('"'))
            return "That module id cannot be loaded.";
        if (IsCore(id))
            return id + " is part of the Realm of Thrones core. It is always loaded.";
        if (NotForCampaign.Contains(id))
            return id + " is not a campaign module.";
        if (OfficialModules.Contains(id))
            return id + " comes with Bannerlord. It cannot be added as an extra mod.";
        if (RotStack.OtherCoopModuleIds.Contains(id, StringComparer.OrdinalIgnoreCase))
            return id + " is an old Coop copy. Two Coop copies crash the game.";
        var overhaul = OtherOverhaul(id);
        if (overhaul != null)
            return id + " is part of " + overhaul + ", another overhaul. It does not load together with Realm of Thrones.";
        if (OtherTools(id) is { } tools)
            return id + " is part of " + tools + ", which has tools of its own. It does not load together with Realm of Thrones.";
        if (SecondCopyOf(id) is { } rot)
            return id + " is a second copy of " + rot + ". The Realm of Thrones core already loads it.";
        if (id.StartsWith("DedicatedServer.", StringComparison.OrdinalIgnoreCase))
            return id + " is a server or launcher module. It is not added by hand.";
        return null;
    }

    /// <summary>Puts every extra in the one place, keeping their order. Each module id is trimmed, and a
    /// later extra whose id repeats an earlier one (case ignored) is dropped, so a hand edited settings
    /// file cannot list a mod twice.</summary>
    public static void Normalize(TaomSetup setup)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var kept = new List<TaomExtraMod>();
        foreach (var mod in setup.Extras)
        {
            if (mod == null)
                continue;
            mod.ModuleId = (mod.ModuleId ?? "").Trim();
            if (!seen.Add(mod.ModuleId))
                continue;
            mod.Slot = SlotAfterRot;
            kept.Add(mod);
        }

        setup.Extras = kept;
    }

    /// <summary>The module order the client launches with: the core in its sealed order with the
    /// enabled extras before the compat module. The locked setup gives exactly RotStack.ClientModules.</summary>
    public static IReadOnlyList<string> ClientOrder(TaomSetup? setup) =>
        Build(setup, RotStack.ClientModules);

    /// <summary>The "modules" list of server-config.json for this setup: the server core with the same
    /// extras before the compat module. The locked setup gives RotStack.ServerModules. The server loads
    /// RotStack.ServerLoadsFirst before it and DedicatedServer.Windows after it.</summary>
    public static IReadOnlyList<string> ServerOrder(TaomSetup? setup) =>
        Build(setup, RotStack.ServerModules);

    /// <summary>The rows the server tool's Mods page shows, in the order ServerOrder lists them:
    /// the server core with the extras ServerOrder uses before the compat module, the ones turned off
    /// too, so the host can turn them back on. A refused or repeated extra is left out, as ServerOrder
    /// leaves it out. ExtraIndex points into setup.Extras, or is -1 for a core module.</summary>
    public static IReadOnlyList<TaomOrderRow> ServerRows(TaomSetup setup)
    {
        var rows = CoreBeforeExtras(RotStack.ServerModules).Select(id => new TaomOrderRow(id, -1)).ToList();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < setup.Extras.Count; index++)
        {
            var id = setup.Extras[index].ModuleId.Trim();
            if (RefusalReason(id) == null && seen.Add(id))
                rows.Add(new TaomOrderRow(id, index));
        }

        rows.Add(new TaomOrderRow(RotStack.CompatModuleId, -1));
        return rows;
    }

    /// <summary>The client order plus the client add-ons (the join password module) last.</summary>
    public static IReadOnlyList<string> LaunchModules(TaomSetup? setup, bool joinPassword)
    {
        var order = ClientOrder(setup).ToList();
        if (joinPassword)
            order.Add(ClientAddOns.JoinPasswordId);
        return order;
    }

    private static IReadOnlyList<string> Build(TaomSetup? setup, IReadOnlyList<string> core)
    {
        var order = CoreBeforeExtras(core).ToList();
        order.AddRange(Usable(setup).Select(mod => mod.ModuleId.Trim()));
        order.Add(RotStack.CompatModuleId);
        var problem = CoreProblem(order, core);
        if (problem != null)
            throw new InvalidOperationException(problem);
        return order;
    }

    // The locked setup has no extras (RotSetupBook.Ensure keeps it that way). A refused id saved as an
    // extra, such as a core module, is skipped here.
    private static List<TaomExtraMod> Usable(TaomSetup? setup)
    {
        var list = new List<TaomExtraMod>();
        if (setup == null)
            return list;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var mod in setup.Extras)
        {
            if (!mod.Enabled || RefusalReason(mod.ModuleId) != null || !seen.Add(mod.ModuleId.Trim()))
                continue;
            list.Add(mod);
        }

        return list;
    }

    /// <summary>Null when the order keeps the core whole: the core first, unbroken and in its order,
    /// the compat module last, no module twice. Otherwise a plain sentence that says what is wrong.</summary>
    public static string? CoreProblem(IReadOnlyList<string> order, IReadOnlyList<string>? core = null)
    {
        core ??= RotStack.ClientModules;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in order)
        {
            if (!seen.Add(id))
                return id + " is in the module list twice.";
        }

        var first = CoreBeforeExtras(core);
        for (var i = 0; i < first.Count; i++)
        {
            if (i >= order.Count || !order[i].Equals(first[i], StringComparison.OrdinalIgnoreCase))
                return "The Realm of Thrones core has to load first, in its order.";
        }

        if (order.Count == 0 || !order[^1].Equals(RotStack.CompatModuleId, StringComparison.OrdinalIgnoreCase))
            return "Realm of Thrones Coop Compat has to load last.";
        return null;
    }

    // ----- Editing. A locked setup refuses every change. -----

    /// <summary>Adds an extra mod at the end of the extras. Returns null when it was added, or why not.</summary>
    public static string? Add(TaomSetup setup, string name, string moduleId, string? workshopId)
    {
        Unlocked(setup);
        var id = (moduleId ?? "").Trim();
        var refusal = RefusalReason(id);
        if (refusal != null)
            return refusal;
        if (setup.Extras.Any(mod => mod.ModuleId.Equals(id, StringComparison.OrdinalIgnoreCase)))
            return (string.IsNullOrWhiteSpace(name) ? id : name.Trim()) + " is already on this setup.";
        setup.Extras.Add(new TaomExtraMod
        {
            Name = string.IsNullOrWhiteSpace(name) ? id : name.Trim(),
            ModuleId = id,
            WorkshopId = SharedModList.IsWorkshopId(workshopId) ? workshopId!.Trim() : "0",
            Enabled = true,
            Slot = SlotAfterRot
        });
        return null;
    }

    public static bool Remove(TaomSetup setup, string moduleId)
    {
        Unlocked(setup);
        return setup.Extras.RemoveAll(mod => mod.ModuleId.Equals(moduleId, StringComparison.OrdinalIgnoreCase)) > 0;
    }

    public static void SetEnabled(TaomSetup setup, string moduleId, bool enabled)
    {
        Unlocked(setup);
        Find(setup, moduleId).Enabled = enabled;
    }

    /// <summary>Moves an extra one place up (-1) or down (+1) among the extras. Returns false when it is
    /// already at the top or the bottom: an extra never passes ROT-Dragon or the compat module.</summary>
    public static bool Move(TaomSetup setup, string moduleId, int delta)
    {
        Unlocked(setup);
        Normalize(setup);
        if (delta == 0)
            return false;
        var mod = Find(setup, moduleId);
        var index = setup.Extras.IndexOf(mod);
        var neighbour = index + Math.Sign(delta);
        if (neighbour < 0 || neighbour >= setup.Extras.Count)
            return false;
        setup.Extras.RemoveAt(index);
        setup.Extras.Insert(neighbour, mod);
        return true;
    }

    /// <summary>Puts an extra at a place among the extras (used by drag and drop and Auto sort). The
    /// index counts the other extras; a big index means the end. Any slot is read as the one place.</summary>
    public static void MoveTo(TaomSetup setup, string moduleId, int index)
    {
        Unlocked(setup);
        var mod = Find(setup, moduleId);
        setup.Extras.Remove(mod);
        mod.Slot = SlotAfterRot;
        Normalize(setup);
        setup.Extras.Insert(Math.Clamp(index, 0, setup.Extras.Count), mod);
    }

    private static TaomExtraMod Find(TaomSetup setup, string moduleId) =>
        setup.Extras.FirstOrDefault(mod => mod.ModuleId.Equals(moduleId, StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidOperationException(moduleId + " is not on this setup.");

    private static void Unlocked(TaomSetup setup)
    {
        if (setup.Locked)
            throw new InvalidOperationException(setup.Name + " is locked. Click New setup or Copy to make a setup you can add mods to.");
    }
}
