using BannerlordCoopLauncher.Core.AD1259;
using BannerlordCoopLauncher.Core.Keep;
using BannerlordCoopLauncher.Core.Legal;
using BannerlordCoopLauncher.Core.Localization;
using BannerlordCoopLauncher.Core.Updates;

namespace BannerlordCoopLauncher.Tests;

public class WhatsNewTests
{
    [Fact]
    public void Every_release_has_notes_at_the_top_of_its_list()
    {
        // Bumping a version without writing what changed fails here.
        Assert.Equal(ReleaseNotice.FriendsVersion, WhatsNew.Friends[0].Version);
        Assert.Equal(ReleaseNotice.AD1259Version, WhatsNew.AD1259[0].Version);
        Assert.Equal(ReleaseNotice.TaomVersion, WhatsNew.Taom[0].Version);
        Assert.Equal(ReleaseNotice.BeVersion, WhatsNew.Be[0].Version);
        Assert.Equal(ReleaseNotice.RotVersion, WhatsNew.Rot[0].Version);
        foreach (var list in new[] { WhatsNew.Friends, WhatsNew.AD1259, WhatsNew.Taom, WhatsNew.Be, WhatsNew.Rot })
            Assert.All(list, entry => Assert.NotEmpty(entry.Lines));
    }

    [Fact]
    public void Notes_are_translated_into_every_language()
    {
        var lines = WhatsNew.Friends.Concat(WhatsNew.AD1259).Concat(WhatsNew.Taom).Concat(WhatsNew.Be).Concat(WhatsNew.Rot)
            .SelectMany(entry => entry.Lines)
            .Concat([WhatsNew.Title, WhatsNew.Heading, WhatsNew.Close, WhatsNew.ButtonTip, WhatsNew.OpenAgainHint, WhatsNew.CloseTip])
            .Distinct();
        foreach (var language in Translator.Languages.Where(language => language.Code != Translator.English))
        {
            var table = Translator.Entries(language.Code);
            Assert.All(lines, line => Assert.True(table.ContainsKey(line), language.Code + " has no translation for: " + line));
        }
    }

    [Fact]
    public void The_v137_wave_lists_name_only_what_each_tool_has()
    {
        // The AD1259 Server Tool does not load the settings sync module, AD1259 and BE rows have no Needs update
        // or Missing on Workshop chips, and BE pins no Coop version.
        const string settingsSync = "When the host has no mod settings to share";
        const string myReports = "My reports keeps checking";
        var ad1259 = WhatsNew.AD1259.Single(entry => entry.Version == "v1.1.8").Lines;
        var taom = WhatsNew.Taom.Single(entry => entry.Version == "v1.2.7").Lines;
        var be = WhatsNew.Be.Single(entry => entry.Version == "v1.0.7").Lines;
        Assert.DoesNotContain(ad1259, line => line.StartsWith(settingsSync));
        Assert.Contains(taom, line => line.StartsWith(settingsSync));
        Assert.All(new[] { ad1259, taom, be }, lines => Assert.Contains(lines, line => line.StartsWith(myReports)));
        Assert.All(new[] { ad1259, be }, lines => Assert.DoesNotContain(lines, line => line.Contains("Needs update") || line.Contains("Missing on Workshop")));
        Assert.Contains(be, line => line.Contains("Bannerlord and Burning Empires versions") && line.Contains("the Coop version the server has"));
        Assert.DoesNotContain(be, line => line.Contains("Bannerlord, Coop and overhaul versions"));
    }

    [Fact]
    public void The_v138_wave_lists_name_only_what_each_tool_has()
    {
        // Download Coop server ships in the Coop, AD1259 and BE Server Tools (BE picks it with Choose Coop server),
        // the line under Active Coop only in the Coop and AD1259 Server Tools, the zip search fix only in TAOM, and
        // the New setup fix only in AD1259.
        var friends = WhatsNew.Friends.Single(entry => entry.Version == "v1.3.8").Lines;
        var ad1259 = WhatsNew.AD1259.Single(entry => entry.Version == "v1.1.9").Lines;
        var taom = WhatsNew.Taom.Single(entry => entry.Version == "v1.2.8").Lines;
        var be = WhatsNew.Be.Single(entry => entry.Version == "v1.0.8").Lines;
        Assert.All(new[] { friends, ad1259 }, lines =>
        {
            Assert.Contains(lines, line => line.StartsWith("New Download Coop server") && line.Contains("Set server folder"));
            Assert.Contains(lines, line => line.Contains("under Active Coop"));
        });
        Assert.Contains(be, line => line.StartsWith("New Download Coop server") && line.Contains("Choose Coop server"));
        Assert.DoesNotContain(be, line => line.Contains("Set server folder") || line.Contains("Active Coop"));
        Assert.DoesNotContain(taom, line => line.Contains("Download Coop server"));
        Assert.Contains(taom, line => line.Contains("CompatOnly zips"));
        Assert.Contains(ad1259, line => line.Contains("New setup"));
        Assert.All(new[] { friends, taom, be }, lines => Assert.DoesNotContain(lines, line => line.Contains("New setup")));
    }

