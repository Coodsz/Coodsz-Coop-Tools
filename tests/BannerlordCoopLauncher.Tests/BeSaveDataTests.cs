using System.Text.Json.Nodes;
using static BurningEmpires.Core.Tests.BeServerFixtures;

namespace BurningEmpires.Core.Tests;

public class BeSaveDataTests
{
    private static string Slot(string module, string campaign, string save, string text) =>
        Write(Path.Combine(BeSaveData.Root(module), "campaigns", campaign, "save_slots", save, "snapshots", "s1", "world.json"), text);

    // A snapshot manifest the way Burning Empires' Json.NET writes it (indented), cut short.
    private static string Manifest(string save, string snapshot) =>
        "{\r\n  \"DataVersion\": 7,\r\n  \"CampaignUniqueGameId\": \"c1\",\r\n  \"SaveBranchId\": \"b1\",\r\n  \"SnapshotId\": \"" + snapshot + "\",\r\n"
        + "  \"SaveName\": \"" + save + "\",\r\n  \"CreatedUtc\": \"2026-10-25T14:03:11.5512345Z\",\r\n  \"PopulationHash\": \"ab12\",\r\n"
        + "  \"FallbackSnapshots\": [\r\n    \"s0\"\r\n  ]\r\n}";

    private static string SnapshotFolder(string module, string campaign, string save, string snapshot) =>
        Path.Combine(BeSaveData.Root(module), "campaigns", campaign, "save_slots", save, "snapshots", snapshot);

    private static void Snapshot(string module, string campaign, string save, string snapshot)
    {
        var folder = SnapshotFolder(module, campaign, save, snapshot);
        Write(Path.Combine(folder, "manifest.json"), Manifest(save, snapshot));
        Write(Path.Combine(folder, "population.json.gz"), "populations of " + snapshot);
    }

    private static JsonObject ManifestIn(string module, string campaign, string save, string snapshot) =>
        JsonNode.Parse(File.ReadAllText(Path.Combine(SnapshotFolder(module, campaign, save, snapshot), "manifest.json")))!.AsObject();

