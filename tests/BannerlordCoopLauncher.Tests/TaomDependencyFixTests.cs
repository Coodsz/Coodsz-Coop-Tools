using BannerlordCoopLauncher.Core.Modules;
using BannerlordCoopLauncher.Core.Parsing;
using BannerlordCoopLauncher.Core.Profile;
using TaomCoop.Core;

namespace BannerlordCoopLauncher.Tests;

public class TaomDependencyFixTests
{
    // Shaped like Party AI Controls: DependedModules and DependedModuleMetadatas, with a version.
    private const string PartyAi = "﻿<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n<Module>\r\n  <Name value=\"Party AI Controls\"/>\r\n  <Id value=\"PartyAIControls\"/>\r\n  <Version value=\"v1.4.8.1\"/>\r\n"
        + "  <DependedModules>\r\n    <DependedModule Id=\"Bannerlord.Harmony\" DependentVersion=\"v2.3.3\" />\r\n    <DependedModule Id=\"Bannerlord.UIExtenderEx\" DependentVersion=\"v2.11.0\" />\r\n"
        + "    <DependedModule Id=\"Native\" DependentVersion=\"v1.4.8\" />\r\n    <DependedModule Id=\"SandBox\" />\r\n  </DependedModules>\r\n"
        + "  <DependedModuleMetadatas>\r\n    <DependedModuleMetadata id=\"Bannerlord.Harmony\" order=\"LoadBeforeThis\" />\r\n    <DependedModuleMetadata id=\"Bannerlord.UIExtenderEx\" order=\"LoadBeforeThis\" />\r\n"
        + "    <DependedModuleMetadata id=\"Native\" order=\"LoadBeforeThis\" />\r\n  </DependedModuleMetadatas>\r\n"
        + "  <SubModules>\r\n    <SubModule>\r\n      <Name value=\"PartyAIControls\"/>\r\n      <DLLName value=\"PartyAIControls.dll\"/>\r\n      <SubModuleClassType value=\"PartyAIControls.SubModule\"/>\r\n    </SubModule>\r\n  </SubModules>\r\n</Module>\r\n";

    // Shaped like Retinues: all four libraries, one of them optional, written on one line each.
    private const string Retinues = "<Module>\n\t<Id value=\"Retinues\"/>\n\t<Name value=\"Retinues\"/>\n\t<Version value=\"v1.3.0\"/>\n\t<DependedModules>\n"
        + "\t\t<DependedModule Id=\"Bannerlord.Harmony\"/>\n\t\t<DependedModule Id=\"Bannerlord.ButterLib\"/>\n\t\t<DependedModule Id=\"Bannerlord.MBOptionScreen\" Optional=\"true\"/>\n"
        + "\t\t<DependedModule Id=\"Bannerlord.UIExtenderEx\"></DependedModule>\n\t\t<DependedModule Id=\"Native\"/>\n\t</DependedModules>\n\t<SubModules/>\n</Module>\n";

    // Shaped like RTS Camera: only the Harmony link, as metadata.
    private const string RtsCamera = "<Module>\n  <Id value=\"RTSCamera\" />\n  <Name value=\"RTS Camera\" />\n  <Version value=\"v5.4.16\" />\n"
        + "  <DependedModuleMetadatas>\n    <DependedModuleMetadata id=\"Bannerlord.Harmony\" order=\"LoadBeforeThis\" optional=\"false\" version=\"v2.2.2\" />\n"
        + "    <DependedModuleMetadata id=\"Native\" order=\"LoadBeforeThis\" />\n  </DependedModuleMetadatas>\n  <SubModules />\n</Module>\n";

    private const string NoLibraries = "<Module><Id value=\"Plain\"/><DependedModules><DependedModule Id=\"Native\"/></DependedModules></Module>";

