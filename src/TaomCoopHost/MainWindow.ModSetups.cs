using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BannerlordCoopLauncher.Core.IO;
using BannerlordCoopLauncher.Core.Launch;
using BannerlordCoopLauncher.Core.Modules;
using BannerlordCoopLauncher.Core.Profile;
using BannerlordCoopLauncher.Core.Saves;
using BannerlordCoopLauncher.Core.Steam;
using BannerlordCoopLauncher.Core.Support;
using Microsoft.Win32;
using TaomCoop.Core;

namespace TaomCoopHost;

// The host side of the TAOM mod setups. Plain TAOM starts the sealed TAOM-Coop server
// (BannerlordCoopServer.exe, its module list fixed inside). A setup with extra mods starts the
// Coodsz TAOM server: the same prepared server folder's engine, started directly with
// TaomLoadOrder.ServerOrder (see TaomCoop.Core.TaomServerPath).
public partial class MainWindow
{
    private static readonly SolidColorBrush RequiredBrush = new(Color.FromRgb(0x7D, 0xAA, 0xA4));
    private static readonly SolidColorBrush ExtraBrush = new(Color.FromRgb(0x8F, 0xBF, 0x86));
    private static readonly SolidColorBrush NotLoadedBrush = new(Color.FromRgb(0xD2, 0xA3, 0x6A));
    private bool _fillingModSetups;
    private IReadOnlyList<FoundMod>? _hostMods;

    private TaomSetup HostSetup => _settings.ModSetups.Current();

    // ----- The TAOM compatibility list -----

    private TaomCompatList _compat = TaomCompatList.BuiltIn();

    private static string CompatCachePath => ToolData.File(TaomCompatList.FileName);

    private void LoadCompatList()
    {
        _compat = TaomCompatList.Load(CompatCachePath);
        _ = RefreshCompatListAsync();
    }

    private async Task RefreshCompatListAsync()
    {
        using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        var online = await TaomCompatList.DownloadAsync(http, CompatCachePath, CancellationToken.None);
        if (online == null)
            return;
        _compat = online;
        Dispatcher.Invoke(ShowModSetups);
    }

    internal static Color CompatColor(CompatTag tag) => TaomCompatList.BadgeColor(tag) switch
    {
        "green" => Color.FromRgb(0x8F, 0xBF, 0x86),
        "red" => Color.FromRgb(0xE0, 0x7A, 0x6E),
        "blue" => Color.FromRgb(0x8E, 0xB4, 0xD4),
        _ => Color.FromRgb(0xE2, 0xC2, 0x7A)
    };

    // The neutral grey of "Unknown, not tested", for a mod the TAOM list does not know.
    internal static readonly Color UnknownGrey = Color.FromRgb(0x9A, 0xA6, 0xB0);

    private static string ManifestVersion(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
            return "";
        try
        {
            return Directory.Exists(directory) ? BannerlordCoopLauncher.Core.Parsing.ModuleManifestReader.Read(directory)?.Version ?? "" : "";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            return "";
        }
    }

