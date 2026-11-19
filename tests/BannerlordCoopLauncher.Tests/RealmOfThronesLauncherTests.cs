using System.Text.RegularExpressions;
using BannerlordCoopLauncher.Core.IO;
using BannerlordCoopLauncher.Core.Joining;
using BannerlordCoopLauncher.Core.Launch;
using BannerlordCoopLauncher.Core.Modules;
using BannerlordCoopLauncher.Core.Support;
using BannerlordCoopLauncher.Core.Updates;
using BurningEmpires.Core;
using RealmOfThrones.Core;
using RealmOfThrones.Ui;
using TaomCoop.Core;

namespace BannerlordCoopLauncher.Tests;

// The Realm of Thrones Client Launcher (src/RealmOfThronesLauncher), a copy of the Burning Empires one on the Coop nightly.
// The window is checked by its source like the Burning Empires launcher tests; the logic it calls is tested as code.
public class RealmOfThronesLauncherTests
{
    private static string Window() => SourceFiles.Window(SourceFiles.Root(), "RealmOfThronesLauncher");

    private static string File_(string name) => SourceFiles.Read(Path.Combine("src", "RealmOfThronesLauncher", name));

    private static void InOrder(string source, params string[] steps)
    {
        var at = -1;
        foreach (var step in steps)
        {
            var next = source.IndexOf(step, at + 1, StringComparison.Ordinal);
            Assert.True(next > at, step + " is missing or out of order.");
            at = next;
        }
    }

    // Every core module at the version the set needs.
    private static Dictionary<string, string?> AllReady() =>
        RotStack.ClientModules.ToDictionary(id => id, id => (string?)(RotStack.ContentVersions.TryGetValue(id, out var version) ? version
            : id == RotStack.Mcm.ModuleId ? "v5.13.1" : "v1.0.0"), StringComparer.OrdinalIgnoreCase);

    private static IReadOnlyList<string> Core(Dictionary<string, string?> versions, bool withWorkshopLibraries = true) =>
        RotJoinCheck.CoreProblems(id => versions.GetValueOrDefault(id), withWorkshopLibraries);

    private static List<SharedModEntry> HostList(params (string Id, string Version)[] extras) =>
        RotStack.ClientModules.Select(id => new SharedModEntry(id, id, AllReady()[id]!, "0"))
            .Concat(extras.Select(extra => new SharedModEntry(extra.Id, extra.Id, extra.Version, "0")))
            .ToList();

    private static Dictionary<string, string> Installed(IEnumerable<SharedModEntry> list) =>
        list.ToDictionary(entry => entry.Id, entry => entry.Version, StringComparer.OrdinalIgnoreCase);

    // ----- Project, names and registration -----

    [Fact]
    public void Csproj_names_the_launcher_its_version_and_its_own_icon()
    {
        var project = File_("RealmOfThronesLauncher.csproj");
        Assert.Contains("<AssemblyName>Coodsz Realm of Thrones Client Launcher</AssemblyName>", project);
        Assert.Equal(RotRelease.Version, "v" + Regex.Match(project, "<Version>([^<]+)</Version>").Groups[1].Value);
        Assert.Contains(@"<ApplicationIcon>..\..\assets\icons\coodsz-rot-client.ico</ApplicationIcon>", project);
        Assert.Contains(@"RealmOfThrones.Core\RealmOfThrones.Core.csproj", project);
        Assert.Contains(@"..\RealmOfThronesHost\RotUiText.cs", project);
        Assert.DoesNotContain("BurningEmpires", project);
        Assert.NotEqual(File.ReadAllBytes(Path.Combine(SourceFiles.Root(), "assets", "icons", "coodsz-be-client.ico")),
            File.ReadAllBytes(Path.Combine(SourceFiles.Root(), "assets", "icons", "coodsz-rot-client.ico")));

        var client = ToolNames.All.Single(item => item.Product == "RealmOfThrones" && item.Role == ToolRole.Client);
        var server = ToolNames.All.Single(item => item.Product == "RealmOfThrones" && item.Role == ToolRole.Server);
        Assert.Equal("Coodsz Realm of Thrones Client Launcher.exe", client.NewExeName);
        Assert.Equal(server.NewZipPrefix, client.NewZipPrefix);
    }

    [Fact]
    public void The_app_starts_in_its_own_data_folder_with_its_own_client_colours()
    {
        var app = File_("App.xaml.cs");
        Assert.Contains("ToolData.Start(ToolData.RotClient)", app);
        Assert.Contains("WhatsNewOnce(\"rot-launcher\", ReleaseNotice.RotVersion, WhatsNew.Rot)", app);
        Assert.Contains("FrameRules.RotClient", app);
        Assert.Equal(new Coodsz.Ui.ToolAccentColors(Coodsz.Ui.FrameRules.RotClient, "coodsz-rot-client.svg", "#2E1D1F", "#E8736B"),
            Coodsz.Ui.FrameRules.Accent(Coodsz.Ui.FrameRules.RotClient));
        Assert.Contains("rot-launcher.json", ToolData.RotClient.OldFiles);
        Assert.Equal(RotClientSettings.FileName, "rot-launcher.json");
    }

    [Fact]
    public void The_window_uses_the_realm_of_thrones_rules_not_the_burning_empires_or_taom_ones()
    {
        var window = Window();
        foreach (var other in new[] { "BeStack", "BeLoadOrder", "BeSharedList", "BeSetupBook", "BeClientSettings", "BeModChecks", "BurningEmpires.", "be-launcher", "be-host-mod-list",
                     "TaomLoadOrder", "TaomStack", "TaomSharedList", "TaomSetupBook", "TaomClientSettings", "taom-launcher" })
            Assert.False(Regex.IsMatch(window, "(?<![A-Za-z])" + Regex.Escape(other)), other + " is still used.");
        Assert.Contains("RotClientSettings.Load(_settingsPath)", window);
        Assert.Contains("ToolData.File(\"rot-host-mod-list.json\")", window);
        Assert.Contains("RotModChecks.Run(setup, GatherModFacts())", window);
        Assert.Contains("Connected. The game is joining the Realm of Thrones server.", window);

        // Nothing the player reads names another overhaul.
        var xaml = File_("MainWindow.xaml");
        Assert.Contains("Title=\"Coodsz Realm of Thrones Client Launcher\"", xaml);
        foreach (Match attribute in Regex.Matches(xaml, "(?:Text|ToolTip|Content|Header|Title)=\"([^\"]*)\""))
        {
            Assert.DoesNotContain("TAOM", attribute.Groups[1].Value);
            Assert.DoesNotContain("Burning Empires", attribute.Groups[1].Value);
        }
    }

    // ----- Setups -----

    [Fact]
    public void A_fresh_install_has_only_the_locked_plain_setup_and_it_can_only_be_copied()
    {
        var settings = RotClientSettings.Parse("");
        var book = settings.ModSetups;
        var plain = Assert.Single(book.Setups);
        Assert.Equal(RotSetupBook.CoreName, plain.Name);
        Assert.True(plain.Locked);
        Assert.Equal(RotSetupBook.CoreName, book.Active);
        Assert.Equal(RotStack.ClientModules, RotLoadOrder.ClientOrder(plain));
        Assert.Throws<InvalidOperationException>(() => book.Rename(RotSetupBook.CoreName, "Mine"));
        Assert.Throws<InvalidOperationException>(() => book.Delete(RotSetupBook.CoreName));
        Assert.Throws<InvalidOperationException>(() => RotLoadOrder.Add(plain, "Extra", "MyExtra", "123"));

        var copy = book.CopyOf(RotSetupBook.CoreName);
        Assert.False(copy.Locked);
        Assert.Equal(copy.Name, book.Active);
        Assert.Null(RotLoadOrder.Add(copy, "My extra", "MyExtra", "123"));
        book.Rename(copy.Name, "Friends");
        Assert.Equal("Friends", book.Active);
        var made = book.Create();
        Assert.Equal(made.Name, book.Active);
        book.Delete(made.Name);
        Assert.Equal(RotSetupBook.CoreName, book.Active);

        // The saved file reads back with the same setups, the locked one first.
        var again = RotClientSettings.Parse(settings.Serialize());
        Assert.Equal(new[] { RotSetupBook.CoreName, "Friends" }, again.ModSetups.Setups.Select(setup => setup.Name));
        Assert.Equal("MyExtra", Assert.Single(again.ModSetups.Find("Friends")!.Extras).ModuleId);
    }

