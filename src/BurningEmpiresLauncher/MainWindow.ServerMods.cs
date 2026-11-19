using System.IO;
using BannerlordCoopLauncher.Core.IO;
using BannerlordCoopLauncher.Core.Joining;
using BannerlordCoopLauncher.Core.Modules;
using BurningEmpires.Core;
using BurningEmpires.Ui;
using TaomCoop.Core;

namespace BurningEmpiresLauncher;

// Get all of this server's mods: when the BUTR modules or the extra mods of the saved setup Connect
// starts are not on this PC, Connect shows one prompt, gets them through Steam and the Install mods
// path, then joins.
public partial class MainWindow
{
    // The BUTR modules first (Harmony, ButterLib and UIExtenderEx from the Steam Workshop, MCM by hand from
    // Nexus Mods), then the setup's extra mods. The Burning Empires core is not here; Install or repair
    // Burning Empires puts it in.
    private IReadOnlyList<ServerModNeed> ServerNeeds() =>
        MissingButr()
            .Select(BeSharedList.ButrNeed)
            .Concat(BeSharedList.ServerNeeds(LaunchSetup, _installedExtras.Keys,
                    id => ToolPayload.Peek(ToolPayload.BundledMods) is { } carried && File.Exists(Path.Combine(carried, id, "SubModule.xml"))))
            .ToList();

    /// <summary>True to go on with Connect: nothing is missing, or the prompt got every mod.</summary>
    private async Task<bool> GetServerModsFirstAsync()
    {
        // A missing Burning Empires core keeps Connect off; Install or repair Burning Empires is the way for that.
        if (_gameRoot == null || CoreProblems(withButr: false).Count > 0)
            return true;
        ScanInstalledExtras();
        var prompt = ServerModPrompt.For(ServerNeeds());
        if (prompt == null)
            return true;

        _actions.Note("Server mods", prompt.Title + ". " + string.Join(", ", prompt.Mods.Select(need => need.Name)) + ".");
        if (!Coodsz.Ui.ServerModsWindow.Ask(this, prompt))
        {
            Log("Connect was cancelled. Nothing was changed.");
            return false;
        }

        var action = prompt.Action;
        await GetButrAsync();
        await DownloadExtrasAsync(BeSharedList.Missing(LaunchSetup, _installedExtras.Keys).ToList());
        ScanInstalledExtras();
        UnblockModDlls(LaunchSetup);
        ChangedMods();
        var left = ServerModPrompt.For(ServerNeeds());
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

    // ----- Version drift before a join. It warns and never stops Connect. -----

    // The mod list imported last (Import mod list), usually the host's. Kept in the launcher's settings
    // folder so Connect can compare the core versions with it.
    private static string HostListPath => ToolData.File("be-host-mod-list.json");

    private static void RememberHostList(IReadOnlyList<SharedModEntry> shared)
    {
        try
        {
            SharedModList.Write(HostListPath, shared);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
        }
    }

    private static IReadOnlyList<SharedModEntry> ReadHostList()
    {
        try
        {
            return File.Exists(HostListPath) ? SharedModList.Read(File.ReadAllText(HostListPath)) : Array.Empty<SharedModEntry>();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.Text.Json.JsonException)
        {
            return Array.Empty<SharedModEntry>();
        }
    }

    // The BUTR modules follow the Workshop, so another version than the tested one only gets a note.
    // The imported mod list also says which BUTR, Coop, compat and Burning Empires versions the host
    // has; a different one here can make the server turn the join down.
    private void WarnVersionDrift()
    {
        if (_gameRoot == null)
            return;
        foreach (var mod in BeStack.Butr)
        {
            var have = CoreVersion(mod.ModuleId);
            if (have.Length > 0 && !have.Equals(mod.TestedVersion, StringComparison.OrdinalIgnoreCase))
                Log(mod.Name + " is " + have + " here. The Burning Empires Coop set was tested with " + mod.TestedVersion + ". It usually still works. If the join fails, check that the host has the same " + mod.Name + ".");
        }

        var warnings = HostDriftWarnings();
        foreach (var warning in warnings)
            Log(warning + " The server may turn the join down. Update it here, or ask the host for a new mod list.");
        ShowVersionDriftNote(warnings.Count);
    }

    // The core versions here that differ from the host's imported mod list, one line each.
    private List<string> HostDriftWarnings()
    {
        var warnings = new List<string>();
        if (_gameRoot == null)
            return warnings;
        foreach (var entry in ReadHostList().Where(entry => IsSharedCore(entry.Id)))
        {
            var warning = SharedModList.VersionWarning(string.IsNullOrWhiteSpace(entry.Name) ? CoreName(entry.Id.Trim()) : entry.Name, entry.Version, CoreVersion(entry.Id.Trim()));
            if (warning != null)
                warnings.Add(warning);
        }

        return warnings;
    }

    // One orange line near Connect, so a version drift is seen without reading the log.
    private void ShowVersionDriftNote(int differences)
    {
        var note = BeUiText.DriftNote(differences);
        VersionDriftNote.Text = note ?? "";
        VersionDriftNote.Visibility = note == null ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;
    }

    private static bool IsSharedCore(string? id)
    {
        var trimmed = (id ?? "").Trim();
        return BeStack.IsButr(trimmed)
            || trimmed.Equals(BeStack.CoopModuleId, StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals(BeStack.CompatModuleId, StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals(BeStack.BeModuleId, StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals(BeStack.ElephantsModuleId, StringComparison.OrdinalIgnoreCase);
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
