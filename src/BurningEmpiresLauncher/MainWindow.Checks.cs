using System.IO;
using System.Windows;
using BannerlordCoopLauncher.Core.IO;
using BannerlordCoopLauncher.Core.Parsing;
using BurningEmpires.Core;
using TaomCoop.Core;

namespace BurningEmpiresLauncher;

// Auto sort and the checks before Connect, from each mod's SubModule.xml and the Burning Empires rules.
public partial class MainWindow
{
    // ----- The UIExtenderEx prefabs fix. Burning Empires has no dependency fix: the BUTR modules
    // load from the Workshop in the core, so a mod keeps its own SubModule.xml. -----

    private List<string> ModRoots()
    {
        var roots = Workshops().Select(library => library.ContentDirectory).ToList();
        var modules = GameModulesFolder();
        if (modules != null)
            roots.Add(modules);
        return roots;
    }

    // Puts back every SubModule.xml the prefabs fix changed. Returns how many were put back.
    private int RestorePrefabsFix()
    {
        var problems = new List<string>();
        var restored = BannerlordCoopLauncher.Core.Modules.UIExtenderExPrefabsFix.RestoreAll(ModRoots(), problems);
        foreach (var folder in restored)
            Log("Put back the original SubModule.xml of " + Path.GetFileName(folder) + ".");
        foreach (var problem in problems)
            Log(problem);
        return restored.Count;
    }

    // Right before the game starts: a UIExtenderEx copy missing a DLL its Compiled Prefabs need gets
    // them turned off (see UIExtenderExPrefabsFix). It is put back when the game closes.
    private void ApplyPrefabsFix(TaomSetup setup)
    {
        RestorePrefabsFix();
        foreach (var line in BannerlordCoopLauncher.Core.Modules.UIExtenderExPrefabsFix.ApplyForLaunch(BeLoadOrder.LaunchModules(setup, joinPassword: false), ModRoots()))
            Log(line);
        if (!setup.HasEnabledExtras)
            return;
        UnblockModDlls(setup);
    }

    // Starts the game with the prefabs fix in place. When the game does not start (no process or an
    // error), the fix is put back right away, since no Exited event ever will.
    private System.Diagnostics.Process StartWithPrefabsFix(TaomSetup setup, System.Diagnostics.ProcessStartInfo start)
    {
        ApplyPrefabsFix(setup);
        System.Diagnostics.Process? started = null;
        try
        {
            started = System.Diagnostics.Process.Start(start);
        }
        finally
        {
            if (started == null)
                RestorePrefabsFix();
        }

        return started ?? throw new InvalidOperationException("Bannerlord did not start.");
    }

    // Windows marks DLLs from a downloaded zip, and the game cannot load a marked DLL. Done before
    // every launch and after Install mods, since an update brings the mark back.
    private void UnblockModDlls(TaomSetup setup, IReadOnlyDictionary<string, TaomModFact>? facts = null)
    {
        facts ??= GatherModFacts();
        foreach (var mod in setup.EnabledExtras)
        {
            if (!facts.TryGetValue(mod.ModuleId, out var fact))
                continue;
            foreach (var folder in fact.Copies)
            {
                try
                {
                    var count = BannerlordCoopLauncher.Core.Support.ModUnblock.UnblockDlls(folder);
                    if (count > 0)
                        Log(BannerlordCoopLauncher.Core.Support.ModUnblock.Line(mod.Label, count));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    Log("Could not unblock the DLL files of " + mod.Label + ". " + ex.Message);
                }
            }
        }
    }

    private void RestoreWhenClosed(System.Diagnostics.Process game)
    {
        try
        {
            game.Exited += (_, _) => Dispatcher.Invoke(() => RestorePrefabsFix());
            game.EnableRaisingEvents = true;
            // A game that closed before the handler was set may not raise Exited; putting the fix back twice is harmless.
            if (game.HasExited)
                RestorePrefabsFix();
        }
        catch (InvalidOperationException)
        {
            // The process can no longer be watched, so no Exited event comes to put the fix back.
            RestorePrefabsFix();
        }
    }

