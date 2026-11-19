using System.IO;
using System.Windows;
using BannerlordCoopLauncher.Core.IO;
using BannerlordCoopLauncher.Core.Launch;
using BannerlordCoopLauncher.Core.Support;
using BurningEmpires.Core;
using Coodsz.Ui;
using TaomCoop.Core;

namespace BurningEmpiresHost;

public partial class MainWindow
{
    // Fix antivirus in the server controls. It lists this PC's folders and opens Windows Security, nothing more.
    private void FixAntivirus_Click(object sender, RoutedEventArgs e) =>
        AntivirusWindow.Show(this, AntivirusFolders.For(AppContext.BaseDirectory, ToolData.Current?.ProductFolder,
            _settings.ServerRoot, FindGameRoot(), AntivirusFiles.WorkshopCoop(BannerlordFinder.SteamLibraries())));

    // After a good Prepare and a start that worked (CR-88BP7). The list replaces the one before, so a file
    // removed on purpose is forgotten. The next Prepare words a Workshop server file gone since then as antivirus.
    // It runs in the background, since writing waits for the file lock.
    private static void RememberServerFiles(string root) =>
        _ = Task.Run(() =>
        {
            try
            {
                var workshop = AntivirusFiles.WorkshopCoop(BannerlordFinder.SteamLibraries());
                // A Workshop Coop missing a server file keeps what the watch saw there, since a start from the
                // server folder's own copy still works and would otherwise forget what antivirus took.
                if (workshop != null && AntivirusFiles.WorkshopServerProblem(workshop, BeServerPrepare.PrepareButton) != null)
                    workshop = null;
                AntivirusWatch.ForTool().Remember([root, workshop], BeServerPrepare.WatchedFiles(root, workshop, ServerFolderRepair.DefaultPayload));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or InvalidOperationException or System.Security.SecurityException)
            {
                // Best effort. Without the list Prepare gives the usual messages.
            }
        });

    /// <summary>The Workshop Coop server files the watch saw before and are gone now. Empty when anything about it fails.</summary>
    private static IReadOnlyList<string> GoneWorkshopFiles(IEnumerable<string> libraries)
    {
        try
        {
            var workshop = AntivirusFiles.WorkshopCoop(libraries);
            return workshop == null ? [] : AntivirusWatch.ForTool().GoneSettled([workshop], workshop, TimeSpan.Zero);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Security.SecurityException)
        {
            return [];
        }
    }
}
