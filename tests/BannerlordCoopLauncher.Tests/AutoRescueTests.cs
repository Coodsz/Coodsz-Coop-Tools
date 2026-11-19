using BannerlordCoopLauncher.Core.Modules;
using BannerlordCoopLauncher.Core.Saves;
using BannerlordCoopLauncher.Core.Server;
using BannerlordCoopLauncher.Core.Support;

namespace BannerlordCoopLauncher.Tests;

// Coop Save Rescue on Start, Restart and Stop: the module id players never need, the notice read
// from its FIXED and Report lines, one retry after a load crash, and the warning signs read on Stop.
public class AutoRescueTests
{
    private static readonly string[] Plain = { "Native", "SandBoxCore", "Sandbox", "Coop", "SomeMod" };

    [Fact]
    public void The_rescue_module_is_left_out_of_the_join_check_and_the_save_check()
    {
        Assert.True(CoopJoinRules.IsIgnored(AutoRescue.ModuleId));
        Assert.True(CoopJoinRules.IsIgnored(AutoRescue.LegacyModuleId));
        // A player without it joins a server that loads it.
        Assert.Null(CoopJoinRules.JoinProblem(Plain, Plain.Append(AutoRescue.ModuleId)));
        // A save made while it was loaded starts without it, and an old save starts with it.
        Assert.Equal(SaveModAction.Start, SaveModChange.Check(Plain.Append(AutoRescue.ModuleId).ToList(), Plain).Action);
        Assert.Equal(SaveModAction.Start, SaveModChange.Check(Plain.Append(AutoRescue.LegacyModuleId).ToList(), Plain).Action);
        Assert.Equal(SaveModAction.Start, SaveModChange.Check(Plain, Plain.Append(AutoRescue.ModuleId)).Action);
        Assert.Null(CoopJoinRules.SaveProblem(Plain.Append(AutoRescue.ModuleId).ToList(), Plain));
        Assert.True(AutoRescue.IsModule("dedicatedserver.coopsaverescue"));
        Assert.False(AutoRescue.IsModule("Coop"));
    }

    [Fact]
    public void A_run_collects_the_fixed_lines_once_and_the_first_report()
    {
        var run = new RescueRun();
        run.Begin(RescueMode.Auto);
        Assert.Equal(RescueRun.Seen.Nothing, run.Observe("14:39:00.100  [CoopSaveRescue] Ready in auto mode (7 of 7 checks active)."));
        Assert.Equal(RescueRun.Seen.Nothing, run.Observe("[CoopSaveRescue] Checked 12 battles for an empty side; 1 found."));
        Assert.Equal(RescueRun.Seen.Fixed, run.Observe("14:39:20.500  [CoopSaveRescue] FIXED: Closed empty battle 'siege' at Pravend."));
        // The same line also comes from the coop-server log.
        Assert.Equal(RescueRun.Seen.Fixed, run.Observe("[CoopSaveRescue] FIXED: Closed empty battle 'siege' at Pravend."));
        run.Observe("[CoopSaveRescue] FIXED: Freed 3 parties from a battle that had already ended.");
        Assert.Equal(RescueRun.Seen.Report, run.Observe("14:40:00.000  [CoopSaveRescue] Report: fixed 2 problem(s). The next save keeps the fixes."));
        Assert.Equal(RescueRun.Seen.Nothing, run.Observe("[CoopSaveRescue] Report: fixed 2 problem(s). The next save keeps the fixes."));
        Assert.Equal(new[] { "Closed empty battle 'siege' at Pravend.", "Freed 3 parties from a battle that had already ended." }, run.Fixes);
        Assert.Equal("fixed 2 problem(s). The next save keeps the fixes.", run.Report);
        Assert.False(AutoRescue.FoundNothing(run.Report!));
        Assert.Equal("Coop Save Rescue fixed 2 problems in this save.", AutoRescue.FixedTitle(run.Fixes.Count));
        Assert.Equal("Coop Save Rescue fixed a problem in this save.", AutoRescue.FixedTitle(1));

        // The next run starts empty.
        run.Begin(RescueMode.Load);
        Assert.Empty(run.Fixes);
        Assert.False(run.Reported);
        Assert.True(run.AfterLoadCrash);
        Assert.Null(AutoRescue.FixedText("[CoopSaveRescue] FIXED:   "));
        Assert.Null(AutoRescue.FixedText("FIXED: not the rescue module"));
    }

