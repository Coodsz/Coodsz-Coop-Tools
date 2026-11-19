using System.Text.RegularExpressions;
using BannerlordCoopLauncher.Core.Parsing;
using BannerlordCoopLauncher.Core.Updates;
using BurningEmpires.Core;
using TaomCoop.Core;

namespace BannerlordCoopLauncher.Tests;

// The Burning Empires client launcher (src\BurningEmpiresLauncher) is a copy of the TAOM launcher with
// the Burning Empires rules: no Low FPS fix, patch shield or dependency fix, Install or repair from
// the player's own Nexus downloads, BUTR from the Workshop, and "be" on every report.
public sealed class BurningEmpiresLauncherTests
{
    private const string Project = "BurningEmpiresLauncher";

    private static string Window() => SourceFiles.Window(RepoRoot(), Project);

    private static string File_(string name) => File.ReadAllText(Path.Combine(RepoRoot(), "src", Project, name));

    [Fact]
    public void The_client_has_the_burning_empires_name_and_the_release_notice_version()
    {
        var csproj = File_(Project + ".csproj");
        Assert.Contains("<AssemblyName>Coodsz Burning Empires Client Launcher</AssemblyName>", csproj);
        Assert.Contains("<Product>Coodsz Burning Empires Client Launcher</Product>", csproj);
        var version = Regex.Match(csproj, "<Version>([^<]+)</Version>").Groups[1].Value;
        Assert.Equal(ReleaseNotice.BeVersion.TrimStart('v'), version);
        Assert.Contains(@"..\BurningEmpires.Core\BurningEmpires.Core.csproj", csproj);
        Assert.Contains(@"..\TaomCoopHost\TaomGuidesPage.cs", csproj);
        Assert.DoesNotContain("Coodsz TAOM", csproj);
    }

