using BannerlordCoopLauncher.Core.Server;
using BurningEmpires.Core;

namespace BannerlordCoopLauncher.Tests;

// The Burning Empires port of the TAOM module DLL check (ServerProgramRepair.ModuleFiles, CR-KFRG6):
// a module DLL antivirus took from the server folder comes back from the Workshop server, one that
// cannot stops the start, and a run that dies with one gone is no load crash.
public sealed class BeServerModuleRepairTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("be-module-repair-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static readonly string SandBoxDll = Path.Combine("engine", "Modules", "SandBox", "bin", "Win64_Shipping_Server", "SandBox.dll");
    private static readonly string CoopDll = Path.Combine("engine", "Modules", "Coop", "bin", "Win64_Shipping_Server", "Coop.dll");
    private static readonly string StoryModeDll = Path.Combine("engine", "Modules", "StoryMode", "bin", "Win64_Shipping_Server", "StoryMode.dll");

    private string Workshop()
    {
        var workshop = Path.Combine(_root, "workshop", "DedicatedServer");
        foreach (var (dll, text) in new[] { (SandBoxDll, "sandbox"), (CoopDll, "coop"), (StoryModeDll, "story") })
            Write(Path.Combine(workshop, dll), text);
        return workshop;
    }

    private string Server(params string[] dlls)
    {
        var server = Path.Combine(_root, "server");
        Directory.CreateDirectory(server);
        foreach (var dll in dlls)
            Write(Path.Combine(server, dll), "old copy");
        return server;
    }

    private static void Write(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    [Fact]
    public void The_list_holds_the_server_dlls_of_the_workshop_modules_the_burning_empires_server_loads()
    {
        var files = BeServerModuleRepair.WorkshopModuleFiles(Workshop());

        Assert.Equal(new[] { SandBoxDll, CoopDll }.Order(), files.Select(file => file.Target).Order());
        // StoryMode is copied by Prepare server but the Burning Empires server never loads it (BeStack.ServerModules).
        Assert.DoesNotContain(files, file => file.Target == StoryModeDll);
        Assert.Empty(BeServerModuleRepair.WorkshopModuleFiles(null));
    }

    [Fact]
    public void Start_puts_a_missing_workshop_module_dll_back_and_never_overwrites_one()
    {
        var workshop = Workshop();
        var server = Server(CoopDll);
        var files = BeServerModuleRepair.WorkshopModuleFiles(workshop);

        Assert.Equal(new[] { SandBoxDll }, BeServerModuleRepair.BlockingModuleFiles(server, files));
        var result = BeServerModuleRepair.Repair(server, Path.Combine(_root, "data"), files);

        Assert.Equal(new[] { SandBoxDll }, result.Restored);
        Assert.True(result.Repaired);
        Assert.Equal("sandbox", File.ReadAllText(Path.Combine(server, SandBoxDll)));
        // A file already there stays as it is, whatever it holds.
        Assert.Equal("old copy", File.ReadAllText(Path.Combine(server, CoopDll)));
        Assert.Empty(BeServerModuleRepair.BlockingModuleFiles(server, files));
    }

    [Fact]
    public void A_missing_dll_blocks_only_when_this_pc_has_its_source()
    {
        var workshop = Workshop();
        var server = Server(CoopDll);
        var files = BeServerModuleRepair.WorkshopModuleFiles(workshop);
        File.Delete(Path.Combine(workshop, SandBoxDll));

        Assert.Empty(BeServerModuleRepair.BlockingModuleFiles(server, files));
        Assert.Equal(new[] { SandBoxDll }, BeServerModuleRepair.MissingModuleFiles(server, files));
        var result = BeServerModuleRepair.Repair(server, Path.Combine(_root, "data"), files);
        Assert.Equal(new[] { SandBoxDll }, result.StillMissing);
        Assert.Empty(BeServerModuleRepair.BlockingModuleFiles(" ", files));
    }

    [Fact]
    public void Nothing_is_written_when_the_data_folder_and_the_server_folder_overlap()
    {
        var workshop = Workshop();
        var server = Server(CoopDll);
        var files = BeServerModuleRepair.WorkshopModuleFiles(workshop);

        var result = BeServerModuleRepair.Repair(server, Path.Combine(server, "Data"), files);

        Assert.Empty(result.Restored);
        Assert.Equal(new[] { SandBoxDll }, result.StillMissing);
        Assert.False(File.Exists(Path.Combine(server, SandBoxDll)));
    }

    [Fact]
    public void A_file_that_shows_up_after_the_scan_is_never_deleted()
    {
        var workshop = Workshop();
        var server = Server();
        var other = Path.Combine(_root, "other", "SandBox.dll");
        Write(other, "someone else");
        // Both entries are missing at the scan; the first one's copy is there when the second one copies,
        // like a file another program writes between the scan and the copy.
        var files = new[] { (SandBoxDll, other), (SandBoxDll, Path.Combine(workshop, SandBoxDll)) };

        var result = BeServerModuleRepair.Repair(server, Path.Combine(_root, "data"), files);

        Assert.Equal(new[] { SandBoxDll }, result.Restored);
        Assert.Equal(new[] { SandBoxDll }, result.StillMissing);
        Assert.Equal("someone else", File.ReadAllText(Path.Combine(server, SandBoxDll)));
        // The failed copy's temp file is gone too.
        Assert.Equal(new[] { "SandBox.dll" }, Directory.GetFiles(Path.GetDirectoryName(Path.Combine(server, SandBoxDll))!).Select(Path.GetFileName));
    }

    [Fact]
    public void The_staged_list_checks_the_burning_empires_server_bin_and_the_compat_server_dll()
    {
        var game = Path.Combine(_root, "game");
        var compat = Path.Combine(_root, "payload", "ServerAddOns", BeStack.CompatModuleId);
        Write(Path.Combine(compat, "bin", "Win64_Shipping_Server", "BurningEmpires.CoopCompat.dll"), "compat");
        var sources = new BeCoreSources(game, [], compat, Path.Combine(_root, "payload", "ServerAddOns", "UIExtenderEx-2.13.3", "Bannerlord.UIExtenderEx.dll"));

        var targets = BeServerModuleRepair.StagedModuleFiles(sources).Select(file => file.Target).ToList();

        var beBin = Path.Combine("engine", "Modules", BeServerPath.BeModuleId, "bin", "Win64_Shipping_Server");
        Assert.Contains(Path.Combine(beBin, BeServerPath.BeDll), targets);
        Assert.Contains(Path.Combine(beBin, BeServerPath.ProbeDll), targets);
        Assert.Contains(Path.Combine(beBin, "Bannerlord.UIExtenderEx.dll"), targets);
        Assert.Contains(Path.Combine(beBin, "TaleWorlds.MountAndBlade.CustomBattle.dll"), targets);
        Assert.Contains(Path.Combine("engine", "Modules", BeStack.CompatModuleId, "bin", "Win64_Shipping_Server", "BurningEmpires.CoopCompat.dll"), targets);
        // Only the compat DLL has a source here, so only it blocks.
        Assert.Equal(new[] { Path.Combine("engine", "Modules", BeStack.CompatModuleId, "bin", "Win64_Shipping_Server", "BurningEmpires.CoopCompat.dll") },
            BeServerModuleRepair.BlockingModuleFiles(Server(), BeServerModuleRepair.StagedModuleFiles(sources)));
    }

    [Fact]
    public void A_crash_with_a_module_dll_gone_is_no_load_crash()
    {
        var workshop = Workshop();
        var server = Server(CoopDll);
        var files = BeServerModuleRepair.WorkshopModuleFiles(workshop);
        var crash = ServerExitInfo.From(-532462766, wasExpected: false);

        Assert.Equal(new[] { SandBoxDll }, BeServerModuleRepair.MissingAtExit(crash, servedThisRun: false, server, files));
        // A stop the tool asked for, or a run that served, is never about missing files.
        Assert.Empty(BeServerModuleRepair.MissingAtExit(ServerExitInfo.From(-532462766, wasExpected: true), false, server, files));
        Assert.Empty(BeServerModuleRepair.MissingAtExit(crash, servedThisRun: true, server, files));
    }

    [Fact]
    public void Start_prepare_test_mods_and_the_exit_use_the_check()
    {
        var setups = SourceFiles.Read(Path.Combine("src", "BurningEmpiresHost", "MainWindow.ModSetups.cs"));
        var prepare = SourceFiles.Method(setups, "private async Task<BeServerStart?> PrepareBeServerAsync(");
        Assert.True(prepare.IndexOf("BeServerPath.StageCore(", StringComparison.Ordinal) < prepare.IndexOf("EnsureModuleFiles(serverRoot, dataRoot, workshop, sources)", StringComparison.Ordinal));
        var ensure = SourceFiles.Method(setups, "private IReadOnlyList<string> EnsureModuleFiles(");
        Assert.Contains("BeServerModuleRepair.Repair(serverRoot, dataRoot, workshopFiles)", ensure);
        Assert.Contains("throw new InvalidOperationException(BeServerModuleRepair.MissingText(blocking))", ensure);
        Assert.Contains("owner.EnsureModuleFiles(", SourceFiles.Read(Path.Combine("src", "BurningEmpiresHost", "MainWindow.ModTest.cs")));

        var control = SourceFiles.Read(Path.Combine("src", "BurningEmpiresHost", "MainWindow.ServerControl.cs"));
        Assert.Contains("BeServerModuleRepair.BlockingModuleFiles(serverRoot, BeServerModuleRepair.WorkshopModuleFiles(serverSource))", SourceFiles.Method(control, "private async Task<bool> PrepareServerAsync("));
        var start = SourceFiles.Method(control, "private async Task StartServerCoreAsync(");
        var check = start.IndexOf("IsMissingModuleFilesStop(exitCode, server, moduleFiles)", StringComparison.Ordinal);
        Assert.True(check >= 0 && check < start.IndexOf("OnRescueExit(process, exitCode, loadStep);", StringComparison.Ordinal));

        var stop = SourceFiles.Method(SourceFiles.Read(Path.Combine("src", "BurningEmpiresHost", "MainWindow.Rescue.cs")), "private bool IsMissingModuleFilesStop(");
        Assert.Contains("BeServerModuleRepair.MissingAtExit(", stop);
        Assert.DoesNotContain("OnRescueExit(", stop);
        Assert.Contains("CancelRescueRetry();", stop);
        Assert.Contains("_rescueRun.Begin(RescueMode.Auto);", stop);
        Assert.Contains("_rescueNextMode = RescueMode.Auto;", stop);
    }
}
