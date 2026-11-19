using BannerlordCoopLauncher.Core.IO;
using BannerlordCoopLauncher.Core.Steam;
using TaomCoop.Core;

namespace BannerlordCoopLauncher.Tests;

public class BannerlordFinderReasonTests
{
    [Fact]
    public void Missing_steam_path_has_the_steam_missing_reason()
    {
        var result = BannerlordFinder.FindGameWithReason(null, new MemoryFileSystem());

        Assert.Null(result.Root);
        Assert.Equal(GameSearch.SteamMissingReason, result.Reason);
    }

    [Fact]
    public void Missing_game_has_the_game_missing_reason()
    {
        var result = BannerlordFinder.FindGameWithReason(@"C:\Steam", new MemoryFileSystem());

        Assert.Null(result.Root);
        Assert.Equal(GameSearch.GameMissingReason, result.Reason);
    }

    [Fact]
    public void Unreadable_library_list_has_the_library_list_reason()
    {
        var files = new ThrowingFileSystem(new MemoryFileSystem(), @"C:\Steam\steamapps\libraryfolders.vdf");
        var result = BannerlordFinder.FindGameWithReason(@"C:\Steam", files);

        Assert.Null(result.Root);
        Assert.Equal(GameSearch.LibraryListUnreadableReason, result.Reason);
    }

    [Fact]
    public void Failed_manifest_read_has_the_library_list_reason()
    {
        var files = new MemoryFileSystem();
        var manifest = Path.Combine(@"C:\Steam", "steamapps", "appmanifest_261550.acf");
        var throwingFiles = new ThrowingFileSystem(files, manifest);
        var result = BannerlordFinder.FindGameWithReason(@"C:\Steam", throwingFiles);

        Assert.Null(result.Root);
        Assert.Equal(GameSearch.LibraryListUnreadableReason, result.Reason);
    }

    [Fact]
    public void Corrupt_library_list_has_the_library_list_reason()
    {
        var files = new MemoryFileSystem();
        files.AddFile(@"C:\Steam\steamapps\libraryfolders.vdf", "\"libraryfolders\" { \"0\" { \"path\" ");

        var result = BannerlordFinder.FindGameWithReason(@"C:\Steam", files);

        Assert.Null(result.Root);
        Assert.Equal(GameSearch.LibraryListUnreadableReason, result.Reason);
    }

    [Fact]
    public void Corrupt_game_manifest_has_the_library_list_reason()
    {
        var files = new MemoryFileSystem();
        files.AddFile(@"C:\Steam\steamapps\appmanifest_261550.acf", "\"AppState\" { \"installdir\" \"Mount & Blade II Bannerlord\"");

        var result = BannerlordFinder.FindGameWithReason(@"C:\Steam", files);

        Assert.Null(result.Root);
        Assert.Equal(GameSearch.LibraryListUnreadableReason, result.Reason);
    }

    [Fact]
    public void Found_game_has_a_root_and_no_reason()
    {
        var files = new MemoryFileSystem();
        const string steam = @"C:\Steam";
        var root = Path.Combine(steam, "steamapps", "common", "Mount & Blade II Bannerlord");
        GameFolderPickTests.AddGame(files, root);

        var result = BannerlordFinder.FindGameWithReason(steam, files);

        Assert.Equal(Path.GetFullPath(root), result.Root);
        Assert.Null(result.Reason);
    }

    private sealed class ThrowingFileSystem : IFileSystem
    {
        private readonly IFileSystem _inner;
        private readonly string _throwPath;

        public ThrowingFileSystem(IFileSystem inner, string throwPath)
        {
            _inner = inner;
            _throwPath = throwPath;
        }

        public bool FileExists(string path) => _inner.FileExists(path);
        public bool DirectoryExists(string path) => _inner.DirectoryExists(path);

        public string? ReadAllText(string path)
        {
            if (string.Equals(path, _throwPath, StringComparison.OrdinalIgnoreCase))
                throw new IOException("Simulated read failure");
            return _inner.ReadAllText(path);
        }

        public IEnumerable<string> EnumerateDirectories(string directory) => _inner.EnumerateDirectories(directory);
    }
}
