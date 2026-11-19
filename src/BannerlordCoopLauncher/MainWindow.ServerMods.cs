using BannerlordCoopLauncher.Core.IO;
using BannerlordCoopLauncher.Core.Joining;
using BannerlordCoopLauncher.Core.Modules;
using BannerlordCoopLauncher.Core.Launch;

namespace BannerlordCoopLauncher;

// Get all of this server's mods: when the setup Connect sends to the server has mods that are missing
// or out of date, Connect shows one prompt, gets them through the Install mods path, then joins.
public partial class MainWindow
{
    // A bundled mod counts as one the launcher gets only when the launcher carries it (its BundledMods
    // payload): Install mods copies it then (LoadPackAsync, InstallBundledModulesAsync). Otherwise it is by hand.
    private IReadOnlyList<ServerModNeed> ServerNeeds(string bundled) =>
        _resolution == null
            ? Array.Empty<ServerModNeed>()
            : ServerModNeeds.FromResolution(_resolution, NeedsManualInstall,
                id => BundledModules.CanCopy(bundled, id));

    /// <summary>True to go on with Connect: nothing is missing, or the prompt got every mod.</summary>
    private async Task<bool> GetServerModsFirstAsync()
    {
        // Nothing missing, or a problem the prompt does not deal with: Connect runs its own checks as before.
        if (_resolution == null || !ServerModNeeds.PromptCovers(_resolution))
            return true;
        // Written out on a worker thread the first time, so the window never waits on it.
        var bundled = await ToolPayload.FolderAsync(ToolPayload.BundledMods);
        var prompt = ServerModPrompt.For(ServerNeeds(bundled));
        if (prompt == null)
            return true;

        NoteAction("Server mods", prompt.Title + ". " + string.Join(", ", prompt.Mods.Select(need => need.Name)) + ".");
        if (!Coodsz.Ui.ServerModsWindow.Ask(this, prompt))
        {
            Log("Connect was cancelled. Nothing was changed.");
            return false;
        }

        var action = prompt.Action;
        await RunBusyAsync(InstallSetupModsAsync);
        var left = ServerModPrompt.For(ServerNeeds(bundled));
        if (action == ServerModAction.GetAndConnect && left == null)
        {
            Log("Got every mod the server needs. Connecting now.");
            NoteAction("Server mods", "Got them all and connected.");
            return true;
        }

        if (left != null)
        {
            var names = string.Join(", ", left.Mods.Select(need => need.Name));
            Log(left.Count == 1
                ? names + " is not ready yet, so the game was not started. Click Connect again when it is there."
                : names + " are not ready yet, so the game was not started. Click Connect again when they are there.");
        }

        return false;
    }

    // A server with Limitless (and its Coop patch) runs battles far bigger than Coop's 1000, and every
    // player's PC shows the whole battle. The setup Connect sends is the server's mod list (Coop's join
    // check makes them match). A short warning when this PC has little RAM; it never stops Connect.
    private void WarnBigBattles()
    {
        if (_resolution == null)
            return;
        var warning = BigBattles.ClientWarning(_resolution.Modules.Where(module => module.Enabled).Select(module => module.ModuleId), BigBattles.ThisPcRamGb());
        if (warning == null)
            return;
        Log(warning);
        NoteAction("Big battles", warning);
    }
}
