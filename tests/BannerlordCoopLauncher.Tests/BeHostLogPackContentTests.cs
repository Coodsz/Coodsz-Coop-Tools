using System.IO.Compression;
using static BurningEmpires.Core.Tests.BeServerFixtures;

namespace BurningEmpires.Core.Tests;

public class BeHostLogPackContentTests
{
    private static Dictionary<string, string> Read(string zipPath)
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
    public void TheHostPackKeepsTheConsoleListsBeSavedataAndScrubsThePassword()
    {
        var root = NewRoot("host-pack");
        try
        {
            var server = Path.Combine(root, "server");
            var data = Path.Combine(root, "data");
            Write(BeHostFolders.ServerConfig(data), "{\n  \"saveName\": \"BETest\",\n  \"password\": \"hunter2secret\"\n}\n");
            Write(Path.Combine(BeHostFolders.Logs(data), "Coop_server.log"), "coop server log line");
            Write(Path.Combine(server, "modded-server-modules.txt"), string.Join("\r\n", ServerOrder));
            Write(Path.Combine(server, BeServerPrepare.PolicyFile), BeServerPrepare.Policy);
            Write(Path.Combine(BeSaveData.ServerModule(server), "BESavedata", "campaigns", "c1", "save_slots", "BETest", "index.json"), "world secrets");
            var window = "[BurningEmpires.CoopCompat] Burning Empires coop patch ready (125 hooks).\n[DedicatedServer] SERVING";
            var zipPath = Path.Combine(root, "be-host-logs.zip");

            BeHostLogPack.Create(zipPath, server, data, Path.Combine(root, "programdata"), window, steamLibraries: []);
            var entries = Read(zipPath);
            var all = string.Join("\n", entries.Values);

            Assert.Contains("patch ready (125 hooks)", entries["host/server-window.txt"]);
            Assert.Contains("BurningEmpires.CoopCompat", entries["host/modded-server-modules.txt"]);
            Assert.Contains("Bannerlord.MBOptionScreen", entries["host/" + BeServerPrepare.PolicyFile]);
            Assert.Contains("index.json", entries["host/besavedata-files.txt"]);
            Assert.Contains(entries.Keys, key => key.EndsWith("Coop_server.log", StringComparison.Ordinal));
            Assert.Contains("BETest", entries["host/server-config.redacted.json"]);
            Assert.DoesNotContain("hunter2secret", all);
            Assert.DoesNotContain("world secrets", all);
            Assert.Contains(entries.Values, text => text.Contains(BeHostLogPack.Title));
            Assert.DoesNotContain(entries.Keys, key => key.Contains("taom", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void TheClientPackPicksTheServerLogsOfThisPc()
    {
        var root = NewRoot("client-pack");
        try
        {
            var data = Path.Combine(root, "data");
            Write(Path.Combine(BeHostFolders.Logs(data), "Coop_server.log"), "coop");
            Write(Path.Combine(BeHostFolders.Logs(data), "host-console-20261026-010000.log"), "console");
            Write(Path.Combine(BeHostFolders.Logs(data), "unrelated.txt"), "x");

            var picks = BeClientLogPack.ServerLogs([new BeServerFolder(Path.Combine(root, "server"), data)]);

            Assert.Equal(2, picks.Files.Count);
            Assert.All(picks.Files, pick => Assert.StartsWith("server/logs/", pick.Entry));
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void ServerFoldersComeFromBeServerJsonAndRealServerFolders()
    {
        var root = NewRoot("folders");
        try
        {
            var server = Path.Combine(root, "elsewhere", "Server");
            Directory.CreateDirectory(server);
            var settings = Write(Path.Combine(root, "tool", BeServerFolders.SettingsFile), "{\"ServerRoot\":" + System.Text.Json.JsonSerializer.Serialize(server) + ",\"Password\":\"x\"}");
            var drive = Path.Combine(root, "drive");
            var script = Path.Combine(root, "Documents", "CoopData", "DedicatedServer");
            Directory.CreateDirectory(script);

            // A drive without a Burning Empires server folder adds nothing, even with the shared data folder there.
            Assert.Single(BeServerFolders.Find([drive], [settings], script));

            Directory.CreateDirectory(Path.Combine(BeHostFolders.DefaultServerRoot(drive), "engine", "Modules", BeServerPath.BeModuleId));
            var found = BeServerFolders.Find([drive], [settings], script);
            Assert.Equal(2, found.Count);
            Assert.Contains(found, folder => folder.DataRoot == script);
        }
        finally
        {
            Delete(root);
        }
    }
}
