using BannerlordCoopLauncher.Core.Modules;
using BannerlordCoopLauncher.Core.Profile;
using RealmOfThrones.Core;
using TaomCoop.Core;

namespace BannerlordCoopLauncher.Tests;

public class RotServerPathTests
{
    private sealed class Pc : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "coodsz-rotserver-" + Guid.NewGuid().ToString("N"));
        public string Game => Path.Combine(Root, "game");
        public string Steam => Path.Combine(Root, "steam");
        public string Server => Path.Combine(Root, "server");
        public string Compat => Path.Combine(Root, "payload", RotStack.CompatModuleId);
        public string Workshop(string id) => Path.Combine(Steam, "steamapps", "workshop", "content", "261550", id);
        public RotCoreSources Sources => new(Game, [Steam], Compat);

        public Pc(string mapVersion = "v8.1.8.2", string coreVersion = "v8.1.8")
        {
            foreach (var library in RotStack.Libraries)
                Module(Workshop(library.WorkshopId), library.ModuleId, library.TestedVersion, "bin/Win64_Shipping_Client/" + library.Name + ".dll", "AssetPackages/lib.tpac");
            Module(Path.Combine(Game, "Modules", "ROT-Core"), "ROT-Core", coreVersion, "bin/Win64_Shipping_Client/ROT.dll", "ModuleData/a.xml", "AssetPackages/big.tpac");
            Module(Path.Combine(Game, "Modules", "ROT-Content"), "ROT-Content", "v8.1.8", "ModuleData/b.xml", "music/song.ogg", "Shaders/s.bin", "EmAssetPackages/e.tpac", "ModuleSounds/x.ogg", "weapons.txt");
            Module(Path.Combine(Game, "Modules", "ROT-Map"), "ROT_Map", mapVersion, "SceneObj/Main_map/scene.xscene", "SceneObj/Other/scene.xscene", "ModuleData/DistanceCaches/c.bin");
            Module(Path.Combine(Game, "Modules", "ROT-Dragon"), "ROT-Dragon", "v8.1.8", "Prefabs/d.xml");
            Module(Compat, RotStack.CompatModuleId, RotStack.CompatVersion, "bin/Win64_Shipping_Client/RealmOfThrones.CoopCompat.dll", "ModuleData/rot_map.txt", RotServerPath.MapScriptName);
        }

        public static void Module(string folder, string id, string version, params string[] files)
        {
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "SubModule.xml"), "<Module><Id value=\"" + id + "\"/><Version value=\"" + version + "\"/></Module>");
            foreach (var file in files)
            {
                var path = Path.Combine(folder, file.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, id + ":" + file);
            }
        }

        public string ServerModule(string id, string file = "SubModule.xml") =>
            Path.Combine(RotServerPath.ModulesRoot(Server), id, file.Replace('/', Path.DirectorySeparatorChar));

        public void Dispose()
        {
            if (Directory.Exists(Root))
                Directory.Delete(Root, recursive: true);
        }
    }

    private static TaomSetup Plain()
    {
        var book = new RotSetupBook();
        return book.Current();
    }

    private static TaomSetup Custom(params string[] extras)
    {
        var setup = new TaomSetup { Name = "Mine" };
        foreach (var id in extras)
            Assert.Null(RotLoadOrder.Add(setup, id + " name", id, null));
        return setup;
    }

    private static FoundMod Found(string id, string folder) => new(id + " name", id, "0", folder, "Modules");

    // ----- The plan -----

    [Fact]
    public void Plain_lists_the_server_order_and_finds_every_core_module_where_the_host_has_it()
    {
        using var pc = new Pc();
        var plan = RotServerPath.Plan(Plain(), pc.Sources, []);
        Assert.Equal(RotStack.ServerModules, plan.Modules);
        Assert.Equal(RotStack.ServerModules, plan.Stage.Select(module => module.ModuleId));
        Assert.DoesNotContain("NavalDLC", plan.Modules);
        Assert.Empty(RotServerPath.Problems(plan));
        // With the server folder the Coop build comes first: this test server has neither Common.dll nor release-info.txt.
        Assert.Equal(RotServerPath.CoopBuildProblem(null), Assert.Single(RotServerPath.Problems(plan, pc.Server)));

        Assert.All(plan.Stage.Take(4), module => Assert.Equal(RotModuleSource.Library, module.Source));
        Assert.Equal(pc.Workshop("2859188632"), plan.Stage[0].SourceDirectory);
        var map = plan.Stage.Single(module => module.ModuleId == "ROT_Map");
        Assert.Equal(RotModuleSource.RealmOfThrones, map.Source);
        Assert.Equal(Path.Combine(pc.Game, "Modules", "ROT-Map"), map.SourceDirectory);
        Assert.Equal("v8.1.8.2", map.Version);
        var compat = plan.Stage[^1];
        Assert.Equal(RotModuleSource.Compat, compat.Source);
        Assert.Equal(pc.Compat, compat.SourceDirectory);
    }

    [Fact]
    public void A_library_in_the_games_modules_folder_and_a_rot_module_in_a_workshop_item_are_found_too()
    {
        using var pc = new Pc();
        Directory.Delete(pc.Workshop("2859238197"), recursive: true);
        Pc.Module(Path.Combine(pc.Game, "Modules", "Bannerlord.MBOptionScreen"), "Bannerlord.MBOptionScreen", "v5.12.3");
        Directory.Delete(Path.Combine(pc.Game, "Modules", "ROT-Dragon"), recursive: true);
        Pc.Module(Path.Combine(pc.Workshop("1234567890"), "Dragons"), "ROT-Dragon", "v8.1.8");

        Assert.Equal(Path.Combine(pc.Game, "Modules", "Bannerlord.MBOptionScreen"), RotServerPath.FindModule(pc.Sources, "Bannerlord.MBOptionScreen"));
        Assert.Equal(Path.Combine(pc.Workshop("1234567890"), "Dragons"), RotServerPath.FindModule(pc.Sources, "ROT-Dragon"));
        Assert.Null(RotServerPath.FindModule(pc.Sources, "Nowhere"));
    }

    [Fact]
    public void Extras_come_from_the_catalog_and_are_classified_for_the_server()
    {
        using var pc = new Pc();
        var data = Path.Combine(pc.Root, "mods", "XmlOnly");
        Pc.Module(data, "XmlOnly", "v1.0.0", "ModuleData/items.xml");
        var plan = RotServerPath.Plan(Custom("XmlOnly", "Gone"), pc.Sources, [Found("XmlOnly", data)]);
        Assert.Equal(new[] { "ROT-Dragon", "XmlOnly", "Gone", RotStack.CompatModuleId }, plan.Modules.TakeLast(4));
        var extra = plan.Stage.Single(module => module.ModuleId == "XmlOnly");
        Assert.Equal(RotModuleSource.Extra, extra.Source);
        Assert.Equal(ServerCode.DataOnly, extra.Code);
        Assert.Equal("XmlOnly name", extra.Label);
        Assert.Equal(new[] { "Gone name is not on this PC, so the server cannot load it." }, RotServerPath.Problems(plan));
    }

    [Fact]
    public void Problems_name_a_missing_core_module_and_a_realm_of_thrones_module_at_another_version()
    {
        using var pc = new Pc(mapVersion: "v8.1.8", coreVersion: "v8.1.7");
        Directory.Delete(pc.Workshop("2859232415"), recursive: true);
        var problems = RotServerPath.Problems(RotServerPath.Plan(Plain(), pc.Sources, []));
        Assert.Equal(new[]
        {
            "ButterLib is not on this PC, so the server cannot load it. Subscribe to it on the Steam Workshop, wait for Steam to download it, then click Prepare server.",
            "ROT-Core on this PC is not v8.1.8, the version the Realm of Thrones server is made for.",
            "ROT_Map on this PC is not v8.1.8.2, the version the Realm of Thrones server is made for."
        }, problems);
    }

    [Fact]
    public void A_library_at_another_version_is_the_hosts_own_unless_it_is_an_mcm_the_server_cannot_load()
    {
        using var pc = new Pc();
        Pc.Module(pc.Workshop("2859188632"), "Bannerlord.Harmony", "v2.5.0.0");
        Pc.Module(pc.Workshop("2859238197"), "Bannerlord.MBOptionScreen", "v5.12.0");
        Assert.Empty(RotServerPath.Problems(RotServerPath.Plan(Plain(), pc.Sources, [])));

        Pc.Module(pc.Workshop("2859238197"), "Bannerlord.MBOptionScreen", "v5.11.9");
        Assert.Equal(new[] { "MCM v5.11.9 on this PC is too old for the server, which needs v5.12.0 or newer." },
            RotServerPath.Problems(RotServerPath.Plan(Plain(), pc.Sources, [])));
        Pc.Module(pc.Workshop("2859238197"), "Bannerlord.MBOptionScreen", "beta");
        Assert.Single(RotServerPath.Problems(RotServerPath.Plan(Plain(), pc.Sources, [])));
    }

    [Fact]
    public void A_realm_of_thrones_version_without_its_v_is_the_same_version()
    {
        using var pc = new Pc(mapVersion: "8.1.8.2", coreVersion: "V8.1.8");
        Assert.Empty(RotServerPath.Problems(RotServerPath.Plan(Plain(), pc.Sources, [])));
    }

    [Fact]
    public void An_unreadable_sub_module_xml_is_named_and_never_copied()
    {
        using var pc = new Pc();
        var xml = Path.Combine(pc.Game, "Modules", "ROT-Dragon", "SubModule.xml");
        using (new FileStream(xml, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var plan = RotServerPath.Plan(Plain(), pc.Sources, []);
            Assert.Null(plan.Stage.Single(module => module.ModuleId == "ROT-Dragon").Version);
            var line = "The tool could not read the version of ROT-Dragon on this PC. Close anything that has its SubModule.xml open and try again.";
            Assert.Equal(new[] { line }, RotServerPath.Problems(plan));
            Assert.Contains(line, RotServerPath.Stage(pc.Server, plan));
            Assert.False(Directory.Exists(pc.ServerModule("ROT-Dragon", "")));
        }
    }

    // ----- Staging -----

    [Fact]
    public void Stage_copies_the_core_unchanged_under_its_ids_without_art_or_the_full_map()
    {
        using var pc = new Pc();
        var lines = RotServerPath.Stage(pc.Server, RotServerPath.Plan(Plain(), pc.Sources, []));
        Assert.Equal(RotStack.ServerModules.Length, lines.Count);
        Assert.All(lines, line => Assert.StartsWith("Copied ", line));

        Assert.Equal(File.ReadAllText(Path.Combine(pc.Workshop("2859188632"), "SubModule.xml")), File.ReadAllText(pc.ServerModule("Bannerlord.Harmony")));
        Assert.True(File.Exists(pc.ServerModule("Bannerlord.Harmony", "AssetPackages/lib.tpac")));
        Assert.True(File.Exists(pc.ServerModule("ROT-Core", "bin/Win64_Shipping_Client/ROT.dll")));
        Assert.False(File.Exists(pc.ServerModule("ROT-Core", "AssetPackages/big.tpac")));
        Assert.True(File.Exists(pc.ServerModule("ROT-Content", "weapons.txt")));
        foreach (var art in new[] { "music/song.ogg", "Shaders/s.bin", "EmAssetPackages/e.tpac", "ModuleSounds/x.ogg" })
            Assert.False(File.Exists(pc.ServerModule("ROT-Content", art)));
        Assert.True(File.Exists(pc.ServerModule("ROT_Map", "SceneObj/Other/scene.xscene")));
        Assert.True(File.Exists(pc.ServerModule("ROT_Map", "ModuleData/DistanceCaches/c.bin")));
        Assert.False(File.Exists(pc.ServerModule("ROT_Map", "SceneObj/Main_map/scene.xscene")));
        Assert.False(Directory.Exists(pc.ServerModule("ROT-Map", "")));
        Assert.False(Directory.Exists(pc.ServerModule("NavalDLC", "")));
        Assert.True(File.Exists(pc.ServerModule(RotStack.CompatModuleId, "ModuleData/rot_map.txt")));
        // The host's own folders are never changed.
        Assert.True(File.Exists(Path.Combine(pc.Game, "Modules", "ROT-Content", "music", "song.ogg")));
    }

    [Fact]
    public void A_second_stage_with_nothing_changed_copies_nothing()
    {
        using var pc = new Pc();
        RotServerPath.Stage(pc.Server, RotServerPath.Plan(Plain(), pc.Sources, []));
        Assert.True(File.Exists(Path.Combine(RotServerPath.StampFolder(pc.Server), "ROT-Core.json")));
        File.WriteAllText(pc.ServerModule("ROT-Core", "server-only.txt"), "keep");

        var again = RotServerPath.Stage(pc.Server, RotServerPath.Plan(Plain(), pc.Sources, []));
        Assert.All(again, line => Assert.EndsWith(" is already on the server.", line));
        Assert.Equal("keep", File.ReadAllText(pc.ServerModule("ROT-Core", "server-only.txt")));
    }

    [Fact]
    public void A_copy_cut_off_half_way_is_repaired()
    {
        using var pc = new Pc();
        // A copy stopped after a few files: its SubModule.xml is there, the rest and the stage record are not.
        Directory.CreateDirectory(pc.ServerModule("ROT-Core", ""));
        File.Copy(Path.Combine(pc.Game, "Modules", "ROT-Core", "SubModule.xml"), pc.ServerModule("ROT-Core"));
        File.WriteAllText(pc.ServerModule("ROT-Core", "half.tmp"), "cut off");

        var lines = RotServerPath.Stage(pc.Server, RotServerPath.Plan(Plain(), pc.Sources, []));
        Assert.Contains("Copied ROT-Core to the server.", lines);
        Assert.True(File.Exists(pc.ServerModule("ROT-Core", "bin/Win64_Shipping_Client/ROT.dll")));
        Assert.True(File.Exists(pc.ServerModule("ROT-Core", "ModuleData/a.xml")));
        Assert.False(File.Exists(pc.ServerModule("ROT-Core", "half.tmp")));
    }

    [Fact]
    public void A_version_change_leaves_no_old_file_and_changed_files_of_the_same_version_are_copied_again()
    {
        using var pc = new Pc();
        RotServerPath.Stage(pc.Server, RotServerPath.Plan(Plain(), pc.Sources, []));
        File.WriteAllText(pc.ServerModule("ROT-Core", "server-only.txt"), "old");

        var source = Path.Combine(pc.Game, "Modules", "ROT-Core");
        File.Delete(Path.Combine(source, "ModuleData", "a.xml"));
        Pc.Module(source, "ROT-Core", "v8.1.9", "ModuleData/new.xml");
        var updated = RotServerPath.Stage(pc.Server, RotServerPath.Plan(Plain(), pc.Sources, []));
        Assert.Contains("Copied ROT-Core to the server.", updated);
        Assert.False(File.Exists(pc.ServerModule("ROT-Core", "server-only.txt")));
        Assert.False(File.Exists(pc.ServerModule("ROT-Core", "ModuleData/a.xml")));
        Assert.True(File.Exists(pc.ServerModule("ROT-Core", "ModuleData/new.xml")));
        Assert.Contains("v8.1.9", File.ReadAllText(pc.ServerModule("ROT-Core")));

        File.WriteAllText(Path.Combine(pc.Game, "Modules", "ROT-Dragon", "Prefabs", "d.xml"), "a fixed prefab, same version");
        var refreshed = RotServerPath.Stage(pc.Server, RotServerPath.Plan(Plain(), pc.Sources, []));
        Assert.Contains("Copied ROT-Dragon to the server.", refreshed);
        Assert.Equal("a fixed prefab, same version", File.ReadAllText(pc.ServerModule("ROT-Dragon", "Prefabs/d.xml")));
    }

    [Fact]
    public void Rot_core_never_keeps_a_server_bin_folder()
    {
        using var pc = new Pc();
        Pc.Module(Path.Combine(pc.Game, "Modules", "ROT-Core"), "ROT-Core", "v8.1.8", "bin/Win64_Shipping_Server/ROT.dll");
        RotServerPath.Stage(pc.Server, RotServerPath.Plan(Plain(), pc.Sources, []));
        Assert.False(Directory.Exists(pc.ServerModule("ROT-Core", "bin/Win64_Shipping_Server")));

        Directory.CreateDirectory(pc.ServerModule("ROT-Core", "bin/Win64_Shipping_Server"));
        File.WriteAllText(pc.ServerModule("ROT-Core", "bin/Win64_Shipping_Server/left.dll"), "left over");
        var lines = RotServerPath.Stage(pc.Server, RotServerPath.Plan(Plain(), pc.Sources, []));
        Assert.Contains("ROT-Core is already on the server.", lines);
        Assert.False(Directory.Exists(pc.ServerModule("ROT-Core", "bin/Win64_Shipping_Server")));
        Assert.True(File.Exists(pc.ServerModule("ROT-Core", "bin/Win64_Shipping_Client/ROT.dll")));
    }

    // ----- The remove guard -----

    [Theory]
    [InlineData("Native", null)]
    [InlineData("SandBoxCore", null)]
    [InlineData("CoopNightly", null)]
    [InlineData("Coop", null)]
    [InlineData("NavalDLC", null)]
    [InlineData("DedicatedServer.Windows", null)]
    [InlineData("NotInThePlan", null)]
    [InlineData("", null)]
    [InlineData("..", null)]
    [InlineData(" ROT-Core", null)]
    [InlineData("ROT-Core\\..\\Native", null)]
    [InlineData("ROT-Core", "..\\..\\Native")]
    [InlineData("ROT-Core", "..")]
    public void The_remove_guard_refuses_every_folder_that_is_not_a_staged_module(string id, string? inside)
    {
        using var pc = new Pc();
        var plan = RotServerPath.Plan(Plain(), pc.Sources, []);
        RotServerPath.Stage(pc.Server, plan);
        foreach (var keep in new[] { "Native", "CoopNightly", "NavalDLC", "DedicatedServer.Windows" })
            Directory.CreateDirectory(pc.ServerModule(keep, ""));

        Assert.Contains("is not a module folder this setup puts on the server", Assert.Throws<InvalidOperationException>(() => RotServerPath.RemoveStaged(pc.Server, plan, id, inside)).Message);
        foreach (var keep in new[] { "Native", "CoopNightly", "NavalDLC", "DedicatedServer.Windows", "ROT-Core" })
            Assert.True(Directory.Exists(pc.ServerModule(keep, "")));
    }

    [Fact]
    public void The_remove_guard_refuses_a_junction_and_removes_a_real_staged_folder()
    {
        using var pc = new Pc();
        var plan = RotServerPath.Plan(Plain(), pc.Sources, []);
        var outside = Path.Combine(pc.Root, "outside");
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "precious.txt"), "keep");
        Directory.CreateDirectory(RotServerPath.ModulesRoot(pc.Server));
        var link = pc.ServerModule("ROT-Dragon", "");
        var made = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", "/c mklink /J \"" + link.TrimEnd('\\') + "\" \"" + outside + "\"")
            { CreateNoWindow = true, UseShellExecute = false });
        made!.WaitForExit();
        Assert.True(Directory.Exists(link));

        Assert.Throws<InvalidOperationException>(() => RotServerPath.RemoveStaged(pc.Server, plan, "ROT-Dragon"));
        Assert.True(File.Exists(Path.Combine(outside, "precious.txt")));
        Directory.Delete(link.TrimEnd('\\'));

        RotServerPath.Stage(pc.Server, plan);
        RotServerPath.RemoveStaged(pc.Server, plan, "ROT-Dragon");
        Assert.False(Directory.Exists(pc.ServerModule("ROT-Dragon", "")));
        RotServerPath.RemoveStaged(pc.Server, plan, "ROT-Dragon");
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("v1.0.")]
    [InlineData("v1.0 ")]
    [InlineData("..\\x")]
    [InlineData("a/b")]
    public void A_map_version_that_is_not_a_plain_folder_name_never_gives_a_path(string version)
    {
        Assert.Contains("cannot be used as a folder name", Assert.Throws<InvalidOperationException>(() => RotServerPath.MapStageFolder(@"C:\Tool", version)).Message);
    }

    [Fact]
    public void An_extra_with_client_code_gets_it_switched_off_in_the_server_copy_only()
    {
        using var pc = new Pc();
        var mod = Path.Combine(pc.Root, "mods", "CodeMod");
        Directory.CreateDirectory(Path.Combine(mod, "bin", "Win64_Shipping_Client"));
        var xml = "<Module><Id value=\"CodeMod\"/><Version value=\"v1.0.0\"/><SubModules><SubModule><Name value=\"CodeMod\"/><DLLName value=\"CodeMod.dll\"/><SubModuleClassType value=\"CodeMod.Sub\"/><Tags/></SubModule></SubModules></Module>";
        File.WriteAllText(Path.Combine(mod, "SubModule.xml"), xml);
        File.WriteAllText(Path.Combine(mod, "bin", "Win64_Shipping_Client", "CodeMod.dll"), "dll");

        var plan = RotServerPath.Plan(Custom("CodeMod"), pc.Sources, [Found("CodeMod", mod)]);
        Assert.Equal(ServerCode.ClientCodeOnly, plan.Stage.Single(module => module.ModuleId == "CodeMod").Code);
        var lines = RotServerPath.Stage(pc.Server, plan);
        Assert.Contains(lines, line => line.StartsWith("Copied CodeMod name to the server.") && line.Contains("client code is switched off"));
        Assert.Equal(xml, File.ReadAllText(Path.Combine(mod, "SubModule.xml")));
        var serverXml = File.ReadAllText(pc.ServerModule("CodeMod"));
        Assert.Contains("DedicatedServerType", serverXml);
        Assert.Equal(ServerCode.ClientCodeSkipped, ServerModuleInstall.Classify(Path.Combine(RotServerPath.ModulesRoot(pc.Server), "CodeMod")));
    }

    [Fact]
    public void A_module_this_pc_lacks_is_skipped_by_stage()
    {
        using var pc = new Pc();
        var plan = RotServerPath.Plan(Custom("Gone"), pc.Sources, []);
        var lines = RotServerPath.Stage(pc.Server, plan);
        Assert.DoesNotContain(lines, line => line.Contains("Gone"));
        Assert.False(Directory.Exists(pc.ServerModule("Gone", "")));
    }

    // ----- The server map -----

    [Fact]
    public void The_staged_map_replaces_the_servers_campaign_map_and_map_data()
    {
        using var pc = new Pc();
        var plan = RotServerPath.Plan(Plain(), pc.Sources, []);
        RotServerPath.Stage(pc.Server, plan);
        var stage = RotServerPath.MapStageFolder(Path.Combine(pc.Root, "tool"), "v8.1.8.2");
        Assert.Equal(Path.Combine(pc.Root, "tool", "ServerMap", "v8.1.8.2"), stage);
        Assert.Equal("The Realm of Thrones map for the server is not made yet.", RotServerPath.MapProblem(stage));
        Assert.Throws<InvalidOperationException>(() => RotServerPath.ApplyMap(pc.Server, plan, stage));
        Directory.CreateDirectory(pc.ServerModule("ROT_Map", "SceneObj/Main_map"));
        File.WriteAllText(pc.ServerModule("ROT_Map", "SceneObj/Main_map/old_terrain.bin"), "an older map");

        Pc.Module(Path.Combine(stage, "ROT_Map", "SceneObj", "Main_map"), "unused", "v0", "scene.xscene", "flowmap.dds", "naval_map_points.txt");
        Directory.CreateDirectory(Path.Combine(stage, RotStack.CompatModuleId, "ModuleData"));
        File.WriteAllText(Path.Combine(stage, RotStack.CompatModuleId, "ModuleData", "rot_map.txt"), "staged borders");
        Assert.Null(RotServerPath.MapProblem(stage));

        Assert.True(RotServerPath.ApplyMap(pc.Server, plan, stage));
        Assert.True(File.Exists(pc.ServerModule("ROT_Map", "SceneObj/Main_map/flowmap.dds")));
        Assert.False(File.Exists(pc.ServerModule("ROT_Map", "SceneObj/Main_map/old_terrain.bin")));
        Assert.True(File.Exists(pc.ServerModule("ROT_Map", "SceneObj/Other/scene.xscene")));
        Assert.Equal("unused:scene.xscene", File.ReadAllText(pc.ServerModule("ROT_Map", "SceneObj/Main_map/scene.xscene")));
        Assert.Equal("staged borders", File.ReadAllText(pc.ServerModule(RotStack.CompatModuleId, "ModuleData/rot_map.txt")));
        Assert.Equal(RotStack.CompatModuleId + ":ModuleData/rot_map.txt", File.ReadAllText(Path.Combine(pc.Compat, "ModuleData", "rot_map.txt")));

        // The same map again copies nothing; a changed staged map or a changed rot_map.txt is copied.
        Assert.False(RotServerPath.ApplyMap(pc.Server, plan, stage));
        File.WriteAllText(Path.Combine(stage, "ROT_Map", "SceneObj", "Main_map", "flowmap.dds"), "a newer flow map");
        Assert.True(RotServerPath.ApplyMap(pc.Server, plan, stage));
        Assert.Equal("a newer flow map", File.ReadAllText(pc.ServerModule("ROT_Map", "SceneObj/Main_map/flowmap.dds")));
        File.WriteAllText(pc.ServerModule(RotStack.CompatModuleId, "ModuleData/rot_map.txt"), "other borders");
        Assert.True(RotServerPath.ApplyMap(pc.Server, plan, stage));
        Assert.False(RotServerPath.ApplyMap(pc.Server, plan, stage));
    }

    // A server folder set up by hand has no stage records: Stage removes its ROT_Map through the guard and copies it
    // again without the campaign map, and ApplyMap then puts the made map in.
    [Fact]
    public void A_hand_set_up_server_gets_rot_map_copied_again_and_the_made_map_put_in()
    {
        using var pc = new Pc();
        var plan = RotServerPath.Plan(Plain(), pc.Sources, []);
        Pc.Module(pc.ServerModule("ROT_Map", ""), "ROT_Map", "v8.1.8.2", "SceneObj/Main_map/scene.xscene", "hand.txt");
        Assert.False(Directory.Exists(RotServerPath.StampFolder(pc.Server)));
        var stage = RotServerPath.MapStageFolder(Path.Combine(pc.Root, "tool"), "v8.1.8.2");
        Pc.Module(Path.Combine(stage, "ROT_Map", "SceneObj", "Main_map"), "made", "v0", "scene.xscene");
        Directory.CreateDirectory(Path.Combine(stage, RotStack.CompatModuleId, "ModuleData"));
        File.WriteAllText(Path.Combine(stage, RotStack.CompatModuleId, "ModuleData", "rot_map.txt"), "staged borders");

        Assert.Contains("Copied ROT_Map to the server.", RotServerPath.Stage(pc.Server, plan));
        Assert.False(File.Exists(pc.ServerModule("ROT_Map", "hand.txt")));
        Assert.False(File.Exists(pc.ServerModule("ROT_Map", "SceneObj/Main_map/scene.xscene")));
        Assert.True(File.Exists(pc.ServerModule("ROT_Map", "SceneObj/Other/scene.xscene")));
        Assert.True(RotServerPath.ApplyMap(pc.Server, plan, stage));
        Assert.Equal("made:scene.xscene", File.ReadAllText(pc.ServerModule("ROT_Map", "SceneObj/Main_map/scene.xscene")));
        Assert.False(RotServerPath.ApplyMap(pc.Server, plan, stage));
    }

    // The sixth test: Prepare server took 2 s and showed no map step, because the map was made in seconds and the
    // only line about it went to the log. Every case now has its own line, shown after "Server prepared."
    [Fact]
    public void The_map_line_says_made_now_made_before_or_already_in_the_server()
    {
        Assert.Equal("Made the Realm of Thrones map for the server and put it into the server.", RotServerPath.MapLine(madeNow: true, copied: true));
        Assert.Equal("Put the Realm of Thrones map for the server into the server. It was made before, so the tool used that copy.", RotServerPath.MapLine(madeNow: false, copied: true));
        Assert.Equal("The Realm of Thrones map for the server is already in the server.", RotServerPath.MapLine(madeNow: false, copied: false));
        Assert.Contains("ROT_Map", RotServerPath.MapSourceMissing);
    }

    [Fact]
    public void The_map_script_reads_the_hosts_rot_map_and_writes_only_into_the_stage_folder()
    {
        var start = RotServerPath.MapStageStart(@"C:\Payload\RealmOfThrones.CoopCompat", @"D:\Games\Modules\ROT-Map", @"C:\Tool\ServerMap\v8.1.8.2");
        Assert.EndsWith("powershell.exe", start.FileName);
        Assert.Equal(new[] { "-NoLogo", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", @"C:\Payload\RealmOfThrones.CoopCompat\Stage-RotServerMap.ps1",
            "-RotMap", @"D:\Games\Modules\ROT-Map", "-OutDir", @"C:\Tool\ServerMap\v8.1.8.2" }, start.Arguments);
        Assert.Empty(start.Environment);
    }

    // ----- Saves -----

    [Fact]
    public void A_world_made_under_another_setup_is_named_before_start()
    {
        using var pc = new Pc();
        var save = Path.Combine(pc.Root, "camp.sav");
        var plainModules = string.Join(";", new[] { "Bannerlord.Harmony", "Bannerlord.ButterLib", "Bannerlord.UIExtenderEx", "Bannerlord.MBOptionScreen", "Native", "SandBoxCore", "Sandbox", "CustomBattle", "StoryMode", "NavalDLC", "CoopNightly", "ROT-Core", "ROT-Content", "ROT_Map", "ROT-Dragon", "RealmOfThrones.CoopCompat" });
        File.WriteAllText(save, "XXXX{\"List\":{\"CharacterName\":\"Ann\",\"MainHeroLevel\":\"3\",\"DayLong\":\"2\",\"Modules\":\"" + plainModules + "\"}}rest");

        Assert.Null(RotServerPath.SaveProblem(save, Plain()));
        Assert.Equal(SaveModAction.Start, RotServerPath.SaveChange(save, Plain()).Action);

        var custom = Custom("Extra");
        Assert.Contains("Extra", RotServerPath.SaveProblem(save, custom));
        var change = RotServerPath.SaveChange(save, custom);
        Assert.Equal(SaveModAction.AskToAdd, change.Action);
        Assert.Equal(new[] { "Extra" }, change.Added);

        File.WriteAllText(save, "XXXX{\"List\":{\"CharacterName\":\"Ann\",\"MainHeroLevel\":\"3\",\"DayLong\":\"2\",\"Modules\":\"" + plainModules + ";Extra\"}}rest");
        Assert.Contains("Extra", RotServerPath.SaveProblem(save, Plain()));
        Assert.Equal(SaveModAction.Refuse, RotServerPath.SaveChange(save, Plain()).Action);
        Assert.Null(RotServerPath.SaveProblem(save, custom));
    }

    [Fact]
    public void Only_a_save_whose_header_lists_no_rot_core_is_foreign()
    {
        using var pc = new Pc();
        var save = Path.Combine(pc.Root, "camp.sav");
        static string Header(string modules) =>
            "XXXX{\"List\":{\"CharacterName\":\"Ann\",\"MainHeroLevel\":\"3\",\"DayLong\":\"2\",\"Modules\":\"" + modules + "\"}}rest";

        File.WriteAllText(save, Header("Native;SandBoxCore;Sandbox;CoopNightly;rot-core ;ROT-Content"));
        Assert.False(RotServerPath.IsForeignSave(save));
        File.WriteAllText(save, Header("Native;SandBoxCore;Sandbox;CoopNightly;BurningEmpires"));
        Assert.True(RotServerPath.IsForeignSave(save));
        // A header that cannot be read is left to the compat module, which refuses a foreign world when it loads.
        File.WriteAllBytes(save, [0, 1, 2, 3, 4, 5]);
        Assert.False(RotServerPath.IsForeignSave(save));
        Assert.False(RotServerPath.IsForeignSave(Path.Combine(pc.Root, "missing.sav")));
    }

    [Fact]
    public void A_missing_realm_of_thrones_module_says_where_it_goes()
    {
        using var pc = new Pc();
        Directory.Delete(Path.Combine(pc.Game, "Modules", "ROT-Dragon"), recursive: true);
        Assert.Equal(new[] { "ROT-Dragon is not on this PC, so the server cannot load it. Put Realm of Thrones v8.1.8 into Bannerlord's Modules folder, then click Prepare server." },
            RotServerPath.Problems(RotServerPath.Plan(Plain(), pc.Sources, [])));
    }

    [Fact]
    public void The_full_server_order_puts_the_fixed_modules_around_the_list()
    {
        var order = RotServerPath.FullServerOrder(Custom("Extra"));
        Assert.Equal(new[] { "Native", "SandBoxCore", "Sandbox", "CoopNightly", "NavalDLC", "Bannerlord.Harmony" }, order.Take(6));
        Assert.Equal(new[] { "ROT-Dragon", "Extra", RotStack.CompatModuleId, "DedicatedServer.Windows" }, order.TakeLast(4));
    }

    // ----- When the world is ready -----

    [Fact]
    public void A_new_world_is_ready_after_serving_and_the_world_check_in_either_order()
    {
        var watch = new RotWorldWatch(newWorld: true);
        Assert.Equal(RotWorldState.Waiting, watch.Feed("09:49:27.084  server is READY when you see:  [DedicatedServer] SERVING"));
        Assert.Equal(RotWorldState.Waiting, watch.Feed("23:58:37.814    [DedicatedServer] SERVING"));
        Assert.Equal(RotWorldState.Waiting, watch.Feed("[RealmOfThrones.CoopCompat] Host: the loaded world is in the year 299"));
        Assert.Equal(RotWorldState.Waiting, watch.Feed("[RealmOfThrones.CoopCompat] World check passed (kingdoms 30, clans 400)"));
        Assert.Equal(RotWorldState.Ready, watch.Feed("09:50:45.233  [DedicatedServer] SERVING \u2014 coop server up, waiting for clients"));
        Assert.Equal(RotWorldState.Ready, watch.Feed("!!! something later"));
        Assert.Null(watch.FailedLine);

        var other = new RotWorldWatch(newWorld: true);
        other.Feed("[DedicatedServer] SERVING - coop server up, waiting for clients");
        Assert.Equal(RotWorldState.Ready, other.Feed("World check passed"));
    }

    [Fact]
    public void A_prefixed_help_line_and_another_mods_alarm_change_nothing_and_a_failure_after_ready_is_ignored()
    {
        var watch = new RotWorldWatch(newWorld: true);
        Assert.Equal(RotWorldState.Waiting, watch.Feed("09:49:27.084  [DedicatedServer Engine] server is READY when you see:  [DedicatedServer] SERVING - coop server up"));
        Assert.Equal(RotWorldState.Waiting, watch.Feed("[SomeOtherMod] !!! could not find its config"));
        Assert.Equal(RotWorldState.Waiting, watch.Feed("World check passed"));
        Assert.Equal(RotWorldState.Ready, watch.Feed("[DedicatedServer] SERVING - coop server up, waiting for clients"));
        Assert.Equal(RotWorldState.Ready, watch.Feed("[RealmOfThrones.CoopCompat] !!! World check FAILED later"));
        Assert.Null(watch.FailedLine);
    }

    [Fact]
    public void A_loaded_world_is_ready_after_serving_and_the_year_line()
    {
        var watch = new RotWorldWatch(newWorld: false);
        Assert.Equal(RotWorldState.Waiting, watch.Feed("World check passed"));
        Assert.Equal(RotWorldState.Waiting, watch.Feed("2026-11-11 09:50:44.597  [RealmOfThrones.CoopCompat] Host: the loaded world is in the year 299 of Realm of Thrones' calendar."));
        Assert.Equal(RotWorldState.Ready, watch.Feed("09:50:45.233  [DedicatedServer] SERVING \u2014 coop server up, waiting for clients"));
    }

    [Theory]
    [InlineData("[RealmOfThrones.CoopCompat] !!! World check FAILED: 2 kingdoms (kingdoms 2, clans 246, settlements 1065 (the map defines 1065), year 299). This world is held back: the server never finishes loading it, so it never serves or saves it (COODSZ_ROT_WORLD_CHECK=warn goes on anyway).")]
    [InlineData("[RealmOfThrones.CoopCompat] !!! World check FAILED: the check could not run (NullReferenceException: x). Going on anyway; do not keep this world's save.")]
    [InlineData("[RealmOfThrones.CoopCompat] !!! Fresh campaign: REFUSED save 'old': it was not made with Realm of Thrones (modules Native, file not found) and is not the server's blank save, so this server cannot load it. Pick a new save name to start a new Realm of Thrones campaign.")]
    [InlineData("[RealmOfThrones.CoopCompat] !!! Fresh campaign: REFUSED to make a new world for save 'rottools1': the host is not complete (2 models failed).")]
    [InlineData("[RealmOfThrones.CoopCompat] Host: !!! THE HOST IS NOT COMPLETE: 2 models failed (the lines above say which). At full a new world is held back and a saved one is not loaded.")]
    [InlineData("[RealmOfThrones.CoopCompat] Host: !!! THIS NEW WORLD IS IN THE YEAR 1084. A Realm of Thrones world starts in 299 and the game's own in 1084, so it was made without Realm of Thrones' time model. DO NOT KEEP ITS SAVE.")]
    [InlineData("[RealmOfThrones.CoopCompat] !!! Fresh campaign: NO CLANS LOADED. The Factions XML failed, the new campaign cannot start. See the XML load error above.")]
    [InlineData("[RealmOfThrones.CoopCompat] !!! Fresh campaign: Realm of Thrones world 'rot1' loads at host level models, where Realm of Thrones' behaviors are not registered: if this server saves, the Realm of Thrones data in it is lost. Do not let it save over a world you keep.")]
    [InlineData("[RealmOfThrones.CoopCompat] Host: !!! at world ready Hero.MainHero is null, Clan.PlayerClan is null and MobileParty.MainParty is null. A hosted model that reads Hero.MainHero without a check (ROTTroopUpgradeModel.DoesPartyHaveRequiredItemsForUpgrade) would throw.")]
    public void A_stop_line_fails_the_watch_and_is_kept_to_show(string line)
    {
        var watch = new RotWorldWatch(newWorld: true);
        watch.Feed("[DedicatedServer] SERVING - coop server up");
        Assert.Equal(RotWorldState.Failed, watch.Feed("  " + line + "  "));
        Assert.Equal(line, watch.FailedLine);
        Assert.Equal(RotWorldState.Failed, watch.Feed("World check passed"));
    }

    // The sixth test's server console (2026-11-11): the module's main hero lines before the world was ready
    // carried "!!!" and stopped the watch, so the tool would not save a good new world.
    [Fact]
    public void The_main_hero_lines_of_a_new_world_and_other_alarms_do_not_stop_the_watch()
    {
        var watch = new RotWorldWatch(newWorld: true);
        string[] lines =
        [
            "18:14:36.667  server is READY when you see:  [DedicatedServer] SERVING",
            "18:16:28.996  [DedicatedServer Engine] [RealmOfThrones.CoopCompat] Host: !!! at game start Hero.MainHero is UNREADABLE (NullReferenceException), Clan.PlayerClan is null and MobileParty.MainParty is null. A hosted model that reads Hero.MainHero without a check (ROTTroopUpgradeModel.DoesPartyHaveRequiredItemsForUpgrade) would throw.",
            "18:16:51.682  [RealmOfThrones.CoopCompat] Host: !!! at the end of game initialization Hero.MainHero is UNREADABLE (NullReferenceException), Clan.PlayerClan is null and MobileParty.MainParty is player_party. A hosted model that reads Hero.MainHero without a check (ROTTroopUpgradeModel.DoesPartyHaveRequiredItemsForUpgrade) would throw.",
            "[RealmOfThrones.CoopCompat] !!! Ship visuals: building a ship visual failed (NullReferenceException)",
            "[SomeOtherMod] Save REFUSED: World check FAILED",
            "18:17:18.541  [RealmOfThrones.CoopCompat] Host: the new world is in the year 299 of Realm of Thrones' calendar.",
            "18:17:18.760  [RealmOfThrones.CoopCompat] World check passed: kingdoms 29, clans 246, settlements 1065 (the map defines 1065), year 299, every town and castle has an owner clan."
        ];
        foreach (var line in lines)
            Assert.Equal(RotWorldState.Waiting, watch.Feed(line));
        Assert.Equal(RotWorldState.Ready, watch.Feed("18:17:19.031  [DedicatedServer] SERVING — coop server up, waiting for clients"));
        Assert.Null(watch.FailedLine);
    }

    // Every "!!! Fresh campaign:" line the module can write stops the watch, so a new one cannot slip past it,
    // and the main hero line is loud only from world ready on, where the watch looks for it.
    [Fact]
    public void Every_loud_fresh_campaign_line_of_the_module_and_the_late_main_hero_line_stop_the_watch()
    {
        var compat = Path.Combine(RepoRoot(), "compat", "RealmOfThronesCoopCompat");
        var lines = Directory.EnumerateFiles(compat, "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)
                && !file.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar))
            .SelectMany(file => System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(file), "\"(!!! Fresh campaign:[^\"]*)\"").Select(match => match.Groups[1].Value))
            .ToList();
        Assert.True(lines.Count >= 9, "found " + lines.Count + " \"!!! Fresh campaign:\" lines");
        foreach (var line in lines)
        {
            var watch = new RotWorldWatch(newWorld: true);
            Assert.True(watch.Feed("[RealmOfThrones.CoopCompat] " + line + "x") == RotWorldState.Failed, line);
        }

        var host = File.ReadAllText(Path.Combine(compat, "Server", "RotHost.cs"));
        Assert.Contains("LogMainHero(\"game start\", early: true);", host);
        Assert.Contains("LogMainHero(\"the end of game initialization\", early: true);", host);
        Assert.Contains("LogMainHero(\"world ready\");", host);
        Assert.Contains("LogMainHero(\"the first daily tick\");", host);
        Assert.Contains("SubModule.Log((loud ? RotHostRules.Loud : \"Host: \") + \"at \" + when + \" Hero.MainHero is \"", host);
        Assert.Contains("public const string Loud = \"Host: !!! \";", File.ReadAllText(Path.Combine(compat, "Server", "RotHostRules.cs")));
        var tick = new RotWorldWatch(newWorld: true);
        Assert.Equal(RotWorldState.Failed, tick.Feed("[RealmOfThrones.CoopCompat] Host: !!! at the first daily tick Hero.MainHero is null, Clan.PlayerClan is null"));
    }

    // ----- Making the server from the Workshop server, War Sails and the overlay (RotServerBuild) -----

    private const string Scene =
        "<scene version=\"1\">\n  <terrain size=\"10\"/>\n  <entities>\n"
        + "    <game_entity name=\"pirates_a\">\n      <transform position=\"5.5, 6, 0\"/>\n"
        + "      <scripts><script name=\"PirateSpawnPoint\"><variables><variable name=\"ClanStringId\" value=\"sea_raiders\"/><variable name=\"Radius\" value=\"12.5\"/></variables></script></scripts>\n"
        + "    </game_entity>\n"
        + "    <game_entity name=\"village_x\">\n      <transform position=\"10,20,0\" rotation_euler=\"0,0,1.5707963267948966\"/>\n"
        + "      <children><game_entity name=\"dock\"><transform position=\"1,2,0\"/><tags><tag name=\"main_map_village_dropoff\"/></tags></game_entity></children>\n"
        + "    </game_entity>\n  </entities>\n</scene>\n";

    // The same scene without its village drop off: the map points step throws half way through a Prepare.
    private const string BrokenScene = "<scene version=\"1\"><entities><game_entity name=\"lonely\"/></entities></scene>";

    private const string OldCommit = "ecd4c479b0c35348c13c3ff4710cea87f799aa40";

    private sealed class ServerPc : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "coodsz-rotbuild-" + Guid.NewGuid().ToString("N"));
        public string Workshop => Path.Combine(Root, "workshop", "3770450698", "DedicatedServer");
        public string Game => Path.Combine(Root, "game");
        public string Server => Path.Combine(Root, RotServerBuild.FolderName);
        public string Work => Server + RotServerBuild.NewFolderSuffix;
        public byte[] Overlay { get; }
        public string Sha => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Overlay));

        public ServerPc()
        {
            File("workshop", "BannerlordCoopServer.exe", "EULA.md", "LICENSE.txt", "Start-ModdedServer.ps1");
            Write(Path.Combine(Workshop, "release-info.txt"), "Paired Coop:  BannerlordCoop commit " + OldCommit + "\r\n");
            File("workshop", "engine/bin/Win64_Shipping_Server/TaleWorlds.Starter.DotNetCore.dll", "engine/bin/Win64_Shipping_Server/0Harmony.dll", "engine/dotnet/dotnet.exe", "engine/XmlSchemas/a.xsd",
                "engine/Modules/Coop/bin/Win64_Shipping_Server/Common.dll", "engine/Modules/Coop/bin/Win64_Shipping_Server/Steamworks.NET.dll",
                "engine/Modules/Coop/bin/Win64_Shipping_Server/TaleWorlds.MountAndBlade.View.dll", "engine/Modules/Coop/bin/Win64_Shipping_Server/Coop.Core.dll.bak");
            System.IO.File.WriteAllBytes(Path.Combine(Workshop, "engine", "bin", "Win64_Shipping_Server", "TaleWorlds.Library.dll"), FakeLibrary());
            Write(Path.Combine(Workshop, "engine", "Parameters", "Version.xml"), "<Version>workshop</Version>");
            foreach (var id in new[] { "Native", "SandBoxCore", "SandBox", "DedicatedServer.Windows", "RBM" })
                Pc.Module(Path.Combine(Workshop, "engine", "Modules", id), id, "v1.4.8", "ModuleData/" + id + ".xml");
            Pc.Module(Path.Combine(Workshop, "engine", "Modules", "Coop"), "Coop", "v0.1.7");

            var naval = Path.Combine(Game, "Modules", "NavalDLC");
            Write(Path.Combine(naval, "SubModule.xml"),
                "<Module><Id value=\"NavalDLC\"/><Version value=\"v1.2.8\"/><ModuleCategory value=\"Singleplayer\"/>"
                + "<DependedModules><DependedModule Id=\"StoryMode\"/></DependedModules><SubModules>"
                + "<SubModule><Name value=\"NavalDLC\"/><DLLName value=\"NavalDLC.dll\"/><Tags><Tag key=\"DedicatedServerType\" value=\"none\"/></Tags></SubModule>"
                + "<SubModule><Name value=\"NavalDLCCustomBattle\"/><DLLName value=\"NavalDLC.CustomBattle.dll\"/></SubModule></SubModules></Module>");
            foreach (var dll in RotServerBuild.NavalDlls.Append("NavalDLC.CustomBattle.dll").Append("NavalDLC.GauntletUI.AutoGenerated.dll"))
                File("game", "Modules/NavalDLC/bin/Win64_Shipping_Client/" + dll);
            File("game", "Modules/NavalDLC/ModuleData/naval.xml", "Modules/NavalDLC/SceneObj/Main_map/terrain.bin", "Modules/NavalDLC/SceneObj/Main_map/TileSets/t.bin",
                "Modules/NavalDLC/SceneObj/Main_map/ShaderCache/s.bin", "Modules/NavalDLC/SceneObj/Other/scene.xscene", "Modules/StoryMode/bin/Win64_Shipping_Client/StoryMode.dll");
            MapScene(Scene);
            Write(Path.Combine(Game, "bin", "Win64_Shipping_Client", "Version.xml"), "<Version>\r\n\t<Singleplayer Value=\"v1.4.8\"/>\r\n</Version>");

            Overlay = Zip(
                ("BannerlordCoopServer.exe", "rot exe"),
                ("release-info.txt", "Paired Coop:  BannerlordCoop commit " + CoopBuildId.Commit(RotStack.CoopBuild.Build) + "\r\n"),
                ("engine/Modules/Coop/SubModule.xml", "<Module><Id value=\"CoopNightly\"/><Version value=\"v0.1.8\"/></Module>"),
                ("engine/Modules/Coop/bin/Win64_Shipping_Server/Common.dll", "rot common"),
                ("engine/Modules/DedicatedServer.Windows/SubModule.xml", "<Module><Id value=\"DedicatedServer.Windows\"/></Module>"),
                ("engine/bin/Win64_Shipping_Server/default_new_game_warsails.sav", "header {\"Modules\":\"Native;SandBoxCore;Sandbox;CoopNightly;NavalDLC\"}"));
        }

        public void MapScene(string text) => Write(Path.Combine(Game, "Modules", "NavalDLC", "SceneObj", "Main_map", "scene.xscene"), text);

        // The drive's free space is a stand in, so no test depends on the real drive.
        public IReadOnlyList<string> Build(Action<string>? progress = null, Func<string, long>? freeSpace = null) =>
            RotServerBuild.Build(Workshop, Game, new MemoryStream(Overlay), Sha, Server, progress, freeSpace ?? (_ => long.MaxValue));

        public string In(string relative) => Path.Combine(Server, relative.Replace('/', Path.DirectorySeparatorChar));

        // Every file under the folder with its bytes, to show the Workshop folder and the game were only read.
        public static Dictionary<string, string> Snapshot(string folder) =>
            Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
                .ToDictionary(file => file, file => Convert.ToHexString(System.IO.File.ReadAllBytes(file)) + "@" + System.IO.File.GetLastWriteTimeUtc(file).Ticks);

        private void File(string where, params string[] files)
        {
            var root = where == "game" ? Game : Workshop;
            foreach (var file in files)
                Write(Path.Combine(root, file.Replace('/', Path.DirectorySeparatorChar)), where + ":" + file);
        }

        private static void Write(string path, string text)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            System.IO.File.WriteAllText(path, text);
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
                Directory.Delete(Root, recursive: true);
        }
    }

    private static byte[] Zip(params (string Entry, string Text)[] entries)
    {
        using var bytes = new MemoryStream();
        using (var zip = new System.IO.Compression.ZipArchive(bytes, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (entry, text) in entries)
            {
                using var writer = new StreamWriter(zip.CreateEntry(entry).Open());
                writer.Write(text);
            }
        }

        return bytes.ToArray();
    }

    // A TaleWorlds.Library.dll stand in with the shape the Version.xml step looks for: VirtualFolders, a nested folder,
    // its Parameters folder and a file in it carrying VirtualFile(name, content).
    private static byte[] FakeLibrary(bool withFolders = true)
    {
        using var assembly = Mono.Cecil.AssemblyDefinition.CreateAssembly(new Mono.Cecil.AssemblyNameDefinition("TaleWorlds.Library", new Version(1, 0)), "TaleWorlds.Library", Mono.Cecil.ModuleKind.Dll);
        var module = assembly.MainModule;
        var attributeBase = module.ImportReference(typeof(Attribute));
        var file = new Mono.Cecil.TypeDefinition("TaleWorlds.Library", "VirtualFileAttribute", Mono.Cecil.TypeAttributes.Public | Mono.Cecil.TypeAttributes.Class, attributeBase);
        var constructor = new Mono.Cecil.MethodDefinition(".ctor", Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.HideBySig | Mono.Cecil.MethodAttributes.SpecialName | Mono.Cecil.MethodAttributes.RTSpecialName, module.TypeSystem.Void);
        constructor.Parameters.Add(new Mono.Cecil.ParameterDefinition(module.TypeSystem.String));
        constructor.Parameters.Add(new Mono.Cecil.ParameterDefinition(module.TypeSystem.String));
        var il = constructor.Body.GetILProcessor();
        il.Emit(Mono.Cecil.Cil.OpCodes.Ldarg_0);
        il.Emit(Mono.Cecil.Cil.OpCodes.Call, module.ImportReference(typeof(Attribute).GetConstructor(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance, Type.EmptyTypes)!));
        il.Emit(Mono.Cecil.Cil.OpCodes.Ret);
        file.Methods.Add(constructor);
        module.Types.Add(file);
        if (withFolders)
        {
            var folders = new Mono.Cecil.TypeDefinition("TaleWorlds.Library", "VirtualFolders", Mono.Cecil.TypeAttributes.Public | Mono.Cecil.TypeAttributes.Class, module.TypeSystem.Object);
            var bin = new Mono.Cecil.TypeDefinition("", "Win64_Shipping_Server", Mono.Cecil.TypeAttributes.NestedPublic | Mono.Cecil.TypeAttributes.Class, module.TypeSystem.Object);
            var parameters = new Mono.Cecil.TypeDefinition("", "Parameters", Mono.Cecil.TypeAttributes.NestedPublic | Mono.Cecil.TypeAttributes.Class, module.TypeSystem.Object);
            var language = new Mono.Cecil.FieldDefinition("Language", Mono.Cecil.FieldAttributes.Public, module.TypeSystem.String);
            var attribute = new Mono.Cecil.CustomAttribute(constructor);
            attribute.ConstructorArguments.Add(new Mono.Cecil.CustomAttributeArgument(module.TypeSystem.String, "language.xml"));
            attribute.ConstructorArguments.Add(new Mono.Cecil.CustomAttributeArgument(module.TypeSystem.String, "<Language/>"));
            language.CustomAttributes.Add(attribute);
            parameters.Fields.Add(language);
            bin.NestedTypes.Add(parameters);
            folders.NestedTypes.Add(bin);
            module.Types.Add(folders);
        }

        using var output = new MemoryStream();
        assembly.Write(output);
        return output.ToArray();
    }

    // The VirtualFile arguments of every field of VirtualFolders' Parameters folder in a library.
    private static List<(string Field, string Name, string Content)> VirtualParameters(string library)
    {
        using var module = Mono.Cecil.ModuleDefinition.ReadModule(library, new Mono.Cecil.ReaderParameters { InMemory = true });
        var parameters = module.Types.Single(type => type.Name == "VirtualFolders").NestedTypes.Single().NestedTypes.Single(type => type.Name == "Parameters");
        return parameters.Fields.Select(field => (field.Name, (string)field.CustomAttributes[0].ConstructorArguments[0].Value, (string)field.CustomAttributes[0].ConstructorArguments[1].Value)).ToList();
    }

    [Fact]
    public void A_first_prepare_makes_the_server_from_the_workshop_server_war_sails_from_the_game_and_the_overlay_on_top()
    {
        using var pc = new ServerPc();
        var workshopBefore = ServerPc.Snapshot(Path.Combine(pc.Root, "workshop"));
        var gameBefore = ServerPc.Snapshot(pc.Game);

        var lines = pc.Build();

        Assert.Equal(new[]
        {
            RotServerBuild.CopiedLine(14, 0, pc.Server), RotServerBuild.NavalLine, "Put Coop " + RotStack.CoopBuild.Label + " into the server."
        }, lines);
        Assert.False(Directory.Exists(pc.Work));
        Assert.Equal(new RotServerRecord(RotStack.CoopBuild.Build, pc.Sha, Done: true), RotServerBuild.ReadRecord(pc.Server));
        Assert.False(RotServerBuild.IsUnfinished(pc.Server));
        Assert.Empty(RotServerBuild.WarSailsGaps(pc.Server));
        // The Workshop folder and the game were only read.
        Assert.Equal(workshopBefore, ServerPc.Snapshot(Path.Combine(pc.Root, "workshop")));
        Assert.Equal(gameBefore, ServerPc.Snapshot(pc.Game));

        // The whitelist only: no mod another tool put in the Workshop server, no .bak, no script.
        Assert.True(File.Exists(pc.In("engine/dotnet/dotnet.exe")));
        Assert.True(File.Exists(pc.In("engine/XmlSchemas/a.xsd")));
        Assert.True(File.Exists(pc.In("engine/Modules/Native/ModuleData/Native.xml")));
        Assert.False(Directory.Exists(pc.In("engine/Modules/RBM")));
        Assert.False(File.Exists(pc.In("engine/Modules/Coop/bin/Win64_Shipping_Server/Coop.Core.dll.bak")));
        Assert.False(File.Exists(pc.In("Start-ModdedServer.ps1")));
        Assert.False(File.Exists(pc.In("engine/bin/Win64_Shipping_Server/0Harmony.dll")));
        // The overlay over it: Coop's own files replaced, the Workshop Coop's game view and Steam DLLs kept.
        Assert.Equal("rot exe", File.ReadAllText(pc.In("BannerlordCoopServer.exe")));
        Assert.Equal("rot common", File.ReadAllText(pc.In("engine/Modules/Coop/bin/Win64_Shipping_Server/Common.dll")));
        Assert.Contains("CoopNightly", File.ReadAllText(pc.In("engine/Modules/Coop/SubModule.xml")));
        Assert.True(File.Exists(pc.In("engine/Modules/Coop/bin/Win64_Shipping_Server/Steamworks.NET.dll")));
        Assert.True(File.Exists(pc.In("engine/Modules/Coop/bin/Win64_Shipping_Server/TaleWorlds.MountAndBlade.View.dll")));
        Assert.Equal("workshop:EULA.md", File.ReadAllText(pc.In("EULA.md")));

        // War Sails from the game: the DLLs with StoryMode.dll, no custom battle or generated DLL, ModuleData, the map without tiles.
        var bin = pc.In("engine/Modules/NavalDLC/bin/Win64_Shipping_Server");
        Assert.Equal(RotServerBuild.NavalDlls.Append("StoryMode.dll").Order(), Directory.EnumerateFiles(bin).Select(Path.GetFileName).Order()!);
        Assert.Equal("game:Modules/StoryMode/bin/Win64_Shipping_Client/StoryMode.dll", File.ReadAllText(Path.Combine(bin, "StoryMode.dll")));
        Assert.True(File.Exists(pc.In("engine/Modules/NavalDLC/ModuleData/naval.xml")));
        var map = pc.In("engine/Modules/NavalDLC/SceneObj/Main_map");
        Assert.Equal(new[] { "naval_map_points.txt", "scene.xscene", "terrain.bin" }, Directory.EnumerateFileSystemEntries(map).Select(Path.GetFileName).Order()!);
        Assert.False(Directory.Exists(pc.In("engine/Modules/NavalDLC/SceneObj/Other")));
        Assert.Equal(new[]
        {
            "# GENERATED by War Sails staging from NavalDLC SceneObj/Main_map/scene.xscene (global positions)",
            "pirate_spawn sea_raiders 5.5 6 12.5",
            "village_dropoff village_x 8 21"
        }, File.ReadAllLines(Path.Combine(map, "naval_map_points.txt")));
        var scene = File.ReadAllText(Path.Combine(map, "scene.xscene"));
        Assert.DoesNotContain("game_entity", scene);
        Assert.DoesNotContain("terrain", scene);
        Assert.Contains("<entities", scene);
        var subModule = File.ReadAllText(pc.In("engine/Modules/NavalDLC/SubModule.xml"));
        Assert.Contains("<Name value=\"NavalDLC\" />", subModule);
        foreach (var gone in new[] { "NavalDLCCustomBattle", "Tags", "DependedModules", "ModuleCategory" })
            Assert.DoesNotContain(gone, subModule);

        // The game's Version.xml in Parameters and in TaleWorlds.Library.dll as a virtual file next to the folder's own.
        var version = File.ReadAllText(Path.Combine(pc.Game, "bin", "Win64_Shipping_Client", "Version.xml"));
        Assert.Equal(version, File.ReadAllText(pc.In("engine/Parameters/Version.xml")));
        Assert.Equal(new[] { ("Language", "language.xml", "<Language/>"), ("Version", "Version.xml", version) },
            VirtualParameters(pc.In("engine/bin/Win64_Shipping_Server/TaleWorlds.Library.dll")));

        // The server's two build marks are the RoT build's.
        Assert.Null(RotServerPath.ServerBuildProblem(pc.Server, dll => File.ReadAllText(dll) == "rot common" ? RotStack.CoopBuild.Build : null));
    }

    [Fact]
    public void A_damaged_overlay_a_missing_workshop_server_or_missing_war_sails_writes_nothing()
    {
        using var pc = new ServerPc();
        var damaged = Assert.Throws<InvalidDataException>(() => RotServerBuild.Build(pc.Workshop, pc.Game, new MemoryStream(pc.Overlay), new string('0', 64), pc.Server));
        Assert.Equal(RotServerBuild.Damaged, damaged.Message);

        var noWorkshop = Assert.Throws<InvalidOperationException>(() => RotServerBuild.Build(Path.Combine(pc.Root, "nothing"), pc.Game, new MemoryStream(pc.Overlay), pc.Sha, pc.Server));
        Assert.Equal(BannerlordCoopLauncher.Core.Support.AntivirusText.NoWorkshopCoop("Prepare server"), noWorkshop.Message);
        // A server folder inside the Workshop folder is refused too.
        Assert.Equal(RotServerBuild.InsideWorkshop,
            Assert.Throws<InvalidOperationException>(() => RotServerBuild.Build(pc.Workshop, pc.Game, new MemoryStream(pc.Overlay), pc.Sha, Path.Combine(pc.Workshop, "server"))).Message);
        Assert.False(Directory.Exists(Path.Combine(pc.Workshop, "server")));

        File.Delete(Path.Combine(pc.Game, "Modules", "StoryMode", "bin", "Win64_Shipping_Client", "StoryMode.dll"));
        var file = Assert.Throws<InvalidOperationException>(() => pc.Build());
        Assert.Equal(RotServerBuild.GameFileMissing(Path.Combine("Modules", "StoryMode", "bin", "Win64_Shipping_Client", "StoryMode.dll")), file.Message);
        Directory.Delete(Path.Combine(pc.Game, "Modules", "NavalDLC"), recursive: true);
        Assert.Equal(RotServerBuild.NoWarSails, RotServerBuild.GameProblem(pc.Game));
        Assert.Equal(RotServerBuild.NoWarSails, Assert.Throws<InvalidOperationException>(() => pc.Build()).Message);

        Assert.False(Directory.Exists(pc.Server));
        Assert.False(Directory.Exists(pc.Work));
    }

    [Fact]
    public void A_prepare_cut_off_half_way_is_never_taken_for_a_server_and_the_next_one_finishes_it()
    {
        using var pc = new ServerPc();
        // The first build stops in the War Sails step: the server folder does not exist, the work folder says not done.
        pc.MapScene(BrokenScene);
        var cut = Assert.Throws<InvalidOperationException>(() => pc.Build());
        Assert.Equal(RotServerBuild.PartsMissing("NavalDLC Main_map"), cut.Message);
        Assert.False(Directory.Exists(pc.Server));
        Assert.Equal(new RotServerRecord(RotStack.CoopBuild.Build, pc.Sha, Done: false), RotServerBuild.ReadRecord(pc.Work));

        // The next Prepare goes on in the work folder (the Workshop files are already there) and renames it.
        pc.MapScene(Scene);
        Assert.Equal(RotServerBuild.CopiedLine(0, 14, pc.Server), pc.Build()[0]);
        Assert.False(Directory.Exists(pc.Work));
        Assert.True(RotServerBuild.ReadRecord(pc.Server)!.Done);

        // An update cut off half way leaves the server marked as not done, so Start server refuses it until Prepare finishes.
        pc.MapScene(BrokenScene);
        Assert.Throws<InvalidOperationException>(() => pc.Build());
        Assert.True(RotServerBuild.IsUnfinished(pc.Server));
        pc.MapScene(Scene);
        pc.Build();
        Assert.False(RotServerBuild.IsUnfinished(pc.Server));
        Assert.Empty(RotServerBuild.WarSailsGaps(pc.Server));
    }

    [Fact]
    public void A_second_prepare_updates_the_server_in_place_and_keeps_the_rot_modules()
    {
        using var pc = new ServerPc();
        pc.Build();
        // Realm of Thrones staged into the server, a tile folder an older stage left in NavalDLC, a Workshop file Coop updated.
        Pc.Module(pc.In("engine/Modules/ROT-Core"), "ROT-Core", "v8.1.8");
        Directory.CreateDirectory(RotServerPath.StampFolder(pc.Server));
        File.WriteAllText(Path.Combine(RotServerPath.StampFolder(pc.Server), "ROT-Core.json"), "{}");
        Directory.CreateDirectory(pc.In("engine/Modules/NavalDLC/SceneObj/Main_map/TileSets"));
        File.WriteAllText(pc.In("engine/Modules/NavalDLC/SceneObj/Main_map/TileSets/old.bin"), "old");
        File.WriteAllText(Path.Combine(pc.Workshop, "engine", "Modules", "Native", "ModuleData", "Native.xml"), "native updated");
        File.WriteAllText(Path.Combine(pc.Workshop, "engine", "Modules", "Coop", "bin", "Win64_Shipping_Server", "Common.dll"), "workshop common updated");

        var lines = pc.Build();

        Assert.Equal(RotServerBuild.CopiedLine(1, 13, pc.Server), lines[0]);
        Assert.Equal("native updated", File.ReadAllText(pc.In("engine/Modules/Native/ModuleData/Native.xml")));
        // The overlay's Coop is never replaced by the Workshop's, and the library gets one virtual Version.xml.
        Assert.Equal("rot common", File.ReadAllText(pc.In("engine/Modules/Coop/bin/Win64_Shipping_Server/Common.dll")));
        Assert.Single(VirtualParameters(pc.In("engine/bin/Win64_Shipping_Server/TaleWorlds.Library.dll")), field => field.Name == "Version.xml");
        Assert.True(File.Exists(pc.In("engine/Modules/ROT-Core/SubModule.xml")));
        Assert.True(File.Exists(Path.Combine(RotServerPath.StampFolder(pc.Server), "ROT-Core.json")));
        Assert.False(Directory.Exists(pc.In("engine/Modules/NavalDLC/SceneObj/Main_map/TileSets")));
        Assert.True(RotServerBuild.ReadRecord(pc.Server)!.Done);
    }

    [Fact]
    public void A_server_folder_with_other_files_is_left_alone()
    {
        using var pc = new ServerPc();
        Directory.CreateDirectory(pc.Server);
        File.WriteAllText(Path.Combine(pc.Server, "mine.txt"), "the host's");
        Assert.Equal(RotServerBuild.Taken(Path.GetFullPath(pc.Server)), Assert.Throws<InvalidOperationException>(() => pc.Build()).Message);
        Assert.Equal(new[] { Path.Combine(pc.Server, "mine.txt") }, Directory.EnumerateFileSystemEntries(pc.Server));

        // A work folder with files and no record of ours is not taken for a cut off Prepare either.
        Directory.Delete(pc.Server, recursive: true);
        Directory.CreateDirectory(pc.Work);
        File.WriteAllText(Path.Combine(pc.Work, "mine.txt"), "the host's");
        Assert.Equal(RotServerBuild.Taken(Path.GetFullPath(pc.Work)), Assert.Throws<InvalidOperationException>(() => pc.Build()).Message);
        Assert.Equal(new[] { Path.Combine(pc.Work, "mine.txt") }, Directory.EnumerateFileSystemEntries(pc.Work));
        Assert.False(Directory.Exists(pc.Server));

        // A library without VirtualFolders cannot carry the Version.xml, and that is a plain line too.
        Assert.Equal(RotServerBuild.PartsMissing("TaleWorlds.Library.dll"),
            Assert.Throws<InvalidOperationException>(() => RotServerBuild.AddVirtualVersionXml(FakeLibrary(withFolders: false), "<Version/>")).Message);
    }

    [Fact]
    public void A_drive_without_the_free_space_gets_a_line_before_anything_is_written_and_an_update_needs_only_what_it_still_copies()
    {
        using var pc = new ServerPc();
        var asked = new List<string>();
        var none = Assert.Throws<InvalidOperationException>(() => pc.Build(freeSpace: path => { asked.Add(path); return 0; }));
        // The fixture's few bytes and the margin round up to 2 GB.
        Assert.Equal(RotServerBuild.NoSpace(Path.GetPathRoot(Path.GetFullPath(pc.Server))!.TrimEnd('\\').TrimEnd(':'), 2), none.Message);
        Assert.Equal(new[] { Path.GetFullPath(pc.Server) }, asked);
        // The margin alone is not enough for a first build, which copies everything.
        Assert.Throws<InvalidOperationException>(() => pc.Build(freeSpace: _ => RotServerBuild.SpaceMargin));
        Assert.False(Directory.Exists(pc.Server));
        Assert.False(Directory.Exists(pc.Work));

        var steps = new List<string>();
        pc.Build(steps.Add);
        Assert.Equal(new[] { RotServerBuild.CopyingLine(Path.GetFullPath(pc.Server)), RotServerBuild.PuttingNaval, RotServerBuild.PuttingOverlay }, steps);
        // An update with nothing new to copy only needs the margin.
        pc.Build(freeSpace: _ => RotServerBuild.SpaceMargin);
        Assert.True(RotServerBuild.ReadRecord(pc.Server)!.Done);
    }

    [Fact]
    public void A_damaged_game_file_or_library_is_a_plain_line()
    {
        using var pc = new ServerPc();
        var map = RotServerBuild.PartsMissing("NavalDLC Main_map");
        pc.MapScene("<scene><entities>");
        Assert.Equal(map, Assert.Throws<InvalidOperationException>(() => pc.Build()).Message);
        pc.MapScene(Scene.Replace("5.5, 6, 0", "5.5, six, 0"));
        Assert.Equal(map, Assert.Throws<InvalidOperationException>(() => pc.Build()).Message);
        pc.MapScene(Scene.Replace("5.5, 6, 0", "5.5, 6"));
        Assert.Equal(map, Assert.Throws<InvalidOperationException>(() => pc.Build()).Message);

        pc.MapScene(Scene);
        File.WriteAllText(Path.Combine(pc.Game, "Modules", "NavalDLC", "SubModule.xml"), "<Module><Id value=\"NavalDLC\"");
        Assert.Equal(RotServerBuild.PartsMissing("NavalDLC module"), Assert.Throws<InvalidOperationException>(() => pc.Build()).Message);

        var library = RotServerBuild.PartsMissing("TaleWorlds.Library.dll");
        Assert.Equal(library, Assert.Throws<InvalidOperationException>(() => RotServerBuild.AddVirtualVersionXml("not a dll"u8.ToArray(), "<Version/>")).Message);
        Assert.Equal(library, Assert.Throws<InvalidOperationException>(() => RotServerBuild.AddVirtualVersionXml(FakeLibrary()[..300], "<Version/>")).Message);
    }

    [Fact]
    public void Read_only_files_never_stop_a_prepare_and_no_temporary_file_stays()
    {
        using var pc = new ServerPc();
        pc.Build();
        // Read-only copies in the server: a Workshop file, an overlay file and War Sails' SubModule.xml, each to be written again.
        File.WriteAllText(Path.Combine(pc.Workshop, "EULA.md"), "workshop:EULA.md updated");
        File.WriteAllText(pc.In("engine/Modules/Coop/bin/Win64_Shipping_Server/Common.dll"), "changed by hand");
        File.WriteAllText(pc.In("engine/Modules/NavalDLC/SubModule.xml"), "changed by hand");
        foreach (var file in new[] { "EULA.md", "engine/Modules/Coop/bin/Win64_Shipping_Server/Common.dll", "engine/Modules/NavalDLC/SubModule.xml" })
            File.SetAttributes(pc.In(file), FileAttributes.ReadOnly);
        // A read-only game file, whose copy would be read-only too.
        var gameDll = Path.Combine(pc.Game, "Modules", "NavalDLC", "bin", "Win64_Shipping_Client", "NavalDLC.dll");
        File.WriteAllText(gameDll, "game:NavalDLC.dll updated");
        File.SetAttributes(gameDll, FileAttributes.ReadOnly);
        try
        {
            pc.Build();
            pc.Build();
        }
        finally
        {
            File.SetAttributes(gameDll, FileAttributes.Normal);
        }

        Assert.Equal("workshop:EULA.md updated", File.ReadAllText(pc.In("EULA.md")));
        Assert.Equal("rot common", File.ReadAllText(pc.In("engine/Modules/Coop/bin/Win64_Shipping_Server/Common.dll")));
        Assert.Contains("<Name value=\"NavalDLC\" />", File.ReadAllText(pc.In("engine/Modules/NavalDLC/SubModule.xml")));
        var copy = pc.In("engine/Modules/NavalDLC/bin/Win64_Shipping_Server/NavalDLC.dll");
        Assert.Equal("game:NavalDLC.dll updated", File.ReadAllText(copy));
        Assert.Equal(0, (int)(File.GetAttributes(copy) & FileAttributes.ReadOnly));
        Assert.Empty(Directory.EnumerateFiles(pc.Server, "*.tmp", SearchOption.AllDirectories));
        Assert.True(RotServerBuild.ReadRecord(pc.Server)!.Done);
    }

    [Fact]
    public void A_file_in_the_servers_coop_that_neither_the_overlay_nor_the_workshop_has_is_removed_and_nothing_else()
    {
        using var pc = new ServerPc();
        var bin = Path.Combine("engine", "Modules", "Coop", "bin", "Win64_Shipping_Server");
        var parked = Path.Combine(bin, "Coop.Core.dll.coodsz-password");
        File.WriteAllText(Path.Combine(pc.Workshop, parked), "a TAOM tool parked this");
        pc.Build();
        // The Workshop leftover never goes in.
        Assert.False(File.Exists(Path.Combine(pc.Server, parked)));

        // A DLL an older overlay had, the leftover an earlier Prepare copied, a file outside Coop and a junction inside Coop.
        var stale = Path.Combine(bin, "Coop.Old.dll");
        File.WriteAllText(Path.Combine(pc.Server, stale), "older overlay");
        File.SetAttributes(Path.Combine(pc.Server, stale), FileAttributes.ReadOnly);
        File.WriteAllText(Path.Combine(pc.Server, parked), "an earlier copy");
        Pc.Module(pc.In("engine/Modules/ROT-Core"), "ROT-Core", "v8.1.8");
        var outside = Path.Combine(pc.Root, "outside");
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "precious.txt"), "keep");
        var link = pc.In("engine/Modules/Coop/linked");
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", "/c mklink /J \"" + link + "\" \"" + outside + "\"")
            { CreateNoWindow = true, UseShellExecute = false })!.WaitForExit();
        Assert.True(Directory.Exists(link));
        try
        {
            var lines = pc.Build();

            Assert.Equal(new[] { RotServerBuild.RemovedLine(stale), RotServerBuild.RemovedLine(parked) }.Order(), lines.Where(line => line.StartsWith("Removed ")).Order());
            Assert.False(File.Exists(Path.Combine(pc.Server, stale)));
            Assert.False(File.Exists(Path.Combine(pc.Server, parked)));
            Assert.True(File.Exists(Path.Combine(outside, "precious.txt")));
            Assert.True(File.Exists(pc.In("engine/Modules/ROT-Core/SubModule.xml")));
            Assert.Equal("rot common", File.ReadAllText(pc.In("engine/Modules/Coop/bin/Win64_Shipping_Server/Common.dll")));
            Assert.True(File.Exists(pc.In("engine/Modules/Coop/bin/Win64_Shipping_Server/Steamworks.NET.dll")));
            Assert.True(File.Exists(pc.In("engine/Modules/Coop/SubModule.xml")));
            // The Workshop folder is only read.
            Assert.Equal("a TAOM tool parked this", File.ReadAllText(Path.Combine(pc.Workshop, parked)));
        }
        finally
        {
            Directory.Delete(link);
        }
    }

    [Fact]
    public void The_server_check_wants_both_build_marks_of_the_rot_build_when_the_tool_carries_it()
    {
        using var pc = new ServerPc();
        pc.Build();
        string? Rot(string dll) => RotStack.CoopBuild.Build;
        Assert.Null(RotServerPath.ServerBuildProblem(pc.Server, Rot));
        // Common.dll of another build, or release-info.txt pairing another commit, is not the RoT build.
        Assert.Equal(RotServerPath.NotRotBuild, RotServerPath.ServerBuildProblem(pc.Server, _ => "0.1.8+" + OldCommit));
        File.WriteAllText(pc.In("release-info.txt"), "Paired Coop:  BannerlordCoop commit " + OldCommit + "\r\n");
        Assert.Equal(RotServerPath.NotRotBuild, RotServerPath.ServerBuildProblem(pc.Server, Rot));
        Assert.Equal("The server's Coop is not the RoT build this tool carries. Click Prepare server to put it in.", RotServerPath.NotRotBuild);

        // Problems names it first when the tool carries the build, and keeps the lines of before when it does not.
        using var rot = new Pc();
        var plan = RotServerPath.Plan(Plain(), rot.Sources, []);
        Assert.Equal(RotServerPath.NotRotBuild, Assert.Single(RotServerPath.Problems(plan, rot.Server, carried: true)));
        Assert.Equal(RotServerPath.CoopBuildProblem(null), Assert.Single(RotServerPath.Problems(plan, rot.Server)));
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "BannerlordCoopLauncher.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