    [Fact]
    public void The_setup_buttons_follow_the_lock_and_the_locked_line_is_the_server_tools()
    {
        var window = Window();
        var controls = SourceFiles.Method(window, "private void ShowSetupControls()");
        Assert.Contains("RenameSetupButton.IsEnabled = !locked;", controls);
        Assert.Contains("DeleteSetupButton.IsEnabled = !locked;", controls);
        Assert.Contains("AddModsButton.IsEnabled = !locked;", controls);
        Assert.Contains("InstallModsButton.IsEnabled = !locked;", controls);
        // The greyed buttons say to click Copy, and Add mods is not gold on the locked setup.
        Assert.Contains("AddModsButton.Style = (Style)FindResource(locked ? \"Ghost\" : \"Primary\");", controls);
        Assert.Contains("ToolTipService.SetShowOnDisabled(button, true);", controls);
        Assert.Contains("\"This setup is locked. Click Copy on the Mods page to make one you can change.\"", controls);
        Assert.Contains("LockedSetupText.Text = RotSetupBook.LockedNote(_editing.Name);", controls);
        Assert.Contains("_settings.ModSetups.Create()", SourceFiles.Method(window, "private void NewSetup_Click("));
        Assert.Contains("_settings.ModSetups.CopyOf(from)", SourceFiles.Method(window, "private void CopySetup_Click("));
        Assert.Contains("_settings.ModSetups.Rename(from, to)", SourceFiles.Method(window, "private void RenameSetup_Click("));
        Assert.Contains("_settings.ModSetups.Delete(setup.Name)", SourceFiles.Method(window, "private void DeleteSetup_Click("));
        Assert.Contains("RotLoadOrder.Add(_editing, mod.Name, mod.ModuleId, mod.WorkshopId)", SourceFiles.Method(window, "private void AddMods_Click("));
        Assert.Contains("RotSharedList.Missing(_editing, _installedExtras.Keys)", SourceFiles.Method(window, "private async void InstallMods_Click("));

        // The Mods page shows the core up to ROT-Dragon, the extras, then the compat module last.
        InOrder(SourceFiles.Method(window, "private void ShowModuleList()"),
            "RotLoadOrder.CoreBeforeExtras(RotStack.ClientModules)", "_editing.Extras.Count", "CoreRow(number, RotStack.CompatModuleId)");
        foreach (var button in new[] { "Content=\"New setup\"", "Content=\"Copy\"", "Content=\"Rename\"", "Content=\"Delete\"", "Content=\"Add mods\"", "Content=\"Install mods\"", "Content=\"Import mod list\"", "Header=\"Share mod list\"" })
            Assert.Contains(button, File_("MainWindow.xaml"));
        // Import mod list is a first run step, so it sits in view on the Mods page and not in More.
        Assert.DoesNotContain("Header=\"Import mod list\"", File_("MainWindow.xaml"));
    }

    // ----- Connect -----

    [Fact]
    public void Connect_gets_the_server_mods_first_then_checks_the_core_then_the_host_list()
    {
        var connect = SourceFiles.Method(Window(), "private async Task ConnectAsync()");
        InOrder(connect,
            "if (!await PutInCoopAsync())",
            "if (!ConfirmModChanges())",
            "ready = await GetServerModsFirstAsync()",
            "if (!InstallCompat())",
            "var problems = ModuleProblems();",
            "ShowJoin(problems[0]);",
            "if (!HostListMatches())",
            "if (!ModChecksPass(LaunchSetup))",
            "await CheckDirectJoinAsync(",
            "RotLoadOrder.LaunchModules(setup, InstallJoinPassword())",
            "StartWithPrefabsFix(setup, start)",
            "Log(RotUiText.FirstJoinWait);");

        // The exact Coop build is the first line of ModuleProblems, against the host's list (or the pinned build).
        InOrder(SourceFiles.Method(Window(), "private List<string> ModuleProblems()"),
            "CoreModuleXml(RotStack.CoopModuleId) is { } coop",
            "var thisPc = CoopBuildId.FromModule(Path.GetDirectoryName(coop));",
            "RotJoinCheck.CoopBuildProblem(thisPc, host, CarriesCoop()) is { } build",
            "problems.Insert(0, build);",
            "RotJoinCheck.SameCoopBuild(thisPc, host) is { } same",
            "Log(same);");
        // Check mods only looks: a kept pinned Coop shows as the build line there, and only Connect moves it back.
        var checkMods = SourceFiles.Method(Window(), "private void CheckMods_Click(object sender, RoutedEventArgs e)");
        Assert.Contains("var problems = ModuleProblems();", checkMods);
        Assert.DoesNotContain("RestoreKeptCoop", checkMods);

        // Every check that fails stops Connect right there.
        foreach (var check in new[] { "if (!await PutInCoopAsync())", "if (!ready)", "if (!InstallCompat())", "if (!HostListMatches())", "if (!ModChecksPass(LaunchSetup))" })
            Assert.Matches(Regex.Escape(check) + @"\s+return;", connect);
        Assert.Matches(@"ShowJoin\(problems\[0\]\);\s+foreach \(var line in problems\.Skip\(1\)\)\s+Log\(line\);\s+return;", connect);

        var window = Window();
        Assert.Contains("CoreProblems(withWorkshopLibraries: false).Count == 0", SourceFiles.Method(window, "private void UpdateConnectButton()"));
        Assert.Matches(@"ServerNeeds\(\) =>\s+MissingLibraries\(\)\s+\.Select\(RotJoinCheck\.LibraryNeed\)", window);
        Assert.Contains("await GetLibrariesAsync()", SourceFiles.Method(window, "private async Task<bool> GetServerModsFirstAsync()"));
        var matches = SourceFiles.Method(window, "private bool HostListMatches()");
        Assert.Contains("RotJoinCheck.Blocking(problems)", matches);
        Assert.Contains("ShowJoin(blocking);", matches);
        Assert.Contains("RotJoinCheck.HostProblems(host, InstalledVersions(host), LaunchOrder(),", window);
    }

