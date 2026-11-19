using BurningEmpiresCoopCompat;

namespace BannerlordCoopLauncher.Tests;

// BurningEmpires.CoopCompat on the dedicated server: the server takes SandBox's settlement
// distance cache, the game takes the last active module's, so Burning Empires' own cache is read
// instead (DistanceCacheRules.cs, linked).
public sealed class BurningEmpiresDistanceCacheTests : IDisposable
{
    private const string CacheFile = "settlements_distance_cache_Default.bin";

    private readonly string _modules = Path.Combine(Path.GetTempPath(), "coodsz-bedistance-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_modules, true); } catch (IOException) { }
    }

    private string Module(string id, params string[] caches)
    {
        var folder = Directory.CreateDirectory(Path.Combine(_modules, id)).FullName;
        foreach (var cache in caches)
        {
            var dir = Directory.CreateDirectory(Path.Combine(folder, "ModuleData", "DistanceCaches")).FullName;
            File.WriteAllBytes(Path.Combine(dir, cache), new byte[] { 1 });
        }
        return folder;
    }

    private static string Cache(string module, string file = CacheFile) =>
        Path.Combine(module, "ModuleData", "DistanceCaches", file);

    private static string? Redirect(string requested, string sandBox, params string[] active) =>
        DistanceCacheRules.Redirect(requested, sandBox, active, File.Exists);

    [Fact]
    public void SandBoxs_cache_is_swapped_for_Burning_Empires_own()
    {
        var native = Module("Native");
        var sandBox = Module("SandBox", CacheFile);
        var burningEmpires = Module("BurningEmpires", CacheFile);
        var coop = Module("Coop");

        Assert.Equal(Path.GetFullPath(Cache(burningEmpires)), Redirect(Cache(sandBox), sandBox, native, sandBox, burningEmpires, coop));
    }

    [Fact]
    public void The_last_active_module_with_the_cache_wins_like_the_game()
    {
        var sandBox = Module("SandBox", CacheFile);
        var first = Module("MapA", CacheFile);
        var last = Module("MapB", CacheFile);
        var after = Module("NoMap");

        Assert.Equal(Path.GetFullPath(Cache(last)), Redirect(Cache(sandBox), sandBox, sandBox, first, last, after));
        Assert.Equal(Path.GetFullPath(Cache(last)), DistanceCacheRules.PickLast(CacheFile, new[] { sandBox, first, last, after }, File.Exists));
    }

    [Fact]
    public void With_no_other_cache_SandBoxs_is_kept()
    {
        var sandBox = Module("SandBox", CacheFile);
        var burningEmpires = Module("BurningEmpires");

        Assert.True(DistanceCacheRules.IsInModuleCaches(Cache(sandBox), sandBox));
        Assert.Null(Redirect(Cache(sandBox), sandBox, sandBox, burningEmpires));
    }

    [Fact]
    public void Only_a_cache_with_the_same_file_name_counts()
    {
        var sandBox = Module("SandBox", CacheFile);
        var burningEmpires = Module("BurningEmpires", "settlements_distance_cache_Naval.bin");

        Assert.Null(Redirect(Cache(sandBox), sandBox, sandBox, burningEmpires));
    }

    [Fact]
    public void A_path_outside_SandBoxs_cache_folder_is_left_alone()
    {
        var sandBox = Module("SandBox", CacheFile);
        var burningEmpires = Module("BurningEmpires", CacheFile);
        var later = Module("Later", CacheFile);

        Assert.Null(Redirect(Cache(burningEmpires), sandBox, sandBox, burningEmpires, later));
        Assert.Null(Redirect(Path.Combine(sandBox, CacheFile), sandBox, sandBox, burningEmpires));
        Assert.Null(Redirect(null!, sandBox, sandBox, burningEmpires));
        Assert.Null(Redirect(Cache(sandBox), null!, sandBox, burningEmpires));
    }

    [Fact]
    public void Module_paths_match_like_the_game_writes_them()
    {
        // ModuleHelper.GetModuleFullPath ends in "/", and the server may write another case.
        var sandBox = Module("SandBox", CacheFile);
        var burningEmpires = Module("BurningEmpires", CacheFile);
        var requested = Path.Combine(Path.GetDirectoryName(Cache(sandBox))!.ToUpperInvariant(), CacheFile);

        Assert.Equal(Path.GetFullPath(Cache(burningEmpires)), Redirect(requested, sandBox + "/", sandBox + "/", burningEmpires + "/"));
    }
}
