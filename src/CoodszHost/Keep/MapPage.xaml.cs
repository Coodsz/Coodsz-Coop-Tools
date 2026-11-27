// The live map page follows CoopKeep's map page (https://github.com/Aderimo/bannerlord-coopkeep,
// src/CoopKeep.App/Views/Pages/MapPage.axaml and ViewModels/MainViewModel.Map.cs), ported from
// Avalonia to WPF and changed for Coodsz Host: the map is drawn from the server's data instead of a
// downloaded picture, it only reads (no teleport), and players open the existing profile.

using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BannerlordCoopLauncher.Core.Keep;
using BannerlordCoopLauncher.Core.Localization;

namespace CoodszHost.Keep;

public sealed record MapLegendView(string Name, int Fiefs, Brush Brush);

public sealed record MapPlayerView(MapParty Party, string Name, string Activity, Brush ActivityBrush, string Troops, Brush Brush);

public sealed record MapResultView(object Item, string Title, string Tag);

/// <summary>
/// The live map. Everything it shows comes from the running server through
/// <see cref="KeepClient.MapAsync"/>, read again every few seconds while the page is open; when the
/// server is stopped it says so and offers Start.
/// </summary>
public partial class MapPage : UserControl
{
    private KeepContext? _keep;
    private Func<bool> _running = () => false;
    private MapData _map = MapData.Empty;
    private bool _loading;
    private string _error = "";
    private DateTime? _readAt;
    private DateTime? _fullAt;
    private DateTime _lastTry = DateTime.MinValue;
    private (bool Hero, string Id, string Name)? _focus;

