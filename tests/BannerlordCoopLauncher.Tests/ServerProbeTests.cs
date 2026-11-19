using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using BannerlordCoopLauncher.Core.Network;
using LiteNetLib;
using LiteNetLib.Utils;
using TaomCoop.Core;

namespace BannerlordCoopLauncher.Tests;

// Check server against local stand ins for the Coop server, built on the same LiteNetLib 1.3.1.
public class ServerProbeTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(3);

    [Theory]
    [InlineData("")]
    [InlineData("secret")]
    public async Task A_coop_server_answers_and_turns_the_check_away_before_any_player_exists(string password)
    {
        using var server = new FakeCoopServer(password);
        var probe = await UdpServerProbe.ProbeAsync("127.0.0.1", server.Port, Timeout, CancellationToken.None);

        Assert.Equal(ServerReachability.Online, probe.Reachability);
        Assert.Contains("with code 1", probe.Detail);
        Assert.Equal(new[] { "malformed password data" }, server.Rejections.ToArray());
        Assert.Equal(0, server.Connected);
        Assert.Equal(0, server.PeerCount);
    }

    [Fact]
    public async Task A_restarting_coop_server_still_counts_as_online()
    {
        using var server = new FakeCoopServer("", admissionOpen: false);
        var probe = await UdpServerProbe.ProbeAsync("127.0.0.1", server.Port, Timeout, CancellationToken.None);

        Assert.Equal(ServerReachability.Online, probe.Reachability);
        Assert.Equal(0, server.Connected);
    }

    [Fact]
    public async Task An_old_server_that_accepts_everyone_is_online_and_the_check_leaves_at_once()
    {
        using var server = new FakeCoopServer("", acceptAll: true);
        var probe = await UdpServerProbe.ProbeAsync("127.0.0.1", server.Port, Timeout, CancellationToken.None);

        Assert.Equal(ServerReachability.Online, probe.Reachability);
        Assert.Equal(1, server.Connected);
        Assert.True(SpinWait.SpinUntil(() => server.Disconnected == 1 && server.PeerCount == 0, TimeSpan.FromSeconds(2)),
            "The accepted check stayed connected");
    }

    [Fact]
    public async Task The_launcher_wording_follows_the_real_answer()
    {
        using var server = new FakeCoopServer("");
        var probe = await UdpServerProbe.ProbeAsync("localhost", server.Port, Timeout, CancellationToken.None);

        Assert.Equal(ServerReachability.Online, probe.Reachability);
        Assert.Equal("The Coop server answered. You can click Connect.", JoinGuidance.DescribeProbe(probe, "127.0.0.1"));
        Assert.Equal(JoinGuidance.DescribeProbe(probe, "127.0.0.1"), ClientJoin.DescribeProbe(probe, "127.0.0.1"));
    }

    [Fact]
    public async Task A_port_that_takes_packets_but_never_answers_is_no_answer_and_gets_no_raw_status_text()
    {
        using var silent = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var received = new ConcurrentQueue<byte[]>();
        using var stop = new CancellationTokenSource();
        var reader = Task.Run(async () =>
        {
            while (!stop.IsCancellationRequested)
            {
                try { received.Enqueue((await silent.ReceiveAsync(stop.Token)).Buffer); }
                catch (OperationCanceledException) { return; }
                catch (SocketException) { }
            }
        });

        var started = DateTime.UtcNow;
        var probe = await UdpServerProbe.ProbeAsync("127.0.0.1", ((IPEndPoint)silent.Client.LocalEndPoint!).Port,
            TimeSpan.FromSeconds(1.5), CancellationToken.None);
        stop.Cancel();
        await reader;

        Assert.Equal(ServerReachability.NoReply, probe.Reachability);
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(4));
        Assert.NotEmpty(received);
        // The old check sent the ASCII text "status", which the Coop server never answers.
        Assert.DoesNotContain(received, packet => Encoding.ASCII.GetString(packet).Contains("status"));
        // A LiteNetLib connection request (packet property 5) is sent instead, repeated while waiting.
        Assert.True(received.Count(packet => (packet[0] & 0x1F) == 5) >= 2);
    }

    [Fact]
    public async Task Any_udp_reply_that_is_not_a_coop_handshake_is_not_online()
    {
        using var echo = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        using var stop = new CancellationTokenSource();
        var echoing = Task.Run(async () =>
        {
            while (!stop.IsCancellationRequested)
            {
                try
                {
                    var packet = await echo.ReceiveAsync(stop.Token);
                    await echo.SendAsync(Encoding.ASCII.GetBytes("pong"), packet.RemoteEndPoint);
                }
                catch (OperationCanceledException) { return; }
                catch (SocketException) { }
            }
        });

        var probe = await UdpServerProbe.ProbeAsync("127.0.0.1", ((IPEndPoint)echo.Client.LocalEndPoint!).Port,
            TimeSpan.FromSeconds(1.5), CancellationToken.None);
        stop.Cancel();
        await echoing;

        Assert.Equal(ServerReachability.NoReply, probe.Reachability);
    }

    [Fact]
    public async Task A_name_that_does_not_resolve_is_unreachable()
    {
        var probe = await UdpServerProbe.ProbeAsync("no-such-host.invalid", 4200, Timeout, CancellationToken.None);
        Assert.Equal(ServerReachability.Unreachable, probe.Reachability);
    }

    [Theory]
    [InlineData("", 4200)]
    [InlineData("127.0.0.1", 0)]
    [InlineData("127.0.0.1", 70000)]
    public async Task A_bad_address_or_port_is_unreachable_without_sending(string host, int port)
    {
        var probe = await UdpServerProbe.ProbeAsync(host, port, Timeout, CancellationToken.None);
        Assert.Equal(ServerReachability.Unreachable, probe.Reachability);
    }

    [Fact]
    public async Task Cancelling_stops_the_check()
    {
        using var silent = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => UdpServerProbe.ProbeAsync(
            "127.0.0.1", ((IPEndPoint)silent.Client.LocalEndPoint!).Port, TimeSpan.FromSeconds(10), cancel.Token));
    }

    [Fact]
    public void LiteNetLib_is_found_next_to_the_exe_even_when_deps_json_does_not_list_it()
    {
        var dir = Path.Combine(Path.GetTempPath(), "probe-loader-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var name = new System.Reflection.AssemblyName("LiteNetLib, Version=1.3.1.0");
            Assert.Null(LiteNetLibLoader.FindLocal(name, dir));
            File.WriteAllText(Path.Combine(dir, "LiteNetLib.dll"), "");
            Assert.Equal(Path.Combine(dir, "LiteNetLib.dll"), LiteNetLibLoader.FindLocal(name, dir));
            Assert.Null(LiteNetLibLoader.FindLocal(new System.Reflection.AssemblyName("Mono.Cecil"), dir));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }

        // The LiteNetLib types live outside ProbeAsync, so they load only after the resolver is set.
        var source = File.ReadAllText(Path.Combine(RepoRoot(), "src", "BannerlordCoopLauncher.Core", "Network", "UdpServerProbe.cs"));
        var probe = source[source.IndexOf("public static async Task<ServerProbeResult> ProbeAsync(", StringComparison.Ordinal)..source.IndexOf("private static async Task<ServerProbeResult> HandshakeAsync(", StringComparison.Ordinal)];
        Assert.Contains("LiteNetLibLoader.EnsureResolvable();", probe);
        Assert.DoesNotContain("NetManager", probe);
        Assert.DoesNotContain("Encoding.ASCII", source);
        // Manual mode, so a fast reject cannot arrive before Connect has recorded the peer.
        Assert.Contains("StartInManualMode(", source);
        Assert.Contains("ManualUpdate(", source);
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
    public void Every_answer_has_its_own_plain_wording()
    {
        string Say(ServerReachability reachability, string address) =>
            JoinGuidance.DescribeProbe(new ServerProbeResult(reachability, "detail"), address);

        Assert.Contains("You can click Connect", Say(ServerReachability.Online, "203.0.113.10"));
        Assert.Contains("same Coop version", Say(ServerReachability.OtherVersion, "203.0.113.10"));

        var publicQuiet = Say(ServerReachability.NoReply, "203.0.113.10");
        Assert.Contains("No Coop server answered", publicQuiet);
        Assert.Contains("may be stopped", publicQuiet);
        Assert.Contains("port forwarded", publicQuiet);
        Assert.Contains("ask a friend to check", publicQuiet);
        Assert.DoesNotContain("does not mean the server is stopped", publicQuiet);

        var homeQuiet = Say(ServerReachability.NoReply, "192.168.1.10");
        Assert.Contains("same home network", homeQuiet);
        Assert.Contains("firewall", homeQuiet);

        var loopQuiet = Say(ServerReachability.NoReply, "127.0.0.1");
        Assert.Contains("only the host's own PC", loopQuiet);
        Assert.DoesNotContain("port forwarded", loopQuiet);

        foreach (var reachability in Enum.GetValues<ServerReachability>())
            foreach (var address in new[] { "127.0.0.1", "192.168.1.10", "203.0.113.10", "host.example.com" })
            {
                var text = Say(reachability, address).Replace("127.0.0.1", "");
                Assert.DoesNotContain(":", text);
                Assert.Equal(text, ClientJoin.DescribeProbe(new ServerProbeResult(reachability, "detail"), address).Replace("127.0.0.1", ""));
            }
    }

    /// <summary>
    /// Mirrors CoopServer.OnConnectionRequest from the Coop repo (password read with GetString(128),
    /// malformed or wrong data rejected with ConnectionRejectCode.IncorrectPassword, ServerRestarting
    /// while the admission gate is closed, otherwise Accept). acceptAll mimics builds before passwords.
    /// </summary>
    private sealed class FakeCoopServer : IDisposable
    {
        private readonly NetManager manager;
        private int connected;
        private int disconnected;

        public FakeCoopServer(string password, bool admissionOpen = true, bool acceptAll = false)
        {
            var listener = new EventBasedNetListener();
            listener.ConnectionRequestEvent += request =>
            {
                if (acceptAll)
                {
                    request.Accept();
                    return;
                }

                string supplied;
                try
                {
                    supplied = request.Data.GetString(128);
                }
                catch (Exception)
                {
                    Rejections.Enqueue("malformed password data");
                    Reject(request, 1);
                    return;
                }

                if (!string.IsNullOrEmpty(password) && !string.Equals(password, supplied, StringComparison.Ordinal))
                {
                    Rejections.Enqueue("incorrect password");
                    Reject(request, 1);
                    return;
                }

                if (!admissionOpen)
                {
                    Rejections.Enqueue("the server is restarting");
                    Reject(request, 2);
                    return;
                }

                request.Accept();
            };
            listener.PeerConnectedEvent += _ => Interlocked.Increment(ref connected);
            listener.PeerDisconnectedEvent += (_, _) => Interlocked.Increment(ref disconnected);
            manager = new NetManager(listener) { UnsyncedEvents = true, ChannelsCount = 2 };
            Assert.True(manager.Start(IPAddress.Loopback, IPAddress.IPv6Loopback, 0));
        }

        public ConcurrentQueue<string> Rejections { get; } = new();
        public int Port => manager.LocalPort;
        public int Connected => Volatile.Read(ref connected);
        public int Disconnected => Volatile.Read(ref disconnected);
        public int PeerCount => manager.ConnectedPeersCount;

        private static void Reject(ConnectionRequest request, byte code)
        {
            var reason = new NetDataWriter();
            reason.Put(code);
            request.Reject(reason);
        }

        public void Dispose() => manager.Stop();
    }
}
