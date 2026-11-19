using System.Windows;
using System.Windows.Media;
using BannerlordCoopLauncher.Core.Keep;
using CoodszHost.Keep;
using CoodszHost.Services;

namespace CoodszHost;

// Streamer mode (Coodsz Host only), after CoopKeep's privacy switch: IP addresses, the Realm reward
// code, Steam ids and the join password show as dots on screen. The switch is kept in host.json.
// Copy buttons still copy the real value; only the screen changes.
public partial class MainWindow
{
    /// <summary>True while streamer mode is on. The AD1259 build has no streamer mode.</summary>
    private bool Streamer => !Edition.IsAD1259 && _settings.StreamerMode;

    private void StreamerMode_Click(object sender, RoutedEventArgs e)
    {
        _settings.StreamerMode = !_settings.StreamerMode;
        HostSettings.Save(SettingsPath, _settings);
        ApplyStreamerMode();
        SetControl(Streamer
            ? "Streamer mode is on. Addresses, codes, Steam ids and the password are hidden on screen."
            : "Streamer mode is off.", done: true);
    }

    /// <summary>Shows or hides everything streamer mode covers. Runs on load and on every switch.</summary>
    private void ApplyStreamerMode()
    {
        if (Edition.IsAD1259)
            return;
        var on = Streamer;
        KeepContext.StreamerMode = on;
        StreamerText.Text = on ? "Streamer mode on" : "Streamer mode off";
        StreamerButton.Background = (Brush)FindResource(on ? "Keep.AccentSoft" : "Keep.BgRaised");
        StreamerButton.BorderBrush = (Brush)FindResource(on ? "Keep.Accent" : "Keep.BorderDim");
        StreamerIcon.Stroke = (Brush)FindResource(on ? "Keep.Accent" : "Keep.TextDim");

        // The boxes keep their place; a mask covers them so the value is not on screen.
        foreach (var (box, mask) in new (FrameworkElement Box, FrameworkElement Mask)[]
                 {
                     (ServerAddressBox, AddressMask),
                     (PasswordBox, PasswordMask),
                     (RealmRewardCodeBox, RealmRewardMask),
                 })
        {
            box.Visibility = on ? Visibility.Hidden : Visibility.Visible;
            mask.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        }

        if (_joiningReady)
            ShowJoining();
        RefreshPlayers();
        RefreshBans();
        RefreshDashboard();
        StreamerPreferredHosts(on);
        // Player names on the map, the Cheats page and the profile become Player 1, Player 2.
        KeepMap.SetStreamer(on);
        KeepCheats.StreamerChanged();
    }

    /// <summary>A line for the Logs page, with addresses, Steam ids and the password hidden in streamer mode.</summary>
    private string ForScreen(string line) => Streamer ? StreamerMask.Text(line, true, PasswordBox.Text, _steamName) : line;
}
