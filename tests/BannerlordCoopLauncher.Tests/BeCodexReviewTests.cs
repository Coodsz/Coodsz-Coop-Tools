using BannerlordCoopLauncher.Tests;

namespace BurningEmpires.Core.Tests;

// Release review fixes on the Burning Empires tools (Codex review): busy guards, stale console lines,
// Save as, the last good copy, the prefabs fix restore and the install and build safety checks.
public sealed class BeCodexReviewTests
{
    [Fact]
    public void Prepare_server_says_busy_apart_from_not_found_and_counts_a_starting_server_as_busy()
    {
        var window = SourceFiles.Window(RepoRoot(), "BurningEmpiresHost");
        var prepare = SourceFiles.Method(window, "private async void Prepare_Click(");
        Assert.Contains("if (_busy || _startingServer)", prepare);
        Assert.Contains("Prepare server waits until the tool finishes what it is doing now. Click it again in a moment.", prepare);
        // Busy first, then the work (PrepareServerAsync, which Start server runs too): the game search
        // (the saved pick, then Steam), then its own not found message.
        Assert.True(prepare.IndexOf("if (_busy || _startingServer)", StringComparison.Ordinal) < prepare.IndexOf("await PrepareServerAsync();", StringComparison.Ordinal));
        var work = SourceFiles.Method(window, "private async Task<bool> PrepareServerAsync(");
        var search = work.IndexOf("if (FindGameRoot() == null)", StringComparison.Ordinal);
        var notFound = work.IndexOf("SetControl(GameFolderPick.NotFoundText, failed: true);", StringComparison.Ordinal);
        Assert.True(search >= 0 && notFound > search, "the game search, then not found");

        foreach (var language in new[] { "ar", "de", "fr", "pt", "ru", "tr" })
        {
            var json = SourceFiles.Read(Path.Combine("src", "BannerlordCoopLauncher.Core", "Localization", "Languages", language + ".json"));
            Assert.True(json.Contains("\"Prepare server waits until the tool finishes what it is doing now. Click it again in a moment.\":", StringComparison.Ordinal), language);
        }
    }

    [Fact]
    public void No_burning_empires_tool_hides_busy_behind_bannerlord_not_found()
    {
        foreach (var project in new[] { "BurningEmpiresHost", "BurningEmpiresLauncher" })
            Assert.DoesNotContain("_busy || _gameRoot", SourceFiles.Window(RepoRoot(), project));
    }

