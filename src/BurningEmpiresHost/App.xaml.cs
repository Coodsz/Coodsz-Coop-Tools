using System.IO;
using System.Windows;
using BannerlordCoopLauncher.Core.IO;
using BannerlordCoopLauncher.Core.Support;
using BannerlordCoopLauncher.Core.Updates;

namespace BurningEmpiresHost;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // A copy of this exe started by an update installs it here, then exits (UpdateApply).
        if (UpdateApply.OnStartup(e.Args))
        {
            Environment.Exit(0);
            return;
        }
        // A copy of this exe started to read a crash dump writes its text, then exits (DumpStackSummary).
        if (DumpStackSummary.OnStartup(e.Args))
        {
            Environment.Exit(Environment.ExitCode);
            return;
        }

        // A new build under an old exe name hands over to a copy under its new name.
        if (ToolRename.OnStartup())
        {
            Environment.Exit(0);
            return;
        }

        ToolUpdate.RemovePreviousVersion(AppContext.BaseDirectory, Environment.ProcessPath ?? "", ToolUpdate.IsSingleFileApp());
        // Settings in %LOCALAPPDATA%\Coodsz (copied once from beside the exe), and the modules this exe carries.
        ToolData.Start(ToolData.BeServer);
        // A net like Coodsz Host's: an exception a window handler did not catch is logged and the tool keeps running, instead
        // of closing with the server still up.
        // Only once the window is up: an error while it is still being made would otherwise leave a process with no window, so
        // that one still ends the tool the usual way (and its crash file is written).
        DispatcherUnhandledException += (_, args) =>
        {
            try
            {
                if (MainWindow is not MainWindow { IsLoaded: true } window)
                    return;
                args.Handled = true;
                window.NoteUnexpected(args.Exception);
            }
            catch (Exception)
            {
                // The net itself never throws.
            }
        };
        Coodsz.Ui.UiLanguage.Start();
        Coodsz.Ui.UiLanguage.LicenseOnce("be-server");
        Coodsz.Ui.UiLanguage.WhatsNewOnce("be-server", ReleaseNotice.BeVersion, WhatsNew.Be);
        // The tool's own colour on the sidebar brand mark and the selected page edge (build-plan.md 1.2).
        Coodsz.Ui.ToolAccent.Apply(Resources, Coodsz.Ui.FrameRules.BeServer);
        // The picture behind the pages (Keep.BackdropArt): the Coop castle until Burning Empires has its own art, then
        // that is one file in Assets, its Resource line in the csproj and this path.
        Coodsz.Ui.Backdrop.Apply(Resources, new Uri("pack://application:,,,/Assets/redesign-backdrop-castle.jpg"));
        base.OnStartup(e);
    }
}
