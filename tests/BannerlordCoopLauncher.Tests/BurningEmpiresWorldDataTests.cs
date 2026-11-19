using System.Text.Json.Nodes;
using BurningEmpiresCoopCompat;

namespace BannerlordCoopLauncher.Tests;

// BurningEmpires.CoopCompat: the server marks the full world data it stores in a save, so a
// restart loads that copy and not an older single player snapshot, while Burning Empires' own
// small copy is never taken for it (WorldDataRules.cs, linked).
public sealed class BurningEmpiresWorldDataTests
{
    // A world data copy the way Burning Empires' Json.NET writes it (indented), cut short. One
    // settlement was renamed to the mark's own name, which must not count as the mark.
    private const string ServerWorld =
        "{\r\n  \"DataVersion\": 7,\r\n  \"CampaignUniqueGameId\": \"c1\",\r\n  \"SaveBranchId\": \"b1\",\r\n"
        + "  \"MinimalRecoveryReligionContractVersion\": 1,\r\n  \"SettlementPopulations\": {\r\n    \"town_EN1\": {\r\n      \"Groups\": []\r\n    }\r\n  },\r\n"
        + "  \"HeroReligions\": {},\r\n  \"KingdomReligions\": {},\r\n  \"SettlementCustomNames\": {\r\n    \"town_EN2\": \"BurningEmpiresCoopCompatServerCopy\"\r\n  }\r\n}";

    // Burning Empires' own small copy (BEWorldDataManager.CreateMinimalRecoverySnapshot), which a
    // single player save carries. It has no populations.
    private const string SmallCopy =
        "{\r\n  \"DataVersion\": 7,\r\n  \"CampaignUniqueGameId\": \"c1\",\r\n  \"SaveBranchId\": \"b1\",\r\n"
        + "  \"MinimalRecoveryReligionContractVersion\": 1,\r\n  \"SettlementPopulations\": {},\r\n  \"HeroReligions\": {},\r\n  \"KingdomReligions\": {}\r\n}";

    [Fact]
    public void The_copy_the_server_stores_is_marked_and_keeps_every_value()
    {
        var marked = WorldDataRules.MarkServerCopy(ServerWorld);

        Assert.True(WorldDataRules.IsServerCopy(marked));
        var after = JsonNode.Parse(marked)!.AsObject();
        Assert.True(after[WorldDataRules.ServerCopyProperty]!.GetValue<bool>());
        after.Remove(WorldDataRules.ServerCopyProperty);
        Assert.Equal(JsonNode.Parse(ServerWorld)!.ToJsonString(), after.ToJsonString());

        // A copy is never marked twice.
        Assert.Equal(marked, WorldDataRules.MarkServerCopy(marked));
    }

    [Fact]
    public void Burning_Empires_own_copy_is_not_the_servers()
    {
        Assert.False(WorldDataRules.IsServerCopy(SmallCopy));
        // The mark's name inside a value, here a renamed settlement, is not the mark.
        Assert.False(WorldDataRules.IsServerCopy(ServerWorld));
        // Only the first property counts, which is where the server puts it.
        Assert.False(WorldDataRules.IsServerCopy("{\"DataVersion\":7,\"BurningEmpiresCoopCompatServerCopy\":true}"));
        Assert.False(WorldDataRules.IsServerCopy("[\"BurningEmpiresCoopCompatServerCopy\"]"));
        Assert.False(WorldDataRules.IsServerCopy("{\"BurningEmpiresCoopCompatServerCopyX\":true}"));
        Assert.False(WorldDataRules.IsServerCopy(null));
        Assert.False(WorldDataRules.IsServerCopy(""));
        Assert.False(WorldDataRules.IsServerCopy("   "));
        Assert.True(WorldDataRules.IsServerCopy(" {\r\n  \"BurningEmpiresCoopCompatServerCopy\" : true }"));
    }

    [Fact]
    public void Text_that_is_not_a_json_object_is_left_as_it_is()
    {
        Assert.Null(WorldDataRules.MarkServerCopy(null));
        Assert.Equal("", WorldDataRules.MarkServerCopy(""));
        Assert.Equal("[1,2]", WorldDataRules.MarkServerCopy("[1,2]"));

        // An empty object gets the mark without a comma after it.
        var empty = WorldDataRules.MarkServerCopy("{ }");
        Assert.True(WorldDataRules.IsServerCopy(empty));
        Assert.Single(JsonNode.Parse(empty)!.AsObject());
    }

    [Fact]
    public void The_server_save_and_the_server_load_use_the_mark()
    {
        var source = SourceFiles.Read("compat/BurningEmpiresCoopCompat/Persistence/WorldDataTransfer.cs");

        // The copy is marked before it is stored, and the old single player manifest goes with it.
        var save = SourceFiles.Method(source, "private static void OnBeforeSavePostfix(");
        Assert.Contains("if (!Coop.OnServerProcess || DsSaveSafety.IsRestoreBlocked(__instance))", save);
        var mark = save.IndexOf("json = WorldDataRules.MarkServerCopy(json);", StringComparison.Ordinal);
        Assert.True(mark >= 0);
        Assert.True(mark < save.IndexOf("_setMinimalRecoveryJson.Invoke(__instance", StringComparison.Ordinal));
        Assert.Contains("_externalManifestJson?.SetValue(__instance, string.Empty);", save);

        // The server's load hides the manifest and save name only for a marked copy.
        var read = SourceFiles.Method(source, "private static void ReadSnapshotPrefix(");
        Assert.Contains("if (!WorldDataRules.IsServerCopy(minimalRecoverySnapshotJson))", read);

        // The dedicated server is asked about first, so it never takes the player's path.
        var load = SourceFiles.Method(source, "private static void OnGameLoadedPrefix(");
        Assert.True(load.IndexOf("Coop.OnServerProcess", StringComparison.Ordinal) < load.IndexOf("Coop.OnPlayerPc", StringComparison.Ordinal));
        Assert.Contains("_serverLoadRunning = false;", SourceFiles.Method(source, "private static Exception OnGameLoadedFinalizer("));
    }

    [Fact]
    public void Only_Burning_Empires_own_recovery_notice_is_dropped_on_the_server()
    {
        Assert.True(WorldDataRules.IsOnlyRecoveryNotice("{=BEExternalSaveMrsText}No usable external data snapshot could be loaded for this save. The game has loaded the minimal recovery data stored inside the .sav file."));
        // Burning Empires joins two warnings into one translated text, so the other one stays.
        Assert.False(WorldDataRules.IsOnlyRecoveryNotice("No usable external data snapshot could be loaded for this save.\r\n\r\nThe player's formal religion was restored."));
        Assert.False(WorldDataRules.IsOnlyRecoveryNotice("{=BEExternalSaveUnavailableText}All same-slot external snapshots are unavailable."));
        Assert.False(WorldDataRules.IsOnlyRecoveryNotice(""));
        Assert.False(WorldDataRules.IsOnlyRecoveryNotice(null));
    }
}