    private static string Folder(string root, string id, string xml)
    {
        var dir = Path.Combine(root, id);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "SubModule.xml"), xml.TrimStart('﻿'), new System.Text.UTF8Encoding(xml.StartsWith('﻿')));
        return dir;
    }

    private static string NewRoot() => Path.Combine(Path.GetTempPath(), "taom-fix-" + Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData(PartyAi, new[] { "Bannerlord.Harmony", "Bannerlord.UIExtenderEx" })]
    [InlineData(Retinues, new[] { "Bannerlord.Harmony", "Bannerlord.ButterLib", "Bannerlord.MBOptionScreen", "Bannerlord.UIExtenderEx" })]
    [InlineData(RtsCamera, new[] { "Bannerlord.Harmony" })]
    public void The_fix_takes_out_only_the_libraries_and_keeps_everything_else(string xml, string[] libraries)
    {
        Assert.Equal(libraries, TaomDependencyFix.NamedLibraries(xml));
        var stripped = TaomDependencyFix.Strip(xml)!;
        Assert.Empty(TaomDependencyFix.NamedLibraries(stripped));
        Assert.DoesNotContain("Bannerlord.", stripped);
        var before = ModuleManifestReader.Parse(xml)!;
        var after = ModuleManifestReader.Parse(stripped)!;
        Assert.Equal(before.Id, after.Id);
        Assert.Equal(before.Version, after.Version);
        Assert.Equal(before.Dlls, after.Dlls);
        Assert.Equal(before.Needs.Where(link => !link.Id.StartsWith("Bannerlord.")).Select(link => link.Id), after.Needs.Select(link => link.Id));
        Assert.Contains("Native", stripped);
        Assert.Null(TaomDependencyFix.Strip(NoLibraries));
    }

    [Fact]
    public void The_fix_keeps_the_original_patches_once_and_puts_it_back()
    {
        var root = NewRoot();
        try
        {
            var dir = Folder(root, "PartyAIControls", PartyAi);
            var xml = Path.Combine(dir, "SubModule.xml");
            var original = File.ReadAllBytes(xml);
            Assert.Equal(DependencyFixState.Needed, TaomDependencyFix.State(dir));

            var line = TaomDependencyFix.Apply(dir, "Party AI Controls");
            Assert.Equal("TAOM dependency fix applied to Party AI Controls. It no longer asks for Bannerlord.Harmony, Bannerlord.UIExtenderEx, which TAOM Dependencies already has.", line);
            Assert.Equal(DependencyFixState.Applied, TaomDependencyFix.State(dir));
            Assert.Equal(original, File.ReadAllBytes(Path.Combine(dir, TaomDependencyFix.BackupName)));
            var patched = File.ReadAllBytes(xml);
            Assert.Equal(0xEF, patched[0]);
            Assert.DoesNotContain("Bannerlord.Harmony", File.ReadAllText(xml));

            // Only once.
            Assert.Null(TaomDependencyFix.Apply(dir, "Party AI Controls"));
            Assert.Equal(patched, File.ReadAllBytes(xml));

            Assert.True(TaomDependencyFix.Restore(dir));
            Assert.Equal(original, File.ReadAllBytes(xml));
            Assert.False(File.Exists(Path.Combine(dir, TaomDependencyFix.BackupName)));
            Assert.False(File.Exists(Path.Combine(dir, TaomDependencyFix.MarkName)));
            Assert.False(TaomDependencyFix.Restore(dir));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void A_workshop_update_becomes_the_new_original_and_is_never_written_over()
    {
        var root = NewRoot();
        try
        {
            var dir = Folder(root, "Retinues", Retinues);
            var xml = Path.Combine(dir, "SubModule.xml");
            TaomDependencyFix.Apply(dir, "Retinues");

            // Steam updates the mod while it is changed.
            var update = Retinues.Replace("v1.3.0", "v1.3.1");
            File.WriteAllText(xml, update);
            Assert.Equal(DependencyFixState.Needed, TaomDependencyFix.State(dir));
            Assert.NotNull(TaomDependencyFix.Apply(dir, "Retinues"));
            Assert.Equal(update, File.ReadAllText(Path.Combine(dir, TaomDependencyFix.BackupName)));
            Assert.Contains("v1.3.1", File.ReadAllText(xml));
            Assert.DoesNotContain("Bannerlord.ButterLib", File.ReadAllText(xml));

            // An update while nothing should be written back: the restore leaves the new file alone.
            var second = update.Replace("v1.3.1", "v1.3.2");
            File.WriteAllText(xml, second);
            Assert.False(TaomDependencyFix.Restore(dir));
            Assert.Equal(second, File.ReadAllText(xml));
            Assert.False(File.Exists(Path.Combine(dir, TaomDependencyFix.BackupName)));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Restore_all_puts_back_every_changed_mod_under_the_roots()
    {
        var root = NewRoot();
        try
        {
            var workshop = Path.Combine(root, "workshop");
            var a = Folder(Path.Combine(workshop, "111"), "RTSCamera", RtsCamera);
            var b = Folder(Path.Combine(root, "Modules"), "Retinues", Retinues);
            var c = Folder(Path.Combine(root, "Modules"), "Plain", NoLibraries);
            TaomDependencyFix.Apply(a, "RTS Camera");
            TaomDependencyFix.Apply(b, "Retinues");
            Assert.Null(TaomDependencyFix.Apply(c, "Plain"));
            var restored = TaomDependencyFix.RestoreAll(new[] { workshop, Path.Combine(root, "Modules") });
            Assert.Equal(2, restored.Count);
            Assert.Equal(RtsCamera, File.ReadAllText(Path.Combine(a, "SubModule.xml")));
            Assert.Equal(Retinues, File.ReadAllText(Path.Combine(b, "SubModule.xml")));
            Assert.Empty(TaomDependencyFix.RestoreAll(new[] { workshop, Path.Combine(root, "Modules") }));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void The_server_copy_gets_the_fix_and_the_players_copy_does_not()
    {
        var root = NewRoot();
        try
        {
            var game = Path.Combine(root, "game");
            var serverRoot = Path.Combine(root, "server");
            Directory.CreateDirectory(BannerlordCoopLauncher.Core.Modules.ServerModuleInstall.ModulesRoot(serverRoot));
            var source = Folder(game, "Retinues", Retinues);
            var setup = new TaomSetup { Name = "Mine" };
            TaomLoadOrder.Add(setup, "Retinues", "Retinues", "3599557394");
            var plan = TaomServerPath.Plan(setup, new[] { new FoundMod("Retinues", "Retinues", "3599557394", source, "Workshop") });
            var lines = TaomServerPath.Stage(serverRoot, plan);
            Assert.Contains(lines, line => line.StartsWith("TAOM dependency fix applied to Retinues."));
            var copy = Path.Combine(BannerlordCoopLauncher.Core.Modules.ServerModuleInstall.ModulesRoot(serverRoot), "Retinues");
            Assert.Equal(DependencyFixState.Applied, TaomDependencyFix.State(copy));
            Assert.Equal(Retinues, File.ReadAllText(Path.Combine(source, "SubModule.xml")));
            Assert.DoesNotContain(TaomServerPath.Stage(serverRoot, plan), line => line.StartsWith("TAOM dependency fix"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void The_checks_say_when_the_fix_is_needed_or_applied()
    {
        var root = NewRoot();
        try
        {
            var dir = Folder(Path.Combine(root, "Modules"), "Retinues", Retinues);
            var setup = new TaomSetup { Name = "Mine" };
            TaomLoadOrder.Add(setup, "Retinues", "Retinues", null);
            var needed = TaomModChecks.Run(setup, TaomModFacts.Gather(new[] { Path.Combine(root, "Modules") }));
            Assert.Contains(needed, issue => !issue.Blocks && issue.Text.Contains("The TAOM dependency fix takes them out"));
            Assert.DoesNotContain(needed, issue => issue.Blocks);
            TaomDependencyFix.Apply(dir, "Retinues");
            var applied = TaomModChecks.Run(setup, TaomModFacts.Gather(new[] { Path.Combine(root, "Modules") }));
            Assert.Contains(applied, issue => issue.Text.StartsWith("TAOM dependency fix applied to Retinues."));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Auto_sort_puts_rts_camera_at_the_very_bottom()
    {
        var setup = new TaomSetup { Name = "Mine" };
        foreach (var id in new[] { "RTSCamera", "RTSCamera.CommandSystem", "ModA", "ModB" })
            TaomLoadOrder.Add(setup, id, id, null);
        TaomLoadOrder.MoveTo(setup, "RTSCamera", ExtraSlot.BeforeTaom, 0);
        var manifests = new Dictionary<string, ModuleManifest>(StringComparer.OrdinalIgnoreCase)
        {
            ["RTSCamera.CommandSystem"] = ModuleManifestReader.Parse("<Module><Id value=\"RTSCamera.CommandSystem\"/><DependedModules><DependedModule Id=\"RTSCamera\"/></DependedModules></Module>")!
        };
        TaomAutoSort.Sort(setup, manifests);
        Assert.Equal(new[] { "ModA", "ModB", "RTSCamera", "RTSCamera.CommandSystem" }, setup.Extras.Select(mod => mod.ModuleId));
        Assert.All(setup.Extras, mod => Assert.Equal(ExtraSlot.AfterTaom, mod.Slot));
    }

    [Fact]
    public void Coop_and_friends_puts_back_taom_changes_before_it_starts_the_game()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "BannerlordCoopLauncher.sln")))
            dir = dir.Parent;
        var launch = File.ReadAllText(Path.Combine(dir!.FullName, "src", "BannerlordCoopLauncher", "MainWindow.Launch.cs"));
        var restore = launch.IndexOf("TaomDependencyFix.RestoreAll(", StringComparison.Ordinal);
        Assert.True(restore > 0);
        Assert.True(restore < launch.IndexOf("var started = Process.Start(start);", StringComparison.Ordinal));
    }
}
