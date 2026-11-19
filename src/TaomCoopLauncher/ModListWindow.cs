using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Coodsz.Ui;
using TaomCoop.Core;

namespace TaomCoopLauncher;

/// <summary>
/// The small Mod list window behind the Mod list button: the TAOM mod list in four groups (tested
/// in Coop, loads on the server, works in TAOM but not tested in Coop, risky and broken) with a
/// search box. Each row has the name, where the entry comes from, the note, the day it was checked,
/// a Workshop or Nexus link and Add to setup, which goes through the launcher's own Add mods path.
/// </summary>
public sealed class ModListWindow : Window
{
    private readonly TaomCompatList _list;
    private readonly Func<CompatEntry, Window, string> _add;
    private readonly TextBox _search = new()
    {
        FontSize = 15,
        Padding = new Thickness(8, 4, 8, 4),
        Background = BrushOf(16, 22, 27),
        Foreground = BrushOf(213, 222, 230),
        BorderBrush = BrushOf(58, 76, 92),
        CaretBrush = BrushOf(213, 222, 230)
    };
    private readonly StackPanel _groups = new();
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, FontSize = 13, Margin = new Thickness(0, 8, 0, 0), Foreground = BrushOf(143, 203, 155) };

    public ModListWindow(TaomCompatList list, string setupName, Func<CompatEntry, Window, string> add)
    {
        _list = list;
        _add = add;
        Title = "Mod list";
        Width = 760;
        Height = Math.Min(720, SystemParameters.WorkArea.Height - 40);
        MinWidth = 520;
        MinHeight = 360;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        WindowPlacement.KeepOnScreen(this);
        ShowInTaskbar = false;
        Background = BrushOf(14, 20, 24);
        Foreground = BrushOf(213, 222, 230);
        FontFamily = new FontFamily("Segoe UI");

        var top = new StackPanel { Margin = new Thickness(18, 16, 18, 8) };
        top.Children.Add(new TextBlock
        {
            Text = "Mod list",
            FontFamily = new FontFamily("Palatino Linotype"),
            FontSize = 24,
            Margin = new Thickness(0, 0, 0, 6)
        });
        top.Children.Add(new TextBlock
        {
            Text = "What is known about extra mods with TAOM " + TaomStack.TaomVersionText + " and Coop. Add to setup puts a mod on " + setupName + ", like Add mods. Hover a badge for where it comes from.",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 14,
            Foreground = BrushOf(142, 160, 174),
            Margin = new Thickness(0, 0, 0, 10)
        });
        var searchRow = new DockPanel();
        var searchLabel = new TextBlock { Text = "Search", FontSize = 14, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        DockPanel.SetDock(searchLabel, Dock.Left);
        searchRow.Children.Add(searchLabel);
        searchRow.Children.Add(_search);
        top.Children.Add(searchRow);
        top.Children.Add(_status);
        _status.Visibility = Visibility.Collapsed;
        _search.TextChanged += (_, _) => Fill();

        var close = new Button
        {
            Content = "Close",
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(18, 8, 18, 14),
            Padding = new Thickness(18, 6, 18, 6),
            FontSize = 14,
            Cursor = System.Windows.Input.Cursors.Hand,
            Background = BrushOf(27, 40, 51),
            Foreground = BrushOf(213, 222, 230),
            BorderBrush = BrushOf(58, 76, 92),
            IsCancel = true,
            ToolTip = "Closes the mod list."
        };
        close.Click += (_, _) => Close();

        var layout = new DockPanel();
        DockPanel.SetDock(top, Dock.Top);
        DockPanel.SetDock(close, Dock.Bottom);
        layout.Children.Add(top);
        layout.Children.Add(close);
        layout.Children.Add(new ScrollViewer
        {
            Content = _groups,
            Padding = new Thickness(18, 0, 18, 0),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        });
        Content = layout;
        Fill();
        Loaded += (_, _) => _search.Focus();
    }

    /// <summary>Opens the window over the launcher.</summary>
    public static void Show(Window owner, TaomCompatList list, string setupName, Func<CompatEntry, Window, string> add)
    {
        var window = new ModListWindow(list, setupName, add);
        if (owner.IsLoaded)
            window.Owner = owner;
        window.ShowDialog();
    }

    private void Fill()
    {
        _groups.Children.Clear();
        foreach (var (group, mods) in TaomModList.Groups(_list, _search.Text))
        {
            _groups.Children.Add(new TextBlock
            {
                Text = TaomModList.Title(group) + " (" + mods.Count + ")",
                FontSize = 17,
                FontWeight = FontWeights.SemiBold,
                Foreground = BrushOf(226, 194, 122),
                Margin = new Thickness(0, 12, 0, 4)
            });
            if (mods.Count == 0)
            {
                _groups.Children.Add(new TextBlock
                {
                    Text = string.IsNullOrWhiteSpace(_search.Text) ? "Nothing in this group yet." : "Nothing in this group for that search.",
                    FontSize = 13,
                    Foreground = BrushOf(142, 160, 174),
                    Margin = new Thickness(0, 0, 0, 4)
                });
                continue;
            }

            foreach (var entry in mods)
                _groups.Children.Add(Row(entry));
        }
    }

    private UIElement Row(CompatEntry entry)
    {
        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 0), LastChildFill = true };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(8, 2, 0, 0) };
        var link = TaomModList.Link(entry);
        if (link != null)
        {
            var open = SmallButton(TaomModList.LinkText(entry) ?? "Page", link);
            open.Click += (_, _) => OpenPage(link);
            buttons.Children.Add(open);
        }

        var add = SmallButton("Add to setup", "Puts this mod on the setup. Click Save mods afterwards to keep it.");
        add.Click += (_, _) => ShowStatus(_add(entry, this));
        buttons.Children.Add(add);
        DockPanel.SetDock(buttons, Dock.Right);
        row.Children.Add(buttons);

        var text = new StackPanel();
        var head = new WrapPanel();
        head.Children.Add(new TextBlock
        {
            Text = entry.Label,
            Tag = UiLanguage.NoTranslate,
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            Foreground = BrushOf(232, 230, 225),
            Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center
        });
        var badge = TaomCompatList.BadgeText(entry.Kind);
        if (badge != null)
        {
            head.Children.Add(new Border
            {
                Background = new SolidColorBrush(BadgeColor(entry.Kind)),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 1, 6, 1),
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = TaomCompatList.Hover(entry, BannerlordCoopLauncher.Core.Localization.Translator.T),
                Child = new TextBlock { Text = badge, FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = BrushOf(16, 18, 20) }
            });
        }

        // This PC's own result, when it has one (the newest, whatever version it was).
        var mine = entry.Id.Length == 0 ? null : BannerlordCoopLauncher.Core.Modules.ModResults.Pill(entry.Id, null);
        if (mine != null)
            head.Children.Add(BannerlordCoopLauncher.ModPills.Mine(mine, new Thickness(6, 0, 0, 0)));

        text.Children.Add(head);
        text.Children.Add(new TextBlock
        {
            Text = TaomCompatList.SourceLine(entry),
            FontSize = 12,
            Foreground = BrushOf(142, 180, 212),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 2, 0, 0)
        });
        if (entry.Note.Length > 0)
        {
            text.Children.Add(new TextBlock
            {
                Text = entry.Note,
                Tag = UiLanguage.NoTranslate,
                FontSize = 13,
                Foreground = BrushOf(190, 198, 205),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 0, 0)
            });
        }

        row.Children.Add(text);
        return new Border
        {
            BorderBrush = BrushOf(40, 52, 62),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(0, 6, 0, 8),
            Child = row
        };
    }

    private void ShowStatus(string line)
    {
        _status.Text = line;
        _status.Visibility = string.IsNullOrWhiteSpace(line) ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OpenPage(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            ShowStatus("Could not open the browser. " + ex.Message);
        }
    }

    private static Button SmallButton(string text, string tip) => new()
    {
        Content = text,
        ToolTip = tip,
        Margin = new Thickness(6, 0, 0, 0),
        Padding = new Thickness(10, 3, 10, 3),
        FontSize = 13,
        Cursor = System.Windows.Input.Cursors.Hand,
        Background = BrushOf(27, 40, 51),
        Foreground = BrushOf(142, 180, 212),
        BorderBrush = BrushOf(142, 180, 212)
    };

    private static Color BadgeColor(CompatTag tag) => TaomCompatList.BadgeColor(tag) switch
    {
        "green" => Color.FromRgb(0x8F, 0xBF, 0x86),
        "red" => Color.FromRgb(0xE0, 0x7A, 0x6E),
        "blue" => Color.FromRgb(0x8E, 0xB4, 0xD4),
        _ => Color.FromRgb(0xE2, 0xC2, 0x7A)
    };

    private static SolidColorBrush BrushOf(byte r, byte g, byte b) => new(Color.FromRgb(r, g, b));
}
