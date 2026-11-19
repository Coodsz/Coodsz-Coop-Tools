using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using CoodszHost.Services;

namespace CoodszHost;

public sealed class HostAddModsWindow : Window
{
    private readonly ListBox _list = new();
    private readonly TextBlock _note = new();
    private readonly TextBlock _status = new();
    private readonly Button _add = new();
    private readonly Button _refresh = new();
    private readonly Func<IReadOnlyList<FoundServerMod>> _scan;
    private readonly HashSet<string> _seen = new(StringComparer.OrdinalIgnoreCase);
    private readonly DispatcherTimer _refreshPulse = new() { Interval = TimeSpan.FromMilliseconds(700) };

    private HostAddModsWindow(Func<IReadOnlyList<FoundServerMod>> scan)
    {
        _scan = scan;
        Title = "Add mods";
        Width = 1000;
        Height = 720;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Coodsz.Ui.WindowPlacement.KeepOnScreen(this);
        Background = new SolidColorBrush(Color.FromRgb(14, 20, 24));
        Foreground = new SolidColorBrush(Color.FromRgb(213, 222, 230));
        FontFamily = new FontFamily("Segoe UI");

        _note.TextWrapping = TextWrapping.Wrap;
        _note.Margin = new Thickness(0, 0, 0, 8);
        _note.Foreground = new SolidColorBrush(Color.FromRgb(142, 160, 174));
        _status.TextWrapping = TextWrapping.Wrap;
        _status.Margin = new Thickness(0, 0, 0, 12);
        _status.FontWeight = FontWeights.SemiBold;
        _status.Foreground = new SolidColorBrush(Color.FromRgb(226, 194, 122));
        _status.Visibility = Visibility.Collapsed;
        _list.SelectionMode = SelectionMode.Extended;
        _list.Background = new SolidColorBrush(Color.FromRgb(16, 22, 27));
        _list.BorderBrush = new SolidColorBrush(Color.FromRgb(42, 56, 68));
        _list.Foreground = new SolidColorBrush(Color.FromRgb(213, 222, 230));
        var selectedYellow = new SolidColorBrush(Color.FromRgb(232, 214, 156));
        var selectedText = new SolidColorBrush(Color.FromRgb(26, 22, 12));
        _list.Resources[SystemColors.HighlightBrushKey] = selectedYellow;
        _list.Resources[SystemColors.InactiveSelectionHighlightBrushKey] = selectedYellow;
        _list.Resources[SystemColors.HighlightTextBrushKey] = selectedText;
        _list.Resources[SystemColors.InactiveSelectionHighlightTextBrushKey] = selectedText;

        _add.Content = "Add selected";
        _add.Padding = new Thickness(18, 8, 18, 8);
        _add.Margin = new Thickness(0, 12, 8, 0);
        _add.Background = new SolidColorBrush(Color.FromRgb(31, 78, 121));
        _add.Foreground = new SolidColorBrush(Color.FromRgb(213, 222, 230));
        _add.BorderBrush = new SolidColorBrush(Color.FromRgb(142, 180, 212));
        _add.Click += (_, _) =>
        {
            // Nothing picked used to close the window as if mods were added.
            if (_list.SelectedItems.Count == 0)
            {
                _status.Text = "Pick at least one mod, then click Add selected.";
                _status.Visibility = Visibility.Visible;
                return;
            }
            if (SelectedOverhauls().Count > 0 &&
                !ConfirmWindow.Ask(this,
                    "Total overhaul mods",
                    "Total overhaul mods will not work at all. They have a very high chance of breaking your server and your game.\n\nAdd them anyway?"))
                return;
            DialogResult = true;
            Close();
        };

        _refresh.Content = "Refresh";
        _refresh.Padding = new Thickness(18, 8, 18, 8);
        _refresh.Margin = new Thickness(0, 12, 8, 0);
        _refresh.Background = new SolidColorBrush(Color.FromRgb(58, 52, 32));
        _refresh.Foreground = new SolidColorBrush(Color.FromRgb(226, 194, 122));
        _refresh.BorderBrush = new SolidColorBrush(Color.FromRgb(226, 194, 122));
        _refresh.BorderThickness = new Thickness(2);
        _refresh.FontWeight = FontWeights.SemiBold;
        _refresh.Click += (_, _) => LoadMods(fromRefresh: true);
        _refreshPulse.Tick += (_, _) =>
        {
            _refreshPulse.Stop();
            _refresh.Content = "Refresh";
            _refresh.Background = new SolidColorBrush(Color.FromRgb(58, 52, 32));
            _refresh.Foreground = new SolidColorBrush(Color.FromRgb(226, 194, 122));
        };

        var close = new Button
        {
            Content = "Close",
            ToolTip = "Closes this window.",
            Padding = new Thickness(18, 8, 18, 8),
            Margin = new Thickness(0, 12, 0, 0),
            Background = new SolidColorBrush(Color.FromRgb(27, 40, 51)),
            Foreground = new SolidColorBrush(Color.FromRgb(213, 222, 230)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(58, 76, 92))
        };
        close.Click += (_, _) => Close();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(_refresh);
        buttons.Children.Add(_add);
        buttons.Children.Add(close);

        var warning = new TextBlock
        {
            Text = "Total overhaul mods will not work at all. They have a very high chance of breaking your server and your game.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12),
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(232, 176, 176))
        };
        var header = new StackPanel();
        header.Children.Add(_note);
        header.Children.Add(warning);
        header.Children.Add(_status);
        var stack = new DockPanel { Margin = new Thickness(18) };
        DockPanel.SetDock(header, Dock.Top);
        DockPanel.SetDock(buttons, Dock.Bottom);
        stack.Children.Add(header);
        stack.Children.Add(buttons);
        stack.Children.Add(_list);
        Content = stack;
        LoadMods(fromRefresh: false);
    }

    private void LoadMods(bool fromRefresh)
    {
        var mods = _scan();
        var fresh = mods.Where(mod => !_seen.Contains(mod.ModuleId)).Select(mod => mod.ModuleId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        _list.Items.Clear();
        ListBoxItem? firstNew = null;
        foreach (var mod in mods)
        {
            var isNew = fromRefresh && fresh.Contains(mod.ModuleId);
            var overhaul = IsTotalOverhaul(mod.ModuleId, mod.Name);
            var background = overhaul ? Color.FromRgb(62, 34, 38) : isNew ? Color.FromRgb(58, 52, 32) : Color.FromRgb(16, 22, 27);
            var accent = overhaul ? Color.FromRgb(196, 132, 132) : isNew ? Color.FromRgb(226, 194, 122) : Color.FromRgb(16, 22, 27);
            var text = overhaul ? Color.FromRgb(232, 176, 176) : isNew ? Color.FromRgb(226, 194, 122) : Color.FromRgb(213, 222, 230);
            var label = new TextBlock
            {
                Text = mod.Name + "  —  " + mod.Source + (overhaul ? "  —  total overhaul, will not work" : "") + (isNew ? "  —  new" : ""),
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(text),
                FontWeight = overhaul || isNew ? FontWeights.SemiBold : FontWeights.Normal
            };
            var row = new Border
            {
                Background = new SolidColorBrush(background),
                BorderBrush = new SolidColorBrush(accent),
                BorderThickness = new Thickness(overhaul || isNew ? 2 : 0),
                Padding = new Thickness(8, 6, 8, 6),
                Child = WithBadge(label, mod)
            };
            var item = new ListBoxItem
            {
                Content = row,
                Tag = mod,
                Padding = new Thickness(0),
                FocusVisualStyle = null,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Background = Brushes.Transparent
            };
            item.Selected += (_, _) => PaintSelected(row, label, true, background, accent, text, overhaul || isNew);
            item.Unselected += (_, _) => PaintSelected(row, label, false, background, accent, text, overhaul || isNew);
            _list.Items.Add(item);
            if (isNew && firstNew == null)
                firstNew = item;
        }

        _seen.Clear();
        foreach (var mod in mods)
            _seen.Add(mod.ModuleId);
        _add.IsEnabled = mods.Count > 0;
        _note.Text = mods.Count == 0
            ? "No extra mods were found. This checks Steam Workshop and the Modules folder in the Bannerlord install. Click Refresh after a download. If a mod is not listed in Steam Workshop and is not already in that Modules folder, unzip it, then drag the folder next to this tool. Drag the folder that has SubModule.xml in it, then click Refresh."
            : "Select one or more. Workshop mods are labeled Workshop. Mods already in the Bannerlord Modules folder, or a folder next to this tool, are labeled From Nexus or ModDB. Click Refresh after a new download.";

        if (!fromRefresh)
        {
            _status.Visibility = Visibility.Collapsed;
            return;
        }

        _status.Visibility = Visibility.Visible;
        _status.Text = fresh.Count == 0
            ? "Refreshed. No new mods."
            : fresh.Count == 1
                ? "Refreshed. 1 new mod was found. It is highlighted."
                : "Refreshed. " + fresh.Count + " new mods were found. They are highlighted.";
        if (firstNew != null)
            _list.ScrollIntoView(firstNew);
        _refresh.Content = "Checked";
        _refresh.Background = new SolidColorBrush(Color.FromRgb(226, 194, 122));
        _refresh.Foreground = new SolidColorBrush(Color.FromRgb(26, 22, 12));
        _refreshPulse.Stop();
        _refreshPulse.Start();
    }

    private static void PaintSelected(Border row, TextBlock label, bool selected, Color background, Color accent, Color text, bool marked)
    {
        row.Background = new SolidColorBrush(selected ? Color.FromRgb(232, 214, 156) : background);
        row.BorderBrush = new SolidColorBrush(selected ? Color.FromRgb(212, 184, 96) : accent);
        row.BorderThickness = new Thickness(selected || marked ? 2 : 0);
        label.Foreground = new SolidColorBrush(selected ? Color.FromRgb(26, 22, 12) : text);
        label.FontWeight = selected || marked ? FontWeights.SemiBold : FontWeights.Normal;
    }

    private List<FoundServerMod> SelectedOverhauls() =>
        _list.SelectedItems.Cast<ListBoxItem>()
            .Select(item => (FoundServerMod)item.Tag)
            .Where(mod => IsTotalOverhaul(mod.ModuleId, mod.Name))
            .ToList();

    // Only the modules TAOM installs; TAOM community mods such as TAOM_PlayerVoices are not overhauls.
    private static bool IsTotalOverhaul(string moduleId, string name) =>
        BannerlordCoopLauncher.Core.Modules.TotalOverhaul.Is(moduleId);

    // The community list badge on the right (Unknown, not tested in grey when nobody reported the
    // mod), and this PC's own result next to it when there is one.
    private static UIElement WithBadge(TextBlock label, FoundServerMod mod)
    {
        // The AD1259 server tool keeps its Add mods list as it was.
        if (!Edition.ShowsModResults)
            return label;
        var badge = BannerlordCoopLauncher.Core.Modules.CommunityCompat.Badge(mod.ModuleId, BannerlordCoopLauncher.Core.Modules.CommunityCompat.Current);
        if (badge == null)
            return label;
        var color = badge.Kind switch
        {
            "Works" => Color.FromRgb(0x8F, 0xBF, 0x86),
            "Broken" => Color.FromRgb(0xE0, 0x7A, 0x6E),
            "ClientOnly" => Color.FromRgb(0x8E, 0xB4, 0xD4),
            _ => Color.FromRgb(0x9A, 0xA6, 0xB0)
        };
        var click = BannerlordCoopLauncher.Core.Modules.CommunityCompat.ReportTip;
        var tip = string.Join("\n", badge.Tip.Split('\n').Where(line => line != click));
        var panel = new DockPanel { LastChildFill = true };
        var mark = new Border
        {
            Background = new SolidColorBrush(color),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 2, 8, 2),
            Margin = new Thickness(12, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = tip,
            Child = new TextBlock { Text = badge.Text, Foreground = new SolidColorBrush(Color.FromRgb(16, 18, 20)), FontWeight = FontWeights.SemiBold, FontSize = 12 }
        };
        DockPanel.SetDock(mark, Dock.Right);
        panel.Children.Add(mark);
        var mine = BannerlordCoopLauncher.Core.Modules.ModResults.Pill(mod.ModuleId, VersionIn(mod.Directory));
        if (mine != null)
        {
            var ink = mine.Good ? Color.FromRgb(0x8F, 0xCB, 0x9B) : Color.FromRgb(0xE0, 0x8B, 0x84);
            var pill = new Border
            {
                Background = new SolidColorBrush(mine.Good ? Color.FromRgb(0x1E, 0x33, 0x26) : Color.FromRgb(0x3A, 0x24, 0x22)),
                BorderBrush = new SolidColorBrush(ink),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(9),
                Padding = new Thickness(7, 1, 7, 1),
                Margin = new Thickness(8, 0, 0, 0),
                Opacity = mine.Older ? 0.5 : 1,
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = mine.Tip,
                Child = new TextBlock { Text = mine.Text, FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(ink) }
            };
            DockPanel.SetDock(pill, Dock.Right);
            panel.Children.Add(pill);
        }

        panel.Children.Add(label);
        return panel;
    }

    private static string VersionIn(string directory)
    {
        try
        {
            return System.IO.Directory.Exists(directory) ? BannerlordCoopLauncher.Core.Parsing.ModuleManifestReader.Read(directory)?.Version ?? "" : "";
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            return "";
        }
    }

    public static IReadOnlyList<FoundServerMod> Choose(Window owner, Func<IReadOnlyList<FoundServerMod>> scan)
    {
        var window = new HostAddModsWindow(scan) { Owner = owner };
        if (window.ShowDialog() != true)
            return Array.Empty<FoundServerMod>();
        return window._list.SelectedItems.Cast<ListBoxItem>().Select(item => (FoundServerMod)item.Tag).ToList();
    }
}
