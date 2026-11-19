using System.IO;
using System.Windows;
using BannerlordCoopLauncher.Core.IO;
using BannerlordCoopLauncher.Core.Support;
using Coodsz.Ui;
using TaomCoop.Core;

namespace TaomCoopHost;

// Antivirus aware Prepare and Start (CR-88BP7). Prepare server and a start that worked remember the server
// files. The next start words its own problems with them as antivirus. The watch never stops a start by itself.
public partial class MainWindow
{
    private void FixAntivirus_Click(object sender, RoutedEventArgs e) =>
        AntivirusWindow.Show(this, AntivirusFolders.For(AppContext.BaseDirectory, ToolData.Current?.ProductFolder,
            _settings.ServerRoot, _gameRoot, AntivirusFiles.WorkshopCoop(BannerlordFinder.SteamLibraries())));

    // The list replaces the one before, so a file removed on purpose is forgotten and never named again.
    // It runs in the background, since writing waits for the file lock and Start calls it on the window's thread.
    private static void RememberServerFiles(string root) =>
        _ = Task.Run(() =>
        {
            try
            {
                var workshop = AntivirusFiles.WorkshopCoop(BannerlordFinder.SteamLibraries());
                AntivirusWatch.ForTool().Remember([root, workshop], TaomServerFiles.Watched(root, workshop, TaomServerFiles.KitCompatModule(HostKitFolder)));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or InvalidOperationException)
            {
                // Best effort. Without the list the next start gives the usual messages.
            }
        });

    /// <summary>The remembered files gone since Prepare server or the last start that worked, read before
    /// Start puts anything back. Empty when anything about it fails, so Start never fails on it. Runs off
    /// the UI thread, since a gone Workshop file is looked at again after a moment (a Steam update swaps it).</summary>
    private static (IReadOnlyList<string> Gone, string? Workshop) GoneServerFiles(string root)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(root))
                return ([], null);
            var workshop = AntivirusFiles.WorkshopCoop(BannerlordFinder.SteamLibraries());
            var gone = AntivirusWatch.ForTool().GoneSettled([root, workshop], workshop, TimeSpan.FromSeconds(2));
            // Only files this tool version still puts there, so a file an update renamed or dropped is never named.
            return (AntivirusFiles.StillShipped(gone, TaomServerFiles.Watched(root, workshop, TaomServerFiles.KitCompatModule(HostKitFolder))), workshop);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Security.SecurityException)
        {
            return ([], null);
        }
    }

    // The start goes on, so the gone files only get one log line.
    private void NoteTakenFiles(string root, string? workshop, IReadOnlyList<string> gone)
    {
        if (AntivirusWording.Note(gone, root, workshop) is { } line)
        {
            NoteLog(line);
            _actions.Note("Antivirus", "Gone since the last start that worked: " + string.Join(", ", gone));
        }
    }
}
