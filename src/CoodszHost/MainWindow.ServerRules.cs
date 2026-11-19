using System.IO;
using System.Windows;
using BannerlordCoopLauncher.Core.Server;
using CoodszHost.Services;

namespace CoodszHost;

// Server rules on the Settings page, in the collapsed Advanced: server rules card (both builds, like
// Max clans per kingdom). Save settings writes them
// to coodsz-server-tools.json. The module reads the unstuck rule, the offline XP freeze and the Always
// kingdom protection within a few seconds (compat\CoodszServerTools\src\ServerRules.cs and
// KingdomProtection.cs). Pause on join runs here, from the @DS@ players events
// (BannerlordCoopLauncher.Core\Server\JoinPause.cs). The kingdom protection dropdown also shows Coop's
// modOptions.blockAiWarDeclarationsOnOfflinePlayers, so that is left out of "Show more Coop settings".
// Until the host picks a kingdom choice, the dropdown follows Coop's option as the shown save has it (on
// gives Only while their players are offline, off or missing gives Off) and Start never changes it. Once the
// host picks one, Save settings and every Start write Coop's option to match (on unless the choice is Off).
public partial class MainWindow
{
    private readonly JoinPause _joinPause = new();
    private bool _pauseOnJoin = HostServerRules.Defaults.PauseOnJoin;
    // Pause and resume go out one at a time and in order: a resume never overtakes its pause, and the host's own time
    // commands go out after a step on its way (JoinPauseGate).
    private JoinPauseGate? _joinGate;
    private JoinPauseGate JoinGate => _joinGate ??= new JoinPauseGate(_joinPause);

    private static string ServerRulesPath => ClanCapFile.PathIn(ServerSession.DocumentsData);

    // Called once from the constructor.
    private void StartServerRules()
    {
        ServerRulesArea.Visibility = Visibility.Visible;
        foreach (var label in ServerRulesFile.UnstuckLabels)
            UnstuckBox.Items.Add(label);
        SettingChoiceLabel.SetChoiceLabels(UnstuckBox, true);
        UnstuckBox.SelectedIndex = 0;
        foreach (var label in ServerRulesFile.KingdomProtectionLabels)
            KingdomProtectionBox.Items.Add(label);
        SettingChoiceLabel.SetChoiceLabels(KingdomProtectionBox, true);
        KingdomProtectionBox.SelectedIndex = 0;
        LoadServerRules();
    }

    // Coop's offline war option as the Settings page's save has it, for a host with no kingdom pick.
    private bool _kingdomCoopOption;

    private KingdomProtectionChoice KingdomProtectionFromPage() =>
        (KingdomProtectionChoice)Math.Max(0, KingdomProtectionBox.SelectedIndex);

    /// <summary>From FillCoopSettings: with no kingdom pick, the dropdown shows the save's own Coop option.</summary>
    private void ShowKingdomProtection(IReadOnlyDictionary<string, object> coopValues)
    {
        _kingdomCoopOption = ServerRulesFile.CoopOfflineWarIn(coopValues);
        // Before StartServerRules filled the dropdown, LoadServerRules shows it.
        if (KingdomProtectionBox.Items.Count == 0)
            return;
        var picked = ServerRulesFile.Read(ServerRulesPath).KingdomProtection;
        if (picked == null)
            KingdomProtectionBox.SelectedIndex = (int)ServerRulesFile.ShownKingdomChoice(null, _kingdomCoopOption);
    }

    /// <summary>
    /// Right before Start, after the save's kept settings: once the host picked a kingdom choice, Coop's offline
    /// war option in mod-config.json follows it, whatever an older kept profile says. With no pick it is left alone.
    /// </summary>
    private void ApplyKingdomProtection()
    {
        if (ServerRulesFile.Read(ServerRulesPath).KingdomProtection is not { } picked)
            return;
        try
        {
            var store = new BannerlordCoopLauncher.Core.Configuration.ModConfigStore(ServerSession.ModConfigPath);
            if (!store.Exists)
                return;
            var on = ServerRulesFile.CoopOfflineWarValue(picked);
            if (store.Write(new Dictionary<string, object> { [ServerRulesFile.CoopOfflineWarOption] = on }, ServerCoopVersion()) > 0)
                Append(on
                    ? "Turned on Coop's protection for player kingdoms while their players are offline, as chosen in Advanced: server rules."
                    : "Turned off Coop's protection for player kingdoms while their players are offline, as chosen in Advanced: server rules.");
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            Append("Could not set Coop's protection for player kingdoms in mod-config.json. " + ex.Message);
        }
    }

    private void ServerRulesToggle_Click(object sender, RoutedEventArgs e) =>
        ServerRulesPanel.Visibility = ServerRulesPanel.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;

