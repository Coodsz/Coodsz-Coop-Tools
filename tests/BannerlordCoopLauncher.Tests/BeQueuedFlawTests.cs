using BannerlordCoopLauncher.Core.Modules;
using BannerlordCoopLauncher.Core.Parsing;
using BannerlordCoopLauncher.Core.Profile;
using BurningEmpires.Core;
using TaomCoop.Core;

namespace BannerlordCoopLauncher.Tests;

// The three Burning Empires flaws found by the Chaos in Calradia chat, and the Server Tool fixes from the TAOM v1.2.16
// review (BE v1.0.16).
public sealed class BeQueuedFlawTests
{
    // A setup with mods listed twice: one off first, then on with the id in another case, and one again with spaces.
    private static TaomSetup TwoCopies()
    {
        var setup = new TaomSetup { Name = "Mine" };
        setup.Extras.Add(new TaomExtraMod { Name = "Old copy", ModuleId = "Twice", Enabled = false });
        setup.Extras.Add(new TaomExtraMod { Name = "Kept", ModuleId = "Other" });
        setup.Extras.Add(new TaomExtraMod { Name = "New copy", ModuleId = "TWICE" });
        setup.Extras.Add(new TaomExtraMod { Name = "Spaced", ModuleId = " Other " });
        return setup;
    }

    [Fact]
    public void A_mod_listed_twice_keeps_the_copy_that_is_turned_on()
    {
        var setup = TwoCopies();
        Assert.Equal("New copy", BeLoadOrder.ExtrasById(setup)["Twice"].Name);
        Assert.Equal("Kept", BeLoadOrder.ExtrasById(setup)["Other"].Name);

        BeLoadOrder.Normalize(setup);
        Assert.Equal(new[] { "Kept", "New copy" }, setup.Extras.Select(mod => mod.Name));
    }

    [Fact]
    public void A_mod_listed_twice_no_longer_crashes_the_shared_list_the_mod_checks_auto_sort_or_the_crash_search()
    {
        var entries = BeSharedList.Entries(TwoCopies(), _ => null);
        Assert.Single(entries, entry => entry.Id.Trim().Equals("Twice", StringComparison.OrdinalIgnoreCase));

        BeModChecks.Run(TwoCopies(), new Dictionary<string, TaomModFact>(StringComparer.OrdinalIgnoreCase));

        var sorted = TwoCopies();
        BeAutoSort.Sort(sorted, new Dictionary<string, ModuleManifest>(StringComparer.OrdinalIgnoreCase));
        Assert.Single(sorted.Extras, mod => mod.ModuleId.Trim().Equals("Twice", StringComparison.OrdinalIgnoreCase));

        // Find the crashing mod names the mods through the same one copy per id.
        var crash = SourceFiles.Read(Path.Combine("src", "BurningEmpiresLauncher", "MainWindow.Crash.cs"));
        Assert.Contains("var labels = BeLoadOrder.ExtrasById(setup).ToDictionary(", crash);
        Assert.DoesNotContain("setup.Extras.ToDictionary(", crash);
    }

    [Fact]
    public void The_shared_list_reads_each_module_from_where_the_next_start_copies_it()
    {
        var root = Path.Combine(Path.GetTempPath(), "coodsz-be-startsource-" + Guid.NewGuid().ToString("N"));
        try
        {
            string Module(string folder)
            {
                Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder, "SubModule.xml"), "<Module/>");
                return folder;
            }

            var game = Path.Combine(root, "game");
            var library = Path.Combine(root, "library");
            var harmony = Module(Path.Combine(library, "steamapps", "workshop", "content", "261550", BeStack.Harmony.WorkshopId));
            var compat = Module(Path.Combine(root, "ServerAddOns", BeStack.CompatModuleId));
            var extra = Module(Path.Combine(library, "steamapps", "workshop", "content", "261550", "123456"));
            var sources = new BeCoreSources(game, new[] { library }, compat, Path.Combine(root, "ui.dll"));
            var onThisPc = new[]
            {
                new FoundMod("Extra", "ExtraMod", "123456", extra, "Workshop"),
                // A hand installed Harmony in the game's Modules folder is not what Start copies.
                new FoundMod("Harmony", BeStack.Harmony.ModuleId, "0", Path.Combine(game, "Modules", "Bannerlord.Harmony"), "Modules")
            };

