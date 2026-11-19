using System.ComponentModel;
using System.Diagnostics;
using BurningEmpires.Core;
using BurningEmpires.Ui;

namespace BannerlordCoopLauncher.Tests;

// A player report: Install or repair in the Burning Empires Client Launcher opened Edge with no
// clear reason. He had copied Burning Empires into Modules by hand and had no downloads left, so the
// picker asked for the main file and Cancel opened the Nexus page. Now the browser only opens after a
// Yes to a question that says why, Cancel stops, a copy by hand is asked only for the hotfix, a copy
// still running is waited for, and Steam pages never open (or crash the tool) with Steam closed.
public sealed class BeInstallNexusTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "coodsz-be-copy-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void A_copy_into_Modules_that_is_still_running_is_seen_by_the_made_date_and_old_files_are_not()
    {
        var now = DateTime.UtcNow;
        var file = Path.Combine(_root, "Modules", BeStack.BeModuleId, "ModuleData", "items.xml");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, "x");
        Assert.True(BeArchive.ChangedRecently(_root, now, TimeSpan.FromMinutes(1)));

        // A finished copy from hours ago: made and changed dates are old everywhere.
        Age(Path.Combine(_root, "Modules"), now.AddHours(-3));
        Assert.False(BeArchive.ChangedRecently(_root, now, TimeSpan.FromMinutes(1)));

        // A copy by hand keeps the file's own old changed date, but its made date is now.
        var copied = Path.Combine(_root, "Modules", BeStack.ElephantsModuleId, "SubModule.xml");
        Directory.CreateDirectory(Path.GetDirectoryName(copied)!);
        File.WriteAllText(copied, "x");
        File.SetLastWriteTimeUtc(copied, now.AddDays(-30));
        Directory.SetCreationTimeUtc(Path.GetDirectoryName(copied)!, now.AddHours(-3));
        Directory.SetLastWriteTimeUtc(Path.GetDirectoryName(copied)!, now.AddHours(-3));
        Assert.True(BeArchive.ChangedRecently(_root, now, TimeSpan.FromMinutes(1)));

        // A date far ahead (a wrong clock in an archive) is not a copy that is running.
        File.SetCreationTimeUtc(copied, now.AddDays(2));
        File.SetLastWriteTimeUtc(copied, now.AddDays(2));
        Assert.False(BeArchive.ChangedRecently(_root, now, TimeSpan.FromMinutes(1)));
    }

    [Fact]
    public void No_Burning_Empires_folders_is_not_a_copy_that_is_running()
    {
        Directory.CreateDirectory(Path.Combine(_root, "Modules", "Native"));
        Assert.False(BeArchive.ChangedRecently(_root, DateTime.UtcNow, TimeSpan.FromMinutes(1)));
    }

    [Fact]
    public async Task A_PC_with_no_app_for_steam_links_gets_one_log_line_instead_of_a_crash()
    {
        var lines = new List<string>();
        var tried = new List<string>();
        var opened = await BeSteamPage.OpenAsync(["2859188632", "2859232415"], lines.Add, info =>
        {
            tried.Add(info.FileName);
            throw new Win32Exception("Application not found");
        });

        Assert.False(opened);
        Assert.Equal(new[] { BeSteamPage.CouldNotOpen }, lines);
        Assert.Equal(new[] { "steam://url/CommunityFilePage/2859188632" }, tried);
    }

    [Fact]
    public async Task Each_Workshop_page_opens_through_Steam()
    {
        var tried = new List<ProcessStartInfo>();
        Assert.True(await BeSteamPage.OpenAsync(["2859188632", "2859232415"], _ => { }, tried.Add));
        Assert.Equal(new[] { "steam://url/CommunityFilePage/2859188632", "steam://url/CommunityFilePage/2859232415" }, tried.Select(info => info.FileName));
        Assert.All(tried, info => Assert.True(info.UseShellExecute));
    }

    [Fact]
    public void A_copy_by_hand_is_asked_only_for_the_hotfix_and_the_Nexus_question_says_why()
    {
        var hotfix = BeUiText.PickLine("BurningEmpires.dll is not the 2.0.2 one.", hotfixOnly: true, hotfixFound: false, "2.0.2");
        Assert.Contains("only the 2.0.2 hotfix is needed", hotfix);
        Assert.DoesNotContain("main file", hotfix);
        Assert.Equal("Pick the Burning Empires hotfix", BeUiText.PickTitle(hotfixOnly: true));
        Assert.Contains("Pick the Burning Empires main file", BeUiText.PickLine("Burning Empires is missing.", hotfixOnly: false, hotfixFound: false, "2.0.2"));
        Assert.Contains("The Burning Empires main file was not found.", BeUiText.PickLine("Burning Empires is missing.", hotfixOnly: false, hotfixFound: true, "2.0.2"));

        var question = BeUiText.NexusQuestion("Burning Empires is missing.", hotfixOnly: false, "2.0.2");
        Assert.StartsWith("Burning Empires is missing.", question);
        Assert.EndsWith("?", question);
        Assert.Contains("the 2.0.2 hotfix?", BeUiText.NexusQuestion("x", hotfixOnly: true, "2.0.2"));
        Assert.DoesNotContain("main file", BeUiText.NexusQuestion("x", hotfixOnly: true, "2.0.2"));
        Assert.Contains("https://www.nexusmods.com/", BeUiText.NexusNotOpened("https://www.nexusmods.com/mountandblade2bannerlord/mods/7750", hotfixOnly: false, "2.0.2"));
        Assert.StartsWith("Burning Empires was not installed.", BeUiText.PickCancelled(hotfixOnly: true, "2.0.2"));
    }

    [Theory]
    [InlineData("BurningEmpiresLauncher", "private async void InstallOrRepair_Click(")]
    [InlineData("BurningEmpiresHost", "private async void InstallOrRepairBe_Click(")]
    public void Install_or_repair_waits_for_a_copy_asks_before_the_browser_and_stops_on_Cancel(string project, string signature)
    {
        var window = SourceFiles.Window(SourceFiles.Root(), project);
        var install = SourceFiles.Method(window, signature);

        // The copy check comes before the picker and anything else is asked.
        var copying = install.IndexOf("BeUiText.CopyStillRunning", StringComparison.Ordinal);
        Assert.True(copying > 0);
        Assert.True(copying < install.IndexOf("PickBeDownload(contentProblem, archives, hotfixOnly)", StringComparison.Ordinal));
        Assert.Contains("BeArchive.ChangedRecently(gameRoot, DateTime.UtcNow, TimeSpan.FromMinutes(1))", install);
        Assert.Contains("!unpackedJustNow", install);
        Assert.Contains("!BeArchive.NeedsMainFile(gameRoot)", install);

        // The browser only opens through the question, never straight from the click.
        Assert.Contains("AskOpenNexus(nexusReason, nexusHotfixOnly)", install);
        Assert.Contains("nexusHotfixOnly = hotfixOnly || setProblem == BeArchive.HotfixMissing;", install);
        Assert.Contains("failure = ex.DownloadHelps ? ex.Message : null;", SourceFiles.Method(window, "private bool UnpackBe("));
        Assert.DoesNotContain("OpenNexusPage(", install);
        Assert.Contains("BeUiText.PickCancelled(hotfixOnly, BeStack.BeVersionText)", install);
        var ask = SourceFiles.Method(window, "private void AskOpenNexus(");
        Assert.Contains("BeUiText.NexusQuestion(reason, hotfixOnly, BeStack.BeVersionText)", ask);
        Assert.Contains("MessageBoxResult.Yes", ask);

        // The tool's own unpack is stamped, so its new files never read as a copy by hand.
        Assert.Contains("_unpackedAtUtc = DateTime.UtcNow;", SourceFiles.Method(window, "private bool UnpackBe("));
    }

    [Theory]
    [InlineData("BurningEmpiresLauncher")]
    [InlineData("BurningEmpiresHost")]
    public void Steam_pages_only_open_through_the_guarded_helper_and_never_with_Steam_closed(string project)
    {
        var window = SourceFiles.Window(SourceFiles.Root(), project);
        Assert.DoesNotContain("steam://", window);
        Assert.Contains("BeSteamPage.SteamRunning()", window);
        Assert.Contains("await BeSteamPage.OpenAsync(", window);
    }

    [Fact]
    public void The_client_asks_Steam_for_mods_only_when_Steam_runs()
    {
        var window = SourceFiles.Window(SourceFiles.Root(), "BurningEmpiresLauncher");
        foreach (var signature in new[] { "private async Task<WorkshopSubscribeResult?> SubscribeWorkshopAsync(", "private async Task<string?> SubscribeCoopAsync(" })
        {
            var method = SourceFiles.Method(window, signature);
            var check = method.IndexOf("BeSteamPage.SteamRunning()", StringComparison.Ordinal);
            Assert.True(check > 0, signature);
            Assert.True(check < method.IndexOf("Asking Steam to subscribe", StringComparison.Ordinal), signature);
        }
    }

    private static void Age(string folder, DateTime when)
    {
        foreach (var path in Directory.EnumerateFileSystemEntries(folder, "*", SearchOption.AllDirectories).Append(folder))
        {
            if (Directory.Exists(path))
            {
                Directory.SetCreationTimeUtc(path, when);
                Directory.SetLastWriteTimeUtc(path, when);
            }
            else
            {
                File.SetCreationTimeUtc(path, when);
                File.SetLastWriteTimeUtc(path, when);
            }
        }
    }
}

