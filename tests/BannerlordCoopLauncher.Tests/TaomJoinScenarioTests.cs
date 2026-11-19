using System.Text.Json;
using BannerlordCoopLauncher.Core.Launch;
using BannerlordCoopLauncher.Core.Network;
using TaomCoop.Core;

namespace BannerlordCoopLauncher.Tests;

public class TaomJoinScenarioTests
{
    private const string Options = """
        {
          "CrashReporting": { "Consent": { "automaticCrashReports": false } },
          "Connection": { "DirectConnection": { "lastServerAddress": "192.168.1.135" } }
        }
        """;

    [Theory]
    [InlineData("Barneykoo")]
    [InlineData("host.example.com:4200")]
    [InlineData("192.168.1")]
    [InlineData("192.168.1.256")]
    [InlineData("192.168.1.135 extra")]
    [InlineData("")]
    [InlineData("  ")]
    public void A_wrong_address_is_refused_and_the_saved_address_stays(string address)
    {
        Assert.False(ClientJoin.IsDirectAddress(address));
        var error = Assert.Throws<InvalidOperationException>(() => ClientJoin.SetDirectAddress(Options, address));
        Assert.Equal(ClientJoin.IpRequired, error.Message);
        using var document = JsonDocument.Parse(Options);
        Assert.Equal("192.168.1.135", document.RootElement.GetProperty("Connection").GetProperty("DirectConnection").GetProperty("lastServerAddress").GetString());
    }

    [Theory]
    [InlineData("192.168.1.135")]
    [InlineData("10.1.2.3")]
    [InlineData("172.16.56.97")]
    [InlineData("172.31.0.1")]
    public void A_private_address_is_accepted_and_called_a_home_network_address(string address)
    {
        Assert.True(ClientJoin.IsHomeNetwork(address));
        var saved = ClientJoin.SetDirectAddress(Options, address);
        using var document = JsonDocument.Parse(saved);
        Assert.Equal(address, document.RootElement.GetProperty("Connection").GetProperty("DirectConnection").GetProperty("lastServerAddress").GetString());
        Assert.False(document.RootElement.GetProperty("CrashReporting").GetProperty("Consent").GetProperty("automaticCrashReports").GetBoolean());

        var reply = NoReply(address);
        Assert.Contains("same home network", reply);
        Assert.Contains("No Coop server answered", reply);
        Assert.Contains("port forwarded", reply);
        Assert.DoesNotContain("may be stopped", reply);
    }

