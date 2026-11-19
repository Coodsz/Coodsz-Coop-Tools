using System.Collections.ObjectModel;
using System.Text;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using BannerlordCoopLauncher.Core.IO;
using BannerlordCoopLauncher.Core.Updates;
using BannerlordCoopLauncher.Core.Launch;
using BannerlordCoopLauncher.Core.Models;
using BannerlordCoopLauncher.Core.Modules;
using BannerlordCoopLauncher.Core.Nexus;
using BannerlordCoopLauncher.Core.Network;
using BannerlordCoopLauncher.Core.Parsing;
using BannerlordCoopLauncher.Core.Profile;
using BannerlordCoopLauncher.Core.Steam;
using BannerlordCoopLauncher.Core.Support;
using Microsoft.Win32;

namespace BannerlordCoopLauncher;

public partial class MainWindow
{
    private async void Install_Click(object sender, RoutedEventArgs e) => await RunBusyAsync(InstallSetupModsAsync);

    // Install mods, and the main button of the server mods prompt on Connect: gets the mods this setup
    // needs that are missing or out of date, through Steam, and says which ones need a manual download.
    private async Task InstallSetupModsAsync(CancellationToken cancellationToken)
    {
        if (_gameRoot == null)
            await DetectGamePath(logWhenMissing: true);
        await LoadPackAsync(cancellationToken);
        if (_profile == null)
            return;

        var pending = _resolution?.Modules
            .Where(module => module.Enabled && module.State is ModuleState.Missing or ModuleState.UpdatePending)
            .ToList() ?? new List<ResolvedModule>();
        // Steam can not download a Workshop item that is hidden or removed, or a
        // mod that is not on the Workshop at all, so asking again only looped.
        // Those mods are put in Modules by hand.
        var manual = pending.Where(NeedsManualInstall).ToList();
        var localOnly = pending
            .Where(module => !IsSteamWorkshopId(module.WorkshopId) && !manual.Contains(module))
            .Select(module => module.Name)
            .ToList();
        var missing = pending
            .Where(module => !manual.Contains(module))
            .Select(module => module.WorkshopId)
            .Where(IsSteamWorkshopId)
            .Select(id => id!)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var missingNames = pending.Except(manual).Select(module => module.Name).ToList();
        if (missingNames.Count > 0)
            NoteAction("Install mods", "Missing: " + string.Join(", ", missingNames) + ". Opening Steam so they can subscribe.");
        else if (manual.Count == 0)
            NoteAction("Install mods", "Every mod on this setup was already found.");

        if (localOnly.Count > 0 && missing.Count > 0)
            Log("These mods come with the launcher folder and were not copied: " + string.Join(", ", localOnly) + ".");

        if (manual.Count > 0)
        {
            LogManualInstall(manual);
            NoteAction("Install mods", "Not downloadable from Steam: " + string.Join(", ", manual.Select(module => module.Name)) + ".");
            if (missing.Count == 0 && localOnly.Count == 0)
                return;
        }

        if (missing.Count == 0)
        {
            Log(localOnly.Count == 0
                ? "Install mods checked this setup. Every mod is already in the game's Modules folder, so nothing was copied or downloaded. Add mods is the button that puts extra mods on this list."
                : "Install mods checked this setup. These mods were not copied: " + string.Join(", ", localOnly) + ". Put those folders next to " + LauncherBrand.Name + ".exe, then click Install mods again. Add mods is the button that puts extra mods on this list.");
            return;
        }

        await EnsureSteamAsync(_profile, cancellationToken);
        var (subscribed, steamReady) = await SubscribeAndDownloadAsync(missing, cancellationToken);
        if (subscribed)
        {
            await LoadPackAsync(cancellationToken);
            // Steam says these are installed and ready, but the Workshop page shows
            // a newer update. Clicking again gave the same answer, so the copy on
            // this PC is used instead of locking Connect. This goes by Steam's
            // ready answer, not by whether a download flag showed up for a moment.
            var stuck = ModuleResolver.SteamWillNotUpdate(_resolution?.Modules ?? Array.Empty<ResolvedModule>(), steamReady);
            if (stuck.Count > 0)
            {
                foreach (var module in stuck)
                    _steamKeptLocal.Add(module.WorkshopId!);
                await LoadPackAsync(cancellationToken);
                var stuckNames = string.Join(", ", stuck.Select(module => module.Name));
                Log(stuck.Count == 1
                    ? stuckNames + " is older than the Workshop page, and Steam would not download the update yet. The copy on this PC is used for now, so you can connect. If the host already has the update and the join fails, close Steam fully, open it again, then click Install mods."
                    : stuckNames + " are older than the Workshop page, and Steam would not download the update yet. The copies on this PC are used for now, so you can connect. If the host already has the update and the join fails, close Steam fully, open it again, then click Install mods.");
                NoteAction("Install mods", "Steam would not update " + stuckNames + ". The copy on this PC is used.");
            }

            var left = _resolution?.Modules.Any(module =>
                module.Enabled
                && module.State is ModuleState.Missing or ModuleState.UpdatePending
                && IsSteamWorkshopId(module.WorkshopId)) == true;
            Log(left ? "Steam is still finishing some mods. Click Install mods again in a minute." : "Install mods finished. Those mods are installed.");
            NoteAction("Install mods", left ? "Subscribed through Steam; some downloads were still running." : "Subscribed and downloaded through Steam.");
            return;
        }

        Log("Install mods is opening Steam for Workshop mods that are missing from the game's Modules folder. This does not add extra mods to the list. Click Subscribe on each page if Steam asks.");
        foreach (var id in missing)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!ShellOpen.TryOpen(ShellOpen.SteamWorkshopPage(id), out _))
            {
                Log(ShellOpen.SteamPageProblem);
                return;
            }

