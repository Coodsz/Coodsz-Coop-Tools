// Tests for AdminCommand and ServerResourceMonitor, which port code from CoopKeep
// (https://github.com/Aderimo/bannerlord-coopkeep), Server/AdminCommand.cs and
// Server/ServerResourceMonitor.cs; some cases follow its AdminCommandTests.cs.
//
// Copyright (c) 2026 aderimo
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

using System.Diagnostics;
using System.Globalization;
using BannerlordCoopLauncher.Core.Server;
using BannerlordCoopLauncher.Core.Support;
using TaomCoop.Core;

namespace BannerlordCoopLauncher.Tests;

public sealed class CoopKeepAdminTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "coodsz-admin-" + Guid.NewGuid().ToString("N"));

    public CoopKeepAdminTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); }
        catch (IOException) { }
    }

    [Fact]
    public void Gold_by_id_uses_set_gold_state_and_never_the_removed_SetGold()
    {
        Assert.Equal("coop.debug.hero.set_gold_state main_hero 5000", AdminCommand.SetGold("main_hero", 5000));
        Assert.DoesNotContain("SetGold", AdminCommand.SetGold("main_hero", 1));
        Assert.False(AdminCommand.IsAllowed("coop.debug.hero.SetGold Aderimo 100"));
        Assert.Equal("coop.debug.hero.set_gold \"John Smith\" 10", AdminCommand.SetGoldByName("John Smith", 10));
    }

    [Theory]
    [InlineData(-1000, 0)]
    [InlineData(999_999_999, AdminCommand.MaxGold)]
    [InlineData(1000, 1000)]
    public void Gold_is_clamped(int asked, int sent) =>
        Assert.EndsWith(" " + sent.ToString(CultureInfo.InvariantCulture), AdminCommand.SetGold("main_hero", asked));

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-50, 1)]
    [InlineData(500, 100)]
    [InlineData(50, 50)]
    public void Hit_points_are_clamped(int asked, int sent) =>
        Assert.EndsWith(" " + sent, AdminCommand.SetHitPoints("main_hero", asked));

    [Fact]
    public void Other_numbers_are_clamped()
    {
        Assert.EndsWith(" 18", AdminCommand.SetAge("main_hero", 5));
        Assert.EndsWith(" 90", AdminCommand.SetAge("main_hero", 300));
        Assert.Equal("coop.debug.clan.give_influence clan_1 1", AdminCommand.GiveInfluence("clan_1", -5));
        Assert.Equal("coop.debug.clan.give_influence clan_1 10000", AdminCommand.GiveInfluence("clan_1", 99999));
        Assert.EndsWith(" -10000", AdminCommand.AddInfluence("clan_1", -99999));
        Assert.Equal("coop.server.shutdown 5", AdminCommand.ScheduleShutdown(1));
        Assert.Equal("coop.debug.mobile_party.set_troop_state p t false 0 0 0", AdminCommand.SetTroopState("p", "t", -3, 9, -1));
        Assert.Equal("coop.debug.mobile_party.character_ids lord_1_1", AdminCommand.PartyRoster("Hero_lord_1_1"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\n")]
    [InlineData("John Smith")]
    [InlineData("main_hero\"x")]
    public void A_bad_id_makes_no_command(string id)
    {
        Assert.Throws<ArgumentException>(() => AdminCommand.SetGold(id, 100));
        Assert.Throws<ArgumentException>(() => AdminCommand.SetHitPoints(id, 50));
    }

    [Fact]
    public void A_line_break_cannot_add_a_second_command()
    {
        Assert.Throws<ArgumentException>(() => AdminCommand.SetGold("main_hero\ncampaign.kill_hero X", 100));
        Assert.Throws<ArgumentException>(() => AdminCommand.HeroId("say\"x"));
        Assert.Equal("coop.debug.hero.id \"a b\"", AdminCommand.HeroId("a\nb"));
        Assert.False(AdminCommand.IsAllowed("coop.debug.hero.set_gold_state main_hero 100\ncampaign.kill_hero X"));
        Assert.False(AdminCommand.IsAllowed("campaign.add_gold_to_hero 99999"));
        Assert.True(AdminCommand.IsAllowed(AdminCommand.CancelShutdown()));
    }

    [Theory]
    [InlineData("[DedicatedServer] unknown command 'close' - try 'help'")]
    [InlineData("no game console command 'coop.debug.hero.SetGold'")]
    [InlineData("ERROR|Unknown command: coop.debug.hero.set_gold_state")]
    public void An_unknown_command_reply_is_named(string line)
    {
        Assert.True(AdminCommand.IsUnknownCommandReply([line]));
        Assert.Equal("The server does not know the command coop.debug.hero.set_gold_state.",
            CheatActions.UnknownCommandNote("coop.debug.hero.set_gold_state Hero_main_hero 5", [line]));
    }

    [Fact]
    public void A_normal_reply_is_not_called_unknown()
    {
        Assert.False(AdminCommand.IsUnknownCommandReply(["HERO_GOLD_SET hero=Hero_main_hero oldGold=1 newGold=5"]));
        Assert.False(AdminCommand.IsUnknownCommandReply([]));
        Assert.False(AdminCommand.IsUnknownCommandReply(null));
        Assert.Null(CheatActions.UnknownCommandNote("coop.debug.players.list", ["0: Guthric"]));
    }

    [Fact]
    public void The_load_line_is_small()
    {
        Assert.Equal("", ServerLoad.Format(ServerResources.Empty));
        var line = ServerLoad.Format(new ServerResources(3L * 1024 * 1024 * 1024 + 400L * 1024 * 1024, 12.4, 2));
        Assert.Equal("CPU 12% \u00B7 RAM " + 3.4.ToString("0.0", CultureInfo.CurrentCulture) + " GB", line);
        Assert.Equal("", new ServerLoad().Line(false, @"C:\Server", null));
    }

    [Fact]
    public void Only_processes_under_the_server_folder_count()
    {
        Assert.True(ServerResourceMonitor.IsUnderRoot(@"C:\TAOM-Coop-Host\Server\engine\dotnet\dotnet.exe", @"C:\TAOM-Coop-Host\Server"));
        Assert.True(ServerResourceMonitor.IsUnderRoot(@"C:\TAOM-Coop-Host\Server\BannerlordCoopServer.exe", @"C:\TAOM-Coop-Host\Server\"));
        Assert.False(ServerResourceMonitor.IsUnderRoot(@"C:\TAOM-Coop-Host\Server-old\engine\dotnet\dotnet.exe", @"C:\TAOM-Coop-Host\Server"));
        Assert.False(ServerResourceMonitor.IsUnderRoot(@"C:\Steam\steamapps\common\Mount & Blade II Bannerlord\bin\Win64_Shipping_Client\Bannerlord.exe", @"C:\TAOM-Coop-Host\Server"));
        Assert.False(ServerResourceMonitor.IsUnderRoot(@"C:\x.exe", ""));
    }

    [Fact]
    public void The_monitor_counts_the_started_process_and_nothing_for_an_empty_folder()
    {
        using var self = Process.GetCurrentProcess();
        var monitor = new ServerResourceMonitor(Path.Combine(_dir, "no-server-here"));
        Assert.Equal(ServerResources.Empty, monitor.Sample(null));
        var first = monitor.Sample(self.Id);
        Assert.Equal(1, first.ProcessCount);
        Assert.True(first.MemoryBytes > 0);
        var second = monitor.Sample(self.Id);
        Assert.InRange(second.CpuPercent, 0, 100);
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "BannerlordCoopLauncher.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
