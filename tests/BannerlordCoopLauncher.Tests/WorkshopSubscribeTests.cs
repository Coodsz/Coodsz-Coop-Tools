using BannerlordCoopLauncher.Core.Steam;

namespace BannerlordCoopLauncher.Tests;

public class WorkshopSubscribeTests
{
    private sealed class FakeSteam : IWorkshopApi
    {
        public readonly Dictionary<ulong, WorkshopItemState> States = new();
        public readonly List<ulong> Subscribed = new();
        public readonly List<ulong> Downloads = new();
        public int Pumps;
        public int FinishAfterPumps = 2;

        public readonly HashSet<ulong> Hidden = new();

        public bool Subscribe(ulong itemId)
        {
            Subscribed.Add(itemId);
            // A hidden item: Steam accepts the call but never subscribes.
            if (!Hidden.Contains(itemId))
                States[itemId] = WorkshopItemState.Subscribed | WorkshopItemState.DownloadPending;
            return true;
        }

        // Items Steam finds an update for once it is asked to download them.
        public readonly HashSet<ulong> UpdateOnDownload = new();

        public void Download(ulong itemId)
        {
            Downloads.Add(itemId);
            if (UpdateOnDownload.Contains(itemId))
                States[itemId] = WorkshopItemState.Subscribed | WorkshopItemState.Installed | WorkshopItemState.Downloading;
        }

        public WorkshopItemState State(ulong itemId) => States.TryGetValue(itemId, out var state) ? state : WorkshopItemState.None;

        public (ulong Downloaded, ulong Total) Progress(ulong itemId) => (50, 100);

        public void Pump()
        {
            if (++Pumps < FinishAfterPumps)
                return;
            foreach (var id in States.Keys.ToList())
                States[id] = WorkshopItemState.Subscribed | WorkshopItemState.Installed;
        }
    }

    [Fact]
    public void Missing_items_are_subscribed_and_the_call_waits_for_the_download()
    {
        var steam = new FakeSteam();
        steam.States[2] = WorkshopItemState.Subscribed | WorkshopItemState.Installed;
        steam.States[3] = WorkshopItemState.Subscribed | WorkshopItemState.Installed | WorkshopItemState.NeedsUpdate;
        var log = new List<string>();

        var result = WorkshopSubscribe.Run(steam, new ulong[] { 1, 2, 3 }, new Dictionary<ulong, string> { [1] = "Harmony" }, log.Add, TimeSpan.FromSeconds(5), CancellationToken.None, TimeSpan.FromMilliseconds(1), settle: TimeSpan.FromMilliseconds(20));

        Assert.Equal(new ulong[] { 1 }, steam.Subscribed);
        // Item 2 looked current, and Steam is still asked so it looks for an update.
        Assert.Equal(new ulong[] { 1, 2, 3 }, steam.Downloads);
        Assert.Equal(new ulong[] { 2 }, result.NothingDownloaded);
        Assert.Equal(new ulong[] { 1, 2, 3 }, result.Ready);
        Assert.Empty(result.NotReady);
        Assert.Contains("Asked Steam to subscribe to Harmony (1).", log);
        Assert.Contains(log, line => line.StartsWith("Downloading Harmony (1): 50%", StringComparison.Ordinal));
    }

    [Fact]
    public void Items_still_downloading_at_the_deadline_are_reported()
    {
        var steam = new FakeSteam { FinishAfterPumps = int.MaxValue };

        var result = WorkshopSubscribe.Run(steam, new ulong[] { 7 }, new Dictionary<ulong, string>(), _ => { }, TimeSpan.FromMilliseconds(30), CancellationToken.None, TimeSpan.FromMilliseconds(5));

        Assert.Empty(result.Ready);
        Assert.Equal(new ulong[] { 7 }, result.NotReady);
    }

    [Fact]
    public void An_item_steam_never_subscribes_is_reported_failed_quickly()
    {
        var steam = new FakeSteam();
        steam.Hidden.Add(5);
        var log = new List<string>();

        var started = DateTime.UtcNow;
        var result = WorkshopSubscribe.Run(steam, new ulong[] { 4, 5 }, new Dictionary<ulong, string> { [5] = "More Nations" }, log.Add,
            TimeSpan.FromSeconds(30), CancellationToken.None, TimeSpan.FromMilliseconds(5), TimeSpan.FromMilliseconds(50));

        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(10));
        Assert.Equal(new ulong[] { 4 }, result.Ready);
        Assert.Equal(new ulong[] { 5 }, result.Failed);
        Assert.Empty(result.StillDownloading);
        Assert.Contains(log, line => line.Contains("did not subscribe to More Nations", StringComparison.Ordinal));
    }

    [Fact]
    public void An_item_steam_calls_current_is_asked_to_update_and_its_update_is_waited_for()
    {
        var steam = new FakeSteam { FinishAfterPumps = 3 };
        steam.States[8] = WorkshopItemState.Subscribed | WorkshopItemState.Installed;
        steam.UpdateOnDownload.Add(8);

        var result = WorkshopSubscribe.Run(steam, new ulong[] { 8 }, new Dictionary<ulong, string>(), _ => { }, TimeSpan.FromSeconds(5), CancellationToken.None,
            TimeSpan.FromMilliseconds(1), settle: TimeSpan.FromSeconds(5));

        Assert.Equal(new ulong[] { 8 }, steam.Downloads);
        Assert.Equal(new ulong[] { 8 }, result.Ready);
        Assert.Empty(result.NothingDownloaded);
    }

    [Fact]
    public void An_item_steam_keeps_calling_current_is_reported_after_a_short_wait()
    {
        // Seen with Mod Configuration Menu v5 on 2026-10-17: the Workshop page had a
        // newer update, Steam still called the old copy current and downloaded nothing.
        var steam = new FakeSteam { FinishAfterPumps = int.MaxValue };
        steam.States[9] = WorkshopItemState.Subscribed | WorkshopItemState.Installed;

        var started = DateTime.UtcNow;
        var result = WorkshopSubscribe.Run(steam, new ulong[] { 9 }, new Dictionary<ulong, string>(), _ => { }, TimeSpan.FromSeconds(30), CancellationToken.None,
            TimeSpan.FromMilliseconds(5), settle: TimeSpan.FromMilliseconds(100));

        Assert.True(DateTime.UtcNow - started >= TimeSpan.FromMilliseconds(100));
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(10));
        Assert.Equal(new ulong[] { 9 }, steam.Downloads);
        Assert.Equal(new ulong[] { 9 }, result.Ready);
        Assert.Equal(new ulong[] { 9 }, result.NothingDownloaded);
    }

    [Fact]
    public void Ready_means_subscribed_installed_and_not_downloading()
    {
        Assert.True(WorkshopSubscribe.IsReady(WorkshopItemState.Subscribed | WorkshopItemState.Installed));
        Assert.False(WorkshopSubscribe.IsReady(WorkshopItemState.Installed));
        Assert.False(WorkshopSubscribe.IsReady(WorkshopItemState.Subscribed | WorkshopItemState.Installed | WorkshopItemState.Downloading));
        Assert.False(WorkshopSubscribe.IsReady(WorkshopItemState.Subscribed | WorkshopItemState.Installed | WorkshopItemState.NeedsUpdate));
    }

    [Fact]
    public void Steam_subscribing_reports_why_it_cannot_start()
    {
        var root = Path.Combine(Path.GetTempPath(), "no-steam-" + Guid.NewGuid().ToString("N"));
        Assert.Null(SteamApiWorkshop.TryOpen(root, out var reason));
        Assert.False(string.IsNullOrWhiteSpace(reason));
    }
}