    // The results of "Test mods on the server" this PC kept before mod-results.json existed, so
    // the Modules rows show them as Passed or Failed on your server.
    private void ImportServerTestResults()
    {
        if (string.IsNullOrWhiteSpace(_settings.DataRoot))
            return;
        try
        {
            ModResults.ImportServerTests(ModLoadTest.LoadResults(ModTestResultsPath).Select(result => TaomModResults.FromServerTest(result)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
        }
    }

    // Mods on this PC the server can be given: Steam Workshop items and the game's Modules folder.
    private IReadOnlyList<FoundMod> HostMods(bool rescan = false)
    {
        if (_hostMods != null && !rescan)
            return _hostMods;
        try
        {
            var workshops = SteamInstallFinder.ReadWorkshopLibraries(BannerlordFinder.SteamLibraries(), new PhysicalFileSystem());
            var scan = CustomModCatalog.Scan(_gameRoot == null ? "" : Path.Combine(_gameRoot, "Modules"), workshops);
            // TAOM plus mods the tool carries, when this PC has no copy of its own. The first start writes them
            // out on a worker thread; until then the list goes without them and fills in once they are ready.
            var bundled = ToolPayload.Peek(ToolPayload.BundledMods);
            if (bundled == null)
            {
                ReloadWhenBundledReady();
                return scan;
            }

            _hostMods = scan.Concat(TaomPlus.BundledNextTo(bundled, scan)).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or InvalidOperationException)
        {
            _hostMods = Array.Empty<FoundMod>();
        }

        return _hostMods;
    }

    private bool _bundledReloadStarted;

    // Shows the mod setups again once the bundled TAOM plus mods are written out, so they join the list.
    // At most once a session, and only when the write-out worked: a failed one falls back to the folder
    // beside the exe for a while (ToolPayload.RetryAfter), so the list never reloads in a loop.
    private void ReloadWhenBundledReady()
    {
        if (_bundledReloadStarted)
            return;
        _bundledReloadStarted = true;
        ToolPayload.FolderAsync(ToolPayload.BundledMods).ContinueWith(task =>
        {
            if (task.IsCompletedSuccessfully && ToolPayload.IsWrittenOut(ToolPayload.BundledMods, task.Result))
                Dispatcher.BeginInvoke(() =>
                {
                    _hostMods = null;
                    ShowModSetups();
                });
        });
    }

    private void ShowModSetups()
    {
        ImportServerTestResults();
        _settings.ModSetups ??= new TaomSetupBook();
        _settings.ModSetups.Ensure();
        _fillingModSetups = true;
        ModSetupBox.Items.Clear();
        foreach (var setup in _settings.ModSetups.Setups)
        {
            var item = new ComboBoxItem { Content = setup.Name, Tag = setup.Name };
            ModSetupBox.Items.Add(item);
            if (setup.Name.Equals(_settings.ModSetups.Active, StringComparison.OrdinalIgnoreCase))
                ModSetupBox.SelectedItem = item;
        }

        _fillingModSetups = false;
        // The list shows the working copy: the host's changes show at once, and Save mods keeps them.
        var current = EditingSetup;
        var locked = current.Locked;
        DeleteModSetupButton.IsEnabled = !locked;
        AutoSortHostButton.IsEnabled = !locked;
        RenameHostSetupButton.IsEnabled = !locked;
        AddHostModsButton.IsEnabled = !locked;
        SaveHostModsButton.IsEnabled = !locked;
        if (locked)
            RenameHostSetupPanel.Visibility = Visibility.Collapsed;
        LockedSetupTag.Visibility = locked ? Visibility.Visible : Visibility.Collapsed;
        LockedSetupText.Visibility = locked ? Visibility.Visible : Visibility.Collapsed;
        LockedSetupText.Text = TaomSetupBook.LockedNote(current.Name);
        // The server runs or starts: the mod list waits until it stops (ModListBlock).
        ShowModListBlock(locked);
        var blocked = ModListRunning;
        var unsaved = HasUnsavedHostMods;
        HostModsSaveHint.Visibility = unsaved ? Visibility.Visible : Visibility.Collapsed;
        ShareModListHint.Visibility = !locked && (unsaved || current.Name.Equals(_hostEditedSetup, StringComparison.OrdinalIgnoreCase))
            ? Visibility.Visible
            : Visibility.Collapsed;
        var coodsz = TaomServerPath.Needed(current);
        ExtrasNotLoadedBar.Visibility = coodsz ? Visibility.Visible : Visibility.Collapsed;

        OrderList.Items.Clear();
        var plan = coodsz ? TaomServerPath.Plan(current, HostMods()) : null;
        // The ids the mods on this PC really have; the extras keep their places, so a row's index
        // still points at the mod in the working copy.
        var shown = plan?.Setup ?? (current.Extras.Count > 0 ? TaomModIds.Resolve(current, HostMods()) : current);
        var number = 1;
        // The mods the server loads, in order, for the reports; filled as the rows are added.
        var loadOrder = new List<(string Id, string? Version)>();
        // The side Mods card counts the loaded mods: the core is Always on, an extra Works when the list says so.
        var tally = new List<BannerlordCoopLauncher.Core.Modules.TallyRow>();
        foreach (var row in TaomLoadOrder.ServerRows(shown))
        {
            var id = row.ModuleId;
            if (!row.IsCore)
            {
                var extra = shown.Extras[row.ExtraIndex];
                var own = current.Extras[row.ExtraIndex];
                var staged = extra.Enabled ? plan?.Extras.FirstOrDefault(item => item.Mod.ModuleId.Equals(id, StringComparison.OrdinalIgnoreCase)) : null;
                var entry = _compat.Find(extra.ModuleId, extra.WorkshopId, extra.Label);
                tally.Add(new BannerlordCoopLauncher.Core.Modules.TallyRow(extra.ModuleId, staged != null, false, TaomCompatList.Works(entry?.Kind)));
                var badge = entry == null ? null : TaomCompatList.BadgeText(entry.Kind);
                var note = !extra.Enabled
                    ? "Off"
                    : staged == null
                        ? entry is { HasServerBuild: true } ? "Not on this PC. It has a server build" : "Not on this PC"
                        : TaomServerPath.RowNote(staged.Code);
                var unknown = TaomModResults.ShowsUnknown(_compat, extra.ModuleId, extra.WorkshopId, extra.Label);
                var onPc = HostMods().FirstOrDefault(mod => mod.ModuleId.Equals(extra.ModuleId, StringComparison.OrdinalIgnoreCase));
                var version = ManifestVersion(onPc?.Directory);
                if (staged != null)
                    loadOrder.Add((extra.ModuleId, version));
                OrderList.Items.Add(new SealedModuleRow(staged == null ? null : number++, extra.Label, note, staged == null ? NotLoadedBrush : ExtraBrush)
                {
                    Badge = unknown ? UnknownModBadge.Text : badge ?? "",
                    BadgeBrush = unknown ? new SolidColorBrush(UnknownGrey) : entry == null ? null : new SolidColorBrush(CompatColor(entry.Kind)),
                    BadgeNote = unknown
                        ? BannerlordCoopLauncher.Core.Localization.Translator.T(UnknownModBadge.Tip)
                        : entry == null ? null : TaomCompatList.Hover(entry, BannerlordCoopLauncher.Core.Localization.Translator.T),
                    Mine = TaomModResults.IsExtraMod(extra.ModuleId) ? ModResults.Pill(extra.ModuleId, version)?.Translated() : null,
                    Reports = ReportChips(extra.ModuleId, extra.WorkshopId, extra.Label, version, loadOrder),
                    ModuleId = own.ModuleId,
                    Editable = !locked,
                    Blocked = blocked,
                    IsOn = own.Enabled
                });
                continue;
            }

            var coreVersion = HostModVersion(id);
            tally.Add(new BannerlordCoopLauncher.Core.Modules.TallyRow(id, true, true));
            loadOrder.Add((id, coreVersion));
            OrderList.Items.Add(new SealedModuleRow(number++, id == TaomStack.CoopModuleId ? "Bannerlord Coop" : id, BannerlordCoopLauncher.Core.Modules.ModChips.AlwaysOn, RequiredBrush)
            {
                Tip = TaomSetupBook.CoreRowTip,
                Reports = ReportChips(id, null, id, coreVersion, loadOrder)
            });
        }

        ModsSummaryCard.Show(BannerlordCoopLauncher.Core.Modules.ModTally.Count(tally));
        ModuleSummaryText.Text = (number - 1).ToString();
    }

    private void ModSetupBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_fillingModSetups || ModSetupBox.SelectedItem is not ComboBoxItem { Tag: string name })
            return;
        if (name.Equals(_settings.ModSetups.Active, StringComparison.OrdinalIgnoreCase))
            return;
        // While the server only runs, a pick is for the next Start; while it starts or an action runs, the box goes back.
        if (RefuseSetupChangeWhileBusy() || !ConfirmHostModChanges())
        {
            ShowModSetups();
            return;
        }

        _settings.ModSetups.Switch(name);
        _hostEditing = null;
        SaveSettings();
        ShowModSetups();
        NoteLog(HostSetup.HasEnabledExtras
            ? HostSetup.Name + " is picked. Start server starts the Coodsz TAOM server with the TAOM core plus " + string.Join(", ", HostSetup.EnabledExtras.Select(mod => mod.Label)) + "."
            : HostSetup.Name + " is picked. Players join with the TAOM core only.");
    }