    [Fact]
    public void The_coop017_workshop_wave_lists_name_only_what_each_tool_has()
    {
        // Coop 0.1.7 reached the Workshop: the Coop, AD1259 and BE tools drop the GitHub lines and use the Workshop
        // server (BE through Prepare server and Choose Coop server), while TAOM stays on Coop 0.1.6 for TAOM.CoopCompat.
        var friends = WhatsNew.Friends.Single(entry => entry.Version == "v1.3.9").Lines;
        var ad1259 = WhatsNew.AD1259.Single(entry => entry.Version == "v1.1.10").Lines;
        var taom = WhatsNew.Taom.Single(entry => entry.Version == "v1.2.9").Lines;
        var be = WhatsNew.Be.Single(entry => entry.Version == "v1.0.9").Lines;
        Assert.All(new[] { friends, ad1259, be }, lines =>
        {
            Assert.Contains(lines, line => line.StartsWith("Coop v0.1.7 is on the Steam Workshop now")
                && line.Contains("Workshop item " + BannerlordCoopLauncher.Core.Modules.CoopInstall.WorkshopId)
                && line.Contains("Download Coop server under Advanced: server folder"));
            Assert.DoesNotContain(lines, line => line.Contains("TAOM"));
        });
        Assert.Contains(friends, line => line.Contains("Set server folder"));
        Assert.Equal(friends, ad1259);
        Assert.Contains(be, line => line.Contains("Prepare server") && line.Contains("Choose Coop server"));
        Assert.DoesNotContain(be, line => line.Contains("Set server folder"));
        var taomLine = Assert.Single(taom);
        Assert.Contains("TAOM still needs Coop v0.1.6", taomLine);
        Assert.Contains("TAOM.CoopCompat " + TaomCoop.Core.TaomStack.CompatVersion.TrimStart('v') + " only works with it", taomLine);
        Assert.DoesNotContain("Download Coop server", taomLine);

        // v1.1.10 is the first AD1259 version with two digits at the end: it sorts after v1.1.9, not before it.
        Assert.True(ReleaseNotice.IsNewer("v1.1.10", "v1.1.9"));
        Assert.Equal(["v1.1.10", "v1.1.9"], WhatsNew.Recent(WhatsNew.AD1259, "v1.1.10", max: 2).Select(entry => entry.Version));
        Assert.Equal(["v1.1.9", "v1.1.8"], WhatsNew.Recent(WhatsNew.AD1259, "v1.1.9", max: 2).Select(entry => entry.Version));
    }

    [Fact]
    public void The_after_139_round_lists_name_only_what_each_tool_has()
    {
        var friends = WhatsNew.Friends.Single(entry => entry.Version == "v1.3.10");
        var ad1259 = WhatsNew.AD1259.Single(entry => entry.Version == "v1.1.11");
        var taom = WhatsNew.Taom.Single(entry => entry.Version == "v1.2.10");
        var be = WhatsNew.Be.Single(entry => entry.Version == "v1.0.10");

        // Every Client Launcher: the join pill and the numbered Coop client logs in Send my logs.
        foreach (var lines in new[] { friends.Lines, ad1259.Lines, taom.Lines, be.Lines })
        {
            Assert.Contains(lines, line => line.Contains("it says Game open instead"));
            Assert.Contains(lines, line => line.StartsWith("Send my logs now also packs the newest three numbered Coop client logs"));
        }

        // The Coop and AD1259 Server Tools share the host code: FATAL line, Use the Steam server and the map box label.
        Assert.All(new[] { friends.Lines, ad1259.Lines }, lines =>
        {
            Assert.Contains(lines, line => line.StartsWith("When the server stops itself with a FATAL line"));
            Assert.Contains(lines, line => line.Contains("Click Use the Steam server"));
            Assert.Contains(lines, line => line.Contains("(map files missing on the server)"));
        });
        Assert.Contains(friends.Lines, line => line.Contains("Coodsz Coop Server Tool now quotes"));
        Assert.DoesNotContain(ad1259.Lines, line => line.Contains("Coodsz Coop Server Tool"));
        Assert.Contains(ad1259.Lines, line => line.StartsWith("The AD1259 tools now also take the older Open Source set"));
        Assert.DoesNotContain(friends.Lines, line => line.Contains("Open Source"));

        Assert.Contains(taom.Lines, line => line.Contains("click Choose Coop 0.1.6 server on the Saves page"));
        Assert.Contains(taom.Lines, line => line.Contains("Check mods now shows the Coop line on the Play page"));
        Assert.All(new[] { taom.Lines, be.Lines }, lines =>
            Assert.DoesNotContain(lines, line => line.Contains("FATAL") || line.Contains("Use the Steam server") || line.Contains("Open Source")));
        Assert.Equal(2, be.Lines.Count);
    }

