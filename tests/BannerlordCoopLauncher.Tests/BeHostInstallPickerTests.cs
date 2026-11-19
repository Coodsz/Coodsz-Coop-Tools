using BannerlordCoopLauncher.Tests;

namespace BurningEmpires.Core.Tests;

// Install or repair Burning Empires in the server tool follows the client: a lone hotfix asks for the
// main file instead of going in alone, and a lone main file is refused before the question.
public sealed class BeHostInstallPickerTests
{
    [Fact]
    public void A_lone_hotfix_asks_the_host_for_the_main_file_and_a_lone_main_file_is_refused_before_the_question()
    {
        var host = SourceFiles.Window(RepoRoot(), "BurningEmpiresHost");
        var install = SourceFiles.Method(host, "private async void InstallOrRepairBe_Click(");
        Assert.Contains("BeArchive.SetProblem(found, gameRoot)", install);
        Assert.Contains("setProblem == BeArchive.MainFileMissing", install);
        Assert.Contains("PickBeDownload(contentProblem, archives, hotfixOnly)", install);
        var refuse = install.IndexOf("else if (setProblem != null && contentProblem != null)", StringComparison.Ordinal);
        Assert.True(refuse > 0);
        Assert.True(refuse < install.IndexOf("AskUnpack(contentProblem, archives)", StringComparison.Ordinal));

        // Cancel stops: nothing is unpacked, so a lone hotfix never goes in alone, and no browser opens.
        var pick = SourceFiles.Method(host, "private IReadOnlyList<string>? PickBeDownload(");
        Assert.Contains("return null;", pick);
        Assert.Contains(".Concat(found)", pick);

        // The old hotfix hint after the install is gone, since the set is checked before the question.
        Assert.DoesNotContain("The hotfix download was not found. Download it from Nexus Mods", host);
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "BannerlordCoopLauncher.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
