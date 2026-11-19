using BannerlordCoopLauncher.Core.Support;
using TaomCoop.Core;

namespace BannerlordCoopLauncher.Tests;

public class ConsoleQueueTests
{
    [Fact]
    public void A_command_nobody_reads_is_removed_so_it_does_not_run_on_the_next_start()
    {
        var root = Path.Combine(Path.GetTempPath(), "console-queue-" + Guid.NewGuid().ToString("N"));
        try
        {
            var reply = ConsoleQueue.Send(new[] { root }, "stop", TimeSpan.FromMilliseconds(300));

            Assert.False(reply.PickedUp);
            Assert.False(reply.Answered);
            Assert.Equal(ConsoleQueue.NotPickedUp, reply.Text);
            Assert.Empty(Directory.GetFiles(Path.Combine(root, "console-queue")));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void The_server_answer_is_returned_without_the_ok_prefix()
    {
        var root = Path.Combine(Path.GetTempPath(), "console-queue-" + Guid.NewGuid().ToString("N"));
        var queue = Path.Combine(root, "console-queue");
        var results = Path.Combine(root, "console-results");
        using var stop = new CancellationTokenSource();
        var server = Task.Run(async () =>
        {
            while (!stop.IsCancellationRequested)
            {
                if (Directory.Exists(queue))
                {
                    foreach (var command in Directory.GetFiles(queue, "*.cmd"))
                    {
                        var text = File.ReadAllText(command);
                        File.Delete(command);
                        File.WriteAllText(Path.Combine(results, Path.GetFileNameWithoutExtension(command) + ".result"), "OK|ran " + text);
                        return;
                    }
                }

                await Task.Delay(20);
            }
        });
        try
        {
            var reply = ConsoleQueue.Send(new[] { root }, "  coop.debug.players.list  ", TimeSpan.FromSeconds(5));

            Assert.True(reply.PickedUp);
            Assert.True(reply.Answered);
            Assert.Equal("ran coop.debug.players.list", reply.Text);
            Assert.Empty(Directory.GetFiles(results));
        }
        finally
        {
            stop.Cancel();
            server.Wait(TimeSpan.FromSeconds(2));
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private static Task FakeServer(string root, string answerPrefix, CancellationToken stop, bool emptyFirst = false) => Task.Run(async () =>
    {
        var queue = Path.Combine(root, "console-queue");
        var results = Path.Combine(root, "console-results");
        while (!stop.IsCancellationRequested)
        {
            if (Directory.Exists(queue))
            {
                foreach (var command in Directory.GetFiles(queue, "*.cmd"))
                {
                    var text = File.ReadAllText(command);
                    File.Delete(command);
                    var result = Path.Combine(results, Path.GetFileNameWithoutExtension(command) + ".result");
                    if (emptyFirst)
                    {
                        // The moment a real server has created the result but not written it yet.
                        File.WriteAllText(result, "");
                        await Task.Delay(400);
                    }

                    File.WriteAllText(result, answerPrefix + text);
                    return;
                }
            }

            await Task.Delay(20);
        }
    });

    [Fact]
    public void A_refused_command_is_reported_as_failed()
    {
        var root = Path.Combine(Path.GetTempPath(), "console-queue-" + Guid.NewGuid().ToString("N"));
        using var stop = new CancellationTokenSource();
        var server = FakeServer(root, "ERROR|unknown command ", stop.Token);
        try
        {
            var reply = ConsoleQueue.Send(new[] { root }, "launcher.server.save", TimeSpan.FromSeconds(5));

            Assert.True(reply.PickedUp);
            Assert.True(reply.Answered);
            Assert.True(reply.Failed);
            Assert.False(reply.Succeeded);
            Assert.Equal("unknown command launcher.server.save", reply.Text);
        }
        finally
        {
            stop.Cancel();
            server.Wait(TimeSpan.FromSeconds(2));
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void A_result_read_before_the_server_filled_it_waits_for_the_answer()
    {
        var root = Path.Combine(Path.GetTempPath(), "console-queue-" + Guid.NewGuid().ToString("N"));
        using var stop = new CancellationTokenSource();
        var server = FakeServer(root, "ERROR|unknown command ", stop.Token, emptyFirst: true);
        try
        {
            var reply = ConsoleQueue.Send(new[] { root }, "launcher.server.save", TimeSpan.FromSeconds(5));

            Assert.True(reply.Answered);
            Assert.True(reply.Failed);
            Assert.Equal("unknown command launcher.server.save", reply.Text);
        }
        finally
        {
            stop.Cancel();
            server.Wait(TimeSpan.FromSeconds(2));
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void With_two_roots_the_answering_one_wins_and_the_other_command_is_removed()
    {
        var idle = Path.Combine(Path.GetTempPath(), "console-queue-" + Guid.NewGuid().ToString("N"));
        var live = Path.Combine(Path.GetTempPath(), "console-queue-" + Guid.NewGuid().ToString("N"));
        using var stop = new CancellationTokenSource();
        var server = FakeServer(live, "OK|", stop.Token);
        try
        {
            // The same root twice is only written once.
            var reply = ConsoleQueue.Send(new[] { idle, live, live + Path.DirectorySeparatorChar }, "coop.debug.players.list", TimeSpan.FromSeconds(5));

            Assert.True(reply.Succeeded);
            Assert.Equal("coop.debug.players.list", reply.Text);
            Assert.Empty(Directory.GetFiles(Path.Combine(idle, "console-queue")));
            Assert.Empty(Directory.GetFiles(Path.Combine(live, "console-queue")));
        }
        finally
        {
            stop.Cancel();
            server.Wait(TimeSpan.FromSeconds(2));
            foreach (var root in new[] { idle, live })
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void Old_command_files_are_cleared_before_a_new_send()
    {
        var root = Path.Combine(Path.GetTempPath(), "console-queue-" + Guid.NewGuid().ToString("N"));
        var queue = Path.Combine(root, "console-queue");
        Directory.CreateDirectory(queue);
        var stale = Path.Combine(queue, Guid.NewGuid().ToString("N") + ".cmd");
        var fresh = Path.Combine(queue, Guid.NewGuid().ToString("N") + ".cmd");
        File.WriteAllText(stale, "stop");
        File.WriteAllText(fresh, "coop.debug.players.list");
        File.SetLastWriteTimeUtc(stale, DateTime.UtcNow.AddMinutes(-10));
        try
        {
            Assert.Equal(1, ConsoleQueue.RemoveStale(queue));
            Assert.False(File.Exists(stale));
            Assert.True(File.Exists(fresh));

            var reply = ConsoleQueue.Send(new[] { root }, "coop.debug.players.list", TimeSpan.FromMilliseconds(200));
            Assert.False(reply.PickedUp);
            Assert.Equal(queue, Assert.Single(reply.Folders));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Clear_pending_removes_every_command_file_and_leaves_the_rest()
    {
        var queue = Path.Combine(Path.GetTempPath(), "console-queue-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(queue);
        try
        {
            // A fresh one too: a tool closed mid wait leaves a file younger than StaleAfter.
            var fresh = Path.Combine(queue, Guid.NewGuid().ToString("N") + ".cmd");
            var old = Path.Combine(queue, Guid.NewGuid().ToString("N") + ".cmd");
            var temporary = Path.Combine(queue, Guid.NewGuid().ToString("N") + ".tmp");
            var other = Path.Combine(queue, "notes.txt");
            File.WriteAllText(fresh, "launcher.time.set Play");
            File.WriteAllText(old, "stop");
            File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddMinutes(-10));
            File.WriteAllText(temporary, "x");
            File.WriteAllText(other, "x");

            var cleared = ConsoleQueue.ClearPending(queue);
            Assert.Equal(2, cleared.Removed);
            Assert.Empty(cleared.Locked);
            Assert.False(File.Exists(fresh));
            Assert.False(File.Exists(old));
            Assert.True(File.Exists(temporary));
            Assert.True(File.Exists(other));
        }
        finally
        {
            Directory.Delete(queue, recursive: true);
        }
    }

    [Fact]
    public void Clear_pending_never_throws_on_a_missing_folder_or_a_locked_file()
    {
        Assert.Equal(0, ConsoleQueue.ClearPending(Path.Combine(Path.GetTempPath(), "console-queue-" + Guid.NewGuid().ToString("N"))).Removed);

        var queue = Path.Combine(Path.GetTempPath(), "console-queue-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(queue);
        var locked = Path.Combine(queue, "a.cmd");
        var free = Path.Combine(queue, "b.cmd");
        File.WriteAllText(locked, "stop");
        File.WriteAllText(free, "stop");
        try
        {
            (int Removed, IReadOnlyList<string> Locked) cleared;
            using (new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None))
                cleared = ConsoleQueue.ClearPending(queue);
            Assert.Equal(1, cleared.Removed);
            // Still locked after the second try, so the host can name it.
            Assert.Equal(locked, Assert.Single(cleared.Locked));
            Assert.True(File.Exists(locked));
            Assert.False(File.Exists(free));
        }
        finally
        {
            Directory.Delete(queue, recursive: true);
        }
    }

    [Fact]
    public void A_locked_command_file_freed_before_the_second_try_is_removed()
    {
        var queue = Path.Combine(Path.GetTempPath(), "console-queue-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(queue);
        var path = Path.Combine(queue, "a.cmd");
        File.WriteAllText(path, "stop");
        try
        {
            // The lock goes exactly between the first failed delete and the second try.
            using var holder = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
            var retried = new List<string>();
            var cleared = ConsoleQueue.ClearPending(queue, locked => { Assert.True(File.Exists(locked)); retried.Add(locked); holder.Dispose(); });
            Assert.Equal(path, Assert.Single(retried));
            Assert.Equal(1, cleared.Removed);
            Assert.Empty(cleared.Locked);
        }
        finally
        {
            Directory.Delete(queue, recursive: true);
        }
    }

    [Fact]
    public void A_send_whose_command_file_stayed_locked_keeps_its_usual_answer()
    {
        var root = Path.Combine(Path.GetTempPath(), "console-queue-" + Guid.NewGuid().ToString("N"));
        var queue = Path.Combine(root, "console-queue");
        try
        {
            var sending = Task.Run(() => ConsoleQueue.Send(new[] { root }, "coop.debug.players.list", TimeSpan.FromMilliseconds(1500)));
            var waited = System.Diagnostics.Stopwatch.StartNew();
            while (!(Directory.Exists(queue) && Directory.GetFiles(queue, "*.cmd").Length > 0) && waited.Elapsed < TimeSpan.FromSeconds(5))
                Thread.Sleep(10);
            var path = Assert.Single(Directory.GetFiles(queue, "*.cmd"));

            (int Removed, IReadOnlyList<string> Locked) cleared;
            using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                cleared = ConsoleQueue.ClearPending(queue, _ => { });
            Assert.Equal(path, Assert.Single(cleared.Locked));

            // Not "not sent": the file is still there, so nobody took it and nobody cleared it.
            var reply = sending.Result;
            Assert.NotEqual(ConsoleQueue.Cleared, reply.Text);
            Assert.False(reply.PickedUp);
            Assert.Equal(ConsoleQueue.NotPickedUp, reply.Text);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void A_send_whose_command_a_server_start_cleared_says_it_was_not_sent()
    {
        var root = Path.Combine(Path.GetTempPath(), "console-queue-" + Guid.NewGuid().ToString("N"));
        var queue = Path.Combine(root, "console-queue");
        try
        {
            var sending = Task.Run(() => ConsoleQueue.Send(new[] { root }, "coop.debug.players.list", TimeSpan.FromSeconds(10)));
            var waited = System.Diagnostics.Stopwatch.StartNew();
            while (!(Directory.Exists(queue) && Directory.GetFiles(queue, "*.cmd").Length > 0) && waited.Elapsed < TimeSpan.FromSeconds(5))
                Thread.Sleep(10);

            Assert.Equal(1, ConsoleQueue.ClearPending(queue).Removed);
            var reply = sending.Result;

            // Not "Sent. The server took the command": the file went because the start threw it away.
            Assert.False(reply.PickedUp);
            Assert.True(reply.Failed);
            Assert.Equal(ConsoleQueue.Cleared, reply.Text);
            Assert.True(reply.WasCleared);
            Assert.True(waited.Elapsed < TimeSpan.FromSeconds(8));

            // Through the same shaping SendDetailed gives it: Explain adds where it was put, and the flag stays.
            var explained = new CoodszHost.Services.ServerSession().Explain(reply);
            Assert.StartsWith(ConsoleQueue.Cleared + " Command folder: ", explained.Text);
            Assert.True(explained.WasCleared);
            // So Save never falls back to a console that may be the next run's, and stop and say count it as not taken.
            var writes = 0;
            Assert.True(CoodszHost.Services.ServerSession.SaveReply(() => explained, () => { writes++; return ConsoleWrite.Sent; }, () => true).WasCleared);
            Assert.Equal(0, writes);
            Assert.False(CoodszHost.Services.ServerSession.StopAsked(() => ConsoleWrite.NotSent, () => explained).Asked);
            Assert.Equal(ConsoleWrite.NotSent, CoodszHost.Services.ServerSession.SayResult(() => ConsoleWrite.NotSent, () => explained));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Coodsz_host_clears_the_queue_before_it_launches_the_server()
    {
        var session = SourceFiles.Read(Path.Combine("src", "CoodszHost", "Services", "ServerSession.cs"));
        var start = session.IndexOf("public void Start(string executable", StringComparison.Ordinal);
        var clear = session.IndexOf("ConsoleQueue.ClearPending(Path.Combine(folder, \"server-data\", \"console-queue\")).Locked", StringComparison.Ordinal);
        var launch = session.IndexOf("started = process.Start();", StringComparison.Ordinal);
        Assert.True(start > 0 && clear > start && clear < launch);
        // A file that stays locked is named in the host log, and the start goes on.
        Assert.Contains("WriteConsole(\"[CoodszHost] A waiting command file could not be removed before the start, so the server may still run it: \" + locked);", session);
        // The run moves and the old process goes in one step, before the clear: nothing from before Start reaches a console.
        var moved = session.IndexOf("run = ++_run;\r\n            _process = null;", start, StringComparison.Ordinal);
        Assert.True(moved > start && moved < clear);
    }

    [Fact]
    public void A_clear_that_throws_between_its_tries_leaves_the_waiting_send_its_usual_answer()
    {
        var root = Path.Combine(Path.GetTempPath(), "console-queue-" + Guid.NewGuid().ToString("N"));
        var queue = Path.Combine(root, "console-queue");
        try
        {
            var sending = Task.Run(() => ConsoleQueue.Send(new[] { root }, "coop.debug.players.list", TimeSpan.FromMilliseconds(1500)));
            var waited = System.Diagnostics.Stopwatch.StartNew();
            while (!(Directory.Exists(queue) && Directory.GetFiles(queue, "*.cmd").Length > 0) && waited.Elapsed < TimeSpan.FromSeconds(5))
                Thread.Sleep(10);
            var path = Assert.Single(Directory.GetFiles(queue, "*.cmd"));

            using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                ConsoleQueue.ClearPending(queue, _ => throw new IOException("The disk went away."));

            // Not left Clearing: the sender answers at its own deadline, with its usual words.
            var reply = sending.Result;
            Assert.False(reply.WasCleared);
            Assert.Equal(ConsoleQueue.NotPickedUp, reply.Text);
            Assert.True(waited.Elapsed < TimeSpan.FromSeconds(3));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private sealed class RecordingWriter : TextWriter
    {
        public readonly List<string> Calls = new();
        public override System.Text.Encoding Encoding => System.Text.Encoding.UTF8;
        public override void WriteLine(string? value) => Calls.Add("line " + value);
        public override void Flush() => Calls.Add("flush");
    }

    // The bounded write itself (not sent, may still land) is tested in ServerCommandQueueTests.
    [Fact]
    public void A_console_line_is_written_then_flushed_and_lines_from_two_callers_never_mix()
    {
        var recorded = new RecordingWriter();
        Assert.Equal(ConsoleWrite.Sent, ServerCommandQueue.TryWriteWithin(new object(), TimeSpan.FromSeconds(5), () => { recorded.WriteLine("save"); recorded.Flush(); }));
        Assert.Equal(new[] { "line save", "flush" }, recorded.Calls);

        // Each caller writes its line a letter at a time; under the one lock every line comes out whole.
        var gate = new object();
        var output = new System.Text.StringBuilder();
        var lines = Enumerable.Range(0, 8).Select(i => "say line " + i).ToList();
        var writers = lines.Select(line => Task.Run(() => ServerCommandQueue.TryWriteWithin(gate, TimeSpan.FromSeconds(10), () =>
        {
            foreach (var letter in line)
            {
                output.Append(letter);
                Thread.Sleep(1);
            }

            output.Append('\n');
        }))).ToArray();
        Task.WaitAll(writers);

        Assert.All(writers, writer => Assert.Equal(ConsoleWrite.Sent, writer.Result));
        Assert.Equal(lines.OrderBy(line => line), output.ToString().TrimEnd('\n').Split('\n').OrderBy(line => line));

        var session = SourceFiles.Read(Path.Combine("src", "CoodszHost", "Services", "ServerSession.cs"));
        Assert.Contains("ServerCommandQueue.WriteBounded(_stdinLock, ServerCommandQueue.StdinLockWait, () => { stdin.WriteLine(line); stdin.Flush(); })", session);
        Assert.DoesNotContain("process.StandardInput.WriteLine(line);", session);
    }

    private static ConsoleQueueReply Answer(string text, bool failed = false) => new(true, true, failed, text);
    // A cleared reply as SendDetailed hands it on: Explain has added where the command was put.
    private static ConsoleQueueReply ClearedReply() => new(false, false, true, ConsoleQueue.Cleared + " Command folder: the queue. Running server: one.") { WasCleared = true };

    // NotSent was never written, so only it may go the console queue's way. MaybeSent may still land: never sent again,
    // not a failure. The rules are driven here with counted fakes for the console and the queue.
    [Fact]
    public void Stop_writes_the_console_once_and_only_a_stop_never_written_goes_to_the_queue()
    {
        foreach (var console in new[] { ConsoleWrite.Sent, ConsoleWrite.MaybeSent })
        {
            var writes = 0;
            var queued = 0;
            var (asked, written) = CoodszHost.Services.ServerSession.StopAsked(() => { writes++; return console; }, () => { queued++; return Answer("ok"); });
            Assert.True(asked);
            Assert.Equal(console, written);
            Assert.Equal(1, writes);
            Assert.Equal(0, queued);
        }

        // Never written: the queue once. A stop the server took and did not refuse is waited for even without an answer.
        var tries = 0;
        Assert.True(CoodszHost.Services.ServerSession.StopAsked(() => ConsoleWrite.NotSent, () => { tries++; return new ConsoleQueueReply(true, false, false, ConsoleQueue.StillWorking); }).Asked);
        Assert.Equal(1, tries);
        // The queue answers "unknown" to stop today, a start may have cleared it, or nothing can send: then the forced end.
        Assert.False(CoodszHost.Services.ServerSession.StopAsked(() => ConsoleWrite.NotSent, () => Answer("Unknown command stop", failed: true)).Asked);
        Assert.False(CoodszHost.Services.ServerSession.StopAsked(() => ConsoleWrite.NotSent, () => ClearedReply()).Asked);
        Assert.False(CoodszHost.Services.ServerSession.StopAsked(() => ConsoleWrite.NotSent, () => null).Asked);
        // A stop file the server never read starts no two minute wait.
        Assert.False(CoodszHost.Services.ServerSession.StopAsked(() => ConsoleWrite.NotSent, () => new ConsoleQueueReply(false, false, false, ConsoleQueue.NotPickedUp)).Asked);

        // Asked or not, StopGracefully ends with the forced Stop() after the wait.
        var session = SourceFiles.Read(Path.Combine("src", "CoodszHost", "Services", "ServerSession.cs"));
        var stop = SourceFiles.Method(session, "public StopOutcome StopGracefully(");
        var wait = stop.IndexOf("while (ThisServerRunning() && DateTime.UtcNow < deadline)", StringComparison.Ordinal);
        var end = stop.LastIndexOf("Stop();", StringComparison.Ordinal);
        Assert.True(wait >= 0 && end > wait);
        Assert.Contains("var outcome = ownConsole ? StopOutcome.NotTaken : StopOutcome.NotReached;", stop);
        // A stop that may not have landed says so in the host log.
        Assert.Contains("console == ConsoleWrite.MaybeSent", stop);
    }

    [Fact]
    public void Save_never_writes_the_console_twice_or_after_a_start_cleared_it()
    {
        var unavailable = Answer("The dedicated-server save service is unavailable");
        foreach (var (console, text) in new[] { (ConsoleWrite.Sent, "Save sent to the server."), (ConsoleWrite.MaybeSent, ServerCommandQueue.ConsoleSlow) })
        {
            var writes = 0;
            var reply = CoodszHost.Services.ServerSession.SaveReply(() => unavailable, () => { writes++; return console; }, () => true);
            Assert.Equal(1, writes);
            Assert.Equal(text, reply.Text);
            Assert.False(reply.Failed);
        }

        // This window's own server whose console did not take it: the timeout, not "started before the tool opened".
        Assert.Equal(ServerCommandQueue.ConsoleTimeout, CoodszHost.Services.ServerSession.SaveReply(() => unavailable, () => ConsoleWrite.NotSent, () => true).Text);
        Assert.Equal(CoodszHost.Services.ServerSession.SaveNeedsRestart, CoodszHost.Services.ServerSession.SaveReply(() => unavailable, () => ConsoleWrite.NotSent, () => false).Text);

        // A save a server start cleared belonged to the run before: no console write at all, so it can't save the new run.
        var cleared = 0;
        var gone = CoodszHost.Services.ServerSession.SaveReply(() => ClearedReply(), () => { cleared++; return ConsoleWrite.Sent; }, () => true);
        Assert.Equal(0, cleared);
        Assert.True(gone.WasCleared);
        // A save the queue did: no console write either.
        var done = 0;
        CoodszHost.Services.ServerSession.SaveReply(() => Answer("Saved."), () => { done++; return ConsoleWrite.Sent; }, () => true);
        Assert.Equal(0, done);

        // The console fallback is tied to the run the save began in, and a may still land save is never sent at the nudge.
        var session = SourceFiles.Read(Path.Combine("src", "CoodszHost", "Services", "ServerSession.cs"));
        var sendSave = SourceFiles.Method(session, "private ConsoleQueueReply SendSave(");
        Assert.Contains("() => TryConsoleLine(\"save\", saveRun)", sendSave);
        // The legacy retry after a Start writes nothing either.
        Assert.Contains("return SameRun(saveRun) ? SendDetailed(LegacySaveCommand) : RunEndedReply;", sendSave);
        var world = SourceFiles.Method(session, "public string SaveCurrentWorld(");
        Assert.Contains("var unconfirmed = sent.Text == ServerCommandQueue.ConsoleSlow;", world);
        Assert.Contains("if (!unconfirmed && SameRun(run))\r\n                        SendSave(run);", world);
        // After a Start the nudge and its pause write nothing.
        Assert.Contains("if (!IsRunning || !SameRun(run))", world);
        Assert.Contains("if (resumed && IsRunning && SameRun(run))", world);
    }

    [Fact]
    public void Say_writes_once_and_counts_as_told_only_with_a_real_answer()
    {
        foreach (var console in new[] { ConsoleWrite.Sent, ConsoleWrite.MaybeSent })
        {
            var writes = 0;
            var queued = 0;
            Assert.Equal(console, CoodszHost.Services.ServerSession.SayResult(() => { writes++; return console; }, () => { queued++; return Answer("ok"); }));
            Assert.Equal(1, writes);
            Assert.Equal(0, queued);
        }

        Assert.Equal(ConsoleWrite.Sent, CoodszHost.Services.ServerSession.SayResult(() => ConsoleWrite.NotSent, () => Answer("said")));
        // A file that went without an answer (a reopened window), a refusal, a cleared say or no way to send: not told.
        Assert.Equal(ConsoleWrite.NotSent, CoodszHost.Services.ServerSession.SayResult(() => ConsoleWrite.NotSent, () => new ConsoleQueueReply(true, false, false, ConsoleQueue.StillWorking)));
        Assert.Equal(ConsoleWrite.NotSent, CoodszHost.Services.ServerSession.SayResult(() => ConsoleWrite.NotSent, () => Answer("Unknown command say", failed: true)));
        Assert.Equal(ConsoleWrite.NotSent, CoodszHost.Services.ServerSession.SayResult(() => ConsoleWrite.NotSent, () => ClearedReply()));
        Assert.Equal(ConsoleWrite.NotSent, CoodszHost.Services.ServerSession.SayResult(() => ConsoleWrite.NotSent, () => null));

        var shutdown = SourceFiles.Read(Path.Combine("src", "CoodszHost", "MainWindow.Shutdown.cs"));
        Assert.Contains("Task.Run(() => _session.Say(message))", shutdown);
        var warn = SourceFiles.Method(shutdown, "private async Task WarnPlayersOfRestart(");
        Assert.Contains("if (warned == ConsoleWrite.MaybeSent)\r\n        {\r\n            Append(ServerCommandQueue.ConsoleSlow);", warn);
        Assert.Contains("ServerSession.StopOutcome.NotTaken => StopNotTakenNote,", shutdown);
        var control = SourceFiles.Window(SourceFiles.Root(), "CoodszHost");
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(control, @"if \(StopNote\(").Count);
    }

    [Fact]
    public void A_typed_builtin_and_every_console_miss_get_the_right_words_and_one_log_line()
    {
        var session = SourceFiles.Read(Path.Combine("src", "CoodszHost", "Services", "ServerSession.cs"));
        var send = SourceFiles.Method(session, "public string Send(string command)");
        Assert.Contains("ConsoleWrite.MaybeSent => ServerCommandQueue.ConsoleSlow,", send);
        Assert.Contains("_ => StartedHere ? ServerCommandQueue.ConsoleTimeout : BuiltinNeedsRestart", send);

        // A missed line, a broken pipe, and a pipe that breaks after the deadline are each one plain host log line.
        var line = SourceFiles.Method(session, "public ConsoleWrite TryConsoleLine(");
        Assert.Contains("WriteConsole(\"[CoodszHost] The server's console did not take \" + verb + \" within 5 seconds.\");", line);
        // The late line goes only to the log of the run that wrote it.
        Assert.Contains("if (SameRun(writeRun))\r\n                            WriteConsole(\"[CoodszHost] The server's console did not take \" + verb + \": \"", line);
        Assert.Contains("TaskContinuationOptions.OnlyOnFaulted", line);
        Assert.Contains("WriteConsole(\"[CoodszHost] The server's console did not take \" + line.Split(' ')[0] + \": \" + ex.Message);", line);
        // A request tied to a run never writes to the next run's console.
        Assert.Contains("if (run != null && run != _run)\r\n                return ConsoleWrite.NotSent;", line);
    }

    [Fact]
    public void Empty_and_too_long_commands_are_refused()
    {
        Assert.Throws<InvalidOperationException>(() => ConsoleQueue.Send(Array.Empty<string>(), "   "));
        Assert.Throws<InvalidOperationException>(() => ConsoleQueue.Send(Array.Empty<string>(), new string('a', ConsoleQueue.MaxCommandLength + 1)));
    }
}