// Every new Install or repair message reaches the player translated, including the two line reason
// in the Nexus question, which the translator takes one line at a time.
[Collection(TranslatorLanguage.Name)]
public sealed class BeInstallNexusTranslationTests
{
    [Theory]
    [InlineData("de")]
    [InlineData("ar")]
    [InlineData("fr")]
    [InlineData("pt")]
    [InlineData("ru")]
    [InlineData("tr")]
    public void The_new_install_and_Steam_messages_are_translated(string language)
    {
        var before = BannerlordCoopLauncher.Core.Localization.Translator.Current;
        try
        {
            BannerlordCoopLauncher.Core.Localization.Translator.Use(language);
            var v = BeStack.BeVersionText;
            var texts = new[]
            {
                BeUiText.NexusQuestion("Burning Empires is missing.\n" + BeArchive.HotfixMissing, hotfixOnly: false, v),
                BeUiText.NexusQuestion("Burning Empires is missing.\n" + BeArchive.MainFileMissing, hotfixOnly: true, v),
                BeUiText.PickLine("Burning Empires is missing.", hotfixOnly: true, hotfixFound: false, v),
                BeUiText.PickLine("Burning Empires is missing.", hotfixOnly: false, hotfixFound: true, v),
                BeUiText.PickLine("Burning Empires is missing.", hotfixOnly: false, hotfixFound: false, v),
                BeUiText.PickTitle(hotfixOnly: true),
                BeUiText.PickCancelled(hotfixOnly: true, v),
                BeUiText.PickCancelled(hotfixOnly: false, v),
                BeUiText.NexusNotOpened(BeStack.BeNexusPage, hotfixOnly: true, v),
                BeUiText.NexusNotOpened(BeStack.BeNexusPage, hotfixOnly: false, v),
                BeUiText.NexusOpened(hotfixOnly: true, v),
                BeUiText.NexusOpened(hotfixOnly: false, v),
                BeUiText.CopyStillRunning,
                "Steam is needed for Harmony, ButterLib. " + BeSteamPage.SteamNotRunning,
                "Steam is needed for Bannerlord Coop. " + BeSteamPage.SteamNotRunning,
                BeSteamPage.CouldNotOpen,
            };
            var english = new[] { "Open Burning Empires", "in your browser", "was not found", "Pick the", "was not installed", "is not running", "Could not open", "in the last minute", "is missing", "Download the", "only the", "is needed for", "Opened Burning" };
            foreach (var text in texts)
            {
                var shown = BannerlordCoopLauncher.Core.Localization.Translator.T(text);
                foreach (var phrase in english)
                    Assert.False(shown.Contains(phrase, StringComparison.Ordinal), language + ": \"" + phrase + "\" left in: " + shown);
                Assert.Contains(v, BannerlordCoopLauncher.Core.Localization.Translator.T(BeUiText.NexusOpened(hotfixOnly: true, v)));
            }
        }
        finally
        {
            BannerlordCoopLauncher.Core.Localization.Translator.Use(before);
        }
    }

    // A one line template still takes a value with a line break in it (an exception message, a
    // list) when going line by line finds nothing, so no message comes out worse than before.
    [Fact]
    public void A_value_with_a_line_break_inside_a_one_line_template_is_still_translated()
    {
        var before = BannerlordCoopLauncher.Core.Localization.Translator.Current;
        try
        {
            BannerlordCoopLauncher.Core.Localization.Translator.Use("de");
            Assert.Equal("Spieler laden A\nB, der Server aber nicht.", BannerlordCoopLauncher.Core.Localization.Translator.T("Players load A\nB but the server does not."));
            // One line of the value is a known word on its own ("Start"); the whole template still wins
            // over a line by line result that leaves the other lines in English.
            Assert.StartsWith("Spieler laden A\n", BannerlordCoopLauncher.Core.Localization.Translator.T("Players load A\nStart\nB but the server does not."));
            Assert.EndsWith(", der Server aber nicht.", BannerlordCoopLauncher.Core.Localization.Translator.T("Players load A\nStart\nB but the server does not."));
        }
        finally
        {
            BannerlordCoopLauncher.Core.Localization.Translator.Use(before);
        }
    }
}
