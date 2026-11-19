using System.Text.Json;
using BannerlordCoopLauncher.Core.Steam;

namespace BannerlordCoopLauncher.Tests;

public class GameFolderPickTests
{
    [Fact]
    public void Valid_saved_path_wins_over_search()
    {
        var files = new MemoryFileSystem();
        const string root = @"C:\Games\Bannerlord";
        AddGame(files, root);
        var searched = false;

        var selected = GameFolderPick.ResolveSavedPath(root, () => { searched = true; return null; }, files);

        Assert.Equal(Path.GetFullPath(root), selected);
        Assert.False(searched);
    }

    [Fact]
    public void Invalid_saved_path_falls_back_to_search()
    {
        var called = false;

        var selected = GameFolderPick.ResolveSavedPath(@"C:\OldGame", () => { called = true; return @"D:\Steam\Bannerlord"; }, new MemoryFileSystem());

        Assert.Equal(@"D:\Steam\Bannerlord", selected);
        Assert.True(called);
    }

    [Fact]
    public void Root_shipping_folder_and_executable_all_normalize_to_the_root()
    {
        var files = new MemoryFileSystem();
        const string root = @"C:\Games\Bannerlord";
        AddGame(files, root);

        AssertOk(root, root, files);
        AssertOk(Path.Combine(root, "bin", "Win64_Shipping_Client"), root, files);
        AssertOk(Path.Combine(root, "bin", "Win64_Shipping_Client", "Bannerlord.exe"), root, files);
    }

    [Fact]
    public void Epic_path_and_markers_are_detected_after_a_game_root_is_found()
    {
        var epicPathFiles = new MemoryFileSystem();
        const string epicRoot = @"C:\Epic Games\Bannerlord";
        AddGame(epicPathFiles, epicRoot);
        AssertEpic(GameFolderPick.Check(epicRoot, epicPathFiles));

        var rootMarkerFiles = new MemoryFileSystem();
        const string rootMarkerRoot = @"C:\Games\Bannerlord";
        AddGame(rootMarkerFiles, rootMarkerRoot);
        rootMarkerFiles.AddFile(Path.Combine(rootMarkerRoot, ".egstore"), "");
        AssertEpic(GameFolderPick.Check(rootMarkerRoot, rootMarkerFiles));

        var binMarkerFiles = new MemoryFileSystem();
        const string binMarkerRoot = @"C:\Games\Bannerlord";
        AddGame(binMarkerFiles, binMarkerRoot);
        binMarkerFiles.AddDirectory(Path.Combine(binMarkerRoot, "bin", ".egstore"));
        AssertEpic(GameFolderPick.Check(binMarkerRoot, binMarkerFiles));
    }

    [Fact]
    public void Steam_signals_override_epic_signals()
    {
        var steamPathFiles = new MemoryFileSystem();
        const string steamRoot = @"D:\Epic Games\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord";
        AddGame(steamPathFiles, steamRoot);
        AssertOk(steamRoot, steamRoot, steamPathFiles);

        foreach (var markerPath in new[] { "", Path.Combine("bin", "Win64_Shipping_Client") })
        {
            var files = new MemoryFileSystem();
            const string root = @"C:\Games\Bannerlord";
            AddGame(files, root);
            files.AddFile(Path.Combine(root, ".egstore"), "");
            files.AddFile(Path.Combine(root, markerPath, "steam_appid.txt"), "261550");
            AssertOk(root, root, files);
        }

        var ancestorMarkerFiles = new MemoryFileSystem();
        const string ancestorRoot = @"C:\Games\Bannerlord";
        AddGame(ancestorMarkerFiles, ancestorRoot);
        ancestorMarkerFiles.AddDirectory(@"C:\Games\.egstore");
        AssertOk(ancestorRoot, ancestorRoot, ancestorMarkerFiles);
    }

    [Fact]
    public void Epic_folder_without_a_game_root_is_not_bannerlord()
    {
        var files = new MemoryFileSystem();
        files.AddDirectory(@"C:\Epic Games\Fortnite");
        AssertNotBannerlord(GameFolderPick.Check(@"C:\Epic Games\Fortnite", files));

        files.AddDirectory(@"C:\Games\Unrelated");
        files.AddFile(@"C:\Games\Unrelated\.egstore", "");
        AssertNotBannerlord(GameFolderPick.Check(@"C:\Games\Unrelated", files));
    }

    [Fact]
    public void Executable_without_modules_is_not_a_game_root()
    {
        var files = new MemoryFileSystem();
        const string root = @"C:\Games\Bannerlord";
        files.AddFile(Path.Combine(root, "bin", "Win64_Shipping_Client", "Bannerlord.exe"), "");
        AssertNotBannerlord(GameFolderPick.Check(root, files));
    }

    [Fact]
    public void Trimming_quotes_whitespace_and_forward_slashes_is_supported()
    {
        var files = new MemoryFileSystem();
        const string root = @"C:\Games\Bannerlord";
        AddGame(files, root);

        AssertOk(root + "\\", root, files);
        AssertOk("  " + root + "  ", root, files);
        AssertOk('"' + root + '"', root, files);
        AssertOk(root.Replace('\\', '/') + "/", root, files);
    }

    [Fact]
    public void Search_walks_at_most_three_parents_from_the_picked_folder()
    {
        var files = new MemoryFileSystem();
        const string root = @"C:\Games\Bannerlord";
        AddGame(files, root);
        AssertOk(Path.Combine(root, "a", "b", "c"), root, files);
        AssertNotBannerlord(GameFolderPick.Check(Path.Combine(root, "a", "b", "c", "d"), files));
    }

