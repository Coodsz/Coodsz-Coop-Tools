using System.Net;
using BannerlordCoopLauncher.Core.Joining;
using BannerlordCoopLauncher.Core.Steam;

namespace BannerlordCoopLauncher.Tests;

public class EasyJoiningTests
{
    private const string SsdpAnswer =
        "HTTP/1.1 200 OK\r\nCACHE-CONTROL: max-age=120\r\nST: urn:schemas-upnp-org:device:InternetGatewayDevice:1\r\n" +
        "USN: uuid:abc::urn:schemas-upnp-org:device:InternetGatewayDevice:1\r\nEXT:\r\nSERVER: Linux UPnP/1.1 MiniUPnPd/2.2\r\n" +
        "Location: http://192.168.1.1:5000/rootDesc.xml\r\n\r\n";

    // A MiniUPnPd style description: WANIPConnection:1 nested two devices deep, relative control URL.
    private const string Description = """
        <?xml version="1.0"?>
        <root xmlns="urn:schemas-upnp-org:device-1-0">
          <specVersion><major>1</major><minor>1</minor></specVersion>
          <device>
            <deviceType>urn:schemas-upnp-org:device:InternetGatewayDevice:1</deviceType>
            <serviceList>
              <service><serviceType>urn:schemas-upnp-org:service:Layer3Forwarding:1</serviceType><controlURL>/ctl/L3F</controlURL></service>
            </serviceList>
            <deviceList><device>
              <deviceType>urn:schemas-upnp-org:device:WANDevice:1</deviceType>
              <deviceList><device>
                <deviceType>urn:schemas-upnp-org:device:WANConnectionDevice:1</deviceType>
                <serviceList>
                  <service><serviceType>urn:schemas-upnp-org:service:WANPPPConnection:1</serviceType><controlURL>/ctl/PPPConn</controlURL></service>
                  <service><serviceType>urn:schemas-upnp-org:service:WANIPConnection:1</serviceType><controlURL>/ctl/IPConn</controlURL></service>
                </serviceList>
              </device></deviceList>
            </device></deviceList>
          </device>
        </root>
        """;

    private static string Ok(string action, string inner = "") =>
        "<?xml version=\"1.0\"?><s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\"><s:Body>" +
        "<u:" + action + "Response xmlns:u=\"urn:schemas-upnp-org:service:WANIPConnection:1\">" + inner + "</u:" + action + "Response></s:Body></s:Envelope>";

    private static string Fault(int code, string text) =>
        "<?xml version=\"1.0\"?><s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\"><s:Body><s:Fault>" +
        "<faultcode>s:Client</faultcode><faultstring>UPnPError</faultstring><detail><UPnPError xmlns=\"urn:schemas-upnp-org:control-1-0\">" +
        "<errorCode>" + code + "</errorCode><errorDescription>" + text + "</errorDescription></UPnPError></detail></s:Fault></s:Body></s:Envelope>";

    private sealed class FakeRouter : IRouterNetwork
    {
        public List<string> Ssdp { get; } = new() { SsdpAnswer };
        public string? DescriptionXml { get; set; } = Description;
        public string External { get; set; } = "203.0.113.7";
        public string? Mapped { get; set; }
        public Func<string, string>? OnAdd { get; set; }
        public Func<byte[], byte[]?>? NatPmp { get; set; }
        public List<string> Calls { get; } = new();
        public List<string> Bodies { get; } = new();
        public List<IPAddress> GatewayList { get; } = new() { IPAddress.Parse("192.168.1.1") };

        public Task<IReadOnlyList<string>> SearchAsync(IReadOnlyList<string> requests, TimeSpan timeout, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<string>>(Ssdp);

        public Task<string?> GetAsync(Uri url, CancellationToken cancellationToken) => Task.FromResult(DescriptionXml);

        public Task<string?> PostSoapAsync(Uri url, string soapAction, string body, CancellationToken cancellationToken)
        {
            var action = soapAction.Trim('"').Split('#')[1];
            Calls.Add(url.AbsolutePath + " " + action);
            Bodies.Add(body);
            if (url.AbsolutePath == "/ctl/PPPConn")
                return Task.FromResult<string?>(Fault(401, "Invalid Action"));
            return Task.FromResult<string?>(action switch
            {
                "GetExternalIPAddress" => Ok(action, "<NewExternalIPAddress>" + External + "</NewExternalIPAddress>"),
                "GetSpecificPortMappingEntry" => Mapped == null
                    ? Fault(714, "NoSuchEntryInArray")
                    : Ok(action, "<NewInternalPort>4200</NewInternalPort><NewInternalClient>" + Mapped + "</NewInternalClient><NewEnabled>1</NewEnabled><NewPortMappingDescription>Porthole</NewPortMappingDescription><NewLeaseDuration>0</NewLeaseDuration>"),
                "AddPortMapping" => OnAdd?.Invoke(body) ?? Ok(action),
                "DeletePortMapping" => Ok(action),
                _ => Fault(401, "Invalid Action")
            });
        }

        public string? LocalAddressFor(IPAddress router) => "192.168.1.20";

        public IReadOnlyList<IPAddress> Gateways() => GatewayList;

        public Task<byte[]?> NatPmpAsync(IPAddress gateway, byte[] request, TimeSpan timeout, CancellationToken cancellationToken) =>
            Task.FromResult(NatPmp?.Invoke(request));
    }

