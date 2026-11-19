using System.IO.Compression;
using System.Net;
using BannerlordCoopLauncher.Core.Updates;

namespace BannerlordCoopLauncher.Tests;

public class ReleaseNoticeTests
{
    [Fact]
    public void Newer_published_release_writes_the_download_line()
    {
        var message = ReleaseNotice.Message("v1.1.1", "v1.2.0", ReleaseNotice.TaomRepo);

        Assert.Equal(
            "A new version is available. This copy is v1.1.1. Download v1.2.0 from https://github.com/Coodsz/Coodsz-TAOM/releases/latest",
            message);
    }

    [Fact]
    public void Matching_release_stays_quiet()
    {
        Assert.Null(ReleaseNotice.Message("v1.1.2", "v1.1.2", ReleaseNotice.TaomRepo));
        Assert.Null(ReleaseNotice.Message("v1.2.1", "v1.2.0", ReleaseNotice.FriendsRepo));
    }

    [Fact]
    public void Unreadable_release_stays_quiet()
    {
        Assert.Null(ReleaseNotice.Message("v1.1.2", null, ReleaseNotice.TaomRepo));
        Assert.Null(ReleaseNotice.Message("v1.1.2", "latest", ReleaseNotice.TaomRepo));
        Assert.Null(ReleaseNotice.ReadTag("{"));
        Assert.Null(ReleaseNotice.ReadTag("{\"name\":\"Coodsz TAOM\"}"));
    }

    [Fact]
    public void Coodsz_host_file_version_matches_the_release_version()
    {
        var root = AppContext.BaseDirectory;
        while (root != null && !File.Exists(Path.Combine(root, "BannerlordCoopLauncher.sln")))
            root = Path.GetDirectoryName(root);
        Assert.NotNull(root);
        var project = File.ReadAllText(Path.Combine(root!, "src", "CoodszHost", "CoodszHost.csproj"));
        var version = System.Text.RegularExpressions.Regex.Match(project, "<Version>([^<]+)</Version>").Groups[1].Value;
        Assert.Equal(ReleaseNotice.FriendsVersion, "v" + version);
    }

    [Fact]
    public async Task Failed_request_stays_quiet()
    {
        using var http = new HttpClient(new StatusHandler(HttpStatusCode.InternalServerError));
        var message = await ReleaseNotice.CheckAsync(http, ReleaseNotice.FriendsRepo, ReleaseNotice.FriendsVersion);
        Assert.Null(message);
    }

    [Fact]
    public async Task Published_tag_is_compared_to_this_copy()
    {
        using var http = new HttpClient(new JsonHandler("{\"tag_name\":\"v1.3.0\"}"));
        var message = await ReleaseNotice.CheckAsync(http, ReleaseNotice.FriendsRepo, "v1.2.1");
        Assert.Equal(
            "A new version is available. This copy is v1.2.1. Download v1.3.0 from https://github.com/Coodsz/Coodsz-Coop-and-Friends/releases/latest",
            message);
    }

    [Fact]
    public void Each_tool_picks_its_own_release_zip()
    {
        var taom = new[]
        {
            "Coodsz-Dedicated-TAOM-Server-Tool.zip",
            "Coodsz-TAOM-Client-Launcher.zip",
            "TAOM-Coop-v0.3.17-CompatOnly-HOST-OPERATOR-36ae2c5a-d8a2ad02-r1.zip",
            "taom-server-tool.png"
        };
        var friends = new[] { "Coodsz-Coop-and-Friends-v1.2.5.zip", "coodsz-host.png" };

        Assert.Equal("Coodsz-Dedicated-TAOM-Server-Tool.zip", ToolUpdate.PickZip("Coodsz Dedicated TAOM Server Tool.exe", taom));
        Assert.Equal("Coodsz-TAOM-Client-Launcher.zip", ToolUpdate.PickZip("Coodsz TAOM Client Launcher.exe", taom));
        Assert.Equal("Coodsz-Coop-and-Friends-v1.2.5.zip", ToolUpdate.PickZip("Coodsz and Friends.exe", friends));
        Assert.Equal("Coodsz-Coop-and-Friends-v1.2.5.zip", ToolUpdate.PickZip("Coodsz Coop and Friends.exe", friends));
        Assert.Equal("Coodsz-Coop-and-Friends-v1.2.5.zip", ToolUpdate.PickZip("CoodszHost.exe", friends));
        Assert.Equal("Coodsz-Host.zip", ToolUpdate.PickZip("CoodszHost.exe", ["Coodsz-Host.zip", "Coodsz-Coop-and-Friends-v1.2.5.zip"]));
    }

