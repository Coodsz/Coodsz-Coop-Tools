using System.Net;
using System.Text;
using System.Xml.Linq;
using BannerlordCoopLauncher.Core.Support;

namespace BannerlordCoopLauncher.Tests;

// Coodsz's notice from compat-list/announcement.json (Coodsz is on vacation): which tools show it, in which language,
// what Dismiss keeps, and that every tool window has it at the top of its notices.
public sealed class AnnouncementTests : IDisposable
{
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";
    private static readonly XNamespace Ui = "clr-namespace:Coodsz.Ui";
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "coodsz-announcement-" + Guid.NewGuid().ToString("N"));

    public AnnouncementTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); } catch (IOException) { }
    }

    private static string Json(bool enabled = true, int schema = 1, string tools = "\"coop\", \"taom\"", string id = "vacation-2026-11") =>
        "{ \"schema\": " + schema + ", \"id\": \"" + id + "\", \"enabled\": " + (enabled ? "true" : "false") + ", \"tools\": [" + tools + "],"
        + " \"title\": { \"en\": \"Coodsz is on vacation\", \"fr\": \"Coodsz est en vacances\", \"de\": \" \" },"
        + " \"text\": { \"en\": \"Click Send my logs.\", \"fr\": \"Cliquez sur Envoyer mes journaux.\" } }";

    [Fact]
    public void A_valid_file_shows_for_its_families_only()
    {
        var file = AnnouncementFile.Parse(Json())!;
        var shown = file.For("coop", "en");
        Assert.Equal(new AnnouncementText("vacation-2026-11", "Coodsz is on vacation", "Click Send my logs."), shown);
        Assert.NotNull(file.For("TAOM", "en"));
        Assert.Null(file.For("be", "en"));
        Assert.Null(file.For("ad1259", "en"));
        // A file saved with a BOM reads the same.
        Assert.Equal(shown, AnnouncementFile.Parse("\uFEFF" + Json())!.For("coop", "en"));
    }

    [Fact]
    public void Off_a_newer_schema_bad_json_or_missing_words_show_nothing()
    {
        Assert.Null(AnnouncementFile.Parse(Json(enabled: false))!.For("coop", "en"));
        Assert.Null(AnnouncementFile.Parse(Json(schema: 2)));
        Assert.Null(AnnouncementFile.Parse("{ \"schema\": 1, \"id\": "));
        Assert.Null(AnnouncementFile.Parse("[1, 2]"));
        Assert.Null(AnnouncementFile.Parse(""));
        Assert.Null(AnnouncementFile.Parse(Json(tools: ""))!.For("coop", "en"));
        Assert.Null(AnnouncementFile.Parse(Json(id: " "))!.For("coop", "en"));
        Assert.Null(AnnouncementFile.Parse("{ \"schema\": 1, \"id\": \"a\", \"enabled\": true, \"tools\": [\"coop\"], \"title\": { \"en\": \"Hi\" } }")!.For("coop", "en"));
        Assert.Null(AnnouncementFile.Parse("{ \"schema\": 1, \"id\": \"a\", \"enabled\": true, \"tools\": [\"coop\"] }")!.For("coop", "en"));
    }

    [Fact]
    public void A_file_over_64_KB_is_not_read()
    {
        var big = Json().Replace("Click Send my logs.", new string('a', AnnouncementFile.MaxBytes));
        Assert.Null(AnnouncementFile.Parse(big));
    }

    [Fact]
    public void The_words_follow_the_language_and_fall_back_to_English()
    {
        var file = AnnouncementFile.Parse(Json())!;
        var french = file.For("coop", "fr")!;
        Assert.Equal("Coodsz est en vacances", french.Title);
        Assert.Equal("Cliquez sur Envoyer mes journaux.", french.Text);
        // A blank German title and no German text: both English.
        var german = file.For("coop", "de")!;
        Assert.Equal("Coodsz is on vacation", german.Title);
        Assert.Equal("Click Send my logs.", german.Text);
        Assert.Equal("Coodsz is on vacation", file.For("coop", null)!.Title);
    }

    [Fact]
    public void Dismiss_holds_until_the_id_or_the_words_change()
    {
        var path = Path.Combine(_directory, "tool", "announcement-dismissed-coop-client.txt");
        var shown = new AnnouncementText("vacation-2026-11", "Coodsz is on vacation", "Click Send my logs.");
        Assert.False(AnnouncementDismissals.IsDismissed(path, shown));
        AnnouncementDismissals.Dismiss(path, shown);
        AnnouncementDismissals.Dismiss(path, shown);
        Assert.True(AnnouncementDismissals.IsDismissed(path, shown));
        Assert.Single(File.ReadAllLines(path));
        Assert.False(AnnouncementDismissals.IsDismissed(path, shown with { Text = "Click Send my logs. Back on Monday." }));
        Assert.False(AnnouncementDismissals.IsDismissed(path, shown with { Id = "vacation-2026-12" }));
        Assert.NotEqual(shown.DismissKey, (shown with { Title = "Coodsz is back" }).DismissKey);
        Assert.StartsWith("vacation-2026-11:", shown.DismissKey);
    }

    [Fact]
    public async Task The_download_keeps_a_copy_and_a_failed_one_uses_it()
    {
        var cache = Path.Combine(_directory, "announcement.json");
        Assert.Null(AnnouncementFile.LoadCached(cache));
        using (var http = new HttpClient(new Answer(HttpStatusCode.OK, Json())))
            Assert.NotNull((await AnnouncementFile.FetchAsync(http, cache, "https://example.invalid/a.json"))!.For("coop", "en"));
        Assert.NotNull(AnnouncementFile.LoadCached(cache)!.For("coop", "en"));

        // Offline, a server error, a damaged or a too big answer: the kept copy, untouched.
        using (var http = new HttpClient(new Failing()))
            Assert.NotNull(await AnnouncementFile.FetchAsync(http, cache, "https://example.invalid/a.json"));
        foreach (var (status, body) in new[] { (HttpStatusCode.InternalServerError, ""), (HttpStatusCode.OK, "{ broken"), (HttpStatusCode.OK, new string(' ', AnnouncementFile.MaxBytes + 1)) })
        {
            using var http = new HttpClient(new Answer(status, body));
            Assert.NotNull((await AnnouncementFile.FetchAsync(http, cache, "https://example.invalid/a.json"))!.For("coop", "en"));
        }
        Assert.NotNull(AnnouncementFile.LoadCached(cache)!.For("coop", "en"));

        // Turned off on GitHub: the copy says so too.
        using (var http = new HttpClient(new Answer(HttpStatusCode.OK, Json(enabled: false))))
            Assert.Null((await AnnouncementFile.FetchAsync(http, cache, "https://example.invalid/a.json"))!.For("coop", "en"));
        Assert.Null(AnnouncementFile.LoadCached(cache)!.For("coop", "en"));

        // Deleted on GitHub: the copy goes too.
        using (var http = new HttpClient(new Answer(HttpStatusCode.OK, Json())))
            await AnnouncementFile.FetchAsync(http, cache, "https://example.invalid/a.json");
        using (var http = new HttpClient(new Answer(HttpStatusCode.NotFound, "404: Not Found")))
            Assert.Null(await AnnouncementFile.FetchAsync(http, cache, "https://example.invalid/a.json"));
        Assert.False(File.Exists(cache));

        File.WriteAllText(cache, "{ damaged");
        Assert.Null(AnnouncementFile.LoadCached(cache));
    }

    [Fact]
    public void The_published_file_is_the_vacation_notice_in_every_language()
    {
        var file = AnnouncementFile.Parse(SourceFiles.Read(Path.Combine("compat-list", "announcement.json")))!;
        Assert.Equal(AnnouncementFile.DefaultUrl, "https://raw.githubusercontent.com/Coodsz/Coodsz-Coop-and-Friends/main/compat-list/announcement.json");
        Assert.Equal(new[] { "coop", "taom", "ad1259", "be" }, file.Tools);
        var english = file.For("coop", "en")!;
        Assert.Equal("Coodsz is on vacation", english.Title);
        Assert.Equal("If you have a question, a concern or a problem, click Send my logs. A bot answers while Coodsz is away, and its reply shows in My reports.", english.Text);
        Assert.DoesNotContain(':', english.Title + english.Text);
        foreach (var language in new[] { "ar", "de", "fr", "pt", "ru", "tr" })
        {
            var entries = BannerlordCoopLauncher.Core.Localization.Translator.Entries(language);
            var text = file.Text![language];
            Assert.False(string.IsNullOrWhiteSpace(file.Title![language]), language);
            // The buttons are named as that language's tool shows them.
            Assert.Contains(entries["Send my logs"], text);
            Assert.Contains(entries["My reports"], text);
        }
    }

    public static TheoryData<string, string, string> Windows => new()
    {
        { "CoodszHost", "CrashTool", "coop or ad1259 by Edition" },
        { "BannerlordCoopLauncher", "\"coop\"", "Coop client" },
        { "AD1259Launcher", "\"ad1259\"", "AD1259 client" },
        { "TaomCoopHost", "\"taom\"", "TAOM server" },
        { "TaomCoopLauncher", "\"taom\"", "TAOM client" },
        { "BurningEmpiresHost", "\"be\"", "BE server" },
        { "BurningEmpiresLauncher", "\"be\"", "BE client" },
    };

    [Theory]
    [MemberData(nameof(Windows))]
    public void Every_tool_window_shows_the_notice_above_the_pages_after_the_crash_report_and_reply_bars(string project, string family, string what)
    {
        var xaml = XDocument.Parse(SourceFiles.Read(Path.Combine("src", project, "MainWindow.xaml")));
        var banners = xaml.Descendants().Single(e => (string?)e.Attribute(X + "Name") == "Banners");
        var banner = xaml.Descendants(Ui + "Banner").Single(e => (string?)e.Attribute(X + "Name") == "AnnouncementBanner");
        Assert.Same(banners, banner.Parent);
        Assert.Equal("True", (string?)banner.Attribute("CanDismiss"));
        Assert.Equal("Warn", (string?)banner.Attribute("Kind"));
        Assert.Equal("Collapsed", (string?)banner.Attribute("Visibility"));
        // It asks players to send reports, so the crash, report and reply bars (and LogAlert, SaveToNewNameBar) come
        // before it. It comes before the rest: a tie keeps file order, and it is the first child.
        int Priority(XElement e) => (int)e.Attribute(Ui + "BannerStack.Priority")!;
        string? Name(XElement e) => (string?)e.Attribute(X + "Name");
        var priority = Priority(banner);
        Assert.Same(banner, banners.Elements().First());
        var bars = banners.Elements().Where(e => Name(e) is "CrashBarHost" or "ReportBarHost" or "ReplyBannerHost").ToList();
        Assert.Equal(2, bars.Count);
        var urgent = banners.Elements().Where(e => Name(e) is "LogAlert" or "SaveToNewNameBar").Concat(bars).ToList();
        Assert.All(urgent, e => Assert.True(Priority(e) < priority, what + ": " + Name(e)));
        Assert.All(banners.Elements().Where(e => e != banner && !urgent.Contains(e)), e => Assert.True(Priority(e) >= priority, what + ": " + Name(e)));
        // Above the page grid, not inside a page.
        Assert.Equal("Top", (string?)banners.Attribute(XName.Get("DockPanel.Dock")));
        Assert.Equal("Grid", banners.ElementsAfterSelf().First().Name.LocalName);

        var setUp = SourceFiles.Method(SourceFiles.Window(SourceFiles.Root(), project), "private void SetUpCrashReports()");
        Assert.Contains("Coodsz.Ui.AnnouncementUi.Attach(this, AnnouncementBanner, " + family + ");", setUp);
        if (project == "CoodszHost")
            Assert.Contains("CrashTool => Edition.IsAD1259 ? \"ad1259\" : \"coop\";", SourceFiles.Window(SourceFiles.Root(), project));
    }

    [Fact]
    public void The_shared_wiring_shows_the_kept_copy_then_the_download_and_remembers_Dismiss()
    {
        var ui = SourceFiles.Read(Path.Combine("src", "CoodszHost", "Shared", "Frame", "AnnouncementUi.cs"));
        Assert.Contains("AnnouncementFile.LoadCached()", ui);
        Assert.Contains("Task.Run(", ui);
        Assert.Contains("AnnouncementFile.FetchForWindowAsync(", ui);
        Assert.Contains("AnnouncementDismissals.Dismiss(path, shown)", ui);
        Assert.Contains("Translator.Changed += LanguageChanged;", ui);
        Assert.Contains("Translator.Changed -= LanguageChanged", ui);
        var core = SourceFiles.Read(Path.Combine("src", "BannerlordCoopLauncher.Core", "Support", "Announcement.cs"));
        Assert.Contains("Timeout = TimeSpan.FromSeconds(6)", core);
    }

    private sealed class Answer(HttpStatusCode status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8) });
    }

    private sealed class Failing : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("offline");
    }
}
