// Parts adapted from ModderLords by Ryvius (@PlueRyvius), used with permission (his Mod settings tab).
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using BannerlordCoopLauncher.Core.Localization;
using CoodszHost.Services;

namespace CoodszHost;

/// <summary>
/// Settings sync for the host. It works with no setup: every mod is locked to the host, and Start stages the
/// lock list and the host overrides for Coodsz Server Tools. The Advanced area on the Settings page shows the
/// mods with MCM settings the server reported. While the server runs, Apply sends edits to it (it uses them at
/// once and passes them to players); while it is stopped, edits are saved and set at the next launch.
/// </summary>
public partial class MainWindow
{
    private List<ModSettingObject> _modObjects = new();
    private SettingsOverrides _modOverrides = new();
    private DispatcherTimer? _modSettingsTimer;
    private DateTime _modSettingsWritten;
    private bool _modSettingsBusy;
    private bool _modListRefreshing;

    private string ModSettingsKey(HostProfile profile) => EnsureMcmFolder(profile);

    private bool ModSettingsServerUp => _session.IsRunning && !string.IsNullOrWhiteSpace(ServerRoot);

    /// <summary>On Start: the lock list (recipes.json) and this setup's overrides, in a fresh live folder.</summary>
    private void StageModSettings(HostProfile profile)
    {
        if (string.IsNullOrWhiteSpace(ServerRoot))
            return;
        try
        {
            McmKeepOwn.Write(CoodszServerTools.ModuleFolder(ServerRoot), profile.PlayersOwnSettings);
            var overrides = ModSettingsLive.LoadOverrides(ModSettingsLive.OverridesPath(ModSettingsKey(profile)));
            // ModderLords' DefaultSettings for the mods this server runs, under what the host set. Never in AD1259 (a pinned set).
            var lines = new List<string>();
            overrides = CompatRecordDefaults.WithDefaults(overrides, BannerlordCoopLauncher.Core.Modules.CompatRecords.Current.All,
                _modules.Where(row => row.Enabled).Select(row => row.Id), lines, pinnedSet: Edition.IsAD1259);
            foreach (var line in lines)
                Append(line);
            ModSettingsLive.Stage(ModSettingsLive.LiveDir(ServerRoot), overrides);
            _modSettingsWritten = default;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Settings sync still runs with the defaults; only a line for the log.
            Append("Mod settings for players: " + ex.Message);
        }
    }

    private void ModSettingsToggle_Click(object sender, RoutedEventArgs e)
    {
        var show = ModSettingsPanel.Visibility != Visibility.Visible;
        ModSettingsPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (!show)
        {
            _modSettingsTimer?.Stop();
            return;
        }
        LoadModSettings();
        _modSettingsTimer ??= new DispatcherTimer(TimeSpan.FromSeconds(2), DispatcherPriority.Background, (_, _) => PollModSettings(), Dispatcher);
        _modSettingsTimer.Start();
    }

    /// <summary>Reads this setup's overrides and the last description (the server's, else the cached copy).</summary>
    private void LoadModSettings()
    {
        var profile = _settings.Current();
        _modOverrides = ModSettingsLive.LoadOverrides(ModSettingsLive.OverridesPath(ModSettingsKey(profile)));
        var document = ModSettingsServerUp ? ModSettingsLive.ReadDocument(Path.Combine(ModSettingsLive.LiveDir(ServerRoot), ModSettingsLive.SettingsFileName)) : null;
        if (document != null)
        {
            _modSettingsWritten = document.WrittenAt;
            SaveModSettingsCache(profile, document);
        }
        document ??= ModSettingsLive.ReadDocument(ModSettingsLive.CachePath(ModSettingsKey(profile)));
        ShowModSettings(document);
    }

    private void PollModSettings()
    {
        if (ModSettingsPanel.Visibility != Visibility.Visible || !ModSettingsServerUp || _modSettingsBusy)
            return;
        var document = ModSettingsLive.ReadDocument(Path.Combine(ModSettingsLive.LiveDir(ServerRoot), ModSettingsLive.SettingsFileName));
        if (document == null || document.WrittenAt == _modSettingsWritten)
        {
            if (document == null && _modObjects.Count == 0)
                ModSettingsStatus.Text = Translator.T("Waiting for the server's mod settings. They show up within a minute of the start.");
            return;
        }
        _modSettingsWritten = document.WrittenAt;
        SaveModSettingsCache(_settings.Current(), document);
        ShowModSettings(document);
    }

