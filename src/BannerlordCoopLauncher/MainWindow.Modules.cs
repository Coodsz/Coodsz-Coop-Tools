using System.Collections.ObjectModel;
using System.Text;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using BannerlordCoopLauncher.Core.IO;
using BannerlordCoopLauncher.Core.Updates;
using BannerlordCoopLauncher.Core.Launch;
using BannerlordCoopLauncher.Core.Models;
using BannerlordCoopLauncher.Core.Modules;
using BannerlordCoopLauncher.Core.Nexus;
using BannerlordCoopLauncher.Core.Network;
using BannerlordCoopLauncher.Core.Parsing;
using BannerlordCoopLauncher.Core.Profile;
using BannerlordCoopLauncher.Core.Steam;
using BannerlordCoopLauncher.Core.Support;
using Microsoft.Win32;

namespace BannerlordCoopLauncher;

public partial class MainWindow
{
    private void ModuleDragStart(object sender, MouseButtonEventArgs e)
    {
        if (_settings.Current().Locked)
        {
            ModuleSaveHint.Text = "This setup is locked. The load order cannot be changed.";
            ModuleSaveHint.Visibility = Visibility.Visible;
            return;
        }

        if (IsInsideCheckBox(e.OriginalSource as DependencyObject) || IsInsideButton(e.OriginalSource as DependencyObject))
            return;
        _dragRow = RowUnder(ModuleList, e.GetPosition(ModuleList));
        _dragMoved = false;
        _dragBlocked = false;
        _dragStart = e.GetPosition(ModuleList);
        if (_dragRow == null)
            return;
        if (!_settings.Current().Locked && DefaultClientSet.IsFixed(_dragRow.ModuleId))
        {
            _dragRow = null;
            ModuleSaveHint.Text = "This mod stays in this spot.";
            ModuleSaveHint.Visibility = Visibility.Visible;
            return;
        }
        _dragRow.IsDragging = true;
        ModuleList.CaptureMouse();
        e.Handled = true;
    }

    private void ModuleDragMove(object sender, MouseEventArgs e)
    {
        if (_dragRow == null || !ModuleList.IsMouseCaptured)
            return;
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            FinishModuleDrag();
            return;
        }

