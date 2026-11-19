using System.Text.RegularExpressions;
using BannerlordCoopLauncher.Core.Localization;

namespace BannerlordCoopLauncher.Tests;

// Translator.Use switches the language for the whole process. Run in parallel, the switch to German
// or Arabic leaked into other classes that check English text (the settings sync answers), so these
// tests run on their own, after the parallel classes.
[Collection(TranslatorLanguage.Name)]
public class TranslatorTests
{
    private static readonly string[] Codes = Translator.Languages.Where(language => language.Code != Translator.English).Select(language => language.Code).ToArray();

    [Fact]
    public void Every_language_has_the_same_entries_and_keeps_the_values()
    {
        var german = Translator.Entries("de");
        Assert.True(german.Count > 1000, "The German table looks empty.");
        foreach (var code in Codes)
        {
            var table = Translator.Entries(code);
            var missing = german.Keys.Where(key => !table.ContainsKey(key)).Take(5).ToList();
            Assert.True(missing.Count == 0, code + " is missing: " + string.Join(" | ", missing));
            foreach (var (english, translated) in table)
            {
                // A value the translation names must exist in the English text, or the
                // screen would show a raw {3}.
                var allowed = Placeholders(english);
                var used = Placeholders(translated);
                Assert.True(used.IsSubsetOf(allowed), code + ": " + english + " -> " + translated);
                // Only a plural ending ("file{1}") may be dropped.
                foreach (var dropped in allowed.Except(used))
                    Assert.Matches(@"[A-Za-z]\{" + dropped + @"\}", english);
            }
        }
    }

    [Fact]
    public void Text_is_translated_whole_with_values_or_left_in_english()
    {
        var path = Path.Combine(Path.GetTempPath(), "lang-" + Guid.NewGuid().ToString("N") + ".txt");
        var before = Translator.SettingsPath;
        Translator.SettingsPath = path;
        try
        {
            Translator.Use("de");
            var german = Translator.Entries("de");
            Assert.Equal(german["Start"], Translator.T("Start"));
            Assert.Equal("  " + german["Start"] + " ", Translator.T("  Start "));
            Assert.Equal("Not a line any tool shows 123", Translator.T("Not a line any tool shows 123"));

            var template = german.Keys.First(key => key == "{0} must be a whole number from {1} to {2}.");
            var expected = german[template].Replace("{0}", "Autosave").Replace("{1}", "0").Replace("{2}", "60");
            Assert.Equal(expected, Translator.T("Autosave must be a whole number from 0 to 60."));

            // Lines put together in code are translated line by line.
            Assert.Equal(german["Start"] + "\n" + german["Stop"], Translator.T("Start\nStop"));

            Assert.False(Translator.HasSavedLanguage);
            Translator.Use("ar", save: true);
            Assert.True(Translator.RightToLeft);
            Assert.True(Translator.HasSavedLanguage);
            Assert.Equal("ar", Translator.StartupLanguage());

            Translator.Use(Translator.English);
            Assert.Equal("Start", Translator.T("Start"));
        }
        finally
        {
            Translator.Use(Translator.English);
            Translator.SettingsPath = before;
            File.Delete(path);
        }
    }

    [Fact]
    public void A_report_line_on_the_logs_page_shows_its_translated_body_and_keeps_the_english()
    {
        const string key = "What happened: The server stopped before anyone could join. The campaign save named {0} was made with {1} and this server has {2}. The mods match, but a save made with another version of a mod stays hidden, so the server did not open that world.";
        var line = key.Replace("{0}", "X").Replace("{1}", "CoopNightly v0.1.8.123").Replace("{2}", "v0.1.8.130");
        var entry = CoodszHost.Services.LogLineFormatter.Format(line)!.Value;
        var unknown = CoodszHost.Services.LogLineFormatter.Format("What happened: Not a line any tool shows 123.")!.Value;
        try
        {
            foreach (var code in Codes)
            {
                Translator.Use(code);
                var full = Translator.Entries(code)[key].Replace("{0}", "X").Replace("{1}", "CoopNightly v0.1.8.123").Replace("{2}", "v0.1.8.130");
                var shown = CoodszHost.Services.LogLineFormatter.ShownMessage(entry);
                Assert.Equal(full[(full.IndexOfAny([':', '\uFF1A']) + 1)..].TrimStart(), shown);
                Assert.False(shown.StartsWith(Translator.T("What happened"), StringComparison.Ordinal), code + ": " + shown);
                Assert.DoesNotContain("The server stopped", shown);
                Assert.Equal("Not a line any tool shows 123.", CoodszHost.Services.LogLineFormatter.ShownMessage(unknown));
            }

            Translator.Use(Translator.English);
            Assert.Equal(entry.Message, CoodszHost.Services.LogLineFormatter.ShownMessage(entry));
        }
        finally
        {
            Translator.Use(Translator.English);
        }

        // The run shows the translation but keeps the English, so the log zip stays English.
        Assert.Contains("LogRun(entry.Message, LogLineFormatter.ShownMessage(entry))", SourceFiles.Read(Path.Combine("src", "CoodszHost", "MainWindow.ServerLog.cs")));
        Assert.Contains("new Run(shown) { Tag = english }", SourceFiles.Read(Path.Combine("src", "BannerlordCoopLauncher", "UiLanguage.cs")));
    }

    private static HashSet<string> Placeholders(string text) =>
        Regex.Matches(text, @"\{(\d+)\}").Select(match => match.Groups[1].Value).ToHashSet();
}

/// <summary>Tests that change Translator.Current, the process wide UI language.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class TranslatorLanguage
{
    public const string Name = "Translator language";
}