    private void DeleteModSetup_Click(object sender, RoutedEventArgs e)
    {
        var setup = HostSetup;
        if (setup.Locked || RefuseSetupChangeWhileBusy())
            return;
        if (Coodsz.Ui.UiLanguage.Message("Delete " + setup.Name + " from the server tool?", "Delete setup", MessageBoxButton.OKCancel, MessageBoxImage.Warning, this) != MessageBoxResult.OK)
            return;
        _settings.ModSetups.Delete(setup.Name);
        _hostEditing = null;
        SaveSettings();
        ShowModSetups();
        NoteLog(setup.Name + " was deleted. " + HostSetup.Name + " is picked.");
    }

    private (string Name, string Version, string? Directory)? DescribeServerModule(string moduleId)
    {
        var modules = string.IsNullOrWhiteSpace(_settings.ServerRoot) ? null : Path.Combine(_settings.ServerRoot, "engine", "Modules");
        var xml = modules == null ? null : FindServerModuleXml(modules, moduleId);
        if (xml == null && _gameRoot != null)
            xml = FindServerModuleXml(Path.Combine(_gameRoot, "Modules"), moduleId);
        if (xml == null)
            return null;
        return (moduleId, ModuleIdentityReader.Read(xml).Version, Path.GetDirectoryName(xml));
    }

