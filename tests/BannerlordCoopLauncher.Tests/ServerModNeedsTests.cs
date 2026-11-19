using BannerlordCoopLauncher.Core.AD1259;
using BannerlordCoopLauncher.Core.Joining;
using BannerlordCoopLauncher.Core.Modules;
using TaomCoop.Core;

namespace BannerlordCoopLauncher.Tests;

// Get all of this server's mods: Connect finds the mods the server needs that this PC does not have
// turned on, and picks one action for them.
public sealed class ServerModNeedsTests
{
    private static ResolvedModule Base(string id) => new(id, id, null, true, true, ModuleState.Ready, "", "C:\\Game\\Modules\\" + id);

    private static ResolvedModule Mod(string id, ModuleState state, string? workshop = "2859188632", string? name = null, bool enabled = true) =>
        new(name ?? id, id, workshop, enabled, false, state, "", state == ModuleState.Missing ? null : "C:\\Workshop\\" + id);

    private static ModuleResolution Resolution(params ResolvedModule[] mods) =>
        new(new[] { Base("Native"), Base("SandBoxCore") }, mods.Prepend(Mod("Coop", ModuleState.Ready, "3770450698", "Bannerlord Coop")).ToList(), true);

    private static IReadOnlyList<ServerModNeed> Needs(ModuleResolution resolution, params string[] byHand) =>
        ServerModNeeds.FromResolution(resolution, module => byHand.Contains(module.ModuleId), id => id == "CoodszBundled");

    [Fact]
    public void Nothing_missing_means_connect_works_as_before()
    {
        var resolution = Resolution(Mod("Bannerlord.Harmony", ModuleState.Ready));
        Assert.Empty(Needs(resolution));
        Assert.Null(ServerModPrompt.For(Needs(resolution)));
        Assert.Equal(ServerModAction.Connect, ServerModPrompt.ActionFor(Array.Empty<ServerModNeed>()));
        Assert.False(ServerModNeeds.PromptCovers(resolution));
    }

    [Fact]
    public void Missing_and_outdated_workshop_mods_are_got_and_then_it_connects()
    {
        var resolution = Resolution(
            Mod("Bannerlord.Harmony", ModuleState.Missing, name: "Harmony"),
            Mod("Bannerlord.ButterLib", ModuleState.UpdatePending, "2859232415", "ButterLib"),
            Mod("Off.Mod", ModuleState.Missing, enabled: false));
        var needs = Needs(resolution);
        Assert.Equal(new[] { "Bannerlord.Harmony", "Bannerlord.ButterLib" }, needs.Select(need => need.Id));
        Assert.Equal(ServerModProblem.Missing, needs[0].Problem);
        Assert.Equal(ServerModProblem.WrongVersion, needs[1].Problem);
        Assert.All(needs, need => Assert.Equal(ServerModFetch.Workshop, need.Fetch));
        Assert.Equal("https://steamcommunity.com/sharedfiles/filedetails/?id=2859188632", needs[0].Link);
        Assert.True(ServerModNeeds.PromptCovers(resolution));

        var prompt = ServerModPrompt.For(needs)!;
        Assert.Equal(ServerModAction.GetAndConnect, prompt.Action);
        Assert.Equal("This server needs 2 mods you don't have turned on", prompt.Title);
        Assert.Equal("Get them and connect", prompt.MainButton);
        Assert.Equal("", prompt.ByHandNote);
        Assert.Empty(prompt.ByHand);
    }

    [Fact]
    public void One_mod_reads_as_one()
    {
        var prompt = ServerModPrompt.For(Needs(Resolution(Mod("Bannerlord.Harmony", ModuleState.Missing, name: "Harmony"))))!;
        Assert.Equal("This server needs a mod you don't have turned on", prompt.Title);
        Assert.Equal("Get it and connect", prompt.MainButton);
        Assert.Equal("One click gets it, turns it on and connects.", prompt.Why);
        Assert.Equal("Bannerlord.Harmony. It is not on this PC yet.", prompt.Mods[0].Tip);
    }

