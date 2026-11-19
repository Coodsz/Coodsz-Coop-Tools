using System.IO.Compression;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using BannerlordCoopLauncher.Core.Support;
using BannerlordCoopLauncher.Core.Updates;

namespace BannerlordCoopLauncher.Tests;

/// <summary>
/// In tool replies (docs/design/in-tool-replies.md), the tool side: the reply key, the files it keeps,
/// the check against POST /replies (a fake worker here, never the live one), the scrub, My reports and
/// the Still not working? message.
/// </summary>
[Collection("Report reply files")]
public sealed class ReportRepliesTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "coodsz-report-replies-" + Guid.NewGuid().ToString("N"));
    private DateTime _now = new(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);

    private const string Code = "CR-7KQ2M";
    private const string OtherCode = "CR-ABCDE";

    public ReportRepliesTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, true); }
        catch (IOException) { }
    }

    private string Product => Path.Combine(_root, "TAOM");

    private ReportReplyStore Store(string role = "Client", string key = "taom-client") =>
        new(Path.Combine(Product, role), key, [Product, Path.Combine(Product, "Server"), Path.Combine(Product, "Client")], () => _now);

    private static string Answer(params (string Code, object[] Items)[] replies) => JsonSerializer.Serialize(new
    {
        ok = true,
        next = 1800,
        replies = replies.Select(reply => new { code = reply.Code, items = reply.Items }).ToArray()
    });

    private static object Item(string id, string at, string text, bool logs = false) =>
        new { id, at, text, buttons = logs ? new[] { "send-logs" } : Array.Empty<string>() };

    // ----- The key -----

    [Fact]
    public void The_key_is_32_random_bytes_in_base64url_without_padding()
    {
        var keys = Enumerable.Range(0, 50).Select(_ => ReportReplies.NewKey()).ToList();
        foreach (var key in keys)
        {
            Assert.Matches("^[A-Za-z0-9_-]{43}$", key);
            Assert.True(ReportReplies.IsKey(key));
            Assert.Equal(32, Convert.FromBase64String(key.Replace('-', '+').Replace('_', '/') + "=").Length);
        }

        Assert.Equal(keys.Count, keys.Distinct().Count());
        Assert.False(ReportReplies.IsKey(null));
        Assert.False(ReportReplies.IsKey(keys[0] + "="));
        Assert.False(ReportReplies.IsKey(keys[0][..42]));
        Assert.False(ReportReplies.IsKey(keys[0][..42] + "+"));
    }

    [Fact]
    public async Task The_key_goes_only_in_the_sent_summary_never_in_the_zip_or_the_preview()
    {
        var zip = Path.Combine(_root, "logs.zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        using (var writer = new StreamWriter(archive.CreateEntry("game/rgl_log.txt").Open()))
            writer.Write("a log line");
        var pack = new CrashReportPack(zip, new CrashReportSummary { ToolVersion = "1.2.0" }, []);
        var key = ReportReplies.NewKey();
        var worker = new Recorder("{\"ok\":true,\"code\":\"" + Code + "\",\"replies\":true}");
        var result = await CrashReports.SendAsync(new HttpClient(worker), pack, CrashReports.WithReplyKey(pack.Summary, key));

        Assert.Equal(CrashReportSendStatus.Sent, result.Status);
        Assert.True(result.Replies);
        var body = worker.Bodies.Single();
        var summaryPart = body[..body.IndexOf("name=\"zip\"", StringComparison.Ordinal)];
        var zipPart = body[body.IndexOf("name=\"zip\"", StringComparison.Ordinal)..];
        Assert.Contains("\"replyKey\": \"" + key + "\"", summaryPart);
        Assert.DoesNotContain(key, zipPart);
        // The zip that went is the same bytes as the zip on disk, and the summary the window previews has no key.
        Assert.DoesNotContain(key, Encoding.Latin1.GetString(File.ReadAllBytes(zip)));
        Assert.DoesNotContain("replyKey", CrashReports.SummaryJson(pack.Summary));
        Assert.Null(pack.Summary.ReplyKey);
        Assert.Null(CrashReports.WithReplyKey(pack.Summary, "not a key").ReplyKey);
        Assert.DoesNotContain(key, result.Message);
    }

    [Fact]
    public void An_older_worker_that_does_not_say_replies_true_means_the_key_is_dropped()
    {
        Assert.True(CrashReports.ReadReplies("{\"code\":\"CR-7KQ2M\",\"replies\":true}"));
        Assert.False(CrashReports.ReadReplies("{\"code\":\"CR-7KQ2M\"}"));
        Assert.False(CrashReports.ReadReplies("{\"code\":\"CR-7KQ2M\",\"replies\":\"true\"}"));
        Assert.False(CrashReports.ReadReplies("{\"code\":\"CR-7KQ2M\",\"replies\":false}"));
        Assert.False(CrashReports.ReadReplies("not json"));
        Assert.False(CrashReports.ReadReplies("[true]"));
    }

    [Fact]
    public void The_window_keeps_the_key_only_on_replies_true_and_never_logs_it()
    {
        var window = SourceFiles.Read(Path.Combine("src", "BannerlordCoopLauncher", "CrashReportWindow.cs"));
        Assert.Contains("CrashReports.SendAsync(http, final, CrashReports.WithReplyKey(SummaryOf(final), replyKey))", window);
        Assert.Contains("ReportReplies.KeepSentAsync(store, result.Code, result.Replies ? replyKey : null, _knownFixId, _log, title: title, reason: _request.Reason)", window);
        // The code screen promises an answer in the tool only when the key was kept.
        Assert.Contains("var kept = await KeepReplyKeyAsync(result, replyKey);", window);
        Assert.Contains("ShowCode(result.Code, kept);", window);
        // No log line, status line or preview takes the key.
        foreach (Match call in Regex.Matches(window, @"(?:_log|Say|log)\([^;]*;"))
            Assert.DoesNotContain("replyKey", call.Value, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("WithReplyKey", SourceFiles.Method(window, "private string PreviewText("));
        var replies = SourceFiles.Read(Path.Combine("src", "BannerlordCoopLauncher", "ReportReplyWindow.cs"));
        foreach (Match call in Regex.Matches(replies, @"_log\([^;]*;"))
            Assert.DoesNotMatch(@"replyKey|\.Key\b|\bkey\b", call.Value);
    }

    // ----- The files -----

    [Fact]
    public void Each_tool_writes_its_own_two_files_in_its_folder()
    {
        var store = Store();
        var key = ReportReplies.NewKey();
        store.Remember(Code, key);
        Assert.Equal(Path.Combine(Product, "Client", "report-replies-taom-client.json"), store.KeysPath);
        Assert.Equal(Path.Combine(Product, "Client", "report-answers-taom-client.json"), store.AnswersPath);
        Assert.True(File.Exists(store.KeysPath));
        using var doc = JsonDocument.Parse(File.ReadAllText(store.KeysPath));
        var entry = doc.RootElement.GetProperty("reports").EnumerateArray().Single();
        Assert.Equal(Code, entry.GetProperty("code").GetString());
        Assert.Equal(key, entry.GetProperty("key").GetString());
        Assert.Equal(0, entry.GetProperty("seen").GetArrayLength());
        Assert.True(entry.TryGetProperty("sentAt", out _));

        // A bad code or key is never kept.
        store.Remember("CR-1", key);
        store.Remember(OtherCode, "short");
        Assert.Single(store.KeysToCheck());
    }

    [Fact]
    public void Reports_older_than_30_days_are_dropped_and_not_asked_about()
    {
        var store = Store();
        store.Remember(Code, ReportReplies.NewKey());
        _now = _now.AddDays(20);
        store.Remember(OtherCode, ReportReplies.NewKey());
        Assert.Equal([OtherCode, Code], store.KeysToCheck().Select(entry => entry.Code));
        _now = _now.AddDays(11);
        Assert.Equal([OtherCode], store.KeysToCheck().Select(entry => entry.Code));
        store.Prune();
        Assert.DoesNotContain(Code, File.ReadAllText(store.KeysPath));
        _now = _now.AddDays(30);
        Assert.False(store.HasKeys);
    }

    [Fact]
    public async Task No_keys_means_no_call_to_the_worker()
    {
        var worker = new Recorder(Answer());
        var check = await ReportReplies.CheckAsync(new HttpClient(worker), Store());
        Assert.False(check.Called);
        Assert.Empty(worker.Bodies);

        // Only expired keys: still no call.
        var store = Store();
        store.Remember(Code, ReportReplies.NewKey());
        _now = _now.AddDays(31);
        check = await ReportReplies.CheckAsync(new HttpClient(worker), store);
        Assert.False(check.Called);
        Assert.Empty(worker.Bodies);
    }

    [Fact]
    public async Task The_request_is_the_exact_shape_with_at_most_10_newest_reports()
    {
        var store = Store();
        var codes = new List<string>();
        for (var i = 0; i < 12; i++)
        {
            var code = "CR-" + "23456789ABCD"[i] + "QQQQ";
            codes.Add(code);
            store.Remember(code, ReportReplies.NewKey());
            _now = _now.AddMinutes(1);
        }

        var worker = new Recorder(Answer());
        var check = await ReportReplies.CheckAsync(new HttpClient(worker), store);
        Assert.True(check.Called);
        Assert.Equal(HttpMethod.Post, worker.Methods.Single());
        Assert.Equal(ReportReplies.Endpoint, worker.Uris.Single());
        Assert.Equal("https://coodsz-mod-reports.coodsz.workers.dev/replies", ReportReplies.Endpoint);
        Assert.Equal("application/json", worker.ContentTypes.Single());
        using var doc = JsonDocument.Parse(worker.Bodies.Single());
        // "fast" (v1.3.5): the tool follows ReplySchedule. The worker before reply push ignores it.
        Assert.Equal(["kind", "schema", "fast", "reports"], doc.RootElement.EnumerateObject().Select(property => property.Name));
        Assert.True(doc.RootElement.GetProperty("fast").GetBoolean());
        Assert.Equal("CoodszReplyCheck", doc.RootElement.GetProperty("kind").GetString());
        Assert.Equal(1, doc.RootElement.GetProperty("schema").GetInt32());
        var reports = doc.RootElement.GetProperty("reports").EnumerateArray().ToList();
        Assert.Equal(10, reports.Count);
        Assert.Equal(codes.AsEnumerable().Reverse().Take(10), reports.Select(report => report.GetProperty("code").GetString()));
        Assert.All(reports, report =>
        {
            Assert.Equal(["code", "key"], report.EnumerateObject().Select(property => property.Name));
            Assert.True(ReportReplies.IsKey(report.GetProperty("key").GetString()));
        });
        Assert.True(Encoding.UTF8.GetByteCount(worker.Bodies.Single()) < 4096);
        Assert.Equal(TimeSpan.FromSeconds(1800), check.Next);
    }

    // ----- Seen and edited -----

    [Fact]
    public async Task A_reply_shows_once_an_edit_shows_again_and_a_newer_one_replaces_it()
    {
        var store = Store();
        store.Remember(Code, ReportReplies.NewKey());
        var first = Answer((Code, [Item("123456", "2026-11-02T10:00:00Z", "Turn off Diplomacy and try again.", logs: true)]));

        var check = await ReportReplies.CheckAsync(new HttpClient(new Recorder(first)), store);
        var reply = Assert.Single(check.NewItems);
        Assert.Equal(new ReplyItem(Code, "123456", "2026-11-02T10:00:00Z", "Turn off Diplomacy and try again.", true, false), reply);
        Assert.Contains("\"id\": \"123456\"", File.ReadAllText(store.KeysPath));

        // The same answer again: nothing new.
        check = await ReportReplies.CheckAsync(new HttpClient(new Recorder(first)), store);
        Assert.Empty(check.NewItems);

        // Edited: same id, new date, shows once more and the saved text follows.
        var edited = Answer((Code, [Item("123456", "2026-11-02T11:00:00Z", "Turn off Diplomacy and RBM, then try again.")]));
        check = await ReportReplies.CheckAsync(new HttpClient(new Recorder(edited)), store);
        Assert.Equal("Turn off Diplomacy and RBM, then try again.", Assert.Single(check.NewItems).Text);
        check = await ReportReplies.CheckAsync(new HttpClient(new Recorder(edited)), store);
        Assert.Empty(check.NewItems);
        var answer = Assert.Single(store.Answers());
        Assert.Equal("Turn off Diplomacy and RBM, then try again.", answer.Text);
        Assert.False(answer.Read);

        // A newer comment: the older answer stays in My reports, marked replaced.
        var newer = Answer((Code, [Item("123999", "2026-11-03T09:00:00Z", "Fixed in the next update.")]));
        Assert.Single((await ReportReplies.CheckAsync(new HttpClient(new Recorder(newer)), store)).NewItems);
        var answers = store.Answers();
        Assert.Equal(2, answers.Count);
        Assert.True(answers.Single(other => other.Id == "123456").Replaced);
        Assert.False(answers.Single(other => other.Id == "123999").Replaced);
    }

    [Fact]
    public async Task An_automatic_answer_is_marked_so_the_tool_labels_it()
    {
        var store = Store();
        store.Remember(Code, ReportReplies.NewKey());
        var body = JsonSerializer.Serialize(new
        {
            ok = true,
            next = 1800,
            replies = new[] { new { code = Code, items = new object[] { new { id = "77", at = "2026-11-02T10:00:00Z", text = "Verify game files in Steam.", buttons = Array.Empty<string>(), auto = true } } } }
        });
        var reply = Assert.Single((await ReportReplies.CheckAsync(new HttpClient(new Recorder(body)), store)).NewItems);
        Assert.True(reply.Auto);
        Assert.Equal(AnswerSource.Auto, Assert.Single(store.Answers()).Source);
        Assert.Equal("Automatic answer", ReportReplyText.SourceLabel(AnswerSource.Auto));
        Assert.Equal("Automatic answer", ReportReplyText.AutoTitle);
        var (items, _) = ReportReplies.ParseResponse(body.Replace("\"auto\":true", "\"source\":\"auto\""), [Code]);
        Assert.True(Assert.Single(items).Auto);
    }

    // ----- Junk -----

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("{\"ok\":true}")]
    [InlineData("{\"ok\":true,\"replies\":{}}")]
    [InlineData("{\"ok\":true,\"replies\":[1,\"x\",null,{\"code\":5},{\"code\":\"CR-7KQ2M\",\"items\":\"x\"}]}")]
    [InlineData("{\"ok\":true,\"replies\":[{\"code\":\"CR-7KQ2M\",\"items\":[{\"id\":\"../x\",\"at\":\"2026-11-02T10:00:00Z\",\"text\":\"a\"}]}]}")]
    [InlineData("{\"ok\":true,\"replies\":[{\"code\":\"CR-7KQ2M\",\"items\":[{\"id\":\"1\",\"at\":\"yesterday\",\"text\":\"a\"}]}]}")]
    [InlineData("{\"ok\":true,\"replies\":[{\"code\":\"CR-7KQ2M\",\"items\":[{\"id\":\"1\",\"at\":\"2026-11-02T10:00:00Z\",\"text\":\"<b></b>\"}]}]}")]
    [InlineData("{\"ok\":true,\"replies\":[{\"code\":\"CR-ABCDE\",\"items\":[{\"id\":\"1\",\"at\":\"2026-11-02T10:00:00Z\",\"text\":\"not asked\"}]}]}")]
    public void Junk_answers_give_no_replies(string json)
    {
        var (items, _) = ReportReplies.ParseResponse(json, [Code]);
        Assert.Empty(items);
    }

    [Fact]
    public async Task Worker_errors_and_a_429_never_throw_and_set_the_wait()
    {
        var store = Store();
        store.Remember(Code, ReportReplies.NewKey());
        var limited = await ReportReplies.CheckAsync(new HttpClient(new Recorder("{\"ok\":false,\"error\":\"slow down\",\"next\":3600}", HttpStatusCode.TooManyRequests)), store);
        Assert.Empty(limited.NewItems);
        Assert.Equal(TimeSpan.FromHours(1), limited.Next);
        var broken = await ReportReplies.CheckAsync(new HttpClient(new Recorder("oops", HttpStatusCode.InternalServerError)), store);
        Assert.Empty(broken.NewItems);
        Assert.Equal(ReportReplies.DefaultWait, broken.Next);
        var offline = await ReportReplies.CheckAsync(new HttpClient(new Throwing()), store);
        Assert.Empty(offline.NewItems);
        Assert.True(offline.Called);

        Assert.Equal(TimeSpan.FromMinutes(10), ReportReplies.NextWait(null));
        Assert.Equal(TimeSpan.FromMinutes(10), ReportReplies.NextWait(5));
        Assert.Equal(TimeSpan.FromMinutes(30), ReportReplies.NextWait(1800));
        Assert.Equal(TimeSpan.FromHours(6), ReportReplies.NextWait(999_999));
    }

    [Fact]
    public async Task Junk_files_are_read_as_empty_and_kept_aside_once()
    {
        var store = Store();
        Directory.CreateDirectory(store.Folder);
        File.WriteAllText(store.KeysPath, "{ not json");
        File.WriteAllText(store.AnswersPath, "[[[");
        Assert.False(store.HasKeys);
        Assert.Empty(store.Answers());
        Assert.Empty(store.Rows());

        store.Remember(Code, ReportReplies.NewKey());
        Assert.Single(store.KeysToCheck());
        Assert.Equal("{ not json", File.ReadAllText(store.KeysPath + ".bad"));
        var body = Answer((Code, [Item("1", "2026-11-02T10:00:00Z", "Hello")]));
        Assert.Single((await ReportReplies.CheckAsync(new HttpClient(new Recorder(body)), store)).NewItems);
        Assert.Equal("[[[", File.ReadAllText(store.AnswersPath + ".bad"));

        // Entries with missing values do not break the check.
        File.WriteAllText(store.KeysPath, "{\"reports\":[null,{\"code\":null},{\"code\":\"CR-7KQ2M\",\"key\":\"x\"}]}");
        Assert.False(store.HasKeys);
    }

    // ----- The scrub -----

    [Fact]
    public void The_reply_is_scrubbed_again_to_plain_text()
    {
        var text = string.Join("\n",
            "Hi @someone, see #4157 and Coodsz/BannerlordCoopLauncher#12.",
            "Your id 76561198012345678 and [U:1:12345] and STEAM_0:1:1234 are gone.",
            "Path C:\\Users\\RealName\\Documents and mail me@example.com, IP 192.168.1.20.",
            "<b>Bold</b> <script>alert(1)</script> <!-- coodsz-answer CR-7KQ2M -->",
            "[the guide](https://example.com/guide) ![pic](https://example.com/a.png)",
            "https://evil.example/path www.evil.example and https://discord.gg/bannerlordcoop stay or go.",
            "```",
            "rm -rf something",
            "```",
            "Also CR-ABCDE is someone else's, but " + Code + " is yours.",
            "Bell" + (char)7 + " and hidden" + (char)0x202E + " text.");
        var cleaned = ReportReplies.Scrub(text, Code);

        foreach (var gone in new[] { "@someone", "#4157", "#12", "76561198012345678", "[U:1:12345]", "STEAM_0:1:1234", "RealName", "me@example.com",
                     "192.168.1.20", "<b>", "<script>", "coodsz-answer", "https://example.com", "a.png", "evil.example", "rm -rf", "```", "CR-ABCDE", "pic" })
            Assert.DoesNotContain(gone, cleaned);
        Assert.Contains("Bold", cleaned);
        Assert.Contains("the guide", cleaned);
        Assert.Contains("https://discord.gg/bannerlordcoop", cleaned);
        Assert.Contains(Code + " is yours", cleaned);
        Assert.DoesNotMatch(@"[\x00-\x08\x0B\x0C\x0E-\x1F\u202E]", cleaned);
        Assert.DoesNotContain("\r", cleaned);

        var longText = ReportReplies.Scrub(new string('a', 3000));
        Assert.Equal(1500, longText.Length);
        Assert.EndsWith("...", longText);
        Assert.Equal("", ReportReplies.Scrub(null));
        Assert.Equal("", ReportReplies.Scrub("<p></p>"));
    }

    [Fact]
    public void Nothing_but_the_cleaned_text_is_kept_in_my_reports()
    {
        var store = Store();
        var key = ReportReplies.NewKey();
        store.Remember(Code, key);
        store.TakeNew([new ReplyItem(Code, "5", "2026-11-02T10:00:00Z", "Mail me@example.com", false, false)]);
        var saved = File.ReadAllText(store.AnswersPath);
        Assert.DoesNotContain(key, saved);
        Assert.DoesNotContain("me@example.com", saved);
        using var doc = JsonDocument.Parse(saved);
        var entry = doc.RootElement.GetProperty("answers").EnumerateArray().Single();
        Assert.Equal(["code", "id", "at", "savedAt", "source", "text", "read", "logs", "replaced"], entry.EnumerateObject().Select(property => property.Name));
    }

    // ----- My reports -----

    [Fact]
    public async Task The_other_exe_of_the_product_is_read_too_with_read_kept_per_exe()
    {
        var server = Store("Server", "taom-server");
        var client = Store();
        server.Remember(Code, ReportReplies.NewKey());
        Assert.Equal([Code], client.KeysToCheck().Select(entry => entry.Code));

        // The client checks the server's report and shows the answer once; the server still shows it too.
        var body = Answer((Code, [Item("9", "2026-11-02T10:00:00Z", "Update TAOM.")]));
        Assert.Single((await ReportReplies.CheckAsync(new HttpClient(new Recorder(body)), client)).NewItems);
        Assert.Empty((await ReportReplies.CheckAsync(new HttpClient(new Recorder(body)), client)).NewItems);
        Assert.Single((await ReportReplies.CheckAsync(new HttpClient(new Recorder(body)), server)).NewItems);
        Assert.False(File.Exists(Path.Combine(Product, "Server", "report-answers-taom-client.json")));

        client.MarkRead(client.Answers().Single());
        Assert.Equal(0, client.UnreadCount());
        Assert.Equal(1, server.UnreadCount());

        // An answer only the other exe has shows here unread, and reading it copies it into this exe's file.
        File.Delete(client.AnswersPath);
        Assert.Equal(1, client.UnreadCount());
        client.MarkAllRead();
        Assert.Equal(0, client.UnreadCount());
        Assert.Contains("Update TAOM.", File.ReadAllText(client.AnswersPath));
    }

    [Fact]
    public void My_reports_lists_answers_and_reports_with_no_answer_and_searches_code_and_text()
    {
        var store = Store();
        store.Remember(Code, ReportReplies.NewKey());
        _now = _now.AddMinutes(5);
        store.Remember(OtherCode, ReportReplies.NewKey());
        _now = _now.AddMinutes(5);
        store.TakeNew([new ReplyItem(Code, "1", "2026-11-02T10:00:00Z", "Remove the Diplomacy mod.", false, false)]);

        var rows = store.Rows();
        Assert.Equal(2, rows.Count);
        Assert.Equal(Code, rows[0].Code);
        Assert.NotNull(rows[0].Answer);
        Assert.Equal(OtherCode, rows[1].Code);
        Assert.Null(rows[1].Answer);
        Assert.NotNull(rows[1].SentAt);

        Assert.Equal([Code], ReportReplies.Search(rows, "diplomacy").Select(row => row.Code));
        Assert.Equal([OtherCode], ReportReplies.Search(rows, "cr-abc").Select(row => row.Code));
        Assert.Equal(2, ReportReplies.Search(rows, "  ").Count);
        Assert.Empty(ReportReplies.Search(rows, "nothing like this"));
    }

    [Fact]
    public void My_reports_keeps_the_newest_200_answers_without_an_age_limit()
    {
        var store = Store();
        var items = Enumerable.Range(1, 205).Select(i => new ReplyItem(Code, i.ToString(), "2026-11-02T10:00:00Z", "Answer " + i, false, false));
        foreach (var item in items)
        {
            _now = _now.AddMinutes(1);
            store.TakeNew([item]);
        }

        var answers = store.Answers();
        Assert.Equal(ReportReplyStore.MaxAnswers, answers.Count);
        Assert.Equal("Answer 205", answers[0].Text);
        Assert.DoesNotContain(answers, answer => answer.Text == "Answer 5");
        _now = _now.AddDays(400);
        Assert.Equal(ReportReplyStore.MaxAnswers, store.Answers().Count);
    }

    [Fact]
    public void A_known_fix_is_kept_read_and_gets_the_code_when_the_report_is_sent()
    {
        var store = Store();
        store.SaveKnownFix("ig-party-size", "Update Improved Garrisons.");
        store.SaveKnownFix("ig-party-size", "Update Improved Garrisons.");
        var fix = Assert.Single(store.Answers());
        Assert.Equal(AnswerSource.Known, fix.Source);
        Assert.True(fix.Read);
        Assert.Equal("", fix.Code);
        Assert.Equal("Known fix", ReportReplyText.SourceLabel(fix.Source));

        store.AttachCode("ig-party-size", Code);
        Assert.Equal(Code, Assert.Single(store.Answers()).Code);
        Assert.Equal([Code], ReportReplies.Search(store.Rows(), Code).Select(row => row.Code));
    }

    // ----- Still not working? -----

    [Fact]
    public void The_discord_message_has_only_the_code_tool_and_version_and_never_asks_for_files()
    {
        var message = ReportReplies.DiscordMessage(Code, "Coodsz TAOM Client Launcher", "1.2.0");
        Assert.Equal("Hi Coodsz, the fix for my report CR-7KQ2M didn't work. Tool: Coodsz TAOM Client Launcher v1.2.0. If you need my logs, I'll send them with Send my logs in the tool.", message);
        Assert.EndsWith("If you need my logs, I'll send them with Send my logs in the tool.", ReportReplies.DiscordMessage(null, "x", "1"));
        Assert.StartsWith("Hi Coodsz, the fix the tool showed me didn't work.", ReportReplies.DiscordMessage(null, "Coodsz Coop Server Tool", "1.3.1"));
        var odd = ReportReplies.DiscordMessage("CR-1", "C:\\Users\\Pat\\tool", "1.2.0 \\Users");
        Assert.DoesNotContain("Pat", odd);
        Assert.DoesNotContain("CR-1", odd);
        foreach (var text in new[] { message, odd })
            Assert.DoesNotMatch(@"(?i)\b(zip|dump|attach|upload|file)", text);
        Assert.Equal("https://discord.gg/bannerlordcoop", ReportReplies.CoopDiscordInvite);
        Assert.Equal("https://discord.com/channels/709516043332354119", ReportReplies.CoopDiscordChannel);
        Assert.DoesNotContain("z8SncS7UX", SourceFiles.Read(Path.Combine("src", "BannerlordCoopLauncher", "ReportReplyWindow.cs")));
    }

    [Fact]
    public void Every_tool_opens_the_same_current_Realm_invite()
    {
        // The old invite DFhep3yrD expired (AD1259 #33); the Realm button must use the new one in all 9 tools.
        foreach (var project in new[] { "AD1259Launcher", "BannerlordCoopLauncher", "BurningEmpiresHost", "BurningEmpiresLauncher", "CoodszHost", "RealmOfThronesHost", "RealmOfThronesLauncher", "TaomCoopHost", "TaomCoopLauncher" })
        {
            var source = SourceFiles.Read(Path.Combine("src", project, "MainWindow.Realm.cs"));
            Assert.Contains("RealmInviteUrl = \"https://discord.gg/z8SncS7UX\"", source);
            Assert.DoesNotContain("DFhep3yrD", source);
        }
    }

    // ----- Words and wiring -----

    private static IEnumerable<string> WhatsNewLines() =>
        new[] { "RepliesInTool", "MyReportsPage", "AskOnDiscord" }
            .Select(name => (string)typeof(WhatsNew).GetField(name, BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!);

    [Fact]
    public void Every_new_text_is_plain_and_in_all_six_languages()
    {
        var english = ReportReplyText.All.Concat(WhatsNewLines()).Append(ReportFileBusyException.BusyText)
            .Append(ReportReplies.KeyWaitsText).Append(ReportReplies.KeyLostText)
            .Append(ReportReplies.FollowUpWaitsText).Append(ReportReplies.FollowUpLostText).ToList();
        Assert.Equal(english.Count, english.Distinct().Count());
        foreach (var text in english)
        {
            Assert.DoesNotContain("—", text);
            Assert.DoesNotMatch(new Regex(@"[a-z]-[a-z]"), text);
        }

        foreach (var code in new[] { "de", "fr", "pt", "ru", "tr", "ar" })
        {
            using var doc = JsonDocument.Parse(SourceFiles.Read(Path.Combine("src", "BannerlordCoopLauncher.Core", "Localization", "Languages", code + ".json")));
            foreach (var text in english)
                Assert.True(doc.RootElement.TryGetProperty(text, out var value) && value.GetString()!.Length > 0, code + " lacks: " + text);
        }

        // The message for Coodsz stays English, so ExtractUiText leaves it out and no language has it.
        var extract = SourceFiles.Read(Path.Combine("tools", "ExtractUiText", "Program.cs"));
        Assert.Contains("\"" + ReportReplies.DiscordUnknownTool + "\"", extract);
        foreach (var code in new[] { "de", "fr", "pt", "ru", "tr", "ar" })
        {
            var table = SourceFiles.Read(Path.Combine("src", "BannerlordCoopLauncher.Core", "Localization", "Languages", code + ".json"));
            Assert.DoesNotContain("Hi Coodsz", table);
        }
        // Every tool got in tool replies in the same release, so one entry of every list (the newest then, a
        // later hotfix can come before it) names all three lines.
        foreach (var list in new[] { WhatsNew.Friends, WhatsNew.Taom, WhatsNew.AD1259, WhatsNew.Be })
            Assert.Contains(list, entry => WhatsNewLines().All(entry.Lines.Contains));
    }

    [Fact]
    public void Every_tool_shows_my_reports_and_runs_the_check()
    {
        foreach (var project in new[] { "BannerlordCoopLauncher", "TaomCoopLauncher", "AD1259Launcher", "BurningEmpiresLauncher" })
            Assert.Contains("Coodsz.Ui.ReportRepliesUi.Attach(this, SendLogsSlot, OpenSendLogs, Log, logsRequest: () => CrashRequest(Coodsz.Ui.CrashContext.Logs), bannerHost: ReplyBannerHost);", SourceFiles.Window(SourceFiles.Root(), project));
        foreach (var project in new[] { "CoodszHost", "TaomCoopHost", "BurningEmpiresHost" })
        {
            var main = SourceFiles.Window(SourceFiles.Root(), project);
            Assert.Contains("Coodsz.Ui.ReportRepliesUi.Attach(this, SendLogsSlot, _crashBar.OpenLogs,", main);
            // No, still broken packs the same Send my logs as the tool's own button.
            Assert.Contains("below: true, logsRequest: () => CrashRequest(Coodsz.Ui.CrashContext.Logs), bannerHost: ReplyBannerHost);", main);
        }

        foreach (var project in new[] { "AD1259Host", "AD1259Launcher", "BurningEmpiresHost", "BurningEmpiresLauncher", "CoodszCoopAndFriends", "CoodszHost", "TaomCoopHost", "TaomCoopLauncher" })
            Assert.Contains("<Compile Include=\"..\\BannerlordCoopLauncher\\ReportReplyWindow.cs\" Link=\"ReportReplyWindow.cs\" />",
                SourceFiles.Read(Path.Combine("src", project, project + ".csproj")));
        // The check waits 30 s after start (none while minimized or with nothing open) and only calls the worker
        // through CheckAsync, which asks nothing without keys.
        var window = SourceFiles.Read(Path.Combine("src", "BannerlordCoopLauncher", "ReportReplyWindow.cs"));
        Assert.Contains("DateTime.UtcNow + ReplySchedule.StartWait", SourceFiles.Method(window, "public void Start()"));
        Assert.Equal(TimeSpan.FromSeconds(30), ReplySchedule.StartWait);
        Assert.Contains("ReportReplies.CheckAsync(http, _store)", window);
        Assert.DoesNotContain("PostAsync", window);
    }

    // ----- Review fixes -----

    [Fact]
    public void An_answer_that_could_not_be_kept_is_not_marked_seen_and_comes_again()
    {
        var store = Store();
        store.Remember(Code, ReportReplies.NewKey());
        var item = new ReplyItem(Code, "42", "2026-11-02T10:00:00Z", "Update the tool.", false, false);
        // A folder where the answers file should be: keeping the answer fails.
        Directory.CreateDirectory(store.AnswersPath);
        Assert.NotNull(Record.Exception(() => store.TakeNew([item])));
        Assert.DoesNotContain("\"42\"", File.ReadAllText(store.KeysPath));
        Directory.Delete(store.AnswersPath);

        Assert.Single(store.TakeNew([item]));
        Assert.Equal("Update the tool.", Assert.Single(store.Answers()).Text);
        Assert.Empty(store.TakeNew([item]));
        Assert.Empty(Directory.GetFiles(store.Folder, "*.tmp"));
    }

    [Fact]
    public void A_file_another_process_holds_is_waited_for_briefly_then_left_alone()
    {
        var store = Store();
        Assert.Equal(ReportReplyStore.MutexName(store.KeysPath), ReportReplyStore.MutexName(store.KeysPath.ToUpperInvariant()));
        Assert.NotEqual(ReportReplyStore.MutexName(store.KeysPath), ReportReplyStore.MutexName(store.AnswersPath));
        Assert.StartsWith("Local\\Coodsz-report-file-", ReportReplyStore.MutexName(store.KeysPath));

        var wait = ReportReplyStore.FileWait;
        using var held = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var holder = new Thread(() =>
        {
            using var mutex = new Mutex(false, ReportReplyStore.MutexName(store.KeysPath));
            mutex.WaitOne();
            held.Set();
            release.Wait();
            mutex.ReleaseMutex();
        });
        try
        {
            ReportReplyStore.FileWait = TimeSpan.FromMilliseconds(100);
            holder.Start();
            held.Wait();
            Assert.Throws<ReportFileBusyException>(() => store.Remember(Code, ReportReplies.NewKey()));
            Assert.False(File.Exists(store.KeysPath));
        }
        finally
        {
            release.Set();
            holder.Join();
            ReportReplyStore.FileWait = wait;
        }

        store.Remember(Code, ReportReplies.NewKey());
        Assert.Single(store.KeysToCheck());
        var source = SourceFiles.Read(Path.Combine("src", "BannerlordCoopLauncher.Core", "Support", "ReportReplies.cs"));
        Assert.Contains("path + \".\" + Guid.NewGuid()", source);
    }

    [Fact]
    public async Task More_than_10_reports_take_turns_so_every_one_is_asked_about()
    {
        var store = Store();
        var codes = new List<string>();
        for (var i = 0; i < 12; i++)
        {
            var code = "CR-" + "23456789ABCD"[i] + "WWWW";
            codes.Add(code);
            store.Remember(code, ReportReplies.NewKey());
            _now = _now.AddMinutes(1);
        }

        var asked = new HashSet<string>();
        for (var check = 0; check < 2; check++)
        {
            var worker = new Recorder(Answer());
            await ReportReplies.CheckAsync(new HttpClient(worker), store);
            using var doc = JsonDocument.Parse(worker.Bodies.Single());
            var batch = doc.RootElement.GetProperty("reports").EnumerateArray().Select(report => report.GetProperty("code").GetString()!).ToList();
            Assert.Equal(10, batch.Count);
            asked.UnionWith(batch);
        }

        Assert.Equal(codes.ToHashSet(), asked);
    }

    [Fact]
    public async Task Expired_keys_leave_the_file_after_a_check_and_the_tool_prunes_on_start()
    {
        var store = Store();
        store.Remember(Code, ReportReplies.NewKey());
        _now = _now.AddDays(25);
        store.Remember(OtherCode, ReportReplies.NewKey());
        _now = _now.AddDays(6);
        Assert.Contains(Code, File.ReadAllText(store.KeysPath));
        await ReportReplies.CheckAsync(new HttpClient(new Recorder(Answer())), store);
        Assert.DoesNotContain(Code, File.ReadAllText(store.KeysPath));
        Assert.Contains(OtherCode, File.ReadAllText(store.KeysPath));

        var window = SourceFiles.Read(Path.Combine("src", "BannerlordCoopLauncher", "ReportReplyWindow.cs"));
        Assert.Contains("_store.Prune();", SourceFiles.Method(window, "public void Start()"));
    }

    [Fact]
    public void Closing_a_reply_keeps_the_worker_s_next_check_time()
    {
        var window = SourceFiles.Read(Path.Combine("src", "BannerlordCoopLauncher", "ReportReplyWindow.cs"));
        var show = SourceFiles.Method(window, "private void ShowNext()");
        Assert.Contains("WaitForCheck();", show);
        Assert.DoesNotContain("DefaultWait", show);
        Assert.Contains("_nextCheckUtc = wait == null ? null : now + wait.Wait + Jitter(wait);", window);
        // A keys file busy for a moment gives a retry, never a stop.
        Assert.Contains("var wait = ReplySchedule.AfterCheck(check, now, _store.ScheduleReports, Minimized);", window);
        // A sent report, a No, still broken or a restored window brings a check soon, never before the worker's wait;
        // minimized, nothing is checked.
        Assert.Contains("ReportRepliesUi.Changed += OnChanged;", window);
        Assert.Contains("ReportRepliesUi.Changed -= OnChanged;", window);
        Assert.Contains("owner.StateChanged += OnState;", window);
        Assert.Contains("owner.StateChanged -= OnState;", window);
        // Only Minimized to another state is a restore; Normal to Maximized is not.
        Assert.Contains("var restored = lastState == WindowState.Minimized && owner.WindowState != WindowState.Minimized;", window);
        Assert.Contains("Replan(restored);", window);
        Assert.Contains("ReplySchedule.ReplanFrom(DateTime.UtcNow, _nextCheckUtc, _notBeforeUtc, _lastStart, _store.ScheduleReports,",
            SourceFiles.Method(window, "private void Replan(bool resume)"));
        Assert.Contains("if (!ReplySchedule.ShouldCheck(DateTime.UtcNow, _store.ScheduleReports, Minimized))", SourceFiles.Method(window, "private async Task TickAsync()"));
        // The answer body is capped before it is read.
        Assert.Contains("MaxResponseContentBufferSize = ReportReplies.MaxResponseBytes", window);
        Assert.Equal(256 * 1024, ReportReplies.MaxResponseBytes);
    }

    [Fact]
    public void A_known_fix_from_the_other_exe_shows_once_even_without_a_code()
    {
        var server = Store("Server", "taom-server");
        var client = Store();
        server.SaveKnownFix("ig-party-size", "Update Improved Garrisons.");
        var fix = Assert.Single(client.Answers());
        Assert.Equal("", fix.Code);
        Assert.Equal(AnswerSource.Known, fix.Source);

        // Both exes showed the same fix a minute apart: one row.
        _now = _now.AddMinutes(1);
        client.SaveKnownFix("ig-party-size", "Update Improved Garrisons.");
        Assert.Single(client.Answers());
        client.MarkAllRead();
        Assert.Single(client.Answers());

        // A sibling reply with no code is still refused.
        File.WriteAllText(server.AnswersPath, "{\"answers\":[{\"code\":\"\",\"id\":\"5\",\"at\":\"x\",\"source\":\"reply\",\"text\":\"no code\"}]}");
        Assert.DoesNotContain(client.Answers(), answer => answer.Text == "no code");
    }

    [Fact]
    public void The_links_the_worker_keeps_stay_as_plain_text()
    {
        var text = string.Join("\n",
            "get it here: https://github.com/Coodsz/Coodsz-TAOM/releases.",
            "[the latest](https://github.com/Coodsz/Coodsz-Coop-and-Friends/releases/latest)",
            "Realm of Thrones https://github.com/Coodsz/Coodsz-RealmOfThrones/releases/latest",
            "Workshop https://steamcommunity.com/sharedfiles/filedetails/?id=3824313421",
            "Invite https://discord.com/invite/bannerlordcoop and https://discord.com/channels/709516043332354119/123",
            "Not these https://github.com/someone/else/releases https://discord.com/channels/1/2 [x](https://evil.example)");
        var cleaned = ReportReplies.Scrub(text, Code);
        Assert.Contains("get it here: https://github.com/Coodsz/Coodsz-TAOM/releases.", cleaned);
        Assert.Contains("the latest (https://github.com/Coodsz/Coodsz-Coop-and-Friends/releases/latest)", cleaned);
        Assert.Contains("Realm of Thrones https://github.com/Coodsz/Coodsz-RealmOfThrones/releases/latest", cleaned);
        Assert.Contains("https://steamcommunity.com/sharedfiles/filedetails/?id=3824313421", cleaned);
        Assert.Contains("https://discord.com/invite/bannerlordcoop", cleaned);
        Assert.Contains("https://discord.com/channels/709516043332354119/123", cleaned);
        Assert.DoesNotContain("someone", cleaned);
        Assert.DoesNotContain("channels/1/2", cleaned);
        Assert.DoesNotContain("evil", cleaned);
        Assert.False(ReportReplies.IsAllowedLink("https://discord.gg.evil.example/x"));
    }

    // Holds the named mutex of one file on its own thread until released, like the other exe saving it.
    // With holdFor it lets go by itself after that long, on its own thread, so a starved thread pool on
    // a busy CI machine can't keep it holding past every retry.
    private sealed class OtherExe : IDisposable
    {
        private readonly ManualResetEventSlim _held = new();
        private readonly ManualResetEventSlim _release = new();
        private readonly Thread _thread;

        public OtherExe(string path, TimeSpan? holdFor = null)
        {
            _thread = new Thread(() =>
            {
                using var mutex = new Mutex(false, ReportReplyStore.MutexName(path));
                mutex.WaitOne();
                _held.Set();
                if (holdFor is { } hold)
                    _release.Wait(hold);
                else
                    _release.Wait();
                mutex.ReleaseMutex();
            });
            _thread.Start();
            _held.Wait();
        }

        public void Dispose()
        {
            _release.Set();
            _thread.Join();
            _held.Dispose();
            _release.Dispose();
        }
    }

    [Fact]
    public async Task A_key_the_other_exe_blocks_waits_in_memory_and_is_written_at_the_next_check()
    {
        var store = Store();
        var key = ReportReplies.NewKey();
        var log = new List<string>();
        var wait = ReportReplyStore.FileWait;
        ReportReplyStore.FileWait = TimeSpan.FromMilliseconds(50);
        try
        {
            using (new OtherExe(store.KeysPath))
            {
                var kept = await ReportReplies.KeepSentAsync(store, Code, key, null, log.Add, TimeSpan.FromMilliseconds(10));
                Assert.False(kept);
                Assert.False(File.Exists(store.KeysPath));
                Assert.Equal([Code], store.PendingCodes());
                Assert.Equal(string.Format(ReportReplies.KeyWaitsText, Code), Assert.Single(log));
                Assert.DoesNotContain(key, log.Single());
                // The check already asks with the waiting key.
                Assert.Equal([Code], store.KeysToCheck().Select(entry => entry.Code));
            }

            // The file is free again: the next check writes the key first.
            var worker = new Recorder(Answer());
            await ReportReplies.CheckAsync(new HttpClient(worker), store);
            Assert.Contains(key, File.ReadAllText(store.KeysPath));
            Assert.Contains(key, worker.Bodies.Single());
            Assert.Empty(store.PendingCodes());
        }
        finally
        {
            ReportReplyStore.FileWait = wait;
        }
    }

    [Fact]
    public async Task A_key_is_written_when_the_other_exe_lets_go_during_the_retries()
    {
        var store = Store();
        var key = ReportReplies.NewKey();
        var wait = ReportReplyStore.FileWait;
        ReportReplyStore.FileWait = TimeSpan.FromMilliseconds(50);
        try
        {
            // The other exe lets go after 200 ms by itself, well before the next try 3 seconds later.
            using var other = new OtherExe(store.KeysPath, TimeSpan.FromMilliseconds(200));
            var keep = ReportReplies.KeepSentAsync(store, Code, key, null, _ => { }, TimeSpan.FromSeconds(3));
            Assert.True(await keep);
            Assert.Contains(key, File.ReadAllText(store.KeysPath));
            Assert.Empty(store.PendingCodes());
        }
        finally
        {
            ReportReplyStore.FileWait = wait;
        }

        // The window waits for it off the window thread and logs a key lost on close.
        var window = SourceFiles.Read(Path.Combine("src", "BannerlordCoopLauncher", "ReportReplyWindow.cs"));
        Assert.Contains("_log(string.Format(ReportReplies.KeyLostText, code));", window);
    }

    // ----- Fake worker -----

    private sealed class Recorder(string body, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public List<string> Bodies { get; } = new();
        public List<HttpMethod> Methods { get; } = new();
        public List<string> Uris { get; } = new();
        public List<string?> ContentTypes { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Methods.Add(request.Method);
            Uris.Add(request.RequestUri!.ToString());
            ContentTypes.Add(request.Content?.Headers.ContentType?.MediaType);
            Bodies.Add(request.Content == null ? "" : Encoding.Latin1.GetString(await request.Content.ReadAsByteArrayAsync(cancellationToken)));
            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }

    private sealed class Throwing : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("offline");
    }
}
