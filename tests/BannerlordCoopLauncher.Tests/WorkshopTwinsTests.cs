using BannerlordCoopLauncher.Core.Modules;

namespace BannerlordCoopLauncher.Tests;

public class WorkshopTwinsTests : IDisposable
{
    private readonly string _content = Path.Combine(Path.GetTempPath(), "twins-" + Guid.NewGuid().ToString("N"), "261550");

    public WorkshopTwinsTests() => Directory.CreateDirectory(_content);

    public void Dispose() => Directory.Delete(Path.GetDirectoryName(_content)!, true);

    private void Item(string itemId, string moduleId, string version = "v2.4.2.248", string? name = null)
    {
        var folder = Path.Combine(_content, itemId);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "SubModule.xml"),
            $"<Module><Name value=\"{name ?? moduleId}\"/><Id value=\"{moduleId}\"/><Version value=\"{version}\"/></Module>");
    }

    // The player of Coodsz-Crash-Reports#31: official BUTR items plus re-uploads with the same id and version.
    private void ChadnarsWorkshop()
    {
        Item("2859188632", "Bannerlord.Harmony", name: "Harmony");
        Item("3747619959", "Bannerlord.Harmony", name: "Harmony");
        Item("2859232415", "Bannerlord.ButterLib", "v2.12.0", "ButterLib");
        Item("3747620149", "Bannerlord.ButterLib", "v2.12.0", "ButterLib");
        Item("3770450698", "Coop", "v0.1.6", "Bannerlord Coop");
    }

    [Fact]
    public void A_re_upload_with_the_same_id_and_version_is_a_twin_and_the_official_item_is_kept()
    {
        ChadnarsWorkshop();

        var twins = WorkshopTwins.Find(new[] { _content }, new[] { "Bannerlord.Harmony", "Bannerlord.ButterLib", "Coop" });

        Assert.Equal(new[] { "Bannerlord.ButterLib", "Bannerlord.Harmony" }, twins.Select(twin => twin.ModuleId));
        var harmony = twins.Single(twin => twin.ModuleId == "Bannerlord.Harmony");
        Assert.Equal(new[] { "2859188632", "3747619959" }, harmony.ItemIds);
        Assert.Equal("2859188632", harmony.KeepId);
        Assert.Equal(new[] { "3747619959" }, harmony.Unsubscribe);
    }

    [Fact]
    public void A_mod_that_is_not_on_the_setup_is_left_alone()
    {
        ChadnarsWorkshop();

        Assert.Empty(WorkshopTwins.Find(new[] { _content }, new[] { "Coop" }));
    }

    [Fact]
    public void The_game_log_names_the_item_the_game_loaded()
    {
        ChadnarsWorkshop();
        const string log = @"[20:58:37.202] Loading assembly: C:\Program Files (x86)\Steam\steamapps\workshop\content\261550\3747619959\bin\Win64_Shipping_Client\0Harmony.dll";

        var harmony = WorkshopTwins.Find(new[] { _content }, new[] { "Bannerlord.Harmony" }, gameLog: () => log).Single();

        Assert.Equal("3747619959", harmony.LoadedId);
        Assert.Contains("The game loaded 3747619959 in the last game start.", WorkshopTwins.Describe(harmony));
        Assert.Contains("Unsubscribe https://steamcommunity.com/sharedfiles/filedetails/?id=3747619959 on the Workshop and keep https://steamcommunity.com/sharedfiles/filedetails/?id=2859188632, the official one", WorkshopTwins.Describe(harmony));
    }

    [Fact]
    public void The_log_is_not_read_when_there_is_no_twin()
    {
        Item("2859188632", "Bannerlord.Harmony");
        var read = false;

        WorkshopTwins.Find(new[] { _content }, new[] { "Bannerlord.Harmony" }, gameLog: () => { read = true; return null; });

        Assert.False(read);
    }

    [Fact]
    public void An_unknown_mod_in_two_items_asks_the_player_to_keep_one()
    {
        Item("111", "SomeMod", "v1.0.0");
        Item("222", "SomeMod", "v1.0.1");

        var twin = WorkshopTwins.Find(new[] { _content }, new[] { "SomeMod" }).Single();

        Assert.Null(twin.KeepId);
        Assert.Contains("Keep the one you use", WorkshopTwins.Describe(twin));
        Assert.Contains("can pick either", WorkshopTwins.Describe(twin));
    }

    [Fact]
    public void The_tool_knows_which_item_to_keep_for_its_own_mods()
    {
        Item("111", "SomeMod");
        Item("222", "SomeMod");

        var twin = WorkshopTwins.Find(new[] { _content }, new[] { "SomeMod" },
            new Dictionary<string, string> { ["SomeMod"] = "222" }).Single();

        Assert.Equal("222", twin.KeepId);
        Assert.Equal(new[] { "111" }, twin.Unsubscribe);
        Assert.Contains("the one this tool uses", WorkshopTwins.Describe(twin));
        Assert.DoesNotContain("official", WorkshopTwins.Describe(twin));
    }

    [Fact]
    public void The_official_item_wins_even_when_the_tool_picked_the_re_upload()
    {
        ChadnarsWorkshop();

        var harmony = WorkshopTwins.Find(new[] { _content }, new[] { "Bannerlord.Harmony" },
            new Dictionary<string, string> { ["Bannerlord.Harmony"] = "3747619959" }).Single();

        Assert.Equal("2859188632", harmony.KeepId);
        Assert.Equal(new[] { "3747619959" }, harmony.Unsubscribe);
    }

    [Fact]
    public void A_known_item_that_is_not_installed_leaves_the_choice_to_the_player()
    {
        Item("111", "SomeMod");
        Item("222", "SomeMod");

        var twin = WorkshopTwins.Find(new[] { _content }, new[] { "SomeMod" },
            new Dictionary<string, string> { ["SomeMod"] = "999" }).Single();

        Assert.Null(twin.KeepId);
        Assert.Empty(twin.Unsubscribe);
    }

    [Fact]
    public void Without_the_official_item_the_tool_s_own_pick_is_kept()
    {
        Item("3747619959", "Bannerlord.Harmony");
        Item("3999999999", "Bannerlord.Harmony");

        var harmony = WorkshopTwins.Find(new[] { _content }, new[] { "Bannerlord.Harmony" },
            new Dictionary<string, string> { ["Bannerlord.Harmony"] = "3999999999" }).Single();

        Assert.Equal("3999999999", harmony.KeepId);
        Assert.Contains("the one this tool uses", WorkshopTwins.Describe(harmony));
    }

    [Fact]
    public void A_crash_log_tail_without_the_loading_lines_falls_back_to_the_newest_log()
    {
        ChadnarsWorkshop();
        var tail = string.Concat(Enumerable.Repeat("[20:58:38.892] Loading font\n", 1000));
        const string newest = @"Loading assembly: C:\Steam\steamapps\workshop\content\261550\3747619959\bin\Win64_Shipping_Client\0Harmony.dll";

        var harmony = WorkshopTwins.Find(new[] { _content }, new[] { "Bannerlord.Harmony" },
            gameLog: () => tail, fallbackLog: () => newest).Single();

        Assert.Equal("3747619959", harmony.LoadedId);
    }

    [Fact]
    public void The_crash_log_wins_over_the_newest_log()
    {
        ChadnarsWorkshop();
        const string crash = @"261550\2859188632\bin\0Harmony.dll";
        const string newest = @"261550\3747619959\bin\0Harmony.dll";

        var harmony = WorkshopTwins.Find(new[] { _content }, new[] { "Bannerlord.Harmony" },
            gameLog: () => crash, fallbackLog: () => newest).Single();

        Assert.Equal("2859188632", harmony.LoadedId);
    }

    [Fact]
    public void Module_ids_match_whatever_their_case()
    {
        Item("2859188632", "Bannerlord.Harmony");
        Item("3747619959", "bannerlord.harmony");

        var twin = WorkshopTwins.Find(new[] { _content }, new[] { "BANNERLORD.HARMONY" }).Single();

        Assert.Equal("2859188632", twin.KeepId);
    }

    [Fact]
    public void A_log_that_names_both_items_names_neither()
    {
        ChadnarsWorkshop();
        const string log = @"261550\2859188632\bin\0Harmony.dll
261550\3747619959\bin\0Harmony.dll";

        Assert.Null(WorkshopTwins.Find(new[] { _content }, new[] { "Bannerlord.Harmony" }, gameLog: () => log).Single().LoadedId);
    }

    [Fact]
    public void An_item_Steam_is_still_filling_is_seen_once_its_SubModule_xml_arrives()
    {
        Item("2859188632", "Bannerlord.Harmony");
        Directory.CreateDirectory(Path.Combine(_content, "3747619959"));
        Assert.Empty(WorkshopTwins.Find(new[] { _content }, new[] { "Bannerlord.Harmony" }));

        File.WriteAllText(Path.Combine(_content, "3747619959", "SubModule.xml"),
            "<Module><Name value=\"Harmony\"/><Id value=\"Bannerlord.Harmony\"/><Version value=\"v2.4.2.248\"/></Module>");

        Assert.Single(WorkshopTwins.Find(new[] { _content }, new[] { "Bannerlord.Harmony" }));
    }

    [Fact]
    public void Broken_and_non_item_folders_are_skipped()
    {
        Item("2859188632", "Bannerlord.Harmony");
        Directory.CreateDirectory(Path.Combine(_content, "3747619959"));
        File.WriteAllText(Path.Combine(_content, "3747619959", "SubModule.xml"), "not xml");
        Directory.CreateDirectory(Path.Combine(_content, "backup"));
        File.WriteAllText(Path.Combine(_content, "backup", "SubModule.xml"),
            "<Module><Name value=\"Harmony\"/><Id value=\"Bannerlord.Harmony\"/><Version value=\"v1\"/></Module>");

        Assert.Empty(WorkshopTwins.Find(new[] { _content }, new[] { "Bannerlord.Harmony" }));
    }

    [Fact]
    public void No_engine_log_folder_means_no_client_log()
    {
        Assert.Null(WorkshopTwins.LatestClientLog(Path.Combine(_content, "no such folder")));
    }

    [Fact]
    public void A_crash_explanation_starts_with_the_twins()
    {
        ChadnarsWorkshop();
        var twins = WorkshopTwins.Find(new[] { _content }, new[] { "Bannerlord.Harmony" });
        var generic = new[] { "The game closed while starting. The log does not name one extra mod." };

        var lines = WorkshopTwins.PointCrashAt(generic, twins);

        Assert.Equal(WorkshopTwins.CrashLead, lines[0]);
        Assert.StartsWith("Harmony is in more than one Steam Workshop item", lines[1]);
        Assert.Equal(generic[0], lines[^1]);
    }

    [Fact]
    public void No_crash_or_no_twin_leaves_the_explanation_as_it_is()
    {
        ChadnarsWorkshop();
        var twins = WorkshopTwins.Find(new[] { _content }, new[] { "Bannerlord.Harmony" });
        var noCrash = new[] { "The game closed normally. This test did not see a startup crash." };
        var crash = new[] { "The game closed while starting." };

        Assert.Same(noCrash, WorkshopTwins.PointCrashAt(noCrash, twins));
        Assert.Same(crash, WorkshopTwins.PointCrashAt(crash, Array.Empty<WorkshopTwin>()));
    }

    [Fact]
    public void A_new_subscription_is_seen_on_the_next_check()
    {
        Item("2859188632", "Bannerlord.Harmony");
        Assert.Empty(WorkshopTwins.Find(new[] { _content }, new[] { "Bannerlord.Harmony" }));

        Item("3747619959", "Bannerlord.Harmony");

        Assert.Single(WorkshopTwins.Find(new[] { _content }, new[] { "Bannerlord.Harmony" }));
    }

    [Fact]
    public void The_latest_client_log_skips_server_and_error_logs()
    {
        var logs = Path.Combine(Path.GetDirectoryName(_content)!, "logs");
        Directory.CreateDirectory(logs);
        File.WriteAllText(Path.Combine(logs, "rgl_log_1.txt"), "Command Args: /singleplayer _MODULES_*Native*_MODULES_");
        File.SetLastWriteTimeUtc(Path.Combine(logs, "rgl_log_1.txt"), DateTime.UtcNow.AddMinutes(-5));
        File.WriteAllText(Path.Combine(logs, "rgl_log_2.txt"), "Command Args: /singleplayer _MODULES_*Native*DedicatedServer.Windows*Coop*_MODULES_");
        File.WriteAllText(Path.Combine(logs, "rgl_log_errors_3.txt"), "Command Args: /singleplayer errors");

        Assert.Equal("Command Args: /singleplayer _MODULES_*Native*_MODULES_", WorkshopTwins.LatestClientLog(logs));
    }
}

