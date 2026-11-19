using BannerlordCoopLauncher.Core.AD1259;
using BannerlordCoopLauncher.Core.Joining;
using BannerlordCoopLauncher.Core.Modules;
using BannerlordCoopLauncher.Core.Models;
using BannerlordCoopLauncher.Core.Steam;
using BurningEmpires.Core;
using CoodszHost.Services;
using static BurningEmpires.Core.Tests.BeServerFixtures;

namespace BannerlordCoopLauncher.Tests;

// Coop 0.1.7 is on Coop's GitHub release and ModDB before the Steam Workshop, so players unzip it into the
// game's Modules folder (Modules\Coop, id "Coop"). The game loads that copy before the Workshop Coop, so every
// tool judges it, keeps it and never calls the Workshop Coop a second copy of it.
public sealed class CoopManualInstallTests
{
    // The Modules Coop, the Workshop Coop and the one the game loads (the Modules one first).
    private static (CoopCopy? Modules, CoopCopy? Workshop, CoopCopy? Loaded) Copies(string game, string content)
    {
        var modules = CoopInstall.InModules(game);
        var workshop = CoopInstall.Read(Path.Combine(content, CoopInstall.WorkshopId), inModules: false);
        return (modules, workshop, modules ?? workshop);
    }

    private static (string Root, string Game, string Content) Pc(string name)
    {
        var root = NewRoot(name);
        var game = Path.Combine(root, "game");
        Directory.CreateDirectory(Path.Combine(game, "Modules"));
        var content = Path.Combine(root, "steamapps", "workshop", "content", "261550");
        Directory.CreateDirectory(content);
        return (root, game, content);
    }

