using BannerlordCoopLauncher.Core.Modules;
using BannerlordCoopLauncher.Core.Profile;
using BurningEmpires.Core;
using RealmOfThrones.Core;
using TaomCoop.Core;

namespace BannerlordCoopLauncher.Tests;

public class RotSharedListTests
{
    private static List<SharedModEntry> HostList(params string[] extras)
    {
        var list = RotStack.ClientModules.Select(id => new SharedModEntry(id, id, "v1.0.0", "0")).ToList();
        list.InsertRange(list.Count - 1, extras.Select(id => new SharedModEntry(id, id, "v1.0.0", "123456")));
        return list;
    }

    private static Dictionary<string, string> Installed(IEnumerable<SharedModEntry> list) =>
        list.ToDictionary(entry => entry.Id, entry => entry.Version);

    [Fact]
    public void A_shared_list_goes_out_and_comes_back_into_a_new_setup()
    {
        var book = new RotSetupBook();
        var host = book.CopyOf(RotSetupBook.CoreName, "Host setup");
        Assert.Null(RotLoadOrder.Add(host, "RBM", "RBM", "2859251492"));
        Assert.Null(RotLoadOrder.Add(host, "Xml only", "XmlOnly", "3000000001"));
        var entries = RotSharedList.Entries(host, id => (id + " name", "v2.0.0", null));
        Assert.Equal(RotLoadOrder.ClientOrder(host), entries.Select(entry => entry.Id));
        Assert.Equal("RBM", entries.Single(entry => entry.Id == "RBM").Name);
        Assert.Equal("2859251492", entries.Single(entry => entry.Id == "RBM").WorkshopId);
        foreach (var library in RotStack.Libraries)
            Assert.Equal(library.WorkshopId, entries.Single(entry => entry.Id == library.ModuleId).WorkshopId);
        Assert.Equal(RotStack.CoopWorkshopId, entries.Single(entry => entry.Id == RotStack.CoopModuleId).WorkshopId);

        var file = Path.Combine(Path.GetTempPath(), "rot-list-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            SharedModList.Write(file, entries);
            var read = SharedModList.Read(File.ReadAllText(file));
            Assert.Null(RotSharedList.ForeignGame(read));

            var player = new TaomSetup { Name = "Player" };
            var result = RotSharedList.Import(player, read, Array.Empty<FoundMod>());
            Assert.Equal(new[] { "RBM", "XmlOnly" }, player.Extras.Select(mod => mod.ModuleId));
            Assert.Equal(RotLoadOrder.ClientOrder(host), RotLoadOrder.ClientOrder(player));
            Assert.Equal(RotLoadOrder.ServerOrder(host), RotLoadOrder.ServerOrder(player));
            Assert.Empty(result.MissingCore);
            Assert.Empty(result.Refused);
            Assert.True(result.Changed);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void Import_into_the_locked_setup_is_refused_so_the_launcher_makes_a_new_setup_as_burning_empires_does()
    {
        var book = new RotSetupBook();
        var plain = book.Current();
        var error = Assert.Throws<InvalidOperationException>(() => RotSharedList.Import(plain, HostList("Extra"), Array.Empty<FoundMod>()));
        Assert.Equal("Plain Realm of Thrones is locked. Make a new setup for the list.", error.Message);
        Assert.Empty(plain.Extras);

        // The Burning Empires launcher's Import mod list: Create, then import into the new setup.
        var made = book.Create();
        RotSharedList.Import(made, HostList("Extra"), Array.Empty<FoundMod>());
        Assert.Equal("Setup 2", book.Active);
        Assert.Equal("Extra", Assert.Single(made.Extras).ModuleId);
        Assert.Empty(book.Find(RotSetupBook.CoreName)!.Extras);
    }

    [Fact]
    public void Import_turns_off_what_the_list_lacks_and_leaves_out_core_refused_and_unlinked_mods()
    {
        var setup = new TaomSetup { Name = "Mine" };
        Assert.Null(RotLoadOrder.Add(setup, "Old", "Old", "333"));
        var list = HostList("Wanted");
        list.Insert(list.Count - 1, new SharedModEntry("NexusOnly", "Nexus only", "v1", "0"));
        list.Insert(list.Count - 1, new SharedModEntry("ROT_Core", "A ROT copy", "v1", "444"));
        list.Add(new SharedModEntry("DedicatedServer.CoodszJoinPassword", "Join password", "v1", "0"));
        var result = RotSharedList.Import(setup, list, Array.Empty<FoundMod>());

        Assert.Equal(new[] { "Wanted" }, result.Added);
        Assert.Equal(new[] { "Old" }, result.TurnedOff);
        Assert.Equal(new[] { "Nexus only" }, result.NoLink);
        Assert.Equal(new[] { "A ROT copy" }, result.Refused);
        Assert.Equal(new[] { "Wanted", "Old" }, setup.Extras.Select(mod => mod.ModuleId));
        Assert.Equal(new[] { "ROT-Dragon", "Wanted", "RealmOfThrones.CoopCompat" }, RotLoadOrder.ClientOrder(setup).TakeLast(3));
    }

    [Fact]
    public void A_taom_or_burning_empires_list_is_refused_before_anything_changes()
    {
        var setup = new TaomSetup { Name = "Mine" };
        Assert.Null(RotLoadOrder.Add(setup, "Kept", "Kept", "111"));
        var taom = TaomStack.ClientModules.Select(id => new SharedModEntry(id, id, "v1", "0")).Append(new SharedModEntry("Other", "Other", "v1", "222")).ToList();
        var be = BeStack.ClientModules.Select(id => new SharedModEntry(id, id, "v1", "0")).ToList();

        Assert.StartsWith("This mod list is for TAOM (it has ", RotSharedList.ForeignGame(taom));
        Assert.StartsWith("This mod list is for Burning Empires (it has ", RotSharedList.ForeignGame(be));
        Assert.EndsWith("not Realm of Thrones. Ask the host for the list from the Realm of Thrones tools.", RotSharedList.ForeignGame(be));
        foreach (var list in new[] { taom, be })
            Assert.Throws<InvalidOperationException>(() => RotSharedList.Import(setup, list, Array.Empty<FoundMod>()));
        var kept = Assert.Single(setup.Extras);
        Assert.Equal("Kept", kept.ModuleId);
        Assert.True(kept.Enabled);
    }

    [Theory]
    [InlineData("ChaosInCalradia")]
    [InlineData("ChaosInCalradia.CoopCompat")]
    public void A_chaos_in_calradia_list_is_refused_before_anything_changes(string chaosId)
    {
        var setup = new TaomSetup { Name = "Mine" };
        Assert.Null(RotLoadOrder.Add(setup, "Kept", "Kept", "111"));
        var chaos = new List<SharedModEntry>
        {
            new("Bannerlord.Harmony", "Harmony", "v2.4.2.248", "2859188632"),
            new("Native", "Native", "v1.4.8", "0"),
            new("Coop", "Bannerlord Coop", "v0.1.7", "3770450698"),
            new(chaosId, chaosId, "v1.0.0", "0")
        };
        Assert.Equal("This mod list is for Chaos in Calradia (it has " + chaosId + "), not Realm of Thrones. Ask the host for the list from the Realm of Thrones tools.",
            RotSharedList.ForeignGame(chaos));
        Assert.Throws<InvalidOperationException>(() => RotSharedList.Import(setup, chaos, Array.Empty<FoundMod>()));
        Assert.True(Assert.Single(setup.Extras).Enabled);
    }

    // ----- Version compare -----

    [Fact]
    public void The_same_list_on_both_sides_has_no_problems()
    {
        var host = HostList("Extra");
        Assert.Empty(RotSharedList.VersionProblems(host, Installed(host)));
        Assert.Empty(RotSharedList.NotOnHost(host, host.Select(entry => entry.Id.ToUpperInvariant())));
    }

    [Fact]
    public void A_missing_library_and_a_missing_extra_are_named()
    {
        var host = HostList("Extra");
        var installed = Installed(host);
        installed.Remove("Bannerlord.ButterLib");
        installed.Remove("Extra");
        var problems = RotSharedList.VersionProblems(host, installed);
        Assert.Equal(2, problems.Count);
        Assert.All(problems, problem => Assert.Equal(RotVersionProblemKind.Missing, problem.Kind));
        Assert.Equal(RotModPart.Library, problems[0].Part);
        Assert.Equal("Bannerlord.ButterLib is on the host's list but not on this PC.", problems[0].Text);
        Assert.Equal(RotModPart.Extra, problems[1].Part);
        Assert.Equal("v1.0.0", problems[1].HostVersion);
    }

    [Fact]
    public void A_library_or_extra_with_another_version_here_is_named_with_both_versions()
    {
        var host = HostList("Extra");
        var installed = Installed(host);
        installed["Bannerlord.MBOptionScreen"] = "v5.12.3";
        installed["Extra"] = "v1.1.0";
        installed["ROT-Core"] = " V1.0.0 ";
        var problems = RotSharedList.VersionProblems(host, installed);
        Assert.Equal(new[] { "Bannerlord.MBOptionScreen", "Extra" }, problems.Select(problem => problem.ModuleId));
        Assert.All(problems, problem => Assert.Equal(RotVersionProblemKind.Different, problem.Kind));
        Assert.Equal(RotModPart.Library, problems[0].Part);
        Assert.Equal("v5.12.3", problems[0].ThisPcVersion);
        Assert.Equal("Bannerlord.MBOptionScreen is v5.12.3 here. The shared list says v1.0.0.", problems[0].Text);
        Assert.Equal(RotModPart.Extra, problems[1].Part);
    }

    [Fact]
    public void A_mod_this_pc_loads_that_the_host_lacks_is_named_and_client_add_ons_are_left_out()
    {
        var host = HostList();
        var active = host.Select(entry => entry.Id).ToList();
        active.Insert(active.Count - 1, "MyExtra");
        active.Add("DedicatedServer.CoodszJoinPassword");
        var problem = Assert.Single(RotSharedList.NotOnHost(host, active, id => id == "MyExtra" ? "My extra" : null));
        Assert.Equal(RotVersionProblemKind.NotOnHost, problem.Kind);
        Assert.Equal(RotModPart.Extra, problem.Part);
        Assert.Equal("My extra is on this setup but not on the host's list.", problem.Text);

        // A Plain player against a custom server is the other direction: the host's extra is missing here.
        var custom = HostList("HostExtra");
        Assert.Equal("HostExtra", Assert.Single(RotSharedList.VersionProblems(custom, Installed(host))).ModuleId);
    }

    [Fact]
    public void An_empty_version_on_either_side_is_not_compared()
    {
        var host = HostList("Extra");
        host[^2] = host[^2] with { Version = "" };
        var installed = Installed(HostList("Extra"));
        installed["Bannerlord.Harmony"] = "";
        Assert.Empty(RotSharedList.VersionProblems(host, installed));
    }

    [Fact]
    public void Installed_ids_that_differ_only_in_case_do_not_crash_and_the_first_wins()
    {
        var host = HostList("Extra");
        var installed = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in host)
            installed[entry.Id] = entry.Version;
        installed["EXTRA"] = "v9.9.9";
        Assert.Empty(RotSharedList.VersionProblems(host, installed));
    }

    [Fact]
    public void The_version_text_is_compared_as_written_so_a_missing_v_is_a_difference()
    {
        var host = HostList();
        var installed = Installed(host);
        host[0] = host[0] with { Version = "v2.4.2.248" };
        installed["Bannerlord.Harmony"] = "2.4.2.248";
        var problem = Assert.Single(RotSharedList.VersionProblems(host, installed));
        Assert.Equal(RotVersionProblemKind.Different, problem.Kind);
        Assert.Equal("2.4.2.248", problem.ThisPcVersion);
    }

    [Fact]
    public void Import_matches_extras_whose_ids_differ_in_case_from_the_setup()
    {
        var setup = new TaomSetup { Name = "Mine" };
        Assert.Null(RotLoadOrder.Add(setup, "My mod", "MyMod", "111"));
        RotLoadOrder.SetEnabled(setup, "MyMod", false);
        var result = RotSharedList.Import(setup, HostList("mymod"), Array.Empty<FoundMod>());
        Assert.Equal(new[] { "My mod" }, result.TurnedOn);
        Assert.Empty(result.Added);
        Assert.Empty(result.TurnedOff);
        var mod = Assert.Single(setup.Extras);
        Assert.Equal("MyMod", mod.ModuleId);
        Assert.True(mod.Enabled);
    }

    // ----- The exact Coop build (CoopBuildId) -----

    private const string Pinned = "0.1.8+01846e136cf38cf64c79d9d4014ddb2e01907de3";
    private const string Other = "0.1.8+04038e2d6aa1b2c3d4e5f60718293a4b5c6d7e8f";

    [Fact]
    public void A_coop_build_is_split_compared_and_shortened()
    {
        Assert.Equal(Pinned, RotStack.CoopBuild.Build);
        Assert.Equal("01846e136cf38cf64c79d9d4014ddb2e01907de3", CoopBuildId.Commit(Pinned));
        Assert.Equal("0.1.8", CoopBuildId.Version(Pinned));
        Assert.Equal("0.1.8+01846e13", CoopBuildId.Short(Pinned));
        Assert.Equal("commit 01846e13", CoopBuildId.Short("01846E136CF38CF64C79D9D4014DDB2E01907DE3"));
        Assert.True(CoopBuildId.Same(Pinned, " " + Pinned.ToUpperInvariant() + " "));
        Assert.True(CoopBuildId.Same(Pinned, "01846e136cf38cf64c79d9d4014ddb2e01907de3"));
        Assert.True(CoopBuildId.Same(Pinned, "v0.1.8+01846e13"));
        Assert.False(CoopBuildId.Same(Pinned, Other));
        Assert.False(CoopBuildId.Same(Pinned, "0.1.9+01846e136cf38cf64c79d9d4014ddb2e01907de3"));
        Assert.False(CoopBuildId.Same(Pinned, null));
        Assert.False(CoopBuildId.Same("", ""));
        Assert.True(CoopBuildId.Same("1.0.0", "1.0.0"));
        Assert.Null(CoopBuildId.Commit("1.0.0"));
        // A Common.dll whose ProductVersion has no "+" (a bare version) never passes for the pinned build.
        Assert.False(CoopBuildId.Same(Pinned, "0.1.8"));
        Assert.Equal("0.1.8", CoopBuildId.Short("0.1.8"));
        Assert.NotNull(RotJoinCheck.CoopBuildProblem("0.1.8", []));
    }

    [Fact]
    public void A_coop_build_is_read_from_a_module_folder_or_a_server_folder()
    {
        var root = Path.Combine(Path.GetTempPath(), "rot-build-" + Guid.NewGuid().ToString("N"));
        try
        {
            // Common.dll stands in as a copy of this test's own Core assembly, which has a ProductVersion.
            var assembly = typeof(CoopBuildId).Assembly.Location;
            var expected = System.Diagnostics.FileVersionInfo.GetVersionInfo(assembly).ProductVersion;
            var client = Path.Combine(root, "Modules", "Coop");
            Assert.Null(CoopBuildId.FromModule(client));
            Directory.CreateDirectory(Path.Combine(client, "bin", "Win64_Shipping_Client"));
            File.Copy(assembly, Path.Combine(client, "bin", "Win64_Shipping_Client", "Common.dll"));
            Assert.Equal(expected, CoopBuildId.FromModule(client));

            var server = Path.Combine(root, "server");
            Assert.Null(CoopBuildId.FromServer(server));
            Directory.CreateDirectory(server);
            File.WriteAllText(Path.Combine(server, "release-info.txt"),
                "﻿Bannerlord Coop Dedicated Server (Windows x64)\r\n\r\nBuilt:        2026-11-09\r\nPaired Coop:  BannerlordCoop commit 01846e136cf38cf64c79d9d4014ddb2e01907de3\r\n");
            Assert.Equal("01846e136cf38cf64c79d9d4014ddb2e01907de3", CoopBuildId.FromServer(server));
            Assert.Null(RotServerPath.CoopBuildProblem(CoopBuildId.FromServer(server)));
            var coop = Path.Combine(server, "engine", "Modules", "Coop", "bin", "Win64_Shipping_Server");
            Directory.CreateDirectory(coop);
            File.Copy(assembly, Path.Combine(coop, "Common.dll"));
            Assert.Equal(expected, CoopBuildId.FromServer(server));
            Assert.Null(CoopBuildId.FromReleaseInfo("Paired Coop: unknown"));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void The_coop_entry_of_a_shared_list_carries_the_build_and_an_old_list_without_it_still_reads()
    {
        var file = Path.Combine(Path.GetTempPath(), "rot-list-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            SharedModList.Write(file, [new SharedModEntry(RotStack.CoopModuleId, "Bannerlord Coop", "v0.1.8", "0", Pinned), new SharedModEntry("Native", "Native", "v1.4.8", "")]);
            var text = File.ReadAllText(file);
            Assert.Single(System.Text.RegularExpressions.Regex.Matches(text, "CoopBuild"));
            var read = SharedModList.Read(text);
            Assert.Equal(Pinned, read[0].CoopBuild);
            Assert.Null(read[1].CoopBuild);

            var old = SharedModList.Read("{\"Kind\":\"CoodszModList\",\"Modules\":[{\"Id\":\"CoopNightly\",\"Name\":\"Bannerlord Coop\",\"Version\":\"v0.1.8\",\"WorkshopId\":\"0\"}]}");
            Assert.Null(Assert.Single(old).CoopBuild);
            Assert.Equal("CoopNightly", old[0].Id);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void Connect_compares_the_coop_build_with_the_hosts_list_or_else_with_the_pinned_build()
    {
        var withBuild = new List<SharedModEntry> { new(RotStack.CoopModuleId, "Bannerlord Coop", "v0.1.8", "0", Other) };
        // A list from this version has the build as the Coop entry's Version too; one with only that still counts.
        var inVersion = new List<SharedModEntry> { new(RotStack.CoopModuleId, "Bannerlord Coop", Other, "0") };
        var pinnedList = new List<SharedModEntry> { new(RotStack.CoopModuleId, "Bannerlord Coop", Pinned, "0", Pinned) };
        var oldList = new List<SharedModEntry> { new(RotStack.CoopModuleId, "Bannerlord Coop", "v0.1.8", "0") };

        Assert.Null(RotJoinCheck.CoopBuildProblem(Other, withBuild));
        Assert.Null(RotJoinCheck.CoopBuildProblem(Other, inVersion));
        Assert.Null(RotJoinCheck.CoopBuildProblem(Pinned, []));
        Assert.Null(RotJoinCheck.CoopBuildProblem(Pinned, oldList));
        Assert.Null(RotJoinCheck.CoopBuildProblem(Pinned, pinnedList));

        // The host on another build than the one this launcher carries: one side updates its tool.
        const string hostOther = "The host runs Coop 0.1.8+04038e2d and this launcher has Coop RoT build 2026-11-14 (01846e1). Update the Client Launcher, or ask the host to update the Server Tool.";
        Assert.Equal(hostOther, RotJoinCheck.CoopBuildProblem(Pinned, withBuild));
        Assert.Equal(hostOther, RotJoinCheck.CoopBuildProblem(Pinned, inVersion));
        Assert.Equal(hostOther, RotJoinCheck.CoopBuildProblem(null, withBuild));

        // This PC's Coop is not the carried build, and the host runs the carried build or no list says.
        const string pcOther = "The Coop on this PC is 0.1.8+04038e2d, not Coop RoT build 2026-11-14 (01846e1) that this launcher carries. The launcher puts its own in when you click Connect.";
        Assert.Equal(pcOther, RotJoinCheck.CoopBuildProblem(Other, []));
        Assert.Equal(pcOther, RotJoinCheck.CoopBuildProblem(Other, oldList));
        Assert.Equal(pcOther, RotJoinCheck.CoopBuildProblem(Other, pinnedList));
        Assert.Equal("The launcher could not read the Coop build on this PC (Common.dll in Modules\\Coop). This launcher carries Coop RoT build 2026-11-14 (01846e1) and puts it in when you click Connect.",
            RotJoinCheck.CoopBuildProblem(null, pinnedList));

        // The same build as the host's list goes to the log; without a build on the list nothing is said.
        Assert.Equal("Coop is RoT build 2026-11-14 (01846e1), the same as the host.", RotJoinCheck.SameCoopBuild(Pinned, pinnedList));
        Assert.Equal("Coop is 0.1.8+04038e2d, the same as the host.", RotJoinCheck.SameCoopBuild(Other, withBuild));
        Assert.Null(RotJoinCheck.SameCoopBuild(Pinned, oldList));
        Assert.Null(RotJoinCheck.SameCoopBuild(Pinned, withBuild));
        Assert.Null(RotJoinCheck.SameCoopBuild(null, pinnedList));

        // A missing Coop no longer says to unpack it by hand.
        var missing = RotJoinCheck.ModuleProblem(RotStack.CoopModuleId, null);
        Assert.Equal("Bannerlord Coop (CoopNightly) is not on this PC. This launcher carries Coop RoT build 2026-11-14 (01846e1) and puts it in when you click Connect.", missing);
        Assert.Contains(missing, RotJoinCheck.CoreProblems(_ => null));
    }

    // A launcher built without the zip (a Debug build) puts no Coop in, so no line may say Connect puts it in; the lines of
    // before ask for the host's build.
    [Fact]
    public void Without_the_carried_zip_the_coop_lines_ask_for_the_hosts_build()
    {
        var withBuild = new List<SharedModEntry> { new(RotStack.CoopModuleId, "Bannerlord Coop", "v0.1.8", "0", Other) };
        Assert.Null(RotJoinCheck.CoopBuildProblem(Pinned, [], carriesCoop: false));
        Assert.Null(RotJoinCheck.CoopBuildProblem(Other, withBuild, carriesCoop: false));
        Assert.Equal("Your Coop build is 0.1.8+04038e2d, but Realm of Thrones in Coop runs on 0.1.8+01846e13. Coop only lets you join on the very same build. Get the same Coop build as the host, then click Connect again.",
            RotJoinCheck.CoopBuildProblem(Other, [], carriesCoop: false));
        Assert.Equal("Your Coop build is 0.1.8+01846e13 and the host's is 0.1.8+04038e2d. Coop only lets you join on the very same build. Get the same Coop build as the host, then click Connect again.",
            RotJoinCheck.CoopBuildProblem(Pinned, withBuild, carriesCoop: false));
        Assert.Equal("The launcher could not read your Coop build (Common.dll in the Coop folder of the game's Modules folder). The host runs 0.1.8+01846e13. Get the same Coop build as the host, then click Connect again.",
            RotJoinCheck.CoopBuildProblem(null, [], carriesCoop: false));
        const string missing = "Bannerlord Coop (CoopNightly) is not on this PC. Ask the host for the Coop build they use and unpack it into the game's Modules folder (click Module folder), then click Check mods.";
        Assert.Equal(missing, RotJoinCheck.ModuleProblem(RotStack.CoopModuleId, null, carriesCoop: false));
        Assert.Contains(missing, RotJoinCheck.CoreProblems(_ => null, carriesCoop: false));
        Assert.DoesNotContain(RotJoinCheck.CoreProblems(_ => null, carriesCoop: false), line => line.Contains("when you click Connect", StringComparison.Ordinal));
    }

    [Fact]
    public void The_carried_coop_build_is_pinned_in_rotstack_the_projects_and_the_release_list()
    {
        var build = RotStack.CoopBuild;
        Assert.Equal("RoT build 2026-11-14 (01846e1)", build.Label);
        Assert.Equal("0.1.8", CoopBuildId.Version(build.Build));
        Assert.Equal(40, CoopBuildId.Commit(build.Build)!.Length);
        Assert.Contains(build.LabelDate + "-" + CoopBuildId.Commit(build.Build)![..7], build.ClientZip);
        Assert.Contains(build.LabelDate + "-" + CoopBuildId.Commit(build.Build)![..7], build.ServerZip);

        // Both tools carry the client zip and the Server Tool the server zip too, with the hashes RotStack pins.
        static string Property(string csproj, string name) =>
            System.Text.RegularExpressions.Regex.Match(csproj, "<" + name + ">([^<]+)<").Groups[1].Value;
        foreach (var (project, server) in new[] { ("src/RealmOfThronesLauncher/RealmOfThronesLauncher.csproj", false), ("src/RealmOfThronesHost/RealmOfThronesHost.csproj", true) })
        {
            var csproj = SourceFiles.Read(project);
            Assert.Contains("<Import Project=\"..\\RotCoopBuild.targets\" />", csproj);
            Assert.Equal(build.ClientZip, Property(csproj, "CoodszRotCoopClientName"));
            Assert.Equal(build.ClientZipSha256, Property(csproj, "CoodszRotCoopClientSha256"), ignoreCase: true);
            Assert.Equal(server ? build.ServerZip : "", Property(csproj, "CoodszRotCoopServerName"));
            Assert.Equal(server ? build.ServerZipSha256 : "", Property(csproj, "CoodszRotCoopServerSha256"), ignoreCase: true);
        }

        // A single file publish carries them and stops on a missing zip or another hash; a plain build carries nothing.
        var targets = SourceFiles.Read("src/RotCoopBuild.targets");
        Assert.Contains("LogicalName=\"Coodsz.RotCoop.$(CoodszRotCoopClientName)\"", targets);
        Assert.Contains("LogicalName=\"Coodsz.RotCoop.$(CoodszRotCoopServerName)\"", targets);
        Assert.Contains("<PropertyGroup Condition=\"'$(PublishSingleFile)' == 'true'\">", targets);
        Assert.Contains("artifacts\\release-assets-cache\\$(CoodszRotCoopClientName)", targets);
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(targets, "<GetFileHash ").Count);
        Assert.Contains("'$(_CoodszRotCoopClientHash)' != '$(CoodszRotCoopClientSha256)'", targets);
        Assert.Contains("'$(_CoodszRotCoopServerHash)' != '$(CoodszRotCoopServerSha256)'", targets);
        Assert.Contains("artifacts/", SourceFiles.Read(".gitignore"));
        // The targets name the tests that check them (this class).
        Assert.DoesNotContain("RotCoopBuildTests", targets);
        Assert.Contains("(" + nameof(RotSharedListTests) + " checks them", targets);

        // The build script cleans and checks out only a build clone, and never writes into any Steam library.
        var script = SourceFiles.Read("tools/Build-RotCoop.ps1");
        var setCommit = script[script.IndexOf("function Set-RepoCommit", StringComparison.Ordinal)..];
        Assert.True(setCommit.IndexOf("is not a build clone", StringComparison.Ordinal) < setCommit.IndexOf("checkout -q --detach", StringComparison.Ordinal));
        Assert.Contains("-ne 'DISABLED'", setCommit);
        Assert.Contains(".git\\coodsz-build-clone", setCommit);
        Assert.Contains("steamapps\\libraryfolders.vdf", script);
        Assert.Contains("IndexOf('\\steamapps\\', [StringComparison]::OrdinalIgnoreCase)", script);

        // The two files ride only inside the exes: the release does not attach them.
        using var json = System.Text.Json.JsonDocument.Parse(SourceFiles.Read("tools/release-assets.json"));
        var assets = json.RootElement.GetProperty("repos").EnumerateArray()
            .Single(repo => repo.GetProperty("repo").GetString() == RotRelease.Repo)
            .GetProperty("assets").EnumerateArray()
            .ToDictionary(asset => asset.GetProperty("name").GetString()!, asset => asset);
        Assert.False(assets.ContainsKey(build.ClientZip));
        Assert.False(assets.ContainsKey(build.ServerZip));
    }

    [Fact]
    public void The_coop_entry_version_is_the_build_and_the_compat_module_the_launcher_carries_is_never_missing()
    {
        var root = Path.Combine(Path.GetTempPath(), "rot-versions-" + Guid.NewGuid().ToString("N"));
        try
        {
            var assembly = typeof(CoopBuildId).Assembly.Location;
            var build = System.Diagnostics.FileVersionInfo.GetVersionInfo(assembly).ProductVersion!;
            var coop = Path.Combine(root, "Modules", "Coop");
            Directory.CreateDirectory(Path.Combine(coop, "bin", "Win64_Shipping_Client"));
            File.Copy(assembly, Path.Combine(coop, "bin", "Win64_Shipping_Client", "Common.dll"));
            (string, string, string?)? Found(string id) => id == RotStack.CoopModuleId ? ("Bannerlord Coop", "v0.1.8", coop) : (id, "v1.0.0", null);

            // The host writes the build as the Coop entry's Version (and CoopBuild).
            var host = RotSharedList.Entries(new RotSetupBook().Current(), Found);
            var entry = host.Single(item => item.Id == RotStack.CoopModuleId);
            Assert.Equal((build, build), (entry.Version, entry.CoopBuild));

            // The player reports Common.dll's build for Coop, so the compare stays exact.
            Assert.Equal(build, RotSharedList.InstalledVersion(entry, Found(RotStack.CoopModuleId), null));
            // An old list with only v0.1.8 is compared with the SubModule.xml version as before.
            Assert.Equal("v0.1.8", RotSharedList.InstalledVersion(entry with { Version = "v0.1.8", CoopBuild = null }, Found(RotStack.CoopModuleId), null));
            Assert.Null(RotSharedList.InstalledVersion(entry, null, null));

            // The live test case: a list imported before the first Connect, when the compat module is not in Modules yet.
            // Connect puts in the copy the launcher carries, so that copy's version counts and nothing differs.
            var installed = host.Where(item => item.Id != RotStack.CompatModuleId)
                .Select(item => (item.Id, Version: RotSharedList.InstalledVersion(item, Found(item.Id), RotStack.CompatVersion)))
                .ToDictionary(item => item.Id, item => item.Version!);
            var compat = host.Single(item => item.Id == RotStack.CompatModuleId) with { Version = RotStack.CompatVersion };
            installed[compat.Id] = RotSharedList.InstalledVersion(compat, null, RotStack.CompatVersion)!;
            Assert.Empty(RotSharedList.VersionProblems(host.Select(item => item.Id == compat.Id ? compat : item).ToList(), installed));
            // Without a carried copy a compat module that is not here is still missing.
            Assert.Null(RotSharedList.InstalledVersion(compat, null, null));

            // The launcher uses it for every compare against the host's list.
            var launcher = SourceFiles.Method(SourceFiles.Read("src/RealmOfThronesLauncher/MainWindow.ServerMods.cs"), "private Dictionary<string, string> InstalledVersions(IReadOnlyList<SharedModEntry> list)");
            Assert.Contains("var carried = CarriedCompatXml() is { } xml ? VersionIn(Path.GetDirectoryName(xml)) : null;", launcher);
            Assert.Contains("RotSharedList.InstalledVersion(entry, Describe(id), carried) is { } version", launcher);
            // So does the Server Tool's import, so a player's list does not read as another Coop version there.
            Assert.Contains("RotSharedList.InstalledVersion(entry, DescribeServerModule(entry.Id), null) is not { } have",
                SourceFiles.Read("src/RealmOfThronesHost/MainWindow.ModSetups.cs"));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void A_coop_build_that_cannot_be_read_is_unknown_and_never_throws()
    {
        var root = Path.Combine(Path.GetTempPath(), "rot-unknown-" + Guid.NewGuid().ToString("N"));
        try
        {
            // A Common.dll that is not a program, a folder named Common.dll, and a release-info.txt that is a folder.
            var client = Path.Combine(root, "Modules", "Coop");
            Directory.CreateDirectory(Path.Combine(client, "bin", "Win64_Shipping_Client"));
            File.WriteAllText(Path.Combine(client, "bin", "Win64_Shipping_Client", "Common.dll"), "not a dll");
            Directory.CreateDirectory(Path.Combine(client, "bin", "Win64_Shipping_Server", "Common.dll"));
            Assert.Null(CoopBuildId.FromModule(client));
            Assert.Null(CoopBuildId.FromModule(null));
            Assert.Null(CoopBuildId.FromModule(""));

            var server = Path.Combine(root, "server");
            Directory.CreateDirectory(Path.Combine(server, "release-info.txt"));
            Directory.CreateDirectory(Path.Combine(server, "engine", "Modules", "Coop", "bin", "Win64_Shipping_Server"));
            File.WriteAllText(Path.Combine(server, "engine", "Modules", "Coop", "bin", "Win64_Shipping_Server", "Common.dll"), "");
            Assert.Null(CoopBuildId.FromServer(server));
            Assert.Null(CoopBuildId.FromServer(Path.Combine(root, "no such folder")));

            // Unknown gives the plain lines, not an error.
            Assert.NotNull(RotJoinCheck.CoopBuildProblem(null, []));
            Assert.NotNull(RotServerPath.CoopBuildProblem(null));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void The_server_tool_starts_only_the_pinned_coop_build()
    {
        Assert.Null(RotServerPath.CoopBuildProblem(Pinned));
        Assert.Equal("The Coop server is build 0.1.8+04038e2d, but Realm of Thrones in Coop runs on 0.1.8+01846e13. Players can only join the very same build. Get the Coop server of build 0.1.8+01846e13, then click Choose Coop server and pick it.",
            RotServerPath.CoopBuildProblem(Other));
        Assert.Equal("The tool could not read the Coop build of this Coop server (engine\\Modules\\Coop or release-info.txt). Realm of Thrones in Coop runs on 0.1.8+01846e13. Get the Coop server of that build, then click Choose Coop server and pick it.",
            RotServerPath.CoopBuildProblem(null));
    }

    // ----- Putting a kept Coop back at Connect (RotCoopRestore) -----

    private static RotCoopFolder Folder(string directory, string id, string? build) => new(directory, id, build);

    [Fact]
    public void Restore_decides_from_the_builds_in_modules_and_in_the_kept_folders()
    {
        var pinnedKept = Folder(@"C:\Game\Coop-kept-0.1.8", RotStack.CoopModuleId, Pinned);
        var otherKept = Folder(@"C:\Game\Coop-kept-0.1.7", RotStack.CoopModuleId, Other);
        var pinnedHere = Folder(@"C:\Game\Modules\Coop", RotStack.CoopModuleId, Pinned);
        var otherHere = Folder(@"C:\Game\Modules\Coop", "Coop", "0.1.7+aaaaaaaa");

        Assert.Equal(RotRestoreAction.Nothing, RotCoopRestore.Decide([pinnedHere], [pinnedKept], false).Action);
        Assert.Equal(RotRestoreAction.Nothing, RotCoopRestore.Decide([], [], false).Action);
        Assert.Equal(RotRestoreAction.Nothing, RotCoopRestore.Decide([], [otherKept], false).Action);
        // A kept folder of another module id with the same build is not the pinned Coop.
        Assert.Equal(RotRestoreAction.Nothing, RotCoopRestore.Decide([], [Folder(@"C:\Game\Coop-kept-x", "Coop", Pinned)], false).Action);

        var restore = RotCoopRestore.Decide([], [otherKept, pinnedKept], false);
        Assert.Equal(RotRestoreAction.Restore, restore.Action);
        Assert.Equal(pinnedKept, restore.Kept);
        Assert.Empty(restore.Park);

        var park = RotCoopRestore.Decide([otherHere], [pinnedKept], false);
        Assert.Equal(RotRestoreAction.ParkThenRestore, park.Action);
        Assert.Equal(new[] { otherHere }, park.Park);

        // Two kept folders with the pinned build: the first by name wins.
        var second = Folder(@"C:\Game\Coop-kept-20261111-101010", RotStack.CoopModuleId, "01846e136cf38cf64c79d9d4014ddb2e01907de3");
        Assert.Equal(pinnedKept, RotCoopRestore.Decide([], [second, pinnedKept], false).Kept);

        Assert.Equal(RotRestoreAction.GameRunning, RotCoopRestore.Decide([otherHere], [pinnedKept], true).Action);
        Assert.Equal(RotRestoreAction.Nothing, RotCoopRestore.Decide([pinnedHere], [pinnedKept], true).Action);
        Assert.Equal("Coop", RotCoopRestore.OriginalName(@"C:\Game\Coop-kept-0.1.8\"));
    }

    private static string Module(string folder, string id, string version)
    {
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "SubModule.xml"), "<Module><Id value=\"" + id + "\"/><Version value=\"" + version + "\"/></Module>");
        return folder;
    }

    [Fact]
    public void Restore_moves_the_kept_coop_back_and_parks_the_other_one_and_never_deletes()
    {
        var game = Path.Combine(Path.GetTempPath(), "rot-restore-" + Guid.NewGuid().ToString("N"));
        try
        {
            var kept = Module(Path.Combine(game, "Coop-kept-0.1.8"), RotStack.CoopModuleId, "v0.1.8");
            File.WriteAllText(Path.Combine(kept, "pinned.txt"), "pinned");
            var other = Module(Path.Combine(game, "Modules", "Coop"), "Coop", "v0.1.7");
            File.WriteAllText(Path.Combine(other, "other.txt"), "other");
            Module(Path.Combine(game, "Modules", "Native"), "Native", "v1.4.8");
            Module(Path.Combine(game, "Coop backup"), RotStack.CoopModuleId, "v0.1.8");

            Assert.Equal(new[] { other }, RotCoopRestore.InModules(game).Select(folder => folder.Directory));
            Assert.Equal(new[] { kept }, RotCoopRestore.Kept(game).Select(folder => folder.Directory));

            // The game runs: the line says so and nothing moves.
            var running = new RotRestorePlan(RotRestoreAction.GameRunning, Folder(kept, RotStack.CoopModuleId, Pinned), []);
            Assert.Equal("Bannerlord is running, so the launcher did not put the Coop build 0.1.8+01846e13 back from Coop-kept-0.1.8. Close the game, then click Connect again.",
                RotCoopRestore.Apply(game, running));
            Assert.True(File.Exists(Path.Combine(other, "other.txt")));
            Assert.Null(RotCoopRestore.Apply(game, new RotRestorePlan(RotRestoreAction.Nothing, null, [])));

            var plan = new RotRestorePlan(RotRestoreAction.ParkThenRestore, Folder(kept, RotStack.CoopModuleId, Pinned), [Folder(other, "Coop", Other)]);
            Assert.Equal("Moved the other Coop out of the game's Modules folder to Coop-kept-0.1.7 and put the Coop build 0.1.8+01846e13 back from Coop-kept-0.1.8.",
                RotCoopRestore.Apply(game, plan));
            Assert.Equal("pinned", File.ReadAllText(Path.Combine(game, "Modules", "Coop", "pinned.txt")));
            Assert.Equal("other", File.ReadAllText(Path.Combine(game, "Coop-kept-0.1.7", "other.txt")));
            Assert.False(Directory.Exists(kept));

            // Restore alone, and a Modules place that something else holds stops it before anything moves.
            Directory.Move(Path.Combine(game, "Modules", "Coop"), kept);
            Assert.Equal("Put the Coop build 0.1.8+01846e13 back into the game's Modules folder from Coop-kept-0.1.8, where another launcher had moved it.",
                RotCoopRestore.Apply(game, new RotRestorePlan(RotRestoreAction.Restore, Folder(kept, RotStack.CoopModuleId, Pinned), [])));
            Directory.Move(Path.Combine(game, "Modules", "Coop"), kept);
            Directory.CreateDirectory(Path.Combine(game, "Modules", "Coop"));
            Assert.Equal("The Coop build 0.1.8+01846e13 is in Coop-kept-0.1.8, but Modules\\Coop is taken, so the launcher left both as they are. Move one of them yourself, then click Connect again.",
                RotCoopRestore.Apply(game, new RotRestorePlan(RotRestoreAction.Restore, Folder(kept, RotStack.CoopModuleId, Pinned), [])));
            Assert.True(File.Exists(Path.Combine(kept, "pinned.txt")));

            // A park name that is taken gets the date instead.
            Assert.Matches(@"Coop-kept-\d{8}-\d{6}$", RotCoopRestore.ParkPath(game, Path.Combine(game, "Modules", "Coop"), "v0.1.8"));
            Assert.Equal(Path.Combine(game, "Coop-kept-0.1.9"), RotCoopRestore.ParkPath(game, Path.Combine(game, "Modules", "Coop"), "v0.1.9"));
        }
        finally
        {
            if (Directory.Exists(game))
                Directory.Delete(game, recursive: true);
        }
    }

    // The other Coop is parked, then the kept folder cannot move (a file in it is open): Apply throws for Connect's
    // line, nothing is lost, and the next Connect sees no Coop in Modules and only puts the pinned build back.
    [Fact]
    public void A_restore_that_fails_half_way_loses_nothing_and_the_next_connect_finishes_it()
    {
        var game = Path.Combine(Path.GetTempPath(), "rot-restore-" + Guid.NewGuid().ToString("N"));
        try
        {
            var kept = Module(Path.Combine(game, "Coop-kept-0.1.8"), RotStack.CoopModuleId, "v0.1.8");
            var other = Module(Path.Combine(game, "Modules", "Coop"), "Coop", "v0.1.7");
            var plan = new RotRestorePlan(RotRestoreAction.ParkThenRestore, Folder(kept, RotStack.CoopModuleId, Pinned), [Folder(other, "Coop", Other)]);
            using (new FileStream(Path.Combine(kept, "open.txt"), FileMode.Create, FileAccess.ReadWrite, FileShare.None))
                Assert.ThrowsAny<IOException>(() => RotCoopRestore.Apply(game, plan));
            Assert.True(File.Exists(Path.Combine(game, "Coop-kept-0.1.7", "SubModule.xml")));
            Assert.True(File.Exists(Path.Combine(kept, "SubModule.xml")));
            Assert.Empty(RotCoopRestore.InModules(game));

            var next = RotCoopRestore.Decide([], [Folder(Path.Combine(game, "Coop-kept-0.1.7"), "Coop", Other), Folder(kept, RotStack.CoopModuleId, Pinned)], false);
            Assert.Equal(RotRestoreAction.Restore, next.Action);
            Assert.StartsWith("Put the Coop build", RotCoopRestore.Apply(game, next));
            Assert.True(File.Exists(Path.Combine(game, "Modules", "Coop", "open.txt")));
        }
        finally
        {
            if (Directory.Exists(game))
                Directory.Delete(game, recursive: true);
        }
    }

    // ----- The carried Coop in and out of Modules (RotCoopInstall) -----

    // A temp folder per test: the game, the carried copy and the marker, never the real game or the tool's data folder.
    private sealed class CoopScratch : IDisposable
    {
        public readonly string Root = Path.Combine(Path.GetTempPath(), "rot-coop-in-" + Guid.NewGuid().ToString("N"));
        public string Game => Path.Combine(Root, "game");
        public string Modules => Path.Combine(Game, "Modules");
        public string Marker => Path.Combine(Root, "data", RotCoopInstall.MarkerFileName);
        public string Carried { get; }

        public CoopScratch()
        {
            Carried = Coop(Path.Combine(Root, "carried", "Coop"), RotStack.CoopModuleId, Pinned);
            File.WriteAllText(Path.Combine(Carried, "bin.txt"), "ours");
            Module(Path.Combine(Modules, "Native"), "Native", "v1.4.8");
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
                Directory.Delete(Root, recursive: true);
        }
    }

    // The build a test Coop folder says it is: Common.dll cannot be faked here, so build.txt stands in for its ProductVersion.
    private static string? BuildTxt(string folder) =>
        File.Exists(Path.Combine(folder, "build.txt")) ? File.ReadAllText(Path.Combine(folder, "build.txt")) : null;

    private static string Coop(string folder, string id, string build, string version = "v0.1.8")
    {
        Module(folder, id, version);
        File.WriteAllText(Path.Combine(folder, "build.txt"), build);
        return folder;
    }

    private static RotPutIn PutIn(CoopScratch scratch, bool yes = true, bool gameRunning = false, List<string>? asked = null) =>
        RotCoopInstall.PutIn(scratch.Game, scratch.Carried, scratch.Marker, gameRunning, others =>
        {
            asked?.AddRange(others.Select(folder => Path.GetFileName(folder.Directory)));
            return yes;
        }, BuildTxt);

    private static IReadOnlyList<string> TakeOut(CoopScratch scratch, bool gameRunning = false) =>
        RotCoopInstall.TakeOut(scratch.Marker, gameRunning, BuildTxt).Lines;

    [Fact]
    public void The_carried_zip_is_unpacked_once_after_its_sha256_and_again_when_the_unpack_is_partial()
    {
        using var scratch = new CoopScratch();
        using var zip = new MemoryStream();
        using (var archive = new System.IO.Compression.ZipArchive(zip, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, text) in new[] { ("Coop/SubModule.xml", "<Module><Id value=\"CoopNightly\"/></Module>"), ("Coop/bin/Win64_Shipping_Client/Common.dll", "dll"), ("LICENSE", "license") })
            {
                using var writer = new StreamWriter(archive.CreateEntry(name).Open());
                writer.Write(text);
            }
        }

        var sha = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(zip.ToArray()));
        var folder = Path.Combine(scratch.Root, "CoopBuild", "0.1.8+01846e13");

        // Another SHA256 stops before anything is written.
        zip.Position = 0;
        Assert.Throws<InvalidDataException>(() => RotCoopInstall.Unpack(zip, new string('0', 64), folder));
        Assert.False(Directory.Exists(folder));

        zip.Position = 0;
        var coop = RotCoopInstall.Unpack(zip, sha.ToLowerInvariant(), folder);
        Assert.Equal(Path.Combine(folder, "Coop"), coop);
        Assert.Equal("dll", File.ReadAllText(Path.Combine(coop, "bin", "Win64_Shipping_Client", "Common.dll")));
        Assert.True(File.Exists(Path.Combine(folder, "LICENSE")));
        Assert.True(File.Exists(Path.Combine(folder, RotCoopInstall.DoneFileName)));

        // A done record that fits: the zip is not read again.
        Assert.Equal(coop, RotCoopInstall.Unpack(new MemoryStream(), sha, folder));

        // A partial unpack (no done record, or a file gone or added since) is unpacked again, and a stray file goes.
        File.Delete(Path.Combine(coop, "bin", "Win64_Shipping_Client", "Common.dll"));
        File.WriteAllText(Path.Combine(coop, "stray.txt"), "stray");
        zip.Position = 0;
        RotCoopInstall.Unpack(zip, sha, folder);
        Assert.True(File.Exists(Path.Combine(coop, "bin", "Win64_Shipping_Client", "Common.dll")));
        Assert.False(File.Exists(Path.Combine(coop, "stray.txt")));
        File.Delete(Path.Combine(folder, RotCoopInstall.DoneFileName));
        Assert.Throws<InvalidDataException>(() => RotCoopInstall.Unpack(new MemoryStream(), sha, folder));
        zip.Position = 0;
        RotCoopInstall.Unpack(zip, sha, folder);
        Assert.True(File.Exists(Path.Combine(folder, RotCoopInstall.DoneFileName)));

        // The exe names the resource the way src\RotCoopBuild.targets embeds it.
        Assert.Equal("Coodsz.RotCoop." + RotStack.CoopBuild.ClientZip, RotCoopInstall.ResourceName);
        Assert.Contains("LogicalName=\"Coodsz.RotCoop.$(CoodszRotCoopClientName)\"", SourceFiles.Read(Path.Combine("src", "RotCoopBuild.targets")));
    }

    [Fact]
    public void Connect_puts_the_carried_coop_in_and_close_takes_it_out_with_nothing_left()
    {
        using var scratch = new CoopScratch();
        var result = PutIn(scratch);
        Assert.Null(result.Problem);
        Assert.Equal(new[] { "Put Coop RoT build 2026-11-14 (01846e1) in the game's Modules folder. It comes back out when you close this launcher." }, result.Lines);
        Assert.Equal("ours", File.ReadAllText(Path.Combine(scratch.Modules, "Coop", "bin.txt")));
        Assert.False(Directory.Exists(Path.Combine(scratch.Modules, RotCoopInstall.WorkFolderName)));
        Assert.True(RotCoopInstall.HasWork(scratch.Marker));

        // A second Connect finds it in and does nothing.
        Assert.Empty(PutIn(scratch).Lines);

        Assert.Equal(new[] { "Took Coop RoT build 2026-11-14 (01846e1) back out of the game's Modules folder." }, TakeOut(scratch));
        Assert.Equal(new[] { "Native" }, Directory.GetDirectories(scratch.Modules).Select(Path.GetFileName));
        Assert.False(File.Exists(scratch.Marker));
        Assert.False(RotCoopInstall.HasWork(scratch.Marker));
        Assert.Empty(TakeOut(scratch));
        // The carried copy stays for the next run.
        Assert.True(File.Exists(Path.Combine(scratch.Carried, "bin.txt")));
    }

    [Fact]
    public void Another_coop_is_parked_after_a_yes_and_put_back_at_close_and_a_no_or_the_game_moves_nothing()
    {
        using var scratch = new CoopScratch();
        Coop(Path.Combine(scratch.Modules, "Coop"), "Coop", "0.1.7+aaaaaaaa", "v0.1.7");
        File.WriteAllText(Path.Combine(scratch.Modules, "Coop", "mine.txt"), "player");
        Coop(Path.Combine(scratch.Modules, "CoopNightly"), RotStack.CoopModuleId, Other);

        // Bannerlord runs, then the player says no: nothing moves and no marker is written.
        Assert.Equal("Bannerlord is running, so the launcher can not put Coop RoT build 2026-11-14 (01846e1) in the game's Modules folder. Close the game, then click Connect again.",
            PutIn(scratch, gameRunning: true).Problem);
        var asked = new List<string>();
        Assert.Equal("Your other Coop stays in Modules, so the game was not started. Realm of Thrones only runs on Coop RoT build 2026-11-14 (01846e1) that this launcher carries.",
            PutIn(scratch, yes: false, asked: asked).Problem);
        Assert.Equal(new[] { "Coop", "CoopNightly" }, asked.Order());
        Assert.Equal("player", File.ReadAllText(Path.Combine(scratch.Modules, "Coop", "mine.txt")));
        Assert.False(File.Exists(scratch.Marker));
        Assert.Equal("Realm of Thrones runs on Coop RoT build 2026-11-14 (01846e1) that this launcher carries. Your Coop v0.1.7 in Modules will be set aside while this launcher runs, and put back when you close it. Continue?",
            RotCoopInstall.Question([new RotCoopFolder(Path.Combine(scratch.Modules, "Coop"), "Coop", null)]));

        var result = PutIn(scratch);
        Assert.Null(result.Problem);
        Assert.Equal("player", File.ReadAllText(Path.Combine(scratch.Game, "Coop-parked-by-rot-0.1.7", "mine.txt")));
        Assert.True(Directory.Exists(Path.Combine(scratch.Game, "CoopNightly-parked-by-rot-0.1.8")));
        Assert.Equal("ours", File.ReadAllText(Path.Combine(scratch.Modules, "Coop", "bin.txt")));
        Assert.Contains("Set Coop-parked-by-rot-0.1.7 aside in the game folder. It goes back into Modules when you close this launcher.", result.Lines);

        // While the game runs nothing comes out; after it closed both are back and ours is gone.
        Assert.Equal(new[] { "Bannerlord is running, so Coop RoT build 2026-11-14 (01846e1) stays in the game's Modules folder for now. It comes back out when you close this launcher." },
            TakeOut(scratch, gameRunning: true));
        Assert.True(File.Exists(Path.Combine(scratch.Modules, "Coop", "bin.txt")));
        var lines = TakeOut(scratch);
        Assert.Contains("Put Coop-parked-by-rot-0.1.7 back into Modules as Coop.", lines);
        Assert.Contains("Put CoopNightly-parked-by-rot-0.1.8 back into Modules as CoopNightly.", lines);
        Assert.Equal("player", File.ReadAllText(Path.Combine(scratch.Modules, "Coop", "mine.txt")));
        Assert.Equal(Other, BuildTxt(Path.Combine(scratch.Modules, "CoopNightly")));
        Assert.False(Directory.Exists(Path.Combine(scratch.Game, "Coop-parked-by-rot-0.1.7")));
        Assert.False(File.Exists(scratch.Marker));
    }

    [Fact]
    public void A_modules_coop_of_the_rot_build_is_used_and_stays_and_a_folder_that_is_not_coop_is_never_moved()
    {
        using var scratch = new CoopScratch();
        Coop(Path.Combine(scratch.Modules, "Coop"), RotStack.CoopModuleId, Pinned);
        File.WriteAllText(Path.Combine(scratch.Modules, "Coop", "mine.txt"), "player");
        var used = PutIn(scratch, asked: null);
        Assert.Null(used.Problem);
        Assert.Empty(used.Lines);
        Assert.False(File.Exists(scratch.Marker));
        Assert.Empty(TakeOut(scratch));
        Assert.Equal("player", File.ReadAllText(Path.Combine(scratch.Modules, "Coop", "mine.txt")));

        // The game running does not matter when nothing has to move.
        Assert.Null(PutIn(scratch, gameRunning: true).Problem);

        Directory.Delete(Path.Combine(scratch.Modules, "Coop"), recursive: true);
        Directory.CreateDirectory(Path.Combine(scratch.Modules, "Coop", "notes"));
        Assert.Equal(RotCoopInstall.Taken, PutIn(scratch).Problem);
        Assert.True(Directory.Exists(Path.Combine(scratch.Modules, "Coop", "notes")));
        Assert.False(File.Exists(scratch.Marker));
    }

    // The copy in fails half way (a carried file is locked): the half copy goes, the parked Coop comes back, no marker stays.
    [Fact]
    public void A_copy_that_fails_half_way_is_undone_at_once()
    {
        using var scratch = new CoopScratch();
        Coop(Path.Combine(scratch.Modules, "Coop"), "Coop", "0.1.7+aaaaaaaa", "v0.1.7");
        RotPutIn result;
        using (new FileStream(Path.Combine(scratch.Carried, "bin.txt"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            result = PutIn(scratch);
        Assert.StartsWith("Could not put Coop RoT build 2026-11-14 (01846e1) in the game's Modules folder, so the game was not started. ", result.Problem);
        Assert.EndsWith(" Close anything that uses the Modules folder, then click Connect again.", result.Problem);
        Assert.Contains("Put Coop-parked-by-rot-0.1.7 back into Modules as Coop.", result.Lines);
        Assert.Equal("0.1.7+aaaaaaaa", BuildTxt(Path.Combine(scratch.Modules, "Coop")));
        Assert.False(Directory.Exists(Path.Combine(scratch.Modules, RotCoopInstall.WorkFolderName)));
        Assert.False(File.Exists(scratch.Marker));
    }

    // Our Coop cannot come out at close (a file in it is open): the line says so, the parked Coop waits in the marker,
    // and the next start finishes both.
    [Fact]
    public void A_take_out_that_fails_keeps_the_marker_and_the_next_start_finishes_it()
    {
        using var scratch = new CoopScratch();
        Coop(Path.Combine(scratch.Modules, "Coop"), "Coop", "0.1.7+aaaaaaaa", "v0.1.7");
        Assert.Null(PutIn(scratch).Problem);
        IReadOnlyList<string> lines;
        using (new FileStream(Path.Combine(scratch.Modules, "Coop", "bin.txt"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            lines = TakeOut(scratch);
        Assert.Single(lines);
        Assert.StartsWith("Could not take Coop RoT build 2026-11-14 (01846e1) out of the game's Modules folder. ", lines[0]);
        Assert.EndsWith(" The launcher tries again on its next start.", lines[0]);
        Assert.Equal(Pinned, BuildTxt(Path.Combine(scratch.Modules, "Coop")));
        Assert.True(Directory.Exists(Path.Combine(scratch.Game, "Coop-parked-by-rot-0.1.7")));
        Assert.True(RotCoopInstall.HasWork(scratch.Marker));

        lines = TakeOut(scratch);
        Assert.Contains("Put Coop-parked-by-rot-0.1.7 back into Modules as Coop.", lines);
        Assert.Equal("0.1.7+aaaaaaaa", BuildTxt(Path.Combine(scratch.Modules, "Coop")));
        Assert.False(File.Exists(scratch.Marker));
    }

    // A launcher killed half way: the marker names our copy, the work folder holds a half copy (or a half removal with a
    // locked file) and the player's Coop is parked. The next start puts the player's Coop back first, and clears the rest
    // once the file is free. A Coop someone put in Modules since is never taken for ours. The marker is one of bb936c11
    // (no id, the player's Coop parked as Coop-kept-0.1.7), which still reads.
    [Fact]
    public void After_a_crash_the_next_start_undoes_a_half_copy_and_never_takes_a_coop_that_is_not_ours()
    {
        using var scratch = new CoopScratch();
        var parked = Coop(Path.Combine(scratch.Game, "Coop-kept-0.1.7"), "Coop", "0.1.7+aaaaaaaa", "v0.1.7");
        var work = Path.Combine(scratch.Modules, RotCoopInstall.WorkFolderName);
        Directory.CreateDirectory(Path.Combine(work, "bin"));
        File.WriteAllText(Path.Combine(work, "bin", "half.dll"), "half");
        Directory.CreateDirectory(Path.GetDirectoryName(scratch.Marker)!);
        File.WriteAllText(scratch.Marker, System.Text.Json.JsonSerializer.Serialize(
            new RotCoopMarker(Path.Combine(scratch.Modules, "Coop"), [new SetAsideEntry(Path.Combine(scratch.Modules, "Coop"), parked)])));

        IReadOnlyList<string> lines;
        using (new FileStream(Path.Combine(work, "bin", "half.dll"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            lines = TakeOut(scratch);
        Assert.Contains("Put Coop-kept-0.1.7 back into Modules as Coop.", lines);
        Assert.Equal("0.1.7+aaaaaaaa", BuildTxt(Path.Combine(scratch.Modules, "Coop")));
        Assert.True(RotCoopInstall.HasWork(scratch.Marker));

        // The work folder is free now; Modules\Coop is the player's again, so only the work folder goes.
        lines = TakeOut(scratch);
        Assert.Empty(lines);
        Assert.False(Directory.Exists(work));
        Assert.Equal("0.1.7+aaaaaaaa", BuildTxt(Path.Combine(scratch.Modules, "Coop")));
        Assert.False(File.Exists(scratch.Marker));
    }

    // Something else took a parked Coop's place in Modules: it stays parked and in the marker, the player is told where it
    // is (under Connect), and a later start puts it back once the place is free.
    [Fact]
    public void A_parked_coop_whose_place_is_taken_stays_in_the_marker_and_the_player_is_told_where_it_is()
    {
        using var scratch = new CoopScratch();
        Coop(Path.Combine(scratch.Modules, "CoopNightly"), RotStack.CoopModuleId, Other);
        Assert.Null(PutIn(scratch).Problem);
        var parked = Path.Combine(scratch.Game, "CoopNightly-parked-by-rot-0.1.8");
        Directory.CreateDirectory(Path.Combine(scratch.Modules, "CoopNightly", "notes"));

        var result = RotCoopInstall.TakeOut(scratch.Marker, false, BuildTxt);
        Assert.Equal("Your CoopNightly is in " + parked + ", because something else is in Modules\\CoopNightly now. Move that out of Modules and the launcher puts yours back when it next starts.",
            result.Notice);
        Assert.Equal(new[] { "Took Coop RoT build 2026-11-14 (01846e1) back out of the game's Modules folder." }, result.Lines);
        Assert.False(Directory.Exists(Path.Combine(scratch.Modules, "Coop")));
        Assert.Equal(Other, BuildTxt(parked));
        Assert.True(RotCoopInstall.HasWork(scratch.Marker));
        // Each start tells the player again until the place is free.
        Assert.Equal(result.Notice, RotCoopInstall.TakeOut(scratch.Marker, false, BuildTxt).Notice);

        Directory.Delete(Path.Combine(scratch.Modules, "CoopNightly"), recursive: true);
        result = RotCoopInstall.TakeOut(scratch.Marker, false, BuildTxt);
        Assert.Null(result.Notice);
        Assert.Equal(new[] { "Put CoopNightly-parked-by-rot-0.1.8 back into Modules as CoopNightly." }, result.Lines);
        Assert.Equal(Other, BuildTxt(Path.Combine(scratch.Modules, "CoopNightly")));
        Assert.False(File.Exists(scratch.Marker));
    }

    // A marker that cannot be read never means nothing to undo: nothing in Modules moves and the line goes under Connect.
    // A locked one stays for the next start; a garbled one is kept aside as .bad, so the next start begins clean. The marker
    // is written whole, so no .tmp stays behind.
    [Fact]
    public void A_marker_that_cannot_be_read_moves_nothing_and_a_garbled_one_is_kept_aside()
    {
        using var scratch = new CoopScratch();
        Coop(Path.Combine(scratch.Modules, "Coop"), "Coop", "0.1.7+aaaaaaaa", "v0.1.7");
        Assert.Null(PutIn(scratch).Problem);
        Assert.False(File.Exists(scratch.Marker + ".tmp"));
        var parked = Path.Combine(scratch.Game, "Coop-parked-by-rot-0.1.7");
        var written = File.ReadAllText(scratch.Marker);

        RotTakeOut result;
        using (new FileStream(scratch.Marker, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            Assert.True(RotCoopInstall.HasWork(scratch.Marker));
            result = RotCoopInstall.TakeOut(scratch.Marker, false, BuildTxt);
        }
        Assert.StartsWith("The launcher could not read its record of the Coop folders it moved, so it left the game's Modules folder as it is. ", result.Notice);
        Assert.EndsWith(" The launcher tries again on its next start.", result.Notice);
        Assert.Empty(result.Lines);
        Assert.Equal(written, File.ReadAllText(scratch.Marker));

        File.WriteAllText(scratch.Marker, "{ not json");
        Assert.True(RotCoopInstall.HasWork(scratch.Marker));
        Assert.True(File.Exists(scratch.Marker));
        result = RotCoopInstall.TakeOut(scratch.Marker, false, BuildTxt);
        Assert.Equal("The launcher could not read its record of the Coop folders it moved, so it left the game's Modules folder as it is. The record is kept as "
            + scratch.Marker + ".bad. If your own Coop is gone from Modules, look for it in the game folder.", result.Notice);
        Assert.False(File.Exists(scratch.Marker));
        Assert.Equal("{ not json", File.ReadAllText(scratch.Marker + ".bad"));
        Assert.Equal(Pinned, BuildTxt(Path.Combine(scratch.Modules, "Coop")));
        Assert.Equal("0.1.7+aaaaaaaa", BuildTxt(parked));

        // At Connect a garbled marker stops before anything moves too.
        using var fresh = new CoopScratch();
        Coop(Path.Combine(fresh.Modules, "Coop"), "Coop", "0.1.7+aaaaaaaa", "v0.1.7");
        Directory.CreateDirectory(Path.GetDirectoryName(fresh.Marker)!);
        File.WriteAllText(fresh.Marker, "null");
        Assert.StartsWith("The launcher could not read its record of the Coop folders it moved", PutIn(fresh).Problem);
        Assert.Equal("0.1.7+aaaaaaaa", BuildTxt(Path.Combine(fresh.Modules, "Coop")));
        Assert.Empty(Directory.GetDirectories(fresh.Game, "Coop-*"));
    }

    // Only our own copy comes out: a player's copy of the same RoT build in Modules\Coop has no id of the marker, so it stays.
    // A marker of before without an id still goes by the build. A Coop we park is Name-parked-by-rot-version, which the
    // TAOM and Burning Empires Restore parked Coop (Coop-kept-*) and the kept Coop restore never pick up.
    [Fact]
    public void Only_our_own_copy_comes_out_and_a_coop_we_park_has_its_own_name()
    {
        using var scratch = new CoopScratch();
        Coop(Path.Combine(scratch.Modules, "Coop"), "Coop", "0.1.7+aaaaaaaa", "v0.1.7");
        Assert.Null(PutIn(scratch).Problem);
        Assert.Equal(new[] { "Coop-parked-by-rot-0.1.7" }, Directory.GetDirectories(scratch.Game, "Coop-*").Select(Path.GetFileName));
        Assert.Empty(Directory.GetDirectories(scratch.Game, "Coop-kept-*"));
        Assert.Empty(RotCoopRestore.Kept(scratch.Game));
        Assert.True(File.Exists(Path.Combine(scratch.Modules, "Coop", RotCoopInstall.OwnFileName)));
        Assert.False(File.Exists(Path.Combine(scratch.Carried, RotCoopInstall.OwnFileName)));
        Assert.Equal("0.1.7+aaaaaaaa", BuildTxt(Path.Combine(scratch.Game, "Coop-parked-by-rot-0.1.7")));
        Assert.Equal(new[] { "Put Coop-parked-by-rot-0.1.7 back into Modules as Coop." }, TakeOut(scratch).Skip(1));

        // A launcher killed after the put in, and the player puts their own copy of the RoT build in Modules\Coop.
        Assert.Null(PutIn(scratch).Problem);
        var marker = File.ReadAllText(scratch.Marker);
        Directory.Move(Path.Combine(scratch.Modules, "Coop"), Path.Combine(scratch.Root, "ours"));
        Coop(Path.Combine(scratch.Modules, "Coop"), RotStack.CoopModuleId, Pinned);
        File.WriteAllText(Path.Combine(scratch.Modules, "Coop", "mine.txt"), "player");
        var result = RotCoopInstall.TakeOut(scratch.Marker, false, BuildTxt);
        Assert.DoesNotContain("Took Coop RoT build 2026-11-14 (01846e1) back out of the game's Modules folder.", result.Lines);
        Assert.Equal("player", File.ReadAllText(Path.Combine(scratch.Modules, "Coop", "mine.txt")));
        Assert.Equal("Your Coop is in " + Path.Combine(scratch.Game, "Coop-parked-by-rot-0.1.7") + ", because something else is in Modules\\Coop now. Move that out of Modules and the launcher puts yours back when it next starts.",
            result.Notice);

        // A marker of before (bb936c11, no id) takes a Coop of the RoT build out by its build.
        Directory.Delete(Path.Combine(scratch.Modules, "Coop"), recursive: true);
        Directory.Move(Path.Combine(scratch.Root, "ours"), Path.Combine(scratch.Modules, "Coop"));
        var old = System.Text.Json.JsonSerializer.Deserialize<RotCoopMarker>(marker)!;
        Assert.NotNull(old.Id);
        File.WriteAllText(scratch.Marker, System.Text.Json.JsonSerializer.Serialize(new RotCoopMarker(old.Ours, old.Parked)));
        Assert.Contains("Took Coop RoT build 2026-11-14 (01846e1) back out of the game's Modules folder.", TakeOut(scratch));
        Assert.Equal("0.1.7+aaaaaaaa", BuildTxt(Path.Combine(scratch.Modules, "Coop")));
        Assert.False(File.Exists(scratch.Marker));
    }

    // Live test runs 22 to 24: the game was killed and the launcher closed. The start grace could count a game that is gone
    // as running, and Coop.CrashReporter, which the killed game starts from Modules\Coop\bin, held files there for about
    // half a minute, so the move out failed and the Coop stayed in Modules.
    [Fact]
    public void The_close_takes_coop_out_once_the_started_game_is_gone_and_waits_for_the_coop_crash_reporter()
    {
        var fiveSeconds = TimeSpan.FromSeconds(5);
        Assert.Null(RotCoopInstall.WaitReason(gameProcess: false, coopProgram: null, startedExited: true, fiveSeconds, taleWorldsLauncher: false));
        Assert.Equal(RotCloseStep.TakeOut, RotCoopInstall.CloseStep(waiting: false, null));

        // A crash reporter from Modules\Coop\bin still running makes the close wait, game gone or not, and is named.
        Assert.Equal("Coop program Coop.CrashReporter", RotCoopInstall.WaitReason(false, coopProgram: "Coop.CrashReporter", true, TimeSpan.FromHours(1), false));
        Assert.Equal(RotCloseStep.Wait, RotCoopInstall.CloseStep(waiting: false, "Coop program Coop.CrashReporter"));

        // Within the grace with no process yet, or a started process that can not be told, the game still counts as starting.
        Assert.Equal("starting grace", RotCoopInstall.WaitReason(false, null, startedExited: false, fiveSeconds, false));
        Assert.Equal("starting grace", RotCoopInstall.WaitReason(false, null, startedExited: null, fiveSeconds, false));
        Assert.Null(RotCoopInstall.WaitReason(false, null, null, RotCoopInstall.StartGrace, false));
        Assert.Equal("game process", RotCoopInstall.WaitReason(true, "Coop.CrashReporter", true, TimeSpan.FromHours(1), false));
        Assert.Equal("TaleWorlds launcher", RotCoopInstall.WaitReason(false, null, true, fiveSeconds, true));

        // The first close waits; a second close while waiting takes Coop out once the game is gone and otherwise leaves it.
        Assert.Equal(RotCloseStep.Wait, RotCoopInstall.CloseStep(waiting: false, "game process"));
        Assert.Equal(RotCloseStep.TakeOut, RotCoopInstall.CloseStep(waiting: true, null));
        Assert.Equal(RotCloseStep.LeaveForNextStart, RotCoopInstall.CloseStep(waiting: true, "game process"));
    }
}
