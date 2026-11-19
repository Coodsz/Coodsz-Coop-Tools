using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TaomCoop.Core;

namespace BannerlordCoopLauncher.Tests;

// Player report CR-ZAZSG: the prepared server folder lost BannerlordCoopServer.exe, Start said
// "Prepare the server" and Prepare refused the folder. Start now puts back only the wrapper files.
public class TaomServerProgramRepairTests
{
    private static string NewRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "taom-repair-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    // A wrapper artifact laid out like the host kit's Artifacts\TAOM-Coop-Wrapper\<name> folder.
    private static string Kit(string root, bool badExe = false)
    {
        var artifact = Path.Combine(root, "kit", "Artifacts", "TAOM-Coop-Wrapper", "taom-coop-wrapper-test");
        var entries = new List<object>();
        foreach (var payload in ServerProgramRepair.Files.Select(file => file.Payload).Distinct())
        {
            var path = Path.Combine(artifact, payload.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var bytes = Encoding.UTF8.GetBytes("wrapper " + payload);
            File.WriteAllBytes(path, bytes);
            entries.Add(new { path = payload, sha256 = Convert.ToHexString(SHA256.HashData(bytes)), length = (long)bytes.Length });
        }

        File.WriteAllText(Path.Combine(artifact, ServerProgramRepair.ManifestName), JsonSerializer.Serialize(new { files = entries }));
        if (badExe)
            File.WriteAllText(Path.Combine(artifact, ServerProgramRepair.ExeName), "quarantined and replaced");
        return artifact;
    }

    // A prepared server with every wrapper file in place, plus engine, TAOM module and TAOM.CoopCompat files.
    private static string Server(string root)
    {
        var server = Path.Combine(root, "Server-v0325-Coop-v016-r2");
        var targets = ServerProgramRepair.Files.Select(file => file.Target)
            .Concat(ServerProgramRepair.EngineBinFiles.Select(file => Path.Combine(ServerProgramRepair.EngineBin, file.Name)))
            .Concat(ServerProgramRepair.ModuleFiles.Select(file => file.Target))
            .Concat(ServerProgramRepair.CompatFiles.Select(file => file.Target));
        foreach (var target in targets)
        {
            var path = Path.Combine(server, target);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "prepared " + target);
        }

        var taom = Path.Combine(server, "engine", "Modules", "TAOM", "SubModule.xml");
        Directory.CreateDirectory(Path.GetDirectoryName(taom)!);
        File.WriteAllText(taom, "<Module/>");
        return server;
    }

    private static string Data(string root)
    {
        var data = Path.Combine(root, "Data-v0325");
        Directory.CreateDirectory(Path.Combine(data, "Game Saves"));
        File.WriteAllText(Path.Combine(data, "Game Saves", "campaign.sav"), "live campaign, played on after Prepare");
        File.WriteAllText(Path.Combine(data, "server-config.json"), "{ \"saveName\": \"campaign\" }");
        return data;
    }

    private static Dictionary<string, (string Text, DateTime Written)> Snapshot(string folder) =>
        Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
            .ToDictionary(path => path, path => (File.ReadAllText(path), File.GetLastWriteTimeUtc(path)));

    [Fact]
    public void Missing_exe_in_a_prepared_folder_needs_repair_not_prepare()
    {
        var root = NewRoot();
        var server = Server(root);
        File.Delete(Path.Combine(server, ServerProgramRepair.ExeName));

        Assert.Equal(ServerProgramState.NeedsRepair, ServerProgramRepair.Check(server, serverRunning: false));
    }

    [Fact]
    public void A_running_server_is_never_repaired()
    {
        var root = NewRoot();
        var server = Server(root);
        File.Delete(Path.Combine(server, ServerProgramRepair.ExeName));

        Assert.Equal(ServerProgramState.ServerRunning, ServerProgramRepair.Check(server, serverRunning: true));
    }

    [Fact]
    public void Missing_or_empty_folder_still_asks_for_prepare_and_a_whole_folder_is_ready()
    {
        var root = NewRoot();
        Assert.Equal(ServerProgramState.NotPrepared, ServerProgramRepair.Check(Path.Combine(root, "none"), false));
        Directory.CreateDirectory(Path.Combine(root, "empty"));
        Assert.Equal(ServerProgramState.NotPrepared, ServerProgramRepair.Check(Path.Combine(root, "empty"), false));
        Assert.Equal(ServerProgramState.Ready, ServerProgramRepair.Check(Server(root), false));
    }

    [Fact]
    public void Repair_puts_back_only_missing_wrapper_files_and_leaves_data_alone()
    {
        var root = NewRoot();
        var server = Server(root);
        var data = Data(root);
        var artifact = Kit(root);
        File.Delete(Path.Combine(server, ServerProgramRepair.ExeName));
        var dll = Path.Combine(server, "engine", "bin", "Win64_Shipping_Server", "DedicatedServer.Core.dll");
        File.Delete(dll);
        var dataBefore = Snapshot(data);
        var keptFiles = Snapshot(server);

        var result = ServerProgramRepair.Repair(server, data, artifact);

        Assert.True(result.Repaired);
        Assert.Equal(2, result.Restored.Count);
        Assert.Equal("wrapper " + ServerProgramRepair.ExeName, File.ReadAllText(Path.Combine(server, ServerProgramRepair.ExeName)));
        Assert.Equal("wrapper Modules/DedicatedServer.Windows/bin/Win64_Shipping_Server/DedicatedServer.Core.dll", File.ReadAllText(dll));
        Assert.Equal(ServerProgramState.Ready, ServerProgramRepair.Check(server, false));
        // Files that were there are not overwritten, and the Data folder is byte for byte the same.
        foreach (var (path, before) in keptFiles)
            Assert.Equal(before, (File.ReadAllText(path), File.GetLastWriteTimeUtc(path)));
        Assert.Equal(dataBefore, Snapshot(data));
    }

    [Fact]
    public void A_changed_or_missing_kit_copy_fails_without_writing_anything()
    {
        var root = NewRoot();
        var server = Server(root);
        var data = Data(root);
        var artifact = Kit(root, badExe: true);
        var exe = Path.Combine(server, ServerProgramRepair.ExeName);
        File.Delete(exe);
        var dataBefore = Snapshot(data);

        var result = ServerProgramRepair.Repair(server, data, artifact);
        Assert.False(result.Repaired);
        Assert.Contains(ServerProgramRepair.ExeName, result.StillMissing);
        Assert.False(File.Exists(exe));

        var noKit = ServerProgramRepair.Repair(server, data, null);
        Assert.False(noKit.Repaired);
        Assert.False(File.Exists(exe));
        Assert.Equal(dataBefore, Snapshot(data));
    }

