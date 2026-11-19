using TaomCoop.Core;

namespace TaomCoopHost;

// Coodsz War Sync on every start: the server part of the War of the Ring sync, riding in
// DedicatedServer.Windows like Coop Save Rescue. A failed copy never stops the start. Any error, a locked file or
// a timeout too, only writes one log line.
public partial class MainWindow
{
    private void InstallWarSync(string server)
    {
        try
        {
            WarSyncSubModule.Install(server, WarSyncSubModule.PayloadDirectory());
        }
        catch (Exception ex)
        {
            NoteLog(WarSyncSubModule.InstallFailed(WarSyncSubModule.IsInstalled(server), ex.Message));
        }
    }
}
