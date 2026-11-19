using BannerlordCoopLauncher.Core.Support;

namespace BannerlordCoopLauncher.Tests;

// The Donate button in every tool's header opens Coodsz's Linktree, which lists his Patreon and PayPal.
public sealed class DonateTests
{
    [Fact]
    public void The_donate_link_is_coodszs_linktree()
    {
        Assert.True(Uri.TryCreate(Donate.Url, UriKind.Absolute, out var page));
        Assert.Equal(Uri.UriSchemeHttps, page!.Scheme);
        Assert.Equal("https://linktr.ee/Coodsz", Donate.Url);
    }

    // Coodsz: Buy Me a Coffee goes under the Linktree, on its own line.
    [Fact]
    public void The_menu_lists_the_linktree_first_and_buy_me_a_coffee_under_it()
    {
        Assert.Equal(2, Donate.Links.Count);
        Assert.Equal(Donate.Url, Donate.Links[0].Url);
        Assert.StartsWith("Linktree", Donate.Links[0].Name);
        Assert.Equal(("Buy Me a Coffee", "https://buymeacoffee.com/coodsz"), Donate.Links[1]);
        Assert.All(Donate.Links, link => Assert.Equal(Uri.UriSchemeHttps, new Uri(link.Url).Scheme));
        Assert.Contains(Donate.BuyMeACoffeeUrl, Donate.OpenProblemText(Donate.BuyMeACoffeeUrl));
    }

    // Every Server Tool and Client Launcher shows it right after What's new.
    [Theory]
    [InlineData("BannerlordCoopLauncher")]
    [InlineData("TaomCoopLauncher")]
    [InlineData("AD1259Launcher")]
    [InlineData("BurningEmpiresLauncher")]
    [InlineData("CoodszHost")]
    [InlineData("TaomCoopHost")]
    [InlineData("BurningEmpiresHost")]
    public void Every_tool_shows_the_donate_button_after_whats_new(string project)
    {
        var xaml = SourceFiles.Read(Path.Combine("src", project, "MainWindow.xaml"));
        var whatsNew = xaml.IndexOf("x:Name=\"WhatsNewSlot\"", StringComparison.Ordinal);
        var donate = xaml.IndexOf("x:Name=\"DonateSlot\"", StringComparison.Ordinal);
        Assert.True(whatsNew >= 0 && donate > whatsNew, project + " has no DonateSlot after WhatsNewSlot");
        Assert.Contains("DonateSlot.Content = Coodsz.Ui.UiLanguage.MakeDonateButton();", SourceFiles.Window(SourceFiles.Root(), project));
    }

    [Fact]
    public void The_donate_text_is_in_every_language()
    {
        foreach (var language in new[] { "ar", "de", "fr", "pt", "ru", "tr" })
        {
            var json = SourceFiles.Read(Path.Combine("src", "BannerlordCoopLauncher.Core", "Localization", "Languages", language + ".json"));
            foreach (var key in new[] { Donate.Title, Donate.ButtonTip, Donate.OpenProblem })
                Assert.True(json.Contains("\"" + key + "\":", StringComparison.Ordinal), language + " is missing " + key);
        }
    }
}

// The box when the browser won't open is all in the picked language. Run through Message, the wide
// "Could not open {0}. {1}" template took the whole text and left it half English.
[Collection(TranslatorLanguage.Name)]
public sealed class DonateLanguageTests
{
    [Fact]
    public void The_browser_problem_is_shown_whole_in_the_picked_language()
    {
        var before = BannerlordCoopLauncher.Core.Localization.Translator.Current;
        try
        {
            BannerlordCoopLauncher.Core.Localization.Translator.Use("de");
            Assert.Equal("Dein Browser konnte nicht geöffnet werden. Hier ist der Link:\nhttps://linktr.ee/Coodsz", Donate.OpenProblemText());
        }
        finally
        {
            BannerlordCoopLauncher.Core.Localization.Translator.Use(before);
        }

        var button = SourceFiles.Read(Path.Combine("src", "BannerlordCoopLauncher", "UiLanguage.cs"));
        Assert.DoesNotContain("Message(Donate.", button);
        Assert.Contains("Donate.OpenProblemText(url)", button);
    }
}