[Collection(TranslatorLanguage.Name)]
public sealed class WorkshopTwinsLanguageTests
{
    [Fact]
    public void The_twin_line_is_translated_with_the_item_ids_filled_in()
    {
        var twin = new WorkshopTwin("Bannerlord.Harmony", "Harmony", new[] { "2859188632", "3747619959" }, "2859188632", "3747619959");
        var before = BannerlordCoopLauncher.Core.Localization.Translator.Current;
        try
        {
            BannerlordCoopLauncher.Core.Localization.Translator.Use("de");
            Assert.Equal(
                "Harmony ist in mehr als einem Steam-Workshop-Objekt (2859188632, 3747619959). Das Spiel hat beim letzten Start 3747619959 geladen. Deabonniere https://steamcommunity.com/sharedfiles/filedetails/?id=3747619959 im Workshop und behalte https://steamcommunity.com/sharedfiles/filedetails/?id=2859188632, das offizielle, dann klicke auf „Mods prüfen“.",
                BannerlordCoopLauncher.Core.Localization.Translator.T(WorkshopTwins.Describe(twin)));
            Assert.NotEqual(WorkshopTwins.CrashLead, BannerlordCoopLauncher.Core.Localization.Translator.T(WorkshopTwins.CrashLead));
        }
        finally
        {
            BannerlordCoopLauncher.Core.Localization.Translator.Use(before);
        }
    }
}
