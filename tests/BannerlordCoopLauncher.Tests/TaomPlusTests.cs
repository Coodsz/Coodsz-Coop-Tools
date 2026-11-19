using BannerlordCoopLauncher.Core.Launch;
using BannerlordCoopLauncher.Core.Modules;
using BannerlordCoopLauncher.Core.Profile;
using TaomCoop.Core;

namespace BannerlordCoopLauncher.Tests;

public class TaomPlusTests
{
    private static TaomSetup Plus()
    {
        var book = new TaomSetupBook();
        book.Ensure();
        return book.Find(TaomSetupBook.PlusName)!;
    }

    [Fact]
    public void Taom_plus_is_locked_and_launches_the_core_plus_its_mods()
    {
        var plus = Plus();
        Assert.True(plus.Locked);
        var order = TaomLoadOrder.ClientOrder(plus);
        Assert.Null(TaomLoadOrder.CoreProblem(order));
        Assert.Equal(TaomStack.ClientModules.Concat(TaomPlus.ModuleIds), order);
        Assert.True(TaomServerPath.Needed(plus));
        Assert.False(TaomServerPath.Needed(new TaomSetupBook().Current()));
        Assert.Throws<InvalidOperationException>(() => TaomLoadOrder.Add(plus, "X", "X", null));
        Assert.Null(CoopJoinRules.JoinProblem(TaomLoadOrder.LaunchModules(plus, joinPassword: true), TaomLoadOrder.ServerOrder(plus)));
    }

    [Fact]
    public void Taom_plus_uses_only_mods_the_list_calls_safe_or_proven_and_never_a_refused_one()
    {
        var compat = TaomCompatList.BuiltIn();
        foreach (var mod in Plus().Extras)
        {
            Assert.Null(TaomLoadOrder.RefusalReason(mod.ModuleId));
            var entry = compat.Find(mod.ModuleId, mod.WorkshopId, mod.Label);
            Assert.NotNull(entry);
            Assert.Contains(entry!.Kind, new[] { CompatTag.Safe, CompatTag.Everyone });
        }

        // Banner Fix loads before Raise your Banner, and RTS Camera before its Command System.
        var ids = TaomPlus.ModuleIds.ToList();
        Assert.True(ids.IndexOf("BannerFix") < ids.IndexOf("RaiseYourBanner"));
        Assert.True(ids.IndexOf("RTSCamera") < ids.IndexOf("RTSCamera.CommandSystem"));
        Assert.Equal(new[] { "RTSCamera", "RTSCamera.CommandSystem" }, ids.TakeLast(2));
        Assert.DoesNotContain("PlayerSettlement", ids);
        Assert.DoesNotContain("PSCoopBridge", ids);
    }

