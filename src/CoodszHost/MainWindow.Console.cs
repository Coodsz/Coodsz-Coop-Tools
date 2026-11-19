using System.IO;
using System.Windows;
using System.Windows.Controls;
using BannerlordCoopLauncher.Core.Keep;
using BannerlordCoopLauncher.Core.Localization;
using BannerlordCoopLauncher.Core.Server;
using CoodszHost.Services;
using TaomCoop.Core;

namespace CoodszHost;

// The Console page: the live server output with what you send, the command box with the recent
// commands, and every Coop command from bannerlordcoop.com/cheats with one box per parameter
// (Shared/CoopCommandBrowser, which the TAOM server tool shows too). The player cheats are on the
// Cheats page.
public partial class MainWindow
{
    private const int MaxRecentCommands = 8;

    private void StartConsole()
    {
        CommandBrowser.Send = SendConsoleCommand;
        CommandBrowser.PutInBox = command =>
        {
            CommandBox.Text = command;
            CommandBox.Focus();
            CommandBox.CaretIndex = CommandBox.Text.Length;
        };
        CommandBrowser.GuidePlayer = () =>
        {
            var player = ReadLivePlayers().FirstOrDefault();
            return (player?.Name, player is { SteamId.Length: > 0 } ? player.SteamId : null);
        };
        ShowRecentCommands();
    }

    /// <summary>
    /// Sends a command from the box or the list. A CLIENT command, or a Coop command with too few
    /// values, is not sent and its usage line is shown instead.
    /// </summary>
    private async Task SendConsoleCommand(string command)
    {
        command = command.Trim();
        if (command.Length == 0)
            return;
        var check = CoopCommandCatalog.CheckLine(command);
        var tokens = CoopArg.Split(command);
        var known = CoopCommandCatalog.Find(tokens.FirstOrDefault());
        // Too many values only warns: some commands join the words of a name with spaces themselves.
        if (check != null && known != null && (known.IsClientOnly || tokens.Count - 1 < known.Parameters.Count(p => !p.Optional)))
        {
            Remember("> " + command);
            Remember(Translator.T(check));
            return;
        }

        if (!_session.CanSendCommands)
        {
            Remember(_session.CommandBlockReason());
            return;
        }

        AddRecentCommand(command);
        NoteHostCommand(command);
        var hostCommands = _joinPause.HostCommandCount;
        Remember("> " + command);
        try
        {
            var reply = await SendHostCommand(command);
            Remember(string.IsNullOrWhiteSpace(reply) ? "Sent." : reply);
            // A typed pause the server refused is not sent again after a join (HostSendFailed only looks at a pause).
            if (ServerTime.Result(JoinPause.NormalMode, true, reply).Failed)
                _joinPause.HostSendFailed(command, hostCommands);
            if (check != null)
                Remember(Translator.T(check));
            else if (known != null && !string.IsNullOrWhiteSpace(reply)
                     && reply.Replace("\r", "").Split('\n').Any(line => CommandReply.Tone(line) == CommandReplyTone.Failed))
                Remember(Translator.T("Usage") + " " + known.Usage);
        }
        catch (Exception ex)
        {
            _joinPause.HostSendFailed(command, hostCommands);
            Remember(ex.Message);
        }
    }

    private void AddRecentCommand(string command)
    {
        var recent = _settings.RecentCommands;
        recent.RemoveAll(c => string.Equals(c, command, StringComparison.Ordinal));
        recent.Insert(0, command);
        if (recent.Count > MaxRecentCommands)
            recent.RemoveRange(MaxRecentCommands, recent.Count - MaxRecentCommands);
        try
        {
            HostSettings.Save(SettingsPath, _settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }

        ShowRecentCommands();
    }

    /// <summary>The recent command chips under the box: a click puts the command back in the box.</summary>
    private void ShowRecentCommands()
    {
        RecentCommands.Children.Clear();
        foreach (var command in _settings.RecentCommands.Take(MaxRecentCommands))
        {
            var text = command;
            var chip = new Button
            {
                Content = text.Length > 60 ? text[..57] + "..." : text,
                Style = (Style)FindResource("Keep.Small"),
                FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                Margin = new Thickness(0, 0, 6, 6),
                ToolTip = text,
            };
            chip.Click += (_, _) =>
            {
                CommandBox.Text = text;
                CommandBox.Focus();
                CommandBox.CaretIndex = text.Length;
            };
            RecentCommands.Children.Add(chip);
        }
    }

    /// <summary>A live server line, shown in the Console list too while Show server output is ticked.</summary>
    private void MirrorServerLine(string line)
    {
        if (ConsoleServerLines.IsChecked == true && !string.IsNullOrWhiteSpace(line))
            ColoredConsole.Append(HistoryBox, line, AutoScroll.IsChecked == true);
    }
}