    [Fact]
    public void A_mod_the_launcher_can_not_get_is_listed_by_name_and_the_rest_are_got_without_connecting()
    {
        var resolution = Resolution(
            Mod("Bannerlord.Harmony", ModuleState.Missing, name: "Harmony"),
            Mod("NexusOnly", ModuleState.Missing, workshop: "0", name: "Nexus Only"),
            Mod("Pinned", ModuleState.VersionMismatch, "2875090166", "Pinned Mod"));
        var needs = Needs(resolution);
        Assert.Equal(ServerModFetch.ByHand, needs.Single(need => need.Id == "NexusOnly").Fetch);
        var pinned = needs.Single(need => need.Id == "Pinned");
        Assert.Equal(ServerModFetch.ByHand, pinned.Fetch);
        Assert.Equal(ServerModProblem.WrongVersion, pinned.Problem);
        Assert.Equal("https://steamcommunity.com/sharedfiles/filedetails/?id=2875090166", pinned.Link);

        var prompt = ServerModPrompt.For(needs)!;
        Assert.Equal(ServerModAction.GetSomeThenStop, prompt.Action);
        Assert.Equal("Get the others", prompt.MainButton);
        // The ones the player gets come first, so +N more never hides them.
        Assert.Equal(new[] { "NexusOnly", "Pinned", "Bannerlord.Harmony" }, prompt.Mods.Select(need => need.Id));
        Assert.Equal(new[] { "Nexus Only", "Pinned Mod" }, prompt.ByHand.Select(need => need.Name));
        Assert.Equal("The launcher can't get these by itself.", prompt.ByHandNote);
    }

    [Fact]
    public void Only_mods_got_by_hand_have_no_main_button_and_stop()
    {
        var needs = Needs(Resolution(Mod("Gone", ModuleState.Missing, name: "Gone Mod")), "Gone");
        var prompt = ServerModPrompt.For(needs)!;
        Assert.Equal(ServerModAction.GetByHand, prompt.Action);
        Assert.Equal("", prompt.MainButton);
        Assert.Equal("The launcher can't get this one by itself.", prompt.ByHandNote);
        Assert.Null(prompt.Mods[0].Link);
    }

    [Fact]
    public void A_mod_that_comes_with_the_launcher_is_got_by_the_launcher()
    {
        var need = Assert.Single(Needs(Resolution(Mod("CoodszBundled", ModuleState.Missing, workshop: "0"))));
        Assert.Equal(ServerModFetch.Launcher, need.Fetch);
        Assert.True(need.Automatic);
        var prompt = ServerModPrompt.For(new[] { need })!;
        Assert.Equal("Get it and connect", prompt.MainButton);
        Assert.Equal("Copies the mods that come with the launcher into Modules, turns the mods on, then starts the game and joins.", prompt.MainTip);
        var mixed = ServerModPrompt.For(new[] { need, new ServerModNeed("Harmony", "Harmony", ServerModProblem.Missing, ServerModFetch.Workshop) })!;
        Assert.StartsWith("Copies the mods that come with the launcher, subscribes in Steam", mixed.MainTip);
    }

