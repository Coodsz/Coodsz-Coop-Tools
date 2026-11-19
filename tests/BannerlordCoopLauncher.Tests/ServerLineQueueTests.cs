using BannerlordCoopLauncher.Core.Launch;

namespace BannerlordCoopLauncher.Tests;

public class ServerLineQueueTests
{
    private static readonly TimeSpan Long = TimeSpan.FromMinutes(1);

    [Fact]
    public void Lines_come_out_in_order_and_only_the_last_ones_are_drawn()
    {
        var queue = new ServerLineQueue<int>();
        for (var i = 0; i < 1000; i++)
            queue.Add(i);

        var seen = new List<(int Line, bool Draw)>();
        Assert.Equal(1000, queue.Drain((line, draw) => seen.Add((line, draw)), 400, Long));

        Assert.Equal(Enumerable.Range(0, 1000), seen.Select(item => item.Line));
        Assert.All(seen.Take(600), item => Assert.False(item.Draw));
        Assert.All(seen.Skip(600), item => Assert.True(item.Draw));
        Assert.True(queue.IsEmpty);
        Assert.Equal(0, queue.Drain((_, _) => { }, 400, Long));
    }

    [Fact]
    public void A_small_batch_is_drawn_whole()
    {
        var queue = new ServerLineQueue<string>();
        queue.Add("a");
        queue.Add("b");
        var drawn = new List<string>();
        queue.Drain((line, draw) => { if (draw) drawn.Add(line); }, 400, Long);
        Assert.Equal(new[] { "a", "b" }, drawn);
        Assert.True(ServerLineQueue<string>.Drawn(399, 400));
        Assert.False(ServerLineQueue<string>.Drawn(400, 400));
    }

    [Fact]
    public void The_time_budget_leaves_the_rest_for_the_next_round()
    {
        var queue = new ServerLineQueue<int>();
        for (var i = 0; i < 10; i++)
            queue.Add(i);

        var seen = new List<int>();
        // A zero budget stops after the first line, so the window gets a frame.
        Assert.Equal(1, queue.Drain((line, _) => seen.Add(line), 400, TimeSpan.Zero));
        Assert.Equal(9, queue.Count);
        queue.Drain((line, _) => seen.Add(line), 400, Long);
        Assert.Equal(Enumerable.Range(0, 10), seen);
    }

    [Fact]
    public void Every_line_is_handled_once_in_order_while_readers_keep_adding()
    {
        // Two output readers (stdout and stderr) add while the window drains on its timer.
        var queue = new ServerLineQueue<int>();
        var handled = new List<int>();
        var writers = Enumerable.Range(0, 2).Select(w => Task.Run(() =>
        {
            for (var i = 0; i < 20_000; i++)
                queue.Add(w * 100_000 + i);
        })).ToArray();

        while (!Task.WhenAll(writers).IsCompleted || !queue.IsEmpty)
            queue.Drain((line, _) => handled.Add(line), 400, TimeSpan.FromMilliseconds(5));

        Assert.Equal(40_000, handled.Count);
        foreach (var writer in new[] { 0, 1 })
        {
            var mine = handled.Where(n => n / 100_000 == writer).ToList();
            Assert.Equal(Enumerable.Range(writer * 100_000, 20_000), mine);
        }
    }
}
