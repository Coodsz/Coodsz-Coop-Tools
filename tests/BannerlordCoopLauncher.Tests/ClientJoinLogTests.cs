using BannerlordCoopLauncher.Core.Launch;

namespace BannerlordCoopLauncher.Tests;

// Coop 0.1.7 left Coop_client.log empty for players (Coop crash reports #69 and #72, live join 2026-10-31), so the
// join watch saw no result and the pill stayed on Joining while the player was in game. The watch now reads
// Coop_client_<pid>.log too, and says Game open when both logs stay empty.
public sealed class ClientJoinLogTests : IDisposable
{
    private const int Pid = 4242;
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ClientJoinLogTests-" + Guid.NewGuid().ToString("N"));
    private readonly string _bin;

    public ClientJoinLogTests()
    {
        _bin = Path.Combine(_root, "bin", "Win64_Shipping_Client");
        Directory.CreateDirectory(_bin);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
    }

    [Fact]
    public void Both_logs_sit_in_the_client_bin_folder()
    {
        Assert.Equal(Path.Combine(_bin, "Coop_client.log"), ClientJoinLog.PathIn(_root));
        Assert.Equal(Path.Combine(_bin, "Coop_client_4242.log"), ClientJoinLog.PathIn(_root, Pid));
        var log = new ClientJoinLog(_root, Pid);
        Assert.Equal(ClientJoinLog.PathIn(_root), log.MainPath);
        Assert.Equal(ClientJoinLog.PathIn(_root, Pid), log.ProcessPath);
    }

    [Fact]
    public void The_process_log_gives_the_join_result_when_the_main_log_stays_empty()
    {
        File.WriteAllText(Path.Combine(_bin, "Coop_client.log"), "");
        var log = new ClientJoinLog(_root, Pid);
        Assert.Equal("", log.Read());
        Assert.False(log.GotText);

        File.WriteAllText(Path.Combine(_bin, "Coop_client_4242.log"), "[INF] Connected! Please wait for transfer\n");
        var text = log.Read();
        Assert.True(log.GotText);
        Assert.True(JoinResultExplain.Succeeded(text));
        // Another game's log is not read.
        File.WriteAllText(Path.Combine(_bin, "Coop_client_9999.log"), "[ERR] other game");
        Assert.DoesNotContain("other game", log.Read());
    }

    [Fact]
    public void The_main_log_still_counts_and_text_from_before_the_start_is_skipped()
    {
        var main = Path.Combine(_bin, "Coop_client.log");
        File.WriteAllText(main, "[INF] Joining Coop Campaign from the last session\n");
        var log = new ClientJoinLog(_root, Pid);
        Assert.Equal("", log.Read());
        Assert.False(log.GotText);

        File.AppendAllText(main, "[INF] Sending your character to the host\n");
        Assert.Equal("[INF] Sending your character to the host\n", log.Read());

        // Coop deletes the log when it starts, so a shorter file is read from the top.
        File.WriteAllText(main, "new\n");
        Assert.EndsWith("new\n", log.Read());
        Assert.True(log.Bytes > 0);
    }

    [Fact]
    public void A_log_Coop_holds_while_it_makes_it_again_is_skipped_for_that_poll()
    {
        var main = Path.Combine(_bin, "Coop_client.log");
        File.WriteAllText(main, "");
        var log = new ClientJoinLog(_root, Pid);
        File.WriteAllText(main, "[INF] Connected! Please wait for transfer\n");
        using (new FileStream(main, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            Assert.Equal("", log.Read());

        // The next poll reads it.
        Assert.True(JoinResultExplain.Succeeded(log.Read()));
    }

    [Fact]
    public void Empty_logs_with_the_game_open_say_Game_open()
    {
        File.WriteAllText(Path.Combine(_bin, "Coop_client.log"), "");
        File.WriteAllText(Path.Combine(_bin, "Coop_client_4242.log"), "");
        var log = new ClientJoinLog(_root, Pid);
        log.Read();
        Assert.Equal(ClientNextStep.GameOpen, log.TimedOutState(gameRunning: true));
        Assert.Equal("Game open", ClientNextStep.PillText(true, log.TimedOutState(gameRunning: true)));
    }

    [Fact]
    public void Text_without_a_result_keeps_Joining()
    {
        var log = new ClientJoinLog(_root, Pid);
        File.WriteAllText(Path.Combine(_bin, "Coop_client_4242.log"), "[INF] Coop loading\n");
        var text = log.Read();
        Assert.False(JoinResultExplain.Succeeded(text));
        Assert.Equal(ClientNextStep.Joining, log.TimedOutState(gameRunning: true));
    }

    [Theory]
    [InlineData(true, false, ClientNextStep.GameOpen)]
    [InlineData(true, true, ClientNextStep.Joining)]
    [InlineData(false, false, ClientNextStep.Joining)]
    [InlineData(false, true, ClientNextStep.Joining)]
    public void Only_an_open_game_with_no_log_text_says_Game_open(bool gameRunning, bool gotText, string expected) =>
        Assert.Equal(expected, ClientJoinLog.TimedOutState(gameRunning, gotText));

    [Theory]
    [InlineData("BannerlordCoopLauncher")]
    [InlineData("AD1259Launcher")]
    [InlineData("TaomCoopLauncher")]
    [InlineData("BurningEmpiresLauncher")]
    public void Every_client_launcher_watches_both_logs_and_shows_Game_open(string project)
    {
        var window = SourceFiles.Window(SourceFiles.Root(), project);
        var watch = SourceFiles.Method(window, "private async Task WatchJoinAsync(");
        Assert.Contains("new ClientJoinLog(gameRoot, started.Id)", watch);
        Assert.Contains("joinLog.Read()", watch);
        Assert.DoesNotContain("Coop_client.log", watch);
        Assert.Contains("joinLog.TimedOutState(ClientJoinLog.StillRunning(started))", watch);
        Assert.Contains("ClientJoinLog.EmptyLogNote", watch);
        // The note and Game open come after the watch loop, never inside it.
        Assert.True(watch.IndexOf("ClientJoinLog.EmptyLogNote", StringComparison.Ordinal) > watch.LastIndexOf("await Task.Delay(2000);", StringComparison.Ordinal));
        // Game open has the neutral Joining look, not In game's or Refused's.
        Assert.Contains("ClientNextStep.GameOpen => Coodsz.Ui.StatusKind.Joining,", window);
    }
}