    [Fact]
    public void Null_blank_and_invalid_paths_are_not_bannerlord()
    {
        AssertNotBannerlord(GameFolderPick.Check(null, new MemoryFileSystem()));
        AssertNotBannerlord(GameFolderPick.Check("  ", new MemoryFileSystem()));
        AssertNotBannerlord(GameFolderPick.Check("\0", new MemoryFileSystem()));
    }

    [Fact]
    public void Epic_and_wrong_folders_never_reach_the_save()
    {
        var files = new MemoryFileSystem();
        AddGame(files, @"C:\Epic Games\Bannerlord");
        files.AddDirectory(@"C:\Games\Unrelated");
        var saved = new List<string>();

        var epic = GameFolderPick.Use(@"C:\Epic Games\Bannerlord", saved.Add, files);
        var wrong = GameFolderPick.Use(@"C:\Games\Unrelated", saved.Add, files);

        Assert.Equal(GameFolderPickResult.Epic, epic.Result);
        Assert.Equal(GameFolderPickResult.NotBannerlord, wrong.Result);
        Assert.Empty(saved);
    }

    [Fact]
    public void A_good_pick_is_saved_once_as_the_game_root()
    {
        var files = new MemoryFileSystem();
        const string root = @"D:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord";
        AddGame(files, root);
        var saved = new List<string>();

        var check = GameFolderPick.Use(Path.Combine(root, "bin", "Win64_Shipping_Client"), saved.Add, files);

        Assert.Equal(GameFolderPickResult.Ok, check.Result);
        Assert.Equal(new[] { Path.GetFullPath(root) }, saved);
    }

    [Fact]
    public void Dialog_opens_at_the_saved_folder_then_steam_common_then_the_usual_steam_folder()
    {
        var files = new MemoryFileSystem();
        files.AddDirectory(@"E:\Games\Bannerlord");
        files.AddDirectory(@"D:\Steam\steamapps\common");
        Assert.Equal(@"E:\Games\Bannerlord", GameFolderPick.DialogStartFolder(@"E:\Games\Bannerlord", @"D:\Steam", files));
        Assert.Equal(@"D:\Steam\steamapps\common", GameFolderPick.DialogStartFolder(@"E:\Gone", @"D:\Steam", files));
        Assert.Equal(@"D:\Steam\steamapps\common", GameFolderPick.DialogStartFolder("", @"D:\Steam", files));

        var noSteam = new MemoryFileSystem();
        Assert.Equal("", GameFolderPick.DialogStartFolder(null, null, noSteam));
        noSteam.AddDirectory(GameFolderPick.DefaultSteamFolder);
        Assert.Equal(GameFolderPick.DefaultSteamFolder, GameFolderPick.DialogStartFolder(null, @"D:\NoSteamHere", noSteam));
    }

    [Fact]
    public void Picker_messages_use_the_plain_wording()
    {
        Assert.Equal("This folder is not Bannerlord. Pick the folder named Mount & Blade II Bannerlord. It is usually in Steam, then steamapps, then common.", GameFolderPick.NotBannerlordMessage);
        Assert.Equal("This looks like the Epic Games version of Bannerlord. The Coodsz tools need the Steam version. Install Bannerlord from Steam to use them.", GameFolderPick.EpicMessage);
        Assert.Equal("Steam was not found on this PC.", GameSearch.SteamMissingReason);
        Assert.Equal("Steam's library list could not be read. Pick the Bannerlord folder yourself.", GameSearch.LibraryListUnreadableReason);
        Assert.Equal("Bannerlord was not found. Press Find Bannerlord and pick its folder.", GameFolderPick.NotFoundText);
    }

    [Theory]
    [InlineData("ar")]
    [InlineData("de")]
    [InlineData("fr")]
    [InlineData("pt")]
    [InlineData("ru")]
    [InlineData("tr")]
    public void Every_language_contains_the_new_search_and_picker_strings(string language)
    {
        var file = Path.Combine(RepoRoot(), "src", "BannerlordCoopLauncher.Core", "Localization", "Languages", language + ".json");
        var strings = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(file))!;
        foreach (var text in new[]
                 {
                     GameSearch.SteamMissingReason, GameSearch.LibraryListUnreadableReason, GameSearch.GameMissingReason,
                     GameFolderPick.EpicMessage, GameFolderPick.NotBannerlordMessage, GameFolderPick.NotFoundText,
                 })
            Assert.False(string.IsNullOrWhiteSpace(strings.GetValueOrDefault(text)), language + " is missing: " + text);
    }

    private static void AssertOk(string picked, string root, MemoryFileSystem files)
    {
        var result = GameFolderPick.Check(picked, files);
        Assert.Equal(GameFolderPickResult.Ok, result.Result);
        Assert.Equal(Path.GetFullPath(root), result.Root);
        Assert.Equal("", result.Message);
    }

    private static void AssertEpic(GameFolderCheck result)
    {
        Assert.Equal(GameFolderPickResult.Epic, result.Result);
        Assert.Null(result.Root);
        Assert.Equal(GameFolderPick.EpicMessage, result.Message);
    }

    private static void AssertNotBannerlord(GameFolderCheck result)
    {
        Assert.Equal(GameFolderPickResult.NotBannerlord, result.Result);
        Assert.Null(result.Root);
        Assert.Equal(GameFolderPick.NotBannerlordMessage, result.Message);
    }

    internal static void AddGame(MemoryFileSystem files, string root)
    {
        files.AddFile(GameRoot.ExecutablePath(root), "");
        files.AddDirectory(Path.Combine(root, "Modules"));
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "src", "BannerlordCoopLauncher.Core")))
            dir = dir.Parent;
        return dir!.FullName;
    }
}
