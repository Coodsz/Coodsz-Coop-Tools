using BannerlordCoopLauncher.Core.Launch;
using BannerlordCoopLauncher.Core.Modules;
using BannerlordCoopLauncher.Core.Profile;
using TaomCoop.Core;

namespace BannerlordCoopLauncher.Tests;

public class TaomServerPathTests
{
    private static string NewRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "taom-server-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static string Module(string root, string id, string version, string subModules, params string[] serverDlls)
    {
        var dir = Path.Combine(root, id);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "SubModule.xml"),
            "<Module>\n  <Id value=\"" + id + "\"/>\n  <Name value=\"" + id + " name\"/>\n  <Version value=\"" + version + "\"/>\n  <SubModules>\n" + subModules + "  </SubModules>\n</Module>\n");
        if (serverDlls.Length > 0)
        {
            var bin = Path.Combine(dir, "bin", "Win64_Shipping_Server");
            Directory.CreateDirectory(bin);
            foreach (var dll in serverDlls)
                File.WriteAllText(Path.Combine(bin, dll), "x");
        }

        return dir;
    }

    private static string Sub(string dll, string? tags = null) =>
        "    <SubModule>\n      <Name value=\"" + dll + "\"/>\n      <DLLName value=\"" + dll + "\"/>\n      <SubModuleClassType value=\"X.SubModule\"/>\n" + (tags ?? "") + "    </SubModule>\n";

    private static TaomSetup Setup(params string[] ids)
    {
        var setup = new TaomSetup { Name = "Mine" };
        foreach (var id in ids)
            Assert.Null(TaomLoadOrder.Add(setup, id + " name", id, null));
        return setup;
    }

    [Fact]
    public void Is_taom_campaign_requires_the_taom_module_but_accepts_empty_or_case_insensitive_lists()
    {
        Assert.False(TaomServerPath.IsTaomCampaign(new[] { "Native", "SandBoxCore", "CustomBattle", "Sandbox", "Coop" }));
        Assert.True(TaomServerPath.IsTaomCampaign(new[] { "TAOM", "TAOM.Dependencies", "TAOM_Map" }));
        Assert.True(TaomServerPath.IsTaomCampaign(Array.Empty<string>()));
        Assert.True(TaomServerPath.IsTaomCampaign(new[] { "taom" }));
    }

    [Fact]
    public void The_server_tool_rejects_non_taom_saves_before_setup_mod_checks()
    {
        var host = SourceFiles.Window(RepoRoot(), "TaomCoopHost");
        // Start refuses in its own step, so no later "pick the setup" line overwrites the message,
        // and before the TAOM version, config and save mods checks.
        var start = SourceFiles.Method(host, "private async Task StartServerCoreAsync");
        var refuse = start.IndexOf("TaomServerPath.IsTaomCampaign(save)", StringComparison.Ordinal);
        Assert.True(refuse > start.IndexOf("SaveSlot.CanHost", StringComparison.Ordinal));
        Assert.True(refuse < start.IndexOf("ConfirmSaveTaomVersion", StringComparison.Ordinal));
        Assert.True(refuse < start.IndexOf("ConfirmSaveMatchesSetup", StringComparison.Ordinal));
        Assert.Contains("SetControl(TaomServerPath.NotTaomCampaign, failed: true);\n            return;", start.Replace("\r\n", "\n"));
        Assert.DoesNotContain("IsTaomCampaign", SourceFiles.Method(host, "private bool ConfirmSaveMatchesSetup(string savePath)"));
        var prepare = SourceFiles.Method(host, "private async void Prepare_Click(object sender, RoutedEventArgs e)");
        Assert.True(prepare.IndexOf("IsTaomCampaign", StringComparison.Ordinal) is var check && check > 0
            && check < prepare.IndexOf("FindDedicatedServer", StringComparison.Ordinal));
    }

    [Fact]
    public void A_save_whose_header_cannot_be_read_still_counts_as_taom()
    {
        var file = Path.Combine(Path.GetTempPath(), "taom-header-" + Guid.NewGuid().ToString("N") + ".sav");
        File.WriteAllText(file, "not a save");
        try
        {
            Assert.True(TaomServerPath.IsTaomCampaign(file));
        }
        finally
        {
            File.Delete(file);
        }
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "BannerlordCoopLauncher.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root was not found.");
    }

    // ----- Coop's join rules -----

    [Fact]
    public void Client_and_server_lists_for_a_setup_with_extras_pass_coops_join_check()
    {
        var setup = Setup("BetterTime", "MapFix", "XorberaxLegacy");
        TaomLoadOrder.Move(setup, "BetterTime", -1);
        var client = TaomLoadOrder.LaunchModules(setup, joinPassword: true);
        var server = TaomLoadOrder.ServerOrder(setup);

        Assert.Null(CoopJoinRules.JoinProblem(client, server));
        Assert.Contains(ClientAddOns.JoinPasswordId, client);
        Assert.DoesNotContain(ClientAddOns.JoinPasswordId, server);
        Assert.Equal("DedicatedServer.Windows", server[^1]);
        // The server keeps every extra, even one whose code it switches off.
        Assert.Equal(new[] { "BetterTime", "MapFix", "XorberaxLegacy" }, setup.EnabledExtras.Select(mod => mod.ModuleId).Where(server.Contains));
        // TAOM.Dependencies stays first, so every extra loads after it (its ModulesToLoadAfterThis rule).
        Assert.Equal("TAOM.Dependencies", server[0]);
        Assert.Equal("TAOM.Dependencies", client[0]);
    }

    [Fact]
    public void Join_rules_ignore_dedicated_server_and_official_modules_but_not_community_mods()
    {
        Assert.Null(CoopJoinRules.JoinProblem(
            new[] { "Native", "StoryMode", "CustomBattle", "Coop", "ModA", "DedicatedServer.CoodszJoinPassword" },
            new[] { "Native", "DedicatedServer.Windows", "CoopNightly", "ModA" }));
        var problem = CoopJoinRules.JoinProblem(new[] { "Native", "Coop", "ModA" }, new[] { "Native", "Coop", "ModB" });
        Assert.Contains("ModA", problem);
        Assert.Contains("ModB", problem);
        Assert.NotNull(CoopJoinRules.JoinProblem(TaomLoadOrder.LaunchModules(Setup("ModA"), true), TaomStack.ServerModules));
    }

    [Fact]
    public void A_save_made_with_other_mods_is_named()
    {
        var setup = Setup("ModA");
        var oldSave = new[] { "TAOM.Dependencies", "Native", "SandBoxCore", "Sandbox", "CustomBattle", "TAOM", "TAOM_Map", "LOTRLOME_Armory", "CoopNightly", "TAOM.CoopCompat" };
        Assert.Null(CoopJoinRules.SaveProblem(oldSave, TaomStack.ServerModules));
        var problem = CoopJoinRules.SaveProblem(oldSave, TaomLoadOrder.ServerOrder(setup));
        Assert.Contains("ModA", problem);
        Assert.Null(CoopJoinRules.SaveProblem(oldSave.Append("ModA").ToList(), TaomLoadOrder.ServerOrder(setup)));
        Assert.Contains("ModB", CoopJoinRules.SaveProblem(oldSave.Append("ModB").ToList(), TaomStack.ServerModules));
        Assert.Null(CoopJoinRules.SaveProblem(Array.Empty<string>(), TaomStack.ServerModules));
    }

    [Fact]
    public void Save_problem_reads_the_module_list_from_the_save_file()
    {
        var root = NewRoot();
        try
        {
            var save = Path.Combine(root, "camp.sav");
            File.WriteAllText(save, "XXXX{\"List\":{\"CharacterName\":\"Ann\",\"MainHeroLevel\":\"3\",\"DayLong\":\"2\",\"Modules\":\"TAOM.Dependencies;Native;SandBoxCore;Sandbox;CustomBattle;TAOM;TAOM_Map;LOTRLOME_Armory;Coop;TAOM.CoopCompat;ModA\"}}rest");
            Assert.Null(TaomServerPath.SaveProblem(save, Setup("ModA")));
            Assert.Contains("ModA", TaomServerPath.SaveProblem(save, new TaomSetup { Name = "Plain TAOM", Locked = true }));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    // ----- What a mod brings to the server -----

    [Fact]
    public void Mods_are_classified_by_their_server_build()
    {
        Assert.Equal(ServerCode.DataOnly, ServerModuleInstall.Classify("<Module><SubModules></SubModules></Module>", Array.Empty<string>()));
        Assert.Equal(ServerCode.ServerBuild, ServerModuleInstall.Classify("<Module><SubModules>" + Sub("A.dll") + "</SubModules></Module>", new[] { "A.dll" }));
        Assert.Equal(ServerCode.ClientCodeOnly, ServerModuleInstall.Classify("<Module><SubModules>" + Sub("A.dll") + "</SubModules></Module>", Array.Empty<string>()));
        Assert.Equal(ServerCode.ClientCodeSkipped, ServerModuleInstall.Classify(
            "<Module><SubModules>" + Sub("A.dll", "      <Tags><Tag key=\"DedicatedServerType\" value=\"none\" /></Tags>\n") + "</SubModules></Module>", Array.Empty<string>()));
        // Xorberax's server folder DLL is its client build.
        Assert.Equal(ServerCode.ClientCodeOnly, ServerModuleInstall.Classify("<Module><SubModules>" + Sub("XorberaxLegacy.dll") + "</SubModules></Module>", new[] { "XorberaxLegacy.dll" }, "XorberaxLegacy"));
    }

    [Fact]
    public void Client_code_is_switched_off_in_the_server_copy_only_where_there_is_no_server_build()
    {
        var xml = "<Module><SubModules>\n" + Sub("Client.dll") + Sub("Both.dll") + Sub("Tagged.dll", "      <Tags>\n        <Tag key=\"IsNoRenderModeElement\" value=\"false\" />\n      </Tags>\n")
            + Sub("Other.dll", "      <Tags><Tag key=\"DedicatedServerType\" value=\"all\" /></Tags>\n") + "</SubModules></Module>";
        var patched = ServerModuleInstall.TurnOffClientCode(xml, new[] { "Both.dll" });
        Assert.Equal(ServerCode.ClientCodeSkipped, ServerModuleInstall.Classify(patched, new[] { "Both.dll" }));
        Assert.Equal(3, System.Text.RegularExpressions.Regex.Matches(patched, "DedicatedServerType\" value=\"none\"").Count);
        Assert.Contains("IsNoRenderModeElement", patched);
        Assert.Equal(patched, ServerModuleInstall.TurnOffClientCode(patched, new[] { "Both.dll" }));
        Assert.True(System.Xml.Linq.XDocument.Parse(patched).Descendants("SubModule").Count() == 4);
        // A data only mod is left alone.
        var data = "<Module><SubModules></SubModules></Module>";
        Assert.Equal(data, ServerModuleInstall.TurnOffClientCode(data, Array.Empty<string>()));
    }

    // ----- Plan and staging -----

    [Fact]
    public void Plan_and_stage_copy_extras_and_switch_off_client_code_on_the_server()
    {
        var root = NewRoot();
        try
        {
            var game = Path.Combine(root, "game");
            var serverRoot = Path.Combine(root, "server");
            Directory.CreateDirectory(ServerModuleInstall.ModulesRoot(serverRoot));
            var data = Module(game, "Armour", "v1.0", "");
            var server = Module(game, "Smart", "v2.0", Sub("Smart.dll"), "Smart.dll");
            var client = Module(game, "Pretty", "v3.0", Sub("Pretty.dll"));
            Directory.CreateDirectory(Path.Combine(client, "AssetSources"));
            var found = new List<FoundMod>
            {
                new("Armour", "Armour", "11", data, "Workshop"),
                new("Smart", "Smart", "0", server, "Modules"),
                new("Pretty", "Pretty", "0", client, "Modules")
            };
            var setup = Setup("Armour", "Smart", "Pretty", "NotHere");

            var plan = TaomServerPath.Plan(setup, found);
            Assert.Null(plan.JoinProblem);
            Assert.Equal("NotHere", Assert.Single(plan.Missing).ModuleId);
            Assert.Equal(new[] { ServerCode.DataOnly, ServerCode.ServerBuild, ServerCode.ClientCodeOnly }, plan.Extras.Select(extra => extra.Code));

            var lines = TaomServerPath.Stage(serverRoot, plan);
            Assert.Equal(3, lines.Count);
            Assert.All(lines, line => Assert.StartsWith("Copied", line));
            var modules = ServerModuleInstall.ModulesRoot(serverRoot);
            Assert.True(File.Exists(Path.Combine(modules, "Smart", "bin", "Win64_Shipping_Server", "Smart.dll")));
            Assert.False(Directory.Exists(Path.Combine(modules, "Pretty", "AssetSources")));
            Assert.Equal(ServerCode.ClientCodeSkipped, ServerModuleInstall.Classify(Path.Combine(modules, "Pretty")));
            // The player's own copy is not changed.
            Assert.Equal(ServerCode.ClientCodeOnly, ServerModuleInstall.Classify(client));

            // A second start copies nothing that did not change.
            Assert.All(TaomServerPath.Stage(serverRoot, plan), line => Assert.Contains("already on the server", line));
            File.WriteAllText(Path.Combine(server, "SubModule.xml"), File.ReadAllText(Path.Combine(server, "SubModule.xml")).Replace("v2.0", "v2.1"));
            Assert.StartsWith("Copied Smart", TaomServerPath.Stage(serverRoot, plan).Single(line => line.Contains("Smart")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Improved_garrisons_code_runs_on_the_server_when_coopmodpatch_is_in_the_setup()
    {
        // IG marks its code DedicatedServerType none but ships a server build. Coodsz Host force loads
        // it (forceHeadlessModules); the TAOM server has no such loader, so its copy loses the mark,
        // or CoopModPatch's IG adapter cannot load ImprovedGarrisons.dll on the server.
        var root = NewRoot();
        try
        {
            var game = Path.Combine(root, "game");
            var serverRoot = Path.Combine(root, "server");
            Directory.CreateDirectory(ServerModuleInstall.ModulesRoot(serverRoot));
            var igTags = "      <Tags>\n        <Tag key=\"DedicatedServerType\" value=\"none\" />\n        <Tag key=\"IsNoRenderModeElement\" value=\"false\" />\n      </Tags>\n";
            var ig = Module(game, "ImprovedGarrisons", "v4.2.0.7", Sub("ImprovedGarrisons.dll", igTags));
            var igClient = Path.Combine(ig, "bin", "Win64_Shipping_Client");
            Directory.CreateDirectory(igClient);
            File.WriteAllText(Path.Combine(igClient, "ImprovedGarrisons.dll"), "client build");
            var patch = Module(game, "CoopModPatch", "v0.7.1", Sub("CoopModPatch.dll"), "CoopModPatch.dll");
            var found = new List<FoundMod>
            {
                new("ImprovedGarrisons", "ImprovedGarrisons", "2859265386", ig, "Workshop"),
                new("CoopModPatch", "CoopModPatch", "3786391685", patch, "Workshop")
            };
            var serverXml = Path.Combine(ServerModuleInstall.ModulesRoot(serverRoot), "ImprovedGarrisons", "SubModule.xml");

            var setup = Setup("CoopModPatch", "ImprovedGarrisons");
            var plan = TaomServerPath.Plan(setup, found);
            Assert.Equal(ServerCode.ServerBuild, plan.Extras.Single(extra => extra.Mod.ModuleId == "ImprovedGarrisons").Code);
            TaomServerPath.Stage(serverRoot, plan);
            var staged = File.ReadAllText(serverXml);
            Assert.DoesNotContain("DedicatedServerType", staged);
            Assert.DoesNotContain("IsNoRenderModeElement", staged);
            Assert.Contains("<DLLName value=\"ImprovedGarrisons.dll\"/>", staged);
            Assert.Single(System.Xml.Linq.XDocument.Parse(staged).Descendants("SubModule"));
            Assert.Equal(ServerCode.ServerBuild, ServerModuleInstall.Classify(Path.GetDirectoryName(serverXml)!));
            // The player's copy keeps its mark, and a second start writes nothing new.
            Assert.Contains("DedicatedServerType", File.ReadAllText(Path.Combine(ig, "SubModule.xml")));
            var written = File.GetLastWriteTimeUtc(serverXml);
            TaomServerPath.Stage(serverRoot, plan);
            Assert.Equal(written, File.GetLastWriteTimeUtc(serverXml));

            Assert.Equal("client build", File.ReadAllText(Path.Combine(ServerModuleInstall.ModulesRoot(serverRoot), "ImprovedGarrisons", "bin", "Win64_Shipping_Server", "ImprovedGarrisons.dll")));
            Assert.False(Directory.Exists(Path.Combine(ig, "bin", "Win64_Shipping_Server")));

            // A same-version server copy that lost its server DLL gets it again and loses the mark.
            var serverBin = Path.Combine(ServerModuleInstall.ModulesRoot(serverRoot), "ImprovedGarrisons", "bin", "Win64_Shipping_Server");
            Directory.Delete(serverBin, recursive: true);
            File.WriteAllText(serverXml, File.ReadAllText(Path.Combine(ig, "SubModule.xml")));
            TaomServerPath.Stage(serverRoot, plan);
            Assert.True(File.Exists(Path.Combine(serverBin, "ImprovedGarrisons.dll")));
            Assert.DoesNotContain("DedicatedServerType", File.ReadAllText(serverXml));

            // A Workshop update (client build only) replaces the made server build instead of being refused.
            var igXml = Path.Combine(ig, "SubModule.xml");
            File.WriteAllText(igXml, File.ReadAllText(igXml).Replace("v4.2.0.7", "v4.2.0.8"));
            File.WriteAllText(Path.Combine(igClient, "ImprovedGarrisons.dll"), "new client build");
            TaomServerPath.Stage(serverRoot, TaomServerPath.Plan(setup, found));
            Assert.Equal("new client build", File.ReadAllText(Path.Combine(serverBin, "ImprovedGarrisons.dll")));
            Assert.DoesNotContain("DedicatedServerType", File.ReadAllText(serverXml));

            // Without CoopModPatch the server copy gets the mod's own mark back.
            var without = TaomServerPath.Plan(Setup("ImprovedGarrisons"), found);
            Assert.Equal(ServerCode.ClientCodeSkipped, Assert.Single(without.Extras).Code);
            TaomServerPath.Stage(serverRoot, without);
            Assert.Contains("DedicatedServerType\" value=\"none\"", File.ReadAllText(serverXml));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Turning_server_code_on_only_touches_submodules_with_a_server_build()
    {
        var tags = "      <Tags>\n        <Tag key=\"DedicatedServerType\" value=\"none\" />\n        <Tag key=\"IsNoRenderModeElement\" value=\"false\" />\n      </Tags>\n";
        var xml = "<Module><SubModules>\n" + Sub("Server.dll", tags) + Sub("Client.dll", tags) + "</SubModules></Module>";
        var patched = ServerModuleInstall.TurnOnServerCode(xml, new[] { "Server.dll" });
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(patched, "DedicatedServerType"));
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(patched, "IsNoRenderModeElement"));
        Assert.Equal(patched, ServerModuleInstall.TurnOnServerCode(patched, new[] { "Server.dll" }));
        Assert.Equal(2, System.Xml.Linq.XDocument.Parse(patched).Descendants("SubModule").Count());
    }

    [Fact]
    public void Plain_taom_does_not_need_the_coodsz_server_path()
    {
        var book = new TaomSetupBook();
        Assert.False(TaomServerPath.Needed(book.Current()));
        var setup = Setup("ModA");
        Assert.True(TaomServerPath.Needed(setup));
        TaomLoadOrder.SetEnabled(setup, "ModA", false);
        Assert.False(TaomServerPath.Needed(setup));
    }

    // ----- Engine start -----

    [Fact]
    public void The_server_tool_keeps_the_sealed_start_for_plain_taom_and_uses_the_coodsz_start_only_for_extras()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "BannerlordCoopLauncher.sln")))
            dir = dir.Parent;
        var host = SourceFiles.Window(dir!.FullName, "TaomCoopHost");
        var start = SourceFiles.Method(host, "private async Task StartServerCoreAsync");
        Assert.Contains("if (TaomServerPath.Needed(HostSetup))", start);
        Assert.Contains("FileName = engine?.FileName ?? exe", start);
        Assert.Contains("engine?.Arguments ?? \"--data-dir \\\"\" + _settings.DataRoot + \"\\\"\"", start);
        // The save check and the server terms come before anything is copied or started.
        Assert.True(start.IndexOf("ConfirmSaveMatchesSetup(save)", StringComparison.Ordinal) < start.IndexOf("PrepareCoodszServerAsync", StringComparison.Ordinal));
        Assert.True(start.IndexOf("AskServerTerms", StringComparison.Ordinal) < start.IndexOf("PrepareCoodszServerAsync", StringComparison.Ordinal));
    }

    [Fact]
    public void The_engine_starts_like_start_modded_server_with_the_setup_order_and_the_data_folder()
    {
        var setup = Setup("swadian armoury", "ModA");
        var start = TaomServerPath.Launch(@"C:\TAOM-Coop-Host\Server", @"C:\TAOM-Coop-Host\Data", @"C:\Docs\CoopData", setup);
        Assert.Equal(@"C:\TAOM-Coop-Host\Server\engine\dotnet\dotnet.exe", start.FileName);
        Assert.Equal(@"C:\TAOM-Coop-Host\Server\engine\bin\Win64_Shipping_Server", start.WorkingDirectory);
        Assert.Equal("TaleWorlds.Starter.DotNetCore.dll \"_MODULES_*" + string.Join("*", TaomLoadOrder.ServerOrder(setup)) + "*_MODULES_\" /dedicatedcustomserver 7210 EU 0", start.Arguments);
        Assert.Contains("*TAOM.CoopCompat*swadian armoury*ModA*DedicatedServer.Windows*", start.Arguments);
        Assert.Equal(@"C:\TAOM-Coop-Host\Data", start.Environment["BANNERLORD_USER_DIR"]);
        Assert.Equal(@"C:\Docs\CoopData", start.Environment["COOP_DATA_DIR"]);
        Assert.Equal(@"C:\TAOM-Coop-Host\Server\engine\dotnet", start.Environment["DOTNET_ROOT"]);
        Assert.Equal("0", start.Environment["DOTNET_MULTILEVEL_LOOKUP"]);
        Assert.Throws<InvalidOperationException>(() => DedicatedEngineLaunch.ModuleToken(new[] { "Bad*" }));
        Assert.NotNull(DedicatedEngineLaunch.MissingPiece(Path.Combine(Path.GetTempPath(), "no-server-" + Guid.NewGuid().ToString("N"))));
    }
}
