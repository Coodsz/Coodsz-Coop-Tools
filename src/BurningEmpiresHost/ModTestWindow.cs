using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using BannerlordCoopLauncher.Core.Localization;
using Coodsz.Ui;
using BurningEmpires.Core;
using TaomCoop.Core;

namespace BurningEmpiresHost;

/// <summary>
/// The "Test mods on the server" window: one row per mod with a tick box, the name and version,
/// the mod list badge and the last test result. Start test runs the ticked mods one at a time
/// (see <see cref="ModLoadTest"/>) and each row shows its live state, with a gold progress bar
/// and the elapsed time. Stop ends the running test. Copy results into the mod list file writes
/// the passed mods into a copy of the Burning Empires mod list.
/// </summary>
public sealed class ModTestWindow : Window
{
    public delegate Task<string> RunTests(IReadOnlyList<ModTestCandidate> mods, IProgress<ModTestProgress> progress, Action<ModTestResult> recorded, CancellationToken cancellationToken);

    // The tool's own look (MainWindow.xaml): the hover button, Primary, Ghost and Danger buttons, the
    // tick box, and a row that lights up under the mouse.
    private const string Styles = """
        <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
            <ControlTemplate x:Key="HoverButton" TargetType="Button">
                <Border Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="{TemplateBinding BorderThickness}">
                    <Grid>
                        <Border x:Name="Hover" Background="#33F6EDE0" BorderBrush="#FF8C7355" BorderThickness="2" Opacity="0" IsHitTestVisible="False"/>
                        <ContentPresenter Margin="{TemplateBinding Padding}" HorizontalAlignment="Center" VerticalAlignment="Center" TextElement.Foreground="{TemplateBinding Foreground}"/>
                    </Grid>
                </Border>
                <ControlTemplate.Triggers>
                    <MultiTrigger>
                        <MultiTrigger.Conditions>
                            <Condition Property="IsMouseOver" Value="True"/>
                            <Condition Property="IsEnabled" Value="True"/>
                        </MultiTrigger.Conditions>
                        <Setter TargetName="Hover" Property="Opacity" Value="1"/>
                    </MultiTrigger>
                    <Trigger Property="IsEnabled" Value="False">
                        <Setter Property="Opacity" Value="0.45"/>
                    </Trigger>
                </ControlTemplate.Triggers>
            </ControlTemplate>
            <Style x:Key="Base" TargetType="Button">
                <Setter Property="BorderThickness" Value="1"/>
                <Setter Property="Padding" Value="16,7"/>
                <Setter Property="MinWidth" Value="104"/>
                <Setter Property="Height" Value="36"/>
                <Setter Property="Margin" Value="0,0,8,0"/>
                <Setter Property="FontFamily" Value="Segoe UI"/>
                <Setter Property="FontSize" Value="14"/>
                <Setter Property="Template" Value="{StaticResource HoverButton}"/>
                <Setter Property="Cursor" Value="Hand"/>
            </Style>
            <Style x:Key="Primary" TargetType="Button" BasedOn="{StaticResource Base}">
                <Setter Property="Background" Value="#FF314236"/>
                <Setter Property="Foreground" Value="#FFE8E6E1"/>
                <Setter Property="BorderBrush" Value="#FF5C6A56"/>
                <Setter Property="FontWeight" Value="SemiBold"/>
            </Style>
            <Style x:Key="Ghost" TargetType="Button" BasedOn="{StaticResource Base}">
                <Setter Property="Background" Value="#CC16181C"/>
                <Setter Property="Foreground" Value="#FFE8E6E1"/>
                <Setter Property="BorderBrush" Value="#FF4A4E54"/>
            </Style>
            <Style x:Key="Danger" TargetType="Button" BasedOn="{StaticResource Base}">
                <Setter Property="Background" Value="#FF4A2A28"/>
                <Setter Property="Foreground" Value="#FFF3E0DC"/>
                <Setter Property="BorderBrush" Value="#FF6E3C38"/>
            </Style>
            <Style x:Key="Link" TargetType="Button">
                <Setter Property="Foreground" Value="#FF7EA4C4"/>
                <Setter Property="FontSize" Value="13"/>
                <Setter Property="Cursor" Value="Hand"/>
                <Setter Property="HorizontalAlignment" Value="Left"/>
                <Setter Property="Template">
                    <Setter.Value>
                        <ControlTemplate TargetType="Button">
                            <Border Background="Transparent" Padding="0,2">
                                <ContentPresenter TextElement.Foreground="{TemplateBinding Foreground}"/>
                            </Border>
                            <ControlTemplate.Triggers>
                                <Trigger Property="IsMouseOver" Value="True">
                                    <Setter Property="Foreground" Value="#FFE2C27A"/>
                                </Trigger>
                                <Trigger Property="IsEnabled" Value="False">
                                    <Setter Property="Opacity" Value="0.4"/>
                                </Trigger>
                            </ControlTemplate.Triggers>
                        </ControlTemplate>
                    </Setter.Value>
                </Setter>
            </Style>
            <Style TargetType="CheckBox">
                <Setter Property="Cursor" Value="Hand"/>
                <Setter Property="Template">
                    <Setter.Value>
                        <ControlTemplate TargetType="CheckBox">
                            <Border x:Name="Box" Width="18" Height="18" BorderBrush="#FF5C6058" BorderThickness="1" Background="#FF121416">
                                <Path x:Name="Mark" Data="M 3 9 L 7 13 L 15 4" Stroke="#FFE2C27A" StrokeThickness="1.8" Visibility="Collapsed"/>
                            </Border>
                            <ControlTemplate.Triggers>
                                <Trigger Property="IsChecked" Value="True">
                                    <Setter TargetName="Mark" Property="Visibility" Value="Visible"/>
                                    <Setter TargetName="Box" Property="BorderBrush" Value="#FF8C7355"/>
                                </Trigger>
                                <Trigger Property="IsMouseOver" Value="True">
                                    <Setter TargetName="Box" Property="BorderBrush" Value="#FFE2C27A"/>
                                </Trigger>
                                <Trigger Property="IsEnabled" Value="False">
                                    <Setter Property="Opacity" Value="0.45"/>
                                </Trigger>
                            </ControlTemplate.Triggers>
                        </ControlTemplate>
                    </Setter.Value>
                </Setter>
            </Style>
            <Style x:Key="Row" TargetType="Border">
                <Setter Property="Background" Value="Transparent"/>
                <Setter Property="BorderBrush" Value="#FF262A30"/>
                <Setter Property="BorderThickness" Value="0,0,0,1"/>
                <Setter Property="Padding" Value="12,8"/>
                <Style.Triggers>
                    <Trigger Property="IsMouseOver" Value="True">
                        <Setter Property="Background" Value="#14F6EDE0"/>
                    </Trigger>
                </Style.Triggers>
            </Style>
        </ResourceDictionary>
        """;

