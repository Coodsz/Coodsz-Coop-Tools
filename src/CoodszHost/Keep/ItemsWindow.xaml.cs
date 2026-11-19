// The Items window follows CoopKeep's ItemBrowser and its view model
// (https://github.com/Aderimo/bannerlord-coopkeep, src/CoopKeep.App/Views/ItemBrowser.axaml and
// ViewModels/MainViewModel.Items.cs), ported from Avalonia to WPF and changed for Coodsz Host.
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

using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using BannerlordCoopLauncher.Core.Keep;
using BannerlordCoopLauncher.Core.Localization;
using CoodszHost.Services;

namespace CoodszHost.Keep;

/// <summary>
/// Give items (from every item in the game, read from the server) or take items (from the
/// target's inventory). Every item that went in or out is confirmed by the server and logged.
/// </summary>
public partial class ItemsWindow : Window
{
    private readonly KeepContext _keep;
    private readonly IReadOnlyList<KeepTarget> _targets;
    private readonly Dictionary<string, ItemCard> _cards = new(StringComparer.Ordinal);
    private readonly ObservableCollection<CategoryRow> _categories = new();
    private readonly ObservableCollection<ItemCard> _shown = new();
    private readonly ObservableCollection<BasketLine> _basket = new();
    private IReadOnlyList<GameItem> _items = Array.Empty<GameItem>();
    private IReadOnlyList<InventoryItem> _inventory = Array.Empty<InventoryItem>();
    private ItemGroup? _group;
    private bool _busy;

    /// <param name="search">Text for the search box (the Encyclopedia opens the window on one item).</param>
    public ItemsWindow(KeepContext keep, IReadOnlyList<KeepTarget> targets, string? search = null)
    {
        _keep = keep;
        _targets = targets.Where(t => t.HasHero).ToList();
        InitializeComponent();
        Coodsz.Ui.WindowPlacement.KeepOnScreen(this);
        Owner = keep.Owner;
        Categories.ItemsSource = _categories;
        Cards.ItemsSource = _shown;
        Basket.ItemsSource = _basket;
        SearchBox.Tag = Translator.T("Search items... (name, type or culture)");
        if (!string.IsNullOrWhiteSpace(search))
            SearchBox.Text = search;
        TargetRun.Text = _targets.Count == 1 ? _targets[0].Name : Translator.T(_targets.Count + " players picked");
        _basket.CollectionChanged += (_, _) => ShowBasket();
        Loaded += async (_, _) => await LoadAsync();
        ShowBasket();
    }

    private bool Taking => TakeMode.IsChecked == true;

