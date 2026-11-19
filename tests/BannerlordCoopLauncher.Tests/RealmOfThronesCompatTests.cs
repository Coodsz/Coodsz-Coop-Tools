using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using HarmonyLib;
using RealmOfThronesCoopCompat;

namespace BannerlordCoopLauncher.Tests;

// RealmOfThrones.CoopCompat: when the module runs and what its start line says (RotRules.cs), the
// map data file and the staged map checks (MapData.cs), the shape based rewrite of the dedicated
// server's map calls (MapRedirectRules.cs, run with Harmony on stand ins shaped like the server's
// map scene) and the crash trace's log rules (CrashTraceRules.cs), all linked.
public sealed class RealmOfThronesCompatTests
{
    private static readonly string[] Coop = { "Native", "SandBoxCore", "Sandbox", "CoopNightly", "NavalDLC", "ROT-Core", "ROT-Content", "ROT_Map" };

    [Fact]
    public void Single_player_and_a_game_without_the_map_are_left_alone()
    {
        Assert.False(RotRules.ShouldRun(false, new[] { "Native", "Sandbox", "ROT-Core", "ROT_Map" }));
        Assert.False(RotRules.ShouldRun(false, new[] { "Native", "Sandbox", "CoopNightly" }));
        Assert.False(RotRules.ShouldRun(true, new[] { "Native", "Sandbox", "CoopNightly", "NavalDLC" }));
        Assert.False(RotRules.ShouldRun(true, null));
    }

    [Fact]
    public void The_server_and_a_coop_game_with_the_map_run()
    {
        Assert.True(RotRules.ShouldRun(true, Coop));
        Assert.True(RotRules.ShouldRun(false, Coop));
        Assert.True(RotRules.ShouldRun(false, new[] { "Coop", "rot_map" }));
    }

    [Fact]
    public void The_server_is_told_by_its_bin_folder()
    {
        Assert.True(RotRules.IsServerBinFolder(@"C:\cdst\engine\bin\Win64_Shipping_Server\"));
        Assert.False(RotRules.IsServerBinFolder(@"C:\Games\Bannerlord\bin\Win64_Shipping_Client\"));
        Assert.False(RotRules.IsServerBinFolder(null));
    }

    [Fact]
    public void The_start_line_names_the_process_coop_war_sails_modules_and_the_rot_dlls()
    {
        var modules = Coop.Select(id => new KeyValuePair<string, string>(id, id == "CoopNightly" ? "v0.1.8.0" : "v1.4.8.0")).ToList();
        var line = RotRules.StartLine("v0.1.0", true, modules, new[] { "TaleWorlds.Core", "BannerColorPersistence" });

        Assert.Contains("on the dedicated server", line);
        Assert.Contains("Coop module CoopNightly v0.1.8.0", line);
        Assert.Contains("War Sails on", line);
        Assert.Contains("ROT_Map v1.4.8.0", line);
        Assert.Contains("ROT.dll loaded no", line);
        Assert.Contains("BannerColorPersistence.dll loaded yes", line);

        var client = RotRules.StartLine("v0.1.0", false, modules.Where(m => m.Key != "NavalDLC" && m.Key != "CoopNightly").ToList(), null);
        Assert.Contains("on the game client", client);
        Assert.Contains("Coop module none", client);
        Assert.Contains("War Sails off", client);
    }

    [Fact]
    public void The_shipped_map_file_holds_the_numbers_read_from_the_rot_scene()
    {
        var text = File.ReadAllText(Path.Combine(RepoRoot(), "compat", "RealmOfThronesCoopCompat", "ModuleData", MapData.FileName));
        var data = MapData.Parse(text, out var error);

        Assert.Null(error);
        Assert.NotNull(data);
        Assert.Equal("ROT_Map", data!.Module);
        Assert.Equal(new object[] { 0f, 0f, 1599.99f, 1400.3f, 620f, 1600f }, data.ExtentsValues);
    }

    [Theory]
    [InlineData("border_min_x=0\nborder_min_y=0\nborder_max_x=10\nborder_max_y=10\nborder_max_height=5\nterrain_size=10", "no module")]
    [InlineData("module=ROT_Map\nborder_min_x=0\nborder_min_y=0\nborder_max_x=10\nborder_max_y=10\nterrain_size=10", "border_max_height")]
    [InlineData("module=ROT_Map\nborder_min_x=0\nborder_min_y=0\nborder_max_x=ten\nborder_max_y=10\nborder_max_height=5\nterrain_size=10", "border_max_x")]
    [InlineData("module=ROT_Map\nborder_min_x=10\nborder_min_y=0\nborder_max_x=10\nborder_max_y=10\nborder_max_height=5\nterrain_size=10", "not below")]
    [InlineData("module=ROT_Map\nborder_min_x=0\nborder_min_y=0\nborder_max_x=1599\nborder_max_y=1400\nborder_max_height=5\nterrain_size=1040", "does not cover")]
    [InlineData("module=ROT_Map\njunk", "not key=value")]
    // The weather grid index is (int)(position / (terrain / 32)), so a border max equal to the
    // terrain size lands one past the grid and a negative min goes below it.
    [InlineData("module=ROT_Map\nborder_min_x=0\nborder_min_y=0\nborder_max_x=1600\nborder_max_y=1400.3\nborder_max_height=620\nterrain_size=1600", "does not cover")]
    [InlineData("module=ROT_Map\nborder_min_x=0\nborder_min_y=0\nborder_max_x=1599.99\nborder_max_y=1600\nborder_max_height=620\nterrain_size=1600", "does not cover")]
    [InlineData("module=ROT_Map\nborder_min_x=-0.001\nborder_min_y=0\nborder_max_x=1599.99\nborder_max_y=1400.3\nborder_max_height=620\nterrain_size=1600", "below zero")]
    [InlineData("module=ROT_Map\nborder_min_x=0\nborder_min_y=-5\nborder_max_x=1599.99\nborder_max_y=1400.3\nborder_max_height=620\nterrain_size=1600", "below zero")]
    public void A_bad_map_file_is_refused_with_the_reason(string text, string reason)
    {
        Assert.Null(MapData.Parse(text, out var error));
        Assert.Contains(reason, error);
    }

    [Fact]
    public void A_border_from_zero_to_just_inside_the_terrain_is_accepted()
    {
        var data = MapData.Parse("module=ROT_Map\nborder_min_x=0\nborder_min_y=0\nborder_max_x=1599.99\nborder_max_y=1400.3\nborder_max_height=620\nterrain_size=1600", out var error);
        Assert.Null(error);
        Assert.NotNull(data);
    }

    [Fact]
    public void The_rot_module_needs_a_stripped_scene_its_own_caches_and_with_war_sails_the_points_and_wind()
    {
        var module = Path.Combine("x", "ROT_Map");
        var map = Path.Combine(module, "SceneObj", "Main_map");
        var caches = Path.Combine(module, "ModuleData", "DistanceCaches");
        string Cache(string kind) => Path.Combine(caches, "settlements_distance_cache_" + kind + ".bin");
        var all = new HashSet<string>
        {
            Path.Combine(map, "scene.xscene"), Path.Combine(map, "naval_map_points.txt"), Path.Combine(map, "flowmap.dds"),
            Cache("Default"), Cache("Naval"), Cache("All"),
        };
        HashSet<string> Without(string end) => new HashSet<string>(all.Where(path => !path.EndsWith(end)));

        Assert.Empty(MapData.MapFolderProblems(module, true, all.Contains, _ => true));
        Assert.Contains("still has terrain", Assert.Single(MapData.MapFolderProblems(module, true, all.Contains, _ => false)));
        Assert.Contains("flowmap.dds", Assert.Single(MapData.MapFolderProblems(module, true, Without("flowmap.dds").Contains, _ => true)));
        Assert.Empty(MapData.MapFolderProblems(module, false, Without("flowmap.dds").Contains, _ => true));
        Assert.Contains("scene.xscene", Assert.Single(MapData.MapFolderProblems(module, false, path => path != Path.Combine(map, "scene.xscene") && all.Contains(path), _ => true)));

        // A cache Realm of Thrones lacks would be read from SandBox or NavalDLC, so it blocks.
        Assert.Contains("settlements_distance_cache_Default.bin", Assert.Single(MapData.MapFolderProblems(module, false, Without("_Default.bin").Contains, _ => true)));
        Assert.Contains("settlements_distance_cache_Naval.bin", Assert.Single(MapData.MapFolderProblems(module, true, Without("_Naval.bin").Contains, _ => true)));
        Assert.Contains("settlements_distance_cache_All.bin", Assert.Single(MapData.MapFolderProblems(module, true, Without("_All.bin").Contains, _ => true)));
        Assert.Empty(MapData.MapFolderProblems(module, false, Without("_Naval.bin").Contains, _ => true));
    }

    [Fact]
    public void Only_a_scene_without_terrain_and_entities_counts_as_stripped()
    {
        const string stripped = "<?xml version=\"1.0\"?><scene name=\"Main_map\"><levels/><entities>\n</entities><Paths><path name=\"r\"/></Paths></scene>";
        const string withTerrain = "<?xml version=\"1.0\"?><scene name=\"Main_map\"><entities/><terrain node_size=\"100\"/></scene>";
        const string withEntity = "<?xml version=\"1.0\"?><scene name=\"Main_map\"><entities><game_entity name=\"border_min\"/></entities></scene>";

        Assert.True(MapData.SceneIsStripped(new StringReader(stripped)));
        Assert.False(MapData.SceneIsStripped(new StringReader(withTerrain)));
        Assert.False(MapData.SceneIsStripped(new StringReader(withEntity)));
    }

    [Fact]
    public void A_distance_cache_from_another_module_is_read_from_rot_when_rot_has_it()
    {
        var rot = Path.Combine("Modules", "ROT_Map");
        var rotDefault = Path.Combine(rot, "ModuleData", "DistanceCaches", "settlements_distance_cache_Default.bin");
        var sandBox = Path.Combine("Modules", "SandBox", "ModuleData", "DistanceCaches", "settlements_distance_cache_Default.bin");
        var sandBoxNaval = Path.Combine("Modules", "NavalDLC", "ModuleData", "DistanceCaches", "settlements_distance_cache_Naval.bin");

        Assert.Equal(rotDefault, MapData.CacheRedirect(sandBox, rot, path => path == rotDefault));
        Assert.Null(MapData.CacheRedirect(rotDefault, rot, path => path == rotDefault));
        Assert.Null(MapData.CacheRedirect(sandBoxNaval, rot, path => path == rotDefault));
        Assert.Null(MapData.CacheRedirect(Path.Combine("Modules", "SandBox", "ModuleData", "other.bin"), rot, _ => true));
        Assert.Null(MapData.CacheRedirect(sandBox, null, _ => true));
    }

    // ---- the map call rewrite, on stand ins shaped like the server's map scene ----

    internal readonly record struct StandInExtents(float BorderMinX, float BorderMinY, float BorderMaxX, float BorderMaxY, float BorderMaxHeight, float TerrainSize)
    {
        internal static readonly StandInExtents Land = new(62f, 30f, 790f, 640f, 620f, 848f);
        internal static readonly StandInExtents Naval = new(13.353f, 6.733f, 992.535f, 937.948f, 756.484f, 1040f);

        internal static StandInExtents For(bool warSails) => warSails ? Naval : Land;

        internal static string? ModuleFor(bool warSails) => warSails ? "NavalDLC" : null;
    }

    // A value type with the same static shape but no six float constructor is not the extents.
    internal readonly struct NotExtents
    {
        internal static NotExtents For(bool warSails) => default;
    }

    internal sealed class StandInScene
    {
        public string? Module;
        public StandInExtents Extents;

        [MethodImpl(MethodImplOptions.NoInlining)]
        public void Load(bool warSails)
        {
            Module = StandInExtents.ModuleFor(warSails);
            Extents = StandInExtents.For(warSails);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public void LoadWithoutExtents() => Module = "x" + NotExtents.For(true).GetHashCode();
    }

    public static string? RotModule(string? original) => "ROT_Map";

    public static object RotExtents(object original) =>
        MapRedirectRules.ExtentsConstructor(original.GetType())!.Invoke(new object[] { 0f, 0f, 1599.99f, 1400.3f, 620f, 1600f });

    private static IEnumerable<CodeInstruction> Rewrite(IEnumerable<CodeInstruction> instructions) =>
        MapRedirectRules.Rewrite(instructions, typeof(StandInExtents), Hook(nameof(RotModule)), Hook(nameof(RotExtents)), out _, out _);

    private static MethodInfo Hook(string name) => typeof(RealmOfThronesCompatTests).GetMethod(name)!;

    private static List<CodeInstruction> Original(string method) =>
        PatchProcessor.GetOriginalInstructions(typeof(StandInScene).GetMethod(method)!);

    [Fact]
    public void The_extents_type_and_both_calls_are_found_by_shape()
    {
        var original = Original(nameof(StandInScene.Load));

        Assert.Equal(typeof(StandInExtents), MapRedirectRules.FindExtentsType(original));
        MapRedirectRules.Rewrite(original, typeof(StandInExtents), Hook(nameof(RotModule)), Hook(nameof(RotExtents)), out var moduleCalls, out var extentsCalls);
        Assert.Equal(1, moduleCalls);
        Assert.Equal(1, extentsCalls);
    }

    [Fact]
    public void Nothing_is_found_or_changed_when_the_calls_are_not_there()
    {
        var original = Original(nameof(StandInScene.LoadWithoutExtents));

        Assert.Null(MapRedirectRules.FindExtentsType(original));
        var rewritten = MapRedirectRules.Rewrite(original, null, Hook(nameof(RotModule)), Hook(nameof(RotExtents)), out var moduleCalls, out var extentsCalls);
        Assert.Equal(0, moduleCalls + extentsCalls);
        Assert.Equal(original.Count, rewritten.Count);
    }

    [Fact]
    public void Two_candidate_extents_types_count_as_none()
    {
        var calls = new[]
        {
            new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(StandInExtents), nameof(StandInExtents.For))),
            new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(OtherExtents), nameof(OtherExtents.For))),
        };
        Assert.Null(MapRedirectRules.FindExtentsType(calls));
    }

    internal readonly record struct OtherExtents(float A, float B, float C, float D, float E, float F)
    {
        internal static OtherExtents For(bool warSails) => default;
    }

    [Fact]
    public void Patched_with_harmony_the_scene_gets_rot_module_and_extents_and_war_sails_off_too()
    {
        var harmony = new Harmony("coodsz.tests.rot-map-redirect." + Guid.NewGuid().ToString("N"));
        var load = typeof(StandInScene).GetMethod(nameof(StandInScene.Load))!;
        try
        {
            harmony.Patch(load, transpiler: new HarmonyMethod(typeof(RealmOfThronesCompatTests), nameof(Rewrite)));
            var rot = new StandInExtents(0f, 0f, 1599.99f, 1400.3f, 620f, 1600f);
            foreach (var warSails in new[] { true, false })
            {
                var scene = new StandInScene();
                scene.Load(warSails);
                Assert.Equal("ROT_Map", scene.Module);
                Assert.Equal(rot, scene.Extents);
            }
        }
        finally
        {
            harmony.UnpatchAll(harmony.Id);
        }

        var unpatched = new StandInScene();
        unpatched.Load(true);
        Assert.Equal("NavalDLC", unpatched.Module);
        Assert.Equal(StandInExtents.Naval, unpatched.Extents);
    }

    // ---- crash trace log rules ----

    [Fact]
    public void A_first_chance_stack_is_logged_whole_up_to_the_frame_cap()
    {
        var frames = Enumerable.Range(0, CrashTraceRules.MaxFrames + 5).Select(i => "Frame.N" + i).ToList();
        var text = CrashTraceRules.StackText(frames);

        Assert.Contains("at Frame.N0", text);
        Assert.Contains("at Frame.N" + (CrashTraceRules.MaxFrames - 1), text);
        Assert.DoesNotContain("at Frame.N" + CrashTraceRules.MaxFrames + Environment.NewLine, text);
        Assert.EndsWith("... 5 more frames", text);
        Assert.Equal(CrashTraceRules.MaxFrames + 1, text.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).Length);
    }

