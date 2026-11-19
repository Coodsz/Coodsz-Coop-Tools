using System.Diagnostics;
using BannerlordCoopLauncher.Core.Launch;

namespace BannerlordCoopLauncher.Tests;

public class ClientLaunchGuardTests
{
    private static Process StartIdle() => Process.Start(new ProcessStartInfo("ping.exe", "-n 120 127.0.0.1")
    {
        CreateNoWindow = true,
        UseShellExecute = false,
    })!;

    [Fact]
    public async Task Close_ends_a_process_that_is_still_running()
    {
        using var process = StartIdle();
        using var watch = Process.GetProcessById(process.Id);
        try
        {
            Assert.True(await ClientLaunchGuard.CloseAsync(new[] { process }, TimeSpan.FromSeconds(15)));
            Assert.True(watch.HasExited);
        }
        finally
        {
            if (!watch.HasExited)
                watch.Kill();
        }
    }

    [Fact]
    public async Task Close_is_fine_with_a_process_that_already_ended()
    {
        using var process = StartIdle();
        process.Kill();
        await process.WaitForExitAsync();

        Assert.True(await ClientLaunchGuard.CloseAsync(new[] { process }, TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task Close_with_no_game_running_has_nothing_to_do()
    {
        Assert.True(await ClientLaunchGuard.CloseAsync(Array.Empty<Process>(), TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void The_question_says_what_closing_costs_and_the_way_out_names_task_manager()
    {
        Assert.Contains("Close it now", ClientLaunchGuard.AlreadyOpenAsk);
        Assert.Contains("not saved", ClientLaunchGuard.AlreadyOpenAsk);
        Assert.Contains("Task Manager", ClientLaunchGuard.CouldNotCloseGame);
    }

    [Theory]
    [InlineData("ar")]
    [InlineData("de")]
    [InlineData("fr")]
    [InlineData("pt")]
    [InlineData("ru")]
    [InlineData("tr")]
    public void Every_language_translates_the_popup(string language)
    {
        var file = Path.Combine(RepoRoot(), "src", "BannerlordCoopLauncher.Core", "Localization", "Languages", language + ".json");
        var strings = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(file))!;
        foreach (var text in new[]
                 {
                     ClientLaunchGuard.AlreadyOpenTitle, ClientLaunchGuard.AlreadyOpenAsk, ClientLaunchGuard.AlreadyOpenKept,
                     ClientLaunchGuard.ClosedOpenGame, ClientLaunchGuard.CouldNotCloseGame,
                 })
            Assert.False(string.IsNullOrWhiteSpace(strings.GetValueOrDefault(text)), language + " is missing: " + text);
        Assert.Contains("\n\n", strings[ClientLaunchGuard.AlreadyOpenAsk]);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "src", "BannerlordCoopLauncher.Core")))
            dir = dir.Parent;
        return dir!.FullName;
    }
}
