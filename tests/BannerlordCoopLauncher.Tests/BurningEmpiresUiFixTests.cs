using System.Text.RegularExpressions;
using BurningEmpires.Core;
using BurningEmpires.Ui;

namespace BannerlordCoopLauncher.Tests;

// The review fixes on the Burning Empires server tool and client: the server tool can install
// Burning Empires, downloads found by themselves are only unpacked after a Yes, the status line
// shows one problem, the save box keeps to Burning Empires saves, and the player text has no jargon.
public sealed class BurningEmpiresUiFixTests
{
    private static string Host() => SourceFiles.Window(RepoRoot(), "BurningEmpiresHost");

    private static string Client() => SourceFiles.Window(RepoRoot(), "BurningEmpiresLauncher");

    private static string Src(string relative) => File.ReadAllText(Path.Combine(RepoRoot(), "src", relative));

    [Fact]
    public void The_download_question_names_each_file_with_its_size()
    {
        var question = BeUiText.DownloadQuestion("Burning Empires is missing.", "2.0.2",
            new[] { new BeFoundArchive(@"C:\Users\me\Downloads\BurningEmpires_main.7z", 5_583_457_484L), new BeFoundArchive(@"C:\Users\me\Downloads\BurningEmpires_Hotfix.rar", 12_000_000L) });
        Assert.StartsWith("Burning Empires is missing. Install Burning Empires from these downloads?", question);
        Assert.Contains(@"C:\Users\me\Downloads\BurningEmpires_main.7z (5.2 GB)", question);
        Assert.Contains(@"C:\Users\me\Downloads\BurningEmpires_Hotfix.rar (12 MB)", question);
        Assert.Contains("Your Burning Empires save data is not touched.", question);
        // A file someone else dropped in Downloads must not get in by a quick Yes.
        Assert.EndsWith("Click Yes only if you downloaded them yourself from Nexus Mods.", question);
        Assert.DoesNotContain("BESavedata", question);

        var repair = BeUiText.DownloadQuestion(null, "2.0.2", new[] { new BeFoundArchive(@"D:\BurningEmpires.7z", 0L) });
        Assert.StartsWith("Burning Empires 2.0.2 is ready. Put its files in again from these downloads to repair a damaged file?", repair);
        Assert.Contains(@"D:\BurningEmpires.7z (1 MB)", repair);
    }

    [Fact]
    public void The_status_line_shows_the_first_problem_and_counts_the_rest()
    {
        Assert.Equal("", BeUiText.FirstProblem(Array.Empty<string>()));
        Assert.Equal("One.", BeUiText.FirstProblem(new[] { "One." }));
        Assert.Equal("One. 1 more problem is in the log. Click Open server logs to see them.", BeUiText.FirstProblem(new[] { "One.", "Two." }));
        Assert.Equal("One. 2 more problems are in the log. Click Open server logs to see them.", BeUiText.FirstProblem(new[] { "One.", "Two.", "Three." }));

        // A Burning Empires line from the server tool goes first and points at step 3.
        var be = BeUiText.BeNotReady("2.0.2");
        Assert.Equal("Burning Empires 2.0.2 is not ready on this PC. Click Install or repair Burning Empires in step 3.", be);
        Assert.StartsWith(be + " 1 more problem", BeUiText.FirstProblem(new[] { "Burning Empires is not in the game's Modules folder.", "MCM is not downloaded." }, be));
    }

    [Fact]
    public void The_version_note_counts_the_mods_that_differ()
    {
        Assert.Null(BeUiText.DriftNote(0));
        Assert.Equal("1 mod version differs from the host. See the log.", BeUiText.DriftNote(1));
        Assert.Equal("3 mod versions differ from the host. See the log.", BeUiText.DriftNote(3));
    }

