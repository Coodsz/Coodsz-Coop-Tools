using System.IO;
using BannerlordCoopLauncher.Core.Modules;
using BannerlordCoopLauncher.Core.Server;
using BannerlordCoopLauncher.Core.Support;
using CoodszHost.Services;

namespace CoodszHost;

// What a server exit nobody asked for turns out to be, before the crash bar and the save retry:
// CoopModPatch's own stop after its first time setup (no crash), a mod's raw Assets folder the
// engine died on while loading packages (hidden now, not the save), and an exit code 1 with no
// error (the action log says whether this tool ended the process). The last crash is kept so the
// crash report can add the Windows event log entries around it.
public partial class MainWindow
{
    private readonly ModPatchSetup.Watch _modPatchSetup = new();
    private DateTime? _lastCrashAt;
    private bool _saveRefused;

    // The server's own FATAL line of this run, for the exit explanation: the window keeps only its last lines.
    private string? _fatalLine;

    // OnServerExited: the start script refused the save (the mods that are on do not match it).
    // The game never started, so the deeper save fixes and the crash restart cannot help.
    private bool IsSaveRefusedStop(ServerExitInfo exit)
    {
        if (exit.WasExpected || !_saveRefused)
            return false;
        _actions.Note("Exit", "The start script refused the save because the mods that are on do not match it. Not a crash: no save retry, no restart.");
        SetControl(SaveRefusedText, failed: true);
        return true;
    }

    private const string SaveRefusedText = "The save does not match the mods or the mod versions that are on, so the server did not open it. The Logs page says what to do.";

    private bool _mapMissing;

    // OnServerExited: the start script found no Main_map from the picked map mod and stopped, instead of serving a
    // plain Calradia world every join crashes on (MissingCampaignMap). Not a crash: no bar, no save retry, no restart.
    private bool IsMapMissingStop(ServerExitInfo exit)
    {
        if (!MissingCampaignMap.IsScriptStop(_mapMissing, exit.WasExpected))
            return false;
        _actions.Note("Exit", "The start script found no campaign map files from the picked map mod and stopped (exit code " + exit.ExitCode + "). Not a crash: no save retry, no restart.");
        // The script stopped before the engine made the world, so a new campaign is still new for the next Start.
        KeepNewCampaignIfNotSaved();
        Append(MissingCampaignMap.ScriptStopText);
        SetControl(MissingCampaignMap.ScriptStopText, failed: true);
        return true;
    }

    // OnServerExited: CoopModPatch ended the server with code 1 after its first time setup.
    private bool IsModPatchSetupStop(ServerExitInfo exit)
    {
        if (exit.WasExpected || !_modPatchSetup.Finished(exit.ExitCode))
            return false;
        _actions.Note("Exit", "CoopModPatch finished its first time setup and stopped the server on purpose (exit code 1).");
        KeepNewCampaignIfNotSaved();
        Append(ModPatchSetup.Text);
        SetControl(ModPatchSetup.Text, done: true);
        return true;
    }

    // Start marks a new campaign as made when the server starts, but CoopModPatch's first time setup stops it
    // before the campaign is saved, so the next Start found no save and refused (v1.3.6 live test on a fresh
    // Coop 0.1.7 server folder). Any other stop nobody asked for before the first save (a crash at load) is the
    // same. With no save yet, the campaign is still new for the next Start.
    private void KeepNewCampaignIfNotSaved()
    {
        var profile = _settings.Current();
        if (!_runCreatesCampaign || _launchedSave.Length == 0
            || !string.Equals(profile.SaveName, _launchedSave, StringComparison.OrdinalIgnoreCase)
            || File.Exists(Path.Combine(ServerSession.SavesPath, _launchedSave + ".sav")))
            return;
        profile.NewCampaign = true;
        HostSettings.Save(SettingsPath, _settings);
        _actions.Note("Exit", "The new campaign " + _launchedSave + " was not saved yet, so the next Start makes it.");
    }