            Assert.Equal(harmony, BeServerPath.StartSource(BeStack.Harmony.ModuleId, sources, onThisPc));
            Assert.Equal(compat, BeServerPath.StartSource(BeStack.CompatModuleId, sources, onThisPc));
            Assert.Equal(Path.Combine(game, "Modules", BeStack.BeModuleId), BeServerPath.StartSource(BeStack.BeModuleId, sources, onThisPc));
            Assert.Equal(extra, BeServerPath.StartSource(" extramod ", sources, onThisPc));
            // Coop and the game modules come with the server folder, so the server's own copy is read for them.
            Assert.Null(BeServerPath.StartSource(BeStack.CoopModuleId, sources, onThisPc));
            Assert.Null(BeServerPath.StartSource("Native", sources, onThisPc));
            Assert.Null(BeServerPath.StartSource("NotHere", sources, onThisPc));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }

        var window = SourceFiles.Read(Path.Combine("src", "BurningEmpiresHost", "MainWindow.ModSetups.cs"));
        Assert.Contains("BeServerPath.StartSource(moduleId, CoreSources(), HostMods())", window);
    }

    [Fact]
    public void The_server_tool_keeps_running_after_an_uncaught_error_and_keeps_the_last_player_list_only_for_its_run()
    {
        // The same net and player list rules TAOM v1.2.16 got in its security review.
        var app = SourceFiles.Read(Path.Combine("src", "BurningEmpiresHost", "App.xaml.cs"));
        var loaded = app.IndexOf("if (MainWindow is not MainWindow { IsLoaded: true } window)", StringComparison.Ordinal);
        Assert.True(loaded > 0 && loaded < app.IndexOf("args.Handled = true;", StringComparison.Ordinal), app);
        Assert.Contains("window.NoteUnexpected(args.Exception);", app);
        var window = SourceFiles.Window(SourceFiles.Root(), "BurningEmpiresHost");
        Assert.Contains("SetControl(\"Something went wrong, and the tool kept running: \" + ex.Message, failed: true);", SourceFiles.Method(window, "internal void NoteUnexpected(Exception ex)"));
        Assert.Contains("var players = running ? HostPlayerLog.Read(ReadPlayerLog(), _onlinePlayers) : [];", SourceFiles.Method(window, "private void LoadPlayerList(bool running)"));
        // A stopped server shows nobody online, though its log still ends with the last players event.
        Assert.Contains("LoadPlayerList(running);", SourceFiles.Read(Path.Combine("src", "BurningEmpiresHost", "MainWindow.State.cs")));
        // A new run and a stopped run forget the old players, so the last good list never outlives its run.
        Assert.Contains("_onlinePlayers = [];", SourceFiles.Method(window, "private void ForgetPlayers()"));
        Assert.True(window.Split("ForgetPlayers();").Length - 1 >= 2, "ForgetPlayers is called on start and on stop");
        // The blocked DLL line comes from the server output thread, so it hops to the window thread first.
        var blocked = SourceFiles.Method(window, "private void ShowBlockedDllFix()");
        var hop = blocked.IndexOf("Dispatcher.BeginInvoke(new Action(ShowBlockedDllFix));", StringComparison.Ordinal);
        Assert.True(hop > 0 && hop < blocked.IndexOf("ShowPage(\"Settings\");", StringComparison.Ordinal), blocked);
    }

    [Fact]
    public void Import_mod_list_keeps_the_host_list_only_after_the_locked_setup_question()
    {
        var source = SourceFiles.Read(Path.Combine("src", "BurningEmpiresLauncher", "MainWindow.Setups.cs"));
        var start = source.IndexOf("void ImportList_Click", StringComparison.Ordinal);
        var confirm = source.IndexOf("if (!ConfirmModChanges())", start, StringComparison.Ordinal);
        var remember = source.IndexOf("RememberHostList(shared);", start, StringComparison.Ordinal);
        Assert.True(confirm > start && remember > confirm, "RememberHostList must come after the Cancel can leave");
        Assert.Equal(remember, source.IndexOf("RememberHostList(", start, StringComparison.Ordinal));
    }
}
