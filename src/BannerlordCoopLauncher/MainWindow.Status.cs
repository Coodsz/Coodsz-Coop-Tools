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
    private void BindModules(ModuleResolution resolution)
    {
        ShowRbmCoopFriendly(resolution);
        _rows.Clear();
        var modules = resolution.BaseModules.Concat(resolution.Modules).ToList();
        IReadOnlyList<string> saved = _settings.Current().Locked ? Array.Empty<string>() : _settings.Current().ModuleOrder;
        IReadOnlyList<string> preferred = saved.Count > 0
            ? saved
            : _profile?.ModuleLoadOrder ?? new List<string>();
        var orderedIds = ClientLoadOrder.Arrange(modules.Select(module => module.ModuleId), preferred);
        var setupNow = _settings.Current();
        var defaultSetup = !setupNow.Locked;
        var workingOrder = defaultSetup
            ? DefaultClientSet.Fixed
            : LockedWorkingOrder();
        var placedIds = setupNow.Locked
            ? orderedIds
            : WorkingLoadOrder.PlaceExtrasBelow(orderedIds, workingOrder);
        placedIds = placedIds.Where(id => !setupNow.RemovedModules.Contains(id, StringComparer.OrdinalIgnoreCase)).ToList();
        var coopIds = modules.Select(module => module.ModuleId).Concat(modules.Select(module => module.LaunchId ?? "")).ToList();
        placedIds = placedIds.Where(id => !BannerlordCoopModules.Hide(id, coopIds)).ToList();
        var repairedOrder = !placedIds.SequenceEqual(orderedIds, StringComparer.OrdinalIgnoreCase);
        orderedIds = placedIds;
        var byId = modules.ToDictionary(module => module.ModuleId, StringComparer.OrdinalIgnoreCase);
        var order = 1;
        var pending = PendingMods(_settings.Current());
        foreach (var id in orderedIds)
        {
            if (!byId.TryGetValue(id, out var module))
                continue;
            var canToggle = !setupNow.Locked && (defaultSetup
                ? !DefaultClientSet.IsFixed(module.ModuleId)
                : ClientModToggles.CanToggle(module.ModuleId));
            var followsSettlement = !defaultSetup && ClientModToggles.FollowsSettlement(module.ModuleId);
            _rows.Add(new ModuleRow
            {
                Order = order++,
                IsPending = pending.Contains(module.ModuleId),
                Name = module.Name,
                ModuleId = module.ModuleId,
                DisplayId = string.IsNullOrWhiteSpace(module.LaunchId) ? module.ModuleId : module.LaunchId,
                // The English word, like TAOM: UiLanguage shows it in the picked language, so a language switch updates the rows too.
                Status = StatusText(module),
                StatusKind = ModChips.StatusKind(StatusText(module)).ToString(),
                // A bound hover is not translated by UiLanguage, so the words are translated here.
                StatusTip = BannerlordCoopLauncher.Core.Localization.Translator.T(ModChips.StatusTip(StatusText(module)) is { Length: > 0 } statusTip ? statusTip : StatusText(module)),
                Detail = followsSettlement && !module.Enabled
                    ? "Turns on with Player Settlement."
                    : (module.Enabled && module.ModuleId.Equals(UIExtenderExPrefabsFix.ModuleId, StringComparison.OrdinalIgnoreCase)
                        ? UIExtenderExPrefabsFix.HealthProblem(module.Directory)
                        : null) ?? module.Detail,
                IsOn = module.Enabled,
                CanToggle = canToggle,
                CanRemove = CanRemoveModule(module.ModuleId),
                // The same rule as dragging (ModuleDragStart): the working Coop mods stay in place in every unlocked setup.
                CanMove = !setupNow.Locked && !DefaultClientSet.IsFixed(module.ModuleId),
                Compat = BannerlordCoopLauncher.Core.Modules.CommunityCompat.Badge(module.ModuleId, BannerlordCoopLauncher.Core.Modules.CommunityCompat.Current, reportTip: BannerlordCoopLauncher.Core.Modules.ModReports.ReportTip(),
                    records: BannerlordCoopLauncher.Core.Modules.CompatRecords.Current, installedVersion: ManifestVersion(module.Directory))?.Translated(),
                Mine = module.IsBaseModule || !UnknownModBadge.Applies(module.ModuleId)
                    ? null
                    : ModResults.Pill(module.ModuleId, ManifestVersion(module.Directory))?.Translated(),
                ToggleTip = canToggle
                    ? "On sends this mod when you connect. Off leaves it installed and skips it."
                    : followsSettlement
                        ? "This follows Player Settlement."
                        : "This mod stays on, in the working load order."
            });
        }

        if (repairedOrder && pending.Count == 0)
        {
            ModuleSaveHint.Text = "The working mods were put back in the order that runs. Click Save mods.";
            ModuleSaveHint.Visibility = Visibility.Visible;
            SaveModsButton.Visibility = Visibility.Visible;
        }

        var enabled = resolution.BaseModules.Concat(resolution.Modules).Where(module => module.Enabled).ToList();
        var ready = enabled.Count(module => module.State == ModuleState.Ready);
        var attention = enabled.Count - ready;
        var ok = attention == 0;
        // A green chip next to the title like the Server Tool's count: 12 mods on, or 12 mods on, 2 need attention in amber.
        ModuleSummaryText.Text = ok ? enabled.Count + " mods on" : enabled.Count + " mods on, " + attention + " need attention";
        ModuleSummaryText.Kind = ok ? Coodsz.Ui.ChipKind.Ok : Coodsz.Ui.ChipKind.Warn;
        ShowMoreNationsNote();
        NoteMissingModules(resolution);
        NoteDuplicateModules(resolution);
        NoteRetiredSettingsLock(enabled.Select(module => module.ModuleId));
        ShowNeedsAttention();
        ShowActiveCoop(resolution);
    }

    // COOP Server Settings Lock left the setup, so a host who dropped it turns away a player who still sends it:
    // once per setup each time the launcher starts, for as long as the setup still turns it on.
    private readonly HashSet<string> _settingsLockNoted = new(StringComparer.OrdinalIgnoreCase);

    private void NoteRetiredSettingsLock(IEnumerable<string> enabledIds)
    {
        var setup = _settings.Current();
        if (RetiredSettingsLock.ShowNote(setup.Name, setup.Locked, enabledIds, _settingsLockNoted))
            Log(RetiredSettingsLock.PlayerNote);
    }

    private void NoteBlockedDll(string? text)
    {
        if (_unblockNoted || !BlockedDllNotice.Matches(text))
            return;
        _unblockNoted = true;
        Log("Windows blocked a DLL. Click the red Unblock DLLs button on Settings, then connect again.");
        if (!_unblockPulse.IsEnabled)
            _unblockPulse.Start();
        PulseUnblockButton();
    }

    private void StopUnblockAlert()
    {
        _unblockPulse.Stop();
        UnblockButton.ClearValue(Control.BackgroundProperty);
        UnblockButton.ClearValue(Control.ForegroundProperty);
        UnblockButton.ClearValue(Control.BorderBrushProperty);
        UnblockButton.ClearValue(Control.BorderThicknessProperty);
        UnblockButton.ClearValue(Control.FontSizeProperty);
        UnblockButton.FontWeight = FontWeights.SemiBold;
    }

    private void PulseUnblockButton()
    {
        _unblockBright = !_unblockBright;
        var color = _unblockBright ? Color.FromRgb(176, 42, 36) : Color.FromRgb(92, 22, 20);
        UnblockButton.Background = new SolidColorBrush(color);
        UnblockButton.Foreground = new SolidColorBrush(Color.FromRgb(255, 236, 232));
        UnblockButton.BorderBrush = new SolidColorBrush(Color.FromRgb(255, 120, 108));
        UnblockButton.BorderThickness = new Thickness(3);
        UnblockButton.FontSize = 15;
        UnblockButton.FontWeight = FontWeights.Bold;
    }

    private void NoteMissingModules(ModuleResolution resolution)
    {
        var missing = resolution.Modules
            .Where(module => module.Enabled && module.State == ModuleState.Missing)
            .ToList();
        var key = string.Join("|", missing.Select(module => module.Name));
        if (key.Equals(_missingNoted, StringComparison.Ordinal))
            return;
        _missingNoted = key;
        if (missing.Count == 0)
            return;
        NoteAction("Missing mods", string.Join(", ", missing.Select(module => module.Name)));
        // Install mods can not get these through Steam, so say where to get them instead.
        var manual = missing.Where(NeedsManualInstall).ToList();
        if (manual.Count > 0)
            LogManualInstall(manual);
        var names = missing.Except(manual).Select(module => module.Name).ToList();
        if (names.Count == 0)
            return;
        Log(names.Count == 1
            ? names[0] + " is on this setup and is not installed. Click Install mods. The launcher subscribes in Steam and waits for the download."
            : string.Join(", ", names) + " are on this setup and are not installed. Click Install mods. The launcher subscribes in Steam and waits for the downloads.");
    }

    // After a crash: the twins again, with the crashed game's own log saying which item it loaded.
    private IReadOnlyList<WorkshopTwin> TwinsAfterCrash(string? gameLog) =>
        _resolution is { } resolution ? FindWorkshopTwins(resolution, gameLog) : _twins;

    private IReadOnlyList<WorkshopTwin> FindWorkshopTwins(ModuleResolution resolution, string? gameLog = null)
    {
        var enabled = resolution.Modules.Where(module => module.Enabled).ToList();
        var preferred = enabled
            .Where(module => module.WorkshopId is { Length: > 1 } id && id.All(char.IsDigit))
            .GroupBy(module => module.ModuleId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().WorkshopId!, StringComparer.OrdinalIgnoreCase);
        return WorkshopTwins.Find(ReadWorkshops().Select(library => library.ContentDirectory), enabled.Select(module => module.ModuleId),
            preferred, () => gameLog, () => WorkshopTwins.LatestClientLog(StartupCrashExplain.EngineLogDirectory()));
    }

    private static bool IsWorkshopPath(string path) =>
        path.Replace('/', '\\').Contains(@"\workshop\content\261550\", StringComparison.OrdinalIgnoreCase);

    // The game loads every copy of a mod it finds, so a second copy is worth a
    // clear line even when the launcher already picked the right one.
    private void NoteDuplicateModules(ModuleResolution resolution)
    {
        var lines = new List<string>();
        // Coop and CoopNightly have different ids, so the game only loads the one
        // the launcher names on the command line. Those copies are not a clash.
        foreach (var module in resolution.Modules.Where(module => module.Enabled && module.ExtraCopies is { Count: > 0 } && !BannerlordCoopModules.IsEither(module.ModuleId)))
        {
            // With the Workshop copy in use, a second Workshop item is not a folder Connect can
            // turn off; the twin line below covers it.
            var used = module.Directory ?? "";
            var others = IsWorkshopPath(used)
                ? module.ExtraCopies!.Where(path => !IsWorkshopPath(path)).ToList()
                : module.ExtraCopies!.ToList();
            if (others.Count == 0)
                continue;
            var extras = string.Join(", ", others.Select(path => "\"" + path + "\""));
            var fromWorkshop = used.Replace('/', '\\').Contains(@"\workshop\content\261550\", StringComparison.OrdinalIgnoreCase);
            lines.Add(fromWorkshop && !BannerlordCoopModules.IsEither(module.ModuleId)
                ? module.Name + " has a second copy: " + extras + ". The launcher uses the Workshop copy and turns the other one off when you click Connect, so the game does not load both."
                // The game folder copy is used and the other is the download of a removed Workshop item (the old
                // MCM). Connect turns that download off (WorkshopDuplicates.ParkRetiredWorkshopCopies); unsubscribing ends it.
                : others.All(IsWorkshopPath) && RetiredWorkshopItems.LocalCopyWins(module.ModuleId)
                    ? module.Name + " has a second copy: " + extras + ". That is an old Steam Workshop download. Unsubscribe from it in Steam so the game does not load both, then click Check mods."
                    : module.Name + " has a second copy: " + extras + ". The game loads both copies. Move the one you do not use out of the Bannerlord Modules folder, then click Check mods.");
        }

        _twins = FindWorkshopTwins(resolution);
        lines.AddRange(_twins.Select(WorkshopTwins.Describe));

        var key = string.Join("|", lines);
        if (key.Equals(_duplicatesNoted, StringComparison.Ordinal))
            return;
        _duplicatesNoted = key;
        foreach (var line in lines)
            Log(line);
        if (lines.Count > 0)
            NoteAction("Duplicate mods", string.Join(" ", lines));
    }

    // The words live in ModChips, shared by every tool (Needs update, Wrong version, Missing on Workshop).
    private static string StatusText(ResolvedModule module) =>
        ModChips.ClientStatus(module.State, module.Enabled, module.ExtraCopies is { Count: > 0 }, module.WorkshopId);

    private void SetJoinMode(bool direct)
    {
        SteamPanel.Visibility = direct ? Visibility.Collapsed : Visibility.Visible;
        DirectPanel.Visibility = direct ? Visibility.Visible : Visibility.Collapsed;
        if (direct)
            SteamSwitchNote.Visibility = Visibility.Collapsed;
        var selected = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x1F, 0x4E, 0x79));
        var idle = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x1B, 0x28, 0x33));
        var selectedBorder = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x8E, 0xB4, 0xD4));
        var idleBorder = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x3A, 0x4C, 0x5C));
        SteamModeButton.Background = direct ? idle : selected;
        DirectModeButton.Background = direct ? selected : idle;
        SteamModeButton.BorderBrush = direct ? idleBorder : selectedBorder;
        DirectModeButton.BorderBrush = direct ? selectedBorder : idleBorder;
        RefreshJoinCaption();
        UpdateConnectButton();
        UpdateOwnServerNote();
    }

    private void JoinField_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_applyingSetup)
            return;
        RefreshJoinCaption();
        UpdateConnectButton();
        UpdateOwnServerNote();
    }

    private void RefreshJoinCaption()
    {
        if (!UseDirect)
        {
            ShowLobbyStatus();
            return;
        }

        if (!JoinGuidance.IsDirectAddress(DirectHostBox.Text))
            SetStatus("Enter an address", 0x8E, 0xB4, 0xD4, 0x1B, 0x28, 0x33);
        else if (!TryReadDirect(out var host, out var port))
            SetStatus("Enter the UDP port", 0x8E, 0xB4, 0xD4, 0x1B, 0x28, 0x33);
        else
            SetStatus("Direct ? " + host + ":" + port.ToString(System.Globalization.CultureInfo.InvariantCulture), 0x8E, 0xB4, 0xD4, 0x1B, 0x28, 0x33);
    }

    private async Task ShowJoinStatusAsync(CancellationToken cancellationToken)
    {
        if (!UseDirect)
        {
            ShowLobbyStatus();
            return;
        }

        if (!JoinGuidance.IsDirectAddress(DirectHostBox.Text))
        {
            SetStatus("Enter an address", 0x8E, 0xB4, 0xD4, 0x1B, 0x28, 0x33);
            return;
        }

        if (!TryReadDirect(out var host, out var port))
        {
            SetStatus("Enter the UDP port", 0x8E, 0xB4, 0xD4, 0x1B, 0x28, 0x33);
            return;
        }

        SetStatus("Checking", 0xE0, 0xB1, 0x5A, 0x3A, 0x30, 0x1C);
        var probe = await UdpServerProbe.ProbeAsync(host, port, UdpServerProbe.DefaultTimeout, cancellationToken);
        ApplyProbe(probe);
    }
}
