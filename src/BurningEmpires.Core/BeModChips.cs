using BannerlordCoopLauncher.Core.Modules;
using TaomCoop.Core;

namespace BurningEmpires.Core;

/// <summary>The player counts the tally (.github/scripts/tally-taom-compat.mjs --list be) writes on a
/// Burning Empires list entry or a "community" entry.</summary>
public sealed class BeModCounts
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string WorkshopId { get; set; } = "";
    public int Works { get; set; }
    public int Broken { get; set; }
    public int ClientOnly { get; set; }

    internal static BeModCounts Clean(BeModCounts counts)
    {
        counts.Id = (counts.Id ?? "").Trim();
        counts.Name = (counts.Name ?? "").Trim();
        counts.WorkshopId = (counts.WorkshopId ?? "").Trim();
        return counts;
    }
}

/// <summary>The status chip of a mod row. Kind picks the color: Works green, Risky gold, Reported blue,
/// Broken red, Unknown grey. WorksChip and BrokenChip are the green and red count chips, or null.</summary>
public sealed record BeStatusChip(string Kind, string Text, string Tip)
{
    public string? WorksChip { get; init; }
    public string? BrokenChip { get; init; }
}

/// <summary>The status column of a mod row in the Burning Empires client, the same chips as Coodsz Coop and
/// Friends: the Ready pill, the list status chip, the count chips (then ModderLords' verdict, this PC's own
/// result and the Report link, which the window adds). Everything comes from the Burning Empires list and the
/// reports sent with tool "be", never from the Coop and Friends list.</summary>
public static class BeModChips
{
    // The chip kinds (colors, not screen text).
    public const string Works = nameof(Works);
    public const string Risky = nameof(Risky);
    public const string Reported = nameof(Reported);
    public const string Broken = nameof(Broken);
    public const string Unknown = nameof(Unknown);

    /// <summary>The chip color of a list tag.</summary>
    public static string KindOf(CompatTag tag) => tag switch
    {
        CompatTag.Safe or CompatTag.Everyone => Works,
        CompatTag.Broken => Broken,
        CompatTag.Reported or CompatTag.Loads => Reported,
        CompatTag.Risky => Risky,
        _ => Unknown
    };

    /// <summary>ModderLords' Tested chip of a mod row, or null for the Burning Empires core: his records test
    /// plain Coop, not the Burning Empires stack, so the core rows only show player reports (as on the TAOM core).</summary>
    public static CompatTestedChip? RecordsChip(CompatRecordDatabase records, string? moduleId, string? version) =>
        BeModResults.IsExtraMod(moduleId) ? CompatRecords.Chip(records.Find(moduleId), version) : null;

    /// <summary>The status chip of an extra mod, or null for the Burning Empires core, Coop and the official
    /// modules. A mod the list does not know is Unknown, not tested, or Unknown once players reported it (the
    /// count chips then say what they found). reportTip adds the line that says a click opens the report.</summary>
    public static BeStatusChip? Status(BeCompatList list, string? moduleId, string? workshopId, string? name, bool reportTip,
        Func<string, string>? translate = null)
    {
        if (!BeModResults.IsExtraMod(moduleId))
            return null;
        var entry = list.Find(moduleId, workshopId, name);
        var badge = entry == null ? null : BeCompatList.BadgeText(entry.Kind);
        var counts = list.CountsFor(moduleId, workshopId, name);
        var lines = new List<string>();
        string kind;
        string text;
        if (entry != null && badge != null)
        {
            kind = KindOf(entry.Kind);
            text = badge;
            lines.Add(BeCompatList.Hover(entry, translate));
        }
        else
        {
            kind = Unknown;
            text = counts == null ? UnknownModBadge.Text : "Unknown";
            if (counts == null)
                lines.Add(UnknownModBadge.Tip);
        }

        if (counts != null)
            lines.Add(ModReports.CountsLine(counts.Works, counts.Broken));
        if (reportTip)
            lines.Add(ModReports.ClickTip);
        return new BeStatusChip(kind, text, string.Join("\n", lines))
        {
            WorksChip = ModReports.WorksChip(counts?.Works ?? 0),
            BrokenChip = ModReports.BrokenChip(counts?.Broken ?? 0)
        };
    }

