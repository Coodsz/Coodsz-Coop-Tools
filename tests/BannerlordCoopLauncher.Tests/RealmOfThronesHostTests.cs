using System.Text.RegularExpressions;
using BannerlordCoopLauncher.Core.IO;
using BannerlordCoopLauncher.Core.Keep;
using BannerlordCoopLauncher.Core.Updates;
using RealmOfThrones.Core;
using RealmOfThrones.Ui;
using TaomCoop.Core;

namespace BannerlordCoopLauncher.Tests;

// The Realm of Thrones Server Tool (src/RealmOfThronesHost), a copy of the Burning Empires one on the Coop nightly server.
// The window is checked by its source like the Burning Empires host tests; the logic it calls is tested as code.
public class RealmOfThronesHostTests
{
    private static string Window() => SourceFiles.Window(SourceFiles.Root(), "RealmOfThronesHost");

    private static string Project() => SourceFiles.Read(Path.Combine("src", "RealmOfThronesHost", "RealmOfThronesHost.csproj"));

    private static void InOrder(string source, params string[] steps)
    {
        var at = -1;
        foreach (var step in steps)
        {
            var next = source.IndexOf(step, at + 1, StringComparison.Ordinal);
            Assert.True(next > at, step + " is missing or out of order.");
            at = next;
        }
    }

    // ----- Project, names and placeholders -----

    [Fact]
    public void Csproj_names_the_tool_its_version_and_its_own_icon()
    {
        var project = Project();
        Assert.Contains("<AssemblyName>Coodsz Realm of Thrones Server Tool</AssemblyName>", project);
        Assert.Equal(RotRelease.Version, "v" + Regex.Match(project, "<Version>([^<]+)</Version>").Groups[1].Value);
        Assert.Contains(@"<ApplicationIcon>..\..\assets\icons\coodsz-rot.ico</ApplicationIcon>", project);
        Assert.Contains(@"RealmOfThrones.Core\RealmOfThrones.Core.csproj", project);
        Assert.DoesNotContain("BurningEmpires", project);
        Assert.NotEqual(File.ReadAllBytes(Path.Combine(SourceFiles.Root(), "assets", "icons", "coodsz-be.ico")),
            File.ReadAllBytes(Path.Combine(SourceFiles.Root(), "assets", "icons", "coodsz-rot.ico")));
        var tool = ToolNames.All.Single(item => item.Product == "RealmOfThrones" && item.Role == ToolRole.Server);
        Assert.Equal("Coodsz Realm of Thrones Server Tool.exe", tool.NewExeName);
    }

    [Fact]
    public void The_compat_module_rides_in_server_add_ons_where_the_window_looks_for_it()
    {
        var project = Project();
        Assert.Contains(@"<RotCompatModule>..\..\compat\RealmOfThronesCoopCompat\module</RotCompatModule>", project);
        Assert.Contains("Payload=\"ServerAddOns\" PayloadFolder=\"RealmOfThrones.CoopCompat\\\"", project);
        Assert.Contains("Path.Combine(ToolPayload.Folder(\"ServerAddOns\"), RotStack.CompatModuleId)", Window());
    }

    // ----- The compat module both tools carry -----

    // compat/RealmOfThronesCoopCompat/module is the committed Build.cmd output, like the Burning Empires module.
    private static string CompatSource => Path.Combine(SourceFiles.Root(), "compat", "RealmOfThronesCoopCompat");

    private static string CompatFolder => Path.Combine(CompatSource, "module");

    [Theory]
    [InlineData("RealmOfThronesHost")]
    [InlineData("RealmOfThronesLauncher")]
    public void Each_tool_finds_the_built_module_where_its_csproj_looks(string tool)
    {
        var project = SourceFiles.Read(Path.Combine("src", tool, tool + ".csproj"));
        var folder = Regex.Match(project, "<RotCompatModule>([^<]+)</RotCompatModule>").Groups[1].Value;
        var found = Path.GetFullPath(Path.Combine(SourceFiles.Root(), "src", tool, folder));
        Assert.Equal(Path.GetFullPath(CompatFolder), found);
        Assert.True(File.Exists(Path.Combine(found, "SubModule.xml")), tool + " would be built without the compat module.");
        Assert.Contains("Exclude=\"$(RotCompatModule)\\README.txt\"", project);
    }

    [Fact]
    public void The_module_folder_has_every_file_the_tools_use_and_matches_its_source()
    {
        foreach (var file in new[] { "SubModule.xml", "README.txt", RotServerPath.MapScriptName, Path.Combine("ModuleData", RotServerPath.MapDataFile) })
            Assert.Equal(File.ReadAllBytes(Path.Combine(CompatSource, file)), File.ReadAllBytes(Path.Combine(CompatFolder, file)));
        foreach (var dll in new[] { "RealmOfThrones.CoopCompat.dll", "Coodsz.CoopLink.dll" })
        {
            var client = Path.Combine(CompatFolder, "bin", "Win64_Shipping_Client", dll);
            var server = Path.Combine(CompatFolder, "bin", "Win64_Shipping_Server", dll);
            Assert.NotEmpty(File.ReadAllBytes(client));
            Assert.Equal(File.ReadAllBytes(client), File.ReadAllBytes(server));
        }
    }

    // Pack-RotCompat.ps1 makes the standalone zip and the Workshop package from module\ alone. The uploader reads the
    // first node's children as tasks, so a task file with an <?xml?> line or a comment first would do nothing.
    [Fact]
    public void Pack_makes_the_zip_and_Workshop_package_from_the_module_folder_only()
    {
        var root = Path.Combine(Path.GetTempPath(), "rot-pack-" + Guid.NewGuid().ToString("N"));
        try
        {
            using (var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoProfile -ExecutionPolicy Bypass -File \"" + Path.Combine(CompatSource, "Pack-RotCompat.ps1") + "\" -OutDir \"" + root + "\" -ItemId 42",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            }))
            {
                Assert.NotNull(process);
                process!.StandardOutput.ReadToEndAsync();
                var error = process.StandardError.ReadToEndAsync();
                Assert.True(process.WaitForExit(120000), "The pack script did not finish.");
                Assert.True(process.ExitCode == 0, error.Result);
            }

