namespace BannerlordCoopLauncher.Tests;

// Reads program source for the tests that check how a window is wired.
// MainWindow is split over MainWindow.xaml.cs and MainWindow.<Area>.cs files.
internal static class SourceFiles
{
    public static string Window(string repoRoot, string project)
    {
        var folder = Path.Combine(repoRoot, "src", project);
        var files = new[] { Path.Combine(folder, "MainWindow.xaml.cs") }
            .Concat(Directory.GetFiles(folder, "MainWindow.*.cs").Where(path => !path.EndsWith(".xaml.cs", StringComparison.OrdinalIgnoreCase)).OrderBy(path => path, StringComparer.Ordinal));
        return string.Join("\n", files.Select(File.ReadAllText));
    }

    // One file of the repository, by its path from the repository root.
    public static string Read(string relativePath) => File.ReadAllText(Path.Combine(Root(), relativePath));

    public static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "BannerlordCoopLauncher.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    // The private folder the build takes the files that are not in the public source from, the same way
    // src\Directory.Build.props finds it (CONTRIBUTING.md, Private files). A contributor's checkout has none.
    public static string PrivateRoot() =>
        Environment.GetEnvironmentVariable("CoodszPrivateRoot") is { Length: > 0 } root ? Path.GetFullPath(root) : Path.Combine(Root(), "private");

    // The text of one method, from its signature to its closing brace.
    public static string Method(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, signature + " was not found.");
        var open = source.IndexOf('{', start);
        var depth = 0;
        for (var i = open; i < source.Length; i++)
        {
            if (source[i] == '{')
                depth++;
            else if (source[i] == '}' && --depth == 0)
                return source[start..(i + 1)];
        }

        throw new InvalidOperationException(signature + " has no closing brace.");
    }
}
