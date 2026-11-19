using CoodszHost.Services;

namespace BannerlordCoopLauncher.Tests;

public class ExtraBaseMapTests : IDisposable
{
    private readonly string _server = Path.Combine(Path.GetTempPath(), "basemap-" + Guid.NewGuid().ToString("N"), "DedicatedServer");

    private string Modules => Path.Combine(_server, "engine", "Modules");

    public ExtraBaseMapTests()
    {
        Directory.CreateDirectory(Path.Combine(Modules, "SandBox", "ModuleData"));
        Directory.CreateDirectory(Path.Combine(Modules, "DedicatedServer.Windows", "SceneObj", "Main_map"));
    }

    public void Dispose() => Directory.Delete(Path.GetDirectoryName(_server)!, true);

    private void Map(string module) => Directory.CreateDirectory(Path.Combine(Modules, module, "SceneObj", "Main_map"));

    private static readonly string[] AD1259Set = ["Native", "DedicatedServer.Windows", "SandBoxCore", "Sandbox", "Coop", "AD1259"];

    [Fact]
    public void A_clean_server_starts_as_it_is()
    {
        Map("AD1259");

        Assert.Null(ExtraBaseMap.OtherMap(_server, AD1259Set));
    }

    [Fact]
    public void A_SandBox_map_next_to_a_map_mod_names_the_mod()
    {
        Map("AD1259");
        Map("SandBox");

        Assert.Equal("AD1259", ExtraBaseMap.OtherMap(_server, AD1259Set));
    }

    [Fact]
    public void A_SandBox_map_alone_is_left_alone()
    {
        Map("SandBox");

        Assert.Null(ExtraBaseMap.OtherMap(_server, ["Native", "DedicatedServer.Windows", "SandBoxCore", "Sandbox", "Coop"]));
    }

    [Fact]
    public void A_map_mod_that_is_turned_off_does_not_count()
    {
        Map("AD1259");
        Map("SandBox");

        Assert.Null(ExtraBaseMap.OtherMap(_server, ["Native", "DedicatedServer.Windows", "SandBoxCore", "Sandbox", "Coop"]));
    }

    [Fact]
    public void Moving_it_aside_keeps_the_files_in_removed_mods()
    {
        Map("SandBox");
        File.WriteAllText(Path.Combine(Modules, "SandBox", "SceneObj", "Main_map", "scene.xscene"), "map");

        Directory.CreateDirectory(Path.Combine(Modules, "SandBox", "SceneObj", "town_scene"));

        var target = ExtraBaseMap.MoveAside(_server);

        Assert.False(Directory.Exists(Path.Combine(Modules, "SandBox", "SceneObj", "Main_map")));
        Assert.True(Directory.Exists(Path.Combine(Modules, "SandBox", "SceneObj", "town_scene")));
        Assert.True(Directory.Exists(Path.Combine(Modules, "SandBox", "ModuleData")));
        Assert.Equal("map", File.ReadAllText(Path.Combine(target, "scene.xscene")));
        Assert.StartsWith(Path.Combine(_server, ModuleRemoval.RemovedFolderName), target);
        Assert.Contains(@"DedicatedServer\removed-mods\SandBox-Main_map-", ExtraBaseMap.Moved(_server, target));
    }

    [Fact]
    public void A_second_move_in_the_same_second_gets_its_own_name()
    {
        Map("SandBox");
        var first = ExtraBaseMap.MoveAside(_server);
        Map("SandBox");

        var second = ExtraBaseMap.MoveAside(_server);

        Assert.NotEqual(first, second);
        Assert.True(Directory.Exists(first));
        Assert.True(Directory.Exists(second));
    }

    [Fact]
    public void An_old_link_is_moved_and_the_folder_it_points_to_is_kept()
    {
        var real = Path.Combine(Path.GetDirectoryName(_server)!, "game-map");
        Directory.CreateDirectory(real);
        File.WriteAllText(Path.Combine(real, "scene.xscene"), "game");
        Directory.CreateDirectory(Path.Combine(Modules, "SandBox", "SceneObj"));
        var link = Path.Combine(Modules, "SandBox", "SceneObj", "Main_map");
        using (var mklink = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{real}\"") { CreateNoWindow = true, UseShellExecute = false })!)
            mklink.WaitForExit();
        if (!Directory.Exists(link))
            return; // junctions are not available here

        var target = ExtraBaseMap.MoveAside(_server);

        Assert.False(Directory.Exists(link));
        Assert.True(new DirectoryInfo(target).Attributes.HasFlag(FileAttributes.ReparsePoint));
        Assert.Equal("game", File.ReadAllText(Path.Combine(real, "scene.xscene")));
        Directory.Delete(target);
    }

    [Fact]
    public void A_wrapped_console_line_is_still_read()
    {
        const string log = "22:56:06.624  Multiple active modules provide SceneObj\\Main_map: Sandbox,\r\nAD1259. Their\r\nload order will decide which assets win";

        var lines = ServerCrashExplain.FindTwoMaps(log)!;

        Assert.Contains("SandBox and AD1259", lines[0]);
    }

    [Fact]
    public void The_failed_start_names_the_SandBox_folder()
    {
        const string log = @"22:56:06.624  Multiple active modules provide SceneObj\Main_map: Sandbox, AD1259. Their load order will decide which assets win and";

        var lines = ServerCrashExplain.Explain("Server process exited with code 1.", false, log, null, [], ["Sandbox", "AD1259"]);

        Assert.Contains("two modules bring a campaign map: SandBox and AD1259", lines[0]);
        Assert.Contains(@"SandBox\SceneObj", lines[0]);
        Assert.Contains("Move it aside", lines[1]);
        Assert.DoesNotContain(lines, line => line.Contains("one of the working mods"));
    }

    [Fact]
    public void Two_map_mods_ask_to_keep_one()
    {
        const string log = @"Multiple active modules provide SceneObj\Main_map: AD1259, TAOM_Map. Their load order will decide";

        var lines = ServerCrashExplain.FindTwoMaps(log)!;

        Assert.Contains("AD1259, TAOM_Map", lines[0]);
        Assert.Contains("turn the other one off", lines[1]);
    }
}
