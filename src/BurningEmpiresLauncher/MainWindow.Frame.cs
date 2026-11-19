using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using BannerlordCoopLauncher.Core.Launch;
using BannerlordCoopLauncher.Core.Localization;
using BannerlordCoopLauncher.Core.Modules;
using BannerlordCoopLauncher.Core.Steam;
using BannerlordCoopLauncher.Core.Updates;
using BurningEmpires.Core;
using TaomCoop.Core;

namespace BurningEmpiresLauncher;

// The redesign frame of the Burning Empires Client Launcher (build-plan.md 4, wave 7), the same as the Coop client's: the sidebar
// with Play, Mods, Logs and help, Settings and Guides, the header pill, and the Next step card and the Needs attention
// list on Play.
public partial class MainWindow
{
    private const double PlaySideWidth = 380;

    // Joining, In game or Refused while a join runs (WatchJoinAsync); null the rest of the time.
    private string? _joinState;

    /// <summary>Called once from the window's constructor.</summary>
    private void StartFrame()
    {
        VersionText.Text = ReleaseNotice.BeVersion;
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
        ("Guides", GuidesPage),
    };

    private Button[] NavButtons => new[] { NavPlay, NavMods, NavLogs, NavSettings, GuidesButton };

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

    // "Play" alone is the server time button of the Burning Empires Server Tool (Weiter, Reprendre), so the page name has a key of
    // its own and shows as Play in English.
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
    /// attention list, the Active Coop line and the header pill. Connect is green when it is the step (UpdateConnectButton),
    /// so it never gets the gold style too.
    /// </summary>
    private void ShowNextStep(bool connectReady)
    {
        var gameMissing = _gameRoot == null;
        // Read once per RefreshModules, not on every key typed in the Join box (UpdateConnectButton calls this too).
        if (_attention == null)
        {
            _attention = AttentionItems();
            ShowActiveCoop();
        }
        var items = _attention;
        // A missing or wrong Burning Empires core keeps Connect off, and Install or repair Burning Empires puts it in;
        // missing extra mods and helper mods leave Connect on (it offers to get them), so Install mods is the step only
        // while Connect is off for another reason.
        var core = items.Any(item => item.BlocksConnect);
        // The setups and the mods are read when the window opens (RefreshModules), so there is no Check mods step first.
        var step = ClientNextStep.For(gameMissing, modsChecked: true, connectReady, items.Count);
        NextStepText.Text = ClientNextStep.Title(step, items.Count);
        NextFindButton.Visibility = step == NextStep.FindBannerlord ? Visibility.Visible : Visibility.Collapsed;
        NextRepairButton.Visibility = core ? Visibility.Visible : Visibility.Collapsed;
        NextInstallButton.Visibility = !gameMissing && !core && items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        CheckModsButton.Visibility = gameMissing ? Visibility.Collapsed : Visibility.Visible;
        Primary(NextFindButton, step == NextStep.FindBannerlord);
        Primary(NextRepairButton, step == NextStep.InstallMods && core);
        Primary(NextInstallButton, step == NextStep.InstallMods && !core);
        ShowNeedsAttention(items);
        ShowClientPill(connectReady);
    }

    private void Primary(Button button, bool primary)
    {
        var style = (Style)FindResource(primary ? "Keep.BtnPrimary" : "Keep.BtnGhost");
        if (!ReferenceEquals(button.Style, style))
            button.Style = style;
    }

    /// <summary>One mod on the Needs attention list: its name and the status chip (the Mods page words). BlocksConnect is
    /// a core module that keeps Connect off (the CoreProblems rule), so Install or repair is the step.</summary>
    private sealed record AttentionItem(string Name, string Status, string StatusKind, string StatusTip, bool BlocksConnect = false);

    // The list ShowNextStep shows, read again after each RefreshModules (null until then).
    private List<AttentionItem>? _attention;

    private static AttentionItem Attention(string name, string word, string? tip = null, bool blocksConnect = false)
    {
        var chip = ModChips.Status(word);
        return new AttentionItem(name, chip.Text, chip.KindName, tip ?? chip.Tip, blocksConnect);
    }

    /// <summary>The Burning Empires core and helper modules that are missing or the wrong version (the same check as
    /// CoreProblems), then the extra mods of the setup Connect starts that are not on this PC (the same as ModuleProblems).</summary>
    private List<AttentionItem> AttentionItems()
    {
        var items = new List<AttentionItem>();
        if (_gameRoot == null)
            return items;
        foreach (var id in BeStack.ClientModules)
        {
            var xml = FindModuleXml(_gameRoot, id);
            var expected = Expected(id);
            // Connect gets a missing Workshop helper mod from Steam; MCM comes from Nexus Mods.
            var butr = BeStack.Butr.FirstOrDefault(mod => mod.ModuleId.Equals(id, StringComparison.OrdinalIgnoreCase));
            if (xml == null)
                items.Add(Attention(CoreName(id), butr != null && SharedModList.IsWorkshopId(butr.WorkshopId) ? ModChips.MissingOnWorkshop : ModChips.Missing, blocksConnect: butr == null));
            else if (expected != null && !ModuleIdentityReader.Read(xml).Version.Equals(expected, StringComparison.OrdinalIgnoreCase))
                items.Add(Attention(CoreName(id), ModChips.WrongVersion, blocksConnect: true));
        }

        foreach (var mod in BeSharedList.Missing(LaunchSetup, _installedExtras.Keys))
            items.Add(Attention(mod.Label, SharedModList.IsWorkshopId(mod.WorkshopId) ? ModChips.MissingOnWorkshop : ModChips.Missing));
        return items;
    }

    /// <summary>The first problem mods on Play, with Open Mods; hidden when every mod is ready.</summary>
    private void ShowNeedsAttention(IReadOnlyList<AttentionItem> items)
    {
        NeedsAttentionList.ItemsSource = items.Take(3).ToList();
        NeedsAttentionCard.Visibility = items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>The Made for card on Play: the game, Burning Empires and Coop this PC loads against what the set pins
    /// (BeStack). Coop has no pin: the Burning Empires tools follow the Workshop Coop. The newer game line (NewerGame) goes
    /// there and under Connect.</summary>
    private void ShowMadeFor()
    {
        string? Found(string id) => _gameRoot != null && FindModuleXml(_gameRoot, id) is { } xml ? ModuleIdentityReader.Read(xml).Version : null;
        var newerGame = NewerGame.Notice(_gameRoot);
        NewerGameText.Text = newerGame ?? "";
        NewerGameText.Visibility = newerGame == null ? Visibility.Collapsed : Visibility.Visible;
        MadeForCard.Show(new[]
        {
            new VersionPin("Bannerlord", PinIcon.Game, BeStack.GameVersion, Found("Native"), BaseGame: true),
            new VersionPin(BeStack.BeName, PinIcon.Mod, BeStack.BeVersion, Found(BeStack.BeModuleId)),
            new VersionPin("Coop", PinIcon.Coop, null, Found(BeStack.CoopModuleId)),
        }, new[] { newerGame ?? "", "War Sails and the Historical start are not supported." });
    }

    /// <summary>The Active Coop line on Play: the Coop the game loads and where it comes from.</summary>
    private void ShowActiveCoop()
    {
        ShowMadeFor();
        if (_gameRoot == null)
            return;
        var xml = FindModuleXml(_gameRoot, BeStack.CoopModuleId);
        ActiveCoopText.Text = xml == null
            ? CoopInstall.ActiveClientMissing
            : CoopInstall.ActiveClientLine(ModuleIdentityReader.Read(xml).Version, InGameModules(xml));
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
}
