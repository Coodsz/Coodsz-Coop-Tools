// Tests for SteamBanFile and the graceful stop, which adapt code from CoopKeep
// (https://github.com/Aderimo/bannerlord-coopkeep), Server/SteamBanList.cs and
// Server/ServerSupervisor.cs.

using System.Text.Json;
using BannerlordCoopLauncher.Core.Server;

namespace BannerlordCoopLauncher.Tests;

public sealed class SteamBanFileTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "coodsz-bans-" + Guid.NewGuid().ToString("N"));

    private string BanPath => Path.Combine(_folder, "server-data", SteamBanFile.FileName);

    public void Dispose()
    {
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, recursive: true);
    }

    [Theory]
    [InlineData("76561198000000000", true)]
    [InlineData("76561197960265728", true)]
    [InlineData("7656119800000000", false)]
    [InlineData("765611980000000000", false)]
    [InlineData("12345678901234567", false)]
    [InlineData("7656119800000000a", false)]
    [InlineData("0", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void A_SteamID64_is_17_digits_that_start_with_7656119(string? id, bool valid) =>
        Assert.Equal(valid, SteamBanFile.IsSteamId64(id));

    [Fact]
    public void The_list_stays_the_plain_array_Launcher_Cheats_reads_and_leaves_no_temp_file()
    {
        SteamBanFile.Write(BanPath, new[] { "76561198000000001", " 76561198000000002 ", "76561198000000001", "12", "0" });

        var written = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(BanPath));
        Assert.Equal(new[] { "76561198000000001", "76561198000000002" }, written);
        Assert.False(File.Exists(BanPath + ".tmp"));
        Assert.Equal(written, SteamBanFile.Read(BanPath));
    }

    [Fact]
    public void The_older_files_and_Coop_s_own_shape_are_read()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(BanPath)!);
        File.WriteAllText(BanPath, "[\"76561198000000003\", \"3\", \"76561198000000003\"]");
        Assert.Equal(new[] { "76561198000000003" }, SteamBanFile.Read(BanPath));

        File.WriteAllText(BanPath, "{\"steamIds\": [\"76561198000000004\"], \"entries\": [{\"steamId\": \"76561198000000004\", \"heroName\": \"Aderimo\"}]}");
        Assert.Equal(new[] { "76561198000000004" }, SteamBanFile.Read(BanPath));
    }

    [Fact]
    public void A_missing_or_broken_file_bans_nobody()
    {
        Assert.Empty(SteamBanFile.Read(BanPath));
        Directory.CreateDirectory(Path.GetDirectoryName(BanPath)!);
        File.WriteAllText(BanPath, "[\"76561198000000005\"");
        Assert.Empty(SteamBanFile.Read(BanPath));
    }
}

public class GracefulStopWiringTests
{
    [Fact]
    public void Stop_and_Restart_send_stop_and_wait_while_a_broken_world_is_ended_without_saving()
    {
        var host = SourceFiles.Window(RepoRoot(), "CoodszHost");
        var stop = SourceFiles.Method(host, "private async void Stop_Click(");
        Assert.Contains("_session.StopGracefully()", stop);
        Assert.Contains("_session.Stop()", stop.Split("var saveNote")[0]);
        Assert.Contains("await RestartServerAsync();", SourceFiles.Method(host, "private async void Restart_Click("));
        var restart = SourceFiles.Method(host, "private async Task<bool> RestartServerAsync(");
        Assert.Contains("await WarnPlayersOfRestart();", restart);
        Assert.Contains("_session.StopGracefully()", restart);
        Assert.DoesNotContain("StopGracefully", SourceFiles.Method(host, "private async void RestartLastGood_Click("));
        Assert.DoesNotContain("StopGracefully", SourceFiles.Method(host, "private void HoldBrokenAutosave("));

        var session = File.ReadAllText(Path.Combine(RepoRoot(), "src", "CoodszHost", "Services", "ServerSession.cs"));
        Assert.Contains("TryConsoleLine(ServerCommand.Stop())", session);
        Assert.Contains("TimeSpan.FromSeconds(120)", session);
        Assert.Contains("process.Kill(entireProcessTree: true);", session);
        Assert.Contains("IsThisServerEngine(process.MainModule?.FileName, ServerRoot)", session);

        var taom = SourceFiles.Window(RepoRoot(), "TaomCoopHost");
        var saveThenStop = SourceFiles.Method(taom, "private async Task SaveThenStop(");
        Assert.Contains("await Task.Run(AskServerToStop);", saveThenStop);
        Assert.True(saveThenStop.IndexOf("AskServerToStop", StringComparison.Ordinal) < saveThenStop.IndexOf("StopServer();", StringComparison.Ordinal));
    }

    [Fact]
    public void Restart_warnings_fit_on_one_line_of_say()
    {
        foreach (var message in new[] { "The server restarts in 30 seconds. The campaign is saved first.", "The server restarts in 10 seconds." })
        {
            var line = ServerCommand.Say(message);
            Assert.True(ServerCommand.IsAllowed(line));
            Assert.Equal("say " + message, line);
        }
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "BannerlordCoopLauncher.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
