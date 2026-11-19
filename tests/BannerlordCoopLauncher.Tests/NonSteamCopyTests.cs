using System.IO.Compression;
using BannerlordCoopLauncher.Core.Launch;
using BannerlordCoopLauncher.Core.Server;
using BannerlordCoopLauncher.Core.Steam;

namespace BannerlordCoopLauncher.Tests;

public class NonSteamCopyTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "coodsz-nonsteam-" + Guid.NewGuid().ToString("N"));
    private string Bin => Path.Combine(_root, "bin", "Win64_Shipping_Client");

    public NonSteamCopyTests() => Directory.CreateDirectory(Bin);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    // A tiny PE32+ file whose security directory is empty, or points at a signature.
    private static byte[] Pe(bool signed)
    {
        var bytes = new byte[0x200];
        bytes[0] = (byte)'M';
        bytes[1] = (byte)'Z';
        BitConverter.GetBytes(0x80).CopyTo(bytes, 0x3C);
        BitConverter.GetBytes(0x00004550u).CopyTo(bytes, 0x80);
        var optional = 0x80 + 24;
        BitConverter.GetBytes((ushort)0x20B).CopyTo(bytes, optional);
        if (signed)
        {
            var security = optional + 112 + 4 * 8;
            BitConverter.GetBytes(0x1000u).CopyTo(bytes, security);
            BitConverter.GetBytes(0x2000u).CopyTo(bytes, security + 4);
        }
        return bytes;
    }

    [Fact]
    public void A_normal_Steam_copy_has_no_signs()
    {
        File.WriteAllText(Path.Combine(Bin, "steam_appid.txt"), "261550");
        File.WriteAllBytes(Path.Combine(Bin, "steam_api64.dll"), Pe(signed: true));
        Assert.Empty(NonSteamCopy.Find(_root));
        Assert.Null(NonSteamCopy.Note(_root));
    }

    [Fact]
    public void Emulator_files_and_an_unsigned_steam_api_are_found()
    {
        Directory.CreateDirectory(Path.Combine(Bin, "steam_settings"));
        File.WriteAllText(Path.Combine(Bin, "steam_emu.ini"), "");
        File.WriteAllText(Path.Combine(_root, "CODEX.ini"), "");
        File.WriteAllBytes(Path.Combine(Bin, "steam_api64.dll"), Pe(signed: false));

        var signs = NonSteamCopy.Find(_root);

        Assert.Equal(new[] { "steam_settings", "steam_emu.ini", "CODEX.ini", NonSteamCopy.UnsignedSteamApi }, signs);
        Assert.Equal("This game folder has files from a non Steam copy (steam_settings, steam_emu.ini, CODEX.ini, unsigned steam_api64.dll). Coop needs each player to own the game on Steam.",
            NonSteamCopy.Note(signs));
    }

    [Fact]
    public void A_file_that_is_not_a_PE_file_is_not_called_unsigned()
    {
        File.WriteAllText(Path.Combine(Bin, "steam_api64.dll"), "not a dll");
        Assert.Empty(NonSteamCopy.Find(_root));
        Assert.Empty(NonSteamCopy.Find(null));
        Assert.Empty(NonSteamCopy.Find(Path.Combine(_root, "missing")));
    }

    [Fact]
    public void The_help_zip_notes_say_so()
    {
        Directory.CreateDirectory(Path.Combine(Bin, "steam_settings"));
        var zip = Path.Combine(_root, "logs.zip");
        ClientLogZip.Create(zip, _root, Path.Combine(_root, "Documents"), Path.Combine(_root, "ProgramData"));
        using var archive = ZipFile.OpenRead(zip);
        using var reader = new StreamReader(archive.GetEntry("NOTES.txt")!.Open());
        Assert.Contains("This game folder has files from a non Steam copy (steam_settings).", reader.ReadToEnd());
    }
}

public class SharedSteamIdWatchTests
{
    private static ConnectedPlayer Player(int id, string name, string state = "on map") => new(id, name, state, "127.0.0.1:1");

    private static readonly IReadOnlyList<LiveRosterEntry> OneId = SharedSteamIdWatch.ReadRoster("""
        { "UpdatedUtc": "2026-10-19T15:06:21Z", "Players": [
          { "ControllerId": "76561198000000006", "HeroId": "Hero_Player", "HeroName": "Bob", "ClanName": "Bob's clan" },
          { "ControllerId": "76561198000000009", "HeroId": "Hero_Player2", "HeroName": "Ann", "ClanName": "Ann's clan" } ] }
        """);

