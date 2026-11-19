using System.Text.Json;
using BannerlordCoopLauncher.Core.Models;
using BannerlordCoopLauncher.Core.Modules;
using BannerlordCoopLauncher.Core.Profile;
using BannerlordCoopLauncher.Core.Updates;
using CoodszHost.Services;

namespace BannerlordCoopLauncher.Tests;

public class PlainCoopSetupTests
{
    private static readonly string[] ServerBase = ["Native", "DedicatedServer.Windows", "SandBoxCore", "SandBox", "Coop"];

    [Fact]
    public void Both_tools_name_the_plain_setup_the_same()
    {
        Assert.Equal(StableClientSetup.PlainName, StableCoop.PlainName);
        Assert.Equal("Plain Coop (no mods)", StableCoop.PlainName);
    }

    [Fact]
    public void Host_plain_setup_loads_only_the_base_modules()
    {
        var settings = new HostSettings { Profiles = { new HostProfile { Name = "Default" } } };
        StableCoop.Ensure(settings);
        var plain = settings.Profiles.Single(profile => profile.Name == StableCoop.PlainName);

        Assert.True(plain.Locked);
        Assert.Equal(StableCoop.VanillaMap, plain.CampaignMap);
        Assert.Equal(ServerBase, plain.ModuleOrder);
        Assert.Equal(ServerBase, StableCoop.OrderFor(plain));

        var installed = StableCoop.Order.Where(id => !id.Equals("CoopNightly", StringComparison.OrdinalIgnoreCase)).Concat(["RBM", "StoryMode"]).ToList();
        var on = installed.Where(id => StableCoop.ShouldEnable(plain, id, installed)).ToList();
        Assert.Equal(ServerBase, on);
        foreach (var library in new[] { "Bannerlord.Harmony", "Bannerlord.ButterLib", "Bannerlord.UIExtenderEx", "Bannerlord.MBOptionScreen" })
            Assert.False(StableCoop.KeepsRequired(plain, library));
        Assert.True(StableCoop.KeepsRequired(plain, "Native"));
        Assert.True(StableCoop.ShouldEnable(plain, "CoopNightly", ["CoopNightly", "Native"]));
    }

    [Fact]
    public void Other_setups_keep_their_stable_mods()
    {
        var settings = new HostSettings { Profiles = { new HostProfile { Name = "Default" } } };
        StableCoop.Ensure(settings);
        var vanilla = settings.Profiles.Single(profile => profile.Name == StableCoop.VanillaName);
        var nations = settings.Profiles.Single(profile => profile.Name == StableCoop.MoreNationsName);

        Assert.Equal(StableCoop.Order, vanilla.ModuleOrder);
        Assert.Equal(StableCoop.Order, nations.ModuleOrder);
        Assert.Equal(StableCoop.Order, StableCoop.OrderFor(vanilla));
        Assert.False(StableCoop.IsPlain(vanilla));
        Assert.True(StableCoop.KeepsRequired(vanilla, "Bannerlord.Harmony"));
        Assert.True(StableCoop.ShouldEnable(vanilla, "Bannerlord.ButterLib", StableCoop.Order));
        Assert.Equal(StableCoop.Order, StableCoop.CreateFresh("Profile 2").ModuleOrder);
    }

    [Fact]
    public void New_host_file_lists_plain_after_the_stable_setups()
    {
        var path = Path.Combine(Path.GetTempPath(), "coodsz-host-" + Guid.NewGuid().ToString("N") + ".json");
        var settings = HostSettings.Load(path);
        var names = settings.Profiles.Select(profile => profile.Name).ToList();

        Assert.Equal(["Default", StableCoop.VanillaName, StableCoop.MoreNationsName, StableCoop.PlainName], names);
        Assert.Equal("Default", settings.ActiveProfile);
    }