    // Set once a focus that was not on the map asked for one more read before saying so.
    private bool _focusReread;
    private int _territoryVersion;
    private MapTerrain? _terrain;
    private bool _terrainLoading;
    private bool _terrainTried;
    private DateTime _terrainRetryAt = DateTime.MinValue;
    private int _world;
    private bool _quiet;
    private bool _streamer;
    private int _detailVersion;
    private readonly System.Windows.Threading.DispatcherTimer _detailTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };

    public MapPage()
    {
        InitializeComponent();
        Hints();
        // Armies, towns, castles and villages at first; other lords only when Lords is ticked.
        Canvas.SetLayers(CurrentLayers());
        Canvas.Picked += item =>
        {
            ShowDetails(item);
            _quiet = true;
            PlayerList.SelectedItem = item is MapParty p ? PlayerList.Items.Cast<MapPlayerView>().FirstOrDefault(v => v.Party.HeroId == p.HeroId) : null;
            _quiet = false;
        };
        Canvas.ViewChanged += () =>
        {
            ShowZoom();
            // Close up, a sharper territory of the part in view is drawn once the map stops moving.
            _detailTimer.Stop();
            _detailTimer.Start();
        };
        _detailTimer.Tick += (_, _) =>
        {
            _detailTimer.Stop();
            _ = BuildDetailAsync();
        };
        Translator.Changed += () => Dispatcher.BeginInvoke(() =>
        {
            Hints();
            ShowLegend();
            ShowPlayers();
            ShowDetails(Canvas.Selected);
            ShowSearch();
            Canvas.InvalidateVisual();
        });
    }

    /// <summary>Start server on the stopped card.</summary>
    public event Action? StartRequested;

    /// <summary>Open profile on a player: (hero id, name).</summary>
    public event Action<string, string>? ProfileRequested;

    /// <summary>Open in Encyclopedia on a settlement or hero: (section, id).</summary>
    public event Action<EncSection, string>? EncyclopediaRequested;

    private void Hints() => SearchBox.Tag = Translator.T("Find a player, lord or settlement...");

    /// <summary>Called once by MainWindow.</summary>
    public void Attach(KeepContext keep, Func<bool> running)
    {
        _keep = keep;
        _running = running;
    }

    /// <summary>A server start (or stop): the map is read again from the new campaign.</summary>
    public void ServerStarting()
    {
        _map = MapData.Empty;
        _error = "";
        _readAt = null;
        _fullAt = null;
        _lastTry = DateTime.MinValue;
        _territoryVersion++;
        _terrain = null;
        _terrainTried = false;
        _terrainRetryAt = DateTime.MinValue;
        _world++;
        Canvas.Reset();
        Details.Visibility = Visibility.Collapsed;
        ShowNote("");
    }

    /// <summary>Streamer mode switched: player names on the map become Player 1, Player 2.</summary>
    public void SetStreamer(bool on)
    {
        _streamer = on;
        Canvas.SetStreamer(on);
        ShowPlayers();
        ShowDetails(Canvas.Selected);
        ShowSearch();
    }

    /// <summary>The page was opened, or the one second clock ticked while it is open: reads the map when it is due.</summary>
    public void Shown()
    {
        if (_keep == null)
            return;
        var running = _running();
        if (!running && (!_map.IsEmpty || _error.Length > 0))
            ServerStarting();
        var due = _map.IsEmpty ? _error.Length == 0 : DateTime.Now - _lastTry >= LiveMap.RefreshEvery;
        if (running && _keep.CanSend && !_loading && due)
            _ = LoadAsync(false);
        ShowState();
        ReadText.Text = _readAt is { } at && !_map.IsEmpty ? Translator.T(HomeDashboard.Ago(at, DateTime.Now)) : "";
    }

    private void ShowState()
    {
        var running = _running();
        var (state, headline, line) = LiveMap.Status(running, running ? _keep?.BlockReason() ?? "" : "", _loading, _error, !_map.IsEmpty);
        var ready = state == EncState.Ready;
        Browser.Visibility = ready ? Visibility.Visible : Visibility.Collapsed;
        StateCard.Visibility = ready ? Visibility.Collapsed : Visibility.Visible;
        StateHeadline.Text = headline;
        StateLine.Text = line;
        StartButton.Visibility = state == EncState.Stopped ? Visibility.Visible : Visibility.Collapsed;
        TryAgainButton.Visibility = state == EncState.Failed ? Visibility.Visible : Visibility.Collapsed;
        RefreshButton.IsEnabled = running && !_loading && state is EncState.Ready or EncState.Failed;
        // A refresh that failed while a map is shown keeps the map and says why on it.
        if (ready && _error.Length > 0)
            ShowNote(_error);
    }

    private async Task LoadAsync(bool full)
    {
        if (_keep == null || _loading)
            return;
        _loading = true;
        _lastTry = DateTime.Now;
        ShowState();
        try
        {
            if (_keep.Client.ToolsReady != true && !await _keep.Client.CheckToolsAsync())
            {
                _error = KeepClient.NoTools;
                return;
            }

            var now = DateTime.Now;
            var settlements = full || LiveMap.NeedsSettlements(_map, _fullAt, now);
            var (map, message) = await _keep.Client.MapAsync(_map, settlements);
            _error = message;
            if (!map.IsEmpty)
            {
                if (settlements)
                    _fullAt = now;
                _readAt = DateTime.Now;
                if (_error.Length == 0 && _focus == null)
                    ShowNote("");
                Apply(map, settlements);
            }
        }
        finally
        {
            _loading = false;
            ShowState();
        }
    }

    /// <summary>Shows a map without the server (the render check uses it); the task ends when the territory is drawn.</summary>
    public Task ShowMapAsync(MapData map, bool streamer = false, MapTerrain? terrain = null)
    {
        _terrain = terrain;
        _terrainTried = true;
        _running = () => true;
        _readAt = DateTime.Now;
        _fullAt = DateTime.Now;
        _streamer = streamer;
        var territory = Apply(map, true);
        ShowState();
        return territory;
    }

    private Task Apply(MapData map, bool settlementsChanged)
    {
        _map = map;
        Canvas.SetMap(map, _streamer);
        var territory = settlementsChanged ? BuildTerritoryAsync(map) : Task.CompletedTask;
        if (settlementsChanged)
            _ = LoadTerrainAsync(map);
        if (settlementsChanged)
            ShowLegend();
        ShowPlayers();
        ShowDetails(Canvas.Selected);
        ShowSearch();
        ShowZoom();
        ApplyFocus();
        return territory;
    }

    /// <summary>
    /// The real coast, seas, rivers and land of this world from the server (coodsz.terrain), read
    /// once per server start or taken from the copy kept on this PC; the paper is drawn again with
    /// it. Until then, or when the server's tools are too old, the map is plain parchment.
    /// </summary>
    private async Task LoadTerrainAsync(MapData map)
    {
        if (_keep == null || _terrain != null || _terrainLoading || _terrainTried || DateTime.Now < _terrainRetryAt || map.IsEmpty)
            return;
        _terrainLoading = true;
        _terrainTried = true;
        var world = _world;
        try
        {
            var (terrain, message) = await _keep.Client.TerrainAsync(map);
            if (world != _world)
                return;
            if (terrain == null)
            {
                // A busy server can miss an answer: try again a minute later. Old tools (no message) are not asked again.
                if (message.Length > 0)
                {
                    _terrainTried = false;
                    _terrainRetryAt = DateTime.Now.AddMinutes(1);
                }

                return;
            }

            _terrain = terrain;
            if (!_map.IsEmpty)
                await BuildTerritoryAsync(_map);
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _terrainTried = false;
            _terrainRetryAt = DateTime.Now.AddMinutes(1);
        }
        finally
        {
            _terrainLoading = false;
        }
    }

    /// <summary>The soft territory under the map, worked out away from the window so the page stays quick.</summary>
    private async Task BuildTerritoryAsync(MapData map)
    {
        var version = ++_territoryVersion;
        var bounds = LiveMap.Bounds(map);
        const int columns = 1600;
        var rows = (int)Math.Clamp(Math.Round(columns * bounds.Height / Math.Max(1, bounds.Width)), 40, 1600);
        var territory = TerritoryBox.IsChecked == true;
        var terrain = _terrain;
        var picture = await Task.Run(() => MapPaper.Picture(map, bounds, columns, rows, LiveMap.TerritoryReach(map), territory, terrain));
        if (version != _territoryVersion)
            return;
        Canvas.SetTerritory(picture, bounds);
        await BuildDetailAsync();
    }

    /// <summary>The sharp territory of the part in view, when the map is close enough for the whole map picture to look blocky.</summary>
    private async Task BuildDetailAsync()
    {
        var version = ++_detailVersion;
        var map = _map;
        if (map.IsEmpty || Canvas.Camera.Zoom < 1.6 || Canvas.ActualWidth < 10)
        {
            Canvas.SetDetail(null, default);
            return;
        }

        var view = Canvas.VisibleBounds();
        var padX = view.Width * 0.2;
        var padY = view.Height * 0.2;
        var bounds = new MapBounds(view.MinX - padX, view.MinY - padY, view.MaxX + padX, view.MaxY + padY);
        var columns = (int)Math.Clamp(Canvas.ActualWidth * 1.4 / 1.25, 100, 1600);
        var rows = (int)Math.Clamp(Math.Round(columns * bounds.Height / Math.Max(0.001, bounds.Width)), 40, 1600);
        var territoryVersion = _territoryVersion;
        var territory = TerritoryBox.IsChecked == true;
        var terrain = _terrain;
        var picture = await Task.Run(() => MapPaper.Picture(map, bounds, columns, rows, LiveMap.TerritoryReach(map), territory, terrain));
        if (version == _detailVersion && territoryVersion == _territoryVersion)
            Canvas.SetDetail(picture, bounds);
    }

    // --- show on map -------------------------------------------------------

    /// <summary>Centres the map on a hero's party (Show on map in the profile and the Encyclopedia).</summary>
    public void FocusHero(string heroId, string name)
    {
        _focus = (true, heroId, name);
        _focusReread = false;
        ApplyFocus();
    }

    /// <summary>Centres the map on a settlement (Show on map in the Encyclopedia).</summary>
    public void FocusSettlement(string id, string name)
    {
        _focus = (false, id, name);
        _focusReread = false;
        ApplyFocus();
    }

    private void ApplyFocus()
    {
        if (_focus is not { } focus)
            return;
        if (_map.IsEmpty)
        {
            ShowNote(_running() ? "The map is being read. It goes to " + focus.Name + " once it is drawn." : "");
            return;
        }

        object? item = focus.Hero
            ? LiveMap.FindHero(_map, focus.Id)
            : _map.SettlementById.TryGetValue(focus.Id, out var s) ? s : null;
        // The map on hand can be older than the request (it is only read while this page is
        // open, so a player who joined since is not on it yet): read it once more before saying no.
        if (item == null && !_focusReread && _running() && _keep is { CanSend: true })
        {
            _focusReread = true;
            ShowNote("The map is being read. It goes to " + focus.Name + " once it is drawn.");
            _ = LoadAsync(false);
            return;
        }

        _focus = null;
        if (item == null)
        {
            ShowNote(focus.Hero
                ? focus.Name + " has no party on the map right now (captured, in a settlement without a party, or not on the map yet)."
                : focus.Name + " is not on this map.");
            return;
        }

        ShowNote("");
        Show(item, 4);
    }

    /// <summary>Picks and centres one settlement or party.</summary>
    private void Show(object item, double zoom)
    {
        var (x, y) = item switch
        {
            MapSettlement s => (s.X, s.Y),
            MapParty p => (p.X, p.Y),
            _ => (0d, 0d),
        };
        Canvas.Select(item);
        Canvas.CenterOn(x, y, zoom);
        ShowDetails(item);
    }

    private void ShowNote(string text)
    {
        NoteText.Text = Translator.T(text);
        NoteCard.Visibility = text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // --- side panel ----------------------------------------------------------

    private Brush Brush(uint argb)
    {
        var brush = new SolidColorBrush(Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb));
        brush.Freeze();
        return brush;
    }

    private void ShowLegend()
    {
        LegendList.ItemsSource = LiveMap.Legend(_map)
            .Select(r => new MapLegendView(r.KingdomId.Length == 0 ? Translator.T(r.Name) : r.Name, r.Fiefs, Brush(r.Color)))
            .ToList();
    }

    private void ShowPlayers()
    {
        var warning = (Brush)FindResource("Keep.Warning");
        var dim = (Brush)FindResource("Keep.TextDim");
        var players = _map.Parties.Where(p => p.IsPlayer)
            .Select(p => new MapPlayerView(p, Translator.T(LiveMap.ShownName(_map, p, _streamer)), MapText.Activity(_map, p),
                p.InBattle || p.Besieging ? warning : dim, p.Troops.ToString("N0", CultureInfo.CurrentCulture),
                Brush(LiveMap.ColorOf(_map, p.KingdomId, p.Color))))
            .OrderBy(v => v.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        var selected = (PlayerList.SelectedItem as MapPlayerView)?.Party.HeroId;
        _quiet = true;
        PlayerList.ItemsSource = players;
        PlayerList.SelectedItem = players.FirstOrDefault(v => v.Party.HeroId == selected);
        _quiet = false;
        NoPlayers.Visibility = players.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        var (_, lords, _) = LiveMap.Counts(_map);
        PlayerCount.Text = Translator.T(players.Count + " players and " + lords + " lords on the map");
    }

    private void Player_Selected(object sender, SelectionChangedEventArgs e)
    {
        if (_quiet || PlayerList.SelectedItem is not MapPlayerView view)
            return;
        Show(view.Party, Math.Max(Canvas.Camera.Zoom, 3));
    }

    private void Search_Changed(object sender, TextChangedEventArgs e) => ShowSearch();

    private void ShowSearch()
    {
        var query = SearchBox.Text.Trim();
        if (query.Length < 2 || _map.IsEmpty)
        {
            SearchList.ItemsSource = null;
            SearchList.Visibility = Visibility.Collapsed;
            NoMatch.Visibility = Visibility.Collapsed;
            return;
        }

        var results = new List<MapResultView>();
        foreach (var p in _map.Parties)
        {
            var name = LiveMap.ShownName(_map, p, _streamer);
            if (name.Contains(query, StringComparison.CurrentCultureIgnoreCase))
                results.Add(new MapResultView(p, Translator.T(name), Translator.T(p.IsPlayer ? "Player" : p.LeadsArmy ? "Army leader" : "Lord")));
        }

        foreach (var s in _map.Settlements)
            if (s.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase))
                results.Add(new MapResultView(s, s.Name, MapText.Kind(s)));
        var list = results
            .OrderBy(r => r.Title.StartsWith(query, StringComparison.CurrentCultureIgnoreCase) ? 0 : 1)
            .ThenBy(r => r.Item is MapParty { IsPlayer: true } ? 0 : r.Item is MapSettlement { IsVillage: false } ? 1 : 2)
            .ThenBy(r => r.Title, StringComparer.CurrentCultureIgnoreCase)
            .Take(14)
            .ToList();
        _quiet = true;
        SearchList.ItemsSource = list;
        _quiet = false;
        SearchList.Visibility = list.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        NoMatch.Visibility = list.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private void Result_Selected(object sender, SelectionChangedEventArgs e)
    {
        if (_quiet || SearchList.SelectedItem is not MapResultView result)
            return;
        Show(result.Item, Math.Max(Canvas.Camera.Zoom, 4));
    }

    private void ShowDetails(object? item)
    {
        DetailFacts.Children.Clear();
        switch (item)
        {
            case MapSettlement s:
            {
                DetailTitle.Text = s.Name;
                var owner = _map.KingdomById.TryGetValue(s.KingdomId, out var k) ? k.Name : Translator.T("No kingdom");
                DetailSub.Text = MapText.Kind(s) + "  ·  " + owner;
                if (s.OwnerClanName.Length > 0)
                    Fact("Owner", s.OwnerClanName);
                if (s.BoundId.Length > 0 && _map.SettlementById.TryGetValue(s.BoundId, out var bound))
                    Fact("Bound to", bound.Name);
                var here = _map.Parties.Where(p => p.SettlementId == s.Id).ToList();
                if (here.Count > 0)
                    Fact("Parties inside", string.Join(", ", here.Take(4).Select(p => Translator.T(LiveMap.ShownName(_map, p, _streamer)))) + (here.Count > 4 ? " +" + (here.Count - 4) : ""));
                if (s.UnderSiege)
                    Fact("Siege", Translator.T("Under siege"));
                ProfileButton.Visibility = Visibility.Collapsed;
                EncyclopediaButton.Visibility = Visibility.Visible;
                break;
            }
            case MapParty p:
            {
                DetailTitle.Text = Translator.T(LiveMap.ShownName(_map, p, _streamer));
                var who = Translator.T(p.IsPlayer ? "Player" : p.LeadsArmy ? "Army leader" : "Lord");
                var kingdom = _map.KingdomById.TryGetValue(p.KingdomId, out var k) ? k.Name : "";
                DetailSub.Text = string.Join("  ·  ", new[] { who, p.IsPlayer && _streamer ? "" : p.ClanName, kingdom }.Where(t => t.Length > 0));
                Fact("Troops", p.Troops.ToString("N0", CultureInfo.CurrentCulture));
                if (p.Wounded > 0)
                    Fact("Wounded", p.Wounded.ToString("N0", CultureInfo.CurrentCulture));
                if (p.LeadsArmy)
                    Fact("Army", p.ArmyTroops.ToString("N0", CultureInfo.CurrentCulture));
                Fact("Now", MapText.Activity(_map, p));
                ProfileButton.Visibility = p.IsPlayer ? Visibility.Visible : Visibility.Collapsed;
                EncyclopediaButton.Visibility = p.IsPlayer && _streamer ? Visibility.Collapsed : Visibility.Visible;
                break;
            }
            default:
                Details.Visibility = Visibility.Collapsed;
                return;
        }

        CentreButton.Content = Translator.T("Centre the map");
        ProfileButton.Content = Translator.T("Open profile");
        EncyclopediaButton.Content = Translator.T("Open in Encyclopedia");
        Details.Visibility = Visibility.Visible;
    }

    private void Fact(string label, string value)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = Translator.T(label), Foreground = (Brush)FindResource("Keep.TextDim"), FontSize = 10.5 });
        panel.Children.Add(new TextBlock { Text = value, FontWeight = FontWeights.SemiBold, FontSize = 13, TextWrapping = TextWrapping.Wrap, MaxWidth = 250 });
        DetailFacts.Children.Add(new Border { Style = (Style)FindResource("Fact"), Child = panel });
    }

    private void Centre_Click(object sender, RoutedEventArgs e)
    {
        if (Canvas.Selected is { } item)
            Show(item, Math.Max(Canvas.Camera.Zoom, 3));
    }

    private void Profile_Click(object sender, RoutedEventArgs e)
    {
        if (Canvas.Selected is MapParty { IsPlayer: true } p)
            ProfileRequested?.Invoke(p.HeroId, p.Name);
    }

    private void Encyclopedia_Click(object sender, RoutedEventArgs e)
    {
        switch (Canvas.Selected)
        {
            case MapSettlement s:
                EncyclopediaRequested?.Invoke(EncSection.Settlements, s.Id);
                break;
            case MapParty p:
                EncyclopediaRequested?.Invoke(EncSection.Lords, p.HeroId);
                break;
        }
    }

    // --- map controls --------------------------------------------------------

    private MapLayers CurrentLayers() =>
        new(TownsBox.IsChecked == true, CastlesBox.IsChecked == true, VillagesBox.IsChecked == true,
            LordsBox.IsChecked == true, TerritoryBox.IsChecked == true, ArmiesBox.IsChecked == true);

    private void Layer_Click(object sender, RoutedEventArgs e)
    {
        Canvas.SetLayers(CurrentLayers());
        // The kingdom tints are part of the paper picture, so it is drawn again without them (or with them).
        if (ReferenceEquals(sender, TerritoryBox) && !_map.IsEmpty)
            _ = BuildTerritoryAsync(_map);
    }

    private void Legend_Click(object sender, RoutedEventArgs e) =>
        LegendCard.Visibility = LegendToggle.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;

    private void ZoomIn_Click(object sender, RoutedEventArgs e) => Canvas.ZoomBy(1.5);

    private void ZoomOut_Click(object sender, RoutedEventArgs e) => Canvas.ZoomBy(1 / 1.5);

    private void Fit_Click(object sender, RoutedEventArgs e) => Canvas.FitAll();

    private void ShowZoom() =>
        ZoomText.Text = Canvas.Camera.Zoom.ToString("0.0", CultureInfo.CurrentCulture) + "×";

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await LoadAsync(true);

    private void Start_Click(object sender, RoutedEventArgs e) => StartRequested?.Invoke();
}
