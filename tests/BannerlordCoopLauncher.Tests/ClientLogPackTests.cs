using System.IO.Compression;
using TaomCoop.Core;

namespace BannerlordCoopLauncher.Tests;

public class ClientLogPackTests
{
    [Fact]
    public void The_zip_collects_the_client_logs_and_leaves_out_saves_and_shader_cache()
    {
        var root = Path.Combine(Path.GetTempPath(), "taom-logs-" + Guid.NewGuid().ToString("N"));
        var game = Path.Combine(root, "Bannerlord");
        var documents = Path.Combine(root, "Documents");
        var programData = Path.Combine(root, "ProgramData");
        var zip = Path.Combine(root, "taom-client-logs.zip");
        Directory.CreateDirectory(Path.Combine(game, "bin", "Win64_Shipping_Client"));
        Directory.CreateDirectory(Path.Combine(documents, "Coop Crash Reports"));
        Directory.CreateDirectory(Path.Combine(documents, "Logs", "TAOM.CoopCompat"));
        Directory.CreateDirectory(Path.Combine(game, "Modules", "TAOM.Dependencies"));
        Directory.CreateDirectory(Path.Combine(documents, "Game Saves"));
        Directory.CreateDirectory(Path.Combine(programData, "logs"));
        Directory.CreateDirectory(Path.Combine(programData, "crashes", "2026-09-26"));
        Directory.CreateDirectory(Path.Combine(programData, "Shaders", "TerrainShaders", "TAOM_Map"));
        File.WriteAllText(Path.Combine(game, "bin", "Win64_Shipping_Client", "Coop_client.log"), "client");
        File.WriteAllText(Path.Combine(documents, "Coop Crash Reports", "report.txt"), "report");
        var report = Path.Combine(documents, "Coop Crash Reports", "2026-09-27_client");
        Directory.CreateDirectory(Path.Combine(report, "logs"));
        File.WriteAllText(Path.Combine(report, "report.txt"), "nested");
        File.WriteAllText(Path.Combine(report, "logs", "Coop_client.log"), "nested-log");
        using (var dump = new FileStream(Path.Combine(report, "dump.dmp"), FileMode.CreateNew, FileAccess.Write, FileShare.None))
            dump.SetLength(ClientLogPack.MaxFileBytes + 1);
        File.WriteAllText(Path.Combine(report, "save001.sav"), "campaign");
        var windowsDumps = Path.Combine(root, "CrashDumps");
        Directory.CreateDirectory(windowsDumps);
        File.WriteAllBytes(Path.Combine(windowsDumps, "Bannerlord.exe.1.dmp"), new byte[] { 0x4D, 0x44, 0x4D, 0x50 });
        File.WriteAllText(Path.Combine(programData, "logs", "rgl_log_19612.txt"), "rgl");
        File.WriteAllText(Path.Combine(programData, "crashes", "2026-09-26", "report.txt"), "crash");
        File.WriteAllText(Path.Combine(documents, "Logs", "TAOM.CoopCompat", "coopcompat-12.log"), "compat");
        File.WriteAllBytes(Path.Combine(game, "Modules", "TAOM.Dependencies", "patchshield-disabled.flag"), Array.Empty<byte>());
        File.WriteAllText(Path.Combine(documents, "Game Saves", "save001.sav"), "campaign");
            File.WriteAllBytes(Path.Combine(programData, "Shaders", "TerrainShaders", "TAOM_Map", "compressed_shader_cache.sack"), new byte[] { 1, 2, 3 });
            var huge = Path.Combine(documents, "Coop Crash Reports", "big.rar");
            using (var stream = new FileStream(huge, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                stream.SetLength(ClientLogPack.MaxFileBytes + 1);
            var action = Path.Combine(root, "client-action.log");
            File.WriteAllText(action, "Connect  steam");
            File.WriteAllText(Path.Combine(game, "bin", "Win64_Shipping_Client", "BootPatches.log"), "butter");
            try
            {
                var result = ClientLogPack.Create(zip, game, documents, programData, windowsDumps, activityLog: "12:00  Connect", actionLogPath: action);
                Assert.Equal(13, result.Included);
                Assert.True(result.DumpIncluded);
                Assert.Contains(result.Skipped, line => line.Contains("big.rar"));
                using var archive = ZipFile.OpenRead(zip);
                var names = archive.Entries.Select(entry => entry.FullName).ToList();
                Assert.Contains("game/Coop_client.log", names);
                Assert.Contains("game/BootPatches.log", names);
                Assert.Contains("crash-reports/report.txt", names);
                Assert.Contains("crash-reports/2026-09-27_client/report.txt", names);
                Assert.Contains("crash-reports/2026-09-27_client/logs/Coop_client.log", names);
                Assert.Contains("crash-reports/2026-09-27_client/dump.dmp", names);
                Assert.Contains("windows-crash-dumps/Bannerlord.exe.1.dmp", names);
                Assert.Contains("engine-logs/rgl_log_19612.txt", names);
                Assert.Contains("hard-crashes/report.txt", names);
                Assert.Contains("compat/coopcompat-12.log", names);
                Assert.Contains("modules/patchshield-disabled.flag", names);
                Assert.Contains("launcher/client-action.log", names);
                Assert.Contains("launcher/activity-log.txt", names);
                Assert.Contains("NOTES.txt", names);
            Assert.DoesNotContain(names, name => name.Contains(".sav") || name.Contains(".sack") || name.Contains("big.rar"));
            Assert.Contains(result.Skipped, line => line.Contains("save001.sav"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }


    [Fact]
    public void A_crash_zip_is_unpacked_and_cleaned_and_a_rar_is_left_out_with_a_note()
    {
        var root = Path.Combine(Path.GetTempPath(), "taom-crashzip-" + Guid.NewGuid().ToString("N"));
        var documents = Path.Combine(root, "Documents");
        var reports = Path.Combine(documents, "Coop Crash Reports");
        Directory.CreateDirectory(reports);
        var crashZip = Path.Combine(reports, "crash1.zip");
        using (var inner = ZipFile.Open(crashZip, ZipArchiveMode.Create))
        {
            using var writer = new StreamWriter(inner.CreateEntry("logs/Coop_client.log").Open());
            writer.Write("Command Args: /cooppassword hunter2");
        }

        File.SetLastWriteTimeUtc(crashZip, DateTime.UtcNow.AddHours(-1));
        File.WriteAllBytes(Path.Combine(reports, "crash2.rar"), new byte[] { 1, 2, 3 });
        var zip = Path.Combine(root, "out.zip");
        try
        {
            // The rar is the newest archive, so it is left out with a note.
            var result = ClientLogPack.Create(zip, null, documents, Path.Combine(root, "ProgramData"));
            Assert.Contains(result.Skipped, line => line.Contains("crash2.rar") && line.Contains("cannot be cleaned"));
            using (var archive = ZipFile.OpenRead(zip))
            {
                Assert.DoesNotContain(archive.Entries, entry => entry.FullName.Contains("crash2.rar"));
                var notes = archive.GetEntry("NOTES.txt")!;
                using var reader = new StreamReader(notes.Open());
                Assert.Contains("crash2.rar was left out", reader.ReadToEnd());
            }

            File.Delete(Path.Combine(reports, "crash2.rar"));
            ClientLogPack.Create(zip, null, documents, Path.Combine(root, "ProgramData"));
            using var packed = ZipFile.OpenRead(zip);
            var entry = packed.GetEntry("crash-reports/crash1/logs/Coop_client.log");
            Assert.NotNull(entry);
            using var text = new StreamReader(entry!.Open());
            var cleaned = text.ReadToEnd();
            Assert.DoesNotContain("hunter2", cleaned);
            Assert.Contains("/cooppassword", cleaned);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
    [Fact]
    public void Fix_low_FPS_creates_the_empty_patch_shield_flag_and_leaves_it_in_place()
    {
        var root = Path.Combine(Path.GetTempPath(), "taom-fps-" + Guid.NewGuid().ToString("N"));
        var folder = Path.Combine(root, "Modules", "TAOM.Dependencies");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "SubModule.xml"), "<Module/>");
        try
        {
            Assert.True(Patchshield.Disable(root));
            var path = Path.Combine(folder, Patchshield.FileName);
            Assert.Equal(0, new FileInfo(path).Length);
            Assert.False(Patchshield.Disable(root));
            Assert.True(Patchshield.Enable(root));
            Assert.False(File.Exists(path));
            Assert.False(Patchshield.Enable(root));
            Assert.Equal("<Module/>", File.ReadAllText(Path.Combine(folder, "SubModule.xml")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void The_host_zip_keeps_the_engine_log_and_removes_the_password()
    {
        var root = Path.Combine(Path.GetTempPath(), "taom-host-logs-" + Guid.NewGuid().ToString("N"));
        var server = Path.Combine(root, "server");
        var data = Path.Combine(root, "data");
        var programData = Path.Combine(root, "ProgramData");
        var zip = Path.Combine(root, "host-logs.zip");
        Directory.CreateDirectory(Path.Combine(data, "logs", "TAOM.CoopCompat"));
        Directory.CreateDirectory(Path.Combine(data, "Game Saves"));
        Directory.CreateDirectory(Path.Combine(server, "engine", "bin", "Win64_Shipping_Server", "Logs"));
        Directory.CreateDirectory(Path.Combine(server, "engine", "Modules", "TAOM.Dependencies"));
        Directory.CreateDirectory(Path.Combine(programData, "logs"));
        File.WriteAllText(Path.Combine(data, "logs", "TAOM.CoopCompat", "coopcompat-1.log"), "compat");
        File.WriteAllText(Path.Combine(data, "logs", "host-console-20261004-120000.log"), "player joined from 10.1.2.3");
        Directory.CreateDirectory(Path.Combine(programData, "crashes", "2026-10-04_server"));
        File.WriteAllText(Path.Combine(programData, "crashes", "2026-10-04_server", "crash_tags.txt"), "Win64_Shipping_Server");
        File.WriteAllText(Path.Combine(programData, "crashes", "2026-10-04_server", "dump.dmp"), "memory");
        File.WriteAllText(Path.Combine(data, "Game Saves", "Byrthraui.sav"), "campaign");
        File.WriteAllText(Path.Combine(server, "engine", "bin", "Win64_Shipping_Server", "Logs", "taom_debug_1.log"), "debug");
        File.WriteAllBytes(Path.Combine(server, "engine", "Modules", "TAOM.Dependencies", Patchshield.FileName), Array.Empty<byte>());
        File.WriteAllText(Path.Combine(programData, "logs", "rgl_log_10.txt"), "[DedicatedServer Engine] serving\n");
        File.WriteAllText(Path.Combine(programData, "logs", "rgl_log_11.txt"), "single player menu\n");
        File.WriteAllText(Path.Combine(data, "server-config.json"), """
            { "saveName": "Byrthraui", "password": "secret-word" }
            """);
        var modLogs = Path.Combine(root, "ModLogs");
        Directory.CreateDirectory(modLogs);
        File.WriteAllText(Path.Combine(modLogs, "CoopModPatch-client.log"), "IG client from 10.4.5.6");
        var igSettings = BannerlordCoopLauncher.Core.Support.LogZipBuilder.IgSettingsPath(Path.Combine(server, "server-data"));
        Directory.CreateDirectory(Path.GetDirectoryName(igSettings)!);
        File.WriteAllText(igSettings, "WageLimit=900");
        try
        {
            var result = HostLogPack.Create(zip, server, data, programData, modLogsRoot: modLogs, steamLibraries: []);
            using var archive = ZipFile.OpenRead(zip);
            var names = archive.Entries.Select(entry => entry.FullName).ToList();
            Assert.Contains("coopmodpatch/CoopModPatch-client.log", names);
            Assert.Contains("coopmodpatch/ig-settings.txt", names);
            Assert.DoesNotContain("coopmodpatch/CoopModPatch-server.log", names);
            using (var patch = new StreamReader(archive.GetEntry("coopmodpatch/CoopModPatch-client.log")!.Open()))
                Assert.DoesNotContain("10.4.5.6", patch.ReadToEnd());
            Assert.Contains("server-logs/coopcompat-1.log", names);
            Assert.Contains("taom-debug/taom_debug_1.log", names);
            Assert.Contains("engine-logs/rgl_log_10.txt", names);
            Assert.Contains("taom-dependencies/patchshield-disabled.flag", names);
            Assert.DoesNotContain(names, name => name.Contains("rgl_log_11") || name.Contains(".sav"));
            var config = archive.GetEntry("host/server-config.redacted.json");
            Assert.NotNull(config);
            using var reader = new StreamReader(config.Open());
            var text = reader.ReadToEnd();
            Assert.Contains("Byrthraui", text);
            Assert.DoesNotContain("secret-word", text);
            Assert.True(result.Included >= 4);
            Assert.Contains("host-console/host-console-20261004-120000.log", names);
            Assert.Contains("crashes/2026-10-04_server/crash_tags.txt", names);
            Assert.DoesNotContain(names, name => name.EndsWith(".dmp"));
            Assert.False(result.DumpIncluded);
            using (var console = new StreamReader(archive.GetEntry("host-console/host-console-20261004-120000.log")!.Open()))
                Assert.DoesNotContain("10.1.2.3", console.ReadToEnd());

            Assert.Single(HostLogPack.Dumps(programData, null));
            var withDumps = HostLogPack.Create(Path.Combine(root, "with-dumps.zip"), server, data, programData, includeDumps: true, modLogsRoot: modLogs, steamLibraries: []);
            Assert.True(withDumps.DumpIncluded);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
