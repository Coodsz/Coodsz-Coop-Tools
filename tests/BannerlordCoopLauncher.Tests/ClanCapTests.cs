using BannerlordCoopLauncher.Core.Server;
using BannerlordCoopLauncher.Core.Updates;
using Coodsz.ServerRules;
using HostClanCap = BannerlordCoopLauncher.Core.Server.ClanCap;

namespace BannerlordCoopLauncher.Tests;

// Max clans per kingdom: the rule the Coodsz Server Tools module runs on the server
// (ClanCapRules.cs, linked), the settings file Coodsz Host writes, and how the two fit.
public sealed class ClanCapTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "coodsz-clancap-" + Guid.NewGuid().ToString("N"));

    public ClanCapTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    [Fact]
    public void A_full_kingdom_refuses_a_new_clan_and_keeps_the_ones_it_has()
    {
        var cap = new ClanCapSettings(3, countMercenaries: false);
        Assert.True(cap.Refuses(counted: 3, joinCounts: true, alreadyCounted: false));
        Assert.True(cap.Refuses(counted: 5, joinCounts: true, alreadyCounted: false));
        Assert.False(cap.Refuses(counted: 2, joinCounts: true, alreadyCounted: false));
        // A clan already counted there (a vassal moving inside its own kingdom) is never refused.
        Assert.False(cap.Refuses(counted: 3, joinCounts: true, alreadyCounted: true));
        // Mercenaries do not count, so hiring one is not capped.
        Assert.False(cap.Counts(mercenary: true));
        Assert.True(cap.Counts(mercenary: false));
        Assert.False(cap.Refuses(counted: 3, joinCounts: cap.Counts(true), alreadyCounted: false));
    }

    [Fact]
    public void Counting_mercenaries_caps_mercenary_contracts_too()
    {
        var cap = new ClanCapSettings(4, countMercenaries: true);
        Assert.True(cap.Counts(mercenary: true));
        Assert.True(cap.Refuses(counted: 4, joinCounts: cap.Counts(true), alreadyCounted: false));
        Assert.False(cap.Refuses(counted: 3, joinCounts: cap.Counts(true), alreadyCounted: false));
    }

    [Fact]
    public void No_limit_never_refuses()
    {
        Assert.False(ClanCapSettings.Off.Enabled);
        Assert.False(ClanCapSettings.Off.Refuses(1000, true, false));
        Assert.False(new ClanCapSettings(0, true).Refuses(1, true, false));
        Assert.Equal(0, new ClanCapSettings(-4, false).MaxClans);
        Assert.Equal(ClanCapSettings.Highest, new ClanCapSettings(100000, false).MaxClans);
    }

    [Theory]
    [InlineData(null, 0, false)]
    [InlineData("", 0, false)]
    [InlineData("not json", 0, false)]
    [InlineData("{ }", 0, false)]
    [InlineData("{ \"maxClansPerKingdom\": 12 }", 12, false)]
    [InlineData("{\"maxClansPerKingdom\":7,\"countMercenaries\":true}", 7, true)]
    [InlineData("{ // hand edited\n  \"maxClansPerKingdom\": \"9\", \"countMercenaries\": True, }", 9, true)]
    [InlineData("{ \"maxClansPerKingdom\": -3 }", 0, false)]
    [InlineData("{ \"maxClansPerKingdom\": 99999 }", 500, false)]
    public void The_module_reads_the_file_the_way_a_host_writes_it(string? json, int max, bool mercenaries)
    {
        var read = ClanCapSettings.Parse(json);
        Assert.Equal(max, read.MaxClans);
        Assert.Equal(mercenaries, read.CountMercenaries);
    }

    [Fact]
    public void Settings_file_round_trip_keeps_other_keys_and_the_module_reads_it()
    {
        var path = ClanCapFile.PathIn(_dir);
        Assert.Equal(HostClanCap.Off, ClanCapFile.Read(path));

        File.WriteAllText(path, "{ \"someOtherTool\": \"keep me\" }");
        ClanCapFile.Write(path, new HostClanCap(12, true));
        Assert.Equal(new HostClanCap(12, true), ClanCapFile.Read(path));
        var text = File.ReadAllText(path);
        Assert.Contains("\"someOtherTool\": \"keep me\"", text);
        Assert.Contains("\"maxClansPerKingdom\": 12", text);
        Assert.False(File.Exists(path + ".tmp"));

        var module = ClanCapSettings.Parse(text);
        Assert.Equal(12, module.MaxClans);
        Assert.True(module.CountMercenaries);

        ClanCapFile.Write(path, HostClanCap.Off);
        Assert.Equal(HostClanCap.Off, ClanCapFile.Read(path));
        Assert.False(ClanCapSettings.Parse(File.ReadAllText(path)).Enabled);

        File.WriteAllText(path, "{ broken");
        Assert.Equal(HostClanCap.Off, ClanCapFile.Read(path));
        ClanCapFile.Write(path, new HostClanCap(3, false));
        Assert.Equal(new HostClanCap(3, false), ClanCapFile.Read(path));
    }

    [Fact]
    public void The_host_and_the_module_use_the_same_file_and_keys()
    {
        Assert.Equal(ClanCapSettings.FileName, ClanCapFile.FileName);
        Assert.Equal(ClanCapSettings.MaxKey, ClanCapFile.MaxKey);
        Assert.Equal(ClanCapSettings.MercenaryKey, ClanCapFile.MercenaryKey);
        Assert.Equal(ClanCapSettings.Highest, ClanCapFile.Highest);
        // Next to Coop's server-config.json, which the dedicated server reads from Documents.
        Assert.Equal(Path.Combine(CoodszHost.Services.ServerSession.DocumentsData, "coodsz-server-tools.json"),
            ClanCapFile.PathIn(CoodszHost.Services.ServerSession.DocumentsData));
    }

    [Theory]
    [InlineData("", 0)]
    [InlineData(" 0 ", 0)]
    [InlineData("12", 12)]
    [InlineData("500", 500)]
    public void Settings_box_takes_a_whole_number(string text, int expected) => Assert.Equal(expected, ClanCapFile.ParseBox(text));

    [Theory]
    [InlineData("-1")]
    [InlineData("501")]
    [InlineData("3.5")]
    [InlineData("ten")]
    public void Settings_box_refuses_anything_else(string text) =>
        Assert.Equal(ClanCapFile.BadNumber, Assert.Throws<InvalidOperationException>(() => ClanCapFile.ParseBox(text)).Message);

    [Fact]
    public void Module_registers_the_command_and_patches_every_join_path()
    {
        var root = RepoRoot();
        var tools = Path.Combine(root, "compat", "CoodszServerTools");
        var module = File.ReadAllText(Path.Combine(tools, "src", "ToolsSubModule.cs"));
        Assert.Contains("Add(\"coodsz.clancap\", Safe(ClanCap.Command));", module);
        Assert.Contains("HasFunctionForCommand(\"coodsz.clancap\")", module);
        Assert.Contains("if (GameNetwork.IsDedicatedServer) ClanCapSafely(ClanCap.PatchGame);", module);

        var cap = File.ReadAllText(Path.Combine(tools, "src", "ClanCap.cs"));
        foreach (var target in new[]
                 {
                     "\"ApplyInternal\"", "nameof(ChangeKingdomAction.ApplyByJoinToKingdomByDefection)", "JoinKingdomAsClanBarterable.GetUnitValueForFaction", "MercenaryJoinKingdomBarterable.GetUnitValueForFaction",
                     "VassalServiceHandler\", \"ApplyVassalage\"", "MercenaryServiceHandler\", \"ApplyMercenaryService\"",
                     "PlayerPartyInteractionOutcomeHandler\", \"ApplyVassalage\"", "PlayerPartyInteractionOutcomeHandler\", \"ApplyMercenaryJoin\"",
                     "LordBarterHandler\", \"CanAuthorizeKind\""
                 })
            Assert.Contains(target, cap);
        // Only joins are capped: leaving, rebellions and new kingdoms go through untouched.
        Assert.DoesNotContain("ChangeKingdomActionDetail.CreateKingdom", cap);
        Assert.DoesNotContain("ChangeKingdomActionDetail.LeaveWithRebellion", cap);

        // The module still needs no Coop DLL, and builds for the server's .NET 6 with its own Harmony.
        var project = File.ReadAllText(Path.Combine(tools, "CoodszServerTools.csproj"));
        Assert.Contains("<TargetFramework>net6.0</TargetFramework>", project);
        Assert.Contains("$(ServerBin)\\0Harmony.dll", project);
        Assert.DoesNotContain("Modules\\Coop", project);
    }

    [Fact]
    public void Settings_page_shows_it_in_both_builds_with_tips_and_a_NEW_badge()
    {
        var root = RepoRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "CoodszHost", "MainWindow.xaml"));
        Assert.Contains("<StackPanel x:Name=\"ClanCapGroup\" Visibility=\"Collapsed\">", xaml);
        Assert.Contains("x:Name=\"ClanCapBox\" ToolTip=", xaml);
        Assert.Contains("x:Name=\"ClanCapMercenariesBox\" ToolTip=", xaml);

        // Both builds show it: the AD1259 server reads the same coodsz-server-tools.json.
        var window = SourceFiles.Window(root, "CoodszHost");
        var start = SourceFiles.Method(window, "private void StartClanCap()");
        Assert.Contains("ClanCapGroup.Visibility = Visibility.Visible;", start);
        Assert.DoesNotContain("return;", start);
        var code = File.ReadAllText(Path.Combine(root, "src", "CoodszHost", "MainWindow.ClanCap.cs"));
        Assert.Contains("NewBadges.Attach(ClanCapHeading, NewFeatures.ClanCap", code);
        Assert.Contains(NewFeatures.CoodszHost, f => f.Id == NewFeatures.ClanCap && f.Version == "v1.2.57");
        Assert.Contains(WhatsNew.Friends.Single(e => e.Version == "v1.2.57").Lines, line => line.Contains("Max clans per kingdom"));

        // After Save settings the note says the cap applies live and the rest after a restart.
        Assert.Contains("SettingsSaveDetail.Text = \"Save settings writes this page. Max clans per kingdom applies within a few seconds.", code);
        var settings = File.ReadAllText(Path.Combine(root, "src", "CoodszHost", "MainWindow.Settings.cs"));
        Assert.Contains("? \"Saved. Max clans per kingdom applies within a few seconds. Restart the server so the other changes apply.\"", settings);
    }

    [Fact]
    public void Every_patch_returns_at_once_while_the_cap_is_off()
    {
        // The release condition for Max clans per kingdom: with maxClansPerKingdom 0, or no
        // coodsz-server-tools.json at all (the default), every Harmony patch is a no op, so a
        // server that does not use the cap runs exactly as before, player joins included.
        Assert.False(ClanCapSettings.Parse(null).Enabled);
        Assert.False(ClanCapSettings.Parse("{ \"maxClansPerKingdom\": 0, \"countMercenaries\": true }").Enabled);
        Assert.False(ClanCapSettings.Off.Refuses(int.MaxValue, true, false));

        var cap = File.ReadAllText(Path.Combine(RepoRoot(), "compat", "CoodszServerTools", "src", "ClanCap.cs")).Replace("\r\n", "\n");
        Assert.Contains("public static bool Inert => !Settings.Enabled;", cap);
        // A missing file reads as no limit.
        Assert.Contains("var read = time == DateTime.MinValue ? ClanCapSettings.Off : ClanCapSettings.Parse(File.ReadAllText(path));", cap);

        var handlers = System.Text.RegularExpressions.Regex.Matches(cap, @"nameof\((\w+(?:Prefix|Postfix))\)")
            .Select(m => m.Groups[1].Value).Distinct().ToList();
        Assert.Equal(
            new[] { "ApplyInternalPrefix", "DefectionPrefix", "JoinValuePostfix", "MercenaryValuePostfix", "VassalPrefix", "MercenaryPrefix", "PartyVassalPrefix", "PartyMercenaryPrefix", "LordBarterPostfix" }.OrderBy(n => n),
            handlers.OrderBy(n => n));
        foreach (var handler in handlers)
        {
            var name = handler;
            var signature = cap.IndexOf("private static " + (name.EndsWith("Prefix") ? "bool " : "void ") + name + "(", StringComparison.Ordinal);
            Assert.True(signature >= 0, name + " was not found.");
            var lineEnd = cap.IndexOf('\n', signature);
            var line = cap[signature..lineEnd];
            // PartyVassalPrefix and PartyMercenaryPrefix hand straight on to PartyJoin.
            if (line.Contains("=> PartyJoin(", StringComparison.Ordinal))
            {
                name = "PartyJoin";
                signature = cap.IndexOf("private static bool PartyJoin(", StringComparison.Ordinal);
            }

            var body = SourceFiles.Method(cap[signature..], "private static");
            var open = body.IndexOf('{');
            var guard = body.IndexOf("if (Inert) return", StringComparison.Ordinal);
            Assert.True(guard > open, name + " does not check Inert.");
            // Nothing before the check but the try: no game object, no Coop object, no result change.
            var before = body[(open + 1)..guard].Trim();
            Assert.True(before == "try" || before == "try {" || before == "try\n        {" || before.Replace(" ", "").Replace("\n", "") == "try{", name + " does work before it checks Inert: " + before);
            var ret = body.IndexOf(';', guard);
            Assert.Equal(name.EndsWith("Postfix") ? "if (Inert) return;" : "if (Inert) return true;", body[guard..(ret + 1)]);
        }
    }

    [Fact]
    public void A_failed_Harmony_patch_is_logged_and_the_server_goes_on()
    {
        var tools = Path.Combine(RepoRoot(), "compat", "CoodszServerTools", "src");
        var module = File.ReadAllText(Path.Combine(tools, "ToolsSubModule.cs")).Replace("\r\n", "\n");
        // Every call into the patches goes through a try that logs, even a missing 0Harmony.dll.
        Assert.DoesNotContain(" ClanCap.PatchGame();", module);
        Assert.DoesNotContain(" ClanCap.PatchCoop();", module);
        Assert.Contains("if (GameNetwork.IsDedicatedServer) ClanCapSafely(ClanCap.PatchGame);", module);
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(module, @"ClanCapSafely\(ClanCap\.PatchCoop\);").Count);
        var safely = SourceFiles.Method(module, "private static void ClanCapSafely(Action patch)");
        Assert.Contains("try { patch(); }", safely);
        Assert.Contains("catch (Exception e)", safely);
        Assert.Contains("MethodImplOptions.NoInlining", module);
        // The commands are registered before any patching, so they work either way.
        var load = SourceFiles.Method(module, "protected override void OnSubModuleLoad()");
        Assert.True(load.IndexOf("Register();", StringComparison.Ordinal) < load.IndexOf("ClanCapSafely", StringComparison.Ordinal));

        var cap = File.ReadAllText(Path.Combine(tools, "ClanCap.cs")).Replace("\r\n", "\n");
        Assert.Contains("Log(\"Max clans per kingdom could not patch the game: \"", cap);
        Assert.Contains("Log(\"Max clans per kingdom could not patch Coop: \"", cap);
        // Each patch body is wrapped too, so an error inside one lets the game's own code run.
        Assert.Equal(8, System.Text.RegularExpressions.Regex.Matches(cap, @"catch \(Exception\) \{ return true; \}").Count
                        + System.Text.RegularExpressions.Regex.Matches(cap, @"catch \(Exception\) \{ \}").Count);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "BannerlordCoopLauncher.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
