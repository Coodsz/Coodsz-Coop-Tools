using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using BannerlordCoopLauncher.Core.Support;

namespace BannerlordCoopLauncher.Tests;

/// <summary>
/// The v1.3.3 My reports layout, the tool side: the status badge (from the worker's status and what the
/// player did), Yes, it's fixed (POST /replies/solved), No, still broken (packed like Send my logs and
/// sent with summary.followUp), the reply as numbered steps, the unread badge and the report title.
/// A fake worker here, never the live one.
/// </summary>
public sealed class ReportRepliesLayoutTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "coodsz-replies-layout-" + Guid.NewGuid().ToString("N"));
    private DateTime _now = new(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);

    private const string Code = "CR-7KQ2M";
    private const string OtherCode = "CR-ABCDE";

    public ReportRepliesLayoutTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, true); }
        catch (IOException) { }
    }

    private string Product => Path.Combine(_root, "Coop");

    private ReportReplyStore Store(string role = "Server", string key = "coop-server") =>
        new(Path.Combine(Product, role), key, [Product, Path.Combine(Product, "Server"), Path.Combine(Product, "Client")], () => _now);

    // ----- The status badge -----

    [Fact]
    public void The_status_comes_from_the_newest_of_the_answer_the_players_answer_and_yes()
    {
        var answer = new DateTime(2026, 11, 1, 10, 0, 0, DateTimeKind.Utc);
        Assert.Equal(ReportStatusKind.Looking, ReportStatus.Of(null, null, null).Kind);
        Assert.Equal(ReportStatusKind.Answered, ReportStatus.Of(null, answer, null).Kind);
        Assert.Equal(ReportStatusKind.Answered, ReportStatus.Of(new ReportState { Status = ReportStatus.Answered }, answer, null).Kind);

        // No, still broken after the answer: Looking into it, whatever the worker said before.
        var fixedState = new ReportState { Status = ReportStatus.Fixed, FixedIn = "1.3.3", StatusAt = answer };
        Assert.Equal(ReportStatusKind.Looking, ReportStatus.Of(fixedState, answer, answer.AddMinutes(5)).Kind);
        Assert.Equal(new ReportStatus(ReportStatusKind.Fixed, "1.3.3"), ReportStatus.Of(fixedState, answer, null));

        // Yes, it's fixed after the answer: Solved; It broke again after that: Looking into it.
        var solved = new ReportState { Status = ReportStatus.Answered, SolvedAt = answer.AddMinutes(1) };
        Assert.Equal(ReportStatusKind.Solved, ReportStatus.Of(solved, answer, null).Kind);
        Assert.Equal(ReportStatusKind.Looking, ReportStatus.Of(solved, answer, answer.AddMinutes(2)).Kind);
        // A newer answer after It broke again: the worker's status again.
        Assert.Equal(ReportStatusKind.Answered, ReportStatus.Of(solved, answer.AddMinutes(3), answer.AddMinutes(2)).Kind);

        // The player-solved label from the other PC or exe shows Solved; a fixed label with no version too.
        Assert.Equal(ReportStatusKind.Solved, ReportStatus.Of(new ReportState { Status = ReportStatus.Solved }, answer, null).Kind);
        Assert.Equal(ReportStatusKind.Solved, ReportStatus.Of(new ReportState { Status = ReportStatus.Fixed, FixedIn = "next" }, answer, null).Kind);
        // The worker said looking before this PC got the answer: the answer counts.
        Assert.Equal(ReportStatusKind.Answered, ReportStatus.Of(new ReportState { Status = ReportStatus.Looking, StatusAt = answer.AddMinutes(-1) }, answer, null).Kind);
        Assert.Equal(ReportStatusKind.Looking, ReportStatus.Of(new ReportState { Status = ReportStatus.Looking, StatusAt = answer.AddMinutes(1) }, answer, null).Kind);

        // A fixed-in label the worker first reported after the player's No wins without a new /reply.
        var labeledLater = new ReportState { Status = ReportStatus.Fixed, FixedIn = "1.3.4", StatusAt = answer.AddMinutes(10) };
        Assert.Equal(new ReportStatus(ReportStatusKind.Fixed, "1.3.4"), ReportStatus.Of(labeledLater, answer, answer.AddMinutes(5)));
    }

    [Fact]
    public async Task The_check_keeps_the_workers_status_and_the_badge_shows_fixed_in_the_version()
    {
        var store = Store();
        store.Remember(Code, ReportReplies.NewKey());
        store.Remember(OtherCode, ReportReplies.NewKey());
        var body = JsonSerializer.Serialize(new
        {
            ok = true,
            next = 1800,
            replies = new object[]
            {
                new { code = Code, items = new[] { new { id = "1", at = "2026-11-01T11:00:00Z", text = "Update to 1.3.3.", buttons = Array.Empty<string>() } }, status = "fixed", fixedIn = "1.3.3" },
                new { code = OtherCode, items = Array.Empty<object>(), status = "looking" },
                new { code = "CR-ZZZZZ", items = Array.Empty<object>(), status = "answered" }
            }
        });
        var check = await ReportReplies.CheckAsync(new HttpClient(new Recorder(body)), store);
        Assert.True(check.Called);
        Assert.Equal(new ReportStatus(ReportStatusKind.Fixed, "1.3.3"), store.StatusOf(Code));
        Assert.Equal(ReportStatusKind.Looking, store.StatusOf(OtherCode).Kind);
        Assert.Null(store.State("CR-ZZZZZ"));
        var statuses = store.Statuses();
        Assert.Equal(ReportStatusKind.Fixed, statuses[Code].Kind);
        Assert.Equal(ReportStatusKind.Looking, statuses[OtherCode].Kind);

        // An older worker sends no status: the badge comes from the answers alone.
        Assert.Empty(ReportReplies.ParseStatuses("{\"replies\":[{\"code\":\"CR-7KQ2M\",\"items\":[]}]}", [Code]));
        Assert.Empty(ReportReplies.ParseStatuses("{\"replies\":[{\"code\":\"CR-7KQ2M\",\"status\":\"closed\"}]}", [Code]));
        Assert.Empty(ReportReplies.ParseStatuses("not json", [Code]));
        Assert.Null(ReportReplies.ParseStatuses("{\"replies\":[{\"code\":\"CR-7KQ2M\",\"status\":\"fixed\",\"fixedIn\":\"soon\"}]}", [Code])[Code].FixedIn);
    }

    // ----- Yes, it's fixed -----

    [Fact]
    public async Task Yes_posts_only_the_code_and_key_to_replies_solved_and_reads_the_answer()
    {
        var key = ReportReplies.NewKey();
        var worker = new Recorder("{\"ok\":true,\"solved\":true,\"action\":\"closed\"}");
        Assert.Equal(SolvedResult.Done, await ReportReplies.MarkSolvedAsync(new HttpClient(worker), Code, key));
        Assert.EndsWith("/replies/solved", worker.Uris.Single());
        using (var doc = JsonDocument.Parse(worker.Bodies.Single()))
        {
            Assert.Equal("CoodszReplySolved", doc.RootElement.GetProperty("kind").GetString());
            Assert.Equal(Code, doc.RootElement.GetProperty("code").GetString());
            Assert.Equal(key, doc.RootElement.GetProperty("key").GetString());
            Assert.Equal(4, doc.RootElement.EnumerateObject().Count());
        }

        Assert.Equal(SolvedResult.Unknown, await ReportReplies.MarkSolvedAsync(new HttpClient(new Recorder("{\"ok\":true,\"solved\":false}")), Code, key));
        Assert.Equal(SolvedResult.Busy, await ReportReplies.MarkSolvedAsync(new HttpClient(new Recorder("{}", HttpStatusCode.TooManyRequests)), Code, key));
        // A worker without the endpoint yet (before the deploy) answers 404: Could not reach, try later.
        Assert.Equal(SolvedResult.Failed, await ReportReplies.MarkSolvedAsync(new HttpClient(new Recorder("{\"ok\":false}", HttpStatusCode.NotFound)), Code, key));
        Assert.Equal(SolvedResult.Failed, await ReportReplies.MarkSolvedAsync(new HttpClient(new Throwing()), Code, key));
        var none = new Recorder("{}");
        Assert.Equal(SolvedResult.Unknown, await ReportReplies.MarkSolvedAsync(new HttpClient(none), Code, "short"));
        Assert.Empty(none.Uris);
    }

    [Fact]
    public void Yes_and_the_players_answers_are_kept_per_exe_and_read_from_both()
    {
        var server = Store();
        server.Remember(Code, ReportReplies.NewKey());
        server.MarkSolved(Code);
        Assert.Equal(ReportStatusKind.Solved, server.StatusOf(Code).Kind);

        // The Client Launcher of the same product sees it too, and its own answer after it wins.
        var client = Store("Client", "coop-client");
        Assert.Equal(ReportStatusKind.Solved, client.StatusOf(Code).Kind);
        _now = _now.AddMinutes(5);
        client.AddFollowUp(new ReportFollowUp
        {
            Code = Code, At = _now, Steps = FollowUpSteps.Some, Text = "Still closes at 192.168.1.20, see C:\\Users\\Bob\\x.log", Logs = true, NewCode = OtherCode
        });
        Assert.Equal(ReportStatusKind.Looking, server.StatusOf(Code).Kind);
        var own = Assert.Single(server.FollowUps(Code));
        Assert.Equal(FollowUpSteps.Some, own.Steps);
        Assert.True(own.Logs);
        Assert.DoesNotContain("192.168.1.20", own.Text);
        Assert.DoesNotContain("Bob", own.Text);
        Assert.Contains("Still closes", own.Text);

        // Junk in the file is dropped, and at most 20 answers per report are kept.
        for (var i = 0; i < 25; i++)
        {
            _now = _now.AddMinutes(1);
            client.AddFollowUp(new ReportFollowUp { Code = Code, At = _now, Steps = "maybe", Text = "again " + i });
        }

        var kept = client.FollowUps(Code);
        Assert.Equal(ReportReplyStore.MaxFollowUpsPerReport, kept.Count);
        Assert.Equal("again 24", kept[^1].Text);
        Assert.All(kept, followUp => Assert.True(FollowUpSteps.IsKnown(followUp.Steps)));
        client.AddFollowUp(new ReportFollowUp { Code = "CR-1", Text = "no code" });
        Assert.Empty(client.FollowUps("CR-1"));
    }

    // ----- No, still broken -----

    private CrashReportRequest Request(string folder) => new()
    {
        Tool = "coop",
        Side = "server",
        Reason = CrashReason.Logs,
        ToolVersion = "1.3.3",
        Secrets = ["hunter2"],
        FileStem = "coodsz-host-logs",
        Modules = [("Coop", "v0.1.6")],
        Collect = (zip, limits) =>
        {
            using var archive = ZipFile.Open(zip, ZipArchiveMode.Create);
            using var writer = new StreamWriter(archive.CreateEntry("server/rgl_log.txt").Open());
            writer.Write("the server log");
            return [];
        },
        FindDump = () => throw new InvalidOperationException("No dump is ever looked for in a follow up.")
    };

    // A PNG with a text chunk that must not reach the zip (the CRCs are not checked).
    private static byte[] Png()
    {
        static byte[] Chunk(string type, byte[] data)
        {
            var bytes = new byte[12 + data.Length];
            bytes[0] = (byte)(data.Length >> 24);
            bytes[1] = (byte)(data.Length >> 16);
            bytes[2] = (byte)(data.Length >> 8);
            bytes[3] = (byte)data.Length;
            Encoding.ASCII.GetBytes(type).CopyTo(bytes, 4);
            data.CopyTo(bytes, 8);
            return bytes;
        }

        return new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }
            .Concat(Chunk("IHDR", new byte[13]))
            .Concat(Chunk("tEXt", Encoding.ASCII.GetBytes("Author\0SECRET-AUTHOR")))
            .Concat(Chunk("IDAT", new byte[] { 1, 2, 3 }))
            .Concat(Chunk("IEND", []))
            .ToArray();
    }

    private static (string Summary, byte[] Zip) Parts(Recorder worker)
    {
        var body = worker.Bodies.Single();
        var at = body.IndexOf("name=\"zip\"", StringComparison.Ordinal);
        var start = body.IndexOf("PK", at, StringComparison.Ordinal);
        var end = body.LastIndexOf("\r\n--", StringComparison.Ordinal);
        return (body[..at], Encoding.Latin1.GetBytes(body[start..end]));
    }

    private static Dictionary<string, string> Entries(byte[] zip)
    {
        using var archive = new ZipArchive(new MemoryStream(zip));
        return archive.Entries.ToDictionary(entry => entry.FullName, entry =>
        {
            using var reader = new StreamReader(entry.Open(), Encoding.Latin1);
            return reader.ReadToEnd();
        });
    }

    [Fact]
    public async Task Still_broken_is_packed_like_send_my_logs_and_carries_the_code_key_and_steps_only_in_the_summary()
    {
        var key = ReportReplies.NewKey();
        var worker = new Recorder("{\"ok\":true,\"code\":\"" + OtherCode + "\",\"filed\":true,\"followUp\":\"" + Code + "\"}");
        var shot = new ReportScreenshot("shot.png", Png(), ReportImageKind.Png);
        var form = new FollowUpForm(Code, key, FollowUpSteps.All, "Still closes at 10.0.0.5 with password hunter2 after Loading campaign", true, shot);
        var result = await ReportFollowUps.SendAsync(new HttpClient(worker), Request(_root), "0123456789abcdef0123456789abcdef", form, folder: _root);

        Assert.Equal(FollowUpStatus.Sent, result.Status);
        Assert.Equal(OtherCode, result.NewCode);
        Assert.DoesNotContain("10.0.0.5", result.CleanedText);
        Assert.DoesNotContain("hunter2", result.CleanedText);

        var (summary, zip) = Parts(worker);
        Assert.Contains("\"followUp\"", summary);
        Assert.Contains("\"key\": \"" + key + "\"", summary);
        Assert.Contains("\"steps\": \"all\"", summary);
        Assert.Contains("\"reason\": \"logs\"", summary);
        Assert.DoesNotContain("replyKey", summary);
        Assert.DoesNotContain(key, Encoding.Latin1.GetString(zip));

        var entries = Entries(zip);
        Assert.Contains("server/rgl_log.txt", entries.Keys);
        var notes = entries[ReportExtras.NotesName];
        Assert.Contains("Loading campaign", notes);
        Assert.DoesNotContain("10.0.0.5", notes);
        Assert.DoesNotContain("hunter2", notes);
        var picture = Assert.Single(entries.Keys, name => name.StartsWith(ReportExtras.ScreenshotFolder, StringComparison.Ordinal));
        Assert.DoesNotContain("SECRET-AUTHOR", entries[picture]);
        Assert.DoesNotContain(entries.Keys, name => name.EndsWith(".dmp", StringComparison.Ordinal));
        // Nothing is left behind in the temp folder.
        Assert.Empty(Directory.GetFiles(_root, "*.zip"));
    }

    [Fact]
    public async Task Still_broken_without_the_logs_ticked_sends_only_the_words()
    {
        var worker = new Recorder("{\"ok\":true,\"code\":\"" + OtherCode + "\",\"filed\":true}");
        var form = new FollowUpForm(Code, ReportReplies.NewKey(), FollowUpSteps.None, "I could not find the mod list.", false, null);
        Assert.Equal(FollowUpStatus.Sent, (await ReportFollowUps.SendAsync(new HttpClient(worker), Request(_root), "0123456789abcdef0123456789abcdef", form, folder: _root)).Status);
        var entries = Entries(Parts(worker).Zip);
        Assert.DoesNotContain("server/rgl_log.txt", entries.Keys);
        Assert.Contains(ReportExtras.NotesName, entries.Keys);
        Assert.Contains("\"steps\": \"none\"", Parts(worker).Summary);
    }

    [Fact]
    public async Task Still_broken_needs_words_a_real_picture_and_says_why_it_failed()
    {
        var key = ReportReplies.NewKey();
        var none = new Recorder("{}");
        Assert.Equal(FollowUpStatus.Empty, (await ReportFollowUps.SendAsync(new HttpClient(none), Request(_root), "x", new FollowUpForm(Code, key, FollowUpSteps.All, "   ", true, null), folder: _root)).Status);
        var fake = new ReportScreenshot("shot.png", Encoding.ASCII.GetBytes("MZ not a picture"), ReportImageKind.Png);
        Assert.Equal(FollowUpStatus.LocalFailed, (await ReportFollowUps.SendAsync(new HttpClient(none), Request(_root), "x", new FollowUpForm(Code, key, FollowUpSteps.All, "words", true, fake), folder: _root)).Status);
        var big = new ReportScreenshot("big.png", Png().Concat(new byte[ReportFollowUps.MaxScreenshotBytes]).ToArray(), ReportImageKind.Png);
        Assert.Equal(FollowUpStatus.LocalFailed, (await ReportFollowUps.SendAsync(new HttpClient(none), Request(_root), "x", new FollowUpForm(Code, key, FollowUpSteps.All, "words", true, big), folder: _root)).Status);
        Assert.Empty(none.Uris);

        var form = new FollowUpForm(Code, key, FollowUpSteps.Some, "words", false, null);
        Assert.Equal(FollowUpStatus.TooOld, (await ReportFollowUps.SendAsync(new HttpClient(new Recorder("{\"ok\":false,\"error\":\"unknown or expired report\"}", HttpStatusCode.NotFound)), Request(_root), "0123456789abcdef0123456789abcdef", form, folder: _root)).Status);
        Assert.Equal(FollowUpStatus.Busy, (await ReportFollowUps.SendAsync(new HttpClient(new Recorder("{\"ok\":false}", HttpStatusCode.TooManyRequests)), Request(_root), "0123456789abcdef0123456789abcdef", form, folder: _root)).Status);
        Assert.Equal(FollowUpStatus.Failed, (await ReportFollowUps.SendAsync(new HttpClient(new Throwing()), Request(_root), "0123456789abcdef0123456789abcdef", form, folder: _root)).Status);
    }

    [Fact]
    public async Task Still_broken_counts_as_sent_only_when_the_worker_says_it_is_on_the_report()
    {
        var form = new FollowUpForm(Code, ReportReplies.NewKey(), FollowUpSteps.All, "still broken", false, null);
        async Task<FollowUpStatus> With(string body, HttpStatusCode status = HttpStatusCode.OK) =>
            (await ReportFollowUps.SendAsync(new HttpClient(new Recorder(body, status)), Request(_root), "0123456789abcdef0123456789abcdef", form, folder: _root)).Status;

        Assert.Equal(FollowUpStatus.Sent, await With("{\"ok\":true,\"code\":\"" + OtherCode + "\",\"filed\":true,\"followUp\":\"" + Code + "\"}"));
        // A code without filed true means the comment never landed: try again, never "your answer was added".
        Assert.Equal(FollowUpStatus.Refused, await With("{\"ok\":true,\"code\":\"" + OtherCode + "\",\"filed\":false}"));
        Assert.Equal(FollowUpStatus.Refused, await With("{\"ok\":true,\"code\":\"" + OtherCode + "\"}"));
        // The worker's 502 when GitHub took no comment: the worker was reached, so not the connection.
        Assert.Equal(FollowUpStatus.Refused, await With("{\"ok\":false,\"error\":\"the answer could not be added to the report, try again later\",\"filed\":false}", HttpStatusCode.BadGateway));

        Assert.True(CrashReports.ReadFiled("{\"filed\":true}"));
        Assert.False(CrashReports.ReadFiled("{\"filed\":false}"));
        Assert.Null(CrashReports.ReadFiled("{\"filed\":\"true\"}"));
        Assert.Null(CrashReports.ReadFiled("junk"));
    }

    [Fact]
    public async Task Still_broken_cuts_a_long_answer_without_splitting_a_surrogate_pair()
    {
        var worker = new Recorder("{\"ok\":true,\"code\":\"" + OtherCode + "\",\"filed\":true}");
        var text = new string('a', ReportFollowUps.MaxText - 1) + "\U0001F600 and more";
        var result = await ReportFollowUps.SendAsync(new HttpClient(worker), Request(_root), "0123456789abcdef0123456789abcdef",
            new FollowUpForm(Code, ReportReplies.NewKey(), FollowUpSteps.All, text, false, null), folder: _root);
        Assert.Equal(FollowUpStatus.Sent, result.Status);
        Assert.False(char.IsHighSurrogate(result.CleanedText[^1]));
        Assert.Equal(ReportFollowUps.MaxText - 1, result.CleanedText.Length);

        // An emoji joined with ZWJ and a letter with its combining mark are kept whole or left out whole.
        const string family = "\U0001F468‍\U0001F469‍\U0001F467";
        Assert.Equal("ab", ReportFollowUps.Cut("ab" + family, 6));
        Assert.Equal("ab" + family, ReportFollowUps.Cut("ab" + family + "c", 2 + family.Length));
        Assert.Equal("a", ReportFollowUps.Cut("aé", 2));
        Assert.Equal("short", ReportFollowUps.Cut("short", 10));
    }

    [Fact]
    public void A_failed_send_keeps_the_form_and_the_card_picks_one_thing_under_the_reply()
    {
        // The form while it is open, whether a send runs or failed (busy is not part of the rule any more).
        Assert.Equal(CardBottom.Form, ReportReplyText.BottomOf(formOpen: true, canAct: true, solved: false, waiting: false, hasAnswer: true));
        Assert.Equal(CardBottom.Form, ReportReplyText.BottomOf(formOpen: true, canAct: true, solved: true, waiting: true, hasAnswer: true));
        Assert.Equal(CardBottom.Solved, ReportReplyText.BottomOf(formOpen: false, canAct: true, solved: true, waiting: false, hasAnswer: true));
        Assert.Equal(CardBottom.Question, ReportReplyText.BottomOf(formOpen: false, canAct: true, solved: false, waiting: false, hasAnswer: true));
        Assert.Equal(CardBottom.None, ReportReplyText.BottomOf(formOpen: false, canAct: true, solved: false, waiting: true, hasAnswer: true));
        Assert.Equal(CardBottom.None, ReportReplyText.BottomOf(formOpen: true, canAct: false, solved: false, waiting: false, hasAnswer: true));

        // In the window: the busy flag only turns the buttons off, and a failed send leaves the form open with the words.
        var window = SourceFiles.Read(Path.Combine("src", "BannerlordCoopLauncher", "ReportReplyWindow.cs"));
        Assert.Contains("var canAct = hasCode && entry != null && ReportReplies.IsKey(entry.Key);", window);
        Assert.Contains("send.IsEnabled = !_busy;", window);
        var send = SourceFiles.Method(window, "private async Task SendAsync()");
        var afterFailure = send[send.IndexOf("_tooOld = result.Status == FollowUpStatus.TooOld;", StringComparison.Ordinal)..];
        // Only a report too old for an answer closes the form (its Send my logs again link must show).
        Assert.DoesNotContain("_formOpen = false", System.Text.RegularExpressions.Regex.Replace(afterFailure, @"if \(_tooOld\)\r?\n\s*_formOpen = false;", ""));
        // The words are cleared once, on the success path before the failure part, so a failed send keeps them.
        var cleared = send.IndexOf("_words.Text = \"\"", StringComparison.Ordinal);
        Assert.True(cleared >= 0 && cleared < send.Length - afterFailure.Length, "Only a sent answer clears the words.");
        Assert.Equal(-1, send.IndexOf("_words.Text = \"\"", cleared + 1, StringComparison.Ordinal));
    }

    [Fact]
    public void My_reports_sorts_by_when_coodsz_replied_not_when_this_pc_saved_it()
    {
        var store = Store();
        store.Remember(Code, ReportReplies.NewKey());
        store.Remember(OtherCode, ReportReplies.NewKey());
        string Body(string code, string id, string at) =>
            "{\"ok\":true,\"next\":1800,\"replies\":[{\"code\":\"" + code + "\",\"status\":\"answered\",\"items\":[{\"id\":\"" + id + "\",\"at\":\"" + at + "\",\"text\":\"Answer " + id + "\",\"buttons\":[]}]}]}";
        // The newer reply (11:00) is saved first, the older one (10:00) is saved later, at 12:00.
        _now = new DateTime(2026, 11, 1, 11, 30, 0, DateTimeKind.Utc);
        store.TakeNew(ReportReplies.ParseResponse(Body(OtherCode, "2", "2026-11-01T11:00:00Z"), [OtherCode]).Items);
        _now = new DateTime(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);
        store.TakeNew(ReportReplies.ParseResponse(Body(Code, "1", "2026-11-01T10:00:00Z"), [Code]).Items);
        Assert.Equal(new[] { OtherCode, Code }, store.Rows().Select(row => row.Code));

        // Too old while the No form is open: the form closes, so the Send my logs again link shows.
        var window = SourceFiles.Read(Path.Combine("src", "BannerlordCoopLauncher", "ReportReplyWindow.cs"));
        var send = SourceFiles.Method(window, "private async Task SendAsync()");
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(send, @"if \(_tooOld\)\r?\n\s*_formOpen = false;").Count);
        Assert.Contains("_notice.BringIntoView();", SourceFiles.Method(window, "private void ShowForm()"));
    }

    [Fact]
    public void A_row_in_my_reports_shows_when_coodsz_replied_like_the_card()
    {
        var sent = new DateTime(2026, 11, 1, 9, 0, 0, DateTimeKind.Utc);
        var saved = new DateTime(2026, 11, 2, 18, 30, 0, DateTimeKind.Utc);
        var answer = new ReportAnswer { Code = Code, Id = "1", At = "2026-11-01T11:00:00Z", SavedAt = saved };
        Assert.Equal(new DateTime(2026, 11, 1, 11, 0, 0, DateTimeKind.Utc), ReportReplies.RowTime(new MyReportRow(Code, sent, answer)));
        Assert.Equal(sent, ReportReplies.RowTime(new MyReportRow(Code, sent, null)));
        Assert.Equal(saved, ReportReplies.RowTime(new MyReportRow("", null, new ReportAnswer { Source = AnswerSource.Known, At = "", SavedAt = saved })));

        var window = SourceFiles.Read(Path.Combine("src", "BannerlordCoopLauncher", "ReportReplyWindow.cs"));
        Assert.Contains("var when = ReportReplies.RowTime(row);", window);
        Assert.Contains("public static string AnswerDate(ReportAnswer answer) => Date(ReportReplies.AnswerTime(answer));", window);
        // The open form scrolls its Send row into view after layout, also after a failed send.
        Assert.Contains("row.BringIntoView();", SourceFiles.Method(window, "private void ShowForm()"));
        Assert.Contains("ShowForm();", SourceFiles.Method(window, "private async Task SendAsync()"));
    }

    [Fact]
    public void The_badge_tip_says_one_report_or_reports()
    {
        Assert.Equal("1 report with a new answer", ReportReplyText.NewAnswersLabel(1));
        Assert.Equal(ReportReplyText.NewAnswers, ReportReplyText.NewAnswersLabel(3));
    }

    [Fact]
    public async Task An_old_reply_the_other_exe_saves_after_a_newer_no_does_not_bring_answered_back()
    {
        var server = Store();
        server.Remember(Code, ReportReplies.NewKey());
        var client = Store("Client", "coop-client");
        // The player answered No at 12:05 in the Client Launcher...
        _now = new DateTime(2026, 11, 1, 12, 5, 0, DateTimeKind.Utc);
        client.AddFollowUp(new ReportFollowUp { Code = Code, At = _now, Steps = FollowUpSteps.All, Text = "still broken" });
        // ...and the Server Tool only now, at 12:10, saves Coodsz's reply from 11:00.
        _now = new DateTime(2026, 11, 1, 12, 10, 0, DateTimeKind.Utc);
        var body = "{\"ok\":true,\"next\":1800,\"replies\":[{\"code\":\"" + Code + "\",\"status\":\"looking\",\"items\":[{\"id\":\"9\",\"at\":\"2026-11-01T11:00:00Z\",\"text\":\"Try this.\",\"buttons\":[]}]}]}";
        Assert.Single((await ReportReplies.CheckAsync(new HttpClient(new Recorder(body)), server)).NewItems);
        Assert.Equal(ReportStatusKind.Looking, server.StatusOf(Code).Kind);
        Assert.Equal(ReportStatusKind.Looking, server.Statuses()[Code].Kind);
        Assert.Equal(new DateTime(2026, 11, 1, 11, 0, 0, DateTimeKind.Utc), ReportReplies.AnswerTime(server.Answers().Single()));
        Assert.Equal(_now, ReportReplies.AnswerTime(new ReportAnswer { At = "not a date", SavedAt = _now }));
    }

    [Fact]
    public async Task A_follow_up_too_large_for_the_worker_is_a_local_problem_not_try_again_later()
    {
        var form = new FollowUpForm(Code, ReportReplies.NewKey(), FollowUpSteps.All, "still broken", false, null);
        var result = await ReportFollowUps.SendAsync(new HttpClient(new Recorder("{\"ok\":false,\"error\":\"too large\"}", HttpStatusCode.RequestEntityTooLarge)),
            Request(_root), "0123456789abcdef0123456789abcdef", form, folder: _root);
        Assert.Equal(FollowUpStatus.LocalFailed, result.Status);
    }

    [Fact]
    public void The_card_keeps_typed_words_safe_and_catches_a_broken_picture()
    {
        var window = SourceFiles.Read(Path.Combine("src", "BannerlordCoopLauncher", "ReportReplyWindow.cs"));
        // No MaxLength (it splits a pasted emoji); the box cuts with the same whole character Cut.
        Assert.DoesNotContain("MaxLength =", window);
        Assert.Contains("_words.Text = ReportFollowUps.Cut(_words.Text, ReportFollowUps.MaxText);", window);
        // Send my logs again closes the window, so it is not offered while the No form is open.
        Assert.Contains("View.Children.Add(OtherRoutes(!_formOpen && (", window);
        // A broken picture header throws FileFormatException, a FormatException.
        Assert.Contains("or FormatException", SourceFiles.Method(window, "private async Task PickScreenshotAsync()"));
        // A file that cannot be read now is not "too old".
        var send = SourceFiles.Method(window, "private async Task SendAsync()");
        Assert.Contains("_tooOld = entry == null;", send);
        Assert.DoesNotContain("Try(() => _tools.Store.Entry(_code))", send);
    }

    // ----- The reply, the badge on My reports and the title -----

    [Fact]
    public void The_reply_keeps_its_numbered_steps_and_puts_the_discord_line_last()
    {
        var blocks = ReportReplies.ReplyBlocks("Hey, thanks for the logs.\nIf it still crashes, ask in the Coop Discord and mention CR-55WBT.\n1. Turn off Europe 1100\n2) Pick Vanilla campaign map\n\n3. Press Start server again");
        Assert.Equal(new[] { "Hey, thanks for the logs.", "Turn off Europe 1100", "Pick Vanilla campaign map", "Press Start server again", "If it still crashes, ask in the Coop Discord and mention CR-55WBT." },
            blocks.Select(block => block.Text));
        Assert.Equal(new int?[] { null, 1, 2, 3, null }, blocks.Select(block => block.Step));
        Assert.Empty(ReportReplies.ReplyBlocks(null));
    }

    [Fact]
    public void The_badge_counts_reports_with_an_unread_answer_not_answers()
    {
        var answers = new[]
        {
            new ReportAnswer { Code = Code, Id = "1", Read = false },
            new ReportAnswer { Code = Code, Id = "2", Read = false },
            new ReportAnswer { Code = OtherCode, Id = "3", Read = true },
            new ReportAnswer { Code = "", Id = "known-a", Source = AnswerSource.Known, Read = false },
            new ReportAnswer { Code = "", Id = "known-b", Source = AnswerSource.Known, Read = false }
        };
        Assert.Equal(3, ReportReplies.UnreadReports(answers));
    }

    [Fact]
    public async Task A_sent_report_keeps_a_short_clean_title_and_its_reason()
    {
        var store = Store();
        Assert.True(await ReportReplies.KeepSentAsync(store, Code, ReportReplies.NewKey(), null, _ => { },
            title: "Server crashed on start at 10.1.2.3 in C:\\Users\\Bob\\Documents " + new string('x', 200), reason: CrashReason.ServerCrash));
        var entry = store.Entry(Code)!;
        Assert.Equal(CrashReason.ServerCrash, entry.Reason);
        Assert.StartsWith("Server crashed on start", entry.Title);
        Assert.DoesNotContain("10.1.2.3", entry.Title);
        Assert.DoesNotContain("Bob", entry.Title);
        Assert.True(entry.Title!.Length <= 80);
        Assert.Equal(ReportReplyText.ServerCrashTitle, ReportReplyText.ReasonTitle(CrashReason.ServerCrash));
        Assert.Equal(ReportReplyText.LogsTitle, ReportReplyText.ReasonTitle("odd"));

        store.Remember(OtherCode, ReportReplies.NewKey(), reason: "made-up");
        Assert.Null(store.Entry(OtherCode)!.Reason);
        Assert.Equal("", store.Entry(OtherCode)!.Title);
    }

    private sealed class Recorder(string body, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public List<string> Bodies { get; } = new();
        public List<string> Uris { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Uris.Add(request.RequestUri!.ToString());
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
