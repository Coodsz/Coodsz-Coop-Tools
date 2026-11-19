using BannerlordCoopLauncher.Core.Support;
using BannerlordCoopLauncher.Core.Steam;
using Xunit;

namespace BannerlordCoopLauncher.Tests;

public class SteamLobbyRetryTests
{
    private static readonly TimeSpan Every = TimeSpan.FromMilliseconds(10);
    // A starved runner must not turn a success into a give up.
    private static readonly TimeSpan Long = TimeSpan.FromMinutes(5);

    [Fact]
    public async Task Retry_FindsTheLobbyOnTheThirdTry()
    {
        var tries = 0;
        var lines = new List<string>();
        var id = await SteamLobbyFinder.RetryAsync(() =>
        {
            tries++;
            return tries < 3 ? Task.FromException<ulong>(new InvalidOperationException(SteamLobbyFinder.NoneFound)) : Task.FromResult(77UL);
        }, "Kodiak", lines.Add, Long, Every, CancellationToken.None);

        Assert.Equal(77UL, id);
        Assert.Equal(3, tries);
        Assert.Equal("Looking for the Steam lobby for Kodiak.", lines[0]);
        Assert.StartsWith("No Steam lobby for Kodiak yet.", lines[1]);
        Assert.Equal("Still looking for the Steam lobby for Kodiak.", lines[2]);
    }

    [Fact]
    public async Task Retry_GivesUpAfterTheTimeWithTheServingHint()
    {
        var tries = 0;
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => SteamLobbyFinder.RetryAsync(() =>
        {
            tries++;
            return Task.FromException<ulong>(new InvalidOperationException(SteamLobbyFinder.NoneFound));
        }, "Kodiak", _ => { }, TimeSpan.FromMilliseconds(80), Every, CancellationToken.None));

        Assert.Equal(SteamLobbyFinder.GaveUp, error.Message);
        Assert.StartsWith(SteamLobbyFinder.NoneFound, SteamLobbyFinder.GaveUp);
        Assert.Contains("Server online", error.Message);
        // A starved runner can spend the whole window on the first try, so one try is enough to give up.
        Assert.InRange(tries, 1, 9);
    }

    [Fact]
    public async Task Retry_StopsAtOnceOnOtherErrors()
    {
        var tries = 0;
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => SteamLobbyFinder.RetryAsync(() =>
        {
            tries++;
            return Task.FromException<ulong>(new InvalidOperationException(SteamLobbyFinder.MoreThanOne));
        }, "Kodiak", _ => { }, TimeSpan.FromSeconds(30), Every, CancellationToken.None));

        Assert.Equal(SteamLobbyFinder.MoreThanOne, error.Message);
        Assert.Equal(1, tries);
    }

    [Fact]
    public async Task Retry_StopsWhenCancelled()
    {
        using var cancel = new CancellationTokenSource();
        var tries = 0;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => SteamLobbyFinder.RetryAsync(() =>
        {
            if (++tries == 2)
                cancel.Cancel();
            return Task.FromException<ulong>(new InvalidOperationException(SteamLobbyFinder.NoneFound));
        }, "Kodiak", _ => { }, TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(5), cancel.Token));

        Assert.Equal(2, tries);
    }

    [Theory]
    [InlineData("17:30:38.382  [Coop] Steam lobby \"109775245183549596\" created", SteamLobbyState.Up)]
    [InlineData("[stdout] [Coop] Steam lobby \"109775243936300118\" created", SteamLobbyState.Up)]
    [InlineData("[Coop] Steam lobby lease renewal failed repeatedly; recreating the advertisement", SteamLobbyState.Renewing)]
    [InlineData("[DedicatedServer] Steam lobby advertisement withdrawn", SteamLobbyState.Closed)]
    [InlineData("[DedicatedServer] Steam discoverability disabled (steam=false in server-config.json) — direct connect only", SteamLobbyState.Off)]
    [InlineData("[Coop] Server Steam integration inactive; initialization did not reach anonymous logon. See SteamGameServerBoot diagnostics above", SteamLobbyState.Failed)]
    [InlineData("[DedicatedServer] coop Steam services did not start — direct connect only (see coop log above)", SteamLobbyState.Failed)]
    // Steam not open or not logged in yet when the server started (RedBoye, 2026-10-20).
    [InlineData("14:20:20.433  [DedicatedServer] Steam unavailable (client not running or SteamAPI.Init failed) — direct connect only", SteamLobbyState.Failed)]
    public void LobbyLine_ReadsTheServersOwnLines(string line, SteamLobbyState expected)
    {
        Assert.Equal(expected, SteamLobbyLine.Read(line));
    }

    [Theory]
    [InlineData("[DedicatedServer] Steam discoverability armed: user session up, game-server logon pending; the coop stack advertises the lobby once the server is listening")]
    [InlineData("[Coop] Steam lobby lease renewal failed (1/3); keeping the current advertisement")]
    [InlineData("[DedicatedServer] SERVING — coop server up, waiting for clients")]
    [InlineData("[DedicatedServer] Steam game-server logon observed by the coop layer after 0 pumps")]
    [InlineData("")]
    [InlineData(null)]
    public void LobbyLine_IgnoresLinesThatAreNoProof(string? line)
    {
        Assert.Null(SteamLobbyLine.Read(line));
    }

    [Fact]
    public void LobbyLine_HidesTheSteamNameInStreamerMode()
    {
        Assert.Equal("Steam lobby is up as Kodiak.", SteamLobbyLine.Describe(SteamLobbyState.Up, "Kodiak"));
        Assert.DoesNotContain("Kodiak", SteamLobbyLine.Describe(SteamLobbyState.Up, ""));
        Assert.Contains("not up yet", SteamLobbyLine.Describe(SteamLobbyState.Unknown, "Kodiak"));
        Assert.Contains("press Restart", SteamLobbyLine.Describe(SteamLobbyState.Failed, ""));
    }
}
