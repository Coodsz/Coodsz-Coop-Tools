using System.Xml.Linq;
using BannerlordCoopLauncher.Core.Configuration;
using BannerlordCoopLauncher.Core.Modules;
using BannerlordCoopLauncher.Core.Server;
using Coodsz.ServerRules;
using CoodszSettingsSync;
using CoodszWarSync;
using TaomCoop.Core;

namespace BannerlordCoopLauncher.Tests;

// The host rules and the mod settings for players on the TAOM Server Tool (TaomServerTools): the same Coodsz Server
// Tools module and defaults as the Coop and Realm of Thrones Server Tools, riding in DedicatedServer.Windows because
// the TAOM host only loads its sealed module list.
public class TaomServerToolsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "coodsz-taomtools-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    // The Coop Server Tool's build of the module; the TAOM exe carries its two DLLs as the ServerTools payload.
    private static string PayloadBin => Path.Combine(SourceFiles.Root(), "src", "CoodszHost", "ServerTools", ServerToolsModule.ModuleId, "bin", "Win64_Shipping_Server");

    private const string ServerWindowsXml = """
        <?xml version="1.0" encoding="utf-8"?>
        <Module>
          <Name value="DedicatedServer.Windows"/>
          <Id value="DedicatedServer.Windows"/>
          <SubModules>
            <SubModule>
              <Name value="DedicatedServer.Windows"/>
              <DLLName value="DedicatedServer.Windows.dll"/>
              <SubModuleClassType value="DedicatedServer.Windows.SubModule"/>
            </SubModule>
          </SubModules>
        </Module>
        """;

    private string PreparedServer()
    {
        var server = Path.Combine(_root, "server");
        Directory.CreateDirectory(ServerWindowsSubModules.ModuleDirectory(server));
        File.WriteAllText(Path.Combine(ServerWindowsSubModules.ModuleDirectory(server), "SubModule.xml"), ServerWindowsXml.Replace("\n", "\r\n"));
        return server;
    }

    [Fact]
    public void The_module_rides_in_DedicatedServer_Windows_once_next_to_War_Sync_which_steps_aside_for_it()
    {
        var server = PreparedServer();
        WarSyncSubModule.Install(server, Path.Combine(SourceFiles.Root(), "src", "TaomCoopHost", WarSyncSubModule.PayloadFolder));
        Assert.False(TaomServerTools.IsInstalled(server));

        TaomServerTools.Install(server, PayloadBin);
        TaomServerTools.Install(server, PayloadBin); // twice adds it once
        var xml = File.ReadAllText(Path.Combine(ServerWindowsSubModules.ModuleDirectory(server), "SubModule.xml"));

        Assert.True(TaomServerTools.IsInstalled(server));
        Assert.True(WarSyncSubModule.IsInstalled(server));
        Assert.Equal(1, xml.Split("<Name value=\"Coodsz Server Tools\"/>").Length - 1);
        Assert.Contains("<DLLName value=\"DedicatedServer.CoodszTools.dll\"/>", xml);
        // The class the module's own SubModule.xml names.
        var own = XDocument.Load(Path.Combine(PayloadBin, "..", "..", "SubModule.xml"));
        Assert.Equal(TaomServerTools.ClassType, own.Descendants("SubModuleClassType").Single().Attribute("value")!.Value);
        Assert.Contains("<SubModuleClassType value=\"" + TaomServerTools.ClassType + "\"/>", xml);
        Assert.DoesNotContain("\n", xml.Replace("\r\n", ""));
        // One settings sync DLL in the bin folder: the same file War Sync brought.
        var bin = ServerWindowsSubModules.BinDirectory(server);
        Assert.Equal(File.ReadAllBytes(Path.Combine(PayloadBin, TaomServerTools.SyncDllName)), File.ReadAllBytes(Path.Combine(bin, TaomServerTools.SyncDllName)));
        Assert.Equal(File.ReadAllBytes(Path.Combine(PayloadBin, TaomServerTools.DllName)), File.ReadAllBytes(Path.Combine(bin, TaomServerTools.DllName)));

        // With the module loaded, War Sync stays off and the module's sync sends the War of the Ring state: one sync runs.
        Assert.Equal(WarSyncRules.FullToolsAssembly + ".dll", TaomServerTools.DllName);
        Assert.False(WarSyncRules.ShouldRun(true, new[] { "CoodszWarSync", WarSyncRules.FullToolsAssembly }));
        // recipes.json for the looks mods goes where the module runs from, which is where its sync reads it.
        Assert.Equal(ServerWindowsSubModules.ModuleDirectory(server), TaomServerTools.ModuleFolder(server));
    }

    [Fact]
    public void Install_refuses_a_missing_payload_or_an_unprepared_server_and_the_start_goes_on()
    {
        var payload = Path.Combine(_root, "payload");
        Directory.CreateDirectory(payload);
        File.WriteAllText(Path.Combine(payload, TaomServerTools.DllName), "tools");
        var server = PreparedServer();

        var missing = Assert.Throws<InvalidOperationException>(() => TaomServerTools.Install(server, payload));
        Assert.Contains(ServerToolsModule.Name, missing.Message);
        Assert.False(TaomServerTools.IsInstalled(server));

        File.WriteAllText(Path.Combine(payload, TaomServerTools.SyncDllName), "sync");
        var bare = Path.Combine(_root, "bare");
        Assert.Throws<InvalidOperationException>(() => TaomServerTools.Install(bare, payload));
        Assert.False(Directory.Exists(bare));

        Assert.Contains("uses the copy already there", TaomServerTools.InstallFailed(true, "Locked."));
        Assert.Contains("the server rules and the mod settings for players are off on this start", TaomServerTools.InstallFailed(false, "Locked."));
    }

    [Fact]
    public void The_TAOM_start_names_its_data_folder_so_the_module_and_the_sync_never_read_the_Coop_Server_Tools_files()
    {
        var documents = Path.Combine(_root, "Documents");
        var dataRoot = Path.Combine(_root, "TAOM-Coop-Host", "Data");
        var variables = TaomServerTools.StartVariables(dataRoot);
        Assert.Equal(dataRoot, variables[ServerToolsModule.DataDirVariable]);
        Assert.Equal(ServerToolsModule.DataDirVariable, ClanCapSettings.ServerDataDirVariable);
        Assert.Equal(ServerToolsModule.DataDirVariable, SettingsFiles.ServerDataDirVariable);
        Assert.Equal(TaomServerTools.RulesPath(dataRoot), ClanCapSettings.PathIn(documents, variables[ServerToolsModule.DataDirVariable]));

        // The rules the tool writes are what the module reads; no file gives the Coop Server Tool's defaults on both sides.
        Assert.Equal(HostServerRules.Defaults, ServerRulesFile.Read(TaomServerTools.RulesPath(dataRoot)));
        ServerRulesFile.Write(TaomServerTools.RulesPath(dataRoot), new HostServerRules(UnstuckChoice.NotInFights, true, false, KingdomProtectionChoice.Always));
        var read = ServerRulesSettings.Parse(File.ReadAllText(ClanCapSettings.PathIn(documents, dataRoot)));
        Assert.Equal(UnstuckMode.NotInFights, read.Unstuck);
        Assert.True(read.PauseOnJoin);
        Assert.False(read.FreezeOfflineXp);
        Assert.Equal(KingdomProtectionMode.Always, read.KingdomProtection);

        // The sync: the Coop Server Tool's CoopData folder never, this PC's MCM files until the server's own copy has one.
        var coopServerTool = Path.Combine(documents, "Mount and Blade II Bannerlord", "CoopData", "DedicatedServer", "Configs", "ModSettings");
        var game = Path.Combine(documents, "Mount and Blade II Bannerlord", "Configs", "ModSettings");
        Directory.CreateDirectory(coopServerTool);
        Directory.CreateDirectory(game);
        Assert.Equal(game, SettingsFiles.FindModSettingsRoot(documents, dataRoot));
        var own = ServerModSettings.ServerRoot(dataRoot);
        Directory.CreateDirectory(Path.Combine(own, "Global", "RBM"));
        File.WriteAllText(Path.Combine(own, "Global", "RBM", "RBM_v1.json"), "{}");
        Assert.Equal(own, SettingsFiles.FindModSettingsRoot(documents, dataRoot));
    }

    [Fact]
    public void The_kingdom_choice_writes_Coops_own_option_where_Coop_0_1_6_reads_it()
    {
        // Coop 0.1.6 reads modOptions.blockAiWarDeclarationsOnOfflinePlayers (GameInterface\Configuration\ModConfigData.cs).
        const string key = "blockAiWarDeclarationsOnOfflinePlayers";
        Assert.Equal(ServerRulesFile.CoopOfflineWarOption, "modOptions." + key);
        Assert.Equal("modOptions", CoopModConfig.SectionOf(key));
        var window = SourceFiles.Window(SourceFiles.Root(), "TaomCoopHost");
        Assert.Contains("private static readonly string CoopOfflineWarKey = ServerRulesFile.CoopOfflineWarOption[(ServerRulesFile.CoopOfflineWarOption.IndexOf('.') + 1)..];", window);

        // Missing is off, like Coop's own default; the pick goes inside the modOptions block.
        var json = "{\r\n  \"modOptions\": {\r\n    \"fastForwardEnabled\": true\r\n  }\r\n}";
        Assert.False(CoopModConfig.ReadBool(json, key));
        var on = CoopModConfig.SetBool(json, key, ServerRulesFile.CoopOfflineWarValue(KingdomProtectionChoice.OfflineOnly));
        Assert.True(CoopModConfig.ReadBool(on, key));
        Assert.Contains("\"modOptions\"", on);
        Assert.True(on.IndexOf(key, StringComparison.Ordinal) > on.IndexOf("\"modOptions\"", StringComparison.Ordinal));
        Assert.False(CoopModConfig.ReadBool(CoopModConfig.SetBool(on, key, ServerRulesFile.CoopOfflineWarValue(KingdomProtectionChoice.Off)), key));
        // An untouched dropdown keeps following Coop's option: nothing is written.
        Assert.Null(ServerRulesFile.KingdomChoiceToSave(KingdomProtectionChoice.OfflineOnly, null, coopOption: true));
    }

    [Fact]
    public void The_looks_mods_go_into_recipes_json_in_DedicatedServer_Windows()
    {
        var server = PreparedServer();
        var dataRoot = Path.Combine(_root, "Data");
        var game = Path.Combine(_root, "Documents", "Configs", "ModSettings");
        foreach (var (folder, id, text) in new[] { ("AggregatedIncome", "AggregatedIncome_v1", "{\"Show\": true}"), ("RBM", "RBM_v1", "{\"Speed\": 1}") })
        {
            Directory.CreateDirectory(Path.Combine(game, "Global", folder));
            File.WriteAllText(Path.Combine(game, "Global", folder, id + ".json"), text);
        }

        Assert.Equal(2, ServerModSettings.Prepare(dataRoot, TaomServerTools.ModuleFolder(server), game, ModCompatDatabase.Embedded()));
        var keep = SettingsFiles.ReadKeepOwn(Path.Combine(ServerWindowsSubModules.ModuleDirectory(server), KeepOwnList.FileName));
        Assert.True(keep.Keeps("AggregatedIncome_v1"));
        Assert.False(keep.Keeps("RBM_v1"));
        Assert.Equal(new[] { "RBM_v1" }, SettingsFiles.CaptureFromFiles(ServerModSettings.ServerRoot(dataRoot), keep).Select(snapshot => snapshot.SettingsId));
    }

    [Fact]
    public void The_exe_and_the_release_carry_the_module_and_one_tool_sets_the_variable_each()
    {
        var csproj = SourceFiles.Read(@"src\TaomCoopHost\TaomCoopHost.csproj");
        Assert.Contains(@"<CoodszPayload Include=""..\CoodszHost\ServerTools\DedicatedServer.CoodszTools\bin\Win64_Shipping_Server\*.dll"" Payload=""ServerTools"" />", csproj);
        Assert.Equal("ServerTools", TaomServerTools.Payload);
        Assert.Equal(new[] { TaomServerTools.SyncDllName, TaomServerTools.DllName }.Order(), Directory.GetFiles(PayloadBin).Select(Path.GetFileName).Order());
        // The module's sync DLL is War Sync's file, so the two never fight over the one name in the bin folder.
        Assert.Equal(File.ReadAllBytes(Path.Combine(PayloadBin, TaomServerTools.SyncDllName)),
            File.ReadAllBytes(Path.Combine(SourceFiles.Root(), "src", "TaomCoopHost", WarSyncSubModule.PayloadFolder, WarSyncSubModule.SyncDllName)));
        var release = SourceFiles.Read(@"tools\Make-Release.ps1");
        Assert.Contains(@"Assert-Payload $taomHost ""$payloads\taom-host"" 'ServerTools' @(@('', 'src\CoodszHost\ServerTools\DedicatedServer.CoodszTools\bin\Win64_Shipping_Server\DedicatedServer.CoodszTools.dll'), @('', 'src\CoodszHost\ServerTools\DedicatedServer.CoodszTools\bin\Win64_Shipping_Server\CoodszSettingsSync.Coop.dll'))", release);

        var window = SourceFiles.Window(SourceFiles.Root(), "TaomCoopHost");
        var start = SourceFiles.Method(window, "private async Task StartServerCoreAsync()");
        // Every start, both paths (the sealed server and the Coodsz TAOM server), names the data folder.
        Assert.Contains("foreach (var (key, value) in TaomServerTools.StartVariables(_settings.DataRoot))", start);
        Assert.True(start.IndexOf("TaomServerTools.StartVariables", StringComparison.Ordinal) > start.IndexOf("if (engine != null)", StringComparison.Ordinal));
        Assert.True(start.IndexOf("InstallServerTools(server);", StringComparison.Ordinal) > start.IndexOf("InstallWarSync(server);", StringComparison.Ordinal));
        Assert.Contains("ApplyKingdomProtection();", start);
        Assert.Contains("InstallServerTools(serverRoot);", SourceFiles.Method(window, "private async void Prepare_Click(object sender, RoutedEventArgs e)"));
        // The mod load test runs the same server folder, so it names the data folder too.
        Assert.Contains("TaomServerTools.StartVariables(owner._settings.DataRoot)", window);
    }

    [Fact]
    public void The_Settings_page_has_the_Coop_Server_Tools_controls_and_defaults_tucked_away()
    {
        var taom = XDocument.Parse(SourceFiles.Read(@"src\TaomCoopHost\MainWindow.xaml"));
        var coop = XDocument.Parse(SourceFiles.Read(@"src\CoodszHost\MainWindow.xaml"));
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        XElement Named(XDocument page, string name) => page.Descendants().Single(element => (string?)element.Attribute(x + "Name") == name);

        foreach (var box in new[] { "PauseOnJoinBox", "FreezeOfflineXpBox" })
        {
            Assert.Equal("True", (string?)Named(taom, box).Attribute("IsChecked"));
            Assert.Equal((string?)Named(coop, box).Attribute("ToolTip"), (string?)Named(taom, box).Attribute("ToolTip"));
        }
        foreach (var box in new[] { "UnstuckBox", "KingdomProtectionBox" })
            Named(taom, box);
        XNamespace wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var fold = taom.Descendants(wpf + "Expander").Single(item => (string?)item.Attribute(XName.Get("AutomationProperties.AutomationId")) == "SettingServerRules");
        Assert.Null(fold.Attribute("IsExpanded"));

        var window = SourceFiles.Window(SourceFiles.Root(), "TaomCoopHost");
        Assert.Contains("private bool _pauseOnJoin = HostServerRules.Defaults.PauseOnJoin;", window);
        Assert.Contains("StartServerRules();", window);
        Assert.Contains("NoteJoinPause(_onlinePlayers, serving);", window);
        Assert.Contains("_playHold.Line(line, DateTime.UtcNow, _joinPause.Pausing)", window);
        Assert.Contains("_playHold.ResendStands(generation, command, _joinPause.Pausing);", window);
        Assert.Contains("ShowKingdomProtection();", SourceFiles.Method(window, "private void ShowPage(string page)"));
        Assert.Contains("json = SaveKingdomProtection(json);", SourceFiles.Method(window, "private void SaveTimePermissions()"));
        var save = SourceFiles.Method(window, "private bool SaveSettingsPage(bool fromSettingsPage = true)");
        Assert.True(save.IndexOf("ServerRulesFromPage()", StringComparison.Ordinal) < save.IndexOf("SaveTimePermissions();", StringComparison.Ordinal));
        Assert.Contains("SaveServerRules(serverRules);", save);
        Assert.Contains("RunCommandAndRead(_joinPause.ResumeCommand, () => _joinPause.HostCommandCount == hostCommands)", window);
        Assert.Contains("RunCommandAndRead(JoinPause.PauseCommand, () => _joinPause.HostCommandCount == asked)", window);
        // The host's own time commands win over a join pause: the buttons, the console and a Play hold resend.
        Assert.Contains("_joinPause.HostSent(command);", SourceFiles.Method(window, "private async Task SetServerTime(string mode)"));
        Assert.Contains("_joinPause.HostSent(command);", SourceFiles.Method(window, "private async void SendConsoleCommand(string command)"));
        Assert.Contains("_joinPause.HostSent(command);", SourceFiles.Method(window, "private async void NotePlayHold(string line)"));
    }

    [Fact]
    public void Every_command_goes_through_the_one_ordered_queue()
    {
        var window = SourceFiles.Window(SourceFiles.Root(), "TaomCoopHost");
        Assert.Contains("return (await _commands.Enqueue(async () => (await ReadCommandReply(command, run))!))!;", window);
        Assert.Contains("}, () => PlayHoldStillStands(generation, command));", window);
        Assert.Contains("return _commandWriteFailed ? null : lines;", window);
        Assert.Contains("_commands.WriteWithin(write, QueuedWriteWait)", window);
        Assert.Contains("WriteQueued(() => WriteLine(process, \"save\"))", window);
        Assert.Contains("return WriteQueued(() => WriteLine(process, line));", window);
        Assert.Contains("SendQueued(\"kick \" + player.Id, StillConnected(player));", window);
        Assert.DoesNotContain("SendCommand(\"kick ", window);
        Assert.DoesNotContain("_readingCommand", window);
        Assert.DoesNotContain("CollectCommandReply", window);
        // Only ReadCommandReply and WriteLine write to the server's console pipe, each under the stdin lock. (The mod test
        // writes to its own test server's pipe.)
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(window, @"ServerCommandQueue\.(WriteBounded|WriteWithinAsync|TryWriteWithin)\(_stdinLock, ServerCommandQueue\.StdinLockWait").Count);
        Assert.Contains("process.StandardInput.WriteLine(command);", SourceFiles.Method(window, "private async Task<IReadOnlyList<string>?> ReadCommandReply(string command, ServerRun run, Func<bool>? stillValid = null)"));
        Assert.Contains("process.StandardInput.WriteLine(line);", SourceFiles.Method(window, "private bool WriteLine(System.Diagnostics.Process process, string line)"));
        Assert.DoesNotContain("_process.StandardInput", window);
        Assert.DoesNotContain("_process!.StandardInput", window);
        // A server this window did not start still gets commands through its console queue.
        Assert.Contains("var (reply, sent) = await SendThroughQueue(command);", SourceFiles.Method(window, "private async Task<IReadOnlyList<string>?> ReadCommandReply(string command, ServerRun run, Func<bool>? stillValid = null)"));
    }

    [Fact]
    public void The_mod_settings_fold_is_the_Realm_of_Thrones_page_closed_with_Import_first_and_recipes_json_in_DedicatedServer_Windows()
    {
        var taom = XDocument.Parse(SourceFiles.Read(@"src\TaomCoopHost\MainWindow.xaml"));
        var rot = XDocument.Parse(SourceFiles.Read(@"src\RealmOfThronesHost\MainWindow.xaml"));
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        XNamespace wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XElement Named(XDocument page, string name) => page.Descendants().Single(element => (string?)element.Attribute(x + "Name") == name);
        XElement Fold(XDocument page) => page.Descendants(wpf + "Expander").Single(item => (string?)item.Attribute(XName.Get("AutomationProperties.AutomationId")) == "SettingModSettings");

        Assert.Null(Fold(taom).Attribute("IsExpanded"));
        // The same text, buttons and handlers as the Realm of Thrones page; only the colours are TAOM's.
        static IEnumerable<string> Words(XElement fold) => fold.DescendantsAndSelf()
            .SelectMany(element => new[] { (string?)element.Attribute("Text"), (string?)element.Attribute("Content"), (string?)element.Attribute("ToolTip"), (string?)element.Attribute("Click") })
            .Where(text => text != null)!;
        Assert.Equal(Words(Fold(rot)), Words(Fold(taom)));
        // No MCM files on the server or this PC yet: Import comes first, right after the note.
        var empty = Named(taom, "ModSettingsEmpty");
        Assert.Equal("ModSettingsNote", (string?)empty.ElementsBeforeSelf().Last().Attribute(x + "Name"));
        Assert.Equal("ImportModSettingsZip_Click", (string?)empty.Descendants().First(element => element.Name.LocalName == "Button").Attribute("Click"));

        var window = SourceFiles.Window(SourceFiles.Root(), "TaomCoopHost");
        Assert.Contains("ShowModSettings();", SourceFiles.Method(window, "private void ShowPage(string page)"));
        var show = SourceFiles.Method(window, "private void ShowModSettings(string? keep = null)");
        Assert.Contains("ServerModSettings.WritePlayersOwn(TaomServerTools.ModuleFolder(_settings.ServerRoot), _modFiles, line => NoteLog(", show);
        Assert.Contains("private string ModSettingsRoot => ServerModSettings.ServerRoot(_settings.DataRoot);", window);
        Assert.Contains("private bool ModSettingsServerUp => _startingServer || OwnsServerProcess() || ServerIsRunning();", window);
        Assert.Contains("TaomLoadOrder.ServerOrder(HostSetup)", SourceFiles.Method(window, "private List<string> ServerModNames()"));
        // The page's folder is the one the start names, which is the one the sync reads.
        Assert.Contains("ServerModSettings.Prepare(_settings.DataRoot, TaomServerTools.ModuleFolder(server), ", window);
    }

    [Fact]
    public void A_queued_command_goes_out_only_to_the_run_it_was_queued_for()
    {
        using var current = System.Diagnostics.Process.GetCurrentProcess();
        using var other = System.Diagnostics.Process.GetCurrentProcess();
        using var neverStarted = new System.Diagnostics.Process();
        Assert.True(ServerCommandQueue.SameRun(current, current));
        // A crash or stop, then a new start: another process object, so a pending command never reaches the new run.
        Assert.False(ServerCommandQueue.SameRun(current, other));
        Assert.False(ServerCommandQueue.SameRun(current, (System.Diagnostics.Process?)null));
        Assert.False(ServerCommandQueue.SameRun(null, current));
        Assert.False(ServerCommandQueue.SameRun(neverStarted, neverStarted));
        // No process on either side is no run: a tool opened again with no process never sends to a restarted server.
        Assert.False(ServerCommandQueue.SameRun((System.Diagnostics.Process?)null, null));
        // A server this window did not start is matched by its start time, and only while the window still has no process.
        var started = new DateTime(2026, 11, 19, 10, 0, 0, DateTimeKind.Utc);
        Assert.True(ServerCommandQueue.SameRun(new ServerRun(null, started), new ServerRun(null, started)));
        Assert.False(ServerCommandQueue.SameRun(new ServerRun(null, started), new ServerRun(null, started.AddMinutes(1))));
        Assert.False(ServerCommandQueue.SameRun(new ServerRun(null, started), new ServerRun(null, null)));
        Assert.False(ServerCommandQueue.SameRun(new ServerRun(null, null), new ServerRun(null, null)));
        Assert.False(ServerCommandQueue.SameRun(new ServerRun(null, started), new ServerRun(current, null)));
        Assert.True(ServerCommandQueue.SameRun(new ServerRun(current, null), new ServerRun(current, null)));
        Assert.Contains("was not sent", ServerCommandQueue.RunEnded);

        var window = SourceFiles.Window(SourceFiles.Root(), "TaomCoopHost");
        var reply = SourceFiles.Method(window, "private async Task<IReadOnlyList<string>?> ReadCommandReply(string command, ServerRun run, Func<bool>? stillValid = null)");
        var check = reply.IndexOf("var check = ServerCommandQueue.Check(run, CurrentRun());", StringComparison.Ordinal);
        Assert.True(check > 0 && check < reply.IndexOf("SendThroughQueue", StringComparison.Ordinal) && check < reply.IndexOf("StandardInput.WriteLine", StringComparison.Ordinal), reply);
        // Every reply read keeps the run of the moment it was queued: the two RunCommandAndRead and the Play hold resend.
        Assert.Equal(3, System.Text.RegularExpressions.Regex.Matches(window, @"var run = CurrentRun\(\);").Count);
        Assert.Contains("await ReadCommandReply(command, run, () => PlayHoldStillStands(generation, command));", SourceFiles.Method(window, "private async void NotePlayHold(string line)"));
        // The save for a server this window did not start goes in its turn too.
        Assert.Contains("WriteQueued(AskQueuedSave)", SourceFiles.Method(window, "private bool WaitForWorldSave()"));
    }

    [Fact]
    public void The_window_handles_a_write_that_may_still_land_and_never_reads_its_output_as_the_next_reply()
    {
        // The queue's own behaviour is in ServerCommandQueueTests; here the window's use of it.
        var window = SourceFiles.Window(SourceFiles.Root(), "TaomCoopHost");
        var reply = SourceFiles.Method(window, "private async Task<IReadOnlyList<string>?> ReadCommandReply(string command, ServerRun run, Func<bool>? stillValid = null)");
        Assert.Contains("ConsoleWrite.NotSent", reply);
        Assert.Contains("ServerCommandQueue.ConsoleSlow", reply);
        // The next reply read waits for a stuck write before it takes its starting point in the log and before it writes.
        var wait = reply.IndexOf("ServerCommandQueue.AfterStuckWrite(_stuckWrite", StringComparison.Ordinal);
        Assert.True(wait > 0 && wait < reply.IndexOf("var offset = file?.Length", StringComparison.Ordinal) && wait < reply.IndexOf("WriteBounded(", StringComparison.Ordinal), reply);
        Assert.Contains("_stuckWrite = writing;", reply);
        // The save and stop: a write that may still land counts as written, so the fallback never writes it a second time.
        Assert.Matches(@"!=\s*ConsoleWrite\.NotSent", SourceFiles.Method(window, "private bool WriteLine(System.Diagnostics.Process process, string line)"));
        Assert.DoesNotContain("ServerCommandQueue.TryWriteUnder(", window);
        // Play hold and pause on join say unconfirmed instead of done.
        Assert.Contains("ServerCommandQueue.Unconfirmed(", SourceFiles.Method(window, "private async void NotePlayHold(string line)"));
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(SourceFiles.Method(window, "private async void DoJoinPause(JoinPauseStep step)"), @"ServerCommandQueue\.Unconfirmed\(").Count);
    }

    [Fact]
    public void With_no_Data_folder_the_mod_settings_page_and_the_seed_wait_and_say_why()
    {
        var window = SourceFiles.Window(SourceFiles.Root(), "TaomCoopHost");
        Assert.Contains("private bool ModSettingsReady => Path.IsPathFullyQualified(_settings.DataRoot ?? \"\");", window);
        var show = SourceFiles.Method(window, "private void ShowModSettings(string? keep = null)");
        var guard = show.IndexOf("if (!ModSettingsReady)", StringComparison.Ordinal);
        Assert.True(guard > 0 && guard < show.IndexOf("ServerModSettings.Seed(", StringComparison.Ordinal), show);
        foreach (var part in new[] { "ModSettingsEmpty.Visibility = Visibility.Collapsed;", "ModSettingsEditor.Visibility = Visibility.Collapsed;", "ModSettingsButtons.Visibility = Visibility.Collapsed;", "ModSettingsStatus.Text = ModSettingsNoDataFolder;" })
            Assert.Contains(part, show);
        Assert.Contains("if (!ModSettingsReady)", SourceFiles.Method(window, "private void ImportModSettings(string source)"));
        Assert.Contains("if (!ModSettingsReady)", SourceFiles.Method(window, "private void ResetModSettings(string? path, string? keep)"));
        Assert.Contains("!ModSettingsReady", SourceFiles.Method(window, "private void SaveModSettings_Click(object sender, RoutedEventArgs e)"));
        var install = SourceFiles.Method(window, "private void InstallServerTools(string server)");
        Assert.True(install.IndexOf("Path.IsPathFullyQualified(_settings.DataRoot", StringComparison.Ordinal) < install.IndexOf("ServerModSettings.Prepare(", StringComparison.Ordinal));
        // An empty Data folder gives a relative ModSettings folder, which is never taken.
        Assert.False(Path.IsPathFullyQualified(ServerModSettings.ServerRoot("")));
    }

    [Fact]
    public void The_Settings_intro_says_which_rules_apply_at_once()
    {
        var taom = XDocument.Parse(SourceFiles.Read(@"src\TaomCoopHost\MainWindow.xaml"));
        var texts = taom.Descendants().Select(element => (string?)element.Attribute("Text")).Where(text => text != null).ToList();
        Assert.Contains("Open a section to read what each setting does. Save settings writes the host files. Most settings need a server restart. The unstuck, offline XP and Always kingdom rules and the mod settings for players apply within a few seconds.", texts);
        Assert.DoesNotContain(texts, text => text!.Contains("MCM and client cheats from the other host are not here", StringComparison.Ordinal));
    }

    [Fact]
    public void An_imported_or_saved_file_is_listed_as_changed_before_it_is_written_and_a_failed_write_leaves_no_entry()
    {
        var server = Path.Combine(_root, "Data", "Configs", "ModSettings");
        var game = Path.Combine(_root, "Documents", "Configs", "ModSettings");
        var locked = Path.Combine(server, "Global", "RBM", "RBM_v1.json");
        Directory.CreateDirectory(Path.GetDirectoryName(locked)!);
        File.WriteAllText(locked, "{\"Speed\": 1}");
        var sent = Path.Combine(_root, "sent");
        Directory.CreateDirectory(Path.Combine(sent, "Global", "RBM"));
        File.WriteAllText(Path.Combine(sent, "Global", "RBM", "RBM_v1.json"), "{\"Speed\": 2}");
        File.WriteAllText(Path.Combine(sent, "Global", "RBM", "RBM_v2.json"), "{\"Speed\": 3}");

        ModSettingsImport result;
        using (new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None))
            result = ServerModSettings.Import(sent, server, game, ["RBM"]);

        Assert.Equal(1, result.Added);
        var listed = File.ReadAllText(ServerModSettings.EditedListPath(server));
        Assert.Contains("RBM_v2.json", listed);
        Assert.DoesNotContain("RBM_v1.json", listed);

        var source = SourceFiles.Read(@"src\TaomCoop.Core\ServerModSettings.cs").Replace("\r\n", "\n");
        var listedNow = source.IndexOf("var listedNow = edited.Add(relative);", StringComparison.Ordinal);
        Assert.True(listedNow > 0 && listedNow < source.IndexOf("WriteAtomic(file!, bytes);", StringComparison.Ordinal));
        var save = SourceFiles.Method(source, "private static string? SaveLocked(string serverRoot, string path, IReadOnlyDictionary<string, string> changes)");
        Assert.True(save.IndexOf("WriteEdited(serverRoot, edited);", StringComparison.Ordinal) < save.IndexOf("WriteAtomic(path,", StringComparison.Ordinal));
    }

    [Fact]
    public void A_save_that_can_not_write_leaves_no_changed_mark_and_seed_and_save_share_one_lock()
    {
        var server = Path.Combine(_root, "Data", "Configs", "ModSettings");
        var game = Path.Combine(_root, "Documents", "Configs", "ModSettings");
        var file = Path.Combine(server, "Global", "RBM", "RBM_v1.json");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, "{\"Speed\": 1}");
        // Open for reading only, sharing reads: Save can read the file and write its mark, and only the move over the file at
        // the end fails (access denied, where a failed read would have been a sharing violation).
        using (new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read))
            Assert.IsType<UnauthorizedAccessException>(Assert.ThrowsAny<Exception>(() => ServerModSettings.Save(server, file, new Dictionary<string, string> { ["Speed"] = "2" })));
        Assert.Equal("{\"Speed\": 1}", File.ReadAllText(file));
        Assert.Empty(Directory.GetFiles(server, "*.tmp", SearchOption.AllDirectories));
        // The next seed still keeps it in step with this PC: it is not listed as changed here.
        var listed = ServerModSettings.EditedListPath(server);
        Assert.True(!File.Exists(listed) || !File.ReadAllText(listed).Contains("RBM_v1.json", StringComparison.Ordinal));
        Directory.CreateDirectory(Path.Combine(game, "Global", "RBM"));
        File.WriteAllText(Path.Combine(game, "Global", "RBM", "RBM_v1.json"), "{\"Speed\": 5}");
        ServerModSettings.Seed(server, game);
        Assert.Equal("{\"Speed\": 5}", File.ReadAllText(file));

        var source = SourceFiles.Read(@"src\TaomCoop.Core\ServerModSettings.cs").Replace("\r\n", "\n");
        foreach (var method in new[] { "public static int Seed(", "public static string? Save(", "public static int Reset(", "public static ModSettingsImport Import(" })
            Assert.Contains("lock (FilesLock)", SourceFiles.Method(source, method));

        // The page's changes wait while Prepare or Start seeds the same folder.
        var window = SourceFiles.Window(SourceFiles.Root(), "TaomCoopHost");
        Assert.Contains("if (!_busy && !_startingServer)", SourceFiles.Method(window, "private bool ModSettingsWait()"));
        foreach (var handler in new[] { "SaveModSettings_Click", "ResetModSettings_Click", "ResetAllModSettings_Click", "ImportModSettingsZip_Click", "ImportModSettingsFolder_Click" })
            Assert.Contains("if (ModSettingsWait())", SourceFiles.Method(window, "private void " + handler + "(object sender, RoutedEventArgs e)"));
    }

    [Fact]
    public void Review_fixes_stay_wired_in_the_TAOM_window()
    {
        var window = SourceFiles.Window(SourceFiles.Root(), "TaomCoopHost");
        // A restart while the reply read waited for a stuck write: checked again right before the write, which goes to the
        // run the command was queued for.
        var reply = SourceFiles.Method(window, "private async Task<IReadOnlyList<string>?> ReadCommandReply(string command, ServerRun run, Func<bool>? stillValid = null)");
        var stuck = reply.IndexOf("AfterStuckWrite(", StringComparison.Ordinal);
        var again = reply.IndexOf("if (run.Process is not { } queuedProcess || !ServerCommandQueue.SameRun(queuedProcess, _process))", StringComparison.Ordinal);
        var valid = reply.IndexOf("if (stillValid != null && !stillValid())", StringComparison.Ordinal);
        var write = reply.IndexOf("WriteBounded(", StringComparison.Ordinal);
        // The run and a kick's "still on the server" are both checked again after the wait, right before the write.
        Assert.True(stuck > 0 && again > stuck && valid > again && write > valid, reply);
        Assert.Contains("var process = queuedProcess;", reply);
        Assert.DoesNotContain("var process = _process;", reply);
        // A rule change saves before the files a new host does not have yet.
        var save = SourceFiles.Method(window, "private bool SaveSettingsPage(bool fromSettingsPage = true)");
        Assert.True(save.IndexOf("SaveServerRules(serverRules);", StringComparison.Ordinal) < save.IndexOf("WriteConfig();", StringComparison.Ordinal));
        Assert.True(save.IndexOf("SaveServerRules(serverRules);", StringComparison.Ordinal) < save.IndexOf("SaveTimePermissions();", StringComparison.Ordinal));
        // The blocked DLL fix comes from the output thread: it hops to the window thread first.
        var fix = SourceFiles.Method(window, "private void ShowBlockedDllFix()");
        var check = fix.IndexOf("Dispatcher.CheckAccess()", StringComparison.Ordinal);
        Assert.True(check > 0 && check < fix.IndexOf("ShowPage(", StringComparison.Ordinal), fix);
        // A cheat whose console was slow is never shown as Done.
        var cheats = SourceFiles.Read(@"src\TaomCoopHost\CoopCheatsPage.xaml.cs");
        var slow = cheats.IndexOf("if (ServerCommandQueue.Unconfirmed(lines))", StringComparison.Ordinal);
        Assert.True(slow > 0 && slow < cheats.IndexOf("Translator.T(\"Done.\")", StringComparison.Ordinal));
    }

    [Fact]
    public void A_mod_settings_file_with_text_that_can_not_be_read_is_skipped_by_import_and_left_out_by_load()
    {
        var server = Path.Combine(_root, "Data", "Configs", "ModSettings");
        var game = Path.Combine(_root, "Documents", "Configs", "ModSettings");
        var known = Path.Combine(server, "Global", "RBM", "RBM_v1.json");
        Directory.CreateDirectory(Path.GetDirectoryName(known)!);
        File.WriteAllText(known, "{\"Speed\": 1}");
        var sent = Path.Combine(_root, "sent", "Global", "RBM");
        Directory.CreateDirectory(sent);
        // A lone surrogate escape in a value, the same under a known id in a value the sync never reads (a hotkey), and bad
        // UTF-8: JsonDocument takes all three and only throws when the text is read.
        File.WriteAllText(Path.Combine(sent, "RBM_v2.json"), @"{""a"":""\ud800""}");
        File.WriteAllText(Path.Combine(sent, "RBM_v1.json"), @"{""Hotkey"":""\ud800""}");
        File.WriteAllBytes(Path.Combine(sent, "RBM_v3.json"), [.. "{\"a\":\""u8, 0xC3, 0x28, .. "\"}"u8]);

        var result = ServerModSettings.Import(Path.Combine(_root, "sent"), server, game, ["RBM"]);
        Assert.Equal(0, result.Added);
        Assert.Equal(3, result.Skipped.Count(line => line.EndsWith(": has text that is not valid, so it was skipped.", StringComparison.Ordinal)));
        Assert.Equal("{\"Speed\": 1}", File.ReadAllText(known));

        // Such a file put on the server by hand: Load leaves just that file out, so the page and every Start still work.
        File.WriteAllText(Path.Combine(server, "Global", "RBM", "RBM_bad.json"), @"{""Hotkey"":""\ud800""}");
        Assert.Equal("RBM_v1", Assert.Single(ServerModSettings.Load(server, ModCompatDatabase.Embedded())).Id);

        // The window keeps running whatever a handler did not catch, and logs it.
        var app = SourceFiles.Read(@"src\TaomCoopHost\App.xaml.cs");
        Assert.Contains("DispatcherUnhandledException += (_, args) =>", app);
        Assert.Contains("args.Handled = true;", app);
        Assert.Contains("NoteUnexpected(args.Exception)", app);
    }

    [Fact]
    public void Only_a_players_event_the_server_wrote_counts_and_a_broken_one_keeps_the_last_list()
    {
        const string real = "16:15:25.478  @DS@{\"ev\":\"players\",\"list\":[{\"id\":3,\"name\":\"Guthric\",\"state\":\"on map\",\"addr\":\"1.2.3.4:5\"}]}";
        var read = HostPlayerLog.Read(real);
        Assert.Equal("Guthric", Assert.Single(read).Name);
        // A player name or chat line carrying the event text further along a line can't set or blank the list.
        var forged = real + "\n16:15:30.001  [Coop] Player joined: @DS@{\"ev\":\"players\",\"list\":[]}";
        Assert.Equal("Guthric", Assert.Single(HostPlayerLog.Read(forged)).Name);
        Assert.Equal("Guthric", Assert.Single(HostPlayerLog.Read(real + "\n16:15:30.001  chat Bob: \"ev\":\"players\" {\"list\":[]}")).Name);
        // A line cut in half keeps the last good list.
        Assert.Equal("Guthric", Assert.Single(HostPlayerLog.Read("16:15:31.000  @DS@{\"ev\":\"players\",\"list\":[{\"id\":3,", read)).Name);
        Assert.Empty(HostPlayerLog.Read("16:15:31.000  @DS@{\"ev\":\"players\",\"list\":[{\"id\":3,"));
        // Bare on stdout counts too.
        Assert.Single(HostPlayerLog.Read("@DS@{\"ev\":\"players\",\"list\":[{\"id\":1,\"name\":\"Ned\"}]}"));
        Assert.Contains("HostPlayerLog.Read(ReadPlayerLog(), _onlinePlayers)", SourceFiles.Window(SourceFiles.Root(), "TaomCoopHost"));
    }

    [Fact]
    public void A_glued_players_event_counts_unless_a_name_or_chat_line_carries_it_and_a_missing_list_keeps_the_roster()
    {
        const string ev = "@DS@{\"ev\":\"players\",\"list\":[{\"id\":4,\"name\":\"Ned\",\"state\":\"on map\",\"addr\":\"1.2.3.4:5\"}]}";
        // The engine glues the event onto the end of a warning (DsEventParser reads those too): it counts.
        Assert.Equal("Ned", Assert.Single(HostPlayerLog.Read("16:15:25.478   WARN" + ev)).Name);
        Assert.Equal("Ned", Assert.Single(HostPlayerLog.Read("02:28:25.072  #0Harmony.pdb@{5D97B13327B2C36202F0ECDD2F0BCDBD} " + ev)).Name);
        // The same text inside a quote, after a Coop or chat tag, or with more text after it does not.
        var last = HostPlayerLog.Read("16:15:25.478  @DS@{\"ev\":\"players\",\"list\":[{\"id\":1,\"name\":\"Guthric\"}]}");
        foreach (var forged in new[] { "16:15:26.000  [Coop] Player 'x" + ev + "' joined", "16:15:26.000  [Chat] Bob: " + ev, "16:15:26.000  say \"" + ev, "16:15:26.000  WARN" + ev + " more text" })
            Assert.Equal("Guthric", Assert.Single(HostPlayerLog.Read("16:15:25.478  @DS@{\"ev\":\"players\",\"list\":[{\"id\":1,\"name\":\"Guthric\"}]}\n" + forged, last)).Name);
        // An event with no list, or a list that is not a list, keeps the roster.
        Assert.Equal("Guthric", Assert.Single(HostPlayerLog.Read("16:15:27.000  @DS@{\"ev\":\"players\"}", last)).Name);
        Assert.Equal("Guthric", Assert.Single(HostPlayerLog.Read("16:15:27.000  @DS@{\"ev\":\"players\",\"list\":{}}", last)).Name);
    }

    [Fact]
    public void Seed_never_copies_a_host_file_with_unreadable_text_and_says_so()
    {
        var server = Path.Combine(_root, "Data", "Configs", "ModSettings");
        var game = Path.Combine(_root, "Documents", "Configs", "ModSettings");
        Directory.CreateDirectory(Path.Combine(game, "Global", "RBM"));
        File.WriteAllText(Path.Combine(game, "Global", "RBM", "RBM_v1.json"), @"{""Hotkey"":""\ud800""}");
        File.WriteAllText(Path.Combine(game, "Global", "RBM", "RBM_v2.json"), "{\"Speed\": 2}");
        var skipped = new List<string>();
        Assert.Equal(1, ServerModSettings.Seed(server, game, skipped: skipped));
        Assert.False(File.Exists(Path.Combine(server, "Global", "RBM", "RBM_v1.json")));
        Assert.True(File.Exists(Path.Combine(server, "Global", "RBM", "RBM_v2.json")));
        Assert.Contains(skipped, line => line.EndsWith("RBM_v1.json: has text that is not valid, so it was skipped.", StringComparison.Ordinal));
        // Prepare passes those lines to the log.
        var notes = new List<string>();
        Directory.CreateDirectory(Path.Combine(_root, "module"));
        ServerModSettings.Prepare(Path.Combine(_root, "Data"), Path.Combine(_root, "module"), game, ModCompatDatabase.Embedded(), notes.Add);
        Assert.Contains(notes, line => line.Contains("has text that is not valid", StringComparison.Ordinal));
    }

    [Fact]
    public void Last_round_fixes_stay_wired()
    {
        var window = SourceFiles.Window(SourceFiles.Root(), "TaomCoopHost");
        // The dispatcher net only covers a loaded window, never throws, and the status line says a Start did not finish.
        var app = SourceFiles.Read(@"src\TaomCoopHost\App.xaml.cs");
        var loaded = app.IndexOf("if (MainWindow is not MainWindow { IsLoaded: true } window)", StringComparison.Ordinal);
        Assert.True(loaded > 0 && loaded < app.IndexOf("args.Handled = true;", StringComparison.Ordinal), app);
        Assert.Contains("SetControl(\"Something went wrong, and the tool kept running: \" + ex.Message, failed: true);", SourceFiles.Method(window, "internal void NoteUnexpected(Exception ex)"));
        // No rules file next to the tool when there is no Data folder.
        var rules = SourceFiles.Method(window, "private void SaveServerRules(HostServerRules rules)");
        var guard = rules.IndexOf("Path.IsPathFullyQualified(_settings.DataRoot", StringComparison.Ordinal);
        // The guard comes first, before the pause on join choice is taken or anything is read or written, and says why.
        Assert.True(guard > 0 && guard < rules.IndexOf("_pauseOnJoin = rules.PauseOnJoin;", StringComparison.Ordinal)
            && guard < rules.IndexOf("ServerRulesFile.Write(", StringComparison.Ordinal), rules);
        Assert.Contains("Server rules can't be saved until the tool knows the server's Data folder", rules);
        Assert.DoesNotContain("NoteLog(", SourceFiles.Method(window, "internal void NoteUnexpected(Exception ex)"));
        // The page only reads while Prepare or Start runs.
        var show = SourceFiles.Method(window, "private void ShowModSettings(string? keep = null)");
        Assert.Contains("var toolBusy = _busy || _startingServer;", show);
        Assert.Contains("if (!toolBusy)", show);
        Assert.Contains("if (!toolBusy && !string.IsNullOrWhiteSpace(_settings.ServerRoot))", show);
        // A run that starts or ends takes its players and ban kicks with it.
        var forget = SourceFiles.Method(window, "private void ForgetPlayers()");
        Assert.Contains("_onlinePlayers = [];", forget);
        Assert.Contains("_banKicks.Clear();", forget);
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(SourceFiles.Read(@"src\TaomCoopHost\MainWindow.ServerControl.cs"), @"ForgetPlayers\(\);").Count);
        // A slow console is no success for a cheat or its undo.
        var cheats = SourceFiles.Read(@"src\TaomCoopHost\CoopCheatsPage.xaml.cs");
        Assert.Contains("&& !ServerCommandQueue.Unconfirmed(lines);", SourceFiles.Method(cheats, "private async Task<(bool Ok, IReadOnlyList<string> Lines)> SendCoopAsync(string command)"));
        // A rollback of the changed mark that fails is told, not swallowed.
        var source = SourceFiles.Read(@"src\TaomCoop.Core\ServerModSettings.cs");
        Assert.Contains("This file also stays marked as changed here", source);
        Assert.Contains("it also stays marked as changed here", source);
    }

    [Fact]
    public void A_server_this_window_did_not_start_matches_itself_even_without_a_start_time_and_no_server_says_so()
    {
        // Found, but Windows would not give its start time (a protected process): a stable stand in, so every command to that
        // healthy server still goes out.
        Assert.Equal(DateTime.MinValue, ServerCommandQueue.ForeignStart(found: true, started: null));
        var protectedServer = new ServerRun(null, ServerCommandQueue.ForeignStart(true, null));
        Assert.Equal(RunCheck.Same, ServerCommandQueue.Check(protectedServer, new ServerRun(null, ServerCommandQueue.ForeignStart(true, null))));
        // No server at all, now or when it was queued: the old no server line, not "run ended".
        var none = new ServerRun(null, ServerCommandQueue.ForeignStart(found: false, started: null));
        Assert.Null(none.ForeignStarted);
        Assert.Equal(RunCheck.NoServer, ServerCommandQueue.Check(protectedServer, none));
        Assert.Equal(RunCheck.NoServer, ServerCommandQueue.Check(none, protectedServer));
        // Another start time is another run.
        var started = new DateTime(2026, 11, 19, 10, 0, 0, DateTimeKind.Utc);
        Assert.Equal(RunCheck.Ended, ServerCommandQueue.Check(new ServerRun(null, started), new ServerRun(null, started.AddMinutes(5))));

        var window = SourceFiles.Window(SourceFiles.Root(), "TaomCoopHost");
        Assert.Contains("return new ServerRun(null, ServerCommandQueue.ForeignStart(found, started), processId);", SourceFiles.Method(window, "private ServerRun CurrentRun()"));
        var reply = SourceFiles.Method(window, "private async Task<IReadOnlyList<string>?> ReadCommandReply(string command, ServerRun run, Func<bool>? stillValid = null)");
        Assert.Contains("var message = check == RunCheck.NoServer ? CheatBlockReason() : ServerCommandQueue.RunEnded;", reply);
        // A queued command whose last check fails inside the stdin lock writes nothing.
        Assert.Contains("}, stillValid);", reply);
        Assert.Contains("if (written == ConsoleWrite.Dropped)", reply);
        // A console queue reply that was not picked up is not sent; one picked up with no answer yet may still land.
        var queue = SourceFiles.Method(window, "private async Task<(string Text, bool Sent)> SendThroughQueue(string command)");
        Assert.Contains("!reply.PickedUp ? ServerCommandQueue.NotSent(reply.Text)", queue);
        Assert.Contains("!reply.Answered && !reply.Failed ? ServerCommandQueue.ConsoleSlow", queue);
    }

    [Fact]
    public void Only_engine_lines_count_as_glue_for_a_players_event_and_an_unreadable_newest_event_falls_back()
    {
        const string ev = "@DS@{\"ev\":\"players\",\"list\":[{\"id\":4,\"name\":\"Ned\"}]}";
        // The real glued lines: a WARN line, the TAOM test server's 0Harmony pdb line and an RGL warning.
        foreach (var glued in new[] { "16:15:25.478   WARN" + ev, "02:28:25.072  #0Harmony.pdb@{5D97B13327B2C36202F0ECDD2F0BCDBD} " + ev,
                     "00:23:59.809  rgl_post_warning_line: RGL WARNING - Could not find animation: elephant_stand_for_moveme" + ev })
            Assert.Equal("Ned", Assert.Single(HostPlayerLog.Read(glued)).Name);
        // Untagged chat or a name line with the same text does not.
        Assert.Empty(HostPlayerLog.Read("16:15:26.000  Bob: hi " + ev));
        Assert.Empty(HostPlayerLog.Read("16:15:26.000  Ned joined " + ev));
        // The newest event has no list and there is no roster yet: the event before it is used.
        var text = "16:15:25.478  @DS@{\"ev\":\"players\",\"list\":[{\"id\":1,\"name\":\"Guthric\"}]}\n16:15:27.000  @DS@{\"ev\":\"players\"}";
        Assert.Equal("Guthric", Assert.Single(HostPlayerLog.Read(text)).Name);
    }

    [Fact]
    public void Prepare_logs_files_it_could_not_copy_and_a_locked_Coop_gets_a_plain_line()
    {
        var game = Path.Combine(_root, "Documents", "Configs", "ModSettings");
        var locked = Path.Combine(game, "Global", "RBM", "RBM_v1.json");
        Directory.CreateDirectory(Path.GetDirectoryName(locked)!);
        File.WriteAllText(locked, "{\"Speed\": 2}");
        Directory.CreateDirectory(Path.Combine(_root, "module"));
        var notes = new List<string>();
        using (new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None))
            ServerModSettings.Prepare(Path.Combine(_root, "Data"), Path.Combine(_root, "module"), game, ModCompatDatabase.Embedded(), notes.Add);
        Assert.Contains("1 file on this PC could not be copied now. It is tried again next time.", notes);

        var launcher = SourceFiles.Read(@"src\TaomCoopLauncher\MainWindow.xaml.cs");
        var coop = SourceFiles.Method(launcher, "private string? CoopVersionProblem(out bool workshopNewer)");
        var unreadable = coop.IndexOf("return CoopUnreadable;", StringComparison.Ordinal);
        Assert.True(unreadable > 0 && unreadable < coop.IndexOf("ModulesCoopNotPinned", StringComparison.Ordinal), coop);
        var newer = SourceFiles.Method(SourceFiles.Read(@"src\TaomCoop.Core\CoopStaging.cs"), "public static string? NewerCoopProblem(string? coopModuleDirectory, bool server = false)");
        Assert.Contains("ModuleIdentityReader.TryRead(xml) is not { } identity", newer);
        Assert.DoesNotContain("ModuleIdentityReader.Read(", newer);
    }

    [Fact]
    public void An_empty_roster_never_stops_the_walk_back_and_no_event_keeps_the_last_list()
    {
        var older = "16:15:25.478  @DS@{\"ev\":\"players\",\"list\":[{\"id\":1,\"name\":\"Guthric\"}]}";
        var broken = "16:15:27.000  @DS@{\"ev\":\"players\",\"list\":[{\"id\":1,";
        // The dashboard starts with an empty list: an unreadable newest event still finds the older one.
        Assert.Equal("Guthric", Assert.Single(HostPlayerLog.Read(older + "\n" + broken, new List<HostPlayer>())).Name);
        // No event that counts at all keeps the list the window has.
        var known = new List<HostPlayer> { new(2, "Ned", "on map", "") };
        Assert.Equal("Ned", Assert.Single(HostPlayerLog.Read("16:15:28.000  Bob: hi @DS@{\"ev\":\"players\",\"list\":[]}", known)).Name);
        Assert.Empty(HostPlayerLog.Read("nothing here"));
    }

    [Fact]
    public void A_restart_with_no_readable_start_time_is_still_another_run_by_its_process_id_and_refused_is_a_failure()
    {
        var unknownStart = ServerCommandQueue.ForeignStart(true, null);
        Assert.Equal(RunCheck.Same, ServerCommandQueue.Check(new ServerRun(null, unknownStart, 4100), new ServerRun(null, unknownStart, 4100)));
        Assert.Equal(RunCheck.Ended, ServerCommandQueue.Check(new ServerRun(null, unknownStart, 4100), new ServerRun(null, unknownStart, 5200)));
        Assert.Contains("TryStarted(_settings.ServerRoot, out var started, out var processId)", SourceFiles.Method(SourceFiles.Window(SourceFiles.Root(), "TaomCoopHost"), "private ServerRun CurrentRun()"));
        Assert.True(DedicatedServerWatch.TryStarted(Path.Combine(_root, "no-server"), out _, out var none) == false && none == null);

        // A refused console queue reply is a failure, so the Cheats page never says Done or records an undo for it.
        Assert.Equal(CommandReplyTone.Failed, CommandReply.Tone("The server refused that command: no such hero"));
        foreach (var normal in new[] { "Gave 1000 gold to Guthric.", "Time mode set to Pause.", "Done." })
            Assert.Equal(CommandReplyTone.Normal, CommandReply.Tone(normal));

        // A locked Workshop Coop SubModule.xml is still found, so the locked file line shows for it.
        var launcher = SourceFiles.Read(@"src\TaomCoopLauncher\MainWindow.xaml.cs");
        Assert.Contains("return moduleId.Equals(TaomStack.CoopModuleId, StringComparison.OrdinalIgnoreCase) ? workshopXml : null;",
            SourceFiles.Method(launcher, "private static string? FindModuleXml(string gameRoot, string moduleId)"));
    }
}
