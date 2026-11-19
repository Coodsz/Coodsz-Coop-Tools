using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using CoodszHost.Services;

namespace BannerlordCoopLauncher.Tests;

public class CoodszFriendsHostTests
{
    [Fact]
    public void Required_framework_mods_are_written_into_selection_so_the_start_script_loads_them()
    {
        var root = Path.Combine(Path.GetTempPath(), "coodsz-selection-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            ModuleCatalog.Write(root,
            [
                new ModuleRow { Id = "Bannerlord.Harmony", Name = "Harmony", Required = true, Enabled = true },
                new ModuleRow { Id = "Bannerlord.ButterLib", Name = "ButterLib", Required = true, Enabled = true },
                new ModuleRow { Id = "Bannerlord.UIExtenderEx", Name = "UIExtenderEx", Required = true, Enabled = true },
                new ModuleRow { Id = "Bannerlord.MBOptionScreen", Name = "MCM", Required = true, Enabled = true },
                new ModuleRow { Id = "Native", Name = "Native", Required = true, Enabled = true },
                new ModuleRow { Id = "CoopNightly", Name = "Coop", Required = false, Enabled = true }
            ]);

            var selection = File.ReadAllText(Path.Combine(root, "modded-server-selection.json"));
            Assert.Contains("\"Bannerlord.Harmony\": true", selection);
            Assert.Contains("\"Bannerlord.ButterLib\": true", selection);
            Assert.Contains("\"Bannerlord.UIExtenderEx\": true", selection);
            Assert.Contains("\"Bannerlord.MBOptionScreen\": true", selection);
            Assert.Contains("\"CoopNightly\": true", selection);
            Assert.DoesNotContain("\"Native\"", selection);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Save_game_uses_the_servers_own_save_command()
    {
        // Coop 0.1.6 turns the game's autosave off on the server, so force_autosave writes nothing.
        Assert.Equal("launcher.server.save", ServerSession.SaveCommand);
        Assert.Equal("coop.debug.save.force_autosave", ServerSession.LegacySaveCommand);
        // The prebuilt DLL is in the private folder only (CONTRIBUTING.md, Private files), so a checkout without it skips this part.
        var module = Path.Combine(SourceFiles.PrivateRoot(), "DedicatedServerRepair", "engine", "Modules", "DedicatedServer.Windows", "bin", "Win64_Shipping_Server", "DedicatedServer.LauncherCheats.dll");
        if (File.Exists(module))
        {
            var bytes = File.ReadAllBytes(module);
            var text = System.Text.Encoding.Unicode.GetString(bytes) + System.Text.Encoding.Unicode.GetString(bytes, 1, bytes.Length - 1);
            Assert.Contains("launcher.server.save", text);
        }
    }

    [Fact]
    public void A_single_player_save_is_not_treated_as_the_dedicated_server_save()
    {
        var root = Path.Combine(Path.GetTempPath(), "coodsz-saves-" + Guid.NewGuid().ToString("N"));
        var documents = Path.Combine(root, "Game Saves");
        var server = Path.Combine(root, "CoopData", "DedicatedServer", "Game Saves");
        Directory.CreateDirectory(documents);
        Directory.CreateDirectory(server);
        try
        {
            File.WriteAllText(Path.Combine(documents, "save001.sav"), "single-player");
            File.WriteAllText(Path.Combine(documents, "TAOMHOST.sav"), "single-player");
            File.WriteAllText(Path.Combine(server, "Finale.sav"), "server");
            File.WriteAllText(Path.Combine(server, "Finale.backup1.sav"), "backup");

            var listed = SaveCatalog.NamesIn(server);
            Assert.Equal(new[] { "Finale" }, listed);
            Assert.DoesNotContain("save001", listed);
            Assert.DoesNotContain("TAOMHOST", listed);

            Assert.Equal(
                "That save is not in the dedicated server Game Saves folder. Make a new campaign save, or pick one that is already there.",
                LaunchOptions.MissingSaveReason("save001", fileExists: false, newCampaign: false));
            Assert.Equal(
                "That save is not in the dedicated server Game Saves folder. Make a new campaign save, or pick one that is already there.",
                LaunchOptions.MissingSaveReason("TAOMHOST", fileExists: false, newCampaign: false));
            Assert.Null(LaunchOptions.MissingSaveReason("Finale", fileExists: true, newCampaign: false));
            Assert.Null(LaunchOptions.MissingSaveReason("Campaign_20260926_1517", fileExists: false, newCampaign: true));
            Assert.Equal(
                "Make a new campaign save first. The save field is empty.",
                LaunchOptions.MissingSaveReason("   ", fileExists: false, newCampaign: false));

            var create = new LaunchOptions
            {
                SaveName = "Campaign_20260926_1517",
                CreateNew = true,
                Region = "EU",
                EnginePort = 7210
            }.ToArguments();
            Assert.Contains("-NewSaveName \"Campaign_20260926_1517\"", create);
            Assert.DoesNotContain("-SaveName", create);

            var load = new LaunchOptions
            {
                SaveName = "Finale",
                CreateNew = false,
                Region = "EU",
                EnginePort = 7210
            }.ToArguments();
            Assert.Contains("-SaveName \"Finale\"", load);
            Assert.DoesNotContain("-NewSaveName", load);

            File.WriteAllText(Path.Combine(documents, "save003.sav"), "single-player campaign");
            File.WriteAllText(Path.Combine(documents, "save003.json"), "{\"UniqueGameId\":\"save003\"}");
            var imported = SaveCatalog.ImportSinglePlayer(Path.Combine(documents, "save003.sav"), server, "MoreNationsSP");
            Assert.True(imported.Ok);
            Assert.Equal("single-player campaign", File.ReadAllText(Path.Combine(documents, "save003.sav")));
            Assert.Equal("single-player campaign", File.ReadAllText(Path.Combine(server, "MoreNationsSP.sav")));
            Assert.Contains("MoreNationsSP", File.ReadAllText(Path.Combine(server, "MoreNationsSP.json")));
            Assert.Equal("server", File.ReadAllText(Path.Combine(server, "Finale.sav")));

            var refused = SaveCatalog.ImportSinglePlayer(Path.Combine(documents, "save001.sav"), server, "Finale");
            Assert.False(refused.Ok);
            Assert.Equal("server", File.ReadAllText(Path.Combine(server, "Finale.sav")));
            Assert.False(SaveCatalog.ImportSinglePlayer(Path.Combine(documents, "save001.sav"), server, "default_new_game").Ok);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void A_missing_autosave_setting_is_added_and_a_broken_mod_options_block_is_repaired()
    {
        var server = "{\n  \"saveName\": \"saveauto1\"\n}";
        var withAutosave = JsoncEditor.SetNumber(server, "autosaveMinutes", "5");
        Assert.Contains("\"saveName\": \"saveauto1\"", withAutosave);
        Assert.Contains("\"autosaveMinutes\": 5", withAutosave);

        const string valid = "{\n  \"network\": {\n    \"movementIncomingMiBPerSecond\": 1,\n  },\n  \"modOptions\": {\n    \"battleSize\": 700,\n  }\n}";
        var broken = "{\n  \"modOptions\": {\n  ,\n    \"battleSize\": 700,\n,\n    \"keep\": true,\n  },\n  \"playerReceivedDamage\": \"Easy\"\n}";
        var fixedDifficulty = JsoncEditor.SetString(broken, "playerReceivedDamage", "VeryEasy");
        Assert.Contains("\"playerReceivedDamage\": \"VeryEasy\"", fixedDifficulty);
        Assert.DoesNotContain("{\n  ,", fixedDifficulty);
        Assert.DoesNotContain(",\n,", fixedDifficulty);
        Assert.Contains("\"keep\": true", fixedDifficulty);
        Assert.Equal(valid, JsoncEditor.Repair(valid));

        var existing = "{\n  \"autoRestartHours\": 12,\n  \"autosaveMinutes\": 30,\n  \"battleSize\": 1000,\n  \"port\": 7210,\n  \"smithingStaminaRecoveryMultiplier\": 1\n}";
        var updated = JsoncEditor.SetNumber(existing, "autoRestartHours", "3");
        updated = JsoncEditor.SetNumber(updated, "autosaveMinutes", "5");
        updated = JsoncEditor.SetNumber(updated, "battleSize", "15");
        updated = JsoncEditor.SetNumber(updated, "port", "7210");
        updated = JsoncEditor.SetNumber(updated, "smithingStaminaRecoveryMultiplier", "0.1");
        Assert.Contains("\"autoRestartHours\": 3", updated);
        Assert.Contains("\"autosaveMinutes\": 5", updated);
        Assert.Contains("\"battleSize\": 15", updated);
        Assert.Contains("\"port\": 7210", updated);
        Assert.Contains("\"smithingStaminaRecoveryMultiplier\": 0.1", updated);
        Assert.DoesNotContain("$", updated);

        // An empty box (a key an older server-config.json never had) must not block the rest of Save settings.
        var noRestartHours = "{\n  \"autosaveMinutes\": 30\n}";
        Assert.Equal(noRestartHours, JsoncEditor.SetNumber(noRestartHours, "autoRestartHours", ""));
        Assert.Equal(updated, JsoncEditor.SetNumber(updated, "autoRestartHours", "  "));
        Assert.Throws<InvalidOperationException>(() => JsoncEditor.SetNumber(updated, "autoRestartHours", "abc"));

        const string corrupted = """
            {
              // Hours between clean scheduled restarts: 1, 3, 12 or 24.
              $13,
              // Minutes between world autosaves; 0 disables autosaving.
              $15,
              "saveName": "saveauto1",
              $15,
              $13
            }
            """;
        var restored = JsoncEditor.Repair(corrupted);
        Assert.Contains("\"autoRestartHours\": 3", restored);
        Assert.Contains("\"autosaveMinutes\": 5", restored);
        Assert.Contains("\"saveName\": \"saveauto1\"", restored);
        Assert.DoesNotContain("$", restored);
    }

    [Fact]
    public void Both_Coop_copies_leave_only_Coop_able_to_load()
    {
        var root = Path.Combine(Path.GetTempPath(), "coodsz-coop-choice-" + Guid.NewGuid().ToString("N"));
        var coop = Path.Combine(root, "Coop");
        var nightly = Path.Combine(root, "CoopNightly");
        Directory.CreateDirectory(coop);
        Directory.CreateDirectory(nightly);
        try
        {
            File.WriteAllText(Path.Combine(coop, "SubModule.xml"), "<Module><Id value=\"Coop\" /></Module>");
            File.WriteAllText(Path.Combine(nightly, "SubModule.xml"), "<Module><Id value=\"CoopNightly\" /></Module>");

            var parked = CoopModuleChoice.KeepOne(root);
            Assert.Equal("CoopNightly", Assert.Single(parked));
            Assert.True(File.Exists(Path.Combine(coop, "SubModule.xml")));
            Assert.False(File.Exists(Path.Combine(nightly, "SubModule.xml")));
            Assert.True(File.Exists(Path.Combine(nightly, CoopModuleChoice.ParkedFile)));

            File.Delete(Path.Combine(coop, "SubModule.xml"));
            var restored = CoopModuleChoice.KeepOne(root);
            Assert.Empty(restored);
            Assert.True(File.Exists(Path.Combine(nightly, "SubModule.xml")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Start_repairs_work_on_a_copy_of_the_live_files_and_leave_the_originals_alone()
    {
        var documents = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Mount and Blade II Bannerlord", "CoopData");
        var serverPath = Path.Combine(documents, "DedicatedServer", "server-config.json");
        var modPath = Path.Combine(documents, "mod-config.json");
        var manifest = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            "Steam", "steamapps", "workshop", "content", "261550", "3770450698",
            "DedicatedServer", "engine", "Modules", "Coop", "SubModule.xml");
        if (!File.Exists(serverPath) || !File.Exists(modPath) || !File.Exists(manifest))
            return;

        var serverBefore = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(serverPath)));
        var modBefore = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(modPath)));
        var manifestBefore = File.GetLastWriteTimeUtc(manifest);

        var server = File.ReadAllText(serverPath);
        var mod = File.ReadAllText(modPath);
        // Steam's Coop moved from id CoopNightly (0.1.5) to Coop (0.1.6). The
        // checks below are about the CoopNightly copy, so use that id either way.
        var xml = Regex.Replace(File.ReadAllText(manifest), "<Id value=\"Coop\"\\s*/>", "<Id value=\"CoopNightly\"/>");
        var repairedServer = JsoncEditor.Repair(server);
        Assert.Equal(repairedServer, JsoncEditor.Repair(repairedServer));
        Assert.DoesNotMatch(@"\$1\d+", repairedServer);
        Assert.Contains("\"autoRestartHours\":", repairedServer);
        Assert.Contains("\"autosaveMinutes\":", repairedServer);
        Assert.Equal(JsoncEditor.GetString(server, "saveName"), JsoncEditor.GetString(repairedServer, "saveName"));
        Assert.Equal(JsoncEditor.GetString(server, "password"), JsoncEditor.GetString(repairedServer, "password"));
        ParseJsonc(repairedServer);
        var resaved = JsoncEditor.SetNumber(repairedServer, "autoRestartHours", "3");
        resaved = JsoncEditor.SetNumber(resaved, "autosaveMinutes", "5");
        Assert.Contains("\"autoRestartHours\": 3", resaved);
        Assert.Contains("\"autosaveMinutes\": 5", resaved);
        Assert.DoesNotMatch(@"\$1\d+", resaved);
        Assert.Equal(JsoncEditor.GetString(server, "password"), JsoncEditor.GetString(resaved, "password"));
        var repairedMod = JsoncEditor.Repair(mod);
        Assert.Equal(repairedMod, JsoncEditor.Repair(repairedMod));
        Assert.DoesNotMatch(@"\$1\d+", repairedMod);
        ParseJsonc(repairedMod);

        var stripped = Regex.Replace(
            server,
            "\\r?\\n\\s*\"autosaveMinutes\"\\s*:\\s*-?\\d+(?:\\.\\d+)?\\s*,?",
            "",
            RegexOptions.None,
            TimeSpan.FromSeconds(1));
        Assert.DoesNotContain("\"autosaveMinutes\"", stripped);
        var withAutosave = JsoncEditor.SetNumber(stripped, "autosaveMinutes", "5");
        Assert.Contains("\"autosaveMinutes\": 5", withAutosave);
        Assert.Equal(JsoncEditor.GetString(server, "saveName"), JsoncEditor.GetString(withAutosave, "saveName"));
        Assert.Equal(JsoncEditor.GetString(server, "password"), JsoncEditor.GetString(withAutosave, "password"));
        Assert.Contains("\"loadTimeoutMinutes\"", withAutosave);
        ParseJsonc(withAutosave);

        var broken = Regex.Replace(
            mod,
            "(\"modOptions\"\\s*:\\s*\\{)",
            "$1\n  ,\n,",
            RegexOptions.None,
            TimeSpan.FromSeconds(1));
        Assert.Contains("\"modOptions\": {\n  ,\n,", broken);
        Assert.ThrowsAny<JsonException>(() => ParseJsonc(broken));
        var repaired = JsoncEditor.SetString(broken, "playerReceivedDamage", "Realistic");
        Assert.DoesNotContain("{\n  ,", repaired);
        Assert.DoesNotContain(",\n,", repaired);
        Assert.Contains("\"playerReceivedDamage\": \"Realistic\"", repaired);
        if (mod.Contains("\"battleSize\"", StringComparison.Ordinal))
            Assert.Contains("\"battleSize\"", repaired);
        Assert.Contains("//", repaired);
        ParseJsonc(repaired);

        var root = Path.Combine(Path.GetTempPath(), "coodsz-functional-" + Guid.NewGuid().ToString("N"));
        var onlyNightly = Path.Combine(root, "only");
        var both = Path.Combine(root, "both");
        Directory.CreateDirectory(Path.Combine(onlyNightly, "Coop"));
        Directory.CreateDirectory(Path.Combine(both, "Coop"));
        Directory.CreateDirectory(Path.Combine(both, "CoopNightly"));
        try
        {
            File.WriteAllText(Path.Combine(onlyNightly, "Coop", "SubModule.xml"), xml);
            Assert.Empty(CoopModuleChoice.KeepOne(onlyNightly));
            Assert.Equal(new[] { "CoopNightly" }, IdsTheStartScriptWouldLoad(onlyNightly));

            File.WriteAllText(Path.Combine(both, "Coop", "SubModule.xml"), xml.Replace("CoopNightly", "Coop", StringComparison.Ordinal));
            File.WriteAllText(Path.Combine(both, "CoopNightly", "SubModule.xml"), xml);
            Assert.Equal("CoopNightly", Assert.Single(CoopModuleChoice.KeepOne(both)));
            Assert.Equal(new[] { "Coop" }, IdsTheStartScriptWouldLoad(both));

            var create = new LaunchOptions
            {
                SaveName = "Campaign_functional",
                CreateNew = true,
                Region = "EU",
                EnginePort = 7210
            }.ToArguments();
            Assert.Contains("-NewSaveName \"Campaign_functional\"", create);
        Assert.Contains("-Region EU", create);
        Assert.Contains("-Region NA", new LaunchOptions { Region = "na", EnginePort = 7210 }.ToArguments());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }

        var host = SourceFiles.Window(RepoRoot(), "CoodszHost");
        var body = SourceFiles.Method(host, "private void StartServer()");
        var park = body.IndexOf("ParkExtraCoop();", StringComparison.Ordinal);
        var repair = body.IndexOf("RepairModConfigFile();", StringComparison.Ordinal);
        var repairServer = body.IndexOf("RepairServerConfigFile();", StringComparison.Ordinal);
        var reload = body.IndexOf("ReloadModules();", StringComparison.Ordinal);
        var writeList = body.IndexOf("EnforceSafeModuleList();", StringComparison.Ordinal);
        var launch = body.IndexOf("_session.Start(", StringComparison.Ordinal);
        Assert.True(park >= 0 && park < repair && repair < repairServer && repairServer < reload && reload < writeList && writeList < launch);

        Assert.Equal(serverBefore, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(serverPath))));
        Assert.Equal(modBefore, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(modPath))));
        Assert.Equal(manifestBefore, File.GetLastWriteTimeUtc(manifest));
    }

    private static string[] IdsTheStartScriptWouldLoad(string modulesRoot)
    {
        var ids = new List<string>();
        foreach (var directory in Directory.EnumerateDirectories(modulesRoot))
        {
            var manifest = Path.Combine(directory, "SubModule.xml");
            if (!File.Exists(manifest))
                continue;
            var match = Regex.Match(File.ReadAllText(manifest), "<Id\\s+value=\"(?<id>[^\"]+)\"", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
            if (match.Success)
                ids.Add(match.Groups["id"].Value);
        }

        return ids.ToArray();
    }

    private static void ParseJsonc(string json)
    {
        var withoutComments = Regex.Replace(json, "(?m)^\\s*//.*$", "", RegexOptions.None, TimeSpan.FromSeconds(1));
        var withoutTrailingCommas = Regex.Replace(withoutComments, ",(\\s*[}\\]])", "$1", RegexOptions.None, TimeSpan.FromSeconds(1));
        using var _ = JsonDocument.Parse(withoutTrailingCommas);
    }

    [Fact]
    public void Commands_go_to_the_server_folder_that_is_running()
    {
        var picked = Path.Combine("C:", "Steam", "Modules", "Bannerlord Coop", "DedicatedServer");
        var workshop = Path.Combine("C:", "Steam", "workshop", "3770450698", "DedicatedServer");
        Assert.Equal(new[] { Path.Combine(picked, "server-data") }, ServerSession.ChooseDataRoots(picked, Array.Empty<string>()));
        Assert.Equal(new[] { Path.Combine(picked, "server-data") }, ServerSession.ChooseDataRoots(picked, new[] { picked.ToUpperInvariant() }));
        Assert.Equal(new[] { Path.Combine(workshop, "server-data") }, ServerSession.ChooseDataRoots(picked, new[] { workshop }));
    }

    [Fact]
    public void Server_folder_is_read_from_the_engine_path()
    {
        Assert.Equal(@"C:\Steam\3770450698\DedicatedServer",
            ServerSession.ServerRootFromEngine(@"C:\Steam\3770450698\DedicatedServer\engine\dotnet\dotnet.exe"));
        Assert.Null(ServerSession.ServerRootFromEngine(@"C:\Program Files\dotnet\dotnet.exe"));
        Assert.Null(ServerSession.ServerRootFromEngine(null));
    }

    [Fact]
    public void Cheats_reach_a_server_this_window_did_not_start_and_list_players_fresh()
    {
        var session = File.ReadAllText(Path.Combine(RepoRoot(), "src", "CoodszHost", "Services", "ServerSession.cs"));
        // Cheats and game commands still go through the queue; stdin gets only the builtins (save, status, players, say, kick, help).
        Assert.Contains("TryConsoleLine(\"save\", saveRun)", session);
        Assert.DoesNotContain("this window did not start it", session);
        Assert.Contains("ConsoleQueue.Send(ServerDataRoots(), text)", session);

        var host = SourceFiles.Window(RepoRoot(), "CoodszHost");
        Assert.Contains("DsEventParser.LastPlayers(tail)", host);
        Assert.Contains("_session.ServerDataRoots()", host);
        Assert.DoesNotContain("OwnsCommandPipe", host);
    }

    [Fact]
    public void The_host_log_zip_packs_the_logs_without_the_password_and_checks_the_coop_files()
    {
        var root = Path.Combine(Path.GetTempPath(), "coodsz-host-zip-" + Guid.NewGuid().ToString("N"));
        try
        {
            var data = Path.Combine(root, "CoopData", "DedicatedServer");
            var logs = Path.Combine(data, "logs");
            var server = Path.Combine(root, "DedicatedServer");
            var coopBin = Path.Combine(server, "engine", "Modules", "Coop", "bin", "Win64_Shipping_Server");
            Directory.CreateDirectory(logs);
            Directory.CreateDirectory(coopBin);
            File.WriteAllText(Path.Combine(logs, "coop-server-20261002-170000.log"), "[DedicatedServer] COOP MODULE VERIFICATION FAILED from 192.168.1.20");
            File.WriteAllText(Path.Combine(logs, "host-console-20261002-170000.log"), "engine exited with code 4");
            File.WriteAllText(Path.Combine(logs, "host-action.log"), "Start failed");
            File.WriteAllText(Path.Combine(data, "server-config.json"), "{ \"password\": \"hunter2\", \"saveName\": \"World\" }");
            File.WriteAllText(Path.Combine(coopBin, "Coop.Core.dll"), "client-build");
            var expected = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("server-build"))).ToLowerInvariant();
            File.WriteAllText(Path.Combine(server, "release-info.txt"), "Pinned Coop assemblies (SHA-256):\r\n  Coop.Core.dll      " + expected + "\r\n");
            Directory.CreateDirectory(Path.Combine(data, "Game Saves"));
            File.WriteAllText(Path.Combine(data, "Game Saves", "World.sav"), "save");
            var zipPath = Path.Combine(root, "out", "host-logs.zip");
            var crashes = Path.Combine(root, "ProgramData", "crashes");
            var windowsDumps = Path.Combine(root, "CrashDumps");
            Directory.CreateDirectory(Path.Combine(crashes, "2026-10-02_17.00.00"));
            Directory.CreateDirectory(windowsDumps);
            File.WriteAllBytes(Path.Combine(crashes, "2026-10-02_17.00.00", "dump.dmp"), new byte[] { 1, 2, 3, 4 });
            File.WriteAllText(Path.Combine(crashes, "2026-10-02_17.00.00", "crash_tags.txt"), "engine crash Build Name Win64_Shipping_Server");
            Directory.CreateDirectory(Path.Combine(crashes, "2026-10-01_12.00.00"));
            File.WriteAllText(Path.Combine(crashes, "2026-10-01_12.00.00", "crash_tags.txt"), "older crash Build Name Win64_Shipping_Server");
            Directory.CreateDirectory(Path.Combine(crashes, "2026-10-01_13.00.00"));
            File.WriteAllText(Path.Combine(crashes, "2026-10-01_13.00.00", "crash_tags.txt"), "game crash Build Name Win64_Shipping_Client");
            File.WriteAllBytes(Path.Combine(crashes, "2026-10-01_13.00.00", "dump.dmp"), new byte[] { 5 });
            File.WriteAllBytes(Path.Combine(windowsDumps, "BannerlordCoopServer.exe.1234.dmp"), new byte[] { 9, 9 });
            File.WriteAllBytes(Path.Combine(windowsDumps, "dotnet.exe.1.dmp"), new byte[] { 7 });
            File.SetLastWriteTimeUtc(Path.Combine(windowsDumps, "BannerlordCoopServer.exe.1234.dmp"), DateTime.UtcNow.AddMinutes(-5));
            var modLogs = Path.Combine(root, "Documents", "Mount and Blade II Bannerlord", "Configs", "ModLogs");
            Directory.CreateDirectory(modLogs);
            File.WriteAllText(Path.Combine(modLogs, "CoopModPatch-server.log"), "IG garrison sync failed for 76561198000000001 at 192.168.1.30");
            Directory.CreateDirectory(Path.GetDirectoryName(BannerlordCoopLauncher.Core.Support.LogZipBuilder.IgSettingsPath(Path.Combine(server, "server-data")))!);
            File.WriteAllText(BannerlordCoopLauncher.Core.Support.LogZipBuilder.IgSettingsPath(Path.Combine(server, "server-data")), "MaxGarrisonWage=1200");

            var withoutDumps = HostLogZip.Create(zipPath + ".plain.zip", logs, data, new[] { server }, null, "window line", "console line", crashes, windowsDumps, modLogsRoot: modLogs, steamLibraries: []);
            Assert.False(withoutDumps.DumpIncluded);
            using (var plain = System.IO.Compression.ZipFile.OpenRead(zipPath + ".plain.zip"))
                Assert.DoesNotContain(plain.Entries, entry => entry.FullName.EndsWith(".dmp", StringComparison.OrdinalIgnoreCase));

            var result = HostLogZip.Create(zipPath, logs, data, new[] { server }, null, "window line", "console line", crashes, windowsDumps, includeDumps: true, modLogsRoot: modLogs, steamLibraries: []);

            using var zip = System.IO.Compression.ZipFile.OpenRead(zipPath);
            string Read(string name)
            {
                var entry = zip.GetEntry(name);
                Assert.NotNull(entry);
                using var reader = new StreamReader(entry!.Open());
                return reader.ReadToEnd();
            }

            Assert.Contains("VERIFICATION FAILED", Read("server-logs/coop-server-20261002-170000.log"));
            Assert.DoesNotContain("192.168.1.20", Read("server-logs/coop-server-20261002-170000.log"));
            Assert.Contains("engine exited", Read("host-console/host-console-20261002-170000.log"));
            Assert.Contains("Start failed", Read("host/host-action.log"));
            var config = Read("settings/server-config.json");
            Assert.DoesNotContain("hunter2", config);
            Assert.Contains("\"saveName\": \"World\"", config);
            Assert.Contains("window line", Read("host/server-window.txt"));
            Assert.Contains("console line", Read("host/console-window.txt"));
            Assert.Contains("server-folder/release-info.txt", zip.Entries.Select(entry => entry.FullName));
            var patchLog = Read("coopmodpatch/CoopModPatch-server.log");
            Assert.Contains("IG garrison sync failed", patchLog);
            Assert.DoesNotContain("76561198000000001", patchLog);
            Assert.DoesNotContain("192.168.1.30", patchLog);
            Assert.Contains("MaxGarrisonWage=1200", Read("coopmodpatch/ig-settings.txt"));
            Assert.Null(zip.GetEntry("coopmodpatch/CoopModPatch-client.log"));
            Assert.Contains("Coop.Core.dll: DOES NOT MATCH", Read("NOTES.txt"));
            Assert.DoesNotContain(zip.Entries, entry => entry.FullName.EndsWith(".sav", StringComparison.OrdinalIgnoreCase));
            var dump = zip.GetEntry("crash-dumps/2026-10-02_17.00.00-dump.dmp");
            Assert.NotNull(dump);
            using (var bytes = new MemoryStream())
            {
                dump!.Open().CopyTo(bytes);
                Assert.Equal(new byte[] { 1, 2, 3, 4 }, bytes.ToArray());
            }

            Assert.Contains(zip.Entries, entry => entry.FullName.EndsWith("BannerlordCoopServer.exe.1234.dmp", StringComparison.Ordinal));
            Assert.DoesNotContain(zip.Entries, entry => entry.FullName.Contains("dotnet.exe", StringComparison.Ordinal));
            Assert.DoesNotContain(zip.Entries, entry => entry.FullName.Contains("2026-10-01_13.00.00", StringComparison.Ordinal));
            Assert.Contains("engine crash", Read("crashes/2026-10-02_17.00.00/crash_tags.txt"));
            Assert.Contains("older crash", Read("crashes/2026-10-01_12.00.00/crash_tags.txt"));
            Assert.Contains("only send this zip to someone you trust", Read("NOTES.txt"));
            Assert.True(result.DumpIncluded);
            Assert.True(result.Included >= 7);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Delete_moves_the_mod_folder_out_of_modules_so_it_can_be_put_back()
    {
        var server = Path.Combine(Path.GetTempPath(), "coodsz-delete-" + Guid.NewGuid().ToString("N"), "DedicatedServer");
        try
        {
            var modules = ModuleCatalog.ModulesRoot(server);
            var folder = Path.Combine(modules, "Warlords Battlefield");
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "SubModule.xml"), "<Module><Id value=\"WarlordsBattlefield\"/><Name value=\"Warlords Battlefield\"/></Module>");
            Directory.CreateDirectory(Path.Combine(modules, "Other"));
            File.WriteAllText(Path.Combine(modules, "Other", "SubModule.xml"), "<Module><Id value=\"Other\"/></Module>");

            var moved = ModuleRemoval.MoveOut(server, "WarlordsBattlefield");

            Assert.Single(moved);
            Assert.False(Directory.Exists(folder));
            Assert.True(File.Exists(Path.Combine(moved[0], "SubModule.xml")));
            Assert.StartsWith(ModuleRemoval.RemovedRoot(server), moved[0]);
            Assert.True(Directory.Exists(Path.Combine(modules, "Other")));
            Assert.Throws<InvalidOperationException>(() => ModuleRemoval.MoveOut(server, "WarlordsBattlefield"));
        }
        finally
        {
            var root = Path.GetDirectoryName(server)!;
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Delete_is_refused_for_required_working_and_locked_mods_and_while_serving()
    {
        var working = new[] { "Coop", "MoreNationsRemastered" };
        Assert.Null(ModuleRemoval.Refusal("RBM", false, false, false, working));
        Assert.Contains("Stop the server", ModuleRemoval.Refusal("RBM", false, false, true, working));
        Assert.Contains("locked", ModuleRemoval.Refusal("RBM", false, true, false, working));
        Assert.Contains("required", ModuleRemoval.Refusal("Native", true, false, false, working));
        Assert.Contains("working Coop mods", ModuleRemoval.Refusal("MoreNationsRemastered", false, false, false, working));
        Assert.Contains("working Coop mods", ModuleRemoval.Refusal("CoopNightly", false, false, false, working));
    }

    [Fact]
    public void The_host_keeps_its_own_copy_of_the_server_output_and_notes_failed_starts()
    {
        var session = File.ReadAllText(Path.Combine(RepoRoot(), "src", "CoodszHost", "Services", "ServerSession.cs"));
        Assert.Equal("host-console-*.log", ServerSession.ConsoleLogPattern);
        Assert.Contains("OpenConsoleLog(start.Arguments);", session);
        Assert.Contains("WriteConsole(e.Data); ServerLine?.Invoke(e.Data);", session);
        Assert.Contains("Server process exited with code", session);

        var host = SourceFiles.Window(RepoRoot(), "CoodszHost");
        Assert.Contains("_actions.Note(\"Start failed\", ex.Message);", host);
        Assert.Contains("_actions.Note(\"Exit\"", host);
        Assert.Contains("Newest(ServerSession.LogsPath, ServerSession.ConsoleLogPattern)", host);
    }

    [Fact]
    public void A_reopened_host_sees_the_workshop_dedicated_server_that_is_already_running()
    {
        var workshop = Path.Combine(
            "C:", "Program Files (x86)", "Steam", "steamapps", "workshop", "content", "261550", "3770450698", "DedicatedServer");
        var engine = Path.Combine(workshop, "engine", "dotnet", "dotnet.exe");
        var steamCopy = Path.Combine(
            "C:", "Program Files (x86)", "Steam", "steamapps", "common", "Mount & Blade II Bannerlord",
            "Modules", "Bannerlord Coop", "DedicatedServer");
        Assert.True(ServerSession.IsThisServerEngine(engine, workshop));
        Assert.False(ServerSession.IsThisServerEngine(engine, steamCopy));
        Assert.True(ServerSession.IsThisServerEngine(Path.Combine(steamCopy, "engine", "dotnet", "dotnet.exe"), steamCopy));
        Assert.False(ServerSession.IsThisServerEngine(Path.Combine("C:", "Program Files", "dotnet", "dotnet.exe"), workshop));
        Assert.False(ServerSession.IsThisServerEngine(null, workshop));

        var root = Path.Combine(Path.GetTempPath(), "coodsz-lock-" + Guid.NewGuid().ToString("N"));
        var lockPath = Path.Combine(root, "server-data", "server-instance.lock");
        Directory.CreateDirectory(Path.GetDirectoryName(lockPath)!);
        try
        {
            using var held = new FileStream(lockPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
            Assert.True(ServerSession.InstanceLockHeld(root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }

        Assert.False(ServerSession.InstanceLockHeld(root));
    }

    [Fact]
    public void The_dedicated_server_save_folder_is_not_the_single_player_folder()
    {
        Assert.Contains(Path.Combine("CoopData", "DedicatedServer", "Game Saves"), ServerSession.SavesPath);
        var singlePlayer = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Mount and Blade II Bannerlord",
            "Game Saves");
        Assert.NotEqual(singlePlayer, ServerSession.SavesPath);
    }

    [Fact]
    public void Coop_and_Friends_does_not_copy_a_second_Coop_and_still_clears_a_bad_address()
    {
        var client = SourceFiles.Window(RepoRoot(), "BannerlordCoopLauncher");
        Assert.DoesNotContain("UsePlaytestCoop", client);
        Assert.DoesNotContain("CopyModule", client);
        Assert.DoesNotContain("RaiseLowTerrain", client);
        Assert.Contains("ClearStoredAddress();", client);
        var hostWindow = SourceFiles.Window(RepoRoot(), "CoodszHost");
        var hostXaml = File.ReadAllText(Path.Combine(RepoRoot(), "src", "CoodszHost", "MainWindow.xaml"));
        Assert.DoesNotContain("profile.Region = \"NA\"", hostWindow);
        Assert.Contains("SelectionChanged=\"Region_Changed\"", hostXaml);
        var regionLine = hostXaml.Split('\n').First(line => line.Contains("x:Name=\"RegionBox\""));
        Assert.Contains("ComboBox", regionLine);
        Assert.DoesNotContain("IsReadOnly", regionLine);
        var saveBody = SourceFiles.Method(hostWindow, "private bool SaveSettingsPage(");
        Assert.Contains("if (profile.Locked)", saveBody);
        Assert.Contains("HostSettings.Save(SettingsPath, _settings);", saveBody);
        Assert.Contains("x:Name=\"SettingsSaveDetail\"", hostXaml);
        Assert.Contains("FontSize=\"16\" FontWeight=\"SemiBold\" Foreground=\"#FFFFFFFF\"", hostXaml);

        var host = File.ReadAllText(Path.Combine(RepoRoot(), "src", "CoodszHost", "Services", "ServerSession.cs"));
        Assert.Contains("_logPosition", host);
        Assert.Contains("stream.Position = _logPosition", host);
    }

    [Fact]
    public void Different_copies_of_the_same_dll_are_named_and_the_same_bytes_are_quiet()
    {
        var root = Path.Combine(Path.GetTempPath(), "coodsz-dll-" + Guid.NewGuid().ToString("N"));
        try
        {
            WriteModule(root, "Alpha", "shared.dll", new byte[] { 1, 2, 3 });
            WriteModule(root, "Beta", "shared.dll", new byte[] { 9, 9, 9 });
            WriteModule(root, "Gamma", "shared.dll", new byte[] { 1, 2, 3 });
            var warnings = ModuleDllClash.Warnings(root, new[] { "Alpha", "Beta" });
            Assert.Contains(warnings, line => line.Contains("shared.dll", StringComparison.Ordinal));
            Assert.Contains(warnings, line => line.StartsWith("What to do:", StringComparison.Ordinal) && line.Contains("Move the extra file", StringComparison.Ordinal));
            Assert.Contains(warnings, line => line.StartsWith("What not to do:", StringComparison.Ordinal) && line.Contains("The server still starts", StringComparison.Ordinal));
            Assert.Empty(ModuleDllClash.Warnings(root, new[] { "Alpha", "Gamma" }));
            Assert.Empty(ModuleDllClash.Warnings(root, new[] { "Alpha" }));
            WriteModule(root, "Bannerlord.Harmony", "0Harmony.dll", new byte[] { 4, 5 });
            WriteModule(root, "Beta", "0Harmony.dll", new byte[] { 6, 7 });
            var owned = ModuleDllClash.Warnings(root, new[] { "Bannerlord.Harmony", "Beta" });
            Assert.Contains(owned, line => line.StartsWith("What to do:", StringComparison.Ordinal) && line.Contains("Keep 0Harmony.dll in Bannerlord.Harmony", StringComparison.Ordinal) && line.Contains("Beta", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void A_single_player_mcm_copy_stays_in_the_setup_and_leaves_the_source()
    {
        var root = Path.Combine(Path.GetTempPath(), "coodsz-mcm-" + Guid.NewGuid().ToString("N"));
        var source = Path.Combine(root, "ModSettings", "ImprovedGarrisons");
        var folder = "test-" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(source);
        try
        {
            File.WriteAllText(Path.Combine(source, "settings.json"), "{\"Keep\":true}");
            var count = McmCatalog.ImportFrom(Path.Combine(root, "ModSettings"), folder);
            Assert.Equal(1, count);
            Assert.Equal("{\"Keep\":true}", File.ReadAllText(Path.Combine(source, "settings.json")));
            var copied = Directory.GetFiles(McmCatalog.SnapshotRoot(folder), "settings.json", SearchOption.AllDirectories);
            Assert.Contains("{\"Keep\":true}", File.ReadAllText(copied[0]));
            Assert.True(McmCatalog.SnapshotHasSettings(folder));
        }
        finally
        {
            McmCatalog.DeleteSnapshot(folder);
            Directory.Delete(root, recursive: true);
        }
    }

    private static void WriteModule(string root, string id, string dllName, byte[] bytes)
    {
        var bin = Path.Combine(root, id, "bin", "Win64_Shipping_Server");
        Directory.CreateDirectory(bin);
        File.WriteAllText(Path.Combine(root, id, "SubModule.xml"), "<Module><Id value=\"" + id + "\"/><Name value=\"" + id + "\"/></Module>");
        File.WriteAllBytes(Path.Combine(bin, dllName), bytes);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "src", "CoodszHost", "MainWindow.xaml")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    [Fact]
    public void A_command_the_server_did_not_take_says_where_it_went()
    {
        var folders = new[] { @"C:\S\DedicatedServer\server-data\console-queue" };
        var none = ServerSession.NotPickedUpDetail(folders, Array.Empty<string>(), @"C:\S\DedicatedServer");
        Assert.Contains(folders[0], none);
        Assert.Contains("could not see a running server", none);

        var other = ServerSession.NotPickedUpDetail(folders, new[] { @"D:\Other\DedicatedServer" }, @"C:\S\DedicatedServer");
        Assert.Contains("Set server folder", other);
        Assert.Contains(@"D:\Other\DedicatedServer", other);
    }

    [Fact]
    public void The_host_uses_only_commands_the_dedicated_server_accepts()
    {
        Assert.Equal("launcher.time.set Pause", ServerSession.PauseCommand);
        Assert.Equal("launcher.time.set Play", ServerSession.PlayCommand);
        var players = File.ReadAllText(Path.Combine(RepoRoot(), "src", "CoodszHost", "MainWindow.Players.cs"));
        Assert.DoesNotContain("\"kick ", players);
        Assert.DoesNotContain("campaign.set_time_speed", players);
        var session = File.ReadAllText(Path.Combine(RepoRoot(), "src", "CoodszHost", "Services", "ServerSession.cs"));
        Assert.DoesNotContain("TrySend(\"stop\")", session);
    }

    [Fact]
    public void Every_start_adds_the_rescue_module_as_a_DedicatedServer_module_and_drops_the_old_one()
    {
        var root = Path.Combine(Path.GetTempPath(), "rescue-" + Guid.NewGuid().ToString("N"));
        var server = Path.Combine(root, "DedicatedServer");
        var modules = Path.Combine(server, "engine", "Modules");
        try
        {
            foreach (var id in new[] { "Native", "SandBoxCore", "Sandbox", "Coop", "DedicatedServer.Windows", "SomeMod" })
            {
                Directory.CreateDirectory(Path.Combine(modules, id));
                File.WriteAllText(Path.Combine(modules, id, "SubModule.xml"),
                    "<Module><Name value=\"" + id + "\"/><Id value=\"" + id + "\"/><Version value=\"v1.0.0\"/></Module>");
            }

            // An older Coodsz Host left the module under its old id, and a stray mode file.
            var legacyBin = Path.Combine(modules, "CoopSaveRescue", "bin", "Win64_Shipping_Server");
            Directory.CreateDirectory(legacyBin);
            File.WriteAllText(Path.Combine(legacyBin, "CoopSaveRescue.dll"), "old");
            File.WriteAllText(Path.Combine(modules, "CoopSaveRescue", "SubModule.xml"), "<Module><Id value=\"CoopSaveRescue\"/></Module>");
            var newBin = Path.Combine(modules, "DedicatedServer.CoopSaveRescue", "bin", "Win64_Shipping_Server");
            Directory.CreateDirectory(newBin);
            File.WriteAllText(Path.Combine(newBin, "CoopSaveRescue.mode"), "full");

            // The shipped payload is what gets installed.
            var payload = Path.Combine(RepoRoot(), "src", "CoodszHost", "SaveRescue", "DedicatedServer.CoopSaveRescue");
            Assert.True(File.Exists(Path.Combine(payload, "bin", "Win64_Shipping_Server", "CoopSaveRescue.dll")));
            Assert.Contains("<Id value=\"DedicatedServer.CoopSaveRescue\"/>", File.ReadAllText(Path.Combine(payload, "SubModule.xml")));
            Assert.Equal("DedicatedServer.CoopSaveRescue", SaveRescue.ModuleId);
            SaveRescue.Install(server, payload);
            Assert.True(File.Exists(Path.Combine(modules, "DedicatedServer.CoopSaveRescue", "SubModule.xml")));
            Assert.True(File.Exists(Path.Combine(newBin, "CoopSaveRescue.dll")));
            Assert.False(File.Exists(Path.Combine(newBin, "CoopSaveRescue.mode")));
            Assert.False(Directory.Exists(Path.Combine(modules, "CoopSaveRescue")));

            // The installed module is never offered as a normal mod.
            var rows = ModuleCatalog.Load(server);
            Assert.DoesNotContain(rows, row => row.Id == SaveRescue.ModuleId);
            Assert.Contains(rows, row => row.Id == "SomeMod");

            // A start keeps the save's own mods and adds the module last, switched on, once.
            ModuleCatalog.Write(server, SaveRescue.WithRescue(SaveRescue.WithRescue(rows.Append(new ModuleRow { Id = "CoopSaveRescue", Enabled = true }))));
            var list = File.ReadAllLines(ModuleCatalog.ListPath(server)).Where(line => !line.StartsWith('#')).ToList();
            Assert.Equal(SaveRescue.ModuleId, list.Last());
            Assert.Single(list, id => id.Contains("CoopSaveRescue"));
            Assert.Contains("SomeMod", list);
            Assert.Contains("\"DedicatedServer.CoopSaveRescue\": true", File.ReadAllText(ModuleCatalog.SelectionPath(server)));

            // The next normal start writes the list without it.
            ModuleCatalog.Write(server, ModuleCatalog.Load(server));
            Assert.DoesNotContain(SaveRescue.ModuleId, File.ReadAllText(ModuleCatalog.ListPath(server)));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Rescue_report_lines_are_read_from_the_server_log()
    {
        const string fixedLine = "14:39:26.813  [CoopSaveRescue] Report: fixed 1 problem(s). Save now, then turn this module off.";
        const string nothing = "[CoopSaveRescue] Report: nothing to fix was found. This save does not have the problems this module knows.";
        Assert.True(SaveRescue.IsReport(fixedLine));
        Assert.False(SaveRescue.FoundNothing(fixedLine));
        Assert.Equal("fixed 1 problem(s). Save now, then turn this module off.", SaveRescue.ReportText(fixedLine));
        Assert.True(SaveRescue.FoundNothing(nothing));
        Assert.False(SaveRescue.IsReport("[CoopSaveRescue] FIXED: Closed broken battle."));
        var host = File.ReadAllText(Path.Combine(RepoRoot(), "src", "CoodszHost", "MainWindow.xaml"));
        Assert.Contains("RescueSave_Click", host);
        var release = File.ReadAllText(Path.Combine(RepoRoot(), "tools", "Make-Release.ps1"));
        Assert.Contains("\\SaveRescue", release);
    }

    [Fact]
    public void Rescue_keeps_a_war_declaration_with_a_leaderless_kingdom_from_crashing_the_server()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot(), "compat", "CoopSaveRescue", "SubModule.cs")).Replace("\r\n", "\n");
        // Two finalizers, on in every mode: the exact throw (GetRelationScore) and the call to war in Coop's OnWarDeclared prefix.
        Assert.Contains("typeof(DefaultDiplomacyModel).GetMethod(\"GetRelationScore\", Any, null, new[] { typeof(IFaction), typeof(IFaction), typeof(IFaction) }, null),\n                null, nameof(RelationScoreFinalizer), \"DefaultDiplomacyModel.GetRelationScore\");", source);
        Assert.Contains("Patch(harmony, typeof(AllianceCampaignBehavior).GetMethod(\"OnWarDeclared\", Any), null, nameof(WarDeclaredFinalizer), \"AllianceCampaignBehavior.OnWarDeclared\");", source);
        Assert.Contains("of 14 checks active", source);

        // The relation score: only a NullReferenceException while a leader or the leader's clan is really missing,
        // and then the neutral 0 the game gives a pair that is not on bad terms. Anything else is thrown on.
        var relation = Body(source, "private static Exception RelationScoreFinalizer(");
        Assert.Contains("if (__exception == null)\n                return null;", relation);
        Assert.Contains("if (!(__exception is NullReferenceException))\n                return __exception;", relation);
        Assert.Contains("var missing = NoLeader(factionDeclaresWar) ?? NoLeader(factionDeclaredWar) ?? NoLeader(evaluatingFaction);", relation);
        Assert.Contains("if (missing == null)\n                return __exception;", relation);
        Assert.Contains("__result = 0f;", relation);
        Assert.True(relation.IndexOf("return __exception;", StringComparison.Ordinal) < relation.IndexOf("__result = 0f;", StringComparison.Ordinal));
        // Counted in the Report once per kingdom, with both kingdoms named.
        Assert.Contains("first = NoLeaderSeen.Add(missing);", relation);
        Assert.Contains("\"A call to war\" : \"Weighing a war\";", relation);
        Assert.Contains("Fix(what + \" crashed (\" + FactionLabel(factionDeclaresWar) + \" against \" + FactionLabel(factionDeclaredWar) + \") because \"", relation);
        var noLeader = Body(source, "private static string NoLeader(IFaction faction)");
        Assert.Contains("if (leader == null)", noLeader);
        Assert.Contains("if (leader.Clan == null)", noLeader);
        Assert.Contains("return null;", noLeader);

        // The call to war: only an error from building or adding one is swallowed; the war is already declared.
        var war = Body(source, "private static Exception WarDeclaredFinalizer(IFaction faction1, IFaction faction2, Exception __exception)");
        Assert.Contains("if (!IsCallToWarError(trace))\n                return __exception;", war);
        Assert.Contains("Fix(\"Skipped 1 call to war that crashed (\" + FactionLabel(faction1) + \" against \" + FactionLabel(faction2) + \"): \"", war);
        Assert.Contains("The war itself is declared.", war);
        var filter = Body(source, "public static bool IsCallToWarError(string trace)");
        Assert.Contains("\"ProposeCallToWarAgreementDecision\"", filter);
        Assert.Contains("\"Kingdom.AddDecision\"", filter);
        Assert.Contains("!string.IsNullOrEmpty(trace)", filter);

        // The tools count it like every other fix.
        var run = new BannerlordCoopLauncher.Core.Support.RescueRun();
        run.Begin(BannerlordCoopLauncher.Core.Support.RescueMode.Auto);
        Assert.Equal(BannerlordCoopLauncher.Core.Support.RescueRun.Seen.Fixed, run.Observe("[CoopSaveRescue] FIXED: A call to war crashed (Gondor [empire_s] against Rohan [vlandia]) because Gondor [empire_s] has no leader. Counted that relation as neutral so the war goes on and the server keeps running."));
        Assert.Single(run.Fixes);

        // The shipped DLL was rebuilt from this source.
        var dll = System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(Path.Combine(RepoRoot(), "src", "CoodszHost", "SaveRescue", "DedicatedServer.CoopSaveRescue", "bin", "Win64_Shipping_Server", "CoopSaveRescue.dll")));
        Assert.Contains("RelationScoreFinalizer", dll);
        Assert.Contains("WarDeclaredFinalizer", dll);
        Assert.Contains("IsCallToWarError", dll);

        static string Body(string code, string signature)
        {
            var start = code.IndexOf(signature, StringComparison.Ordinal);
            Assert.True(start >= 0, signature);
            var end = code.IndexOf("\n        }\n", start, StringComparison.Ordinal);
            return code[start..end];
        }
    }

    [Fact]
    public void Rescue_marks_a_killed_lord_left_alive_dead_and_one_bad_hero_does_not_stop_the_load()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot(), "compat", "CoopSaveRescue", "SubModule.cs")).Replace("\r\n", "\n");
        // Only Hero.AfterLoad gets the prefix and the finalizer; skipping a hero that fails to load is not automatic.
        Assert.Contains("Patch(harmony, typeof(Hero).GetMethod(\"AfterLoad\", Any), nameof(HeroAfterLoadPrefix), _mode == RescueMode.Auto ? null : nameof(HeroAfterLoadFinalizer), \"Hero.AfterLoad\");", source);
        var prefix = Body(source, "private static void HeroAfterLoadPrefix(Hero __instance)");
        // Alive heroes with their data are left alone; a hero without data is only marked dead when the game had killed it.
        Assert.Contains("__instance.HeroState == Hero.CharacterStates.Dead", prefix);
        Assert.Contains("HeroPerks.GetValue(__instance) != null && HeroDeveloper.GetValue(__instance) != null", prefix);
        Assert.Contains("__instance.DeathMark == KillCharacterAction.KillCharacterActionDetail.None", prefix);
        Assert.True(prefix.IndexOf("KillCharacterActionDetail.None", StringComparison.Ordinal) < prefix.IndexOf("ChangeState(Hero.CharacterStates.Dead)", StringComparison.Ordinal));
        // Counted in the Report like every other fix, with the hero id and name.
        Assert.Contains("Fix(\"Hero \" + __instance.StringId + \" (\" + name + \")", prefix);
        var finalizer = Body(source, "private static Exception HeroAfterLoadFinalizer(Hero __instance, Exception __exception)");
        Assert.Contains("Fix(\"Hero \" + id + \" failed to load: \"", finalizer);
        Assert.DoesNotContain("return __exception", finalizer);
        Assert.Contains("of 14 checks active", source);

        // The shipped DLL was rebuilt from this source.
        var dll = File.ReadAllBytes(Path.Combine(RepoRoot(), "src", "CoodszHost", "SaveRescue", "DedicatedServer.CoopSaveRescue", "bin", "Win64_Shipping_Server", "CoopSaveRescue.dll"));
        var text = System.Text.Encoding.ASCII.GetString(dll);
        Assert.Contains("HeroAfterLoadPrefix", text);
        Assert.Contains("HeroAfterLoadFinalizer", text);
        Assert.True(dll.AsSpan().IndexOf(System.Text.Encoding.Unicode.GetBytes("COODSZ_SAVE_RESCUE")) >= 0);

        static string Body(string code, string signature)
        {
            var start = code.IndexOf(signature, StringComparison.Ordinal);
            Assert.True(start >= 0, signature);
            var end = code.IndexOf("\n        }\n", start, StringComparison.Ordinal);
            return code[start..end];
        }
    }

    [Fact]
    public void Rescue_skips_taking_a_dead_hero_off_a_troop_list_he_is_not_on_so_a_destroyed_clan_does_not_stop_the_server()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot(), "compat", "CoopSaveRescue", "SubModule.cs")).Replace("\r\n", "\n");
        // Server only: MakeDead marks the hero being made dead, and the roster call gets a prefix and a finalizer.
        Assert.Contains("if (GameNetwork.IsDedicatedServer)\n            {\n                Patch(harmony, typeof(KillCharacterAction).GetMethod(\"MakeDead\", Any, null, new[] { typeof(Hero), typeof(bool) }, null),\n                    nameof(MakeDeadPrefix), nameof(MakeDeadFinalizer), \"KillCharacterAction.MakeDead\");\n                Patch(harmony, typeof(TroopRoster).GetMethod(\"AddToCountsAtIndex\", Any, null, new[] { typeof(int), typeof(int), typeof(int), typeof(int), typeof(bool) }, null),\n                    nameof(RosterIndexPrefix), nameof(RosterIndexFinalizer), \"TroopRoster.AddToCountsAtIndex\");\n", source);
        Assert.Contains("of 14 checks active", source);

        // The hero is only marked while MakeDead runs; the finalizer puts the earlier one back, also when it threw.
        Assert.Contains("[ThreadStatic]\n        private static Hero _dyingHero;", source);
        var makeDeadPrefix = Body(source, "private static void MakeDeadPrefix(Hero victim, out Hero __state)");
        Assert.Contains("__state = _dyingHero;", makeDeadPrefix);
        Assert.Contains("_dyingHero = victim;", makeDeadPrefix);
        var makeDeadFinalizer = Body(source, "private static Exception MakeDeadFinalizer(Exception __exception, Hero __state)");
        Assert.Contains("_dyingHero = __state;", makeDeadFinalizer);
        Assert.Contains("return __exception;", makeDeadFinalizer);
        Assert.DoesNotContain("return null", makeDeadFinalizer);

        // A call may only be skipped inside MakeDead, with nothing added, and an index outside the list before anything changed.
        var prefix = Body(source, "private static void RosterIndexPrefix(TroopRoster __instance, int index, int countChange, out int? __state)");
        Assert.Contains("__state = hero != null && countChange <= 0 && (index < 0 || index >= __instance.Count)", prefix);
        Assert.Contains("&& __instance.FindIndexOfTroop(hero.CharacterObject) < 0 ? index : (int?)null;", prefix);

        // Only an IndexOutOfRangeException in a marked call is swallowed; anything else is thrown on.
        var finalizer = Body(source, "private static Exception RosterIndexFinalizer(TroopRoster __instance, int countChange, ref int __result, Exception __exception, int? __state)");
        Assert.Contains("if (__exception == null || __state == null || !(__exception is IndexOutOfRangeException))\n                return __exception;", finalizer);
        Assert.True(finalizer.IndexOf("return __exception;", StringComparison.Ordinal) < finalizer.IndexOf("__result = -1;", StringComparison.Ordinal));
        Assert.Contains("first = RosterSkipSeen.Add(id);", finalizer);
        Assert.Contains("could not be taken off the troop list of", finalizer);
        Assert.Contains("Skipped that change so the death goes on and the server keeps running.", finalizer);
        Assert.Contains("catch (Exception ex)", finalizer);
        Assert.EndsWith("return null;", finalizer.TrimEnd());

        // The tools count it like every other fix.
        var run = new BannerlordCoopLauncher.Core.Support.RescueRun();
        run.Begin(BannerlordCoopLauncher.Core.Support.RescueMode.Auto);
        Assert.Equal(BannerlordCoopLauncher.Core.Support.RescueRun.Seen.Fixed, run.Observe("[CoopSaveRescue] FIXED: Hero lord_nos_1 (Finarfin, clan Nos Finarfin) could not be taken off the troop list of Finarfin's Party [lord_nos_1_party]: the hero was not on it (index -1, the list has 0, change -1). Skipped that change so the death goes on and the server keeps running."));
        Assert.Single(run.Fixes);

        // Both READMEs say it, the TAOM server tool ships the DLL, and the shipped DLL was rebuilt from this source.
        foreach (var readme in new[]
        {
            Path.Combine(RepoRoot(), "compat", "CoopSaveRescue", "README.txt"),
            Path.Combine(RepoRoot(), "src", "CoodszHost", "SaveRescue", "DedicatedServer.CoopSaveRescue", "README.txt")
        })
            Assert.Contains("A clan is destroyed and one of its dead heroes is not on his party's troop list any more", File.ReadAllText(readme));
        Assert.Contains("SaveRescue/DedicatedServer.CoopSaveRescue/bin/Win64_Shipping_Server/CoopSaveRescue.dll", File.ReadAllText(Path.Combine(RepoRoot(), "src", "TaomCoopHost", "TaomCoopHost.csproj")).Replace((char)92, '/'));
        var dll = File.ReadAllBytes(Path.Combine(RepoRoot(), "src", "CoodszHost", "SaveRescue", "DedicatedServer.CoopSaveRescue", "bin", "Win64_Shipping_Server", "CoopSaveRescue.dll"));
        var text = System.Text.Encoding.ASCII.GetString(dll);
        Assert.Contains("RosterIndexPrefix", text);
        Assert.Contains("RosterIndexFinalizer", text);
        Assert.Contains("MakeDeadPrefix", text);
        Assert.Contains("MakeDeadFinalizer", text);
        Assert.True(dll.AsSpan().IndexOf(System.Text.Encoding.Unicode.GetBytes("could not be taken off the troop list of ")) >= 0);

        static string Body(string code, string signature)
        {
            var start = code.IndexOf(signature, StringComparison.Ordinal);
            Assert.True(start >= 0, signature);
            var end = code.IndexOf("\n        }\n", start, StringComparison.Ordinal);
            return code[start..end];
        }
    }

    [Fact]
    public void Rescue_fills_in_a_missing_hero_name_before_coop_0_1_6_sends_it_so_a_boy_being_born_does_not_stop_the_server()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot(), "compat", "CoopSaveRescue", "SubModule.cs")).Replace("\r\n", "\n");
        // Coop's GameInterface can load after this module, so the check is set up once every module is loaded, and the last try says why it is off.
        Assert.Contains("protected override void OnBeforeInitialModuleScreenSetAsRoot()\n        {\n            base.OnBeforeInitialModuleScreenSetAsRoot();\n            InstallNameGuard(lastChance: false);\n        }", source);
        Assert.Contains("public override void OnGameInitializationFinished(Game game)\n        {\n            base.OnGameInitializationFinished(game);\n            InstallNameGuard(lastChance: true);\n        }", source);
        Assert.Contains("_harmony = harmony;", source);
        Assert.Contains("private const string NameDataType = \"GameInterface.Services.Heroes.Data.HeroChangeNameData\";", source);

        var install = Body(source, "private static void InstallNameGuard(bool lastChance)");
        // Server only and once.
        Assert.Contains("if (_nameGuardDone || _harmony == null || !GameNetwork.IsDedicatedServer)\n                return;", install);
        // Coop's type is looked up by name through Harmony's AccessTools; missing until the last try, then off with a line.
        Assert.Contains("Type.GetType(\"HarmonyLib.AccessTools, 0Harmony\")", install);
        Assert.Contains("GetMethod(\"TypeByName\"", install);
        Assert.Contains("was not found; the no-first-name check is off.", install);
        Assert.True(install.IndexOf("if (lastChance)", StringComparison.Ordinal) < install.IndexOf("was not found", StringComparison.Ordinal));
        // Only Coop 0.1.6's shape (names kept as text, built from two TextObjects) turns it on; Coop's fix (eb9fddeaa) keeps TextObjects.
        Assert.Contains("data.GetProperty(\"FirstName\", Any)?.PropertyType != typeof(string)", install);
        Assert.Contains("data.GetConstructor(Any, null, new[] { typeof(Hero), typeof(TextObject), typeof(TextObject) }, null) == null", install);
        Assert.Contains("already takes a hero name with no first name", install);
        Assert.True(install.IndexOf("already takes a hero name", StringComparison.Ordinal) < install.IndexOf("Patch(_harmony", StringComparison.Ordinal));
        // Hero.SetName gets a prefix that runs before Coop's own (which has no priority), and one line says it is on.
        Assert.Contains("Patch(_harmony, typeof(Hero).GetMethod(\"SetName\", Any, null, new[] { typeof(TextObject), typeof(TextObject) }, null),\n                    nameof(SetNamePrefix), null, \"Hero.SetName\", FirstPriority);", install);
        Assert.Contains("private const int FirstPriority = 800;", source);
        Assert.Contains("No-first-name check on:", install);

        // The priority really reaches Harmony, and a Harmony without it fails the patch instead of running after Coop.
        var patch = Body(source, "private static void Patch(object harmony, MethodBase target, string prefix, string finalizer, string name, int priority = -1)");
        Assert.Contains("harmonyMethodType.GetField(\"priority\")", patch);
        Assert.Contains("throw new MissingFieldException(\"HarmonyMethod.priority\");", patch);
        Assert.Contains("field.SetValue(before, priority);", patch);
        Assert.Contains("patch.Invoke(harmony, new[] { target, before, null, null, Wrap(finalizer) });", patch);

        // Two names leave it alone; one missing name gets the other one (what Hero.AfterLoad does), by ref so Coop and the game both see it.
        var prefix = Body(source, "private static void SetNamePrefix(Hero __instance, ref TextObject fullName, ref TextObject firstName)");
        Assert.Contains("if (fullName != null && firstName != null)\n                return;", prefix);
        Assert.Contains("else if (firstName == null)\n                firstName = fullName;\n            else\n                fullName = firstName;", prefix);
        Assert.Contains("var missing = none ? \"name at all\" : firstName == null ? \"first name\" : \"full name\";", prefix);
        Assert.True(prefix.IndexOf("firstName = fullName;", StringComparison.Ordinal) < prefix.IndexOf("try", StringComparison.Ordinal));
        Assert.Contains("first = NameFixSeen.Add(id);", prefix);
        Assert.Contains("was named with no ", prefix);
        Assert.Contains("catch (Exception ex)", prefix);

        // The tools count it like every other fix.
        var run = new BannerlordCoopLauncher.Core.Support.RescueRun();
        run.Begin(BannerlordCoopLauncher.Core.Support.RescueMode.Auto);
        Assert.Equal(BannerlordCoopLauncher.Core.Support.RescueRun.Seen.Fixed, run.Observe("[CoopSaveRescue] FIXED: Hero CharacterObject_4711 (Bob, clan Nos Finarfin) was named with no first name, which stops the server in this Coop build. Used the other name for both, as the game does when a save loads, so the server keeps running and the players get the same name."));
        Assert.Single(run.Fixes);

        // Both READMEs say it, and the shipped DLL was rebuilt from this source.
        foreach (var readme in new[]
        {
            Path.Combine(RepoRoot(), "compat", "CoopSaveRescue", "README.txt"),
            Path.Combine(RepoRoot(), "src", "CoodszHost", "SaveRescue", "DedicatedServer.CoopSaveRescue", "README.txt")
        })
            Assert.Contains("Coop 0.1.6 sends the new name to the players and crashes when the first name is missing.", File.ReadAllText(readme));
        var dll = File.ReadAllBytes(Path.Combine(RepoRoot(), "src", "CoodszHost", "SaveRescue", "DedicatedServer.CoopSaveRescue", "bin", "Win64_Shipping_Server", "CoopSaveRescue.dll"));
        var text = System.Text.Encoding.ASCII.GetString(dll);
        Assert.Contains("SetNamePrefix", text);
        Assert.Contains("InstallNameGuard", text);
        Assert.Contains("OnGameInitializationFinished", text);
        Assert.True(dll.AsSpan().IndexOf(System.Text.Encoding.Unicode.GetBytes("GameInterface.Services.Heroes.Data.HeroChangeNameData")) >= 0);
        Assert.True(dll.AsSpan().IndexOf(System.Text.Encoding.Unicode.GetBytes(") was named with no ")) >= 0);

        static string Body(string code, string signature)
        {
            var start = code.IndexOf(signature, StringComparison.Ordinal);
            Assert.True(start >= 0, signature);
            var end = code.IndexOf("\n        }\n", start, StringComparison.Ordinal);
            return code[start..end];
        }
    }

    [Fact]
    public void Rescue_gives_a_boy_with_a_player_father_an_empty_name_until_the_game_names_him_so_coop_0_1_6_does_not_stop_the_server()
    {
        // CR-36XTG: Hero.SetInitialValuesFromCharacter calls SetName(characterObject.Name, characterObject.Name), so a boy copied
        // from a player father's nameless character has both names null, and the exactly one check let Coop's prefix throw.
        var source = File.ReadAllText(Path.Combine(RepoRoot(), "compat", "CoopSaveRescue", "SubModule.cs")).Replace("\r\n", "\n");
        var prefix = Body(source, "private static void SetNamePrefix(Hero __instance, ref TextObject fullName, ref TextObject firstName)");
        Assert.DoesNotContain("if ((fullName == null) == (firstName == null))", prefix);
        // Both null get one empty TextObject (never null, which Coop's NameIntercept also reads), before the log try so a log failure can't skip it.
        Assert.Contains("var none = fullName == null && firstName == null;", prefix);
        Assert.Contains("if (none)\n                fullName = firstName = new TextObject(\"\");\n            else if (firstName == null)", prefix);
        Assert.True(prefix.IndexOf("new TextObject(\"\")", StringComparison.Ordinal) < prefix.IndexOf("try", StringComparison.Ordinal));
        // Still once per hero, with its own line; the one missing name line stays as it was.
        Assert.Contains("if (first && none)", prefix);
        Assert.Contains("Gave it an empty name so the server keeps running. A child of a player gets his real name right after, and Coop sends that name", prefix);
        Assert.Contains("else if (first)", prefix);
        Assert.Contains("Used the other name for both, as the game does when a save loads", prefix);

        var run = new BannerlordCoopLauncher.Core.Support.RescueRun();
        run.Begin(BannerlordCoopLauncher.Core.Support.RescueMode.Auto);
        Assert.Equal(BannerlordCoopLauncher.Core.Support.RescueRun.Seen.Fixed, run.Observe("[CoopSaveRescue] FIXED: Hero CharacterObject_4712 (from main_hero) was named with no name at all, which stops the server in this Coop build (Coop #4104). Gave it an empty name so the server keeps running. A child of a player gets his real name right after, and Coop sends that name to the players."));
        Assert.Single(run.Fixes);

        foreach (var readme in new[]
        {
            Path.Combine(RepoRoot(), "compat", "CoopSaveRescue", "README.txt"),
            Path.Combine(RepoRoot(), "src", "CoodszHost", "SaveRescue", "DedicatedServer.CoopSaveRescue", "README.txt")
        })
            Assert.Contains("A boy whose father is a player\n  starts with no name at all", File.ReadAllText(readme).Replace("\r\n", "\n"));
        // The shipped DLL was rebuilt from this source.
        var dll = File.ReadAllBytes(Path.Combine(RepoRoot(), "src", "CoodszHost", "SaveRescue", "DedicatedServer.CoopSaveRescue", "bin", "Win64_Shipping_Server", "CoopSaveRescue.dll"));
        Assert.True(dll.AsSpan().IndexOf(System.Text.Encoding.Unicode.GetBytes("Gave it an empty name so the server keeps running. A child of a player gets his real name right after, and Coop sends that name")) >= 0);

        static string Body(string code, string signature)
        {
            var start = code.IndexOf(signature, StringComparison.Ordinal);
            Assert.True(start >= 0, signature);
            var end = code.IndexOf("\n        }\n", start, StringComparison.Ordinal);
            return code[start..end];
        }
    }

    [Fact]
    public void Rescue_copies_a_boy_with_a_player_father_from_a_lord_template_so_coop_0_1_6_does_not_stop_the_server()
    {
        // Coop #4104 live test: after the name fix the same birth threw in CharacterObject.get_StealthEquipments, since a boy
        // is copied from his father's character and a player's has no culture of its own. TAOM overrides the template model, so
        // the swap sits in HeroCreator.CreateHero, and only while DeliverOffSpring runs.
        var source = File.ReadAllText(Path.Combine(RepoRoot(), "compat", "CoopSaveRescue", "SubModule.cs")).Replace("\r\n", "\n");
        Assert.Contains("if (GameNetwork.IsDedicatedServer)\n            {\n                Patch(harmony, typeof(KillCharacterAction)", source);
        Assert.Contains("Patch(harmony, typeof(HeroCreator).GetMethod(\"DeliverOffSpring\", Any, null, new[] { typeof(Hero), typeof(Hero), typeof(bool) }, null),\n                    nameof(DeliverOffSpringPrefix), nameof(DeliverOffSpringFinalizer), \"HeroCreator.DeliverOffSpring\");", source);
        Assert.Contains("Patch(harmony, typeof(HeroCreator).GetMethod(\"CreateHero\", Any, null, new[] { typeof(CharacterObject), typeof(bool), typeof(CampaignTime), typeof(CampaignTime) }, null),\n                    nameof(CreateHeroPrefix), null, \"HeroCreator.CreateHero\");", source);

        // The parents are only known while DeliverOffSpring runs; the finalizer puts the earlier ones back, also when it threw.
        Assert.Contains("[ThreadStatic]\n        private static Hero[] _birthParents;", source);
        Assert.Contains("_birthParents = new[] { mother, father };", Body(source, "private static void DeliverOffSpringPrefix(Hero mother, Hero father, out Hero[] __state)"));
        var finalizer = Body(source, "private static Exception DeliverOffSpringFinalizer(Exception __exception, Hero[] __state)");
        Assert.Contains("_birthParents = __state;", finalizer);
        Assert.Contains("return __exception;", finalizer);

        // Normal births stay untouched: outside a birth, a regular template or a healthy hero character returns before anything changes.
        var prefix = Body(source, "private static void CreateHeroPrefix(ref CharacterObject character, bool useCharacterAsTemplate)");
        Assert.Contains("if (parents == null || !useCharacterAsTemplate || character == null || !character.IsHero)\n                return;", prefix);
        Assert.Contains("if (broken == null)\n                    return;", prefix);
        Assert.True(prefix.IndexOf("if (broken == null)", StringComparison.Ordinal) < prefix.IndexOf("character = template;", StringComparison.Ordinal));
        Assert.True(prefix.IndexOf("if (template == null)", StringComparison.Ordinal) < prefix.IndexOf("character = template;", StringComparison.Ordinal));
        Assert.Contains("used \"\n                    + template.StringId + \" instead so the birth doesn't stop the server (Coop #4104).\");", prefix);
        Assert.Contains("catch (Exception ex)", prefix);

        // Broken means what SetInitialValuesFromCharacter trips on in the copy: the character's own culture (not the hero's) and name.
        var broken = Body(source, "private static string Broken(CharacterObject character)");
        Assert.Contains("var culture = ((BasicCharacterObject)character).Culture;", broken);
        Assert.Contains("full.DefaultBattleEquipmentRoster != null && full.DefaultStealthEquipmentRoster == null", broken);
        Assert.Contains("BasicName.GetValue(character) == null", broken);
        Assert.Contains("GetField(\"_basicName\", Any)", source);

        // The new template has the same gender, the same race first, and is healthy itself; parents' cultures come first.
        var safe = Body(source, "private static CharacterObject SafeTemplate(bool female, int race, params CultureObject[] cultures)");
        Assert.Contains(".SelectMany(culture => culture.LordTemplates", safe);
        Assert.Contains("template.IsFemale == female && Broken(template) == null", safe);
        Assert.Contains("candidates.FirstOrDefault(template => template.Race == race) ?? candidates.FirstOrDefault()", safe);
        Assert.True(safe.IndexOf("cultures.Where", StringComparison.Ordinal) < safe.IndexOf("GetObjectTypeList<CultureObject>()", StringComparison.Ordinal));

        // The tools count it like every other fix.
        var run = new BannerlordCoopLauncher.Core.Support.RescueRun();
        run.Begin(BannerlordCoopLauncher.Core.Support.RescueMode.Auto);
        Assert.Equal(BannerlordCoopLauncher.Core.Support.RescueRun.Seen.Fixed, run.Observe("[CoopSaveRescue] FIXED: Child of hero Bob [CharacterObject_4711] would have copied that hero's broken character (no culture and no name, as a player hero has in this Coop build), used spc_empire_lord_template_male instead so the birth doesn't stop the server (Coop #4104)."));
        Assert.Single(run.Fixes);

        foreach (var readme in new[]
        {
            Path.Combine(RepoRoot(), "compat", "CoopSaveRescue", "README.txt"),
            Path.Combine(RepoRoot(), "src", "CoodszHost", "SaveRescue", "DedicatedServer.CoopSaveRescue", "README.txt")
        })
            Assert.Contains("NullReferenceException in CharacterObject.get_StealthEquipments, Coop #4104", File.ReadAllText(readme));
        // The shipped DLL was rebuilt from this source.
        var dll = File.ReadAllBytes(Path.Combine(RepoRoot(), "src", "CoodszHost", "SaveRescue", "DedicatedServer.CoopSaveRescue", "bin", "Win64_Shipping_Server", "CoopSaveRescue.dll"));
        var text = System.Text.Encoding.ASCII.GetString(dll);
        Assert.Contains("CreateHeroPrefix", text);
        Assert.Contains("DeliverOffSpringFinalizer", text);
        Assert.True(dll.AsSpan().IndexOf(System.Text.Encoding.Unicode.GetBytes(" instead so the birth doesn't stop the server (Coop #4104).")) >= 0);
        Assert.True(dll.AsSpan().IndexOf(System.Text.Encoding.Unicode.GetBytes("_basicName")) >= 0);

        static string Body(string code, string signature)
        {
            var start = code.IndexOf(signature, StringComparison.Ordinal);
            Assert.True(start >= 0, signature);
            var end = code.IndexOf("\n        }\n", start, StringComparison.Ordinal);
            return code[start..end];
        }
    }

    [Fact]
    public void Rescue_fills_a_parent_character_missing_culture_or_body_shape_before_a_birth_like_coop_3858()
    {
        // Coop #4104 live test: after the template swap the birth threw in DefaultHeroCreationModel.GetStaticBodyProperties,
        // since the player father's character has no BodyPropertyRange. Coop fixed it on development in #3858
        // (RepairMissingParentCharacterData); the rescue does the same in its DeliverOffSpring prefix.
        var source = File.ReadAllText(Path.Combine(RepoRoot(), "compat", "CoopSaveRescue", "SubModule.cs")).Replace("\r\n", "\n");
        var prefix = Body(source, "private static void DeliverOffSpringPrefix(Hero mother, Hero father, out Hero[] __state)");
        Assert.Contains("RepairParent(mother);\n            RepairParent(father);", prefix);

        // The setter is protected, so it goes through reflection.
        Assert.Contains("typeof(BasicCharacterObject).GetProperty(\"BodyPropertyRange\", Any)?.GetSetMethod(true)", source);

        // Only a value that is really missing is filled; a healthy parent is never touched. Sources follow #3858.
        var repair = Body(source, "private static void RepairParent(Hero parent)");
        Assert.Contains("if (basic.Culture == null && parent.Culture != null)", repair);
        Assert.Contains("basic.Culture = parent.Culture;", repair);
        Assert.Contains("if (basic.BodyPropertyRange == null && SetBodyRange != null)", repair);
        Assert.Contains("new[] { character.OriginalCharacter, Game.Current.ObjectManager.GetObject<CharacterObject>(\"main_hero\") }", repair);
        Assert.Contains("?? SafeTemplate(character.IsFemale, character.Race, true, parent.Culture);", repair);
        Assert.Contains("(!needsBody || template.BodyPropertyRange != null)", source);
        Assert.Contains("SetBodyRange.Invoke(character, new object[] { source.BodyPropertyRange });", repair);
        Assert.Contains("if (missing.Count > 0)", repair);
        Assert.Contains("catch (Exception ex)", repair);
        // The repair is kept after the birth, as in Coop's fix, so the finalizer only puts the parents back.
        Assert.DoesNotContain("BodyPropertyRange", Body(source, "private static Exception DeliverOffSpringFinalizer(Exception __exception, Hero[] __state)"));

        // The tools count it like every other fix.
        var run = new BannerlordCoopLauncher.Core.Support.RescueRun();
        run.Begin(BannerlordCoopLauncher.Core.Support.RescueMode.Auto);
        Assert.Equal(BannerlordCoopLauncher.Core.Support.RescueRun.Seen.Fixed, run.Observe("[CoopSaveRescue] FIXED: Hero Bob [CharacterObject_4711] had no culture or body shape on the server, filled them from the hero's own culture gondor and main_hero before the birth so it doesn't stop the server (Coop #4104)."));
        Assert.Single(run.Fixes);

        foreach (var readme in new[]
        {
            Path.Combine(RepoRoot(), "compat", "CoopSaveRescue", "README.txt"),
            Path.Combine(RepoRoot(), "src", "CoodszHost", "SaveRescue", "DedicatedServer.CoopSaveRescue", "README.txt")
        })
            Assert.Contains("DefaultHeroCreationModel.GetStaticBodyProperties, Coop #4104", File.ReadAllText(readme));
        // The shipped DLL was rebuilt from this source.
        var dll = File.ReadAllBytes(Path.Combine(RepoRoot(), "src", "CoodszHost", "SaveRescue", "DedicatedServer.CoopSaveRescue", "bin", "Win64_Shipping_Server", "CoopSaveRescue.dll"));
        Assert.Contains("RepairParent", System.Text.Encoding.ASCII.GetString(dll));
        Assert.True(dll.AsSpan().IndexOf(System.Text.Encoding.Unicode.GetBytes(" before the birth so it doesn't stop the server (Coop #4104).")) >= 0);
        Assert.True(dll.AsSpan().IndexOf(System.Text.Encoding.Unicode.GetBytes("main_hero")) >= 0);

        static string Body(string code, string signature)
        {
            var start = code.IndexOf(signature, StringComparison.Ordinal);
            Assert.True(start >= 0, signature);
            var end = code.IndexOf("\n        }\n", start, StringComparison.Ordinal);
            return code[start..end];
        }
    }

    [Fact]
    public void Rescue_keeps_the_captivity_menu_shut_for_the_servers_own_hero_so_the_world_moves()
    {
        // TAOM CR-DFX5S: the save had the server's own hero as a prisoner. PlayerCaptivity.Update reopened the captivity menu
        // on every tick and every party stood on Holding. Ending the captivity with the game's own escape threw on the main
        // party Coop removes (live test 2026-11-08), so the update is skipped on the server instead, as Coop's own fix does.
        var source = File.ReadAllText(Path.Combine(RepoRoot(), "compat", "CoopSaveRescue", "SubModule.cs")).Replace("\r\n", "\n");

        // Server only, next to the other server checks, and counted in the Ready line.
        var load = Body(source, "protected override void OnSubModuleLoad()");
        var serverOnly = load.IndexOf("if (GameNetwork.IsDedicatedServer)", StringComparison.Ordinal);
        var patch = load.IndexOf("Patch(harmony, typeof(PlayerCaptivity).GetMethod(\"Update\", Any, null, new[] { typeof(float) }, null),\n"
            + "                    nameof(CaptivityUpdatePrefix), null, \"PlayerCaptivity.Update\", FirstPriority);", StringComparison.Ordinal);
        Assert.True(serverOnly >= 0);
        Assert.True(patch > serverOnly);
        Assert.True(patch < load.IndexOf("Log(\"Ready in ", StringComparison.Ordinal));
        Assert.Contains("of 14 checks active", source);
        // First priority: Coop's own prefix also returns false on the server, and a prefix after a false one may be skipped,
        // so this one runs first and still logs once Coop's fix lands.
        Assert.Contains("private const int FirstPriority = 800;", source);

        // The update never runs on the server, so the hero stays a prisoner and nothing touches the main party.
        var prefix = Body(source, "private static bool CaptivityUpdatePrefix()");
        Assert.EndsWith("return false;", prefix.TrimEnd());
        Assert.Equal(1, System.Text.RegularExpressions.Regex.Matches(prefix, @"return ").Count);
        Assert.DoesNotContain("EndCaptivityAction", prefix);
        Assert.DoesNotContain("MainParty", prefix);
        Assert.DoesNotContain("EndServerHeroCaptivity", source);
        Assert.DoesNotContain("_captivityPending", source);
        Assert.DoesNotContain("_captivityRetry", source);

        // One plain line per load while the hero is a prisoner. Not FIXED: nothing in the save changes, and a FIXED line
        // would make the tools save and back up the game on every start.
        Assert.DoesNotContain("Fix(", prefix);
        Assert.Contains("if (!_captivityLogged)", prefix);
        Assert.True(prefix.IndexOf("if (PlayerCaptivity.IsCaptive)", StringComparison.Ordinal) < prefix.IndexOf("Log(", StringComparison.Ordinal));
        Assert.Contains("_captivityLogged = false;", Body(source, "private static void LoadingFinishedPrefix(MapState __instance)"));
        const string line = "[CoopSaveRescue] The server's own hero is a prisoner of Looters in the save. The server keeps the captivity menu shut, so the world moves; the hero stays a prisoner.";
        Assert.Contains("\" in the save. The server keeps the captivity menu shut, so the world moves; the hero stays a prisoner.\"", prefix);
        var rescue = new BannerlordCoopLauncher.Core.Support.RescueRun();
        rescue.Begin(BannerlordCoopLauncher.Core.Support.RescueMode.Auto);
        Assert.Equal(BannerlordCoopLauncher.Core.Support.RescueRun.Seen.Nothing, rescue.Observe(line));
        Assert.Empty(rescue.Fixes);

        // The report line says so too, still as "nothing to fix" when it is the only thing, so the tools save nothing.
        Assert.Contains("_captivitySeen = true;", prefix);
        var report = source[source.IndexOf("if (!_reported && seconds >= 60)", StringComparison.Ordinal)..];
        report = report[..report.IndexOf("\n        }\n", StringComparison.Ordinal)];
        Assert.Contains("const string Captive = \" The server's own hero is a prisoner in this save, and the captivity menu stays shut while this module loads.\";", report);
        Assert.Contains("? (_captivitySeen ? \"Report: nothing to fix was found.\" + Captive", report);
        Assert.Contains("+ (_captivitySeen ? Captive : \"\"));", report);
        Assert.True(BannerlordCoopLauncher.Core.Support.AutoRescue.FoundNothing("[CoopSaveRescue] Report: nothing to fix was found. The server's own hero is a prisoner in this save, and the captivity menu stays shut while this module loads."));

        // The module version moved on together everywhere the tools show it.
        var payload = Path.Combine(RepoRoot(), "src", "CoodszHost", "SaveRescue", "DedicatedServer.CoopSaveRescue");
        Assert.Contains("<Version value=\"v1.3.0\"/>", File.ReadAllText(Path.Combine(RepoRoot(), "compat", "CoopSaveRescue", "SubModule.xml")));
        Assert.Contains("<Version value=\"v1.3.0\"/>", File.ReadAllText(Path.Combine(payload, "SubModule.xml")));
        Assert.Contains("Version = \"v1.3.0\"", File.ReadAllText(Path.Combine(RepoRoot(), "src", "CoodszHost", "Services", "SaveRescue.cs")));
        Assert.Contains("Keeps that menu shut so the world moves again, for as long as Save", File.ReadAllText(Path.Combine(payload, "README.txt")));
        // The by hand zip removes the module after one start, so it says this one comes back without it.
        var byHand = File.ReadAllText(Path.Combine(RepoRoot(), "compat", "CoopSaveRescue", "vanilla", "README.txt")).Replace("\r\n", "\n");
        Assert.Contains("so after step 8 it stops again.", byHand);
        Assert.Contains("Coop Server Tool, which loads Save Rescue on every start", byHand);
        // Its "nothing to fix" report then names the prisoner, so the README does not send that host to a support ticket.
        Assert.Contains("If the Report says \"nothing to fix\" and says nothing about the server's own\nhero being a prisoner", byHand);
        // The shipped DLL was rebuilt from this source.
        var dll = File.ReadAllBytes(Path.Combine(payload, "bin", "Win64_Shipping_Server", "CoopSaveRescue.dll"));
        var ascii = System.Text.Encoding.ASCII.GetString(dll);
        Assert.Contains("CaptivityUpdatePrefix", ascii);
        Assert.DoesNotContain("EndServerHeroCaptivity", ascii);
        Assert.True(dll.AsSpan().IndexOf(System.Text.Encoding.Unicode.GetBytes(" in the save. The server keeps the captivity menu shut, so the world moves; the hero stays a prisoner.")) >= 0);

        static string Body(string code, string signature)
        {
            var start = code.IndexOf(signature, StringComparison.Ordinal);
            Assert.True(start >= 0, signature);
            var end = code.IndexOf("\n        }\n", start, StringComparison.Ordinal);
            return code[start..end];
        }
    }
}
