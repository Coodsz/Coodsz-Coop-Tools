using System.IO.Compression;
using BannerlordCoopLauncher.Core.Support;
using RealmOfThrones.Core;
using static BurningEmpires.Core.Tests.BeServerFixtures;

namespace BannerlordCoopLauncher.Tests;

// The Realm of Thrones log packs for Send my logs and the crash reports (RotHostLogPack, RotClientLogPack).
public class RotLogPackTests
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
    public void The_host_pack_keeps_coop_server_log_the_compat_log_and_scrubs_the_password()
    {
        var root = NewRoot("rot-host-pack");
        try
        {
            var server = Path.Combine(root, "server");
            var data = Path.Combine(root, "CoopData", "DedicatedServer");
            var logs = Path.Combine(data, "logs");
            Write(RotHostLogPack.ServerConfig(data), "{\n  \"saveName\": \"RotTest\",\n  \"password\": \"hunter2secret\"\n}\n");
            Write(Path.Combine(logs, "Coop_server.log"), "coop server log line, join password hunter2secret");
            Write(Path.Combine(logs, "host-console-20261112-010000.log"), "console");
            Write(Path.Combine(logs, "host-action.log"), "action");
            Write(Path.Combine(logs, RotStack.CompatModuleId, "coopcompat.log"), "[RoT compat] ready");
            // More than the newest 12 other logs, written after Coop_server.log, still leave it in.
            for (var i = 0; i < 14; i++)
                Write(Path.Combine(logs, "other-" + i + ".log"), "other");
            var zipPath = Path.Combine(root, "rot-host-logs.zip");

            RotHostLogPack.Create(zipPath, server, data, Path.Combine(root, "programdata"), "[RoT host] Server exited with code 8.",
                limits: LogZipLimits.Full with { Secrets = ["hunter2secret"] });
            var entries = Read(zipPath);
            var all = string.Join("\n", entries.Values);

            Assert.Contains("server-logs/Coop_server.log", entries.Keys);
            Assert.Contains("[RoT compat] ready", entries["compat/coopcompat.log"]);
            Assert.Contains("host-console/host-console-20261112-010000.log", entries.Keys);
            Assert.Contains("host/host-action.log", entries.Keys);
            Assert.Contains("RotTest", entries["host/server-config.redacted.json"]);
            Assert.Contains("exited with code 8", entries["host/server-window.txt"]);
            Assert.DoesNotContain("hunter2secret", all);
            Assert.Contains(entries.Values, text => text.Contains(RotHostLogPack.Title));
            // The compat log is in compat only, not again under server-logs.
            Assert.DoesNotContain(entries.Keys, key => key.StartsWith("server-logs/", StringComparison.Ordinal) && key.EndsWith("coopcompat.log", StringComparison.Ordinal));
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void The_client_pack_has_the_compat_log_and_the_server_logs_of_this_pc_without_its_password()
    {
        var root = NewRoot("rot-client-pack");
        try
        {
            var documents = Path.Combine(root, "Documents", "Mount and Blade II Bannerlord");
            var data = Path.Combine(root, "CoopData", "DedicatedServer");
            var logs = Path.Combine(data, "logs");
            Write(Path.Combine(RotClientLogPack.CompatLogs(documents), "coopcompat.log"), "client compat line");
            Write(RotHostLogPack.ServerConfig(data), "{\"password\": \"joinpass77\"}");
            Write(Path.Combine(logs, "Coop_server.log"), "server says joinpass77");
            Write(Path.Combine(logs, RotStack.CompatModuleId, "coopcompat.log"), "server compat line");
            Write(Path.Combine(logs, "unrelated.txt"), "x");
            // Coop writes its own Coop_server.log into the server folder's engine folder, not the data logs.
            var serverRoot = Path.Combine(root, "RotServer");
            Write(Path.Combine(serverRoot, "engine", "bin", "Win64_Shipping_Server", "Coop_server.log"), "engine folder coop line joinpass77");
            var zipPath = Path.Combine(root, "rot-client-logs.zip");

            // The caller does not know the server's password; the pack reads it from server-config.json.
            RotClientLogPack.Create(zipPath, null, documents, Path.Combine(root, "programdata"), serverDataRoot: data, serverRoot: serverRoot);
            var entries = Read(zipPath);
            var all = string.Join("\n", entries.Values);

            Assert.Contains("client compat line", entries["compat/coopcompat.log"]);
            Assert.Contains("server/logs/Coop_server.log", entries.Keys);
            Assert.Contains("server compat line", entries["server/logs/compat/coopcompat.log"]);
            Assert.Contains("engine folder coop line", entries["server/coop-logs/Coop_server.log"]);
            Assert.DoesNotContain("joinpass77", all);
            Assert.DoesNotContain(entries.Keys, key => key.EndsWith("unrelated.txt", StringComparison.Ordinal));
            Assert.Contains(entries.Values, text => text.Contains(RotClientLogPack.Title));
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void The_client_pack_without_a_server_on_this_pc_adds_no_server_logs()
    {
        Assert.Empty(RotClientLogPack.ServerLogs(null).Files);
        Assert.Null(RotClientLogPack.ServerPassword(""));
        Assert.Same(LogZipLimits.Full, RotClientLogPack.WithServerPassword(null, null));
        // The compat module writes to Logs\RealmOfThrones.CoopCompat (CompatLogFile.FolderName).
        Assert.Contains("public const string FolderName = \"" + RotClientLogPack.CompatLogFolder + "\";",
            SourceFiles.Read(Path.Combine("compat", "RealmOfThronesCoopCompat", "CompatLogFile.cs")));
    }
}
