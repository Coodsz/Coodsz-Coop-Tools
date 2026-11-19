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
using BannerlordCoopLauncher.Core.Localization;
using BannerlordCoopLauncher.Core.Saves;
using BannerlordCoopLauncher.Core.Support;
using BannerlordCoopLauncher.Core.Updates;
using BannerlordCoopLauncher.Core.Modules;
using CoodszHost.Services;
using Microsoft.Win32;

namespace CoodszHost;

public partial class MainWindow
{
    /// <summary>Reads the server's mods again. With install on, a locked setup first copies its missing working mods
    /// into the server's Modules folder, but never while a server has those files open: a setup picked while the server
    /// runs only changes what the next Start uses, and Start copies them then.</summary>
    private void ReloadModules(bool install = true)
    {
        // A mod copy still runs (WhileCopying); Start reloads once it is done.
        if (_copyingMods)
            return;
        // Plain Coop loads only what the server ships, so it copies none of the working mods in.
        // A running server keeps its modules: Check mods, a setup switch or Set server folder only read the list then,
        // since a copy replaces the server copy (Install deletes it first). Start copies what is missing anyway.
        if (install && !ServerHoldsModFiles && _settings.Current().Locked && !StableCoop.IsPlain(_settings.Current()) && !string.IsNullOrWhiteSpace(ServerRoot))
        {
            var mapProblems = new List<MapCopyProblem>();
            NoteLockedSetupCopy(ExtraModScan.InstallLockedSetup(ServerRoot, mapProblems, CopyIntoServer));
            NoteMapProblems(mapProblems, "Locked setup");
            if (Edition.IsAD1259)
                InstallAD1259Compat();
        }
        _modules.Clear();
        foreach (var row in ModuleCatalog.Load(ServerRoot))
            _modules.Add(row);
        _gameCoop = GameModulesCoop(out _newerGame);
        _steamServerCoop = SteamServerCoop();
        ApplyProfileModules(_settings.Current());
        // The AD1259 set is locked, so the local client's mod choices never change it.
        if (!Edition.IsAD1259)
            ClientSync.Apply(_modules);
        ApplyProfileModules(_settings.Current());
        BindMapBox(_settings.Current());
        ApplyCampaignMap(_settings.Current());
        RefreshModuleView(this, new RoutedEventArgs());
        DependencyNote.Text = ModuleCatalog.Validate(_modules.ToList());
        if (!string.IsNullOrWhiteSpace(ModuleCatalog.DuplicateNote))
            DependencyNote.Text = ModuleCatalog.DuplicateNote + " " + DependencyNote.Text;
        RefreshModuleSaveHint();
        UpdateModuleSummary();
        RefreshBattleSizeMode();
        // Set server folder, Use the Steam server, a Coop update or the locked setup copy above may change the
        // server's Coop version, so the settings a newer Coop added are greyed or offered again to match it.
        ShowCoopSettingSupport();
    }

    // The version of the Coop in the game's Modules folder, read on each module reload for the line under Active Coop
    // (CoopInstall.ServerBehindLine). Null when the game or its Modules\Coop is not found. The same game gives the newer
    // game line (NewerGame) for the Made for card and Start server; the server's own Native is 1.4.8 from the Workshop.
    private string? _gameCoop;
    private string? _newerGame;

    private static string? GameModulesCoop(out string? newerGame)
    {
        newerGame = null;
        try
        {
            var modules = ExtraModScan.GameModulesDirectory();
            if (modules == null)
                return null;
            newerGame = BannerlordCoopLauncher.Core.Steam.NewerGame.Notice(Path.GetDirectoryName(modules));
            return BannerlordCoopLauncher.Core.Modules.CoopInstall.InModules(Path.GetDirectoryName(modules))?.Version;
        }
        catch (Exception)
        {
            // Only a hint line: a registry, path or file error never breaks the module reload.
            return null;
        }
    }

    // The Coop version of the Workshop item's DedicatedServer (engine\Modules\Coop), read on each module reload for the
    // Use the Steam server line under Active Coop (CoopInstall.ChosenServerOlderLine). Null when there is none.
    private string? _steamServerCoop;

    // The Use the Steam server line under Active Coop, or null, and whether the AD1259 tool sees the server's Coop as the
    // nightly (AD1259Server.IsNightlyServer). Start's nightly refusal asks the same, so it only names the button when it shows.
    private (bool Nightly, string? Line) SteamServerState()
    {
        var coop = _modules.FirstOrDefault(row => row.Id.Equals("Coop", StringComparison.OrdinalIgnoreCase));
        var nightly = Edition.IsAD1259 && AD1259Server.IsNightlyServer(_modules.ToList(), CoopBuildId.FromServer(ServerRoot));
        var line = coop == null && !nightly ? null : CoopInstall.ChosenServerOlderLine(ServerRoot, coop?.Version, _steamServerCoop, Edition.IsAD1259, nightly);
        return (nightly, line);
    }

    private static string? SteamServerExe()
    {
        var workshop = AntivirusFiles.WorkshopCoop(BannerlordFinder.SteamLibraries());
        if (workshop == null)
            return null;
        var exe = AntivirusFiles.WorkshopServerExe(workshop);
        return File.Exists(exe) ? exe : null;
    }

    private static string? SteamServerCoop()
    {
        try
        {
            var exe = SteamServerExe();
            if (exe == null)
                return null;
            var version = CoopInstall.Read(Path.Combine(ModuleCatalog.ModulesRoot(Path.GetDirectoryName(exe)!), "Coop"), inModules: false)?.Version;
            // Nightly files over the Workshop Coop keep its SubModule.xml, so the AD1259 tool takes the build's version then:
            // the line stays hidden and Use the Steam server refuses it, like a Workshop Coop newer than the one it pins.
            var build = Edition.IsAD1259 ? CoopBuildId.FromServer(Path.GetDirectoryName(exe)) : null;
            return build != null && BannerlordCoopLauncher.Core.AD1259.AD1259Stack.IsNightlyCoop(version, build) && !BannerlordCoopLauncher.Core.AD1259.AD1259Stack.IsNightlyCoop(version) ? CoopBuildId.Version(build) : version;
        }
        catch (Exception)
        {
            // Only a hint line, as above.
            return null;
        }
    }

