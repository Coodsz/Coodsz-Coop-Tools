using BannerlordCoopLauncher.Core.Modules;
using BannerlordCoopLauncher.Core.Profile;
using CoodszHost.Services;
using TaomCoop.Core;

namespace BannerlordCoopLauncher.Tests;

public class AddModsToCampaignTests
{
    private static readonly string[] SaveMods = { "Native", "SandBoxCore", "Sandbox", "Bannerlord.Harmony", "Coop", "ImprovedGarrisons" };

    [Fact]
    public void Same_mods_start_as_they_are()
    {
        var change = SaveModChange.Check(SaveMods, new[] { "Native", "Bannerlord.Harmony", "Coop", "ImprovedGarrisons", "DedicatedServer.Windows" });
        Assert.Equal(SaveModAction.Start, change.Action);
        // a save with no module list has nothing to compare
        Assert.Equal(SaveModAction.Start, SaveModChange.Check(Array.Empty<string>(), new[] { "Coop", "BattleInstinct" }).Action);
    }

    // AD1259 crash report #9: a vanilla save (made by starting the Workshop server copy with its 5 mods) was
    // picked in the AD1259 setup, the host said yes to Add mods and the server stopped on load, since the
    // save has none of AD1259's settlements or kingdoms. Adding the setup's map mod is refused before Start.
    [Fact]
    public void Adding_the_campaign_map_mod_to_a_save_from_another_map_is_refused()
    {
        var vanilla = new[] { "Native", "SandBoxCore", "Sandbox", "Coop", "DedicatedServer.Windows" };
        var server = new[] { "Bannerlord.Harmony", "Bannerlord.ButterLib", "Native", "SandBoxCore", "Sandbox", "Coop", "RBM", "AD1259", "DedicatedServer.AD1259CoopCompat" };
        var change = SaveModChange.Check(vanilla, server);
        Assert.Equal(SaveModAction.AskToAdd, change.Action);
        Assert.True(change.AddsMap("AD1259"));
        Assert.True(change.AddsMap(" ad1259 "));
        // A setup on the base game map, or no map picked, only adds mods.
        Assert.False(change.AddsMap("Vanilla"));
        Assert.False(change.AddsMap(null));
        Assert.False(change.AddsMap(""));
        // A save made on the map already has it; a save missing mods is refused by the older rule.
        Assert.False(SaveModChange.Check(vanilla.Append("AD1259").ToArray(), server).AddsMap("AD1259"));
        var removed = SaveModChange.Check(new[] { "Native", "Coop", "OtherMap" }, server);
        Assert.Equal(SaveModAction.Refuse, removed.Action);
        Assert.False(removed.AddsMap("AD1259"));

        // Start refuses a replacement map. More Nations keeps every vanilla settlement, so a vanilla save
        // on the tools' own More Nations setup still gets the Add mods question (MainWindow.Saves says so too).
        Assert.True(MapModWarning.RefusesAdding(change, "AD1259"));
        var moreNations = SaveModChange.Check(vanilla, new[] { "Native", "SandBoxCore", "Sandbox", "Coop", "MoreNationsRemastered" });
        Assert.Equal(SaveModAction.AskToAdd, moreNations.Action);
        Assert.True(moreNations.AddsMap("MoreNationsRemastered"));
        Assert.False(MapModWarning.RefusesAdding(moreNations, "MoreNationsRemastered"));
        Assert.False(MapModWarning.RefusesAdding(change, null));
    }