    [Fact]
    public void Upgrading_host_settings_adds_plain_once_and_keeps_the_rest()
    {
        var path = Path.Combine(Path.GetTempPath(), "coodsz-host-" + Guid.NewGuid().ToString("N") + ".json");
        var old = new HostSettings
        {
            ActiveProfile = "My mods",
            Profiles =
            [
                new HostProfile { Name = "Default", SaveName = "default-save" },
                new HostProfile { Name = StableCoop.VanillaName, Locked = true, SaveName = "stable-save" },
                new HostProfile { Name = StableCoop.MoreNationsName, Locked = true },
                new HostProfile { Name = "My mods", ModuleOrder = ["Native", "Coop", "RBM"] }
            ]
        };
        File.WriteAllText(path, JsonSerializer.Serialize(old));
        try
        {
            var loaded = HostSettings.Load(path);
            HostSettings.Save(path, loaded);
            var again = HostSettings.Load(path);
            StableCoop.Ensure(again);
            var names = again.Profiles.Select(profile => profile.Name).ToList();

            Assert.Equal(["Default", StableCoop.VanillaName, StableCoop.MoreNationsName, StableCoop.PlainName, "My mods"], names);
            Assert.Single(names, name => name == StableCoop.PlainName);
            Assert.Equal("My mods", again.ActiveProfile);
            Assert.Equal("stable-save", again.Profiles[1].SaveName);
            Assert.Equal(["Native", "Coop", "RBM"], again.Profiles[4].ModuleOrder);
            Assert.False(again.Profiles[4].Locked);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Plain_server_list_leaves_the_libraries_out()
    {
        var root = Path.Combine(Path.GetTempPath(), "plain-list-" + Guid.NewGuid().ToString("N"));
        var modules = Path.Combine(root, "engine", "Modules");
        foreach (var id in StableCoop.Order.Where(id => !id.Equals("CoopNightly", StringComparison.OrdinalIgnoreCase)))
        {
            var folder = Path.Combine(modules, id);
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "SubModule.xml"), "<Module><Id value=\"" + id + "\"/><Name value=\"" + id + "\"/></Module>");
        }

        try
        {
            var settings = new HostSettings { Profiles = { new HostProfile { Name = "Default" } } };
            StableCoop.Ensure(settings);
            var plain = settings.Profiles.Single(profile => profile.Name == StableCoop.PlainName);
            var ranked = StableCoop.OrderFor(plain).Select((id, index) => (id, index))
                .ToDictionary(item => item.id, item => item.index, StringComparer.OrdinalIgnoreCase);
            var rows = ModuleCatalog.Load(root)
                .OrderBy(row => ranked.TryGetValue(row.Id, out var index) ? index : int.MaxValue)
                .ThenBy(row => row.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var installed = rows.Select(row => row.Id).ToList();
            foreach (var row in rows)
                row.Enabled = row.Required && StableCoop.KeepsRequired(plain, row.Id) || StableCoop.ShouldEnable(plain, row.Id, installed);
            ModuleCatalog.Write(root, rows);

            var written = File.ReadAllLines(Path.Combine(root, "modded-server-modules.txt"))
                .Where(line => line.Length > 0 && !line.StartsWith('#'))
                .ToList();
            var selection = JsonSerializer.Deserialize<Dictionary<string, bool>>(File.ReadAllText(Path.Combine(root, "modded-server-selection.json")))!;

            Assert.Equal(ServerBase, rows.Where(row => row.Enabled).Select(row => row.Id));
            Assert.Equal(ServerBase, written.Take(ServerBase.Length));
            Assert.DoesNotContain(written, ServerModuleNotes.IsLibrary);
            Assert.DoesNotContain(selection.Keys, ServerModuleNotes.IsLibrary);
            Assert.All(selection.Where(pair => !ServerBase.Contains(pair.Key)), pair => Assert.False(pair.Value));

            // Every start adds Coodsz Server Tools after Coop; Coop leaves DedicatedServer.* out of the match.
            var client = new ServerProfile();
            ServerModuleSet.ApplyPlain(client);
            ClientCampaignModes.KeepOnClient(client);
            var started = CoodszServerTools.WithTools(rows.Where(row => row.Enabled)).Select(row => row.Id).ToList();
            Assert.Null(CoopJoinRules.JoinProblem(client.ModuleLoadOrder, started));
            Assert.Equal(["Coop"], CoopJoinRules.Compared(started));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Client_plain_setup_connects_with_coop_alone()
    {
        var profile = ProfileJson.BuiltIn();
        ServerModuleSet.ApplyPlain(profile);
        ClientCampaignModes.KeepOnClient(profile);

        Assert.Equal(["Native", "SandBoxCore", "SandBox", "StoryMode", "CustomBattle", "Coop"], profile.ModuleLoadOrder);
        Assert.Equal(["Coop"], profile.Mods.Select(mod => mod.ModuleId));
        Assert.Equal(["Coop"], CoopJoinRules.Compared(profile.ModuleLoadOrder));
        Assert.Equal(["Native", "SandBoxCore", "SandBox", "Coop"], ServerModuleSet.PlainOrder);
        Assert.NotNull(CoopJoinRules.JoinProblem(profile.ModuleLoadOrder, ServerModuleSet.WorkingOrder(includeMoreNations: false)));
    }

    [Fact]
    public void Client_settings_add_plain_once_after_the_stable_setups()
    {
        var fresh = new LauncherSettings();
        fresh.EnsureSetups();
        Assert.Equal(["Default", StableClientSetup.VanillaName, StableClientSetup.MoreNationsName, StableClientSetup.PlainName], fresh.Setups.Select(setup => setup.Name));
        var plain = fresh.Setups[3];
        Assert.True(plain.Locked);
        Assert.Equal(StableClientSetup.VanillaMap, plain.CampaignMap);
        Assert.Equal(StableClientSetup.VanillaMap, StableClientSetup.MapFor(plain.Name));
        Assert.True(StableClientSetup.IsPlain(plain.Name));
        Assert.False(StableClientSetup.IsPlain(StableClientSetup.VanillaName));

        var path = Path.Combine(Path.GetTempPath(), "coop-friends-" + Guid.NewGuid().ToString("N") + ".json");
        var old = new LauncherSettings
        {
            ActiveSetup = "Setup 2",
            Setups =
            [
                new ClientSetup { Name = "Default" },
                new ClientSetup { Name = StableClientSetup.VanillaName, Locked = true, SteamHostName = "Coodsz" },
                new ClientSetup { Name = StableClientSetup.MoreNationsName, Locked = true, CampaignMap = "MoreNations" },
                new ClientSetup { Name = "Setup 2", DisabledModules = ["RTSCamera"] }
            ]
        };
        old.Save(path);
        try
        {
            var loaded = LauncherSettings.Load(path);
            loaded.Save(path);
            var again = LauncherSettings.Load(path);
            again.EnsureSetups();

            Assert.Equal(["Default", StableClientSetup.VanillaName, StableClientSetup.MoreNationsName, StableClientSetup.PlainName, "Setup 2"], again.Setups.Select(setup => setup.Name));
            Assert.Equal("Setup 2", again.ActiveSetup);
            Assert.Equal("Coodsz", again.Setups[1].SteamHostName);
            Assert.Equal(["RTSCamera"], again.Setups[4].DisabledModules);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Plain_setup_name_is_not_sent_for_translation_but_its_notes_are()
    {
        var root = RepoRoot();
        var extract = File.ReadAllText(Path.Combine(root, "tools", "ExtractUiText", "Program.cs"));
        Assert.Contains("key == \"Plain Coop (no mods)\"", extract);
        Assert.Contains(WhatsNew.Friends.Single(entry => entry.Version == "v1.2.57").Lines, line => line.Contains(StableCoop.PlainName));
        var guide = GuideLibraryText();
        Assert.Contains(StableCoop.PlainName, guide);
    }

    [Fact]
    public void Host_and_client_use_the_plain_order_for_locked_lists()
    {
        var root = RepoRoot();
        var hostModules = File.ReadAllText(Path.Combine(root, "src", "CoodszHost", "MainWindow.Modules.cs"));
        Assert.Contains("StableCoop.OrderFor(profile)", hostModules);
        Assert.Contains("row.Required && StableCoop.KeepsRequired(profile, row.Id)", hostModules);
        Assert.Contains("!StableCoop.IsPlain(_settings.Current())", hostModules);

        var launch = File.ReadAllText(Path.Combine(root, "src", "BannerlordCoopLauncher", "MainWindow.Launch.cs"));
        Assert.Contains("ServerModuleSet.ApplyPlain(profile);", launch);
        var status = File.ReadAllText(Path.Combine(root, "src", "BannerlordCoopLauncher", "MainWindow.Status.cs"));
        Assert.Contains("LockedWorkingOrder()", status);
    }

    private static string GuideLibraryText() =>
        string.Join("\n", BannerlordCoopLauncher.Core.Keep.GuideLibrary.All.SelectMany(BannerlordCoopLauncher.Core.Keep.GuideLibrary.Lines));

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "BannerlordCoopLauncher.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
