using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using BannerlordCoopLauncher.Core.Launch;
using BannerlordCoopLauncher.Core.Modules;
using BannerlordCoopLauncher.Core.Saves;
using BannerlordCoopLauncher.Core.Support;
using BannerlordCoopLauncher.Core.Updates;
using Microsoft.Win32;
using TaomCoop.Core;

namespace TaomCoopHost;

public partial class MainWindow
{
    private void BrowseSave_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Bannerlord save|*.sav",
            Title = "Existing TAOM campaign save"
        };
        if (dialog.ShowDialog(this) != true)
            return;
        if (Path.GetFileNameWithoutExtension(dialog.FileName).Equals("default_new_game", StringComparison.OrdinalIgnoreCase))
        {
            SetControl("default_new_game is a vanilla bootstrap. The TAOM host will not load it.", failed: true);
            return;
        }

        RememberSave(dialog.FileName);
    }

    private void NameSave_Click(object sender, RoutedEventArgs e)
    {
        if (SaveBox.SelectedItem is not SaveChoice selected || !File.Exists(selected.FullPath))
        {
            SetControl("Pick a campaign save first.", failed: true);
            return;
        }

        var name = SaveSlot.ResolveCopyName(SaveNameBox.Text, selected.Name);
        SaveNameBox.Text = name;
        var error = SaveSlot.NameError(name);
        if (error != null)
        {
            SetControl(error, failed: true);
            return;
        }

        var serverDir = ServerSavesDirectory();
        Directory.CreateDirectory(serverDir);
        var destination = Path.Combine(serverDir, name + ".sav");
        if (selected.FullPath.Equals(destination, StringComparison.OrdinalIgnoreCase))
        {
            SetControl(name + " is already the name of this server copy.");
            return;
        }

        if (File.Exists(destination))
        {
            SetControl("A server copy named " + name + " already exists. Pick another name. Existing saves are not overwritten.", failed: true);
            return;
        }

        var renamingServerCopy = SaveSlot.IsInsideDirectory(selected.FullPath, serverDir);
        if (ServerIsRunning() && (renamingServerCopy && IsLiveSaveFamily(selected.Name) || IsLiveSaveFamily(name)))
        {
            SetControl("The running server is using " + ConfiguredSaveName() + ". Stop it before renaming that save.", failed: true);
            return;
        }

        if (!renamingServerCopy && !ConfirmOlderCopy(selected))
            return;

        try
        {
            var sourceJson = Path.ChangeExtension(selected.FullPath, ".json");
            if (renamingServerCopy)
            {
                File.Move(selected.FullPath, destination);
                MoveSlotJson(sourceJson, serverDir, selected.Name, name);
                // A rename of the save the server last ran keeps the Start question on it.
                if (_settings.LastHostedSave.Equals(selected.Name, StringComparison.OrdinalIgnoreCase))
                    _settings.LastHostedSave = name;
            }
            else
            {
                File.Copy(selected.FullPath, destination, overwrite: false);
                if (File.Exists(sourceJson) && !SaveSlot.IsInsideDirectory(sourceJson, serverDir))
                    SaveSlot.CopySidecar(sourceJson, serverDir, selected.Name, name);
            }

            if (!ServerIsRunning() && IsConfiguredSave(selected.Name))
                PointConfigAt(name);
            ReloadSaves();
            RememberSave(destination);
            SaveSettings();
            _actions.Note("Save", (renamingServerCopy ? "Renamed server copy to " : "Named server copy ") + name);
            var made = renamingServerCopy
                ? "Server copy renamed to " + name + ". The single-player save was not changed."
                : "Server copy " + name + " was made. The single-player save was not changed.";
            // A copy from single player gets the Sandbox only line under it. The last line says which save Start hosts now.
            SetControl((renamingServerCopy ? made : made + "\n" + SaveSet.SandboxOnlyNote) + "\n" + SaveSwitchWarning.NowHosts(name));
        }
        catch (Exception ex)
        {
            SetControl(ex.Message, failed: true);
        }
    }

    private void DeleteSave_Click(object sender, RoutedEventArgs e)
    {
        if (SaveBox.SelectedItem is not SaveChoice selected || !File.Exists(selected.FullPath))
        {
            SetControl("Pick a server copy to delete.", failed: true);
            return;
        }

        var serverDir = ServerSavesDirectory();
        if (!SaveSlot.IsInsideDirectory(selected.FullPath, serverDir) || SaveSlot.IsInsideDirectory(selected.FullPath, SinglePlayerSavesDirectory()))
        {
            SetControl("Single-player campaign saves cannot be deleted. Delete removes a server copy only.", failed: true);
            return;
        }

        if (ServerIsRunning() && IsLiveSaveFamily(selected.Name))
        {
            SetControl("The running server is using " + ConfiguredSaveName() + ". That server copy stays.", failed: true);
            return;
        }

        if (!ConfirmWindow.Ask(this, "Delete server copy", "Delete the server copy of " + selected.Name + "? The single-player save is not touched."))
            return;

        try
        {
            File.Delete(selected.FullPath);
            var json = Path.Combine(serverDir, selected.Name + ".json");
            if (File.Exists(json) && SaveSlot.IsInsideDirectory(json, serverDir))
                File.Delete(json);
            ReloadSaves();
            SaveSettings();
            SetControl("Deleted the server copy of " + selected.Name + ". Single-player saves were not touched.");
        }
        catch (Exception ex)
        {
            SetControl(ex.Message, failed: true);
        }
    }

    // TAOM report #44: a copy of an earlier save silently became the save Start hosts. A copy of a save earlier
    // in its campaign than the one the server last ran asks first (SaveSwitchWarning).
    private bool ConfirmOlderCopy(SaveChoice selected)
    {
        var hosted = LastRanSaveName();
        if (hosted.Length == 0)
            return true;
        var hostedPath = HostedSavePath(hosted);
        var question = SaveSwitchWarning.CopyQuestion(hostedPath, CampaignSaveInfoReader.Read(hostedPath)?.Day, selected.FullPath, CampaignSaveInfoReader.Read(selected.FullPath)?.Day);
        return question == null || ConfirmWindow.Ask(this, SaveSwitchWarning.CopyTitle, question, null, SaveSwitchWarning.CopyButton);
    }

    // At Start: a picked save earlier in its campaign than the one the server last ran asks once. Only asks.
    private bool ConfirmSaveSwitch(string savePath)
    {
        var hosted = LastRanSaveName();
        // The tool's own starts (load retry, Restart, Last good save) never ask: nobody may be there to answer.
        if (_automaticRun || hosted.Length == 0 || savePath.Equals(_saveSwitchAgreed, StringComparison.OrdinalIgnoreCase))
            return true;
        var question = SaveSwitchWarning.StartQuestion(hosted, HostedSaveDay(hosted), Path.GetFileNameWithoutExtension(savePath), CampaignSaveInfoReader.Read(savePath)?.Day);
        if (question == null)
            return true;
        if (!ConfirmWindow.Ask(this, SaveSwitchWarning.StartTitle, question, null, SaveSwitchWarning.StartButton))
            return false;
        // The start again after a setup switch does not ask a second time.
        _saveSwitchAgreed = savePath;
        return true;
    }

    // The save the host said yes to. Kept only for the start again after a setup switch; cleared when a start ends
    // and when the box pick changes.
    private string? _saveSwitchAgreed;
    private bool _keepSaveSwitchAgreed;
    // This run was started by the tool itself, not by a Start server click.
    private bool _automaticRun;

    // The save the server last ran. Save settings writes the box pick into server-config.json while the server is
    // stopped, so the name kept when a Start's server ran comes first (SaveSwitchWarning.LastRan).
    private string LastRanSaveName()
    {
        var kept = _settings.LastHostedSave;
        return SaveSwitchWarning.LastRan(kept, kept.Length > 0 && File.Exists(HostedSavePath(kept)), ConfiguredSaveName());
    }

    private string HostedSavePath(string hosted) => Path.Combine(ServerSavesDirectory(), hosted + ".sav");

    private string? HostedSaveDay(string hosted) => CampaignSaveInfoReader.Read(HostedSavePath(hosted))?.Day;

    // Restart loads the save the server ran, not one picked in the box since. Save settings may have written a later
    // pick into server-config.json in the meantime, so the load retry and Restart pass LastRanSaveName(); Last good save
    // passes the save it just put back.
    private void SelectHostedSave(string hosted)
    {
        var choice = SaveBox.Items.OfType<SaveChoice>()
            .FirstOrDefault(item => item.PlaceRank == 0 && item.Name.Equals(hosted, StringComparison.OrdinalIgnoreCase));
        if (choice != null)
            SaveBox.SelectedItem = choice;
    }

    private void MoveSlotJson(string sourceJson, string serverDir, string oldName, string newName)
    {
        if (!File.Exists(sourceJson))
            return;
        var json = File.ReadAllText(sourceJson);
        var destination = Path.Combine(serverDir, newName + ".json");
        File.WriteAllText(destination, SaveSlot.RewriteSlotId(json, oldName, newName));
        if (!sourceJson.Equals(destination, StringComparison.OrdinalIgnoreCase))
            File.Delete(sourceJson);
    }

    private string ServerSavesDirectory() => Path.Combine(_settings.DataRoot, "Game Saves");

    private static string SinglePlayerSavesDirectory() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Mount and Blade II Bannerlord", "Game Saves");

    private bool IsConfiguredSave(string name) =>
        name.Equals(ConfiguredSaveName(), StringComparison.OrdinalIgnoreCase);

    private bool IsLiveSaveFamily(string name)
    {
        var live = ConfiguredSaveName();
        return live.Length > 0
            && (name.Equals(live, StringComparison.OrdinalIgnoreCase)
                || name.StartsWith(live + ".backup", StringComparison.OrdinalIgnoreCase));
    }

    private string ConfiguredSaveName()
    {
        var path = Path.Combine(_settings.DataRoot, "server-config.json");
        if (!File.Exists(path))
            return "";
        return ReadString(File.ReadAllText(path), "saveName") ?? "";
    }

    private void PointConfigAt(string saveName)
    {
        if (ServerIsRunning())
            return;
        var path = Path.Combine(_settings.DataRoot, "server-config.json");
        if (!File.Exists(path))
            return;
        var raw = File.ReadAllText(path);
        if (!int.TryParse(ReadNumber(raw, "autosaveMinutes"), out var autosave))
            autosave = 15;
        var updated = HostConfigEditor.Apply(raw, saveName, ReadString(raw, "password") ?? "", autosave, ReadBool(raw, "steam"), ReadBool(raw, "logFile"));
        if (!ServerIsRunning())
            File.WriteAllText(path, updated);
    }

    private string SelectedSavePath()
    {
        if (SaveBox.SelectedItem is SaveChoice selected)
            return selected.FullPath;
        return "";
    }

    private void ReloadSaves()
    {
        var selected = SelectedSavePath();
        var choices = new List<SaveChoice>();
        foreach (var directory in new[]
        {
            Path.Combine(_settings.DataRoot, "Game Saves"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Mount and Blade II Bannerlord", "Game Saves")
        })
        {
            if (!Directory.Exists(directory))
                continue;
            foreach (var file in Directory.EnumerateFiles(directory, "*.sav"))
            {
                var stem = Path.GetFileNameWithoutExtension(file);
                if (stem.Equals("default_new_game", StringComparison.OrdinalIgnoreCase)
                    || stem.Contains(".backup", StringComparison.OrdinalIgnoreCase)
                    || stem.EndsWith(LastGoodSave.Suffix, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (choices.Any(choice => choice.FullPath.Equals(file, StringComparison.OrdinalIgnoreCase)))
                    continue;
                choices.Add(new SaveChoice(file, SavePlace(file)));
            }
        }

        if (!string.IsNullOrWhiteSpace(_settings.SaveFile) && File.Exists(_settings.SaveFile) && choices.All(choice => !choice.FullPath.Equals(_settings.SaveFile, StringComparison.OrdinalIgnoreCase)))
            choices.Add(new SaveChoice(_settings.SaveFile, SavePlace(_settings.SaveFile)));

        choices.Sort(static (left, right) =>
        {
            var place = left.PlaceRank.CompareTo(right.PlaceRank);
            return place != 0 ? place : string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase);
        });

        SaveBox.Items.Clear();
        foreach (var choice in choices)
            SaveBox.Items.Add(choice);
        var configured = ConfiguredSaveName();
        var match = choices.FirstOrDefault(choice => choice.FullPath.Equals(selected, StringComparison.OrdinalIgnoreCase))
            ?? choices.FirstOrDefault(choice => choice.FullPath.Equals(_settings.SaveFile, StringComparison.OrdinalIgnoreCase))
            ?? choices.FirstOrDefault(choice => choice.PlaceRank == 0 && choice.Name.Equals(configured, StringComparison.OrdinalIgnoreCase))
            ?? choices.FirstOrDefault(choice => choice.PlaceRank == 0 && choice.Name.Equals("save001", StringComparison.OrdinalIgnoreCase))
            ?? choices.FirstOrDefault(choice => choice.PlaceRank == 0)
            ?? choices.FirstOrDefault();
        if (match != null)
            SaveBox.SelectedItem = match;
        ShowSaveDetail();
    }

    private void SaveBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // A yes to the older save question was for the save picked then.
        _saveSwitchAgreed = null;
        ShowSaveDetail();
    }

    private void ShowSaveDetail()
    {
        var path = SelectedSavePath();
        if (path.Length == 0)
        {
            ShowSaveFacts(null, missing: false);
            ShowSaveVersionWarning(null);
            return;
        }

        ShowSaveFacts(CampaignSaveInfoReader.Read(path), missing: true);
        ShowSaveVersionWarning(SaveVersionWarningText(path));
    }

    private void ShowSaveVersionWarning(string? text)
    {
        if (SaveVersionWarning == null)
            return;
        SaveVersionWarning.Text = text ?? "";
        SaveVersionWarning.Visibility = text == null ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>The warning for a save made on another TAOM release, or null when it matches.</summary>
    private string? SaveVersionWarningText(string savePath)
    {
        if (!File.Exists(savePath))
            return null;
        var installed = InstalledTaomVersion();
        var made = SaveTaomVersion.MismatchedVersion(SaveTaomVersion.Read(savePath), installed);
        return made == null ? null : SaveTaomVersion.Warning(made, installed);
    }

    // The TAOM in the server folder, or the playtest's TAOM when the folder is not prepared yet.
    private string InstalledTaomVersion()
    {
        try
        {
            var subModule = Path.Combine(_settings.ServerRoot ?? "", "engine", "Modules", "TAOM", "SubModule.xml");
            if (File.Exists(subModule))
            {
                var version = ModuleIdentityReader.Read(subModule).Version.TrimStart('v', 'V');
                if (version.Length > 0)
                    return version;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }

        return TaomStack.TaomVersionText;
    }

    /// <summary>At Start: asks before starting a save made on another TAOM release.</summary>
    private bool ConfirmSaveTaomVersion(string savePath)
    {
        var warning = SaveVersionWarningText(savePath);
        if (warning == null)
            return true;
        NoteLog(warning);
        return ConfirmWindow.Ask(this, "This save is from another TAOM version",
            warning + " The server stops while it loads a save like this.", null, "Start anyway");
    }

    private void ShowSaveFacts(CampaignSaveInfo? info, bool missing)
    {
        if (info == null)
        {
            SaveFacts.Visibility = Visibility.Collapsed;
            SaveMods.ItemsSource = null;
            SaveMissing.Text = missing ? "This save has no readable campaign header yet." : "";
            SaveMissing.Visibility = missing ? Visibility.Visible : Visibility.Collapsed;
            return;
        }

        SaveMissing.Visibility = Visibility.Collapsed;
        SaveFacts.Visibility = Visibility.Visible;
        SaveCharacter.Text = info.Character.Length == 0 ? "Unknown" : info.Character;
        SaveLevel.Text = info.Level.Length == 0 ? "?" : info.Level;
        SaveDay.Text = CampaignSaveInfoReader.CampaignDay(info.Day);
        SaveMods.ItemsSource = info.Modules;
    }

    private void RememberSave(string path)
    {
        var existing = SaveBox.Items.OfType<SaveChoice>().FirstOrDefault(choice => choice.FullPath.Equals(path, StringComparison.OrdinalIgnoreCase));
        if (existing == null)
        {
            existing = new SaveChoice(path, SavePlace(path));
            SaveBox.Items.Add(existing);
        }

        SaveBox.SelectedItem = existing;
    }

    private string SavePlace(string path)
    {
        var host = Path.Combine(_settings.DataRoot, "Game Saves");
        if (path.StartsWith(host, StringComparison.OrdinalIgnoreCase))
            return "Server copy";
        var documents = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Mount and Blade II Bannerlord", "Game Saves");
        if (path.StartsWith(documents, StringComparison.OrdinalIgnoreCase))
            return "Single player copy";
        return "Other folder";
    }
}
