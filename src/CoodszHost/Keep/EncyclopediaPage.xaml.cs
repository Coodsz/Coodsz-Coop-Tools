// The Encyclopedia page follows CoopKeep's EncyclopediaPage view (https://github.com/Aderimo/bannerlord-coopkeep,
// src/CoopKeep.App/Views/Pages/EncyclopediaPage.axaml), ported from Avalonia to WPF and changed for Coodsz
// Host: the world is read from the running server, and player heroes open the profile and the Cheats page.
//
// Copyright (c) 2026 aderimo
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BannerlordCoopLauncher.Core.Keep;
using BannerlordCoopLauncher.Core.Localization;

namespace CoodszHost.Keep;

/// <summary>A row of the list, with its tag in the picked language.</summary>
public sealed record EncRowView(EncRow Row)
{
    public string Title => Row.Title;
    public string Subtitle => Row.Subtitle;
    public string Tag => Translator.T(Row.Tag);
}

public sealed record EncFactView(string Label, string Value);

public sealed record EncLinkView(EncLink Link)
{
    public string Title => Link.Title;
    public string Sub => Link.Sub;
    public bool HasSub => Link.Sub.Length > 0;
}

public sealed record EncGroupView(string Title, IReadOnlyList<EncLinkView> Links);

/// <summary>The card on the right: the entry with its fixed words in the picked language and its picture.</summary>
public sealed class EncCard
{
    public EncCard(EncEntry entry, bool onMap = false)
    {
        Entry = entry;
        IsOnMap = onMap;
        Image = GameImages.Get(entry.ImageKind, entry.Id);
        Facts = entry.Facts.Select(f => new EncFactView(Translator.T(f.Label), f.TranslateValue ? Translator.T(f.Value) : f.Value)).ToList();
        Groups = entry.Groups.Select(g => new EncGroupView(Translator.T(g.Title), g.Links.Select(l => new EncLinkView(l)).ToList())).ToList();
    }

    public EncEntry Entry { get; }
    public GameImage Image { get; }
    public string Title => Entry.Title;
    public string Subtitle => Entry.Subtitle;
    public string Initial => Entry.Initial;
    public string Text => Entry.Text;
    public bool HasText => Entry.Text.Length > 0;
    public string TagText => Translator.T(Entry.Tag);
    public bool HasTag => Entry.Tag.Length > 0;
    public bool IsPlayer => Entry.IsPlayer && Entry.Section == EncSection.Lords;
    public bool IsItem => Entry.Section == EncSection.Items;

    /// <summary>A town, castle, village or hero: Show on map opens the Map page centred on it.</summary>
    public bool IsOnMap { get; }
    public IReadOnlyList<EncFactView> Facts { get; }
    public IReadOnlyList<EncGroupView> Groups { get; }
    public string BackText => Translator.T("Back");
    public string ProfileText => Translator.T("Open profile");
    public string CheatsText => Translator.T("Open in Cheats");
    public string ItemsText => Translator.T("Give with the Items window");
    public string ShowOnMapText => Translator.T("Show on map");
    public string ItemsTip => Translator.T("Opens the Items window for the players picked on the Cheats page, with this item searched.");
}

/// <summary>
/// The Encyclopedia page. Everything it shows comes from the running server through
/// <see cref="KeepClient.WorldAsync"/>; when the server is stopped it says so and offers Start.
/// </summary>
public partial class EncyclopediaPage : UserControl
{
    private readonly Stack<(EncSection Section, string Id)> _back = new();
    private KeepContext? _keep;
    private Func<bool> _running = () => false;
    private WorldData _world = WorldData.Empty;
    private EncSection _section = EncSection.Lords;
    private string _filter = Encyclopedia.All;
    private EncCard? _card;
    private bool _loading;
    private bool _quiet;
    private string _error = "";
    private DateTime? _readAt;
    private (EncSection Section, string Id)? _pending;

    public EncyclopediaPage()
    {
        InitializeComponent();
        Hints();
        Translator.Changed += () => Dispatcher.BeginInvoke(() =>
        {
            Hints();
            ShowChips();
            ShowList();
            if (_card != null)
                ShowCard(_card.Entry);
        });
    }

    /// <summary>Start server on the stopped card.</summary>
    public event Action? StartRequested;

    /// <summary>Open profile on a player hero: (hero id, name).</summary>
    public event Action<string, string>? ProfileRequested;

    /// <summary>Open in Cheats on a player hero: (hero id, name).</summary>
    public event Action<string, string>? CheatsRequested;

    /// <summary>Show on map on a settlement or hero: (section, id, name).</summary>
    public event Action<EncSection, string, string>? MapRequested;

    /// <summary>Give with the Items window on an item: the item name. Returns a line to show, or "" when the window opened.</summary>
    public Func<string, string>? ItemsRequested { get; set; }

