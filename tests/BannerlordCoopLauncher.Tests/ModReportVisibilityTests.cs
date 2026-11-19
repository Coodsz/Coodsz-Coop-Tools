using BannerlordCoopLauncher.Core.Modules;
using BannerlordCoopLauncher.Core.Steam;

namespace BannerlordCoopLauncher.Tests;

/// <summary>
/// Mod reports easier to find: Report a mod always answers (a message when there is nothing to rate),
/// the calm Played with mods? line under the title, the Report link on each mod row, one click Send
/// with what is sent in view, and the Coop version read even when the setup has Coop off.
/// </summary>
[Collection(ModReportsSession.Name)]
public sealed class ModReportVisibilityTests : IDisposable
{
    private const string TestEndpoint = "https://reports.example/report";
    private static readonly string[] Clients = ["BannerlordCoopLauncher", "TaomCoopLauncher", "AD1259Launcher", "BurningEmpiresLauncher"];
    private const string GameRoot = @"C:\Games\Bannerlord";
    private const string WorkshopRoot = @"C:\steam\steamapps\workshop\content\261550";
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "coodsz-report-hint-" + Guid.NewGuid().ToString("N"));

    public ModReportVisibilityTests() => Directory.CreateDirectory(_directory);

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void Nothing_to_rate_gives_a_friendly_message_and_mods_open_the_window()
    {
        Assert.Equal("No extra mods in this setup to rate. Add a mod first, then report how it worked.", ModReports.NothingToRate(0));
        Assert.Null(ModReports.NothingToRate(1));
        Assert.Null(ModReports.NothingToRate(12));

        // Report a mod and Mod crashed? Report it both go through the one place that shows the message.
        var window = SourceFiles.Read(Path.Combine("src", "BannerlordCoopLauncher", "ModReportWindow.cs"));
        var open = SourceFiles.Method(window, "private void Open(ModReportRequest request)");
        Assert.Contains("ModReports.NothingToRate(request.Rate.Count)", open);
        Assert.Contains("UiLanguage.Message(nothing,", open);
        Assert.Contains("public void OpenCurrent() => Open(_current());", window);
        Assert.Contains("public void OpenCrash() => Open(LastCrash ?? _current().AsCrash(null));", window);
    }

    [Fact]
    public void The_hint_shows_until_a_report_is_sent_or_it_is_closed()
    {
        var settings = new ModReportSettings();
        Assert.True(ModReports.ShowsHint(settings, TestEndpoint));
        Assert.False(ModReports.ShowsHint(settings, ""));

        settings.Sent.Add(new ModReportItem { Id = "SomeMod", Version = "v1.0", Result = ModReportItem.Works });
        Assert.False(ModReports.ShowsHint(settings, TestEndpoint));

        var closed = new ModReportSettings { HintClosed = true };
        Assert.False(ModReports.ShowsHint(closed, TestEndpoint));
    }

    [Fact]
    public void Closing_the_hint_is_kept_and_an_older_file_still_shows_it()
    {
        var path = Path.Combine(_directory, "mod-reports.json");
        File.WriteAllText(path, """{ "kind": "CoodszModReportSettings", "previewSeen": true, "introSeen": true }""");
        var old = ModReports.LoadSettings(path);
        Assert.False(old.HintClosed);
        Assert.True(ModReports.ShowsHint(old, TestEndpoint));

        old.HintClosed = true;
        Assert.True(ModReports.SaveSettings(old, path));
        Assert.True(ModReports.LoadSettings(path).HintClosed);
        Assert.False(ModReports.ShowsHint(ModReports.LoadSettings(path), TestEndpoint));
    }

    [Fact]
    public void Every_client_shows_the_hint_and_a_report_link_on_each_mod_row()
    {
        foreach (var client in Clients)
        {
            var xaml = SourceFiles.Read(Path.Combine("src", client, "MainWindow.xaml"));
            Assert.Contains("x:Name=\"ReportHintSlot\"", xaml);
            var setUp = SourceFiles.Method(SourceFiles.Window(Root(), client), "private void SetUpModReports(");
            Assert.Contains("ReportHintSlot.Content = Coodsz.Ui.ModReportUi.Hint(this, _reportBar.OpenCurrent);", setUp);
        }

        var window = SourceFiles.Read(Path.Combine("src", "BannerlordCoopLauncher", "ModReportWindow.cs"));
        Assert.Contains("public const string HintText = \"Played with mods? Tell others if they worked.\";", window);
        var hint = SourceFiles.Method(window, "public static FrameworkElement? Hint(");
        Assert.Contains("ModReports.ShowsHint(ModReports.LoadSettings())", hint);
        Assert.Contains("settings.HintClosed = true;", hint);
        Assert.Contains("ModReportWindow.ReportSent += Gone;", hint);
        Assert.Contains("ReportSent?.Invoke();", SourceFiles.Method(window, "private async Task SendAsync("));

        // Coop and Friends binds the link in its mod rows; the TAOM and AD1259 clients build it per row.
        var friends = SourceFiles.Read(Path.Combine("src", "BannerlordCoopLauncher", "MainWindow.xaml"));
        Assert.Contains("AutomationProperties.AutomationId=\"RowReportLink\"", friends);
        Assert.Contains("<TextBlock Text=\"Report\"", friends);
        Assert.Contains("Coodsz.Ui.ModReportUi.RowLink(this, Report)", SourceFiles.Window(Root(), "TaomCoopLauncher"));
        Assert.Contains(".Only(id)", SourceFiles.Window(Root(), "AD1259Launcher"));
    }

    [Fact]
    public void One_send_click_sends_what_is_in_view()
    {
        var window = SourceFiles.Read(Path.Combine("src", "BannerlordCoopLauncher", "ModReportWindow.cs"));
        Assert.DoesNotContain("Click Send again", window);
        Assert.DoesNotContain("_shownPreview", window);
        Assert.DoesNotContain("Show what is sent", window);
        Assert.Contains("stack.Children.Add(Muted(ModReports.PrivacyText", window);
        Assert.Contains("stack.Children.Add(Muted(PreviewLine", window);
        var send = SourceFiles.Method(window, "private async Task SendAsync(");
        Assert.Contains("_preview.Text = ModReports.PreviewText(report);", send);
        Assert.True(send.IndexOf("_preview.Text = ModReports.PreviewText(report);", StringComparison.Ordinal)
            < send.IndexOf("ModReports.SendAsync(http, report)", StringComparison.Ordinal));
    }

    [WindowsFact]
    public void The_Coop_version_is_read_from_its_own_folder_when_the_setup_has_it_off()
    {
        var files = new MemoryFileSystem();
        var workshops = new[] { new WorkshopLibrary(WorkshopRoot, new Dictionary<string, long>(), ManifestParsed: true) };
        Assert.Equal("", ModuleResolver.InstalledVersion("Coop", "3770450698", GameRoot, workshops, files));

        // The game's Modules folder, matched by the id inside SubModule.xml, not the folder name.
        files.AddFile(Path.Combine(GameRoot, "Modules", "BannerlordCoop", "SubModule.xml"), Xml("Coop", "v0.1.5"));
        files.AddFile(Path.Combine(GameRoot, "Modules", "Other", "SubModule.xml"), Xml("OtherMod", "v9.9.9"));
        Assert.Equal("v0.1.5", ModuleResolver.InstalledVersion("Coop", "3770450698", GameRoot, workshops, files));

        // The game loads a Modules Coop before the Workshop Coop, so the Modules copy still wins (a Coop 0.1.7 unzipped by hand).
        files.AddFile(Path.Combine(WorkshopRoot, "3770450698", "Coop", "SubModule.xml"), Xml("Coop", "v0.1.6"));
        Assert.Equal("v0.1.5", ModuleResolver.InstalledVersion("Coop", "3770450698", GameRoot, workshops, files));
        Assert.Equal("", ModuleResolver.InstalledVersion("CoopNightly", null, GameRoot, workshops, files));
        Assert.Equal("", ModuleResolver.InstalledVersion("", null, GameRoot, workshops, files));

        // The Coop and Friends reports and crash reports use it.
        var friends = SourceFiles.Window(Root(), "BannerlordCoopLauncher");
        Assert.Contains("ModuleResolver.InstalledVersion(\"Coop\"", SourceFiles.Method(friends, "private string CoopVersion("));
        Assert.Contains("Coop = CoopVersion(),", SourceFiles.Method(friends, "private Coodsz.Ui.ModReportRequest ReportRequest("));
        Assert.Contains("Coop = CoopVersion(),", SourceFiles.Method(friends, "private CrashReportRequest CrashRequest("));
        Assert.DoesNotContain("VersionOf(\"Coop\"),", friends);
    }

    private static string Xml(string id, string version) =>
        $"""<Module><Name value="{id}"/><Id value="{id}"/><Version value="{version}"/></Module>""";

    private static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "BannerlordCoopLauncher.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
