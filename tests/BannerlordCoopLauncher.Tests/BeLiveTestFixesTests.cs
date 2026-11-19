using System.IO.Compression;
using System.Text;
using System.Text.Json;
using BannerlordCoopLauncher.Core.Launch;
using BannerlordCoopLauncher.Core.Modules;
using BannerlordCoopLauncher.Core.Profile;
using BannerlordCoopLauncher.Core.Support;
using BurningEmpires.Core;
using TaomCoop.Core;
using static BurningEmpires.Core.Tests.BeServerFixtures;

namespace BannerlordCoopLauncher.Tests;

// Fixes from the live test of the Burning Empires tools: a long server folder path, Elephants in the
// core, the battle size the server really starts with, a stale rescue note next to a fresh crash bar,
// and TAOM debug logs and a 16 MB BootPatches.log in the client log pack.
public class BeLiveTestFixesTests
{
    private static string Host() => SourceFiles.Window(RepoRoot(), "BurningEmpiresHost");

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "BannerlordCoopLauncher.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    private static Dictionary<string, string> ReadZip(string zipPath)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        return zip.Entries.Where(entry => !entry.FullName.EndsWith('/')).ToDictionary(
            entry => entry.FullName,
            entry =>
            {
                using var reader = new StreamReader(entry.Open());
                return reader.ReadToEnd();
            });
    }

    // ----- F1: the server folder path -----

    [Fact]
    public void The_server_folder_limit_leaves_room_for_the_deepest_server_file_under_max_path()
    {
        // The deepest staged file (a ButterLib DLL) and the deepest file of the Workshop server whitelist.
        var deepestStaged = @"engine\Modules\Bannerlord.ButterLib\bin\Gaming.Desktop.x64_Shipping_Client\Microsoft.Extensions.DependencyInjection.Abstractions.dll";
        var deepestWhitelist = @"engine\bin\Win64_Shipping_Server\Microsoft.AspNetCore.App\Microsoft.Extensions.Diagnostics.HealthChecks.Abstractions.dll";
        Assert.Equal(deepestStaged.Length, BeServerPrepare.DeepestRelativePath);
        Assert.True(deepestWhitelist.Length < BeServerPrepare.DeepestRelativePath);
        Assert.True(BeServerPrepare.MaxServerFolderLength + 1 + BeServerPrepare.DeepestRelativePath <= 259);
        Assert.True(BeServerPrepare.MaxServerFolderLength >= 60);

        // The live test's folder: 157 characters, and the engine stopped at init.
        var liveTest = @"C:\Users\Admin\AppData\Local\Temp\claude\C--Users-Admin-Projects-BannerlordCoopLauncher\6c15ecd7-9e5d-4e58-aa46-d10cfd9e9c33\scratchpad\betools-live\BEServer";
        Assert.Equal(157, liveTest.Length);
        Assert.True(BeServerPrepare.FolderIsTooLong(liveTest));
        Assert.True(liveTest.Length + 1 + deepestWhitelist.Length > 259);
        var problem = BeServerPrepare.FolderTooLong(liveTest)!;
        Assert.Contains("157 characters long", problem);
        Assert.Contains(BeServerPrepare.MaxServerFolderLength.ToString(), problem);

        // The default folder is short on any drive, and it is the one the messages suggest.
        foreach (var drive in new[] { "C:\\", "D:\\", "Z:\\" })
            Assert.Null(BeServerPrepare.FolderTooLong(BeHostFolders.DefaultServerRoot(drive)));
        Assert.Equal(@"C:\BurningEmpires-Coop-Host\Server", BeServerPrepare.ShortFolderExample);
    }

    [Fact]
    public void Prepare_and_start_refuse_a_server_folder_that_is_too_long()
    {
        var root = NewRoot("long");
        try
        {
            var workshop = WorkshopServer(root);
            var server = Path.Combine(root, new string('s', BeServerPrepare.MaxServerFolderLength));
            Assert.True(BeServerPrepare.FolderIsTooLong(server));

            var refused = Assert.Throws<InvalidOperationException>(() => BeServerPrepare.Prepare(workshop, server));
            Assert.Contains("characters long", refused.Message);
            Assert.Contains("Use a short folder like " + BeServerPrepare.ShortFolderExample + ".", refused.Message);
            // Nothing was copied into the deep folder.
            Assert.False(Directory.Exists(server));

            var missing = BeServerPrepare.MissingPiece(server)!;
            Assert.StartsWith(BeServerPrepare.FolderTooLong(server)!, missing);
            Assert.Contains("Click Prepare server", missing);

            var fine = Path.Combine(root, "server");
            BeServerPrepare.Prepare(workshop, fine);
            Assert.Null(BeServerPrepare.MissingPiece(fine));
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void Prepare_server_offers_the_short_default_folder_before_it_copies()
    {
        var host = Host();
        var prepare = SourceFiles.Method(host, "private async Task<bool> PrepareServerAsync(");
        Assert.True(prepare.IndexOf("if (!UseShortServerFolder())", StringComparison.Ordinal) < prepare.IndexOf("SaveSettings();", StringComparison.Ordinal));
        var offer = SourceFiles.Method(host, "private bool UseShortServerFolder()");
        Assert.Contains("BeServerPrepare.FolderTooLong(_settings.ServerRoot)", offer);
        Assert.Contains("BeHostFolders.DefaultServerRoot(", offer);
        Assert.Contains("ConfirmWindow.Ask(this, \"Server folder path too long\"", offer);
        Assert.Contains("_settings.ServerRoot = shortFolder;", offer);
        // The default folder when none is set is on the game's drive root.
        // ApplyGameFound sets it, at startup and after a Find Bannerlord pick alike.
        var found = SourceFiles.Method(host, "private void ApplyGameFound(");
        Assert.Contains("if (string.IsNullOrWhiteSpace(_settings.ServerRoot))", found);
        Assert.Contains("_settings.ServerRoot = BeHostFolders.DefaultServerRoot(Path.GetPathRoot(root) ?? \"C:\\\\\");", found);
    }

    // ----- F2: Elephants in the core -----

    [Fact]
    public void Start_server_always_stages_elephants_and_says_when_it_is_missing()
    {
        var root = NewRoot("elephants");
        try
        {
            var sources = HostPc(root);
            var server = Path.Combine(root, "server");
            BeServerPath.StageCore(server, sources);
            Assert.True(File.Exists(Path.Combine(BeServerPath.ModulesRoot(server), BeStack.ElephantsModuleId, "SubModule.xml")));

            Directory.Delete(Path.Combine(sources.GameRoot, "Modules", BeStack.ElephantsModuleId), recursive: true);
            Assert.Contains(BeServerPath.CoreProblems(sources), line => line.StartsWith("Burning Empires Elephants is not in the game's Modules folder.", StringComparison.Ordinal));
            Assert.Throws<InvalidOperationException>(() => BeServerPath.StageCore(server, sources));
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void A_mod_list_shared_from_the_old_plain_setup_imports_without_a_missing_core_line()
    {
        var oldPlainList = BeStack.ClientModules
            .Where(id => id != BeStack.ElephantsModuleId)
            .Select(id => new SharedModEntry(id, id, "v1.0.0", "0"))
            .Append(new SharedModEntry("Extra", "Extra", "v1.0.0", "123456"))
            .ToList();
        var setup = new TaomSetup { Name = "Mine" };
        var result = BeSharedList.Import(setup, oldPlainList, Array.Empty<FoundMod>());
        Assert.Empty(result.MissingCore);
        Assert.Equal(new[] { "Extra" }, setup.Extras.Select(mod => mod.ModuleId));
        Assert.Contains(BeStack.ElephantsModuleId, BeLoadOrder.ClientOrder(setup));
    }

    [Fact]
    public void The_compat_list_marks_elephants_as_part_of_the_core()
    {
        var elephants = BeCompatList.BuiltIn().Find(BeStack.ElephantsModuleId)!;
        Assert.Equal(CompatTag.Safe, elephants.Kind);
        Assert.Equal(
            File.ReadAllText(Path.Combine(RepoRoot(), "src", "BurningEmpires.Core", "Compat", "be-mod-compat.json")),
            File.ReadAllText(Path.Combine(RepoRoot(), "be-compat", "compat", "be-mod-compat.json")));
        foreach (var file in new[] { @"src\BurningEmpires.Core\Compat\be-mod-compat.json", @"be-compat\MODS.md", "Coodsz-BurningEmpires-Instructions.txt", @"compat\BurningEmpiresCoopCompat\README.txt" })
        {
            var text = File.ReadAllText(Path.Combine(RepoRoot(), file));
            Assert.DoesNotContain("Plain Burning Empires", text);
            Assert.DoesNotContain("Burning Empires with Elephants", text);
            Assert.DoesNotContain("Elephants (optional)", text);
        }
    }

    // ----- F3: the battle size the server starts with -----

    [Fact]
    public void The_start_battle_size_is_the_settings_value_in_mod_config()
    {
        Assert.Equal(400, BeServerLaunch.StartBattleSize("{ \"modOptions\": { \"battleSize\": 400 } }"));
        Assert.Equal(CoopModConfig.BattleSizeDefault, BeServerLaunch.StartBattleSize(null));
        Assert.Equal(CoopModConfig.BattleSizeDefault, BeServerLaunch.StartBattleSize("{ \"modOptions\": {} }"));
        // The script refuses less than 300; the box never goes above Coop's 1000.
        Assert.Equal(BeServerLaunch.MinBattleSize, BeServerLaunch.StartBattleSize("{ \"modOptions\": { \"battleSize\": 250 } }"));
        Assert.Equal(CoopModConfig.BattleSizeMax, BeServerLaunch.StartBattleSize("{ \"modOptions\": { \"battleSize\": 5000 } }"));
        Assert.Equal("Battle size set to 400.", BeServerLaunch.BattleSizeLine(400));

        var root = NewRoot("battle");
        try
        {
            var server = Path.Combine(root, "server");
            Write(Path.Combine(server, BeServerPrepare.ScriptName), "script");
            var start = BeServerLaunch.Build(server, new BeStartOptions { SaveName = "BETest", NewCampaign = true, BattleSize = 400 });
            Assert.Contains(" -BattleSize 400", start.Arguments);
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void Server_config_never_carries_the_battle_size_the_server_ignores()
    {
        Assert.DoesNotContain("battleSize", BeServerPrepare.DefaultServerConfig);
        // Nothing acts on the auto restart keys; the server logs them as unknown.
        Assert.DoesNotContain("autoRestart", BeServerPrepare.DefaultServerConfig);
        using (JsonDocument.Parse(BeServerPrepare.DefaultServerConfig))
        {
        }

        // The live test's file: battleSize last, after a comma.
        var live = "{\r\n  \"saveName\": \"BETest\",\r\n  \"password\": \"\",\r\n  \"allowClientTimeControls\": true,\r\n  \"battleSize\": 700\r\n}\r\n";
        var cleaned = BeServerLaunch.WithoutBattleSize(live);
        Assert.DoesNotContain("battleSize", cleaned);
        Assert.EndsWith("}\r\n", cleaned);
        using (var document = JsonDocument.Parse(cleaned))
        {
            Assert.True(document.RootElement.GetProperty("allowClientTimeControls").GetBoolean());
            Assert.Equal("BETest", document.RootElement.GetProperty("saveName").GetString());
        }

        var middle = "{\n  \"battleSize\": 1000,\n  \"saveName\": \"BETest\"\n}\n";
        using (var document = JsonDocument.Parse(BeServerLaunch.WithoutBattleSize(middle)))
            Assert.False(document.RootElement.TryGetProperty("battleSize", out _));

        var none = "{\n  \"saveName\": \"BETest\"\n}\n";
        Assert.Same(none, BeServerLaunch.WithoutBattleSize(none));
    }

    [Fact]
    public void Start_server_passes_the_settings_battle_size_and_logs_it()
    {
        var host = Host();
        Assert.Contains("BattleSize = StartBattleSize(),", SourceFiles.Method(host, "private async Task StartServerCoreAsync("));
        Assert.Contains("BeServerLaunch.StartBattleSize(File.Exists(path) ? File.ReadAllText(path) : null)", SourceFiles.Method(host, "private int StartBattleSize()"));
        Assert.Contains("NoteStart(BeServerLaunch.BattleSizeLine(battleSize));", SourceFiles.Method(host, "private async Task<BeServerStart?> PrepareBeServerAsync("));
        Assert.Contains("BeServerLaunch.WithoutBattleSize(HostConfigEditor.Apply(", SourceFiles.Method(host, "private static void WriteConfigFile("));
    }

    // ----- F4: a stale rescue note next to a fresh crash bar -----

    [Fact]
    public void A_new_start_or_a_new_crash_bar_clears_an_old_problem_note()
    {
        var host = Host();
        Assert.Contains("_rescueNoticeProblem = problem;", SourceFiles.Method(host, "private void ShowRescueNotice("));
        var clear = SourceFiles.Method(host, "private void ClearStaleRescueNotice()");
        // Only a problem note goes; a note about fixes kept in the save stays until Dismiss.
        Assert.Contains("if (!_rescueNoticeProblem || RescueNotice.Visibility != Visibility.Visible)", clear);
        Assert.Contains("RescueNotice.Visibility = Visibility.Collapsed;", clear);

        var begin = SourceFiles.Method(host, "private void BeginRescueRun()");
        // The load mode retry keeps its own "starting once more" note.
        Assert.True(begin.IndexOf("if (_rescueNextMode != RescueMode.Load)", StringComparison.Ordinal) is var guard && guard > 0
            && guard < begin.IndexOf("ClearStaleRescueNotice();", StringComparison.Ordinal));

        var start = SourceFiles.Method(host, "private async void Start_Click(");
        Assert.True(start.IndexOf("ClearStaleRescueNotice();", StringComparison.Ordinal) is var at && at > 0
            && at < start.IndexOf("await StartServerAsync(prepareIfNeeded: true, automatic);", StringComparison.Ordinal));

        var offer = SourceFiles.Method(host, "private void OfferServerCrashReport(Process process)");
        var cleared = offer.IndexOf("ClearStaleRescueNotice();", StringComparison.Ordinal);
        Assert.True(cleared > offer.IndexOf("if (!CrashReports.ShowsServerCrashBar(exit))", StringComparison.Ordinal));
        Assert.True(cleared < offer.IndexOf("_crashBar.ServerCrashed(", StringComparison.Ordinal));
        Assert.True(cleared < offer.IndexOf("if (_crashBar == null)", StringComparison.Ordinal));

        // The crash bar (and its clean up) comes before the note about this crash.
        var exited = host[host.IndexOf("process.Exited += (_, _) =>", StringComparison.Ordinal)..];
        Assert.True(exited.IndexOf("OfferServerCrashReport(process);", StringComparison.Ordinal) < exited.IndexOf("OnRescueExit(process, exitCode, loadStep);", StringComparison.Ordinal));
    }

    // ----- F7: the log packs -----

    [Fact]
    public void The_client_pack_leaves_out_taom_debug_logs_and_keeps_the_end_of_boot_patches()
    {
        var root = NewRoot("client-logs");
        try
        {
            var game = Path.Combine(root, "game");
            var bin = Path.Combine(game, "bin", "Win64_Shipping_Client");
            var lines = new StringBuilder();
            var number = 0;
            while (lines.Length < 5_000_000)
                lines.Append("BootPatches line ").Append(number++).Append('\n');
            lines.Append("LAST BOOT PATCHES LINE\n");
            Write(Path.Combine(bin, "BootPatches.log"), lines.ToString());
            Write(Path.Combine(bin, "Coop_client.log"), "coop client");
            Write(Path.Combine(bin, "Logs", "taom_debug_2026-10-23_18-38-39.log"), "taom debug");
            Write(Path.Combine(bin, "Logs", "game.log"), "game log");

            var zipPath = Path.Combine(root, "client.zip");
            BeClientLogPack.Create(zipPath, game, Path.Combine(root, "docs"), Path.Combine(root, "programdata"));
            var zip = ReadZip(zipPath);

            Assert.DoesNotContain(zip.Keys, name => name.Contains("taom_debug_", StringComparison.OrdinalIgnoreCase));
            Assert.Contains("game-logs/game.log", zip.Keys);
            var boot = zip["game/BootPatches.log"];
            Assert.StartsWith("[Only the last 4 MB of this file is included.]", boot);
            Assert.Contains("LAST BOOT PATCHES LINE", boot);
            Assert.True(boot.Length < BeClientLogPack.BootPatchesBytes + 1000);
            Assert.Equal(CrashReports.FirstTextBytes, BeClientLogPack.BootPatchesBytes);
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void The_other_client_packs_keep_their_game_logs_as_before()
    {
        var root = NewRoot("taom-logs");
        try
        {
            var game = Path.Combine(root, "game");
            var bin = Path.Combine(game, "bin", "Win64_Shipping_Client");
            Write(Path.Combine(bin, "BootPatches.log"), "boot patches");
            Write(Path.Combine(bin, "Logs", "taom_debug_2026-10-23_18-38-39.log"), "taom debug");

            var zipPath = Path.Combine(root, "client.zip");
            ClientLogZip.Create(zipPath, game, Path.Combine(root, "docs"), Path.Combine(root, "programdata"));
            var zip = ReadZip(zipPath);
            Assert.Contains("game-logs/taom_debug_2026-10-23_18-38-39.log", zip.Keys);
            Assert.Equal("boot patches", zip["game/BootPatches.log"]);
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void The_host_pack_leaves_out_taom_debug_logs_from_the_shared_data_folder()
    {
        var root = NewRoot("host-logs");
        try
        {
            var data = Path.Combine(root, "data");
            Write(Path.Combine(BeHostFolders.Logs(data), "Coop_server.log"), "coop server");
            Write(Path.Combine(BeHostFolders.Logs(data), "taom_debug_2026-10-23_11-48-22.log"), "taom debug");

            var zipPath = Path.Combine(root, "host.zip");
            BeHostLogPack.Create(zipPath, Path.Combine(root, "server"), data, Path.Combine(root, "programdata"), steamLibraries: []);
            var zip = ReadZip(zipPath);
            Assert.Contains("server-logs/Coop_server.log", zip.Keys);
            Assert.DoesNotContain(zip.Keys, name => name.Contains("taom_debug_", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Delete(root);
        }
    }
}
