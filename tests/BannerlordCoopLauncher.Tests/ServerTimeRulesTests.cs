using System.Text;
using CoodszHost.Services;

namespace BannerlordCoopLauncher.Tests;

public class ServerTimeRulesTests
{
    private readonly Xunit.Abstractions.ITestOutputHelper _output;

    public ServerTimeRulesTests(Xunit.Abstractions.ITestOutputHelper output) => _output = output;

    private static string RepairRoot() => Path.Combine(RepoRoot(), "src", "CoodszHost", "DedicatedServerRepair");

    [Fact]
    public void Start_script_reads_fast_forward_and_auto_pause_instead_of_overwriting_them()
    {
        var script = File.ReadAllText(Path.Combine(RepairRoot(), "Start-ModdedServer.ps1"));
        // It used to copy allowClientTimeControls into fastForwardEnabled, undoing the host's setting.
        Assert.DoesNotContain("-Key 'fastForwardEnabled' `", script);
        Assert.Contains("Get-CoopBooleanModOption -Key 'fastForwardEnabled' -Default $true", script);
        Assert.Contains("Get-CoopBooleanModOption -Key 'autoPauseEnabled' -Default $false", script);
        Assert.Contains("$env:BANNERLORD_DS_AUTO_PAUSE =", script);
        // The cheats block Coodsz Host removes from its copy is still there to remove.
        Assert.NotNull(HostStartScript.WithoutForcedCheats(script));
    }

    [Fact]
    public void Launcher_battle_size_has_the_auto_pause_switch_and_is_replaced_on_servers()
    {
        var relative = Path.Combine("engine", "Modules", "DedicatedServer.Windows", "bin", "Win64_Shipping_Server", "DedicatedServer.LauncherBattleSize.dll");
        // The prebuilt DLL is in the private folder only (CONTRIBUTING.md, Private files).
        var shipped = Path.Combine(SourceFiles.PrivateRoot(), "DedicatedServerRepair", relative);
        if (File.Exists(shipped))
            Assert.True(Contains(File.ReadAllBytes(shipped), Encoding.Unicode.GetBytes("BANNERLORD_DS_AUTO_PAUSE")));
        else
            _output.WriteLine("Skipped the auto pause switch check: " + shipped + " is not on this PC (a private file).");
        Assert.Contains(relative, DedicatedServerRepair.ManagedScripts);

        var root = Path.Combine(Path.GetTempPath(), "repair-" + Guid.NewGuid().ToString("N"));
        try
        {
            var payload = Path.Combine(root, "payload");
            var server = Path.Combine(root, "server");
            Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(payload, relative))!);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(server, relative))!);
            File.WriteAllText(Path.Combine(payload, relative), "edited");
            File.WriteAllText(Path.Combine(server, relative), "original");

            var note = DedicatedServerRepair.Ensure(server, payload);

            Assert.Equal("edited", File.ReadAllText(Path.Combine(server, relative)));
            Assert.Equal("original", File.ReadAllText(Path.Combine(server, relative) + ".coodsz-old"));
            Assert.Contains("DedicatedServer.LauncherBattleSize.dll", note);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Rescue_module_is_left_out_of_the_save_check_and_has_a_guide()
    {
        var script = File.ReadAllText(Path.Combine(RepairRoot(), "Start-ModdedServer.ps1"));
        // Without this, Rescue save hid the save as incompatible ("module mismatch") and the server did not start.
        // DedicatedServer.CoopSaveRescue (every start) falls under the DedicatedServer.* pattern; saves
        // from an older Rescue save list the old id.
        Assert.Contains("$nonSaveModulePattern = 'DedicatedServer.*'", script);
        Assert.StartsWith("DedicatedServer.", SaveRescue.ModuleId);
        Assert.Contains("$nonSaveModuleIds = @('" + BannerlordCoopLauncher.Core.Support.AutoRescue.LegacyModuleId + "', ", script);
        // the Coodsz patch modules store nothing either, so adding one keeps an old save usable
        Assert.Contains("'Diplomacy.CoopCompat', 'StuckBattleFix', ", script);
        Assert.Equal(2, CountOf(script, "-and $_ -notin $nonSaveModuleIds"));

        Assert.StartsWith(SaveRescue.Steps, SaveRescue.Explain);
        Assert.Contains("5. Press Stop server, then Start server.", SaveRescue.Steps);
        // the step people got wrong: pressing Start themselves
        Assert.Contains("Don't press Start server. Rescue save starts the server for you.", SaveRescue.Steps);
        // one text for Coodsz Host, the AD1259 tool and the TAOM tool
        Assert.Equal(SaveRescue.Steps, TaomCoop.Core.RescueSubModule.Steps);
        var xaml = File.ReadAllText(Path.Combine(RepoRoot(), "src", "CoodszHost", "MainWindow.xaml"));
        Assert.Contains("Click=\"RescueHelp_Click\"", xaml);
    }

    private static int CountOf(string text, string part)
    {
        var count = 0;
        for (var index = text.IndexOf(part, StringComparison.Ordinal); index >= 0; index = text.IndexOf(part, index + part.Length, StringComparison.Ordinal))
            count++;
        return count;
    }

    private static bool Contains(byte[] haystack, byte[] needle)
    {
        for (var i = 0; i <= haystack.Length - needle.Length; i++)
        {
            var match = true;
            for (var j = 0; j < needle.Length && match; j++)
                match = haystack[i + j] == needle[j];
            if (match)
                return true;
        }

        return false;
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "BannerlordCoopLauncher.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