    // Every module folder the game can load: Steam Workshop items and the game's Modules folder.
    private IReadOnlyDictionary<string, TaomModFact> GatherModFacts()
    {
        var roots = Workshops().Select(library => library.ContentDirectory).ToList();
        var modules = GameModulesFolder();
        if (modules != null)
            roots.Add(modules);
        return BeModFacts.Gather(roots);
    }

    private static IReadOnlyDictionary<string, ModuleManifest> Manifests(IReadOnlyDictionary<string, TaomModFact> facts) =>
        facts.Where(pair => pair.Value.Manifest != null)
            .ToDictionary(pair => pair.Key, pair => pair.Value.Manifest!, StringComparer.OrdinalIgnoreCase);

    private void AutoSort_Click(object sender, RoutedEventArgs e)
    {
        if (_editing.Locked)
        {
            Log(_editing.Name + " is locked. Click New setup or Copy to make a setup you can add mods to.");
            return;
        }

        var lines = BeAutoSort.Sort(_editing, Manifests(GatherModFacts()));
        ChangedMods();
        if (lines.Count == 0)
        {
            Log("Auto sort found nothing to change. The extra mods already load in a good order.");
            return;
        }

        foreach (var line in lines)
            Log(line);
        Log("Click Save mods to keep the new order.");
    }

    // ----- The Burning Empires compatibility list -----

    private BeCompatList _compat = BeCompatList.BuiltIn();

    private static string CompatCachePath => ToolData.File(BeCompatList.FileName);

    private void LoadCompatList()
    {
        _compat = BeCompatList.Load(CompatCachePath);
        _ = RefreshCompatListAsync();
    }

    // Takes the online list when it can be read; otherwise the saved or built in list stays.
    private async Task RefreshCompatListAsync()
    {
        using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        var online = await BeCompatList.DownloadAsync(http, CompatCachePath, CancellationToken.None);
        if (online == null)
            return;
        _compat = online;
        Dispatcher.Invoke(ShowModuleList);
    }

    private static System.Windows.Media.Color BadgeColor(CompatTag tag) => BeCompatList.BadgeColor(tag) switch
    {
        "green" => System.Windows.Media.Color.FromRgb(0x8F, 0xBF, 0x86),
        "red" => System.Windows.Media.Color.FromRgb(0xE0, 0x7A, 0x6E),
        "blue" => System.Windows.Media.Color.FromRgb(0x8E, 0xB4, 0xD4),
        _ => System.Windows.Media.Color.FromRgb(0xE2, 0xC2, 0x7A)
    };

    // The note, then where it comes from and when it was checked (the source line is translated).
    private static string CompatHover(CompatEntry entry) =>
        BeCompatList.Hover(entry, BannerlordCoopLauncher.Core.Localization.Translator.T);

    private BannerlordCoopLauncher.ModBadge? PickerBadge(BannerlordCoopLauncher.Core.Profile.FoundMod mod)
    {
        var mine = BeModResults.IsExtraMod(mod.ModuleId)
            ? BeModResults.Pill(mod.ModuleId, BannerlordCoopLauncher.ModPills.VersionIn(mod.Directory))
            : null;
        if (BeModResults.ShowsUnknown(_compat, mod.ModuleId, mod.WorkshopId, mod.Name))
            return BannerlordCoopLauncher.ModPills.Unknown(BannerlordCoopLauncher.Core.Modules.UnknownModBadge.Tip, mine);
        var entry = _compat.Find(mod.ModuleId, mod.WorkshopId, mod.Name);
        var text = entry == null ? null : BeCompatList.BadgeText(entry.Kind);
        return entry == null || text == null ? null : new BannerlordCoopLauncher.ModBadge(text, BadgeColor(entry.Kind), CompatHover(entry), mine);
    }

    // The status chips of a mod row, in the order Coodsz Coop and Friends shows them: the Burning Empires
    // list status (Unknown, not tested in grey when the list does not know the mod), ModderLords' verdict,
    // the green and red counts of the reports sent with tool be, this PC's own Burning Empires result and
    // the Report link. The Ready pill sits just before this column (StatusPill).
    // A fixed column: the chips wrap to a second line when they do not fit, and a chip wider than the
    // column is trimmed (the hover has the text).
    internal const double BadgeColumnWidth = Coodsz.Ui.ModRowLayout.ClientChipColumn;

