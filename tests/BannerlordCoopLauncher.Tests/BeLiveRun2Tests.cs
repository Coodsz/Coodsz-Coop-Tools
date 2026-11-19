using BannerlordCoopLauncher.Core.Keep;
using BannerlordCoopLauncher.Core.Modules;
using BannerlordCoopLauncher.Core.Server;
using BurningEmpires.Core;
using TaomCoop.Core;

namespace BannerlordCoopLauncher.Tests;

// Live run 2 of the Burning Empires tools: the save fix notes name this tool's button, the crash
// bar and the status skip the harmless lines, Stop on a server still loading does not wait for a
// save, Coop from the Workshop is fine in Check mods, the join note names interface mods, and the
// first start says what to click in the Windows Firewall prompt.
public sealed class BeLiveRun2Tests
{
    // The server log after the server was ended by hand while it loaded (live run 2, trimmed).
    private const string KilledWhileLoading =
        "09:36:32.192  [BurningEmpires.CoopCompat] Crash trace: dedicated server exception logging ready.\n"
        + "09:36:40.100  [BurningEmpires.CoopCompat] First chance System.IO.FileNotFoundException: Could not load file or assembly 'BurningEmpires.XmlSerializers, Version=2.0.2.0, Culture=neutral'. The system cannot find the file specified.\n"
        + "09:36:40.101  System.IO.FileNotFoundException: Could not load file or assembly 'C:\\BurningEmpires-Coop-Host\\Server\\engine\\bin\\Win64_Shipping_Server\\BurningEmpires.XmlSerializers.dll'. The system cannot find the file specified.\n"
        + "09:37:02.722  [DedicatedServer Engine] [BurningEmpires.CoopCompat] First chance System.Net.Sockets.SocketException: No connection could be made because the target machine actively refused it.\n"
        + "09:37:02.722      at System.Net.Sockets.Socket+AwaitableSocketAsyncEventArgs.ThrowException\n"
        + "09:37:02.729  [BurningEmpires.CoopCompat] First chance System.Net.Http.HttpRequestException: No connection could be made because the target machine actively refused it. (localhost:5341)\n"
        + "09:37:02.730  Seq: System.Net.Http.HttpRequestException: No connection could be made because the target machine actively refused it. (localhost:5341)\n"
        + "09:37:02.730      at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw\n"
        + "09:37:03.000  [BurningEmpires.CoopCompat] Recruit deduction: hooked Coop's recruitment commit.\n"
        + "09:37:10.500  System.Reflection.TargetInvocationException: Exception has been thrown by the target of an invocation.\n"
        + "09:37:10.501  [BurningEmpires.CoopCompat] Kingdom sync ready.\n"
        + "09:37:19.774  [Server] Operational | online=0 | loading=0 | tick=PAUSED | queueMax=0 | mode=Pause\n"
        + "[BE host] engine exited with code -1\n";

    [Fact]
    public void The_save_fix_notes_name_Restore_save_backup_on_the_Modules_page()
    {
        foreach (var line in new[] { BeRescueText.FixedSaved, BeRescueText.FixedNotSaved, BeRescueText.RetryFailedBody, BeRescueText.BackupKept("BETest2") })
        {
            Assert.Contains("Restore save backup on the Saves page", line);
            Assert.DoesNotContain("Load backup", line);
        }

        var host = SourceFiles.Read("src/BurningEmpiresHost/MainWindow.Rescue.cs");
        Assert.DoesNotContain("AutoRescue.FixedSaved", host);
        Assert.DoesNotContain("AutoRescue.FixedNotSaved", host);
        Assert.DoesNotContain("AutoRescue.RetryFailedBody", host);
        Assert.DoesNotContain("AutoRescue.BackupKept", host);
        Assert.Contains("Content=\"Restore save backup\"", SourceFiles.Read("src/BurningEmpiresHost/MainWindow.xaml"));
    }

    [Fact]
    public void A_server_ended_by_hand_names_no_harmless_line_as_the_cause()
    {
        Assert.Null(BeHostReport.CrashLine(KilledWhileLoading));
        Assert.Equal(BeHostReport.NoErrorTitle, BeHostReport.CrashTitle(null, ServerExitKind.Unknown));
        Assert.Equal("The server stopped without an error in its log", BeHostReport.NoErrorTitle);

        var report = BeHostReport.Explain(KilledWhileLoading, wasServing: false);
        Assert.NotNull(report);
        Assert.Equal("The server stopped without an error in its log.", report.Value.Status);
        Assert.DoesNotContain("XmlSerializers", report.Value.Status + string.Join(" ", report.Value.Lines));
        Assert.Contains("before it was ready for players, without an error in its log", report.Value.Lines[0]);

        var served = BeHostReport.Explain(KilledWhileLoading, wasServing: true);
        Assert.Contains("was up and then it stopped without an error in its log", served!.Value.Lines[0]);
    }