    [Fact]
    public void The_roster_file_is_read()
    {
        Assert.Equal(2, OneId.Count);
        Assert.Equal(new LiveRosterEntry("76561198000000006", "Bob"), OneId[0]);
        Assert.Empty(SharedSteamIdWatch.ReadRoster("{ broken"));
        Assert.Empty(SharedSteamIdWatch.ReadRoster(null));
        Assert.Empty(SharedSteamIdWatch.ReadRoster("""{ "Players": [] }"""));
    }

    [Fact]
    public void Two_connections_with_one_Steam_ID_get_one_line_once()
    {
        var watch = new SharedSteamIdWatch();
        Assert.True(DsEventParser.TryParse("""21:24:25.353  @DS@{"ev":"players","list":[{"id":0,"name":"Bob","state":"on map","addr":"1.2.3.4:5"},{"id":1,"name":"Bob","state":"on map","addr":"1.2.3.5:5"},{"id":2,"name":"Ann","state":"on map","addr":"1.2.3.6:5"}]}""", out var evt));
        var players = Assert.IsType<DsEvent.Players>(evt).List;

        var lines = watch.Check(players, OneId);

        Assert.Equal(new[] { "Two players joined with the same Steam ID, both as Bob. This happens with non Steam copies of the game, and they will share one character." }, lines);
        Assert.Empty(watch.Check(players, OneId));
        watch.Reset();
        Assert.Single(watch.Check(players, OneId));
    }

    [Fact]
    public void A_new_pair_is_noted_again()
    {
        var watch = new SharedSteamIdWatch();
        Assert.Single(watch.Check(new[] { Player(0, "Bob"), Player(1, "Bob") }, OneId));
        Assert.Equal(new[] { SharedSteamIdWatch.Line("Bob", 3) }, watch.Check(new[] { Player(0, "Bob"), Player(1, "Bob"), Player(4, "bob ") }, OneId));
        Assert.StartsWith("3 players joined with the same Steam ID, all as Bob.", SharedSteamIdWatch.Line("Bob", 3));
    }

    [Fact]
    public void Two_players_who_named_their_heroes_the_same_get_no_line()
    {
        var roster = SharedSteamIdWatch.ReadRoster("""
            { "Players": [ { "ControllerId": "76561198000000006", "HeroName": "Bob" }, { "ControllerId": "76561198000000009", "HeroName": "Bob" } ] }
            """);
        Assert.Empty(new SharedSteamIdWatch().Check(new[] { Player(0, "Bob"), Player(1, "Bob") }, roster));
    }

    [Fact]
    public void No_line_without_proof()
    {
        var watch = new SharedSteamIdWatch();
        // No live-players.json, so two heroes with one name can not be told apart.
        Assert.Empty(watch.Check(new[] { Player(0, "Bob"), Player(1, "Bob") }, null));
        Assert.Empty(watch.Check(new[] { Player(0, "Bob"), Player(1, "Bob") }, Array.Empty<LiveRosterEntry>()));
        // One of them is still loading: a player who crashed and came back can be listed twice for a while.
        Assert.Empty(watch.Check(new[] { Player(0, "Bob"), Player(1, "Bob", "loading") }, OneId));
        Assert.Empty(watch.Check(new[] { Player(0, "(joining)"), Player(1, "(joining)") }, OneId));
        // The same connection twice is not two players.
        Assert.Empty(watch.Check(new[] { Player(0, "Bob"), Player(0, "Bob") }, OneId));
        Assert.Empty(watch.Check(new[] { Player(0, "Carl"), Player(1, "Carl") }, OneId));
        Assert.False(SharedSteamIdWatch.HasSharedHero(new[] { Player(0, "Bob"), Player(1, "Ann") }));
        Assert.True(SharedSteamIdWatch.HasSharedHero(new[] { Player(0, "Bob"), Player(1, "Bob") }));
    }

    [Fact]
    public void An_old_roster_file_is_not_read()
    {
        var root = Path.Combine(Path.GetTempPath(), "coodsz-roster-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var file = Path.Combine(root, "live-players.json");
            File.WriteAllText(file, """{ "Players": [ { "ControllerId": "76561198000000006", "HeroName": "Bob" } ] }""");
            File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddHours(-1));
            Assert.Empty(SharedSteamIdWatch.ReadRosterFile(new[] { root }, DateTime.UtcNow));
            Assert.Single(SharedSteamIdWatch.ReadRosterFile(new[] { root }, DateTime.UtcNow.AddHours(-2)));
            Assert.Empty(SharedSteamIdWatch.ReadRosterFile(new[] { Path.Combine(root, "missing") }));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
