using BannerlordCoopLauncher.Core.Launch;

namespace BannerlordCoopLauncher.Tests;

public class ServerLogTailTests
{
    [Fact]
    public void Keeps_the_newest_lines_within_the_cap()
    {
        var tail = new ServerLogTail(30);
        for (var i = 0; i < 20; i++)
            tail.Add("line " + i);

        var text = tail.ToString();
        Assert.True(text.Length <= 30, text);
        Assert.EndsWith("line 19", text);
        Assert.DoesNotContain("line 0" + Environment.NewLine, text);
    }

    [Fact]
    public void Recent_returns_only_the_last_lines_in_order()
    {
        var tail = new ServerLogTail(10_000);
        foreach (var line in new[] { "a", "b", "c", "d" })
            tail.Add(line);

        Assert.Equal("c" + Environment.NewLine + "d", tail.Recent(2));
        Assert.Equal("a" + Environment.NewLine + "b" + Environment.NewLine + "c" + Environment.NewLine + "d", tail.Recent(60));
    }

    [Fact]
    public void Clear_empties_it_and_a_long_line_is_still_kept()
    {
        var tail = new ServerLogTail(5);
        tail.Add("first");
        tail.Clear();
        Assert.Equal("", tail.ToString());

        tail.Add("a line longer than the cap");
        Assert.Equal("a line longer than the cap", tail.ToString());
    }

    [Fact]
    public void Adding_a_burst_stays_fast()
    {
        var tail = new ServerLogTail(200_000);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        for (var i = 0; i < 20_000; i++)
            tail.Add("[Coop] [FieldSkew] \"MBList`1\" has no field '\"_syncRoot\"' on this runtime; skipping packed value " + i);
        Assert.True(watch.ElapsedMilliseconds < 2000, watch.ElapsedMilliseconds + " ms");
    }
}
