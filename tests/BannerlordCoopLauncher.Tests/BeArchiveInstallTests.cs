using System.Security.Cryptography;
using System.Text;
using Xunit.Abstractions;
using static BurningEmpires.Core.Tests.BeServerFixtures;

namespace BurningEmpires.Core.Tests;

public class BeArchiveInstallTests(ITestOutputHelper output)
{
    private const string HotfixDll = "hotfix dll";
    private const string HotfixProbe = "hotfix probe";

    private static string Sha(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    private static readonly string BeXml = SubModuleXmlWithDlls("BurningEmpires", "v2.0.2", "BurningEmpires.dll", "BurningEmpires.DistanceCacheProbe.dll");

    private static string SubModuleXmlWithDlls(string id, string version, params string[] dlls) =>
        "<Module>\n  <Id value=\"" + id + "\"/>\n  <Version value=\"" + version + "\"/>\n  <SubModules>\n"
        + string.Concat(dlls.Select(dll => "    <SubModule>\n      <DLLName value=\"" + dll + "\"/>\n    </SubModule>\n"))
        + "  </SubModules>\n</Module>\n";

    // The install with test hashes for the fake 2.0.2 DLLs and room on every drive.
    private static IReadOnlyList<string> Install(IReadOnlyList<string> archives, string game, string scratch, string? tar = null, Func<string, long>? free = null) =>
        BeArchive.InstallOverlay(archives, game, scratch, null, tar, Sha(HotfixDll), Sha(HotfixProbe), free ?? (_ => long.MaxValue));

    private static string MainZip(string folder, string name = "BurningEmpires 2.0 (For Bannerlord v1.4.8) 7750 2.0.1.zip") =>
        Zip(Path.Combine(folder, name),
            ("BurningEmpires/SubModule.xml", SubModuleXmlWithDlls("BurningEmpires", "v2.0.1", "BurningEmpires.dll", "BurningEmpires.DistanceCacheProbe.dll")),
            ("BurningEmpires/ModuleData/settlements.xml", "main settlements"),
            ("BurningEmpires/bin/Win64_Shipping_Client/BurningEmpires.dll", "main dll"),
            ("BurningEmpires/bin/Win64_Shipping_Client/BurningEmpires.DistanceCacheProbe.dll", "main probe"),
            ("BurningEmpires/bin/Win64_Shipping_Client/BurningEmpires.pdb", "symbols"),
            ("BurningEmpires_Elephants/SubModule.xml", SubModuleXml("BurningEmpires_Elephants", "v1.1.0")));

    private static string HotfixZip(string folder, string name = "BurningEmpires 2.0.2 Hotfix 7750 2.0.2.zip") =>
        Zip(Path.Combine(folder, name),
            ("BurningEmpires_2.0.2_Hotfix/BurningEmpires/SubModule.xml", BeXml),
            ("BurningEmpires_2.0.2_Hotfix/BurningEmpires/bin/Win64_Shipping_Client/BurningEmpires.dll", HotfixDll),
            ("BurningEmpires_2.0.2_Hotfix/BurningEmpires/bin/Win64_Shipping_Client/BurningEmpires.DistanceCacheProbe.dll", HotfixProbe));

    // A 2.0.1 install from the main file, which the hotfix alone can go on top of.
    private static void EarlierInstall(string game)
    {
        var modules = Path.Combine(game, "Modules");
        Module(modules, "BurningEmpires", "BurningEmpires", "v2.0.1");
        Write(Path.Combine(modules, "BurningEmpires", "ModuleData", "settlements.xml"), "main settlements");
        Module(modules, "BurningEmpires_Elephants", "BurningEmpires_Elephants", "v1.1.0");
    }

    [Fact]
    public void TheOverlayPutsTheHotfixOnTheMainFileAndKeepsThePlayersBeSavedata()
    {
        var root = NewRoot("overlay");
        try
        {
            var game = Path.Combine(root, "game");
            var module = Path.Combine(game, "Modules", "BurningEmpires");
            var saveData = Write(Path.Combine(module, "BESavedata", "campaigns", "c1", "save_slots", "Mine", "index.json"), "my campaign");
            var leftover = Write(Path.Combine(module, "ModuleData", "old_extra.xml"), "kept");
            var downloads = Path.Combine(root, "Downloads");
            var main = MainZip(downloads);
            var hotfix = HotfixZip(downloads);

            // Given hotfix first, the main file still goes first.
            var lines = Install([hotfix, main], game, Path.Combine(root, "scratch"));

            Assert.Equal(2, lines.Count);
            Assert.Contains(lines, line => line.Contains("Burning Empires Elephants"));
            Assert.Equal("my campaign", File.ReadAllText(saveData));
            Assert.Equal("kept", File.ReadAllText(leftover));
            Assert.Equal("main settlements", File.ReadAllText(Path.Combine(module, "ModuleData", "settlements.xml")));
            Assert.Equal(HotfixDll, File.ReadAllText(Path.Combine(module, "bin", "Win64_Shipping_Client", "BurningEmpires.dll")));
            Assert.Equal(HotfixProbe, File.ReadAllText(Path.Combine(module, "bin", "Win64_Shipping_Client", "BurningEmpires.DistanceCacheProbe.dll")));
            Assert.Equal("v2.0.2", BeServerPath.VersionOf(module));
            Assert.True(File.Exists(Path.Combine(game, "Modules", "BurningEmpires_Elephants", "SubModule.xml")));
            Assert.Empty(Directory.EnumerateFileSystemEntries(Path.Combine(root, "scratch")));
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void ACopyThatFailsHalfwayPutsModulesBackTheWayItWas()
    {
        var root = NewRoot("overlay-rollback");
        try
        {
            var game = Path.Combine(root, "game");
            EarlierInstall(game);
            var module = Path.Combine(game, "Modules", "BurningEmpires");
            var downloads = Path.Combine(root, "Downloads");
            var main = MainZip(downloads);
            var hotfix = HotfixZip(downloads);
            var scratch = Path.Combine(root, "scratch");

            // Burning Empires goes in first, then Elephants fails: its SubModule.xml is held open.
            var elephantsXml = Path.Combine(game, "Modules", "BurningEmpires_Elephants", "SubModule.xml");
            BeArchiveException error;
            using (new FileStream(elephantsXml, FileMode.Open, FileAccess.Read, FileShare.None))
                error = Assert.Throws<BeArchiveException>(() => Install([main, hotfix], game, scratch));

            Assert.Contains("was put back the way it was", error.Message);
            Assert.Equal("v2.0.1", BeServerPath.VersionOf(module));
            Assert.Equal("main settlements", File.ReadAllText(Path.Combine(module, "ModuleData", "settlements.xml")));
            Assert.False(Directory.Exists(Path.Combine(module, "bin")));
            Assert.Equal("v1.1.0", BeServerPath.VersionOf(Path.Combine(game, "Modules", "BurningEmpires_Elephants")));
            Assert.Empty(Directory.EnumerateFileSystemEntries(scratch));
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void ATarListingWithALinkIsRefusedBeforeAnythingIsUnpacked()
    {
        Assert.False(BeArchive.HasTarLink("drwxr-xr-x  0 0      0           0 Sep 12  2025 BurningEmpires/\r\n-rw-r--r--  0 0      0         120 Sep 12  2025 BurningEmpires/SubModule.xml\r\n"));
        Assert.True(BeArchive.HasTarLink("-rw-r--r--  0 0      0         120 Sep 12  2025 a.txt\nlrwxrwxrwx  0 0      0           0 Sep 12  2025 BurningEmpires/x -> ../../../Windows\n"));
        Assert.True(BeArchive.HasTarLink("hrw-r--r--  0 0      0           0 sept. 12  2025 b link to a.txt\n"));

        var tar = BeArchive.TarPath();
        if (!File.Exists(tar))
        {
            output.WriteLine("Skipped: " + tar + " is not on this PC.");
            return;
        }

        var root = NewRoot("tar-link");
        try
        {
            // A tar with a symbolic link entry; tar.exe reads the format from the content, so it can be named .rar.
            var evil = Path.Combine(root, "BurningEmpires link.rar");
            using (var stream = File.Create(evil))
            using (var writer = new System.Formats.Tar.TarWriter(stream))
            {
                writer.WriteEntry(new System.Formats.Tar.PaxTarEntry(System.Formats.Tar.TarEntryType.RegularFile, "BurningEmpires/SubModule.xml")
                {
                    DataStream = new MemoryStream(Encoding.UTF8.GetBytes(BeXml))
                });
                writer.WriteEntry(new System.Formats.Tar.PaxTarEntry(System.Formats.Tar.TarEntryType.SymbolicLink, "BurningEmpires/escape") { LinkName = "../../outside" });
            }

            var target = Path.Combine(root, "x");
            var error = Assert.Throws<BeArchiveException>(() => BeArchive.Extract(evil, target, tar));
            Assert.Contains("has a link in it", error.Message);
            Assert.Empty(Directory.EnumerateFileSystemEntries(target));
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void AnArchiveNeverWritesIntoBeSavedata()
    {
        var root = NewRoot("overlay-savedata");
        try
        {
            var game = Path.Combine(root, "game");
            EarlierInstall(game);
            var saveData = Write(Path.Combine(game, "Modules", "BurningEmpires", "BESavedata", "global_index.json"), "mine");
            var archive = Zip(Path.Combine(root, "BurningEmpires 2.0.2 Hotfix.zip"),
                ("BurningEmpires/SubModule.xml", BeXml),
                ("BurningEmpires/bin/Win64_Shipping_Client/BurningEmpires.dll", HotfixDll),
                ("BurningEmpires/bin/Win64_Shipping_Client/BurningEmpires.DistanceCacheProbe.dll", HotfixProbe),
                ("BurningEmpires/BESavedata/global_index.json", "from the archive"));

            Install([archive], game, Path.Combine(root, "scratch"));

            Assert.Equal("mine", File.ReadAllText(saveData));
        }
        finally
        {
            Delete(root);
        }
    }

    // The real 2.0.2 hashes are the default: fake DLLs never reach Modules through the public call.
    [Fact]
    public void TheDefaultInstallRefusesDllsThatAreNotTheReal202Files()
    {
        var root = NewRoot("overlay-hash");
        try
        {
            var game = Path.Combine(root, "game");
            var downloads = Path.Combine(root, "Downloads");

            var error = Assert.Throws<BeArchiveException>(() => BeArchive.InstallOverlay([MainZip(downloads), HotfixZip(downloads)], game, Path.Combine(root, "scratch")));

            Assert.Contains("BurningEmpires.dll", error.Message);
            Assert.Contains("main file first, then the hotfix", error.Message);
            // The wrong Burning Empires in a download is fixed by downloading it again, so Nexus is offered.
            Assert.True(error.DownloadHelps);
            Assert.False(Directory.Exists(Path.Combine(game, "Modules", "BurningEmpires")));
            Assert.False(Directory.Exists(Path.Combine(game, "Modules", "BurningEmpires_Elephants")));
            Assert.Empty(Directory.EnumerateFileSystemEntries(Path.Combine(root, "scratch")));
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void TheMainFileAloneIsRefusedAndTheInstalledCopyStaysAsItWas()
    {
        var root = NewRoot("overlay-mainonly");
        try
        {
            var game = Path.Combine(root, "game");
            var installed = Write(Path.Combine(game, "Modules", "BurningEmpires", "bin", "Win64_Shipping_Client", "BurningEmpires.dll"), "installed");

            var error = Assert.Throws<BeArchiveException>(() => Install([MainZip(Path.Combine(root, "Downloads"))], game, Path.Combine(root, "scratch")));

            Assert.Contains("hotfix", error.Message);
            Assert.Equal("installed", File.ReadAllText(installed));
            Assert.False(File.Exists(Path.Combine(game, "Modules", "BurningEmpires", "ModuleData", "settlements.xml")));
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void TheHotfixAloneIsRefusedWhenThereIsNoEarlierBurningEmpiresUnderIt()
    {
        var root = NewRoot("overlay-hotfixonly");
        try
        {
            var game = Path.Combine(root, "game");
            var scratch = Path.Combine(root, "scratch");
            var hotfix = HotfixZip(Path.Combine(root, "Downloads"));

            // Before, the hotfix's SubModule.xml and two DLLs went in alone and showed as v2.0.2.
            var error = Assert.Throws<BeArchiveException>(() => Install([hotfix], game, scratch));

            Assert.Equal(BeArchive.MainFileMissing, error.Message);
            Assert.False(Directory.Exists(Path.Combine(game, "Modules", "BurningEmpires")));
            Assert.False(Directory.Exists(scratch) && Directory.EnumerateFileSystemEntries(scratch).Any());

            // A 2.0.1 install without Elephants or ModuleData still needs the main file.
            Module(Path.Combine(game, "Modules"), "BurningEmpires", "BurningEmpires", "v2.0.1");
            Assert.True(BeArchive.NeedsMainFile(game));
            EarlierInstall(game);
            Assert.False(BeArchive.NeedsMainFile(game));
            Assert.Null(BeArchive.SetProblem([hotfix], game));
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void AMainFileWithoutTheHotfixIsRefusedBeforeItIsUnpacked()
    {
        var root = NewRoot("overlay-setproblem");
        try
        {
            var game = Path.Combine(root, "game");
            var downloads = Path.Combine(root, "Downloads");

            // A .7z is never unpacked to find out, so this one is not even a real archive.
            var main7z = Write(Path.Combine(downloads, "BurningEmpires 2.0 (For Bannerlord v1.4.8) 7750 2.0.1 2026-09-01T21-56Z.7z"), "not read");
            Assert.Equal(BeArchive.HotfixMissing, BeArchive.SetProblem([main7z], game));
            Assert.Equal(BeArchive.HotfixMissing, Assert.Throws<BeArchiveException>(() => Install([main7z], game, Path.Combine(root, "scratch"))).Message);
            Assert.Null(BeArchive.SetProblem([main7z, HotfixZip(downloads)], game));

            // A main file that already is 2.0.2, by its name or by its DLL, needs no hotfix.
            var named = Write(Path.Combine(downloads, "BurningEmpires 2.0.2 7750 2-0-2.7z"), "not read");
            Assert.Null(BeArchive.SetProblem([named], game));
            var fixedZip = Zip(Path.Combine(downloads, "BurningEmpires full.zip"),
                ("BurningEmpires/SubModule.xml", BeXml),
                ("BurningEmpires/bin/Win64_Shipping_Client/BurningEmpires.dll", HotfixDll));
            Assert.Null(BeArchive.SetProblem([fixedZip], game, Sha(HotfixDll)));
            Assert.Equal(BeArchive.HotfixMissing, BeArchive.SetProblem([MainZip(downloads)], game, Sha(HotfixDll)));
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void ADllThatIsNotOnTheKnownListStopsTheInstall()
    {
        var root = NewRoot("overlay-extra-dll");
        try
        {
            var game = Path.Combine(root, "game");
            var downloads = Path.Combine(root, "Downloads");
            var hotfix = HotfixZip(downloads);
            var evil = Zip(Path.Combine(downloads, "BurningEmpires 2.0 main.zip"),
                ("BurningEmpires/SubModule.xml", BeXml),
                ("BurningEmpires/bin/Win64_Shipping_Client/Evil.dll", "evil"),
                ("BurningEmpires_Elephants/SubModule.xml", SubModuleXml("BurningEmpires_Elephants", "v1.1.0")));

            var error = Assert.Throws<BeArchiveException>(() => Install([evil, hotfix], game, Path.Combine(root, "scratch")));
            Assert.Contains("Evil.dll", error.Message);
            Assert.False(Directory.Exists(Path.Combine(game, "Modules", "BurningEmpires")));

            // Elephants is data only, so any DLL in it is refused too.
            var elephantsDll = Zip(Path.Combine(downloads, "BurningEmpires 2.0 elephants.zip"),
                ("BurningEmpires_Elephants/SubModule.xml", SubModuleXml("BurningEmpires_Elephants", "v1.1.0")),
                ("BurningEmpires_Elephants/bin/Win64_Shipping_Client/Elephants.dll", "x"));
            Assert.Throws<BeArchiveException>(() => Install([elephantsDll, hotfix], game, Path.Combine(root, "scratch")));
            Assert.False(Directory.Exists(Path.Combine(game, "Modules", "BurningEmpires_Elephants")));
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void ASubModuleXmlThatLoadsAnotherDllStopsTheInstall()
    {
        var root = NewRoot("overlay-xml-dll");
        try
        {
            var game = Path.Combine(root, "game");
            var archive = Zip(Path.Combine(root, "BurningEmpires 2.0.2 Hotfix.zip"),
                ("BurningEmpires/SubModule.xml", SubModuleXmlWithDlls("BurningEmpires", "v2.0.2", "BurningEmpires.dll", "..\\..\\Coop\\bin\\Win64_Shipping_Client\\Coop.dll")),
                ("BurningEmpires/bin/Win64_Shipping_Client/BurningEmpires.dll", HotfixDll),
                ("BurningEmpires/bin/Win64_Shipping_Client/BurningEmpires.DistanceCacheProbe.dll", HotfixProbe));

            EarlierInstall(game);
            var error = Assert.Throws<BeArchiveException>(() => Install([archive], game, Path.Combine(root, "scratch")));

            Assert.Contains("Coop.dll", error.Message);
            Assert.Equal("v2.0.1", BeServerPath.VersionOf(Path.Combine(game, "Modules", "BurningEmpires")));
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void NotEnoughFreeSpaceIsRefusedBeforeAnythingIsUnpacked()
    {
        var root = NewRoot("overlay-space");
        try
        {
            var game = Path.Combine(root, "game");
            var downloads = Path.Combine(root, "Downloads");
            var scratch = Path.Combine(root, "scratch");

            var error = Assert.Throws<BeArchiveException>(() => Install([MainZip(downloads), HotfixZip(downloads)], game, scratch, free: _ => 1_000_000));

            Assert.Contains("Free up space", error.Message);
            // A new download does not free space, so the tools do not offer the Nexus page for it.
            Assert.False(error.DownloadHelps);
            Assert.Empty(Directory.EnumerateFileSystemEntries(scratch));
            Assert.False(Directory.Exists(Path.Combine(game, "Modules", "BurningEmpires")));
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void TheSpaceCheckCountsTwoCopiesOnOneDriveAndOneCopyPerDriveOtherwise()
    {
        const long unpacked = 6_000_000_000;
        var free = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase) { ["C:\\"] = 15_000_000_000, ["D:\\"] = 9_000_000_000 };

        // One drive needs 6 + 6 + 2 GB.
        Assert.Null(BeArchive.SpaceProblem(unpacked, "C:\\tool\\temp", "C:\\game\\Modules", drive => free[drive]));
        free["C:\\"] = 13_000_000_000;
        Assert.Contains("C:", BeArchive.SpaceProblem(unpacked, "C:\\tool\\temp", "C:\\game\\Modules", drive => free[drive]));

        // Two drives need 6 + 2 GB each.
        Assert.Null(BeArchive.SpaceProblem(unpacked, "C:\\tool\\temp", "D:\\game\\Modules", drive => free[drive]));
        free["D:\\"] = 7_000_000_000;
        Assert.Contains("D:", BeArchive.SpaceProblem(unpacked, "C:\\tool\\temp", "D:\\game\\Modules", drive => free[drive]));
    }

    [Fact]
    public void TarListingSizesAreAddedUpAndAnUnreadableLineIsRefused()
    {
        // Lines the way Windows' tar.exe -tvf prints the Burning Empires .7z and .rar.
        var listing =
            "-rw-r--r--  0 0      0      128000 Sep 03 16:40 BurningEmpires_2.0.2_Hotfix/BurningEmpires/bin/Win64_Shipping_Client/BurningEmpires.DistanceCacheProbe.dll\r\n"
            + "drwxr-xr-x  0 0      0           0 Aug 31 15:40 BurningEmpires/\r\n"
            + "-rw-r--r--  0 0      0  5000000000 Aug 23 16:56 BurningEmpires/AssetPackages/big file.tpac\r\n";
        Assert.Equal(5_000_128_000, BeArchive.TarListedBytes(listing, "x.7z"));

        Assert.Throws<BeArchiveException>(() => BeArchive.TarListedBytes("BurningEmpires/SubModule.xml\n", "x.7z"));
    }

    [Theory]
    [InlineData("en", "Sep 03 16:40", "Aug 23  2024")]
    [InlineData("de", "Sep 03 16:40", "Aug 23  2024")]
    [InlineData("fr", "sept. 03 16:40", "août 23  2024")]
    [InlineData("fr day first", "03 sept. 16:40", "23 août  2024")]
    [InlineData("ru", "сент. 03 16:40", "авг. 23  2024")]
    [InlineData("ru day first", "03 сент. 16:40", "23 авг.  2024")]
    [InlineData("ar", "سبتمبر 03 16:40", "أغسطس 23  2024")]
    [InlineData("tr", "Eyl 03 16:40", "Ağu 23  2024")]
    [InlineData("code page lost the month", "????? 03 16:40", "??? 23  2024")]
    public void TarListingSizesAreReadWhateverLanguageTheMonthIsIn(string locale, string recent, string older)
    {
        // Windows' tar.exe prints the month in the PC's regional format and ignores LC_ALL.
        var listing =
            "-rw-r--r--  0 0      0      128000 " + recent + " BurningEmpires_2.0.2_Hotfix/BurningEmpires/bin/Win64_Shipping_Client/BurningEmpires.DistanceCacheProbe.dll\r\n"
            + "drwxr-xr-x  0 0      0           0 " + recent + " BurningEmpires/\r\n"
            + "-rw-r--r--  0 0      0  5000000000 " + older + " BurningEmpires/AssetPackages/big file 2024.tpac\r\n";
        Assert.True(BeArchive.TarListedBytes(listing, "x.7z") == 5_000_128_000, locale);

        // Lines that are not a file with a size are still refused.
        Assert.Throws<BeArchiveException>(() => BeArchive.TarListedBytes("crw-r--r--  0 0      0  4,1 " + recent + " dev/null\n", "x.7z"));
        Assert.Throws<BeArchiveException>(() => BeArchive.TarListedBytes("tar: Error opening archive: Unrecognized archive format\n", "x.7z"));
        Assert.Throws<BeArchiveException>(() => BeArchive.TarListedBytes("-rw-r--r--  0 0      0      128000\n", "x.7z"));
    }

    [Fact]
    public void TheFoundDownloadsCarryTheirPathAndSizeForTheQuestion()
    {
        var root = NewRoot("describe");
        try
        {
            var hotfix = HotfixZip(root, "BurningEmpires_2.0.2_Hotfix.zip");
            var main = MainZip(root);

            var found = BeArchive.Describe([hotfix, main]);

            Assert.Equal([main, hotfix], found.Select(item => item.Path));
            Assert.Equal(new FileInfo(main).Length, found[0].Bytes);
            Assert.Equal(Path.GetFileName(main), found[0].Name);
            Assert.Equal(root, found[0].Folder);
            // File Explorer sizes, so the question and Downloads show the same number.
            Assert.Equal("5.2 GB", BeArchive.SizeText(5_583_457_484));
            Assert.Equal("12 MB", BeArchive.SizeText(12_000_000));
            Assert.Equal("1 MB", BeArchive.SizeText(12_000));
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void ZipSlipPathsAreRefusedAndNothingIsWritten()
    {
        var root = NewRoot("zip-slip");
        try
        {
            var game = Path.Combine(root, "game");
            var archive = Zip(Path.Combine(root, "BurningEmpires evil.zip"),
                ("BurningEmpires/SubModule.xml", SubModuleXml("BurningEmpires", "v2.0.1")),
                ("BurningEmpires/../../../escaped.txt", "bad"));

            var error = Assert.Throws<BeArchiveException>(() => Install([archive, HotfixZip(root)], game, Path.Combine(root, "scratch")));
            Assert.Contains("outside its folder", error.Message);
            Assert.False(File.Exists(Path.Combine(root, "escaped.txt")));
            Assert.False(Directory.Exists(Path.Combine(game, "Modules", "BurningEmpires")));

            Assert.True(BeArchive.UnsafeEntry("../x"));
            Assert.True(BeArchive.UnsafeEntry("a\\..\\..\\x"));
            Assert.True(BeArchive.UnsafeEntry("/etc/x"));
            Assert.True(BeArchive.UnsafeEntry("C:/Windows/x"));
            Assert.True(BeArchive.UnsafeEntry("\\\\server\\share\\x"));
            Assert.False(BeArchive.UnsafeEntry("BurningEmpires/..hidden/x"));
            Assert.False(BeArchive.UnsafeEntry("BurningEmpires/SubModule.xml"));
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void A7zOrRarGoesThroughWindowsTar()
    {
        var tar = BeArchive.TarPath();
        if (!File.Exists(tar))
        {
            output.WriteLine("Skipped: " + tar + " is not on this PC.");
            return;
        }

        var root = NewRoot("tar");
        try
        {
            // tar.exe reads the format from the content, so a zip named .rar stands in for the hotfix .rar.
            var game = Path.Combine(root, "game");
            var downloads = Path.Combine(root, "Downloads");
            var hotfix = HotfixZip(downloads, "BurningEmpires 2.0.2 Hotfix 7750 2.0.2.rar");
            Assert.True(BeArchive.UnpackedBytes(hotfix, tar) > 0);
            EarlierInstall(game);
            var lines = Install([hotfix], game, Path.Combine(root, "scratch"), tar);

            Assert.Single(lines);
            Assert.Equal(HotfixDll, File.ReadAllText(Path.Combine(game, "Modules", "BurningEmpires", "bin", "Win64_Shipping_Client", "BurningEmpires.dll")));

            var evil = Zip(Path.Combine(root, "BurningEmpires evil.7z"), ("../escaped.txt", "bad"));
            Assert.Throws<BeArchiveException>(() => BeArchive.Extract(evil, Path.Combine(root, "x"), tar));
            Assert.False(File.Exists(Path.Combine(root, "escaped.txt")));

            var broken = Write(Path.Combine(root, "BurningEmpires broken.7z"), "not an archive");
            var error = Assert.Throws<BeArchiveException>(() => BeArchive.Extract(broken, Path.Combine(root, "y"), tar));
            Assert.Contains("nexusmods.com", error.Message);
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void FindArchivesPicksTheNewestMainAndHotfixAndSkipsWarSails()
    {
        var root = NewRoot("find");
        try
        {
            var main = MainZip(root, "BurningEmpires 2.0 (For Bannerlord v1.4.8) 7750 2.0.1 2026-09-01T21-56Z Q6divpbZd.7z");
            var hotfix = HotfixZip(root, "BurningEmpires_2.0.2_Hotfix.rar");
            Write(Path.Combine(root, "BurningEmpiresWithNavalDLC 2.0.zip"), "naval");
            Write(Path.Combine(root, "SomethingElse.7z"), "x");
            Write(Path.Combine(root, "BurningEmpires notes.txt"), "x");

            Assert.Equal([main, hotfix], BeArchive.FindArchives([root, Path.Combine(root, "missing")]));
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void ContentAndCompatChecksSayWhatIsWrong()
    {
        var root = NewRoot("content");
        try
        {
            var game = Path.Combine(root, "game");
            Assert.Equal("Burning Empires is missing.", BeArchive.ContentProblem(game));
            Module(Path.Combine(game, "Modules"), "BurningEmpires", "BurningEmpires", "v2.0.1");
            Assert.Contains("v2.0.1", BeArchive.ContentProblem(game));
            Module(Path.Combine(game, "Modules"), "BurningEmpires", "BurningEmpires", "v2.0.2");
            Assert.Contains("hotfix", BeArchive.ContentProblem(game));
            Assert.DoesNotContain("Co-op", BeArchive.ContentProblem(game));

            var addOn = Module(Path.Combine(root, "ClientAddOns"), BeServerPath.CompatModuleId, BeServerPath.CompatModuleId, "v1.0.0");
            Assert.Contains("missing", BeArchive.CompatProblem(game, addOn));
            Module(Path.Combine(game, "Modules"), BeServerPath.CompatModuleId, BeServerPath.CompatModuleId, "v0.9.0");
            Write(Path.Combine(game, "Modules", BeServerPath.CompatModuleId, "stale.dll"), "old");
            Assert.Contains("v0.9.0", BeArchive.CompatProblem(game, addOn));
            Assert.Contains("v1.0.0", BeArchive.InstallCompat(addOn, game, Path.Combine(root, "temp")));
            Assert.Null(BeArchive.CompatProblem(game, addOn));
            Assert.False(File.Exists(Path.Combine(game, "Modules", BeServerPath.CompatModuleId, "stale.dll")));
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void TheCompatIsStagedOutsideModulesSoNoSecondCopyIsLeftThere()
    {
        var root = NewRoot("compat-staging");
        try
        {
            var game = Path.Combine(root, "game");
            var modules = Path.Combine(game, "Modules");
            var temp = Path.Combine(root, "temp");
            // A staging folder an older build left in Modules, which the game would load as a second copy.
            Module(modules, "." + BeServerPath.CompatModuleId + ".installing", BeServerPath.CompatModuleId, "v0.9.0");
            var addOn = Module(Path.Combine(root, "ClientAddOns"), BeServerPath.CompatModuleId, BeServerPath.CompatModuleId, "v1.0.0");
            Write(Path.Combine(addOn, "bin", "Win64_Shipping_Client", "BurningEmpires.CoopCompat.dll"), "compat");

            BeArchive.InstallCompat(addOn, game, temp);

            Assert.Equal([Path.Combine(modules, BeServerPath.CompatModuleId)], Directory.GetDirectories(modules));
            Assert.Equal("compat", File.ReadAllText(Path.Combine(modules, BeServerPath.CompatModuleId, "bin", "Win64_Shipping_Client", "BurningEmpires.CoopCompat.dll")));
            Assert.Empty(Directory.EnumerateFileSystemEntries(temp));
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void NoDownloadPointsToNexus()
    {
        var error = Assert.Throws<BeArchiveException>(() => BeArchive.InstallOverlay([], "C:\\game", "C:\\scratch"));
        Assert.Contains("nexusmods.com", error.Message);
        Assert.DoesNotContain("(", error.Message);
    }
}
