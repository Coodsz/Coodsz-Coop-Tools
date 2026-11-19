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
using BurningEmpires.Core;
using Microsoft.Win32;
using TaomCoop.Core;

namespace BurningEmpiresHost;

// The host side of the Burning Empires mod setups. Every setup starts the same way: the Workshop
// Coop dedicated server in the tool's server folder, with the Burning Empires core staged into
// engine\Modules (BeServerPath.StageCore), the setup's extras copied next to it, the order written
// for Start-ModdedServer.ps1 (BeServerPath.WriteOrder) and the script started (BeServerLaunch).
public partial class MainWindow
{
    private static readonly SolidColorBrush RequiredBrush = new(Color.FromRgb(0x7D, 0xAA, 0xA4));
    private static readonly SolidColorBrush ExtraBrush = new(Color.FromRgb(0x8F, 0xBF, 0x86));
    private static readonly SolidColorBrush NotLoadedBrush = new(Color.FromRgb(0xD2, 0xA3, 0x6A));
    private bool _fillingModSetups;
    private IReadOnlyList<FoundMod>? _hostMods;

    private TaomSetup HostSetup => _settings.ModSetups.Current();

    // ----- The Burning Empires compatibility list -----

    private BeCompatList _compat = BeCompatList.BuiltIn();

    private static string CompatCachePath => ToolData.File(BeCompatList.FileName);

    private void LoadCompatList()
    {
        _compat = BeCompatList.Load(CompatCachePath);
        _ = RefreshCompatListAsync();
    }

    private async Task RefreshCompatListAsync()
    {
        using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        var online = await BeCompatList.DownloadAsync(http, CompatCachePath, CancellationToken.None);
        if (online == null)
            return;
        _compat = online;
        Dispatcher.Invoke(ShowModSetups);
    }

