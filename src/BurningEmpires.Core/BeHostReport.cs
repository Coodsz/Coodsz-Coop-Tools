using System.Text.RegularExpressions;
using BannerlordCoopLauncher.Core.Server;
using BannerlordCoopLauncher.Core.Support;
using TaomCoop.Core;

namespace BurningEmpires.Core;

public static class BeHostReport
{
    public readonly record struct Report(IReadOnlyList<string> Lines, string Status, bool Failed);

    private const string CompatPrefix = "[BurningEmpires.CoopCompat]";

    private const string KeepSaves = "Do not delete the campaign save or your Burning Empires save data.";

    public const string HelperMods = "the four helper mods Harmony, ButterLib, UIExtenderEx and MCM";

    private static readonly Regex PatchReadyLine = new(@"Burning Empires coop patch ready \((?<hooks>\d+) hooks?\)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>The hook count when the compat module says it is ready, or null.</summary>
    public static int? PatchReady(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return null;
        var matches = PatchReadyLine.Matches(text);
        return matches.Count > 0 && int.TryParse(matches[^1].Groups["hooks"].Value, out var hooks) ? hooks : null;
    }

    /// <summary>The healthy line for the window when the compat module is ready.</summary>
    public static string ReadyLine(int hooks) =>
        "Burning Empires Coop Compat is ready (" + hooks + " hooks).";

    /// <summary>The crash bar title and the status when the log of a server that stopped has no real error.</summary>
    public const string NoErrorTitle = "The server stopped without an error in its log";

    public const string NoErrorStatus = "The server stopped without an error in its log.";

    /// <summary>The line the crash bar shows and sends: the crash line of the log with the known
    /// harmless lines left out, or null when nothing real is left.</summary>
    public static string? CrashLine(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        var kept = text.Replace("\r", "").Split('\n').Where(line => !IsHarmless(line));
        return CrashReports.FindCrashLine(string.Join("\n", kept));
    }

    /// <summary>The crash bar title: a crash with no real line in the log (a server ended from
    /// outside, for one) says so plainly instead of The server crashed.</summary>
    public static string CrashTitle(string? crashLine, ServerExitKind kind) =>
        crashLine == null && kind == ServerExitKind.Unknown ? NoErrorTitle : CrashReports.ServerCrashTitle(kind);

    /// <summary>A report for the host window. While the server still runs (exited false) only the
    /// reports that are sure while it runs count: a blocked DLL, the compat module failing to hook
    /// Burning Empires, the save safety lines and the server's own fatal event. The others say the
    /// server stopped, so they wait until the process has really exited.</summary>
    public static Report? Explain(string? text, bool wasServing, bool exited = true)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var blocked = BlockedDllNotice.Explain(text);
        if (blocked != null)
            return new Report(blocked, "The server cannot load a DLL. Click Unblock DLLs on the Settings page, then click Start server again.", true);

        var hook = CompatHookFailed(text);
        if (hook != null)
            return hook;

        var fatal = Fatal(text);
        if (fatal != null)
            return fatal;

        var restore = RestoreFailed(text);
        if (restore != null)
            return restore;

        if (!exited)
            return SetupStep(text) ?? Migration(text);

        var serverDll = MissingServerDll(text);
        if (serverDll != null)
            return serverDll;

        var save = SaveMismatch(text);
        if (save != null)
            return save;

        var order = WrongOrder(text);
        if (order != null)
            return order;

        var failedLoad = FailedLoad(text);
        if (failedLoad != null)
            return failedLoad;

        var dependency = Dependency(text);
        if (dependency != null)
            return dependency;

        var duplicate = Duplicate(text);
        if (duplicate != null)
            return duplicate;

        var crash = Exception(text);
        if (crash != null)
            return crash;

        var code = ExitCode(text);
        if (code is 0 && wasServing)
        {
            return new Report(
            [
                "What happened: The server was already up and then it stopped on its own. The log shows a normal exit.",
                "What to do: Click Start server when you want players to join again.",
                "What not to do: " + KeepSaves + " Do not click Prepare server a second time."
            ],
            "The server stopped on its own.",
            false);
        }

        // Nothing real in the log: the harmless lines (IsHarmless) are never named as the cause.
        if (code != null)
        {
            return new Report(
            [
                wasServing
                    ? "What happened: The server was up and then it stopped without an error in its log."
                    : "What happened: The server stopped before it was ready for players, without an error in its log.",
                "What to do: Click Start server again. If it stops the same way, click Send server crash report.",
                "What not to do: " + KeepSaves + " Do not click Prepare server a second time."
            ],
            NoErrorStatus,
            true);
        }

        return SetupStep(text) ?? Migration(text);
    }

