using System.Text;
using System.Text.RegularExpressions;
using BannerlordCoopLauncher.Core.Keep;
using BannerlordCoopLauncher.Core.Updates;
using TaomCoop.Core;
using static BurningEmpires.Core.Tests.BeServerFixtures;

namespace BannerlordCoopLauncher.Tests;

// The TAOM Server Tool's Coop 0.1.6 server a host chose (KitCoopServer): Steam's Workshop has Coop 0.1.7 since 2026-10-30
// and TAOM.CoopCompat 0.3.25 needs Coop 0.1.6, so Prepare server copies from a downloaded Coop 0.1.6 dedicated server and
// Start server repairs from it. The tests' own pins stand in for the released Coop 0.1.6 DLLs the host kit checks.
public sealed class KitCoopServerTests
{
    private static readonly string[] PinnedNames = TaomStack.CoopServerPins.Keys.ToArray();

    private static string Released(string name) => "released " + name;

    private static readonly IReadOnlyDictionary<string, string> Pins = PinnedNames.ToDictionary(
        name => name, name => Sha256Files.Hash(new MemoryStream(Encoding.UTF8.GetBytes(Released(name)))), StringComparer.OrdinalIgnoreCase);

    // A dedicated server folder with BannerlordCoopServer.exe and a Coop module of the given version. Every pinned DLL is the
    // released stand in, unless changed names it with other text (null leaves it out).
    private static string Server(string folder, string version, Dictionary<string, string?>? changed = null, string? coreBackup = null)
    {
        Write(Path.Combine(folder, KitCoopServer.ExeName), "exe");
        Module(Path.Combine(folder, "engine", "Modules"), "Coop", "Coop", version);
        foreach (var name in PinnedNames)
        {
            var text = changed != null && changed.TryGetValue(name, out var other) ? other : Released(name);
            if (text != null)
                Write(Path.Combine(KitCoopServer.CoopBin(folder), name), text);
        }

        if (coreBackup != null)
            Write(KitCoopServer.CoopCoreDll(folder) + ".pre-password.bak", coreBackup);
        return folder;
    }

    private static Dictionary<string, string?> Changed(string name, string? text) => new() { [name] = text };

    [Fact]
    public void The_folder_resolver_takes_the_root_a_DedicatedServer_folder_or_one_extra_folder()
    {
        var root = NewRoot("kit-resolve");
        try
        {
            var github = Server(Path.Combine(root, "github"), "v0.1.6");
            Assert.Equal(github, KitCoopServer.Resolve(github));

            var nexus = Path.Combine(root, "nexus");
            var nexusServer = Server(Path.Combine(nexus, "DedicatedServer"), "v0.1.6");
            Assert.Equal(nexusServer, KitCoopServer.Resolve(nexus));

            // 7-Zip's Extract to "name\" adds one more folder around the archive's own.
            var extra = Path.Combine(root, "extra");
            var extraServer = Server(Path.Combine(extra, "BannerlordCoop-DedicatedServer-Win64-v0.1.6"), "v0.1.6");
            Assert.Equal(extraServer, KitCoopServer.Resolve(extra));

            var empty = Path.Combine(root, "empty");
            Directory.CreateDirectory(Path.Combine(empty, "a"));
            Directory.CreateDirectory(Path.Combine(empty, "b"));
            Assert.Null(KitCoopServer.Resolve(empty));
            Assert.Null(KitCoopServer.Resolve(null));
            Assert.Equal(KitCoopServer.NotAServer, KitCoopServer.Problem(empty, Pins));

            // A saved folder that was deleted or moved says so.
            var gone = Path.Combine(root, "moved away");
            Assert.Null(KitCoopServer.Resolve(gone));
            Assert.Equal(KitCoopServer.Gone(gone), KitCoopServer.Problem(gone, Pins));
            Assert.Equal(KitCoopServer.WithWhereToGet(KitCoopServer.Gone(gone)), KitCoopServer.Pick(null, gone, Pins).Problem);
        }
        finally
        {
            Delete(root);
        }
    }

