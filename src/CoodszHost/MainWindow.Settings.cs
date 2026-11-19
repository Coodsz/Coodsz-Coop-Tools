using System.Collections.ObjectModel;
using System.Text;
using System.Windows.Documents;
using TaomCoop.Core;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using BannerlordCoopLauncher.Core.Launch;
using BannerlordCoopLauncher.Core.Saves;
using BannerlordCoopLauncher.Core.Support;
using BannerlordCoopLauncher.Core.Updates;
using BannerlordCoopLauncher.Core.Modules;
using CoodszHost.Services;
using Microsoft.Win32;

namespace CoodszHost;

public partial class MainWindow
{
    private void SaveBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || SaveBox.SelectedItem is not string name || name.Length == 0)
            return;
        HideSaveModCard();
        _settings.Current().SaveName = name;
        OverviewSave.Text = name;
        ShowSaveDetail(name);
        // The Settings page shows the same save, so per campaign settings go to the save that starts.
        if (!Equals(SettingsSaveBox.SelectedItem, name))
            SettingsSaveBox.SelectedItem = name;
    }

    // The Saves page's save is the one source of truth for the Settings page's Campaign save box.
    private void FollowServerPageSave()
    {
        var name = SaveBox.SelectedItem as string;
        if (string.IsNullOrWhiteSpace(name))
            name = _settings.Current().SaveName;
        if (!string.IsNullOrWhiteSpace(name) && !Equals(SettingsSaveBox.SelectedItem, name))
            SettingsSaveBox.SelectedItem = name;
    }

    private void FillDifficulties()
    {
        foreach (var box in new[] { PlayerDamage, TroopDamage, CombatAi, Recruitment, MapMovement, Stealth, Persuasion, ClanDeath, BattleDeath })
        {
            box.Items.Add("VeryEasy");
            box.Items.Add("Easy");
            box.Items.Add("Realistic");
            box.SelectedIndex = 1;
        }

        foreach (var box in new[] { SettlementEconomyBox, DisconnectedEconomyBox, FastForwardBox, AutoPauseBox, ClientCheatsBox, SpeedLimitBox, NameplatesBox, BirthDeathBox, AutoPerks, WandererScaleBox, SmithOutsideBox, HeroExecutionsBox, ClanExecutionsBox })
        {
            box.Items.Add("True");
            box.Items.Add("False");
            box.SelectedIndex = 1;
        }

        foreach (var option in new[] { "False", "One day max", "True" })
            BattleEconomyBox.Items.Add(option);
        BattleEconomyBox.SelectedIndex = 0;
        foreach (var option in new[] { "Vanilla", "NeverExpire", "AlwaysRetry" })
            DefectionBox.Items.Add(option);
        DefectionBox.SelectedIndex = 0;
        // The boxes keep the raw values; the screen shows them as translated labels (On, Very easy).
        foreach (var (box, _) in CatalogueCombos())
            SettingChoiceLabel.SetChoiceLabels(box, true);
        BuildMoreCoopSettings();
        FillCoopSettings(BannerlordCoopLauncher.Core.Configuration.ModSettingsCatalog.Defaults(ServerCoopVersion()));
        SettingsSaveBox.SelectionChanged += SettingsSave_SelectionChanged;
    }

    // True once the server-config.json switches on the page were read from the file. Until then the boxes show
    // Coop's defaults (ServerConfigFlags), and Save settings keeps a value the file has for a box the host left alone.
    private bool _serverFlagsLoaded;

    private void LoadDifficulty()
    {
        FollowServerPageSave();
        LoadServerSwitches();

        LoadClanCap();
        LoadServerRules();
        LoadPvpRules();
        if (!File.Exists(ServerSession.ModConfigPath))
        {
            // Coop's defaults until the server makes the file on its first start.
            var shownSave = SettingsSaveBox.SelectedItem as string;
            FillCoopSettings(CoopSettingsFor(shownSave, out var keptOnly));
            ShowSaveProfileNote(shownSave, keptOnly);
            RefreshGameSpeed();
            return;
        }
        ShowModConfigWarning(RepairModConfigFile());
        var json = JsoncEditor.Read(ServerSession.ModConfigPath);
        if (File.Exists(ServerSession.ServerConfigPath))
        {
            var server = JsoncEditor.Read(ServerSession.ServerConfigPath);
            // server-config.json's saveName is only a fallback: it can still name the last save that ran.
            var serverSave = JsoncEditor.GetString(server, "saveName");
            if (SettingsSaveBox.SelectedItem == null && !string.IsNullOrWhiteSpace(serverSave))
                SettingsSaveBox.SelectedItem = serverSave;
        }
        // The settings kept for the picked save when it has them, otherwise mod-config.json.
        var save = SettingsSaveBox.SelectedItem as string;
        FillCoopSettings(CoopSettingsFor(save, out var kept));
        ShowSaveProfileNote(save, kept);
        RefreshGameSpeed();
    }

    // A box left empty kept its stored value when Save settings ran. Show that value again, so
    // the page matches the files instead of staying blank (Auto restart hours kept 3 but showed nothing).
    private void RefillEmptySettings()
    {
        static bool Empty(TextBox box) => box.Text.Trim().Length == 0;

        if (File.Exists(ServerSession.ServerConfigPath) && (Empty(AutosaveBox) || Empty(RestartHoursBox)))
        {
            var server = JsoncEditor.Read(ServerSession.ServerConfigPath);
            if (Empty(AutosaveBox))
            {
                var fileMinutes = JsoncEditor.GetRaw(server, "autosaveMinutes");
                AutosaveBox.Text = fileMinutes == "0" && _settings.Current().AutosaveMinutes > 0 && BrokenWorldRefusal() != null
                    ? _settings.Current().AutosaveMinutes.ToString()
                    : fileMinutes;
            }
            if (Empty(RestartHoursBox))
                RestartHoursBox.Text = JsoncEditor.GetRaw(server, "autoRestartHours");
        }
        if (Empty(ClanCapBox))
            ClanCapBox.Text = BannerlordCoopLauncher.Core.Server.ClanCapFile.Read(ClanCapPath).MaxClans.ToString();

        var coopTexts = CatalogueTexts()
            .Concat(_moreCoopSettings.Where(pair => pair.Value is TextBox).Select(pair => ((TextBox)pair.Value, pair.Key)))
            .Where(item => Empty(item.Item1))
            .ToList();
        if (coopTexts.Count == 0)
            return;
        var values = CoopSettingsFor(SettingsSaveBox.SelectedItem as string, out _);
        _fillingCoopSettings = true;
        try
        {
            foreach (var (box, id) in coopTexts)
                box.Text = SettingText(id, values);
        }
        finally
        {
            _fillingCoopSettings = false;
        }
    }

    private void ShowModConfigWarning(string warning)
    {
        if (warning.Length == 0)
            return;
        SettingsNote.Text = warning;
        SettingsNote.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xB4, 0xA8));
    }

    // The server-config.json switches, read before anything that can return early (a missing mod-config.json), so
    // _serverFlagsLoaded is only true once the boxes show the file. Start writes Steam advertise from SteamBox, and an
    // unread box used to turn "steam": true into false. No server-config.json yet (a fresh setup): Coop's defaults.
    private void LoadServerSwitches()
    {
        _serverFlagsLoaded = File.Exists(ServerSession.ServerConfigPath);
        if (!_serverFlagsLoaded)
        {
            SteamBox.IsChecked = ServerConfigFlags.Defaults["steam"];
            LogFileBox.IsChecked = ServerConfigFlags.Defaults["logFile"];
            AutoRestartBox.IsChecked = ServerConfigFlags.Defaults["autoRestart"];
            ClientTimeBox.IsChecked = ServerConfigFlags.Defaults["allowClientTimeControls"];
            return;
        }

        var server = JsoncEditor.Read(ServerSession.ServerConfigPath);
        SteamBox.IsChecked = Flag(server, "steam", true);
        LogFileBox.IsChecked = Flag(server, "logFile", true);
        // An older config says autoRestart true without the host ever choosing it, so the box starts off
        // until Settings were saved once; else a Save for another setting would turn 3 hour restarts on.
        AutoRestartBox.IsChecked = _settings.AutoRestartSaved && Flag(server, "autoRestart", true);
        ClientTimeBox.IsChecked = Flag(server, "allowClientTimeControls", true);
        // The password, autosave and restart hours boxes too: Start writes the password and autosave from them, and an
        // unread password box turned the file's password into "" (Devin, v1.3.6 delta review).
        PasswordBox.Text = _settings.Current().Password ?? JsoncEditor.GetString(server, "password");
        var fileMinutes = JsoncEditor.GetRaw(server, "autosaveMinutes");
        if (int.TryParse(fileMinutes, out var loadedMinutes) && loadedMinutes > 0)
            _settings.Current().AutosaveMinutes = loadedMinutes;
        AutosaveBox.Text = fileMinutes == "0" && _settings.Current().AutosaveMinutes > 0 && BrokenWorldRefusal() != null
            ? _settings.Current().AutosaveMinutes.ToString()
            : fileMinutes;
        RestartHoursBox.Text = JsoncEditor.GetRaw(server, "autoRestartHours");
    }

    private static bool Flag(string json, string key, bool fallback)
    {
        var raw = JsoncEditor.GetRaw(json, key);
        return raw.Length == 0 ? fallback : raw == "true";
    }

    private void BattleSize_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (BattleSizeLabel != null)
            BattleSizeLabel.Text = ((int)BattleSizeSlider.Value).ToString();
        ShowBattleSizeTier();
        // The host moved the slider by hand: the note that the saved size was brought back is old now.
        if (_battleSizeClampedFrom > 0 && !_settingBattleSizeMode && !_fillingCoopSettings && !_loading)
        {
            _battleSizeClampedFrom = 0;
            RefreshBattleSizeMode();
        }
    }

    private void WatchSettingsPage()
    {
        foreach (var box in SettingsCombos())
            box.SelectionChanged += (_, _) => RefreshSettingsPulse();
        foreach (var box in SettingsTexts())
            box.TextChanged += (_, _) => RefreshSettingsPulse();
        foreach (var box in SettingsChecks())
        {
            box.Checked += (_, _) => RefreshSettingsPulse();
            box.Unchecked += (_, _) => RefreshSettingsPulse();
        }
        BattleSizeSlider.ValueChanged += (_, _) => RefreshSettingsPulse();
    }

    private IEnumerable<ComboBox> SettingsCombos() =>
    [
        SettingsSaveBox, PlayerDamage, TroopDamage, CombatAi, Recruitment, MapMovement, Stealth, Persuasion,
        ClanDeath, BattleDeath, AutoPerks, BirthDeathBox, FastForwardBox, AutoPauseBox, ClientCheatsBox,
        SpeedLimitBox, SettlementEconomyBox, BattleEconomyBox, DisconnectedEconomyBox, NameplatesBox, UnstuckBox,
        KingdomProtectionBox, WandererScaleBox, SmithOutsideBox, DefectionBox, HeroExecutionsBox, ClanExecutionsBox,
        .. _moreCoopSettings.Values.OfType<ComboBox>()
    ];

    private IEnumerable<TextBox> SettingsTexts() =>
    [
        AutosaveBox, RestartHoursBox, AiWindowBox, WandererBox, ClanTierBox,
        SmithBox, LooterCountBox, LooterSizeBox, ClanCapBox, PvpSecondsBox,
        .. _moreCoopSettings.Values.OfType<TextBox>()
    ];

    private IEnumerable<CheckBox> SettingsChecks() => [ClientTimeBox, LogFileBox, AutoRestartBox, ClanCapMercenariesBox, PauseOnJoinBox, FreezeOfflineXpBox, PvpBlockLeaveBox, PvpTimerBox];

    private string SettingsSnapshot()
    {
        var parts = SettingsCombos().Select(box => box.Name + "=" + box.SelectedItem)
            .Concat(SettingsTexts().Select(box => box.Name + "=" + box.Text.Trim()))
            .Concat(SettingsChecks().Select(box => box.Name + "=" + (box.IsChecked == true)))
            .Append("battle=" + (int)BattleSizeSlider.Value)
            .Append("hosts=" + string.Join(",", _preferredHosts));
        return string.Join("|", parts);
    }

    private void RememberSettingsSnapshot()
    {
        _savedSettings = SettingsSnapshot();
        _watchSettings = true;
        RefreshSettingsPulse();
    }

    private void RefreshSettingsPulse()
    {
        if (!_watchSettings)
            return;
        if (SettingsSnapshot() == _savedSettings)
        {
            StopSettingsPulse();
            SettingsSavePrompt.Text = "Finish your changes";
            SettingsSavePrompt.FontSize = 18;
            SettingsSavePrompt.Foreground = new SolidColorBrush(Color.FromRgb(0x8E, 0xA0, 0xAE));
            SettingsSaveDetail.FontSize = 16;
            SettingsSaveDetail.Foreground = Brushes.White;
            SettingsSaveBanner.Background = new SolidColorBrush(Color.FromRgb(0x15, 0x1D, 0x24));
            SettingsSaveBanner.BorderBrush = new SolidColorBrush(Color.FromRgb(0x3A, 0x4C, 0x5C));
            SettingsSaveBanner.BorderThickness = new Thickness(1);
            return;
        }

        SettingsSavePrompt.Text = "Make sure to press Save settings, then restart the server. A running server keeps the old values until you do.";
        SettingsSavePrompt.FontSize = 20;
        SettingsSavePrompt.Foreground = new SolidColorBrush(Color.FromRgb(0xE2, 0xC2, 0x7A));
        SettingsSaveDetail.FontSize = 18;
        SettingsSaveDetail.Foreground = Brushes.White;
        SettingsSaveBanner.Background = new SolidColorBrush(Color.FromRgb(0x3A, 0x34, 0x20));
        SettingsSaveBanner.BorderBrush = new SolidColorBrush(Color.FromRgb(0xE2, 0xC2, 0x7A));
        SettingsSaveBanner.BorderThickness = new Thickness(2);
        PulseSettingsButton();
    }

    private void PulseSettingsButton()
    {
        SaveSettingsButton.Background = new SolidColorBrush(Color.FromRgb(0x3A, 0x34, 0x20));
        SaveSettingsButton.Foreground = new SolidColorBrush(Color.FromRgb(0xE2, 0xC2, 0x7A));
        SaveSettingsButton.BorderBrush = new SolidColorBrush(Color.FromRgb(0xE2, 0xC2, 0x7A));
        SaveSettingsButton.BorderThickness = new Thickness(2);
        if (_settingsPulse != null)
            return;

        var animation = new DoubleAnimation(1, 0.45, TimeSpan.FromMilliseconds(700))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever
        };
        Storyboard.SetTarget(animation, SaveSettingsButton);
        Storyboard.SetTargetProperty(animation, new PropertyPath(UIElement.OpacityProperty));
        _settingsPulse = new Storyboard();
        _settingsPulse.Children.Add(animation);
        _settingsPulse.Begin(SaveSettingsButton, true);
    }

    private void StopSettingsPulse()
    {
        if (_settingsPulse == null)
            return;
        _settingsPulse.Stop(SaveSettingsButton);
        _settingsPulse = null;
        SaveSettingsButton.BeginAnimation(UIElement.OpacityProperty, null);
        SaveSettingsButton.Opacity = 1;
        SaveSettingsButton.ClearValue(Control.BackgroundProperty);
        SaveSettingsButton.ClearValue(Control.ForegroundProperty);
        SaveSettingsButton.ClearValue(Control.BorderBrushProperty);
        SaveSettingsButton.ClearValue(Control.BorderThicknessProperty);
    }

    private void SaveSettingsPage_Click(object sender, RoutedEventArgs e) => SaveSettingsPage();

    // The Settings page's one save. The Game speed buttons on the Dashboard use it too.
    private bool SaveSettingsPage()
    {
        try
        {
            var profile = _settings.Current();
            var clanCap = ClanCapFromPage();
            var serverRules = ServerRulesFromPage();
            var pvpRules = PvpRulesFromPage();
            profile.ServerAddress = ServerAddressBox.Text.Trim();
            ApplyLaunchDefaults(profile);
            ApplyProfileToFields(profile);
            if (!File.Exists(ServerSession.ModConfigPath))
                throw new InvalidOperationException("mod-config.json was not found. Start the server once so Coop can create it.");
            // Every Coop gameplay setting goes through the one catalogue and the section aware
            // store, after the health check has fixed a capital True and stray copies.
            var values = CoopSettingsFromPage();
            var json = CoopModConfig.Check(JsoncEditor.Read(ServerSession.ModConfigPath)).Text;
            json = BannerlordCoopLauncher.Core.Configuration.ModConfigStore.WriteText(json, values, out _, ServerCoopVersion());
            if (File.Exists(ServerSession.ServerConfigPath))
            {
                var server = JsoncEditor.Read(ServerSession.ServerConfigPath);
                // Only spaces counts as cleared, the same as Start (ServerConfigFlags.NormalPassword).
                var password = ServerConfigFlags.NormalPassword(PasswordBox.Text);
                _settings.Current().Password = password;
                server = JsoncEditor.SetString(server, "password", password);
                if (int.TryParse(AutosaveBox.Text.Trim(), out var chosenMinutes) && chosenMinutes > 0)
                    _settings.Current().AutosaveMinutes = chosenMinutes;
                server = JsoncEditor.SetNumber(server, "autosaveMinutes", AutosaveBox.Text.Trim());
                server = JsoncEditor.SetNumber(server, "autoRestartHours", RestartHoursBox.Text.Trim());
                server = JsoncEditor.SetBool(server, "autoRestart", AutoRestartBox.IsChecked == true);
                _settings.AutoRestartSaved = true;
                // The file is read again here: a switch the page never loaded and the host left alone keeps its value.
                server = JsoncEditor.SetBool(server, "steam", ServerConfigFlags.ToWrite("steam", SteamBox.IsChecked == true, _serverFlagsLoaded, server));
                server = JsoncEditor.SetBool(server, "logFile", ServerConfigFlags.ToWrite("logFile", LogFileBox.IsChecked == true, _serverFlagsLoaded, server));
                server = JsoncEditor.SetBool(server, "allowClientTimeControls", ServerConfigFlags.ToWrite("allowClientTimeControls", ClientTimeBox.IsChecked == true, _serverFlagsLoaded, server));
                if (SettingsSaveBox.SelectedItem is string saveName && saveName.Length > 0)
                {
                    server = JsoncEditor.SetString(server, "saveName", saveName);
                    _settings.Current().SaveName = saveName;
                    SaveBox.SelectedItem = saveName;
                }

                JsoncEditor.Write(ServerSession.ServerConfigPath, server);
                _serverFlagsLoaded = true;
            }
            JsoncEditor.Write(ServerSession.ModConfigPath, json);
            SaveClanCap(clanCap);
            SaveServerRules(serverRules);
            SavePvpRules(pvpRules);
            if (profile.Locked)
                HostSettings.Save(SettingsPath, _settings);
            else
                PersistProfile();
            var keptFor = SettingsSaveBox.SelectedItem as string;
            KeepSaveProfile(keptFor, values);
            // Max clans per kingdom is read again by the server within seconds; the rest waits for a restart.
            SettingsNote.Text = string.IsNullOrWhiteSpace(keptFor)
                ? "Saved. Max clans per kingdom applies within a few seconds. Restart the server so the other changes apply."
                : "Saved, and kept for " + keptFor + ". Max clans per kingdom applies within a few seconds. Restart the server so the other changes apply.";
            // With Limitless and its Coop patch the server reads the battle size again before each battle.
            if (BattleMode == BannerlordCoopLauncher.Core.Modules.BigBattleMode.Unlocked)
                SettingsNote.Text += " Battle size applies from the next battle.";
            SettingsNote.Foreground = Brushes.White;
            ShowModConfigWarning(CoopModConfig.BadLineWarning(CoopModConfig.Check(json), ServerSession.ModConfigPath));
            // The save already worked; a file that cannot be read again only leaves a box blank.
            try { RefillEmptySettings(); } catch (Exception) { }
            RememberSettingsSnapshot();
            RefreshGameSpeed();
            return true;
        }
        catch (Exception ex)
        {
            SettingsNote.Text = ex.Message;
            SettingsNote.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xB4, 0xA8));
            return false;
        }
    }
}
