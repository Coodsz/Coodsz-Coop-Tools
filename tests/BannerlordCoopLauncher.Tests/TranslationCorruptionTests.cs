using System.Text.Json;

namespace BannerlordCoopLauncher.Tests;

// A translation saved through the wrong code page turns its letters into "?" (de "W?hle", ru "?????").
// Some languages add a real "?" ("Immer noch nicht? Klicke ..."), so only the broken shapes count.
public class TranslationCorruptionTests
{
    [Fact]
    public void No_translation_has_letters_turned_into_question_marks()
    {
        var folder = Path.Combine(SourceFiles.Root(), "src", "BannerlordCoopLauncher.Core", "Localization", "Languages");
        var problems = new List<string>();
        foreach (var file in Directory.GetFiles(folder, "*.json").OrderBy(path => path, StringComparer.Ordinal))
        {
            var language = Path.GetFileNameWithoutExtension(file);
            var strings = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(file))!;
            foreach (var (key, value) in strings)
            {
                if (Broken(language, key, value))
                    problems.Add(language + ": " + key + " => " + value);
            }
        }

        Assert.True(problems.Count == 0, "These translations look broken:\n" + string.Join("\n", problems));
    }

    [Theory]
    [InlineData("de", "Pick the folder", "W?hle den Ordner", true)]
    [InlineData("tr", "Find Bannerlord", "Bannerlord?u bul", true)]
    [InlineData("pt", "The tool remembers it.", "A ferramenta vai guard?-la.", true)]
    [InlineData("ru", "Find Bannerlord", "????? Bannerlord", true)]
    [InlineData("ar", "Find Bannerlord", "?????? ??? Bannerlord", true)]
    [InlineData("ru", "Steam was not found.", "Steam not found", true)]
    [InlineData("de", "Still stuck. Click Fix.", "Immer noch nicht? Klicke Fix.", false)]
    [InlineData("fr", "Still stuck. Click Fix.", "Toujours bloqué ? Clique Fix.", false)]
    [InlineData("ru", "Find Bannerlord", "Найти Bannerlord", false)]
    [InlineData("de", "Is it on?", "Ist es an?", false)]
    public void Broken_shapes_are_told_apart_from_real_question_marks(string language, string key, string value, bool broken)
    {
        Assert.Equal(broken, Broken(language, key, value));
    }

    private static bool Broken(string language, string key, string value)
    {
        if (value.Contains("??", StringComparison.Ordinal) && !key.Contains("??", StringComparison.Ordinal))
            return true;
        if (QuestionBetweenLetters(value) && !QuestionBetweenLetters(key))
            return true;
        // Arabic and Russian text always has letters outside ASCII; none at all means they were lost.
        // Names and log lines kept in English on purpose are the key itself.
        return language is "ar" or "ru" && value != key && key.Any(char.IsLetter) && !value.Any(c => c > 127 && char.IsLetter(c));
    }

    private static bool QuestionBetweenLetters(string text)
    {
        for (var i = 1; i < text.Length - 1; i++)
        {
            // pt "guard?-la" lost its letter before a hyphen too.
            var next = text[i + 1] == '-' && i + 2 < text.Length ? text[i + 2] : text[i + 1];
            if (text[i] == '?' && char.IsLetter(text[i - 1]) && char.IsLetter(next))
                return true;
        }

        return false;
    }
}
