using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BannerlordCoopLauncher.Core.Joining;
using BannerlordCoopLauncher.Core.Keep;
using BannerlordCoopLauncher.Core.Localization;
using BannerlordCoopLauncher.Core.Steam;
using CoodszHost.Services;

namespace CoodszHost;

// Easy joining: the addresses friends can use, Copy invite, automatic router port opening while
// the server runs, the Windows Firewall rule, and the join guide. The logic is in Core\Joining.
public partial class MainWindow
{
    private readonly RouterPortKeeper _routerPort = new();
    private IReadOnlyList<AdapterAddress> _adapters = Array.Empty<AdapterAddress>();
    private FirewallState _firewall = FirewallState.Unknown;
    private string? _steamName;
    private bool _routerTried;
    private bool _routerBusy;
    private DateTime? _notRunningSince;
    private bool _joiningReady;
    private bool _lastRunning;
    private string _joiningShown = "";

    private static readonly SolidColorBrush JoinGood = new(Color.FromRgb(143, 203, 155));
    private static readonly SolidColorBrush JoinMuted = new(Color.FromRgb(142, 160, 174));
    private static readonly SolidColorBrush JoinWarn = new(Color.FromRgb(224, 177, 90));

    private string ClientToolName => Edition.IsAD1259 ? "Coodsz AD1259 Client Launcher" : "Coodsz Coop Client Launcher";

    // The listening program is the server engine (engine\dotnet\dotnet.exe under DedicatedServer).
    private string? ServerProgram
    {
        get
        {
            if (string.IsNullOrWhiteSpace(ServerRoot))
                return null;
            var engine = Path.Combine(ServerRoot, "engine", "dotnet", "dotnet.exe");
            return File.Exists(engine) ? engine : null;
        }
    }

    private int JoinPort => _settings.Current().CoopPort > 0 ? _settings.Current().CoopPort : LocalServer.DefaultPort;

    private void StartJoining()
    {
        RouterPortBox.IsChecked = _settings.OpenRouterPort;
        _routerPort.Changed += result => Dispatcher.BeginInvoke(() =>
        {
            SteamLobbyForSharedAddress(result);
            ShowJoining();
        });
        Closed += (_, _) =>
        {
            if (_routerPort.IsOpen)
                _actions.Note("Router port", "Closing on exit");
            _routerPort.CloseNow(TimeSpan.FromSeconds(4));
            _routerPort.Dispose();
        };
        _joiningReady = true;
        _ = DetectJoiningAsync();
    }

    private async Task DetectJoiningAsync()
    {
        var port = JoinPort;
        var program = ServerProgram;
        var (adapters, firewall, steamName) = await Task.Run(() =>
        {
            IReadOnlyList<AdapterAddress> found;
            try
            {
                found = NetworkAdapters.Read();
            }
            catch (Exception)
            {
                found = Array.Empty<AdapterAddress>();
            }

            return (found, FirewallRules.Check(FirewallRules.Read(), port, program), SteamRegistry.TryGetPersonaName());
        });
        _adapters = adapters;
        _firewall = firewall;
        _steamName = steamName;
        ShowJoining();
    }

    private JoinFacts JoinFactsNow() => JoinFactsNow(_session.IsRunning);

    private JoinFacts JoinFactsNow(bool running)
    {
        _lastRunning = running;
        return new JoinFacts
        {
            IsHost = true,
            Port = JoinPort,
            SteamLobbyOn = SteamBox.IsChecked == true,
            SteamName = _steamName,
            PasswordNeeded = !string.IsNullOrWhiteSpace(PasswordBox.Text),
            Adapters = _adapters,
            // Only a mapping that is open now, or the answer to the last try when it failed.
            Router = _routerPort.IsOpen || _routerPort.Last?.IsOpen == false ? _routerPort.Last : null,
            PortOpensOnStart = _settings.OpenRouterPort && !running && _routerPort.Last?.IsOpen != false,
            Firewall = _firewall,
            ClientTool = ClientToolName,
            ServerTool = Edition.Name,
            Map = MapBox.SelectedItem as string
        };
    }

    private void ShowJoining() => ShowJoining(_session.IsRunning, force: true);

