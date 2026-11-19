using System.Diagnostics;
using System.IO;
using System.Windows;
using BannerlordCoopLauncher.Core.Modules;
using BannerlordCoopLauncher.Core.Parsing;
using BannerlordCoopLauncher.Core.Support;

namespace BannerlordCoopLauncher;

// The community mod list badge in the mod list: Works, Broken, Client only or Unknown, with
// the community counts on hover. Clicking it opens the GitHub form to report how the mod
// worked, filled in with the mod, its version and the Coop version.
public partial class MainWindow
{
    private Coodsz.Ui.ModReportBar? _reportBar;

    // Reporting in plain view: Report a mod on Logs and help, Mod crashed? Report it in the Next step card, and the
    // report bar (the one time card, How did your mods work? and the crash bar). Each one opens the report window, which shows what is sent first.
    private void SetUpModReports()
    {
        if (!ModReports.HasEndpoint())
            return;
        _reportBar = new Coodsz.Ui.ModReportBar(this, ReportBarHost, () => ReportRequest(EnabledMods(), afterGame: false, crashed: false), Log)
        {
            Tool = "coop",
            CrashRequest = CrashRequest
        };
        SetUpCrashReports();
        ReportModSlot.Content = Coodsz.Ui.ModReportUi.MainButton(this, _reportBar.OpenCurrent);
        ReportHintSlot.Content = Coodsz.Ui.ModReportUi.Hint(this, _reportBar.OpenCurrent);
        CrashLinkSlot.Content = Coodsz.Ui.ModReportUi.CrashLink(this, _reportBar.OpenCrash);
        Loaded += (_, _) => _reportBar.ShowIntroOnce();
    }

    // The enabled mods of this setup a player can rate, in load order.
    private List<(string Id, string? Version)> EnabledMods() =>
        _resolution?.Modules
            .Where(module => module.Enabled && !module.IsBaseModule && UnknownModBadge.Applies(module.ModuleId))
            .Select(module => (module.ModuleId, (string?)ManifestVersion(module.Directory)))
            .ToList() ?? new List<(string Id, string? Version)>();

    // The mods a crash log names, for the report window to mark as suggested Broken.
    private IReadOnlyList<string> CrashNamed(BannerlordCoopLauncher.Core.Launch.StartupCrashExplain.Capture capture, IEnumerable<string> ids) =>
        BannerlordCoopLauncher.Core.Launch.StartupCrashExplain.NamedMods(capture.ReportText, capture.GameLog, ids
            .Select(id => new BannerlordCoopLauncher.Core.Launch.StartupCrashExplain.ModName(id,
                _rows.FirstOrDefault(row => row.ModuleId.Equals(id, StringComparison.OrdinalIgnoreCase))?.Name is { Length: > 0 } name ? name : id))
            .ToList());

    private void WatchCommunityCompat()
    {
        SetUpModReports();
        CommunityCompat.Changed += () => Dispatcher.BeginInvoke(() =>
        {
            var database = CommunityCompat.Current;
            var records = CompatRecords.Current;
            foreach (var row in _rows)
                row.Compat = CommunityCompat.Badge(row.ModuleId, database, reportTip: ModReports.ReportTip(), records: records, installedVersion: VersionOf(row.ModuleId))?.Translated();
        });
        _ = CommunityCompat.RefreshAsync(LauncherBrand.Name.Replace(" ", ""));
    }

