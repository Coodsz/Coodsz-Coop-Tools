using BannerlordCoopLauncher.Core.Joining;
using TaomCoop.Core;

namespace BannerlordCoopLauncher.Tests;

// Fixes from a live test of the TAOM tools: own server detection, the Steam lobby wait, the
// status after a fatal load, and saves made on another TAOM release.
[Collection(LocalServerFolders.Name)]
public class TaomLiveTestFixesTests
{
    private const string FatalLine = "23:06:28.948  @DS@{\"ev\":\"state\",\"phase\":\"fatal\",\"save\":\"bugtest\",\"detail\":\"MBObjectManager.LoadXml failed for Settlements. The campaign is incomplete and must not be served. If updating TAOM, use a save created with this TAOM release. Original error: System.ArgumentOutOfRangeException: Index was out of range.\"}";

    [Fact]
    public void Data_dir_is_read_from_the_server_command_line()
    {
        Assert.Equal(@"c:\TAOM-Coop-Host\Data-v0325", LocalServer.DataDirFromCommandLine("\"C:\\TAOM-Coop-Host\\Server\\BannerlordCoopServer.exe\" --data-dir \"c:\\TAOM-Coop-Host\\Data-v0325\""));
        Assert.Equal(@"D:\Data", LocalServer.DataDirFromCommandLine(@"BannerlordCoopServer.exe --data-dir D:\Data\"));
        Assert.Equal(@"D:\My Data", LocalServer.DataDirFromCommandLine("BannerlordCoopServer.exe --data-dir=\"D:\\My Data\""));
        Assert.Null(LocalServer.DataDirFromCommandLine("BannerlordCoopServer.exe"));
        Assert.Null(LocalServer.DataDirFromCommandLine(null));
    }

    [Fact]
    public void The_running_servers_data_folder_is_read_before_documents()
    {
        var paths = LocalServer.ConfigCandidates(new[] { "x.exe --data-dir \"c:\\TAOM-Coop-Host\\Data-v0325\"", null, "x.exe" });
        Assert.Equal(Path.Combine(@"c:\TAOM-Coop-Host\Data-v0325", "server-config.json"), paths[0]);
        Assert.Equal(LocalServer.ServerConfigPath, paths[^1]);
        Assert.Equal(2, paths.Count);
        Assert.Equal(new[] { LocalServer.ServerConfigPath }, LocalServer.ConfigCandidates(Array.Empty<string?>()));
    }

    [WindowsFact]
    public void The_command_line_of_a_process_can_be_read()
    {
        var line = ProcessCommandLine.Read(Environment.ProcessId);
        Assert.False(string.IsNullOrWhiteSpace(line));
        Assert.Null(ProcessCommandLine.Read(-1));
    }

    [WindowsFact]
    public void A_server_engine_outside_a_DedicatedServer_folder_is_found()
    {
        var root = Path.Combine(Path.GetTempPath(), "taom-engine-" + Guid.NewGuid().ToString("N"));
        try
        {
            var bin = Directory.CreateDirectory(Path.Combine(root, "Server-v0325", "engine", "bin", "Win64_Shipping_Server"));
            File.WriteAllText(Path.Combine(bin.FullName, "TaleWorlds.Starter.DotNetCore.dll"), "");
            Assert.True(LocalServer.IsServerEngine(Path.Combine(root, "Server-v0325", "engine", "dotnet", "dotnet.exe")));
            Assert.False(LocalServer.IsServerEngine(Path.Combine(root, "Other", "engine", "dotnet", "dotnet.exe")));
            Assert.False(LocalServer.IsServerEngine(@"C:\Program Files\dotnet\dotnet.exe"));
            Assert.True(LocalServer.IsServerEngine(@"D:\Steam\DedicatedServer\engine\dotnet\dotnet.exe"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Taom_steam_lobby_steps_say_it_can_take_up_to_an_hour()
    {
        var host = new JoinFacts { IsHost = true, Taom = true, SteamOption = "Advertise through Steam", SteamLobbyOn = true };
        var client = new JoinFacts { Taom = true, SteamOption = "Advertise through Steam" };
        Assert.Contains(JoinGuide.Steps(GuideTopic.SteamLobby, host), step => step.Text.Contains("up to an hour to show up", StringComparison.Ordinal));
        Assert.Contains(JoinGuide.Steps(GuideTopic.SteamLobby, client), step => step.Text.Contains("up to an hour to show up", StringComparison.Ordinal));
        Assert.DoesNotContain(JoinGuide.Steps(GuideTopic.SteamLobby, new JoinFacts { IsHost = true }), step => step.Text.Contains("TAOM", StringComparison.Ordinal));
        Assert.Equal("Click Connect.", JoinGuide.Steps(GuideTopic.SteamLobby, client)[^1].Text);
    }

    [Fact]
    public void Both_taom_tools_pass_taom_to_the_join_guide()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "BannerlordCoopLauncher.sln")))
            root = root.Parent;
        Assert.NotNull(root);
        foreach (var project in new[] { "TaomCoopHost", "TaomCoopLauncher" })
            Assert.Contains("Taom = true", SourceFiles.Window(root!.FullName, project), StringComparison.Ordinal);
        Assert.Contains("ShowOwnServerHintAsync(fill: UseDirect)", SourceFiles.Window(root!.FullName, "TaomCoopLauncher"), StringComparison.Ordinal);
    }

    [Fact]
    public void The_start_banner_is_not_serving()
    {
        const string banner = "23:05:11.310  server is READY when you see:  [DedicatedServer] SERVING";
        const string real = "23:52:54.054  [DedicatedServer] SERVING - coop server up, waiting for clients";
        Assert.False(ServerStateEvent.IsServingLine(banner));
        Assert.True(ServerStateEvent.IsServingLine(real));
        Assert.False(ServerStateEvent.HasServingLine(banner + "\n" + FatalLine));
        Assert.True(ServerStateEvent.HasServingLine(banner + "\r\n" + real + "\r\n"));
    }

    [Fact]
    public void The_indented_banner_line_is_not_serving()
    {
        // The newer banner puts the marker on its own indented line (Coodsz Host live test, 2026-10-18).
        const string intro = "01:08:51.912  Starting the native engine. The server is ready when the log shows:";
        const string banner = "01:08:51.913    [DedicatedServer] SERVING";
        const string real = "01:10:04.544  [DedicatedServer] SERVING — coop server up, waiting for clients";
        const string state = "01:10:04.547  @DS@{\"ev\":\"state\",\"phase\":\"serving\",\"save\":\"cktest\",\"pw\":true}";
        Assert.False(ServerStateEvent.IsServingLine(banner));
        Assert.False(ServerStateEvent.HasServingLine(intro + "\r\n" + banner + "\r\n"));
        Assert.True(ServerStateEvent.IsServingLine(real));
        Assert.True(ServerStateEvent.IsServingLine(state));
        Assert.True(ServerStateEvent.HasServingLine(intro + "\r\n" + banner + "\r\n" + real + "\r\n"));
    }

    [Fact]
    public void State_events_are_read_by_the_shared_parser()
    {
        // The serving state event counts as serving, as in the CoopKeep port.
        Assert.True(ServerStateEvent.IsServingLine("21:24:25.353  @DS@{\"ev\":\"state\",\"phase\":\"serving\",\"save\":\"a\",\"pw\":false}"));
        Assert.False(ServerStateEvent.IsServingLine("@DS@{\"ev\":\"state\",\"phase\":\"loading\",\"save\":\"a\",\"pw\":false}"));

        // DsEventParser keeps the fatal detail, and an event glued to another line is still found.
        Assert.True(BannerlordCoopLauncher.Core.Server.DsEventParser.TryParse(FatalLine, out var evt));
        var parsed = Assert.IsType<BannerlordCoopLauncher.Core.Server.DsEvent.State>(evt);
        Assert.Equal("fatal", parsed.RawPhase);
        Assert.StartsWith("MBObjectManager.LoadXml failed", parsed.Detail);
        var glued = ServerStateEvent.Last("x\n14:49:18.816  [Engine] save block started@DS@{\"ev\":\"state\",\"phase\":\"stopping\",\"save\":\"s\"} trailing\n");
        Assert.Equal("stopping", glued!.Phase);
        Assert.Equal("s", glued.Save);
    }

    [Fact]
    public void The_newest_state_event_decides()
    {
        var log = "@DS@{\"ev\":\"state\",\"phase\":\"boot\",\"save\":\"bugtest\",\"pw\":false}\n@DS@{\"ev\":\"state\",\"phase\":\"loading\",\"save\":\"bugtest\",\"pw\":false}\n" + FatalLine + "\n[BannerlordCoopServer] engine exited with code 2";
        var state = ServerStateEvent.Last(log);
        Assert.NotNull(state);
        Assert.True(state!.Fatal);
        Assert.Equal("bugtest", state.Save);
        Assert.True(ServerStateEvent.Last("@DS@{\"ev\":\"state\",\"phase\":\"serving\",\"save\":\"a\"}")!.Serving);
        Assert.Null(ServerStateEvent.Last("@DS@{\"ev\":\"players\",\"list\":[]}"));
        Assert.Null(ServerStateEvent.Last("@DS@{\"ev\":\"state\",\"phase\":\"fat"));
    }

    [Fact]
    public void A_fatal_load_of_an_older_taom_save_is_explained_plainly()
    {
        var state = ServerStateEvent.Last(FatalLine)!;
        var status = ServerStateEvent.FatalStatus(state);
        Assert.Contains("bugtest", status);
        Assert.Contains("older TAOM version", status);
        Assert.Contains(TaomStack.TaomVersionText, status);
        Assert.DoesNotContain("Serving", status, StringComparison.OrdinalIgnoreCase);

        var report = TaomHostReport.Explain(FatalLine, wasServing: false);
        Assert.NotNull(report);
        Assert.True(report.Value.Failed);
        Assert.Equal(status, report.Value.Status);
        Assert.Contains("Start a new campaign", report.Value.Lines[1]);

        var other = ServerStateEvent.Last("@DS@{\"ev\":\"state\",\"phase\":\"fatal\",\"save\":\"s\",\"detail\":\"Port 4200 is in use. Original error: x\"}")!;
        Assert.Equal("The server could not load the save s and stopped. Port 4200 is in use.", ServerStateEvent.FatalStatus(other));
    }

    [Fact]
    public void Save_header_module_versions_are_read()
    {
        var header = "\u0001\u0002\u0000\u0000{\"List\":{\"Modules\":\"Native;TAOM;TAOM_Map\",\"Module_Native\":\"v1.4.8.0\",\"Module_TAOM\":\"v2.0.27.0\",\"Module_TAOM_Map\":\"v2.0.27.0\",\"Module_LOTRLOME_Armory\":\"v2.0.27.0\"}}";
        var versions = SaveTaomVersion.ModuleVersions(header);
        Assert.Equal("2.0.27.0", versions["TAOM"]);
        Assert.Equal("1.4.8.0", versions["Native"]);
        Assert.Equal("2.0.27", SaveTaomVersion.MismatchedVersion(versions, "2.0.29.5"));
        Assert.Equal(
            "This campaign was made on TAOM 2.0.27. It will not load on TAOM 2.0.29.5. Start a new campaign in Bannerlord and pick that save.",
            SaveTaomVersion.Warning("2.0.27.0", "2.0.29.5"));
    }

    [Fact]
    public void Only_the_first_three_version_parts_count()
    {
        Assert.False(SaveTaomVersion.Differs("2.0.29.0", "2.0.29.5"));
        Assert.False(SaveTaomVersion.Differs("v2.0.29.5", "2.0.29.5"));
        Assert.True(SaveTaomVersion.Differs("2.0.27.0", "2.0.29.5"));
        Assert.True(SaveTaomVersion.Differs("2.1.29", "2.0.29.5"));
        Assert.Null(SaveTaomVersion.MismatchedVersion(new Dictionary<string, string> { ["TAOM"] = "2.0.29.5", ["TAOM_Map"] = "2.0.29.0" }, "2.0.29.5"));
        Assert.Null(SaveTaomVersion.MismatchedVersion(new Dictionary<string, string>(), "2.0.29.5"));
        Assert.Equal("2.0.28", SaveTaomVersion.MismatchedVersion(new Dictionary<string, string> { ["LOTRLOME_Armory"] = "2.0.28.0" }, "2.0.29.5"));
    }

    [Fact]
    public void A_save_file_on_disk_is_read()
    {
        var path = Path.Combine(Path.GetTempPath(), "taom-save-" + Guid.NewGuid().ToString("N") + ".sav");
        try
        {
            var json = "{\"List\":{\"Module_TAOM\":\"v2.0.27.0\"}}";
            using (var stream = File.Create(path))
            {
                stream.Write(BitConverter.GetBytes(json.Length));
                stream.Write(System.Text.Encoding.UTF8.GetBytes(json));
                stream.Write(new byte[] { 0x78, 0x9C, 0x00, 0xFF });
            }

            Assert.Equal("2.0.27.0", SaveTaomVersion.Read(path)["TAOM"]);
            Assert.Empty(SaveTaomVersion.Read(path + ".missing"));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
