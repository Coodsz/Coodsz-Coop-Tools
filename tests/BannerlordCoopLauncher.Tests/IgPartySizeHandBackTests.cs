using System.Text;
using BannerlordCoopLauncher.Core.Localization;
using BannerlordCoopLauncher.Core.Updates;
using Coodsz.IgPartySize;

namespace BannerlordCoopLauncher.Tests;

// The Improved Garrisons party size hand back: IG's party size model starts from the game's own model, so with IG
// after TAOM, TAOM's party sizes and the TAOM tool's party settings never ran. The decision
// (PartySizeHandBackRules.cs) is linked; the engine part (PartySizeHandBack.cs) is checked through its source, the
// three module builds and the shipped DLLs.
public sealed class IgPartySizeHandBackTests
{
    private const string TaomModel = "TAOM.Features.CulturalFeats.Models.TaomPartySizeModel";

    [Fact]
    public void Plain_Coop_and_AD1259_keep_Improved_Garrisons_as_it_is()
    {
        // Coop and AD1259 add no party size model, so the model before IG's is the game's own (or none).
        Assert.False(PartySizeHandBackRules.HandsBack(instanceIsIg: true, PartySizeHandBackRules.DefaultModelType));
        Assert.False(PartySizeHandBackRules.HandsBack(instanceIsIg: true, null));
        Assert.False(PartySizeHandBackRules.HandsBack(instanceIsIg: true, ""));
    }

    [Fact]
    public void Another_mods_model_before_Improved_Garrisons_wins()
    {
        Assert.True(PartySizeHandBackRules.HandsBack(instanceIsIg: true, TaomModel));
        Assert.True(PartySizeHandBackRules.HandsBack(instanceIsIg: true, "SomeMod.Models.BiggerPartiesModel"));
    }

    [Fact]
    public void Only_Improved_Garrisons_own_model_is_changed_and_never_into_a_loop()
    {
        // TAOM's own base call reaches the game's model with TAOM's instance; that stays the game's result.
        Assert.False(PartySizeHandBackRules.HandsBack(instanceIsIg: false, TaomModel));
        // A second IG model before IG's would hand back to itself.
        Assert.False(PartySizeHandBackRules.HandsBack(instanceIsIg: true, PartySizeHandBackRules.IgModelType));
    }

    [Fact]
    public void Names_match_Improved_Garrisons_4_2_0_7_and_Bannerlord_1_4_8()
    {
        Assert.Equal("ImprovedGarrisons.Models.GarrisonpartySizeLimitModel", PartySizeHandBackRules.IgModelType);
        Assert.Equal("TaleWorlds.CampaignSystem.GameComponents.DefaultPartySizeLimitModel", PartySizeHandBackRules.DefaultModelType);
        var source = Source("PartySizeHandBack.cs");
        Assert.Contains("\"GetPartyMemberSizeLimit\", parameters", source);
        Assert.Contains("\"CalculateGarrisonPartySizeLimit\"", source);
        Assert.Contains("typeof(MBGameModel<PartySizeLimitModel>)", source);
        Assert.Contains(".GetProperty(\"BaseModel\"", source);
    }

    [Fact]
    public void Special_parties_and_multipliers_stay_Improved_Garrisons_own()
    {
        var source = Source("PartySizeHandBack.cs");
        // IG's method is only changed right after its base call, so its own code still returns the transfer, guard,
        // recruiter and template party sizes and adds its multiplier lines on top of the previous model's result.
        Assert.Contains("harmony.Patch(igMemberLimit, transpiler: new HarmonyMethod(typeof(PartySizeHandBack), nameof(SwapBaseResult)))", source);
        Assert.DoesNotContain("harmony.Patch(igMemberLimit, prefix", source);
        Assert.Contains("instruction.Calls(_defaultMemberLimit)", source);
        Assert.Contains("yield return new CodeInstruction(OpCodes.Call, after);", source);
        // The previous model's own ExplainedNumber is used, so its tooltip lines stay.
        Assert.Contains("return previous.GetPartyMemberSizeLimit(party, includeDescriptions);", source);
        Assert.Contains("__result = previous.CalculateGarrisonPartySizeLimit(settlement, includeDescriptions);", source);
    }

    [Fact]
    public void Another_patch_on_Improved_Garrisons_method_keeps_the_hand_back()
    {
        // Harmony runs every transpiler again when any mod patches the same method. A static found flag made the
        // second run skip the swap, so the hand back was quietly lost.
        var source = Source("PartySizeHandBack.cs");
        Assert.Contains("var found = false;", source);
        Assert.Contains("if (found || _defaultMemberLimit == null || !instruction.Calls(_defaultMemberLimit))", source);
        Assert.DoesNotContain("if (_baseCallFound ||", source);
    }

