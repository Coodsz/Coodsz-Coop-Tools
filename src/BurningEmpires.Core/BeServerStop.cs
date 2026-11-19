namespace BurningEmpires.Core;

/// <summary>
/// Stop and Restart in the Burning Empires server tool. They wait for a save only on a server that
/// reached SERVING in this run. One still loading has no campaign running to save: live run 2 sat
/// in SAVING for about 3.5 minutes on a server stuck loading, then wrote no save. A broken world is
/// never saved either (BrokenWorldGuard), which the tool checks first.
/// </summary>
public static class BeServerStop
{
    public static bool WaitsForSave(bool reachedServing) => reachedServing;

    public const string StoppedWhileLoading = "Stopped right away. The server was still loading, so no save was needed.";
}
