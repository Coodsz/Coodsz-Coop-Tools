using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BannerlordCoopLauncher.Core.Localization;
using BannerlordCoopLauncher.Core.Saves;
using BannerlordCoopLauncher.Core.Server;
using CoodszHost.Services;
using TaomCoop.Core;

namespace CoodszHost;

// Automatic save backups. After every "world saved to 'name.sav'" line (autosave,
// Save game and the save on stop) the save gets a zip backup in Save Backups; 10 are
// kept per save, none older than 30 days. Load backup lists them with Coop's own
// .backup copies and puts one back only while the server is stopped.
public partial class MainWindow
{
    private readonly AutoBackupSchedule _autoBackups = new();

    private static BackupService SaveBackups => SaveCatalog.Backups(ServerSession.SavesPath);

    private void NoteSavedWorld(string line)
    {
        var name = KnownLogLines.SavedWorldName(line);
        if (name == null)
            return;
        // Opening the window replays the end of the last log. That save line is from an
        // earlier run: it was backed up then, and the save may be gone since (a test save).
        if (_session.ReplayingLog)
            return;
        // The console can bend a non ASCII save name, so it is looked up on disk and both lines
        // back up the same save. When that is refused the coop-server log line, which has the name
        // right, still makes the backup, so the refusal is not shown. This runs on the UI thread;
        // it only lists the saves folder, and only when the name is not there as it came.
        name = SaveSet.ResolveLoggedName(ServerSession.SavesPath, name, DateTime.UtcNow, out _);
        if (name == null || !SaveSet.IsValidSaveName(name))
            return;
        // The same line comes from the server output and from the coop-server log, and Restart
        // saves again a few seconds after Save game: a waiting backup moves after the newest save.
        var reason = line.Contains("shutdown", StringComparison.OrdinalIgnoreCase) ? BackupReason.Shutdown : BackupReason.Autosave;
        if (!_autoBackups.Saved(name, reason, DateTime.UtcNow))
            return;
        // The server just wrote this save. After a failed load or with a broken world the older backups stay as they are.
        var saveRefusal = SaveRefusal();
        if (saveRefusal != null)
        {
            _autoBackups.Cancel(name);
            Append(saveRefusal == BrokenWorldGuard.RefusalText
                ? "Did not back up " + name + ". Your hero has no party to rejoin in this world, so the older backups are kept as they are."
                : BrokenWorldGuard.FailedLoadBackupText);
            return;
        }

        var backups = SaveBackups;
        _ = Task.Run(async () =>
        {
            for (var attempt = 1; ; attempt++)
            {
                // Coop writes the .json right after the .sav.
                TimeSpan wait;
                while ((wait = _autoBackups.Wait(name, DateTime.UtcNow)) > TimeSpan.Zero)
                    await Task.Delay(wait);
                var started = DateTime.UtcNow;
                try
                {
                    backups.Create(name, _autoBackups.Reason(name));
                    _ = Dispatcher.BeginInvoke(() => Append("Backed up " + name + ". Load backup puts it back."));
                    if (_autoBackups.Done(name, started))
                        return;
                    attempt = 0;
                }
                catch (IOException) when (attempt < AutoBackupSchedule.Attempts)
                {
                    await Task.Delay(AutoBackupSchedule.RetryDelay);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
                {
                    if (!_autoBackups.Done(name, started))
                    {
                        attempt = 0;
                        continue;
                    }

                    _actions.Note("Backup", "Failed for " + name + ": " + ex.Message);
                    _ = Dispatcher.BeginInvoke(() => Append("Could not back up " + name + ". " + ex.Message));
                    return;
                }
            }
        });
    }

    // Before Last good save writes an older copy over the live save.
    private void BackupBeforeReplace(string name)
    {
        try
        {
            SaveBackups.Create(name, BackupReason.BeforeRestore);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            _actions.Note("Backup", "No copy before replacing " + name + ": " + ex.Message);
        }
    }

    private sealed record BackupChoice(string Label, DateTime When, BackupEntry? Zip, string? CoopCopy);

    private void LoadBackup_Click(object sender, RoutedEventArgs e)
    {
        if (_session.IsRunning)
        {
            SetControl("Stop the server before loading a backup.", failed: true);
            return;
        }

        var name = (SaveBox.SelectedItem as string ?? _settings.Current().SaveName ?? "").Trim();
        if (name.Length == 0)
        {
            SetControl("Pick a campaign save first.", failed: true);
            return;
        }

        var choices = BackupChoices(name);
        if (choices.Count == 0)
        {
            SetControl("No backups of " + name + " were found yet. One is made each time the server saves.", failed: true);
            return;
        }

        var list = new ListBox
        {
            Margin = new Thickness(12, 0, 12, 0),
            ItemsSource = choices.Select(choice => choice.Label).ToList(),
            SelectedIndex = 0,
            Background = new SolidColorBrush(Color.FromRgb(16, 22, 27)),
            Foreground = new SolidColorBrush(Color.FromRgb(213, 222, 230)),
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 13
        };
        var note = new TextBlock
        {
            Text = "Backups of " + name + ", newest first. The save as it is now is backed up before it is replaced.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(12),
            Foreground = new SolidColorBrush(Color.FromRgb(142, 160, 174)),
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 13
        };
        var apply = new Button
        {
            Content = "Restore",
            ToolTip = "Puts back the backup selected above. The save as it is now is backed up first. Stop the server before you do this.",
            Margin = new Thickness(12),
            Padding = new Thickness(12, 8, 12, 8),
            HorizontalAlignment = HorizontalAlignment.Right,
            Background = new SolidColorBrush(Color.FromRgb(31, 78, 121)),
            Foreground = new SolidColorBrush(Color.FromRgb(213, 222, 230)),
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 13
        };
        var panel = new DockPanel();
        DockPanel.SetDock(note, Dock.Top);
        DockPanel.SetDock(apply, Dock.Bottom);
        panel.Children.Add(note);
        panel.Children.Add(apply);
        panel.Children.Add(list);
        var pick = new Window
        {
            Title = "Load backup",
            Width = 520,
            Height = 380,
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = new SolidColorBrush(Color.FromRgb(14, 20, 24)),
            Foreground = new SolidColorBrush(Color.FromRgb(213, 222, 230)),
            Content = panel
        };
        Coodsz.Ui.WindowPlacement.KeepOnScreen(pick);
        apply.Click += (_, _) =>
        {
            if (list.SelectedIndex < 0)
                return;
            pick.Close();
            RestoreBackup(name, choices[list.SelectedIndex]);
        };
        pick.ShowDialog();
    }

    private List<BackupChoice> BackupChoices(string name)
    {
        var choices = new List<BackupChoice>();
        if (SaveSet.IsValidSaveName(name))
        {
            foreach (var entry in SaveBackups.List(name))
                choices.Add(new BackupChoice(
                    entry.CreatedLocal.ToString("g") + "   " + ReasonText(entry.Reason) + "   " + (entry.SizeBytes / 1048576.0).ToString("0.0") + " MB",
                    entry.CreatedLocal, entry, null));
        }

        if (Directory.Exists(ServerSession.SavesPath))
        {
            foreach (var path in Directory.GetFiles(ServerSession.SavesPath, name + ".backup*.sav"))
            {
                var when = File.GetLastWriteTime(path);
                choices.Add(new BackupChoice(
                    when.ToString("g") + "   " + Translator.T("Coop's own copy") + " (" + Path.GetFileNameWithoutExtension(path) + ")",
                    when, null, path));
            }
        }

        return choices.OrderByDescending(choice => choice.When).ToList();
    }

    private static string ReasonText(BackupReason reason) => reason switch
    {
        BackupReason.Autosave => Translator.T("after an autosave"),
        BackupReason.Shutdown => Translator.T("after the save on stop"),
        BackupReason.BeforeRestore => Translator.T("made before a restore"),
        BackupReason.BeforeRescue => Translator.T("made before the save fixes"),
        BackupReason.Manual => Translator.T("made by hand"),
        _ => Translator.T("Backup")
    };

    private void RestoreBackup(string name, BackupChoice choice)
    {
        if (_session.IsRunning)
        {
            SetControl("Stop the server before loading a backup.", failed: true);
            return;
        }

        try
        {
            if (choice.Zip != null)
                SaveBackups.Restore(choice.Zip);
            else if (choice.CoopCopy != null)
                SaveBackups.RestoreCopy(name, choice.CoopCopy);
            _actions.Note("Backup", "Restored " + name + " from " + choice.When.ToString("s"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or InvalidDataException or ArgumentException)
        {
            SetControl("The backup was not put back. The save is as it was. " + ex.Message, failed: true);
            Append("The backup was not put back. The save is as it was. " + ex.Message);
            return;
        }

        ReloadSaves();
        SaveBox.SelectedItem = name;
        SettingsSaveBox.SelectedItem = name;
        _settings.Current().SaveName = name;
        HostSettings.Save(SettingsPath, _settings);
        OverviewSave.Text = name;
        ShowSaveDetail(name);
        var done = "Put back the backup of " + name + " from " + choice.When.ToString("g") + ". The save it replaced is kept as a backup too.";
        SetControl(done, done: true);
        Append(done);
    }
}