    [Fact]
    public void A_load_crash_gets_one_retry_per_start_by_hand()
    {
        var crash = ServerExitInfo.From(-1073741819, wasExpected: false);
        var retry = new LoadCrashRetry();
        Assert.True(retry.Take(crash, servedThisRun: false, RescueMode.Auto));
        // The retry itself crashed (load mode), or a crash restart crashed again: no second retry.
        Assert.False(retry.Take(crash, servedThisRun: false, RescueMode.Load));
        Assert.False(retry.Take(crash, servedThisRun: false, RescueMode.Auto));
        retry.ManualStart();
        Assert.True(retry.Take(ServerExitInfo.From(ServerExit.LoadFailureCode, false), false, RescueMode.Auto));

        retry.ManualStart();
        // Not a load crash: it was serving, the host stopped it, a clean exit, Coop files or the terms.
        Assert.False(retry.Take(crash, servedThisRun: true, RescueMode.Auto));
        Assert.False(retry.Take(ServerExitInfo.From(-1, wasExpected: true), false, RescueMode.Auto));
        Assert.False(retry.Take(ServerExitInfo.From(0, false), false, RescueMode.Auto));
        Assert.False(retry.Take(ServerExitInfo.From(ServerExit.ModuleVerificationFailedCode, false), false, RescueMode.Auto));
        Assert.False(retry.Take(ServerExitInfo.From(ServerExit.EulaRequiredCode, false), false, RescueMode.Auto));
        // The manual full run already has every fix.
        Assert.False(retry.Take(crash, false, RescueMode.Full));
        Assert.False(retry.Used);
        Assert.Equal("load", AutoRescue.ModeValue(RescueMode.Load));
        Assert.Equal("full", AutoRescue.ModeValue(RescueMode.Full));
        Assert.Equal("auto", AutoRescue.ModeValue(RescueMode.Auto));
    }

    [Fact]
    public void Load_mode_is_chosen_once_after_a_load_crash_and_its_own_crash_is_the_end()
    {
        var crash = ServerExitInfo.From(-1073741819, wasExpected: false);
        var retry = new LoadCrashRetry();

        // An auto start that crashed while the save loaded: one more start in load mode.
        Assert.Equal(LoadCrashStep.RetryInLoadMode, retry.Decide(crash, servedThisRun: false, RescueMode.Auto));
        Assert.True(retry.Used);
        // That load mode start crashed too: the host is told and nothing starts again, not even the crash restart.
        Assert.Equal(LoadCrashStep.GaveUp, retry.Decide(crash, servedThisRun: false, RescueMode.Load));
        // A crash restart start (auto) that crashes on load after the retry was used: the crash restart decides.
        Assert.Equal(LoadCrashStep.LeftToCrashRestart, retry.Decide(crash, servedThisRun: false, RescueMode.Auto));

        // A start by hand gives one retry again.
        retry.ManualStart();
        Assert.False(retry.Used);
        // Code 2 (the save could not load) is a load crash too.
        Assert.Equal(LoadCrashStep.RetryInLoadMode, retry.Decide(ServerExitInfo.From(ServerExit.LoadFailureCode, false), false, RescueMode.Auto));

        retry.ManualStart();
        // The load mode start served and crashed later while playing: an ordinary crash.
        Assert.Equal(LoadCrashStep.NotALoadCrash, retry.Decide(crash, servedThisRun: true, RescueMode.Load));
        // Stop by the host, a clean exit, changed Coop files and the server terms are no load crash.
        Assert.Equal(LoadCrashStep.NotALoadCrash, retry.Decide(ServerExitInfo.From(-1, wasExpected: true), false, RescueMode.Auto));
        Assert.Equal(LoadCrashStep.NotALoadCrash, retry.Decide(ServerExitInfo.From(0, false), false, RescueMode.Auto));
        Assert.Equal(LoadCrashStep.NotALoadCrash, retry.Decide(ServerExitInfo.From(ServerExit.ModuleVerificationFailedCode, false), false, RescueMode.Auto));
        Assert.Equal(LoadCrashStep.NotALoadCrash, retry.Decide(ServerExitInfo.From(ServerExit.EulaRequiredCode, false), false, RescueMode.Auto));
        // The manual full run already has every fix, and a new campaign had no save to fix.
        Assert.Equal(LoadCrashStep.LeftToCrashRestart, retry.Decide(crash, false, RescueMode.Full));
        Assert.Equal(LoadCrashStep.LeftToCrashRestart, retry.Decide(crash, false, RescueMode.Auto, loadedASave: false));
        // Neither of those used up the retry.
        Assert.False(retry.Used);
        Assert.Equal(LoadCrashStep.RetryInLoadMode, retry.Decide(crash, false, RescueMode.Auto));
    }

