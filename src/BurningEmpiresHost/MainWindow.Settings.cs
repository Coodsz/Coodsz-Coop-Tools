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
using BurningEmpires.Core;
using Microsoft.Win32;
using TaomCoop.Core;

namespace BurningEmpiresHost;

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
        if (!string.IsNullOrWhiteSpace(saveName) && !BeServerLaunch.IsTemplate(saveName))
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
        if (!repaired.Equals(json, StringComparison.Ordinal))
        {
            File.WriteAllText(path, repaired);
            NoteLog("Repaired server-config.json. A saved number had been written as text the server cannot read, so it was ignoring the save name and password.");
        }

        KeepClientTimeControls(path, repaired);
    }

    // Every start: a server-config.json made before the tool wrote allowClientTimeControls (BE #11, #12) gets it as
    // on, before the start script reads a missing key as off ("Client time controls: disabled", fast forward
    // capped at 1x). A host's own false stays.
    private void KeepClientTimeControls(string path, string json)
    {
        var added = ClientTimeControls.WithKey(json);
        if (added == null)
            return;
        try
        {
            File.WriteAllText(path, added);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The server still starts, with players' time buttons off as before.
            NoteLog("Could not turn on players' time buttons in server-config.json.");
            NoteLog(ex.Message);
            return;
        }

        NoteLog("Players' time buttons turned on. server-config.json had no setting for them, so the server took them as off.");
        _actions.Note("Client time controls", "Added allowClientTimeControls true to server-config.json");
    }

    private void SaveTimePermissions()
    {
        var path = CoopModConfig.UserPath();
        if (!File.Exists(path))
            throw new InvalidOperationException("Coop mod-config.json was not found, so the fast forward choice was not saved. Click Prepare server first.");
        var json = File.ReadAllText(path);
        json = CoopModConfig.SetBool(json, "fastForwardEnabled", FastForwardBox.IsChecked == true);
        json = CoopModConfig.SetBool(json, "speedLimitWhilePlayersInBattle", BattleSpeedLimitBox.IsChecked == true);
        json = SaveDifficulty(json);
        json = SaveBattleSize(json);
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

    // Burning Empires has no settings file of its own on the server: the battle size and the
    // difficulty are Coop's, in mod-config.json.
    private void BuildGameplaySettings()
    {
        AddBattleSize();
        AddDifficulty();
        LoadBattleSize();
        LoadDifficulty();
    }

    // Campaign difficulty lives in Coop's mod-config.json, so players who join get the same values.
    private void AddDifficulty()
    {
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Top };
        // szymon (BE ticket): greyed boxes looked broken and Save skipped them without a word.
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

    // Coop's server reads one battle size from modOptions.battleSize in mod-config.json, and
    // Start-ModdedServer.ps1 refuses a size under 300, so the box takes 300 to 1000.
    private const int MinBattleSize = BeServerLaunch.MinBattleSize;
    private const int MaxBattleSize = CoopModConfig.BattleSizeMax;

    private void AddBattleSize()
    {
        _battleSizeBox = NumberBox();
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Top };
        stack.Children.Add(SettingRow("Battle size", "How many troops can be in one battle, for field battles, sieges and sally-outs alike. Use a whole number from " + MinBattleSize + " to " + MaxBattleSize + ".", _battleSizeBox));
        AddSection("Battle size", "How many troops can be in a battle. Coop keeps it in mod-config.json and the server reads it when it starts.", stack, "SettingBattle");
    }

    private void LoadBattleSize()
    {
        if (_battleSizeBox == null)
            return;
        var path = CoopModConfig.UserPath();
        try
        {
            // mod-config.json is shared with Coodsz Host and Coop's own server, which allow 200. The box
            // shows the size Start server really uses (300 or more), so an older 250 no longer stops
            // Save settings or the Game speed buttons with a battle size error.
            _battleSizeBox.Text = BeServerLaunch.StartBattleSize(File.Exists(path) ? File.ReadAllText(path) : null).ToString();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            // This runs while the window is built; a throw here would close the tool before it showed.
            NoteLog("The battle size could not be read from mod-config.json. " + ex.Message);
        }
    }

    // The battle size Start server passes as -BattleSize: the saved one in mod-config.json, so the
    // server's Launcher Battle Size and Coop's own battle size get the same number.
    private int StartBattleSize()
    {
        var path = CoopModConfig.UserPath();
        try
        {
            return BeServerLaunch.StartBattleSize(File.Exists(path) ? File.ReadAllText(path) : null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            NoteLog("The battle size could not be read from mod-config.json. " + ex.Message);
            return BeServerLaunch.StartBattleSize(null);
        }
    }

    // An empty box keeps the battle size already in mod-config.json; only bad text stops the save.
    private string SaveBattleSize(string json)
    {
        var text = _battleSizeBox?.Text.Trim() ?? "";
        if (text.Length == 0)
            return json;
        if (!int.TryParse(text, out var size) || size < MinBattleSize || size > MaxBattleSize)
            throw new InvalidOperationException("The battle size must be a whole number from " + MinBattleSize + " to " + MaxBattleSize + ".");
        return CoopModConfig.SetBattleSize(json, size);
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

        if (_battleSizeBox != null && Empty(_battleSizeBox))
            LoadBattleSize();
    }
}
