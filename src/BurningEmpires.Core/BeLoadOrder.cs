using BannerlordCoopLauncher.Core.Launch;
using TaomCoop.Core;

namespace BurningEmpires.Core;

/// <summary>
/// The locked Burning Empires core and the rules for extra mods around it.
///
/// The core is BeStack.ClientModules (BeStack.ServerModules on the host) in its sealed order. It is
/// split in two blocks that always stay whole:
///   game block: Harmony, ButterLib, UIExtenderEx, MCM, Native, SandBoxCore, Sandbox, StoryMode,
///     CustomBattle (on the server DedicatedServer.Windows after Native, and no StoryMode or CustomBattle)
///   Burning Empires block: Coop, BurningEmpires.CoopCompat, BurningEmpires, BurningEmpires_Elephants
/// Extras load in one of two places: between the game block and Coop (before Coop), or after
/// Burning Empires Elephants (the default). Nothing goes inside a block, so the compat module always
/// stays right before Burning Empires and Elephants right after it. Client add-ons (DedicatedServer.*)
/// still load last, after every extra.
///
/// The setups use the TAOM setup model. Its slots mean: ExtraSlot.BeforeTaom is "before Coop" and
/// ExtraSlot.AfterTaom is "after Burning Empires". ExtraSlot.BeforeCoop has no Burning Empires place
/// and is read as after Burning Empires.
/// </summary>
public static class BeLoadOrder
{
    /// <summary>Between the game block and Coop.</summary>
    public const ExtraSlot SlotBeforeCoop = ExtraSlot.BeforeTaom;

    /// <summary>After Burning Empires, the default.</summary>
    public const ExtraSlot SlotAfterBe = ExtraSlot.AfterTaom;

    public const string FirstBeModule = BeStack.CoopModuleId;

    public static IReadOnlyList<string> GameBlock { get; } =
        BeStack.ClientModules.TakeWhile(id => id != FirstBeModule).ToArray();

    public static IReadOnlyList<string> BeBlock { get; } =
        BeStack.ClientModules.SkipWhile(id => id != FirstBeModule).ToArray();