    [Fact]
    public void Only_added_mods_ask_once_and_name_them()
    {
        var change = SaveModChange.Check(SaveMods, new[] { "Native", "Bannerlord.Harmony", "Coop", "ImprovedGarrisons", "BattleInstinct", "CoopSaveRescue" });
        Assert.Equal(SaveModAction.AskToAdd, change.Action);
        Assert.Equal(new[] { "BattleInstinct" }, change.Added);
        Assert.Empty(change.Removed);
        Assert.Equal("This campaign was made without this mod. Add it?", change.AskQuestion);
        Assert.Equal("Removing it later can break the save.", change.AskWarning);
        Assert.Contains("BattleInstinct", change.DeclinedText);

        var two = SaveModChange.Check(SaveMods, new[] { "Native", "Bannerlord.Harmony", "Coop", "ImprovedGarrisons", "BattleInstinct", "RBM" });
        Assert.Equal("This campaign was made without these mods. Add them?", two.AskQuestion);
        Assert.Equal("Removing them later can break the save.", two.AskWarning);

        // Coodsz: the No answer was one wall of mod ids. The status box now gets three lines: what happened,
        // the mods by name on their own line, and what to do.
        var lines = two.DeclinedLines(id => id == "RBM" ? "Realistic Battle Mod" : null);
        Assert.Equal(3, lines.Count);
        Assert.Equal("The server was not started. This campaign was made without these 2 mods:", lines[0]);
        Assert.Contains("Realistic Battle Mod", lines[1]);
        Assert.Contains("BattleInstinct", lines[1]);
        Assert.DoesNotContain(",", lines[1]);
        Assert.Equal("To play it as it was, turn them off on the Mods page, or make a new campaign save.", lines[2]);
        Assert.Equal("The server was not started. This campaign was made without this mod:", change.DeclinedLines(_ => null)[0]);
        // The list shows the name from the module list, or the id when there is none.
        Assert.Equal(new[] { ("Battle Instinct", "BattleInstinct"), ("RBM", "RBM") },
            two.AddedRows(id => id == "BattleInstinct" ? "Battle Instinct" : null));
    }

    [Fact]
    public void Libraries_without_save_data_are_not_a_change_but_ButterLib_and_MCM_are()
    {
        // A plain save, and the libraries: Harmony and UIExtenderEx store nothing in a save.
        // ButterLib registers save types and MCM keeps per save settings, so they still ask.
        var plain = new[] { "Native", "SandBoxCore", "Sandbox", "Coop" };
        var change = SaveModChange.Check(plain, new[] { "Bannerlord.Harmony", "Bannerlord.ButterLib", "Bannerlord.UIExtenderEx", "Bannerlord.MBOptionScreen", "Native", "DedicatedServer.Windows", "SandBoxCore", "SandBox", "Coop" });
        Assert.Equal(SaveModAction.AskToAdd, change.Action);
        Assert.Equal(new[] { "Bannerlord.ButterLib", "Bannerlord.MBOptionScreen" }, change.Added);
        Assert.Equal(SaveModAction.Start, SaveModChange.Check(plain, new[] { "Bannerlord.Harmony", "Native", "Bannerlord.UIExtenderEx", "SandBox", "Coop" }).Action);
        Assert.Equal(SaveModAction.Start, SaveModChange.Check(plain.Append("Bannerlord.Harmony").ToList(), plain).Action);

        // One rule: the server script's $nonSaveModuleIds is the same list (plus Save Rescue,
        // which CoopJoinRules leaves out everywhere).
        var script = File.ReadAllText(Path.Combine(RepoRoot(), "src", "CoodszHost", "DedicatedServerRepair", "Start-ModdedServer.ps1"));
        var line = System.Text.RegularExpressions.Regex.Match(script, @"\$nonSaveModuleIds = @\((.*)\)").Groups[1].Value;
        var ids = line.Split(',').Select(id => id.Trim().Trim('\'')).Where(id => id != "CoopSaveRescue").OrderBy(id => id).ToList();
        Assert.Equal(SaveModChange.NoSaveDataModules.OrderBy(id => id), ids);
    }

