using System.Net;
using BannerlordCoopLauncher.Core.Parsing;
using TaomCoop.Core;

namespace BannerlordCoopLauncher.Tests;

public class TaomCompatListTests
{
    [Fact]
    public void The_built_in_list_loads_and_follows_the_research()
    {
        var list = TaomCompatList.BuiltIn();
        Assert.Equal(TaomCompatList.Kind, list.FileKind);
        Assert.True(list.Mods.Count >= 40);
        Assert.Equal(CompatTag.Safe, list.Find("DismembermentPlus")!.Kind);
        Assert.Equal(CompatTag.Broken, list.Find("FrontlineMod")!.Kind);
        Assert.Equal(CompatTag.Broken, list.Find(null, "2881380744")!.Kind);
        Assert.Equal(CompatTag.Everyone, list.Find("ImprovedGarrisons")!.Kind);
        Assert.True(list.Find("ImprovedGarrisons")!.HasServerBuild);
        Assert.True(list.Find("XorberaxLegacy")!.ClientOnlyOnServer);
        Assert.Equal(CompatTag.Risky, list.Find("NotAnId", "0", "Clan Honor")!.Kind);
        Assert.Null(list.Find("SomethingNew", "999", "Something new"));
        // Every tag in the file is one the tools know, and no note uses a colon.
        Assert.All(list.Mods, entry => Assert.NotEqual(CompatTag.Unknown, entry.Kind));
        Assert.All(list.Mods, entry => Assert.DoesNotContain(":", entry.Note));
    }

    [Fact]
    public void A_list_with_the_tally_counts_and_community_mods_reads_as_before()
    {
        // The Coodsz-TAOM tally (.github/scripts/tally-taom-compat.mjs) adds count fields to entries and a
        // community array; released tools must keep reading the file and ignore both.
        const string json = "{ \"kind\": \"CoodszTaomCompat\", \"schemaVersion\": 2, \"updated\": \"2026-10-24\", \"mods\": [\n" +
            "  { \"id\": \"RTSCamera\", \"name\": \"RTS Camera\", \"workshopId\": \"3747725551\", \"tag\": \"safe\", \"class\": \"D\", \"clientOnlyOnServer\": true, \"source\": \"coodsz-coop\", \"checked\": \"2026-10-18\", \"note\": \"n\", \"works\": 5, \"broken\": 1, \"clientOnly\": 2, \"lastReport\": \"2026-10-23\" }\n" +
            "], \"community\": [ { \"id\": \"New.Mod\", \"name\": \"New Mod\", \"works\": 1, \"broken\": 0, \"lastReport\": \"2026-10-22\" } ] }";
        var list = TaomCompatList.Parse(json);
        var entry = Assert.Single(list.Mods);
        Assert.Equal(CompatTag.Safe, entry.Kind);
        Assert.True(entry.ClientOnlyOnServer);
        Assert.Equal("2026-10-18", entry.Checked);
        Assert.Null(list.Find("New.Mod"));
        Assert.EndsWith("/MODS.md", TaomCompatList.ModsPageUrl);
    }

    [Fact]
    public void Badges_are_green_yellow_or_red()
    {
        Assert.Equal("green", TaomCompatList.BadgeColor(CompatTag.Safe));
        Assert.Equal("yellow", TaomCompatList.BadgeColor(CompatTag.Everyone));
        Assert.Equal("yellow", TaomCompatList.BadgeColor(CompatTag.Risky));
        Assert.Equal("red", TaomCompatList.BadgeColor(CompatTag.Broken));
        Assert.Null(TaomCompatList.BadgeText(CompatTag.Unknown));
        Assert.Equal("Known broken", TaomCompatList.BadgeText(CompatTag.Broken));
    }

    [Fact]
    public void A_bad_file_is_refused_and_the_built_in_list_is_used()
    {
        Assert.Throws<InvalidOperationException>(() => TaomCompatList.Parse("{\"kind\":\"Other\",\"schemaVersion\":1}"));
        Assert.Throws<InvalidOperationException>(() => TaomCompatList.Parse("not json"));
        var path = Path.Combine(Path.GetTempPath(), "compat-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllText(path, "broken");
            Assert.NotNull(TaomCompatList.Load(path).Find("DismembermentPlus"));
            File.WriteAllText(path, "{\"kind\":\"CoodszTaomCompat\",\"schemaVersion\":1,\"mods\":[{\"id\":\"OnlyOne\",\"tag\":\"safe\"}]}");
            var saved = TaomCompatList.Load(path);
            Assert.Single(saved.Mods);
            Assert.Equal(CompatTag.Safe, saved.Find("onlyone")!.Kind);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private sealed class Answer(HttpStatusCode code, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(code) { Content = new StringContent(body) });
    }

    [Fact]
    public async Task A_good_download_is_saved_and_a_bad_one_changes_nothing()
    {
        var path = Path.Combine(Path.GetTempPath(), "compat-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var good = "{\"kind\":\"CoodszTaomCompat\",\"schemaVersion\":1,\"mods\":[{\"id\":\"NewMod\",\"tag\":\"broken\",\"note\":\"Crashes\"}]}";
            using (var http = new HttpClient(new Answer(HttpStatusCode.OK, good)))
            {
                var list = await TaomCompatList.DownloadAsync(http, path, CancellationToken.None);
                Assert.Equal(CompatTag.Broken, list!.Find("NewMod")!.Kind);
                Assert.Equal(good, File.ReadAllText(path));
            }

            using (var http = new HttpClient(new Answer(HttpStatusCode.NotFound, "")))
                Assert.Null(await TaomCompatList.DownloadAsync(http, path, CancellationToken.None));
            using (var http = new HttpClient(new Answer(HttpStatusCode.OK, "{\"kind\":\"Other\"}")))
                Assert.Null(await TaomCompatList.DownloadAsync(http, path, CancellationToken.None));
            Assert.Equal(good, File.ReadAllText(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void A_known_broken_mod_is_warned_about_before_launch()
    {
        var setup = new TaomSetup { Name = "Mine" };
        TaomLoadOrder.Add(setup, "Frontline Mod", "FrontlineMod", null);
        var facts = new Dictionary<string, TaomModFact>(StringComparer.OrdinalIgnoreCase)
        {
            ["FrontlineMod"] = new("FrontlineMod", ModuleManifestReader.Parse("<Module><Id value=\"FrontlineMod\"/></Module>"), new[] { "M\\FrontlineMod" }, Array.Empty<string>())
        };
        var issue = Assert.Single(TaomModChecks.Run(setup, facts, TaomCompatList.BuiltIn()));
        Assert.False(issue.Blocks);
        Assert.StartsWith("Frontline Mod is known to be broken with TAOM. Damages TAOM files.", issue.Text);
        Assert.Empty(TaomModChecks.Run(setup, facts));
    }
}
