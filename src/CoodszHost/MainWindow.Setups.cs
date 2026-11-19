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
    private void ProfileBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading)
            return;
        if (ProfileBox.SelectedItem is not string name)
        {
            _loading = true;
            ProfileBox.SelectedItem = _settings.ActiveProfile;
            _loading = false;
            return;
        }
        var profile = _settings.Profiles.FirstOrDefault(item => item.Name == name);
        if (profile == null)
            return;
        HideSaveModCard();
        RememberPassword(_settings.Current());
        _settings.ActiveProfile = profile.Name;
        ApplyLaunchDefaults(profile);
        ApplyProfileToFields(profile);
        ApplyProfileModules(profile);
        BindMapBox(profile);
        ApplyCampaignMap(profile);
        ShowProfileSave(profile);
        ShowProfilePassword(profile);
        RefreshMcm_Click(this, new RoutedEventArgs());
        HostSettings.Save(SettingsPath, _settings);
        _actions.Note("Profile", "Selected " + profile.Name + (profile.Locked ? " (locked)" : "")
            + ". Save=" + (string.IsNullOrWhiteSpace(profile.SaveName) ? "(empty)" : profile.SaveName)
            + ". Map=" + (string.IsNullOrWhiteSpace(profile.CampaignMap) ? "(none)" : profile.CampaignMap)
            + ". NewCampaign=" + profile.NewCampaign);
        ModuleRefreshStatus.Visibility = Visibility.Collapsed;
        if (profile.Locked)
        {
            // While the mod list runs the pick is for the next Start: only the list is read again, and Start copies the
            // setup's missing mods once the server has let go of the Modules folder.
            ReloadModules(install: !ModListRunning);
            RefreshModuleSaveHint();
        }
        else if (_repairedWorkingOrder)
            ShowWorkingOrderHint(putBack: true);
        else
            ModuleSaveHint.Visibility = Visibility.Collapsed;
        RefreshState();
    }

    private void NewSetup_Click(object sender, RoutedEventArgs e)
    {
        RememberPassword(_settings.Current());
        var name = StableCoop.NextProfileName(_settings.Profiles.Select(profile => profile.Name));
        var created = StableCoop.CreateFresh(name);
        ApplyLaunchDefaults(created);
        SelectProfile(created);
        _actions.Note("Setup", "Created setup " + name);
        Append(Edition.IsAD1259
            ? name + " is a new setup. The mod order is back to the default order. The map is the AD1259 map and the save starts empty."
            : name + " is a new setup. The mod order is back to the default order. The map is the vanilla campaign map and the save starts empty.");
    }

    private void NewProfile_Click(object sender, RoutedEventArgs e)
    {
        if (_settings.Current().Locked)
        {
            Append("This setup is locked. It cannot be copied.");
            return;
        }

        var name = "Profile " + (_settings.Profiles.Count + 1);
        var copy = _settings.Current();
        var created = new HostProfile
        {
            Name = name,
            Locked = false,
            SaveName = "",
            NewCampaign = false,
            // Null: a new profile keeps the server-config.json password until the host types or clears one.
            Password = null,
            EnginePort = copy.EnginePort,
            CoopPort = copy.CoopPort,
            Region = copy.Region,
            CampaignMap = "",
            ModuleOrder = copy.ModuleOrder.ToList(),
            DisabledModules = copy.DisabledModules.ToList(),
            McmFolder = Guid.NewGuid().ToString("N"),
            PlayersOwnSettings = copy.PlayersOwnSettings.ToList(),
            Rbm = copy.Rbm == null
                ? null
                : new RbmSettings { Combat = copy.Rbm.Combat, Ai = copy.Rbm.Ai, Campaign = copy.Rbm.Campaign, Tournament = copy.Rbm.Tournament }
        };
        McmCatalog.CopySnapshot(EnsureMcmFolder(copy), created.McmFolder);
        SelectProfile(created);
        _actions.Note("Setup", "Created blank profile " + name);
        Append(name + " is blank. Its MCM was copied. Make a new campaign save and pick a campaign map before you start.");
    }

    private void SelectProfile(HostProfile created)
    {
        _settings.Profiles.Add(created);
        _settings.ActiveProfile = created.Name;
        _loading = true;
        ProfileBox.ItemsSource = _settings.Profiles.Select(profile => profile.Name).ToList();
        ProfileBox.SelectedItem = created.Name;
        _loading = false;
        ApplyProfileToFields(created);
        ApplyProfileModules(created);
        BindMapBox(created);
        ApplyCampaignMap(created);
        ShowProfileSave(created);
        ShowProfilePassword(created);
        RefreshMcm_Click(this, new RoutedEventArgs());
        HostSettings.Save(SettingsPath, _settings);
        ModuleRefreshStatus.Visibility = Visibility.Collapsed;
        if (_repairedWorkingOrder)
            ShowWorkingOrderHint(putBack: true);
        else
            ModuleSaveHint.Visibility = Visibility.Collapsed;
        RefreshState();
    }

    private void SaveProfile_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            PersistProfile();
            _actions.Note("Profile", "Saved settings for " + _settings.Current().Name);
            Append("Profile saved. Restart the server so these changes apply.");
        }
        catch (Exception ex)
        {
            Append(ex.Message);
        }
    }

    private void PersistProfile(bool allowLockedStart = false)
    {
        var profile = _settings.Current();
        if (profile.Locked && !allowLockedStart)
            throw new InvalidOperationException("This setup is locked. It cannot be changed.");
        if (!profile.Locked)
        {
            var previous = profile.Name;
            CaptureProfile(profile);
            if (!profile.Name.Equals(previous, StringComparison.Ordinal) &&
                _settings.Profiles.Any(item => item != profile && item.Name.Equals(profile.Name, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("A profile with that name already exists.");
        }
        _settings.ActiveProfile = profile.Name;
        if (!File.Exists(Executable))
            throw new InvalidOperationException(DedicatedServerFinder.MissingMessage);
        ModuleCatalog.Write(ServerRoot, _modules.ToList());
        WriteServerConfig(profile);
        HostSettings.Save(SettingsPath, _settings);
        _loading = true;
        ProfileBox.ItemsSource = _settings.Profiles.Select(item => item.Name).ToList();
        ProfileBox.SelectedItem = profile.Name;
        _loading = false;
        RefreshState();
        DependencyNote.Text = ModuleCatalog.Validate(_modules.ToList());
    }

    private void WriteServerConfig(HostProfile profile)
    {
        if (!File.Exists(ServerSession.ServerConfigPath))
            return;
        var json = JsoncEditor.Read(ServerSession.ServerConfigPath);
        if (!string.IsNullOrWhiteSpace(profile.SaveName))
            json = JsoncEditor.SetString(json, "saveName", profile.SaveName);
        if (JsoncEditor.GetRaw(json, "port").Length > 0)
            json = JsoncEditor.SetNumber(json, "port", profile.CoopPort.ToString());
        JsoncEditor.Write(ServerSession.ServerConfigPath, json);
    }

    private void DeleteProfile_Click(object sender, RoutedEventArgs e)
    {
        if (_settings.Profiles.Count < 2)
        {
            Append("Keep at least one profile.");
            return;
        }

        var current = _settings.Current();
        if (current.Locked)
        {
            Append("This setup is locked. It cannot be renamed or deleted.");
            return;
        }
        if (!ConfirmWindow.Ask(this, "Delete setup", "Delete " + current.Name + "? This also deletes its MCM settings. This cannot be undone.", null, "Delete"))
            return;
        McmCatalog.DeleteSnapshot(current.McmFolder);
        _settings.Profiles.Remove(current);
        _actions.Note("Setup", "Deleted profile " + current.Name);
        _settings.ActiveProfile = _settings.Profiles[0].Name;
        _loading = true;
        ProfileBox.ItemsSource = _settings.Profiles.Select(profile => profile.Name).ToList();
        ProfileBox.SelectedItem = _settings.ActiveProfile;
        _loading = false;
        ApplyProfileToFields(_settings.Current());
        ApplyProfileModules(_settings.Current());
        BindMapBox(_settings.Current());
        ApplyCampaignMap(_settings.Current());
        ShowProfileSave(_settings.Current());
        ShowProfilePassword(_settings.Current());
        RefreshMcm_Click(this, new RoutedEventArgs());
        HostSettings.Save(SettingsPath, _settings);
    }
}
