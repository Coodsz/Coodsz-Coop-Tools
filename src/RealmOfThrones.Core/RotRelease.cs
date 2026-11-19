using BannerlordCoopLauncher.Core.Updates;

namespace RealmOfThrones.Core;

/// <summary>
/// Where the Realm of Thrones tools publish releases and send reports. A value that is empty or starts with
/// PLACEHOLDER-ROT turns that part off, and the update check or the log and report sending say they are not set up.
/// </summary>
public static class RotRelease
{
    public const string PlaceholderPrefix = "PLACEHOLDER-ROT";

    /// <summary>The public release repo for the update check (owner/name), from ReleaseNotice.RotRepo.</summary>
    public const string Repo = ReleaseNotice.RotRepo;

    /// <summary>The private crash report repo (owner/name), the default of the report worker's ROT_CRASH_REPO (crashRepo in crash.js).</summary>
    public const string CrashRepo = "Coodsz/Coodsz-RealmOfThrones-Crash-Reports";

    /// <summary>The tool id the report service knows these tools by (TOOLS and CRASH_TOOLS of the report worker).</summary>
    public const string ToolId = "rot";

    /// <summary>The version of both tools, from ReleaseNotice.RotVersion; the csproj Version says the same.</summary>
    public const string Version = ReleaseNotice.RotVersion;

    public static bool IsPlaceholder(string? value) =>
        string.IsNullOrWhiteSpace(value) || value.StartsWith(PlaceholderPrefix, StringComparison.Ordinal);

    /// <summary>False while the release repo is a placeholder: no update check runs.</summary>
    public static bool UpdatesReady => !IsPlaceholder(Repo);

    /// <summary>False while the crash repo is a placeholder: no log, crash or mod report is sent.</summary>
    public static bool ReportsReady => !IsPlaceholder(CrashRepo);

    public const string UpdatesNotSetUp = "Update checks are not set up yet for the Realm of Thrones tools.";

    public const string ReportsNotSetUp = "Sending logs and reports is not set up yet for the Realm of Thrones tools.";
}