        var position = e.GetPosition(ModuleList);
        if (!_dragMoved && Math.Abs(position.Y - _dragStart.Y) < 6 && Math.Abs(position.X - _dragStart.X) < 6)
            return;
        _dragMoved = true;
        MoveDraggedRow(position);
        // The list scrolls inside the Mods page, so a drag near its top or bottom edge scrolls it.
        var onPage = ModuleList.TranslatePoint(position, ModsListScroll);
        if (onPage.Y < 48)
            ModsListScroll.ScrollToVerticalOffset(ModsListScroll.VerticalOffset - 28);
        else if (onPage.Y > ModsListScroll.ViewportHeight - 48)
            ModsListScroll.ScrollToVerticalOffset(ModsListScroll.VerticalOffset + 28);
        e.Handled = true;
    }

    private void ModuleDragUp(object sender, EventArgs e) => FinishModuleDrag();

    private void FinishModuleDrag()
    {
        if (_dragRow != null)
            _dragRow.IsDragging = false;
        if (ModuleList.IsMouseCaptured)
            ModuleList.ReleaseMouseCapture();
        if (_dragRow == null)
            return;
        var moved = _dragMoved;
        var blocked = _dragBlocked;
        _dragRow = null;
        _dragMoved = false;
        _dragBlocked = false;
        if (blocked)
        {
            ModuleSaveHint.Text = "That mod stays in the working load order. Moving it, or putting another mod between those mods, can break the game. Extra mods can move below that list.";
            ModuleSaveHint.Visibility = Visibility.Visible;
            return;
        }
        if (!moved || _profile == null || _settings.Current().Locked)
            return;
        SaveRowOrder();
    }

    // After a drag, Move up or Move down: the order of the rows is this setup's load order now.
    private void SaveRowOrder()
    {
        if (_profile == null)
            return;
        var setup = _settings.Current();
        setup.ModuleOrder = _rows.Select(row => row.ModuleId).ToList();
        ClientLoadOrder.Apply(_profile, setup.ModuleOrder);
        ApplyModuleToggles(_profile, setup.DisabledModules);
        SaveSettingsFromForm();
        if (PendingMods(setup).Count > 0)
            ShowPendingSave();
        else
        {
            ModuleSaveHint.Text = "Saved for this setup. Connect uses this load order.";
            ModuleSaveHint.Visibility = Visibility.Visible;
        }
    }

    private void MoveDraggedRow(Point position)
    {
        if (_dragRow == null)
            return;
        var target = RowUnder(ModuleList, position);
        if (target == null || ReferenceEquals(target, _dragRow))
            return;
        var from = _rows.IndexOf(_dragRow);
        var to = _rows.IndexOf(target);
        if (from < 0 || to < 0 || from == to)
            return;
        if (ModuleList.ItemContainerGenerator.ContainerFromItem(target) is not FrameworkElement container)
            return;
        var top = container.TranslatePoint(new Point(0, 0), ModuleList).Y;
        var mid = top + container.ActualHeight / 2;
        if (to > from && position.Y < mid)
            return;
        if (to < from && position.Y > mid)
            return;
        var dragOrder = _settings.Current().Locked
            ? LockedWorkingOrder()
            : DefaultClientSet.Fixed;
        if (!WorkingLoadOrder.CanMove(_rows.Select(row => row.ModuleId).ToList(), from, to, dragOrder))
        {
            _dragBlocked = true;
            return;
        }
        _rows.Move(from, to);
        for (var i = 0; i < _rows.Count; i++)
            _rows[i].Order = i + 1;
    }

    private static ModuleRow? RowUnder(ItemsControl list, Point position)
    {
        var node = list.InputHitTest(position) as DependencyObject;
        while (node != null && !ReferenceEquals(node, list))
        {
            if (node is FrameworkElement element && element.DataContext is ModuleRow row)
                return row;
            node = ParentOf(node);
        }

        return null;
    }

    private static bool IsInsideButton(DependencyObject? node)
    {
        while (node != null)
        {
            if (node is Button)
                return true;
            node = ParentOf(node);
        }

        return false;
    }

    private async void RemoveMod_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string moduleId)
            return;

        var setup = _settings.Current();
        if (setup.Locked || !CanRemoveModule(moduleId))
        {
            Log("This mod stays in the working list.");
            return;
        }

        var name = _rows.FirstOrDefault(row => row.ModuleId.Equals(moduleId, StringComparison.OrdinalIgnoreCase))?.Name ?? moduleId;
        setup.CustomMods.RemoveAll(mod => mod.ModuleId.Equals(moduleId, StringComparison.OrdinalIgnoreCase));
        setup.ModuleOrder.RemoveAll(id => id.Equals(moduleId, StringComparison.OrdinalIgnoreCase));
        setup.DisabledModules.RemoveAll(id => id.Equals(moduleId, StringComparison.OrdinalIgnoreCase));
        PendingMods(setup).Remove(moduleId);
        if (!setup.RemovedModules.Contains(moduleId, StringComparer.OrdinalIgnoreCase))
            setup.RemovedModules.Add(moduleId);
        await RefreshAsync();
        ShowPendingSave();
        Log(name + " was taken off this list. Make sure to press Save mods.");
    }

    private bool CanRemoveModule(string moduleId)
    {
        var setup = _settings.Current();
        return ModListRemoval.CanRemove(moduleId, setup.Locked, defaultSetup: !setup.Locked, IncludeMoreNations);
    }

    private static bool IsInsideCheckBox(DependencyObject? node)
    {
        while (node != null)
        {
            if (node is CheckBox)
                return true;
            node = ParentOf(node);
        }

        return false;
    }

    private async void AddMods_Click(object sender, RoutedEventArgs e)
    {
        var setup = _settings.Current();
        if (setup.Locked)
        {
            ModuleSaveHint.Text = "This setup is locked. The working mods cannot be changed.";
            ModuleSaveHint.Visibility = Visibility.Visible;
            NoteAction("Add mods", "Refused. " + setup.Name + " is locked, so the mod list cannot be changed.");
            return;
        }

        Log("Add mods is opening the list of mods already on this PC. It does not download anything.");
        NoteAction("Add mods", "Opened the list of mods already on this PC.");
        // The bundled mods are written out first, so a copy an older version left beside the launcher is listed once.
        await ToolPayload.FolderAsync(ToolPayload.BundledMods);
        IReadOnlyList<FoundMod> picked;
        try
        {
            picked = CustomModWindow.Choose(this, () =>
            {
                var gameRoot = _gameRoot ?? GameRoot.Normalize(GamePathBox.Text, _files);
                var modules = gameRoot == null ? null : Path.Combine(gameRoot, "Modules");
                return CustomModCatalog.Scan(AppContext.BaseDirectory, ReadWorkshops(), modules, ToolPayload.Peek(ToolPayload.BundledMods))
                    .Where(mod => setup.CustomMods.All(existing => !existing.ModuleId.Equals(mod.ModuleId, StringComparison.OrdinalIgnoreCase)))
                    .Where(mod => _rows.All(row => !row.ModuleId.Equals(mod.ModuleId, StringComparison.OrdinalIgnoreCase)))
                    .ToList();
            }, PickerBadge);
        }
        catch (Exception ex)
        {
            Log("Add mods could not open the list. " + ex.Message);
            NoteAction("Add mods", "Could not open the list. " + ex.Message);
            return;
        }
        if (picked.Count == 0)
        {
            NoteAction("Add mods", "Closed without adding a mod.");
            return;
        }

        var pending = PendingMods(setup);
        foreach (var mod in picked)
        {
            setup.CustomMods.Add(new CustomMod { Name = mod.Name, ModuleId = mod.ModuleId, WorkshopId = mod.WorkshopId });
            setup.RemovedModules.RemoveAll(id => id.Equals(mod.ModuleId, StringComparison.OrdinalIgnoreCase));
            pending.Add(mod.ModuleId);
            var gameRoot = _gameRoot ?? GameRoot.Normalize(GamePathBox.Text, _files);
            if (mod.WorkshopId == "0" && gameRoot != null)
                ModuleZipInstaller.InstallDirectory(mod.Directory, gameRoot, mod.ModuleId);
        }

        await RefreshAsync();
        ReportExtraDependencies();
        ShowPendingSave();
        Log("Added " + string.Join(", ", picked.Select(mod => mod.Name)) + " to " + setup.Name + ". Make sure to press Save mods. The server must have the same mods or the join fails.");
        NoteAction("Add mods", "Added " + string.Join(", ", picked.Select(mod => mod.Name)) + " to " + setup.Name + ". Save has not been pressed yet.");
    }

    private void AutoOrder_Click(object sender, RoutedEventArgs e)
    {
        var setup = _settings.Current();
        if (setup.Locked)
        {
            Log("This setup is locked. Auto dependency order stays off.");
            return;
        }

        if (_resolution == null || _rows.Count == 0 || !TryReadModuleFacts(out var dependencies, out var names, out var enabled))
        {
            Log("The mod list is not ready. Click Check mods, then press Auto dependency order.");
            return;
        }

        var working = PinnedOrder();
        var current = _rows.Select(row => row.ModuleId).ToList();
        var sorted = ExtraDependencyOrder.Sort(current, working, dependencies);
        if (sorted.SequenceEqual(current, StringComparer.OrdinalIgnoreCase))
            Log(AlreadyInOrderMessage(current, working));
        else
        {
            setup.ModuleOrder = sorted;
            if (_profile != null)
                _profile.ModuleLoadOrder = sorted;
            BindModules(_resolution);
            ShowPendingSave();
            Log("Extra mods were sorted so each one loads after the mods it needs. Bannerlord Coop and the working list stayed put. Press Save mods, then Test to main menu.");
        }

        foreach (var line in ExtraDependencyOrder.Problems(sorted, working, dependencies, names, enabled))
            Log(line);
    }

    private static string AlreadyInOrderMessage(IReadOnlyList<string> current, IReadOnlyList<string> working)
    {
        var extras = current.Count(id => !working.Contains(id, StringComparer.OrdinalIgnoreCase));
        return extras == 0
            ? "Checked this list. There are no extra mods to sort. Bannerlord Coop and the working list stayed put."
            : "Checked " + extras + " extra mods. None of them needs another extra mod, so nothing moved. A mod on this list still loads. Bannerlord Coop and the working list stayed put.";
    }

    private void LogExtraOrderCheck()
    {
        if (_settings.Current().Locked || _resolution == null || _rows.Count == 0 || !TryReadModuleFacts(out var dependencies, out var names, out var enabled))
            return;

        var working = PinnedOrder();
        var current = _rows.Select(row => row.ModuleId).ToList();
        var sorted = ExtraDependencyOrder.Sort(current, working, dependencies);
        if (!sorted.SequenceEqual(current, StringComparer.OrdinalIgnoreCase))
            Log("Press Auto dependency order before the next test. An extra mod needs another extra mod that loads later. Bannerlord Coop and the working list stay put.");
        else
            Log(AlreadyInOrderMessage(current, working));

        foreach (var line in ExtraDependencyOrder.Problems(sorted, working, dependencies, names, enabled))
            Log(line);
    }

    private void LogLoadedExtras(string gameRoot, IReadOnlyList<StartupCrashExplain.ModName> extras)
    {
        var path = Path.Combine(gameRoot, "bin", "Win64_Shipping_Client", "Coop_client.log");
        if (!File.Exists(path))
            return;
        string text;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            text = reader.ReadToEnd();
        }
        catch (IOException)
        {
            return;
        }

        var loaded = new HashSet<string>(StartupCrashExplain.LoadedModuleIds(text), StringComparer.OrdinalIgnoreCase);
        var names = extras
            .Where(mod => loaded.Contains(mod.Id))
            .Select(mod => string.IsNullOrWhiteSpace(mod.Name) ? mod.Id : mod.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (names.Count == 0)
            return;
        LogLater("These extra mods loaded: " + string.Join(", ", names) + ".");
    }

    private IReadOnlyList<string> PinnedOrder()
    {
        var setup = _settings.Current();
        var working = (setup.Locked
            ? LockedWorkingOrder()
            : (IReadOnlyList<string>)DefaultClientSet.Fixed).ToList();
        var shownCoop = _rows.Select(row => row.ModuleId).FirstOrDefault(BannerlordCoopModules.IsEither);
        if (shownCoop == null)
            return working;
        for (var i = 0; i < working.Count; i++)
        {
            if (BannerlordCoopModules.IsEither(working[i]))
                working[i] = shownCoop;
        }

        return working;
    }

    private bool TryReadModuleFacts(
        out Dictionary<string, IReadOnlyList<string>> dependencies,
        out Dictionary<string, string> names,
        out HashSet<string> enabled)
    {
        dependencies = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        enabled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (_resolution == null)
            return false;

        foreach (var module in _resolution.BaseModules.Concat(_resolution.Modules))
        {
            names[module.ModuleId] = string.IsNullOrWhiteSpace(module.Name) ? module.ModuleId : module.Name;
            if (module.Enabled)
                enabled.Add(module.ModuleId);
            dependencies[module.ModuleId] = ReadDependencies(module.Directory);
        }

        return true;
    }

    private void ReportExtraDependencies()
    {
        if (_settings.Current().Locked || _resolution == null || _rows.Count == 0)
            return;

        var dependencies = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var enabled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var module in _resolution.BaseModules.Concat(_resolution.Modules))
        {
            names[module.ModuleId] = string.IsNullOrWhiteSpace(module.Name) ? module.ModuleId : module.Name;
            if (module.Enabled)
                enabled.Add(module.ModuleId);
            dependencies[module.ModuleId] = ReadDependencies(module.Directory);
        }

        var working = PinnedOrder();
        var current = _rows.Select(row => row.ModuleId).ToList();
        var sorted = ExtraDependencyOrder.Sort(current, working, dependencies);
        if (!sorted.SequenceEqual(current, StringComparer.OrdinalIgnoreCase))
        {
            var setup = _settings.Current();
            setup.ModuleOrder = sorted;
            if (_profile != null)
                _profile.ModuleLoadOrder = sorted;
            BindModules(_resolution);
            Log("Extra mods were sorted so each one loads after the mods it needs. Bannerlord Coop and the working list were not moved.");
        }
        else if (current.Any(id => !working.Contains(id, StringComparer.OrdinalIgnoreCase)))
            Log("Checked the new mods. Each one loads after the mods it needs. Bannerlord Coop was not moved.");

        foreach (var line in ExtraDependencyOrder.Problems(sorted, working, dependencies, names, enabled))
            Log(line);
    }

    private IReadOnlyList<string> ReadDependencies(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
            return Array.Empty<string>();
        var xml = _files.ReadAllText(Path.Combine(directory, "SubModule.xml"));
        return xml == null ? Array.Empty<string>() : SubModuleXml.ReadDependencies(xml);
    }

    private void SaveMods_Click(object sender, RoutedEventArgs e)
    {
        var setup = _settings.Current();
        if (setup.Locked)
        {
            ModuleSaveHint.Text = "This setup is locked. The working mods cannot be changed.";
            ModuleSaveHint.Visibility = Visibility.Visible;
            NoteAction("Save", "Refused. " + setup.Name + " is locked.");
            return;
        }

        var changed = HasUnsavedModChanges() || PendingMods(setup).Count > 0;
        PendingMods(setup).Clear();
        SaveSettingsFromForm();
        RememberSavedMods();
        if (_resolution != null)
            BindModules(_resolution);
        if (changed)
        {
            ModuleSaveHint.Text = "Saved. Press Test to main menu before you connect. It starts the game at the main menu and does not join the server.";
            ModuleSaveHint.Visibility = Visibility.Visible;
            DiagnosticButton.BorderBrush = new SolidColorBrush(Color.FromRgb(226, 194, 122));
            DiagnosticButton.BorderThickness = new Thickness(2);
            Log("Saved. Press Test to main menu before you connect.");
            NoteAction("Save", "Saved " + setup.Name + ". The highlighted adds were kept.");
        }
        else
        {
            ModuleSaveHint.Text = "Saved for this setup. Connect uses these mods.";
            ModuleSaveHint.Visibility = Visibility.Visible;
            NoteAction("Save", "Saved " + setup.Name + ". The mod list was already current.");
        }
        SaveModsButton.BorderBrush = new SolidColorBrush(Color.FromRgb(142, 180, 212));
        SaveModsButton.BorderThickness = new Thickness(1);
        StopSavePulse();
    }

    private void ShowPendingSave()
    {
        ModuleSaveHint.Text = "Make sure to press Save mods. This change is not kept until you do.";
        ModuleSaveHint.Visibility = Visibility.Visible;
        PulseSaveButton();
    }

    private void PulseSaveButton()
    {
        // Save mods shows only while a change waits to be saved; it is also in the More menu.
        SaveModsButton.Visibility = Visibility.Visible;
        SaveModsButton.Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x7A, 0xD4));
        SaveModsButton.BorderBrush = new SolidColorBrush(Color.FromRgb(0x8E, 0xB4, 0xD4));
        SaveModsButton.BorderThickness = new Thickness(2);
        if (_savePulse != null)
            return;

        var animation = new DoubleAnimation(1, 0.45, TimeSpan.FromMilliseconds(700))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever
        };
        Storyboard.SetTarget(animation, SaveModsButton);
        Storyboard.SetTargetProperty(animation, new PropertyPath(UIElement.OpacityProperty));
        _savePulse = new Storyboard();
        _savePulse.Children.Add(animation);
        _savePulse.Begin(SaveModsButton, true);
    }

    private void StopSavePulse()
    {
        SaveModsButton.Visibility = Visibility.Collapsed;
        if (_savePulse == null)
            return;
        _savePulse.Stop(SaveModsButton);
        _savePulse = null;
        SaveModsButton.BeginAnimation(UIElement.OpacityProperty, null);
        SaveModsButton.Opacity = 1;
        SaveModsButton.ClearValue(Control.BackgroundProperty);
        SaveModsButton.ClearValue(Control.BorderBrushProperty);
        SaveModsButton.ClearValue(Control.BorderThicknessProperty);
    }

    private HashSet<string> PendingMods(ClientSetup setup)
    {
        if (!_pendingMods.TryGetValue(setup.Name, out var pending))
        {
            pending = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _pendingMods[setup.Name] = pending;
        }

        return pending;
    }

    private void PageScroll_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source && IsDescendantOf(LogBox, source))
            return;
        if (PageScroll.ScrollableHeight <= 0)
            return;
        var lines = SystemParameters.WheelScrollLines;
        if (lines == 0)
            return;
        if (lines < 0)
            lines = 3;
        PageScroll.ScrollToVerticalOffset(PageScroll.VerticalOffset - e.Delta / 120.0 * lines * 24);
        e.Handled = true;
    }

    private static bool IsDescendantOf(DependencyObject parent, DependencyObject? node)
    {
        while (node != null)
        {
            if (ReferenceEquals(node, parent))
                return true;
            node = ParentOf(node);
        }

        return false;
    }

    private static DependencyObject? ParentOf(DependencyObject node)
    {
        if (node is Visual || node is System.Windows.Media.Media3D.Visual3D)
            return VisualTreeHelper.GetParent(node);
        return LogicalTreeHelper.GetParent(node);
    }

    private void ModuleToggle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox box || box.Tag is not string moduleId || _profile == null)
            return;
        if (!CanToggleModule(moduleId))
        {
            box.IsChecked = true;
            ModuleSaveHint.Text = _settings.Current().Locked
                ? "This setup is locked. The working mods cannot be changed."
                : "This mod stays on, in this spot.";
            ModuleSaveHint.Visibility = Visibility.Visible;
            return;
        }

        var setup = _settings.Current();
        var disabled = setup.DisabledModules;
        disabled.RemoveAll(id => id.Equals(moduleId, StringComparison.OrdinalIgnoreCase));
        if (box.IsChecked != true)
            disabled.Add(moduleId);
        var name = _rows.FirstOrDefault(row => row.ModuleId.Equals(moduleId, StringComparison.OrdinalIgnoreCase))?.Name ?? moduleId;
        ApplyModuleToggles(_profile, disabled);
        var workshops = ReadWorkshops();
        RestoreRetiredCopies(workshops);
        _resolution = ModuleResolver.Resolve(_profile, _gameRoot, workshops, null, _files);
        BindModules(_resolution);
        SaveSettingsFromForm();
        RememberSavedSwitches();
        if (PendingMods(setup).Count > 0 || HasUnsavedModChanges())
            ShowPendingSave();
        else if (Coodsz.Ui.UiLanguage.English(ModuleSaveHint).StartsWith("Make sure to press Save", StringComparison.Ordinal))
        {
            ModuleSaveHint.Visibility = Visibility.Collapsed;
            StopSavePulse();
        }
        Log(box.IsChecked == true
            ? name + " is on. Saved. Connect uses it."
            : name + " is off. Saved. Connect skips it.");
        // Refreshes the buttons; a toggle during an install keeps them waiting until RunBusyAsync is done.
        SetBusy(_busy);
    }
}