    [Fact]
    public void Burning_Empires_v1_0_11_names_the_map_check_guard_and_the_SubModule_repeats()
    {
        var be = WhatsNew.Be.Single(entry => entry.Version == "v1.0.11");
        Assert.Equal(2, be.Lines.Count);

        // The Client Launcher line only says should: peaches' exact save was never reproduced.
        var join = be.Lines[0];
        Assert.Contains("should no longer crash", join);
        Assert.Contains(@"Client Diagnostics\CoodszJoinPassword.log", join);
        Assert.Contains("click Send my logs in the Client Launcher again", join);
        Assert.DoesNotContain("fixed", join, StringComparison.OrdinalIgnoreCase);

        var server = be.Lines[1];
        Assert.StartsWith("Start server no longer adds Launcher Cheats and Launcher Battle Size", server);
        Assert.Contains("SubModule.xml.coodsz-original", server);
    }

    [Fact]
    public void The_ad1259_v1112_list_names_only_the_companion_guard()
    {
        var entry = WhatsNew.AD1259.Single(candidate => candidate.Version == "v1.1.12");
        var line = Assert.Single(entry.Lines);
        Assert.StartsWith("A new AD1259 campaign on Coop 0.1.7 no longer stops the server.", line);
        Assert.Contains(AD1259Stack.CompatName + " module " + AD1259Stack.CompatVersion + " by SkyApolo", line);
    }

    [Fact]
    public void The_map_shader_round_lists_name_only_what_each_tool_has()
    {
        var friends = WhatsNew.Friends.Single(entry => entry.Version == "v1.3.11");
        var ad1259 = WhatsNew.AD1259.Single(entry => entry.Version == "v1.1.13");
        var taom = WhatsNew.Taom.Single(entry => entry.Version == "v1.2.11");
        // Burning Empires v1.0.12 was its entry for this round.
        var be = WhatsNew.Be.Single(entry => entry.Version == "v1.0.12");

        // Every Client Launcher clears the map shader files on Connect; the line only says should, since no player has
        // confirmed the crash gone yet.
        foreach (var lines in new[] { friends.Lines, ad1259.Lines, taom.Lines, be.Lines })
        {
            var shader = lines[0];
            Assert.StartsWith("The game should no longer crash on the campaign map because of old map shader files", shader);
            Assert.Contains("click Connect", shader);
            Assert.Contains("Say No and Connect goes on as before.", shader);
        }

        // The greyed difficulty box note is only in the TAOM and Burning Empires Server Tools.
        Assert.Single(friends.Lines);
        Assert.Single(ad1259.Lines);
        Assert.All(new[] { taom.Lines, be.Lines }, lines =>
        {
            Assert.Equal(2, lines.Count);
            Assert.StartsWith("Settings now says why some difficulty boxes are greyed out.", lines[1]);
            Assert.Contains("Save settings", lines[1]);
        });
    }