    private void Note(string text)
    {
        NoteText.Text = text;
        NoteBox.Visibility = text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private async Task LoadAsync()
    {
        if (_targets.Count == 0)
        {
            Note("Pick a player whose hero is known first.");
            return;
        }

        var blocked = _keep.BlockReason();
        if (blocked.Length > 0)
        {
            Note(blocked);
            return;
        }

        Note("Reading every item from the server...");
        if (_keep.Client.ToolsReady != true && !await _keep.Client.CheckToolsAsync())
        {
            Note(KeepClient.NoTools);
            return;
        }

        _items = await _keep.Client.ItemsAsync();
        if (_items.Count == 0)
        {
            Note("The server did not send its item list. Try again in a moment.");
            return;
        }

        Note(PictureBuilder.Store.HasPictures ? "" : "No pictures yet: the cards show names. Build pictures on the Cheats page draws them with your own game.");
        foreach (var item in _items)
            _cards.TryAdd(item.Id, new ItemCard(item));
        ShowCategories(_items);
        Filter();
    }

    private void ShowCategories(IEnumerable<GameItem> items)
    {
        var list = items.ToList();
        _categories.Clear();
        _categories.Add(new CategoryRow(null, list.Count));
        foreach (var (group, count) in ItemCatalog.Counts(list))
            _categories.Add(new CategoryRow(group, count));
        Dispatcher.BeginInvoke(() =>
        {
            // Check the category that was picked (All items the first time).
            for (var i = 0; i < _categories.Count; i++)
            {
                if (Categories.ItemContainerGenerator.ContainerFromIndex(i) is ContentPresenter presenter
                    && FindChild<RadioButton>(presenter) is { } radio && _categories[i].Group == _group)
                {
                    radio.IsChecked = true;
                    break;
                }
            }
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private static T? FindChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is T found)
                return found;
            if (FindChild<T>(child) is { } deeper)
                return deeper;
        }

        return null;
    }

    /// <summary>In take mode the cards are the target's inventory (with how many they have).</summary>
    private IEnumerable<GameItem> Source() =>
        Taking
            ? _inventory.Select(i => _cards.TryGetValue(i.Id, out var card) ? card.Item : new GameItem(i.Id, i.Name, "", i.Value, 0, "", "", false, false, false, "", 0))
            : _items;

    private void Filter()
    {
        if (ResultText == null)
            return;
        var sort = SortExpensive.IsChecked == true ? ItemSort.MostExpensive : SortTier.IsChecked == true ? ItemSort.Tier : SortName.IsChecked == true ? ItemSort.Name : ItemSort.Cheapest;
        var wear = WearCivilian.IsChecked == true ? ItemWear.Civilian : WearBattle.IsChecked == true ? ItemWear.Battle : ItemWear.All;
        var page = ItemCatalog.Filter(Source(), _group, SearchBox.Text, sort, wear);
        _shown.Clear();
        foreach (var item in page.Shown)
        {
            if (!_cards.TryGetValue(item.Id, out var card))
                _cards[item.Id] = card = new ItemCard(item);
            _shown.Add(card);
        }

        ResultText.Text = ItemCatalog.ResultText(page);
        CardScroll.ScrollToTop();
    }

    private void Category_Checked(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is CategoryRow row)
        {
            _group = row.Group;
            Filter();
        }
    }

    private void Search_Changed(object sender, TextChangedEventArgs e) => Filter();

    private void Filter_Changed(object sender, RoutedEventArgs e) => Filter();

    private async void Mode_Checked(object sender, RoutedEventArgs e)
    {
        if (ApplyButton == null)
            return;
        _basket.Clear();
        _group = null;
        GoldButton.Visibility = Taking ? Visibility.Collapsed : Visibility.Visible;
        if (Taking)
        {
            if (_targets.Count != 1)
            {
                Note("Take items works on one player at a time. Pick one player.");
                _inventory = Array.Empty<InventoryItem>();
            }
            else if (_keep.CanSend)
            {
                _inventory = await _keep.Client.InventoryAsync(_targets[0].HeroId);
                Note(_inventory.Count == 0 ? _targets[0].Name + " has nothing in the inventory, or the hero has no party." : "");
            }
        }
        else
        {
            Note("");
        }

        ShowCategories(Source());
        Filter();
    }

    private int PerClick()
    {
        var n = SkillSheet.AmountOf(PerClickBox.Text);
        return Math.Clamp(n <= 0 ? 1 : n, 1, ToolsCommand.MaxItems);
    }

    private void PerClick_Click(object sender, RoutedEventArgs e)
    {
        var step = (sender as FrameworkElement)?.Tag as string == "-1" ? -1 : 1;
        var now = PerClick();
        var next = step > 0 ? (now < 10 ? now + 1 : now < 100 ? now + 10 : now + 100) : (now <= 10 ? now - 1 : now <= 100 ? now - 10 : now - 100);
        PerClickBox.Text = Math.Clamp(next, 1, ToolsCommand.MaxItems).ToString(CultureInfo.InvariantCulture);
    }

