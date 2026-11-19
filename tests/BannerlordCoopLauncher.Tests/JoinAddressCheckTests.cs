using System.Net.NetworkInformation;
using BannerlordCoopLauncher.Core.Joining;
using BannerlordCoopLauncher.Core.Network;

namespace BannerlordCoopLauncher.Tests;

// Connect in Direct mode: a Discord player typed 172.19.0.1, the gateway of their own VPN adapter, and sat
// for an hour on Coop's "Contacting the server" screen. These check the address and answer checks.
public class JoinAddressCheckTests
{
    // A PC like that player's: Wi Fi with the home router, a commercial VPN, and WSL's virtual switch.
    private static readonly IReadOnlyList<PcAdapter> Adapters = new[]
    {
        new PcAdapter("Wi-Fi", "Intel(R) Wi-Fi 6 AX201 160MHz", false, new[] { "192.168.1.20" }, new[] { "192.168.1.1" }),
        new PcAdapter("Proton VPN", "WireGuard Tunnel", true, new[] { "172.19.0.2" }, new[] { "172.19.0.1" }),
        new PcAdapter("vEthernet (WSL)", "Hyper-V Virtual Ethernet Adapter", true, new[] { "172.24.80.1" }, Array.Empty<string>()),
    };

    private static OwnAddressMatch? Find(string address, bool localServer = false) =>
        OwnAddressCheck.Find(address, new[] { address }, Adapters, localServer);

    [Fact]
    public void Own_adapter_gateway_is_flagged_as_this_pc()
    {
        var match = Find("172.19.0.1");
        Assert.NotNull(match);
        Assert.Equal(OwnAddressKind.AdapterGateway, match!.Kind);
        Assert.Equal("Proton VPN", match.Adapter);
        Assert.Equal("172.19.0.1 belongs to your VPN or a network adapter on this PC, not to your host. Click Use Steam lobby and type your host's Steam name.", match.Warning);
    }

    [Theory]
    [InlineData("172.19.0.2")]
    [InlineData("172.24.80.1")]
    [InlineData("192.168.1.20")]
    public void Own_adapter_address_is_flagged(string address) =>
        Assert.Equal(OwnAddressKind.ThisPc, Find(address)?.Kind);

    [Fact]
    public void Home_router_is_flagged_as_the_router()
    {
        var match = Find("192.168.1.1");
        Assert.Equal(OwnAddressKind.Router, match?.Kind);
        Assert.Equal("192.168.1.1 is your router, not your host. Click Use Steam lobby and type your host's Steam name.", match!.Warning);
    }

    [Fact]
    public void Loopback_and_own_addresses_are_allowed_while_a_coop_server_runs_here()
    {
        Assert.Null(Find("127.0.0.1", localServer: true));
        Assert.Null(Find("192.168.1.20", localServer: true));
        Assert.Null(Find("172.24.80.1", localServer: true));
        // A gateway is never this PC's server, even when one runs here.
        Assert.Equal(OwnAddressKind.AdapterGateway, Find("172.19.0.1", localServer: true)?.Kind);

        var alone = Find("127.0.0.1");
        Assert.Equal(OwnAddressKind.Loopback, alone?.Kind);
        Assert.Equal("127.0.0.1 is this PC, and no Coop server is running on it. Click Use Steam lobby and type your host's Steam name.", alone!.Warning);
    }

    [Theory]
    [InlineData("81.2.69.160")]
    [InlineData("192.168.1.50")]
    [InlineData("26.10.20.30")]
    [InlineData("172.19.0.3")]
    public void Remote_and_lan_hosts_are_not_flagged(string address) => Assert.Null(Find(address));

