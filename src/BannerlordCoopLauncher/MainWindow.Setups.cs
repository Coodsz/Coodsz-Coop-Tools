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
    private async void MapBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_applyingMap || !IsLoaded)
            return;
        ShowMoreNationsNote();
        await RefreshAsync();
    }

    private void SaveSettingsFromForm()
    {
        var setup = _settings.Setups.FirstOrDefault(item => item.Name.Equals(_shownSetup, StringComparison.OrdinalIgnoreCase))
            ?? _settings.Current();
        setup.CampaignMap = setup.Locked
            ? StableClientSetup.MapFor(setup.Name)
            : IncludeMoreNations ? "MoreNations" : "Vanilla";
        setup.UseDirectConnection = UseDirect;
        setup.SteamHostName = SteamNameBox.Text.Trim();
        setup.DirectHost = DirectHostBox.Text.Trim();
        setup.DirectPort = DirectPortBox.Text.Trim();
        setup.ServerPassword = CurrentPassword();
        _settings.ActiveSetup = setup.Name;
        // Only a folder the player picked is remembered; a Steam found one is looked up again next time.
        _settings.GamePath = _gamePicked ? GamePathBox.Text.Trim() : "";
        _settings.SteamHostName = setup.SteamHostName;
        _settings.UseDirectConnection = setup.UseDirectConnection;
        _settings.DirectHost = setup.DirectHost;
        _settings.DirectPort = setup.DirectPort;
        _settings.ServerPassword = setup.ServerPassword;
        _settings.CampaignMap = setup.CampaignMap;
        _settings.DisabledModules = setup.DisabledModules;
        _settings.NexusApiKey = NexusKeyBox.Password.Trim();
        _settings.ProfileUrl = string.IsNullOrWhiteSpace(ProfileUrlBox.Text)
            ? ProfileJson.DefaultProfileUrl
            : ProfileUrlBox.Text.Trim();
        _settings.Save(SettingsPath);
    }

    private void FillSetupList()
    {
        _applyingSetup = true;
        SetupBox.Items.Clear();
        foreach (var setup in _settings.Setups)
        {
            var item = new ComboBoxItem { Content = setup.Name, Tag = setup.Name };
            SetupBox.Items.Add(item);
            if (setup.Name.Equals(_settings.ActiveSetup, StringComparison.OrdinalIgnoreCase))
                SetupBox.SelectedItem = item;
        }
        if (SetupBox.SelectedItem == null && SetupBox.Items.Count > 0)
            SetupBox.SelectedIndex = 0;
        _applyingSetup = false;
    }

    private void ApplySetupToForm(ClientSetup setup)
    {
        _shownSetup = setup.Name;
        _settings.ActiveSetup = setup.Name;
        _settings.DisabledModules = setup.DisabledModules;
        _applyingSetup = true;
        SetupNameBox.Text = setup.Name;
        SetupNameBox.IsReadOnly = setup.Locked;
        MapBox.IsEnabled = !setup.Locked;
        CopySetupButton.IsEnabled = !setup.Locked;
        RenameSetupButton.IsEnabled = !setup.Locked;
        DeleteSetupButton.IsEnabled = !setup.Locked;
        AddModsButton.IsEnabled = !setup.Locked;
        SaveModsButton.IsEnabled = !setup.Locked;
        SaveModsMenuItem.IsEnabled = !setup.Locked;
        AutoOrderButton.IsEnabled = !setup.Locked;
        LockedSetupTag.Visibility = setup.Locked ? Visibility.Visible : Visibility.Collapsed;
        LockedSetupText.Visibility = setup.Locked ? Visibility.Visible : Visibility.Collapsed;
        LockedSetupText.Text = LockedSetupNote.Line(setup.Name);
        foreach (var button in new Control[] { CopySetupButton, RenameSetupButton, DeleteSetupButton, AddModsButton, SaveModsButton, AutoOrderButton })
            ShowLockedTip(button, setup.Locked);
        if (setup.Locked)
        {
            ModuleSaveHint.Text = "This setup is locked. The working mods cannot be changed.";
            ModuleSaveHint.Visibility = Visibility.Visible;
            StopSavePulse();
        }
        else if (PendingMods(setup).Count > 0)
            ShowPendingSave();
        else
        {
            ModuleSaveHint.Visibility = Visibility.Collapsed;
            StopSavePulse();
        }
        SteamNameBox.Text = setup.SteamHostName;
        DirectHostBox.Text = setup.DirectHost;
        DirectPortBox.Text = setup.DirectPort;
        ServerPasswordBox.Password = setup.ServerPassword;
        if (_passwordVisible)
            ServerPasswordVisibleBox.Text = setup.ServerPassword;
        SetJoinMode(setup.UseDirectConnection);
        SelectCampaignMap(setup.CampaignMap);
        ShowMoreNationsNote();
        _applyingSetup = false;
    }

    private bool ConfirmLeaveSetup()
    {
        if (!HasUnsavedModChanges())
        {
            SaveSettingsFromForm();
            return true;
        }

        var choice = AskToSaveModChanges();
        if (choice == ModChangeChoice.Stay)
            return false;
        if (choice == ModChangeChoice.Discard)
            RestoreSavedMods();
        else
            Log("Saved. Press Test to main menu before you connect.");
        PendingMods(_settings.Current()).Clear();
        SaveSettingsFromForm();
        return true;
    }

    private void ReselectShownSetup()
    {
        _applyingSetup = true;
        foreach (ComboBoxItem item in SetupBox.Items)
        {
            if (item.Tag is string name && name.Equals(_shownSetup, StringComparison.OrdinalIgnoreCase))
            {
                SetupBox.SelectedItem = item;
                break;
            }
        }
        _applyingSetup = false;
    }

    private void RememberSavedMods()
    {
        var setup = _settings.Current();
        _savedModsSetup = setup.Name;
        _savedMods = ModSnapshot.Capture(setup);
    }

    private void RememberSavedSwitches()
    {
        var setup = _settings.Current();
        if (_savedMods == null || !setup.Name.Equals(_savedModsSetup, StringComparison.OrdinalIgnoreCase))
        {
            RememberSavedMods();
            return;
        }

        _savedMods.RememberDisabled(setup);
    }

    private bool HasUnsavedModChanges()
    {
        var setup = _settings.Current();
        if (_savedMods == null || !setup.Name.Equals(_savedModsSetup, StringComparison.OrdinalIgnoreCase))
            return PendingMods(setup).Count > 0;
        return !_savedMods.Matches(setup);
    }

    private void RestoreSavedMods()
    {
        if (_savedMods == null)
            return;
        _savedMods.Apply(_settings.Current());
    }

    private ModChangeChoice AskToSaveModChanges()
    {
        var choice = ModChangeChoice.Stay;
        var dialog = new Window
        {
            Title = "Save these changes?",
            Width = 480,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            ResizeMode = ResizeMode.NoResize,
            Background = new SolidColorBrush(Color.FromRgb(0x0E, 0x14, 0x18)),
            Foreground = new SolidColorBrush(Color.FromRgb(0xD5, 0xDE, 0xE6)),
            FontFamily = FontFamily,
            ShowInTaskbar = false
        };
        Coodsz.Ui.WindowPlacement.KeepOnScreen(dialog);
        var card = new Border
        {
            Margin = new Thickness(18),
            Padding = new Thickness(18),
            Background = new SolidColorBrush(Color.FromRgb(0x15, 0x1D, 0x24)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x2A, 0x38, 0x44)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(2)
        };
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = "Save these changes?",
            FontSize = 20,
            Margin = new Thickness(0, 0, 0, 8)
        });
        stack.Children.Add(new TextBlock
        {
            Text = "You added or removed mods and have not pressed Save. Leave this setup without saving?",
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromRgb(0xE2, 0xC2, 0x7A)),
            FontSize = 14,
            FontWeight = FontWeights.SemiBold
        });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 16, 0, 0) };
        var save = new Button { Content = "Save", ToolTip = "Saves the mod changes to this setup first.", Padding = new Thickness(18, 8, 18, 8), Margin = new Thickness(0, 0, 8, 0), Cursor = Cursors.Hand, Background = new SolidColorBrush(Color.FromRgb(0x1F, 0x4E, 0x79)), Foreground = new SolidColorBrush(Color.FromRgb(0xD5, 0xDE, 0xE6)), BorderBrush = new SolidColorBrush(Color.FromRgb(0x8E, 0xB4, 0xD4)) };
        var discard = new Button { Content = "Don't save", ToolTip = "Leaves without saving the mod changes.", Padding = new Thickness(18, 8, 18, 8), Cursor = Cursors.Hand, Background = new SolidColorBrush(Color.FromRgb(0x1B, 0x28, 0x33)), Foreground = new SolidColorBrush(Color.FromRgb(0xD5, 0xDE, 0xE6)), BorderBrush = new SolidColorBrush(Color.FromRgb(0x3A, 0x4C, 0x5C)) };
        save.Click += (_, _) => { choice = ModChangeChoice.Save; dialog.DialogResult = true; };
        // Cancel stays on this setup with the changes still there, the same as closing the window.
        var cancel = new Button { Content = "Cancel", IsCancel = true, ToolTip = "Stays on this setup with your changes still there.", Padding = new Thickness(18, 8, 18, 8), Margin = new Thickness(8, 0, 0, 0), Cursor = Cursors.Hand, Background = new SolidColorBrush(Color.FromRgb(0x1B, 0x28, 0x33)), Foreground = new SolidColorBrush(Color.FromRgb(0xD5, 0xDE, 0xE6)), BorderBrush = new SolidColorBrush(Color.FromRgb(0x3A, 0x4C, 0x5C)) };
        discard.Click += (_, _) => { choice = ModChangeChoice.Discard; dialog.DialogResult = true; };
        buttons.Children.Add(save);
        buttons.Children.Add(discard);
        buttons.Children.Add(cancel);
        stack.Children.Add(buttons);
        card.Child = stack;
        dialog.Content = card;
        dialog.ShowDialog();
        return choice;
    }

    private async void SetupBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_applyingSetup || !IsLoaded)
            return;
        if (SetupBox.SelectedItem is not ComboBoxItem item || item.Tag is not string name)
            return;
        if (name.Equals(_shownSetup, StringComparison.OrdinalIgnoreCase))
            return;

        if (!ConfirmLeaveSetup())
        {
            ReselectShownSetup();
            return;
        }

        var setup = _settings.Setups.FirstOrDefault(entry => entry.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (setup == null)
            return;
        ApplySetupToForm(setup);
        await RefreshAsync();
        RememberSavedMods();
        Log(setup.Name + " loaded. Its map, join, and mod switches are showing.");
        NoteAction("Setup", "Loaded " + setup.Name + ".");
    }

    private async void NewSetup_Click(object sender, RoutedEventArgs e)
    {
        if (!ConfirmLeaveSetup())
            return;

        var setup = new ClientSetup
        {
            Name = _settings.NextSetupName(),
            CampaignMap = "Vanilla",
            RemovedModules = DefaultClientSet.NotOnNewSetup.ToList()
        };
        _settings.Setups.Add(setup);
        _settings.ActiveSetup = setup.Name;
        FillSetupList();
        ApplySetupToForm(setup);
        _settings.Save(SettingsPath);
        await RefreshAsync();
        RememberSavedMods();
        Log(setup.Name + " is a new setup. The mod order is back to the default order. CoopModPatch and PSCoopBridge are not on it. The map and join start fresh.");
    }

    // A button that is off because the setup is locked says why on hover; unlocked, it gets its own tooltip back.
    private readonly Dictionary<Control, object?> _unlockedTips = new();

    private void ShowLockedTip(Control control, bool locked)
    {
        if (!_unlockedTips.ContainsKey(control))
            _unlockedTips[control] = control.ToolTip;
        control.ToolTip = locked ? LockedSetupNote.ButtonTip : _unlockedTips[control];
        // Hover tips show on buttons that are off in every tool (UiLanguage.HoverTips).
        control.ClearValue(ToolTipService.ShowOnDisabledProperty);
    }

    private void CopySetup_Click(object sender, RoutedEventArgs e)
    {
        if (_settings.Current().Locked)
        {
            Log("This setup is locked. It cannot be copied.");
            return;
        }

        if (!ConfirmLeaveSetup())
            return;

        var copy = _settings.Current().Copy(_settings.NextSetupName());
        _settings.Setups.Add(copy);
        _settings.ActiveSetup = copy.Name;
        FillSetupList();
        ApplySetupToForm(copy);
        _settings.Save(SettingsPath);
        RememberSavedMods();
        Log(copy.Name + " copied this map, join, and mod switches. Change it without affecting the other setups.");
    }

    private void SetupNameBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_applyingSetup || !IsLoaded)
            return;

        var setup = _settings.Setups.FirstOrDefault(item => item.Name.Equals(_shownSetup, StringComparison.OrdinalIgnoreCase));
        if (setup is not { Locked: false })
            return;

        var name = SetupNameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name) || name.Equals(setup.Name, StringComparison.OrdinalIgnoreCase))
        {
            _rejectedSetupName = "";
            return;
        }

        if (_settings.Setups.Any(item => !ReferenceEquals(item, setup) && item.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            if (!_rejectedSetupName.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                _rejectedSetupName = name;
                Log("A setup named " + name + " already exists.");
            }
            return;
        }

        _rejectedSetupName = "";
        var previous = setup.Name;
        setup.Name = name;
        _shownSetup = name;
        _settings.ActiveSetup = name;
        if (_savedModsSetup != null && _savedModsSetup.Equals(previous, StringComparison.OrdinalIgnoreCase))
            _savedModsSetup = name;
        if (SetupBox.SelectedItem is ComboBoxItem selected)
        {
            _applyingSetup = true;
            selected.Content = name;
            selected.Tag = name;
            _applyingSetup = false;
        }

        SaveSettingsFromForm();
    }

    private void RenameSetup_Click(object sender, RoutedEventArgs e)
    {
        var name = SetupNameBox.Text.Trim();
        var setup = _settings.Setups.FirstOrDefault(item => item.Name.Equals(_shownSetup, StringComparison.OrdinalIgnoreCase));
        if (setup is { Locked: true })
        {
            Log("This setup is locked. The name cannot be changed.");
            return;
        }
        if (setup == null)
            return;
        if (string.IsNullOrWhiteSpace(name))
        {
            Log("Type a setup name first.");
            return;
        }
        if (_settings.Setups.Any(item => !ReferenceEquals(item, setup) && item.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            Log("A setup named " + name + " already exists.");
            return;
        }

        setup.Name = name;
        _shownSetup = name;
        _settings.ActiveSetup = name;
        FillSetupList();
        SaveSettingsFromForm();
        RememberSavedMods();
        Log("Renamed this setup to " + name + ".");
    }

    private async void DeleteSetup_Click(object sender, RoutedEventArgs e)
    {
        if (_settings.Setups.Count < 2)
        {
            Log("Keep at least one setup.");
            return;
        }
        if (_settings.Current().Locked)
        {
            Log("This setup is locked. It cannot be renamed or deleted.");
            return;
        }

        var setup = _settings.Setups.FirstOrDefault(item => item.Name.Equals(_shownSetup, StringComparison.OrdinalIgnoreCase));
        if (setup == null)
            return;
        if (Coodsz.Ui.UiLanguage.Message("Delete " + setup.Name + "? Its map, join, and mod switches are removed.", "Delete setup", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK)
            return;

        _settings.Setups.Remove(setup);
        _settings.ActiveSetup = _settings.Setups[0].Name;
        FillSetupList();
        ApplySetupToForm(_settings.Setups[0]);
        await RefreshAsync();
        RememberSavedMods();
        Log(setup.Name + " was deleted.");
    }
}
