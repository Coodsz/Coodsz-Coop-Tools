using System.Text.RegularExpressions;
using System.Xml.Linq;
using BannerlordCoopLauncher.Core.Localization;
using BannerlordCoopLauncher.Core.Modules;
using BannerlordCoopLauncher.Core.Updates;

namespace BannerlordCoopLauncher.Tests;

public class PlayerTextTests
{
    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "BannerlordCoopLauncher.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    [Fact]
    public void Whats_new_lines_have_no_hyphen_between_words()
    {
        // File names keep theirs (mod-config.json), so a word with a dot or slash in it is skipped.
        var lines = WhatsNew.Friends.Concat(WhatsNew.AD1259).Concat(WhatsNew.Taom).Concat(WhatsNew.Be).SelectMany(entry => entry.Lines).Distinct();
        foreach (var line in lines)
            foreach (var word in line.Split(' ').Select(word => word.Trim(',', ';', ':', '(', ')', '!', '?', '"')))
                if (!word.TrimEnd('.').Contains('.') && !word.Contains('/') && !word.Contains('\\'))
                    Assert.False(Regex.IsMatch(word, "[A-Za-z]-[A-Za-z]"), "Hyphen in What's new: " + word + " in: " + line);
    }

    [Theory]
    [InlineData("BannerlordCoopLauncher")]
    [InlineData("TaomCoopLauncher")]
    [InlineData("AD1259Launcher")]
    public void Client_launchers_have_no_Refresh_button_so_no_line_asks_for_one(string project)
    {
        var xaml = XDocument.Load(Path.Combine(RepoRoot(), "src", project, "MainWindow.xaml"));
        Assert.DoesNotContain(xaml.Descendants(), element => (string?)element.Attribute("Content") == "Refresh");
        Assert.Contains(xaml.Descendants(), element => (string?)element.Attribute("Content") == "Check mods");

        // The Add mods window (CustomModWindow.cs) has its own Refresh button, so it may name it.
        var files = Directory.EnumerateFiles(Path.Combine(RepoRoot(), "src", project), "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains("\\obj\\") && !file.Contains("\\bin\\") && Path.GetFileName(file) != "CustomModWindow.cs")
            .Concat(project == "BannerlordCoopLauncher"
                ? [Path.Combine(RepoRoot(), "src", "BannerlordCoopLauncher.Core", "Modules", "ModuleResolver.cs"),
                   Path.Combine(RepoRoot(), "src", "BannerlordCoopLauncher.Core", "Launch", "StartupCrashExplain.cs")]
                : []);
        var asks = new Regex(@"(?i:click|press|then|and) Refresh\b|Refresh the profile|Refresh to retry");
        foreach (var file in files)
        {
            var hit = asks.Match(File.ReadAllText(file));
            Assert.False(hit.Success, Path.GetFileName(file) + " asks for a Refresh button: " + hit.Value);
        }
    }

    [Fact]
    public void Lines_that_now_say_Check_mods_are_translated()
    {
        var keys = new[]
        {
            "Connect is locked. Resolve the marked modules in Steam, then click Check mods.",
            "Click Check mods before launching.",
            "{0} is in more than one folder. Move the extra folder out of the Bannerlord Modules folder, click Check mods, and press Test to main menu again.",
            "At most {0} screenshots can be added.",
        };
        foreach (var language in Translator.Languages.Where(language => language.Code != Translator.English))
        {
            var table = Translator.Entries(language.Code);
            var checkMods = table["Check mods"];
            foreach (var key in keys)
                Assert.True(table.ContainsKey(key), language.Code + " has no translation for: " + key);
            Assert.Contains(checkMods, table[keys[0]]);
            Assert.Contains(checkMods, table[keys[1]]);
        }
    }
}
