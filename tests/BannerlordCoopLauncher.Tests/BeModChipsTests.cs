using BannerlordCoopLauncher.Core.Modules;
using BurningEmpires.Core;
using TaomCoop.Core;

namespace BannerlordCoopLauncher.Tests;

// The Burning Empires client's mods page shows the same status column as Coodsz Coop and Friends (the Ready
// pill, the list status chip, the count chips, this PC's result and the Report link), from the Burning
// Empires list and the reports sent with tool be, never from the Coop and Friends list.
public sealed class BeModChipsTests
{
    // The list as the tally writes it: counts on the hand entries, reported mods off the list under "community".
    private const string Tallied = """
        {
          "kind": "CoodszBeCompat",
          "schemaVersion": 2,
          "be": "2.0.2",
          "updated": "2026-10-26",
          "mods": [
            { "id": "SafeMod", "name": "Safe mod", "workshopId": "111", "tag": "safe", "source": "coodsz-coop", "checked": "2026-10-20", "note": "Tested with a player joining.", "works": 4, "broken": 1, "lastReport": "2026-10-25" },
            { "id": "", "name": "Named only", "workshopId": "", "tag": "risky", "source": "coodsz-notes", "note": "Changes battles.", "works": 2, "broken": 0 },
            { "id": "", "name": "By workshop", "workshopId": "222", "tag": "broken", "source": "coodsz-notes", "note": "Breaks joining.", "broken": 3, "works": 0 },
            { "id": "SingleOnly", "name": "Single only", "tag": "reported", "source": "be-discord", "note": "" },
            { "id": "Loader", "name": "Loader", "tag": "loads", "source": "server-loads", "note": "" },
            { "id": "Shared", "name": "Shared", "tag": "everyone", "source": "coodsz-notes", "note": "" }
          ],
          "community": [
            { "id": "RTSCamera", "name": "RTS Camera", "works": 2, "broken": 1, "lastReport": "2026-10-24" },
            { "id": "NobodyYet", "name": "Nobody yet", "works": 0, "broken": 0 }
          ]
        }
        """;

    private static BeCompatList List() => BeCompatList.Parse(Tallied);

    [Fact]
    public void The_tally_counts_are_read_from_the_list_entries_and_the_community_array()
    {
        var list = List();
        Assert.Equal((4, 1), Counts(list.CountsFor("SafeMod")));
        Assert.Equal((4, 1), Counts(list.CountsFor("OtherId", "111")));
        Assert.Equal((2, 0), Counts(list.CountsFor("NotTheId", null, "Named only")));
        Assert.Equal((0, 3), Counts(list.CountsFor("Unknown.Id", "222")));
        Assert.Equal((2, 1), Counts(list.CountsFor("rtscamera")));
        // Nobody reported these, so no count chips.
        Assert.Null(list.CountsFor("NobodyYet"));
        Assert.Null(list.CountsFor("SingleOnly"));
        Assert.Null(list.CountsFor("NotOnTheList"));
        // The hand entries still read as before, and a community mod gets no list badge.
        Assert.Equal(6, list.Mods.Count);
        Assert.Null(list.Find("RTSCamera"));
        // The built in list has no reports yet.
        Assert.All(BeCompatList.BuiltIn().Mods, entry => Assert.Null(BeCompatList.BuiltIn().CountsFor(entry.Id, entry.WorkshopId, entry.Name)));
    }

    private static (int, int) Counts(BeModCounts? counts) => counts == null ? (-1, -1) : (counts.Works, counts.Broken);

    [Theory]
    [InlineData("SafeMod", BeModChips.Works, "Tested safe")]
    [InlineData("Shared", BeModChips.Works, "Everyone needs it")]
    [InlineData("SingleOnly", BeModChips.Reported, "Works in Burning Empires, not tested in Coop")]
    [InlineData("Loader", BeModChips.Reported, "Loads on the server")]
    [InlineData("TAOM", BeModChips.Unknown, "Unknown, not tested")]
    public void The_status_chip_takes_its_color_and_words_from_the_burning_empires_tag(string id, string kind, string text)
    {
        var chip = BeModChips.Status(List(), id, null, null, reportTip: false)!;
        Assert.Equal(kind, chip.Kind);
        Assert.Equal(text, chip.Text);
    }

