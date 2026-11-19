using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using BannerlordCoopLauncher.Core.Launch;
using BannerlordCoopLauncher.Core.Modules;
using BannerlordCoopLauncher.Core.Server;
using BannerlordCoopLauncher.Core.Support;
using CoodszHost.Services;
using TaomCoop.Core;

namespace BannerlordCoopLauncher.Tests;

/// <summary>
/// Crash reports: what the scrubber takes out, the text only pack that fits the worker's size cap,
/// the summary, sending to a fake endpoint, the show and hide rules of the bars and the one time
/// card, and how the six tools are wired (no log zip main button left). Nothing here reaches the
/// live worker: every send goes to a fake handler.
/// </summary>
[Collection(ModReportsSession.Name)]
public sealed class CrashReportsTests : IDisposable
{
    private const string TestEndpoint = "https://crash.example/crash";
    private const string Install = "0123456789abcdef0123456789abcdef";
    private static readonly XNamespace Wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly string[] Tools = ["BannerlordCoopLauncher", "TaomCoopLauncher", "AD1259Launcher", "BurningEmpiresLauncher", "CoodszHost", "TaomCoopHost", "BurningEmpiresHost"];
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "coodsz-crash-reports-" + Guid.NewGuid().ToString("N"));
    private readonly IReadOnlyList<string> _names = LogScrubber.PrivateNames;

    public CrashReportsTests()
    {
        Directory.CreateDirectory(_directory);
        LogScrubber.PrivateNames = ["Patricia7", "DESKTOP-QX42ZZ", "Admin"];
    }

    public void Dispose()
    {
        LogScrubber.PrivateNames = _names;
        Directory.Delete(_directory, recursive: true);
    }

    // ----- The scrubber -----

    [Theory]
    [InlineData(@"Loading C:\Users\Patricia7\Documents\Mount and Blade II Bannerlord\Configs", "Patricia7")]
    [InlineData("Path C:/Users/Patricia7/AppData/Local/Temp/x.log", "Patricia7")]
    [InlineData(@"{""path"": ""C:\\Users\\Patricia7\\Documents""}", "Patricia7")]
    [InlineData("Hello from Patricia7 on DESKTOP-QX42ZZ", "Patricia7")]
    [InlineData("Machine DESKTOP-QX42ZZ started", "DESKTOP-QX42ZZ")]
    [InlineData("Peer 192.168.1.20:4200 joined", "192.168.1.20")]
    [InlineData("Peer 2001:0db8:85a3:0000:0000:8a2e:0370:7334 joined", "2001:0db8:85a3")]
    [InlineData("Listening on fe80::1ff:fe23:4567:890a%eth0", "fe80::1ff")]
    [InlineData("Player 76561198000000001 joined", "76561198000000001")]
    [InlineData("Player [U:1:39874521] joined", "39874521")]
    [InlineData("Player STEAM_0:1:19937260 joined", "19937260")]
    [InlineData("See https://steamcommunity.com/id/patsname/ for more", "patsname")]
    [InlineData("Mail me at pat.smith+coop@example.co.uk please", "pat.smith")]
    [InlineData("Command Args: /client /cooppassword hunter2 _MODULES_*Native*", "hunter2")]
    [InlineData("Args -password \"two words\" -port 4200", "two words")]
    [InlineData("/dedicatedcustomserverauthtoken AbCdEf123456 /port 7210", "AbCdEf123456")]
    [InlineData("ServerName Coodsz\nServerPassword secretpass\nAdminPassword adminpass2", "secretpass")]
    [InlineData("ServerName Coodsz\nServerPassword secretpass\nAdminPassword adminpass2", "adminpass2")]
    [InlineData("{ \"joinPassword\": \"hunter3\" }", "hunter3")]
    [InlineData("$env:COODSZ_COOP_PASSWORD = 'two words'", "two words")]
    [InlineData("<Settings><Password>xml-secret</Password></Settings>", "xml-secret")]
    [InlineData("Config.Token = tok_9f8e7d", "tok_9f8e7d")]
    [InlineData("{ \"authToken\": \"abc.def.ghi\" }", "abc.def.ghi")]
    [InlineData("api_key=sk_live_123", "sk_live_123")]
    public void The_scrubber_takes_out_names_steam_ids_addresses_and_passwords(string text, string secret)
    {
        var cleaned = LogZipBuilder.Clean(text);
        Assert.DoesNotContain(secret, cleaned);
    }

    [Fact]
    public void The_known_passwords_go_wherever_they_are_and_short_ones_only_by_pattern()
    {
        var text = "join?pw=Sunflower77&x=1\nThe chat said Sunflower77 twice\nabc is a word";
        var cleaned = LogZipBuilder.Clean(text, ["Sunflower77", "abc"]);
        Assert.DoesNotContain("Sunflower77", cleaned);
        Assert.Contains("[password]", cleaned);
        Assert.Contains("abc is a word", cleaned);
    }

    [Fact]
    public void A_password_that_is_a_common_word_leaves_mod_ids_whole()
    {
        // Crash report #33: the password was a word inside mod ids, and the zip read [password]lord.Harmony.
        var text = "Loaded Bannerlord.Harmony and MyLordMod\npw_lord /cooppassword lord lord77 _lord_ \"lord\"";
        var cleaned = LogZipBuilder.Clean(text, ["lord"]);
        Assert.Contains("Bannerlord.Harmony", cleaned);
        Assert.Contains("MyLordMod", cleaned);
        Assert.Contains("pw_[password] /cooppassword [password] [password]77 _[password]_ \"[password]\"", cleaned);

        // Next to letters of another script it still goes.
        Assert.DoesNotContain("lord", LogZipBuilder.Clean("пароль lordвход 密码lord进入", ["lord"]));
    }

    [Fact]
    public void A_password_with_digits_symbols_or_more_than_8_letters_goes_wherever_it_is()
    {
        Assert.Equal("join[password]now", LogZipBuilder.Clean("joinSunflower77now", ["Sunflower77"]));
        Assert.Equal("Banner[password]", LogZipBuilder.Clean("Bannerkingslords", ["kingslords"]));
        Assert.Equal("x[password]y", LogZipBuilder.Clean("xpa$$y", ["pa$$"]));
    }

    [Fact]
    public void The_scrubber_keeps_what_support_needs()
    {
        var text = string.Join("\n",
            "12:34:56.789 Frame#: 120",
            "at TaleWorlds.Engine.Scene.Tick() in std::vector",
            "Assembly Coop.Core, Version=0.1.6.0, Culture=neutral, PublicKeyToken=b77a5c561934e089",
            "Loading module Admin settings from Steam",
            "Token count: 5 tokens",
            "System.NullReferenceException: Object reference not set");
        var cleaned = LogZipBuilder.Clean(text);
        Assert.Contains("12:34:56.789", cleaned);
        Assert.Contains("std::vector", cleaned);
        Assert.Contains("PublicKeyToken=b77a5c561934e089", cleaned);
        Assert.Contains("Admin settings", cleaned);
        Assert.Contains("Token count: 5 tokens", cleaned);
        Assert.Contains("System.NullReferenceException", cleaned);
    }

    [Fact]
    public void Common_words_are_never_taken_out_as_names()
    {
        Assert.Empty(LogScrubber.UsableNames(["Admin", "user", "Steam", "pc", "", null]));
        Assert.Equal(["Patricia7"], LogScrubber.UsableNames(["Patricia7", "patricia7"]));
    }

    // ----- The pack -----

    [Fact]
    public void A_text_only_pack_leaves_dumps_out_and_cleans_the_notes()
    {
        var log = Write("game.log", "user C:\\Users\\Patricia7\\x from 10.0.0.9");
        var dump = Write("game.dmp", "MDMP binary");
        var zip = Path.Combine(_directory, "out.zip");
        var builder = new LogZipBuilder(new LogZipLimits(TextOnly: true, MaxTextBytes: 1000) { Secrets = ["Sunflower77"] });
        builder.AddFile(log, "game/game.log");
        builder.AddDump(dump, "dumps/game.dmp");
        builder.AddText("launcher/activity.txt", "typed Sunflower77 in the box");
        var result = builder.Write(zip, "Notes for C:\\Users\\Patricia7\\Documents");

        using var archive = ZipFile.OpenRead(zip);
        Assert.DoesNotContain(archive.Entries, entry => entry.Name.EndsWith(".dmp", StringComparison.OrdinalIgnoreCase));
        Assert.False(result.DumpIncluded);
        Assert.Contains(result.Skipped, line => line.Contains("Crash dumps"));
        Assert.DoesNotContain("Patricia7", Read(archive, "NOTES.txt"));
        Assert.DoesNotContain("Sunflower77", Read(archive, "launcher/activity.txt"));
        Assert.DoesNotContain("10.0.0.9", Read(archive, "game/game.log"));
    }

    [Fact]
    public void A_long_log_keeps_only_its_end()
    {
        var log = Write("long.log", new string('a', 5000) + "THE END");
        var zip = Path.Combine(_directory, "long.zip");
        var builder = new LogZipBuilder(new LogZipLimits(TextOnly: true, MaxTextBytes: 1000));
        builder.AddFile(log, "long.log");
        builder.Write(zip, "notes");
        using var archive = ZipFile.OpenRead(zip);
        var text = Read(archive, "long.log");
        Assert.StartsWith("[Only the last 1 KB of this file is included.]", text);
        Assert.EndsWith("THE END", text);
        Assert.True(text.Length < 1200);
    }

    [Fact]
    public void Prepare_packs_the_client_logs_text_only_cleaned_and_lists_every_file()
    {
        var game = Path.Combine(_directory, "game");
        var bin = Path.Combine(game, "bin", "Win64_Shipping_Client");
        Directory.CreateDirectory(bin);
        File.WriteAllText(Path.Combine(bin, "Coop_client.log"), "Connecting to 203.0.113.7 as 76561198000000001 with Sunflower77\nSystem.InvalidOperationException: boom");
        var programData = Path.Combine(_directory, "programdata");
        Directory.CreateDirectory(Path.Combine(programData, "crashes", "2026-10-22"));
        File.WriteAllText(Path.Combine(programData, "crashes", "2026-10-22", "dump.dmp"), "MDMP");
        File.WriteAllText(Path.Combine(programData, "crashes", "2026-10-22", "crash_tags.txt"), "tag");
        var documents = Path.Combine(_directory, "documents");
        Directory.CreateDirectory(documents);

        var request = new CrashReportRequest
        {
            Tool = "coop",
            Side = "client",
            Reason = CrashReason.Crash,
            ToolVersion = "1.2.62",
            Coop = "v0.1.6",
            Game = "v1.4.8",
            CrashLine = "System.InvalidOperationException: boom at C:\\Users\\Patricia7",
            SuspectedMod = "Bannerlord.Diplomacy",
            Secrets = ["Sunflower77"],
            FileStem = "coodsz-client-logs",
            Collect = (zip, limits) => ClientLogZip.Create(zip, game, documents, programData, null, "activity", null, limits: limits).Skipped
        };
        var pack = CrashReports.Prepare(request, Install, Path.Combine(_directory, "packs"));

        Assert.True(File.Exists(pack.ZipPath));
        Assert.True(pack.ZipBytes <= CrashReports.MaxZipBytes);
        using (var archive = ZipFile.OpenRead(pack.ZipPath))
        {
            Assert.DoesNotContain(archive.Entries, entry => entry.Name.EndsWith(".dmp", StringComparison.OrdinalIgnoreCase));
            var log = Read(archive, "game/Coop_client.log");
            foreach (var secret in new[] { "203.0.113.7", "76561198000000001", "Sunflower77" })
                Assert.DoesNotContain(secret, log);
        }

        Assert.Contains(pack.Summary.Files, file => file.Name == "game/Coop_client.log" && file.Size > 0);
        Assert.Contains(pack.Summary.Files, file => file.Name == "NOTES.txt");
        foreach (var generated in new[] { CrashReports.ManifestName, CrashReports.ModulesName, CrashReports.EnvironmentName })
            Assert.Contains(pack.Summary.Files, file => file.Name == generated);
        Assert.Contains(pack.Skipped, line => line.Contains("Crash dumps"));
        Assert.Equal(Install, pack.Summary.Install);
        Assert.Equal("Bannerlord.Diplomacy", pack.Summary.SuspectedMod);
        Assert.DoesNotContain("Patricia7", pack.Summary.CrashLine);
    }

    [Fact]
    public void The_zip_has_a_manifest_with_hashes_and_flags_plus_modules_and_environment()
    {
        var log = Write("m.log", new string('a', 3000) + " from 10.0.0.9 the end");
        var plain = Write("p.log", "nothing private here");
        var zip = Path.Combine(_directory, "manifest.zip");
        var trace = new LogZipTrace();
        var builder = new LogZipBuilder(new LogZipLimits(TextOnly: true, MaxTextBytes: 1000) { Trace = trace });
        builder.AddFile(log, "game/m.log");
        builder.AddFile(plain, "game/p.log");
        builder.Write(zip, "notes");
        var request = new CrashReportRequest
        {
            Tool = "taom",
            Side = "server",
            ToolVersion = "1.1.42",
            Coop = "v0.1.6",
            Game = "v1.4.8",
            Modules = [("Native", "v1.4.8"), ("C:\\Users\\Patricia7\\Mods", "1"), ("TAOM", "2.0.29.5")],
            Collect = (_, _) => []
        };
        CrashReports.AddGenerated(zip, request, trace);
        CrashReports.AddGenerated(zip, request, trace);

        using var archive = ZipFile.OpenRead(zip);
        Assert.Single(archive.Entries, entry => entry.FullName == CrashReports.ManifestName);
        using var manifest = JsonDocument.Parse(Read(archive, CrashReports.ManifestName));
        var files = manifest.RootElement.GetProperty("files").EnumerateArray().ToDictionary(file => file.GetProperty("name").GetString()!);
        Assert.Equal(["NOTES.txt", "environment.json", "game/m.log", "game/p.log", "modules.json"], files.Keys.OrderBy(name => name, StringComparer.Ordinal));
        Assert.True(files["game/m.log"].GetProperty("redacted").GetBoolean());
        Assert.True(files["game/m.log"].GetProperty("truncated").GetBoolean());
        Assert.False(files["game/p.log"].GetProperty("redacted").GetBoolean());
        Assert.False(files["game/p.log"].GetProperty("truncated").GetBoolean());
        Assert.True(files["modules.json"].GetProperty("generated").GetBoolean());
        foreach (var entry in archive.Entries.Where(entry => entry.FullName != CrashReports.ManifestName))
        {
            using var stream = entry.Open();
            var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream)).ToLowerInvariant();
            Assert.Equal(hash, files[entry.FullName].GetProperty("sha256").GetString());
            Assert.Equal(entry.Length, files[entry.FullName].GetProperty("size").GetInt64());
        }

        Assert.EndsWith("the end", Read(archive, "game/m.log"));
        using var modules = JsonDocument.Parse(Read(archive, CrashReports.ModulesName));
        var list = modules.RootElement.GetProperty("modules").EnumerateArray().ToList();
        Assert.Equal(["Native", "TAOM"], list.Select(module => module.GetProperty("id").GetString()));
        Assert.Equal([1, 2], list.Select(module => module.GetProperty("order").GetInt32()));
        Assert.Equal("2.0.29.5", list[1].GetProperty("version").GetString());
        var environment = Read(archive, CrashReports.EnvironmentName);
        using var env = JsonDocument.Parse(environment);
        Assert.Equal("taom", env.RootElement.GetProperty("tool").GetString());
        Assert.Equal("server", env.RootElement.GetProperty("side").GetString());
        Assert.Equal("v0.1.6", env.RootElement.GetProperty("coop").GetString());
        Assert.StartsWith("Windows ", env.RootElement.GetProperty("os").GetString());
        foreach (var name in new[] { Environment.UserName, Environment.MachineName, "Patricia7" })
            Assert.DoesNotContain(name, environment + Read(archive, CrashReports.ModulesName), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FitToSize_marks_the_cut_files_truncated_in_the_trace()
    {
        var zip = Path.Combine(_directory, "trace.zip");
        var random = new Random(3);
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        using (var writer = new StreamWriter(archive.CreateEntry("big.log").Open()))
            writer.Write(new string(Enumerable.Range(0, 300_000).Select(_ => (char)random.Next(33, 126)).ToArray()));
        var trace = new LogZipTrace();
        Assert.NotNull(CrashReports.FitToSize(zip, 150_000, [100_000], trace));
        Assert.True(trace.Get("big.log").Truncated);
        Assert.True(CrashReports.FirstTextBytes <= CrashReports.MaxZipBytes);
        Assert.True(CrashReports.MaxZipBytes <= CrashReports.WorkerMaxZipBytes);
    }

    [Fact]
    public void The_host_packs_take_the_same_limits()
    {
        var logs = Path.Combine(_directory, "host-logs");
        Directory.CreateDirectory(logs);
        File.WriteAllText(Path.Combine(logs, "coop-server-1.log"), "Player 10.1.1.1 joined with HostPass99");
        var zip = Path.Combine(_directory, "host.zip");
        var limits = new LogZipLimits(TextOnly: true, MaxTextBytes: 4000) { Secrets = ["HostPass99"] };
        HostLogZip.Create(zip, logs, Path.Combine(_directory, "data"), [], null, "window", "console", limits: limits, modLogsRoot: Path.Combine(_directory, "nomodlogs"), steamLibraries: []);
        using (var archive = ZipFile.OpenRead(zip))
        {
            var text = Read(archive, "server-logs/coop-server-1.log");
            Assert.DoesNotContain("HostPass99", text);
            Assert.DoesNotContain("10.1.1.1", text);
        }

        var taomZip = Path.Combine(_directory, "taom.zip");
        var taomLogs = Path.Combine(_directory, "taomdata", "logs");
        Directory.CreateDirectory(taomLogs);
        File.WriteAllText(Path.Combine(taomLogs, "server.log"), "join HostPass99");
        HostLogPack.Create(taomZip, null, Path.Combine(_directory, "taomdata"), Path.Combine(_directory, "pd"), "window", limits: limits, modLogsRoot: Path.Combine(_directory, "nomodlogs"), steamLibraries: []);
        using var taom = ZipFile.OpenRead(taomZip);
        Assert.DoesNotContain("HostPass99", Read(taom, "server-logs/server.log"));
    }

    // Crash report #43 (CR-83JWK) had no Coop_server.log: Coop writes it into the engine folder the
    // server runs from, which no host pack looked at. All three host packs now take it and its pid copies.
    [Fact]
    public void The_host_packs_take_Coop_server_log_from_the_engine_folder_cleaned_and_newest_first()
    {
        var server = Path.Combine(_directory, "DedicatedServer");
        var bin = DedicatedEngineLaunch.ServerBin(server);
        Directory.CreateDirectory(bin);
        var now = DateTime.UtcNow;
        void Log(string name, string text, int minutesAgo)
        {
            var path = Path.Combine(bin, name);
            File.WriteAllText(path, text);
            File.SetLastWriteTimeUtc(path, now.AddMinutes(-minutesAgo));
        }

        Log("Coop_server.log", "Peer 10.2.3.4:4200 joined as 76561198000000001 from C:\\Users\\Patricia7\\x with HostPass99, handle 77", 1);
        Log("Coop_server_4100.log", new string('x', 600_000) + " last line of the second server", 5);
        Log("Coop_server_4200.log", "older copy", 10);
        Log("Coop_server_4300.log", "oldest kept", 20);
        Log("Coop_server_4400.log", "too old", 30);
        Log("Coop_server.log.lock", "lock", 0);
        var limits = new LogZipLimits(TextOnly: true) { Secrets = ["HostPass99"] };

        var zip = Path.Combine(_directory, "coop-log.zip");
        HostLogZip.Create(zip, Path.Combine(_directory, "nologs"), Path.Combine(_directory, "data"), [server], null, null, null,
            limits: limits, modLogsRoot: Path.Combine(_directory, "nomodlogs"));
        AssertCoopLogs(zip, "server-folder/coop-logs/");

        var beZip = Path.Combine(_directory, "be-coop-log.zip");
        BurningEmpires.Core.BeHostLogPack.Create(beZip, server, Path.Combine(_directory, "bedata"), Path.Combine(_directory, "pd"), limits: limits);
        AssertCoopLogs(beZip, "server-folder/coop-logs/");

        var taomZip = Path.Combine(_directory, "taom-coop-log.zip");
        HostLogPack.Create(taomZip, server, Path.Combine(_directory, "taomdata"), Path.Combine(_directory, "pd"), limits: limits, modLogsRoot: Path.Combine(_directory, "nomodlogs"));
        AssertCoopLogs(taomZip, "server-folder/coop-logs/");

        static void AssertCoopLogs(string zipPath, string folder)
        {
            using var archive = ZipFile.OpenRead(zipPath);
            var names = archive.Entries.Select(entry => entry.FullName).Where(name => name.StartsWith(folder, StringComparison.Ordinal)).ToList();
            Assert.Equal(new[] { "Coop_server.log", "Coop_server_4100.log", "Coop_server_4200.log", "Coop_server_4300.log" }.Select(name => folder + name), names);
            var text = Read(archive, folder + "Coop_server.log");
            Assert.Contains("handle 77", text);
            // Each one keeps only its end, so a long run cannot push the other logs out.
            var second = Read(archive, folder + "Coop_server_4100.log");
            Assert.Contains("last line of the second server", second);
            Assert.True(second.Length < 450_000, "Coop_server_4100.log kept " + second.Length + " characters");
            foreach (var secret in new[] { "10.2.3.4", "76561198000000001", "Patricia7", "HostPass99" })
                Assert.DoesNotContain(secret, text);
        }
    }

    // A player's note can repeat the join password of another server on this PC, so both launchers put every
    // found server's password in the report's secrets, which clean player-notes.txt as well as the logs.
    [Fact]
    public void A_note_with_a_found_servers_join_password_is_cleaned()
    {
        var taomData = Path.Combine(_directory, "TaomData");
        Directory.CreateDirectory(taomData);
        File.WriteAllText(Path.Combine(taomData, "server-config.json"), "{\n  \"password\": \"xK9pQ2zzLm\"\n}\n");
        var beData = Path.Combine(_directory, "BeData");
        Directory.CreateDirectory(beData);
        File.WriteAllText(Path.Combine(beData, "server-config.json"), "{\n  \"password\": \"Qw7RtYb3Zn\"\n}\n");

        var taom = ClientLogPack.ServerPasswords([new TaomServerFolder(null, taomData)]);
        var be = BurningEmpires.Core.BeClientLogPack.ServerPasswords([new BurningEmpires.Core.BeServerFolder(null, beData)]);
        Assert.Equal(["xK9pQ2zzLm"], taom);
        Assert.Equal(["Qw7RtYb3Zn"], be);
        Assert.DoesNotContain("xK9pQ2zzLm", ReportExtras.CleanNotes("My other server uses xK9pQ2zzLm, can you join it?", taom));
        Assert.DoesNotContain("Qw7RtYb3Zn", ReportExtras.CleanNotes("My other server uses Qw7RtYb3Zn, can you join it?", be));

        Assert.Contains("Concat(ClientLogPack.ServerPasswords(servers))", SourceFiles.Window(SourceFiles.Root(), "TaomCoopLauncher"));
        Assert.Contains("Concat(BeClientLogPack.ServerPasswords(servers))", SourceFiles.Window(SourceFiles.Root(), "BurningEmpiresLauncher"));
    }

    // The server logs can repeat the join password without a password key, so the TAOM client pack
    // takes out the password in each TAOM server's server-config.json, like the Burning Empires one.
    [Fact]
    public void The_TAOM_client_pack_takes_out_the_join_password_of_every_TAOM_server_on_this_PC()
    {
        var server = Path.Combine(_directory, "TaomServer");
        var data = Path.Combine(_directory, "TaomData");
        Directory.CreateDirectory(DedicatedEngineLaunch.ServerBin(server));
        File.WriteAllText(Path.Combine(DedicatedEngineLaunch.ServerBin(server), "Coop_server.log"), "join code xK9pQ2zzLm accepted from the engine folder");
        Directory.CreateDirectory(Path.Combine(data, "logs"));
        File.WriteAllText(Path.Combine(data, "server-config.json"), "{\n  \"saveName\": \"Taom\",\n  \"password\": \"xK9pQ2zzLm\"\n}\n");
        File.WriteAllText(Path.Combine(data, "logs", "coop-server-1.log"), "join code xK9pQ2zzLm accepted in the data logs");
        var servers = new[] { new TaomServerFolder(server, data), new TaomServerFolder(null, Path.Combine(_directory, "no-config")) };

        Assert.Equal(["xK9pQ2zzLm"], ClientLogPack.WithServerPasswords(null, servers).Secrets);
        var zipPath = Path.Combine(_directory, "taom-password.zip");
        ClientLogPack.Create(zipPath, null, Path.Combine(_directory, "docs"), Path.Combine(_directory, "pd"), servers: servers,
            limits: new LogZipLimits(TextOnly: true));
        using var archive = ZipFile.OpenRead(zipPath);
        var engine = Read(archive, archive.Entries.Single(entry => entry.FullName.EndsWith("coop-logs/Coop_server.log", StringComparison.Ordinal)).FullName);
        var dataLog = Read(archive, archive.Entries.Single(entry => entry.FullName.EndsWith("logs/coop-server-1.log", StringComparison.Ordinal)).FullName);
        Assert.Contains("accepted from the engine folder", engine);
        Assert.Contains("accepted in the data logs", dataLog);
        Assert.DoesNotContain("xK9pQ2zzLm", engine);
        Assert.DoesNotContain("xK9pQ2zzLm", dataLog);
    }

    // A player who hosts and plays on one PC sends the client pack, so it takes the engine folder's
    // Coop_server logs of every server found on this PC too, each one once.
    [Fact]
    public void The_client_packs_take_Coop_server_log_from_the_engine_folder_of_a_server_on_this_PC_once()
    {
        var server = Path.Combine(_directory, "Server");
        var bin = DedicatedEngineLaunch.ServerBin(server);
        Directory.CreateDirectory(bin);
        File.WriteAllText(Path.Combine(bin, "Coop_server.log"), "engine log, handle 77");
        File.WriteAllText(Path.Combine(bin, "Coop_server_4100.log"), new string('x', 600_000) + " last line of the second server");
        File.WriteAllText(Path.Combine(bin, "Coop_server.log.lock"), "lock");
        var beData = Path.Combine(_directory, "bedata");
        Directory.CreateDirectory(BurningEmpires.Core.BeHostFolders.Logs(beData));
        File.WriteAllText(Path.Combine(BurningEmpires.Core.BeHostFolders.Logs(beData), "Coop_server.log"), "data log");
        var limits = new LogZipLimits(TextOnly: true);

        var beZip = Path.Combine(_directory, "be-client.zip");
        BurningEmpires.Core.BeClientLogPack.Create(beZip, null, Path.Combine(_directory, "docs"), Path.Combine(_directory, "pd"),
            servers: [new BurningEmpires.Core.BeServerFolder(server, beData)], limits: limits);
        using (var archive = ZipFile.OpenRead(beZip))
        {
            Assert.Equal(["server/coop-logs/Coop_server.log", "server/coop-logs/Coop_server_4100.log"], CoopLogs(archive));
            Assert.Contains("handle 77", Read(archive, "server/coop-logs/Coop_server.log"));
            Assert.Contains("data log", Read(archive, "server/logs/Coop_server.log"));
            var second = Read(archive, "server/coop-logs/Coop_server_4100.log");
            Assert.Contains("last line of the second server", second);
            Assert.True(second.Length < 450_000, "Coop_server_4100.log kept " + second.Length + " characters");
        }

        // Two TAOM data folders that share one server folder give its engine logs once, named by that server folder.
        var taomZip = Path.Combine(_directory, "taom-client.zip");
        ClientLogPack.Create(taomZip, null, Path.Combine(_directory, "docs"), Path.Combine(_directory, "pd"),
            servers: [new TaomServerFolder(server, Path.Combine(_directory, "Data-a")), new TaomServerFolder(server, Path.Combine(_directory, "Data-b"))], limits: limits);
        using (var archive = ZipFile.OpenRead(taomZip))
        {
            Assert.Equal(["server/coop-logs/Coop_server.log", "server/coop-logs/Coop_server_4100.log"], CoopLogs(archive));
            Assert.Contains("handle 77", Read(archive, "server/coop-logs/Coop_server.log"));
        }

        // A log already in the zip is not listed a second time under another folder. The cleaned text
        // dedupe would drop the copy too, so this looks at the list before anything is written.
        var builder = new LogZipBuilder(limits);
        builder.AddCoopServerLogs(server, "server/A/coop-logs");
        builder.AddCoopServerLogs(server, "server/B/coop-logs");
        Assert.Equal(["server/A/coop-logs/Coop_server.log", "server/A/coop-logs/Coop_server_4100.log"], builder.Entries.OrderBy(name => name, StringComparer.Ordinal).ToList());

        // No server folder known: nothing from an engine folder.
        var noServer = Path.Combine(_directory, "no-server.zip");
        ClientLogPack.Create(noServer, null, Path.Combine(_directory, "docs"), Path.Combine(_directory, "pd"),
            servers: [new TaomServerFolder(null, Path.Combine(_directory, "Data-a"))], limits: limits);
        using (var archive = ZipFile.OpenRead(noServer))
            Assert.Empty(CoopLogs(archive));

        // Two servers whose folders have the same name keep their own engine logs: the second gets -2.
        var one = Path.Combine(_directory, "one");
        var two = Path.Combine(_directory, "two");
        foreach (var (root, text) in new[] { (one, "first server"), (two, "second server") })
        {
            Directory.CreateDirectory(DedicatedEngineLaunch.ServerBin(Path.Combine(root, "Server")));
            File.WriteAllText(Path.Combine(DedicatedEngineLaunch.ServerBin(Path.Combine(root, "Server")), "Coop_server.log"), text);
        }

        var drive = Path.GetPathRoot(_directory)!.TrimEnd(':', '\\', '/');
        var sameNames = Path.Combine(_directory, "same-names.zip");
        ClientLogPack.Create(sameNames, null, Path.Combine(_directory, "docs"), Path.Combine(_directory, "pd"),
            servers: [new TaomServerFolder(Path.Combine(one, "Server"), Path.Combine(one, "Data")), new TaomServerFolder(Path.Combine(two, "Server"), Path.Combine(two, "Data"))], limits: limits);
        using (var archive = ZipFile.OpenRead(sameNames))
        {
            Assert.Equal(["server/" + drive + "-Server-2/coop-logs/Coop_server.log", "server/" + drive + "-Server/coop-logs/Coop_server.log"], CoopLogs(archive));
            Assert.Contains("first server", Read(archive, "server/" + drive + "-Server/coop-logs/Coop_server.log"));
            Assert.Contains("second server", Read(archive, "server/" + drive + "-Server-2/coop-logs/Coop_server.log"));
        }

        var beSameNames = Path.Combine(_directory, "be-same-names.zip");
        BurningEmpires.Core.BeClientLogPack.Create(beSameNames, null, Path.Combine(_directory, "docs"), Path.Combine(_directory, "pd"),
            servers: [new BurningEmpires.Core.BeServerFolder(Path.Combine(one, "Server"), null), new BurningEmpires.Core.BeServerFolder(Path.Combine(two, "Server"), null)], limits: limits);
        using (var archive = ZipFile.OpenRead(beSameNames))
        {
            Assert.Contains("first server", Read(archive, "server/" + drive + "-Server/coop-logs/Coop_server.log"));
            Assert.Contains("second server", Read(archive, "server/" + drive + "-Server-2/coop-logs/Coop_server.log"));
        }

        static List<string> CoopLogs(ZipArchive archive) =>
            archive.Entries.Select(entry => entry.FullName).Where(name => name.Contains("coop-logs/", StringComparison.Ordinal)).OrderBy(name => name, StringComparer.Ordinal).ToList();
    }

    // The update helper's logs sit in the tool's own folder; every client and host pack takes them,
    // cleaned and cut to their end, and a missing one is simply left out.
    [Fact]
    public void Every_log_pack_takes_the_tool_update_logs_from_the_tool_folder()
    {
        var tool = Path.Combine(_directory, "tool");
        Directory.CreateDirectory(tool);
        File.WriteAllText(Path.Combine(tool, "update-install.log"), new string('x', 300_000) + " swap done for HostPass99");
        var limits = new LogZipLimits(TextOnly: true) { Secrets = ["HostPass99"] };
        var none = Path.Combine(_directory, "none");

        var client = Path.Combine(_directory, "client-update.zip");
        ClientLogZip.Create(client, null, none, none, limits: limits, toolFolder: tool);
        AssertUpdateLogs(client, "launcher/", skipped: false);

        File.WriteAllText(Path.Combine(tool, "update-skipped.log"), "skipped v9.9.9");
        var host = Path.Combine(_directory, "host-update.zip");
        HostLogZip.Create(host, none, none, [], null, null, null, limits: limits, modLogsRoot: none, toolFolder: tool);
        AssertUpdateLogs(host, "host/", skipped: true);
        var taom = Path.Combine(_directory, "taom-update.zip");
        HostLogPack.Create(taom, null, none, none, limits: limits, modLogsRoot: none, toolFolder: tool);
        AssertUpdateLogs(taom, "host/", skipped: true);
        var be = Path.Combine(_directory, "be-update.zip");
        BurningEmpires.Core.BeHostLogPack.Create(be, null, none, none, limits: limits, toolFolder: tool);
        AssertUpdateLogs(be, "host/", skipped: true);

        static void AssertUpdateLogs(string zipPath, string folder, bool skipped)
        {
            using var archive = ZipFile.OpenRead(zipPath);
            var text = Read(archive, folder + "update-install.log");
            Assert.Contains("swap done for", text);
            Assert.DoesNotContain("HostPass99", text);
            Assert.True(text.Length < 250_000, "update-install.log kept " + text.Length + " characters");
            Assert.Equal(skipped, archive.GetEntry(folder + "update-skipped.log") != null);
        }
    }

    [Fact]
    public void FitToSize_cuts_long_logs_until_the_zip_fits_and_keeps_the_notes()
    {
        var zip = Path.Combine(_directory, "big.zip");
        var random = new Random(7);
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            foreach (var name in new[] { "a.log", "b.log" })
            {
                using var writer = new StreamWriter(archive.CreateEntry(name).Open());
                var chars = new char[400_000];
                for (var i = 0; i < chars.Length; i++)
                    chars[i] = (char)random.Next(33, 126);
                writer.Write(chars);
                writer.Write("LAST LINE");
            }

            using var notes = new StreamWriter(archive.CreateEntry("NOTES.txt").Open());
            notes.Write("the notes");
        }

        var note = CrashReports.FitToSize(zip, 300_000, [200_000, 100_000]);
        Assert.NotNull(note);
        Assert.True(new FileInfo(zip).Length <= 300_000);
        using var after = ZipFile.OpenRead(zip);
        Assert.Equal("the notes", Read(after, "NOTES.txt"));
        Assert.EndsWith("LAST LINE", Read(after, "a.log"));
        Assert.Null(CrashReports.FitToSize(zip, 10_000_000));
    }

    // ----- The summary and the crash line -----

    [Fact]
    public void The_summary_carries_only_clean_known_fields()
    {
        var request = new CrashReportRequest
        {
            Tool = "something",
            Side = "server",
            Reason = CrashReason.Logs,
            Problem = CrashProblem.Join,
            ToolVersion = "76561198000000001",
            Coop = "v0.1.6",
            Game = "v1.4.8",
            CrashLine = "Exception at 10.0.0.1 " + new string('x', 400),
            SuspectedMod = "C:\\Users\\Pat\\Mods",
            Collect = (_, _) => Array.Empty<string>()
        };
        var summary = CrashReports.BuildSummary(request, Install, [new("crash-reports\\..\\76561198000000001\\a b.log", 12), new("NOTES.txt", -3)]);
        Assert.Equal("coop", summary.Tool);
        Assert.Equal("server", summary.Side);
        Assert.Equal(CrashProblem.Join, summary.Problem);
        Assert.Equal("", summary.ToolVersion);
        Assert.Null(summary.SuspectedMod);
        Assert.True(summary.CrashLine.Length <= CrashReports.MaxCrashLine);
        Assert.DoesNotContain("10.0.0.1", summary.CrashLine);
        Assert.Equal("crash-reports/steamid/a b.log", summary.Files[0].Name);
        Assert.Equal(0, summary.Files[1].Size);

        var json = CrashReports.SummaryJson(summary);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("CoodszCrashReport", doc.RootElement.GetProperty("kind").GetString());
        Assert.Equal(1, doc.RootElement.GetProperty("schema").GetInt32());
        Assert.False(doc.RootElement.TryGetProperty("suspectedMod", out _), "an empty field is left out");
        Assert.Equal(["kind", "schema", "install", "tool", "side", "reason", "problem", "toolVersion", "coop", "game", "crashLine", "files"],
            doc.RootElement.EnumerateObject().Select(property => property.Name));

        Assert.Null(CrashReports.WithProblem(summary, "boss").Problem);
        Assert.Equal(CrashProblem.Lag, CrashReports.WithProblem(summary, CrashProblem.Lag).Problem);
        var crash = CrashReports.BuildSummary(new CrashReportRequest { Reason = CrashReason.Crash, Problem = CrashProblem.Lag, Collect = (_, _) => [] }, Install, []);
        Assert.Null(crash.Problem);
        Assert.Null(CrashReports.WithProblem(crash, CrashProblem.Lag).Problem);
    }

    [Fact]
    public void The_crash_line_is_the_strongest_last_line_and_comes_from_the_first_text_that_has_one()
    {
        Assert.Equal("Unhandled exception in TaleWorlds.Core",
            CrashReports.FindCrashLine("ok\nSystem.Exception: early\nUnhandled exception in TaleWorlds.Core\nmore"));
        Assert.Equal("System.NullReferenceException: Object reference not set",
            CrashReports.FindCrashLine(null, "line\nSystem.NullReferenceException: Object reference not set\nend"));
        Assert.Equal("The game crashed while loading", CrashReports.FindCrashLine("", "The game crashed while loading"));
        Assert.Null(CrashReports.FindCrashLine("all good", null));
        Assert.DoesNotContain("76561198000000001", CrashReports.FindCrashLine("System.Exception: player 76561198000000001")!);
    }

    [Fact]
    public void A_warning_that_something_may_still_crash_is_not_the_crash_line()
    {
        // The start script's headless loading warning, wrapped by the console (live test 2026-10-26).
        var run = "WARNING: Unsupported headless loading enabled for: RBM. HexTool will attempt them; client/UI\nsubmodules may still crash.\n[DedicatedServer] SERVING";
        Assert.Null(CrashReports.FindCrashLine(run));
        Assert.Equal("The server crashed while loading the save",
            CrashReports.FindCrashLine("The server crashed while loading the save\nsubmodules may still crash."));
    }

    // ----- Show and hide rules -----

    [Fact]
    public void The_one_time_card_shows_once_per_tool_and_the_crash_card_comes_before_the_mod_card()
    {
        var path = Path.Combine(_directory, "mod-reports.json");
        var settings = new ModReportSettings();
        Assert.Equal(IntroCard.CrashReports, CrashReports.IntroFor(settings, "coop", TestEndpoint, "https://reports.example/report"));
        Assert.True(CrashReports.ShouldShowIntro(settings, "coop", "server", TestEndpoint));
        Assert.False(CrashReports.ShouldShowIntro(settings, "coop", "client", ""), "no endpoint, no card");

        CrashReports.MarkIntroSeen(settings, "coop", "client");
        CrashReports.MarkIntroSeen(settings, "coop", "client");
        Assert.Single(settings.CrashIntroSeen);
        Assert.True(ModReports.SaveSettings(settings, path));
        var loaded = ModReports.LoadSettings(path);
        Assert.False(CrashReports.ShouldShowIntro(loaded, "coop", "client", TestEndpoint), "gone for good once closed or used");
        Assert.True(CrashReports.ShouldShowIntro(loaded, "coop", "server", TestEndpoint), "Coodsz Host keeps its own card");
        Assert.True(CrashReports.ShouldShowIntro(loaded, "taom", "client", TestEndpoint), "the TAOM client keeps its own card");
        Assert.Equal(IntroCard.ModReports, CrashReports.IntroFor(loaded, "coop", TestEndpoint, "https://reports.example/report"));
        loaded.IntroSeen = true;
        Assert.Equal(IntroCard.None, CrashReports.IntroFor(loaded, "coop", TestEndpoint, "https://reports.example/report"));
        Assert.Equal(IntroCard.ModReports, CrashReports.IntroFor(new ModReportSettings(), "coop", null, "https://reports.example/report"));

        File.WriteAllText(path, "{\"kind\":\"CoodszModReportSettings\",\"introSeen\":true}");
        Assert.Empty(ModReports.LoadSettings(path).CrashIntroSeen);
    }

    [Fact]
    public void The_crash_bar_puts_send_crash_report_first_and_shows_without_mods()
    {
        const string reports = "https://reports.example/report";
        Assert.Equal([CrashBarButton.SendCrashReport, CrashBarButton.TellWhichMod, CrashBarButton.SaveCopy], CrashReports.CrashBarButtons(3, TestEndpoint, reports));
        Assert.Equal([CrashBarButton.SendCrashReport, CrashBarButton.SaveCopy], CrashReports.CrashBarButtons(0, TestEndpoint, reports));
        Assert.Equal([CrashBarButton.TellWhichMod, CrashBarButton.SaveCopy], CrashReports.CrashBarButtons(3, null, reports));
        Assert.True(CrashReports.ShowsClientCrashBar(0, TestEndpoint, reports), "a crash with no extra mod still offers the crash report");
        Assert.True(CrashReports.ShowsClientCrashBar(3, null, reports));
        Assert.False(CrashReports.ShowsClientCrashBar(0, null, reports));
        Assert.False(CrashReports.ShowsClientCrashBar(3, "", ""));
    }

    [Theory]
    [InlineData(0, false, false)]
    [InlineData(3, true, false)]
    [InlineData(3, false, true)]
    [InlineData(-1073741819, false, true)]
    [InlineData(2, false, true)]
    public void The_server_bar_shows_after_a_crash_but_not_after_stop_or_a_clean_exit(int code, bool asked, bool shows)
    {
        Assert.Equal(shows, CrashReports.ShowsServerCrashBar(ServerExitInfo.From(code, asked), TestEndpoint));
        Assert.False(CrashReports.ShowsServerCrashBar(ServerExitInfo.From(code, asked), ""));
    }

    [Fact]
    public void The_server_bar_title_says_crashed_only_for_a_crash()
    {
        Assert.Equal("The server crashed", CrashReports.ServerCrashTitle(ServerExitKind.Unknown));
        Assert.Equal("The server crashed", CrashReports.ServerCrashTitle(ServerExitKind.FatalWhileServing));
        Assert.Equal("The server stopped with an error", CrashReports.ServerCrashTitle(ServerExitKind.LoadFailure));
    }

    // ----- Sending -----

    [Fact]
    public void The_crash_endpoint_is_the_report_worker_ending_in_crash()
    {
        Assert.Equal(CrashReports.Endpoint, ModReports.Endpoint[..^"/report".Length] + "/crash");
        Assert.True(CrashReports.HasEndpoint());
        Assert.True(CrashReports.MaxZipBytes < CrashReports.WorkerMaxZipBytes);
    }

    [Fact]
    public async Task Send_posts_the_summary_and_the_zip_and_returns_the_code()
    {
        var pack = Pack();
        var recorder = new Recorder(HttpStatusCode.OK, "{\"ok\":true,\"code\":\"CR-7KQ2M\",\"filed\":true}");
        using var http = new HttpClient(recorder);
        var result = await CrashReports.SendAsync(http, pack, CrashReports.WithProblem(pack.Summary, CrashProblem.Desync), TestEndpoint);

        Assert.Equal(CrashReportSendStatus.Sent, result.Status);
        Assert.Equal("CR-7KQ2M", result.Code);
        Assert.Contains(CrashReports.PostOnlyCode, result.Message);
        Assert.Equal(TestEndpoint, recorder.Url);
        Assert.Matches("name=\"?summary\"?", recorder.Body);
        Assert.Matches("name=\"?zip\"?", recorder.Body);
        Assert.Contains("\"problem\": \"desync\"", recorder.Body);
        Assert.Contains("application/zip", recorder.Body);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, CrashReportSendStatus.RateLimited)]
    [InlineData(HttpStatusCode.RequestEntityTooLarge, CrashReportSendStatus.TooLarge)]
    [InlineData(HttpStatusCode.BadRequest, CrashReportSendStatus.Refused)]
    [InlineData(null, CrashReportSendStatus.Failed)]
    public async Task A_failed_send_says_so_and_points_to_save_a_copy(HttpStatusCode? status, CrashReportSendStatus expected)
    {
        using var http = new HttpClient(new Recorder(status, "{}"));
        var result = await CrashReports.SendAsync(http, Pack(), endpoint: TestEndpoint);
        Assert.Equal(expected, result.Status);
        Assert.Null(result.Code);
        Assert.Contains("Save a copy instead", result.Message);
    }

    [Fact]
    public async Task No_code_in_the_answer_or_no_endpoint_is_a_failure()
    {
        using var http = new HttpClient(new Recorder(HttpStatusCode.OK, "{\"ok\":true,\"code\":\"CR-0OIL1\"}"));
        Assert.Equal(CrashReportSendStatus.Failed, (await CrashReports.SendAsync(http, Pack(), endpoint: TestEndpoint)).Status);
        Assert.Equal(CrashReportSendStatus.Failed, (await CrashReports.SendAsync(http, Pack(), endpoint: "")).Status);
        Assert.True(CrashReports.IsCode("CR-7KQ2M"));
        Assert.False(CrashReports.IsCode("CR-7KQ2"));
        Assert.Null(CrashReports.ReadCode("not json"));
    }

    // ----- The six tools -----

    [Fact]
    public void No_tool_has_a_log_zip_main_button_left()
    {
        foreach (var tool in Tools)
        {
            var path = Path.Combine(RepoRoot(), "src", tool, "MainWindow.xaml");
            var text = File.ReadAllText(path);
            foreach (var old in new[] { "Save all logs to one zip", "Save logs for help" })
                Assert.DoesNotContain("Content=\"" + old + "\"", text);
            var clicks = XDocument.Load(path).Descendants().Select(element => (string?)element.Attribute("Click")).Where(click => click != null).ToList();
            foreach (var handler in new[] { "SaveLogs_Click", "SaveLogZip_Click", "SaveLogsZip_Click", "SaveEngineLogs_Click" })
                Assert.DoesNotContain(handler, clicks);
            var source = SourceFiles.Window(RepoRoot(), tool);
            Assert.DoesNotContain("SaveFileDialog", SourceFiles.Method(source, "private CrashReportRequest CrashRequest("));
        }

        // The guides and help lines point at Send my logs now.
        var guides = Directory.GetFiles(Path.Combine(RepoRoot(), "src", "BannerlordCoopLauncher.Core", "Keep", "Guides"), "*.json", SearchOption.AllDirectories)
            .Select(File.ReadAllText);
        foreach (var text in guides.Append(File.ReadAllText(Path.Combine(RepoRoot(), "src", "BannerlordCoopLauncher.Core", "Joining", "JoinGuide.cs"))))
        {
            Assert.DoesNotContain("Save all logs to one zip", text);
            Assert.DoesNotContain("Save logs for help", text);
        }
    }

    [Fact]
    public void Every_tool_shows_send_my_logs_its_link_and_the_crash_report_bar()
    {
        foreach (var tool in Tools)
        {
            var xaml = File.ReadAllText(Path.Combine(RepoRoot(), "src", tool, "MainWindow.xaml"));
            foreach (var slot in new[] { "SendLogsSlot", "SendLogsLinkSlot" })
                Assert.Contains("x:Name=\"" + slot + "\"", xaml);
            foreach (var brush in new[] { "ReportRed", "ReportRedBack", "ReportGold", "ReportGoldBack", "ReportGreen", "ReportGreenBack", "ReportInk" })
                Assert.Contains("x:Key=\"" + brush + "\"", xaml);
            var source = SourceFiles.Window(RepoRoot(), tool);
            var setUp = SourceFiles.Method(source, "private void SetUpCrashReports(");
            Assert.Contains("CrashReports.HasEndpoint()", setUp);
            Assert.Contains("SendLogsSlot.Content = Coodsz.Ui.CrashReportUi.SendLogsButton(this,", setUp);
            Assert.Contains("SendLogsLinkSlot.Content = Coodsz.Ui.CrashReportUi.SomethingWrongLink(this,", setUp);
            Assert.Contains("void SendLogs_Click(object", source);
            Assert.Contains("limits: limits", SourceFiles.Method(source, "private CrashReportRequest CrashRequest("));
            Assert.Contains("Secrets =", SourceFiles.Method(source, "private CrashReportRequest CrashRequest("));
            Assert.Contains("Modules =", SourceFiles.Method(source, "private CrashReportRequest CrashRequest("));
        }

        foreach (var host in new[] { "CoodszHost", "TaomCoopHost", "BurningEmpiresHost" })
        {
            Assert.Contains("x:Name=\"CrashBarHost\"", File.ReadAllText(Path.Combine(RepoRoot(), "src", host, "MainWindow.xaml")));
            var source = SourceFiles.Window(RepoRoot(), host);
            Assert.Contains("new Coodsz.Ui.ServerCrashBar(this, CrashBarHost,", source);
            Assert.Contains("_crashBar.ShowIntroOnce()", source);
            Assert.Contains("CrashReports.ShowsServerCrashBar(exit)", SourceFiles.Method(source, "private void OfferServerCrashReport("));
            Assert.Contains("includeDumps: false", SourceFiles.Method(source, "private CrashReportRequest CrashRequest("));
        }

        Assert.Contains("OfferServerCrashReport(info, crashLine);", SourceFiles.Method(SourceFiles.Window(RepoRoot(), "CoodszHost"), "private void OnServerExited("));
        var taomHost = SourceFiles.Window(RepoRoot(), "TaomCoopHost");
        Assert.Contains("OfferServerCrashReport(process);", taomHost);
        Assert.Contains("_stopAsked = true;", SourceFiles.Method(taomHost, "private void StopServer("));
        Assert.Contains("_stopAsked = true;", SourceFiles.Method(taomHost, "private void AskServerToStop("));

        foreach (var client in new[] { "BannerlordCoopLauncher", "TaomCoopLauncher", "AD1259Launcher", "BurningEmpiresLauncher" })
        {
            var source = SourceFiles.Window(RepoRoot(), client);
            Assert.Contains("CrashRequest = CrashRequest", SourceFiles.Method(source, "private void SetUpModReports("));
            Assert.Contains("CrashReports.FindCrashLine(capture.ReportText, capture.GameLog)", source);
        }

        foreach (var project in new[] { "CoodszCoopAndFriends", "TaomCoopLauncher", "AD1259Launcher", "BurningEmpiresLauncher", "CoodszHost", "AD1259Host", "TaomCoopHost", "BurningEmpiresHost" })
        {
            var csproj = File.ReadAllText(Directory.GetFiles(Path.Combine(RepoRoot(), "src", project), "*.csproj").Single());
            Assert.Contains("CrashReportWindow.cs", csproj);
            Assert.Contains("ModReportWindow.cs", csproj);
        }
    }

    [Fact]
    public void Nothing_is_sent_without_the_send_click_and_the_code_shows_big_with_copy()
    {
        var window = File.ReadAllText(Path.Combine(RepoRoot(), "src", "BannerlordCoopLauncher", "CrashReportWindow.cs"));
        var reports = File.ReadAllText(Path.Combine(RepoRoot(), "src", "BannerlordCoopLauncher", "ModReportWindow.cs"));
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(window, @"CrashReports\.SendAsync\("));
        Assert.Contains("CrashReports.SendAsync(", SourceFiles.Method(window, "private async Task SendAsync("));
        Assert.Contains("_send.Click += async (_, _) => await SendAsync();", window);
        Assert.DoesNotContain("SendAsync(", SourceFiles.Method(window, "public sealed class ServerCrashBar"));
        Assert.DoesNotContain("CrashReports.SendAsync(", reports);
        var code = SourceFiles.Method(window, "private void ShowCode(");
        Assert.Contains("FontSize = 48", code);
        Assert.Contains("CopyText", code);
        Assert.Contains("CrashReports.PostOnlyCode", code);
        Assert.Equal("Post only this code in Discord, not your log files.", CrashReports.PostOnlyCode);
        // The consent window lists every file with its size and offers Save a copy instead.
        var consent = SourceFiles.Method(window, "private void ShowConsent(");
        Assert.Contains("ShowFiles();", consent);
        Assert.Contains("FileList(files)", SourceFiles.Method(window, "private void ShowFiles("));
        Assert.Contains("SummaryGrid(pack.Summary)", consent);
        Assert.Contains("SaveCopyText", consent);
        // The crash bar: Send crash report is the biggest, Tell everyone which mod broke the second, Save a copy the link.
        var bar = SourceFiles.Method(reports, "private FrameworkElement BuildCrash(");
        Assert.Contains("CrashReportUi.CrashButton, CrashReportUi.CrashButtonTip, red, \"📤\", size: 21", bar);
        Assert.Contains("ModReportUi.Outlined(_host, CrashButton,", bar);
        Assert.Contains("CrashReportUi.SaveCopyLink(", bar);
        Assert.DoesNotContain("= \"Save logs for help\"", reports);
        Assert.DoesNotContain("(_host, \"Save logs for help\"", reports);
    }

    // ----- Helpers -----

    private CrashReportPack Pack()
    {
        var zip = Path.Combine(_directory, "pack-" + Guid.NewGuid().ToString("N") + ".zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        using (var writer = new StreamWriter(archive.CreateEntry("NOTES.txt").Open()))
            writer.Write("notes");
        var summary = CrashReports.BuildSummary(new CrashReportRequest { Reason = CrashReason.Logs, Collect = (_, _) => [] }, Install, CrashReports.FileList(zip));
        return new CrashReportPack(zip, summary, []);
    }

    private string Write(string name, string text)
    {
        var path = Path.Combine(_directory, name);
        File.WriteAllText(path, text);
        return path;
    }

    private static string Read(ZipArchive archive, string name)
    {
        var entry = archive.GetEntry(name) ?? throw new InvalidOperationException(name + " is not in the zip");
        using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private sealed class Recorder(HttpStatusCode? status, string answer) : HttpMessageHandler
    {
        public string Url { get; private set; } = "";
        public string Body { get; private set; } = "";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Url = request.RequestUri!.ToString();
            Body = request.Content == null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            if (status == null)
                throw new HttpRequestException("offline");
            return new HttpResponseMessage(status.Value) { Content = new StringContent(answer) };
        }
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "BannerlordCoopLauncher.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
