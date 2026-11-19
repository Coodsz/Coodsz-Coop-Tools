using CoodszWarSync;
using TaomCoop.Core;

namespace BannerlordCoopLauncher.Tests;

// Coodsz War Sync: the TAOM server tool's small War of the Ring sender, riding in DedicatedServer.Windows.
public class TaomWarSyncTests
{
    private const string ServerWindowsXml = """
        <?xml version="1.0" encoding="utf-8"?>
        <Module>
          <Name value="DedicatedServer.Windows"/>
          <Id value="DedicatedServer.Windows"/>
          <SubModules>
            <SubModule>
              <Name value="DedicatedServer.Windows"/>
              <DLLName value="DedicatedServer.Windows.dll"/>
              <SubModuleClassType value="DedicatedServer.Windows.SubModule"/>
            </SubModule>
          </SubModules>
        </Module>
        """;

    [Fact]
    public void War_sync_rides_in_DedicatedServer_Windows_once_next_to_Coop_Save_Rescue_and_updates_its_DLLs()
    {
        var root = Path.Combine(Path.GetTempPath(), "taomwarsync-" + Guid.NewGuid().ToString("N"));
        try
        {
            var module = ServerWindowsSubModules.ModuleDirectory(root);
            Directory.CreateDirectory(module);
            var xmlPath = Path.Combine(module, "SubModule.xml");
            File.WriteAllText(xmlPath, ServerWindowsXml.Replace("\n", "\r\n"));
            var payload = Path.Combine(root, "payload");
            Directory.CreateDirectory(payload);
            File.WriteAllText(Path.Combine(payload, WarSyncSubModule.DllName), "war");
            File.WriteAllText(Path.Combine(payload, WarSyncSubModule.SyncDllName), "sync");
            var rescue = Path.Combine(root, RescueSubModule.DllName);
            File.WriteAllText(rescue, "rescue");

            Assert.False(WarSyncSubModule.IsInstalled(root));
            RescueSubModule.Install(root, rescue);
            WarSyncSubModule.Install(root, payload);
            WarSyncSubModule.Install(root, payload); // twice adds it once
            var installed = File.ReadAllText(xmlPath);

            Assert.True(WarSyncSubModule.IsInstalled(root));
            Assert.True(RescueSubModule.IsInstalled(root));
            Assert.Equal(1, installed.Split("<Name value=\"Coodsz War Sync\"/>").Length - 1);
            Assert.Equal(1, installed.Split("<Name value=\"Coop Save Rescue\"/>").Length - 1);
            Assert.Contains("<DLLName value=\"CoodszWarSync.dll\"/>", installed);
            Assert.Contains("<SubModuleClassType value=\"CoodszWarSync.WarSyncSubModule\"/>", installed);
            Assert.True(installed.IndexOf("Coodsz War Sync", StringComparison.Ordinal) < installed.IndexOf("</SubModules>", StringComparison.Ordinal));
            // The file keeps its own line ends and the server's own entry.
            Assert.DoesNotContain("\n", installed.Replace("\r\n", ""));
            Assert.Contains("<DLLName value=\"DedicatedServer.Windows.dll\"/>", installed);

            var bin = Path.Combine(module, "bin", "Win64_Shipping_Server");
            Assert.Equal("war", File.ReadAllText(Path.Combine(bin, "CoodszWarSync.dll")));
            Assert.Equal("sync", File.ReadAllText(Path.Combine(bin, "CoodszSettingsSync.Coop.dll")));

            // A newer tool updates the DLLs and leaves the entry alone.
            File.WriteAllText(Path.Combine(payload, WarSyncSubModule.SyncDllName), "sync v2");
            WarSyncSubModule.Install(root, payload);
            Assert.Equal("sync v2", File.ReadAllText(Path.Combine(bin, "CoodszSettingsSync.Coop.dll")));
            Assert.Equal(installed, File.ReadAllText(xmlPath));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void War_sync_install_refuses_a_missing_payload_or_an_unprepared_server_without_touching_it()
    {
        var root = Path.Combine(Path.GetTempPath(), "taomwarsync-" + Guid.NewGuid().ToString("N"));
        try
        {
            var payload = Path.Combine(root, "payload");
            Directory.CreateDirectory(payload);
            File.WriteAllText(Path.Combine(payload, WarSyncSubModule.DllName), "war");

            // Only one of the two DLLs.
            var missing = Assert.Throws<InvalidOperationException>(() => WarSyncSubModule.Install(root, payload));
            Assert.Contains("Start the tool again", missing.Message);

            // Both DLLs, but no DedicatedServer.Windows yet.
            File.WriteAllText(Path.Combine(payload, WarSyncSubModule.SyncDllName), "sync");
            var unprepared = Assert.Throws<InvalidOperationException>(() => WarSyncSubModule.Install(root, payload));
            Assert.Contains("Prepare the server first", unprepared.Message);
            Assert.False(Directory.Exists(ServerWindowsSubModules.ModuleDirectory(root)));
            Assert.False(WarSyncSubModule.IsInstalled(root));

            Assert.Contains("uses the copy already there", WarSyncSubModule.InstallFailed(true, "x"));
            Assert.Contains("does not update for players", WarSyncSubModule.InstallFailed(false, "x"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void A_locked_SubModule_xml_fails_the_install_but_IsInstalled_never_throws_so_Start_goes_on()
    {
        var root = Path.Combine(Path.GetTempPath(), "taomwarsync-" + Guid.NewGuid().ToString("N"));
        try
        {
            var module = ServerWindowsSubModules.ModuleDirectory(root);
            Directory.CreateDirectory(module);
            var xmlPath = Path.Combine(module, "SubModule.xml");
            File.WriteAllText(xmlPath, ServerWindowsXml);
            var payload = Path.Combine(root, "payload");
            Directory.CreateDirectory(payload);
            File.WriteAllText(Path.Combine(payload, WarSyncSubModule.DllName), "war");
            File.WriteAllText(Path.Combine(payload, WarSyncSubModule.SyncDllName), "sync");

            using (new FileStream(xmlPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                Assert.ThrowsAny<IOException>(() => WarSyncSubModule.Install(root, payload));
                Assert.False(WarSyncSubModule.IsInstalled(root));
            }

            // The bridge DLL goes first, so a failure after it never leaves the module without a bridge.
            var bin = Path.Combine(module, "bin", "Win64_Shipping_Server");
            Assert.True(File.Exists(Path.Combine(bin, WarSyncSubModule.SyncDllName)));
            WarSyncSubModule.Install(root, payload);
            Assert.True(WarSyncSubModule.IsInstalled(root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData(true, new[] { "TaleWorlds.Core", "Coop.Core" }, true)]
    [InlineData(true, new[] { "TaleWorlds.Core", "DedicatedServer.CoodszTools" }, false)]
    [InlineData(true, new[] { "dedicatedserver.coodsztools" }, false)]
    [InlineData(false, new[] { "TaleWorlds.Core" }, false)]
    public void War_sync_steps_aside_for_Coodsz_Server_Tools_so_a_request_is_never_answered_twice(bool server, string[] assemblies, bool runs)
    {
        Assert.Equal(runs, WarSyncRules.ShouldRun(server, assemblies));
    }

    [Fact]
    public void The_TAOM_server_tool_installs_war_sync_on_every_start_and_ships_its_DLLs()
    {
        var root = RepoRoot();
        var control = File.ReadAllText(Path.Combine(root, "src", "TaomCoopHost", "MainWindow.ServerControl.cs"));
        var rescueAt = control.IndexOf("if (!InstallRescue(server))", StringComparison.Ordinal);
        var warAt = control.IndexOf("InstallWarSync(server);", StringComparison.Ordinal);
        Assert.True(rescueAt > 0 && warAt > rescueAt, "Start installs Coodsz War Sync right after Coop Save Rescue.");

        var project = File.ReadAllText(Path.Combine(root, "src", "TaomCoopHost", "TaomCoopHost.csproj"));
        Assert.Contains("<CoodszPayload Include=\"WarSync\\**\\*\" Payload=\"WarSync\" />", project);
        foreach (var dll in new[] { WarSyncSubModule.DllName, WarSyncSubModule.SyncDllName })
            Assert.True(File.Exists(Path.Combine(root, "src", "TaomCoopHost", WarSyncSubModule.PayloadFolder, dll)), dll + " is in the payload.");

        // The payload's sync DLL is the same server build Coodsz Server Tools ships.
        Assert.Equal(
            File.ReadAllBytes(Path.Combine(root, "src", "CoodszHost", "ServerTools", "DedicatedServer.CoodszTools", "bin", "Win64_Shipping_Server", "CoodszSettingsSync.Coop.dll")),
            File.ReadAllBytes(Path.Combine(root, "src", "TaomCoopHost", "WarSync", "CoodszSettingsSync.Coop.dll")));

        // The small module loads it in War of the Ring only mode, which ServerSync has.
        Assert.Contains("\"StartWarOnly\"", File.ReadAllText(Path.Combine(root, "compat", "CoodszWarSync", "src", "WarSyncSubModule.cs")));
        Assert.Contains("public static void StartWarOnly(Action<string> log, string moduleFolder)",
            File.ReadAllText(Path.Combine(root, "compat", "CoodszSettingsSync", "src", "Server", "ServerSync.cs")));
    }

    // Coop builds its handlers once, when the server starts hosting, and a slow server hosts before the module
    // screen callbacks come, so War Sync decides on the first tick too, once, before it ticks the loader.
    [Fact]
    public void War_sync_decides_on_the_first_tick_once_before_it_ticks_the_loader()
    {
        var module = File.ReadAllText(Path.Combine(RepoRoot(), "compat", "CoodszWarSync", "src", "WarSyncSubModule.cs")).Replace("\r\n", "\n");
        var tick = SourceFiles.Method(module, "protected override void OnApplicationTick(float dt)");
        var guard = tick.IndexOf("if (!_decided && !_tickBegun)", StringComparison.Ordinal);
        var once = tick.IndexOf("_tickBegun = true;", StringComparison.Ordinal);
        var begin = tick.IndexOf("Begin();", StringComparison.Ordinal);
        var ticks = tick.IndexOf("_loader?.Tick(dt);", StringComparison.Ordinal);
        Assert.True(guard > 0 && once > guard && begin > once && ticks > begin, tick);
        Assert.Contains("private static bool _tickBegun;", module);
    }

    [Fact]
    public void Coodsz_Host_never_installs_war_sync_since_its_own_server_tools_send_the_war_state()
    {
        var host = Path.Combine(RepoRoot(), "src", "CoodszHost");
        var sources = Directory.GetFiles(host, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) && !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar));
        Assert.DoesNotContain(sources, f => File.ReadAllText(f).Contains("WarSyncSubModule", StringComparison.Ordinal));
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "BannerlordCoopLauncher.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