    // The style keys in Styles.
    private enum Look
    {
        Primary,
        Ghost,
        Danger,
        Link,
        Row
    }

    private static readonly Brush Ivory = BrushOf(0xE8, 0xE6, 0xE1);
    private static readonly Brush Muted = BrushOf(0x8A, 0x92, 0x9A);
    private static readonly Brush Teal = BrushOf(0x7D, 0xAA, 0xA4);
    private static readonly Brush Gold = BrushOf(0xE2, 0xC2, 0x7A);
    private static readonly Brush GoldDark = BrushOf(0x3A, 0x34, 0x20);
    private static readonly Brush Green = BrushOf(0x8F, 0xBF, 0x86);
    private static readonly Brush Red = BrushOf(0xE0, 0x7A, 0x6E);
    private static readonly Brush ReasonRed = BrushOf(0xD9, 0xA0, 0x96);
    private static readonly Brush Ink = BrushOf(0x10, 0x12, 0x14);
    private static readonly Brush Edge = BrushOf(0x3A, 0x3E, 0x44);

    private readonly IReadOnlyList<ModTestCandidate> _mods;
    private readonly RunTests _run;
    private readonly Func<IReadOnlyList<ModTestResult>> _results;
    private readonly Func<string> _export;
    private readonly List<ModRow> _rows = new();
    private readonly List<ModTestResult> _session = new();
    private readonly TextBlock _count = new() { FontSize = 13, Foreground = Muted, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _summary = new() { FontSize = 13, Foreground = Teal, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) };
    private readonly TextBlock _status = new() { FontSize = 13, Foreground = Ivory, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0), Visibility = Visibility.Collapsed };
    private readonly StackPanel _progressPanel = new() { Margin = new Thickness(0, 12, 0, 0), Visibility = Visibility.Collapsed };
    private readonly TextBlock _progressLine = new() { FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = Gold, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _elapsed = new() { FontSize = 14, Foreground = Gold, Tag = UiLanguage.NoTranslate, Margin = new Thickness(12, 0, 0, 0) };
    private readonly ProgressBar _bar = new() { Height = 10, Minimum = 0, Maximum = 1, Margin = new Thickness(0, 6, 0, 0), Foreground = Gold, Background = GoldDark, BorderBrush = Gold, BorderThickness = new Thickness(1) };
    private readonly StackPanel _others = new() { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 4, 0, 0) };
    private readonly Button _othersLink;
    private readonly Button _selectAll;
    private readonly Button _selectNone;
    private readonly Button _start;
    private readonly Button _stop;
    private readonly Button _copy;
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(1) };
    private CancellationTokenSource? _running;
    private DateTime _runStarted;
    private int _runCount;
    private int _runDone;
    private bool _closeWhenDone;

    private sealed class ModRow
    {
        public required ModTestCandidate Mod { get; init; }
        public required CheckBox Box { get; init; }
        public required Border Shell { get; init; }
        public required TextBlock Detail { get; init; }
        public required Border Pill { get; init; }
        public required TextBlock PillText { get; init; }
        public required Ellipse Dot { get; init; }
        public required TextBlock When { get; init; }
        public ModTestRowState State { get; set; }
        public DateTime StartedAt { get; set; }
        public bool Finished { get; set; }
    }

    public ModTestWindow(IReadOnlyList<ModTestCandidate> mods, BeCompatList compat, string saveName,
        RunTests run, Func<IReadOnlyList<ModTestResult>> results, Func<string> export)
    {
        _mods = mods;
        _run = run;
        _results = results;
        _export = export;
        Resources.MergedDictionaries.Add((ResourceDictionary)XamlReader.Parse(Styles));
        Title = "Test mods on the server";
        Width = 940;
        Height = Math.Min(760, SystemParameters.WorkArea.Height - 40);
        MinWidth = 700;
        MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        WindowPlacement.KeepOnScreen(this);
        ShowInTaskbar = false;
        Background = Ink;
        Foreground = Ivory;
        FontFamily = new FontFamily("Segoe UI");

        var top = new StackPanel { Margin = new Thickness(22, 18, 22, 0) };
        top.Children.Add(new TextBlock { Text = "Test mods on the server", FontFamily = new FontFamily("Palatino Linotype"), FontSize = 24, Foreground = BrushOf(0x7E, 0xA4, 0xC4), Margin = new Thickness(0, 0, 0, 6) });
        top.Children.Add(new TextBlock
        {
            Text = "Starts the server once per mod with the Burning Empires core plus that mod, on a copy of " + saveName + " named " + ModLoadTest.TestSaveName(saveName)
                + ". Your saves are not written. A mod passes when the server says serving and fails on a crash, an exit, or after " + (int)ModTestOptions.Default.Total.TotalMinutes + " minutes. Players cannot join while it runs.",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 14,
            Foreground = Teal
        });

        // Select all, Select none and the count above the list.
        var tools = new DockPanel { Margin = new Thickness(0, 16, 0, 6), LastChildFill = false };
        _selectAll = LinkButton("Select all");
        _selectNone = LinkButton("Select none");
        _selectAll.Click += (_, _) => SetAll(true);
        _selectNone.Click += (_, _) => SetAll(false);
        var links = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        links.Children.Add(_selectAll);
        links.Children.Add(new TextBlock { Text = "·", Tag = UiLanguage.NoTranslate, Foreground = Muted, Margin = new Thickness(8, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center });
        links.Children.Add(_selectNone);
        DockPanel.SetDock(links, Dock.Left);
        DockPanel.SetDock(_count, Dock.Right);
        tools.Children.Add(links);
        tools.Children.Add(_count);
        top.Children.Add(tools);

        // The list: a header line and one row per mod, scrolled when long.
        var list = new StackPanel();
        foreach (var mod in mods)
        {
            var row = MakeRow(mod, compat);
            _rows.Add(row);
            list.Children.Add(row.Shell);
        }

        if (mods.Count == 0)
        {
            list.Children.Add(new TextBlock
            {
                Text = "No Workshop mod from the Burning Empires mod list is on this PC that is not tested in Coop yet. Subscribe to one on the Steam Workshop, then open this again.",
                TextWrapping = TextWrapping.Wrap,
                FontSize = 14,
                Margin = new Thickness(12, 12, 12, 12),
                Foreground = BrushOf(0xD2, 0xA3, 0x6A)
            });
        }

        var frame = new DockPanel();
        var header = HeaderRow();
        DockPanel.SetDock(header, Dock.Top);
        frame.Children.Add(header);
        frame.Children.Add(new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        var listBox = new Border { BorderBrush = Edge, BorderThickness = new Thickness(1), Background = BrushOf(0x14, 0x16, 0x1A), Margin = new Thickness(22, 0, 22, 0), Child = frame };

        // Below the list: the summary, earlier results of mods not in the list, the progress and the buttons.
        var bottom = new StackPanel { Margin = new Thickness(22, 0, 22, 16) };
        bottom.Children.Add(_summary);
        _othersLink = LinkButton("");
        _othersLink.Visibility = Visibility.Collapsed;
        _othersLink.Click += (_, _) => _others.Visibility = _others.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        bottom.Children.Add(_othersLink);
        bottom.Children.Add(new ScrollViewer { Content = _others, MaxHeight = 140, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });

        var progressHead = new DockPanel();
        DockPanel.SetDock(_elapsed, Dock.Right);
        progressHead.Children.Add(_elapsed);
        progressHead.Children.Add(_progressLine);
        _progressPanel.Children.Add(progressHead);
        _progressPanel.Children.Add(_bar);
        bottom.Children.Add(_progressPanel);
        bottom.Children.Add(_status);

        var buttons = new DockPanel { Margin = new Thickness(0, 14, 0, 0), LastChildFill = false };
        _start = MakeButton("Start test", Look.Primary, "Tests the ticked mods one at a time.");
        _stop = MakeButton("Stop", Look.Danger, "Ends the test that is running and stops. Results so far are kept.");
        _copy = MakeButton("Copy results into the mod list file", Look.Ghost, "Writes the Burning Empires mod list with the mods that passed marked Loads on the server, for Coodsz to publish.");
        var close = MakeButton("Close", Look.Ghost, null);
        close.Margin = new Thickness(0);
        close.IsCancel = true;
        close.Click += (_, _) => Close();
        _start.Click += async (_, _) => await StartAsync();
        _stop.Click += (_, _) => Stop();
        _copy.Click += (_, _) => ShowStatus(_export());
        foreach (var button in new[] { _start, _stop, _copy })
        {
            DockPanel.SetDock(button, Dock.Left);
            buttons.Children.Add(button);
        }

        DockPanel.SetDock(close, Dock.Right);
        buttons.Children.Add(close);
        bottom.Children.Add(buttons);

        var layout = new DockPanel();
        DockPanel.SetDock(top, Dock.Top);
        DockPanel.SetDock(bottom, Dock.Bottom);
        layout.Children.Add(top);
        layout.Children.Add(bottom);
        layout.Children.Add(listBox);
        Content = layout;

        _clock.Tick += (_, _) => Tick();
        Closing += OnClosing;
        Closed += (_, _) => _clock.Stop();
        SetRunning(false);
        ShowSaved();
        UpdateCount();
    }

    public static void Show(Window owner, IReadOnlyList<ModTestCandidate> mods, BeCompatList compat, string saveName,
        RunTests run, Func<IReadOnlyList<ModTestResult>> results, Func<string> export)
    {
        var window = new ModTestWindow(mods, compat, saveName, run, results, export);
        if (owner.IsLoaded)
            window.Owner = owner;
        window.ShowDialog();
    }

    // ----- Rows -----

    private static Grid RowGrid()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
        return grid;
    }

    private static Border HeaderRow()
    {
        var grid = RowGrid();
        AddHeader(grid, "Mod", 1);
        AddHeader(grid, "Mod list", 2);
        AddHeader(grid, "Last test", 3);
        return new Border { Background = BrushOf(0x1A, 0x1D, 0x22), BorderBrush = Edge, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(12, 6, 12, 6), Child = grid };
    }

    private static void AddHeader(Grid grid, string text, int column)
    {
        var block = new TextBlock { Text = text, FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = Muted };
        Grid.SetColumn(block, column);
        grid.Children.Add(block);
    }

    // The mark from the Burning Empires mod list for a row, the way ModTestView.ListBadge marks TAOM's.
    private static (string Text, CompatEntry Entry)? ListBadge(BeCompatList list, ModTestCandidate mod)
    {
        var entry = list.Find(mod.ModuleId, mod.WorkshopId, mod.Name);
        if (entry == null)
            return null;
        var text = entry.Kind switch
        {
            CompatTag.Risky => "Risky",
            CompatTag.Broken => "Broken",
            _ => BeModList.GroupOf(entry) switch
            {
                CompatGroup.TestedInCoop => "Tested in Coop",
                CompatGroup.LoadsOnServer => "Loads on the server",
                _ => "Works in Burning Empires"
            }
        };
        return (text, entry);
    }

    private ModRow MakeRow(ModTestCandidate mod, BeCompatList compat)
    {
        var grid = RowGrid();
        var broken = BeModTest.KnownBroken(compat, mod);
        var box = new CheckBox { IsChecked = !broken, VerticalAlignment = VerticalAlignment.Center, ToolTip = broken ? "Known broken with Burning Empires, so it is not ticked." : null };
        box.Checked += (_, _) => UpdateCount();
        box.Unchecked += (_, _) => UpdateCount();
        grid.Children.Add(box);

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
        var name = new TextBlock
        {
            Tag = UiLanguage.NoTranslate,
            TextTrimming = TextTrimming.CharacterEllipsis,
            FontSize = 14,
            ToolTip = new TextBlock { Text = mod.Label + (mod.Version.Length > 0 ? " " + mod.Version : "") + " (" + mod.ModuleId + ")", Tag = UiLanguage.NoTranslate }
        };
        name.Inlines.Add(new Run(mod.Label) { FontWeight = FontWeights.SemiBold, Foreground = Ivory });
        if (mod.Version.Length > 0)
            name.Inlines.Add(new Run("  " + mod.Version) { FontSize = 12, Foreground = Muted });
        text.Children.Add(name);
        var detail = new TextBlock { FontSize = 12, Foreground = Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0), Visibility = Visibility.Collapsed };
        text.Children.Add(detail);
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        if (ListBadge(compat, mod) is { } badge)
        {
            var tag = new Border
            {
                Background = new SolidColorBrush(MainWindow.CompatColor(badge.Entry.Kind)),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(7, 2, 7, 2),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = BeCompatList.Hover(badge.Entry, Translator.T),
                Child = new TextBlock { Text = badge.Text, FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = Ink }
            };
            Grid.SetColumn(tag, 2);
            grid.Children.Add(tag);
        }

        var dot = new Ellipse { Width = 7, Height = 7, Fill = Gold, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
        var pillText = new TextBlock { FontSize = 11, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        var pillInside = new StackPanel { Orientation = Orientation.Horizontal };
        pillInside.Children.Add(dot);
        pillInside.Children.Add(pillText);
        var pill = new Border { CornerRadius = new CornerRadius(9), Padding = new Thickness(9, 2, 9, 2), BorderThickness = new Thickness(1), VerticalAlignment = VerticalAlignment.Center, Child = pillInside };
        var when = new TextBlock { FontSize = 12, Foreground = Muted, Tag = UiLanguage.NoTranslate, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        var result = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        result.Children.Add(pill);
        result.Children.Add(when);
        Grid.SetColumn(result, 3);
        grid.Children.Add(result);

        var shell = new Border { Style = (Style)FindResource(nameof(Look.Row)), Child = grid, Cursor = Cursors.Hand };
        // A click anywhere on the row ticks it, like the box itself (the box handles its own click).
        shell.MouseLeftButtonUp += (_, _) =>
        {
            if (_running == null && box.IsEnabled)
                box.IsChecked = box.IsChecked != true;
        };
        return new ModRow { Mod = mod, Box = box, Shell = shell, Detail = detail, Pill = pill, PillText = pillText, Dot = dot, When = when };
    }

    private static void SetState(ModRow row, ModTestRowState state, string detail, string when)
    {
        row.State = state;
        row.PillText.Text = ModTestView.PillText(state);
        var (back, edge, fore) = state switch
        {
            ModTestRowState.Passed => (Green, Green, Ink),
            ModTestRowState.Failed => (Red, Red, Ink),
            ModTestRowState.Testing => (GoldDark, Gold, Gold),
            ModTestRowState.Waiting => (Brushes.Transparent, Edge, Muted),
            _ => (Edge, Edge, BrushOf(0xBE, 0xC6, 0xCD))
        };
        row.Pill.Background = back;
        row.Pill.BorderBrush = edge;
        row.PillText.Foreground = fore;
        row.Dot.Visibility = state == ModTestRowState.Testing ? Visibility.Visible : Visibility.Collapsed;
        // The dot pulses while the mod is tested.
        row.Dot.BeginAnimation(OpacityProperty, state == ModTestRowState.Testing
            ? new DoubleAnimation(1, 0.2, TimeSpan.FromSeconds(0.6)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever }
            : null);
        row.Detail.Text = detail;
        row.Detail.Foreground = state == ModTestRowState.Failed ? ReasonRed : Muted;
        row.Detail.Visibility = detail.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        row.When.Text = when;
    }

    // Every row with its last saved result, and the summary under the list.
    private void ShowSaved()
    {
        var saved = _results().Concat(_session).ToList();
        foreach (var row in _rows)
        {
            var last = ModTestView.LastResult(saved, row.Mod);
            var state = ModTestView.StateOf(last);
            SetState(row, state, state == ModTestRowState.Failed ? last!.Reason : "", last?.Date ?? "");
            row.Pill.ToolTip = last == null || last.Version.Length == 0 ? null : "Tested with version " + last.Version + ".";
            row.Shell.Opacity = 1;
        }

        _summary.Text = ModTestView.Summary(_mods, saved);
        ShowOthers(ModTestView.OtherResults(saved, _mods));
    }

    // Saved results of mods that are not in the list (now tested in Coop, or not on this PC any more).
    private void ShowOthers(IReadOnlyList<ModTestResult> others)
    {
        _others.Children.Clear();
        _othersLink.Visibility = others.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        _othersLink.Content = others.Count == 1 ? "1 earlier result for a mod not in this list" : others.Count + " earlier results for mods not in this list";
        foreach (var result in others)
        {
            var line = new DockPanel { Margin = new Thickness(0, 3, 0, 0) };
            var mark = new TextBlock { Text = result.Passed ? "Passed" : "Failed", FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = result.Passed ? Green : Red, Width = 70 };
            DockPanel.SetDock(mark, Dock.Left);
            line.Children.Add(mark);
            var text = new StackPanel();
            text.Children.Add(new TextBlock
            {
                Text = (result.Name.Length > 0 ? result.Name : result.Id) + (result.Version.Length > 0 ? "  " + result.Version : "") + "  " + result.Date,
                Tag = UiLanguage.NoTranslate,
                FontSize = 12,
                Foreground = Ivory
            });
            if (!result.Passed && result.Reason.Length > 0)
                text.Children.Add(new TextBlock { Text = result.Reason, FontSize = 12, Foreground = ReasonRed, TextWrapping = TextWrapping.Wrap });
            line.Children.Add(text);
            _others.Children.Add(line);
        }
    }

    private void SetAll(bool ticked)
    {
        foreach (var row in _rows)
            row.Box.IsChecked = ticked;
    }

    private void UpdateCount() => _count.Text = ModTestView.SelectedLine(_rows.Count(row => row.Box.IsChecked == true), _rows.Count);

    private ModRow? RowOf(string moduleId) =>
        _rows.FirstOrDefault(row => row.Mod.ModuleId.Equals(moduleId, StringComparison.OrdinalIgnoreCase));

    // ----- The run -----

    private async Task StartAsync()
    {
        var picked = _rows.Where(row => row.Box.IsChecked == true).ToList();
        if (picked.Count == 0)
        {
            ShowStatus("Tick at least one mod first.");
            return;
        }

        _running = new CancellationTokenSource();
        SetRunning(true);
        foreach (var row in _rows)
        {
            row.Finished = false;
            if (picked.Contains(row))
                SetState(row, ModTestRowState.Waiting, "", "");
            else
                row.Shell.Opacity = 0.5;
        }

        _runStarted = DateTime.Now;
        _runCount = picked.Count;
        _runDone = 0;
        _bar.Value = 0;
        _progressLine.Text = "Starting the test.";
        _elapsed.Text = ModTestView.Clock(TimeSpan.Zero);
        _progressPanel.Visibility = Visibility.Visible;
        ShowStatus("");
        _clock.Start();
        var progress = new Progress<ModTestProgress>(OnProgress);
        string summary;
        try
        {
            summary = await _run(picked.Select(row => row.Mod).ToList(), progress, result => Dispatcher.Invoke(() => OnRecorded(result)), _running.Token);
        }
        catch (Exception ex)
        {
            summary = ex.Message;
        }
        finally
        {
            _clock.Stop();
            _running.Dispose();
            _running = null;
            SetRunning(false);
            _progressPanel.Visibility = Visibility.Collapsed;
        }

        ShowSaved();
        ShowStatus(summary);
        if (_closeWhenDone)
            Close();
    }

    private void OnProgress(ModTestProgress step)
    {
        if (_running == null)
            return;
        _progressLine.Text = ModTestView.ProgressLine(step);
        // A late line of a mod that already has its result does not turn it back to Testing.
        if (RowOf(step.Mod.ModuleId) is not { Finished: false } row)
            return;
        if (row.State != ModTestRowState.Testing)
        {
            row.StartedAt = DateTime.Now;
            row.Shell.BringIntoView();
        }

        SetState(row, ModTestRowState.Testing, step.Status, ModTestView.Clock(DateTime.Now - row.StartedAt));
    }

    private void OnRecorded(ModTestResult result)
    {
        _session.Add(result);
        _runDone++;
        _bar.Value = Math.Min(1, _runDone / (double)Math.Max(1, _runCount));
        if (RowOf(result.Id) is not { } row)
            return;
        row.Finished = true;
        SetState(row, result.Passed ? ModTestRowState.Passed : ModTestRowState.Failed, result.Passed ? "" : result.Reason, ModTestView.Clock(TimeSpan.FromSeconds(result.Seconds)));
    }

    private void Tick()
    {
        var now = DateTime.Now;
        _elapsed.Text = ModTestView.Clock(now - _runStarted);
        foreach (var row in _rows.Where(row => row.State == ModTestRowState.Testing && !row.Finished))
            row.When.Text = ModTestView.Clock(now - row.StartedAt);
    }

    private void SetRunning(bool running)
    {
        var idle = !running && _mods.Count > 0;
        _start.IsEnabled = idle;
        _copy.IsEnabled = !running;
        _stop.IsEnabled = running;
        _selectAll.IsEnabled = idle;
        _selectNone.IsEnabled = idle;
        // While a test runs the rows and the progress bar show where it is; the saved summary comes back after.
        _summary.Visibility = running ? Visibility.Collapsed : Visibility.Visible;
        if (running)
        {
            _othersLink.Visibility = Visibility.Collapsed;
            _others.Visibility = Visibility.Collapsed;
        }

        foreach (var row in _rows)
        {
            row.Box.IsEnabled = !running;
            row.Shell.Cursor = running ? Cursors.Arrow : Cursors.Hand;
        }
    }

    private void Stop()
    {
        if (_running == null)
            return;
        _stop.IsEnabled = false;
        ShowStatus("Stopping the test server. This can take a minute.");
        _running.Cancel();
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_running == null)
            return;
        // The running test is stopped first, so no test server is left behind.
        e.Cancel = true;
        _closeWhenDone = true;
        Stop();
    }

    private void ShowStatus(string line)
    {
        _status.Text = line;
        _status.Visibility = string.IsNullOrWhiteSpace(line) ? Visibility.Collapsed : Visibility.Visible;
    }

    private Button MakeButton(string text, Look style, string? tip) => new()
    {
        Content = text,
        ToolTip = tip,
        Style = (Style)FindResource(style.ToString())
    };

    private Button LinkButton(string text) => new() { Content = text, Style = (Style)FindResource(nameof(Look.Link)) };

    private static SolidColorBrush BrushOf(byte r, byte g, byte b) => new(Color.FromRgb(r, g, b));
}
