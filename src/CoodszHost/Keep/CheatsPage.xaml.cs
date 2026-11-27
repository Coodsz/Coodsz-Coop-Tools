// The Cheats page started from CoopKeep's CheatsPage view and its view model
// (https://github.com/Aderimo/bannerlord-coopkeep, src/CoopKeep.App/Views/Pages/CheatsPage.axaml and
// ViewModels/MainViewModel.Cheats.cs), ported from Avalonia to WPF. Coodsz Host now sends Coop's own
// coop.debug commands from it: player cards, category chips, the cheat list and one form per cheat.

using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BannerlordCoopLauncher.Core.Keep;
using BannerlordCoopLauncher.Core.Localization;
using CoodszHost.Services;
using Microsoft.Win32;
using BannerlordCoopLauncher.Core.Steam;

namespace CoodszHost.Keep;

/// <summary>A line of the admin log list.</summary>
public sealed class LogRow
{
    public LogRow(AuditEntry entry, bool canUndo)
    {
        Entry = entry;
        CanUndo = canUndo;
    }

    public AuditEntry Entry { get; }
    public bool CanUndo { get; }
    public string When => Entry.DayText + "  " + Entry.TimeText;
    public string Text => Entry.Line;
    public string UndoText => Translator.T("Undo");
}

/// <summary>A dropdown choice: an id and the text shown.</summary>
public sealed record Choice(string Key, string Value)
{
    public override string ToString() => Value;
}

/// <summary>A troop line of the troop basket (give or take).</summary>
public sealed class TroopLine : Observable
{
    private int _count;

    public TroopLine(string id, string name, int have)
    {
        Id = id;
        Name = name;
        Have = have;
    }

    public string Id { get; }
    public string Name { get; }
    public int Have { get; }
    public int Count { get => _count; set { if (Set(ref _count, Math.Max(0, value))) OnChanged(nameof(CountText)); } }
    public string CountText => Count + "×";
}

/// <summary>A cheat in the list of the picked category, with its favourite star.</summary>
public sealed class CheatRow : Observable
{
    private bool _isPicked;
    private bool _isFavourite;

    public CheatRow(PlayerCheat cheat, bool favourite)
    {
        Cheat = cheat;
        _isFavourite = favourite;
    }

    public PlayerCheat Cheat { get; }
    public string Title => Translator.T(Cheat.Title);
    public string Help => Translator.T(Cheat.Help);

    public string Tags => string.Join(" · ", new[]
    {
        Cheat.NotAPlayer ? Translator.T("Not a player") : "",
        Cheat.Id == "skills.reset" ? Translator.T("Can't be undone") : "",
    }.Where(t => t.Length > 0));

    public bool HasTags => Tags.Length > 0;
    public bool IsPicked { get => _isPicked; set => Set(ref _isPicked, value); }

    public bool IsFavourite
    {
        get => _isFavourite;
        set
        {
            if (Set(ref _isFavourite, value))
                OnChanged(nameof(StarFill));
        }
    }

    public Brush StarFill => _isFavourite ? (Brush)Application.Current.FindResource("Keep.Warning") : Brushes.Transparent;
}

/// <summary>
/// The Cheats page: pick a player card, a category chip and a cheat, fill its form and press Run.
/// Every cheat sends a Coop coop.debug SERVER command (built by <see cref="PlayerCheats"/>), shows the
/// exact line before it goes, and is written to the admin log with its undo when Coop has one.
/// </summary>
public partial class CheatsPage : UserControl
{
    public Func<string?>? SavedGamePath { get; set; }
    public Action<string>? SaveGamePath { get; set; }

    public void UpdateGameFolderButton()
    {
        var game = PictureBuilder.FindGame();
        if (game == null)
            GameMissingPanel.Visibility = Visibility.Visible;
        else
            ApplyGameFound(game);
    }
    private readonly ObservableCollection<KeepTarget> _visible = new();
    private readonly ObservableCollection<CheatRow> _cheats = new();
    private readonly ObservableCollection<LogRow> _log = new();
    private readonly List<KeepTarget> _all = new();
    private readonly Dictionary<string, Func<string>> _values = new(StringComparer.Ordinal);
    private readonly Dictionary<CheatInputKind, IReadOnlyList<CoopListItem>> _lists = new();
    private IReadOnlyList<CoopPlayerRow> _coopPlayers = Array.Empty<CoopPlayerRow>();
    private readonly ObservableCollection<TroopCard> _troopCards = new();
    private readonly ObservableCollection<ArmyCard> _rosterCards = new();
    private readonly ObservableCollection<TroopLine> _troopBasket = new();
    private IReadOnlyList<TroopType> _troops = Array.Empty<TroopType>();
    private SkillSheet? _sheet;
    private KeepContext? _keep;
    private PlayerCheat? _cheat;
    private AuditEntry? _lastEntry;
    private string _category = nameof(CheatCategory.Gold);
    private bool _loadingPlayers;
    private bool _busy;
    private string _liveKey = "";
    private string? _gameRoot;
    private System.Diagnostics.Process? _pictureGame;

    public CheatsPage()
    {
        InitializeComponent();
        PlayerList.ItemsSource = _visible;
        CheatList.ItemsSource = _cheats;
        LogList.ItemsSource = _log;
        TroopCards.ItemsSource = _troopCards;
        RosterCards.ItemsSource = _rosterCards;
        TroopBasket.ItemsSource = _troopBasket;
        _troopBasket.CollectionChanged += (_, _) => ShowTroopBasket();
        Hints();
        Translator.Changed += () => Dispatcher.BeginInvoke(() =>
        {
            Hints();
            _sheet?.Rename();
            ShowCheats();
            ShowForm();
        });
    }

