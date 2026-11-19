using TaomCoop.Core;

namespace BannerlordCoopLauncher.Tests;

public class TaomHostReportTests
{
    [Fact]
    public void A_blocked_dll_still_points_at_this_server_tool()
    {
        var report = TaomHostReport.Explain("Cannot load: ..\\Modules\\TradeBoundFix\\bin\\Win64_Shipping_Client\\TradeBoundFix.dll", wasServing: false);

        Assert.NotNull(report);
        Assert.Contains("does not clear this server folder", report.Value.Lines[0]);
        Assert.Contains("Unblock DLLs in this server tool", report.Value.Lines[1]);
        Assert.True(report.Value.Failed);
    }

    [Fact]
    public void A_save_that_does_not_match_refuses_a_vanilla_new_game()
    {
        var report = TaomHostReport.Explain("Save 'Finale' was not found or is incompatible with the selected module set.", false);

        Assert.NotNull(report);
        Assert.Contains("Finale", report.Value.Lines[0]);
        Assert.Contains("Name server copy", report.Value.Lines[1]);
        Assert.Contains("Do not start a vanilla new game", report.Value.Lines[2]);
        Assert.Contains("Do not delete the single-player save", report.Value.Lines[2]);
    }

    [Fact]
    public void A_sealed_module_that_did_not_load_is_not_turned_off()
    {
        var report = TaomHostReport.Explain("TAOM.CoopCompat could not be loaded correctly", false);

        Assert.NotNull(report);
        Assert.Contains("TAOM.CoopCompat did not load", report.Value.Lines[0]);
        Assert.Contains("Do not turn Harmony or ButterLib on", report.Value.Lines[2]);
        Assert.DoesNotContain("Save module config", string.Join(" ", report.Value.Lines));
    }

    [Fact]
    public void A_missing_dependency_keeps_the_sealed_list_whole()
    {
        var report = TaomHostReport.Explain("TAOM.CoopCompat depends on CoopNightly", false);

        Assert.NotNull(report);
        Assert.Contains("needs CoopNightly", report.Value.Lines[0]);
        Assert.Contains("Do not turn TAOM.CoopCompat off", report.Value.Lines[2]);
    }

    [Fact]
    public void Two_copies_of_a_module_tell_the_host_to_move_the_extra_folder()
    {
        var report = TaomHostReport.Explain("module id CoopNightly is already loaded", false);

        Assert.NotNull(report);
        Assert.Contains("more than one folder", report.Value.Lines[0]);
        Assert.Contains("Do not delete the playtest copy", report.Value.Lines[2]);
    }

    [Fact]
    public void An_exception_names_the_sealed_module()
    {
        var report = TaomHostReport.Explain("NullReferenceException in TAOM.CoopCompat.Bootstrap", false);

        Assert.NotNull(report);
        Assert.Contains("TAOM.CoopCompat", report.Value.Lines[0]);
        Assert.Contains("Do not add mods to the sealed list", report.Value.Lines[2]);
    }

    [Fact]
    public void A_normal_stop_after_serving_does_not_tell_them_to_change_mods()
    {
        var report = TaomHostReport.Explain("engine exited with code 0", wasServing: true);

        Assert.NotNull(report);
        Assert.Contains("stopped on its own", report.Value.Lines[0]);
        Assert.False(report.Value.Failed);
        Assert.Contains("Do not delete the campaign save", report.Value.Lines[2]);
    }

    [Fact]
    public void A_stop_before_serving_does_not_name_a_mod()
    {
        var report = TaomHostReport.Explain("engine exited with code 1", wasServing: false);

        Assert.NotNull(report);
        Assert.Contains("before it was ready", report.Value.Lines[0]);
        Assert.Contains("Do not start a vanilla new game", report.Value.Lines[2]);
    }

    [Fact]
    public void Ordinary_log_lines_stay_quiet()
    {
        Assert.Null(TaomHostReport.Explain("[DedicatedServer] SERVING", wasServing: true));
        Assert.Null(TaomHostReport.Explain("Loading assembly: Native.dll", wasServing: false));
    }

    // The TAOM plus live test: the Command Args line names BetterExceptionWindow and Native, and
    // the tool said the server stopped one second after the start while it went on to SERVING.
    [Fact]
    public void The_BetterExceptionWindow_mod_name_is_not_an_exception()
    {
        const string args = "[00:06:47.436] Command Args: _MODULES_*TAOM.Dependencies*Native*SandBoxCore*Coop*TAOM.CoopCompat*BetterExceptionWindow*Bannerlord.CCsBanners*_MODULES_ /dedicatedcustomserver 7210 EU 0";
        const string xml = "[00:06:47.233] Loading xml file: $BASE/Modules/BetterExceptionWindow/SubModule.xml. Native";

        Assert.Null(TaomHostReport.Explain(args, wasServing: false));
        Assert.Null(TaomHostReport.Explain(xml, wasServing: false));
    }

    [Fact]
    public void While_the_server_runs_only_sure_reports_count()
    {
        Assert.Null(TaomHostReport.Explain("NullReferenceException in TAOM.CoopCompat.Bootstrap", false, exited: false));
        Assert.Null(TaomHostReport.Explain("engine exited with code 1", false, exited: false));
        Assert.NotNull(TaomHostReport.Explain("NullReferenceException in TAOM.CoopCompat.Bootstrap", false, exited: true));
        Assert.NotNull(TaomHostReport.Explain("Cannot load: ..\\Modules\\TradeBoundFix\\bin\\Win64_Shipping_Client\\TradeBoundFix.dll", false, exited: false));
    }

    [Fact]
    public void An_older_run_log_is_not_read_as_this_run()
    {
        var dir = Directory.CreateTempSubdirectory("taom-runlog-");
        try
        {
            var old = Path.Combine(dir.FullName, "coop-server-20261017-232448.log");
            File.WriteAllText(old, "[DedicatedServer] SERVING coop server up, waiting for clients");
            File.SetLastWriteTimeUtc(old, new DateTime(2026, 10, 18, 4, 2, 2, DateTimeKind.Utc));
            var console = Path.Combine(dir.FullName, "host-console-20261018-000646.log");
            File.WriteAllText(console, "[DedicatedServer] OnBeforeInitialModuleScreenSetAsRoot at tick 213");
            File.SetLastWriteTimeUtc(console, new DateTime(2026, 10, 18, 4, 6, 57, DateTimeKind.Utc));
            var compat = Path.Combine(dir.FullName, "coopcompat-30288.log");
            File.WriteAllText(compat, "general-navigation-recovery");
            File.SetLastWriteTimeUtc(compat, new DateTime(2026, 10, 18, 4, 7, 34, DateTimeKind.Utc));
            var started = new DateTime(2026, 10, 18, 4, 6, 44, DateTimeKind.Utc);
            var files = dir.GetFiles();

            Assert.Equal("host-console-20261018-000646.log", HostLogFile.Prefer(HostLogFile.ThisRun(files, started)).First().Name);
            Assert.Equal("host-console-20261018-000646.log", HostLogFile.ConsoleOf(files, started)!.Name);
            Assert.Equal("coop-server-20261017-232448.log", HostLogFile.ConsoleOf(files, null)!.Name);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }
}