    [Fact]
    public void A_bundled_mod_whose_folder_is_not_next_to_the_launcher_is_got_by_hand()
    {
        var launcher = Path.Combine(Path.GetTempPath(), "coodsz-bundled-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(launcher, "BannerFix"));
        File.WriteAllText(Path.Combine(launcher, "BannerFix", "SubModule.xml"), "<Module/>");
        try
        {
            Assert.True(BannerlordCoopLauncher.Core.Launch.BundledModules.CanCopy(launcher, "bannerfix"));
            Assert.False(BannerlordCoopLauncher.Core.Launch.BundledModules.CanCopy(launcher, "AliveScenes"), "no folder, nothing to copy");
            Assert.False(BannerlordCoopLauncher.Core.Launch.BundledModules.CanCopy(launcher, "NotBundled"));

            var needs = ServerModNeeds.FromResolution(Resolution(Mod("AliveScenes", ModuleState.Missing, workshop: "0")), _ => false,
                id => BannerlordCoopLauncher.Core.Launch.BundledModules.CanCopy(launcher, id));
            Assert.Equal(ServerModFetch.ByHand, Assert.Single(needs).Fetch);
            var prompt = ServerModPrompt.For(needs)!;
            Assert.NotEqual(ServerModAction.GetAndConnect, prompt.Action);
            Assert.Equal("", prompt.MainButton);
        }
        finally
        {
            Directory.Delete(launcher, true);
        }

        var serverMods = SourceFiles.Read("src/BannerlordCoopLauncher/MainWindow.ServerMods.cs");
        Assert.Contains("BundledModules.CanCopy(bundled, id)", serverMods);
        Assert.Contains("var bundled = await ToolPayload.FolderAsync(ToolPayload.BundledMods);", serverMods);
    }

    [Fact]
    public void Other_problems_keep_connect_locked_as_before()
    {
        Assert.False(ServerModNeeds.PromptCovers(Resolution(Mod("Bannerlord.Harmony", ModuleState.Missing), Mod("Twice", ModuleState.Ambiguous))));
        Assert.False(ServerModNeeds.PromptCovers(Resolution(Mod("WrongId", ModuleState.IdMismatch))));
        var noCoop = new ModuleResolution(new[] { Base("Native") }, new[] { Mod("Bannerlord.Harmony", ModuleState.Missing) }, false);
        Assert.False(ServerModNeeds.PromptCovers(noCoop));
        var baseMissing = new ModuleResolution(new[] { Base("Native") with { State = ModuleState.Missing } }, new[] { Mod("Coop", ModuleState.Ready) }, true);
        Assert.False(ServerModNeeds.PromptCovers(baseMissing));
        // An update Steam can not download for a mod that is not on the Workshop is not the prompt's either.
        Assert.False(ServerModNeeds.PromptCovers(Resolution(Mod("Local", ModuleState.UpdatePending, workshop: "0"))));
    }

    [Fact]
    public void A_mod_is_listed_once_and_chips_fold_after_eight()
    {
        var needs = Enumerable.Range(1, 11)
            .Select(i => new ServerModNeed("Mod" + i, "Mod " + i, ServerModProblem.Missing, ServerModFetch.Workshop))
            .Append(new ServerModNeed("mod1", "Mod 1 again", ServerModProblem.Missing, ServerModFetch.Workshop))
            .ToList();
        var prompt = ServerModPrompt.For(needs)!;
        Assert.Equal(11, prompt.Count);
        Assert.True(prompt.HasMore);
        Assert.Equal(8, prompt.Shown(false).Count);
        Assert.Equal(11, prompt.Shown(true).Count);
        Assert.Equal("+3 more", prompt.MoreButton(false));
        Assert.Equal("Show less", prompt.MoreButton(true));
    }

    [Fact]
    public void An_off_mod_is_turned_on_and_says_so()
    {
        var need = new ServerModNeed("ImprovedGarrisons", "Improved Garrisons", ServerModProblem.Off, ServerModFetch.TurnOn);
        Assert.True(need.Automatic);
        Assert.Equal("ImprovedGarrisons. It is on this PC but turned off.", need.Tip);
        Assert.Equal(ServerModAction.GetAndConnect, ServerModPrompt.For(new[] { need })!.Action);
    }

    [Fact]
    public void Taom_extras_not_on_this_pc_are_got_from_the_workshop_the_launcher_folder_or_by_hand()
    {
        var setup = new TaomSetup
        {
            Name = "Mine",
            Extras =
            {
                new TaomExtraMod { ModuleId = "Bannerlord.Harmony", Name = "Harmony", WorkshopId = "2859188632" },
                new TaomExtraMod { ModuleId = "Bundled", Name = "Bundled", WorkshopId = "0" },
                new TaomExtraMod { ModuleId = "NexusMod", Name = "Nexus Mod", WorkshopId = "0" },
                new TaomExtraMod { ModuleId = "Here", Name = "Here", WorkshopId = "0" },
                new TaomExtraMod { ModuleId = "OffMod", Name = "Off", WorkshopId = "0", Enabled = false }
            }
        };
        var needs = TaomSharedList.ServerNeeds(setup, new[] { "Here" }, id => id == "Bundled");
        Assert.Equal(new[] { "Bannerlord.Harmony", "Bundled", "NexusMod" }, needs.Select(need => need.Id));
        Assert.Equal(new[] { ServerModFetch.Workshop, ServerModFetch.Launcher, ServerModFetch.ByHand }, needs.Select(need => need.Fetch));
        Assert.Equal(ServerModAction.GetSomeThenStop, ServerModPrompt.For(needs)!.Action);
        Assert.Null(ServerModPrompt.For(TaomSharedList.ServerNeeds(setup, new[] { "Bannerlord.Harmony", "Bundled", "NexusMod", "Here" }, _ => false)));
    }

    [Fact]
    public void Ad1259_missing_workshop_mods_are_subscribed_and_the_rest_open_their_page()
    {
        AD1259ModStatus Status(string id, AD1259ModState state) =>
            new(AD1259Stack.ClientMods.Single(mod => mod.Id == id), state, null, Array.Empty<string>(), Array.Empty<string>());
        var needs = ServerModNeeds.FromAD1259(new[]
        {
            Status("Bannerlord.Harmony", AD1259ModState.Missing),
            Status("RBM", AD1259ModState.WrongVersion),
            Status("AD1259", AD1259ModState.Missing),
            Status("Native", AD1259ModState.Missing),
            Status("Bannerlord.ButterLib", AD1259ModState.Duplicate),
            Status("Coop", AD1259ModState.Ready)
        });
        Assert.Equal(new[] { "Bannerlord.Harmony", "RBM", "AD1259", "Native" }, needs.Select(need => need.Id));
        // A Workshop mod with the wrong version is by hand, like VersionMismatch in FromResolution:
        // subscribing again keeps the same version, so Get all missing would loop.
        Assert.Equal(new[] { ServerModFetch.Workshop, ServerModFetch.ByHand, ServerModFetch.ByHand, ServerModFetch.ByHand }, needs.Select(need => need.Fetch));
        Assert.Equal(ServerModProblem.WrongVersion, needs[1].Problem);
        Assert.StartsWith("https://steamcommunity.com/sharedfiles/filedetails/?id=", needs[1].Link);
        Assert.Equal("https://www.nexusmods.com/mountandblade2bannerlord/mods/6723", needs[2].Link);
        Assert.Null(needs[3].Link);
    }

    [Fact]
    public void The_text_has_no_colons_or_hyphens_between_words()
    {
        var prompts = new[]
        {
            ServerModPrompt.For(new[] { new ServerModNeed("A", "A", ServerModProblem.Missing, ServerModFetch.Workshop) })!,
            ServerModPrompt.For(new[] { new ServerModNeed("A", "A", ServerModProblem.Missing, ServerModFetch.Workshop), new ServerModNeed("B", "B", ServerModProblem.Missing, ServerModFetch.ByHand) })!,
            ServerModPrompt.For(new[] { new ServerModNeed("B", "B", ServerModProblem.WrongVersion, ServerModFetch.ByHand) })!
        };
        foreach (var prompt in prompts)
        {
            foreach (var text in new[] { prompt.Title, prompt.Why, prompt.ByHandNote, prompt.MainButton, prompt.MainTip, ServerModPrompt.CancelTip, prompt.Mods[0].Tip })
            {
                Assert.DoesNotContain(":", text);
                Assert.DoesNotMatch(@"\w-\w", text);
            }
        }
    }

    [Fact]
    public void Connect_asks_first_in_coop_and_friends_and_the_taom_launcher()
    {
        var coop = SourceFiles.Read("src/BannerlordCoopLauncher/MainWindow.Launch.cs");
        var launch = SourceFiles.Method(coop, "private async Task LaunchAsync(bool diagnostic)");
        Assert.True(launch.IndexOf("GetServerModsFirstAsync()", StringComparison.Ordinal) < launch.IndexOf("ConnectBlockReason", StringComparison.Ordinal));
        Assert.Contains("!diagnostic && !await GetServerModsFirstAsync()", launch);
        Assert.Contains("await RunBusyAsync(InstallSetupModsAsync)", SourceFiles.Read("src/BannerlordCoopLauncher/MainWindow.ServerMods.cs"));
        Assert.Contains("ServerModNeeds.PromptCovers(_resolution)", SourceFiles.Read("src/BannerlordCoopLauncher/MainWindow.xaml.cs"));

        var taom = SourceFiles.Method(SourceFiles.Read("src/TaomCoopLauncher/MainWindow.xaml.cs"), "private async Task ConnectOnceAsync()");
        Assert.True(taom.IndexOf("GetServerModsFirstAsync()", StringComparison.Ordinal) < taom.IndexOf("ModuleProblems()", StringComparison.Ordinal));
        Assert.Contains("DownloadExtrasAsync(", SourceFiles.Read("src/TaomCoopLauncher/MainWindow.ServerMods.cs"));
        Assert.Contains("ServerModsWindow.cs", SourceFiles.Read("src/TaomCoopLauncher/TaomCoopLauncher.csproj"));
        Assert.Contains("ServerModsWindow.cs", SourceFiles.Read("src/CoodszCoopAndFriends/CoodszCoopAndFriends.csproj"));

        Assert.Contains("Content=\"Get all missing\"", SourceFiles.Read("src/AD1259Launcher/MainWindow.xaml"));
        Assert.Contains("WorkshopDownload.RunAsync", SourceFiles.Method(SourceFiles.Read("src/AD1259Launcher/MainWindow.xaml.cs"), "private async void GetMissing_Click"));
    }
}