    [Fact]
    public void The_mcm_nightly_and_coop016_round_lists_name_only_what_each_tool_has()
    {
        // Later rounds came after this one, so each entry of this round is found by version.
        var friends = WhatsNew.Friends.Single(entry => entry.Version == "v1.3.12");
        var ad1259 = WhatsNew.AD1259.Single(entry => entry.Version == "v1.1.14");
        var taom = WhatsNew.Taom.Single(entry => entry.Version == "v1.2.12");
        var be = WhatsNew.Be.Single(entry => entry.Version == "v1.0.13");

        // Both Server Tools built from CoodszHost get the Logs page lines in the picked language.
        const string logsPage = "The Logs page now shows its What happened, What to do and What not to do lines in the language you picked.";
        // Every Server Tool that ships Coop Save Rescue gets the captive server hero line.
        const string saveRescue = "Save Rescue now keeps the map moving when a save has the server's own hero, the one nobody plays, as a prisoner.";
        Assert.StartsWith(saveRescue, Assert.Single(be.Lines));
        Assert.Equal(3, friends.Lines.Count);
        Assert.StartsWith("A campaign played on the Coop nightly no longer disappears from the saves", friends.Lines[0]);
        Assert.StartsWith(logsPage, friends.Lines[1]);
        Assert.StartsWith(saveRescue, friends.Lines[2]);

        // AD1259 never takes the Coop nightly, so its save line leaves the nightly half out.
        Assert.Equal(4, ad1259.Lines.Count);
        Assert.StartsWith("The AD1259 tools now take MCM v5.13.1", ad1259.Lines[0]);
        Assert.StartsWith("When a mod's version changed since a save was made, the Logs page now names that mod", ad1259.Lines[1]);
        Assert.DoesNotContain("nightly", ad1259.Lines[1]);
        Assert.StartsWith(logsPage, ad1259.Lines[2]);
        Assert.StartsWith(saveRescue, ad1259.Lines[3]);

        Assert.Equal(2, taom.Lines.Count);
        var taomLine = taom.Lines[0];
        Assert.StartsWith(saveRescue, taom.Lines[1]);
        Assert.StartsWith("TAOM players can join again while Steam has Coop v0.1.7.", taomLine);
        Assert.Contains("Get Coop 0.1.6", taomLine);
    }

    [Fact]
    public void Coop_and_friends_v1_3_13_lists_the_my_little_warband_recruits_and_the_copy_setup_tip_only()
    {
        // Only Coop and Friends was released this round; the next round comes after it, so it is found by version.
        var friends = WhatsNew.Friends.Single(entry => entry.Version == "v1.3.13");
        Assert.Equal(2, friends.Lines.Count);
        Assert.StartsWith("Players can now recruit their own My Little Warband troops in Coop.", friends.Lines[0]);
        Assert.Contains("Gold is charged at the normal troop's price", friends.Lines[0]);
        Assert.StartsWith("The tip on the Copy setup button no longer says it copies the save and the map.", friends.Lines[1]);
        // v1.8.0 brought the recruits; later fixes may raise it.
        Assert.True(Version.Parse(ToolsCommand.ModuleVersion.TrimStart('v')) >= new Version(1, 8, 0), ToolsCommand.ModuleVersion);
        Assert.DoesNotContain(WhatsNew.AD1259.Concat(WhatsNew.Taom).Concat(WhatsNew.Be), entry => entry.Lines.Any(line => line.Contains("My Little Warband")));
    }

