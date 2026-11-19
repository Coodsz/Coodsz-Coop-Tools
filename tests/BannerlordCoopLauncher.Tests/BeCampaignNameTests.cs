using System.Text.RegularExpressions;
using BannerlordCoopLauncher.Core.Saves;
using static BurningEmpires.Core.Tests.BeServerFixtures;

namespace BurningEmpires.Core.Tests;

// Live run 4: New campaign refused YÜZÜK TEST, and the status read "Up for 8:55:45" while a new
// start was still booting (a server-instance.lock left by an earlier run). The name check now reads
// the server folder's own start script (SaveSet.NewSaveNameProblemFor), like Coodsz Host.
public class BeCampaignNameTests
{
    // The -NewSaveName line of a start script from before letters of any language were allowed.
    private const string OldScript = "    if ($selectedSaveName -cnotmatch '^[A-Za-z0-9][A-Za-z0-9 _-]{0,63}$') {\n        throw 'New save names must contain 1-64 letters, numbers, spaces, hyphens, or underscores and start with a letter or number.'\n    }\n";

    [Fact]
    public void NewCampaignNamesFollowTheServerFoldersOwnScript()
    {
        var root = NewRoot("names");
        try
        {
            var newScript = ShippedScript();
            Assert.Null(BeServerLaunch.NewCampaignProblem("Kampanya_1", root, newScript));
            Assert.Null(BeServerLaunch.NewCampaignProblem("Kampanya_1", root, OldScript));
            // The new script takes any letter, so YÜZÜK TEST is a fine new campaign there.
            Assert.Null(BeServerLaunch.NewCampaignProblem("YÜZÜK TEST", root, newScript));
            Assert.Null(BeServerLaunch.NewCampaignProblem("YÜZÜKLERİN EFENDİSİ", root, newScript));
            // An older A to Z script would refuse it after the start, so the tool says why before.
            Assert.Equal(BeServerLaunch.NewNameReason, BeServerLaunch.NewCampaignProblem("YÜZÜK TEST", root, OldScript));
            Assert.Contains("A to Z", BeServerLaunch.NewNameReason);
            // A script that cannot be read counts as the old rule.
            Assert.Equal(BeServerLaunch.NewNameReason, BeServerLaunch.NewCampaignProblem("YÜZÜK TEST", root, null));
            foreach (var script in new[] { newScript, OldScript })
            {
                Assert.Equal(SaveSet.NewSaveNameRule, BeServerLaunch.NewCampaignProblem("a<b", root, script));
                Assert.NotNull(BeServerLaunch.NewCampaignProblem("x\"y", root, script));
                Assert.NotNull(BeServerLaunch.NewCampaignProblem("CON", root, script));
                Assert.Equal(BeServerLaunch.TemplateReason, BeServerLaunch.NewCampaignProblem("default_new_game", root, script));
                Assert.NotNull(BeServerLaunch.NewCampaignProblem("Kampanya.", root, script));
                Assert.NotNull(BeServerLaunch.NewCampaignProblem(new string('a', 65), root, script));
                Assert.Null(BeServerLaunch.NewCampaignProblem(new string('a', 64), root, script));
            }

            // Start reads the script in the server folder. A trailing space never reaches it or the file name.
            var scriptPath = Path.Combine(root, BeServerPrepare.ScriptName);
            Write(scriptPath, OldScript);
            var start = BeServerLaunch.Build(root, new BeStartOptions { SaveName = "Kampanya_1 ", NewCampaign = true });
            Assert.Contains("-NewSaveName \"Kampanya_1\"", start.Arguments);
            var refused = Assert.Throws<InvalidOperationException>(() =>
                BeServerLaunch.Build(root, new BeStartOptions { SaveName = "YÜZÜK TEST", NewCampaign = true }));
            Assert.Equal(BeServerLaunch.NewNameReason, refused.Message);
            Write(scriptPath, newScript);
            Assert.Contains("-NewSaveName \"YÜZÜK TEST\"",
                BeServerLaunch.Build(root, new BeStartOptions { SaveName = "YÜZÜK TEST", NewCampaign = true }).Arguments);
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void AnOlderScriptGetsTheBeWordsNotTheSharedUpdateWords()
    {
        // The shared words ask to press Start server again so the script is updated. BE never updates
        // the Workshop script it copied, so New campaign and Start both say BE's own line.
        var root = NewRoot("names-old-script");
        try
        {
            var problem = BeServerLaunch.NewCampaignProblem("YÜZÜK TEST", root, OldScript);
            Assert.Equal(BeServerLaunch.NewNameReason, problem);
            Assert.NotEqual(SaveSet.OldScriptNewSaveNameRule, problem);
            Assert.DoesNotContain("press Start server again", problem);

            Write(Path.Combine(root, BeServerPrepare.ScriptName), OldScript);
            var refused = Assert.Throws<InvalidOperationException>(() =>
                BeServerLaunch.Build(root, new BeStartOptions { SaveName = "YÜZÜK TEST", NewCampaign = true }));
            Assert.Equal(BeServerLaunch.NewNameReason, refused.Message);
            // Other refusals keep the shared words.
            Assert.Equal(SaveSet.NewSaveNameRule, BeServerLaunch.NewNameProblem("a<b", OldScript));
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void TheToolsRuleIsTheScriptsOwnRule()
    {
        var script = ShippedScript();
        var match = Regex.Match(script, @"\$selectedSaveName -cnotmatch '(?<rule>[^']+)'");
        Assert.True(match.Success);
        var rule = new Regex(match.Groups["rule"].Value, RegexOptions.CultureInvariant);
        var root = NewRoot("names-script");
        try
        {
            foreach (var name in new[] { "Kampanya_1", "My Campaign 2", "YÜZÜK TEST", "YÜZÜKLERİN EFENDİSİ", "a<b", "Kampanya.", new string('a', 65) })
                Assert.Equal(rule.IsMatch(name), BeServerLaunch.NewCampaignProblem(name, root, script) == null);
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void AnExistingSaveWithAnyLettersStartsUnchanged()
    {
        var root = NewRoot("names-existing");
        try
        {
            Write(Path.Combine(root, BeServerPrepare.ScriptName), OldScript);
            var start = BeServerLaunch.Build(root, new BeStartOptions { SaveName = "YÜZÜKLERİN EFENDİSİ" }, Path.Combine(root, "Game Saves"));
            Assert.Contains(" -SaveName \"YÜZÜKLERİN EFENDİSİ\"", start.Arguments);
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void AnOldLockFileNeverGivesANewStartItsUptime()
    {
        var clickedUtc = DateTime.UtcNow.AddSeconds(-2);
        var oldLock = DateTime.Now.AddHours(-8).AddMinutes(-55);

        Assert.Equal(clickedUtc.ToLocalTime(), BeServerLaunch.UptimeFrom(oldLock, clickedUtc));
        // The engine started after the click: its own start counts.
        var engine = DateTime.Now;
        Assert.Equal(engine, BeServerLaunch.UptimeFrom(engine, clickedUtc));
        // A server this window did not start keeps the time it was found with.
        Assert.Equal(oldLock, BeServerLaunch.UptimeFrom(oldLock, null));
        Assert.Null(BeServerLaunch.UptimeFrom(null, clickedUtc));
    }

    // The start script this repo ships (Coodsz Host writes it), with the any letter rule.
    private static string ShippedScript() =>
        File.ReadAllText(Path.Combine(RepoRoot(), "src", "CoodszHost", "DedicatedServerRepair", "Start-ModdedServer.ps1"));

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "BannerlordCoopLauncher.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
