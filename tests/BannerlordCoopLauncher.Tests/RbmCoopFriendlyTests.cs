using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using BannerlordCoopLauncher.Core.Localization;
using BannerlordCoopLauncher.Core.Models;
using BannerlordCoopLauncher.Core.Updates;
using CoodszHost.Services;

namespace BannerlordCoopLauncher.Tests;

// Keep RBM coop friendly: RBM's code never runs on the Coop server, so Connect in Coop and Friends
// (and in the AD1259 client launcher) writes the coop friendly RBM values into the player's own
// config.xml, through the same safe edit as the Coodsz Host RBM card.
public sealed class RbmCoopFriendlyTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "rbm-friendly-" + Guid.NewGuid().ToString("N"));
    private static readonly string[] WithRbm = ["Native", "SandBoxCore", "Coop", "RBM"];

    public RbmCoopFriendlyTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, recursive: true);
    }

    // Shaped like the file RBM 4.5 writes: BOM, CRLF, version 2, settings this tool never touches.
    private const string RbmFile =
        "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n"
        + "<Config version=\"2\">\r\n"
        + "  <RBMAI>\r\n"
        + "    <Enabled>1</Enabled>\r\n"
        + "    <PostureEnabled>1</PostureEnabled>\r\n"
        + "  </RBMAI>\r\n"
        + "  <RBMCampaign>\r\n"
        + "    <Enabled>1</Enabled>\r\n"
        + "    <BuildingCostMultiplier>250</BuildingCostMultiplier>\r\n"
        + "    <KingdomCaravansEnabled>1</KingdomCaravansEnabled>\r\n"
        + "    <LordEquipmentUpgradeEnabled>1</LordEquipmentUpgradeEnabled>\r\n"
        + "    <RecruitDrawsFromSettlementStock>1</RecruitDrawsFromSettlementStock>\r\n"
        + "  </RBMCampaign>\r\n"
        + "  <LastSeenChangelogVersion>v4.5.4</LastSeenChangelogVersion>\r\n"
        + "</Config>";

    private string Write(string text)
    {
        var path = Path.Combine(_folder, "config.xml");
        File.WriteAllBytes(path, new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes(text)).ToArray());
        return path;
    }

    [Fact]
    public void Connect_changes_only_lords_buy_equipment_and_keeps_a_backup()
    {
        var path = Write(RbmFile);
        var before = File.ReadAllBytes(path);

        var line = RbmSwitches.ApplyForPlayer(WithRbm, keepCoopFriendly: true, path: path);

        var expected = before.Take(3).Concat(Encoding.UTF8.GetBytes(RbmFile.Replace(
            "<LordEquipmentUpgradeEnabled>1</LordEquipmentUpgradeEnabled>", "<LordEquipmentUpgradeEnabled>0</LordEquipmentUpgradeEnabled>"))).ToArray();
        Assert.Equal(expected, File.ReadAllBytes(path));
        Assert.Equal(before, File.ReadAllBytes(RbmConfigFile.BackupPath(path)));
        Assert.Equal("Keep RBM coop friendly turned off Lords buy equipment in this PC's RBM settings (" + path + "), so lords do not empty the town markets in coop.", line);

        // The next Connect finds it off already and writes nothing; the backup stays the original.
        var again = RbmSwitches.ApplyForPlayer(WithRbm, keepCoopFriendly: true, path: path);
        Assert.Equal("Keep RBM coop friendly: Lords buy equipment is already off in this PC's RBM settings.", again);
        Assert.Equal(expected, File.ReadAllBytes(path));
        Assert.Equal(before, File.ReadAllBytes(RbmConfigFile.BackupPath(path)));
    }

    [Fact]
    public void Unticked_keep_rbm_coop_friendly_leaves_the_file_alone()
    {
        var path = Write(RbmFile);
        var before = File.ReadAllBytes(path);
        Assert.Equal(RbmSwitches.KeepCoopFriendlyOff, RbmSwitches.ApplyForPlayer(WithRbm, keepCoopFriendly: false, path: path));
        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.False(File.Exists(RbmConfigFile.BackupPath(path)));

        // Missing file and the box off: nothing is made either.
        var missing = Path.Combine(_folder, "none", "config.xml");
        RbmSwitches.ApplyForPlayer(WithRbm, keepCoopFriendly: false, path: missing);
        Assert.False(File.Exists(missing));
    }

    [Fact]
    public void A_missing_config_is_made_with_rbm_version_2_and_only_that_value()
    {
        var path = Path.Combine(_folder, "Configs", "RBM", "config.xml");
        Assert.NotNull(RbmSwitches.ApplyForPlayer(WithRbm, keepCoopFriendly: true, path: path));

        var root = XDocument.Load(path).Root!;
        Assert.Equal("Config", root.Name.LocalName);
        Assert.Equal("2", root.Attribute("version")?.Value);
        var element = Assert.Single(root.Descendants().Where(e => !e.HasElements));
        Assert.Equal("LordEquipmentUpgradeEnabled", element.Name.LocalName);
        Assert.Equal("RBMCampaign", element.Parent!.Name.LocalName);
        Assert.Equal("0", element.Value);
        // RBM completes the rest with its own defaults on its next load.
        var read = RbmConfigFile.Read(path);
        Assert.True(read.Usable);
        Assert.Equal("0", read.Values[RbmOptions.LordEquipment]);
        Assert.Equal("1", read.Values[RbmOptions.Campaign]);
    }

    [Fact]
    public void Without_rbm_in_the_setup_nothing_is_written()
    {
        var missing = Path.Combine(_folder, "Configs", "RBM", "config.xml");
        Assert.Null(RbmSwitches.ApplyForPlayer(["Native", "SandBoxCore", "Coop", "RBMExtra"], keepCoopFriendly: true, path: missing));
        Assert.False(Directory.Exists(Path.GetDirectoryName(missing)));

        var path = Write(RbmFile);
        var before = File.ReadAllBytes(path);
        Assert.Null(RbmSwitches.ApplyForPlayer(["Native", "Coop"], keepCoopFriendly: true, path: path));
        Assert.Null(RbmSwitches.ApplyForPlayer([], keepCoopFriendly: true, parts: new RbmSettings(), path: path));
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    [Fact]
    public void A_broken_config_is_reported_and_left_as_it_is()
    {
        var path = Path.Combine(_folder, "config.xml");
        File.WriteAllText(path, "<Config><RBMCampaign>");
        Assert.Throws<InvalidOperationException>(() => RbmSwitches.ApplyForPlayer(WithRbm, keepCoopFriendly: true, path: path));
        Assert.Equal("<Config><RBMCampaign>", File.ReadAllText(path));
        Assert.False(File.Exists(RbmConfigFile.BackupPath(path)));
    }

    [Fact]
    public void The_ad1259_launcher_writes_its_four_parts_and_the_coop_friendly_value_in_one_edit()
    {
        var path = Write(RbmFile);
        var line = RbmSwitches.ApplyForPlayer(WithRbm, keepCoopFriendly: true, parts: new RbmSettings { Campaign = true, Ai = false }, path: path);
        var read = RbmConfigFile.Read(path);
        Assert.Equal("0", read.Values[RbmOptions.Ai]);
        Assert.Equal("1", read.Values[RbmOptions.Campaign]);
        Assert.Equal("0", read.Values[RbmOptions.LordEquipment]);
        Assert.Equal("1", read.Values[RbmOptions.RecruitStock]);
        Assert.StartsWith("RBM: Combat on, AI off, Campaign on, Tournament on (written to " + path + ").", line);
        Assert.EndsWith("so lords do not empty the town markets in coop.", line);
        Assert.Contains("<PostureEnabled>1</PostureEnabled>", File.ReadAllText(path));

        // Opted out: the parts are still written, Lords buy equipment is not touched.
        var other = Path.Combine(_folder, "b", "config.xml");
        Directory.CreateDirectory(Path.GetDirectoryName(other)!);
        File.WriteAllText(other, RbmFile);
        var optOut = RbmSwitches.ApplyForPlayer(WithRbm, keepCoopFriendly: false, parts: new RbmSettings(), path: other);
        Assert.Equal("1", RbmConfigFile.Read(other).Values[RbmOptions.LordEquipment]);
        Assert.Equal("0", RbmConfigFile.Read(other).Values[RbmOptions.Campaign]);
        Assert.EndsWith(RbmSwitches.KeepCoopFriendlyOff, optOut);
        // The coop friendly values are the Host card's Coop friendly button.
        var shown = RbmOptions.All.ToDictionary(option => option, option => option.RbmDefault);
        var button = RbmOptions.CoopFriendly(shown).Where(pair => shown[pair.Key] != pair.Value).ToDictionary(pair => pair.Key, pair => pair.Value);
        Assert.Equal(button, RbmSwitches.CoopFriendlyValues);
    }

    [Fact]
    public void Keep_rbm_coop_friendly_is_on_by_default_and_saved()
    {
        Assert.True(new LauncherSettings().KeepRbmCoopFriendly);
        // A launcher.json from an older version has no such field: it starts on.
        var old = JsonSerializer.Deserialize<LauncherSettings>("{\"GamePath\":\"C:\\\\Games\"}", new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        Assert.True(old.KeepRbmCoopFriendly);
        var path = Path.Combine(_folder, "launcher.json");
        var settings = new LauncherSettings { KeepRbmCoopFriendly = false };
        settings.Save(path);
        Assert.False(LauncherSettings.Load(path).KeepRbmCoopFriendly);
    }

    [Fact]
    public void Coop_and_friends_and_the_ad1259_launcher_take_the_one_shared_path_on_connect()
    {
        var root = RepoRoot();
        var client = SourceFiles.Window(root, "BannerlordCoopLauncher");
        var launch = SourceFiles.Method(client, "private async Task LaunchAsync(bool diagnostic)");
        Assert.True(launch.IndexOf("ApplyRbmCoopFriendly();", StringComparison.Ordinal) is var at && at > 0
            && at < launch.IndexOf("Process.Start(start)", StringComparison.Ordinal), "The RBM write must come before the game starts.");
        var apply = SourceFiles.Method(client, "private void ApplyRbmCoopFriendly()");
        Assert.Contains("RbmSwitches.ApplyForPlayer(_resolution.All.Select(module => module.ModuleId), _settings.KeepRbmCoopFriendly)", apply);
        Assert.Contains("Log(line)", apply);
        Assert.Contains("ShowRbmCoopFriendly(resolution);", SourceFiles.Method(client, "private void BindModules(ModuleResolution resolution)"));
        Assert.Contains("RbmCoopFriendlyPanel.Visibility = RbmInSetup(resolution) ? Visibility.Visible : Visibility.Collapsed;", client);
        Assert.Contains("NewBadges.Attach(RbmCoopFriendlyBox, NewFeatures.RbmCoopFriendly", client);

        var ad1259 = SourceFiles.Window(root, "AD1259Launcher");
        Assert.Contains("RbmSwitches.ApplyForPlayer(AD1259Stack.ClientModules, _settings.KeepRbmCoopFriendly, _settings.Rbm)", SourceFiles.Method(ad1259, "private void ApplyRbm()"));
        Assert.DoesNotContain("RbmSwitches.Apply(", ad1259);
        Assert.Contains("NewBadges.Attach(RbmCoopFriendlyBox, NewFeatures.RbmCoopFriendly", ad1259);

        // Both player launchers build the same two files the Host card uses.
        foreach (var project in new[] { "CoodszCoopAndFriends/CoodszCoopAndFriends.csproj", "AD1259Launcher/AD1259Launcher.csproj" })
        {
            var csproj = File.ReadAllText(Path.Combine(root, "src", project));
            Assert.Contains("<Compile Include=\"..\\CoodszHost\\Services\\RbmSwitches.cs\"", csproj);
            Assert.Contains("<Compile Include=\"..\\CoodszHost\\Services\\RbmConfigFile.cs\"", csproj);
        }

        Assert.Contains(NewFeatures.Friends, feature => feature.Id == NewFeatures.RbmCoopFriendly && feature.Version == "v1.2.55");
        Assert.Contains(NewFeatures.AD1259, feature => feature.Id == NewFeatures.RbmCoopFriendly && feature.Version == "v1.0.14");
        Assert.Contains(NewFeatures.CoodszHost, feature => feature.Id == NewFeatures.RbmSettings && feature.Version == "v1.2.55");
    }

    [Fact]
    public void The_box_has_a_tip_and_starts_hidden_and_its_words_are_translated()
    {
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var texts = new List<string>();
        foreach (var app in new[] { "BannerlordCoopLauncher", "AD1259Launcher" })
        {
            var xaml = XDocument.Load(Path.Combine(RepoRoot(), "src", app, "MainWindow.xaml"));
            var box = xaml.Descendants().Single(element => (string?)element.Attribute(x + "Name") == "RbmCoopFriendlyBox");
            Assert.Equal("Keep RBM coop friendly", (string?)box.Attribute("Content"));
            Assert.False(string.IsNullOrWhiteSpace((string?)box.Attribute("ToolTip")));
            texts.Add((string)box.Attribute("Content")!);
            texts.Add((string)box.Attribute("ToolTip")!);
            if (app == "BannerlordCoopLauncher")
            {
                var panel = box.Parent!;
                Assert.Equal("RbmCoopFriendlyPanel", (string?)panel.Attribute(x + "Name"));
                Assert.Equal("Collapsed", (string?)panel.Attribute("Visibility"));
                texts.AddRange(panel.Elements().Where(element => element.Name.LocalName == "TextBlock").Select(element => (string)element.Attribute("Text")!));
            }
        }

        texts.AddRange(
        [
            RbmSwitches.KeepCoopFriendlyOff,
            "Keep RBM coop friendly turned off Lords buy equipment in this PC's RBM settings ({0}), so lords do not empty the town markets in coop.",
            "Keep RBM coop friendly: Lords buy equipment is already off in this PC's RBM settings.",
            "Keep RBM coop friendly is on. Connect turns off RBM's Lords buy equipment in your own RBM settings.",
            "Keep RBM coop friendly is off. Connect leaves your own RBM settings as they are.",
            "RBM: {0} (written to {1}).",
        ]);
        texts.AddRange(WhatsNew.Friends[0].Lines);
        texts.AddRange(WhatsNew.AD1259[0].Lines);
        foreach (var code in Translator.Languages.Where(language => language.Code != Translator.English).Select(language => language.Code))
        {
            var table = Translator.Entries(code);
            var missing = texts.Where(text => !table.ContainsKey(text)).ToList();
            Assert.True(missing.Count == 0, code + " is missing: " + string.Join(" | ", missing));
        }
    }

    // RBM runs in each player's own game, so the AD1259 client text says to agree on one set, never "match the server".
    [Fact]
    public void Ad1259_client_rbm_text_does_not_say_match_the_server()
    {
        var xaml = File.ReadAllText(Path.Combine(RepoRoot(), "src", "AD1259Launcher", "MainWindow.xaml"));
        Assert.DoesNotContain("Match the server", xaml);
        Assert.Contains("the server does not use RBM settings, so everyone in your group should pick the same ones to make battles feel the same", xaml);
    }

    // NewBadges clears the badge on a real click (ButtonBase.Click); the Changed handler also clears it for
    // a toggle that raises only Checked (keyboard, UI automation).
    [Fact]
    public void Coop_friendly_new_badge_clears_on_a_click_or_a_toggle()
    {
        var root = RepoRoot();
        var badges = File.ReadAllText(Path.Combine(root, "src", "BannerlordCoopLauncher", "NewBadges.cs"));
        Assert.Contains("button.Click += (_, _) => Seen(id);", badges);
        var client = SourceFiles.Window(root, "BannerlordCoopLauncher");
        Assert.Contains("NewBadges.Seen(NewFeatures.RbmCoopFriendly);", SourceFiles.Method(client, "private void RbmCoopFriendly_Changed("));
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "BannerlordCoopLauncher.sln")))
            directory = directory.Parent;
        return directory!.FullName;
    }
}