    // The compat module loaded but could not patch Burning Empires: another Burning Empires
    // version than the one it was made for. The campaign must not run that way. The single
    // method lines ("Could not hook Burning Empires X.Y") leave the rest of the patch on.
    private static Report? CompatHookFailed(string text)
    {
        if (!text.Contains("Burning Empires coop patch could not hook Burning Empires", StringComparison.OrdinalIgnoreCase))
            return null;
        return new Report(
        [
            "What happened: Burning Empires Coop Compat could not hook Burning Empires. This usually means Burning Empires is not " + BeServerPath.BeVersion + ". The compat module was made for that version.",
            "What to do: Stop the server. " + BeArchive.MainThenHotfix + " Then click Start server again.",
            "What not to do: Do not let players join a server in this state. Do not save over a good campaign."
        ],
        "Burning Empires Coop Compat could not hook Burning Empires. Check the Burning Empires version.",
        true);
    }

    // One of the compat module's setup steps threw; the rest still runs.
    private static Report? SetupStep(string text)
    {
        var match = Regex.Matches(text, Regex.Escape(CompatPrefix) + @"\s*(?<step>[A-Za-z][A-Za-z ]{1,40}?) could not be set up", RegexOptions.CultureInvariant);
        if (match.Count == 0)
            return null;
        var step = match[^1].Groups["step"].Value.Trim();
        return new Report(
        [
            "What happened: One part of Burning Empires Coop Compat (" + step + ") could not be set up. The server keeps running with the other parts.",
            "What to do: Keep playing if nothing looks wrong. Click Send server crash report so Coodsz can fix it.",
            "What not to do: Do not change the module order to work around it."
        ],
        "Part of Burning Empires Coop Compat (" + step + ") could not be set up.",
        false);
    }

    // DsSaveSafety: Burning Empires could not restore the save's data and runs on empty data.
    private static Report? RestoreFailed(string text)
    {
        if (!BeSaveData.RestoreBlocked(text))
            return null;
        return new Report(
        [
            "What happened: Burning Empires could not restore this campaign's own data, such as populations and religions. The server runs on empty Burning Empires data. It does not save that data, so your Burning Empires save data stays as it was.",
            "What to do: Stop the server. Click Last good save, or click Restore save backup and pick a backup with your Burning Empires save data. Then click Start server again.",
            "What not to do: Do not restore over the save while the server runs. Do not let players play on in this state. Their progress will not keep the Burning Empires data."
        ],
        "Burning Empires could not restore this save's data. Stop the server and restore a good save.",
        true);
    }

    // DsSaveSafety's startup warning: a migrated save blocks saving until it is saved to a new name.
    private static Report? Migration(string text)
    {
        if (!text.Contains("this Burning Empires build migrates older saves", StringComparison.OrdinalIgnoreCase))
            return null;
        return new Report(
        [
            "What happened: This Burning Empires version changes older saves when they load. A changed save cannot be saved again under its old name.",
            "What to do: If the campaign was made on an older Burning Empires, load it in single player and save it under a new name. Then host that save. Or click Save to a new name in this tool.",
            "What not to do: Do not keep hosting an older save under its old name. Its progress will not be saved."
        ],
        "This Burning Empires version changes older saves. Save an older campaign under a new name.",
        false);
    }