    [Theory]
    [InlineData("Wi-Fi", "Intel(R) Wi-Fi 6 AX201 160MHz", NetworkInterfaceType.Wireless80211, false)]
    [InlineData("Ethernet", "Realtek PCIe GbE Family Controller", NetworkInterfaceType.Ethernet, false)]
    [InlineData("Local Area Connection", "TAP-Windows Adapter V9", NetworkInterfaceType.Ethernet, true)]
    [InlineData("ProtonVPN", "WireGuard Tunnel", NetworkInterfaceType.Unknown, true)]
    [InlineData("vEthernet (WSL)", "Hyper-V Virtual Ethernet Adapter", NetworkInterfaceType.Ethernet, true)]
    [InlineData("vEthernet (Default Switch)", "Hyper-V Virtual Ethernet Adapter", NetworkInterfaceType.Ethernet, true)]
    [InlineData("Ethernet 3", "Docker Desktop NAT", NetworkInterfaceType.Ethernet, true)]
    [InlineData("VPN", "WAN Miniport (IKEv2)", NetworkInterfaceType.Ppp, true)]
    public void Vpn_and_virtual_adapters_are_told_from_the_home_network(string name, string description, NetworkInterfaceType type, bool virtualAdapter) =>
        Assert.Equal(virtualAdapter, OwnAddressCheck.LooksVirtual(name, description, type));

    private static Func<string, int, TimeSpan, CancellationToken, Task<ServerProbeResult>> Probe(ServerReachability reachability, List<string>? asked = null) =>
        (host, port, timeout, _) =>
        {
            asked?.Add(host + ":" + port + " " + timeout.TotalSeconds);
            return Task.FromResult(new ServerProbeResult(reachability, "test"));
        };

    private static Func<string, CancellationToken, Task<IReadOnlyList<string>>> Dns(params string[] addresses) =>
        (_, _) => Task.FromResult<IReadOnlyList<string>>(addresses);

    private static Task<JoinCheckResult> Check(string host, ServerReachability reachability, bool localServer = false, List<string>? asked = null) =>
        JoinPreflight.CheckAsync(host, 4200, localServer, CancellationToken.None, () => Adapters, JoinPreflight.ResolveAsync, Probe(reachability, asked));

    [Fact]
    public async Task Dns_names_are_resolved_once_and_the_probe_uses_that_address()
    {
        var lookups = 0;
        var asked = new List<string>();
        var check = await JoinPreflight.CheckAsync("home.example.com", 4200, false, CancellationToken.None,
            () => Adapters, (_, _) =>
            {
                lookups++;
                return Task.FromResult<IReadOnlyList<string>>(new[] { "172.19.0.1" });
            }, Probe(ServerReachability.NoReply, asked));
        Assert.Equal(JoinCheckOutcome.OwnAddress, check.Outcome);
        Assert.Equal("172.19.0.1", check.Own!.Address);
        Assert.StartsWith("home.example.com belongs to your VPN", check.Message);
        Assert.Equal("This address is on your own PC", check.Title);
        Assert.Equal(1, lookups);
        Assert.Equal("172.19.0.1:4200 5", Assert.Single(asked));
    }

    [Fact]
    public async Task A_name_that_does_not_resolve_gives_the_no_answer_prompt_without_a_probe()
    {
        var asked = new List<string>();
        var check = await JoinPreflight.CheckAsync("nowhere.example.com", 4200, false, CancellationToken.None,
            () => Adapters, Dns(), Probe(ServerReachability.Online, asked));
        Assert.Equal(JoinCheckOutcome.NoAnswer, check.Outcome);
        Assert.Empty(asked);
    }

    [Fact]
    public async Task No_answer_leads_to_the_prompt_with_the_address_and_port()
    {
        var asked = new List<string>();
        var check = await Check(" 81.2.69.160 ", ServerReachability.NoReply, asked: asked);
        Assert.Equal(JoinCheckOutcome.NoAnswer, check.Outcome);
        Assert.Equal("The server did not answer", check.Title);
        Assert.Equal("Nobody answered at 81.2.69.160:4200. The server may be off, or your host's internet may not allow direct joins. Steam lobby usually works. Ask your host if unsure.", check.Message);
        // A 5 second wait, so a slow or far server is not taken for a silent one.
        Assert.Equal("81.2.69.160:4200 5", Assert.Single(asked));
        Assert.Equal(JoinCheckOutcome.NoAnswer, (await Check("81.2.69.160", ServerReachability.Unreachable)).Outcome);
    }

