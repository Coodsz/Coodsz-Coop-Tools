using System.IO;
using System.Windows;
using System.Windows.Threading;
using BannerlordCoopLauncher.Core.IO;
using BannerlordCoopLauncher.Core.Support;
using BannerlordCoopLauncher.Core.Updates;

namespace BannerlordCoopLauncher;

public partial class App : Application
{
    public App()
    {
        // A copy of this exe started by an update installs it here, then exits (UpdateApply).
        if (UpdateApply.OnStartup(Environment.GetCommandLineArgs().Skip(1).ToList()))
        {
            Environment.Exit(0);
            return;
        }
        // A copy of this exe started to read a crash dump writes its text, then exits (DumpStackSummary).
        if (DumpStackSummary.OnStartup(Environment.GetCommandLineArgs().Skip(1).ToList()))
        {
            Environment.Exit(Environment.ExitCode);
            return;
        }

        // Read before the rename cleanup below can remove Coodsz and Friends.exe from this folder. It leaves a
        // note there too, so a fold in that fails on this start still knows the folder on the next one.
        var fromFriends = MoreNationsFoldIn.CameFromFriends(AppContext.BaseDirectory);
        // A new build under an old exe name hands over to a copy under its new name.
        if (ToolRename.OnStartup())
        {
            Environment.Exit(0);
            return;
        }

        ToolUpdate.RemovePreviousVersion(AppContext.BaseDirectory, Environment.ProcessPath ?? "", ToolUpdate.IsSingleFileApp());
        // Coodsz and Friends settings (and a second client folder's) join this client's settings once.
        MoreNationsFoldIn.Run(ToolData.FolderOf(ToolData.CoopClient), AppContext.BaseDirectory, fromFriends, ToolData.FolderOf(ToolData.MoreNationsClient));
        // Settings in %LOCALAPPDATA%\Coodsz (copied once from beside the exe), and the modules this exe carries.
        ToolData.Start(ToolData.CoopClient);
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        Coodsz.Ui.UiLanguage.Start();
        Coodsz.Ui.UiLanguage.LicenseOnce("friends-launcher");
        Coodsz.Ui.UiLanguage.WhatsNewOnce("friends-launcher", ReleaseNotice.FriendsVersion, WhatsNew.Friends);
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        // After InitializeComponent, so the keys land in the App.xaml resources: the tool's own colour on the sidebar brand
        // mark and the selected page edge (build-plan.md 1.2), and the Coop Server Tool's castle behind the pages.
        Coodsz.Ui.ToolAccent.Apply(Resources, Coodsz.Ui.FrameRules.CoopClient);
        Coodsz.Ui.Backdrop.Apply(Resources, new Uri("pack://application:,,,/Assets/redesign-backdrop-castle.jpg"));
        base.OnStartup(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "launcher-error.txt");
            File.AppendAllText(path, DateTime.Now.ToString("u") + " " + e.Exception + Environment.NewLine);
        }
        catch
        {
            // The error log is best-effort.
        }

        Coodsz.Ui.UiLanguage.Message(e.Exception.Message, LauncherBrand.Name);
        e.Handled = true;
    }
}
