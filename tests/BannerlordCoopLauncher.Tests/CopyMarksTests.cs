using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using BannerlordCoopLauncher.Core.Support;

namespace BannerlordCoopLauncher.Tests;

// The anti copy measures: the build marks in every module and tool (docs/design/copy-marks.md), the module
// fingerprints in a report (coodsz-modules.txt) and the license in every packed module folder.
public class CopyMarksTests
{
    [Fact]
    public void A_dll_with_a_known_hash_is_our_build_and_any_other_is_changed()
    {
        var root = Path.Combine(Path.GetTempPath(), "coodsz-fingerprints-" + Guid.NewGuid().ToString("N"));
        try
        {
            var ours = Write(Path.Combine(root, "Modules", "DedicatedServer.CoodszJoinPassword", "bin", "Win64_Shipping_Client", "CoodszJoinPassword.dll"), "our build bytes");
            Write(Path.Combine(root, "Modules", "Copied", "bin", "Win64_Shipping_Client", "CoodszMenuGuard.dll"), "edited bytes");
            Write(Path.Combine(root, "Server", "DedicatedServer.CoodszTools", "bin", "Win64_Shipping_Server", "DedicatedServer.CoodszTools.dll"), "server bytes");
            // Not a Coodsz DLL name, so it is never listed or hashed.
            Write(Path.Combine(root, "Modules", "Other", "bin", "Win64_Shipping_Client", "Other.dll"), "x");

            var oursHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("our build bytes"))).ToLowerInvariant();
            Assert.Equal(oursHash, ModuleFingerprints.Sha256(ours));
            var known = ModuleFingerprints.ParseKnown("# notes\r\n" + oursHash.ToUpperInvariant() + "  CoodszJoinPassword.dll\r\nnot-a-hash\r\n");
            Assert.Equal(new[] { oursHash }, known);

            var found = ModuleFingerprints.Find([("Modules", Path.Combine(root, "Modules")), ("Server\\Modules", Path.Combine(root, "Server")), ("Gone", Path.Combine(root, "missing"))], known);

            Assert.Equal(3, found.Count);
            Assert.Equal(@"Modules\Copied\bin\Win64_Shipping_Client\CoodszMenuGuard.dll", found[0].Where);
            Assert.False(found[0].OurBuild);
            Assert.Equal(@"Modules\DedicatedServer.CoodszJoinPassword\bin\Win64_Shipping_Client\CoodszJoinPassword.dll", found[1].Where);
            Assert.True(found[1].OurBuild);
            Assert.Equal(oursHash, found[1].Sha256);
            Assert.False(found[2].OurBuild);

