using BannerlordCoopLauncher.Core.Support;

// Test only: the tool exes' start of a reader process, plus the ways a reader can go wrong.
if (args.Length > 0 && args[0] == "--hang")
{
    Thread.Sleep(TimeSpan.FromMinutes(2));
    return 0;
}

// Writes its text whole and then does not close, like a DAC thread stuck in native code.
if (args.Length > 0 && args[0] == "--write-then-hang")
{
    DumpStackSummary.OnStartup(args[1..]);
    Thread.Sleep(TimeSpan.FromMinutes(2));
    return 0;
}

// Cut off mid write: only a half .part file (args[3] is the output), then no close.
if (args.Length > 3 && args[0] == "--part-then-hang")
{
    File.WriteAllText(args[3] + ".part", "Crash stack summary read from the crash dump on this PC.\r\nExcep");
    Thread.Sleep(TimeSpan.FromMinutes(2));
    return 0;
}

if (args.Length > 0 && args[0] == "--crash")
    Environment.FailFast("A reader that crashes, for the test.");

return DumpStackSummary.OnStartup(args) ? Environment.ExitCode : 1;
