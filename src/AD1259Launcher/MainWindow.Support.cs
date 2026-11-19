using System.Diagnostics;
using System.IO;
using System.Windows;
using BannerlordCoopLauncher.Core.AD1259;
using BannerlordCoopLauncher.Core.Launch;
using BannerlordCoopLauncher.Core.Modules;

namespace AD1259Launcher;

public partial class MainWindow
{
    // The game closed before it joined: read its crash report and engine log and say what to change.
    private async Task ExplainEarlyExitAsync(Process started)
    {
        int? exitCode = null;
        try
        {
            exitCode = started.ExitCode;
        }
        catch (InvalidOperationException)
        {
        }

        var documents = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Mount and Blade II Bannerlord");
        await Task.Delay(1500);
        var capture = await Task.Run(() => StartupCrashExplain.Read(documents, started.Id));
        NoteBlockedDll(capture.ReportText + "\n" + capture.GameLog);
        var lines = StartupCrashExplain.Explain(
            false,
            capture.ExitCode ?? exitCode,
            capture.ReportText,
            capture.GameLog,
            Array.Empty<StartupCrashExplain.ModName>(),
            AD1259Stack.ClientModules.ToList(),
            setupUsesWorkshop: true);
        if (!StartupCrashExplain.SawStartupCrash(lines))
            return;
        foreach (var line in WorkshopTwins.PointCrashAt(lines, FindWorkshopTwins(capture.GameLog)))
            Log(line);
        ShowCrashReport(capture);
    }

    private string _twinsNoted = "";

    // A mod of this setup in two Workshop items: the Duplicate row already blocks Connect,
    // and this line says which item the game loads and which one to unsubscribe.
    private IReadOnlyList<WorkshopTwin> FindWorkshopTwins(string? gameLog = null)
    {
        var preferred = _mods
            .Where(status => status.Mod.WorkshopId != null)
            .GroupBy(status => status.Mod.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Mod.WorkshopId!, StringComparer.OrdinalIgnoreCase);
        return WorkshopTwins.Find(WorkshopRoots(), AD1259Stack.ClientModules, preferred,
            () => gameLog, () => WorkshopTwins.LatestClientLog(StartupCrashExplain.EngineLogDirectory()));
    }

    private void NoteWorkshopTwins()
    {
        var lines = FindWorkshopTwins().Select(WorkshopTwins.Describe).ToList();
        var key = string.Join("|", lines);
        if (key == _twinsNoted)
            return;
        _twinsNoted = key;
        foreach (var line in lines)
            Log(line);
    }
}
