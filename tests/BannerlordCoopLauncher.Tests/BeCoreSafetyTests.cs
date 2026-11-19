using System.IO.Compression;
using BannerlordCoopLauncher.Core.Profile;
using BannerlordCoopLauncher.Core.Support;
using static BurningEmpires.Core.Tests.BeServerFixtures;

namespace BurningEmpires.Core.Tests;

// The client log zip scrubs server passwords, and the shared Data folder only gives Burning
// Empires saves to the Burning Empires host.
public class BeCoreSafetyTests
{
    [Fact]
    public void TheClientLogZipTakesOutThePasswordOfEveryServerOnThisPc()
    {
        var root = NewRoot("client-pack-password");
        try
        {
            var data = Path.Combine(root, "data");
            var other = Path.Combine(root, "other-data");
            Write(BeHostFolders.ServerConfig(data), "{\n  \"saveName\": \"BETest\",\n  \"password\": \"xK9pQ2zzLm\"\n}\n");
            Write(BeHostFolders.ServerConfig(other), "{ \"password\": \"\" }");
            // The log repeats the password without a password key, so only the known secret catches it.
            Write(Path.Combine(BeHostFolders.Logs(data), "Coop_server.log"), "join code xK9pQ2zzLm accepted for a player");
            var servers = new[] { new BeServerFolder(Path.Combine(root, "server"), data), new BeServerFolder(null, other) };

            Assert.Equal(["xK9pQ2zzLm"], BeClientLogPack.ServerPasswords(servers));
            var limits = BeClientLogPack.WithServerPasswords(new LogZipLimits { Secrets = ["known"] }, servers);
            Assert.Contains("known", limits.Secrets);
            Assert.Contains("xK9pQ2zzLm", limits.Secrets);

            var zipPath = Path.Combine(root, "client.zip");
            BeClientLogPack.Create(zipPath, null, Path.Combine(root, "documents"), Path.Combine(root, "programdata"), servers: servers);
            using var zip = ZipFile.OpenRead(zipPath);
            var log = zip.Entries.Single(entry => entry.FullName.EndsWith("Coop_server.log", StringComparison.Ordinal));
            using var reader = new StreamReader(log.Open());
            var text = reader.ReadToEnd();
            Assert.Contains("accepted for a player", text);
            Assert.DoesNotContain("xK9pQ2zzLm", text);
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void OnlyBurningEmpiresSavesPassTheSharedDataFolderFilter()
    {
        var root = NewRoot("save-filter");
        try
        {
            var saves = Path.Combine(root, "Game Saves");
            var server = Path.Combine(root, "server");
            var beModule = BeSaveData.ServerModule(server);
            var be = Save(saves, "MyBE", "Native;SandBoxCore;Sandbox;Coop;BurningEmpires.CoopCompat;BurningEmpires", "v2.0.2");
            var taom = Save(saves, "TaomCampaign", "Native;SandBoxCore;Sandbox;Coop;TheArmiesOfMiddleEarth", null);
            var vanillaWithData = Save(saves, "Renamed", "Native;SandBoxCore;Sandbox", null);
            Write(Path.Combine(beModule, "BESavedata", "campaigns", "c1", "save_slots", "Renamed", "index.json"), "{}");

            Assert.True(BeSaveVersion.IsBeOwned(be, beModule));
            Assert.False(BeSaveVersion.IsBeOwned(taom, beModule));
            Assert.True(BeSaveVersion.IsBeOwned(vanillaWithData, beModule));
            Assert.False(BeSaveVersion.IsBeOwned(vanillaWithData, null));
            Assert.False(BeSaveVersion.IsBeOwned(Path.Combine(saves, "missing.sav"), beModule));
            Assert.Equal([be, vanillaWithData], BeSaveVersion.OnlyBeSaves([be, taom, vanillaWithData], beModule));
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void TheStartNeverPointsTheConfigAtAnotherToolsSave()
    {
        var root = NewRoot("launch-filter");
        try
        {
            var saves = Path.Combine(root, "Game Saves");
            Write(Path.Combine(root, BeServerPrepare.ScriptName), "script");
            Save(saves, "TaomCampaign", "Native;SandBoxCore;Sandbox;Coop;TheArmiesOfMiddleEarth", null);
            Save(saves, "MyBE", "Native;Coop;BurningEmpires", "v2.0.2");

            var error = Assert.Throws<InvalidOperationException>(() => BeServerLaunch.Build(root, new BeStartOptions { SaveName = "TaomCampaign" }, saves));
            Assert.Contains("not made with Burning Empires", error.Message);
            Assert.Contains("-SaveName \"MyBE\"", BeServerLaunch.Build(root, new BeStartOptions { SaveName = "MyBE" }, saves).Arguments);
            // A new campaign and a start without the Game Saves folder are not checked here.
            Assert.Contains("-NewSaveName \"Fresh\"", BeServerLaunch.Build(root, new BeStartOptions { SaveName = "Fresh", NewCampaign = true }, saves).Arguments);
            Assert.Contains("-SaveName \"TaomCampaign\"", BeServerLaunch.Build(root, new BeStartOptions { SaveName = "TaomCampaign" }).Arguments);
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void TheHostReportsUsePlainNamesAndTheRealButtonNames()
    {
        var restore = BeHostReport.Explain("[BurningEmpires.CoopCompat] ERROR Save safety: Burning Empires could not restore this save's data (load failed).\n", wasServing: true, exited: false)!.Value;
        Assert.Contains("Restore save backup", restore.Lines[1]);
        Assert.Contains("Do not let players play on in this state. Their progress will not keep the Burning Empires data.", restore.Lines[2]);

        var dependency = BeHostReport.Explain("BurningEmpires depends on Bannerlord.ButterLib\n", wasServing: true)!.Value;
        var text = string.Join(" ", dependency.Lines);
        Assert.Contains("Burning Empires needs ButterLib", text);
        Assert.Contains("the four helper mods Harmony, ButterLib, UIExtenderEx and MCM", text);
        Assert.Contains("Click Install mods on the Mods page for Harmony, ButterLib and UIExtenderEx, and get MCM from Nexus Mods.", text);

        // Install mods never gets MCM, so a missing MCM points at Nexus Mods instead.
        var mcm = string.Join(" ", BeHostReport.Explain("BurningEmpires depends on Bannerlord.MBOptionScreen\n", wasServing: true)!.Value.Lines);
        Assert.Contains("Burning Empires needs MCM", mcm);
        Assert.Contains("Get MCM v5.12.3 or newer from Nexus Mods and put it in the game's Modules folder.", mcm);
        Assert.Contains("https://www.nexusmods.com/mountandblade2bannerlord/mods/612", mcm);
        Assert.DoesNotContain("Install mods", mcm);

        var logs = new[]
        {
            "[BurningEmpires.CoopCompat] Burning Empires coop patch could not hook Burning Empires: x\n",
            "System.IO.FileNotFoundException: Could not load file or assembly 'Bannerlord.UIExtenderEx, Version=2.13.3.0'.\n[BE host] engine exited with code 2\n",
            "Save 'x' was not found or is incompatible\n",
            "BurningEmpires could not be loaded correctly\n",
            "module BurningEmpires is already loaded\n",
            "System.NullReferenceException at BurningEmpires.SubModule\n[BE host] engine exited with code 3\n",
            "[BE host] engine exited with code 0\n",
            "[BE host] engine exited with code 5\n"
        };
        foreach (var log in logs)
        {
            var lines = string.Join(" ", BeHostReport.Explain(log, wasServing: log.Contains("code 0"))!.Value.Lines);
            Assert.DoesNotContain("BESavedata", lines);
            Assert.DoesNotContain("Press ", lines);
            Assert.DoesNotContain("co-op", lines, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("says:", lines);
            Assert.DoesNotContain("unzip", lines, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void TheServerCoreCheckNamesHelperModsAndNexusWithoutJargon()
    {
        var root = NewRoot("core-copy");
        try
        {
            var sources = HostPc(root) with { SteamLibraries = [Path.Combine(root, "no-steam")] };
            Directory.Delete(Path.Combine(sources.GameRoot, "Modules", BeServerPath.BeModuleId), recursive: true);
            Directory.Delete(BeServerPath.McmSource(sources.GameRoot), recursive: true);

            var problems = BeServerPath.CoreProblems(sources);

            Assert.Contains(problems, line => line.Contains("Burning Empires is on Nexus Mods at https://www.nexusmods.com/", StringComparison.Ordinal));
            // The server tool has its own Install or repair Burning Empires button in step 3.
            Assert.Contains(problems, line => line.Contains("click Install or repair Burning Empires in step 3.", StringComparison.Ordinal));
            Assert.DoesNotContain(problems, line => line.Contains("in the client", StringComparison.Ordinal));
            Assert.Contains(problems, line => line.StartsWith("Harmony is not downloaded.", StringComparison.Ordinal) && line.Contains("Click Install mods on the Mods page"));
            // MCM comes from Nexus Mods (its official Workshop item was removed), so its line names the page, not Install mods.
            Assert.Contains(problems, line => line.StartsWith("MCM is not on this PC.", StringComparison.Ordinal)
                && line.EndsWith("MCM is on Nexus Mods at https://www.nexusmods.com/mountandblade2bannerlord/mods/612", StringComparison.Ordinal));
            Assert.DoesNotContain(problems, line => line.StartsWith("MCM is not downloaded.", StringComparison.Ordinal));
            Assert.DoesNotContain(problems, line => line.Contains(RetiredWorkshopItems.For("Bannerlord.MBOptionScreen")!) || line.Contains("Bannerlord.MBOptionScreen") || line.Contains("BUTR") || line.Contains("Nexus Mods ("));
        }
        finally
        {
            Delete(root);
        }
    }
}