    [Fact]
    public void Ssdp_search_and_answers_are_read()
    {
        var request = Upnp.SearchRequest(Upnp.SearchTargets[0]);
        Assert.StartsWith("M-SEARCH * HTTP/1.1\r\nHOST: 239.255.255.250:1900\r\n", request);
        Assert.Contains("MAN: \"ssdp:discover\"\r\n", request);
        Assert.EndsWith("ST: urn:schemas-upnp-org:device:InternetGatewayDevice:1\r\n\r\n", request);
        Assert.Equal(new Uri("http://192.168.1.1:5000/rootDesc.xml"), Upnp.ParseLocation(SsdpAnswer));
        Assert.Null(Upnp.ParseLocation("NOTIFY * HTTP/1.1\r\nLOCATION: http://x/\r\n\r\n"));
        Assert.Null(Upnp.ParseLocation("HTTP/1.1 200 OK\r\nLOCATION: https://192.168.1.1/desc.xml\r\n\r\n"));
    }

    [Fact]
    public void Device_description_gives_the_ip_connection_first_with_absolute_urls()
    {
        var services = Upnp.ParseServices(Description, new Uri("http://192.168.1.1:5000/rootDesc.xml"));
        Assert.Equal(2, services.Count);
        Assert.Equal("urn:schemas-upnp-org:service:WANIPConnection:1", services[0].ServiceType);
        Assert.Equal(new Uri("http://192.168.1.1:5000/ctl/IPConn"), services[0].ControlUrl);
        Assert.Contains("WANPPPConnection", services[1].ServiceType);

        var withBase = Description.Replace("<specVersion>", "<URLBase>http://10.0.0.1:49000/</URLBase><specVersion>");
        Assert.Equal(new Uri("http://10.0.0.1:49000/ctl/IPConn"), Upnp.ParseServices(withBase, new Uri("http://10.0.0.1:5000/x.xml"))[0].ControlUrl);
        Assert.Empty(Upnp.ParseServices("not xml", new Uri("http://10.0.0.1/")));
    }

    [Fact]
    public void Soap_calls_carry_the_right_arguments_and_faults_are_read()
    {
        var body = Upnp.AddPortMapping("urn:schemas-upnp-org:service:WANIPConnection:1", 4200, "192.168.1.20", 4200, "Coodsz <Coop> server", 3600);
        Assert.Contains("<u:AddPortMapping xmlns:u=\"urn:schemas-upnp-org:service:WANIPConnection:1\">", body);
        Assert.Contains("<NewRemoteHost></NewRemoteHost><NewExternalPort>4200</NewExternalPort><NewProtocol>UDP</NewProtocol><NewInternalPort>4200</NewInternalPort><NewInternalClient>192.168.1.20</NewInternalClient><NewEnabled>1</NewEnabled>", body);
        Assert.Contains("<NewPortMappingDescription>Coodsz &lt;Coop&gt; server</NewPortMappingDescription><NewLeaseDuration>3600</NewLeaseDuration>", body);
        Assert.Contains("<NewProtocol>UDP</NewProtocol>", Upnp.DeletePortMapping("urn:x", 4200));

        var fault = Upnp.ParseFault(Fault(718, "ConflictInMappingEntry"));
        Assert.Equal(new UpnpFault(718, "ConflictInMappingEntry"), fault);
        Assert.Null(Upnp.ParseFault(Ok("AddPortMapping")));
        Assert.Equal("203.0.113.7", Upnp.ReadValue(Ok("GetExternalIPAddress", "<NewExternalIPAddress>203.0.113.7</NewExternalIPAddress>"), "NewExternalIPAddress"));
        Assert.Equal("\"urn:x:1#AddPortMapping\"", Upnp.SoapAction(new UpnpService(new Uri("http://a/"), "urn:x:1"), "AddPortMapping"));
    }