    private FrameworkElement? CompatBadge(string moduleId, string? workshopId, string label, string? directory = null)
    {
        // With the report endpoint set, a click opens the Works, Broken and Works only with buttons.
        var report = BannerlordCoopLauncher.Core.Modules.ModReports.HasEndpoint();
        var status = BeModChips.Status(_compat, moduleId, workshopId, label, report, BannerlordCoopLauncher.Core.Localization.Translator.T);
        if (status == null)
            return null;
        var panel = BadgeColumn();
        var (ink, back) = ChipColors(status.Kind);
        var badge = new System.Windows.Controls.Border
        {
            Background = back,
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(7, 1, 7, 1),
            Margin = new Thickness(0, 1, 6, 1),
            MaxWidth = BadgeColumnWidth,
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = status.Text + "\n" + status.Tip,
            Child = new System.Windows.Controls.TextBlock
            {
                Text = status.Text,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = ink
            }
        };
        void Report() => Coodsz.Ui.ModReportWindow.Show(this, ReportRequest(new[] { (moduleId, (string?)InstalledVersion(moduleId)) }, LaunchOrder(), afterGame: false, crashed: false));
        if (report)
        {
            badge.Cursor = System.Windows.Input.Cursors.Hand;
            badge.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                Report();
            };
        }
        panel.Children.Add(badge);

        // ModderLords' verdict, then what players of the Burning Empires tools reported, as green and red count chips.
        var version = BannerlordCoopLauncher.ModPills.VersionIn(directory);
        if (CommunityCounts(moduleId, workshopId, label, version) is { } chips)
            AddChips(panel, chips);

        var mine = BeModResults.Pill(moduleId, version);
        if (mine != null)
        {
            var pill = BannerlordCoopLauncher.ModPills.Mine(mine, new Thickness(0, 1, 6, 1));
            pill.MaxWidth = BadgeColumnWidth;
            panel.Children.Add(pill);
        }