    [Fact]
    public void A_load_mode_run_reports_like_any_run_and_says_it_came_after_the_crash()
    {
        var run = new RescueRun();
        run.Begin(RescueMode.Load);
        Assert.True(run.AfterLoadCrash);
        Assert.Equal(RescueRun.Seen.Nothing, run.Observe("[CoopSaveRescue] Ready in load mode (7 of 7 checks active, plus the empty-battle and ended-battle checks after load)."));
        Assert.Equal(RescueRun.Seen.Fixed, run.Observe("[CoopSaveRescue] FIXED: Hero lord_1_1 failed to load: NullReferenceException: x. Skipped it so the rest of the save loads."));
        Assert.Equal(RescueRun.Seen.Report, run.Observe("[CoopSaveRescue] Report: fixed 1 problem(s). The next save keeps the fixes."));
        Assert.Single(run.Fixes);

        // The next start by hand is auto again.
        run.Begin(RescueMode.Auto);
        Assert.False(run.AfterLoadCrash);
    }

    [Fact]
    public void The_module_takes_load_mode_from_the_tools_and_only_then_skips_a_hero_that_fails_to_load()
    {
        var module = File.ReadAllText(Path.Combine(RepoRoot(), "compat", "CoopSaveRescue", "SubModule.cs")).Replace("\r\n", "\n");
        var readMode = SourceFiles.Method(module, "private static RescueMode ReadMode()");
        // The variable the tools set wins; the mode file is only read when it is not set.
        Assert.True(readMode.IndexOf("GetEnvironmentVariable(ModeVariable)", StringComparison.Ordinal)
            < readMode.IndexOf("if (string.IsNullOrWhiteSpace(value))", StringComparison.Ordinal));
        Assert.Contains("case \"load\": return RescueMode.Load;", readMode);
        Assert.Contains("default: return RescueMode.Auto;", readMode);
        Assert.Equal("load", AutoRescue.ModeValue(RescueMode.Load));
        // The hero skip (a finalizer on Hero.AfterLoad) is on in load and full mode, never in auto.
        Assert.Contains("_mode == RescueMode.Auto ? null : nameof(HeroAfterLoadFinalizer)", module);
        // Load mode closes a broken battle after 3 failures like auto; only full closes it on the first.
        Assert.Contains("CloseAfterFailures => _mode == RescueMode.Full ? 1 : 3;", module);

        // Both tools set the variable on every start, so a stray mode file never decides the mode.
        var host = SourceFiles.Window(RepoRoot(), "CoodszHost");
        Assert.Contains("_rescueNextMode = RescueMode.Load;", SourceFiles.Method(host, "private async Task StartAfterLoadCrashAsync()"));
        Assert.Contains("_loadRetry.Decide(info, _servedThisRun, _rescueRun.Mode, loadedASave: !_runCreatesCampaign)", host);
        Assert.Contains("_runCreatesCampaign = createNew;", SourceFiles.Method(host, "private void StartServer()"));
        var taom = SourceFiles.Window(RepoRoot(), "TaomCoopHost");
        Assert.Contains("_rescueNextMode = RescueMode.Load;", SourceFiles.Method(taom, "private async Task StartAfterLoadCrashAsync()"));
        Assert.Contains("_loadRetry.Decide(exit, _servedThisRun, _rescueRun.Mode)", taom);
    }

    [Fact]
    public void Stop_finds_the_warning_signs_the_fixer_handles()
    {
        const string log = """
            10:00:01.000 [WRN] Releasing 3 parties from ended map event created_257585
            10:00:02.000 [WRN] Releasing 2 parties from ended map event created_1
            10:01:00.000 [WRN] Detaching 4 parties from half-finalized loaded player map event created_9
            10:02:00.000 [ERR] Failed to auto-finalize concluded map event
            10:03:00.000 [CoopSaveRescue] Battle 'field battle' failed its update: NullReferenceException: x
            """;
        Assert.Equal(new[]
        {
            "A battle in the save was left half finished.",
            "A battle could not finish.",
            "A battle failed to update, so the map can freeze.",
        }, AutoRescue.StopScan(log));

        // A battle the fixer already closed in this run is not a problem any more.
        Assert.Empty(AutoRescue.StopScan("[CoopSaveRescue] Battle 'x' failed its update: boom\n[CoopSaveRescue] FIXED: Closed broken battle 'x'."));
        // Only the fixer's own line counts for a failed update, and a quiet log has nothing.
        Assert.Empty(AutoRescue.StopScan("some mod: the item failed its update check"));
        Assert.Empty(AutoRescue.StopScan("10:00 SERVING\n10:05 world saved to 'calradia.sav'"));
        Assert.Empty(AutoRescue.StopScan(null));
    }

