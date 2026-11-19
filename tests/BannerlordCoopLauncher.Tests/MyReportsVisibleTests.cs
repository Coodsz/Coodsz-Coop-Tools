using System.Reflection;
using BannerlordCoopLauncher.Core.Localization;
using BannerlordCoopLauncher.Core.Support;
using BannerlordCoopLauncher.Core.Updates;

namespace BannerlordCoopLauncher.Tests;

// My reports made easy to notice (Coodsz, 2026-10-30: the small link was "so freaking small"): a real button
// with a bigger red count, a gold glow while an answer is unread, and a banner at the top until every answer is read.
[Collection(TranslatorLanguage.Name)]
public sealed class MyReportsVisibleTests
{
    private static ReportAnswer Answer(string code, string at, bool read = false, string source = AnswerSource.Reply, string id = "1") =>
        new() { Code = code, Id = id, At = at, SavedAt = DateTime.Parse(at, null, System.Globalization.DateTimeStyles.AdjustToUniversal), Source = source, Text = "Steps.", Read = read };

    // ----- The button -----

    [Fact]
    public void The_button_shows_once_there_is_a_report_and_glows_with_a_count_only_while_an_answer_is_unread()
    {
        Assert.Equal(new MyReportsLook(false, false, null), ReportReplies.Look(0, 0));
        Assert.Equal(new MyReportsLook(true, false, null), ReportReplies.Look(4, 0));
        Assert.Equal(new MyReportsLook(true, true, "1"), ReportReplies.Look(4, 1));
        Assert.Equal(new MyReportsLook(true, true, "12"), ReportReplies.Look(20, 12));
        // The pill stays small enough to sit on the button.
        Assert.Equal("99", ReportReplies.Look(150, 99).Badge);
        Assert.Equal("99+", ReportReplies.Look(150, 100).Badge);
    }

    [Fact]
    public void The_glow_stops_when_every_answer_is_read()
    {
        var answers = new List<ReportAnswer> { Answer("CR-ABCDE", "2026-10-30T10:00:00Z"), Answer("CR-FGHJK", "2026-10-30T11:00:00Z") };
        Assert.True(ReportReplies.Look(2, ReportReplies.UnreadReports(answers)).Glow);
        answers[0].Read = true;
        Assert.True(ReportReplies.Look(2, ReportReplies.UnreadReports(answers)).Glow);
        answers[1].Read = true;
        var look = ReportReplies.Look(2, ReportReplies.UnreadReports(answers));
        Assert.False(look.Glow);
        Assert.Null(look.Badge);
        Assert.True(look.Visible);
    }

    // ----- The banner -----

    [Fact]
    public void No_banner_when_every_answer_is_read_or_there_is_none()
    {
        Assert.Null(ReportReplies.Banner([]));
        Assert.Null(ReportReplies.Banner([Answer("CR-ABCDE", "2026-10-30T10:00:00Z", read: true)]));
    }

    [Fact]
    public void The_banner_names_the_newest_unread_report_and_counts_the_other_reports()
    {
        var answers = new[]
        {
            Answer("CR-ABCDE", "2026-10-29T10:00:00Z"),
            Answer("CR-FGHJK", "2026-10-30T12:00:00Z"),
            // A second answer to the same report counts once.
            Answer("CR-ABCDE", "2026-10-30T09:00:00Z", id: "2"),
            // A read answer is not counted, even when it is the newest.
            Answer("CR-MNPQR", "2026-10-30T13:00:00Z", read: true)
        };
        Assert.Equal(new ReplyBanner("CR-FGHJK", false, 1), ReportReplies.Banner(answers));
        Assert.Equal("Coodsz answered your report CR-FGHJK and 1 more", ReportReplies.BannerText(ReportReplies.Banner(answers)!));

        // Reading the named report moves the banner to the next one, and the and N more goes.
        answers[1].Read = true;
        Assert.Equal(new ReplyBanner("CR-ABCDE", false, 0), ReportReplies.Banner(answers));
        Assert.Equal("Coodsz answered your report CR-ABCDE", ReportReplies.BannerText(ReportReplies.Banner(answers)!));
        answers[0].Read = true;
        answers[2].Read = true;
        Assert.Null(ReportReplies.Banner(answers));
    }

    [Fact]
    public void An_automatic_answer_says_automatic_answer()
    {
        var auto = ReportReplies.Banner([Answer("CR-ABCDE", "2026-10-30T10:00:00Z", source: AnswerSource.Auto)])!;
        Assert.True(auto.Auto);
        Assert.Equal("Automatic answer to your report CR-ABCDE", ReportReplies.BannerText(auto));
        Assert.Equal("Automatic answer to your report CR-ABCDE and 3 more", ReportReplies.BannerText(auto with { More = 3 }));

        // A known fix is automatic too; one shown before the report was sent has no code.
        var known = ReportReplies.Banner([Answer("", "2026-10-30T10:00:00Z", source: AnswerSource.Known, id: "known-mcm")])!;
        Assert.Equal(new ReplyBanner("", true, 0), known);
        Assert.Equal("Automatic answer to your crash", ReportReplies.BannerText(known));
        Assert.Equal("Automatic answer to your crash and 2 more", ReportReplies.BannerText(known with { More = 2 }));

        // The newest answer decides: a newer reply from Coodsz says Coodsz answered.
        var mixed = ReportReplies.Banner([Answer("CR-ABCDE", "2026-10-30T10:00:00Z", source: AnswerSource.Auto), Answer("CR-FGHJK", "2026-10-30T11:00:00Z")])!;
        Assert.Equal("Coodsz answered your report CR-FGHJK and 1 more", ReportReplies.BannerText(mixed));
    }

