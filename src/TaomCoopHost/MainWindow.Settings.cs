using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using BannerlordCoopLauncher.Core.Launch;
using BannerlordCoopLauncher.Core.Modules;
using BannerlordCoopLauncher.Core.Saves;
using BannerlordCoopLauncher.Core.Support;
using BannerlordCoopLauncher.Core.Updates;
using Microsoft.Win32;
using TaomCoop.Core;

namespace TaomCoopHost;

public partial class MainWindow
{
    private void LoadConfigIntoFields()
    {
        PasswordBox.Text = _settings.Password;
        AutosaveBox.Text = _settings.AutosaveMinutes.ToString();
        SteamBox.IsChecked = _settings.SteamRelay;
        LogFileBox.IsChecked = true;
        var path = Path.Combine(_settings.DataRoot, "server-config.json");
        if (!File.Exists(path))
            return;
        var raw = File.ReadAllText(path);
        PasswordBox.Text = ReadString(raw, "password") ?? PasswordBox.Text;
        var fileMinutes = ReadNumber(raw, "autosaveMinutes");
        if (fileMinutes == "0" && _settings.AutosaveMinutes > 0 && BrokenWorldRefusal() != null)
            AutosaveBox.Text = _settings.AutosaveMinutes.ToString();
        else
            AutosaveBox.Text = fileMinutes ?? AutosaveBox.Text;
        SteamBox.IsChecked = ReadBool(raw, "steam");
        LogFileBox.IsChecked = ReadBool(raw, "logFile");
        var saveName = ReadString(raw, "saveName");
        if (!string.IsNullOrWhiteSpace(saveName))
            _settings.SaveFile = Path.Combine(_settings.DataRoot, "Game Saves", saveName + ".sav");
    }

    private void LoadTimePermissions()
    {
        var path = CoopModConfig.UserPath();
        if (!File.Exists(path))
            return;
        var json = File.ReadAllText(path);
        FastForwardBox.IsChecked = CoopModConfig.ReadBool(json, "fastForwardEnabled");
        BattleSpeedLimitBox.IsChecked = CoopModConfig.ReadBool(json, "speedLimitWhilePlayersInBattle");
    }

    private void RepairModConfig()
    {
        var path = CoopModConfig.UserPath();
        if (!File.Exists(path))
            return;
        var json = File.ReadAllText(path);
        var health = CoopModConfig.Check(json);
        if (health.Changed)
        {
            File.WriteAllText(path, health.Text);
            foreach (var fix in health.BoolFixes)
                NoteLog(CoopModConfig.BoolFixNote(fix));
            foreach (var stray in health.Strays)
                NoteLog(CoopModConfig.StrayFixNote(stray));
            if (health.OtherRepairs)
                NoteLog(System.Text.RegularExpressions.Regex.IsMatch(json, @"\$1\d")
                    ? "Repaired mod-config.json. A saved number had been written as text the server cannot read."
                    : "Repaired mod-config.json. A broken mod options block was stopping gameplay settings from loading.");
        }

        // One mistake makes the Coop server ignore the whole file and run on its defaults.
        var warning = CoopModConfig.BadLineWarning(health, path);
        if (warning.Length > 0)
            NoteLog(warning);
    }

    private void RepairServerConfig()
    {
        var path = Path.Combine(_settings.DataRoot, "server-config.json");
        if (!File.Exists(path))
            return;
        var json = File.ReadAllText(path);
        var repaired = JsoncValues.Repair(json);
        if (repaired.Equals(json, StringComparison.Ordinal))
            return;
        File.WriteAllText(path, repaired);
        NoteLog("Repaired server-config.json. A saved number had been written as text the server cannot read, so it was ignoring the save name and password.");
    }