    /// <summary>Fills the card. From the one second clock it only redraws when something changed.</summary>
    private void ShowJoining(bool running, bool force)
    {
        if (!_joiningReady)
            return;
        var facts = JoinFactsNow(running);
        var addresses = JoinPlan.Addresses(facts);
        var shown = string.Join("|", addresses) + "|" + RouterStatusText() + "|" + _firewall + "|" + facts.SteamLobbyOn + "|" + facts.Map + "|" + Streamer;
        if (!force && shown == _joiningShown)
            return;
        _joiningShown = shown;
        // Streamer mode: the rows show dots; Copy still finds the real address (CopyAddress_Click).
        JoinAddressList.ItemsSource = Streamer
            ? addresses.Select(item => item with { Value = StreamerMask.Value(item.Value, true), Note = StreamerMask.Text(item.Note, true, _steamName) }).ToList()
            : addresses;
        var best = JoinGuide.Choices(facts).FirstOrDefault(choice => choice.WorksNow && choice.Topic != GuideTopic.Host);
        JoinBestText.Text = best == null
            ? "No easy way found yet. Turn on Steam advertise, use Radmin VPN, or open the port. Click How will friends join?"
            : "Easiest way right now is " + best.Title + ". " + StreamerMask.Text(best.Status, Streamer, _steamName);
        JoinBestText.Foreground = best == null ? JoinWarn : JoinGood;

        RouterPortStatus.Text = RouterStatusText();
        RouterPortStatus.Foreground = _routerPort.IsOpen ? JoinGood : _routerPort.Last is { IsOpen: false } ? JoinWarn : JoinMuted;
        FirewallStatus.Text = FirewallRules.Describe(_firewall, facts.Port);
        FirewallStatus.Foreground = _firewall switch
        {
            FirewallState.Allowed => JoinGood,
            FirewallState.Blocked => new SolidColorBrush(Color.FromRgb(232, 120, 108)),
            _ => JoinMuted
        };
    }

    private string RouterStatusText()
    {
        if (!_settings.OpenRouterPort)
            return "Off. Friends use Steam lobby, a VPN, or a port you forward by hand.";
        if (_routerBusy)
            return "Opening the port on your router...";
        if (_routerPort.IsOpen && _routerPort.Last != null)
            return _routerPort.Last.Status;
        if (_routerPort.Last is { IsOpen: false } failed)
            return failed.Status;
        if (_routerPort.Last != null && !_lastRunning)
            return "The port was closed on your router because the server stopped.";
        return "The port opens on your router when you start the server.";
    }

    /// <summary>Called every second from RefreshState: opens the port while the server runs, closes it after it stops.</summary>
    private void KeepRouterPort(bool running)
    {
        if (!_joiningReady)
            return;
        ShowJoining(running, force: false);
        if (_routerBusy)
            return;
        if (running)
        {
            _notRunningSince = null;
            if (_settings.OpenRouterPort && !_routerTried)
                _ = OpenRouterPortAsync();
            return;
        }

        // A restart stops the server for a moment, so wait before closing.
        _notRunningSince ??= DateTime.UtcNow;
        if (DateTime.UtcNow - _notRunningSince.Value < TimeSpan.FromSeconds(10))
            return;
        _routerTried = false;
        if (_routerPort.IsOpen)
            _ = CloseRouterPortAsync("Closed the port on your router because the server stopped.");
    }

    private async Task OpenRouterPortAsync()
    {
        _routerTried = true;
        _routerBusy = true;
        ShowJoining();
        try
        {
            var result = await _routerPort.OpenAsync(JoinPort, RouterPortMapper.DefaultDescription);
            _actions.Note("Router port", result.LogLine);
            Append(result.Status);
        }
        catch (Exception ex)
        {
            _actions.Note("Router port", "Failed: " + ex.Message);
        }
        finally
        {
            _routerBusy = false;
            ShowJoining();
        }
    }

    private async Task CloseRouterPortAsync(string message)
    {
        _routerBusy = true;
        try
        {
            await _routerPort.CloseAsync();
            _actions.Note("Router port", "Closed " + JoinPort + " UDP");
            Append(message);
        }
        finally
        {
            _routerBusy = false;
            ShowJoining();
        }
    }

    private async void RouterPort_Changed(object sender, RoutedEventArgs e)
    {
        if (!_joiningReady)
            return;
        var on = RouterPortBox.IsChecked == true;
        if (_settings.OpenRouterPort != on)
        {
            _settings.OpenRouterPort = on;
            HostSettings.Save(SettingsPath, _settings);
        }

        if (!on && _routerPort.IsOpen)
            await CloseRouterPortAsync("Closed the port on your router.");
        else if (on && _session.IsRunning && !_routerBusy)
            await OpenRouterPortAsync();
        ShowJoining();
    }

    private async void CheckJoining_Click(object sender, RoutedEventArgs e)
    {
        await DetectJoiningAsync();
        if (_settings.OpenRouterPort && _session.IsRunning && !_routerPort.IsOpen && !_routerBusy)
            await OpenRouterPortAsync();
    }

