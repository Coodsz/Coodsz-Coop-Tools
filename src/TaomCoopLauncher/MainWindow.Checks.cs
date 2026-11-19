using System.IO;
using System.Windows;
using BannerlordCoopLauncher.Core.IO;
using BannerlordCoopLauncher.Core.Parsing;
using TaomCoop.Core;

namespace TaomCoopLauncher;

// Auto sort and the checks before Connect, from each mod's SubModule.xml and the TAOM rules.
public partial class MainWindow
{
    // ----- The TAOM dependency fix (see TaomDependencyFix for why it is done in place) -----

    private List<string> ModRoots()
    {
        var roots = Workshops().Select(library => library.ContentDirectory).ToList();
        var modules = GameModulesFolder();
        if (modules != null)
            roots.Add(modules);
        return roots;
    }

    // Puts back every SubModule.xml the fix changed. Returns how many were put back.
    private int RestoreDependencyFix()
    {
        var problems = new List<string>();
        var roots = ModRoots();
        var restored = BannerlordCoopLauncher.Core.Modules.TaomDependencyFix.RestoreAll(roots, problems)
            .Concat(BannerlordCoopLauncher.Core.Modules.UIExtenderExPrefabsFix.RestoreAll(roots, problems))
            .ToList();
        foreach (var folder in restored)
            Log("Put back the original SubModule.xml of " + Path.GetFileName(folder) + ".");
        foreach (var problem in problems)
            Log(problem);
        return restored.Count;
    }

    // Right before the game starts: takes the Harmony, ButterLib, UIExtenderEx and MCM entries out of
    // the SubModule.xml of each extra mod that asks for them. They are put back when the game closes.
    private void ApplyDependencyFix(TaomSetup setup)
    {
        RestoreDependencyFix();
        // A UIExtenderEx copy missing a DLL its Compiled Prefabs need gets them turned off (see UIExtenderExPrefabsFix).
        foreach (var line in BannerlordCoopLauncher.Core.Modules.UIExtenderExPrefabsFix.ApplyForLaunch(TaomLoadOrder.LaunchModules(setup, joinPassword: false), ModRoots()))
            Log(line);
        if (!setup.HasEnabledExtras)
            return;
        var facts = GatherModFacts();
        foreach (var mod in setup.EnabledExtras)
        {
            if (!facts.TryGetValue(mod.ModuleId, out var fact))
                continue;
            foreach (var folder in fact.Copies)
            {
                try
                {
                    var line = BannerlordCoopLauncher.Core.Modules.TaomDependencyFix.Apply(folder, mod.Label);
                    if (line != null)
                        Log(line);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    Log("Could not apply the TAOM dependency fix to " + mod.Label + ". " + ex.Message);
                }
            }
        }

        UnblockModDlls(setup, facts);
    }

