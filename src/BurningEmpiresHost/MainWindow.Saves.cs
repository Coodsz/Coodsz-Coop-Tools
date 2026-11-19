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
using BurningEmpires.Core;
using BurningEmpires.Ui;
using TaomCoop.Core;

namespace BurningEmpiresHost;

public partial class MainWindow
{
    // True while the tool itself fills or picks in the save box, so only the host's own pick
    // drops a waiting new campaign.
    private bool _fillingSaves;

    private void BrowseSave_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Bannerlord save|*.sav",
            Title = "Existing Burning Empires campaign save"
        };
        if (dialog.ShowDialog(this) != true)
            return;
        if (BeServerLaunch.IsTemplate(dialog.FileName))
        {
            SetControl(BeServerLaunch.TemplateReason, failed: true);
            return;
        }

        if (!BeSaveVersion.IsBeSave(dialog.FileName))
        {
            SetControl(Path.GetFileNameWithoutExtension(dialog.FileName) + " was not made with Burning Empires. Pick a Burning Empires save, or click New campaign.", failed: true);
            return;
        }

        // RememberSave picks the save without SaveBox_SelectionChanged, so a name typed before would
        // still make Start server build a new world instead of hosting this save.
        NewCampaignBox.Clear();
        ClearNewCampaign();
        RememberSave(dialog.FileName);
    }

    // ----- New campaign -----

    // New campaign: the host types a name and Start server makes a fresh Burning Empires world under
    // it (Start-ModdedServer.ps1 -NewSaveName, then the compat module builds the world). No .sav is needed.
    private void NewCampaign_Click(object sender, RoutedEventArgs e)
    {
        var name = NewCampaignBox.Text.Trim();
        var problem = BeServerLaunch.NewCampaignProblem(name, ServerSavesDirectory(), BeServerLaunch.ReadServerScript(_settings.ServerRoot));
        if (problem != null)
        {
            SetControl(problem, failed: true);
            return;
        }

        _settings.NewCampaignName = name;
        SaveSettings();
        // The name now shows in the note under the box, so the box is cleared for no double click.
        NewCampaignBox.Clear();
        _actions.Note("Save", "New campaign " + name);
        ShowSaveDetail();
        SetControl("New campaign " + name + " is ready. Click Start server to make it. The first start builds the world and can take a long time.");
    }

    // The Start server button. A name typed in step 1 counts as the new campaign without a New
    // campaign click. A name with a problem stops the start, and the status line says why. Restart and
    // Last good save call Start_Click themselves, so a name left in the box never changes their save.
    private void StartButton_Click(object sender, RoutedEventArgs e)
    {
        if (NewCampaignBox.Text.Trim().Length > 0 && !ServerIsRunning())
        {
            NewCampaign_Click(sender, e);
            // New campaign empties the box once it took the name.
            if (NewCampaignBox.Text.Trim().Length > 0)
                return;
        }

        Start_Click(sender, e);
    }

    private void ClearNewCampaign()
    {
        if (_settings.NewCampaignName.Length == 0)
            return;
        _settings.NewCampaignName = "";
        SaveSettings();
    }

    // The new campaign name the next start makes, or null. Once the server wrote that campaign, it
    // is a save like any other and the name is let go.
    private string? PendingNewCampaign()
    {
        var name = _settings.NewCampaignName.Trim();
        if (name.Length == 0)
            return null;
        var sav = Path.Combine(ServerSavesDirectory(), name + ".sav");
        if (!File.Exists(sav))
            return name;
        _settings.NewCampaignName = "";
        PointAtSave(sav);
        return null;
    }

    // Picks this save in the box and remembers it. SaveSettings stores whatever the box shows, so a
    // save set only in _settings.SaveFile was lost and the next start hosted the old pick again.
    private void PointAtSave(string sav)
    {
        ReloadSaves();
        RememberSave(sav);
        SaveSettings();
    }

    // ----- Save to a new name -----

    // Burning Empires changes an older save when it loads, and a changed save cannot be saved again
    // under its old name (the compat module's save safety warning). Save to a new name saves the
    // running campaign under another name and points the server at it, so the next start loads it.
    private const string MigrationLine = "this Burning Empires build migrates older saves";

    private void ShowSaveToNewName(bool running)
    {
        var show = running && _logTail.ToString().Contains(MigrationLine, StringComparison.OrdinalIgnoreCase);
        SaveToNewNameBar.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void SaveToNewName_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _startingServer)
        {
            SetControl("Wait for the current action to finish.", failed: true);
            return;
        }
        if (!ServerIsRunning())
        {
            SetControl("Save to a new name works while the server runs.", failed: true);
            return;
        }

        var live = ConfiguredSaveName();
        var name = SaveToNewNameBox.Text.Trim();
        if (name.Length == 0)
            name = live + "_" + BeStack.BeVersionText.Replace(".", "");
        SaveToNewNameBox.Text = name;
        var error = SaveSlot.NameError(name);
        if (error != null)
        {
            SetControl(error, failed: true);
            return;
        }

        var sav = Path.Combine(ServerSavesDirectory(), name + ".sav");
        if (File.Exists(sav))
        {
            SetControl("A server copy named " + name + " already exists. Pick another name. Existing saves are not overwritten.", failed: true);
            return;
        }

        _busy = true;
        try
        {
            SetControl("Saving the campaign as " + name + ".");
            // The server's own save command takes a save name, as the queued save does. The live test confirms this.
            var reply = await Task.Run(() => ConsoleQueue.Send(CommandQueueRoots(), "launcher.server.save " + name));
            NoteLog("Save as " + name + ": " + reply.Text);
            var started = DateTime.UtcNow;
            var deadline = started.AddSeconds(180);
            var stillSaving = false;
            // The .sav shows up before it is done: like the save before Stop, wait for the transaction marker
            // to go and the file to stop growing, so the config never points at a half written save.
            var marker = WorldSavedLine.TransactionMarker(Path.GetDirectoryName(sav)!, name);
            var finished = false;
            while (!(finished = await Task.Run(() => SaveFileFinished(sav, marker, DateTime.MinValue))) && DateTime.UtcNow < deadline)
            {
                await Task.Delay(1000);
                // A big Burning Empires world takes a while to write, so the host hears it is still going.
                if (!stillSaving && DateTime.UtcNow - started >= TimeSpan.FromSeconds(20))
                {
                    stillSaving = true;
                    SetControl(BeUiText.StillSaving);
                }
            }
            if (!finished)
            {
                SetControl("The server did not write " + name + ". The campaign keeps its old name. " + reply.Text, failed: true);
                return;
            }

            var path = Path.Combine(_settings.DataRoot, "server-config.json");
            var raw = File.ReadAllText(path);
            if (!int.TryParse(ReadNumber(raw, "autosaveMinutes"), out var autosave))
                autosave = 15;
            File.WriteAllText(path, HostConfigEditor.Apply(raw, name, ReadString(raw, "password") ?? "", autosave, ReadBool(raw, "steam"), ReadBool(raw, "logFile")));
            PointAtSave(sav);
            _actions.Note("Save", "Saved " + live + " as " + name);
            SetControl("Saved the campaign as " + name + ". The next start loads " + name + ", and its progress saves again.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            SetControl(ex.Message, failed: true);
        }
        finally
        {
            _busy = false;
        }
    }

    private void NameSave_Click(object sender, RoutedEventArgs e)
    {
        if (SaveBox.SelectedItem is not SaveChoice selected || !File.Exists(selected.FullPath))
        {
            SetControl("Pick a campaign save first.", failed: true);
            return;
        }

        if (!IsBeSaveFile(selected.FullPath))
        {
            SetControl(NotBeSave(selected.Name), failed: true);
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
            var serverModule = BeSaveData.ServerModule(_settings.ServerRoot);
            if (renamingServerCopy)
            {
                // Burning Empires' own data for the save (BESavedata) moves to the new name first, so a
                // name it already has data for stops the rename before any file moves.
                BeSaveData.Rename(serverModule, selected.Name, name);
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
                CarryBeSaveData(selected.FullPath, selected.Name, serverModule, name);
            }

            if (!ServerIsRunning() && IsConfiguredSave(selected.Name))
                PointConfigAt(name);
            ReloadSaves();
            NewCampaignBox.Clear();
            ClearNewCampaign();
            RememberSave(destination);
            SaveSettings();
            _actions.Note("Save", (renamingServerCopy ? "Renamed server copy to " : "Named server copy ") + name);
            var made = renamingServerCopy
                ? "Server copy renamed to " + name + ". The single player save was not changed."
                : "Server copy " + name + " was made. The single player save was not changed.";
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

        if (!IsBeSaveFile(selected.FullPath))
        {
            SetControl(NotBeSave(selected.Name), failed: true);
            return;
        }

        var serverDir = ServerSavesDirectory();
        if (!SaveSlot.IsInsideDirectory(selected.FullPath, serverDir) || SaveSlot.IsInsideDirectory(selected.FullPath, SinglePlayerSavesDirectory()))
        {
            SetControl("Single player campaign saves cannot be deleted. Delete removes a server copy only.", failed: true);
            return;
        }

        if (ServerIsRunning() && IsLiveSaveFamily(selected.Name))
        {
            SetControl("The running server is using " + ConfiguredSaveName() + ". That server copy stays.", failed: true);
            return;
        }

        // Coodsz Host and Coop's own server keep their saves in the same Game Saves folder.
        if (!ConfirmWindow.Ask(this, "Delete server copy", "Delete the server copy of " + selected.Name + "? The single player save is not touched. The Game Saves folder is shared with Coodsz Host. Only this Burning Empires save is deleted."))
            return;

        try
        {
            File.Delete(selected.FullPath);
            var json = Path.Combine(serverDir, selected.Name + ".json");
            if (File.Exists(json) && SaveSlot.IsInsideDirectory(json, serverDir))
                File.Delete(json);
            BeSaveData.Delete(BeSaveData.ServerModule(_settings.ServerRoot), selected.Name);
            ReloadSaves();
            SaveSettings();
            SetControl("Deleted the server copy of " + selected.Name + " and its Burning Empires data. Single player saves were not touched.");
        }
        catch (Exception ex)
        {
            SetControl(ex.Message, failed: true);
        }
    }

    // A save copied from single player brings its BESavedata slot from the game's Burning Empires
    // folder into the server's, under the server copy's name.
    private void CarryBeSaveData(string sourceSave, string fromName, string serverModule, string toName)
    {
        if (_gameRoot == null || !SaveSlot.IsInsideDirectory(sourceSave, SinglePlayerSavesDirectory()))
            return;
        var gameModule = Path.Combine(_gameRoot, "Modules", BeServerPath.BeModuleId);
        if (!BeSaveData.Has(gameModule, fromName))
            return;
        var scratch = Path.Combine(Path.GetTempPath(), "coodsz-be-savedata-" + Guid.NewGuid().ToString("N"));
        try
        {
            BeSaveData.Backup(gameModule, fromName, scratch);
            BeSaveData.Rename(scratch, fromName, toName);
            var copied = BeSaveData.Restore(scratch, serverModule, toName);
            if (copied > 0)
                NoteLog("Copied the Burning Empires save data of " + fromName + " to the server copy " + toName + ".");
        }
        finally
        {
            try
            {
                if (Directory.Exists(scratch))
                    Directory.Delete(scratch, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    // TAOM report #44 (the same code here): a copy of an earlier save silently became the save Start hosts. A copy
    // of a save earlier in its campaign than the one the server last ran asks first (SaveSwitchWarning).
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
    // passes the save it just put back. A tool pick, so a waiting new campaign stays as it was.
    private void SelectHostedSave(string hosted)
    {
        var choice = SaveBox.Items.OfType<SaveChoice>()
            .FirstOrDefault(item => item.PlaceRank == 0 && item.Name.Equals(hosted, StringComparison.OrdinalIgnoreCase));
        if (choice == null)
            return;
        _fillingSaves = true;
        try
        {
            SaveBox.SelectedItem = choice;
        }
        finally
        {
            _fillingSaves = false;
        }
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

    // The data folder and its Game Saves are shared with Coodsz Host and Coop's own server, so the
    // save box lists, renames and deletes only Burning Empires saves (the save names Burning Empires,
    // or the server's BESavedata has data for it), and the config is only pointed at one of those.
    private bool IsBeSaveFile(string path)
    {
        try
        {
            var serverModule = string.IsNullOrWhiteSpace(_settings.ServerRoot) ? null : BeSaveData.ServerModule(_settings.ServerRoot);
            return BeSaveVersion.IsBeOwned(path, serverModule);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return false;
        }
    }

    private static string NotBeSave(string name) =>
        name + " was not made with Burning Empires, so this tool leaves it as it is.";

    private void PointConfigAt(string saveName)
    {
        if (ServerIsRunning())
            return;
        if (!IsBeSaveFile(Path.Combine(ServerSavesDirectory(), saveName + ".sav")))
        {
            NoteLog(saveName + " is not a Burning Empires save, so the server config was left as it is.");
            return;
        }

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
        _fillingSaves = true;
        try
        {
            FillSaves();
        }
        finally
        {
            _fillingSaves = false;
        }
    }

    private void FillSaves()
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
                if (!IsBeSaveFile(file))
                    continue;
                choices.Add(new SaveChoice(file, SavePlace(file)));
            }
        }

        if (!string.IsNullOrWhiteSpace(_settings.SaveFile) && File.Exists(_settings.SaveFile) && IsBeSaveFile(_settings.SaveFile) && choices.All(choice => !choice.FullPath.Equals(_settings.SaveFile, StringComparison.OrdinalIgnoreCase)))
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
        // The host picked a save, so a waiting new campaign, or a name only typed in step 1, is not started.
        if (!_fillingSaves && SaveBox.SelectedItem != null)
        {
            ClearNewCampaign();
            NewCampaignBox.Clear();
        }
        ShowSaveDetail();
    }

    private void ShowSaveDetail()
    {
        var newCampaign = _settings.NewCampaignName.Trim();
        NewCampaignNote.Text = newCampaign.Length == 0
            ? ""
            : "Start server makes the new campaign " + newCampaign + ". Pick a save above to host that save instead.";
        NewCampaignNote.Visibility = newCampaign.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        // An empty save box with no new campaign named says what to do, so a first host does not go
        // off to make a single player save.
        SaveEmpty.Visibility = SaveBox.Items.Count == 0 && newCampaign.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        var path = SelectedSavePath();
        if (path.Length == 0 || newCampaign.Length > 0)
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

    /// <summary>The warning for a save that is not a Burning Empires save or was made on another
    /// Burning Empires release, or null when it can be hosted.</summary>
    private string? SaveVersionWarningText(string savePath)
    {
        if (!File.Exists(savePath))
            return null;
        return BeSaveVersion.Problem(savePath, InstalledBeVersion());
    }

    // The Burning Empires in the server folder, or the one this tool was made for when the folder
    // does not have it yet.
    private string InstalledBeVersion()
    {
        try
        {
            var subModule = Path.Combine(_settings.ServerRoot ?? "", "engine", "Modules", BeServerPath.BeModuleId, "SubModule.xml");
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

        return BeStack.BeVersionText;
    }

    /// <summary>At Start: asks before starting a save made on another Burning Empires release.</summary>
    private bool ConfirmSaveBeVersion(string savePath)
    {
        var warning = SaveVersionWarningText(savePath);
        if (warning == null)
            return true;
        NoteLog(warning);
        return ConfirmWindow.Ask(this, "This save is from another Burning Empires version",
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

        _fillingSaves = true;
        try
        {
            SaveBox.SelectedItem = existing;
        }
        finally
        {
            _fillingSaves = false;
        }
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
