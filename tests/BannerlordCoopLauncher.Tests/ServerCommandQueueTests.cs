using TaomCoop.Core;
using BannerlordCoopLauncher.Core.Server;

namespace BannerlordCoopLauncher.Tests;

// The bounded console writes of the shared command queue (src\TaomCoop.Core\ServerCommandQueue.cs), with no window: a
// write reported as not sent never goes out later, one stuck on a full pipe may still land, and the next reply read waits
// for it. The tests decide when each deadline comes, so a slow thread pool can't change the outcome.
public class ServerCommandQueueTests
{
    private static readonly TimeSpan Roomy = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task A_write_whose_thread_gets_the_lock_after_the_deadline_never_writes()
    {
        var held = new object();
        var written = 0;
        using var holding = new ManualResetEventSlim(false);
        using var letGo = new ManualResetEventSlim(false);
        var holder = new Thread(() => { lock (held) { holding.Set(); letGo.Wait(); } });
        holder.Start();
        Assert.True(holding.Wait(Roomy));

        var (late, lateWrite) = await ServerCommandQueue.WriteBounded(held, Task.CompletedTask, Roomy, () => Interlocked.Increment(ref written));
        Assert.Equal(ConsoleWrite.NotSent, late);
        letGo.Set();
        Assert.Equal(ConsoleWrite.NotSent, await lateWrite);
        holder.Join();
        Assert.Equal(0, written);
    }

    [Fact]
    public async Task A_write_stuck_on_a_full_pipe_may_still_land_and_is_never_called_not_sent()
    {
        var stdin = new object();
        var written = 0;
        using var began = new ManualResetEventSlim(false);
        using var pipe = new ManualResetEventSlim(false);
        var deadline = new TaskCompletionSource();
        var call = ServerCommandQueue.WriteBounded(stdin, deadline.Task, Roomy, () => { began.Set(); pipe.Wait(); Interlocked.Increment(ref written); });
        Assert.True(began.Wait(Roomy));
        deadline.SetResult();
        var (stuck, stuckWrite) = await call;
        Assert.Equal(ConsoleWrite.MaybeSent, stuck);
        // It still holds the stdin lock, so the save or stop line times out instead of waiting, and writes nothing.
        Assert.Equal(ConsoleWrite.NotSent, ServerCommandQueue.TryWriteWithin(stdin, TimeSpan.FromMilliseconds(100), () => Interlocked.Increment(ref written)));
        pipe.Set();
        Assert.Equal(ConsoleWrite.Sent, await stuckWrite);
        Assert.Equal(1, written);

        // The same through the sync path the save and stop use.
        using var began2 = new ManualResetEventSlim(false);
        using var pipe2 = new ManualResetEventSlim(false);
        Assert.Equal(ConsoleWrite.MaybeSent, ServerCommandQueue.TryWriteWithin(new object(), Roomy, () => { began2.Set(); pipe2.Wait(); }, _ => began2.Wait(Roomy)));
        pipe2.Set();
    }

    [Fact]
    public async Task A_free_pipe_writes_and_a_broken_one_gives_the_caller_its_exception()
    {
        var free = new object();
        Assert.Equal(ConsoleWrite.Sent, await ServerCommandQueue.WriteWithinAsync(free, Roomy, () => { }));
        Assert.Equal(ConsoleWrite.Sent, (await ServerCommandQueue.WriteBounded(free, Roomy, () => { })).Result);
        Assert.Equal(ConsoleWrite.Sent, ServerCommandQueue.TryWriteWithin(free, Roomy, () => { }));
        await Assert.ThrowsAsync<IOException>(() => ServerCommandQueue.WriteWithinAsync(free, Roomy, () => throw new IOException("The pipe has been ended.")));
        Assert.Throws<IOException>(() => ServerCommandQueue.TryWriteWithin(free, Roomy, () => throw new IOException("The pipe has been ended.")));
    }

    [Fact]
    public async Task A_last_check_that_says_no_inside_the_lock_writes_nothing()
    {
        var written = 0;
        var (result, writing) = await ServerCommandQueue.WriteBounded(new object(), Roomy, () => Interlocked.Increment(ref written), stillValid: () => false);
        Assert.Equal(ConsoleWrite.Dropped, result);
        Assert.Equal(ConsoleWrite.Dropped, await writing);
        Assert.Equal(0, written);
        Assert.Equal(ConsoleWrite.Sent, (await ServerCommandQueue.WriteBounded(new object(), Roomy, () => Interlocked.Increment(ref written), stillValid: () => true)).Result);
        Assert.Equal(1, written);
    }

    [Fact]
    public void The_may_still_land_line_is_neither_a_done_line_nor_a_failure_and_marks_a_reply_unconfirmed()
    {
        Assert.Equal((ServerCommandQueue.ConsoleSlow, false), ServerTime.Result(JoinPause.PauseMode, true, ServerCommandQueue.ConsoleSlow, false));
        Assert.Equal(CommandReplyTone.Normal, CommandReply.Tone(ServerCommandQueue.ConsoleSlow));
        Assert.True(ServerCommandQueue.Unconfirmed(new[] { ServerCommandQueue.ConsoleSlow }));
        Assert.False(ServerCommandQueue.Unconfirmed(new[] { "Time mode set to Pause." }));
        Assert.False(ServerCommandQueue.Unconfirmed(null));
    }

    [Fact]
    public async Task The_next_reply_read_waits_for_a_stuck_write_and_its_output()
    {
        // Nothing stuck: at once.
        Assert.True(await ServerCommandQueue.AfterStuckWrite(null, Roomy, TimeSpan.Zero));
        // A stuck write that finishes: waited for, then its output gets the settle time before the next read starts.
        var stuck = new TaskCompletionSource<ConsoleWrite>();
        var waiting = ServerCommandQueue.AfterStuckWrite(stuck.Task, Roomy, TimeSpan.FromMilliseconds(50));
        Assert.False(waiting.IsCompleted);
        stuck.SetResult(ConsoleWrite.Sent);
        Assert.True(await waiting);
        // One still stuck: the next command is not sent (it would only wait on the stdin lock the stuck write holds).
        Assert.False(await ServerCommandQueue.AfterStuckWrite(new TaskCompletionSource<ConsoleWrite>().Task, TimeSpan.FromMilliseconds(50), TimeSpan.Zero));
    }
}