    [Fact]
    public void An_exception_message_stays_on_one_line_and_short()
    {
        Assert.Equal("a b", CrashTraceRules.Shorten("a\nb"));
        Assert.Equal(CrashTraceRules.MaxMessage + 3, CrashTraceRules.Shorten(new string('x', 5000)).Length);
        Assert.Equal("", CrashTraceRules.Shorten(null!));
    }

    [Fact]
    public void The_check_server_probe_noise_is_still_skipped_here()
    {
        Assert.True(CrashTraceRules.IsProbeNoise("System.ArgumentOutOfRangeException", new[]
        {
            "System.ThrowHelper.ThrowArgumentOutOfRangeException", "System.Text.UTF8Encoding.GetCharCount",
            CrashTraceRules.ProbeReader, CrashTraceRules.ProbeFrame,
        }));
    }

    [Fact]
    public void The_crash_trace_logs_each_stack_once_and_stops_at_its_caps()
    {
        var gate = new CrashTraceGate(maxPrinted: 3, maxSeen: 100);
        Assert.True(gate.ShouldPrint("a", out var stop));
        Assert.Null(stop);
        Assert.False(gate.ShouldPrint("a", out _));
        Assert.True(gate.ShouldPrint("b", out _));
        Assert.True(gate.ShouldPrint("c", out stop));
        Assert.Contains("3 different", stop);
        Assert.True(gate.Stopped);
        Assert.False(gate.Count(out _));

        var noisy = new CrashTraceGate(maxPrinted: 200, maxSeen: 5);
        for (var i = 0; i < 4; i++)
            Assert.True(noisy.Count(out var none) && none == null);
        Assert.True(noisy.Count(out stop));
        Assert.Contains("5 first chance exceptions seen", stop);
        Assert.False(noisy.Count(out _));
    }

    [Fact]
    public void The_crash_trace_handler_does_not_reenter_itself_on_one_thread()
    {
        var gate = new CrashTraceGate(200, 20000);
        Assert.True(gate.Enter());
        try
        {
            Assert.False(gate.Enter());
            var other = Task.Run(() => { var entered = gate.Enter(); if (entered) gate.Exit(); return entered; }).Result;
            Assert.True(other);
        }
        finally
        {
            gate.Exit();
        }
        Assert.True(gate.Enter());
        gate.Exit();
    }

    // ---- all or nothing ----

    internal class StandInMapSceneBase
    {
    }

    internal sealed class PlanScene : StandInMapSceneBase
    {
        public string? Module;
        public StandInExtents Extents;

        [MethodImpl(MethodImplOptions.NoInlining)]
        public void Load()
        {
            Module = StandInExtents.ModuleFor(true);
            Extents = StandInExtents.For(true);
        }
    }

    internal sealed class TwiceScene : StandInMapSceneBase
    {
        public string? Module;
        public string? SecondModule;
        public StandInExtents Extents;
        public StandInExtents SecondExtents;

        [MethodImpl(MethodImplOptions.NoInlining)]
        public void Load()
        {
            Module = StandInExtents.ModuleFor(true);
            Extents = StandInExtents.For(true);
            SecondModule = StandInExtents.ModuleFor(false);
            SecondExtents = StandInExtents.For(false);
        }
    }

    internal sealed class StandInGame
    {
    }

    internal static class PlanInstaller
    {
        public static string? Module;
        public static StandInExtents Extents;

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void Install(StandInGame game)
        {
            Module = StandInExtents.ModuleFor(true);
            Extents = StandInExtents.For(true);
        }
    }

