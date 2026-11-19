using System.Globalization;
using System.Windows;
using BannerlordCoopLauncher.Core.Joining;

namespace BannerlordCoopLauncher;

// Join your own server and the join guide: when the Coop server runs on this PC, Direct with
// an empty address gets 127.0.0.1 and the server's port; a Radmin or Hamachi adapter gets a hint.
public partial class MainWindow
{
    private LocalServerInfo? _localServer;
    private IReadOnlyList<AdapterAddress> _adapters = Array.Empty<AdapterAddress>();
    private DateTime _localServerReadAt = DateTime.MinValue;
    private bool _joinChecking;

    private async Task ReadLocalServerAsync()
    {
        // Keep a found server for 10 seconds, but look again when none was found, so a server
        // started a moment ago shows up.
        if (_localServer != null && DateTime.UtcNow - _localServerReadAt < TimeSpan.FromSeconds(10))
            return;
        _localServerReadAt = DateTime.UtcNow;
        var (local, adapters) = await Task.Run(() => (LocalServer.Detect(), NetworkAdapters.Read()));
        _localServer = local;
        _adapters = adapters;
    }

    /// <summary>Checks for a server on this PC, fills 127.0.0.1 when asked and the address is empty, and shows the note.</summary>
    private async Task ShowOwnServerHintAsync(bool fill)
    {
        try
        {
            await ReadLocalServerAsync();
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or System.Net.NetworkInformation.NetworkInformationException)
        {
            return;
        }

        var hint = LocalServer.Suggest(UseDirect, DirectHostBox.Text, _localServer, _adapters.Select(a => a.Address));
        if (fill && hint is { Fill: true })
        {
            DirectHostBox.Text = hint.Address;
            DirectPortBox.Text = hint.Port.ToString(CultureInfo.InvariantCulture);
            Log(hint.Note);
        }

        UpdateOwnServerNote();
    }

    private void UpdateOwnServerNote()
    {
        if (!UseDirect)
        {
            OwnServerNote.Visibility = Visibility.Collapsed;
            return;
        }

        var hint = LocalServer.Suggest(true, DirectHostBox.Text, _localServer, _adapters.Select(a => a.Address));
        var text = hint?.Note ?? (_localServer == null ? LocalServer.VpnHint(_adapters) : null);
        OwnServerNote.Text = text ?? "";
        OwnServerNote.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
    }

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
        JoinCheckChoice choice;
        try
        {
            choice = await JoinPreflight.DecideAsync(host, port, () => RunJoinCheckAsync(host, port), check =>
            {
                Log(check.Message);
                NoteAction("Join check", check.Outcome + ". " + host + ":" + port.ToString(CultureInfo.InvariantCulture) + ".");
                return Coodsz.Ui.JoinCheckWindow.Ask(this, check);
            });
        }
        finally
        {
            _joinChecking = false;
            UpdateConnectButton();
        }

        switch (choice)
        {
            case JoinCheckChoice.Connect:
                return true;
            case JoinCheckChoice.SteamLobby:
                SetJoinMode(direct: false);
                BlockReasonText.Text = "";
                SteamSwitchNote.Text = JoinPreflight.SteamSwitchHint;
                SteamSwitchNote.Visibility = Visibility.Visible;
                SteamNameBox.Focus();
                return false;
            default:
                Log("Connect was cancelled. Nothing was changed.");
                return false;
        }
    }

    // The check runs as a busy action; a failure inside becomes the no answer prompt, a cancel starts nothing.
    private async Task<JoinCheckResult> RunJoinCheckAsync(string host, int port)
    {
        JoinCheckResult? check = null;
        await RunBusyAsync(async cancellationToken =>
        {
            try
            {
                SetStatus("Checking", 0xE0, 0xB1, 0x5A, 0x3A, 0x30, 0x1C);
                await ReadLocalServerAsync();
                check = await JoinPreflight.CheckAsync(host, port, _localServer != null, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                check = JoinPreflight.Failed(host, port);
            }

            // Outside the try: a status pill problem must not turn an answer into a no answer prompt.
            if (check.Probe != null)
                ApplyProbe(check.Probe);
        });
        return check ?? throw new OperationCanceledException();
    }

    private async void JoinHelp_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await ReadLocalServerAsync();
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or System.Net.NetworkInformation.NetworkInformationException)
        {
        }

        var port = int.TryParse(DirectPortBox.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var typed) && typed is >= 1 and <= 65535
            ? typed
            : _localServer?.Port ?? LocalServer.DefaultPort;
        Coodsz.Ui.JoinGuideWindow.Show(this, new JoinFacts
        {
            IsHost = false,
            Port = port,
            Adapters = _adapters,
            LocalServer = _localServer,
            ClientTool = LauncherBrand.Name,
            ServerTool = LauncherBrand.ServerTool
        });
    }
}