    [Fact]
    public void The_server_tool_installs_Burning_Empires_like_the_client()
    {
        var xaml = Src(@"BurningEmpiresHost\MainWindow.xaml");
        Assert.Contains("Content=\"Install or repair Burning Empires\" Click=\"InstallOrRepairBe_Click\"", xaml);
        Assert.Contains("Click Prepare server. If it says something is missing, follow that line.", xaml);
        Assert.Contains("Click Install or repair Burning Empires.", xaml);

        var host = Host();
        var install = SourceFiles.Method(host, "private async void InstallOrRepairBe_Click(");
        Assert.Contains("FindBeArchives()", install);
        Assert.Contains("unpack = AskUnpack(contentProblem, archives);", install);
        Assert.Contains("PickBeDownload(contentProblem, archives, hotfixOnly)", install);
        Assert.Contains("BeArchive.InstallCompat(addOn, gameRoot)", install);
        Assert.Contains("BeArchive.FindArchives(folders)", SourceFiles.Method(host, "private IReadOnlyList<string> FindBeArchives()"));
        Assert.Contains("BeUiText.DownloadQuestion(", SourceFiles.Method(host, "private bool AskUnpack("));
        Assert.Contains("BeArchive.InstallOverlay(archives, gameRoot, scratch, progress)", SourceFiles.Method(host, "private bool UnpackBe("));

        // Prepare server and Start server show one problem and say where Burning Empires is installed.
        Assert.Contains("BeUiText.FirstProblem(problems, BeLine(sources.GameRoot))", SourceFiles.Method(host, "private async Task<bool> PrepareServerAsync("));
        Assert.Contains("BeUiText.FirstProblem(problems, BeLine(sources.GameRoot))", SourceFiles.Method(host, "private async Task<BeServerStart?> PrepareBeServerAsync("));
    }

    [Fact]
    public void The_client_asks_before_it_unpacks_downloads_it_found()
    {
        var install = SourceFiles.Method(Client(), "private async void InstallOrRepair_Click(");
        var ask = install.IndexOf("BeUiText.DownloadQuestion(contentProblem", StringComparison.Ordinal);
        Assert.True(ask > 0);
        Assert.Contains("MessageBoxButton.YesNo", install[ask..]);
        // The question comes for every found download, not only for a repair.
        Assert.Contains("else if (archives.Count > 0)", install);
        Assert.True(ask < install.IndexOf("UnpackBe(archives", StringComparison.Ordinal));
    }

    [Fact]
    public void The_save_box_keeps_to_Burning_Empires_saves_in_the_shared_folder()
    {
        var host = Host();
        Assert.Contains("BeSaveVersion.IsBeOwned(path, serverModule)", SourceFiles.Method(host, "private bool IsBeSaveFile("));
        var fill = SourceFiles.Method(host, "private void FillSaves()");
        Assert.Contains("if (!IsBeSaveFile(file))", fill);
        Assert.Contains("IsBeSaveFile(_settings.SaveFile)", fill);
        Assert.Contains("IsBeSaveFile(Path.Combine(ServerSavesDirectory(), saveName + \".sav\"))", SourceFiles.Method(host, "private void PointConfigAt("));
        Assert.Contains("if (!IsBeSaveFile(selected.FullPath))", SourceFiles.Method(host, "private void NameSave_Click("));
        var delete = SourceFiles.Method(host, "private void DeleteSave_Click(");
        Assert.Contains("if (!IsBeSaveFile(selected.FullPath))", delete);
        Assert.Contains("shared with Coodsz Host", delete);

        // Start server and the server load test hand the shared Game Saves to Build, which refuses another tool's save.
        Assert.Contains("BeServerLaunch.Build(serverRoot, options, BeHostFolders.GameSaves(_settings.DataRoot))", host);
        Assert.Contains("AllowAddedModules = true }, BeHostFolders.GameSaves(owner._settings.DataRoot))", host);
    }

    [Fact]
    public void The_buttons_the_reports_name_are_on_the_server_tool()
    {
        // BeHostReport and the core checks tell the host to click these, so the labels must stay.
        var xaml = Src(@"BurningEmpiresHost\MainWindow.xaml");
        foreach (var label in new[] { "Unblock DLLs", "Restore save backup", "Last good save", "Install mods", "Install or repair Burning Empires", "Open server logs" })
            Assert.Contains("Content=\"" + label + "\"", xaml);
    }

    [Fact]
    public void New_campaign_and_Save_to_a_new_name_give_feedback()
    {
        var host = Host();
        Assert.Contains("NewCampaignBox.Clear();", SourceFiles.Method(host, "private void NewCampaign_Click("));
        var saveAs = SourceFiles.Method(host, "private async void SaveToNewName_Click(");
        Assert.Contains("TimeSpan.FromSeconds(20)", saveAs);
        Assert.Contains("SetControl(BeUiText.StillSaving)", saveAs);
        Assert.Equal("Still saving. Large worlds take a while.", BeUiText.StillSaving);
        Assert.Contains("like MyCampaign_202.", Src(@"BurningEmpiresHost\MainWindow.xaml"));
    }

