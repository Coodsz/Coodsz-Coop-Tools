using BannerlordCoopLauncher.Core.AD1259;
using BannerlordCoopLauncher.Core.Modules;
using BannerlordCoopLauncher.Core.Server;
using BannerlordCoopLauncher.Core.Updates;
using CoodszHost.Services;
using TaomCoop.Core;

namespace BannerlordCoopLauncher.Tests;

public class AD1259ToolsTests
{
    [Fact]
    public void Client_order_is_the_order_the_test_join_was_accepted_with()
    {
        Assert.Equal(
            new[]
            {
                "Bannerlord.Harmony", "Bannerlord.ButterLib", "Bannerlord.UIExtenderEx", "Bannerlord.MBOptionScreen",
                "Native", "SandBoxCore", "Sandbox", "StoryMode", "CustomBattle", "RBM", "ExpandedArmouryBL",
                "swadian armoury", "OpenSourceArmory", "OpenSourceSaddlery", "OpenSourceWeaponry", "Templar Armor",
                "Templar Equipment", "SettlementIconRedesign", "AD1259", "Coop"
            },
            AD1259Stack.ClientModules);
        Assert.All(AD1259Stack.ClientMods.Where(mod => !mod.BaseGame), mod => Assert.NotNull(mod.Link));
    }

    [Fact]
    public void Server_order_has_the_engine_module_coop_early_and_the_compat_module_last()
    {
        var order = AD1259Stack.ServerModules.ToList();
        Assert.Equal(order.IndexOf("Native") + 1, order.IndexOf("DedicatedServer.Windows"));
        Assert.Equal(order.IndexOf("Sandbox") + 1, order.IndexOf("Coop"));
        Assert.Equal(AD1259Stack.CompatModuleId, order[^1]);
        Assert.Equal(order.Count - 2, order.IndexOf("AD1259"));
        Assert.DoesNotContain("StoryMode", order);
        Assert.DoesNotContain("CustomBattle", order);
        // Every player mod except the two the server leaves out is on the server.
        Assert.All(AD1259Stack.ClientModules.Where(id => id is not ("StoryMode" or "CustomBattle")), id => Assert.Contains(id, order));
    }

    [Fact]
    public void Versions_match_exactly_except_the_base_game_build()
    {
        var native = AD1259Stack.Find("Native")!;
        Assert.True(AD1259Stack.VersionMatches(native, "v1.4.8"));
        Assert.True(AD1259Stack.VersionMatches(native, "v1.4.8.118999"));
        Assert.False(AD1259Stack.VersionMatches(native, "v1.4.6"));

        var butterLib = AD1259Stack.Find("Bannerlord.ButterLib")!;
        Assert.True(AD1259Stack.VersionMatches(butterLib, "v2.12.0.0"));
        Assert.True(AD1259Stack.VersionMatches(butterLib, "v2.12.0"));
        Assert.False(AD1259Stack.VersionMatches(butterLib, "v2.12.1"));

        // Coop 0.1.7 is the set's Coop; 0.1.6 is still taken while Steam moves players over (Coop itself refuses a
        // join between different Coop versions). Nothing else is.
        var coop = AD1259Stack.Find("Coop")!;
        Assert.Equal("v0.1.7", coop.Version);
        Assert.True(AD1259Stack.VersionMatches(coop, "v0.1.7"));
        Assert.True(AD1259Stack.VersionMatches(coop, "v0.1.6"));
        Assert.False(AD1259Stack.VersionMatches(coop, "v0.1.5"));
        Assert.False(AD1259Stack.VersionMatches(coop, "v0.1.8"));
        Assert.True(AD1259Stack.IsNewerThanPinned(coop, "v0.1.8"));
        Assert.False(AD1259Stack.IsNewerThanPinned(coop, "v0.1.6"));

        var armoury = AD1259Stack.Find("ExpandedArmouryBL")!;
        Assert.True(AD1259Stack.VersionMatches(armoury, "e1.5.2"));
        Assert.False(AD1259Stack.VersionMatches(armoury, "v1.5.2"));
        Assert.False(AD1259Stack.VersionMatches(AD1259Stack.Find("AD1259")!, ""));
    }

    [Theory]
    [InlineData("v1.4.7", true)]
    [InlineData("v1.5.0", true)]
    [InlineData("v1.10.0", true)]
    [InlineData("v1.4.5", false)]
    [InlineData("v1.4.6", false)]
    [InlineData("v1.4.6.0", false)]
    [InlineData("", false)]
    [InlineData("abc", false)]
    [InlineData("v", false)]
    public void Newer_version_check_compares_numeric_parts(string found, bool expected)
    {
        Assert.Equal(expected, AD1259Stack.IsNewerThanPinned(AD1259Stack.Find("AD1259")!, found));
    }

    [Fact]
    public void Newer_version_check_ignores_the_base_game_build_number()
    {
        var native = AD1259Stack.Find("Native")!;
        Assert.False(AD1259Stack.IsNewerThanPinned(native, "v1.4.8.118999"));
        Assert.True(AD1259Stack.IsNewerThanPinned(native, "v1.4.9"));
        Assert.False(AD1259Stack.IsNewerThanPinned(native, "v1.4.7"));
        Assert.False(AD1259Stack.IsNewerThanPinned(AD1259Stack.Find("AD1259")!, null));
    }

    [Fact]
    public void Mod_status_explains_newer_versions_and_keeps_the_old_version_text()
    {
        var ad1259 = AD1259Stack.Find("AD1259")!;
        var newer = new AD1259ModStatus(ad1259, AD1259ModState.WrongVersion, "v1.4.7", [], []);
        Assert.Equal("Anno Domini 1259 is v1.4.7, which is newer than this tool supports. " + AD1259Stack.NewerAdvice
            + " Until then, go back to v1.4.6 from Nexus Mods.", newer.Problem);
        Assert.Equal("Newer than this tool supports (found v1.4.7, needs v1.4.6) \u00b7 Nexus Mods. Update the tool first.", newer.RowDetail);

        var older = new AD1259ModStatus(ad1259, AD1259ModState.WrongVersion, "v1.4.5", [], []);
        Assert.Equal("Anno Domini 1259 is v1.4.5. The server needs v1.4.6.", older.Problem);
        Assert.Equal("Needs v1.4.6, found v1.4.5 \u00b7 Nexus Mods", older.RowDetail);

        var missing = new AD1259ModStatus(ad1259, AD1259ModState.WrongVersion, "", [], []);
        Assert.Contains("missing a version", missing.Problem);
        Assert.Contains("found no version", missing.RowDetail);

        var newerNative = new AD1259ModStatus(AD1259Stack.Find("Native")!, AD1259ModState.WrongVersion, "v1.4.9", [], []);
        // No tool update fixes a newer game, so it says how to go back to 1.4.8 in Steam (NewerGame).
        Assert.Equal(BannerlordCoopLauncher.Core.Steam.NewerGame.Line("v1.4.9"), newerNative.Problem);
        Assert.DoesNotContain(AD1259Stack.NewerAdvice, newerNative.Problem);
    }