    // Mods players found work only at the very bottom of the load order. Auto sort puts them last.
    public static readonly IReadOnlySet<string> LoadsLast = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "RTSCamera", "RTSCamera.CommandSystem" };

    /// <summary>0 for most mods; 1 for RTS Camera and its Command System, which work only at the very bottom.</summary>
    public static int LastRank(string id) => LoadsLast.Contains(id) ? 1 : 0;

    private static readonly ExtraSlot[] SlotOrder = [SlotBeforeCoop, SlotAfterBe];

    private static readonly HashSet<string> NotForCampaign = new(StringComparer.OrdinalIgnoreCase)
    {
        "Multiplayer",
        "DedicatedCustomServer"
    };

    /// <summary>The TAOM core modules. TAOM is another overhaul and never loads with Burning Empires.</summary>
    public static IReadOnlyCollection<string> TaomCoreIds { get; } =
        TaomStack.ContentVersions.Keys.Append(TaomStack.CompatModuleId).ToHashSet(StringComparer.OrdinalIgnoreCase);

    public static bool IsTaomCore(string? moduleId) =>
        moduleId != null && TaomCoreIds.Contains(moduleId.Trim());

    /// <summary>True for the modules of the Burning Empires core: the BUTR modules, the game modules,
    /// Coop, the compat module, Burning Empires and Burning Empires Elephants.</summary>
    public static bool IsCore(string? moduleId) =>
        !string.IsNullOrWhiteSpace(moduleId)
        && (BeStack.ClientModules.Contains(moduleId.Trim(), StringComparer.OrdinalIgnoreCase)
            || BeStack.ServerModules.Contains(moduleId.Trim(), StringComparer.OrdinalIgnoreCase));

    public static bool IsElephants(string? moduleId) =>
        moduleId != null && moduleId.Trim().Equals(BeStack.ElephantsModuleId, StringComparison.OrdinalIgnoreCase);

    /// <summary>True for the core: the modules the server stages itself and never strips client code from.</summary>
    public static bool IsBeModule(string? moduleId) => IsCore(moduleId);

    /// <summary>Why this module cannot be an extra mod on a Burning Empires setup, or null when it can.</summary>
    public static string? RefusalReason(string? moduleId)
    {
        var id = (moduleId ?? "").Trim();
        if (id.Length == 0 || id is "." or ".." || id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || id.Contains('*') || id.Contains('"'))
            return "That module id cannot be loaded.";
        if (IsCore(id))
            return id + " is part of the Burning Empires core. It is always loaded.";
        if (id.Equals(BeStack.LegacyCoopModuleId, StringComparison.OrdinalIgnoreCase))
            return id + " is an old Coop copy. Two Coop copies crash the game.";
        if (IsTaomCore(id))
            return id + " is part of TAOM, another overhaul. It does not load together with Burning Empires.";
        if (id.StartsWith("DedicatedServer.", StringComparison.OrdinalIgnoreCase))
            return id + " is a server or launcher module. It is not added by hand.";
        if (NotForCampaign.Contains(id))
            return id + " is not a campaign module.";
        return null;
    }

    /// <summary>Puts the extras in slot order (before Coop, after Burning Empires), keeping the order
    /// inside each slot. An Elephants extra from a setup saved before Elephants joined the core is
    /// dropped, because the core loads it now. A mod listed twice (the same id, or ids that differ only
    /// in case or spaces) keeps the copy ExtrasById picks.</summary>
    public static void Normalize(TaomSetup setup)
    {
        setup.Extras.RemoveAll(mod => IsElephants(mod.ModuleId));
        var kept = new HashSet<TaomExtraMod>(ExtrasById(setup).Values, ReferenceEqualityComparer.Instance);
        setup.Extras.RemoveAll(mod => !kept.Contains(mod));
        foreach (var mod in setup.Extras)
            mod.Slot = SlotOf(mod);

        setup.Extras = SlotOrder.SelectMany(slot => setup.Extras.Where(mod => mod.Slot == slot)).ToList();
    }

    /// <summary>One extra per module id (trimmed, any case): the first copy that is turned on, as the
    /// load order uses, or the first copy when none is. Two copies of one id used to crash the code
    /// that keyed a dictionary on the id (the shared mod list, the mod checks, Auto sort).</summary>
    public static Dictionary<string, TaomExtraMod> ExtrasById(TaomSetup setup)
    {
        var byId = new Dictionary<string, TaomExtraMod>(StringComparer.OrdinalIgnoreCase);
        foreach (var mod in setup.Extras)
        {
            var id = mod.ModuleId.Trim();
            if (!byId.TryGetValue(id, out var had) || (!had.Enabled && mod.Enabled))
                byId[id] = mod;
        }

        return byId;
    }

    private static ExtraSlot SlotOf(TaomExtraMod mod) =>
        mod.Slot == SlotBeforeCoop ? SlotBeforeCoop : SlotAfterBe;

    /// <summary>The module order the client launches with: the core in its sealed order with the
    /// enabled extras in their slots. The locked Burning Empires setup gives exactly BeStack.ClientModules.</summary>
    public static IReadOnlyList<string> ClientOrder(TaomSetup? setup) =>
        Build(setup, BeStack.ClientModules);

    /// <summary>The order the dedicated server loads for this setup: the server core with the same
    /// extras. The locked Burning Empires setup gives BeStack.ServerModules.</summary>
    public static IReadOnlyList<string> ServerOrder(TaomSetup? setup) =>
        Build(setup, BeStack.ServerModules);

    /// <summary>The rows the server tool's Mods page shows, in the order ServerOrder loads them:
    /// the server core with every extra in its slot, the ones turned off too, so the host can turn
    /// them back on. ExtraIndex points into setup.Extras, or is -1 for a core module.</summary>
    public static IReadOnlyList<TaomOrderRow> ServerRows(TaomSetup setup)
    {
        var core = BeStack.ServerModules;
        var rows = new List<TaomOrderRow>();
        IEnumerable<TaomOrderRow> Core(IEnumerable<string> ids) => ids.Select(id => new TaomOrderRow(id, -1));
        IEnumerable<TaomOrderRow> In(ExtraSlot slot) => setup.Extras
            .Select((mod, index) => (mod, index))
            .Where(pair => SlotOf(pair.mod) == slot && !IsElephants(pair.mod.ModuleId))
            .Select(pair => new TaomOrderRow(pair.mod.ModuleId.Trim(), pair.index));
        rows.AddRange(Core(core.TakeWhile(id => id != FirstBeModule)));
        rows.AddRange(In(SlotBeforeCoop));
        rows.AddRange(Core(core.SkipWhile(id => id != FirstBeModule)));
        rows.AddRange(In(SlotAfterBe));
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
        var game = core.TakeWhile(id => id != FirstBeModule).ToList();
        var block = core.SkipWhile(id => id != FirstBeModule).ToList();
        var order = new List<string>(game);
        var extras = Usable(setup);
        IEnumerable<string> In(ExtraSlot slot) => extras.Where(mod => mod.Slot == slot).Select(mod => mod.ModuleId.Trim());
        order.AddRange(In(SlotBeforeCoop));
        order.AddRange(block);
        order.AddRange(In(SlotAfterBe));
        var problem = CoreProblem(order, core);
        if (problem != null)
            throw new InvalidOperationException(problem);
        return order;
    }

    // The locked setup has no extras (BeSetupBook.Ensure keeps it that way). A core module saved as an
    // extra, such as Elephants from a setup made before it joined the core, is skipped here.
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
            var copy = mod.Copy();
            copy.Slot = SlotOf(copy);
            list.Add(copy);
        }

        return list.OrderBy(mod => mod.Slot == SlotBeforeCoop ? 0 : 1).ToList();
    }

    /// <summary>Null when the order keeps the core whole: the game block first and unbroken, then
    /// Coop, the compat module, Burning Empires and Elephants together after it, no module twice.
    /// Otherwise a plain sentence that says what is wrong.</summary>
    public static string? CoreProblem(IReadOnlyList<string> order, IReadOnlyList<string>? core = null)
    {
        core ??= BeStack.ClientModules;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in order)
        {
            if (!seen.Add(id))
                return id + " is in the module list twice.";
        }

        var game = core.TakeWhile(id => id != FirstBeModule).ToList();
        var block = core.SkipWhile(id => id != FirstBeModule).ToList();
        for (var i = 0; i < game.Count; i++)
        {
            if (i >= order.Count || !order[i].Equals(game[i], StringComparison.OrdinalIgnoreCase))
                return "Harmony, ButterLib, UIExtenderEx, MCM and the game modules have to load first, in the Burning Empires order.";
        }

        var list = order.ToList();
        var start = list.FindIndex(id => id.Equals(FirstBeModule, StringComparison.OrdinalIgnoreCase));
        if (start < 0)
            return "Coop is missing from the module list.";
        if (!list.Any(id => id.Equals(BeStack.BeModuleId, StringComparison.OrdinalIgnoreCase)))
            return "Burning Empires is missing from the module list.";
        if (!BeStack.CompatIsAdjacent(order))
            return "Burning Empires Coop Compat has to load right before Burning Empires.";
        for (var i = 0; i < block.Count; i++)
        {
            if (start + i >= order.Count || !order[start + i].Equals(block[i], StringComparison.OrdinalIgnoreCase))
            {
                return IsElephants(block[i])
                    ? "Burning Empires Elephants has to load right after Burning Empires."
                    : "Coop, Burning Empires Coop Compat and Burning Empires have to stay together, in this order.";
            }
        }

        return null;
    }

    // ----- Editing. A locked setup refuses every change. -----

    /// <summary>Adds an extra mod after Burning Empires. Returns null when it was added, or why not.</summary>
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
            WorkshopId = BannerlordCoopLauncher.Core.Modules.SharedModList.IsWorkshopId(workshopId) ? workshopId!.Trim() : "0",
            Enabled = true,
            Slot = SlotAfterBe
        });
        Normalize(setup);
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
        var mod = Find(setup, moduleId);
        mod.Enabled = enabled;
    }

    /// <summary>Moves an extra one place up (-1) or down (+1) in the load order. Moving up past the
    /// Burning Empires block puts it before Coop; moving down past it puts it after Burning Empires.
    /// Returns false when it is already at the top or the bottom.</summary>
    public static bool Move(TaomSetup setup, string moduleId, int delta)
    {
        Unlocked(setup);
        Normalize(setup);
        if (delta == 0)
            return false;
        var mod = Find(setup, moduleId);
        var index = setup.Extras.IndexOf(mod);
        var neighbour = index + Math.Sign(delta);
        if (neighbour >= 0 && neighbour < setup.Extras.Count && setup.Extras[neighbour].Slot == mod.Slot)
        {
            setup.Extras.RemoveAt(index);
            setup.Extras.Insert(neighbour, mod);
            return true;
        }

        var next = Array.IndexOf(SlotOrder, mod.Slot) + Math.Sign(delta);
        if (next < 0 || next >= SlotOrder.Length)
            return false;
        mod.Slot = SlotOrder[next];
        Normalize(setup);
        return true;
    }

    /// <summary>Puts an extra at a place in a slot (used by drag and drop and Auto sort). The
    /// index counts the other extras in that slot; a big index means the end of the slot.</summary>
    public static void MoveTo(TaomSetup setup, string moduleId, ExtraSlot slot, int index)
    {
        Unlocked(setup);
        var mod = Find(setup, moduleId);
        setup.Extras.Remove(mod);
        mod.Slot = slot;
        mod.Slot = SlotOf(mod);
        Normalize(setup);
        var slotStart = SlotOrder.TakeWhile(item => item != mod.Slot).Sum(item => setup.Extras.Count(extra => extra.Slot == item));
        var slotCount = setup.Extras.Count(item => item.Slot == mod.Slot);
        setup.Extras.Insert(slotStart + Math.Clamp(index, 0, slotCount), mod);
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