    [Fact]
    public void Each_tag_has_the_coop_and_friends_chip_color()
    {
        Assert.Equal(BeModChips.Works, BeModChips.KindOf(CompatTag.Safe));
        Assert.Equal(BeModChips.Works, BeModChips.KindOf(CompatTag.Everyone));
        Assert.Equal(BeModChips.Risky, BeModChips.KindOf(CompatTag.Risky));
        Assert.Equal(BeModChips.Broken, BeModChips.KindOf(CompatTag.Broken));
        Assert.Equal(BeModChips.Reported, BeModChips.KindOf(CompatTag.Reported));
        Assert.Equal(BeModChips.Reported, BeModChips.KindOf(CompatTag.Loads));
        Assert.Equal(BeModChips.Unknown, BeModChips.KindOf(CompatTag.Unknown));
        Assert.Equal(BeModChips.Risky, BeModChips.Status(List(), "x", null, "Named only", false)!.Kind);
        Assert.Equal(BeModChips.Broken, BeModChips.Status(List(), "y", "222", null, false)!.Kind);
    }

    [Fact]
    public void A_listed_mod_shows_its_note_source_and_counts_and_the_report_line_only_with_the_endpoint()
    {
        var chip = BeModChips.Status(List(), "SafeMod", null, null, reportTip: true)!;
        Assert.Equal("4 say works", chip.WorksChip);
        Assert.Equal("1 say broken", chip.BrokenChip);
        var lines = chip.Tip.Split('\n');
        Assert.Equal("Tested with a player joining.", lines[0]);
        Assert.Contains("Tested in Coop by Coodsz, checked 2026-10-20.", lines);
        Assert.Contains(ModReports.CountsLine(4, 1), lines);
        Assert.Equal(ModReports.ClickTip, lines[^1]);

        Assert.DoesNotContain(ModReports.ClickTip, BeModChips.Status(List(), "SafeMod", null, null, reportTip: false)!.Tip);
    }

    [Fact]
    public void A_mod_off_the_list_is_unknown_and_shows_the_player_counts_once_someone_reported_it()
    {
        var untested = BeModChips.Status(List(), "NotOnTheList", null, null, reportTip: false)!;
        Assert.Equal(UnknownModBadge.Text, untested.Text);
        Assert.Equal(UnknownModBadge.Tip, untested.Tip);
        Assert.Null(untested.WorksChip);
        Assert.Null(untested.BrokenChip);

        var reported = BeModChips.Status(List(), "RTSCamera", null, null, reportTip: false)!;
        Assert.Equal(BeModChips.Unknown, reported.Kind);
        Assert.Equal("Unknown", reported.Text);
        Assert.Equal("2 say works", reported.WorksChip);
        Assert.Equal("1 say broken", reported.BrokenChip);
        Assert.Equal(ModReports.CountsLine(2, 1), reported.Tip);
    }

    [Theory]
    [InlineData("BurningEmpires")]
    [InlineData("BurningEmpires_Elephants")]
    [InlineData("BurningEmpires.CoopCompat")]
    [InlineData("Coop")]
    [InlineData("Native")]
    [InlineData("")]
    public void The_core_coop_and_official_modules_get_no_status_chip(string id) =>
        Assert.Null(BeModChips.Status(List(), id, null, null, reportTip: true));

    [Fact]
    public void The_row_pill_says_ready_off_missing_or_wrong_version_like_coop_and_friends()
    {
        Assert.Equal(("Ready", "Ready"), BeModChips.ExtraMark(enabled: true, installed: true));
        Assert.Equal(("Missing", "Missing"), BeModChips.ExtraMark(enabled: true, installed: false));
        Assert.Equal(("Off", "Off"), BeModChips.ExtraMark(enabled: false, installed: true));
        Assert.Equal(("Off", "Off"), BeModChips.ExtraMark(enabled: false, installed: false));
        Assert.Equal(("Ready", "Ready"), BeModChips.CoreMark(found: true, rightVersion: true));
        Assert.Equal(("Wrong version", "Wrong version"), BeModChips.CoreMark(found: true, rightVersion: false));
        // Red in every tool, since a wrong version blocks joining.
        Assert.Equal(BannerlordCoopLauncher.Core.Modules.ModChipKind.Bad, BannerlordCoopLauncher.Core.Modules.ModChips.StatusKind(BeModChips.CoreMark(found: true, rightVersion: false).Kind));
        Assert.Equal(("Missing", "Missing"), BeModChips.CoreMark(found: false, rightVersion: true));
    }

