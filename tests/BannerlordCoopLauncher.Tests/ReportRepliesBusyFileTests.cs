using System.Net;
using System.Text;
using System.Text.Json;
using BannerlordCoopLauncher.Core.Support;

namespace BannerlordCoopLauncher.Tests;

/// <summary>
/// My reports when a file is busy (the other exe or an antivirus holds it): a No, still broken the worker took is kept
/// in memory and saved later, never lost; a keys file that can't be read is never taken for a report older than 30
/// days; and an answer Coodsz deleted stops showing as current only when the worker sent its full list.
/// </summary>
// The same collection as ReportRepliesTests: both change the static ReportReplyStore.FileWait.
[Collection("Report reply files")]
public sealed class ReportRepliesBusyFileTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "coodsz-busy-file-" + Guid.NewGuid().ToString("N"));
    private DateTime _now = new(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);
    private const string Code = "CR-7KQ2M";

    public ReportRepliesBusyFileTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, true); }
        catch (IOException) { }
    }

    private ReportReplyStore Store() => new(Path.Combine(_root, "Client"), "coop", [_root], () => _now);

    // Holds the file's named mutex on another thread, as the other exe does while it saves.
    private static void WhileHeld(string path, Action work)
    {
        var wait = ReportReplyStore.FileWait;
        using var held = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var holder = new Thread(() =>
        {
            using var mutex = new Mutex(false, ReportReplyStore.MutexName(path));
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
            work();
        }
        finally
        {
            release.Set();
            holder.Join();
            ReportReplyStore.FileWait = wait;
        }
    }

    [Fact]
    public void A_no_still_broken_the_worker_took_is_kept_in_memory_while_the_answers_file_is_busy_and_saved_at_the_next_check()
    {
        var store = Store();
        store.Remember(Code, ReportReplies.NewKey());
        store.TakeNew([new ReplyItem(Code, "123", "2026-11-01T10:00:00Z", "Turn off RBM.", false, false)]);
        _now = _now.AddMinutes(5);
        store.MarkSolved(Code);
        Assert.Equal(ReportStatusKind.Solved, store.StatusOf(Code).Kind);

        _now = _now.AddMinutes(5);
        var followUp = new ReportFollowUp { Code = Code, At = _now, Steps = FollowUpSteps.All, Text = "Still crashes." };
        var saved = true;
        WhileHeld(store.AnswersPath, () => saved = store.KeepFollowUp(followUp));
        Assert.False(saved);
        Assert.Equal([Code], store.PendingFollowUpCodes());
        // It counts at once: the old Solved does not hide the worker's Looking into it.
        Assert.Equal(ReportStatusKind.Looking, store.StatusOf(Code).Kind);
        Assert.Single(store.FollowUps(Code));
        Assert.DoesNotContain("Still crashes.", File.ReadAllText(store.AnswersPath));

        // The next check prunes, which writes it, once.
        store.Prune();
        Assert.Empty(store.PendingFollowUpCodes());
        Assert.Contains("Still crashes.", File.ReadAllText(store.AnswersPath));
        Assert.Single(store.FollowUps(Code));
        Assert.Equal(ReportStatusKind.Looking, store.StatusOf(Code).Kind);
    }

    [Fact]
    public void A_waiting_no_still_broken_is_also_saved_by_any_other_write_of_the_answers_file()
    {
        var store = Store();
        store.Remember(Code, ReportReplies.NewKey());
        WhileHeld(store.AnswersPath, () => Assert.False(store.KeepFollowUp(new ReportFollowUp { Code = Code, At = _now, Text = "Still." })));
        store.MarkSolved(Code);
        Assert.Empty(store.PendingFollowUpCodes());
        Assert.Contains("Still.", File.ReadAllText(store.AnswersPath));
    }

    [Fact]
    public void The_window_keeps_the_follow_up_and_logs_when_it_waits()
    {
        var window = SourceFiles.Read(Path.Combine("src", "BannerlordCoopLauncher", "ReportReplyWindow.cs"));
        Assert.Contains("var saved = _tools.Store.KeepFollowUp(new ReportFollowUp", window);
        Assert.Contains("_tools.Log(string.Format(ReportReplies.FollowUpWaitsText, _code));", window);
        Assert.Contains("_log(string.Format(ReportReplies.FollowUpLostText, code));", window);
        Assert.DoesNotContain("TryDo(() => _tools.Store.AddFollowUp(", window);
    }

    [Fact]
    public void A_keys_file_that_cannot_be_read_is_busy_not_a_report_older_than_30_days()
    {
        var store = Store();
        store.Remember(Code, ReportReplies.NewKey());
        Assert.NotNull(store.Entry(Code));
        // An antivirus or the other exe holds the keys file: reading fails.
        using (new FileStream(store.KeysPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            Assert.Throws<ReportFileBusyException>(() => store.Entry(Code));
        Assert.NotNull(store.Entry(Code));
        // Not found while every file reads fine: that is a report this PC no longer keeps.
        Assert.Null(store.Entry("CR-ABCDE"));
        _now = _now.AddDays(31);
        Assert.Null(store.Entry(Code));

        // The window keeps the form open with Try again later when Entry throws, and only a null entry is too old.
        var window = SourceFiles.Read(Path.Combine("src", "BannerlordCoopLauncher", "ReportReplyWindow.cs"));
        Assert.Contains("entry = _tools.Store.Entry(_code);\n        }\n        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)\n        {\n            // The file could not be read now; that says nothing about the report's age.\n            Notice(ReportReplyText.FollowUpLocalFailed, false);\n            return;", window.Replace("\r\n", "\n"));
    }

    // ----- An answer Coodsz deleted -----

    private static string Body(bool? full, params object[] items) => JsonSerializer.Serialize(new
    {
        ok = true,
        next = 30,
        replies = new[] { full == null ? (object)new { code = Code, items, status = "looking" } : new { code = Code, items, status = "looking", full = full.Value } }
    });

    private sealed class Answering(string body, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
    }

    private async Task<ReportReplyStore> WithAnswer()
    {
        var store = Store();
        store.Remember(Code, ReportReplies.NewKey());
        await ReportReplies.CheckAsync(new HttpClient(new Answering(Body(null, new { id = "123", at = "2026-11-01T10:00:00Z", text = "Turn off RBM.", buttons = Array.Empty<string>() }))), store);
        Assert.False(Assert.Single(store.Answers()).Replaced);
        return store;
    }

    [Fact]
    public async Task An_older_worker_or_a_partial_answer_never_sets_an_answer_aside()
    {
        // The v1.3.4 worker sends no "full", and its empty items can also mean a failed GitHub read.
        var store = await WithAnswer();
        await ReportReplies.CheckAsync(new HttpClient(new Answering(Body(null))), store);
        await ReportReplies.CheckAsync(new HttpClient(new Answering(Body(false))), store);
        await ReportReplies.CheckAsync(new HttpClient(new Answering("{\"ok\":false}", HttpStatusCode.InternalServerError)), store);
        var answer = Assert.Single(store.Answers());
        Assert.False(answer.Replaced);
        Assert.False(answer.Read);
    }

    [Fact]
    public async Task A_full_list_without_the_answer_sets_it_aside_as_replaced_and_read_and_never_touches_a_known_fix()
    {
        var store = await WithAnswer();
        store.SaveKnownFix("rbm", "Known fix text.");
        store.AttachCode("rbm", Code);
        // The same answer in a full list: nothing changes.
        await ReportReplies.CheckAsync(new HttpClient(new Answering(Body(true, new { id = "123", at = "2026-11-01T10:00:00Z", text = "Turn off RBM.", buttons = Array.Empty<string>() }))), store);
        Assert.All(store.Answers(), answer => Assert.False(answer.Replaced));
        // Coodsz deleted it: the full list is empty.
        await ReportReplies.CheckAsync(new HttpClient(new Answering(Body(true))), store);
        var reply = store.Answers().Single(answer => answer.Source == AnswerSource.Reply);
        Assert.True(reply.Replaced);
        Assert.True(reply.Read);
        Assert.False(store.Answers().Single(answer => answer.Source == AnswerSource.Known).Replaced);

        // The worker serves it again (the full list was wrong, or it came back): it shows again, unread again.
        await ReportReplies.CheckAsync(new HttpClient(new Answering(Body(true, Rbm))), store);
        reply = store.Answers().Single(answer => answer.Source == AnswerSource.Reply);
        Assert.False(reply.Replaced);
        Assert.False(reply.Read);
        Assert.False(reply.SetAsideUnread);
        Assert.False(store.Answers().Single(answer => answer.Source == AnswerSource.Known).Replaced);
        Assert.Equal(1, ReportReplies.ParseFull(Body(true), [Code]).Count);
        Assert.Empty(ReportReplies.ParseFull(Body(true), ["CR-ABCDE"]));
        Assert.Empty(ReportReplies.ParseFull("junk", [Code]));
    }

    private static readonly object Rbm = new { id = "123", at = "2026-11-01T10:00:00Z", text = "Turn off RBM.", buttons = Array.Empty<string>() };

    [Fact]
    public async Task An_answer_read_before_it_was_set_aside_comes_back_read_and_an_older_answer_comes_back_when_the_newer_is_deleted()
    {
        var store = await WithAnswer();
        store.MarkAllRead();
        await ReportReplies.CheckAsync(new HttpClient(new Answering(Body(true))), store);
        Assert.True(Assert.Single(store.Answers()).Replaced);
        await ReportReplies.CheckAsync(new HttpClient(new Answering(Body(true, Rbm))), store);
        var answer = Assert.Single(store.Answers());
        Assert.False(answer.Replaced);
        Assert.True(answer.Read);

        // A newer answer replaces it, then Coodsz deletes the newer one and the worker serves the older again.
        var newer = new { id = "456", at = "2026-11-02T10:00:00Z", text = "Update the tool.", buttons = Array.Empty<string>() };
        await ReportReplies.CheckAsync(new HttpClient(new Answering(Body(true, newer))), store);
        Assert.True(store.Answers().Single(a => a.Id == "123").Replaced);
        await ReportReplies.CheckAsync(new HttpClient(new Answering(Body(true, Rbm))), store);
        Assert.False(store.Answers().Single(a => a.Id == "123").Replaced);
        Assert.True(store.Answers().Single(a => a.Id == "456").Replaced);
        Assert.True(store.Answers().Single(a => a.Id == "456").Read, "the deleted newer answer no longer glows");
    }

    [Fact]
    public async Task An_older_answer_served_without_a_full_list_never_takes_over_from_a_newer_one_shown()
    {
        // A fresh read served the newer answer B, but the worker could not store it (the push write cap, a failed
        // store, or a KV copy 60 s behind): the next check serves the stored record's older answer A, without full.
        var store = await WithAnswer();
        var newer = new { id = "456", at = "2026-11-02T10:00:00Z", text = "Update the tool.", buttons = Array.Empty<string>() };
        await ReportReplies.CheckAsync(new HttpClient(new Answering(Body(true, newer))), store);
        Assert.True(store.Answers().Single(a => a.Id == "123").Replaced);
        await ReportReplies.CheckAsync(new HttpClient(new Answering(Body(null, Rbm))), store);
        var b = store.Answers().Single(a => a.Id == "456");
        Assert.False(b.Replaced);
        Assert.False(b.Read, "B keeps its glow");
        Assert.True(store.Answers().Single(a => a.Id == "123").Replaced);
        Assert.Equal(1, store.UnreadReports());

        // A full list that serves A (Coodsz deleted B): A comes back as before.
        await ReportReplies.CheckAsync(new HttpClient(new Answering(Body(true, Rbm))), store);
        Assert.False(store.Answers().Single(a => a.Id == "123").Replaced);
        Assert.True(store.Answers().Single(a => a.Id == "456").Replaced);
    }

    [Fact]
    public void A_keys_file_busy_for_a_moment_gives_a_retry_in_30_to_60_seconds_never_a_stop()
    {
        var store = Store();
        store.Remember(Code, ReportReplies.NewKey());
        var answered = new ReplyCheck([], TimeSpan.FromMinutes(10), true, true, TimeSpan.FromSeconds(30));
        Assert.Equal(TimeSpan.FromSeconds(30), ReplySchedule.AfterCheck(answered, _now, store.ScheduleReports)!.Wait);
        using (new FileStream(store.KeysPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            Assert.Throws<ReportKeysBusyException>(() => store.ScheduleReports());
            Assert.Equal(ReplySchedule.BusyRetry, ReplySchedule.AfterCheck(answered, _now, store.ScheduleReports));
            Assert.Equal(new ReplyWait(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30)), ReplySchedule.BusyRetry);
            // Minimized stays no check; nothing else could start the checks there anyway.
            Assert.Null(ReplySchedule.AfterCheck(answered, _now, store.ScheduleReports, minimized: true));
        }

        // No keys at all (and every file reads fine) is still no check.
        var empty = new ReportReplyStore(Path.Combine(_root, "Empty"), "coop", [Path.Combine(_root, "Empty")], () => _now);
        Assert.Null(ReplySchedule.AfterCheck(answered, _now, empty.ScheduleReports));
    }

    [Fact]
    public void A_busy_keys_file_never_asks_the_worker_sooner_than_it_asked()
    {
        var store = Store();
        store.Remember(Code, ReportReplies.NewKey());
        using var held = new FileStream(store.KeysPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        // The worker asked for an hour (its brake), then the keys file was busy: an hour, not 30 s.
        var hour = new ReplyCheck([], TimeSpan.FromHours(1), true, true, TimeSpan.FromHours(1));
        Assert.Equal(TimeSpan.FromHours(1), ReplySchedule.AfterCheck(hour, _now, store.ScheduleReports)!.Wait);
        // A 429 with 15 minutes: 15 minutes.
        var limited = new ReplyCheck([], TimeSpan.FromMinutes(15), true);
        Assert.Equal(TimeSpan.FromMinutes(15), ReplySchedule.AfterCheck(limited, _now, store.ScheduleReports)!.Wait);
        // A short next, or nothing asked: the 30 to 60 s retry.
        Assert.Equal(ReplySchedule.BusyRetry, ReplySchedule.AfterCheck(new ReplyCheck([], TimeSpan.FromMinutes(10), true, true, TimeSpan.FromSeconds(30)), _now, store.ScheduleReports));
        Assert.Equal(ReplySchedule.BusyRetry, ReplySchedule.AfterCheck(new ReplyCheck([], TimeSpan.FromMinutes(10), false), _now, store.ScheduleReports));

        // The window applies the worker's wait before every check, and the busy retry in Replan never comes sooner.
        var window = SourceFiles.Read(Path.Combine("src", "BannerlordCoopLauncher", "ReportReplyWindow.cs"));
        Assert.Contains("if (DateTime.UtcNow < _notBeforeUtc)", SourceFiles.Method(window, "private async Task TickAsync()"));
        Assert.Contains("ReplySchedule.ReplanFrom(DateTime.UtcNow, _nextCheckUtc, _notBeforeUtc, _lastStart, _store.ScheduleReports,", SourceFiles.Method(window, "private void Replan(bool resume)"));
    }

    [Fact]
    public void A_busy_keys_file_of_the_other_exe_keeps_a_retry_when_this_exe_has_only_solved_reports()
    {
        string[] folders = [_root, Path.Combine(_root, "Server"), Path.Combine(_root, "Client")];
        var client = new ReportReplyStore(Path.Combine(_root, "Client"), "coop-client", folders, () => _now);
        var server = new ReportReplyStore(Path.Combine(_root, "Server"), "coop-host", folders, () => _now);
        client.Remember("CR-ABCDE", ReportReplies.NewKey());
        client.MarkSolved("CR-ABCDE");
        server.Remember(Code, ReportReplies.NewKey());
        var answered = new ReplyCheck([], TimeSpan.FromMinutes(10), true, true, TimeSpan.FromSeconds(30));
        Assert.Equal(TimeSpan.FromSeconds(30), ReplySchedule.AfterCheck(answered, _now, client.ScheduleReports)!.Wait);
        using (new FileStream(server.KeysPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var busy = Assert.Throws<ReportKeysBusyException>(() => client.ScheduleReports());
            Assert.Equal(ReplyNeed.None, Assert.Single(busy.Reports).Need);
            Assert.Equal(ReplySchedule.BusyRetry, ReplySchedule.AfterCheck(answered, _now, client.ScheduleReports));
        }
    }

    [Fact]
    public async Task Restore_goes_by_comment_id_so_equal_times_or_a_later_edit_of_an_older_comment_never_win()
    {
        // Equal times: B (a higher comment id) is current; A served again without a full list stays set aside.
        var store = await WithAnswer();
        var sameTime = new { id = "456", at = "2026-11-01T10:00:00Z", text = "Update the tool.", buttons = Array.Empty<string>() };
        await ReportReplies.CheckAsync(new HttpClient(new Answering(Body(true, sameTime))), store);
        await ReportReplies.CheckAsync(new HttpClient(new Answering(Body(null, Rbm))), store);
        Assert.True(store.Answers().Single(a => a.Id == "123").Replaced);
        Assert.False(store.Answers().Single(a => a.Id == "456").Replaced);

        // An older comment edited later than the current answer: still older by id.
        var other = new ReportRepliesBusyFileTests();
        try
        {
            var second = await other.WithAnswer();
            var edited = new { id = "123", at = "2026-11-03T10:00:00Z", text = "Turn off RBM and Diplomacy.", buttons = Array.Empty<string>() };
            var newer = new { id = "456", at = "2026-11-02T10:00:00Z", text = "Update the tool.", buttons = Array.Empty<string>() };
            await ReportReplies.CheckAsync(new HttpClient(new Answering(Body(true, edited))), second);
            await ReportReplies.CheckAsync(new HttpClient(new Answering(Body(true, newer))), second);
            await ReportReplies.CheckAsync(new HttpClient(new Answering(Body(null, edited))), second);
            Assert.True(second.Answers().Single(a => a.Id == "123" && a.At == "2026-11-03T10:00:00Z").Replaced);
            Assert.False(second.Answers().Single(a => a.Id == "456").Replaced);
        }
        finally
        {
            other.Dispose();
        }

        Assert.True(ReportReplies.Newer(new ReportAnswer { Id = "456", At = "2026-11-01T10:00:00Z" }, new ReportAnswer { Id = "123", At = "2026-11-03T10:00:00Z" }));
        Assert.True(ReportReplies.Newer(new ReportAnswer { Id = "123", At = "2026-11-03T10:00:00Z" }, new ReportAnswer { Id = "123", At = "2026-11-01T10:00:00Z" }));
        Assert.True(ReportReplies.Newer(new ReportAnswer { Id = "known-rbm", At = "2026-11-03T10:00:00Z" }, new ReportAnswer { Id = "123", At = "2026-11-01T10:00:00Z" }));
    }

    private (ReportReplyStore Client, ReportReplyStore Server) TwoExes()
    {
        string[] folders = [_root, Path.Combine(_root, "Server"), Path.Combine(_root, "Client")];
        return (new ReportReplyStore(Path.Combine(_root, "Client"), "coop-client", folders, () => _now),
            new ReportReplyStore(Path.Combine(_root, "Server"), "coop-host", folders, () => _now));
    }

    [Fact]
    public void A_due_check_with_the_other_exe_s_keys_file_busy_goes_out_only_while_the_window_is_open()
    {
        var (client, server) = TwoExes();
        client.Remember("CR-ABCDE", ReportReplies.NewKey());
        client.MarkSolved("CR-ABCDE");
        server.Remember(Code, ReportReplies.NewKey());
        _now = _now.AddDays(2);
        // Readable: the server's open report needs the check, minimized nothing does.
        Assert.True(ReplySchedule.ShouldCheck(_now, client.ScheduleReports, minimized: false));
        Assert.False(ReplySchedule.ShouldCheck(_now, client.ScheduleReports, minimized: true));
        using (new FileStream(server.KeysPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            // Only a solved report readable, the open one in the busy file: open window goes on, minimized stops.
            Assert.True(ReplySchedule.ShouldCheck(_now, client.ScheduleReports, minimized: false));
            Assert.False(ReplySchedule.ShouldCheck(_now, client.ScheduleReports, minimized: true));
        }

        // Every file readable and only solved reports: no check.
        server.MarkSolved(Code);
        Assert.False(ReplySchedule.ShouldCheck(_now, client.ScheduleReports, minimized: false));
    }

    [Fact]
    public void A_send_while_the_other_exe_s_keys_file_is_busy_still_brings_a_check_within_seconds()
    {
        var (client, server) = TwoExes();
        server.Remember(Code, ReportReplies.NewKey());
        var lastStart = _now;
        _now = _now.AddDays(1);
        var planned = _now.AddHours(1);
        // A report sent from this exe while the server tool holds its keys file.
        client.Remember("CR-ABCDE", ReportReplies.NewKey());
        using (new FileStream(server.KeysPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var (next, newest) = ReplySchedule.ReplanFrom(_now, planned, DateTime.MinValue, lastStart, client.ScheduleReports, false, false, 0);
            Assert.Equal(_now.AddSeconds(5), next);
            Assert.Equal(_now, newest);

            // Nothing new and only solved reports readable: the planned check stays, or a retry when none is planned.
            client.MarkSolved("CR-ABCDE");
            Assert.Equal(planned, ReplySchedule.ReplanFrom(_now, planned, DateTime.MinValue, newest, client.ScheduleReports, false, false, 0).Next);
            Assert.Equal(_now.AddSeconds(30), ReplySchedule.ReplanFrom(_now, null, DateTime.MinValue, newest, client.ScheduleReports, false, false, 0).Next);
            Assert.Null(ReplySchedule.ReplanFrom(_now, null, DateTime.MinValue, newest, client.ScheduleReports, true, false, 0).Next);
        }

        // Nothing readable: the planned check stays, or a 30 to 60 s retry, never before the worker's wait.
        IReadOnlyList<ReplyScheduleReport> Busy() => throw new ReportFileBusyException("report-replies-coop.json");
        Assert.Equal(planned, ReplySchedule.ReplanFrom(_now, planned, DateTime.MinValue, null, Busy, false, true, 0).Next);
        Assert.Equal(_now.AddSeconds(45), ReplySchedule.ReplanFrom(_now, null, DateTime.MinValue, null, Busy, false, true, 0.5).Next);
        Assert.Equal(_now.AddMinutes(20), ReplySchedule.ReplanFrom(_now, null, _now.AddMinutes(20), null, Busy, false, true, 0).Next);
        Assert.Null(ReplySchedule.ReplanFrom(_now, null, DateTime.MinValue, null, Busy, true, true, 0).Next);
    }

    // The PC clock went back 12 hours: a planned check and the worker's wait at the old 18:00 would hold every
    // check until then. Anything more than LongestWait ahead no longer counts.
    [Fact]
    public void A_clock_that_went_back_does_not_hold_the_checks()
    {
        var now = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);
        var oldTime = now.AddHours(18);
        Assert.True(ReplySchedule.ClockWentBack(now, oldTime));
        Assert.False(ReplySchedule.ClockWentBack(now, now.AddHours(5)));
        Assert.False(ReplySchedule.ClockWentBack(now, null));

        IReadOnlyList<ReplyScheduleReport> Open() => [new ReplyScheduleReport(now.AddMinutes(-1), null, ReplyNeed.Open)];
        // A send after the rollback checks within seconds, not at the old 18:00.
        Assert.Equal(now.AddSeconds(5), ReplySchedule.Replan(now, oldTime, oldTime, Open(), false, true, 0));
        Assert.Equal(now.AddSeconds(5), ReplySchedule.ReplanFrom(now, oldTime, oldTime, null, Open, false, true, 0).Next);
        // A real worker wait under 6 hours still counts.
        Assert.Equal(now.AddHours(1), ReplySchedule.Replan(now, null, now.AddHours(1), Open(), false, true, 0));

        // The window resets both before its not due yet return.
        var tick = SourceFiles.Method(SourceFiles.Read(Path.Combine("src", "BannerlordCoopLauncher", "ReportReplyWindow.cs")), "private async Task TickAsync()");
        var reset = tick.IndexOf("ReplySchedule.ClockWentBack(DateTime.UtcNow, _nextCheckUtc)", StringComparison.Ordinal);
        Assert.True(reset > 0);
        Assert.True(reset < tick.IndexOf("DateTime.UtcNow < due", StringComparison.Ordinal));
    }
}
