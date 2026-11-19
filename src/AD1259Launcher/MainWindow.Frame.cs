using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using BannerlordCoopLauncher.Core.AD1259;
using BannerlordCoopLauncher.Core.Launch;
using BannerlordCoopLauncher.Core.Localization;
using BannerlordCoopLauncher.Core.Modules;
using BannerlordCoopLauncher.Core.Steam;
using BannerlordCoopLauncher.Core.Updates;

namespace AD1259Launcher;

// The redesign frame of the AD1259 Client Launcher (build-plan.md 4, wave 7), the same as the Coop client's: the sidebar
// with Play, Mods, Logs and help and Settings, the header with the pack name and the pill, and the Next step card and the
// Needs attention list on Play. There are no setups: the pack is fixed (AD1259Stack).
public partial class MainWindow
{
    private const double PlaySideWidth = 380;

    // Joining, In game or Refused while a join runs (WatchJoinAsync); null the rest of the time.
    private string? _joinState;

    /// <summary>Called once from the window's constructor.</summary>
    private void StartFrame()
    {
        VersionText.Text = ReleaseNotice.AD1259Version;
        PackText.ToolTip = AD1259Stack.SetText;
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

    // "Play" alone is the server time button of the Server Tools (Weiter, Reprendre), so the page name has a key of its own
    // and shows as Play in English.
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
    /// From UpdateConnectButton: the one gold button of the Next step card (ClientNextStep), the line over it, the Needs
    /// attention list and the header pill. Connect is green when it is the step (UpdateConnectButton), so it never gets the
    /// gold style too.
    /// </summary>
    private void ShowNextStep(bool connectReady)
    {
        var gameMissing = _gameRoot == null;
        List<AD1259ModStatus> problems = gameMissing ? [] : _mods.Where(mod => !mod.IsReady).ToList();
        // The pack is checked when the window opens (RefreshModules), so there is no Check mods step first.
        var step = ClientNextStep.For(gameMissing, modsChecked: true, connectReady, problems.Count);
        NextStepText.Text = ClientNextStep.Title(step, problems.Count);
        NextFindButton.Visibility = step == NextStep.FindBannerlord ? Visibility.Visible : Visibility.Collapsed;
        // Get all missing only when it has something to get: a Coop nightly or two copies are fixed by hand, as their rows say.
        var getsSomething = !gameMissing && BannerlordCoopLauncher.Core.Joining.ServerModNeeds.FromAD1259(problems).Count > 0;
        NextGetButton.Visibility = getsSomething ? Visibility.Visible : Visibility.Collapsed;
        PlayCheckModsButton.Visibility = gameMissing ? Visibility.Collapsed : Visibility.Visible;
        Primary(NextFindButton, step == NextStep.FindBannerlord);
        Primary(NextGetButton, step == NextStep.InstallMods && getsSomething);
        ShowNeedsAttention(problems);
        ShowClientPill(connectReady);
    }

    private void Primary(Button button, bool primary)
    {
        var style = (Style)FindResource(primary ? "Keep.BtnPrimary" : "Keep.BtnGhost");
        if (!ReferenceEquals(button.Style, style))
            button.Style = style;
    }

    /// <summary>The word and colour of a pack mod's status chip, on its Mods page row and in the Needs attention list.</summary>
    private static (string Word, Coodsz.Ui.ChipKind Kind) RowStatus(AD1259ModStatus status) => status.State switch
    {
        AD1259ModState.Ready => (ModChips.Ready, Coodsz.Ui.ChipKind.Ok),
        AD1259ModState.Missing => (ModChips.Missing, Coodsz.Ui.ChipKind.Bad),
        AD1259ModState.Duplicate => ("Two copies", Coodsz.Ui.ChipKind.Bad),
        _ => (ModChips.WrongVersion, Coodsz.Ui.ChipKind.Warn),
    };

    /// <summary>One mod on the Needs attention list: its name and the status chip (the Mods page words).</summary>
    private sealed record AttentionItem(string Name, string Status, string StatusKind, string StatusTip);

    /// <summary>The first pack mods that are not Ready on Play, with Open Mods; hidden when every mod is Ready.</summary>
    private void ShowNeedsAttention(IReadOnlyList<AD1259ModStatus> problems)
    {
        NeedsAttentionList.ItemsSource = problems.Take(3).Select(mod =>
        {
            var (word, kind) = RowStatus(mod);
            return new AttentionItem(mod.Mod.Name, word, kind.ToString(), mod.Problem ?? "");
        }).ToList();
        NeedsAttentionCard.Visibility = problems.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

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

    // From the join watch, which can run off the window thread.
    private void SetJoinState(string? state) => Dispatcher.BeginInvoke(() =>
    {
        _joinState = state;
        ShowClientPill(_connectReady);
    });

    // Joining, In game or Refused until the game closes, then the pill follows Connect again.
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
    /// <summary>The Made for card on Play: the game, AD1259, RBM and Coop this PC has (the last mod scan) against the pack
    /// (AD1259Stack). Coop 0.1.6 still counts while Steam moves players to 0.1.7 (AlsoAccepted). The newer game line
    /// (NewerGame) goes there and under Connect, which the base game rows keep off.</summary>
    private void ShowMadeFor()
    {
        VersionPin Pin(string id, string name, PinIcon icon)
        {
            var mod = AD1259Stack.Find(id)!;
            var found = _mods.FirstOrDefault(status => status.Mod.Id == id && status.State != AD1259ModState.Missing)?.FoundVersion;
            return new VersionPin(name, icon, mod.Version, found, mod.BaseGame, mod.AlsoAccepted);
        }

        var newerGame = NewerGame.Notice(_gameRoot);
        NewerGameText.Text = newerGame ?? "";
        NewerGameText.Visibility = newerGame == null ? Visibility.Collapsed : Visibility.Visible;
        MadeForCard.Show(new[]
        {
            Pin("Native", "Bannerlord", PinIcon.Game),
            Pin("AD1259", "AD1259", PinIcon.Mod),
            Pin("RBM", "RBM", PinIcon.Mod),
            Pin("Coop", "Coop", PinIcon.Coop),
        }, new[] { newerGame ?? "" });
    }
}
