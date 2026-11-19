using System.Text.RegularExpressions;
using BannerlordCoopLauncher.Core.Configuration;
using BannerlordCoopLauncher.Core.Keep;
using BannerlordCoopLauncher.Core.Localization;

namespace BannerlordCoopLauncher.Tests;

// What a Turkish player (Rendegarr) found in Coodsz Host: dropdowns that showed True, False and
// VeryEasy in English, the Community filter chips in English, and the Cheats page Who? list that
// showed Player2863 instead of a name.
public class TranslationFixesTests : IDisposable
{
    private static readonly string[] Codes = ["ar", "de", "fr", "pt", "ru", "tr"];
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "coodsz-trfix-" + Guid.NewGuid().ToString("N"));

    public TranslationFixesTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "BannerlordCoopLauncher.sln")))
            dir = Path.GetDirectoryName(dir);
        return dir ?? throw new InvalidOperationException("Repo root not found.");
    }

    // --- 1. dropdown values ---------------------------------------------------

    [Fact]
    public void Dropdown_values_show_as_labels_and_the_raw_value_is_not_changed()
    {
        Assert.Equal("On", ModSettingText.ChoiceLabel("True"));
        Assert.Equal("Off", ModSettingText.ChoiceLabel("False"));
        Assert.Equal("Very easy", ModSettingText.ChoiceLabel("VeryEasy"));
        Assert.Equal("Easy", ModSettingText.ChoiceLabel("Easy"));
        Assert.Equal("Realistic", ModSettingText.ChoiceLabel("Realistic"));
        Assert.Equal("One day max", ModSettingText.ChoiceLabel("One day max"));
        Assert.Equal("Never expire", ModSettingText.ChoiceLabel("NeverExpire"));
        Assert.Equal("Always retry", ModSettingText.ChoiceLabel("AlwaysRetry"));
        Assert.Equal("Vanilla", ModSettingText.ChoiceLabel("Vanilla"));
        Assert.Equal("", ModSettingText.ChoiceLabel(null));

        var tr = Translator.Entries("tr");
        string T(string english) => tr.TryGetValue(english, out var shown) ? shown : english;
        Assert.Equal("Açık", T(ModSettingText.ChoiceLabel("True")));
        Assert.Equal("Kapalı", T(ModSettingText.ChoiceLabel("False")));
        Assert.Equal("Çok kolay", T(ModSettingText.ChoiceLabel("VeryEasy")));
        Assert.Equal("Kolay", T(ModSettingText.ChoiceLabel("Easy")));
        Assert.Equal("Gerçekçi", T(ModSettingText.ChoiceLabel("Realistic")));
        Assert.Equal("En fazla bir gün", T(ModSettingText.ChoiceLabel("One day max")));
    }

    [Fact]
    public void Every_settings_choice_has_a_label_in_every_language()
    {
        var raw = ModSettingsCatalog.All.Where(d => d.Choices != null).SelectMany(d => d.Choices!)
            .Concat(["True", "False", "One day max"]).Distinct().ToList();
        foreach (var code in Codes)
        {
            var table = Translator.Entries(code);
            foreach (var value in raw)
                Assert.True(table.ContainsKey(ModSettingText.ChoiceLabel(value)), code + " has no label for " + value);
        }
    }

    [Fact]
    public void Settings_boxes_keep_the_raw_values_so_the_files_get_True_False_and_VeryEasy()
    {
        var root = RepoRoot();
        var window = SourceFiles.Window(root, "CoodszHost");
        // The items stay the values the code writes; only the screen shows labels.
        var fill = SourceFiles.Method(window, "private void FillDifficulties()");
        Assert.Contains("box.Items.Add(\"VeryEasy\");", fill);
        Assert.Contains("box.Items.Add(\"True\");", fill);
        Assert.Contains("new[] { \"False\", \"One day max\", \"True\" }", fill);
        Assert.Contains("new[] { \"Vanilla\", \"NeverExpire\", \"AlwaysRetry\" }", fill);
        Assert.Contains("SettingChoiceLabel.SetChoiceLabels(box, true);", fill);
        Assert.Contains("private const string TrueText = \"True\";", window);
        var read = SourceFiles.Method(window, "private object SettingFromCombo(ComboBox box, string id)");
        Assert.Contains("item == TrueText", read);
        Assert.Contains("\"One day max\" => \"OneDayMax\"", read);
        Assert.Contains("\"True\" => \"Enabled\"", read);
        Assert.Contains("SettingChoiceLabel.SetChoiceLabels(box, true);", SourceFiles.Method(window, "private UIElement MoreSettingRow(ModSettingDefinition setting)"));

        // One converter in the shared box style, for the list and the closed box.
        var xaml = File.ReadAllText(Path.Combine(root, "src", "CoodszHost", "MainWindow.xaml"));
        var style = xaml[xaml.IndexOf("<Style x:Key=\"DifficultyBox\"", StringComparison.Ordinal)..];
        style = style[..style.IndexOf("</Style>", StringComparison.Ordinal)];
        Assert.Equal(2, Regex.Matches(style, "Converter=\"\\{StaticResource ChoiceLabel\\}\"").Count);
        Assert.Contains("<local:SettingChoiceLabel x:Key=\"ChoiceLabel\"/>", xaml);
    }

    [Fact]
    public void Notes_use_the_same_On_and_Off_words_as_the_dropdowns_in_every_language()
    {
        var root = RepoRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "CoodszHost", "MainWindow.xaml"));
        var texts = Regex.Matches(xaml, "(?:Text|ToolTip)=\"([^\"]*)\"").Select(m => System.Net.WebUtility.HtmlDecode(m.Groups[1].Value)).ToList();
        Assert.DoesNotContain(texts, t => Regex.IsMatch(t, @"\b(True|False):") || Regex.IsMatch(t, @"^(True|False) (lets|keeps|picks|pauses)"));
        var notes = texts.Where(t => t.StartsWith("On: ", StringComparison.Ordinal) || t.StartsWith("Off: ", StringComparison.Ordinal)).Distinct().ToList();
        Assert.True(notes.Count >= 20);
        foreach (var code in Codes)
        {
            var table = Translator.Entries(code);
            foreach (var note in notes)
            {
                var word = table[note.StartsWith("On", StringComparison.Ordinal) ? "On" : "Off"];
                Assert.True(table.TryGetValue(note, out var shown) && shown.StartsWith(word, StringComparison.Ordinal), code + ": " + note);
                Assert.DoesNotContain("True", shown);
                Assert.DoesNotContain("False", shown);
            }
        }

        var turkish = Translator.Entries("tr");
        Assert.StartsWith("Açık: ", turkish["On: clients can add troops, gear, and teleport. "]);
        Assert.StartsWith("Kapalı: ", turkish["Off: those cheats are blocked."]);
    }

    [Fact]
    public void Number_settings_under_Other_say_what_higher_and_lower_do()
    {
        var xaml = File.ReadAllText(Path.Combine(RepoRoot(), "src", "CoodszHost", "MainWindow.xaml"));
        foreach (var box in new[] { "AiWindowBox", "WandererBox", "ClanTierBox", "SmithBox", "LooterCountBox", "LooterSizeBox" })
        {
            var line = xaml.Split('\n').Single(l => l.Contains("x:Name=\"" + box + "\"", StringComparison.Ordinal));
            Assert.Contains("ToolTip=", line);
            Assert.Matches("(Higher|Lower) ", line);
            Assert.Contains("From ", line);
        }
    }

    // --- 2. Modules filter chips -----------------------------------------------

    [Fact]
    public void Community_filter_chips_are_translated_in_every_language()
    {
        foreach (var code in Codes)
        {
            var table = Translator.Entries(code);
            foreach (var kind in ModFilter.Chips)
            {
                var title = ModFilter.Title(kind);
                Assert.True(table.TryGetValue(title, out var shown) && shown != title || title == "All", code + ": " + title);
            }
        }

        var chips = Translator.Entries("tr");
        Assert.Equal("Topluluk: çalışıyor", chips[ModFilter.Title(ModFilterKind.Works)]);
        Assert.Equal("Topluluk: bozuk", chips[ModFilter.Title(ModFilterKind.Broken)]);
        Assert.Equal("Topluluk: bilinmiyor", chips[ModFilter.Title(ModFilterKind.Unknown)]);
    }

    // --- 3. Turkish wording --------------------------------------------------

    [Fact]
    public void Turkish_names_the_mods_page_and_its_buttons_plainly()
    {
        var tr = Translator.Entries("tr");
        Assert.Equal("Modlar", tr["Modules"]);
        Assert.Equal("Modları kaydet", tr["Save module config"]);
        Assert.Equal("Otomatik mod sırası", tr["Auto dependency order"]);
        Assert.DoesNotContain(tr.Values, v => v.Contains("Modül ayarlarını kaydet", StringComparison.Ordinal) || v.Contains("bağımlılık sırası", StringComparison.Ordinal));
        foreach (var code in Codes)
            Assert.NotEqual(Translator.Entries(code)["Save module config"], Translator.Entries(code)["Modules"]);
    }

    // --- 4. names in the Who? list -----------------------------------------------

    private string SaveJson()
    {
        var json = Path.Combine(_dir, "camp.json");
        File.WriteAllText(json, """
            { "UniqueGameId": "camp", "Players": [
              { "PlatformName": "Rowan", "ControllerId": "76561198000000013", "HeroId": "Hero_Player", "MobilePartyId": "MobileParty_Player", "ClanId": "Clan_Player" },
              { "PlatformName": "Ash Tree", "ControllerId": "76561198000000011", "HeroId": "Hero_Player2863", "MobilePartyId": "MobileParty_Player1222", "ClanId": "Clan_Player12" },
              { "ControllerId": "76561198000000012", "HeroId": "Hero_Player3323", "MobilePartyId": "MobileParty_Player58402", "ClanId": "Clan_Player103" } ] }
            """);
        return json;
    }

    [Fact]
    public void Stopped_server_shows_the_Steam_names_from_the_save_with_the_hero_id_below()
    {
        var session = KeepRoster.ReadSession(SaveJson());
        Assert.Equal("Rowan", session[0].PlatformName);
        var rows = KeepRoster.Merge([], session, []);
        Assert.Equal(["Ash Tree", "Player3323", "Rowan"], rows.Select(r => r.Name));
        var rende = rows.Single(r => r.HeroId == "Player");
        Assert.Equal("Rowan", rende.SteamName);
        Assert.Equal("Player", rende.Detail(streamer: false));
        // No name at all: the id is the name and is not repeated below.
        Assert.Equal("", rows.Single(r => r.HeroId == "Player3323").Detail(false));
    }

    [Fact]
    public void Running_server_shows_hero_names_with_the_Steam_name_and_id_below_and_remembers_them()
    {
        var session = KeepRoster.ReadSession(SaveJson());
        var heroes = new[]
        {
            new HeroSummary("Player", "Kaan", "Clan_Player", "Kaan's clan", "", 8400, 72, 100, 10, "p1", "", true),
            new HeroSummary("Player2863", "Lucon", "Clan_Player12", "Osticos", "Northern Empire", 160000, 100, 112, 27, "p2", "Argoron", true),
        };
        var rows = KeepRoster.Merge([new LivePlayerInfo("Ash Tree", "")], session, heroes);
        var lucon = rows.Single(r => r.HeroId == "Player2863");
        Assert.Equal("Lucon", lucon.Name);
        Assert.True(lucon.Online);
        Assert.Equal("Ash Tree · Player2863", lucon.Detail(streamer: false));
        Assert.Equal("Player2863", lucon.Detail(streamer: true));
        Assert.Equal("Rowan · Player", rows.Single(r => r.HeroId == "Player").Detail(false));

        var memory = new HeroNameMemory(Path.Combine(_dir, "hero-names.json"));
        memory.Remember("camp", rows.Where(r => r.Hero != null).Select(r => r.Hero!));
        var known = new HeroNameMemory(Path.Combine(_dir, "hero-names.json")).For("camp");
        Assert.Equal("Lucon", known["Player2863"]);
        Assert.Empty(memory.For("other"));

        // The server stopped: the names from the last run come back, the Steam name stays below.
        var stopped = KeepRoster.Merge([], session, [], known);
        Assert.Equal("Lucon", stopped.Single(r => r.HeroId == "Player2863").Name);
        Assert.Equal("Kaan", stopped.Single(r => r.HeroId == "Player").Name);
        Assert.Equal("Rowan · Player", stopped.Single(r => r.HeroId == "Player").Detail(false));
    }

    [Fact]
    public void Streamer_mode_numbers_the_names_and_hides_the_Steam_name()
    {
        var rows = KeepRoster.Merge([], KeepRoster.ReadSession(SaveJson()), []);
        var alias = StreamerNames.Alias(rows[0].Name);
        Assert.Matches("^Player [0-9]+$", alias);
        Assert.Equal(alias, StreamerNames.Alias(rows[0].Name));
        Assert.DoesNotContain("Rowan", rows.Single(r => r.HeroId == "Player").Detail(streamer: true));
        Assert.Contains("Binding Detail", File.ReadAllText(Path.Combine(RepoRoot(), "src", "CoodszHost", "Keep", "CheatsPage.xaml")));
    }
}