    private void Hints() => SearchBox.Tag = Translator.T("Search by name...");

    /// <summary>Called once by MainWindow.</summary>
    public void Attach(KeepContext keep, Func<bool> running)
    {
        _keep = keep;
        _running = running;
    }

    /// <summary>A server start: the world is read again from the new campaign.</summary>
    public void ServerStarting()
    {
        _world = WorldData.Empty;
        _error = "";
        _readAt = null;
        _back.Clear();
        _card = null;
    }

    /// <summary>The page was opened, or the one second clock ticked while it is open.</summary>
    public void Shown()
    {
        if (_keep == null)
            return;
        var running = _running();
        if (!running && (!_world.IsEmpty || _error.Length > 0))
            ServerStarting();
        // Reads the world by itself once the server can answer.
        if (running && _keep.CanSend && _world.IsEmpty && !_loading && _error.Length == 0)
            _ = LoadAsync(false);
        ShowState();
        ReadText.Text = _readAt is { } at && !_world.IsEmpty ? HomeDashboard.Ago(at, DateTime.Now) : "";
    }

    private void ShowState()
    {
        var running = _running();
        var (state, headline, line) = Encyclopedia.Status(running, running ? _keep?.BlockReason() ?? "" : "", _loading, _error, !_world.IsEmpty);
        var ready = state == EncState.Ready;
        Browser.Visibility = ready ? Visibility.Visible : Visibility.Collapsed;
        StateCard.Visibility = ready ? Visibility.Collapsed : Visibility.Visible;
        StateHeadline.Text = headline;
        StateLine.Text = line;
        StartButton.Visibility = state == EncState.Stopped ? Visibility.Visible : Visibility.Collapsed;
        TryAgainButton.Visibility = state == EncState.Failed ? Visibility.Visible : Visibility.Collapsed;
        RefreshButton.IsEnabled = running && !_loading && state is EncState.Ready or EncState.Failed;
    }

    private async Task LoadAsync(bool refresh)
    {
        if (_keep == null || _loading)
            return;
        _loading = true;
        _error = "";
        ShowState();
        try
        {
            if (_keep.Client.ToolsReady != true && !await _keep.Client.CheckToolsAsync())
            {
                _error = KeepClient.NoTools;
                return;
            }

            var (world, message) = await _keep.Client.WorldAsync(refresh);
            _error = message;
            if (!world.IsEmpty)
            {
                _world = world;
                _readAt = DateTime.Now;
                ShowWorld();
            }
        }
        finally
        {
            _loading = false;
            ShowState();
        }
    }

    /// <summary>Shows a world without the server (the render check uses it).</summary>
    public void ShowWorld(WorldData world)
    {
        _world = world;
        _running = () => true;
        _readAt = DateTime.Now;
        ShowWorld();
        ShowState();
    }

    private void ShowWorld()
    {
        CountLords.Text = Encyclopedia.Count(EncSection.Lords, _world).ToString();
        CountClans.Text = Encyclopedia.Count(EncSection.Clans, _world).ToString();
        CountKingdoms.Text = Encyclopedia.Count(EncSection.Kingdoms, _world).ToString();
        CountSettlements.Text = Encyclopedia.Count(EncSection.Settlements, _world).ToString();
        CountTroops.Text = Encyclopedia.Count(EncSection.Troops, _world).ToString();
        CountItems.Text = Encyclopedia.Count(EncSection.Items, _world).ToString();
        // An open card is read again from the new world; one that is gone closes.
        if (_card != null && Encyclopedia.Entry(_world, _card.Entry.Section, _card.Entry.Id) is { } entry)
            ShowCard(entry);
        else
            CloseCard();
        ShowChips();
        ShowList();
        if (_pending is { } pending)
        {
            _pending = null;
            OpenEntry(pending.Section, pending.Id);
        }
    }

    /// <summary>Opens one entry from another page (Open in Encyclopedia on the map); waits for the world when it is not read yet.</summary>
    public void OpenEntry(EncSection section, string id)
    {
        if (_world.IsEmpty)
        {
            _pending = (section, id);
            return;
        }

        _back.Clear();
        Open(section, id);
    }

    // --- sections, chips and the list ----------------------------------------

    private void Section_Checked(object sender, RoutedEventArgs e)
    {
        // The first section is checked while the page is still being built.
        if (_quiet || Chips is null || sender is not RadioButton { Tag: string tag } || !Enum.TryParse<EncSection>(tag, out var section))
            return;
        _section = section;
        _filter = Encyclopedia.All;
        ShowChips();
        ShowList();
    }

