using System.IO.Compression;
using System.Xml.Linq;
using BannerlordCoopLauncher.Core.Launch;
using BannerlordCoopLauncher.Core.Modules;
using BannerlordCoopLauncher.Core.Server;
using Coodsz.ServerRules;
using CoodszSettingsSync;
using RealmOfThrones.Core;
using TaomCoop.Core;

namespace BannerlordCoopLauncher.Tests;

// The host rules and the mod settings for players on the Realm of Thrones Server Tool (RotServerTools): the same
// Coodsz Server Tools module and defaults as the Coop Server Tool, reading the RoT tool's own data folder.
public class RotServerToolsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "coodsz-rottools-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private static string Payload => Path.Combine(SourceFiles.Root(), "src", "CoodszHost", "ServerTools", ServerToolsModule.ModuleId);

    private static TaomSetup Setup(params string[] extras)
    {
        var setup = new TaomSetup { Name = "Test" };
        foreach (var id in extras)
            setup.Extras.Add(new TaomExtraMod { ModuleId = id, Name = id, Enabled = true });
        RotLoadOrder.Normalize(setup);
        return setup;
    }

    [Fact]
    public void The_module_reads_the_rules_file_the_RoT_tool_writes_and_other_tools_keep_Documents()
    {
        var documents = Path.Combine(_root, "Documents");
        var coopData = Path.Combine(_root, "tool", "CoopData");
        var dataRoot = NightlyServer.DataDir(coopData);
        // Only the RoT start names the data folder in COODSZ_SERVER_DATA_DIR; the tool writes the rules there.
        var start = NightlyServer.StartPlan(Path.Combine(_root, "server"), coopData, acceptTerms: false, RotServerTools.StartVariables(dataRoot));
        Assert.Equal(dataRoot, start.Environment[RotServerTools.DataDirVariable]);
        Assert.Equal(RotServerTools.RulesPath(dataRoot), ClanCapSettings.PathIn(documents, start.Environment[RotServerTools.DataDirVariable]));
        Assert.Equal(RotServerTools.DataDirVariable, ClanCapSettings.ServerDataDirVariable);
        Assert.Equal(RotServerTools.DataDirVariable, SettingsFiles.ServerDataDirVariable);

        // A start without it (Coodsz Host) keeps reading Documents.
        Assert.False(NightlyServer.StartPlan(Path.Combine(_root, "server"), coopData, acceptTerms: false).Environment.ContainsKey(RotServerTools.DataDirVariable));
        Assert.Equal(Path.Combine(documents, "Mount and Blade II Bannerlord", "CoopData", "DedicatedServer", ClanCapFile.FileName), ClanCapSettings.PathIn(documents, null));
        Assert.Equal(ClanCapSettings.PathIn(documents, null), ClanCapSettings.PathIn(documents, "  "));
        // The name is written in one place of the tools (ServerToolsModule); the RoT and TAOM starts use that constant.
        var setters = Directory.GetFiles(Path.Combine(SourceFiles.Root(), "src"), "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) && File.ReadAllText(file).Contains("\"COODSZ_SERVER_DATA_DIR\""))
            .Select(Path.GetFileName).ToList();
        Assert.Equal(new[] { "ServerToolsModule.cs" }, setters);
    }

    [Fact]
    public void The_sync_sends_the_RoT_hosts_own_MCM_files_never_the_Coop_Server_Tools()
    {
        var documents = Path.Combine(_root, "Documents");
        var hostFolder = Path.Combine(documents, "Mount and Blade II Bannerlord", "CoopData", "DedicatedServer", "Configs", "ModSettings");
        var game = Path.Combine(documents, "Mount and Blade II Bannerlord", "Configs", "ModSettings");
        var dataDir = Path.Combine(_root, "tool", "CoopData", "DedicatedServer");
        Directory.CreateDirectory(hostFolder);
        Directory.CreateDirectory(game);

        Assert.Equal(hostFolder, SettingsFiles.FindCoopDataModSettings(documents));
        Assert.Equal(hostFolder, SettingsFiles.FindModSettingsRoot(documents));
        // MCM's provider does not run on the RoT server (McmSettings.FindLive is null there), so the sync reads files:
        // never the Coop Server Tool's folder, but the MCM files saved on the host's PC.
        Assert.Null(SettingsFiles.FindCoopDataModSettings(documents, dataDir));
        Assert.Equal(game, SettingsFiles.FindModSettingsRoot(documents, dataDir));
        // An empty Configs\ModSettings in the server's data folder (Coop's --data-dir) does not hide the host's files.
        var own = Path.Combine(dataDir, "Configs", "ModSettings");
        Directory.CreateDirectory(Path.Combine(own, "Global"));
        Assert.Equal(game, SettingsFiles.FindModSettingsRoot(documents, dataDir));
        File.WriteAllText(Path.Combine(own, "Global", "mod.json"), "{}");
        Assert.Equal(own, SettingsFiles.FindModSettingsRoot(documents, dataDir));
        // Coodsz Host's empty CoopData folder still counts, as before.
        Assert.Equal(hostFolder, SettingsFiles.FindModSettingsRoot(documents));
    }

    // The ordered command queue (ServerCommandQueue) as the RoT window uses it: the host's intent is recorded when a command
    // is queued, and an automatic step checks at its turn, right before it is written.
    private static Task<string?> Queue(ServerCommandQueue queue, List<string> sent, string command, Func<bool>? stillValid = null) =>
        queue.Enqueue(() => { sent.Add(command); return Task.FromResult(command); }, stillValid);

    [Fact]
    public async Task A_console_Play_queued_before_a_button_Pause_leaves_the_game_paused()
    {
        var queue = new ServerCommandQueue();
        var pause = new JoinPause();
        var sent = new List<string>();
        var busy = new TaskCompletionSource<string>();
        var running = queue.Enqueue(() => busy.Task);

        // A join pause resume waits for its turn first.
        var resumeSeen = pause.HostCommandCount;
        var resume = Queue(queue, sent, pause.ResumeCommand, () => pause.HostCommandCount == resumeSeen);
        // The host types Play in the console, then presses Pause: each is recorded as it is queued.
        var play = JoinPause.SetModeCommand + " " + JoinPause.NormalMode;
        pause.HostSent(play);
        var console = Queue(queue, sent, play);
        pause.HostSent(JoinPause.PauseCommand);
        var button = Queue(queue, sent, JoinPause.PauseCommand);
        // The join pause's correction for the host's speed comes last and is checked at its turn.
        var landed = resumeSeen;
        var correction = Queue(queue, sent, play, () => pause.HostCommandCount == landed);
        busy.SetResult("done");
        await Task.WhenAll(running, resume, console, button, correction);

        Assert.Null(await resume);
        Assert.Null(await correction);
        Assert.Equal(new[] { play, JoinPause.PauseCommand }, sent);
        Assert.Equal(JoinPause.PauseMode, JoinPause.HostMode(sent[^1]));
    }

    [Fact]
    public async Task A_stale_Play_hold_resend_is_dropped()
    {
        var queue = new ServerCommandQueue();
        var hold = new PlayHold();
        var pause = new JoinPause();
        var sent = new List<string>();
        var play = JoinPause.SetModeCommand + " " + JoinPause.NormalMode;
        hold.HostSent(play);
        var busy = new TaskCompletionSource<string>();
        var running = queue.Enqueue(() => busy.Task);

        // The resend checks at its turn with the code the window uses (PlayHold.ResendStands).
        bool Stands(int generation, string command) => hold.ResendStands(generation, command, pause.Pausing);
        var generation = hold.Generation;
        var resend = Queue(queue, sent, play, () => Stands(generation, play));
        // The host presses Pause before the resend's turn comes.
        hold.HostSent(JoinPause.PauseCommand);
        busy.SetResult("done");
        await Task.WhenAll(running, resend);

        Assert.Null(await resend);
        Assert.Empty(sent);
        // A resend whose hold still stands goes out.
        hold.HostSent(play);
        var current = hold.Generation;
        Assert.Equal(play, await Queue(queue, sent, play, () => Stands(current, play)));

        // While pause on join holds the game, the same resend is dropped too.
        pause.UpdateServing([new ConnectedPlayer(7, "Bran", "loading", null)], DateTime.UtcNow, true, serving: true);
        Assert.True(pause.Paused(null, DateTime.UtcNow));
        Assert.True(pause.Pausing);
        Assert.Null(await Queue(queue, sent, play, () => Stands(current, play)));
        Assert.Equal(new[] { play }, sent);
    }

    [Fact]
    public async Task A_kick_for_a_player_who_left_is_dropped()
    {
        var queue = new ServerCommandQueue();
        var sent = new List<string>();
        var online = new List<HostPlayer> { new(3, "Arya", "on map", "") };
        var busy = new TaskCompletionSource<string>();
        var running = queue.Enqueue(() => busy.Task);

        // The kick checks at its turn with the code the window uses (HostPlayerLog.StillOn).
        var player = online[0];
        bool StillConnected() => HostPlayerLog.StillOn(online, player);
        var kick = Queue(queue, sent, "kick 3", StillConnected);
        // She leaves and someone else gets id 3 before the kick's turn.
        online.Clear();
        online.Add(new HostPlayer(3, "Sansa", "joining", ""));
        busy.SetResult("done");
        await Task.WhenAll(running, kick);

        Assert.Null(await kick);
        Assert.Empty(sent);

        // The same name on another id (she joined again) is not the player the kick was for either.
        online.Clear();
        online.Add(new HostPlayer(9, "Arya", "on map", ""));
        Assert.Null(await Queue(queue, sent, "kick 3", StillConnected));
        Assert.Empty(sent);
        // Still on with that id and name: the kick goes out.
        online.Add(new HostPlayer(3, "ARYA", "on map", ""));
        Assert.Equal("kick 3", await Queue(queue, sent, "kick 3", StillConnected));
    }

    [Fact]
    public async Task The_command_queue_runs_in_queue_order_and_a_throw_never_stops_the_next_turn()
    {
        var queue = new ServerCommandQueue();
        var sent = new List<string>();
        var busy = new TaskCompletionSource<string>();
        var running = queue.Enqueue(() => busy.Task);
        var turns = Enumerable.Range(1, 5).Select(i => Queue(queue, sent, "c" + i)).ToList();
        busy.SetResult("done");
        await Task.WhenAll(turns);
        Assert.Equal(new[] { "c1", "c2", "c3", "c4", "c5" }, sent);

        // A send that throws, then a check that throws: each fails its own turn only.
        var throwingSend = queue.Enqueue<string>(() => throw new IOException("pipe"));
        var throwingCheck = queue.Enqueue(() => Task.FromResult("never"), () => throw new InvalidOperationException("check"));
        var after = Queue(queue, sent, "after");
        await Assert.ThrowsAsync<IOException>(() => throwingSend);
        await Assert.ThrowsAsync<InvalidOperationException>(() => throwingCheck);
        Assert.Equal("after", await after);
        Assert.Equal("after", sent[^1]);
    }

    [Fact]
    public async Task A_queued_write_goes_out_once_in_its_turn_or_once_from_the_fallback()
    {
        var queue = new ServerCommandQueue();
        var writes = 0;
        bool Write() { Interlocked.Increment(ref writes); return true; }

        // Its turn comes in time: written once, in the queue.
        Assert.True(await queue.WriteWithin(Write, TimeSpan.FromSeconds(5)));
        Assert.Equal(1, writes);

        // A turn that hangs: the fallback writes after the wait, and the queued turn is dropped when it comes.
        var busy = new TaskCompletionSource<string>();
        var running = queue.Enqueue(() => busy.Task);
        Assert.True(await queue.WriteWithin(Write, TimeSpan.FromMilliseconds(100)));
        Assert.Equal(2, writes);
        busy.SetResult("done");
        await running;
        Assert.Equal("next", await queue.Enqueue(() => Task.FromResult("next")));
        Assert.Equal(2, writes);
    }

    [Fact]
    public async Task A_write_blocked_on_the_console_counts_as_not_sent_so_the_forced_stop_still_runs()
    {
        var stdinLock = new object();
        var holding = new ManualResetEventSlim();
        var release = new ManualResetEventSlim();
        // A write that blocks in the pipe holds the stdin lock on its own thread.
        var blocked = new Thread(() => { lock (stdinLock) { holding.Set(); release.Wait(); } });
        blocked.Start();
        holding.Wait();
        try
        {
            var wrote = false;
            var watch = System.Diagnostics.Stopwatch.StartNew();
            Assert.False(ServerCommandQueue.TryWriteUnder(stdinLock, TimeSpan.FromMilliseconds(100), () => wrote = true));
            Assert.False(wrote);
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5));

            // The save or stop fallback through WriteWithin returns too, as not sent, while a turn hangs.
            var queue = new ServerCommandQueue();
            var busy = new TaskCompletionSource<string>();
            var running = queue.Enqueue(() => busy.Task);
            Assert.False(await queue.WriteWithin(() => ServerCommandQueue.TryWriteUnder(stdinLock, TimeSpan.FromMilliseconds(100), () => wrote = true), TimeSpan.FromMilliseconds(100)));
            Assert.False(wrote);
            busy.SetResult("done");
            await running;

            // The holder lets go before the deadline: the waiting write gets the lock and runs.
            var waiting = Task.Run(() => ServerCommandQueue.TryWriteUnder(stdinLock, TimeSpan.FromSeconds(10), () => wrote = true));
            Thread.Sleep(100);
            release.Set();
            Assert.True(await waiting);
            Assert.True(wrote);
        }
        finally
        {
            release.Set();
            blocked.Join();
        }

        Assert.Equal(5, ServerCommandQueue.StdinLockWait.TotalSeconds);
        // The lock is let go after a write and after a write that throws: another thread gets it at once.
        static bool OtherThreadGets(object gate) => Task.Run(() =>
        {
            if (!Monitor.TryEnter(gate, TimeSpan.FromMilliseconds(500)))
                return false;
            Monitor.Exit(gate);
            return true;
        }).Result;
        Assert.True(ServerCommandQueue.TryWriteUnder(stdinLock, TimeSpan.FromSeconds(1), () => { }));
        Assert.True(OtherThreadGets(stdinLock));
        Assert.Throws<IOException>(() => ServerCommandQueue.TryWriteUnder(stdinLock, TimeSpan.FromSeconds(1), () => throw new IOException("pipe")));
        Assert.True(OtherThreadGets(stdinLock));
    }

    [Fact]
    public void A_command_that_was_not_written_reads_as_a_failure_for_the_buttons_and_the_join_pause()
    {
        // ReadCommandReply answers a write that timed out or broke with these lines, and the callers pass them on as sent.
        foreach (var reply in new[] { ServerCommandQueue.ConsoleTimeout, ServerCommandQueue.NotSent("Pipe is broken.") })
        {
            foreach (var mode in new[] { JoinPause.PauseMode, JoinPause.NormalMode, JoinPause.FastMode })
            {
                var result = ServerTime.Result(mode, true, reply, false);
                Assert.True(result.Failed, reply);
                Assert.Equal(reply, result.Line);
            }
        }
    }

    [Fact]
    public void The_shipped_module_DLLs_read_the_new_variable_and_the_two_sync_copies_match()
    {
        var bin = Path.Combine(Payload, "bin", "Win64_Shipping_Server");
        foreach (var dll in new[] { "DedicatedServer.CoodszTools.dll", CoopProbe.AssemblyFileName })
        {
            var bytes = File.ReadAllBytes(Path.Combine(bin, dll));
            Assert.True(Holds(bytes, RotServerTools.DataDirVariable), dll + " is an older build: run compat\\CoodszServerTools\\build.ps1");
            Assert.False(Holds(bytes, "BANNERLORD_USER_DIR"), dll);
        }
        Assert.Equal(File.ReadAllBytes(Path.Combine(bin, CoopProbe.AssemblyFileName)),
            File.ReadAllBytes(Path.Combine(SourceFiles.Root(), "src", "TaomCoopHost", "WarSync", CoopProbe.AssemblyFileName)));

        // A string literal sits in the DLL as UTF-16.
        static bool Holds(byte[] bytes, string text) =>
            bytes.AsSpan().IndexOf(System.Text.Encoding.Unicode.GetBytes(text)) >= 0;
    }

    [Fact]
    public void Pause_on_join_forgets_the_session_when_the_server_stops()
    {
        var pause = new JoinPause();
        var now = DateTime.UtcNow;
        var onMap = new[] { new ConnectedPlayer(1, "Ned", "on map", null) };
        var loading = new[] { new ConnectedPlayer(1, "Ned", "loading", null) };
        Assert.Equal(JoinPauseStep.None, pause.UpdateServing(onMap, now, true, serving: true));
        // Seen on the map once: a reconnect in the same session does not pause.
        Assert.Equal(JoinPauseStep.None, pause.UpdateServing(loading, now, true, serving: true));

        // The server stops and starts again: a new session, so Ned joining pauses again.
        Assert.Equal(JoinPauseStep.None, pause.UpdateServing(loading, now, true, serving: false));
        Assert.Equal(JoinPauseStep.Pause, pause.UpdateServing(loading, now, true, serving: true));

        // A host time command is counted, so a resume queued before it is dropped.
        var before = pause.HostCommandCount;
        pause.HostSent(JoinPause.PauseCommand);
        pause.HostSent("coop.players");
        Assert.Equal(before + 1, pause.HostCommandCount);
    }

    [Fact]
    public void The_server_list_puts_the_tools_last_after_the_compat_module_once()
    {
        var setup = Setup("Extra.Mod");
        var list = RotServerTools.ServerModules(RotLoadOrder.ServerOrder(setup));

        Assert.Equal(RotLoadOrder.ServerOrder(setup).Append(ServerToolsModule.ModuleId), list);
        Assert.Equal(RotStack.CompatModuleId, list[^2]);
        Assert.Equal(list, RotServerTools.ServerModules(list));
        // The server loads DedicatedServer.Windows after the whole list.
        var full = NightlyServer.FullOrder(RotStack.CoopModuleId, list, warSails: true).ToList();
        Assert.Equal([ServerToolsModule.ModuleId, RotStack.ServerLoadsLast], full.TakeLast(2));
        Assert.True(full.IndexOf(RotStack.CoopModuleId) < full.IndexOf(ServerToolsModule.ModuleId));
    }

    [Fact]
    public void The_payload_holds_the_tools_and_the_server_sync_and_the_exe_and_release_carry_it()
    {
        var xml = XDocument.Load(Path.Combine(Payload, "SubModule.xml"));
        Assert.Equal(ServerToolsModule.ModuleId, xml.Root!.Element("Id")!.Attribute("value")!.Value);
        // Optional LoadBeforeThis on CoopNightly: it loads after the RoT build of Coop.
        Assert.Contains(xml.Root.Descendants("DependedModuleMetadata"), item => (string?)item.Attribute("id") == RotStack.CoopModuleId && (string?)item.Attribute("order") == "LoadBeforeThis");
        foreach (var dll in new[] { "DedicatedServer.CoodszTools.dll", CoopProbe.AssemblyFileName })
            Assert.True(File.Exists(Path.Combine(Payload, "bin", "Win64_Shipping_Server", dll)), dll);

        var csproj = SourceFiles.Read(@"src\RealmOfThronesHost\RealmOfThronesHost.csproj");
        Assert.Contains(@"<CoodszPayload Include=""..\CoodszHost\ServerTools\DedicatedServer.CoodszTools\**\*"" Payload=""ServerAddOns"" PayloadFolder=""DedicatedServer.CoodszTools\"" />", csproj);
        Assert.Equal(RotServerTools.Payload, "ServerAddOns");
        var assets = SourceFiles.Read(@"tools\release-assets.json");
        Assert.Contains("src/CoodszHost/ServerTools/DedicatedServer.CoodszTools/bin/Win64_Shipping_Server/DedicatedServer.CoodszTools.dll", assets);
        var release = SourceFiles.Read(@"tools\Make-RealmOfThrones-Release.ps1");
        Assert.Contains(@"(Get-PayloadPairs 'src\CoodszHost\ServerTools\DedicatedServer.CoodszTools' 'DedicatedServer.CoodszTools/')", release);
    }

    [Fact]
    public void Install_copies_the_payload_once_and_says_when_it_is_missing()
    {
        var server = Path.Combine(_root, "server");
        Assert.False(ServerToolsModule.Installed(server));

        var line = RotServerTools.Install(server, Payload);
        Assert.Contains("Players do not need it.", line);
        Assert.True(ServerToolsModule.Installed(server));
        Assert.Equal(File.ReadAllBytes(Path.Combine(Payload, "bin", "Win64_Shipping_Server", CoopProbe.AssemblyFileName)),
            File.ReadAllBytes(Path.Combine(ServerToolsModule.ModuleFolder(server), "bin", "Win64_Shipping_Server", CoopProbe.AssemblyFileName)));
        Assert.Null(RotServerTools.Install(server, Payload));

        var missing = RotServerTools.Install(server, Path.Combine(_root, "nothing"));
        Assert.Contains(ServerToolsModule.Name + " is missing", missing);
    }

    [Fact]
    public void The_rules_written_for_the_RoT_server_are_what_the_module_reads()
    {
        var dataRoot = Path.Combine(_root, "tool", "CoopData", "DedicatedServer");

        // No file: the defaults, the Coop Server Tool's, on both sides.
        Assert.Equal(HostServerRules.Defaults, ServerRulesFile.Read(RotServerTools.RulesPath(dataRoot)));
        Assert.True(ServerRulesSettings.Defaults.PauseOnJoin && ServerRulesSettings.Defaults.FreezeOfflineXp);

        ServerRulesFile.Write(RotServerTools.RulesPath(dataRoot), new HostServerRules(UnstuckChoice.HostOnly, false, true, KingdomProtectionChoice.Always));
        var read = ServerRulesSettings.Parse(File.ReadAllText(ClanCapSettings.PathIn(Path.Combine(_root, "Documents"), dataRoot)));
        Assert.Equal(UnstuckMode.HostOnly, read.Unstuck);
        Assert.False(read.PauseOnJoin);
        Assert.True(read.FreezeOfflineXp);
        Assert.Equal(KingdomProtectionMode.Always, read.KingdomProtection);
    }

    [Fact]
    public void The_Settings_page_has_the_Coop_Server_Tools_controls_and_defaults_tucked_away()
    {
        var rot = XDocument.Parse(SourceFiles.Read(@"src\RealmOfThronesHost\MainWindow.xaml"));
        var coop = XDocument.Parse(SourceFiles.Read(@"src\CoodszHost\MainWindow.xaml"));
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        XElement Named(XDocument page, string name) => page.Descendants().Single(element => (string?)element.Attribute(x + "Name") == name);

        foreach (var box in new[] { "PauseOnJoinBox", "FreezeOfflineXpBox" })
        {
            Assert.Equal("True", (string?)Named(rot, box).Attribute("IsChecked"));
            Assert.Equal((string?)Named(coop, box).Attribute("IsChecked"), (string?)Named(rot, box).Attribute("IsChecked"));
            Assert.Equal((string?)Named(coop, box).Attribute("ToolTip"), (string?)Named(rot, box).Attribute("ToolTip"));
        }
        foreach (var box in new[] { "UnstuckBox", "KingdomProtectionBox", "ModSettingsNote", "ModSettingsMods", "ModSettingsValues" })
            Named(rot, box);
        // MCM does not run on this server: the page edits the server's own copy of the files, which the sync sends.
        Assert.Contains("own copy of your MCM settings", (string?)Named(rot, "ModSettingsNote").Attribute("Text"));
        // Both folds start closed.
        XNamespace wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var folds = rot.Descendants(wpf + "Expander").Where(fold => ((string?)fold.Attribute(XName.Get("AutomationProperties.AutomationId")))?.Contains("ServerRules") == true
            || ((string?)fold.Attribute(XName.Get("AutomationProperties.AutomationId")))?.Contains("ModSettings") == true).ToList();
        Assert.Equal(2, folds.Count);
        Assert.All(folds, fold => Assert.Null(fold.Attribute("IsExpanded")));

        var window = SourceFiles.Window(SourceFiles.Root(), "RealmOfThronesHost");
        Assert.Contains("private bool _pauseOnJoin = HostServerRules.Defaults.PauseOnJoin;", window);
        Assert.Contains("StartServerRules();", window);
        Assert.Contains("NoteJoinPause(_onlinePlayers, serving);", window);
        Assert.Contains("_playHold.Line(line, DateTime.UtcNow, _joinPause.Pausing)", window);
        Assert.Contains("ApplyKingdomProtection();", window);
        Assert.Contains("RotServerTools.StartVariables(_settings.DataRoot)", window);
        Assert.Contains("ShowKingdomProtection();", SourceFiles.Method(window, "private void ShowPage(string page)"));
        Assert.Contains("_joinPause.UpdateServing(connections, DateTime.UtcNow, _pauseOnJoin, serving)", window);
        Assert.Contains("if (step == JoinPauseStep.Resume && _joinPause.HostCommandCount != hostCommands)", window);
        Assert.Contains("RunCommandAndRead(_joinPause.ResumeCommand, () => _joinPause.HostCommandCount == hostCommands)", window);
        var console = SourceFiles.Method(window, "private async void SendConsoleCommand(string command)");
        foreach (var part in new[] { "Remember(Translator.T(check));", "_joinPause.HostSent(command);", "await RunCommandAndRead(command)" })
            Assert.Contains(part, console);
        Assert.True(console.IndexOf("Remember(Translator.T(check));", StringComparison.Ordinal) < console.IndexOf("await RunCommandAndRead(command)", StringComparison.Ordinal));
        Assert.True(console.IndexOf("_joinPause.HostSent(command);", StringComparison.Ordinal) < console.IndexOf("await RunCommandAndRead(command)", StringComparison.Ordinal));
        Assert.DoesNotContain("foreach (var line in reply)", console);
        Assert.Contains("RunCommandAndRead(hostSpeed, () => _joinPause.HostCommandCount == landed)", window);
        Assert.Contains("RunCommandAndRead(JoinPause.PauseCommand, () => _joinPause.HostCommandCount == asked)", window);
        Assert.Contains("_joinPause.HostSent(command);", SourceFiles.Method(window, "private async void NotePlayHold(string line)"));
        Assert.DoesNotContain("SendCommand(\"kick ", window);
        Assert.Contains("return (await _commands.Enqueue(async () => (await ReadCommandReply(command, run))!))!;", window);
        Assert.Contains("}, () => PlayHoldStillStands(generation, command));", window);
        Assert.Contains("_playHold.ResendStands(generation, command, _joinPause.Pausing);", window);
        Assert.Contains("() => HostPlayerLog.StillOn(_onlinePlayers, player);", window);
        Assert.Contains("_commands.WriteWithin(write, QueuedWriteWait)", window);
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(window, @"ServerCommandQueue\.(WriteBounded|TryWriteWithin)\(_stdinLock, ServerCommandQueue\.StdinLockWait").Count);
        Assert.DoesNotContain("_readingCommand", window);
        Assert.Contains("return _commandWriteFailed ? null : lines;", window);
        Assert.Contains("SendQueued(\"kick \" + player.Id, StillConnected(player));", window);
        Assert.Contains("WriteQueued(() => WriteLine(process, \"save\"))", window);
        Assert.Contains("return WriteQueued(() => WriteLine(process, line));", window);
        // Only ReadCommandReply and WriteLine write to the server's console pipe.
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(window, @"StandardInput\.WriteLine\(").Count);
        Assert.Contains("ServerToolsModule.Installed(server) ? RotServerTools.ServerModules(plan.Modules) : plan.Modules", window);
    }

    [Fact]
    public void The_TAOM_host_fixes_stay_wired_in_the_RoT_window()
    {
        // The queue's own behaviour is in ServerCommandQueueTests and TaomServerToolsTests; here the RoT window's use of it.
        var window = SourceFiles.Window(SourceFiles.Root(), "RealmOfThronesHost");
        const string readReply = "private async Task<IReadOnlyList<string>?> ReadCommandReply(string command, ServerRun run, Func<bool>? stillValid = null)";
        var reply = SourceFiles.Method(window, readReply);
        // The blocked DLL fix comes from the output thread: it hops to the window thread first.
        var dllFix = SourceFiles.Method(window, "private void ShowBlockedDllFix()");
        var hop = dllFix.IndexOf("Dispatcher.CheckAccess()", StringComparison.Ordinal);
        Assert.True(hop > 0 && hop < dllFix.IndexOf("ShowPage(", StringComparison.Ordinal), dllFix);

        // A queued command keeps its run (with the process id of a server this window did not start) and is dropped once
        // that run ended, before anything is written: it never reaches the next run.
        Assert.Contains("TryStarted(_settings.ServerRoot, out var started, out var processId)", SourceFiles.Method(window, "private ServerRun CurrentRun()"));
        Assert.Contains("return new ServerRun(null, ServerCommandQueue.ForeignStart(found, started), processId);", SourceFiles.Method(window, "private ServerRun CurrentRun()"));
        var check = reply.IndexOf("var check = ServerCommandQueue.Check(run, CurrentRun());", StringComparison.Ordinal);
        Assert.True(check > 0 && check < reply.IndexOf("SendThroughQueue", StringComparison.Ordinal) && check < reply.IndexOf("StandardInput.WriteLine", StringComparison.Ordinal), reply);
        Assert.Contains("var message = check == RunCheck.NoServer ? CheatBlockReason() : ServerCommandQueue.RunEnded;", reply);
        Assert.Equal(3, System.Text.RegularExpressions.Regex.Matches(window, @"var run = CurrentRun\(\);").Count);
        Assert.Contains("await ReadCommandReply(command, run, () => PlayHoldStillStands(generation, command));", SourceFiles.Method(window, "private async void NotePlayHold(string line)"));

        // A write to a full pipe is bounded: the next reply read waits for a stuck write, the run and a kick's still on the
        // server are checked again after that wait, and the write goes to the queued run's own process.
        var stuck = reply.IndexOf("ServerCommandQueue.AfterStuckWrite(_stuckWrite", StringComparison.Ordinal);
        var again = reply.IndexOf("if (run.Process is not { } queuedProcess || !ServerCommandQueue.SameRun(queuedProcess, _process))", StringComparison.Ordinal);
        var valid = reply.IndexOf("if (stillValid != null && !stillValid())", StringComparison.Ordinal);
        var write = reply.IndexOf("WriteBounded(", StringComparison.Ordinal);
        Assert.True(stuck > 0 && again > stuck && valid > again && write > valid && stuck < reply.IndexOf("var offset = file?.Length", StringComparison.Ordinal), reply);
        Assert.Contains("var process = queuedProcess;", reply);
        Assert.Contains("}, stillValid);", reply);
        Assert.Contains("if (written == ConsoleWrite.Dropped)", reply);
        Assert.Contains("_stuckWrite = writing;", reply);
        Assert.Contains("return new[] { ServerCommandQueue.ConsoleSlow };", reply);
        // The save and stop: a write that may still land counts as written, so the fallback never writes it a second time.
        Assert.Matches(@"!=\s*ConsoleWrite\.NotSent", SourceFiles.Method(window, "private bool WriteLine(System.Diagnostics.Process process, string line)"));
        Assert.DoesNotContain("ServerCommandQueue.TryWriteUnder(", window);
        // A command that may still land never shows a done line: Play hold, pause on join and the Cheats page.
        Assert.Contains("ServerCommandQueue.Unconfirmed(", SourceFiles.Method(window, "private async void NotePlayHold(string line)"));
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(SourceFiles.Method(window, "private async void DoJoinPause(JoinPauseStep step)"), @"ServerCommandQueue\.Unconfirmed\(").Count);
        var cheats = SourceFiles.Read(@"src\RealmOfThronesHost\CoopCheatsPage.xaml.cs");
        Assert.Contains("&& !ServerCommandQueue.Unconfirmed(lines);", SourceFiles.Method(cheats, "private async Task<(bool Ok, IReadOnlyList<string> Lines)> SendCoopAsync(string command)"));
        var slow = cheats.IndexOf("if (ServerCommandQueue.Unconfirmed(lines))", StringComparison.Ordinal);
        Assert.True(slow > 0 && slow < cheats.IndexOf("Translator.T(\"Done.\")", StringComparison.Ordinal));
        // A console queue reply that was not picked up is not sent; one picked up with no answer yet may still land.
        var queue = SourceFiles.Method(window, "private async Task<(string Text, bool Sent)> SendThroughQueue(string command)");
        Assert.Contains("!reply.PickedUp ? ServerCommandQueue.NotSent(reply.Text)", queue);
        Assert.Contains("!reply.Answered && !reply.Failed ? ServerCommandQueue.ConsoleSlow", queue);
        Assert.Contains("var (reply, sent) = await SendThroughQueue(command);", reply);

        // The save for a server this window did not start goes in its turn of the command queue too.
        Assert.Contains("WriteQueued(AskQueuedSave)", SourceFiles.Method(window, "private bool WaitForWorldSave()"));

        // The window keeps running whatever a handler did not catch, once it is up, and says so on the status line.
        var app = SourceFiles.Read(@"src\RealmOfThronesHost\App.xaml.cs");
        var loaded = app.IndexOf("if (MainWindow is not MainWindow { IsLoaded: true } window)", StringComparison.Ordinal);
        Assert.True(loaded > 0 && loaded < app.IndexOf("args.Handled = true;", StringComparison.Ordinal), app);
        Assert.Contains("NoteUnexpected(args.Exception)", app);
        Assert.Contains("SetControl(\"Something went wrong, and the tool kept running: \" + ex.Message, failed: true);", SourceFiles.Method(window, "internal void NoteUnexpected(Exception ex)"));

        // A players event that does not read keeps the list, and a run that starts or ends forgets it, so no kick goes by id
        // into another run.
        Assert.Contains("HostPlayerLog.Read(ReadPlayerLog(), _onlinePlayers)", window);
        var forget = SourceFiles.Method(window, "private void ForgetPlayers()");
        Assert.Contains("_onlinePlayers = [];", forget);
        Assert.Contains("_banKicks.Clear();", forget);
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(SourceFiles.Read(@"src\RealmOfThronesHost\MainWindow.ServerControl.cs"), @"ForgetPlayers\(\);").Count);
        Assert.Contains("ForgetPlayers();", SourceFiles.Method(window, "private bool TryServerUptime(out string uptime)"));
    }

    [Fact]
    public void Host_and_client_agree_without_the_tools_module_and_the_client_carries_the_sync()
    {
        var setup = Setup();
        var host = RotServerTools.ServerModules(RotLoadOrder.ServerOrder(setup)).Select(id => new SharedModEntry(id, id, "v1.0.0", "0")).ToList();
        var installed = RotLoadOrder.ClientOrder(setup).ToDictionary(id => id, _ => "v1.0.0", StringComparer.OrdinalIgnoreCase);

        Assert.DoesNotContain(RotSharedList.VersionProblems(host, installed), problem => problem.ModuleId == ServerToolsModule.ModuleId);
        Assert.DoesNotContain(RotSharedList.Entries(setup, _ => null), entry => entry.Id == ServerToolsModule.ModuleId);
        // A save the server made with the tools module still fits the setup it was made with.
        var saved = NightlyServer.FullOrder(RotStack.CoopModuleId, RotServerTools.ServerModules(RotLoadOrder.ServerOrder(setup)), warSails: true);
        Assert.Null(CoopJoinRules.SaveProblem(saved, RotServerPath.FullServerOrder(setup)));

        // The client half of the sync rides in the join password module the Client Launcher puts in on Connect.
        Assert.True(File.Exists(Path.Combine(SourceFiles.Root(), "compat", "CoodszJoinPassword", "module", "bin", "Win64_Shipping_Client", CoopProbe.AssemblyFileName)));
        Assert.Contains(@"<CoodszPayload Include=""..\..\compat\CoodszJoinPassword\module\**\*"" Payload=""ClientAddOns""", SourceFiles.Read(@"src\RealmOfThronesLauncher\RealmOfThronesLauncher.csproj"));
        Assert.Equal(ClientAddOns.JoinPasswordId, RotLoadOrder.LaunchModules(setup, joinPassword: true)[^1]);
    }

    // The mod settings page (ServerModSettings): the folders as the sync finds them, Documents and the server's own copy.
    private (string Documents, string Game, string DataRoot, string Server) ModSettingsFolders()
    {
        var documents = Path.Combine(_root, "Documents");
        var game = Path.Combine(documents, "Mount and Blade II Bannerlord", "Configs", "ModSettings");
        var dataRoot = Path.Combine(_root, "tool", "CoopData", "DedicatedServer");
        return (documents, game, dataRoot, ServerModSettings.ServerRoot(dataRoot));
    }

    private static string WriteSetting(string root, string folder, string id, string json)
    {
        var path = Path.Combine(root, "Global", folder, id + ".json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, json);
        return path;
    }

    private static string Zip(string path, params (string Name, string Text)[] entries)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (name, text) in entries)
        {
            var entry = zip.CreateEntry(name);
            using var writer = new StreamWriter(entry.Open());
            writer.Write(text);
        }

        return path;
    }

    [Fact]
    public void Seed_copies_the_missing_files_and_after_it_every_mod_is_still_sent()
    {
        var (documents, game, dataRoot, server) = ModSettingsFolders();
        WriteSetting(game, "RBM", "RBM_v1", "{\"Speed\": 1}");
        WriteSetting(game, "Diplomacy", "Diplomacy_v1", "{\"Wars\": true}");
        // The host changed RBM here, so it stays.
        var edited = WriteSetting(server, "RBM", "RBM_v1", "{\"Speed\": 1}");
        Assert.Null(ServerModSettings.Save(server, edited, new Dictionary<string, string> { ["Speed"] = "5" }));

        Assert.Equal(1, ServerModSettings.Seed(server, game));
        Assert.Contains("\"Speed\": 5", File.ReadAllText(edited));
        Assert.Equal(0, ServerModSettings.Seed(server, game));
        // A mod the host sets up in their own game after the first seed comes in on the next one.
        WriteSetting(game, "Banners", "Banners_v1", "{\"Big\": true}");
        Assert.Equal(1, ServerModSettings.Seed(server, game));

        // All or nothing: the server's folder holds files now, so the sync sends only it, with every mod in it.
        Assert.Equal(server, SettingsFiles.FindModSettingsRoot(documents, dataRoot));
        var sent = SettingsFiles.CaptureFromFiles(server, new KeepOwnList());
        Assert.Equal(new[] { "Banners_v1", "Diplomacy_v1", "RBM_v1" }, sent.Select(snapshot => snapshot.SettingsId).OrderBy(id => id));
        Assert.Contains("5", sent.Single(snapshot => snapshot.SettingsId == "RBM_v1").Values.Single());
    }

    [Fact]
    public void Seed_refreshes_files_the_host_did_not_change_keeps_changed_ones_and_goes_past_a_locked_file()
    {
        var (_, game, _, server) = ModSettingsFolders();
        var pcRbm = WriteSetting(game, "RBM", "RBM_v1", "{\"Speed\": 1}");
        var pcDip = WriteSetting(game, "Diplomacy", "Diplomacy_v1", "{\"Wars\": true}");
        var pcBan = WriteSetting(game, "Banners", "Banners_v1", "{\"Big\": true}");
        var failed = new List<string>();
        using (new FileStream(pcBan, FileMode.Open, FileAccess.Read, FileShare.None))
            Assert.Equal(2, ServerModSettings.Seed(server, game, failed));
        Assert.Equal(new[] { Path.Combine("Global", "Banners", "Banners_v1.json") }, failed);
        // A locked file is tried again on the next seed, and the files it could not copy before come in.
        Assert.Equal(1, ServerModSettings.Seed(server, game));

        // The host changes RBM here; then both files change in the host's own game.
        var rbm = Path.Combine(server, "Global", "RBM", "RBM_v1.json");
        var dip = Path.Combine(server, "Global", "Diplomacy", "Diplomacy_v1.json");
        Assert.Null(ServerModSettings.Save(server, rbm, new Dictionary<string, string> { ["Speed"] = "9" }));
        Assert.Contains(Path.Combine("Global", "RBM", "RBM_v1.json"), System.Text.Json.JsonSerializer.Deserialize<string[]>(File.ReadAllText(ServerModSettings.EditedListPath(server)))!);
        File.WriteAllText(pcRbm, "{\"Speed\": 2}");
        File.WriteAllText(pcDip, "{\"Wars\": false}");
        // Content decides, not dates: an older Documents copy that differs still comes in.
        File.SetLastWriteTimeUtc(pcDip, DateTime.UtcNow.AddDays(-1));

        Assert.Equal(1, ServerModSettings.Seed(server, game));
        Assert.Equal("{\"Wars\": false}", File.ReadAllText(dip));
        Assert.Contains("9", File.ReadAllText(rbm));
        // The list of changed files sits next to the folder, so it never makes the sync's folder count as filled.
        Assert.False(ServerModSettings.EditedListPath(server).StartsWith(server + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Save_replaces_the_file_in_one_step_and_keeps_each_numbers_kind()
    {
        var (_, _, _, server) = ModSettingsFolders();
        var path = WriteSetting(server, "RBM", "RBM_v1",
            "{\"Scale\": 2.0, \"Ratio\": 0.5, \"Count\": 3, \"On\": true, \"Name\": \"x\", \"List\": [1], \"Big\": 1e3, \"Huge\": 3000000000, \"Empty\": null}");

        Assert.Null(ServerModSettings.Save(server, path, new Dictionary<string, string> { ["Count"] = "5", ["On"] = "false", ["Name"] = "y", ["Ratio"] = "1", ["Empty"] = "7" }));
        var text = File.ReadAllText(path);
        foreach (var part in new[] { "\"Scale\": 2.0", "\"Ratio\": 1.0", "\"Count\": 5", "\"On\": false", "\"Name\": \"y\"", "\"List\"", "\"Big\": 1e3", "\"Huge\": 3000000000", "\"Empty\": null" })
            Assert.Contains(part, text);
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path)!, "*.tmp"));

        // A value that does not fit its kind writes nothing.
        Assert.Equal("Count needs a whole number.", ServerModSettings.Save(server, path, new Dictionary<string, string> { ["Scale"] = "9", ["Count"] = "2.5" }));
        Assert.Equal("Scale needs a number.", ServerModSettings.Save(server, path, new Dictionary<string, string> { ["Scale"] = "lots" }));
        Assert.Equal(text, File.ReadAllText(path));

        // 1e3 is a float, and a whole number past int32 stays whole.
        Assert.Null(ServerModSettings.Save(server, path, new Dictionary<string, string> { ["Big"] = "5", ["Huge"] = "4000000000" }));
        Assert.Contains("\"Big\": 5.0", File.ReadAllText(path));
        Assert.Contains("\"Huge\": 4000000000", File.ReadAllText(path));
        var values = ServerModSettings.Load(server, ModCompatDatabase.Empty).Single().Values;
        Assert.True(values.Single(value => value.Name == "Count").Whole);
        Assert.True(values.Single(value => value.Name == "Huge").Whole);
        Assert.False(values.Single(value => value.Name == "Scale").Whole);
        Assert.Equal(System.Text.Json.JsonValueKind.Null, values.Single(value => value.Name == "Empty").Kind);
    }

    [Fact]
    public void Each_mod_is_gameplay_or_looks_from_the_community_list_and_unknown_counts_as_gameplay()
    {
        // The list built into Core, as CommunityCompat.Current has it with no download.
        var list = ModCompatDatabase.Embedded();
        Assert.True(list.Count > 0);
        Assert.True(ServerModSettings.IsLooks(list, "AggregatedIncome_v1", "AggregatedIncome"));
        Assert.True(ServerModSettings.IsLooks(list, "AIBannerIcons_v2", "AI Banner Icons"));
        // Client only alone is not looks: XorberaxLegacy is a gameplay mod, so its values stay synced and locked.
        Assert.Equal(CommunityStatus.ClientOnly, list.Find("XorberaxLegacy")!.Status);
        Assert.False(ServerModSettings.IsLooks(list, "XorberaxLegacy", "XorberaxLegacy"));
        Assert.False(ServerModSettings.IsLooks(list, "AgricultureEstate_v1", "AgricultureEstate"));
        Assert.False(ServerModSettings.IsLooks(list, "NoSuchMod_v1", "NoSuchMod"));

        // Matched on the id, the folder, or the name with its spaces removed.
        var own = ModCompatDatabase.Parse("[{\"id\": \"My.Ui\", \"name\": \"My Pretty Bars\", \"category\": \"UI\"}, {\"id\": \"Loud\", \"category\": \"Audio\"}, {\"id\": \"War\", \"category\": \"Gameplay\"}]")!;
        Assert.True(ServerModSettings.IsLooks(own, "MyPrettyBars_v1", "MyPrettyBars"));
        Assert.True(ServerModSettings.IsLooks(own, "Something", "My.Ui"));
        Assert.True(ServerModSettings.IsLooks(own, "Loud", ""));
        Assert.False(ServerModSettings.IsLooks(own, "War", "War"));

        // A copy of a gameplay id in a looks mod's folder never unlocks it.
        var (_, _, _, server) = ModSettingsFolders();
        WriteSetting(server, "RBM", "RBM_v1", "{\"Speed\": 1}");
        WriteSetting(server, "AggregatedIncome", "RBM_v1", "{\"Speed\": 9}");
        WriteSetting(server, "AggregatedIncome", "AggregatedIncome_v1", "{\"Show\": true}");
        var files = ServerModSettings.Load(server, list);
        Assert.All(files.Where(file => file.Id == "RBM_v1"), file => Assert.False(file.Looks));
        Assert.True(files.Single(file => file.Id == "AggregatedIncome_v1").Looks);
    }

    [Fact]
    public void The_looks_mods_go_into_recipes_json_next_to_the_hosts_own_entries()
    {
        var (_, game, dataRoot, _) = ModSettingsFolders();
        var gameServer = Path.Combine(_root, "server");
        var module = ServerToolsModule.ModuleFolder(gameServer);
        Directory.CreateDirectory(module);
        var recipes = Path.Combine(module, "recipes.json");
        File.WriteAllText(recipes, "{\"Mods\": [\"kept\"], \"PlayersOwnSettings\": [\"HandAdded\"]}");
        WriteSetting(game, "AggregatedIncome", "AggregatedIncome_v1", "{\"Show\": true}");
        WriteSetting(game, "RBM", "RBM_v1", "{\"Speed\": 1}");

        Assert.Equal(2, ServerModSettings.Prepare(dataRoot, ServerToolsModule.ModuleFolder(gameServer), game, ModCompatDatabase.Embedded()));
        var keep = SettingsFiles.ReadKeepOwn(Path.Combine(module, KeepOwnList.FileName));
        Assert.Equal(2, keep.Count);
        Assert.True(keep.Keeps("AggregatedIncome_v1"));
        Assert.True(keep.Keeps("HandAdded"));
        Assert.False(keep.Keeps("RBM_v1"));
        Assert.Contains("kept", File.ReadAllText(recipes));
        // The sync leaves the looks mod out, so each player keeps their own values for it.
        var server = ServerModSettings.ServerRoot(dataRoot);
        Assert.Equal(new[] { "RBM_v1" }, SettingsFiles.CaptureFromFiles(server, keep).Select(snapshot => snapshot.SettingsId));

        // A mod that is no longer looks leaves the list; the host's own entry stays.
        Assert.True(ServerModSettings.WritePlayersOwn(ServerToolsModule.ModuleFolder(gameServer), ServerModSettings.Load(server, ModCompatDatabase.Empty)));
        keep = SettingsFiles.ReadKeepOwn(recipes);
        Assert.Equal(1, keep.Count);
        Assert.True(keep.Keeps("HandAdded"));

        // A recipes.json that does not read is kept as recipes.json.bak.
        File.WriteAllText(recipes, "{broken");
        Assert.True(ServerModSettings.WritePlayersOwn(ServerToolsModule.ModuleFolder(gameServer), ServerModSettings.Load(server, ModCompatDatabase.Embedded())));
        Assert.Equal("{broken", File.ReadAllText(recipes + ".bak"));
        Assert.True(SettingsFiles.ReadKeepOwn(recipes).Keeps("AggregatedIncome_v1"));

        Assert.False(ServerModSettings.WritePlayersOwn(ServerToolsModule.ModuleFolder(Path.Combine(_root, "no-server")), ServerModSettings.Load(server, ModCompatDatabase.Embedded())));
    }

    [Fact]
    public void Back_to_this_PCs_values_puts_this_PCs_file_in_and_removes_one_this_PC_does_not_have()
    {
        var (_, game, _, server) = ModSettingsFolders();
        WriteSetting(game, "RBM", "RBM_v1", "{\"Speed\": 1}");
        var rbm = WriteSetting(server, "RBM", "RBM_v1", "{\"Speed\": 5}");
        var imported = WriteSetting(server, "Diplomacy", "Diplomacy_v1", "{\"Wars\": false}");
        Assert.Null(ServerModSettings.Save(server, rbm, new Dictionary<string, string> { ["Speed"] = "6" }));

        Assert.True(ServerModSettings.HasPcCopy(server, game, rbm));
        Assert.False(ServerModSettings.HasPcCopy(server, game, imported));
        Assert.Equal(1, ServerModSettings.Reset(server, game, rbm));
        Assert.Equal("{\"Speed\": 1}", File.ReadAllText(rbm));
        Assert.False(File.Exists(ServerModSettings.EditedListPath(server)));
        Assert.True(File.Exists(imported));

        // While the server runs, one with no file on this PC stays, so every player keeps getting the same values;
        // all mods still get this PC's files in place.
        var kept = new List<string>();
        File.WriteAllText(rbm, "{\"Speed\": 8}");
        Assert.Equal(1, ServerModSettings.Reset(server, game, serverRunning: true, kept: kept));
        Assert.Equal("{\"Speed\": 1}", File.ReadAllText(rbm));
        Assert.True(File.Exists(imported));
        Assert.Equal(new[] { "Diplomacy (Diplomacy_v1)" }, kept);
        kept.Clear();
        Assert.Equal(0, ServerModSettings.Reset(server, game, imported, serverRunning: true, kept: kept));
        Assert.True(File.Exists(imported));
        Assert.Equal(new[] { "Diplomacy (Diplomacy_v1)" }, kept);

        // Once it is stopped, one with no file on this PC goes.
        Assert.Equal(0, ServerModSettings.Reset(server, game, imported));
        Assert.False(File.Exists(imported));

        // All mods: this PC's files go in over the server's, and the rest goes.
        File.WriteAllText(rbm, "{\"Speed\": 7}");
        WriteSetting(server, "Other", "Other_v1", "{}");
        Assert.Equal(1, ServerModSettings.Reset(server, game));
        Assert.Equal("{\"Speed\": 1}", File.ReadAllText(rbm));
        Assert.Equal(new[] { rbm }, Directory.GetFiles(Path.Combine(server, "Global"), "*.json", SearchOption.AllDirectories));
    }

    [Fact]
    public void Import_picks_the_target_by_settings_id_and_skips_the_rest_with_one_safe_line()
    {
        var (_, game, _, server) = ModSettingsFolders();
        var rbm = WriteSetting(server, "RBM", "RBM_v1", "{\"Speed\": 1}");
        WriteSetting(game, "RBM", "CombatAI", "{\"On\": true}");
        var many = "{" + string.Join(", ", Enumerable.Range(0, ServerModSettings.MaxValues + 1).Select(i => "\"v" + i + "\": " + i)) + "}";
        var zip = Zip(Path.Combine(_root, "sent.zip"),
            // Another case of RBM_v1 is skipped; the exact id from a look-alike folder still goes over the server's own file.
            ("ModSettings/Global/R.B M/rbm_V1.json", "{\"Speed\": 3}"),
            ("Global/R.B M/RBM_v1.json", "{\"Speed\": 4}"),
            ("Global/RBM/RBMAI_v2.json", "{\"Aim\": 1}"),
            ("Global/RBM/CombatAI.json", "{\"On\": false}"),
            ("Global/RBM/combatai.json", "{\"On\": false}"),
            ("Global/RBM/NotOnServer_v1.json", "{}"),
            ("Global/Evil/RBM.json", "{}"),
            ("../Global/RBM/up.json", "{}"),
            ("Global/RBM/../../../escape.json", "{}"),
            ("C:/Windows/Global/RBM/full.json", "{}"),
            ("/Global/RBM/rooted.json", "{}"),
            ("a/b/c/d/Global/RBM/RBM_deep.json", "{}"),
            ("Global/RBM/CON.json", "{}"),
            ("Global/RBM/COM\u00B9.txt.json", "{}"),
            ("Global/RBM/RBM_big.json", "{\"a\": \"" + new string('x', 300 * 1024) + "\"}"),
            ("Global/RBM/RBM_broken.json", "{not json"),
            ("Global/RBM/RBM_list.json", "[1, 2]"),
            ("Global/RBM/RBM_many.json", many),
            ("PerCampaign/RBM/RBM_v1.json", "{}"),
            ("Global/Evil\nFAKE LINE/x.json", "{}"),
            ("Global/RBM/" + new string('a', 300) + ".json", "{}"));
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Update))
        {
            var link = archive.CreateEntry("Global/RBM/RBM_link.json");
            link.ExternalAttributes = 0xA1FF << 16;
        }

        var result = ServerModSettings.Import(zip, server, game, ["RBM", "Bannerlord.Diplomacy"]);

        Assert.Equal(3, result.Added);
        Assert.Equal("{\"Speed\": 4}", File.ReadAllText(rbm));
        Assert.Equal(new[] { "RBM_v1.json" }, Directory.GetFiles(Path.Combine(server, "Global", "RBM"), "rbm_v1.json").Select(Path.GetFileName));
        Assert.True(File.Exists(Path.Combine(server, "Global", "RBM", "RBMAI_v2.json")));
        // The host's CombatAI comes in by its exact id; combatai is a case twin of it and is skipped.
        Assert.Equal(new[] { "CombatAI.json" }, Directory.GetFiles(Path.Combine(server, "Global", "RBM"), "combatai.json").Select(Path.GetFileName));
        Assert.Contains("Global/RBM/combatai.json: combatai differs only in capital letters from CombatAI, so it was skipped.", result.Skipped);
        Assert.Equal(new[] { "RBM" }, Directory.GetDirectories(Path.Combine(server, "Global")).Select(Path.GetFileName));
        Assert.False(File.Exists(Path.Combine(_root, "escape.json")));
        Assert.Equal(new[] { "RBM (CombatAI)", "RBM (RBM_v1)", "RBM (RBMAI_v2)" }.Order(StringComparer.Ordinal), result.Mods.Order(StringComparer.Ordinal));
        Assert.Equal(3, System.Text.Json.JsonDocument.Parse(File.ReadAllText(ServerModSettings.EditedListPath(server))).RootElement.GetArrayLength());

        void Line(string text) => Assert.Contains(text, result.Skipped);
        Line("ModSettings/Global/R.B M/rbm_V1.json: rbm_V1 differs only in capital letters from RBM_v1, so it was skipped.");
        Line("Global/RBM/NotOnServer_v1.json: NotOnServer_v1 is not a settings file of the mod RBM, so it was skipped.");
        Line("Global/Evil/RBM.json: the server does not have the mod Evil, so it was skipped.");
        Line("../Global/RBM/up.json: points outside what was sent, so it was skipped.");
        Line("Global/RBM/../../../escape.json: points outside what was sent, so it was skipped.");
        Line("C:/Windows/Global/RBM/full.json: a full path, not a file inside what was sent, so it was skipped.");
        Line("/Global/RBM/rooted.json: a full path, not a file inside what was sent, so it was skipped.");
        Line("a/b/c/d/Global/RBM/RBM_deep.json: not a Global mod settings file (Global\\mod\\id.json), so it was skipped.");
        Line("Global/RBM/CON.json: a file or folder name Windows can't use, so it was skipped.");
        Line("Global/RBM/COM\u00B9.txt.json: a file or folder name Windows can't use, so it was skipped.");
        Line("Global/RBM/RBM_big.json: bigger than 256 KB, so it was skipped.");
        Line("Global/RBM/RBM_broken.json: not valid JSON, so it was skipped.");
        Line("Global/RBM/RBM_list.json: not a mod settings file (its JSON is not an object), so it was skipped.");
        Line("Global/RBM/RBM_many.json: more than 512 values, more than players can get, so it was skipped.");
        Line("PerCampaign/RBM/RBM_v1.json: not a Global mod settings file (Global\\mod\\id.json), so it was skipped.");
        Line("Global/RBM/RBM_link.json: a link, not a file, so it was skipped.");
        Line("Global/EvilFAKE LINE/x.json: a file or folder name Windows can't use, so it was skipped.");
        Assert.Equal(19, result.Skipped.Count);
        // A line never breaks or runs long, whatever names were sent.
        Assert.All(result.Skipped, line => Assert.DoesNotContain('\n', line));
        Assert.All(result.Skipped, line => Assert.True(line.Length < 220, line));

        // A mod whose folder drops the Bannerlord. of its module id, from a folder the player sent.
        var folder = Path.Combine(_root, "sent", "Configs", "ModSettings");
        WriteSetting(folder, "Diplomacy", "Diplomacy_v1", "{\"Wars\": true}");
        var fromFolder = ServerModSettings.Import(Path.Combine(_root, "sent"), server, game, ["RBM", "Bannerlord.Diplomacy"]);
        Assert.Equal(1, fromFolder.Added);
        Assert.Empty(fromFolder.Skipped);
        // Named after the mod as the server has it, not the folder that was sent.
        Assert.True(File.Exists(Path.Combine(server, "Global", "Bannerlord.Diplomacy", "Diplomacy_v1.json")));
    }

    [Fact]
    public void Import_takes_nothing_from_a_zip_with_too_many_files_and_stops_at_16_MB()
    {
        var (_, game, _, server) = ModSettingsFolders();
        var many = Zip(Path.Combine(_root, "many.zip"), Enumerable.Range(0, ServerModSettings.MaxEntries + 1).Select(i => ("Global/RBM/RBM_" + i + ".json", "{}")).ToArray());
        var result = ServerModSettings.Import(many, server, game, ["RBM"]);
        Assert.Equal(0, result.Added);
        Assert.Equal(new[] { "many.zip: more than 500 files, so nothing was taken from it." }, result.Skipped);
        Assert.False(Directory.Exists(Path.Combine(server, "Global")));

        // 64 files of 256 KB make 16 MB exactly: all go in, and one more byte is over. A broken file first does not count.
        var full = "{\"a\": \"" + new string('x', (int)ServerModSettings.MaxFileBytes - 9) + "\"}";
        Assert.Equal(ServerModSettings.MaxFileBytes, full.Length);
        var entries = Enumerable.Range(0, 64).Select(i => ("Global/RBM/RBM_" + i + ".json", full)).Append(("Global/RBM/RBM_last.json", "{}"))
            .Prepend(("Global/RBM/RBM_broken.json", full[..^1])).ToArray();
        var big = ServerModSettings.Import(Zip(Path.Combine(_root, "big.zip"), entries), server, game, ["RBM"]);
        Assert.Equal(64, big.Added);
        Assert.Equal(new[] { "Global/RBM/RBM_broken.json: not valid JSON, so it was skipped.", "big.zip: more than 16 MB in all, so the rest was skipped." }, big.Skipped);
        Assert.False(File.Exists(Path.Combine(server, "Global", "RBM", "RBM_last.json")));
    }

    [Fact]
    public void Import_goes_on_past_a_file_it_can_not_write_and_leaves_no_temp_file()
    {
        var (_, game, _, server) = ModSettingsFolders();
        var locked = WriteSetting(server, "RBM", "RBM_v1", "{\"Speed\": 1}");
        var zip = Zip(Path.Combine(_root, "sent.zip"), ("Global/RBM/RBM_v1.json", "{\"Speed\": 2}"), ("Global/RBM/RBM_v2.json", "{\"Speed\": 3}"));

        ModSettingsImport result;
        using (new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None))
            result = ServerModSettings.Import(zip, server, game, ["RBM"]);

        Assert.Equal(1, result.Added);
        Assert.Equal("Global/RBM/RBM_v1.json: could not be written (the file may be in use), so it was skipped.", Assert.Single(result.Skipped));
        Assert.Equal("{\"Speed\": 1}", File.ReadAllText(locked));
        Assert.True(File.Exists(Path.Combine(server, "Global", "RBM", "RBM_v2.json")));
        Assert.Empty(Directory.GetFiles(server, "*.tmp", SearchOption.AllDirectories));
    }

    [Fact]
    public void With_no_mod_settings_the_fold_shows_Import_first()
    {
        // No MCM file on this PC or the server: the page has nothing to list, so the fold shows its empty state.
        var (_, game, _, server) = ModSettingsFolders();
        ServerModSettings.Seed(server, game);
        Assert.Empty(ServerModSettings.Load(server, ModCompatDatabase.Embedded()));
        // Import fills it, and the list shows the mod.
        var zip = Zip(Path.Combine(_root, "sent.zip"), ("Global/RBM/RBM_v1.json", "{\"Speed\": 2}"));
        Assert.Equal(1, ServerModSettings.Import(zip, server, game, ["RBM"]).Added);
        Assert.Equal("RBM (RBM_v1)", Assert.Single(ServerModSettings.Load(server, ModCompatDatabase.Embedded())).Label);

        // The empty state is the first thing in the fold after its note, with Import as its main button.
        var rot = XDocument.Parse(SourceFiles.Read(@"src\RealmOfThronesHost\MainWindow.xaml"));
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var empty = rot.Descendants().Single(element => (string?)element.Attribute(x + "Name") == "ModSettingsEmpty");
        Assert.Equal("ModSettingsNote", (string?)empty.ElementsBeforeSelf().Last().Attribute(x + "Name"));
        var texts = empty.Descendants().Select(element => (string?)element.Attribute("Text")).Where(text => text != null).ToList();
        Assert.Equal("No mod settings yet", texts[0]);
        var buttons = empty.Descendants().Where(element => element.Name.LocalName == "Button").ToList();
        Assert.Equal("ImportModSettingsZip_Click", (string?)buttons[0].Attribute("Click"));
        Assert.Contains("Primary", (string?)buttons[0].Attribute("Style"));
        Assert.Contains(buttons, button => (string?)button.Attribute("Click") == "ImportModSettingsFolder_Click");
    }

    [Fact]
    public void A_list_of_changed_files_that_can_not_be_read_is_never_written_over()
    {
        var (_, game, _, server) = ModSettingsFolders();
        var pc = WriteSetting(game, "RBM", "RBM_v1", "{\"Speed\": 1}");
        var rbm = WriteSetting(server, "RBM", "RBM_v1", "{\"Speed\": 5}");
        var list = ServerModSettings.EditedListPath(server);
        File.WriteAllText(list, "{broken");

        Assert.Throws<IOException>(() => ServerModSettings.Save(server, rbm, new Dictionary<string, string> { ["Speed"] = "6" }));
        Assert.Throws<IOException>(() => ServerModSettings.Reset(server, game, rbm));
        Assert.Throws<IOException>(() => ServerModSettings.Import(Zip(Path.Combine(_root, "sent.zip"), ("Global/RBM/RBM_v1.json", "{}")), server, game, ["RBM"]));
        // Seed only copies missing files then, so nothing the host may have changed is refreshed.
        File.WriteAllText(pc, "{\"Speed\": 2}");
        WriteSetting(game, "RBM", "RBM_v2", "{}");
        Assert.Equal(1, ServerModSettings.Seed(server, game));
        Assert.Equal("{\"Speed\": 5}", File.ReadAllText(rbm));
        Assert.Equal("{broken", File.ReadAllText(list));
    }

    [Fact]
    public void Back_to_this_PCs_values_stopped_by_a_locked_file_keeps_the_files_it_did_not_get_to()
    {
        var (_, game, _, server) = ModSettingsFolders();
        WriteSetting(game, "A", "A_v1", "{\"x\": 1}");
        WriteSetting(game, "B", "B_v1", "{\"x\": 1}");
        var a = WriteSetting(server, "A", "A_v1", "{\"x\": 1}");
        var b = WriteSetting(server, "B", "B_v1", "{\"x\": 1}");
        Assert.Null(ServerModSettings.Save(server, a, new Dictionary<string, string> { ["x"] = "2" }));
        Assert.Null(ServerModSettings.Save(server, b, new Dictionary<string, string> { ["x"] = "2" }));

        using (new FileStream(b, FileMode.Open, FileAccess.Read, FileShare.None))
            Assert.Throws<UnauthorizedAccessException>(() => ServerModSettings.Reset(server, game));
        var listed = System.Text.Json.JsonSerializer.Deserialize<string[]>(File.ReadAllText(ServerModSettings.EditedListPath(server)))!;
        Assert.Equal(new[] { Path.Combine("Global", "B", "B_v1.json") }, listed);
        Assert.Equal("{\"x\": 1}", File.ReadAllText(a));
    }

    [Fact]
    public void One_settings_id_never_gets_two_files_from_an_import_or_a_seed()
    {
        var (_, game, _, server) = ModSettingsFolders();
        var zip = Zip(Path.Combine(_root, "sent.zip"), ("Global/RBM/RBM_new.json", "{\"a\": 1}"), ("Global/R.B M/rbm_NEW.json", "{\"a\": 2}"));
        var result = ServerModSettings.Import(zip, server, game, ["RBM"]);
        Assert.Equal(1, result.Added);
        Assert.Equal(new[] { "Global/R.B M/rbm_NEW.json: rbm_NEW differs only in capital letters from RBM_new, so it was skipped." }, result.Skipped);
        Assert.Single(Directory.GetFiles(Path.Combine(server, "Global"), "*.json", SearchOption.AllDirectories));

        // The host's game has the same id in another folder: the server keeps its own file.
        WriteSetting(game, "RBMOld", "RBM_new", "{\"a\": 3}");
        Assert.Equal(0, ServerModSettings.Seed(server, game));
        Assert.Single(Directory.GetFiles(Path.Combine(server, "Global"), "*.json", SearchOption.AllDirectories));
    }

    [Fact]
    public void An_id_the_host_listed_by_hand_stays_theirs_in_recipes_json()
    {
        var (_, _, _, server) = ModSettingsFolders();
        var gameServer = Path.Combine(_root, "server");
        var module = ServerToolsModule.ModuleFolder(gameServer);
        Directory.CreateDirectory(module);
        var recipes = Path.Combine(module, "recipes.json");
        File.WriteAllText(recipes, "{\"PlayersOwnSettings\": [\"AggregatedIncome_v1\"]}");
        WriteSetting(server, "AggregatedIncome", "AggregatedIncome_v1", "{\"Show\": true}");

        Assert.True(ServerModSettings.WritePlayersOwn(ServerToolsModule.ModuleFolder(gameServer), ServerModSettings.Load(server, ModCompatDatabase.Embedded())));
        Assert.DoesNotContain("CoodszLooksSettings", File.ReadAllText(recipes));
        // Not looks any more: the host's own entry still stays.
        Assert.True(ServerModSettings.WritePlayersOwn(ServerToolsModule.ModuleFolder(gameServer), ServerModSettings.Load(server, ModCompatDatabase.Empty)));
        Assert.True(SettingsFiles.ReadKeepOwn(recipes).Keeps("AggregatedIncome_v1"));
    }

    [Fact]
    public void Prepare_writes_recipes_json_even_when_the_seed_fails_and_still_says_why()
    {
        var (_, game, dataRoot, _) = ModSettingsFolders();
        var gameServer = Path.Combine(_root, "server");
        Directory.CreateDirectory(ServerToolsModule.ModuleFolder(gameServer));
        WriteSetting(ServerModSettings.ServerRoot(dataRoot), "AggregatedIncome", "AggregatedIncome_v1", "{\"Show\": true}");
        // A folder in Documents the seed may not list makes it fail partway.
        var locked = Path.Combine(game, "Global", "Locked");
        Directory.CreateDirectory(locked);
        var info = new DirectoryInfo(locked);
        var deny = new System.Security.AccessControl.FileSystemAccessRule(System.Security.Principal.WindowsIdentity.GetCurrent().User!,
            System.Security.AccessControl.FileSystemRights.ListDirectory, System.Security.AccessControl.AccessControlType.Deny);
        var rules = info.GetAccessControl();
        rules.AddAccessRule(deny);
        info.SetAccessControl(rules);
        try
        {
            Assert.Throws<UnauthorizedAccessException>(() => ServerModSettings.Prepare(dataRoot, ServerToolsModule.ModuleFolder(gameServer), game, ModCompatDatabase.Embedded()));
            Assert.True(SettingsFiles.ReadKeepOwn(Path.Combine(ServerToolsModule.ModuleFolder(gameServer), KeepOwnList.FileName)).Keeps("AggregatedIncome_v1"));
        }
        finally
        {
            rules.RemoveAccessRule(deny);
            info.SetAccessControl(rules);
        }
    }

    // The window parts no test can run without WPF: each fix stays wired where it was put.
    [Fact]
    public void The_page_keeps_recipes_json_in_step_and_never_removes_a_mod_while_the_server_runs()
    {
        var window = SourceFiles.Window(SourceFiles.Root(), "RealmOfThronesHost");
        var show = SourceFiles.Method(window, "private void ShowModSettings(string? keep = null)");
        Assert.Contains("ServerModSettings.WritePlayersOwn(ServerToolsModule.ModuleFolder(_settings.ServerRoot), _modFiles, line => NoteLog(", show);
        Assert.True(show.IndexOf("ServerModSettings.Load(", StringComparison.Ordinal) < show.IndexOf("ServerModSettings.WritePlayersOwn(", StringComparison.Ordinal));
        Assert.Contains("private bool ModSettingsServerUp => _startingServer || OwnsServerProcess() || ServerIsRunning();", window);
        var click = SourceFiles.Method(window, "private void ResetModSettings_Click(object sender, RoutedEventArgs e)");
        var up = click.IndexOf("if (ModSettingsServerUp)", StringComparison.Ordinal);
        Assert.True(up > 0 && up < click.IndexOf("ConfirmWindow.Ask(", StringComparison.Ordinal));
        Assert.Contains("ModSettingsStatus.Text = StopFirstLine(file.Label);", click);
        Assert.Contains("ServerModSettings.Reset(ModSettingsRoot, ServerModSettings.DocumentsRoot, path, ModSettingsServerUp, kept);",
            SourceFiles.Method(window, "private void ResetModSettings(string? path, string? keep)"));
    }

    [Fact]
    public void Import_never_pushes_a_servers_mod_out_of_the_256_the_sync_sends()
    {
        var (_, game, _, server) = ModSettingsFolders();
        var rbm = WriteSetting(server, "RBM", "RBM_v1", "{\"Speed\": 1}");
        // 300 new ids of a real mod: only 255 fit next to RBM_v1. Native is no server mod, so its ids never get in.
        var entries = Enumerable.Range(0, 300).Select(i => ("Global/Combat.Plus/CombatPlus_" + i + ".json", "{\"a\": 1}"))
            .Append(("Global/Native/Native_x.json", "{}")).ToArray();
        var result = ServerModSettings.Import(Zip(Path.Combine(_root, "many.zip"), entries), server, game, ["Native", "SandBox", "DedicatedServer.CoodszTools", "CombatPlus"]);

        Assert.Equal(ServerModSettings.MaxIds - 1, result.Added);
        Assert.Equal(45, result.Skipped.Count(line => line.EndsWith(": the server already has 256 mod settings files, the most players can get, so it was skipped.", StringComparison.Ordinal)));
        Assert.Contains("Global/Native/Native_x.json: the server does not have the mod Native, so it was skipped.", result.Skipped);
        var ids = SettingsFiles.CaptureFromFiles(server, new KeepOwnList()).Select(snapshot => snapshot.SettingsId).ToList();
        Assert.Equal(ServerModSettings.MaxIds, ids.Count);
        Assert.Contains("RBM_v1", ids);
        Assert.True(File.Exists(rbm));
        // A new folder is named after the server's mod, never the spelling that was sent.
        Assert.Equal(new[] { "CombatPlus", "RBM" }, Directory.GetDirectories(Path.Combine(server, "Global")).Select(Path.GetFileName).Order());
    }

    [Fact]
    public void One_new_id_sent_twice_under_two_spellings_is_written_once()
    {
        var (_, game, _, server) = ModSettingsFolders();
        var zip = Zip(Path.Combine(_root, "sent.zip"), ("Global/CombatPlus/CombatPlus_x.json", "{\"a\": 1}"), ("Global/Combat.Plus/CombatPlus_x.json", "{\"a\": 2}"));
        var result = ServerModSettings.Import(zip, server, game, ["CombatPlus"]);
        Assert.Equal(1, result.Added);
        Assert.Equal(new[] { "Global/Combat.Plus/CombatPlus_x.json: the same settings file as Global/CombatPlus/CombatPlus_x.json, so only the first one was taken." }, result.Skipped);
        Assert.Single(Directory.GetFiles(Path.Combine(server, "Global"), "*.json", SearchOption.AllDirectories));
    }

    [Fact]
    public void A_looks_file_named_after_a_gameplay_mod_never_unlocks_it()
    {
        var (_, _, _, server) = ModSettingsFolders();
        var list = ModCompatDatabase.Parse("[{\"id\": \"Fancy\", \"category\": \"UI\"}, {\"id\": \"FancyCombat\", \"category\": \"Gameplay\"}]")!;
        WriteSetting(server, "FancyCombat", "FancyCombat_v1", "{\"Damage\": 2}");
        WriteSetting(server, "Fancy", "FancyCombat", "{\"Damage\": 9}");
        WriteSetting(server, "Fancy", "Fancy_v1", "{\"Big\": true}");
        var files = ServerModSettings.Load(server, list);
        Assert.False(files.Single(file => file.Folder == "Fancy" && file.Id == "FancyCombat").Looks);
        Assert.True(files.Single(file => file.Id == "Fancy_v1").Looks);
    }

    [Fact]
    public void Names_with_hidden_or_look_alike_characters_are_skipped()
    {
        var (_, game, _, server) = ModSettingsFolders();
        var zip = Zip(Path.Combine(_root, "sent.zip"),
            ("Global/RBM/RBM" + (char)0x200B + "_v1.json", "{}"),
            ("Global/RBM/RBM" + (char)0x202E + "_v2.json", "{}"),
            ("Global/R" + (char)0x0412 + "M/RBM_v3.json", "{}"));
        var result = ServerModSettings.Import(zip, server, game, ["RBM"]);
        Assert.Equal(0, result.Added);
        Assert.Equal(3, result.Skipped.Count(line => line.EndsWith(": a file or folder name Windows can't use, so it was skipped.", StringComparison.Ordinal)));
        Assert.All(result.Skipped, line => Assert.DoesNotContain(line, c => c == (char)0x200B || c == (char)0x202E));
    }

    [Fact]
    public void A_recipes_json_that_does_not_read_is_kept_and_an_older_copy_stays()
    {
        var (_, _, _, server) = ModSettingsFolders();
        var gameServer = Path.Combine(_root, "server");
        var module = ServerToolsModule.ModuleFolder(gameServer);
        Directory.CreateDirectory(module);
        var recipes = Path.Combine(module, "recipes.json");
        File.WriteAllText(recipes + ".bak", "older");
        var notes = new List<string>();

        // A key named twice, then a PlayersOwnSettings that is no list: each kept, the first .bak never written over.
        File.WriteAllText(recipes, "{\"Mods\": [], \"Mods\": []}");
        Assert.True(ServerModSettings.WritePlayersOwn(ServerToolsModule.ModuleFolder(gameServer), ServerModSettings.Load(server, ModCompatDatabase.Empty), notes.Add));
        File.WriteAllText(recipes, "{\"PlayersOwnSettings\": \"RBM_v1\"}");
        Assert.True(ServerModSettings.WritePlayersOwn(ServerToolsModule.ModuleFolder(gameServer), ServerModSettings.Load(server, ModCompatDatabase.Empty), notes.Add));
        // The same bytes again make no new copy.
        File.WriteAllText(recipes, "{\"PlayersOwnSettings\": \"RBM_v1\"}");
        Assert.True(ServerModSettings.WritePlayersOwn(ServerToolsModule.ModuleFolder(gameServer), ServerModSettings.Load(server, ModCompatDatabase.Empty), notes.Add));
        Assert.False(File.Exists(recipes + ".bak4"));

        Assert.Equal("older", File.ReadAllText(recipes + ".bak"));
        Assert.Equal("{\"Mods\": [], \"Mods\": []}", File.ReadAllText(recipes + ".bak2"));
        Assert.Equal("{\"PlayersOwnSettings\": \"RBM_v1\"}", File.ReadAllText(recipes + ".bak3"));
        Assert.Equal(new[] { "recipes.json could not be read as a list of mods players keep for themselves, so it was kept as recipes.json.bak2 and written again.",
            "recipes.json could not be read as a list of mods players keep for themselves, so it was kept as recipes.json.bak3 and written again.",
            "recipes.json could not be read as a list of mods players keep for themselves, so it was kept as recipes.json.bak3 and written again." }, notes);
        Assert.Equal(0, SettingsFiles.ReadKeepOwn(recipes).Count);
    }

    [Fact]
    public void The_512_value_cap_counts_what_the_sync_sends_with_its_own_rules()
    {
        var (_, game, _, server) = ModSettingsFolders();
        static string Values(int gameplay, string extra) =>
            "{" + extra + string.Join(", ", Enumerable.Range(0, gameplay).Select(i => "\"v" + i + "\": " + i)) + "}";
        // One hotkey and 512 gameplay values: the sync sends 512, so it goes in. 513 gameplay values do not.
        var zip = Zip(Path.Combine(_root, "sent.zip"),
            ("Global/RBM/RBM_fits.json", Values(ServerModSettings.MaxValues, "\"OpenMenuHotkey\": 5, ")),
            ("Global/RBM/RBM_over.json", Values(ServerModSettings.MaxValues + 1, "")));
        var result = ServerModSettings.Import(zip, server, game, ["RBM"]);
        Assert.Equal(1, result.Added);
        Assert.Equal(new[] { "Global/RBM/RBM_over.json: more than 512 values, more than players can get, so it was skipped." }, result.Skipped);

        // The page counts the same values the sync sends, file by file.
        WriteSetting(server, "Mixed", "Mixed_v1", Values(600, "\"CameraZoom\": 1.5, \"WebhookUrl\": \"x\", \"List\": [1], \"Long\": \"" + new string('a', 600) + "\", \"Huge\": 1e400, "));
        foreach (var file in ServerModSettings.Load(server, ModCompatDatabase.Empty))
        {
            var sent = SettingsFiles.CaptureFromFiles(server, new KeepOwnList()).Single(snapshot => snapshot.SettingsId == file.Id).Values;
            // The very names the sync sends, not only as many.
            Assert.Equal(sent.Select(line => SettingsWire.Decode(line)!.Id).Order(StringComparer.Ordinal), file.Synced.Order(StringComparer.Ordinal));
        }
        var mixed = ServerModSettings.Load(server, ModCompatDatabase.Empty).Single(file => file.Id == "Mixed_v1");
        Assert.True(mixed.More);
        Assert.DoesNotContain("CameraZoom", mixed.Synced);
        Assert.DoesNotContain("WebhookUrl", mixed.Synced);
        Assert.False(ServerModSettings.Load(server, ModCompatDatabase.Empty).Single(file => file.Id == "RBM_fits").More);
    }

    [Fact]
    public void A_new_id_belongs_to_the_mod_with_the_longest_name_it_starts_with()
    {
        var (_, game, _, server) = ModSettingsFolders();
        var zip = Zip(Path.Combine(_root, "sent.zip"), ("Global/Fancy/FancyCombat_v1.json", "{\"Damage\": 9}"), ("Global/Fancy/Fancy_v2.json", "{\"Big\": true}"));
        var result = ServerModSettings.Import(zip, server, game, ["Fancy", "FancyCombat"]);
        Assert.Equal(1, result.Added);
        Assert.Equal(new[] { "Global/Fancy/FancyCombat_v1.json: FancyCombat_v1 belongs to the mod FancyCombat, not Fancy, so it was skipped." }, result.Skipped);
        Assert.True(File.Exists(Path.Combine(server, "Global", "Fancy", "Fancy_v2.json")));
    }

    [Fact]
    public void An_import_never_crowds_out_the_hosts_own_files_seeded_later()
    {
        var (_, game, _, server) = ModSettingsFolders();
        WriteSetting(server, "RBM", "RBM_v1", "{\"a\": 1}");
        foreach (var i in Enumerable.Range(0, 200))
            WriteSetting(game, "Host", "Host_" + i, "{\"a\": 1}");
        var entries = Enumerable.Range(0, 100).Select(i => ("Global/CombatPlus/CombatPlus_" + i + ".json", "{\"a\": 1}"))
            .Append(("Global/SandBox/SandBox_x.json", "{}")).ToArray();
        var result = ServerModSettings.Import(Zip(Path.Combine(_root, "many.zip"), entries), server, game, ["CombatPlus", "Sand Box"]);

        // 256 less RBM_v1 and the host's 200: 55 fit. "Sand Box" is the game's own SandBox, so its ids never get in.
        Assert.Equal(55, result.Added);
        Assert.Contains("Global/SandBox/SandBox_x.json: the server does not have the mod SandBox, so it was skipped.", result.Skipped);
        ServerModSettings.Seed(server, game);
        Assert.Equal(ServerModSettings.MaxIds, SettingsFiles.CaptureFromFiles(server, new KeepOwnList()).Count);
        Assert.Contains("Host_199", SettingsFiles.CaptureFromFiles(server, new KeepOwnList()).Select(snapshot => snapshot.SettingsId));
    }

    [Fact]
    public void Lines_drop_hidden_separators_and_the_256_warning_has_its_own_line()
    {
        Assert.Equal("abc d", ServerModSettings.Shown("a" + (char)0x2028 + "b" + (char)0xA0 + "c" + char.ConvertFromUtf32(0x1F600) + (char)0x2029 + " d"));

        var window = SourceFiles.Window(SourceFiles.Root(), "RealmOfThronesHost");
        var show = SourceFiles.Method(window, "private void ShowModSettings(string? keep = null)");
        Assert.Contains("ModSettingsWarning.Text = ids > ServerModSettings.MaxIds ?", show);
        Assert.DoesNotContain("ModSettingsStatus.Text = \"The server has", show);
        var rot = XDocument.Parse(SourceFiles.Read(@"src\RealmOfThronesHost\MainWindow.xaml"));
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        Assert.Single(rot.Descendants(), element => (string?)element.Attribute(x + "Name") == "ModSettingsWarning");
    }

    [Fact]
    public void This_PCs_copy_is_found_by_settings_id_when_the_folders_are_named_differently()
    {
        var (_, game, _, server) = ModSettingsFolders();
        var pc = WriteSetting(game, "RBM", "RBM_v1", "{\"Speed\": 1}");
        // The server keeps RBM_v1 in its own folder for the mod, imported with other values.
        var own = WriteSetting(server, "Bannerlord.RBM", "RBM_v1", "{\"Speed\": 1}");
        Assert.Null(ServerModSettings.Save(server, own, new Dictionary<string, string> { ["Speed"] = "7" }));

        Assert.True(ServerModSettings.HasPcCopy(server, game, own));
        // While the server runs a single reset still puts this PC's values in, and no second file comes.
        var kept = new List<string>();
        Assert.Equal(1, ServerModSettings.Reset(server, game, own, serverRunning: true, kept: kept));
        Assert.Empty(kept);
        Assert.Equal("{\"Speed\": 1}", File.ReadAllText(own));
        Assert.Single(Directory.GetFiles(Path.Combine(server, "Global"), "*.json", SearchOption.AllDirectories));

        // Seed refreshes it from this PC across the two folder names; all mods put this PC's values over an import.
        File.WriteAllText(pc, "{\"Speed\": 2}");
        Assert.Equal(1, ServerModSettings.Seed(server, game));
        Assert.Equal("{\"Speed\": 2}", File.ReadAllText(own));
        Assert.Null(ServerModSettings.Save(server, own, new Dictionary<string, string> { ["Speed"] = "9" }));
        Assert.Equal(1, ServerModSettings.Reset(server, game));
        Assert.Equal("{\"Speed\": 2}", File.ReadAllText(own));
        Assert.Single(Directory.GetFiles(Path.Combine(server, "Global"), "*.json", SearchOption.AllDirectories));
    }

    [Fact]
    public void Two_ids_that_differ_only_in_case_stay_two_like_the_sync_sees_them()
    {
        var (_, game, _, server) = ModSettingsFolders();
        WriteSetting(game, "A", "Combat_v1", "{\"Speed\": 1}");
        WriteSetting(game, "B", "combat_v1", "{\"Speed\": 2}");
        var upper = WriteSetting(server, "A", "Combat_v1", "{\"Speed\": 8}");
        var lower = WriteSetting(server, "B", "combat_v1", "{\"Speed\": 9}");
        Assert.Equal(new[] { "Combat_v1", "combat_v1" }, SettingsFiles.CaptureFromFiles(server, new KeepOwnList()).Select(snapshot => snapshot.SettingsId).Order(StringComparer.Ordinal));

        // Each gets its own copy from this PC, never the other one's.
        Assert.Equal(2, ServerModSettings.Reset(server, game));
        Assert.Equal("{\"Speed\": 1}", File.ReadAllText(upper));
        Assert.Equal("{\"Speed\": 2}", File.ReadAllText(lower));
        Assert.Equal(1, ServerModSettings.Reset(server, game, lower));
        Assert.Equal("{\"Speed\": 2}", File.ReadAllText(lower));
        Assert.Equal("{\"Speed\": 1}", File.ReadAllText(upper));
    }

    [Fact]
    public void Seed_skips_an_id_that_differs_only_in_capital_letters_from_the_servers()
    {
        var (_, game, _, server) = ModSettingsFolders();
        WriteSetting(game, "A", "Combat_v1", "{\"Speed\": 1}");
        WriteSetting(game, "B", "combat_v1", "{\"Speed\": 2}");
        WriteSetting(game, "C", "Other_v1", "{\"x\": 1}");
        WriteSetting(game, "D", "other_V1", "{\"x\": 2}");
        var own = WriteSetting(server, "A", "Combat_v1", "{\"Speed\": 7}");
        Assert.Null(ServerModSettings.Save(server, own, new Dictionary<string, string> { ["Speed"] = "7" }));

        var skipped = new List<string>();
        Assert.Equal(1, ServerModSettings.Seed(server, game, skipped: skipped));
        // The twin of the server's id and the twin of one taken this run, one line each; Combat_v1 keeps its value.
        Assert.Equal(new[] { "combat_v1 differs only in capital letters from Combat_v1, so it was skipped.", "other_V1 differs only in capital letters from Other_v1, so it was skipped." }, skipped);
        Assert.Contains("\"Speed\": 7", File.ReadAllText(own));
        Assert.Equal(new[] { "Combat_v1", "Other_v1" }, SettingsFiles.CaptureFromFiles(server, new KeepOwnList()).Select(snapshot => snapshot.SettingsId).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void This_PCs_copy_across_case_is_used_only_when_it_is_the_one_candidate()
    {
        var (_, game, _, server) = ModSettingsFolders();
        WriteSetting(game, "RBM", "Combat_v1", "{\"Speed\": 1}");
        var own = WriteSetting(server, "X", "combat_v1", "{\"Speed\": 5}");
        Assert.True(ServerModSettings.HasPcCopy(server, game, own));
        Assert.Equal(1, ServerModSettings.Reset(server, game, own, serverRunning: true));
        Assert.Equal("{\"Speed\": 1}", File.ReadAllText(own));

        // Two candidates that differ only in case: no guess, so the server's file counts as having no copy here.
        WriteSetting(game, "Other", "COMBAT_v1", "{\"Speed\": 2}");
        Assert.False(ServerModSettings.HasPcCopy(server, game, own));
        var kept = new List<string>();
        Assert.Equal(0, ServerModSettings.Reset(server, game, own, serverRunning: true, kept: kept));
        Assert.Equal(new[] { "X (combat_v1)" }, kept);
        Assert.Equal("{\"Speed\": 1}", File.ReadAllText(own));
    }

    [Fact]
    public void The_256_cap_counts_case_twins_as_two_ids_like_the_sync()
    {
        var (_, game, _, server) = ModSettingsFolders();
        foreach (var i in Enumerable.Range(0, 253))
            WriteSetting(server, "Host", "Host_" + i, "{\"a\": 1}");
        WriteSetting(server, "A", "Combat_v1", "{\"a\": 1}");
        WriteSetting(server, "B", "combat_v1", "{\"a\": 2}");
        // 255 ids the sync sends, the twins as two: one more fits, the next does not.
        Assert.Equal(255, SettingsFiles.CaptureFromFiles(server, new KeepOwnList()).Count);
        var zip = Zip(Path.Combine(_root, "sent.zip"), ("Global/CombatPlus/CombatPlus_1.json", "{\"a\": 1}"), ("Global/CombatPlus/CombatPlus_2.json", "{\"a\": 1}"));
        var result = ServerModSettings.Import(zip, server, game, ["CombatPlus"]);
        Assert.Equal(1, result.Added);
        Assert.Equal(new[] { "Global/CombatPlus/CombatPlus_2.json: the server already has 256 mod settings files, the most players can get, so it was skipped." }, result.Skipped);
        Assert.Equal(ServerModSettings.MaxIds, SettingsFiles.CaptureFromFiles(server, new KeepOwnList()).Count);
        // The page counts them the same way: 256 ids, no warning yet.
        Assert.Equal(ServerModSettings.MaxIds, ServerModSettings.Load(server, ModCompatDatabase.Empty).Select(file => file.Id).Distinct(StringComparer.Ordinal).Count());
        var show = SourceFiles.Method(SourceFiles.Window(SourceFiles.Root(), "RealmOfThronesHost"), "private void ShowModSettings(string? keep = null)");
        Assert.Contains("var ids = _modFiles.Select(file => file.Id).Distinct(StringComparer.Ordinal).Count();", show);
        Assert.Contains("ids > ServerModSettings.MaxIds", show);
    }

    [Fact]
    public void An_import_never_replaces_a_server_file_by_its_case_twin_even_when_this_PC_has_the_twin()
    {
        var (_, game, _, server) = ModSettingsFolders();
        var own = WriteSetting(server, "RBM", "CombatAI", "{\"On\": true}");
        WriteSetting(game, "RBM", "combatai", "{\"On\": false}");
        var zip = Zip(Path.Combine(_root, "sent.zip"), ("Global/RBM/combatai.json", "{\"On\": false}"));

        var result = ServerModSettings.Import(zip, server, game, ["RBM"]);
        Assert.Equal(0, result.Added);
        Assert.Equal(new[] { "Global/RBM/combatai.json: combatai differs only in capital letters from CombatAI, so it was skipped." }, result.Skipped);
        Assert.Equal("{\"On\": true}", File.ReadAllText(own));
        Assert.Equal(new[] { "CombatAI.json" }, Directory.GetFiles(Path.Combine(server, "Global", "RBM")).Select(Path.GetFileName));
    }

    [Fact]
    public void This_PCs_file_counts_for_an_import_only_under_its_exact_name_in_the_folder_sent()
    {
        var (_, game, _, server) = ModSettingsFolders();
        // This PC has Combat_v1 in another folder and only its case twin in RBM.
        WriteSetting(game, "Other", "Combat_v1", "{\"a\": 1}");
        WriteSetting(game, "RBM", "combat_v1", "{\"a\": 2}");
        var zip = Zip(Path.Combine(_root, "sent.zip"), ("Global/RBM/Combat_v1.json", "{\"a\": 3}"));

        var result = ServerModSettings.Import(zip, server, game, ["RBM"]);
        Assert.Equal(0, result.Added);
        Assert.Equal(new[] { "Global/RBM/Combat_v1.json: Combat_v1 is not a settings file of the mod RBM, so it was skipped." }, result.Skipped);
        Assert.False(Directory.Exists(Path.Combine(server, "Global")));
    }
}