    [Fact]
    public void Coops_own_default_save_from_an_older_patch_starts_on_plain_coop()
    {
        // The live test: a copy of Coop's default_new_game.sav (game v1.4.6, Coop only) saved as
        // "plaintest" was refused by the server script on a v1.4.8 server with Plain Coop.
        const string header = "{\"List\":{\"Modules\":\"Native;SandBoxCore;Sandbox;Coop\",\"Module_Native\":\"v1.4.6.115628\",\"Module_SandBoxCore\":\"v1.4.6.115628\",\"Module_Sandbox\":\"v1.4.6.115628\",\"Module_Coop\":\"v0.0.2.0\",\"ApplicationVersion\":\"v1.4.6.115628\",\"CharacterName\":\"Pacarios\",\"DayLong\":\"0\",\"Version\":\"1\"}}";
        var plainSetup = new[] { "Native", "DedicatedServer.Windows", "SandBoxCore", "SandBox", "Coop" };
        Assert.Equal(SaveModAction.Start, SaveModChange.Check(new[] { "Native", "SandBoxCore", "Sandbox", "Coop" }, plainSetup).Action);

        var root = Path.Combine(Path.GetTempPath(), "plain-save-" + Guid.NewGuid().ToString("N"));
        var saves = Directory.CreateDirectory(Path.Combine(root, "saves")).FullName;
        try
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(header);
            using (var file = File.Create(Path.Combine(saves, "plaintest.sav")))
            using (var writer = new BinaryWriter(file))
            {
                writer.Write(bytes.Length);
                writer.Write(bytes);
                writer.Write(new byte[64]);
            }

            // Runs the script's own save functions on that folder.
            var script = File.ReadAllText(Path.Combine(RepoRoot(), "src", "CoodszHost", "DedicatedServerRepair", "Start-ModdedServer.ps1"));
            var functions = string.Join("\r\n", new[] { "Test-IsRecoverySaveName", "Get-PrimarySaveName", "Test-IsSelectablePrimarySaveName", "Get-BannerlordSaveInfo", "Test-SaveGameVersionLoads", "Get-SaveCandidates" }
                .Select(name => System.Text.RegularExpressions.Regex.Match(script, "(?ms)^function " + name + @" \{.*?^\}").Value));
            var run = "$strictUtf8NoBom = New-Object System.Text.UTF8Encoding($false, $true)\r\n" +
                      "$serverSaveRoot = '" + saves + "'\r\n" +
                      "$rollingSaveVersionModules = @(); $installedModuleVersions = @{ Coop = 'v0.1.6' }; $AllowAddedModules = $false\r\n" +
                      "$nativeGameVersionPrefix = 'v1.4.8'\r\n" + functions + "\r\n" +
                      "$found = @(Get-SaveCandidates -LoadedModuleIds @('" + string.Join("','", plainSetup) + "') | Where-Object { $_.Name -eq 'plaintest' })\r\n" +
                      "$lines = @(('found=' + $found.Count), ('newer=' + (Test-SaveGameVersionLoads -SaveVersion 'v1.4.9' -ServerVersion 'v1.4.8')), " +
                      "('oldminor=' + (Test-SaveGameVersionLoads -SaveVersion 'v1.3.8' -ServerVersion 'v1.4.8')), ('same=' + (Test-SaveGameVersionLoads -SaveVersion 'v1.4.8.2' -ServerVersion 'v1.4.8')))\r\n" +
                      "[System.IO.File]::WriteAllLines('" + Path.Combine(root, "out.txt") + "', [string[]]$lines)\r\n";
            File.WriteAllText(Path.Combine(root, "run.ps1"), run);
            using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoProfile -ExecutionPolicy Bypass -File \"" + Path.Combine(root, "run.ps1") + "\"",
                UseShellExecute = false,
                CreateNoWindow = true
            });
            Assert.NotNull(process);
            Assert.True(process!.WaitForExit(60000), "The save check did not finish.");
            Assert.Equal(new[] { "found=1", "newer=False", "oldminor=False", "same=True" }, File.ReadAllLines(Path.Combine(root, "out.txt")));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void A_nightly_campaign_keeps_its_saves_and_a_version_change_names_the_mod()
    {
        // CR-P4984: Coop and CoopNightly count as one mod, but the save's .launcher.json and the installed list
        // keep the version under the id that loaded, so every nightly save with a .launcher.json was hidden.
        var root = Path.Combine(Path.GetTempPath(), "nightly-save-" + Guid.NewGuid().ToString("N"));
        var saves = Directory.CreateDirectory(Path.Combine(root, "saves")).FullName;
        void WriteSave(string name, string coopId, string coopVersion)
        {
            var modules = "Native;SandBoxCore;Sandbox;" + coopId;
            var bytes = System.Text.Encoding.UTF8.GetBytes("{\"List\":{\"Modules\":\"" + modules + "\",\"ApplicationVersion\":\"v1.4.8.0\",\"CharacterName\":\"Host\",\"DayLong\":\"3\",\"Version\":\"1\"}}");
            var path = Path.Combine(saves, name + ".sav");
            using (var file = File.Create(path))
            using (var writer = new BinaryWriter(file))
            {
                writer.Write(bytes.Length);
                writer.Write(bytes);
                writer.Write(new byte[64]);
            }
            var saved = new FileInfo(path);
            File.WriteAllText(Path.ChangeExtension(path, ".launcher.json"),
                "{\"SchemaVersion\":2,\"SaveName\":\"" + name + "\",\"GameVersion\":\"v1.4.8\",\"Modules\":[\"" + modules.Replace(";", "\",\"") + "\"]," +
                "\"ModuleVersions\":{\"Native\":\"v1.4.8\",\"SandBoxCore\":\"v1.4.8\",\"Sandbox\":\"v1.4.8\",\"" + coopId + "\":\"" + coopVersion + "\"}," +
                "\"SaveLength\":" + saved.Length + ",\"SaveLastWriteTimeUtcTicks\":" + saved.LastWriteTimeUtc.Ticks + "}");
        }
        try
        {
            WriteSave("nightlyok", "CoopNightly", "v0.1.8.130");
            WriteSave("nightlyold", "CoopNightly", "v0.1.8.123");
            WriteSave("releasesame", "Coop", "v0.1.8.130");
            WriteSave("releaseold", "Coop", "v0.1.7");

            var script = File.ReadAllText(Path.Combine(RepoRoot(), "src", "CoodszHost", "DedicatedServerRepair", "Start-ModdedServer.ps1"));
            var functions = string.Join("\r\n", new[] { "Test-IsRecoverySaveName", "Get-PrimarySaveName", "Test-IsSelectablePrimarySaveName", "Get-BannerlordSaveInfo", "Test-SaveGameVersionLoads", "Get-SaveCandidates" }
                .Select(name => System.Text.RegularExpressions.Regex.Match(script, "(?ms)^function " + name + @" \{.*?^\}").Value));
            // Both Coop copies are installed and the server loads the nightly.
            var run = "$strictUtf8NoBom = New-Object System.Text.UTF8Encoding($false, $true)\r\n" +
                      "$serverSaveRoot = '" + saves + "'\r\n" +
                      "$rollingSaveVersionModules = @(); $installedModuleVersions = @{ Coop = 'v0.1.7'; CoopNightly = 'v0.1.8.130' }; $AllowAddedModules = $false\r\n" +
                      "$nativeGameVersionPrefix = 'v1.4.8'\r\n" + functions + "\r\n" +
                      "$found = @(Get-SaveCandidates -LoadedModuleIds @('Native', 'DedicatedServer.Windows', 'SandBoxCore', 'SandBox', 'CoopNightly') | ForEach-Object { $_.Name })\r\n" +
                      "Write-Host ('shown=' + (@(@('nightlyok', 'nightlyold', 'releasesame', 'releaseold') | Where-Object { $_ -in $found }) -join ','))\r\n";
            File.WriteAllText(Path.Combine(root, "run.ps1"), run);
            using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoProfile -ExecutionPolicy Bypass -File \"" + Path.Combine(root, "run.ps1") + "\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            });
            Assert.NotNull(process);
            var output = process!.StandardOutput.ReadToEnd();
            Assert.True(process.WaitForExit(60000), "The save check did not finish.");

            Assert.Contains("shown=nightlyok,releasesame", output);
            Assert.Contains("Save 'nightlyold' was made with CoopNightly v0.1.8.123 and this server has v0.1.8.130, so it is hidden.", output);
            Assert.Contains("Save 'releaseold' was made with Coop v0.1.7 and this server has CoopNightly v0.1.8.130, so it is hidden.", output);
            Assert.DoesNotContain("Save 'nightlyok'", output);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Crash_explain_for_a_save_hidden_by_a_version_change_names_the_mod()
    {
        // CR-P4984: the mods matched, only Coop's version did not, yet the tool said the mods don't match.
        const string refused = "Save 'orta 7' was not found or is incompatible with the selected module set.";
        var lines = ServerCrashExplain.Explain(null, false,
            "Save 'old run' was made with Coop v0.1.7 and this server has CoopNightly v0.1.8.130, so it is hidden.\n" +
            "Save 'orta 7' was made with CoopNightly v0.1.8.123 and this server has v0.1.8.130, so it is hidden.\n" +
            "Hidden incompatible saves: 2\n" + refused, null, [], []);
        var text = string.Join("\n", lines);
        Assert.Contains("The campaign save named orta 7 was made with CoopNightly v0.1.8.123 and this server has v0.1.8.130.", text);
        Assert.DoesNotContain("does not match the mods", text);

        // A version line for another save leaves a real mod difference on the old text.
        var other = string.Join("\n", ServerCrashExplain.Explain(null, false,
            "Save 'old run' was made with Coop v0.1.7 and this server has CoopNightly v0.1.8.130, so it is hidden.\n" + refused, null, [], []));
        Assert.Contains("does not match the mods that are turned on", other);

        // A save name with an apostrophe is still named (Codex).
        var apostrophe = string.Join("\n", ServerCrashExplain.Explain(null, false,
            "Save 'King's run' was made with CoopNightly v0.1.8.123 and this server has v0.1.8.130, so it is hidden.\n" +
            "Save 'King's run' was not found or is incompatible with the selected module set.", null, [], []));
        Assert.Contains("The campaign save named King's run was made with CoopNightly v0.1.8.123 and this server has v0.1.8.130.", apostrophe);
    }

    [Fact]
    public void An_unlocked_setup_only_turns_on_the_mods_in_its_order()
    {
        // A copy of a fresh setup lists the working order. RBM and an armour pack were
        // installed on the server later; they stay off instead of joining the campaign.
        var fresh = StableCoop.CreateFresh("Profile 2");
        var copy = new HostProfile { Name = "Profile 3", ModuleOrder = fresh.ModuleOrder.ToList(), DisabledModules = fresh.DisabledModules.ToList() };
        Assert.True(StableCoop.UnlockedEnables(copy, "Coop"));
        Assert.True(StableCoop.UnlockedEnables(copy, "coop"));
        Assert.False(StableCoop.UnlockedEnables(copy, "RBM"));
        Assert.False(StableCoop.UnlockedEnables(copy, "HeavyArmourPack"));
        Assert.False(StableCoop.UnlockedEnables(copy, "CoopNightly"));

        // A mod the host saved into the order is on; one turned off stays off.
        copy.ModuleOrder.Add("RBM");
        Assert.True(StableCoop.UnlockedEnables(copy, "RBM"));
        copy.DisabledModules.Add("RBM");
        Assert.False(StableCoop.UnlockedEnables(copy, "RBM"));

        // An old setup that never saved an order keeps every mod it does not turn off.
        var old = new HostProfile { Name = "Default", DisabledModules = { "RBM" } };
        Assert.True(StableCoop.UnlockedEnables(old, "HeavyArmourPack"));
        Assert.False(StableCoop.UnlockedEnables(old, "RBM"));

        // The Modules page uses it; the old rule turned on every mod missing from the order.
        var window = SourceFiles.Window(RepoRoot(), "CoodszHost");
        var apply = SourceFiles.Method(window, "private void ApplyProfileModules(HostProfile profile)");
        Assert.Contains("row.Enabled = StableCoop.UnlockedEnables(profile, row.Id);", apply);
        Assert.DoesNotContain("row.Enabled = !disabled.Contains(row.Id);", apply);
        // Mods the host picks in Add mods are not in the order yet, so they are turned on there.
        Assert.Contains("row.Enabled = true;", SourceFiles.Method(window, "private void AddMods_Click("));
    }

    [Fact]
    public void A_missing_mod_is_refused_even_when_others_are_added()
    {
        var change = SaveModChange.Check(SaveMods, new[] { "Native", "Bannerlord.Harmony", "Coop", "BattleInstinct" });
        Assert.Equal(SaveModAction.Refuse, change.Action);
        Assert.Equal(new[] { "ImprovedGarrisons" }, change.Removed);
        Assert.Equal(new[] { "BattleInstinct" }, change.Added);
        Assert.Contains("made with ImprovedGarrisons", change.RefuseText);
        Assert.Contains("Removing mods from a running campaign can break it.", change.RefuseText);
    }

    [Fact]
    public void Patch_modules_and_the_old_Coop_name_are_not_a_change()
    {
        // the server script leaves these out of its save check too ($nonSaveModuleIds)
        var change = SaveModChange.Check(SaveMods.Append("StuckBattleFix").ToList(),
            new[] { "Bannerlord.Harmony", "CoopNightly", "ImprovedGarrisons", "Diplomacy.CoopCompat" });
        Assert.Equal(SaveModAction.Start, change.Action);
    }

    [Fact]
    public void Adding_limitless_coop_compat_keeps_an_old_save_usable()
    {
        // Limitless.CoopCompat only patches code; the Add Limitless Coop Compat button puts it in a running campaign.
        var change = SaveModChange.Check(SaveMods.Append("Limitless").ToList(),
            new[] { "Bannerlord.Harmony", "CoopNightly", "ImprovedGarrisons", "Limitless", "Limitless.CoopCompat" });
        Assert.Equal(SaveModAction.Start, change.Action);
        Assert.Contains("Limitless.CoopCompat", SaveModChange.NoSaveDataModules);
        var script = File.ReadAllText(Path.Combine(RepoRoot(), "src", "CoodszHost", "DedicatedServerRepair", "Start-ModdedServer.ps1"));
        Assert.Contains("'StuckBattleFix', 'Limitless.CoopCompat', 'Bannerlord.Harmony'", script);
    }

    [Fact]
    public void Host_passes_the_flag_only_for_an_existing_save()
    {
        var load = new LaunchOptions { SaveName = "Finale", AllowAddedModules = true }.ToArguments();
        Assert.EndsWith(" -AllowAddedModules", load);
        Assert.DoesNotContain("-AllowAddedModules", new LaunchOptions { SaveName = "Finale" }.ToArguments());
        Assert.DoesNotContain("-AllowAddedModules", new LaunchOptions { SaveName = "Fresh", CreateNew = true, AllowAddedModules = true }.ToArguments());
    }

    [Fact]
    public void Start_script_accepts_added_mods_only_with_the_flag()
    {
        var script = File.ReadAllText(Path.Combine(RepoRoot(), "src", "CoodszHost", "DedicatedServerRepair", "Start-ModdedServer.ps1"));
        Assert.Contains("[switch]$AllowAddedModules", script);
        // the superset path: only with the flag, the server has more mods, and none of the save's mods is gone
        Assert.Contains("if (-not $moduleIdsMatch -and $AllowAddedModules -and", script);
        Assert.Contains("$serverCommunityModules.Count -gt $saveCommunityModules.Count -and", script);
        Assert.Contains("$_ -notin $serverCommunityModules", script);
        // a moved mod no longer hides the save (Bannerlord never checks the order); the start says so
        Assert.Contains("$serverSaveOrder = @($serverCommunityModules | Where-Object {", script);
        Assert.Contains("[string]$serverSaveOrder[$moduleIndex],", script);
        Assert.Contains("$moduleOrderChanged = $true", script);
        Assert.Contains("$saveModulesLoad = $moduleIdsMatch -and", script);
        Assert.Contains("$candidate.ModuleOrderChanged = $moduleOrderChanged", script);
        Assert.Contains("'Mod load order changed since this save was made. Saved order: ' +", script);
        // versions are still checked for every mod the save was made with
        Assert.Contains("foreach ($moduleId in $saveCommunityModules) {", script);
        Assert.Contains("$candidate.AddedModules = $addedModules", script);
        Assert.Contains("'Adding mods to an existing campaign: ' +", script);
        Assert.Contains("'. Removing them later can break the save.'", script);
        Assert.Contains("$rollingModuleId -in @($selectedSave.AddedModules)", script);
        Assert.DoesNotContain("\n", script.Replace("\r\n", ""));
        Assert.True(script.All(ch => ch < 128), "Start-ModdedServer.ps1 stays ASCII");
    }

    [Fact]
    public void A_refused_save_is_no_load_crash()
    {
        // CR-Z4X89: the start script refused the save, and the Host ran the deeper save fixes twice.
        Assert.True(ServerCrashExplain.IsSaveRefusedLine("Save '261013 2118' was not found or is incompatible with the selected module set."));
        Assert.True(ServerCrashExplain.IsSaveRefusedLine("+ throw \"Configured save 'x' was not found or is incompatible with the selected module set.\""));
        Assert.False(ServerCrashExplain.IsSaveRefusedLine("Hidden incompatible saves: 15"));
        Assert.False(ServerCrashExplain.IsSaveRefusedLine(null));
        var host = File.ReadAllText(Path.Combine(RepoRoot(), "src", "CoodszHost", "MainWindow.CrashRestart.cs"));
        Assert.True(host.IndexOf("IsSaveRefusedStop(info)", StringComparison.Ordinal) < host.IndexOf("RetryAfterLoadCrash(info, loadStep, crashLine)", StringComparison.Ordinal));
    }

    [Fact]
    public void Crash_explain_for_a_refused_save_mentions_adding_mods()
    {
        var lines = ServerCrashExplain.Explain(null, false, "Save 'lol' was not found or is incompatible with the selected module set.", null, [], []);
        var text = string.Join("\n", lines);
        Assert.Contains("If you only added mods", text);
        Assert.Contains("asks to add them to this campaign and makes a backup first", text);
        Assert.Contains("Removing a mod can break a campaign.", text);
    }

    [Fact]
    public void Taom_save_change_tells_added_from_removed()
    {
        var path = Path.Combine(Path.GetTempPath(), "taom-change-" + Guid.NewGuid().ToString("N") + ".sav");
        try
        {
            File.WriteAllText(path, "XXXX{\"List\":{\"CharacterName\":\"Ann\",\"MainHeroLevel\":\"3\",\"DayLong\":\"2\",\"Modules\":\"TAOM.Dependencies;Native;SandBoxCore;Sandbox;CustomBattle;TAOM;TAOM_Map;LOTRLOME_Armory;Coop;TAOM.CoopCompat;ModA\"}}rest");
            var plain = new TaomSetup { Name = "Plain TAOM", Locked = true };
            Assert.Equal(SaveModAction.Start, TaomServerPath.SaveChange(path, Setup("ModA")).Action);
            var added = TaomServerPath.SaveChange(path, Setup("ModA", "ModB"));
            Assert.Equal(SaveModAction.AskToAdd, added.Action);
            Assert.Equal(new[] { "ModB" }, added.Added);
            var removed = TaomServerPath.SaveChange(path, plain);
            Assert.Equal(SaveModAction.Refuse, removed.Action);
            Assert.Equal(new[] { "ModA" }, removed.Removed);
            Assert.Equal(SaveModAction.Start, TaomServerPath.SaveChange(path + ".missing", plain).Action);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static TaomSetup Setup(params string[] ids)
    {
        var setup = new TaomSetup { Name = "Mine" };
        foreach (var id in ids)
            Assert.Null(TaomLoadOrder.Add(setup, id + " name", id, null));
        return setup;
    }

    [Fact]
    public void Locked_setup_note_tells_how_to_change_mods()
    {
        Assert.Equal("Default is locked. Click New setup to change mods or their order.", LockedSetupNote.Line("Default"));
        Assert.Contains("New setup", LockedSetupNote.ButtonTip);
        foreach (var tool in new[] { Path.Combine("src", "BannerlordCoopLauncher"), Path.Combine("src", "CoodszHost") })
        {
            var xaml = File.ReadAllText(Path.Combine(RepoRoot(), tool, "MainWindow.xaml"));
            Assert.Contains("x:Name=\"LockedSetupTag\"", xaml);
            Assert.Contains("x:Name=\"LockedSetupText\"", xaml);
        }

        // The AD1259 server tool builds from the Coodsz Host window but has no mod features,
        // so its one fixed set shows no Locked tag or line.
        var fields = SourceFiles.Method(SourceFiles.Window(RepoRoot(), "CoodszHost"), "private void ApplyProfileToFields(HostProfile profile)");
        Assert.Contains("var showLocked = profile.Locked && !Edition.IsAD1259;", fields);
        Assert.Contains("LockedSetupTag.Visibility = showLocked ?", fields);
        Assert.Contains("LockedSetupText.Visibility = showLocked ?", fields);
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "BannerlordCoopLauncher.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