    // The window's text hook translates the English line: the and {1} more template must win over the short one,
    // and the code must come through as it is.
    [Theory]
    [InlineData("de")]
    [InlineData("ar")]
    [InlineData("fr")]
    [InlineData("pt")]
    [InlineData("ru")]
    [InlineData("tr")]
    public void The_banner_is_translated_with_the_code_and_the_count(string language)
    {
        var before = Translator.Current;
        try
        {
            Translator.Use(language);
            foreach (var banner in new[]
                     {
                         new ReplyBanner("CR-ABCDE", false, 0), new ReplyBanner("CR-ABCDE", false, 7), new ReplyBanner("CR-ABCDE", true, 0),
                         new ReplyBanner("CR-ABCDE", true, 7), new ReplyBanner("", true, 0), new ReplyBanner("", true, 7)
                     })
            {
                var english = ReportReplies.BannerText(banner);
                var shown = Translator.T(english);
                Assert.NotEqual(english, shown);
                foreach (var phrase in new[] { "answered", "Automatic", "your report", "your crash", "more" })
                    Assert.DoesNotContain(phrase, shown);
                if (banner.Code.Length > 0)
                    Assert.Contains("CR-ABCDE", shown);
                if (banner.More > 0)
                    Assert.Contains("7", shown);
                else
                    Assert.DoesNotContain("7", shown);
            }
        }
        finally
        {
            Translator.Use(before);
        }
    }

    // ----- Wiring -----

    [Fact]
    public void Every_tool_has_the_banner_slot_and_hands_it_to_my_reports()
    {
        foreach (var project in new[] { "BannerlordCoopLauncher", "TaomCoopLauncher", "AD1259Launcher", "BurningEmpiresLauncher", "CoodszHost", "TaomCoopHost", "BurningEmpiresHost" })
        {
            var xaml = SourceFiles.Read(Path.Combine("src", project, "MainWindow.xaml"));
            Assert.Contains("<ContentControl x:Name=\"ReplyBannerHost\"", xaml);
            // Above the other bars, so it is the first thing at the top.
            Assert.True(xaml.IndexOf("x:Name=\"ReplyBannerHost\"", StringComparison.Ordinal) < xaml.IndexOf(project.EndsWith("Host") ? "x:Name=\"CrashBarHost\"" : "x:Name=\"ReportBarHost\"", StringComparison.Ordinal), project);
            Assert.Contains("bannerHost: ReplyBannerHost);", SourceFiles.Window(SourceFiles.Root(), project));
        }
    }

    [Fact]
    public void The_button_is_the_send_my_logs_size_and_the_banner_has_no_x()
    {
        var window = SourceFiles.Read(Path.Combine("src", "BannerlordCoopLauncher", "ReportReplyWindow.cs"));
        // Send my logs is ModReportUi.Filled with its default size and padding; My reports is the same call.
        Assert.Contains("_button = ModReportUi.Filled(owner, ReportReplyText.MyReports, ReportReplyText.MyReportsTip, _fill, \"📬\");", window);
        Assert.Contains("ModReportUi.Filled(scope, SendLogsText, SendLogsTip, Gold(scope), \"📤\");", SourceFiles.Read(Path.Combine("src", "BannerlordCoopLauncher", "CrashReportWindow.cs")));
        // A still glow: no animation running while the tool is open.
        Assert.DoesNotContain("Storyboard", window);
        Assert.DoesNotContain("Animation", window);
        // The banner frame gets no close, so it has no x that hides it for good.
        Assert.Contains("new UIElement[] { open }, null, null);", window);
        Assert.Contains("MyReportsWindow.Open(_owner, _tools, banner.Code)", window);
        var frame = SourceFiles.Method(SourceFiles.Read(Path.Combine("src", "BannerlordCoopLauncher", "ModReportWindow.cs")), "public static Border Frame(");
        Assert.Contains("var x = close == null ? null : new Button", frame);
    }