    // OnServerExited: the engine died (0xC0000005) right after "Loading packages .../X/Assets".
    // The raw Assets folder of X is hidden in the server copy at once, so the next Start works.
    // Returns the mod folder, or null when this crash is something else.
    private string? HideCrashedAssets(ServerExitInfo exit)
    {
        if (!exit.IsCrash || exit.ExitCode != ServerCrashExplain.AccessViolationCode)
            return null;
        string? module;
        try
        {
            var logs = ServerCrashExplain.Read("", _session.StartedAt, ServerCrashExplain.EngineLogDirectory());
            module = ServerCrashExplain.FindAssetsCrash(exit.ExitCode, logs.EngineLog);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        if (module == null)
            return null;
        var hidden = ServerRawAssets.Note(ServerRawAssets.Hide(ModuleCatalog.ModulesRoot(ServerRoot), [module]));
        if (hidden != null)
            Append(hidden);
        _actions.Note("Exit", "The raw Assets folder of " + module + " crashed the server while it loaded packages. "
            + (hidden != null ? "Hid it in the server copy." : "It was already hidden or could not be renamed.") + " No save retry.");
        return module;
    }

    // OnServerExited: an exit code 1 nobody asked for. Whether this tool ended the process goes to
    // the action log, since the server's own logs say nothing in that case.
    // The time and that line are kept for host/exit-evidence.txt in the log pack (crash repo #12).
    private void NoteSilentExit(ServerExitInfo exit)
    {
        if (exit.WasExpected || exit.ExitCode != ServerCrashExplain.SilentExitCode)
            return;
        var line = _session.KilledThisRun
            ? "Coodsz Coop Server Tool ended the server process itself (Stop or the engine clean up), although no stop was asked for."
            : "Coodsz Coop Server Tool did not end the server process and no stop was asked for. Something outside this tool closed it, or it ended itself.";
        _actions.Note("Exit code 1", line);
        _silentExit = (DateTime.Now, line + " Coodsz Coop Server Tool did not restart it before it ended"
            + (_settings.Current().RestartOnCrash ? "; Restart if it crashes is on, so it may start it again after this exit." : "; Restart if it crashes is off."));
    }

    private (DateTime At, string HostLine)? _silentExit;

    // host/exit-evidence.txt: the event logs around the exit code 1, what this tool did, and the
    // last minute of the engine log. Read when the zip is made, so entries written after the exit count.
    private string? SilentExitEvidence()
    {
        if (_silentExit is not { } exit)
            return null;
        var entries = ServerExitEvidence.Select(WindowsEventLog.ExitEntries(exit.At, out var unread), exit.At);
        string? engine = null;
        string? engineErrors = null;
        try
        {
            engine = ServerExitEvidence.EngineLastMinute(ServerCrashExplain.Read("", exit.At.AddMinutes(-30), ServerCrashExplain.EngineLogDirectory()).EngineLog);
            engineErrors = ServerCrashExplain.EngineErrorsTail(ServerCrashExplain.EngineLogDirectory(), exit.At.AddMinutes(-30));
        }
        catch (Exception)
        {
            // The evidence goes without the engine log; it must never sink the whole log pack.
        }

        return ServerExitEvidence.Format(exit.At, exit.HostLine, unread.Count == ServerExitEvidence.Logs.Count ? null : entries, unread, engine, engineErrors);
    }

    // The crash report of the last crash also carries the Windows Application event log entries
    // around it and the end of the engine log. Reading either can fail; then it is left out.
    private IReadOnlyList<(string Entry, string Text)> CrashExtras()
    {
        var extras = new List<(string Entry, string Text)>();
        var evidence = SilentExitEvidence();
        if (evidence != null)
            extras.Add((ServerExitEvidence.EntryName, evidence));
        if (_lastCrashAt is not { } exitAt)
            return extras;
        var events = WindowsEventLog.ServerEntries(exitAt);
        if (events != null)
            extras.Add((ServerEventLog.EntryName, events));
        try
        {
            var tail = ServerEventLog.Tail(ServerCrashExplain.Read("", exitAt.AddMinutes(-30), ServerCrashExplain.EngineLogDirectory()).EngineLog);
            if (tail != null)
                extras.Add((ServerEventLog.EngineTailName, tail));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }

        return extras;
    }
}