    /// <summary>Called once by MainWindow when the window opens.</summary>
    public void Attach(KeepContext keep)
    {
        _keep = keep;
        keep.Status += (message, ok) => Dispatcher.BeginInvoke(() => Say(message, ok));
        keep.Log.Changed += () => Dispatcher.BeginInvoke(ShowLog);
        _sheet = new SkillSheet((hero, command, audit, undo) => RunSheetAsync(hero, command, audit, undo));
        CheatSheetView.DataContext = _sheet;
        if (!keep.ToolsModule)
        {
            // The AD1259 build sends coop.debug only (CoodszServerTools.CheatsUseTools): the character sheet, the
            // profile and the pictures all read the server through it, so they are left out.
            PicturePanel.Visibility = Visibility.Collapsed;
            SheetButton.Visibility = Visibility.Collapsed;
            ProfileButton.Visibility = Visibility.Collapsed;
            SheetCard.Visibility = Visibility.Collapsed;
        }

        ShowLog();
        ShowCheats();
        ShowPictures();
    }

    /// <summary>The server has Coodsz Server Tools, so the coodsz.* cheats can be shown.</summary>
    private bool Tools => _keep?.ToolsModule != false;

    /// <summary>A server start: the ids and lists of the last run are read again.</summary>
    public void ServerStarting()
    {
        _keep?.Client.Reset();
        _lists.Clear();
        _coopPlayers = Array.Empty<CoopPlayerRow>();
        _troops = Array.Empty<TroopType>();
        _troopCards.Clear();
    }

    /// <summary>The page was opened.</summary>
    public async void Shown()
    {
        ShowServerLine();
        ShowPictures();
        await LoadPlayersAsync();
    }

    /// <summary>The host's live player list changed; reads the players again when names came or went.</summary>
    public async void LivePlayersChanged(IReadOnlyList<string> names)
    {
        var key = string.Join("|", names.OrderBy(n => n, StringComparer.Ordinal));
        if (key == _liveKey || !IsVisible)
            return;
        _liveKey = key;
        await LoadPlayersAsync();
    }

    private void ShowServerLine()
    {
        if (_keep == null)
            return;
        var blocked = _keep.BlockReason();
        ServerLine.Text = blocked.Length > 0 ? blocked : Translator.T("Cheats work while the server is running.");
    }