    private void CopyAddress_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not JoinAddress shown)
            return;
        // In streamer mode the row shows dots, so the real address comes from the plan.
        var address = JoinPlan.Addresses(JoinFactsNow()).FirstOrDefault(item => item.Kind == shown.Kind && item.Label == shown.Label) ?? shown;
        var value = address.Value.Length > 0 ? address.Value : address.Label;
        CopyText(value, address.Kind == JoinAddressKind.SteamLobby ? "Copied." : "Copied " + StreamerMask.Value(value, Streamer) + ". The UDP port is " + JoinPort + ".");
    }

    // The steam setting the running server started with; null when this tool did not start it.
    private bool? _steamAtStart;

    private bool SteamLobbyLive => _session.IsRunning && _steamAtStart is { } started ? started : SteamBox.IsChecked == true;

    // A shared address (CGNAT) means only Steam lobby reaches the server, so it is the smart default
    // for the next start. No new setting: the Steam advertise box is ticked and saved. Only once, so a
    // host who turns it off again keeps it off.
    private void SteamLobbyForSharedAddress(PortOpenResult? result)
    {
        if (result?.Outcome != PortOpenOutcome.SharedAddress || SteamBox.IsChecked == true || _settings.SteamLobbyAutoOn)
            return;
        if (!TurnOnSteamLobby())
            return;
        Append(JoinPlan.SteamLobbyTurnedOn);
        _settings.SteamLobbyAutoOn = true;
        try
        {
            HostSettings.Save(SettingsPath, _settings);
        }
        catch (Exception ex)
        {
            Append("Could not save the settings: " + ex.Message);
        }
    }

    // Runs from the router callback and Copy invite, so a broken server-config.json only gets a log line.
    private bool TurnOnSteamLobby()
    {
        try
        {
            if (File.Exists(ServerSession.ServerConfigPath))
                JsoncEditor.Write(ServerSession.ServerConfigPath, JsoncEditor.SetBool(JsoncEditor.Read(ServerSession.ServerConfigPath), "steam", true));
            SteamBox.IsChecked = true;
            _actions.Note("Steam lobby", "Turned on for a shared address");
            return true;
        }
        catch (Exception ex)
        {
            Append("Could not turn on Steam lobby: " + ex.Message);
            return false;
        }
    }

    private async void CopyInvite_Click(object sender, RoutedEventArgs e)
    {
        await DetectJoiningAsync();
        var facts = JoinFactsNow() with { SteamLobbyOn = SteamLobbyLive };
        // Never a Steam lobby invite while it is off: turn it on first (a running server restarts for it).
        if (JoinPlan.NeedsSteamLobby(facts))
        {
            if (!_session.IsRunning)
            {
                if (TurnOnSteamLobby())
                    facts = facts with { SteamLobbyOn = true };
            }
            else if (ConfirmWindow.Ask(this, JoinPlan.SteamLobbyOffTitle, JoinPlan.SteamLobbyOffBody, okText: JoinPlan.SteamLobbyOffButton) && TurnOnSteamLobby())
            {
                // RestartServerAsync refuses before its first await when another server action runs,
                // so the task is already done with false then; the invite stays without Steam lobby.
                var restart = RestartServerAsync();
                if (!restart.IsCompleted || restart.Result)
                {
                    CopyText(JoinPlan.Invite(facts with { SteamLobbyOn = true }, Translator.T), JoinPlan.InviteCopiedRestart);
                    await restart;
                    return;
                }
            }
        }

        CopyText(JoinPlan.Invite(facts, Translator.T), JoinPlan.InviteCopied(facts));
    }

    private void CopyText(string text, string done)
    {
        try
        {
            Clipboard.SetText(text);
            SetControl(done, done: true);
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            SetControl("Could not copy. Another program is using the clipboard. Try again.", failed: true);
        }
    }

    private async void HowJoin_Click(object sender, RoutedEventArgs e)
    {
        await DetectJoiningAsync();
        Coodsz.Ui.JoinGuideWindow.Show(this, JoinFactsNow());
    }

    private async void JoinGuide_Click(object sender, RoutedEventArgs e)
    {
        await DetectJoiningAsync();
        Coodsz.Ui.JoinGuideWindow.Show(this, JoinFactsNow());
    }

    // The Dashboard and Settings both have Allow through firewall: one Windows prompt at a time, from the first click on.
    private bool _firewallBusy;

    private async void Firewall_Click(object sender, RoutedEventArgs e)
    {
        if (_firewallBusy)
            return;
        _firewallBusy = true;
        try
        {
            await AllowThroughFirewallAsync();
        }
        finally
        {
            _firewallBusy = false;
        }
    }

    private async Task AllowThroughFirewallAsync()
    {
        var port = JoinPort;
        var program = ServerProgram;
        _firewall = await Task.Run(() => FirewallRules.Check(FirewallRules.Read(), port, program));
        if (_firewall == FirewallState.Allowed)
        {
            ShowJoining();
            SetControl(FirewallRules.Describe(_firewall, port), done: true);
            return;
        }

        var body = "Windows asks for permission, then adds an inbound rule that lets friends reach the server on UDP " + port + "."
            + (program != null ? " A second rule allows the server program." : "")
            + " Nothing else changes.";
        if (!ConfirmWindow.Ask(this, "Allow through firewall", body, okText: "Continue"))
            return;
        FirewallButton.IsEnabled = false;
        try
        {
            var outcome = await Task.Run(() => FirewallRules.Add(port, program));
            _actions.Note("Firewall", outcome + " UDP " + port + (program != null ? " and " + program : ""));
            _firewall = await Task.Run(() => FirewallRules.Check(FirewallRules.Read(), port, program));
            var message = FirewallRules.Describe(outcome) + (outcome == FirewallAddOutcome.Added ? " " + FirewallRules.Describe(_firewall, port) : "");
            SetControl(message, done: outcome == FirewallAddOutcome.Added, failed: outcome == FirewallAddOutcome.Failed);
            Append(message);
        }
        finally
        {
            FirewallButton.IsEnabled = true;
            ShowJoining();
        }
    }
}
