using System.Text.RegularExpressions;
using BannerlordCoopLauncher.Core.Updates;
using BurningEmpires.Core;

namespace BannerlordCoopLauncher.Tests;

// The Burning Empires server tool (src/BurningEmpiresHost) is a copy of the TAOM server tool with the
// start and install rewritten for the Workshop Coop dedicated server: these check how it is wired.
public sealed class BurningEmpiresHostTests
{
    private static readonly string Project = Path.Combine(RepoRoot(), "src", "BurningEmpiresHost");

    private static string Window() => SourceFiles.Window(RepoRoot(), "BurningEmpiresHost");

    private static string Csproj() => File.ReadAllText(Path.Combine(Project, "BurningEmpiresHost.csproj"));

    [Fact]
    public void Csproj_names_the_tool_and_matches_the_release_version()
    {
        var csproj = Csproj();
        Assert.Contains("<AssemblyName>Coodsz Burning Empires Server Tool</AssemblyName>", csproj);
        Assert.Contains("<Product>Coodsz Burning Empires Server Tool</Product>", csproj);
        var version = Regex.Match(csproj, "<Version>([0-9.]+)</Version>").Groups[1].Value;
        Assert.Equal(ReleaseNotice.BeVersion.TrimStart('v'), version);
        Assert.Contains(@"..\BurningEmpires.Core\BurningEmpires.Core.csproj", csproj);
    }

