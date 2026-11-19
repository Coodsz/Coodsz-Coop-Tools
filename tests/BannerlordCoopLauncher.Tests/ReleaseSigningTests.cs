using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BannerlordCoopLauncher.Tests;

// The release scripts sign every shipped exe with Microsoft Artifact Signing before any zip is made, and
// Check-ReleaseAssets.ps1 checks the signatures of a published release. Nothing here signs anything or needs
// an Azure login: the tests read the scripts and the json, and run the pure helper functions.
public class ReleaseSigningTests
{
    private static readonly string[] MakeScripts =
    [
        "tools/Make-Release.ps1",
        "tools/Make-AD1259-Release.ps1",
        "tools/Make-BurningEmpires-Release.ps1",
        "tools/Make-RealmOfThrones-Release.ps1"
    ];

    private static JsonElement Signing()
    {
        using var json = JsonDocument.Parse(SourceFiles.Read("tools/release-assets.json"));
        return json.RootElement.GetProperty("signing").Clone();
    }

    [Fact]
    public void The_signing_section_names_the_account_and_pins_both_packages_with_a_hash()
    {
        var signing = Signing();
        Assert.Equal("https://eus.codesigning.azure.net/", signing.GetProperty("endpoint").GetString());
        Assert.Equal("coodsz-signing", signing.GetProperty("account").GetString());
        Assert.Equal("coodsz-public", signing.GetProperty("profile").GetString());
        Assert.Equal("CN=Youssef Osseili", signing.GetProperty("signer").GetString());
        Assert.Equal("http://timestamp.acs.microsoft.com", signing.GetProperty("timestampUrl").GetString());

        var packages = signing.GetProperty("packages").EnumerateArray().ToList();
        Assert.Equal(["Microsoft.Windows.SDK.BuildTools", "Microsoft.ArtifactSigning.Client"], packages.Select(p => p.GetProperty("id").GetString()).ToArray());
        foreach (var package in packages)
        {
            // An exact version (no range, no wildcard) and a SHA512 as nuget.org shows it (88 base64 characters).
            Assert.Matches(@"^\d+(\.\d+)+$", package.GetProperty("version").GetString()!);
            Assert.Equal(88, package.GetProperty("sha512").GetString()!.Length);
        }
        Assert.EndsWith("signtool.exe", packages[0].GetProperty("signtool").GetString());
        Assert.EndsWith("Azure.CodeSigning.Dlib.dll", packages[1].GetProperty("dlib").GetString());
    }

    [Fact]
    public void The_metadata_has_only_names_and_credential_exclusions_and_never_leaves_out_AzureCliCredential()
    {
        var output = RunPowerShell(
            "$config = (Get-Content -Raw '" + Path.Combine(SourceFiles.Root(), "tools", "release-assets.json") + "' | ConvertFrom-Json).signing\r\n" +
            "Get-SigningMetadataJson $config\r\n");
        using var metadata = JsonDocument.Parse(string.Join("\n", output));
        var names = metadata.RootElement.EnumerateObject().Select(property => property.Name).ToList();
        Assert.Equal(["Endpoint", "CodeSigningAccountName", "CertificateProfileName", "ExcludeCredentials"], names);
        Assert.Equal("coodsz-signing", metadata.RootElement.GetProperty("CodeSigningAccountName").GetString());
        Assert.Equal("coodsz-public", metadata.RootElement.GetProperty("CertificateProfileName").GetString());
        var excluded = metadata.RootElement.GetProperty("ExcludeCredentials").EnumerateArray().Select(item => item.GetString()).ToList();
        Assert.DoesNotContain("AzureCliCredential", excluded);
        Assert.Equal(9, excluded.Count);
        // No secret field of any kind.
        foreach (var name in names)
            Assert.DoesNotMatch("(?i)secret|password|token|key|tenant|client", name);
        // The script never writes a secret either.
        var shared = SourceFiles.Read("tools/ReleaseAssets.ps1");
        Assert.DoesNotContain("ClientSecret", shared);
        Assert.DoesNotContain("AZURE_CLIENT_SECRET", shared);
    }

    [Fact]
    public void Only_AzureCliCredential_is_left_for_the_dlib_to_try()
    {
        // Every credential type the 1.0.146 dlib can exclude (its Exclude... options), except the Azure CLI one.
        // EnvironmentCredential matters most: stray AZURE_CLIENT_ID, AZURE_CLIENT_SECRET and AZURE_TENANT_ID
        // variables would otherwise sign as another identity. The dlib has no BrokerCredential option.
        string[] everyOption =
        [
            "EnvironmentCredential", "ManagedIdentityCredential", "WorkloadIdentityCredential", "SharedTokenCacheCredential",
            "VisualStudioCredential", "VisualStudioCodeCredential", "AzurePowerShellCredential", "AzureDeveloperCliCredential", "InteractiveBrowserCredential"
        ];
        var excluded = Signing().GetProperty("excludeCredentials").EnumerateArray().Select(item => item.GetString()!).ToArray();
        Assert.Equal(everyOption.OrderBy(name => name), excluded.OrderBy(name => name));
        Assert.DoesNotContain("AzureCliCredential", excluded);
    }