    private void LoadServerRules()
    {
        var rules = ServerRulesFile.Read(ServerRulesPath);
        UnstuckBox.SelectedIndex = (int)rules.Unstuck;
        PauseOnJoinBox.IsChecked = rules.PauseOnJoin;
        FreezeOfflineXpBox.IsChecked = rules.FreezeOfflineXp;
        if (rules.KingdomProtection == null)
            _kingdomCoopOption = ServerRulesFile.CoopOfflineWarIn(CoopSettingsFor(SettingsSaveBox.SelectedItem as string, out _));
        KingdomProtectionBox.SelectedIndex = (int)ServerRulesFile.ShownKingdomChoice(rules.KingdomProtection, _kingdomCoopOption);
        _pauseOnJoin = rules.PauseOnJoin;
    }

    private HostServerRules ServerRulesFromPage() =>
        new((UnstuckChoice)Math.Max(0, UnstuckBox.SelectedIndex), PauseOnJoinBox.IsChecked == true, FreezeOfflineXpBox.IsChecked == true,
            ServerRulesFile.KingdomChoiceToSave(KingdomProtectionFromPage(), ServerRulesFile.Read(ServerRulesPath).KingdomProtection, _kingdomCoopOption));

    private void SaveServerRules(HostServerRules rules)
    {
        var before = ServerRulesFile.Read(ServerRulesPath);
        _pauseOnJoin = rules.PauseOnJoin;
        if (rules == before && File.Exists(ServerRulesPath) && File.ReadAllText(ServerRulesPath).Contains(ServerRulesFile.UnstuckKey, StringComparison.Ordinal))
            return;
        ServerRulesFile.Write(ServerRulesPath, rules);
        if (rules.Unstuck != before.Unstuck)
            Append(rules.Unstuck switch
            {
                UnstuckChoice.NotInFights => "Unstuck for players: not in fights, as a prisoner or near enemies, and once every 5 minutes. The server uses it within a few seconds.",
                UnstuckChoice.HostOnly => "Unstuck for players: host only. Players can't use coop.unstuck; you still can from the console. The server uses it within a few seconds.",
                _ => "Unstuck for players: allowed, like Coop on its own.",
            });
        if (rules.PauseOnJoin != before.PauseOnJoin)
            Append(rules.PauseOnJoin ? "Pause on join is on." : "Pause on join is off.");
        if (rules.FreezeOfflineXp != before.FreezeOfflineXp)
            Append(rules.FreezeOfflineXp ? "Offline XP freeze is on." : "Offline XP freeze is off.");
        if (rules.KingdomProtection != null && rules.KingdomProtection != before.KingdomProtection)
            Append(rules.KingdomProtection switch
            {
                KingdomProtectionChoice.Always => "Player kingdoms are protected from AI wars always, online or offline. The server uses it within a few seconds.",
                KingdomProtectionChoice.Off => "Player kingdoms are not protected from AI wars. Restart the server so Coop uses it.",
                _ => "Player kingdoms are protected from AI wars only while their players are offline, with Coop's own protection. Restart the server so Coop uses it.",
            });
    }

    // ---- pause on join -----------------------------------------------------------------------

    private bool PauseOnJoinActive => _pauseOnJoin;

    // Every @DS@ players event while the server runs.
    private void NoteJoinPause(IReadOnlyList<ConnectedPlayer> players)
    {
        DoJoinPause(_joinPause.Update(players, DateTime.UtcNow, PauseOnJoinActive && _serving));
    }

    // The tool's one second clock, so the 3 minute give up comes without a new player list.
    private void TickJoinPause()
    {
        if (!_joinPause.Holding && _joinPause.Waiting.Count == 0)
            return;
        // A server that stopped took its pause with it; the next start is a new session.
        if (!_serving)
        {
            _joinPause.Reset();
            return;
        }

        DoJoinPause(_joinPause.Tick(DateTime.UtcNow, PauseOnJoinActive && _serving));
    }

    // A time command the host sends himself wins over a join pause, and sets or ends Play hold.
    private void NoteHostCommand(string command)
    {
        // Play hold reads this window's own server output, so a server it did not start holds nothing.
        if (_session.OwnsProcess)
            _playHold.HostSent(command);
        else
            _playHold.Stop();
        _joinPause.HostSent(command);
    }

    private const string HostCommandWaits = "Waiting for the join pause to finish, then sending your command.";

    // True while a host time command waits behind a join pause step, so Pause game and Resume game are not pressed twice.
    private bool _hostCommandWaiting;

    // The host's typed or button command, after NoteHostCommand. A time command waits for a join pause step already on
    // its way (JoinPauseGate), so the host's speed always goes out after it and is never overwritten by it; a step still
    // waiting sees the moved HostCommandCount and is dropped. A step can take up to about 15 seconds (three sends of at
    // most 5), so a wait says so.
    private async Task<string> SendHostCommand(string command)
    {
        var waits = JoinGate.Busy && JoinPause.HostMode(command) != null;
        if (waits)
        {
            _hostCommandWaiting = true;
            Remember(HostCommandWaits);
            GameTimeStatus.Text = HostCommandWaits;
        }

        var run = _session.RunId;
        var entered = await JoinGate.EnterHost(command);
        try
        {
            // A Restart while it waited: the server it was meant for is gone, so it never goes to the new one.
            if (_session.RunId != run)
                return TaomCoop.Core.ServerCommandQueue.RunEnded;
            return await Task.Run(() => _session.Send(command));
        }
        finally
        {
            if (entered)
                JoinGate.Release();
            if (waits)
                _hostCommandWaiting = false;
        }
    }

