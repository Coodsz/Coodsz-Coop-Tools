using System.Windows;
using System.Windows.Controls;
using BannerlordCoopLauncher.Core.Keep;
using CoodszHost.Services;

namespace CoodszHost;

// The filter chips on the Modules page (Coodsz Host only), after CoopKeep's Mods page: All, Enabled,
// Turned off and the community list results the badges already show. A chip only hides rows on
// screen; the server list, the order and Save module config are not changed.
public partial class MainWindow
{
    private ModFilterKind _modFilter = ModFilterKind.All;
    private readonly Dictionary<ModFilterKind, (RadioButton Chip, TextBlock Count)> _modChips = new();
    private string _modCountsShown = "";

    /// <summary>Called once from StartKeepPages.</summary>
    private void StartModFilter()
    {
        foreach (var kind in ModFilter.Chips)
        {
            var count = new TextBlock { Margin = new Thickness(6, 0, 0, 0), Opacity = 0.7, Tag = "coodsz:no-translate" };
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            content.Children.Add(new TextBlock { Text = ModFilter.Title(kind) });
            content.Children.Add(count);
            var chip = new RadioButton
            {
                Style = (Style)FindResource("Keep.Pill"),
                GroupName = "ModFilter",
                ToolTip = "Filters the module list below to this kind. The number is how many modules match.",
                Content = content,
                Margin = new Thickness(0, 0, 6, 6),
                IsChecked = kind == _modFilter,
            };
            chip.Checked += (_, _) =>
            {
                _modFilter = kind;
                RefreshModuleView(this, new RoutedEventArgs());
            };
            _modChips[kind] = (chip, count);
            ModFilterChips.Children.Add(chip);
        }

        ModFilterChips.Visibility = Visibility.Visible;
        UpdateModFilterCounts();
    }

    /// <summary>Whether a row shows with the Enabled only button and the chip together.</summary>
    private bool ModuleRowShown(ModuleRow row) =>
        (!_enabledOnly || row.Enabled) && ModFilter.Matches(_modFilter, row.Enabled, row.Compat?.Kind);

    /// <summary>The numbers on the chips; from the one second clock while Modules is open.</summary>
    private void UpdateModFilterCounts()
    {
        if (_modChips.Count == 0)
            return;
        var counts = ModFilter.Counts(_modules.Select(row => (row.Enabled, row.Compat?.Kind)));
        var shown = string.Join(",", counts.Values);
        if (shown == _modCountsShown)
            return;
        var first = _modCountsShown.Length == 0;
        _modCountsShown = shown;
        foreach (var (kind, (_, count)) in _modChips)
            count.Text = counts[kind].ToString();
        // A row turned on or off, or a badge that arrived, can move it in or out of the chip.
        if (!first && _modFilter != ModFilterKind.All && _dragRow == null)
            System.Windows.Data.CollectionViewSource.GetDefaultView(_modules).Refresh();
    }

    /// <summary>The Keep pages that follow the one second clock.</summary>
    private void RefreshKeepPages()
    {
        RefreshDashboard();
        if (_page == "Modules")
            UpdateModFilterCounts();
        if (_page == "Encyclopedia")
            KeepEncyclopedia.Shown();
        // The map reads where the parties are every few seconds, only while it is open.
        if (_page == "Map")
            KeepMap.Shown();
    }
}