    [Fact]
    public void The_signer_must_match_the_name_exactly_and_the_issuer_must_be_the_Microsoft_ID_Verified_CA()
    {
        var output = RunPowerShell(
            "function Sig($subject, $issuer) { [pscustomobject]@{ SignerCertificate = [pscustomobject]@{ Subject = $subject; Issuer = $issuer } } }\r\n" +
            "$issuer = 'CN=Microsoft ID Verified CS EOC CA 01, O=Microsoft Corporation, C=US'\r\n" +
            "'1=[' + (Get-SignerProblem (Sig 'CN=Youssef Osseili, O=Youssef Osseili, C=US' $issuer) 'CN=Youssef Osseili' 'Microsoft ID Verified') + ']'\r\n" +
            "'2=' + (Get-SignerProblem (Sig 'CN=Not Youssef Osseili Ltd, C=US' $issuer) 'CN=Youssef Osseili' 'Microsoft ID Verified')\r\n" +
            "'3=' + (Get-SignerProblem (Sig 'O=Youssef Osseili, CN=Someone Else' $issuer) 'CN=Youssef Osseili' 'Microsoft ID Verified')\r\n" +
            "'4=' + (Get-SignerProblem (Sig 'CN=Youssef Osseili' 'CN=Evil Root CA') 'CN=Youssef Osseili' 'Microsoft ID Verified')\r\n" +
            "'5=' + (Get-CertCommonName 'CN=\"Osseili, Youssef\", O=X')\r\n" +
            "'6=' + (Get-CertCommonName 'O=NoCommonName')\r\n");
        Assert.Equal("1=[]", output[0]);
        Assert.Contains("not CN=Youssef Osseili", output[1]);
        Assert.Contains("not CN=Youssef Osseili", output[2]);
        Assert.Contains("not Microsoft ID Verified", output[3]);
        Assert.Equal("5=Osseili, Youssef", output[4]);
        Assert.Equal("6=", output[5]);
        Assert.Equal("Microsoft ID Verified", Signing().GetProperty("issuerCommonNamePrefix").GetString());
        var shared = SourceFiles.Read("tools/ReleaseAssets.ps1");
        Assert.DoesNotContain("-notlike \"*$", shared);
        Assert.DoesNotContain("-like 'CN=Microsoft Corporation", shared);
    }

    [Fact]
    public void Install_always_unpacks_fresh_and_Check_says_when_signatures_are_not_checked()
    {
        var shared = SourceFiles.Read("tools/ReleaseAssets.ps1");
        Assert.DoesNotContain("if (-not (Test-Path -LiteralPath $target)) {", shared);
        Assert.Contains("Remove-Item -LiteralPath $target -Recurse -Force }\r\n    Add-Type", shared.Replace("\n", "\r\n").Replace("\r\r\n", "\r\n"));
        Assert.Contains("signatures NOT checked", shared);
    }

    // ReleaseAssets.ps1 run from text has no $PSScriptRoot to find release-assets.json, so the repo info is
    // a stand in (PowerShell finds a function from the caller's scope).
    private const string NoRepoFiles = "function Get-ReleaseRepoInfo { [pscustomobject]@{ Product = 'AD1259' } }\r\n";

    // Every release script prints its publish commands through Write-PublishCommands. A signed build's
    // check line asks for the signatures, since signedFromVersion alone may not reach this release yet.
    [Fact]
    public void A_signed_build_prints_the_release_check_with_RequireSigned_and_an_unsigned_one_prints_none()
    {
        var signed = RunPowerShell(NoRepoFiles + "Write-PublishCommands 'Coodsz/Coodsz-AD1259' '9.9.9' 'C:\\out' @('C:\\out\\a.zip')\r\n");
        Assert.Contains(@"  .\tools\Check-ReleaseAssets.ps1 -Repo Coodsz/Coodsz-AD1259 -Tag v9.9.9 -RequireSigned", signed);

        var unsigned = RunPowerShell(NoRepoFiles + "Write-PublishCommands 'Coodsz/Coodsz-AD1259' '9.9.9' 'C:\\out' @('C:\\out\\a.zip') -Unsigned\r\n");
        Assert.DoesNotContain(unsigned, line => line.Contains("Check-ReleaseAssets", StringComparison.Ordinal));
        Assert.Contains(unsigned, line => line.StartsWith("UNSIGNED TEST BUILD:", StringComparison.Ordinal));
    }

    [Fact]
    public void Signatures_that_must_be_checked_refuse_SkipZipContents_instead_of_passing()
    {
        var output = RunPowerShell(
            "'1=' + (Get-SkipZipContentsRefusal $true $true)\r\n" +
            "'2=' + ($null -eq (Get-SkipZipContentsRefusal $true $false))\r\n" +
            "'3=' + ($null -eq (Get-SkipZipContentsRefusal $false $true))\r\n");
        Assert.Equal(3, output.Count);
        Assert.StartsWith("1=signatures NOT checked", output[0]);
        Assert.Contains("without -SkipZipContents", output[0]);
        Assert.Equal(["2=True", "3=True"], output.Skip(1).ToList());

        // -RequireSigned stops before gh runs, and a release past signedFromVersion counts as a problem, not a warning.
        var check = SourceFiles.Read("tools/Check-ReleaseAssets.ps1");
        Assert.Contains("$refusal = Get-SkipZipContentsRefusal $RequireSigned.IsPresent $SkipZipContents.IsPresent\r\nif ($refusal) { Write-Host \"-RequireSigned: $refusal\" -ForegroundColor Red; exit 2 }", check.Replace("\r\n", "\n").Replace("\n", "\r\n"));
        Assert.Contains("$refusal = Get-SkipZipContentsRefusal ($checkSignatures -or $checkDlls) $SkipZipContents.IsPresent", check);
        Assert.Contains("if ($refusal) { Write-Host \"  WRONG    $refusal\" -ForegroundColor Red; $problems++ }", check);
        Assert.DoesNotContain("WARNING  signatures NOT checked", check);
        Assert.True(check.IndexOf("Get-SkipZipContentsRefusal $RequireSigned", StringComparison.Ordinal) < check.IndexOf("Get-Command gh", StringComparison.Ordinal));
    }

