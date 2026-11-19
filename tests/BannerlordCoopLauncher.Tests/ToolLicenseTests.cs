using BannerlordCoopLauncher.Core.Legal;
using BannerlordCoopLauncher.Core.Localization;

namespace BannerlordCoopLauncher.Tests;

// The tools are GPL 3 and never ask anyone to agree to it. Every tool shows the license, read only,
// from the License link in its sidebar.
public sealed class ToolLicenseTests
{
    private static readonly string[] Tools =
        ["BannerlordCoopLauncher", "CoodszHost", "TaomCoopHost", "TaomCoopLauncher", "AD1259Launcher", "BurningEmpiresHost", "BurningEmpiresLauncher", "RealmOfThronesHost", "RealmOfThronesLauncher"];

    private static string Normalize(string text) => text.Replace("\r\n", "\n").Trim();

    [Fact]
    public void The_tools_show_the_license_from_the_repository_root()
    {
        Assert.Contains("GNU GENERAL PUBLIC LICENSE", ToolLicense.Text);
        Assert.Contains("Version 3, 29 June 2007", ToolLicense.Text);
        Assert.Equal(Normalize(SourceFiles.Read("LICENSE")), Normalize(ToolLicense.Text));
    }

    [Fact]
    public void The_license_window_shows_the_gpl_and_then_the_third_party_notices()
    {
        Assert.False(string.IsNullOrWhiteSpace(ThirdPartyNotices.Text));
        Assert.Equal(ToolLicense.Text + "\r\n\r\n\r\n" + ThirdPartyNotices.Text, ToolLicense.WithNotices);

        var window = SourceFiles.Method(SourceFiles.Read("src/BannerlordCoopLauncher/UiLanguage.cs"), "public static void ShowLicense(");
        Assert.Contains("Text = BannerlordCoopLauncher.Core.Legal.ToolLicense.WithNotices,", window);
        Assert.Contains("IsReadOnly = true,", window);
        Assert.Contains("Content = Translator.T(\"Close\"),", window);
        Assert.DoesNotContain("Shutdown", window);
    }

    [Fact]
    public void No_tool_asks_to_agree_to_the_license_or_keeps_an_answer()
    {
        // The answer store and the first start gate are gone.
        foreach (var removed in new[] { "IsAccepted", "Accept", "Hash", "SettingsFolder", "SettingsPath" })
            Assert.Null(typeof(ToolLicense).GetMember(removed).FirstOrDefault());
        var ui = SourceFiles.Read("src/BannerlordCoopLauncher/UiLanguage.cs");
        foreach (var gone in new[] { "LicenseOnce", "AskLicense", "_licenseTool", "I agree" })
            Assert.DoesNotContain(gone, ui);
        foreach (var tool in Tools)
            Assert.DoesNotContain("LicenseOnce", SourceFiles.Read(Path.Combine("src", tool, "App.xaml.cs")));

        // Nothing in the tools reads or writes the old answer file.
        foreach (var file in Directory.EnumerateFiles(Path.Combine(SourceFiles.Root(), "src"), "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) || file.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar))
                continue;
            Assert.DoesNotContain("license-accepted", File.ReadAllText(file));
        }
    }

    [Fact]
    public void Every_tool_has_the_license_link_under_donate_and_in_the_more_menu()
    {
        foreach (var tool in Tools)
        {
            var xaml = SourceFiles.Read(Path.Combine("src", tool, "MainWindow.xaml"));
            var donate = xaml.IndexOf("x:Name=\"DonateSlot\"", StringComparison.Ordinal);
            var license = xaml.IndexOf("x:Name=\"LicenseSlot\"", StringComparison.Ordinal);
            Assert.True(donate > 0 && license > donate, tool + " has no License link under Donate.");

            var window = SourceFiles.Window(SourceFiles.Root(), tool);
            Assert.Contains("LicenseSlot.Content = Coodsz.Ui.UiLanguage.MakeLicenseButton();", window);
            Assert.Contains("Add(\"License\", \"" + ToolLicense.ButtonTip + "\",", SourceFiles.Method(window, "private void CompactMore_Click("));
        }
    }

    [Fact]
    public void The_license_window_is_translated_into_every_language()
    {
        string[] lines = [ToolLicense.Title, ToolLicense.ButtonTip, ToolLicense.EnglishOnly, "Close", "Closes this window."];
        foreach (var language in Translator.Languages.Where(language => language.Code != Translator.English))
        {
            var table = Translator.Entries(language.Code);
            Assert.All(lines, line => Assert.True(table.ContainsKey(line), language.Code + " has no translation for: " + line));
        }
    }
}
