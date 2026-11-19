using BannerlordCoopLauncher.Core.IO;

namespace BannerlordCoopLauncher.Tests;

internal sealed class MemoryFileSystem : IFileSystem
{
    private readonly Dictionary<string, string> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _directories = new(StringComparer.OrdinalIgnoreCase);

    public void AddFile(string path, string contents)
    {
        var full = Normalize(path);
        _files[full] = contents;
        AddParents(full);
    }

    public void AddDirectory(string path)
    {
        var full = Normalize(path);
        _directories.Add(full);
        AddParents(full);
    }

    private void AddParents(string full)
    {
        var directory = Path.GetDirectoryName(full);
        while (!string.IsNullOrEmpty(directory))
        {
            _directories.Add(Normalize(directory));
            directory = Path.GetDirectoryName(directory);
        }
    }

    public bool FileExists(string path) => _files.ContainsKey(Normalize(path));

    public bool DirectoryExists(string path) => _directories.Contains(Normalize(path));

    public string? ReadAllText(string path) =>
        _files.TryGetValue(Normalize(path), out var contents) ? contents : null;

    public IEnumerable<string> EnumerateDirectories(string directory)
    {
        var parent = Normalize(directory);
        if (!_directories.Contains(parent))
            return Array.Empty<string>();

        return _directories
            .Where(candidate => string.Equals(Path.GetDirectoryName(candidate), parent, StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    private static string Normalize(string path)
    {
        var value = path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        return value.TrimEnd(Path.DirectorySeparatorChar);
    }
}