            await Task.Delay(400, cancellationToken);
        }

        Log("Waiting for Steam to finish the downloads.");
        for (var attempt = 0; attempt < 24; attempt++)
        {
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            await LoadPackAsync(cancellationToken);
            var stillMissing = _resolution?.Modules.Any(module =>
                module.Enabled
                && module.State is ModuleState.Missing or ModuleState.UpdatePending
                && IsSteamWorkshopId(module.WorkshopId)) == true;
            if (!stillMissing)
            {
                Log("Install mods finished. Those mods are installed.");
                NoteAction("Install mods", "The missing mods were found after the download.");
                return;
            }
        }

        Log("Steam is still downloading. Click Install mods again in a minute.");
        NoteAction("Install mods", "Steam was still downloading when the wait ended.");
    }

    // Subscribes and downloads through Steam itself, like a mod manager does.
    // Returns false when Steam could not be used, so the caller opens the
    // Workshop pages instead.
    // Also returns the Workshop ids Steam reported installed and ready at the end.
    private async Task<(bool Used, IReadOnlyCollection<string> SteamReady)> SubscribeAndDownloadAsync(IReadOnlyList<string> workshopIds, CancellationToken cancellationToken)
    {
        var notUsed = (false, (IReadOnlyCollection<string>)Array.Empty<string>());
        var ids = workshopIds
            .Select(id => ulong.TryParse(id, out var number) ? number : 0UL)
            .Where(number => number > 0)
            .Distinct()
            .ToList();
        if (ids.Count == 0 || _gameRoot == null)
            return notUsed;

        var names = new Dictionary<ulong, string>();
        foreach (var module in _resolution?.Modules ?? Array.Empty<ResolvedModule>())
        {
            if (ulong.TryParse(module.WorkshopId, out var number) && !names.ContainsKey(number))
                names[number] = module.Name;
        }

        var gameRoot = _gameRoot;
        string? failure = null;
        WorkshopSubscribeResult? result = null;
        Log("Subscribing in Steam to " + ids.Count + " Workshop mod" + (ids.Count == 1 ? "" : "s") + ". Steam shows Bannerlord as running until the downloads finish.");
        await Task.Factory.StartNew(() =>
        {
            // Every Steam call has to come from this one thread.
            using var steam = SteamApiWorkshop.TryOpen(gameRoot, out var reason);
            if (steam == null)
            {
                failure = reason;
                return;
            }

            result = WorkshopSubscribe.Run(steam, ids, names, line => Dispatcher.Invoke(() => Log(line)), TimeSpan.FromMinutes(10), cancellationToken);
        }, cancellationToken, TaskCreationOptions.LongRunning, TaskScheduler.Default);

        if (failure != null)
        {
            Log("Could not subscribe through Steam by itself. " + failure + " Opening the Workshop pages instead.");
            return notUsed;
        }

        string Label(ulong id) => names.TryGetValue(id, out var name) ? name : id.ToString();
        if (result!.Ready.Count > 0)
            Log("Subscribed and downloaded: " + string.Join(", ", result.Ready.Select(Label)) + ".");
        if (result.StillDownloading.Count > 0)
            Log("Steam is still downloading " + string.Join(", ", result.StillDownloading.Select(Label)) + ". The download keeps going in Steam.");
        // A public item Steam would not subscribe to points at Steam itself, not
        // at the item (seen when Steam was offline or on another account).
        var publicFailed = result.Failed.Where(id => IsPublicOnWorkshop(id.ToString())).Select(Label).ToList();
        if (publicFailed.Count > 0)
        {
            var publicNames = string.Join(", ", publicFailed);
            Log(publicFailed.Count == 1
                ? publicNames + " is public on the Workshop, so Steam itself did not take the subscribe. Check that Steam is online and signed in with the account that owns Bannerlord, then click Subscribe on the Workshop page."
                : publicNames + " are public on the Workshop, so Steam itself did not take the subscribe. Check that Steam is online and signed in with the account that owns Bannerlord, then click Subscribe on the Workshop pages.");
        }
        // Nothing moved at all: the caller opens every Workshop page itself.
        if (result.Ready.Count == 0 && result.StillDownloading.Count == 0)
            return notUsed;

        if (result.Failed.Count > 0)
        {
            Log("Steam would not subscribe to " + string.Join(", ", result.Failed.Select(Label)) + ". Opening those Workshop pages so you can subscribe by hand.");
            foreach (var id in result.Failed)
            {
                if (!ShellOpen.TryOpen(ShellOpen.SteamWorkshopPage(id.ToString()), out _))
                {
                    Log(ShellOpen.SteamPageProblem);
                    break;
                }

                await Task.Delay(400, cancellationToken);
            }
        }

        return (true, result.Ready.Select(id => id.ToString()).ToList());
    }

    // Workshop ids where Install mods found Steam would not download a newer
    // update. Kept for this session; Install mods asks Steam again next time.
    private readonly HashSet<string> _steamKeptLocal = new(StringComparer.Ordinal);
    private IReadOnlyDictionary<string, PublishedFile>? _remote;

    private bool IsGoneFromWorkshop(string? workshopId) =>
        IsSteamWorkshopId(workshopId)
        && _remote != null
        && _remote.TryGetValue(workshopId!, out var published)
        && published.Result is 9 or 15;

    // A missing mod Install mods can not get through Steam: its Workshop item is
    // hidden or removed, or it is a local mod that does not come with the launcher.
    private bool NeedsManualInstall(ResolvedModule module) =>
        module.State == ModuleState.Missing
        && (IsGoneFromWorkshop(module.WorkshopId)
            || (!IsSteamWorkshopId(module.WorkshopId) && !BundledModules.ModuleIds.Contains(module.ModuleId, StringComparer.OrdinalIgnoreCase)));

    private void LogManualInstall(IReadOnlyCollection<ResolvedModule> modules)
    {
        var gone = modules.Where(module => IsSteamWorkshopId(module.WorkshopId)).ToList();
        if (gone.Count > 0)
        {
            var goneNames = string.Join(", ", gone.Select(module => module.Name));
            Log(gone.Count == 1
                ? goneNames + " is hidden or removed on the Steam Workshop, so Steam can not download it. Get it from Nexus Mods or ModDB, put its folder in the game's Modules folder, then click Check mods. The launcher uses that copy."
                : goneNames + " are hidden or removed on the Steam Workshop, so Steam can not download them. Get them from Nexus Mods or ModDB, put their folders in the game's Modules folder, then click Check mods. The launcher uses those copies.");
        }

        var local = modules.Except(gone).ToList();
        if (local.Count > 0)
        {
            var localNames = string.Join(", ", local.Select(module => module.Name));
            Log(local.Count == 1
                ? localNames + " is not on the Steam Workshop, so Install mods can not download it. Get it from Nexus Mods or ModDB, put its folder in the game's Modules folder, then click Check mods."
                : localNames + " are not on the Steam Workshop, so Install mods can not download them. Get them from Nexus Mods or ModDB, put their folders in the game's Modules folder, then click Check mods.");
            // MCM since its Workshop item was removed: name the page.
            foreach (var module in local)
            {
                if (BannerlordCoopLauncher.Core.Joining.ServerModNeeds.NexusPage(module.ModuleId) is { } page)
                    Log(module.Name + " is on Nexus Mods: " + page);
            }
        }
    }

    private bool IsPublicOnWorkshop(string workshopId) =>
        _remote != null && _remote.TryGetValue(workshopId, out var published) && published.Result == 1;

    private static bool IsSteamWorkshopId(string? id) =>
        !string.IsNullOrWhiteSpace(id) && id!.Length > 1 && id.All(char.IsDigit) && !BannerlordCoopLauncher.Core.Profile.RetiredWorkshopItems.IsRetired(id);

    private async void Connect_Click(object sender, RoutedEventArgs e) => await LaunchAsync(diagnostic: false);

    private async void Diagnostic_Click(object sender, RoutedEventArgs e) => await LaunchAsync(diagnostic: true);

    private void ModuleFolder_Click(object sender, RoutedEventArgs e)
    {
        var gameRoot = GameRoot.Normalize(GamePathBox.Text, _files);
        var modules = gameRoot == null ? null : Path.Combine(gameRoot, "Modules");
        if (modules != null && Directory.Exists(modules))
        {
            Process.Start(new ProcessStartInfo(modules) { UseShellExecute = true });
            return;
        }

        Log("Install Mount & Blade II: Bannerlord in Steam, then open this again.");
    }

    private void Unblock_Click(object sender, RoutedEventArgs e)
    {
        var gameRoot = GameRoot.Normalize(GamePathBox.Text, _files);
        var modules = gameRoot == null ? null : Path.Combine(gameRoot, "Modules");
        if (modules == null || !Directory.Exists(modules))
        {
            Log("Install Mount & Blade II: Bannerlord in Steam, then open this again.");
            return;
        }

        try
        {
            var cleared = DownloadMark.Clear(modules);
            StopUnblockAlert();
            var detail = cleared == 0
                ? "No files in Modules were blocked."
                : "Cleared the download block on " + cleared + " files in Modules.";
            NoteAction("Unblock DLLs", detail);
            Log(detail);
        }
        catch (Exception ex)
        {
            Log(ex.Message);
        }
    }

    private void ClearShaders_Click(object sender, RoutedEventArgs e)
    {
        var programData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Mount and Blade II Bannerlord");
        try
        {
            var deleted = ShaderCache.Clear(programData, Process.GetProcessesByName("Bannerlord").Length > 0);
            Log(deleted == 0
                ? "There was no shader cache to delete."
                : "Deleted the shader cache. The game builds it again the next time it starts. Saves were not touched.");
        }
        catch (Exception ex)
        {
            Log(ex.Message);
        }
    }

    private void ClearUnusedMcm_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var keep = _rows.Where(row => row.IsOn).Select(row => row.ModuleId)
                .Concat(_profile?.BaseModules ?? Enumerable.Empty<string>())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var result = McmLeftovers.ClearUnused(keep);
            if (result.Removed == 0)
            {
                Log("No unused optional MCM configs were found under Documents\\Mount and Blade II Bannerlord\\Configs\\ModSettings.");
                NoteAction("Clear unused MCM", "Nothing to remove.");
                return;
            }

            Log("Removed " + result.Removed + " leftover MCM config" + (result.Removed == 1 ? "" : "s") + ": "
                + string.Join(", ", result.Names) + ". Make a fresh single-player save if MCM menus still look wrong, then load settings from that save.");
            NoteAction("Clear unused MCM", "Removed " + string.Join(", ", result.Names) + ".");
        }
        catch (Exception ex)
        {
            Log(ex.Message);
            NoteAction("Clear unused MCM", ex.Message);
        }
    }

    // The small arrow buttons open their menu on a normal click, under the button.
    private void OpenMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { ContextMenu: { } menu } button)
            return;
        menu.PlacementTarget = button;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private void CrashLogs_Click(object sender, RoutedEventArgs e)
    {
        var gameRoot = GameRoot.Normalize(GamePathBox.Text, _files);
        var clientBin = gameRoot == null ? null : Path.Combine(gameRoot, "bin", "Win64_Shipping_Client");
        var clientLog = clientBin == null ? null : Path.Combine(clientBin, "Coop_client.log");
        var actionLog = _actions.FilePath;
        if (clientBin != null && Directory.Exists(clientBin))
        {
            if (File.Exists(clientLog) || (actionLog != null && File.Exists(actionLog)))
            {
                LogFolderOpen.Select(new[] { clientLog, actionLog });
                NoteAction("Open client logs", "Opened Coop_client.log and client-action.log.");
                Log("Opened Coop_client.log and client-action.log. Send my logs also packs crash reports and hard crashes.");
            }
            else
            {
                Process.Start(new ProcessStartInfo(clientBin) { UseShellExecute = true });
                NoteAction("Open client logs", "Opened the game log folder.");
                Log("Opened the game log folder.");
            }

            return;
        }

        var reports = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Mount and Blade II Bannerlord",
            "Coop Crash Reports");
        Directory.CreateDirectory(reports);
        Process.Start(new ProcessStartInfo(reports) { UseShellExecute = true });
        NoteAction("Open client logs", "Game log folder was missing. Opened Coop Crash Reports.");
        Log("Game log folder was missing. Opened Coop Crash Reports.");
    }
}