    [Fact]
    public void Signing_uses_the_exact_signtool_and_verify_commands_and_stops_with_plain_steps()
    {
        var shared = SourceFiles.Read("tools/ReleaseAssets.ps1");
        Assert.Contains("sign /v /fd SHA256 /tr $context.Timestamp /td SHA256 /dlib $context.Dlib /dmdf $context.Metadata $file", shared);
        Assert.Contains("verify /pa /v $File", shared);
        Assert.Contains("Run az login and check you have the Artifact Signing Certificate Profile Signer role on coodsz-signing", shared);
        Assert.Contains("winget install Microsoft.AzureCLI", shared);
        Assert.Contains("az account show", shared);
        Assert.Contains("Test-MicrosoftSigned", shared);
        Assert.Contains("(Get-FileSha512Base64 $nupkg) -ne [string] $Package.sha512", shared);
        Assert.DoesNotContain("??", shared);
        Assert.True(SourceFiles.Read(".gitignore").Contains("tools/.signing/"), "tools/.signing must stay out of git.");
    }

    [Theory]
    [InlineData("tools/Make-Release.ps1", 4)]
    [InlineData("tools/Make-AD1259-Release.ps1", 2)]
    [InlineData("tools/Make-BurningEmpires-Release.ps1", 1)]
    [InlineData("tools/Make-RealmOfThrones-Release.ps1", 1)]
    public void Every_zip_is_made_after_its_exes_are_signed_and_verified(string path, int zips)
    {
        var script = SourceFiles.Read(path);
        Assert.Contains("[switch] $SkipSigning", script);
        Assert.Contains("if ($SkipSigning) { Write-UnsignedWarning } else { Initialize-Signing }", script);
        Assert.Contains("Invoke-ReleaseSigning", script);
        Assert.Contains("-Unsigned:$SkipSigning", script);

        var lines = script.Replace("\r\n", "\n").Split('\n');
        var zipLines = lines.Select((line, index) => (line, index)).Where(item => item.line.TrimStart().StartsWith("New-ReleaseZip ", StringComparison.Ordinal)).ToList();
        Assert.Equal(zips, zipLines.Count);
        var signAt = Array.FindIndex(lines, line => line.Contains("Invoke-ReleaseSigning", StringComparison.Ordinal));
        foreach (var (line, index) in zipLines)
        {
            // Signed first, and the staged folder is verified on the line right before the zip is made.
            Assert.True(signAt < index, line.Trim() + " comes before the exes are signed.");
            Assert.StartsWith("Assert-StagedExesSigned ", lines[index - 1].TrimStart());
        }
        // The checksums and release files are made after the zips, so they describe signed files.
        var sumsAt = Array.FindIndex(lines, line => line.Contains("SHA256SUMS.txt'", StringComparison.Ordinal) && line.Contains("WriteAllText", StringComparison.Ordinal) || line.Contains("Write-Sums ", StringComparison.Ordinal) && !line.TrimStart().StartsWith("function", StringComparison.Ordinal));
        Assert.True(sumsAt > zipLines.Last().index);
    }

    [Fact]
    public void Every_shipped_exe_goes_through_the_sign_step_including_the_TAOM_ones()
    {
        var make = SourceFiles.Read("tools/Make-Release.ps1");
        Assert.Contains("\"$publish\\coop\\Coodsz Coop Client Launcher.exe\"", make);
        Assert.Contains("\"$publish\\host\\Coodsz Coop Server Tool.exe\"", make);
        Assert.Contains("\"$publish\\taom-host\\Coodsz TAOM Server Tool.exe\"", make);
        Assert.Contains("\"$publish\\taom-client\\Coodsz TAOM Client Launcher.exe\"", make);
        Assert.Contains("Invoke-ReleaseSigning @($server, $client)", SourceFiles.Read("tools/Make-AD1259-Release.ps1"));
        Assert.Contains("Invoke-ReleaseSigning @($server, $client)", SourceFiles.Read("tools/Make-BurningEmpires-Release.ps1"));
        Assert.Contains("Invoke-ReleaseSigning @($server, $client)", SourceFiles.Read("tools/Make-RealmOfThrones-Release.ps1"));
        // Every copy that goes into a zip (the legacy names too) is staged from the signed publish output.
        foreach (var path in MakeScripts)
            Assert.DoesNotContain("dotnet publish", SourceFiles.Read(path).Split("Invoke-ReleaseSigning")[1]);
    }

    [Fact]
    public void An_unsigned_test_build_is_marked_and_Check_ReleaseAssets_has_the_signature_rule()
    {
        var shared = SourceFiles.Read("tools/ReleaseAssets.ps1");
        // The marker is written out once; the body, the warnings and Check-ReleaseAssets all use it.
        Assert.Contains("$script:UnsignedMarker = 'UNSIGNED TEST BUILD'", shared);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(shared, "'UNSIGNED TEST BUILD'"));
        Assert.Contains("\"**$($script:UnsignedMarker) - do not publish.**", shared);
        Assert.Contains("[switch] $Unsigned", shared);

        var check = SourceFiles.Read("tools/Check-ReleaseAssets.ps1");
        Assert.Contains("Test-ZipSignatures $zip $signedBy", check);
        Assert.Contains("Test-SignatureRuleApplies", check);
        Assert.Contains("[switch] $RequireSigned", check);
        Assert.Contains("$body.Contains($script:UnsignedMarker)", check);
        Assert.DoesNotContain("'UNSIGNED TEST BUILD'", check);
        Assert.Contains("WRONG    signature:", check);
        Assert.Contains("Get-AuthenticodeSignature", shared);
        Assert.Contains("TimeStamperCertificate", shared);

