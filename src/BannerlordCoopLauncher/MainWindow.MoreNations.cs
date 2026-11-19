using System.Windows;

namespace BannerlordCoopLauncher;

// The More Nations switch. It took the place of the Coodsz and Friends build: on, a setup with no map
// saved uses the More Nations Remastered Map (LauncherSettings.DefaultMap), the way that build did. A
// setup with its own map keeps it either way, and the map picker still picks per setup.
public partial class MainWindow
{
    private bool _fillingMoreNations;

    private void ShowMoreNationsSwitch()
    {
        _fillingMoreNations = true;
        MoreNationsBox.IsChecked = _settings.MoreNations;
        _fillingMoreNations = false;
    }

    private async void MoreNations_Changed(object sender, RoutedEventArgs e)
    {
        if (_fillingMoreNations || !IsLoaded)
            return;
        _settings.MoreNations = MoreNationsBox.IsChecked == true;
        _settings.Save(SettingsPath);
        Log(_settings.MoreNations
            ? "More Nations is on. A setup with no saved map starts on More Nations Remastered Map."
            : "More Nations is off. A setup with no saved map starts on Vanilla campaign Map.");
        // Only a setup with no map saved follows the switch, so only that one needs the map and mods again.
        var setup = _settings.Current();
        if (!setup.Locked && string.IsNullOrWhiteSpace(setup.CampaignMap))
        {
            SelectCampaignMap(setup.CampaignMap);
            ShowMoreNationsNote();
            await RefreshAsync();
        }
    }
}