    [Theory]
    [InlineData(ServerReachability.Online)]
    [InlineData(ServerReachability.OtherVersion)]
    public async Task An_answer_connects_straight_away(ServerReachability reachability)
    {
        Assert.Equal(JoinCheckOutcome.Connect, (await Check("81.2.69.160", reachability)).Outcome);
        // Whatever answered the Coop handshake is a Coop server, even on an address of this PC.
        Assert.Equal(JoinCheckOutcome.Connect, (await Check("192.168.1.20", reachability)).Outcome);
    }

    private static Func<JoinCheckResult, JoinCheckChoice> Ask(List<JoinCheckResult> shown, JoinCheckChoice pick) => result =>
    {
        shown.Add(result);
        return pick;
    };

    [Fact]
    public async Task Probe_answer_on_a_gateway_address_starts_the_game_without_a_prompt()
    {
        var shown = new List<JoinCheckResult>();
        var choice = await JoinPreflight.DecideAsync("172.19.0.1", 4200, () => Check("172.19.0.1", ServerReachability.Online), Ask(shown, JoinCheckChoice.Cancel));
        Assert.Equal(JoinCheckChoice.Connect, choice);
        Assert.Empty(shown);
    }

    [Fact]
    public async Task Own_address_prompt_still_lets_connect_anyway_start_the_game()
    {
        var shown = new List<JoinCheckResult>();
        var choice = await JoinPreflight.DecideAsync("172.19.0.1", 4200, () => Check("172.19.0.1", ServerReachability.NoReply), Ask(shown, JoinCheckChoice.Connect));
        Assert.Equal(JoinCheckChoice.Connect, choice);
        Assert.Equal(JoinCheckOutcome.OwnAddress, Assert.Single(shown).Outcome);
    }

    [Fact]
    public async Task An_exception_in_the_check_gives_the_no_answer_prompt()
    {
        var shown = new List<JoinCheckResult>();
        var choice = await JoinPreflight.DecideAsync("81.2.69.160", 4200, () => throw new InvalidOperationException("broken"), Ask(shown, JoinCheckChoice.SteamLobby));
        Assert.Equal(JoinCheckChoice.SteamLobby, choice);
        Assert.Equal(JoinPreflight.NoAnswer("81.2.69.160", 4200), Assert.Single(shown).Message);

        // Inside the check too: a broken adapter list or probe never escapes as an exception.
        var adapters = await JoinPreflight.CheckAsync("81.2.69.160", 4200, false, CancellationToken.None,
            () => throw new NetworkInformationException(), JoinPreflight.ResolveAsync, Probe(ServerReachability.Online));
        Assert.Equal(JoinCheckOutcome.NoAnswer, adapters.Outcome);
        var probe = await JoinPreflight.CheckAsync("81.2.69.160", 4200, false, CancellationToken.None,
            () => Adapters, JoinPreflight.ResolveAsync, (_, _, _, _) => throw new System.Net.Sockets.SocketException());
        Assert.Equal(JoinCheckOutcome.NoAnswer, probe.Outcome);
    }

    [Fact]
    public async Task A_cancelled_check_starts_nothing_and_asks_nothing()
    {
        var shown = new List<JoinCheckResult>();
        using var stop = new CancellationTokenSource();
        stop.Cancel();
        var choice = await JoinPreflight.DecideAsync("81.2.69.160", 4200,
            () => JoinPreflight.CheckAsync("81.2.69.160", 4200, false, stop.Token, () => Adapters, JoinPreflight.ResolveAsync,
                (_, _, _, token) => Task.FromCanceled<ServerProbeResult>(token)),
            Ask(shown, JoinCheckChoice.Connect));
        Assert.Equal(JoinCheckChoice.Cancel, choice);
        Assert.Empty(shown);
    }

    [Fact]
    public async Task Hosting_player_joining_their_own_server_gets_no_prompt()
    {
        var check = await Check("127.0.0.1", ServerReachability.Online, localServer: true);
        Assert.Equal(JoinCheckOutcome.Connect, check.Outcome);
        Assert.Null(check.Own);
    }

