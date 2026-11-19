using System.Globalization;
using System.Text.RegularExpressions;
using BannerlordCoopLauncher.Core.Localization;
using BannerlordCoopLauncher.Core.Updates;

namespace BannerlordCoopLauncher.Tests;

// Simplified Chinese, Korean and Japanese, added for every tool through the shared Core tables.
[Collection(TranslatorLanguage.Name)]
public class EastAsianLanguagesTests
{
    public static TheoryData<string> NewCodes => new() { "ja", "ko", "zh-CN" };

    [Fact]
    public void The_picker_lists_them_in_their_own_words()
    {
        var names = Translator.Languages.ToDictionary(language => language.Code, language => language.NativeName);
        Assert.Equal("日本語", names["ja"]);
        Assert.Equal("한국어", names["ko"]);
        Assert.Equal("简体中文", names["zh-CN"]);
        Assert.All(new[] { "ja", "ko", "zh-CN" }, code => Assert.False(Translator.Languages.Single(language => language.Code == code).RightToLeft));
    }

    [Theory]
    [InlineData("zh-CN", "zh-CN")]
    [InlineData("zh-Hans", "zh-CN")]
    [InlineData("zh-SG", "zh-CN")]
    [InlineData("zh-TW", "zh-CN")]
    [InlineData("ja-JP", "ja")]
    [InlineData("ko-KR", "ko")]
    [InlineData("de-DE", "de")]
    [InlineData("en-US", "en")]
    [InlineData("es-ES", "en")]
    public void A_windows_language_picks_the_matching_table(string culture, string expected)
    {
        Assert.Equal(expected, Translator.FromCulture(new CultureInfo(culture)));
    }

    [Theory]
    [MemberData(nameof(NewCodes))]
    public void Every_text_has_an_entry_with_the_same_placeholders_and_lines(string code)
    {
        var german = Translator.Entries("de");
        var russian = Translator.Entries("ru");
        var table = Translator.Entries(code);
        var problems = new List<string>();
        foreach (var (key, _) in german)
        {
            if (!table.TryGetValue(key, out var value))
            {
                problems.Add("missing: " + key);
                continue;
            }

            // A placeholder glued to an English word ("file{1}", "mod{1}") is an English plural ending like
            // "s" or "s need" where Russian drops it, and these languages have no plural ending: it must go.
            // Every other placeholder stays, once each.
            var ending = Regex.Matches(key, @"[A-Za-z](\{\d+\})").Select(m => m.Groups[1].Value)
                .Where(placeholder => !russian.GetValueOrDefault(key, key).Contains(placeholder)).ToHashSet();
            var expected = Regex.Matches(key, @"\{\d+\}").Select(m => m.Value).Where(placeholder => !ending.Contains(placeholder)).Order();
            if (!expected.SequenceEqual(Regex.Matches(value, @"\{\d+\}").Select(m => m.Value).Order()))
                problems.Add("placeholders: " + key);
            if (key.Count(c => c == '\n') != value.Count(c => c == '\n'))
                problems.Add("lines: " + key);
        }

        Assert.True(problems.Count == 0, code + ":\n" + string.Join("\n", problems.Take(40)));
    }

    [Theory]
    [MemberData(nameof(NewCodes))]
    public void The_text_is_really_in_that_language(string code)
    {
        // Hangul for Korean, kana for Japanese, Han without kana or Hangul for Chinese.
        var table = Translator.Entries(code);
        var translated = table.Count(pair => pair.Key != pair.Value);
        Assert.True(translated > table.Count * 9 / 10, code + " translated " + translated + " of " + table.Count);
        foreach (var (key, value) in table.Where(pair => pair.Key != pair.Value))
        {
            var hangul = value.Any(c => c is >= '가' and <= '힯');
            var kana = value.Any(c => c is >= '぀' and <= 'ヿ');
            if (code == "zh-CN")
                Assert.False(hangul || kana, "Korean or Japanese letters in Chinese: " + key);
            if (code == "ko")
                Assert.False(kana, "Japanese kana in Korean: " + key);
            if (code == "ja")
                Assert.False(hangul, "Korean letters in Japanese: " + key);
        }
    }

    [Fact]
    public void The_new_languages_line_is_translated_into_every_language()
    {
        foreach (var language in Translator.Languages.Where(language => language.Code != Translator.English))
            Assert.True(Translator.Entries(language.Code).ContainsKey(WhatsNew.MoreLanguages), language.Code);
    }

    [Fact]
    public void Sentences_put_together_keep_no_space_after_a_full_width_stop()
    {
        var before = Translator.Current;
        try
        {
            Translator.Use("ja");
            Assert.True(Translator.EastAsian);
            var table = Translator.Entries("ja");
            // Two plain one sentence texts whose Japanese ends with 。
            var pair = table.Where(entry => !entry.Key.Contains('{') && !entry.Key.Contains('\n') && entry.Key.EndsWith('.')
                    && entry.Key.Count(c => c == '.') == 1 && char.IsUpper(entry.Key[0]) && entry.Value.EndsWith('。'))
                .Take(2).ToList();
            Assert.Equal(2, pair.Count);
            var joined = Translator.T(pair[0].Key + " " + pair[1].Key);
            Assert.Equal(pair[0].Value + pair[1].Value, joined);
        }
        finally
        {
            Translator.Use(before);
        }

        Translator.Use("de");
        try
        {
            Assert.False(Translator.EastAsian);
        }
        finally
        {
            Translator.Use(before);
        }
    }
}