    private void SaveModSettingsCache(HostProfile profile, LiveSettingsDocument document)
    {
        try { ModSettingsLive.SaveCache(ModSettingsLive.CachePath(ModSettingsKey(profile)), document); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private void ShowModSettings(LiveSettingsDocument? document)
    {
        var profile = _settings.Current();
        var live = ModSettingsServerUp && document != null && document.WrittenAt == _modSettingsWritten && _modSettingsWritten != default;
        var selected = (ModSettingsList.SelectedItem as ModSettingObject)?.SettingsId;
        _modObjects = document == null
            ? new List<ModSettingObject>()
            : ModSettingsMerge.Merge(_modObjects, document, _modOverrides, offline: !live,
                source => !McmKeepOwn.Keeps(profile.PlayersOwnSettings, source.ModuleId, source.SettingsId, source.Folder));
        // While the server runs: how the values saved for the next launch went when it started.
        var overridesAck = live ? ModSettingsLive.ReadOverridesAck(ModSettingsLive.LiveDir(ServerRoot)) : null;
        foreach (var item in _modObjects)
        {
            item.OverrideState = overridesAck?.StateOf(item.SettingsId);
            item.Refresh();
        }
        if (overridesAck is { Ok: false })
            Append("Mod settings: " + (overridesAck.Report ?? "the saved values could not be read on the server"));
        _modListRefreshing = true;
        try
        {
            ModSettingsList.ItemsSource = null;
            ModSettingsList.ItemsSource = _modObjects;
            ModSettingsList.SelectedItem = _modObjects.FirstOrDefault(o => o.SettingsId == selected) ?? _modObjects.FirstOrDefault();
        }
        finally { _modListRefreshing = false; }
        // A poll that keeps the same mod selected keeps its note, so "Applied" stays readable.
        ShowSelectedModSettings(keepNote: (ModSettingsList.SelectedItem as ModSettingObject)?.SettingsId == selected);

        if (live)
            ModSettingsStatus.Text = document!.McmRunning
                ? Translator.T("Server running. Apply sends your changes to the server, and players get them in a few seconds.")
                : Translator.T("Server running, but its MCM is not loaded, so there are no mod settings to show.");
        else if (_modObjects.Count > 0)
            ModSettingsStatus.Text = Translator.T("Server not running, edits apply at the next launch.");
        else
            ModSettingsStatus.Text = ModSettingsServerUp
                ? Translator.T("Waiting for the server's mod settings. They show up within a minute of the start.")
                : Translator.T("No mod settings yet. Start the server once and the mods with MCM settings show up here.");
    }

    private void ModSettingsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_modListRefreshing)
            ShowSelectedModSettings();
    }

    private void ShowSelectedModSettings(bool keepNote = false)
    {
        var item = ModSettingsList.SelectedItem as ModSettingObject;
        ModSettingsGroups.ItemsSource = item?.Groups;
        ModSettingsLock.Visibility = item == null ? Visibility.Collapsed : Visibility.Visible;
        ModSettingsLock.IsChecked = item?.Locked ?? true;
        if (!keepNote)
            ModSettingsNote.Text = "";
    }

    private void ModSettingsLock_Click(object sender, RoutedEventArgs e)
    {
        if (ModSettingsList.SelectedItem is not ModSettingObject item)
            return;
        var profile = _settings.Current();
        var locked = ModSettingsLock.IsChecked == true;
        // Locking removes every key the mod may be listed under (module id, settings id, folder), so an
        // entry made in ModderLords or by an older Coodsz Host is removed too.
        var changed = locked
            ? McmKeepOwn.Lock(profile.PlayersOwnSettings, item.LockKey, item.ModuleId, item.SettingsId, item.Folder)
            : McmKeepOwn.SetLocked(profile.PlayersOwnSettings, item.LockKey, false);
        if (!changed)
            return;
        item.Locked = locked;
        item.Refresh();
        HostSettings.Save(SettingsPath, _settings);
        if (!string.IsNullOrWhiteSpace(ServerRoot))
        {
            try { McmKeepOwn.Write(CoodszServerTools.ModuleFolder(ServerRoot), profile.PlayersOwnSettings); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Append("Mod settings for players: " + ex.Message); }
        }
        _actions.Note("Mod settings", (locked ? "Players use the host's settings for " : "Players keep their own settings for ") + item.LockKey + " in " + profile.Name);
        ModSettingsNote.Text = locked
            ? Translator.T("Players use your settings for this mod.")
            : Translator.T("Players keep their own settings for this mod. Players already on the server keep yours until they join again.");
    }

