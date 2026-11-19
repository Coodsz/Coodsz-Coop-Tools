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
    private void ReloadSaves()
    {
        var names = SaveCatalog.NamesIn(ServerSession.SavesPath);

        var forgotDeletedSave = false;
        foreach (var profile in _settings.Profiles)
        {
            if (string.IsNullOrWhiteSpace(profile.SaveName))
                continue;
            if (names.Contains(profile.SaveName, StringComparer.OrdinalIgnoreCase))
                continue;
            profile.SaveName = "";
            forgotDeletedSave = true;
        }

        if (forgotDeletedSave)
            HostSettings.Save(SettingsPath, _settings);

        ShowProfileSave(_settings.Current(), names);
    }

    private void ShowProfileSave(HostProfile profile, List<string>? names = null)
    {
        names ??= SaveBox.Items.Cast<object>().Select(item => item?.ToString() ?? "").Where(item => item.Length > 0).ToList();
        _loading = true;
        SaveBox.ItemsSource = names;
        SettingsSaveBox.ItemsSource = names;
        if (!string.IsNullOrWhiteSpace(profile.SaveName) && names.Contains(profile.SaveName, StringComparer.OrdinalIgnoreCase))
        {
            SaveBox.SelectedItem = profile.SaveName;
            SettingsSaveBox.SelectedItem = profile.SaveName;
        }
        else
        {
            SaveBox.SelectedIndex = -1;
            SettingsSaveBox.SelectedIndex = -1;
        }
        _loading = false;
        OverviewSave.Text = string.IsNullOrWhiteSpace(profile.SaveName) ? "-" : profile.SaveName;
        ShowSaveDetail(profile.SaveName);
    }

    private void ShowSaveDetail(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            ShowSaveFacts(null, missing: false);
            return;
        }

        ShowSaveFacts(CampaignSaveInfoReader.Read(Path.Combine(ServerSession.SavesPath, name + ".sav")), missing: true);
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

    private void ShowProfilePassword(HostProfile profile)
    {
        PasswordBox.Text = profile.Password ?? SharedServerPassword();
    }

    // Only a real change of the box (or Clear) is kept; the file's own password shown at load keeps null (ServerConfigFlags.PasswordToKeep).
    private void RememberPassword(HostProfile profile)
    {
        profile.Password = ServerConfigFlags.PasswordToKeep(profile.Password, PasswordBox.Text, SharedServerPassword());
    }

    private static void WriteServerPassword(string password)
    {
        if (!File.Exists(ServerSession.ServerConfigPath))
            return;
        var json = JsoncEditor.Read(ServerSession.ServerConfigPath);
        JsoncEditor.Write(ServerSession.ServerConfigPath, JsoncEditor.SetString(json, "password", password));
    }

    private static string SharedServerPassword()
    {
        if (!File.Exists(ServerSession.ServerConfigPath))
            return "";
        return JsoncEditor.GetString(JsoncEditor.Read(ServerSession.ServerConfigPath), "password");
    }

    private void NewSaveName_Changed(object sender, RoutedEventArgs e)
    {
        NewSaveHint.Visibility = NewSaveNameBox.Text.Length == 0 && !NewSaveNameBox.IsKeyboardFocused
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void NewSave_Click(object sender, RoutedEventArgs e)
    {
        // An empty name used to pick an automatic one and mark a new campaign straight away, even on a
        // locked setup. Nothing changes until the host types a name.
        var name = NewSaveNameBox.Text.Trim();
        if (name.Length == 0)
        {
            const string needName = "Type a name for the new save first, then click + New save.";
            SetControl(needName, failed: true);
            Append(needName);
            NewSaveNameBox.Focus();
            return;
        }
        if (!TryValidateSaveName(name, out var error))
        {
            SetControl(error, failed: true);
            Append(error);
            return;
        }

        var exists = File.Exists(Path.Combine(ServerSession.SavesPath, name + ".sav"));
        // A new campaign goes to the start script as -NewSaveName, which has its own name rule; a name it refuses
        // used to fail only after Start server was pressed.
        if (!exists && SaveSet.NewSaveNameProblem(name) is { } rule)
        {
            SetControl(rule, failed: true);
            Append(rule);
            NewSaveNameBox.Focus();
            return;
        }
        var names = SaveBox.Items.Cast<object>().Select(item => item?.ToString() ?? "").Where(item => item.Length > 0).ToList();
        if (!names.Contains(name, StringComparer.OrdinalIgnoreCase))
            names.Insert(0, name);
        _loading = true;
        SaveBox.ItemsSource = names;
        SettingsSaveBox.ItemsSource = names;
        SaveBox.SelectedItem = name;
        SettingsSaveBox.SelectedItem = name;
        _loading = false;
        var profile = _settings.Current();
        profile.SaveName = name;
        profile.NewCampaign = !exists;
        OverviewSave.Text = name;
        NewSaveNameBox.Text = "";
        HostSettings.Save(SettingsPath, _settings);
        var note = exists
            ? name + " already exists, so Start server will load it."
            : name + " is selected. Click Start server to create it.";
        _actions.Note("Save", (exists ? "Selected existing " : "Selected new ") + name + " on " + profile.Name);
        SetControl(note);
        Append(note);
    }

    private void LoadSinglePlayerSave_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Load a single-player campaign save",
            Filter = "Campaign save|*.sav",
            InitialDirectory = Directory.Exists(ClientSync.ClientSaves)
                ? ClientSync.ClientSaves
                : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };
        if (dialog.ShowDialog(this) != true || string.IsNullOrWhiteSpace(dialog.FileName))
            return;

        var typed = NewSaveNameBox.Text.Trim();
        var name = typed.Length == 0 ? Path.GetFileNameWithoutExtension(dialog.FileName) : typed;
        if (!TryValidateSaveName(name, out var error))
        {
            SetControl(error, failed: true);
            Append(error);
            return;
        }

        try
        {
            var imported = SaveCatalog.ImportSinglePlayer(dialog.FileName, ServerSession.SavesPath, name);
            if (!imported.Ok)
            {
                SetControl(imported.Message, failed: true);
                Append(imported.Message);
                return;
            }

            var names = SaveBox.Items.Cast<object>().Select(item => item?.ToString() ?? "").Where(item => item.Length > 0).ToList();
            if (!names.Contains(name, StringComparer.OrdinalIgnoreCase))
                names.Insert(0, name);
            _loading = true;
            SaveBox.ItemsSource = names;
            SettingsSaveBox.ItemsSource = names;
            SaveBox.SelectedItem = name;
            SettingsSaveBox.SelectedItem = name;
            _loading = false;
            var profile = _settings.Current();
            profile.SaveName = name;
            profile.NewCampaign = false;
            OverviewSave.Text = name;
            NewSaveNameBox.Text = "";
            HostSettings.Save(SettingsPath, _settings);
            ShowSaveDetail(name);
            _actions.Note("Save", "Imported single-player save as " + name + " for " + profile.Name);
            var note = imported.Message.Length > 0
                ? imported.Message
                : _session.IsRunning
                    ? name + " was copied into the server Game Saves folder. The single-player save was not changed. This running server keeps its current campaign until you stop it and press Start server."
                    : name + " was copied into the server Game Saves folder. The single-player save was not changed. Start server will load it.";
            var header = CampaignSaveInfoReader.Read(Path.Combine(ServerSession.SavesPath, name + ".sav"));
            if (string.Equals(profile.CampaignMap, StableCoop.MoreNationsMap, StringComparison.OrdinalIgnoreCase)
                && header != null
                && header.Modules.All(id => id.IndexOf("MoreNations", StringComparison.OrdinalIgnoreCase) < 0))
                note += " This save does not list More Nations, so it keeps the map it was created with.";
            // The Sandbox only line goes under the note, never blocking (Story Mode can't be read from the header).
            SetControl(note + "\n" + SaveSet.SandboxOnlyNote);
            Append(note);
            Append(SaveSet.SandboxOnlyNote);
        }
        catch (Exception ex)
        {
            SetControl(ex.Message, failed: true);
            Append(ex.Message);
        }
    }

    private void DeleteSave_Click(object sender, RoutedEventArgs e)
    {
        if (SaveBox.SelectedItem is not string name || name.Length == 0)
        {
            SetControl("Select a campaign save to delete.", failed: true);
            return;
        }

        if (name.Equals("default_new_game", StringComparison.OrdinalIgnoreCase))
        {
            SetControl("The new-game template stays. Pick a campaign save.", failed: true);
            return;
        }

        if (_session.IsRunning && name.Equals(_launchedSave.Length == 0 ? _settings.Current().SaveName : _launchedSave, StringComparison.OrdinalIgnoreCase))
        {
            SetControl("Stop the server before deleting the save it is running.", failed: true);
            return;
        }

        if (!ConfirmWindow.Ask(this, "Delete save", "Delete " + name + "? This removes that dedicated-server save."))
            return;

        try
        {
            if (Directory.Exists(ServerSession.SavesPath))
            {
                foreach (var path in Directory.GetFiles(ServerSession.SavesPath))
                {
                    var file = Path.GetFileName(path);
                    var stem = Path.GetFileNameWithoutExtension(file);
                    var matches = file.Equals(name + ".sav", StringComparison.OrdinalIgnoreCase)
                        || file.Equals(name + ".json", StringComparison.OrdinalIgnoreCase)
                        || file.Equals(name + ".launcher.json", StringComparison.OrdinalIgnoreCase)
                        || stem.StartsWith(name + ".backup", StringComparison.OrdinalIgnoreCase)
                        || stem.Equals(name + ".last-good", StringComparison.OrdinalIgnoreCase);
                    if (matches)
                        File.Delete(path);
                }
            }

            foreach (var profile in _settings.Profiles)
            {
                if (name.Equals(profile.SaveName, StringComparison.OrdinalIgnoreCase))
                    profile.SaveName = "";
            }

            ForgetSaveProfile(name);

            HostSettings.Save(SettingsPath, _settings);
            ReloadSaves();
            SetControl(name + " was deleted.");
            Append(name + " was deleted.");
        }
        catch (Exception ex)
        {
            SetControl(ex.Message, failed: true);
            Append(ex.Message);
        }
    }

    private static bool TryValidateSaveName(string name, out string error)
    {
        error = "";
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.Contains('"'))
        {
            error = "That save name cannot be used as a file name.";
            return false;
        }

        if (name.Equals("default_new_game", StringComparison.OrdinalIgnoreCase))
        {
            error = "Pick a campaign name. default_new_game is the template.";
            return false;
        }

        return true;
    }

    private void KeepDropdownSelection(object sender, RequestBringIntoViewEventArgs e) => e.Handled = true;
}