    [Fact]
    public void Saved_json_is_left_in_place()
    {
        Assert.False(ToolUpdate.ShouldReplace("taom-server.json"));
        Assert.False(ToolUpdate.ShouldReplace("profile-cache.json"));
        Assert.False(ToolUpdate.ShouldReplace("cheats.json"));
        Assert.True(ToolUpdate.ShouldReplace("Coodsz Dedicated TAOM Server Tool.exe"));
        Assert.True(ToolUpdate.ShouldReplace("TaomCoop.Core.dll"));
    }

    [Theory]
    [InlineData("Coodsz TAOM Client Launcher.deps.json")]
    [InlineData("Coodsz TAOM Client Launcher.runtimeconfig.json")]
    [InlineData("Coodsz Dedicated TAOM Server Tool.deps.json")]
    [InlineData("Coodsz Dedicated TAOM Server Tool.runtimeconfig.json")]
    [InlineData("Coodsz AD1259 Client Launcher.DEPS.JSON")]
    public void Build_json_of_the_program_is_replaced(string fileName)
    {
        Assert.True(ToolUpdate.ShouldReplace(fileName));
    }

    [Theory]
    [InlineData("launcher.json")]
    [InlineData("profile-cache.json")]
    [InlineData("host.json")]
    [InlineData("cheats.json")]
    [InlineData("taom-launcher.json")]
    [InlineData("taom-server.json")]
    [InlineData("taom-cheats.json")]
    [InlineData("ad1259-launcher.json")]
    [InlineData(@"DedicatedServerRepair\server-module-policy.json")]
    [InlineData("update-install.log")]
    [InlineData("launcher-error.txt")]
    public void Player_files_are_never_replaced(string relativePath)
    {
        Assert.False(ToolUpdate.ShouldReplace(relativePath));
    }

