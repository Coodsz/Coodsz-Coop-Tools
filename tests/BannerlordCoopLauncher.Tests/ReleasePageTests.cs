using System.Diagnostics;
using System.Text.Json;
using BannerlordCoopLauncher.Core.Updates;

namespace BannerlordCoopLauncher.Tests;

// GitHub sorts release files by name, so on the two exe releases the TEMPORARY legacy zips showed
// above the Coodsz-<Product>-Tools zip and new players took the wrong one. Every release page now
// starts with the download line, labels each legacy zip, and shows the real icon colours in its
// download picture. These tests keep tools/release-assets.json, the release scripts,
// Set-ReleaseLabels.ps1 and Check-ReleaseAssets.ps1 doing that.
public class ReleasePageTests
{
    private const string Taom = "Coodsz/Coodsz-TAOM";
    private const string Friends = "Coodsz/Coodsz-Coop-and-Friends";
    private const string AD1259 = "Coodsz/Coodsz-AD1259";
    private const string BurningEmpires = "Coodsz/Coodsz-BurningEmpires";
    private const string SkipLabel = "Older versions update from this. You don't need it.";
    // The TAOM tools carry the CompatOnly zips since v1.2.1; the zips stay only for older tools, which find them by name.
    private const string CompatLabel = "Older versions use this. You don't need it.";

    private static IEnumerable<JsonElement> Repos()
    {
        using var json = JsonDocument.Parse(SourceFiles.Read("tools/release-assets.json"));
        return json.RootElement.GetProperty("repos").EnumerateArray().Select(repo => repo.Clone()).ToList();
    }

    [Fact]
    public void Every_legacy_zip_has_the_skip_label_the_CompatOnly_zips_theirs_and_no_other_asset_has_one()
    {
        var assets = Repos().SelectMany(repo => repo.GetProperty("assets").EnumerateArray()).ToList();
        Assert.Equal(5, assets.Count(asset => asset.TryGetProperty("legacy", out var legacy) && legacy.GetBoolean()));
        foreach (var asset in assets)
        {
            var legacy = asset.TryGetProperty("legacy", out var flag) && flag.GetBoolean();
            if (legacy)
                Assert.Equal(SkipLabel, asset.GetProperty("label").GetString());
            else if (asset.GetProperty("name").GetString()!.Contains("-CompatOnly-", StringComparison.Ordinal))
                Assert.Equal(CompatLabel, asset.GetProperty("label").GetString());
            else
                Assert.False(asset.TryGetProperty("label", out _), asset.GetProperty("name").GetString() + " is a download players need, so it gets no skip label.");
        }
    }

    [Theory]
    [InlineData(Friends, "Coop", "Coodsz-Coop-Tools-v{version}.zip", "src/CoodszHost/CoodszHost.csproj", "src/CoodszCoopAndFriends/CoodszCoopAndFriends.csproj")]
    [InlineData(Taom, "TAOM", "Coodsz-TAOM-Tools-v{version}.zip", "src/TaomCoopHost/TaomCoopHost.csproj", "src/TaomCoopLauncher/TaomCoopLauncher.csproj")]
    [InlineData(AD1259, "AD1259", "Coodsz-AD1259-Tools-v{version}.zip", "src/AD1259Host/AD1259Host.csproj", "src/AD1259Launcher/AD1259Launcher.csproj")]
    [InlineData(BurningEmpires, "BurningEmpires", "Coodsz-Burning-Empires-Tools-v{version}.zip", "src/BurningEmpiresHost/BurningEmpiresHost.csproj", "src/BurningEmpiresLauncher/BurningEmpiresLauncher.csproj")]
    [InlineData(RealmOfThrones.Core.RotRelease.Repo, "RealmOfThrones", "Coodsz-Realm-of-Thrones-Tools-v{version}.zip", "src/RealmOfThronesHost/RealmOfThronesHost.csproj", "src/RealmOfThronesLauncher/RealmOfThronesLauncher.csproj")]
    public void Every_product_has_a_download_line_naming_its_Tools_zip(string name, string product, string zip, string server, string client)
    {
        var repo = Repos().Single(item => item.GetProperty("repo").GetString() == name);
        var tools = repo.GetProperty("assets").EnumerateArray().Single(asset => asset.TryGetProperty("exactly", out _)).GetProperty("name").GetString()!;
        var line = repo.GetProperty("downloadLine").GetString()!;

        Assert.Equal(zip, tools);
        Assert.StartsWith("**Download " + tools + ".** ", line);
        // Only a repo with legacy zips tells players what they are for. Burning Empires has none.
        if (repo.GetProperty("assets").EnumerateArray().Any(asset => asset.TryGetProperty("legacy", out var legacy) && legacy.GetBoolean()))
            Assert.Contains("only used by older versions to update themselves", line);
        else
            Assert.DoesNotContain("older versions", line);
        Assert.DoesNotContain("\n", line);
        Assert.Equal(product, repo.GetProperty("product").GetString());
        Assert.Equal(server, repo.GetProperty("serverProject").GetString());
        Assert.Equal(client, repo.GetProperty("clientProject").GetString());
        if (name == Taom)
        {
            Assert.Contains("CompatOnly CLIENT zip", line);
            Assert.Contains("HOST-OPERATOR zip", line);
        }
    }