    [Fact]
    public async Task Literal_ip_resolves_to_itself_without_dns()
    {
        Assert.Equal(new[] { "172.19.0.1" }, await JoinPreflight.ResolveAsync("172.19.0.1", CancellationToken.None));
    }

    private static JoinFacts CgnatHost(bool steam) => new()
    {
        IsHost = true,
        Port = 4200,
        SteamLobbyOn = steam,
        SteamName = "Coodsz",
        Adapters = new[] { new AdapterAddress("Wi-Fi", "", "192.168.1.20", AdapterKind.Lan, true) },
        Router = new PortOpenResult(PortOpenOutcome.SharedAddress, 4200, "UPnP", "100.70.1.1"),
        ClientTool = "Coodsz TAOM Client Launcher",
        SteamOption = "Advertise through Steam"
    };

    [Fact]
    public void Cgnat_invite_puts_steam_lobby_first()
    {
        var lines = JoinPlan.InviteLines(CgnatHost(steam: true));
        Assert.Equal("Steam lobby. Pick Steam lobby and type Coodsz.", lines[2]);
        Assert.Equal("My internet does not allow direct joins, so click Steam lobby, not Direct.", lines[3]);
        Assert.DoesNotContain(lines, line => line.StartsWith("Internet.", StringComparison.Ordinal) || line.Contains("100.70.1.1", StringComparison.Ordinal));
        Assert.False(JoinPlan.NeedsSteamLobby(CgnatHost(steam: true)));
    }

    [Fact]
    public void Cgnat_invite_never_promises_steam_lobby_while_it_is_off()
    {
        var off = CgnatHost(steam: false);
        Assert.True(JoinPlan.NeedsSteamLobby(off));
        Assert.DoesNotContain(JoinPlan.InviteLines(off), line => line.Contains("Steam lobby", StringComparison.Ordinal));
        Assert.Equal("Invite copied. Steam lobby is off, so friends outside your home network cannot join yet.", JoinPlan.InviteCopied(off));
        Assert.Equal("Invite copied. Paste it in Discord. The password is not in it.", JoinPlan.InviteCopied(CgnatHost(steam: true)));
        // No shared address: nothing to turn on.
        Assert.False(JoinPlan.NeedsSteamLobby(new JoinFacts { IsHost = true, SteamLobbyOn = false }));
    }
}

// The prompts follow the tool's language with the address and port filled in. Translator.Current is
// process wide, so this runs in the collection that never runs next to other tests.
[Collection(TranslatorLanguage.Name)]
public sealed class JoinAddressCheckLanguageTests
{
    [Fact]
    public void Prompts_are_translated_with_the_address_and_port_kept()
    {
        var before = BannerlordCoopLauncher.Core.Localization.Translator.Current;
        try
        {
            BannerlordCoopLauncher.Core.Localization.Translator.Use("de");
            var own = new OwnAddressMatch("172.19.0.1", "172.19.0.1", OwnAddressKind.AdapterGateway, "VPN");
            Assert.StartsWith("172.19.0.1 gehört zu deinem VPN", BannerlordCoopLauncher.Core.Localization.Translator.T(own.Warning));
            Assert.StartsWith("Niemand hat unter 81.2.69.160:4200 geantwortet.", BannerlordCoopLauncher.Core.Localization.Translator.T(JoinPreflight.NoAnswer("81.2.69.160", 4200)));
            BannerlordCoopLauncher.Core.Localization.Translator.Use("tr");
            Assert.StartsWith("81.2.69.160:4200 adresinde", BannerlordCoopLauncher.Core.Localization.Translator.T(JoinPreflight.NoAnswer("81.2.69.160", 4200)));
            Assert.StartsWith("192.168.1.1 senin modemin", BannerlordCoopLauncher.Core.Localization.Translator.T(new OwnAddressMatch("192.168.1.1", "192.168.1.1", OwnAddressKind.Router, "Wi-Fi").Warning));
        }
        finally
        {
            BannerlordCoopLauncher.Core.Localization.Translator.Use(before);
        }
    }
}
