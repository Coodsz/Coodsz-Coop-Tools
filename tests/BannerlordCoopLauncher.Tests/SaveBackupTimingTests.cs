using System.IO.Compression;
using BannerlordCoopLauncher.Core.Saves;
using TaomCoop.Core;

namespace BannerlordCoopLauncher.Tests;

// Restart saves twice a few seconds apart, and the server writes the .sav in place. A backup
// taken 3 seconds after the first "world saved" line read the second save half written, and the
// 30 second dedupe dropped the second save's own backup (BE live test, 2026-10-26).
public sealed class SaveBackupTimingTests : IDisposable
{
    private readonly string _root;
    private readonly string _saves;
    private readonly string _backups;
    private readonly BackupService _service;

    public SaveBackupTimingTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "coodsz-backup-timing-" + Guid.NewGuid().ToString("N"));
        _saves = Path.Combine(_root, "Game Saves");
        _backups = Path.Combine(_root, "Save Backups");
        Directory.CreateDirectory(_saves);
        _service = new BackupService(_saves, _backups);
        File.WriteAllText(Path.Combine(_saves, "BETest.sav"), "world one");
        File.WriteAllText(Path.Combine(_saves, "BETest.json"), "{\"Players\":[]}");
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string[] BackupFiles() =>
        Directory.Exists(_backups) ? Directory.GetFiles(_backups, "*", SearchOption.AllDirectories) : [];

    [Fact]
    public void A_save_still_being_written_is_not_backed_up()
    {
        // The dedicated server keeps a folder, not a file, while it writes the save.
        var marker = Directory.CreateDirectory(Path.Combine(_saves, "BETest.autosave-transaction"));
        File.WriteAllText(Path.Combine(marker.FullName, "primary.json"), "{}");

        Assert.Throws<IOException>(() => _service.Create("BETest", BackupReason.Autosave));
        Assert.Throws<IOException>(() => _service.Create("BETest", BackupReason.Shutdown));
        Assert.Empty(BackupFiles());

        // Once the save is done the retry takes it.
        marker.Delete(recursive: true);
        _service.Create("BETest", BackupReason.Shutdown);
        Assert.Single(BackupFiles());
    }

    [Fact]
    public void A_marker_left_by_a_crash_does_not_block_backups_made_with_the_server_stopped()
    {
        Directory.CreateDirectory(Path.Combine(_saves, "BETest.autosave-transaction"));

        _service.Create("BETest", BackupReason.BeforeRescue);
        _service.Create("BETest", BackupReason.Manual);
        var entry = _service.Create("BETest", BackupReason.BeforeRestore);
        _service.Restore(entry);
        Assert.Equal(4, BackupFiles().Length);
    }

    [Fact]
    public void A_save_that_changes_while_it_is_copied_is_not_kept()
    {
        var sav = Path.Combine(_saves, "BETest.sav");
        _service.AfterCopy = () => File.WriteAllText(sav, "world two, written while the zip read world one");

        Assert.Throws<IOException>(() => _service.Create("BETest", BackupReason.Autosave));
        Assert.Empty(BackupFiles());

        // The same size but a newer write is a new save too.
        _service.AfterCopy = () =>
        {
            File.WriteAllText(sav, "world 3");
            File.SetLastWriteTimeUtc(sav, DateTime.UtcNow.AddSeconds(5));
        };
        File.WriteAllText(sav, "world 2");
        Assert.Throws<IOException>(() => _service.Create("BETest", BackupReason.Autosave));
        Assert.Empty(BackupFiles());

        _service.AfterCopy = null;
        var entry = _service.Create("BETest", BackupReason.Autosave);
        using var zip = ZipFile.OpenRead(entry.FilePath);
        using var reader = new StreamReader(zip.GetEntry("BETest.sav")!.Open());
        Assert.Equal("world 3", reader.ReadToEnd());
    }

    [Fact]
    public void A_second_save_moves_the_waiting_backup_after_it()
    {
        var schedule = new AutoBackupSchedule();
        var t0 = new DateTime(2026, 10, 26, 5, 58, 52, DateTimeKind.Utc);

        // Save game, then Restart's save on stop 4 seconds later.
        Assert.True(schedule.Saved("BETest", BackupReason.Autosave, t0));
        Assert.Equal(TimeSpan.FromSeconds(2), schedule.Wait("BETest", t0.AddSeconds(1)));
        Assert.False(schedule.Saved("BETest", BackupReason.Shutdown, t0.AddSeconds(4)));
        Assert.Equal(TimeSpan.FromSeconds(3), schedule.Wait("betest", t0.AddSeconds(4)));
        Assert.Equal(TimeSpan.Zero, schedule.Wait("BETest", t0.AddSeconds(7)));
        Assert.Equal(BackupReason.Shutdown, schedule.Reason("BETest"));
        Assert.True(schedule.Done("BETest", t0.AddSeconds(7)));
    }

    [Fact]
    public void A_save_that_comes_during_the_backup_gets_its_own_backup()
    {
        var schedule = new AutoBackupSchedule();
        var t0 = new DateTime(2026, 10, 26, 4, 30, 0, DateTimeKind.Utc);

        Assert.True(schedule.Saved("BETest", BackupReason.Autosave, t0));
        var started = t0.AddSeconds(3);
        Assert.False(schedule.Saved("BETest", BackupReason.Shutdown, t0.AddSeconds(4)));
        // The run goes again for the newer save instead of ending.
        Assert.False(schedule.Done("BETest", started));
        Assert.Equal(TimeSpan.FromSeconds(2), schedule.Wait("BETest", t0.AddSeconds(5)));
        Assert.True(schedule.Done("BETest", t0.AddSeconds(7)));

        // A save after the backup was taken, still inside the old 30 seconds, is no longer dropped.
        Assert.True(schedule.Saved("BETest", BackupReason.Autosave, t0.AddSeconds(12)));
    }

    [Fact]
    public void The_copy_of_a_refused_line_does_not_start_a_backup()
    {
        var schedule = new AutoBackupSchedule();
        var t0 = new DateTime(2026, 10, 26, 4, 30, 0, DateTimeKind.Utc);

        Assert.True(schedule.Saved("BETest", BackupReason.Autosave, t0));
        schedule.Cancel("BETest");
        Assert.False(schedule.Saved("BETest", BackupReason.Autosave, t0.AddMilliseconds(400)));
        Assert.True(schedule.Saved("BETest", BackupReason.Autosave, t0.AddMinutes(10)));
        // Other saves have their own plan.
        Assert.True(schedule.Saved("Other", BackupReason.Autosave, t0));
    }

    [Fact]
    public void The_save_marker_counts_as_a_folder_or_a_file()
    {
        var marker = WorldSavedLine.TransactionMarker(_saves, "BETest");
        Assert.Equal(Path.Combine(_saves, "BETest.autosave-transaction"), marker);
        Assert.False(SaveSet.MarkerExists(marker));
        Directory.CreateDirectory(marker);
        Assert.True(SaveSet.MarkerExists(marker));
        Assert.True(SaveSet.BeingSaved(_saves, "BETest"));
        Directory.Delete(marker);
        File.WriteAllText(marker, "");
        Assert.True(SaveSet.MarkerExists(marker));
    }

    [Theory]
    [InlineData("TaomCoopHost")]
    [InlineData("BurningEmpiresHost")]
    public void The_server_tools_see_the_marker_folder(string project)
    {
        var source = SourceFiles.Read(Path.Combine("src", project, "MainWindow.ServerControl.cs"));
        Assert.DoesNotContain("File.Exists(marker)", source);
        Assert.DoesNotContain("File.Exists(WorldSavedLine.TransactionMarker(", source);
        Assert.Contains("SaveSet.MarkerExists(WorldSavedLine.TransactionMarker(", source);
        Assert.Contains("_actions.Note(\"Last good save\"", SourceFiles.Method(source, project == "BurningEmpiresHost" ? "private async void RestartLastGood_Click(" : "private void RestartLastGood_Click("));
        var restore = SourceFiles.Method(SourceFiles.Read(Path.Combine("src", project, "MainWindow.ModSetups.cs")), "private void RestoreSaveBackup_Click(");
        Assert.Contains("_actions.Note(\"Backup\", \"Restored \"", restore);
    }

    [Theory]
    [InlineData("CoodszHost")]
    [InlineData("TaomCoopHost")]
    [InlineData("BurningEmpiresHost")]
    public void Every_host_moves_a_waiting_backup_instead_of_dropping_the_newer_save(string project)
    {
        var noted = SourceFiles.Method(SourceFiles.Read(Path.Combine("src", project, "MainWindow.Backups.cs")), "private void NoteSavedWorld(string line)");
        Assert.Contains("_autoBackups.Saved(name, reason, DateTime.UtcNow)", noted);
        Assert.Contains("_autoBackups.Wait(name, DateTime.UtcNow)", noted);
        Assert.Contains("_autoBackups.Done(name, started)", noted);
        Assert.Contains("attempt < AutoBackupSchedule.Attempts", noted);
        Assert.DoesNotContain("FromSeconds(30)", noted);
        Assert.DoesNotContain("Task.Delay(3000)", noted);
    }
}