    [Fact]
    public void The_release_scripts_write_the_body_and_the_labels_and_print_the_publish_commands()
    {
        var shared = SourceFiles.Read("tools/ReleaseAssets.ps1");
        Assert.Contains("(Join-Path $Folder 'release-body.md')", shared);
        Assert.Contains("(Join-Path $Folder 'release-labels.json')", shared);
        Assert.Contains("--notes-file", shared);
        Assert.Contains(@".\tools\Set-ReleaseLabels.ps1 -Repo $Repo -Tag v$version", shared);

        var make = SourceFiles.Read("tools/Make-Release.ps1");
        Assert.Contains("Write-ReleasePageFiles 'Coodsz/Coodsz-Coop-and-Friends' $version $Output", make);
        Assert.Contains("Write-ReleasePageFiles 'Coodsz/Coodsz-TAOM' $taomVersion $taomOutput", make);
        Assert.Contains("Write-PublishCommands 'Coodsz/Coodsz-Coop-and-Friends' $version $Output", make);
        Assert.Contains("Write-PublishCommands 'Coodsz/Coodsz-TAOM' $taomVersion $taomOutput", make);
        var makeAd = SourceFiles.Read("tools/Make-AD1259-Release.ps1");
        Assert.Contains("Write-ReleasePageFiles 'Coodsz/Coodsz-AD1259' $version $Output", makeAd);
        Assert.Contains("Write-PublishCommands 'Coodsz/Coodsz-AD1259' $version $Output", makeAd);
        var makeBe = SourceFiles.Read("tools/Make-BurningEmpires-Release.ps1");
        Assert.Contains("Write-ReleasePageFiles 'Coodsz/Coodsz-BurningEmpires' $version $Output", makeBe);
        Assert.Contains("Write-PublishCommands 'Coodsz/Coodsz-BurningEmpires' $version $Output", makeBe);
        // The files are written after Assert-ReleaseFolder, which would refuse them as unknown otherwise.
        Assert.True(make.IndexOf("Write-ReleasePageFiles", StringComparison.Ordinal) > make.LastIndexOf("Assert-ReleaseFolder", StringComparison.Ordinal));
        Assert.True(makeAd.IndexOf("Write-ReleasePageFiles", StringComparison.Ordinal) > makeAd.LastIndexOf("Assert-ReleaseFolder", StringComparison.Ordinal));
        Assert.True(makeBe.IndexOf("Write-ReleasePageFiles", StringComparison.Ordinal) > makeBe.LastIndexOf("Assert-ReleaseFolder", StringComparison.Ordinal));
        var makeRot = SourceFiles.Read("tools/Make-RealmOfThrones-Release.ps1");
        Assert.Contains("Write-ReleasePageFiles $repo $version $Output -Unsigned:$SkipSigning", makeRot);
        Assert.Contains("Write-PublishCommands $repo $version $Output", makeRot);
        Assert.True(makeRot.IndexOf("Write-ReleasePageFiles", StringComparison.Ordinal) > makeRot.LastIndexOf("Assert-ReleaseFolder", StringComparison.Ordinal));
        Assert.Contains("Set-ReleaseLabels.ps1", SourceFiles.Read("README.md"));
        Assert.Contains("release-body.md", SourceFiles.Read("README.md"));
    }

    [Fact]
    public void Set_ReleaseLabels_only_ever_sends_a_label()
    {
        var script = SourceFiles.Read("tools/Set-ReleaseLabels.ps1");
        Assert.DoesNotContain("name=", script);
        Assert.DoesNotContain("-F ", script);
        Assert.Contains("-f \"label=$($asset.Label)\"", script);
        Assert.Contains("SupportsShouldProcess = $true", script);
    }

