using System.Net;
using System.Text.Json;
using BannerlordCoopLauncher.Core.Profile;
using TaomCoop.Core;

namespace BannerlordCoopLauncher.Tests;

public class TaomModListTests
{
    private static readonly string[] TestedLive =
    [
        "BetterExceptionWindow", "Bannerlord.CCsBanners", "XorberaxLegacy", "CoopModPatch", "ImprovedGarrisons", "DismembermentPlus",
        "BannerFix", "RaiseYourBanner", "RaiseYourTorch", "ImmersiveBattlefields", "RTSCamera", "RTSCamera.CommandSystem"
    ];

    [Fact]
    public void Every_entry_says_where_it_comes_from_and_when()
    {
        var list = TaomCompatList.BuiltIn();
        Assert.Equal(2, list.SchemaVersion);
        Assert.True(list.Mods.Count >= 90);
        var known = new[] { CompatSource.CoodszCoop, CompatSource.ServerLoads, CompatSource.TaomDiscord, CompatSource.TaomDev, CompatSource.CoodszNotes };
        Assert.All(list.Mods, entry => Assert.Contains(entry.Source, known));
        Assert.All(list.Mods, entry => Assert.Matches(@"^\d{4}-\d{2}-\d{2}$", entry.Checked));
        Assert.All(list.Mods, entry => Assert.NotEqual(CompatTag.Unknown, entry.Kind));
        Assert.All(list.Mods, entry => Assert.DoesNotContain(":", entry.Note));
        Assert.Equal(TestedLive.OrderBy(id => id), list.Mods.Where(entry => entry.Source == CompatSource.CoodszCoop).Select(entry => entry.Id).OrderBy(id => id));
        // Not tested in Coop means not called tested safe.
        Assert.All(list.Mods.Where(entry => entry.Kind == CompatTag.Safe), entry => Assert.Equal(CompatSource.CoodszCoop, entry.Source));
        // Names are matched by name when the id is not known, so no two entries share one.
        Assert.Equal(list.Mods.Count, list.Mods.Select(entry => entry.Label.ToLowerInvariant()).Distinct().Count());
    }

    [Fact]
    public void The_pasted_lists_and_the_channel_mods_are_in()
    {
        var list = TaomCompatList.BuiltIn();
        foreach (var name in new[] { "Banner Editor", "Draks Battle Tweaks", "Draks Character Stuff", "Draks Bandits", "Draks Crafting", "Plantation Estates",
                     "New Slide - Additional Sliders", "Dual Wield - TAOM", "TAOM AI Frontier - Immersion", "TOAM Food Debuff", "Player Settlement: TAOM",
                     "Castle Redux", "Battle Voice", "Bannerlord Cheats Reload", "TAOM Troop limit expander", "Creator Camera And Video Maker",
                     "HorseCounter", "Dynamic Combat Camera", "Warbandlord", "Stamina", "Organized Frontlines" })
            Assert.Contains(list.Mods, entry => entry.Name == name);
        Assert.Contains(list.Find("CampaignMapVisibility")!.Source, new[] { CompatSource.TaomDev, CompatSource.ServerLoads });
        Assert.Equal("13918", list.Find("CampaignMapVisibility")!.NexusId);
    }

