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
using BannerlordCoopLauncher.Core.Launch;
using BannerlordCoopLauncher.Core.Saves;
using BannerlordCoopLauncher.Core.Support;
using BannerlordCoopLauncher.Core.Updates;
using BannerlordCoopLauncher.Core.Modules;
using CoodszHost.Services;
using Microsoft.Win32;

namespace CoodszHost;

public partial class MainWindow
{
    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (!Directory.Exists(ServerRoot))
        {
            SetControl(DedicatedServerFinder.MissingMessage, failed: true);
            return;
        }

        Process.Start(new ProcessStartInfo(ServerRoot) { UseShellExecute = true });
    }

    private void Unblock_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(ServerRoot) || !Directory.Exists(ServerRoot))
        {
            SetControl(DedicatedServerFinder.MissingMessage, failed: true);
            return;
        }

        // The same guard as every sweep: a real server folder (exe and engine folder), never a broad folder.
        if (!BannerlordCoopLauncher.Core.Modules.CoopInstall.IsServerFolder(ServerRoot))
        {
            SetControl(BannerlordCoopLauncher.Core.Modules.CoopInstall.NotServerFolder, failed: true);
            return;
        }

        try
        {
            var cleared = DownloadMark.Clear(ServerRoot);
            StopUnblockAlert();
            StopLogAlert();
            _actions.Note("Unblock", cleared == 0
                ? "No blocked files in the server folder."
                : "Cleared " + cleared + " blocked file(s) in the server folder.");
            SetControl(cleared == 0
                ? "No files in the server folder were blocked."
                : "Cleared the download block on " + cleared + " files in the server folder.");
        }
        catch (Exception ex)
        {
            SetControl(ex.Message, failed: true);
        }
    }

    // The two collapsed groups at the end of Settings: Advanced: server folder and Advanced: Windows.
    private void ServerFolderToggle_Click(object sender, RoutedEventArgs e) =>
        ServerFolderPanel.Visibility = ServerFolderPanel.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;

    private void WindowsToggle_Click(object sender, RoutedEventArgs e) =>
        WindowsPanel.Visibility = WindowsPanel.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;

    private void SetServerFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Bannerlord Coop server|BannerlordCoopServer.exe",
            Title = "Choose BannerlordCoopServer.exe"
        };
        if (dialog.ShowDialog(this) != true)
            return;
        if (!dialog.FileName.EndsWith("BannerlordCoopServer.exe", StringComparison.OrdinalIgnoreCase) || !File.Exists(dialog.FileName))
        {
            SetControl(DedicatedServerFinder.MissingMessage, failed: true);
            return;
        }

        // The tool sweeps the server folder on every Start (Windows' download marks), so it must be a real server
        // folder: the engine folder beside the exe, and never a drive root, the profile, Desktop, Documents or Downloads.
        if (!BannerlordCoopLauncher.Core.Modules.CoopInstall.IsServerFolder(Path.GetDirectoryName(dialog.FileName)))
        {
            SetControl(BannerlordCoopLauncher.Core.Modules.CoopInstall.NotServerFolder, failed: true);
            return;
        }

        _settings.ServerExecutable = dialog.FileName;
        HostSettings.Save(SettingsPath, _settings);
        // A running server keeps its own folder; the next Start copies a locked setup's mods into the new one.
        ReloadModules(install: !ModListRunning);
        SetControl("Using the dedicated server at " + Path.GetDirectoryName(dialog.FileName) + ".");
    }

    // Download Coop server: the browser opens the server .7z linked from Coop's release (CoopServerDownload), or the
    // release page. This tool never downloads it itself.
    private bool _openingCoopServer;

    private async void DownloadCoopServer_Click(object sender, RoutedEventArgs e)
    {
        // The manifest takes up to a few seconds; a second click meanwhile shows the same line again.
        SetControl(BannerlordCoopLauncher.Core.Modules.CoopServerDownload.LookingUpLine);
        if (_openingCoopServer)
            return;
        _openingCoopServer = true;
        try
        {
            var (line, failed) = await BannerlordCoopLauncher.Core.Modules.CoopServerDownload.OpenAsync("CoodszServerTool", "Set server folder");
            SetControl(line, failed: failed);
            // The status line is on the Dashboard, so a browser that did not open shows its link where the host clicked.
            if (failed)
                ConfirmWindow.Show(this, "Download Coop server", line);
        }
        finally
        {
            _openingCoopServer = false;
        }
    }

    private void Logs_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(ServerSession.LogsPath);
        _actions.Note("Logs", "Opened log folder. Send the highlighted files: the newest coop-server log, the newest host-console log, and the dated host-action-*.log copy (not the plain host-action.log).");
        // host-console-*.log holds what this window captured from the server,
        // including failures that stop the engine before its own log opens.
        var files = LogFolderOpen.FilesToHighlight(ServerSession.LogsPath).ToList();
        var console = Newest(ServerSession.LogsPath, ServerSession.ConsoleLogPattern);
        if (console != null && !files.Contains(console, StringComparer.OrdinalIgnoreCase))
            files.Insert(Math.Min(1, files.Count), console);
        if (files.Count > 0)
            LogFolderOpen.Select(files);
        else
            LogFolderOpen.Open(ServerSession.LogsPath);
    }

    private static string? Newest(string directory, string pattern)
    {
        try
        {
            return new DirectoryInfo(directory).GetFiles(pattern)
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .Select(file => file.FullName)
                .FirstOrDefault();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private void OpenSaves_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(ServerSession.SavesPath);
        Process.Start(new ProcessStartInfo(ServerSession.SavesPath) { UseShellExecute = true });
    }
}