        using var json = JsonDocument.Parse(SourceFiles.Read("tools/release-assets.json"));
        foreach (var repo in json.RootElement.GetProperty("repos").EnumerateArray())
        {
            Assert.Equal("CN=Youssef Osseili", repo.GetProperty("signedBy").GetString());
            // Every tool is signed now, so an empty value would skip the after publish signature check.
            Assert.True(repo.TryGetProperty("signedFromVersion", out var from) && from.GetString() is { Length: > 0 }, repo.GetProperty("repo").GetString() + " needs signedFromVersion.");
        }
    }

    [Fact]
    public void The_signature_rule_skips_releases_before_signedFromVersion_unless_forced()
    {
        var output = RunPowerShell(
            "function Info($by, $from) { [pscustomobject]@{ SignedBy = $by; SignedFromVersion = $from } }\r\n" +
            "'1=' + (Test-SignatureRuleApplies (Info 'CN=X' '') 'v9.9.9')\r\n" +
            "'2=' + (Test-SignatureRuleApplies (Info 'CN=X' 'v1.4.0') 'v1.3.9')\r\n" +
            "'3=' + (Test-SignatureRuleApplies (Info 'CN=X' 'v1.4.0') 'v1.4.0')\r\n" +
            "'4=' + (Test-SignatureRuleApplies (Info 'CN=X' '') 'v1.0.0' -Force)\r\n" +
            "'5=' + (Test-SignatureRuleApplies (Info '' 'v1.0.0') 'v2.0.0' -Force)\r\n");
        Assert.Equal(["1=False", "2=False", "3=True", "4=True", "5=False"], output);
    }

    [Fact]
    public void An_exe_in_a_zip_without_a_valid_signature_is_reported()
    {
        var folder = Path.Combine(Path.GetTempPath(), "coodsz-signing-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var zip = Path.Combine(folder, "tools.zip");
            using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
            {
                var entry = archive.CreateEntry("Coodsz Tools/Coodsz Server Tool.exe");
                using var stream = entry.Open();
                stream.Write(new byte[] { 0x4D, 0x5A, 0x90, 0x00 });
            }
            var output = RunPowerShell("Test-ZipSignatures '" + zip + "' 'CN=Youssef Osseili'\r\n");
            Assert.Single(output);
            Assert.Contains("Coodsz Server Tool.exe in tools.zip has no valid signature", output[0]);
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Fact]
    public void A_module_folder_without_LICENSE_or_with_an_unsigned_dll_inside_an_exe_is_reported()
    {
        var folder = Path.Combine(Path.GetTempPath(), "coodsz-module-payload-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            // A fake single file exe: the payload zip sits in it as it is, between other bytes.
            string Exe(string name, bool license)
            {
                using var zipBytes = new MemoryStream();
                using (var archive = new ZipArchive(zipBytes, ZipArchiveMode.Create, true))
                {
                    foreach (var entry in new[] { "SubModule.xml", "bin/Win64_Shipping_Client/CoodszJoinPassword.dll", "bin/Win64_Shipping_Client/0Harmony.dll" }.Concat(license ? new[] { "LICENSE.txt" } : Array.Empty<string>()))
                    {
                        using var stream = archive.CreateEntry("DedicatedServer.CoodszJoinPassword/" + entry).Open();
                        stream.Write(new byte[] { 0x4D, 0x5A, 0x90, 0x00 });
                    }
                }

                var path = Path.Combine(folder, name);
                File.WriteAllBytes(path, new byte[] { 0x4D, 0x5A, 1, 2, 3 }.Concat(zipBytes.ToArray()).Concat(new byte[] { 9, 9, 9 }).ToArray());
                return path;
            }

            var bare = Exe("bare.exe", license: false);
            var licensed = Exe("licensed.exe", license: true);
            var output = RunPowerShell(
                "$a = Test-ExeModulePayloads '" + bare + "' @('CoodszJoinPassword.dll') -CheckLicense\r\n" +
                "'A=' + ($a.Problems -join ';') + '|' + ($a.Found -join ',')\r\n" +
                "$b = Test-ExeModulePayloads '" + licensed + "' @('CoodszJoinPassword.dll') -CheckLicense\r\n" +
                "'B=' + $b.Problems.Count\r\n" +
                "$c = Test-ExeModulePayloads '" + licensed + "' @('CoodszJoinPassword.dll') -CheckLicense -CheckSignatures -SignedBy 'CN=Youssef Osseili'\r\n" +
                "'C=' + ($c.Problems -join ';')\r\n" +
                "'D=' + (Test-FromVersion '' 'v9.9.9') + (Test-FromVersion 'v1.3.15' 'v1.3.14') + (Test-FromVersion 'v1.3.15' 'v1.3.15') + '|' +\r\n" +
                // A tag that is not plain vX.Y.Z is compared by its version part, and one without a version is checked anyway.
                "(Test-FromVersion 'v1.3.15' 'v1.3.16-hotfix') + (Test-FromVersion 'v1.3.15' 'coop-v1.3.15') + (Test-FromVersion 'v1.3.15' 'v1.3.14-beta') + (Test-FromVersion 'v1.3.15' 'latest')\r\n");
            Assert.Equal("A=bare.exe carries the module DedicatedServer.CoodszJoinPassword without its LICENSE.txt|CoodszJoinPassword.dll", output[0]);
            Assert.Equal("B=0", output[1]);
            // Harmony is not a Coodsz DLL, so only the module's own DLL needs a signature.
            Assert.StartsWith("C=DedicatedServer.CoodszJoinPassword/bin/Win64_Shipping_Client/CoodszJoinPassword.dll in licensed.exe has no valid signature", output[2]);
            Assert.DoesNotContain("0Harmony", output[2]);
            Assert.Equal("D=FalseFalseTrue|TrueTrueFalseTrue", output[3]);
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    // The new LICENSE.txt ships from each product's next release, so a published release keeps passing with the one it carries.
    [Fact]
    public void A_published_release_keeps_its_license_hash_and_the_next_release_needs_the_new_one()
    {
        var output = RunPowerShell(
            "function Get-ReleaseAssetsFile { '" + Path.Combine(SourceFiles.Root(), "tools", "release-assets.json") + "' }\r\n" +
            "foreach ($run in @(@('Coodsz/Coodsz-TAOM', 'v1.2.15'), @('Coodsz/Coodsz-TAOM', 'v1.2.16'), @('Coodsz/Coodsz-TAOM', 'v1.2.5'), @('Coodsz/Coodsz-TAOM', 'v1.1.40'), @('Coodsz/Coodsz-BurningEmpires', 'v1.0.15'), @('Coodsz/Coodsz-BurningEmpires', 'v1.0.14'), @('Coodsz/Coodsz-BurningEmpires', 'v1.0.16'), @('Coodsz/Coodsz-RealmOfThrones', 'v0.1.0'), @('Coodsz/Coodsz-TAOM', 'v1.2.17'), @('Coodsz/Coodsz-BurningEmpires', 'v1.0.17'), @('Coodsz/Coodsz-RealmOfThrones', 'v0.1.3'))) {\r\n" +
            "  (@(Get-ReleaseAssetSpec $run[0] $run[1]) | Where-Object { $_.Name -eq 'LICENSE.txt' }).Sha256.Substring(0, 8) }\r\n");
        var current = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(SourceFiles.Root(), "LICENSE")))).ToLowerInvariant()[..8];
        Assert.Equal(new[] { "5135fe63", "5b8d80df", "582d3e0e", "253c38e2", "5135fe63", "582d3e0e", "5b8d80df", "5b8d80df", current, current, current }, output);
    }

    [Fact]
    public void Module_dlls_are_signed_before_the_exes_are_built_and_checked_after()
    {
        foreach (var (path, repo) in new[] { ("tools/Make-Release.ps1", "Coodsz/Coodsz-Coop-and-Friends"), ("tools/Make-AD1259-Release.ps1", "Coodsz/Coodsz-AD1259"), ("tools/Make-BurningEmpires-Release.ps1", "Coodsz/Coodsz-BurningEmpires") })
        {
            var script = SourceFiles.Read(path);
            var sign = script.IndexOf("Invoke-ModuleDllSigning ", StringComparison.Ordinal);
            var publish = script.IndexOf("Run 'dotnet' (@('publish'", StringComparison.Ordinal);
            var check = script.IndexOf("Assert-ModulePayloads '" + repo + "'", StringComparison.Ordinal);
            Assert.True(sign > 0 && sign < publish, path + " must sign the module DLLs before it builds the exes.");
            Assert.True(check > publish, path + " must check the module payloads of the built exes.");
        }

        Assert.Contains("Assert-ModulePayloads 'Coodsz/Coodsz-TAOM' @($taomHost, $taomClient)", SourceFiles.Read("tools/Make-Release.ps1"));
        // The Realm of Thrones script reads its repo from ReleaseNotice.RotRepo.
        var rot = SourceFiles.Read("tools/Make-RealmOfThrones-Release.ps1");
        var rotPublish = rot.IndexOf("Run 'dotnet' (@('publish'", StringComparison.Ordinal);
        Assert.InRange(rot.IndexOf("Invoke-ModuleDllSigning ", StringComparison.Ordinal), 1, rotPublish);
        Assert.True(rot.IndexOf("Assert-ModulePayloads $repo @($server, $client)", StringComparison.Ordinal) > rotPublish);
        var check2 = SourceFiles.Read("tools/Check-ReleaseAssets.ps1");
        Assert.Contains("Test-FromVersion $repoInfo.SignedDllsFromVersion $release.tagName", check2);
        Assert.Contains("Test-FromVersion $repoInfo.ModuleLicenseFromVersion $release.tagName", check2);
        Assert.Contains("Test-ZipModulePayloads $zip $repoInfo", check2);

        // signedDllsFromVersion is empty until the first release that signs the DLLs, then that version. No third party DLL is listed.
        using var json = JsonDocument.Parse(SourceFiles.Read("tools/release-assets.json"));
        foreach (var repo in json.RootElement.GetProperty("repos").EnumerateArray())
        {
            var from = repo.GetProperty("signedDllsFromVersion").GetString()!;
            Assert.True(from.Length == 0 || Regex.IsMatch(from, @"^v\d+\.\d+\.\d+$"), "signedDllsFromVersion " + from);
            // An entry is a path, or {"path", "fromVersion"} for a DLL a tool carries only from that release on.
            var dlls = repo.GetProperty("moduleDlls").EnumerateArray()
                .Select(dll => dll.ValueKind == JsonValueKind.String ? dll.GetString()! : dll.GetProperty("path").GetString()!).ToList();
            foreach (var gated in repo.GetProperty("moduleDlls").EnumerateArray().Where(dll => dll.ValueKind == JsonValueKind.Object))
                Assert.Matches(@"^v\d+\.\d+\.\d+$", gated.GetProperty("fromVersion").GetString()!);
            Assert.NotEmpty(dlls);
            foreach (var dll in dlls)
            {
                Assert.True(File.Exists(Path.Combine(SourceFiles.Root(), dll)), dll);
                Assert.Contains(Path.GetFileName(dll), BannerlordCoopLauncher.Core.Support.ModuleFingerprints.DllNames);
                Assert.DoesNotContain("AD1259Coop", dll);
            }
        }
    }

    [Fact]
    public void A_module_DLL_listed_from_a_version_is_only_asked_of_releases_from_that_version_on_and_is_still_signed()
    {
        // Coodsz Server Tools rides in the TAOM Server Tool from v1.2.16: v1.2.15, already published, never carried it.
        const string tools = "DedicatedServer.CoodszTools.dll";
        var output = RunPowerShell(
            "function Get-ReleaseAssetsFile { '" + Path.Combine(SourceFiles.Root(), "tools", "release-assets.json") + "' }\r\n" +
            "'old=' + (((Get-ReleaseRepoInfo 'Coodsz/Coodsz-TAOM' 'v1.2.15').ModuleDlls | ForEach-Object { Split-Path -Leaf $_ }) -join ',')\r\n" +
            "'new=' + (((Get-ReleaseRepoInfo 'Coodsz/Coodsz-TAOM' 'v1.2.16').ModuleDlls | ForEach-Object { Split-Path -Leaf $_ }) -join ',')\r\n" +
            // Get-ModuleDllPaths, which the build check and the signing run use, asks with '0'.
            "'build=' + (((Get-ReleaseRepoInfo 'Coodsz/Coodsz-TAOM' '0').ModuleDlls | ForEach-Object { Split-Path -Leaf $_ }) -join ',')\r\n" +
            "'signable=' + ((Get-SignableModuleDllNames) -join ',')\r\n" +
            "'plain=' + (Test-ModuleDllApplies 'a.dll' 'v0.0.1')\r\n" +
            "try { Get-ModuleDllPath ([pscustomobject]@{ fromVersion = 'v1.2.16' }) } catch { 'nopath=' + $_.Exception.Message }\r\n");
        string Line(string key) => output.Single(line => line.StartsWith(key + "=", StringComparison.Ordinal));

        Assert.DoesNotContain(tools, Line("old"));
        Assert.Contains("CoodszWarSync.dll", Line("old"));
        Assert.Contains(tools, Line("new"));
        // The build check of a new release and the signing run take every entry.
        Assert.Contains(tools, Line("build"));
        Assert.Contains(tools, Line("signable"));
        Assert.Equal("plain=True", Line("plain"));
        // An object entry without a path names the file to fix instead of failing somewhere in Split-Path.
        Assert.Contains(@"tools\release-assets.json has no ""path""", Line("nopath"));
    }

    [Fact]
    public void The_AD1259_server_tools_DLLs_are_only_asked_of_v1_1_17_on_and_are_still_signed()
    {
        // The published AD1259 v1.1.16 Server Tool had no ServerTools payload, so its check must not ask for these two.
        var output = RunPowerShell(
            "function Get-ReleaseAssetsFile { '" + Path.Combine(SourceFiles.Root(), "tools", "release-assets.json") + "' }\r\n" +
            "'old=' + (((Get-ReleaseRepoInfo 'Coodsz/Coodsz-AD1259' 'v1.1.16').ModuleDlls | ForEach-Object { Split-Path -Leaf $_ }) -join ',')\r\n" +
            "'new=' + (((Get-ReleaseRepoInfo 'Coodsz/Coodsz-AD1259' 'v1.1.17').ModuleDlls | ForEach-Object { $_.Replace('\\', '/') }) -join ',')\r\n" +
            "'signable=' + ((Get-SignableModuleDllNames) -join ',')\r\n");
        string Line(string key) => output.Single(line => line.StartsWith(key + "=", StringComparison.Ordinal));

        Assert.DoesNotContain("DedicatedServer.CoodszTools.dll", Line("old"));
        Assert.Contains("CoopSaveRescue.dll", Line("old"));
        Assert.Contains("ServerTools/DedicatedServer.CoodszTools/bin/Win64_Shipping_Server/DedicatedServer.CoodszTools.dll", Line("new"));
        Assert.Contains("ServerTools/DedicatedServer.CoodszTools/bin/Win64_Shipping_Server/CoodszSettingsSync.Coop.dll", Line("new"));
        Assert.Contains("DedicatedServer.CoodszTools.dll", Line("signable"));
    }

    [Fact]
    public void Signing_signs_each_build_once_copies_it_over_every_byte_equal_copy_and_records_both_hashes()
    {
        var folder = Path.Combine(Path.GetTempPath(), "coodsz-module-sign-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            string Put(string name, string text)
            {
                var path = Path.Combine(folder, name);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, text);
                return path;
            }

            // The Coop server copy and the TAOM War Sync copy are byte equal; only the Coop one is asked for, like a
            // Coop only release. The Menu Guard copy in compat\CoodszMenuGuard is tracked but not asked for either.
            var coop = Put(@"host\CoodszSettingsSync.Coop.dll", "server sync");
            var taom = Put(@"taom\CoodszSettingsSync.Coop.dll", "server sync");
            var guard = Put(@"jp\CoodszMenuGuard.dll", "guard");
            var guardCopy = Put(@"mg\CoodszMenuGuard.dll", "guard");
            var signed = Put(@"jp\CoodszJoinPassword.dll", "already signed");
            var other = Put(@"x\Other.dll", "other");
            var list = Put("KnownModuleHashes.txt", "# list\r\n");
            var log = Path.Combine(folder, "signed.txt");
            string Sha(string text) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

            var output = RunPowerShell(
                "$sign = { param($file) Add-Content -LiteralPath '" + log + "' -Value (Split-Path -Leaf $file); [System.IO.File]::AppendAllText($file, ' signed') }\r\n" +
                "$isSigned = { param($file) ([System.IO.File]::ReadAllText($file)) -eq 'already signed' }\r\n" +
                "Invoke-ModuleDllSigning @('" + coop + "', '" + guard + "', '" + signed + "') -Sign $sign -IsSigned $isSigned " +
                "-Tracked @('" + coop + "', '" + taom + "', '" + guard + "', '" + guardCopy + "', '" + signed + "', '" + other + "') -HashList '" + list + "'\r\n" +
                "'changed=' + $script:ModuleDllsChanged\r\n");

            // Each build is signed once, the signed file replaced every byte equal copy, and nothing else was touched.
            Assert.Equal(new[] { "CoodszSettingsSync.Coop.dll", "CoodszMenuGuard.dll" }, File.ReadAllLines(log).OrderByDescending(line => line.Length).ToArray());
            Assert.Equal("server sync signed", File.ReadAllText(coop));
            Assert.Equal("server sync signed", File.ReadAllText(taom));
            Assert.Equal("guard signed", File.ReadAllText(guardCopy));
            Assert.Equal("already signed", File.ReadAllText(signed));
            Assert.Equal("other", File.ReadAllText(other));
            Assert.Contains("changed=4", output);

            // The hash list gets the hash before and after signing, each once.
            var lines = File.ReadAllLines(list);
            foreach (var expected in new[]
            {
                Sha("server sync") + "  CoodszSettingsSync.Coop.dll", Sha("server sync signed") + "  CoodszSettingsSync.Coop.dll",
                Sha("guard") + "  CoodszMenuGuard.dll", Sha("guard signed") + "  CoodszMenuGuard.dll", Sha("already signed") + "  CoodszJoinPassword.dll"
            })
                Assert.Single(lines, line => line == expected);
            Assert.DoesNotContain(lines, line => line.EndsWith("Other.dll", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Fact]
    public void Only_Coodsz_module_dll_names_can_be_signed_or_recorded()
    {
        // Loaded from text, the script has no folder of its own, so the test names the json.
        var output = RunPowerShell("function Get-ReleaseAssetsFile { '" + Path.Combine(SourceFiles.Root(), "tools", "release-assets.json") + "' }\r\n" +
                                   "(Get-SignableModuleDllNames | Sort-Object) -join ','\r\n");
        var names = BannerlordCoopLauncher.Core.Support.ModuleFingerprints.DllNames.Where(name => !name.StartsWith("AD1259", StringComparison.Ordinal)).OrderBy(name => name, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(string.Join(",", names), output.Single(), ignoreCase: true);

        var script = SourceFiles.Read("tools/Sign-ModuleDlls.ps1");
        Assert.Contains("$allowed = Get-SignableModuleDllNames", script);
        Assert.Contains("if ($allowed -notcontains (Split-Path -Leaf $dll)) { throw", script);
        Assert.True(script.IndexOf("$allowed -notcontains", StringComparison.Ordinal) < script.IndexOf("Initialize-Signing", StringComparison.Ordinal));
        // The release scripts sign every tool's DLLs, so a Coop only run still signs the TAOM War Sync copy, and say again to commit them.
        foreach (var path in MakeScripts)
        {
            Assert.Contains("Invoke-ModuleDllSigning (Get-AllModuleDllPaths)", SourceFiles.Read(path));
            Assert.Contains("\r\nWrite-ModuleDllCommitReminder\r\n", SourceFiles.Read(path));
        }
    }

    [Fact]
    public void Sign_ModuleDlls_takes_only_module_folders_of_the_repository()
    {
        var root = SourceFiles.Root();
        string Check(string relative) => "'" + relative + "=' + (Test-ModulePackageFolder '" + Path.Combine(root, relative) + "' '" + root + "')\r\n";
        var output = RunPowerShell(
            Check(@"compat\CoodszMenuGuard\module") + Check(@"artifacts\compat\Limitless.CoopCompat") + Check(@"src\CoodszHost\ServerTools\DedicatedServer.CoodszTools") +
            Check(@"src\TaomCoopHost\WarSync") + Check(@"compat\CoodszMenuGuard") + Check(@"compat\CoodszMenuGuard\module\bin") + Check(@"..\elsewhere\compat\X\module") +
            "'outside=' + (Test-ModulePackageFolder '" + Path.Combine(Path.GetTempPath(), "compat", "X", "module") + "' '" + root + "')\r\n");
        Assert.Equal(new[]
        {
            @"compat\CoodszMenuGuard\module=True", @"artifacts\compat\Limitless.CoopCompat=True", @"src\CoodszHost\ServerTools\DedicatedServer.CoodszTools=True",
            @"src\TaomCoopHost\WarSync=True", @"compat\CoodszMenuGuard=False", @"compat\CoodszMenuGuard\module\bin=False", @"..\elsewhere\compat\X\module=False", "outside=False"
        }, output);

        var script = SourceFiles.Read("tools/Sign-ModuleDlls.ps1");
        // The folder is resolved once and a link or junction is refused, before the check and the listing both use it.
        var resolve = script.IndexOf("$Folder = (Resolve-Path -LiteralPath $Folder).ProviderPath", StringComparison.Ordinal);
        var reparse = script.IndexOf("[System.IO.FileAttributes]::ReparsePoint", StringComparison.Ordinal);
        Assert.True(resolve > 0 && resolve < reparse && reparse < script.IndexOf("Test-ModulePackageFolder $Folder", StringComparison.Ordinal));
        Assert.True(script.IndexOf("Test-ModulePackageFolder $Folder", StringComparison.Ordinal) < script.IndexOf("Get-ChildItem -LiteralPath $Folder", StringComparison.Ordinal));
        Assert.True(script.IndexOf("Get-ModuleDllMarkProblem $dll $rows", StringComparison.Ordinal) < script.IndexOf("Add-KnownModuleHashes", StringComparison.Ordinal));
        Assert.True(script.IndexOf("Get-ModuleDllMarkProblem $dll $rows", StringComparison.Ordinal) < script.IndexOf("Initialize-Signing", StringComparison.Ordinal));
    }

    [Fact]
    public void A_dll_without_all_its_build_marks_is_never_signed_or_recorded()
    {
        var folder = Path.Combine(Path.GetTempPath(), "coodsz-mark-check-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var real = Path.Combine(SourceFiles.Root(), "compat", "CoodszJoinPassword", "module", "bin", "Win64_Shipping_Client", "CoodszJoinPassword.dll");
            var fake = Path.Combine(folder, "CoodszJoinPassword.dll");
            File.WriteAllText(fake, "MZ someone else's DLL");
            var half = Path.Combine(folder, "half", "CoodszJoinPassword.dll");
            Directory.CreateDirectory(Path.GetDirectoryName(half)!);
            // Two of the four marks: the attribute (UTF-8) and the seal (UTF-16).
            File.WriteAllBytes(half, System.Text.Encoding.ASCII.GetBytes("MZ czm-jp-8L5YJ84S ").Concat(System.Text.Encoding.Unicode.GetBytes("WN4L-ac3de9c7-plo")).ToArray());
            var other = Path.Combine(folder, "Other.dll");
            File.WriteAllText(other, "MZ");
            var list = Path.Combine(SourceFiles.Root(), "docs", "design", "copy-marks.md");
            var output = RunPowerShell(
                "$rows = @(Get-CopyMarkRows '" + list + "')\r\n" +
                "'real=' + (Get-ModuleDllMarkProblem '" + real + "' $rows)\r\n" +
                "'fake=' + (Get-ModuleDllMarkProblem '" + fake + "' $rows)\r\n" +
                "'half=' + (Get-ModuleDllMarkProblem '" + half + "' $rows)\r\n" +
                "'other=' + (Get-ModuleDllMarkProblem '" + other + "' $rows)\r\n");
            Assert.Equal("real=", output[0]);
            Assert.Contains("holds 0 of the 4 build marks of CoodszJoinPassword.dll", output[1]);
            Assert.Contains("holds 2 of the 4 build marks", output[2]);
            Assert.Equal("other=Other.dll has no build marks in docs\\design\\copy-marks.md, so it is not signed or recorded.", output[3]);
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Fact]
    public void The_release_scripts_never_sign_a_dll_without_its_build_marks()
    {
        var folder = Path.Combine(Path.GetTempPath(), "coodsz-release-marks-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            // A swapped DLL on a moduleDlls path, and a signing context whose signtool only writes down that it ran.
            var fake = Path.Combine(folder, "CoodszJoinPassword.dll");
            File.WriteAllText(fake, "MZ someone else's DLL");
            var list = Path.Combine(folder, "KnownModuleHashes.txt");
            File.WriteAllText(list, "# list\r\n");
            var ran = Path.Combine(folder, "signtool-ran.txt");
            var marks = Path.Combine(SourceFiles.Root(), "docs", "design", "copy-marks.md");
            var output = RunPowerShell(
                // Loaded from text, the script has no folder of its own, so the test names the mark list.
                "$rows = ${function:Get-CopyMarkRows}\r\n" +
                "function Get-CopyMarkRows { & $rows '" + marks + "' }\r\n" +
                "$script:SigningContext = [pscustomobject]@{ SignTool = { Set-Content -LiteralPath '" + ran + "' -Value 'ran'; $global:LASTEXITCODE = 0 }; Timestamp = 't'; Dlib = 'd'; Metadata = 'm'; SignedBy = 'CN=Youssef Osseili'; IssuerPrefix = '' }\r\n" +
                "try { Invoke-ModuleDllSigning @('" + fake + "') -Tracked @('" + fake + "') -HashList '" + list + "'; 'error=' } catch { 'error=' + $_.Exception.Message }\r\n");

            Assert.Contains("holds 0 of the 4 build marks of CoodszJoinPassword.dll", Assert.Single(output, line => line.StartsWith("error=", StringComparison.Ordinal)));
            Assert.False(File.Exists(ran), "The release signing ran signtool on a DLL without its build marks.");
            Assert.Equal("# list\r\n", File.ReadAllText(list));
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    // Runs PowerShell code after dot sourcing ReleaseAssets.ps1 from text (no script file runs, so the
    // execution policy does not matter) and returns the lines it printed.
    private static List<string> RunPowerShell(string code)
    {
        var folder = Path.Combine(Path.GetTempPath(), "coodsz-signing-run-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var script = "$ErrorActionPreference = 'Stop'\r\n" +
                         ". ([scriptblock]::Create((Get-Content -Raw -LiteralPath '" + Path.Combine(SourceFiles.Root(), "tools", "ReleaseAssets.ps1") + "')))\r\n" +
                         "$result = @(& { " + code + " } *>&1 | ForEach-Object { [string] $_ })\r\n" +
                         "[System.IO.File]::WriteAllLines('" + Path.Combine(folder, "out.txt") + "', [string[]] $result)\r\n";
            var file = Path.Combine(folder, "run.txt");
            File.WriteAllText(file, script);
            var start = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoProfile -Command \"Invoke-Expression (Get-Content -Raw -LiteralPath '" + file + "')\"",
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };
            // CI runs the tests from PowerShell 7, whose module path makes Windows PowerShell load the
            // PowerShell 7 copy of Get-AuthenticodeSignature and fail. Without it Windows PowerShell uses its own.
            start.Environment.Remove("PSModulePath");
            using var process = Process.Start(start);
            Assert.NotNull(process);
            var errors = process!.StandardError.ReadToEnd();
            process.StandardOutput.ReadToEnd();
            Assert.True(process.WaitForExit(120000), "PowerShell did not finish.");
            var output = Path.Combine(folder, "out.txt");
            Assert.True(File.Exists(output), "PowerShell wrote nothing: " + errors);
            return File.ReadAllLines(output).Where(line => line.Length > 0).ToList();
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }
}