    // Runs Set-ReleaseLabels.ps1 against a fake gh that records every call: one PATCH with only the
    // label for the unlabelled legacy zip, nothing for the one already labelled, and nothing at all with -WhatIf.
    [Fact]
    public void Set_ReleaseLabels_labels_only_the_unlabelled_legacy_zips_and_WhatIf_sends_nothing()
    {
        var release = "{\"assets\":[" +
                      "{\"id\":11,\"name\":\"Coodsz-AD1259-Server-v1.1.0.zip\",\"label\":\"\"}," +
                      "{\"id\":12,\"name\":\"Coodsz-AD1259-Client-Launcher-v1.1.0.zip\",\"label\":\"" + SkipLabel.Replace("'", "''") + "\"}," +
                      "{\"id\":13,\"name\":\"Coodsz-AD1259-Tools-v1.1.0.zip\",\"label\":\"\"}]}";
        var (calls, _) = RunWithFakeGh("& $tools\\Set-ReleaseLabels.ps1 -Repo Coodsz/Coodsz-AD1259 -Tag v1.1.0", release);
        var patches = calls.Where(call => call.Contains("PATCH")).ToList();
        Assert.Equal(["api|-X|PATCH|repos/Coodsz/Coodsz-AD1259/releases/assets/11|-f|label=" + SkipLabel], patches);
        Assert.DoesNotContain(calls, call => call.Contains("name="));

        var (whatIf, output) = RunWithFakeGh("& $tools\\Set-ReleaseLabels.ps1 -Repo Coodsz/Coodsz-AD1259 -Tag v1.1.0 -WhatIf", release);
        Assert.Contains("api|repos/Coodsz/Coodsz-AD1259/releases/tags/v1.1.0", whatIf);
        Assert.DoesNotContain(whatIf, call => call.Contains("PATCH"));
        Assert.Contains("  WOULD SET Coodsz-AD1259-Server-v1.1.0.zip (asset 11) to \"" + SkipLabel + "\", now \"\"", output);
        Assert.DoesNotContain(output, line => line.Contains("Client-Launcher") && line.Contains("WOULD SET"));
        Assert.Equal("exit=0", output.Last());
    }

    // Check-ReleaseAssets.ps1 against a fake gh: a legacy zip with no label and a body that does not
    // start with the download line each give a WRONG line and exit 1; the fixed page passes.
    [Fact]
    public void Check_ReleaseAssets_fails_on_a_missing_label_or_a_body_without_the_download_line()
    {
        string Release(string serverLabel, string body) => "{\"tagName\":\"v1.1.0\",\"body\":\"" + body + "\",\"assets\":[" +
            "{\"name\":\"Coodsz-AD1259-Server-v1.1.0.zip\",\"label\":\"" + serverLabel + "\",\"digest\":\"\"}," +
            "{\"name\":\"Coodsz-AD1259-Client-Launcher-v1.1.0.zip\",\"label\":\"" + SkipLabel.Replace("'", "''") + "\",\"digest\":\"\"}," +
            "{\"name\":\"Coodsz-AD1259-Tools-v1.1.0.zip\",\"label\":\"\",\"digest\":\"\"}," +
            "{\"name\":\"SHA256SUMS.txt\",\"label\":\"\",\"digest\":\"\"}]}";
        // -SkipZipContents runs against the real release-assets.json, and Check refuses it for a release whose
        // signatures must be checked. v1.1.0 is older than any AD1259 release that can be signed, so it stays
        // unchecked; this guard says why if signedFromVersion is ever set at or below it.
        using (var assets = JsonDocument.Parse(SourceFiles.Read("tools/release-assets.json")))
        {
            var from = assets.RootElement.GetProperty("repos").EnumerateArray()
                .Single(repo => repo.GetProperty("repo").GetString() == "Coodsz/Coodsz-AD1259")
                .GetProperty("signedFromVersion").GetString() ?? "";
            Assert.True(from.Length == 0 || Version.Parse(from.TrimStart('v', 'V')) > new Version(1, 1, 0),
                "AD1259 signedFromVersion " + from + " makes v1.1.0 need signatures, so -SkipZipContents is refused. Use an older tag here.");
        }

        const string command = "& $tools\\Check-ReleaseAssets.ps1 -Repo Coodsz/Coodsz-AD1259 -Tag v1.1.0 -SkipZipContents";
        var line = "**Download Coodsz-AD1259-Tools-v1.1.0.zip.** The other two zips are only used by older versions to update themselves.";

        var (_, bad) = RunWithFakeGh(command, Release("", "### What is new in v1.1.0\\n\\nText"));
        Assert.Contains(bad, item => item.StartsWith("  WRONG    Coodsz-AD1259-Server-v1.1.0.zip  label \"\"", StringComparison.Ordinal));
        Assert.Contains(bad, item => item.StartsWith("  WRONG    body: the release body starts with \"### What is new in v1.1.0\"", StringComparison.Ordinal));
        Assert.Contains("  WRONG    body has no download picture", bad);
        Assert.Equal("exit=1", bad.Last());

        var picture = "![Download Coodsz-AD1259-Tools](https://raw.githubusercontent.com/Coodsz/Coodsz-AD1259/main/images/Coodsz-AD1259-new-download.png)";
        var (_, good) = RunWithFakeGh(command, Release(SkipLabel.Replace("'", "''"), "\\n" + line.Replace("'", "''") + "\\n\\n" + picture + "\\n\\n### What is new in v1.1.0"));
        Assert.DoesNotContain(good, item => item.Contains("WRONG"));
        Assert.Contains("  OK       Coodsz-AD1259-new-download.png shows both icon colours", good);
        Assert.Equal("exit=0", good.Last());
    }

