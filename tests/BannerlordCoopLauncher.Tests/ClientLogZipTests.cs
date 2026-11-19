using System.IO.Compression;
using BannerlordCoopLauncher.Core.Launch;

namespace BannerlordCoopLauncher.Tests;

public class ClientLogZipTests
{
    [Fact]
    public void The_zip_collects_client_logs_and_leaves_out_saves()
    {
        var root = Path.Combine(Path.GetTempPath(), "coodsz-logs-" + Guid.NewGuid().ToString("N"));
        var game = Path.Combine(root, "Bannerlord");
        var documents = Path.Combine(root, "Documents");
        var programData = Path.Combine(root, "ProgramData");
        var zip = Path.Combine(root, "coodsz-client-logs.zip");
        var bin = Path.Combine(game, "bin", "Win64_Shipping_Client");
        Directory.CreateDirectory(bin);
        Directory.CreateDirectory(Path.Combine(documents, "Coop Crash Reports"));
        Directory.CreateDirectory(Path.Combine(documents, "Game Saves"));
        Directory.CreateDirectory(Path.Combine(programData, "logs"));
        Directory.CreateDirectory(Path.Combine(programData, "crashes", "2026-09-27"));
        File.WriteAllText(Path.Combine(bin, "Coop_client.log"), "client");
        File.WriteAllText(Path.Combine(bin, "BootPatches.log"), "butter");
        File.WriteAllText(Path.Combine(bin, "NoHarmony.txt"), "harmony");
        var report = Path.Combine(documents, "Coop Crash Reports", "2026-09-27_client");
        Directory.CreateDirectory(Path.Combine(report, "logs"));
        File.WriteAllText(Path.Combine(report, "report.txt"), "nested");
        File.WriteAllText(Path.Combine(report, "logs", "rgl_log_1.txt"), "rgl-report");
        File.WriteAllText(Path.Combine(report, "save001.sav"), "campaign");
        File.WriteAllText(Path.Combine(programData, "logs", "rgl_log_8744.txt"), "engine");
        File.WriteAllText(Path.Combine(programData, "crashes", "2026-09-27", "report.txt"), "hard");
        File.WriteAllText(Path.Combine(documents, "Game Saves", "save001.sav"), "campaign");
        var huge = Path.Combine(documents, "Coop Crash Reports", "big.txt");
        using (var stream = new FileStream(huge, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            stream.SetLength(ClientLogZip.MaxFileBytes + 1);
        try
        {
            var action = Path.Combine(root, "client-action.log");
            File.WriteAllText(action, "Connect  direct");
            var result = ClientLogZip.Create(zip, game, documents, programData, activityLog: "14:56  Bannerlord started.", actionLogPath: action);
            // A text file over 20 MB keeps only its end.
            Assert.Equal(10, result.Included);
            Assert.DoesNotContain(result.Skipped, line => line.Contains("big.txt"));
            Assert.Contains(result.Skipped, line => line.Contains("save001.sav"));
            using var archive = ZipFile.OpenRead(zip);
            var names = archive.Entries.Select(entry => entry.FullName).ToList();
            Assert.Contains("game/Coop_client.log", names);
            Assert.Contains("game/BootPatches.log", names);
            Assert.Contains("game/NoHarmony.txt", names);
            Assert.Contains("crash-reports/2026-09-27_client/report.txt", names);
            Assert.Contains("crash-reports/2026-09-27_client/logs/rgl_log_1.txt", names);
            Assert.Contains("engine-logs/rgl_log_8744.txt", names);
            Assert.Contains("hard-crashes/report.txt", names);
            Assert.Contains("launcher/activity-log.txt", names);
            Assert.Contains("launcher/client-action.log", names);
            Assert.Contains("NOTES.txt", names);
            Assert.DoesNotContain(names, name => name.Contains(".sav"));
            Assert.Contains("crash-reports/big.txt", names);
            Assert.True(archive.GetEntry("crash-reports/big.txt")!.Length <= ClientLogZip.MaxFileBytes + 100);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    // Coop 0.1.7 writes Coop_client_<pid>.log when Coop_client.log is taken. Send my logs packs the newest three, never a
    // .log.lock, and the known causes read them as the Coop client log.
    [Fact]
    public void The_newest_Coop_client_pid_logs_are_packed_and_read_as_the_Coop_client_log()
    {
        var root = Path.Combine(Path.GetTempPath(), "coodsz-logs-pid-" + Guid.NewGuid().ToString("N"));
        var game = Path.Combine(root, "Bannerlord");
        var bin = Path.Combine(game, "bin", "Win64_Shipping_Client");
        var zip = Path.Combine(root, "coodsz-client-logs.zip");
        Directory.CreateDirectory(bin);
        try
        {
            File.WriteAllText(Path.Combine(bin, "Coop_client.log"), "main log");
            var now = DateTime.UtcNow;
            foreach (var (pid, age) in new[] { (101, 4), (102, 3), (103, 2), (104, 1) })
            {
                var path = Path.Combine(bin, "Coop_client_" + pid + ".log");
                File.WriteAllText(path, "pid log " + pid);
                File.SetLastWriteTimeUtc(path, now.AddMinutes(-age));
            }
            File.WriteAllText(Path.Combine(bin, "Coop_client_104.log.lock"), "lock");

            ClientLogZip.Create(zip, game, Path.Combine(root, "Documents"), Path.Combine(root, "ProgramData"));
            using (var archive = ZipFile.OpenRead(zip))
            {
                var names = archive.Entries.Select(entry => entry.FullName).ToList();
                Assert.Contains("game/Coop_client.log", names);
                Assert.Contains("game/Coop_client_104.log", names);
                Assert.Contains("game/Coop_client_103.log", names);
                Assert.Contains("game/Coop_client_102.log", names);
                Assert.DoesNotContain("game/Coop_client_101.log", names);
                Assert.DoesNotContain(names, name => name.EndsWith(".lock", StringComparison.OrdinalIgnoreCase));
            }

            var texts = BannerlordCoopLauncher.Core.Support.CrashEvidence.FromZip(zip, "client")
                .TextsOf(BannerlordCoopLauncher.Core.Support.CrashLogSource.CoopClient);
            // Older pid logs are packed for people but are not this run's evidence for the known causes.
            Assert.Equal(["main log", "pid log 104"], texts);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void The_join_password_is_taken_out_of_every_place_the_client_zip_packs()
    {
        var root = Path.Combine(Path.GetTempPath(), "coodsz-logs-pw-" + Guid.NewGuid().ToString("N"));
        var game = Path.Combine(root, "Bannerlord");
        var documents = Path.Combine(root, "Documents");
        var programData = Path.Combine(root, "ProgramData");
        var zip = Path.Combine(root, "coodsz-client-logs.zip");
        var bin = Path.Combine(game, "bin", "Win64_Shipping_Client");
        var report = Path.Combine(documents, "Coop Crash Reports", "2026-10-18_client");
        Directory.CreateDirectory(bin);
        Directory.CreateDirectory(report);
        Directory.CreateDirectory(Path.Combine(programData, "logs"));
        File.WriteAllText(Path.Combine(bin, "rgl_log_5.txt"), "Command Args: /client /cooppassword \"two words\" _MODULES_*Native*_MODULES_");
        File.WriteAllText(Path.Combine(programData, "logs", "rgl_log_6.txt"), "Command Args: /client /cooppassword hunter2 _MODULES_*Coop*_MODULES_");
        File.WriteAllText(Path.Combine(programData, "logs", "watchdog_log_6.txt"), "#TW#Arguments#TW#/cooppassword hunter2 _MODULES_");
        File.WriteAllText(Path.Combine(report, "launch.ini"), "password=hunter2");
        using (var inner = ZipFile.Open(Path.Combine(report, "extra.zip"), ZipArchiveMode.Create))
        {
            using var writer = new StreamWriter(inner.CreateEntry("rgl_log_7.txt").Open());
            writer.Write("Command Args: /cooppassword hunter2");
        }

        try
        {
            ClientLogZip.Create(zip, game, documents, programData,
                activityLog: "COODSZ_COOP_PASSWORD = \"two words\"\nCOODSZ_COOP_PASSWORD=hunter2\n{ \"password\": \"two words\" }");
            using var archive = ZipFile.OpenRead(zip);
            Assert.Contains("crash-reports/2026-10-18_client/launch.ini", archive.Entries.Select(entry => entry.FullName));
            Assert.Contains("crash-reports/2026-10-18_client/extra.zip/rgl_log_7.txt", archive.Entries.Select(entry => entry.FullName));
            foreach (var entry in archive.Entries)
            {
                using var reader = new StreamReader(entry.Open());
                var text = reader.ReadToEnd();
                Assert.DoesNotContain("hunter2", text);
                Assert.DoesNotContain("two words", text);
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void A_quoted_password_value_goes_as_a_whole_and_the_next_line_stays()
    {
        var text = BannerlordCoopLauncher.Core.Support.ProblemReport.Redact("$env:COODSZ_COOP_PASSWORD = 'two words'\npassword:\nNext line");
        Assert.DoesNotContain("two words", text);
        Assert.DoesNotContain("words", text);
        Assert.Contains("Next line", text);
    }
}
