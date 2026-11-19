using BannerlordCoopLauncher.Core.Legal;
using BannerlordCoopLauncher.Core.Updates;

namespace BannerlordCoopLauncher.Tests;

// Every tool already installed runs the updater it shipped with, up to v1.2.78, v1.1.54 and v1.0.33
// (commit 191be48). That updater picks its zip by a part of the asset name and refuses a zip that does
// not hold its own exe name. The two exe releases add Coodsz-<Product>-Tools zips beside the legacy
// zips, so these tests run that exact shipped logic on the new asset lists: every old tool must still
// find its legacy zip, and no new zip may ever be picked by an old rule.
public class ShippedPickZipTests
{
    // Copied from ToolUpdate.PickZip at 191be48, unchanged. Do not "fix" it: it is what players run.
    internal static class Shipped191be48
    {
        public static string? PickZip(string exeFileName, IEnumerable<string> assetNames)
        {
            var exe = Path.GetFileNameWithoutExtension(exeFileName);
            var zips = assetNames.Where(name => name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)).ToList();
            if (IsAD1259Server(exe))
                return zips.FirstOrDefault(name => name.Contains("AD1259-Server", StringComparison.OrdinalIgnoreCase));
            if (IsAD1259Client(exe))
                return zips.FirstOrDefault(name => name.Contains("AD1259-Client-Launcher", StringComparison.OrdinalIgnoreCase));
            if (IsTaomServer(exe))
                return zips.FirstOrDefault(name => name.Contains("Server-Tool", StringComparison.OrdinalIgnoreCase));
            if (IsTaomClient(exe))
                return zips.FirstOrDefault(name => name.Contains("Client-Launcher", StringComparison.OrdinalIgnoreCase));
            if (IsHost(exe))
            {
                return zips.FirstOrDefault(name => name.Contains("Coodsz-Host", StringComparison.OrdinalIgnoreCase)
                        || name.Contains("CoodszHost", StringComparison.OrdinalIgnoreCase))
                    ?? zips.FirstOrDefault(name => name.Contains("Coop-and-Friends", StringComparison.OrdinalIgnoreCase));
            }

            if (IsFriends(exe))
                return zips.FirstOrDefault(name => name.Contains("Coop-and-Friends", StringComparison.OrdinalIgnoreCase));
            return null;
        }

        private static bool IsAD1259Server(string exe) =>
            exe.Contains("AD1259", StringComparison.OrdinalIgnoreCase) && exe.Contains("Server", StringComparison.OrdinalIgnoreCase);

        private static bool IsAD1259Client(string exe) =>
            exe.Contains("AD1259", StringComparison.OrdinalIgnoreCase) && exe.Contains("Client", StringComparison.OrdinalIgnoreCase);

        private static bool IsTaomServer(string exe) =>
            exe.Contains("TAOM", StringComparison.OrdinalIgnoreCase) && exe.Contains("Server", StringComparison.OrdinalIgnoreCase);

        private static bool IsTaomClient(string exe) =>
            exe.Contains("TAOM", StringComparison.OrdinalIgnoreCase) && exe.Contains("Client", StringComparison.OrdinalIgnoreCase);

        private static bool IsHost(string exe) =>
            exe.Equals("CoodszHost", StringComparison.OrdinalIgnoreCase)
            || exe.Contains("Coodsz Host", StringComparison.OrdinalIgnoreCase);

