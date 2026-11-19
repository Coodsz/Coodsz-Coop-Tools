using static BurningEmpires.Core.Tests.BeServerFixtures;

namespace BurningEmpires.Core.Tests;

public class BeServerPrepareTests
{
    [Fact]
    public void PrepareCopiesTheWhitelistAndLeavesOtherToolsFilesOut()
    {
        var root = NewRoot("prepare");
        try
        {
            var workshop = WorkshopServer(root);
            var server = Path.Combine(root, "server");

            BeServerPrepare.Prepare(workshop, server);

            Assert.True(File.Exists(Path.Combine(server, BeServerPrepare.ScriptName)));
            Assert.True(File.Exists(Path.Combine(server, BeServerPrepare.RuntimeBootstrap)));
            Assert.True(File.Exists(Path.Combine(server, "engine", "dotnet", "dotnet.exe")));
            Assert.True(File.Exists(Path.Combine(server, "engine", "Modules", "Coop", "bin", "Win64_Shipping_Server", "Coop.Core.dll")));
            Assert.True(File.Exists(Path.Combine(server, "engine", "Modules", "DedicatedServer.Windows", "SubModule.xml")));
            Assert.Null(BeServerPrepare.MissingPiece(server));

            Assert.False(Directory.Exists(Path.Combine(server, "removed-mods")));
            Assert.False(Directory.Exists(Path.Combine(server, "server-data")));
            Assert.False(Directory.Exists(Path.Combine(server, "engine", "Modules", "AD1259")));
            Assert.False(Directory.Exists(Path.Combine(server, "engine", "Modules", "DedicatedServer.AD1259CoopCompat")));
            Assert.False(Directory.Exists(Path.Combine(server, "engine", "Modules", "RBM")));
            Assert.False(Directory.Exists(Path.Combine(server, "engine", "Modules", "CoopModPatch")));
            Assert.False(File.Exists(Path.Combine(server, "server-module-policy.json.coopmodpatch.bak")));
            Assert.False(File.Exists(Path.Combine(server, "Start-ModdedServer.ps1.coodsz-old")));
            Assert.False(File.Exists(Path.Combine(server, "Coodsz-Start-ModdedServer.ps1")));
            Assert.False(File.Exists(Path.Combine(server, "modded-server-modules.txt")));
            Assert.False(File.Exists(Path.Combine(server, BeServerPrepare.PolicyFile)));
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void PrepareAgainCopiesNothingNewAndDeletesNothing()
    {
        var root = NewRoot("prepare-again");
        try
        {
            var workshop = WorkshopServer(root);
            var server = Path.Combine(root, "server");
            BeServerPrepare.Prepare(workshop, server);
            var saveData = Write(Path.Combine(server, "engine", "Modules", "BurningEmpires", "BESavedata", "campaigns", "c1", "save_slots", "BETest", "index.json"), "{}");
            var hostFile = Write(Path.Combine(server, "notes.txt"), "mine");

            var again = BeServerPrepare.Prepare(workshop, server);

            Assert.Equal(0, again.Copied);
            Assert.True(again.Same > 0);
            Assert.True(File.Exists(saveData));
            Assert.True(File.Exists(hostFile));
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void ACoopUpdateIsSeenAndCopiedWithTheServer()
    {
        var root = NewRoot("coop-update");
        try
        {
            var workshop = WorkshopServer(root);
            var server = Path.Combine(root, "server");
            BeServerPrepare.Prepare(workshop, server);
            Assert.False(BeServerPrepare.CoopChanged(workshop, server));

            Write(Path.Combine(workshop, "engine", "Modules", "Coop", "bin", "Win64_Shipping_Server", "Coop.Core.dll"), "coop core 2, a new build");
            Write(Path.Combine(workshop, "release-info.txt"), "release-info.txt 2");
            Assert.True(BeServerPrepare.CoopChanged(workshop, server));

            BeServerPrepare.Prepare(workshop, server);
            Assert.False(BeServerPrepare.CoopChanged(workshop, server));
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void AServerFolderInsideTheWorkshopFolderIsRefused()
    {
        var root = NewRoot("inside");
        try
        {
            var workshop = WorkshopServer(root);
            Assert.Throws<InvalidOperationException>(() => BeServerPrepare.Prepare(workshop, Path.Combine(workshop, "mine")));
            Assert.Throws<InvalidOperationException>(() => BeServerPrepare.Prepare(Path.Combine(root, "nothing"), Path.Combine(root, "server")));
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void EnsureDataMakesTheConfigsAndTemplateOnceAndKeepsTheHostsOwn()
    {
        var root = NewRoot("data");
        try
        {
            var workshop = WorkshopServer(root);
            var server = Path.Combine(root, "server");
            BeServerPrepare.Prepare(workshop, server);
            var coopData = Path.Combine(root, "CoopData");
            var data = Path.Combine(coopData, "DedicatedServer");

            var made = BeServerPrepare.EnsureData(server, data, coopData);

            Assert.Equal(3, made.Count);
            Assert.Contains("\"port\": 4200", File.ReadAllText(BeHostFolders.ServerConfig(data)));
            Assert.Contains("battleSize", File.ReadAllText(BeHostFolders.ModConfig(coopData)));
            Assert.True(File.Exists(Path.Combine(BeHostFolders.GameSaves(data), BeServerPrepare.TemplateSave)));

            File.WriteAllText(BeHostFolders.ServerConfig(data), "{\"password\":\"mine\"}");
            Assert.Empty(BeServerPrepare.EnsureData(server, data, coopData));
            Assert.Equal("{\"password\":\"mine\"}", File.ReadAllText(BeHostFolders.ServerConfig(data)));
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void ThePolicyIsTheToolsOwnWithMcmHeadless()
    {
        var root = NewRoot("policy");
        try
        {
            BeServerPrepare.WritePolicy(root);
            var text = File.ReadAllText(Path.Combine(root, BeServerPrepare.PolicyFile));
            Assert.Contains("\"Bannerlord.MBOptionScreen\"", text);
            Assert.DoesNotContain("CoopModPatch", text);
            Assert.Contains("\"allowUnsupportedMods\": false", text);
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void ANewerCompatModuleDoesNotHideACleanlyStoppedCampaign()
    {
        // The script skips a rolling module in its exact version check and moves the save to the
        // installed version, as Coodsz Host's policy does for CoopModPatch.
        using var policy = System.Text.Json.JsonDocument.Parse(BeServerPrepare.Policy);
        var rolling = policy.RootElement.GetProperty("rollingSaveVersionModules").EnumerateArray().Select(item => item.GetString()).ToList();
        Assert.Equal(new[] { BeStack.CompatModuleId }, rolling);
        var script = BannerlordCoopLauncher.Tests.SourceFiles.Read(Path.Combine("src", "CoodszHost", "DedicatedServerRepair", BeServerPrepare.ScriptName));
        Assert.Contains("if ($moduleId -in $rollingSaveVersionModules -or", script);
        Assert.Contains("foreach ($rollingModuleId in $rollingSaveVersionModules) {", script);
    }

    [Fact]
    public void MissingPieceNamesWhatPrepareHasToFix()
    {
        var root = NewRoot("missing");
        try
        {
            Assert.Contains("Prepare server", BeServerPrepare.MissingPiece(root));
        }
        finally
        {
            Delete(root);
        }
    }
}