    [Fact]
    public void Stop_reads_the_lines_as_the_host_console_log_has_them()
    {
        // host-console-*.log of a live run (2026-10-22): Coop's warnings come through its console sink
        // with a [Coop] prefix, and every fixer line also comes once more from the engine.
        const string log = """
            19:39:51.528  [DedicatedServer Engine] [CoopSaveRescue] Checked 0 battles for an empty side; 0 found.
            19:39:51.528  [CoopSaveRescue] Checked 0 battles for an empty side; 0 found.
            19:40:01.529  [CoopSaveRescue] Checked parties for links to an ended battle; 0 freed.
            19:40:26.532  [CoopSaveRescue] Report: nothing to fix was found. This save does not have the problems this module knows.
            19:31:47.841  [Coop] Rejected vassal service request for clan "Clan_Player" because it is no longer eligible
            19:35:00.000  [Coop] Operator unstuck for controller 7656 (hero Hero_Player, party MobileParty_Player): Released 2 parties still linked to a battle that already ended.
            """;
        Assert.Empty(AutoRescue.StopScan(log));

        const string broken = """
            19:41:00.100  [Coop] Releasing 3 parties from ended map event "created_257585"
            19:41:00.200  [Coop] Detaching 2 parties from half-finalized loaded player map event "created_9"
            19:42:00.000  [DedicatedServer Engine] [CoopSaveRescue] Battle 'Battle: Alary's Party x Utred's Party' failed its update: NullReferenceException: Object reference not set to an instance of an object.
            19:42:00.000  [CoopSaveRescue] Battle 'Battle: Alary's Party x Utred's Party' failed its update: NullReferenceException: Object reference not set to an instance of an object.
            """;
        Assert.Equal(new[]
        {
            "A battle in the save was left half finished.",
            "A battle failed to update, so the map can freeze.",
        }, AutoRescue.StopScan(broken));
    }

    [Fact]
    public void Stop_counts_the_parties_Coop_already_freed_as_a_note_not_a_problem()
    {
        // coop.unstuck (Coop #4070) and the load cleaner (#3910) print this after they freed the parties.
        // The same battle can be in the server log and in the console log, so it counts once.
        const string log = """
            19:41:00.100  [Coop] Releasing 3 parties from ended map event "created_257585"
            19:41:00.200  [WRN] Releasing 3 parties from ended map event created_257585
            19:41:00.300  [Coop] Releasing 2 parties from ended map event "created_1"
            19:45:00.000  SERVING
            """;
        Assert.Empty(AutoRescue.StopScan(log));
        Assert.Equal(5, AutoRescue.PartiesFreedByCoop(log));
        Assert.Equal("Coop freed 5 stuck parties this run.", AutoRescue.FreedByCoopTitle(5));
        Assert.Equal("Coop freed 1 stuck party this run.", AutoRescue.FreedByCoopTitle(1));

        Assert.Equal(0, AutoRescue.PartiesFreedByCoop("10:00 SERVING"));
        Assert.Equal(0, AutoRescue.PartiesFreedByCoop(null));
        // A real problem next to it is still a problem.
        Assert.Equal(new[] { "A battle could not finish." },
            AutoRescue.StopScan(log + "\n10:02:00.000 [ERR] Failed to auto-finalize concluded map event"));
    }