            var text = ModuleFingerprints.Text(found);
            Assert.Contains(@"Modules\DedicatedServer.CoodszJoinPassword\bin\Win64_Shipping_Client\CoodszJoinPassword.dll | no version | " + oursHash + " | our build", text);
            Assert.Contains("CoodszMenuGuard.dll | no version | ", text);
            Assert.Contains("| not in this tool's list", text);
            // Only labels and module folders, never the folder on this PC.
            Assert.DoesNotContain(root, text, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("No Coodsz module found.", ModuleFingerprints.Text([]));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void The_report_looks_in_the_game_the_workshop_items_and_each_server()
    {
        var roots = ModuleFingerprints.Roots(@"D:\Steam\steamapps\common\Mount & Blade II Bannerlord", [@"C:\cdst", null, @"C:\cdst"]);
        Assert.Equal(new (string, string)[]
        {
            ("Modules", @"D:\Steam\steamapps\common\Mount & Blade II Bannerlord\Modules"),
            ("Workshop", @"D:\Steam\steamapps\workshop\content\261550"),
            (@"Server\Modules", @"C:\cdst\Modules"),
            (@"Server\engine\Modules", @"C:\cdst\engine\Modules")
        }, roots);
        Assert.Empty(ModuleFingerprints.Roots(null));
    }

    [Fact]
    public void The_build_lists_the_hash_of_every_module_dll_in_the_repository_and_of_earlier_builds()
    {
        foreach (var dll in new[]
        {
            "compat/CoodszJoinPassword/module/bin/Win64_Shipping_Client/CoodszJoinPassword.dll",
            "compat/BurningEmpiresCoopCompat/module/bin/Win64_Shipping_Server/BurningEmpires.CoopCompat.dll",
            "src/CoodszHost/ServerTools/DedicatedServer.CoodszTools/bin/Win64_Shipping_Server/DedicatedServer.CoodszTools.dll",
            "src/TaomCoopHost/WarSync/CoodszWarSync.dll"
        })
        {
            var path = Path.Combine(SourceFiles.Root(), dll);
            Assert.Contains(ModuleFingerprints.Sha256(path), ModuleFingerprints.KnownHashes);
            Assert.Contains(Path.GetFileName(path), ModuleFingerprints.DllNames);
        }

        // Support/KnownModuleHashes.txt keeps builds that are no longer in the repository (here the Join Password build before the marks), so removing that list fails this.
        Assert.Contains("9ff3c90404cc58e025214ae7291c9d70f7a9d510e2eeca2cfd7fada9e7df00a3", ModuleFingerprints.KnownHashes);
    }

    [Fact]
    public void A_report_with_module_roots_gets_coodsz_modules_txt_marked_as_generated()
    {
        var folder = Path.Combine(Path.GetTempPath(), "coodsz-fingerprint-zip-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            Write(Path.Combine(folder, "Modules", "DedicatedServer.CoodszJoinPassword", "bin", "Win64_Shipping_Client", "CoodszJoinPassword.dll"), "edited");
            // A folder name can hold a Steam id or a password; the file is cleaned like the logs.
            Write(Path.Combine(folder, "Modules", "Copy-76561198012345678-hunter2", "bin", "Win64_Shipping_Client", "CoodszMenuGuard.dll"), "edited");
            // A real build with a four part version (1.0.0.0), and a password that happens to sit inside its hash:
            // the version is not read as an address and the hash stays whole, so it neither breaks nor gives the password away.
            var real = Path.Combine(SourceFiles.Root(), "compat", "BurningEmpiresCoopCompat", "module", "bin", "Win64_Shipping_Client", "BurningEmpires.CoopCompat.dll");
            var realCopy = Path.Combine(folder, "Modules", "BurningEmpires.CoopCompat", "bin", "Win64_Shipping_Client", "BurningEmpires.CoopCompat.dll");
            Directory.CreateDirectory(Path.GetDirectoryName(realCopy)!);
            File.Copy(real, realCopy);
            var realHash = ModuleFingerprints.Sha256(real);
            var hexSecret = realHash.Substring(20, 8);
            var zip = Path.Combine(folder, "report.zip");
            using (ZipFile.Open(zip, ZipArchiveMode.Create))
            {
            }

            var request = new CrashReportRequest { Tool = "taom", Side = "server", ToolVersion = "1.2.14", Secrets = ["hunter2", hexSecret], Collect = (_, _) => [], ModuleRoots = [("Modules", Path.Combine(folder, "Modules"))] };
            CrashReports.AddGenerated(zip, request);
            CrashReports.AddGenerated(zip, request);

            using var archive = ZipFile.OpenRead(zip);
            Assert.Single(archive.Entries, entry => entry.FullName == ModuleFingerprints.FileName);
            using (var reader = new StreamReader(archive.GetEntry(ModuleFingerprints.FileName)!.Open()))
            {
                var text = reader.ReadToEnd();
                Assert.StartsWith("Listed by Coodsz TAOM Server Tool 1.2.14. A hash it does not list may be a newer Coodsz build: check it against the module DLLs and src/BannerlordCoopLauncher.Core/Support/KnownModuleHashes.txt on master.", text);
                Assert.Contains(@"Modules\DedicatedServer.CoodszJoinPassword\bin\Win64_Shipping_Client\CoodszJoinPassword.dll | no version | ", text);
                Assert.Contains("CoodszMenuGuard.dll | no version | ", text);
                Assert.DoesNotContain("76561198012345678", text);
                Assert.DoesNotContain("hunter2", text);
                Assert.Contains(@"Modules\BurningEmpires.CoopCompat\bin\Win64_Shipping_Client\BurningEmpires.CoopCompat.dll | 1.0.0.0 | " + realHash + " | our build\r\n", text);
            }
            using (var reader = new StreamReader(archive.GetEntry(CrashReports.ManifestName)!.Open()))
                Assert.Matches("\"name\":\\s*\"coodsz-modules.txt\"[^}]*\"generated\":\\s*true", reader.ReadToEnd());

            // The Realm of Thrones tools name themselves too.
            var rot = Path.Combine(folder, "rot.zip");
            using (ZipFile.Open(rot, ZipArchiveMode.Create))
            {
            }
            CrashReports.AddGenerated(rot, new CrashReportRequest { Tool = "rot", Side = "client", ToolVersion = "0.1.0", Collect = (_, _) => [], ModuleRoots = [("Modules", Path.Combine(folder, "Modules"))] });
            using (var rotArchive = ZipFile.OpenRead(rot))
            using (var reader = new StreamReader(rotArchive.GetEntry(ModuleFingerprints.FileName)!.Open()))
                Assert.StartsWith("Listed by Coodsz Realm of Thrones Client Launcher 0.1.0.", reader.ReadToEnd());

            // A tool that names no folders sends the same zip as before.
            var plain = Path.Combine(folder, "plain.zip");
            using (ZipFile.Open(plain, ZipArchiveMode.Create))
            {
            }

            CrashReports.AddGenerated(plain, new CrashReportRequest { Collect = (_, _) => [] });
            using var plainArchive = ZipFile.OpenRead(plain);
            Assert.Null(plainArchive.GetEntry(ModuleFingerprints.FileName));
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Fact]
    public void A_list_cut_at_the_cap_says_so()
    {
        var full = Enumerable.Range(0, ModuleFingerprints.MaxDlls).Select(i => new ModuleFingerprint("Modules\\M" + i + "\\bin\\Win64_Shipping_Client\\CoodszJoinPassword.dll", "", new string('a', 64), false)).ToList();
        Assert.Contains("Stopped at 200 DLLs, so more may be on this PC.", ModuleFingerprints.Text(full));
        Assert.DoesNotContain("Stopped at", ModuleFingerprints.Text(full.Take(3).ToList()));
    }

    [Fact]
    public void A_version_a_changed_dll_fills_with_columns_or_lines_cannot_forge_a_row()
    {
        Assert.Equal("1.0.0.0", ModuleFingerprints.SafeVersion("1.0.0.0"));
        Assert.Equal("1.2.3-beta+5_x", ModuleFingerprints.SafeVersion(" 1.2.3-beta+5_x "));
        Assert.Equal("", ModuleFingerprints.SafeVersion(null));
        Assert.Equal("odd version", ModuleFingerprints.SafeVersion("1.0 | " + new string('a', 64) + " | our build\r\nModules\\X"));
        Assert.Equal("odd version", ModuleFingerprints.SafeVersion(new string('1', 41)));

        var forged = ModuleFingerprints.Text([new ModuleFingerprint(@"Modules\X\bin\Win64_Shipping_Client\CoodszJoinPassword.dll", ModuleFingerprints.SafeVersion("1 | " + new string('b', 64) + " | our build\r\nY"), new string('c', 64), false)]);
        Assert.DoesNotContain("our build", forged.Split("\r\n")[2]);
        Assert.Equal(4, forged.Split("\r\n").Length);
    }

    [Fact]
    public void A_folder_name_keeps_only_plain_characters_and_never_a_Steam_id()
    {
        // Next line (U+0085), line separator (U+2028), right to left override (U+202E), look-alike bars and a Steam id inside a word.
        var folder = "Mod_76561198012345678\u0085x\u2028y\u202Ez\u2223\uFF5C\u01C0|end";
        var clean = ModuleFingerprints.CleanFolder(folder);
        Assert.Equal("Mod_steamid_x_y_z____end", clean);
        Assert.Matches(@"^[A-Za-z0-9 _.\-+()\[\]]*$", clean);
        Assert.Equal("DedicatedServer.CoodszJoinPassword (copy) [2]", ModuleFingerprints.CleanFolder("DedicatedServer.CoodszJoinPassword (copy) [2]"));
        Assert.Equal(ModuleFingerprints.MaxFolderChars, ModuleFingerprints.CleanFolder(new string('a', 300)).Length);
        Assert.Equal("odd version", ModuleFingerprints.SafeVersion("76561198012345678"));
    }

    [Fact]
    public void A_locked_dll_gets_a_could_not_read_row_instead_of_vanishing()
    {
        var root = Path.Combine(Path.GetTempPath(), "coodsz-fingerprint-locked-" + Guid.NewGuid().ToString("N"));
        try
        {
            var dll = Write(Path.Combine(root, "M", "bin", "Win64_Shipping_Client", "CoodszJoinPassword.dll"), "locked");
            using (new FileStream(dll, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                var found = ModuleFingerprints.Find([("Modules", root)], new HashSet<string>());
                Assert.Single(found);
                Assert.False(found[0].Checked);
                Assert.Contains(@"Modules\M\bin\Win64_Shipping_Client\CoodszJoinPassword.dll | no version | could not read | not checked", ModuleFingerprints.Text(found));
            }
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void The_Coop_client_compiles_its_mark_although_default_items_are_off()
    {
        var project = SourceFiles.Read("src/CoodszCoopAndFriends/CoodszCoopAndFriends.csproj");
        Assert.Contains("<EnableDefaultItems>false</EnableDefaultItems>", project);
        Assert.Contains("<Compile Include=\"CopyMark.cs\" />", project);
    }

    [Fact]
    public void Every_tool_names_its_module_folders_for_the_report()
    {
        foreach (var project in new[] { "BannerlordCoopLauncher", "CoodszHost", "AD1259Launcher", "BurningEmpiresHost", "BurningEmpiresLauncher", "TaomCoopHost", "TaomCoopLauncher", "RealmOfThronesHost", "RealmOfThronesLauncher" })
            Assert.Contains("ModuleRoots = ModuleFingerprints.Roots(", SourceFiles.Read("src/" + project + "/MainWindow.CrashReports.cs"));
    }

    [Fact]
    public void Every_mark_in_the_private_list_is_in_its_CopyMark_file_and_every_mark_file_is_listed()
    {
        var rows = Regex.Matches(SourceFiles.Read("docs/design/copy-marks.md"), @"^\|\s*(?<module>[^|]+?)\s*\|\s*(?<dll>[^|]+?)\s*\|\s*(?<kind>[^|]+?)\s*\|\s*`(?<mark>[^`]+)`\s*\|\s*$", RegexOptions.Multiline)
            .Select(match => match.Groups["mark"].Value).ToList();
        var files = Directory.GetFiles(Path.Combine(SourceFiles.Root(), "compat"), "CopyMark.cs", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(Path.Combine(SourceFiles.Root(), "src"), "CopyMark.cs", SearchOption.AllDirectories))
            .Where(path => !path.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar))
            .ToList();
        Assert.Equal(24, files.Count);
        Assert.Equal(files.Count * 4, rows.Count);
        Assert.Equal(rows.Count, rows.Distinct().Count());
        var sources = string.Join("\n", files.Select(File.ReadAllText));
        foreach (var mark in rows)
            Assert.Contains(mark, sources);
        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            var marks = Regex.Matches(text, "\"(czm-[^\"]+|[A-Z0-9]{4}-[0-9a-f]{8}-[a-z]{3}|cz\\.[0-9a-f]{10})\"|class (Kx\\w+)").Select(match => match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value).ToList();
            Assert.Equal(4, marks.Count);
            Assert.All(marks, mark => Assert.Contains(mark, rows));
        }

        // SkyApolo's AD1259 modules are not marked; the AD1259 Server Tool compiles Coodsz Host's sources but not its mark.
        Assert.False(File.Exists(Path.Combine(SourceFiles.Root(), "compat", "AD1259CoopCompat", "src", "CopyMark.cs")));
        Assert.Contains(@"$(HostSource)CopyMark.cs", SourceFiles.Read("src/AD1259Host/AD1259Host.csproj"));
        Assert.Contains(@"src\CopyMark.cs", SourceFiles.Read("compat/CoodszJoinPassword/build.ps1"));
        Assert.Contains(@"src\CopyMark.cs", SourceFiles.Read("compat/CoodszMenuGuard/build.ps1"));
        Assert.Contains("SubModule.cs CopyMark.cs", SourceFiles.Read("compat/CoopSaveRescue/Build.sh"));
    }

    [Theory]
    [InlineData("compat/BurningEmpiresCoopCompat/module", false, false)]
    [InlineData("compat/CoodszJoinPassword/module", false, true)]
    [InlineData("compat/CoodszMenuGuard/module", false, false)]
    [InlineData("src/CoodszHost/ServerTools/DedicatedServer.CoodszTools", true, true)]
    [InlineData("src/CoodszHost/ImageExporter/CoodszImageExporter", true, false)]
    [InlineData("src/CoodszHost/PatchModules/DedicatedServer.IGCoopCompat", false, false)]
    [InlineData("src/CoodszHost/SaveRescue/DedicatedServer.CoopSaveRescue", false, false)]
    [InlineData("compat/DiplomacyCoopCompat", false, false)]
    [InlineData("compat/LimitlessCoopCompat", false, false)]
    [InlineData("compat/StuckBattleFix", false, false)]
    [InlineData("compat/RealmOfThronesCoopCompat/module", false, false)]
    public void Every_packed_module_folder_carries_the_module_license_with_the_notices_of_code_from_others(string folder, bool coopKeep, bool modderLords)
    {
        var license = SourceFiles.Read(folder + "/LICENSE.txt").Replace("\r\n", "\n");
        Assert.StartsWith(SourceFiles.Read("compat/MODULE-LICENSE.txt").Replace("\r\n", "\n").TrimEnd(), license);
        Assert.Equal(coopKeep, license.Contains("Copyright (c) 2026 aderimo\n\nPermission is hereby granted, free of charge"));
        Assert.Equal(modderLords, license.Contains("Required Notice: Copyright PlueRyvius (https://github.com/PlueRyvius/ModderLords)"));
    }

    [Fact]
    public void The_build_scripts_pack_the_license_with_the_module()
    {
        foreach (var script in new[] { "compat/DiplomacyCoopCompat/Build.cmd", "compat/LimitlessCoopCompat/Build.cmd", "compat/StuckBattleFix/Build.cmd" })
        {
            var text = SourceFiles.Read(script);
            Assert.Contains("copy /y LICENSE.txt \"%MOD%\\\"", text);
            // Every Build.cmd run records the DLL hashes, and Build.cmd sign signs them first, before the zip is made.
            Assert.Contains("set \"SIGN=-RecordOnly\"\r\nif /i \"%~1\"==\"sign\" set \"SIGN=\"", text.Replace("\r\r\n", "\r\n"));
            var sign = text.IndexOf("tools\\Sign-ModuleDlls.ps1\" \"%MOD%\" %SIGN%", StringComparison.Ordinal);
            Assert.True(sign > 0 && sign < text.IndexOf("tar.exe\" -a -c", StringComparison.Ordinal), script);
        }

        var be = SourceFiles.Read("compat/BurningEmpiresCoopCompat/Build.cmd");
        Assert.Contains("copy /y LICENSE.txt \"%SHIP%\\\"", be);
        Assert.Contains("copy /y LICENSE.txt \"%SHIP%\\\"", SourceFiles.Read("compat/RealmOfThronesCoopCompat/Build.cmd"));
        Assert.True(be.IndexOf("tools\\Sign-ModuleDlls.ps1\" \"%SHIP%\"", StringComparison.Ordinal) < be.IndexOf("xcopy /e", StringComparison.Ordinal));
        foreach (var script in new[] { "compat/CoodszServerTools/build.ps1", "compat/ImprovedGarrisonsCoopCompat/build.ps1", "compat/CoodszImageExporter/build.ps1" })
            Assert.Contains("(Join-Path $here 'LICENSE.txt') -Destination $payload", SourceFiles.Read(script));
        Assert.Contains("cp SubModule.xml README.txt LICENSE.txt", SourceFiles.Read("compat/CoopSaveRescue/Build.sh"));
    }

    [Fact]
    public void Coodsz_Server_Tools_patches_under_one_coodsz_Harmony_id()
    {
        Assert.Contains("public const string HarmonyId = \"coodsz.servertools\";", SourceFiles.Read("compat/CoodszServerTools/src/ToolsSubModule.cs"));
        foreach (var file in Directory.GetFiles(Path.Combine(SourceFiles.Root(), "compat", "CoodszServerTools", "src"), "*.cs"))
            Assert.DoesNotContain("new Harmony(\"", File.ReadAllText(file));
    }

    private static string Write(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        return path;
    }
}
