using BannerlordCoopLauncher.Core.AD1259;
using BannerlordCoopLauncher.Core.Modules;
using CoodszHost.Services;

namespace BannerlordCoopLauncher.Tests;

// AD1259 crash report #20: a setup made with New setup is not locked, so the module reload never copied the compat
// module in, and Start refused with advice to get a module that only ships inside the tool.
[Collection(TranslatorLanguage.Name)]
public sealed class AD1259CompatStartTests
{
    private static List<ModuleRow> WithCompat(string? version)
    {
        var rows = AD1259ToolsTests.CompleteServerRows();
        rows.RemoveAll(row => row.Id == AD1259Stack.CompatModuleId);
        if (version != null)
            rows.Add(new ModuleRow { Id = AD1259Stack.CompatModuleId, Version = version });
        return rows;
    }

    [Fact]
    public void Needs_compat_when_it_is_missing_or_another_version()
    {
        Assert.True(AD1259Server.NeedsCompat(WithCompat(null)));
        Assert.True(AD1259Server.NeedsCompat(WithCompat("v1.0.0")));
        Assert.False(AD1259Server.NeedsCompat(WithCompat(AD1259Stack.CompatVersion)));
        Assert.False(AD1259Server.NeedsCompat(WithCompat(AD1259Stack.CompatVersion.ToUpperInvariant())));
    }