    private void Say(string message, bool ok)
    {
        StatusLine.Text = message;
        StatusLine.Foreground = (Brush)FindResource(ok ? "Keep.Success" : "Keep.Warning");
        StatusLine.Visibility = message.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    // --- players -----------------------------------------------------------

    private KeepTarget? Picked => _all.FirstOrDefault(t => t.IsTarget);

    /// <summary>The player picked now when their hero is known (the Encyclopedia's Give with the Items window uses it).</summary>
    public IReadOnlyList<KeepTarget> PickedTargets => _all.Where(t => t.IsTarget && t.HasHero).ToList();

    private async Task LoadPlayersAsync()
    {
        if (_keep == null || _loadingPlayers)
            return;
        _loadingPlayers = true;
        try
        {
            var players = (await _keep.LoadPlayersAsync()).ToList();
            if (_keep.CanSend)
            {
                var (_, lines) = await _keep.SendCoopAsync(CoopReplies.PlayersCommand);
                var rows = CoopReplies.Players(lines);
                if (rows.Count > 0)
                    _coopPlayers = rows;
            }

            // Players Coop knows that the host did not list yet (for example before the tools module answered).
            foreach (var row in _coopPlayers)
            {
                if (players.Any(p => Same(p, row)))
                    continue;
                players.Add(new KeepPlayer(row.Name, row.HeroId, SteamId(row.ControllerId), false, null));
            }

            ShowServerLine();
            var picked = Picked is { } p0 ? Key(p0.Player) : null;
            var old = _all.ToDictionary(t => Key(t.Player), StringComparer.Ordinal);
            _all.Clear();
            foreach (var player in players)
            {
                var key = Key(player);
                if (old.TryGetValue(key, out var row))
                    row.Update(player);
                else
                    row = new KeepTarget(player);
                row.IsTarget = key == picked;
                _all.Add(row);
            }

            if (Picked == null && _all.Count > 0)
                (_all.FirstOrDefault(t => t.Online) ?? _all[0]).IsTarget = true;
            _visible.Clear();
            foreach (var row in _all)
                _visible.Add(row);
            NobodyText.Visibility = _all.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            await ReadProfilesAsync();
            FocusChanged();
        }
        finally
        {
            _loadingPlayers = false;
        }
    }

    private static string Key(KeepPlayer player) => player.HeroId.Length > 0 ? player.HeroId : "name:" + player.Name;

    private static bool Same(KeepPlayer player, CoopPlayerRow row) =>
        row.HeroId.Length > 0 && player.HeroId == row.HeroId
        || string.Equals(player.Name, row.Name, StringComparison.OrdinalIgnoreCase);

    private static string SteamId(string controllerId) => controllerId.Length == 17 && controllerId.All(char.IsAsciiDigit) ? controllerId : "";

    /// <summary>Gold and age for the cards, read by the server tools module when it is running.</summary>
    private async Task ReadProfilesAsync()
    {
        if (_keep is not { CanSend: true, ToolsModule: true } keep)
            return;
        if (keep.Client.ToolsReady != true && !await keep.Client.CheckToolsAsync())
            return;
        foreach (var row in _all.Where(r => r.HasHero).Take(16))
            if (await keep.Client.ProfileAsync(row.HeroId) is { } profile)
                row.Profile = profile;
    }

    private async void RefreshPlayers_Click(object sender, RoutedEventArgs e)
    {
        _lists.Clear();
        await LoadPlayersAsync();
        ShowForm();
    }

    private void Player_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not KeepTarget row)
            return;
        foreach (var r in _all)
            r.IsTarget = ReferenceEquals(r, row);
        FocusChanged();
    }

    /// <summary>Selects this hero (from the player profile, the Players page or the Encyclopedia).</summary>
    public async void Pick(KeepTarget target)
    {
        await LoadPlayersAsync();
        var row = _all.FirstOrDefault(r => target.HeroId.Length > 0 ? r.HeroId == target.HeroId : r.Name == target.Name);
        if (row == null)
            return;
        foreach (var r in _all)
            r.IsTarget = ReferenceEquals(r, row);
        FocusChanged();
    }

    /// <summary>Streamer mode was switched: the cards and the form show the new names.</summary>
    public void StreamerChanged()
    {
        foreach (var row in _all)
            row.StreamerChanged();
        ShowForm();
    }

    private async void FocusChanged()
    {
        ShowCheats();
        ShowForm();
        var focus = Picked;
        SheetTitle.Text = Translator.T("Character sheet") + " · " + (focus?.Shown ?? Translator.T("nobody yet"));
        if (_sheet != null && focus is { HasHero: true } && !ReferenceEquals(_sheet.Hero, focus))
        {
            _sheet.Hero = focus;
            if (SheetCard.Visibility == Visibility.Visible)
                await _sheet.LoadAsync();
        }

        if (_cheat?.Id == "party.army")
        {
            _troopBasket.Clear();
            await ShowArmyAsync();
        }
    }

    private void Hints()
    {
        NoteBox.Tag = Translator.T("Note (optional), e.g. troops lost to a cheater");
        TroopSearch.Tag = Translator.T("Search for a troop...");
    }

    private string NoteText => NoteBox.Text.Trim();

    /// <summary>The Character sheet button: shows the sheet of the picked player under the form, or hides it.</summary>
    private async void SheetButton_Click(object sender, RoutedEventArgs e)
    {
        if (!Tools)
            return;
        var show = SheetCard.Visibility != Visibility.Visible;
        SheetCard.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (!show || _sheet == null)
            return;
        if (Picked is not { HasHero: true } focus)
        {
            Say(Translator.T("Pick a player above first."), false);
            return;
        }

        _sheet.Hero = focus;
        SheetCard.BringIntoView();
        await _sheet.LoadAsync();
    }

    private void OpenProfile_Click(object sender, RoutedEventArgs e)
    {
        if (_keep != null && Picked is { } focus)
            new PlayerProfileWindow(_keep, focus).Show();
        else
            Say(Translator.T("Pick a player above first."), false);
    }

    private async Task<bool> RunSheetAsync(KeepTarget hero, string command, string audit, Func<ToolsChange, string?>? undo)
    {
        if (_keep == null)
            return false;
        var result = await _keep.ChangeAsync(hero, command, audit, NoteText, undo);
        return result.Ok;
    }

    /// <summary>The picked player with the ids from coop.debug.players.list, or from the tools module.</summary>
    private CheatTarget? Target()
    {
        if (Picked is not { } t)
            return null;
        var coop = _coopPlayers.FirstOrDefault(r => r.HeroId.Length > 0 && r.HeroId == t.HeroId)
                   ?? _coopPlayers.FirstOrDefault(r => string.Equals(r.Name, t.Name, StringComparison.OrdinalIgnoreCase));
        static string First(params string?[] values) => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? "";
        return new CheatTarget(
            First(coop?.Name, t.Name),
            First(coop?.HeroId, t.HeroId),
            First(coop?.ClanId, t.Profile?.ClanId, t.Player.Hero?.ClanId),
            First(coop?.PartyId, t.Profile?.PartyId, t.Player.Hero?.PartyId),
            First(coop?.ControllerId, t.SteamId),
            t.Gold,
            t.Profile?.Age,
            t.Online,
            t.HitPoints,
            t.MaxHitPoints,
            t.Profile?.ClanTier);
    }

    // --- categories and the cheat list -------------------------------------

    private void Category_Checked(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is string tag)
            _category = tag;
        if (CheatList == null)
            return;
        ShowCheats();
        ShowForm();
    }

    private HashSet<string> Favourites() =>
        new(_keep?.ReadFavourites?.Invoke() ?? Array.Empty<string>(), StringComparer.Ordinal);

    private void ShowCheats()
    {
        if (CheatList == null)
            return;
        var target = Target();
        var favourites = Favourites();
        IEnumerable<PlayerCheat> list = _category == "Favourites"
            ? PlayerCheats.Favourites(favourites, target, Tools)
            : Enum.TryParse<CheatCategory>(_category, out var category) ? PlayerCheats.In(category, target, Tools) : Array.Empty<PlayerCheat>();
        _cheats.Clear();
        foreach (var cheat in list)
            _cheats.Add(new CheatRow(cheat, favourites.Contains(cheat.Id)) { IsPicked = cheat.Id == _cheat?.Id });
        NoFavourites.Visibility = _category == "Favourites" && _cheats.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (_cheat != null && _cheats.All(r => r.Cheat.Id != _cheat.Id))
        {
            _cheat = null;
            BuildFields();
        }
    }

    private void Cheat_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not CheatRow row)
            return;
        _cheat = row.Cheat;
        foreach (var r in _cheats)
            r.IsPicked = ReferenceEquals(r, row);
        _lastEntry = null;
        ResultBar.Visibility = Visibility.Collapsed;
        BuildFields();
    }

    private void Star_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not CheatRow row || _keep == null)
            return;
        var favourites = Favourites();
        if (!favourites.Remove(row.Cheat.Id))
            favourites.Add(row.Cheat.Id);
        row.IsFavourite = favourites.Contains(row.Cheat.Id);
        _keep.SaveFavourites?.Invoke(PlayerCheats.All.Select(c => c.Id).Where(favourites.Contains).ToList());
        if (_category == "Favourites")
            ShowCheats();
    }

    // --- the form ------------------------------------------------------------

    /// <summary>Makes the boxes of the picked cheat: numbers with preset chips, dropdowns and tick boxes.</summary>
    private void BuildFields()
    {
        FormFields.Children.Clear();
        _values.Clear();
        var army = _cheat?.Id == "party.army";
        ArmyPanel.Visibility = army ? Visibility.Visible : Visibility.Collapsed;
        RunButton.Content = Translator.T(_cheat?.Id == "party.items" ? "Open items" : "Run");
        if (_cheat == null)
        {
            ShowForm();
            return;
        }

        if (army)
        {
            _troopBasket.Clear();
            _ = ShowArmyAsync();
        }

        foreach (var input in _cheat.Inputs)
        {
            switch (input.Kind)
            {
                case CheatInputKind.Number:
                    AddNumber(input);
                    break;
                case CheatInputKind.Toggle:
                    AddToggle(input);
                    break;
                default:
                    AddList(input);
                    break;
            }
        }

        ShowForm();
    }

    private TextBlock Label(string text) =>
        new() { Text = Translator.T(text), Style = (Style)FindResource("Keep.Label"), Margin = new Thickness(0, 0, 0, 4) };

    private void AddNumber(CheatInput input)
    {
        FormFields.Children.Add(Label(input.Label));
        var row = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
        var box = new TextBox
        {
            Text = input.Default.ToString(CultureInfo.InvariantCulture),
            Style = (Style)FindResource("Keep.Text"),
            Width = 150,
            Margin = new Thickness(0, 0, 8, 0),
            ToolTip = Translator.T("From") + " " + input.Min.ToString("N0", CultureInfo.CurrentCulture) + " " + Translator.T("to") + " " + input.Max.ToString("N0", CultureInfo.CurrentCulture),
        };
        box.TextChanged += (_, _) => ShowPreview();
        row.Children.Add(box);
        foreach (var preset in input.Presets ?? Array.Empty<int>())
        {
            var value = preset;
            var chip = new Button
            {
                Content = value.ToString("N0", CultureInfo.CurrentCulture),
                Style = (Style)FindResource("Keep.Small"),
                Margin = new Thickness(0, 0, 6, 0),
                ToolTip = Translator.T("Puts this number in the box."),
            };
            chip.Click += (_, _) => box.Text = value.ToString(CultureInfo.InvariantCulture);
            row.Children.Add(chip);
        }

        FormFields.Children.Add(row);
        _values[input.Key] = () => box.Text;
    }

    private void AddToggle(CheatInput input)
    {
        var check = new CheckBox
        {
            Content = Translator.T(input.Label),
            IsChecked = input.Default != 0,
            Style = (Style)FindResource("Keep.Check"),
            Margin = new Thickness(0, 0, 0, 12),
            ToolTip = Translator.T("Ticked sends true, not ticked sends false."),
        };
        check.Click += (_, _) => ShowPreview();
        FormFields.Children.Add(check);
        _values[input.Key] = () => check.IsChecked == true ? "1" : "0";
    }

    private void AddList(CheatInput input)
    {
        FormFields.Children.Add(Label(input.Label));
        var combo = new ComboBox
        {
            Style = (Style)FindResource("Keep.Combo"),
            MinWidth = 260,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 0, 4),
            ToolTip = Translator.T("Pick one. The server's own list fills it."),
        };
        combo.SelectionChanged += (_, _) => ShowPreview();
        var note = new TextBlock { Style = (Style)FindResource("Keep.Dim"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) };
        var longList = input.Kind is CheatInputKind.Hero or CheatInputKind.Town or CheatInputKind.Village;
        TextBox? search = null;
        if (longList)
        {
            search = new TextBox { Style = (Style)FindResource("Keep.Text"), Width = 260, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 6), Tag = Translator.T("Search...") };
            FormFields.Children.Add(search);
        }

        FormFields.Children.Add(combo);
        FormFields.Children.Add(note);
        _values[input.Key] = () => (combo.SelectedItem as Choice)?.Key ?? "";

        IReadOnlyList<Choice> all = Array.Empty<Choice>();
        void Fill()
        {
            var q = search?.Text.Trim() ?? "";
            var selected = combo.SelectedItem as Choice;
            var shown = all.Where(c => q.Length == 0 || c.Value.Contains(q, StringComparison.CurrentCultureIgnoreCase) || c.Key.Contains(q, StringComparison.OrdinalIgnoreCase)).Take(400).ToList();
            combo.ItemsSource = shown;
            combo.SelectedItem = selected != null && shown.Contains(selected) ? selected : shown.Count == 1 || !longList ? shown.FirstOrDefault() : null;
        }

        if (search != null)
            search.TextChanged += (_, _) => Fill();

        if (input.Kind == CheatInputKind.Skill)
        {
            all = PlayerCheats.Skills.Select(s => new Choice(s.Id, Translator.T(s.Name))).ToList();
            Fill();
            return;
        }

        _ = LoadListAsync(input.Kind, note, items =>
        {
            all = items.Select(i => new Choice(i.Id, i.Name.Length > 0 ? i.Name + "  (" + i.Id + ")" : i.Id)).ToList();
            Fill();
        });
    }

    private async Task LoadListAsync(CheatInputKind kind, TextBlock note, Action<IReadOnlyList<CoopListItem>> done)
    {
        if (_lists.TryGetValue(kind, out var cached))
        {
            done(cached);
            return;
        }

        if (_keep is not { CanSend: true } keep)
        {
            note.Text = Translator.T("Start the server to fill this list.");
            return;
        }

        var (command, read) = kind switch
        {
            CheatInputKind.Kingdom => (CoopReplies.KingdomsCommand, (Func<IEnumerable<string>, IReadOnlyList<CoopListItem>>)CoopReplies.Kingdoms),
            CheatInputKind.Town => (CoopReplies.TownsCommand, CoopReplies.IdThenName),
            CheatInputKind.Village => (CoopReplies.VillagesCommand, CoopReplies.IdThenName),
            _ => (CoopReplies.HeroesCommand, CoopReplies.Heroes),
        };
        note.Text = Translator.T("Reading the list from the server...");
        var (_, lines) = await keep.SendCoopAsync(command);
        var items = read(lines);
        if (kind == CheatInputKind.Hero && Target() is { } target)
            items = items.Where(i => i.Id != target.HeroId).ToList();
        if (items.Count > 0)
            _lists[kind] = items;
        note.Text = items.Count == 0
            ? Translator.T("The server sent no list. Press refresh above and try again.")
            : "";
        done(items);
    }

    private Dictionary<string, string> Values() => _values.ToDictionary(v => v.Key, v => v.Value(), StringComparer.Ordinal);

    /// <summary>The title, help, offline note and the command line of the picked cheat.</summary>
    private void ShowForm()
    {
        if (FormPanel == null)
            return;
        PickCheat.Visibility = _cheat == null ? Visibility.Visible : Visibility.Collapsed;
        FormPanel.Visibility = _cheat == null ? Visibility.Collapsed : Visibility.Visible;
        if (_cheat == null)
            return;
        var target = Target();
        FormTitle.Text = _cheat.NotAPlayer
            ? Translator.T(_cheat.Title)
            : Translator.T(_cheat.Title) + " " + Translator.T("for") + " " + (Picked?.Shown ?? Translator.T("nobody yet"));
        FormHelp.Text = Translator.T(_cheat.Help);
        var notes = new List<string>();
        if (!_cheat.NotAPlayer && target == null)
            notes.Add(Translator.T("Pick a player above first."));
        if (PlayerCheats.OfflineNote(_cheat, target) is { } offline)
            notes.Add(Translator.T(offline));
        if (_cheat.Id == "skills.reset")
            notes.Add(Translator.T("This can't be undone. The hero's skills, attributes and focus all go back to the start."));
        if (_cheat.Tools && _keep?.Client.ToolsReady == false)
            notes.Add(Translator.T(KeepClient.NoTools));
        FormNoteText.Text = string.Join(" ", notes);
        FormNote.Visibility = notes.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        ShowPreview();
    }

    private void ShowPreview()
    {
        if (_cheat == null)
            return;
        var target = Target();
        if (_cheat.Id == "party.army")
        {
            var lines = ArmyCommands(target);
            CommandPreview.Text = lines.Count == 0 ? Translator.T("Click troops to add them, then press Run.") : string.Join(Environment.NewLine, lines);
            CommandPreview.Foreground = (Brush)FindResource(lines.Count == 0 ? "Keep.TextDim" : "Keep.TextMain");
            return;
        }

        try
        {
            CommandPreview.Text = Masked(PlayerCheats.Build(_cheat, _cheat.NotAPlayer ? null : target, Values()).Command, target);
            CommandPreview.Foreground = (Brush)FindResource("Keep.TextMain");
        }
        catch (ArgumentException ex)
        {
            CommandPreview.Text = Translator.T(ex.Message);
            CommandPreview.Foreground = (Brush)FindResource("Keep.TextDim");
        }
    }

    /// <summary>Streamer mode: the line on screen has the alias and no Steam id.</summary>
    private string Masked(string command, CheatTarget? target)
    {
        if (!KeepContext.StreamerMode || target == null || Picked is not { } picked)
            return command;
        var shown = command;
        if (target.ControllerId.Length > 0)
            shown = shown.Replace(target.ControllerId, "•••••");
        if (target.Name.Length > 0)
            shown = shown.Replace(CoopArg.Quote(target.Name), CoopArg.Quote(picked.Shown));
        return shown;
    }

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        _troopBasket.Clear();
        BuildFields();
        ResultBar.Visibility = Visibility.Collapsed;
    }

    private void ShowResult(string text, bool ok, bool canUndo = false)
    {
        ResultLine.Text = text;
        ResultLine.Foreground = (Brush)FindResource(ok ? "Keep.Success" : "Keep.Warning");
        ResultBar.Visibility = Visibility.Visible;
        UndoButton.Visibility = canUndo ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void Run_Click(object sender, RoutedEventArgs e)
    {
        if (_keep == null || _cheat == null || _busy)
            return;
        var blocked = _keep.BlockReason();
        if (blocked.Length > 0)
        {
            ShowResult(blocked, false);
            return;
        }

        var cheat = _cheat;
        var target = cheat.NotAPlayer ? null : Target();
        if (cheat.Tools)
        {
            await RunToolsAsync(cheat, target);
            return;
        }

        CheatBuild build;
        try
        {
            build = PlayerCheats.Build(cheat, target, Values());
        }
        catch (ArgumentException ex)
        {
            ShowResult(Translator.T(ex.Message), false);
            return;
        }

        var who = target == null ? Translator.T("World") : Picked?.Shown ?? target.Name;
        if (cheat.Id == "skills.reset" && Window.GetWindow(this) is { } owner
            && !ConfirmWindow.Ask(owner, Translator.T("Reset skills"), Translator.T("Reset the skills of") + " " + who + "? " + Translator.T("This can't be undone."), null, Translator.T("Reset")))
            return;

        _busy = true;
        RunButton.IsEnabled = false;
        try
        {
            _keep.Remember?.Invoke("[Cheats] > " + Masked(build.Command, target));
            var (ok, lines) = await _keep.SendCoopAsync(build.Command);
            var reply = lines.Count == 0 ? Translator.T("The server printed nothing after that command.") : string.Join(" ", lines.Take(4));
            _keep.Remember?.Invoke(reply);
            if (!ok)
            {
                ShowResult(reply, false);
                return;
            }

            var undo = build.Undo;
            var title = Translator.T(cheat.Title);
            var line = Masked(build.Command, target);
            _lastEntry = _keep.Log.Add(who, build.Summary + " · " + line, NoteText, undo == null ? null : async () =>
            {
                var (back, backLines) = await _keep.SendCoopAsync(undo);
                if (back)
                    _keep.Log.Add(who, Translator.T("undo") + " " + title + " · " + Masked(undo, target));
                var said = backLines.Count == 0 ? "" : " " + string.Join(" ", backLines.Take(2));
                _keep.Say(back ? who + ": " + Translator.T("undone") + said : who + ":" + said, back);
            });
            ShowResult(Translator.T("Done.") + " " + reply + (build.Note == null ? "" : " " + Translator.T(build.Note)), true, undo != null);
            await RefreshTargetAsync();
        }
        finally
        {
            _busy = false;
            RunButton.IsEnabled = true;
        }
    }

    /// <summary>Reads the picked player's gold and age again after a change.</summary>
    private async Task RefreshTargetAsync()
    {
        if (_keep is not { CanSend: true } keep || Picked is not { HasHero: true } picked || keep.Client.ToolsReady != true)
            return;
        if (await keep.Client.ProfileAsync(picked.HeroId) is { } profile)
            picked.Profile = profile;
        ShowCheats();
        ShowPreview();
    }

    private async void UndoLast_Click(object sender, RoutedEventArgs e)
    {
        if (_keep == null || _lastEntry == null)
            return;
        var blocked = _keep.BlockReason();
        if (blocked.Length > 0)
        {
            ShowResult(blocked, false);
            return;
        }

        var entry = _lastEntry;
        _lastEntry = null;
        UndoButton.Visibility = Visibility.Collapsed;
        await _keep.Log.UndoAsync(entry);
        ShowResult(StatusLine.Text, true);
        await RefreshTargetAsync();
    }

    // --- Coodsz Server Tools cheats (health, clan tier, take renown, troops, items) ---

    /// <summary>
    /// Runs a coodsz.* cheat like the earlier Cheats page: the module confirms the value it read back,
    /// the change goes into the admin log with the note, and the undo comes from the old value.
    /// </summary>
    private async Task RunToolsAsync(PlayerCheat cheat, CheatTarget? target)
    {
        if (_keep == null || Picked is not { HasHero: true } hero)
        {
            ShowResult(Translator.T("Pick a player above first."), false);
            return;
        }

        if (cheat.Id == "party.items")
        {
            new ItemsWindow(_keep, new[] { hero }).Show();
            return;
        }

        _busy = true;
        RunButton.IsEnabled = false;
        try
        {
            if (_keep.Client.ToolsReady != true && !await _keep.Client.CheckToolsAsync())
            {
                ShowResult(KeepClient.NoTools, false);
                return;
            }

            if (cheat.Id == "party.army")
            {
                await ApplyTroopsAsync(hero);
                return;
            }

            CheatBuild build;
            try
            {
                build = PlayerCheats.Build(cheat, target, Values());
            }
            catch (ArgumentException ex)
            {
                ShowResult(Translator.T(ex.Message), false);
                return;
            }

            var id = cheat.Id;
            var before = _keep.Log.Entries.FirstOrDefault();
            var result = await _keep.ChangeAsync(hero, build.Command, build.Summary, NoteText, change => PlayerCheats.ToolsUndo(id, change));
            var entry = _keep.Log.Entries.FirstOrDefault();
            _lastEntry = result.Ok && entry != null && !ReferenceEquals(entry, before) && _keep.Log.CanUndo(entry) ? entry : null;
            ShowResult(result.Ok ? Translator.T("Done.") + " " + StatusLine.Text : result.Message, result.Ok, _lastEntry != null);
            await RefreshTargetAsync();
        }
        finally
        {
            _busy = false;
            RunButton.IsEnabled = true;
        }
    }

    private bool TakingTroops => TakeTroops.IsChecked == true;

    /// <summary>The coodsz.troops lines the troop basket sends.</summary>
    private IReadOnlyList<string> ArmyCommands(CheatTarget? target)
    {
        if (target == null || target.HeroId.Length == 0)
            return Array.Empty<string>();
        var taking = TakingTroops;
        return _troopBasket.Where(l => l.Count > 0)
            .Select(l => ToolsCommand.Troops(target.HeroId, l.Id, taking ? -l.Count : l.Count))
            .ToList();
    }

    private async void ArmyMode_Checked(object sender, RoutedEventArgs e)
    {
        if (TroopBasket == null)
            return;
        _troopBasket.Clear();
        await ShowArmyAsync();
    }

    private void ArmyNoteSay(string text)
    {
        ArmyNoteText.Text = text;
        ArmyNote.Visibility = text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private async Task ShowArmyAsync()
    {
        if (_keep == null)
            return;
        var taking = TakingTroops;
        TroopCards.Visibility = taking ? Visibility.Collapsed : Visibility.Visible;
        RosterCards.Visibility = taking ? Visibility.Visible : Visibility.Collapsed;
        CultureBox.IsEnabled = ClassBox.IsEnabled = TroopSearch.IsEnabled = !taking;
        ShowTroopBasket();

        if (!_keep.CanSend)
        {
            ArmyNoteSay(_keep.BlockReason());
            return;
        }

        if (_keep.Client.ToolsReady != true && !await _keep.Client.CheckToolsAsync())
        {
            ArmyNoteSay(KeepClient.NoTools);
            return;
        }

        if (taking)
        {
            if (Picked is not { HasHero: true } hero)
            {
                ArmyNoteSay(Translator.T("Pick a player above first."));
                _rosterCards.Clear();
                return;
            }

            await RefreshRosterAsync(hero);
            return;
        }

        if (_troops.Count == 0)
        {
            ArmyNoteSay(Translator.T("Reading the troop list from the server..."));
            _troops = await _keep.Client.TroopTypesAsync();
            FillTroopFilters();
        }

        ArmyNoteSay(_troops.Count == 0
            ? Translator.T("The server did not send its troop list. Try again in a moment.")
            : PictureBuilder.Store.HasPictures ? "" : Translator.T("No pictures yet: the cards show names. Build pictures above draws them with your own game."));
        FilterTroops();
    }

    private async Task RefreshRosterAsync(KeepTarget target)
    {
        var roster = await _keep!.Client.RosterAsync(target.HeroId);
        _rosterCards.Clear();
        if (roster == null)
        {
            ArmyNoteSay(target.Name + " " + Translator.T("has no party right now, so troops cannot be changed."));
            return;
        }

        foreach (var troop in roster.Members.Where(m => !m.IsHero).OrderByDescending(m => m.Tier).ThenByDescending(m => m.Number))
            _rosterCards.Add(new ArmyCard(troop));
        ArmyNoteSay(TroopCatalog.ArmyLine(roster.Troops, roster.Wounded));
    }

    private void FillTroopFilters()
    {
        var cultures = new List<Choice> { new("", Translator.T("All cultures")) };
        cultures.AddRange(TroopCatalog.Cultures(_troops).Select(c => new Choice(c.Id, c.Name)));
        CultureBox.ItemsSource = cultures;
        CultureBox.SelectedIndex = 0;
        ClassBox.ItemsSource = new List<Choice>
        {
            new("", Translator.T("All classes")),
            new(nameof(TroopClass.Infantry), Translator.T(TroopCatalog.ClassName(TroopClass.Infantry))),
            new(nameof(TroopClass.Archer), Translator.T(TroopCatalog.ClassName(TroopClass.Archer))),
            new(nameof(TroopClass.Cavalry), Translator.T(TroopCatalog.ClassName(TroopClass.Cavalry))),
            new(nameof(TroopClass.HorseArcher), Translator.T(TroopCatalog.ClassName(TroopClass.HorseArcher))),
        };
        ClassBox.SelectedIndex = 0;
    }

    private void FilterTroops()
    {
        var culture = (CultureBox.SelectedItem as Choice)?.Key;
        var classKey = (ClassBox.SelectedItem as Choice)?.Key;
        TroopClass? troopClass = Enum.TryParse<TroopClass>(classKey, out var c) ? c : null;
        _troopCards.Clear();
        foreach (var troop in TroopCatalog.Filter(_troops, culture, troopClass, TroopSearch.Text))
            _troopCards.Add(new TroopCard(troop));
    }

    private void TroopFilter_Changed(object sender, SelectionChangedEventArgs e) => FilterTroops();

    private void TroopSearch_Changed(object sender, TextChangedEventArgs e) => FilterTroops();

    private int TroopsPerClick()
    {
        var n = SkillSheet.AmountOf(TroopPerClick.Text);
        return Math.Clamp(n <= 0 ? 1 : n, 1, ToolsCommand.MaxTroops);
    }

    private void TroopPerClick_Click(object sender, RoutedEventArgs e)
    {
        var up = (sender as FrameworkElement)?.Tag as string != "-1";
        var now = TroopsPerClick();
        var next = up ? (now < 10 ? now + 1 : now + 10) : (now <= 10 ? now - 1 : now - 10);
        TroopPerClick.Text = Math.Clamp(next, 1, ToolsCommand.MaxTroops).ToString(CultureInfo.InvariantCulture);
    }

    private void TroopCard_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not TroopCard card)
            return;
        var line = _troopBasket.FirstOrDefault(l => l.Id == card.Troop.Id);
        if (line == null)
            _troopBasket.Add(line = new TroopLine(card.Troop.Id, card.Name, 0));
        line.Count = Math.Min(line.Count + TroopsPerClick(), ToolsCommand.MaxTroops);
        ShowTroopBasket();
    }

    private void RosterCard_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not ArmyCard card)
            return;
        var line = _troopBasket.FirstOrDefault(l => l.Id == card.Troop.Id);
        if (line == null)
            _troopBasket.Add(line = new TroopLine(card.Troop.Id, card.Name, card.Troop.Number));
        line.Count = Math.Min(line.Count + TroopsPerClick(), line.Have);
        ShowTroopBasket();
    }

    private void RemoveTroopLine_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is TroopLine line)
            _troopBasket.Remove(line);
    }

    private void ShowTroopBasket()
    {
        if (TroopBasketHint == null)
            return;
        TroopBasketHint.Visibility = _troopBasket.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (_cheat?.Id == "party.army")
        {
            var total = _troopBasket.Sum(l => l.Count);
            RunButton.Content = TakingTroops ? Translator.T("Take troops") + " (" + total + ")" : Translator.T("Give troops") + " (" + total + ")";
            ShowPreview();
        }
    }

    /// <summary>Gives or takes every troop kind in the basket; the server confirms each kind, each can be undone.</summary>
    private async Task ApplyTroopsAsync(KeepTarget hero)
    {
        var lines = _troopBasket.Where(l => l.Count > 0).ToList();
        if (lines.Count == 0)
        {
            ShowResult(Translator.T("Click troops to add them, then press Run."), false);
            return;
        }

        var taking = TakingTroops;
        var moved = 0;
        foreach (var line in lines)
        {
            var troopId = line.Id;
            var result = await _keep!.ChangeAsync(hero, ToolsCommand.Troops(hero.HeroId, troopId, taking ? -line.Count : line.Count),
                (taking ? "took " : "gave ") + line.Count + "× " + line.Name, NoteText,
                c => c.Changed ? ToolsCommand.Troops(c.HeroId, troopId, c.Old - c.New) : null, quiet: true);
            if (result.Ok && result.Change is { } change)
                moved += Math.Abs(change.New - change.Old);
        }

        ShowResult(moved > 0
            ? (taking ? Translator.T("Took") : Translator.T("Gave")) + " " + moved + " " + Translator.T("troops. The server confirmed each kind. Undo is in the admin log.")
            : StatusLine.Text, moved > 0);
        _troopBasket.Clear();
        if (taking)
            await RefreshRosterAsync(hero);
    }

    // --- admin log ----------------------------------------------
    // Both run on Checked and Unchecked, not Click: a toggle by keyboard or by a screen reader
    // turned the arrow without a Click, and the section stayed empty.

    private void LogToggle_Click(object sender, RoutedEventArgs e) =>
        LogPanel.Visibility = LogToggle.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;

    private void ShowLog()
    {
        if (_keep == null)
            return;
        _log.Clear();
        foreach (var entry in _keep.Log.Entries.Take(80))
            _log.Add(new LogRow(entry, _keep.Log.CanUndo(entry)));
        LogEmpty.Visibility = _log.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        LogCount.Text = _keep.Log.Entries.Count == 0 ? "" : "(" + _keep.Log.Entries.Count + ")";
    }

    private async void Undo_Click(object sender, RoutedEventArgs e)
    {
        if (_keep == null || (sender as FrameworkElement)?.DataContext is not LogRow row)
            return;
        var blocked = _keep.BlockReason();
        if (blocked.Length > 0)
        {
            Say(blocked, false);
            return;
        }

        await _keep.Log.UndoAsync(row.Entry);
        await RefreshTargetAsync();
    }

    // --- pictures for the Items window, the profile and the Encyclopedia -------

    private void ShowPictures()
    {
        var running = _pictureGame is { HasExited: false };
        PictureLine.Text = running ? Translator.T("Drawing pictures in your game...") : PictureBuilder.Store.Summary();
        BuildPicturesButton.IsEnabled = !running;
    }

    private async void BuildPictures_Click(object sender, RoutedEventArgs e)
    {
        var game = _gameRoot ?? PictureBuilder.FindGame();
        if (game == null)
            GameMissingPanel.Visibility = Visibility.Visible;
        else
            ApplyGameFound(game);
        if (PictureBuilder.Problem(game) is { } problem)
        {
            Say(problem, false);
            return;
        }

        var redraw = PictureBuilder.Store.HasPictures
                     && MessageBox.Show(Window.GetWindow(this)!, Translator.T("Draw every picture again? Choose No to draw only the missing ones (for example after adding mods)."),
                         Translator.T("Build pictures"), MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
        try
        {
            _pictureGame = PictureBuilder.Start(game!, redraw);
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            Say(Translator.T("Could not start the game to draw pictures:") + " " + ex.Message, false);
            return;
        }

        Say(Translator.T("Bannerlord opens and draws the pictures by itself, then closes. Do not click in it; it takes a few minutes."), true);
        ShowPictures();
        await Task.Run(() => _pictureGame.WaitForExit());
        PictureBuilder.Remove(game!);
        GameImages.Refresh();
        ShowPictures();
        Say(PictureBuilder.Store.Finished
            ? Translator.T("Pictures are ready:") + " " + PictureBuilder.Store.Summary()
            : Translator.T("The game closed before the pictures were done. Press Build pictures to go on where it stopped."), PictureBuilder.Store.Finished);
    }

    private void FindBannerlord_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = Translator.T("Pick the Mount & Blade II Bannerlord folder"), InitialDirectory = GameFolderPick.DialogStartFolder(SavedGamePath?.Invoke()), Multiselect = false };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true)
            return;
        var check = GameFolderPick.Use(dialog.FolderName, root =>
        {
            SaveGamePath?.Invoke(root);
            ApplyGameFound(root);
            Say(Translator.T(GameFolderPick.RememberedText), true);
        });
        if (check.Result != GameFolderPickResult.Ok)
            Coodsz.Ui.UiLanguage.Message(check.Message, "Find Bannerlord", MessageBoxButton.OK, MessageBoxImage.Warning, Window.GetWindow(this));
    }

    private void ApplyGameFound(string root)
    {
        _gameRoot = root;
        GameMissingPanel.Visibility = Visibility.Collapsed;
        ShowPictures();
    }
}
