using System.IO.Compression;
using BurningEmpires.Core;
using BurningEmpiresCoopCompat;
using static BurningEmpires.Core.Tests.BeServerFixtures;

namespace BannerlordCoopLauncher.Tests;

// Burning Empires Coop Compat writes its lines to coopcompat.log of its own (CompatLogFile.cs,
// linked), because live run 3 left no client rgl log with them. Covers where the file goes, its
// size cap, and that both log packs pick it up from that same place.
public sealed class BeCompatLogFileTests
{
    private static Dictionary<string, string> ReadZip(string zipPath)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        return zip.Entries.Where(entry => !entry.FullName.EndsWith('/')).ToDictionary(
            entry => entry.FullName,
            entry =>
            {
                using var reader = new StreamReader(entry.Open());
                return reader.ReadToEnd();
            });
    }

    [Fact]
    public void The_module_writes_where_the_log_packs_look()
    {
        var documents = @"C:\Users\Player\Documents";
        var data = @"C:\Users\Player\Documents\Mount and Blade II Bannerlord\CoopData\DedicatedServer";

        Assert.Equal(BeClientLogPack.CompatLogs(Path.Combine(documents, "Mount and Blade II Bannerlord")),
            CompatLogFile.Folder(null, documents));
        Assert.Equal(BeClientLogPack.ServerCompatLogs(BeHostFolders.Logs(data)), CompatLogFile.Folder(data, documents));
        Assert.Matches("^coopcompat.*\\.log$", CompatLogFile.FileName);
        Assert.Matches("^coopcompat.*\\.log$", CompatLogFile.OldFileName);
    }

    [Fact]
    public void No_documents_folder_gives_no_folder_rather_than_a_relative_one()
    {
        Assert.Null(CompatLogFile.Folder(null, ""));
        Assert.Null(CompatLogFile.Folder(null, null));
        Assert.Null(CompatLogFile.Folder("", "Documents"));
    }

    [Fact]
    public void A_full_log_moves_to_the_old_file_so_the_two_stay_small()
    {
        var root = NewRoot("compat-log");
        try
        {
            var path = Path.Combine(root, CompatLogFile.FileName);
            using (var log = new CompatLogFile(path, 1000))
            {
                for (var i = 0; i < 200; i++)
                    log.Append("[BurningEmpires.CoopCompat] line " + i);
                // The packs read the file while the game still has it open.
                using var reader = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                Assert.True(reader.Length > 0);
            }

            var old = Path.Combine(root, CompatLogFile.OldFileName);
            Assert.True(File.Exists(old));
            Assert.True(new FileInfo(path).Length < 1100);
            Assert.True(new FileInfo(old).Length < 1100);
            Assert.Contains("line 199", File.ReadAllText(path));
            Assert.Equal(2, Directory.GetFiles(root).Length);
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void An_old_file_held_open_makes_the_log_start_over_and_keep_its_lines()
    {
        var root = NewRoot("compat-log-held");
        try
        {
            var path = Path.Combine(root, CompatLogFile.FileName);
            var old = Write(Path.Combine(root, CompatLogFile.OldFileName), "older run");
            // An editor or scanner holding coopcompat.old.log without delete sharing.
            using (new FileStream(old, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var log = new CompatLogFile(path, 1000))
            {
                for (var i = 0; i < 200; i++)
                    log.Append("[BurningEmpires.CoopCompat] line " + i);
            }

            Assert.Equal("older run", File.ReadAllText(old));
            Assert.True(new FileInfo(path).Length < 1100);
            Assert.Contains("line 199", File.ReadAllText(path));
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void The_client_pack_has_the_client_and_server_compat_logs()
    {
        var root = NewRoot("compat-client-pack");
        try
        {
            var documents = Path.Combine(root, "docs");
            var data = Path.Combine(root, "data");
            Write(Path.Combine(BeClientLogPack.CompatLogs(documents), CompatLogFile.FileName), "client: Burning Empires coop patch ready (125 hooks).");
            Write(Path.Combine(BeClientLogPack.CompatLogs(documents), CompatLogFile.OldFileName), "client older line");
            Write(Path.Combine(BeClientLogPack.ServerCompatLogs(BeHostFolders.Logs(data))!, CompatLogFile.FileName), "server: Burning Empires coop patch ready (128 hooks).");

            var zipPath = Path.Combine(root, "client.zip");
            BeClientLogPack.Create(zipPath, null, documents, Path.Combine(root, "programdata"),
                servers: [new BeServerFolder(Path.Combine(root, "server"), data)]);
            var zip = ReadZip(zipPath);

            Assert.Contains("125 hooks", zip["compat/" + CompatLogFile.FileName]);
            Assert.Contains("client older line", zip["compat/" + CompatLogFile.OldFileName]);
            Assert.Contains("128 hooks", zip["server/logs/compat/" + CompatLogFile.FileName]);
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void The_host_pack_has_the_server_compat_log_once()
    {
        var root = NewRoot("compat-host-pack");
        try
        {
            var data = Path.Combine(root, "data");
            var logs = BeHostFolders.Logs(data);
            for (var i = 0; i < 12; i++)
            {
                var coop = Write(Path.Combine(logs, "Coop_server_" + i + ".log"), "coop " + i);
                File.SetLastWriteTimeUtc(coop, DateTime.UtcNow.AddHours(-1).AddMinutes(i));
            }
            // The compat logs are the newest, so without the pack leaving their folder out of the
            // newest 12 *.log pick they would take 2 of its places and push 2 Coop logs out. (The
            // zip writes a file with the same text once, so a second copy would not show.)
            Write(Path.Combine(BeClientLogPack.ServerCompatLogs(logs)!, CompatLogFile.OldFileName), "server: older run");
            Write(Path.Combine(BeClientLogPack.ServerCompatLogs(logs)!, CompatLogFile.FileName), "server: Burning Empires coop patch ready (128 hooks).");

            var zipPath = Path.Combine(root, "host.zip");
            BeHostLogPack.Create(zipPath, Path.Combine(root, "server"), data, Path.Combine(root, "programdata"), steamLibraries: []);
            var zip = ReadZip(zipPath);

            Assert.Contains("128 hooks", zip["compat/" + CompatLogFile.FileName]);
            Assert.Contains("older run", zip["compat/" + CompatLogFile.OldFileName]);
            Assert.DoesNotContain(zip.Keys, key => key.StartsWith("server-logs/", StringComparison.Ordinal)
                && (key.EndsWith(CompatLogFile.FileName, StringComparison.Ordinal) || key.EndsWith(CompatLogFile.OldFileName, StringComparison.Ordinal)));
            Assert.Single(zip.Keys, key => key.EndsWith("/" + CompatLogFile.FileName, StringComparison.Ordinal));
            Assert.Single(zip.Keys, key => key.EndsWith("/" + CompatLogFile.OldFileName, StringComparison.Ordinal));
            Assert.Equal(12, zip.Keys.Count(key => key.StartsWith("server-logs/Coop_server_", StringComparison.Ordinal)));
        }
        finally
        {
            Delete(root);
        }
    }
}