    [Fact]
    public void The_nexus_mods_have_their_real_module_ids_and_each_taom_patch_its_own_entry()
    {
        var list = TaomCompatList.BuiltIn();
        foreach (var (id, nexus) in new[] { ("ArmiesOfMiddleEarth", "13876"), ("TAOM_SettlementPlacementProbe", "13856"), ("TAOM_PlayerVoices", "13934"),
                     ("TAOM_BloodOverhaul", "13982"), ("CampaignMapVisibility", "13918"), ("PlayerSpeedControl", "13919"), ("PlayerJumpHeight", "13921"),
                     ("NoFallDamage", "13923"), ("VSShieldTauntSystem", "9948"), ("Limitless", "14025"), ("TAOM_CheatPanel", "13608") })
        {
            Assert.Equal(nexus, list.Find(id)!.NexusId);
            Assert.Null(TaomLoadOrder.RefusalReason(id));
        }

        Assert.Null(list.Find(null, null, "TAOM Compatibility Patches"));
        foreach (var (id, needs) in new[] { ("TAOM_Standalone_Compat_Diplomacy", "Diplomacy"), ("TAOM_Standalone_Compat_RBM", "Realistic Battle Mod"),
                     ("TAOM_Standalone_Compat_AIInfluence", "AI Influence"), ("TAOM_Standalone_Compat_Fourberie", "Fourberie"),
                     ("TAOM_Standalone_Compat_Tournaments", "the Tournaments mod"), ("TAOM_XorberaxCompatibility", "Xorberax's Legacy") })
        {
            var entry = list.Find(id)!;
            Assert.Equal("13207", entry.NexusId);
            Assert.Contains(entry.Source, new[] { CompatSource.TaomDev, CompatSource.ServerLoads });
            Assert.Contains("Needs " + needs, entry.Note);
            Assert.Contains(entry.Kind, new[] { CompatTag.Reported, CompatTag.Risky, CompatTag.Loads });
            Assert.Null(TaomLoadOrder.RefusalReason(id));
        }

        Assert.Contains("broken", list.Find("TAOM_Standalone_Compat_Diplomacy")!.Note);
        Assert.Equal("TAOM RBM Patch", list.Find("TAOM_Standalone_Compat_RBM")!.Name);
        Assert.Equal(CompatTag.Reported, list.Find(null, null, "Banner Editor")!.Kind);
        Assert.Equal(CompatTag.Broken, list.Find("Bannerlord.Diplomacy")!.Kind);
        Assert.Equal(CompatSource.TaomDiscord, list.Find("Bannerlord.Diplomacy")!.Source);
    }

    [Fact]
    public void A_schema_2_list_still_reads_in_an_older_tool()
    {
        var text = new StreamReader(typeof(TaomCompatList).Assembly.GetManifestResourceStream("TaomCoop.Compat." + TaomCompatList.FileName)!).ReadToEnd();
        // What a schema 1 tool reads: it knows fewer fields and four tags, and wants schemaVersion 1 or more.
        var old = JsonSerializer.Deserialize<OldList>(text, new JsonSerializerOptions { PropertyNameCaseInsensitive = true, AllowTrailingCommas = true })!;
        Assert.Equal("CoodszTaomCompat", old.Kind);
        Assert.True(old.SchemaVersion >= 1);
        Assert.Equal(TaomCompatList.BuiltIn().Mods.Count, old.Mods.Count);
        Assert.Contains(old.Mods, mod => mod.Tag == "reported");

        // And a newer list with fields and tags this tool does not know is still read.
        var newer = TaomCompatList.Parse("{\"kind\":\"CoodszTaomCompat\",\"schemaVersion\":7,\"later\":1,\"mods\":[{\"id\":\"A\",\"tag\":\"someday\",\"extra\":[1]}]}");
        Assert.Equal(CompatTag.Unknown, newer.Find("A")!.Kind);
        Assert.Null(TaomCompatList.BadgeText(newer.Find("A")!.Kind));
        Assert.Equal("Coodsz notes", CompatSource.Label(newer.Find("A")!.Source));
    }

    private sealed class OldList
    {
        [System.Text.Json.Serialization.JsonPropertyName("kind")]
        public string Kind { get; set; } = "";
        public int SchemaVersion { get; set; }
        public List<OldEntry> Mods { get; set; } = new();
    }

    private sealed class OldEntry
    {
        public string Id { get; set; } = "";
        public string Tag { get; set; } = "";
        public string Note { get; set; } = "";
    }

    [Fact]
    public void Badges_show_the_source_on_hover()
    {
        Assert.Equal("Works in TAOM, not tested in Coop", TaomCompatList.BadgeText(CompatTag.Reported));
        Assert.Equal("Loads on the server", TaomCompatList.BadgeText(CompatTag.Loads));
        Assert.Equal("blue", TaomCompatList.BadgeColor(CompatTag.Reported));
        Assert.Equal("blue", TaomCompatList.BadgeColor(CompatTag.Loads));
        var entry = TaomCompatList.BuiltIn().Find("RTSCamera")!;
        var hover = TaomCompatList.Hover(entry);
        Assert.StartsWith(entry.Note, hover);
        Assert.EndsWith("Tested in Coop by Coodsz, checked 2026-10-18.", hover);
        Assert.EndsWith("[x]", TaomCompatList.Hover(entry, text => "[x]"));
        Assert.Equal("Said by TAOM developer Theboyys.", TaomCompatList.SourceLine(new CompatEntry { Source = "taom-dev" }));
    }