    [Theory]
    [InlineData("host.example.com")]
    [InlineData("my.server.com")]
    [InlineData("play.ddns.net")]
    public void A_custom_port_forwarded_address_is_accepted(string address)
    {
        Assert.False(ClientJoin.IsIpv4(address));
        Assert.True(ClientJoin.IsDirectAddress(address));
        var saved = ClientJoin.SetDirectAddress(Options, address);
        using var document = JsonDocument.Parse(saved);
        Assert.Equal(address, document.RootElement.GetProperty("Connection").GetProperty("DirectConnection").GetProperty("lastServerAddress").GetString());
        var reply = NoReply(address);
        Assert.Contains("port forwarded", reply);
        Assert.Contains("may be stopped", reply);
        Assert.DoesNotContain("same home network", reply);
    }

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("172.15.0.1")]
    [InlineData("172.32.0.1")]
    [InlineData("11.0.0.1")]
    [InlineData("192.0.2.1")]
    public void A_public_address_with_no_reply_says_the_port_may_not_be_forwarded(string address)
    {
        Assert.True(ClientJoin.IsIpv4(address));
        Assert.False(ClientJoin.IsHomeNetwork(address));
        var reply = NoReply(address);
        Assert.Contains("custom public address", reply);
        Assert.Contains("not forwarded", reply);
        Assert.Contains("may be stopped", reply);
        Assert.DoesNotContain("same home network", reply);
        Assert.DoesNotContain("does not mean the server is stopped", reply);
    }

    [Fact]
    public void The_host_pc_address_is_not_described_as_a_port_forward()
    {
        Assert.True(ClientJoin.IsLoopback("127.0.0.1"));
        Assert.False(ClientJoin.IsHomeNetwork("127.0.0.1"));
        var quiet = NoReply("127.0.0.1");
        Assert.Contains("only the host's own PC", quiet);
        Assert.Contains("Friends cannot use it", quiet);
        Assert.Contains("No Coop server answered on 127.0.0.1", quiet);
        Assert.DoesNotContain("port forwarded", quiet);
        Assert.DoesNotContain("same home network", quiet);

        var down = ClientJoin.DescribeProbe(new ServerProbeResult(ServerReachability.Unreachable, "down"), "127.0.0.1");
        Assert.Contains("only the host's own PC", down);
        Assert.DoesNotContain("port forwarded", down);
    }

    [Fact]
    public void A_private_address_that_cannot_be_reached_is_not_called_a_stopped_server()
    {
        var reply = ClientJoin.DescribeProbe(new ServerProbeResult(ServerReachability.Unreachable, "down"), "10.4.5.6");
        Assert.Contains("same home network", reply);
        Assert.Contains("port forwarded", reply);
        Assert.DoesNotContain("may be stopped", reply);
        Assert.DoesNotContain("does not mean the server is stopped", reply);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("a")]
    [InlineData("name\"quote")]
    [InlineData("tab\tname")]
    public void A_wrong_steam_lobby_name_is_refused_before_the_game_starts(string name)
    {
        Assert.False(SteamHostName.IsValid(name));
    }

    [Theory]
    [InlineData("Cobra|El!te|#666")]
    [InlineData("Светлый Взор")]
    [InlineData("bad name!")]
    public void Any_steam_profile_name_without_a_quote_is_accepted(string name)
    {
        Assert.True(SteamHostName.IsValid(name));
    }

    [Fact]
    public void A_steam_name_that_only_looks_valid_does_not_send_the_private_address()
    {
        Assert.True(SteamHostName.IsValid("NotARealSteamName"));
        var cleared = ClientJoin.RemoveDirectAddress(Options);
        Assert.NotNull(cleared);
        using var document = JsonDocument.Parse(cleared);
        Assert.False(document.RootElement.GetProperty("Connection").GetProperty("DirectConnection").TryGetProperty("lastServerAddress", out _));
        Assert.False(document.RootElement.GetProperty("CrashReporting").GetProperty("Consent").GetProperty("automaticCrashReports").GetBoolean());

        var arguments = TaomLaunch.Arguments(connect: true, steamLobby: true, password: "", lobbyId: 77);
        Assert.Contains("+connect_lobby 77", arguments);
        Assert.DoesNotContain("/autoconnect", arguments);
        Assert.DoesNotContain("/coopowner", arguments);
        Assert.DoesNotContain("/steamname", arguments);
        Assert.DoesNotContain("192.168.1.135", arguments);
        Assert.DoesNotContain("/cooppassword", arguments);
    }

    [Fact]
    public void Low_terrain_is_raised_to_medium_and_other_settings_stay()
    {
        var config = "texture_quality = 2\r\nterrain_quality = 0\r\nwater_quality = 2\r\n";
        var raised = ClientJoin.RaiseLowTerrain(config);
        Assert.NotNull(raised);
        Assert.Contains("terrain_quality = 1", raised);
        Assert.Contains("texture_quality = 2", raised);
        Assert.DoesNotContain("terrain_quality = 0", raised);
        Assert.Null(ClientJoin.RaiseLowTerrain(raised));
        Assert.Null(ClientJoin.RaiseLowTerrain("terrain_quality = 2\r\n"));
        Assert.Null(ClientJoin.RaiseLowTerrain("terrain_quality = 10\r\n"));
    }

    [Fact]
    public async Task A_real_address_with_nobody_listening_is_not_online()
    {
        var probe = await UdpServerProbe.ProbeAsync("192.0.2.1", 4200, TimeSpan.FromSeconds(1), CancellationToken.None);
        Assert.NotEqual(ServerReachability.Online, probe.Reachability);
        var reply = ClientJoin.DescribeProbe(probe, "192.0.2.1");
        Assert.Contains("port forwarded", reply);
        Assert.DoesNotContain("does not mean the server is stopped", reply);
        Assert.DoesNotContain("same home network", reply);
    }

    [Fact]
    public void The_server_copies_only_the_dotnet_6_desktop_runtime_it_asks_for()
    {
        var root = Path.Combine(Path.GetTempPath(), "taom-scenario-" + Guid.NewGuid().ToString("N"));
        try
        {
            var global = Path.Combine(root, "global");
            WriteFramework(global, "Microsoft.WindowsDesktop.App", "6.0.16", "old");
            WriteFramework(global, "Microsoft.WindowsDesktop.App", "6.0.35", "desktop");
            WriteFramework(global, "Microsoft.WindowsDesktop.App", "8.0.21", "eight");
            WriteFramework(global, "Microsoft.NETCore.App", "6.0.36", "core");
            var server = PreparedServer(root, "server");
            var copied = ServerFramework.Ensure(server, global);
            Assert.True(copied.Ready);
            Assert.Equal("desktop", ReadMarker(server, "Microsoft.WindowsDesktop.App", "6.0.35"));
            Assert.Equal("core", ReadMarker(server, "Microsoft.NETCore.App", "6.0.36"));
            Assert.False(Directory.Exists(Path.Combine(server, "engine", "dotnet", "shared", "Microsoft.WindowsDesktop.App", "8.0.21")));
            Assert.False(Directory.Exists(Path.Combine(server, "engine", "dotnet", "shared", "Microsoft.WindowsDesktop.App", "6.0.16")));
            File.WriteAllText(Path.Combine(server, "engine", "dotnet", "shared", "Microsoft.WindowsDesktop.App", "6.0.35", "marker.txt"), "kept");
            var again = ServerFramework.Ensure(server, global);
            Assert.Equal("", again.Detail);
            Assert.Equal("kept", ReadMarker(server, "Microsoft.WindowsDesktop.App", "6.0.35"));

            var bare = PreparedServer(root, "bare");
            var missing = ServerFramework.Ensure(bare, Path.Combine(root, "no-desktop"));
            WriteFramework(Path.Combine(root, "no-desktop"), "Microsoft.NETCore.App", "6.0.36", "core-only");
            missing = ServerFramework.Ensure(bare, Path.Combine(root, "no-desktop"));
            Assert.False(missing.Ready);
            Assert.Contains("Microsoft.WindowsDesktop.App 6.0.35", missing.Detail);
            Assert.Contains(".NET 6 Desktop Runtime", missing.Detail);
            Assert.Equal("core-only", ReadMarker(bare, "Microsoft.NETCore.App", "6.0.36"));

            var broken = PreparedServer(root, "broken");
            File.WriteAllText(ConfigPath(broken), "{");
            var unreadable = ServerFramework.Ensure(broken, global);
            Assert.False(unreadable.Ready);
            Assert.Contains("could not be read", unreadable.Detail);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private static string NoReply(string address) =>
        ClientJoin.DescribeProbe(new ServerProbeResult(ServerReachability.NoReply, "No answer to the connection handshake."), address);

    private static string PreparedServer(string root, string name)
    {
        var server = Path.Combine(root, name);
        Directory.CreateDirectory(Path.Combine(server, "engine", "dotnet", "shared"));
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath(server))!);
        File.WriteAllText(ConfigPath(server), """
            {
              "runtimeOptions": {
                "rollForward": "LatestPatch",
                "frameworks": [
                  { "name": "Microsoft.NETCore.App", "version": "6.0.0" },
                  { "name": "Microsoft.WindowsDesktop.App", "version": "6.0.35" }
                ]
              }
            }
            """);
        return server;
    }

    private static string ConfigPath(string server) =>
        Path.Combine(server, "engine", "bin", "Win64_Shipping_Server", "TaleWorlds.Starter.DotNetCore.runtimeconfig.json");

    private static void WriteFramework(string global, string name, string version, string marker)
    {
        var directory = Path.Combine(global, name, version);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "marker.txt"), marker);
    }

    private static string ReadMarker(string server, string name, string version) =>
        File.ReadAllText(Path.Combine(server, "engine", "dotnet", "shared", name, version, "marker.txt"));
}