    // The carried Coop goes in at Connect and Find the crashing mod (off the UI thread, Connect off meanwhile, the
    // question on the UI thread), comes out at close (after the game, the window waiting minimized in the taskbar) and at
    // the next start after a crash; a build without the zip keeps the kept Coop check. One launcher runs at a time, a
    // Windows log off ends the wait, and a game started a moment ago counts as running.
    [Fact]
    public void The_carried_coop_goes_in_at_connect_and_comes_out_at_close_and_after_a_crash()
    {
        var window = Window();
        var start = SourceFiles.Method(window, "public MainWindow()");
        InOrder(start, "TakeOutAfterCrash();", "Application.Current.SessionEnding += App_SessionEnding;", "_ = PrepareCarriedCoopAsync();", "InitSetups();");
        var putIn = SourceFiles.Method(window, "private async Task<bool> PutInCoopAsync()");
        InOrder(putIn,
            "if (!CarriesCoop())", "Log(RotCoopInstall.NotCarried);", "return RestoreKeptCoop();",
            "_busy = _coopMoving = true;", "UpdateConnectButton();",
            "await Task.Run(() => RotCoopInstall.PutIn(gameRoot, carried.Folder, _coopMarker, gameRunning,",
            "others => Dispatcher.Invoke(() => AskSetAside(others))", "ShowJoin(result.Problem);",
            "catch (Exception ex)", "ShowJoin(RotCoopInstall.CouldNotPutIn(ex.Message));",
            "finally", "_busy = _coopMoving = false;", "UpdateConnectButton();");
        Assert.Contains("&& !_joinChecking && !_coopMoving;", SourceFiles.Method(window, "private void UpdateConnectButton()"));
        // Nothing from the unpack or the put in escapes to async void Connect_Click.
        Assert.Contains("catch (Exception ex)", SourceFiles.Method(window, "private static (string? Folder, string? Problem) UnpackCarriedCoop()"));
        Assert.Contains("catch (Exception ex)", SourceFiles.Method(window, "private async Task PrepareCarriedCoopAsync()"));
        InOrder(SourceFiles.Method(window, "private bool AskSetAside(IReadOnlyList<RotCoopFolder> others)"),
            "CoopParking.Decide(_settings.SetAsideCoop, _setAsideThisRun,", "SetAsideCoopWindow.Ask(this, RotCoopInstall.Question(others))", "_settings.SetAsideCoop = decision.Save;");
        InOrder(SourceFiles.Method(window, "private async void FindCrash_Click(object sender, RoutedEventArgs e)"),
            "if (!await PutInCoopAsync())", "_busy = true;");
        // A second close while the window waits skips the mod changes question and takes the Coop out when the game is gone.
        InOrder(SourceFiles.Method(window, "private void MainWindow_Closing(object? sender, CancelEventArgs e)"),
            "if (_sessionEnding || (_closingAfterGame && _closeWait == null))", "if (_closingAfterGame)", "TakeOutOnClose();",
            "if (!ConfirmModChanges())", "_actions.Note(\"Close\",", "e.Cancel = true;", "if (!TakeOutOnClose())");
        var close = SourceFiles.Method(window, "private bool TakeOutOnClose()");
        InOrder(close,
            "if (_sessionEnding || !RotCoopInstall.HasWork(_coopMarker))", "StopCloseWait();", "RotCoopInstall.CloseStep(_closingAfterGame, reason)",
            "case RotCloseStep.TakeOut:", "TakeOutCoop();", "case RotCloseStep.LeaveForNextStart:", "_actions.Note(\"Close\", \"Waiting for the game to close (\" + reason + \").\");",
            "Title = Translator.T(RotCoopInstall.WaitingTitle);", "ShowJoin(RotCoopInstall.WaitingToTakeOut, problem: false);",
            "WindowState = WindowState.Minimized;", "if (GameWaitReason() != null)", "StopCloseWait();", "TakeOutCoop();", "Application.Current.Shutdown();");
        Assert.DoesNotContain("Hide();", close);
        InOrder(SourceFiles.Method(window, "private void App_SessionEnding(object? sender, SessionEndingCancelEventArgs e)"),
            "_sessionEnding = true;", "StopCloseWait();");
        InOrder(SourceFiles.Method(window, "private string? GameWaitReason()"),
            "exited = _startedGame?.HasExited;", "\"TaleWorlds.MountAndBlade.Launcher\"",
            "RotCoopInstall.WaitReason(ClientLaunchGuard.IsGameRunning(), CoopProgramRunning(), exited, DateTime.UtcNow - _gameStartedAt,");
        // Only a process whose file is an exe in Modules\Coop\bin counts, and the exe list is read once per wait.
        InOrder(SourceFiles.Method(window, "private string? CoopProgramRunning()"),
            "if (_closeWait == null || _coopExes == null)", "Path.Combine(_gameRoot ?? \"\", \"Modules\", RotCoopInstall.FolderName, \"bin\")",
            "Directory.GetFiles(coopBin, \"*.exe\", SearchOption.AllDirectories)", "Process.GetProcessesByName(name)",
            "string.Equals(Path.GetFullPath(process.MainModule?.FileName ?? \"\"), Path.GetFullPath(exe), StringComparison.OrdinalIgnoreCase)",
            "System.ComponentModel.Win32Exception or InvalidOperationException");
        InOrder(SourceFiles.Method(window, "private System.Diagnostics.Process StartWithPrefabsFix(TaomSetup setup, System.Diagnostics.ProcessStartInfo start)"),
            "System.Diagnostics.Process.Start(start);", "_gameStartedAt = DateTime.UtcNow;", "_startedGame = started;");
        // A Coop that stays parked or a marker that cannot be read goes under Connect, and every Coop line into client-action.log.
        InOrder(SourceFiles.Method(window, "private void TakeOutCoop()"),
            "RotCoopInstall.TakeOut(_coopMarker, GameWaitReason() != null)", "LogCoop(line);", "ShowJoin(result.Notice);", "catch (Exception ex)");
        InOrder(SourceFiles.Method(window, "private void LogCoop(string line)"), "Log(line);", "_actions.Note(\"Coop\", line);");
        Assert.Contains("LogCoop(line);", putIn);
        InOrder(File_("App.xaml.cs"), "Coodsz.Ui.UiLanguage.Start();", "new Mutex(true, @\"Local\\CoodszRotClientLauncher\", out var first);",
            "if (!first)", "UiLanguage.Message(AlreadyOpen,", "Environment.Exit(0);", "WhatsNewOnce(\"rot-launcher\",");
        // The rows and Connect read the carried Coop like the carried compat module.
        Assert.Contains("id.Equals(RotStack.CoopModuleId, StringComparison.OrdinalIgnoreCase) && _carriedCoopXml is { } coop ? coop", window);
        Assert.Contains("var xml = CoreModuleXml(moduleId);", SourceFiles.Method(window, "private (string Name, string Version, string? Directory)? Describe(string moduleId)"));
        Assert.Contains("Link=\"SetAsideCoopWindow.cs\"", File_("RealmOfThronesLauncher.csproj"));
        // The Play page no longer asks players to put the host's Coop build in by hand.
        Assert.DoesNotContain("host's Coop build", File_("MainWindow.xaml"));
        Assert.Contains("Text=\"Put Realm of Thrones in the game's Modules folder yourself. Coop comes with this launcher.\"", File_("MainWindow.xaml"));
    }

