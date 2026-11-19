using BannerlordCoopLauncher.Core.Support;
using BannerlordCoopLauncher.Core.Updates;
using TaomCoop.Core;

namespace BannerlordCoopLauncher.Tests;

public class TaomSupportFixTests
{
    [Fact]
    public void A_reopened_taom_host_sees_its_own_server_engine_and_not_another_one()
    {
        var taom = Path.Combine("C:", "TAOM-Coop-Host", "Server-v0317-Coop-v015-r1");
        var taomEngine = Path.Combine(taom, "engine", "dotnet", "dotnet.exe");
        var friends = Path.Combine(
            "C:", "Program Files (x86)", "Steam", "steamapps", "workshop", "content", "261550", "3770450698", "DedicatedServer");
        var friendsEngine = Path.Combine(friends, "engine", "dotnet", "dotnet.exe");
        Assert.True(DedicatedServerWatch.IsEngine(taomEngine, taom));
        Assert.False(DedicatedServerWatch.IsEngine(friendsEngine, taom));
        Assert.True(DedicatedServerWatch.IsEngine(friendsEngine, friends));
        Assert.False(DedicatedServerWatch.IsEngine(Path.Combine("C:", "Program Files", "dotnet", "dotnet.exe"), taom));

        var root = Path.Combine(Path.GetTempPath(), "taom-lock-" + Guid.NewGuid().ToString("N"));
        var lockPath = Path.Combine(root, "server-data", "server-instance.lock");
        Directory.CreateDirectory(Path.GetDirectoryName(lockPath)!);
        try
        {
            using var held = new FileStream(lockPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
            Assert.True(DedicatedServerWatch.LockHeld(root));
            Assert.True(DedicatedServerWatch.IsRunning(root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }

        Assert.False(DedicatedServerWatch.LockHeld(root));
    }

    [Fact]
    public void Unblock_clears_the_download_mark_and_leaves_the_file()
    {
        var root = Path.Combine(Path.GetTempPath(), "taom-unblock-" + Guid.NewGuid().ToString("N"));
        var module = Path.Combine(root, "Modules", "TAOM", "bin");
        Directory.CreateDirectory(module);
        try
        {
            var dll = Path.Combine(module, "TradeBoundFix.dll");
            File.WriteAllText(dll, "dll");
            File.WriteAllText(dll + ":Zone.Identifier", "[ZoneTransfer]\r\nZoneId=3\r\n");
            File.WriteAllText(Path.Combine(module, "clean.dll"), "ok");

            var cleared = DownloadMark.Clear(Path.Combine(root, "Modules"));

            Assert.Equal(1, cleared);
            Assert.Equal("dll", File.ReadAllText(dll));
            Assert.False(File.Exists(dll + ":Zone.Identifier"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [WindowsFact]
    public void Unblock_refuses_a_drive_root()
    {
        var root = Path.GetPathRoot(Path.GetTempPath());
        Assert.NotNull(root);
        var error = Assert.Throws<InvalidOperationException>(() => DownloadMark.Clear(root));
        Assert.Contains("too broad", error.Message);
    }

    [Fact]
    public void Shader_clear_deletes_only_the_shader_folder()
    {
        var root = Path.Combine(Path.GetTempPath(), "taom-shaders-" + Guid.NewGuid().ToString("N"));
        var shaders = Path.Combine(root, "Shaders", "TerrainShaders");
        Directory.CreateDirectory(shaders);
        try
        {
            File.WriteAllText(Path.Combine(shaders, "cache.sack"), "shader");
            File.WriteAllText(Path.Combine(root, "keep.txt"), "keep");

            var deleted = ShaderCache.Clear(root, gameIsRunning: false);

            Assert.Equal(1, deleted);
            Assert.False(Directory.Exists(Path.Combine(root, "Shaders")));
            Assert.Equal("keep", File.ReadAllText(Path.Combine(root, "keep.txt")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Shader_clear_refuses_while_the_game_is_open()
    {
        var root = Path.Combine(Path.GetTempPath(), "taom-shaders-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Shaders"));
        try
        {
            var error = Assert.Throws<InvalidOperationException>(() => ShaderCache.Clear(root, gameIsRunning: true));
            Assert.Contains("Close Bannerlord", error.Message);
            Assert.True(Directory.Exists(Path.Combine(root, "Shaders")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Shader_clear_is_quiet_when_the_cache_is_already_gone()
    {
        var root = Path.Combine(Path.GetTempPath(), "taom-shaders-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            Assert.Equal(0, ShaderCache.Clear(root, gameIsRunning: false));
            Assert.True(Directory.Exists(root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Unblock_refuses_a_missing_folder()
    {
        var missing = Path.Combine(Path.GetTempPath(), "taom-missing-" + Guid.NewGuid().ToString("N"));
        var error = Assert.Throws<InvalidOperationException>(() => DownloadMark.Clear(missing));
        Assert.Contains("not found", error.Message);
    }

    [Fact]
    public void Blocked_dll_notice_names_the_server_tool_and_not_the_client()
    {
        var lines = BlockedDllNotice.Explain("Cannot load: ..\\Modules\\TradeBoundFix\\bin\\Win64_Shipping_Client\\TradeBoundFix.dll");

        Assert.NotNull(lines);
        Assert.Equal(3, lines.Count);
        Assert.StartsWith("What happened:", lines[0]);
        Assert.Contains("does not clear this server folder", lines[0]);
        Assert.StartsWith("What to do:", lines[1]);
        Assert.Contains("Unblock DLLs in this server tool", lines[1]);
        Assert.StartsWith("What not to do:", lines[2]);
        Assert.Contains("Do not use the client launcher button", lines[2]);
    }

    [Fact]
    public void A_mod_that_failed_to_start_is_not_called_a_blocked_dll()
    {
        Assert.Null(BlockedDllNotice.Explain(null));
        Assert.Null(BlockedDllNotice.Explain(""));
        Assert.Null(BlockedDllNotice.Explain("ChaosBattle could not be loaded correctly"));
        Assert.Null(BlockedDllNotice.Explain("Cannot load: the campaign save"));
        Assert.Null(BlockedDllNotice.Explain("Loading assembly: TradeBoundFix.dll"));
        Assert.Null(BlockedDllNotice.Explain("Messagebox [ERROR] message: Cannot load: SandBox.dll"));
        Assert.Null(BlockedDllNotice.Explain("FileLoadException: ImprovedGarrisons.dll could not be loaded correctly"));
    }

    [Fact]
    public void A_windows_block_on_a_downloaded_dll_is_the_unblock_fix()
    {
        var lines = BlockedDllNotice.Explain("System.IO.FileLoadException: Could not load file or assembly 'TradeBoundFix'. Operation is not supported. (Exception from HRESULT: 0x80131515)");

        Assert.NotNull(lines);
        Assert.Contains("Unblock DLLs in this server tool", lines[1]);
        Assert.NotNull(BlockedDllNotice.Explain("An attempt was made to load an assembly from a network location which would have caused the assembly to be sandboxed."));
    }

    [Fact]
    public void Update_line_stays_hidden_until_a_newer_release_is_published()
    {
        Assert.Null(ReleaseNotice.Message(ReleaseNotice.TaomVersion, "v1.1.1", ReleaseNotice.TaomRepo));
        Assert.Null(ReleaseNotice.Message(ReleaseNotice.FriendsVersion, ReleaseNotice.FriendsVersion, ReleaseNotice.FriendsRepo));
        Assert.NotNull(ReleaseNotice.Message("v1.1.2", "v1.1.3", ReleaseNotice.TaomRepo));
    }
}