    [WindowsFact]
    public void An_update_keeps_every_saved_file_and_replaces_build_json()
    {
        var root = Path.Combine(Path.GetTempPath(), "coodsz-kept-" + Guid.NewGuid().ToString("N"));
        var stage = Path.Combine(root, "stage");
        var source = Path.Combine(stage, "files");
        var dest = Path.Combine(root, "dest");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(dest);
        foreach (var name in ToolUpdate.KeptUserFiles.Where(name => name != "update-install.log"))
        {
            File.WriteAllText(Path.Combine(source, name), "from the zip");
            File.WriteAllText(Path.Combine(dest, name), "saved");
        }

        File.WriteAllText(Path.Combine(source, "tool.deps.json"), "new deps");
        File.WriteAllText(Path.Combine(dest, "tool.deps.json"), "old deps");
        try
        {
            BridgeUpdaterTests.RunPlan(stage, ToolUpdate.LegacyPlan(source, dest, Path.Combine(dest, "tool.exe"), 2147483000, stage));

            foreach (var name in ToolUpdate.KeptUserFiles.Where(name => name != "update-install.log"))
                Assert.Equal("saved", File.ReadAllText(Path.Combine(dest, name)));
            Assert.Equal("new deps", File.ReadAllText(Path.Combine(dest, "tool.deps.json")));
            Assert.StartsWith("Installed ", File.ReadAllText(Path.Combine(dest, "update-install.log")));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [WindowsFact]
    public void An_update_replaces_build_json_and_keeps_saved_json()
    {
        var root = Path.Combine(Path.GetTempPath(), "coodsz-json-" + Guid.NewGuid().ToString("N"));
        var stage = Path.Combine(root, "stage");
        var source = Path.Combine(stage, "files");
        var dest = Path.Combine(root, "dest");
        Directory.CreateDirectory(Path.Combine(source, "DedicatedServerRepair"));
        Directory.CreateDirectory(Path.Combine(dest, "DedicatedServerRepair"));
        const string tool = "Coodsz TAOM Client Launcher";
        File.WriteAllText(Path.Combine(source, tool + ".dll"), "new program");
        File.WriteAllText(Path.Combine(source, tool + ".deps.json"), "new deps");
        File.WriteAllText(Path.Combine(source, tool + ".runtimeconfig.json"), "new runtime");
        File.WriteAllText(Path.Combine(source, "cheats.json"), "default cheats");
        File.WriteAllText(Path.Combine(source, "LiteNetLib.dll"), "new dll");
        File.WriteAllText(Path.Combine(source, "DedicatedServerRepair", "server-module-policy.json"), "default policy");
        File.WriteAllText(Path.Combine(dest, tool + ".dll"), "old program");
        File.WriteAllText(Path.Combine(dest, tool + ".deps.json"), "old deps");
        File.WriteAllText(Path.Combine(dest, tool + ".runtimeconfig.json"), "old runtime");
        File.WriteAllText(Path.Combine(dest, "taom-launcher.json"), "saved settings");
        File.WriteAllText(Path.Combine(dest, "cheats.json"), "saved cheats");
        File.WriteAllText(Path.Combine(dest, "DedicatedServerRepair", "server-module-policy.json"), "edited policy");
        try
        {
            BridgeUpdaterTests.RunPlan(stage, ToolUpdate.LegacyPlan(source, dest, Path.Combine(dest, tool + ".exe"), 2147483000, stage));
            Assert.StartsWith("Installed ", File.ReadAllText(Path.Combine(dest, "update-install.log")));
            Assert.Equal("new program", File.ReadAllText(Path.Combine(dest, tool + ".dll")));
            Assert.Equal("new deps", File.ReadAllText(Path.Combine(dest, tool + ".deps.json")));
            Assert.Equal("new runtime", File.ReadAllText(Path.Combine(dest, tool + ".runtimeconfig.json")));
            Assert.Equal("new dll", File.ReadAllText(Path.Combine(dest, "LiteNetLib.dll")));
            Assert.Equal("saved settings", File.ReadAllText(Path.Combine(dest, "taom-launcher.json")));
            Assert.Equal("saved cheats", File.ReadAllText(Path.Combine(dest, "cheats.json")));
            Assert.Equal("edited policy", File.ReadAllText(Path.Combine(dest, "DedicatedServerRepair", "server-module-policy.json")));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void The_payload_is_the_folder_that_contains_this_program()
    {
        var root = Path.Combine(Path.GetTempPath(), "coodsz-payload-" + Guid.NewGuid().ToString("N"));
        var nested = Path.Combine(root, "tool");
        Directory.CreateDirectory(nested);
        try
        {
            File.WriteAllText(Path.Combine(nested, "Coodsz and Friends.exe"), "");
            Assert.Equal(nested, ToolUpdate.FindPayloadRoot(root, "Coodsz and Friends.exe"));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void A_zip_that_steps_outside_its_folder_is_refused()
    {
        var root = Path.Combine(Path.GetTempPath(), "coodsz-zip-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var zipPath = Path.Combine(root, "bad.zip");
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            var entry = zip.CreateEntry("../outside.txt");
            using var writer = new StreamWriter(entry.Open());
            writer.Write("no");
        }

        var destination = Path.Combine(root, "out");
        Directory.CreateDirectory(destination);
        try
        {
            Assert.Throws<InvalidOperationException>(() => ToolUpdate.ExtractZip(zipPath, destination));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [WindowsFact]
    public void A_locked_program_is_moved_aside_and_the_window_opens_again()
    {
        var root = Path.Combine(Path.GetTempPath(), "coodsz-install-" + Guid.NewGuid().ToString("N"));
        var stage = Path.Combine(root, "stage");
        var source = Path.Combine(stage, "files");
        var dest = Path.Combine(root, "dest");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(dest);
        var relaunch = "@echo off\r\necho opened> \"%~dp0reopened.txt\"\r\n";
        File.WriteAllText(Path.Combine(source, "tool.cmd"), relaunch);
        File.WriteAllText(Path.Combine(source, "launcher.json"), "new settings");
        File.WriteAllText(Path.Combine(dest, "tool.cmd"), "old program");
        File.WriteAllText(Path.Combine(dest, "launcher.json"), "saved settings");
        File.WriteAllText(Path.Combine(dest, "host.json"), "saved host");
                File.WriteAllText(Path.Combine(dest, "tool.dll"), "old version");
        File.WriteAllText(Path.Combine(dest, "tool.deps.json"), "old deps");
        // A dll the tool never shipped is the player's.
        File.WriteAllText(Path.Combine(dest, "OldProgram.dll"), "player");
        var mod = Path.Combine(dest, "Coop");
        Directory.CreateDirectory(mod);
        File.WriteAllText(Path.Combine(mod, "keep.dll"), "bundled mod");
        // Open the way Windows keeps a running exe: readable and renamable, but not writable.
        using var locked = new FileStream(Path.Combine(dest, "tool.cmd"), FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        try
        {
            BridgeUpdaterTests.RunPlan(stage, ToolUpdate.LegacyPlan(source, dest, Path.Combine(dest, "tool.cmd"), 2147483000, stage));
            var opened = Path.Combine(dest, "reopened.txt");
            var until = DateTime.UtcNow.AddSeconds(8);
            while (!File.Exists(opened) && DateTime.UtcNow < until)
                Thread.Sleep(100);
            Assert.Equal(relaunch, File.ReadAllText(Path.Combine(dest, "tool.cmd")));
            Assert.Equal("saved settings", File.ReadAllText(Path.Combine(dest, "launcher.json")));
            Assert.Equal("saved host", File.ReadAllText(Path.Combine(dest, "host.json")));
            Assert.Equal("bundled mod", File.ReadAllText(Path.Combine(mod, "keep.dll")));
            Assert.False(File.Exists(Path.Combine(dest, "tool.dll")));
            Assert.False(File.Exists(Path.Combine(dest, "tool.deps.json")));
            Assert.Equal("player", File.ReadAllText(Path.Combine(dest, "OldProgram.dll")));
            Assert.StartsWith("Installed ", File.ReadAllText(Path.Combine(dest, "update-install.log")));
            Assert.True(File.Exists(opened));
        }
        finally
        {
            locked.Dispose();
            // The reopened tool.cmd can still be writing reopened.txt for a moment.
            try
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, true);
            }
            catch (IOException)
            {
            }
        }
    }

    [Fact]
    public void A_single_file_update_removes_the_previous_program_and_keeps_settings()
    {
        var root = Path.Combine(Path.GetTempPath(), "coodsz-stale-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var exe = Path.Combine(root, "Coodsz Coop and Friends.exe");
        File.WriteAllText(exe, "new");
        File.WriteAllText(Path.Combine(root, "Coodsz Coop and Friends.dll"), "old");
        File.WriteAllText(Path.Combine(root, "Coodsz Coop and Friends.deps.json"), "old");
        File.WriteAllText(Path.Combine(root, "Coodsz Coop and Friends.runtimeconfig.json"), "old");
        File.WriteAllText(Path.Combine(root, "BannerlordCoopLauncher.Core.dll"), "old");
        File.WriteAllText(Path.Combine(root, "launcher.json"), "saved");
        File.WriteAllText(Path.Combine(root, "launcher-error.txt"), "log");
        var mod = Path.Combine(root, "Coop");
        Directory.CreateDirectory(mod);
        File.WriteAllText(Path.Combine(mod, "keep.dll"), "mod");
        try
        {
            ToolUpdate.RemovePreviousVersion(root, exe, singleFile: true);
            Assert.True(File.Exists(exe));
            Assert.Equal("saved", File.ReadAllText(Path.Combine(root, "launcher.json")));
            Assert.Equal("log", File.ReadAllText(Path.Combine(root, "launcher-error.txt")));
            Assert.Equal("mod", File.ReadAllText(Path.Combine(mod, "keep.dll")));
            Assert.False(File.Exists(Path.Combine(root, "Coodsz Coop and Friends.dll")));
            Assert.False(File.Exists(Path.Combine(root, "Coodsz Coop and Friends.deps.json")));
            Assert.False(File.Exists(Path.Combine(root, "Coodsz Coop and Friends.runtimeconfig.json")));
            Assert.False(File.Exists(Path.Combine(root, "BannerlordCoopLauncher.Core.dll")));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void A_matching_release_does_not_offer_an_install()
    {
        var assets = new[] { new ReleaseAsset("Coodsz-TAOM-Client-Launcher.zip", "https://example.test/tool.zip") };
        Assert.Null(ReleaseNotice.Offer("v1.1.5", "v1.1.5", ReleaseNotice.TaomRepo, assets));
    }

    private sealed class StatusHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;

        public StatusHandler(HttpStatusCode status) => _status = status;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(_status));
    }

    private sealed class JsonHandler : HttpMessageHandler
    {
        private readonly string _json;

        public JsonHandler(string json) => _json = json;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_json)
            });
    }
}