            var module = Directory.GetFiles(CompatFolder, "*", SearchOption.AllDirectories)
                .Select(file => RotStack.CompatModuleId + "/" + Path.GetRelativePath(CompatFolder, file).Replace('\\', '/')).Order(StringComparer.Ordinal).ToList();
            Assert.Equal(new[] { "LICENSE.txt", "ModuleData/rot_map.txt", "README.txt", RotServerPath.MapScriptName, "SubModule.xml",
                    "bin/Win64_Shipping_Client/Coodsz.CoopLink.dll", "bin/Win64_Shipping_Client/RealmOfThrones.CoopCompat.dll",
                    "bin/Win64_Shipping_Server/Coodsz.CoopLink.dll", "bin/Win64_Shipping_Server/RealmOfThrones.CoopCompat.dll" }
                .Select(file => RotStack.CompatModuleId + "/" + file).Order(StringComparer.Ordinal), module);
            using (var zip = System.IO.Compression.ZipFile.OpenRead(Path.Combine(root, RotStack.CompatModuleId + "-" + RotStack.CompatVersion + ".zip")))
                Assert.Equal(module, zip.Entries.Where(entry => entry.Name.Length > 0).Select(entry => entry.FullName).Order(StringComparer.Ordinal));

            var workshop = Path.Combine(root, RotStack.CompatModuleId + "-workshop");
            var package = Path.Combine(workshop, RotStack.CompatModuleId);
            Assert.Equal(File.ReadAllBytes(Path.Combine(CompatFolder, "SubModule.xml")), File.ReadAllBytes(Path.Combine(package, "SubModule.xml")));
            Assert.True(File.Exists(Path.Combine(workshop, "thumbnail.png")) && File.Exists(Path.Combine(workshop, "upload.cmd")));
            foreach (var task in new[] { "WorkshopCreate.xml", "WorkshopUpdate.xml" })
            {
                var text = File.ReadAllText(Path.Combine(workshop, task));
                Assert.StartsWith("<Tasks>", text);
                var xml = System.Xml.Linq.XDocument.Parse(text);
                Assert.Equal(package, (string?)xml.Descendants("ModuleFolder").Single().Attribute("Value"));
            }
            var create = System.Xml.Linq.XDocument.Load(Path.Combine(workshop, "WorkshopCreate.xml"));
            Assert.Equal(File.ReadAllText(Path.Combine(CompatSource, "workshop", "workshop-description.txt")).Replace("\r\n", "\n").Trim(),
                (string?)create.Descendants("ItemDescription").Single().Attribute("Value"));
            Assert.Equal("Private", (string?)create.Descendants("Visibility").Single().Attribute("Value"));
            Assert.Equal("42", (string?)System.Xml.Linq.XDocument.Load(Path.Combine(workshop, "WorkshopUpdate.xml")).Descendants("ItemId").Single().Attribute("Value"));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    // The DLL carries the SHA256 of each source file it was built from (StampSourceHashes in its csproj), so a source
    // change without a rebuilt and committed module\ fails here.
    [Fact]
    public void The_module_dll_was_built_from_the_source_next_to_it()
    {
        var expected = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var skip = new[] { "bin", "obj", "module" }.Select(folder => Path.Combine(CompatSource, folder) + Path.DirectorySeparatorChar).ToArray();
        var sources = Directory.GetFiles(CompatSource, "*.cs", SearchOption.AllDirectories)
            .Where(file => !skip.Any(folder => file.StartsWith(folder, StringComparison.OrdinalIgnoreCase)))
            .Concat(Directory.GetFiles(Path.Combine(SourceFiles.Root(), "compat", "CoodszCoopLink", "src"), "*.cs"));
        foreach (var file in sources)
            expected[Path.GetRelativePath(CompatSource, file).Replace('/', '\\')] = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(file)));

        var stamped = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using (var pe = new System.Reflection.PortableExecutable.PEReader(File.OpenRead(Path.Combine(CompatFolder, "bin", "Win64_Shipping_Client", "RealmOfThrones.CoopCompat.dll"))))
        {
            var metadata = System.Reflection.Metadata.PEReaderExtensions.GetMetadataReader(pe);
            foreach (var handle in metadata.GetAssemblyDefinition().GetCustomAttributes())
            {
                var attribute = metadata.GetCustomAttribute(handle);
                if (attribute.Constructor.Kind != System.Reflection.Metadata.HandleKind.MemberReference)
                    continue;
                var type = metadata.GetMemberReference((System.Reflection.Metadata.MemberReferenceHandle)attribute.Constructor).Parent;
                if (type.Kind != System.Reflection.Metadata.HandleKind.TypeReference
                    || metadata.GetString(metadata.GetTypeReference((System.Reflection.Metadata.TypeReferenceHandle)type).Name) != "AssemblyMetadataAttribute")
                    continue;
                var blob = metadata.GetBlobReader(attribute.Value);
                blob.ReadUInt16();
                var key = blob.ReadSerializedString() ?? "";
                if (key.StartsWith("Source ", StringComparison.Ordinal))
                    stamped[key["Source ".Length..]] = (blob.ReadSerializedString() ?? "").ToUpperInvariant();
            }
        }
        Assert.NotEmpty(stamped);
        Assert.Equal(expected, stamped);
    }

    [Fact]
    public void The_module_manifest_its_dll_its_build_and_RotStack_name_one_version()
    {
        var xml = File.ReadAllText(Path.Combine(CompatFolder, "SubModule.xml"));
        Assert.Contains("<Id value=\"" + RotStack.CompatModuleId + "\"/>", xml);
        Assert.Contains("<Name value=\"" + RotStack.CompatName + "\"/>", xml);
        var version = Regex.Match(xml, "<Version value=\"(v[0-9.]+)\"").Groups[1].Value;
        Assert.Equal(RotStack.CompatVersion, version);
        Assert.Equal(RotStack.ContentVersions[RotStack.CompatModuleId], version);
        var dll = Path.Combine(CompatFolder, "bin", "Win64_Shipping_Client", "RealmOfThrones.CoopCompat.dll");
        Assert.Equal(version, "v" + System.Reflection.AssemblyName.GetAssemblyName(dll).Version!.ToString(3));
        Assert.Contains("set \"VER=" + version[1..] + "\"", File.ReadAllText(Path.Combine(CompatSource, "Build.cmd")));
    }

    [Fact]
    public void Releases_and_reports_use_the_real_repos()
    {
        Assert.Equal("Coodsz/Coodsz-RealmOfThrones", RotRelease.Repo);
        Assert.Equal("Coodsz/Coodsz-RealmOfThrones-Crash-Reports", RotRelease.CrashRepo);
        Assert.True(RotRelease.UpdatesReady);
        Assert.True(RotRelease.ReportsReady);
        Assert.True(RotRelease.IsPlaceholder("PLACEHOLDER-ROT-REPO"));
        // The worker files RoT reports in the same repos when its vars are not set.
        Assert.Contains("env.ROT_CRASH_REPO || '" + RotRelease.CrashRepo + "'", SourceFiles.Read("server/mod-reports-worker/src/crash.js"));
        Assert.Contains("DEFAULT_ROT_REPO = '" + RotRelease.Repo + "'", SourceFiles.Read("server/mod-reports-worker/src/index.js"));

        var window = Window();
        Assert.Contains("if (RotRelease.UpdatesReady)", window);
        Assert.Contains("NoteLog(RotRelease.UpdatesNotSetUp);", window);
        Assert.Contains("ConfirmWindow.Ask(this, \"Send my logs\", RotRelease.ReportsNotSetUp + \" \" + LogsInstead, null, \"Open server logs\")", window);
        Assert.DoesNotContain("ReleaseNotice.BeRepo", window);
        // The crash bar and the real Send my logs only come once RotRelease names the crash repo.
        var setUp = SourceFiles.Method(window, "private void SetUpCrashReports()");
        InOrder(setUp, "if (!RotRelease.ReportsReady || !CrashReports.HasEndpoint())", "SendLogsButton(this, ShowReportsNotSetUp)", "return;",
            "new Coodsz.Ui.ServerCrashBar(this, CrashBarHost, RotRelease.ToolId, CrashRequest", "SendLogsButton(this, _crashBar.OpenLogs)");
        Assert.Contains("if (_crashBar == null || !CrashReports.ShowsServerCrashBar(exit))", SourceFiles.Method(window, "private void OfferServerCrashReport(Process process)"));
        var request = SourceFiles.Method(window, "private CrashReportRequest CrashRequest(");
        Assert.Contains("Tool = RotRelease.ToolId,", request);
        Assert.Contains("RotHostLogPack.Create(zip, serverRoot, dataRoot, programData, windowLog, includeDumps: false", request);
        Assert.Contains("Secrets = KnownPasswords(),", request);
        // The mod report window opens only from a mod row's Report link, with tool rot (MainWindow.Reports).
        Assert.Single(Regex.Matches(window, Regex.Escape("ModReportWindow.Show(")));
        var app = SourceFiles.Read(Path.Combine("src", "RealmOfThronesHost", "App.xaml.cs"));
        Assert.Contains("ToolData.Start(ToolData.RotServer);", app);
        Assert.Contains("LicenseOnce(\"rot-server\")", app);
        Assert.Contains("WhatsNewOnce(\"rot-server\", ReleaseNotice.RotVersion, WhatsNew.Rot)", app);
        Assert.Contains("FrameRules.RotServer", app);
    }

    [Fact]
    public void No_burning_empires_parts_or_text_reach_the_tool()
    {
        var folder = Path.Combine(SourceFiles.Root(), "src", "RealmOfThronesHost");
        foreach (var file in Directory.GetFiles(folder, "*.cs").Concat(Directory.GetFiles(folder, "*.xaml")))
        {
            var text = File.ReadAllText(file);
            foreach (var word in new[] { "Burning Empires", "BurningEmpires", "BESavedata", "Start-ModdedServer", "BeServerPrepare", "BeServerLaunch", "BeHostReport", "Install or repair" })
                Assert.False(text.Contains(word, StringComparison.Ordinal), Path.GetFileName(file) + " still has " + word);
        }
    }

    [Fact]
    public void The_tool_and_its_core_reach_no_address_but_the_allowed_ones()
    {
        // Nothing but these may be reached. The MCM Nexus Mods page is only named to players. The Encyclopedia pages (the
        // mod's wiki and tables) only open in the browser. The mod list is read from the public Realm of Thrones repo
        // (RotCompatList), whose MODS.md page is only named.
        string[] allowed =
        [
            "http://schemas.microsoft.com/winfx/2006/xaml", "pack://application:", "https://bannerlordcoop.com/cheats",
            "https://discord.gg/z8SncS7UX", "steam://url/CommunityFilePage/", "https://www.nexusmods.com/mountandblade2bannerlord/mods/612",
            "https://github.com/RealmofThrones/Wiki/wiki", "https://rotmod.notion.site/",
            RotCompatList.OnlineUrl, RotCompatList.ModsPageUrl
        ];
        var address = new Regex(@"[a-z][a-z0-9+.\-]*://[^\s""'<]*|github\.com|workers\.dev|mod-reports|r2\.dev|api\.github", RegexOptions.IgnoreCase);
        var checkedFiles = 0;
        foreach (var project in new[] { "RealmOfThronesHost", "RealmOfThrones.Core", "RealmOfThronesLauncher" })
        {
            var folder = Path.Combine(SourceFiles.Root(), "src", project);
            foreach (var file in Directory.GetFiles(folder, "*.cs", SearchOption.AllDirectories).Concat(Directory.GetFiles(folder, "*.xaml", SearchOption.AllDirectories))
                         .Where(file => !file.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) && !file.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar)))
            {
                checkedFiles++;
                foreach (Match match in address.Matches(File.ReadAllText(file)))
                    Assert.True(allowed.Any(prefix => match.Value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)), Path.GetFileName(file) + " reaches " + match.Value);
            }
        }

        Assert.True(checkedFiles > 20);
    }

    // ----- Encyclopedia -----

    [Fact]
    public void The_encyclopedia_list_has_the_nine_pages_in_order_on_the_two_allowed_hosts()
    {
        var links = RotEncyclopedia.Links;
        Assert.Equal(new[] { "Realm of Thrones wiki", "Troops", "Armors", "Shields", "Weapons", "Cultures", "Wanderers", "Kingdoms", "Factions and clans" },
            links.Select(link => link.Title));
        foreach (var link in links)
        {
            var uri = new Uri(link.Url);
            Assert.Equal(Uri.UriSchemeHttps, uri.Scheme);
            Assert.Contains(uri.Host, new[] { "github.com", "rotmod.notion.site" });
            Assert.False(string.IsNullOrWhiteSpace(link.Line), link.Title + " has no line.");
        }
        Assert.Equal("https://github.com/RealmofThrones/Wiki/wiki", links[0].Url);
        Assert.Equal(links.Count, links.Select(link => link.Url).Distinct().Count());
        // The Kingdoms view id was easy to mix up with the Shields one.
        Assert.Equal("https://rotmod.notion.site/1215d7c0cc8b80bda5b5ff85243fd962?v=1215d7c0cc8b81f98fe7000caa67a7e8", links[7].Url);
        // The note names the version the tools pin, so a new Realm of Thrones version brings the note up too.
        Assert.Contains("differ in " + RotStack.RotVersion.TrimStart('v')[..3] + ".", RotEncyclopedia.Note);
    }

    [Fact]
    public void The_encyclopedia_page_has_its_sidebar_entry_and_opens_links_through_shell_open()
    {
        var xaml = SourceFiles.Read(Path.Combine("src", "RealmOfThronesHost", "MainWindow.xaml"));
        Assert.Contains("x:Name=\"NavEncyclopedia\"", xaml);
        Assert.Contains("Content=\"Encyclopedia\" Tag=\"Encyclopedia\" Click=\"Nav_Click\"", xaml);
        Assert.Contains("<ScrollViewer x:Name=\"EncyclopediaPage\" Visibility=\"Collapsed\"", xaml);
        var window = Window();
        Assert.Contains("(\"Encyclopedia\", EncyclopediaPage),", window);
        Assert.Contains("NavLogs, NavEncyclopedia, NavGuides }", window);
        Assert.Contains("ShowEncyclopedia();", SourceFiles.Method(window, "public MainWindow()"));
        InOrder(SourceFiles.Method(window, "private void EncyclopediaLink_Click("), "ShellOpen.TryOpen(url, out _)", "RotEncyclopedia.CouldNotOpen");
    }

    // ----- Guides -----

    [Fact]
    public void The_guides_page_has_its_sidebar_entry_and_shows_the_server_guides()
    {
        var xaml = SourceFiles.Read(Path.Combine("src", "RealmOfThronesHost", "MainWindow.xaml"));
        Assert.Contains("x:Name=\"NavGuides\"", xaml);
        Assert.Contains("Content=\"Guides\" Tag=\"Guides\" Click=\"Nav_Click\"", xaml);
        Assert.Contains("<ContentControl x:Name=\"GuidesPage\" Visibility=\"Collapsed\"/>", xaml);
        var window = Window();
        Assert.Contains("(\"Guides\", GuidesPage),", window);
        Assert.Contains("GuideLibrary.For(BannerlordCoopLauncher.Core.Keep.GuideSet.RotServer)", SourceFiles.Method(window, "private void ShowPage("));
        Assert.Contains(@"<Compile Include=""..\TaomCoopHost\TaomGuidesPage.cs"" Link=""TaomGuidesPage.cs"" />", Project());
    }

    [Fact]
    public void Each_realm_of_thrones_tool_loads_its_own_guides()
    {
        Assert.Equal(new[] { "getting-started", "inviting-friends", "setups-and-mods", "host-rules", "not-finished-yet" },
            GuideLibrary.For(GuideSet.RotServer).Select(g => g.Id));
        Assert.Equal(new[] { "getting-started", "joining-a-friend", "mods-and-setups", "not-finished-yet", "common-problems" },
            GuideLibrary.For(GuideSet.RotClient).Select(g => g.Id));
        // The Burning Empires sets keep their own guides.
        Assert.Equal(7, GuideLibrary.For(GuideSet.BeServer).Count);
        Assert.Equal(4, GuideLibrary.For(GuideSet.BeClient).Count);
    }

    // Every button a guide tells the player to click, and the tool whose window must have it.
    public static TheoryData<GuideSet, string, string[]> GuideButtons => new()
    {
        { GuideSet.RotServer, "RealmOfThronesHost", ["Prepare server", "New campaign", "Name server copy", "Start server", "Share mod list", "Import mod list", "Auto sort",
            "New setup", "Copy", "Open the port on my router automatically", "Allow through firewall", "Advertise through Steam", "Save game", "Stop server", "Restart",
            "Last good save", "Kick player", "Ban player", "Unban player", "Save settings", "Import mod settings", "Back to this PC's values", "Save", "Open server logs",
            "Send my logs", "Saves", "Dashboard", "Mods", "Settings", "Logs"] },
        { GuideSet.RotClient, "RealmOfThronesLauncher", ["Import mod list", "Check mods", "Connect", "Direct", "Steam lobby", "New setup", "Copy", "Add mods", "Save mods",
            "Install mods", "Share mod list", "Auto sort", "Safe mode", "Find the crashing mod", "Open client logs", "Clear shader cache", "Mods", "Logs and help", "Settings"] },
    };

    [Theory]
    [MemberData(nameof(GuideButtons))]
    public void Realm_of_thrones_guides_keep_to_the_house_style_and_name_only_real_buttons(GuideSet set, string tool, string[] buttons)
    {
        var guides = GuideLibrary.For(set);
        foreach (var guide in guides)
        {
            Assert.NotEmpty(guide.Summary);
            Assert.All(guide.Sections, s => Assert.NotEmpty(s.Steps));
            foreach (var line in GuideLibrary.Lines(guide))
            {
                Assert.True(line.Length <= 160, "Too long for a step: " + line);
                Assert.False(char.IsLower(line[0]), "Starts with a small letter: " + line);
                Assert.DoesNotContain("{", line);
                foreach (var word in new[] { "TAOM", "Burning Empires", "http" })
                    Assert.DoesNotContain(word, line);
                // House style: no colon inside a sentence, no hyphen between words.
                Assert.DoesNotMatch(@":\s", line);
                Assert.DoesNotMatch(@"[A-Za-z]-[A-Za-z]", line);
            }
        }

        var text = string.Join("\n", guides.SelectMany(GuideLibrary.Lines));
        Assert.DoesNotMatch(@"\b[Pp]ress\b", text);
        var xaml = SourceFiles.Read(Path.Combine("src", tool, "MainWindow.xaml"));
        foreach (var button in buttons)
        {
            Assert.Contains(button, text);
            Assert.True(xaml.Contains("Content=\"" + button + "\"") || xaml.Contains("Header=\"" + button + "\""), tool + " has no " + button + " button.");
        }

        // The facts come from the tools' own lines, so a change there shows up here.
        Assert.Contains(RotUiText.WarSailsNeeded, text);
        Assert.Contains(RotUiText.WarSailsLine, text);
        Assert.Contains(RotUiText.OffInFirstVersion[0], text);
        Assert.Contains(RotUiText.OffInFirstVersion[1], text);
        Assert.Contains(RotSetupBook.CoreName + " is the locked setup", text);
        Assert.Contains("The first player to answer decides for everyone. If nobody answers in 60 seconds, the default goes ahead.", text);
        Assert.Contains("MCM settings", text);
        // RotRelease names the crash repo, so both tools have Send my logs and the guides point to it.
        Assert.Contains("Click Send my logs", text);
        if (set == GuideSet.RotServer)
        {
            Assert.Contains("click " + RotServerTerms.AcceptButton, text);
            Assert.Contains(@"C:\" + RotServerBuild.FolderName, text);
            Assert.Contains("UDP port 4200", text);
            Assert.Equal("host-rules", GuideLibrary.Search(guides, "coop.unstuck")[0].Id);
        }
        else
        {
            Assert.Contains(RotUiText.FirstJoinWait, text);
            Assert.Equal("common-problems", GuideLibrary.Search(guides, "shader cache")[0].Id);
        }
    }

    // ----- Its own data -----

    [Fact]
    public void The_server_data_and_mod_config_are_the_tools_own()
    {
        var window = Window();
        Assert.Contains("_settings.DataRoot = NightlyServer.DataDir(CoopDataRoot);", window);
        Assert.Contains("private static string CoopDataRoot => Path.Combine(ToolData.Folder, \"CoopData\");", window);
        Assert.Contains("NightlyServer.ModConfigPath(_settings.DataRoot)", window);
        Assert.DoesNotContain("CoopModConfig.UserPath()", window);
        Assert.Contains("rot-server", ToolData.RotServer.Key);
        Assert.Equal(ToolData.RealmOfThrones, ToolData.RotServer.Product);
    }

    // RoT live test: each Stop server click stacked another question, and its button said OK.
    [Fact]
    public void Stop_and_Restart_ask_one_question_at_a_time_with_a_button_that_says_what_it_does()
    {
        var window = Window();
        var ask = SourceFiles.Method(window, "private bool AskStopOrRestart(string action, string body)");
        Assert.Matches(@"if \(_askingStop\)\s+return false;\s+_askingStop = true;\s+try\s+\{\s+return ConfirmWindow\.Ask\(this, action, body, null, action\);\s+\}\s+finally\s+\{\s+_askingStop = false;", ask);
        Assert.Contains("AskStopOrRestart(\"Stop server\", ", SourceFiles.Method(window, "private void Stop_Click(object sender, RoutedEventArgs e)"));
        var restart = SourceFiles.Method(window, "private void Restart_Click(object sender, RoutedEventArgs e)");
        Assert.Contains("AskStopOrRestart(\"Restart server\", body)", restart);
        Assert.DoesNotContain("ConfirmWindow.Ask(", restart);
    }

    // RoT live test: the server's boot wrote hundreds of lines at a time, and every chunk printed the skip line again.
    [Fact]
    public void The_skip_line_for_a_big_log_chunk_comes_at_most_once_a_minute_with_the_lines_summed()
    {
        var skipped = new RotSkippedLines();
        var start = new DateTime(2026, 11, 12, 6, 0, 0, DateTimeKind.Utc);
        Assert.Null(skipped.Note(RotSkippedLines.MostShown, start));
        var first = skipped.Note(565, start);
        Assert.StartsWith("The server wrote 565 log lines in bursts. They stay in the log file.", first);
        Assert.Null(skipped.Note(300, start.AddSeconds(10)));
        Assert.Null(skipped.Note(5, start.AddSeconds(30)));
        Assert.Null(skipped.Note(200, start.AddSeconds(59)));
        // The lines held back come with the next chunk after the minute, even a small one that was shown.
        Assert.StartsWith("The server wrote 500 log lines in bursts.", skipped.Note(3, start.AddSeconds(61)));
        Assert.Null(skipped.Note(3, start.AddMinutes(5)));
        Assert.StartsWith("The server wrote 121 log lines", skipped.Note(121, start.AddMinutes(6)));

        var follow = SourceFiles.Method(Window(), "private void FollowServerLog()");
        Assert.Matches(@"if \(lines <= RotSkippedLines\.MostShown\)\s+\{\s+NoteLog\(chunk\);\s+MirrorServerLines\(chunk\);", follow);
        Assert.Contains("if (_skippedLines.Note(lines, DateTime.UtcNow) is { } skipped)", follow);
        Assert.DoesNotContain("log lines at once", follow);
    }

    // ----- The setup page -----

    [Fact]
    public void A_locked_setup_turns_off_every_button_that_would_change_it()
    {
        var show = SourceFiles.Method(Window(), "private void ShowModSetups()");
        foreach (var button in new[] { "DeleteModSetupButton", "AutoSortHostButton", "RenameHostSetupButton", "AddHostModsButton", "SaveHostModsButton" })
            Assert.Contains(button + ".IsEnabled = !locked;", show);
        Assert.Contains("LockedSetupText.Text = RotSetupBook.LockedNote(current.Name);", show);
        Assert.Contains("RotLoadOrder.ServerRows(shown)", show);
        Assert.Contains("Tip = RotSetupBook.CoreRowTip", show);
    }

    [Fact]
    public void Share_and_import_use_the_realm_of_thrones_list_and_import_into_a_locked_setup_makes_a_new_one()
    {
        var window = Window();
        Assert.Contains("SharedModList.Write(dialog.FileName, RotSharedList.Entries(HostSetup, DescribeServerModule));", window);
        var import = SourceFiles.Method(window, "private void ImportModList_Click(object sender, RoutedEventArgs e)");
        InOrder(import, "RotSharedList.ForeignGame(shared)", "if (setup.Locked)", "setup = _settings.ModSetups.Create();", "RotSharedList.Import(setup, shared, onThisPc)");
        Assert.Contains("RotAutoSort.Sort(setup, manifests)", window);
        Assert.Contains("TaomModFacts.Gather(roots)", window);
    }

    // ----- Start server -----

    [Fact]
    public void Start_server_runs_its_steps_in_order()
    {
        var start = SourceFiles.Method(Window(), "private async Task StartServerCoreAsync()");
        InOrder(start,
            "ServerFolderProblem(server)",
            "StartSave()",
            "ConfirmSaveMatchesSetup(save)",
            "AskServerTerms(server)",
            "await PrepareRotServerAsync(server)",
            "NightlyServer.ApplyConfig(dataDir, ServerToolsModule.Installed(server) ? RotServerTools.ServerModules(plan.Modules) : plan.Modules, saveName);",
            "NightlyServer.ApplyWarSails(dataDir, warSails: true, NightlyServer.ModConfigTemplate(server));",
            "ApplyKingdomProtection();",
            "NoteStart(RotUiText.WarSailsLine);",
            "NightlyServer.SeedSave(dataDir, server, saveName, warSails: true)",
            "NightlyServer.StartPlan(server, CoopDataRoot, acceptTerms == true, RotServerTools.StartVariables(_settings.DataRoot))",
            "process.Start()");
    }

    [Fact]
    public void Prepare_shows_the_problems_first_then_makes_the_map_then_stages_and_puts_the_map_in()
    {
        var window = Window();
        InOrder(SourceFiles.Method(window, "private async Task<RotServerPlan?> PrepareRotServerAsync(string serverRoot)"),
            "RotServerPath.Problems(plan, serverRoot, CarriesServerCoop())", "RotModChecks.Run(plan.Setup, HostModFacts())", "RotUiText.FirstProblem(problems)",
            "await MakeServerMapAsync(plan)", "RotServerPath.Stage(serverRoot, plan)", "await ApplyServerMapAsync(serverRoot, plan, map)",
            "RotServerTools.Install(serverRoot, ServerToolsAddOn)", "ShowKingdomProtection();");
        InOrder(SourceFiles.Method(window, "private async Task<ServerMap?> MakeServerMapAsync(RotServerPlan plan)"),
            "RotServerPath.MapStageFolder(ServerMapRoot, map.Version ?? \"\")", "RotServerPath.MapProblem(stage)",
            "RotServerPath.MapStageStart(compat.SourceDirectory, map.SourceDirectory, stage)", "return new ServerMap(stage, MadeNow: true);");
        InOrder(SourceFiles.Method(window, "private async Task<bool> ApplyServerMapAsync(string serverRoot, RotServerPlan plan, ServerMap map)"),
            "RotServerPath.ApplyMap(serverRoot, plan, map.Stage)", "_mapLine = RotServerPath.MapLine(map.MadeNow, copied);");
        Assert.Contains("SetControl(\"Server prepared. \" + _mapLine + \" Pick a Realm of Thrones save or make a new campaign, then click Start server.\");", window);
    }

    [Fact]
    public void The_start_removes_an_inherited_terms_answer_and_tester_switches_and_writes_utf8_without_a_byte_order_mark()
    {
        var start = SourceFiles.Method(Window(), "private async Task StartServerCoreAsync()");
        InOrder(start,
            "StandardInputEncoding = engine.InputEncoding,",
            "start.ArgumentList.Add(argument);",
            "foreach (var name in engine.RemoveVariables)",
            "start.Environment.Remove(name);",
            "foreach (var name in start.Environment.Keys.Where(RotServerBuild.TesterOnlyVariable).ToList())",
            "start.Environment.Remove(name);",
            "start.Environment[key] = value;");
        Assert.DoesNotContain("--accept-eula", Window());

        // A tester's leftover switch never reaches a server the tool starts; the loopback bind and the rest pass.
        foreach (var name in new[] { "COODSZ_ROT_STORY_TEST", "COODSZ_ROT_STORY_TEST_DELAY", "coodsz_rot_anyversion", "COODSZ_ROT_WORLDCHECK", "COODSZ_ROT_HOST", "COODSZ_ROT_HOST_JIT" })
            Assert.True(RotServerBuild.TesterOnlyVariable(name), name);
        foreach (var name in new[] { "COODSZ_TEST_LOOPBACK_BIND", "COODSZ_ROT_STORY", "COODSZ_ROT_PATIENCE", "COOP_DATA_DIR", "PATH" })
            Assert.False(RotServerBuild.TesterOnlyVariable(name), name);
        Assert.Contains("SetControl(RotServerTerms.Refused, failed: true);", start);
    }

    [Fact]
    public void The_terms_are_asked_like_the_burning_empires_tool_and_only_when_they_changed()
    {
        var ask = SourceFiles.Method(Window(), "private bool? AskServerTerms(string serverFolder)");
        InOrder(ask, "RotServerTerms.TermsHash(serverFolder)", "RotServerTerms.NeedsAnswer(hash, _settings.AcceptedEulaSha256)",
            "ConfirmWindow.Ask(this, RotServerTerms.Title, RotServerTerms.Body, RotServerTerms.TermsText(serverFolder), RotServerTerms.AcceptButton)");

        var server = Path.Combine(Path.GetTempPath(), "coodsz-rot-terms-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(server);
        try
        {
            Assert.Null(RotServerTerms.TermsText(server));
            Assert.Null(RotServerTerms.TermsHash(server));
            Assert.False(RotServerTerms.NeedsAnswer(null, null));
            File.WriteAllText(Path.Combine(server, "EULA.md"), "Terms\nline two");
            File.WriteAllText(Path.Combine(server, "LICENSE.txt"), "License");
            Assert.Equal("Terms\r\nline two\r\n\r\n--- LICENSE.txt ---\r\n\r\nLicense", RotServerTerms.TermsText(server));
            var hash = RotServerTerms.TermsHash(server);
            Assert.True(RotServerTerms.NeedsAnswer(hash, null));
            Assert.False(RotServerTerms.NeedsAnswer(hash, hash!.ToUpperInvariant()));
            File.WriteAllText(Path.Combine(server, "EULA.md"), "New terms");
            Assert.True(RotServerTerms.NeedsAnswer(RotServerTerms.TermsHash(server), hash));
        }
        finally
        {
            Directory.Delete(server, recursive: true);
        }

        Assert.Equal("DS_ACCEPT_EULA", RotServerTerms.EnvironmentVariable);
        Assert.Equal(6, RotServerTerms.ExitCode);
        Assert.DoesNotContain("Burning Empires", RotServerTerms.Body);
    }

    [Fact]
    public void The_world_watch_sends_save_when_the_world_is_ready_and_shows_a_failure()
    {
        var window = Window();
        var start = SourceFiles.Method(window, "private async Task StartServerCoreAsync()");
        Assert.Contains("var watch = new RotWorldWatch(newWorld: newCampaign != null);", start);
        Assert.Contains("WatchWorld(watch, args.Data);", start);
        var watch = SourceFiles.Method(window, "private void WatchWorld(RotWorldWatch watch, string? line)");
        InOrder(watch, "watch.Feed(line)", "RotWorldState.Failed", "RotServerNotes.WorldFailed(watch.FailedLine)", "RotWorldState.Ready", "ShowWorldSave()");

        Assert.Equal("The world is not right, so the tool did not save it. The server said [RealmOfThrones.CoopCompat] !!! World check FAILED.",
            RotServerNotes.WorldFailed("  [RealmOfThrones.CoopCompat] !!! World check FAILED "));
    }

    [Fact]
    public void An_exit_the_host_did_not_ask_for_shows_its_exit_hint()
    {
        var start = SourceFiles.Method(Window(), "private async Task StartServerCoreAsync()");
        Assert.Contains("if (!_stopAsked && exitCode is { } code && NightlyServer.ExitHint(code) is { } hint)", start);
        Assert.Contains("SetControl(hint, failed: true);", start);
    }

    [Fact]
    public void A_new_campaign_that_never_built_its_world_has_its_seed_removed_and_the_name_back()
    {
        var window = Window();
        var start = SourceFiles.Method(window, "private async Task StartServerCoreAsync()");
        // The run that exits by itself, and both ways the process can fail to start.
        InOrder(start, "NightlyServer.ExitHint(code)", "if (newCampaign != null)", "RemoveUnbuiltCampaign(saveName);", "OfferServerCrashReport(process);");
        InOrder(start, "SetControl(\"The server did not start. \" + ex.Message, failed: true);", "RemoveUnbuiltCampaign(saveName);",
            "SetControl(\"The server did not start.\", failed: true);", "RemoveUnbuiltCampaign(saveName);");
        // Stop and Restart: after the kill, before a restart starts again.
        InOrder(SourceFiles.Method(window, "private async Task SaveThenStop(bool restart)"),
            "var unbuilt = _runCreatesCampaign ? _settings.LastHostedSave : null;", "StopServer();", "RemoveUnbuiltCampaign(unbuilt);", "Start_Click(this, new RoutedEventArgs());");
        InOrder(SourceFiles.Method(window, "private void RemoveUnbuiltCampaign(string saveName)"),
            "NightlyServer.RemoveUnbuiltSeed(_settings.DataRoot, _settings.ServerRoot, saveName)", "_settings.NewCampaignName = NightlyServer.SaveStem(saveName);");
    }

    [Fact]
    public void Stop_server_never_stops_a_server_it_cannot_tell_is_its_own()
    {
        var stop = SourceFiles.Method(Window(), "private void StopServer()");
        InOrder(stop, "ownId = _process?.Id;", "Process.GetProcessesByName(\"BannerlordCoopServer\")",
            "var ours = path != null && !string.IsNullOrWhiteSpace(_settings.ServerRoot)", "DedicatedServerWatch.IsUnder(path, _settings.ServerRoot)",
            ": process.Id == ownId;", "if (!ours)", "process.Kill(entireProcessTree: true);");
    }

    [Fact]
    public void A_map_script_that_hangs_is_stopped_and_the_map_step_shows_a_moving_bar()
    {
        var window = Window();
        InOrder(SourceFiles.Method(window, "private static MapScriptResult RunMapScript(string stage, BannerlordCoopLauncher.Core.Server.NightlyServerStart start)"),
            "Path.GetFullPath(Path.Combine(ServerMapRoot, \"ServerMap\"))", "StartsWith(root + Path.DirectorySeparatorChar", "Directory.Delete(stage, recursive: true);",
            "if (!process.WaitForExit(MapScriptTimeout))", "process.Kill(entireProcessTree: true);", "return new MapScriptResult(lines, MapScriptFailed);");
        Assert.Contains("TimeSpan.FromMinutes(30)", window);
        InOrder(SourceFiles.Method(window, "private async Task<ServerMap?> MakeServerMapAsync(RotServerPlan plan)"),
            "_makingMap = true;", "ShowStartProgress(null);", "RunMapScript(", "_makingMap = false;", "HideStartProgress();");
        Assert.Contains("if ((fatal != null || !running || serving) && !_makingMap && !_makingServer)", window);
    }

    [Fact]
    public void The_first_run_strip_starts_with_choose_coop_server_and_ticks_prepare_only_once_it_ran()
    {
        var steps = SourceFiles.Method(Window(), "private void ShowFirstRunSteps(string? save)");
        InOrder(steps, "RotServerPath.ModulesRoot(_settings.ServerRoot), RotStack.CoreModuleId, \"SubModule.xml\"",
            "CarriesServerCoop() ? null : new FrameStep(Translator.T(\"Choose Coop server\"), hasServer", "PickCoopServer_Click(this, new RoutedEventArgs())",
            "Translator.T(\"Prepare server\"), prepared", "Translator.T(\"Start a new campaign, or use one you have\"), hasSave", "Translator.T(\"Start server\"), false",
            "}.OfType<FrameStep>().ToList());");
        Assert.Contains("_stepsStay = !_serverRunning && (needed || _stepsStay);", steps);
        Assert.DoesNotContain("OffInFirstVersion", steps);
        var xaml = SourceFiles.Read(Path.Combine("src", "RealmOfThronesHost", "MainWindow.xaml"));
        InOrder(xaml, "x:Name=\"ServerPage\"", "x:Name=\"ChooseServerCard\"", "<TextBlock Text=\"1\"", "Content=\"Choose Coop server\" Margin=\"0,0,8,0\" Click=\"PickCoopServer_Click\"",
            "x:Name=\"PrepareStepNumber\" Text=\"2\"", "Content=\"Prepare server\"", "x:Name=\"CampaignStepNumber\" Text=\"3\"", "Text=\"Start a new campaign, or use one you have\"", "Text=\"Or pick a campaign you have\"");
        // With the RoT build's server carried the Saves page has no Choose Coop server step, and its numbers follow.
        InOrder(Window(), "if (CarriesServerCoop())", "ChooseServerCard.Visibility = Visibility.Collapsed;", "PrepareStepNumber.Text = \"1\";", "CampaignStepNumber.Text = \"2\";",
            "SavesStepsIntro.Text = \"The steps are the same as on the Dashboard. Do 1 and 2 here, then click Start server on the Dashboard. \";", "PrepareStepText.Text = ");
    }

    [Fact]
    public void The_server_runs_from_the_folder_prepare_made_or_in_a_build_without_the_rot_server_the_one_the_host_picked()
    {
        var window = Window();
        var pick = SourceFiles.Method(window, "private void PickCoopServer_Click(object sender, RoutedEventArgs e)");
        InOrder(pick, "CoopInstall.ServerFolderIn(dialog.FolderName)", "NightlyServer.ExeName", "_settings.ServerRoot = server;");
        var problem = SourceFiles.Method(window, "private string? ServerFolderProblem(string? server)");
        InOrder(problem, "var carried = CarriesServerCoop();", "carried ? NoServerYet : \"Click Choose Coop server and pick the folder of the unpacked Coop server.\"",
            "? NightlyServer.ExeName + \" is not in \" + server + \". Click Prepare server.\"",
            ": NightlyServer.ExeName + \" is not in \" + server + \". Click Choose Coop server and pick the folder of the unpacked Coop server.\";",
            "if (RotServerBuild.IsUnfinished(server))", "return RotServerBuild.Unfinished;");
        Assert.Contains("private const string NoServerYet = \"There is no server yet. Click Prepare server to make it.\";", window);
        Assert.Contains("GetManifestResourceStream(RotServerBuild.ResourceName)", SourceFiles.Method(window, "private static bool CarriesServerCoop()"));
        Assert.DoesNotContain("DownloadCoopServer_Click", SourceFiles.Read(Path.Combine("src", "RealmOfThronesHost", "MainWindow.xaml")));
    }

    [Fact]
    public void Prepare_makes_the_rot_server_first_when_the_tool_carries_it_and_points_the_tool_at_it()
    {
        var window = Window();
        InOrder(SourceFiles.Method(window, "private async void Prepare_Click(object sender, RoutedEventArgs e)"),
            "if (_busy || _startingServer)", "if (ServerIsRunning())", "var carried = CarriesServerCoop();", "if (!carried)", "NoteLog(RotServerBuild.NotCarried);",
            "ServerFolderProblem(_settings.ServerRoot)", "_busy = true;", "if (carried && !await MakeRotServerAsync())", "await PrepareRotServerAsync(_settings.ServerRoot)",
            // Nothing a step throws can close the tool: the async void handler shows and logs a plain line.
            "catch (Exception ex)", "NoteLog(ex.ToString());", "SetControl(PrepareFailed, failed: true);", "_busy = false;");
        var make = SourceFiles.Method(window, "private async Task<bool> MakeRotServerAsync()");
        InOrder(make, "FindGameRoot()", "SetControl(GameMissingLine, failed: true);",
            "CoopStaging.FindDedicatedServer(libraries)", "AntivirusFiles.WorkshopServerProblem(CoopStaging.FindWorkshopModule(libraries), RotServerBuild.PrepareButton, null, libraries)",
            "RotServerBuild.GameProblem(game)", "var root = RotServerBuild.DefaultRoot;", "SetControl(RotServerBuild.Making(root));", "_makingServer = true;",
            "await Task.Run(", "GetManifestResourceStream(RotServerBuild.ResourceName)", "RotServerBuild.Build(workshop, game, zip, RotStack.CoopBuild.ServerZipSha256, root, line => SetControl(line))",
            "NoteStart(line);", "SetControl(ex.Message, failed: true);", "_makingServer = false;",
            "_settings.ServerRoot = root;", "SaveSettings();", "NoteStart(RotServerBuild.LeftOld(root, old));", "return true;");
        // The earlier server folder is never touched: the tool only stops pointing at it.
        Assert.DoesNotContain("Delete", make);
        Assert.Equal(@"C:\RotServer", RotServerBuild.DefaultRoot);
        Assert.Equal("Coodsz.RotCoop." + RotStack.CoopBuild.ServerZip, RotServerBuild.ResourceName);
        Assert.Contains("SetControl(GameMissingLine, failed: true);", SourceFiles.Method(window, "private async Task<RotServerPlan?> PrepareRotServerAsync(string serverRoot)"));
    }

    // ----- Notes and text -----

    [Fact]
    public void The_notes_list_what_is_off_and_that_war_sails_is_always_on()
    {
        Assert.Equal("War Sails is always on, because Realm of Thrones needs it.", RotUiText.WarSailsLine);
        var off = string.Join(" ", RotUiText.OffInFirstVersion);
        foreach (var word in new[] { "Enlistment", "duels", "White Walker start", "ravens", "Dragons are not tested in Coop", "first player to answer", "60 seconds" })
            Assert.Contains(word, off);
        Assert.Contains("RotUiText.OffInFirstVersion.Prepend(RealmOfThrones.Ui.RotUiText.WarSailsLine)", Window());
        Assert.Equal("", RotUiText.FirstProblem([]));
        Assert.Equal("A", RotUiText.FirstProblem(["A"]));
        Assert.Equal("A 2 more problems are in the log. Click Open server logs to see them.", RotUiText.FirstProblem(["A", "B", "C"]));
    }

    [Fact]
    public void Server_notes_follow_the_burning_empires_rules()
    {
        Assert.True(RotServerNotes.FirewallHintShows(null, @"C:\Server"));
        Assert.False(RotServerNotes.FirewallHintShows(@"C:\Server\", @"c:\server"));
        Assert.False(RotServerNotes.FirewallHintShows(@"C:\Server", ""));
        Assert.False(RotServerNotes.FirewallHintShows(null, @"C:\Server", firewallAllows: true));
        Assert.True(RotServerNotes.WaitsForSave(true));
        Assert.False(RotServerNotes.WaitsForSave(false));

        var found = new DateTime(2026, 11, 11, 9, 0, 0, DateTimeKind.Local);
        Assert.Null(RotServerNotes.UptimeFrom(null, DateTime.UtcNow));
        Assert.Equal(found, RotServerNotes.UptimeFrom(found, null));
        var click = found.AddHours(2).ToUniversalTime();
        Assert.Equal(click.ToLocalTime(), RotServerNotes.UptimeFrom(found, click));

        var setup = new TaomSetup { Name = "Mine" };
        Assert.Empty(RotServerNotes.ModNotes(setup));
        Assert.Null(RotLoadOrder.Add(setup, "BattleSizeResized", TaomModNotes.BattleSizeResizedId, null));
        Assert.Equal(new[] { RotServerNotes.BattleSizeResized }, RotServerNotes.ModNotes(setup));
    }
}
