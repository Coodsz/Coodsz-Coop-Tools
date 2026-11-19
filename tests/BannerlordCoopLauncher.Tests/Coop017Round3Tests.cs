using BurningEmpires.Core;
using CoodszHost.Services;
using static BurningEmpires.Core.Tests.BeServerFixtures;

namespace BannerlordCoopLauncher.Tests;

// Round 3 of the Coop 0.1.7 review (Claude and Devin): Prepare server never quietly puts an older Workshop Coop
// over a newer server Coop, and the Choose Coop server status names the server Prepare really copies.
public sealed class Coop017Round3Tests
{
    private static string Host() => SourceFiles.Window(SourceFiles.Root(), "BurningEmpiresHost");

    private static string ManualServer(string root, string folder, string coopVersion)
    {
        var workshop = WorkshopServer(Path.Combine(root, folder));
        var server = Path.Combine(root, folder, "CoopServer");
        Directory.Move(workshop, server);
        Module(Path.Combine(server, "engine", "Modules"), "Coop", "Coop", coopVersion);
        return server;
    }

    // v1.3.6 live test: CoopModPatch's first time setup stopped a new campaign's first run before the save was
    // written, and the next Start refused with "That save is not in the dedicated server Game Saves folder".
    [Fact]
    public void A_new_campaign_stays_new_when_CoopModPatch_setup_stops_the_first_run()
    {
        var host = SourceFiles.Window(SourceFiles.Root(), "CoodszHost");
        var stop = SourceFiles.Method(host, "private bool IsModPatchSetupStop(ServerExitInfo exit)");
        Assert.Contains("KeepNewCampaignIfNotSaved();", stop);
        var keep = SourceFiles.Method(host, "private void KeepNewCampaignIfNotSaved()");
        Assert.Contains("_runCreatesCampaign", keep);
        Assert.Contains("File.Exists(Path.Combine(ServerSession.SavesPath, _launchedSave + \".sav\"))", keep);
        Assert.Contains("profile.NewCampaign = true;", keep);
        Assert.Contains("HostSettings.Save(SettingsPath, _settings);", keep);
        // Any other stop nobody asked for before the first save keeps it new too.
        var exited = SourceFiles.Method(host, "private void OnServerExited(ServerExitInfo exit)");
        Assert.Contains("if (!info.WasExpected)", exited);
        Assert.Contains("KeepNewCampaignIfNotSaved();", exited);
    }

    [Fact]
    public void Prepare_from_an_older_workshop_coop_stops_on_start_and_asks_on_a_click()
    {
        var root = NewRoot("be-prepare-downgrade");
        try
        {
            var workshop = WorkshopServer(root);
            var server = ManualServer(root, "server", "v0.1.7");
            Assert.True(BeServerPrepare.ServerIsNewerThan(server, workshop));
            Assert.Equal("This puts Coop v0.1.7 back to v0.1.6. Continue?", BeServerPrepare.DowngradeQuestion(server, workshop));
            var stop = BeServerPrepare.AutoPrepareKeptNewer(server, workshop);
            Assert.StartsWith("The server runs Coop v0.1.7, which is newer than the Steam Workshop Coop v0.1.6, so Start server did not copy", stop);
            Assert.Contains(BeServerPrepare.PickButton, stop);
        }
        finally
        {
            Delete(root);
        }

        var prepare = SourceFiles.Method(Host(), "private async Task<bool> PrepareServerAsync(bool automatic = false)");
        var check = prepare.IndexOf("if (BeServerPrepare.ServerIsNewerThan(serverRoot, serverSource))", StringComparison.Ordinal);
        var copy = prepare.IndexOf("BeServerPrepare.Prepare(serverSource, serverRoot", StringComparison.Ordinal);
        Assert.True(check >= 0 && check < copy, "the newer Coop check runs before the copy");
        var guard = prepare[check..copy];
        // Start server's automatic prepare stops; a click asks first.
        Assert.True(guard.IndexOf("if (automatic)", StringComparison.Ordinal) < guard.IndexOf("BeServerPrepare.AutoPrepareKeptNewer(serverRoot, serverSource, picked)", StringComparison.Ordinal));
        Assert.Contains("return false;", guard);
        Assert.Contains("ConfirmWindow.Ask(this, BeServerPrepare.PrepareButton, BeServerPrepare.DowngradeQuestion(serverRoot, serverSource)", guard);
        Assert.Contains("if (!await PrepareServerAsync(automatic: true))", SourceFiles.Method(Host(), "private async Task StartServerCoreAsync("));
    }

    [Fact]
    public void Choose_coop_server_says_when_the_workshop_one_is_used_because_it_is_newer()
    {
        var root = NewRoot("be-choose-status");
        try
        {
            var workshop = WorkshopServer(root);
            Module(Path.Combine(workshop, "engine", "Modules"), "Coop", "Coop", "v0.1.8");
            var picked = ManualServer(root, "manual", "v0.1.7");
            Assert.Equal(new BeServerSource(workshop, Picked: false), BeServerPrepare.Source(picked, workshop));
            Assert.Equal("The Steam Workshop Coop server (Coop v0.1.8) is newer than the one in " + picked + ", so Prepare server copies the Workshop one.",
                BeServerPrepare.PickedButWorkshopNewer(picked, workshop));
        }
        finally
        {
            Delete(root);
        }

        var pick = SourceFiles.Method(Host(), "private void PickCoopServer_Click(");
        Assert.True(pick.IndexOf("_settings.CoopServerFolder = server;", StringComparison.Ordinal)
            < pick.IndexOf("BeServerPrepare.PickedButWorkshopNewer(server, workshopSource.Folder)", StringComparison.Ordinal));
    }

    [Fact]
    public void A_gone_pick_is_said_once_and_forgotten()
    {
        var forget = SourceFiles.Method(Host(), "private void ForgetGonePick(Action<string> note)");
        Assert.Contains("note(BeServerPrepare.PickedGoneNote);", forget);
        Assert.Contains("_settings.CoopServerFolder = \"\";", forget);
        Assert.Contains("SaveSettings();", forget);
    }

    [Fact]
    public void The_server_tool_text_after_the_flag_keeps_both_folders_find_looks_in()
    {
        var source = SourceFiles.Read(Path.Combine("src", "CoodszHost", "Services", "DedicatedServerFinder.cs"));
        const string after = "Bannerlord Coop's dedicated server was not found. Click Set server folder and choose BannerlordCoopServer.exe. It is in Modules\\Bannerlord Coop\\DedicatedServer, or in Workshop folder 3770450698\\DedicatedServer.";
        Assert.Contains(after.Replace("\\", "\\\\"), source);
        foreach (var language in new[] { "ar", "de", "fr", "pt", "ru", "tr" })
        {
            var table = BannerlordCoopLauncher.Core.Localization.Translator.Entries(language);
            Assert.True(table.ContainsKey(after), language);
            Assert.True(table.ContainsKey(DedicatedServerFinder.MissingMessage), language);
        }
    }
}
