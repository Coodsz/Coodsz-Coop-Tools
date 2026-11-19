using BannerlordCoopLauncher.Core.Modules;
using BurningEmpires.Core;
using TaomCoop.Core;

namespace BannerlordCoopLauncher.Tests;

// The TAOM client launcher asks before it sets a newer Coop aside and puts it back when it closes (CoopParking). The
// Burning Empires launcher keeps a newer Modules Coop, so it has nothing to ask.
public class CoopParkingTests
{
    private static string NewRoot(string name)
    {
        var root = Path.Combine(Path.GetTempPath(), "coop-parking-" + name + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "game", "Modules"));
        return root;
    }

    private static string Module(string parent, string folder, string id, string version)
    {
        var path = Path.Combine(parent, folder);
        Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, "SubModule.xml"), "<Module><Id value=\"" + id + "\"/><Version value=\"" + version + "\"/></Module>");
        return path;
    }

    private static string VersionIn(string module) => ModuleIdentityReader.Read(Path.Combine(module, "SubModule.xml")).Version;

    private static void Delete(string root)
    {
        if (Directory.Exists(root))
            Directory.Delete(root, recursive: true);
    }

    // ----- The ask -----

    [Fact]
    public void Set_aside_and_play_sets_it_aside_and_remembers_nothing_without_the_box()
    {
        var asked = 0;
        var decision = CoopParking.Decide("", null, () => { asked++; return (true, false); });

        Assert.Equal(1, asked);
        Assert.True(decision.SetAside);
        Assert.True(decision.Asked);
        Assert.Null(decision.Save);
    }

    [Fact]
    public void Cancel_keeps_the_coop_and_the_launcher_moves_nothing()
    {
        var decision = CoopParking.Decide(null, null, () => (false, false));

        Assert.False(decision.SetAside);
        Assert.Null(decision.Save);
        // The launcher only moves a folder after a yes, and a no shows the TAOM needs Coop 0.1.6 line.
        var source = SourceFiles.Window(SourceFiles.Root(), "TaomCoopLauncher");
        var method = SourceFiles.Method(source, "private void SetAsideExtraCoop()");
        var no = method.IndexOf("if (!decision.SetAside)", StringComparison.Ordinal);
        var move = method.IndexOf("CoopParking.SetAside(_gameRoot, _setAsideMarker)", StringComparison.Ordinal);
        Assert.True(no >= 0 && no < move, "the no answer returns before anything moves");
        Assert.Contains("ShowJoin(problem)", method[no..move]);
    }

    [Theory]
    [InlineData(true, CoopParking.AlwaysAnswer)]
    [InlineData(false, CoopParking.NeverAnswer)]
    public void Dont_ask_again_saves_the_answer_and_the_next_start_does_not_ask(bool setAside, string saved)
    {
        var first = CoopParking.Decide("", null, () => (setAside, true));
        Assert.Equal(saved, first.Save);

        var next = CoopParking.Decide(first.Save, null, () => throw new InvalidOperationException("asked again"));
        Assert.Equal(setAside, next.SetAside);
        Assert.False(next.Asked);
        Assert.False(CoopParking.NeedsAsk(first.Save, null));
    }

    [Fact]
    public void An_answer_given_this_run_is_not_asked_again()
    {
        var decision = CoopParking.Decide("", true, () => throw new InvalidOperationException("asked again"));
        Assert.True(decision.SetAside);
        Assert.True(CoopParking.NeedsAsk("", null));
        Assert.True(CoopParking.NeedsAsk("something old", null));
        Assert.False(CoopParking.NeedsAsk("", false));
    }

    [Fact]
    public void The_saved_answer_round_trips_and_an_old_settings_file_asks()
    {
        var settings = new TaomClientSettings { SetAsideCoop = CoopParking.AlwaysAnswer };
        Assert.Equal(CoopParking.AlwaysAnswer, TaomClientSettings.Parse(settings.Serialize()).SetAsideCoop);
        Assert.Equal("", TaomClientSettings.Parse("{\"Host\":\"1.2.3.4\"}").SetAsideCoop);
    }

    [Fact]
    public void The_question_names_the_coop_taom_needs_and_the_one_set_aside()
    {
        var root = NewRoot("question");
        try
        {
            var game = Path.Combine(root, "game");
            Module(Path.Combine(game, "Modules"), "Coop", "Coop", "v0.1.7");

            var question = CoopParking.Question(CoopStaging.ExtraCoop(game));

            Assert.Equal("TAOM needs Coop 0.1.6. Your Coop v0.1.7 in Modules will be set aside while this launcher runs, and put back when you close it. Continue?", question);
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void Startup_does_not_move_a_coop_without_asking()
    {
        var source = SourceFiles.Window(SourceFiles.Root(), "TaomCoopLauncher");
        var guards = SourceFiles.Method(source, "private void ApplyCrashGuards()");
        Assert.DoesNotContain("CoopStaging.ParkExtraCoop(", source);
        Assert.Contains("SetAsideExtraCoop();", guards);
        // The window isn't shown in the constructor, so the question waits for Loaded.
        Assert.Contains("_askSetAsideWhenLoaded = true;", SourceFiles.Method(source, "private void SetAsideExtraCoop()"));
        var startup = SourceFiles.Method(source, "public MainWindow()");
        Assert.Contains("if (_askSetAsideWhenLoaded)", startup);
    }

    // Devin review: the put back side waits for Bannerlord to close, the set aside side moved Coop while the game ran.
    [Fact]
    public void Nothing_is_set_aside_or_asked_while_Bannerlord_runs()
    {
        var source = SourceFiles.Window(SourceFiles.Root(), "TaomCoopLauncher");
        var method = SourceFiles.Method(source, "private void SetAsideExtraCoop()");
        var guard = method.IndexOf("if (ClientLaunchGuard.IsGameRunning())", StringComparison.Ordinal);
        Assert.True(guard > 0);
        Assert.True(guard < method.IndexOf("CoopParking.Decide(", StringComparison.Ordinal));
        Assert.True(guard < method.IndexOf("CoopParking.SetAside(", StringComparison.Ordinal));
        Assert.True(guard < method.IndexOf("_askSetAsideWhenLoaded = true;", StringComparison.Ordinal));
        var refusal = method[guard..method.IndexOf("return;", guard, StringComparison.Ordinal)];
        Assert.Contains("Log(CoopParking.CloseGameFirst);", refusal);
        Assert.Equal("Close Bannerlord first, then TAOM can set the other Coop aside.", CoopParking.CloseGameFirst);
        foreach (var language in new[] { "ar", "de", "fr", "pt", "ru", "tr" })
            Assert.True(BannerlordCoopLauncher.Core.Localization.Translator.Entries(language).ContainsKey(CoopParking.CloseGameFirst), language);
    }

    [Fact]
    public void The_new_window_buttons_have_hover_tips()
    {
        var window = SourceFiles.Read(Path.Combine("src", "TaomCoopLauncher", "SetAsideCoopWindow.cs"));
        Assert.Contains("ToolTip = CoopParking.SetAsideTip", window);
        Assert.Contains("ToolTip = CoopParking.CancelTip", window);
        Assert.Contains("ToolTip = CoopParking.DontAskTip", window);
        Assert.Equal("Set aside and play", CoopParking.SetAsideButton);
        Assert.Equal("Cancel", CoopParking.CancelButton);
        Assert.Equal("Don't ask again", CoopParking.DontAskBox);
    }

    // ----- Set aside and put back -----

    [Fact]
    public void Set_aside_moves_a_newer_coop_and_lists_it_in_the_marker()
    {
        var root = NewRoot("set-aside");
        try
        {
            var game = Path.Combine(root, "game");
            var modules = Path.Combine(game, "Modules");
            var marker = Path.Combine(root, "data", CoopParking.MarkerFileName);
            Module(modules, "Coop", "Coop", "v0.1.7");
            Module(modules, "Coop 016", "Coop", "v0.1.6");

            var moved = CoopParking.SetAside(game, marker);

            Assert.Equal([Path.Combine(game, "Coop-kept-0.1.7")], moved);
            Assert.False(Directory.Exists(Path.Combine(modules, "Coop")));
            Assert.True(Directory.Exists(Path.Combine(modules, "Coop 016")));
            Assert.True(CoopParking.HasSetAside(marker));
            Assert.Contains("Coop-kept-0.1.7", File.ReadAllText(marker));
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void Closing_puts_the_coop_back_and_clears_the_marker()
    {
        var root = NewRoot("close");
        try
        {
            var game = Path.Combine(root, "game");
            var modules = Path.Combine(game, "Modules");
            var marker = Path.Combine(root, "data", CoopParking.MarkerFileName);
            Module(modules, "Coop", "Coop", "v0.1.7");
            CoopParking.SetAside(game, marker);

            var lines = CoopParking.PutBack(marker);

            Assert.Equal("v0.1.7", VersionIn(Path.Combine(modules, "Coop")));
            Assert.False(Directory.Exists(Path.Combine(game, "Coop-kept-0.1.7")));
            Assert.False(File.Exists(marker));
            Assert.Equal(["Put Coop-kept-0.1.7 back into Modules as Coop."], lines);
            // The launcher calls it from Closing, after the mod changes question.
            var closing = SourceFiles.Method(SourceFiles.Window(SourceFiles.Root(), "TaomCoopLauncher"), "private void MainWindow_Closing(");
            var confirm = closing.IndexOf("ConfirmModChanges()", StringComparison.Ordinal);
            Assert.True(confirm >= 0 && confirm < closing.IndexOf("PutBackOnClose()", StringComparison.Ordinal));
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void A_marker_left_by_a_crash_puts_the_coop_back_at_the_next_start()
    {
        var root = NewRoot("crash");
        try
        {
            var game = Path.Combine(root, "game");
            var modules = Path.Combine(game, "Modules");
            var marker = Path.Combine(root, "data", CoopParking.MarkerFileName);
            // What a killed launcher leaves: the folder parked and the marker that lists it.
            var parked = Module(game, "Coop-kept-0.1.7", "Coop", "v0.1.7");
            Directory.CreateDirectory(Path.GetDirectoryName(marker)!);
            File.WriteAllText(marker, System.Text.Json.JsonSerializer.Serialize(new[] { new SetAsideEntry(Path.Combine(modules, "Coop"), parked) }));

            // A marker from before the tags: no tag beside the folder, and the marker alone still brings it back.
            Assert.True(CoopParking.HasSetAside(marker, game));
            CoopParking.PutBack(marker, game);

            Assert.Equal("v0.1.7", VersionIn(Path.Combine(modules, "Coop")));
            Assert.False(File.Exists(marker));
            // At startup, before the rows are read.
            var startup = SourceFiles.Method(SourceFiles.Window(SourceFiles.Root(), "TaomCoopLauncher"), "public MainWindow()");
            var putBack = startup.IndexOf("PutBackAfterCrash();", StringComparison.Ordinal);
            Assert.True(putBack >= 0 && putBack < startup.IndexOf("RefreshModules();", StringComparison.Ordinal));
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void Put_back_never_overwrites_a_coop_that_is_in_modules_again()
    {
        var root = NewRoot("taken");
        try
        {
            var game = Path.Combine(root, "game");
            var modules = Path.Combine(game, "Modules");
            var marker = Path.Combine(root, "data", CoopParking.MarkerFileName);
            Module(modules, "Coop", "Coop", "v0.1.7");
            CoopParking.SetAside(game, marker);
            Module(modules, "Coop", "Coop", "v0.1.8");

            var lines = CoopParking.PutBack(marker);

            Assert.Equal("v0.1.8", VersionIn(Path.Combine(modules, "Coop")));
            Assert.Equal("v0.1.7", VersionIn(Path.Combine(game, "Coop-kept-0.1.7")));
            Assert.Contains("Restore parked Coop", Assert.Single(lines));
            Assert.False(File.Exists(marker));
            // Restore parked Coop still finds it, and keeps the Coop that was there as another parked copy.
            CoopStaging.RestoreKeptCoop(game, null);
            Assert.Equal("v0.1.7", VersionIn(Path.Combine(modules, "Coop")));
            Assert.Equal("v0.1.8", VersionIn(Path.Combine(game, "Coop-kept-0.1.8")));
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void A_coop_that_could_not_move_back_stays_in_the_marker_for_the_next_start()
    {
        var root = NewRoot("locked");
        try
        {
            var game = Path.Combine(root, "game");
            var modules = Path.Combine(game, "Modules");
            var marker = Path.Combine(root, "data", CoopParking.MarkerFileName);
            Module(modules, "Coop", "Coop", "v0.1.7");
            CoopParking.SetAside(game, marker);

            IReadOnlyList<string> lines;
            using (new FileStream(Path.Combine(game, "Coop-kept-0.1.7", "SubModule.xml"), FileMode.Open, FileAccess.Read, FileShare.None))
                lines = CoopParking.PutBack(marker);

            Assert.StartsWith("Could not put Coop-kept-0.1.7 back into Modules.", Assert.Single(lines));
            Assert.True(CoopParking.HasSetAside(marker));
            CoopParking.PutBack(marker);
            Assert.Equal("v0.1.7", VersionIn(Path.Combine(modules, "Coop")));
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void A_garbled_or_stale_marker_moves_nothing()
    {
        var root = NewRoot("stale");
        try
        {
            var game = Path.Combine(root, "game");
            var marker = Path.Combine(root, CoopParking.MarkerFileName);
            File.WriteAllText(marker, "{ not json");
            Assert.Empty(CoopParking.PutBack(marker));
            Assert.False(CoopParking.HasSetAside(marker));

            // Restore parked Coop already took it back: the entry is dropped with no line.
            File.WriteAllText(marker, System.Text.Json.JsonSerializer.Serialize(new[] { new SetAsideEntry(Path.Combine(game, "Modules", "Coop"), Path.Combine(game, "Coop-kept-0.1.7")) }));
            Assert.False(CoopParking.HasSetAside(marker));
            Assert.Empty(CoopParking.PutBack(marker));
            Assert.False(File.Exists(marker));
        }
        finally
        {
            Delete(root);
        }
    }

    // ----- The tag beside each parked folder: it goes back even when the marker in the data folder is lost -----

    [Fact]
    public void The_tag_alone_puts_a_nightly_modules_coop_back_to_the_same_path()
    {
        var root = NewRoot("tag-alone");
        try
        {
            var game = Path.Combine(root, "game");
            var modules = Path.Combine(game, "Modules");
            var marker = Path.Combine(root, "data", CoopParking.MarkerFileName);
            // The Coop nightly the Realm of Thrones tools run on, in Modules\Coop.
            var coop = Module(modules, "Coop", "Coop", "v0.1.8");

            var parked = Assert.Single(CoopParking.SetAside(game, marker));
            Assert.Equal(Path.Combine(game, "Coop-kept-0.1.8"), parked);
            var tag = System.Text.Json.JsonSerializer.Deserialize<SetAsideTag>(File.ReadAllText(parked + CoopParking.TagSuffix))!;
            Assert.Equal((coop, parked, CoopParking.TaomTool), (tag.From, tag.To, tag.Tool));

            // A run with another data folder, or a lost data folder: only the tag in the game folder is left.
            File.Delete(marker);
            Assert.False(CoopParking.HasSetAside(marker));
            Assert.True(CoopParking.HasSetAside(marker, game));
            var lines = CoopParking.PutBack(marker, game);

            Assert.Equal(["Put Coop-kept-0.1.8 back into Modules as Coop."], lines);
            Assert.True(Directory.Exists(coop));
            Assert.Equal("v0.1.8", VersionIn(coop));
            Assert.False(Directory.Exists(parked));
            Assert.False(File.Exists(parked + CoopParking.TagSuffix));
            Assert.False(File.Exists(marker));
            // The launcher reads the tags in its game folder at startup and on close.
            var setAside = SourceFiles.Read(Path.Combine("src", "TaomCoopLauncher", "MainWindow.SetAside.cs"));
            Assert.Contains("CoopParking.PutBack(_setAsideMarker, _gameRoot)", setAside);
            Assert.DoesNotContain("CoopParking.HasSetAside(_setAsideMarker)", setAside);
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void Be_sets_its_extra_coop_aside_and_puts_it_back_with_its_own_tags()
    {
        var root = NewRoot("be-put-back");
        try
        {
            var game = Path.Combine(root, "game");
            var modules = Path.Combine(game, "Modules");
            var marker = Path.Combine(root, "data", BeCoopCopy.SetAsideMarkerFileName);
            var workshop = Module(Path.Combine(root, "workshop"), "3770450698", "Coop", "v0.1.7");
            var stale = Module(modules, "Coop", "Coop", "v0.1.6");
            var nightly = Module(modules, "CoopNightly", "CoopNightly", "v0.0.9");

            var moved = BeCoopCopy.ParkBesideWorkshop(game, workshop, marker);

            Assert.Equal(2, moved.Count);
            Assert.All(moved, parked => Assert.Equal(CoopParking.BeTool,
                System.Text.Json.JsonSerializer.Deserialize<SetAsideTag>(File.ReadAllText(parked + CoopParking.TagSuffix))!.Tool));
            // The TAOM launcher leaves Burning Empires' tags alone.
            Assert.False(CoopParking.HasSetAside(Path.Combine(root, "taom", CoopParking.MarkerFileName), game));
            Assert.Empty(CoopParking.PutBack(Path.Combine(root, "taom", CoopParking.MarkerFileName), game));
            Assert.False(Directory.Exists(stale));

            File.Delete(marker);
            var lines = CoopParking.PutBack(marker, game, CoopParking.BeTool);

            Assert.Equal(2, lines.Count);
            Assert.Equal("v0.1.6", VersionIn(stale));
            Assert.Equal("v0.0.9", VersionIn(nightly));
            Assert.Empty(Directory.EnumerateFileSystemEntries(game, "*-kept-*"));
        }
        finally
        {
            Delete(root);
        }
    }

    [Theory]
    [InlineData("bin", "outside")]
    [InlineData("bin-kept-1", "outside")]
    [InlineData("Coop-kept-0.1.8", "outside")]
    [InlineData("bin", "Modules")]
    public void A_crafted_tag_moves_nothing(string toName, string fromParent)
    {
        // A tag is a file anyone can write in the game folder; only a -kept- folder going back into Modules moves.
        var root = NewRoot("crafted-tag");
        try
        {
            var game = Path.Combine(root, "game");
            var marker = Path.Combine(root, "data", CoopParking.MarkerFileName);
            var to = Path.Combine(game, toName);
            Directory.CreateDirectory(to);
            var from = fromParent == "Modules" ? Path.Combine(game, "Modules", "Moved") : Path.Combine(root, "elsewhere", "Moved");
            Directory.CreateDirectory(Path.GetDirectoryName(from)!);
            File.WriteAllText(to + CoopParking.TagSuffix, System.Text.Json.JsonSerializer.Serialize(new SetAsideTag(from, to, CoopParking.TaomTool, DateTime.UtcNow)));

            Assert.False(CoopParking.HasSetAside(marker, game));
            Assert.Empty(CoopParking.PutBack(marker, game));
            Assert.True(Directory.Exists(to));
            Assert.False(Directory.Exists(from));
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void A_parked_name_that_is_taken_moves_nothing_and_keeps_the_other_tag()
    {
        // KeptPath falls back to a time stamped name; when that name is taken too, the folder stays and the other
        // parked folder's tag is left alone (Codex).
        var root = NewRoot("taken-name");
        try
        {
            var game = Path.Combine(root, "game");
            var modules = Path.Combine(game, "Modules");
            var marker = Path.Combine(root, "data", CoopParking.MarkerFileName);
            var coop = Module(modules, "Coop", "Coop", "v0.1.8");
            var folder = Assert.Single(CoopStaging.ExtraCoop(game));
            // A file (not a folder) holds the parked name, so KeptPath keeps it and SetAside has to refuse it.
            var taken = Path.Combine(game, "Coop-kept-0.1.8");
            File.WriteAllText(taken, "");
            File.WriteAllText(taken + CoopParking.TagSuffix, "other");
            Assert.Equal(taken, CoopStaging.KeptPath(folder, game));

            Assert.ThrowsAny<IOException>(() => CoopParking.SetAside(game, marker, [folder], CoopParking.TaomTool));
            Assert.True(Directory.Exists(coop));
            Assert.Equal("other", File.ReadAllText(taken + CoopParking.TagSuffix));
            Assert.False(CoopParking.HasSetAside(marker));
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void A_tag_of_the_other_tool_is_not_touched()
    {
        var root = NewRoot("tag-other");
        try
        {
            var game = Path.Combine(root, "game");
            var modules = Path.Combine(game, "Modules");
            var marker = Path.Combine(root, "data", CoopParking.MarkerFileName);
            var beMarker = Path.Combine(root, "data", BeCoopCopy.SetAsideMarkerFileName);
            Module(modules, "Coop", "Coop", "v0.1.8");
            var parked = Assert.Single(CoopParking.SetAside(game, marker));

            Assert.False(CoopParking.HasSetAside(beMarker, game, CoopParking.BeTool));
            Assert.Empty(CoopParking.PutBack(beMarker, game, CoopParking.BeTool));

            Assert.True(Directory.Exists(parked));
            Assert.True(File.Exists(parked + CoopParking.TagSuffix));
            Assert.False(Directory.Exists(Path.Combine(modules, "Coop")));
            Assert.False(File.Exists(beMarker));
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void A_modules_coop_that_is_there_again_keeps_the_parked_copy_and_drops_the_tag()
    {
        var root = NewRoot("tag-taken");
        try
        {
            var game = Path.Combine(root, "game");
            var modules = Path.Combine(game, "Modules");
            var marker = Path.Combine(root, "data", CoopParking.MarkerFileName);
            Module(modules, "Coop", "Coop", "v0.1.8");
            var parked = Assert.Single(CoopParking.SetAside(game, marker));
            File.Delete(marker);
            Module(modules, "Coop", "Coop", "v0.1.9");

            var lines = CoopParking.PutBack(marker, game);

            Assert.Equal("Modules\\Coop is there again, so Coop-kept-0.1.8 stays in the game folder. Click Restore parked Coop in Settings if you want it back.", Assert.Single(lines));
            Assert.Equal("v0.1.9", VersionIn(Path.Combine(modules, "Coop")));
            Assert.Equal("v0.1.8", VersionIn(parked));
            Assert.False(File.Exists(parked + CoopParking.TagSuffix));
            Assert.False(CoopParking.HasSetAside(marker, game));
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void A_coop_that_could_not_move_back_keeps_its_tag_and_marker()
    {
        var root = NewRoot("tag-locked");
        try
        {
            var game = Path.Combine(root, "game");
            var modules = Path.Combine(game, "Modules");
            var marker = Path.Combine(root, "data", CoopParking.MarkerFileName);
            Module(modules, "Coop", "Coop", "v0.1.8");
            var parked = Assert.Single(CoopParking.SetAside(game, marker));

            IReadOnlyList<string> lines;
            using (new FileStream(Path.Combine(parked, "SubModule.xml"), FileMode.Open, FileAccess.Read, FileShare.None))
                lines = CoopParking.PutBack(marker, game);

            Assert.StartsWith("Could not put Coop-kept-0.1.8 back into Modules.", Assert.Single(lines));
            Assert.True(File.Exists(parked + CoopParking.TagSuffix));
            Assert.True(File.Exists(marker));
            Assert.True(CoopParking.HasSetAside(marker, game));

            // The next start, with the data folder gone: the tag still brings it back.
            File.Delete(marker);
            CoopParking.PutBack(marker, game);
            Assert.Equal("v0.1.8", VersionIn(Path.Combine(modules, "Coop")));
            Assert.False(File.Exists(parked + CoopParking.TagSuffix));
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void Use_workshop_coop_with_a_marker_sets_aside_the_same_way()
    {
        var source = SourceFiles.Window(SourceFiles.Root(), "TaomCoopLauncher");
        Assert.Contains("CoopStaging.UsePlaytestCoop(workshop, _gameRoot, new Progress<string>(Log), marker)", SourceFiles.Method(source, "private async void UseCoop_Click("));
        Assert.Contains("CoopParking.SetAside(gameRoot, setAsideMarker)", SourceFiles.Read(Path.Combine("src", "TaomCoop.Core", "CoopStaging.cs")));
    }

    // ----- A newer Workshop Coop: setting a Coop aside can't make TAOM run, so nothing is asked or moved -----

    [Theory]
    [InlineData("v0.1.7", true)]
    [InlineData("v0.1.6", false)]
    [InlineData("v0.1.5", false)]
    public void A_newer_workshop_coop_has_nothing_to_set_aside_for(string workshopVersion, bool newer)
    {
        var root = NewRoot("newer-workshop");
        try
        {
            var game = Path.Combine(root, "game");
            Module(Path.Combine(game, "Modules"), "Coop", "Coop", "v0.1.7");
            var workshop = Module(Path.Combine(root, "workshop"), "3770450698", "Coop", workshopVersion);
            // A Modules Coop the launcher would otherwise ask about.
            Assert.NotEmpty(CoopStaging.ExtraCoop(game));

            var line = CoopParking.NothingToSetAsideFor(workshop, game);
            if (newer)
                Assert.Equal(CoopStaging.WrongWorkshopCoop("Coop", workshopVersion), line);
            else
                Assert.Null(line);
            Assert.Null(CoopParking.NothingToSetAsideFor(null, game));
            Assert.Null(CoopParking.NothingToSetAsideFor(Path.Combine(root, "no-workshop"), game));
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void The_launcher_checks_the_workshop_coop_before_it_asks_or_acts_on_a_saved_always()
    {
        var setAside = SourceFiles.Method(SourceFiles.Window(SourceFiles.Root(), "TaomCoopLauncher"), "private void SetAsideExtraCoop()");
        var check = setAside.IndexOf("CoopParking.NothingToSetAsideFor(CoopStaging.FindWorkshopModule(", StringComparison.Ordinal);
        Assert.True(check > 0);
        Assert.True(check < setAside.IndexOf("CoopParking.NeedsAsk(", StringComparison.Ordinal));
        Assert.True(check < setAside.IndexOf("CoopParking.Decide(", StringComparison.Ordinal));
        Assert.True(check < setAside.IndexOf("CoopParking.SetAside(", StringComparison.Ordinal));
        Assert.Contains("ShowJoin(newer)", setAside);
    }

    [Fact]
    public void Use_workshop_coop_counts_as_this_runs_answer_only_after_it_worked()
    {
        var click = SourceFiles.Method(SourceFiles.Window(SourceFiles.Root(), "TaomCoopLauncher"), "private async void UseCoop_Click(");
        var use = click.IndexOf("CoopStaging.UsePlaytestCoop(", StringComparison.Ordinal);
        var answer = click.IndexOf("_setAsideThisRun = true", StringComparison.Ordinal);
        Assert.True(use > 0);
        Assert.True(answer > use);
        Assert.Contains("if (used)", click[use..answer]);
    }

    // ----- Burning Empires keeps a newer Modules Coop, so it parks nothing to ask about -----

    [Theory]
    [InlineData("v0.1.6")]
    [InlineData("v0.1.7")]
    public void Be_leaves_a_lone_newer_or_same_modules_coop_where_it_is(string workshopVersion)
    {
        var root = NewRoot("be");
        try
        {
            var game = Path.Combine(root, "game");
            var modules = Path.Combine(game, "Modules");
            var workshop = Module(Path.Combine(root, "workshop"), "3770450698", "Coop", workshopVersion);
            Module(modules, "Coop", "Coop", "v0.1.7");

            Assert.Empty(BeCoopCopy.ParkBesideWorkshop(game, workshop, Path.Combine(root, BeCoopCopy.SetAsideMarkerFileName)));
            Assert.Empty(BeCoopCopy.ParkWithoutWorkshop(game, Path.Combine(root, BeCoopCopy.SetAsideMarkerFileName)));
            Assert.Equal("v0.1.7", VersionIn(Path.Combine(modules, "Coop")));
            Assert.Empty(Directory.EnumerateDirectories(game, "Coop-kept-*"));
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void Be_launcher_puts_its_own_set_aside_coop_back_without_the_taom_question()
    {
        var source = SourceFiles.Window(SourceFiles.Root(), "BurningEmpiresLauncher");
        Assert.DoesNotContain("CoopStaging.ParkExtraCoop(", source);
        Assert.DoesNotContain("CoopParking.Decide(", source);
        Assert.DoesNotContain("CoopParking.SetAside(", source);
        Assert.DoesNotContain("CoopParking.PutBack(_setAsideMarker)", source);
        Assert.Contains("CoopParking.PutBack(_setAsideMarker, _gameRoot, CoopParking.BeTool)", source);
        // What a killed launcher left goes back at startup, before the crash guards set the extra copies aside again.
        var startup = SourceFiles.Method(source, "public MainWindow()");
        var putBack = startup.IndexOf("PutBackAfterCrash();", StringComparison.Ordinal);
        Assert.True(putBack >= 0 && putBack < startup.IndexOf("RefreshModules();", StringComparison.Ordinal));
        Assert.True(putBack < startup.IndexOf("ApplyCrashGuards();", StringComparison.Ordinal));
        var closing = SourceFiles.Method(source, "private void MainWindow_Closing(");
        var confirm = closing.IndexOf("ConfirmModChanges()", StringComparison.Ordinal);
        Assert.True(confirm >= 0 && confirm < closing.IndexOf("PutBackOnClose()", StringComparison.Ordinal));
    }

    // ----- The side Mods card hides empty groups -----

    [Fact]
    public void Plain_taom_shows_only_the_always_on_chip()
    {
        var tally = ModTally.Count(TaomStack.ServerModules.Select(id => new TallyRow(id, true, true)));

        Assert.Equal([(ModTally.AlwaysOnChip, tally.On)], tally.Chips());
        Assert.Empty(ModTally.Empty.Chips());
        var card = SourceFiles.Read(Path.Combine("src", "CoodszHost", "Shared", "Frame", "SideModsCard.cs"));
        Assert.Contains("tally.Chips()", card);
        Assert.DoesNotContain("group.Always", card);
    }

    [Fact]
    public void Works_counts_an_extra_the_taom_list_calls_safe()
    {
        var safe = TaomCompatList.BuiltIn().Mods.FirstOrDefault(entry => TaomCompatList.Works(entry.Kind));
        Assert.NotNull(safe);
        var rows = TaomStack.ServerModules.Select(id => new TallyRow(id, true, true))
            .Append(new TallyRow(safe!.Id.Length > 0 ? safe.Id : "SomeSafeMod", true, false, TaomCompatList.Works(safe.Kind)));

        var chips = ModTally.Count(rows).Chips();

        Assert.Contains((ModTally.WorksChip, 1), chips);
        Assert.DoesNotContain(chips, chip => chip.Count == 0);
    }
}
