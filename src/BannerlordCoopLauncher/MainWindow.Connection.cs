using System.Collections.ObjectModel;
using System.Text;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using BannerlordCoopLauncher.Core.IO;
using BannerlordCoopLauncher.Core.Updates;
using BannerlordCoopLauncher.Core.Launch;
using BannerlordCoopLauncher.Core.Models;
using BannerlordCoopLauncher.Core.Modules;
using BannerlordCoopLauncher.Core.Nexus;
using BannerlordCoopLauncher.Core.Network;
using BannerlordCoopLauncher.Core.Parsing;
using BannerlordCoopLauncher.Core.Profile;
using BannerlordCoopLauncher.Core.Steam;
using BannerlordCoopLauncher.Core.Support;
using Microsoft.Win32;

namespace BannerlordCoopLauncher;

public partial class MainWindow
{
    private async void CheckServer_Click(object sender, RoutedEventArgs e)
    {
        if (!JoinGuidance.IsDirectAddress(DirectHostBox.Text))
        {
            var needAddress = UseDirect
                ? JoinGuidance.IpRequired
                : "A Steam name cannot be checked this way. Switch to Direct, or fill the host address and UDP port, then click Check server.";
            BlockReasonText.Text = needAddress;
            Log(needAddress);
            return;
        }

        if (!TryReadDirect(out var host, out var port))
        {
            var message = "Enter the UDP port the host set.";
            BlockReasonText.Text = message;
            Log(message);
            return;
        }

        ServerProbeResult? probe = null;
        await RunBusyAsync(async cancellationToken =>
        {
            SetStatus("Checking", 0xE0, 0xB1, 0x5A, 0x3A, 0x30, 0x1C);
            probe = await UdpServerProbe.ProbeAsync(host, port, UdpServerProbe.DefaultTimeout, cancellationToken);
            ApplyProbe(probe);
        });
        if (probe != null)
            BlockReasonText.Text = JoinGuidance.DescribeProbe(probe, host);
    }

    private void ApplyProbe(ServerProbeResult probe)
    {
        Log(JoinGuidance.DescribeProbe(probe, DirectHostBox.Text.Trim()));
        switch (probe.Reachability)
        {
            case ServerReachability.Online:
                SetStatus("Server online", 0x8F, 0xCB, 0x9B, 0x16, 0x30, 0x28);
                break;
            case ServerReachability.NoReply:
                SetStatus("No answer", 0xE0, 0xB1, 0x5A, 0x3A, 0x30, 0x1C);
                break;
            case ServerReachability.OtherVersion:
                SetStatus("Other Coop version", 0xE0, 0xB1, 0x5A, 0x3A, 0x30, 0x1C);
                break;
            default:
                SetStatus("Unreachable", 0xE0, 0x8B, 0x84, 0x3A, 0x24, 0x22);
                break;
        }
    }

    private void ShowLobbyStatus()
    {
        var name = SteamNameBox.Text.Trim();
        var ready = SteamHostName.IsValid(name);
        SetStatus(ready ? "Steam lobby ? " + name : "Enter a Steam name", 0x8E, 0xB4, 0xD4, 0x1B, 0x28, 0x33);
    }

    private void SetStatus(string text, byte textR, byte textG, byte textB, byte fillR, byte fillG, byte fillB)
    {
        ServerStatusText.Text = text;
        ServerStatusText.Foreground = new System.Windows.Media.SolidColorBrush(
            System.Windows.Media.Color.FromRgb(textR, textG, textB));
        ServerStatusPill.Background = new System.Windows.Media.SolidColorBrush(
            System.Windows.Media.Color.FromRgb(fillR, fillG, fillB));
    }

    private bool TryReadDirect(out string host, out int port)
    {
        host = DirectHostBox.Text.Trim();
        port = 0;
        return JoinGuidance.IsDirectAddress(host)
            && int.TryParse(DirectPortBox.Text.Trim(), out port)
            && port is >= 1 and <= 65535;
    }

    private void WriteDirectAddress(string address)
    {
        var path = CoopOptionsPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var current = File.Exists(path) ? File.ReadAllText(path) : "{}";
        File.WriteAllText(path, JoinGuidance.SetDirectAddress(current, address));
    }

    private void ClearStoredAddress()
    {
        var path = CoopOptionsPath();
        if (!File.Exists(path))
            return;
        var current = File.ReadAllText(path);
        var named = JoinGuidance.StoredAddressIsNotIp(current);
        var updated = JoinGuidance.RemoveDirectAddress(current);
        if (updated == null)
            return;
        File.WriteAllText(path, updated);
        if (named)
            Log("Removed a saved address that was not an IP, so the game will not try to look that name up on the loading screen.");
    }

    private static string CoopOptionsPath() =>
        Path.Combine(SlowMotionGuard.BannerlordDocuments(), "Configs", "BannerlordCoop", "coop_options.json");

