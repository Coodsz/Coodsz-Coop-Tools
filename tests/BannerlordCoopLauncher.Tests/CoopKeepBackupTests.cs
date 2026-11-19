// Tests ported from CoopKeep (https://github.com/Aderimo/bannerlord-coopkeep),
// tests/CoopKeep.Core.Tests/Saves/BackupServiceTests.cs, translated from Turkish and
// changed for the Coodsz tools.
//
// Copyright (c) 2026 aderimo
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

using System.IO.Compression;
using BannerlordCoopLauncher.Core.Saves;

namespace BannerlordCoopLauncher.Tests;

public sealed class BackupServiceTests : IDisposable
{
    private readonly string _root;
    private readonly string _saves;
    private readonly string _backups;
    private readonly BackupService _service;

    public BackupServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "coodsz-backup-" + Guid.NewGuid().ToString("N"));
        _saves = Path.Combine(_root, "Game Saves");
        _backups = Path.Combine(_root, "Save Backups");
        Directory.CreateDirectory(_saves);
        _service = new BackupService(_saves, _backups);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private void WriteWorld(string name, string sav = "world", string json = "{\"Players\":[]}", string? launcher = null)
    {
        File.WriteAllText(Path.Combine(_saves, name + ".sav"), sav);
        File.WriteAllText(Path.Combine(_saves, name + ".json"), json);
        if (launcher != null)
            File.WriteAllText(Path.Combine(_saves, name + ".launcher.json"), launcher);
    }

    private string Read(string file) => File.ReadAllText(Path.Combine(_saves, file));

    [Fact]
    public void A_backup_holds_the_sav_the_json_and_the_launcher_json()
    {
        WriteWorld("Calradia", launcher: "{\"Modules\":[\"Coop\"]}");
        var entry = _service.Create("Calradia", BackupReason.Autosave);

        Assert.StartsWith(Path.Combine(_backups, "Calradia"), entry.FilePath);
        Assert.EndsWith("_auto.zip", entry.FilePath);
        using var zip = ZipFile.OpenRead(entry.FilePath);
        Assert.NotNull(zip.GetEntry("Calradia.sav"));
        Assert.NotNull(zip.GetEntry("Calradia.json"));
        Assert.NotNull(zip.GetEntry("Calradia.launcher.json"));
        Assert.Equal(BackupReason.Autosave, Assert.Single(_service.List("Calradia")).Reason);
    }

    [Fact]
    public void A_save_that_lost_its_json_is_not_backed_up()
    {
        // The server's .backup1 pair shows the save had a .json before.
        File.WriteAllText(Path.Combine(_saves, "Half.sav"), "world");
        File.WriteAllText(Path.Combine(_saves, "Half.backup1.sav"), "older");
        File.WriteAllText(Path.Combine(_saves, "Half.backup1.json"), "{\"Players\":[1]}");
        var error = Assert.Throws<InvalidOperationException>(() => _service.Create("Half"));
        Assert.DoesNotContain("not complete", error.Message);
        Assert.Empty(_service.List("Half"));
        Assert.Throws<FileNotFoundException>(() => _service.Create("NoSuchWorld"));
    }

    [Fact]
    public void An_imported_save_without_a_json_is_backed_up_and_its_restore_removes_the_json_the_server_wrote_later()
    {
        // Crash report CR-8XCEF: a base game save imported in the Server Tool is only a .sav.
        File.WriteAllText(Path.Combine(_saves, "Imported.sav"), "IMPORTED");
        File.WriteAllText(Path.Combine(_saves, "Imported.launcher.json"), "{\"Modules\":[\"A\"]}");
        var entry = _service.Create("Imported", BackupReason.BeforeRescue);
        using (var zip = ZipFile.OpenRead(entry.FilePath))
        {
            Assert.NotNull(zip.GetEntry("Imported.sav"));
            Assert.NotNull(zip.GetEntry("Imported.launcher.json"));
            Assert.NotNull(zip.GetEntry("Imported" + BackupService.NoCompanionMarker));
            Assert.Null(zip.GetEntry("Imported.json"));
        }

        // The server loads it and saves, which writes the .json.
        WriteWorld("Imported", "SERVED", "{\"Players\":[1]}", "{\"Modules\":[\"B\"]}");
        _service.Restore(entry);

        Assert.Equal("IMPORTED", Read("Imported.sav"));
        Assert.False(File.Exists(Path.Combine(_saves, "Imported.json")));
        Assert.Equal("{\"Modules\":[\"A\"]}", Read("Imported.launcher.json"));
        Assert.Empty(Directory.GetFiles(_saves, "*.rollback"));
        // The served state went to its own backup, .json and all.
        var before = _service.List("Imported").First(item => item.Reason == BackupReason.BeforeRestore);
        using (var zip = ZipFile.OpenRead(before.FilePath))
            Assert.NotNull(zip.GetEntry("Imported.json"));
    }

    private void WriteServerCopy(string name, string copy, string sav)
    {
        File.WriteAllText(Path.Combine(_saves, name + "." + copy + ".sav"), sav);
        File.WriteAllText(Path.Combine(_saves, name + "." + copy + ".json"), "{\"Players\":[1]}");
    }

    [Fact]
    public void Restoring_a_save_without_a_json_after_two_server_saves_removes_the_server_copies_so_backups_still_work()
    {
        File.WriteAllText(Path.Combine(_saves, "Imported.sav"), "IMPORTED");
        var entry = _service.Create("Imported", BackupReason.BeforeRescue);
        // Two server saves: the live pair, and the first save rotated into .backup1.
        WriteWorld("Imported", "SECOND");
        WriteServerCopy("Imported", "backup1", "FIRST");

        _service.Restore(entry);

        Assert.Equal("IMPORTED", Read("Imported.sav"));
        Assert.Empty(Directory.GetFiles(_saves, "Imported.backup*"));
        Assert.Empty(Directory.GetFiles(_saves, "*.rollback"));
        var again = _service.Create("Imported");
        Assert.True(File.Exists(again.FilePath));
    }

    [Fact]
    public void A_restore_over_a_save_that_lost_its_json_can_still_be_undone()
    {
        WriteWorld("Calradia", "OLD", "{\"Players\":[1]}");
        var entry = _service.Create("Calradia");
        // The live save lost its .json, and the server's copies are still there.
        File.WriteAllText(Path.Combine(_saves, "Calradia.sav"), "LIVE");
        File.Delete(Path.Combine(_saves, "Calradia.json"));
        WriteServerCopy("Calradia", "backup1", "COPY");

        _service.Restore(entry);

        Assert.Equal("OLD", Read("Calradia.sav"));
        Assert.Equal("COPY", Read("Calradia.backup1.sav"));
        var undo = _service.List("Calradia").First(item => item.Reason == BackupReason.BeforeRestore);
        using (var zip = ZipFile.OpenRead(undo.FilePath))
        {
            Assert.NotNull(zip.GetEntry("Calradia.sav"));
            Assert.NotNull(zip.GetEntry("Calradia" + BackupService.NoCompanionMarker));
            Assert.NotNull(zip.GetEntry("Calradia.backup1.json"));
        }

        _service.Restore(undo);

        Assert.Equal("LIVE", Read("Calradia.sav"));
        Assert.False(File.Exists(Path.Combine(_saves, "Calradia.json")));
        Assert.Equal("COPY", Read("Calradia.backup1.sav"));
        Assert.Equal("{\"Players\":[1]}", Read("Calradia.backup1.json"));
    }

    [Fact]
    public void Undoing_a_restore_without_a_json_puts_the_server_copies_back()
    {
        File.WriteAllText(Path.Combine(_saves, "Imported.sav"), "IMPORTED");
        var entry = _service.Create("Imported", BackupReason.BeforeRescue);
        WriteWorld("Imported", "THIRD", "{\"Players\":[3]}");
        WriteServerCopy("Imported", "backup1", "SECOND");
        WriteServerCopy("Imported", "backup2", "FIRST");

        _service.Restore(entry);
        Assert.Empty(Directory.GetFiles(_saves, "Imported.backup*"));
        var undo = _service.List("Imported").First(item => item.Reason == BackupReason.BeforeRestore);
        _service.Restore(undo);

        Assert.Equal("THIRD", Read("Imported.sav"));
        Assert.Equal("{\"Players\":[3]}", Read("Imported.json"));
        Assert.Equal("SECOND", Read("Imported.backup1.sav"));
        Assert.Equal("{\"Players\":[1]}", Read("Imported.backup1.json"));
        Assert.Equal("FIRST", Read("Imported.backup2.sav"));
        Assert.Equal("{\"Players\":[1]}", Read("Imported.backup2.json"));
        Assert.Empty(Directory.GetFiles(_saves, "*.rollback"));
    }

    [Fact]
    public void A_restore_without_a_json_over_a_missing_sav_backs_up_the_server_copies_and_backups_still_work()
    {
        File.WriteAllText(Path.Combine(_saves, "Imported.sav"), "IMPORTED");
        var entry = _service.Create("Imported", BackupReason.BeforeRescue);
        // The live pair is gone, only the server's copies are left.
        File.Delete(Path.Combine(_saves, "Imported.sav"));
        WriteServerCopy("Imported", "backup1", "SECOND");
        WriteServerCopy("Imported", "backup2", "FIRST");

        _service.Restore(entry);

        Assert.Equal("IMPORTED", Read("Imported.sav"));
        Assert.Empty(Directory.GetFiles(_saves, "Imported.backup*"));
        Assert.Empty(Directory.GetFiles(_saves, "*.rollback"));
        // The copies went into the backup made before the restore, with no .sav since there was none.
        var undo = _service.List("Imported").Single(item => item.Reason == BackupReason.BeforeRestore);
        using (var zip = ZipFile.OpenRead(undo.FilePath))
        {
            Assert.Null(zip.GetEntry("Imported.sav"));
            Assert.NotNull(zip.GetEntry("Imported.backup1.sav"));
            Assert.NotNull(zip.GetEntry("Imported.backup2.json"));
        }

        // Nothing left says a .json was lost, so the backup before a start and a Manual backup both work.
        Assert.True(File.Exists(_service.Create("Imported").FilePath));
        Assert.True(File.Exists(_service.Create("Imported", BackupReason.Manual).FilePath));

        // Putting the backup before the restore back returns the copies and no .sav.
        _service.Restore(undo);
        Assert.False(File.Exists(Path.Combine(_saves, "Imported.sav")));
        Assert.Equal("SECOND", Read("Imported.backup1.sav"));
        Assert.Equal("{\"Players\":[1]}", Read("Imported.backup1.json"));
        Assert.Equal("FIRST", Read("Imported.backup2.sav"));
        Assert.Equal("{\"Players\":[1]}", Read("Imported.backup2.json"));
        Assert.Empty(Directory.GetFiles(_saves, "*.rollback"));
        // That restore made its own backup of the restored save, so it can be undone too.
        Assert.Equal(2, _service.List("Imported").Count(item => item.Reason == BackupReason.BeforeRestore));
    }

    [Fact]
    public void A_server_copy_in_backup2_alone_also_shows_the_json_was_lost()
    {
        File.WriteAllText(Path.Combine(_saves, "Half.sav"), "world");
        WriteServerCopy("Half", "backup2", "older");
        Assert.Throws<InvalidOperationException>(() => _service.Create("Half"));
    }

    [Fact]
    public void A_zip_with_both_the_marker_and_a_json_restores_the_json_and_keeps_the_server_copies()
    {
        WriteWorld("Calradia", "SOUND");
        WriteServerCopy("Calradia", "backup1", "COPY");
        var folder = _service.FolderFor("Calradia");
        Directory.CreateDirectory(folder);
        var both = Path.Combine(folder, "Calradia_both.zip");
        using (var zip = ZipFile.Open(both, ZipArchiveMode.Create))
        {
            using (var writer = new StreamWriter(zip.CreateEntry("Calradia.sav").Open()))
                writer.Write("RESTORED");
            using (var writer = new StreamWriter(zip.CreateEntry("Calradia.json").Open()))
                writer.Write("{\"Players\":[7]}");
            zip.CreateEntry("Calradia" + BackupService.NoCompanionMarker);
        }

        _service.Restore(new BackupEntry(both, "Calradia", DateTime.UtcNow, 0));

        Assert.Equal("RESTORED", Read("Calradia.sav"));
        Assert.Equal("{\"Players\":[7]}", Read("Calradia.json"));
        Assert.Equal("COPY", Read("Calradia.backup1.sav"));
    }

    [Fact]
    public void A_zip_with_its_own_server_copies_over_a_missing_sav_puts_its_copies_in_and_backs_up_the_live_ones()
    {
        // The live .sav is gone; the server's copies are still there.
        WriteServerCopy("Calradia", "backup1", "LIVE1");
        WriteServerCopy("Calradia", "backup2", "LIVE2");
        var folder = _service.FolderFor("Calradia");
        Directory.CreateDirectory(folder);
        // A zip that carries its own copies, like a backup made before an earlier restore.
        var carried = Path.Combine(folder, "Calradia_carried.zip");
        using (var zip = ZipFile.Open(carried, ZipArchiveMode.Create))
        {
            foreach (var (name, text) in new[] { ("Calradia.sav", "ZIPSAV"), ("Calradia.json", "{\"Players\":[7]}"), ("Calradia.backup1.sav", "ZIP1"), ("Calradia.backup1.json", "{\"Players\":[8]}") })
            {
                using var writer = new StreamWriter(zip.CreateEntry(name).Open());
                writer.Write(text);
            }
        }

        _service.Restore(new BackupEntry(carried, "Calradia", DateTime.UtcNow, 0));

        // The save and copies are exactly the zip's: its .backup1, and no .backup2 since it had none.
        Assert.Equal("ZIPSAV", Read("Calradia.sav"));
        Assert.Equal("{\"Players\":[7]}", Read("Calradia.json"));
        Assert.Equal("ZIP1", Read("Calradia.backup1.sav"));
        Assert.Equal("{\"Players\":[8]}", Read("Calradia.backup1.json"));
        Assert.False(File.Exists(Path.Combine(_saves, "Calradia.backup2.sav")));
        Assert.False(File.Exists(Path.Combine(_saves, "Calradia.backup2.json")));
        Assert.Empty(Directory.GetFiles(_saves, "*.rollback"));
        // The live copies went into the backup made before the restore, with no .sav.
        var undo = _service.List("Calradia").Single(item => item.Reason == BackupReason.BeforeRestore);
        using (var zip = ZipFile.OpenRead(undo.FilePath))
        {
            Assert.Null(zip.GetEntry("Calradia.sav"));
            Assert.Equal(
                new[] { "Calradia" + BackupService.NoCompanionMarker, "Calradia" + BackupService.NoSaveMarker, "Calradia.backup1.json", "Calradia.backup1.sav", "Calradia.backup2.json", "Calradia.backup2.sav" }.OrderBy(name => name, StringComparer.Ordinal).ToList(),
                zip.Entries.Select(entry => entry.FullName).OrderBy(name => name, StringComparer.Ordinal).ToList());
        }

        _service.Restore(undo);
        Assert.False(File.Exists(Path.Combine(_saves, "Calradia.sav")));
        Assert.False(File.Exists(Path.Combine(_saves, "Calradia.json")));
        Assert.Equal("LIVE1", Read("Calradia.backup1.sav"));
        Assert.Equal("{\"Players\":[1]}", Read("Calradia.backup1.json"));
        Assert.Equal("LIVE2", Read("Calradia.backup2.sav"));
        Assert.Equal("{\"Players\":[1]}", Read("Calradia.backup2.json"));
    }

    [Fact]
    public void A_copies_only_zip_over_a_lone_json_backs_it_up_and_undo_puts_both_json_files_back()
    {
        // Grok review: the .sav is gone and no server copies are left, only its .json (the player
        // mapping) and .launcher.json. The restore removed them with no backup to undo it.
        File.WriteAllText(Path.Combine(_saves, "Calradia.json"), "{\"Players\":[5]}");
        File.WriteAllText(Path.Combine(_saves, "Calradia.launcher.json"), "{\"Mods\":[\"Coop\"]}");
        var folder = _service.FolderFor("Calradia");
        Directory.CreateDirectory(folder);
        var copiesOnly = Path.Combine(folder, "Calradia_copies.zip");
        using (var zip = ZipFile.Open(copiesOnly, ZipArchiveMode.Create))
        {
            foreach (var (name, text) in new[] { ("Calradia.backup1.sav", "ZIP1"), ("Calradia.backup1.json", "{\"Players\":[8]}") })
            {
                using var writer = new StreamWriter(zip.CreateEntry(name).Open());
                writer.Write(text);
            }

            zip.CreateEntry("Calradia" + BackupService.NoCompanionMarker);
        }

        var before = _service.Restore(new BackupEntry(copiesOnly, "Calradia", DateTime.UtcNow, 0));

        Assert.Equal("ZIP1", Read("Calradia.backup1.sav"));
        Assert.False(File.Exists(Path.Combine(_saves, "Calradia.json")));
        Assert.False(File.Exists(Path.Combine(_saves, "Calradia.launcher.json")));
        Assert.NotNull(before);
        var undo = _service.List("Calradia").Single(item => item.Reason == BackupReason.BeforeRestore);
        Assert.Equal(before!.FilePath, undo.FilePath);
        using (var zip = ZipFile.OpenRead(undo.FilePath))
        {
            Assert.Null(zip.GetEntry("Calradia.sav"));
            Assert.NotNull(zip.GetEntry("Calradia" + BackupService.NoSaveMarker));
        }

        _service.Restore(undo);

        Assert.Equal("{\"Players\":[5]}", Read("Calradia.json"));
        Assert.Equal("{\"Mods\":[\"Coop\"]}", Read("Calradia.launcher.json"));
        Assert.False(File.Exists(Path.Combine(_saves, "Calradia.sav")));
        // The undo zip holds no copies (there were none live), so it leaves the zip's copies alone.
        Assert.Equal("ZIP1", Read("Calradia.backup1.sav"));
        Assert.Empty(Directory.GetFiles(_saves, "*.rollback"));
    }

    [Fact]
    public void A_restore_over_nothing_makes_no_backup_before_it()
    {
        WriteWorld("Calradia", "OLD");
        var entry = _service.Create("Calradia");
        File.Delete(Path.Combine(_saves, "Calradia.sav"));
        File.Delete(Path.Combine(_saves, "Calradia.json"));

        Assert.Null(_service.Restore(entry));
        Assert.Equal("OLD", Read("Calradia.sav"));
        Assert.DoesNotContain(_service.List("Calradia"), item => item.Reason == BackupReason.BeforeRestore);
    }

    [Fact]
    public void A_zip_without_a_sav_or_the_marker_is_still_refused()
    {
        File.WriteAllText(Path.Combine(_saves, "Calradia.json"), "{\"Players\":[5]}");
        var folder = _service.FolderFor("Calradia");
        Directory.CreateDirectory(folder);
        var lone = Path.Combine(folder, "Calradia_lone.zip");
        using (var zip = ZipFile.Open(lone, ZipArchiveMode.Create))
        using (var writer = new StreamWriter(zip.CreateEntry("Calradia.json").Open()))
            writer.Write("{\"Players\":[9]}");

        Assert.Throws<InvalidDataException>(() => _service.Restore(new BackupEntry(lone, "Calradia", DateTime.UtcNow, 0)));
        Assert.Equal("{\"Players\":[5]}", Read("Calradia.json"));
    }

    [Fact]
    public void A_save_named_like_a_stamp_still_puts_the_newer_backup_of_the_same_millisecond_first()
    {
        // Codex review: the unanchored stamp match read the save name's own stamp for every backup.
        const string name = "World_20261027-120000-000";
        WriteWorld(name);
        var made = _service.Create(name);
        var folder = Path.GetDirectoryName(made.FilePath)!;
        var older = Path.Combine(folder, name + "_20261028-224549-483_auto.zip");
        var newer = Path.Combine(folder, name + "_20261028-224549-483-1_auto.zip");
        File.Copy(made.FilePath, older);
        File.Copy(made.FilePath, newer);
        File.Delete(made.FilePath);
        var at = new DateTime(2026, 10, 28, 22, 45, 49, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(older, at);
        File.SetLastWriteTimeUtc(newer, at);

        Assert.Equal([newer, older], _service.List(name).Select(entry => entry.FilePath));
        // Count pruning keeps the newer one.
        Assert.Equal(1, new BackupService(_saves, _backups, new BackupPolicy(MaxCount: 1, MaxAgeDays: 0)).Prune(name));
        Assert.True(File.Exists(newer));
        Assert.False(File.Exists(older));
    }

    [Fact]
    public void A_restore_of_a_save_without_a_json_that_fails_halfway_puts_the_old_files_back()
    {
        File.WriteAllText(Path.Combine(_saves, "Imported.sav"), "IMPORTED");
        var entry = _service.Create("Imported");
        WriteWorld("Imported", "SERVED");
        WriteServerCopy("Imported", "backup1", "FIRST");
        WriteServerCopy("Imported", "backup2", "ZERO");
        // The server's .backup2.json is locked, so it cannot be moved aside after the save and .backup1 were.
        using (new FileStream(Path.Combine(_saves, "Imported.backup2.json"), FileMode.Open, FileAccess.Read, FileShare.Read))
            Assert.ThrowsAny<IOException>(() => _service.Restore(entry));

        Assert.Equal("SERVED", Read("Imported.sav"));
        Assert.Equal("{\"Players\":[]}", Read("Imported.json"));
        Assert.Equal("FIRST", Read("Imported.backup1.sav"));
        Assert.Equal("{\"Players\":[1]}", Read("Imported.backup1.json"));
        Assert.Equal("ZERO", Read("Imported.backup2.sav"));
        Assert.Empty(Directory.GetFiles(_saves, "*.rollback"));
    }

    [Fact]
    public void Two_backups_in_the_same_second_keep_both_and_the_newest_comes_first()
    {
        WriteWorld("Calradia");
        var first = _service.Create("Calradia");
        var second = _service.Create("Calradia");

        Assert.NotEqual(first.FilePath, second.FilePath);
        var list = _service.List("Calradia");
        Assert.Equal(2, list.Count);
        Assert.Equal(second.FilePath, list[0].FilePath);
    }

    [Fact]
    public void Two_backups_with_the_same_time_and_millisecond_put_the_one_with_the_counter_first()
    {
        // CI caught this: a file system with coarse times showed both as written at once, and the
        // name order alone put "-483.zip" above the newer "-483-1.zip".
        WriteWorld("Calradia");
        var made = _service.Create("Calradia");
        var folder = Path.GetDirectoryName(made.FilePath)!;
        var older = Path.Combine(folder, "Calradia_20261028-224549-483.zip");
        var newer = Path.Combine(folder, "Calradia_20261028-224549-483-1.zip");
        File.Copy(made.FilePath, older);
        File.Copy(made.FilePath, newer);
        File.Delete(made.FilePath);
        var at = new DateTime(2026, 10, 28, 22, 45, 49, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(older, at);
        File.SetLastWriteTimeUtc(newer, at);

        Assert.Equal([newer, older], _service.List("Calradia").Select(entry => entry.FilePath));
        Assert.Equal([newer, older], _service.ListAll().Select(entry => entry.FilePath));
    }

    [Fact]
    public void Backups_stay_out_of_Game_Saves()
    {
        WriteWorld("Calradia");
        _service.Create("Calradia");
        Assert.Equal(new[] { "Calradia.json", "Calradia.sav" }, Directory.GetFiles(_saves).Select(Path.GetFileName).OrderBy(name => name, StringComparer.Ordinal));
    }

    [Fact]
    public void Ten_are_kept_by_default()
    {
        WriteWorld("Calradia");
        for (var i = 0; i < 13; i++)
            _service.Create("Calradia");
        Assert.Equal(10, _service.List("Calradia").Count);
    }

    [Fact]
    public void Backups_older_than_the_age_limit_go()
    {
        var service = new BackupService(_saves, _backups, new BackupPolicy(MaxCount: 0, MaxAgeDays: 30));
        WriteWorld("Calradia");
        var old = service.Create("Calradia");
        var fresh = service.Create("Calradia");
        File.SetLastWriteTimeUtc(old.FilePath, DateTime.UtcNow.AddDays(-31));

        Assert.Equal(1, service.Prune("Calradia"));
        Assert.Equal(fresh.FilePath, Assert.Single(service.List("Calradia")).FilePath);
    }

    [Fact]
    public void Count_and_age_limits_both_apply()
    {
        var service = new BackupService(_saves, _backups, new BackupPolicy(MaxCount: 5, MaxAgeDays: 7));
        WriteWorld("Calradia");
        for (var i = 0; i < 4; i++)
            service.Create("Calradia");
        foreach (var entry in service.List("Calradia").Skip(2))
            File.SetLastWriteTimeUtc(entry.FilePath, DateTime.UtcNow.AddDays(-10));

        service.Prune("Calradia");
        Assert.Equal(2, service.List("Calradia").Count);
    }

    [Fact]
    public void Restore_puts_the_world_back_and_backs_up_the_world_it_replaced()
    {
        WriteWorld("Calradia", "FIRST", "{\"Players\":[1]}", "{\"Modules\":[\"A\"]}");
        var entry = _service.Create("Calradia");
        WriteWorld("Calradia", "BROKEN", "{\"Players\":[]}", "{\"Modules\":[\"B\"]}");

        _service.Restore(entry);

        Assert.Equal("FIRST", Read("Calradia.sav"));
        Assert.Equal("{\"Players\":[1]}", Read("Calradia.json"));
        Assert.Equal("{\"Modules\":[\"A\"]}", Read("Calradia.launcher.json"));
        var list = _service.List("Calradia");
        Assert.Equal(2, list.Count);
        Assert.Equal(BackupReason.BeforeRestore, list[0].Reason);
        Assert.Empty(Directory.GetFiles(_saves, "*.rollback"));
        Assert.Empty(Directory.GetDirectories(_service.FolderFor("Calradia")));
    }

    [Fact]
    public void Restore_leaves_the_save_with_only_the_files_the_backup_holds()
    {
        WriteWorld("Calradia", "FIRST");
        var entry = _service.Create("Calradia");
        WriteWorld("Calradia", "LATER", launcher: "{\"Modules\":[\"B\"]}");

        _service.Restore(entry);

        Assert.Equal("FIRST", Read("Calradia.sav"));
        Assert.False(File.Exists(Path.Combine(_saves, "Calradia.launcher.json")));
    }

    [Fact]
    public void The_backup_being_restored_survives_a_full_folder()
    {
        WriteWorld("Calradia", "OLDEST");
        var oldest = _service.Create("Calradia");
        for (var i = 0; i < 9; i++)
            _service.Create("Calradia");
        File.SetLastWriteTimeUtc(oldest.FilePath, DateTime.UtcNow.AddHours(-1));
        WriteWorld("Calradia", "NOW");

        _service.Restore(_service.List("Calradia").Last());

        Assert.Equal("OLDEST", Read("Calradia.sav"));
    }

    [Fact]
    public void A_backup_without_the_json_is_refused_and_the_world_stays()
    {
        WriteWorld("Calradia", "SOUND");
        var folder = _service.FolderFor("Calradia");
        Directory.CreateDirectory(folder);
        var broken = Path.Combine(folder, "Calradia_broken.zip");
        using (var zip = ZipFile.Open(broken, ZipArchiveMode.Create))
            zip.CreateEntry("Calradia.sav");

        Assert.Throws<InvalidDataException>(() => _service.Restore(new BackupEntry(broken, "Calradia", DateTime.UtcNow, 0)));
        Assert.Equal("SOUND", Read("Calradia.sav"));
        Assert.Single(_service.List("Calradia"));
    }

    [Fact]
    public void Entries_with_other_names_in_a_zip_are_never_written()
    {
        WriteWorld("Calradia", "SOUND");
        var folder = _service.FolderFor("Calradia");
        Directory.CreateDirectory(folder);
        var crafted = Path.Combine(folder, "Calradia_crafted.zip");
        using (var zip = ZipFile.Open(crafted, ZipArchiveMode.Create))
        {
            using (var writer = new StreamWriter(zip.CreateEntry("Calradia.sav").Open()))
                writer.Write("RESTORED");
            using (var writer = new StreamWriter(zip.CreateEntry("Calradia.json").Open()))
                writer.Write("{}");
            using (var writer = new StreamWriter(zip.CreateEntry("../../evil.txt").Open()))
                writer.Write("x");
        }

        _service.Restore(new BackupEntry(crafted, "Calradia", DateTime.UtcNow, 0));

        Assert.Equal("RESTORED", Read("Calradia.sav"));
        Assert.Empty(Directory.GetFiles(_root, "evil.txt", SearchOption.AllDirectories));
    }

    [Fact]
    public void A_restore_that_fails_halfway_puts_the_old_files_back()
    {
        WriteWorld("Calradia", "FIRST");
        var entry = _service.Create("Calradia");
        WriteWorld("Calradia", "CURRENT");
        // The live .json is locked, so it cannot be moved aside.
        using (new FileStream(Path.Combine(_saves, "Calradia.json"), FileMode.Open, FileAccess.Read, FileShare.Read))
            Assert.ThrowsAny<IOException>(() => _service.Restore(entry));

        Assert.Equal("CURRENT", Read("Calradia.sav"));
        Assert.Equal("{\"Players\":[]}", Read("Calradia.json"));
        Assert.Empty(Directory.GetFiles(_saves, "*.rollback"));
    }

    [Fact]
    public void Coop_s_own_copy_goes_back_the_same_safe_way_and_keeps_the_launcher_json()
    {
        WriteWorld("Calradia", "CURRENT", launcher: "{\"Modules\":[\"A\"]}");
        File.WriteAllText(Path.Combine(_saves, "Calradia.backup1.sav"), "COPY");
        File.WriteAllText(Path.Combine(_saves, "Calradia.backup1.json"), "{\"Players\":[2]}");

        _service.RestoreCopy("Calradia", Path.Combine(_saves, "Calradia.backup1.sav"));

        Assert.Equal("COPY", Read("Calradia.sav"));
        Assert.Equal("{\"Players\":[2]}", Read("Calradia.json"));
        Assert.Equal("{\"Modules\":[\"A\"]}", Read("Calradia.launcher.json"));
        Assert.Equal("COPY", Read("Calradia.backup1.sav"));
        Assert.Equal(BackupReason.BeforeRestore, Assert.Single(_service.List("Calradia")).Reason);
    }

    [Fact]
    public void A_missing_backup_file_is_refused()
    {
        Assert.Throws<FileNotFoundException>(() => _service.Restore(new BackupEntry(Path.Combine(_root, "none.zip"), "Calradia", DateTime.UtcNow, 0)));
        Assert.Empty(_service.List("NeverBackedUp"));
        Assert.Empty(_service.ListAll());
    }

    [Theory]
    [InlineData("../out")]
    [InlineData("sub/folder")]
    [InlineData("")]
    [InlineData("CON")]
    [InlineData(" padded")]
    public void Unsafe_names_are_refused(string name)
    {
        Assert.False(SaveSet.IsValidSaveName(name));
        Assert.Throws<ArgumentException>(() => _service.FolderFor(name));
    }

    [Theory]
    [InlineData("en bastan")]
    [InlineData("saveauto11")]
    [InlineData("Campaign_20261017_1200")]
    public void Real_save_names_are_accepted(string name) => Assert.True(SaveSet.IsValidSaveName(name));

    [Fact]
    public void Coodsz_Host_keeps_its_backups_next_to_Game_Saves()
    {
        var service = CoodszHost.Services.SaveCatalog.Backups(_saves);
        Assert.Equal(_backups, service.BackupRoot);
        WriteWorld("Calradia");
        Assert.Equal(BackupReason.Manual, CoodszHost.Services.SaveCatalog.Backup(_saves, "Calradia").Reason);
    }
}
