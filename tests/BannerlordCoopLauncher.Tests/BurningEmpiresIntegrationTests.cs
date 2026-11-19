using BannerlordCoopLauncher.Core.Localization;
using BurningEmpires.Core;
using TaomCoop.Core;

namespace BannerlordCoopLauncher.Tests;

// The Burning Empires tools wired into the repository: the solution, CI, the release script, the
// translation extractor, the ignored settings files, and the fixes the integration made across packages.
public sealed class BurningEmpiresIntegrationTests
{
    private static readonly string[] Projects = ["BurningEmpires.Core", "BurningEmpiresHost", "BurningEmpiresLauncher"];

    [Fact]
    public void The_solution_ci_and_text_extractor_know_the_three_projects()
    {
        var sln = SourceFiles.Read("BannerlordCoopLauncher.sln");
        var ci = SourceFiles.Read(".github/workflows/tests.yml");
        var extract = SourceFiles.Read("tools/ExtractUiText/Program.cs");
        foreach (var project in Projects)
        {
            Assert.Contains(@"src\" + project + @"\" + project + ".csproj", sln);
            Assert.Contains("\"" + project + "\"", extract);
        }

        foreach (var exe in new[] { "BurningEmpiresHost", "BurningEmpiresLauncher", "AD1259Host", "AD1259Launcher" })
            Assert.Contains("dotnet build src/" + exe + "/" + exe + ".csproj", ci);
        // The server half tests moved into this project, so there is no second test project to forget in CI.
        Assert.DoesNotContain("BurningEmpires.Core.Tests", sln);
        Assert.Contains(@"..\..\src\BurningEmpires.Core\BurningEmpires.Core.csproj", SourceFiles.Read("tests/BannerlordCoopLauncher.Tests/BannerlordCoopLauncher.Tests.csproj"));

        // The two locked setup names are names, like Plain TAOM, not text to translate.
        Assert.Contains("key == \"Plain Burning Empires\"", extract);
        Assert.Contains("key == \"Burning Empires with Elephants\"", extract);
    }

    [Fact]
    public void The_settings_files_with_the_password_are_ignored_and_the_compat_module_bin_is_not()
    {
        var ignore = SourceFiles.Read(".gitignore").Replace("\r\n", "\n").Split('\n');
        foreach (var file in new[] { "be-launcher.json", "be-server.json", "be-cheats.json", "be-bans.json", "be-host-mod-list.json" })
            Assert.Contains(file, ignore);
        Assert.Contains("!compat/BurningEmpiresCoopCompat/module/bin/", ignore);
        Assert.Contains("be-compat/MODS.md text eol=lf", SourceFiles.Read(".gitattributes"));
    }

    [Fact]
    public void The_server_path_uses_the_one_burning_empires_stack()
    {
        Assert.Equal(64, BeStack.BeDllSha256.Length);
        Assert.Equal(64, BeStack.DistanceCacheProbeDllSha256.Length);
        Assert.Equal(BeStack.BeDllSha256, BeServerPath.BeDllSha256);
        Assert.Equal(BeStack.DistanceCacheProbeDllSha256, BeServerPath.ProbeDllSha256);
        Assert.Equal(BeStack.ServerUiExtenderExSha256, BeServerPath.UIExtenderExSha256);
        Assert.Equal(BeStack.BeVersion, BeServerPath.BeVersion);
        Assert.Equal(BeStack.ElephantsVersion, BeServerPath.ElephantsVersion);
        Assert.Equal(BeStack.Butr.Select(mod => (mod.ModuleId, mod.WorkshopId, mod.Name)), BeServerPath.Butr);
        Assert.Equal(BeStack.BeNexusPage, BeServerPath.NexusUrl);
    }

    [Fact]
    public void Find_the_crashing_mod_halves_in_the_load_order_it_is_given()
    {
        var setup = new TaomSetup { Name = "Mine" };
        foreach (var id in new[] { "A", "B", "C", "D" })
            setup.Extras.Add(new TaomExtraMod { ModuleId = id, Name = id });

        var search = CrashBisect.Start(setup, order: s => s.Extras.Select(mod => mod.ModuleId).Reverse().ToList());
        Assert.Equal(["D", "C", "B", "A"], search.Suspects);
        Assert.Equal(["D", "C"], search.Testing);
        Assert.Contains("CrashBisect.Start(setup, Manifests(GatherModFacts()), BeLoadOrder.ClientOrder)",
            SourceFiles.Read("src/BurningEmpiresLauncher/MainWindow.Crash.cs"));
    }

    [Fact]
    public void The_client_memory_warning_names_burning_empires_and_keeps_its_translation()
    {
        var low = new MemoryState(16UL << 30, 1UL << 30, 95);
        var warning = LowMemory.ClientWarning(low, serverOnThisPc: false, game: BeStack.BeName)!;
        Assert.Contains("Burning Empires can freeze", warning);
        Assert.DoesNotContain("TAOM", warning);
        Assert.Contains("LowMemory.ClientWarning(LowMemory.Read(), LowMemory.DedicatedServerRunning(), game: BeStack.BeName)",
            SourceFiles.Read("src/BurningEmpiresLauncher/MainWindow.Support.cs"));

        // The game name is a placeholder in the three keys, so the TAOM and Burning Empires warnings share one translation.
        string[] keys =
        [
            "A dedicated server is running on this PC, which has {0} GB of memory. {1} and the server together can fill it and freeze the game. Run the server on another PC if you can, close heavy apps such as browsers, and let Windows manage the page file size.",
            "Low memory: only {0} GB of {1} GB is free, and a dedicated server is running on this PC too. {2} and the server together can fill it and freeze the game. Run the server on another PC if you can, close heavy apps such as browsers, and let Windows manage the page file size.",
            "Low memory: only {0} GB of {1} GB is free. {2} can freeze or close like this. Close heavy apps such as browsers before you play, and let Windows manage the page file size.",
        ];
        foreach (var language in Translator.Languages.Where(language => language.Code != Translator.English))
        {
            var table = Translator.Entries(language.Code);
            foreach (var key in keys)
            {
                Assert.True(table.TryGetValue(key, out var text), language.Code + " has no translation for: " + key);
                Assert.DoesNotContain("TAOM", text);
                Assert.Contains(key.Contains("{2}") ? "{2}" : "{1}", text);
            }
        }
    }

    [Fact]
    public void The_save_rescue_message_fits_both_server_tools()
    {
        var root = Directory.CreateTempSubdirectory("be-rescue-").FullName;
        try
        {
            var error = Assert.Throws<InvalidOperationException>(() => RescueSubModule.Install(root, Path.Combine(root, "missing.dll")));
            Assert.Contains("Coop Save Rescue is missing from", error.Message);
            Assert.DoesNotContain("TAOM", error.Message);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void The_release_script_refuses_to_build_without_the_official_uiextenderex()
    {
        var make = SourceFiles.Read("tools/Make-BurningEmpires-Release.ps1");
        Assert.Contains("BeVersion\\s*=", make);
        Assert.Contains(@"src\BurningEmpiresHost\BurningEmpiresHost.csproj", make);
        Assert.Contains(@"src\BurningEmpiresLauncher\BurningEmpiresLauncher.csproj", make);
        // One self contained file per program, like every other product since the two exe release.
        Assert.Contains("'--self-contained', 'true', '-p:PublishSingleFile=true'", make);
        Assert.DoesNotContain("'--self-contained', 'false'", make);
        // The DLL check comes before anything is built, and reads the hash and size from BeStack.
        var check = make.IndexOf("UIExtenderEx 2.13.3 is missing", StringComparison.Ordinal);
        Assert.True(check > 0 && check < make.IndexOf("Run 'dotnet' (@('test'", StringComparison.Ordinal));
        // The payload check sees UIExtenderEx and its texts inside the server exe's ServerAddOns.
        Assert.Contains("Assert-Payload $server \"$payloads\\server\" 'ServerAddOns'", make);
        Assert.Contains("(Get-PayloadPairs $uiFolder 'UIExtenderEx-2.13.3/')", make);
        Assert.Contains("ServerUiExtenderExSha256", make);
        Assert.Contains("ServerUiExtenderExBytes", make);
        Assert.Contains(BeStack.ServerUiExtenderExBundlePath.Replace(@"\Bannerlord.UIExtenderEx.dll", ""), make);
        foreach (var text in new[] { "LICENSE-UIExtenderEx.txt", "GPL-3.0.txt", "SOURCE.txt" })
            Assert.Contains(text, make);
        Assert.Contains("THIRD-PARTY-NOTICES.md", make);
        Assert.Contains("Coodsz/Coodsz-BurningEmpires", make);

        var notices = SourceFiles.Read("THIRD-PARTY-NOTICES.md");
        Assert.Contains("Bannerlord.UIExtenderEx 2.13.3", notices);
        Assert.Contains("GNU Lesser General Public License v3.0", notices);
    }
}
