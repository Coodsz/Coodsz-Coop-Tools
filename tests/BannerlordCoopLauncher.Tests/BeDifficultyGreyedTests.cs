using System.Text.Json;
using System.Text.RegularExpressions;
using TaomCoop.Core;

namespace BannerlordCoopLauncher.Tests;

// szymon (BE ticket, 2026-11-06): the Burning Empires Server Tool greys out a difficulty box when Coop's
// mod-config.json has no such setting yet, and Save settings skipped those boxes without a word. Now a note
// says why and what to do, and the save status says which part was not saved.
public class BeDifficultyGreyedTests
{
    [Fact]
    public void Missing_count_is_every_setting_without_a_file_and_none_with_the_full_block()
    {
        Assert.Equal(CoopDifficulty.Settings.Count, CoopDifficulty.MissingCount(""));
        Assert.Equal(CoopDifficulty.Settings.Count, CoopDifficulty.MissingCount("{ \"modOptions\": { \"fastForwardEnabled\": true } }"));

        var full = "{\n  \"difficulty\": {\n" + string.Join(",\n", CoopDifficulty.Settings.Select(setting => "    \"" + setting.Key + "\": \"Easy\""))
            + "\n  }\n}\n";
        Assert.Equal(0, CoopDifficulty.MissingCount(full));

        var partial = "{ \"difficulty\": { \"playerReceivedDamage\": \"VeryEasy\", \"battleDeath\": \"Realistic\" } }";
        Assert.Equal(CoopDifficulty.Settings.Count - 2, CoopDifficulty.MissingCount(partial));
    }

    [Fact]
    public void The_note_and_the_save_line_say_what_to_do_in_plain_words()
    {
        foreach (var text in new[] { CoopDifficulty.MissingNote, CoopDifficulty.SkippedOnSave })
        {
            Assert.Contains("Start the server once", text);
            Assert.Contains("mod-config.json", text);
            Assert.DoesNotContain(":", text);
            // No hyphen between words (the file name is the only hyphen).
            Assert.DoesNotMatch(new Regex(@"[A-Za-z]-[A-Za-z]"), text.Replace("mod-config.json", ""));
        }

        Assert.Contains("greyed out", CoopDifficulty.MissingNote);
        Assert.Contains("open Settings again", CoopDifficulty.MissingNote);
        Assert.Contains("not the greyed out difficulty boxes", CoopDifficulty.SkippedOnSave);
    }

    // The WPF hosts are not built by this test project, so their wiring is checked in the source.
    [Theory]
    [InlineData("BurningEmpiresHost")]
    [InlineData("TaomCoopHost")]
    public void Both_server_tools_show_the_note_and_the_save_line(string host)
    {
        var folder = Path.Combine(MapCheckGuardTests.RepoRoot(), "src", host);
        var settings = File.ReadAllText(Path.Combine(folder, "MainWindow.Settings.cs"));
        var control = File.ReadAllText(Path.Combine(folder, "MainWindow.ServerControl.cs"));
        Assert.Contains("Text = CoopDifficulty.MissingNote", settings);
        Assert.Contains("_difficultySkipped++", settings);
        Assert.Contains("_difficultySkipped > 0 && fromSettingsPage", control);
        Assert.Contains("CoopDifficulty.SkippedOnSave", control);
        Assert.Contains("LoadDifficulty(onlyGreyed: true)", File.ReadAllText(Path.Combine(folder, "MainWindow.xaml.cs")));
        Assert.Contains("SaveSettingsPage(fromSettingsPage: false)", File.ReadAllText(Path.Combine(folder, "MainWindow.GameSpeed.cs")));
    }

    [Theory]
    [InlineData("ar")]
    [InlineData("de")]
    [InlineData("fr")]
    [InlineData("pt")]
    [InlineData("ru")]
    [InlineData("tr")]
    public void Both_texts_are_translated(string language)
    {
        var file = Path.Combine(MapCheckGuardTests.RepoRoot(), "src", "BannerlordCoopLauncher.Core", "Localization", "Languages", language + ".json");
        var texts = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(file))!;
        foreach (var english in new[] { CoopDifficulty.MissingNote, CoopDifficulty.SkippedOnSave })
        {
            Assert.True(texts.TryGetValue(english, out var translated), language + " is missing: " + english);
            Assert.False(string.IsNullOrWhiteSpace(translated));
            Assert.Contains("mod-config.json", translated);
            Assert.NotEqual(english, translated);
        }
    }
}
