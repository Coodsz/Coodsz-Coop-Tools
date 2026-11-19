using static BurningEmpires.Core.Tests.BeServerFixtures;

namespace BurningEmpires.Core.Tests;

public class BeServerLaunchTests
{
    [Fact]
    public void ANewCampaignStartsTheScriptWithNewSaveName()
    {
        var root = NewRoot("launch-new");
        try
        {
            Write(Path.Combine(root, BeServerPrepare.ScriptName), "script");
            var start = BeServerLaunch.Build(root, new BeStartOptions { SaveName = "BETest", NewCampaign = true, AcceptTerms = true });

            Assert.EndsWith("powershell.exe", start.FileName, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(root, start.WorkingDirectory);
            Assert.Contains("-File \"" + Path.Combine(root, BeServerPrepare.ScriptName) + "\"", start.Arguments);
            Assert.Contains(" -NoPrompt -Port 7210 -Region EU -NewSaveName \"BETest\"", start.Arguments);
            Assert.DoesNotContain("-BattleSize", start.Arguments);
            Assert.Equal("true", start.Environment[BeServerTerms.EnvironmentVariable]);
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void AnExistingSaveStartsWithSaveNameAndTheBattleSize()
    {
        var root = NewRoot("launch-save");
        try
        {
            Write(Path.Combine(root, BeServerPrepare.ScriptName), "script");
            var start = BeServerLaunch.Build(root, new BeStartOptions { SaveName = "My BE Campaign", BattleSize = 1500, AllowAddedModules = true });

            Assert.Contains("-SaveName \"My BE Campaign\" -BattleSize 1500 -AllowAddedModules", start.Arguments);
            Assert.False(start.Environment.ContainsKey(BeServerTerms.EnvironmentVariable));
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void TheTemplateBadNamesAndOutOfRangeSizesAreRefused()
    {
        var root = NewRoot("launch-refuse");
        try
        {
            Write(Path.Combine(root, BeServerPrepare.ScriptName), "script");
            Assert.Throws<InvalidOperationException>(() => BeServerLaunch.Build(root, new BeStartOptions { SaveName = "default_new_game" }));
            Assert.Throws<InvalidOperationException>(() => BeServerLaunch.Build(root, new BeStartOptions { SaveName = "default_new_game", NewCampaign = true }));
            Assert.Throws<InvalidOperationException>(() => BeServerLaunch.Build(root, new BeStartOptions { SaveName = "a\" -Evil" }));
            Assert.Throws<InvalidOperationException>(() => BeServerLaunch.Build(root, new BeStartOptions { SaveName = "$(Remove-Item)" }));
            Assert.Throws<InvalidOperationException>(() => BeServerLaunch.Build(root, new BeStartOptions { SaveName = "_bad", NewCampaign = true }));
            Assert.Throws<InvalidOperationException>(() => BeServerLaunch.Build(root, new BeStartOptions { SaveName = "BETest", BattleSize = 5000 }));
            Assert.Throws<InvalidOperationException>(() => BeServerLaunch.Build(root, new BeStartOptions { SaveName = "" }));
            Assert.Throws<InvalidOperationException>(() => BeServerLaunch.Build(Path.Combine(root, "no-server"), new BeStartOptions { SaveName = "BETest" }));
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void NewCampaignNamesFollowTheScriptsRules()
    {
        var root = NewRoot("new-name");
        try
        {
            Save(root, "Taken", "Native;BurningEmpires", "v2.0.2");
            Assert.Null(BeServerLaunch.NewCampaignProblem("BETest", root, null));
            Assert.Null(BeServerLaunch.NewCampaignProblem("  My Campaign 2 ", root, null));
            Assert.Contains("already on the server", BeServerLaunch.NewCampaignProblem("Taken", root, null));
            Assert.Equal(BeServerLaunch.TemplateReason, BeServerLaunch.NewCampaignProblem("default_new_game", root, null));
            Assert.NotNull(BeServerLaunch.NewCampaignProblem("", root, null));
            Assert.NotNull(BeServerLaunch.NewCampaignProblem("CON", root, null));
            Assert.NotNull(BeServerLaunch.NewCampaignProblem("..\\up", root, null));
            Assert.NotNull(BeServerLaunch.NewCampaignProblem(new string('a', 65), root, null));
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void ScriptRefusalsGetPlainWords()
    {
        Assert.Contains("already on the server", BeServerLaunch.ScriptRefusal("A server campaign named 'BETest' already exists. Choose another name."));
        Assert.Contains("Prepare server", BeServerLaunch.ScriptRefusal("The fresh-campaign template is missing: C:\\x\\default_new_game.sav"));
        Assert.Contains("port", BeServerLaunch.ScriptRefusal("UDP port 7210 is already in use. Stop the existing server or choose another port."));
        Assert.Null(BeServerLaunch.ScriptRefusal("[DedicatedServer] SERVING"));
    }
}