    private void SaveTimePermissions()
    {
        var path = CoopModConfig.UserPath();
        if (!File.Exists(path))
            throw new InvalidOperationException("Coop mod-config.json was not found, so the fast-forward choice was not saved.");
        var json = File.ReadAllText(path);
        json = CoopModConfig.SetBool(json, "fastForwardEnabled", FastForwardBox.IsChecked == true);
        json = CoopModConfig.SetBool(json, "speedLimitWhilePlayersInBattle", BattleSpeedLimitBox.IsChecked == true);
        json = SaveDifficulty(json);
        json = SaveKingdomProtection(json);
        File.WriteAllText(path, json);
    }

    private static string? ReadString(string raw, string name)
    {
        var match = System.Text.RegularExpressions.Regex.Match(raw, "\"" + name + "\"\\s*:\\s*\"((?:\\\\.|[^\"\\\\])*)\"");
        return match.Success ? JsonSerializer.Deserialize<string>("\"" + match.Groups[1].Value + "\"") : null;
    }

    private static string? ReadNumber(string raw, string name)
    {
        var match = System.Text.RegularExpressions.Regex.Match(raw, "\"" + name + "\"\\s*:\\s*(-?\\d+)");
        return match.Success ? match.Groups[1].Value : null;
    }

    private static bool ReadBool(string raw, string name)
    {
        var match = System.Text.RegularExpressions.Regex.Match(raw, "\"" + name + "\"\\s*:\\s*(true|false)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return match.Success && match.Groups[1].Value.Equals("true", StringComparison.OrdinalIgnoreCase);
    }

    private void LoadSettings()
    {
        if (!File.Exists(_settingsPath))
            return;
        try
        {
            _settings = JsonSerializer.Deserialize<HostSettings>(File.ReadAllText(_settingsPath)) ?? new HostSettings();
        }
        catch (JsonException)
        {
            _settings = new HostSettings();
        }
    }

    private readonly Dictionary<string, ComboBox> _difficultyBoxes = new();

    private const string KitLacksSetting = "The TAOM server kit does not have this setting yet.";

    private void BuildGameplaySettings()
    {
        AddBattleSize();
        AddDifficulty();
        foreach (var group in GameplaySettingsEditor.Definitions.Select(item => item.Group).Distinct())
        {
            var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Top };
            foreach (var setting in GameplaySettingsEditor.Definitions.Where(item => item.Group == group))
                stack.Children.Add(SettingRow(setting.Label, setting.Hint, ControlFor(setting)));
            AddSection(GroupTitle(group), GroupSummary(group), stack, "Setting" + group);
        }

        var advanced = new StackPanel { VerticalAlignment = VerticalAlignment.Top };
        foreach (var setting in GameplaySettingsEditor.AdvancedDefinitions)
            advanced.Children.Add(SettingRow(setting.Label, setting.Hint, ControlFor(setting)));
        // English in, like every other row: UiLanguage translates it and again after a language switch.
        AddSection(GroupTitle("advanced"), GroupSummary("advanced"), advanced, "Settingadvanced");
        if (_gameplayChecks.TryGetValue("party.troopWeight", out var troopWeight))
            troopWeight.ToolTip = KitLacksSetting;

        LoadGameplaySettings();
        LoadDifficulty();
    }