    [Fact]
    public void Mod_scan_reports_ready_missing_wrong_and_duplicate_copies()
    {
        var root = Temp();
        try
        {
            var modules = Path.Combine(root, "Modules");
            var workshop = Path.Combine(root, "workshop");
            Module(modules, "AD1259", "AD1259", "v1.4.6");
            Module(modules, "Templar Armor", "Templar Armor", "v1.0.2");
            Module(modules, "RBM", "RBM", "v4.5.4");
            Module(workshop, "2859251492", "RBM", "v4.5.4");
            Module(workshop, "3770450698", "Coop", "v0.1.6");
            Module(modules, "ExpandedArmouryBL", "ExpandedArmouryBL", "e1.5.2");

            var statuses = AD1259ModScan.Check(modules, [workshop]).ToDictionary(status => status.Mod.Id);

            Assert.Equal(AD1259ModState.Ready, statuses["AD1259"].State);
            Assert.Equal(AD1259ModState.Ready, statuses["Coop"].State);
            Assert.Equal(AD1259ModState.WrongVersion, statuses["Templar Armor"].State);
            Assert.Contains("v1.0.2", statuses["Templar Armor"].Problem);
            Assert.Equal(AD1259ModState.Duplicate, statuses["RBM"].State);
            Assert.Contains("Workshop 2859251492", statuses["RBM"].Problem);
            Assert.Equal(AD1259ModState.Missing, statuses["SettlementIconRedesign"].State);
            Assert.Contains("Nexus Mods", statuses["SettlementIconRedesign"].Problem);
            Assert.Contains("Verify the game files", statuses["Native"].Problem);

            Assert.False(AD1259ModScan.HasArmouryFix(statuses["ExpandedArmouryBL"]));
            var bin = Path.Combine(modules, "ExpandedArmouryBL", "bin", "Win64_Shipping_Client");
            Directory.CreateDirectory(bin);
            File.WriteAllText(Path.Combine(bin, AD1259Stack.ArmouryFixDll), "");
            Assert.True(AD1259ModScan.HasArmouryFix(statuses["ExpandedArmouryBL"]));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void The_coop_nightly_is_refused_with_how_to_go_back_not_update_the_tool()
    {
        // Every nightly's SubModule.xml says v0.1.8; Common.dll says 0.1.7+commit for the Workshop Coop (CoopBuildId).
        Assert.True(AD1259Stack.IsNightlyCoop("v0.1.8"));
        Assert.True(AD1259Stack.IsNightlyCoop("v0.1.7", "0.1.8+26a82a49d947ae45bd32977b1eefd30c54a419e5"));
        Assert.False(AD1259Stack.IsNightlyCoop("v0.1.7", "0.1.7+ecd4c479b0c35348c13c3ff4710cea87f799aa40"));
        Assert.False(AD1259Stack.IsNightlyCoop("v0.1.6", null));
        Assert.False(AD1259Stack.IsNightlyCoop("", "ecd4c479b0c35348c13c3ff4710cea87f799aa40"));

        var root = Temp();
        try
        {
            // A nightly unzipped into Modules\Coop beside the Workshop Coop: the Modules copy is the one the game loads.
            var modules = Path.Combine(root, "Modules");
            var workshop = Path.Combine(root, "workshop");
            Module(modules, "Coop", "Coop", "v0.1.8");
            Module(workshop, "3770450698", "Coop", "v0.1.7");
            var coop = AD1259ModScan.Check(modules, [workshop]).Single(status => status.Mod.Id == "Coop");
            Assert.True(coop.Nightly);
            Assert.False(coop.IsReady);
            Assert.Equal(AD1259Stack.NightlyNeeds + "\n" + AD1259ModStatus.NightlyInModules, coop.Problem);
            Assert.DoesNotContain(AD1259Stack.NewerAdvice, coop.Problem);
            Assert.StartsWith("Coop nightly (found v0.1.8, needs v0.1.7)", coop.RowDetail);

            // Only the old CoopNightly id: no Coop would load, so the get-it line stays under the nightly line.
            Directory.Delete(Path.Combine(modules, "Coop"), recursive: true);
            Directory.Delete(Path.Combine(workshop, "3770450698"), recursive: true);
            Module(modules, "CoopNightly", "CoopNightly", "v0.1.8");
            coop = AD1259ModScan.Check(modules, [workshop]).Single(status => status.Mod.Id == "Coop");
            Assert.Equal(AD1259ModState.Missing, coop.State);
            Assert.Equal(AD1259Stack.NightlyNeeds + "\nBannerlord Coop is missing. Get v0.1.7 from Steam Workshop.", coop.Problem);

            // The Workshop Coop alone stays ready.
            Module(workshop, "3770450698", "Coop", "v0.1.7");
            coop = AD1259ModScan.Check(modules, [workshop]).Single(status => status.Mod.Id == "Coop");
            Assert.True(coop.IsReady);
            Assert.False(coop.Nightly);

            // Nightly files copied over the Workshop Coop keep its SubModule.xml v0.1.7; Common.dll's build gives them away.
            // This test's Core assembly stands in for Common.dll: its ProductVersion is newer than 0.1.7, like a 0.1.8 nightly.
            var stub = typeof(AD1259Stack).Assembly.Location;
            var stubBuild = System.Diagnostics.FileVersionInfo.GetVersionInfo(stub).ProductVersion!;
            Assert.True(AD1259Stack.IsNewerThanPinned(AD1259Stack.Find("Coop")!, CoopBuildId.Version(stubBuild)), stubBuild);
            var bin = Path.Combine(workshop, "3770450698", "bin", "Win64_Shipping_Client");
            Directory.CreateDirectory(bin);
            File.Copy(stub, Path.Combine(bin, CoopBuildId.AssemblyName));
            coop = AD1259ModScan.Check(modules, [workshop]).Single(status => status.Mod.Id == "Coop");
            Assert.True(coop.Nightly);
            Assert.Equal(AD1259ModState.WrongVersion, coop.State);
            Assert.Equal(CoopBuildId.Version(stubBuild), coop.FoundVersion);
            Assert.Equal(AD1259Stack.NightlyNeeds + "\n" + AD1259ModStatus.NightlyInWorkshop, coop.Problem);

            // Two Coop copies in Modules, one of them the nightly: keep one, and the nightly line says which.
            Directory.Delete(Path.Combine(modules, "CoopNightly"), recursive: true);
            Directory.Delete(Path.Combine(workshop, "3770450698"), recursive: true);
            Module(modules, "Coop", "Coop", "v0.1.7");
            Module(modules, "Coop nightly", "Coop", "v0.1.8");
            coop = AD1259ModScan.Check(modules, [workshop]).Single(status => status.Mod.Id == "Coop");
            Assert.Equal(AD1259ModState.Duplicate, coop.State);
            Assert.True(coop.Nightly);
            Assert.EndsWith("Keep one copy; two copies of a mod crash the game.\n" + AD1259Stack.NightlyNeeds, coop.Problem);
            Assert.StartsWith("Keep one: ", coop.RowDetail);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }

        // The Server Tool refuses Start on a nightly server before any mod step.
        var rows = CompleteServerRows();
        var picked = @"C:\CoopNightlyServer";
        var workshopServer = @"C:\Steam\steamapps\workshop\content\261550\3770450698\DedicatedServer";
        var modulesServer = @"C:\Steam\steamapps\common\Mount & Blade II Bannerlord\Modules\Bannerlord Coop\DedicatedServer";
        Assert.Null(AD1259Server.NightlyServer(rows, "0.1.7+ecd4c479b0c35348c13c3ff4710cea87f799aa40", picked, true));
        var nightlyServer = AD1259Stack.NightlyNeeds + " " + AD1259Server.NightlyServerAdvice;
        Assert.Equal(nightlyServer, AD1259Server.NightlyServer(rows, "0.1.8+26a82a49", picked, true));
        // The Use the Steam server step only while its line shows; elsewhere the way back that works there.
        Assert.Equal(AD1259Stack.NightlyNeeds + " " + AD1259Server.NightlyPickedNoSteamAdvice, AD1259Server.NightlyServer(rows, "0.1.8+26a82a49", picked, false));
        Assert.Equal(AD1259Stack.NightlyNeeds + " " + AD1259ModStatus.NightlyInWorkshop, AD1259Server.NightlyServer(rows, "0.1.8+26a82a49", workshopServer, false));
        Assert.Equal(AD1259Stack.NightlyNeeds + " " + AD1259ModStatus.NightlyInModules, AD1259Server.NightlyServer(rows, "0.1.8+26a82a49", modulesServer, false));
        // A picked folder whose nightly only shows in the build gets the line, so the refusal's button is there.
        Assert.True(AD1259Server.IsNightlyServer(rows, "0.1.8+26a82a49"));
        Assert.Equal("Your picked server folder has the Coop nightly. Steam has Coop v0.1.7.",
            CoopInstall.ChosenServerOlderLine(picked, "v0.1.7", "v0.1.7", ad1259: true, nightly: AD1259Server.IsNightlyServer(rows, "0.1.8+26a82a49")));
        Assert.Null(CoopInstall.ChosenServerOlderLine(workshopServer, "v0.1.7", "v0.1.7", ad1259: true, nightly: true));
        Assert.Null(CoopInstall.ChosenServerOlderLine(modulesServer, "v0.1.7", "v0.1.7", ad1259: true, nightly: true));
        rows[rows.FindIndex(row => row.Id == "Coop")] = new ModuleRow { Id = "Coop", Version = "v0.1.8" };
        Assert.Equal(nightlyServer, AD1259Server.NightlyServer(rows, null, picked, true));
        rows.RemoveAll(row => row.Id == "Coop");
        rows.Add(new ModuleRow { Id = "CoopNightly", Version = "v0.1.8" });
        Assert.Equal(nightlyServer, AD1259Server.NightlyServer(rows, null, picked, true));
        // A lone CoopNightly module has no Coop version, so the line hangs on the shared nightly answer alone.
        Assert.True(AD1259Server.IsNightlyServer(rows, null));
        Assert.NotNull(CoopInstall.ChosenServerOlderLine(picked, null, "v0.1.7", ad1259: true, nightly: AD1259Server.IsNightlyServer(rows, null)));

        // In StartServer the refusal comes before every step that copies or changes server mods, and a newer game on this
        // PC is only the warning under Start server (the server runs the Workshop's own 1.4.8).
        var start = SourceFiles.Method(SourceFiles.Window(RepoRoot(), "CoodszHost"), "private void StartServer()");
        var refusal = start.IndexOf("AD1259Server.NightlyServer(", StringComparison.Ordinal);
        Assert.True(refusal > 0);
        // Only the AD1259 build refuses (the Coop build runs the nightly), and the refusal and the line ask one nightly answer.
        Assert.Contains("if (Edition.IsAD1259 && AD1259Server.NightlyServer(_modules.ToList(), CoopBuildId.FromServer(ServerRoot), ServerRoot, SteamServerState().Line != null) is { } nightly)", start);
        var host = SourceFiles.Window(RepoRoot(), "CoodszHost");
        Assert.Contains("var steamNewer = SteamServerState().Line;", host);
        var state = SourceFiles.Method(host, "private (bool Nightly, string? Line) SteamServerState(");
        Assert.Contains("Edition.IsAD1259 && AD1259Server.IsNightlyServer(_modules.ToList(), CoopBuildId.FromServer(ServerRoot))", state);
        foreach (var step in new[] { "ParkExtraCoop();", "ExtraModScan.UpdateFromWorkshop(", "ExtraModScan.UpdateNewerOnlyFromGame(", "PrepareAD1259Server();", "CoodszServerTools.Install(" })
            Assert.True(refusal < start.IndexOf(step, StringComparison.Ordinal), step);
        Assert.DoesNotContain("_newerGame", start);

        // A picked nightly server folder shows Use the Steam server in the AD1259 tool only.
        Assert.Equal("Your picked server folder has the Coop nightly. Steam has Coop v0.1.7.",
            CoopInstall.ChosenServerOlderLine(picked, "v0.1.8", "v0.1.7", ad1259: true));
        Assert.Null(CoopInstall.ChosenServerOlderLine(picked, "v0.1.8", "v0.1.7"));
        Assert.Null(CoopInstall.ChosenServerOlderLine(picked, "v0.1.8", null, ad1259: true));
    }

    [Fact]
    public async Task A_join_resume_that_waited_behind_a_pause_never_overwrites_a_speed_the_host_sent()
    {
        // Pause on join is on by default in AD1259 now. JoinPauseGate, driven the way Coodsz Host drives it.
        var pause = new JoinPause();
        var gate = new JoinPauseGate(pause);
        var sent = new List<string>();

        // A join pause is on its way (it holds the gate). The host presses Pause game: noted first, then it waits.
        Assert.True(await gate.EnterStep(JoinPauseStep.Pause));
        Assert.True(gate.Busy);
        pause.HostSent(JoinPause.PauseCommand);
        var host = Task.Run(async () =>
        {
            Assert.True(await gate.EnterHost(JoinPause.PauseCommand));
            lock (sent)
                sent.Add("host pause");
            gate.Release();
        });
        await Task.Delay(50);
        Assert.False(host.IsCompleted);
        lock (sent)
            sent.Add("join pause");
        gate.Release();
        await host.WaitAsync(TimeSpan.FromSeconds(5));
        // The host's command went out after the step that was on its way.
        Assert.Equal(new[] { "join pause", "host pause" }, sent);

        // A resume step taken before the host's press, still waiting for the gate when it moved, is dropped.
        Assert.True(await gate.EnterStep(JoinPauseStep.Pause));
        var queuedResume = gate.EnterStep(JoinPauseStep.Resume);
        pause.HostSent(JoinPause.SetModeCommand + " " + JoinPause.NormalMode);
        gate.Release();
        Assert.False(await queuedResume.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.False(gate.Busy);
        // A resume with no host press in between goes ahead; a non time command never waits.
        var plain = await gate.EnterStep(JoinPauseStep.Resume);
        Assert.True(plain);
        gate.Release();
        Assert.False(await gate.EnterHost("coop.debug.players.list"));

        var host2 = SourceFiles.Window(RepoRoot(), "CoodszHost");
        Assert.Contains("if (!await JoinGate.EnterStep(step))\r\n            return;", SourceFiles.Method(host2, "private async void DoJoinPause("));
        var send = SourceFiles.Method(host2, "private async Task<string> SendHostCommand(");
        Assert.Contains("var entered = await JoinGate.EnterHost(command);", send);
        // A Restart while it waited: the command is dropped, never sent to the new server.
        var run = send.IndexOf("var run = _session.RunId;", StringComparison.Ordinal);
        var dropped = send.IndexOf("if (_session.RunId != run)\r\n                return TaomCoop.Core.ServerCommandQueue.RunEnded;", StringComparison.Ordinal);
        Assert.True(run >= 0 && run < send.IndexOf("await JoinGate.EnterHost(command)", StringComparison.Ordinal) && dropped > run);
        Assert.Contains("GameTimeStatus.Text = HostCommandWaits;", send);
        Assert.Contains("if (_hostCommandWaiting)", SourceFiles.Method(host2, "private async Task SendServerCommand("));
    }

    [Fact]
    public void Start_turns_the_whole_AD1259_set_back_on()
    {
        // The 10 mod start from Coodsz-Crash-Reports (Fraus): the map picker had turned the pack mods off.
        var rows = AD1259Stack.ServerModules
            .Select(id => new ModuleRow { Id = id, Enabled = id is "Bannerlord.Harmony" or "Bannerlord.ButterLib" or "Bannerlord.UIExtenderEx"
                or "Bannerlord.MBOptionScreen" or "Native" or "DedicatedServer.Windows" or "SandBoxCore" or "Sandbox" or "Coop" or "AD1259" })
            .Append(new ModuleRow { Id = "SomeOtherMod", Enabled = false })
            .ToList();

        var turnedOn = AD1259Server.EnableLockedSet(rows);

        Assert.Contains("RBM", turnedOn);
        Assert.Contains(AD1259Stack.CompatModuleId, turnedOn);
        Assert.All(rows.Where(row => AD1259Stack.ServerModules.Contains(row.Id)), row => Assert.True(row.Enabled));
        Assert.False(rows.Single(row => row.Id == "SomeOtherMod").Enabled);
        Assert.Empty(AD1259Server.EnableLockedSet(rows));
        Assert.StartsWith("Turned these AD1259 mods back on", AD1259Server.TurnedOnLine(turnedOn));
    }

    [Fact]
    public void Server_check_names_every_missing_or_wrong_mod()
    {
        var rows = AD1259Stack.ServerModules
            .Where(id => id != "DedicatedServer.Windows")
            .Select(id => new ModuleRow
            {
                Id = id,
                Version = id == AD1259Stack.CompatModuleId ? AD1259Stack.CompatVersion : AD1259Stack.Find(id)!.Version
            })
            .ToList();
        Assert.Null(AD1259Server.Problem(rows));

        rows.RemoveAll(row => row.Id == "AD1259");
        rows[rows.FindIndex(row => row.Id == "RBM")] = new ModuleRow { Id = "RBM", Version = "v4.5.3" };
        var problem = AD1259Server.Problem(rows);
        Assert.NotNull(problem);
        Assert.Contains("Anno Domini 1259 v1.4.6 (missing; get it from Nexus Mods)", problem);
        Assert.Contains("Realistic Battle Mod v4.5.4 (the server has v4.5.3)", problem);
    }

    [Fact]
    public void Server_hides_raw_assets_installs_the_compat_module_and_refreshes_a_stale_nexus_mod()
    {
        var root = Temp();
        try
        {
            var server = Path.Combine(root, "DedicatedServer");
            var serverModules = ModuleCatalog.ModulesRoot(server);
            Module(serverModules, "AD1259", "AD1259", "v1.4.5");
            Directory.CreateDirectory(Path.Combine(serverModules, "AD1259", "Assets"));
            Directory.CreateDirectory(Path.Combine(serverModules, "swadian armoury", "Assets"));

            var line = BannerlordCoopLauncher.Core.Modules.ServerRawAssets.Note(
                BannerlordCoopLauncher.Core.Modules.ServerRawAssets.Hide(serverModules, BannerlordCoopLauncher.Core.Modules.ServerRawAssets.Ids()));
            Assert.Contains("AD1259, swadian armoury", line);
            Assert.True(Directory.Exists(Path.Combine(serverModules, "AD1259", AD1259Server.HiddenAssets)));
            Assert.False(Directory.Exists(Path.Combine(serverModules, "swadian armoury", "Assets")));
            Assert.Empty(BannerlordCoopLauncher.Core.Modules.ServerRawAssets.Hide(serverModules, BannerlordCoopLauncher.Core.Modules.ServerRawAssets.Ids()));

            var payload = Path.Combine(root, "payload");
            Module(Path.GetDirectoryName(payload)!, "payload", AD1259Stack.CompatModuleId, AD1259Stack.CompatVersion);
            AD1259Server.InstallCompat(server, payload);
            Assert.True(File.Exists(Path.Combine(serverModules, AD1259Stack.CompatModuleId, "SubModule.xml")));
            Assert.Throws<InvalidOperationException>(() => AD1259Server.InstallCompat(server, Path.Combine(root, "nothing")));

            var game = Path.Combine(root, "Modules");
            Module(game, "AD1259", "AD1259", "v1.4.6");
            var rows = new List<ModuleRow> { new() { Id = "AD1259", Version = "v1.4.5" } };
            var refreshed = AD1259Server.RefreshWrongVersions(server, rows, game, []);
            // The line names the source folder, so reports show where a copy came from (CR-JTQ45).
            Assert.Equal(new[] { "Anno Domini 1259 v1.4.6 from " + Path.Combine(game, "AD1259") }, refreshed);
            Assert.Contains("v1.4.6", File.ReadAllText(Path.Combine(serverModules, "AD1259", "SubModule.xml")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    // The Workshop MCM updated itself to 5.13.1 on 2026-11-04, and the client said it was newer than the tools support.
    // 5.13.1 and 5.12.3 both count now, and the copy in the game's Modules folder is the one the game loads.
    [Fact]
    public void Mcm_5_13_1_and_5_12_3_both_count_and_the_modules_copy_is_judged()
    {
        var mcm = AD1259Stack.Find("Bannerlord.MBOptionScreen")!;
        AD1259ModStatus Status(params (string Folder, string Version)[] copies) => AD1259ModScan.Status(mcm,
            new Dictionary<string, List<(string Folder, string Directory, string Version)>>(StringComparer.OrdinalIgnoreCase)
            {
                [mcm.Id] = copies.Select(copy => (copy.Folder, @"C:\x\" + copy.Folder, copy.Version)).ToList()
            });

        Assert.Equal(AD1259ModState.Ready, Status(("Workshop 2859238197", "v5.13.1")).State);
        Assert.Equal(AD1259ModState.Ready, Status(("Modules\\Bannerlord.MBOptionScreen", "v5.12.3")).State);
        var newer = Status(("Workshop 2859238197", "v5.13.2"));
        Assert.Equal(AD1259ModState.WrongVersion, newer.State);
        Assert.Equal("Newer than this tool supports (found v5.13.2, needs v5.13.1) \u00b7 Nexus Mods. Update the tool first.", newer.RowDetail);
        var older = Status(("Workshop 2859238197", "v5.12.2"));
        Assert.Equal(AD1259ModState.WrongVersion, older.State);
        Assert.Equal("Mod Configuration Menu v5 is v5.12.2. The server needs v5.13.1.", older.Problem);

        // Nexus 5.12.3 in Modules next to the Workshop's 5.13.1 is one copy, the Modules one.
        var both = Status(("Modules\\Bannerlord.MBOptionScreen", "v5.12.3"), ("Workshop 2859238197", "v5.13.1"));
        Assert.Equal(AD1259ModState.Ready, both.State);
        Assert.Equal("v5.12.3", both.FoundVersion);
        Assert.Equal(["Modules\\Bannerlord.MBOptionScreen"], both.Folders);
        Assert.Equal(AD1259ModState.WrongVersion, Status(("Modules\\Bannerlord.MBOptionScreen", "v5.12.2"), ("Workshop 2859238197", "v5.13.1")).State);
        Assert.Equal(AD1259ModState.Duplicate, Status(("Modules\\Bannerlord.MBOptionScreen", "v5.12.3"), ("Modules\\MCM old", "v5.13.1")).State);
    }

    // Coop refuses a join when the server's MCM is another version than the players', so the server gets the accepted MCM
    // the host's game loads: a Workshop download the Coop and Friends launcher turned off when that is all this PC has, and
    // the game's Modules copy over any Workshop one.
    [Fact]
    public void The_ad1259_server_gets_the_accepted_mcm_the_hosts_game_loads()
    {
        const string Mcm = "Bannerlord.MBOptionScreen";
        var root = Temp();
        try
        {
            var server = Path.Combine(root, "DedicatedServer");
            var serverModules = ModuleCatalog.ModulesRoot(server);
            var serverMcm = Path.Combine(serverModules, Mcm);
            Module(serverModules, Mcm, Mcm, "v5.11.4");
            var game = Path.Combine(root, "Modules");
            var workshop = Path.Combine(root, "workshop");
            var workshopMcm = Path.Combine(workshop, "2859238197");
            Module(workshop, "2859238197", Mcm, "v5.12.3");
            File.Move(Path.Combine(workshopMcm, "SubModule.xml"), Path.Combine(workshopMcm, WorkshopDuplicates.ParkedFile));
            List<ModuleRow> Rows(string version) => [new() { Id = Mcm, Version = version }];

            Assert.Equal(new[] { "Mod Configuration Menu v5 v5.12.3 from " + workshopMcm }, AD1259Server.RefreshWrongVersions(server, Rows("v5.11.4"), game, [workshop]));
            Assert.Contains("v5.12.3", File.ReadAllText(Path.Combine(serverMcm, "SubModule.xml")));
            Assert.False(File.Exists(Path.Combine(serverMcm, WorkshopDuplicates.ParkedFile)));
            // The player's parked download stays as the launcher left it.
            Assert.True(File.Exists(Path.Combine(workshopMcm, WorkshopDuplicates.ParkedFile)));

            // Host 5.13.1 in Modules, server 5.12.3: the server gets 5.13.1. Once they match nothing is copied.
            Module(game, Mcm, Mcm, "v5.13.1");
            Assert.Equal(new[] { "Mod Configuration Menu v5 v5.13.1 from " + Path.Combine(game, Mcm) }, AD1259Server.RefreshWrongVersions(server, Rows("v5.12.3"), game, [workshop]));
            Assert.Contains("v5.13.1", File.ReadAllText(Path.Combine(serverMcm, "SubModule.xml")));
            Assert.Empty(AD1259Server.RefreshWrongVersions(server, Rows("v5.13.1"), game, [workshop]));

            // Host 5.12.3 in Modules next to the Workshop's 5.13.1, server 5.13.1: the server gets 5.12.3.
            Module(game, Mcm, Mcm, "v5.12.3");
            Directory.Delete(workshopMcm, recursive: true);
            Module(workshop, "2859238197", Mcm, "v5.13.1");
            Assert.Equal(new[] { "Mod Configuration Menu v5 v5.12.3 from " + Path.Combine(game, Mcm) }, AD1259Server.RefreshWrongVersions(server, Rows("v5.13.1"), game, [workshop]));
            Assert.Contains("v5.12.3", File.ReadAllText(Path.Combine(serverMcm, "SubModule.xml")));
            Assert.Empty(AD1259Server.RefreshWrongVersions(server, Rows("v5.12.3"), game, [workshop]));

            // A host with only the Workshop's 5.13.1 (client bin only) still replaces a Nexus server copy with a server bin.
            Directory.Delete(Path.Combine(game, Mcm), recursive: true);
            Directory.CreateDirectory(Path.Combine(serverMcm, "bin", "Win64_Shipping_Server"));
            File.WriteAllText(Path.Combine(serverMcm, "bin", "Win64_Shipping_Server", "MCMv5.dll"), "5.12.3");
            Directory.CreateDirectory(Path.Combine(workshopMcm, "bin", "Win64_Shipping_Client"));
            File.WriteAllText(Path.Combine(workshopMcm, "bin", "Win64_Shipping_Client", "MCMv5.dll"), "5.13.1");
            Assert.Equal(new[] { "Mod Configuration Menu v5 v5.13.1 from " + workshopMcm }, AD1259Server.RefreshWrongVersions(server, Rows("v5.12.3"), game, [workshop]));
            Assert.Contains("v5.13.1", File.ReadAllText(Path.Combine(serverMcm, "SubModule.xml")));
            Assert.False(Directory.Exists(Path.Combine(serverMcm, "bin", "Win64_Shipping_Server")));

            // When the copy fails part way, the server keeps its old MCM instead of none (pr-reviewer, Codex).
            Module(serverModules, Mcm, Mcm, "v5.12.3");
            Directory.CreateDirectory(Path.Combine(serverMcm, "bin", "Win64_Shipping_Server"));
            File.WriteAllText(Path.Combine(serverMcm, "bin", "Win64_Shipping_Server", "MCMv5.dll"), "5.12.3");
            using (new FileStream(Path.Combine(workshopMcm, "bin", "Win64_Shipping_Client", "MCMv5.dll"), FileMode.Open, FileAccess.Read, FileShare.None))
                Assert.ThrowsAny<IOException>(() => AD1259Server.RefreshWrongVersions(server, Rows("v5.12.3"), game, [workshop]));
            Assert.Contains("v5.12.3", File.ReadAllText(Path.Combine(serverMcm, "SubModule.xml")));
            Assert.Equal("5.12.3", File.ReadAllText(Path.Combine(serverMcm, "bin", "Win64_Shipping_Server", "MCMv5.dll")));

            // Start's Workshop refresh leaves MCM to the step above and keeps every other pinned mod.
            Assert.Equal(new[] { "Bannerlord.ButterLib", "RBM" }, AD1259Server.RefreshIds(ad1259: true, [Mcm, "Bannerlord.ButterLib", "RBM"]));
            Assert.Equal(new[] { Mcm, "RBM" }, AD1259Server.RefreshIds(ad1259: false, [Mcm, "RBM"]));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void The_ad1259_server_tool_ships_the_server_tools_module_and_the_set_checks_never_count_it()
    {
        // The host rules and the MCM settings sync live in Coodsz Server Tools, so the AD1259 exe carries it and the
        // release checks and signs it like the Coop Server Tool's.
        var root = RepoRoot();
        Assert.Contains("<CoodszPayload Include=\"$(HostSource)ServerTools\\**\\*\" Payload=\"ServerTools\" />",
            File.ReadAllText(Path.Combine(root, "src", "AD1259Host", "AD1259Host.csproj")));
        Assert.Contains("Assert-Payload $server \"$payloads\\server\" 'ServerTools' @(, @('', 'src\\CoodszHost\\ServerTools'))",
            File.ReadAllText(Path.Combine(root, "tools", "Make-AD1259-Release.ps1")));
        using (var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "tools", "release-assets.json"))))
        {
            var ad1259 = json.RootElement.GetProperty("repos").EnumerateArray().Single(repo => repo.GetProperty("repo").GetString() == "Coodsz/Coodsz-AD1259");
            // The published v1.1.16 never carried the ServerTools payload, so both DLLs are asked of v1.1.17 on.
            var gated = ad1259.GetProperty("moduleDlls").EnumerateArray().Where(dll => dll.ValueKind == System.Text.Json.JsonValueKind.Object)
                .ToDictionary(dll => dll.GetProperty("path").GetString()!, dll => dll.GetProperty("fromVersion").GetString());
            Assert.Equal("v1.1.17", gated["src/CoodszHost/ServerTools/DedicatedServer.CoodszTools/bin/Win64_Shipping_Server/DedicatedServer.CoodszTools.dll"]);
            Assert.Equal("v1.1.17", gated["src/CoodszHost/ServerTools/DedicatedServer.CoodszTools/bin/Win64_Shipping_Server/CoodszSettingsSync.Coop.dll"]);
        }

        // The players already get the sync client inside the join password module the Client Launcher adds on Connect.
        Assert.True(File.Exists(Path.Combine(root, "compat", "CoodszJoinPassword", "module", "bin", "Win64_Shipping_Client", "CoodszSettingsSync.Coop.dll")));
        Assert.Contains(BannerlordCoopLauncher.Core.Launch.ClientAddOns.JoinPasswordId, AD1259Stack.LaunchModules(withClientFix: true, withJoinPassword: true));

        // A start lists the module after the set; the set check, the save check and the join check all leave it out.
        var started = CoodszServerTools.WithTools(CompleteServerRows());
        Assert.Equal(CoodszServerTools.ModuleId, started[^1].Id);
        Assert.Null(AD1259Server.Problem(started));
        var save = AD1259Stack.ServerModules.ToList();
        Assert.Equal(SaveModAction.Start, SaveModChange.Check(save, started.Select(row => row.Id)).Action);
        Assert.True(CoopJoinRules.IsIgnored(CoodszServerTools.ModuleId));
        Assert.Null(AD1259Server.NightlyServer(started, null, null, false));

        // The Start line says what the module is for in each build.
        Assert.Contains("for the host rules and the MCM settings players get", CoodszServerTools.AddedText(ad1259: true));
        Assert.Contains("the Cheats page and player profiles", CoodszServerTools.AddedText(ad1259: false));
    }

    [Fact]
    public void A_fresh_ad1259_host_shows_every_rules_card_with_clan_cap_and_pvp_off()
    {
        // Both tools read the same coodsz-server-tools.json, so the AD1259 Settings page shows Max clans per kingdom and the
        // PvP rules too. With no file yet (a fresh host) both are off; pause on join and the XP freeze are on.
        var missing = Path.Combine(Temp(), ClanCapFile.FileName);
        try
        {
            Assert.False(File.Exists(missing));
            Assert.Equal(ClanCap.Off, ClanCapFile.Read(missing));
            Assert.Equal(0, ClanCapFile.Read(missing).MaxClans);
            var pvp = PvpRulesFile.Read(missing);
            Assert.False(pvp.BlockLeave);
            Assert.False(pvp.ResponseTimer);
            var rules = ServerRulesFile.Read(missing);
            Assert.True(rules.PauseOnJoin);
            Assert.True(rules.FreezeOfflineXp);
            Assert.Null(rules.KingdomProtection);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(missing)!, recursive: true);
        }

        // The three cards show in both builds: their Start methods never return early or ask for the edition.
        var window = SourceFiles.Window(RepoRoot(), "CoodszHost");
        foreach (var (method, card) in new[] { ("private void StartClanCap()", "ClanCapGroup"), ("private void StartPvpRules()", "PvpRulesGroup"), ("private void StartServerRules()", "ServerRulesArea") })
        {
            var body = SourceFiles.Method(window, method);
            Assert.Contains(card + ".Visibility = Visibility.Visible;", body);
            Assert.DoesNotContain("return;", body);
            Assert.DoesNotContain("Edition.", body);
        }
    }

    [Fact]
    public void Each_ad1259_tool_picks_its_own_release_zip()
    {
        var assets = new[] { "Coodsz-AD1259-Server-v1.0.0.zip", "Coodsz-AD1259-Client-Launcher-v1.0.0.zip", "SHA256SUMS.txt" };
        Assert.Equal("Coodsz-AD1259-Server-v1.0.0.zip", ToolUpdate.PickZip("Coodsz AD1259 Server.exe", assets));
        Assert.Equal("Coodsz-AD1259-Client-Launcher-v1.0.0.zip", ToolUpdate.PickZip("Coodsz AD1259 Client Launcher.exe", assets));
        Assert.True(ToolUpdate.IsKeptUserFile("ad1259-launcher.json"));
    }

    [Fact]
    public void Module_list_with_spaces_stays_one_argument()
    {
        var arguments = TaomLaunch.Arguments(connect: true, steamLobby: false, password: null, directEndpoint: "1.2.3.4:4200",
            modules: AD1259Stack.ClientModules);
        Assert.EndsWith("\"_MODULES_*Bannerlord.Harmony*" + string.Join("*", AD1259Stack.ClientModules.Skip(1)) + "*_MODULES_\"", arguments);
        Assert.DoesNotContain("\"", TaomLaunch.Arguments(connect: false, steamLobby: false, password: null));
    }

    [Fact]
    public void Loading_watch_says_still_working_while_the_game_uses_cpu_or_writes_logs()
    {
        var watch = new LoadingWatch();
        var first = watch.Update(new LoadingSample(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(1), 100, 0), "");
        Assert.False(first.Stalled);
        Assert.StartsWith("Bannerlord is still loading · 0:02", first.Title);
        Assert.Contains("builds shaders", first.Detail);

        // Window frozen on shaders: no log writes, but the CPU is busy. Still working.
        var shaders = watch.Update(new LoadingSample(TimeSpan.FromMinutes(6), TimeSpan.FromMinutes(5), 100, 0), "");
        Assert.False(shaders.Stalled);
        Assert.Contains("using the CPU", shaders.Detail);

        // Nothing at all for three minutes: stalled.
        var stuck = watch.Update(new LoadingSample(TimeSpan.FromMinutes(9), TimeSpan.FromMinutes(5), 100, 0), "");
        Assert.True(stuck.Stalled);
        Assert.Contains("3 minutes", stuck.Title);

        // The log moves again: back to working, and the stage follows the Coop log.
        var download = watch.Update(new LoadingSample(TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(5), 100, 500),
            "Connected! Please wait for transfer\nReceiving host save transfer 1: 415 chunks");
        Assert.False(download.Stalled);
        Assert.StartsWith("Downloading the server's world", download.Detail);
        Assert.Contains("writing its log now", download.Detail);
    }

    [Fact]
    public void Loading_ends_at_character_creation_or_the_map_not_at_connect()
    {
        Assert.False(LoadingWatch.InGame("Connected! Please wait for transfer"));
        Assert.True(LoadingWatch.InGame("Game State is changing to MapState"));
        Assert.True(LoadingWatch.InGame("Game State is changing to CharacterCreationState"));
        // Coop's own log can stay empty; the engine log's screen names still end the wait.
        Assert.True(LoadingWatch.InGame(LoadingWatch.ScreenLines("[00:43:22.513] TopScreen: MapScreen \r\n[00:43:22.6] other")));
        Assert.Equal("Connecting to the server and receiving its world", LoadingWatch.Stage("TopScreen: CoopConnectionUI"));
        Assert.Equal("Loading the server's world", LoadingWatch.Stage("Received host save transfer 1\nGame State is changing to GameLoadingState"));
        Assert.Equal("Connected. Waiting for the server to send its world", LoadingWatch.Stage("Connected! Please wait for transfer"));
    }

    [Fact]
    public void Old_mod_shader_caches_are_found_from_the_engine_log_and_the_known_list()
    {
        var root = Temp();
        try
        {
            var logs = Path.Combine(root, "logs");
            Directory.CreateDirectory(logs);
            File.WriteAllText(Path.Combine(logs, "rgl_log_1.txt"),
                "rgl_post_warning_line: Shader cache version of the external module (Templar Armor) is invalid. \n");
            File.WriteAllText(Path.Combine(logs, "rgl_log_errors_1.txt"),
                "Shader cache version of the external module (Native) is invalid.");
            Assert.Equal(new[] { "Templar Armor" }, AD1259ShaderCaches.Reported(logs));
            Assert.Empty(AD1259ShaderCaches.Reported(Path.Combine(root, "missing")));

            var modules = Path.Combine(root, "Modules");
            var workshop = Path.Combine(root, "workshop");
            Module(modules, "Templar Armor", "Templar Armor", "v1.0.3");
            Module(workshop, "3010984416", "OpenSourceWeaponry", "v2.0.1");
            Module(workshop, "2859251492", "RBM", "v4.5.4");
            var statuses = AD1259ModScan.Check(modules, [workshop]);

            var aside = AD1259ShaderCaches.ToSetAside(statuses, AD1259ShaderCaches.Reported(logs));
            Assert.Equal(new[] { "Open Source Weaponry", "Templar Armor" }, aside.Select(item => item.Name).OrderBy(name => name));
            Assert.DoesNotContain(aside, item => item.Name == "Realistic Battle Mod");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Client_fix_is_copied_into_modules_and_loaded_after_coop()
    {
        var root = Temp();
        try
        {
            var payload = Path.Combine(root, "payload");
            Module(root, "payload", AD1259Stack.ClientFixModuleId, AD1259Stack.ClientFixVersion);
            Directory.CreateDirectory(Path.Combine(payload, "bin", "Win64_Shipping_Client"));
            File.WriteAllText(Path.Combine(payload, "bin", "Win64_Shipping_Client", "AD1259CoopClientFix.dll"), "dll");
            var game = Path.Combine(root, "game");

            AD1259ClientFix.Install(game, payload);
            AD1259ClientFix.Install(game, payload);

            var installed = Path.Combine(game, "Modules", AD1259Stack.ClientFixModuleId);
            Assert.True(File.Exists(Path.Combine(installed, "SubModule.xml")));
            Assert.True(File.Exists(Path.Combine(installed, "bin", "Win64_Shipping_Client", "AD1259CoopClientFix.dll")));
            Assert.Throws<InvalidOperationException>(() => AD1259ClientFix.Install(game, Path.Combine(root, "nothing")));

            var launch = AD1259Stack.LaunchModules(withClientFix: true);
            Assert.Equal("Coop", launch[^2]);
            Assert.Equal(AD1259Stack.ClientFixModuleId, launch[^1]);
            Assert.Equal(AD1259Stack.ClientModules, AD1259Stack.LaunchModules(withClientFix: false));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Bundled_client_fix_matches_the_constants()
    {
        var module = Path.Combine(RepoRoot(), "compat", "AD1259CoopClientFix", "module");
        var xml = File.ReadAllText(Path.Combine(module, "SubModule.xml"));
        Assert.Contains("<Id value=\"" + AD1259Stack.ClientFixModuleId + "\"", xml);
        Assert.Contains("<Version value=\"" + AD1259Stack.ClientFixVersion + "\"", xml);
        Assert.True(File.Exists(Path.Combine(module, "bin", "Win64_Shipping_Client", "AD1259CoopClientFix.dll")));
    }

    [Fact]
    public void Join_password_module_loads_last_and_matches_its_constants()
    {
        var launch = AD1259Stack.LaunchModules(withClientFix: true, withJoinPassword: true);
        Assert.Equal(BannerlordCoopLauncher.Core.Launch.ClientAddOns.JoinPasswordId, launch[^1]);
        Assert.Equal(AD1259Stack.ClientFixModuleId, launch[^2]);

        // TAOM keeps Coop directly followed by TAOM.CoopCompat; the module goes after it.
        var taom = TaomStack.ClientModules.Append(BannerlordCoopLauncher.Core.Launch.ClientAddOns.JoinPasswordId).ToList();
        Assert.True(TaomStack.CoopIsAdjacent(taom));

        var module = Path.Combine(RepoRoot(), "compat", "CoodszJoinPassword", "module");
        var xml = File.ReadAllText(Path.Combine(module, "SubModule.xml"));
        Assert.Contains("<Id value=\"" + BannerlordCoopLauncher.Core.Launch.ClientAddOns.JoinPasswordId + "\"", xml);
        // No Bannerlord.Harmony dependency: TAOM's list does not load that module.
        Assert.DoesNotContain("Bannerlord.Harmony", xml);
        Assert.True(File.Exists(Path.Combine(module, "bin", "Win64_Shipping_Client", "CoodszJoinPassword.dll")));
        var source = File.ReadAllText(Path.Combine(RepoRoot(), "compat", "CoodszJoinPassword", "src", "JoinPassword.cs"));
        Assert.Contains("\"" + BannerlordCoopLauncher.Core.Launch.CoopPasswordPatch.VariableName + "\"", source);
    }

    [Fact]
    public void The_AD1259_community_list_seed_is_the_pack_players_rate()
    {
        // ad1259-compat/compat/ad1259-mod-compat.json is copied to Coodsz-AD1259, where its tally writes MODS.md.
        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoRoot(), "ad1259-compat", "compat", "ad1259-mod-compat.json")));
        var root = doc.RootElement;
        Assert.Equal("CoodszAd1259Compat", root.GetProperty("kind").GetString());
        Assert.Equal(AD1259Stack.AD1259Version, root.GetProperty("ad1259").GetString());
        Assert.Equal(AD1259Stack.GameVersion, root.GetProperty("game").GetString());
        var rated = AD1259Stack.ClientMods.Where(mod => BannerlordCoopLauncher.Core.Modules.ModReports.Rates(mod.Id, mod.BaseGame)).ToList();
        var seed = root.GetProperty("mods").EnumerateArray().ToList();
        Assert.Equal(rated.Select(mod => mod.Id), seed.Select(entry => entry.GetProperty("id").GetString()));
        foreach (var (mod, entry) in rated.Zip(seed))
        {
            Assert.Equal(mod.Name, entry.GetProperty("name").GetString());
            Assert.Equal(mod.Version, entry.GetProperty("version").GetString());
            Assert.Equal(mod.WorkshopId, entry.TryGetProperty("workshopId", out var workshop) ? workshop.GetString() : null);
            Assert.Equal(mod.NexusId?.ToString(), entry.TryGetProperty("nexusId", out var nexus) ? nexus.GetString() : null);
            Assert.Equal("pack", entry.GetProperty("tag").GetString());
        }
        Assert.Equal("https://github.com/Coodsz/Coodsz-AD1259/blob/main/MODS.md", AD1259Stack.ModsPageUrl);
    }

    internal static List<ModuleRow> CompleteServerRows() => AD1259Stack.ServerModules
        .Where(id => id != "DedicatedServer.Windows")
        .Select(id => new ModuleRow
        {
            Id = id,
            Version = id == AD1259Stack.CompatModuleId ? AD1259Stack.CompatVersion : AD1259Stack.Find(id)!.Version
        })
        .ToList();

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "BannerlordCoopLauncher.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    private static string Temp()
    {
        var root = Path.Combine(Path.GetTempPath(), "ad1259-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void Module(string root, string folder, string id, string version)
    {
        var directory = Path.Combine(root, folder);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "SubModule.xml"),
            "<Module><Id value=\"" + id + "\"/><Version value=\"" + version + "\"/></Module>");
    }
}

// Switches the shared translator language, so it runs apart from the other language tests.
[Collection(TranslatorLanguage.Name)]
public sealed class AD1259ServerLanguageTests
{
    // AD1259 crash report #8: the refusal named the pinned versions and the server's, so Tatache read the
    // pinned ones as his own and asked whether to go back to the server's. It now says what the PC has.
    [Fact]
    public void The_refusal_says_what_this_pc_has()
    {
        var before = BannerlordCoopLauncher.Core.Localization.Translator.Current;
        var root = Path.Combine(Path.GetTempPath(), "ad1259-pc-" + Guid.NewGuid().ToString("N"));
        try
        {
            BannerlordCoopLauncher.Core.Localization.Translator.Use("en");
            var workshop = Path.Combine(root, "workshop");
            var game = Path.Combine(root, "Modules");
            Write(workshop, "3011479883", "OpenSourceArmory", "v1.17.0");
            Write(game, "OpenSourceArmory", "OpenSourceArmory", "v1.19.0");
            Write(workshop, "2859251492", "RBM", "v4.5.4");
            var pc = AD1259Server.PcVersions(game, [workshop]);
            Assert.Equal(new[] { "v1.19.0", "v1.17.0" }, pc["OpenSourceArmory"]);

            var rows = AD1259ToolsTests.CompleteServerRows();
            rows[rows.FindIndex(row => row.Id == "OpenSourceArmory")] = new ModuleRow { Id = "OpenSourceArmory", Version = "v1.17.0" };
            rows[rows.FindIndex(row => row.Id == "OpenSourceSaddlery")] = new ModuleRow { Id = "OpenSourceSaddlery", Version = "v1.7.0" };
            Assert.Equal("The AD1259 set is not complete on this PC: "
                + "Open Source Armory needs v2.0.0 (the server has v1.17.0, this PC has v1.19.0, v1.17.0); "
                + "Open Source Saddlery needs v2.0.0 (the server has v1.7.0, not on this PC). "
                + "Install those versions where the game finds them (Workshop or the game's Modules folder), then Start again. " + OpenSourceSet.OlderToo,
                AD1259Server.Problem(rows, pc));
            // Without the PC's versions the old text stays.
            Assert.Contains("Open Source Armory v2.0.0 (the server has v1.17.0)", AD1259Server.Problem(rows));
            Assert.Null(AD1259Server.Problem(AD1259ToolsTests.CompleteServerRows(), pc));
        }
        finally
        {
            BannerlordCoopLauncher.Core.Localization.Translator.Use(before);
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void The_map_refusal_names_the_map_twice()
    {
        var before = BannerlordCoopLauncher.Core.Localization.Translator.Current;
        try
        {
            BannerlordCoopLauncher.Core.Localization.Translator.Use("en");
            Assert.Equal("The server was not started. This campaign was made on another map, without Anno Domini 1259. A map mod can't be added to a campaign "
                + "that already started, the save would not load. Make a new campaign save to play on Anno Domini 1259.", SaveModChange.MapRefuseText("Anno Domini 1259"));
            BannerlordCoopLauncher.Core.Localization.Translator.Use("fr");
            // English for the logs; the window shows it through the translator.
            var french = BannerlordCoopLauncher.Core.Localization.Translator.T(SaveModChange.MapRefuseText("Anno Domini 1259"));
            Assert.DoesNotContain("another map", french);
            Assert.Equal(2, french.Split("Anno Domini 1259").Length - 1);
        }
        finally
        {
            BannerlordCoopLauncher.Core.Localization.Translator.Use(before);
        }
    }

    private static void Write(string root, string folder, string id, string version)
    {
        var directory = Path.Combine(root, folder);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "SubModule.xml"),
            "<Module><Id value=\"" + id + "\"/><Version value=\"" + version + "\"/></Module>");
    }

    private static string Problem(params (string Id, string Version)[] changes)
    {
        var rows = AD1259ToolsTests.CompleteServerRows();
        foreach (var (id, version) in changes)
            rows[rows.FindIndex(row => row.Id == id)] = new ModuleRow { Id = id, Version = version };
        return AD1259Server.Problem(rows)!;
    }

    [Fact]
    public void Newer_server_versions_get_newer_advice_and_old_versions_keep_the_existing_text()
    {
        var before = BannerlordCoopLauncher.Core.Localization.Translator.Current;
        try
        {
            BannerlordCoopLauncher.Core.Localization.Translator.Use("en");
            const string advice = " Update this tool first. Install update shows at the top when there is one, or get the newest release."
                + " Until then, install the versions above where the game finds them (Workshop or the game's Modules folder), then Start again.";
            Assert.Equal("The AD1259 set is not complete on this PC: Anno Domini 1259 v1.4.6 (the server has v1.4.7, which is newer than this tool supports)."
                + advice, Problem(("AD1259", "v1.4.7")));
            Assert.Equal("The AD1259 set is not complete on this PC: Realistic Battle Mod v4.5.4 (the server has v4.5.3); "
                + "Anno Domini 1259 v1.4.6 (the server has v1.4.7, which is newer than this tool supports)." + advice, Problem(("AD1259", "v1.4.7"), ("RBM", "v4.5.3")));

            var older = Problem(("RBM", "v4.5.3"));
            Assert.Equal("The AD1259 set is not complete on this PC: Realistic Battle Mod v4.5.4 (the server has v4.5.3). "
                + "Install those versions where the game finds them (Workshop or the game's Modules folder), then Start again.", older);
        }
        finally
        {
            BannerlordCoopLauncher.Core.Localization.Translator.Use(before);
        }
    }

    // Every fixed sentence of the refusal is translated, for the newer only, mixed and older only cases.
    [Theory]
    [InlineData("de")]
    [InlineData("ar")]
    [InlineData("fr")]
    [InlineData("pt")]
    [InlineData("ru")]
    [InlineData("tr")]
    public void The_refusal_is_translated_in_every_case(string language)
    {
        var before = BannerlordCoopLauncher.Core.Localization.Translator.Current;
        try
        {
            var english = new[] { "not complete", "newer than", "the server has", "Update this tool", "Install update", "Until then", "Install those", "Start again", "needs", "this PC has", "not on this PC" };
            BannerlordCoopLauncher.Core.Localization.Translator.Use(language);
            // The PC's versions as Start passes them: one copy that is old too, and a mod not on the PC.
            var rows = AD1259ToolsTests.CompleteServerRows();
            rows[rows.FindIndex(row => row.Id == "RBM")] = new ModuleRow { Id = "RBM", Version = "v4.5.3" };
            rows[rows.FindIndex(row => row.Id == "OpenSourceArmory")] = new ModuleRow { Id = "OpenSourceArmory", Version = "v1.18.0" };
            var pc = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase) { ["RBM"] = new[] { "v4.5.3" } };
            var withPc = AD1259Server.Problem(rows, pc)!;
            // The window shows the refusal through the translator, like every other message.
            foreach (var text in new[] { Problem(("AD1259", "v1.4.7")), Problem(("AD1259", "v1.4.7"), ("RBM", "v4.5.3")), Problem(("RBM", "v4.5.3")), withPc })
            {
                var shown = BannerlordCoopLauncher.Core.Localization.Translator.T(text);
                foreach (var phrase in english)
                    Assert.DoesNotContain(phrase, shown);
            }
        }
        finally
        {
            BannerlordCoopLauncher.Core.Localization.Translator.Use(before);
        }
    }
}