    // The server bin copies: Burning Empires, UIExtenderEx 2.13.3 or the CustomBattle DLL.
    private static Report? MissingServerDll(string text)
    {
        // A first chance line, or .NET looking for BurningEmpires.XmlSerializers, is not a missing file.
        var match = text.Replace("\r", "").Split('\n')
            .Where(line => !IsHarmless(line))
            .Select(line => ServerDllLine.Match(line))
            .FirstOrDefault(found => found.Success);
        if (match == null)
            return null;
        var name = match.Groups["name"].Value;
        return new Report(
        [
            "What happened: The server could not load " + name + ". Burning Empires needs four server files in BurningEmpires\\bin\\Win64_Shipping_Server. They are BurningEmpires.dll, BurningEmpires.DistanceCacheProbe.dll, UIExtenderEx " + BeServerPath.UIExtenderExVersion + " and the CustomBattle DLL.",
            "What to do: Click Start server again. The tool copies those files on every start. If it stops the same way, check that Burning Empires " + BeServerPath.Short(BeServerPath.BeVersion) + " is in the game's Modules folder. Then download this tool again into a fresh folder.",
            "What not to do: Do not put a whole UIExtenderEx " + BeServerPath.UIExtenderExVersion + " module in the server. Only its DLL goes in Burning Empires' server folder."
        ],
        name + " did not load on the server.",
        true);
    }

    // The server's own fatal state event (@DS@{"ev":"state","phase":"fatal",...}).
    private static Report? Fatal(string text)
    {
        if (ServerStateEvent.Last(text) is not { Fatal: true } state)
            return null;
        var status = state.Save.Length > 0
            ? "The server could not load the save " + state.Save + " and stopped. " + ServerStateEvent.ShortDetail(state.Detail)
            : "The server could not load the campaign and stopped. " + ServerStateEvent.ShortDetail(state.Detail);
        return new Report(
        [
            "What happened: The server stopped while it loaded the campaign. The server says " + ServerStateEvent.ShortDetail(state.Detail),
            "What to do: Click Start server again. If it stops the same way, click Send server crash report.",
            "What not to do: " + KeepSaves + " Do not click Prepare server a second time."
        ],
        status,
        true);
    }

    private static Report? SaveMismatch(string text)
    {
        var match = Regex.Match(text, @"\bsave(?: file)? '([^']+)' was not found or is incompatible", RegexOptions.IgnoreCase);
        if (!match.Success)
            return null;
        var name = Path.GetFileNameWithoutExtension(match.Groups[1].Value);
        return new Report(
        [
            "What happened: The server stopped before anyone could join. The campaign save named " + name + " is not in the server's Game Saves folder, or it was made with other mods. A save remembers the modules and the Burning Empires version it was made with.",
            "What to do: Pick a Burning Empires " + BeServerPath.Short(BeServerPath.BeVersion) + " save that is in the server's Game Saves folder, or click New campaign.",
            "What not to do: Do not start a vanilla save. Do not delete the single player save or your Burning Empires save data."
        ],
        SaveMismatchStatus,
        true);
    }

    /// <summary>The status when Start-ModdedServer.ps1 would not open the save (BeServerLaunch.ScriptRefusal too).</summary>
    public const string SaveMismatchStatus = "That save does not match this server. Pick a Burning Empires save, or click New campaign.";

    // The script prints "Active load order:" with one numbered line per module.
    private static Report? WrongOrder(string text)
    {
        var start = text.LastIndexOf("Active load order:", StringComparison.OrdinalIgnoreCase);
        if (start < 0)
            return null;
        var modules = new List<string>();
        foreach (var line in text[start..].Replace("\r", "").Split('\n').Skip(1))
        {
            var match = Regex.Match(line, @"^\s*\d+\.\s+(?<id>\S.*?)\s*$");
            if (!match.Success)
                break;
            modules.Add(match.Groups["id"].Value);
        }

        var problem = modules.Count == 0 ? null : BeServerPath.OrderProblem(modules);
        if (problem == null)
            return null;
        return new Report(
        [
            "What happened: The server started with the Burning Empires modules in the wrong order. " + problem,
            "What to do: Click Start server again. This tool writes the right order on every start. If the order is still wrong, pick the Burning Empires setup on the Mods page.",
            "What not to do: Do not edit modded-server-modules.txt by hand."
        ],
        "The module order on the server is wrong. " + problem,
        true);
    }

