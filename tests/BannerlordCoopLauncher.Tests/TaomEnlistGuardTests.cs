using System.Text;
using Coodsz.EnlistGuard;

namespace BannerlordCoopLauncher.Tests;

// The TAOM enlist guard in the Join Password client module: TAOM 2.0.29.5 only enlists on the server, so a
// Coop client that swears the oath attacks the lord instead. The rule (TaomEnlistGuardRules.cs) is linked;
// the engine part is checked through its source, the build script and the built DLL.
public sealed class TaomEnlistGuardTests
{
    [Fact]
    public void Guard_is_off_without_taom()
    {
        Assert.False(TaomEnlistGuardRules.TaomLoaded(["TaleWorlds.Core", "Coop.Core", "GameInterface"]));
        Assert.False(TaomEnlistGuardRules.ShouldGuard(dedicatedServer: false, coopServer: false, coopClientStarted: true, taomLoaded: false));
    }

    [Fact]
    public void Taom_side_modules_alone_do_not_count_as_taom()
    {
        Assert.False(TaomEnlistGuardRules.TaomLoaded(["TAOM.Dependencies", "TAOM.CoopCompat", "TAOM.CoopCompat.Bootstrap", null]));
        Assert.True(TaomEnlistGuardRules.TaomLoaded(["Coop.Core", "TAOM"]));
        Assert.True(TaomEnlistGuardRules.TaomLoaded(["taom"]));
    }

    [Fact]
    public void Guard_runs_only_on_a_coop_client_with_taom()
    {
        Assert.True(TaomEnlistGuardRules.ShouldGuard(dedicatedServer: false, coopServer: false, coopClientStarted: true, taomLoaded: true));
        // The server, a Coop server and single player (no Coop connect in this game) are left alone.
        Assert.False(TaomEnlistGuardRules.ShouldGuard(dedicatedServer: true, coopServer: true, coopClientStarted: false, taomLoaded: true));
        Assert.False(TaomEnlistGuardRules.ShouldGuard(dedicatedServer: false, coopServer: true, coopClientStarted: false, taomLoaded: true));
        Assert.False(TaomEnlistGuardRules.ShouldGuard(dedicatedServer: false, coopServer: false, coopClientStarted: false, taomLoaded: true));
        Assert.False(TaomEnlistGuardRules.ShouldGuard(dedicatedServer: true, coopServer: false, coopClientStarted: true, taomLoaded: true));
    }

    [Fact]
    public void Ids_match_taom_2_0_29_5()
    {
        Assert.Equal("TAOM", TaomEnlistGuardRules.TaomAssembly);
        Assert.Equal("taom_enlist_offer", TaomEnlistGuardRules.OfferId);
        Assert.Equal("taom_enlist_oath_swear", TaomEnlistGuardRules.SwearId);
    }

    [Fact]
    public void Hint_text_is_the_agreed_one()
    {
        Assert.Equal("Enlisting is not supported in co-op yet", TaomEnlistGuardRules.Hint);
        Assert.Contains("TAOM may have changed", TaomEnlistGuardRules.MissingLine(TaomEnlistGuardRules.OfferId));
        Assert.Contains(TaomEnlistGuardRules.OfferId, TaomEnlistGuardRules.MissingLine(TaomEnlistGuardRules.OfferId));
    }

    [Fact]
    public void Module_uses_the_game_conversation_hooks_and_ships_the_guard()
    {
        var folder = Path.Combine(RepoRoot(), "compat", "CoodszJoinPassword");
        var guard = File.ReadAllText(Path.Combine(folder, "src", "TaomEnlistGuard.cs"));
        Assert.Contains("offer.OnClickableCondition = OfferNotClickable", guard);
        Assert.Contains("swear.OnConsequence = new LeaveAfterSwear(swear.OnConsequence).Run", guard);
        Assert.Contains("PlayerEncounter.LeaveEncounter = true", guard);
        Assert.Contains("TaomEnlistGuardRules.ShouldGuard(", guard);

        var module = File.ReadAllText(Path.Combine(folder, "src", "JoinPassword.cs"));
        Assert.Contains("TaomEnlistGuard.CoopClientStarted = true", module);
        Assert.Contains("TaomEnlistGuard.OnCampaignReady(Log)", module);

        var build = File.ReadAllText(Path.Combine(folder, "build.ps1"));
        Assert.Contains("src\\TaomEnlistGuard.cs", build);
        Assert.Contains("src\\TaomEnlistGuardRules.cs", build);

        // The shipped DLL was rebuilt with the guard (string literals are stored as UTF-16).
        var dll = File.ReadAllBytes(Path.Combine(folder, "module", "bin", "Win64_Shipping_Client", "CoodszJoinPassword.dll"));
        Assert.True(Contains(dll, Encoding.Unicode.GetBytes(TaomEnlistGuardRules.Hint)));
        Assert.True(Contains(dll, Encoding.Unicode.GetBytes(TaomEnlistGuardRules.OfferId)));
        Assert.True(Contains(dll, Encoding.Unicode.GetBytes(TaomEnlistGuardRules.SwearId)));
    }

    private static bool Contains(byte[] haystack, byte[] needle) =>
        haystack.AsSpan().IndexOf(needle) >= 0;

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "BannerlordCoopLauncher.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