        // The small Report link, the same as a click on the status chip, so reporting is plain to see.
        if (report)
            panel.Children.Add(Coodsz.Ui.ModReportUi.RowLink(this, Report));
        return panel;
    }

    // A Burning Empires core row (Burning Empires, Elephants, the compat module, the BUTR modules): ModderLords'
    // verdict, what players reported, this PC's own result and the Report link, like the TAOM client's core
    // rows. The list status stays for the extra mods. Null for Coop and the official modules.
    private FrameworkElement? PackModChips(string moduleId, string? directory)
    {
        if (!BannerlordCoopLauncher.Core.Modules.ModReports.Rates(moduleId))
            return null;
        var panel = BadgeColumn();
        // The core folder's own version, so ModderLords' verdict and this PC's result match the copy on this PC.
        var version = BannerlordCoopLauncher.ModPills.VersionIn(directory);
        if (CommunityCounts(moduleId, null, null, version) is { } chips)
            AddChips(panel, chips);
        var mine = BeModResults.Pill(moduleId, version);
        if (mine != null)
        {
            var pill = BannerlordCoopLauncher.ModPills.Mine(mine, new Thickness(0, 1, 6, 1));
            pill.MaxWidth = BadgeColumnWidth;
            panel.Children.Add(pill);
        }

        if (BannerlordCoopLauncher.Core.Modules.ModReports.HasEndpoint())
            panel.Children.Add(Coodsz.Ui.ModReportUi.RowLink(this, () => Coodsz.Ui.ModReportWindow.Show(this,
                ReportRequest(new[] { (moduleId, (string?)version) }, LaunchOrder(), afterGame: false, crashed: false))));
        return panel.Children.Count == 0 ? null : panel;
    }

    // CountChips gives one row of chips; each chip goes into the column on its own, so a chip that does not
    // fit wraps to the next line like in Coop and Friends instead of being cut off. A chip keeps its row's tip.
    private static void AddChips(System.Windows.Controls.Panel column, FrameworkElement chips)
    {
        if (chips is not System.Windows.Controls.StackPanel row)
        {
            column.Children.Add(chips);
            return;
        }

        foreach (var chip in row.Children.OfType<FrameworkElement>().ToList())
        {
            row.Children.Remove(chip);
            chip.ToolTip ??= row.ToolTip;
            AddChips(column, chip);
        }
    }

    // The colors of a status chip, the same as Coodsz Coop and Friends' list badge: green, gold, blue, red or grey.
    private (System.Windows.Media.Brush Ink, System.Windows.Media.Brush Back) ChipColors(string kind) => kind switch
    {
        BeModChips.Works => (Coodsz.Ui.ModReportUi.Green(this), Coodsz.Ui.ModReportUi.GreenBack(this)),
        BeModChips.Broken => (Coodsz.Ui.ModReportUi.Red(this), Coodsz.Ui.ModReportUi.RedBack(this)),
        BeModChips.Risky => (Coodsz.Ui.ModReportUi.Gold(this), Coodsz.Ui.ModReportUi.GoldBack(this)),
        BeModChips.Reported => (ChipBrush(0x8E, 0xB4, 0xD4), ChipBrush(0x1C, 0x2A, 0x3A)),
        _ => (ChipBrush(0x8E, 0xA0, 0xAE), ChipBrush(0x24, 0x30, 0x3A))
    };

    private static System.Windows.Media.SolidColorBrush ChipBrush(byte r, byte g, byte b) => new(System.Windows.Media.Color.FromRgb(r, g, b));

    internal const double StatusColumnWidth = 104;

    /// <summary>The Ready, Off, Missing or Wrong version chip of a row, the shared Coodsz.Ui.Chip like TAOM's rows, in
    /// its colour from ModChips.Status (the English word; UiLanguage shows it in the picked language), in a fixed column
    /// so the chips after it line up on every row.</summary>
    private System.Windows.Controls.Border StatusPill(string text, string kind)
    {
        var chip = BannerlordCoopLauncher.Core.Modules.ModChips.Status(kind);
        var pill = new Coodsz.Ui.Chip
        {
            Text = text,
            Kind = Enum.Parse<Coodsz.Ui.ChipKind>(chip.KindName),
            Tip = chip.Tip,
            MaxWidth = StatusColumnWidth,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center
        };
        System.Windows.Automation.AutomationProperties.SetAutomationId(pill, "RowStatusPill");
        return new System.Windows.Controls.Border { Width = StatusColumnWidth, VerticalAlignment = VerticalAlignment.Center, Child = pill };
    }

    // The report window for these mods (the Burning Empires list), with the setup's launch order.
    private Coodsz.Ui.ModReportRequest ReportRequest(IEnumerable<(string Id, string? Version)> rate, IEnumerable<string> loadOrder, bool afterGame, bool crashed) => new()
    {
        Tool = "be",
        ToolVersion = typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "",
        Coop = CoopVersionHere(),
        Game = BeStack.GameVersion,
        Rate = rate
            .Where(mod => BannerlordCoopLauncher.Core.Modules.ModReports.Rates(mod.Id))
            .Select(mod => new Coodsz.Ui.ModReportChoice(mod.Id, LaunchSetup.Extras.Concat(_editing.Extras).FirstOrDefault(item => item.ModuleId.Equals(mod.Id, StringComparison.OrdinalIgnoreCase))?.Label ?? CoreName(mod.Id), mod.Version ?? ""))
            .ToList(),
        ModList = BannerlordCoopLauncher.Core.Modules.ModReports.ModList(loadOrder.Select(id => (id, (string?)ReportVersion(id)))),
        AfterGame = afterGame,
        Crashed = crashed
    };

    // The version a report gives a mod of the load order: the extra's copy, else the core module's own.
    private string ReportVersion(string moduleId) =>
        InstalledVersion(moduleId) is { Length: > 0 } version ? version : CoreVersion(moduleId);

    // The tools follow the Workshop Coop, so reports carry the Coop on this PC, or the tested one.
    private string CoopVersionHere()
    {
        var version = CoreVersion(BeStack.CoopModuleId);
        return version.Length > 0 ? version : BeStack.CoopVersion;
    }

    /// <summary>The badge column of a mod row: one fixed width for every row, so the move and remove
    /// buttons of each row line up whether a row has no badge, one or two.</summary>
    internal static System.Windows.Controls.WrapPanel BadgeColumn() => new()
    {
        Orientation = System.Windows.Controls.Orientation.Horizontal,
        Margin = new Thickness(0, 0, 8, 0),
        VerticalAlignment = VerticalAlignment.Center,
        Width = BadgeColumnWidth
    };

    // A known broken mod is only added when the player says so.
    private bool ConfirmBrokenMod(string name, string moduleId, string? workshopId, Window? owner = null)
    {
        var entry = _compat.Find(moduleId, workshopId, name);
        if (entry is not { Kind: CompatTag.Broken })
            return true;
        return Coodsz.Ui.UiLanguage.Message(BeCompatList.BrokenWarning(name, entry) + "\n\nAdd it anyway?", "Known broken mod",
            MessageBoxButton.YesNo, MessageBoxImage.Warning, owner ?? this) == MessageBoxResult.Yes;
    }

    // ----- The Mod list window -----

    private void ModList_Click(object sender, RoutedEventArgs e) =>
        ModListWindow.Show(this, _compat, _editing.Name, AddFromModList);

    // The Full Burning Empires mod list link on the mods page: MODS.md in the Burning Empires tools repository.
    private void FullModList_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(BeCompatList.ModsPageUrl) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
        }
    }

    // Add to setup in the Mod list window: the same path as Add mods (the copy on this PC, the load
    // order rules, the known broken prompt, a launcher folder mod copied into Modules). A mod that is
    // not on this PC yet is added by its list id, so Install mods can get it. Returns the line to show.
    private string AddFromModList(CompatEntry entry, Window owner)
    {
        if (_editing.Locked)
            return BeModList.LockedText(_editing.Name);
        var found = BeModList.OnThisPc(entry, ScanForAdding());
        var target = BeModList.AddTarget(entry, found, out var reason);
        if (target is not { } mod)
            return reason ?? "";
        if (!ConfirmBrokenMod(mod.Name, mod.ModuleId, mod.WorkshopId, owner))
            return mod.Name + " was not added.";
        var refusal = BeLoadOrder.Add(_editing, mod.Name, mod.ModuleId, mod.WorkshopId);
        if (refusal != null)
            return refusal;
        if (found != null)
        {
            try
            {
                CopyIntoModules(found);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log("Could not copy " + found.Name + " into the Modules folder. " + ex.Message);
            }
        }

        ScanInstalledExtras();
        ChangedMods();
        var line = found != null
            ? "Added " + mod.Name + " to " + _editing.Name + ". Click Save mods to keep it."
            : BannerlordCoopLauncher.Core.Modules.SharedModList.IsWorkshopId(mod.WorkshopId)
                ? "Added " + mod.Name + " to " + _editing.Name + ". It is not on this PC yet, so click Install mods, then Save mods."
                : "Added " + mod.Name + " to " + _editing.Name + ". It is not on this PC yet. Get it from Nexus Mods or ModDB, then click Save mods.";
        Log(line);
        return line;
    }

    /// <summary>Logs the checks for a setup. False when a problem blocks the launch.</summary>
    private bool ModChecksPass(TaomSetup setup)
    {
        if (BeLoadOrder.ClientOrder(setup).Count == BeStack.ClientModules.Length)
            return true;
        IReadOnlyList<TaomModIssue> issues;
        try
        {
            issues = BeModChecks.Run(setup, GatherModFacts(), _compat);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Log("Could not check the extra mods. " + ex.Message);
            return true;
        }

        foreach (var issue in issues)
            Log(issue.Text);
        var blocking = issues.Where(issue => issue.Blocks).ToList();
        if (blocking.Count == 0)
            return true;
        ShowJoin("Fix the mod problems first. " + blocking[0].Text);
        return false;
    }
}
