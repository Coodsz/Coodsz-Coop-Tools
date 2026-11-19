using BannerlordCoopLauncher.Core.Support;
using BannerlordCoopLauncher.Core.Updates;
using Xunit;

namespace BannerlordCoopLauncher.Tests;

// No test may open a program it made: Windows showed "Unsupported 16-Bit Application" on Coodsz's PC when an
// update test started a fake "Coodsz Coop and Friends.exe" through the shell.
public class SafeToolStartTests
{
    [Fact]
    public void Every_test_run_uses_the_safe_tool_start()
    {
        Assert.True(UpdateApply.Starter == (Action<string>)SafeToolStart.Start);
        Assert.True(TerrainShaderCache.ElevatedStart == (Func<System.Diagnostics.ProcessStartInfo, int>)SafeToolStart.Elevated);
    }

    [Fact]
    public void A_fake_exe_is_recorded_and_never_opened()
    {
        var root = Path.Combine(Path.GetTempPath(), "coodsz-safestart-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var fake = Path.Combine(root, "Coodsz Coop and Friends.exe");
        File.WriteAllText(fake, "new");
        try
        {
            // Only a .cmd test tool runs; an exe is recorded, so the shell never sees it.
            SafeToolStart.Start(fake);

            Assert.Contains(fake, SafeToolStart.Started);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void A_missing_tool_still_fails_like_a_real_start()
    {
        var missing = Path.Combine(Path.GetTempPath(), "coodsz-safestart-missing-" + Guid.NewGuid().ToString("N") + ".exe");

        Assert.Throws<FileNotFoundException>(() => SafeToolStart.Start(missing));
    }
}
