using System.Text.Json;
using System.Text.RegularExpressions;
using BannerlordCoopLauncher.Core.Updates;

namespace BannerlordCoopLauncher.Tests;

// tools/release-assets.json lists the files every GitHub release must carry. TAOM v1.1.41 to
// v1.1.44 shipped without the CompatOnly zips the guide sends players to, so these tests keep the
// list, the guides and the release scripts in step.
public class ReleaseAssetsTests
{
    private const string Taom = "Coodsz/Coodsz-TAOM";
    private const string Friends = "Coodsz/Coodsz-Coop-and-Friends";
    private const string AD1259 = "Coodsz/Coodsz-AD1259";
    private const string Be = "Coodsz/Coodsz-BurningEmpires";
    private const string Rot = RealmOfThrones.Core.RotRelease.Repo;

    private sealed record Asset(string Name, string Source, string? Path, string? Sha256, bool Required,
        IReadOnlyList<string> Exactly, IReadOnlyList<string> Holds, bool Legacy, string Why);

    private static Dictionary<string, List<Asset>> Load()
    {
        using var json = JsonDocument.Parse(SourceFiles.Read("tools/release-assets.json"));
        var repos = new Dictionary<string, List<Asset>>();
        foreach (var repo in json.RootElement.GetProperty("repos").EnumerateArray())
        {
            var assets = repo.GetProperty("assets").EnumerateArray().Select(asset => new Asset(
                asset.GetProperty("name").GetString()!,
                asset.TryGetProperty("source", out var source) ? source.GetString()! : "build",
                asset.TryGetProperty("path", out var path) ? path.GetString() : null,
                asset.TryGetProperty("sha256", out var sha) ? sha.GetString() : null,
                !asset.TryGetProperty("required", out var required) || required.GetBoolean(),
                Strings(asset, "exactly"),
                Strings(asset, "holds"),
                asset.TryGetProperty("legacy", out var legacy) && legacy.GetBoolean(),
                asset.TryGetProperty("why", out var why) ? why.GetString()! : "")).ToList();
            repos.Add(repo.GetProperty("repo").GetString()!, assets);
        }

        return repos;
    }

    private static IReadOnlyList<string> Strings(JsonElement asset, string name) =>
        asset.TryGetProperty(name, out var list) ? list.EnumerateArray().Select(item => item.GetString()!).ToList() : [];

    private static readonly Dictionary<string, string> ProductOf = new()
    {
        [Friends] = "Coop",
        [Taom] = "TAOM",
        [AD1259] = "AD1259",
        [Be] = "BurningEmpires",
        [Rot] = "RealmOfThrones"
    };

    [Theory]
    [InlineData(Friends, "Coodsz Coop Tools")]
    [InlineData(Taom, "Coodsz TAOM Tools")]
    [InlineData(AD1259, "Coodsz AD1259 Tools")]
    [InlineData(Be, "Coodsz Burning Empires Tools")]
    [InlineData(Rot, "Coodsz Realm of Thrones Tools")]
    public void Each_release_has_one_two_exe_zip_with_exactly_its_new_exes(string repo, string folder)
    {
        var tools = ToolNames.All.Where(tool => tool.Product == ProductOf[repo]).ToList();
        var zip = Load()[repo].Single(asset => asset.Exactly.Count > 0);

        Assert.Equal(("build", true, false), (zip.Source, zip.Required, zip.Legacy));
        Assert.Empty(zip.Holds);
        Assert.Equal(tools[0].NewZipPrefix + "{version}.zip", zip.Name);
        Assert.All(tools, tool => Assert.True(tool.IsNewZip(zip.Name.Replace("{version}", "1.3.0"))));
        Assert.Equal(2, zip.Exactly.Count);
        Assert.Equal(tools.Select(tool => folder + "/" + tool.NewExeName).Order(), zip.Exactly.Order());
    }

    [Theory]
    [InlineData(Friends)]
    [InlineData(Taom)]
    [InlineData(AD1259)]
    public void The_legacy_zips_are_marked_temporary_and_hold_every_old_exe_name(string repo)
    {
        var legacy = Load()[repo].Where(asset => asset.Legacy).ToList();
        Assert.NotEmpty(legacy);
        Assert.All(legacy, asset =>
        {
            Assert.True(asset.Required);
            Assert.StartsWith("TEMPORARY legacy zip.", asset.Why);
            Assert.NotEmpty(asset.Holds);
            Assert.Empty(asset.Exactly);
        });

        // The shipped updater refuses a zip without its own exe name (VerifyDownload), anywhere in the zip.
        var held = legacy.SelectMany(asset => asset.Holds).Select(path => path.Split('/').Last()).ToList();
        foreach (var tool in ToolNames.All.Where(tool => tool.Product == ProductOf[repo]))
        {
            var installed = tool.OldExeNames.Count > 0 ? tool.OldExeNames : [tool.NewExeName];
            foreach (var exe in installed)
                Assert.Contains(exe, held);
        }

        if (repo == Friends)
            Assert.Contains("Coodsz and Friends.exe", held);
    }