    [Fact]
    public void A_compat_copy_cut_short_has_no_SubModule_xml_so_the_next_Start_copies_it_again()
    {
        var root = Path.Combine(Path.GetTempPath(), "coodsz-compat-cut-" + Guid.NewGuid().ToString("N"));
        try
        {
            var payload = Path.Combine(root, "payload");
            Directory.CreateDirectory(Path.Combine(payload, "bin", "Win64_Shipping_Server"));
            File.WriteAllText(Path.Combine(payload, "SubModule.xml"), "<Module><Version value=\"" + AD1259Stack.CompatVersion + "\"/></Module>");
            File.WriteAllText(Path.Combine(payload, "bin", "Win64_Shipping_Server", "AD1259CoopCompat.dll"), "dll");
            var server = Path.Combine(root, "DedicatedServer");
            var target = Path.Combine(ModuleCatalog.ModulesRoot(server), AD1259Stack.CompatModuleId);
            // A folder where the DLL goes makes that copy fail, like a DLL locked by antivirus.
            Directory.CreateDirectory(Path.Combine(target, "bin", "Win64_Shipping_Server", "AD1259CoopCompat.dll"));

            Assert.ThrowsAny<Exception>(() => AD1259Server.InstallCompat(server, payload));
            Assert.False(File.Exists(Path.Combine(target, "SubModule.xml")));

            Directory.Delete(Path.Combine(target, "bin", "Win64_Shipping_Server", "AD1259CoopCompat.dll"));
            AD1259Server.InstallCompat(server, payload);
            Assert.True(File.Exists(Path.Combine(target, "SubModule.xml")));
            Assert.True(File.Exists(Path.Combine(target, "bin", "Win64_Shipping_Server", "AD1259CoopCompat.dll")));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void A_missing_compat_never_sends_players_to_the_Workshop_or_Modules()
    {
        var before = BannerlordCoopLauncher.Core.Localization.Translator.Current;
        try
        {
            BannerlordCoopLauncher.Core.Localization.Translator.Use("en");
            var only = AD1259Server.Problem(WithCompat(null))!;
            Assert.Equal(AD1259Stack.CompatName + " " + AD1259Stack.CompatVersion
                + " comes with this tool but could not be added to the server. The server window says why. Start again, and if it still fails, download this tool again.", only);
            Assert.DoesNotContain("Workshop", only);
            Assert.DoesNotContain("not complete", only);

            // With another mod wrong too, the set advice stays for that mod and the compat line follows it.
            var rows = WithCompat("v1.0.0");
            rows[rows.FindIndex(row => row.Id == "RBM")] = new ModuleRow { Id = "RBM", Version = "v4.5.3" };
            var both = AD1259Server.Problem(rows)!;
            Assert.StartsWith("The AD1259 set is not complete on this PC: Realistic Battle Mod v4.5.4 (the server has v4.5.3). Install those versions", both);
            Assert.DoesNotContain(AD1259Stack.CompatName + " " + AD1259Stack.CompatVersion + ";", both);
            Assert.EndsWith("Start again, and if it still fails, download this tool again.", both);
        }
        finally
        {
            BannerlordCoopLauncher.Core.Localization.Translator.Use(before);
        }
    }

    [Fact]
    public void The_compat_line_is_translated()
    {
        var before = BannerlordCoopLauncher.Core.Localization.Translator.Current;
        try
        {
            BannerlordCoopLauncher.Core.Localization.Translator.Use("fr");
            var shown = AD1259Server.Problem(WithCompat(null))!;
            Assert.StartsWith(AD1259Stack.CompatName + " " + AD1259Stack.CompatVersion + " est fourni avec cet outil", shown);
            Assert.DoesNotContain("comes with this tool", shown);
        }
        finally
        {
            BannerlordCoopLauncher.Core.Localization.Translator.Use(before);
        }
    }

    [Fact]
    public void Start_adds_the_compat_before_the_set_check_for_every_setup()
    {
        var window = SourceFiles.Window(SourceFiles.Root(), "CoodszHost");
        var prepare = SourceFiles.Method(window, "private void PrepareAD1259Server()");
        var install = prepare.IndexOf("InstallAD1259Compat();", StringComparison.Ordinal);
        Assert.True(install >= 0, "Start installs the compat module");
        Assert.True(install < prepare.IndexOf("AD1259Server.Problem(", StringComparison.Ordinal), "before the set check");
        Assert.Contains("if (AD1259Server.NeedsCompat(_modules.ToList()))", prepare);
        Assert.DoesNotContain("Created locked setup", window);
    }

    [Fact]
    public void A_browser_that_did_not_open_shows_its_link_in_a_box_in_both_server_tools()
    {
        // The status line sits on the Dashboard while the button is on the Mods or Settings page (v1.3.8 Codex review).
        const string notice = "if (failed)\r\n                ConfirmWindow.Show(this, \"Download Coop server\", line);";
        Assert.Contains(notice, SourceFiles.Read(Path.Combine("src", "CoodszHost", "MainWindow.LogsAndFolders.cs")));
        Assert.Contains(notice, SourceFiles.Read(Path.Combine("src", "BurningEmpiresHost", "MainWindow.ServerControl.cs")));
        Assert.Contains("window.CancelButton.Visibility = Visibility.Collapsed;", SourceFiles.Read(Path.Combine("src", "BurningEmpiresHost", "ConfirmWindow.xaml.cs")));
    }

    [Fact]
    public void An_AD1259_new_setup_starts_on_the_AD1259_map()
    {
        var profile = SourceFiles.Read(Path.Combine("src", "CoodszHost", "Services", "HostProfile.cs"));
        Assert.Contains("CampaignMap = Edition.IsAD1259 ? AD1259Server.CampaignMap : VanillaMap,", SourceFiles.Method(profile, "public static HostProfile CreateFresh(string name)"));
        // The map box keeps it while the AD1259 mod is not on the server yet, so Start never says to pick a map.
        var bind = SourceFiles.Method(SourceFiles.Window(SourceFiles.Root(), "CoodszHost"), "private void BindMapBox(HostProfile profile)");
        Assert.Contains("&& !(Edition.IsAD1259 && profile.CampaignMap.Equals(AD1259Server.CampaignMap, StringComparison.OrdinalIgnoreCase)))", bind);
        // The Saves page hint and the button tooltip say the AD1259 map in that tool (live test 2026-11-02).
        var window = SourceFiles.Read(Path.Combine("src", "CoodszHost", "MainWindow.xaml.cs"));
        Assert.Contains("NewSetupHintRun.Text = \" starts on the default mod order, with the AD1259 map and an empty save. \";", window);
        Assert.Contains("NewSetupButton.ToolTip = \"Starts a setup on the default mod order, with the AD1259 map and an empty save.\";", window);
    }

    [Fact]
    public void A_kept_map_with_no_files_on_the_server_still_shows_its_name()
    {
        // Coop crash report CR-JTQ45: a locked setup whose map's SceneObj\Main_map is missing showed a blank map box.
        var window = SourceFiles.Window(SourceFiles.Root(), "CoodszHost");
        Assert.Contains("labels.Add(KeptMapLabel(profile));", SourceFiles.Method(window, "private void BindMapBox(HostProfile profile)"));
        Assert.Contains("return profile.Locked ? string.Format(Translator.T(MissingMapText), name) : name;", SourceFiles.Method(window, "private string KeptMapLabel(HostProfile profile)"));
        Assert.Contains("public const string MissingMapText = \"{0} (map files missing on the server)\";", window);
        // Picking that line, or cancelling a map switch while it is kept, rebinds instead of looking it up in MapChoices.
        var changed = SourceFiles.Method(window, "private void MapBox_SelectionChanged(object sender, SelectionChangedEventArgs e)");
        Assert.Contains("if (choice.Id == null)", changed);
        Assert.DoesNotContain("MapChoices().First(", changed);
    }
}
