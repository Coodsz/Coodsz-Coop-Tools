using System.Text.Json;

namespace BannerlordCoopLauncher.Tests;

// The server terms window names its button "I accept" in its sentences. The button has its own language key, so in
// every language the sentences name the same word the button shows (Chaos found ja, ko and zh-CN disagreeing).
public sealed class ServerTermsButtonTests
{
    [Fact]
    public void Every_language_names_the_terms_button_the_way_the_button_shows_it()
    {
        foreach (var language in new[] { "ar", "de", "fr", "pt", "ru", "tr", "ja", "ko", "zh-CN" })
        {
            var table = JsonSerializer.Deserialize<Dictionary<string, string>>(
                SourceFiles.Read(Path.Combine("src", "BannerlordCoopLauncher.Core", "Localization", "Languages", language + ".json")))!;
            var button = table["I accept"];
            Assert.False(string.IsNullOrWhiteSpace(button), language);
            Assert.NotEqual("I accept", button);
            var sentences = table.Where(pair => pair.Key.Contains("I accept", StringComparison.Ordinal) && pair.Key != "I accept").ToList();
            Assert.NotEmpty(sentences);
            foreach (var (key, value) in sentences)
            {
                Assert.True(value.Contains(button, StringComparison.Ordinal), language + ": " + key);
                Assert.DoesNotContain("I accept", value);
            }
        }
    }

    [Fact]
    public void The_text_scan_keeps_the_button()
    {
        // A lone capital I counts as a word, or tools/ExtractUiText would skip "I accept" again.
        Assert.Contains(@"(?:[A-Za-z]{2,}|\bI):? [A-Za-z]{2,}", SourceFiles.Read(Path.Combine("tools", "ExtractUiText", "Program.cs")));
    }
}