    [Fact]
    public void A_hand_entry_and_a_community_entry_with_the_same_id_count_once_from_the_hand_entry()
    {
        var list = BeCompatList.Parse("""
            { "kind": "CoodszBeCompat", "schemaVersion": 2,
              "mods": [ { "id": "Twin", "name": "Twin", "tag": "safe", "works": 5, "broken": 1 } ],
              "community": [ { "id": "twin", "name": "Twin", "works": 9, "broken": 9 } ] }
            """);
        Assert.Equal((5, 1), Counts(list.CountsFor("Twin")));
        Assert.Equal("5 say works", BeModChips.Status(list, "Twin", null, null, false)!.WorksChip);

        // The tally counts a report on the hand entry when there is one, so a stale community entry of the same
        // id never adds to a hand entry, also one nobody reported yet.
        var unreported = BeCompatList.Parse("""
            { "kind": "CoodszBeCompat", "schemaVersion": 2,
              "mods": [ { "id": "Twin", "name": "Twin", "tag": "safe" } ],
              "community": [ { "id": "Twin", "works": 3, "broken": 0 } ] }
            """);
        Assert.Null(unreported.CountsFor("Twin"));
    }

    [Fact]
    public void A_server_row_report_carries_tool_be_the_mod_and_the_servers_mods_in_order_with_versions()
    {
        // The working copy the mods page shows: one mod before Coop that loads, one after Burning Empires that
        // loads, one turned off and one turned on that is not on this PC (not staged).
        var shown = new TaomSetup
        {
            Name = "Edited, not saved",
            Extras =
            {
                new TaomExtraMod { Name = "Before", ModuleId = "BeforeMod", Slot = BeLoadOrder.SlotBeforeCoop },
                new TaomExtraMod { Name = "After", ModuleId = "AfterMod", Slot = BeLoadOrder.SlotAfterBe },
                new TaomExtraMod { Name = "Off", ModuleId = "OffMod", Slot = BeLoadOrder.SlotAfterBe, Enabled = false },
                new TaomExtraMod { Name = "Missing", ModuleId = "MissingMod", Slot = BeLoadOrder.SlotAfterBe }
            }
        };
        var staged = new HashSet<string>(["BeforeMod", "AfterMod", "OffMod"], StringComparer.OrdinalIgnoreCase);
        var order = BeModChips.ServerReportOrder(shown, staged.Contains, id => "v-" + id);
        var ids = order.Select(mod => mod.Id).ToList();

        Assert.Contains("BeforeMod", ids);
        Assert.Contains("AfterMod", ids);
        Assert.DoesNotContain("OffMod", ids);
        Assert.DoesNotContain("MissingMod", ids);
        Assert.Equal(BeStack.ServerModules.ToList(), ids.Where(id => BeStack.ServerModules.Contains(id)).ToList());
        Assert.True(ids.IndexOf("BeforeMod") < ids.IndexOf(BeStack.CoopModuleId));
        Assert.True(ids.IndexOf(BeStack.BeModuleId) < ids.IndexOf("AfterMod"));
        Assert.All(order, mod => Assert.Equal("v-" + mod.Id, mod.Version));

        var report = BeModChips.RowReport("AfterMod", "After", "v-AfterMod", order);
        Assert.Equal("be", report.Tool);
        Assert.Equal(("AfterMod", "After", "v-AfterMod"), (report.Id, report.Name, report.Version));
        Assert.Same(order, report.ServerOrder);
    }

