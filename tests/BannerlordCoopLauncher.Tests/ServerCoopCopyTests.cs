using CoodszHost.Services;

namespace BannerlordCoopLauncher.Tests;

public class ServerCoopCopyTests
{
    [Fact]
    public void The_game_Coop_folder_does_not_replace_the_dedicated_server_copy()
    {
        var root = Path.Combine(Path.GetTempPath(), "coop-guard-" + Guid.NewGuid().ToString("N"));
        var serverCoop = Path.Combine(root, "DedicatedServer", "engine", "Modules", "Coop");
        var serverDll = Path.Combine(serverCoop, "bin", "Win64_Shipping_Server", "Coop.Core.dll");
        Directory.CreateDirectory(Path.GetDirectoryName(serverDll)!);
        File.WriteAllText(Path.Combine(serverCoop, "SubModule.xml"), "<Module><Id value=\"CoopNightly\"/><Name value=\"Bannerlord Coop\"/></Module>");
        File.WriteAllText(serverDll, "server");

        var gameCoop = Path.Combine(root, "game", "Coop");
        Directory.CreateDirectory(Path.Combine(gameCoop, "bin", "Win64_Shipping_Client"));
        File.WriteAllText(Path.Combine(gameCoop, "SubModule.xml"), "<Module><Id value=\"Coop\"/><Name value=\"Bannerlord Coop\"/></Module>");
        File.WriteAllText(Path.Combine(gameCoop, "bin", "Win64_Shipping_Client", "Coop.Core.dll"), "client");

        try
        {
            var error = Assert.Throws<InvalidOperationException>(() =>
                ExtraModScan.Install(gameCoop, Path.Combine(root, "DedicatedServer"), "Coop"));
            Assert.Contains("dedicated server", error.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("server", File.ReadAllText(serverDll));
            Assert.False(Directory.Exists(Path.Combine(serverCoop, "bin", "Win64_Shipping_Client")));

            var copied = ExtraModScan.CopyMissing(
                Path.Combine(root, "DedicatedServer"),
                new[] { "Coop" },
                new[] { (Path.Combine(root, "game"), false) });
            Assert.Empty(copied.Copied);
            Assert.Equal("server", File.ReadAllText(serverDll));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void A_client_only_mod_does_not_replace_a_server_bin_that_is_already_there()
    {
        var root = Path.Combine(Path.GetTempPath(), "server-bin-" + Guid.NewGuid().ToString("N"));
        var destination = Path.Combine(root, "DedicatedServer", "engine", "Modules", "ExampleMod");
        var serverDll = Path.Combine(destination, "bin", "Win64_Shipping_Server", "ExampleMod.dll");
        Directory.CreateDirectory(Path.GetDirectoryName(serverDll)!);
        File.WriteAllText(Path.Combine(destination, "SubModule.xml"), "<Module><Id value=\"ExampleMod\"/><Name value=\"Example\"/></Module>");
        File.WriteAllText(serverDll, "server");

        var source = Path.Combine(root, "game", "ExampleMod");
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "SubModule.xml"), "<Module><Id value=\"ExampleMod\"/><Name value=\"Example\"/></Module>");

        try
        {
            var error = Assert.Throws<InvalidOperationException>(() =>
                ExtraModScan.Install(source, Path.Combine(root, "DedicatedServer"), "ExampleMod"));
            Assert.Contains("left in place", error.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("server", File.ReadAllText(serverDll));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
