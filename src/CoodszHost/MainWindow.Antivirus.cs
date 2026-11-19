using System.IO;
using System.Windows;
using BannerlordCoopLauncher.Core.IO;
using BannerlordCoopLauncher.Core.Launch;
using BannerlordCoopLauncher.Core.Support;
using Coodsz.Ui;
using CoodszHost.Services;
using TaomCoop.Core;

namespace CoodszHost;

// Antivirus aware Start (CR-88BP7). A start that worked remembers the server files it ran with. The next
// start words its own problems with them as antivirus. The watch never stops a start by itself.
public partial class MainWindow
{
    private void FixAntivirus_Click(object sender, RoutedEventArgs e)
    {
        var modules = ExtraModScan.GameModulesDirectory();
        AntivirusWindow.Show(this, AntivirusFolders.For(AppContext.BaseDirectory, ToolData.Current?.ProductFolder,
            SavedServerRoot(), modules == null ? null : Path.GetDirectoryName(modules), AntivirusFiles.WorkshopCoop(BannerlordFinder.SteamLibraries())));
    }

    // The folder of the BannerlordCoopServer.exe picked last, also when that exe is gone (Executable is empty then).
    private string? SavedServerRoot()
    {
        try
        {
            return string.IsNullOrWhiteSpace(_settings.ServerExecutable) ? null : Path.GetDirectoryName(_settings.ServerExecutable);
        }
        catch (Exception ex) when (ex is ArgumentException or PathTooLongException)
        {
            return null;
        }
    }

    // What is gone since the last start that worked, read before this start puts anything back. Empty
    // when anything about it fails, so a bad path in the settings never fails Start. StartServer runs on
    // the window's thread for every start (Start server, Restart, the crash and rescue restarts), so this
    // never waits. It reads one small file and checks the files exist, with no delay and no lock. A
    // Workshop file Steam is updating right now is left out (GoneSettled with no recheck). With no recheck,
    // a Workshop file Steam deletes in the moment before appworkshop_261550.acf shows the update can still
    // be called antivirus, in the log note and, for the Workshop server program, in Start's stop line. That
    // line also says to subscribe again, which a finished update makes true anyway, so the narrow window is
    // not worth a wait on the window's thread for every start.
    private (IReadOnlyList<string> Gone, string? Root, string? Workshop, IReadOnlyList<string>? Libraries) GoneSinceLastStart()
    {
        try
        {
            var root = SavedServerRoot();
            if (string.IsNullOrWhiteSpace(root))
                return ([], null, null, null);
            var libraries = BannerlordFinder.SteamLibraries();
            var workshop = AntivirusFiles.WorkshopCoop(libraries);
            var gone = AntivirusWatch.ForTool().GoneSettled([root, workshop], workshop, TimeSpan.Zero);
            // Only files this tool version still ships, so a DLL an update renamed or dropped is never named.
            var shipped = ServerFileWatch.Files(root, workshop, ServerFolderRepair.DefaultPayload, ServerFileWatch.ModulePayloads());
            return (AntivirusFiles.StillShipped(gone, shipped), root, workshop, libraries);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Security.SecurityException)
        {
            return ([], null, null, null);
        }
    }

    // The usual "dedicated server was not found" stop, worded as a download still running when Steam is still
    // getting the Workshop Coop (CR-P75R3), and as antivirus when that exe was there before. Steam first, so an
    // update Steam already shows is never called antivirus.
    private string ServerMissingText((IReadOnlyList<string> Gone, string? Root, string? Workshop, IReadOnlyList<string>? Libraries) gone)
    {
        try
        {
            if (gone.Workshop != null && AntivirusFiles.UsesWorkshopServer(_settings.ServerExecutable) && AntivirusFiles.SteamUpdating(gone.Workshop))
                return AntivirusText.WorkshopDownloading("Start server");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Security.SecurityException)
        {
            // The usual or antivirus line below.
        }

        var text = AntivirusWording.Reword(DedicatedServerFinder.MissingMessage, _settings.ServerExecutable, gone.Gone, gone.Root, gone.Workshop,
            "Start server", AntivirusText.RemovedYourselfSetFolder);
        if (!string.Equals(text, DedicatedServerFinder.MissingMessage, StringComparison.Ordinal))
            return text;
        // Only the Workshop Coop's own server program can be mid-download. A custom or mistyped Set server
        // folder path keeps the usual line.
        if (!AntivirusFiles.UsesWorkshopServer(_settings.ServerExecutable))
            return text;
        try
        {
            // The libraries GoneSinceLastStart found, so Steam is searched once per start.
            var libraries = gone.Libraries ?? BannerlordFinder.SteamLibraries();
            var workshop = gone.Workshop ?? AntivirusFiles.WorkshopCoop(libraries);
            var downloading = workshop != null ? AntivirusFiles.SteamUpdating(workshop) : AntivirusFiles.CoopDownloading(libraries);
            return downloading ? AntivirusText.WorkshopDownloading("Start server") : text;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Security.SecurityException)
        {
            return text;
        }
    }

    // The start goes on, so the other gone files only get one log line.
    private void NoteGoneFiles((IReadOnlyList<string> Gone, string? Root, string? Workshop, IReadOnlyList<string>? Libraries) gone)
    {
        if (AntivirusWording.Note(gone.Gone, gone.Root, gone.Workshop) is { } line)
        {
            Append(line);
            _actions.Note("Antivirus", "Gone since the last start that worked: " + string.Join(", ", gone.Gone));
        }
    }

    // After the server process started. The list replaces the one before, so a file removed on purpose is
    // forgotten and never named again. It runs in the background, since writing waits for the file lock.
    private void RememberServerFiles()
    {
        var root = ServerRoot;
        _ = Task.Run(() =>
        {
            try
            {
                var workshop = AntivirusFiles.WorkshopCoop(BannerlordFinder.SteamLibraries());
                AntivirusWatch.ForTool().Remember([root, workshop], ServerFileWatch.Files(root, workshop, ServerFolderRepair.DefaultPayload, ServerFileWatch.ModulePayloads()));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or InvalidOperationException)
            {
                // Best effort. Without the list the next start gives the usual messages.
            }
        });
    }

    // LocateDedicatedServer picked another BannerlordCoopServer.exe because the saved one is gone. One log
    // line when the watch had that exe, so the host knows why the server folder changed. A Workshop Coop exe
    // is left out while Steam is updating Coop (GoneSettled, SteamWorkshopAcf.CoopPending).
    private void NoteReplacedServer(string? old, string found)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(old) || Path.GetDirectoryName(old) is not { } oldRoot)
                return;
            var full = Path.GetFullPath(old);
            // Null while Steam has SubModule.xml out mid update, so the note can still name the Workshop exe then.
            // It is only a log line.
            var workshop = AntivirusFiles.WorkshopCoop(BannerlordFinder.SteamLibraries());
            if (AntivirusWatch.ForTool().GoneSettled([oldRoot], workshop, TimeSpan.Zero).Contains(full, StringComparer.OrdinalIgnoreCase))
                Append(AntivirusText.OtherServer(full, Path.GetDirectoryName(found) ?? found));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
        }
    }
}