    private async Task DetectGamePath(bool logWhenMissing)
    {
        var saved = GameFolderPick.CheckSaved(_settings.GamePath);
        if (saved.Result == GameFolderPickResult.Ok)
        {
            _gamePicked = true;
            await ApplyGameFoundAsync(saved.Root!);
            UpdateSteamStatus();
            return;
        }

        var steamPath = SteamRegistry.TryGetSteamPath();
        if (steamPath == null)
        {
            ShowGameMissing(GameSearch.SteamMissingReason, logWhenMissing);
            await RefreshAsync();
            UpdateSteamStatus();
            return;
        }

        try
        {
            var root = SteamInstallFinder.FindGameRoot(steamPath, _files, out var manifestUnreadable);
            if (root == null)
                ShowGameMissing(manifestUnreadable ? GameSearch.LibraryListUnreadableReason : GameSearch.GameMissingReason, logWhenMissing);
            else
            {
                // A Steam found root is not saved, so the next start looks in Steam again.
                _gamePicked = false;
                await ApplyGameFoundAsync(root);
            }
        }
        catch (Exception ex)
        {
            var unreadable = ex is IOException or UnauthorizedAccessException or FormatException or VdfException;
            ShowGameMissing(unreadable ? GameSearch.LibraryListUnreadableReason : ex.Message, logWhenMissing);
        }

        if (_gameRoot == null)
            await RefreshAsync();
        UpdateSteamStatus();
    }

    private void ShowGameMissing(string reason, bool log)
    {
        _gameSearched = true;
        if (log)
            Log(reason);
        GameFoundText.Text = GameFolderPick.NotFoundText;
        FindBannerlordButton.Visibility = Visibility.Visible;
    }

    // False until the first game search ends, so the Next step card says Looking for Bannerlord... instead of Check your mods.
    private bool _gameSearched;

    private string GameFoundMessage => _gamePicked ? GameFolderPick.RememberedText : "Found in Steam. You do not pick a folder.";

    private async Task ApplyGameFoundAsync(string root)
    {
        _gameSearched = true;
        _gameRoot = root;
        GamePathBox.Text = root;
        GameFoundText.Text = GameFoundMessage;
        FindBannerlordButton.Visibility = Visibility.Collapsed;
        await RefreshAsync();
    }

    private async void FindBannerlord_Click(object sender, RoutedEventArgs e)
    {
        // A pick swaps the game folder, so it waits for an install, a download or a join check to finish.
        if (_busy || _joinChecking)
        {
            Log("Wait for the current action to finish.");
            return;
        }

        var dialog = new OpenFolderDialog { Title = BannerlordCoopLauncher.Core.Localization.Translator.T("Pick the Mount & Blade II Bannerlord folder"), InitialDirectory = GameFolderPick.DialogStartFolder(_settings.GamePath), Multiselect = false };
        if (dialog.ShowDialog(this) != true)
            return;
        string? picked = null;
        var check = GameFolderPick.Use(dialog.FolderName, root =>
        {
            picked = root;
            _gamePicked = true;
            GamePathBox.Text = root;
            SaveSettingsFromForm();
        });
        if (picked == null)
        {
            Coodsz.Ui.UiLanguage.Message(check.Message, "Find Bannerlord", MessageBoxButton.OK, MessageBoxImage.Warning, this);
            return;
        }

        await ApplyGameFoundAsync(picked);
        // A UIExtenderEx SubModule.xml in the picked game's Modules is put back, as at startup.
        if (Process.GetProcessesByName("Bannerlord").Length == 0)
            RestoreUIExtenderExFix();
    }

    private void UpdateSteamStatus()
    {
        var path = SteamRegistry.TryGetSteamPath();
        var running = SteamRegistry.GetActiveProcessId() > 0;
        SteamDot.Fill = new System.Windows.Media.SolidColorBrush(running
            ? System.Windows.Media.Color.FromRgb(0x8F, 0xCB, 0x9B)
            : System.Windows.Media.Color.FromRgb(0xE0, 0xB1, 0x5A));
        SteamStatusText.Text = path == null
            ? "Steam is not installed for this Windows user."
            : running ? "Steam is running" : "Steam is installed, not running";
    }

    private void ShowMoreNationsNote()
    {
        var remasteredMod = _rows.Any(row => row.IsOn && IsRemasteredMod(row));
        MoreNationsSaveInfo.Visibility = MoreNationsMapSelected || remasteredMod
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private static bool IsRemasteredMod(ModuleRow row) =>
        CampaignMapMods.Ids.Contains(row.ModuleId, StringComparer.OrdinalIgnoreCase)
        || row.Name.Contains("More Nations", StringComparison.OrdinalIgnoreCase);

    private void SelectCampaignMap(string campaignMap)
    {
        var id = string.IsNullOrWhiteSpace(campaignMap)
            ? _settings.DefaultMap()
            : campaignMap;
        _applyingMap = true;
        foreach (ComboBoxItem item in MapBox.Items)
        {
            if (item.Tag is string tag && tag.Equals(id, StringComparison.OrdinalIgnoreCase))
            {
                MapBox.SelectedItem = item;
                break;
            }
        }
        _applyingMap = false;
    }
}
