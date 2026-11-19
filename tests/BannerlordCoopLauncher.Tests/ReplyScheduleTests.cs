using System.Net;
using System.Text;
using System.Text.Json;
using BannerlordCoopLauncher.Core.Support;

namespace BannerlordCoopLauncher.Tests;

/// <summary>
/// The reply check's schedule (v1.3.5, reply push on the worker's free plan): report ages and needs to the next wait,
/// no checks while minimized or with nothing open, the worker's own wait, a restore or a send bringing a check soon,
/// and what the store hands the schedule.
/// </summary>
public sealed class ReplyScheduleTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "coodsz-reply-schedule-" + Guid.NewGuid().ToString("N"));
    private DateTime _now = new(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);
    private const string Code = "CR-7KQ2M";

    public ReplyScheduleTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, true); }
        catch (IOException) { }
    }

    private ReplyScheduleReport Sent(TimeSpan ago, ReplyNeed need = ReplyNeed.Open, TimeSpan? followUpAgo = null) =>
        new(_now - ago, followUpAgo is { } f ? _now - f : null, need);

    [Theory]
    [InlineData(0, 30, 5)]
    [InlineData(60, 30, 5)]
    [InlineData(599, 30, 5)]
    [InlineData(600, 120, 60)]
    [InlineData(3600, 120, 60)]
    [InlineData(29 * 24 * 3600, 120, 60)]
    public void An_open_report_s_age_gives_the_wait(int secondsAgo, int waitSeconds, int jitterSeconds)
    {
        var wait = ReplySchedule.Next(_now, [Sent(TimeSpan.FromSeconds(secondsAgo))]);
        Assert.NotNull(wait);
        Assert.Equal(TimeSpan.FromSeconds(waitSeconds), wait.Wait);
        Assert.Equal(TimeSpan.FromSeconds(jitterSeconds), wait.Jitter);
    }

    [Fact]
    public void No_check_with_no_reports_closed_reports_or_reports_past_30_days()
    {
        Assert.Null(ReplySchedule.Next(_now, []));
        Assert.Null(ReplySchedule.Next(_now, [Sent(TimeSpan.FromDays(2), ReplyNeed.None)]));
        // Solved within its first 10 minutes: no fast checks either.
        Assert.Null(ReplySchedule.Next(_now, [Sent(TimeSpan.FromMinutes(2), ReplyNeed.None)]));
        Assert.Null(ReplySchedule.Next(_now, [Sent(TimeSpan.FromDays(30))]));
        Assert.Null(ReplySchedule.Next(_now, [Sent(TimeSpan.FromDays(31), followUpAgo: TimeSpan.FromMinutes(1))]));
    }

    [Fact]
    public void Answered_and_read_reports_get_the_slow_check_and_one_open_report_wins()
    {
        Assert.Equal(new ReplyWait(TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(15)), ReplySchedule.Next(_now, [Sent(TimeSpan.FromDays(3), ReplyNeed.Read)]));
        Assert.Equal(TimeSpan.FromMinutes(2), ReplySchedule.Next(_now, [Sent(TimeSpan.FromDays(3), ReplyNeed.Read), Sent(TimeSpan.FromDays(5))])!.Wait);
        Assert.Equal(TimeSpan.FromMinutes(2), ReplySchedule.Next(_now, [Sent(TimeSpan.FromDays(5)), Sent(TimeSpan.FromDays(3), ReplyNeed.Read)])!.Wait);
        Assert.Equal(TimeSpan.FromMinutes(2), ReplySchedule.Next(_now, [Sent(TimeSpan.FromDays(2), ReplyNeed.None), Sent(TimeSpan.FromDays(5))])!.Wait);
    }

    [Theory]
    [InlineData(ReportStatusKind.Looking, false, ReplyNeed.Looking)]
    [InlineData(ReportStatusKind.Looking, true, ReplyNeed.Open)]
    [InlineData(ReportStatusKind.Answered, true, ReplyNeed.Open)]
    [InlineData(ReportStatusKind.Answered, false, ReplyNeed.Read)]
    [InlineData(ReportStatusKind.Solved, true, ReplyNeed.None)]
    [InlineData(ReportStatusKind.Fixed, false, ReplyNeed.None)]
    [InlineData(null, false, ReplyNeed.Looking)]
    public void A_badge_and_unread_answers_give_the_need(ReportStatusKind? status, bool unread, ReplyNeed need) =>
        Assert.Equal(need, ReplySchedule.NeedOf(status, unread));

    [Fact]
    public void Looking_into_it_with_no_send_or_no_for_a_day_gets_the_slow_check()
    {
        Assert.Equal(TimeSpan.FromMinutes(2), ReplySchedule.Next(_now, [Sent(TimeSpan.FromHours(23), ReplyNeed.Looking)])!.Wait);
        Assert.Equal(TimeSpan.FromMinutes(15), ReplySchedule.Next(_now, [Sent(TimeSpan.FromHours(24), ReplyNeed.Looking)])!.Wait);
        // A No, still broken an hour ago makes it busy again.
        Assert.Equal(TimeSpan.FromMinutes(2), ReplySchedule.Next(_now, [Sent(TimeSpan.FromDays(3), ReplyNeed.Looking, TimeSpan.FromHours(1))])!.Wait);
        // An unread answer keeps the 2 to 3 minutes whatever its age.
        Assert.Equal(TimeSpan.FromMinutes(2), ReplySchedule.Next(_now, [Sent(TimeSpan.FromDays(3), ReplyNeed.Looking), Sent(TimeSpan.FromDays(4))])!.Wait);
    }

    [Fact]
    public void Minimized_means_no_check_at_all_even_right_after_a_send()
    {
        Assert.Null(ReplySchedule.Next(_now, [Sent(TimeSpan.FromMinutes(1))], minimized: true));
        Assert.Null(ReplySchedule.After(new ReplyCheck([], TimeSpan.FromMinutes(10), true, true, TimeSpan.FromSeconds(30)), _now, [Sent(TimeSpan.FromMinutes(1))], minimized: true));
    }

    [Fact]
    public void A_no_still_broken_starts_the_30_second_phase_again()
    {
        Assert.Equal(TimeSpan.FromSeconds(30), ReplySchedule.Next(_now, [Sent(TimeSpan.FromDays(5), followUpAgo: TimeSpan.FromMinutes(3))])!.Wait);
        Assert.Equal(TimeSpan.FromMinutes(2), ReplySchedule.Next(_now, [Sent(TimeSpan.FromDays(5), followUpAgo: TimeSpan.FromMinutes(11))])!.Wait);
    }

    [Fact]
    public void A_report_or_send_dated_in_the_future_by_a_clock_that_went_back_keeps_the_checks_going()
    {
        // Sent 20 hours "ahead" after the PC clock went back: it counts as sent 10 minutes ago, so the checks go on,
        // at the open report's 2 minutes, not 30 s until the clock catches up.
        Assert.Equal(TimeSpan.FromMinutes(2), ReplySchedule.Next(_now, [Sent(-TimeSpan.FromHours(20))])!.Wait);
        // Also once the time ahead has shrunk under 6 hours as the clock ran.
        Assert.Equal(TimeSpan.FromMinutes(2), ReplySchedule.Next(_now, [Sent(-TimeSpan.FromHours(3))])!.Wait);
        Assert.Equal(TimeSpan.FromMinutes(15), ReplySchedule.Next(_now, [Sent(-TimeSpan.FromHours(20), ReplyNeed.Read)])!.Wait);
        // A No, still broken dated ahead on an old report: the same.
        Assert.Equal(TimeSpan.FromMinutes(2), ReplySchedule.Next(_now, [Sent(TimeSpan.FromDays(5), ReplyNeed.Looking, followUpAgo: -TimeSpan.FromHours(8))])!.Wait);
        // A minute ahead is just clock drift: the 30 s checks of a new send.
        Assert.Equal(TimeSpan.FromSeconds(30), ReplySchedule.Next(_now, [Sent(-TimeSpan.FromMinutes(1))])!.Wait);
        Assert.True(ReplySchedule.ShouldCheck(_now, () => [Sent(-TimeSpan.FromHours(20))], minimized: false));
    }

    [Fact]
    public void The_worker_s_longer_wait_always_wins_in_every_phase_and_is_capped_at_6_hours()
    {
        var fresh = new[] { Sent(TimeSpan.FromMinutes(1)) };
        // An older worker (or one without push) says 30 minutes: the tool waits that long, as v1.3.4 did.
        Assert.Equal(new ReplyWait(TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(1)), ReplySchedule.Next(_now, fresh, TimeSpan.FromMinutes(30)));
        // The worker's brake (5 minutes) slows the fast phase too.
        Assert.Equal(TimeSpan.FromMinutes(5), ReplySchedule.Next(_now, fresh, TimeSpan.FromMinutes(5))!.Wait);
        Assert.Equal(TimeSpan.FromMinutes(5), ReplySchedule.Next(_now, [Sent(TimeSpan.FromDays(1))], TimeSpan.FromMinutes(5))!.Wait);
        Assert.Equal(TimeSpan.FromMinutes(30), ReplySchedule.Next(_now, [Sent(TimeSpan.FromDays(1), ReplyNeed.Read)], TimeSpan.FromMinutes(30))!.Wait);
        // The push worker's 30 s lets the schedule run.
        Assert.Equal(new ReplyWait(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(5)), ReplySchedule.Next(_now, fresh, TimeSpan.FromSeconds(30)));
        Assert.Equal(TimeSpan.FromHours(6), ReplySchedule.Next(_now, fresh, TimeSpan.FromHours(9))!.Wait);
    }

    [Fact]
    public void After_a_check_failures_keep_their_own_wait_and_nothing_to_ask_stops()
    {
        var fresh = new[] { Sent(TimeSpan.FromMinutes(1)) };
        Assert.Null(ReplySchedule.After(new ReplyCheck([], TimeSpan.FromMinutes(10), false), _now, []));
        // A 500, a timeout or offline: the 10 minutes CheckAsync says. A 429: the worker's wait.
        Assert.Equal(TimeSpan.FromMinutes(10), ReplySchedule.After(new ReplyCheck([], TimeSpan.FromMinutes(10), true), _now, fresh)!.Wait);
        Assert.Equal(TimeSpan.FromMinutes(15), ReplySchedule.After(new ReplyCheck([], TimeSpan.FromMinutes(15), true), _now, fresh)!.Wait);
        // Answered: the schedule, with the worker's wait.
        Assert.Equal(TimeSpan.FromSeconds(30), ReplySchedule.After(new ReplyCheck([], TimeSpan.FromMinutes(10), true, true, TimeSpan.FromSeconds(30)), _now, fresh)!.Wait);
        Assert.Equal(TimeSpan.FromSeconds(30), ReplySchedule.After(new ReplyCheck([], TimeSpan.FromMinutes(10), true, true, null), _now, fresh)!.Wait);
        Assert.Equal(TimeSpan.FromMinutes(30), ReplySchedule.After(new ReplyCheck([], TimeSpan.FromMinutes(30), true, true, TimeSpan.FromMinutes(30)), _now, fresh)!.Wait);
        // The answer made the report read: the slow check.
        Assert.Equal(TimeSpan.FromMinutes(15), ReplySchedule.After(new ReplyCheck([], TimeSpan.FromMinutes(10), true, true, TimeSpan.FromSeconds(30)), _now, [Sent(TimeSpan.FromDays(1), ReplyNeed.Read)])!.Wait);
    }

    [Fact]
    public void A_restore_or_a_send_brings_a_check_within_seconds_but_never_before_the_worker_s_wait()
    {
        var open = new[] { Sent(TimeSpan.FromDays(1)) };
        var planned = _now.AddMinutes(2);
        // Restored after being minimized (nothing planned): 5 s plus up to 10 s.
        Assert.Equal(_now.AddSeconds(5), ReplySchedule.Replan(_now, null, DateTime.MinValue, open, false, true, 0));
        Assert.Equal(_now.AddSeconds(15), ReplySchedule.Replan(_now, null, DateTime.MinValue, open, false, true, 1));
        Assert.Equal(_now.AddSeconds(10), ReplySchedule.Replan(_now, planned, DateTime.MinValue, open, false, true, 0.5));
        // A planned check that is sooner stays.
        Assert.Equal(_now.AddSeconds(3), ReplySchedule.Replan(_now, _now.AddSeconds(3), DateTime.MinValue, open, false, true, 1));
        // Not a restore or a send (an answer read): the planned check stays, its jitter too.
        Assert.Equal(planned, ReplySchedule.Replan(_now, planned, DateTime.MinValue, open, false, false, 0));
        // The worker asked for 3 more minutes, or for 30 (an older worker): not before then.
        Assert.Equal(_now.AddMinutes(3), ReplySchedule.Replan(_now, planned.AddMinutes(10), _now.AddMinutes(3), open, false, true, 0));
        Assert.Equal(planned, ReplySchedule.Replan(_now, planned, _now.AddMinutes(30), open, false, true, 0));
        // Minimized, or nothing open: stop.
        Assert.Null(ReplySchedule.Replan(_now, planned, DateTime.MinValue, open, true, false, 0));
        Assert.Null(ReplySchedule.Replan(_now, planned, DateTime.MinValue, [Sent(TimeSpan.FromDays(1), ReplyNeed.None)], false, true, 0));
        // A send while nothing was planned starts again.
        Assert.Equal(_now.AddSeconds(5), ReplySchedule.Replan(_now, null, DateTime.MinValue, [Sent(TimeSpan.Zero)], false, false, 0));
    }

    [Fact]
    public void The_newest_start_is_the_newest_send_or_no_still_broken()
    {
        Assert.Null(ReplySchedule.NewestStart([]));
        Assert.Equal(_now.AddMinutes(-1), ReplySchedule.NewestStart([Sent(TimeSpan.FromDays(3), followUpAgo: TimeSpan.FromMinutes(1)), Sent(TimeSpan.FromHours(1))]));
    }

    // ----- The store and the check -----

    private ReportReplyStore Store() => new(Path.Combine(_root, "Client"), "coop", [_root], () => _now);

    [Fact]
    public void The_store_gives_each_key_its_sent_time_newest_follow_up_and_need()
    {
        var store = Store();
        Assert.Empty(store.ScheduleReports());
        store.Remember(Code, ReportReplies.NewKey());
        var report = Assert.Single(store.ScheduleReports());
        Assert.Equal(_now, report.SentAt);
        Assert.Null(report.FollowUpAt);
        Assert.Equal(ReplyNeed.Looking, report.Need);
        Assert.Equal(TimeSpan.FromSeconds(30), ReplySchedule.Next(_now, store.ScheduleReports())!.Wait);

        // Answered: open while the answer is unread, the slow check once it is read.
        _now = _now.AddDays(2);
        store.TakeNew([new ReplyItem(Code, "123", "2026-11-03T10:00:00Z", "Turn off RBM.", false, false)]);
        Assert.Equal(ReplyNeed.Open, Assert.Single(store.ScheduleReports()).Need);
        store.MarkAllRead();
        Assert.Equal(ReplyNeed.Read, Assert.Single(store.ScheduleReports()).Need);
        Assert.Equal(TimeSpan.FromMinutes(15), ReplySchedule.Next(_now, store.ScheduleReports())!.Wait);

        store.MarkSolved(Code);
        Assert.Equal(ReplyNeed.None, Assert.Single(store.ScheduleReports()).Need);
        Assert.Null(ReplySchedule.Next(_now, store.ScheduleReports()));

        // It broke again: No, still broken opens it and starts the 30 second phase.
        _now = _now.AddMinutes(1);
        store.AddFollowUp(new ReportFollowUp { Code = Code, At = _now, Steps = FollowUpSteps.All, Text = "Still crashes." });
        report = Assert.Single(store.ScheduleReports());
        Assert.Equal(_now, report.FollowUpAt);
        Assert.Equal(ReplyNeed.Looking, report.Need);
        Assert.Equal(TimeSpan.FromSeconds(30), ReplySchedule.Next(_now, store.ScheduleReports())!.Wait);
    }

    [Fact]
    public async Task The_check_says_fast_and_keeps_the_worker_s_wait_only_when_it_answered()
    {
        var store = Store();
        store.Remember(Code, ReportReplies.NewKey());
        var handler = new Answering("{\"ok\":true,\"next\":30,\"replies\":[]}");
        var check = await ReportReplies.CheckAsync(new HttpClient(handler), store);
        Assert.True(check.Answered);
        Assert.Equal(TimeSpan.FromSeconds(30), check.Asked);
        // v1.3.4 tools read Next, still at least 10 minutes.
        Assert.Equal(TimeSpan.FromMinutes(10), check.Next);
        using (var doc = JsonDocument.Parse(handler.Body!))
            Assert.True(doc.RootElement.GetProperty("fast").GetBoolean());

        check = await ReportReplies.CheckAsync(new HttpClient(new Answering("{\"ok\":true,\"next\":99999,\"replies\":[]}")), store);
        Assert.Equal(ReportReplies.LongestWait, check.Asked);
        check = await ReportReplies.CheckAsync(new HttpClient(new Answering("{\"ok\":true,\"replies\":[]}")), store);
        Assert.True(check.Answered);
        Assert.Null(check.Asked);
        check = await ReportReplies.CheckAsync(new HttpClient(new Answering("{\"ok\":false,\"next\":900}", HttpStatusCode.TooManyRequests)), store);
        Assert.False(check.Answered);
        Assert.Equal(TimeSpan.FromMinutes(15), check.Next);
        Assert.Equal(TimeSpan.FromMinutes(15), ReplySchedule.After(check, _now, store.ScheduleReports())!.Wait);
    }

    private sealed class Answering(string body, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Body = request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }
}