        private static bool IsFriends(string exe) =>
            exe.Contains("Friends", StringComparison.OrdinalIgnoreCase);
    }

    // What Make-Release.ps1 and Make-AD1259-Release.ps1 write for one release, in the order GitHub may list them.
    private static readonly string[] Coop = ["Coodsz-Coop-Tools-v1.3.0.zip", "Coodsz-Coop-and-Friends-v1.3.0.zip", "SHA256SUMS.txt"];

    private static readonly string[] Taom =
    [
        "Coodsz-TAOM-Tools-v1.2.0.zip", "Coodsz-Dedicated-TAOM-Server-Tool-v1.2.0.zip", "Coodsz-TAOM-Client-Launcher-v1.2.0.zip",
        "TAOM-Coop-v0.3.25-CompatOnly-CLIENT-PLAYTEST-1675bea7-a2ac41c2-r2.zip", "TAOM-Coop-v0.3.25-CompatOnly-HOST-OPERATOR-1675bea7-a2ac41c2-r2.zip",
        "LICENSE.txt", "README.-.Instructions.txt", "SHA256SUMS.txt"
    ];

    private static readonly string[] AD1259 = ["Coodsz-AD1259-Tools-v1.1.0.zip", "Coodsz-AD1259-Server-v1.1.0.zip", "Coodsz-AD1259-Client-Launcher-v1.1.0.zip", "SHA256SUMS.txt"];

    private static readonly string[] NewZips = ["Coodsz-Coop-Tools-v1.3.0.zip", "Coodsz-TAOM-Tools-v1.2.0.zip", "Coodsz-AD1259-Tools-v1.1.0.zip"];

    // Each exe a player may have installed, with its release and the legacy zip its updater must pick.
    public static TheoryData<string, string, string> OldTools => new()
    {
        { "CoodszHost.exe", "coop", "Coodsz-Coop-and-Friends-v1.3.0.zip" },
        { "Coodsz Coop and Friends.exe", "coop", "Coodsz-Coop-and-Friends-v1.3.0.zip" },
        { "Coodsz and Friends.exe", "coop", "Coodsz-Coop-and-Friends-v1.3.0.zip" },
        { "Coodsz Dedicated TAOM Server Tool.exe", "taom", "Coodsz-Dedicated-TAOM-Server-Tool-v1.2.0.zip" },
        { "Coodsz TAOM Client Launcher.exe", "taom", "Coodsz-TAOM-Client-Launcher-v1.2.0.zip" },
        { "Coodsz AD1259 Server.exe", "ad1259", "Coodsz-AD1259-Server-v1.1.0.zip" },
        { "Coodsz AD1259 Client Launcher.exe", "ad1259", "Coodsz-AD1259-Client-Launcher-v1.1.0.zip" },
    };

    private static string[] Release(string name) => name switch
    {
        "coop" => Coop,
        "taom" => Taom,
        _ => AD1259
    };

    [Theory]
    [MemberData(nameof(OldTools))]
    public void An_installed_old_tool_still_picks_its_legacy_zip(string exe, string release, string legacyZip)
    {
        Assert.Equal(legacyZip, Shipped191be48.PickZip(exe, Release(release)));
        // GitHub may list the assets in any order.
        Assert.Equal(legacyZip, Shipped191be48.PickZip(exe, Release(release).Reverse()));
    }

    [Theory]
    [MemberData(nameof(OldTools))]
    public void No_old_rule_ever_picks_a_new_zip_even_from_another_product(string exe, string release, string legacyZip)
    {
        _ = release;
        _ = legacyZip;
        foreach (var zip in NewZips)
            Assert.Null(Shipped191be48.PickZip(exe, [zip]));
        Assert.DoesNotContain(Shipped191be48.PickZip(exe, Coop.Concat(Taom).Concat(AD1259)), NewZips);
    }

    [Fact]
    public void The_new_exe_names_never_match_an_old_rule_of_another_product()
    {
        // A new build copied under a new name and somehow run by an old updater: it may only ever find
        // its own product's legacy zip, never another product's.
        Assert.Null(Shipped191be48.PickZip("Coodsz Coop Server Tool.exe", Coop.Concat(Taom).Concat(AD1259)));
        Assert.Null(Shipped191be48.PickZip("Coodsz Coop Client Launcher.exe", Coop.Concat(Taom).Concat(AD1259)));
        Assert.Equal("Coodsz-Dedicated-TAOM-Server-Tool-v1.2.0.zip", Shipped191be48.PickZip("Coodsz TAOM Server Tool.exe", Taom));
        Assert.Equal("Coodsz-AD1259-Server-v1.1.0.zip", Shipped191be48.PickZip("Coodsz AD1259 Server Tool.exe", AD1259));
    }

    [Theory]
    [MemberData(nameof(OldTools))]
    public void The_bridge_updater_picks_the_new_zip_for_every_renamed_tool(string exe, string release, string legacyZip)
    {
        _ = legacyZip;
        var plan = ToolUpdate.PlanInstall(exe, Release(release));
        Assert.NotNull(plan);
        // Coodsz and Friends too: it became the client launcher's More Nations switch.
        Assert.True(plan!.SuccessorOnly);
        Assert.Contains(plan.ZipName, NewZips);
        Assert.Equal(ToolNames.Find(exe)!.NewExeName, plan.PayloadExe);
    }

    [Fact]
    public void The_release_scripts_make_the_new_zips_with_two_exes_and_the_legacy_zips_with_the_old_names()
    {
        var make = SourceFiles.Read("tools/Make-Release.ps1");
        var makeAd = SourceFiles.Read("tools/Make-AD1259-Release.ps1");

        Assert.Contains("\"Coodsz-Coop-Tools-v$version.zip\"", make);
        Assert.Contains("Assert-ToolsZip $toolsZip 'Coodsz Coop Tools' $coopTools", make);
        Assert.Contains("$coopTools = @('Coodsz Coop Server Tool.exe', 'Coodsz Coop Client Launcher.exe')", make);
        Assert.Contains("\"Coodsz-Coop-and-Friends-v$version.zip\"", make);
        Assert.Contains("Join-Path $hostFolder 'CoodszHost.exe'", make);
        Assert.Contains("Join-Path $coop 'Coodsz Coop and Friends.exe'", make);
        Assert.Contains("\"$publish\\coop\\Coodsz Coop Client Launcher.exe\" -Destination (Join-Path $and 'Coodsz and Friends.exe')", make);

        Assert.Contains("\"Coodsz-TAOM-Tools-v$taomVersion.zip\"", make);
        Assert.Contains("Assert-ToolsZip $taomZip 'Coodsz TAOM Tools' $taomTools", make);
        Assert.Contains("'Coodsz Dedicated TAOM Server Tool.exe', \"Coodsz-Dedicated-TAOM-Server-Tool-v$taomVersion.zip\"", make);
        Assert.Contains("'Coodsz TAOM Client Launcher.exe', \"Coodsz-TAOM-Client-Launcher-v$taomVersion.zip\"", make);

        Assert.Contains("\"Coodsz-AD1259-Tools-v$version.zip\"", makeAd);
        Assert.Contains("Assert-ToolsZip $toolsZip 'Coodsz AD1259 Tools' $adTools", makeAd);
        Assert.Contains("'Coodsz AD1259 Server', 'Coodsz AD1259 Server.exe', \"Coodsz-AD1259-Server-v$version.zip\"", makeAd);
        Assert.Contains("'Coodsz AD1259 Client Launcher', 'Coodsz AD1259 Client Launcher.exe', \"Coodsz-AD1259-Client-Launcher-v$version.zip\"", makeAd);

        // The zip checks themselves live beside the release asset list.
        var assets = SourceFiles.Read("tools/ReleaseAssets.ps1");
        Assert.Contains("function Assert-ToolsZip", assets);
        Assert.Contains("function Assert-LegacyZip", assets);
        Assert.Contains("A tools zip holds two exes", assets);
    }

    [Fact]
    public void Every_exe_carries_the_third_party_notices_since_the_new_zips_only_hold_exes()
    {
        Assert.Contains("Copyright (c) 2026 aderimo", ThirdPartyNotices.Text);
        var file = SourceFiles.Read("THIRD-PARTY-NOTICES.md").Replace("\r\n", "\n").Trim().Replace("\n", "\r\n");
        Assert.Equal(file, ThirdPartyNotices.Text);
        Assert.Contains("ThirdPartyNotices.Text", SourceFiles.Read("src/BannerlordCoopLauncher/UiLanguage.cs"));
        Assert.False(string.IsNullOrWhiteSpace(ToolLicense.Text));
    }
}
