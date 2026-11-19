using System.IO;
using BannerlordCoopLauncher.Core.IO;
using BannerlordCoopLauncher.Core.Joining;
using BannerlordCoopLauncher.Core.Modules;
using TaomCoop.Core;

namespace TaomCoopLauncher;

// Get all of this server's mods: when the saved setup Connect starts has extra mods that are not on
// this PC, Connect shows one prompt, gets them through the Install mods path, then joins.
public partial class MainWindow
{
    private IReadOnlyList<ServerModNeed> ServerNeeds(string bundled) =>
        TaomSharedList.ServerNeeds(LaunchSetup, _installedExtras.Keys,
            id => File.Exists(Path.Combine(bundled, id, "SubModule.xml")));

    /// <summary>True to go on with Connect: nothing is missing, or the prompt got every mod.</summary>
    private async Task<bool> GetServerModsFirstAsync()
    {
        // A missing TAOM core keeps Connect off; Install or repair TAOM is the way for that.
        if (_gameRoot == null || CoreProblems().Count > 0)
            return true;
        ScanInstalledExtras();
        // Written out on a worker thread the first time, so the window never waits on it.
        var bundled = await ToolPayload.FolderAsync(ToolPayload.BundledMods);
        var prompt = ServerModPrompt.For(ServerNeeds(bundled));
        if (prompt == null)
            return true;

        _actions.Note("Server mods", prompt.Title + ". " + string.Join(", ", prompt.Mods.Select(need => need.Name)) + ".");
        if (!Coodsz.Ui.ServerModsWindow.Ask(this, prompt))
        {
            Log("Connect was cancelled. Nothing was changed.");
            return false;
        }

        var action = prompt.Action;
        await DownloadExtrasAsync(TaomSharedList.Missing(LaunchSetup, _installedExtras.Keys));
        ScanInstalledExtras();
        UnblockModDlls(LaunchSetup);
        ChangedMods();
        var left = ServerModPrompt.For(ServerNeeds(bundled));
        if (action == ServerModAction.GetAndConnect && left == null)
        {
            Log("Got every mod the server needs. Connecting now.");
            _actions.Note("Server mods", "Got them all and connected.");
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
    // player's PC shows the whole battle. The host has to load the setup's extra mods too, so they
    // stand for the server's list. A short warning when this PC has little RAM; it never stops Connect.
    private void WarnBigBattles(TaomSetup setup)
    {
        var warning = BigBattles.ClientWarning(setup.EnabledExtras.Select(mod => mod.ModuleId), BigBattles.ThisPcRamGb());
        if (warning == null)
            return;
        Log(warning);
        _actions.Note("Big battles", warning);
    }
}