    private void ShowChips()
    {
        Chips.Children.Clear();
        foreach (var filter in Encyclopedia.Filters(_section, _world))
        {
            var chip = new RadioButton
            {
                Style = (Style)FindResource("Keep.Pill"),
                GroupName = "EncFilter",
                Content = filter.Fixed ? Translator.T(filter.Label) : filter.Label,
                IsChecked = filter.Key == _filter,
                Margin = new Thickness(0, 0, 6, 6),
                Tag = filter.Key,
                ToolTip = "Shows only the entries that match this filter.",
            };
            chip.Checked += (_, _) =>
            {
                _filter = (string)chip.Tag;
                ShowList();
            };
            Chips.Children.Add(chip);
        }
    }

    private void Search_Changed(object sender, TextChangedEventArgs e) => ShowList();

    private void ShowList()
    {
        var rows = Encyclopedia.Search(_section, _world, SearchBox.Text, _filter);
        var selected = _card?.Entry;
        _quiet = true;
        EntryList.ItemsSource = rows.Select(r => new EncRowView(r)).ToList();
        EntryList.SelectedItem = selected == null ? null : EntryList.Items.Cast<EncRowView>().FirstOrDefault(r => r.Row.Section == selected.Section && r.Row.Id == selected.Id);
        _quiet = false;
        CountText.Text = Translator.T(Encyclopedia.CountText(rows.Count));
    }

    private void Entry_Selected(object sender, SelectionChangedEventArgs e)
    {
        if (_quiet || EntryList.SelectedItem is not EncRowView row)
            return;
        _back.Clear();
        if (Encyclopedia.Entry(_world, row.Row.Section, row.Row.Id) is { } entry)
            ShowCard(entry);
    }

    // --- the card ----------------------------------------------------------

    private void ShowCard(EncEntry entry)
    {
        _card = new EncCard(entry, OnMap(entry));
        Card.DataContext = _card;
        BackButton.Visibility = _back.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        ActionLine.Visibility = Visibility.Collapsed;
        PickText.Visibility = Visibility.Collapsed;
        CardScroll.Visibility = Visibility.Visible;
        CardScroll.ScrollToTop();
    }

    /// <summary>Settlements the map draws (not hideouts) and heroes (the map finds their party).</summary>
    private bool OnMap(EncEntry entry) =>
        entry.Section == EncSection.Lords
        || (entry.Section == EncSection.Settlements && _world.SettlementById.TryGetValue(entry.Id, out var s) && s.Kind != "hideout");

    private void CloseCard()
    {
        _card = null;
        Card.DataContext = null;
        PickText.Visibility = Visibility.Visible;
        CardScroll.Visibility = Visibility.Collapsed;
    }

    /// <summary>Opens another entry; its section and list come along so the row shows picked.</summary>
    private void Open(EncSection section, string id)
    {
        if (Encyclopedia.Entry(_world, section, id) is not { } entry)
            return;
        if (section != _section)
        {
            _section = section;
            _filter = Encyclopedia.All;
            _quiet = true;
            SearchBox.Text = "";
            foreach (var radio in SectionButtons())
                radio.IsChecked = (string)radio.Tag == section.ToString();
            _quiet = false;
            ShowChips();
        }

        ShowCard(entry);
        ShowList();
        if (EntryList.SelectedItem != null)
            EntryList.ScrollIntoView(EntryList.SelectedItem);
    }

    private IEnumerable<RadioButton> SectionButtons() =>
        SectionList.Children.OfType<RadioButton>();

    private void Link_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not EncLinkView link || _card == null)
            return;
        _back.Push((_card.Entry.Section, _card.Entry.Id));
        Open(link.Link.Section, link.Link.Id);
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        if (_back.Count == 0)
            return;
        var (section, id) = _back.Pop();
        Open(section, id);
    }

    private void Profile_Click(object sender, RoutedEventArgs e)
    {
        if (_card is { IsPlayer: true } card)
            ProfileRequested?.Invoke(card.Entry.Id, card.Entry.Title);
    }

    private void Cheats_Click(object sender, RoutedEventArgs e)
    {
        if (_card is { IsPlayer: true } card)
            CheatsRequested?.Invoke(card.Entry.Id, card.Entry.Title);
    }

    private void Map_Click(object sender, RoutedEventArgs e)
    {
        if (_card is { IsOnMap: true } card)
            MapRequested?.Invoke(card.Entry.Section, card.Entry.Id, card.Entry.Title);
    }

    private void Items_Click(object sender, RoutedEventArgs e)
    {
        if (_card is not { IsItem: true } card || ItemsRequested == null)
            return;
        var line = ItemsRequested(card.Entry.Title);
        ActionLine.Text = Translator.T(line);
        ActionLine.Foreground = (Brush)FindResource("Keep.Warning");
        ActionLine.Visibility = line.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await LoadAsync(true);

    private void Start_Click(object sender, RoutedEventArgs e) => StartRequested?.Invoke();
}