    [Fact]
    public void A_stopped_runs_late_console_lines_never_reach_the_next_run()
    {
        var host = SourceFiles.Window(RepoRoot(), "BurningEmpiresHost");
        var start = host[host.IndexOf("var consoleLog = new ConsoleLogFile();", StringComparison.Ordinal)..];
        start = start[..start.IndexOf("process.Exited +=", StringComparison.Ordinal)];
        Assert.Contains("var runId = Interlocked.Increment(ref _consoleRunId);", start);
        // Output and error each pass a line on only behind the run check.
        Assert.Equal(2, start.Split("CaptureLine(args.Data);").Length - 1);
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(start, @"if \(IsCurrentRun\(runId\)\)\s+CaptureLine\(args\.Data\);").Count);
    }

    [Fact]
    public void Save_as_waits_for_the_marker_to_go_and_the_save_to_settle()
    {
        var host = SourceFiles.Window(RepoRoot(), "BurningEmpiresHost");
        var saveAs = host[host.IndexOf("\"launcher.server.save \" + name", StringComparison.Ordinal)..];
        saveAs = saveAs[..saveAs.IndexOf("PointAtSave(sav);", StringComparison.Ordinal)];
        Assert.Contains("WorldSavedLine.TransactionMarker(Path.GetDirectoryName(sav)!, name)", saveAs);
        Assert.Contains("SaveFileFinished(sav, marker, DateTime.MinValue)", saveAs);
        Assert.Contains("if (!finished)", saveAs);
        Assert.DoesNotContain("while (!File.Exists(sav)", saveAs);
    }

    [Fact]
    public void The_last_good_copy_runs_on_a_worker_one_at_a_time()
    {
        var host = SourceFiles.Window(RepoRoot(), "BurningEmpiresHost");
        var keep = SourceFiles.Method(host, "private void KeepLastGoodCopy()");
        Assert.Contains("Interlocked.Exchange(ref _keepingLastGood, 1) == 1", keep);
        Assert.Contains("Task.Run(", keep);
        Assert.Contains("KeepLastGoodCopyNow();", keep);
        Assert.DoesNotContain("LastGoodSave.CopyOver", keep);
        Assert.Contains("LastGoodSave.CopyOver(pick, destination);", SourceFiles.Method(host, "private void KeepLastGoodCopyNow()"));
        // Last good save waits for a copy still writing the kept file before it picks.
        var restart = SourceFiles.Method(host, "private async void RestartLastGood_Click(");
        Assert.True(restart.IndexOf("await KeepLastGoodCopyAsync();", StringComparison.Ordinal) < restart.IndexOf("LastGoodSave.Pick(", StringComparison.Ordinal));
    }

    [Fact]
    public void A_game_that_does_not_start_puts_the_prefabs_fix_back()
    {
        var client = SourceFiles.Window(RepoRoot(), "BurningEmpiresLauncher");
        var helper = SourceFiles.Method(client, "private System.Diagnostics.Process StartWithPrefabsFix(");
        Assert.True(helper.IndexOf("ApplyPrefabsFix(setup);", StringComparison.Ordinal) < helper.IndexOf("Process.Start(start)", StringComparison.Ordinal));
        Assert.Contains("finally", helper);
        Assert.Contains("if (started == null)", helper);
        Assert.Contains("RestorePrefabsFix();", helper);
        // Connect and Find the crashing mod both start the game through it, never on their own.
        Assert.Equal(2, client.Split("StartWithPrefabsFix(").Length - 2);
        Assert.Equal(1, client.Split("ApplyPrefabsFix(").Length - 2);
        Assert.Contains("RestorePrefabsFix();", SourceFiles.Method(client, "private void RestoreWhenClosed("));
    }

    [Fact]
    public void A_wrong_or_missing_distance_cache_probe_is_a_core_problem()
    {
        var root = BeServerFixtures.NewRoot("probe-hash");
        try
        {
            var sources = BeServerFixtures.HostPc(root);
            Assert.DoesNotContain(BeServerPath.CoreProblems(sources), line => line.Contains(BeServerPath.ProbeDll, StringComparison.Ordinal));

            var probe = Path.Combine(sources.GameRoot, "Modules", BeServerPath.BeModuleId, "bin", "Win64_Shipping_Client", BeServerPath.ProbeDll);
            File.WriteAllText(probe, "a probe from another build");
            Assert.Contains(BeServerPath.CoreProblems(sources), line => line.StartsWith(BeServerPath.ProbeDll + " is not the file this tool was made for.", StringComparison.Ordinal));

            File.Delete(probe);
            Assert.Contains(BeServerPath.CoreProblems(sources), line => line.StartsWith(BeServerPath.ProbeDll + " is missing.", StringComparison.Ordinal));
        }
        finally
        {
            BeServerFixtures.Delete(root);
        }
    }

    [Fact]
    public void The_compat_build_checks_every_copy_and_the_zip_before_build_ok()
    {
        var lines = SourceFiles.Read(Path.Combine("compat", "BurningEmpiresCoopCompat", "Build.cmd")).Replace("\r", "").Split('\n');
        var steps = 0;
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (!(line.StartsWith("mkdir ") || line.StartsWith("copy ") || line.StartsWith("xcopy ") || line.Contains("tar.exe\"")))
                continue;
            steps++;
            Assert.True(lines[i + 1].Trim().StartsWith("if errorlevel 1", StringComparison.Ordinal), "no error check after: " + line);
        }

        Assert.True(steps >= 8);
        Assert.Contains(":fail", lines);
    }

    [Fact]
    public void Every_busy_guard_in_the_server_tool_also_waits_for_a_starting_server()
    {
        var host = SourceFiles.Window(RepoRoot(), "BurningEmpiresHost");
        Assert.DoesNotMatch(@"if \(_busy\)", host);
        Assert.DoesNotContain("if (_busy || ServerIsRunning())", host);
        Assert.DoesNotContain("if (_busy || !ServerIsRunning())", host);
        foreach (var method in new[] { "private async void InstallOrRepairBe_Click(", "private async void InstallHostMods_Click(", "private async void SaveToNewName_Click(",
                     "private async Task ShowWorldSave(", "private async Task SaveThenStop(", "private async void RestartLastGood_Click(", "private void RestoreSaveBackup_Click(" })
            Assert.Contains("if (_busy || _startingServer)", SourceFiles.Method(host, method));
        Assert.Contains("Wait for the current action to finish.", SourceFiles.Method(host, "private void RestoreSaveBackup_Click("));
    }

    [Fact]
    public void Connect_logs_a_failed_mod_download_instead_of_closing_the_client()
    {
        var connect = SourceFiles.Method(SourceFiles.Window(RepoRoot(), "BurningEmpiresLauncher"), "private async Task ConnectAsync()");
        Assert.Matches(@"try\s+\{\s+ready = await GetServerModsFirstAsync\(\);\s+\}\s+catch \(Exception ex\)", connect);
        Assert.Contains("Getting the mods the server needs stopped, so Connect did not go on. \" + ex.Message", connect);
        foreach (var language in new[] { "ar", "de", "fr", "pt", "ru", "tr" })
        {
            var json = SourceFiles.Read(Path.Combine("src", "BannerlordCoopLauncher.Core", "Localization", "Languages", language + ".json"));
            Assert.True(json.Contains("\"Getting the mods the server needs stopped, so Connect did not go on. {0}\":", StringComparison.Ordinal), language);
        }
    }

    [Fact]
    public void Last_good_save_checks_busy_again_after_the_copy_it_waits_for()
    {
        var restart = SourceFiles.Method(SourceFiles.Window(RepoRoot(), "BurningEmpiresHost"), "private async void RestartLastGood_Click(");
        var copy = restart.IndexOf("await KeepLastGoodCopyAsync();", StringComparison.Ordinal);
        Assert.True(copy > 0);
        var recheck = restart.IndexOf("if (_busy || _startingServer)", copy, StringComparison.Ordinal);
        Assert.True(recheck > copy && recheck < restart.IndexOf("LastGoodSave.Pick(", StringComparison.Ordinal));
    }

    [Fact]
    public void The_prefabs_fix_comes_back_for_a_game_that_closed_before_it_was_watched()
    {
        var watch = SourceFiles.Method(SourceFiles.Window(RepoRoot(), "BurningEmpiresLauncher"), "private void RestoreWhenClosed(");
        Assert.True(watch.IndexOf("game.Exited +=", StringComparison.Ordinal) < watch.IndexOf("game.EnableRaisingEvents = true;", StringComparison.Ordinal));
        Assert.Contains("if (game.HasExited)", watch);
    }

    [Fact]
    public void Files_an_undo_could_not_put_back_come_with_a_list_of_where_each_goes()
    {
        var archive = SourceFiles.Read(Path.Combine("src", "BurningEmpires.Core", "BeArchive.cs"));
        Assert.Contains("public const string IndexName = \"README - where each file goes.txt\";", archive);
        var copy = SourceFiles.Method(archive, "public void Copy(string source, string target)");
        // The file is in the journal before the list is written, so a failed write still gets undone.
        Assert.True(copy.IndexOf("_files.Add((target, backup));", StringComparison.Ordinal) < copy.IndexOf("File.AppendAllText(", StringComparison.Ordinal));
    }

    [Fact]
    public void The_wanderer_guard_only_rewrites_a_settlement_comparison()
    {
        var guards = SourceFiles.Read(Path.Combine("compat", "BurningEmpiresCoopCompat", "Headless", "DsHeadlessGuards.cs"));
        Assert.Contains("if (!IsComparedSettlement(code, i))", SourceFiles.Method(guards, "private static bool HasMainHeroSettlementRead("));
        var transpiler = SourceFiles.Method(guards, "private static IEnumerable<CodeInstruction> WandererTranspiler(");
        Assert.True(transpiler.IndexOf("!IsComparedSettlement(code, i)", StringComparison.Ordinal) < transpiler.IndexOf("OpCodes.Dup", StringComparison.Ordinal));
        var compared = SourceFiles.Method(guards, "private static bool IsComparedSettlement(");
        foreach (var op in new[] { "OpCodes.Ceq", "OpCodes.Beq_S", "OpCodes.Bne_Un_S", "typeof(Settlement).IsAssignableFrom" })
            Assert.Contains(op, compared);
    }

    internal static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "BannerlordCoopLauncher.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