    // The Full mod list link on the mods card: MODS.md in the Coodsz-Coop-and-Friends repository.
    private void FullModList_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(ModCompatDatabase.ModsPageUrl) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
        }
    }

    // With the report endpoint set, the Works, Broken and Works only with buttons (no GitHub
    // account); without it, the GitHub report form in the browser as before.
    private void CompatBadge_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not ModuleRow row)
            return;
        e.Handled = true;
        if (ModReports.HasEndpoint())
        {
            Coodsz.Ui.ModReportWindow.Show(this, ReportRequest(new[] { (row.ModuleId, (string?)VersionOf(row.ModuleId)) }, afterGame: false, crashed: false));
            return;
        }

        try
        {
            var url = CommunityCompat.ReportUrl(row.ModuleId, row.Name, VersionOf(row.ModuleId), CoopVersion(), VersionOf("Native"));
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Coodsz.Ui.UiLanguage.Message("Could not open the browser. " + ex.Message);
        }
    }

    // The version in the mod's SubModule.xml, or empty when the folder is not known.
    private string VersionOf(string moduleId)
    {
        var module = _resolution?.BaseModules.Concat(_resolution.Modules)
            .FirstOrDefault(item => item.ModuleId.Equals(moduleId, StringComparison.OrdinalIgnoreCase));
        return ManifestVersion(module?.Directory);
    }

    // The Bannerlord Coop version for reports: the setup's Coop (or CoopNightly) copy, or, when the
    // setup has Coop off, the Coop module's own SubModule.xml on this PC. Read once per refresh.
    private (ModuleResolution? For, string Version) _coopVersion;

    private string CoopVersion()
    {
        if (VersionOf("Coop") is { Length: > 0 } enabled)
            return enabled;
        if (VersionOf("CoopNightly") is { Length: > 0 } nightly)
            return nightly;
        if (_resolution != null && ReferenceEquals(_coopVersion.For, _resolution))
            return _coopVersion.Version;
        var workshopId = _profile?.Mods.FirstOrDefault(mod => mod.ModuleId.Equals("Coop", StringComparison.OrdinalIgnoreCase))?.WorkshopId;
        var version = ModuleResolver.InstalledVersion("Coop", workshopId is { Length: > 0 } ? workshopId : CoopWorkshopId, _gameRoot, ReadWorkshops(), _files);
        _coopVersion = (_resolution, version);
        return version;
    }

    private const string CoopWorkshopId = "3770450698";

    private static string ManifestVersion(string? directory)
    {
        if (directory is not { Length: > 0 } || !Directory.Exists(directory))
            return "";
        try
        {
            return ModuleManifestReader.Read(directory)?.Version ?? "";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return "";
        }
    }

    // The community list badge in the Add mods window (Unknown, not tested in grey when nobody
    // reported the mod), with this PC's own result next to it.
    private static ModBadge? PickerBadge(BannerlordCoopLauncher.Core.Profile.FoundMod mod)
    {
        var badge = CommunityCompat.Badge(mod.ModuleId, CommunityCompat.Current);
        if (badge == null)
            return null;
        var color = badge.Kind switch
        {
            "Works" => System.Windows.Media.Color.FromRgb(0x8F, 0xBF, 0x86),
            "Broken" => System.Windows.Media.Color.FromRgb(0xE0, 0x7A, 0x6E),
            "ClientOnly" => System.Windows.Media.Color.FromRgb(0x8E, 0xB4, 0xD4),
            _ => ModPills.UnknownGrey
        };
        // The list rows open the report form on a click; this window does not.
        var tip = string.Join("\n", badge.Tip.Split('\n').Where(line => !CommunityCompat.IsReportTip(line)));
        return new ModBadge(badge.Text, color, tip, ModResults.Pill(mod.ModuleId, ModPills.VersionIn(mod.Directory)));
    }

    // The extra mods of the session that is starting, with their versions, for "Worked for you".
    private List<(string Id, string? Version)> SessionExtras()
    {
        if (_resolution == null)
            return new List<(string Id, string? Version)>();
        var working = new HashSet<string>(BannerlordCoopLauncher.Core.Profile.ServerModuleSet.WorkingOrder(IncludeMoreNations), StringComparer.OrdinalIgnoreCase);
        return _resolution.Modules
            .Where(module => module.Enabled && !module.IsBaseModule && !working.Contains(module.ModuleId) && UnknownModBadge.Applies(module.ModuleId))
            .Select(module => (module.ModuleId, (string?)ManifestVersion(module.Directory)))
            .ToList();
    }

    // The report window for these mods, with the enabled mods of this setup in load order.
    private Coodsz.Ui.ModReportRequest ReportRequest(IEnumerable<(string Id, string? Version)> rate, bool afterGame, bool crashed)
    {
        var loadOrder = _resolution?.Modules
            .Where(module => module.Enabled && !module.IsBaseModule)
            .Select(module => (module.ModuleId, (string?)ManifestVersion(module.Directory)))
            .ToList() ?? new List<(string ModuleId, string?)>();
        return new Coodsz.Ui.ModReportRequest
        {
            Tool = "coop",
            ToolVersion = typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "",
            Coop = CoopVersion(),
            Game = VersionOf("Native"),
            Rate = rate
                .Where(mod => UnknownModBadge.Applies(mod.Id))
                .Select(mod => new Coodsz.Ui.ModReportChoice(mod.Id, _rows.FirstOrDefault(row => row.ModuleId.Equals(mod.Id, StringComparison.OrdinalIgnoreCase))?.Name is { Length: > 0 } name ? name : mod.Id, mod.Version ?? ""))
                .ToList(),
            ModList = ModReports.ModList(loadOrder),
            AfterGame = afterGame,
            Crashed = crashed
        };
    }

    // After a join: when the game closes without a crash report, every extra mod of that session
    // worked for this player. Shown as the Worked for you pill next to the list badge. Then the
    // report bar offers to report them: the window when the player turned it on, the crash bar
    // after a crash, or How did your mods work? once per run (ModReports.AfterGame).
    private async Task RecordSessionAsync(Process started, IReadOnlyList<(string Id, string? Version)> extras)
    {
        // With no extra mod there is nothing to rate, but a crash still brings Send crash report.
        if (extras.Count == 0 && !CrashReports.HasEndpoint())
            return;
        try
        {
            await started.WaitForExitAsync();
            int? exitCode = null;
            try { exitCode = started.ExitCode; }
            catch (InvalidOperationException) { }
            await Task.Delay(1500);
            var capture = await Task.Run(() => BannerlordCoopLauncher.Core.Launch.StartupCrashExplain.Read(BannerlordCoopLauncher.Core.Launch.SlowMotionGuard.BannerlordDocuments(), started.Id));
            var closedNormally = ModResults.ClosedNormally(capture.ExitCode ?? exitCode, capture.ReportText);
            if (closedNormally && ModResults.Record(ModResults.SessionWorked(extras)))
                await Dispatcher.InvokeAsync(() =>
                {
                    foreach (var row in _rows)
                        if (UnknownModBadge.Applies(row.ModuleId))
                            row.Mine = ModResults.Pill(row.ModuleId, VersionOf(row.ModuleId))?.Translated();
                });
            _ = Dispatcher.BeginInvoke(() =>
            {
                var request = ReportRequest(extras, afterGame: true, crashed: !closedNormally);
                if (!closedNormally)
                    request = request.AsCrash(CrashNamed(capture, extras.Select(mod => mod.Id)), CrashLine(capture));
                _reportBar?.AfterGame(request, crashed: !closedNormally);
            });
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
        }
    }
}