    // Campaign difficulty lives in Coop's mod-config.json (not the TAOM server-config), so
    // the playtest's own setting check does not reject it.
    private void AddDifficulty()
    {
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Top };
        // Same as Burning Empires (szymon's ticket): greyed boxes looked broken and Save skipped them without a word.
        _difficultyNote = new TextBlock
        {
            Text = CoopDifficulty.MissingNote,
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 15,
            Foreground = new SolidColorBrush(Color.FromRgb(0xD6, 0xB0, 0x5C)),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 28, 12),
            Visibility = Visibility.Collapsed
        };
        AutomationProperties.SetAutomationId(_difficultyNote, "DifficultyMissingNote");
        stack.Children.Add(_difficultyNote);
        foreach (var setting in CoopDifficulty.Settings)
        {
            var box = new ComboBox { Width = 170, Height = 36, Style = (Style)FindResource("SettingChoice"), ToolTip = "Pick how hard this part of the campaign is, then click Save settings. It is greyed out when the Coop config file does not have this setting yet." };
            foreach (var level in CoopDifficulty.Levels)
                box.Items.Add(new ComboBoxItem { Content = CoopDifficulty.LevelName(level), Tag = level });
            _difficultyBoxes[setting.Key] = box;
            stack.Children.Add(SettingRow(setting.Label, setting.Hint, box));
        }

        AddSection("Campaign difficulty",
            "Realistic is the hardest. The server applies these every time it starts, and players who join get the same values. Restart the server after you save.",
            stack, "SettingDifficulty");
    }

    private TextBlock? _difficultyNote;

    // How many greyed out difficulty boxes the last Save settings left as they are.
    private int _difficultySkipped;

    // onlyGreyed: opening Settings again picks up the block Coop added at the first start, but leaves the
    // boxes the host can already change (and any choice not saved yet) alone.
    private void LoadDifficulty(bool onlyGreyed = false)
    {
        if (onlyGreyed && _difficultyBoxes.Values.All(box => box.IsEnabled))
            return;
        string json;
        try
        {
            var path = CoopModConfig.UserPath();
            json = File.Exists(path) ? File.ReadAllText(path) : "";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (onlyGreyed)
                return;
            json = "";
        }

        foreach (var (key, box) in _difficultyBoxes)
        {
            if (onlyGreyed && box.IsEnabled)
                continue;
            var level = CoopDifficulty.Read(json, key);
            box.SelectedItem = box.Items.Cast<ComboBoxItem>().FirstOrDefault(item => (string)item.Tag == level);
            // A file without the setting (or no Coop file yet) cannot be changed from here.
            box.IsEnabled = level != null;
        }

        if (_difficultyNote != null)
            _difficultyNote.Visibility = _difficultyBoxes.Values.Any(box => !box.IsEnabled) ? Visibility.Visible : Visibility.Collapsed;
    }

    private string SaveDifficulty(string json)
    {
        _difficultySkipped = 0;
        foreach (var (key, box) in _difficultyBoxes)
        {
            if (!box.IsEnabled)
                _difficultySkipped++;
            else if (box.SelectedItem is ComboBoxItem { Tag: string level })
                CoopDifficulty.TrySet(ref json, key, level);
        }

        return json;
    }

    private void AddBattleSize()
    {
        _fieldBattleBox = NumberBox();
        _siegeBattleBox = NumberBox();
        _sallyBattleBox = NumberBox();
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Top };
        stack.Children.Add(SettingRow("Field battle size", "How many troops can take the field in an open battle. Use a whole number from 200 to 2048.", _fieldBattleBox));
        stack.Children.Add(SettingRow("Siege battle size", "How many troops can take part in a siege. Use a whole number from 150 to 2048.", _siegeBattleBox));
        stack.Children.Add(SettingRow("Sally-out battle size", "How many troops can take part when a garrison sallies out. Use a whole number from 150 to 2048.", _sallyBattleBox));
        AddSection("Battle size", "How many troops can be in a field battle, a siege, and a sally-out.", stack, "SettingBattle");
    }

    private void AddSection(string title, string summary, UIElement body, string automationId)
    {
        var header = new StackPanel();
        header.Children.Add(new TextBlock
        {
            Text = title,
            FontFamily = new FontFamily("Palatino Linotype"),
            FontSize = 20,
            Foreground = SettingHeader
        });
        header.Children.Add(new TextBlock
        {
            Text = summary,
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 15,
            Foreground = SettingHint,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 28, 0)
        });
        var fold = new Expander
        {
            Style = (Style)FindResource("SettingFold"),
            Header = header,
            Content = body,
            VerticalAlignment = VerticalAlignment.Top,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        AutomationProperties.SetAutomationId(fold, automationId);
        GameplayPanel.Children.Add(fold);
    }

    private FrameworkElement ControlFor(GameplaySetting setting)
    {
        if (setting.Kind == "bool")
        {
            var box = new CheckBox { Margin = new Thickness(0), ToolTip = setting.Hint };
            _gameplayChecks[setting.Key] = box;
            return box;
        }

        var number = NumberBox();
        _gameplayNumbers[setting.Key] = number;
        return number;
    }

    private static TextBox NumberBox() => new()
    {
        Width = 140,
        Height = 36,
        FontSize = 16,
        HorizontalAlignment = HorizontalAlignment.Left,
        Background = new SolidColorBrush(Color.FromRgb(18, 20, 22)),
        Foreground = Ivory,
        BorderBrush = new SolidColorBrush(Color.FromRgb(58, 62, 68)),
        CaretBrush = Ivory,
        Padding = new Thickness(8, 6, 8, 6)
    };

    // The setting's name is on the left of the row, so the box itself says what it is set to.
    internal static void ShowOnOff(CheckBox box)
    {
        void Update() => box.Content = box.IsChecked == true ? "On" : "Off";
        box.Checked += (_, _) => Update();
        box.Unchecked += (_, _) => Update();
        Update();
    }

    private static Border SettingRow(string label, string hint, FrameworkElement control)
    {
        control.VerticalAlignment = VerticalAlignment.Center;
        control.HorizontalAlignment = HorizontalAlignment.Right;
        control.Margin = new Thickness(16, 0, 0, 0);
        if (control is CheckBox check)
        {
            ShowOnOff(check);
            check.FontSize = 16;
            check.Foreground = Ivory;
        }

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock
        {
            Text = label,
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Foreground = SettingName,
            TextWrapping = TextWrapping.Wrap
        });
        text.Children.Add(new TextBlock
        {
            Text = hint,
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 15,
            Foreground = SettingHint,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 2, 0, 0)
        });

        var row = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(control, Dock.Right);
        row.Children.Add(control);
        row.Children.Add(text);
        return new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x3A, 0x3E, 0x44)),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(0, 14, 0, 14),
            Child = row
        };
    }

    private static string GroupSummary(string group) => group switch
    {
        "wotr" => "When the scripted war phases begin.",
        "momentum" => "How the war score builds, and whether it can end the war.",
        "party" => "How large lord parties and garrisons can become.",
        "settlement" => "Whether towns use the TAOM food and market rules.",
        "bandits" => "How many bandits and hideouts the campaign grows toward.",
        "army" => "How AI armies pick targets and stay committed.",
        "balance" => "Troop strength, and how often casualties are wounded instead of killed.",
        "advanced" => "Settings most servers leave alone.",
        _ => ""
    };

    private static string GroupTitle(string group) => group switch
    {
        "wotr" => "War of the Ring",
        "momentum" => "Momentum",
        "party" => "Party",
        "settlement" => "Settlement",
        "bandits" => "Bandits",
        "army" => "Armies",
        "balance" => "Balance",
        "advanced" => "Advanced",
        _ => group
    };

    private void LoadGameplaySettings()
    {
        var path = GameplayPath();
        if (!File.Exists(path) || _fieldBattleBox == null || _siegeBattleBox == null || _sallyBattleBox == null)
            return;
        IReadOnlyDictionary<string, string> values;
        int field, siege, sally;
        try
        {
            values = GameplaySettingsEditor.Read(File.ReadAllText(path), out field, out siege, out sally);
        }
        catch (Exception ex)
        {
            // This runs while the window is built; a throw here closed the tool before it ever showed.
            NoteLog("Gameplay settings could not be read, so the Settings page is blank. " + ex.Message);
            _gameplayLoaded = false;
            return;
        }
        _gameplayLoaded = true;
        _fieldBattleBox.Text = field.ToString();
        _siegeBattleBox.Text = siege.ToString();
        _sallyBattleBox.Text = sally.ToString();
        foreach (var pair in _gameplayChecks)
            pair.Value.IsChecked = values.TryGetValue(pair.Key, out var flag) && flag.Equals("true", StringComparison.OrdinalIgnoreCase);
        if (_gameplayChecks.TryGetValue("party.troopWeight", out var troopWeight))
        {
            var available = values.ContainsKey("party.troopWeight");
            troopWeight.IsEnabled = available;
            troopWeight.ToolTip = available ? GameplaySettingsEditor.AdvancedDefinitions[0].Hint : KitLacksSetting;
        }
        foreach (var pair in _gameplayNumbers)
            pair.Value.Text = values.TryGetValue(pair.Key, out var number) ? number : "";
    }

    private void SaveGameplaySettings()
    {
        var path = GameplayPath();
        if (!File.Exists(path))
            throw new InvalidOperationException("taom-coop-settings.json was not found.");
        // A blank page (the file could not be read when it opened) has nothing to save, and it
        // must not stop the server config and fast forward choices from saving.
        if (!_gameplayLoaded || _fieldBattleBox == null || _siegeBattleBox == null || _sallyBattleBox == null)
            return;
        var values = _gameplayNumbers.ToDictionary(pair => pair.Key, pair => pair.Value.Text.Trim());
        foreach (var pair in _gameplayChecks)
            if (pair.Value.IsEnabled)
                values[pair.Key] = pair.Value.IsChecked == true ? "true" : "false";
        var updated = GameplaySettingsEditor.Apply(File.ReadAllText(path), values, _fieldBattleBox.Text.Trim(), _siegeBattleBox.Text.Trim(), _sallyBattleBox.Text.Trim());
        File.WriteAllText(path, updated);
    }

    private string GameplayPath() => Path.Combine(_settings.DataRoot, "taom-coop-settings.json");

    private void ServerAddress_LostFocus(object sender, RoutedEventArgs e) => SaveSettings();

    private void SaveSettings()
    {
        _settings.SaveFile = SelectedSavePath();
        _settings.Password = PasswordBox.Text;
        _settings.ServerAddress = ServerAddressBox.Text.Trim();
        _settings.SteamRelay = SteamBox.IsChecked == true;
        if (int.TryParse(AutosaveBox.Text.Trim(), out var autosave) && autosave is >= 0 and <= 1440)
            _settings.AutosaveMinutes = autosave;
        File.WriteAllText(_settingsPath, JsonSerializer.Serialize(_settings, new JsonSerializerOptions { WriteIndented = true }));
    }

    // A box left empty kept its stored value when Save settings ran. Show that value again, so
    // the page matches the files instead of staying blank.
    private void RefillEmptySettings()
    {
        static bool Empty(TextBox box) => box.Text.Trim().Length == 0;

        if (Empty(AutosaveBox))
        {
            var path = Path.Combine(_settings.DataRoot, "server-config.json");
            var fileMinutes = File.Exists(path) ? ReadNumber(File.ReadAllText(path), "autosaveMinutes") : null;
            AutosaveBox.Text = fileMinutes == null || (fileMinutes == "0" && _settings.AutosaveMinutes > 0 && BrokenWorldRefusal() != null)
                ? _settings.AutosaveMinutes.ToString()
                : fileMinutes;
        }

        if (!_gameplayLoaded || _fieldBattleBox == null || _siegeBattleBox == null || _sallyBattleBox == null)
            return;
        var gameplayBoxes = _gameplayNumbers.Values.Append(_fieldBattleBox).Append(_siegeBattleBox).Append(_sallyBattleBox);
        if (!gameplayBoxes.Any(Empty) || !File.Exists(GameplayPath()))
            return;
        IReadOnlyDictionary<string, string> values;
        int field, siege, sally;
        try
        {
            values = GameplaySettingsEditor.Read(File.ReadAllText(GameplayPath()), out field, out siege, out sally);
        }
        catch (Exception)
        {
            // The save itself worked; the boxes just stay as they are.
            return;
        }
        if (Empty(_fieldBattleBox))
            _fieldBattleBox.Text = field.ToString();
        if (Empty(_siegeBattleBox))
            _siegeBattleBox.Text = siege.ToString();
        if (Empty(_sallyBattleBox))
            _sallyBattleBox.Text = sally.ToString();
        foreach (var pair in _gameplayNumbers)
            if (Empty(pair.Value) && values.TryGetValue(pair.Key, out var number))
                pair.Value.Text = number;
    }
}