    [Theory]
    [InlineData(Friends)]
    [InlineData(Taom)]
    [InlineData(AD1259)]
    public void The_shipped_updater_finds_a_legacy_zip_holding_its_exe_in_the_listed_assets(string repo)
    {
        var assets = Load()[repo];
        var names = assets.Select(asset => asset.Name.Replace("{version}", "1.3.0")).ToList();
        var old = ToolNames.All.Where(tool => tool.Product == ProductOf[repo])
            .SelectMany(tool => tool.OldExeNames.Count > 0 ? tool.OldExeNames : [tool.NewExeName]);
        foreach (var exe in old)
        {
            var picked = ShippedPickZipTests.Shipped191be48.PickZip(exe, names);
            Assert.NotNull(picked);
            var asset = assets.Single(item => item.Name.Replace("{version}", "1.3.0") == picked);
            Assert.True(asset.Legacy, exe + " picked " + picked + ", which is not a legacy zip.");
            Assert.Contains(asset.Holds, path => path.Split('/').Last() == exe);
        }
    }

    // Burning Empires was never released, so no installed updater looks for an old zip name.
    [Fact]
    public void Burning_empires_ships_only_the_two_exe_zip_and_no_legacy_zips()
    {
        var be = Load()[Be];
        Assert.DoesNotContain(be, asset => asset.Legacy || asset.Holds.Count > 0);
        Assert.Equal(["Coodsz-Burning-Empires-Tools-v{version}.zip"], be.Where(asset => asset.Name.EndsWith(".zip", StringComparison.Ordinal)).Select(asset => asset.Name));
        Assert.All(ToolNames.All.Where(tool => tool.Product == "BurningEmpires"), tool => Assert.Empty(tool.OldZipPatterns));
    }

    // Realm of Thrones ships no legacy zips. The two zips of the RoT build of Coop ride only inside the exes
    // (RotStack.CoopBuild, src/RotCoopBuild.targets), so they are not release files; the release script still
    // checks the pins, the exes and the payloads.
    [Fact]
    public void Realm_of_thrones_ships_the_two_exe_zip_and_no_Coop_zips_and_no_legacy_zips()
    {
        var rot = Load()[Rot];
        Assert.DoesNotContain(rot, asset => asset.Legacy || asset.Holds.Count > 0);
        Assert.All(ToolNames.All.Where(tool => tool.Product == "RealmOfThrones"), tool => Assert.Empty(tool.OldZipPatterns));
        Assert.DoesNotContain(rot, asset => asset.Source == "release" || asset.Name.StartsWith("Coop-RoT-", StringComparison.Ordinal));
        Assert.Equal(["Coodsz-Realm-of-Thrones-Tools-v{version}.zip", "LICENSE.txt", "README.-.Instructions.txt"], rot.Select(asset => asset.Name).Where(name => name != "SHA256SUMS.txt").Order(StringComparer.Ordinal));

        var byName = rot.ToDictionary(asset => asset.Name);
        var license = byName["LICENSE.txt"];
        Assert.Equal(("repo", "LICENSE", true, Sha256Of("LICENSE")), (license.Source, license.Path, license.Required, license.Sha256));
        // The license names the Realm of Thrones tools, with the same hash as TAOM and Burning Empires.
        Assert.Contains("GNU GENERAL PUBLIC LICENSE", SourceFiles.Read("LICENSE"));
        Assert.Equal(license.Sha256, Load()[Taom].Single(asset => asset.Name == "LICENSE.txt").Sha256);
        var instructions = byName["README.-.Instructions.txt"];
        Assert.Equal(("repo", "Coodsz-RealmOfThrones-Instructions.txt", true), (instructions.Source, instructions.Path, instructions.Required));

        var make = SourceFiles.Read("tools/Make-RealmOfThrones-Release.ps1");
        Assert.Contains(@"Assert-Payload $server ""$payloads\server"" 'ServerAddOns'", make);
        Assert.Contains(@"Assert-Payload $client ""$payloads\client"" 'ClientAddOns'", make);
        Assert.Contains("Assert-CarriedCoopZips $server @($coopZips.Client.Path, $coopZips.Server.Path)", make);
        Assert.Contains("Assert-CarriedCoopZips $client @($coopZips.Client.Path) @($coopZips.Server.Name)", make);
        Assert.Contains(@"Get-Content -Raw -LiteralPath 'src\RealmOfThrones.Core\RotStack.cs'", make);
        // RotRepo is the real repo, and a placeholder one would only make an unsigned test build.
        Assert.Equal("Coodsz/Coodsz-RealmOfThrones", Rot);
        Assert.Contains("$repo.StartsWith('PLACEHOLDER-ROT', [StringComparison]::Ordinal) -and -not $SkipSigning", make);
        // A zip from an earlier build in the output folder stops the build before anything is made, so
        // false-positive-submission.md (every zip in the folder) lists only this release's files.
        var steps = new[] { "$releaseNames = @(Get-ReleaseAssetSpec $repo $version | ForEach-Object { $_.Name })",
            "Get-ChildItem -LiteralPath $Output -Filter '*.zip' -File | Where-Object { $releaseNames -notcontains $_.Name }",
            "holds zips this release does not have", "Run 'dotnet'", "Write-ReleasePageFiles $repo" }.Select(step => make.IndexOf(step, StringComparison.Ordinal)).ToList();
        Assert.DoesNotContain(-1, steps);
        Assert.Equal(steps.Order(), steps);
        Assert.Contains("foreach ($zip in @(Get-ChildItem -LiteralPath $Folder -Filter '*.zip' -File", SourceFiles.Read("tools/ReleaseAssets.ps1"));
    }