    [Fact]
    public void A_data_folder_inside_the_server_folder_is_never_written()
    {
        var root = NewRoot();
        var server = Server(root);
        var artifact = Kit(root);
        File.Delete(Path.Combine(server, ServerProgramRepair.ExeName));

        var result = ServerProgramRepair.Repair(server, Path.Combine(server, "Data"), artifact);

        Assert.False(result.Repaired);
        Assert.Empty(result.Restored);
        Assert.False(File.Exists(Path.Combine(server, ServerProgramRepair.ExeName)));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_server_inside_the_data_folder_or_an_unreadable_data_path_is_never_written(bool serverInsideData)
    {
        var root = NewRoot();
        var server = Server(root);
        var artifact = Kit(root);
        File.Delete(Path.Combine(server, ServerProgramRepair.ExeName));
        // The Data folder holds the server folder, or the settings name a path with characters Windows refuses.
        var data = serverInsideData ? root : "C:\\bad\0path";

        var result = ServerProgramRepair.Repair(server, data, artifact);

        Assert.False(result.Repaired);
        Assert.Empty(result.Restored);
        Assert.False(File.Exists(Path.Combine(server, ServerProgramRepair.ExeName)));
    }

    [Fact]
    public void Start_repairs_before_asking_for_prepare_and_prepare_points_to_start()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "BannerlordCoopLauncher.sln")))
            dir = dir.Parent;
        var host = SourceFiles.Window(dir!.FullName, "TaomCoopHost");
        var start = SourceFiles.Method(host, "private async Task StartServerCoreAsync");
        Assert.Contains("if (!await EnsureServerProgramAsync(server))", start);
        Assert.DoesNotContain("Prepare the server before starting it.", start);
        var prepare = SourceFiles.Method(host, "private async void Prepare_Click");
        Assert.Contains("Click Start server. It puts back missing server files by itself.", prepare);
        // The repair never runs the kit's Prepare-Host.ps1, which takes -DataRoot and -SaveFile.
        var repair = SourceFiles.Method(host, "private static ServerProgramRepairResult RepairServerProgram");
        Assert.DoesNotContain("RunPrepare", repair);
    }

    [Fact]
    public void Prepare_says_busy_on_its_own_and_looks_for_the_game_again_before_not_found()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "BannerlordCoopLauncher.sln")))
            dir = dir.Parent;
        var host = SourceFiles.Window(dir!.FullName, "TaomCoopHost");
        var prepare = SourceFiles.Method(host, "private async void Prepare_Click");
        var busy = prepare.IndexOf("if (_busy || _startingServer)", StringComparison.Ordinal);
        var find = prepare.IndexOf("FindGameRoot() == null", StringComparison.Ordinal);
        var notFound = prepare.IndexOf("SetControl(GameFolderPick.NotFoundText", StringComparison.Ordinal);
        Assert.True(busy >= 0 && find > busy && notFound > find, "busy check, then FindGameRoot, then not found");
        Assert.Contains("Prepare server waits until the tool finishes what it is doing now.", prepare);
        Assert.DoesNotContain("_busy || _gameRoot == null", prepare);
    }

    [Fact]
    public void No_tool_hides_busy_behind_bannerlord_not_found()
    {
        // Review rule: busy and not found each say why; the release review greps for the mixed guard.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "BannerlordCoopLauncher.sln")))
            dir = dir.Parent;
        var hits = Directory.EnumerateFiles(Path.Combine(dir!.FullName, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar))
            .Where(file => File.ReadAllText(file).Contains("_busy || _gameRoot", StringComparison.Ordinal))
            .ToList();
        Assert.Empty(hits);
    }

    [Fact]
    public void Busy_guards_in_a_tool_with_a_start_flag_also_wait_for_it()
    {
        // Same rule as tools/Check-ButtonGuards.ps1: an action that waits for a busy tool also waits while Start
        // server prepares, unless the line says "// guard ok: <why>".
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "BannerlordCoopLauncher.sln")))
            dir = dir.Parent;
        var files = Directory.EnumerateFiles(Path.Combine(dir!.FullName, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar))
            .ToList();
        var toolsWithFlag = files.Where(file => File.ReadAllText(file).Contains("_startingServer", StringComparison.Ordinal))
            .Select(Path.GetDirectoryName).ToHashSet();
        var hits = new List<string>();
        // A tool's subfolders count too, like the script.
        foreach (var file in files.Where(file => toolsWithFlag.Any(tool => file.StartsWith(tool + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))))
        {
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                var optOut = lines[i].Contains("// guard ok:") || (i > 0 && lines[i - 1].Contains("// guard ok:"));
                if (System.Text.RegularExpressions.Regex.IsMatch(lines[i], @"if\s*\(\s*_busy\b") && !lines[i].Contains("_startingServer") && !optOut)
                    hits.Add(Path.GetFileName(file) + ":" + (i + 1) + " " + lines[i].Trim());
            }
        }
        Assert.Empty(hits);
    }

    [Fact]
    public void Wrapper_artifact_is_found_only_when_the_kit_has_exactly_one()
    {
        var root = NewRoot();
        var artifact = Kit(root);
        var kit = Path.Combine(root, "kit");
        Assert.Equal(artifact, ServerProgramRepair.FindWrapperArtifact(kit));
        Directory.CreateDirectory(Path.Combine(kit, "Artifacts", "TAOM-Coop-Wrapper", "second"));
        Assert.Null(ServerProgramRepair.FindWrapperArtifact(kit));
        Assert.Null(ServerProgramRepair.FindWrapperArtifact(Path.Combine(root, "none")));
    }

    // Player report CR-WRTH2: antivirus took 0Harmony.dll and three GauntletUI DLLs from the engine bin,
    // the exe repair said nothing was missing, and every start died 2 seconds in.
    [Fact]
    public void Missing_engine_bin_dlls_need_repair()
    {
        var root = NewRoot();
        var server = Server(root);
        var bin = Path.Combine(server, ServerProgramRepair.EngineBin);
        foreach (var name in new[] { "0Harmony.dll", "TaleWorlds.GauntletUI.dll", "TaleWorlds.GauntletUI.PrefabSystem.dll", "TaleWorlds.Engine.GauntletUI.dll" })
            File.Delete(Path.Combine(bin, name));

        Assert.Equal(ServerProgramState.NeedsRepair, ServerProgramRepair.Check(server, serverRunning: false));
        Assert.Equal(new[] { "0Harmony.dll", "TaleWorlds.GauntletUI.dll", "TaleWorlds.GauntletUI.PrefabSystem.dll", "TaleWorlds.Engine.GauntletUI.dll" },
            ServerProgramRepair.MissingEngineBinFiles(server));
        // With no Workshop copy to put back, only 0Harmony.dll stops the start.
        Assert.Equal(new[] { "0Harmony.dll" }, ServerProgramRepair.BlockingEngineBinFiles(server, null));
    }

    // The staging moves only the names the main bin lacks, so a healthy server can lack one of the list.
    [Fact]
    public void A_missing_view_dll_with_no_checked_copy_does_not_stop_the_start()
    {
        var root = NewRoot();
        var server = Server(root);
        File.Delete(Path.Combine(server, ServerProgramRepair.EngineBin, "TaleWorlds.GauntletUI.Data.dll"));
        var workshop = Workshop(root, Encoding.UTF8.GetBytes("client Harmony"), inCoopBin: true);

        Assert.Equal(ServerProgramState.Ready, ServerProgramRepair.Check(server, false));
        Assert.Equal(ServerProgramState.Ready, ServerProgramRepair.Check(server, false, workshop));
        Assert.Empty(ServerProgramRepair.BlockingEngineBinFiles(server, workshop));
        Assert.Equal(new[] { "TaleWorlds.GauntletUI.Data.dll" }, ServerProgramRepair.MissingEngineBinFiles(server));
    }

    [Fact]
    public void A_missing_view_dll_with_a_checked_workshop_copy_is_put_back_first()
    {
        var root = NewRoot();
        var server = Server(root);
        var dll = Path.Combine(server, ServerProgramRepair.EngineBin, "TaleWorlds.GauntletUI.Data.dll");
        File.Delete(dll);
        var workshop = Path.Combine(root, "workshop", "3770450698", "DedicatedServer");
        var coopBin = Path.Combine(workshop, "engine", "Modules", "Coop", "bin", "Win64_Shipping_Server");
        Directory.CreateDirectory(coopBin);
        var bytes = Encoding.UTF8.GetBytes("Workshop GauntletUI.Data");
        File.WriteAllBytes(Path.Combine(coopBin, "TaleWorlds.GauntletUI.Data.dll"), bytes);
        var pins = new[] { ("TaleWorlds.GauntletUI.Data.dll", (long)bytes.Length, Convert.ToHexString(SHA256.HashData(bytes))) };

        Assert.Equal(new[] { "TaleWorlds.GauntletUI.Data.dll" }, ServerProgramRepair.BlockingEngineBinFiles(server, workshop, pins));
        var result = ServerProgramRepair.Repair(server, Data(root), null, workshop, pins);

        Assert.True(result.Repaired);
        Assert.Equal(bytes, File.ReadAllBytes(dll));
        Assert.Empty(ServerProgramRepair.BlockingEngineBinFiles(server, workshop, pins));
    }

    // The same pin Stage-TaomCompatServer.ps1 checks, so a client built Harmony never fills the slot.
    [Fact]
    public void Harmony_pin_matches_the_staging_script()
    {
        var harmony = ServerProgramRepair.EngineBinFiles.Single(file => file.Name == "0Harmony.dll");
        Assert.Equal(2333696, harmony.Length);
        Assert.Equal("12EED32DE73312B376F616EE8659BA5D3766D96859181728437FA85C6B5CE309", harmony.Sha256);
    }

    private static string Workshop(string root, byte[] harmony, bool inCoopBin)
    {
        var workshop = Path.Combine(root, "workshop", "3770450698", "DedicatedServer");
        var folder = inCoopBin
            ? Path.Combine(workshop, "engine", "Modules", "Coop", "bin", "Win64_Shipping_Server")
            : Path.Combine(workshop, ServerProgramRepair.EngineBin);
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, "0Harmony.dll"), harmony);
        return workshop;
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Repair_puts_back_harmony_only_from_a_copy_matching_the_pin(bool inCoopBin)
    {
        var root = NewRoot();
        var server = Server(root);
        var data = Data(root);
        var bytes = Encoding.UTF8.GetBytes("server built Harmony");
        var workshop = Workshop(root, bytes, inCoopBin);
        var harmony = Path.Combine(server, ServerProgramRepair.EngineBin, "0Harmony.dll");
        File.Delete(harmony);
        var dataBefore = Snapshot(data);
        var pins = new[] { ("0Harmony.dll", (long)bytes.Length, Convert.ToHexString(SHA256.HashData(bytes))) };

        var result = ServerProgramRepair.Repair(server, data, null, workshop, pins);

        Assert.True(result.Repaired);
        Assert.Equal(new[] { Path.Combine(ServerProgramRepair.EngineBin, "0Harmony.dll") }, result.Restored);
        Assert.Equal(bytes, File.ReadAllBytes(harmony));
        // A copy, never a move: the Workshop item keeps its file.
        Assert.Single(Directory.EnumerateFiles(workshop, "0Harmony.dll", SearchOption.AllDirectories));
        Assert.Equal(dataBefore, Snapshot(data));
    }

    [Fact]
    public void A_harmony_that_does_not_match_the_pin_is_refused()
    {
        var root = NewRoot();
        var server = Server(root);
        var data = Data(root);
        // A client Harmony in both places the staging looks, with the real pin.
        var workshop = Workshop(root, Encoding.UTF8.GetBytes("client Harmony"), inCoopBin: true);
        Workshop(root, Encoding.UTF8.GetBytes("client Harmony"), inCoopBin: false);
        var harmony = Path.Combine(server, ServerProgramRepair.EngineBin, "0Harmony.dll");
        File.Delete(harmony);

        var result = ServerProgramRepair.Repair(server, data, null, workshop);

        Assert.False(result.Repaired);
        Assert.Contains(Path.Combine(ServerProgramRepair.EngineBin, "0Harmony.dll"), result.StillMissing);
        Assert.False(File.Exists(harmony));
        Assert.Equal(new[] { "0Harmony.dll" }, ServerProgramRepair.MissingEngineBinFiles(server));

        var noWorkshop = ServerProgramRepair.Repair(server, data, null, null);
        Assert.False(noWorkshop.Repaired);
        Assert.False(File.Exists(harmony));
    }

    [Fact]
    public void An_engine_bin_dll_is_never_written_with_the_data_folder_inside_the_server_folder()
    {
        var root = NewRoot();
        var server = Server(root);
        var bytes = Encoding.UTF8.GetBytes("server built Harmony");
        var workshop = Workshop(root, bytes, inCoopBin: true);
        var harmony = Path.Combine(server, ServerProgramRepair.EngineBin, "0Harmony.dll");
        File.Delete(harmony);
        var pins = new[] { ("0Harmony.dll", (long)bytes.Length, Convert.ToHexString(SHA256.HashData(bytes))) };

        var result = ServerProgramRepair.Repair(server, Path.Combine(server, "Data"), null, workshop, pins);

        Assert.False(result.Repaired);
        Assert.False(File.Exists(harmony));
    }

    [Fact]
    public void Log_names_only_engine_bin_dlls_that_are_gone()
    {
        var root = NewRoot();
        var server = Server(root);
        var bin = Path.Combine(server, ServerProgramRepair.EngineBin);
        File.Delete(Path.Combine(bin, "0Harmony.dll"));
        File.Delete(Path.Combine(bin, "TaleWorlds.GauntletUI.dll"));
        // The lines from CR-WRTH2 (Russian Windows), twice for 0Harmony, plus one for a file that is there.
        var log = string.Join("\n",
            "01:39:18.096  [01:39:18.093] Messagebox [ERROR] message: Cannot load: 0Harmony.dll",
            $"01:39:18.096  [01:39:18.093] ERROR: 0Harmony.dll: Could not load file or assembly '{bin}\\0Harmony.dll'. Не удается найти указанный файл. ",
            $"01:39:18.097  [01:39:18.093] ERROR: TaleWorlds.GauntletUI.dll: Could not load file or assembly '{bin}\\TaleWorlds.GauntletUI.dll'. Не удается найти указанный файл. ",
            $"[01:39:18.093] ERROR: 0Harmony.dll: Could not load file or assembly '{bin}\\0Harmony.dll'.",
            $"[01:39:18.093] ERROR: SandBox.View.dll: Could not load file or assembly '{bin}\\SandBox.View.dll'. Access denied.",
            "01:39:18.104  Could not load file or assembly 'TaleWorlds.GauntletUI, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null'.");

        Assert.Equal(new[] { "0Harmony.dll", "TaleWorlds.GauntletUI.dll" }, ServerProgramRepair.MissingInLog(log));
        // A module's own bin is not the engine bin, and a healthy log names nothing.
        Assert.Empty(ServerProgramRepair.MissingInLog($"Could not load file or assembly '{server}\\engine\\Modules\\TAOM\\bin\\Win64_Shipping_Server\\Gone.dll'."));
        Assert.Empty(ServerProgramRepair.MissingInLog("[01:39:18.102] Loaded Types: Bannerlord.UIExtenderEx.Patches.WidgetPrefabPatch"));
        Assert.Empty(ServerProgramRepair.MissingInLog(null));
        // UIExtenderEx probing can log an engine bin file that never existed. Only the staged list counts,
        // so a real save load crash still gets its load mode retry.
        Assert.Empty(ServerProgramRepair.MissingInLog($"ERROR: Probe.dll: Could not load file or assembly '{bin}\\Bannerlord.UIExtenderEx.Probe.dll'. The system cannot find the file specified."));
    }

    // The exact line from the CR-WRTH2 server log, Russian Windows, E: drive.
    [Fact]
    public void The_real_cr_wrth2_harmony_line_counts()
    {
        const string line = "01:39:18.096  [01:39:18.093] ERROR: 0Harmony.dll: Could not load file or assembly 'E:\\TAOM-Coop-Host\\Server-v0325-Coop-v016-r2\\engine\\bin\\Win64_Shipping_Server\\0Harmony.dll'. Не удается найти указанный файл. ";
        Assert.Equal(new[] { "0Harmony.dll" }, ServerProgramRepair.MissingInLog(line));
    }

    [Fact]
    public void Missing_server_files_stop_the_start_and_their_exit_is_no_load_crash()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "BannerlordCoopLauncher.sln")))
            dir = dir.Parent;
        var host = SourceFiles.Window(dir!.FullName, "TaomCoopHost");
        var ensure = SourceFiles.Method(host, "private async Task<bool> EnsureServerProgramAsync");
        Assert.Contains("ServerProgramRepair.BlockingEngineBinFiles(server, workshop)", ensure);
        Assert.Contains("SetControl(MissingServerFilesText(missingBin), failed: true);", ensure);
        Assert.Contains("NoteUnrestorableServerFiles(server);", ensure);
        Assert.Contains("FindWorkshopModule", SourceFiles.Method(host, "private static string? WorkshopServerFolder"));
        // The exit check comes first, and only the else branch reaches the load mode retry.
        var start = SourceFiles.Method(host, "private async Task StartServerCoreAsync");
        var missingAt = start.IndexOf("if (IsMissingServerFilesStop(process, exitCode, server))", StringComparison.Ordinal);
        Assert.True(missingAt >= 0);
        Assert.True(missingAt < start.IndexOf("OnRescueExit(process, exitCode, loadStep);", StringComparison.Ordinal));
        var stop = SourceFiles.Method(host, "private bool IsMissingServerFilesStop");
        Assert.Contains("ServerProgramRepair.MissingAtExit(", stop);
        Assert.Contains("_logTail.ToString(), server, GameModulesFolder())", stop);
        Assert.Contains("OfferServerCrashReport(process, \"Server files are missing\"", stop);
        Assert.DoesNotContain("OnRescueExit(", stop);
        Assert.DoesNotContain("StartAfterLoadCrashAsync", stop);
        // OnRescueExit does not run there, so the stop clears the run's rescue state itself.
        Assert.Contains("CancelRescueRetry();", stop);
        Assert.Contains("_rescueRun.Begin(RescueMode.Auto);", stop);
        Assert.Contains("_rescueNextMode = RescueMode.Auto;", stop);
    }

    private static readonly string StoryModeDll = Path.Combine("engine", "Modules", "StoryMode", "bin", "Win64_Shipping_Server", "StoryMode.dll");
    private static readonly string CustomBattleDll = Path.Combine("engine", "Modules", "CustomBattle", "bin", "Win64_Shipping_Server", "TaleWorlds.MountAndBlade.CustomBattle.dll");
    private static readonly string DependenciesMcm = Path.Combine("engine", "Modules", "TAOM.Dependencies", "bin", "Win64_Shipping_Server", "MCMv5.dll");
    private static readonly string EngineBinMcm = Path.Combine("engine", "bin", "Win64_Shipping_Server", "MCMv5.dll");

    // A game Modules folder holding one file at source, and a pin for it.
    private static (string Modules, (string Target, string Source, long Length, string Sha256)[] Pins) GameModules(string root, string target, string source, string text, string? pinnedText = null)
    {
        var modules = Path.Combine(root, "game", "Modules");
        var path = Path.Combine(modules, source);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        var pinned = Encoding.UTF8.GetBytes(pinnedText ?? text);
        return (modules, new[] { (target, source, (long)pinned.Length, Convert.ToHexString(SHA256.HashData(pinned))) });
    }

    // A game Modules folder with a source file for every ModuleFiles entry.
    private static string FullGameModules(string root)
    {
        var modules = Path.Combine(root, "game", "Modules");
        foreach (var file in ServerProgramRepair.ModuleFiles)
        {
            var path = Path.Combine(modules, file.Source);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "game " + file.Source);
        }

        return modules;
    }

    // Player report CR-KFRG6: antivirus took StoryMode.dll, the CustomBattle DLL and MCMv5.dll, the repair
    // said the server was fine, and every start died 3 seconds in while TAOM's types loaded.
    [Fact]
    public void Missing_module_dlls_with_a_game_source_need_repair()
    {
        var root = NewRoot();
        var server = Server(root);
        var modules = FullGameModules(root);
        foreach (var target in new[] { StoryModeDll, CustomBattleDll, DependenciesMcm, EngineBinMcm })
            File.Delete(Path.Combine(server, target));

        Assert.Equal(ServerProgramState.NeedsRepair, ServerProgramRepair.Check(server, serverRunning: false, gameModules: modules));
        Assert.Equal(new[] { StoryModeDll, CustomBattleDll, DependenciesMcm, EngineBinMcm }, ServerProgramRepair.MissingModuleFiles(server));
        Assert.Equal(new[] { StoryModeDll, CustomBattleDll, DependenciesMcm, EngineBinMcm }, ServerProgramRepair.BlockingModuleFiles(server, modules));
    }

    // A server staged by an older host kit, or for another TAOM version, can lack a name the game's
    // Modules folder lacks too. It starts, and the name only goes to the action log.
    [Fact]
    public void A_missing_module_dll_with_no_game_source_does_not_block()
    {
        var root = NewRoot();
        var server = Server(root);
        var modules = FullGameModules(root);
        var dryIoc = ServerProgramRepair.ModuleFiles.First(file => file.Target.EndsWith("DryIoc.dll", StringComparison.Ordinal));
        File.Delete(Path.Combine(modules, dryIoc.Source));
        File.Delete(Path.Combine(server, dryIoc.Target));

        Assert.Equal(ServerProgramState.Ready, ServerProgramRepair.Check(server, false, gameModules: modules));
        Assert.Empty(ServerProgramRepair.BlockingModuleFiles(server, modules));
        Assert.Equal(new[] { dryIoc.Target }, ServerProgramRepair.MissingModuleFiles(server));
        // No game found at all: nothing blocks either.
        File.Delete(Path.Combine(server, StoryModeDll));
        Assert.Empty(ServerProgramRepair.BlockingModuleFiles(server, null));
        Assert.Equal(ServerProgramState.Ready, ServerProgramRepair.Check(server, false));
    }

    [Fact]
    public void A_missing_module_dll_with_a_game_source_that_fails_its_pin_still_blocks()
    {
        var root = NewRoot();
        var server = Server(root);
        var (modules, pins) = GameModules(root, DependenciesMcm, Path.Combine("TAOM.Dependencies", "bin", "Win64_Shipping_Server", "MCMv5.dll"),
            "MCM from another TAOM version", pinnedText: "MCM from TAOM 2.0.29.5");
        File.Delete(Path.Combine(server, DependenciesMcm));

        var result = ServerProgramRepair.Repair(server, Data(root), null, null, null, modules, pins);

        Assert.False(result.Repaired);
        Assert.Equal(new[] { DependenciesMcm }, ServerProgramRepair.BlockingModuleFiles(server, modules, pins));
    }

    [Fact]
    public void Present_module_dlls_leave_the_server_ready()
    {
        var root = NewRoot();
        var server = Server(root);
        var modules = FullGameModules(root);

        Assert.Equal(ServerProgramState.Ready, ServerProgramRepair.Check(server, false, gameModules: modules));
        Assert.Empty(ServerProgramRepair.MissingModuleFiles(server));
        Assert.Empty(ServerProgramRepair.BlockingModuleFiles(server, modules));
        Assert.Empty(ServerProgramRepair.MissingModuleFiles(null));
        Assert.Empty(ServerProgramRepair.MissingModuleFiles(" "));
        Assert.Empty(ServerProgramRepair.BlockingModuleFiles(" ", modules));
    }

    // The exit decision itself (MissingAtExit), as the TAOM tool runs it before OnRescueExit.
    [Fact]
    public void A_crash_with_module_dlls_gone_is_no_load_crash()
    {
        var root = NewRoot();
        var server = Server(root);
        var modules = FullGameModules(root);
        File.Delete(Path.Combine(server, StoryModeDll));
        var crash = BannerlordCoopLauncher.Core.Server.ServerExitInfo.From(-532462766, wasExpected: false);
        const string log = "Messagebox [Error] message: Cannot find: ..\\..\\Modules\\StoryMode\\bin\\Win64_Shipping_Server\\StoryMode.dll";

        var (engineBin, missingModules) = ServerProgramRepair.MissingAtExit(crash, servedThisRun: false, log, server, modules);

        Assert.Empty(engineBin);
        Assert.Equal(new[] { StoryModeDll }, missingModules);
        // A stop the tool asked for, or a run that served, is never about missing files.
        Assert.Empty(ServerProgramRepair.MissingAtExit(BannerlordCoopLauncher.Core.Server.ServerExitInfo.From(-532462766, wasExpected: true), false, log, server, modules).Modules);
        Assert.Empty(ServerProgramRepair.MissingAtExit(crash, servedThisRun: true, log, server, modules).Modules);
    }

    [Fact]
    public void A_real_save_crash_with_nothing_missing_still_gets_the_save_rescue()
    {
        var root = NewRoot();
        var server = Server(root);
        var modules = FullGameModules(root);
        var crash = BannerlordCoopLauncher.Core.Server.ServerExitInfo.From(-532462766, wasExpected: false);

        var (engineBin, missingModules) = ServerProgramRepair.MissingAtExit(crash, servedThisRun: false, "Loading save Hui", server, modules);

        Assert.Empty(engineBin);
        Assert.Empty(missingModules);
        Assert.Equal(BannerlordCoopLauncher.Core.Support.LoadCrashStep.RetryInLoadMode,
            new BannerlordCoopLauncher.Core.Support.LoadCrashRetry().Decide(crash, false, BannerlordCoopLauncher.Core.Support.RescueMode.Auto));

        // A file from an older layout with no game source never hides a real save crash.
        var dryIoc = ServerProgramRepair.ModuleFiles.First(file => file.Target.EndsWith("DryIoc.dll", StringComparison.Ordinal));
        File.Delete(Path.Combine(modules, dryIoc.Source));
        File.Delete(Path.Combine(server, dryIoc.Target));
        Assert.Empty(ServerProgramRepair.MissingAtExit(crash, false, "Loading save Hui", server, modules).Modules);
    }

    [Fact]
    public void A_missing_module_dll_comes_back_from_the_games_modules_folder_when_it_matches_the_pin()
    {
        var root = NewRoot();
        var server = Server(root);
        var data = Data(root);
        var source = Path.Combine("StoryMode", "bin", "Win64_Shipping_Client", "StoryMode.dll");
        var (modules, pins) = GameModules(root, StoryModeDll, source, "game StoryMode v1.4.8");
        File.Delete(Path.Combine(server, StoryModeDll));
        var dataBefore = Snapshot(data);
        var keptFiles = Snapshot(server);

        var result = ServerProgramRepair.Repair(server, data, null, null, null, modules, pins);

        Assert.True(result.Repaired);
        Assert.Equal(new[] { StoryModeDll }, result.Restored);
        Assert.Equal("game StoryMode v1.4.8", File.ReadAllText(Path.Combine(server, StoryModeDll)));
        Assert.Empty(ServerProgramRepair.MissingModuleFiles(server, pins));
        // A copy, never a move, and nothing else in the server or Data folder changes.
        Assert.True(File.Exists(Path.Combine(modules, source)));
        foreach (var (path, before) in keptFiles)
            Assert.Equal(before, (File.ReadAllText(path), File.GetLastWriteTimeUtc(path)));
        Assert.Equal(dataBefore, Snapshot(data));
    }

    [Fact]
    public void A_module_dll_that_does_not_match_the_pin_is_refused_and_still_missing()
    {
        var root = NewRoot();
        var server = Server(root);
        var data = Data(root);
        // The game's copy is another TAOM version than the pinned one.
        var (modules, pins) = GameModules(root, DependenciesMcm, Path.Combine("TAOM.Dependencies", "bin", "Win64_Shipping_Server", "MCMv5.dll"),
            "MCM from another TAOM version", pinnedText: "MCM from TAOM 2.0.29.5");
        File.Delete(Path.Combine(server, DependenciesMcm));

        var result = ServerProgramRepair.Repair(server, data, null, null, null, modules, pins);

        Assert.False(result.Repaired);
        Assert.Equal(new[] { DependenciesMcm }, result.StillMissing);
        Assert.False(File.Exists(Path.Combine(server, DependenciesMcm)));
        Assert.Equal(new[] { DependenciesMcm }, ServerProgramRepair.MissingModuleFiles(server, pins));

        // No game Modules folder found at all.
        var noGame = ServerProgramRepair.Repair(server, data, null, null, null, null, pins);
        Assert.False(noGame.Repaired);
        Assert.False(File.Exists(Path.Combine(server, DependenciesMcm)));
    }

    [Fact]
    public void A_module_dll_is_never_written_with_the_data_folder_inside_the_server_folder()
    {
        var root = NewRoot();
        var server = Server(root);
        var (modules, pins) = GameModules(root, StoryModeDll, Path.Combine("StoryMode", "bin", "Win64_Shipping_Client", "StoryMode.dll"), "game StoryMode");
        File.Delete(Path.Combine(server, StoryModeDll));

        var result = ServerProgramRepair.Repair(server, Path.Combine(server, "Data"), null, null, null, modules, pins);

        Assert.False(result.Repaired);
        Assert.Contains(StoryModeDll, result.StillMissing);
        Assert.False(File.Exists(Path.Combine(server, StoryModeDll)));
    }

    // The same pins Prepare-Host.ps1 checks for TAOM 2.0.29.5, and the CustomBattle DLL lives in the
    // CustomBattle module, where Stage-TaomCompatServer.ps1 puts it.
    [Fact]
    public void Module_pins_match_the_host_kit()
    {
        var taom = ServerProgramRepair.ModuleFiles.Single(file => file.Target.EndsWith("TAOM.dll", StringComparison.Ordinal));
        Assert.Equal("AEEC07AA61C84F45E9E8662C294CFF68B000F041D8FCA453CF30058CB9AD14D8", taom.Sha256);
        var dependencies = ServerProgramRepair.ModuleFiles.Single(file => file.Target.EndsWith("TAOM.Dependencies.dll", StringComparison.Ordinal));
        Assert.Equal("21F792903622417872A04D9DFA2819C824A3647C1716DF7B9055048C54D7EC7B", dependencies.Sha256);
        Assert.Contains(ServerProgramRepair.ModuleFiles, file => file.Target == CustomBattleDll);
        Assert.Contains(ServerProgramRepair.ModuleFiles, file => file.Target == StoryModeDll);
        // Every pin is a real SHA-256 and a target is never a wrapper file or a Data folder path.
        Assert.All(ServerProgramRepair.ModuleFiles, file =>
        {
            Assert.Matches("^[0-9A-F]{64}$", file.Sha256);
            Assert.True(file.Length > 0);
            Assert.False(ServerProgramRepair.IsWrapperFile(file.Target));
            Assert.StartsWith("engine" + Path.DirectorySeparatorChar, file.Target);
        });
    }

    [Fact]
    public void Missing_module_dlls_stop_the_start_and_their_exit_never_runs_the_save_rescue()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "BannerlordCoopLauncher.sln")))
            dir = dir.Parent;
        var host = SourceFiles.Window(dir!.FullName, "TaomCoopHost");
        var ensure = SourceFiles.Method(host, "private async Task<bool> EnsureServerProgramAsync");
        Assert.Contains("ServerProgramRepair.BlockingModuleFiles(server, gameModules)", ensure);
        Assert.Contains("ServerProgramRepair.Check(server, running, workshop, gameModules, compatByHash: true)", ensure);
        Assert.Contains("SetControl(MissingModuleFilesText(missingModules), failed: true);", ensure);
        Assert.Contains("var gameModules = GameModulesFolder();", ensure);
        Assert.Contains("ServerProgramRepair.MissingModuleFiles(server)", SourceFiles.Method(host, "private void NoteUnrestorableServerFiles"));
        var repair = SourceFiles.Method(host, "private static ServerProgramRepairResult RepairServerProgram");
        Assert.Contains("gameModules: gameModules", repair);
        // The exit check looks at the disk of the folder that started, so a crash with a module DLL gone is never a load crash.
        var stop = SourceFiles.Method(host, "private bool IsMissingServerFilesStop");
        Assert.DoesNotContain("_settings.ServerRoot", stop);
        Assert.Contains("if (missing.Count == 0 && missingModules.Count == 0)", stop);
        Assert.Contains("MissingModuleFilesText(", stop);
        Assert.DoesNotContain("OnRescueExit(", stop);
        Assert.DoesNotContain("StartAfterLoadCrashAsync", stop);
        Assert.Contains("CancelRescueRetry();", stop);
    }

    // Player report CR-HZYX5: antivirus took a TAOM.CoopCompat DLL from the server, its preflight stopped every
    // start, Repair did not know the file and Prepare refused the prepared folder.
    private static readonly string CompatDll = Path.Combine("engine", "Modules", "TAOM.CoopCompat", "bin", "Win64_Shipping_Server", "TAOM.CoopCompat.dll");
    private static readonly string CompatBootstrapDll = Path.Combine("engine", "Modules", "TAOM.CoopCompat", "bin", "Win64_Shipping_Server", "TAOM.CoopCompat.Bootstrap.dll");
    private static readonly string CompatXml = Path.Combine("engine", "Modules", "TAOM.CoopCompat", "SubModule.xml");

    // The host kit's Artifacts\TAOM.CoopCompat\<version>\Modules\TAOM.CoopCompat with a file for each pin, and
    // the pins of those files (or of pinnedText, for a kit copy that is not the pinned one).
    private static (string Kit, (string Target, string Source, long Length, string Sha256)[] Pins) CompatKit(string root, string? pinnedText = null)
    {
        var kit = Path.Combine(root, "kit");
        var module = Path.Combine(kit, "Artifacts", "TAOM.CoopCompat", "0.3.25+test", "Modules", "TAOM.CoopCompat");
        Directory.CreateDirectory(module);
        File.WriteAllText(Path.Combine(module, "SubModule.xml"), "<Module/>");
        var pins = new List<(string, string, long, string)>();
        foreach (var file in ServerProgramRepair.CompatFiles)
        {
            var path = Path.Combine(module, file.Source);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "kit " + file.Source);
            var pinned = Encoding.UTF8.GetBytes(pinnedText ?? "kit " + file.Source);
            pins.Add((file.Target, file.Source, pinned.Length, Convert.ToHexString(SHA256.HashData(pinned))));
        }

        return (kit, pins.ToArray());
    }

    [Fact]
    public void A_missing_compat_dll_needs_repair_and_blocks_the_start()
    {
        var root = NewRoot();
        var server = Server(root);
        Assert.Equal(ServerProgramState.Ready, ServerProgramRepair.Check(server, false));
        Assert.Empty(ServerProgramRepair.MissingCompatFiles(server));

        File.Delete(Path.Combine(server, CompatDll));

        Assert.Equal(ServerProgramState.NeedsRepair, ServerProgramRepair.Check(server, false));
        Assert.Equal(new[] { CompatDll }, ServerProgramRepair.MissingCompatFiles(server));
        Assert.True(ServerProgramRepair.IsKitFile(CompatDll));
        Assert.False(ServerProgramRepair.IsWrapperFile(CompatDll));
    }

    [Fact]
    public void Repair_puts_both_compat_dlls_back_from_the_host_kit()
    {
        var root = NewRoot();
        var server = Server(root);
        var data = Data(root);
        var (kit, pins) = CompatKit(root);
        File.Copy(Path.Combine(TaomServerFiles.KitCompatModule(kit)!, "SubModule.xml"), Path.Combine(server, CompatXml), overwrite: true);
        File.Delete(Path.Combine(server, CompatDll));
        File.Delete(Path.Combine(server, CompatBootstrapDll));
        var dataBefore = Snapshot(data);
        var keptFiles = Snapshot(server);

        var result = ServerProgramRepair.Repair(server, data, null, compatModule: TaomServerFiles.KitCompatModule(kit), compatPins: pins);

        Assert.True(result.Repaired);
        Assert.Equal(new[] { CompatDll, CompatBootstrapDll }, result.Restored);
        Assert.Equal("kit " + Path.Combine("bin", "Win64_Shipping_Server", "TAOM.CoopCompat.dll"), File.ReadAllText(Path.Combine(server, CompatDll)));
        Assert.Equal("kit " + Path.Combine("bin", "Win64_Shipping_Server", "TAOM.CoopCompat.Bootstrap.dll"), File.ReadAllText(Path.Combine(server, CompatBootstrapDll)));
        Assert.Empty(ServerProgramRepair.MissingCompatFiles(server, pins));
        // A copy, never a move, and nothing else in the server or Data folder changes.
        Assert.All(pins, pin => Assert.True(File.Exists(Path.Combine(TaomServerFiles.KitCompatModule(kit)!, pin.Source))));
        foreach (var (path, before) in keptFiles)
            Assert.Equal(before, (File.ReadAllText(path), File.GetLastWriteTimeUtc(path)));
        Assert.Equal(dataBefore, Snapshot(data));
    }

    [Fact]
    public void A_compat_dll_that_does_not_match_the_pin_or_has_no_kit_stays_missing()
    {
        var root = NewRoot();
        var server = Server(root);
        var data = Data(root);
        var (kit, pins) = CompatKit(root, pinnedText: "TAOM.CoopCompat from another kit");
        File.Delete(Path.Combine(server, CompatDll));

        var result = ServerProgramRepair.Repair(server, data, null, compatModule: TaomServerFiles.KitCompatModule(kit), compatPins: pins);

        Assert.False(result.Repaired);
        // The server's other compat files are not the pinned ones either, and with no pinned kit copy they stay as they are.
        Assert.Equal(new[] { CompatXml, CompatDll, CompatBootstrapDll }, result.StillMissing);
        Assert.Equal("prepared " + CompatXml, File.ReadAllText(Path.Combine(server, CompatXml)));
        Assert.Equal("prepared " + CompatBootstrapDll, File.ReadAllText(Path.Combine(server, CompatBootstrapDll)));
        Assert.False(File.Exists(Path.Combine(server, CompatDll)));

        var noKit = ServerProgramRepair.Repair(server, data, null, compatPins: pins);
        Assert.Equal(new[] { CompatDll }, noKit.StillMissing);

        // The Data folder inside the server folder: nothing is written.
        var (_, goodPins) = CompatKit(NewRoot());
        var overlap = ServerProgramRepair.Repair(server, Path.Combine(server, "Data"), null, compatModule: TaomServerFiles.KitCompatModule(kit), compatPins: goodPins);
        Assert.Contains(CompatDll, overlap.StillMissing);
        Assert.False(File.Exists(Path.Combine(server, CompatDll)));
    }

    // The kit 0.3.25 TAOM.CoopCompat (its ARTIFACT.json dllSha256 and bootstrapDllSha256), in the server bin the
    // staging copies to.
    [Fact]
    public void Compat_pins_match_the_host_kit()
    {
        var main = ServerProgramRepair.CompatFiles.Single(file => file.Target == CompatDll);
        Assert.Equal(("E0B27271032D062DE45A0E29A31C52A278EF9680A80AC11EBD1311FE6D84B21A", 1227776L), (main.Sha256, main.Length));
        var bootstrap = ServerProgramRepair.CompatFiles.Single(file => file.Target == CompatBootstrapDll);
        Assert.Equal(("B105EAC2EE225BEF5AC02C2BBD98F5C7D5D1D5DE546558C98F825B9CB4800C0B", 18432L), (bootstrap.Sha256, bootstrap.Length));
        // The kit SubModule.xml, its ARTIFACT.json descriptor hash and the wrapper's descriptor pin.
        var descriptor = ServerProgramRepair.CompatFiles.Single(file => file.Target == CompatXml);
        Assert.Equal(("EB46CE58436D7FC61BAFEEA15FED14C6712960B439F3FFF3AB2246EA39814E08", 2108L), (descriptor.Sha256, descriptor.Length));
        Assert.Equal("0.3.25", TaomStack.KitVersion);
        Assert.All(ServerProgramRepair.CompatFiles, file =>
        {
            Assert.Equal(Path.Combine("engine", "Modules", TaomStack.CompatModuleId, file.Source), file.Target);
            Assert.False(ServerProgramRepair.IsWrapperFile(file.Target));
        });
    }

    // Player report CR-XXP67: the server's TAOM.CoopCompat.dll was another build ("feature DLL hash ... does not match
    // the wrapper pin"), and after that its SubModule.xml was gone ("module descriptor is missing"). Repair only
    // looked for missing DLLs, so every start stopped the same way.
    [Fact]
    public void Repair_replaces_a_changed_compat_dll_and_puts_back_the_descriptor_from_the_kit()
    {
        var root = NewRoot();
        var server = Server(root);
        var data = Data(root);
        var (kit, pins) = CompatKit(root);
        var kitModule = TaomServerFiles.KitCompatModule(kit)!;
        foreach (var pin in pins)
            File.Copy(Path.Combine(kitModule, pin.Source), Path.Combine(server, pin.Target), overwrite: true);
        Assert.Empty(ServerProgramRepair.MissingCompatFiles(server, pins, byHash: true));
        File.WriteAllText(Path.Combine(server, CompatDll), "another TAOM.CoopCompat build");
        File.Delete(Path.Combine(server, CompatXml));
        var dataBefore = Snapshot(data);

        // Only a missing file counts without byHash; Start checks by hash.
        Assert.Equal(new[] { CompatXml }, ServerProgramRepair.MissingCompatFiles(server, pins));
        Assert.Equal(new[] { CompatXml, CompatDll }, ServerProgramRepair.MissingCompatFiles(server, pins, byHash: true));
        Assert.Equal(ServerProgramState.NeedsRepair, ServerProgramRepair.Check(server, false, compatByHash: true));

        var result = ServerProgramRepair.Repair(server, data, null, compatModule: kitModule, compatPins: pins);

        Assert.True(result.Repaired);
        Assert.Equal(new[] { CompatXml, CompatDll }, result.Restored);
        Assert.Equal("kit " + Path.Combine("bin", "Win64_Shipping_Server", "TAOM.CoopCompat.dll"), File.ReadAllText(Path.Combine(server, CompatDll)));
        Assert.Equal("kit SubModule.xml", File.ReadAllText(Path.Combine(server, CompatXml)));
        Assert.Empty(ServerProgramRepair.MissingCompatFiles(server, pins, byHash: true));
        Assert.Empty(Directory.GetFiles(server, "*.coodsz-repair", SearchOption.AllDirectories));
        Assert.Equal(dataBefore, Snapshot(data));

        // Without the kit a changed file is never removed.
        File.WriteAllText(Path.Combine(server, CompatDll), "another TAOM.CoopCompat build");
        Assert.Empty(ServerProgramRepair.Repair(server, data, null, compatPins: pins).Restored);
        Assert.Equal("another TAOM.CoopCompat build", File.ReadAllText(Path.Combine(server, CompatDll)));
    }

    // A changed file that is in use can't be swapped: it stays, the half step is cleaned up and the log says why.
    [WindowsFact]
    public void A_changed_compat_dll_in_use_stays_and_the_log_says_why()
    {
        var root = NewRoot();
        var server = Server(root);
        var data = Data(root);
        var (kit, pins) = CompatKit(root);
        var kitModule = TaomServerFiles.KitCompatModule(kit)!;
        foreach (var pin in pins)
            File.Copy(Path.Combine(kitModule, pin.Source), Path.Combine(server, pin.Target), overwrite: true);
        File.WriteAllText(Path.Combine(server, CompatDll), "another TAOM.CoopCompat build");
        var log = new List<string>();

        ServerProgramRepairResult result;
        using (File.Open(Path.Combine(server, CompatDll), FileMode.Open, FileAccess.Read, FileShare.Read))
            result = ServerProgramRepair.Repair(server, data, null, compatModule: kitModule, compatPins: pins, log: log.Add);

        Assert.Equal(new[] { CompatDll }, result.StillMissing);
        Assert.Equal("another TAOM.CoopCompat build", File.ReadAllText(Path.Combine(server, CompatDll)));
        Assert.Empty(Directory.GetFiles(server, "*.coodsz-repair", SearchOption.AllDirectories));
        Assert.StartsWith("Could not replace " + Path.Combine(server, CompatDll) + ". ", Assert.Single(log));
    }

    // A server TAOM.CoopCompat whose SubModule.xml can't be read and that has neither a Coop DLL nor a kit Bootstrap DLL:
    // nothing tells which one it is, so Repair writes nothing there.
    [WindowsFact]
    public void Repair_writes_nothing_into_a_compat_folder_it_cannot_identify()
    {
        var root = NewRoot();
        var server = Server(root);
        var data = Data(root);
        var (kit, pins) = CompatKit(root);
        File.Delete(Path.Combine(server, CompatBootstrapDll));
        File.WriteAllText(Path.Combine(server, CompatDll), "unknown TAOM.CoopCompat build");
        var module = Path.Combine(server, ServerProgramRepair.CompatFolder);
        var before = Directory.EnumerateFiles(module, "*", SearchOption.AllDirectories).Where(path => path != Path.Combine(server, CompatXml))
            .ToDictionary(path => path, File.ReadAllText);

        ServerProgramRepairResult result;
        using (File.Open(Path.Combine(server, CompatXml), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.Equal(NewTaomCompat.UnsureText(module), NewTaomCompat.Problem(module));
            Assert.Empty(ServerProgramRepair.MissingCompatFiles(server, pins, byHash: true));
            result = ServerProgramRepair.Repair(server, data, null, compatModule: TaomServerFiles.KitCompatModule(kit), compatPins: pins);
        }

        Assert.Empty(result.Restored);
        Assert.Equal(before, Directory.EnumerateFiles(module, "*", SearchOption.AllDirectories).Where(path => path != Path.Combine(server, CompatXml))
            .ToDictionary(path => path, File.ReadAllText));
        Assert.False(File.Exists(Path.Combine(server, CompatBootstrapDll)));
    }

    // The official TAOM.CoopCompat v2.0.40 from the new TAOM download for Bannerlord 1.5.7 in the server folder.
    [Fact]
    public void Repair_and_start_leave_the_new_TAOM_download_compat_alone()
    {
        var root = NewRoot();
        var server = Server(root);
        var data = Data(root);
        var (kit, pins) = CompatKit(root);
        var module = Path.Combine(server, ServerProgramRepair.CompatFolder);
        Directory.Delete(module, recursive: true);
        Directory.CreateDirectory(Path.Combine(module, "bin", "Win64_Shipping_Server"));
        File.WriteAllText(Path.Combine(module, "SubModule.xml"), "<Module>\r\n  <Id value=\"TAOM.CoopCompat\" />\r\n  <Version value=\"v2.0.40\" />\r\n</Module>");
        File.WriteAllText(Path.Combine(module, "bin", "Win64_Shipping_Server", "TAOM.CoopCompat.dll"), "official");
        File.WriteAllText(Path.Combine(module, "bin", "Win64_Shipping_Server", "TAOM.CoopCompat.Coop.dll"), "official coop");
        var before = Snapshot(module);

        Assert.Equal(NewTaomCompat.Text("v2.0.40"), NewTaomCompat.Problem(module));
        Assert.Empty(ServerProgramRepair.MissingCompatFiles(server, pins, byHash: true));
        var result = ServerProgramRepair.Repair(server, data, null, compatModule: TaomServerFiles.KitCompatModule(kit), compatPins: pins);

        Assert.Empty(result.Restored);
        Assert.Empty(result.StillMissing);
        Assert.Equal(before, Snapshot(module));

        // Start server says why and stops before the repair and the start.
        var host = SourceFiles.Window(SourceFiles.Root(), "TaomCoopHost");
        var ensure = SourceFiles.Method(host, "private async Task<bool> EnsureServerProgramAsync");
        var guard = ensure.IndexOf("NewTaomCompat.Problem(Path.Combine(server, ServerProgramRepair.CompatFolder))) is { } kept)", StringComparison.Ordinal);
        Assert.True(guard >= 0 && guard < ensure.IndexOf("RepairServerProgram(", StringComparison.Ordinal));
        Assert.Contains("SetControl(kept, failed: true);", ensure[guard..]);
    }

    [Fact]
    public void Start_puts_compat_dlls_back_from_the_kit_and_stops_when_it_cannot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "BannerlordCoopLauncher.sln")))
            dir = dir.Parent;
        var host = SourceFiles.Window(dir!.FullName, "TaomCoopHost");
        var repair = SourceFiles.Method(host, "private static ServerProgramRepairResult RepairServerProgram");
        Assert.Contains("ServerProgramRepair.MissingCompatFiles(server, byHash: true).Count > 0", repair);
        Assert.Contains("compatModule: TaomServerFiles.KitCompatModule(hostKit)", repair);
        Assert.Contains("compatModule: TaomServerFiles.KitCompatModule(kit)", repair);
        Assert.Contains("result.StillMissing.Any(ServerProgramRepair.IsKitFile)", repair);
        var ensure = SourceFiles.Method(host, "private async Task<bool> EnsureServerProgramAsync");
        Assert.Contains("SetControl(changed.Count > 0 ? ChangedCompatFilesText(changed) : MissingCompatFilesText(missingCompat), failed: true);", ensure);
        Assert.Contains("ServerProgramRepair.MissingCompatFiles(server, byHash: true));", ensure);
        Assert.Contains("could not be used. Antivirus most likely removed them. Click Fix antivirus", host);
        Assert.Contains("Content=\"Fix antivirus\"", SourceFiles.Read("src/TaomCoopHost/MainWindow.xaml"));
    }
}
