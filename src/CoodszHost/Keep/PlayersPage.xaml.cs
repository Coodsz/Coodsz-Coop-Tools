// The Players page follows CoopKeep's PlayersPage view (https://github.com/Aderimo/bannerlord-coopkeep,
// src/CoopKeep.App/Views/Pages/PlayersPage.axaml), ported from Avalonia to WPF and changed for Coodsz
// Host: it keeps the Server page's player and ban functions (profile, ban, Add SteamID, unban).
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

/// <summary>A connected player on the Players page.</summary>
public sealed record PlayerRow(string Name, string SteamId, string SteamLabel, string Shown = "")
{
    /// <summary>What the screen shows: the name, or "Player 1" in streamer mode.</summary>
    public string ShownName => Shown.Length > 0 ? Translator.T(Shown) : Name;
    public string Initial => ShownName.Length > 0 ? ShownName[..1].ToUpperInvariant() : "?";
    public string ProfileText => Translator.T("Profile");
    public string ProfileTip => Translator.T("Skills, equipment, inventory and army of the selected player");
    public string CheatsText => Translator.T("Cheats");
    public string BanText => Translator.T("Ban");
}

/// <summary>A banned SteamID64: the id, and what the screen shows (dots in streamer mode).</summary>
public sealed record BanRow(string SteamId, string Shown)
{
    public string UnbanText => Translator.T("Unban");
}

/// <summary>
/// The Players page. MainWindow gives it the players and the ban list and does the work when a
/// button raises one of the events, with the same code the Server page's buttons used.
/// </summary>
public partial class PlayersPage : UserControl
{
    private IReadOnlyList<PlayerRow> _players = Array.Empty<PlayerRow>();

    public PlayersPage()
    {
        InitializeComponent();
        // The search hint is set here, not as a Tag in the XAML, so it is translated like the other pages' hints.
        Hints();
        Translator.Changed += () => Dispatcher.BeginInvoke(Hints);
    }

    private void Hints() => SearchBox.Tag = Translator.T("Search for a player...");

    public event Action? RefreshRequested;
    public event Action? AddBansRequested;
    public event Action? RefreshBansRequested;
    public event Action<PlayerRow>? ProfileRequested;
    public event Action<PlayerRow>? CheatsRequested;
    public event Action<PlayerRow>? BanRequested;
    public event Action<string>? UnbanRequested;

    /// <summary>The players online now and a line about the server.</summary>
    public void ShowPlayers(IReadOnlyList<(string Name, string SteamId)> players, bool streamer, string serverLine)
    {
        _players = players
            .Select(p => new PlayerRow(p.Name, p.SteamId, StreamerMask.SteamLabel(p.SteamId, streamer) is { Length: > 0 } label ? label : Translator.T("No SteamID64 yet"),
                streamer ? StreamerNames.Alias(p.Name) : ""))
            .ToList();
        OnlineTitle.Text = "Online players: " + players.Count;
        ServerLine.Text = serverLine;
        Filter();
    }

    /// <summary>The ban list, with the ids hidden in streamer mode.</summary>
    public void ShowBans(IReadOnlyList<string> ids, bool streamer)
    {
        BanList.ItemsSource = ids.Select(id => new BanRow(id, StreamerMask.SteamId64(id, streamer))).ToList();
        BanStatus.Text = ids.Count == 0 ? "No banned players" : ids.Count == 1 ? "1 player banned" : ids.Count + " players banned";
        BanStatus.Foreground = ids.Count == 0 ? (Brush)FindResource("Keep.TextDim") : new SolidColorBrush(Color.FromRgb(255, 154, 60));
    }

    /// <summary>The answer to the last button, green or red.</summary>
    public void Say(string text, bool ok)
    {
        StatusLine.Text = text;
        StatusLine.Foreground = (Brush)FindResource(ok ? "Keep.Success" : "Keep.Red");
        StatusLine.Visibility = text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Filter()
    {
        var search = SearchBox.Text.Trim();
        var shown = search.Length == 0
            ? _players
            : _players.Where(p => p.ShownName.Contains(search, StringComparison.OrdinalIgnoreCase)).ToList();
        PlayerList.ItemsSource = shown;
        NobodyText.Text = _players.Count == 0 ? "No players connected." : "No player matches the search.";
        NobodyText.Visibility = shown.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Search_Changed(object sender, TextChangedEventArgs e) => Filter();

    private void Refresh_Click(object sender, RoutedEventArgs e) => RefreshRequested?.Invoke();

    private void AddBans_Click(object sender, RoutedEventArgs e) => AddBansRequested?.Invoke();

    private void RefreshBans_Click(object sender, RoutedEventArgs e) => RefreshBansRequested?.Invoke();

    private static T? Row<T>(object sender) where T : class => (sender as FrameworkElement)?.DataContext as T;

    private void Profile_Click(object sender, RoutedEventArgs e)
    {
        if (Row<PlayerRow>(sender) is { } row)
            ProfileRequested?.Invoke(row);
    }

    private void Cheats_Click(object sender, RoutedEventArgs e)
    {
        if (Row<PlayerRow>(sender) is { } row)
            CheatsRequested?.Invoke(row);
    }

    private void Ban_Click(object sender, RoutedEventArgs e)
    {
        if (Row<PlayerRow>(sender) is { } row)
            BanRequested?.Invoke(row);
    }

    private void Unban_Click(object sender, RoutedEventArgs e)
    {
        if (Row<BanRow>(sender) is { } row)
            UnbanRequested?.Invoke(row.SteamId);
    }
}