    internal static Color CompatColor(CompatTag tag) => BeCompatList.BadgeColor(tag) switch
    {
        "green" => Color.FromRgb(0x8F, 0xBF, 0x86),
        "red" => Color.FromRgb(0xE0, 0x7A, 0x6E),
        "blue" => Color.FromRgb(0x8E, 0xB4, 0xD4),
        _ => Color.FromRgb(0xE2, 0xC2, 0x7A)
    };

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
            BeModResults.ImportServerTests(ModLoadTest.LoadResults(ModTestResultsPath).Select(result => BeModResults.FromServerTest(result)));
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
            _hostMods = CustomModCatalog.Scan(_gameRoot == null ? "" : Path.Combine(_gameRoot, "Modules"), workshops);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or InvalidOperationException)
        {
            _hostMods = Array.Empty<FoundMod>();
        }

        return _hostMods;
    }

    // What the server needs for a setup: the server and client orders from the load order rules,
    // with the ids the mods on this PC really have. Null when the setup breaks the core order.
    private BeServerPlan? ServerPlan(TaomSetup setup, out string? problem)
    {
        problem = null;
        try
        {
            var resolved = BeModIds.Resolve(setup, HostMods());
            return BeServerPath.Plan(resolved, BeLoadOrder.ServerOrder(resolved), BeLoadOrder.ClientOrder(resolved), HostMods());
        }
        catch (InvalidOperationException ex)
        {
            problem = ex.Message;
            return null;
        }
    }

    // The name a core row shows.
    private static string CoreName(string id)
    {
        if (id.Equals(BeStack.CoopModuleId, StringComparison.OrdinalIgnoreCase))
            return "Bannerlord Coop";
        if (id.Equals(BeStack.CompatModuleId, StringComparison.OrdinalIgnoreCase))
            return BeStack.CompatName;
        if (id.Equals(BeStack.BeModuleId, StringComparison.OrdinalIgnoreCase))
            return BeStack.BeName;
        if (id.Equals(BeStack.ElephantsModuleId, StringComparison.OrdinalIgnoreCase))
            return BeStack.ElephantsName;
        return BeStack.Butr.FirstOrDefault(mod => mod.ModuleId.Equals(id, StringComparison.OrdinalIgnoreCase))?.Name ?? id;
    }

    private void ShowModSetups()
    {
        ImportServerTestResults();
        _settings.ModSetups ??= new BeSetupBook();
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
        LockedSetupText.Text = BeSetupBook.LockedNote(current.Name);
        // The server runs or starts: the mod list waits until it stops (ModListBlock).
        ShowModListBlock(locked);
        var blocked = ModListRunning;
        var unsaved = HasUnsavedHostMods;
        HostModsSaveHint.Visibility = unsaved ? Visibility.Visible : Visibility.Collapsed;
        ShareModListHint.Visibility = !locked && (unsaved || current.Name.Equals(_hostEditedSetup, StringComparison.OrdinalIgnoreCase))
            ? Visibility.Visible
            : Visibility.Collapsed;

        OrderList.Items.Clear();
        var plan = ServerPlan(current, out var orderProblem);
        ExtrasNotLoadedBar.Visibility = orderProblem != null ? Visibility.Visible : Visibility.Collapsed;
        ExtrasNotLoadedText.Text = orderProblem ?? "";
        // The ids the mods on this PC really have; the extras keep their places, so a row's index
        // still points at the mod in the working copy.
        var shown = plan?.Setup ?? (current.Extras.Count > 0 ? BeModIds.Resolve(current, HostMods()) : current);
        var number = 1;
        // Each mod's version is read once per redraw (RowVersion), and the reports carry the mods the server
        // loads for the setup on screen, in order, with those versions.
        _rowVersions.Clear();
        _rowSources = null;
        var loadOrder = BeModChips.ServerReportOrder(shown,
            id => plan?.Extras.Any(item => item.Mod.ModuleId.Equals(id, StringComparison.OrdinalIgnoreCase)) == true, RowVersion);
        // The side Mods card counts the loaded mods: the core is Always on, an extra Works when the list says so.
        var tally = new List<BannerlordCoopLauncher.Core.Modules.TallyRow>();
        foreach (var row in BeLoadOrder.ServerRows(shown))
        {
            var id = row.ModuleId;
            if (!row.IsCore)
            {
                var extra = shown.Extras[row.ExtraIndex];
                var own = current.Extras[row.ExtraIndex];
                var staged = extra.Enabled ? plan?.Extras.FirstOrDefault(item => item.Mod.ModuleId.Equals(id, StringComparison.OrdinalIgnoreCase)) : null;
                var entry = _compat.Find(extra.ModuleId, extra.WorkshopId, extra.Label);
                tally.Add(new BannerlordCoopLauncher.Core.Modules.TallyRow(extra.ModuleId, staged != null, false, TaomCompatList.Works(entry?.Kind)));
                var note = !extra.Enabled
                    ? "Off"
                    : staged == null
                        ? entry is { HasServerBuild: true } ? "Not on this PC. It has a server build" : "Not on this PC"
                        : TaomServerPath.RowNote(staged.Code);
                var version = RowVersion(extra.ModuleId);
                OrderList.Items.Add(new SealedModuleRow(staged == null ? null : number++, extra.Label, note, staged == null ? NotLoadedBrush : ExtraBrush)
                {
                    Status = BeModChips.Status(_compat, extra.ModuleId, extra.WorkshopId, extra.Label, ModReports.HasEndpoint(), BannerlordCoopLauncher.Core.Localization.Translator.T),
                    Mine = BeModResults.IsExtraMod(extra.ModuleId) ? BeModResults.Pill(extra.ModuleId, version)?.Translated() : null,
                    Reports = ReportChips(extra.ModuleId, extra.WorkshopId, extra.Label, version, loadOrder),
                    ModuleId = own.ModuleId,
                    Editable = !locked,
                    Blocked = blocked,
                    IsOn = own.Enabled
                });
                continue;
            }

            // A core row gets this PC's result, the counts and the Report link too (not the list status), like the
            // TAOM server tool's core rows; Coop and the official modules get none.
            var coreVersion = RowVersion(id);
            tally.Add(new BannerlordCoopLauncher.Core.Modules.TallyRow(id, true, true));
            OrderList.Items.Add(new SealedModuleRow(number++, CoreName(id), BannerlordCoopLauncher.Core.Modules.ModChips.AlwaysOn, RequiredBrush)
            {
                Tip = BeSetupBook.CoreRowTip,
                Mine = ModReports.Rates(id) ? BeModResults.Pill(id, coreVersion)?.Translated() : null,
                Reports = ReportChips(id, null, CoreName(id), coreVersion, loadOrder)
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
        var extras = HostSetup.EnabledExtras.ToList();
        NoteLog(extras.Count > 0
            ? HostSetup.Name + " is picked. Start server starts Burning Empires plus " + string.Join(", ", extras.Select(mod => mod.Label)) + "."
            : HostSetup.Name + " is picked. Players join with the Burning Empires core only.");
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

    // The copy the next Start copies from comes first (BeServerPath.StartSource: the BUTR Workshop items, the
    // MCM Start picks, the bundled compat module, Burning Empires from the game's Modules folder, an extra as
    // ServerPlan picks it). The server's copy is only refreshed on Start, so reading it first shared old
    // versions after a Workshop update. Coop and the game modules come with the server folder, so for them,
    // and for a module Start can't find here, the server's copy comes first and the game's Modules folder last.
    private (string Name, string Version, string? Directory)? DescribeServerModule(string moduleId)
    {
        var source = BeServerPath.StartSource(moduleId, CoreSources(), HostMods());
        var xml = source == null ? null : Path.Combine(source, "SubModule.xml");
        if (xml != null && !File.Exists(xml))
            xml = null;
        var modules = string.IsNullOrWhiteSpace(_settings.ServerRoot) ? null : Path.Combine(_settings.ServerRoot, "engine", "Modules");
        if (xml == null && modules != null)
            xml = FindServerModuleXml(modules, moduleId);
        if (xml == null && _gameRoot != null)
            xml = FindServerModuleXml(Path.Combine(_gameRoot, "Modules"), moduleId);
        if (xml == null)
            return null;
        return (moduleId, ModuleIdentityReader.Read(xml).Version, Path.GetDirectoryName(xml));
    }

    private void ShareModList_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Filter = "Mod list|*.json", FileName = "coodsz-be-mod-list.json" };
        if (dialog.ShowDialog(this) != true)
            return;
        try
        {
            SharedModList.Write(dialog.FileName, BeSharedList.Entries(HostSetup, DescribeServerModule));
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

        // A mod list from another overhaul (TAOM) is refused before any setup is made for it.
        var foreign = BeSharedList.ForeignGame(shared);
        if (foreign != null)
        {
            NoteLog(foreign);
            Coodsz.Ui.UiLanguage.Message(foreign, "Import mod list", MessageBoxButton.OK, MessageBoxImage.Warning, this);
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

        TaomImportResult result;
        try
        {
            result = BeSharedList.Import(setup, shared, onThisPc);
        }
        catch (InvalidOperationException ex)
        {
            NoteLog(ex.Message);
            Coodsz.Ui.UiLanguage.Message(ex.Message, "Import mod list", MessageBoxButton.OK, MessageBoxImage.Warning, this);
            return;
        }

        if (result.Added.Count > 0)
            summary.Add("Added " + string.Join(", ", result.Added) + " to " + setup.Name + ".");
        if (result.TurnedOff.Count > 0)
            summary.Add("Turned off " + string.Join(", ", result.TurnedOff) + ", because the list does not have them.");
        if (result.Refused.Count > 0)
            summary.Add("Left out " + string.Join(", ", result.Refused) + ", because Burning Empires already has them or they do not work with Burning Empires.");
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
            summary.Add("Start server starts Burning Empires plus these mods. Players join with the same setup.");
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
        return BeModFacts.Gather(roots);
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
        var lines = BeAutoSort.Sort(setup, manifests);
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

    // The extras of a setup that are not on this PC, plus the BUTR Workshop items when one is missing and
    // MCM when neither this PC nor the server folder has one the server can take (it comes from Nexus Mods).
    private List<TaomExtraMod> MissingForInstall(TaomSetup setup)
    {
        var missing = ServerPlan(setup, out _)?.Missing.ToList() ?? new List<TaomExtraMod>();
        foreach (var butr in BeServerPath.MissingButr(BannerlordFinder.SteamLibraries(), FindGameRoot(), _settings.ServerRoot))
            missing.Add(new TaomExtraMod { Name = butr.Name, ModuleId = butr.ModuleId, WorkshopId = butr.WorkshopId });
        return missing;
    }

    private string? McmProblem()
    {
        var gameRoot = FindGameRoot();
        return gameRoot == null ? null : BeServerPath.McmProblem(gameRoot, BannerlordFinder.SteamLibraries(), _settings.ServerRoot);
    }

    private static bool IsMcm(TaomExtraMod mod) => mod.ModuleId.Equals(BeStack.Mcm.ModuleId, StringComparison.OrdinalIgnoreCase);

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

        ModStatus("Looking for the mods of " + EditingSetup.Name + " on this PC.");
        HostMods(rescan: true);
        var missing = MissingForInstall(EditingSetup);
        if (missing.Count == 0)
        {
            ShowModSetups();
            ModStatus(EditingSetup.HasEnabledExtras
                ? "Every mod on " + EditingSetup.Name + " is already on this PC."
                : EditingSetup.Name + " has no extra mods to install, and the four helper mods (Harmony, ButterLib, UIExtenderEx, MCM) are on this PC.");
            return;
        }

        var manual = missing.Where(mod => !SharedModList.IsWorkshopId(mod.WorkshopId) && !IsMcm(mod)).Select(mod => mod.Label).ToList();
        if (manual.Count > 0)
            ModStatus("These mods are not on the Steam Workshop. Get " + string.Join(", ", manual) + " from Nexus Mods or ModDB and put the folders in the game's Modules folder.", failed: true);
        if (missing.Any(IsMcm) && McmProblem() is { } mcm)
        {
            ModStatus(mcm, failed: true);
            manual.Add(BeStack.Mcm.Name);
        }
        var names = new Dictionary<ulong, string>();
        foreach (var mod in missing)
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

        // With Steam closed nothing is asked or opened: a steam: link would start Steam on pages the
        // host did not ask for, or fail on a PC with no app for steam: links.
        if (!BeSteamPage.SteamRunning())
        {
            ModStatus("Steam is needed for " + string.Join(", ", names.Values) + ". " + BeSteamPage.SteamNotRunning, failed: true);
            return;
        }

        // The game folder, found the same way the client launcher finds it (the Steam libraries).
        if (FindGameRoot() == null)
        {
            ModStatus("Bannerlord was not found in the Steam libraries on this PC, so Install mods cannot ask Steam for the mods. Subscribe to " + string.Join(", ", names.Values) + " on the Steam Workshop yourself, then click Install mods again.", failed: true);
            await BeSteamPage.OpenAsync(names.Keys.Select(id => id.ToString(System.Globalization.CultureInfo.InvariantCulture)), line => ModStatus(line, failed: true));
            return;
        }

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
                await BeSteamPage.OpenAsync(open.Select(id => id.ToString(System.Globalization.CultureInfo.InvariantCulture)), line => ModStatus(line, failed: true));
            }
        }
        finally
        {
            _busy = false;
        }

        HostMods(rescan: true);
        ShowModSetups();
        var left = MissingForInstall(EditingSetup);
        var still = left.Where(mod => !IsMcm(mod)).Select(mod => mod.Label).ToList();
        if (still.Count > 0)
            ModStatus("Steam is still finishing " + string.Join(", ", still) + ". Click Install mods again in a minute.");
        else if (left.Any(IsMcm) && McmProblem() is { } mcmLeft)
            ModStatus(mcmLeft, failed: true);
        else
            ModStatus("Install mods finished. Those mods are installed.");
    }

    // ----- Start server -----

    // A save remembers the mods it was made with. Adding mods is fine for Bannerlord (they start
    // fresh in the campaign); leaving out a mod the save was made with can break the campaign.
    private bool ConfirmSaveMatchesSetup(string savePath)
    {
        _allowAddedModules = false;
        IReadOnlyList<string> order;
        try
        {
            order = BeLoadOrder.ServerOrder(HostSetup);
        }
        catch (InvalidOperationException ex)
        {
            SetControl(ex.Message, failed: true);
            return false;
        }

        var change = BeServerPath.SaveChange(savePath, order);
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
                default:
                    // No Start anyway here. Start-ModdedServer.ps1 hides a save that needs a mod the
                    // server leaves out, so that start could only end in the script's refusal.
                    return false;
            }
        }

        // The first start with a new mod list keeps a copy of the save, so Restore save backup can undo it.
        try
        {
            var backup = SaveBackup.Create(savePath, SaveBackupFolder);
            var slots = BeSaveData.Backup(BeSaveData.ServerModule(_settings.ServerRoot), Path.GetFileNameWithoutExtension(savePath), Path.ChangeExtension(backup, null));
            NoteLog("Kept a copy of the save before it runs with other mods. Restore save backup on the Saves page puts it back. " + backup
                + (slots > 0 ? " Its Burning Empires save data is kept next to it." : ""));
            // Start-ModdedServer.ps1 asks before it runs a save with mods it was not made with; the host just agreed.
            _allowAddedModules = true;
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

    // Set when the host agreed to add mods to the save and a backup was made.
    private bool _allowAddedModules;

    // The save mods card: friendly names from the setups and the mods on this PC, and the other
    // setups that load every mod the save was made with. Burning Empires setups have no save of their
    // own, so a setup is offered when it loads exactly the save's mods.
    private SaveModCard SaveModsCardFor(string savePath, SaveModChange change)
    {
        var saveModules = CampaignSaveInfoReader.Read(savePath)?.Modules ?? Array.Empty<string>();
        var others = new List<SaveModSetup>();
        foreach (var setup in _settings.ModSetups.Setups.Where(setup => !setup.Name.Equals(HostSetup.Name, StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                others.Add(new SaveModSetup(setup.Name, "", BeLoadOrder.ServerOrder(setup).ToList()));
            }
            catch (InvalidOperationException)
            {
            }
        }

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
                RestoreBeSaveData(AutoBackupDataFolder(dialog.FileName), name);
            }
            else
            {
                undo = SaveBackup.Restore(dialog.FileName, ServerSavesDirectory(), SaveBackupFolder);
                // The Burning Empires data kept next to a manual backup goes back with it.
                RestoreBeSaveData(Path.ChangeExtension(dialog.FileName, null), name);
            }
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

    // Lines about the server folder from before this start (unblocked DLLs, the staged core, the
    // load order). They show on Logs and also go into the run's host-console log.
    private readonly List<string> _startNotes = [];

    private void NoteStart(string line)
    {
        NoteLog(line);
        _startNotes.Add(line);
    }

    // The add-ons this exe carries (its ServerAddOns payload, written out on first use).
    private static string CompatAddOn => Path.Combine(ToolPayload.Folder("ServerAddOns"), BeStack.CompatModuleId);

    private static string UIExtenderExAddOn => Path.Combine(ToolPayload.Folder("ServerAddOns"), "UIExtenderEx-" + BeServerPath.UIExtenderExVersion, BeServerPath.UIExtenderExDll);

    private BeCoreSources CoreSources() =>
        new(_gameRoot ?? "", BannerlordFinder.SteamLibraries(), CompatAddOn, UIExtenderExAddOn) { ServerRoot = _settings.ServerRoot };

    // The Burning Empires server: stages the core and the setup's extras into the server folder,
    // writes the order and the module policy, and returns the start of Start-ModdedServer.ps1.
    // Null when it cannot start; the reason is shown.
    // The Coop server a missing module DLL is put back from, or null (and a line) when the server runs a newer Coop than
    // that one, so an older package never mixes into it (BeServerPrepare.RepairSource).
    private string? ModuleRepairSource(string serverRoot)
    {
        var source = CoopServerSource(BannerlordFinder.SteamLibraries());
        var repair = BeServerPrepare.RepairSource(serverRoot, source);
        if (source != null && repair == null)
            NoteLog(BeServerPrepare.RepairSourceSkipped);
        return repair;
    }

    // The module DLLs the server loads (BeServerModuleRepair, CR-KFRG6 on TAOM): a Workshop module DLL that
    // antivirus took goes back from the Workshop server, and one with a source on this PC that is still
    // missing stops the start. One with no source (an older Coop build) only goes to the action log. The
    // list is kept for the exit check of this run.
    private IReadOnlyList<string> EnsureModuleFiles(string serverRoot, string dataRoot, string? workshop, BeCoreSources sources)
    {
        var workshopFiles = BeServerModuleRepair.WorkshopModuleFiles(workshop);
        var repaired = BeServerModuleRepair.Repair(serverRoot, dataRoot, workshopFiles);
        var files = workshopFiles.Concat(BeServerModuleRepair.StagedModuleFiles(sources)).ToList();
        _runModuleFiles = files;
        var blocking = BeServerModuleRepair.BlockingModuleFiles(serverRoot, files);
        if (repaired.Restored.Count > 0 || blocking.Count > 0)
            _actions.Note("Server files", "Restored=" + string.Join(",", repaired.Restored) + " Missing=" + string.Join(",", blocking));
        if (blocking.Count > 0)
            throw new InvalidOperationException(BeServerModuleRepair.MissingText(blocking));
        var unrestorable = BeServerModuleRepair.MissingModuleFiles(serverRoot, files);
        if (unrestorable.Count > 0)
            _actions.Note("Server files", "Module files missing with no source on this PC, starting anyway: " + string.Join(",", unrestorable));
        return repaired.Restored.Count > 0 ? ["Put back: " + string.Join(", ", repaired.Restored)] : [];
    }

    // The module DLLs of the last staged start, for the exit check (IsMissingModuleFilesStop).
    private IReadOnlyList<(string Target, string Source)> _runModuleFiles = [];

    private async Task<BeServerStart?> PrepareBeServerAsync(string serverRoot, BeStartOptions options)
    {
        var setup = HostSetup;
        var missingPiece = BeServerPrepare.MissingPiece(serverRoot);
        if (missingPiece != null)
        {
            SetControl(missingPiece, failed: true);
            return null;
        }

        if (_gameRoot == null)
        {
            SetControl("Bannerlord was not found, so Burning Empires cannot be copied to the server.", failed: true);
            return null;
        }

        var plan = ServerPlan(setup, out var orderProblem);
        if (plan == null)
        {
            SetControl(orderProblem ?? "The mod order of this setup is not one the server can load.", failed: true);
            return null;
        }

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

        var issues = BeModChecks.Run(plan.Setup, HostModFacts(), _compat);
        foreach (var issue in issues)
            NoteStart(issue.Text);
        var blocking = issues.FirstOrDefault(issue => issue.Blocks);
        if (blocking != null)
        {
            SetControl("Fix the mod problems first. " + blocking.Text, failed: true);
            return null;
        }

        var sources = CoreSources();
        var problems = BeServerPath.CoreProblems(sources);
        if (problems.Count > 0)
        {
            foreach (var problem in problems)
                NoteLog(problem);
            SetControl(BurningEmpires.Ui.BeUiText.FirstProblem(problems, BeLine(sources.GameRoot)), failed: true);
            return null;
        }

        SetControl("Copying Burning Empires and the mods to the server.");
        var workshop = ModuleRepairSource(serverRoot);
        var dataRoot = _settings.DataRoot;
        IReadOnlyList<string> lines;
        try
        {
            lines = await Task.Run(() =>
            {
                // The Coop copy after a Coop update ran at the top of StartServerCoreAsync
                // (RefreshServerFolderAsync), before Coop Save Rescue went into DedicatedServer.Windows.
                var done = new List<string>();
                done.AddRange(BeServerPath.StageCore(serverRoot, sources));
                done.AddRange(BeServerPath.StageExtras(serverRoot, plan));
                BeServerPath.WriteOrder(serverRoot, plan.ServerModules);
                BeServerPrepare.WritePolicy(serverRoot);
                done.AddRange(EnsureModuleFiles(serverRoot, dataRoot, workshop, sources));
                return done;
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            SetControl(ex.Message, failed: true);
            return null;
        }

        foreach (var line in lines)
            NoteStart(line);
        NoteStart("Server load order: " + string.Join(", ", plan.ServerModules) + ".");
        if (options.BattleSize is { } battleSize)
            NoteStart(BeServerLaunch.BattleSizeLine(battleSize));
        try
        {
            // The Game Saves folder is shared, so a save that is not Burning Empires is refused here too.
            return BeServerLaunch.Build(serverRoot, options, BeHostFolders.GameSaves(_settings.DataRoot));
        }
        catch (InvalidOperationException ex)
        {
            SetControl(ex.Message, failed: true);
            return null;
        }
    }
}