    [Fact]
    public void The_mod_list_window_groups_searches_and_links()
    {
        var list = TaomCompatList.BuiltIn();
        var groups = TaomModList.Groups(list);
        Assert.Equal(TaomModList.Order, groups.Select(group => group.Group));
        Assert.Equal(list.Mods.Count, groups.Sum(group => group.Mods.Count));
        Assert.Equal(12, groups[0].Mods.Count);
        Assert.Contains(groups[0].Mods, entry => entry.Id == "XorberaxLegacy");
        Assert.Contains(groups[2].Mods, entry => entry.Name == "Banner Editor");
        Assert.Contains(groups[3].Mods, entry => entry.Id == "Bannerlord.Diplomacy");
        // Mods that passed the server load test; the risky ones stay in the risky group.
        Assert.Contains(groups[1].Mods, entry => entry.Id == "TAOM_PlayerVoices");

        var camera = TaomModList.Groups(list, "rts camera");
        Assert.Equal(2, camera[0].Mods.Count);
        Assert.Empty(camera[3].Mods);
        Assert.Single(TaomModList.Groups(list, "CAMPAIGN MAP VISIBILITY 13918").SelectMany(group => group.Mods));

        Assert.Equal("https://steamcommunity.com/sharedfiles/filedetails/?id=3747725551", TaomModList.Link(list.Find("RTSCamera")!));
        Assert.Equal("Workshop", TaomModList.LinkText(list.Find("RTSCamera")!));
        Assert.Equal("https://www.nexusmods.com/mountandblade2bannerlord/mods/13876", TaomModList.Link(list.Find("ArmiesOfMiddleEarth")!));
        Assert.Equal("Nexus", TaomModList.LinkText(list.Find("ArmiesOfMiddleEarth")!));
        Assert.Null(TaomModList.Link(list.Find(null, null, "Skynet")!));

        var loads = new CompatEntry { Name = "X", Tag = "loads", Source = CompatSource.ServerLoads };
        Assert.Equal(CompatGroup.LoadsOnServer, TaomModList.GroupOf(loads));
        Assert.Equal(CompatGroup.RiskyOrBroken, TaomModList.GroupOf(new CompatEntry { Name = "Y", Tag = "risky", Source = CompatSource.ServerLoads }));
    }

    [Fact]
    public void Add_to_setup_uses_the_copy_on_this_pc_or_the_list_id()
    {
        var list = TaomCompatList.BuiltIn();
        var onPc = new List<FoundMod>
        {
            new("Party AI Controls", "PartyAIControls", "3732334365", "W\\3732334365", "Workshop"),
            new("Banner Editor", "BannerEditor", "0", "M\\BannerEditor", "Modules")
        };
        var party = list.Find(null, "3732334365")!;
        Assert.Equal("PartyAIControls", TaomModList.AddTarget(party, TaomModList.OnThisPc(party, onPc), out _)!.Value.ModuleId);
        var editor = list.Find(null, null, "Banner Editor")!;
        Assert.Equal("BannerEditor", TaomModList.AddTarget(editor, TaomModList.OnThisPc(editor, onPc), out _)!.Value.ModuleId);

        var rbm = list.Find("RBM")!;
        var target = TaomModList.AddTarget(rbm, TaomModList.OnThisPc(rbm, onPc), out var none)!.Value;
        Assert.Null(none);
        Assert.Equal(("Realistic Battle Mod", "RBM", "2859251492"), target);

        var skynet = list.Find(null, null, "Skynet")!;
        Assert.Null(TaomModList.AddTarget(skynet, null, out var reason));
        Assert.Contains("not on this PC yet", reason);
        Assert.Equal("TAOM plus is locked. Click Copy first to make a setup you can add mods to.", TaomModList.LockedText("TAOM plus"));

        // The same load order rules as Add mods.
        var setup = new TaomSetup { Name = "Mine" };
        Assert.Null(TaomLoadOrder.Add(setup, target.Name, target.ModuleId, target.WorkshopId));
        Assert.NotNull(TaomLoadOrder.Add(setup, target.Name, target.ModuleId, target.WorkshopId));
    }

