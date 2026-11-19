using System.IO;
using System.Windows;
using System.Windows.Threading;
using BannerlordCoopLauncher.Core.IO;
using BannerlordCoopLauncher.Core.Launch;
using TaomCoop.Core;

namespace TaomCoopLauncher;

// A Coop in Modules that isn't TAOM's 0.1.6 (often a Coop 0.1.7 for the Coop or AD1259 tools) is set aside only after
// the player says yes (CoopParking), and goes back into Modules when this launcher closes, or on the next start when
// the launcher crashed or was killed while it was out.
public partial class MainWindow
{
    private readonly string _setAsideMarker = ToolData.File(CoopParking.MarkerFileName);
    // The answer given this run, so Connect and Find the crashing mod don't ask again.
    private bool? _setAsideThisRun;
    // At startup the window isn't shown yet, so the question waits for Loaded.
    private bool _askSetAsideWhenLoaded;
    // The Workshop's newer Coop line was shown this run, so each crash guard pass doesn't log it again.
    private bool _newerWorkshopShown;
    // The window is hidden and waits for Bannerlord to close before it puts the Coop back and exits.
    private bool _closingAfterGame;

    private void SetAsideExtraCoop()
    {
        if (string.IsNullOrWhiteSpace(_gameRoot))
            return;
        IReadOnlyList<string> moved;
        try
        {
            var extra = CoopStaging.ExtraCoop(_gameRoot);
            if (extra.Count == 0)
                return;
            // With the Workshop on a newer Coop (0.1.7 since 2026-10-30) and no Coop 0.1.6 of the player's own in
            // Modules, setting a Coop aside can't help, so nothing is asked or set aside, a saved always too, and the
            // Workshop's line shows once this run. A local Coop 0.1.6 still gets the other copies set aside.
            if (CoopParking.NothingToSetAsideFor(CoopStaging.FindWorkshopModule(BannerlordFinder.SteamLibraries()), _gameRoot) is { } newer)
            {
                if (!_newerWorkshopShown)
                    ShowJoin(newer);
                _newerWorkshopShown = true;
                return;
            }

            // The same rule as the put back side: while Bannerlord runs nothing moves in Modules, and nothing is asked.
            if (ClientLaunchGuard.IsGameRunning())
            {
                Log(CoopParking.CloseGameFirst);
                return;
            }

            if (!IsLoaded && CoopParking.NeedsAsk(_settings.SetAsideCoop, _setAsideThisRun))
            {
                _askSetAsideWhenLoaded = true;
                return;
            }

            var decision = CoopParking.Decide(_settings.SetAsideCoop, _setAsideThisRun, () => SetAsideCoopWindow.Ask(this, CoopParking.Question(extra)));
            _setAsideThisRun = decision.SetAside;
            if (decision.Save != null)
            {
                _settings.SetAsideCoop = decision.Save;
                SaveSettings();
            }

            if (!decision.SetAside)
            {
                // Nothing moves, so the Coop row and Connect keep saying TAOM needs Coop 0.1.6.
                if (decision.Asked && CoopVersionProblem() is { } problem)
                    ShowJoin(problem);
                return;
            }

            moved = CoopParking.SetAside(_gameRoot, _setAsideMarker);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log("Could not set the other Coop aside. " + ex.Message);
            return;
        }

        foreach (var parked in moved)
            Log(CoopParking.SetAsideLine(parked));
        // The rows, the Next step card and the Active Coop line were read with that copy still in Modules.
        if (moved.Count > 0 && !_loadingForm)
            RefreshModules();
    }

    // At startup: what a launcher that crashed or was killed left set aside goes back first. While Bannerlord runs it
    // stays out, so the game doesn't find a second Coop; closing this launcher puts it back then.
    private void PutBackAfterCrash()
    {
        if (!CoopParking.HasSetAside(_setAsideMarker, _gameRoot))
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
            foreach (var line in CoopParking.PutBack(_setAsideMarker, _gameRoot))
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
        if (!CoopParking.HasSetAside(_setAsideMarker, _gameRoot))
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
