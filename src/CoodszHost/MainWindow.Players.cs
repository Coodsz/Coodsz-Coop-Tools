using System.Collections.ObjectModel;
using System.Text;
using System.Windows.Documents;
using TaomCoop.Core;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using BannerlordCoopLauncher.Core.Keep;
using BannerlordCoopLauncher.Core.Launch;
using BannerlordCoopLauncher.Core.Saves;
using BannerlordCoopLauncher.Core.Server;
using BannerlordCoopLauncher.Core.Support;
using BannerlordCoopLauncher.Core.Updates;
using BannerlordCoopLauncher.Core.Modules;
using CoodszHost.Services;
using Microsoft.Win32;

namespace CoodszHost;

public partial class MainWindow
{
    private void OpenCheatList_Click(object sender, RoutedEventArgs e)
    {
        const string cheats = "https://bannerlordcoop.com/cheats?tab=all";
        if (!ShellOpen.TryOpen(cheats, out _))
            Append("Could not open the browser. " + cheats);
    }

    private void RefreshPlayers_Click(object sender, RoutedEventArgs e) => RefreshPlayers();

    private void PlayerBox_DropDownOpened(object sender, EventArgs e) => RefreshPlayers();

    private void RefreshPlayers()
    {
        var players = ReadLivePlayers();
        _livePlayers = players;
        OnlinePlayersText.Text = "Online players: " + players.Count;
        // Streamer mode leaves the SteamID64 out of the box.
        var labels = players.Select(player => PlayerLine(player)).ToList();
        var current = PlayerBox.SelectedItem as string;
        var shown = PlayerBox.Items.Cast<object>().Select(item => item?.ToString() ?? "").ToList();
        if (!labels.SequenceEqual(shown))
        {
            _loading = true;
            PlayerBox.ItemsSource = labels;
            PlayerBox.SelectedItem = current != null && labels.Contains(current) ? current : labels.FirstOrDefault();
            _loading = false;
        }

        if (_page == "Cheats")
        {
            var names = players.Select(player => player.Name).Distinct().ToList();
            KeepCheats.LivePlayersChanged(names);
        }

        if (_page == "Players")
            ShowKeepPlayers();
    }

