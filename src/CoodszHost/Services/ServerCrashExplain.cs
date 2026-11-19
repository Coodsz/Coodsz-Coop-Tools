using System.IO;
using System.Text.RegularExpressions;
using BannerlordCoopLauncher.Core.Server;
using BannerlordCoopLauncher.Core.Support;

namespace CoodszHost.Services;

public static class ServerCrashExplain
{
    public readonly record struct ModName(string Id, string Name);

    public readonly record struct Logs(string? ServerLog, string? EngineLog);

    /// <summary>The server window and this run's coop server log one by one, as a crash report has them, so a
    /// pattern's tail reads the end of each. The other checks keep reading them as one text.</summary>
    public readonly record struct Apart(string? Window, string? CoopServerLog);

    public static IReadOnlyList<string> Explain(
        string? exitLine,
        bool wasServing,
        string? serverLog,
        string? engineLog,
        IReadOnlyList<ModName> extras,
        IReadOnlyCollection<string> workingIds,
        int setupModCount = 0,
        IReadOnlyList<string>? enabledIds = null,
        Apart? apart = null,
        string? fatalLine = null)
    {
        // The server's own FATAL line, kept by the caller for the whole run. It can scroll out of the window's last
        // lines while the coop server log of the run is missing, which left "closed from outside" (AD1259 crash report #19).
        if (!string.IsNullOrWhiteSpace(fatalLine) && !(serverLog ?? "").Contains(fatalLine, StringComparison.Ordinal))
        {
            serverLog = fatalLine + "\n" + serverLog;
            if (apart is { } parts)
                apart = parts with { Window = fatalLine + "\n" + parts.Window };
        }

        // The known crash patterns see every turned on module (the map too), or the extras when the caller has no list.
        var modules = enabledIds ?? extras.Select(mod => mod.Id).ToList();
        var lines = ExplainStop(exitLine, wasServing, serverLog, engineLog, extras, workingIds, setupModCount, modules, apart);
        // An unreadable mod-config.json does not stop the server, but it changes the whole
        // run (battle size 1000 and every other default), so it is told after the cause.
        var config = FindUnreadableModConfig((serverLog ?? "") + "\n" + (engineLog ?? ""));
        return config == null ? lines : lines.Concat(config).ToList();
    }

    /// <summary>The Coop server logs "mod-config.json UNREADABLE (path) — continuing on
    /// defaults: reason" when one mistake in the file (for example True with a capital
    /// letter) stops its parser. It then ignores every setting in that file.</summary>
    public static IReadOnlyList<string>? FindUnreadableModConfig(string? log)
    {
        if (string.IsNullOrEmpty(log))
            return null;
        var match = Regex.Match(log, @"mod-config\.json UNREADABLE[^\r\n]*?continuing on defaults:?\s*(?<reason>[^\r\n]*)", RegexOptions.IgnoreCase);
        if (!match.Success)
            return null;
        var where = Regex.Match(match.Groups["reason"].Value, @"\bline (?<line>\d+)", RegexOptions.IgnoreCase);
        var happened = where.Success
            ? "What happened: The Coop server could not read mod-config.json because of a mistake on line " + where.Groups["line"].Value + ". So it ignored the whole file and used its defaults, like battle size 1000. None of the settings in that file were used this run."
            : "What happened: The Coop server could not read mod-config.json. So it ignored the whole file and used its defaults, like battle size 1000. None of the settings in that file were used this run.";
        return
        [
            happened,
            "What to do: Stop the server and press Start server again. This tool fixes True and False written with capital letters before the server starts. If this comes back, open mod-config.json and fix the line it names, or delete the file so Coop makes a new one with default settings. Then set your settings again on the Settings page.",
            "What not to do: Do not change the battle size in the game options for this. Coop battles use the battle size on the Settings page, and the server uses it for everyone."
        ];
    }

