using System.Text;
using System.Xml.Linq;
using BannerlordCoopLauncher.Core.Keep;
using BannerlordCoopLauncher.Core.Localization;
using BannerlordCoopLauncher.Core.Updates;
using CoodszHost.Services;

namespace BannerlordCoopLauncher.Tests;

// The RBM settings card: text edits of RBM's config.xml that keep everything else byte for byte,
// the Coop friendly values, when the card shows, and its text in every language.
public sealed class RbmSettingsCardTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "rbm-card-" + Guid.NewGuid().ToString("N"));

    public RbmSettingsCardTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, recursive: true);
    }

    // Shaped like the file RBM 4.5 writes: BOM, CRLF, two space indent, version 2, unknown elements.
    private const string RealStyle =
        "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n"
        + "<Config version=\"2\">\r\n"
        + "  <RBMTournament>\r\n"
        + "    <Enabled>1</Enabled>\r\n"
        + "  </RBMTournament>\r\n"
        + "  <RBMAI>\r\n"
        + "    <Enabled>1</Enabled>\r\n"
        + "    <PostureEnabled>1</PostureEnabled>\r\n"
        + "  </RBMAI>\r\n"
        + "  <RBMCombat>\r\n"
        + "    <Enabled>1</Enabled>\r\n"
        + "    <Global>\r\n"
        + "      <ArmorMultiplier>2</ArmorMultiplier>\r\n"
        + "    </Global>\r\n"
        + "  </RBMCombat>\r\n"
        + "  <!-- a comment RBM never wrote -->\r\n"
        + "  <RBMCampaign>\r\n"
        + "    <Enabled>0</Enabled>\r\n"
        + "    <BuildingCostMultiplier>250</BuildingCostMultiplier>\r\n"
        + "    <CampaignStartingGoldMultiplier>5</CampaignStartingGoldMultiplier>\r\n"
        + "    <VillageProductionMultiplier>0.5</VillageProductionMultiplier>\r\n"
        + "    <WorkshopProductionMultiplier>1</WorkshopProductionMultiplier>\r\n"
        + "    <KingdomCaravansEnabled>1</KingdomCaravansEnabled>\r\n"
        + "    <CaravanInvestmentEnabled>1</CaravanInvestmentEnabled>\r\n"
        + "    <DeserterRaidersEnabled>1</DeserterRaidersEnabled>\r\n"
        + "    <LordEquipmentUpgradeEnabled>1</LordEquipmentUpgradeEnabled>\r\n"
        + "    <RecruitDrawsFromSettlementStock>1</RecruitDrawsFromSettlementStock>\r\n"
        + "    <TroopUpgradeRequireSupplyTown>1</TroopUpgradeRequireSupplyTown>\r\n"
        + "    <SomethingNewer>abc</SomethingNewer>\r\n"
        + "  </RBMCampaign>\r\n"
        + "  <DeveloperMode>0</DeveloperMode>\r\n"
        + "  <LastSeenChangelogVersion>v4.5.4</LastSeenChangelogVersion>\r\n"
        + "</Config>";

    private string WriteFile(string text, bool bom = true)
    {
        var path = Path.Combine(_folder, "config.xml");
        File.WriteAllBytes(path, (bom ? new byte[] { 0xEF, 0xBB, 0xBF } : []).Concat(Encoding.UTF8.GetBytes(text)).ToArray());
        return path;
    }

    private static string ReadRaw(string path) => Encoding.UTF8.GetString(File.ReadAllBytes(path));

    [Fact]
    public void Saving_one_value_changes_only_that_value_byte_for_byte()
    {
        var path = WriteFile(RealStyle);
        var before = File.ReadAllBytes(path);
        var changed = RbmConfigFile.Save(path, new Dictionary<RbmOption, string> { [RbmOptions.LordEquipment] = "0" });

        Assert.Equal(new[] { "RBMCampaign/LordEquipmentUpgradeEnabled" }, changed);
        var expected = before.Take(3).Concat(Encoding.UTF8.GetBytes(RealStyle.Replace(
            "<LordEquipmentUpgradeEnabled>1</LordEquipmentUpgradeEnabled>", "<LordEquipmentUpgradeEnabled>0</LordEquipmentUpgradeEnabled>"))).ToArray();
        Assert.Equal(expected, File.ReadAllBytes(path));
        // The first save keeps the old file next to it.
        Assert.Equal(before, File.ReadAllBytes(RbmConfigFile.BackupPath(path)));
        Assert.False(File.Exists(path + ".coodsz-tmp"));
    }

    [Fact]
    public void Every_card_value_edits_in_place_and_reads_back()
    {
        var path = WriteFile(RealStyle);
        var values = new Dictionary<RbmOption, string>
        {
            [RbmOptions.Campaign] = "1", [RbmOptions.Ai] = "0", [RbmOptions.Combat] = "0", [RbmOptions.Tournament] = "0",
            [RbmOptions.LordEquipment] = "0", [RbmOptions.KingdomCaravans] = "0", [RbmOptions.CaravanInvestment] = "0",
            [RbmOptions.DeserterRaiders] = "0", [RbmOptions.RecruitStock] = "0", [RbmOptions.UpgradeSupplyTown] = "0",
            [RbmOptions.WorkshopProduction] = "1.5", [RbmOptions.VillageProduction] = "0.75", [RbmOptions.BuildingCost] = "100",
            [RbmOptions.StartingGold] = "2",
        };
        Assert.Equal(14, RbmConfigFile.Save(path, values).Count);
        var read = RbmConfigFile.Read(path);
        Assert.True(read.Usable);
        Assert.Empty(read.Missing);
        foreach (var (option, value) in values)
            Assert.Equal(value, read.Values[option]);

        // Unknown elements, nested settings, comments and the root stay as they were.
        var raw = ReadRaw(path);
        Assert.Contains("<SomethingNewer>abc</SomethingNewer>", raw);
        Assert.Contains("      <ArmorMultiplier>2</ArmorMultiplier>\r\n", raw);
        Assert.Contains("<!-- a comment RBM never wrote -->", raw);
        Assert.Contains("<Config version=\"2\">", raw);
        Assert.Equal(RealStyle.Split("\r\n").Length, raw.Split("\r\n").Length);
        Assert.Equal(XDocument.Parse(RealStyle).Descendants().Count(), XDocument.Parse(raw.TrimStart('﻿')).Descendants().Count());
    }

    [Fact]
    public void Saving_the_same_values_writes_nothing_and_keeps_no_backup()
    {
        var path = WriteFile(RealStyle);
        var before = File.ReadAllBytes(path);
        Assert.Empty(RbmConfigFile.Save(path, new Dictionary<RbmOption, string> { [RbmOptions.LordEquipment] = "1", [RbmOptions.BuildingCost] = "250" }));
        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.False(File.Exists(RbmConfigFile.BackupPath(path)));
    }

    [Fact]
    public void The_backup_is_made_only_the_first_time()
    {
        var path = WriteFile(RealStyle);
        var original = File.ReadAllBytes(path);
        RbmConfigFile.Save(path, new Dictionary<RbmOption, string> { [RbmOptions.LordEquipment] = "0" });
        RbmConfigFile.Save(path, new Dictionary<RbmOption, string> { [RbmOptions.DeserterRaiders] = "0" });
        Assert.Equal(original, File.ReadAllBytes(RbmConfigFile.BackupPath(path)));
        Assert.Equal("0", RbmConfigFile.Read(path).Values[RbmOptions.DeserterRaiders]);
    }

    [Fact]
    public void Missing_settings_are_added_at_the_end_of_their_part_with_its_indent()
    {
        var text = "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n<Config version=\"2\">\r\n  <RBMCampaign>\r\n    <Enabled>1</Enabled>\r\n  </RBMCampaign>\r\n</Config>";
        var path = WriteFile(text);
        var read = RbmConfigFile.Read(path);
        Assert.Contains(RbmOptions.LordEquipment, read.Missing);
        Assert.Equal("1", read.Values[RbmOptions.LordEquipment]);
        Assert.Equal("250", read.Values[RbmOptions.BuildingCost]);

        RbmConfigFile.Save(path, new Dictionary<RbmOption, string> { [RbmOptions.LordEquipment] = "0", [RbmOptions.StartingGold] = "2" });
        Assert.Equal(
            "﻿<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n<Config version=\"2\">\r\n  <RBMCampaign>\r\n    <Enabled>1</Enabled>\r\n"
            + "    <LordEquipmentUpgradeEnabled>0</LordEquipmentUpgradeEnabled>\r\n    <CampaignStartingGoldMultiplier>2</CampaignStartingGoldMultiplier>\r\n"
            + "  </RBMCampaign>\r\n</Config>",
            ReadRaw(path));
    }

    [Fact]
    public void A_missing_part_is_added_at_the_end_of_config_and_an_lf_file_stays_lf()
    {
        var text = "<Config version=\"2\">\n\t<RBMAI>\n\t\t<Enabled>1</Enabled>\n\t</RBMAI>\n</Config>\n";
        var path = WriteFile(text, bom: false);
        RbmConfigFile.Save(path, new Dictionary<RbmOption, string> { [RbmOptions.LordEquipment] = "0" });
        Assert.Equal(
            "<Config version=\"2\">\n\t<RBMAI>\n\t\t<Enabled>1</Enabled>\n\t</RBMAI>\n\t<RBMCampaign>\n\t\t<LordEquipmentUpgradeEnabled>0</LordEquipmentUpgradeEnabled>\n\t</RBMCampaign>\n</Config>\n",
            ReadRaw(path));
        Assert.Equal("0", RbmConfigFile.Read(path).Values[RbmOptions.LordEquipment]);
    }

    [Fact]
    public void Empty_and_self_closing_parts_and_values_are_filled_in()
    {
        var path = WriteFile("<Config version=\"2\"><RBMTournament/><RBMAI><Enabled/></RBMAI><RBMCampaign></RBMCampaign></Config>", bom: false);
        RbmConfigFile.Save(path, new Dictionary<RbmOption, string>
        {
            [RbmOptions.Tournament] = "0", [RbmOptions.Ai] = "0", [RbmOptions.LordEquipment] = "0",
        });
        var read = RbmConfigFile.Read(path);
        Assert.Equal("0", read.Values[RbmOptions.Tournament]);
        Assert.Equal("0", read.Values[RbmOptions.Ai]);
        Assert.Equal("0", read.Values[RbmOptions.LordEquipment]);
        Assert.Contains("<RBMCampaign><LordEquipmentUpgradeEnabled>0</LordEquipmentUpgradeEnabled></RBMCampaign>", ReadRaw(path));
    }

    [Fact]
    public void A_bad_or_missing_file_is_reported_and_left_alone()
    {
        var missing = Path.Combine(_folder, "none", "config.xml");
        var none = RbmConfigFile.Read(missing);
        Assert.False(none.Exists);
        Assert.False(none.Usable);
        Assert.Throws<FileNotFoundException>(() => RbmConfigFile.Save(missing, new Dictionary<RbmOption, string> { [RbmOptions.LordEquipment] = "0" }));
        Assert.False(File.Exists(missing));

        foreach (var bad in new[] { "<Config><RBMCampaign>", "not xml at all", "<Settings><RBMCampaign/></Settings>" })
        {
            var path = WriteFile(bad, bom: false);
            var read = RbmConfigFile.Read(path);
            Assert.True(read.Exists);
            Assert.NotNull(read.Problem);
            Assert.False(read.Usable);
            Assert.ThrowsAny<Exception>(() => RbmConfigFile.Save(path, new Dictionary<RbmOption, string> { [RbmOptions.LordEquipment] = "0" }));
            Assert.Equal(bad, ReadRaw(path));
            Assert.False(File.Exists(RbmConfigFile.BackupPath(path)));
        }

        // A setting that holds other settings is never flattened.
        var nested = WriteFile("<Config><RBMCampaign><LordEquipmentUpgradeEnabled><X>1</X></LordEquipmentUpgradeEnabled></RBMCampaign></Config>", bom: false);
        Assert.Throws<InvalidDataException>(() => RbmConfigFile.Save(nested, new Dictionary<RbmOption, string> { [RbmOptions.LordEquipment] = "0" }));
    }

    [Fact]
    public void Values_are_written_the_way_rbm_parses_them()
    {
        Assert.True(RbmConfigFile.TryNormalize(RbmOptions.WorkshopProduction, " 1,5 ", "Workshop production multiplier", out var value, out _));
        Assert.Equal("1.5", value);
        Assert.True(RbmConfigFile.TryNormalize(RbmOptions.VillageProduction, "0.50", "Village production multiplier", out value, out _));
        Assert.Equal("0.5", value);
        Assert.True(RbmConfigFile.TryNormalize(RbmOptions.BuildingCost, "100", "Building cost multiplier", out value, out _));
        Assert.Equal("100", value);
        Assert.False(RbmConfigFile.TryNormalize(RbmOptions.BuildingCost, "2.5", "Building cost multiplier", out _, out var error));
        Assert.Equal("Building cost multiplier must be a whole number from 1 to 1000.", error);
        Assert.False(RbmConfigFile.TryNormalize(RbmOptions.StartingGold, "500", "Starting gold multiplier", out _, out error));
        Assert.Equal("Starting gold multiplier must be a number from 0 to 100.", error);
        Assert.False(RbmConfigFile.TryNormalize(RbmOptions.WorkshopProduction, "lots", "Workshop production multiplier", out _, out _));
        Assert.False(RbmConfigFile.TryNormalize(RbmOptions.WorkshopProduction, "NaN", "Workshop production multiplier", out _, out _));
        Assert.True(RbmConfigFile.TryNormalize(RbmOptions.LordEquipment, "1", "Lords buy equipment", out value, out _));
        Assert.Equal("1", value);

        // RBM counts a switch as on only for the text 1.
        Assert.True(RbmConfigFile.IsOn(" 1 "));
        Assert.False(RbmConfigFile.IsOn("true"));
        Assert.True(RbmConfigFile.SameValue(RbmOptions.WorkshopProduction, "1.0", "1"));
        Assert.False(RbmConfigFile.SameValue(RbmOptions.WorkshopProduction, "1.5", "1"));
        Assert.True(RbmConfigFile.SameValue(RbmOptions.LordEquipment, "0", "false"));
    }

    [Fact]
    public void Coop_friendly_turns_off_lords_buying_equipment_and_nothing_else()
    {
        var shown = RbmOptions.All.ToDictionary(option => option, option => option.RbmDefault);
        var friendly = RbmOptions.CoopFriendly(shown);
        Assert.Equal("0", friendly[RbmOptions.LordEquipment]);
        foreach (var option in RbmOptions.All.Where(option => option != RbmOptions.LordEquipment))
            Assert.Equal(shown[option], friendly[option]);
        // It changes the values shown, not the file.
        Assert.Equal("1", shown[RbmOptions.LordEquipment]);
    }

    [Fact]
    public void The_card_shows_only_in_coodsz_host_while_rbm_is_on_in_the_setup()
    {
        Assert.True(RbmSwitches.ShowsCard(false, ["Native", "Coop", "RBM"]));
        Assert.True(RbmSwitches.ShowsCard(false, ["rbm"]));
        Assert.False(RbmSwitches.ShowsCard(false, ["Native", "Coop", "RBMExtra"]));
        Assert.False(RbmSwitches.ShowsCard(false, []));
        Assert.False(RbmSwitches.ShowsCard(true, ["Native", "RBM"]));
        Assert.False(Edition.IsAD1259);

        var source = SourceFiles.Window(RepoRoot(), "CoodszHost");
        // Visibility follows the enabled mods on every state refresh, and the AD1259 build shows only a note.
        Assert.Contains("RbmSwitches.ShowsCard(Edition.IsAD1259, _modules.Where(row => row.Enabled)", source);
        Assert.Contains("RefreshRbmCard();", SourceFiles.Method(source, "private void RefreshState()"));
        var start = SourceFiles.Method(source, "private void StartRbmCard()");
        Assert.Contains("RbmCard.Visibility = Visibility.Collapsed;", start);
        Assert.Contains("RbmPartsGroup.Visibility = Visibility.Collapsed;", start);
        Assert.Contains("NewFeatures.RbmSettings", start);
    }

    // RBM's code never runs on the Coop server (DedicatedServerType none), so the card says it edits
    // this PC's game, never offers a server restart, and no Start writes RBM.
    [Fact]
    public void The_card_tells_the_truth_about_where_rbm_runs()
    {
        var xaml = File.ReadAllText(Path.Combine(RepoRoot(), "src", "CoodszHost", "MainWindow.xaml"));
        var cardStart = xaml.IndexOf("x:Name=\"RbmCard\"", StringComparison.Ordinal);
        var card = xaml[cardStart..xaml.IndexOf("x:Name=\"RbmNote\"", cardStart, StringComparison.Ordinal)];
        Assert.Contains("Text=\"RBM settings for this PC's game\"", card);
        Assert.Contains("The Coop server does not run RBM's code. RBM's campaign rules run in each player's own game, so every player should use the same values.", card);
        Assert.Contains("Coop and Friends sets the coop friendly values in each player's game when they press Connect", card);
        Assert.DoesNotContain("restart the server", card, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Restart server", card);
        Assert.DoesNotContain("for the server", card);
        Assert.DoesNotContain("do not change the server", card);
        Assert.DoesNotContain("RbmRestartButton", xaml);

        var source = SourceFiles.Window(RepoRoot(), "CoodszHost");
        var cardCode = File.ReadAllText(Path.Combine(RepoRoot(), "src", "CoodszHost", "MainWindow.RbmCard.cs"));
        Assert.DoesNotContain("Restart", cardCode);
        Assert.DoesNotContain("_session.IsRunning", cardCode);
        Assert.DoesNotContain("ApplyRbmSwitches", source);
        Assert.DoesNotContain("RbmSwitches.Apply(", source);
        var guide = GuideLibrary.All.Single(g => g.Id == "mods");
        Assert.DoesNotContain(GuideLibrary.Lines(guide), line => line.Contains("RBM") && line.Contains("restart", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(GuideLibrary.Lines(guide), line => line.Contains("RBM's campaign rules run in each player's own game"));
    }

    [Fact]
    public void Every_card_control_has_a_tooltip_and_all_card_text_is_translated()
    {
        var xaml = XDocument.Load(Path.Combine(RepoRoot(), "src", "CoodszHost", "MainWindow.xaml"));
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var card = xaml.Descendants().Single(element => (string?)element.Attribute(x + "Name") == "RbmCard");
        var controls = card.Descendants().Where(element => element.Name.LocalName is "CheckBox" or "TextBox" or "Button").ToList();
        Assert.Equal(17, controls.Count);
        foreach (var control in controls)
            Assert.False(string.IsNullOrWhiteSpace((string?)control.Attribute("ToolTip")), (string?)control.Attribute(x + "Name") + " has no tooltip.");

        var texts = card.DescendantsAndSelf()
            .SelectMany(element => element.Attributes().Where(attribute => attribute.Name.LocalName is "Text" or "Content" or "ToolTip"))
            .Select(attribute => attribute.Value)
            .Concat(CardCodeText)
            .ToList();
        var guide = GuideLibrary.All.Single(g => g.Id == "mods");
        texts.AddRange(GuideLibrary.Lines(guide).Where(line => line.Contains("RBM")));
        texts.AddRange(WhatsNew.Friends.Single(entry => entry.Version == "v1.2.55").Lines);
        Assert.Contains(texts, text => text.Contains("RBM settings card"));
        foreach (var code in Translator.Languages.Where(language => language.Code != Translator.English).Select(language => language.Code))
        {
            var table = Translator.Entries(code);
            var missing = texts.Where(text => !table.ContainsKey(text)).ToList();
            Assert.True(missing.Count == 0, code + " is missing: " + string.Join(" | ", missing));
        }
    }

    // What MainWindow.RbmCard.cs shows (the error lines are translated as templates).
    private static readonly string[] CardCodeText =
    [
        "Workshop production multiplier", "Village production multiplier", "Building cost multiplier", "Starting gold multiplier",
        "RBM has not made its config.xml on this PC yet. RBM makes it the first time your game runs with RBM. Play once with RBM on this PC, then press Read the file again.",
        "Values read from RBM's config.xml.",
        "Read RBM's config.xml again.",
        "Lords buy equipment is off now. Press Save RBM settings to keep it.",
        "Lords buy equipment is already off. Coop friendly changes nothing else.",
        "RBM's settings were not saved ({0}).",
        "Nothing changed. RBM's config.xml already has these settings.",
        "Saved. Your game on this PC uses these RBM settings the next time it starts. Other players set theirs in their own game.",
        "RBM settings saved to {0}: {1}.",
        "{0} must be a number from {1} to {2}.",
        "{0} must be a whole number from {1} to {2}.",
        "RBM's config.xml could not be opened ({0}).",
        "RBM's config.xml could not be read ({0}). RBM puts back its defaults the next time it loads.",
    ];

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "BannerlordCoopLauncher.sln")))
            directory = directory.Parent;
        return directory!.FullName;
    }
}
