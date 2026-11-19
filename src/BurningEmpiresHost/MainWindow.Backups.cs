using System.IO;
using BannerlordCoopLauncher.Core.Saves;
using BannerlordCoopLauncher.Core.Server;
using BurningEmpires.Core;
using TaomCoop.Core;

namespace BurningEmpiresHost;

// Automatic save backups, as in Coodsz Host: after every "world saved to 'name.sav'"
// line from the server this window started, the save gets a zip backup in
// save-backups\automatic\<save>. 10 are kept per save, none older than 30 days.
// Restore save backup on the Saves page takes these zips as well as the copies kept
// before a save runs with other mods. Burning Empires keeps part of a campaign in its own
// BESavedata folder, so every backup carries the save's BESavedata slot in a folder named
// like the backup file, and a restore puts it back.
public partial class MainWindow
{
    private readonly AutoBackupSchedule _autoBackups = new();

    private string AutoBackupFolder => Path.Combine(SaveBackupFolder, "automatic");

    private BackupService AutoBackups() => new(ServerSavesDirectory(), AutoBackupFolder);

    // Runs on the thread that reads the server output.
    private void NoteSavedWorld(string line)
    {
        var name = KnownLogLines.SavedWorldName(line);
        if (name == null || string.IsNullOrWhiteSpace(_settings.DataRoot))
            return;
        // A save name the console bent is looked up on disk (only when it is not there as it came).
        var found = SaveSet.ResolveLoggedName(ServerSavesDirectory(), name, DateTime.UtcNow, out var refusal);
        if (found == null)
        {
            _actions.Note("Backup", refusal!);
            NoteLog(refusal!);
            return;
        }

        name = found;
        if (!SaveSet.IsValidSaveName(name))
            return;
        // The new campaign's first save: from now on it is a save like any other, picked in the box,
        // so the save card stops saying Start server makes it and the next start hosts it.
        if (name.Equals(_settings.NewCampaignName.Trim(), StringComparison.OrdinalIgnoreCase))
            Dispatcher.BeginInvoke(() => { PendingNewCampaign(); });
        // Restart saves again a few seconds after Save game: a waiting backup moves after the newest save.
        var reason = line.Contains("shutdown", StringComparison.OrdinalIgnoreCase) ? BackupReason.Shutdown : BackupReason.Autosave;
        if (!_autoBackups.Saved(name, reason, DateTime.UtcNow))
            return;
        // The server just wrote this save. After a failed load or with a broken world the older backups stay as they are.
        var saveRefusal = SaveRefusal();
        if (saveRefusal != null)
        {
            _autoBackups.Cancel(name);
            NoteLog(saveRefusal == BrokenWorldGuard.RefusalText
                ? "Did not back up " + name + ". Your hero has no party to rejoin in this world, so the older backups are kept as they are."
                : BrokenWorldGuard.FailedLoadBackupText);
            return;
        }

        var backups = AutoBackups();
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
                    var entry = backups.Create(name, _autoBackups.Reason(name));
                    BackupBeSaveData(name, AutoBackupDataFolder(entry.FilePath), backups.FolderFor(name));
                    NoteLog("Backed up " + name + ". Restore save backup puts it back.");
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
                    NoteLog("Could not back up " + name + ". " + ex.Message);
                    return;
                }
            }
        });
    }

    // A zip from save-backups\automatic\<save>, or null when the file is not one.
    private BackupEntry? AutoBackupEntry(string path)
    {
        if (!path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) || !SaveSlot.IsInsideDirectory(path, AutoBackupFolder))
            return null;
        var name = Path.GetFileName(Path.GetDirectoryName(path));
        if (!SaveSet.IsValidSaveName(name))
            return null;
        var info = new FileInfo(path);
        return new BackupEntry(info.FullName, name!, info.LastWriteTimeUtc, info.Length);
    }

    // The log that can block a restore. It only counts while that server still runs on empty data.
    // _logTail keeps the stopped run's lines until the next start, and after Stop the host is told
    // to click Restore save backup or Last good save, so the old lines must not refuse those. Restore
    // save backup only runs on a stopped server, so it never puts the .sav back and then refuses the data.
    private string? RestoreBlockLog() => ServerIsRunning() ? _logTail.ToString() : null;

    // The BESavedata copy of a backup sits in a folder named like the backup file.
    private static string AutoBackupDataFolder(string backupFile) => Path.ChangeExtension(backupFile, null);

    // Copies the save's BESavedata slot next to a backup. The copies of backups that were pruned go too.
    private void BackupBeSaveData(string name, string dataFolder, string? backupFolder = null)
    {
        try
        {
            BeSaveData.Backup(BeSaveData.ServerModule(_settings.ServerRoot), name, dataFolder);
            if (backupFolder == null || !Directory.Exists(backupFolder))
                return;
            foreach (var folder in Directory.EnumerateDirectories(backupFolder))
            {
                if (!Directory.EnumerateFiles(backupFolder, Path.GetFileName(folder) + ".*").Any())
                    Directory.Delete(folder, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            NoteLog("Could not back up the Burning Empires data of " + name + ". " + ex.Message);
        }
    }

    // Puts a backup's BESavedata slot back. Refused while a running server's log says Burning Empires
    // could not restore a save's data, because that server runs on empty data.
    private void RestoreBeSaveData(string dataFolder, string name)
    {
        if (!Directory.Exists(BeSaveData.Root(dataFolder)))
            return;
        var restored = BeSaveData.Restore(dataFolder, BeSaveData.ServerModule(_settings.ServerRoot), name, RestoreBlockLog());
        if (restored > 0)
            NoteLog("Put back the Burning Empires save data of " + name + ".");
    }
}
