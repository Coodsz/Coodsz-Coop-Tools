using System.IO;
using System.Windows;
using System.Windows.Threading;
using BannerlordCoopLauncher.Core.IO;
using BannerlordCoopLauncher.Core.Launch;
using BurningEmpires.Core;
using TaomCoop.Core;

namespace BurningEmpiresLauncher;

// The extra Coop copies ApplyCrashGuards and Use Workshop Coop set aside (BeCoopCopy through CoopParking, Tool BE) go
// back into Modules when this launcher closes, or on the next start when the launcher crashed or was killed while they
// were out. A tag beside each parked folder brings it back even when the marker in the data folder is gone.
public partial class MainWindow
{
    private readonly string _setAsideMarker = ToolData.File(BeCoopCopy.SetAsideMarkerFileName);
    // The window is hidden and waits for Bannerlord to close before it puts the Coop back and exits.
    private bool _closingAfterGame;

    // At startup: what a launcher that crashed or was killed left set aside goes back first. While Bannerlord runs it
    // stays out, so the game doesn't find a second Coop; closing this launcher puts it back then.
    private void PutBackAfterCrash()
    {
        if (!CoopParking.HasSetAside(_setAsideMarker, _gameRoot, CoopParking.BeTool))
            return;
        if (ClientLaunchGuard.IsGameRunning())
        {
            Log(CoopParking.WaitsForGame);
            return;
        }

        PutBackSetAsideCoop();
    }

    private void PutBackSetAsideCoop()
    {
        try
        {
            foreach (var line in CoopParking.PutBack(_setAsideMarker, _gameRoot, CoopParking.BeTool))
                Log(line);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log("Could not put the Coop that was set aside back into Modules. " + ex.Message);
        }
    }

    // True when the window can close now. With Bannerlord still running the window hides instead and waits, so the game
    // never finds a second Coop in Modules; once it closes the Coop goes back and the launcher exits.
    private bool PutBackOnClose()
    {
        if (!CoopParking.HasSetAside(_setAsideMarker, _gameRoot, CoopParking.BeTool))
            return true;
        if (!ClientLaunchGuard.IsGameRunning())
        {
            PutBackSetAsideCoop();
            return true;
        }

        _closingAfterGame = true;
        Hide();
        var wait = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        wait.Tick += (_, _) =>
        {
            if (ClientLaunchGuard.IsGameRunning())
                return;
            wait.Stop();
            PutBackSetAsideCoop();
            Application.Current.Shutdown();
        };
        wait.Start();
        return false;
    }
}