    [Fact]
    public void Bundled_taom_plus_mods_are_the_ones_coop_and_friends_bundles()
    {
        Assert.Equal(new[] { "BannerFix", "RaiseYourBanner", "RaiseYourTorch", "ImmersiveBattlefields" }, TaomPlus.BundledIds);
        Assert.All(TaomPlus.BundledIds, id => Assert.Contains(id, BundledModules.ModuleIds));
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "BannerlordCoopLauncher.sln")))
            dir = dir.Parent;
        return dir!.FullName;
    }

    [Fact]
    public void The_taom_release_tools_carry_the_bundled_taom_plus_mods_inside_both_exes()
    {
        var root = RepoRoot();
        var script = File.ReadAllText(Path.Combine(root, "tools", "Make-Release.ps1"));
        // The script reads the list from TaomPlus.cs with its own pattern; it must give the same mods.
        var pattern = System.Text.RegularExpressions.Regex.Match(script, @"function Get-TaomBundledIds[\s\S]*?\[regex\]::Matches\(\$source, '(?<p>[^']+)'\)").Groups["p"].Value;
        Assert.NotEqual("", pattern);
        var source = File.ReadAllText(Path.Combine(root, "src", "TaomCoop.Core", "TaomPlus.cs"));
        var ids = System.Text.RegularExpressions.Regex.Matches(source, pattern).Select(match => match.Groups["id"].Value).ToList();
        Assert.Equal(TaomPlus.BundledIds, ids);

        // Staged into one folder that both TAOM programs pack inside them (the BundledMods payload).
        Assert.Contains("Get-TaomBundledIds (Get-Content -Raw -LiteralPath 'src\\TaomCoop.Core\\TaomPlus.cs')", script);
        Assert.Contains("Copy-Item -LiteralPath (Join-Path $BundledMods $id) -Destination (Join-Path $bundledTaom $id) -Recurse", script);
        Assert.Contains("must not be bundled", script);
        Assert.Equal(2, script.Split("\"-p:CoodszBundledMods=$bundledTaom\"").Length - 1);
        // Both programs must carry this build's mods, file for file (tools\PayloadCheck.ps1).
        var hostCheck = "Assert-Payload $taomHost \"$payloads\\taom-host\" 'BundledMods' @(, @('', $bundledTaom))";
        var clientCheck = "Assert-Payload $taomClient \"$payloads\\taom-client\" 'BundledMods' @(, @('', $bundledTaom))";
        Assert.Contains(hostCheck, script);
        Assert.Contains(clientCheck, script);
        // Staged and checked before either TAOM zip is written.
        var zips = script.IndexOf("# 7. TAOM zips", StringComparison.Ordinal);
        Assert.True(script.IndexOf("$bundledTaom $id", StringComparison.Ordinal) < zips);
        Assert.True(script.IndexOf(clientCheck, StringComparison.Ordinal) < zips);
    }

    [Fact]
    public void CoopModPatch_and_improved_garrisons_sit_together_where_coop_and_friends_puts_them()
    {
        var ids = TaomPlus.ModuleIds.ToList();
        Assert.True(TaomPlus.IncludeCoopLogicPair);
        Assert.Equal(ids.IndexOf("CoopModPatch") + 1, ids.IndexOf("ImprovedGarrisons"));
        // Coop and Friends: Coop, XorberaxLegacy, ..., CoopModPatch, ImprovedGarrisons, ..., RTSCamera.
        var friends = ServerModuleSet.WorkingOrder(includeMoreNations: false).Where(id => ids.Contains(id) && !TaomLoadOrder.LoadsLast.Contains(id)).ToList();
        Assert.Equal(friends, ids.Where(friends.Contains).ToList());
        var plus = Plus();
        Assert.Equal("3786391685", plus.Extras.Single(mod => mod.ModuleId == "CoopModPatch").WorkshopId);
        Assert.Null(CoopJoinRules.JoinProblem(TaomLoadOrder.LaunchModules(plus, true), TaomLoadOrder.ServerOrder(plus)));
        var entry = TaomCompatList.BuiltIn().Find("CoopModPatch")!;
        Assert.True(entry.HasServerBuild);
        Assert.Equal(CompatTag.Everyone, entry.Kind);
    }

    [Fact]
    public void A_mod_with_another_id_than_the_list_says_is_found_by_its_workshop_id()
    {
        var plus = Plus();
        var installed = new[]
        {
            new BannerlordCoopLauncher.Core.Profile.FoundMod("Better Exception Window", "Bannerlord.BetterExceptionWindow", "2875478172", "W\\2875478172", "Workshop"),
            new BannerlordCoopLauncher.Core.Profile.FoundMod("RTS Camera", "RTSCamera", "3747725551", "W\\3747725551", "Workshop")
        };
        var resolved = TaomModIds.Resolve(plus, installed);
        Assert.Contains("Bannerlord.BetterExceptionWindow", TaomLoadOrder.ClientOrder(resolved));
        Assert.DoesNotContain("BetterExceptionWindow", TaomLoadOrder.ClientOrder(resolved));
        Assert.Equal("BetterExceptionWindow", plus.Extras[0].ModuleId);
        Assert.Contains("BetterExceptionWindow", TaomSharedList.Missing(plus, installed.Select(mod => mod.ModuleId)).Select(mod => mod.ModuleId));
        Assert.DoesNotContain("Bannerlord.BetterExceptionWindow", TaomSharedList.Missing(resolved, installed.Select(mod => mod.ModuleId)).Select(mod => mod.ModuleId));
        var plan = TaomServerPath.Plan(plus, installed);
        Assert.Contains("Bannerlord.BetterExceptionWindow", plan.ServerModules);
        Assert.Contains("Bannerlord.BetterExceptionWindow", plan.ClientModules);
    }

    [Fact]
    public void The_server_tool_finds_bundled_mods_in_its_own_folder()
    {
        var folder = Path.Combine(Path.GetTempPath(), "taom-tool-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(folder, "BannerFix"));
            File.WriteAllText(Path.Combine(folder, "BannerFix", "SubModule.xml"), "<Module><Id value=\"BannerFix\"/><Name value=\"Banner Fix\"/></Module>");
            var found = TaomPlus.BundledNextTo(folder, Array.Empty<BannerlordCoopLauncher.Core.Profile.FoundMod>());
            Assert.Equal("BannerFix", Assert.Single(found).ModuleId);
            Assert.Empty(TaomPlus.BundledNextTo(folder, found));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void Edits_to_taom_plus_in_the_file_are_put_back_and_copies_can_be_changed()
    {
        var settings = TaomClientSettings.Parse("{}");
        var json = settings.Serialize().Replace("\"BetterExceptionWindow\"", "\"SomethingElse\"");
        var back = TaomClientSettings.Parse(json);
        var plus = back.ModSetups.Find(TaomSetupBook.PlusName)!;
        Assert.Equal(TaomPlus.ModuleIds, plus.Extras.Select(mod => mod.ModuleId));
        Assert.Throws<InvalidOperationException>(() => back.ModSetups.Delete(TaomSetupBook.PlusName));
        Assert.Throws<InvalidOperationException>(() => back.ModSetups.Rename(TaomSetupBook.PlusName, "Mine"));

        var copy = back.ModSetups.CopyOf(TaomSetupBook.PlusName);
        Assert.False(copy.Locked);
        TaomLoadOrder.SetEnabled(copy, "XorberaxLegacy", false);
        Assert.DoesNotContain("XorberaxLegacy", TaomLoadOrder.ClientOrder(copy));
    }
}