    private sealed class Answers(Func<string, (HttpStatusCode Code, string Body)> answer) : HttpMessageHandler
    {
        public List<string> Asked { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Asked.Add(request.RequestUri!.ToString());
            var (code, body) = answer(request.RequestUri!.ToString());
            return Task.FromResult(new HttpResponseMessage(code) { Content = new StringContent(body) });
        }
    }

    [Fact]
    public async Task The_list_comes_from_github_with_bannerlord_eu_as_the_fallback()
    {
        Assert.Equal("https://raw.githubusercontent.com/Coodsz/Coodsz-TAOM/main/compat/taom-mod-compat.json", TaomCompatList.OnlineUrl);
        Assert.Equal("https://bannerlord.eu/download/taom-mod-compat.json", TaomCompatList.FallbackUrl);
        var path = Path.Combine(Path.GetTempPath(), "compat-" + Guid.NewGuid().ToString("N") + ".json");
        var newer = "{\"kind\":\"CoodszTaomCompat\",\"schemaVersion\":2,\"updated\":\"2099-01-01\",\"mods\":[{\"id\":\"NewMod\",\"tag\":\"reported\"}]}";
        var older = "{\"kind\":\"CoodszTaomCompat\",\"schemaVersion\":1,\"updated\":\"2026-10-01\",\"mods\":[{\"id\":\"OldMod\",\"tag\":\"safe\"}]}";
        try
        {
            var handler = new Answers(url => url == TaomCompatList.OnlineUrl ? (HttpStatusCode.NotFound, "") : (HttpStatusCode.OK, newer));
            using (var http = new HttpClient(handler))
                Assert.NotNull((await TaomCompatList.DownloadAsync(http, path, CancellationToken.None))!.Find("NewMod"));
            Assert.Equal(new[] { TaomCompatList.OnlineUrl, TaomCompatList.FallbackUrl }, handler.Asked);
            Assert.NotNull(TaomCompatList.Load(path).Find("NewMod"));

            // A list older than the one inside the tool is not taken, online or saved.
            using (var http = new HttpClient(new Answers(_ => (HttpStatusCode.OK, older))))
                Assert.Null(await TaomCompatList.DownloadAsync(http, path, CancellationToken.None));
            Assert.Equal(newer, File.ReadAllText(path));
            File.WriteAllText(path, older);
            Assert.Null(TaomCompatList.Load(path).Find("OldMod"));
            Assert.NotNull(TaomCompatList.Load(path).Find("RTSCamera"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void The_mod_list_is_first_in_the_more_menu_by_add_mods_and_adds_the_add_mods_way()
    {
        var root = RepoRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "TaomCoopLauncher", "MainWindow.xaml"));
        var more = xaml.IndexOf("x:Name=\"ModsMoreButton\"", StringComparison.Ordinal);
        var modList = xaml.IndexOf("x:Name=\"ModListItem\"", StringComparison.Ordinal);
        Assert.True(more > 0 && modList > more && modList - more < 600);
        Assert.Contains("Click=\"ModList_Click\"", xaml);

        var add = SourceFiles.Method(SourceFiles.Window(root, "TaomCoopLauncher"), "private string AddFromModList(");
        Assert.Contains("TaomModList.LockedText", add);
        Assert.Contains("ScanForAdding()", add);
        Assert.Contains("ConfirmBrokenMod(", add);
        Assert.Contains("TaomLoadOrder.Add(_editing", add);
        Assert.Contains("CopyIntoModules(found)", add);
        Assert.True(add.IndexOf("_editing.Locked", StringComparison.Ordinal) < add.IndexOf("TaomLoadOrder.Add", StringComparison.Ordinal));

        var window = File.ReadAllText(Path.Combine(root, "src", "TaomCoopLauncher", "ModListWindow.cs"));
        Assert.Contains("TaomModList.Groups(_list, _search.Text)", window);
        Assert.Contains("\"Add to setup\"", window);
        Assert.Contains("IsCancel = true", window);
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "BannerlordCoopLauncher.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
