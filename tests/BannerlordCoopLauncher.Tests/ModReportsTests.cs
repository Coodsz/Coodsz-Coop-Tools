using System.Net;
using System.Text.Json;
using BannerlordCoopLauncher.Core.Modules;

namespace BannerlordCoopLauncher.Tests;

/// <summary>One click community mod reports (ModReports, the report window wiring and the new list fields).</summary>
[Collection(ModReportsSession.Name)]
public sealed class ModReportsTests : IDisposable
{
    private const string Install = "0123456789abcdef0123456789abcdef";
    private const string TestEndpoint = "https://reports.example/report";
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "coodsz-reports-" + Guid.NewGuid().ToString("N"));

    public ModReportsTests() => Directory.CreateDirectory(_directory);

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private static ModReport Sample(IEnumerable<ModReportItem>? items = null, IEnumerable<ModReportMod>? mods = null) =>
        ModReports.Build(Install, "coop", "1.2.61", "v0.1.6", "v1.4.8",
            items ?? new[] { new ModReportItem { Id = "Bannerlord.Diplomacy", Name = "Diplomacy", Version = "v1.2.3", Result = ModReportItem.Works } },
            mods ?? new[] { new ModReportMod("Bannerlord.Harmony", "2.3"), new ModReportMod("Bannerlord.Diplomacy", "v1.2.3") });

    [Fact]
    public void The_report_carries_no_private_data()
    {
        var report = Sample(
            new[]
            {
                new ModReportItem { Id = "Bannerlord.Diplomacy", Name = @"C:\Users\Pat\Diplomacy", Version = "v1.2.3", Result = ModReportItem.Works },
                new ModReportItem { Id = @"D:\Steam\steamapps\workshop\content\261550\123", Result = ModReportItem.Broken },
                new ModReportItem { Id = "76561198012345678", Result = ModReportItem.Broken },
                new ModReportItem { Id = "Pat's Mod", Version = "/home/pat", Result = ModReportItem.Broken }
            },
            new[]
            {
                new ModReportMod("Bannerlord.Harmony", "2.3"),
                new ModReportMod(@"C:\Users\Pat\Documents\Mount and Blade II Bannerlord", "1"),
                new ModReportMod("SomeMod", "76561198012345678"),
                new ModReportMod("Native", "v1.4.8")
            });
        var text = ModReports.PreviewText(report);

        foreach (var forbidden in new[] { "Users", "Pat\\", "steamapps", "76561198012345678", "/home", ":\\" })
            Assert.DoesNotContain(forbidden, text);
        Assert.Equal(["Bannerlord.Diplomacy", "Pat's Mod"], report.Reports.Select(item => item.Id));
        Assert.Null(report.Reports[0].Name);
        Assert.Equal("", report.Reports[1].Version);
        Assert.Equal("", report.Mods.Single(mod => mod.Id == "SomeMod").Version);

        // Only these fields, nothing about the PC or the player.
        using var doc = JsonDocument.Parse(text);
        Assert.Equal(["kind", "schema", "install", "tool", "toolVersion", "coop", "game", "reports", "mods"],
            doc.RootElement.EnumerateObject().Select(p => p.Name));
        Assert.All(doc.RootElement.GetProperty("mods").EnumerateArray(), mod =>
            Assert.Equal(["id", "version"], mod.EnumerateObject().Select(p => p.Name)));
    }

    [Fact]
    public void The_mod_list_keeps_the_load_order_without_official_modules_or_repeats()
    {
        var list = ModReports.ModList(new (string, string?)[]
        {
            ("Native", "v1.4.8"), ("Bannerlord.Harmony", "2.3"), ("Coop", "v0.1.6"), ("Bannerlord.Diplomacy", "v1.2.3"),
            ("DedicatedServer.CoodszJoinPassword", "1"), ("SandBox", "v1.4.8")
        });
        Assert.Equal(["Bannerlord.Harmony", "Coop", "Bannerlord.Diplomacy"], list.Select(mod => mod.Id));

        var report = Sample(mods: list.Concat(list));
        Assert.Equal(3, report.Mods.Count);
    }

    [Fact]
    public void Works_only_with_needs_another_valid_mod_or_becomes_works()
    {
        var report = Sample(new[]
        {
            new ModReportItem { Id = "A", Result = ModReportItem.WorksWith, With = "B" },
            new ModReportItem { Id = "C", Result = ModReportItem.WorksWith, With = @"C:\x" },
            new ModReportItem { Id = "D", Result = ModReportItem.WorksWith, With = "d" },
            new ModReportItem { Id = "E", Result = "maybe" },
            new ModReportItem { Id = "a", Result = ModReportItem.Broken }
        });
        Assert.Equal([("A", "works-with", "B"), ("C", "works", null), ("D", "works", null)],
            report.Reports.Select(item => (item.Id, item.Result, item.With)));
    }

    [Fact]
    public void One_vote_per_install_and_mod_version()
    {
        var settings = new ModReportSettings();
        var works = new ModReportItem { Id = "Bannerlord.Diplomacy", Version = "v1.2.3", Result = ModReportItem.Works };
        Assert.Single(ModReports.NotSentYet(settings, new[] { works }, "coop"));
        ModReports.RememberSent(settings, new[] { works }, "coop");

        // The same vote again is not sent; another result or another version is.
        Assert.Empty(ModReports.NotSentYet(settings, new[] { new ModReportItem { Id = "bannerlord.diplomacy", Version = "v1.2.3", Result = ModReportItem.Works } }, "coop"));
        var broken = new ModReportItem { Id = "Bannerlord.Diplomacy", Version = "v1.2.3", Result = ModReportItem.Broken };
        Assert.Single(ModReports.NotSentYet(settings, new[] { broken }, "coop"));
        Assert.Single(ModReports.NotSentYet(settings, new[] { new ModReportItem { Id = "Bannerlord.Diplomacy", Version = "v1.3.0", Result = ModReportItem.Works } }, "coop"));

        // A changed vote replaces the old one: still one line for that version.
        ModReports.RememberSent(settings, new[] { broken }, "coop");
        Assert.Single(settings.Sent);
        Assert.Equal(ModReportItem.Broken, ModReports.SentFor(settings, "Bannerlord.Diplomacy", "v1.2.3", "coop")!.Result);
        Assert.Null(ModReports.SentFor(settings, "Bannerlord.Diplomacy", "v1.3.0", "coop"));

        Assert.Equal(ModReports.VoteKey(Install, "Bannerlord.Diplomacy", "V1.2.3"), ModReports.VoteKey(Install, "bannerlord diplomacy", "v1.2.3"));
        Assert.NotEqual(ModReports.VoteKey(Install, "Bannerlord.Diplomacy", "v1.2.3"), ModReports.VoteKey(Install, "Bannerlord.Diplomacy", "v1.2.4"));
    }

    [Fact]
    public void Each_tool_keeps_its_own_vote_and_old_votes_count_for_every_tool()
    {
        // The worker keeps one vote per tool (voteKey in server/mod-reports-worker), so a TAOM vote for the same mod
        // version must not stop the Realm of Thrones one.
        var settings = new ModReportSettings();
        var works = new ModReportItem { Id = "Bannerlord.Diplomacy", Version = "v1.2.3", Result = ModReportItem.Works };
        ModReports.RememberSent(settings, new[] { works }, "taom");
        Assert.Single(ModReports.NotSentYet(settings, new[] { works }, "rot"));
        Assert.Null(ModReports.SentFor(settings, "Bannerlord.Diplomacy", "v1.2.3", "rot"));
        ModReports.RememberSent(settings, new[] { new ModReportItem { Id = "Bannerlord.Diplomacy", Version = "v1.2.3", Result = ModReportItem.Broken } }, "rot");
        Assert.Equal(2, settings.Sent.Count);
        Assert.Equal(ModReportItem.Works, ModReports.SentFor(settings, "Bannerlord.Diplomacy", "v1.2.3", "taom")!.Result);
        Assert.Equal(ModReportItem.Broken, ModReports.SentFor(settings, "Bannerlord.Diplomacy", "v1.2.3", "rot")!.Result);

        // A vote saved before votes had a tool still loads and counts for every tool, so nothing is sent again,
        // until a tool sends its own vote for that version.
        var path = Path.Combine(Path.GetTempPath(), "mod-reports-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllText(path, "{ \"kind\": \"CoodszModReportSettings\", \"sent\": [ { \"id\": \"OldMod\", \"version\": \"v1.0.0\", \"result\": \"works\" } ] }");
            var old = ModReports.LoadSettings(path);
            var again = new ModReportItem { Id = "OldMod", Version = "v1.0.0", Result = ModReportItem.Works };
            Assert.All(new[] { "coop", "taom", "rot" }, tool => Assert.Empty(ModReports.NotSentYet(old, new[] { again }, tool)));
            ModReports.RememberSent(old, new[] { new ModReportItem { Id = "OldMod", Version = "v1.0.0", Result = ModReportItem.Broken } }, "rot");
            Assert.Equal(ModReportItem.Broken, ModReports.SentFor(old, "OldMod", "v1.0.0", "rot")!.Result);
            Assert.Single(ModReports.NotSentYet(old, new[] { again }, "rot"));
            Assert.Equal(ModReportItem.Works, ModReports.SentFor(old, "OldMod", "v1.0.0", "taom")!.Result);
        }
        finally
        {
            File.Delete(path);
        }

        // The tool stays out of the report itself; the worker knows it from the report's own tool field.
        var report = ModReports.Build(Install, "rot", "v0.1.0", "v0.1.8", "v1.4.8", new[] { works }, new List<ModReportMod>());
        Assert.All(report.Reports, item => Assert.Null(item.Tool));
    }

    [Fact]
    public void The_install_id_is_random_and_kept()
    {
        var path = Path.Combine(_directory, "mod-reports.json");
        var settings = ModReports.LoadSettings(path);
        var id = ModReports.InstallId(settings);
        Assert.Matches("^[0-9a-f]{32}$", id);
        Assert.Equal(id, ModReports.InstallId(settings));
        Assert.True(ModReports.SaveSettings(settings, path));
        Assert.Equal(id, ModReports.InstallId(ModReports.LoadSettings(path)));
        Assert.NotEqual(id, ModReports.InstallId(new ModReportSettings()));
    }

    [Fact]
    public void Without_an_endpoint_the_tools_fall_back_to_the_GitHub_form()
    {
        Assert.False(ModReports.HasEndpoint(""));
        Assert.False(ModReports.HasEndpoint("http://reports.example/report"));
        Assert.False(ModReports.HasEndpoint("not a url"));
        Assert.True(ModReports.HasEndpoint(TestEndpoint));
        Assert.Equal(CommunityCompat.ReportTip, ModReports.ReportTip(""));
        Assert.Equal(ModReports.ClickTip, ModReports.ReportTip(TestEndpoint));
        Assert.Equal(ModReports.HasEndpoint() ? ModReports.ClickTip : CommunityCompat.ReportTip, ModReports.ReportTip());

        // The clients open the report window only with an endpoint; otherwise the GitHub form as before.
        var friends = SourceFiles.Method(SourceFiles.Window(RepoRoot(), "BannerlordCoopLauncher"), "private void CompatBadge_Click(");
        Assert.True(friends.IndexOf("ModReports.HasEndpoint()", StringComparison.Ordinal) < friends.IndexOf("CommunityCompat.ReportUrl(", StringComparison.Ordinal));
        Assert.Contains("ModReportWindow.Show(", friends);
        var window = File.ReadAllText(Path.Combine(RepoRoot(), "src", "BannerlordCoopLauncher", "ModReportWindow.cs"));
        Assert.Contains("if (!ModReports.HasEndpoint() || request.Rate.Count == 0)", window);
        var taom = SourceFiles.Window(RepoRoot(), "TaomCoopLauncher");
        Assert.Contains("var report = BannerlordCoopLauncher.Core.Modules.ModReports.HasEndpoint();", taom);
        Assert.Contains("if (report)", taom);
    }

    [Fact]
    public async Task Sending_without_an_endpoint_never_touches_the_network()
    {
        var handler = new Recorder(HttpStatusCode.OK);
        using var http = new HttpClient(handler);
        var result = await ModReports.SendAsync(http, Sample(), "");
        Assert.Equal(ModReportSendStatus.Failed, result.Status);
        Assert.Empty(handler.Bodies);
    }

    [Fact]
    public async Task Sending_posts_exactly_the_preview_text()
    {
        var handler = new Recorder(HttpStatusCode.OK);
        using var http = new HttpClient(handler);
        var report = Sample();
        var result = await ModReports.SendAsync(http, report, TestEndpoint);
        Assert.Equal(ModReportSendStatus.Sent, result.Status);
        Assert.Equal(TestEndpoint, handler.Urls.Single());
        Assert.Equal(JsonDocument.Parse(ModReports.PreviewText(report)).RootElement.ToString(),
            JsonDocument.Parse(handler.Bodies.Single()).RootElement.ToString());

        using var limited = new HttpClient(new Recorder(HttpStatusCode.TooManyRequests));
        Assert.Equal(ModReportSendStatus.RateLimited, (await ModReports.SendAsync(limited, report, TestEndpoint)).Status);
        using var refused = new HttpClient(new Recorder(HttpStatusCode.BadRequest));
        Assert.Equal(ModReportSendStatus.Refused, (await ModReports.SendAsync(refused, report, TestEndpoint)).Status);
        using var offline = new HttpClient(new Recorder(null));
        Assert.Equal(ModReportSendStatus.Failed, (await ModReports.SendAsync(offline, report, TestEndpoint)).Status);
    }

    [Fact]
    public void The_prompt_after_a_game_is_off_by_default_and_opens_once_per_run()
    {
        Assert.False(new ModReportSettings().AskAfterGame);
        Assert.False(ModReports.LoadSettings(Path.Combine(_directory, "missing.json")).AskAfterGame);

        ModReports.ResetSession();
        var off = new ModReportSettings();
        Assert.False(ModReports.ShouldAskAfterGame(off, 3, TestEndpoint));

        var on = new ModReportSettings { AskAfterGame = true };
        Assert.False(ModReports.ShouldAskAfterGame(on, 3, ""), "no endpoint, no prompt");
        Assert.False(ModReports.ShouldAskAfterGame(on, 0, TestEndpoint), "no mods to rate, no prompt");
        Assert.True(ModReports.ShouldAskAfterGame(on, 3, TestEndpoint));
        ModReports.MarkAsked();
        Assert.False(ModReports.ShouldAskAfterGame(on, 3, TestEndpoint), "never twice in one run");
        ModReports.ResetSession();

        // Both clients offer it after a joined game, closed or crashed, through the report bar (ModReports.AfterGame).
        Assert.Contains("_reportBar?.AfterGame(", SourceFiles.Method(SourceFiles.Window(RepoRoot(), "BannerlordCoopLauncher"), "private async Task RecordSessionAsync("));
        Assert.Contains("_reportBar?.AfterGame(", SourceFiles.Method(SourceFiles.Window(RepoRoot(), "TaomCoopLauncher"), "private async Task RecordSessionAsync("));
    }

    [Fact]
    public void The_badge_shows_players_conflicts_and_mod_lists_and_old_lists_still_read()
    {
        var db = ModCompatDatabase.Parse("""
            { "mods": [
              { "id": "A", "name": "Mod A", "status": "Works", "works": 12, "broken": 2, "testedWith": ["B", "Bannerlord.Harmony"], "conflicts": ["X"], "needs": ["B"] },
              { "id": "B", "name": "Mod B", "status": "Works", "works": 1, "broken": 0 },
              { "id": "X", "name": "Mod X", "status": "Broken", "works": 0, "broken": 5 },
              { "id": "Old", "status": "Works" }
            ] }
            """)!;

        var a = CommunityCompat.Badge("A", db, reportTip: ModReports.ClickTip)!;
        Assert.Equal("Works for 12 players, conflicts with Mod X", a.Text);
        Assert.Contains("Works only with Mod B.", a.Tip);
        Assert.Contains("Broken together with Mod X.", a.Tip);
        Assert.Contains("Tested with Mod B, Bannerlord.Harmony.", a.Tip);
        Assert.EndsWith(ModReports.ClickTip, a.Tip);
        Assert.True(CommunityCompat.IsReportTip(ModReports.ClickTip));

        Assert.Equal("Works for 1 player", CommunityCompat.Badge("B", db)!.Text);
        Assert.Equal("Broken for 5 players", CommunityCompat.Badge("X", db)!.Text);
        var old = CommunityCompat.Badge("Old", db)!;
        Assert.Equal("Works", old.Text);
        Assert.EndsWith(CommunityCompat.ReportTip, old.Tip);
        Assert.Null(db.Find("Old")!.TestedWith);

        // The built in list (no new fields yet) still reads.
        Assert.True(ModCompatDatabase.Embedded().Count > 0);
    }

    private sealed class Recorder(HttpStatusCode? status) : HttpMessageHandler
    {
        public List<string> Bodies { get; } = new();
        public List<string> Urls { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (status == null)
                throw new HttpRequestException("offline");
            Urls.Add(request.RequestUri!.ToString());
            Bodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
            return new HttpResponseMessage(status.Value) { Content = new StringContent("{\"ok\":true}") };
        }
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "BannerlordCoopLauncher.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