    [Fact]
    public async Task Opens_the_port_on_a_upnp_router_and_closes_it()
    {
        var router = new FakeRouter();
        var mapper = new RouterPortMapper(router);
        var result = await mapper.OpenAsync(4200, RouterPortMapper.DefaultDescription, TimeSpan.FromHours(1), CancellationToken.None);

        Assert.Equal(PortOpenOutcome.Opened, result.Outcome);
        Assert.Equal("203.0.113.7", result.ExternalAddress);
        Assert.Equal("192.168.1.20", result.LocalAddress);
        Assert.Equal("UPnP", result.Method);
        Assert.True(mapper.HasMapping);
        Assert.Equal(3600, mapper.LeaseSeconds);
        Assert.Contains("/ctl/IPConn AddPortMapping", router.Calls);
        Assert.DoesNotContain(router.Calls, call => call.StartsWith("/ctl/PPPConn A", StringComparison.Ordinal));
        Assert.Contains("Coodsz Coop server", router.Bodies.Last());
        Assert.Contains("Port 4200 is open on your router", result.Status);

        Assert.True(await mapper.RenewAsync(CancellationToken.None));
        await mapper.CloseAsync(CancellationToken.None);
        Assert.Equal("/ctl/IPConn DeletePortMapping", router.Calls.Last());
        Assert.False(mapper.HasMapping);
        var calls = router.Calls.Count;
        await mapper.CloseAsync(CancellationToken.None);
        Assert.Equal(calls, router.Calls.Count);
    }