    /// <summary>The status pill of an extra mod row: Off, Ready or Missing, with its color kind (Off grey,
    /// Ready green, Missing red), the same words and colors as Coop and Friends.</summary>
    public static (string Text, string Kind) ExtraMark(bool enabled, bool installed) =>
        !enabled ? ("Off", "Off") : installed ? ("Ready", "Ready") : ("Missing", "Missing");

    /// <summary>The status pill of a core row: Missing, Ready or Wrong version (red, as in every tool: it blocks joining).</summary>
    public static (string Text, string Kind) CoreMark(bool found, bool rightVersion) =>
        !found ? ("Missing", "Missing") : rightVersion ? ("Ready", "Ready") : (ModChips.WrongVersion, ModChips.WrongVersion);

    /// <summary>The tool every Burning Empires report carries, so the worker files it in Coodsz-BurningEmpires.</summary>
    public const string Tool = "be";

    /// <summary>The mods the server loads for a setup, in order, with their versions: every core module and the
    /// extras that load (loads, the staged ones), in the order of the rows on the mods page. shown is the setup
    /// as the page shows it (the working copy with the ids the mods on this PC really have).</summary>
    public static IReadOnlyList<(string Id, string? Version)> ServerReportOrder(TaomSetup shown, Func<string, bool> loads, Func<string, string> versionOf) =>
        BeLoadOrder.ServerRows(shown)
            .Where(row => row.IsCore || (shown.Extras[row.ExtraIndex].Enabled && loads(row.ModuleId)))
            .Select(row => (row.ModuleId, (string?)versionOf(row.ModuleId)))
            .ToList();

    /// <summary>The one click report of one mod row of the server tool: tool be, the mod, and the server's mods in order.</summary>
    public static BeRowReport RowReport(string id, string name, string version, IReadOnlyList<(string Id, string? Version)> serverOrder) =>
        new(Tool, id, name, version, serverOrder);

    /// <summary>The folder Start server copies a core module from (BeServerPath.StageCore): the BUTR Workshop
    /// items, the compat module the tool ships, and Burning Empires and Elephants in the game's Modules folder.
    /// For MCM it is BeServerPath.BestMcm; when that is null (no copy the server loads on this PC, so Start keeps
    /// the server's own copy or refuses), it is still the game's Modules folder path, which may not exist, so the
    /// row reads no version or the old one there. Null for other modules, and for a game folder module when the
    /// game was not found. The mod scan leaves the BUTR modules and the shipped compat module out.</summary>
    public static string? CoreFolder(string id, BeCoreSources sources)
    {
        var butr = BeStack.Butr.FirstOrDefault(mod => mod.ModuleId.Equals(id, StringComparison.OrdinalIgnoreCase));
        if (butr is { FromNexus: true })
            return BeServerPath.BestMcm(sources.GameRoot, sources.SteamLibraries) ?? (sources.GameRoot.Length == 0 ? null : BeServerPath.McmSource(sources.GameRoot));
        if (butr != null)
            return BeServerPath.WorkshopItem(sources.SteamLibraries, butr.WorkshopId);
        if (id.Equals(BeStack.CompatModuleId, StringComparison.OrdinalIgnoreCase))
            return sources.CompatModule;
        if (id.Equals(BeStack.BeModuleId, StringComparison.OrdinalIgnoreCase) || id.Equals(BeStack.ElephantsModuleId, StringComparison.OrdinalIgnoreCase))
            return sources.GameRoot.Length == 0 ? null : Path.Combine(sources.GameRoot, "Modules", id);
        return null;
    }
}

/// <summary>A server tool mod row report (BeModChips.RowReport); the window turns it into the report window's request.</summary>
public sealed record BeRowReport(string Tool, string Id, string Name, string Version, IReadOnlyList<(string Id, string? Version)> ServerOrder);
