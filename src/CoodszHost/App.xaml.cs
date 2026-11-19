using System.IO;
using System.Windows;
using BannerlordCoopLauncher.Core.IO;
using BannerlordCoopLauncher.Core.Support;
using BannerlordCoopLauncher.Core.Updates;

namespace CoodszHost;

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
        ToolData.Start(CoodszHost.Services.Edition.IsAD1259 ? ToolData.AD1259Server : ToolData.CoopServer);
        DispatcherUnhandledException += (_, args) => args.Handled = true;
        Coodsz.Ui.UiLanguage.Start();
        if (CoodszHost.Services.Edition.IsAD1259)
            Coodsz.Ui.UiLanguage.WhatsNewOnce("ad1259-server", ReleaseNotice.AD1259Version, WhatsNew.AD1259);
        else
            Coodsz.Ui.UiLanguage.WhatsNewOnce("coodsz-host", ReleaseNotice.FriendsVersion, WhatsNew.Friends);
        // The tool's own colour on the sidebar brand mark and the selected page edge (build-plan.md 1.2).
        Coodsz.Ui.ToolAccent.Apply(Resources, CoodszHost.Services.Edition.IsAD1259 ? Coodsz.Ui.FrameRules.AD1259Server : Coodsz.Ui.FrameRules.CoopServer);
        // The picture behind the pages, one per tool (Keep.BackdropArt). AD1259 uses the Coop castle until it has its own art:
        // a picture of its own is one file in Assets and Edition.BackdropArt.
        Coodsz.Ui.Backdrop.Apply(Resources, new Uri("pack://application:,,,/Assets/" + CoodszHost.Services.Edition.BackdropArt));
        base.OnStartup(e);
    }
}