    private void BanPlayer_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedPlayer() is not LivePlayer player)
        {
            GameTimeStatus.Text = "Select a connected player.";
            return;
        }

        if (!SteamBanFile.IsSteamId64(player.SteamId))
        {
            GameTimeStatus.Text = player.Name + " has no SteamID64 yet, so the ban list cannot store them.";
            return;
        }

        var ids = ReadBanIds();
        if (!ids.Contains(player.SteamId))
            ids.Add(player.SteamId);
        WriteBanIds(ids);
        RefreshBans();
        GameTimeStatus.Text = "Banned " + player.Name + ".";
    }

    private void RefreshBans_Click(object sender, RoutedEventArgs e) => RefreshBans();

    private void RefreshBans()
    {
        var ids = ReadBanIds();
        // Streamer mode shows dots for each id; the list order stays the file order, so Unban still finds it.
        BanList.ItemsSource = ids.Select(id => StreamerMask.SteamId64(id, Streamer)).ToList();
        if (!Edition.IsAD1259)
            KeepPlayers.ShowBans(ids, Streamer);
        if (ids.Count == 0)
        {
            BanStatus.Text = "No banned players";
            BanStatus.Foreground = new SolidColorBrush(Color.FromRgb(142, 153, 174));
        }
        else
        {
            BanStatus.Text = ids.Count == 1 ? "1 player banned" : ids.Count + " players banned";
            BanStatus.Foreground = new SolidColorBrush(Color.FromRgb(255, 154, 60));
        }
    }

    private void UnbanPlayer_Click(object sender, RoutedEventArgs e)
    {
        // The list can show dots (streamer mode), so the id comes from the file by position.
        var shown = ReadBanIds();
        if (BanList.SelectedIndex < 0 || BanList.SelectedIndex >= shown.Count)
        {
            BanStatus.Text = "Select a banned player first.";
            BanStatus.Foreground = new SolidColorBrush(Color.FromRgb(232, 197, 71));
            return;
        }

        UnbanId(shown[BanList.SelectedIndex]);
    }

    private async void PauseGame_Click(object sender, RoutedEventArgs e) =>
        await SendServerCommand(ServerSession.PauseCommand);

    private async void ResumeGame_Click(object sender, RoutedEventArgs e) =>
        await SendServerCommand(ServerSession.PlayCommand);

    private async Task SendServerCommand(string command)
    {
        if (!_session.CanSendCommands)
        {
            var blocked = _session.CommandBlockReason();
            GameTimeStatus.Text = blocked;
            Remember(blocked);
            return;
        }

        // One press already waits behind a join pause step: a second one would only queue up behind it.
        if (_hostCommandWaiting)
        {
            GameTimeStatus.Text = HostCommandWaits;
            return;
        }

        Remember("> " + command);
        // The hold changes before the send, so a Coop pause handled meanwhile can't put the old speed back. The press is
        // this one's generation: if a newer press comes while this one waits, Restore below leaves that newer one alone.
        var heldBefore = _playHold.Command;
        NoteHostCommand(command);
        var press = _playHold.Generation;
        var hostCommands = _joinPause.HostCommandCount;
        try
        {
            var reply = await SendHostCommand(command);
            var text = string.IsNullOrWhiteSpace(reply) ? "Sent." : reply;
            Remember(text);
            // A refused press changed nothing on the server, so the hold goes back to what it was, and a refused pause is
            // not sent again after a join.
            if (ServerTime.Result(JoinPause.NormalMode, true, reply).Failed)
            {
                _playHold.Restore(heldBefore, press);
                _joinPause.HostSendFailed(command, hostCommands);
            }
            GameTimeStatus.Text = _playHold.Held && command == ServerSession.PlayCommand ? PlayHeldText : text;
        }
        catch (Exception ex)
        {
            _playHold.Restore(heldBefore, press);
            _joinPause.HostSendFailed(command, hostCommands);
            Remember(ex.Message);
            GameTimeStatus.Text = ex.Message;
        }
    }

    private readonly SharedSteamIdWatch _sharedSteamIds = new();

    // Two connections with one hero can be two non Steam copies with one fake Steam ID.
    // live-players.json says whether one Steam ID is behind it; only then is a line written.
    private void NoteSharedSteamIds(IReadOnlyList<ConnectedPlayer> players)
    {
        if (!SharedSteamIdWatch.HasSharedHero(players))
            return;
        var roster = SharedSteamIdWatch.ReadRosterFile(_session.ServerDataRoots(), _session.StartedAt?.ToUniversalTime());
        foreach (var line in _sharedSteamIds.Check(players, roster))
        {
            Append(line);
            _session.NoteConsole(line);
        }
    }

    private LivePlayer? SelectedPlayer()
    {
        var label = PlayerBox.SelectedItem as string;
        return ReadLivePlayers().FirstOrDefault(player => PlayerLine(player) == label);
    }

    private List<string> ReadPlayers() => ReadLivePlayers().Select(player => player.Name).Distinct().ToList();

    private List<LivePlayer> ReadLivePlayers()
    {
        // A stopped server has nobody on it. The server does not empty live-players.json when it
        // shuts down, so Home kept saying Players 1 (and the name under Connected now) after Stop.
        if (!_session.IsRunning || _engineExited)
            return new List<LivePlayer>();
        var fromFile = ReadLivePlayersFile();
        return fromFile.Count > 0 ? fromFile : ReadPlayersFromServerLog();
    }

    private List<LivePlayer> ReadLivePlayersFile()
    {
        // Read the file of the server that is running, which can be another
        // copy than the one picked in Set server folder. A file older than this
        // server start belongs to an earlier run.
        var path = _session.ServerDataRoots()
            .Select(root => Path.Combine(root, "live-players.json"))
            .Where(File.Exists)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
        if (path == null)
            return new List<LivePlayer>();
        if (_session.StartedAt is DateTime started && File.GetLastWriteTime(path) < started.AddSeconds(-5))
            return new List<LivePlayer>();
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var players = new List<LivePlayer>();
            CollectPlayers(doc.RootElement, players);
            return players
                .Where(player => player.Name.Length > 0)
                .GroupBy(player => player.Label, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();
        }
        catch
        {
            return new List<LivePlayer>();
        }
    }

    private static List<LivePlayer> ReadPlayersFromServerLog()
    {
        if (!Directory.Exists(ServerSession.LogsPath))
            return new List<LivePlayer>();
        var file = new DirectoryInfo(ServerSession.LogsPath).GetFiles("coop-server-*.log")
            .OrderByDescending(item => item.LastWriteTimeUtc)
            .FirstOrDefault();
        if (file == null)
            return new List<LivePlayer>();

        const string playersEvent = "\"ev\":\"players\"";
        var tail = HostLogFile.ReadTail(file.FullName, 256_000);
        if (!tail.Contains(playersEvent, StringComparison.Ordinal))
            tail = HostLogFile.ReadTailContaining(file.FullName, playersEvent, 256_000);

        // The newest @DS@ players event. The parser reads events glued to the end of
        // another engine line too. Its id is the peer index of this session, not a
        // SteamID64, so it is not used as one (bans and cheats need the real SteamID).
        var players = DsEventParser.LastPlayers(tail);
        if (players == null)
            return new List<LivePlayer>();
        return players.List
            .Where(player => !player.IsJoining)
            .Select(player => new LivePlayer { Name = player.Name })
            .ToList();
    }

    private static void CollectPlayers(JsonElement element, List<LivePlayer> players)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            string? name = null;
            foreach (var key in new[] { "name", "heroName", "HeroName", "playerName" })
            {
                if (element.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString()))
                    name = value.GetString();
            }

            var steam = "";
            foreach (var key in new[] { "steamId", "steamID", "steamId64", "SteamId", "ControllerId" })
            {
                if (element.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String)
                    steam = value.GetString() ?? "";
            }

            if (!string.IsNullOrWhiteSpace(name))
                players.Add(new LivePlayer { Name = name!, SteamId = steam });
            foreach (var property in element.EnumerateObject())
                CollectPlayers(property.Value, players);
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
                CollectPlayers(item, players);
        }
    }

    // server-data\steam-bans.json stays the plain JSON array Launcher Cheats reads; it is
    // written through a temporary file so the server never reads half a list.
    private List<string> ReadBanIds() => SteamBanFile.Read(BanPath).ToList();

    private void WriteBanIds(List<string> ids) => SteamBanFile.Write(BanPath, ids);

    private async void Send_Click(object sender, RoutedEventArgs e)
    {
        var command = CommandBox.Text.Trim();
        if (command.Length == 0)
            return;
        CommandBox.Clear();
        // Checks the line against the Coop command list first (MainWindow.Console.cs).
        await SendConsoleCommand(command);
    }

    private void CommandBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            Send_Click(sender, e);
            e.Handled = true;
        }
    }

    private void Clear_Click(object sender, RoutedEventArgs e) => ColoredConsole.Clear(HistoryBox);

    private void Copy_Click(object sender, RoutedEventArgs e) => Clipboard.SetText(ColoredConsole.Read(HistoryBox));

    private void AddBanIds_Click(object sender, RoutedEventArgs e)
    {
        var box = new TextBox { Text = string.Join(Environment.NewLine, ReadBanIds()), AcceptsReturn = true, MinHeight = 180 };
        var dialog = Dialog("Ban by SteamID", "One SteamID64 per line. This is the same list as Banned players.", box, () =>
        {
            var ids = box.Text.Split(new[] { '\r', '\n', ',', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries).Select(id => id.Trim()).Distinct().ToList();
            var wrong = ids.FirstOrDefault(id => !SteamBanFile.IsSteamId64(id));
            if (wrong != null)
                throw new InvalidOperationException(wrong + " is not a SteamID64. Each entry has to be 17 digits that start with 7656119.");
            WriteBanIds(ids);
        });
        dialog.ShowDialog();
        RefreshBans();
    }
}
