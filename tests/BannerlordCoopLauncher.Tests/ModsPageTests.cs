using System.Xml.Linq;
using BannerlordCoopLauncher.Core.Modules;
using Coodsz.Ui;

namespace BannerlordCoopLauncher.Tests;

// Wave 3 of the tools redesign (docs/design/redesign/build-plan.md 2 Mods and 5): the chip words of every tool, the
// running block of the Server Tool's Mods page, and Move up and Move down.
public sealed class ModsPageTests
{
    private static readonly XNamespace Wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static XDocument HostXaml() => XDocument.Parse(SourceFiles.Read(Path.Combine("src", "CoodszHost", "MainWindow.xaml")));

    private static XElement Named(XDocument xaml, string name) =>
        xaml.Descendants().Single(element => (string?)element.Attribute(X + "Name") == name);

    // ----- Chip words -----

    [Theory]
    [InlineData(ModuleState.UpdatePending, "Needs update")]
    [InlineData(ModuleState.VersionMismatch, "Wrong version")]
    [InlineData(ModuleState.Ready, "Ready")]
    [InlineData(ModuleState.Ambiguous, "Extra copies")]
    public void The_client_status_words_are_the_renamed_ones(ModuleState state, string word) =>
        Assert.Equal(word, ModChips.ClientStatus(state, enabled: true, extraCopy: false, workshopId: null));

    [Fact]
    public void A_turned_off_or_doubled_mod_keeps_its_words()
    {
        Assert.Equal("Off", ModChips.ClientStatus(ModuleState.Missing, enabled: false, extraCopy: false, workshopId: "2859188632"));
        Assert.Equal("Ready, extra copy", ModChips.ClientStatus(ModuleState.Ready, enabled: true, extraCopy: true, workshopId: null));
    }

    [Theory]
    [InlineData("2859188632", "Missing on Workshop")]
    [InlineData("0", "Missing")]
    [InlineData("", "Missing")]
    [InlineData(null, "Missing")]
    [InlineData("abc123", "Missing")]
    public void Missing_says_on_Workshop_only_with_a_real_Workshop_id(string? workshopId, string word) =>
        Assert.Equal(word, ModChips.ClientStatus(ModuleState.Missing, enabled: true, extraCopy: false, workshopId));

    [Fact]
    public void Missing_on_Workshop_says_what_to_do_on_hover_and_the_others_need_no_tip()
    {
        Assert.Equal("This mod is not on this PC. Subscribe to it on Steam Workshop. Install mods opens Steam.", ModChips.Status(ModChips.MissingOnWorkshop).Tip);
        Assert.Equal("", ModChips.Status(ModChips.Missing).Tip);
        Assert.Equal(ModChipKind.Bad, ModChips.Status(ModChips.MissingOnWorkshop).Kind);
        Assert.Equal(ModChipKind.Gold, ModChips.Status(ModChips.NeedsUpdate).Kind);
        Assert.Equal(ModChipKind.Ok, ModChips.Status(ModChips.Ready).Kind);
        Assert.Equal(ModChipKind.Mute, ModChips.Status(ModChips.Off).Kind);
    }

    [Theory]
    [InlineData("DedicatedServer.CoodszJoinPassword")]
    [InlineData("DedicatedServer.CoodszMenuGuard")]
    [InlineData("DedicatedServer.CoodszTools")]
    [InlineData("DedicatedServer.CoopSaveRescue")]
    [InlineData("DedicatedServer.IGCoopCompat")]
    [InlineData("Diplomacy.CoopCompat")]
    [InlineData("Limitless.CoopCompat")]
    [InlineData("StuckBattleFix")]
    [InlineData("BurningEmpires.CoopCompat")]
    public void Coodsz_modules_get_the_Coodsz_module_chip(string id) =>
        Assert.Equal(new ModChip("Coodsz module", ModChipKind.Gold), ModChips.Role(id, alwaysOn: false));

