using BannerlordCoopLauncher.Core.Modules;
using TaomCoop.Core;

namespace BurningEmpires.Core;

/// <summary>
/// The Burning Empires tools' side of the per PC results (ModResults): which rows get the Unknown,
/// not tested badge, and the results the server load test and Find the crashing mod write.
///
/// mod-results.json keeps one result per module id and version for every Coodsz tool on the PC, so
/// a mod that passed with TAOM would show as passed with Burning Empires too. The Burning Empires
/// tools write and read their results under a game key instead: "be:" before the module id (a
/// module id never has a colon). The other tools never match those ids, and these never match theirs.
/// </summary>
public static class BeModResults
{
    public const string GameKey = "be:";

    /// <summary>The id a Burning Empires result is kept under in mod-results.json.</summary>
    public static string Key(string moduleId) => GameKey + (moduleId ?? "").Trim();

    /// <summary>The module id of a kept id, or null when it is not a Burning Empires result.</summary>
    public static string? ModuleIdOf(string? key) =>
        key != null && key.StartsWith(GameKey, StringComparison.OrdinalIgnoreCase) ? key[GameKey.Length..] : null;

    /// <summary>The same results under the Burning Empires game key.</summary>
    public static IReadOnlyList<ModResult> Keyed(IEnumerable<ModResult> results) =>
        results.Where(result => result.Id.Trim().Length > 0).Select(result => new ModResult
        {
            Id = ModuleIdOf(result.Id) != null ? result.Id.Trim() : Key(result.Id),
            Version = result.Version,
            Result = result.Result,
            Where = result.Where,
            Date = result.Date,
            Reason = result.Reason
        }).ToList();

    /// <summary>Records Burning Empires results. Never throws for a file problem.</summary>
    public static bool Record(IEnumerable<ModResult> results, string? path = null) => ModResults.Record(Keyed(results), path);

    public static bool Record(ModResult result, string? path = null) => Record(new[] { result }, path);

    /// <summary>Adds the server load test results this PC's file does not have yet (or has older).</summary>
    public static bool ImportServerTests(IEnumerable<ModResult> serverResults, string? path = null) =>
        ModResults.ImportServerTests(Keyed(serverResults), path);

    /// <summary>The pill for a mod from this PC's Burning Empires results only.</summary>
    public static ModResultPill? Pill(string? moduleId, string? version, IReadOnlyList<ModResult>? results = null, string? path = null)
    {
        var id = (moduleId ?? "").Trim();
        if (id.Length == 0)
            return null;
        return ModResults.Pill(Key(id), version, results ?? ModResults.Load(path));
    }

    /// <summary>True for a mod that should show a badge: not an official module, Coop, a
    /// DedicatedServer.* module, or a module of the Burning Empires core (Elephants included).</summary>
    public static bool IsExtraMod(string? moduleId)
    {
        var id = (moduleId ?? "").Trim();
        return UnknownModBadge.Applies(id) && !BeLoadOrder.IsBeModule(id);
    }

    /// <summary>True when the Burning Empires list has nothing to say about the mod, so the grey
    /// Unknown, not tested badge shows.</summary>
    public static bool ShowsUnknown(BeCompatList list, string? moduleId, string? workshopId, string? name)
    {
        if (!IsExtraMod(moduleId))
            return false;
        var entry = list.Find(moduleId, workshopId, name);
        return entry == null || BeCompatList.BadgeText(entry.Kind) == null;
    }

    /// <summary>A "Test mods on the server" result as this PC's result: passed or failed on your server.
    /// Record it with BeModResults.Record so it gets the game key.</summary>
    public static ModResult FromServerTest(ModTestResult result, string? date = null) => TaomModResults.FromServerTest(result, date);

    /// <summary>The mods turned on in a Find the crashing mod round that reached the main menu.</summary>
    public static IReadOnlyList<ModResult> CrashRoundPassed(IEnumerable<(string Id, string? Version)> turnedOn, string? date = null) =>
        turnedOn.Where(mod => IsExtraMod(mod.Id)).Select(mod => new ModResult
        {
            Id = mod.Id,
            Version = mod.Version ?? "",
            Result = ModResult.Worked,
            Where = ModResult.ThisPc,
            Date = date ?? ModResults.Now(),
            Reason = "Reached the main menu in a Find the crashing mod round."
        }).ToList();

    /// <summary>The mod Find the crashing mod blamed.</summary>
    public static ModResult CrashFound(string id, string? version, string? date = null) => TaomModResults.CrashFound(id, version, date);
}