    private static IReadOnlyList<string> ExplainStop(
        string? exitLine,
        bool wasServing,
        string? serverLog,
        string? engineLog,
        IReadOnlyList<ModName> extras,
        IReadOnlyCollection<string> workingIds,
        int setupModCount,
        IReadOnlyList<string> modules,
        Apart? apart)
    {
        var combined = (serverLog ?? "") + "\n" + (engineLog ?? "");
        if (!wasServing)
        {
            var blocked = BlockedDllNotice.Explain(combined);
            if (blocked != null)
                return blocked;
            var missingCoop = FindMissingCoopServer(combined);
            if (missingCoop != null)
                return missingCoop;
        }

        var code = ReadExitCode(exitLine) ?? ReadExitCode(combined);
        // The known crash patterns (crash-signatures.json, the same ones the crash report screen
        // uses) win. A silent exit is only decided further down, after the checks that read a cause
        // from the log. The built in answers below stay for when the file lacks a pattern.
        var known = Known(code, exitLine, serverLog, engineLog, signature => signature.Category != CrashCategory.SilentExit, modules, apart);
        if (known != null)
            return known;
        // CoopModPatch stops the server on purpose after its first time setup; that is no crash.
        if (ModPatchSetup.Finished(code, combined))
            return [ModPatchSetup.Text];
        var assets = FindAssetsCrash(code, engineLog);
        if (assets != null)
            return AssetsCrashLines(assets);

        if (!wasServing)
        {
            var eula = ServerEula.Explain(combined, code);
            if (eula != null)
                return eula;
            if (combined.Contains("cannot be hosted directly", StringComparison.OrdinalIgnoreCase))
            {
                return
                [
                    "What happened: The server stopped before it loaded anything. The picked save is default_new_game or a .backup copy. default_new_game is the server's new-world template, and .backup1/.backup2 are spare copies. The server does not host those directly. Your mods are not the problem.",
                    "What to do: Click New campaign, type a name, and press Start server. To keep playing an older world, pick its own save name instead.",
                    "What not to do: Do not turn your mods off for this. Do not rename or delete default_new_game.sav. The server needs it to make new worlds. Do not move Bannerlord Coop in the module list."
                ];
            }
            // The start script writes its config files through a temporary copy (Write-Utf8TextAtomically).
            // When the PC blocks that new file, the PowerShell error is the script's own, not a mod's: it
            // once named No Companion Limit only because the line before it did (Coop #55).
            if (StartScriptWriteFailed.IsMatch(combined))
                return StartScriptWriteLines;
            var maps = FindTwoMaps(combined);
            if (maps != null)
                return maps;
            // Start-ModdedServer.ps1 refuses an old MCMv5.dll (below 5.12, or built against
            // UIExtenderEx), which crashed the engine while it loaded submodules (crash report #23).
            if (combined.Contains("is too old for the dedicated server and crashes it", StringComparison.OrdinalIgnoreCase))
            {
                return
                [
                    "What happened: The server did not start because Mod Configuration Menu (MCM) is an old copy. Old MCM needs a part the dedicated server does not load, so the server crashes with it.",
                    "What to do: Get MCM v5.12.3 or newer from Nexus Mods (mod 612) and put its Bannerlord.MBOptionScreen folder in the game's Modules folder, in place of an older one. Then press Start server again. The server tool copies the newer MCM to the server by itself.",
                    "What not to do: Do not turn MCM off for this. Other mods on the server need it."
                ];
            }
        }

        var failed = FindFailedLoad(combined, extras, workingIds);
        if (failed != null)
            return failed;

        var save = FindSaveMismatch(combined);
        if (save != null)
            return save;

        var dependency = FindDependency(combined, extras);
        if (dependency != null)
            return dependency;

        var duplicate = FindDuplicate(combined, extras);
        if (duplicate != null)
            return duplicate;

        var blamed = FindException(LastLines(combined, 200), extras, workingIds);
        if (blamed != null)
            return blamed;

        // Exit code 2 is Coop's own "the save could not be loaded" (ServerExitKind.LoadFailure).
        // Loading the same save again fails the same way, so the way out is a backup.
        if (code is int exitCode && ServerExit.Classify(exitCode) == ServerExitKind.LoadFailure)
        {
            return
            [
                "What happened: The server could not load the campaign save (exit code 2). The save may be damaged, or it needs a mod that is not on. Your other saves are not touched.",
                "What to do: Press Load backup next to the campaign save and put back a backup from before this happened. Then press Start server. If the save is fine but a mod is missing, turn that mod on and start again.",
                "What not to do: Do not press Start server again and again on the same save. It fails the same way each time. Do not delete the save, a backup can only go back over it."
            ];
        }

        // The server wrote a FATAL line or a @DS@ fatal state: it stopped itself, whatever the exit code says.
        var fatal = FindFatal(combined);
        if (fatal != null)
            return FatalLines(fatal, wasServing);

        // Code 1 with no error at all: neither Coop (its fatal path is code 3) nor a mod. Code -1 has
        // its own silent exit signature for the crash screen, but Coodsz Host keeps its own -1 lines
        // below, which also point at the extra mods.
        var silent = code == SilentExitCode
            ? Known(code, exitLine, serverLog, engineLog, signature => signature.Category == CrashCategory.SilentExit, modules, apart)
            : null;
        if (silent != null)
            return silent;
        if (code == SilentExitCode && !HasErrorLine(LastLines(combined, 200)))
            return SilentExitLines(wasServing);

        if (code is 0 && wasServing)
        {
            return
            [
                "What happened: The server was already up and then it stopped on its own. The log shows a normal exit. Nothing in the log says a mod broke.",
                "What to do: Press Start server when you want players to join again. You do not have to change mods for this stop.",
                "What not to do: Next time, do not move Bannerlord Coop. The patches and the map load after it, so it is not the last mod. Do not drag the working mods around, and do not put another mod between them."
            ];
        }

        // Whether this run reached serving decides the first words: a run that served for a
        // while and then crashed was ready for players. Extra mods are the ones beyond the
        // working list, so a setup made only of working list mods has none.
        var killed = wasServing && code == -1;
        if (extras.Count == 0)
        {
            var happened = (wasServing, killed) switch
            {
                (false, _) => "What happened: The server stopped before it was ready for players. All " + setupModCount + " mods in this setup are on the working list, and the log does not name a broken mod.",
                (true, true) => "What happened: The server was serving players, then it stopped with exit code -1. That code usually means the process was ended from outside (Task Manager, a script or Windows) or crashed without writing why. All " + setupModCount + " mods in this setup are on the working list, and the log does not name a broken mod.",
                _ => "What happened: The server was serving players, then it stopped. All " + setupModCount + " mods in this setup are on the working list, and the log does not name a broken mod."
            };
            return
            [
                happened,
                "What to do: Press Start server again. If it stops the same way, click Open log folder and keep that log. That folder is what someone can read to see the full story.",
                "What not to do: Next time, do not move Bannerlord Coop. The patches and the map load after it, so it is not the last mod. Do not drag the working mods around, and do not put another mod between them."
            ];
        }

        var extraHappened = (wasServing, killed) switch
        {
            (false, _) => "What happened: The server stopped before it was ready for players. The log does not name one extra mod, so the tool cannot point at a single mod and say that one broke it.",
            (true, true) => "What happened: The server was serving players, then it stopped with exit code -1. That code usually means the process was ended from outside (Task Manager, a script or Windows) or crashed without writing why. The log does not name one of the " + extras.Count + " extra mods, so the tool cannot point at a single mod and say that one broke it.",
            _ => "What happened: The server was serving players, then it stopped. The log does not name one of the " + extras.Count + " extra mods, so the tool cannot point at a single mod and say that one broke it."
        };
        return
        [
            extraHappened,
            "What to do: Turn off the extra mods you added. Click Save module config. Press Start server again. If it stays up, turn those extra mods back on one at a time. Save and start after each one. The mod that makes the server stop is the one to leave off.",
            "What not to do: Next time, do not move Bannerlord Coop. The patches and the map load after it, so it is not the last mod. Do not drag the working mods around, and do not put another mod between them. Extra mods only go under the whole working list."
        ];
    }