    [Fact]
    public void Modules_only_is_the_loaded_copy()
    {
        var (root, game, content) = Pc("coop-modules");
        try
        {
            Module(Path.Combine(game, "Modules"), "Coop", "Coop", "v0.1.7");

            var copies = Copies(game, content);

            Assert.Null(copies.Workshop);
            Assert.Equal("v0.1.7", copies.Loaded!.Version);
            Assert.True(copies.Loaded.InModules);
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void Workshop_only_is_the_loaded_copy()
    {
        var (root, game, content) = Pc("coop-workshop");
        try
        {
            Module(content, CoopInstall.WorkshopId, "Coop", "v0.1.6");

            var copies = Copies(game, content);

            Assert.Null(copies.Modules);
            Assert.Equal("v0.1.6", copies.Loaded!.Version);
            Assert.False(copies.Loaded.InModules);
        }
        finally
        {
            Delete(root);
        }
    }

    [Theory]
    [InlineData("v0.1.7", "v0.1.6")]
    [InlineData("v0.1.6", "v0.1.7")]
    [InlineData("v0.1.7", "v0.1.7")]
    public void With_both_the_modules_copy_is_loaded_whatever_its_version(string modules, string workshop)
    {
        var (root, game, content) = Pc("coop-both");
        try
        {
            Module(Path.Combine(game, "Modules"), "Coop", "Coop", modules);
            Module(content, CoopInstall.WorkshopId, "Coop", workshop);

            var copies = Copies(game, content);

            Assert.Equal(workshop, copies.Workshop!.Version);
            Assert.Equal(modules, copies.Loaded!.Version);
            Assert.True(copies.Loaded.InModules);
            Assert.Equal(Path.Combine(game, "Modules", "Coop"), copies.Loaded.Directory);
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void A_broken_submodule_xml_and_the_old_coopnightly_id_are_not_the_coop()
    {
        var (root, game, content) = Pc("coop-broken");
        try
        {
            Write(Path.Combine(game, "Modules", "Coop", "SubModule.xml"), "<Module><Id value=\"Coop\"");
            Module(Path.Combine(game, "Modules"), "CoopNightly", "CoopNightly", "v0.1.5");
            Module(content, CoopInstall.WorkshopId, "Coop", "v0.1.6");

            var copies = Copies(game, content);

            Assert.Null(copies.Modules);
            Assert.Equal("v0.1.6", copies.Loaded!.Version);
            Assert.Null(CoopInstall.Read(Path.Combine(game, "Modules", "Coop"), inModules: true));
        }
        finally
        {
            Delete(root);
        }
    }

    [Theory]
    [InlineData("v0.1.7", "0.1.6", 1)]
    [InlineData("0.1.6", "v0.1.7", -1)]
    [InlineData("v0.1.7", "0.1.7.0", 0)]
    [InlineData("V0.1.10", "v0.1.9", 1)]
    [InlineData("", "v0.1.6", -1)]
    [InlineData("garbled", "v0.1.6", -1)]
    [InlineData("", "", 0)]
    public void Versions_compare_by_number_ignoring_a_leading_v(string left, string right, int sign)
    {
        Assert.Equal(sign, Math.Sign(CoopInstall.Compare(left, right)));
    }

    // ----- Coop and Friends: the Coop it shows and checks against the server is the one the game loads -----

    private const string Game = @"C:\game";
    private const string WorkshopRoot = @"C:\steam\steamapps\workshop\content\261550";

    private static string Xml(string id, string version) =>
        "<Module><Id value=\"" + id + "\"/><Name value=\"" + id + "\"/><Version value=\"" + version + "\"/></Module>";

    private static IReadOnlyList<WorkshopLibrary> Workshops() =>
        [new WorkshopLibrary(WorkshopRoot, new Dictionary<string, long> { [CoopInstall.WorkshopId] = 10 }, ManifestParsed: true)];

    // The client set's Coop row: id Coop, Workshop item 3770450698, pinned (DefaultClientSet).
    private static ServerProfile CoopProfile() =>
        ProfileJson.Parse("""
            {
              "Name": "CALRADIA",
              "Port": 4200,
              "BaseModules": ["Native"],
              "Mods": [{ "Name": "Bannerlord Coop", "WorkshopId": "3770450698", "ModuleId": "Coop", "Enabled": true, "Pinned": true }],
              "ModuleLoadOrder": ["Native", "Coop"]
            }
            """);

    private static MemoryFileSystem Files(string? modulesVersion, string? workshopVersion)
    {
        var files = new MemoryFileSystem();
        files.AddFile(Path.Combine(Game, "bin", "Win64_Shipping_Client", "Bannerlord.exe"), "");
        files.AddFile(Path.Combine(Game, "Modules", "Native", "SubModule.xml"), Xml("Native", "v1.4.8"));
        if (modulesVersion != null)
            files.AddFile(Path.Combine(Game, "Modules", "Coop", "SubModule.xml"), Xml("Coop", modulesVersion));
        if (workshopVersion != null)
            files.AddFile(Path.Combine(WorkshopRoot, CoopInstall.WorkshopId, "SubModule.xml"), Xml("Coop", workshopVersion));
        return files;
    }

    [WindowsTheory]
    [InlineData("v0.1.7", "v0.1.6")]
    [InlineData("v0.1.6", "v0.1.7")]
    public void The_launcher_uses_the_modules_coop_and_never_calls_it_an_extra_copy(string modules, string workshop)
    {
        var files = Files(modules, workshop);

        var coop = Assert.Single(ModuleResolver.Resolve(CoopProfile(), Game, Workshops(), null, files).Modules);

        Assert.Equal(ModuleState.Ready, coop.State);
        Assert.Equal(Path.Combine(Game, "Modules", "Coop"), coop.Directory);
        Assert.Null(coop.ExtraCopies);
        Assert.DoesNotContain("Extra copies", coop.Detail);
        Assert.Null(coop.LaunchId);
        Assert.Equal(modules, ModuleResolver.InstalledVersion("Coop", CoopInstall.WorkshopId, Game, Workshops(), files));
    }

    [WindowsFact]
    public void Without_a_modules_coop_the_workshop_coop_is_used_and_a_missing_coop_points_at_install_mods()
    {
        var workshopOnly = Files(null, "v0.1.6");
        var coop = Assert.Single(ModuleResolver.Resolve(CoopProfile(), Game, Workshops(), null, workshopOnly).Modules);
        Assert.Equal(Path.Combine(WorkshopRoot, CoopInstall.WorkshopId), coop.Directory);
        Assert.Equal("v0.1.6", ModuleResolver.InstalledVersion("Coop", CoopInstall.WorkshopId, Game, Workshops(), workshopOnly));

        var missing = Assert.Single(ModuleResolver.Resolve(CoopProfile(), Game, Workshops(), null, Files(null, null)).Modules);
        Assert.Equal(ModuleState.Missing, missing.State);
        // Coop 0.1.7 is on the Workshop now, so Install mods gets it and the GitHub line is gone.
        Assert.Equal("Click Install mods, then subscribe in Steam.", missing.Detail);
        Assert.DoesNotContain(CoopInstall.ClientGetLine, missing.Detail);
    }

    [Fact]
    public void A_server_asking_another_coop_version_links_coops_github_release()
    {
        var resolution = new ModuleResolution([],
            [new ResolvedModule("Bannerlord Coop", "Coop", CoopInstall.WorkshopId, true, false, ModuleState.VersionMismatch, "", @"C:\game\Modules\Coop")],
            IncludesCoop: true);
        var need = Assert.Single(ServerModNeeds.FromResolution(resolution, _ => false, _ => false));
        Assert.Equal(CoopInstall.ReleaseUrl, need.Link);

        var coop = AD1259Stack.Find("Coop")!;
        var wrong = new AD1259ModStatus(coop, AD1259ModState.WrongVersion, "v0.1.5", ["Workshop 3770450698"], [@"C:\w\3770450698"]);
        Assert.Equal(CoopInstall.ReleaseUrl, Assert.Single(ServerModNeeds.FromAD1259([wrong])).Link);
        var missing = new AD1259ModStatus(coop, AD1259ModState.Missing, null, [], []);
        Assert.Equal(coop.Link, Assert.Single(ServerModNeeds.FromAD1259([missing])).Link);
        // The nightly is never sent to Coop's GitHub release: its row says how to go back. A lone CoopNightly (Coop missing)
        // still gets the Workshop subscribe.
        Assert.Empty(ServerModNeeds.FromAD1259([wrong with { FoundVersion = "v0.1.8", Nightly = true }]));
        Assert.Equal(ServerModFetch.Workshop, Assert.Single(ServerModNeeds.FromAD1259([missing with { Nightly = true }])).Fetch);
        // So with only the nightly left, the Client Launcher shows no Get all missing button that would do nothing.
        var frame = SourceFiles.Read(Path.Combine("src", "AD1259Launcher", "MainWindow.Frame.cs"));
        Assert.Contains("var getsSomething = !gameMissing && BannerlordCoopLauncher.Core.Joining.ServerModNeeds.FromAD1259(problems).Count > 0;", frame);
        Assert.Contains("NextGetButton.Visibility = getsSomething ? Visibility.Visible : Visibility.Collapsed;", frame);
    }

    // ----- AD1259 client: a Modules Coop is judged and the Workshop Coop is no second copy of it -----

    private static Dictionary<string, List<(string Folder, string Directory, string Version)>> Found(params (string Id, string Folder, string Version)[] copies)
    {
        var found = new Dictionary<string, List<(string Folder, string Directory, string Version)>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (id, folder, version) in copies)
        {
            if (!found.TryGetValue(id, out var list))
                found[id] = list = [];
            list.Add((folder, @"C:\x\" + folder, version));
        }

        return found;
    }

    [Fact]
    public void Ad1259_takes_a_modules_coop_beside_the_workshop_coop()
    {
        var coop = AD1259Stack.Find("Coop")!;

        var ready = AD1259ModScan.Status(coop, Found(("Coop", "Modules\\Coop", "v0.1.7"), ("Coop", "Workshop 3770450698", "v0.1.6")));
        Assert.Equal(AD1259ModState.Ready, ready.State);
        Assert.Equal("v0.1.7", ready.FoundVersion);
        Assert.Equal(["Modules\\Coop"], ready.Folders);

        // The Modules copy is the one the game loads, so its version is the one judged.
        var old = AD1259ModScan.Status(coop, Found(("Coop", "Modules\\Coop", "v0.1.5"), ("Coop", "Workshop 3770450698", "v0.1.7")));
        Assert.Equal(AD1259ModState.WrongVersion, old.State);
        // An older Coop gets no GitHub line: the Workshop Coop 0.1.6 is still taken.
        Assert.DoesNotContain(CoopInstall.ClientGetLine, old.Problem);

        var twoInModules = AD1259ModScan.Status(coop, Found(("Coop", "Modules\\Coop", "v0.1.7"), ("Coop", "Modules\\Coop old", "v0.1.6"), ("Coop", "Workshop 3770450698", "v0.1.6")));
        Assert.Equal(AD1259ModState.Duplicate, twoInModules.State);
        Assert.Equal(2, twoInModules.Folders.Count);

        var missing = AD1259ModScan.Status(coop, Found());
        // Coop 0.1.7 is on the Workshop now, so the usual missing text stands alone, without the GitHub line.
        Assert.Equal("Bannerlord Coop is missing. Get v0.1.7 from Steam Workshop.", missing.Problem);
    }

    [Fact]
    public void Ad1259_keeps_two_copies_for_every_other_mod()
    {
        var rbm = AD1259Stack.Find("RBM")!;
        var status = AD1259ModScan.Status(rbm, Found(("RBM", "Modules\\RBM", rbm.Version), ("RBM", "Workshop 1", rbm.Version)));
        Assert.Equal(AD1259ModState.Duplicate, status.State);
    }

    [Fact]
    public void Ad1259_scan_reads_a_modules_coop_and_the_workshop_coop_from_disk()
    {
        var (root, game, content) = Pc("coop-ad1259");
        try
        {
            Module(Path.Combine(game, "Modules"), "Coop", "Coop", "v0.1.7");
            Module(content, CoopInstall.WorkshopId, "Coop", "v0.1.6");

            var coop = Assert.Single(AD1259ModScan.Check(Path.Combine(game, "Modules"), [content]), status => status.Mod.Id == "Coop");

            Assert.True(coop.IsReady);
            Assert.Equal([Path.Combine(game, "Modules", "Coop")], coop.Directories);
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void Ad1259_server_with_an_old_coop_names_the_coop_it_needs_without_the_server_package()
    {
        // Coop 0.1.7 is on the Workshop now, so an old server Coop gets no server download line.
        var rows = AD1259ToolsTests.CompleteServerRows();
        rows[rows.FindIndex(row => row.Id == "Coop")] = new ModuleRow { Id = "Coop", Version = "v0.1.5" };
        var problem = AD1259Server.Problem(rows);
        Assert.Contains("Bannerlord Coop v0.1.7 (the server has v0.1.5)", problem);
        Assert.DoesNotContain(CoopInstall.ServerGetLine("Set server folder"), problem);

        // A manual server with v0.1.7, or a Workshop one with v0.1.6, passes.
        rows[rows.FindIndex(row => row.Id == "Coop")] = new ModuleRow { Id = "Coop", Version = "v0.1.7" };
        Assert.Null(AD1259Server.Problem(rows));
        rows[rows.FindIndex(row => row.Id == "Coop")] = new ModuleRow { Id = "Coop", Version = "v0.1.6" };
        Assert.Null(AD1259Server.Problem(rows));

        // Another mod's problem says nothing about the Coop server package.
        rows[rows.FindIndex(row => row.Id == "RBM")] = new ModuleRow { Id = "RBM", Version = "v4.5.3" };
        Assert.DoesNotContain("BannerlordCoop-DedicatedServer", AD1259Server.Problem(rows));
    }

    [Fact]
    public void The_server_tool_missing_message_names_the_workshop_server_and_set_server_folder()
    {
        // Coop 0.1.7 is on the Workshop now, so the message names both folders Find looks in, not the server package.
        Assert.Equal("Bannerlord Coop's dedicated server was not found. Click Set server folder and choose BannerlordCoopServer.exe. It is in Modules\\Bannerlord Coop\\DedicatedServer, or in Workshop folder 3770450698\\DedicatedServer.",
            DedicatedServerFinder.MissingMessage);
        Assert.DoesNotContain("BannerlordCoop-DedicatedServer-Win64-v0.1.7.7z", DedicatedServerFinder.MissingMessage);
    }

    // ----- Burning Empires client: a newer Modules Coop stays, older ones and extra copies go -----

    [Fact]
    public void Be_keeps_a_newer_modules_coop_beside_the_workshop_coop()
    {
        var (root, game, content) = Pc("coop-be-newer");
        try
        {
            var modules = Path.Combine(game, "Modules");
            var workshop = Module(content, CoopInstall.WorkshopId, "Coop", "v0.1.6");
            Module(modules, "Coop", "Coop", "v0.1.7");
            Module(modules, "Coop 0.1.5", "Coop", "v0.1.5");

            var moved = BeCoopCopy.ParkBesideWorkshop(game, workshop, Path.Combine(root, BeCoopCopy.SetAsideMarkerFileName));

            Assert.Equal([Path.Combine(game, "Coop 0.1.5-kept-0.1.5")], moved);
            Assert.True(File.Exists(Path.Combine(modules, "Coop", "SubModule.xml")));
            Assert.Empty(BeCoopCopy.ParkBesideWorkshop(game, workshop, Path.Combine(root, BeCoopCopy.SetAsideMarkerFileName)));
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void Be_without_a_workshop_coop_keeps_the_newest_modules_coop()
    {
        var (root, game, _) = Pc("coop-be-alone");
        try
        {
            var modules = Path.Combine(game, "Modules");
            Module(modules, "Coop", "Coop", "v0.1.7");
            // CoopStaging.ParkDuplicateCoop kept this one, since TAOM pins 0.1.6.
            Module(modules, "Coop-016", "Coop", "v0.1.6");

            var moved = BeCoopCopy.ParkWithoutWorkshop(game, Path.Combine(root, BeCoopCopy.SetAsideMarkerFileName));

            Assert.Equal([Path.Combine(game, "Coop-016-kept-0.1.6")], moved);
            Assert.True(Directory.Exists(Path.Combine(modules, "Coop")));
            // One Coop alone always stays.
            Assert.Empty(BeCoopCopy.ParkWithoutWorkshop(game, Path.Combine(root, BeCoopCopy.SetAsideMarkerFileName)));
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void Be_client_uses_the_no_workshop_park_that_keeps_the_newest_coop()
    {
        var client = SourceFiles.Window(RepoRoot(), "BurningEmpiresLauncher");
        var guards = SourceFiles.Method(client, "private void ApplyCrashGuards()");
        Assert.Contains("BeCoopCopy.ParkWithoutWorkshop(_gameRoot, _setAsideMarker)", guards);
        Assert.DoesNotContain("CoopStaging.ParkDuplicateCoop(", guards);
    }

    // ----- Burning Empires Server Tool: Prepare server takes a Coop server the host unzipped himself -----

    // A Coop server package unzipped by hand, the same layout as the Workshop DedicatedServer.
    private static string ManualServer(string root, string folder, string coopVersion, string coopCore)
    {
        var workshop = WorkshopServer(Path.Combine(root, folder));
        var server = Path.Combine(root, folder, "CoopServer");
        Directory.Move(workshop, server);
        Module(Path.Combine(server, "engine", "Modules"), "Coop", "Coop", coopVersion);
        Write(Path.Combine(server, "engine", "Modules", "Coop", "bin", "Win64_Shipping_Server", "Coop.Core.dll"), coopCore);
        return server;
    }

    [Fact]
    public void Be_picked_server_folder_is_the_folder_or_its_dedicatedserver_folder()
    {
        var root = NewRoot("coop-be-pick");
        try
        {
            var server = ManualServer(root, "a", "v0.1.7", "coop core 7");
            Assert.Equal(server, BeServerPrepare.PickedServer(server));
            var nested = Path.Combine(root, "b");
            Directory.CreateDirectory(nested);
            Directory.Move(server, Path.Combine(nested, "DedicatedServer"));
            Assert.Equal(Path.Combine(nested, "DedicatedServer"), BeServerPrepare.PickedServer(nested));
            // A folder one level too high finds the one server inside it; an empty folder has none.
            Assert.Equal(Path.Combine(nested, "DedicatedServer"), BeServerPrepare.PickedServer(root));
            Directory.CreateDirectory(Path.Combine(root, "empty"));
            Assert.Null(BeServerPrepare.PickedServer(Path.Combine(root, "empty")));
            Assert.Null(BeServerPrepare.PickedServer(""));
            Assert.Equal("v0.1.7", BeServerPrepare.ServerCoopVersion(Path.Combine(nested, "DedicatedServer")));
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void Be_source_is_the_picked_server_until_the_workshop_coop_is_newer()
    {
        var root = NewRoot("coop-be-source");
        try
        {
            var workshop = WorkshopServer(root);
            var picked = ManualServer(root, "manual", "v0.1.7", "coop core 7");

            Assert.Equal(new BeServerSource(picked, Picked: true), BeServerPrepare.Source(picked, workshop));
            Assert.Equal(new BeServerSource(picked, Picked: true), BeServerPrepare.Source(picked, null));
            Assert.Equal(new BeServerSource(workshop, Picked: false), BeServerPrepare.Source("", workshop));
            Assert.Equal(new BeServerSource(workshop, Picked: false), BeServerPrepare.Source(Path.Combine(root, "gone"), workshop));
            Assert.Null(BeServerPrepare.Source(null, null));

            // Coop reached the Workshop with a newer version: the Workshop server is the default again.
            Module(Path.Combine(workshop, "engine", "Modules"), "Coop", "Coop", "v0.1.8");
            Assert.Equal(new BeServerSource(workshop, Picked: false), BeServerPrepare.Source(picked, workshop));
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void Be_prepare_copies_the_picked_server_and_copies_again_when_its_coop_changes()
    {
        var root = NewRoot("coop-be-prepare");
        try
        {
            var picked = ManualServer(root, "manual", "v0.1.7", "coop core 7");
            var serverRoot = Path.Combine(root, "server");
            var coopCore = Path.Combine("engine", "Modules", "Coop", "bin", "Win64_Shipping_Server", "Coop.Core.dll");

            var result = BeServerPrepare.Prepare(picked, serverRoot, picked: true);

            Assert.True(result.Copied > 0);
            Assert.StartsWith("Copied ", result.Lines[0]);
            Assert.Contains("the Coop server folder you picked", result.Lines[0]);
            Assert.Equal("coop core 7", File.ReadAllText(Path.Combine(serverRoot, coopCore)));
            Assert.True(File.Exists(Path.Combine(serverRoot, "BannerlordCoopServer.exe")));
            Assert.False(BeServerPrepare.CoopChanged(picked, serverRoot));

            // A newer package unzipped over the picked folder.
            Write(Path.Combine(picked, coopCore), "coop core 8");
            Assert.True(BeServerPrepare.CoopChanged(picked, serverRoot));
            BeServerPrepare.Prepare(picked, serverRoot, picked: true);
            Assert.Equal("coop core 8", File.ReadAllText(Path.Combine(serverRoot, coopCore)));
            Assert.False(BeServerPrepare.CoopChanged(picked, serverRoot));
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void Be_prepare_refuses_a_picked_folder_without_a_server_or_inside_the_server_folder()
    {
        var root = NewRoot("coop-be-refuse");
        try
        {
            var serverRoot = Path.Combine(root, "server");
            Directory.CreateDirectory(Path.Combine(root, "empty"));
            Assert.Equal(BeServerPrepare.PickedNotServer,
                Assert.Throws<InvalidOperationException>(() => BeServerPrepare.Prepare(Path.Combine(root, "empty"), serverRoot, picked: true)).Message);

            var inside = ManualServer(Path.Combine(root, "server"), "pkg", "v0.1.7", "coop core 7");
            Assert.Equal(BeServerPrepare.PickedInsideServer,
                Assert.Throws<InvalidOperationException>(() => BeServerPrepare.Prepare(inside, serverRoot, picked: true)).Message);
        }
        finally
        {
            Delete(root);
        }
    }

    // ----- Every new line is in all six languages -----

    [Fact]
    public void The_manual_coop_lines_are_translated()
    {
        string[] lines =
        [
            CoopInstall.ClientGetLine,
            CoopInstall.ClientMissingLine,
            "For the server, click Download Coop server under Advanced: server folder on the Settings page to get the Coop v0.1.7 dedicated server download (about 4.5 GB), linked at the bottom of Coop's release notes, not under Assets. Unzip it to a short folder like C:\\CoopServer, then click {0}.",
            DedicatedServerFinder.MissingMessage,
            BeServerPrepare.PickButton,
            BeServerPrepare.PickedNotServer,
            BeServerPrepare.PickedInsideServer,
            BeServerPrepare.PickedChangedNote
        ];
        foreach (var language in new[] { "ar", "de", "fr", "pt", "ru", "tr" })
        {
            var table = BannerlordCoopLauncher.Core.Localization.Translator.Entries(language);
            foreach (var line in lines)
                Assert.True(table.ContainsKey(line), language + " is missing: " + line);
        }
    }

    private static string RepoRoot() => SourceFiles.Root();
}