    // Runs the shared functions on the real notes, icons and download pictures.
    [Fact]
    public void The_body_starts_with_the_download_line_and_the_pictures_show_both_icon_colours()
    {
        var root = SourceFiles.Root();
        var scratch = Path.Combine(Path.GetTempPath(), "coodsz-release-page-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        try
        {
            var run = ". '" + Path.Combine(root, "tools", "ReleaseAssets.ps1") + "'\r\n" +
                      "$lines = @()\r\n" +
                      "foreach ($pair in @(@('" + Friends + "', '" + ReleaseNotice.FriendsVersion + "', 'Coop'), @('" + Taom + "', '" + ReleaseNotice.TaomVersion + "', 'TAOM'), @('" + AD1259 + "', '" + ReleaseNotice.AD1259Version + "', 'AD1259'), @('" + BurningEmpires + "', '" + ReleaseNotice.BeVersion + "', 'BurningEmpires'), @('" + RealmOfThrones.Core.RotRelease.Repo + "', '" + ReleaseNotice.RotVersion + "', 'RealmOfThrones'))) {\r\n" +
                      "  $info = Get-ReleaseRepoInfo $pair[0] $pair[1]\r\n" +
                      "  $body = Get-ReleaseBody $pair[0] $pair[1]\r\n" +
                      "  $lines += $pair[2] + ' first=' + ($body.Split(\"`n\")[0] -eq $info.DownloadLine) + ' comment=' + $body.Contains('<!--') + ' start=' + @(Test-ReleaseBodyStart $body $pair[0] $pair[1]).Count + ' notes=' + @(Test-ReleaseBodyStart (Get-Content -Raw (Get-ReleaseNotesFile $pair[0] $pair[1])) $pair[0] $pair[1]).Count\r\n" +
                      "  $lines += $pair[2] + ' urls=' + @(Get-DownloadPictureUrls $body $pair[0]).Count + ' colours=' + (Get-BannerColour $info.ServerProject) + ',' + (Get-BannerColour $info.ClientProject)\r\n" +
                      "  $lines += $pair[2] + ' picture=' + (@(Test-DownloadPicture ('" + Path.Combine(root, "docs", "images") + "\\Coodsz-' + $pair[2] + '-new-download.png') $pair[0]) -join '; ')\r\n" +
                      "  Write-ReleasePageFiles $pair[0] $pair[1] '" + scratch + "' 6>$null\r\n" +
                      "  $lines += $pair[2] + ' labels=' + (((Get-Content -Raw '" + scratch + "\\release-labels.json' | ConvertFrom-Json).PSObject.Properties | ForEach-Object { $_.Name + '=' + $_.Value }) -join '|')\r\n" +
                      "  $lines += $pair[2] + ' bodyfile=' + ([System.IO.File]::ReadAllText('" + scratch + "\\release-body.md') -eq $body)\r\n" +
                      "}\r\n" +
                      // A picture with only the Server Tool colour fails for the Client Launcher.
                      "Add-Type -AssemblyName System.Drawing\r\n" +
                      "$bitmap = New-Object System.Drawing.Bitmap 40, 40\r\n" +
                      "$g = [System.Drawing.Graphics]::FromImage($bitmap); $g.Clear([System.Drawing.ColorTranslator]::FromHtml('#791F1F')); $g.Dispose()\r\n" +
                      "$bitmap.Save('" + scratch + "\\wrong.png', [System.Drawing.Imaging.ImageFormat]::Png); $bitmap.Dispose()\r\n" +
                      "$lines += 'wrong=' + (@(Test-DownloadPicture '" + scratch + "\\wrong.png' '" + Friends + "') -join '; ')\r\n" +
                      // A smoothed or JPG copy whose colours are a few steps off still passes.
                      "$bitmap = New-Object System.Drawing.Bitmap 40, 40\r\n" +
                      "$g = [System.Drawing.Graphics]::FromImage($bitmap); $g.Clear([System.Drawing.ColorTranslator]::FromHtml('#7C2222'))\r\n" +
                      "$g.FillRectangle((New-Object System.Drawing.SolidBrush ([System.Drawing.ColorTranslator]::FromHtml('#0F477F'))), 0, 0, 20, 40); $g.Dispose()\r\n" +
                      "$bitmap.Save('" + scratch + "\\near.png', [System.Drawing.Imaging.ImageFormat]::Png); $bitmap.Dispose()\r\n" +
                      "$lines += 'near=' + (@(Test-DownloadPicture '" + scratch + "\\near.png' '" + Friends + "') -join '; ')\r\n" +
                      "[System.IO.File]::WriteAllLines('" + scratch + "\\out.txt', [string[]]$lines)\r\n";
            var lines = RunPowerShell(scratch, run);

            Assert.Contains("Coop first=True comment=False start=0 notes=1", lines);
            Assert.Contains("TAOM first=True comment=False start=0 notes=1", lines);
            Assert.Contains("AD1259 first=True comment=False start=0 notes=1", lines);
            Assert.Contains("BurningEmpires first=True comment=False start=0 notes=1", lines);
            Assert.Contains("Coop urls=1 colours=#791F1F,#0C447C", lines);
            Assert.Contains("TAOM urls=1 colours=#085041,#0F6E56", lines);
            Assert.Contains("AD1259 urls=1 colours=#3C3489,#534AB7", lines);
            Assert.Contains("BurningEmpires urls=1 colours=#D35400,#E67E22", lines);
            Assert.Contains("Coop picture=", lines);
            Assert.Contains("TAOM picture=", lines);
            Assert.Contains("AD1259 picture=", lines);
            Assert.Contains("BurningEmpires picture=", lines);
            // The Realm of Thrones notes link the picture before the release repo exists, so only its colours are checked here.
            Assert.Contains("RealmOfThrones urls=1 colours=#141416,#2E1D1F", lines);
            Assert.Contains("RealmOfThrones picture=", lines);
            Assert.Contains("Coop labels=Coodsz-Coop-and-Friends-" + ReleaseNotice.FriendsVersion + ".zip=" + SkipLabel, lines);
            Assert.Contains("AD1259 labels=Coodsz-AD1259-Client-Launcher-" + ReleaseNotice.AD1259Version + ".zip=" + SkipLabel + "|Coodsz-AD1259-Server-" + ReleaseNotice.AD1259Version + ".zip=" + SkipLabel, lines);
            Assert.Contains("BurningEmpires labels=", lines);
            // The Coop-RoT zips ride only inside the tools, so the Realm of Thrones release has no skip labels.
            Assert.Contains("RealmOfThrones labels=", lines);
            Assert.All(new[] { "Coop", "TAOM", "AD1259", "BurningEmpires" }, product => Assert.Contains(product + " bodyfile=True", lines));
            Assert.Contains("wrong=the download picture shows the Client Launcher icon colour #0C447C on 0 pixels, it needs at least 50", lines);
            Assert.Contains("near=", lines);
        }
        finally
        {
            Directory.Delete(scratch, true);
        }
    }

    // Antivirus tools flag new unsigned exes. Each release folder gets the list Coodsz sends to the vendors.
    [Fact]
    public void Each_release_folder_gets_the_false_positive_checklist()
    {
        var root = SourceFiles.Root();
        var scratch = Path.Combine(Path.GetTempPath(), "coodsz-fp-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        try
        {
            var tools = Path.Combine(scratch, "Coodsz-TAOM-Tools-v1.2.2.zip");
            using (var zip = System.IO.Compression.ZipFile.Open(tools, System.IO.Compression.ZipArchiveMode.Create))
            {
                foreach (var (name, body) in new[] { ("Coodsz-TAOM/Coodsz TAOM Server Tool.exe", "server"), ("Coodsz-TAOM/Coodsz TAOM Client Launcher.exe", "client"), ("Coodsz-TAOM/LICENSE.txt", "license") })
                {
                    using var writer = new StreamWriter(zip.CreateEntry(name).Open());
                    writer.Write(body);
                }
            }
            File.WriteAllText(Path.Combine(scratch, "LICENSE.txt"), "license");
            var run = ". '" + Path.Combine(root, "tools", "ReleaseAssets.ps1") + "'\r\n" +
                      "Write-FalsePositiveList '" + Taom + "' 'v1.2.2' '" + scratch + "'\r\n" +
                      "[System.IO.File]::WriteAllLines('" + Path.Combine(scratch, "out.txt") + "', [string[]](Get-Content -LiteralPath '" + Path.Combine(scratch, "false-positive-submission.md") + "'))\r\n";
            var lines = RunPowerShell(scratch, run);
            var text = string.Join("\n", lines);
            string Sha(byte[] bytes) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();
            var zipHash = Sha(File.ReadAllBytes(tools));
            var serverHash = Sha(System.Text.Encoding.UTF8.GetBytes("server"));

            Assert.Contains("| Coodsz-TAOM-Tools-v1.2.2.zip | " + zipHash + " | https://www.virustotal.com/gui/file/" + zipHash + " |", lines);
            Assert.Contains("| Coodsz TAOM Server Tool.exe (in Coodsz-TAOM-Tools-v1.2.2.zip) | " + serverHash + " | https://www.virustotal.com/gui/file/" + serverHash + " |", lines);
            Assert.Contains(lines, line => line.StartsWith("| Coodsz TAOM Client Launcher.exe (in Coodsz-TAOM-Tools-v1.2.2.zip) | ", StringComparison.Ordinal));
            Assert.DoesNotContain("LICENSE.txt", text);
            Assert.Contains("only shows a result after the zip was uploaded there", text);
            Assert.Contains("https://www.microsoft.com/en-us/wdsi/filesubmission", text);
            Assert.Contains("https://submit.norton.com/", text);
            Assert.Contains("https://www.avast.com/false-positive-file-form.php", text);
            Assert.Contains("https://github.com/" + Taom + "/releases/tag/v1.2.2", text);
            Assert.True(text.All(ch => ch < 128), "false-positive-submission.md stays ASCII");
        }
        finally
        {
            Directory.Delete(scratch, true);
        }
    }

    // Every release script writes it through Write-ReleasePageFiles.
    [Fact]
    public void The_release_page_files_include_the_false_positive_checklist()
    {
        var shared = SourceFiles.Read(Path.Combine("tools", "ReleaseAssets.ps1"));
        var pageFiles = SourceFiles.Method(shared, "function Write-ReleasePageFiles");
        Assert.Contains("Write-FalsePositiveList $Repo $Version $Folder", pageFiles);
        Assert.Contains("false-positive-submission.md", SourceFiles.Method(shared, "function Write-PublishCommands"));
        Assert.True(shared.All(ch => ch < 128), "ReleaseAssets.ps1 stays ASCII");
        foreach (var script in new[] { "Make-Release.ps1", "Make-AD1259-Release.ps1", "Make-BurningEmpires-Release.ps1" })
            Assert.Contains("Write-ReleasePageFiles '", SourceFiles.Read(Path.Combine("tools", script)));
        Assert.Contains("Write-ReleasePageFiles $repo ", SourceFiles.Read(Path.Combine("tools", "Make-RealmOfThrones-Release.ps1")));
    }

    // A release build must stop while another chat holds the live test lock.
    [Fact]
    public void The_release_scripts_stop_while_the_live_test_lock_is_held()
    {
        var root = SourceFiles.Root();
        var scratch = Path.Combine(Path.GetTempPath(), "coodsz-lock-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        try
        {
            var lockFile = Path.Combine(scratch, "live-test.lock");
            var run = ". '" + Path.Combine(root, "tools", "ReleaseAssets.ps1") + "'\r\n" +
                      "$env:COODSZ_LIVE_TEST_LOCK = '" + lockFile + "'\r\n" +
                      "$lines = @()\r\n" +
                      "function Try-Lock { try { Assert-LiveTestLockFree @args; 'ok' } catch { $_.Exception.Message } }\r\n" +
                      "$lines += 'absent=' + (Try-Lock)\r\n" +
                      "[System.IO.File]::WriteAllText('" + lockFile + "', \"King chat 12:00`nsecond line\")\r\n" +
                      "$lines += 'held=' + (Try-Lock)\r\n" +
                      "$lines += 'ignored=' + (Try-Lock -IgnoreLock)\r\n" +
                      "[System.IO.File]::WriteAllLines('" + Path.Combine(scratch, "out.txt") + "', [string[]]$lines)\r\n";
            var lines = RunPowerShell(scratch, run);

            Assert.Contains("absent=ok", lines);
            Assert.Contains("held=The live test lock is held by King chat 12:00. Wait until it is free, or pass -IgnoreLock if that chat agreed.", lines);
            Assert.Contains("ignored=ok", lines);
        }
        finally
        {
            Directory.Delete(scratch, true);
        }
    }

    // Each release script asks before its first build or test step.
    [Theory]
    [InlineData("Make-Release.ps1")]
    [InlineData("Make-AD1259-Release.ps1")]
    [InlineData("Make-BurningEmpires-Release.ps1")]
    [InlineData("Make-RealmOfThrones-Release.ps1")]
    public void Each_release_script_checks_the_lock_before_its_first_dotnet_call(string script)
    {
        var text = File.ReadAllText(Path.Combine(SourceFiles.Root(), "tools", script));
        var check = text.IndexOf("Assert-LiveTestLockFree -IgnoreLock:$IgnoreLock", StringComparison.Ordinal);
        // The scripts start every build and test through Run 'dotnet'; comments may name dotnet earlier.
        var firstDotnet = text.IndexOf("Run 'dotnet'", StringComparison.Ordinal);
        Assert.True(firstDotnet > 0, script + " has no Run 'dotnet' call to check against.");
        Assert.True(check > 0, script + " does not check the live test lock.");
        Assert.Contains("[switch] $IgnoreLock", text);
        Assert.True(firstDotnet < 0 || check < firstDotnet, script + " builds before it checks the lock.");
    }

    // A release refuses to build without the private files (CONTRIBUTING.md, Private files), or with a server DLL
    // that is not the recorded build.
    [Fact]
    public void The_release_scripts_refuse_without_the_private_files()
    {
        var scratch = Path.Combine(Path.GetTempPath(), "coodsz-private-" + Guid.NewGuid().ToString("N"));
        var bootstrap = Path.Combine(scratch, "DedicatedServerRepair", "BannerlordCoop.RuntimeBootstrap.dll");
        Directory.CreateDirectory(Path.GetDirectoryName(bootstrap)!);
        try
        {
            var key = Path.Combine(scratch, "realm-reward-key.txt");
            var run = ". '" + Path.Combine(SourceFiles.Root(), "tools", "ReleaseAssets.ps1") + "'\r\n" +
                      "$env:CoodszPrivateRoot = '" + scratch + "'\r\n" +
                      "$lines = @()\r\n" +
                      "function Try-Private { try { 'ok ' + (Assert-PrivateFiles @args) } catch { $_.Exception.Message } }\r\n" +
                      "$lines += 'nokey=' + (Try-Private)\r\n" +
                      "[System.IO.File]::WriteAllText('" + key + "', 'not hex')\r\n" +
                      "$lines += 'badkey=' + (Try-Private)\r\n" +
                      "[System.IO.File]::WriteAllText('" + key + "', '" + new string('a', 64) + "' + [Environment]::NewLine)\r\n" +
                      "$lines += 'key=' + (Try-Private)\r\n" +
                      "$lines += 'nodlls=' + (Try-Private -RepairDlls)\r\n" +
                      "[System.IO.File]::WriteAllText('" + bootstrap + "', 'stand in')\r\n" +
                      "$lines += 'wrongdll=' + (Try-Private -RepairDlls)\r\n" +
                      "[System.IO.File]::WriteAllLines('" + Path.Combine(scratch, "out.txt") + "', [string[]]$lines)\r\n";
            var lines = RunPowerShell(scratch, run);

            const string help = " A release build needs the private files (CONTRIBUTING.md, Private files).";
            Assert.Contains("nokey=The Realm reward key is missing: " + key + "." + help, lines);
            Assert.Contains("badkey=" + key + " must hold the key as 64 hex digits." + help, lines);
            Assert.Contains("key=ok " + scratch, lines);
            Assert.Contains("nodlls=A prebuilt server DLL is missing: " + bootstrap + "." + help, lines);
            Assert.Contains(lines, line => line.StartsWith("wrongdll=" + bootstrap + " is not the recorded build (SHA-256 ", StringComparison.Ordinal)
                && line.EndsWith(", expected cff7a2456f4fcd6fdc6f6b430fbb315cad69fd425059882a875508e4f793441d).", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(scratch, true);
        }
    }

    // Every tool signs Realm reward codes, so every release script needs the key; the three whose server tool
    // carries DedicatedServerRepair need its DLLs too. Both before the first build.
    [Theory]
    [InlineData("Make-Release.ps1", true)]
    [InlineData("Make-AD1259-Release.ps1", true)]
    [InlineData("Make-BurningEmpires-Release.ps1", true)]
    [InlineData("Make-RealmOfThrones-Release.ps1", false)]
    public void Each_release_script_checks_the_private_files_before_its_first_dotnet_call(string script, bool repairDlls)
    {
        var text = File.ReadAllText(Path.Combine(SourceFiles.Root(), "tools", script));
        var check = text.IndexOf(repairDlls ? "$privateRoot = Assert-PrivateFiles -RepairDlls" : "Assert-PrivateFiles | Out-Null", StringComparison.Ordinal);
        Assert.True(check > 0, script + " does not check the private files.");
        Assert.True(check < text.IndexOf("Run 'dotnet'", StringComparison.Ordinal), script + " builds before it checks the private files.");
        // The private DLLs are part of the payload the script checks file for file.
        Assert.Equal(repairDlls, text.Contains("(Join-Path $privateRoot 'DedicatedServerRepair')", StringComparison.Ordinal));
    }

    // Runs $command with a gh function that logs each call (arguments joined by |) and answers every
    // read with $release, and an Invoke-WebRequest that hands back the real AD1259 download picture
    // instead of going online. Returns the calls and the script's output lines, then exit=<code>.
    private static (List<string> Calls, List<string> Output) RunWithFakeGh(string command, string release)
    {
        var scratch = Path.Combine(Path.GetTempPath(), "coodsz-fake-gh-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        try
        {
            var calls = Path.Combine(scratch, "calls.txt");
            var run = "$tools = '" + Path.Combine(SourceFiles.Root(), "tools") + "'\r\n" +
                      "function gh { [System.IO.File]::AppendAllText('" + calls + "', ($args -join '|') + \"`n\"); $global:LASTEXITCODE = 0; if ($args -notcontains 'PATCH') { '" + release + "' } }\r\n" +
                      "function Invoke-WebRequest([switch] $UseBasicParsing, [string] $Uri, [string] $OutFile) { Copy-Item -LiteralPath '" + Path.Combine(SourceFiles.Root(), "docs", "images", "Coodsz-AD1259-new-download.png") + "' -Destination $OutFile }\r\n" +
                      "$output = @(" + command + " *>&1 | ForEach-Object { [string] $_ })\r\n" +
                      "$output += 'exit=' + $LASTEXITCODE\r\n" +
                      "[System.IO.File]::WriteAllLines('" + Path.Combine(scratch, "out.txt") + "', [string[]]$output)\r\n";
            var output = RunPowerShell(scratch, run);
            var logged = File.Exists(calls) ? File.ReadAllLines(calls).Where(line => line.Length > 0).ToList() : [];
            return (logged, output);
        }
        finally
        {
            Directory.Delete(scratch, true);
        }
    }

    private static List<string> RunPowerShell(string folder, string script)
    {
        File.WriteAllText(Path.Combine(folder, "run.ps1"), script);
        var start = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = "-NoProfile -ExecutionPolicy Bypass -File \"" + Path.Combine(folder, "run.ps1") + "\"",
            UseShellExecute = false,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        // CI runs the tests from PowerShell 7, whose module path makes Windows PowerShell load the
        // PowerShell 7 copy of Get-FileHash and fail. Without it Windows PowerShell uses its own.
        start.Environment.Remove("PSModulePath");
        using var process = Process.Start(start);
        Assert.NotNull(process);
        var errors = process!.StandardError.ReadToEnd();
        Assert.True(process.WaitForExit(120000), "The release page script did not finish.");
        var output = Path.Combine(folder, "out.txt");
        Assert.True(File.Exists(output), "The release page script wrote nothing: " + errors);
        return File.ReadAllLines(output).ToList();
    }
}