    private void ShareModList_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Filter = "Mod list|*.json", FileName = "coodsz-mod-list.json" };
        if (dialog.ShowDialog(this) != true)
            return;
        try
        {
            SharedModList.Write(dialog.FileName, TaomSharedList.Entries(HostSetup, DescribeServerModule));
            NoteLog("Shared mod list saved for " + HostSetup.Name + ". Players click Import mod list in the client launcher and pick that file.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            NoteLog(ex.Message);
        }
    }

    private void ImportModList_Click(object sender, RoutedEventArgs e)
    {
        // The server runs or starts: the Mods page banner says to stop it first (ModListBlock).
        if (RefuseWhileModListRuns())
            return;
        if (!ConfirmHostModChanges())
            return;
        var dialog = new OpenFileDialog { Filter = "Mod list|*.json" };
        if (dialog.ShowDialog(this) != true)
            return;

        IReadOnlyList<SharedModEntry> shared;
        try
        {
            shared = SharedModList.Read(File.ReadAllText(dialog.FileName));
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Text.Json.JsonException or IOException)
        {
            NoteLog("That file is not a Coodsz mod list.");
            return;
        }

        IReadOnlyList<FoundMod> onThisPc;
        try
        {
            var workshops = SteamInstallFinder.ReadWorkshopLibraries(BannerlordFinder.SteamLibraries(), new PhysicalFileSystem());
            onThisPc = CustomModCatalog.Scan(_gameRoot == null ? "" : Path.Combine(_gameRoot, "Modules"), workshops);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or InvalidOperationException)
        {
            onThisPc = Array.Empty<FoundMod>();
        }

        var summary = new List<string>();
        var setup = HostSetup;
        if (setup.Locked)
        {
            var from = setup.Name;
            setup = _settings.ModSetups.Create();
            summary.Add("Made " + setup.Name + " for this list, because " + from + " is locked.");
        }

        var result = TaomSharedList.Import(setup, shared, onThisPc);
        if (result.Added.Count > 0)
            summary.Add("Added " + string.Join(", ", result.Added) + " to " + setup.Name + ".");
        if (result.TurnedOff.Count > 0)
            summary.Add("Turned off " + string.Join(", ", result.TurnedOff) + ", because the list does not have them.");
        if (result.Refused.Count > 0)
            summary.Add("Left out " + string.Join(", ", result.Refused) + ", because TAOM already has them or they do not work with TAOM.");
        if (result.NoLink.Count > 0)
            summary.Add("Not on this PC and the list has no Workshop link, so get these yourself: " + string.Join(", ", result.NoLink) + ".");
        foreach (var entry in shared)
        {
            var have = DescribeServerModule(entry.Id);
            if (have == null)
                continue;
            var warning = SharedModList.VersionWarning(entry.Name, entry.Version, have.Value.Version);
            if (warning != null)
                summary.Add(warning);
        }

        if (setup.HasEnabledExtras)
            summary.Add("Start server starts the Coodsz TAOM server with the TAOM core plus these mods. Players join with the same setup.");
        else if (summary.Count == 0)
            summary.Add("This setup already matches that list.");

        _hostEditing = null;
        SaveSettings();
        ShowModSetups();
        foreach (var line in summary)
            NoteLog(line);
        Coodsz.Ui.UiLanguage.Message(string.Join("\n\n", summary), "Import mod list", MessageBoxButton.OK, MessageBoxImage.Information, this);
    }

    // ----- Auto sort and checks -----

    private IReadOnlyDictionary<string, TaomModFact> HostModFacts()
    {
        var roots = new List<string>();
        try
        {
            roots.AddRange(SteamInstallFinder.ReadWorkshopLibraries(BannerlordFinder.SteamLibraries(), new PhysicalFileSystem()).Select(library => library.ContentDirectory));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or InvalidOperationException)
        {
        }

        if (_gameRoot != null)
            roots.Add(Path.Combine(_gameRoot, "Modules"));
        return TaomModFacts.Gather(roots);
    }

    private void AutoSortHost_Click(object sender, RoutedEventArgs e)
    {
        // The server runs or starts: the Mods page banner says to stop it first (ModListBlock).
        if (RefuseWhileModListRuns())
            return;
        // The same sort as the client launcher, on the working copy. Save mods keeps the new order.
        var setup = EditingSetup;
        if (setup.Locked)
        {
            ModStatus(setup.Name + " is locked. Click New setup or Copy to make a setup you can add mods to.");
            return;
        }

        var manifests = HostModFacts().Where(pair => pair.Value.Manifest != null)
            .ToDictionary(pair => pair.Key, pair => pair.Value.Manifest!, StringComparer.OrdinalIgnoreCase);
        var lines = TaomAutoSort.Sort(setup, manifests);
        ShowModSetups();
        if (lines.Count == 0)
        {
            ModStatus("Auto sort found nothing to change. The extra mods already load in a good order.");
            return;
        }

        foreach (var line in lines)
            NoteLog(line);
        ModStatus("Auto sort changed the order. The Logs page lists each move. Click Save mods to keep the new order.");
    }

    // ----- Install mods on the host -----

    // Install mods shows each step on the Mods page itself (and on Logs), so a click is never silent.
    /// <summary>True (and nothing changes) while Start server prepares or another action holds the tool, since that action
    /// reads the active setup. The header picker is on every page, so the Dashboard line says it too.</summary>
    private bool RefuseSetupChangeWhileBusy()
    {
        if (!_busy && !_startingServer)
            return false;
        ModStatus(ModListBlock.BusyLine, failed: true);
        SetControl(ModListBlock.BusyLine, failed: true);
        return true;
    }

    private void ModStatus(string line, bool failed = false)
    {
        ModSetupStatus.Text = line;
        ModSetupStatus.Foreground = new SolidColorBrush(failed ? Color.FromRgb(0xE0, 0x7A, 0x6E) : Color.FromRgb(0x7D, 0xAA, 0xA4));
        ModSetupStatus.Visibility = Visibility.Visible;
        NoteLog(line);
    }

    private async void InstallHostMods_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _startingServer)
        {
            ModStatus("Install mods waits until the tool finishes what it is doing now. Click it again in a moment.");
            return;
        }

        // The server runs or starts: the Mods page banner says to stop it first (ModListBlock).
        if (RefuseWhileModListRuns())
            return;

        ModStatus("Looking for the extra mods of " + EditingSetup.Name + " on this PC.");
        await ToolPayload.FolderAsync(ToolPayload.BundledMods);
        var plan = TaomServerPath.Plan(EditingSetup, HostMods(rescan: true));
        if (plan.Missing.Count == 0)
        {
            ShowModSetups();
            ModStatus(EditingSetup.HasEnabledExtras
                ? "Every extra mod on " + EditingSetup.Name + " is already on this PC."
                : EditingSetup.Name + " has no extra mods to install. Install TAOM puts in the TAOM core.");
            return;
        }

        var manual = plan.Missing.Where(mod => !SharedModList.IsWorkshopId(mod.WorkshopId)).Select(mod => mod.Label).ToList();
        if (manual.Count > 0)
            ModStatus("These mods are not on the Steam Workshop. Get " + string.Join(", ", manual) + " from Nexus Mods or ModDB and put the folders in the game's Modules folder.", failed: true);
        var names = new Dictionary<ulong, string>();
        foreach (var mod in plan.Missing)
        {
            if (SharedModList.IsWorkshopId(mod.WorkshopId) && ulong.TryParse(mod.WorkshopId, out var id))
                names.TryAdd(id, mod.Label);
        }

        if (names.Count == 0)
        {
            if (manual.Count == 0)
                ModStatus("Install mods found nothing it can download. The missing mods have no Steam Workshop link.", failed: true);
            return;
        }

        // The game folder, found the same way the client launcher finds it (the Steam libraries).
        if (FindGameRoot() == null)
        {
            FindBannerlordButton.Visibility = Visibility.Visible;
            ModStatus("Bannerlord was not found in the Steam libraries on this PC, so Install mods cannot ask Steam for the mods. Subscribe to " + string.Join(", ", names.Values) + " on the Steam Workshop yourself, then click Install mods again.", failed: true);
            foreach (var id in names.Keys)
            {
                // The status above already says to subscribe by hand, which is what a failed page needs too.
                if (!ShellOpen.TryOpen(ShellOpen.SteamWorkshopPage(id.ToString()), out _))
                    break;

                await Task.Delay(400);
            }

            return;
        }

        var pageFailed = false;
        _busy = true;
        try
        {
            ModStatus("Asking Steam to subscribe and download " + string.Join(", ", names.Values) + ".");
            var download = await WorkshopDownload.RunAsync(_gameRoot!, names, line => Dispatcher.Invoke(() => ModStatus(line)), CancellationToken.None);
            if (download.SteamProblem != null)
                ModStatus("Could not subscribe through Steam by itself. " + download.SteamProblem, failed: true);
            var open = download.OpenByHand(names.Keys.ToList());
            if (open.Count > 0)
            {
                ModStatus("Opening the Workshop pages. Subscribe there and wait for the download in Steam.");
                foreach (var id in open)
                {
                    if (!ShellOpen.TryOpen(ShellOpen.SteamWorkshopPage(id.ToString()), out _))
                    {
                        pageFailed = true;
                        break;
                    }

                    await Task.Delay(400);
                }
            }
        }
        finally
        {
            _busy = false;
        }

        HostMods(rescan: true);
        ShowModSetups();
        var still = TaomServerPath.Plan(EditingSetup, HostMods()).Missing.Select(mod => mod.Label).ToList();
        // A Workshop page that didn't open means nothing is downloading, so the player hears that, not "wait".
        if (still.Count > 0 && pageFailed)
            ModStatus(ShellOpen.SteamPageProblem, failed: true);
        else
            ModStatus(still.Count == 0
                ? "Install mods finished. Those mods are installed."
                : "Steam is still finishing " + string.Join(", ", still) + ". Click Install mods again in a minute.");
    }

    // ----- Start server -----

    // A save remembers the mods it was made with. Adding mods is fine for Bannerlord (they start
    // fresh in the campaign); leaving out a mod the save was made with can break the campaign.
    private bool ConfirmSaveMatchesSetup(string savePath)
    {
        var change = TaomServerPath.SaveChange(savePath, HostSetup);
        if (change.Action == SaveModAction.Start)
            return true;
        if (change.Action == SaveModAction.AskToAdd)
        {
            NoteLog("This setup adds " + string.Join(", ", change.Added) + " to the campaign.");
            var rows = change.AddedRows(id => HostSetup.Extras.FirstOrDefault(mod => mod.ModuleId.Equals(id, StringComparison.OrdinalIgnoreCase))?.Name);
            if (!ConfirmWindow.AskList(this, SaveModChange.AskTitle, change.AskQuestion, rows, SaveModChange.AskBackupNote, change.AskWarning,
                    SaveModChange.AskButton, SaveModChange.AskButtonTip))
                return false;
        }
        else
        {
            NoteLog(change.RefuseText);
            var card = SaveModsCardFor(savePath, change);
            switch (SaveModsWindow.Ask(this, card))
            {
                case "fix" when card.Fix == SaveModFix.UseSetup && _settings.ModSetups.Find(card.SetupName) != null:
                    // The same switch as the setup box, then Start server runs again with that setup.
                    _settings.ModSetups.Switch(card.SetupName);
                    _hostEditing = null;
                    SaveSettings();
                    ShowModSetups();
                    NoteLog(card.SetupName + " is picked. It has every mod " + card.SaveName + " was made with.");
                    _startAgain = true;
                    return false;
                case "othersave":
                    ShowPage("Server");
                    SaveBox.Focus();
                    SaveBox.IsDropDownOpen = true;
                    return false;
                case "anyway":
                    break;
                default:
                    return false;
            }
        }

        // The first start with a new mod list keeps a copy of the save, so Restore save backup can undo it.
        try
        {
            var backup = SaveBackup.Create(savePath, SaveBackupFolder);
            NoteLog("Kept a copy of the save before it runs with other mods. Restore save backup on the Saves page puts it back. " + backup);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            SetControl("Could not keep a copy of the save, so the server was not started. " + ex.Message, failed: true);
            return false;
        }
    }

    // Start server runs again once, after the save mods box switched to the setup the save was made with.
    private bool _startAgain;

    // The save mods card: friendly names from the setups and the mods on this PC, and the other
    // setups that load every mod the save was made with. TAOM setups have no save of their own, so a
    // setup is offered when it loads exactly the save's mods.
    private SaveModCard SaveModsCardFor(string savePath, SaveModChange change)
    {
        var saveModules = CampaignSaveInfoReader.Read(savePath)?.Modules ?? Array.Empty<string>();
        var others = _settings.ModSetups.Setups
            .Where(setup => !setup.Name.Equals(HostSetup.Name, StringComparison.OrdinalIgnoreCase))
            .Select(setup => new SaveModSetup(setup.Name, "", TaomLoadOrder.ServerOrder(setup).ToList()))
            .ToList();
        string? NameOf(string id) =>
            _settings.ModSetups.Setups.SelectMany(setup => setup.Extras)
                .FirstOrDefault(mod => mod.ModuleId.Equals(id, StringComparison.OrdinalIgnoreCase) && mod.Name.Trim().Length > 0)?.Name
            ?? HostMods().FirstOrDefault(mod => mod.ModuleId.Equals(id, StringComparison.OrdinalIgnoreCase))?.Name;
        return SaveModCard.Build(Path.GetFileNameWithoutExtension(savePath), saveModules, change, NameOf,
            id => HostMods().Any(mod => mod.ModuleId.Equals(id, StringComparison.OrdinalIgnoreCase)),
            others, canTurnOn: false, canAddMods: false);
    }

    private string SaveBackupFolder => Path.Combine(_settings.DataRoot, "save-backups");

    private void RestoreSaveBackup_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _startingServer)
        {
            NoteLog("Wait for the current action to finish.");
            return;
        }
        if (ServerIsRunning())
        {
            NoteLog("Stop the server before a save backup is put back.");
            return;
        }

        Directory.CreateDirectory(SaveBackupFolder);
        var dialog = new OpenFileDialog { Filter = "Save backup|*.sav;*.zip", InitialDirectory = SaveBackupFolder };
        if (dialog.ShowDialog(this) != true)
            return;
        var zip = AutoBackupEntry(dialog.FileName);
        var name = zip?.SaveName ?? SaveBackup.SaveNameOf(dialog.FileName);
        if (name == null)
        {
            NoteLog("That file is not a save backup.");
            return;
        }

        if (!ConfirmWindow.Ask(this, "Restore save backup", "Put this backup back as " + name + "? The save as it is now is kept as another backup first.", null, "Restore"))
            return;
        try
        {
            // An automatic zip goes back the safe way: the save as it is now is zipped first,
            // and on any error the old files are put back.
            string? undo;
            if (zip != null)
            {
                undo = AutoBackups().Restore(zip) != null ? AutoBackupFolder : null;
            }
            else
                undo = SaveBackup.Restore(dialog.FileName, ServerSavesDirectory(), SaveBackupFolder);
            ReloadSaves();
            _actions.Note("Backup", "Restored " + name + " from " + Path.GetFileName(dialog.FileName));
            NoteLog("Put the backup back as " + name + "." + (undo == null ? "" : " The save it replaced is kept as a backup too."));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or InvalidDataException or ArgumentException)
        {
            _actions.Note("Backup", "Restore of " + name + " failed: " + ex.Message);
            NoteLog(ex.Message);
        }
    }

    // Lines about the server folder from before this start (unblocked DLLs, the dependency fix,
    // the load order). They show on Logs and also go into the run's host-console log.
    private readonly List<string> _startNotes = [];

    private void NoteStart(string line)
    {
        NoteLog(line);
        _startNotes.Add(line);
    }

    // The Coodsz TAOM server: copies the setup's extras into the server folder and returns the
    // engine start. Null when it cannot start; the reason is shown.
    private async Task<EngineStart?> PrepareCoodszServerAsync(string serverRoot)
    {
        var setup = HostSetup;
        var missingPiece = DedicatedEngineLaunch.MissingPiece(serverRoot);
        if (missingPiece != null)
        {
            SetControl(missingPiece + " Prepare the server again.", failed: true);
            return null;
        }

        // The bundled TAOM plus mods must be written out before the plan looks for them.
        await ToolPayload.FolderAsync(ToolPayload.BundledMods);
        var plan = TaomServerPath.Plan(setup, HostMods(rescan: true));
        if (plan.Missing.Count > 0)
        {
            SetControl("Click Install mods on the Mods page first, because " + string.Join(", ", plan.Missing.Select(mod => mod.Label)) + " is not on this PC yet.", failed: true);
            return null;
        }

        if (plan.JoinProblem != null)
        {
            SetControl(plan.JoinProblem, failed: true);
            return null;
        }

        var issues = TaomModChecks.Run(plan.Setup, HostModFacts(), _compat);
        foreach (var issue in issues)
            NoteStart(issue.Text);
        var blocking = issues.FirstOrDefault(issue => issue.Blocks);
        if (blocking != null)
        {
            SetControl("Fix the mod problems first. " + blocking.Text, failed: true);
            return null;
        }

        NoteLog(TaomServerPath.Explain);
        SetControl("Copying the extra mods to the server.");
        IReadOnlyList<string> lines;
        try
        {
            lines = await Task.Run(() => TaomServerPath.Stage(serverRoot, plan));
            ServerModuleGuard.RejectClientXorberax(serverRoot);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            SetControl(ex.Message, failed: true);
            return null;
        }

        foreach (var line in lines.Concat(TaomServerPath.NexusOnlyNotes(plan)))
            NoteStart(line);
        var coopData = Path.GetDirectoryName(CoopModConfig.UserPath()) ?? "";
        var start = TaomServerPath.Launch(serverRoot, _settings.DataRoot, coopData, plan.Setup);
        NoteStart("Server load order: " + string.Join(", ", start.Modules) + ".");
        return start;
    }
}
