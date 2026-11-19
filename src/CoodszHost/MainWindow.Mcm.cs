using System.Collections.ObjectModel;
using System.Text;
using System.Windows.Documents;
using TaomCoop.Core;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using BannerlordCoopLauncher.Core.Launch;
using BannerlordCoopLauncher.Core.Saves;
using BannerlordCoopLauncher.Core.Support;
using BannerlordCoopLauncher.Core.Updates;
using BannerlordCoopLauncher.Core.Modules;
using CoodszHost.Services;
using Microsoft.Win32;

namespace CoodszHost;

public partial class MainWindow
{
    private void EnsureMcmSnapshots()
    {
        foreach (var profile in _settings.Profiles)
            McmCatalog.SeedFromLive(EnsureMcmFolder(profile));
        HostSettings.Save(SettingsPath, _settings);
    }

    // COOP Server Settings Lock left the setup; its old values become this setup's MCM defaults where the host has
    // none, so a server keeps playing the same until the host changes them here. The players get the host's values.
    // Returns the setup's MCM files that could not be written or read; Start leaves the live copies of those as they are.
    // Null when the step stopped partway: Start then copies none of the setup's MCM files over the live ones.
    private IReadOnlyList<string>? SeedSettingsLockDefaults(HostProfile profile)
    {
        if (Edition.IsAD1259)
            return Array.Empty<string>();
        try
        {
            var result = McmCatalog.SeedLockDefaults(EnsureMcmFolder(profile), _modules.Where(row => row.Enabled).Select(row => row.Id));
            static string Folders(IEnumerable<string> paths) => string.Join(", ", paths.Select(path => Path.GetFileName(Path.GetDirectoryName(path)) ?? path));
            if (result.Unwritten.Count > 0)
                Append("The MCM settings of " + Folders(result.Unwritten) + " could not be saved in this setup, so the server keeps its own copy of them.");
            if (result.Unreadable.Count > 0)
                Append("The MCM settings of " + Folders(result.Unreadable) + " in this setup could not be read, so the server keeps its own copy of them.");
            if (result.Seeded.Count > 0)
            {
                // The mod names from the module list (SubModule.xml), or the id when a row has no name.
                var names = result.Seeded.Select(id => _modules.FirstOrDefault(row => row.Id.Equals(id, StringComparison.OrdinalIgnoreCase))?.Name is { Length: > 0 } name ? name : id);
                Append("The MCM settings of " + string.Join(", ", names) + " got the values COOP Server Settings Lock used, where this setup had none. Change them on the MCM page. Players get the host's values.");
                _actions.Note("MCM", "Settings Lock defaults added for " + string.Join(", ", result.Seeded) + " in " + profile.Name);
            }
            return result.KeepLive;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Append(ex.Message);
            return null;
        }
    }

    private static string EnsureMcmFolder(HostProfile profile)
    {
        if (string.IsNullOrWhiteSpace(profile.McmFolder))
            profile.McmFolder = Guid.NewGuid().ToString("N");
        return profile.McmFolder;
    }

    private void ClearUnusedMcm_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var keep = _modules.Where(row => row.Enabled).Select(row => row.Id).ToList();
            var result = McmLeftovers.ClearUnused(McmCatalog.SinglePlayerRoot, keep);
            var live = McmLeftovers.ClearUnused(McmCatalog.Root, keep);
            var removed = result.Removed + live.Removed;
            var names = result.Names.Concat(live.Names).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (removed == 0)
            {
                McmNote.Text = "No unused optional MCM configs were found.";
                Append(McmNote.Text);
                return;
            }