    // With a real RotRepo, a signed release stops while the instructions or the notes still hold <RELEASE-...>.
    // Runs the real script in a copy of the files it reads first, with small guide and notes files of its own, so
    // the test still passes once the real guide has its release link; once both are filled in it gets past the
    // check and stops at RotStack.cs, which the copy leaves out.
    [Fact]
    public void Realm_of_thrones_release_stops_while_the_guide_or_notes_hold_a_release_placeholder()
    {
        var root = Path.Combine(Path.GetTempPath(), "coodsz-rot-release-" + Guid.NewGuid().ToString("N"));
        try
        {
            var notes = "docs/release-notes/v" + RealmOfThrones.Core.RotRelease.Version.TrimStart('v') + "-realmofthrones.md";
            foreach (var file in new[] { "tools/Make-RealmOfThrones-Release.ps1", "tools/ReleaseAssets.ps1", "tools/PayloadCheck.ps1", "tools/release-assets.json",
                "src/BannerlordCoopLauncher.Core/Updates/ReleaseNotice.cs", "src/BannerlordCoopLauncher.Core/Realm/RealmRewardCode.cs", "src/RealmOfThronesHost/RealmOfThronesHost.csproj",
                "src/RealmOfThronesLauncher/RealmOfThronesLauncher.csproj" })
            {
                var to = Path.Combine(root, file);
                Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                File.WriteAllText(to, SourceFiles.Read(file).Replace(Rot, "Coodsz/Coodsz-RealmOfThrones"));
            }
            var guide = Path.Combine(root, "Coodsz-RealmOfThrones-Instructions.txt");
            File.WriteAllText(guide, "Download the tools from <RELEASE-LINK>.\r\n");
            var notesFile = Path.Combine(root, notes);
            Directory.CreateDirectory(Path.GetDirectoryName(notesFile)!);
            File.WriteAllText(notesFile, "<!-- Put <RELEASE-REPO> and <RELEASE-LINK> in before publishing. -->\r\n\r\nFrom <RELEASE-REPO>: <RELEASE-LINK>\r\n");

            // Without the private Realm reward key it stops before anything else (CONTRIBUTING.md, Private files).
            Assert.Contains("The Realm reward key is missing", RunRotRelease(root));
            Directory.CreateDirectory(Path.Combine(root, "private"));
            File.WriteAllText(Path.Combine(root, "private", "realm-reward-key.txt"), new string('0', 64));
            File.WriteAllText(Path.Combine(root, "private", "realm-reward-old-key.txt"), new string('1', 64));

            Assert.Contains("Coodsz-RealmOfThrones-Instructions.txt still has a <RELEASE-...> placeholder", RunRotRelease(root));

            File.WriteAllText(guide, File.ReadAllText(guide).Replace("<RELEASE-LINK>", "https://github.com/Coodsz/Coodsz-RealmOfThrones/releases/latest"));
            Assert.Contains(notes.Replace('/', '\\') + " still has a <RELEASE-...> placeholder", RunRotRelease(root));

            // The notes' leading comment names the placeholders and never reaches players, so it may keep them.
            var text = File.ReadAllText(notesFile);
            var body = text.IndexOf("-->", StringComparison.Ordinal) + 3;
            File.WriteAllText(notesFile, text[..body] + text[body..].Replace("<RELEASE-REPO>", "Coodsz/Coodsz-RealmOfThrones").Replace("<RELEASE-LINK>", "https://github.com/Coodsz/Coodsz-RealmOfThrones/releases/latest"));
            var output = RunRotRelease(root);
            Assert.DoesNotContain("<RELEASE-", output);
            Assert.Contains("RotStack.cs", output);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    // The Realm of Thrones and Burning Empires release scripts refuse every GitHub token kind and a Discord webhook
    // link in the exes, by the token's shape so a bare prefix in compressed bytes is no false alarm.
    [Fact]
    public void Release_scripts_refuse_every_github_token_kind_and_discord_webhooks()
    {
        const string pattern = "github_pat_[A-Za-z0-9_]{20,}|gh[pousr]_[A-Za-z0-9]{20,}|(?i:discord(?:app)?\\.com/api/webhooks/)";
        foreach (var script in new[] { "tools/Make-RealmOfThrones-Release.ps1", "tools/Make-BurningEmpires-Release.ps1" })
        {
            var make = SourceFiles.Read(script);
            Assert.Contains("$found = [regex]::Match($text, '" + pattern + "')", make);
            Assert.DoesNotContain("foreach ($prefix in @('github_pat_', 'ghp_'))", make);
        }

        var token = new string('a', 36);
        foreach (var bad in new[] { "github_pat_" + token, "ghp_" + token, "gho_" + token, "ghu_" + token, "ghs_" + token, "ghr_" + token,
            "https://discord.com/api/webhooks/1/x", "https://discordapp.com/api/webhooks/1/x", "https://Discord.com/api/webhooks/1/x" })
            Assert.Matches(pattern, "x" + bad + "x");
        foreach (var fine in new[] { "ghp_", "ghs_short", "github_pat_", "ghx_" + token, "discord.com/invite/abc" })
            Assert.DoesNotMatch(pattern, fine);
    }

    private static string RunRotRelease(string root)
    {
        var start = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = "-NoProfile -ExecutionPolicy Bypass -File \"" + Path.Combine(root, "tools", "Make-RealmOfThrones-Release.ps1") + "\" -IgnoreLock -SkipTests",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        // CI runs the tests from PowerShell 7, whose module path makes Windows PowerShell load the wrong modules.
        start.Environment.Remove("PSModulePath");
        // The copy's own private folder, never the real one.
        start.Environment.Remove("CoodszPrivateRoot");
        using var process = System.Diagnostics.Process.Start(start)!;
        var errors = process.StandardError.ReadToEndAsync();
        var output = process.StandardOutput.ReadToEnd();
        Assert.True(process.WaitForExit(120000), "The release script did not finish.");
        return output + errors.Result;
    }

    [Fact]
    public void No_asset_name_can_catch_the_old_CoodszHost_rule()
    {
        // The shipped CoodszHost takes a zip with Coodsz-Host or CoodszHost in its name before it falls
        // back to Coop-and-Friends, so such a name would pull it off its legacy zip.
        foreach (var asset in Load().Values.SelectMany(list => list))
        {
            Assert.DoesNotContain("CoodszHost", asset.Name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Coodsz-Host", asset.Name, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void The_release_check_opens_the_zips_and_the_release_scripts_check_them_too()
    {
        var shared = SourceFiles.Read("tools/ReleaseAssets.ps1");
        Assert.Contains("Exactly  = @(Get-JsonValue $asset 'exactly' @()", shared);
        Assert.Contains("Holds    = @(Get-JsonValue $asset 'holds' @()", shared);
        Assert.Contains("Test-ZipContents $file $asset.Exactly $asset.Holds", shared);
        var check = SourceFiles.Read("tools/Check-ReleaseAssets.ps1");
        Assert.Contains("Test-ZipContents $zip $asset.Exactly $asset.Holds", check);
        Assert.Contains("[switch] $SkipZipContents", check);
        Assert.Contains("TEMPORARY legacy zip", check);
    }

    [Fact]
    public void The_list_loads_with_the_four_release_repos_and_the_unreleased_realm_of_thrones_one()
    {
        var repos = Load();
        Assert.Equal([Taom, Friends, AD1259, Be, Rot], repos.Keys);
        foreach (var asset in repos.Values.SelectMany(list => list))
        {
            Assert.Contains(asset.Source, new[] { "build", "repo", "release" });
            if (asset.Sha256 != null)
                Assert.Matches("^[0-9a-f]{64}$", asset.Sha256);
            // A file that is not built here can only be checked by its exact content.
            if (asset.Source == "release")
                Assert.NotNull(asset.Sha256);
            if (asset.Source == "repo")
                Assert.False(string.IsNullOrEmpty(asset.Path));
        }

        foreach (var list in repos.Values)
            Assert.Equal(list.Count, list.Select(asset => asset.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void Taom_needs_both_CompatOnly_zips_with_their_exact_hash_and_the_license()
    {
        var taom = Load()[Taom].ToDictionary(asset => asset.Name);
        var client = taom["TAOM-Coop-v0.3.25-CompatOnly-CLIENT-PLAYTEST-1675bea7-a2ac41c2-r2.zip"];
        var host = taom["TAOM-Coop-v0.3.25-CompatOnly-HOST-OPERATOR-1675bea7-a2ac41c2-r2.zip"];
        Assert.Equal(("release", true, "621ec05f1439c9d236e4dc7cc2ba2712e96b810ddd15ad70e7726b8812ed42ec"), (client.Source, client.Required, client.Sha256));
        Assert.Equal(("release", true, "ecb996f6daedd329f8423323bb302900fe1c0e2269e043e21938120be4b3d672"), (host.Source, host.Required, host.Sha256));
        Assert.True(taom["Coodsz-Dedicated-TAOM-Server-Tool-v{version}.zip"].Required);
        Assert.True(taom["Coodsz-TAOM-Client-Launcher-v{version}.zip"].Required);
        var license = taom["LICENSE.txt"];
        Assert.Equal(("repo", "LICENSE", true), (license.Source, license.Path, license.Required));
    }

    [Fact]
    public void Burning_empires_carries_the_license_with_the_taom_hash_and_its_instructions()
    {
        var be = Load()[Be].ToDictionary(asset => asset.Name);
        var license = be["LICENSE.txt"];
        Assert.Equal(("repo", "LICENSE", true), (license.Source, license.Path, license.Required));
        // Burning Empires, TAOM and Realm of Thrones pin the same LICENSE.txt, the one in the repo.
        Assert.Equal(Sha256Of("LICENSE"), license.Sha256);
        Assert.Equal(license.Sha256, Load()[Taom].Single(asset => asset.Name == "LICENSE.txt").Sha256);
        var instructions = be["README.-.Instructions.txt"];
        Assert.Equal(("repo", "Coodsz-BurningEmpires-Instructions.txt", true), (instructions.Source, instructions.Path, instructions.Required));
        Assert.Contains("https://github.com/" + Be + "/releases/latest", SourceFiles.Read(instructions.Path!));
        Assert.Contains("LICENSE.txt", SourceFiles.Read(instructions.Path!));
        Assert.Contains("Version 3, 29 June 2007", SourceFiles.Read("LICENSE"));
        Assert.DoesNotContain(be.Values, asset => asset.Source == "release");
    }

    [Fact]
    public void Every_repo_has_its_first_release_so_none_is_left_out_of_the_check()
    {
        // Burning Empires v1.0.0 is out, so Check-ReleaseAssets.ps1 without -Repo checks every released repo. A new
        // product's repo is marked unreleased until its first release. Realm of Thrones v0.1.0 is out too, so none is.
        using var json = JsonDocument.Parse(SourceFiles.Read("tools/release-assets.json"));
        var unreleased = json.RootElement.GetProperty("repos").EnumerateArray()
            .Where(repo => repo.TryGetProperty("unreleased", out var flag) && flag.GetBoolean())
            .Select(repo => repo.GetProperty("repo").GetString()).ToList();
        Assert.Empty(unreleased);
        Assert.Contains("'unreleased'", SourceFiles.Method(SourceFiles.Read("tools/ReleaseAssets.ps1"), "function Get-ReleaseAssetRepos"));
    }

    [Fact]
    public void A_repo_file_in_the_list_has_the_hash_written_there()
    {
        foreach (var asset in Load().Values.SelectMany(list => list).Where(asset => asset.Source == "repo" && asset.Sha256 != null))
        {
            var hash = Sha256Of(asset.Path!);
            Assert.True(hash == asset.Sha256, $"{asset.Path} changed. Put its new SHA256 {hash} in tools/release-assets.json.");
        }
    }

    private static string Sha256Of(string path)
    {
        using var stream = File.OpenRead(Path.Combine(RepoRoot(), path));
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream)).ToLowerInvariant();
    }

    [Theory]
    [InlineData("Coodsz-TAOM-Instructions.txt")]
    [InlineData("README.txt")]
    public void Every_TAOM_Coop_zip_the_guide_names_is_in_the_list(string guide)
    {
        var text = SourceFiles.Read(guide);
        var named = Regex.Matches(text, @"TAOM-Coop-v[0-9.]+-CompatOnly-[A-Za-z0-9.-]+?\.zip").Select(match => match.Value).Distinct().ToList();
        Assert.Equal(2, named.Count);
        var listed = Load()[Taom].Where(asset => asset.Required).Select(asset => asset.Name).ToList();
        foreach (var zip in named)
            Assert.Contains(zip, listed);
        Assert.Contains("https://github.com/Coodsz/Coodsz-TAOM/releases/latest", text);
    }

    [Fact]
    public void The_built_zip_names_match_the_release_scripts_and_the_updater()
    {
        var make = SourceFiles.Read("tools/Make-Release.ps1");
        var makeAd = SourceFiles.Read("tools/Make-AD1259-Release.ps1");
        var makeBe = SourceFiles.Read("tools/Make-BurningEmpires-Release.ps1");
        var makeRot = SourceFiles.Read("tools/Make-RealmOfThrones-Release.ps1");
        var repos = Load();
        string Built(string repo, string start) => repos[repo].Single(asset => asset.Source == "build" && asset.Name.StartsWith(start, StringComparison.Ordinal)).Name;

        Assert.Contains(Built(Taom, "Coodsz-Dedicated").Replace("{version}", "$taomVersion"), make);
        Assert.Contains(Built(Taom, "Coodsz-TAOM-Client").Replace("{version}", "$taomVersion"), make);
        Assert.Contains(Built(Friends, "Coodsz-Coop-and-Friends").Replace("{version}", "$version"), make);
        Assert.Contains(Built(AD1259, "Coodsz-AD1259-Server").Replace("{version}", "$version"), makeAd);
        Assert.Contains(Built(AD1259, "Coodsz-AD1259-Client").Replace("{version}", "$version"), makeAd);

        Assert.Contains(Built(Taom, "Coodsz-TAOM-Tools").Replace("{version}", "$taomVersion"), make);
        Assert.Contains(Built(Friends, "Coodsz-Coop-Tools").Replace("{version}", "$version"), make);
        Assert.Contains(Built(AD1259, "Coodsz-AD1259-Tools").Replace("{version}", "$version"), makeAd);
        Assert.Contains(Built(Be, "Coodsz-Burning-Empires-Tools").Replace("{version}", "$version"), makeBe);
        Assert.Contains(Built(Rot, "Coodsz-Realm-of-Thrones-Tools").Replace("{version}", "$version"), makeRot);

        // The updater in this build takes the two exe zip for every tool, old name or new.
        string Name(string repo, string start) => Built(repo, start).Replace("{version}", "1.0.0");
        var taomNames = repos[Taom].Select(asset => asset.Name.Replace("{version}", "1.0.0")).ToList();
        foreach (var exe in new[] { "Coodsz Dedicated TAOM Server Tool.exe", "Coodsz TAOM Server Tool.exe", "Coodsz TAOM Client Launcher.exe" })
            Assert.Equal(Name(Taom, "Coodsz-TAOM-Tools"), ToolUpdate.PickZip(exe, taomNames));
        var friendsNames = repos[Friends].Select(asset => asset.Name.Replace("{version}", "1.0.0")).ToList();
        foreach (var exe in new[] { "CoodszHost.exe", "Coodsz Coop and Friends.exe", "Coodsz and Friends.exe", "Coodsz Coop Server Tool.exe", "Coodsz Coop Client Launcher.exe" })
            Assert.Equal(Name(Friends, "Coodsz-Coop-Tools"), ToolUpdate.PickZip(exe, friendsNames));
        var adNames = repos[AD1259].Select(asset => asset.Name.Replace("{version}", "1.0.0")).ToList();
        foreach (var exe in new[] { "Coodsz AD1259 Server.exe", "Coodsz AD1259 Server Tool.exe", "Coodsz AD1259 Client Launcher.exe" })
            Assert.Equal(Name(AD1259, "Coodsz-AD1259-Tools"), ToolUpdate.PickZip(exe, adNames));
        var beNames = repos[Be].Select(asset => asset.Name.Replace("{version}", "1.0.0")).ToList();
        foreach (var exe in new[] { "Coodsz Burning Empires Server.exe", "Coodsz Burning Empires Server Tool.exe", "Coodsz Burning Empires Client.exe", "Coodsz Burning Empires Client Launcher.exe" })
            Assert.Equal(Name(Be, "Coodsz-Burning-Empires-Tools"), ToolUpdate.PickZip(exe, beNames));
        var rotNames = repos[Rot].Select(asset => asset.Name.Replace("{version}", "1.0.0")).ToList();
        foreach (var exe in new[] { "Coodsz Realm of Thrones Server Tool.exe", "Coodsz Realm of Thrones Client Launcher.exe" })
            Assert.Equal(Name(Rot, "Coodsz-Realm-of-Thrones-Tools"), ToolUpdate.PickZip(exe, rotNames));
    }

    [Fact]
    public void The_release_scripts_check_the_list_before_they_finish()
    {
        var make = SourceFiles.Read("tools/Make-Release.ps1");
        Assert.Contains("ReleaseAssets.ps1", make);
        Assert.Contains("Copy-KeptReleaseAsset", make);
        Assert.Contains("Assert-ReleaseFolder 'Coodsz/Coodsz-TAOM'", make);
        Assert.Contains("Assert-ReleaseFolder 'Coodsz/Coodsz-Coop-and-Friends'", make);
        Assert.Contains("Assert-ReleaseFolder 'Coodsz/Coodsz-AD1259'", SourceFiles.Read("tools/Make-AD1259-Release.ps1"));
        Assert.Contains("Assert-ReleaseFolder '" + Be + "'", SourceFiles.Read("tools/Make-BurningEmpires-Release.ps1"));
        // The Realm of Thrones script reads its repo from ReleaseNotice.RotRepo.
        var makeRot = SourceFiles.Read("tools/Make-RealmOfThrones-Release.ps1");
        Assert.Contains("Assert-ReleaseFolder $repo $version $Output", makeRot);
        Assert.Contains(@"Get-Content -Raw -LiteralPath 'src\BannerlordCoopLauncher.Core\Updates\ReleaseNotice.cs'", makeRot);
        Assert.Contains("'RotVersion\\s*=", makeRot);
        Assert.Contains("release-assets.json", SourceFiles.Read("tools/ReleaseAssets.ps1"));
        Assert.Contains("Get-ReleaseAssetRepos", SourceFiles.Read("tools/Check-ReleaseAssets.ps1"));
        Assert.Contains("Check-ReleaseAssets.ps1", SourceFiles.Read("README.md"));
    }

    [Fact]
    public void Every_tool_is_published_as_one_self_contained_file()
    {
        // TAOM shipped framework dependent loose files until the two exe release. Now every product ships
        // single files, so a zip holds only its exes and players need no .NET install.
        foreach (var script in new[] { "tools/Make-Release.ps1", "tools/Make-AD1259-Release.ps1", "tools/Make-BurningEmpires-Release.ps1", "tools/Make-RealmOfThrones-Release.ps1" })
        {
            var text = SourceFiles.Read(script);
            Assert.Contains("'--self-contained', 'true', '-p:PublishSingleFile=true'", text);
            Assert.DoesNotContain("'--self-contained', 'false'", text);
            var publishes = text.Split('\n').Where(line => line.Contains("Run 'dotnet' (@('publish'", StringComparison.Ordinal)).ToList();
            Assert.NotEmpty(publishes);
            Assert.All(publishes, line => Assert.EndsWith("+ $single)", line.TrimEnd()));
        }

        // The client launcher, the server tool and the two TAOM tools. Coodsz and Friends is no build of its own.
        Assert.Equal(4, Regex.Matches(SourceFiles.Read("tools/Make-Release.ps1"), @"Run 'dotnet' \(@\('publish'").Count);
    }

    [Fact]
    public void Each_TAOM_legacy_zip_carries_the_other_TAOM_tool_and_the_Coop_legacy_zip_keeps_one_tool_per_folder()
    {
        var make = SourceFiles.Read("tools/Make-Release.ps1");
        Assert.DoesNotContain("Assert-PayloadRoots $friendsZip", make);
        Assert.DoesNotContain("-Destination (Join-Path $coop 'CoodszHost.exe')", make);
        Assert.DoesNotContain("-Destination (Join-Path $and 'CoodszHost.exe')", make);
        Assert.Contains("foreach ($folder in @($coop, $and, $hostFolder)) {", make);
        Assert.Contains("Assert-PayloadRoots $zip @(@{ Exe = $legacy[1]; Root = ''; Holds = $taomLegacyExes })", make);
        Assert.Contains("foreach ($tool in $taomLegacy) { Copy-Item", make);
        Assert.Contains("function Assert-PayloadRoots", SourceFiles.Read("tools/ReleaseAssets.ps1"));
    }

    // Coodsz and Friends is the client launcher's More Nations switch now. The legacy zip still ships a
    // Coodsz and Friends.exe so old installs find their update, and it is the client build, which renames
    // itself on its first start. No separate Coodsz and Friends build is published.
    [Fact]
    public void The_Coop_legacy_zip_ships_the_client_build_as_Coodsz_and_Friends()
    {
        var make = SourceFiles.Read("tools/Make-Release.ps1");
        Assert.Contains("Copy-Item -LiteralPath \"$publish\\coop\\Coodsz Coop Client Launcher.exe\" -Destination (Join-Path $and 'Coodsz and Friends.exe')", make);
        Assert.DoesNotContain("BannerlordCoopLauncher.csproj", make);
        Assert.DoesNotContain("$publish\\and", make);
        Assert.False(File.Exists(Path.Combine(RepoRoot(), "src", "BannerlordCoopLauncher", "BannerlordCoopLauncher.csproj")));
    }

    // Each Coop legacy folder holds its own exe and no other, so each tool keeps its own folder.
    [Fact]
    public void Each_Coop_legacy_folder_lists_only_its_own_exe()
    {
        var legacy = Load()[Friends].Single(asset => asset.Legacy);
        var exes = legacy.Holds.Where(path => path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)).ToList();
        Assert.Equal(3, exes.Count);
        Assert.Equal(3, exes.Select(path => path[..path.LastIndexOf('/')]).Distinct().Count());
        Assert.Contains("Coodsz Launcher and Server/Coodsz and Friends/Coodsz and Friends.exe", exes);
    }

    // The 191be48 updater installs from the folder with the shortest path to its exe and then removes
    // every top level exe that folder lacks. Each TAOM legacy zip lists the exes that root must carry.
    [Theory]
    [InlineData(Taom, "Coodsz Dedicated TAOM Server Tool.exe", "Coodsz TAOM Client Launcher.exe")]
    [InlineData(Taom, "Coodsz TAOM Client Launcher.exe", "Coodsz Dedicated TAOM Server Tool.exe")]
    public void A_legacy_payload_root_carries_the_sibling_exes(string repo, string exe, params string[] siblings)
    {
        var names = Load()[repo].Select(asset => asset.Name.Replace("{version}", "1.3.0")).ToList();
        var picked = ShippedPickZipTests.Shipped191be48.PickZip(exe, names);
        var holds = Load()[repo].Single(asset => asset.Name.Replace("{version}", "1.3.0") == picked).Holds;
        var path = holds.Where(item => item.Split('/').Last() == exe).OrderBy(item => item.Length).First();
        var root = path[..^exe.Length];

        foreach (var sibling in siblings)
            Assert.Contains(root + sibling, holds);
    }

    // The shipped updater takes the folder with the shortest path to its exe as the payload root, so
    // each Coop exe updates from its own folder of the legacy zip, and a shorter folder carrying another
    // exe would pull that tool onto it.
    [Fact]
    public void Each_Coop_exe_takes_its_own_folder_as_the_payload_root()
    {
        var root = Path.Combine(Path.GetTempPath(), "coodsz-layout-" + Guid.NewGuid().ToString("N"));
        var top = Path.Combine(root, "Coodsz Launcher and Server");
        var layout = new Dictionary<string, string[]>
        {
            ["Coodsz Host"] = ["CoodszHost.exe"],
            ["Coodsz and Friends"] = ["Coodsz and Friends.exe"],
            ["Coodsz Coop and Friends"] = ["Coodsz Coop and Friends.exe"]
        };
        try
        {
            foreach (var (folder, exes) in layout)
            {
                Directory.CreateDirectory(Path.Combine(top, folder));
                foreach (var exe in exes)
                    File.WriteAllText(Path.Combine(top, folder, exe), "");
            }

            foreach (var (folder, exes) in layout)
                Assert.Equal(Path.Combine(top, folder), ToolUpdate.FindPayloadRoot(root, exes[0]));

            File.WriteAllText(Path.Combine(top, "Coodsz Host", "Coodsz Coop and Friends.exe"), "");
            Assert.Equal(Path.Combine(top, "Coodsz Host"), ToolUpdate.FindPayloadRoot(root, "Coodsz Coop and Friends.exe"));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "BannerlordCoopLauncher.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