    // The button is taller than the old link: the Coop and AD1259 Server Tool's page buttons scroll at their least
    // height instead of being squeezed (a 720 px window lost all of them before, the button clipped them at 900).
    [Fact]
    public void The_server_tool_page_buttons_scroll_instead_of_being_squeezed()
    {
        // Since the redesign (wave 1) the page buttons are a list at the top of NavScroll, each at least 36 px (Keep.Nav),
        // so a short window scrolls them and there are no grid rows to fit any more.
        var xaml = SourceFiles.Read(Path.Combine("src", "CoodszHost", "MainWindow.xaml"));
        var scroll = xaml.IndexOf("<ScrollViewer x:Name=\"NavScroll\" VerticalScrollBarVisibility=\"Auto\"", StringComparison.Ordinal);
        Assert.True(scroll >= 0);
        Assert.True(xaml.IndexOf("<StackPanel x:Name=\"NavList\" VerticalAlignment=\"Top\"", StringComparison.Ordinal) > scroll);
        Assert.DoesNotContain("NavGrid", xaml);
        var window = SourceFiles.Window(SourceFiles.Root(), "CoodszHost");
        Assert.DoesNotContain("FitNav", window);
        Assert.Contains("<Setter Property=\"MinHeight\" Value=\"36\"/>", SourceFiles.Read(Path.Combine("src", "CoodszHost", "Keep", "KeepTheme.xaml")));

        // The TAOM and Burning Empires Server Tools have the same list since redesign wave 6.
        foreach (var project in new[] { "TaomCoopHost", "BurningEmpiresHost" })
        {
            var toolXaml = SourceFiles.Read(Path.Combine("src", project, "MainWindow.xaml"));
            var toolScroll = toolXaml.IndexOf("<ScrollViewer x:Name=\"NavScroll\" VerticalScrollBarVisibility=\"Auto\"", StringComparison.Ordinal);
            Assert.True(toolScroll >= 0 && toolXaml.IndexOf("<StackPanel x:Name=\"NavList\" VerticalAlignment=\"Top\"", StringComparison.Ordinal) > toolScroll, project);
            Assert.DoesNotContain("NavGrid", toolXaml);
            Assert.DoesNotContain("FitNav", SourceFiles.Window(SourceFiles.Root(), project));
        }
    }

    // Meus relatórios with 99+ did not fit the 187 px sidebar slot with the count in the button's row, so the count sits
    // on the button's corner and the button keeps Send my logs' width.
    [Fact]
    public void The_count_sits_on_the_button_corner()
    {
        var window = SourceFiles.Read(Path.Combine("src", "BannerlordCoopLauncher", "ReportReplyWindow.cs"));
        Assert.Contains("cell.Children.Add(_badge);", window);
        // Raised only in a sidebar: in a launcher's row the raised count covered the bottom edge of the banner above it.
        Assert.Contains("Margin = new Thickness(0, below ? -8 : 0, -10, 0),", window);
        Assert.DoesNotContain("content.Children.Add(_badge);", window);
        Assert.Contains("_badge.MouseLeftButtonUp += (_, _) => MyReportsWindow.Open(owner, tools);", window);
    }

    // Envoyer mes journaux, Отправить мои логи and Günlüklerimi gönder were cut at the Server Tools' sidebar edge, and
    // Join my server in de, fr, pt and ru: in a sidebar their words scale down to fit, words that fit stay as they are.
    [Fact]
    public void Long_sidebar_words_scale_down_instead_of_being_cut()
    {
        var window = SourceFiles.Read(Path.Combine("src", "BannerlordCoopLauncher", "ReportReplyWindow.cs"));
        Assert.Contains("button.Content = new Viewbox { Child = content, StretchDirection = StretchDirection.DownOnly };", window);
        var attach = SourceFiles.Method(window, "public static void Attach(");
        Assert.Contains("FitWidth(sendLogsSlot.Content as Button);", attach);
        Assert.Contains("FitWidth(button.Button);", attach);
        // Every Server Tool shows Join my server as a small footer link since the redesign (wave 1 for the Coop and AD1259
        // Server Tool, wave 6 for TAOM and Burning Empires, then Realm of Thrones), in the footer's down only Viewbox.
        foreach (var project in new[] { "CoodszHost", "TaomCoopHost", "BurningEmpiresHost", "RealmOfThronesHost" })
        {
            var xaml = SourceFiles.Read(Path.Combine("src", project, "MainWindow.xaml"));
            var link = xaml.IndexOf("<Button x:Name=\"RealmLink\" Style=\"{StaticResource Keep.BtnLink}\" Content=\"Join my server\" Tag=\"Realm\"", StringComparison.Ordinal);
            var footer = xaml.IndexOf("<Viewbox x:Name=\"FooterFit\" StretchDirection=\"DownOnly\"", StringComparison.Ordinal);
            Assert.True(footer > 0 && link > footer, project);
            Assert.DoesNotContain("x:Name=\"NavRealm\"", xaml);
        }
    }

    [Fact]
    public void The_whats_new_line_is_plain_and_in_all_six_languages()
    {
        var line = (string)typeof(WhatsNew).GetField("MyReportsVisible", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        Assert.DoesNotContain("—", line);
        Assert.DoesNotMatch(@"[a-z]-[a-z]", line);
        foreach (var code in new[] { "de", "fr", "pt", "ru", "tr", "ar" })
        {
            using var doc = System.Text.Json.JsonDocument.Parse(SourceFiles.Read(Path.Combine("src", "BannerlordCoopLauncher.Core", "Localization", "Languages", code + ".json")));
            Assert.True(doc.RootElement.TryGetProperty(line, out var value) && value.GetString()!.Length > 0, code);
        }
    }
}