    // Windows marks DLLs from a downloaded zip, and the game cannot load a marked DLL. Done before
    // every TAOM launch and after Install mods, since an update brings the mark back.
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
            game.EnableRaisingEvents = true;
            game.Exited += (_, _) => Dispatcher.Invoke(() => RestoreDependencyFix());
        }
        catch (InvalidOperationException)
        {
        }
    }

    private void UndoDependencyFix_Click(object sender, RoutedEventArgs e)
    {
        if (System.Diagnostics.Process.GetProcessesByName("Bannerlord").Length > 0)
        {
            Log("Close Bannerlord first. The TAOM dependency fix is put back when the game closes.");
            return;
        }

        if (RestoreDependencyFix() == 0)
            Log("No SubModule.xml is changed by the TAOM dependency fix right now.");
        ScanInstalledExtras();
        ShowModuleList();
    }

    // Every module folder the game can load: Steam Workshop items and the game's Modules folder.
    private IReadOnlyDictionary<string, TaomModFact> GatherModFacts()
    {
        var roots = Workshops().Select(library => library.ContentDirectory).ToList();
        var modules = GameModulesFolder();
        if (modules != null)
            roots.Add(modules);
        return TaomModFacts.Gather(roots);
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

        var lines = TaomAutoSort.Sort(_editing, Manifests(GatherModFacts()));
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

    // ----- The TAOM compatibility list -----

    private TaomCompatList _compat = TaomCompatList.BuiltIn();

    private static string CompatCachePath => ToolData.File(TaomCompatList.FileName);

    private void LoadCompatList()
    {
        _compat = TaomCompatList.Load(CompatCachePath);
        _ = RefreshCompatListAsync();
    }

    // Takes the online list when it can be read; otherwise the saved or built in list stays.
    private async Task RefreshCompatListAsync()
    {
        using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        var online = await TaomCompatList.DownloadAsync(http, CompatCachePath, CancellationToken.None);
        if (online == null)
            return;
        _compat = online;
        Dispatcher.Invoke(ShowModuleList);
    }

    private static System.Windows.Media.Color BadgeColor(CompatTag tag) => TaomCompatList.BadgeColor(tag) switch
    {
        "green" => System.Windows.Media.Color.FromRgb(0x8F, 0xBF, 0x86),
        "red" => System.Windows.Media.Color.FromRgb(0xE0, 0x7A, 0x6E),
        "blue" => System.Windows.Media.Color.FromRgb(0x8E, 0xB4, 0xD4),
        _ => System.Windows.Media.Color.FromRgb(0xE2, 0xC2, 0x7A)
    };

    // The note, then where it comes from and when it was checked (the source line is translated).
    private static string CompatHover(CompatEntry entry) =>
        TaomCompatList.Hover(entry, BannerlordCoopLauncher.Core.Localization.Translator.T);

    private BannerlordCoopLauncher.ModBadge? PickerBadge(BannerlordCoopLauncher.Core.Profile.FoundMod mod)
    {
        var mine = TaomModResults.IsExtraMod(mod.ModuleId)
            ? BannerlordCoopLauncher.Core.Modules.ModResults.Pill(mod.ModuleId, BannerlordCoopLauncher.ModPills.VersionIn(mod.Directory))
            : null;
        if (TaomModResults.ShowsUnknown(_compat, mod.ModuleId, mod.WorkshopId, mod.Name))
            return BannerlordCoopLauncher.ModPills.Unknown(BannerlordCoopLauncher.Core.Modules.UnknownModBadge.Tip, mine);
        var entry = _compat.Find(mod.ModuleId, mod.WorkshopId, mod.Name);
        var text = entry == null ? null : TaomCompatList.BadgeText(entry.Kind);
        return entry == null || text == null ? null : new BannerlordCoopLauncher.ModBadge(text, BadgeColor(entry.Kind), CompatHover(entry), mine);
    }

    // The TAOM list badge of a mod row (Unknown, not tested in grey when the list does not know
    // the mod), and this PC's own result next to it.
    // A fixed column: Tested safe and Passed on your server fit side by side; longer pairs put the
    // second pill on a line below, and a pill wider than the column is trimmed (the hover has the text).
    internal const double BadgeColumnWidth = Coodsz.Ui.ModRowLayout.ClientChipColumn;

    private FrameworkElement? CompatBadge(string moduleId, string? workshopId, string label, string? directory = null)
    {
        if (!TaomModResults.IsExtraMod(moduleId))
            return null;
        var entry = _compat.Find(moduleId, workshopId, label);
        var text = entry == null ? null : TaomCompatList.BadgeText(entry.Kind);
        var unknown = entry == null || text == null;
        var badgeText = unknown ? BannerlordCoopLauncher.Core.Modules.UnknownModBadge.Text : text!;
        var panel = BadgeColumn();
        // With the report endpoint set, a click opens the Works, Broken and Works only with buttons.
        var report = BannerlordCoopLauncher.Core.Modules.ModReports.HasEndpoint();
        var badge = new System.Windows.Controls.Border
        {
            Background = new System.Windows.Media.SolidColorBrush(unknown ? BannerlordCoopLauncher.ModPills.UnknownGrey : BadgeColor(entry!.Kind)),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 2, 8, 2),
            Margin = new Thickness(0, 1, 6, 1),
            MaxWidth = BadgeColumnWidth,
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = badgeText + "\n" + (unknown ? BannerlordCoopLauncher.Core.Modules.UnknownModBadge.Tip : CompatHover(entry!))
                + (report ? "\n" + BannerlordCoopLauncher.Core.Modules.ModReports.ClickTip : ""),
            Child = new System.Windows.Controls.TextBlock
            {
                Text = badgeText,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(16, 18, 20))
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
        var mine = BannerlordCoopLauncher.Core.Modules.ModResults.Pill(moduleId, BannerlordCoopLauncher.ModPills.VersionIn(directory));
        if (mine != null)
        {
            var pill = BannerlordCoopLauncher.ModPills.Mine(mine, new Thickness(0, 1, 0, 1));
            pill.MaxWidth = BadgeColumnWidth;
            panel.Children.Add(pill);
        }

        // What players of the Coodsz tools reported for this mod, as green and red count chips.
        if (CommunityCounts(moduleId, workshopId) is { } chips)
            panel.Children.Add(chips);

        // The small Report link, the same as a click on the badge, so reporting is plain to see.
        if (report)
            panel.Children.Add(Coodsz.Ui.ModReportUi.RowLink(this, Report));
        return panel;
    }

    // A TAOM core mod row: what players reported (green and red chips, ModderLords' verdict), Passed on
    // your server, and the Report link, like every mod row in Coop and Friends. The TAOM list badge stays
    // for the extra mods only. Null for the base game modules.
    private FrameworkElement? PackModChips(string moduleId, string? directory)
    {
        if (!BannerlordCoopLauncher.Core.Modules.UnknownModBadge.Applies(moduleId))
            return null;
        var panel = BadgeColumn();
        var mine = BannerlordCoopLauncher.Core.Modules.ModResults.Pill(moduleId, BannerlordCoopLauncher.ModPills.VersionIn(directory));
        if (mine != null)
        {
            var pill = BannerlordCoopLauncher.ModPills.Mine(mine, new Thickness(0, 1, 0, 1));
            pill.MaxWidth = BadgeColumnWidth;
            panel.Children.Add(pill);
        }

        if (CommunityCounts(moduleId, null) is { } chips)
            panel.Children.Add(chips);
        if (BannerlordCoopLauncher.Core.Modules.ModReports.HasEndpoint())
            panel.Children.Add(Coodsz.Ui.ModReportUi.RowLink(this, () => Coodsz.Ui.ModReportWindow.Show(this,
                ReportRequest(new[] { (moduleId, (string?)InstalledVersion(moduleId)) }, LaunchOrder(), afterGame: false, crashed: false, core: true))));
        return panel.Children.Count == 0 ? null : panel;
    }

    // The report window for these mods (the TAOM list), with the setup's launch order. Only extra mods are
    // rated, except a core mod's own Report link (core), which rates that core mod.
    private Coodsz.Ui.ModReportRequest ReportRequest(IEnumerable<(string Id, string? Version)> rate, IEnumerable<string> loadOrder, bool afterGame, bool crashed, bool core = false) => new()
    {
        Tool = "taom",
        ToolVersion = typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "",
        Coop = TaomStack.CoopVersion,
        Game = TaomStack.GameVersion,
        Rate = rate
            .Where(mod => TaomModResults.IsExtraMod(mod.Id) || (core && BannerlordCoopLauncher.Core.Modules.UnknownModBadge.Applies(mod.Id)))
            .Select(mod => new Coodsz.Ui.ModReportChoice(mod.Id, LaunchSetup.Extras.FirstOrDefault(item => item.ModuleId.Equals(mod.Id, StringComparison.OrdinalIgnoreCase))?.Label ?? mod.Id, mod.Version ?? ""))
            .ToList(),
        ModList = BannerlordCoopLauncher.Core.Modules.ModReports.ModList(loadOrder.Select(id => (id, (string?)InstalledVersion(id)))),
        AfterGame = afterGame,
        Crashed = crashed
    };

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
        return Coodsz.Ui.UiLanguage.Message(TaomCompatList.BrokenWarning(name, entry) + "\n\nAdd it anyway?", "Known broken mod",
            MessageBoxButton.YesNo, MessageBoxImage.Warning, owner ?? this) == MessageBoxResult.Yes;
    }

    // ----- The Mod list window -----

    private void ModList_Click(object sender, RoutedEventArgs e) =>
        ModListWindow.Show(this, _compat, _editing.Name, AddFromModList);

    // The Full TAOM mod list link on the mods page: MODS.md in the Coodsz-TAOM repository.
    private void FullModList_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(TaomCompatList.ModsPageUrl) { UseShellExecute = true });
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
            return TaomModList.LockedText(_editing.Name);
        var found = TaomModList.OnThisPc(entry, ScanForAdding());
        var target = TaomModList.AddTarget(entry, found, out var reason);
        if (target is not { } mod)
            return reason ?? "";
        if (!ConfirmBrokenMod(mod.Name, mod.ModuleId, mod.WorkshopId, owner))
            return mod.Name + " was not added.";
        var refusal = TaomLoadOrder.Add(_editing, mod.Name, mod.ModuleId, mod.WorkshopId);
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
        if (TaomLoadOrder.ClientOrder(setup).Count == TaomStack.ClientModules.Length)
            return true;
        IReadOnlyList<TaomModIssue> issues;
        try
        {
            issues = TaomModChecks.Run(setup, GatherModFacts(), _compat);
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
