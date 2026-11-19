using System.IO.Compression;
using BannerlordCoopLauncher.Core.Support;
using BurningEmpires.Core;
using CoodszHost.Services;
using TaomCoop.Core;

namespace BannerlordCoopLauncher.Tests;

// raven's second report (CR-P75R3): a minute after a resubscribe the Server Tool still said the Workshop
// Coop was not found, and the log pack could not say why. Right after a subscribe Steam has no content
// folder yet, so a download in progress now says wait for Steam, and every server tool's pack carries
// host/workshop-coop.txt with what the tool sees of the Workshop Coop.
public sealed class WorkshopCoopFactsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "coodsz-workshop-facts-" + Guid.NewGuid().ToString("N"));

    public WorkshopCoopFactsTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private string Library(string name) => Directory.CreateDirectory(Path.Combine(_root, name)).FullName;

    private static string Workshop(string library) => Path.Combine(library, "steamapps", "workshop");

    private static void Write(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    [Fact]
    public void The_downloading_line_is_Coodsz_wording_and_names_the_tool_button()
    {
        Assert.Equal("Steam is still downloading Bannerlord Coop. Wait until it's done in Steam's Downloads page, then click Prepare server again.",
            AntivirusText.WorkshopDownloading("Prepare server"));
        Assert.EndsWith("then click Start server again.", AntivirusText.WorkshopDownloading("Start server"));
    }

    [Fact]
    public void A_Coop_download_before_its_content_folder_exists_says_wait_for_Steam_not_subscribe()
    {
        var library = Library("Steam");
        // No libraries given (older callers) and nothing downloading: still subscribe.
        Assert.Equal(AntivirusText.NoWorkshopCoop("Prepare server"), AntivirusFiles.WorkshopServerProblem(null, "Prepare server"));
        Assert.Equal(AntivirusText.NoWorkshopCoop("Prepare server"), AntivirusFiles.WorkshopServerProblem(null, "Prepare server", null, [library]));

        // Steam's own download folder for Coop.
        Directory.CreateDirectory(Path.Combine(Workshop(library), "downloads", "261550", AntivirusFiles.CoopWorkshopId));
        Assert.Equal(AntivirusText.WorkshopDownloading("Prepare server"), AntivirusFiles.WorkshopServerProblem(null, "Prepare server", null, [library]));
    }

    private const string OtherItem = "2859188632";

    // appworkshop_261550.acf the way Steam writes it: NeedsUpdate and NeedsDownload for the whole app, then
    // each item under WorkshopItemsInstalled (when on disk) and WorkshopItemDetails (when subscribed).
    private static string Acf(bool coopInstalled, string coopManifest, string coopLatest) =>
        "\"AppWorkshop\"\n{\n\t\"appid\"\t\t\"261550\"\n\t\"SizeOnDisk\"\t\t\"6400000000\"\n\t\"NeedsUpdate\"\t\t\"1\"\n\t\"NeedsDownload\"\t\t\"1\"\n"
        + "\t\"WorkshopItemsInstalled\"\n\t{\n"
        + (coopInstalled ? "\t\t\"" + AntivirusFiles.CoopWorkshopId + "\"\n\t\t{\n\t\t\t\"size\"\t\t\"6000000000\"\n\t\t\t\"timeupdated\"\t\t\"1761600000\"\n\t\t\t\"manifest\"\t\t\"" + coopManifest + "\"\n\t\t}\n" : "")
        + "\t\t\"" + OtherItem + "\"\n\t\t{\n\t\t\t\"size\"\t\t\"1000\"\n\t\t\t\"timeupdated\"\t\t\"1761500000\"\n\t\t\t\"manifest\"\t\t\"111\"\n\t\t}\n\t}\n"
        + "\t\"WorkshopItemDetails\"\n\t{\n"
        + "\t\t\"" + AntivirusFiles.CoopWorkshopId + "\"\n\t\t{\n\t\t\t\"manifest\"\t\t\"" + coopManifest + "\"\n\t\t\t\"timeupdated\"\t\t\"1761600000\"\n\t\t\t\"timetouched\"\t\t\"1761600100\"\n\t\t\t\"latest_timeupdated\"\t\t\"1761600000\"\n\t\t\t\"latest_manifest\"\t\t\"" + coopLatest + "\"\n\t\t}\n"
        + "\t\t\"" + OtherItem + "\"\n\t\t{\n\t\t\t\"manifest\"\t\t\"111\"\n\t\t\t\"timeupdated\"\t\t\"1761500000\"\n\t\t\t\"timetouched\"\t\t\"1761500100\"\n\t\t\t\"latest_timeupdated\"\t\t\"1761700000\"\n\t\t\t\"latest_manifest\"\t\t\"222\"\n\t\t}\n\t}\n}\n";

    [Fact]
    public void Another_items_pending_update_never_reads_as_Coop_downloading()
    {
        var other = Library("Other");
        var library = Library("Steam");
        // Coop installed and current, the app wide flags on and another item behind its latest manifest, no
        // Coop download folder: the Workshop Coop's folder is just missing, so subscribe.
        Write(Path.Combine(Workshop(library), "appworkshop_261550.acf"), Acf(coopInstalled: true, "555", "555"));
        Assert.False(AntivirusFiles.CoopDownloading([other, library]));
        Assert.Equal(AntivirusText.NoWorkshopCoop("Start server"), AntivirusFiles.WorkshopServerProblem(null, "Start server", null, [other, library]));
        Assert.Contains("Coop's own entry says a download or update is waiting: no", WorkshopCoopFacts.Text([other, library]));
        Assert.Contains("Steam still downloading Coop: no", WorkshopCoopFacts.Text([other, library]));
    }

    [Theory]
    [InlineData(false, "555", "555")] // subscribed, not downloaded yet
    [InlineData(true, "555", "666")] // Coop's own update waiting
    public void Coops_own_entry_in_Steams_workshop_list_says_downloading(bool coopInstalled, string manifest, string latest)
    {
        var other = Library("Other");
        var library = Library("Steam");
        Write(Path.Combine(Workshop(library), "appworkshop_261550.acf"), Acf(coopInstalled, manifest, latest));
        Assert.True(AntivirusFiles.CoopDownloading([other, library]));
        Assert.Equal(AntivirusText.WorkshopDownloading("Start server"), AntivirusFiles.WorkshopServerProblem(null, "Start server", null, [other, library]));
        var text = WorkshopCoopFacts.Text([other, library]);
        Assert.Contains("Coop's own entry says a download or update is waiting: yes", text);
        Assert.Contains("Steam still downloading Coop: yes", text);
    }

    [Fact]
    public void The_facts_file_says_what_each_library_has_without_Steam_ids()
    {
        var empty = Library("Empty");
        var library = Library("Steam");
        var coop = Path.Combine(Workshop(library), "content", "261550", AntivirusFiles.CoopWorkshopId);
        Write(Path.Combine(coop, "SubModule.xml"), "<Module/>");
        Write(Path.Combine(coop, "DedicatedServer", "engine", "dotnet", "dotnet.exe"), "dotnet");
        Write(Path.Combine(Workshop(library), "appworkshop_261550.acf"), "\"AppWorkshop\"\n{\n\t\"subscribedby\"\t\t\"76561198012345678\"\n\t\"" + AntivirusFiles.CoopWorkshopId + "\"\n}");

        var text = WorkshopCoopFacts.Text([empty, library]);

        Assert.Contains("Steam libraries searched: 2", text);
        var steam = text[text.IndexOf(library, StringComparison.Ordinal)..];
        Assert.Contains("content folder: yes", steam);
        Assert.Contains("SubModule.xml: yes", steam);
        Assert.Contains("DedicatedServer\\BannerlordCoopServer.exe: no", steam);
        Assert.Contains("DedicatedServer\\engine\\dotnet\\dotnet.exe: yes", steam);
        Assert.Contains("appworkshop_261550.acf names Coop: yes", steam);
        Assert.Contains("Steam still downloading Coop: no", text);
        Assert.DoesNotContain("76561198012345678", text);
        Assert.Contains("content folder: no", text[..text.IndexOf(library, StringComparison.Ordinal)]);
        Assert.Contains("Steam libraries searched: 0 (Steam was not found)", WorkshopCoopFacts.Text([]));
    }

    [Fact]
    public void The_Burning_Empires_pack_carries_the_facts_file()
    {
        var library = Library("Steam");
        var zipPath = Path.Combine(_root, "be.zip");
        BeHostLogPack.Create(zipPath, Path.Combine(_root, "server"), Path.Combine(_root, "data"), Path.Combine(_root, "programdata"), "window line", steamLibraries: [library]);
        using var zip = ZipFile.OpenRead(zipPath);
        var entry = zip.GetEntry(WorkshopCoopFacts.Entry);
        Assert.NotNull(entry);
        using var reader = new StreamReader(entry!.Open());
        Assert.Contains("Steam libraries searched: 1", reader.ReadToEnd());
    }

    [Fact]
    public void The_TAOM_and_Coop_packs_carry_the_facts_file()
    {
        var library = Library("Steam");
        var taom = Path.Combine(_root, "taom.zip");
        HostLogPack.Create(taom, Path.Combine(_root, "server"), Path.Combine(_root, "data"), Path.Combine(_root, "programdata"), "window line", steamLibraries: [library]);
        using (var zip = ZipFile.OpenRead(taom))
            Assert.NotNull(zip.GetEntry(WorkshopCoopFacts.Entry));

        var coop = Path.Combine(_root, "coop.zip");
        Directory.CreateDirectory(Path.Combine(_root, "logs"));
        HostLogZip.Create(coop, Path.Combine(_root, "logs"), Path.Combine(_root, "data"), [], null, "window line", "console line", steamLibraries: [library]);
        using (var zip = ZipFile.OpenRead(coop))
            Assert.NotNull(zip.GetEntry(WorkshopCoopFacts.Entry));
    }

    [Fact]
    public void The_Coop_Start_and_the_TAOM_and_Burning_Empires_Prepare_ask_for_a_download_in_progress()
    {
        var coop = SourceFiles.Method(SourceFiles.Window(SourceFiles.Root(), "CoodszHost"), "private string ServerMissingText(");
        Assert.Contains("AntivirusFiles.SteamUpdating(workshop)", coop);
        Assert.Contains("AntivirusFiles.CoopDownloading(libraries)", coop);
        Assert.Contains("AntivirusText.WorkshopDownloading(\"Start server\")", coop);
        // The Prepare check reads Steam's workshop list, so it runs off the window's thread inside RunAsync.
        var taom = SourceFiles.Method(SourceFiles.Read(Path.Combine("src", "TaomCoopHost", "MainWindow.ServerControl.cs")), "private async void Prepare_Click(");
        Assert.Contains("throw new InvalidOperationException(AntivirusFiles.WorkshopServerProblem(workshopCoop, \"Prepare server\", AntivirusWatch.ForTool().Gone([workshopCoop]), libraries) ?? \"The Workshop Coop dedicated server was not found.\");", taom);
        var check = taom.IndexOf("AntivirusFiles.WorkshopCoop(libraries)", StringComparison.Ordinal);
        Assert.InRange(taom.LastIndexOf("await RunAsync(() =>", check, StringComparison.Ordinal), taom.IndexOf("if (serverSource == null)", StringComparison.Ordinal), check);
        // The Burning Empires Prepare too, since its own pack carries the facts file. It runs the same check for every
        // Prepare (also a Workshop Coop whose server program is there but an engine file is gone), with its watch.
        var be = SourceFiles.Method(SourceFiles.Read(Path.Combine("src", "BurningEmpiresHost", "MainWindow.ServerControl.cs")), "private async Task<bool> PrepareServerAsync(");
        Assert.Contains("await RunAsync(() => workshopProblem = BeServerPrepare.WorkshopServerProblem(libraries, GoneWorkshopFiles(libraries)));", be);
        Assert.Contains("AntivirusFiles.WorkshopServerProblem(CoopStaging.FindWorkshopModule(steamLibraries), PrepareButton, gone, steamLibraries)", SourceFiles.Read(Path.Combine("src", "BurningEmpires.Core", "BeServerPrepare.cs")));
        // A custom or mistyped Set server folder path never reads as a download.
        Assert.Contains("if (!AntivirusFiles.UsesWorkshopServer(_settings.ServerExecutable))", coop);
        // Steam is searched once per start: the libraries GoneSinceLastStart found.
        Assert.Contains("var libraries = gone.Libraries ?? BannerlordFinder.SteamLibraries();", coop);
        // The window opening with the server program missing words it the same way, off the window's thread.
        var window = SourceFiles.Window(SourceFiles.Root(), "CoodszHost").Replace("\r\n", "\n");
        Assert.Contains("if (!File.Exists(Executable))\n            ShowServerMissingOnOpen();", window);
        var onOpen = SourceFiles.Method(window, "private async void ShowServerMissingOnOpen(");
        Assert.Contains("if (!AntivirusFiles.UsesWorkshopServer(_settings.ServerExecutable))", onOpen);
        Assert.Contains("await Task.Run(() => ServerMissingText(GoneSinceLastStart()))", onOpen);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData(@"C:\Program Files (x86)\Steam\steamapps\workshop\content\261550\3770450698\DedicatedServer\BannerlordCoopServer.exe", true)]
    [InlineData("D:/SteamLibrary/steamapps/workshop/content/261550/3770450698/DedicatedServer/BannerlordCoopServer.exe", true)]
    [InlineData(@"D:\Servers\Coop\BannerlordCoopServer.exe", false)]
    [InlineData(@"C:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord\Modules\Coop\DedicatedServer\BannerlordCoopServer.exe", false)]
    public void Only_the_Workshop_Coop_server_program_can_be_mid_download(string? serverExecutable, bool workshop)
    {
        Assert.Equal(workshop, AntivirusFiles.UsesWorkshopServer(serverExecutable));
    }

    [Fact]
    public void A_Steam_lookup_that_fails_never_fails_the_pack()
    {
        var text = WorkshopCoopFacts.ForPack(null, () => throw new UnauthorizedAccessException("registry"));
        Assert.StartsWith("Bannerlord Coop on the Steam Workshop could not be checked: UnauthorizedAccessException", text);
        Assert.Contains("Steam libraries searched: 1", WorkshopCoopFacts.ForPack(null, () => [Library("Steam")]));
    }

    [Fact]
    public void A_library_under_the_Windows_user_folder_is_masked_in_the_pack()
    {
        // A library path under a user folder, made up so it never depends on where %TEMP% is (CI's TEMP can be
        // C:\Users\RUNNER~1). The facts file only checks what exists, so the folder needn't be there.
        var library = @"C:\Users\CoodszFactsTester\UserSteam";
        var zipPath = Path.Combine(_root, "masked.zip");
        BeHostLogPack.Create(zipPath, Path.Combine(_root, "server"), Path.Combine(_root, "data"), Path.Combine(_root, "programdata"), "window line", steamLibraries: [library]);
        using var zip = ZipFile.OpenRead(zipPath);
        using var reader = new StreamReader(zip.GetEntry(WorkshopCoopFacts.Entry)!.Open());
        var text = reader.ReadToEnd();
        Assert.Contains(@"Users\[user]\UserSteam", text);
        Assert.DoesNotContain("CoodszFactsTester", text, StringComparison.OrdinalIgnoreCase);
    }

    // Grok and Devin review: a Steam update the acf already shows was worded as antivirus with the resubscribe
    // step on Start, a registry SecurityException from finding Steam crashed the TAOM Start (async void), and
    // an update Steam started during the TAOM 2 second recheck was still called antivirus.
    [Fact]
    public void Start_checks_Steam_before_antivirus_and_a_registry_refusal_never_fails_the_watch()
    {
        var coop = SourceFiles.Read(Path.Combine("src", "CoodszHost", "MainWindow.Antivirus.cs"));
        var missing = SourceFiles.Method(coop, "private string ServerMissingText(");
        var steam = missing.IndexOf("AntivirusFiles.SteamUpdating(gone.Workshop)", StringComparison.Ordinal);
        Assert.True(steam > 0 && steam < missing.IndexOf("AntivirusWording.Reword(", StringComparison.Ordinal));
        Assert.Contains("System.Security.SecurityException", SourceFiles.Method(coop, "GoneSinceLastStart()"));
        Assert.Contains("System.Security.SecurityException", SourceFiles.Method(SourceFiles.Read(Path.Combine("src", "TaomCoopHost", "MainWindow.Antivirus.cs")), "GoneServerFiles(string root)"));
        Assert.Contains("System.Security.SecurityException", SourceFiles.Method(SourceFiles.Read(Path.Combine("src", "BurningEmpiresHost", "MainWindow.Antivirus.cs")), "GoneWorkshopFiles("));

        var settled = SourceFiles.Method(SourceFiles.Read(Path.Combine("src", "BannerlordCoopLauncher.Core", "Support", "AntivirusWatch.cs")), "public IReadOnlyList<string> GoneSettled(");
        var sleep = settled.IndexOf("Thread.Sleep(recheck);", StringComparison.Ordinal);
        Assert.True(sleep > 0 && settled.IndexOf("AntivirusFiles.SteamUpdating(workshopCoop)", sleep, StringComparison.Ordinal) > sleep);
    }
}
