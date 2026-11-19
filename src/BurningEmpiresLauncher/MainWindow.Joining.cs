using System.Globalization;
using System.Windows;
using BannerlordCoopLauncher.Core.Joining;

namespace BurningEmpiresLauncher;

// Join your own server and the join guide, the same as in Coodsz Coop and Friends and the TAOM launcher.
public partial class MainWindow
{
    private LocalServerInfo? _localServer;
    private IReadOnlyList<AdapterAddress> _adapters = Array.Empty<AdapterAddress>();
    private DateTime _localServerReadAt = DateTime.MinValue;

    private bool UseDirect => DirectPanel.Visibility == Visibility.Visible;

    // The Burning Empires server tool starts the engine itself, so no command line names its Data
    // folder. Join your own server reads server-config.json (port and password) from the Data folder
    // in be-server.json, next to this launcher or in a folder beside it.
    private static void ReadOwnServerFromBeData() =>
        LocalServer.ExtraDataFolders = () => BurningEmpires.Core.BeServerFolders.OnThisPc(AppContext.BaseDirectory)
            .Select(folder => folder.DataRoot)
            .OfType<string>();

    private async Task ReadLocalServerAsync()
    {
        // Keep a found server for 10 seconds, but look again when none was found, so a server
        // started a moment ago shows up.
        if (_localServer != null && DateTime.UtcNow - _localServerReadAt < TimeSpan.FromSeconds(10))
            return;
        _localServerReadAt = DateTime.UtcNow;
        try
        {
            var (local, adapters) = await Task.Run(() => (LocalServer.Detect(), NetworkAdapters.Read()));
            _localServer = local;
            _adapters = adapters;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or System.Net.NetworkInformation.NetworkInformationException)
        {
        }
    }

    private async Task ShowOwnServerHintAsync(bool fill)
    {
        await ReadLocalServerAsync();
        var hint = LocalServer.Suggest(UseDirect, HostBox.Text, _localServer, _adapters.Select(a => a.Address));
        if (fill && hint is { Fill: true })
        {
            HostBox.Text = hint.Address;
            PortBox.Text = hint.Port.ToString(CultureInfo.InvariantCulture);
            Log(hint.Note);
        }

        UpdateOwnServerNote();
    }

    private void UpdateOwnServerNote()
    {
        if (OwnServerNote == null)
            return;
        if (!UseDirect)
        {
            OwnServerNote.Visibility = Visibility.Collapsed;
            return;
        }

        var hint = LocalServer.Suggest(true, HostBox.Text, _localServer, _adapters.Select(a => a.Address));
        var text = hint?.Note ?? (_localServer == null ? LocalServer.VpnHint(_adapters) : null);
        OwnServerNote.Text = text ?? "";
        OwnServerNote.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
    }

    private bool _joinChecking;
    private CancellationTokenSource? _joinCheck;

    /// <summary>
    /// Direct join: checks the server answers before the game starts (<see cref="JoinPreflight"/>).
    /// An answer goes straight on; otherwise one prompt. True to start the game.
    /// </summary>
    private async Task<bool> CheckDirectJoinAsync(string host, int port)
    {
        if (_joinChecking)
            return false;
        _joinChecking = true;
        UpdateConnectButton();
        // Closing the window stops a check that is still waiting for an answer.
        var stop = new CancellationTokenSource();
        _joinCheck = stop;
        Closed += StopJoinCheck;
        var asked = false;
        JoinCheckChoice choice;
        try
        {
            ShowJoin("Checking that the server answers...", problem: false);
            choice = await JoinPreflight.DecideAsync(host, port, async () =>
            {
                await ReadLocalServerAsync();
                return await JoinPreflight.CheckAsync(host, port, _localServer != null, stop.Token);
            }, check =>
            {
                asked = true;
                ShowJoin(check.Message);
                _actions.Note("Join check", check.Outcome + ". " + host + ":" + port.ToString(CultureInfo.InvariantCulture) + ".");
                return Coodsz.Ui.JoinCheckWindow.Ask(this, check);
            });
        }
        finally
        {
            Closed -= StopJoinCheck;
            _joinCheck = null;
            stop.Dispose();
            _joinChecking = false;
            UpdateConnectButton();
        }

        switch (choice)
        {
            case JoinCheckChoice.Connect:
                if (!asked)
                    ShowJoin("The Coop server answered. Starting the game.", problem: false);
                return true;
            case JoinCheckChoice.SteamLobby:
                SetJoinMode(steam: true);
                // Where the player looks next, in place of any yellow join text.
                JoinStatus.Text = JoinPreflight.SteamSwitchHint;
                JoinStatus.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x8F, 0xBF, 0x86));
                SteamBox.Focus();
                return false;
            default:
                Log("Connect was cancelled. Nothing was changed.");
                return false;
        }
    }

    private void StopJoinCheck(object? sender, EventArgs e) => _joinCheck?.Cancel();

    private async void JoinHelp_Click(object sender, RoutedEventArgs e)
    {
        await ReadLocalServerAsync();
        var port = int.TryParse(PortBox.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var typed) && typed is >= 1 and <= 65535
            ? typed
            : _localServer?.Port ?? LocalServer.DefaultPort;
        Coodsz.Ui.JoinGuideWindow.Show(this, new JoinFacts
        {
            IsHost = false,
            Port = port,
            Adapters = _adapters,
            LocalServer = _localServer,
            ClientTool = "Coodsz Burning Empires Client Launcher",
            ServerTool = "Coodsz Burning Empires Server Tool",
            SteamOption = "Advertise through Steam",
            Taom = false
        });
    }
}