    // A host who unzipped the Coop 0.1.6 client zip and picked it read the old NotAServer line as nothing happened (2026-11-07).
    [Fact]
    public void A_picked_coop_client_folder_says_it_is_the_client_not_the_server()
    {
        var root = NewRoot("kit-client");
        try
        {
            static string Client(string module)
            {
                Directory.CreateDirectory(Path.Combine(module, "bin", "Win64_Shipping_Client"));
                File.WriteAllText(Path.Combine(module, "SubModule.xml"), "<Module><Name value=\"Coop\"/><Id value=\"Coop\"/><Version value=\"v0.1.6\"/></Module>");
                return module;
            }

            // BannerlordCoop-v0.1.6.zip unzipped into a folder (it holds Coop), the Coop folder itself, and one more 7-Zip folder around it.
            var unzipped = Path.Combine(root, "unzipped");
            Client(Path.Combine(unzipped, "Coop"));
            Assert.Equal(KitCoopServer.ClientNotServer, KitCoopServer.Problem(unzipped, Pins));
            Assert.Equal(KitCoopServer.ClientNotServer, KitCoopServer.Problem(Path.Combine(unzipped, "Coop"), Pins));
            var wrapped = Path.Combine(root, "wrapped");
            Client(Path.Combine(wrapped, "BannerlordCoop-v0.1.6", "Coop"));
            Assert.Equal(KitCoopServer.ClientNotServer, KitCoopServer.Problem(wrapped, Pins));
            Assert.Equal(KitCoopServer.ClientNotServer, KitCoopServer.ChooseProblem(unzipped, null, null, Pins));

            // A module that is not Coop stays NotAServer.
            var other = Path.Combine(root, "other");
            Directory.CreateDirectory(Path.Combine(other, "bin", "Win64_Shipping_Client"));
            File.WriteAllText(Path.Combine(other, "SubModule.xml"), "<Module><Id value=\"RBM\"/><Version value=\"v1.0.0\"/></Module>");
            Assert.Equal(KitCoopServer.NotAServer, KitCoopServer.Problem(other, Pins));
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void The_hash_gate_checks_every_coop_dll_the_kit_pins_and_never_writes_to_the_folder()
    {
        var root = NewRoot("kit-gate");
        try
        {
            var pinned = Server(Path.Combine(root, "pinned"), "v0.1.6");
            Assert.Equal(KitCoopServerCheck.Pinned, KitCoopServer.Check(pinned, Pins));
            Assert.Null(KitCoopServer.Problem(pinned, Pins));

            var newer = Server(Path.Combine(root, "newer"), "v0.1.7", Changed("Coop.Core.dll", "coop 0.1.7"));
            Assert.Equal(KitCoopServerCheck.OtherVersion, KitCoopServer.Check(newer, Pins));
            Assert.StartsWith("This folder has Coop 0.1.7. TAOM.CoopCompat 0.3.25 needs the official Coop 0.1.6 dedicated server.", KitCoopServer.Problem(newer, Pins));

            // Steam's update to 0.1.7 keeps an old .bak, which must not make a 0.1.7 folder look like an edited 0.1.6 one.
            var newerWithBackup = Server(Path.Combine(root, "newer-bak"), "v0.1.7", Changed("Coop.Core.dll", "coop 0.1.7"), Released("Coop.Core.dll"));
            Assert.Equal(KitCoopServerCheck.OtherVersion, KitCoopServer.Check(newerWithBackup, Pins));

            var otherCore = Server(Path.Combine(root, "core"), "v0.1.6", Changed("Coop.Core.dll", "another 0.1.6 build"));
            Assert.Equal(KitCoopServer.DifferentBuild, KitCoopServer.Problem(otherCore, Pins));

            // A pinned Coop.Core.dll with another Coop DLL the kit pins is refused too, so is a missing one.
            var otherSteam = Server(Path.Combine(root, "steam"), "v0.1.6", Changed("Coop.Steam.dll", "another Coop.Steam.dll"));
            Assert.Equal(KitCoopServerCheck.DifferentBuild, KitCoopServer.Check(otherSteam, Pins));
            Assert.Equal(KitCoopServer.DifferentBuild, KitCoopServer.Problem(otherSteam, Pins));
            var noMissions = Server(Path.Combine(root, "missions"), "v0.1.6", Changed("Missions.dll", null));
            Assert.Equal(KitCoopServerCheck.DifferentBuild, KitCoopServer.Check(noMissions, Pins));

            var edited = Server(Path.Combine(root, "edited"), "v0.1.6", Changed("Coop.Core.dll", "password edit"), Released("Coop.Core.dll"));
            Assert.Equal(KitCoopServerCheck.PasswordEdited, KitCoopServer.Check(edited, Pins));
            Assert.Equal(KitCoopServer.PasswordEdited, KitCoopServer.Problem(edited, Pins));
            Assert.Equal("password edit", File.ReadAllText(KitCoopServer.CoopCoreDll(edited)));
            Assert.False(File.Exists(KitCoopServer.CoopCoreDll(edited) + ".coodsz-password"));

            Assert.Equal(KitCoopServerCheck.NoServer, KitCoopServer.Check(Path.Combine(root, "missing"), Pins));
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void A_locked_file_gives_its_reason_instead_of_throwing()
    {
        var root = NewRoot("kit-locked");
        try
        {
            var server = Server(Path.Combine(root, "locked"), "v0.1.6");
            var workshop = Server(Path.Combine(root, "w017"), "v0.1.7", Changed("Coop.Core.dll", "coop 0.1.7"));
            using (new FileStream(KitCoopServer.CoopCoreDll(server), FileMode.Open, FileAccess.Read, FileShare.None))
            {
                Assert.StartsWith("The tool could not read the Coop 0.1.6 server folder. ", KitCoopServer.Problem(server, Pins));
                Assert.StartsWith("The tool could not read the Coop 0.1.6 server folder. ", KitCoopServer.Pick(workshop, server, Pins).Problem);
                Assert.Equal(workshop, KitCoopServer.RepairServer(workshop, server, Pins));
                Assert.Null(KitCoopServer.RepairServer(null, server, Pins));
            }
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void A_pinned_workshop_server_stays_first_and_the_chosen_one_is_used_while_steam_has_coop_017()
    {
        var root = NewRoot("kit-pick");
        try
        {
            var workshop016 = Server(Path.Combine(root, "w016", "DedicatedServer"), "v0.1.6");
            var workshop017 = Server(Path.Combine(root, "w017", "DedicatedServer"), "v0.1.7", Changed("Coop.Core.dll", "coop 0.1.7"));
            var chosenFolder = Path.Combine(root, "download");
            var chosen = Server(Path.Combine(chosenFolder, "DedicatedServer"), "v0.1.6");
            var wrong = Server(Path.Combine(root, "wrong"), "v0.1.7", Changed("Coop.Core.dll", "coop 0.1.7"));

            Assert.Equal(new KitCoopServerPick(workshop016, false, null), KitCoopServer.Pick(workshop016, chosenFolder, Pins));
            Assert.Equal(new KitCoopServerPick(workshop016, false, null), KitCoopServer.Pick(workshop016, null, Pins));
            Assert.Equal(new KitCoopServerPick(chosen, true, null), KitCoopServer.Pick(workshop017, chosenFolder, Pins));
            Assert.Equal(new KitCoopServerPick(chosen, true, null), KitCoopServer.Pick(null, chosenFolder, Pins));

            // Neither: the Workshop's newer Coop line, then where to get Coop 0.1.6 with each link on its own line.
            var refused = KitCoopServer.Pick(workshop017, "", Pins);
            Assert.Null(refused.Server);
            Assert.Equal(CoopStaging.WrongWorkshopCoop("Coop", "v0.1.7", server: true) + "\n" + KitCoopServer.WhereToGet + "\n" + KitCoopServer.GitHubRelease + "\n" + KitCoopServer.NexusFiles, refused.Problem);
            Assert.DoesNotContain("moddb", refused.Problem!, StringComparison.OrdinalIgnoreCase);

            var wrongChoice = KitCoopServer.Pick(workshop017, wrong, Pins);
            Assert.Null(wrongChoice.Server);
            Assert.True(wrongChoice.Chosen);
            Assert.StartsWith(KitCoopServer.OtherVersion("v0.1.7") + "\n" + KitCoopServer.WhereToGet, wrongChoice.Problem);

            // No Workshop server and nothing chosen: Prepare's own missing server messages, as before.
            Assert.Equal(new KitCoopServerPick(null, false, null), KitCoopServer.Pick(null, null, Pins));

            // Start repairs from a pinned Workshop server first, then from the chosen server only when it is pinned.
            Assert.Equal(workshop016, KitCoopServer.RepairServer(workshop016, chosenFolder, Pins));
            Assert.Equal(chosen, KitCoopServer.RepairServer(workshop017, chosenFolder, Pins));
            Assert.Equal(chosen, KitCoopServer.RepairServer(null, chosenFolder, Pins));
            Assert.Equal(workshop017, KitCoopServer.RepairServer(workshop017, wrong, Pins));
            Assert.Equal(workshop016, KitCoopServer.RepairServer(workshop016, null, Pins));
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void Start_repair_from_a_chosen_server_copies_only_the_pinned_coop_dlls()
    {
        var root = NewRoot("kit-repair");
        try
        {
            var chosen = Server(Path.Combine(root, "download"), "v0.1.6");
            Write(Path.Combine(KitCoopServer.CoopBin(chosen), "Extra.dll"), "not pinned");

            var server = Path.Combine(root, "server", "engine", "Modules", "Coop");
            var serverBin = Path.Combine(server, "bin", "Win64_Shipping_Server");
            Write(Path.Combine(serverBin, "Coop.Core.dll"), "broken");

            var note = CoopStaging.EnsurePinnedServerBin(server, KitCoopServer.CoopModule(chosen), chosenServer: true, Pins);
            Assert.Equal("The dedicated server Coop files were put back from your Coop 0.1.6 server folder. The game's client files were not used.", note);
            foreach (var name in PinnedNames)
                Assert.Equal(Released(name), File.ReadAllText(Path.Combine(serverBin, name)));
            Assert.False(File.Exists(Path.Combine(serverBin, "Extra.dll")));

            // The Workshop server copy works as before.
            Write(Path.Combine(serverBin, "Coop.Core.dll"), "broken");
            CoopStaging.EnsurePinnedServerBin(server, KitCoopServer.CoopModule(chosen), chosenServer: false, Pins);
            Assert.True(File.Exists(Path.Combine(serverBin, "Extra.dll")));
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void Start_refuses_a_newer_workshop_coop_with_where_to_get_coop_016()
    {
        var root = NewRoot("kit-start");
        try
        {
            var serverCoop = Module(Path.Combine(root, "server", "engine", "Modules"), "Coop", "Coop", "v0.1.6");
            var serverDll = Write(Path.Combine(serverCoop, "bin", "Win64_Shipping_Server", "Coop.Core.dll"), "not the pinned file");
            var workshopCoop = Module(Path.Combine(root, "workshop", "DedicatedServer", "engine", "Modules"), "Coop", "Coop", "v0.1.7");
            Write(Path.Combine(workshopCoop, "bin", "Win64_Shipping_Server", "Coop.Core.dll"), "coop 0.1.7");

            var start = Assert.Throws<InvalidOperationException>(() => CoopStaging.EnsurePinnedServerBin(serverCoop, workshopCoop));
            Assert.Equal(KitCoopServer.WithWhereToGet(CoopStaging.WrongWorkshopCoop("Coop", "v0.1.7", server: true)), start.Message);
            Assert.Equal("not the pinned file", File.ReadAllText(serverDll));
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void The_kit_reads_a_chosen_server_through_a_junction_that_is_made_again_each_time()
    {
        var root = NewRoot("kit-link");
        var linkBase = Path.Combine(root, "Local App Data", "TAOM-Coop", "Coop016Server");
        try
        {
            var first = Server(Path.Combine(root, "my first download"), "v0.1.6");
            var second = Server(Path.Combine(root, "my second download"), "v0.1.6", Changed("Common.dll", "second copy"));

            // Prepare-Host.ps1's Assert-WorkshopServer takes only this shape of path.
            Assert.Matches(new Regex(@"(?i)\\steamapps\\workshop\\content\\261550\\" + TaomStack.CoopWorkshopId + @"\\DedicatedServer$"), KitCoopServer.KitLinkPath(linkBase));

            var link = KitCoopServer.LinkForKit(first, linkBase);
            Assert.Equal(KitCoopServer.KitLinkPath(linkBase), link);
            Assert.True(new DirectoryInfo(link).Attributes.HasFlag(FileAttributes.ReparsePoint));
            Assert.Equal(Released("Common.dll"), File.ReadAllText(Path.Combine(KitCoopServer.CoopBin(link), "Common.dll")));

            // A stale junction left at that path (a closed tool) points at the new folder after the next link.
            link = KitCoopServer.LinkForKit(second, linkBase);
            Assert.Equal("second copy", File.ReadAllText(Path.Combine(KitCoopServer.CoopBin(link), "Common.dll")));

            // A junction to a folder that is gone is replaced too.
            var gone = Server(Path.Combine(root, "deleted later"), "v0.1.6");
            KitCoopServer.LinkForKit(gone, linkBase);
            Delete(gone);
            link = KitCoopServer.LinkForKit(first, linkBase);
            Assert.Equal(Released("Common.dll"), File.ReadAllText(Path.Combine(KitCoopServer.CoopBin(link), "Common.dll")));

            KitCoopServer.RemoveLink(link);
            Assert.False(Directory.Exists(link));
            Assert.True(File.Exists(Path.Combine(first, KitCoopServer.ExeName)));
            Assert.Equal("second copy", File.ReadAllText(Path.Combine(KitCoopServer.CoopBin(second), "Common.dll")));

            // A real folder at that path is never deleted.
            Write(Path.Combine(link, "keep.txt"), "keep");
            KitCoopServer.RemoveLink(link);
            Assert.True(File.Exists(Path.Combine(link, "keep.txt")));
            Assert.Throws<InvalidOperationException>(() => KitCoopServer.LinkForKit(first, linkBase));
        }
        finally
        {
            KitCoopServer.RemoveLink(KitCoopServer.KitLinkPath(linkBase));
            Delete(root);
        }
    }

    [Fact]
    public void Prepare_from_a_chosen_server_hands_the_kit_the_link_and_removes_it_after()
    {
        var root = NewRoot("kit-prepare");
        var linkBase = Path.Combine(root, "TAOM-Coop", "Coop016Server");
        try
        {
            var chosen = Server(Path.Combine(root, "Coop 016"), "v0.1.6");
            var serverRoot = Path.Combine(root, "TAOM-Coop-Host", "Server");
            var dataRoot = Path.Combine(root, "TAOM-Coop-Host", "Data");
            string? seen = null;
            KitCoopServer.PrepareFromChosen(chosen, linkBase, serverRoot, dataRoot, link =>
            {
                seen = link;
                Assert.True(File.Exists(KitCoopServer.CoopCoreDll(link)));
            });
            Assert.Equal(KitCoopServer.KitLinkPath(linkBase), seen);
            Assert.False(Directory.Exists(seen));

            // The link goes also when the kit fails.
            Assert.Throws<InvalidDataException>(() => KitCoopServer.PrepareFromChosen(chosen, linkBase, serverRoot, dataRoot, _ => throw new InvalidDataException()));
            Assert.False(Directory.Exists(KitCoopServer.KitLinkPath(linkBase)));
            Assert.True(File.Exists(Path.Combine(chosen, KitCoopServer.ExeName)));
        }
        finally
        {
            KitCoopServer.RemoveLink(KitCoopServer.KitLinkPath(linkBase));
            Delete(root);
        }
    }

    [Theory]
    [InlineData("inside", true)]
    [InlineData("same", true)]
    [InlineData("parent", true)]
    [InlineData("data inside", true)]
    [InlineData("sibling", false)]
    public void Prepare_refuses_a_server_or_data_folder_that_overlaps_the_chosen_server(string where, bool refused)
    {
        var root = NewRoot("kit-overlap");
        var linkBase = Path.Combine(root, "TAOM-Coop", "Coop016Server");
        try
        {
            var chosen = Server(Path.Combine(root, "Coop016"), "v0.1.6");
            var data = Path.Combine(root, "Data");
            var server = Path.Combine(root, "Server");
            switch (where)
            {
                case "inside": server = Path.Combine(chosen, "TAOM"); break;
                case "same": server = chosen.ToUpperInvariant() + "\\"; break;
                case "parent": server = root; break;
                case "data inside": data = Path.Combine(chosen, "engine", "Data"); break;
                case "sibling": server = Path.Combine(root, "Coop016-host"); break;
            }

            var ran = false;
            if (refused)
            {
                var error = Assert.Throws<InvalidOperationException>(() => KitCoopServer.PrepareFromChosen(chosen, linkBase, server, data, _ => ran = true));
                Assert.Equal(KitCoopServer.OverlapProblem(chosen, server, data), error.Message);
                Assert.StartsWith("Your Coop 0.1.6 server folder " + chosen + " and the TAOM host folder ", error.Message);
                Assert.False(ran);
                Assert.False(Directory.Exists(KitCoopServer.KitLinkPath(linkBase)));
            }
            else
            {
                Assert.Null(KitCoopServer.OverlapProblem(chosen, server, data));
                KitCoopServer.PrepareFromChosen(chosen, linkBase, server, data, _ => ran = true);
                Assert.True(ran);
            }
        }
        finally
        {
            KitCoopServer.RemoveLink(KitCoopServer.KitLinkPath(linkBase));
            Delete(root);
        }
    }

    [Fact]
    public void A_folder_that_names_another_coop_is_that_coop_even_when_its_dlls_match()
    {
        var root = NewRoot("kit-version");
        try
        {
            var renamed = Server(Path.Combine(root, "renamed"), "v0.1.7");
            Assert.Equal(KitCoopServerCheck.OtherVersion, KitCoopServer.Check(renamed, Pins));
            Assert.Equal(KitCoopServer.OtherVersion("v0.1.7"), KitCoopServer.Problem(renamed, Pins));
            Assert.Equal(KitCoopServerCheck.Pinned, KitCoopServer.Check(Server(Path.Combine(root, "plain"), "v0.1.6"), Pins));
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void Start_repair_refuses_a_half_updated_workshop_and_copies_pinned_names_only_with_their_hash()
    {
        var root = NewRoot("kit-half");
        try
        {
            var server = Path.Combine(root, "server", "engine", "Modules", "Coop");
            var serverBin = Path.Combine(server, "bin", "Win64_Shipping_Server");
            var serverDll = Write(Path.Combine(serverBin, "Coop.Core.dll"), "broken");

            // Steam part way through Coop 0.1.7: the SubModule.xml and Common.dll are new, Coop.Core.dll is still 0.1.6's.
            var half = Server(Path.Combine(root, "half", "DedicatedServer"), "v0.1.7", Changed("Common.dll", "common 0.1.7"));
            var refused = Assert.Throws<InvalidOperationException>(() => CoopStaging.EnsurePinnedServerBin(server, KitCoopServer.CoopModule(half), chosenServer: false, Pins));
            Assert.Equal(KitCoopServer.WithWhereToGet(CoopStaging.WrongWorkshopCoop("Coop", "v0.1.7", server: true)), refused.Message);
            Assert.Equal("broken", File.ReadAllText(serverDll));
            Assert.False(File.Exists(Path.Combine(serverBin, "Common.dll")));

            // A 0.1.6 Workshop with one pinned name of another build: the matching files and the other DLLs come back, that one never does.
            var workshop = Server(Path.Combine(root, "w016", "DedicatedServer"), "v0.1.6", Changed("Common.dll", "another Common.dll"));
            Write(Path.Combine(KitCoopServer.CoopBin(workshop), "Extra.dll"), "not pinned");
            CoopStaging.EnsurePinnedServerBin(server, KitCoopServer.CoopModule(workshop), chosenServer: false, Pins);
            Assert.Equal(Released("Coop.Core.dll"), File.ReadAllText(serverDll));
            Assert.False(File.Exists(Path.Combine(serverBin, "Common.dll")));
            Assert.True(File.Exists(Path.Combine(serverBin, "Extra.dll")));
        }
        finally
        {
            Delete(root);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Prepare_refuses_a_chosen_server_and_link_folder_inside_one_another(bool linkInsideChosen)
    {
        var root = NewRoot("kit-link-overlap");
        var chosen = linkInsideChosen ? Path.Combine(root, "Coop016") : Path.Combine(root, "TAOM-Coop", "Coop016Server", "my server");
        var linkBase = linkInsideChosen ? Path.Combine(chosen, "TAOM-Coop", "Coop016Server") : Path.Combine(root, "TAOM-Coop", "Coop016Server");
        try
        {
            Server(chosen, "v0.1.6");
            var ran = false;
            var error = Assert.Throws<InvalidOperationException>(() => KitCoopServer.PrepareFromChosen(chosen, linkBase,
                Path.Combine(root, "Host", "Server"), Path.Combine(root, "Host", "Data"), _ => ran = true));
            Assert.Equal(KitCoopServer.LinkOverlap(chosen, linkBase), error.Message);
            Assert.False(ran);
            Assert.False(Directory.Exists(KitCoopServer.KitLinkPath(linkBase)));
        }
        finally
        {
            KitCoopServer.RemoveLink(KitCoopServer.KitLinkPath(linkBase));
            Delete(root);
        }
    }

    [Fact]
    public void A_percent_sign_in_the_chosen_or_link_path_is_refused_before_any_link()
    {
        var root = NewRoot("kit-percent");
        var linkBase = Path.Combine(root, "TAOM-Coop", "Coop016Server");
        try
        {
            // cmd would turn %TEMP% into another folder, so mklink would link there.
            var chosen = Server(Path.Combine(root, "Coop%TEMP%016"), "v0.1.6");
            var host = Path.Combine(root, "Host");
            Assert.Equal(KitCoopServer.PercentInPath(chosen), KitCoopServer.Problem(chosen, Pins));
            Assert.Equal(KitCoopServer.PercentInPath(chosen), KitCoopServer.ChooseProblem(chosen, Path.Combine(host, "Server"), Path.Combine(host, "Data"), Pins));
            Assert.StartsWith(KitCoopServer.PercentInPath(chosen) + "\n", KitCoopServer.Pick(null, chosen, Pins).Problem);

            var ran = false;
            var refused = Assert.Throws<InvalidOperationException>(() => KitCoopServer.PrepareFromChosen(chosen, linkBase, Path.Combine(host, "Server"), Path.Combine(host, "Data"), _ => ran = true));
            Assert.Equal(KitCoopServer.PercentInPath(chosen), refused.Message);
            Assert.False(ran);
            Assert.False(Directory.Exists(KitCoopServer.KitLinkPath(linkBase)));

            var plain = Server(Path.Combine(root, "download"), "v0.1.6");
            var oddBase = Path.Combine(root, "100%", "Coop016Server");
            var link = Assert.Throws<InvalidOperationException>(() => KitCoopServer.PrepareFromChosen(plain, oddBase, Path.Combine(host, "Server"), Path.Combine(host, "Data"), _ => ran = true));
            Assert.Equal(KitCoopServer.PercentInLinkPath(KitCoopServer.KitLinkPath(oddBase)), link.Message);
            Assert.False(ran);
            Assert.False(Directory.Exists(KitCoopServer.KitLinkPath(oddBase)));
        }
        finally
        {
            KitCoopServer.RemoveLink(KitCoopServer.KitLinkPath(linkBase));
            Delete(root);
        }
    }

    [Fact]
    public void Choose_and_Prepare_agree_on_a_password_edited_workshop_016()
    {
        var root = NewRoot("kit-agree");
        try
        {
            var edited = Server(Path.Combine(root, "w016", "DedicatedServer"), "v0.1.6", Changed("Coop.Core.dll", "password edit"), Released("Coop.Core.dll"));
            var chosen = Server(Path.Combine(root, "download"), "v0.1.6");
            Assert.Equal(new KitCoopServerPick(edited, false, null), KitCoopServer.Pick(edited, chosen, Pins));
            Assert.True(KitCoopServer.WorkshopHasKitCoop(edited, Pins));

            var newer = Server(Path.Combine(root, "w017", "DedicatedServer"), "v0.1.7", Changed("Coop.Core.dll", "coop 0.1.7"));
            Assert.False(KitCoopServer.WorkshopHasKitCoop(newer, Pins));
            Assert.True(KitCoopServer.Pick(newer, chosen, Pins).Chosen);
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void An_empty_folder_that_stays_after_the_link_went_says_nothing()
    {
        var root = NewRoot("kit-parent-stuck");
        var linkBase = Path.Combine(root, "TAOM-Coop", "Coop016Server");
        var parent = Path.GetDirectoryName(KitCoopServer.KitLinkPath(linkBase))!;
        try
        {
            var chosen = Server(Path.Combine(root, "download"), "v0.1.6");
            var host = Path.Combine(root, "Host");
            var lines = new List<string>();
            // The link goes, then its empty parent is made read only, so deleting that one fails.
            void RemoveThenLock(string link)
            {
                KitCoopServer.RemoveLink(link);
                File.SetAttributes(parent, FileAttributes.Directory | FileAttributes.ReadOnly);
            }

            KitCoopServer.PrepareFromChosen(chosen, linkBase, Path.Combine(host, "Server"), Path.Combine(host, "Data"), _ => { }, lines.Add, RemoveThenLock);
            Assert.Empty(lines);
            Assert.False(Directory.Exists(KitCoopServer.KitLinkPath(linkBase)));
            Assert.True(Directory.Exists(parent));
        }
        finally
        {
            if (Directory.Exists(parent))
                File.SetAttributes(parent, FileAttributes.Directory);
            Delete(root);
        }
    }

    [Fact]
    public void A_link_that_cannot_be_removed_only_writes_a_line_and_keeps_how_prepare_went()
    {
        var root = NewRoot("kit-cleanup");
        var linkBase = Path.Combine(root, "TAOM-Coop", "Coop016Server");
        try
        {
            var chosen = Server(Path.Combine(root, "download"), "v0.1.6");
            var host = Path.Combine(root, "Host");
            var lines = new List<string>();
            void Stuck(string link) => throw new IOException("in use");

            // A prepare that worked stays worked.
            var ran = false;
            KitCoopServer.PrepareFromChosen(chosen, linkBase, Path.Combine(host, "Server"), Path.Combine(host, "Data"), _ => ran = true, lines.Add, Stuck);
            Assert.True(ran);
            Assert.Equal([KitCoopServer.LinkNotRemoved(KitCoopServer.KitLinkPath(linkBase))], lines);

            // A prepare that failed keeps its own error, and the next link is made over the stuck one.
            Assert.Throws<InvalidDataException>(() => KitCoopServer.PrepareFromChosen(chosen, linkBase, Path.Combine(host, "Server"), Path.Combine(host, "Data"),
                _ => throw new InvalidDataException(), lines.Add, Stuck));
            Assert.Equal(2, lines.Count);
        }
        finally
        {
            KitCoopServer.RemoveLink(KitCoopServer.KitLinkPath(linkBase));
            Delete(root);
        }
    }

    [Fact]
    public void Prepare_leaves_no_empty_steamapps_folders_and_never_deletes_one_with_files()
    {
        var root = NewRoot("kit-parents");
        var linkBase = Path.Combine(root, "TAOM-Coop", "Coop016Server");
        try
        {
            var chosen = Server(Path.Combine(root, "download"), "v0.1.6");
            var host = Path.Combine(root, "Host");
            KitCoopServer.PrepareFromChosen(chosen, linkBase, Path.Combine(host, "Server"), Path.Combine(host, "Data"), _ => { });
            Assert.True(Directory.Exists(linkBase));
            Assert.False(Directory.Exists(Path.Combine(linkBase, "steamapps")));

            // Something else in one of those folders stops the clean up there.
            var kept = Write(Path.Combine(linkBase, "steamapps", "workshop", "content", "keep.txt"), "keep");
            KitCoopServer.PrepareFromChosen(chosen, linkBase, Path.Combine(host, "Server"), Path.Combine(host, "Data"), _ => { });
            Assert.True(File.Exists(kept));
            Assert.False(Directory.Exists(Path.Combine(linkBase, "steamapps", "workshop", "content", "261550")));
            Assert.True(File.Exists(Path.Combine(chosen, KitCoopServer.ExeName)));
        }
        finally
        {
            KitCoopServer.RemoveLink(KitCoopServer.KitLinkPath(linkBase));
            Delete(root);
        }
    }

    [Fact]
    public void The_texts_say_joining_works_with_your_own_coop_016_and_hosting_needs_its_server()
    {
        var v129 = Assert.Single(WhatsNew.Taom, entry => entry.Version == "v1.2.9");
        Assert.Contains(v129.Lines, line => line.Contains("Players can still join with a Coop 0.1.6 copy of their own in Modules")
            && line.Contains("Hosting a TAOM server needs a Coop 0.1.6 dedicated server until then."));
        var client = GuideLibrary.For(GuideSet.TaomClient).SelectMany(GuideLibrary.Lines).ToList();
        var server = GuideLibrary.For(GuideSet.TaomServer).SelectMany(GuideLibrary.Lines).ToList();
        Assert.DoesNotContain(client.Concat(server), line => line.Contains("TAOM can use it"));
        Assert.Contains(client, line => line.Contains("Get BannerlordCoop-v0.1.6.zip from Coop's GitHub v0.1.6 release"));
        Assert.Contains(server, line => line.Contains(KitCoopServer.ChooseButton));

        foreach (var doc in new[] { "README.txt", "Coodsz-TAOM-Instructions.txt" })
        {
            var lines = SourceFiles.Read(doc).Replace("\r", "").Split('\n');
            Assert.DoesNotContain(lines, line => line.Contains("TAOM can use it"));
            Assert.Contains(KitCoopServer.GitHubRelease, lines);
            Assert.Contains(KitCoopServer.NexusFiles, lines);
        }
    }

    // A directory junction made like the tool makes its own; false when this PC won't make one, and the test then has nothing to check.
    private static bool Junction(string link, string target)
    {
        var start = new System.Diagnostics.ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"), "/d /c mklink /J \"" + link + "\" \"" + target + "\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        using var process = System.Diagnostics.Process.Start(start)!;
        process.StandardOutput.ReadToEnd();
        process.StandardError.ReadToEnd();
        process.WaitForExit();
        return new DirectoryInfo(link).LinkTarget != null;
    }

    [Fact]
    public void Choose_refuses_the_prepared_TAOM_server_also_through_a_junction()
    {
        var root = NewRoot("kit-choose");
        try
        {
            // A prepared TAOM server holds BannerlordCoopServer.exe and the released Coop DLLs, so the hash gate alone takes it.
            var serverRoot = Server(Path.Combine(root, "TAOM-Coop-Host", "Server"), "v0.1.6");
            var dataRoot = Path.Combine(root, "TAOM-Coop-Host", "Data");
            var download = Server(Path.Combine(root, "download"), "v0.1.6");
            Assert.Null(KitCoopServer.Problem(serverRoot, Pins));

            Assert.Equal(KitCoopServer.ChoseHostFolder(serverRoot, serverRoot), KitCoopServer.ChooseProblem(serverRoot, serverRoot, dataRoot, Pins));
            Assert.Equal(KitCoopServer.ChoseHostFolder(serverRoot, serverRoot.ToUpperInvariant() + "\\"), KitCoopServer.ChooseProblem(serverRoot, serverRoot.ToUpperInvariant() + "\\", dataRoot, Pins));
            Assert.Null(KitCoopServer.ChooseProblem(download, serverRoot, dataRoot, Pins));
            Assert.Equal(KitCoopServer.Gone(Path.Combine(root, "gone")), KitCoopServer.ChooseProblem(Path.Combine(root, "gone"), serverRoot, dataRoot, Pins));

            var link = Path.Combine(root, "looks like a download");
            if (!Junction(link, serverRoot))
                return;
            Assert.Null(KitCoopServer.Problem(link, Pins));
            Assert.Equal(KitCoopServer.ChoseHostFolder(link, serverRoot), KitCoopServer.ChooseProblem(link, serverRoot, dataRoot, Pins));
            KitCoopServer.RemoveLink(link);
        }
        finally
        {
            KitCoopServer.RemoveLink(Path.Combine(root, "looks like a download"));
            Delete(root);
        }
    }

    [Fact]
    public void Prepare_refuses_a_server_folder_inside_the_chosen_one_reached_through_a_junction()
    {
        var root = NewRoot("kit-real");
        var linkBase = Path.Combine(root, "TAOM-Coop", "Coop016Server");
        var chosen = Path.Combine(root, "chosen link");
        try
        {
            var download = Server(Path.Combine(root, "download"), "v0.1.6");
            if (!Junction(chosen, download))
                return;
            var serverRoot = Path.Combine(download, "TAOM", "Server");
            Assert.Equal(Path.TrimEndingDirectorySeparator(download), KitCoopServer.RealPath(chosen + "\\"), StringComparer.OrdinalIgnoreCase);
            var ran = false;
            var error = Assert.Throws<InvalidOperationException>(() => KitCoopServer.PrepareFromChosen(chosen, linkBase, serverRoot, Path.Combine(root, "Data"), _ => ran = true));
            Assert.Equal(KitCoopServer.Overlap(chosen, serverRoot), error.Message);
            Assert.False(ran);
            Assert.True(File.Exists(Path.Combine(download, KitCoopServer.ExeName)));
        }
        finally
        {
            KitCoopServer.RemoveLink(chosen);
            KitCoopServer.RemoveLink(KitCoopServer.KitLinkPath(linkBase));
            Delete(root);
        }
    }

    [Theory]
    [InlineData(@"C:\Coop016", @"c:\coop016\TAOM\", true)]
    [InlineData(@"C:\Coop016\", @"C:\COOP016", true)]
    [InlineData(@"c:\coop016\DedicatedServer", @"C:\Coop016\", true)]
    [InlineData(@"C:\Coop016", @"C:\Coop016-host\", false)]
    [InlineData(@"C:\Coop016\DedicatedServer", @"C:\Coop016\Data", false)]
    public void Overlap_ignores_casing_and_trailing_separators(string server, string folder, bool overlaps)
    {
        // None of these folders exist, so only the written paths are compared.
        Assert.Equal(overlaps, KitCoopServer.OverlapProblem(server, folder) != null);
    }

    [Fact]
    public void Empty_link_folders_go_whatever_the_casing_of_the_link_folder()
    {
        var root = NewRoot("kit-case");
        try
        {
            var linkBase = Path.Combine(root, "TAOM-Coop", "Coop016Server");
            var link = KitCoopServer.KitLinkPath(linkBase);
            Directory.CreateDirectory(Path.GetDirectoryName(link)!);

            KitCoopServer.RemoveEmptyParents(link, linkBase.ToUpperInvariant() + "\\");
            Assert.False(Directory.Exists(Path.Combine(linkBase, "steamapps")));
            Assert.True(Directory.Exists(linkBase));

            // Never above the link folder's steamapps, even when that is empty too.
            Directory.CreateDirectory(Path.GetDirectoryName(link)!);
            KitCoopServer.RemoveEmptyParents(link.ToLowerInvariant(), linkBase);
            Assert.False(Directory.Exists(Path.Combine(linkBase, "steamapps")));
            Assert.True(Directory.Exists(linkBase));
        }
        finally
        {
            Delete(root);
        }
    }

    // The window code can't run in tests: only that it calls the tested KitCoopServer steps, with line endings taken out.
    [Fact]
    public void The_window_uses_the_tested_steps()
    {
        var host = SourceFiles.Window(SourceFiles.Root(), "TaomCoopHost").Replace("\r", "");
        foreach (var call in new[] { "KitCoopServer.Pick(", "KitCoopServer.PrepareFromChosen(", "KitCoopServer.ChooseProblem(folder, serverRoot, dataRoot)", "KitCoopServer.RepairServer(" })
            Assert.Contains(call, host);
    }

    [Fact]
    public void Choose_says_what_a_pick_did()
    {
        var server = @"C:\Coop016\DedicatedServer";
        Assert.Equal((KitCoopServer.Chosen(server), false), KitCoopServer.ChooseResult(null, server, workshopFirst: false));
        Assert.Equal((KitCoopServer.ChosenAsBackup(server), false), KitCoopServer.ChooseResult(null, server, workshopFirst: true));
        Assert.Equal((KitCoopServer.WithWhereToGet(KitCoopServer.ClientNotServer), true), KitCoopServer.ChooseResult(KitCoopServer.ClientNotServer, null, workshopFirst: false));
        Assert.Equal((KitCoopServer.WithWhereToGet(KitCoopServer.NotAServer), true), KitCoopServer.ChooseResult(null, null, workshopFirst: true));
    }

    // The tool can't know at startup whether the Workshop has Coop 0.1.6 without hashing, so the line never says which server is used.
    [Fact]
    public void Startup_says_the_chosen_server_is_there_or_gone()
    {
        var root = NewRoot("kit-startup");
        try
        {
            Assert.Null(KitCoopServer.StartupLine(null));
            Assert.Null(KitCoopServer.StartupLine(""));
            Assert.Equal((KitCoopServer.YourServer(root), false), KitCoopServer.StartupLine(root));
            var gone = Path.Combine(root, "gone");
            Assert.Equal((KitCoopServer.Gone(gone), true), KitCoopServer.StartupLine(gone));
            Assert.DoesNotContain("has no Coop", KitCoopServer.YourServer(root));
        }
        finally
        {
            Delete(root);
        }
    }

    // CR-VCTXE: a good pick showed only on the Dashboard while the button is on the Saves page, so the host saw nothing happen.
    [Fact]
    public void A_pick_shows_its_result_under_the_button()
    {
        var root = SourceFiles.Root();
        var host = SourceFiles.Window(root, "TaomCoopHost").Replace("\r", "");
        var choose = SourceFiles.Method(host, "private async void ChooseCoop016Server_Click(");
        Assert.Contains("KitCoopServer.ChooseResult(problem, server, workshopFirst)", choose);
        Assert.Contains("ShowChooseResult(result, failed: false);", choose);
        // Busy, a bad pick, a settings file that can't be written and a good pick all show on both pages.
        Assert.DoesNotContain("SetControl(", choose);
        var show = SourceFiles.Method(host, "private void ShowChooseResult(");
        Assert.Contains("SetControl(text, failed);", show);
        Assert.Contains("ShowCoop016Status(text, failed);", show);
        Assert.Contains("if (KitCoopServer.StartupLine(_settings.Coop016Server) is { } coop016)\n            ShowCoop016Status(coop016.Text, coop016.Failed);", host);

        var xaml = File.ReadAllText(Path.Combine(root, "src", "TaomCoopHost", "MainWindow.xaml")).Replace("\r", "");
        var button = xaml.IndexOf("Click=\"ChooseCoop016Server_Click\"", StringComparison.Ordinal);
        var status = xaml.IndexOf("x:Name=\"Coop016Status\"", StringComparison.Ordinal);
        Assert.True(button > 0 && status > button && xaml.IndexOf("</StackPanel>", button, StringComparison.Ordinal) > status);
    }
}
