using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using BannerlordCoopLauncher.Core.Launch;
using BannerlordCoopLauncher.Core.Localization;
using BannerlordCoopLauncher.Core.Models;
using BannerlordCoopLauncher.Core.Modules;
using BannerlordCoopLauncher.Core.Profile;
using BannerlordCoopLauncher.Core.Updates;

namespace BannerlordCoopLauncher;

// The redesign frame of the Coop client (build-plan.md 3): the sidebar with Play, Mods, Logs and help and Settings, the
// header pill, the Next step card and the Needs attention list on Play, and Move up and Move down on the Mods page.
public partial class MainWindow
{
    private const double PlaySideWidth = 380;

    // Joining, In game or Refused while a join runs (WatchJoinAsync); null the rest of the time.
    private string? _joinState;

    /// <summary>Called once from the window's constructor.</summary>
    private void StartFrame()
    {
        VersionText.Text = ReleaseNotice.FriendsVersion;
        Coodsz.Ui.NavRail.Fit(this, NavColumn, changed: ShowCompactSidebar);
        PageScroll.SizeChanged += (_, e) => FitPlay(e.NewSize.Width);
        ShowPage("Play");
        Translator.Changed += () => Dispatcher.BeginInvoke(() => ShowPage(_page));
    }

    private void Nav_Click(object sender, RoutedEventArgs e) => ShowPage((sender as FrameworkElement)?.Tag as string ?? "Play");

    // Every page of the window by its nav Tag. A new page is one line here plus its nav button in NavButtons.
    private IReadOnlyList<(string Name, FrameworkElement Page)> Pages => new (string, FrameworkElement)[]
    {
        ("Play", PageScroll),
        ("Mods", ModsCard),
        ("Logs", LogsPage),
        ("Settings", SettingsPage),
    };

    private Button[] NavButtons => new[] { NavPlay, NavMods, NavLogs, NavSettings };

    private string _page = "Play";

    private void ShowPage(string page)
    {
        _page = page;
        foreach (var (name, element) in Pages)
            element.Visibility = name == page ? Visibility.Visible : Visibility.Collapsed;
        Coodsz.Ui.NavRail.Select(NavButtons, page);
        // The title is translated here, since the Play page's word has a key of its own (PlayWord).
        NavPlayText.Text = PlayWord();
        PageTitle.Text = page switch
        {
            "Play" => PlayWord(),
            "Logs" => Translator.T("Logs and help"),
            _ => Translator.T(page),
        };
    }

    // "Play" alone is the server time button of the TAOM and BE Server Tools (Weiter, Reprendre), so the page name
    // has a key of its own and shows as Play in English.
    private const string PlayPageKey = "Play (the page name)";

    private static string PlayWord() => Translator.T(PlayPageKey) is var word && word != PlayPageKey ? word : "Play";

    // The 64 px sidebar keeps the brand mark and the icons; the footer buttons don't fit, so a More menu stands in for them.
    private void ShowCompactSidebar(bool compact)
    {
        BrandText.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        FooterFit.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        CompactMore.Visibility = compact ? Visibility.Visible : Visibility.Collapsed;
        System.Windows.Automation.AutomationProperties.SetName(CompactMore, Translator.T("More"));
    }