    internal static class NoCallInstaller
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void Install(StandInGame game) => GC.KeepAlive(game);
    }

    private static MapRedirectRules.Plan MakePlan(Type scene, Type? installer, bool warSails = true) =>
        MapRedirectRules.MakePlan(new[] { typeof(StandInMapSceneBase), scene, typeof(StandInGame) }, typeof(StandInMapSceneBase), installer,
            typeof(StandInGame), warSails, Hook(nameof(RotModule)), Hook(nameof(RotModule)), Hook(nameof(RotExtents)));

    [Fact]
    public void Every_call_site_is_dry_run_before_anything_is_patched()
    {
        var plan = MakePlan(typeof(PlanScene), typeof(PlanInstaller));
        Assert.Null(plan.Problem);
        Assert.Equal((1, 1, 1), (plan.LoadModuleCalls, plan.LoadExtentsCalls, plan.InstallModuleCalls));
        Assert.Equal(typeof(StandInExtents), plan.ExtentsType);

        var noInstaller = MakePlan(typeof(PlanScene), null);
        Assert.Null(noInstaller.Problem);
        Assert.Null(noInstaller.Install);

        // An installer that is there but lost its call fails the whole plan, so Load is not
        // patched either and the naval files never split from the map.
        Assert.Contains("no module call found", MakePlan(typeof(PlanScene), typeof(NoCallInstaller)).Problem);
        Assert.Contains("map scene types instead of 1",
            MapRedirectRules.MakePlan(new[] { typeof(PlanScene), typeof(TwiceScene) }, typeof(StandInMapSceneBase), null, typeof(StandInGame), true,
                Hook(nameof(RotModule)), Hook(nameof(RotModule)), Hook(nameof(RotExtents))).Problem);
    }

    [Fact]
    public void A_step_that_fails_undoes_the_steps_before_it_and_the_cache_step_never_runs()
    {
        var log = new List<string>();
        var result = MapRedirectRules.ApplyAll(new List<(string Name, Action Apply, Action Undo)>
        {
            ("Load", () => log.Add("load"), () => log.Add("undo load")),
            ("Install", () => throw new InvalidOperationException("boom"), () => log.Add("undo install")),
            ("NavigationCache.Deserialize", () => log.Add("cache"), () => log.Add("undo cache")),
        });

        Assert.Equal(new[] { "load", "undo install", "undo load" }, log);
        Assert.Contains("Install could not be patched", result);
        Assert.Contains("nothing stays patched", result);
    }

    [Fact]
    public void A_step_that_cannot_be_undone_is_named_as_half_applied()
    {
        var result = MapRedirectRules.ApplyAll(new List<(string Name, Action Apply, Action Undo)>
        {
            ("Load", () => { }, () => throw new InvalidOperationException("stuck")),
            ("Install", () => throw new InvalidOperationException("boom"), () => { }),
        });

        Assert.Contains("HALF APPLIED", result);
        Assert.EndsWith("Load", result);
        Assert.Null(MapRedirectRules.ApplyAll(new List<(string Name, Action Apply, Action Undo)> { ("Load", () => { }, () => { }) }));
    }

    // A Harmony patch can throw after it is already on the method, so the failing step is undone
    // too, and named when even that fails.
    [Fact]
    public void The_step_that_throws_is_undone_as_well_and_named_if_it_stays()
    {
        var log = new List<string>();
        Assert.Contains("nothing stays patched", MapRedirectRules.ApplyAll(new List<(string Name, Action Apply, Action Undo)>
        {
            ("Load", () => throw new InvalidOperationException("boom"), () => log.Add("undo load")),
        }));
        Assert.Equal(new[] { "undo load" }, log);

        var stuck = MapRedirectRules.ApplyAll(new List<(string Name, Action Apply, Action Undo)>
        {
            ("Load", () => { }, () => { }),
            ("Install", () => throw new InvalidOperationException("boom"), () => throw new InvalidOperationException("stuck")),
        });
        Assert.Contains("HALF APPLIED", stuck);
        Assert.EndsWith(": Install", stuck);
    }

    [Fact]
    public void With_war_sails_off_the_installer_is_left_alone_and_cannot_stop_the_redirect()
    {
        var off = MakePlan(typeof(PlanScene), typeof(NoCallInstaller), warSails: false);
        Assert.Null(off.Problem);
        Assert.Null(off.Install);
        Assert.Null(MakePlan(typeof(PlanScene), typeof(PlanInstaller), warSails: false).Install);

        Assert.Contains("no module call found", MakePlan(typeof(PlanScene), typeof(NoCallInstaller), warSails: true).Problem);
        Assert.NotNull(MakePlan(typeof(PlanScene), typeof(PlanInstaller), warSails: true).Install);
    }

    // ---- the production hooks (RedirectHooks), which MapRedirect's rewritten calls land in ----

    private static readonly StandInExtents Rot = new(0f, 0f, 1599.99f, 1400.3f, 620f, 1600f);

    private static RedirectHooks NewHooks(List<string> log) => new(log.Add)
    {
        Data = MapData.Parse("module=ROT_Map\nborder_min_x=0\nborder_min_y=0\nborder_max_x=1599.99\nborder_max_y=1400.3\nborder_max_height=620\nterrain_size=1600", out _),
        ExtentsConstructor = MapRedirectRules.ExtentsConstructor(typeof(StandInExtents)),
    };

    private static void AssertServerValues(RedirectHooks hooks)
    {
        Assert.False(hooks.Active);
        Assert.Equal("NavalDLC", hooks.MapModule("NavalDLC"));
        Assert.Null(hooks.MapModule(null!));
        Assert.Equal("NavalDLC", hooks.NavalModule("NavalDLC"));
        Assert.Equal(StandInExtents.Naval, hooks.MapExtents(StandInExtents.Naval));
    }

    [Fact]
    public void Before_activation_the_hooks_hand_back_the_servers_own_values()
    {
        var log = new List<string>();
        AssertServerValues(NewHooks(log));
        Assert.Empty(log);
    }

    [Fact]
    public void After_a_failed_or_half_undone_apply_the_hooks_still_hand_back_the_servers_values()
    {
        var log = new List<string>();
        var rolledBack = NewHooks(log);
        Assert.Contains("nothing stays patched", rolledBack.ApplyAndActivate(new List<(string Name, Action Apply, Action Undo)>
        {
            ("Load", () => { }, () => { }),
            ("Install", () => throw new InvalidOperationException("boom"), () => { }),
        }));
        AssertServerValues(rolledBack);

        // The Load rewrite stays on the method here, so this is the case the guard is for.
        var stuck = NewHooks(log);
        Assert.Contains("HALF APPLIED", stuck.ApplyAndActivate(new List<(string Name, Action Apply, Action Undo)>
        {
            ("Load", () => { }, () => throw new InvalidOperationException("stuck")),
            ("Install", () => throw new InvalidOperationException("boom"), () => { }),
        }));
        AssertServerValues(stuck);
        Assert.Empty(log);
    }

    [Fact]
    public void After_a_full_apply_the_hooks_hand_back_realm_of_thrones_values_and_log_once()
    {
        var log = new List<string>();
        var hooks = NewHooks(log);
        Assert.Null(hooks.ApplyAndActivate(new List<(string Name, Action Apply, Action Undo)> { ("Load", () => { }, () => { }) }));

        Assert.True(hooks.Active);
        Assert.Equal("ROT_Map", hooks.MapModule("NavalDLC"));
        Assert.Equal("ROT_Map", hooks.MapModule(null!));
        Assert.Equal("ROT_Map", hooks.NavalModule("NavalDLC"));
        Assert.Equal(Rot, hooks.MapExtents(StandInExtents.Naval));
        Assert.Equal(Rot, hooks.MapExtents(StandInExtents.Land));
        Assert.Equal(3, log.Count);
    }

    private static RedirectHooks? _patchedHooks;

    public static string? ProductionModule(string? original) => _patchedHooks!.MapModule(original!);

    public static object ProductionExtents(object original) => _patchedHooks!.MapExtents(original);

    private static IEnumerable<CodeInstruction> ProductionRewrite(IEnumerable<CodeInstruction> instructions) =>
        MapRedirectRules.Rewrite(instructions, typeof(StandInExtents), Hook(nameof(ProductionModule)), Hook(nameof(ProductionExtents)), out _, out _);

    [Fact]
    public void A_patched_load_keeps_the_servers_values_until_activation_then_gets_realm_of_thrones()
    {
        _patchedHooks = NewHooks(new List<string>());
        var harmony = new Harmony("coodsz.tests.rot-map-hooks." + Guid.NewGuid().ToString("N"));
        try
        {
            harmony.Patch(typeof(StandInScene).GetMethod(nameof(StandInScene.Load)), transpiler: new HarmonyMethod(typeof(RealmOfThronesCompatTests), nameof(ProductionRewrite)));
            var before = new StandInScene();
            before.Load(true);
            Assert.Equal(("NavalDLC", StandInExtents.Naval), (before.Module, before.Extents));

            _patchedHooks.ApplyAndActivate(new List<(string Name, Action Apply, Action Undo)> { ("Load", () => { }, () => { }) });
            var after = new StandInScene();
            after.Load(true);
            Assert.Equal(("ROT_Map", Rot), (after.Module, after.Extents));
        }
        finally
        {
            harmony.UnpatchAll(harmony.Id);
        }
    }

    // ---- the naval village lookup survives two settlements sharing one village (NavalVillageRules) ----

    public sealed class StandInVillage
    {
        public string? Id;
        public bool Throws;
        public string Key => Throws ? throw new InvalidOperationException("no settlement") : Id!;
    }

    // Shaped like the server's NavalMapSceneInstaller: a private static method builds the lookup
    // from a static getter with a cached key lambda (Village.All.ToDictionary(v => ...)).
    internal static class StandInNavalInstaller
    {
        public static List<StandInVillage> All = new();
        public static Dictionary<string, StandInVillage>? Last;
        public static int LambdaCalls;

        public static List<StandInVillage> AllVillages => All;

        public static void Install() => ApplyDropOffs();

        private static void ApplyDropOffs() => Last = AllVillages.ToDictionary(village => { LambdaCalls++; return village.Key; });

        private static void Unrelated() => Last = null;
    }

    internal static class TwoLookupsInstaller
    {
        private static Dictionary<string, StandInVillage> First(List<StandInVillage> all) => all.ToDictionary(village => village.Key);

        private static Dictionary<string, StandInVillage> Second(List<StandInVillage> all) => all.ToDictionary(village => village.Key);
    }

    // Something else between the villages and the lookup: the shape changed.
    internal static class OtherStepInstaller
    {
        public static List<StandInVillage> AllVillages => new();

        private static Dictionary<string, StandInVillage> Lookup() => AllVillages.Concat(AllVillages).ToDictionary(village => village.Key);
    }

    private static List<string> _villageSkips = new();

    public static Dictionary<string, StandInVillage> StandInVillages(IEnumerable<StandInVillage> villages)
    {
        _villageSkips = new List<string>();
        return NavalVillageRules.FirstPerKey(villages, village => village.Key, _villageSkips);
    }

    private static IEnumerable<CodeInstruction> VillageRewrite(IEnumerable<CodeInstruction> instructions) =>
        NavalVillageRules.Rewrite(instructions, typeof(StandInVillage), Hook(nameof(StandInVillages)), out _, out _);

    // The patched lookup runs only our method: the lambda steps are nops, ToDictionary is our call
    // and no field is read any more, so MonoMod cannot pick a wrong one.
    [Fact]
    public void The_rewrite_drops_the_key_lambda_and_calls_only_our_method()
    {
        var original = PatchProcessor.GetOriginalInstructions(AccessTools.Method(typeof(StandInNavalInstaller), "ApplyDropOffs"));
        Assert.Contains(original, i => i.opcode == System.Reflection.Emit.OpCodes.Ldftn);
        var rewritten = NavalVillageRules.Rewrite(original, typeof(StandInVillage), Hook(nameof(StandInVillages)), out var replaced, out var problem);
        Assert.Null(problem);
        Assert.Equal(1, replaced);
        var call = rewritten.FindIndex(i => i.operand is MethodInfo m && m == Hook(nameof(StandInVillages)));
        Assert.True(call > 0);
        // Up to our call only the getter and nops are left (the stand in's own store of the result comes after).
        Assert.All(rewritten.Take(call), i => Assert.True(i.opcode == System.Reflection.Emit.OpCodes.Nop || MapRedirectRules.Called(i)?.Name == "get_AllVillages", i.ToString()));
        Assert.Equal(0, NavalVillageRules.CountCalls(rewritten, typeof(StandInVillage)));

        var other = PatchProcessor.GetOriginalInstructions(AccessTools.Method(typeof(OtherStepInstaller), "Lookup"));
        var untouched = NavalVillageRules.Rewrite(other, typeof(StandInVillage), Hook(nameof(StandInVillages)), out replaced, out problem);
        Assert.Equal(0, replaced);
        Assert.Contains("unexpected step", problem);
        Assert.Equal(1, NavalVillageRules.CountCalls(untouched, typeof(StandInVillage)));
    }

    // The obfuscated server gives several fields of one type the same name, which Harmony's copy
    // (built by MonoMod) cannot tell apart. A type made here the same way shows the check finds it.
    [Fact]
    public void A_field_that_shares_its_name_with_another_is_reported()
    {
        var module = System.Reflection.Emit.AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("rot-dupfields"), System.Reflection.Emit.AssemblyBuilderAccess.Run).DefineDynamicModule("m");
        var builder = module.DefineType("Closure", TypeAttributes.Public | TypeAttributes.Sealed);
        builder.DefineField("A", builder, FieldAttributes.Public | FieldAttributes.Static);
        builder.DefineField("A", typeof(Func<string, string>), FieldAttributes.Public | FieldAttributes.Static);
        builder.DefineField("B", typeof(int), FieldAttributes.Public | FieldAttributes.Static);
        var type = builder.CreateType()!;
        var fields = type.GetFields(BindingFlags.Public | BindingFlags.Static);
        var list = fields.Select(f => new CodeInstruction(System.Reflection.Emit.OpCodes.Ldsfld, f)).ToList();

        var ambiguous = MapRedirectRules.AmbiguousFields(list);
        Assert.Equal(2, ambiguous.Count);
        Assert.All(ambiguous, text => Assert.StartsWith("Closure::A (", text));
        Assert.Empty(MapRedirectRules.AmbiguousFields(list.Where(i => ((FieldInfo)i.operand).Name == "B")));
    }

    [Fact]
    public void The_village_lookup_is_found_by_shape_or_left_alone_with_a_reason()
    {
        IEnumerable<CodeInstruction> Il(MethodInfo method) => PatchProcessor.GetOriginalInstructions(method);
        Assert.Equal("ApplyDropOffs", NavalVillageRules.FindMethod(typeof(StandInNavalInstaller), typeof(StandInVillage), Il, out var problem)!.Name);
        Assert.Null(problem);

        Assert.Null(NavalVillageRules.FindMethod(typeof(TwoLookupsInstaller), typeof(StandInVillage), Il, out problem));
        Assert.Contains("2 private static methods", problem);
        Assert.Null(NavalVillageRules.FindMethod(typeof(StandInNavalInstaller), typeof(string), Il, out problem));
        Assert.Contains("no private static method", problem);
        Assert.Null(NavalVillageRules.FindMethod(null!, typeof(StandInVillage), Il, out problem));
        Assert.NotNull(problem);
    }

    [Fact]
    public void A_patched_lookup_keeps_the_first_village_per_id_and_skips_ids_that_fail()
    {
        var a = new StandInVillage { Id = "ROT_town40_village2" };
        var b = new StandInVillage { Id = "ROT_town40_village3" };
        var other = new StandInVillage { Id = "ROT_town40_village3" };
        StandInNavalInstaller.All = new List<StandInVillage> { a, b, a, other, new() { Throws = true }, new() { Id = null } };
        Assert.Throws<ArgumentException>(StandInNavalInstaller.Install);

        var harmony = new Harmony("coodsz.tests.rot-villages." + Guid.NewGuid().ToString("N"));
        try
        {
            harmony.Patch(AccessTools.Method(typeof(StandInNavalInstaller), "ApplyDropOffs"), transpiler: new HarmonyMethod(typeof(RealmOfThronesCompatTests), nameof(VillageRewrite)));
            var lambdaCalls = StandInNavalInstaller.LambdaCalls;
            StandInNavalInstaller.Install();
            // The installer's own key lambda never runs in the patched copy.
            Assert.Equal(lambdaCalls, StandInNavalInstaller.LambdaCalls);
            Assert.Equal(2, StandInNavalInstaller.Last!.Count);
            Assert.Same(a, StandInNavalInstaller.Last["ROT_town40_village2"]);
            Assert.Same(b, StandInNavalInstaller.Last["ROT_town40_village3"]);
            Assert.Equal(new[] { "ROT_town40_village2", "ROT_town40_village3", "(no id: InvalidOperationException)", "(no id)" }, _villageSkips);

            var line = NavalVillageRules.SkippedLine(_villageSkips);
            Assert.StartsWith("Naval village guard: 4 village entries left out of the server's village lookup (ROT_town40_village2, ROT_town40_village3,", line);
            Assert.Contains("Realm of Thrones' settlements.xml gives these villages another village's component", line);
            Assert.Null(NavalVillageRules.SkippedLine(new List<string>()));
        }
        finally
        {
            harmony.UnpatchAll(harmony.Id);
        }
    }

    [Fact]
    public void Without_repeats_the_lookup_is_the_same_as_to_dictionary()
    {
        var villages = Enumerable.Range(0, 50).Select(i => new StandInVillage { Id = "village_" + i }).ToList();
        var skipped = new List<string>();
        var ours = NavalVillageRules.FirstPerKey(villages, village => village.Key, skipped);
        Assert.Empty(skipped);
        Assert.Equal(villages.ToDictionary(village => village.Key), ours);
        Assert.Empty(NavalVillageRules.FirstPerKey<StandInVillage>(null!, village => village.Key, skipped));
    }

    // ---- the staging script judges the border as the Singles the module reads ----

    private static (int ExitCode, string? MapFile) Stage(string root, string borderMaxX)
    {
        var rotMap = Path.Combine(root, "ROT_Map");
        Directory.CreateDirectory(Path.Combine(rotMap, "SceneObj", "Main_map"));
        File.WriteAllText(Path.Combine(rotMap, "SceneObj", "Main_map", "scene.xscene"),
            "<?xml version=\"1.0\"?>\n<scene name=\"Main_map\"><entities>"
            + "<game_entity name=\"border_min\"><transform position=\"0.000, 0.000, 0.000\"/></game_entity>"
            + "<game_entity name=\"border_max\"><transform position=\"" + borderMaxX + ", 1400.300, 620.000\"/></game_entity>"
            + "</entities><terrain node_dimension_x=\"16\" node_dimension_y=\"16\" node_size=\"100.000\"/></scene>\n");
        var output = Path.Combine(root, "out");
        var script = Path.Combine(RepoRoot(), "compat", "RealmOfThronesCoopCompat", "Stage-RotServerMap.ps1");
        using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = "-NoProfile -ExecutionPolicy Bypass -File \"" + script + "\" -RotMap \"" + rotMap + "\" -OutDir \"" + output + "\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        });
        Assert.NotNull(process);
        process!.StandardOutput.ReadToEndAsync();
        process.StandardError.ReadToEndAsync();
        Assert.True(process.WaitForExit(120000), "The staging script did not finish.");
        var mapFile = Path.Combine(output, "RealmOfThrones.CoopCompat", "ModuleData", MapData.FileName);
        return (process.ExitCode, File.Exists(mapFile) ? File.ReadAllText(mapFile) : null);
    }

    [Fact]
    public void Staging_refuses_a_border_that_only_a_double_would_keep_inside_the_terrain()
    {
        var root = Path.Combine(Path.GetTempPath(), "rot-stage-" + Guid.NewGuid().ToString("N"));
        try
        {
            // 1599.99999 is below 1600 as a Double, but the Single the module reads is 1600.
            Assert.Equal(1600f, float.Parse("1599.99999", System.Globalization.CultureInfo.InvariantCulture));
            var refused = Stage(Path.Combine(root, "a"), "1599.99999");
            Assert.NotEqual(0, refused.ExitCode);
            Assert.Null(refused.MapFile);

            var kept = Stage(Path.Combine(root, "b"), "1599.990");
            Assert.Equal(0, kept.ExitCode);
            var data = MapData.Parse(kept.MapFile, out var error);
            Assert.Null(error);
            Assert.Equal(new object[] { 0f, 0f, 1599.99f, 1400.3f, 620f, 1600f }, data!.ExtentsValues);

            // 1599.9999 is a Single below 1600 whose 7 digit text is "1600", so only the 9 digit
            // text keeps it; the short text stays where it is exact.
            var long9 = Stage(Path.Combine(root, "c"), "1599.9999");
            Assert.Equal(0, long9.ExitCode);
            var lines = long9.MapFile!.Split('\n').Select(line => line.TrimEnd('\r')).ToList();
            Assert.Contains("border_max_x=1599.99988", lines);
            Assert.Contains("border_max_y=1400.3", lines);
            Assert.Equal(1599.9999f, (float)MapData.Parse(long9.MapFile, out _)!.ExtentsValues[2]);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Two_calls_of_each_kind_in_one_method_are_all_rewritten()
    {
        var original = PatchProcessor.GetOriginalInstructions(typeof(TwiceScene).GetMethod(nameof(TwiceScene.Load))!);
        MapRedirectRules.Rewrite(original, typeof(StandInExtents), Hook(nameof(RotModule)), Hook(nameof(RotExtents)), out var moduleCalls, out var extentsCalls);
        Assert.Equal((2, 2), (moduleCalls, extentsCalls));

        var harmony = new Harmony("coodsz.tests.rot-map-twice." + Guid.NewGuid().ToString("N"));
        try
        {
            harmony.Patch(typeof(TwiceScene).GetMethod(nameof(TwiceScene.Load)), transpiler: new HarmonyMethod(typeof(RealmOfThronesCompatTests), nameof(Rewrite)));
            var scene = new TwiceScene();
            scene.Load();
            var rot = new StandInExtents(0f, 0f, 1599.99f, 1400.3f, 620f, 1600f);
            Assert.Equal(("ROT_Map", "ROT_Map"), (scene.Module, scene.SecondModule));
            Assert.Equal((rot, rot), (scene.Extents, scene.SecondExtents));
        }
        finally
        {
            harmony.UnpatchAll(harmony.Id);
        }
    }

    private static IEnumerable<CodeInstruction> InstallerRewrite(IEnumerable<CodeInstruction> instructions) =>
        MapRedirectRules.Rewrite(instructions, typeof(StandInExtents), Hook(nameof(RotModule)), null, out _, out _);

    [Fact]
    public void The_installer_rewrite_changes_only_the_module_call()
    {
        var harmony = new Harmony("coodsz.tests.rot-map-installer." + Guid.NewGuid().ToString("N"));
        try
        {
            harmony.Patch(typeof(PlanInstaller).GetMethod(nameof(PlanInstaller.Install)), transpiler: new HarmonyMethod(typeof(RealmOfThronesCompatTests), nameof(InstallerRewrite)));
            PlanInstaller.Install(new StandInGame());
            Assert.Equal("ROT_Map", PlanInstaller.Module);
            Assert.Equal(StandInExtents.Naval, PlanInstaller.Extents);
        }
        finally
        {
            harmony.UnpatchAll(harmony.Id);
        }
    }

    [Fact]
    public void The_cache_pick_swaps_nothing_until_the_map_is_patched()
    {
        var rot = Path.Combine("Modules", "ROT_Map");
        var rotDefault = Path.Combine(rot, "ModuleData", "DistanceCaches", "settlements_distance_cache_Default.bin");
        var sandBox = Path.Combine("Modules", "SandBox", "ModuleData", "DistanceCaches", "settlements_distance_cache_Default.bin");
        var log = new List<string>();
        var pick = new MapData.CachePick();

        Assert.Null(pick.Pick(false, sandBox, rot, _ => true, log.Add));
        Assert.Empty(log);
        Assert.Equal(rotDefault, pick.Pick(true, sandBox, rot, path => path == rotDefault, log.Add));
        Assert.Equal(rotDefault, pick.Pick(true, sandBox, rot, path => path == rotDefault, log.Add));
        Assert.Contains("reading " + rotDefault, Assert.Single(log));
        Assert.Null(pick.Pick(true, rotDefault, rot, _ => true, log.Add));
        Assert.Single(log);
    }

    [Fact]
    public void A_cache_realm_of_thrones_lacks_is_logged_once()
    {
        var rot = Path.Combine("Modules", "ROT_Map");
        var naval = Path.Combine("Modules", "NavalDLC", "ModuleData", "DistanceCaches", "settlements_distance_cache_Naval.bin");
        var log = new List<string>();
        var pick = new MapData.CachePick();

        Assert.Null(pick.Pick(true, naval, rot, _ => false, log.Add));
        Assert.Null(pick.Pick(true, naval, rot, _ => false, log.Add));
        Assert.Contains("has no settlements_distance_cache_Naval.bin", Assert.Single(log));
    }

    // ---- where the server finds Harmony and the other libraries (HarmonyLocator) ----

    [Theory]
    [InlineData("0Harmony, Version=2.4.2.0, Culture=neutral, PublicKeyToken=null", true)]
    [InlineData("0Harmony", true)]
    [InlineData(" 0harmony ,Version=2.4.2.0", true)]
    [InlineData("0Harmony.Extras, Version=1.0.0.0", false)]
    [InlineData("Bannerlord.Harmony, Version=2.4.2.248", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Only_a_request_for_0harmony_is_a_harmony_request(string? name, bool answered)
    {
        Assert.Equal(answered, HarmonyLocator.IsHarmonyRequest(name!));
    }

    [Theory]
    [InlineData("MCMv5, Version=5.11.4.0, Culture=neutral, PublicKeyToken=null", "MCMv5")]
    [InlineData(" GameInterface ", "GameInterface")]
    [InlineData(@"..\..\evil", null)]
    [InlineData(@"C:\x\y", null)]
    [InlineData("a/b", null)]
    [InlineData(".hidden", null)]
    [InlineData(", Version=1.0.0.0", null)]
    [InlineData(null, null)]
    public void A_request_becomes_a_plain_simple_name_or_nothing(string? requested, string? simple)
    {
        Assert.Equal(simple, HarmonyLocator.SimpleName(requested!));
    }

    private static readonly KeyValuePair<string, string>[] RotServerModules =
    {
        new("Native", @"C:\srv\engine\Modules\Native"),
        new("SandBox", @"C:\srv\engine\Modules\SandBox"),
        new("CoopNightly", @"C:\srv\engine\Modules\Coop"),
        new("Bannerlord.UIExtenderEx", @"C:\srv\engine\Modules\Bannerlord.UIExtenderEx"),
        new("Bannerlord.MBOptionScreen", @"C:\srv\engine\Modules\Bannerlord.MBOptionScreen"),
        new("ROT-Core", @"C:\srv\engine\Modules\ROT-Core"),
        new("ROT_Map", @"C:\srv\engine\Modules\ROT_Map"),
    };

    // MCM and UIExtenderEx have no server bin: their types come from their client bin. Harmony and
    // the listed view and UI assemblies come only from the hosted Coop module's server bin, and
    // Coop's own assemblies and SandBox never from us.
    [Fact]
    public void Each_library_has_its_own_folder_and_sandbox_is_never_answered()
    {
        foreach (var coopOwn in new[] { "Common", "GameInterface", "Coop.Core", "Coop", "Coop.Naval", "Missions", "Missions.Naval", "LiteNetLib", "SandBox", "System.Windows.Forms" })
        {
            Assert.Empty(HarmonyLocator.Candidates(coopOwn, RotServerModules));
            Assert.False(HarmonyLocator.HasRule(coopOwn), coopOwn);
        }
        Assert.True(HarmonyLocator.IsWindowsForms("system.windows.forms"));
        Assert.All(HarmonyLocator.CoopBinLibraries, name => Assert.Equal(@"C:\srv\engine\Modules\Coop\bin\Win64_Shipping_Server\" + name + ".dll", Assert.Single(HarmonyLocator.Candidates(name, RotServerModules)).Value));
        Assert.Equal(16, HarmonyLocator.CoopBinLibraries.Length);
        Assert.Equal(new KeyValuePair<string, string>("Bannerlord.MBOptionScreen", @"C:\srv\engine\Modules\Bannerlord.MBOptionScreen\bin\Win64_Shipping_Client\MCMv5.dll"),
            Assert.Single(HarmonyLocator.Candidates("MCMv5", RotServerModules)));
        Assert.Equal(new KeyValuePair<string, string>("Bannerlord.UIExtenderEx", @"C:\srv\engine\Modules\Bannerlord.UIExtenderEx\bin\Win64_Shipping_Client\Bannerlord.UIExtenderEx.dll"),
            Assert.Single(HarmonyLocator.Candidates("bannerlord.uiextenderex", RotServerModules)));
        Assert.Equal(new KeyValuePair<string, string>("CoopNightly", @"C:\srv\engine\Modules\Coop\bin\Win64_Shipping_Server\TaleWorlds.MountAndBlade.View.dll"),
            Assert.Single(HarmonyLocator.Candidates("TaleWorlds.MountAndBlade.View", RotServerModules)));
        Assert.Empty(HarmonyLocator.Candidates("SandBox", RotServerModules));
        Assert.Empty(HarmonyLocator.Candidates(@"..\ROT", RotServerModules));
        // Without the module that ships it, a client library has no candidate, not even Coop's bin.
        Assert.Empty(HarmonyLocator.Candidates("MCMv5", RotServerModules.Where(m => m.Key != "Bannerlord.MBOptionScreen")));
        Assert.Null(HarmonyLocator.Pick("ROT", RotServerModules, path => path.EndsWith(@"ROT-Core\bin\Win64_Shipping_Client\ROT.dll")));

        Assert.True(HarmonyLocator.IsNamedLibrary("0harmony") && HarmonyLocator.IsNamedLibrary("MCMv5") && HarmonyLocator.IsNamedLibrary("Bannerlord.UIExtenderEx"));
        Assert.False(HarmonyLocator.IsNamedLibrary("GameInterface"));
        Assert.Equal(@"C:\srv\engine\Modules\ROT-Core\bin\Win64_Shipping_Client\ROT.dll", HarmonyLocator.ModuleFile(RotServerModules, "rot-core", "Win64_Shipping_Client", "ROT.dll"));
        Assert.Null(HarmonyLocator.ModuleFile(RotServerModules, "ROT-Dragon", "Win64_Shipping_Client", "ROT.dll"));
    }

    [Theory]
    [InlineData("probe", true)]
    [InlineData(" Probe ", true)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("1", false)]
    [InlineData("probes", false)]
    public void The_host_probe_runs_only_when_the_variable_says_probe(string? value, bool on)
    {
        Assert.Equal(on, RotRules.IsProbeOn(value!));
    }

    private static readonly KeyValuePair<string, string>[] ServerModules =
    {
        new("Native", @"C:\srv\engine\Modules\Native"),
        new("CoopNightly", @"C:\srv\engine\Modules\Coop"),
        new("NavalDLC", @"C:\srv\engine\Modules\NavalDLC"),
        new("Bannerlord.Harmony", @"C:\srv\engine\Modules\Bannerlord.Harmony"),
        new("RealmOfThrones.CoopCompat", @"C:\srv\engine\Modules\RealmOfThrones.CoopCompat"),
    };

    // Only the Coop module's server bin: Bannerlord.Harmony's file is the game's .NET Framework build.
    [Fact]
    public void Only_the_coop_modules_server_bin_is_a_candidate()
    {
        var candidate = Assert.Single(HarmonyLocator.Candidates(ServerModules));
        Assert.Equal(new KeyValuePair<string, string>("CoopNightly", @"C:\srv\engine\Modules\Coop\bin\Win64_Shipping_Server\0Harmony.dll"), candidate);
        Assert.Empty(HarmonyLocator.Candidates(new[] { new KeyValuePair<string, string>("Bannerlord.Harmony", @"C:\srv\engine\Modules\Bannerlord.Harmony") }));
        Assert.Empty(HarmonyLocator.Candidates(null!));
    }

    [Fact]
    public void The_coop_folder_comes_from_the_module_list_whatever_its_id_or_folder()
    {
        var release = new[] { new KeyValuePair<string, string>("Coop", @"D:\x\Modules\BannerlordCoop") };
        Assert.Equal(@"D:\x\Modules\BannerlordCoop\bin\Win64_Shipping_Server\0Harmony.dll", Assert.Single(HarmonyLocator.Candidates(release)).Value);
    }

    [Fact]
    public void With_two_coop_modules_the_first_id_with_the_server_file_is_hosted()
    {
        var both = new[]
        {
            new KeyValuePair<string, string>("Coop", @"C:\srv\engine\Modules\CoopRelease"),
            new KeyValuePair<string, string>("CoopNightly", @"C:\srv\engine\Modules\Coop"),
        };
        var nightly = @"C:\srv\engine\Modules\Coop\bin\Win64_Shipping_Server\0Harmony.dll";
        var release = @"C:\srv\engine\Modules\CoopRelease\bin\Win64_Shipping_Server\0Harmony.dll";

        Assert.Equal(new[] { "CoopNightly", "Coop" }, HarmonyLocator.Candidates(both).Select(c => c.Key));
        Assert.Equal("CoopNightly", HarmonyLocator.Pick(both, path => path == nightly || path == release)!.Value.Key);
        Assert.Equal(new KeyValuePair<string, string>("Coop", release), HarmonyLocator.Pick(both, path => path == release));
        Assert.Null(HarmonyLocator.Pick(both, _ => false));
        Assert.Null(HarmonyLocator.Pick(ServerModules, path => path.Contains("Bannerlord.Harmony")));
    }

    // The committed module DLL the tools ship (Build.cmd rebuilds it, and RealmOfThronesHostTests fails a stale one),
    // so the compiled module tests run in CI too, which has no game files to build it.
    internal static string CompatDll => Path.Combine(RepoRoot(), "compat", "RealmOfThronesCoopCompat", "module", "bin", "Win64_Shipping_Client", "RealmOfThrones.CoopCompat.dll");

    private const string GameFolder = @"C:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord";

    // A test of the compiled module runs wherever the module DLL is, and with needsGame only where
    // Bannerlord is installed. Anywhere else it shows as skipped with the reason, never as a pass.
    public sealed class CompiledCompatFactAttribute : FactAttribute
    {
        public CompiledCompatFactAttribute(bool needsGame = false)
        {
            if (!File.Exists(CompatDll))
                Skip = "RealmOfThrones.CoopCompat.dll is not built here; run compat\\RealmOfThronesCoopCompat\\Build.cmd first.";
            else if (needsGame && !File.Exists(Path.Combine(GameFolder, "bin", "Win64_Shipping_Client", "TaleWorlds.MountAndBlade.dll")))
                Skip = "Bannerlord is not installed at " + GameFolder + ".";
        }
    }

    // The resolver must be usable before Harmony is: the compiled SubModule (with its nested and
    // compiler made types), ServerHarmony and HarmonyLocator may not name anything from 0Harmony,
    // and neither may Coop, which any code path may ask.
    // Reads the DLL Build.cmd builds; the test project has no game files to build it itself.
    private static readonly string[] EarlyTypes =
    {
        "RealmOfThronesCoopCompat.SubModule", "RealmOfThronesCoopCompat.ServerHarmony", "RealmOfThronesCoopCompat.HarmonyLocator", "RealmOfThronesCoopCompat.ServerResolveHandler",
        "RealmOfThronesCoopCompat.Coop", "RealmOfThronesCoopCompat.RotHostProbe",
    };

    [CompiledCompatFact]
    public void The_types_that_run_before_harmony_reference_nothing_from_harmony()
    {
        using var module = Mono.Cecil.ModuleDefinition.ReadModule(CompatDll);
        bool FromHarmony(Mono.Cecil.TypeReference? type)
        {
            while (type is Mono.Cecil.TypeSpecification spec)
            {
                if (spec is Mono.Cecil.GenericInstanceType generic && generic.GenericArguments.Any(FromHarmony))
                    return true;
                type = spec.ElementType;
            }
            return type != null && type.Scope is Mono.Cecil.AssemblyNameReference scope && scope.Name == "0Harmony";
        }
        IEnumerable<Mono.Cecil.TypeDefinition> WithNested(Mono.Cecil.TypeDefinition type) => new[] { type }.Concat(type.NestedTypes.SelectMany(WithNested));

        var checkedTypes = 0;
        var harmonyUses = new List<string>();
        foreach (var name in EarlyTypes)
        {
            foreach (var type in WithNested(module.GetType(name)))
            {
                checkedTypes++;
                if (FromHarmony(type.BaseType) || type.CustomAttributes.Any(a => FromHarmony(a.AttributeType)))
                    harmonyUses.Add(type.FullName);
                harmonyUses.AddRange(type.Fields.Where(f => FromHarmony(f.FieldType) || f.CustomAttributes.Any(a => FromHarmony(a.AttributeType))).Select(f => f.FullName));
                foreach (var method in type.Methods)
                {
                    if (FromHarmony(method.ReturnType) || method.Parameters.Any(p => FromHarmony(p.ParameterType)) || method.CustomAttributes.Any(a => FromHarmony(a.AttributeType)))
                        harmonyUses.Add(method.FullName);
                    if (!method.HasBody)
                        continue;
                    if (method.Body.Variables.Any(v => FromHarmony(v.VariableType)))
                        harmonyUses.Add(method.FullName + " locals");
                    foreach (var instruction in method.Body.Instructions)
                    {
                        var uses = instruction.Operand switch
                        {
                            Mono.Cecil.TypeReference t => FromHarmony(t),
                            Mono.Cecil.MemberReference m => FromHarmony(m.DeclaringType) || (m is Mono.Cecil.MethodReference mr && (FromHarmony(mr.ReturnType) || mr.Parameters.Any(p => FromHarmony(p.ParameterType))))
                                || (m is Mono.Cecil.FieldReference fr && FromHarmony(fr.FieldType)),
                            _ => false,
                        };
                        if (uses)
                            harmonyUses.Add(method.FullName + " " + instruction);
                    }
                }
            }
        }

        Assert.True(checkedTypes >= EarlyTypes.Length);
        Assert.Empty(harmonyUses);
        // The check itself sees Harmony where it is used.
        Assert.Contains(WithNested(module.GetType("RealmOfThronesCoopCompat.MapRedirect")).SelectMany(t => t.Fields), f => FromHarmony(f.FieldType));
    }

    // The startup contract: the engine builds the SubModule before any OnSubModuleLoad, so the
    // constructor is where the resolver must be registered.
    [CompiledCompatFact]
    public void The_compiled_submodule_constructor_registers_the_resolver_on_the_server()
    {
        using var module = Mono.Cecil.ModuleDefinition.ReadModule(CompatDll);
        IEnumerable<string> Calls(Mono.Cecil.MethodDefinition method) => method.Body.Instructions
            .Where(i => i.OpCode.Code == Mono.Cecil.Cil.Code.Call || i.OpCode.Code == Mono.Cecil.Cil.Code.Callvirt)
            .Select(i => ((Mono.Cecil.MethodReference)i.Operand).DeclaringType.Name + "." + ((Mono.Cecil.MethodReference)i.Operand).Name);
        var subModule = module.GetType("RealmOfThronesCoopCompat.SubModule");

        var constructor = Assert.Single(subModule.Methods, m => m.IsConstructor && !m.IsStatic);
        Assert.Contains("SubModule.RegisterHarmonyOnServer", Calls(constructor));
        var register = Assert.Single(subModule.Methods, m => m.Name == "RegisterHarmonyOnServer");
        Assert.True(register.NoInlining);
        Assert.Contains("HarmonyLocator.ShouldRegister", Calls(register));
        Assert.Contains("ServerHarmony.Register", Calls(register));
        // The server folder check comes before the module list is built, so a game client skips it.
        var calls = Calls(register).ToList();
        Assert.True(calls.IndexOf("RotRules.IsServerBinFolder") >= 0 && calls.IndexOf("RotRules.IsServerBinFolder") < calls.IndexOf("SubModule.ActiveModules"), string.Join(", ", calls));
    }

    // The engine lists every type of a module DLL when it loads it, before Harmony can be found
    // on the server. A copy of the compiled DLL goes into its own context where 0Harmony cannot
    // load (the game's TaleWorlds and SandBox DLLs can), and listing its types must not throw.
    // The gates' wiring in the compiled module: the save load prefix asks the ROT gate and then
    // decides with DecideLoad, and the new world is only marked loaded after the world check said
    // so (a call to Passes, a branch on its answer, then set_IsLoaded). Deleting either call would
    // otherwise leave every rule test green.
    [CompiledCompatFact]
    public void The_gates_are_wired_into_the_save_load_and_the_new_world()
    {
        using var module = Mono.Cecil.ModuleDefinition.ReadModule(CompatDll);
        List<Mono.Cecil.Cil.Instruction> Body(string type, string method) =>
            module.GetType(type).Methods.Single(m => m.Name == method).Body.Instructions.ToList();
        int CallIndex(List<Mono.Cecil.Cil.Instruction> body, string declaring, string name) =>
            body.FindIndex(i => i.Operand is Mono.Cecil.MethodReference m && m.DeclaringType.Name == declaring && m.Name == name);

        var prefix = Body("RealmOfThronesCoopCompat.FreshCampaign", "LoadGameActionPrefix");
        var refusal = CallIndex(prefix, "RotHostRules", "WorldRefusal");
        var decide = CallIndex(prefix, "FreshCampaignRules", "DecideLoad");
        Assert.True(refusal >= 0 && decide > refusal, "the save load prefix calls RotHostRules.WorldRefusal and then FreshCampaignRules.DecideLoad");

        var finished = Body("RealmOfThronesCoopCompat.HeadlessCampaignManager", "OnLoadFinished");
        var passes = CallIndex(finished, "WorldCheck", "Passes");
        var loaded = finished.FindIndex(i => i.Operand is Mono.Cecil.MethodReference m && m.Name == "set_IsLoaded");
        Assert.True(passes >= 0 && loaded > passes, "OnLoadFinished calls WorldCheck.Passes before set_IsLoaded");
        Assert.Contains(finished[passes + 1].OpCode.FlowControl, new[] { Mono.Cecil.Cil.FlowControl.Cond_Branch });
        Assert.Equal(1, finished.Count(i => i.Operand is Mono.Cecil.MethodReference m && m.Name == "set_IsLoaded"));
    }

    [CompiledCompatFact(needsGame: true)]
    public void The_compiled_module_lists_its_types_without_harmony()
    {
        var context = new NoHarmonyContext(new[]
        {
            Path.Combine(GameFolder, "bin", "Win64_Shipping_Client"),
            Path.Combine(GameFolder, "Modules", "SandBox", "bin", "Win64_Shipping_Client"),
            Path.Combine(GameFolder, "Modules", "SandBoxCore", "bin", "Win64_Shipping_Client"),
        });
        try
        {
            var assembly = context.LoadFromStream(new MemoryStream(File.ReadAllBytes(CompatDll)));
            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                throw new Xunit.Sdk.XunitException("GetTypes threw: " + string.Join(" | ", ex.LoaderExceptions.Select(e => e?.Message)));
            }
            Assert.Contains(types, t => t.FullName == "RealmOfThronesCoopCompat.SubModule");
            // The context really keeps Harmony out: MapRedirect's Harmony field cannot be read.
            var harmonyField = types.Single(t => t.FullName == "RealmOfThronesCoopCompat.MapRedirect").GetField("_harmony", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(harmonyField);
            Assert.ThrowsAny<Exception>(() => harmonyField!.FieldType);
        }
        finally
        {
            context.Unload();
        }
    }

    private sealed class NoHarmonyContext : System.Runtime.Loader.AssemblyLoadContext
    {
        private readonly string[] _folders;

        public NoHarmonyContext(string[] folders) : base("rot-compat-no-harmony", isCollectible: true)
        {
            _folders = folders;
        }

        protected override Assembly? Load(AssemblyName name)
        {
            if (string.Equals(name.Name, "0Harmony", StringComparison.OrdinalIgnoreCase))
                throw new FileNotFoundException("0Harmony is kept out of this context on purpose.", "0Harmony.dll");
            if (name.Name == null || name.Name.StartsWith("System", StringComparison.Ordinal) || name.Name.StartsWith("Microsoft.", StringComparison.Ordinal)
                || name.Name == "mscorlib" || name.Name == "netstandard")
                return null;
            foreach (var folder in _folders)
            {
                var path = Path.Combine(folder, name.Name + ".dll");
                if (File.Exists(path))
                    return LoadFromAssemblyPath(path);
            }
            return null;
        }
    }

    [Theory]
    [InlineData(@"C:\srv\engine\bin\Win64_Shipping_Server\", true, true)]
    [InlineData(@"C:\srv\engine\bin\Win64_Shipping_Server\", false, false)]
    [InlineData(@"C:\Games\Bannerlord\bin\Win64_Shipping_Client\", true, false)]
    [InlineData(@"C:\Games\Bannerlord\bin\Win64_Shipping_Client\", false, false)]
    [InlineData(null, true, false)]
    public void Only_the_server_with_the_rot_map_registers_the_resolver(string? baseDirectory, bool rotMap, bool registers)
    {
        var ids = new List<string> { "Native", "SandBoxCore", "Sandbox", "CoopNightly", "NavalDLC" };
        if (rotMap)
            ids.AddRange(new[] { "ROT-Core", "ROT_Map" });
        Assert.Equal(registers, HarmonyLocator.ShouldRegister(baseDirectory!, ids));
        // Single player: the client folder without Coop.
        Assert.False(HarmonyLocator.ShouldRegister(@"C:\Games\Bannerlord\bin\Win64_Shipping_Client\", new[] { "Native", "Sandbox", "ROT-Core", "ROT_Map" }));
    }

    [Fact]
    public void The_resolve_handler_subscribes_once_passes_plain_names_and_never_throws()
    {
        var asked = new List<(string Simple, string Requested)>();
        var harmony = typeof(Harmony).Assembly;
        var handler = new ServerResolveHandler((simple, request) => { asked.Add((simple, request.Name)); return simple == "0Harmony" ? harmony : null; });
        var subscribed = new List<ResolveEventHandler>();

        Assert.True(handler.Register(subscribed.Add));
        Assert.False(handler.Register(subscribed.Add));
        Assert.False(handler.Register(subscribed.Add));
        Assert.Single(subscribed);

        // Odd names and names without a rule (Coop's own assemblies, SandBox) never reach the finder.
        foreach (var name in new[] { "", @"..\x", ", Version=1.0.0.0", "GameInterface", "Common, Version=1.0.0.0", "Coop.Core", "SandBox", "ROT" })
            Assert.Null(subscribed[0](null!, new ResolveEventArgs(name)));
        Assert.Empty(asked);
        Assert.Null(subscribed[0](null!, new ResolveEventArgs("MCMv5, Version=5.11.4.0, Culture=neutral, PublicKeyToken=null")));
        Assert.Same(harmony, subscribed[0](null!, new ResolveEventArgs("0Harmony, Version=2.4.2.0, Culture=neutral, PublicKeyToken=null")));
        // The Windows Forms tripwire reaches the finder, which only logs it.
        Assert.Null(subscribed[0](null!, new ResolveEventArgs("System.Windows.Forms, Version=4.0.0.0")));
        Assert.Equal(new[] { ("MCMv5", "MCMv5, Version=5.11.4.0, Culture=neutral, PublicKeyToken=null"), ("0Harmony", "0Harmony, Version=2.4.2.0, Culture=neutral, PublicKeyToken=null"),
            ("System.Windows.Forms", "System.Windows.Forms, Version=4.0.0.0") }, asked);
        Assert.Null(handler.Resolve(null!, null!));

        var throwing = new ServerResolveHandler((_, _) => throw new FileLoadException("locked"));
        Assert.Null(throwing.Resolve(null!, new ResolveEventArgs("0Harmony")));
    }

    // Loading a library can ask for the same name again on the same thread; that ask gets null
    // instead of a second load, and the name is asked for normally once the first load is done.
    [Fact]
    public void The_resolve_handler_never_resolves_a_name_inside_its_own_resolve()
    {
        var depth = 0;
        var inner = new List<System.Reflection.Assembly?>();
        ServerResolveHandler? handler = null;
        handler = new ServerResolveHandler((simple, _) =>
        {
            depth++;
            if (depth == 1)
            {
                inner.Add(handler!.Resolve(null!, new ResolveEventArgs("MCMv5")));
                inner.Add(handler.Resolve(null!, new ResolveEventArgs("SandBox.View")));
            }
            return typeof(Harmony).Assembly;
        });

        Assert.Same(typeof(Harmony).Assembly, handler.Resolve(null!, new ResolveEventArgs("MCMv5")));
        Assert.Null(inner[0]);
        Assert.Same(typeof(Harmony).Assembly, inner[1]);
        Assert.Same(typeof(Harmony).Assembly, handler.Resolve(null!, new ResolveEventArgs("MCMv5")));
        Assert.Equal(3, depth);
    }

    // ---- the settlement ids a distance cache names (CacheIds), in the game's own file layout ----

    private static byte[] CacheFile(bool all, int faces = 1)
    {
        using var memory = new MemoryStream();
        using (var writer = new BinaryWriter(memory))
        {
            writer.Write(11u);
            writer.Write(22u);
            writer.Write(2);
            foreach (var (from, to) in new[] { ("town_A", "town_B"), ("ROT_town1", "ROT_castle2") })
            {
                writer.Write(from);
                writer.Write(false);
                writer.Write(1);
                writer.Write(to);
                writer.Write(true);
                writer.Write(12.5f);
                if (all)
                    writer.Write(0.5f);
            }
            writer.Write(1);
            writer.Write("town_A");
            writer.Write("ROT_village3");
            writer.Write(faces);
            for (var i = 0; i < faces; i++)
            {
                writer.Write(42 + i);
                writer.Write("ROT_village4");
                writer.Write(false);
            }
        }
        return memory.ToArray();
    }

    private static readonly string[] CacheFileIds = { "ROT_castle2", "ROT_town1", "ROT_village3", "ROT_village4", "town_A", "town_B" };

    [Theory]
    [InlineData("settlements_distance_cache_Default.bin")]
    [InlineData("settlements_distance_cache_Naval.bin")]
    [InlineData("settlements_distance_cache_All.bin")]
    public void Every_settlement_id_in_a_cache_is_read(string name)
    {
        var all = CacheIds.HasLandRatio(name);
        Assert.Equal(name.Contains("_All"), all);
        var ids = CacheIds.Read(new MemoryStream(CacheFile(all)), all, out var error);
        Assert.Null(error);
        Assert.Equal(CacheFileIds, ids!.OrderBy(id => id, StringComparer.Ordinal));
    }

    [Fact]
    public void A_short_or_odd_cache_file_gives_null_with_a_reason_and_never_throws()
    {
        var file = CacheFile(false);
        Assert.Null(CacheIds.Read(new MemoryStream(file, 0, file.Length - 3), false, out var shortError));
        Assert.Contains("EndOfStreamException", shortError);

        var huge = (byte[])file.Clone();
        BitConverter.GetBytes(int.MaxValue).CopyTo(huge, 8);
        Assert.Null(CacheIds.Read(new MemoryStream(huge), false, out var countError));
        Assert.Contains("does not fit", countError);

        Assert.Null(CacheIds.Read(new MemoryStream(new byte[] { 1, 2 }), false, out _));
        // Read as the wrong type, a Default file runs off its own layout instead of throwing.
        Assert.Null(CacheIds.Read(new MemoryStream(file), true, out var wrongType));
        Assert.NotNull(wrongType);
    }

    [Fact]
    public void A_world_lacking_the_caches_settlements_gets_one_line_with_counts_and_the_first_five()
    {
        var ids = new HashSet<string>(CacheFileIds.Concat(new[] { "ROT_town9", "ROT_town8" }));
        var vanilla = new HashSet<string> { "town_A", "town_B" };

        var line = CacheIds.MissingLine(@"x\settlements_distance_cache_Default.bin", ids, vanilla.Contains);
        Assert.StartsWith("Map redirect: settlements_distance_cache_Default.bin names 8 settlements and this world lacks 6 of them", line);
        Assert.Contains("(first ROT_castle2, ROT_town1, ROT_town8, ROT_town9, ROT_village3)", line);
        Assert.Contains("The save may not be a Realm of Thrones world", line);
        Assert.DoesNotContain("vanilla", line);
        Assert.Null(CacheIds.MissingLine("x", ids, _ => true));
    }

    // ---- crash trace noise, with the frames boot test 2 logged (rot-wave1 runD2 coopcompat.log) ----

    [Fact]
    public void The_engine_asking_a_dynamic_assembly_for_its_file_is_skipped()
    {
        var frames = new[] { "System.Reflection.Emit.InternalAssemblyBuilder.get_Location", "TaleWorlds.DotNet.Managed.GetModuleList", "ManagedCallbacks.LibraryCallbacksGenerated.Managed_GetModuleList" };
        Assert.True(CrashTraceRules.IsHandledNoise("System.NotSupportedException", "The invoked member is not supported in a dynamic assembly.", frames));
        Assert.False(CrashTraceRules.IsHandledNoise("System.NotSupportedException", "Specified method is not supported.", frames));
        Assert.False(CrashTraceRules.IsHandledNoise("System.NotSupportedException", "The invoked member is not supported in a dynamic assembly.", new[] { "Coop.Something.Run" }));
    }

    [Fact]
    public void Coops_ambiguous_harmony_lookup_while_wiring_services_is_skipped()
    {
        var frames = new[]
        {
            "System.RuntimeType.GetMethodImplCommon", "System.Type.GetMethod", "HarmonyLib.AccessTools+<>c__DisplayClass50_0.<Method>b__0",
            "HarmonyLib.AccessTools.FindIncludingBaseTypes", "HarmonyLib.AccessTools.Method",
            "GameInterface.GameInterfaceModule+<>c__DisplayClass6_0.<AttachToComponentRegistration>b__3",
            "Autofac.Core.ResolvedParameter+<>c__DisplayClass3_0.<CanSupplyValue>b__0",
        };
        // Harmony 2.4.2's AccessTools.Method catches the runtime's exception and asks again, so this
        // exact shape is handled whoever the caller is.
        Assert.True(CrashTraceRules.IsHandledNoise("System.Reflection.AmbiguousMatchException", "Ambiguous match found.", frames));
        Assert.True(CrashTraceRules.IsHandledNoise("System.Reflection.AmbiguousMatchException", "Ambiguous match found.", frames.Take(5).Append("RealmOfThronesCoopCompat.MapRedirect.PatchAll").ToArray()));
        // Harmony's own rethrow when the second ask fails too stays, and so does GetMethod used directly.
        Assert.False(CrashTraceRules.IsHandledNoise("System.Reflection.AmbiguousMatchException", "Ambiguous match in Harmony patch for GameInterface.X:Y",
            new[] { "HarmonyLib.AccessTools.Method", "GameInterface.GameInterfaceModule+<>c__DisplayClass6_0.<AttachToComponentRegistration>b__3" }));
        Assert.False(CrashTraceRules.IsHandledNoise("System.Reflection.AmbiguousMatchException", "Ambiguous match found.",
            new[] { "System.RuntimeType.GetMethodImplCommon", "System.Type.GetMethod", "ROT.SubModule.OnSubModuleLoad" }));
    }

    private static readonly string[] CollectorFrames =
    {
        "System.Runtime.Loader.AssemblyLoadContext.LoadFromPath", "System.Runtime.Loader.AssemblyLoadContext.LoadFromAssemblyPath",
        "System.Reflection.Assembly.LoadFrom", "System.Reflection.Assembly.LoadFromResolveHandler",
        "System.Runtime.Loader.AssemblyLoadContext.InvokeResolveEvent", "System.Runtime.Loader.AssemblyLoadContext.OnAssemblyResolve",
        "System.Reflection.RuntimeModule.GetTypes", "System.Reflection.Assembly.GetTypes",
        "TaleWorlds.Engine.CrashInformationCollector.CollectInformation", "ManagedCallbacks.EngineCallbacksGenerated.CrashInformationCollector_CollectInformation",
    };

    private static readonly string[] GetTypesSafeFrames =
    {
        "System.Runtime.Loader.AssemblyLoadContext.LoadFromPath", "System.Runtime.Loader.AssemblyLoadContext.LoadFromAssemblyPath",
        "System.Reflection.Assembly.LoadFrom", "System.Reflection.Assembly.LoadFromResolveHandler",
        "System.Runtime.Loader.AssemblyLoadContext.InvokeResolveEvent", "System.Reflection.RuntimeModule.GetTypes",
        "System.Reflection.Assembly.GetTypes", "TaleWorlds.Library.Extensions.GetTypesSafe",
        "TaleWorlds.CampaignSystem.Conversation.ConversationManager.InitializeTags",
    };

    [Fact]
    public void A_missing_ui_assembly_while_the_engine_lists_types_is_skipped()
    {
        const string message = @"Could not load file or assembly 'C:\taomtest164\server\engine\Modules\NavalDLC\bin\Win64_Shipping_Server\StoryMode.GauntletUI.dll'. The system cannot find the file specified.";
        Assert.True(CrashTraceRules.IsHandledNoise("System.IO.FileNotFoundException", message, CollectorFrames));
        Assert.True(CrashTraceRules.IsHandledNoise("System.IO.FileNotFoundException", message, GetTypesSafeFrames));
        // The same miss while Coop's patches compile is not the engine's own probing, so it stays.
        var coopJit = CollectorFrames.Take(6).Concat(new[] { "MonoMod.Core.Interop.CoreCLR+V60.InvokeCompileMethod", "Coop.Naval.PortScreen.Patches.PortScreenHandlerPatches+<TargetMethods>d__0.MoveNext" }).ToArray();
        Assert.False(CrashTraceRules.IsHandledNoise("System.IO.FileNotFoundException", message, coopJit));
        // Our own missing Harmony (boot test 2's real failure) stays.
        Assert.False(CrashTraceRules.IsHandledNoise("System.IO.FileNotFoundException", "Could not load file or assembly '0Harmony, Version=2.4.2.0'.", new[] { "RealmOfThronesCoopCompat.SubModule.SafeInstall" }));
        // The probe must be the first frame outside .NET, not just somewhere down the stack.
        var deeper = CollectorFrames.Take(8).Append("ROT.SubModule.OnGameStart").Append("TaleWorlds.Library.Extensions.GetTypesSafe").ToArray();
        Assert.False(CrashTraceRules.IsHandledNoise("System.IO.FileNotFoundException", message, deeper));
    }

    // A mod DLL with a missing dependency is what the trace is for, even under the same probe.
    [Theory]
    [InlineData(@"Could not load file or assembly 'C:\srv\engine\Modules\ROT-Core\bin\Win64_Shipping_Server\Bannerlord.UIExtenderEx.dll'. The system cannot find the file specified.")]
    [InlineData("Could not load file or assembly 'MCMv5, Version=5.12.3.0, Culture=neutral, PublicKeyToken=null'.")]
    [InlineData("Could not load file or assembly 'ROT, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null'.")]
    [InlineData("Could not load file or assembly 'RealmOfThrones.CoopCompat, Version=0.1.0.0'.")]
    [InlineData("Could not load file or assembly 'StoryMode.GauntletUI.Extra, Version=1.0.0.0'.")]
    [InlineData("Could not load file or assembly")]
    public void Any_other_missing_assembly_under_the_engine_probe_stays_visible(string message)
    {
        Assert.False(CrashTraceRules.IsHandledNoise("System.IO.FileNotFoundException", message, GetTypesSafeFrames));
        Assert.False(CrashTraceRules.IsHandledNoise("System.Reflection.ReflectionTypeLoadException", "Unable to load one or more of the requested types. " + message, GetTypesSafeFrames));
    }

    [Fact]
    public void The_type_load_error_that_follows_it_is_skipped_there_too()
    {
        const string message = "Unable to load one or more of the requested types.  Could not load file or assembly 'StoryMode.GauntletUI, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null'.";
        Assert.True(CrashTraceRules.IsHandledNoise("System.Reflection.ReflectionTypeLoadException", message, new[] { "System.Reflection.RuntimeModule.GetTypes", "System.Reflection.Assembly.GetTypes", "TaleWorlds.Engine.CrashInformationCollector.CollectInformation" }));
        Assert.True(CrashTraceRules.IsHandledNoise("System.Reflection.ReflectionTypeLoadException", message, new[] { "System.Reflection.RuntimeModule.GetTypes", "System.Reflection.Assembly.GetTypes", "TaleWorlds.Library.Extensions.GetTypesSafe" }));
        Assert.False(CrashTraceRules.IsHandledNoise("System.Reflection.ReflectionTypeLoadException", message, new[] { "System.Reflection.Assembly.GetTypes", "RealmOfThronesCoopCompat.MapRedirect.LoadableTypes" }));
        // One engine UI assembly and one mod assembly in the same message: the mod one keeps it visible.
        Assert.False(CrashTraceRules.IsHandledNoise("System.Reflection.ReflectionTypeLoadException",
            message + " Could not load file or assembly 'ROT, Version=1.0.0.0'.", new[] { "System.Reflection.Assembly.GetTypes", "TaleWorlds.Library.Extensions.GetTypesSafe" }));
    }

    // .NET writes each loader error of a type list on its own line. A missing engine UI assembly
    // next to a type of a mod that does not load must stay visible, on one line or many.
    [Fact]
    public void A_type_list_error_that_also_holds_other_loader_errors_stays_visible()
    {
        var frames = new[] { "System.Reflection.RuntimeModule.GetTypes", "System.Reflection.Assembly.GetTypes", "TaleWorlds.Library.Extensions.GetTypesSafe" };
        const string header = "Unable to load one or more of the requested types.";
        const string ui = "Could not load file or assembly 'StoryMode.GauntletUI, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null'. The system cannot find the file specified.";
        Assert.True(CrashTraceRules.IsHandledNoise("System.Reflection.ReflectionTypeLoadException", header + "\r\n" + ui + "\r\n" + ui.Replace("StoryMode.GauntletUI", "NavalDLC.View"), frames));

        foreach (var other in new[]
        {
            "Could not load type 'ROT.ROTSettingsMCM' from assembly 'ROT, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null'.",
            "Method 'OnTick' in type 'ROT.X' from assembly 'ROT, Version=1.0.0.0' does not have an implementation.",
            "Inheritance security rules violated by type: 'ROT.Y'.",
        })
        {
            Assert.False(CrashTraceRules.IsHandledNoise("System.Reflection.ReflectionTypeLoadException", header + "\r\n" + ui + "\r\n" + other, frames), other);
            if (!other.StartsWith("Inheritance"))
                Assert.False(CrashTraceRules.IsHandledNoise("System.Reflection.ReflectionTypeLoadException", header + "  " + ui + "  " + other, frames), other);
        }
    }

    [Fact]
    public void Coop_checking_native_runtime_files_for_being_assemblies_is_skipped()
    {
        var frames = new[] { "System.Reflection.AssemblyName.nGetFileInformation", "System.Reflection.AssemblyName.GetAssemblyName", "GameInterface.AutoSync.Builders.AutoSyncBuilder.IsManagedAssembly", "System.Linq.Enumerable+WhereEnumerableIterator`1.MoveNext" };
        const string message = @"Could not load file or assembly 'C:\taomtest164\server\engine\dotnet\shared\Microsoft.NETCore.App\6.0.36\api-ms-win-core-console-l1-1-0.dll'. The module was expected to contain an assembly manifest.";
        Assert.True(CrashTraceRules.IsHandledNoise("System.BadImageFormatException", message, frames));
        Assert.False(CrashTraceRules.IsHandledNoise("System.BadImageFormatException", message, new[] { "System.Reflection.AssemblyName.GetAssemblyName", "TaleWorlds.Library.AssemblyLoader.LoadFrom" }));
    }

    [Fact]
    public void A_game_text_node_without_an_attribute_is_skipped_only_where_it_is_caught()
    {
        var frames = new[] { "TaleWorlds.Core.GameTextManager.LoadFromXML", "TaleWorlds.Core.GameTextManager.LoadGameTexts", "TaleWorlds.Core.Game.Initialize" };
        Assert.True(CrashTraceRules.IsHandledNoise("System.NullReferenceException", "Object reference not set to an instance of an object.", frames));
        // The distance cache reader's failure on a world without the cache's settlements stays visible.
        var deserialize = new[]
        {
            "TaleWorlds.CampaignSystem.Map.DistanceCache.NavigationCacheElement`1.get_StringId", "TaleWorlds.CampaignSystem.Map.DistanceCache.NavigationCacheElement`1.Sort",
            "TaleWorlds.CampaignSystem.Map.DistanceCache.NavigationCache`1.Deserialize", "A.G.A", "A.G.a", "A.G.Load", "TaleWorlds.CampaignSystem.Campaign.LoadMapScene",
        };
        Assert.False(CrashTraceRules.IsHandledNoise("System.NullReferenceException", "Object reference not set to an instance of an object.", deserialize));
        Assert.False(CrashTraceRules.IsHandledNoise("System.NullReferenceException", "Object reference not set to an instance of an object.", frames.Skip(1).ToArray()));
    }

    [Fact]
    public void The_engine_building_a_stack_text_over_a_patched_method_is_skipped()
    {
        var frames = new[] { "System.Reflection.MemberInfo.get_MetadataToken", "TaleWorlds.DotNet.Managed.GetStackTraceRaw", "ManagedCallbacks.LibraryCallbacksGenerated.Managed_GetStackTraceRaw", "TaleWorlds.CampaignSystem.Map.DistanceCache.NavigationCacheElement`1.get_StringId" };
        Assert.True(CrashTraceRules.IsHandledNoise("System.InvalidOperationException", "Operation is not valid due to the current state of the object.", frames));
        Assert.False(CrashTraceRules.IsHandledNoise("System.InvalidOperationException", "Operation is not valid due to the current state of the object.", new[] { "System.Linq.ThrowHelper.ThrowNoElementsException", "TaleWorlds.DotNet.Managed.GetStackTraceRaw" }));
    }

    [Fact]
    public void A_type_without_a_rule_is_never_noise()
    {
        Assert.False(CrashTraceRules.IsHandledNoise("System.ArgumentOutOfRangeException", "Index was out of range.", new[] { "System.ThrowHelper.ThrowArgumentOutOfRange_IndexException", "TaleWorlds.CampaignSystem.Settlements.Settlement.Deserialize" }));
        Assert.False(CrashTraceRules.IsHandledNoise(null!, "x", new[] { "y" }));
        Assert.False(CrashTraceRules.IsHandledNoise("System.NotSupportedException", "dynamic assembly", null!));
    }

    // ---- which save the server starts a new Realm of Thrones campaign for (FreshCampaignRules) ----

    private static readonly string[] VanillaSaveModules = { "Native", "SandBoxCore", "Sandbox", "CustomBattle", "NavalDLC", "Coop" };

    private static (string Exe, string Saves) TemplateFolders(string root)
    {
        var exe = Path.Combine(root, "engine", "bin", "Win64_Shipping_Server");
        var saves = Path.Combine(root, "server-data", "Game Saves");
        Directory.CreateDirectory(exe);
        Directory.CreateDirectory(saves);
        return (exe, saves);
    }

    [Fact]
    public void A_byte_copy_of_either_template_without_rot_modules_starts_a_new_campaign()
    {
        var root = Path.Combine(Path.GetTempPath(), "rot-fresh-" + Guid.NewGuid().ToString("N"));
        try
        {
            var (exe, saves) = TemplateFolders(root);
            File.WriteAllBytes(Path.Combine(exe, "default_new_game_warsails.sav"), new byte[] { 1, 2, 3, 4 });
            File.WriteAllBytes(Path.Combine(saves, "default_new_game.sav"), new byte[] { 5, 6, 7 });
            var warSailsCopy = Path.Combine(saves, "rot1.sav");
            File.WriteAllBytes(warSailsCopy, new byte[] { 1, 2, 3, 4 });
            var plainCopy = Path.Combine(saves, "rot2.sav");
            File.WriteAllBytes(plainCopy, new byte[] { 5, 6, 7 });

            Assert.Equal(FreshCampaignRules.SaveKind.TemplateCopy, FreshCampaignRules.Classify(VanillaSaveModules, warSailsCopy, FreshCampaignRules.TemplateCandidates(warSailsCopy, exe), out var template));
            Assert.Equal(Path.Combine(exe, "default_new_game_warsails.sav"), template);
            // The template next to the save, with the metadata unreadable.
            Assert.Equal(FreshCampaignRules.SaveKind.TemplateCopy, FreshCampaignRules.Classify(null!, plainCopy, FreshCampaignRules.TemplateCandidates(plainCopy, exe), out template));
            Assert.Equal(Path.Combine(saves, "default_new_game.sav"), template);

            var line = FreshCampaignRules.DecisionLine(FreshCampaignRules.SaveKind.TemplateCopy, "rot1", warSailsCopy, VanillaSaveModules, template);
            Assert.StartsWith("Fresh campaign: save 'rot1' is the server's copy of " + template, line);
            Assert.Contains("a new Realm of Thrones campaign starts in its place", line);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void A_save_with_a_rot_module_loads_and_a_readable_foreign_save_is_refused()
    {
        var root = Path.Combine(Path.GetTempPath(), "rot-fresh-" + Guid.NewGuid().ToString("N"));
        try
        {
            var (exe, saves) = TemplateFolders(root);
            File.WriteAllBytes(Path.Combine(exe, "default_new_game_warsails.sav"), new byte[] { 1, 2, 3, 4 });
            var save = Path.Combine(saves, "rot1.sav");
            File.WriteAllBytes(save, new byte[] { 1, 2, 3, 4 });
            var templates = FreshCampaignRules.TemplateCandidates(save, exe);

            // Same bytes, but the metadata lists a Realm of Thrones module: the server's own save.
            foreach (var id in new[] { "ROT-Core", "ROT-Content", "rot_map", "ROT-Dragon" })
                Assert.Equal(FreshCampaignRules.SaveKind.RotWorld, FreshCampaignRules.Classify(VanillaSaveModules.Append(id), save, templates, out _));
            Assert.Equal("Fresh campaign: save 'rot1' was made with Realm of Thrones (modules ROT-Core, ROT_Map), so it loads normally and no new campaign starts.",
                FreshCampaignRules.DecisionLine(FreshCampaignRules.SaveKind.RotWorld, "rot1", save, new[] { "ROT-Core", "ROT_Map" }, null!));

            var other = Path.Combine(saves, "calradia.sav");
            File.WriteAllBytes(other, new byte[] { 1, 2, 3, 5 });
            Assert.Equal(FreshCampaignRules.SaveKind.OtherWorld, FreshCampaignRules.Classify(VanillaSaveModules, other, FreshCampaignRules.TemplateCandidates(other, exe), out var template));
            Assert.Null(template);
            var loud = FreshCampaignRules.DecisionLine(FreshCampaignRules.SaveKind.OtherWorld, "calradia", other, VanillaSaveModules, null!);
            Assert.StartsWith("!!! Fresh campaign: REFUSED save 'calradia': it was not made with Realm of Thrones", loud);
            Assert.Contains("Pick a new save name", loud);
            Assert.Contains("modules Native, SandBoxCore", loud);

            // A readable header without Realm of Thrones and no file to compare is refused too.
            var missing = Path.Combine(saves, "nope.sav");
            Assert.Equal(FreshCampaignRules.SaveKind.OtherWorld, FreshCampaignRules.Classify(VanillaSaveModules, missing, templates, out _));
            Assert.Contains("file not found", FreshCampaignRules.DecisionLine(FreshCampaignRules.SaveKind.OtherWorld, "nope", missing, VanillaSaveModules, null!));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    // A save named like a template that already holds a campaign is never its own template.
    [Fact]
    public void The_saves_own_path_never_counts_as_its_template()
    {
        var root = Path.Combine(Path.GetTempPath(), "rot-fresh-" + Guid.NewGuid().ToString("N"));
        try
        {
            var (exe, saves) = TemplateFolders(root);
            var save = Path.Combine(saves, "default_new_game_warsails.sav");
            File.WriteAllBytes(save, new byte[] { 9, 9 });

            var templates = FreshCampaignRules.TemplateCandidates(save, exe);
            Assert.DoesNotContain(save, templates, StringComparer.OrdinalIgnoreCase);
            Assert.Equal(new[] { "default_new_game_warsails.sav", "default_new_game.sav", "default_new_game.sav" }, templates.Select(Path.GetFileName));
            Assert.Equal(FreshCampaignRules.SaveKind.OtherWorld, FreshCampaignRules.Classify(VanillaSaveModules, save, templates, out _));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void A_path_query_end_without_a_settlement_component_or_face_is_unsafe()
    {
        Assert.Equal(PathQueryRules.Unsafe.None, PathQueryRules.Check(true, true, 0));
        Assert.Equal(PathQueryRules.Unsafe.NoFace, PathQueryRules.Check(true, true, -1));
        Assert.Equal(PathQueryRules.Unsafe.NoComponent, PathQueryRules.Check(true, false, 5));
        Assert.Equal(PathQueryRules.Unsafe.NoSettlement, PathQueryRules.Check(false, false, 5));
        Assert.Equal(PathQueryRules.Unsafe.NoFace, PathQueryRules.CheckPair(PathQueryRules.Unsafe.None, PathQueryRules.Unsafe.NoFace));
        Assert.Equal(PathQueryRules.Unsafe.NoComponent, PathQueryRules.CheckPair(PathQueryRules.Unsafe.NoComponent, PathQueryRules.Unsafe.NoFace));
    }

    // The dedicated server only loads a SubModule whose tags it accepts, and it refuses
    // IsNoRenderModeElement false (the tag that kept ROT.dll off it) as well as DedicatedServerType
    // none, while the game refuses IsNoRenderModeElement true. No tags loads on both.
    [Fact]
    public void The_compat_submodule_has_no_tags_so_it_loads_on_the_game_and_the_server()
    {
        var xml = XDocument.Load(Path.Combine(RepoRoot(), "compat", "RealmOfThronesCoopCompat", "SubModule.xml"));
        var subModule = Assert.Single(xml.Root!.Element("SubModules")!.Elements("SubModule"));

        Assert.Equal("RealmOfThrones.CoopCompat", xml.Root.Element("Id")!.Attribute("value")!.Value);
        Assert.Equal("RealmOfThrones.CoopCompat.dll", subModule.Element("DLLName")!.Attribute("value")!.Value);
        Assert.Empty(subModule.Element("Tags")!.Elements());
    }

    // COODSZ_TEST_LOOPBACK_BIND is for our own live tests only: only the value 1 binds the game port to
    // 127.0.0.1, it patches nothing otherwise, and no tool sets it, so a host's server stays open to players.
    // TestLoopback needs Coop and the module's log, so it is read here instead of linked.
    [Fact]
    public void The_test_loopback_turns_on_only_with_1_and_no_tool_sets_it()
    {
        var loopback = SourceFiles.Read(Path.Combine("compat", "RealmOfThronesCoopCompat", "Server", "TestLoopback.cs")).Replace("\r\n", "\n");
        Assert.Contains("public const string Variable = \"COODSZ_TEST_LOOPBACK_BIND\";", loopback);
        Assert.Contains("public static bool IsOn(string value) => string.Equals(value?.Trim(), \"1\", StringComparison.Ordinal);", loopback);
        var install = SourceFiles.Method(loopback, "public static void Install()");
        Assert.True(install.IndexOf("if (!IsOn(Environment.GetEnvironmentVariable(Variable)))\n                return;", StringComparison.Ordinal) is var gate && gate >= 0
            && gate < install.IndexOf("SafeTryPatch();", StringComparison.Ordinal), "TestLoopback patches before it checks the variable.");

        foreach (var file in Directory.EnumerateFiles(Path.Combine(RepoRoot(), "src"), "*", SearchOption.AllDirectories)
            .Where(path => !path.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar) && !path.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)
                && new[] { ".cs", ".xaml", ".ps1", ".json", ".targets", ".csproj" }.Contains(Path.GetExtension(path))))
            Assert.DoesNotContain("COODSZ_TEST_LOOPBACK_BIND", File.ReadAllText(file));
        Assert.Contains("COODSZ_TEST_LOOPBACK_BIND=1", SourceFiles.Read(Path.Combine("compat", "RealmOfThronesCoopCompat", "TESTERS.txt")));
    }

    // Stand ins: Coop's WeaponComponentData constructor call with no item, and Realm of Thrones'
    // patch1 on it, which reads the item's id without a null check.
    private sealed class WeaponItem
    {
        public string StringId = "";
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int NewWeapon(WeaponItem? item, int weaponFlags) => weaponFlags;

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void RotWeaponPatch1(WeaponItem item, ref int weaponFlags)
    {
        if (item.StringId == "giant_club")
            weaponFlags |= 0x8000000;
    }

    // The weapon gate: without it a weapon with no item throws inside RoT's patch (the server
    // dropped the message); with it the weapon is built, RoT still flags its giant club and the
    // first skip in a session is logged once, again after the session changes.
    [Fact]
    public void A_weapon_coop_sends_with_no_item_skips_realm_of_thrones_patch_and_logs_once_per_session()
    {
        var harmony = new Harmony("coodsz.tests.rot-weapon." + Guid.NewGuid().ToString("N"));
        var before = RotRules.OnWeaponSkipped;
        var logged = 0;
        MethodInfo Here(string name) => typeof(RealmOfThronesCompatTests).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)!;
        try
        {
            harmony.Patch(Here(nameof(NewWeapon)), prefix: new HarmonyMethod(Here(nameof(RotWeaponPatch1))));
            Assert.Throws<NullReferenceException>(() => NewWeapon(null, 4));

            harmony.Patch(Here(nameof(RotWeaponPatch1)), prefix: new HarmonyMethod(typeof(RotRules), nameof(RotRules.SkipWithoutItem)));
            RotRules.ResetWeaponSkipLog();
            RotRules.OnWeaponSkipped = () => logged++;
            Assert.Equal(4, NewWeapon(null, 4));
            Assert.Equal(4, NewWeapon(null, 4));
            Assert.Equal(1, logged);
            Assert.Equal(4 | 0x8000000, NewWeapon(new WeaponItem { StringId = "giant_club" }, 4));
            Assert.Equal(4, NewWeapon(new WeaponItem { StringId = "sword" }, 4));

            RotRules.ResetWeaponSkipLog();
            NewWeapon(null, 4);
            Assert.Equal(2, logged);
        }
        finally
        {
            RotRules.OnWeaponSkipped = before;
            harmony.UnpatchAll(harmony.Id);
        }
        Assert.Equal("ROT.HarmonyPatches.Core.ROTWeaponComponetData", RotRules.WeaponPatchType);
        Assert.Equal("patch1", RotRules.WeaponPatchMethod);
    }

    // The real patch1 is there once, takes the item first under the name the prefix reads, and returns void: a
    // bool patch1 would be a prefix itself, and skipping it would skip the WeaponComponentData constructor too.
    [RealmOfThronesClientTests.RotDllFact]
    public void Realm_of_thrones_weapon_patch_takes_the_item_by_name()
    {
        using var module = Mono.Cecil.ModuleDefinition.ReadModule(RealmOfThronesClientTests.RotDll);
        var patch = Assert.Single(module.GetType(RotRules.WeaponPatchType).Methods, m => m.Name == RotRules.WeaponPatchMethod);
        Assert.Equal("System.Void", patch.ReturnType.FullName);
        Assert.Equal("item", patch.Parameters[0].Name);
        Assert.Equal("TaleWorlds.Core.ItemObject", patch.Parameters[0].ParameterType.FullName);
    }

    // The gate is installed on the server and on players' PCs, and resets its log on a session edge.
    [CompiledCompatFact]
    public void The_weapon_gate_is_installed_on_both_sides()
    {
        using var module = Mono.Cecil.ModuleDefinition.ReadModule(CompatDll);
        IEnumerable<Mono.Cecil.TypeDefinition> WithNested(Mono.Cecil.TypeDefinition type) => new[] { type }.Concat(type.NestedTypes.SelectMany(WithNested));
        IEnumerable<string> Operands(Mono.Cecil.TypeDefinition type) => WithNested(type).SelectMany(t => t.Methods).Where(m => m.HasBody)
            .SelectMany(m => m.Body.Instructions).Select(i => i.Operand).OfType<Mono.Cecil.MemberReference>()
            .Where(m => m.DeclaringType != null).Select(m => m.DeclaringType.Name + "." + m.Name);

        Assert.Equal(2, Operands(module.GetType("RealmOfThronesCoopCompat.SubModule")).Count(name => name == "WeaponItemGate.Install"));
        var gate = Operands(module.GetType("RealmOfThronesCoopCompat.WeaponItemGate")).ToList();
        Assert.Contains("RotRules.OnWeaponSkipped", gate);
        Assert.Contains("RotRules.ResetWeaponSkipLog", gate);
        // The prefix is named by nameof, so the compiled install carries its name as a string.
        Assert.Contains("SkipWithoutItem", WithNested(module.GetType("RealmOfThronesCoopCompat.WeaponItemGate")).SelectMany(t => t.Methods).Where(m => m.HasBody)
            .SelectMany(m => m.Body.Instructions).Select(i => i.Operand).OfType<string>());
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "BannerlordCoopLauncher.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
