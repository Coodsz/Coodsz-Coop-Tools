using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BannerlordCoopLauncher.Core.Configuration;
using CoodszHost.Services;
using TaomCoop.Core;

namespace CoodszHost;

// Game speed on the Dashboard's Game time card, and the Game speed group at the top of the
// Settings page. Coodsz Host only: the AD1259 build keeps both pages as they were. The buttons
// set the Settings page's own boxes and run its Save settings, so 2x speed has one way into
// mod-config.json (fastForwardEnabled, speedLimitWhilePlayersInBattle) and server-config.json
// (allowClientTimeControls).
public partial class MainWindow
{
    private static bool ShowsGameSpeed => !Edition.IsAD1259;

    // What the saved files say, and what the running server started with.
    private GameSpeedState _savedSpeed = new(FastForward: true, BattleSpeedLimit: true);
    private GameSpeedState? _speedAtStart;
    private string _speedError = "";

    // Called once from the constructor, after the Settings page boxes are filled and watched.
    private void ShowGameSpeedGroup()
    {
        if (!ShowsGameSpeed)
            return;
        // Each box sits in its own row Grid on the Settings page; the row moves with it.
        foreach (var row in new[] { (FrameworkElement)FastForwardBox.Parent, ClientTimeBox, (FrameworkElement)SpeedLimitBox.Parent })
        {
            if (row.Parent is Panel panel)
                panel.Children.Remove(row);
            GameSpeedRows.Children.Add(row);
        }

        ClientTimeBox.Margin = new Thickness(0, 6, 0, 6);
        GameSpeedGroup.Visibility = Visibility.Visible;
        GameSpeedArea.Visibility = Visibility.Visible;
    }

    // Every start, both builds: a server-config.json with no allowClientTimeControls gets it as on, the way
    // the Settings page shows it, before the start script reads it as off.
    private void KeepClientTimeControls()
    {
        try
        {
            if (!File.Exists(ServerSession.ServerConfigPath))
                return;
            var added = ServerSession.WithClientTimeKey(Services.JsoncEditor.Read(ServerSession.ServerConfigPath));
            if (added == null)
                return;
            Services.JsoncEditor.Write(ServerSession.ServerConfigPath, added);
            Append("Players' time buttons turned on. server-config.json had no setting for them, so the server took them as off.");
            _actions.Note("Client time controls", "Added allowClientTimeControls true to server-config.json");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Append("Could not turn on players' time buttons in server-config.json.");
            Append(ex.Message);
        }
    }

    private GameSpeedState SavedGameSpeed()
    {
        var values = CoopSettingsFor(SettingsSaveBox.SelectedItem as string ?? _settings.Current().SaveName, out _);
        var clientTime = true;
        try
        {
            if (File.Exists(ServerSession.ServerConfigPath))
                clientTime = Flag(Services.JsoncEditor.Read(ServerSession.ServerConfigPath), "allowClientTimeControls", true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return GameSpeed.From(values, clientTime);
    }

    private void RefreshGameSpeed()
    {
        if (!ShowsGameSpeed)
            return;
        _savedSpeed = SavedGameSpeed();
        _speedError = "";
        GameSpeedLine.Text = GameSpeed.SpeedLine(_savedSpeed);
        GameSpeedLine.Foreground = new SolidColorBrush(_savedSpeed.TwoTimesOn ? Color.FromRgb(0xB6, 0xE7, 0xC2) : Color.FromRgb(0xE2, 0xC2, 0x7A));
        GameSpeedBattleLine.Text = GameSpeed.BattleLine(_savedSpeed);
        GameSpeedButton.Content = GameSpeed.SpeedButton(_savedSpeed);
        GameSpeedBattleButton.Content = GameSpeed.BattleButton(_savedSpeed);
        ShowGameSpeedRestart();
    }

    // Runs with every state refresh, so Restart server shows only while a server runs.
    private void ShowGameSpeedRestart()
    {
        if (!ShowsGameSpeed)
            return;
        var running = _session.IsRunning;
        GameSpeedRestartLine.Text = _speedError.Length > 0 ? _speedError : GameSpeed.RestartLine(running, _speedAtStart, _savedSpeed);
        GameSpeedRestartButton.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
    }

    private void GameSpeed_Click(object sender, RoutedEventArgs e) =>
        SaveGameSpeed(GameSpeed.WithTwoTimes(_savedSpeed, !_savedSpeed.TwoTimesOn));

    private void GameSpeedBattle_Click(object sender, RoutedEventArgs e) =>
        SaveGameSpeed(GameSpeed.WithBattleLimit(_savedSpeed, !_savedSpeed.BattleSpeedLimit));

    private void SaveGameSpeed(GameSpeedState wanted)
    {
        var before = _savedSpeed;
        var shown = (FastForwardBox.SelectedItem, SpeedLimitBox.SelectedItem, ClientTimeBox.IsChecked);
        FastForwardBox.SelectedItem = wanted.FastForward ? TrueText : FalseText;
        SpeedLimitBox.SelectedItem = wanted.BattleSpeedLimit ? TrueText : FalseText;
        ClientTimeBox.IsChecked = wanted.ClientTimeControls;
        if (!SaveSettingsPage())
        {
            // Put the Settings page back as it was; the reason is in its note, shown here too.
            FastForwardBox.SelectedItem = shown.Item1;
            SpeedLimitBox.SelectedItem = shown.Item2;
            ClientTimeBox.IsChecked = shown.Item3;
            _speedError = Coodsz.Ui.UiLanguage.English(SettingsNote);
            ShowGameSpeedRestart();
            return;
        }

        // A server that was already running when this tool opened started with what was saved before.
        if (_session.IsRunning)
            _speedAtStart ??= before;
        RefreshGameSpeed();
    }
}