    [Fact]
    public void The_backup_from_before_the_fixes_outlives_the_autosave_backups()
    {
        var root = Path.Combine(Path.GetTempPath(), "coodsz-rescue-backup-" + Guid.NewGuid().ToString("N"));
        var saves = Path.Combine(root, "Game Saves");
        Directory.CreateDirectory(saves);
        try
        {
            File.WriteAllText(Path.Combine(saves, "Calradia.sav"), "broken");
            File.WriteAllText(Path.Combine(saves, "Calradia.json"), "{}");
            var service = new BackupService(saves, Path.Combine(root, "Backups"), new BackupPolicy(MaxCount: 3, MaxAgeDays: 0));
            var before = service.Create("Calradia", BackupReason.BeforeRescue);
            Assert.EndsWith("_prerescue.zip", before.FilePath);
            for (var i = 0; i < 5; i++)
                service.Create("Calradia", BackupReason.Autosave);
            var list = service.List("Calradia");
            Assert.Equal(3, list.Count);
            Assert.Contains(list, entry => entry.Reason == BackupReason.BeforeRescue);
            Assert.Equal(2, list.Count(entry => entry.Reason == BackupReason.Autosave));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Start_runs_the_fixer_and_Rescue_save_moved_to_the_Logs_page()
    {
        var host = SourceFiles.Window(RepoRoot(), "CoodszHost");
        var start = SourceFiles.Method(host, "private void StartServer()");
        Assert.Contains("_rescueRun.Begin(_rescueNextMode);", start);
        Assert.Contains("InstallRescue()", start);
        Assert.Contains("Rescue = _rescueRun.Mode", start);
        Assert.Contains("start.Environment[AutoRescue.EnvironmentVariable] = AutoRescue.ModeValue(options.Rescue);",
            File.ReadAllText(Path.Combine(RepoRoot(), "src", "CoodszHost", "Services", "ServerSession.cs")));
        Assert.Contains("ScanAfterStop(startedAt, consoleLog);",SourceFiles.Method(host, "private async void Stop_Click(object sender, RoutedEventArgs e)"));
        Assert.Contains("RetryAfterLoadCrash(info, loadStep, crashLine)", SourceFiles.Method(host, "private void OnServerExited(ServerExitInfo exit)"));

        var taom = SourceFiles.Window(RepoRoot(), "TaomCoopHost");
        Assert.Contains("start.Environment[AutoRescue.EnvironmentVariable] = AutoRescue.ModeValue(_rescueNextMode);", taom);
        Assert.Contains("if (!InstallRescue(server))", taom);
        Assert.DoesNotContain("TakeRescueOff", taom);

        foreach (var project in new[] { "CoodszHost", "TaomCoopHost", "BurningEmpiresHost" })
        {
            var xaml = File.ReadAllText(Path.Combine(RepoRoot(), "src", project, "MainWindow.xaml"));
            // Off the main controls, kept on the Logs page for support tickets, with the sticky note.
            Assert.DoesNotContain("Content=\"Rescue save\"", xaml);
            var logs = xaml.IndexOf("x:Name=\"LogsPage\"", StringComparison.Ordinal);
            var button = xaml.IndexOf("Content=\"Run Rescue save\"", StringComparison.Ordinal);
            Assert.True(logs > 0 && button > logs, project);
            Assert.Contains("Click=\"RescueNoticeDismiss_Click\"", xaml);
        }

        // The empty lord party reset stays behind the manual run.
        var module = File.ReadAllText(Path.Combine(RepoRoot(), "compat", "CoopSaveRescue", "SubModule.cs")).Replace("\r\n", "\n");
        Assert.Contains("if (_mode == RescueMode.Full)\n                    CheckEmptyLordParties();", module);
        Assert.Contains("public const string ModeVariable = \"" + AutoRescue.EnvironmentVariable + "\";", module);
        Assert.Contains("public const string ModeFile = \"" + AutoRescue.ModeFile + "\";", module);
    }

    [Fact]
    public void The_start_after_a_load_crash_goes_on_when_the_backup_fails_in_every_host()
    {
        // Crash report CR-8XCEF: an imported single player save had no .json, the backup threw, and
        // CoodszHost's retry never started. AD1259 runs the CoodszHost window, so it is covered here.
        foreach (var project in new[] { "CoodszHost", "TaomCoopHost", "BurningEmpiresHost" })
        {
            var window = SourceFiles.Window(RepoRoot(), project).Replace("\r\n", "\n");
            var start = SourceFiles.Method(window, "private async Task StartAfterLoadCrashAsync()");
            Assert.DoesNotContain(".Create(", start);
            var backup = start.IndexOf("await BackupBeforeFixesAsync(", StringComparison.Ordinal);
            Assert.True(backup >= 0, project);
            Assert.True(backup < start.IndexOf("_rescueNextMode = RescueMode.Load;", StringComparison.Ordinal), project);

            var helper = SourceFiles.Method(window, "private async Task BackupBeforeFixesAsync(");
            // Every failure is caught there, not only the expected kinds, and the backup service is made inside the try.
            Assert.Contains("catch (Exception ex)\n", helper);
            Assert.DoesNotContain("catch (Exception ex) when", helper);
            Assert.True(helper.IndexOf("try", StringComparison.Ordinal) < helper.IndexOf(".Create(name, BackupReason.BeforeRescue)", StringComparison.Ordinal), project);
            Assert.DoesNotContain("var backups = AutoBackups();\n        try", helper);
        }
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "BannerlordCoopLauncher.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