    private const string HostPauseKept = "A player finished joining and the server let the game run again, so your Pause game was sent again.";
    private const string HostPauseNotKept = "A player finished joining and the server let the game run again. Your pause could not be sent again, so press Pause game.";

    // Coop's server lets time run 5 seconds after any player enters the campaign, also over the host's Pause game
    // (JoinPause.HostPauseUndone), so his pause goes out again. It takes its turn in the gate like his own commands:
    // a Resume game he pressed meanwhile wins, and a Restart meanwhile sends nothing. Players who enter together get one
    // pause and one line: only one waits at a time.
    private bool _keepPauseQueued;

    private async void KeepHostPause(string line)
    {
        if (_keepPauseQueued)
            return;
        _keepPauseQueued = true;
        var run = _session.RunId;
        var entered = false;
        try
        {
            entered = await JoinGate.EnterHost(ServerSession.PauseCommand);
            // A server stopping, or one whose console is blocked, has nothing to pause.
            if (_session.RunId != run || !_session.CanSendCommands || !_joinPause.HostPauseUndone(line))
                return;
            var sent = await Task.Run(() => _session.SendDetailed(ServerSession.PauseCommand, TimeSpan.FromSeconds(5)));
            Append(sent.Succeeded ? HostPauseKept : HostPauseNotKept);
        }
        catch (Exception ex)
        {
            Append(HostPauseNotKept + " " + ex.Message);
        }
        finally
        {
            if (entered)
                JoinGate.Release();
            _keepPauseQueued = false;
        }
    }

    private async void DoJoinPause(JoinPauseStep step)
    {
        if (step == JoinPauseStep.None)
            return;
        // A resume that waited behind a pause on its way never overwrites a speed the host sent meanwhile.
        if (!await JoinGate.EnterStep(step))
            return;
        try
        {
            if (step == JoinPauseStep.Pause)
            {
                var mode = await Task.Run(() => _session.SendDetailed(JoinPause.GetModeCommand, TimeSpan.FromSeconds(5)));
                if (!_joinPause.Paused(mode.Succeeded ? mode.Text : null, DateTime.UtcNow))
                    return;
                var paused = await Task.Run(() => _session.SendDetailed(JoinPause.PauseCommand, TimeSpan.FromSeconds(5)));
                if (!paused.Succeeded)
                {
                    _joinPause.PauseFailed();
                    return;
                }

                // The host set a speed while the pause was on its way (it may have landed after his
                // choice, answered or not): his speed goes back on at once, nothing waits to resume.
                var hostSpeed = _joinPause.PauseLanded();
                if (hostSpeed != null)
                {
                    var put = await Task.Run(() => _session.SendDetailed(hostSpeed, TimeSpan.FromSeconds(5)));
                    Append(put.Succeeded
                        ? "You set the speed while a join pause was going out, so the game runs at " + SpeedName(_joinPause.HostChoice ?? JoinPause.NormalMode) + " as you chose."
                        : "Pause on join could not put your speed back. Press Resume game on the Dashboard.");
                    return;
                }

                if (!_joinPause.Holding)
                    return;

                Append("Paused the game while " + string.Join(", ", _joinPause.Waiting.Select(Shown)) + " joins. It goes back to "
                       + SpeedName(_joinPause.ResumeMode) + " when they are on the map (at most 3 minutes).");
            }
            else
            {
                var gaveUp = _joinPause.GaveUp;
                var resumed = await Task.Run(() => _session.SendDetailed(_joinPause.ResumeCommand, TimeSpan.FromSeconds(5)));
                Append(!resumed.Succeeded
                    ? "Pause on join could not put the speed back. Press Resume game on the Dashboard."
                    : gaveUp
                        ? "A player is still loading after 3 minutes, so the game runs again at " + SpeedName(_joinPause.ResumeMode) + "."
                        : "Everyone is on the map, so the game runs again at " + SpeedName(_joinPause.ResumeMode) + ".");
            }
        }
        catch (Exception ex)
        {
            _joinPause.PauseFailed();
            Append("Pause on join: " + ex.Message);
        }
        finally
        {
            JoinGate.Release();
        }
    }

    // A joining player has no name yet; streamer mode hides names.
    private string Shown(string key) =>
        key.StartsWith('#') ? "a new player" : _settings.StreamerMode ? "a player" : key;

    private static string SpeedName(string mode) => mode switch
    {
        JoinPause.FastMode => "2x speed",
        _ => "normal speed",
    };
}