    [Fact]
    public void The_client_bundles_the_join_password_and_the_compat_module_the_core_expects()
    {
        var csproj = File_(Project + ".csproj");
        Assert.Contains(@"compat\CoodszJoinPassword\module\**\*"" Payload=""ClientAddOns"" PayloadFolder=""DedicatedServer.CoodszJoinPassword\""", csproj);
        Assert.Contains(@"compat\BurningEmpiresCoopCompat\module\**\*"" Exclude=""..\..\compat\BurningEmpiresCoopCompat\module\README.txt"" Payload=""ClientAddOns"" PayloadFolder=""BurningEmpires.CoopCompat\""", csproj);

        var xml = Path.Combine(RepoRoot(), "compat", "BurningEmpiresCoopCompat", "module", "SubModule.xml");
        var identity = ModuleIdentityReader.Read(xml);
        Assert.Equal(BeStack.CompatModuleId, identity.Id);
        Assert.Equal(BeStack.CompatVersion, identity.Version);
    }

    [Fact]
    public void The_low_fps_fix_and_the_dependency_fix_are_gone_and_the_prefabs_fix_stays()
    {
        var window = Window();
        Assert.DoesNotContain("Patchshield", window);
        Assert.DoesNotContain("LowFps", window);
        Assert.DoesNotContain("TaomDependencyFix", window);
        Assert.DoesNotContain("UndoDependencyFix", window);
        Assert.Contains("UIExtenderExPrefabsFix.ApplyForLaunch(BeLoadOrder.LaunchModules(", window);
        Assert.Contains("UIExtenderExPrefabsFix.RestoreAll(", window);
        Assert.Contains("RestorePrefabsFix()", SourceFiles.Method(window, "private void RestoreWhenClosed("));

        var xaml = File_("MainWindow.xaml");
        Assert.DoesNotContain("LowFpsButton", xaml);
        Assert.DoesNotContain("Undo dependency fix", xaml);
    }

    [Fact]
    public void The_window_uses_the_burning_empires_rules_and_settings_not_the_taom_ones()
    {
        var sources = Window() + File_("ModListWindow.cs");
        foreach (var taom in new[] { "TaomLoadOrder", "TaomStack", "TaomSharedList", "TaomCompatList", "TaomModList.", "TaomModIds", "TaomSetupBook", "TaomClientSettings", "TaomArchive", "TaomModResults", "TaomServerFolders", "ClientLogPack.Create(", "taom-launcher" })
            Assert.False(Regex.IsMatch(sources, "(?<![A-Za-z])" + Regex.Escape(taom)), taom + " is still used.");
        Assert.Contains("\"be-launcher.json\"", sources);
        Assert.Contains("BeClientSettings.Load(_settingsPath)", sources);
        Assert.Contains("BeLoadOrder.LaunchModules(setup, InstallJoinPassword())", sources);
        Assert.Contains("BeModResults.Record(results)", sources);
        // D3: the tools follow the Workshop Coop, so the Coop 0.1.6 file pin is not put back.
        Assert.DoesNotContain("RestoreQualifiedDll", sources);
        Assert.DoesNotContain("UsePlaytestCoop", sources);
    }

    [Fact]
    public void Reports_crash_reports_and_log_packs_go_out_as_be()
    {
        var window = Window();
        Assert.Equal(3, Regex.Matches(window, "Tool = \"be\"").Count);
        Assert.DoesNotContain("\"taom\"", window);
        Assert.Contains("FileStem = \"be-client-logs\"", window);
        Assert.Contains("BeClientLogPack.Create(zip, gameRoot", window);
        Assert.Contains("BeServerFolders.OnThisPc(AppContext.BaseDirectory)", window);
        Assert.Contains("CommunityCompat.RefreshAsync(\"CoodszBurningEmpiresClientLauncher\")", window);
    }

    [Fact]
    public void Core_modules_are_found_in_every_workshop_item_so_butr_is_not_missing()
    {
        var window = Window();
        var find = SourceFiles.Method(window, "private string? FindModuleXml(string gameRoot, string moduleId)");
        Assert.Contains("_workshopXml.TryGetValue(moduleId", find);
        Assert.DoesNotContain("CoopStaging.FindWorkshopModule", find);
        var scan = SourceFiles.Method(window, "private void ScanWorkshopModules()");
        Assert.Contains("foreach (var library in Workshops())", scan);
        Assert.Contains("Directory.EnumerateDirectories(library.ContentDirectory)", scan);
        Assert.Contains("ScanWorkshopModules();", SourceFiles.Method(window, "private void ScanInstalledExtras()"));
    }

    [Fact]
    public void Install_or_repair_unpacks_the_downloads_then_puts_in_the_compat_module_then_gets_butr()
    {
        var window = Window();
        var install = SourceFiles.Method(window, "private async void InstallOrRepair_Click(");
        var order = new[] { "BeArchive.ContentProblem(gameRoot)", "UnpackBe(archives", "BeArchive.InstallCompat(addOn, gameRoot)", "await GetButrAsync()", "AskOpenNexus(nexusReason, nexusHotfixOnly)" }
            .Select(text => install.IndexOf(text, StringComparison.Ordinal))
            .ToList();
        Assert.All(order, at => Assert.True(at >= 0));
        Assert.Equal(order.OrderBy(at => at), order);
        Assert.Contains("ClientAddOns.Payload(BeStack.CompatModuleId)", install);

        // The overlay never deletes a module folder, and a failed unpack sends the player to Nexus.
        var unpack = SourceFiles.Method(window, "private bool UnpackBe(");
        Assert.Contains("BeArchive.InstallOverlay(archives, gameRoot, scratch, progress)", unpack);
        Assert.Contains("catch (BeArchiveException ex)", unpack);
        Assert.DoesNotContain("Directory.Delete", unpack);
        Assert.Contains("BeStack.BeNexusPage", SourceFiles.Method(window, "private void OpenNexusPage(bool hotfixOnly)"));

        var butr = SourceFiles.Method(window, "private async Task<bool> GetButrAsync()");
        Assert.Contains("await SubscribeWorkshopAsync(names)", butr);
        Assert.Contains("WorkshopDownload.RunAsync(", SourceFiles.Method(window, "private async Task<WorkshopSubscribeResult?> SubscribeWorkshopAsync("));
    }

    [Fact]
    public void Connect_gets_missing_butr_from_the_workshop_and_warns_about_version_drift()
    {
        var window = Window();
        Assert.Contains("CoreProblems(withButr: false).Count == 0", SourceFiles.Method(window, "private void UpdateConnectButton()"));
        Assert.Matches(@"ServerNeeds\(\) =>\s+MissingButr\(\)\s+\.Select\(BeSharedList\.ButrNeed\)", window);
        Assert.Contains("await GetButrAsync()", SourceFiles.Method(window, "private async Task<bool> GetServerModsFirstAsync()"));

        var connect = SourceFiles.Method(window, "private async Task ConnectAsync()");
        Assert.True(connect.IndexOf("WarnVersionDrift();", StringComparison.Ordinal) is var warn && warn > 0
            && warn < connect.IndexOf("StartWithPrefabsFix(setup, start)", StringComparison.Ordinal));
        var drift = SourceFiles.Method(window, "private void WarnVersionDrift()");
        Assert.Contains("mod.TestedVersion", drift);
        Assert.Contains("HostDriftWarnings()", drift);
        Assert.Contains("ReadHostList()", SourceFiles.Method(window, "private List<string> HostDriftWarnings()"));
        Assert.Contains("RememberHostList(shared);", SourceFiles.Method(window, "private async void ImportList_Click("));
    }

    [Fact]
    public void A_taom_mod_list_is_refused_before_a_setup_is_made_for_it()
    {
        var import = SourceFiles.Method(Window(), "private async void ImportList_Click(");
        var foreign = import.IndexOf("BeSharedList.ForeignGame(shared)", StringComparison.Ordinal);
        Assert.True(foreign > 0);
        Assert.True(foreign < import.IndexOf("_settings.ModSetups.Create()", StringComparison.Ordinal));

        var taomList = new[] { new BannerlordCoopLauncher.Core.Modules.SharedModEntry("TAOM", "TAOM", "v2.0.29.5", "0") };
        Assert.NotNull(BeSharedList.ForeignGame(taomList));
    }

    [Fact]
    public void Find_the_crashing_mod_never_blames_elephants_because_it_is_core()
    {
        // Elephants is in the Burning Empires core now, so a copy of the locked setup has no extra
        // mod to suspect and every round loads Elephants with the core.
        var book = new BeSetupBook();
        book.Ensure();
        var copy = book.CopyOf(BeSetupBook.CoreName);
        Assert.Empty(copy.Extras);
        Assert.Throws<InvalidOperationException>(() => CrashBisect.Start(copy));
        Assert.Contains(BeStack.ElephantsModuleId, BeLoadOrder.ClientOrder(copy));

        var find = SourceFiles.Method(Window(), "private async void FindCrash_Click(");
        Assert.Contains("CrashBisect.Start(setup,", find);
        Assert.Contains("TestToMainMenuAsync(search.TestSetup())", find);
        Assert.DoesNotContain("Elephants(", find);
    }

    [Fact]
    public void Join_your_own_server_reads_the_data_folder_of_the_burning_empires_server()
    {
        var window = Window();
        Assert.Contains("ReadOwnServerFromBeData();", SourceFiles.Method(window, "public MainWindow()"));
        Assert.Contains("LocalServer.ExtraDataFolders = () => BurningEmpires.Core.BeServerFolders.OnThisPc(AppContext.BaseDirectory)", window);
        var guide = SourceFiles.Method(window, "private async void JoinHelp_Click(");
        Assert.Contains("ClientTool = \"Coodsz Burning Empires Client Launcher\"", guide);
        Assert.Contains("ServerTool = \"Coodsz Burning Empires Server Tool\"", guide);
        Assert.Contains("Taom = false", guide);
    }

    [Fact]
    public void Text_guides_and_the_startup_notes_are_for_burning_empires()
    {
        var window = Window();
        Assert.Contains("Connected. The game is joining the Burning Empires server.", window);
        Assert.Contains("GuideLibrary.For(GuideSet.BeClient)", window);
        Assert.Contains("ReleaseNotice.BeRepo, ReleaseNotice.BeVersion", window);
        var start = SourceFiles.Method(window, "public MainWindow()");
        Assert.Contains("Log(KnownLimits);", start);
        Assert.Contains("War Sails and the Historical campaign start are not supported", window);

        var app = File_("App.xaml.cs");
        Assert.Contains("WhatsNewOnce(\"be-launcher\", ReleaseNotice.BeVersion, WhatsNew.Be)", app);

        // No TAOM in anything the player reads; the XAML comments may still name the TAOM tools.
        var xaml = File_("MainWindow.xaml");
        Assert.Contains("Title=\"Coodsz Burning Empires Client Launcher\"", xaml);
        Assert.Contains("Content=\"Install or repair Burning Empires\"", xaml);
        foreach (Match attribute in Regex.Matches(xaml, "(?:Text|ToolTip|Content|Header|Title)=\"([^\"]*)\""))
            Assert.DoesNotContain("TAOM", attribute.Groups[1].Value);
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "BannerlordCoopLauncher.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