    [Fact]
    public void A_failure_keeps_Improved_Garrisons_result_and_nothing_is_patched_without_it()
    {
        var source = Source("PartySizeHandBack.cs");
        Assert.Contains("Failed(e);\r\n                return baseResult;", source);
        Assert.Contains("Failed(e);\r\n                return true;", source);
        Assert.Contains("Log(PartySizeHandBackRules.NotLoadedLine);\r\n                    return;", source);
        Assert.Contains("PartySizeHandBackRules.ProcessKey", source);
        Assert.Contains("Improved Garrisons is not loaded", PartySizeHandBackRules.NotLoadedLine);
        Assert.Contains("TAOM.Features", PartySizeHandBackRules.HandBackLine(TaomModel));
    }

    [Fact]
    public void A_failed_garrison_patch_turns_off_only_the_garrison_part()
    {
        // The member swap is already live when the garrison patch goes on, so its failure must not say everything is off.
        var source = Source("PartySizeHandBack.cs");
        Assert.Contains("Log(PartySizeHandBackRules.GarrisonOffLine(e.GetType().Name + \": \" + e.Message));\r\n                    return;", source);
        var line = PartySizeHandBackRules.GarrisonOffLine("HarmonyException: boom");
        Assert.Contains("(HarmonyException: boom)", line);
        Assert.Contains("Other party sizes, garrison parties too, still build on the model before it.", line);
        Assert.DoesNotContain("off,", line);
    }

    [Fact]
    public void Client_and_both_servers_build_and_start_the_same_file()
    {
        var root = RepoRoot();
        var compat = Path.Combine(root, "compat");

        // Client: the Join Password add on all three client launchers ship, so the mod list does not change.
        var clientBuild = File.ReadAllText(Path.Combine(compat, "CoodszJoinPassword", "build.ps1"));
        Assert.Contains("'..\\ImprovedGarrisonsCoopCompat'", clientBuild);
        Assert.Contains("(Join-Path $igCompat 'PartySizeHandBack.cs') + (Join-Path $igCompat 'PartySizeHandBackRules.cs')", clientBuild);
        var client = File.ReadAllText(Path.Combine(compat, "CoodszJoinPassword", "src", "JoinPassword.cs"));
        Assert.Contains("if (GameNetwork.IsDedicatedServer) return;\r\n            Coodsz.IgPartySize.PartySizeHandBack.TryInstall(Log);", client);

        // TAOM server: Coodsz War Sync, which the TAOM server tool adds to DedicatedServer.Windows on every start.
        var warSync = File.ReadAllText(Path.Combine(compat, "CoodszWarSync", "CoodszWarSync.csproj"));
        Assert.Contains("<Compile Include=\"..\\ImprovedGarrisonsCoopCompat\\PartySizeHandBack.cs\"", warSync);
        Assert.Contains("<Compile Include=\"..\\ImprovedGarrisonsCoopCompat\\PartySizeHandBackRules.cs\"", warSync);
        Assert.Contains("Coodsz.IgPartySize.PartySizeHandBack.TryInstall(ServerLog)", File.ReadAllText(Path.Combine(compat, "CoodszWarSync", "src", "WarSyncSubModule.cs")));

        // Coop server: Improved Garrisons Coop Compat, which Coodsz Host adds when Improved Garrisons is on. Its
        // project compiles every file in its folder.
        Assert.DoesNotContain("EnableDefaultCompileItems", File.ReadAllText(Path.Combine(compat, "ImprovedGarrisonsCoopCompat", "ImprovedGarrisonsCoopCompat.csproj")));
        Assert.Contains("Coodsz.IgPartySize.PartySizeHandBack.TryInstall(Log)", File.ReadAllText(Path.Combine(compat, "ImprovedGarrisonsCoopCompat", "SubModule.cs")));

        // The shipped DLLs were rebuilt with it (string literals are stored as UTF-16).
        var key = Encoding.Unicode.GetBytes(PartySizeHandBackRules.ProcessKey);
        foreach (var dll in new[]
                 {
                     Path.Combine(compat, "CoodszJoinPassword", "module", "bin", "Win64_Shipping_Client", "CoodszJoinPassword.dll"),
                     Path.Combine(root, "src", "TaomCoopHost", "WarSync", "CoodszWarSync.dll"),
                     Path.Combine(root, "src", "CoodszHost", "PatchModules", "DedicatedServer.IGCoopCompat", "bin", "Win64_Shipping_Server", "DedicatedServer.IGCoopCompat.dll"),
                 })
            Assert.True(File.ReadAllBytes(dll).AsSpan().IndexOf(key) >= 0, Path.GetFileName(dll) + " has the party size hand back.");
    }

    [Fact]
    public void Whats_new_lines_are_ready_in_every_language()
    {
        foreach (var language in Translator.Languages.Where(language => language.Code != Translator.English))
        {
            var table = Translator.Entries(language.Code);
            Assert.True(table.ContainsKey(WhatsNew.TaomIgPartySize), language.Code + " has no translation for the TAOM line.");
            Assert.True(table.ContainsKey(WhatsNew.CoopIgPartySize), language.Code + " has no translation for the Coop line.");
        }
    }

    private static string Source(string file) =>
        File.ReadAllText(Path.Combine(RepoRoot(), "compat", "ImprovedGarrisonsCoopCompat", file));

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "BannerlordCoopLauncher.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