    // The compact sidebar's More menu clicks the same buttons the wide footer shows.
    private void CompactMore_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = CompactMore, Placement = System.Windows.Controls.Primitives.PlacementMode.Right };
        void Add(string header, string tip, object? button)
        {
            var item = new MenuItem { Header = header, ToolTip = tip };
            item.Click += (_, _) => (button as Button)?.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            menu.Items.Add(item);
        }
        Add("Language", "Picks the language of this window.", LanguageSlot.Content);
        Add("What's new", "Shows what changed in this version.", WhatsNewSlot.Content);
        Add("Donate", "Opens Coodsz's donation page in your browser.", DonateSlot.Content);
        menu.IsOpen = true;
    }

    // The Next step card goes under the game and the join when the page is too narrow for both columns.
    private void FitPlay(double width)
    {
        var stack = Coodsz.Ui.FrameRules.SideColumnWraps(width, PlaySideWidth);
        PlaySideColumn.Width = new GridLength(stack ? 0 : PlaySideWidth);
        Coodsz.Ui.PlayLayout.Fit(stack, PlayMain, PlaySide, RealmCard);
        PlayMain.Margin = new Thickness(0, 0, stack ? 0 : 16, 0);
    }

    /// <summary>
    /// The one gold button of the Next step card (ClientNextStep), the line over it and the header pill. Connect is green
    /// when it is the step (UpdateConnectButton), so it never gets the gold style too.
    /// </summary>
    private void ShowNextStep(bool connectReady)
    {
        var gameMissing = FindBannerlordButton.Visibility == Visibility.Visible;
        var attention = AttentionCount();
        // The same reason that keeps Connect off (no Coop in the list, a mod that is off and not Ready), so the card
        // never says Fill in the Join box while the mods still block Connect.
        var block = _resolution == null ? null : ModuleResolver.ConnectBlockReason(_resolution);
        var step = ClientNextStep.For(gameMissing, _resolution != null, connectReady, attention, block, searching: !_gameSearched);
        NextStepText.Text = ClientNextStep.Title(step, attention, block);
        NextFindButton.Visibility = step == NextStep.FindBannerlord ? Visibility.Visible : Visibility.Collapsed;
        NextModsButton.Visibility = step == NextStep.FixMods ? Visibility.Visible : Visibility.Collapsed;
        InstallButton.Visibility = !gameMissing && _resolution != null && attention > 0 ? Visibility.Visible : Visibility.Collapsed;
        PlayCheckModsButton.Visibility = gameMissing || step == NextStep.Searching ? Visibility.Collapsed : Visibility.Visible;
        DiagnosticButton.Visibility = _resolution == null ? Visibility.Collapsed : Visibility.Visible;
        Primary(NextFindButton, step == NextStep.FindBannerlord);
        Primary(NextModsButton, step == NextStep.FixMods);
        Primary(PlayCheckModsButton, step == NextStep.CheckMods);
        Primary(InstallButton, step == NextStep.InstallMods);
        ShowClientPill(connectReady);
    }

    private void Primary(Button button, bool primary)
    {
        var style = (Style)FindResource(primary ? "Keep.BtnPrimary" : "Keep.BtnGhost");
        if (!ReferenceEquals(button.Style, style))
            button.Style = style;
    }

    // Install mods and the Needs attention list count the same rows: a mod that is on with a red or gold chip.
    private int AttentionCount() => _resolution == null ? 0 : _rows.Count(NeedsAttention);

    private static bool NeedsAttention(ModuleRow row) =>
        row.IsOn && row.StatusKind is nameof(ModChipKind.Bad) or nameof(ModChipKind.Gold);

    private void ShowClientPill(bool connectReady)
    {
        var text = ClientNextStep.PillText(connectReady, _joinState);
        var kind = text switch
        {
            ClientNextStep.Joining => Coodsz.Ui.StatusKind.Joining,
            ClientNextStep.GameOpen => Coodsz.Ui.StatusKind.Joining,
            ClientNextStep.InGame => Coodsz.Ui.StatusKind.InGame,
            ClientNextStep.Refused => Coodsz.Ui.StatusKind.Refused,
            ClientNextStep.Ready => Coodsz.Ui.StatusKind.Ready,
            _ => Coodsz.Ui.StatusKind.NotReady,
        };
        ClientPill.Show(kind, text);
    }

    // From WatchJoinAsync, which can run off the window thread.
    private void SetJoinState(string? state) => Dispatcher.BeginInvoke(() =>
    {
        _joinState = state;
        ShowClientPill(_connectReady);
    });

    // In game until the game closes, then the pill follows Connect again.
    private void ClearJoinStateWhenClosed(Process game) =>
        _ = Task.Run(() =>
        {
            try
            {
                game.WaitForExit();
            }
            catch (Exception)
            {
                // A process that can't be watched leaves the pill as it is until the next join.
            }
            SetJoinState(null);
        });

    /// <summary>The first problem mods on Play, with Open Mods; hidden when every mod that is on is ready.</summary>
    private void ShowNeedsAttention()
    {
        // row.IsOn and a red or gold chip, the same rule as AttentionCount.
        var problems = _rows.Where(NeedsAttention).ToList();
        NeedsAttentionList.ItemsSource = problems.Take(3).ToList();
        NeedsAttentionCard.Visibility = problems.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>The Active Coop line on Play: the Coop the game loads and where it comes from.</summary>
    private void ShowActiveCoop(ModuleResolution resolution)
    {
        var coop = resolution.Modules.FirstOrDefault(module => module.Enabled && BannerlordCoopModules.IsEither(module.ModuleId) && module.Directory != null);
        ActiveCoopText.Text = coop != null && ManifestVersion(coop.Directory) is { Length: > 0 } version
            ? CoopInstall.ActiveClientLine(version, CoopInstall.IsInModules(coop.Directory, _gameRoot))
            : CoopInstall.ActiveClientMissing;
    }

    private void ModuleMoveUp(object sender, RoutedEventArgs e) => MoveRow((sender as FrameworkElement)?.DataContext as ModuleRow, -1);

    private void ModuleMoveDown(object sender, RoutedEventArgs e) => MoveRow((sender as FrameworkElement)?.DataContext as ModuleRow, 1);

    /// <summary>Move up (-1) or Move down (+1) one place, by the drag rules: a locked setup and the working Coop mods keep
    /// their order (WorkingLoadOrder.CanMove), and the new order is saved like after a drag.</summary>
    private void MoveRow(ModuleRow? row, int delta)
    {
        if (row == null || _profile == null)
            return;
        if (_settings.Current().Locked)
        {
            ModuleSaveHint.Text = "This setup is locked. The load order cannot be changed.";
            ModuleSaveHint.Visibility = Visibility.Visible;
            return;
        }

        var from = _rows.IndexOf(row);
        var to = from + delta;
        if (from < 0 || to < 0 || to >= _rows.Count)
            return;
        var order = DefaultClientSet.Fixed;
        if (!row.CanMove || !WorkingLoadOrder.CanMove(_rows.Select(item => item.ModuleId).ToList(), from, to, order))
        {
            ModuleSaveHint.Text = "That mod stays in the working load order. Moving it, or putting another mod between those mods, can break the game. Extra mods can move below that list.";
            ModuleSaveHint.Visibility = Visibility.Visible;
            return;
        }

        var focused = ModuleList.IsKeyboardFocusWithin;
        _rows.Move(from, to);
        for (var i = 0; i < _rows.Count; i++)
            _rows[i].Order = i + 1;
        SaveRowOrder();
        // A move gives the row a new container, which drops the focus: put it back on the same button of the moved row,
        // so Alt+Up or the button pressed again keeps moving the same mod. At the edge that arrow is off now and can't hold
        // the focus, so the other arrow or the row's first control takes it (MoveFocus), and the focus stays in the list.
        if (focused)
            Dispatcher.BeginInvoke(() =>
            {
                if (ModuleList.ItemContainerGenerator.ContainerFromItem(row) is not FrameworkElement container)
                    return;
                var buttons = FindMoveButtons(container);
                var target = buttons == null
                    ? Core.Modules.MoveFocusTarget.Row
                    : Core.Modules.MoveFocus.After(delta < 0, buttons.UpButton.IsEnabled, buttons.DownButton.IsEnabled);
                if (target == Core.Modules.MoveFocusTarget.Up)
                    buttons!.UpButton.Focus();
                else if (target == Core.Modules.MoveFocusTarget.Down)
                    buttons!.DownButton.Focus();
                else
                    container.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
            }, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    /// <summary>Turns Move up off on the first movable row and Move down off on the last one, after every add, clear or
    /// move, so the arrow that would do nothing is greyed out. Rows that keep their spot stay off both ways.</summary>
    private void RefreshMoveEdges()
    {
        var movable = _rows.Where(row => row.CanMove).ToList();
        foreach (var row in _rows)
        {
            row.CanMoveUp = row.CanMove && !ReferenceEquals(row, movable[0]);
            row.CanMoveDown = row.CanMove && !ReferenceEquals(row, movable[^1]);
        }
    }

    private static Coodsz.Ui.MoveButtons? FindMoveButtons(DependencyObject node)
    {
        if (node is Coodsz.Ui.MoveButtons buttons)
            return buttons;
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(node); i++)
            if (FindMoveButtons(System.Windows.Media.VisualTreeHelper.GetChild(node, i)) is { } found)
                return found;
        return null;
    }

    // Alt+Up and Alt+Down move the row that has the focus (its switch or a button in it).
    private void ModuleListKeys(object sender, KeyEventArgs e)
    {
        if ((e.OriginalSource as FrameworkElement)?.DataContext is not ModuleRow row)
            return;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (Keyboard.Modifiers == ModifierKeys.Alt && (key == Key.Up || key == Key.Down))
        {
            MoveRow(row, key == Key.Up ? -1 : 1);
            e.Handled = true;
        }
    }
}