    private async void ModSettingsApply_Click(object sender, RoutedEventArgs e)
    {
        if (ModSettingsList.SelectedItem is not ModSettingObject item || _modSettingsBusy)
            return;
        var values = ModSettingsMerge.Dirty(item, out var problem);
        if (values == null)
        {
            ModSettingsNote.Text = problem ?? "";
            return;
        }
        if (values.Count == 0)
        {
            ModSettingsNote.Text = Translator.T("Nothing changed.");
            return;
        }

        var profile = _settings.Current();
        if (!StoreModOverrides(profile, item, values))
            return;
        if (!ModSettingsServerUp)
        {
            ModSettingsNote.Text = Translator.T("Saved. These values are set at the next launch.");
            item.Refresh();
            return;
        }

        _modSettingsBusy = true;
        ModSettingsApply.IsEnabled = false;
        ModSettingsNote.Text = Translator.T("Sending your changes to the server...");
        try
        {
            var ack = await ModSettingsLive.ApplyAsync(ModSettingsLive.LiveDir(ServerRoot), item.SettingsId, values, TimeSpan.FromSeconds(15));
            if (ack == null)
            {
                ModSettingsNote.Text = Translator.T("Saved. The server did not answer, so these values are set at the next launch.");
                Append("Mod settings: no answer from the server for " + item.SettingsId + ".");
                return;
            }
            // Only values the server really took become the server's value; the rest stay marked as changed.
            var skipped = new HashSet<string>(ack.Skipped ?? new List<string>(), StringComparer.Ordinal);
            if (ack.Ok)
                foreach (var property in item.AllProperties.Where(p => values.ContainsKey(p.Id) && !skipped.Contains(p.Id)))
                    property.Original = property.Value;
            ModSettingsNote.Text = ack.Ok && skipped.Count == 0
                ? Translator.T("Applied on the server. Players get it in a few seconds, and it is set again at every launch.")
                : ack.Describe();
            Append("Mod settings: " + item.SettingsId + ": " + ack.Report);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ModSettingsNote.Text = Translator.T("Saved. These values are set at the next launch.");
            Append("Mod settings: " + ex.Message);
        }
        finally
        {
            _modSettingsBusy = false;
            ModSettingsApply.IsEnabled = true;
            item.Refresh();
        }
    }

    private bool StoreModOverrides(HostProfile profile, ModSettingObject item, Dictionary<string, string> values)
    {
        _modOverrides.Set(item.SettingsId, values);
        try
        {
            ModSettingsLive.SaveOverrides(ModSettingsLive.OverridesPath(ModSettingsKey(profile)), _modOverrides);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ModSettingsNote.Text = ex.Message;
            return false;
        }
        foreach (var property in item.AllProperties)
            if (values.TryGetValue(property.Id, out var value))
                property.OverrideValue = value;
        HostSettings.Save(SettingsPath, _settings);
        _actions.Note("Mod settings", "Saved " + values.Count + " value(s) for " + item.SettingsId + " in " + profile.Name);
        return true;
    }

    private void ModSettingsRevert_Click(object sender, RoutedEventArgs e)
    {
        if (ModSettingsList.SelectedItem is not ModSettingObject item)
            return;
        foreach (var property in item.AllProperties)
            property.Revert();
        ModSettingsNote.Text = "";
    }

    private void ModSettingsReload_Click(object sender, RoutedEventArgs e)
    {
        _modSettingsWritten = default;
        LoadModSettings();
    }

    private void ModSettingsClear_Click(object sender, RoutedEventArgs e)
    {
        if (ModSettingsList.SelectedItem is not ModSettingObject item)
            return;
        var profile = _settings.Current();
        _modOverrides.Clear(item.SettingsId);
        try
        {
            ModSettingsLive.SaveOverrides(ModSettingsLive.OverridesPath(ModSettingsKey(profile)), _modOverrides);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ModSettingsNote.Text = ex.Message;
            return;
        }
        var live = ModSettingsServerUp;
        foreach (var property in item.AllProperties)
        {
            property.OverrideValue = null;
            if (!live)
                property.Value = property.Original;
        }
        item.Refresh();
        _actions.Note("Mod settings", "Cleared the saved values for " + item.SettingsId + " in " + profile.Name);
        ModSettingsNote.Text = live
            ? Translator.T("Cleared. The server keeps its current values until it stops; they are not set at the next launch.")
            : Translator.T("Cleared. The values shown are the ones the server last reported.");
    }
}
