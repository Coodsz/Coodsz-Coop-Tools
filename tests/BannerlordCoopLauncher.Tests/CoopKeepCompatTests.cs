// Tests ported from CoopKeep (https://github.com/Aderimo/bannerlord-coopkeep),
// tests/CoopKeep.Core.Tests/Installation/ModCompatDatabaseTests.cs, translated from Turkish and
// changed for the Coodsz tools.
//
// Copyright (c) 2026 aderimo
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

using System.Text.RegularExpressions;
using BannerlordCoopLauncher.Core.Modules;

namespace BannerlordCoopLauncher.Tests;

/// <summary>The community mod list.</summary>
public sealed class CoopKeepCompatTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "coodsz-compat-" + Guid.NewGuid().ToString("N"));

    public CoopKeepCompatTests() => Directory.CreateDirectory(_directory);

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void A_list_with_client_only_counts_and_the_last_report_day_reads_as_before()
    {
        // The tally (.github/scripts/tally-mod-compat.mjs) adds clientOnly and lastReport to reported entries;
        // released tools must keep reading the list and skip both.
        const string json = "{ \"mods\": [ { \"id\": \"A\", \"name\": \"Alpha\", \"status\": \"Works\", \"coop\": \"v0.1.6\", \"works\": 3, \"broken\": 1, \"clientOnly\": 2, \"lastReport\": \"2026-10-23\" } ] }";
        var entry = ModCompatDatabase.Parse(json)?.Find("A");
        Assert.NotNull(entry);
        Assert.Equal(CommunityStatus.Works, entry!.Status);
        Assert.Equal((3, 1), (entry.Works, entry.Broken));
        Assert.Equal("https://github.com/Coodsz/Coodsz-Coop-and-Friends/blob/main/MODS.md", ModCompatDatabase.ModsPageUrl);
    }

    [Fact]
    public void The_list_reads_and_ids_match_loosely()
    {
        var db = ModCompatDatabase.Parse("""
            {
              // comments are allowed
              "mods": [
                { "id": "Bannerlord.Harmony", "status": "Works", "coop": "v0.1.6" },
                { "id": "DismembermentPlus", "status": "Broken", "note": "crashes in battle" },
              ]
            }
            """)!;

        Assert.Equal(2, db.Count);
        Assert.Equal(CommunityStatus.Works, db.Find("bannerlord harmony")!.Status);
        Assert.Equal(CommunityStatus.Broken, db.Find("Dismemberment_Plus")!.Status);
        Assert.Null(db.Find("Missing"));
    }

    [Fact]
    public void A_damaged_list_is_null_and_a_missing_copy_falls_back_to_the_built_in_list()
    {
        Assert.Null(ModCompatDatabase.Parse("{ broken"));
        var fallback = ModCompatDatabase.LoadCached(Path.Combine(_directory, "missing.json"));
        Assert.True(fallback.Count > 300);
        File.WriteAllText(Path.Combine(_directory, "damaged.json"), "{ broken");
        Assert.True(ModCompatDatabase.LoadCached(Path.Combine(_directory, "damaged.json")).Count > 300);
    }

    [Fact]
    public void The_repositorys_list_reads_and_fills_the_fields()
    {
        var path = Path.Combine(RepoRoot(), "compat-list", "mods.json");
        var db = ModCompatDatabase.Parse(File.ReadAllText(path));
        Assert.NotNull(db);
        Assert.True(db.Count > 300);
        Assert.Equal(CommunityStatus.Broken, db.Find("NavalDLC")!.Status);
        Assert.Contains(db.All, e => e.Status == CommunityStatus.Unknown && !string.IsNullOrEmpty(e.Name));
        Assert.Contains(db.All, e => e.Url is { } u && u.StartsWith("https://", StringComparison.Ordinal));
        // The built in copy is this same file.
        Assert.Equal(db.Count, ModCompatDatabase.Embedded().Count);
    }

    [Fact]
    public void Workshop_and_Nexus_links_are_made()
    {
        var db = ModCompatDatabase.Parse("""
            { "mods": [
              { "id": "A", "name": "Mod A", "status": "Unknown", "workshop": "2859188632", "works": 2, "broken": 1 },
              { "id": "B", "status": "Works", "nexus": 2006 }
            ] }
            """)!;

        var a = db.Find("A")!;
        Assert.Equal("Mod A", a.DisplayName);
        Assert.Equal("https://steamcommunity.com/sharedfiles/filedetails/?id=2859188632", a.Url);
        Assert.Equal((2, 1), (a.Works, a.Broken));
        Assert.Equal("https://www.nexusmods.com/mountandblade2bannerlord/mods/2006", db.Find("B")!.Url);
        Assert.Equal("B", db.Find("B")!.DisplayName);
    }

    // --- Coodsz additions ---------------------------------------------

    [Theory]
    [InlineData("ImprovedGarrisons", CommunityStatus.Works)]
    [InlineData("CoopModPatch", CommunityStatus.Works)]
    [InlineData("PlayerSettlement", CommunityStatus.Works)]
    [InlineData("PSCoopBridge", CommunityStatus.Works)]
    [InlineData("XorberaxLegacy", CommunityStatus.ClientOnly)]
    [InlineData("Bannerlord.Harmony", CommunityStatus.Works)]
    [InlineData("BirthAndDeath", CommunityStatus.Broken)]
    [InlineData("FastMode", CommunityStatus.Broken)]
    public void The_Coodsz_notes_are_hand_kept_entries(string id, CommunityStatus status)
    {
        var entry = ModCompatDatabase.Embedded().Find(id)!;
        Assert.Equal(status, entry.Status);
        Assert.Equal("v0.1.6", entry.CoopVersion);
        Assert.False(entry.IdGuess);
        Assert.False(string.IsNullOrWhiteSpace(entry.Note));
    }

    [Fact]
    public void The_Coop_compatibility_thread_verdicts_resolve_with_their_notes()
    {
        var db = ModCompatDatabase.Embedded();

        // RBM: combat works, but the campaign economy breaks, so it is Broken.
        var rbm = CommunityCompat.Badge("RBM", db, workshopId: "2859251492")!;
        Assert.Equal(("Broken", "Broken"), (rbm.Kind, rbm.Text));
        Assert.Contains("campaign economy", rbm.Tip);
        Assert.Contains("Mistaken.", rbm.Tip);
        Assert.Contains("Checked by hand with Coop v0.1.6.", rbm.Tip);

        // Improved Garrisons works with issues, needs CoopModPatch, and the tooltip names the patch.
        var garrisons = CommunityCompat.Badge("ImprovedGarrisons", db)!;
        Assert.Equal("Works", garrisons.Kind);
        Assert.StartsWith("Works with issues. Needs CoopModPatch", garrisons.Tip);
        Assert.Contains("Patch available: CoopModPatch.", garrisons.Tip);

        // Better Time: the real module id (the list had a name guess) works, with its note in the tooltip.
        var time = db.Find("BetterTime")!;
        Assert.False(time.IdGuess);
        Assert.Equal("v0.1.6", time.CoopVersion);
        var timeBadge = CommunityCompat.Badge("BetterTime", db)!;
        Assert.Equal(("Works", "Works"), (timeBadge.Kind, timeBadge.Text));
        Assert.StartsWith("Works with issues. Keep the speed up setting low.", timeBadge.Tip);
        Assert.Contains("compatibility thread, 2026-10-08", timeBadge.Tip);

        // The patches name what they patch, where they are posted, and the load order.
        Assert.Contains("right after Diplomacy", db.Find("Diplomacy.CoopCompat")!.Note);
        Assert.Contains("Patch available: Diplomacy.CoopCompat.", db.Find("Bannerlord.Diplomacy")!.Note);
        var modPatch = db.Find("CoopModPatch")!.Note!;
        Assert.Contains("Improved Garrisons", modPatch);
        Assert.Contains("3786391685", modPatch);
        Assert.Contains("above the mods it patches", modPatch);
        Assert.Contains("Outdated", db.Find("RTSCamera_Coop_Fixed")!.Note);
        Assert.Equal(CommunityStatus.Broken, db.Find("BetterSmithingContinued")!.Status);
        Assert.Equal(CommunityStatus.Broken, db.Find("CoopMarriage")!.Status);

        // The thread only set verdicts; nobody's report counts changed.
        Assert.All(db.All, e => Assert.Equal((0, 0), (e.Works, e.Broken)));
    }

    [Fact]
    public void Each_mod_is_in_the_list_once()
    {
        var text = File.ReadAllText(Path.Combine(RepoRoot(), "compat-list", "mods.json"));
        var ids = Regex.Matches(text, "\\{ \"id\": \"(?<id>[^\"]+)\"").Select(m => ModCompatDatabase.Normalize(m.Groups["id"].Value)).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
        Assert.Equal(ids.Count, ModCompatDatabase.Embedded().Count);
    }

    [Fact]
    public void The_badge_shows_the_status_and_the_counts()
    {
        var db = ModCompatDatabase.Parse("""
            { "mods": [
              { "id": "Counted", "name": "Counted", "status": "Works", "works": 4, "broken": 1 },
              { "id": "Hand", "status": "Broken", "coop": "v0.1.6", "note": "Breaks sieges." }
            ] }
            """)!;

        var counted = CommunityCompat.Badge("Counted", db)!;
        Assert.Equal(("Works", "Works for 4 players"), (counted.Kind, counted.Text));
        Assert.Contains("Players reported: 4 say works, 1 say broken.", counted.Tip);
        Assert.EndsWith(CommunityCompat.ReportTip, counted.Tip);

        var hand = CommunityCompat.Badge("hand", db)!;
        Assert.Equal("Broken", hand.Kind);
        Assert.StartsWith("Breaks sieges.", hand.Tip);
        Assert.Contains("Checked by hand with Coop v0.1.6.", hand.Tip);

        var unknown = CommunityCompat.Badge("SomeNewMod", db)!;
        Assert.Equal(("Unknown", "Unknown, not tested"), (unknown.Kind, unknown.Text));
        Assert.Contains("Nobody has reported this mod yet.", unknown.Tip);
    }

    [Theory]
    [InlineData("Native")]
    [InlineData("SandBox")]
    [InlineData("Coop")]
    [InlineData("CoopNightly")]
    [InlineData("DedicatedServer.Windows")]
    [InlineData("")]
    public void Official_modules_and_Coop_get_no_badge(string id) =>
        Assert.Null(CommunityCompat.Badge(id, ModCompatDatabase.Embedded()));

    [Theory]
    [InlineData("NavalDLC")]
    [InlineData("BirthAndDeath")]
    [InlineData("FastMode")]
    public void The_modules_Coop_refuses_show_Broken_even_without_a_list(string id)
    {
        Assert.True(ServerModuleNotes.IsCoopRefused(id));
        var badge = CommunityCompat.Badge(id, ModCompatDatabase.Empty)!;
        Assert.Equal("Broken", badge.Kind);
        Assert.Contains("Turn it off", badge.Tip);
    }

    [Fact]
    public void Refusing_those_modules_does_not_change_the_join_and_dependency_rules()
    {
        // TAOM names BirthAndDeath as a dependency; it stays an official module the join
        // comparison skips and a soft dependency, so nothing is blocked by the note.
        Assert.True(CoopJoinRules.IsIgnored("BirthAndDeath"));
        Assert.True(CoopJoinRules.IsIgnored("NavalDLC"));
        Assert.False(ServerModuleNotes.IsCoopRefused("StoryMode"));
        Assert.False(ServerModuleNotes.IsCoopRefused(null));
    }

    [Fact]
    public void The_report_link_fills_the_issue_form_fields()
    {
        var url = CommunityCompat.ReportUrl("Bannerlord.Diplomacy", "Diplomacy", "v1.2.3", "v0.1.6", "v1.4.8");
        Assert.StartsWith("https://github.com/" + ModCompatDatabase.Repository + "/issues/new?template=mod-compat.yml", url);
        Assert.Contains("&mod_id=Bannerlord.Diplomacy", url);
        Assert.Contains("&mod_name=Diplomacy", url);
        Assert.Contains("&mod_version=v1.2.3", url);
        Assert.Contains("&coop_version=v0.1.6", url);
        Assert.Contains("&title=%5BMod%5D%20Bannerlord.Diplomacy", url);

        // Every field the link fills is an id in the form, and the form's labels are the
        // ones the tally script reads.
        var form = File.ReadAllText(Path.Combine(RepoRoot(), ".github", "ISSUE_TEMPLATE", "mod-compat.yml"));
        foreach (var field in Regex.Matches(url, "&(?<f>[a-z_]+)=").Select(m => m.Groups["f"].Value).Where(f => f != "title"))
            Assert.Contains("id: " + field, form);
        foreach (var label in new[] { "label: Mod id", "label: Mod name", "label: Result", "label: Coop version", "- works", "- broken", "- client-only" })
            Assert.Contains(label, form);
        var script = File.ReadAllText(Path.Combine(RepoRoot(), ".github", "scripts", "tally-mod-compat.mjs"));
        Assert.Contains("compat-list/mods.json", script);
        Assert.Contains("compat-list/mods.json", File.ReadAllText(Path.Combine(RepoRoot(), ".github", "workflows", "mod-compat-tally.yml")));
        Assert.EndsWith("/main/compat-list/mods.json", ModCompatDatabase.DefaultUrl);
    }

    [Fact]
    public async Task Offline_the_kept_copy_is_used()
    {
        var cache = Path.Combine(_directory, "kept.json");
        File.WriteAllText(cache, """{ "mods": [ { "id": "OnlyHere", "status": "Works" } ] }""");
        using var http = new HttpClient(new FailingHandler());
        var db = await ModCompatDatabase.FetchAsync(http, cache, "https://example.invalid/mods.json");
        Assert.Equal(CommunityStatus.Works, db.Find("OnlyHere")!.Status);
    }

    [Fact]
    public void The_host_row_tells_the_page_when_its_badge_changes()
    {
        var row = new CoodszHost.Services.ModuleRow { Id = "ImprovedGarrisons" };
        var changed = new List<string?>();
        row.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        row.Compat = CommunityCompat.Badge(row.Id, ModCompatDatabase.Embedded());
        Assert.Equal("Works", row.Compat!.Kind);
        Assert.Equal(["Compat"], changed);
    }

    private sealed class FailingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("offline");
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "BannerlordCoopLauncher.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