    private static Report? FailedLoad(string text)
    {
        var match = Regex.Match(text, @"\b(?<id>[A-Za-z0-9_.]+) could not be loaded correctly", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!match.Success)
            return null;
        var name = PlainName(match.Groups["id"].Value);
        return new Report(
        [
            "What happened: The server stopped because " + name + " did not load.",
            "What to do: Close Bannerlord if it is open, then click Start server again. If the same mod fails, that copy in the server folder is damaged. Click Prepare server to copy it again.",
            "What not to do: Do not add a second copy of the module. Do not turn on War Sails."
        ],
        name + " did not load.",
        true);
    }

    private static Report? Dependency(string text)
    {
        var match = Regex.Match(text, @"\b(?<mod>[A-Za-z0-9_.]+)\s+depends on(?: module)?\s+(?<dep>[A-Za-z0-9_.]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!match.Success)
            return null;
        var owner = PlainName(match.Groups["mod"].Value);
        var needed = PlainName(match.Groups["dep"].Value);
        // Install mods gets Harmony, ButterLib and UIExtenderEx from Steam; MCM only comes from Nexus Mods.
        var mcm = match.Groups["dep"].Value.Trim().Equals(BeStack.Mcm.ModuleId, StringComparison.OrdinalIgnoreCase);
        return new Report(
        [
            "What happened: The server stopped because " + owner + " needs " + needed + ". " + needed + " is not loaded.",
            mcm
                ? "What to do: Get MCM " + BeStack.Mcm.TestedVersion + " or newer from Nexus Mods and put it in the game's Modules folder. Then click Start server again. " + BeServerPath.McmOnNexus
                : "What to do: Add " + needed + " to the setup, or turn " + owner + " off. Then click Start server again. Burning Empires needs " + HelperMods + ". Click Install mods on the Mods page for Harmony, ButterLib and UIExtenderEx, and get MCM from Nexus Mods.",
            "What not to do: Do not take the four helper mods out of the list."
        ],
        owner + " needs " + needed + ".",
        true);
    }

    private static Report? Duplicate(string text)
    {
        var match = Regex.Match(text, @"module(?: id)?\s+(?<id>[A-Za-z0-9_.]+)\s+is already", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!match.Success)
            return null;
        var name = PlainName(match.Groups["id"].Value);
        return new Report(
        [
            "What happened: The server stopped because " + name + " is in more than one folder. The server can only use one copy.",
            "What to do: Move the extra folder out of the server Modules folder. Then click Start server again.",
            "What not to do: Do not delete your Burning Empires save data. Do not make a second Coop folder inside Modules."
        ],
        name + " is in more than one folder.",
        true);
    }

    private static Report? Exception(string text)
    {
        var modules = BeServerPath.CoreIds;
        var lines = text.Replace("\r", "").Split('\n');
        for (var i = lines.Length - 1; i >= 0; i--)
        {
            // A whole word that ends in Exception. A module name such as BetterExceptionWindow is not one.
            if (!Regex.IsMatch(lines[i], @"Exception\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) || IsHarmless(lines[i]))
                continue;
            var window = string.Join(" ", lines.Skip(i).Take(6));
            var module = modules.FirstOrDefault(id => Regex.IsMatch(window, @"\b" + Regex.Escape(id) + @"\b", RegexOptions.IgnoreCase));
            if (module == null)
                continue;
            var quote = Clean(lines[i]);
            return new Report(
            [
                "What happened: The server stopped while it was starting. The log points at " + PlainName(module) + ". The line is \"" + quote + "\"",
                "What to do: Click Start server again. If it stops on the same mod, click Prepare server to copy the server files again. If it still stops, click Send server crash report.",
                "What not to do: " + KeepSaves + " Do not change the module order by hand."
            ],
            "The server stopped while it was starting.",
            true);
        }

        return null;
    }

    /// <summary>The plain name for a module id of the Burning Empires set; any other id as it is.</summary>
    public static string PlainName(string moduleId)
    {
        var id = moduleId.Trim();
        var butr = BeStack.Butr.FirstOrDefault(mod => mod.ModuleId.Equals(id, StringComparison.OrdinalIgnoreCase));
        if (butr != null)
            return butr.Name;
        if (id.Equals(BeStack.BeModuleId, StringComparison.OrdinalIgnoreCase))
            return BeStack.BeName;
        if (id.Equals(BeStack.ElephantsModuleId, StringComparison.OrdinalIgnoreCase))
            return BeStack.ElephantsName;
        if (id.Equals(BeStack.CompatModuleId, StringComparison.OrdinalIgnoreCase))
            return BeStack.CompatName;
        return id;
    }

    private static readonly Regex ServerDllLine = new(@"Could not load file or assembly '(?<name>(?:BurningEmpires[A-Za-z.]*|Bannerlord\.UIExtenderEx|TaleWorlds\.MountAndBlade\.CustomBattle))[,']", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// The line the tool adds to the run's console text when the server process exits. BE starts
    /// PowerShell with Start-ModdedServer.ps1, which runs the engine itself and ends with a bare
    /// exit of the engine's code, so nothing on this route prints an "exited with code" line the way
    /// BannerlordCoopServer.exe does. Without it the exit reports below never fired.
    /// </summary>
    public static string ExitLine(int code) => "[BE host] engine exited with code " + code.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static int? ExitCode(string text)
    {
        var match = Regex.Match(text, @"(?:engine|process) exited with code\s+(-?\d+)", RegexOptions.IgnoreCase | RegexOptions.RightToLeft);
        return match.Success && int.TryParse(match.Groups[1].Value, out var code) ? code : null;
    }

    // Lines that look like an error but are not why a server stopped. Live run 2 named these after
    // a server was ended by hand: .NET looking for the optional pregenerated
    // BurningEmpires.XmlSerializers, the Seq log sink on localhost:5341 that is not there, and a
    // TargetInvocationException with no inner exception on its line (the inner one, on its own
    // ---> line, still counts). The compat module's Crash trace notes and the "at ..." lines of a
    // stack are not a cause either.
    private static bool IsHarmless(string line) =>
        IsNoise(line)
        || line.Contains("XmlSerializers", StringComparison.OrdinalIgnoreCase)
        || line.Contains(":5341", StringComparison.Ordinal)
        || (line.Contains("TargetInvocationException", StringComparison.Ordinal) && !line.Contains("--->", StringComparison.Ordinal)
            && line.Contains("Exception has been thrown by the target of an invocation", StringComparison.OrdinalIgnoreCase))
        || line.Contains("Crash trace:", StringComparison.Ordinal)
        || StackFrame.IsMatch(line);

    // "   at X.Y", also after the console log's time and a [tag].
    private static readonly Regex StackFrame = new(@"^\s*(?:\d{2}:\d{2}:\d{2}(?:\.\d+)?\s+)?(?:\[[^\]]*\]\s*)*at\s+\S", RegexOptions.CultureInvariant);

    // The start script's own PowerShell error record never names a module (StartScriptOutput).
    private static bool IsNoise(string line) =>
        BannerlordCoopLauncher.Core.Support.StartScriptOutput.IsPowerShellRecord(line)
        || line.Contains("FMOD", StringComparison.OrdinalIgnoreCase)
        || line.Contains("Serilog", StringComparison.OrdinalIgnoreCase)
        || line.Contains("AccessTools", StringComparison.OrdinalIgnoreCase)
        || line.Contains("ReflectionTypeLoadException", StringComparison.OrdinalIgnoreCase)
        || line.Contains("_MODULES_", StringComparison.Ordinal)
        // The compat module logs every first chance exception on the server; most are caught.
        || line.Contains("First chance", StringComparison.OrdinalIgnoreCase);

    private static string Clean(string line)
    {
        var value = Regex.Replace(line.Trim(), @"^\[[^\]]+\]\s*", "");
        value = Regex.Replace(value, @"^\d{2}:\d{2}:\d{2}(?:\.\d+)?\s*", "");
        return value.Length <= 180 ? value : value[..177] + "...";
    }
}
