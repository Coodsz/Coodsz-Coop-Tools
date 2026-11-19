using System.Diagnostics;
using System.IO;
using BannerlordCoopLauncher.Core.IO;
using BannerlordCoopLauncher.Core.Keep;
using BannerlordCoopLauncher.Core.Steam;
using TaomCoop.Core;

namespace CoodszHost.Services;

/// <summary>
/// "Build pictures": starts the player's own Bannerlord once with the Coodsz Image Exporter module
/// (compat\CoodszImageExporter), which saves the item, troop and lord pictures the game draws into
/// %AppData%\Coodsz\images. The pictures belong to TaleWorlds; they are never shipped or downloaded,
/// only drawn on this PC by the installed game. The module folder is removed from the game again
/// when the game closes.
/// </summary>
public static class PictureBuilder
{
    public const string ModuleId = "CoodszImageExporter";
    public const string FolderName = "ImageExporter";
    public const string Name = "Coodsz Image Exporter";

    public static GameImageStore Store { get; } = new(GameImageStore.DefaultRoot);
    public static Func<string?>? SavedGamePath { get; set; }
    public static Action<string>? SearchFailureLog { get; set; }
    private static bool _searchFailureLogged;

    public static string PayloadRoot(string? baseDirectory = null) =>
        baseDirectory == null ? Path.Combine(ToolPayload.Folder(FolderName), ModuleId) : Path.Combine(baseDirectory, FolderName, ModuleId);

    /// <summary>The game launch arguments: a plain single player game with only the exporter added.</summary>
    public static string Arguments() => "/singleplayer _MODULES_*Native*SandBoxCore*Sandbox*" + ModuleId + "*_MODULES_";

    /// <summary>Why the pictures cannot be built now, or null.</summary>
    public static string? Problem(string? gameRoot, string? payloadRoot = null)
    {
        var shown = payloadRoot ?? ToolPayload.Expected(FolderName, ModuleId);
        payloadRoot ??= PayloadRoot();
        if (!File.Exists(Path.Combine(payloadRoot, "SubModule.xml")))
            return ToolPayload.MissingText(Name, shown);
        if (string.IsNullOrWhiteSpace(gameRoot) || !File.Exists(GameRoot.ExecutablePath(gameRoot)))
            return "Bannerlord was not found in your Steam libraries, so the pictures cannot be drawn.";
        if (Process.GetProcessesByName("Bannerlord").Length > 0 || Process.GetProcessesByName("Bannerlord.Native").Length > 0)
            return "Bannerlord is open. Close the game first; Build pictures starts it by itself.";
        return null;
    }

    /// <summary>The installed game's folder (the one with bin and Modules), or null.</summary>
    public static string? FindGame()
        => FindGame(SavedGamePath?.Invoke(), SearchGame);

    private static string? SearchGame()
    {
        var search = BannerlordFinder.FindGameWithReason();
        if (search.Root == null && !_searchFailureLogged)
        {
            SearchFailureLog?.Invoke(search.Reason ?? GameSearch.GameMissingReason);
            _searchFailureLogged = true;
        }
        return search.Root;
    }

    public static string? FindGame(string? savedPath, Func<string?> search, IFileSystem? files = null) =>
        GameFolderPick.ResolveSavedPath(savedPath, search, files);

    /// <summary>
    /// Copies the exporter into the game's Modules folder and starts the game with it. With
    /// <paramref name="redraw"/> the old pictures are drawn again (for example after adding mods);
    /// otherwise only missing ones are drawn.
    /// </summary>
    public static Process Start(string gameRoot, bool redraw, string? payloadRoot = null)
    {
        payloadRoot ??= PayloadRoot();
        Directory.CreateDirectory(Store.Root);
        if (File.Exists(Store.DonePath))
            File.Delete(Store.DonePath);
        if (redraw)
        {
            foreach (var kind in new[] { GameImageKind.Item, GameImageKind.Troop, GameImageKind.Lord })
            {
                var folder = Path.Combine(Store.Root, GameImageStore.Folder(kind));
                if (Directory.Exists(folder))
                    Directory.Delete(folder, true);
            }
        }

        var target = ModuleFolder(gameRoot);
        foreach (var source in Directory.EnumerateFiles(payloadRoot, "*", SearchOption.AllDirectories))
        {
            var destination = Path.Combine(target, Path.GetRelativePath(payloadRoot, source));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(source, destination, overwrite: true);
        }

        var start = new ProcessStartInfo(GameRoot.ExecutablePath(gameRoot), Arguments())
        {
            WorkingDirectory = GameRoot.WorkingDirectory(gameRoot),
            UseShellExecute = false
        };
        start.Environment["COODSZ_EXPORT_AUTOSTART"] = "1";
        start.Environment["COODSZ_EXPORT_ROOT"] = Store.Root;
        return Process.Start(start) ?? throw new InvalidOperationException("Bannerlord did not start.");
    }

    public static string ModuleFolder(string gameRoot) => Path.Combine(gameRoot, "Modules", ModuleId);

    /// <summary>Takes the exporter out of the game again, so the game launcher never lists it.</summary>
    public static void Remove(string gameRoot)
    {
        try
        {
            var folder = ModuleFolder(gameRoot);
            if (Directory.Exists(folder))
                Directory.Delete(folder, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The game still holds the DLL; the next Build pictures replaces the folder anyway.
        }
    }
}
