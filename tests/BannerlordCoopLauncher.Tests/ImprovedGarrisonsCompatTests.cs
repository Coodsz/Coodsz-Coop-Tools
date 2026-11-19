using System.Text.Json;
using System.Xml.Linq;
using BannerlordCoopLauncher.Core.Modules;
using CoodszHost.Services;

namespace BannerlordCoopLauncher.Tests;

// Improved Garrisons Coop Compat (compat\ImprovedGarrisonsCoopCompat): the server only module Coodsz Host
// adds to a start whose mods include ImprovedGarrisons.
public sealed class ImprovedGarrisonsCompatTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "coodsz-igcompat-" + Guid.NewGuid().ToString("N"));

    public ImprovedGarrisonsCompatTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); }
        catch (IOException) { }
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "BannerlordCoopLauncher.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    [Fact]
    public void Module_is_server_only_so_players_and_the_save_check_ignore_it()
    {
        Assert.StartsWith("DedicatedServer.", ImprovedGarrisonsCompat.ModuleId);
        Assert.True(CoopJoinRules.IsIgnored(ImprovedGarrisonsCompat.ModuleId));

        var save = new[] { "Native", "Coop", "ImprovedGarrisons", "CoopModPatch" };
        var change = SaveModChange.Check(save, save.Append(ImprovedGarrisonsCompat.ModuleId));
        Assert.Equal(SaveModAction.Start, change.Action);
        change = SaveModChange.Check(save.Append(ImprovedGarrisonsCompat.ModuleId).ToList(), save);
        Assert.Equal(SaveModAction.Start, change.Action);
    }

    [Fact]
    public void Start_list_gets_the_module_last_only_when_Improved_Garrisons_is_on()
    {
        var rows = new List<ModuleRow>
        {
            new() { Id = "Native", Required = true, Enabled = true },
            new() { Id = "Coop", Enabled = true },
            new() { Id = "ImprovedGarrisons", Enabled = true },
            new() { Id = "CoopModPatch", Enabled = true },
            new() { Id = ImprovedGarrisonsCompat.ModuleId, Enabled = true },
        };
        var start = ImprovedGarrisonsCompat.WithCompat(rows);
        Assert.Equal(new[] { "Native", "Coop", "ImprovedGarrisons", "CoopModPatch", ImprovedGarrisonsCompat.ModuleId }, start.Select(r => r.Id));
        Assert.True(start[^1].Enabled);

        rows[2].Enabled = false;
        Assert.False(ImprovedGarrisonsCompat.Applies(rows));
        Assert.DoesNotContain(ImprovedGarrisonsCompat.WithCompat(rows), r => r.Id == ImprovedGarrisonsCompat.ModuleId);

        var plain = new List<ModuleRow> { new() { Id = "Native", Enabled = true }, new() { Id = "Coop", Enabled = true } };
        Assert.Equal(new[] { "Native", "Coop" }, ImprovedGarrisonsCompat.WithCompat(plain).Select(r => r.Id));
    }

    [Fact]
    public void Module_is_copied_into_the_server_and_only_when_it_changed()
    {
        var payload = Path.Combine(_dir, "payload");
        Directory.CreateDirectory(Path.Combine(payload, "bin", "Win64_Shipping_Server"));
        File.WriteAllText(Path.Combine(payload, "SubModule.xml"), "<Module/>");
        File.WriteAllText(Path.Combine(payload, "bin", "Win64_Shipping_Server", "DedicatedServer.IGCoopCompat.dll"), "dll");
        var server = Path.Combine(_dir, "server");
        Directory.CreateDirectory(Path.Combine(server, "engine", "Modules"));

        Assert.False(ImprovedGarrisonsCompat.Installed(server));
        Assert.NotNull(ImprovedGarrisonsCompat.Install(server, payload));
        Assert.True(File.Exists(Path.Combine(server, "engine", "Modules", ImprovedGarrisonsCompat.ModuleId, "bin", "Win64_Shipping_Server", "DedicatedServer.IGCoopCompat.dll")));
        Assert.True(ImprovedGarrisonsCompat.Installed(server));
        Assert.Null(ImprovedGarrisonsCompat.Install(server, payload));
        // A missing payload never stops a start.
        Assert.Null(ImprovedGarrisonsCompat.Install(server, Path.Combine(_dir, "nothing")));
    }

    [Fact]
    public void Shipped_module_loads_after_Improved_Garrisons_and_CoopModPatch_and_Modules_list_hides_it()
    {
        var root = RepoRoot();
        var payload = Path.Combine(root, "src", "CoodszHost", ImprovedGarrisonsCompat.FolderName, ImprovedGarrisonsCompat.ModuleId);
        var xml = XDocument.Load(Path.Combine(payload, "SubModule.xml"));
        Assert.Equal(ImprovedGarrisonsCompat.ModuleId, xml.Root!.Element("Id")!.Attribute("value")!.Value);
        Assert.Equal(ImprovedGarrisonsCompat.Version, xml.Root!.Element("Version")!.Attribute("value")!.Value);
        var loadBefore = xml.Root!.Element("DependedModuleMetadatas")!.Elements()
            .Where(e => e.Attribute("order")?.Value == "LoadBeforeThis" && e.Attribute("optional")?.Value == "true")
            .Select(e => e.Attribute("id")!.Value).ToList();
        Assert.Contains("ImprovedGarrisons", loadBefore);
        Assert.Contains("CoopModPatch", loadBefore);
        Assert.Empty(xml.Root!.Element("DependedModules")!.Elements());
        Assert.True(File.Exists(Path.Combine(payload, "bin", "Win64_Shipping_Server", "DedicatedServer.IGCoopCompat.dll")));
        Assert.Equal(File.ReadAllText(Path.Combine(root, "compat", "ImprovedGarrisonsCoopCompat", "SubModule.xml")), File.ReadAllText(Path.Combine(payload, "SubModule.xml")));

        // Improved Garrisons and CoopModPatch have no license: found by name, never referenced or copied.
        var project = File.ReadAllText(Path.Combine(root, "compat", "ImprovedGarrisonsCoopCompat", "ImprovedGarrisonsCoopCompat.csproj"));
        Assert.DoesNotContain("ImprovedGarrisons.dll", project, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CoopModPatch.dll", project, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Adapter.ImprovedGarrisons", project, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<Private>false</Private>", project);

        var modules = Path.Combine(_dir, "server", "engine", "Modules");
        foreach (var id in new[] { "Native", ImprovedGarrisonsCompat.ModuleId })
        {
            Directory.CreateDirectory(Path.Combine(modules, id));
            File.WriteAllText(Path.Combine(modules, id, "SubModule.xml"),
                "<Module><Name value=\"" + id + "\"/><Id value=\"" + id + "\"/><Version value=\"v1.0.0\"/></Module>");
        }
        Assert.DoesNotContain(ModuleCatalog.Load(Path.Combine(_dir, "server")), row => row.Id == ImprovedGarrisonsCompat.ModuleId);
    }

    [Fact]
    public void Module_hooks_the_verified_names_and_prints_one_ready_line()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot(), "compat", "ImprovedGarrisonsCoopCompat", "SubModule.cs"));
        Assert.Contains("\"ImprovedGarrisons.SaveSystem.GarrisonBehavior\"", source);
        Assert.Contains("\"InitializeSettlements\", 0", source);
        Assert.Contains("\"onSettlementOwnerChanged\", 6", source);
        Assert.Contains("\"CoopModPatch.Adapters.ImprovedGarrisons.IGBridge\"", source);
        Assert.Contains("\"Execute\", 2", source);
        Assert.Contains("\"ImprovedGarrisons.SaveSystem.SaveWriter.SaveWriterManager\"", source);
        Assert.Contains("\"LoadSaveFile\", 1, null, nameof(ReapplyCoopModPatchSettings)", source);
        Assert.Contains("\"CoopModPatch.Adapters.ImprovedGarrisons.IGSettingsStore\"", source);
        Assert.Contains("\"RestoreAll\"", source);
        Assert.Contains("public const int HookCount = 4;", source);
        Assert.Contains("\"re-applied CoopModPatch town settings after IG loaded its save data\"", source);
        // The load clean up stays skipped.
        Assert.Contains("\"InitializeSettlements\", 0, nameof(SkipOnServer)", source);
        Assert.Contains("ready (4 of 4 hooks)", File.ReadAllText(Path.Combine(RepoRoot(), "compat", "ImprovedGarrisonsCoopCompat", "README.txt")));
        Assert.Contains("\"ready (\" + count + \" of \" + HookCount + \" hooks)\"", source);
        Assert.Contains("\"[IGCoopCompat] \"", source);
        Assert.Contains("GameNetwork.IsDedicatedServer", source);
        // The owner change hook clears IG's own entry (keyed by town name) instead of skipping it.
        Assert.Contains("nameof(OwnerChanged)", source);
        Assert.Contains("\"SettlementSettingsData\"", source);
        Assert.Contains("settlement.Town.Name", source);
    }

    [Fact]
    public void Town_settings_go_only_when_the_town_changes_clan()
    {
        var clanA = new object();
        var clanB = new object();
        Assert.True(ImprovedGarrisonsCoopCompat.OwnerRules.DropsSettings(clanB, clanA));
        Assert.False(ImprovedGarrisonsCoopCompat.OwnerRules.DropsSettings(clanA, clanA));
        Assert.True(ImprovedGarrisonsCoopCompat.OwnerRules.DropsSettings(clanA, null));
        Assert.True(ImprovedGarrisonsCoopCompat.OwnerRules.DropsSettings(null, clanA));
        Assert.False(ImprovedGarrisonsCoopCompat.OwnerRules.DropsSettings(null, null));
    }

    [Fact]
    public void Copy_to_specific_garrisons_is_refused_when_any_target_is_another_clans()
    {
        var mine = new object();
        var theirs = new object();
        var owners = new Dictionary<string, object?> { ["town_A"] = mine, ["castle_B"] = mine, ["town_C"] = theirs, ["town_D"] = null };
        (bool, object?) Lookup(string id) => owners.TryGetValue(id, out var owner) ? (true, owner) : (false, null);

        Assert.Equal(new[] { "town_A", "castle_B" }, ImprovedGarrisonsCoopCompat.OwnerRules.SplitIds("town_A;castle_B;"));
        Assert.Empty(ImprovedGarrisonsCoopCompat.OwnerRules.SplitIds(null));
        Assert.Null(ImprovedGarrisonsCoopCompat.OwnerRules.FirstForeignTarget(new[] { "town_A", "castle_B", "gone" }, Lookup, mine));
        Assert.Equal("town_C", ImprovedGarrisonsCoopCompat.OwnerRules.FirstForeignTarget(ImprovedGarrisonsCoopCompat.OwnerRules.SplitIds("town_A;town_C"), Lookup, mine));
        Assert.Equal("town_D", ImprovedGarrisonsCoopCompat.OwnerRules.FirstForeignTarget(new[] { "town_D" }, Lookup, mine));
    }

    [Fact]
    public void Copy_to_all_becomes_the_requester_clans_own_towns_or_castles()
    {
        var mine = new object();
        var theirs = new object();
        var all = new (string, bool, object?)[]
        {
            ("town_A", false, mine), ("town_B", false, mine), ("town_C", false, theirs),
            ("castle_D", true, mine), ("castle_E", true, theirs), ("castle_F", true, null),
        };
        Assert.Equal(new[] { "town_B" }, ImprovedGarrisonsCoopCompat.OwnerRules.CopyAllTargets(all, "town_A", "towns", mine));
        Assert.Equal(new[] { "castle_D" }, ImprovedGarrisonsCoopCompat.OwnerRules.CopyAllTargets(all, "town_A", "castles", mine));
        Assert.Empty(ImprovedGarrisonsCoopCompat.OwnerRules.CopyAllTargets(all, "castle_D", "castles", mine));

        var source = File.ReadAllText(Path.Combine(RepoRoot(), "compat", "ImprovedGarrisonsCoopCompat", "SubModule.cs"));
        Assert.Contains("SetMember(request, \"Kind\", \"CopySpecific\")", source);
        Assert.Contains("case ClanLookup.NoClan:", source);
    }

    [Fact]
    public void Start_adds_the_module_after_the_tools_module_and_not_in_AD1259()
    {
        var root = RepoRoot();
        var start = SourceFiles.Method(SourceFiles.Window(root, "CoodszHost"), "private void StartServer()");
        Assert.Contains("ImprovedGarrisonsCompat.Applies(startModules)", start);
        Assert.Contains("ImprovedGarrisonsCompat.Install(ServerRoot)", start);
        Assert.Contains("ImprovedGarrisonsCompat.WithCompat(startModules)", start);
        Assert.True(start.IndexOf("CoodszServerTools.WithTools", StringComparison.Ordinal) < start.IndexOf("ImprovedGarrisonsCompat.WithCompat", StringComparison.Ordinal));
        Assert.True(start.IndexOf("ImprovedGarrisonsCompat.WithCompat", StringComparison.Ordinal) < start.IndexOf("ModuleCatalog.Write(ServerRoot, startModules)", StringComparison.Ordinal));
        Assert.Contains("!Edition.IsAD1259", File.ReadAllText(Path.Combine(root, "src", "CoodszHost", "Services", "ImprovedGarrisonsCompat.cs")));
        Assert.Contains("PatchModules\\**\\*", File.ReadAllText(Path.Combine(root, "src", "CoodszHost", "CoodszHost.csproj")));
        Assert.Contains("PatchModules\\DedicatedServer.IGCoopCompat", File.ReadAllText(Path.Combine(root, "tools", "Make-Release.ps1")));
    }

    [Fact]
    public void Added_note_is_in_all_six_languages()
    {
        var folder = Path.Combine(RepoRoot(), "src", "BannerlordCoopLauncher.Core", "Localization", "Languages");
        foreach (var language in new[] { "ar", "de", "fr", "pt", "ru", "tr" })
        {
            var table = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(folder, language + ".json")))!;
            Assert.True(table.TryGetValue(ImprovedGarrisonsCompat.AddedNote, out var text), language + " misses the note");
            Assert.Contains("Improved Garrisons Coop Compat", text);
        }
    }
}