    [Fact]
    public void A_core_rows_version_comes_from_the_folder_start_server_copies_since_the_mod_scan_leaves_butr_out()
    {
        var root = Directory.CreateTempSubdirectory("be-core-folder-").FullName;
        try
        {
            var library = Path.Combine(root, "steam");
            var workshop = Path.Combine(library, "steamapps", "workshop", "content", "261550");
            var harmony = Path.Combine(workshop, BeStack.Harmony.WorkshopId);
            Directory.CreateDirectory(harmony);
            File.WriteAllText(Path.Combine(harmony, "SubModule.xml"),
                "<Module><Name value=\"Harmony\"/><Id value=\"Bannerlord.Harmony\"/><Version value=\"v2.4.2.248\"/></Module>");
            var game = Path.Combine(root, "game");
            var compat = Path.Combine(root, "tool", "BurningEmpires.CoopCompat");
            var sources = new BeCoreSources(game, [library], compat, "");

            Assert.Equal(harmony, BeModChips.CoreFolder("Bannerlord.Harmony", sources));
            Assert.Equal(compat, BeModChips.CoreFolder(BeStack.CompatModuleId, sources));
            Assert.Equal(Path.Combine(game, "Modules", BeStack.BeModuleId), BeModChips.CoreFolder(BeStack.BeModuleId, sources));
            Assert.Equal(Path.Combine(game, "Modules", BeStack.ElephantsModuleId), BeModChips.CoreFolder(BeStack.ElephantsModuleId, sources));
            Assert.Null(BeModChips.CoreFolder("SomeExtra", sources));
            Assert.Null(BeModChips.CoreFolder(BeStack.ButterLib.ModuleId, sources));
            // MCM comes from Nexus Mods into the game's Modules folder, where Start server copies it from.
            Assert.Equal(Path.Combine(game, "Modules", BeStack.Mcm.ModuleId), BeModChips.CoreFolder(BeStack.Mcm.ModuleId, sources));
            Assert.Null(BeModChips.CoreFolder(BeStack.Mcm.ModuleId, sources with { GameRoot = "" }));

            // The server tool's mod scan (HostMods) never lists the BUTR modules, so their rows had no version.
            var scanned = BannerlordCoopLauncher.Core.Profile.CustomModCatalog.Scan(Path.Combine(game, "Modules"),
                [new BannerlordCoopLauncher.Core.Steam.WorkshopLibrary(workshop, new Dictionary<string, long>(), true)]);
            Assert.DoesNotContain(scanned, mod => mod.ModuleId == "Bannerlord.Harmony");
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void The_tools_wire_the_chips_and_reports_to_the_burning_empires_list()
    {
        // What the behaviour tests above cannot reach: which list and which helpers the windows use.
        var client = SourceFiles.Window(Root(), "BurningEmpiresLauncher");
        Assert.DoesNotContain("CommunityCompat.Current.Find", client);
        Assert.Contains("_compat.CountsFor(moduleId, workshopId, name)", client);
        Assert.Contains("Tool = \"be\",", SourceFiles.Method(client, "private Coodsz.Ui.ModReportRequest ReportRequest("));

        var host = SourceFiles.Window(Root(), "BurningEmpiresHost");
        Assert.DoesNotContain("CommunityCompat.Current.Find", host);
        Assert.DoesNotContain("ReportModules()", SourceFiles.Method(host, "private void ShowModSetups("));
        Assert.Contains("BeModChips.ServerReportOrder(shown,", SourceFiles.Method(host, "private void ShowModSetups("));
        Assert.Contains("BeModChips.RowReport(moduleId, name, version, loadOrder)", SourceFiles.Method(host, "private FrameworkElement? ReportChips("));
        Assert.Contains("Tool = report.Tool,", host);
        Assert.Contains("Coodsz.Ui.ModReportUi.RowLink(this,", host);
        Assert.Contains("BeModChips.CoreFolder(moduleId, _rowSources)", SourceFiles.Method(host, "private string RowVersion("));
        Assert.Contains("<ContentControl Content=\"{Binding Reports}\"", SourceFiles.Read("src/BurningEmpiresHost/MainWindow.xaml"));

        Assert.Equal("Coodsz/Coodsz-BurningEmpires", BeCompatList.Repository);
        Assert.Contains("BE_GITHUB_REPO   var, default Coodsz/Coodsz-BurningEmpires", SourceFiles.Read("server/mod-reports-worker/src/index.js"));
    }

    [Fact]
    public void The_core_rows_get_no_tested_chip_from_ModderLords_records()
    {
        // His records test plain Coop, not the Burning Empires stack (the TAOM live test showed
        // "Tested: broken" on TAOM.CoopCompat), so only the extra mods get his chip.
        var records = CompatRecords.Parse($$"""
            { "schemaVersion": 1, "records": [
              { "Id": "{{BeStack.CompatModuleId}}", "Verdict": "Broken" },
              { "Id": "{{BeStack.ElephantsModuleId}}", "Verdict": "Broken" },
              { "Id": "SafeMod", "Verdict": "Broken" } ] }
            """)!;
        Assert.Null(BeModChips.RecordsChip(records, BeStack.CompatModuleId, "v1.0.0"));
        Assert.Null(BeModChips.RecordsChip(records, BeStack.ElephantsModuleId, "v1.0.0"));
        Assert.Equal("Broken", BeModChips.RecordsChip(records, "SafeMod", "v1.0.0")?.Kind);
        Assert.Null(BeModChips.RecordsChip(records, "NotInTheRecords", "v1.0.0"));
        // Both tools take the row's chip from it.
        foreach (var tool in new[] { "BurningEmpiresLauncher", "BurningEmpiresHost" })
            Assert.Contains("BeModChips.RecordsChip(CompatRecords.Current, moduleId, version)", SourceFiles.Read(Path.Combine("src", tool, "MainWindow.Reports.cs")));
    }

    private static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "BannerlordCoopLauncher.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