    // Live test T5: Plain Realm of Thrones against a server that adds Coodsz.TestExtra started the game into Coop's refusal.
    [Fact]
    public void A_server_mod_this_setup_leaves_out_stops_Connect_before_the_game_starts()
    {
        const string refusal = "[Coop] Module validation failed. To join the server the module 'Coodsz TestExtra' (Coodsz.TestExtra) with version 'v1.0.0.0' is required.\r\n"
            + "To join the server the module 'NoIdMod' with version 'v2' is required.";
        // Only a line with a module id is kept for the next Connect; a name alone is only logged.
        Assert.Equal(new[] { "Coodsz.TestExtra" }, RotJoinCheck.RequiredByServer(refusal));
        Assert.Equal(new[] { "NoIdMod" }, RotJoinCheck.RequiredByServer(refusal, namesOnly: true));
        Assert.Empty(RotJoinCheck.RequiredByServer(null));
        Assert.Empty(RotJoinCheck.RequiredByServer("Wrong version of module 'Foo' detected."));

        // The core, Coop, the official modules and the client add ons have checks of their own or are not in Coop's join check.
        var launch = new[] { "Native", "SandBoxCore", "Sandbox", RotStack.CoopModuleId, "Kept.Mod" };
        var server = new[] { "coodsz.testextra", RotStack.RotModules[0], RotStack.CoopModuleId, "NavalDLC", "kept.mod", "DedicatedServer.Windows", "Coodsz.TestExtra" };
        Assert.Equal(new[] { "coodsz.testextra" }, RotJoinCheck.NotLoadedHere(server, launch));
        Assert.Empty(RotJoinCheck.NotLoadedHere(Array.Empty<string>(), launch));

        var before = RotJoinCheck.ServerModsLine(new[] { "Coodsz.TestExtra" }, before: true);
        Assert.StartsWith("The server needs mods this setup does not load: Coodsz.TestExtra. The game was not started.", before);
        Assert.Contains("click Import mod list on the Mods page, then click Connect again.", before);
        Assert.DoesNotContain("was not started", RotJoinCheck.ServerModsLine(new[] { "A" }, before: false));
        Assert.Contains("Content=\"Import mod list\"", File_("MainWindow.xaml"));

        // After every other check and before the game starts; a refusal is remembered for that host, and an import drops it.
        var connect = SourceFiles.Method(Window(), "private async Task ConnectAsync()");
        InOrder(connect,
            "if (!ModChecksPass(LaunchSetup))",
            "var joinKey = JoinMemory.Key(",
            "if (!await ServerModsLoadedHereAsync(steam, joinKey))",
            "await CheckDirectJoinAsync(",
            "StartWithPrefabsFix(setup, start)",
            "log => RememberServerNeeds(joinKey, modules, log)");
        Assert.Matches(@"if \(!await ServerModsLoadedHereAsync\(steam, joinKey\)\)\s+return;", connect);
        var check = SourceFiles.Method(Window(), "private async Task<bool> ServerModsLoadedHereAsync(bool steam, string joinKey)");
        Assert.Contains("_settings.ServerNeeds.GetValueOrDefault(joinKey)", check);
        Assert.Contains("before: true", check);
        // Advice, not a wall: the second Connect to the same host in this session goes on.
        Assert.Contains("if (missing.Count == 0 || !_serverModsWarned.Add(joinKey))", check);
        // This PC's server only on the port typed, and only when it is the Realm of Thrones server (its --data-dir).
        Assert.Contains("_localServer.Port.ToString(System.Globalization.CultureInfo.InvariantCulture) == port", check);
        Assert.Contains("known.AddRange(await Task.Run(() => OwnServerModules(port)));", check);
        var own = SourceFiles.Method(Window(), "private static IReadOnlyList<string> OwnServerModules(string port)");
        Assert.Contains("!RotJoinCheck.IsRotServer(ServerCommandLines(), dataRoot)", own);
        Assert.Contains("NightlyServer.ReadModules(text)", own);
        Assert.True(RotJoinCheck.IsRotServer(new[] { null, @"x.exe --data-dir ""C:\Rot Data\DedicatedServer\""" }, @"c:/rot data/DedicatedServer"));
        Assert.False(RotJoinCheck.IsRotServer(new[] { @"x.exe --data-dir C:\TAOM\DedicatedServer", "x.exe" }, @"C:\Rot Data\DedicatedServer"));
        Assert.False(RotJoinCheck.IsRotServer(new[] { @"x.exe --data-dir C:\Rot" }, ""));
        // A join to that host that gets in clears what it named.
        Assert.Contains("_settings.ServerNeeds.Remove(key);", SourceFiles.Method(Window(), "private void RememberJoin(string key, IReadOnlyList<string> modules)"));
        var remember = SourceFiles.Method(Window(), "private void RememberServerNeeds(string joinKey, IReadOnlyList<string> modules, string? log)");
        Assert.Matches(@"if \(missing\.Count > 0\)\s+\{\s+_settings\.ServerNeeds\[joinKey\] = missing\.ToList\(\);", remember);
        Assert.Contains("RotJoinCheck.RequiredByServer(log, namesOnly: true)", remember);
        Assert.Matches(@"Log\(line\);\s+refused\?\.Invoke\(text\);", SourceFiles.Method(Window(), "private async Task WatchJoinAsync(Process started, string gameRoot, Action? joined = null, Action<string?>? refused = null)"));
        Assert.Contains("_settings.ServerNeeds.Clear();", File_("MainWindow.Setups.cs"));

        var settings = RotClientSettings.Parse("{\"ServerNeeds\":{\"direct:1.2.3.4:4200\":[\"Coodsz.TestExtra\"]}}");
        Assert.Equal("Coodsz.TestExtra", Assert.Single(settings.ServerNeeds["DIRECT:1.2.3.4:4200"]));
        Assert.Empty(RotClientSettings.Parse("{\"ServerNeeds\":null}").ServerNeeds);
    }

    [Fact]
    public void Every_core_module_at_its_version_passes_and_each_problem_is_one_plain_line()
    {
        Assert.Empty(Core(AllReady()));
        var noV = AllReady();
        noV[RotStack.CoreModuleId] = "8.1.8";
        Assert.Empty(Core(noV));

        var rotMissing = AllReady();
        rotMissing.Remove(RotStack.ContentModuleId);
        Assert.Equal("Realm of Thrones (ROT-Content) is not on this PC. Download Realm of Thrones v8.1.8 from its mod page and unpack its four folders into the game's Modules folder (click Module folder), then click Check mods.", Assert.Single(Core(rotMissing)));

        var oldMap = AllReady();
        oldMap[RotStack.MapModuleId] = "v8.1.8";
        Assert.Equal("Realm of Thrones (ROT_Map) on this PC is v8.1.8. Realm of Thrones in Coop needs v8.1.8.2.", Assert.Single(Core(oldMap)));

        var oldCompat = AllReady();
        oldCompat[RotStack.CompatModuleId] = "v0.0.9";
        Assert.Equal("Realm of Thrones Coop Compat on this PC is v0.0.9. Realm of Thrones in Coop needs v0.1.0.", Assert.Single(Core(oldCompat)));

        var noNaval = AllReady();
        noNaval.Remove(RotStack.NavalModuleId);
        Assert.Contains("War Sails DLC", Assert.Single(Core(noNaval)));

        var noCoop = AllReady();
        noCoop.Remove(RotStack.CoopModuleId);
        Assert.Equal("Bannerlord Coop (CoopNightly) is not on this PC. This launcher carries Coop RoT build 2026-11-14 (01846e1) and puts it in when you click Connect.", Assert.Single(Core(noCoop)));

        var noCompat = AllReady();
        noCompat.Remove(RotStack.CompatModuleId);
        Assert.Contains("It comes with this launcher", Assert.Single(Core(noCompat)));

        // The lines come in load order.
        var two = AllReady();
        two.Remove(RotStack.DragonModuleId);
        two.Remove("Bannerlord.ButterLib");
        var lines = Core(two);
        Assert.Equal(2, lines.Count);
        Assert.StartsWith("ButterLib is not on this PC. Subscribe to it on the Steam Workshop", lines[0]);
        Assert.StartsWith("Realm of Thrones (ROT-Dragon) is not on this PC.", lines[1]);
    }

    [Fact]
    public void A_missing_workshop_library_leaves_connect_on_but_mcm_is_got_by_hand_from_nexus_and_must_be_new_enough()
    {
        var noHarmony = AllReady();
        noHarmony.Remove(RotStack.Harmony.ModuleId);
        Assert.Single(Core(noHarmony));
        Assert.Empty(Core(noHarmony, withWorkshopLibraries: false));

        var noMcm = AllReady();
        noMcm.Remove(RotStack.Mcm.ModuleId);
        Assert.Equal(RotJoinCheck.McmMissing, Assert.Single(Core(noMcm, withWorkshopLibraries: false)));
        Assert.EndsWith(RotStack.McmNexusPage, RotJoinCheck.McmMissing);
        // The Burning Empires tools send players to the same MCM page; the retired Workshop item is never subscribed.
        Assert.Equal(BeStack.Mcm.NexusPage, RotStack.McmNexusPage);
        var mcm = RotJoinCheck.LibraryNeed(RotStack.Mcm);
        Assert.Equal(ServerModFetch.ByHand, mcm.Fetch);
        Assert.Equal(RotStack.McmNexusPage, mcm.Link);
        Assert.Equal(ServerModFetch.Workshop, RotJoinCheck.LibraryNeed(RotStack.Harmony).Fetch);
        Assert.Contains("!RotJoinCheck.FromNexus(mod)", SourceFiles.Method(Window(), "private async Task<bool> GetLibrariesAsync()"));

        var oldMcm = AllReady();
        oldMcm[RotStack.Mcm.ModuleId] = "v5.11.9";
        Assert.StartsWith("MCM v5.11.9 on this PC is too old. Realm of Thrones in Coop needs MCM v5.12.0 or newer", Assert.Single(Core(oldMcm)));
        oldMcm[RotStack.Mcm.ModuleId] = "v5.12.0";
        Assert.Empty(Core(oldMcm));
        // An MCM whose SubModule.xml names no version says so, not "MCM  on this PC is too old".
        oldMcm[RotStack.Mcm.ModuleId] = "";
        Assert.StartsWith("The launcher could not read the version of MCM on this PC. Realm of Thrones in Coop needs MCM v5.12.0 or newer", Assert.Single(Core(oldMcm)));
    }

    [Fact]
    public void A_host_list_that_matches_this_pc_passes_and_no_list_imported_checks_nothing()
    {
        var host = HostList(("MyExtra", "v1.2"));
        Assert.Empty(RotJoinCheck.HostProblems(host, Installed(host), RotStack.ClientModules.Append("MyExtra")));
        Assert.Empty(RotJoinCheck.HostProblems(Array.Empty<SharedModEntry>(), Installed(host), RotStack.ClientModules.Append("Other")));
        Assert.Null(RotJoinCheck.Blocking(Array.Empty<RotVersionProblem>()));
    }

    [Fact]
    public void A_version_apart_a_mod_missing_or_a_mod_the_host_lacks_blocks_with_a_line_that_names_it()
    {
        var host = HostList(("MyExtra", "v1.2"));

        var older = Installed(host);
        older["Bannerlord.Harmony"] = "v2.4.1.0";
        var different = Assert.Single(RotJoinCheck.HostProblems(host, older, RotStack.ClientModules.Append("MyExtra")));
        Assert.Equal(RotVersionProblemKind.Different, different.Kind);
        Assert.Equal("Bannerlord.Harmony is v2.4.1.0 here. The shared list says v1.0.0.", different.Text);
        var blocking = RotJoinCheck.Blocking(new[] { different })!;
        Assert.StartsWith(different.Text + " The host and every player need the same mods at the same versions", blocking);
        Assert.Contains("Import mod list", blocking);

        var missing = Installed(host);
        missing.Remove("MyExtra");
        var gone = Assert.Single(RotJoinCheck.HostProblems(host, missing, RotStack.ClientModules));
        Assert.Equal(RotVersionProblemKind.Missing, gone.Kind);
        Assert.Equal("MyExtra is on the host's list but not on this PC.", gone.Text);

        var extra = Assert.Single(RotJoinCheck.HostProblems(host, Installed(host), RotStack.ClientModules.Append("MyExtra").Append("Mine"), id => id == "Mine" ? "My mod" : null));
        Assert.Equal(RotVersionProblemKind.NotOnHost, extra.Kind);
        Assert.Equal("My mod is on this setup but not on the host's list.", extra.Text);
    }

    [Fact]
    public void The_compare_skips_the_official_game_modules_and_client_add_ons_as_coop_does()
    {
        // The host lists Native at another version and no StoryMode; this PC loads the join password module.
        var host = HostList().Where(entry => entry.Id != "StoryMode")
            .Select(entry => entry.Id == "Native" ? entry with { Version = "v1.4.7" } : entry).ToList();
        var installed = Installed(HostList());
        var active = RotStack.ClientModules.Append(ClientAddOns.JoinPasswordId);
        Assert.Empty(RotJoinCheck.HostProblems(host, installed, active));
        Assert.All(new[] { "Native", "StoryMode", "NavalDLC" }, id => Assert.True(CoopJoinRules.IsIgnored(id)));
    }

    [Fact]
    public void Import_refuses_another_overhauls_list_before_a_setup_is_made_and_keeps_the_host_list()
    {
        var import = SourceFiles.Method(Window(), "private async void ImportList_Click(");
        // The list is kept only after the save prompt, so a cancelled import leaves the stored host list alone.
        InOrder(import, "RotSharedList.ForeignGame(shared)", "RotSharedList.Import(_editing.Copy(_editing.Name), shared, onThisPc).Changed",
            "if (!ConfirmModChanges())", "return;", "_settings.ModSetups.Create()", "RememberHostList(shared);", "RotSharedList.Import(keepLocked ? _editing.Copy(_editing.Name) : _editing, shared, onThisPc)");
        Assert.Contains("var keepLocked = _editing.Locked && !", import);
        Assert.Contains("if (_editing.Locked && !keepLocked)", import);
        Assert.Contains("RotVersionProblemKind.Different", import);
        Assert.NotNull(RotSharedList.ForeignGame(new[] { new SharedModEntry("BurningEmpires", "Burning Empires", "v2.0.2", "0") }));
        Assert.NotNull(RotSharedList.ForeignGame(new[] { new SharedModEntry("TAOM", "TAOM", "v2.0.29.5", "0") }));

        // A list imported into the locked setup lands in a new setup with the list's extras.
        var book = RotClientSettings.Parse("").ModSetups;
        var made = book.Create();
        var result = RotSharedList.Import(made, HostList(("MyExtra", "v1.2")).Select(entry => entry.Id == "MyExtra" ? entry with { WorkshopId = "123" } : entry).ToList(), Array.Empty<BannerlordCoopLauncher.Core.Profile.FoundMod>());
        Assert.Equal(new[] { "MyExtra" }, result.Added);
        Assert.True(book.Find(RotSetupBook.CoreName)!.Locked);
        Assert.Empty(book.Find(RotSetupBook.CoreName)!.Extras);

        // A host on Plain Realm of Thrones: its list adds nothing to the locked setup, so the launcher keeps it.
        var plain = book.Find(RotSetupBook.CoreName)!;
        Assert.False(RotSharedList.Import(plain.Copy(plain.Name), HostList(), Array.Empty<BannerlordCoopLauncher.Core.Profile.FoundMod>()).Changed);
        Assert.True(RotSharedList.Import(plain.Copy(plain.Name), HostList(("MyExtra", "v1.2")).Select(entry => entry.Id == "MyExtra" ? entry with { WorkshopId = "123" } : entry).ToList(),
            Array.Empty<BannerlordCoopLauncher.Core.Profile.FoundMod>()).Changed);
        Assert.True(plain.Locked);
    }

    // ----- Safe mode and the launch -----

    [Fact]
    public void Safe_mode_starts_the_locked_plain_setup_once_and_leaves_the_saved_setup_alone()
    {
        var window = Window();
        Assert.Matches(@"LaunchSetup => _safeModeOnce\s+\? _settings\.ModSetups\.Find\(RotSetupBook\.CoreName\) \?\? _settings\.ModSetups\.Current\(\)\s+: _settings\.ModSetups\.Current\(\);", window);
        var safe = SourceFiles.Method(window, "private async void SafeMode_Click(");
        InOrder(safe, "_safeModeOnce = true;", "await ConnectAsync();", "_safeModeOnce = false;");
        Assert.Contains("Safe mode joins with the Realm of Thrones core only this one time.", safe);
        Assert.Contains("Click=\"SafeMode_Click\"", File_("MainWindow.xaml"));

        var book = RotClientSettings.Parse("").ModSetups;
        var mine = book.CopyOf(RotSetupBook.CoreName);
        RotLoadOrder.Add(mine, "My extra", "MyExtra", "123");
        Assert.Equal(mine.Name, book.Active);
        Assert.Equal(RotStack.ClientModules, RotLoadOrder.LaunchModules(book.Find(RotSetupBook.CoreName), joinPassword: false));
    }

    [Fact]
    public void The_launch_order_is_the_core_with_extras_before_the_compat_module_then_the_join_password_module()
    {
        var setup = RotClientSettings.Parse("").ModSetups.CopyOf(RotSetupBook.CoreName);
        RotLoadOrder.Add(setup, "First", "FirstExtra", "1");
        RotLoadOrder.Add(setup, "Second", "SecondExtra", "2");
        var order = RotLoadOrder.LaunchModules(setup, joinPassword: false);
        Assert.Equal(RotStack.ClientModules.Take(RotStack.ClientModules.Length - 1).Concat(new[] { "FirstExtra", "SecondExtra", RotStack.CompatModuleId }), order);
        Assert.Equal(ClientAddOns.JoinPasswordId, RotLoadOrder.LaunchModules(setup, joinPassword: true)[^1]);

        // The pinned Coop's Direct join sends no password, so Connect always adds the join password module, which
        // fills it from COODSZ_COOP_PASSWORD. The launcher carries the module in its ClientAddOns payload.
        var window = Window();
        Assert.DoesNotContain("JoinPasswordAddOn", window);
        Assert.Contains("var modules = RotLoadOrder.LaunchModules(setup, InstallJoinPassword());", SourceFiles.Method(window, "private async Task ConnectAsync()"));
        Assert.Contains("ClientAddOns.Install(_gameRoot, ClientAddOns.JoinPasswordId, ClientAddOns.JoinPasswordName)", SourceFiles.Method(window, "private bool InstallJoinPassword()"));
        Assert.Contains("start.Environment[CoopPasswordPatch.VariableName] = CurrentPassword();", window);
        Assert.Contains(@"compat\CoodszJoinPassword\module\**\*"" Payload=""ClientAddOns"" PayloadFolder=""DedicatedServer.CoodszJoinPassword\""", File_("RealmOfThronesLauncher.csproj"));
        // The server needs no copy: Coop's join check (ModuleValidator.ShouldValidateModule) and the launcher's host
        // list compare both skip DedicatedServer.* ids, and the module only runs on a client.
        Assert.StartsWith("DedicatedServer.", ClientAddOns.JoinPasswordId);
        Assert.True(CoopJoinRules.IsIgnored(ClientAddOns.JoinPasswordId));
        Assert.DoesNotContain("CoodszJoinPassword", SourceFiles.Read(Path.Combine("src", "RealmOfThronesHost", "RealmOfThronesHost.csproj")));
        Assert.Contains("TaomLaunch.Arguments(connect: true, steam, CurrentPassword(), lobbyId,", window);
        Assert.Contains("FileName = GameRoot.ExecutablePath(_gameRoot)", window);
        // The compat module comes with the launcher and goes in on every Connect.
        Assert.Contains("ClientAddOns.TryInstall(_gameRoot, RotStack.CompatModuleId, RotStack.CompatName,", SourceFiles.Method(window, "private bool InstallCompat()"));
        // So the core check reads the carried copy, and a first Connect is not held back before InstallCompat ran.
        Assert.Contains("CarriedCompatXml() is { } carried ? carried", window);
        Assert.Contains("var xml = CoreModuleXml(id);", SourceFiles.Method(window, "private string? CoreVersionOrNull(string id)"));
        Assert.Contains("PayloadFolder=\"RealmOfThrones.CoopCompat\\\"", File_("RealmOfThronesLauncher.csproj"));
    }

    // The Activity box is a selectable TextBox a stream or screenshot shows, so Connect logs the game's command
    // line without the /cooppassword value, in all four client launchers.
    [Theory]
    [InlineData("RealmOfThronesLauncher")]
    [InlineData("BurningEmpiresLauncher")]
    [InlineData("TaomCoopLauncher")]
    [InlineData("AD1259Launcher")]
    public void Connect_logs_the_command_line_without_the_join_password(string launcher)
    {
        var window = SourceFiles.Read(Path.Combine("src", launcher, "MainWindow.xaml.cs"));
        Assert.DoesNotContain("Log(arguments);", window);
        Assert.Contains("Log(TaomLaunch.Shown(arguments, CurrentPassword()));", window);

        // Only the password goes: the address, the modules and the other switches stay readable, and a short one goes too.
        // A saved password with a trailing line break goes on the line trimmed and is hidden all the same.
        foreach (var password in new[] { "lord hunter2", "ab", "hunter2\r\n" })
        {
            var arguments = TaomLaunch.Arguments(connect: true, steamLobby: false, password, directEndpoint: "10.0.0.5:7777", modules: ["Native", "ROT-Core"]);
            Assert.Contains(password.Trim(), arguments);
            var shown = TaomLaunch.Shown(arguments, password);
            Assert.DoesNotContain(password.Trim(), shown);
            Assert.Contains("/cooppassword ****", shown);
            Assert.Contains("/autoconnect 10.0.0.5:7777", shown);
            Assert.Contains("_MODULES_*Native*ROT-Core*_MODULES_", shown);
        }
        var noPassword = TaomLaunch.Arguments(connect: true, steamLobby: false, "", directEndpoint: "10.0.0.5:7777");
        Assert.Equal(noPassword, TaomLaunch.Shown(noPassword, ""));
        // A Steam lobby join carries no password, so a saved one with a quote in it must not break the log line.
        var lobby = TaomLaunch.Arguments(connect: true, steamLobby: true, "say \"hi\"", lobbyId: 42);
        Assert.Equal(lobby, TaomLaunch.Shown(lobby, "say \"hi\""));
    }

    // ----- Notes and placeholders -----

    [Fact]
    public void The_notes_say_war_sails_what_is_off_the_story_choices_and_the_long_first_join()
    {
        var notes = RotUiText.ClientNotes;
        Assert.Equal(RotUiText.WarSailsNeeded, notes[0]);
        Assert.Equal(RotUiText.FirstJoinWait, notes[^1]);
        Assert.All(RotUiText.OffInFirstVersion, line => Assert.Contains(line, notes));
        Assert.Contains(notes, line => line.Contains("first player to answer") && line.Contains("60 seconds"));
        Assert.Contains("several minutes", RotUiText.FirstJoinWait);
        Assert.Contains("Do not close the game", RotUiText.FirstJoinWait);
        foreach (var line in notes)
            Assert.DoesNotMatch(@"\w-\w", line);

        var window = Window();
        Assert.Contains("RealmOfThrones.Ui.RotUiText.ClientNotes", SourceFiles.Method(window, "private void ShowMadeFor()"));
        // The notes sit on the Made for card only, not in the log at every start.
        Assert.DoesNotContain("RotUiText.ClientNotes", SourceFiles.Method(window, "public MainWindow()"));
    }

    [Fact]
    public void Update_checks_and_sending_logs_wait_for_the_release_repos()
    {
        var window = Window();
        var start = SourceFiles.Method(window, "public MainWindow()");
        InOrder(start, "if (RotRelease.UpdatesReady)", "WatchForUpdateAsync(RotRelease.Repo, RotRelease.Version)", "Log(RotRelease.UpdatesNotSetUp);");
        // No Send my logs button that only says it is not set up: Logs and help opens the client logs, and the empty
        // slots fill with Send my logs only once RotRelease names the crash repo.
        var xaml = File_("MainWindow.xaml");
        foreach (var gone in new[] { "SendLogs_Click", "Content=\"Send my logs\"" })
            Assert.DoesNotContain(gone, xaml);
        Assert.Single(Regex.Matches(xaml, "Click=\"OpenClientLogs_Click\""));
        InOrder(start, "Log(RotRelease.UpdatesNotSetUp);", "SetUpCrashReports();");
        InOrder(SourceFiles.Method(window, "private void SetUpCrashReports()"), "if (!RotRelease.ReportsReady || !CrashReports.HasEndpoint())", "return;",
            "_reportsOn = true;", "SendLogsSlot.Content = Coodsz.Ui.CrashReportUi.SendLogsButton(this, OpenSendLogs);");
        Assert.Contains("if (!_reportsOn)", SourceFiles.Method(window, "private void OfferCrashReport("));
        Assert.Contains("OfferCrashReport(capture);", SourceFiles.Method(window, "private async Task ExplainEarlyExitAsync("));
        var request = SourceFiles.Method(window, "private CrashReportRequest CrashRequest(");
        Assert.Contains("Tool = RotRelease.ToolId,", request);
        Assert.Contains("RotClientLogPack.Create(zip, gameRoot, documents, programData, windowsDumps, activity, actionLog, serverData, serverRoot, limits)", request);
        Assert.Contains("var serverRoot = string.IsNullOrWhiteSpace(server.ServerRoot) ? RotServerBuild.DefaultRoot : server.ServerRoot;", request);
        Assert.Contains("_settings.Password, server.Password, RotClientLogPack.ServerPassword(serverData) }", request);
        // The one click mod reports go out from each mod row only (Both_tools_show_the_list_chips_and_open_rot_reports_from_each_row).
        foreach (var sends in new[] { "ModReportBar(", "CommunityCompat.RefreshAsync(", "ReleaseNotice.BeRepo", "AnnouncementUi.Attach(" })
            Assert.DoesNotContain(sends, window);
        // No request names a tool id as a bare string: the crash request takes RotRelease.ToolId.
        Assert.DoesNotMatch("(?<![A-Za-z])Tool = \"", window);
        Assert.True(RotRelease.UpdatesReady);
        Assert.True(RotRelease.ReportsReady);
    }

    [Fact]
    public void Join_your_own_server_reads_the_realm_of_thrones_server_data_folder()
    {
        var window = Window();
        Assert.Contains("ReadOwnServerFromRotData();", SourceFiles.Method(window, "public MainWindow()"));
        Assert.Contains("RotServerSettings.Load(Path.Combine(ToolData.FolderOf(ToolData.RotServer), RealmOfThrones.Core.RotServerSettings.FileName)).DataRoot", window);
        var guide = SourceFiles.Method(window, "private async void JoinHelp_Click(");
        Assert.Contains("ClientTool = \"Coodsz Realm of Thrones Client Launcher\"", guide);
        Assert.Contains("ServerTool = \"Coodsz Realm of Thrones Server Tool\"", guide);
    }

    // ----- Play -----

    [Fact]
    public void A_missing_core_shows_its_line_on_play_with_module_folder_as_the_step_and_no_list_asks_for_the_hosts()
    {
        var next = SourceFiles.Method(Window(), "private void ShowNextStep(bool connectReady)");
        Assert.Contains("CoreProblemText.Text = firstCore?.StatusTip ?? \"\";", next);
        Assert.Contains("Primary(NextModulesButton, step == NextStep.InstallMods && core);", next);
        // Check mods only reads again, so it is never the gold step.
        Assert.DoesNotContain("Primary(CheckModsButton", next);
        Assert.Contains("HostListHint.Visibility = !gameMissing && !core && !_hostListImported", next);
        Assert.Contains("_hostListImported = ReadHostList().Count > 0;", next);

        var xaml = File_("MainWindow.xaml");
        Assert.Contains("x:Name=\"NextModulesButton\" Content=\"Module folder\"", xaml);
        Assert.Contains("Text=\"Ask the host for their mod list, then click Import mod list on the Mods page.\"", xaml);
        // The line that sends the player to the Modules folder names the button on Play.
        Assert.Contains("click Module folder", RotJoinCheck.ModuleProblem(RotStack.CoreModuleId, null));
        // Coop comes with the launcher, so its line sends nobody to the Modules folder.
        Assert.DoesNotContain("Module folder", RotJoinCheck.ModuleProblem(RotStack.CoopModuleId, null));
    }

    [Fact]
    public void A_failed_compat_copy_stops_connect_with_a_line()
    {
        var install = SourceFiles.Method(Window(), "private bool InstallCompat()");
        InOrder(install, "if (ClientAddOns.TryInstall(", "return true;",
            "ShowJoin(\"Could not put \" + RotStack.CompatName + \" in the Modules folder, so the game was not started.", "return false;");
    }

    // ----- Encyclopedia -----

    [Fact]
    public void The_encyclopedia_page_has_its_sidebar_entry_and_uses_the_shared_list()
    {
        var xaml = File_("MainWindow.xaml");
        Assert.Contains("x:Name=\"NavEncyclopedia\"", xaml);
        Assert.Contains("Content=\"Encyclopedia\" Click=\"Nav_Click\" Tag=\"Encyclopedia\"", xaml);
        Assert.Contains("<ScrollViewer x:Name=\"EncyclopediaPage\" Visibility=\"Collapsed\"", xaml);
        var window = Window();
        Assert.Contains("(\"Encyclopedia\", EncyclopediaPage),", window);
        Assert.Contains("NavLogs, NavEncyclopedia, NavSettings, GuidesButton }", window);
        Assert.Contains("ShowEncyclopedia();", SourceFiles.Method(window, "private void StartFrame()"));
        Assert.Contains("foreach (var link in RotEncyclopedia.Links)", SourceFiles.Method(window, "private void ShowEncyclopedia()"));
        InOrder(SourceFiles.Method(window, "private void EncyclopediaLink_Click("), "ShellOpen.TryOpen(url, out _)", "RotEncyclopedia.CouldNotOpen");
    }

    // ----- Guides -----

    [Fact]
    public void The_guides_page_is_the_last_sidebar_entry_and_shows_the_launcher_guides()
    {
        var xaml = File_("MainWindow.xaml");
        Assert.Contains("x:Name=\"GuidesButton\"", xaml);
        Assert.Contains("Content=\"Guides\" Click=\"Guides_Click\" Tag=\"Guides\"", xaml);
        Assert.Contains("<ContentControl x:Name=\"GuidesPage\" Visibility=\"Collapsed\"/>", xaml);
        var window = Window();
        Assert.Contains("(\"Guides\", GuidesPage),", window);
        var click = SourceFiles.Method(window, "private void Guides_Click(");
        InOrder(click, "GuideLibrary.For(GuideSet.RotClient)", "CloseGuides", "ShowPage(\"Guides\");");
        Assert.Contains("private void CloseGuides() => ShowPage(\"Play\");", window);
        Assert.Contains(@"<Compile Include=""..\TaomCoopHost\TaomGuidesPage.cs"" Link=""TaomGuidesPage.cs"" />", File_("RealmOfThronesLauncher.csproj"));
    }

    // Run 17: before Connect the Active Coop line named the Coop in Modules (a nightly that Connect sets aside). With the
    // RoT build unpacked it names that build, and says whether it is in Modules\Coop yet.
    [Fact]
    public void The_active_coop_line_names_the_carried_rot_build()
    {
        Assert.Equal("Active Coop: " + RotStack.CoopBuild.Label + ", which Connect puts in the game's Modules folder.", RotCoopInstall.ActiveCarried);
        Assert.Equal("Active Coop: " + RotStack.CoopBuild.Label + ", in the game's Modules folder until you close this launcher.", RotCoopInstall.ActiveIn);
        InOrder(SourceFiles.Method(Window(), "private void ShowActiveCoop()"),
            "if (_carriedCoopXml != null)", "RotCoopInstall.OwnFileName", "RotCoopInstall.ActiveIn : RotCoopInstall.ActiveCarried", "return;",
            "CoopInstall.ActiveClientLine(");
    }

    // ----- The Realm of Thrones mod list (checklist item 30) -----

    [Fact]
    public void The_built_in_list_knows_the_realm_of_thrones_core_and_marks_other_overhauls_broken()
    {
        var list = RotCompatList.BuiltIn();
        Assert.Equal(RotCompatList.Kind, list.FileKind);
        Assert.Equal(RotStack.GameVersion, list.Game);
        Assert.Equal(RotStack.CoopVersion, list.Coop);
        // The core the tools load: the four library mods, the four Realm of Thrones modules and the compat module.
        foreach (var id in RotStack.Libraries.Select(mod => mod.ModuleId).Concat(RotStack.RotModules).Append(RotStack.CompatModuleId))
            Assert.Equal(CompatTag.Safe, list.Find(id)!.Kind);
        Assert.Equal("2907", list.Find(RotStack.CoreModuleId)!.NexusId);
        foreach (var other in new[] { "TAOM", "TAOM.Dependencies", "AD1259", "BurningEmpires", "BurningEmpires_Elephants", "TOR_Core", "BannerKings" })
            Assert.Equal(CompatTag.Broken, list.Find(other)!.Kind);
        Assert.Equal(CompatTag.Broken, list.Find("NotAnId", "3496296180")!.Kind);
        Assert.Null(list.Find(null, null, "Realm of Thrones"));
        Assert.All(list.Mods, entry => Assert.NotEqual(CompatTag.Unknown, entry.Kind));
        Assert.All(list.Mods, entry => Assert.DoesNotContain(":", entry.Note));
        // The public repo is the release repo, written out in full for the address test.
        Assert.Equal("https://raw.githubusercontent.com/" + RotRelease.Repo + "/main/compat/" + RotCompatList.FileName, RotCompatList.OnlineUrl);
        Assert.Equal("https://github.com/" + RotRelease.Repo + "/blob/main/MODS.md", RotCompatList.ModsPageUrl);
        // The repo seed is the built in list, line for line.
        Assert.Equal(SourceFiles.Read(Path.Combine("src", "RealmOfThrones.Core", "Compat", RotCompatList.FileName)).Replace("\r\n", "\n"),
            SourceFiles.Read(Path.Combine("rot-compat", "compat", RotCompatList.FileName)).Replace("\r\n", "\n"));
    }

    [Fact]
    public void The_list_reads_only_its_own_kind_and_an_older_saved_copy_loses()
    {
        Assert.Throws<InvalidOperationException>(() => RotCompatList.Parse("{ \"kind\": \"CoodszBeCompat\", \"schemaVersion\": 2, \"mods\": [] }"));
        Assert.Throws<InvalidOperationException>(() => RotCompatList.Parse("{ \"kind\": \"CoodszTaomCompat\", \"schemaVersion\": 2, \"mods\": [] }"));
        Assert.Throws<InvalidOperationException>(() => BeCompatList.Parse("{ \"kind\": \"CoodszRotCompat\", \"schemaVersion\": 2, \"mods\": [] }"));
        Assert.Equal("Works in Realm of Thrones, not tested in Coop", RotCompatList.BadgeText(CompatTag.Reported));
        Assert.Equal("Tested in Coop by Coodsz, checked 2026-11-15.", RotCompatList.SourceLine(new CompatEntry { Source = "coodsz-coop", Checked = "2026-11-15" }));
        Assert.Equal("Coodsz notes.", RotCompatList.SourceLine(new CompatEntry { Source = "be-discord" }));

        var folder = Directory.CreateTempSubdirectory("rot-compat-").FullName;
        try
        {
            var cache = Path.Combine(folder, RotCompatList.FileName);
            File.WriteAllText(cache, "{ \"kind\": \"CoodszRotCompat\", \"schemaVersion\": 2, \"updated\": \"2000-01-01\", \"mods\": [] }");
            Assert.Equal(RotCompatList.BuiltIn().Mods.Count, RotCompatList.Load(cache).Mods.Count);
            File.WriteAllText(cache, "{ \"kind\": \"CoodszRotCompat\", \"schemaVersion\": 2, \"updated\": \"2999-01-01\", \"mods\": [] }");
            Assert.Empty(RotCompatList.Load(cache).Mods);
            // A count field of the wrong type is a bad list, not a crash, so the built in list stays.
            File.WriteAllText(cache, "{ \"kind\": \"CoodszRotCompat\", \"schemaVersion\": 2, \"updated\": \"2999-01-01\", \"mods\": [ { \"id\": \"A\", \"tag\": \"safe\", \"works\": \"many\" } ] }");
            Assert.Throws<InvalidOperationException>(() => RotCompatList.Parse(File.ReadAllText(cache)));
            Assert.Equal(RotCompatList.BuiltIn().Mods.Count, RotCompatList.Load(cache).Mods.Count);
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    private sealed class Answer(string body) : HttpMessageHandler
    {
        public string? Url { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Url = request.RequestUri?.ToString();
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(body) });
        }
    }

    [Fact]
    public async Task A_downloaded_list_older_than_the_built_in_one_is_not_saved()
    {
        const string url = "https://example.invalid/rot-mod-compat.json";
        var folder = Directory.CreateTempSubdirectory("rot-compat-").FullName;
        try
        {
            var cache = Path.Combine(folder, RotCompatList.FileName);
            var older = "{ \"kind\": \"CoodszRotCompat\", \"schemaVersion\": 2, \"updated\": \"2000-01-01\", \"mods\": [] }";
            var answer = new Answer(older);
            using (var http = new HttpClient(answer))
                Assert.Null(await RotCompatList.DownloadAsync(http, cache, CancellationToken.None, url));
            Assert.Equal(url, answer.Url);
            Assert.False(File.Exists(cache));

            var newer = "{ \"kind\": \"CoodszRotCompat\", \"schemaVersion\": 2, \"updated\": \"2999-01-01\", \"mods\": [ { \"id\": \"NewMod\", \"tag\": \"broken\" } ] }";
            using (var http = new HttpClient(new Answer(newer)))
                Assert.Equal(CompatTag.Broken, (await RotCompatList.DownloadAsync(http, cache, CancellationToken.None, url))!.Find("NewMod")!.Kind);
            Assert.Equal(newer, File.ReadAllText(cache));
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Fact]
    public void An_extra_mod_gets_its_list_status_and_the_player_counts_and_the_core_gets_none()
    {
        var list = RotCompatList.Parse("{ \"kind\": \"CoodszRotCompat\", \"schemaVersion\": 2, \"mods\": ["
            + "{ \"id\": \"GoodMod\", \"tag\": \"safe\", \"source\": \"coodsz-coop\", \"checked\": \"2026-11-15\", \"note\": \"Fine.\", \"works\": 3, \"broken\": 1 },"
            + "{ \"id\": \"BadMod\", \"tag\": \"broken\", \"note\": \"It crashes.\" },"
            + "{ \"id\": \"ROT-Core\", \"tag\": \"safe\", \"works\": 2 } ],"
            + "\"community\": [ { \"id\": \"Reported.Mod\", \"works\": 0, \"broken\": 2 } ] }");

        var good = RotModChips.Status(list, "GoodMod", null, "Good mod", reportTip: true)!;
        Assert.Equal((RotModChips.Works, "Tested safe"), (good.Kind, good.Text));
        Assert.Equal("3 say works", good.WorksChip);
        Assert.Equal("1 say broken", good.BrokenChip);
        Assert.Equal("Fine.\n\nTested in Coop by Coodsz, checked 2026-11-15.\n" + ModReports.CountsLine(3, 1) + "\n" + ModReports.ClickTip, good.Tip);
        var bad = RotModChips.Status(list, "BadMod", null, "Bad mod", reportTip: false)!;
        Assert.Equal((RotModChips.Broken, "Known broken"), (bad.Kind, bad.Text));
        Assert.Equal("Bad mod is known to be broken with Realm of Thrones. It crashes. Leave it off unless you are testing it.",
            RotCompatList.BrokenWarning("Bad mod", list.Find("BadMod")!));
        // A mod only players reported: Unknown with its counts; a mod nobody reported: Unknown, not tested.
        var reported = RotModChips.Status(list, "Reported.Mod", null, null, reportTip: false)!;
        Assert.Equal((RotModChips.Unknown, "Unknown", "2 say broken"), (reported.Kind, reported.Text, reported.BrokenChip));
        var unknown = RotModChips.Status(list, "NewMod", null, "New mod", reportTip: false)!;
        Assert.Equal((UnknownModBadge.Text, RotModChips.UnknownTip), (unknown.Text, unknown.Tip));
        // The Server Tool has no Test mods on the server, so the tip does not send anyone there.
        Assert.DoesNotContain("Test mods on the server", RotModChips.UnknownTip);
        Assert.True(RotModChips.ShowsUnknown(list, "NewMod", null, "New mod"));
        // The core, Coop and the official modules get no list status, but the core keeps its player counts.
        foreach (var id in new[] { RotStack.CoreModuleId, RotStack.CompatModuleId, "Bannerlord.Harmony", RotStack.CoopModuleId, "Native", RotStack.NavalModuleId })
        {
            Assert.Null(RotModChips.Status(list, id, null, null, reportTip: true));
            Assert.False(RotModChips.ShowsUnknown(list, id, null, null));
        }

        Assert.Equal(2, list.CountsFor(RotStack.CoreModuleId)!.Works);
        Assert.Null(list.CountsFor("BadMod"));
    }

    [Fact]
    public void Reports_go_out_with_tool_rot_for_rated_mods_and_carry_the_server_order()
    {
        Assert.Equal("rot", RotRelease.ToolId);
        Assert.True(RotModChips.OffersReport("GoodMod"));
        Assert.True(RotModChips.OffersReport(RotStack.CoreModuleId));
        Assert.False(RotModChips.OffersReport(RotStack.CoopModuleId));
        Assert.False(RotModChips.OffersReport("Native"));
        Assert.False(RotModChips.OffersReport("GoodMod", endpoint: "http://not-https.example/report"));

        // The server's mods in order: the core, then the extras that load, before the compat module.
        var setup = new TaomSetup { Name = "Mine" };
        Assert.Null(RotLoadOrder.Add(setup, "Loads", "Loads.Mod", null));
        Assert.Null(RotLoadOrder.Add(setup, "Not here", "Missing.Mod", null));
        Assert.Null(RotLoadOrder.Add(setup, "Off", "Off.Mod", null));
        RotLoadOrder.SetEnabled(setup, "Off.Mod", false);
        var order = RotModChips.ServerReportOrder(setup, id => id != "Missing.Mod", id => id == "Loads.Mod" ? "v1.0" : "");
        Assert.Equal(RotStack.ServerModules.SkipLast(1).Append("Loads.Mod").Append(RotStack.CompatModuleId), order.Select(mod => mod.Id));
        Assert.Equal("v1.0", order.Single(mod => mod.Id == "Loads.Mod").Version);
    }

    [Fact]
    public void Both_tools_show_the_list_chips_and_open_rot_reports_from_each_row()
    {
        var window = Window();
        Assert.Contains("LoadCompatList();", SourceFiles.Method(window, "private void InitSetups()"));
        Assert.Contains("var badge = CompatBadge(mod.ModuleId, mod.WorkshopId, mod.Label) ?? BadgeColumn();", SourceFiles.Method(window, "private Border ExtraRow("));
        Assert.Contains("var chips = CoreReportChips(id, RotJoinCheck.Name(id), found ? version : \"\") ?? BadgeColumn();", SourceFiles.Method(window, "private Border CoreRow("));
        InOrder(SourceFiles.Method(window, "private void AddMods_Click("), ".ToList(), PickerBadge);", "if (!ConfirmBrokenMod(mod.Name, mod.ModuleId, mod.WorkshopId))", "RotLoadOrder.Add(");
        InOrder(SourceFiles.Method(window, "private FrameworkElement? CompatBadge("), "var report = RotModChips.OffersReport(moduleId);", "RotModChips.Status(_compat,",
            "if (report)", "CommunityCounts(moduleId, workshopId, label)", "Coodsz.Ui.ModReportUi.RowLink(this, Report)");
        Assert.Contains("Tool = RotRelease.ToolId,", SourceFiles.Method(window, "private Coodsz.Ui.ModReportRequest ReportRequest("));

        var host = SourceFiles.Window(SourceFiles.Root(), "RealmOfThronesHost");
        Assert.Contains("LoadCompatList();", SourceFiles.Method(host, "private void SetUpModReports()"));
        InOrder(SourceFiles.Method(host, "private void ShowModSetups()"), "var loadOrder = RotModChips.ServerReportOrder(shown, id => StagedExtra(plan, id) != null, RowVersion);",
            "Status = RotModChips.Status(_compat, extra.ModuleId, extra.WorkshopId, extra.Label, reportTip: false,", "Reports = ReportChips(");
        InOrder(SourceFiles.Method(host, "private FrameworkElement? ReportChips("), "if (!ModReports.Rates(moduleId))", "_compat.CountsFor(moduleId, workshopId, name)",
            "if (RotModChips.OffersReport(moduleId) && serverRunsCode)", "Coodsz.Ui.ModReportWindow.Show(this, ReportRequest(");
        // The plan's code decides, not the manifest alone: a ClientCodeOnly extra (client DLLs with no server build and
        // no skip mark) or a ClientCodeSkipped one gets no host Report link; the core always runs on the server.
        var rows = SourceFiles.Method(host, "private void ShowModSetups()");
        Assert.Contains("Reports = ReportChips(extra.ModuleId, extra.WorkshopId, extra.Label, version, loadOrder, staged?.Code is ServerCode.DataOnly or ServerCode.ServerBuild),", rows);
        Assert.Contains("Reports = ReportChips(id, null, CoreName(id), coreVersion, loadOrder, serverRunsCode: true)", rows);
        Assert.DoesNotContain("ServerModuleNotes.SkippedCode", SourceFiles.Read(Path.Combine("src", "RealmOfThronesHost", "MainWindow.Reports.cs")));
        Assert.Contains("Tool = RotRelease.ToolId,", SourceFiles.Method(host, "private Coodsz.Ui.ModReportRequest ReportRequest("));
        Assert.Contains("RotModChips.ShowsUnknown(_compat, mod.ModuleId, mod.WorkshopId, mod.Name)", SourceFiles.Method(host, "private BannerlordCoopLauncher.ModBadge? HostPickerBadge("));
        Assert.Contains("RotCompatList.BrokenWarning(name, entry)", SourceFiles.Method(host, "private bool ConfirmBrokenHostMod("));
    }
}