    [Fact]
    public void Known_limits_are_on_screen_and_the_old_elephants_note_is_gone()
    {
        var host = Src(@"BurningEmpiresHost\MainWindow.xaml");
        Assert.Contains("War Sails and the Historical start are not supported.", Src(@"BurningEmpiresHost\MainWindow.Frame.cs"));
        // Elephants is core now, so the line under the setup picker went (live test).
        Assert.DoesNotContain("Pick Elephants only if all your players installed it.", host);
        Assert.DoesNotContain("ElephantsNote", host);
        Assert.Contains("That is a known issue in Burning Empires itself, not in Coop.", Src(@"BurningEmpiresHost\MainWindow.Frame.cs"));

        var client = Src(@"BurningEmpiresLauncher\MainWindow.xaml");
        // The client's Made for card ticks the versions (ShowMadeFor) and keeps the known limit as its note.
        Assert.Contains("<ui:MadeForCard x:Name=\"MadeForCard\"", client);
        Assert.Contains("War Sails and the Historical start are not supported.", Src(@"BurningEmpiresLauncher\MainWindow.Frame.cs"));
        Assert.Contains("x:Name=\"VersionDriftNote\"", client);
        Assert.Contains("ShowVersionDriftNote(warnings.Count);", SourceFiles.Method(Client(), "private void WarnVersionDrift()"));

        // The Setup row wraps, so long translations fit at the smallest window size.
        Assert.Matches("<WrapPanel>\\s*<Button x:Name=\"InstallOrRepairButton\"", client);
    }

    [Fact]
    public void Player_text_has_no_jargon()
    {
        var banned = new[] { "BUTR", "BESavedata", "co-op", "Co-op", "single-player", "Single-player", "fast-forward", "Wi-Fi", "port-forwarded", "3770450698", "Known issue:", "On: ", "Off: " };
        foreach (var project in new[] { "BurningEmpiresHost", "BurningEmpiresLauncher" })
        {
            var folder = Path.Combine(RepoRoot(), "src", project);
            foreach (var xaml in Directory.GetFiles(folder, "*.xaml"))
            {
                // Only the attribute values a player sees, not the XAML comments.
                foreach (Match value in Regex.Matches(File.ReadAllText(xaml), "(?:Text|Content|ToolTip|Header)=\"([^\"]*)\""))
                {
                    foreach (var word in banned)
                        Assert.False(value.Groups[1].Value.Contains(word, StringComparison.Ordinal), Path.GetFileName(xaml) + " shows " + value.Value);
                }
            }

            foreach (var file in Directory.GetFiles(folder, "*.cs"))
            {
                foreach (Match literal in Regex.Matches(File.ReadAllText(file), "\"(?:\\\\.|[^\"\\\\\\r\\n])*\""))
                {
                    foreach (var word in banned)
                        Assert.False(literal.Value.Contains(word, StringComparison.Ordinal), Path.GetFileName(file) + " shows " + literal.Value);
                }
            }
        }
    }

    [Fact]
    public void The_Discord_instructions_read_plainly()
    {
        var text = File.ReadAllText(Path.Combine(RepoRoot(), "Coodsz-BurningEmpires-Instructions.txt"));
        foreach (var word in new[] { "\u2014", "BESavedata", "BUTR", "anywhere on this PC", "in this thread", "item 3770450698", "Tools version:", "port forwarded IP" })
            Assert.DoesNotContain(word, text);
        Assert.Contains("PART 1, players", text);
        Assert.Contains("your IP address with the port forwarded", text);
        Assert.Contains("finds them in Downloads, on the Desktop or in the folder you picked last time", text);
        Assert.Contains("**Install or repair Burning Empires** next to it", text);
    }

    [Fact]
    public void The_publish_carries_the_UIExtenderEx_texts_and_leaves_the_compat_readme_out()
    {
        var host = Src(@"BurningEmpiresHost\BurningEmpiresHost.csproj");
        Assert.Contains(@"server-extra\UIExtenderEx-2.13.3\*.txt"" Payload=""ServerAddOns"" PayloadFolder=""UIExtenderEx-2.13.3\""", host);
        var client = Src(@"BurningEmpiresLauncher\BurningEmpiresLauncher.csproj");
        Assert.Contains(@"Exclude=""..\..\compat\BurningEmpiresCoopCompat\module\README.txt"" Payload=""ClientAddOns"" PayloadFolder=""BurningEmpires.CoopCompat\""", client);
        Assert.Contains(@"..\BurningEmpiresHost\BeUiText.cs", client);
        foreach (var name in new[] { "GPL-3.0.txt", "LICENSE-UIExtenderEx.txt", "SOURCE.txt" })
            Assert.True(File.Exists(Path.Combine(RepoRoot(), "compat", "BurningEmpiresCoopCompat", "server-extra", "UIExtenderEx-2.13.3", name)), name);
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "BannerlordCoopLauncher.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
