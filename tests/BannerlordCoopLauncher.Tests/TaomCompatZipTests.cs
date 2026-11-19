using System.IO.Enumeration;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using BannerlordCoopLauncher.Core.IO;
using BannerlordCoopLauncher.Core.Localization;
using BannerlordCoopLauncher.Core.Updates;
using TaomCoop.Core;

namespace BannerlordCoopLauncher.Tests;

// The TAOM tools carry their CompatOnly zip inside the exe (TaomCompatZip). A copy on this PC is used first,
// the carried copy is the fallback, and neither is used unless its SHA256 is the one TaomStack pins.
public class TaomCompatZipTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "taom-compat-zip-" + Guid.NewGuid().ToString("N"));

    public TaomCompatZipTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private string Zip(string name, string text)
    {
        var path = Path.Combine(_folder, name);
        File.WriteAllText(path, text);
        return path;
    }

    private static string Sha256Of(string text) => FileHash.Sha256(new MemoryStream(Encoding.UTF8.GetBytes(text)));

    [Fact]
    public void A_copy_on_this_pc_with_the_right_hash_wins_and_the_carried_copy_is_not_written_out()
    {
        var wrong = Zip("newest.zip", "an older build");
        var right = Zip("older.zip", "the r2 zip");

        var pick = TaomCompatZip.Choose([wrong, right], FileHash.TrySha256, Sha256Of("the r2 zip"),
            () => throw new InvalidOperationException("The carried copy must not be written out when this PC has one."));

        Assert.Equal(CompatZipFrom.ThisPc, pick.From);
        Assert.Equal(right, pick.Path);
    }

    [Fact]
    public void A_copy_on_this_pc_with_the_wrong_hash_is_skipped_for_the_carried_copy()
    {
        var wrong = Zip("downloads.zip", "an older build");
        var carried = Zip("carried.zip", "the r2 zip");

        var pick = TaomCompatZip.Choose([wrong], FileHash.TrySha256, Sha256Of("the r2 zip"), () => new CompatZipWrite(carried, null));

        Assert.Equal(CompatZipFrom.Embedded, pick.From);
        Assert.Equal(carried, pick.Path);
    }

    [Fact]
    public void Nothing_with_the_right_hash_is_missing()
    {
        var wrong = Zip("downloads.zip", "an older build");
        var carriedWrong = Zip("carried.zip", "a damaged copy");
        var expected = Sha256Of("the r2 zip");

        Assert.Equal(CompatZipPick.Missing, TaomCompatZip.Choose([wrong], FileHash.TrySha256, expected, () => new CompatZipWrite(carriedWrong, null)));
        Assert.Equal(CompatZipPick.Missing, TaomCompatZip.Choose([], FileHash.TrySha256, expected, () => null));
        // A file that is gone or locked hashes to "" and never counts as a match.
        Assert.Equal(CompatZipPick.Missing, TaomCompatZip.Choose([Path.Combine(_folder, "gone.zip")], FileHash.TrySha256, expected, () => null));
    }

    // A TAOM player's search stopped with "The file or directory is corrupted and unreadable" on a folder under
    // AppData\Local. The listing is lazy, so the damaged folder throws while it is read.
    private static IEnumerable<string> Damaged(string folder)
    {
        yield return Path.Combine(folder, "first entry");
        throw new IOException("The file or directory is corrupted and unreadable.");
    }

    [Fact]
    public void A_folder_that_cannot_be_read_is_skipped_and_a_zip_in_a_later_folder_is_still_found()
    {
        var damaged = Path.Combine(_folder, "AppData", "Local", "Damaged");
        var locked = Path.Combine(_folder, "AppData", "Local", "Locked");
        var games = Path.Combine(_folder, "Games", "mods");
        Directory.CreateDirectory(damaged);
        Directory.CreateDirectory(locked);
        Directory.CreateDirectory(games);
        var zip = Zip(Path.Combine("Games", "mods", "TAOM-Coop-v0.3.25-CompatOnly-CLIENT.zip"), "the zip");

        IEnumerable<string> Files(string folder, string pattern) =>
            folder == damaged ? Damaged(folder)
            : folder == locked ? throw new UnauthorizedAccessException("Access to the path is denied.")
            : Directory.EnumerateFiles(folder, pattern);
        IEnumerable<string> Folders(string folder) =>
            folder == damaged ? Damaged(folder) : Directory.EnumerateDirectories(folder);

        var found = PlaytestZipFinder.Matches("TAOM-Coop-v0.3.25-CompatOnly-CLIENT*.zip", [_folder], Files, Folders).ToList();

        Assert.Equal([zip], found);
    }

    [Fact]
    public void A_search_that_throws_counts_as_not_found_instead_of_showing_the_raw_error()
    {
        var carried = Zip("carried.zip", "the r2 zip");
        var expected = Sha256Of("the r2 zip");

        Assert.Equal(CompatZipPick.Missing, TaomCompatZip.Choose(Damaged(_folder), FileHash.TrySha256, expected, () => null));
        var pick = TaomCompatZip.Choose(Damaged(_folder), FileHash.TrySha256, expected, () => new CompatZipWrite(carried, null));
        Assert.Equal(CompatZipFrom.Embedded, pick.From);
        var failed = TaomCompatZip.Choose([], FileHash.TrySha256, expected, () => new CompatZipWrite(null, "Disk full."), Damaged(_folder));
        Assert.Equal(CompatZipFrom.WriteFailed, failed.From);
        Assert.Equal("Disk full.", failed.Problem);
    }

    [Fact]
    public void A_failed_write_out_searches_the_drives_and_keeps_the_reason_when_that_finds_nothing()
    {
        var expected = Sha256Of("the r2 zip");
        var onDrive = Zip("on-another-drive.zip", "the r2 zip");
        var failed = new CompatZipWrite(null, "There is not enough space on the disk.");

        var found = TaomCompatZip.Choose([], FileHash.TrySha256, expected, () => failed, [Zip("old.zip", "an older build"), onDrive]);
        Assert.Equal(new CompatZipPick(CompatZipFrom.ThisPc, onDrive), found);

        var none = TaomCompatZip.Choose([], FileHash.TrySha256, expected, () => failed, [Zip("old2.zip", "an older build")]);
        Assert.Equal(new CompatZipPick(CompatZipFrom.WriteFailed, null, "There is not enough space on the disk."), none);
        Assert.Equal(CompatZipFrom.WriteFailed, TaomCompatZip.Choose([], FileHash.TrySha256, expected, () => failed).From);

        // The drives are only searched after a failed write, not when the carried copy was written fine.
        var written = Zip("written.zip", "the r2 zip");
        Assert.Equal(CompatZipFrom.Embedded, TaomCompatZip.Choose([], FileHash.TrySha256, expected, () => new CompatZipWrite(written, null),
            ThrowWhenSearched()).From);
    }

    private static IEnumerable<string> ThrowWhenSearched()
    {
        throw new InvalidOperationException("The drives must not be searched.");
#pragma warning disable CS0162 // Makes this an iterator, so it only throws when enumerated.
        yield break;
#pragma warning restore CS0162
    }

    [Fact]
    public void The_carried_copy_is_written_out_only_with_the_pinned_hash()
    {
        var kit = new TaomCompatKit("kit-r2.zip", Sha256Of("the r2 zip"), "kit*.zip");
        var target = Path.Combine(_folder, "out", kit.Name);

        Assert.Null(TaomCompatZip.WriteOut(kit, () => new MemoryStream(Encoding.UTF8.GetBytes("a damaged copy")), Path.Combine(_folder, "out")));
        Assert.False(File.Exists(target));
        Assert.Empty(Directory.GetFiles(Path.Combine(_folder, "out")));

        Assert.Equal(new CompatZipWrite(target, null), TaomCompatZip.WriteOut(kit, () => new MemoryStream(Encoding.UTF8.GetBytes("the r2 zip")), Path.Combine(_folder, "out")));
        Assert.Equal(kit.Sha256, FileHash.Sha256(target), ignoreCase: true);
        // A good copy already there is used as it is.
        Assert.Equal(new CompatZipWrite(target, null), TaomCompatZip.WriteOut(kit, () => throw new InvalidOperationException("Already written out."), Path.Combine(_folder, "out")));
        // An exe that carries no copy writes nothing.
        Assert.Null(TaomCompatZip.WriteOut(kit with { Name = "other.zip" }, () => null, Path.Combine(_folder, "out")));
    }

    [Fact]
    public void A_wrong_copy_already_at_the_target_is_replaced()
    {
        var kit = new TaomCompatKit("kit-r2.zip", Sha256Of("the r2 zip"), "kit*.zip");
        var folder = Path.Combine(_folder, "out");
        Directory.CreateDirectory(folder);
        var target = Path.Combine(folder, kit.Name);
        File.WriteAllText(target, "a damaged copy");

        Assert.Equal(new CompatZipWrite(target, null), TaomCompatZip.WriteOut(kit, () => new MemoryStream(Encoding.UTF8.GetBytes("the r2 zip")), folder));
        Assert.Equal("the r2 zip", File.ReadAllText(target));
        Assert.Single(Directory.GetFiles(folder));
    }

    [Fact]
    public void A_write_out_that_fails_gives_the_reason()
    {
        var kit = new TaomCompatKit("kit-r2.zip", Sha256Of("the r2 zip"), "kit*.zip");
        // The CompatOnly folder path is taken by a file, so the folder cannot be made.
        var blocked = Zip("CompatOnly", "not a folder");

        var write = TaomCompatZip.WriteOut(kit, () => new MemoryStream(Encoding.UTF8.GetBytes("the r2 zip")), blocked);

        Assert.NotNull(write);
        Assert.Null(write.Path);
        Assert.False(string.IsNullOrWhiteSpace(write.Problem));
        Assert.Equal(CompatZipFrom.WriteFailed, TaomCompatZip.Choose([], FileHash.TrySha256, kit.Sha256, () => write).From);
    }

    [Fact]
    public void Carried_zips_are_the_release_files_TaomStack_pins()
    {
        // Make-Release.ps1 embeds the kept release file checked against release-assets.json, and
        // src\TaomCompatZip.targets checks it again against the csproj, so all three must match TaomStack.
        using var json = JsonDocument.Parse(SourceFiles.Read("tools/release-assets.json"));
        var assets = json.RootElement.GetProperty("repos").EnumerateArray()
            .Single(repo => repo.GetProperty("repo").GetString() == "Coodsz/Coodsz-TAOM")
            .GetProperty("assets").EnumerateArray()
            .ToDictionary(asset => asset.GetProperty("name").GetString()!, asset => asset.TryGetProperty("sha256", out var sha) ? sha.GetString() : null);

        foreach (var (kit, project) in new[] { (TaomCompatKit.Client, "src/TaomCoopLauncher/TaomCoopLauncher.csproj"), (TaomCompatKit.Host, "src/TaomCoopHost/TaomCoopHost.csproj") })
        {
            Assert.True(assets.TryGetValue(kit.Name, out var sha), kit.Name + " is not a TAOM release asset.");
            Assert.Equal(kit.Sha256, sha, ignoreCase: true);
            Assert.True(FileSystemName.MatchesSimpleExpression(kit.Pattern, kit.Name), kit.Name + " does not match " + kit.Pattern);

            var csproj = SourceFiles.Read(project);
            Assert.Equal(kit.Name, Regex.Match(csproj, "<CoodszTaomCompatName>([^<]+)<").Groups[1].Value);
            Assert.Equal(kit.Sha256, Regex.Match(csproj, "<CoodszTaomCompatSha256>([^<]+)<").Groups[1].Value, ignoreCase: true);
            Assert.Contains("<Import Project=\"..\\TaomCompatZip.targets\" />", csproj);
        }

        var targets = SourceFiles.Read("src/TaomCompatZip.targets");
        Assert.Contains("LogicalName=\"" + TaomCompatZip.ResourcePrefix + "$(CoodszTaomCompatName)\"", targets);
        Assert.Contains("'$(PublishSingleFile)' == 'true'", targets);
        Assert.Contains("GetFileHash", targets);
    }

    [Fact]
    public void The_whats_new_line_and_the_write_out_messages_are_translated()
    {
        string[] lines =
        [
            WhatsNew.TaomCompatBuiltIn,
            "Could not write the built in {0} to {1}. {2}",
            "The built in copy of {0} could not be written to {1}. Check that the drive has free space and the folder isn't blocked, then try again.",
            "{0} The built in copy of {1} could not be written to {2}. Check that the drive has free space and the folder isn't blocked, then try again.",
            "{0} The {1} was not found on this PC. Put it in Downloads.",
        ];
        foreach (var language in Translator.Languages.Where(language => language.Code != Translator.English))
        {
            var table = Translator.Entries(language.Code);
            Assert.All(lines, line => Assert.True(table.ContainsKey(line), language.Code + " has no translation for: " + line));
        }
    }

    [Fact]
    public void Guides_and_the_release_page_say_the_zips_are_built_in()
    {
        var client = SourceFiles.Read("src/BannerlordCoopLauncher.Core/Keep/Guides/TaomClient/1-getting-started.json");
        Assert.Contains("CLIENT zip is built into the launcher", client);
        Assert.DoesNotContain("both zips", client);
        Assert.Contains("HOST-OPERATOR zip is built into the server tool", SourceFiles.Read("src/BannerlordCoopLauncher.Core/Keep/Guides/TaomServer/1-getting-started.json"));
        foreach (var guide in new[] { "Coodsz-TAOM-Instructions.txt", "README.txt" })
            Assert.Contains("You don't need to download the CompatOnly zips anymore.", SourceFiles.Read(guide));
        Assert.DoesNotContain("Players still need the TAOM-Coop CompatOnly", SourceFiles.Read("tools/release-assets.json"));
    }

    [Fact]
    public void Make_release_passes_each_tool_its_zip()
    {
        var script = SourceFiles.Read("tools/Make-Release.ps1");
        Assert.Contains("\"-p:CoodszTaomCompatZip=$($taomCompatZip['HOST-OPERATOR'])\"", script);
        Assert.Contains("\"-p:CoodszTaomCompatZip=$($taomCompatZip['CLIENT'])\"", script);
        Assert.Contains("Copy-KeptReleaseAsset $asset[0] 'Coodsz/Coodsz-TAOM' $AssetCache $taomCompat", script);
    }

    [Fact]
    public void Both_tools_find_their_zip_through_the_hash_check_and_keep_the_not_found_message()
    {
        var client = SourceFiles.Read("src/TaomCoopLauncher/MainWindow.xaml.cs");
        Assert.Contains("TaomCompatZip.Find(TaomCompatKit.Client, wholePc", client);
        Assert.DoesNotContain("PlaytestZipFinder.NewestOnThisPc(TaomStack.ClientKitPattern", client);
        Assert.Contains("TaomStack.ClientKitName + \" was not found on this PC. Put it in Downloads.\");", client);
        Assert.DoesNotContain("ClientKitName + \" zip was not found", client);
        // A carried copy that could not be written out says so (twice, Install and Repair) and logs the reason first.
        Assert.Equal(2, Regex.Matches(client, Regex.Escape("compatPick.From == CompatZipFrom.WriteFailed")).Count);
        Assert.Equal(2, Regex.Matches(client, Regex.Escape("could not be written to \" + TaomCompatZip.Folder() + \". Check that the drive has free space and the folder isn't blocked, then try again.\");")).Count);
        Assert.Equal(2, Regex.Matches(client, Regex.Escape("progress.Report(\"Could not write the built in \" + TaomStack.ClientKitName + \" to \" + TaomCompatZip.Folder() + \". \" + compatPick.Problem);")).Count);

        var host = SourceFiles.Read("src/TaomCoopHost/MainWindow.ServerControl.cs");
        Assert.Contains("TaomCompatZip.Find(TaomCompatKit.Host, wholePc: true)", host);
        Assert.DoesNotContain("PlaytestZipFinder.NewestOnThisPc(PlaytestZipFinder.HostOperatorPattern)", host);
        Assert.Contains("CompatOnly-HOST-OPERATOR zip was not found on this PC. Put it in Downloads.\");", host);
        Assert.Contains("if (zip.From == CompatZipFrom.WriteFailed)", host);
        Assert.Contains("log(\"Could not write the built in \" + TaomStack.HostKitName", host);
        Assert.Contains("ExtractHostKit(FindHostKitZip(log))", host);

        // After a failed write out the drives are searched like in a build without the zip.
        Assert.Contains("wholePc && carries ? PlaytestZipFinder.Matches(kit.Pattern, PlaytestZipFinder.FixedDrives()) : null", SourceFiles.Read("src/TaomCoop.Core/TaomCompatZip.cs"));
    }
}