    [Fact]
    public void A_real_error_next_to_the_harmless_lines_is_still_the_cause()
    {
        var crash = KilledWhileLoading.Replace("[BE host] engine exited with code -1\n", "")
            + "System.NullReferenceException: Object reference not set to an instance of an object.\n"
            + "   at BurningEmpires.Campaign.Tick()\n"
            + "[BE host] engine exited with code -1\n";
        Assert.StartsWith("System.NullReferenceException: Object reference", BeHostReport.CrashLine(crash));
        Assert.Equal("The server stopped while it was starting.", BeHostReport.Explain(crash, wasServing: false)!.Value.Status);
        Assert.Equal("The server crashed", BeHostReport.CrashTitle(BeHostReport.CrashLine(crash), ServerExitKind.Unknown));

        // The inner exception of a TargetInvocationException is on its own ---> line and still counts.
        var inner = "System.Reflection.TargetInvocationException: Exception has been thrown by the target of an invocation.\n"
            + " ---> System.IO.FileNotFoundException: Could not load file or assembly 'Bannerlord.UIExtenderEx, Version=2.13.3.0, Culture=neutral'.\n"
            + "[BE host] engine exited with code 2\n";
        Assert.Contains("Bannerlord.UIExtenderEx", BeHostReport.CrashLine(inner));
        Assert.Contains("Bannerlord.UIExtenderEx", BeHostReport.Explain(inner, wasServing: false)!.Value.Status);
    }

    [Fact]
    public void Stop_on_a_server_still_loading_does_not_wait_for_a_save()
    {
        Assert.False(BeServerStop.WaitsForSave(reachedServing: false));
        Assert.True(BeServerStop.WaitsForSave(reachedServing: true));
        Assert.Equal("Stopped right away. The server was still loading, so no save was needed.", BeServerStop.StoppedWhileLoading);

        var control = SourceFiles.Read("src/BurningEmpiresHost/MainWindow.ServerControl.cs");
        Assert.Contains("BeServerStop.WaitsForSave(_seenServing || _servedThisRun)", control);
        Assert.Contains("if (refusal == null && !loading)", control);
    }

    [Fact]
    public void Coop_from_the_Workshop_is_fine_in_Check_mods()
    {
        var root = Path.Combine(Path.GetTempPath(), "be-coop-copy-" + Guid.NewGuid().ToString("N"));
        var modules = Path.Combine(root, "Modules");
        Directory.CreateDirectory(modules);
        try
        {
            Assert.Equal(BeCoopCopy.FromWorkshop, BeCoopCopy.Describe(modules, @"C:\Steam\steamapps\workshop\content\261550\2881181332"));
            Assert.Equal(CoopStaging.NotInModules, BeCoopCopy.Describe(modules, null));

            Directory.CreateDirectory(Path.Combine(modules, "Coop"));
            File.WriteAllText(Path.Combine(modules, "Coop", "SubModule.xml"), "<Module><Id value=\"Coop\"/><Version value=\"v0.1.6\"/></Module>");
            Assert.StartsWith("Bannerlord Coop is in this Modules folder once.", BeCoopCopy.Describe(modules, @"C:\Steam\workshop\Coop"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }

        Assert.DoesNotContain("CoopStaging.DescribeCopies", SourceFiles.Read("src/BurningEmpiresLauncher/MainWindow.xaml.cs"));
    }

    [Fact]
    public void An_interface_mod_on_the_client_only_is_still_turned_down_and_the_note_says_so()
    {
        // Coop 0.1.6's ModuleValidator compares every community module both ways. Item Quality
        // Indicator only changes the screen, but it is not a DedicatedServer.* module.
        var core = BeStack.ServerModules;
        Assert.NotNull(CoopJoinRules.JoinProblem(core.Append("ItemQualityIndicator"), core));
        Assert.Null(CoopJoinRules.JoinProblem(core.Append("DedicatedServer.CoodszJoinPassword"), core));

        var xaml = SourceFiles.Read("src/BurningEmpiresLauncher/MainWindow.xaml");
        Assert.Contains("The host has to load the same extra mods, even interface mods, or the server turns the join down.", xaml);
        Assert.Contains("The host has to load the same mods, even interface mods, or the server turns the join down.", SourceFiles.Read("src/BurningEmpiresLauncher/MainWindow.xaml.cs"));
    }

    [Fact]
    public void The_first_start_from_a_server_folder_says_to_allow_dotnet_host()
    {
        Assert.Equal("Windows may ask to allow .NET Host. Click Allow so players can join.", BeFirewallHint.Text);
        Assert.True(BeFirewallHint.Shows("", @"C:\BurningEmpires-Coop-Host\Server"));
        Assert.True(BeFirewallHint.Shows(@"D:\Old\Server", @"C:\BurningEmpires-Coop-Host\Server"));
        Assert.False(BeFirewallHint.Shows(@"C:\BurningEmpires-Coop-Host\Server\", @"c:\burningempires-coop-host\server"));
        Assert.False(BeFirewallHint.Shows("", ""));

        Assert.Contains("x:Name=\"FirewallHintText\"", SourceFiles.Read("src/BurningEmpiresHost/MainWindow.xaml"));
        var lines = GuideLibrary.For(GuideSet.BeServer).SelectMany(GuideLibrary.Lines).ToList();
        Assert.Contains("The first time, Windows may ask to allow .NET Host. Click Allow so players can join.", lines);
        Assert.Contains("A server that is still loading stops right away, with no save.", lines);
    }
}