            McmNote.Text = "Removed " + removed + " leftover MCM config" + (removed == 1 ? "" : "s") + ": "
                + string.Join(", ", names) + ". Use Load single-player MCM after a fresh single-player save if menus still look wrong.";
            Append(McmNote.Text);
            _actions.Note("MCM", "Cleared unused: " + string.Join(", ", names));
            RefreshMcm_Click(sender, e);
        }
        catch (Exception ex)
        {
            McmNote.Text = ex.Message;
            Append(ex.Message);
        }
    }

    private void LoadSinglePlayerMcm_Click(object sender, RoutedEventArgs e)
    {
        var profile = _settings.Current();
        var folder = EnsureMcmFolder(profile);
        if (McmCatalog.SnapshotHasSettings(folder)
            && !ConfirmWindow.Ask(this, "Replace MCM", "This setup already has MCM settings. Replace them with the single-player MCM? A server that is already running keeps its current MCM until you stop it and press Start server."))
            return;

        try
        {
            var count = McmCatalog.ImportSinglePlayer(folder);
            HostSettings.Save(SettingsPath, _settings);
            _actions.Note("MCM", "Loaded " + count + " single-player MCM file(s) into " + _settings.Current().Name);
            RefreshMcm_Click(sender, e);
            var note = count + " single-player MCM file" + (count == 1 ? "" : "s") + " copied into this setup. The single-player files were not changed. "
                + (_session.IsRunning
                    ? "This running server keeps its current MCM until you stop it and press Start server."
                    : "Start server will use them.");
            McmNote.Text = note;
            Append(note);
        }
        catch (Exception ex)
        {
            McmNote.Text = ex.Message;
            Append(ex.Message);
        }
    }

    private void RefreshMcm_Click(object sender, RoutedEventArgs e)
    {
        _loading = true;
        SeedSettingsLockDefaults(_settings.Current());
        _mcmFiles = McmCatalog.Load(EnsureMcmFolder(_settings.Current())).ToList();
        McmFileBox.ItemsSource = _mcmFiles.Select(file => file.Name).ToList();
        McmFileBox.SelectedIndex = _mcmFiles.Count > 0 ? 0 : -1;
        _loading = false;
        var enabled = _modules.Count(row => row.Enabled);
        var singlePlayer = McmCatalog.SinglePlayerFileCount();
        if (_mcmFiles.Count == 0)
        {
            McmNote.Text = enabled + " mods are on in this setup, but no MCM settings files were found yet. "
                + "Only mods that use MCM write a file here. Click Load single-player MCM, or start the server once with Harmony, ButterLib, UIExtender, and MCM so those menus can create files. "
                + "Their in-game menus stay off on the dedicated server; this page edits the settings files.";
        }
        else
        {
            McmNote.Text = _mcmFiles.Count + " MCM settings file" + (_mcmFiles.Count == 1 ? "" : "s")
                + " for " + _settings.Current().Name + ". "
                + enabled + " mods are on in this setup. "
                + "This list is only the mods that use MCM and already have a settings file, not every enabled mod. "
                + "They load the next time this profile starts the server.";
            if (singlePlayer > _mcmFiles.Count)
                McmNote.Text += " Single-player has " + singlePlayer + " MCM file" + (singlePlayer == 1 ? "" : "s")
                    + ". Click Load single-player MCM if you want those copied into this setup.";
        }
        ShowMcmFile();
    }

    private void McmFile_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loading)
            ShowMcmFile();
    }

    private void ShowMcmFile()
    {
        var file = SelectedMcmFile();
        McmValueList.ItemsSource = file?.Values;
        McmValueTitle.Text = "Select a setting";
        McmValueBox.Text = "";
        McmValueBox.IsEnabled = false;
        McmValueCheck.Visibility = Visibility.Collapsed;
        McmApply.IsEnabled = false;
    }

    private void McmValue_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (McmValueList.SelectedItem is not McmValue value)
            return;
        McmValueTitle.Text = value.Name;
        var editable = value.Kind is "bool" or "number" or "string";
        McmApply.IsEnabled = editable;
        McmValueCheck.Visibility = value.Kind == "bool" ? Visibility.Visible : Visibility.Collapsed;
        McmValueBox.Visibility = value.Kind == "bool" ? Visibility.Collapsed : Visibility.Visible;
        McmValueBox.IsEnabled = editable && value.Kind != "bool";
        McmValueBox.Text = value.Text;
        McmValueCheck.IsChecked = value.Kind == "bool" && value.Text.Equals("True", StringComparison.OrdinalIgnoreCase);
        if (!editable)
            McmValueBox.Text = "This setting is nested and stays read-only.";
    }

    private void EnableAllMcm_Click(object sender, RoutedEventArgs e)
    {
        var file = SelectedMcmFile();
        if (file == null)
            return;
        try
        {
            var count = McmCatalog.EnableAll(file);
            if (count == 0)
            {
                McmNote.Text = file.Name + " has no on/off MCM options.";
                return;
            }

            _actions.Note("MCM", "Enabled " + count + " option(s) in " + file.Name + " for " + _settings.Current().Name);

            if (McmValueList.SelectedItem is McmValue selected && selected.Kind == "bool")
                McmValueCheck.IsChecked = true;
            McmNote.Text = "Enabled " + count + " MCM options in " + file.Name + " for " + _settings.Current().Name + ". Saved. Stop the server and start this setup again to use them.";
        }
        catch (Exception ex)
        {
            McmNote.Text = ex.Message;
        }
    }

    private void ApplyMcm_Click(object sender, RoutedEventArgs e)
    {
        var file = SelectedMcmFile();
        if (file == null || McmValueList.SelectedItem is not McmValue value)
            return;
        try
        {
            var text = value.Kind == "bool"
                ? (McmValueCheck.IsChecked == true ? "true" : "false")
                : McmValueBox.Text.Trim();
            if (value.Kind == "number" && !double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _))
                throw new InvalidOperationException("Enter a number.");
            McmCatalog.Apply(file, value, text);
            _actions.Note("MCM", "Set " + value.Name + " in " + file.Name + " for " + _settings.Current().Name);
            McmNote.Text = "Saved " + value.Name + " for " + _settings.Current().Name + ". The running server keeps the old value until you stop it and start this setup again.";
        }
        catch (Exception ex)
        {
            McmNote.Text = ex.Message;
        }
    }

    private McmSettingFile? SelectedMcmFile() =>
        McmFileBox.SelectedIndex >= 0 && McmFileBox.SelectedIndex < _mcmFiles.Count ? _mcmFiles[McmFileBox.SelectedIndex] : null;
}