    [Fact]
    public void BackupAndRestoreCarryTheSavesSlotFolders()
    {
        var root = NewRoot("save-backup");
        try
        {
            var module = Path.Combine(root, "BurningEmpires");
            Slot(module, "c1", "BETest", "good");
            Slot(module, "c1", "Other", "other");
            var backup = Path.Combine(root, "backup");

            Assert.Equal(1, BeSaveData.Backup(module, "BETest.sav", backup));
            Assert.True(File.Exists(Path.Combine(BeSaveData.Root(backup), "campaigns", "c1", "save_slots", "BETest", "snapshots", "s1", "world.json")));
            Assert.False(Directory.Exists(Path.Combine(BeSaveData.Root(backup), "campaigns", "c1", "save_slots", "Other")));

            var world = Slot(module, "c1", "BETest", "broken");
            var newer = Write(Path.Combine(Path.GetDirectoryName(world)!, "newer.json"), "kept");
            Assert.Equal(1, BeSaveData.Restore(backup, module, "BETest"));
            Assert.Equal("good", File.ReadAllText(world));
            Assert.True(File.Exists(newer));
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void RestoreIsRefusedWhileTheServerRunsOnEmptyData()
    {
        var root = NewRoot("save-blocked");
        try
        {
            var module = Path.Combine(root, "BurningEmpires");
            var world = Slot(module, "c1", "BETest", "current");
            var backup = Path.Combine(root, "backup");
            BeSaveData.Backup(module, "BETest", backup);
            File.WriteAllText(world, "newer");
            var log = "[BurningEmpires.CoopCompat] ERROR Save safety: Burning Empires could not restore this save's data (load failed) and runs on empty data.";

            Assert.True(BeSaveData.RestoreBlocked(log));
            Assert.True(BeSaveData.RestoreBlocked("ERROR Save safety: Burning Empires snapshot for \"BETest\" skipped, its data was not restored (load failed)."));
            Assert.False(BeSaveData.RestoreBlocked("[BurningEmpires.CoopCompat] Burning Empires coop patch ready (125 hooks)."));
            var error = Assert.Throws<InvalidOperationException>(() => BeSaveData.Restore(backup, module, "BETest", log));
            Assert.Equal(BeSaveData.RestoreBlockedReason, error.Message);
            Assert.Equal("newer", File.ReadAllText(world));
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void RenameCopyAndDeleteFollowTheSave()
    {
        var root = NewRoot("save-rename");
        try
        {
            var module = Path.Combine(root, "BurningEmpires");
            Slot(module, "c1", "Old", "one");
            Slot(module, "c2", "Old", "two");

            Assert.Equal(2, BeSaveData.Rename(module, "Old", "New"));
            Assert.False(BeSaveData.Has(module, "Old"));
            Assert.Equal(2, BeSaveData.SlotFolders(module, "New").Count);

            Assert.Equal(2, BeSaveData.CopyTo(module, "New", "Copy"));
            Assert.True(BeSaveData.Has(module, "New"));
            Assert.Throws<InvalidOperationException>(() => BeSaveData.CopyTo(module, "New", "Copy"));
            Assert.Throws<InvalidOperationException>(() => BeSaveData.Rename(module, "New", "Copy"));

            Assert.Equal(2, BeSaveData.Delete(module, "Copy"));
            Assert.False(BeSaveData.Has(module, "Copy"));
            Assert.True(BeSaveData.Has(module, "New"));
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void ASlotMovedToANewSaveNameCarriesThatNameInItsSnapshots()
    {
        // Burning Empires refuses a snapshot whose manifest names another save, so a server copy
        // named RomeCoop would load the small copy inside the .sav instead of its snapshot.
        var root = NewRoot("save-manifest");
        try
        {
            var module = Path.Combine(root, "BurningEmpires");
            Snapshot(module, "c1", "Rome", "s1");
            Snapshot(module, "c1", "Rome", "s2");

            Assert.Equal(1, BeSaveData.Rename(module, "Rome", "RomeCoop"));
            var moved = ManifestIn(module, "c1", "RomeCoop", "s1");
            Assert.Equal("RomeCoop", moved["SaveName"]!.GetValue<string>());
            Assert.Equal("RomeCoop", ManifestIn(module, "c1", "RomeCoop", "s2")["SaveName"]!.GetValue<string>());
            // Everything else in the manifest and the snapshot files stays as Burning Empires wrote it.
            Assert.Equal("s1", moved["SnapshotId"]!.GetValue<string>());
            Assert.Equal("2026-10-25T14:03:11.5512345Z", moved["CreatedUtc"]!.GetValue<string>());
            Assert.Equal("ab12", moved["PopulationHash"]!.GetValue<string>());
            Assert.Equal("s0", moved["FallbackSnapshots"]![0]!.GetValue<string>());
            Assert.Equal(7, moved["DataVersion"]!.GetValue<int>());
            Assert.Equal("populations of s1", File.ReadAllText(Path.Combine(SnapshotFolder(module, "c1", "RomeCoop", "s1"), "population.json.gz")));
            Assert.Empty(Directory.GetFiles(BeSaveData.Root(module), "*.coodsz-new", SearchOption.AllDirectories));

            // A copy gets its own name and the slot it came from keeps its name.
            Assert.Equal(1, BeSaveData.CopyTo(module, "RomeCoop", "RomeTest"));
            Assert.Equal("RomeTest", ManifestIn(module, "c1", "RomeTest", "s1")["SaveName"]!.GetValue<string>());
            Assert.Equal("RomeCoop", ManifestIn(module, "c1", "RomeCoop", "s1")["SaveName"]!.GetValue<string>());
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void ASinglePlayerSaveCopiedToTheServerUnderANewNameKeepsItsSnapshot()
    {
        // The way Name server copy carries the data (MainWindow.Saves.cs CarryBeSaveData): back up
        // the game's slot, rename it in a scratch folder, restore it into the server's folder.
        var root = NewRoot("save-carry");
        try
        {
            var game = Path.Combine(root, "game", "BurningEmpires");
            var server = Path.Combine(root, "server", "BurningEmpires");
            var scratch = Path.Combine(root, "scratch");
            Snapshot(game, "c1", "Rome", "s1");

            Assert.Equal(1, BeSaveData.Backup(game, "Rome", scratch));
            Assert.Equal(1, BeSaveData.Rename(scratch, "Rome", "RomeCoop"));
            Assert.Equal(1, BeSaveData.Restore(scratch, server, "RomeCoop"));

            Assert.Equal("RomeCoop", ManifestIn(server, "c1", "RomeCoop", "s1")["SaveName"]!.GetValue<string>());
            Assert.Equal("Rome", ManifestIn(game, "c1", "Rome", "s1")["SaveName"]!.GetValue<string>());
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void OnlyAManifestThatNamesTheOldSaveIsChanged()
    {
        // Burning Empires trims the name and ignores its case.
        Assert.Contains("\"SaveName\": \"RomeCoop\"", BeSaveData.PointManifestAt(Manifest(" rome ", "s1"), "Rome", "RomeCoop"));
        Assert.Contains("\"SaveName\": \"YÜZÜK\"", BeSaveData.PointManifestAt(Manifest("Rome", "s1"), "Rome.sav", "YÜZÜK"));
        Assert.Null(BeSaveData.PointManifestAt(Manifest("Other", "s1"), "Rome", "RomeCoop"));
        Assert.Null(BeSaveData.PointManifestAt("{\"SnapshotId\": \"s1\"}", "Rome", "RomeCoop"));
        Assert.Null(BeSaveData.PointManifestAt("{\"SaveName\": 5}", "Rome", "RomeCoop"));
        Assert.Null(BeSaveData.PointManifestAt("[\"Rome\"]", "Rome", "RomeCoop"));
        Assert.Null(BeSaveData.PointManifestAt("not json", "Rome", "RomeCoop"));
    }

    [Fact]
    public void ASaveNameCannotReachOutsideItsSlotFolder()
    {
        Assert.Throws<InvalidOperationException>(() => BeSaveData.SlotName("..\\..\\x"));
        Assert.Throws<InvalidOperationException>(() => BeSaveData.SlotName(".."));
        Assert.Throws<InvalidOperationException>(() => BeSaveData.SlotName(""));
        Assert.Equal("BETest", BeSaveData.SlotName(" BETest.sav "));
    }

    [Fact]
    public void TheListingHasNamesAndSizesButNoContent()
    {
        var root = NewRoot("save-listing");
        try
        {
            var module = Path.Combine(root, "BurningEmpires");
            Slot(module, "c1", "BETest", "secret world data");
            var listing = BeSaveData.Listing(module);
            Assert.Contains("world.json", listing);
            Assert.Contains("17 bytes", listing);
            Assert.DoesNotContain("secret", listing);
            Assert.Contains("no BESavedata folder", BeSaveData.Listing(Path.Combine(root, "none")));
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void OnlyBurningEmpiresSavesOnTheRightVersionCanBeHosted()
    {
        var root = NewRoot("save-version");
        try
        {
            var be = Save(root, "BEGame", "Native;SandBoxCore;Sandbox;Coop;BurningEmpires.CoopCompat;BurningEmpires", "v2.0.1");
            var vanilla = Save(root, "Vanilla", "Native;SandBoxCore;Sandbox;Coop", null);
            var old = Save(root, "OldBE", "Native;BurningEmpires", "v1.9.3");
            var template = Save(root, "default_new_game", "Native;SandBoxCore;Sandbox", null);

            Assert.True(BeSaveVersion.IsBeSave(be));
            Assert.False(BeSaveVersion.IsBeSave(vanilla));
            Assert.Null(BeSaveVersion.Problem(be, "v2.0.2"));
            Assert.Contains("not made with Burning Empires", BeSaveVersion.Problem(vanilla, "v2.0.2"));
            Assert.Contains("1.9.3", BeSaveVersion.Problem(old, "v2.0.2"));
            Assert.Equal(BeServerLaunch.TemplateReason, BeSaveVersion.Problem(template, "v2.0.2"));
            Assert.NotNull(BeSaveVersion.Problem(Path.Combine(root, "missing.sav"), "v2.0.2"));
        }
        finally
        {
            Delete(root);
        }
    }
}