    // Use the Steam server: the tool goes back to the Workshop item's server instead of the folder the host chose with
    // Set server folder. Only the saved path changes; the chosen folder stays on disk.
    private const string UseSteamServerRunning = "Stop the server first, then click Use the Steam server.";

    private void UseSteamServer_Click(object sender, RoutedEventArgs e)
    {
        // The button is off while the server runs (ApplyModListBlock); this covers a click in the same moment.
        if (ModListRunning || _copyingMods)
        {
            SetControl(UseSteamServerRunning, failed: true);
            return;
        }

        string? steam;
        try
        {
            steam = SteamServerExe();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            steam = null;
        }

        if (steam == null)
        {
            SetControl(DedicatedServerFinder.MissingMessage, failed: true);
            return;
        }

        // Steam swaps the Workshop Coop's files while it updates it, so the server folder waits until that is done. The
        // status line is on the Dashboard, so the answer shows where the host clicked.
        if (AntivirusFiles.SteamUpdating(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(steam)!, ".."))))
        {
            var wait = AntivirusText.WorkshopDownloading(CoopInstall.UseSteamServerButton);
            SetControl(wait, failed: true);
            ConfirmWindow.Show(this, CoopInstall.UseSteamServerButton, wait);
            return;
        }

        // Steam may have updated the Workshop Coop since the line was shown, so its version is read again here. The AD1259
        // tool never saves a Workshop server newer than the Coop it pins (or one it can't read), since it won't start that one.
        _steamServerCoop = SteamServerCoop();
        if (CoopInstall.UseSteamServerProblem(_steamServerCoop, Edition.IsAD1259) is { } problem)
        {
            SetControl(problem, failed: true);
            ConfirmWindow.Show(this, CoopInstall.UseSteamServerButton, problem);
            RefreshState();
            return;
        }

        var picked = ServerRoot;
        _settings.ServerExecutable = steam;
        HostSettings.Save(SettingsPath, _settings);
        _actions.Note(CoopInstall.UseSteamServerButton, "Server folder set to the Steam Workshop server " + Path.GetDirectoryName(steam) + ". The picked folder " + picked + " was left on disk.");
        ReloadModules();
        RefreshState();
        SetControl("Using the Steam Workshop server at " + Path.GetDirectoryName(steam) + ".");
    }

    private void UpdateModuleSummary()
    {
        var ready = _modules.Count(row => row.Enabled);
        OverviewMods.Text = ready.ToString();
        ModuleSummaryText.Text = ready.ToString();
    }

    private List<(string Id, string Label)> MapChoices()
    {
        var choices = new List<(string Id, string Label)> { (VanillaMapId, VanillaMap) };
        foreach (var row in _modules)
        {
            // SandBox's own Main_map is the extra copy Start moves aside (ExtraBaseMap), never a map to pick.
            if (row.Id.Equals("DedicatedServer.Windows", StringComparison.OrdinalIgnoreCase)
                || row.Id.Equals("SandBox", StringComparison.OrdinalIgnoreCase))
                continue;
            var map = Path.Combine(ModuleCatalog.ModulesRoot(ServerRoot), row.Id, "SceneObj", "Main_map");
            if (Directory.Exists(map))
                choices.Add((row.Id, MapLabel(row)));
        }

        return choices;
    }

    private static string MapLabel(ModuleRow row)
    {
        if (row.Id.Equals("MoreNationsRemastered", StringComparison.OrdinalIgnoreCase))
            return "More Nations Remastered Map";
        var name = string.IsNullOrWhiteSpace(row.Name) ? row.Id : row.Name.Trim();
        return name.EndsWith(" Map", StringComparison.OrdinalIgnoreCase) ? name : name + " Map";
    }

    private void BindMapBox(HostProfile profile)
    {
        var choices = MapChoices();
        _loading = true;
        var labels = choices.Select(choice => choice.Label).ToList();
        var index = string.IsNullOrWhiteSpace(profile.CampaignMap)
            ? -1
            : choices.FindIndex(choice => string.Equals(choice.Id, profile.CampaignMap, StringComparison.OrdinalIgnoreCase));
        // A map the setup keeps but the server has no SceneObj\Main_map for still shows its name, not a blank box.
        if (index < 0 && !string.IsNullOrWhiteSpace(profile.CampaignMap)
            && (profile.Locked || (Edition.IsAD1259 && profile.CampaignMap.Equals(AD1259Server.CampaignMap, StringComparison.OrdinalIgnoreCase))))
        {
            labels.Add(KeptMapLabel(profile));
            index = labels.Count - 1;
        }
        MapBox.ItemsSource = labels;
        MapBox.SelectedIndex = index;
        // The AD1259 map stays picked before the AD1259 mod reaches the server: Start copies it in, then checks the map.
        if (index < 0 && !profile.Locked && !string.IsNullOrWhiteSpace(profile.CampaignMap)
            && !(Edition.IsAD1259 && profile.CampaignMap.Equals(AD1259Server.CampaignMap, StringComparison.OrdinalIgnoreCase)))
            profile.CampaignMap = "";
        _loading = false;
        ShowMapSaveInfo(profile);
    }

    public const string MissingMapText = "{0} (map files missing on the server)";

    // A locked setup names a map whose files are missing on the server (Coop crash report CR-JTQ45). AD1259 setups are locked
    // too, so before the first Start copies the AD1259 mod in, the box shows its map with (map files missing on the server).
    private string KeptMapLabel(HostProfile profile)
    {
        var id = profile.CampaignMap!;
        var row = _modules.FirstOrDefault(module => module.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        var name = row != null ? MapLabel(row) : id.EndsWith(" Map", StringComparison.OrdinalIgnoreCase) ? id : id + " Map";
        return profile.Locked ? string.Format(Translator.T(MissingMapText), name) : name;
    }

    private void ShowMapSaveInfo(HostProfile profile)
    {
        var moreNations = string.Equals(profile.CampaignMap, StableCoop.MoreNationsMap, StringComparison.OrdinalIgnoreCase);
        MoreNationsSaveInfo.Visibility = moreNations ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ApplyCampaignMap(HostProfile profile)
    {
        var maps = MapChoices().Where(choice => !choice.Id.Equals(VanillaMapId, StringComparison.OrdinalIgnoreCase)).Select(choice => choice.Id).ToList();
        var selected = profile.CampaignMap ?? "";
        if (string.IsNullOrWhiteSpace(selected))
            return;
        if (!maps.Any(id => id.Equals(selected, StringComparison.OrdinalIgnoreCase)))
            selected = VanillaMapId;
        foreach (var row in _modules.Where(row => !row.Required))
        {
            var isMap = maps.Any(id => id.Equals(row.Id, StringComparison.OrdinalIgnoreCase));
            var followsFix = row.Id.Equals("MoreNationsRemasteredFix", StringComparison.OrdinalIgnoreCase);
            var followsMap = followsFix || row.Dependencies.Any(dependency => maps.Any(id => id.Equals(dependency, StringComparison.OrdinalIgnoreCase)));
            if (!isMap && !followsMap)
                continue;
            var owner = followsFix
                ? "MoreNationsRemastered"
                : isMap
                ? row.Id
                : row.Dependencies.First(dependency => maps.Any(id => id.Equals(dependency, StringComparison.OrdinalIgnoreCase)));
            row.Enabled = owner.Equals(selected, StringComparison.OrdinalIgnoreCase);
        }
    }

    private void MapBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || MapBox.SelectedItem is not string label)
            return;
        var choice = MapChoices().FirstOrDefault(item => item.Label == label);
        var profile = _settings.Current();
        if (profile.Locked)
        {
            _loading = true;
            BindMapBox(profile);
            _loading = false;
            return;
        }
        // The kept map line (KeptMapLabel) is not a map to switch to.
        if (choice.Id == null)
        {
            BindMapBox(profile);
            return;
        }
        if (!choice.Id.Equals(VanillaMapId, StringComparison.OrdinalIgnoreCase))
        {
            if (!ConfirmMapSwitch(choice.Label))
            {
                // Back to what the setup had, the kept map line included (a missing map has no MapChoices entry).
                BindMapBox(profile);
                return;
            }
        }
        profile.CampaignMap = choice.Id;
        ShowMapSaveInfo(profile);
        ApplyCampaignMap(profile);
        if (!profile.Locked)
        {
            if (!File.Exists(Executable))
            {
                SetControl(DedicatedServerFinder.MissingMessage, failed: true);
                return;
            }
            profile.DisabledModules = _modules.Where(row => !row.Required && !row.Enabled).Select(row => row.Id).ToList();
            ModuleCatalog.Write(ServerRoot, _modules.ToList());
        }
        HostSettings.Save(SettingsPath, _settings);
        ModuleList.Items.Refresh();
        DependencyNote.Text = ModuleCatalog.Validate(_modules.ToList());
        RefreshModuleSaveHint();
        RefreshBattleSizeMode();
        RefreshState();
    }

    private void ModuleSwitch_Click(object sender, RoutedEventArgs e)
    {
        RefreshModuleSaveHint();
        UpdateModuleSummary();
        RefreshBattleSizeMode();
    }

    private void RefreshModuleSaveHint()
    {
        var profile = _settings.Current();
        ModuleSaveHint.ToolTip = null;
        if (profile.Locked)
        {
            SetModuleSaveHint("This setup is locked. The working mods cannot be changed.");
            ModuleSaveHint.Visibility = Visibility.Visible;
            return;
        }

        if (_repairedWorkingOrder)
        {
            ShowWorkingOrderHint(putBack: true);
            return;
        }

        SetModuleSaveHint("Please save to keep these changes. Click Save module config, then restart the server.");
        var disabled = _modules.Where(row => !row.Required && !row.Enabled).Select(row => row.Id);
        var disabledSame = new HashSet<string>(profile.DisabledModules, StringComparer.OrdinalIgnoreCase).SetEquals(disabled);
        var orderSame = profile.ModuleOrder.Count == 0
            || profile.ModuleOrder.SequenceEqual(_modules.Select(row => row.Id), StringComparer.OrdinalIgnoreCase);
        ModuleSaveHint.Visibility = disabledSame && orderSame ? Visibility.Collapsed : Visibility.Visible;
    }

    private bool ConfirmMapSwitch(string mapName)
    {
        var confirmed = false;
        var ivory = new SolidColorBrush(Color.FromRgb(213, 222, 230));
        var muted = new SolidColorBrush(Color.FromRgb(142, 160, 174));
        var bronze = new SolidColorBrush(Color.FromRgb(42, 56, 68));
        var gold = new SolidColorBrush(Color.FromRgb(226, 194, 122));
        var dialog = new Window
        {
            Title = "Different campaign map",
            Width = 480,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            ResizeMode = ResizeMode.NoResize,
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            ShowInTaskbar = false
        };
        Coodsz.Ui.WindowPlacement.KeepOnScreen(dialog);
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 18, 0, 0)
        };
        Button Make(string content, bool yes, bool filled, string tip)
        {
            var button = new Button
            {
                Content = content,
                ToolTip = tip,
                Margin = new Thickness(8, 0, 0, 0),
                Padding = new Thickness(18, 8, 18, 8),
                Cursor = Cursors.Hand,
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 14,
                Foreground = ivory,
                Background = filled
                    ? new SolidColorBrush(Color.FromRgb(31, 78, 121))
                    : new SolidColorBrush(Color.FromArgb(153, 20, 16, 14)),
                BorderBrush = bronze,
                BorderThickness = new Thickness(1)
            };
            button.Click += (_, _) =>
            {
                confirmed = yes;
                dialog.Close();
            };
            button.MouseEnter += (_, _) =>
            {
                button.BorderBrush = gold;
                button.BorderThickness = new Thickness(2);
            };
            button.MouseLeave += (_, _) =>
            {
                button.BorderBrush = bronze;
                button.BorderThickness = new Thickness(1);
            };
            return button;
        }
        buttons.Children.Add(Make("Yes", true, true, "Switches this setup to that map for the next start."));
        buttons.Children.Add(Make("No", false, false, "Keeps the map this setup had."));
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = "Different campaign map",
            FontFamily = new FontFamily("Palatino Linotype"),
            FontSize = 22,
            Foreground = ivory,
            Margin = new Thickness(0, 0, 0, 12)
        });
        stack.Children.Add(new TextBlock
        {
            Text = mapName + " is not included in " + ClientToolName + ". Friends on that launcher are rejected at the module check and never enter the world. If you want that map, ask Coodsz in Discord.\n\nThe save that is already running is not changed. Switch the map for the next start anyway?",
            TextWrapping = TextWrapping.Wrap,
            Foreground = muted,
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 14,
            LineHeight = 22
        });
        stack.Children.Add(buttons);
        dialog.Content = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(21, 29, 36)),
            BorderBrush = gold,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(22),
            Child = stack
        };
        dialog.ShowDialog();
        return confirmed;
    }

    private void ApplyProfileModules(HostProfile profile)
    {
        if (profile.Locked)
            StableCoop.Ensure(_settings);
        var order = profile.Locked ? StableCoop.OrderFor(profile).ToArray() : profile.ModuleOrder.ToArray();
        var ranked = order.Select((id, index) => (id, index)).ToDictionary(item => item.id, item => item.index, StringComparer.OrdinalIgnoreCase);
        var ordered = _modules.OrderBy(row => ranked.TryGetValue(row.Id, out var index) ? index : int.MaxValue).ThenBy(row => row.Name).ToList();
        var installedIds = ordered.Select(row => row.Id).ToList();
        _modules.Clear();
        foreach (var row in ordered)
        {
            if (row.Required && StableCoop.KeepsRequired(profile, row.Id))
                row.Enabled = true;
            else if (profile.Locked)
                row.Enabled = StableCoop.ShouldEnable(profile, row.Id, installedIds);
            else
                row.Enabled = StableCoop.UnlockedEnables(profile, row.Id);
            // A blocked list (the server runs) keeps every switch and move off; ApplyModListBlock turns them back on after Stop.
            row.CanToggle = !profile.Locked && !row.Required && !ModListRunning;
            row.CanMove = !profile.Locked && !ModListRunning;
            row.CanDelete = ModuleRemoval.Refusal(row.Id, row.Required, profile.Locked, serverRunning: false, StableCoop.Order) == null;
            _modules.Add(row);
        }
        _repairedWorkingOrder = !profile.Locked && RestoreWorkingOrder();
        StampModuleOrder();
        PublishLockedModuleList(profile);
        NoteRetiredSettingsLock(profile);
    }

    // COOP Server Settings Lock left the setup, and the players can not get it any more: once per setup each time
    // the tool starts, for as long as the setup still turns it on.
    private readonly HashSet<string> _settingsLockNoted = new(StringComparer.OrdinalIgnoreCase);

    private void NoteRetiredSettingsLock(HostProfile profile)
    {
        var enabled = _modules.Where(row => row.Enabled).Select(row => row.Id);
        if (BannerlordCoopLauncher.Core.Profile.RetiredSettingsLock.ShowNote(profile.Name, profile.Locked, enabled, _settingsLockNoted))
            Append(BannerlordCoopLauncher.Core.Profile.RetiredSettingsLock.HostNote);
    }

    private void PublishLockedModuleList(HostProfile profile)
    {
        if (!profile.Locked || _modules.Count == 0 || string.IsNullOrWhiteSpace(ServerRoot) || !Directory.Exists(ServerRoot))
            return;

        var path = ModuleCatalog.ListPath(ServerRoot);
        var before = File.Exists(path) ? File.ReadAllText(path) : "";
        try
        {
            ModuleCatalog.Write(ServerRoot, _modules.ToList());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Append("The locked mod list was not saved into the dedicated server folder. " + ex.Message);
            return;
        }

        var after = File.Exists(path) ? File.ReadAllText(path) : "";
        if (!string.Equals(before, after, StringComparison.Ordinal))
            Append($"Saved the locked mod list into the dedicated server folder. Start the server from {Edition.Name}. Starting the Workshop copy uses the old 5-mod list.");
    }

    private void NoteLockedSetupCopy((IReadOnlyList<string> Copied, IReadOnlyList<string> Missing) result)
    {
        if (result.Copied.Count > 0)
        {
            var line = "Copied the locked setup into the server Modules folder: " + string.Join(", ", result.Copied) + ".";
            Append(line);
            _actions.Note("Locked setup", line);
        }
        if (result.Missing.Count > 0)
            Append("These locked setup mods are not installed in Bannerlord yet: " + string.Join(", ", result.Missing) + ".");
    }

    private bool RestoreWorkingOrder()
    {
        var ids = _modules.Select(row => row.Id).ToList();
        if (BannerlordCoopLauncher.Core.Profile.WorkingLoadOrder.KeepsCoopPlacement(ids, StableCoop.Order))
            return false;

        var placed = BannerlordCoopLauncher.Core.Profile.WorkingLoadOrder.PlaceExtrasBelow(ids, StableCoop.Order);
        if (placed.SequenceEqual(_modules.Select(row => row.Id), StringComparer.OrdinalIgnoreCase))
            return false;

        var byId = _modules.ToDictionary(row => row.Id, StringComparer.OrdinalIgnoreCase);
        _modules.Clear();
        foreach (var id in placed)
        {
            if (byId.TryGetValue(id, out var row))
                _modules.Add(row);
        }

        return true;
    }

    private void StampModuleOrder()
    {
        NumberModules();
        ModuleList.Items.Refresh();
    }

    private void NumberModules()
    {
        for (var i = 0; i < _modules.Count; i++)
            _modules[i].Order = i + 1;
    }

    private void AddMods_Click(object sender, RoutedEventArgs e)
    {
        if (RefuseWhileModListRuns())
            return;
        if (_settings.Current().Locked)
        {
            SetModuleSaveHint("This setup is locked. Mods cannot be added.");
            ModuleSaveHint.Visibility = Visibility.Visible;
            return;
        }

        var installed = _modules.Select(row => row.Id).ToList();
        var picked = HostAddModsWindow.Choose(this, () => ExtraModScan.Scan(AppContext.BaseDirectory, installed));
        // The window waits for the host, and a restart after a crash may have started the server meanwhile.
        if (picked.Count == 0 || RefuseWhileModListRuns())
            return;

        var addedMods = new List<FoundServerMod>();
        foreach (var mod in picked)
        {
            try
            {
                ExtraModScan.Install(mod.Directory, ServerRoot, mod.ModuleId);
                addedMods.Add(mod);
            }
            catch (InvalidOperationException ex)
            {
                Append(ex.Message);
                SetModuleSaveHint(ex.Message);
                ModuleSaveHint.Visibility = Visibility.Visible;
            }
        }

        if (addedMods.Count == 0)
            return;

        ReloadModules();
        var added = new HashSet<string>(addedMods.Select(mod => mod.ModuleId), StringComparer.OrdinalIgnoreCase);
        foreach (var row in _modules)
        {
            row.IsNew = added.Contains(row.Id);
            // The setup's order does not list a mod the host just picked yet, so turn it on here.
            if (row.IsNew)
                row.Enabled = true;
        }
        UpdateModuleSummary();
        ModuleList.Items.Refresh();
        if (_modules.FirstOrDefault(row => row.IsNew) is ModuleRow first)
            ModuleList.ScrollIntoView(first);
        SetModuleSaveHint("New mods stay highlighted until you click Save module config, then restart the server. Players need the same mods in the client launcher.");
        ModuleSaveHint.Visibility = Visibility.Visible;
        _actions.Note("Modules", "Added " + string.Join(", ", addedMods.Select(mod => mod.ModuleId)));
        Append("Added " + string.Join(", ", addedMods.Select(mod => mod.Name)) + ". Click Save module config, then restart the server.");
    }

    private void ReloadModules_Click(object sender, RoutedEventArgs e)
    {
        if (RefuseWhileModListRuns())
            return;
        if (_settings.Current().Locked)
            return;

        var before = new HashSet<string>(_modules.Select(row => row.Id), StringComparer.OrdinalIgnoreCase);
        ReloadModules();
        var updatedMods = _session.IsRunning
            ? (IReadOnlyList<string>)Array.Empty<string>()
            : ExtraModScan.UpdateFromNewerCopies(ServerRoot, AD1259Server.RefreshIds(Edition.IsAD1259, _modules.Select(row => row.Id)));
        if (updatedMods.Count > 0)
        {
            ReloadModules();
            _actions.Note("Modules", "Updated " + string.Join(", ", updatedMods));
            Append("Updated to the newer version: " + string.Join(", ", updatedMods) + ". The old copies are in DedicatedServer\\" + ModuleRemoval.RemovedFolderName + ".");
        }

        var fresh = _modules.Where(row => !before.Contains(row.Id)).ToList();
        foreach (var row in _modules)
            row.IsNew = fresh.Contains(row);
        ModuleList.Items.Refresh();
        var keep = _settings.Current().Locked
            ? " This setup is locked. These mods cannot be changed."
            : " Click Save module config, then restart the server.";
        ModuleRefreshStatus.Visibility = Visibility.Visible;
        ModuleRefreshStatus.Text = (fresh.Count == 0
            ? "Refreshed. No new mods."
            : fresh.Count == 1
                ? "Refreshed. 1 new mod was found. It is highlighted." + keep
                : "Refreshed. " + fresh.Count + " new mods were found. They are highlighted." + keep)
            + (updatedMods.Count > 0
                ? " Updated: " + string.Join(", ", updatedMods) + ". Players need the same version in the client launcher." + (fresh.Count == 0 ? keep : "")
                : _session.IsRunning ? " Stop the server to update mods." : "");
        if (fresh.Count > 0)
            ModuleList.ScrollIntoView(fresh[0]);
        RefreshModulesButton.Content = "Checked";
        RefreshModulesButton.Background = new SolidColorBrush(Color.FromRgb(226, 194, 122));
        RefreshModulesButton.Foreground = new SolidColorBrush(Color.FromRgb(26, 22, 12));
        RefreshModulesButton.BorderBrush = new SolidColorBrush(Color.FromRgb(226, 194, 122));
        RefreshModulesButton.BorderThickness = new Thickness(2);
        _refreshPulse.Stop();
        _refreshPulse.Start();
    }

    private void EnabledOnly_Click(object sender, RoutedEventArgs e)
    {
        _enabledOnly = !_enabledOnly;
        EnabledOnly.Content = _enabledOnly ? "Show all modules" : "Enabled only";
        EnabledOnly.Background = new System.Windows.Media.SolidColorBrush(_enabledOnly
            ? System.Windows.Media.Color.FromRgb(31, 78, 121)
            : System.Windows.Media.Color.FromRgb(14, 20, 24));
        EnabledOnly.ToolTip = _enabledOnly
            ? "The list is hiding turned-off modules. Click Show all modules to bring them back."
            : "Hides modules that are turned off. It does not change the server list. Click again to show every module.";
        RefreshModuleView(sender, e);
    }

    private void RefreshModuleView(object sender, RoutedEventArgs e)
    {
        var view = System.Windows.Data.CollectionViewSource.GetDefaultView(_modules);
        // Enabled only and the filter chip (Coodsz Host) together; no filter at all when neither is on.
        view.Filter = _enabledOnly || _modFilter != BannerlordCoopLauncher.Core.Keep.ModFilterKind.All
            ? item => item is ModuleRow row && ModuleRowShown(row)
            : null;
        ModuleList.ItemsSource = view;
    }

    private void AutoOrder_Click(object sender, RoutedEventArgs e)
    {
        if (RefuseWhileModListRuns())
            return;
        if (_settings.Current().Locked)
            return;

        var before = string.Join("\n", _modules.Select(row => row.Id));
        var ordered = ModuleCatalog.AutoOrder(_modules.ToList());
        _modules.Clear();
        foreach (var row in ordered)
            _modules.Add(row);
        RestoreWorkingOrder();
        var note = ModuleCatalog.Validate(_modules.ToList());
        DependencyNote.Text = note;
        StampModuleOrder();
        RefreshModuleSaveHint();
        var same = before == string.Join("\n", _modules.Select(row => row.Id));
        ModuleNotice("Auto dependency order", same
            ? "Already in order. The working Coop mods stayed put. Extra mods can sit below that list."
            : "Extra mods were sorted below the working list. Those Coop mods stayed in the order that runs. Click Save module config, then restart the server. " + note);
    }

    private void SaveModules_Click(object sender, RoutedEventArgs e)
    {
        if (RefuseWhileModListRuns())
            return;
        if (_settings.Current().Locked)
            return;

        try
        {
            var profile = _settings.Current();
            var orderSame = profile.ModuleOrder.SequenceEqual(_modules.Select(row => row.Id), StringComparer.OrdinalIgnoreCase);
            var disabled = _modules.Where(row => !row.Required && !row.Enabled).Select(row => row.Id);
            var disabledSame = new HashSet<string>(profile.DisabledModules, StringComparer.OrdinalIgnoreCase).SetEquals(disabled);
            if (orderSame && disabledSame && profile.ModuleOrder.Count > 0)
            {
                // A plain note, so a status line instead of a window that waits to be closed.
                ShowModuleNote("Already saved. This profile already has this module list and order. You don't need to save again.");
                return;
            }

            PersistProfile();
            foreach (var row in _modules)
                row.IsNew = false;
            ModuleList.Items.Refresh();
            ShowModuleNote("Saved. The highlight on new mods is cleared.");
            RefreshModuleSaveHint();
            var on = _modules.Where(row => row.Enabled).Select(row => row.Id).ToList();
            var off = _modules.Where(row => !row.Enabled).Select(row => row.Id).ToList();
            _actions.Note("Modules", "Saved config for " + profile.Name
                + ". On=" + on.Count + " [" + string.Join(", ", on) + "]"
                + (off.Count == 0 ? "" : ". Off=[" + string.Join(", ", off) + "]"));
            Append("Module configuration saved. Restart the server so this list applies.");
            ModuleNotice("Save module config", "Saved. This profile now has which modules are on and the order they load. Restart the server. A running server keeps the old list until then.");
        }
        catch (Exception ex)
        {
            DependencyNote.Text = ex.Message;
            ModuleNotice("Save module config", ex.Message);
        }
    }

    private string ModuleSnapshot() =>
        string.Join("\n", _modules.Select(row => row.Id + (row.Enabled ? ":1" : ":0")));

    private void ModuleNotice(string title, string message)
    {
        DependencyNote.Text = message;
        var text = new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromRgb(213, 222, 230)),
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 14,
            Margin = new Thickness(0, 0, 0, 16)
        };
        var button = new Button
        {
            Content = "OK",
            ToolTip = "Closes this message.",
            HorizontalAlignment = HorizontalAlignment.Right,
            Padding = new Thickness(18, 8, 18, 8),
            Background = new SolidColorBrush(Color.FromRgb(31, 78, 121)),
            Foreground = new SolidColorBrush(Color.FromRgb(213, 222, 230)),
            BorderThickness = new Thickness(0),
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 14,
            FontWeight = FontWeights.SemiBold
        };
        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(text);
        panel.Children.Add(button);
        var dialog = new Window
        {
            Title = title,
            Width = 460,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            ResizeMode = ResizeMode.NoResize,
            Background = new SolidColorBrush(Color.FromRgb(16, 22, 27)),
            Foreground = new SolidColorBrush(Color.FromRgb(213, 222, 230)),
            Content = panel
        };
        Coodsz.Ui.WindowPlacement.KeepOnScreen(dialog);
        button.Click += (_, _) => dialog.Close();
        dialog.ShowDialog();
    }

    private void ModuleDragStart(object sender, MouseButtonEventArgs e)
    {
        // A click on a switch, Move up or down, Delete or a Report link is that control's own, not a drag.
        if (IsInsideCheckBox(e.OriginalSource as DependencyObject) || IsInsideButton(e.OriginalSource as DependencyObject))
            return;
        // Only on a row: a click on empty list space says nothing.
        if (RowUnder(ModuleList, e.GetPosition(ModuleList)) == null)
            return;
        if (RefuseWhileModListRuns())
            return;
        if (_settings.Current().Locked)
        {
            SetModuleSaveHint("This setup is locked. The load order cannot be changed.");
            ModuleSaveHint.Visibility = Visibility.Visible;
            return;
        }

        _dragRow = RowUnder(ModuleList, e.GetPosition(ModuleList));
        _dragMoved = false;
        _dragBlocked = false;
        _dragStart = e.GetPosition(ModuleList);
        if (_dragRow == null)
            return;
        _dragRow.IsDragging = true;
        ModuleList.CaptureMouse();
        e.Handled = true;
    }

    private void ModuleDragMove(object sender, MouseEventArgs e)
    {
        if (_dragRow == null || !ModuleList.IsMouseCaptured)
            return;
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            FinishModuleDrag();
            return;
        }

        var position = e.GetPosition(ModuleList);
        if (!_dragMoved && Math.Abs(position.Y - _dragStart.Y) < 6 && Math.Abs(position.X - _dragStart.X) < 6)
            return;
        _dragMoved = true;
        MoveDraggedRow(position);
        if (position.Y < 36)
            ScrollModuleList(-28);
        else if (position.Y > ModuleList.ActualHeight - 36)
            ScrollModuleList(28);
        e.Handled = true;
    }

    private void ModuleDragUp(object sender, EventArgs e) => FinishModuleDrag();

    private void FinishModuleDrag()
    {
        if (_dragRow != null)
            _dragRow.IsDragging = false;
        if (ModuleList.IsMouseCaptured)
            ModuleList.ReleaseMouseCapture();
        if (_dragRow == null)
            return;
        var moved = _dragMoved;
        var blocked = _dragBlocked;
        _dragRow = null;
        _dragMoved = false;
        _dragBlocked = false;
        if (blocked)
        {
            ShowWorkingOrderHint();
            return;
        }
        if (!moved || _settings.Current().Locked)
            return;
        StampModuleOrder();
        DependencyNote.Text = ModuleCatalog.Validate(_modules.ToList());
        RefreshModuleSaveHint();
        if (OrderNeedsSave())
        {
            SetModuleSaveHint("Please save to keep these changes. Click Save module config, then restart the server.");
            ModuleSaveHint.Visibility = Visibility.Visible;
        }
    }

    private void MoveDraggedRow(Point position)
    {
        if (_dragRow == null)
            return;
        var target = RowUnder(ModuleList, position);
        if (target == null || ReferenceEquals(target, _dragRow))
            return;
        var from = _modules.IndexOf(_dragRow);
        var to = _modules.IndexOf(target);
        if (from < 0 || to < 0 || from == to)
            return;
        if (ModuleList.ItemContainerGenerator.ContainerFromItem(target) is not FrameworkElement container)
            return;
        var top = container.TranslatePoint(new Point(0, 0), ModuleList).Y;
        var mid = top + container.ActualHeight / 2;
        if (to > from && position.Y < mid)
            return;
        if (to < from && position.Y > mid)
            return;
        if (!BannerlordCoopLauncher.Core.Profile.WorkingLoadOrder.CanMoveKeepingCoop(_modules.Select(row => row.Id).ToList(), from, to, StableCoop.Order))
        {
            _dragBlocked = true;
            return;
        }
        _modules.Move(from, to);
        NumberModules();
    }

    /// <summary>One short line, the reasons on hover: after the tool put the Coop mods back in their working order, or
    /// after a move or drag into them was refused.</summary>
    private void ShowWorkingOrderHint(bool putBack = false)
    {
        SetModuleSaveHint(putBack
            ? "The Coop mods were put back in their working order. Click Save module config, then restart the server."
            : "Keep the Coop mods in this order. Extra mods go below.");
        ModuleSaveHint.ToolTip = ModulesIntro.ToolTip;
        ModuleSaveHint.Visibility = Visibility.Visible;
    }

    /// <summary>Every save hint line goes through here, so the order reasons on hover (ShowWorkingOrderHint) never stay
    /// on another line.</summary>
    private void SetModuleSaveHint(string text)
    {
        ModuleSaveHint.ToolTip = null;
        ModuleSaveHint.Text = text;
    }

    private void ScrollModuleList(double delta)
    {
        if (FindVisualChild<ScrollViewer>(ModuleList) is not ScrollViewer scroll)
            return;
        scroll.ScrollToVerticalOffset(scroll.VerticalOffset + delta);
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match)
                return match;
            var nested = FindVisualChild<T>(child);
            if (nested != null)
                return nested;
        }

        return null;
    }

    private bool OrderNeedsSave() =>
        _settings.Current().ModuleOrder.Count == 0
        || !_settings.Current().ModuleOrder.SequenceEqual(_modules.Select(row => row.Id), StringComparer.OrdinalIgnoreCase);

    private static ModuleRow? RowUnder(ItemsControl list, Point position)
    {
        var node = list.InputHitTest(position) as DependencyObject;
        while (node != null && !ReferenceEquals(node, list))
        {
            if (node is FrameworkElement element && element.DataContext is ModuleRow row)
                return row;
            node = ParentOf(node);
        }

        return null;
    }

    private static bool IsInsideButton(DependencyObject? node)
    {
        while (node != null)
        {
            if (node is Button)
                return true;
            node = ParentOf(node);
        }

        return false;
    }

    private void DeleteModule_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.DataContext is not ModuleRow row)
            return;
        var profile = _settings.Current();
        var refusal = ModuleRemoval.Refusal(row.Id, row.Required, profile.Locked, ModListRunning, StableCoop.Order);
        if (refusal != null)
        {
            ShowModuleNote(refusal);
            return;
        }

        if (!ConfirmWindow.Ask(this, "Delete mod",
                "Delete " + row.Name + " (" + row.Id + ") from the dedicated server? Its folder moves out of the server Modules folder into DedicatedServer\\" + ModuleRemoval.RemovedFolderName
                + ". Move it back to Modules if you change your mind. Players do not need to do anything."))
            return;

        try
        {
            var moved = ModuleRemoval.MoveOut(ServerRoot, row.Id);
            _actions.Note("Modules", "Deleted " + row.Id + " from the server. Moved to " + string.Join(", ", moved));
            ReloadModules();
            ModuleList.Items.Refresh();
            ShowModuleNote("Deleted " + row.Name + ". Its folder is in DedicatedServer\\" + ModuleRemoval.RemovedFolderName + ". Click Save module config, then start the server.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            ShowModuleNote("Could not delete " + row.Name + ". " + ex.Message + " Close anything that has files of that mod open, then try again.");
        }
    }

    private void ShowModuleNote(string text)
    {
        ModuleRefreshStatus.Visibility = Visibility.Visible;
        ModuleRefreshStatus.Text = text;
    }

    private static bool IsInsideCheckBox(DependencyObject? node)
    {
        while (node != null)
        {
            if (node is CheckBox)
                return true;
            node = ParentOf(node);
        }

        return false;
    }

    private static DependencyObject? ParentOf(DependencyObject node)
    {
        if (node is Visual || node is System.Windows.Media.Media3D.Visual3D)
            return VisualTreeHelper.GetParent(node);
        return LogicalTreeHelper.GetParent(node);
    }

    // ----- The running block, Move up and Move down, and the More menu (redesign wave 3) -----

    // --show-mods-blocked (Debug builds only) shows the Mods page as it looks while the server runs, for screenshots without a server.
    private bool _modsBlockedExample;

    /// <summary>The server runs, a mod test runs its own server, a restart after a crash waits, or a Start, Stop or
    /// Restart is under way (ModListBlock.Running).</summary>
    private bool ModListRunning => _modsBlockedExample
        || ModListBlock.Running(_session.IsRunning, _modTestRunning, CrashRestartWaits || _restartingNow || _restartingAfterCrash || _controlBusy);

    /// <summary>A server (the real one or a mod test's) has the Modules folder open, so nothing is copied into it.</summary>
    private bool ServerHoldsModFiles => _session.IsRunning || _modTestRunning;

    private (bool Running, bool Locked)? _modListBlockShown;

    /// <summary>Called once from the window's constructor.</summary>
    private void StartModsPage()
    {
        // The dependency line shows only when there is a problem or a notice, not to say the order is valid.
        System.ComponentModel.DependencyPropertyDescriptor.FromProperty(TextBlock.TextProperty, typeof(TextBlock)).AddValueChanged(DependencyNote, (_, _) =>
            DependencyNote.Visibility = ModuleCatalog.ShowsNote(Coodsz.Ui.UiLanguage.English(DependencyNote)) ? Visibility.Visible : Visibility.Collapsed);
        ModsRunningBanner.Title = ModListBlock.RunningLine;
        ModsRunningBanner.ActionText = "Go to Dashboard";
        ModsRunningBanner.ActionTip = "Opens the Dashboard, where Stop server is.";
        ModsRunningBanner.Action += () => ShowPage("Dashboard");
        // At a small window the banners and the toolbar would leave the list a row or two: past half the page they scroll.
        ModulesPage.SizeChanged += (_, e) => ModsTopScroll.MaxHeight = Math.Max(180, e.NewSize.Height * 0.5);
#if DEBUG
        _modsBlockedExample = Environment.GetCommandLineArgs().Any(arg => string.Equals(arg, "--show-mods-blocked", StringComparison.OrdinalIgnoreCase));
#endif
    }

    /// <summary>
    /// From RefreshState (every second) and after a setup is picked. While the mod list runs no switch, move, drag or
    /// Delete works, and Add mods, Save module config, Refresh, Auto dependency order, Test mods and Import mod list are
    /// off; after Stop they come back as the setup allows. Share mod list, Enabled only, the filters and Report stay on.
    /// </summary>
    private void ApplyModListBlock(bool force = false)
    {
        var running = ModListRunning;
        var locked = _settings.Current().Locked;
        if (!force && _modListBlockShown == (running, locked))
            return;
        _modListBlockShown = (running, locked);
        foreach (var row in _modules)
        {
            row.CanToggle = !running && !locked && !row.Required;
            row.CanMove = !running && !locked;
        }

        foreach (var control in new Control[] { AddModsButton, SaveModulesButton, RefreshModulesButton, AutoOrderButton })
        {
            control.IsEnabled = !running && !locked;
            ShowBlockedTip(control, running ? ModListBlock.RunningTip : locked ? BannerlordCoopLauncher.Core.Profile.LockedSetupNote.ButtonTip : null);
        }

        foreach (var control in new Control[] { TestModsButton, ImportListItem })
        {
            control.IsEnabled = !running;
            ShowBlockedTip(control, running ? ModListBlock.RunningTip : null);
        }

        // Use the Steam server changes the server folder, so it waits until the server is stopped too.
        UseSteamServerButton.IsEnabled = !running;
        ShowBlockedTip(UseSteamServerButton, running ? UseSteamServerRunning : null);

        ModsRunningBanner.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
        // The setup picker still works while the server runs: the pick is for the next Start.
        SetupRunningNote.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
        if (running && _dragRow != null)
        {
            _dragMoved = false;
            FinishModuleDrag();
        }
    }

    /// <summary>True (and nothing changes) while the mod list runs; the banner at the top of the page says why.</summary>
    private bool RefuseWhileModListRuns()
    {
        if (!ModListRunning)
            return false;
        ApplyModListBlock();
        return true;
    }

    private void ModuleMoveUp(object sender, RoutedEventArgs e) => MoveModule((sender as FrameworkElement)?.DataContext as ModuleRow, -1);

    private void ModuleMoveDown(object sender, RoutedEventArgs e) => MoveModule((sender as FrameworkElement)?.DataContext as ModuleRow, 1);

    /// <summary>Move up (-1) or Move down (+1) one place, by the same rules as dragging: the working Coop mods keep
    /// their order (WorkingLoadOrder.CanMoveKeepingCoop), then the numbers, the dependency note and the save hint.</summary>
    private void MoveModule(ModuleRow? row, int delta)
    {
        if (row == null || RefuseWhileModListRuns())
            return;
        if (_settings.Current().Locked)
        {
            SetModuleSaveHint("This setup is locked. The load order cannot be changed.");
            ModuleSaveHint.Visibility = Visibility.Visible;
            return;
        }

        // The neighbour is the next row the page shows (a filter can hide the rows in between), as when dragging.
        var from = _modules.IndexOf(row);
        var shown = ModuleList.Items.OfType<ModuleRow>().Select(item => _modules.IndexOf(item)).ToList();
        if (ModListBlock.MoveTarget(_modules.Select(item => item.Id).ToList(), shown, from, delta, StableCoop.Order) is not int to)
        {
            // The top row up or the last row down does nothing; a move into the working Coop mods says why not.
            var at = shown.IndexOf(from);
            if (at >= 0 && at + delta >= 0 && at + delta < shown.Count)
                ShowWorkingOrderHint();
            return;
        }

        var focused = ModuleList.IsKeyboardFocusWithin;
        _modules.Move(from, to);
        // The rows stay as they are (no Items.Refresh), so the focus stays on the button or the row.
        NumberModules();
        DependencyNote.Text = ModuleCatalog.Validate(_modules.ToList());
        RefreshModuleSaveHint();
        if (OrderNeedsSave())
        {
            SetModuleSaveHint("Please save to keep these changes. Click Save module config, then restart the server.");
            ModuleSaveHint.Visibility = Visibility.Visible;
        }
        ModuleList.ScrollIntoView(row);
        // A move can give the row a new container, which drops the focus: put it back on the row, so Alt+Up pressed
        // again keeps moving the same mod.
        if (focused)
            Dispatcher.BeginInvoke(() =>
            {
                var onRow = Keyboard.FocusedElement is FrameworkElement element && ReferenceEquals(element.DataContext, row);
                if (!onRow && ModuleList.ItemContainerGenerator.ContainerFromItem(row) is ListBoxItem item)
                    item.Focus();
            }, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    // Alt+Up and Alt+Down move the row that has the focus (on the row or on a control in it); Space on a row turns it on or off.
    private void ModuleListKeys(object sender, KeyEventArgs e)
    {
        if ((e.OriginalSource as FrameworkElement)?.DataContext is not ModuleRow row)
            return;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (Keyboard.Modifiers == ModifierKeys.Alt && (key == Key.Up || key == Key.Down))
        {
            MoveModule(row, key == Key.Up ? -1 : 1);
            e.Handled = true;
            return;
        }

        // A focused switch or button handles its own Space.
        if (key == Key.Space && Keyboard.Modifiers == ModifierKeys.None && e.OriginalSource is ListBoxItem)
        {
            if (row.CanToggle)
            {
                row.Enabled = !row.Enabled;
                ModuleSwitch_Click(row, e);
            }
            e.Handled = true;
        }
    }

    private void ModsMore_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { ContextMenu: { } menu } button)
            return;
        menu.PlacementTarget = button;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.IsOpen = true;
    }
}