    [Fact]
    public void SkyApolos_AD1259_modules_are_Bundled_and_TAOM_compat_is_not_Coodsz()
    {
        Assert.Equal(new ModChip("Bundled", ModChipKind.Info), ModChips.Role("DedicatedServer.AD1259CoopCompat", alwaysOn: true));
        Assert.Equal(new ModChip("Bundled", ModChipKind.Info), ModChips.Role("DedicatedServer.AD1259CoopClientFix", alwaysOn: false));
        Assert.Null(ModChips.Role("TAOM.CoopCompat", alwaysOn: false));
        Assert.Equal(new ModChip("Always on", ModChipKind.Mute), ModChips.Role("Bannerlord.Harmony", alwaysOn: true));
        Assert.Null(ModChips.Role("RBM", alwaysOn: false));
    }

    // Every id the list names is a real module: a compat\ SubModule.xml id, or a constant the tools already use.
    [Fact]
    public void Each_Coodsz_and_Bundled_id_is_a_real_module_id()
    {
        var compat = Path.Combine(SourceFiles.Root(), "compat");
        var ids = Directory.EnumerateFiles(compat, "SubModule.xml", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .Select(path => (string?)XDocument.Load(path).Root!.Element("Id")?.Attribute("value"))
            .OfType<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        // The old Coop Save Rescue id, which the tools still clean up (AutoRescue.LegacyModuleId).
        ids.Add(BannerlordCoopLauncher.Core.Support.AutoRescue.LegacyModuleId);
        foreach (var id in CoodszModuleIds.Coodsz.Concat(CoodszModuleIds.Bundled))
            Assert.True(ids.Contains(id), id + " is not the Id of a SubModule.xml under compat.");
    }

    [Fact]
    public void A_row_shows_at_most_three_chips_status_first()
    {
        var chips = ModChips.For(ModChips.Status(ModChips.New), ModChips.Role("DedicatedServer.CoodszMenuGuard", false), new ModChip("Works", ModChipKind.Ok));
        Assert.Equal(new[] { "New", "Coodsz module", "Works" }, chips.Select(chip => chip.Text));
        Assert.Equal(new[] { "Always on" }, ModChips.For(null, ModChips.Role("Native", true)).Select(chip => chip.Text));
        Assert.True(ModChips.Max == 3);
    }

    [Fact]
    public void The_chip_kinds_bind_by_name_to_the_frame_chip()
    {
        // XAML binds ModChip.KindName to Chip.Kind, so the two enums must keep the same names.
        Assert.Equal(Enum.GetNames<ChipKind>(), Enum.GetNames<ModChipKind>());
        Assert.Equal("Gold", new ModChip("New", ModChipKind.Gold).KindName);
    }

    [Fact]
    public void The_old_chip_words_are_gone_from_every_tool()
    {
        Assert.DoesNotContain("=> \"Update\"", SourceFiles.Read(Path.Combine("src", "BannerlordCoopLauncher", "MainWindow.Status.cs")));
        Assert.DoesNotContain("=> \"Version\"", SourceFiles.Read(Path.Combine("src", "BannerlordCoopLauncher", "MainWindow.Status.cs")));
        var client = SourceFiles.Read(Path.Combine("src", "BannerlordCoopLauncher", "MainWindow.xaml"));
        // The status chip takes its colour from the English word, so a translated word keeps it (redesign wave 5).
        Assert.Contains("Kind=\"{Binding StatusKind}\"", client);
        Assert.Contains("StatusKind = ModChips.StatusKind(StatusText(module)).ToString(),", SourceFiles.Read(Path.Combine("src", "BannerlordCoopLauncher", "MainWindow.Status.cs")));
        Assert.Equal(ModChipKind.Gold, ModChips.StatusKind(ModChips.NeedsUpdate));
        Assert.DoesNotContain("Value=\"Update\"", client);
        foreach (var host in new[] { "TaomCoopHost", "BurningEmpiresHost" })
            Assert.DoesNotContain("\"Required\"", SourceFiles.Read(Path.Combine("src", host, "MainWindow.ModSetups.cs")));
        Assert.DoesNotContain("Text=\"Required\"", SourceFiles.Read(Path.Combine("src", "CoodszHost", "MainWindow.xaml")));
    }

    // ----- The running block -----

    [Fact]
    public void Running_means_the_server_a_mod_test_or_a_restart_that_waits()
    {
        Assert.False(ModListBlock.Running(serverRunning: false, modTestRunning: false, restartPending: false));
        Assert.True(ModListBlock.Running(serverRunning: true, modTestRunning: false, restartPending: false));
        Assert.True(ModListBlock.Running(serverRunning: false, modTestRunning: true, restartPending: false));
        Assert.True(ModListBlock.Running(serverRunning: false, modTestRunning: false, restartPending: true));
    }

    [Fact]
    public void The_block_reason_says_stop_first_then_locked()
    {
        Assert.Null(ModListBlock.Reason(running: false, locked: false));
        Assert.Equal("The server is running. Stop it to change the mod list.", ModListBlock.Reason(running: true, locked: false));
        Assert.Equal("The server is running. Stop it to change the mod list.", ModListBlock.Reason(running: true, locked: true));
        Assert.Equal("This setup is locked. The working mods cannot be changed.", ModListBlock.Reason(running: false, locked: true));
    }

    [Fact]
    public void The_Mods_page_turns_the_changing_controls_off_while_running_and_keeps_the_others()
    {
        var code = SourceFiles.Window(SourceFiles.Root(), "CoodszHost");
        var apply = SourceFiles.Method(code, "private void ApplyModListBlock(");
        foreach (var name in new[] { "AddModsButton", "SaveModulesButton", "RefreshModulesButton", "AutoOrderButton", "TestModsButton", "ImportListItem", "ModsRunningBanner", "SetupRunningNote" })
            Assert.Contains(name, apply);
        Assert.Contains("row.CanToggle = !running && !locked && !row.Required;", apply);
        Assert.Contains("row.CanMove = !running && !locked;", apply);
        foreach (var name in new[] { "EnabledOnly", "ModFilterChips", "ShareList" })
            Assert.DoesNotContain(name, apply);
        // Running is the server, a mod test, a crash restart that waits or a Start, Stop or Restart under way, and RefreshState (every second) applies it.
        Assert.Contains("ModListBlock.Running(_session.IsRunning, _modTestRunning, CrashRestartWaits || _restartingNow || _restartingAfterCrash || _controlBusy)", code);
        Assert.Contains("ApplyModListBlock();", SourceFiles.Method(code, "private void RefreshState("));
        Assert.Contains("ApplyModListBlock(force: true);", SourceFiles.Method(code, "private void ApplyProfileToFields("));
        // A setup picked while running keeps the block.
        Assert.Contains("!ModListRunning", SourceFiles.Method(code, "private void ApplyProfileModules("));
        foreach (var handler in new[] { "private void AddMods_Click(", "private void ReloadModules_Click(", "private void AutoOrder_Click(", "private void SaveModules_Click(", "private void ModuleDragStart(", "private void OpenSharedListFile(", "private void TestMods_Click(", "private void MoveModule(" })
            Assert.Contains("RefuseWhileModListRuns()", SourceFiles.Method(code, handler));
        Assert.Contains("ModListRunning, StableCoop.Order", SourceFiles.Method(code, "private void DeleteModule_Click("));
    }

    [Fact]
    public void The_row_controls_follow_CanToggle_and_CanMove()
    {
        var xaml = HostXaml();
        var list = Named(xaml, "ModuleList");
        Assert.Equal("ModuleListKeys", (string?)list.Attribute("PreviewKeyDown"));
        var toggle = list.Descendants(Wpf + "CheckBox").Single();
        Assert.Equal("{Binding CanToggle}", (string?)toggle.Attribute("IsEnabled"));
        Assert.Equal("{StaticResource Keep.Switch}", (string?)toggle.Attribute("Style"));
        var moves = list.Descendants().Single(element => element.Name.LocalName == "MoveButtons");
        Assert.Equal("{Binding CanMove}", (string?)moves.Attribute("CanMoveUp"));
        Assert.Equal("{Binding CanMove}", (string?)moves.Attribute("CanMoveDown"));
        var delete = list.Descendants(Wpf + "Button").Single(element => (string?)element.Attribute("Click") == "DeleteModule_Click");
        // Delete works only when the row can be deleted and moved (a MultiDataTrigger in its style), and a hidden Delete is
        // off and out of UI Automation.
        Assert.Null(delete.Attribute("IsEnabled"));
        var conditions = delete.Descendants(Wpf + "Condition").Select(condition => (string?)condition.Attribute("Binding")).ToList();
        Assert.Equal(new[] { "{Binding CanDelete}", "{Binding CanMove}" }, conditions);
        Assert.Contains(delete.Descendants(Wpf + "Setter"), setter => (string?)setter.Attribute("Property") == "AutomationProperties.IsOffscreenBehavior"
            && (string?)setter.Attribute("Value") == "Offscreen");
        // The chips trim instead of wrapping.
        var chip = list.Descendants().Single(element => element.Name.LocalName == "Chip");
        Assert.NotNull((string?)chip.Attribute("MaxWidth"));
        Assert.Contains("TextTrimming = TextTrimming.CharacterEllipsis", SourceFiles.Read(Path.Combine("src", "CoodszHost", "Shared", "Frame", "Chip.cs")));
        Assert.Contains("new DockPanel", SourceFiles.Read(Path.Combine("src", "CoodszHost", "Shared", "Frame", "Chip.cs")));
    }

    [Fact]
    public void The_More_menu_holds_share_import_auto_order_and_test()
    {
        var more = Named(HostXaml(), "ModsMoreButton");
        var clicks = more.Descendants(Wpf + "MenuItem").Select(item => (string?)item.Attribute("Click")).ToList();
        Assert.Equal(new[] { "ShareList_Click", "OpenSharedList_Click", "AutoOrder_Click", "TestMods_Click" }, clicks);
        Assert.All(more.Descendants(Wpf + "MenuItem"), item => Assert.False(string.IsNullOrWhiteSpace((string?)item.Attribute("ToolTip"))));
    }

    // ----- Move up and Move down -----

    private static readonly string[] Working = { "Native", "SandBoxCore", "Sandbox", "Bannerlord.Harmony", "Coop" };

    [Fact]
    public void An_extra_mod_moves_one_place_up_or_down()
    {
        var ids = Working.Concat(new[] { "RBM", "Diplomacy", "Calradia" }).ToList();
        Assert.Equal(5, ModListBlock.MoveTarget(ids, 6, -1, Working));
        Assert.Equal(7, ModListBlock.MoveTarget(ids, 6, 1, Working));
    }

    [Fact]
    public void Nothing_moves_past_the_ends_of_the_list()
    {
        var ids = Working.Concat(new[] { "RBM", "Diplomacy" }).ToList();
        Assert.Null(ModListBlock.MoveTarget(ids, 0, -1, Working));
        Assert.Null(ModListBlock.MoveTarget(ids, ids.Count - 1, 1, Working));
        Assert.Null(ModListBlock.MoveTarget(ids, -1, 1, Working));
        Assert.Null(ModListBlock.MoveTarget(ids, 3, 0, Working));
    }

    [Fact]
    public void A_move_into_the_working_Coop_order_is_refused_like_a_drag()
    {
        var ids = Working.Concat(new[] { "RBM" }).ToList();
        // A working mod can't swap with its neighbour, and an extra mod can't go up between two of them.
        Assert.Null(ModListBlock.MoveTarget(ids, 1, 1, Working));
        Assert.Null(ModListBlock.MoveTarget(ids, 2, -1, Working));
        Assert.False(BannerlordCoopLauncher.Core.Profile.WorkingLoadOrder.CanMoveKeepingCoop(ids, 1, 2, Working));
    }

    [Fact]
    public void Move_buttons_and_keys_use_the_drag_rules()
    {
        var code = SourceFiles.Window(SourceFiles.Root(), "CoodszHost");
        var move = SourceFiles.Method(code, "private void MoveModule(");
        Assert.Contains("ModListBlock.MoveTarget(", move);
        Assert.Contains("StableCoop.Order", move);
        Assert.Contains("ModuleCatalog.Validate(", move);
        Assert.Contains("RefreshModuleSaveHint();", move);
        var keys = SourceFiles.Method(code, "private void ModuleListKeys(");
        Assert.Contains("ModifierKeys.Alt", keys);
        Assert.Contains("Key.Space", keys);
        Assert.Contains("row.CanToggle", keys);
    }

    [Fact]
    public void With_a_filter_on_a_move_jumps_over_the_hidden_rows()
    {
        // Rows 5 and 7 are shown, row 6 is hidden by Enabled only: Move down takes row 5 to row 7.
        var ids = Working.Concat(new[] { "RBM", "Diplomacy", "Calradia" }).ToList();
        var shown = new[] { 0, 1, 2, 3, 4, 5, 7 };
        Assert.Equal(7, ModListBlock.MoveTarget(ids, shown, 5, 1, Working));
        Assert.Equal(5, ModListBlock.MoveTarget(ids, shown, 7, -1, Working));
        // The last shown row goes no further, and a row the page doesn't show doesn't move.
        Assert.Null(ModListBlock.MoveTarget(ids, shown, 7, 1, Working));
        Assert.Null(ModListBlock.MoveTarget(ids, shown, 6, 1, Working));
        // A move up between the working mods is still refused, also with them hidden.
        Assert.Null(ModListBlock.MoveTarget(ids, new[] { 3, 5 }, 5, -1, Working));
        var move = SourceFiles.Method(SourceFiles.Window(SourceFiles.Root(), "CoodszHost"), "private void MoveModule(");
        Assert.Contains("ModuleList.Items.OfType<ModuleRow>()", move);
        Assert.Contains("item.Focus();", move);
    }

    // ----- A setup picked while the server runs (wave 3 review) -----

    [Fact]
    public void A_setup_picked_while_running_copies_nothing_into_the_live_Modules_folder()
    {
        var code = SourceFiles.Window(SourceFiles.Root(), "CoodszHost");
        // The copy of a locked setup's mods (InstallLockedSetup) needs install on and no server holding the files.
        var reload = SourceFiles.Method(code, "private void ReloadModules(bool install = true)");
        var copy = reload.IndexOf("ExtraModScan.InstallLockedSetup(ServerRoot,", StringComparison.Ordinal);
        var guard = reload.IndexOf("install && !ServerHoldsModFiles", StringComparison.Ordinal);
        Assert.True(guard >= 0 && guard < copy);
        Assert.Contains("private bool ServerHoldsModFiles => _session.IsRunning || _modTestRunning;", code);
        // Picking a setup and Set server folder only read the list again while the mod list runs; Start copies later.
        var pick = SourceFiles.Method(code, "private void ProfileBox_SelectionChanged(");
        Assert.Contains("ReloadModules(install: !ModListRunning);", pick);
        Assert.DoesNotContain("ReloadModules();", pick);
        Assert.Contains("ReloadModules(install: !ModListRunning);", SourceFiles.Method(code, "private void SetServerFolder_Click("));
        Assert.Contains("ReloadModules();", SourceFiles.Method(code, "private void StartServer()"));
    }

    [Fact]
    public void The_restart_gap_and_the_modal_windows_count_as_running()
    {
        var code = SourceFiles.Window(SourceFiles.Root(), "CoodszHost");
        Assert.Contains("CrashRestartWaits || _restartingNow || _restartingAfterCrash || _controlBusy", code);
        // Add mods and Import mod list ask again after their window closes, before anything is copied or changed.
        var add = SourceFiles.Method(code, "private void AddMods_Click(");
        var choose = add.IndexOf("HostAddModsWindow.Choose(", StringComparison.Ordinal);
        var again = add.IndexOf("picked.Count == 0 || RefuseWhileModListRuns()", StringComparison.Ordinal);
        Assert.True(choose >= 0 && choose < again && again < add.IndexOf("ExtraModScan.Install(", StringComparison.Ordinal));
        var import = SourceFiles.Method(code, "private void OpenSharedListFile(");
        Assert.True(import.IndexOf("dialog.ShowDialog()", StringComparison.Ordinal) < import.IndexOf("SharedModList.ImportAfterStop", StringComparison.Ordinal));
        Assert.Contains("if (ModListRunning)", SourceFiles.Method(code, "private void FixSaveMods("));
        // The example block is for screenshots of a Debug build only.
        var example = code.IndexOf("_modsBlockedExample = Environment.GetCommandLineArgs()", StringComparison.Ordinal);
        Assert.True(code.LastIndexOf("#if DEBUG", example, StringComparison.Ordinal) > code.LastIndexOf("#endif", example, StringComparison.Ordinal));
    }
}