    [Fact]
    public async Task Router_that_only_keeps_permanent_mappings_gets_lease_zero()
    {
        var router = new FakeRouter();
        router.OnAdd = body => body.Contains("<NewLeaseDuration>3600<") ? Fault(725, "OnlyPermanentLeasesSupported") : Ok("AddPortMapping");
        var mapper = new RouterPortMapper(router);
        var result = await mapper.OpenAsync(4200, "Coodsz Coop server", TimeSpan.FromHours(1), CancellationToken.None);
        Assert.Equal(PortOpenOutcome.Opened, result.Outcome);
        Assert.Equal(0, mapper.LeaseSeconds);
        Assert.Equal(2, router.Calls.Count(call => call.EndsWith("AddPortMapping", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Port_mapped_to_another_pc_is_left_alone()
    {
        var router = new FakeRouter { Mapped = "192.168.1.50" };
        var mapper = new RouterPortMapper(router);
        var result = await mapper.OpenAsync(4200, "Coodsz Coop server", TimeSpan.FromHours(1), CancellationToken.None);
        Assert.Equal(PortOpenOutcome.TakenByOtherPc, result.Outcome);
        Assert.Equal("192.168.1.50", result.OtherPc);
        Assert.DoesNotContain(router.Calls, call => call.EndsWith("AddPortMapping", StringComparison.Ordinal) || call.EndsWith("DeletePortMapping", StringComparison.Ordinal));
        Assert.False(mapper.HasMapping);
        Assert.Contains("already open for another PC (192.168.1.50)", result.Status);

        // Our own old mapping (same PC) is simply renewed.
        router.Mapped = "192.168.1.20";
        Assert.Equal(PortOpenOutcome.Opened, (await new RouterPortMapper(router).OpenAsync(4200, "x", TimeSpan.FromHours(1), CancellationToken.None)).Outcome);

        var conflict = new FakeRouter { OnAdd = _ => Fault(718, "ConflictInMappingEntry") };
        Assert.Equal(PortOpenOutcome.TakenByOtherPc, (await new RouterPortMapper(conflict).OpenAsync(4200, "x", TimeSpan.FromHours(1), CancellationToken.None)).Outcome);
    }

    [Fact]
    public async Task Router_refusal_and_no_router_are_told_apart()
    {
        var refusing = new FakeRouter { OnAdd = _ => Fault(606, "Action not authorized") };
        var refused = await new RouterPortMapper(refusing).OpenAsync(4200, "x", TimeSpan.FromHours(1), CancellationToken.None);
        Assert.Equal(PortOpenOutcome.RouterSaidNo, refused.Outcome);
        Assert.Equal(606, refused.RouterCode);
        Assert.Contains("Router code 606", refused.LogLine);

        var silent = new FakeRouter();
        silent.Ssdp.Clear();
        var none = await new RouterPortMapper(silent).OpenAsync(4200, "x", TimeSpan.FromHours(1), CancellationToken.None);
        Assert.Equal(PortOpenOutcome.NoRouter, none.Outcome);
        Assert.Contains("No router answered", none.Status);
    }

    [Theory]
    [InlineData("100.72.10.4")]
    [InlineData("10.20.0.5")]
    [InlineData("192.168.0.2")]
    [InlineData("172.20.1.1")]
    public async Task Shared_internet_address_stops_before_mapping(string external)
    {
        var router = new FakeRouter { External = external };
        var result = await new RouterPortMapper(router).OpenAsync(4200, "x", TimeSpan.FromHours(1), CancellationToken.None);
        Assert.Equal(PortOpenOutcome.SharedAddress, result.Outcome);
        Assert.Contains("internet provider shares your address", result.Status);
        Assert.DoesNotContain(router.Calls, call => call.EndsWith("AddPortMapping", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Nat_pmp_is_used_when_no_upnp_router_answers()
    {
        var router = new FakeRouter();
        router.Ssdp.Clear();
        router.NatPmp = request => request[1] switch
        {
            0 => new byte[] { 0, 128, 0, 0, 0, 0, 0, 1, 203, 0, 113, 9 },
            _ => new byte[] { 0, 129, 0, 0, 0, 0, 0, 1, request[4], request[5], request[6], request[7], request[8], request[9], request[10], request[11] }
        };
        var mapper = new RouterPortMapper(router);
        var result = await mapper.OpenAsync(4200, "x", TimeSpan.FromHours(1), CancellationToken.None);
        Assert.Equal(PortOpenOutcome.Opened, result.Outcome);
        Assert.Equal("NAT-PMP", result.Method);
        Assert.Equal("203.0.113.9", result.ExternalAddress);
        Assert.Equal(3600, mapper.LeaseSeconds);

        var request = NatPmp.MapUdpRequest(4200, 4200, 3600);
        Assert.Equal(new byte[] { 0, 1, 0, 0, 0x10, 0x68, 0x10, 0x68, 0, 0, 0x0E, 0x10 }, request);
        Assert.Null(NatPmp.ParseMapping(new byte[] { 0, 128, 0, 0 }));
    }

    [Theory]
    [InlineData("8.8.8.8", Ipv4Kind.Public)]
    [InlineData("100.64.0.1", Ipv4Kind.CarrierShared)]
    [InlineData("100.127.255.254", Ipv4Kind.CarrierShared)]
    [InlineData("100.128.0.1", Ipv4Kind.Public)]
    [InlineData("172.15.0.1", Ipv4Kind.Public)]
    [InlineData("172.31.0.1", Ipv4Kind.Private)]
    [InlineData("127.0.0.1", Ipv4Kind.Loopback)]
    [InlineData("169.254.3.3", Ipv4Kind.LinkLocal)]
    [InlineData("0.0.0.0", Ipv4Kind.Unspecified)]
    [InlineData("1.2.3", Ipv4Kind.Invalid)]
    [InlineData("1.2.3.256", Ipv4Kind.Invalid)]
    public void Addresses_are_classified(string address, Ipv4Kind kind) => Assert.Equal(kind, Ipv4.Classify(address));

    [Fact]
    public void Cgnat_and_double_nat_count_as_shared()
    {
        Assert.True(Ipv4.IsSharedAddress("100.100.1.1"));
        Assert.True(Ipv4.IsSharedAddress("192.168.1.1"));
        Assert.False(Ipv4.IsSharedAddress("81.2.69.160"));
        Assert.False(Ipv4.IsSharedAddress(""));
    }

    [Theory]
    [InlineData("Radmin VPN", "Famatech Radmin VPN Ethernet Adapter", "26.12.34.56", true, AdapterKind.Radmin)]
    [InlineData("Ethernet 3", "LogMeIn Hamachi Virtual Ethernet Adapter", "25.1.2.3", false, AdapterKind.Hamachi)]
    [InlineData("ZeroTier One [8056c2e21c000001]", "ZeroTier Virtual Port", "10.147.17.5", false, AdapterKind.ZeroTier)]
    [InlineData("Tailscale", "Tailscale Tunnel", "100.101.5.6", false, AdapterKind.Tailscale)]
    [InlineData("Ethernet 4", "TAP adapter", "26.0.0.9", false, AdapterKind.Radmin)]
    [InlineData("Wi-Fi", "Intel(R) Wi-Fi 6 AX201 160MHz", "192.168.1.20", true, AdapterKind.Lan)]
    [InlineData("Ethernet", "Realtek PCIe GbE Family Controller", "10.0.0.7", true, AdapterKind.Lan)]
    public void Adapters_are_classified(string name, string description, string address, bool gateway, AdapterKind kind) =>
        Assert.Equal(kind, NetworkAdapters.Classify(name, description, address, gateway));

    [Theory]
    [InlineData("vEthernet (WSL)", "Hyper-V Virtual Ethernet Adapter", "172.24.80.1")]
    [InlineData("VirtualBox Host-Only Network", "VirtualBox Host-Only Ethernet Adapter", "192.168.56.1")]
    [InlineData("Wi-Fi", "Intel(R) Wi-Fi 6", "169.254.10.10")]
    [InlineData("Ethernet", "Realtek", "81.2.69.160")]
    public void Useless_adapters_are_skipped(string name, string description, string address) =>
        Assert.Null(NetworkAdapters.Classify(name, description, address, true));

    [Fact]
    public void Virtual_lans_come_before_the_home_network()
    {
        var ordered = NetworkAdapters.Order(new[]
        {
            new AdapterAddress("Wi-Fi", "", "192.168.1.20", AdapterKind.Lan, true),
            new AdapterAddress("Hamachi", "", "25.1.1.1", AdapterKind.Hamachi, false),
            new AdapterAddress("Radmin VPN", "", "26.1.1.1", AdapterKind.Radmin, false),
            new AdapterAddress("Wi-Fi again", "", "192.168.1.20", AdapterKind.Lan, true),
        });
        Assert.Equal(new[] { "26.1.1.1", "25.1.1.1", "192.168.1.20" }, ordered.Select(a => a.Address));
    }

    [Theory]
    [InlineData("*", 4200, true)]
    [InlineData("", 4200, true)]
    [InlineData("4200", 4200, true)]
    [InlineData("4199", 4200, false)]
    [InlineData("80,443,4200", 4200, true)]
    [InlineData("4000-4300", 4200, true)]
    [InlineData("4201-4300", 4200, false)]
    [InlineData("RPC", 4200, false)]
    public void Firewall_ports_match(string ports, int port, bool match) => Assert.Equal(match, FirewallRules.PortMatches(ports, port));

    [Fact]
    public void Firewall_rules_are_checked_with_block_rules_first()
    {
        const string engine = @"C:\Steam\steamapps\common\Mount & Blade II Bannerlord\Modules\Bannerlord Coop\DedicatedServer\engine\dotnet\dotnet.exe";
        var allowPort = new FirewallRuleInfo("Coodsz Coop server UDP 4200", true, true, true, 17, "4200", null);
        var allowProgram = new FirewallRuleInfo("dotnet", true, true, true, 17, "*", engine.ToUpperInvariant());
        var blockProgram = new FirewallRuleInfo("dotnet", true, false, true, 17, "*", engine);
        var tcpOnly = new FirewallRuleInfo("tcp", true, true, true, 6, "4200", null);
        var otherApp = new FirewallRuleInfo("game", true, true, true, 17, "*", @"C:\Games\other.exe");
        var disabled = allowPort with { Enabled = false };

        Assert.Equal(FirewallState.Allowed, FirewallRules.Check(new[] { allowPort }, 4200, engine));
        Assert.Equal(FirewallState.Allowed, FirewallRules.Check(new[] { allowProgram }, 4200, engine));
        Assert.Equal(FirewallState.Blocked, FirewallRules.Check(new[] { allowPort, blockProgram }, 4200, engine));
        Assert.Equal(FirewallState.Missing, FirewallRules.Check(new[] { tcpOnly, otherApp, disabled }, 4200, engine));
        Assert.Equal(FirewallState.Unknown, FirewallRules.Check(null, 4200, engine));
        var serviceBlock = new FirewallRuleInfo("svc", true, false, true, 17, "*", null, "Dnscache");
        var everyoneBlock = new FirewallRuleInfo("block all", true, false, true, 256, "", null);
        Assert.Equal(FirewallState.Allowed, FirewallRules.Check(new[] { allowPort, serviceBlock }, 4200, engine));
        Assert.Equal(FirewallState.Blocked, FirewallRules.Check(new[] { allowPort, everyoneBlock }, 4200, engine));
    }

    [Fact]
    public void Firewall_rules_scoped_to_another_windows_account_are_ignored()
    {
        const string engine = @"C:\Server\engine\dotnet\dotnet.exe";
        const string me = "S-1-5-21-1111111111-2222222222-3333333333-1001";
        const string sandbox = "S-1-5-21-1111111111-2222222222-3333333333-1003";
        var allowPort = new FirewallRuleInfo("Coodsz Coop server UDP 4200", true, true, true, 17, "4200", null);
        // Like codex_sandbox_offline_block_inbound: block everything, but only for the sandbox user.
        var sandboxBlock = new FirewallRuleInfo("codex_sandbox_offline_block_inbound", true, false, true, 256, "*", null, null, "O:LSD:(A;;CC;;;" + sandbox + ")");
        var myBlock = sandboxBlock with { Name = "mine", LocalUsers = "O:LSD:(A;;CC;;;" + me + ")" };
        var everyoneBlock = sandboxBlock with { Name = "everyone", LocalUsers = "O:LSD:(A;;CC;;;WD)" };
        var sandboxAllow = allowPort with { Name = "sandbox allow", LocalUsers = "O:LSD:(A;;CC;;;" + sandbox + ")" };

        Assert.Equal(FirewallState.Allowed, FirewallRules.Check(new[] { allowPort, sandboxBlock }, 4200, engine, me));
        Assert.Equal(FirewallState.Blocked, FirewallRules.Check(new[] { allowPort, myBlock }, 4200, engine, me));
        Assert.Equal(FirewallState.Blocked, FirewallRules.Check(new[] { allowPort, everyoneBlock }, 4200, engine, me));
        Assert.Equal(FirewallState.Missing, FirewallRules.Check(new[] { sandboxAllow }, 4200, engine, me));
        Assert.True(FirewallRules.CoversUser(null, me));
        Assert.True(FirewallRules.CoversUser("", me));
        Assert.False(FirewallRules.CoversUser("O:LSD:(A;;CC;;;" + sandbox + ")", me));
        Assert.True(FirewallRules.CoversUser("O:LSD:(A;;CC;;;" + sandbox + ")(A;;CC;;;" + me + ")", me));
        // Without the user's SID a scoped rule still counts, so nothing is ruled out by mistake.
        Assert.True(FirewallRules.CoversUser("O:LSD:(A;;CC;;;" + sandbox + ")", null));
    }

    [Fact]
    public void Firewall_commands_are_built_for_netsh()
    {
        const string engine = @"C:\Steam\steamapps\common\Mount & Blade II Bannerlord\Modules\Bannerlord Coop\DedicatedServer\engine\dotnet\dotnet.exe";
        var commands = FirewallRules.AddCommands(4200, engine);
        Assert.Equal(2, commands.Count);
        Assert.Equal("netsh advfirewall firewall add rule name=\"Coodsz Coop server UDP 4200\" dir=in action=allow protocol=UDP localport=4200 profile=any enable=yes", commands[0]);
        Assert.Equal("netsh advfirewall firewall add rule name=\"Coodsz Coop server program\" dir=in action=allow program=\"" + engine + "\" protocol=UDP profile=any enable=yes", commands[1]);

        var start = FirewallRules.ElevatedStart(4200, engine);
        Assert.Equal("cmd.exe", start.FileName);
        Assert.Equal("runas", start.Verb);
        Assert.True(start.UseShellExecute);
        Assert.Equal("/c " + commands[0] + " && " + commands[1], start.Arguments);

        Assert.Single(FirewallRules.AddCommands(4200, @"C:\100%\dotnet.exe"));
        Assert.Single(FirewallRules.AddCommands(4200, "dotnet.exe"));
        Assert.Single(FirewallRules.AddCommands(4200, null));
    }

    [Fact]
    public void Server_config_port_and_password_are_read()
    {
        const string json = """
            {
              // "port": 1111,
              "port": 4300, /* the UDP port */
              "password": "secret"
            }
            """;
        Assert.Equal(4300, LocalServer.ConfigPort(json));
        Assert.True(LocalServer.ConfigHasPassword(json));
        Assert.False(LocalServer.ConfigHasPassword("{\"password\": \"\"}"));
        Assert.Null(LocalServer.ConfigPort("{}"));
        Assert.Equal(new LocalServerInfo(4200, false), LocalServer.Detect(() => true, () => null));
        Assert.Null(LocalServer.Detect(() => false, () => json));
    }

    [Fact]
    public void Own_server_suggests_127_0_0_1()
    {
        var local = new LocalServerInfo(4300, false);
        var fill = LocalServer.Suggest(direct: true, address: " ", local);
        Assert.Equal(new OwnServerHint("127.0.0.1", 4300, "You are the host, use 127.0.0.1.", true), fill);
        Assert.Null(LocalServer.Suggest(direct: false, address: "", local));
        Assert.Null(LocalServer.Suggest(direct: true, address: "", null));
        Assert.Null(LocalServer.Suggest(direct: true, address: "26.5.5.5", local));
        Assert.False(LocalServer.Suggest(direct: true, address: "127.0.0.1", local)!.Fill);
        var own = LocalServer.Suggest(direct: true, address: "192.168.1.20", local, new[] { "192.168.1.20" });
        Assert.NotNull(own);
        Assert.False(own!.Fill);
        Assert.StartsWith("That address is this PC.", own.Note);
    }

    [Fact]
    public void Vpn_hint_names_the_app()
    {
        Assert.Null(LocalServer.VpnHint(new[] { new AdapterAddress("Wi-Fi", "", "192.168.1.2", AdapterKind.Lan, true) }));
        Assert.Contains("starts with 26", LocalServer.VpnHint(new[] { new AdapterAddress("Radmin VPN", "", "26.1.1.1", AdapterKind.Radmin, false) }));
        Assert.Contains("starts with 25", LocalServer.VpnHint(new[] { new AdapterAddress("Hamachi", "", "25.1.1.1", AdapterKind.Hamachi, false) }));
    }

    private static JoinFacts HostFacts(PortOpenResult? router = null, bool steam = true, bool password = true) => new()
    {
        IsHost = true,
        Port = 4200,
        SteamLobbyOn = steam,
        SteamName = "Coodsz",
        PasswordNeeded = password,
        Adapters = new[]
        {
            new AdapterAddress("Radmin VPN", "", "26.10.20.30", AdapterKind.Radmin, false),
            new AdapterAddress("Wi-Fi", "", "192.168.1.20", AdapterKind.Lan, true),
        },
        Router = router,
        ClientTool = "Coodsz Coop Client Launcher",
        Setup = "Vanilla campaign Map"
    };

    private static readonly PortOpenResult Opened = new(PortOpenOutcome.Opened, 4200, "UPnP", "81.2.69.160", "192.168.1.20");

    [Fact]
    public void Addresses_list_the_easiest_first_and_skip_a_shared_address()
    {
        var list = JoinPlan.Addresses(HostFacts(Opened));
        Assert.Equal(new[] { JoinAddressKind.SteamLobby, JoinAddressKind.VirtualLan, JoinAddressKind.Internet, JoinAddressKind.SameWifi, JoinAddressKind.ThisPc }, list.Select(a => a.Kind));
        Assert.Equal("Radmin VPN", list[1].Label);
        Assert.Equal("81.2.69.160", list[2].Value);

        var shared = JoinPlan.Addresses(HostFacts(new PortOpenResult(PortOpenOutcome.SharedAddress, 4200, "UPnP", "100.70.1.1"), steam: false));
        Assert.DoesNotContain(shared, a => a.Kind is JoinAddressKind.Internet or JoinAddressKind.SteamLobby);
    }

    [Fact]
    public void Invite_has_the_addresses_and_port_but_never_the_password()
    {
        var facts = HostFacts(Opened) with { };
        var invite = JoinPlan.Invite(facts);
        Assert.Equal(
            "Join my Bannerlord Coop server." + Environment.NewLine +
            "Open Coodsz Coop Client Launcher and pick the Vanilla campaign Map setup." + Environment.NewLine +
            "Steam lobby. Pick Steam lobby and type Coodsz." + Environment.NewLine +
            "Radmin VPN. Join my Radmin VPN network, pick Direct and type 26.10.20.30 with UDP port 4200." + Environment.NewLine +
            "Internet. Pick Direct and type 81.2.69.160 with UDP port 4200." + Environment.NewLine +
            "Same Wi Fi only. Pick Direct and type 192.168.1.20 with UDP port 4200." + Environment.NewLine +
            "A password is needed, ask me for it.", invite);

        var plain = JoinPlan.InviteLines(new JoinFacts { IsHost = true, PasswordNeeded = false });
        Assert.Contains("Ask me for the address. The UDP port is 4200.", plain);
        Assert.Equal("No password needed.", plain[^1]);
        Assert.DoesNotContain(plain, line => line.Contains(':'));
        Assert.All(JoinPlan.InviteLines(facts), line => Assert.DoesNotContain(':', line));
        Assert.Equal("X", JoinPlan.Invite(new JoinFacts(), _ => "X").Split(Environment.NewLine).Distinct().Single());
    }

    [Fact]
    public void Invite_names_the_map_and_stopped_server_says_the_port_opens_on_start()
    {
        var facts = new JoinFacts { IsHost = true, Map = "Vanilla campaign Map", ClientTool = "Coodsz Coop Client Launcher", PortOpensOnStart = true, ServerTool = "Coodsz Coop Server Tool" };
        Assert.Equal("Open Coodsz Coop Client Launcher and pick Vanilla campaign Map.", JoinPlan.InviteLines(facts)[1]);
        Assert.StartsWith("Coodsz Coop Server Tool tries to open UDP 4200 on your router when the server starts.", JoinGuide.Steps(GuideTopic.PortForward, facts)[0].Text);
        Assert.Equal("The port opens on your router when the server starts.", JoinGuide.Choices(facts).Single(c => c.Topic == GuideTopic.PortForward).Status);
        Assert.False(JoinGuide.Choices(facts).Single(c => c.Topic == GuideTopic.Host).WorksNow);
    }

    [Fact]
    public void Steam_persona_name_is_read_from_loginusers()
    {
        const string vdf = """
            "users"
            {
                "76561198000000001"
                {
                    "AccountName"		"login"
                    "PersonaName"		"Coodsz \"The Host\""
                    "MostRecent"		"1"
                }
            }
            """;
        Assert.Equal("Coodsz \"The Host\"", SteamRegistry.PersonaName(vdf, 76561198000000001));
        Assert.Null(SteamRegistry.PersonaName(vdf, 76561198000000002));
    }

    [Fact]
    public void Guide_picker_puts_what_works_first_for_the_host()
    {
        var choices = JoinGuide.Choices(HostFacts(steam: false));
        Assert.Equal(new[] { GuideTopic.SameWifi, GuideTopic.VirtualLan, GuideTopic.SteamLobby, GuideTopic.PortForward, GuideTopic.Host }, choices.Select(c => c.Topic));
        Assert.Equal("Turn on Steam advertise first.", choices[2].Status);
        Assert.Equal(GuideTopic.SameWifi, JoinGuide.Suggested(HostFacts(steam: false)));
        Assert.Equal(GuideTopic.SteamLobby, JoinGuide.Suggested(HostFacts()));

        var client = new JoinFacts();
        Assert.Equal(new[] { GuideTopic.SteamLobby, GuideTopic.SameWifi, GuideTopic.VirtualLan, GuideTopic.PortForward, GuideTopic.Host }, JoinGuide.Choices(client).Select(c => c.Topic));
        Assert.Equal(GuideTopic.SteamLobby, JoinGuide.Suggested(client));
        Assert.Equal(GuideTopic.Host, JoinGuide.Suggested(client with { LocalServer = new LocalServerInfo(4200, false) }));
    }

    [Fact]
    public void Guide_steps_use_the_detected_state()
    {
        var host = HostFacts(Opened);
        var vpn = JoinGuide.Steps(GuideTopic.VirtualLan, host);
        Assert.Contains(vpn, step => step.Copy == "26.10.20.30" && step.Text == "Your Radmin VPN address is 26.10.20.30.");

        var wifi = JoinGuide.Steps(GuideTopic.SameWifi, host);
        Assert.Contains(wifi, step => step.Copy == "192.168.1.20");
        Assert.Contains(wifi, step => step.Text.Contains("198", StringComparison.Ordinal));

        var forward = JoinGuide.Steps(GuideTopic.PortForward, host);
        Assert.StartsWith("Coodsz Coop Server Tool opened UDP 4200 on your router", forward[0].Text);
        Assert.Contains(forward, step => step.Copy == "81.2.69.160");

        var manual = JoinGuide.Steps(GuideTopic.PortForward, HostFacts());
        Assert.Equal("On your router page, forward UDP 4200 to this PC's IPv4 address 192.168.1.20.", manual[0].Text);
        Assert.Contains(manual, step => step.Text.Contains("whatismyip.com", StringComparison.Ordinal));

        var shared = JoinGuide.Steps(GuideTopic.PortForward, HostFacts(new PortOpenResult(PortOpenOutcome.SharedAddress, 4200, "UPnP", "100.70.1.1")));
        Assert.Equal(2, shared.Count);
        Assert.Contains("(100.70.1.1)", shared[0].Text);

        var steam = JoinGuide.Steps(GuideTopic.SteamLobby, host);
        Assert.Contains(steam, step => step.Copy == "Coodsz");

        var own = JoinGuide.Steps(GuideTopic.Host, new JoinFacts { LocalServer = new LocalServerInfo(4300, false), ClientTool = "Coodsz Coop Client Launcher" });
        Assert.Equal("A server runs on this PC right now, so you are the host.", own[0].Text);
        Assert.Contains(own, step => step.Copy == "127.0.0.1" && step.Text.Contains("UDP port 4300", StringComparison.Ordinal));
    }

    [Fact]
    public void Taom_wording_is_used_when_asked()
    {
        var taom = new JoinFacts { IsHost = true, Taom = true, SteamOption = "Advertise through Steam", SteamLobbyOn = false, ServerTool = "Coodsz TAOM Server Tool" };
        Assert.Contains(JoinGuide.Steps(GuideTopic.SteamLobby, taom), step => step.Text == "Tick Advertise through Steam in Coodsz TAOM Server Tool, save, and restart the server.");
        Assert.Contains(JoinGuide.Steps(GuideTopic.Host, taom), step => step.Text.Contains("up to an hour", StringComparison.Ordinal));
        Assert.DoesNotContain(JoinGuide.Steps(GuideTopic.Host, HostFacts()), step => step.Text.Contains("TAOM", StringComparison.Ordinal));
    }

    [Fact]
    public void Guide_text_has_no_colons_or_word_hyphens()
    {
        var facts = new[] { HostFacts(Opened), HostFacts(), new JoinFacts(), new JoinFacts { LocalServer = new LocalServerInfo(4200, true) } };
        var texts = facts.SelectMany(f => Enum.GetValues<GuideTopic>().SelectMany(t => JoinGuide.Steps(t, f)).Select(s => s.Text)
                .Concat(JoinGuide.Fixes(f)).Concat(JoinGuide.Choices(f).SelectMany(c => new[] { c.Title, c.Status })))
            .ToList();
        Assert.Equal(9, JoinGuide.Fixes(new JoinFacts()).Count);
        foreach (var text in texts)
        {
            Assert.DoesNotContain(':', text);
            Assert.DoesNotMatch(@"[A-Za-z]-[A-Za-z]", text);
        }
    }
}