    /// <summary>
    /// The What happened and What to do lines of the best known server crash pattern among those
    /// the filter keeps, or null. It reads the same logs as the rest of this explanation. The modules are the
    /// turned on mods, for a pattern's module rules. With apart, the server window and the coop server log are read
    /// one by one instead of the server log text.
    /// </summary>
    public static IReadOnlyList<string>? Known(int? exitCode, string? exitLine, string? serverLog, string? engineLog, Func<CrashSignature, bool>? filter = null, IReadOnlyList<string>? modules = null, Apart? apart = null)
    {
        var file = CrashSignatureFile.Current;
        var picked = new CrashSignatureFile
        {
            Schema = file.Schema,
            Version = file.Version,
            Signatures = file.Signatures.Where(signature => filter?.Invoke(signature) ?? true).ToList()
        };
        var texts = new Dictionary<string, IReadOnlyList<string>>();
        if (apart is { } logs)
        {
            // Like a crash report: a pattern's tail reads the end of the window, not the end of the log after it.
            if (!string.IsNullOrEmpty(logs.Window))
                texts[CrashLogSource.Server] = [logs.Window];
            if (!string.IsNullOrEmpty(logs.CoopServerLog))
                texts[CrashLogSource.ServerLog] = [logs.CoopServerLog];
        }
        else if (!string.IsNullOrEmpty(serverLog))
        {
            texts[CrashLogSource.Server] = [serverLog];
            texts[CrashLogSource.ServerLog] = [serverLog];
        }

        if (!string.IsNullOrEmpty(engineLog))
            texts[CrashLogSource.Rgl] = [engineLog];
        if (!string.IsNullOrEmpty(exitLine))
            texts[CrashLogSource.HostConsole] = [exitLine];
        var match = CrashSignatureMatcher.Match(picked, new CrashEvidence { Side = "server", Tool = Edition.IsAD1259 ? "ad1259" : "coop", ExitCode = exitCode, Texts = texts, Modules = modules ?? [] });
        if (match == null)
            return null;
        // The body is in the tool's language; the English prefixes stay, the server log keys on them.
        // Every answer of this tool ends with What not to do; a pattern without one gets the general line.
        var text = match.Localized(BannerlordCoopLauncher.Core.Localization.Translator.Current);
        return
        [
            "What happened: " + text.WhatHappened,
            "What to do: " + text.WhatToDo,
            "What not to do: " + (text.WhatNotToDo ?? "Do not move Bannerlord Coop or turn your mods off for this.")
        ];
    }

    /// <summary>0xC0000005, an access violation inside the engine.</summary>
    public const int AccessViolationCode = -1073741819;

    /// <summary>The code a process gets when it is ended from outside, or ends itself without an error.</summary>
    public const int SilentExitCode = 1;

