using System.Xml.Linq;
using CoodszHost.Services;

namespace BannerlordCoopLauncher.Tests;

public class CoodszHostModSupportTests
{
    [Fact]
    public void Rbm_switches_are_written_and_other_rbm_settings_are_kept()
    {
        var root = Path.Combine(Path.GetTempPath(), "rbm-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(root, "Configs", "RBM", "config.xml");
        try
        {
            // No config yet: RBM would run every part. Defaults turn only Campaign off.
            Assert.NotNull(RbmSwitches.Apply(new RbmSettings(), path));
            var created = XDocument.Load(path).Root!;
            // RBM resets a config without its own version to defaults (Campaign on), so the new file carries it.
            Assert.Equal(RbmConfigFile.ConfigVersion, created.Attribute("version")?.Value);
            Assert.Equal("0", created.Element("RBMCampaign")!.Element("Enabled")!.Value);
            Assert.Equal("1", created.Element("RBMCombat")!.Element("Enabled")!.Value);
            Assert.Equal("1", created.Element("RBMAI")!.Element("Enabled")!.Value);
            Assert.Equal("1", created.Element("RBMTournament")!.Element("Enabled")!.Value);

            // An older config without the Campaign part, with other settings that must stay.
            File.WriteAllText(path,
                "<Config><RBMTournament><Enabled>1</Enabled></RBMTournament><RBMAI><Enabled>1</Enabled><PostureEnabled>1</PostureEnabled></RBMAI>"
                + "<RBMCombat><Enabled>1</Enabled><Global><ArmorStatusUIEnabled>0</ArmorStatusUIEnabled></Global></RBMCombat></Config>");
            Assert.NotNull(RbmSwitches.Apply(new RbmSettings { Ai = false }, path));
            var updated = XDocument.Load(path).Root!;
            Assert.Equal("0", updated.Element("RBMCampaign")!.Element("Enabled")!.Value);
            Assert.Equal("0", updated.Element("RBMAI")!.Element("Enabled")!.Value);
            Assert.Equal("1", updated.Element("RBMAI")!.Element("PostureEnabled")!.Value);
            Assert.Equal("0", updated.Element("RBMCombat")!.Element("Global")!.Element("ArmorStatusUIEnabled")!.Value);

            // Same settings again: nothing to write.
            Assert.Null(RbmSwitches.Apply(new RbmSettings { Ai = false }, path));

            Assert.True(RbmSwitches.InUse(new[] { "Native", "rbm" }));
            Assert.False(RbmSwitches.InUse(new[] { "Native", "Coop" }));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
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