    [Fact]
    public void Csproj_bundles_the_compat_module_and_UIExtenderEx_in_ServerAddOns()
    {
        var csproj = Csproj();
        Assert.Contains(@"..\..\compat\BurningEmpiresCoopCompat\module\**\*", csproj);
        Assert.Contains(@"Payload=""ServerAddOns"" PayloadFolder=""BurningEmpires.CoopCompat\""", csproj);
        Assert.Contains(@"..\..\" + BeStack.ServerUiExtenderExBundlePath, csproj);
        Assert.Contains(@"<CoodszPayload Include=""$(BeUIExtenderExDll)"" Payload=""ServerAddOns"" PayloadFolder=""UIExtenderEx-2.13.3\""", csproj);
        // A checkout without the DLL still builds, and says so.
        Assert.Contains("Condition=\"Exists('$(BeUIExtenderExDll)')\"", csproj);
        Assert.Contains("Condition=\"!Exists('$(BeUIExtenderExDll)')\"", csproj);
        Assert.Contains("CoopSaveRescue.dll", csproj);
        Assert.DoesNotContain("WarSync", csproj);
    }

    [Fact]
    public void The_window_finds_the_add_ons_where_the_csproj_puts_them()
    {
        var source = Window();
        Assert.Contains("Path.Combine(ToolPayload.Folder(\"ServerAddOns\"), BeStack.CompatModuleId)", source);
        Assert.Contains("Path.Combine(ToolPayload.Folder(\"ServerAddOns\"), \"UIExtenderEx-\" + BeServerPath.UIExtenderExVersion, BeServerPath.UIExtenderExDll)", source);
        Assert.Equal("2.13.3", BeServerPath.UIExtenderExVersion);
        Assert.Equal("Bannerlord.UIExtenderEx.dll", BeServerPath.UIExtenderExDll);
    }

    [Fact]
    public void The_TAOM_only_parts_are_left_out()
    {
        Assert.False(File.Exists(Path.Combine(Project, "MainWindow.WarSync.cs")));
        Assert.False(Directory.Exists(Path.Combine(Project, "WarSync")));
        var source = Window();
        foreach (var gone in new[] { "InstallWarSync", "TaomStartupCheck", "GameplaySettingsEditor", "taom-coop-settings.json", "EnsureHostKit", "Prepare-Host.ps1", "HostFolders.MoveToCurrentKit", "TaomServerPath.Plan", "TaomPlus" })
            Assert.DoesNotContain(gone, source);
    }

    [Fact]
    public void Prepare_and_start_go_through_the_Burning_Empires_server_path()
    {
        var source = Window();
        var prepare = SourceFiles.Method(source, "private async Task<bool> PrepareServerAsync(");
        Assert.Contains("BeServerPrepare.Prepare(", prepare);
        Assert.Contains("ServerFramework.Ensure(serverRoot, createDotnetHome: true)", prepare);
        Assert.Contains("BeServerPrepare.EnsureData(", prepare);
        Assert.Contains("BeServerPrepare.WritePolicy(", prepare);

        var start = SourceFiles.Method(source, "private async Task StartServerCoreAsync(");
        Assert.Contains("BeServerPrepare.MissingPiece(server)", start);
        Assert.DoesNotContain("BannerlordCoopServer.exe", start);
        // D3: the tools follow the Workshop Coop, so no pinned Coop server bin.
        Assert.DoesNotContain("EnsurePinnedServerBin", source);
        Assert.DoesNotContain("RestoreQualifiedDll", source);
        Assert.Contains("PrepareBeServerAsync(server, new BeStartOptions", start);

        var stage = SourceFiles.Method(source, "private async Task<BeServerStart?> PrepareBeServerAsync(");
        Assert.Contains("BeServerPath.CoreProblems(", stage);
        Assert.Contains("BeServerPath.StageCore(", stage);
        Assert.Contains("BeServerPath.StageExtras(", stage);
        Assert.Contains("BeServerPath.WriteOrder(", stage);
        // The Coop copy after a Coop update runs at the top of the start (RefreshServerFolderAsync).
        Assert.Contains("BeServerPrepare.CoopChanged(", SourceFiles.Method(source, "private async Task<bool> RefreshServerFolderAsync(string server)"));
        Assert.Contains("BeServerLaunch.Build(", stage);
    }

    [Fact]
    public void The_console_log_lines_carry_the_BE_host_prefix()
    {
        var source = Window();
        Assert.Contains("\"[BE host] Server exited.\"", source);
        Assert.DoesNotContain("[TAOM host]", source);
    }

    [Fact]
    public void Start_refuses_the_template_and_a_save_that_is_not_Burning_Empires()
    {
        var start = SourceFiles.Method(Window(), "private async Task StartServerCoreAsync(");
        Assert.Contains("BeServerLaunch.IsTemplate(save)", start);
        Assert.Contains("BeServerLaunch.TemplateReason", start);
        Assert.Contains("BeSaveVersion.IsBeSave(save)", start);
        Assert.Contains("BeServerLaunch.NewCampaignProblem(newCampaign, ServerSavesDirectory(), BeServerLaunch.ReadServerScript(server))", start);
        Assert.Contains("NewCampaign = newCampaign != null", start);
    }

    [Fact]
    public void A_new_campaign_needs_only_a_name()
    {
        var source = Window();
        var click = SourceFiles.Method(source, "private void NewCampaign_Click(");
        Assert.Contains("BeServerLaunch.NewCampaignProblem(name, ServerSavesDirectory(), BeServerLaunch.ReadServerScript(_settings.ServerRoot))", click);
        Assert.Contains("_settings.NewCampaignName = name", click);
        var xaml = File.ReadAllText(Path.Combine(Project, "MainWindow.xaml"));
        Assert.Contains("Click=\"NewCampaign_Click\"", xaml);
        Assert.Contains("x:Name=\"NewCampaignBox\"", xaml);

        var games = Path.Combine(Path.GetTempPath(), "be-host-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(games);
        try
        {
            Assert.Null(BeServerLaunch.NewCampaignProblem("BETest", games, null));
            Assert.NotNull(BeServerLaunch.NewCampaignProblem("default_new_game", games, null));
            File.WriteAllText(Path.Combine(games, "BETest.sav"), "");
            Assert.NotNull(BeServerLaunch.NewCampaignProblem("BETest", games, null));
        }
        finally
        {
            Directory.Delete(games, recursive: true);
        }
    }

    [Fact]
    public void Rename_delete_and_backups_carry_BESavedata()
    {
        var source = Window();
        var rename = SourceFiles.Method(source, "private void NameSave_Click(");
        Assert.Contains("BeSaveData.Rename(serverModule, selected.Name, name)", rename);
        Assert.Contains("CarryBeSaveData(", rename);
        Assert.Contains("BeSaveData.Delete(", SourceFiles.Method(source, "private void DeleteSave_Click("));
        Assert.Contains("BackupBeSaveData(", SourceFiles.Method(source, "private void NoteSavedWorld("));
        Assert.Contains("BackupBeSaveData(", SourceFiles.Method(source, "private async Task BackupBeforeFixesAsync("));
        Assert.Contains("RestoreBeSaveData(", SourceFiles.Method(source, "private void RestoreSaveBackup_Click("));
        Assert.Contains("BeSaveData.RestoreBlocked(", SourceFiles.Method(source, "private async void RestartLastGood_Click("));
    }

    [Fact]
    public void Save_to_a_new_name_shows_with_the_save_safety_warning()
    {
        var source = Window();
        Assert.Contains("\"this Burning Empires build migrates older saves\"", source);
        Assert.Contains("ShowSaveToNewName(running);", SourceFiles.Method(source, "private void RefreshState()"));
        var xaml = File.ReadAllText(Path.Combine(Project, "MainWindow.xaml"));
        Assert.Contains("x:Name=\"SaveToNewNameBar\"", xaml);
        Assert.Contains("Click=\"SaveToNewName_Click\"", xaml);
    }

    [Fact]
    public void Battle_size_goes_to_Coop_mod_config()
    {
        var source = Window();
        Assert.Contains("CoopModConfig.SetBattleSize(json, size)", SourceFiles.Method(source, "private string SaveBattleSize("));
        Assert.Contains("json = SaveBattleSize(json);", SourceFiles.Method(source, "private void SaveTimePermissions()"));
    }

    [Fact]
    public void Reports_logs_and_settings_use_the_be_names()
    {
        var source = Window();
        var request = SourceFiles.Method(source, "private CrashReportRequest CrashRequest(");
        Assert.Contains("Tool = \"be\"", request);
        Assert.Contains("FileStem = \"be-host-logs\"", request);
        Assert.Contains("BeHostLogPack.Create(", request);
        Assert.Contains("new Coodsz.Ui.ServerCrashBar(this, CrashBarHost, \"be\",", source);
        Assert.Contains("BeServerFolders.SettingsFile", source);
        Assert.Equal("be-server.json", BeServerFolders.SettingsFile);
        Assert.Contains("\"be-bans.json\"", source);
        Assert.Contains("\"be-admin-log.json\"", File.ReadAllText(Path.Combine(Project, "CoopCheatsPage.xaml.cs")));
        Assert.Contains("GuideSet.BeServer", source);
        Assert.Contains("WatchForUpdateAsync(ReleaseNotice.BeRepo, ReleaseNotice.BeVersion)", source);
        Assert.Contains("ClientToolName = \"Coodsz Burning Empires Client Launcher\"", source);
        Assert.Contains("Taom = false", source);

        var app = File.ReadAllText(Path.Combine(Project, "App.xaml.cs"));
        Assert.Contains("WhatsNewOnce(\"be-server\", ReleaseNotice.BeVersion, WhatsNew.Be)", app);
        Assert.DoesNotContain("taom", app, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void No_TAOM_text_reaches_the_window()
    {
        var xaml = File.ReadAllText(Path.Combine(Project, "MainWindow.xaml"));
        Assert.DoesNotContain("TAOM", xaml);
        // The Made for card lists the versions as rows (ShowMadeFor) and keeps the known limits as its notes.
        Assert.Contains("<ui:MadeForCard x:Name=\"MadeForCard\"", xaml);
        var madeFor = SourceFiles.Method(File.ReadAllText(Path.Combine(Project, "MainWindow.Frame.cs")), "private void ShowMadeFor()");
        Assert.Contains("BeStack.BeVersion", madeFor);
        Assert.Contains("Windows only.", madeFor);
        Assert.Contains("black terrain", madeFor);

        // The string literals of the code: only class names from TaomCoop.Core may say Taom, never a sentence.
        foreach (var file in Directory.GetFiles(Project, "*.cs"))
        {
            foreach (Match literal in Regex.Matches(File.ReadAllText(file), "\"(?:\\\\.|[^\"\\\\\\r\\n])*\""))
                Assert.False(literal.Value.Contains("TAOM", StringComparison.Ordinal), Path.GetFileName(file) + " shows " + literal.Value);
        }
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "BannerlordCoopLauncher.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
