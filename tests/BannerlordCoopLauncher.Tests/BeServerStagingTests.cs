using Xunit.Abstractions;
using BannerlordCoopLauncher.Core.Modules;
using TaomCoop.Core;
using static BurningEmpires.Core.Tests.BeServerFixtures;

namespace BurningEmpires.Core.Tests;

public class BeServerStagingTests(ITestOutputHelper output)
{
    [Fact]
    public void TheFourServerBinCopiesAreMadeAndByteCompared()
    {
        var root = NewRoot("bin");
        try
        {
            var sources = HostPc(root);
            var server = Path.Combine(root, "server");

            BeServerPath.StageCore(server, sources);

            var bin = BeServerPath.BeServerBin(server);
            foreach (var name in new[] { BeServerPath.BeDll, BeServerPath.ProbeDll, BeServerPath.UIExtenderExDll, BeServerPath.CustomBattleDll })
                Assert.True(File.Exists(Path.Combine(bin, name)), name);
            Assert.Equal("uiextender 2.13.3", File.ReadAllText(Path.Combine(bin, BeServerPath.UIExtenderExDll)));
            Assert.Empty(BeServerPath.ServerBinProblems(server, sources));
            Assert.Equal("The Burning Empires server files are already in place.", BeServerPath.CopyServerBin(server, sources).Single());

            // A changed server copy is put back on the next start.
            File.WriteAllText(Path.Combine(bin, BeServerPath.CustomBattleDll), "stale");
            Assert.Equal([BeServerPath.CustomBattleDll], BeServerPath.ServerBinProblems(server, sources));
            Assert.Contains("Copied 1", BeServerPath.CopyServerBin(server, sources).Single());
            Assert.Equal("custom battle dll", File.ReadAllText(Path.Combine(bin, BeServerPath.CustomBattleDll)));
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void AWrongUIExtenderExIsRefused()
    {
        var root = NewRoot("ui-hash");
        try
        {
            var sources = HostPc(root) with { UIExtenderExSha256 = BeServerPath.UIExtenderExSha256 };
            var error = Assert.Throws<InvalidOperationException>(() => BeServerPath.CopyServerBin(Path.Combine(root, "server"), sources));
            Assert.Contains("not the file this tool was made for", error.Message);
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void TheBundledUIExtenderExIsTheOfficialBuild()
    {
        var dll = Path.Combine(RepoRoot(), "compat", "BurningEmpiresCoopCompat", "server-extra", "UIExtenderEx-2.13.3", BeServerPath.UIExtenderExDll);
        if (!File.Exists(dll))
        {
            // Downloading it needs Coodsz's OK; until it is in the repo this check cannot run.
            output.WriteLine("Skipped: " + dll + " is not in the repo yet. See the README.txt in that folder.");
            return;
        }

        Assert.Equal(269824, new FileInfo(dll).Length);
        Assert.Equal(BeServerPath.UIExtenderExSha256, Sha256Files.Hash(dll), ignoreCase: true);
    }

    [Fact]
    public void BeSavedataSurvivesARestageAndSinglePlayerDataNeverReachesTheServer()
    {
        var root = NewRoot("restage");
        try
        {
            var sources = HostPc(root);
            var server = Path.Combine(root, "server");
            BeServerPath.StageCore(server, sources);
            var serverSlot = Write(Path.Combine(BeSaveData.Root(BeSaveData.ServerModule(server)), "campaigns", "c1", "save_slots", "BETest", "index.json"), "server data");
            var gameSlot = Write(Path.Combine(sources.GameRoot, "Modules", "BurningEmpires", "BESavedata", "campaigns", "c1", "save_slots", "BETest", "index.json"), "single player data");

            // A Burning Empires update on the host's PC.
            File.WriteAllText(Path.Combine(sources.GameRoot, "Modules", "BurningEmpires", "ModuleData", "settlements.xml"), "<Settlements updated=\"1\"/>");
            BeServerPath.StageCore(server, sources);

            Assert.Equal("server data", File.ReadAllText(serverSlot));
            Assert.Equal("<Settlements updated=\"1\"/>", File.ReadAllText(Path.Combine(BeSaveData.ServerModule(server), "ModuleData", "settlements.xml")));
            Assert.Equal("single player data", File.ReadAllText(gameSlot));
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void CoreModulesKeepTheirOwnSubModuleXmlAndExtrasGetClientCodeOff()
    {
        var root = NewRoot("client-code");
        try
        {
            var sources = HostPc(root);
            var server = Path.Combine(root, "server");
            BeServerPath.StageCore(server, sources);

            // No TurnOffClientCode and no TAOM dependency fix on core modules: their SubModule.xml stays byte for byte.
            foreach (var id in new[] { BeServerPath.BeModuleId, BeServerPath.CompatModuleId, "Bannerlord.UIExtenderEx", "Bannerlord.MBOptionScreen" })
            {
                var source = id switch
                {
                    BeServerPath.BeModuleId => Path.Combine(sources.GameRoot, "Modules", id),
                    BeServerPath.CompatModuleId => sources.CompatModule,
                    "Bannerlord.MBOptionScreen" => BeServerPath.McmSource(sources.GameRoot),
                    _ =>BeServerPath.WorkshopItem(sources.SteamLibraries, BeServerPath.Butr.Single(item => item.ModuleId == id).WorkshopId)!
                };
                Assert.Equal(File.ReadAllText(Path.Combine(source, "SubModule.xml")), File.ReadAllText(Path.Combine(BeServerPath.ModulesRoot(server), id, "SubModule.xml")));
            }

            var extra = Module(Path.Combine(root, "mods"), "ClientMod", "ClientMod", "v1.0.0", ClientSubModule("ClientMod"));
            Write(Path.Combine(extra, "bin", "Win64_Shipping_Client", "ClientMod.dll"), "x");
            var setup = new TaomSetup { Name = "Test", Extras = [new TaomExtraMod { ModuleId = "ClientMod", Name = "Client Mod", Enabled = true }] };
            var order = ServerOrder.Append("ClientMod").ToList();
            var found = new List<BannerlordCoopLauncher.Core.Profile.FoundMod> { new("Client Mod", "ClientMod", "", extra, "Modules") };
            var plan = BeServerPath.Plan(setup, order, order.Where(id => id != "DedicatedServer.Windows").ToList(), found);
            BeServerPath.StageExtras(server, plan);

            Assert.Contains("DedicatedServerType", File.ReadAllText(Path.Combine(BeServerPath.ModulesRoot(server), "ClientMod", "SubModule.xml")));
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void ImprovedGarrisonsCodeRunsOnTheServerWhenCoopModPatchIsInTheSetup()
    {
        // The same switch as the TAOM server tool: IG marks its code DedicatedServerType none, so
        // without it CoopModPatch's IG adapter cannot load ImprovedGarrisons.dll on the server.
        var root = NewRoot("coopmodpatch");
        try
        {
            var server = Path.Combine(root, "server");
            Directory.CreateDirectory(BeServerPath.ModulesRoot(server));
            var mods = Path.Combine(root, "mods");
            var igSub = "    <SubModule>\n      <Name value=\"ImprovedGarrisons\"/>\n      <DLLName value=\"ImprovedGarrisons.dll\"/>\n      <SubModuleClassType value=\"X.SubModule\"/>\n"
                + "      <Tags>\n        <Tag key=\"DedicatedServerType\" value=\"none\" />\n        <Tag key=\"IsNoRenderModeElement\" value=\"false\" />\n      </Tags>\n    </SubModule>\n";
            var ig = Module(mods, "ImprovedGarrisons", "ImprovedGarrisons", "v4.2.0.7", igSub);
            Write(Path.Combine(ig, "bin", "Win64_Shipping_Client", "ImprovedGarrisons.dll"), "client build");
            var patch = Module(mods, "CoopModPatch", "CoopModPatch", "v0.7.1", ClientSubModule("CoopModPatch"));
            Write(Path.Combine(patch, "bin", "Win64_Shipping_Server", "CoopModPatch.dll"), "x");
            var found = new List<BannerlordCoopLauncher.Core.Profile.FoundMod>
            {
                new("ImprovedGarrisons", "ImprovedGarrisons", "2859265386", ig, "Workshop"),
                new("CoopModPatch", "CoopModPatch", "3786391685", patch, "Workshop")
            };
            var setup = new TaomSetup
            {
                Name = "Test",
                Extras =
                [
                    new TaomExtraMod { ModuleId = "CoopModPatch", Name = "CoopModPatch", Enabled = true },
                    new TaomExtraMod { ModuleId = "ImprovedGarrisons", Name = "Improved Garrisons", Enabled = true }
                ]
            };
            var order = ServerOrder.Append("CoopModPatch").Append("ImprovedGarrisons").ToList();
            var serverXml = Path.Combine(BeServerPath.ModulesRoot(server), "ImprovedGarrisons", "SubModule.xml");

            var plan = BeServerPath.Plan(setup, order, order.Where(id => id != "DedicatedServer.Windows").ToList(), found);
            Assert.Equal(ServerCode.ServerBuild, plan.Extras.Single(extra => extra.Mod.ModuleId == "ImprovedGarrisons").Code);
            BeServerPath.StageExtras(server, plan);
            Assert.DoesNotContain("DedicatedServerType", File.ReadAllText(serverXml));
            Assert.Equal(ServerCode.ServerBuild, ServerModuleInstall.Classify(Path.GetDirectoryName(serverXml)!));
            Assert.Equal("client build", File.ReadAllText(Path.Combine(BeServerPath.ModulesRoot(server), "ImprovedGarrisons", "bin", "Win64_Shipping_Server", "ImprovedGarrisons.dll")));
            // The player's copy keeps its mark.
            Assert.Contains("DedicatedServerType", File.ReadAllText(Path.Combine(ig, "SubModule.xml")));

            // Without CoopModPatch the server copy gets the mod's own mark back.
            var without = order.Where(id => id != "CoopModPatch").ToList();
            var plain = BeServerPath.Plan(setup, without, without.Where(id => id != "DedicatedServer.Windows").ToList(), found);
            Assert.Equal(ServerCode.ClientCodeSkipped, plain.Extras.Single(extra => extra.Mod.ModuleId == "ImprovedGarrisons").Code);
            BeServerPath.StageExtras(server, plain);
            Assert.Contains("DedicatedServerType\" value=\"none\"", File.ReadAllText(serverXml));
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void AnExtraWithACoreIdIsNeverStagedAsAnExtra()
    {
        var setup = new TaomSetup { Name = "Test", Extras = [new TaomExtraMod { ModuleId = "BurningEmpires", Name = "BE", Enabled = true }] };
        var found = new List<BannerlordCoopLauncher.Core.Profile.FoundMod> { new("BE", "BurningEmpires", "", "C:\\nowhere", "Modules") };
        var plan = BeServerPath.Plan(setup, ServerOrder, ServerOrder, found);
        Assert.Empty(plan.Extras);
        Assert.Empty(plan.Missing);
    }

    [Fact]
    public void CoreProblemsNameTheMissingPiecesInPlainWords()
    {
        var root = NewRoot("core-check");
        try
        {
            var sources = HostPc(root);
            Assert.Empty(BeServerPath.CoreProblems(sources));

            Directory.Delete(BeServerPath.McmSource(sources.GameRoot), recursive: true);
            Directory.Delete(Path.Combine(sources.GameRoot, "Modules", BeServerPath.ElephantsModuleId), recursive: true);
            Module(Path.Combine(sources.GameRoot, "Modules"), BeServerPath.BeModuleId, BeServerPath.BeModuleId, "v2.0.1");
            var problems = BeServerPath.CoreProblems(sources);

            Assert.Contains(problems, line => line.Contains("v2.0.1") && line.Contains("nexusmods.com"));
            Assert.Contains(problems, line => line.Contains("Elephants"));
            Assert.Contains(problems, line => line.StartsWith("MCM is not on this PC.", StringComparison.Ordinal) && line.Contains("Nexus Mods"));
            Assert.Throws<InvalidOperationException>(() => BeServerPath.StageCore(Path.Combine(root, "server"), sources));
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void TheServerOrderIsCheckedBeforeStart()
    {
        Assert.Null(BeServerPath.OrderProblem(ServerOrder));
        // Elephants is core: a list without it is refused (live test, the Elephant extension popup in every battle).
        Assert.Contains("BurningEmpires_Elephants is missing", BeServerPath.OrderProblem(ServerOrder.Take(11).ToList()));
        Assert.Null(BeServerPath.OrderProblem(ServerOrder.Take(9).Append("SomeMod").Concat(ServerOrder.Skip(9)).Append("AfterMod").ToList()));

        var beFirst = ServerOrder.ToList();
        beFirst.Remove("BurningEmpires");
        beFirst.Insert(0, "BurningEmpires");
        Assert.NotNull(BeServerPath.OrderProblem(beFirst));

        var gap = ServerOrder.ToList();
        gap.Insert(10, "Between");
        Assert.Contains("directly before Burning Empires", BeServerPath.OrderProblem(gap));

        var noHarmony = ServerOrder.Skip(1).ToList();
        Assert.Contains("Bannerlord.Harmony is missing", BeServerPath.OrderProblem(noHarmony));

        var windowsLate = ServerOrder.ToList();
        windowsLate.Remove("DedicatedServer.Windows");
        windowsLate.Insert(7, "DedicatedServer.Windows");
        Assert.NotNull(BeServerPath.OrderProblem(windowsLate));

        var elephantsFirst = ServerOrder.ToList();
        elephantsFirst.Remove("BurningEmpires_Elephants");
        elephantsFirst.Insert(9, "BurningEmpires_Elephants");
        Assert.NotNull(BeServerPath.OrderProblem(elephantsFirst));

        Assert.Contains("twice", BeServerPath.OrderProblem(ServerOrder.Append("Coop").ToList()));
    }

    [Fact]
    public void WriteOrderWritesTheScriptsListAndRefusesAWrongOne()
    {
        var root = NewRoot("order");
        try
        {
            BeServerPath.WriteOrder(root, ServerOrder);
            var lines = File.ReadAllLines(BeServerPath.ListPath(root)).Where(line => !line.StartsWith('#')).ToList();
            Assert.Equal(ServerOrder, lines);
            Assert.Contains("\"BurningEmpires\": true", File.ReadAllText(BeServerPath.SelectionPath(root)));

            var wrong = ServerOrder.Reverse().ToList();
            Assert.Throws<InvalidOperationException>(() => BeServerPath.WriteOrder(root, wrong));
            Assert.Equal(ServerOrder, File.ReadAllLines(BeServerPath.ListPath(root)).Where(line => !line.StartsWith('#')).ToList());
        }
        finally
        {
            Delete(root);
        }
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "compat")))
            dir = dir.Parent;
        return dir?.FullName ?? AppContext.BaseDirectory;
    }
}
