using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using BannerlordCoopLauncher.Core.Support;
using BannerlordCoopLauncher.Core.Updates;

namespace BannerlordCoopLauncher.Tests;

/// <summary>
/// The tool start the updater uses in every test run. A test never opens a program it made: a fake exe (text
/// in a .exe file) opened through the shell made Windows show "Unsupported 16-Bit Application" on Coodsz's PC
/// during test runs. A .cmd test tool still runs, hidden and without the shell, so the tests that wait for its
/// started.txt keep working. Anything else is only recorded. A missing file still fails like a real start.
/// </summary>
internal static class SafeToolStart
{
    internal static readonly ConcurrentQueue<string> Started = new();

    [ModuleInitializer]
    internal static void Install()
    {
        UpdateApply.Starter = Start;
        // No test may pop UAC: the map shader removal starts nothing in a test run.
        TerrainShaderCache.ElevatedStart = Elevated;
    }

    internal static int Elevated(ProcessStartInfo start) => 0;

    internal static void Start(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("The system cannot find the file specified.", path);
        Started.Enqueue(path);
        if (!path.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase))
            return;

        var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(path) ?? ""
        };
        start.ArgumentList.Add("/d");
        start.ArgumentList.Add("/c");
        start.ArgumentList.Add(path);
        using var process = Process.Start(start);
    }
}
