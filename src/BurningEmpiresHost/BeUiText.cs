using BurningEmpires.Core;

namespace BurningEmpires.Ui;

/// <summary>
/// Player text the Burning Empires server tool and client both show. The client links this file
/// in. It has no WPF in it, so the tests can call it.
/// </summary>
public static class BeUiText
{
    public const string StillSaving = "Still saving. Large worlds take a while.";

    public const string InstallHint = "Click Install or repair Burning Empires in step 3.";

    /// <summary>The Yes/No question before downloads the tool found by itself are unpacked: which
    /// files (BeArchive.Describe), how big, and that it takes a while. Problem is what is wrong now,
    /// or null when Burning Empires is ready and this would only repair it. A file someone else put
    /// in Downloads must not get in by itself, so the player is told to say Yes only to their own.</summary>
    public static string DownloadQuestion(string? problem, string beVersion, IReadOnlyList<BeFoundArchive> archives)
    {
        var ask = problem == null
            ? "Burning Empires " + beVersion + " is ready. Put its files in again from these downloads to repair a damaged file?"
            : problem + " Install Burning Empires from these downloads?";
        return ask + "\n\n"
            + string.Join("\n", archives.Select(archive => archive.Path + " (" + archive.SizeText + ")"))
            + "\n\nThis can take several minutes. Your Burning Empires save data is not touched."
            + "\nClick Yes only if you downloaded them yourself from Nexus Mods.";
    }

    /// <summary>Install or repair stops here when Burning Empires files in Modules were made or changed
    /// in the last minute (BeArchive.ChangedRecently): a copy by hand that is still running.</summary>
    public const string CopyStillRunning =
        "Burning Empires files in the game's Modules folder changed in the last minute. If you are still copying them there, wait for the copy to finish, then click Install or repair Burning Empires again.";

    /// <summary>The log line before the file picker. hotfixOnly is true when Burning Empires and
    /// Elephants are already in Modules (BeArchive.NeedsMainFile is false, a copy by hand), so only
    /// the hotfix is asked for.</summary>
    public static string PickLine(string problem, bool hotfixOnly, bool hotfixFound, string beVersion)
    {
        if (hotfixOnly)
            return problem + " Burning Empires is already in the game's Modules folder, so only the " + beVersion + " hotfix is needed. Pick the hotfix you downloaded from Nexus Mods.";
        return hotfixFound
            ? problem + " The Burning Empires main file was not found. Pick the Burning Empires main file you downloaded from Nexus Mods."
            : problem + " Pick the Burning Empires main file you downloaded from Nexus Mods.";
    }

    public static string PickTitle(bool hotfixOnly) =>
        hotfixOnly ? "Pick the Burning Empires hotfix" : "Pick the Burning Empires download";

    /// <summary>The log line when the file picker is closed without a file: nothing more happens.</summary>
    public static string PickCancelled(bool hotfixOnly, string beVersion) => hotfixOnly
        ? "Burning Empires was not installed. Download the " + beVersion + " hotfix from Nexus Mods, then click Install or repair Burning Empires again."
        : "Burning Empires was not installed. Download the main file and the " + beVersion + " hotfix from Nexus Mods, then click Install or repair Burning Empires again.";

    /// <summary>The Yes/No question before the Nexus Mods page opens in the browser. Reason is why
    /// (what is wrong now, one line per message), so a browser never opens without the player knowing
    /// why. The question is a line of its own, so the translator takes each line by itself.</summary>
    public static string NexusQuestion(string reason, bool hotfixOnly, string beVersion)
    {
        var ask = hotfixOnly
            ? "Open Burning Empires on Nexus Mods in your browser to download the " + beVersion + " hotfix?"
            : "Open Burning Empires on Nexus Mods in your browser to download the main file and the " + beVersion + " hotfix?";
        return reason + "\n\n" + ask;
    }

    /// <summary>The log line when the player says No to opening Nexus Mods.</summary>
    public static string NexusNotOpened(string nexusPage, bool hotfixOnly, string beVersion) => hotfixOnly
        ? "Burning Empires is on Nexus Mods at " + nexusPage + ". Download the " + beVersion + " hotfix, then click Install or repair Burning Empires again."
        : "Burning Empires is on Nexus Mods at " + nexusPage + ". Download the main file and the " + beVersion + " hotfix, then click Install or repair Burning Empires again.";

    /// <summary>The log line after the Nexus Mods page opened.</summary>
    public static string NexusOpened(bool hotfixOnly, string beVersion) => hotfixOnly
        ? "Opened Burning Empires on Nexus Mods. Download the " + beVersion + " hotfix, then click Install or repair Burning Empires again."
        : "Opened Burning Empires on Nexus Mods. Download the main file and the " + beVersion + " hotfix, then click Install or repair Burning Empires again.";

    public static string BeNotReady(string beVersion) =>
        "Burning Empires " + beVersion + " is not ready on this PC. " + InstallHint;

    /// <summary>The status line after Prepare server or Start server finds problems: only the first,
    /// the rest are in the log. A Burning Empires line from the server tool goes first when given.</summary>
    public static string FirstProblem(IReadOnlyList<string> problems, string? beLine = null)
    {
        if (problems.Count == 0)
            return beLine ?? "";
        var first = beLine ?? problems[0];
        var more = problems.Count - 1;
        if (more <= 0)
            return first;
        return first + " " + (more == 1 ? "1 more problem is" : more + " more problems are") + " in the log. Click Open server logs to see them.";
    }

    /// <summary>The orange line near Connect when the host's mod list has other versions. Null when none differ.</summary>
    public static string? DriftNote(int differences) => differences switch
    {
        <= 0 => null,
        1 => "1 mod version differs from the host. See the log.",
        _ => differences + " mod versions differ from the host. See the log."
    };
}
