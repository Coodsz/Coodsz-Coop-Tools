using BannerlordCoopLauncher.Core.IO;
using BannerlordCoopLauncher.Core.Launch;
using CoodszHost.Services;
using Mono.Cecil;
using Mono.Cecil.Cil;
using BannerlordCoopLauncher.Core.Models;
using BannerlordCoopLauncher.Core.Modules;
using BannerlordCoopLauncher.Core.Nexus;
using BannerlordCoopLauncher.Core.Parsing;
using BannerlordCoopLauncher.Core.Profile;
using BannerlordCoopLauncher.Core.Steam;
using BannerlordCoopLauncher.Core.Support;
using System.Security.AccessControl;

namespace BannerlordCoopLauncher.Tests;

public class LauncherTests
{
    [Fact]
    public void Nexus_archive_installs_the_matching_module_folder()
    {
        var root = Path.Combine(Path.GetTempPath(), "nexus-install-" + Guid.NewGuid().ToString("N"));
        var extracted = Path.Combine(root, "extracted");
        var game = Path.Combine(root, "game");
        Directory.CreateDirectory(Path.Combine(extracted, "Bannerlord.Harmony"));
        Directory.CreateDirectory(Path.Combine(game, "Modules"));
        File.WriteAllText(
            Path.Combine(extracted, "Bannerlord.Harmony", "SubModule.xml"),
            """<Module><Id value="Bannerlord.Harmony"/></Module>""");
        var found = ModuleZipInstaller.FindModuleDirectory(extracted, "Bannerlord.Harmony");
        Assert.NotNull(found);
        ModuleZipInstaller.InstallDirectory(found!, game, "Bannerlord.Harmony");
        Assert.True(File.Exists(Path.Combine(game, "Modules", "Bannerlord.Harmony", "SubModule.xml")));
        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public void Profile_rejects_a_host_that_includes_a_port()
    {
        var error = Assert.Throws<ProfileException>(() => ProfileJson.Parse("""
            {
              "Host": "127.0.0.1:4200",
              "Port": 4200,
              "BaseModules": ["Native"],
              "Mods": [{ "Name": "Coop", "WorkshopId": "1", "ModuleId": "Coop", "Enabled": true }]
            }
            """));
        Assert.Contains("without a port", error.Message);
    }

    [Fact]
    public void Default_profile_can_contain_only_coop()
    {
        var profile = SampleProfile();
        Assert.True(ProfileJson.IncludesCoopModule(profile));
        Assert.False(ModuleResolver.RequiresSteamMetadata(profile));
    }

    [Fact]
    public void Pinned_modules_skip_steam_metadata_even_when_version_checks_are_on()
    {
        var profile = SampleProfile(checkVersions: true, pinned: true);
        Assert.False(ModuleResolver.RequiresSteamMetadata(profile));
    }

    [Fact]
    public void Launch_preserves_autoconnect_and_diagnostic_omits_it()
    {
        var profile = SampleProfile();
        var normal = LaunchArgumentBuilder.Create(@"C:\game", profile, diagnostic: false);
        var diagnostic = LaunchArgumentBuilder.Create(@"C:\game", profile, diagnostic: true);

        Assert.Equal("/singleplayer /client /autoconnect _MODULES_*Native*CoopNightly*_MODULES_", normal.Arguments);
        var addressed = LaunchArgumentBuilder.Create(@"C:\game", profile, diagnostic: false, directEndpoint: "10.4.5.6:4200");
        Assert.Contains("/autoconnect 10.4.5.6:4200", addressed.Arguments);
        Assert.DoesNotContain("+connect_lobby", addressed.Arguments);
        var named = LaunchArgumentBuilder.Create(@"C:\game", profile, diagnostic: false, "Calradia Host", connectLobby: 99);
        Assert.Contains("+connect_lobby 99", named.Arguments);
        Assert.DoesNotContain("/autoconnect", named.Arguments);
        Assert.DoesNotContain("/steamname", named.Arguments);
        Assert.DoesNotContain("127.0.0.1", named.Arguments);
        Assert.True(normal.AutoConnect);
        Assert.Equal("/singleplayer _MODULES_*Native*CoopNightly*_MODULES_", diagnostic.Arguments);
        Assert.False(diagnostic.AutoConnect);
        Assert.DoesNotContain("/client", diagnostic.Arguments);
    }

    [Fact]
    public void PSCoopBridge_is_left_out_without_Player_Settlement()
    {
        var profile = new ServerProfile();
        DefaultClientSet.Apply(profile);
        var order = LaunchArgumentBuilder.ModuleOrder(profile);
        Assert.DoesNotContain("PSCoopBridge", order);
        Assert.Contains("CoopModPatch", order);

        var locked = new ServerProfile();
        ServerModuleSet.Apply(locked, includeMoreNations: false);
        var withSettlement = LaunchArgumentBuilder.ModuleOrder(locked);
        Assert.Equal("PSCoopBridge", withSettlement[^1]);
        Assert.Contains("PlayerSettlement", withSettlement);

        locked.Mods.First(mod => mod.ModuleId == "PlayerSettlement").Enabled = false;
        Assert.DoesNotContain("PSCoopBridge", LaunchArgumentBuilder.ModuleOrder(locked));

        Assert.True(SettlementBridgeRule.BridgeWithoutSettlement(new[] { "Coop", "PSCoopBridge" }));
        Assert.False(SettlementBridgeRule.BridgeWithoutSettlement(new[] { "Coop", "PlayerSettlement", "PSCoopBridge" }));
        Assert.False(SettlementBridgeRule.BridgeWithoutSettlement(new[] { "Coop" }));
    }

    [Fact]
    public void Join_explains_a_host_that_still_needs_PSCoopBridge()
    {
        var lines = JoinResultExplain.Explain("Module validation failed: To join the server the module 'PSCoopBridge' with version 'v0.17.6' is required.");
        Assert.Contains(lines, line => line.Contains("updates Coodsz Coop Server Tool", StringComparison.Ordinal));
        var settlementHost = JoinResultExplain.Explain("Module validation failed: To join the server the module 'PSCoopBridge' with version 'v0.17.6' is required. To join the server the module 'PlayerSettlement' with version 'v1' is required.");
        Assert.DoesNotContain(settlementHost, line => line.Contains("updates Coodsz Coop Server Tool", StringComparison.Ordinal));
    }

    // Coop 0.1.7 names a mod 'Display Name' (Id) when it has a name of its own (ModuleValidator.Describe).
    [Fact]
    public void Join_explains_Coop_0_1_7_refusals_that_name_the_mod_and_its_id()
    {
        var version = JoinResultExplain.Explain("Module validation failed! Reason: Wrong version of module 'ButterLib' (Bannerlord.ButterLib) detected. Server uses 'v2.12.0.0', client uses 'v2.10.4.0'.");
        Assert.Contains(version, line => line.StartsWith("ButterLib does not match the server.", StringComparison.Ordinal) && line.Contains("v2.12.0.0", StringComparison.Ordinal));

        var bridge = JoinResultExplain.Explain("Module validation failed: To join the server the module 'PS Coop Bridge' (PSCoopBridge) with version 'v0.17.6' is required.");
        Assert.Contains(bridge, line => line.Contains("updates Coodsz Coop Server Tool", StringComparison.Ordinal));
        var withSettlement = JoinResultExplain.Explain("Module validation failed: To join the server the module 'PS Coop Bridge' (PSCoopBridge) with version 'v0.17.6' is required. To join the server the module 'Player Settlement' (PlayerSettlement) with version 'v1' is required.");
        Assert.DoesNotContain(withSettlement, line => line.Contains("updates Coodsz Coop Server Tool", StringComparison.Ordinal));
    }

    [Fact]
    public void SubModule_xml_rejects_dtd_and_reads_the_module_id()
    {
        var malicious = "<!DOCTYPE x [<!ENTITY a SYSTEM 'file:///test'>]><Module><Id value=\"Coop\"/></Module>";
        Assert.False(SubModuleXml.TryRead(malicious, out _, out var error));
        Assert.Equal("SubModule.xml invalid.", error);

        Assert.True(SubModuleXml.TryRead(ModuleXml("CoopNightly", "1.2.0"), out var info, out _));
        Assert.Equal("CoopNightly", info!.Id);
        Assert.Equal("1.2.0", info.Version);
    }

    [WindowsFact]
    public void Steam_libraries_are_counted_once_and_locate_bannerlord()
    {
        var files = new MemoryFileSystem();
        files.AddFile(@"C:\Steam\steamapps\libraryfolders.vdf", """
            "libraryfolders"
            {
                "0" { "path" "C:\\Steam" }
                "1" { "path" "C:/Steam" }
                "2" { "path" "D:\\SteamLibrary" }
            }
            """);
        var libraries = SteamInstallFinder.ReadLibraryPaths(@"C:\Steam", files.ReadAllText(@"C:\Steam\steamapps\libraryfolders.vdf")!);
        Assert.Equal(2, libraries.Count);

        var game = Path.Combine(@"D:\SteamLibrary", "steamapps", "common", "Mount & Blade II Bannerlord");
        files.AddFile(Path.Combine(game, "bin", "Win64_Shipping_Client", "Bannerlord.exe"), "");
        files.AddFile(Path.Combine(game, "Modules", "Native", "SubModule.xml"), ModuleXml("Native", "1.0"));
        files.AddFile(Path.Combine(@"D:\SteamLibrary", "steamapps", "appmanifest_261550.acf"), """
            "AppState"
            {
                "appid" "261550"
                "installdir" "Mount & Blade II Bannerlord"
            }
            """);

        Assert.Equal(game, SteamInstallFinder.FindGameRoot(@"C:\Steam", files));
    }

    [Fact]
    public void The_coop_client_keeps_the_steam_folders_workshop_mods_when_the_library_list_is_damaged()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "BannerlordCoopLauncher.sln")))
            dir = dir.Parent;
        var source = File.ReadAllText(Path.Combine(dir!.FullName, "src", "BannerlordCoopLauncher", "MainWindow.Launch.cs"));
        var read = source[source.IndexOf("private IReadOnlyList<WorkshopLibrary> ReadWorkshops()", StringComparison.Ordinal)..];
        var library = read.IndexOf("SteamInstallFinder.ReadLibraryPaths(steamPath, vdf)", StringComparison.Ordinal);
        Assert.True(library > 0);
        Assert.Contains("catch (BannerlordCoopLauncher.Core.Parsing.VdfException", read[library..]);
        Assert.Contains("IReadOnlyList<string> libraries = new[] { steamPath };", read);
    }

    [WindowsFact]
    public void A_corrupt_library_list_still_finds_bannerlord_in_the_steam_folder()
    {
        var files = new MemoryFileSystem();
        files.AddFile(@"C:\Steam\steamapps\libraryfolders.vdf", "\"libraryfolders\" { \"0\" { \"path\"");
        var game = Path.Combine(@"C:\Steam", "steamapps", "common", "Bannerlord Renamed");
        files.AddFile(Path.Combine(game, "bin", "Win64_Shipping_Client", "Bannerlord.exe"), "");
        files.AddFile(Path.Combine(game, "Modules", "Native", "SubModule.xml"), ModuleXml("Native", "1.0"));
        files.AddFile(Path.Combine(@"C:\Steam", "steamapps", "appmanifest_261550.acf"), """
            "AppState"
            {
                "appid" "261550"
                "installdir" "Bannerlord Renamed"
            }
            """);

        Assert.Equal(game, SteamInstallFinder.FindGameRoot(@"C:\Steam", files));
    }

    [Fact]
    public void Nested_workshop_module_is_ready_without_a_version_check()
    {
        var files = InstalledGame();
        var resolution = ModuleResolver.Resolve(SampleProfile(), Game, Workshops(files, localTime: 10), remote: null, files);
        var coop = Assert.Single(resolution.Modules);
        Assert.Equal(ModuleState.Ready, coop.State);
        Assert.Null(ModuleResolver.ConnectBlockReason(resolution));
    }

    [Fact]
    public void Missing_workshop_module_locks_connect()
    {
        var files = InstalledGame(includeWorkshop: false);
        var resolution = ModuleResolver.Resolve(SampleProfile(), Game, Array.Empty<WorkshopLibrary>(), null, files);
        Assert.Equal(ModuleState.Missing, Assert.Single(resolution.Modules).State);
        Assert.Equal(CoopInstall.ClientMissingLine, Assert.Single(resolution.Modules).Detail);
        Assert.Equal(
            "Connect is locked. Resolve the marked modules in Steam, then click Check mods.",
            ModuleResolver.ConnectBlockReason(resolution));
    }

    [Fact]
    public void Coop_and_CoopNightly_are_the_same_Bannerlord_Coop_mod()
    {
        var files = InstalledGame(moduleId: "CoopNightly");
        var profile = ProfileJson.Parse("""
            {
              "Name": "CALRADIA",
              "Port": 4200,
              "BaseModules": ["Native"],
              "Mods": [{ "Name": "Bannerlord Coop", "WorkshopId": "3770450698", "ModuleId": "Coop", "Enabled": true, "Pinned": true }],
              "ModuleLoadOrder": ["Native", "Coop"]
            }
            """);
        var resolution = ModuleResolver.Resolve(profile, Game, Workshops(files, 10), null, files);
        var coop = Assert.Single(resolution.Modules);
        Assert.Equal(ModuleState.Ready, coop.State);
        Assert.Equal("CoopNightly", coop.LaunchId);
        var plan = LaunchArgumentBuilder.Create(Game, profile, diagnostic: false, launchIds: new Dictionary<string, string> { ["Coop"] = coop.LaunchId! });
        Assert.Contains("*Native*CoopNightly*_MODULES_", plan.Arguments);
        Assert.Null(ModuleResolver.ConnectBlockReason(resolution));
    }

    [Fact]
    public void A_failed_join_names_a_newer_server_version_and_missing_mods()
    {
        var log = """
            Module validation failed!
            Reason: 'CoopNightly' version 'v0.1.6', but the client uses 'v0.1.5'. Update the co-op mod on both sides.
            missing mods: ImprovedGarrisons, AgricultureEstate
            incompatible mods: CoopMarriage
            """;
        var lines = JoinResultExplain.Explain(log);
        Assert.Contains("CoopNightly does not match the server. The server is v0.1.6. Yours is v0.1.5. The server has a newer one than yours.", lines);
        Assert.Contains("These mods are missing: ImprovedGarrisons, AgricultureEstate.", lines);
        Assert.Contains("These mods are not compatible with the server: CoopMarriage.", lines);
        Assert.False(JoinResultExplain.Succeeded(log));
        Assert.True(JoinResultExplain.Succeeded("Received host save transfer 1: 1 chunks"));

        var reported = JoinResultExplain.Explain("Wrong version of module 'ImprovedGarrisons' with version 'v1.0' detected. Server uses 'v1.2', client uses 'v1.0'.");
        Assert.Contains("ImprovedGarrisons does not match the server. The server is v1.2. Yours is v1.0. The server has a newer one than yours.", reported);
    }

    [Fact]
    public void A_Steam_join_that_never_reaches_the_host_does_not_say_to_check_the_address()
    {
        // A Steam join has no address or UDP port to check.
        var steam = JoinResultExplain.Explain("Could not reach the co-op host through Steam. Make sure the host is still in their session, then try the invite again.");
        Assert.Contains(JoinResultExplain.SteamUnreachable, steam);
        Assert.DoesNotContain(steam, line => line.Contains("UDP 4200", StringComparison.Ordinal));

        // The log holds every try since launch: a retry that reached the host and was refused is that refusal, not WARP.
        var retried = JoinResultExplain.Explain("Could not reach the co-op host through Steam. Make sure the host is still in their session, then try the invite again.\n"
            + "Steam tunnel to the host established\n"
            + "The server password is incorrect.");
        Assert.Contains("The server password is wrong.", retried);
        Assert.DoesNotContain(JoinResultExplain.SteamUnreachable, retried);
        Assert.DoesNotContain(retried, line => line.Contains("UDP 4200", StringComparison.Ordinal));

        var direct = JoinResultExplain.Explain("Could not reach the co-op host at [address]:7777.");
        Assert.Contains("Could not finish joining. Check the address and that UDP 4200 is open.", direct);
        Assert.DoesNotContain(JoinResultExplain.SteamUnreachable, direct);
    }

    [Fact]
    public void A_short_server_list_and_a_newer_game_build_tell_the_player_what_to_do()
    {
        var log = "module validation: server=[Native v1.4.8.118999, SandBoxCore v1.4.8.118999] client=[Bannerlord.Harmony v2.4.2.248, Native v1.4.8.119303] → REJECT: Server does not support module 'Bannerlord.Harmony'.";
        var lines = JoinResultExplain.Explain(log);
        Assert.Contains(lines, line => line.StartsWith("What to do: The dedicated server started without those mods.", StringComparison.Ordinal));
        Assert.Contains("Bannerlord builds do not match. The dedicated server is 1.4.8.118999. The joining game is 1.4.8.119303.", lines);
        Assert.Contains(lines, line => line.Contains("3770450698", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.StartsWith("What not to do: Do not copy the game folder", StringComparison.Ordinal));
    }

    [Fact]
    public void Unsupported_PSCoopBridge_tells_the_player_to_match_player_settlement()
    {
        var lines = JoinResultExplain.Explain("Module validation failed!\nReason: Server does not support module 'PSCoopBridge'.");
        Assert.Contains(lines, line => line.Contains("Player Settlement", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(lines, line => line.StartsWith("What not to do: Do not leave PSCoopBridge on", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, line => line.StartsWith("What not to do: Do not turn those mods off on the client", StringComparison.Ordinal));
    }

    [Fact]
    public void Mcm_leftovers_only_remove_optional_configs_that_are_not_kept()
    {
        var root = Path.Combine(Path.GetTempPath(), "mcm-left-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "PlayerSettlement"));
        Directory.CreateDirectory(Path.Combine(root, "Native"));
        File.WriteAllText(Path.Combine(root, "PSCoopBridge.json"), "{}");
        File.WriteAllText(Path.Combine(root, "ImportantMod.json"), "{}");

        var cleared = McmLeftovers.ClearUnused(root, ["Native"]);
        Assert.Equal(2, cleared.Removed);
        Assert.Contains("PlayerSettlement", cleared.Names);
        Assert.Contains("PSCoopBridge", cleared.Names);
        Assert.True(Directory.Exists(Path.Combine(root, "Native")));
        Assert.True(File.Exists(Path.Combine(root, "ImportantMod.json")));
        Directory.Delete(root, recursive: true);
    }

    [WindowsFact]
    public void Module_id_mismatch_and_duplicate_folders_are_reported()
    {
        var files = InstalledGame(moduleId: "WrongId");
        var mismatch = ModuleResolver.Resolve(SampleProfile(), Game, Workshops(files, 10), null, files);
        Assert.Equal(ModuleState.IdMismatch, Assert.Single(mismatch.Modules).State);

        var duplicate = InstalledGame();
        var second = Path.Combine(WorkshopRoot, "3770450698", "OtherCopy");
        duplicate.AddFile(Path.Combine(second, "SubModule.xml"), ModuleXml("CoopNightly", "1.1"));
        var chosen = ModuleResolver.Resolve(SampleProfile(), Game, Workshops(duplicate, 10), null, duplicate);
        var coop = Assert.Single(chosen.Modules);
        Assert.Equal(ModuleState.Ready, coop.State);
        Assert.Contains("OtherCopy", coop.Detail);
        Assert.Contains(coop.ExtraCopies!, path => path.EndsWith("OtherCopy", StringComparison.OrdinalIgnoreCase));

        // A Coop folder with the other id (Coop vs CoopNightly), or an identical
        // copy (same id and version), is not a clash.
        var mixed = InstalledGame();
        mixed.AddFile(Path.Combine(Game, "Modules", "Coop", "SubModule.xml"), ModuleXml("Coop", "1.0"));
        var mixedCoop = Assert.Single(ModuleResolver.Resolve(SampleProfile(), Game, Workshops(mixed, 10), null, mixed).Modules);
        Assert.Equal(ModuleState.Ready, mixedCoop.State);
        Assert.Null(mixedCoop.ExtraCopies);
        Assert.DoesNotContain("Extra copies", mixedCoop.Detail);
        var same = InstalledGame();
        same.AddFile(Path.Combine(Game, "Modules", "Coop", "SubModule.xml"), ModuleXml("CoopNightly", "1.0"));
        Assert.Null(Assert.Single(ModuleResolver.Resolve(SampleProfile(), Game, Workshops(same, 10), null, same).Modules).ExtraCopies);

        var tied = InstalledGame(includeWorkshop: false);
        tied.AddFile(Path.Combine(Game, "Modules", "Alpha", "SubModule.xml"), ModuleXml("CoopNightly", "1.0"));
        tied.AddFile(Path.Combine(Game, "Modules", "Beta", "SubModule.xml"), ModuleXml("CoopNightly", "1.1"));
        // Two Coop folders in Modules: the launcher picks one and reports the
        // other, so the player is told the game would load both.
        var tiedResult = ModuleResolver.Resolve(SampleProfile(), Game, Array.Empty<WorkshopLibrary>(), null, tied);
        var extra = Assert.Single(tiedResult.Modules);
        Assert.Equal(ModuleState.Ready, extra.State);
        var both = new[] { extra.Directory!, Assert.Single(extra.ExtraCopies!) }.Select(Path.GetFileName).ToList();
        Assert.Contains("Alpha", both);
        Assert.Contains("Beta", both);
    }

    [Fact]
    public void Newer_steam_publication_blocks_launch_even_when_the_xml_version_is_unchanged()
    {
        var files = InstalledGame();
        var remote = new Dictionary<string, PublishedFile>
        {
            ["3770450698"] = new("3770450698", 1, TimeUpdated: 200, SteamInstallFinder.BannerlordAppId, "Coop")
        };
        var pending = ModuleResolver.Resolve(SampleProfile(checkVersions: true), Game, Workshops(files, localTime: 100), remote, files);
        Assert.Equal(ModuleState.UpdatePending, Assert.Single(pending.Modules).State);

        remote["3770450698"] = new("3770450698", 1, TimeUpdated: 100, SteamInstallFinder.BannerlordAppId, "Coop");
        var ready = ModuleResolver.Resolve(SampleProfile(checkVersions: true), Game, Workshops(files, localTime: 100), remote, files);
        Assert.Equal(ModuleState.Ready, Assert.Single(ready.Modules).State);
        Assert.Equal("Latest Workshop release verified.", Assert.Single(ready.Modules).Detail);
    }

    [Fact]
    public void A_pending_update_steam_would_not_download_does_not_lock_connect_again()
    {
        var files = InstalledGame();
        var remote = new Dictionary<string, PublishedFile>
        {
            ["3770450698"] = new("3770450698", 1, TimeUpdated: 200, SteamInstallFinder.BannerlordAppId, "Coop")
        };
        var profile = SampleProfile(checkVersions: true);
        Assert.Equal(ModuleState.UpdatePending, Assert.Single(ModuleResolver.Resolve(profile, Game, Workshops(files, localTime: 100), remote, files).Modules).State);

        var kept = Assert.Single(ModuleResolver.Resolve(profile, Game, Workshops(files, localTime: 100), remote, files, new[] { "3770450698" }).Modules);
        Assert.Equal(ModuleState.Ready, kept.State);
        Assert.Contains("would not download the newest Workshop update", kept.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void A_page_edit_without_new_files_is_not_an_update()
    {
        var files = InstalledGame();
        var remote = new Dictionary<string, PublishedFile>
        {
            ["3770450698"] = new("3770450698", 1, TimeUpdated: 200, SteamInstallFinder.BannerlordAppId, "Coop", ContentHandle: "555")
        };
        var same = new[]
        {
            new WorkshopLibrary(WorkshopRoot, new Dictionary<string, long> { ["3770450698"] = 100 }, ManifestParsed: true,
                new Dictionary<string, string> { ["3770450698"] = "555" })
        };
        Assert.Equal(ModuleState.Ready, Assert.Single(ModuleResolver.Resolve(SampleProfile(checkVersions: true), Game, same, remote, files).Modules).State);

        var other = new[]
        {
            new WorkshopLibrary(WorkshopRoot, new Dictionary<string, long> { ["3770450698"] = 100 }, ManifestParsed: true,
                new Dictionary<string, string> { ["3770450698"] = "444" })
        };
        Assert.Equal(ModuleState.UpdatePending, Assert.Single(ModuleResolver.Resolve(SampleProfile(checkVersions: true), Game, other, remote, files).Modules).State);
    }

    // Coodsz's PC on 2026-10-17, Mod Configuration Menu v5. The Workshop page had a
    // newer upload, but Steam's own latest_manifest was the installed one, so Steam
    // downloaded nothing and Install mods kept asking.
    private const string McmId = "2859238197";
    private const string McmPage = """{"response":{"publishedfiledetails":[{"publishedfileid":"2859238197","result":1,"time_updated":1792260143,"consumer_app_id":261550,"title":"Mod Configuration Menu v5","hcontent_file":"2332016996917963039"}]}}""";

    private static string McmAcf(string latestManifest, string latestTime) => $$"""
        "AppWorkshop"
        {
            "appid"		"261550"
            "WorkshopItemsInstalled"
            {
                "2859238197"
                {
                    "size"		"26036072"
                    "timeupdated"		"1787516166"
                    "manifest"		"1230832441695139449"
                }
            }
            "WorkshopItemDetails"
            {
                "2859238197"
                {
                    "manifest"		"1230832441695139449"
                    "timeupdated"		"1787516166"
                    "timetouched"		"1787516200"
                    "latest_timeupdated"		"{{latestTime}}"
                    "latest_manifest"		"{{latestManifest}}"
                }
            }
        }
        """;

    private static (ServerProfile Profile, MemoryFileSystem Files, IReadOnlyList<WorkshopLibrary> Libraries) McmSetup(string? acf)
    {
        var files = InstalledGame(includeWorkshop: false);
        files.AddFile(Path.Combine(WorkshopRoot, McmId, "SubModule.xml"), ModuleXml("Bannerlord.MBOptionScreen", "v5.10.1"));
        if (acf != null)
            files.AddFile(@"C:\steam\steamapps\workshop\appworkshop_261550.acf", acf);
        var profile = SampleProfile(checkVersions: true);
        profile.Mods.Clear();
        profile.Mods.Add(new ProfileMod { Name = "Mod Configuration Menu v5", WorkshopId = McmId, ModuleId = "Bannerlord.MBOptionScreen", Enabled = true });
        var libraries = acf == null
            ? new[] { new WorkshopLibrary(WorkshopRoot, new Dictionary<string, long> { [McmId] = 1787516166 }, ManifestParsed: true) }
            : SteamInstallFinder.ReadWorkshopLibraries([@"C:\steam"], files);
        return (profile, files, libraries);
    }

    private static Dictionary<string, PublishedFile> McmRemote() =>
        SteamWorkshopClient.Parse(McmPage).ToDictionary(file => file.PublishedFileId);

    [Theory]
    [InlineData(McmId)]
    [InlineData("0")]
    public void The_nexus_mcm_in_the_game_folder_wins_over_the_old_workshop_download(string workshopId)
    {
        // A setup saved before v1.2.76 still names MCM by the removed Workshop item (CR-J5AUW).
        var (profile, files, libraries) = McmSetup(null);
        files.AddFile(Path.Combine(Game, "Modules", "Bannerlord.MBOptionScreen", "SubModule.xml"), ModuleXml("Bannerlord.MBOptionScreen", "v5.13.1"));
        profile.Mods[0].WorkshopId = workshopId;

        var module = Assert.Single(ModuleResolver.Resolve(profile, Game, libraries, McmRemote(), files).Modules);

        Assert.Equal(Path.Combine(Game, "Modules", "Bannerlord.MBOptionScreen"), module.Directory);
        Assert.Contains(Path.Combine(WorkshopRoot, McmId), Assert.IsAssignableFrom<IReadOnlyList<string>>(module.ExtraCopies));
    }

    [Fact]
    public void The_old_workshop_mcm_is_still_used_when_it_is_the_only_copy()
    {
        var (profile, files, libraries) = McmSetup(null);
        profile.Mods[0].WorkshopId = "0";

        var module = Assert.Single(ModuleResolver.Resolve(profile, Game, libraries, McmRemote(), files).Modules);

        Assert.Equal(ModuleState.Ready, module.State);
        Assert.Equal(Path.Combine(WorkshopRoot, McmId), module.Directory);
    }

    [Fact]
    public void A_copy_steam_itself_calls_newest_is_not_an_update()
    {
        var (profile, files, libraries) = McmSetup(McmAcf("1230832441695139449", "1787516166"));
        Assert.Equal("1230832441695139449", Assert.Single(libraries).LatestManifests![McmId]);

        var module = Assert.Single(ModuleResolver.Resolve(profile, Game, libraries, McmRemote(), files).Modules);

        Assert.Equal(ModuleState.Ready, module.State);
        Assert.Contains("Steam says this is the newest copy it has", module.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void A_newer_manifest_steam_knows_of_is_still_an_update()
    {
        var (profile, files, libraries) = McmSetup(McmAcf("2332016996917963039", "1792260143"));

        var module = Assert.Single(ModuleResolver.Resolve(profile, Game, libraries, McmRemote(), files).Modules);

        Assert.Equal(ModuleState.UpdatePending, module.State);
    }

    [Fact]
    public void Without_steam_details_a_newer_workshop_page_is_still_an_update()
    {
        var (profile, files, libraries) = McmSetup(acf: null);

        Assert.Equal(ModuleState.UpdatePending, Assert.Single(ModuleResolver.Resolve(profile, Game, libraries, McmRemote(), files).Modules).State);
    }

    // DownloadItem on an item Steam calls current: the download flag shows for one
    // poll, then Steam reports it installed and ready with the same old files.
    private sealed class BlinkingSteam : IWorkshopApi
    {
        private int _pumps;
        public bool Subscribe(ulong itemId) => true;
        public void Download(ulong itemId) { }
        public WorkshopItemState State(ulong itemId) =>
            WorkshopItemState.Subscribed | WorkshopItemState.Installed | (_pumps == 1 ? WorkshopItemState.Downloading : WorkshopItemState.None);
        public (ulong Downloaded, ulong Total) Progress(ulong itemId) => (0, 0);
        public void Pump() => _pumps++;
    }

    [Fact]
    public void Install_mods_keeps_the_local_copy_when_steam_reports_it_ready_after_a_brief_download_flag()
    {
        var (profile, files, libraries) = McmSetup(acf: null);
        var before = ModuleResolver.Resolve(profile, Game, libraries, McmRemote(), files);
        Assert.Equal(ModuleState.UpdatePending, Assert.Single(before.Modules).State);

        var result = WorkshopSubscribe.Run(new BlinkingSteam(), [ulong.Parse(McmId)], new Dictionary<ulong, string>(), _ => { }, TimeSpan.FromSeconds(10), CancellationToken.None,
            TimeSpan.FromMilliseconds(1), settle: TimeSpan.FromMilliseconds(20));
        Assert.Equal([ulong.Parse(McmId)], result.Ready);
        // The flag blinked, so the old AlreadyCurrent check missed it and Install mods looped.
        Assert.Empty(result.NothingDownloaded);

        var stuck = ModuleResolver.SteamWillNotUpdate(before.Modules, result.Ready.Select(id => id.ToString()));
        var keep = stuck.Select(module => module.WorkshopId!).ToList();
        Assert.Equal([McmId], keep);

        var after = Assert.Single(ModuleResolver.Resolve(profile, Game, libraries, McmRemote(), files, keep).Modules);
        Assert.Equal(ModuleState.Ready, after.State);
        Assert.Contains("would not download the newest Workshop update", after.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void A_copy_put_in_modules_by_hand_is_used_without_a_steam_update_check()
    {
        var files = InstalledGame(includeWorkshop: false);
        files.AddFile(Path.Combine(Game, "Modules", "CoopNightly", "SubModule.xml"), ModuleXml("CoopNightly", "1.0"));
        var remote = new Dictionary<string, PublishedFile>
        {
            ["3770450698"] = new("3770450698", 1, TimeUpdated: 200, SteamInstallFinder.BannerlordAppId, "Coop")
        };
        var empty = new[] { new WorkshopLibrary(WorkshopRoot, new Dictionary<string, long>(), ManifestParsed: true) };

        var module = Assert.Single(ModuleResolver.Resolve(SampleProfile(checkVersions: true), Game, empty, remote, files).Modules);
        Assert.Equal(ModuleState.Ready, module.State);
        Assert.Contains("not from Steam", module.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void Mods_removed_from_the_workshop_are_local_mods_on_the_built_in_setups()
    {
        string[] retired = ["3794802607", "3806450944", "3806450713"];
        var locked = new ServerProfile();
        ServerModuleSet.Apply(locked);
        var open = new ServerProfile();
        DefaultClientSet.Apply(open);
        CampaignMapMods.Apply(open, includeMoreNations: true);

        foreach (var profile in new[] { locked, open })
        {
            Assert.DoesNotContain(profile.Mods, mod => retired.Contains(mod.WorkshopId));
            foreach (var id in new[] { "MoreNationsRemastered", "MoreNationsRemasteredFix" })
                Assert.Equal("0", profile.Mods.Single(mod => mod.ModuleId == id).WorkshopId);
        }

        // COOP Server Settings Lock left the setup in v1.3.4 (gone from the Workshop, Nexus and ModDB).
        Assert.DoesNotContain(locked.Mods, mod => mod.ModuleId == "ServerSettingsLock");
        Assert.DoesNotContain("ServerSettingsLock", locked.ModuleLoadOrder);
        foreach (var id in retired)
        {
            Assert.True(RetiredWorkshopItems.IsRetired(id));
            Assert.False(SharedModList.IsWorkshopId(id));
        }

        Assert.Equal("", SharedModList.WorkshopIdFor("0", Path.Combine(WorkshopRoot, "3806450944")));
        Assert.Equal("3770450698", SharedModList.WorkshopIdFor("0", Path.Combine(WorkshopRoot, "3770450698")));
    }

    [Fact]
    public void A_copy_steam_downloaded_before_the_item_was_removed_is_still_used()
    {
        var files = InstalledGame(includeWorkshop: false);
        files.AddFile(Path.Combine(WorkshopRoot, "3806450944", "SubModule.xml"), ModuleXml("MoreNationsRemastered", "v1.0.3"));
        var profile = SampleProfile(checkVersions: true);
        profile.Mods.Add(new ProfileMod { Name = "More Nations Remastered", WorkshopId = "0", ModuleId = "MoreNationsRemastered", Enabled = true });
        profile.Mods.Add(new ProfileMod { Name = "MoreNationsRemastered COOP Fix", WorkshopId = "0", ModuleId = "MoreNationsRemasteredFix", Enabled = true });
        var workshops = new[] { new WorkshopLibrary(WorkshopRoot, new Dictionary<string, long> { ["3806450944"] = 100 }, ManifestParsed: true) };

        var modules = ModuleResolver.Resolve(profile, Game, workshops, remote: null, files).Modules;

        var nations = modules.Single(module => module.ModuleId == "MoreNationsRemastered");
        Assert.Equal(ModuleState.Ready, nations.State);
        Assert.Equal("Installed.", nations.Detail);
        Assert.Equal(Path.Combine(WorkshopRoot, "3806450944"), nations.Directory);
        var fix = modules.Single(module => module.ModuleId == "MoreNationsRemasteredFix");
        Assert.Equal(ModuleState.Missing, fix.State);
        Assert.Equal("This mod is not in the game's Modules folder.", fix.Detail);
    }

    [Fact]
    public void The_newest_install_time_wins_when_two_steam_libraries_list_the_item()
    {
        var files = InstalledGame();
        var remote = new Dictionary<string, PublishedFile>
        {
            ["3770450698"] = new("3770450698", 1, TimeUpdated: 200, SteamInstallFinder.BannerlordAppId, "Coop")
        };
        var libraries = new[]
        {
            new WorkshopLibrary(Path.Combine(Path.GetPathRoot(WorkshopRoot)!, "OldLibrary", "content", "261550"), new Dictionary<string, long> { ["3770450698"] = 50 }, ManifestParsed: true),
            new WorkshopLibrary(WorkshopRoot, new Dictionary<string, long> { ["3770450698"] = 200 }, ManifestParsed: true)
        };
        Assert.Equal(ModuleState.Ready, Assert.Single(ModuleResolver.Resolve(SampleProfile(checkVersions: true), Game, libraries, remote, files).Modules).State);
    }

    [Fact]
    public void Installed_manifests_are_read_from_the_steam_workshop_file()
    {
        var manifests = SteamInstallFinder.ReadInstalledManifests("""
            "AppWorkshop"
            {
                "appid"		"261550"
                "WorkshopItemsInstalled"
                {
                    "2859238197"
                    {
                        "size"		"26036072"
                        "timeupdated"		"1787516166"
                        "manifest"		"1230832441695139449"
                    }
                }
            }
            """);
        Assert.Equal("1230832441695139449", manifests["2859238197"]);
        var parsed = SteamWorkshopClient.Parse("""{"response":{"publishedfiledetails":[{"publishedfileid":"2859238197","result":1,"time_updated":1792260143,"consumer_app_id":261550,"title":"Mod Configuration Menu v5","hcontent_file":"2332016996917963039"}]}}""");
        Assert.Equal("2332016996917963039", Assert.Single(parsed).ContentHandle);
    }

    [Fact]
    public void Hidden_items_that_are_installed_still_launch_and_wrong_game_items_do_not()
    {
        var files = InstalledGame();
        var profile = SampleProfile(checkVersions: true);
        var workshops = Workshops(files, 100);

        var missingMetadata = ModuleResolver.Resolve(profile, Game, workshops, remote: null, files);
        Assert.Equal(ModuleState.Ready, Assert.Single(missingMetadata.Modules).State);
        Assert.Contains("could not verify", Assert.Single(missingMetadata.Modules).Detail, StringComparison.OrdinalIgnoreCase);

        var remote = new Dictionary<string, PublishedFile>
        {
            ["3770450698"] = new("3770450698", 9, 100, SteamInstallFinder.BannerlordAppId, "Hidden")
        };
        var hidden = Assert.Single(ModuleResolver.Resolve(profile, Game, workshops, remote, files).Modules);
        Assert.Equal(ModuleState.Ready, hidden.State);
        Assert.Contains("hidden or removed", hidden.Detail, StringComparison.Ordinal);

        remote["3770450698"] = new("3770450698", 2, 100, SteamInstallFinder.BannerlordAppId, "Busy");
        var busy = Assert.Single(ModuleResolver.Resolve(profile, Game, workshops, remote, files).Modules);
        Assert.Equal(ModuleState.Ready, busy.State);
        Assert.Contains("could not verify", busy.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain("hidden", busy.Detail, StringComparison.Ordinal);

        remote["3770450698"] = new("3770450698", 1, 100, 123, "Other");
        Assert.Equal(ModuleState.WrongGame, Assert.Single(ModuleResolver.Resolve(profile, Game, workshops, remote, files).Modules).State);
    }

    [Fact]
    public void Pinned_module_is_ready_without_steam_metadata()
    {
        var files = InstalledGame();
        var resolution = ModuleResolver.Resolve(
            SampleProfile(checkVersions: true, pinned: true),
            Game,
            Workshops(files, 1),
            remote: null,
            files);
        Assert.Equal(ModuleState.Ready, Assert.Single(resolution.Modules).State);
    }

    [Fact]
    public void Workshop_response_reads_file_details()
    {
        var files = SteamWorkshopClient.Parse("""
            {
              "response": {
                "result": 1,
                "publishedfiledetails": [{
                  "publishedfileid": "3770450698",
                  "result": 1,
                  "time_updated": 123,
                  "consumer_app_id": 261550,
                  "title": "Bannerlord Coop"
                }]
              }
            }
            """);
        var file = Assert.Single(files);
        Assert.Equal("3770450698", file.PublishedFileId);
        Assert.Equal(123, file.TimeUpdated);
        Assert.Equal(261550, file.ConsumerAppId);
    }

    [Fact]
    public void Profile_without_coop_cannot_connect()
    {
        var profile = ProfileJson.Parse("""
            {
              "Host": "example.com",
              "Port": 4200,
              "BaseModules": ["Native"],
              "Mods": [{ "Name": "Harmony", "WorkshopId": "5", "ModuleId": "Bannerlord.Harmony", "Enabled": true }]
            }
            """);
        var files = InstalledGame(includeWorkshop: false);
        var resolution = ModuleResolver.Resolve(profile, Game, Array.Empty<WorkshopLibrary>(), null, files);
        Assert.Equal(
            "The enabled module list must include Coop or CoopNightly.",
            ModuleResolver.ConnectBlockReason(resolution));
    }

    private const string Game = @"C:\game";
    private const string WorkshopRoot = @"C:\steam\steamapps\workshop\content\261550";

    private static MemoryFileSystem InstalledGame(bool includeWorkshop = true, string moduleId = "CoopNightly")
    {
        var files = new MemoryFileSystem();
        files.AddFile(Path.Combine(Game, "bin", "Win64_Shipping_Client", "Bannerlord.exe"), "");
        files.AddFile(Path.Combine(Game, "Modules", "Native", "SubModule.xml"), ModuleXml("Native", "1.0"));
        if (includeWorkshop)
        {
            files.AddFile(
                Path.Combine(WorkshopRoot, "3770450698", "SubModule.xml"),
                ModuleXml(moduleId, "1.0"));
        }

        return files;
    }

    private static IReadOnlyList<WorkshopLibrary> Workshops(MemoryFileSystem files, long localTime)
    {
        _ = files;
        return new[]
        {
            new WorkshopLibrary(
                WorkshopRoot,
                new Dictionary<string, long> { ["3770450698"] = localTime },
                ManifestParsed: true)
        };
    }

    private static ServerProfile SampleProfile(bool checkVersions = false, bool pinned = false) =>
        ProfileJson.Parse($$"""
            {
              "Name": "CALRADIA",
              "PackVersion": "0.1.0",
              "Host": "127.0.0.1",
              "Port": 4200,
              "MissionPort": 4201,
              "CheckModVersions": {{(checkVersions ? "true" : "false")}},
              "StartSteamAutomatically": true,
              "BaseModules": ["Native"],
              "Mods": [{
                "Name": "Bannerlord Coop",
                "WorkshopId": "3770450698",
                "ModuleId": "CoopNightly",
                "Enabled": true,
                "RequiredVersion": "",
                "Pinned": {{(pinned ? "true" : "false")}}
              }],
              "AutoConnect": true,
              "ModuleLoadOrder": ["Native", "CoopNightly"]
            }
            """);

    [Fact]
    public void Settlement_bridge_mods_load_after_the_existing_pack()
    {
        var profile = ProfileJson.Parse("""
            {
              "Name": "CALRADIA",
              "Port": 4200,
              "BaseModules": ["Native", "SandBoxCore", "SandBox", "StoryMode"],
              "Mods": [{ "Name": "Bannerlord Coop", "WorkshopId": "3770450698", "ModuleId": "CoopNightly", "Enabled": true }],
              "ModuleLoadOrder": ["Native", "CoopNightly"]
            }
            """);

        SettlementBridge.Ensure(profile);

        Assert.Contains(profile.Mods, mod => mod.ModuleId == "AgricultureEstate" && mod.Enabled);
        Assert.Contains(profile.Mods, mod => mod.ModuleId == "PlayerSettlement" && mod.Enabled);
        Assert.Contains(profile.Mods, mod => mod.ModuleId == "PSCoopBridge" && mod.Pinned);
        Assert.Equal("AgricultureEstate", profile.ModuleLoadOrder[^3]);
        Assert.Equal("PlayerSettlement", profile.ModuleLoadOrder[^2]);
        Assert.Equal("PSCoopBridge", profile.ModuleLoadOrder[^1]);
    }

    [Fact]
    public void Stale_battle_size_adapter_is_removed()
    {
        var root = Path.Combine(Path.GetTempPath(), "coop-fps-" + Guid.NewGuid().ToString("N"));
        var adapters = Path.Combine(root, "bin", "Win64_Shipping_Client", "Adapters");
        Directory.CreateDirectory(adapters);
        var stale = Path.Combine(adapters, "Adapter.Native.dll");
        var keep = Path.Combine(adapters, "Adapter.ImprovedGarrisons.dll");
        File.WriteAllText(stale, "stale");
        File.WriteAllText(keep, "keep");

        var removed = CoopFpsPatch.RemoveStaleBattleSizeAdapter([root]);

        Assert.Equal(new[] { stale }, removed);
        Assert.False(File.Exists(stale));
        Assert.True(File.Exists(keep));
        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public void Locked_client_list_leaves_off_modes_the_dedicated_server_will_not_load()
    {
        var profile = new ServerProfile();
        ServerModuleSet.Apply(profile);

        Assert.DoesNotContain("StoryMode", profile.BaseModules);
        Assert.DoesNotContain("CustomBattle", profile.BaseModules);
        Assert.DoesNotContain(profile.ModuleLoadOrder, id => id is "StoryMode" or "CustomBattle");
        Assert.Equal("Coop", profile.ModuleLoadOrder[profile.ModuleLoadOrder.IndexOf("SandBox") + 1]);
        Assert.True(profile.ModuleLoadOrder.IndexOf("BannerFix") < profile.ModuleLoadOrder.IndexOf("RaiseYourBanner"));
        Assert.Contains(profile.Mods, mod => mod.ModuleId == "RaiseYourTorch" && mod.Pinned);
        Assert.Contains(profile.ModuleLoadOrder, id => id == "MoreNationsRemastered");
        Assert.Contains(profile.ModuleLoadOrder, id => id == "MoreNationsRemasteredFix");
        Assert.DoesNotContain(profile.Mods, mod => mod.ModuleId is "CoopMarriage" or "FamilyAppearanceEditor");
        Assert.DoesNotContain(profile.ModuleLoadOrder, id => id is "CoopMarriage" or "FamilyAppearanceEditor");
    }

    [Fact]
    public void Locked_client_launch_keeps_story_mode_and_custom_battle_after_sandbox()
    {
        var profile = new ServerProfile();
        ServerModuleSet.Apply(profile);
        ClientCampaignModes.KeepOnClient(profile);

        Assert.Equal("StoryMode", profile.ModuleLoadOrder[profile.ModuleLoadOrder.IndexOf("SandBox") + 1]);
        Assert.Equal("CustomBattle", profile.ModuleLoadOrder[profile.ModuleLoadOrder.IndexOf("StoryMode") + 1]);
        Assert.Equal("Coop", profile.ModuleLoadOrder[profile.ModuleLoadOrder.IndexOf("CustomBattle") + 1]);
        Assert.Contains("StoryMode", profile.BaseModules);
        Assert.Contains("CustomBattle", profile.BaseModules);
        Assert.DoesNotContain("StoryMode", ServerModuleSet.WorkingOrder(true));
        Assert.DoesNotContain("CustomBattle", ServerModuleSet.WorkingOrder(true));
    }

    [Fact]
    public void Extra_mods_sort_below_the_working_list_and_report_a_missing_need()
    {
        var working = new[] { "Harmony", "Coop" };
        var current = new[] { "Harmony", "Late", "Coop", "Early" };
        var dependencies = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["Late"] = new[] { "Early" },
            ["Early"] = new[] { "MissingMod" }
        };
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Late"] = "Late Mod",
            ["Early"] = "Early Mod"
        };

        var sorted = ExtraDependencyOrder.Sort(current, working, dependencies);
        var problems = ExtraDependencyOrder.Problems(sorted, working, dependencies, names, new HashSet<string>(sorted, StringComparer.OrdinalIgnoreCase));

        Assert.Equal(new[] { "Harmony", "Coop", "Early", "Late" }, sorted);
        Assert.Contains("Early Mod needs MissingMod, which is not in this list. The game can stop while starting.", problems);
    }

    [Fact]
    public void Coop_on_the_list_satisfies_a_CoopNightly_dependency()
    {
        var problems = ExtraDependencyOrder.Problems(
            ["Harmony", "Coop", "CoopSycn"],
            ["Harmony", "Coop"],
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["CoopSycn"] = ["CoopNightly"]
            },
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Coop"] = "Bannerlord Coop",
                ["CoopSycn"] = "CoopSycn"
            },
            new HashSet<string>(["Harmony", "Coop", "CoopSycn"], StringComparer.OrdinalIgnoreCase));

        Assert.Empty(problems);
    }

    [Fact]
    public void Working_mods_stay_together_and_cannot_be_dragged_apart()
    {
        var working = new[] { "Harmony", "Native", "Coop" };
        var scrambled = new[] { "Extra", "Coop", "Harmony", "Native", "Another" };

        var placed = WorkingLoadOrder.PlaceExtrasBelow(scrambled, working);

        Assert.Equal(new[] { "Harmony", "Native", "Coop", "Extra", "Another" }, placed);
        Assert.False(WorkingLoadOrder.CanMove(placed, 0, 3, working));
        Assert.False(WorkingLoadOrder.CanMove(placed, 4, 1, working));
        Assert.True(WorkingLoadOrder.CanMove(placed, 4, 3, working));
    }

    [Fact]
    public void A_custom_setup_loads_the_map_it_saved()
    {
        var profile = new ServerProfile();
        DefaultClientSet.Apply(profile);
        CampaignMapMods.Apply(profile, includeMoreNations: true);
        Assert.Contains(profile.ModuleLoadOrder, id => id == "MoreNationsRemastered");
        Assert.Contains(profile.ModuleLoadOrder, id => id == "MoreNationsRemasteredFix");

        CampaignMapMods.Apply(profile, includeMoreNations: false);
        Assert.DoesNotContain(profile.ModuleLoadOrder, id => id == "MoreNationsRemastered");
        Assert.DoesNotContain(profile.Mods, mod => mod.ModuleId == "MoreNationsRemasteredFix");
    }

    [Fact]
    public void Locked_setups_sit_under_default_with_their_maps()
    {
        var settings = new LauncherSettings
        {
            ActiveSetup = StableClientSetup.LegacyName,
            Setups =
            [
                new ClientSetup { Name = "Default", CampaignMap = "MoreNations" },
                new ClientSetup { Name = StableClientSetup.LegacyName, CampaignMap = "MoreNations", SteamHostName = "Coodsz" }
            ]
        };

        settings.EnsureSetups();

        Assert.Equal("Default", settings.Setups[0].Name);
        Assert.Equal(StableClientSetup.VanillaName, settings.Setups[1].Name);
        Assert.Equal("Vanilla", settings.Setups[1].CampaignMap);
        Assert.True(settings.Setups[1].Locked);
        Assert.Equal("Coodsz", settings.Setups[1].SteamHostName);
        Assert.Equal(StableClientSetup.MoreNationsName, settings.Setups[2].Name);
        Assert.Equal("MoreNations", settings.Setups[2].CampaignMap);
        Assert.True(settings.Setups[2].Locked);
        Assert.Equal(StableClientSetup.VanillaName, settings.ActiveSetup);
    }

    [Fact]
    public void Client_setups_keep_separate_mod_sets()
    {
        var settings = new LauncherSettings
        {
            CampaignMap = "Vanilla",
            SteamHostName = "HostOne",
            DisabledModules = ["DismembermentPlus"]
        };
        settings.EnsureSetups();
        var copy = settings.Current().Copy(settings.NextSetupName());
        copy.CampaignMap = "MoreNations";
        copy.DisabledModules.Clear();
        settings.Setups.Add(copy);

        Assert.Equal("Default", settings.Current().Name);
        Assert.Contains("DismembermentPlus", settings.Current().DisabledModules);
        Assert.Empty(copy.DisabledModules);
        Assert.Equal("Setup 2", copy.Name);
        Assert.Equal("Vanilla", settings.Current().CampaignMap);
    }

    [Fact]
    public void Client_load_order_follows_the_saved_setup_order()
    {
        var profile = new ServerProfile();
        ServerModuleSet.Apply(profile);
        ClientLoadOrder.Apply(profile, ["PSCoopBridge", "Bannerlord.Harmony", "Coop"]);

        Assert.Equal("PSCoopBridge", profile.ModuleLoadOrder[0]);
        Assert.Equal("Bannerlord.Harmony", profile.ModuleLoadOrder[1]);
        Assert.Equal("Coop", profile.ModuleLoadOrder[2]);
        Assert.Equal(profile.ModuleLoadOrder.Count, profile.ModuleLoadOrder.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void Add_mods_search_stays_alphabetical()
    {
        var mods = new[]
        {
            new FoundMod("Zebra Mod", "Zebra", "0", "z", "Workshop"),
            new FoundMod("Open Source Saddlery", "OpenSourceSaddlery", "1", "a", "Workshop"),
            new FoundMod("Diplomacy", "Bannerlord.Diplomacy", "2", "b", "From Nexus or ModDB")
        };

        var all = CustomModCatalog.Matching(mods, "");
        Assert.Equal(new[] { "Diplomacy", "Open Source Saddlery", "Zebra Mod" }, all.Select(mod => mod.Name).ToArray());

        var found = CustomModCatalog.Matching(mods, "source sad");
        Assert.Equal("Open Source Saddlery", Assert.Single(found).Name);
        Assert.Empty(CustomModCatalog.Matching(mods, "not a mod"));
    }

    [Fact]
    public void Add_mods_finds_modules_already_in_the_game_folder()
    {
        var root = Path.Combine(Path.GetTempPath(), "coodsz-scan-" + Guid.NewGuid().ToString("N"));
        var launcher = Path.Combine(root, "launcher");
        var modules = Path.Combine(root, "game", "Modules");
        var workshop = Path.Combine(root, "workshop");
        Directory.CreateDirectory(Path.Combine(launcher, "DroppedMod"));
        Directory.CreateDirectory(Path.Combine(modules, "RealisticWeather"));
        Directory.CreateDirectory(Path.Combine(modules, "Native"));
        Directory.CreateDirectory(Path.Combine(modules, "BirthAndDeath"));
        Directory.CreateDirectory(Path.Combine(workshop, "111"));
        File.WriteAllText(Path.Combine(launcher, "DroppedMod", "SubModule.xml"), ModuleXml("DroppedMod", "1"));
        File.WriteAllText(Path.Combine(modules, "RealisticWeather", "SubModule.xml"), ModuleXml("RealisticWeather", "1"));
        File.WriteAllText(Path.Combine(modules, "Native", "SubModule.xml"), ModuleXml("Native", "1"));
        File.WriteAllText(Path.Combine(modules, "BirthAndDeath", "SubModule.xml"), ModuleXml("BirthAndDeath", "1"));
        File.WriteAllText(Path.Combine(workshop, "111", "SubModule.xml"), ModuleXml("WorkshopMod", "1"));

        try
        {
            var found = CustomModCatalog.Scan(launcher, [new WorkshopLibrary(workshop, new Dictionary<string, long>(), true)], modules);
            Assert.Contains(found, mod => mod.ModuleId == "RealisticWeather" && mod.Source == "From Nexus or ModDB  —  auto detected in your module folder" && mod.WorkshopId == "0");
            Assert.Contains(found, mod => mod.ModuleId == "DroppedMod" && mod.Source == "From Nexus or ModDB");
            Assert.Contains(found, mod => mod.ModuleId == "WorkshopMod" && mod.Source == "Workshop" && mod.WorkshopId == "111");
            Assert.DoesNotContain(found, mod => mod.ModuleId is "Native" or "BirthAndDeath");
            Assert.Equal(new[] { "DroppedMod", "RealisticWeather", "WorkshopMod" }, found.Select(mod => mod.ModuleId));

            Directory.CreateDirectory(Path.Combine(workshop, "2859265386"));
            Directory.CreateDirectory(Path.Combine(workshop, "2859188632"));
            Directory.CreateDirectory(Path.Combine(workshop, "3806450944"));
            File.WriteAllText(Path.Combine(workshop, "2859265386", "SubModule.xml"), ModuleXml("ImprovedGarrisons", "1"));
            File.WriteAllText(Path.Combine(workshop, "2859188632", "SubModule.xml"), ModuleXml("Bannerlord.Harmony", "1"));
            File.WriteAllText(Path.Combine(workshop, "3806450944", "SubModule.xml"), ModuleXml("MoreNationsRemastered", "1"));
            var again = CustomModCatalog.Scan(launcher, [new WorkshopLibrary(workshop, new Dictionary<string, long>(), true)], modules);
            Assert.Contains(again, mod => mod.ModuleId == "ImprovedGarrisons" && mod.Source == "Workshop" && mod.WorkshopId == "2859265386");
            Assert.Contains(again, mod => mod.ModuleId == "MoreNationsRemastered" && mod.WorkshopId == "3806450944");
            Assert.DoesNotContain(again, mod => mod.ModuleId is "Bannerlord.Harmony" or "Native");

            var profile = new ServerProfile();
            DefaultClientSet.Apply(profile);
            CustomModCatalog.Apply(profile, [new CustomMod { Name = "Improved Garrisons", ModuleId = "ImprovedGarrisons", WorkshopId = "2859265386" }], locked: false);
            Assert.Contains(profile.Mods, mod => mod.ModuleId == "ImprovedGarrisons" && mod.Enabled && mod.WorkshopId == "2859265386");
            Assert.Contains(profile.ModuleLoadOrder, id => id == "ImprovedGarrisons");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void A_custom_setup_can_use_mods_from_the_locked_setup()
    {
        var root = Path.Combine(Path.GetTempPath(), "coodsz-custom-pack-" + Guid.NewGuid().ToString("N"));
        var launcher = Path.Combine(root, "launcher");
        var modules = Path.Combine(root, "game", "Modules");
        var workshop = Path.Combine(root, "workshop");
        Directory.CreateDirectory(launcher);
        var addable = new (string Id, string WorkshopId)[]
        {
            ("XorberaxLegacy", "2875138158"),
            ("SettlementCultureDrift", "3777470096"),
            ("CoopModPatch", "3786391685"),
            ("ImprovedGarrisons", "2859265386"),
            ("ServerSettingsLock", "3794802607"),
            ("RTSCamera", "3747725551"),
            ("RTSCamera.CommandSystem", "3747771970"),
            ("DismembermentPlus", "2875093027"),
            ("AgricultureEstate", "2880158010"),
            ("PlayerSettlement", "3720376888"),
            ("MoreNationsRemastered", "3806450944"),
            ("MoreNationsRemasteredFix", "3806450713"),
            ("BannerFix", "0"),
            ("RaiseYourBanner", "0"),
            ("RaiseYourTorch", "0"),
            ("PerfectFireArrows", "0"),
            ("AliveScenes", "0"),
            ("ImmersiveBattlefields", "0"),
            ("PSCoopBridge", "0")
        };
        var hidden = new (string Id, string WorkshopId)[]
        {
            ("Bannerlord.Harmony", "2859188632"),
            ("Bannerlord.ButterLib", "2859232415"),
            ("Coop", "3770450698"),
            ("CoopNightly", "9001"),
            ("Native", "0"),
            ("BirthAndDeath", "0")
        };

        try
        {
            foreach (var mod in addable.Concat(hidden))
            {
                var directory = mod.WorkshopId == "0"
                    ? Path.Combine(modules, mod.Id)
                    : Path.Combine(workshop, mod.WorkshopId);
                Directory.CreateDirectory(directory);
                File.WriteAllText(Path.Combine(directory, "SubModule.xml"), ModuleXml(mod.Id, "1"));
            }

            var found = CustomModCatalog.Scan(launcher, [new WorkshopLibrary(workshop, new Dictionary<string, long>(), true)], modules);
            foreach (var mod in addable)
            {
                Assert.Contains(found, item => item.ModuleId == mod.Id && item.WorkshopId == mod.WorkshopId);
            }

            Assert.DoesNotContain(found, item => hidden.Any(mod => mod.Id == item.ModuleId));

            var profile = new ServerProfile();
            DefaultClientSet.Apply(profile);
            var picked = new[]
            {
                new CustomMod { Name = "Player Settlement", ModuleId = "PlayerSettlement", WorkshopId = "3720376888" },
                new CustomMod { Name = "Improved Garrisons", ModuleId = "ImprovedGarrisons", WorkshopId = "2859265386" },
                new CustomMod { Name = "PSCoopBridge", ModuleId = "PSCoopBridge", WorkshopId = "0" },
                new CustomMod { Name = "RTS Camera", ModuleId = "RTSCamera", WorkshopId = "3747725551" },
                new CustomMod { Name = "Harmony", ModuleId = "Bannerlord.Harmony", WorkshopId = "2859188632" },
                new CustomMod { Name = "More Nations Remastered", ModuleId = "MoreNationsRemastered", WorkshopId = "3806450944" }
            };
            CustomModCatalog.Apply(profile, picked, locked: false);
            profile.ModuleLoadOrder = WorkingLoadOrder.PlaceExtrasBelow(profile.ModuleLoadOrder, DefaultClientSet.Fixed);
            RemovedModLaunch.Drop(profile, DefaultClientSet.NotOnNewSetup.Where(id => !id.Equals("PSCoopBridge", StringComparison.OrdinalIgnoreCase)));

            Assert.Contains(profile.Mods, mod => mod.ModuleId == "PlayerSettlement" && mod.Enabled && mod.WorkshopId == "3720376888");
            Assert.Contains(profile.Mods, mod => mod.ModuleId == "ImprovedGarrisons" && mod.Enabled && mod.WorkshopId == "2859265386");
            Assert.Contains(profile.Mods, mod => mod.ModuleId == "PSCoopBridge" && mod.Enabled);
            Assert.Contains(profile.Mods, mod => mod.ModuleId == "RTSCamera" && mod.Enabled && mod.WorkshopId == "3747725551");
            Assert.Contains(profile.Mods, mod => mod.ModuleId == "MoreNationsRemastered" && mod.Enabled && mod.WorkshopId == "3806450944");
            Assert.Contains(profile.Mods, mod => mod.ModuleId == "Coop" && mod.Enabled);
            Assert.DoesNotContain(profile.Mods, mod => mod.ModuleId == "CoopModPatch");
            Assert.Single(profile.Mods, mod => mod.ModuleId == "Bannerlord.Harmony");
            Assert.Single(profile.Mods, mod => mod.ModuleId == "PlayerSettlement");
            Assert.Single(profile.Mods, mod => mod.ModuleId == "MoreNationsRemastered");
            var nativeAt = profile.ModuleLoadOrder.FindIndex(id => id == "Native");
            foreach (var id in new[] { "PlayerSettlement", "ImprovedGarrisons", "PSCoopBridge", "RTSCamera", "MoreNationsRemastered" })
            {
                Assert.Contains(id, profile.ModuleLoadOrder);
                Assert.True(profile.ModuleLoadOrder.FindIndex(item => item == id) > nativeAt);
            }

            CustomModCatalog.Apply(profile, picked, locked: false);
            Assert.Single(profile.Mods, mod => mod.ModuleId == "PlayerSettlement");

            var locked = new ServerProfile();
            ServerModuleSet.Apply(locked, includeMoreNations: false);
            var lockedCount = locked.Mods.Count;
            CustomModCatalog.Apply(locked, [new CustomMod { Name = "Fast Mode", ModuleId = "FastMode", WorkshopId = "0" }], locked: true);
            Assert.Equal(lockedCount, locked.Mods.Count);
            Assert.DoesNotContain(locked.Mods, mod => mod.ModuleId == "FastMode");
            Assert.Contains(locked.Mods, mod => mod.ModuleId == "PlayerSettlement");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Installed_locked_pack_mods_show_in_add_mods_and_the_workshop_is_left_alone()
    {
        var steam = SteamRegistry.TryGetSteamPath();
        if (string.IsNullOrWhiteSpace(steam))
            return;
        var libraryFile = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
        if (!File.Exists(libraryFile))
            return;

        var files = new PhysicalFileSystem();
        var libraries = SteamInstallFinder.ReadLibraryPaths(steam, File.ReadAllText(libraryFile));
        var workshops = SteamInstallFinder.ReadWorkshopLibraries(libraries, files);
        if (workshops.Count == 0)
            return;

        var game = SteamInstallFinder.FindGameRoot(steam, files);
        var modules = game == null ? null : Path.Combine(game, "Modules");
        var watched = workshops
            .Select(library => library.ContentDirectory)
            .Append(modules)
            .Where(directory => !string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
            .Select(directory => new DirectoryInfo(directory!))
            .ToList();
        // An empty launcher folder, not the whole temp folder, where other tests add and delete fake modules mid scan.
        var launcher = Path.Combine(Path.GetTempPath(), "bcl-live-scan-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(launcher);
        IReadOnlyList<FoundMod> found = [];
        try
        {
            // Steam or another chat's live test can touch the real Workshop or Modules folder during a scan.
            // A scan that writes changes a stamp on every try, so one clean try out of three shows it left them alone.
            var untouched = false;
            for (var attempt = 0; attempt < 3 && !untouched; attempt++)
            {
                foreach (var directory in watched)
                    directory.Refresh();
                var before = watched.Select(directory => directory.LastWriteTimeUtc).ToList();

                found = CustomModCatalog.Scan(launcher, workshops, modules);

                foreach (var directory in watched)
                    directory.Refresh();
                untouched = watched.Select(directory => directory.LastWriteTimeUtc).SequenceEqual(before);
            }
            Assert.True(untouched, "The scan changed the Workshop or Modules folder on every try.");
        }
        finally
        {
            Directory.Delete(launcher, recursive: true);
        }

        Assert.DoesNotContain(found, mod => mod.ModuleId is "Bannerlord.Harmony" or "Bannerlord.ButterLib" or "Bannerlord.UIExtenderEx" or "Bannerlord.MBOptionScreen" or "Native" or "SandBoxCore" or "SandBox" or "StoryMode" or "CustomBattle" or "Coop" or "CoopNightly" or "BirthAndDeath" or "DedicatedServer.Windows");

        var expected = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["2875138158"] = "XorberaxLegacy",
            ["3777470096"] = "SettlementCultureDrift",
            ["3786391685"] = "CoopModPatch",
            ["2859265386"] = "ImprovedGarrisons",
            ["3794802607"] = "ServerSettingsLock",
            ["3747725551"] = "RTSCamera",
            ["3747771970"] = "RTSCamera.CommandSystem",
            ["2875093027"] = "DismembermentPlus",
            ["2880158010"] = "AgricultureEstate",
            ["3720376888"] = "PlayerSettlement",
            ["3806450944"] = "MoreNationsRemastered",
            ["3806450713"] = "MoreNationsRemasteredFix"
        };
        var present = 0;
        foreach (var library in workshops)
        {
            foreach (var pair in expected)
            {
                if (!Directory.Exists(Path.Combine(library.ContentDirectory, pair.Key)))
                    continue;
                present++;
                Assert.Contains(found, mod => mod.ModuleId == pair.Value && mod.WorkshopId == pair.Key && mod.Source == "Workshop");
            }
        }

        foreach (var id in new[] { "BannerFix", "RaiseYourBanner", "RaiseYourTorch", "PerfectFireArrows", "AliveScenes", "ImmersiveBattlefields", "PSCoopBridge" })
        {
            if (modules == null || !File.Exists(Path.Combine(modules, id, "SubModule.xml")))
                continue;
            present++;
            // A mod in the Modules folder that this PC also has from the Workshop shows once, as the Workshop copy
            // (PSCoopBridge on the test PC is both), so either copy counts.
            Assert.Contains(found, mod => mod.ModuleId == id && (mod.WorkshopId == "0" || mod.Source == "Workshop"));
        }

        Assert.True(present > 0, "None of the locked-setup mods were installed, so the live scan had nothing to check.");
    }

    [WindowsFact]
    public void Workshop_module_ids_may_contain_spaces()
    {
        var mod = new ProfileMod
        {
            Name = "Swadian Armoury",
            ModuleId = "swadian armoury",
            WorkshopId = "2875090166"
        };
        Assert.True(ProfileJson.IsValidMod(mod));
        Assert.False(ProfileJson.IsSafeModuleId("..\\Native"));

        var profile = new ServerProfile { Port = 4200 };
        ServerModuleSet.Apply(profile);
        CustomModCatalog.Apply(profile, [new CustomMod { Name = mod.Name, ModuleId = mod.ModuleId, WorkshopId = mod.WorkshopId }], locked: false);
        Assert.Contains(profile.Mods, item => item.ModuleId == "swadian armoury" && item.WorkshopId == "2875090166" && item.Enabled);
        ProfileJson.Validate(profile);
    }

    [Fact]
    public void Optional_client_mods_can_be_turned_off()
    {
        var profile = new ServerProfile();
        ServerModuleSet.Apply(profile);
        ClientModToggles.Apply(profile, ["DismembermentPlus", "PlayerSettlement", "Bannerlord.Harmony", "Coop"]);

        Assert.Contains(profile.Mods, mod => mod.ModuleId == "DismembermentPlus" && !mod.Enabled);
        Assert.Contains(profile.Mods, mod => mod.ModuleId == "PlayerSettlement" && !mod.Enabled);
        Assert.Contains(profile.Mods, mod => mod.ModuleId == "AgricultureEstate" && !mod.Enabled);
        Assert.Contains(profile.Mods, mod => mod.ModuleId == "PSCoopBridge" && !mod.Enabled);
        Assert.Contains(profile.Mods, mod => mod.ModuleId == "Bannerlord.Harmony" && mod.Enabled);
        Assert.Contains(profile.Mods, mod => mod.ModuleId == "Coop" && mod.Enabled);
        Assert.DoesNotContain(profile.ModuleLoadOrder, id => id is "DismembermentPlus" or "PlayerSettlement" or "AgricultureEstate" or "PSCoopBridge");
        Assert.False(ClientModToggles.CanToggle("Coop"));
        Assert.True(ClientModToggles.CanToggle("XorberaxLegacy"));
    }

    [Fact]
    public void Vanilla_client_uses_the_base_map()
    {
        var profile = new ServerProfile();
        ServerModuleSet.Apply(profile, includeMoreNations: false);

        Assert.DoesNotContain(profile.Mods, mod => mod.ModuleId is "MoreNationsRemastered" or "MoreNationsRemasteredFix");
        Assert.DoesNotContain(profile.ModuleLoadOrder, id => id is "MoreNationsRemastered" or "MoreNationsRemasteredFix");
        Assert.Contains("Coop", profile.ModuleLoadOrder);
        // Without the More Nations mods, Improved Garrisons now sits right above RTS Camera (Settings Lock left the setup).
        Assert.Equal("ImprovedGarrisons", profile.ModuleLoadOrder[profile.ModuleLoadOrder.IndexOf("RTSCamera") - 1]);
        Assert.DoesNotContain(profile.Mods, mod => mod.ModuleId == "ServerSettingsLock");
    }

    [Fact]
    public void Player_settlement_gettypes_scan_is_redirected_to_harmony()
    {
        var root = Path.Combine(Path.GetTempPath(), "ps-patch-" + Guid.NewGuid().ToString("N"));
        var bin = Path.Combine(root, "bin", "Win64_Shipping_Client");
        Directory.CreateDirectory(bin);
        var dll = Path.Combine(bin, "PlayerSettlement.dll");
        WriteUnsafeGetTypesAssembly(dll);

        var batch = PlayerSettlementCompatPatch.Apply([root]);
        var again = PlayerSettlementCompatPatch.Apply([root]);

        Assert.Equal(new[] { dll }, batch.Patched);
        Assert.Empty(again.Patched);
        Assert.Equal(PatchStatus.AlreadyPatched, PlayerSettlementCompatPatch.PatchAssembly(dll));
        using var module = ModuleDefinition.ReadModule(dll);
        var calls = module.Types.SelectMany(type => type.Methods)
            .SelectMany(method => method.Body.Instructions)
            .Select(instruction => instruction.Operand)
            .OfType<MethodReference>()
            .ToList();
        Assert.Contains(calls, call => call.Name == "GetTypesFromAssembly" && call.DeclaringType.FullName == "HarmonyLib.AccessTools");
        Assert.DoesNotContain(calls, call => call.Name == "GetTypes" && call.DeclaringType.FullName == "System.Reflection.Assembly");
        module.Dispose();
        Directory.Delete(root, recursive: true);
    }

    private static void WriteUnsafeGetTypesAssembly(string path)
    {
        var assembly = AssemblyDefinition.CreateAssembly(
            new AssemblyNameDefinition("PlayerSettlement", new Version(7, 5, 0, 0)),
            "PlayerSettlement",
            ModuleKind.Dll);
        var module = assembly.MainModule;
        module.AssemblyReferences.Add(new AssemblyNameReference("0Harmony", new Version(2, 4, 2, 0)));
        var type = new TypeDefinition(
            "BannerlordPlayerSettlement",
            "Scan",
            TypeAttributes.Public | TypeAttributes.Class,
            module.TypeSystem.Object);
        module.Types.Add(type);
        var method = new MethodDefinition(
            "Load",
            MethodAttributes.Public | MethodAttributes.Static,
            module.TypeSystem.Void);
        type.Methods.Add(method);
        var systemType = new TypeReference("System", "Type", module, module.TypeSystem.CoreLibrary);
        var assemblyType = new TypeReference("System.Reflection", "Assembly", module, module.TypeSystem.CoreLibrary);
        var getTypes = new MethodReference("GetTypes", new ArrayType(systemType), assemblyType) { HasThis = true };
        var il = method.Body.GetILProcessor();
        il.Append(il.Create(OpCodes.Ldnull));
        il.Append(il.Create(OpCodes.Callvirt, module.ImportReference(getTypes)));
        il.Append(il.Create(OpCodes.Pop));
        il.Append(il.Create(OpCodes.Ret));
        assembly.Write(path);
    }

    [Fact]
    public void Bundled_modules_install_only_when_the_game_copy_is_missing()
    {
        var root = Path.Combine(Path.GetTempPath(), "bundled-mods-" + Guid.NewGuid().ToString("N"));
        var launcher = Path.Combine(root, "launcher");
        var game = Path.Combine(root, "game");
        Directory.CreateDirectory(Path.Combine(launcher, "PSCoopBridge"));
        File.WriteAllText(Path.Combine(launcher, "PSCoopBridge", "SubModule.xml"), "<Module/>");
        File.WriteAllText(Path.Combine(launcher, "PSCoopBridge", "note.txt"), "bridge");

        var first = BundledModules.InstallMissing(launcher, game);
        File.WriteAllText(Path.Combine(game, "Modules", "PSCoopBridge", "note.txt"), "local");
        var second = BundledModules.InstallMissing(launcher, game);

        Assert.Equal(new[] { "PSCoopBridge" }, first);
        Assert.Empty(second);
        Assert.Equal("local", File.ReadAllText(Path.Combine(game, "Modules", "PSCoopBridge", "note.txt")));

        Directory.CreateDirectory(Path.Combine(launcher, "Coop"));
        File.WriteAllText(Path.Combine(launcher, "Coop", "SubModule.xml"), ModuleXml("Coop", "v0.1.5"));
        var existing = Path.Combine(game, "Modules", "Bannerlord Coop");
        Directory.CreateDirectory(existing);
        File.WriteAllText(Path.Combine(existing, "SubModule.xml"), ModuleXml("Coop", "v0.1.5"));
        File.WriteAllText(Path.Combine(existing, "keep.txt"), "keep");
        Assert.Empty(BundledModules.InstallMissing(launcher, game));
        Assert.False(Directory.Exists(Path.Combine(game, "Modules", "Coop")));
        Assert.Equal("keep", File.ReadAllText(Path.Combine(existing, "keep.txt")));
        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public void Slow_motion_is_turned_off_in_dismemberment_and_rts_camera()
    {
        var root = Path.Combine(Path.GetTempPath(), "slow-motion-" + Guid.NewGuid().ToString("N"));
        var modules = Path.Combine(root, "Modules");
        var workshop = Path.Combine(root, "workshop", "2875093027");
        var documents = Path.Combine(root, "documents");
        Directory.CreateDirectory(Path.Combine(modules, "DismembermentPlus"));
        Directory.CreateDirectory(workshop);
        File.WriteAllText(Path.Combine(modules, "DismembermentPlus", "DismembermentPlus.json"), "{ \"SlowMotion\": true, \"HeadChance\": 1.0 }");
        File.WriteAllText(Path.Combine(workshop, "DismembermentPlus.json"), "{ \"SlowMotion\": true }");
        Directory.CreateDirectory(Path.Combine(documents, "Configs", "RTSCamera"));
        File.WriteAllText(
            Path.Combine(documents, "Configs", "RTSCamera", "RTSCameraConfig.xml"),
            "<RTSCameraConfig><SlowMotionMode>true</SlowMotionMode><SlowMotionOnRtsView>true</SlowMotionOnRtsView></RTSCameraConfig>");

        var first = SlowMotionGuard.Apply([modules, Path.Combine(root, "workshop")], documents);
        var second = SlowMotionGuard.Apply([modules, Path.Combine(root, "workshop")], documents);

        Assert.True(first.Dismemberment);
        Assert.True(first.RtsCamera);
        Assert.False(second.Dismemberment);
        Assert.False(second.RtsCamera);
        Assert.Contains("\"SlowMotion\": false", File.ReadAllText(Path.Combine(modules, "DismembermentPlus", "DismembermentPlus.json")));
        Assert.Contains("\"HeadChance\": 1.0", File.ReadAllText(Path.Combine(modules, "DismembermentPlus", "DismembermentPlus.json")));
        Assert.Contains("\"SlowMotion\": false", File.ReadAllText(Path.Combine(workshop, "DismembermentPlus.json")));
        Assert.Contains("\"SlowMotion\": false", File.ReadAllText(Path.Combine(documents, "Configs", "ModSettings", "Global", "DismembermentPlus", "DismembermentPlus.json")));
        var camera = File.ReadAllText(Path.Combine(documents, "Configs", "RTSCamera", "RTSCameraConfig.xml"));
        Assert.Contains("<SlowMotionMode>false</SlowMotionMode>", camera);
        Assert.Contains("<SlowMotionOnRtsView>false</SlowMotionOnRtsView>", camera);
        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public void Coop_connect_uses_the_launcher_password_when_the_token_is_empty()
    {
        var root = Path.Combine(Path.GetTempPath(), "coop-password-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(root, "Coop.Core.dll");
        Directory.CreateDirectory(root);
        WriteCoopClient(path);

        Assert.Equal(PatchStatus.Patched, CoopPasswordPatch.PatchAssembly(path));
        Assert.Equal(PatchStatus.AlreadyPatched, CoopPasswordPatch.PatchAssembly(path));

        using var module = ModuleDefinition.ReadModule(path);
        var start = module.Types.Single(type => type.Name == "CoopClient").Methods.Single(method => method.Name == "Start");
        Assert.Contains(start.Body.Instructions, instruction =>
            instruction.Operand is MethodReference called && called.Name == CoopPasswordPatch.HelperName);
        module.Dispose();
        Directory.Delete(root, recursive: true);
    }

    private static void WriteCoopClient(string path)
    {
        var assembly = AssemblyDefinition.CreateAssembly(
            new AssemblyNameDefinition("Coop.Core", new Version(1, 0, 0, 0)),
            "Coop.Core",
            ModuleKind.Dll);
        var module = assembly.MainModule;
        var client = new TypeDefinition("Coop.Core.Client", "CoopClient", TypeAttributes.Public, module.TypeSystem.Object);
        var config = new TypeDefinition("Common.Network", "INetworkConfig", TypeAttributes.Public | TypeAttributes.Interface, null);
        var getToken = new MethodDefinition("get_Token", MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.Abstract | MethodAttributes.HideBySig, module.TypeSystem.String);
        config.Methods.Add(getToken);
        var net = new TypeDefinition("LiteNetLib", "NetManager", TypeAttributes.Public, module.TypeSystem.Object);
        var connect = new MethodDefinition("Connect", MethodAttributes.Public, module.TypeSystem.Void);
        connect.Parameters.Add(new ParameterDefinition(module.TypeSystem.String));
        net.Methods.Add(connect);
        var start = new MethodDefinition("Start", MethodAttributes.Public, module.TypeSystem.Void);
        client.Methods.Add(start);
        module.Types.Add(client);
        module.Types.Add(config);
        module.Types.Add(net);
        var il = start.Body.GetILProcessor();
        il.Append(il.Create(OpCodes.Ldnull));
        il.Append(il.Create(OpCodes.Callvirt, getToken));
        il.Append(il.Create(OpCodes.Call, connect));
        il.Append(il.Create(OpCodes.Ret));
        assembly.Write(path);
    }

    [Fact]
    public void Startup_test_names_the_extra_mod_to_turn_off()
    {
        var log = """
            [02:36:25] AccessTools2.GetTypesFromAssembly: assembly Serilog => System.Reflection.ReflectionTypeLoadException: noise
            [02:40:01] ChaosBattle failed: System.MissingMethodException: Method not found in ChaosBattle.SubModule
            [02:40:01]    at ChaosBattle.SubModule.OnSubModuleLoad()
            """;
        var lines = StartupCrashExplain.Explain(
            false,
            -1073741819,
            "Exit code: -1073741819",
            log,
            [new StartupCrashExplain.ModName("ChaosBattle", "Chaos Battle")],
            ["Harmony", "Coop", "PSCoopBridge"]);

        Assert.Contains(lines, line => line.Contains("The crash says:") && line.Contains("MissingMethodException"));
        Assert.Contains(lines, line => line.Contains("Turn Chaos Battle off") && line.Contains("Leave Bannerlord Coop where it is."));
    }

    [Fact]
    public void Startup_test_tells_them_to_add_a_missing_dependency()
    {
        var lines = StartupCrashExplain.Explain(
            false,
            -1,
            "Exit code: -1",
            "Unable to find dependent module: EarlyMod for module LateMod",
            [new StartupCrashExplain.ModName("LateMod", "Late Mod"), new StartupCrashExplain.ModName("EarlyMod", "Early Mod")],
            ["Coop"]);

        Assert.Contains("Late Mod needs Early Mod, and that mod is not loaded. Add Early Mod and leave it on, or turn Late Mod off. Then click Save mods and press Test to main menu again. Leave Bannerlord Coop where it is.", lines);
    }

    [Fact]
    public void A_normal_close_is_not_called_a_startup_crash()
    {
        var lines = StartupCrashExplain.Explain(
            false,
            -1,
            "Exit code: -1",
            "[02:39:49] Deleting Game... OK!\n[02:39:49] Final_cleanup...",
            [new StartupCrashExplain.ModName("ChaosBattle", "Chaos Battle")],
            ["Coop"]);

        Assert.Equal("The game closed normally. This test did not see a startup crash.", Assert.Single(lines));
        Assert.False(StartupCrashExplain.SawStartupCrash(lines));
    }

    [Fact]
    public void A_game_that_quits_on_frame_zero_without_workshop_mods_is_not_a_pass()
    {
        var log = string.Join("\n",
            "[01:55:13.277] Loading xml file: $BASE/Modules/Native/SubModule.xml.",
            "[01:55:13.429] Command Args: /singleplayer _MODULES_*Bannerlord.Harmony*Native*_MODULES_",
            "[01:55:15.803] Loading xml file: $BASE/Data/font_data.xml.",
            "[01:55:15.809] Deleting Game... [01:55:15.809] OK!",
            "[01:55:15.809] Final_cleanup...",
            "[01:55:15.809] Frame# : 0.");

        var lines = StartupCrashExplain.Explain(false, 0, null, log, [], ["Coop"]);

        Assert.Contains("did not see any Steam Workshop mods", Assert.Single(lines));
        Assert.True(StartupCrashExplain.SawStartupCrash(lines));

        var withWorkshop = log.Replace("$BASE/Modules/Native/SubModule.xml", "C:\\Program Files (x86)\\Steam\\steamapps\\workshop\\content\\261550\\2859188632/SubModule.xml");
        Assert.Contains("closed by itself before the main menu", Assert.Single(StartupCrashExplain.Explain(false, 0, null, withWorkshop, [], ["Coop"])));

        var played = log.Replace("Frame# : 0.", "Frame# : 45894.");
        Assert.Equal("The game closed normally. This test did not see a startup crash.", Assert.Single(StartupCrashExplain.Explain(false, 0, null, played, [], ["Coop"])));

        var vanillaSetup = StartupCrashExplain.Explain(false, 0, null, log, [], ["Coop"], setupUsesWorkshop: false);
        Assert.DoesNotContain("Steam Workshop", Assert.Single(vanillaSetup));
        Assert.Contains("closed by itself before the main menu", vanillaSetup[0]);

        var laterFrame = log + "\n[01:59:00.000] Frame# : 3120.";
        Assert.Equal("The game closed normally. This test did not see a startup crash.", Assert.Single(StartupCrashExplain.Explain(false, 0, null, laterFrame, [], ["Coop"])));
    }

    [Fact]
    public void A_startup_crash_is_separate_from_a_normal_close()
    {
        var crashed = StartupCrashExplain.Explain(
            false,
            1,
            null,
            "the game stopped",
            [new StartupCrashExplain.ModName("ChaosBattle", "Chaos Battle")],
            ["Coop"]);
        var stillOpen = StartupCrashExplain.Explain(true, null, null, null, [], ["Coop"]);

        Assert.True(StartupCrashExplain.SawStartupCrash(crashed));
        Assert.False(StartupCrashExplain.SawStartupCrash(stillOpen));
    }

    [Fact]
    public void An_old_shader_cache_names_the_mod_and_the_clear_button()
    {
        var lines = StartupCrashExplain.Explain(
            false,
            1,
            null,
            "rgl_post_warning_line: Shader cache version of the external module (OpenSourceSaddlery) is invalid.",
            [new StartupCrashExplain.ModName("OpenSourceSaddlery", "Open Source Saddlery")],
            ["Coop"]);

        Assert.Contains("Open Source Saddlery has an old shader file inside the mod", Assert.Single(lines));
        Assert.True(StartupCrashExplain.NamesShaderCache(lines));
    }

    [Fact]
    public void An_old_module_shader_file_is_set_aside_once()
    {
        var root = Path.Combine(Path.GetTempPath(), "shader-aside-" + Guid.NewGuid().ToString("N"));
        var folder = Path.Combine(root, "Shaders", "D3D11");
        Directory.CreateDirectory(folder);
        var sack = Path.Combine(folder, "compressed_shader_cache.sack");
        File.WriteAllBytes(sack, [1, 2, 3]);
        try
        {
            var first = ModuleShaderCache.SetAside([("Open Source Saddlery", root)]);
            var second = ModuleShaderCache.SetAside([("Open Source Saddlery", root)]);

            Assert.Equal("Open Source Saddlery", Assert.Single(first).Name);
            Assert.Null(Assert.Single(first).Error);
            Assert.Empty(second);
            Assert.False(File.Exists(sack));
            Assert.True(File.Exists(sack + ".coodsz-old"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Terrain_shader_files_the_game_could_not_remove_are_read_from_the_newest_engine_logs()
    {
        var root = Path.Combine(Path.GetTempPath(), "terrain-logs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        // The AD1259 crash logs (CR-TXY4V) mix separators in one path.
        var crashed = Path.Combine(root, "rgl_log_22576.txt");
        File.WriteAllText(crashed, """
            rglTerrain_shader_generator::clear
            WARNING: failure on remove_all_files_with_extension : C:\ProgramData/Mount and Blade II Bannerlord/Shaders/TerrainShaders/AD1259/Main_map/D3D11/0p.sacx
            WARNING: failure on remove_all_files_with_extension : C:\ProgramData/Mount and Blade II Bannerlord/Shaders/TerrainShaders/AD1259/Main_map/D3D11/shader_mapping.bin
            WARNING: failure on remove_all_files_with_extension : C:\ProgramData\Mount and Blade II Bannerlord\Shaders\TerrainShaders\TAOM_Map\Main_map\D3D11\9v.sacx
            WARNING: failure on remove_all_files_with_extension : C:\ProgramData/Mount and Blade II Bannerlord/Shaders/TerrainShaders/../../Windows/System32/0p.sacx
            WARNING: failure on remove_all_files_with_extension : C:\ProgramData/Mount and Blade II Bannerlord/Shaders/TerrainShaders/C:/Windows/0p.sacx
            WARNING: failure on remove_all_files_with_extension : D:\Elsewhere\0p.sacx
            WARNING: failure on remove_all_files_with_extension : D:\Elsewhere/TerrainShaders/Other/Main_map/0p.sacx
            WARNING: failure on remove_all_files_with_extension : C:\ProgramData/Mount and Blade II Bannerlord/Shaders/TerrainShaders/Bare
            Missing shader from sack: pbr_terrain
            """);
        File.WriteAllText(Path.Combine(root, "rgl_log_13048.txt"), "rglTerrain_shader_generator::clear\nTicking map scene for first initialization\n");
        File.WriteAllText(Path.Combine(root, "rgl_log_errors_22576.txt"),
            "WARNING: failure on remove_all_files_with_extension : C:/ProgramData/Mount and Blade II Bannerlord/Shaders/TerrainShaders/Errors/Main_map/0p.sacx\n");
        var locked = Path.Combine(root, "rgl_log_7200.txt");
        File.WriteAllText(locked,
            "WARNING: failure on remove_all_files_with_extension : C:/ProgramData/Mount and Blade II Bannerlord/Shaders/TerrainShaders/Locked/Main_map/0p.sacx\n");
        try
        {
            IReadOnlyList<TerrainShaderCache.Report> reported;
            using (new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None))
                reported = TerrainShaderCache.Reported(root);

            Assert.Equal(new[] { "AD1259", "TAOM_Map" }, reported.Select(report => report.Name).Order());
            Assert.All(reported, report => Assert.Equal(File.GetLastWriteTimeUtc(crashed), report.LogTimeUtc));
            Assert.Empty(TerrainShaderCache.Reported(Path.Combine(root, "missing")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Terrain_shaders_the_game_could_not_remove_are_set_aside_even_when_read_only()
    {
        var root = Path.Combine(Path.GetTempPath(), "terrain-aside-" + Guid.NewGuid().ToString("N"));
        var folder = Path.Combine(root, "AD1259");
        Directory.CreateDirectory(Path.Combine(folder, "Main_map", "D3D11"));
        var shader = Path.Combine(folder, "Main_map", "D3D11", "0p.sacx");
        File.WriteAllText(shader, "new");
        File.SetAttributes(shader, FileAttributes.ReadOnly);
        // A copy set aside on an earlier Connect makes way for this one.
        Directory.CreateDirectory(folder + ".coodsz-old");
        var older = Path.Combine(folder + ".coodsz-old", "0p.sacx");
        File.WriteAllText(older, "old");
        File.SetAttributes(older, FileAttributes.ReadOnly);
        var later = DateTime.UtcNow.AddMinutes(1);
        try
        {
            var results = TerrainShaderCache.SetAside(root, [
                new("AD1259", later), new("Missing", later), new("..", later)]);
            // The game rebuilds its shaders inside the same folder, so the folder itself stays old.
            var taom = Path.Combine(root, "TAOM_Map");
            Directory.CreateDirectory(taom);
            File.WriteAllText(Path.Combine(taom, "0p.sacx"), "rebuilt");
            Directory.SetCreationTimeUtc(taom, DateTime.UtcNow.AddHours(-1));
            var rebuilt = TerrainShaderCache.SetAside(root, [new("TAOM_Map", DateTime.UtcNow.AddMinutes(-5))]);

            Assert.Equal(new[] { ("AD1259", TerrainShaderCache.Outcome.SetAside), ("Missing", TerrainShaderCache.Outcome.Gone) },
                results.Select(result => (result.Name, result.Outcome)));
            Assert.False(Directory.Exists(folder));
            Assert.Equal("new", File.ReadAllText(Path.Combine(folder + ".coodsz-old", "Main_map", "D3D11", "0p.sacx")));
            Assert.False(File.Exists(older));
            // A file written after the log that reported the folder is one of the game's new shaders.
            Assert.Empty(rebuilt);
            Assert.Equal("rebuilt", File.ReadAllText(Path.Combine(taom, "0p.sacx")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Terrain_shaders_windows_will_not_let_go_are_removed_elevated_after_one_yes()
    {
        var root = Path.Combine(Path.GetTempPath(), "terrain-denied-" + Guid.NewGuid().ToString("N"));
        var folder = Path.Combine(root, "AD1259");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "0p.sacx"), "x");
        var log = new List<string>();
        var asked = new List<string>();
        System.Diagnostics.ProcessStartInfo? started = null;
        DenyRemoval(root, folder, deny: true);
        try
        {
            await TerrainShaderCache.ClearAsync(root, () => new[] { new TerrainShaderCache.Report("AD1259", DateTime.UtcNow.AddMinutes(1)) }, names =>
            {
                asked.Add(names);
                return true;
            }, log.Add, start =>
            {
                // Stands in for the elevated rd.
                started = start;
                DenyRemoval(root, folder, deny: false);
                Directory.Delete(folder, recursive: true);
                return 0;
            });

            Assert.Equal("AD1259", Assert.Single(asked));
            Assert.Equal("runas", started!.Verb);
            Assert.True(started.UseShellExecute);
            Assert.EndsWith("cmd.exe", started.FileName);
            Assert.Contains("rd /s /q \"" + folder + "\"", started.Arguments);
            Assert.Equal("Removed the old AD1259 map shader files with administrator permission. The game builds new ones.", Assert.Single(log));
            Assert.False(Directory.Exists(folder));
        }
        finally
        {
            DenyRemoval(root, folder, deny: false);
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Terrain_shaders_stay_when_the_player_says_no_or_cancels_the_windows_prompt(bool cancelUac)
    {
        var root = Path.Combine(Path.GetTempPath(), "terrain-kept-" + Guid.NewGuid().ToString("N"));
        var folder = Path.Combine(root, "AD1259");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "0p.sacx"), "x");
        var log = new List<string>();
        var starts = 0;
        DenyRemoval(root, folder, deny: true);
        try
        {
            await TerrainShaderCache.ClearAsync(root, () => new[] { new TerrainShaderCache.Report("AD1259", DateTime.UtcNow.AddMinutes(1)) }, _ => cancelUac, log.Add, _ =>
            {
                starts++;
                throw new System.ComponentModel.Win32Exception(1223);
            });

            Assert.Equal(cancelUac ? 1 : 0, starts);
            Assert.Equal("Kept the old AD1259 map shader files. The game can crash on the map until they're gone. Connect asks again next time.", Assert.Single(log));
            Assert.True(File.Exists(Path.Combine(folder, "0p.sacx")));
        }
        finally
        {
            DenyRemoval(root, folder, deny: false);
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("module")]
    [InlineData("terrain")]
    [InlineData("after ask")]
    public async Task Terrain_shaders_behind_a_junction_are_left_alone(string where)
    {
        var top = Path.Combine(Path.GetTempPath(), "terrain-link-" + Guid.NewGuid().ToString("N"));
        var outside = Path.Combine(top, "outside");
        var root = Path.Combine(top, "Mount and Blade II Bannerlord", "Shaders", "TerrainShaders");
        var folder = Path.Combine(root, "AD1259");
        Directory.CreateDirectory(Path.Combine(outside, "AD1259"));
        File.WriteAllText(Path.Combine(outside, "AD1259", "keep.txt"), "keep");
        var log = new List<string>();
        var asked = 0;
        var starts = 0;
        var link = where == "terrain" ? root : folder;
        var target = where == "terrain" ? outside : Path.Combine(outside, "AD1259");
        if (where == "after ask")
        {
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "0p.sacx"), "x");
            DenyRemoval(root, folder, deny: true);
        }
        else
        {
            Directory.CreateDirectory(Path.GetDirectoryName(link)!);
            Junction(link, target);
        }

        try
        {
            await TerrainShaderCache.ClearAsync(root, () => new[] { new TerrainShaderCache.Report("AD1259", DateTime.UtcNow.AddMinutes(1)) }, _ =>
            {
                asked++;
                // The folder turns into a link while the question is up.
                DenyRemoval(root, folder, deny: false);
                Directory.Delete(folder, recursive: true);
                Junction(folder, target);
                return true;
            }, log.Add, _ => starts++);

            Assert.Equal(where == "after ask" ? 1 : 0, asked);
            Assert.Equal(0, starts);
            Assert.Equal("Left the old AD1259 map shader files alone, because a folder on their path is a link (junction or symlink) to another place.", Assert.Single(log));
            Assert.True(Directory.Exists(link));
            Assert.True(File.Exists(Path.Combine(outside, "AD1259", "keep.txt")));
        }
        finally
        {
            if (where == "after ask" && !IsJunction(folder))
                DenyRemoval(root, folder, deny: false);
            if (IsJunction(link))
                Directory.Delete(link);
            Directory.Delete(top, recursive: true);
        }
    }

    [Fact]
    public void The_elevated_removal_checks_every_folder_on_the_way_right_before_each_rd()
    {
        var root = @"C:\ProgramData\Mount and Blade II Bannerlord\Shaders\TerrainShaders";
        var folders = new[] { root + @"\AD1259", root + @"\TAOM_Map" };

        var fsutil = Path.Combine(Environment.SystemDirectory, "fsutil.exe");

        var arguments = TerrainShaderCache.RemoveCommand(root, folders)!.Arguments;

        Assert.StartsWith("/d /c (if not exist \"" + fsutil + "\" exit /b 76) & ", arguments);
        var ancestors = new[] { @"C:\ProgramData\Mount and Blade II Bannerlord", @"C:\ProgramData\Mount and Blade II Bannerlord\Shaders", root };
        var from = 0;
        foreach (var target in folders.SelectMany(folder => new[] { folder, folder + ".coodsz-old" }))
        {
            var rd = arguments.IndexOf("rd /s /q \"" + target + "\"", from, StringComparison.Ordinal);
            Assert.True(rd > from, "no rd for " + target);
            foreach (var path in ancestors.Append(target))
            {
                var guard = arguments.IndexOf("\"" + fsutil + "\" reparsepoint query \"" + path + "\" >nul 2>&1 && exit /b 75 & ", from, StringComparison.Ordinal);
                Assert.InRange(guard, from, rd);
            }

            from = rd + 1;
        }

        // An fsutil path that could break out of the quotes builds no command at all.
        Assert.Null(TerrainShaderCache.RemoveCommand(root, folders, @"C:\%TEMP%\fsutil.exe"));
    }

    // The guarded command run for real, not elevated, on a temp layout: a junction anywhere on the way stops it.
    [Theory]
    [InlineData("none")]
    [InlineData("terrain")]
    [InlineData("aside")]
    [InlineData("no fsutil")]
    public void The_elevated_removal_stops_at_a_junction_when_it_runs(string where)
    {
        var top = Path.Combine(Path.GetTempPath(), "terrain-rd-" + Guid.NewGuid().ToString("N"));
        var outside = Path.Combine(top, "outside");
        var root = Path.Combine(top, "Mount and Blade II Bannerlord", "Shaders", "TerrainShaders");
        var folders = new[] { Path.Combine(root, "AD1259"), Path.Combine(root, "TAOM_Map") };
        var link = where == "terrain" ? root : folders[0] + ".coodsz-old";
        var real = where == "terrain" ? outside : root;
        foreach (var folder in new[] { "AD1259", "TAOM_Map" })
        {
            Directory.CreateDirectory(Path.Combine(real, folder));
            File.WriteAllText(Path.Combine(real, folder, "0p.sacx"), "x");
        }

        Directory.CreateDirectory(Path.Combine(outside, "aside"));
        File.WriteAllText(Path.Combine(outside, "aside", "keep.txt"), "keep");
        if (where == "terrain")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(root)!);
            Junction(root, outside);
        }
        else if (where == "aside")
        {
            Junction(link, Path.Combine(outside, "aside"));
        }

        try
        {
            var start = TerrainShaderCache.RemoveCommand(root, folders, where == "no fsutil" ? Path.Combine(top, "missing", "fsutil.exe") : null)!;
            start.Verb = "";
            start.UseShellExecute = false;
            start.CreateNoWindow = true;
            using var process = System.Diagnostics.Process.Start(start)!;
            process.WaitForExit();

            Assert.Equal(where switch { "none" => 0, "no fsutil" => TerrainShaderCache.NoFsutilExitCode, _ => TerrainShaderCache.LinkExitCode }, process.ExitCode);
            // A junction at the .coodsz-old copy stops the cmd after the module folder's own rd, before the copy's.
            Assert.Equal(where is "terrain" or "no fsutil", Directory.Exists(folders[0]));
            Assert.Equal(where != "none", Directory.Exists(folders[1]));
            Assert.True(File.Exists(Path.Combine(outside, "aside", "keep.txt")));
        }
        finally
        {
            if (IsJunction(link))
                Directory.Delete(link);
            Directory.Delete(top, recursive: true);
        }
    }

    private static bool IsJunction(string path) =>
        Directory.Exists(path) && File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint);

    // mklink /J needs no admin, unlike a symlink.
    private static void Junction(string link, string target)
    {
        using (var mklink = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe") { ArgumentList = { "/c", "mklink", "/J", link, target }, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true })!)
            mklink.WaitForExit();
        Assert.True(File.GetAttributes(link).HasFlag(FileAttributes.ReparsePoint), "mklink /J could not make a junction at " + link);
    }

    // The elevated cmd's own stops, through the stub: 75 at a link (also when only the .coodsz-old copy is left),
    // 76 when fsutil.exe is missing, and no start at all when the fsutil path could break the quotes.
    [Theory]
    [InlineData(75, false, false, "Left the old AD1259 map shader files alone, because a folder on their path is a link (junction or symlink) to another place.")]
    [InlineData(75, true, false, "Removed the old AD1259 map shader files with administrator permission. The game builds new ones.|Left the old AD1259 map shader files alone, because a folder on their path is a link (junction or symlink) to another place.")]
    [InlineData(76, false, false, "Could not remove the old AD1259 map shader files, because Windows' fsutil.exe, which checks them for links first, can't be used.")]
    [InlineData(0, false, true, "Could not remove the old AD1259 map shader files, because Windows' fsutil.exe, which checks them for links first, can't be used.")]
    public async Task Terrain_shaders_the_elevated_cmd_stops_on_say_why(int exitCode, bool copyLeft, bool badFsutil, string expected)
    {
        var root = Path.Combine(Path.GetTempPath(), "terrain-stop-" + Guid.NewGuid().ToString("N"));
        var folder = Path.Combine(root, "AD1259");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "0p.sacx"), "x");
        var log = new List<string>();
        var starts = 0;
        DenyRemoval(root, folder, deny: true);
        try
        {
            await TerrainShaderCache.ClearAsync(root, () => new[] { new TerrainShaderCache.Report("AD1259", DateTime.UtcNow.AddMinutes(1)) }, _ => true, log.Add, _ =>
            {
                starts++;
                if (copyLeft)
                {
                    // The module folder's rd ran, then the cmd stopped at its .coodsz-old copy.
                    DenyRemoval(root, folder, deny: false);
                    Directory.Delete(folder, recursive: true);
                    Directory.CreateDirectory(folder + ".coodsz-old");
                }

                return exitCode;
            }, badFsutil ? @"C:\%TEMP%\fsutil.exe" : null);

            Assert.Equal(badFsutil ? 0 : 1, starts);
            Assert.Equal(expected.Split('|'), log);
            Assert.Equal(!copyLeft, File.Exists(Path.Combine(folder, "0p.sacx")));
        }
        finally
        {
            DenyRemoval(root, folder, deny: false);
            Directory.Delete(root, recursive: true);
        }
    }

    // What an elevated game run leaves behind: this account may neither delete the folder nor remove it from its parent.
    private static void DenyRemoval(string root, string folder, bool deny)
    {
        var user = System.Security.Principal.WindowsIdentity.GetCurrent().User!;
        foreach (var (path, rights) in new[] { (root, FileSystemRights.DeleteSubdirectoriesAndFiles), (folder, FileSystemRights.Delete) })
        {
            if (!Directory.Exists(path))
                continue;
            var info = new DirectoryInfo(path);
            var security = info.GetAccessControl();
            var rule = new FileSystemAccessRule(user, rights, AccessControlType.Deny);
            if (deny)
                security.AddAccessRule(rule);
            else
                security.RemoveAccessRule(rule);
            info.SetAccessControl(security);
        }
    }

    [Fact]
    public void The_coop_log_names_every_loaded_module()
    {
        var log = """
            Current modules:
            Unofficial v0.1.5.0 CoopNightly
            Unofficial v1.4.2.1 Bannerlord.Diplomacy
            Unofficial v2.0.26.0 LOTRLOME_Armory
            ========================================================
            """;

        var ids = StartupCrashExplain.LoadedModuleIds(log);

        Assert.Equal(["CoopNightly", "Bannerlord.Diplomacy", "LOTRLOME_Armory"], ids);
    }

    [Fact]
    public void Removed_mods_stay_out_of_the_launch()
    {
        var profile = new ServerProfile();
        DefaultClientSet.Apply(profile);
        RemovedModLaunch.Drop(profile, ["CoopModPatch", "PSCoopBridge"]);
        var order = LaunchArgumentBuilder.ModuleOrder(profile);

        Assert.DoesNotContain(order, id => id.Equals("CoopModPatch", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(order, id => id.Equals("PSCoopBridge", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(order, id => id.Equals("Coop", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void An_unnamed_crash_says_to_turn_off_the_extra_mods()
    {
        var lines = StartupCrashExplain.Explain(
            false,
            -1073741819,
            null,
            "FMOD error! The requested event could not be found.",
            [new StartupCrashExplain.ModName("ChaosBattle", "Chaos Battle")],
            ["Coop"]);

        Assert.Contains("Turn off the extra mods you added", Assert.Single(lines));
    }

    [Fact]
    public void A_mod_that_fails_to_load_is_named_in_the_report()
    {
        var log = """
            Loading assembly: ..\..\Modules\CrashExample\bin\Win64_Shipping_Client\CrashExample.dll
            Exception: ReflectionTypeLoadException
            CrashExample could not be loaded correctly.
            "Crash Example.Crash Example" submodule could not be loaded correctly due to a dependency conflict.
            """;
        var lines = StartupCrashExplain.Explain(
            false,
            -1,
            null,
            log,
            [new StartupCrashExplain.ModName("CrashExample", "Crash Example")],
            ["Coop"]);

        Assert.Equal("Crash Example could not be loaded. Turn Crash Example off, click Save mods, and press Test to main menu again. Leave Bannerlord Coop where it is.", Assert.Single(lines));
    }

    [Fact]
    public void A_blocked_server_dll_tells_the_host_to_unblock_here()
    {
        var lines = ServerCrashExplain.Explain(
            "engine exited with code 1",
            false,
            "Messagebox [Error] message: Cannot load: ..\\Modules\\TradeBoundFix\\bin\\Win64_Shipping_Client\\TradeBoundFix.dll",
            null,
            [new ServerCrashExplain.ModName("ChaosBattle", "Chaos Battle")],
            ["Coop"]);

        Assert.Contains(lines, line => line.StartsWith("What happened:") && line.Contains("does not clear this server folder"));
        Assert.Contains(lines, line => line.StartsWith("What to do:") && line.Contains("Unblock DLLs in this server tool"));
        Assert.Contains(lines, line => line.StartsWith("What not to do:") && line.Contains("Do not use the client launcher button"));
    }

    [Fact]
    public void A_startup_cannot_load_line_does_not_replace_a_server_that_was_already_up()
    {
        var lines = ServerCrashExplain.Explain(
            "engine exited with code 0",
            true,
            "Messagebox [Error] message: Cannot load: ..\\Modules\\SandBox\\bin\\Win64_Shipping_Client\\SandBox.dll",
            null,
            [],
            ["Coop"]);

        Assert.Contains(lines, line => line.Contains("Nothing in the log says a mod broke"));
        Assert.DoesNotContain(lines, line => line.Contains("cannot load a DLL"));
    }

    [Fact]
    public void A_bare_cannot_load_name_is_not_a_blocked_server_dll()
    {
        var lines = ServerCrashExplain.Explain(
            "engine exited with code 1",
            false,
            "Messagebox [ERROR] message: Cannot load: SandBox.dll",
            null,
            [],
            ["Coop"]);

        Assert.DoesNotContain(lines, line => line.Contains("cannot load a DLL"));
    }

    [Fact]
    public void Server_crash_names_the_extra_mod_and_what_not_to_move()
    {
        var lines = ServerCrashExplain.Explain(
            "engine exited with code -1073741819",
            false,
            "ChaosBattle could not be loaded correctly.",
            null,
            [new ServerCrashExplain.ModName("ChaosBattle", "Chaos Battle")],
            ["Coop", "Native"]);

        Assert.Contains(lines, line => line.StartsWith("What happened:") && line.Contains("Chaos Battle did not load"));
        Assert.Contains(lines, line => line.StartsWith("What to do:") && line.Contains("Turn Chaos Battle off"));
        Assert.Contains(lines, line => line.StartsWith("What not to do:") && line.Contains("do not move Bannerlord Coop"));
    }

    [Fact]
    public void A_normal_server_exit_does_not_tell_them_to_change_mods()
    {
        var lines = ServerCrashExplain.Explain(
            "[BannerlordCoopServer] engine exited with code 0",
            true,
            "engine exited with code 0",
            null,
            [new ServerCrashExplain.ModName("ChaosBattle", "Chaos Battle")],
            ["Coop"]);

        Assert.Contains(lines, line => line.StartsWith("What happened:") && line.Contains("stopped on its own"));
        Assert.Contains(lines, line => line.StartsWith("What not to do:") && line.Contains("do not move Bannerlord Coop"));
    }

    [Fact]
    public void Every_server_answer_is_listed()
    {
        var examples = ServerCrashExplain.Examples();
        Assert.Equal(15, examples.Count);
        Assert.All(examples, example =>
        {
            Assert.Contains(example.Lines, line => line.StartsWith("What happened:"));
            Assert.Contains(example.Lines, line => line.StartsWith("What to do:"));
            Assert.Contains(example.Lines, line => line.StartsWith("What not to do:"));
        });
        Assert.Contains(examples, example => example.Lines.Any(line => line.Contains("Nothing in the log says a mod broke")));
    }

    [Fact]
    public void The_engine_bin_dll_probe_stays_out_of_the_host_log()
    {
        Assert.Null(LogLineFormatter.Format("Messagebox [ERROR] message: Cannot load: 0Harmony.dll"));
        Assert.Null(LogLineFormatter.Format("ERROR: 0Harmony.dll: Could not load file or assembly 'E:\\SteamLibrary\\steamapps\\workshop\\content\\261550\\3770450698\\DedicatedServer\\engine\\bin\\Win64_Shipping_Server\\0Harmony.dll'. The system cannot find the file specified."));

        var real = LogLineFormatter.Format("Messagebox [ERROR] message: Cannot load: ..\\Modules\\TradeBoundFix\\bin\\Win64_Shipping_Server\\TradeBoundFix.dll");
        Assert.NotNull(real);
        Assert.Equal("bad", real.Value.Tone);
        Assert.Equal("Cannot load", real.Value.Kind);
    }

    [Fact]
    public void HexTool_startup_notes_stay_out_of_the_log()
    {
        Assert.Null(LogLineFormatter.Format("HexTool compatibility mode will attempt startup. The shared CLR context may select only one variant."));
        Assert.Null(LogLineFormatter.Format("failure is the operator's compatibility responsibility."));
        Assert.NotNull(LogLineFormatter.Format("Save 'lol' was not found or is incompatible with the selected module set."));
    }

    [Fact]
    public void A_steam_lobby_name_picks_one_host_and_refuses_two()
    {
        var one = SteamLobbyFinder.Select(
            [new SteamLobby(5, "Kodiak"), new SteamLobby(8, "Someone Else")],
            "kodi");
        Assert.Equal(5UL, one);

        var exact = SteamLobbyFinder.Select(
            [new SteamLobby(5, "Kodiak Two"), new SteamLobby(9, "Kodiak")],
            "Kodiak");
        Assert.Equal(9UL, exact);

        Assert.Null(SteamLobbyFinder.Select([new SteamLobby(5, "Kodiak")], "missing"));
        Assert.Equal(90UL, SteamLobbyFinder.Select(
            [new SteamLobby(5, "Coodsz"), new SteamLobby(90, "Coodsz", 1), new SteamLobby(40, "Coodsz")],
            "Coodsz"));
        var several = Assert.Throws<InvalidOperationException>(() => SteamLobbyFinder.Select(
            [new SteamLobby(5, "Kodiak One"), new SteamLobby(6, "Kodiak Two")],
            "Kodiak"));
        Assert.Equal(SteamLobbyFinder.MoreThanOne, several.Message);
    }

    [Fact]
    public void A_missing_server_coop_bin_is_filled_from_the_workshop_client_folder()
    {
        var root = Path.Combine(Path.GetTempPath(), "coop-server-bin-" + Guid.NewGuid().ToString("N"));
        var server = Path.Combine(root, "workshop", "content", "261550", "3770450698", "DedicatedServer");
        var clientBin = Path.Combine(root, "workshop", "content", "261550", "3770450698", "bin", "Win64_Shipping_Client");
        var gameBin = Path.Combine(root, "Modules", "Coop", "bin", "Win64_Shipping_Client");
        Directory.CreateDirectory(clientBin);
        Directory.CreateDirectory(gameBin);
        WriteReleaseInfo(server, ("Coop.Core.dll", "workshop-server-bytes"), ("Coop.Steam.dll", "steam"));
        File.WriteAllText(Path.Combine(clientBin, "Coop.Core.dll"), "workshop-server-bytes");
        File.WriteAllText(Path.Combine(clientBin, "Coop.Steam.dll"), "steam");
        File.WriteAllText(Path.Combine(clientBin, "0Harmony.dll"), "harmony");
        File.WriteAllText(Path.Combine(gameBin, "Coop.Core.dll"), "game-client-bytes");

        var message = CoopServerBin.Ensure(server, null);

        var copied = Path.Combine(server, "engine", "Modules", "Coop", "bin", "Win64_Shipping_Server", "Coop.Core.dll");
        Assert.Contains(CoopServerBin.Restored, message);
        Assert.Contains(CoopServerBin.HarmonyRestored, message);
        Assert.Equal("workshop-server-bytes", File.ReadAllText(copied));
        Assert.Equal("steam", File.ReadAllText(Path.Combine(Path.GetDirectoryName(copied)!, "Coop.Steam.dll")));
        Assert.Equal("harmony", File.ReadAllText(Path.Combine(server, "engine", "bin", "Win64_Shipping_Server", "0Harmony.dll")));
        Assert.Equal("game-client-bytes", File.ReadAllText(Path.Combine(gameBin, "Coop.Core.dll")));
        File.WriteAllText(copied, "keep");
        var changed = Assert.Throws<InvalidOperationException>(() => CoopServerBin.Ensure(server, null));
        Assert.Contains("COOP MODULE VERIFICATION FAILED", changed.Message);
        Assert.Contains("Coop.Core.dll", changed.Message);
        Assert.Equal("keep", File.ReadAllText(copied));
        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public void A_client_coop_core_that_is_another_build_is_not_copied_to_the_server()
    {
        var root = Path.Combine(Path.GetTempPath(), "coop-server-pin-" + Guid.NewGuid().ToString("N"));
        var server = Path.Combine(root, "workshop", "content", "261550", "3770450698", "DedicatedServer");
        var clientBin = Path.Combine(root, "workshop", "content", "261550", "3770450698", "bin", "Win64_Shipping_Client");
        Directory.CreateDirectory(clientBin);
        WriteReleaseInfo(server, ("Coop.Core.dll", "server-build"));
        File.WriteAllText(Path.Combine(clientBin, "Coop.Core.dll"), "client-build");
        File.WriteAllText(Path.Combine(clientBin, "0Harmony.dll"), "harmony");

        var refused = Assert.Throws<InvalidOperationException>(() => CoopServerBin.Ensure(server, null));

        Assert.Equal(CoopServerBin.ClientCopyRefused, refused.Message);
        Assert.False(File.Exists(Path.Combine(CoopServerBin.CoopServerBinFolder(server), "Coop.Core.dll")));
        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public void A_client_coop_core_left_on_the_server_by_an_older_host_is_named()
    {
        var root = Path.Combine(Path.GetTempPath(), "coop-server-old-" + Guid.NewGuid().ToString("N"));
        var server = Path.Combine(root, "workshop", "content", "261550", "3770450698", "DedicatedServer");
        var clientBin = Path.Combine(root, "workshop", "content", "261550", "3770450698", "bin", "Win64_Shipping_Client");
        var serverBin = CoopServerBin.CoopServerBinFolder(server);
        Directory.CreateDirectory(clientBin);
        Directory.CreateDirectory(serverBin);
        WriteReleaseInfo(server, ("Coop.Core.dll", "server-build"), ("Common.dll", "common"));
        File.WriteAllText(Path.Combine(clientBin, "Coop.Core.dll"), "client-build");
        File.WriteAllText(Path.Combine(serverBin, "Coop.Core.dll"), "client-build");
        File.WriteAllText(Path.Combine(serverBin, "Common.dll"), "common");

        Assert.Equal(new[] { "Coop.Core.dll" }, CoopServerBin.WrongCoopFiles(server));
        var refused = Assert.Throws<InvalidOperationException>(() => CoopServerBin.Ensure(server, null));
        Assert.Contains("Workshop client copy", refused.Message);
        Assert.Contains("Verify integrity", refused.Message);
        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public void A_coop_build_mismatch_is_not_explained_as_a_missing_file()
    {
        var lines = ServerCrashExplain.Explain(
            "[BannerlordCoopServer] engine exited with code 4",
            false,
            "[00:00:06][DedicatedServer] COOP MODULE VERIFICATION FAILED: Coop.Core.dll does not match the Coop build this server release pairs with (sha256 AC743E0869C4AC89B515FDA0F22654488E6FB09E26B13818729D71C8D173D992, expected 90121c59ff8b6d379933ce01d9a4c4bb842fcfe730cd5131c1072d4332a47cf1)\n[BannerlordCoopServer] engine exited with code 4",
            null,
            [],
            ["Coop", "Native"]);

        Assert.Contains(lines, line => line.StartsWith("What happened:") && line.Contains("not the Coop build"));
        Assert.Contains(lines, line => line.StartsWith("What to do:") && line.Contains("Verify integrity"));
    }

    private static void WriteReleaseInfo(string server, params (string File, string Content)[] pins)
    {
        Directory.CreateDirectory(server);
        var lines = new List<string> { "Pinned Coop assemblies (SHA-256):" };
        foreach (var pin in pins)
        {
            var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(pin.Content))).ToLowerInvariant();
            lines.Add("  " + pin.File.PadRight(18) + " " + hash);
        }

        File.WriteAllText(Path.Combine(server, "release-info.txt"), string.Join("\r\n", lines) + "\r\n");
    }

    [Fact]
    public void A_missing_packaged_harmony_is_restored_even_when_coop_bin_exists()
    {
        var root = Path.Combine(Path.GetTempPath(), "coop-harmony-bin-" + Guid.NewGuid().ToString("N"));
        var server = Path.Combine(root, "DedicatedServer");
        var coopBin = Path.Combine(server, "engine", "Modules", "Coop", "bin", "Win64_Shipping_Server");
        var harmonyModule = Path.Combine(server, "engine", "Modules", "Bannerlord.Harmony", "bin", "Win64_Shipping_Client");
        Directory.CreateDirectory(coopBin);
        Directory.CreateDirectory(harmonyModule);
        File.WriteAllText(Path.Combine(coopBin, "Coop.Core.dll"), "coop");
        File.WriteAllText(Path.Combine(harmonyModule, "0Harmony.dll"), "module-harmony");

        var message = CoopServerBin.Ensure(server, null);

        Assert.Equal(CoopServerBin.HarmonyRestored, message);
        Assert.Equal(
            "module-harmony",
            File.ReadAllText(Path.Combine(server, "engine", "bin", "Win64_Shipping_Server", "0Harmony.dll")));
        Assert.Null(CoopServerBin.Ensure(server, null));
        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public void Host_repair_payload_fills_thin_dedicated_server_starter_files()
    {
        var root = Path.Combine(Path.GetTempPath(), "host-repair-" + Guid.NewGuid().ToString("N"));
        var server = Path.Combine(root, "DedicatedServer");
        var payload = Path.Combine(root, "DedicatedServerRepair");
        Directory.CreateDirectory(server);
        Directory.CreateDirectory(Path.Combine(payload, "engine", "bin", "Win64_Shipping_Server"));
        File.WriteAllText(Path.Combine(payload, "Start-ModdedServer.ps1"), "script");
        File.WriteAllText(Path.Combine(payload, "engine", "bin", "Win64_Shipping_Server", "0Harmony.dll"), "harmony");
        File.WriteAllText(Path.Combine(server, "BannerlordCoopServer.exe"), "exe");

        var message = DedicatedServerRepair.Ensure(server, payload);

        Assert.Contains("Start-ModdedServer.ps1", message);
        Assert.Contains("0Harmony.dll", message);
        Assert.Equal("script", File.ReadAllText(Path.Combine(server, "Start-ModdedServer.ps1")));
        Assert.Equal(
            "harmony",
            File.ReadAllText(Path.Combine(server, "engine", "bin", "Win64_Shipping_Server", "0Harmony.dll")));
        Assert.Null(DedicatedServerRepair.Ensure(server, payload));

        // An older start script is replaced, the old one kept beside it.
        File.WriteAllText(Path.Combine(server, "Start-ModdedServer.ps1"), "old script");
        var updated = DedicatedServerRepair.Ensure(server, payload);
        Assert.Contains("Updated Start-ModdedServer.ps1", updated);
        Assert.Equal("script", File.ReadAllText(Path.Combine(server, "Start-ModdedServer.ps1")));
        Assert.Equal("old script", File.ReadAllText(Path.Combine(server, "Start-ModdedServer.ps1.coodsz-old")));
        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public void The_server_start_script_does_not_break_on_a_turkish_windows()
    {
        var repo = new DirectoryInfo(AppContext.BaseDirectory);
        while (repo != null && !File.Exists(Path.Combine(repo.FullName, "BannerlordCoopLauncher.sln")))
            repo = repo.Parent;
        var script = File.ReadAllText(Path.Combine(repo!.FullName, "src", "CoodszHost", "DedicatedServerRepair", "Start-ModdedServer.ps1"));
        // -match ignores case with the PC's language; on Turkish Windows "I" no longer matches [A-Za-z].
        Assert.DoesNotMatch(@"-notmatch\s+'\^\[A-Za-z", script);
        Assert.DoesNotMatch(@"(?<!c)-match\s+'\^\[A-Za-z", script);
    }

    [Fact]
    public void The_workshop_server_gets_the_launcher_submodules_turned_on()
    {
        var root = Path.Combine(Path.GetTempPath(), "host-launcher-sub-" + Guid.NewGuid().ToString("N"));
        var server = Path.Combine(root, "DedicatedServer");
        var payload = Path.Combine(root, "DedicatedServerRepair");
        var payloadBin = Path.Combine(payload, "engine", "Modules", "DedicatedServer.Windows", "bin", "Win64_Shipping_Server");
        var module = Path.Combine(server, "engine", "Modules", "DedicatedServer.Windows");
        Directory.CreateDirectory(payloadBin);
        Directory.CreateDirectory(Path.Combine(module, "bin", "Win64_Shipping_Server"));
        File.WriteAllText(Path.Combine(payloadBin, "DedicatedServer.LauncherCheats.dll"), "cheats");
        File.WriteAllText(Path.Combine(payloadBin, "DedicatedServer.LauncherBattleSize.dll"), "battle");
        var workshopXml = """
            <?xml version="1.0" encoding="utf-8"?>
            <Module>
                <Id value="DedicatedServer.Windows"/>
                <SubModules>
                    <SubModule>
                        <Name value="DedicatedServer.Windows"/>
                        <DLLName value="DedicatedServer.Windows.dll"/>
                        <SubModuleClassType value="DedicatedServer.Windows.DedicatedServerSubModule"/>
                    </SubModule>
                </SubModules>
            </Module>
            """;
        File.WriteAllText(Path.Combine(module, "SubModule.xml"), workshopXml);
        try
        {
            var message = DedicatedServerRepair.Ensure(server, payload);

            Assert.Contains("Launcher Cheats", message);
            var xml = System.Xml.Linq.XDocument.Load(Path.Combine(module, "SubModule.xml"));
            var dlls = xml.Root!.Element("SubModules")!.Elements("SubModule")
                .Select(item => (string?)item.Element("DLLName")!.Attribute("value")).ToList();
            Assert.Equal(new[] { "DedicatedServer.Windows.dll", "DedicatedServer.LauncherCheats.dll", "DedicatedServer.LauncherBattleSize.dll" }, dlls);
            Assert.True(File.Exists(Path.Combine(module, "SubModule.xml.coodsz-original")));

            // Already on: nothing changes the second time.
            Assert.Null(DedicatedServerRepair.Ensure(server, payload));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void The_host_ships_the_launcher_submodules()
    {
        // The DLLs come from the private folder (CONTRIBUTING.md, Private files): every host that carries
        // DedicatedServerRepair packs it into that payload, and the release scripts refuse to build without each DLL.
        foreach (var host in new[] { "CoodszHost", "AD1259Host", "BurningEmpiresHost" })
            Assert.Contains("<CoodszPayload Include=\"$(CoodszPrivateRepair)**\\*\" Payload=\"DedicatedServerRepair\" />", SourceFiles.Read(Path.Combine("src", host, host + ".csproj")));
        var release = SourceFiles.Read(Path.Combine("tools", "ReleaseAssets.ps1"));
        foreach (var (_, dll, _) in DedicatedServerRepair.LauncherSubModules)
            Assert.Contains("'engine\\Modules\\DedicatedServer.Windows\\bin\\Win64_Shipping_Server\\" + dll + "' = '", release);
        Assert.Contains("'BannerlordCoop.RuntimeBootstrap.dll' = '", release);
    }

    [Fact]
    public void A_server_mod_steam_updated_is_replaced_from_the_workshop()
    {
        var root = Path.Combine(Path.GetTempPath(), "host-ws-update-" + Guid.NewGuid().ToString("N"));
        var server = Path.Combine(root, "DedicatedServer");
        var workshop = Path.Combine(root, "workshop");
        static void Module(string folder, string id, string version)
        {
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "SubModule.xml"),
                "<Module><Name value=\"" + id + "\"/><Id value=\"" + id + "\"/><Version value=\"" + version + "\"/></Module>");
        }

        var modules = CoodszHost.Services.ModuleCatalog.ModulesRoot(server);
        Module(Path.Combine(modules, "Bannerlord.UIExtenderEx"), "Bannerlord.UIExtenderEx", "v3.0.0.0");
        Module(Path.Combine(modules, "BannerFix"), "BannerFix", "v1.0");
        Module(Path.Combine(workshop, "2859222409"), "Bannerlord.UIExtenderEx", "v3.0.2.0");
        Module(Path.Combine(workshop, "111"), "BannerFix", "v1.0");
        try
        {
            var updated = CoodszHost.Services.ExtraModScan.UpdateFromWorkshop(server, new[] { "Bannerlord.UIExtenderEx", "BannerFix" }, new[] { workshop });

            Assert.Equal("Bannerlord.UIExtenderEx v3.0.0.0 -> v3.0.2.0", Assert.Single(updated));
            Assert.Contains("v3.0.2.0", File.ReadAllText(Path.Combine(modules, "Bannerlord.UIExtenderEx", "SubModule.xml")));
            Assert.Single(Directory.GetDirectories(Path.Combine(server, "removed-mods"), "Bannerlord.UIExtenderEx-v3.0.0.0-*"));
            Assert.Empty(CoodszHost.Services.ExtraModScan.UpdateFromWorkshop(server, new[] { "Bannerlord.UIExtenderEx" }, new[] { workshop }));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Host_start_script_copy_leaves_client_cheats_as_set()
    {
        var script = string.Join("\r\n", new[]
        {
            "$serverRoot = $PSScriptRoot",
            "Set-CoopBooleanModOption `",
            "    -Key 'fastForwardEnabled' `",
            "    -Enabled $allowClientTimeControls",
            "Set-CoopBooleanModOption `",
            "    -Key 'clientsCanUseCheats' `",
            "    -Enabled $false",
            "",
            "Write-Host 'Client cheats: ALWAYS blocked by the authoritative server'",
            ""
        });
        var patched = CoodszHost.Services.HostStartScript.WithoutForcedCheats(script);

        Assert.NotNull(patched);
        Assert.DoesNotContain("'clientsCanUseCheats'", patched);
        Assert.Contains("-Key 'fastForwardEnabled'", patched);
        Assert.Contains("as set in mod-config.json", patched);
        Assert.Null(CoodszHost.Services.HostStartScript.WithoutForcedCheats("Write-Host 'nothing here'"));

        var folder = Path.Combine(Path.GetTempPath(), "host-script-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            File.WriteAllText(Path.Combine(folder, "Start-ModdedServer.ps1"), script);
            var used = CoodszHost.Services.HostStartScript.Prepare(folder);
            Assert.Equal(Path.Combine(folder, "Coodsz-Start-ModdedServer.ps1"), used);
            Assert.Equal(patched, File.ReadAllText(used));

            File.WriteAllText(Path.Combine(folder, "Start-ModdedServer.ps1"), "Write-Host 'changed'");
            Assert.Equal(Path.Combine(folder, "Start-ModdedServer.ps1"), CoodszHost.Services.HostStartScript.Prepare(folder));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void Refresh_updates_a_working_coop_mod_from_steam_and_only_takes_newer_game_folder_copies()
    {
        var root = Path.Combine(Path.GetTempPath(), "host-refresh-update-" + Guid.NewGuid().ToString("N"));
        var server = Path.Combine(root, "DedicatedServer");
        var workshop = Path.Combine(root, "workshop");
        var game = Path.Combine(root, "game", "Modules");
        static void Module(string folder, string id, string version)
        {
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "SubModule.xml"),
                "<Module><Name value=\"" + id + "\"/><Id value=\"" + id + "\"/><Version value=\"" + version + "\"/></Module>");
        }

        var modules = CoodszHost.Services.ModuleCatalog.ModulesRoot(server);
        Module(Path.Combine(modules, "SettlementCultureDrift"), "SettlementCultureDrift", "v1.1.3");
        Module(Path.Combine(modules, "BannerFix"), "BannerFix", "v3.3.6");
        Module(Path.Combine(modules, "RaiseYourBanner"), "RaiseYourBanner", "v16.1.7");
        Module(Path.Combine(workshop, "3777470096"), "SettlementCultureDrift", "v1.1.4");
        Module(Path.Combine(game, "BannerFix"), "BannerFix", "v3.4.0");
        Module(Path.Combine(game, "RaiseYourBanner"), "RaiseYourBanner", "v16.0.0");
        try
        {
            var ids = new[] { "SettlementCultureDrift", "BannerFix", "RaiseYourBanner" };
            var updated = CoodszHost.Services.ExtraModScan.UpdateFromNewerCopies(server, ids, new[] { workshop }, new[] { game });

            Assert.Equal(new[] { "SettlementCultureDrift v1.1.3 -> v1.1.4", "BannerFix v3.3.6 -> v3.4.0" }, updated);
            Assert.Contains("v1.1.4", File.ReadAllText(Path.Combine(modules, "SettlementCultureDrift", "SubModule.xml")));
            Assert.Contains("v16.1.7", File.ReadAllText(Path.Combine(modules, "RaiseYourBanner", "SubModule.xml")));
            Assert.Empty(CoodszHost.Services.ExtraModScan.UpdateFromNewerCopies(server, ids, new[] { workshop }, new[] { game }));
            Assert.True(CoodszHost.Services.ExtraModScan.IsNewer("v1.1.10", "v1.1.9"));
            Assert.False(CoodszHost.Services.ExtraModScan.IsNewer("", "v1.0"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Start_takes_a_newer_nexus_mcm_and_never_an_older_workshop_one()
    {
        // Crash reports #32 and #34: a locked setup kept MCM v5.11.4 on the server, every Start was refused,
        // while the player had v5.12.3 from Nexus in the game's Modules folder and no Workshop MCM.
        var root = Path.Combine(Path.GetTempPath(), "host-mcm-update-" + Guid.NewGuid().ToString("N"));
        var server = Path.Combine(root, "DedicatedServer");
        var workshop = Path.Combine(root, "workshop");
        var game = Path.Combine(root, "game", "Modules");
        static void Module(string folder, string id, string version)
        {
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "SubModule.xml"),
                "<Module><Name value=\"" + id + "\"/><Id value=\"" + id + "\"/><Version value=\"" + version + "\"/></Module>");
        }

        const string Mcm = "Bannerlord.MBOptionScreen";
        var modules = CoodszHost.Services.ModuleCatalog.ModulesRoot(server);
        Module(Path.Combine(modules, Mcm), Mcm, "v5.11.4");
        Module(Path.Combine(game, Mcm), Mcm, "v5.12.3");
        Module(Path.Combine(game, "BannerFix"), "BannerFix", "v3.4.0");
        Module(Path.Combine(modules, "BannerFix"), "BannerFix", "v3.3.6");
        try
        {
            var ids = new[] { Mcm, "BannerFix" };
            var updated = CoodszHost.Services.ExtraModScan.UpdateNewerOnlyFromGame(server, ids, new[] { game });
            Assert.Equal(new[] { Mcm + " v5.11.4 -> v5.12.3" }, updated);
            Assert.Contains("v5.12.3", File.ReadAllText(Path.Combine(modules, Mcm, "SubModule.xml")));
            // Other mods of a locked setup keep their server copy at Start.
            Assert.Contains("v3.3.6", File.ReadAllText(Path.Combine(modules, "BannerFix", "SubModule.xml")));

            // An older Workshop reupload of MCM never replaces the newer server copy.
            Module(Path.Combine(workshop, "3761572229"), Mcm, "v5.11.4");
            Assert.Empty(CoodszHost.Services.ExtraModScan.UpdateFromWorkshop(server, new[] { Mcm }, new[] { workshop }));
            Assert.Contains("v5.12.3", File.ReadAllText(Path.Combine(modules, Mcm, "SubModule.xml")));

            // An old server copy with its own server build is replaced too: that build is what stops the server.
            Directory.Delete(Path.Combine(modules, Mcm), recursive: true);
            Module(Path.Combine(modules, Mcm), Mcm, "v5.11.3");
            var serverBin = Path.Combine(modules, Mcm, "bin", "Win64_Shipping_Server");
            Directory.CreateDirectory(serverBin);
            File.WriteAllText(Path.Combine(serverBin, "MCMv5.dll"), "old");
            // A locked pack (AD1259) takes only its pinned version.
            Module(Path.Combine(game, Mcm), Mcm, "v5.13.1");
            var pins = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [Mcm] = "v5.12.3" };
            Assert.Empty(CoodszHost.Services.ExtraModScan.UpdateNewerOnlyFromGame(server, ids, new[] { game }, pins));
            Assert.Equal(new[] { Mcm + " v5.11.3 -> v5.13.1" }, CoodszHost.Services.ExtraModScan.UpdateNewerOnlyFromGame(server, ids, new[] { game }));
            Assert.Contains("v5.13.1", File.ReadAllText(Path.Combine(modules, Mcm, "SubModule.xml")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Client_patches_never_touch_the_dedicated_server_copy()
    {
        Assert.True(ClientFiles.IsServerCopy(@"D:\SteamLibrary\steamapps\workshop\content\261550\3770450698\DedicatedServer\engine\Modules\Coop\bin\Win64_Shipping_Server\Coop.Core.dll"));
        Assert.True(ClientFiles.IsServerCopy(@"C:\Mods\Coop\bin\Win64_Shipping_Server\Coop.Core.dll"));
        Assert.False(ClientFiles.IsServerCopy(@"D:\SteamLibrary\steamapps\workshop\content\261550\3770450698\bin\Win64_Shipping_Client\Coop.Core.dll"));

        var root = Path.Combine(Path.GetTempPath(), "client-files-" + Guid.NewGuid().ToString("N"));
        var client = Path.Combine(root, "3770450698", "bin", "Win64_Shipping_Client");
        var server = Path.Combine(root, "3770450698", "DedicatedServer", "engine", "Modules", "Coop", "bin", "Win64_Shipping_Server");
        Directory.CreateDirectory(client);
        Directory.CreateDirectory(server);
        File.WriteAllText(Path.Combine(client, "Coop.Core.dll"), "client");
        File.WriteAllText(Path.Combine(server, "Coop.Core.dll"), "server");
        try
        {
            var found = ClientFiles.Under(root, "Coop.Core.dll").ToList();
            Assert.Equal(Path.Combine(client, "Coop.Core.dll"), Assert.Single(found));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void A_password_patched_server_coop_dll_is_put_back_from_its_backup()
    {
        var root = Path.Combine(Path.GetTempPath(), "coop-heal-" + Guid.NewGuid().ToString("N"));
        var server = Path.Combine(root, "DedicatedServer");
        var bin = CoopServerBin.CoopServerBinFolder(server);
        Directory.CreateDirectory(bin);
        File.WriteAllText(Path.Combine(bin, "Coop.Core.dll.pre-password.bak"), "original");
        File.WriteAllText(Path.Combine(bin, "Coop.Core.dll"), "patched");
        var pin = CoopServerBin.Sha256(Path.Combine(bin, "Coop.Core.dll.pre-password.bak"));
        File.WriteAllText(Path.Combine(server, "release-info.txt"), "Pinned Coop assemblies (SHA-256)\nCoop.Core.dll " + pin + "\n");
        try
        {
            Assert.Contains("Put back the original Coop.Core.dll", CoopServerBin.RestorePatchedCoop(server));
            Assert.Equal("original", File.ReadAllText(Path.Combine(bin, "Coop.Core.dll")));
            Assert.Empty(CoopServerBin.WrongCoopFiles(server));
            Assert.Null(CoopServerBin.RestorePatchedCoop(server));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void A_missing_dedicated_server_coop_dll_is_named()
    {
        var lines = ServerCrashExplain.Explain(
            "[BannerlordCoopServer] engine exited with code 4",
            false,
            """
            ERROR: 0Harmony.dll: Could not load file or assembly 'c:\program files (x86)\steam\steamapps\workshop\content\261550\3770450698\DedicatedServer\engine\bin\Win64_Shipping_Server\0Harmony.dll'. The system cannot find the file specified.
            [DedicatedServer] COOP MODULE VERIFICATION FAILED: required Coop module assembly missing: c:\program files (x86)\steam\steamapps\workshop\content\261550\3770450698\DedicatedServer\engine\Modules\Coop\bin\Win64_Shipping_Server\Coop.Core.dll
            [DedicatedServer] this server only hosts the official BannerlordCoop module build it was released with — redeploy the matching Modules/Coop.
            [BannerlordCoopServer] engine exited with code 4
            """,
            null,
            [],
            ["Coop", "Native"]);

        Assert.Contains(lines, line => line.StartsWith("What happened:") && line.Contains("Coop.Core.dll") && line.Contains("client files"));
        Assert.Contains(lines, line => line.StartsWith("What to do:") && line.Contains("3770450698"));
        Assert.Contains(lines, line => line.StartsWith("What not to do:") && line.Contains("Do not copy the Coop folder"));
        Assert.DoesNotContain(lines, line => line.Contains("does not name a broken mod"));
    }

    [Fact]
    public void A_mismatched_save_is_named_instead_of_extra_mods()
    {
        var lines = ServerCrashExplain.Explain(
            "Server process exited.",
            false,
            "Save 'lol' was not found or is incompatible with the selected module set.",
            null,
            [new ServerCrashExplain.ModName("ChaosBattle", "Chaos Battle")],
            ["Coop"]);

        Assert.Contains(lines, line => line.StartsWith("What happened:") && line.Contains("campaign save named lol"));
        Assert.Contains(lines, line => line.StartsWith("What to do:") && line.Contains("same setup"));
        Assert.Contains(lines, line => line.StartsWith("What not to do:") && line.Contains("do not move Bannerlord Coop"));
    }

    [Fact]
    public void Engine_log_is_used_when_the_crash_folder_is_missing()
    {
        var root = Path.Combine(Path.GetTempPath(), "engine-log-" + Guid.NewGuid().ToString("N"));
        var docs = Path.Combine(root, "docs");
        var logs = Path.Combine(root, "logs");
        Directory.CreateDirectory(docs);
        Directory.CreateDirectory(logs);
        File.WriteAllText(Path.Combine(logs, "rgl_log_77.txt"), "CrashExample could not be loaded correctly.\n");

        var capture = StartupCrashExplain.Read(docs, 77, logs);

        Assert.Contains("CrashExample could not be loaded correctly.", capture.GameLog);
        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public void Crash_report_folder_is_read_for_that_process()
    {
        var root = Path.Combine(Path.GetTempPath(), "crash-read-" + Guid.NewGuid().ToString("N"));
        var folder = Path.Combine(root, "Mount and Blade II Bannerlord", "Coop Crash Reports", "2026-09-26_client_42_abc");
        Directory.CreateDirectory(Path.Combine(folder, "logs"));
        File.WriteAllText(Path.Combine(folder, "report.txt"), "Exit code: -1073741819\n");
        File.WriteAllText(Path.Combine(folder, "logs", "rgl_log_42.txt"), "LateMod depends on module EarlyMod\n");

        var capture = StartupCrashExplain.Read(Path.Combine(root, "Mount and Blade II Bannerlord"), 42);

        Assert.Equal(-1073741819, capture.ExitCode);
        Assert.Contains("depends on module EarlyMod", capture.GameLog);
        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public void Log_text_hides_the_password_addresses_and_steam_ids()
    {
        var text = ProblemReport.Redact(@"Save password = hunter2 from 10.1.2.3 user 76561198000000000 in C:\Users\Bob\Documents");
        Assert.Contains("[password]", text);
        Assert.Contains("[address]", text);
        Assert.Contains("[steam id]", text);
        Assert.Contains(@"Users\[user]", text);
        Assert.DoesNotContain("hunter2", text);
        Assert.DoesNotContain("Bob", text);
    }

    [Theory]
    [InlineData("Could not load file or assembly 'Bannerlord.UIExtenderEx, Version=2.14.0.0, Culture=neutral'")]
    [InlineData("Harmony: server runtime 2.4.2.248 matches the installed client module.")]
    [InlineData("FileVersion: 1.2.3.4 and Harmony v 2.3.3.207")]
    [InlineData("Game build 1.2.12.66 and Coop 1.0.300.1")]
    public void Log_text_keeps_four_part_version_numbers(string line)
    {
        Assert.Equal(line, ProblemReport.Redact(line));
    }

    [Theory]
    [InlineData("Router internet address 81.2.3.4", "Router internet address [address]")]
    [InlineData("Connecting on \"192.168.1.20:53295\"", "Connecting on \"[address]:53295\"")]
    [InlineData("localRelay=127.0.0.1:50194 virtualPort=0", "localRelay=[address]:50194 virtualPort=0")]
    [InlineData("Server version 10.0.0.5:7210 is up.", "Server version [address]:7210 is up.")]
    [InlineData("Peer 10.1.2.3.", "Peer [address].")]
    [InlineData("conversion 10.0.0.5 and inversion 10.0.0.6", "conversion [address] and inversion [address]")]
    [InlineData("http://10.0.0.5/x", "http://[address]/x")]
    [InlineData("[10.0.0.5]:7210", "[[address]]:7210")]
    [InlineData("10.0.0.5 joined", "[address] joined")]
    public void Log_text_still_hides_addresses_next_to_version_numbers(string line, string expected)
    {
        Assert.Equal(expected, ProblemReport.Redact(line));
    }

    [Fact]
    public void Log_text_keeps_an_address_right_after_the_word_version_as_a_known_trade_off()
    {
        Assert.Equal("version 10.0.0.5", ProblemReport.Redact("version 10.0.0.5"));
    }

    [Fact]
    public void Log_text_with_other_scripts_digits_is_cleaned_without_throwing()
    {
        var line = "Peer ١.٢.٣.٤ and runtime ٩٩٩.1.2.3";
        Assert.Equal(line, ProblemReport.Redact(line));
    }

    private static string ModuleXml(string id, string version) =>
        $"""<Module><Name value="{id}"/><Id value="{id}"/><Version value="{version}"/></Module>""";
}