    [Fact]
    public void Realm_of_thrones_v0_1_0_is_the_first_list_and_reads_its_version_from_release_notice()
    {
        // One number for the tools, the update check and the release script (RotRelease reads ReleaseNotice).
        Assert.Equal((ReleaseNotice.RotRepo, ReleaseNotice.RotVersion), (RealmOfThrones.Core.RotRelease.Repo, RealmOfThrones.Core.RotRelease.Version));
        // The newest entry is the current version; v0.1.0, the first release, stays last.
        Assert.Equal(ReleaseNotice.RotVersion, WhatsNew.Rot[0].Version);
        var rot = WhatsNew.Rot[^1];
        Assert.Equal("v0.1.0", rot.Version);
        Assert.Equal(6, rot.Lines.Count);
        Assert.StartsWith("The first Coodsz Realm of Thrones tools.", rot.Lines[0]);
        Assert.Contains("Connect puts it in the game's Modules folder.", rot.Lines[1]);
        Assert.StartsWith("Prepare server builds the server in C:\\RotServer", rot.Lines[2]);
        Assert.Contains("after 60 seconds", rot.Lines[3]);
        Assert.StartsWith("The Encyclopedia page", rot.Lines[4]);
        Assert.StartsWith(RealmOfThrones.Core.RotSetupBook.CoreName + " is the locked setup", rot.Lines[5]);
        // No credit to the Realm of Thrones team anywhere in the tools.
        Assert.DoesNotContain(rot.Lines, line => line.Contains("team", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void The_languages_and_newer_game_round_lists_name_only_what_each_tool_has()
    {
        // The license round came after this one, so each entry of this round is found by version.
        var friends = WhatsNew.Friends.Single(entry => entry.Version == "v1.3.14");
        var ad1259 = WhatsNew.AD1259.Single(entry => entry.Version == "v1.1.15");
        var taom = WhatsNew.Taom.Single(entry => entry.Version == "v1.2.13");
        var be = WhatsNew.Be.Single(entry => entry.Version == "v1.0.14");

        // Every tool opens with the three new languages, the newer game line and the translated I accept button.
        const string newerGame = "When your Bannerlord is newer than 1.4.8, the tools now say so ";
        const string terms = "The I accept button in the Coop server terms window now shows in your language.";
        foreach (var lines in new[] { friends.Lines, ad1259.Lines, taom.Lines, be.Lines })
        {
            Assert.Equal(WhatsNew.MoreLanguages, lines[0]);
            Assert.StartsWith(newerGame, lines[1]);
            Assert.Equal(terms, lines[2]);
        }

        // The Coop Server Tool's card is titled Versions and the Coop Client Launcher has no Made for card (release-reviewer).
        Assert.Contains("Server Tool's Versions card", friends.Lines[1]);
        Assert.DoesNotContain("Made for", friends.Lines[1]);
        foreach (var lines in new[] { ad1259.Lines, taom.Lines, be.Lines })
            Assert.Contains("in the Made for card", lines[1]);

        // Coop and Friends offers every newer Coop setting; the AD1259 server runs Workshop Coop 0.1.7, so only the vote time.
        Assert.Equal(4, friends.Lines.Count);
        Assert.Contains("Preferred battle hosts", friends.Lines[3]);
        Assert.Contains("never written", friends.Lines[3]);
        Assert.Equal(5, ad1259.Lines.Count);
        Assert.Equal("Show more Coop settings on the Settings page now has the kingdom decision vote time.", ad1259.Lines[3]);
        Assert.DoesNotContain("nightly", string.Join(" ", ad1259.Lines));
        // The Copy setup tip shipped in Coop and Friends v1.3.13 only; the AD1259 Server Tool shares that button.
        Assert.StartsWith("The tip on the Copy setup button no longer says", ad1259.Lines[4]);

        // TAOM and Burning Empires Client Launchers put a set aside Coop back; Burning Empires used to keep extra copies out.
        Assert.Equal(4, taom.Lines.Count);
        Assert.StartsWith("When the Client Launcher sets a Coop folder aside", taom.Lines[3]);
        Assert.Equal(5, be.Lines.Count);
        Assert.StartsWith(taom.Lines[3], be.Lines[3]);
        Assert.EndsWith("Before, it moved extra Coop copies into the game folder for good.", be.Lines[3]);
        Assert.StartsWith("Start server now turns on players' time buttons (pause and 2x)", be.Lines[4]);

        // Only the Coop and Friends and AD1259 Server Tools have the Show more Coop settings lines.
        Assert.DoesNotContain(taom.Lines.Concat(be.Lines), line => line.Contains("Show more Coop settings"));
    }

    [Fact]
    public void The_license_round_lists_the_same_three_lines_in_every_tool()
    {
        var friends = WhatsNew.Friends[0];
        // AD1259 v1.1.17 and TAOM v1.2.15 and v1.2.16 came after the license round, so each is found by its version.
        var ad1259 = WhatsNew.AD1259.Single(entry => entry.Version == "v1.1.16");
        var taom = WhatsNew.Taom.Single(entry => entry.Version == "v1.2.14");
        // Burning Empires v1.0.16 came after it too.
        var be = WhatsNew.Be.Single(entry => entry.Version == "v1.0.15");
        Assert.Equal(("v1.3.15", "v1.1.16", "v1.2.14", "v1.0.15"), (friends.Version, ad1259.Version, taom.Version, be.Version));
        // Only Coop and Friends is still on the license round.
        Assert.Equal(ReleaseNotice.FriendsVersion, friends.Version);

        // The tool license changed, so every tool asks again; module folders carry LICENSE.txt; reports list the Coodsz modules.
        foreach (var lines in new[] { friends.Lines, ad1259.Lines, taom.Lines, be.Lines })
            Assert.Equal([WhatsNew.LicensePaidServers, WhatsNew.ModuleFolderLicense, WhatsNew.ReportModuleVersions], lines);
        Assert.Contains("paid server", WhatsNew.LicensePaidServers);
        Assert.Contains("streams or videos", WhatsNew.LicensePaidServers);
        Assert.Contains("accept it once more", WhatsNew.LicensePaidServers);
        // The license text itself carries the new point 1 and point 5.
        Assert.DoesNotContain("Use the tools to make money", ToolLicense.Text);
    }

    [Fact]
    public void AD1259_v1_1_17_lists_the_nightly_refusal_the_host_rules_and_the_console_fix()
    {
        var ad1259 = WhatsNew.AD1259[0];
        Assert.Equal("v1.1.17", ad1259.Version);
        Assert.Equal(ReleaseNotice.AD1259Version, ad1259.Version);
        Assert.Equal(5, ad1259.Lines.Count);
        Assert.Contains("Coop nightly", ad1259.Lines[0]);
        Assert.Contains("Coop 0.1.7 from the Steam Workshop", ad1259.Lines[0]);
        // Pause on join and the XP freeze are on by default; the clan cap and the PvP rules are only named, not switched on.
        Assert.StartsWith("The Server Tool now pauses the game while a player joins", ad1259.Lines[1]);
        Assert.Contains("Both are on by default", ad1259.Lines[1]);
        Assert.Contains("Max clans per kingdom and the PvP rules", ad1259.Lines[1]);
        Assert.StartsWith("On a small window the Console page", ad1259.Lines[2]);
        Assert.StartsWith("Stop server and Restart no longer hang", ad1259.Lines[3]);
        // The console input is UTF-8 now; the restart warnings are English, so the line names say, not them.
        Assert.StartsWith("Text you send to the server, like a say message on the Console page", ad1259.Lines[4]);
        Assert.DoesNotContain("restart warning", ad1259.Lines[4]);
        // The Coop and Friends tools take the same shared code in their own next release, not this one.
        Assert.DoesNotContain(WhatsNew.Friends, entry => entry.Lines.Contains(ad1259.Lines[1]));
    }

    [Fact]
    public void Taom_v1_2_15_lists_the_connect_safe_mode_and_crash_name_fixes()
    {
        var taom = WhatsNew.Taom.Single(entry => entry.Version == "v1.2.15");
        Assert.Equal(3, taom.Lines.Count);
        Assert.Contains(taom.Lines, line => line.Contains("second Bannerlord"));
        Assert.Contains(taom.Lines, line => line.Contains("Safe mode") && line.Contains("Plain TAOM"));
        Assert.Contains(taom.Lines, line => line.Contains("Better Exception Window"));
    }

    [Fact]
    public void Taom_v1_2_16_lists_the_host_rules_new_compat_console_and_license_lines()
    {
        var taom = WhatsNew.Taom[0];
        Assert.Equal(("v1.2.16", "v1.2.16"), (taom.Version, ReleaseNotice.TaomVersion));
        Assert.Equal(8, taom.Lines.Count);
        // Pause on join and the offline XP freeze are on by default now: the line says so plainly.
        Assert.Contains(taom.Lines, line => line.Contains("pauses the game while a player joins") && line.Contains("on by default")
            && line.Contains("Advanced: server rules"));
        // The host's MCM settings reach every player who joins with no setup: the line says so, not as an option.
        Assert.Contains(taom.Lines, line => line.Contains("Advanced: mod settings for players") && line.Contains("Looks mods"));
        Assert.Contains(taom.Lines, line => line.Contains("Players who join now use your MCM settings for the session, with no setup"));
        // The nav button reads Saves, not Server.
        Assert.DoesNotContain(taom.Lines, line => line.Contains("Server page"));
        Assert.Contains(taom.Lines, line => line.Contains("Bannerlord 1.5.7") && line.Contains("TAOM 2.0.29.5 on Bannerlord 1.4.8 with Coop 0.1.6"));
        Assert.Contains(taom.Lines, line => line.Contains("TAOM.CoopCompat 0.3.25") && line.Contains("host kit"));
        Assert.Contains(taom.Lines, line => line.Contains("one at a time") && line.Contains("may still arrive"));
        Assert.Contains(taom.Lines, line => line.Contains("Unblock DLLs"));
        Assert.Contains(WhatsNew.LicenseRotTools, taom.Lines);
    }

    [Fact]
    public void The_button_shows_this_version_first_then_the_ones_before_it()
    {
        WhatsNewEntry[] entries =
        [
            new("v1.2.47", ["f"]),
            new("v1.2.46", ["e"]),
            new("v1.2.44", ["c"]),
            new("v1.2.45", ["d"]),
            new("v1.2.43", ["b"]),
            new("v1.2.42", ["a"]),
            new("v1.2.41", ["z"]),
        ];
        // A newer entry than this copy is left out, the order is newest first, and at most five come.
        Assert.Equal(["v1.2.46", "v1.2.45", "v1.2.44", "v1.2.43", "v1.2.42"], WhatsNew.Recent(entries, "v1.2.46").Select(entry => entry.Version));
        Assert.Equal(["v1.2.43", "v1.2.42"], WhatsNew.Recent(entries, "v1.2.43", max: 2).Select(entry => entry.Version));
        Assert.Empty(WhatsNew.Recent(entries, "v1.2.40"));
        Assert.Empty(WhatsNew.Recent([], "v1.2.46"));

        // Unlike the popup after an update, the button does not care what was seen before.
        foreach (var (list, version) in new[] { (WhatsNew.Friends, ReleaseNotice.FriendsVersion), (WhatsNew.AD1259, ReleaseNotice.AD1259Version), (WhatsNew.Taom, ReleaseNotice.TaomVersion), (WhatsNew.Be, ReleaseNotice.BeVersion), (WhatsNew.Rot, ReleaseNotice.RotVersion) })
        {
            var shown = WhatsNew.Recent(list, version);
            Assert.Equal(version, shown[0].Version);
            Assert.Equal(Math.Min(5, list.Count), shown.Count);
            Assert.Empty(WhatsNew.Since(list, version));
        }
    }

    [Fact]
    public void Shows_what_came_after_the_last_seen_version_or_only_the_newest_the_first_time()
    {
        WhatsNewEntry[] entries =
        [
            new("v1.2.45", ["c"]),
            new("v1.2.44", ["b"]),
            new("v1.2.43", ["a"]),
        ];
        Assert.Equal(["v1.2.45"], WhatsNew.Since(entries, null).Select(entry => entry.Version));
        Assert.Equal(["v1.2.45", "v1.2.44"], WhatsNew.Since(entries, "v1.2.43").Select(entry => entry.Version));
        Assert.Empty(WhatsNew.Since(entries, "v1.2.45"));

        var folder = Path.Combine(Path.GetTempPath(), "whatsnew-" + Guid.NewGuid().ToString("N"));
        var before = WhatsNew.SettingsFolder;
        WhatsNew.SettingsFolder = folder;
        try
        {
            Assert.Null(WhatsNew.LastSeen("test-tool"));
            WhatsNew.MarkSeen("test-tool", "v1.2.44");
            Assert.Equal("v1.2.44", WhatsNew.LastSeen("test-tool"));
        }
        finally
        {
            WhatsNew.SettingsFolder = before;
            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void An_entry_has_no_picture_unless_one_is_given_and_Since_keeps_it()
    {
        // Every entry written as new(version, lines) stays as it was.
        var plain = new WhatsNewEntry("v1.2.47", ["a"]);
        Assert.Null(plain.Picture);
        Assert.All(WhatsNew.Friends.Concat(WhatsNew.AD1259).Concat(WhatsNew.Taom).Concat(WhatsNew.Be).Concat(WhatsNew.Rot),
            entry => Assert.True(entry.Picture is null
                || (WhatsNew.OpenPicture(WhatsNew.PictureFor(entry.Picture, "coodsz-host")) is not null
                    && WhatsNew.OpenPicture(WhatsNew.PictureFor(entry.Picture, "friends-launcher")) is not null),
                entry.Version + " has a picture that is not in Core"));

        // A client launcher and its server tool share one list: the Donate picture is each tool's own copy.
        foreach (var server in new[] { "coodsz-host", "ad1259-server", "taom-server", "be-server", "rot-server" })
            Assert.Equal(WhatsNew.DonatePictureServer, WhatsNew.PictureFor(WhatsNew.DonatePicture, server));
        foreach (var client in new[] { "friends-launcher", "ad1259-launcher", "taom-launcher", "be-launcher", "rot-launcher" })
            Assert.Equal(WhatsNew.DonatePictureClient, WhatsNew.PictureFor(WhatsNew.DonatePicture, client));
        Assert.Equal(WhatsNew.DonatePictureServer, WhatsNew.PictureFor(WhatsNew.DonatePictureServer, "friends-launcher"));
        Assert.Null(WhatsNew.PictureFor(null, "coodsz-host"));

        WhatsNewEntry[] entries = [new("v1.2.48", ["b"], WhatsNew.DonatePictureClient), plain];
        var shown = WhatsNew.Since(entries, "v1.2.46");
        Assert.Equal(WhatsNew.DonatePictureClient, shown[0].Picture);
        Assert.Null(shown[1].Picture);
        Assert.Equal(WhatsNew.DonatePictureServer, WhatsNew.Recent([plain with { Picture = WhatsNew.DonatePictureServer }], "v1.2.47")[0].Picture);
    }

    [Theory]
    [InlineData(WhatsNew.DonatePictureClient)]
    [InlineData(WhatsNew.DonatePictureServer)]
    public void Donate_pictures_are_in_Core_as_small_PNGs(string name)
    {
        using var stream = WhatsNew.OpenPicture(name);
        Assert.NotNull(stream);
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        var bytes = copy.ToArray();
        Assert.InRange(bytes.Length, 1, 150 * 1024);

        // A PNG: the signature, an 8 bit RGBA header, and image data that inflates to every row.
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, bytes[..8]);
        Assert.Equal("IHDR", System.Text.Encoding.ASCII.GetString(bytes, 12, 4));
        var width = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16));
        var height = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20));
        Assert.InRange(width, 50, 1000);
        Assert.InRange(height, 20, 1000);
        Assert.Equal(8, bytes[24]);
        Assert.Equal(6, bytes[25]);
        using var data = new MemoryStream();
        for (var at = 8; at < bytes.Length;)
        {
            var length = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(at));
            if (System.Text.Encoding.ASCII.GetString(bytes, at + 4, 4) == "IDAT")
                data.Write(bytes, at + 8, length);
            at += 12 + length;
        }
        data.Position = 0;
        using var inflate = new System.IO.Compression.ZLibStream(data, System.IO.Compression.CompressionMode.Decompress);
        using var rows = new MemoryStream();
        inflate.CopyTo(rows);
        Assert.Equal(height * (1 + width * 4), rows.Length);

