using System.IO;
using System.Windows;
using BannerlordCoopLauncher.Core.IO;
using BannerlordCoopLauncher.Core.Support;
using BannerlordCoopLauncher.Core.Updates;

namespace AD1259Launcher;

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
        ToolData.Start(ToolData.AD1259Client);
        Coodsz.Ui.UiLanguage.Start();
        Coodsz.Ui.UiLanguage.LicenseOnce("ad1259-launcher");
        Coodsz.Ui.UiLanguage.WhatsNewOnce("ad1259-launcher", ReleaseNotice.AD1259Version, WhatsNew.AD1259);
        // The tool's own colour on the sidebar brand mark and the selected page edge (build-plan.md 1.2), and the Coop castle (until AD1259 has art of its own)
        // behind the pages. After InitializeComponent, so the keys land in the App.xaml resources.
        Coodsz.Ui.ToolAccent.Apply(Resources, Coodsz.Ui.FrameRules.AD1259Client);
        Coodsz.Ui.Backdrop.Apply(Resources, new Uri("pack://application:,,,/Assets/redesign-backdrop-castle.jpg"));
        base.OnStartup(e);
    }
}
