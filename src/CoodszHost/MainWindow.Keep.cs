using System.Windows;
using BannerlordCoopLauncher.Core.Keep;
using BannerlordCoopLauncher.Core.Server;
using CoodszHost.Keep;
using CoodszHost.Services;

namespace CoodszHost;

// The CoopKeep style Cheats page, player profile and Items window. The AD1259 build has the same
// Cheats page with Coop's own coop.debug cheats only (CoodszServerTools.CheatsUseTools) and no profile.
public partial class MainWindow
{
    private void StartKeep()
    {
        KeepCheats.SavedGamePath = () => _settings.GamePath;
        PictureBuilder.SavedGamePath = () => _settings.GamePath;
        PictureBuilder.SearchFailureLog = reason => _actions.Note("Find Bannerlord", reason);
        KeepCheats.UpdateGameFolderButton();
        KeepCheats.SaveGamePath = root =>
        {
            _settings.GamePath = root;
            HostSettings.Save(SettingsPath, _settings);
            // A game folder picked now may be a newer Bannerlord, so the line on Home and under Start server follows it.
            // Find Bannerlord calls this on the window thread; any other thread queues it and never waits on that thread.
            void Follow()
            {
                _gameCoop = GameModulesCoop(out _newerGame);
                ShowMadeFor();
            }

            if (Dispatcher.CheckAccess())
                Follow();
            else
                Dispatcher.BeginInvoke(() => Follow());
        };
        var keep = new KeepContext(this, _session,
            () => ReadLivePlayers().Select(player => new LivePlayerInfo(player.Name, player.SteamId)).ToList(),
            () => _session.IsRunning && _launchedSave.Length > 0 ? _launchedSave : _settings.Current().SaveName)
        {
            // The AD1259 build's Cheats page sends coop.debug commands only.
            ToolsModule = CoodszServerTools.CheatsUseTools,
            Remember = Remember,
            Ban = BanBySteamId,
            ReadFavourites = () => _settings.FavouriteCheats,
            SaveFavourites = ids =>
            {
                _settings.FavouriteCheats = ids.ToList();
                HostSettings.Save(SettingsPath, _settings);
            },
            OpenInCheats = target =>
            {
                ShowPage("Cheats");
                KeepCheats.Pick(target);
            },
        };
        if (Edition.IsAD1259)
        {
            // Home, Players, Map, Encyclopedia and the player profile are Coodsz Host only.
            OpenProfileButton.Visibility = Visibility.Collapsed;
            KeepCheats.Attach(keep);
            return;
        }

        KeepContext.Current = keep;
        KeepCheats.Attach(keep);
        StartKeepPages();
    }

    /// <summary>Adds a SteamID64 to the ban list the server reads; returns the line to show.</summary>
    private string BanBySteamId(string steamId, string name)
    {
        // The line is on screen: Player 1, Player 2 in streamer mode.
        name = StreamerNames.Name(name, Streamer);
        if (!SteamBanFile.IsSteamId64(steamId))
            return name + " has no SteamID64 yet, so the ban list cannot store them.";
        var ids = ReadBanIds();
        if (!ids.Contains(steamId))
            ids.Add(steamId);
        WriteBanIds(ids);
        RefreshBans();
        return "Banned " + name + ".";
    }

    /// <summary>Open profile next to the player box: finds the selected player's hero and opens the profile.</summary>
    private async void OpenProfile_Click(object sender, RoutedEventArgs e)
    {
        if (KeepContext.Current is null)
            return;
        if (SelectedPlayer() is not { } player)
        {
            GameTimeStatus.Text = "Select a connected player.";
            return;
        }

        OpenProfileButton.IsEnabled = false;
        try
        {
            await OpenProfileFor(player.Name, player.SteamId);
        }
        finally
        {
            OpenProfileButton.IsEnabled = true;
        }
    }
}