    private static readonly Regex AssetsPackageLine = new(@"Loading packages\s+\$BASE/Modules/(?<mod>[^/\\\r\n]+)/Assets(?:\.\.\.)?\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    // Lines the engine writes after "Loading packages" before it reads the files.
    private static readonly Regex PackageNoise = new(@"^\s*(?:\[[^\]]*\]\s*)?(?:Registering items\.*|Initializing items\.*|Unable to find item to add dependency.*)?\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex ErrorLine = new(@"(?i)\b(?:unhandled exception|fatal error|EXCEPTION_ACCESS_VIOLATION|access violation|stack overflow|out of memory)\b|\b(?:[A-Z]\w*\.)*[A-Z]\w*Exception\b\s*:", RegexOptions.CultureInvariant);

    /// <summary>
    /// The mod folder whose raw Assets folder crashed the engine: the exit code is 0xC0000005 and
    /// the engine log's last real line is "Loading packages $BASE/Modules/X/Assets...". The
    /// headless server cannot load raw assets, so this is the mod, never the save.
    /// </summary>
    public static string? FindAssetsCrash(int? exitCode, string? engineLog)
    {
        if (exitCode != AccessViolationCode || string.IsNullOrEmpty(engineLog))
            return null;
        var lines = engineLog.Replace("\r", "").Split('\n');
        for (var i = lines.Length - 1; i >= 0; i--)
        {
            if (PackageNoise.IsMatch(lines[i]))
                continue;
            var match = AssetsPackageLine.Match(lines[i]);
            var mod = match.Success ? match.Groups["mod"].Value.Trim() : "";
            return mod.Length > 0 && mod != "." && mod != ".." ? mod : null;
        }

        return null;
    }

    public static IReadOnlyList<string> AssetsCrashLines(string module) =>
    [
        "What happened: The Assets folder of " + module + " crashed the server while it was loading packages, before the save was loaded. The dedicated server cannot load a mod's raw Assets folder. Your save is fine.",
        "What to do: Press Start server again. Coodsz Coop Server Tool hid that Assets folder in the server copy now (renamed to Assets.off), so the server skips it. Players keep theirs.",
        "What not to do: Do not load a backup or run the save fixes for this. The save was not the problem. Do not move Bannerlord Coop or turn " + module + " off for this either."
    ];

    /// <summary>The text for exit code 1 with no error in the log.</summary>
    public static IReadOnlyList<string> SilentExitLines(bool wasServing) =>
    [
        (wasServing
            ? "What happened: The server was serving players, then it stopped with exit code 1 and no error."
            : "What happened: The server stopped with exit code 1 and no error before it was ready for players.")
        + " Exit code 1 with no error usually means the process was closed from outside, by an antivirus, Task Manager or a script. A Windows Insider build can do this too.",
        "What to do: Check your antivirus history and allow the DedicatedServer folder if it was blocked. Make sure no script or tool closes dotnet.exe or the server. If this PC runs a Windows Insider build, try it on a normal Windows build. Then press Start server again.",
        "What not to do: Do not move Bannerlord Coop or turn your mods off for this. Nothing in the log points at a mod or the save."
    ];

    private static readonly Regex FatalTextLine = new(@"\[DedicatedServer\]\s*FATAL during\b", RegexOptions.CultureInvariant);

    /// <summary>True for the server's "[DedicatedServer] FATAL during ..." line or its @DS@ state event with phase "fatal".</summary>
    public static bool IsFatalLine(string? line) =>
        !string.IsNullOrEmpty(line)
        && (FatalTextLine.IsMatch(line)
            || (line.Contains(DsEventParser.Marker, StringComparison.Ordinal)
                && DsEventParser.TryParse(line, out var evt)
                && evt is DsEvent.State state
                && state.RawPhase.Equals("fatal", StringComparison.OrdinalIgnoreCase)));

    /// <summary>
    /// The newest FATAL line of the text, trimmed to one line, or null. A FATAL during line wins over the @DS@ event,
    /// which is quoted by its detail, since the line also says what the server was doing.
    /// </summary>
    public static string? FindFatal(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return null;
        var lines = text.Replace("\r", "").Split('\n');
        var textLine = lines.LastOrDefault(line => FatalTextLine.IsMatch(line));
        if (textLine != null)
            return Clean(textLine);
        var eventLine = lines.LastOrDefault(IsFatalLine);
        if (eventLine == null)
            return null;
        DsEventParser.TryParse(eventLine, out var evt);
        var detail = (evt as DsEvent.State)?.Detail;
        return string.IsNullOrWhiteSpace(detail) ? Clean(eventLine) : Clean("FATAL " + detail);
    }

    /// <summary>
    /// The text for a run whose server wrote a FATAL line: it stopped itself, so no outside close. Like Known, the body
    /// is in the tool's language and the English prefixes stay, since the window strips them before it translates.
    /// </summary>
    public static IReadOnlyList<string> FatalLines(string quote, bool wasServing) =>
    [
        "What happened: " + string.Format(wasServing
            ? BannerlordCoopLauncher.Core.Localization.Translator.T("The server was serving players, then it stopped itself. Its last error line says {0}")
            : BannerlordCoopLauncher.Core.Localization.Translator.T("The server stopped itself before it was ready for players. Its last error line says {0}"), quote),
        "What to do: " + BannerlordCoopLauncher.Core.Localization.Translator.T("That line names the cause. Press Start server again. If it stops with the same line, click Send my logs so Coodsz can look at it."),
        "What not to do: " + BannerlordCoopLauncher.Core.Localization.Translator.T("Do not move Bannerlord Coop or turn your mods off for this, unless that line names a mod.")
    ];

    /// <summary>True when the text has an error line (an exception with its message, a fatal error or an access violation).</summary>
    public static bool HasErrorLine(string? text) =>
        !string.IsNullOrEmpty(text)
        && text.Replace("\r", "").Split('\n').Any(line => !IsNoise(line) && ErrorLine.IsMatch(line));

    public readonly record struct Example(string Title, IReadOnlyList<string> Lines);

    public static IReadOnlyList<Example> Examples()
    {
        var crash = new ModName("CrashExample", "Crash Example");
        var chaos = new ModName("ChaosBattle", "Chaos Battle");
        var diplomacy = new ModName("Bannerlord.Diplomacy", "Diplomacy (Fork)");
        var working = new[] { "ImprovedGarrisons" };
        return
        [
            new("1. An extra mod did not load", Explain(null, false, "CrashExample could not be loaded correctly", null, [crash], working)),
            new("2. Two extra mods did not load", Explain(null, false, "CrashExample could not be loaded correctly\nChaosBattle could not be loaded correctly", null, [crash, chaos], working)),
            new("3. A working mod did not load", Explain(null, false, "ImprovedGarrisons could not be loaded correctly", null, [], working)),
            new("4. The save does not match", Explain(null, false, "Save 'lol' was not found or is incompatible with the selected module set.", null, [chaos], working)),
            new("5. A mod needs another mod", Explain(null, false, "Bannerlord.Diplomacy depends on Bannerlord.Harmony", null, [diplomacy], working)),
            new("6. The same mod is in two folders", Explain(null, false, "module id Coop is already loaded", null, [], working)),
            new("7. A crash line names an extra mod", Explain(null, false, "MissingMethodException: CrashExample.SubModule could not start", null, [crash], working)),
            new("8. A crash line names a working mod", Explain(null, false, "NullReferenceException in ImprovedGarrisons", null, [], working)),
            new("9. The server was up, then stopped cleanly", Explain("engine exited with code 0", true, "", null, [], working)),
            new("10. No extra mods, and the log names nothing", Explain("engine exited with code -1073741819", false, "server stopped", null, [], working)),
            new("11. Extra mods are on, and the log names nothing", Explain("engine exited with code -1073741819", false, "server stopped", null, [crash], working)),
            new("12. The dedicated server is missing its Coop files", Explain(null, false, "COOP MODULE VERIFICATION FAILED: required Coop module assembly missing: DedicatedServer\\engine\\Modules\\Coop\\bin\\Win64_Shipping_Server\\Coop.Core.dll", null, [], working)),
            new("13. The server could not read mod-config.json", Explain("engine exited with code -1073741819", false, "[Coop] mod-config.json UNREADABLE (C:\\Users\\Host\\Documents\\Mount and Blade II Bannerlord\\CoopData\\mod-config.json) — continuing on defaults: \"Unexpected character encountered while parsing value: r. Path 'modOptions.clientsCanUseCheats', line 44, position 28.\"", null, [], working)),
            new("14. A mod's raw Assets folder crashed the server", Explain("engine exited with code -1073741819", false, "", "[20:14:48.304] Loading packages $BASE/Modules/RaiseYourBanner/Assets...\n[20:14:48.309] Registering items...\n[20:14:48.309] Initializing items", [], working)),
            new("15. The server stopped with exit code 1 and no error", Explain("Server process exited with code 1.", true, "pulse: players=1", null, [], working))
        ];
    }

    public static Logs Read(string serverLogsDirectory, DateTime? startedAt, string engineLogDirectory)
    {
        var cutoff = startedAt?.AddMinutes(-2) ?? DateTime.Now.AddMinutes(-20);
        return new Logs(NewestTail(serverLogsDirectory, "coop-server-*.log", cutoff, skipSingleplayer: false),
            NewestTail(engineLogDirectory, "rgl_log_*.txt", cutoff, skipSingleplayer: true));
    }

    public static string EngineLogDirectory() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Mount and Blade II Bannerlord", "logs");

    public static int? ReadExitCode(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        var match = Regex.Match(text, @"(?:engine|process) exited with code\s+(-?\d+)", RegexOptions.IgnoreCase);
        return match.Success && int.TryParse(match.Groups[1].Value, out var code) ? code : null;
    }

    private static IReadOnlyList<string>? FindMissingCoopServer(string log)
    {
        if (!log.Contains("COOP MODULE VERIFICATION FAILED", StringComparison.OrdinalIgnoreCase)
            && !log.Contains("required Coop module assembly missing", StringComparison.OrdinalIgnoreCase)
            && !log.Contains("redeploy the matching Modules/Coop", StringComparison.OrdinalIgnoreCase))
            return null;

        // The same failure also covers a Coop file that is there but is another
        // build (for example the Workshop client Coop.Core.dll copied in).
        if (!log.Contains("assembly missing", StringComparison.OrdinalIgnoreCase))
            return
            [
                "What happened: The server stopped before it was ready for players. A Coop file in DedicatedServer\\engine\\Modules\\Coop\\bin\\Win64_Shipping_Server is not the Coop build this server was released with. The server only runs the exact Coop files listed in release-info.txt next to BannerlordCoopServer.exe.",
                "What to do: In Steam, open Bannerlord Properties, Installed Files, and click Verify integrity of game files. Wait until Workshop item 3770450698 finishes, then press Start server again. If it fails again, unsubscribe from Workshop item 3770450698, subscribe again, wait for the download, and press Start server.",
                "What not to do: Do not copy Coop files from the game Modules folder or the Workshop bin folder into the dedicated server. Those are client files and the server refuses them."
            ];

        return
        [
            "What happened: The server stopped before it was ready for players. The dedicated server is missing Coop.Core.dll in DedicatedServer\\engine\\Modules\\Coop\\bin\\Win64_Shipping_Server. The Coop folder in the Bannerlord install only has the client files, and this server does not load those.",
            "What to do: In Steam, verify Workshop item 3770450698 and wait until that Coop.Core.dll file is on disk inside the DedicatedServer folder. Then press Start server again.",
            "What not to do: Do not copy the Coop folder from the game's Modules folder onto the dedicated server. That copy is the client. Do not move Bannerlord Coop in the module list."
        ];
    }

    private static IReadOnlyList<string>? FindFailedLoad(
        string log,
        IReadOnlyList<ModName> extras,
        IReadOnlyCollection<string> workingIds)
    {
        var named = new List<ModName>();
        foreach (Match match in Regex.Matches(log, @"\b(?<id>[A-Za-z0-9_.]+) could not be loaded correctly", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            var extra = extras.FirstOrDefault(mod => string.Equals(mod.Id, match.Groups["id"].Value, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(extra.Id) && named.All(mod => !string.Equals(mod.Id, extra.Id, StringComparison.OrdinalIgnoreCase)))
                named.Add(extra);
        }

        if (named.Count == 0)
        {
            var working = Regex.Match(log, @"\b(?<id>[A-Za-z0-9_.]+) could not be loaded correctly", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (working.Success && workingIds.Contains(working.Groups["id"].Value, StringComparer.OrdinalIgnoreCase))
            {
                var name = working.Groups["id"].Value;
                return
                [
                    "What happened: The server stopped because " + name + " did not load. " + name + " is one of the working mods. Those mods are the list that already runs this coop game.",
                    "What to do: Turn off the extra mods you added. Click Save module config. Press Start server again. If it stays up, the extra mods were the problem. Add them back one at a time.",
                    "What not to do: Next time, do not move " + name + ". Do not move Bannerlord Coop. The patches and the map load after it, so it is not the last mod. Do not drag the working mods around, and do not put another mod between them."
                ];
            }

            return null;
        }

        var names = string.Join(", ", named.Select(Display));
        return
        [
            "What happened: The server stopped because " + names + " did not load. The game tried to start that mod and could not finish loading it, so the server closed before players could join.",
            "What to do: Turn " + names + " off. Click Save module config. Press Start server again. Leave it off until you have a copy that loads. Players need the same mods in the client, or the join fails.",
            "What not to do: Next time, do not move Bannerlord Coop. The patches and the map load after it, so it is not the last mod. Do not drag the working mods around, and do not put " + names + " between those working mods. Extra mods only go under the whole working list."
        ];
    }

    /// <summary>
    /// Start-ModdedServer.ps1 refused the save before the game started, because the mods that are
    /// on do not match the ones the save was made with. Nothing loaded, so it is no crash.
    /// </summary>
    public static bool IsSaveRefusedLine(string? line) =>
        line != null && line.Contains("was not found or is incompatible with the selected module set", StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<string>? FindSaveMismatch(string log)
    {
        var match = Regex.Match(log, @"Save '(.+?)' was not found or is incompatible", RegexOptions.IgnoreCase);
        if (!match.Success)
            return null;
        var name = match.Groups[1].Value;
        // Same mods, but one changed version (the start script names it, CR-P4984).
        var version = Regex.Match(log, "Save '" + Regex.Escape(name) + "' was made with (.+?) and this server has (.+?), so it is hidden\\.", RegexOptions.IgnoreCase);
        if (version.Success)
        {
            return
            [
                "What happened: The server stopped before anyone could join. The campaign save named " + name + " was made with " + version.Groups[1].Value + " and this server has " + version.Groups[2].Value + ". The mods match, but a save made with another version of a mod stays hidden, so the server did not open that world.",
                "What to do: Pick a campaign save made with the version you have now and press Start server. To keep playing this campaign, put back the version of that mod the save was made with. Or make a new campaign save after you click Save module config.",
                "What not to do: Do not turn mods off or move them for this. Only the version changed."
            ];
        }
        return
        [
            "What happened: The server stopped before anyone could join. The campaign save named " + name + " does not match the mods that are turned on. A save remembers the mods it was made with. When the mods are different, the server will not open that world.",
            "What to do: Pick a campaign save that was made with this same setup, then press Start server. If you only added mods, keep the old mods in their order with the new ones under them and press Start server again. Coodsz Coop Server Tool asks to add them to this campaign and makes a backup first. If a mod the save was made with is turned off or removed, turn it back on. Removing a mod can break a campaign. Or make a new campaign save after you click Save module config.",
            "What not to do: Next time, do not move Bannerlord Coop. The patches and the map load after it, so it is not the last mod. Do not drag the working mods around, and do not put a new mod between them. Extra mods only go under the whole working list."
        ];
    }

    private static IReadOnlyList<string>? FindDependency(string log, IReadOnlyList<ModName> extras)
    {
        var dependent = Regex.Match(
            log,
            @"dependent module:\s*(?<dep>[A-Za-z0-9_.]+).{0,80}?\bmodule\s+(?<mod>[A-Za-z0-9_.]+)",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        var depends = dependent.Success
            ? dependent
            : Regex.Match(log, @"\b(?<mod>[A-Za-z0-9_.]+)\s+depends on(?: module)?\s+(?<dep>[A-Za-z0-9_.]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!depends.Success)
            return null;
        var owner = Describe(depends.Groups["mod"].Value, extras);
        var needed = Describe(depends.Groups["dep"].Value, extras);
        return
        [
            "What happened: The server stopped because " + owner + " needs " + needed + ", and " + needed + " is not loaded. A mod cannot start when the mod it needs is missing or turned off.",
            "What to do: Turn " + needed + " on and leave it on, or turn " + owner + " off. Click Save module config. Press Start server again. Players need the same mods in the client, or the join fails.",
            "What not to do: Next time, do not move Bannerlord Coop. The patches and the map load after it, so it is not the last mod. Do not put " + owner + " between the working mods. Extra mods only go under the whole working list."
        ];
    }

    // Start-ModdedServer.ps1 stops when more than one turned on module has a SceneObj\Main_map.
    // SandBox among them is the extra map folder ExtraBaseMap moves aside on the next Start.
    // The console can wrap the line anywhere, so spaces between the words may be line breaks.
    private static readonly Regex TwoMaps = new(@"Multiple\s+active\s+modules\s+provide\s+SceneObj\\Main_map:\s*(?<ids>.+?)\.\s+Their\s+load\s+order", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline);

    public static IReadOnlyList<string>? FindTwoMaps(string log)
    {
        var match = TwoMaps.Match(log);
        if (!match.Success)
            return null;
        var ids = Regex.Replace(match.Groups["ids"].Value, @"\s+", " ").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var others = ids.Where(id => !id.Equals("SandBox", StringComparison.OrdinalIgnoreCase)).ToList();
        if (others.Count < ids.Length)
        {
            return
            [
                "What happened: The server did not start because two modules bring a campaign map: SandBox and " + string.Join(", ", others) + ". A clean server's SandBox has no map of its own, so its folder SandBox\\SceneObj\\Main_map in the dedicated server is an extra copy. Only one map can load.",
                "What to do: Press Start server again and click Move it aside when the tool asks. Or move the folder DedicatedServer\\engine\\Modules\\SandBox\\SceneObj\\Main_map out yourself, then press Start server.",
                "What not to do: Do not turn SandBox or your map mod off for this, and do not delete anything else in the SandBox folder."
            ];
        }

        return
        [
            "What happened: The server did not start because more than one mod brings a campaign map: " + string.Join(", ", ids) + ". The server can only load one map.",
            "What to do: Keep the map mod you want and turn the other one off, then press Start server again."
        ];
    }

    private static IReadOnlyList<string>? FindDuplicate(string log, IReadOnlyList<ModName> extras)
    {
        var match = Regex.Match(log, @"module(?: id)?\s+(?<id>[A-Za-z0-9_.]+)\s+is already", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!match.Success)
            return null;
        var name = Describe(match.Groups["id"].Value, extras);
        return
        [
            "What happened: The server stopped because " + name + " is in more than one folder. The server can only use one copy. When two folders share that mod, it does not know which one to load.",
            "What to do: Move the extra folder out of the server Modules folder. Keep the copy the working list uses. Click Refresh. Click Save module config. Press Start server again.",
            "What not to do: Next time, do not delete the copy the working list uses. Do not move Bannerlord Coop. The patches and the map load after it, so it is not the last mod."
        ];
    }

    private static IReadOnlyList<string>? FindException(
        string tail,
        IReadOnlyList<ModName> extras,
        IReadOnlyCollection<string> workingIds)
    {
        var lines = tail.Replace("\r", "").Split('\n');
        for (var i = lines.Length - 1; i >= 0; i--)
        {
            if (!lines[i].Contains("Exception", StringComparison.OrdinalIgnoreCase) || IsNoise(lines[i]))
                continue;
            var window = string.Join(" ", lines.Skip(i).Take(6));
            var named = extras.Where(mod => Mentions(window, mod)).ToList();
            if (named.Count == 0)
                continue;
            var names = string.Join(", ", named.Select(Display));
            var quote = Clean(lines[i]);
            return
            [
                "What happened: The server stopped while it was starting. The log says: " + quote + " That line names " + names + ".",
                "What to do: Turn " + names + " off. Click Save module config. Press Start server again. Leave it off if the server stays up. Players need the same mods in the client, or the join fails.",
                "What not to do: Next time, do not move Bannerlord Coop. The patches and the map load after it, so it is not the last mod. Do not drag the working mods around, and do not put " + names + " between those working mods."
            ];
        }

        for (var i = lines.Length - 1; i >= 0; i--)
        {
            if (!lines[i].Contains("Exception", StringComparison.OrdinalIgnoreCase) || IsNoise(lines[i]))
                continue;
            var window = string.Join(" ", lines.Skip(i).Take(6));
            var working = workingIds.FirstOrDefault(id => ContainsToken(window, id));
            if (working == null)
                continue;
            return
            [
                "What happened: The server stopped while it was starting. The log says: " + Clean(lines[i]) + " That line names " + working + ", which is one of the working mods.",
                "What to do: Turn off the extra mods you added. Click Save module config. Press Start server again. If it stays up, add the extra mods back one at a time.",
                "What not to do: Next time, do not move " + working + ". Do not move Bannerlord Coop. The patches and the map load after it, so it is not the last mod. Do not drag the working mods around, and do not put another mod between them."
            ];
        }

        return null;
    }

    private static string NewestTail(string directory, string pattern, DateTime cutoff, bool skipSingleplayer) =>
        NewestLog(directory, pattern, cutoff, skipSingleplayer) is { } log ? log.Tail : "";

    private static (FileInfo File, string Tail)? NewestLog(string directory, string pattern, DateTime cutoff, bool skipSingleplayer)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            return null;
        foreach (var file in new DirectoryInfo(directory).GetFiles(pattern).OrderByDescending(item => item.LastWriteTimeUtc))
        {
            if (file.LastWriteTime < cutoff)
                continue;
            if (file.Name.Contains("errors", StringComparison.OrdinalIgnoreCase))
                continue;
            var text = ReadTail(file.FullName);
            // A game on the same PC writes its rgl_log next to the server's. Its "/singleplayer" command line
            // sits at the top of a long log, past the tail (crash report #33 read the player's game log).
            if (skipSingleplayer && (text.Contains("/singleplayer", StringComparison.OrdinalIgnoreCase) || IsSingleplayerLog(file.FullName)))
                continue;
            return (file, text);
        }

        return null;
    }

    private static bool IsSingleplayerLog(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var head = new byte[(int)Math.Min(stream.Length, 64 * 1024)];
        var read = stream.Read(head, 0, head.Length);
        return System.Text.Encoding.UTF8.GetString(head, 0, read).Contains("/singleplayer", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The end of the engine error log (rgl_log_errors_PID.txt) that goes with the newest server engine log.
    /// The engine writes its last fatal assert there, like Steam's pipe failing (crash report #33), and not in rgl_log.
    /// </summary>
    public static string? EngineErrorsTail(string engineLogDirectory, DateTime cutoff, int lines = 40)
    {
        if (NewestLog(engineLogDirectory, "rgl_log_*.txt", cutoff, skipSingleplayer: true) is not { } log)
            return null;
        var pid = Path.GetFileNameWithoutExtension(log.File.Name)["rgl_log_".Length..];
        var errors = Path.Combine(log.File.DirectoryName ?? "", "rgl_log_errors_" + pid + ".txt");
        if (!File.Exists(errors))
            return null;
        var tail = LastLines(ReadTail(errors), lines).Trim();
        return tail.Length == 0 ? null : tail;
    }

    private static string LastLines(string text, int count)
    {
        var lines = text.Replace("\r", "").Split('\n');
        return string.Join("\n", lines.Skip(Math.Max(0, lines.Length - count)));
    }

    private static bool Mentions(string text, ModName mod)
    {
        if (ContainsToken(text, mod.Id))
            return true;
        var name = Display(mod);
        if (name.Length >= 5 && text.Contains(name, StringComparison.OrdinalIgnoreCase))
            return true;
        var shortName = name.Split('(')[0].Trim();
        return shortName.Length >= 5
            && !string.Equals(shortName, name, StringComparison.Ordinal)
            && text.Contains(shortName, StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsToken(string text, string token) =>
        token.Length >= 3 && Regex.IsMatch(text, @"\b" + Regex.Escape(token) + @"\b", RegexOptions.IgnoreCase);

    private static string Describe(string id, IReadOnlyList<ModName> extras)
    {
        var known = extras.FirstOrDefault(mod => string.Equals(mod.Id, id, StringComparison.OrdinalIgnoreCase));
        return string.IsNullOrWhiteSpace(known.Id) ? id : Display(known);
    }

    private static string Display(ModName mod) =>
        string.IsNullOrWhiteSpace(mod.Name) ? mod.Id : mod.Name;

    private static readonly Regex StartScriptWriteFailed = new(
        // Only a new file the PC refused: a full disk or a file in use is something else and keeps the general text.
        @"Write-Utf8TextAtomically\s*:\s*Exception calling[^\r\n]*(?:Could not find file|Access to the path)|FullyQualifiedErrorId\s*:\s*(?:FileNotFoundException|UnauthorizedAccessException)\s*,\s*Write-Utf8TextAtomically",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static readonly IReadOnlyList<string> StartScriptWriteLines =
    [
        "What happened: The server did not start because its start script could not write a file in the server's data folder. Something on this PC blocked the new file, most likely antivirus or Controlled Folder Access in Windows Security. Your mods are not the problem.",
        "What to do: Press Start server again. If it fails the same way, open Windows Security, look at Protection history for a file blocked at this time, and allow the app it names.",
        "What not to do: Do not turn your mods off for this."
    ];

    // A PowerShell error record from the start script itself never names a mod's own exception.
    private static bool IsNoise(string line) =>
        BannerlordCoopLauncher.Core.Support.StartScriptOutput.IsPowerShellRecord(line)
        || line.Contains("FMOD", StringComparison.OrdinalIgnoreCase)
        || line.Contains("Serilog", StringComparison.OrdinalIgnoreCase)
        || line.Contains("AccessTools", StringComparison.OrdinalIgnoreCase)
        || line.Contains("ReflectionTypeLoadException", StringComparison.OrdinalIgnoreCase)
        || line.Contains("attribute is not declared", StringComparison.OrdinalIgnoreCase);

    private static string Clean(string line)
    {
        var text = Regex.Replace(line.Trim(), @"^\[[^\]]+\]\s*", "");
        text = Regex.Replace(text, @"^\d{2}:\d{2}:\d{2}(?:\.\d+)?\s*", "");
        return text.Length <= 180 ? text : text[..177] + "...";
    }

    private static string ReadTail(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        const int max = 400_000;
        if (stream.Length > max)
            stream.Seek(-max, SeekOrigin.End);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
