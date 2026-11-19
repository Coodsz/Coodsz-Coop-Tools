using BannerlordCoopLauncher.Core.Parsing;
using BannerlordCoopLauncher.Core.Profile;
using BannerlordCoopLauncher.Core.Support;
using TaomCoop.Core;

namespace BannerlordCoopLauncher.Tests;

public class TaomChannelFindingsTests
{
    // Pretends some files carry the Windows download mark.
    private sealed class FakeMarks(params string[] marked) : IZoneMarks
    {
        public HashSet<string> Marked { get; } = new(marked, StringComparer.OrdinalIgnoreCase);
        public bool Has(string file) => Marked.Contains(file);
        public void Remove(string file) => Marked.Remove(file);
    }

    private static string NewRoot() => Path.Combine(Path.GetTempPath(), "taom-find-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Unblock_dlls_removes_the_mark_from_dlls_in_bin_only()
    {
        var root = NewRoot();
        try
        {
            var bin = Path.Combine(root, "AoME", "bin", "Win64_Shipping_Client");
            Directory.CreateDirectory(bin);
            var a = Path.Combine(bin, "AoME.dll");
            var b = Path.Combine(bin, "Helper.dll");
            var xml = Path.Combine(root, "AoME", "SubModule.xml");
            foreach (var file in new[] { a, b, xml })
                File.WriteAllText(file, "x");
            var marks = new FakeMarks(a, b, xml);
            Assert.Equal(2, ModUnblock.BlockedDlls(Path.Combine(root, "AoME"), marks).Count);
            Assert.Equal(2, ModUnblock.UnblockDlls(Path.Combine(root, "AoME"), marks));
            Assert.Equal(new[] { xml }, marks.Marked);
            Assert.Equal(0, ModUnblock.UnblockDlls(Path.Combine(root, "AoME"), marks));
            Assert.Equal("Unblocked 12 DLL files in Armies of Middle Earth that Windows had marked as downloaded from the internet.", ModUnblock.Line("Armies of Middle Earth", 12));
            Assert.Equal("Unblocked 1 DLL file in Limitless that Windows had marked as downloaded from the internet.", ModUnblock.Line("Limitless", 1));
            Assert.Empty(ModUnblock.BlockedDlls(Path.Combine(root, "Missing"), marks));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [WindowsFact]
    public void Unblock_dlls_removes_a_real_zone_identifier_stream()
    {
        var root = NewRoot();
        try
        {
            var bin = Path.Combine(root, "Mod", "bin", "Win64_Shipping_Client");
            Directory.CreateDirectory(bin);
            var dll = Path.Combine(bin, "Mod.dll");
            File.WriteAllText(dll, "x");
            try
            {
                File.WriteAllText(dll + ":Zone.Identifier", "[ZoneTransfer]\r\nZoneId=3\r\n");
            }
            catch (Exception ex) when (ex is IOException or NotSupportedException or UnauthorizedAccessException)
            {
                return; // Not an NTFS temp folder.
            }

            Assert.Single(ModUnblock.BlockedDlls(Path.Combine(root, "Mod")));
            Assert.Equal(1, ModUnblock.UnblockDlls(Path.Combine(root, "Mod")));
            Assert.False(File.Exists(dll + ":Zone.Identifier"));
            Assert.True(File.Exists(dll));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void The_checks_and_the_server_copy_handle_blocked_dlls()
    {
        var setup = new TaomSetup { Name = "Mine" };
        TaomLoadOrder.Add(setup, "Armies of Middle Earth", "ArmiesOfMiddleEarth", null);
        var facts = new Dictionary<string, TaomModFact>(StringComparer.OrdinalIgnoreCase)
        {
            ["ArmiesOfMiddleEarth"] = new("ArmiesOfMiddleEarth", ModuleManifestReader.Parse("<Module><Id value=\"ArmiesOfMiddleEarth\"/></Module>"), new[] { "M\\AoME" }, Array.Empty<string>(), BlockedDlls: 12)
        };
        var issue = Assert.Single(TaomModChecks.Run(setup, facts));
        Assert.False(issue.Blocks);
        Assert.StartsWith("Unblock DLLs. Armies of Middle Earth has DLL files", issue.Text);
    }

    [Fact]
    public void The_taom_dependency_patch_and_community_taom_mods_can_be_added()
    {
        Assert.True(TaomLoadOrder.IsTaomDependencyPatch("TAOMDependencyPatch", null));
        Assert.True(TaomLoadOrder.IsTaomDependencyPatch("SomeId", "TAOM Dependency Patch"));
        Assert.True(TaomLoadOrder.IsTaomDependencyPatch("TAOM.Dependency_Patch", null));
        Assert.False(TaomLoadOrder.IsTaomDependencyPatch("PartyAIControls", "Party AI Controls"));
        Assert.Null(TaomLoadOrder.RefusalReason("TAOMDependencyPatch"));
        Assert.Null(TaomLoadOrder.RefusalReason("TAOM.DependencyPatch"));
        Assert.Null(TaomLoadOrder.RefusalReason("TAOMBloodOverhaul"));
        // The TAOM developer's Nexus mods have TAOM_ ids but are not TAOM pieces.
        foreach (var id in new[] { "TAOM_PlayerVoices", "TAOM_CheatPanel", "TAOM_SettlementPlacementProbe", "TAOM_XorberaxCompatibility", "TAOM_BloodOverhaul",
                     "TAOM_Standalone_Compat_Diplomacy", "TAOM_Standalone_Compat_RBM", "TAOM_Standalone_Compat_AIInfluence", "TAOM_Standalone_Compat_Fourberie",
                     "TAOM_Standalone_Compat_Tournaments", "TAOM.Extra" })
            Assert.Null(TaomLoadOrder.RefusalReason(id));
        // What TAOM and the Coop kit install stays refused.
        foreach (var id in new[] { "TAOM", "TAOM_Map", "TAOM.Dependencies", "TAOM.CoopCompat", "LOTRLOME_Armory", "taom_map" })
            Assert.NotNull(TaomLoadOrder.RefusalReason(id));
        Assert.True(TaomLoadOrder.IsXorberaxTaomPatch("TAOM_XorberaxCompatibility", "Xorberax-TAOM-2.0.29.5"));
        Assert.True(TaomLoadOrder.IsXorberaxTaomPatch("TAOM_XorberaxCompatibility", null));
        Assert.False(TaomLoadOrder.IsXorberaxTaomPatch("XorberaxLegacy", "Xorberax's Legacy"));
        Assert.True(BannerlordCoopLauncher.Core.Modules.TotalOverhaul.Is("TAOM_Map"));
        Assert.False(BannerlordCoopLauncher.Core.Modules.TotalOverhaul.Is("TAOM_PlayerVoices"));

        var setup = new TaomSetup { Name = "Mine" };
        TaomLoadOrder.Add(setup, "TAOM Dependency Patch", "TAOMDependencyPatch", null);
        var issue = Assert.Single(TaomModChecks.Run(setup, new Dictionary<string, TaomModFact>()));
        Assert.Contains("does the same job as the launcher's dependency fix", issue.Text);
        Assert.Equal("Works. Does the same job as the launcher's dependency fix.", TaomCompatList.BuiltIn().Find(null, null, "TAOM Dependency Patch")!.Note);
    }

    [Fact]
    public void Every_extra_goes_below_taom_and_the_xorberax_patch_goes_last()
    {
        var setup = new TaomSetup { Name = "Mine" };
        foreach (var (id, name) in new[] { ("XorberaxTAOMPatch", "Xorberax TAOM Patch"), ("MusicOfTheWaningAge", "Music of the Waning Age"), ("RTSCamera", "RTS Camera"), ("XorberaxLegacy", "Xorberax's Legacy"), ("ModA", "ModA") })
            TaomLoadOrder.Add(setup, name, id, null);
        TaomLoadOrder.MoveTo(setup, "MusicOfTheWaningAge", ExtraSlot.BeforeTaom, 0);
        var warned = TaomModChecks.Run(setup, new Dictionary<string, TaomModFact>());
        Assert.Contains(warned, issue => issue.Text.StartsWith("Music of the Waning Age loads before TAOM. The TAOM developer says"));

        TaomAutoSort.Sort(setup, new Dictionary<string, ModuleManifest>());
        Assert.Equal(new[] { "MusicOfTheWaningAge", "XorberaxLegacy", "ModA", "RTSCamera", "XorberaxTAOMPatch" }, setup.Extras.Select(mod => mod.ModuleId));
        Assert.All(setup.Extras, mod => Assert.Equal(ExtraSlot.AfterTaom, mod.Slot));
        Assert.Empty(TaomModChecks.Run(setup, new Dictionary<string, TaomModFact>()));
    }

    [Fact]
    public void The_compat_list_has_the_channel_findings()
    {
        var list = TaomCompatList.BuiltIn();
        Assert.Equal("13876", list.Find("ArmiesOfMiddleEarth")!.NexusId);
        Assert.Equal(CompatTag.Risky, list.Find("RBM")!.Kind);
        Assert.Contains("Warbandlord", list.Find("RBM")!.Note);
        Assert.Contains("2026-10-14", list.Find("XorberaxLegacy")!.Note);
        Assert.Contains("grey", list.Find("Retinues")!.Note);
        Assert.Contains("loading forever", list.Find("Bannerlord.Diplomacy")!.Note);
        Assert.Contains("config.cheat_mode 1", list.Find("TAOM_CheatPanel")!.Note);
        Assert.Equal(CompatTag.Everyone, list.Find("PeopleOfMiddleEarth")!.Kind);
        Assert.Contains("44100 Hz", list.Find(null, null, "Music of the Waning Age")!.Note);
        Assert.Contains("Smart Cavalry AI", list.Find(null, null, "Smart Cavalry AI")!.Note);
        Assert.Contains("unblocked", list.Find(null, "3732334365")!.Note);
    }

    [Fact]
    public void Nexus_only_mods_are_copied_to_the_server_from_the_hosts_modules_folder_and_named_for_players()
    {
        var root = NewRoot();
        try
        {
            var game = Path.Combine(root, "game", "Modules");
            var source = Path.Combine(game, "ArmiesOfMiddleEarth");
            Directory.CreateDirectory(source);
            File.WriteAllText(Path.Combine(source, "SubModule.xml"), "<Module><Id value=\"ArmiesOfMiddleEarth\"/><Version value=\"v1.0\"/><SubModules/></Module>");
            var serverRoot = Path.Combine(root, "server");
            Directory.CreateDirectory(BannerlordCoopLauncher.Core.Modules.ServerModuleInstall.ModulesRoot(serverRoot));
            var setup = new TaomSetup { Name = "Mine" };
            TaomLoadOrder.Add(setup, "Armies of Middle Earth", "ArmiesOfMiddleEarth", null);
            TaomLoadOrder.Add(setup, "Banner Fix", "BannerFix", null);
            var found = new List<FoundMod>
            {
                new("Armies of Middle Earth", "ArmiesOfMiddleEarth", "0", source, "From Nexus or ModDB"),
                new("Banner Fix", "BannerFix", "0", source, "From Nexus or ModDB")
            };
            var plan = TaomServerPath.Plan(setup, found);
            Assert.StartsWith("Copied Armies of Middle Earth", TaomServerPath.Stage(serverRoot, plan)[0]);
            Assert.True(File.Exists(Path.Combine(BannerlordCoopLauncher.Core.Modules.ServerModuleInstall.ModulesRoot(serverRoot), "ArmiesOfMiddleEarth", "SubModule.xml")));
            var note = Assert.Single(TaomServerPath.NexusOnlyNotes(plan));
            Assert.StartsWith("Armies of Middle Earth is not on the Steam Workshop. Every player has to get it", note);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
