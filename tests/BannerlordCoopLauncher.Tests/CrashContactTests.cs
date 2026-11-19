using BannerlordCoopLauncher.Core.Modules;
using BannerlordCoopLauncher.Core.Support;

namespace BannerlordCoopLauncher.Tests;

/// <summary>
/// The Your Discord name box of the crash report windows. Send needs a name (2 to 40 characters,
/// one line), Save a copy does not. The name goes only in the summary as contact, never in the zip,
/// and it is kept per PC in mod-reports.json so the box is filled in next time.
/// </summary>
public sealed class CrashContactTests : IDisposable
{
    // Invisible characters, built from their code points so the source stays plain ASCII.
    private static readonly string ZeroWidth = ((char)0x200B).ToString();
    private static readonly string RightToLeft = ((char)0x202E).ToString();
    private static readonly string Isolate = ((char)0x2066).ToString();

    private readonly string _directory =Path.Combine(Path.GetTempPath(), "coodsz-crash-contact-" + Guid.NewGuid().ToString("N"));

    public CrashContactTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        try { Directory.Delete(_directory, true); }
        catch (IOException) { }
    }

    private static CrashReportSummary Summary() => new()
    {
        Install = "0123456789abcdef0123456789abcdef",
        ToolVersion = "1.2.53",
        Files = { new CrashReportFile("game/Coop_client.log", 100) }
    };

    [Fact]
    public void The_summary_carries_contact_when_a_name_is_filled_in()
    {
        var summary = CrashContact.WithContact(Summary(), "pat.the_great");
        Assert.Equal("pat.the_great", summary.Contact);
        Assert.Contains("\"contact\": \"pat.the_great\"", CrashReports.SummaryJson(summary));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("x")]
    [InlineData("invisible")]
    public void The_summary_has_no_contact_when_the_box_is_empty_or_too_short(string? name)
    {
        if (name == "invisible")
            name = ZeroWidth + RightToLeft + "\t";
        var summary = CrashContact.WithContact(Summary(), name);
        Assert.Null(summary.Contact);
        Assert.DoesNotContain("contact", CrashReports.SummaryJson(summary));
        Assert.DoesNotContain("contact", CrashReports.SummaryJson(Summary()));
    }

    [Fact]
    public void The_name_is_trimmed_put_on_one_line_and_capped_at_40()
    {
        Assert.Equal("Pat", CrashContact.WithContact(Summary(), "   Pat \r\n ").Contact);
        Assert.Equal("Pat the Great", CrashContact.Clean("Pat\nthe\t\tGreat"));
        Assert.Equal("abcdef", CrashContact.Clean("ab" + RightToLeft + "cd" + Isolate + "ef" + ZeroWidth));
        Assert.Equal("a b", CrashContact.Clean("a\u0007b"));
        var capped = CrashContact.WithContact(Summary(), "  " + new string('x', 60) + "  ").Contact!;
        Assert.Equal(new string('x', CrashContact.MaxChars), capped);
        Assert.Equal(40, CrashContact.MaxChars);
        // A cut never splits an emoji in half.
        var emoji = CrashContact.Clean(new string('y', 39) + "\U0001F600\U0001F600");
        Assert.Equal(new string('y', 39), emoji);
        Assert.True(emoji.Length <= CrashContact.MaxChars);
    }

    [Fact]
    public void Send_is_blocked_when_the_box_is_empty_and_allowed_when_filled()
    {
        Assert.False(CrashContact.CanSend(null));
        Assert.False(CrashContact.CanSend(""));
        Assert.False(CrashContact.CanSend("   "));
        Assert.False(CrashContact.CanSend("x"));
        Assert.False(CrashContact.CanSend(" x \n"));
        Assert.True(CrashContact.CanSend("xy"));
        Assert.True(CrashContact.CanSend("  pat.the_great  "));
        Assert.True(CrashContact.CanSend(new string('z', 80)));

        // The window: Send waits for the name, the hint says why, Save a copy never asks for it.
        var window = File.ReadAllText(Path.Combine(RepoRoot(), "src", "BannerlordCoopLauncher", "CrashReportWindow.cs"));
        var ready = SourceFiles.Method(window, "private void UpdateSendReady(");
        Assert.Contains("CrashContact.CanSend(_contact?.Text)", ready);
        Assert.Contains("_send.IsEnabled = ready && !_sending;", ready);
        Assert.Contains("_sendHint.Visibility = ready ? Visibility.Collapsed : Visibility.Visible;", ready);
        var send = SourceFiles.Method(window, "private async Task SendAsync(");
        Assert.True(send.IndexOf("CrashContact.CanSend(", StringComparison.Ordinal) < send.IndexOf("CrashReports.SendAsync(", StringComparison.Ordinal));
        Assert.Contains("CrashContact.Save(_contact?.Text);", send);
        Assert.DoesNotContain("_send.IsEnabled = true;", send);
        Assert.Contains("CrashContact.WithContact(", SourceFiles.Method(window, "private CrashReportSummary SummaryOf("));
        Assert.DoesNotContain("CrashContact", SourceFiles.Method(window, "private async Task SaveCopyHereAsync("));
        Assert.DoesNotContain("CrashContact", SourceFiles.Method(window, "public static async Task SaveCopyAsync("));
        Assert.Contains("_stack.Children.Add(ContactSection());", SourceFiles.Method(window, "private void ShowConsent("));
        Assert.Contains("Text = CrashContact.Load(),", SourceFiles.Method(window, "private FrameworkElement ContactSection("));
        Assert.Equal("Enter your Discord name to send.", CrashContact.NeededText);
    }

    [Fact]
    public void The_name_is_kept_per_pc_and_prefills_the_box_next_time()
    {
        var path = Path.Combine(_directory, "mod-reports.json");
        Assert.Equal("", CrashContact.Load(path));

        CrashContact.Save("  Pat  ", path);
        Assert.Equal("Pat", CrashContact.Load(path));
        Assert.Equal("Pat", ModReports.LoadSettings(path).CrashContact);

        // A box too short to send never wipes the kept name.
        CrashContact.Save("", path);
        CrashContact.Save("x", path);
        Assert.Equal("Pat", CrashContact.Load(path));

        // The install id and the other per PC values stay as they were.
        var settings = ModReports.LoadSettings(path);
        var install = ModReports.InstallId(settings);
        settings.AskAfterGame = true;
        ModReports.SaveSettings(settings, path);
        CrashContact.Save("pat.the_great", path);
        var after = ModReports.LoadSettings(path);
        Assert.Equal(install, after.Install);
        Assert.True(after.AskAfterGame);
        Assert.Equal("pat.the_great", CrashContact.Remembered(after));

        // A hand edited file is cleaned before it fills the box.
        after.CrashContact = " Pat" + RightToLeft + "\n" + new string('q', 80);
        ModReports.SaveSettings(after, path);
        var prefill = CrashContact.Load(path);
        Assert.StartsWith("Pat ", prefill);
        Assert.Equal(CrashContact.MaxChars, prefill.Length);
    }

    [Fact]
    public void The_contact_never_goes_into_the_zip()
    {
        var window = File.ReadAllText(Path.Combine(RepoRoot(), "src", "BannerlordCoopLauncher", "CrashReportWindow.cs"));
        Assert.DoesNotContain("_contact", SourceFiles.Method(window, "private async Task<CrashReportPack> FinalPackAsync("));
        var reports = File.ReadAllText(Path.Combine(RepoRoot(), "src", "BannerlordCoopLauncher.Core", "Support", "CrashReports.cs"));
        // Packing (report.json and the zip) never reads the name; only WithContact sets it, at send time.
        Assert.DoesNotContain("CrashContact.", reports);
        Assert.DoesNotContain(".Contact =", reports);
        var extras = File.ReadAllText(Path.Combine(RepoRoot(), "src", "BannerlordCoopLauncher.Core", "Support", "ReportExtras.cs"));
        Assert.DoesNotContain("Contact", extras);
    }

    [Fact]
    public void The_new_texts_are_translated_in_every_language()
    {
        var folder = Path.Combine(RepoRoot(), "src", "BannerlordCoopLauncher.Core", "Localization", "Languages");
        foreach (var code in new[] { "ar", "de", "fr", "pt", "ru", "tr" })
        {
            var map = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(folder, code + ".json")))!;
            foreach (var text in new[] { CrashContact.Label, CrashContact.Note, CrashContact.NeededText })
            {
                Assert.True(map.TryGetValue(text, out var translated) && translated.Length > 0, code + ": " + text);
                Assert.DoesNotContain(((char)0xC3).ToString(), translated);
            }
        }
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "BannerlordCoopLauncher.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