    private void Card_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not ItemCard card)
            return;
        var have = Taking ? _inventory.Where(i => i.Id == card.Item.Id).Sum(i => i.Count) : 0;
        var line = _basket.FirstOrDefault(l => ReferenceEquals(l.Card, card));
        var add = PerClick();
        if (line == null)
        {
            line = new BasketLine(card, 0, have);
            _basket.Add(line);
        }

        line.Count = Taking ? Math.Min(line.Count + add, have) : Math.Min(line.Count + add, ToolsCommand.MaxItems);
        ShowBasket();
    }

    private void RemoveLine_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is BasketLine line)
            _basket.Remove(line);
    }

    private int BasketCount => _basket.Sum(l => l.Count);
    private long BasketWorth => _basket.Sum(l => (long)l.Card.Item.Value * l.Count);

    private void ShowBasket()
    {
        if (ApplyButton == null)
            return;
        BasketHint.Visibility = _basket.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        BasketValue.Text = KeepFormat.N((double)BasketWorth);
        ApplyButton.Content = Taking ? "Take items (" + BasketCount + ")" : "Give items (" + BasketCount + ")";
        ApplyButton.IsEnabled = BasketCount > 0 && !_busy;
        GoldButton.IsEnabled = BasketWorth > 0 && !_busy;
    }

    private async void Apply_Click(object sender, RoutedEventArgs e)
    {
        var lines = _basket.Where(l => l.Count > 0).ToList();
        if (lines.Count == 0 || _busy)
            return;

        _busy = true;
        ShowBasket();
        try
        {
            var done = 0;
            string? refused = null;
            var failed = new HashSet<BasketLine>();
            foreach (var target in Taking ? _targets.Take(1) : _targets)
            {
                foreach (var line in lines)
                {
                    var id = line.Card.Item.Id;
                    var command = Taking ? ToolsCommand.TakeItem(target.HeroId, id, line.Count) : ToolsCommand.GiveItem(target.HeroId, id, line.Count);
                    var audit = (Taking ? "took " : "gave ") + line.Count + "× " + line.Card.Name;
                    var result = await _keep.ChangeAsync(target, command, audit, undo: c =>
                        c.New > c.Old ? ToolsCommand.TakeItem(c.HeroId, id, c.New - c.Old)
                        : c.Old > c.New ? ToolsCommand.GiveItem(c.HeroId, id, c.Old - c.New) : null, quiet: true);
                    if (result.Ok && result.Change is { } change)
                        done += Math.Abs(change.New - change.Old);
                    else if (!result.Ok)
                    {
                        refused = result.Message;
                        failed.Add(line);
                    }
                }
            }

            // A refusal (no party, unknown hero) stays on screen and its items stay in the
            // basket; only the lines the server confirmed leave it.
            StatusText.Text = refused ?? (Taking ? "Took " + done + " items. The server confirmed each one." : "Gave " + done + " items. The server confirmed each one.");
            foreach (var line in lines.Where(l => !failed.Contains(l)))
                _basket.Remove(line);
            if (Taking && _targets.Count == 1)
            {
                _inventory = await _keep.Client.InventoryAsync(_targets[0].HeroId);
                ShowCategories(Source());
                Filter();
            }
        }
        finally
        {
            _busy = false;
            ShowBasket();
        }
    }

    /// <summary>Gives the basket's value as gold instead of the items (each target gets the full value).</summary>
    private async void GiveGold_Click(object sender, RoutedEventArgs e)
    {
        var worth = (int)Math.Min(BasketWorth, ToolsCommand.MaxGold);
        if (worth <= 0 || _busy)
            return;

        _busy = true;
        ShowBasket();
        try
        {
            var names = string.Join(", ", _basket.Select(l => l.Count + "× " + l.Card.Name));
            foreach (var target in _targets)
            {
                var result = await _keep.ChangeAsync(target, ToolsCommand.Gold(target.HeroId, worth), "gold +" + KeepFormat.N(worth) + " for items: " + names,
                    undo: c => c.Changed ? ToolsCommand.Gold(c.HeroId, c.Old - c.New) : null);
                StatusText.Text = result.Ok ? "Gave " + KeepFormat.N(worth) + " gold." : result.Message;
            }

            _basket.Clear();
        }
        finally
        {
            _busy = false;
            ShowBasket();
        }
    }

    private void Header_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed && e.OriginalSource is not Button)
            DragMove();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
