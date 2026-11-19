using System.IO;
using System.Windows;
using System.Windows.Media;
using BannerlordCoopLauncher.Core.Configuration;
using BurningEmpires.Core;
using TaomCoop.Core;

namespace BurningEmpiresHost;

// Game speed on the Dashboard. The buttons tick the Settings page's own Game speed boxes and
// run its Save settings, so 2x speed has one way into mod-config.json (fastForwardEnabled,
// speedLimitWhilePlayersInBattle).
public partial class MainWindow
{
    // What mod-config.json says, and what the running server started with.
    private GameSpeedState _savedSpeed = new(FastForward: true, BattleSpeedLimit: true);
    private GameSpeedState? _speedAtStart;
    private string _speedError = "";
    private bool _speedRunning;

    private static GameSpeedState SavedGameSpeed()
    {
        var path = CoopModConfig.UserPath();
        try
        {
            var json = File.Exists(path) ? File.ReadAllText(path) : "";
            return new GameSpeedState(CoopModConfig.ReadBool(json, "fastForwardEnabled"), CoopModConfig.ReadBool(json, "speedLimitWhilePlayersInBattle"));
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            return GameSpeed.From(ModSettingsCatalog.Defaults());
        }
    }

    private void RefreshGameSpeed()
    {
        _savedSpeed = SavedGameSpeed();
        _speedError = "";
        GameSpeedLine.Text = GameSpeed.SpeedLine(_savedSpeed);
        GameSpeedLine.Foreground = new SolidColorBrush(_savedSpeed.TwoTimesOn ? Color.FromRgb(0x8F, 0xBF, 0x86) : Color.FromRgb(0xE2, 0xC2, 0x7A));
        GameSpeedBattleLine.Text = GameSpeed.BattleLine(_savedSpeed);
        GameSpeedButton.Content = GameSpeed.SpeedButton(_savedSpeed);
        GameSpeedBattleButton.Content = GameSpeed.BattleButton(_savedSpeed);
        ShowGameSpeedRestart(_speedRunning);
    }

    // Runs with every state refresh, so Restart server shows only while a server runs.
    private void ShowGameSpeedRestart(bool running)
    {
        _speedRunning = running;
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
        var shown = (FastForwardBox.IsChecked, BattleSpeedLimitBox.IsChecked);
        FastForwardBox.IsChecked = wanted.FastForward;
        BattleSpeedLimitBox.IsChecked = wanted.BattleSpeedLimit;
        if (!SaveSettingsPage(fromSettingsPage: false))
        {
            // Put the Settings page back as it was; the reason is in the status line, shown here too.
            FastForwardBox.IsChecked = shown.Item1;
            BattleSpeedLimitBox.IsChecked = shown.Item2;
            _speedError = Coodsz.Ui.UiLanguage.English(ControlStatus);
            ShowGameSpeedRestart(_speedRunning);
            return;
        }

        // A server that was already running when this tool opened started with what was saved before.
        if (_speedRunning)
            _speedAtStart ??= before;
        RefreshGameSpeed();
    }
}