        Assert.Equal(WhatsNew.DonatePictureLabel, WhatsNew.PictureLabel(name));
    }

    [Fact]
    public void An_unknown_picture_opens_as_null()
    {
        Assert.Null(WhatsNew.OpenPicture("Coodsz.WhatsNew.no-such-picture.png"));
        Assert.Null(WhatsNew.OpenPicture(""));
        Assert.Null(WhatsNew.OpenPicture(null));
        Assert.Equal(WhatsNew.PictureLabelOther, WhatsNew.PictureLabel("Coodsz.WhatsNew.no-such-picture.png"));
    }

    [Fact]
    public void Picture_labels_are_translated_into_every_language()
    {
        foreach (var language in Translator.Languages.Where(language => language.Code != Translator.English))
        {
            var table = Translator.Entries(language.Code);
            Assert.True(table.ContainsKey(WhatsNew.DonatePictureLabel), language.Code);
            Assert.True(table.ContainsKey(WhatsNew.PictureLabelOther), language.Code);
        }
    }

    [Fact]
    public void The_window_adds_a_picture_only_when_it_loads()
    {
        var source = SourceFiles.Read("src/BannerlordCoopLauncher/UiLanguage.cs").Replace("\r\n", "\n");
        // The Image is made only inside the check that the picture loaded, and a broken picture gives null instead of throwing.
        var check = source.IndexOf("if (WhatsNew.PictureFor(entry.Picture, _whatsNewTool) is { } name && LoadPicture(name) is { } picture)", StringComparison.Ordinal);
        var image = source.IndexOf("new Image", StringComparison.Ordinal);
        Assert.True(check > 0, "ShowWhatsNewWindow no longer checks that the picture loaded");
        Assert.True(image > check, "the Image is made before the picture is checked");
        Assert.Equal(image, source.LastIndexOf("new Image", StringComparison.Ordinal));
        Assert.Contains("StretchDirection = StretchDirection.DownOnly", source);
        var load = source[source.IndexOf("LoadPicture(string name)", StringComparison.Ordinal)..];
        load = load[..load.IndexOf("private static void ShowWhatsNewWindow(", StringComparison.Ordinal)];
        Assert.Contains("if (stream is null)\n                return null;", load);
        Assert.Contains("catch (Exception)", load);
    }
}
