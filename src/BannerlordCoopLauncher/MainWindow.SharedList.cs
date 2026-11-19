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
    private async void CheckMods_Click(object sender, RoutedEventArgs e)
    {
        // A Coop unzipped into Modules by hand: Windows' download marks cleared, a folder one level too deep named.
        foreach (var line in CoopInstall.CheckModulesCoop(_gameRoot))
            Log(line);
        await RefreshAsync();
        if (_resolution == null)
        {
            Log("Check mods could not read the mod list yet.");
            return;
        }

        var problems = _resolution.All.Where(module => module.State != ModuleState.Ready).ToList();
        var message = problems.Count == 0
            ? "All enabled mods are ready."
            : "These mods need attention: " + string.Join(", ", problems.Select(module => module.Name + " (" + StatusText(module) + ")"));
        BlockReasonText.Text = message;
        Log(message);
        // Which Coop the game loads: a Modules copy wins over the Workshop one.
        if (_resolution.Modules.FirstOrDefault(module => module.Enabled && BannerlordCoopModules.IsEither(module.ModuleId) && module.Directory != null) is { } coop
            && ManifestVersion(coop.Directory) is { Length: > 0 } coopVersion)
            Log(CoopInstall.LoadedLine(coopVersion, CoopInstall.IsInModules(coop.Directory, _gameRoot)));
    }

    private void ShowSharedListGuide(string continueLabel, Action continueAction)
    {
        _sharedGuideAction = continueAction;
        if (_sharedGuide != null)
        {
            if (_sharedGuideContinue != null)
                _sharedGuideContinue.Content = continueLabel;
            _sharedGuide.Activate();
            return;
        }

        var ivory = new SolidColorBrush(Color.FromRgb(213, 222, 230));
        var muted = new SolidColorBrush(Color.FromRgb(142, 160, 174));
        var bronze = new SolidColorBrush(Color.FromRgb(42, 56, 68));
        var gold = new SolidColorBrush(Color.FromRgb(226, 194, 122));
        var dialog = new Window
        {
            Title = "Import mod list",
            Width = 560,
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
        Button Make(string content, bool filled, string tip)
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
            return button;
        }
        var continueButton = Make(continueLabel, true, "Picks the mod list file to import.");
        continueButton.Click += (_, _) => _sharedGuideAction?.Invoke();
        var close = Make("Close", false, "Closes this guide.");
        close.Click += (_, _) => dialog.Close();
        buttons.Children.Add(continueButton);
        buttons.Children.Add(close);
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = "Import mod list",
            FontFamily = new FontFamily("Palatino Linotype"),
            FontSize = 22,
            Foreground = ivory,
            Cursor = Cursors.SizeAll,
            Margin = new Thickness(0, 0, 0, 4)
        });
        stack.Children.Add(new TextBlock
        {
            Text = "Drag this window anywhere. Leave it open while you follow the steps.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = gold,
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 12)
        });
        stack.Children.Add(GuideLine(SharedModList.Intro, muted, FontWeights.Normal, 0, 0, 0, 12));
        stack.Children.Add(GuideLine(SharedModList.ClientToServer, new SolidColorBrush(Color.FromRgb(143, 224, 208)), FontWeights.Bold, 0, 4, 0, 4));
        stack.Children.Add(GuideLine(SharedModList.ClientStepsFor(LauncherBrand.Name, LauncherBrand.ServerTool), muted, FontWeights.Normal, 0, 0, 0, 12));
        stack.Children.Add(GuideLine(SharedModList.ServerToClient, new SolidColorBrush(Color.FromRgb(183, 196, 240)), FontWeights.Bold, 0, 4, 0, 4));
        stack.Children.Add(GuideLine(SharedModList.ServerStepsFor(LauncherBrand.ServerTool, LauncherBrand.Name), muted, FontWeights.Normal, 0, 0, 0, 12));
        stack.Children.Add(GuideLine(SharedModList.SendToSomeone, new SolidColorBrush(Color.FromRgb(240, 168, 184)), FontWeights.Bold, 0, 4, 0, 8));
        stack.Children.Add(GuideLine(SharedModList.ExtraModsClient, new SolidColorBrush(Color.FromRgb(240, 168, 184)), FontWeights.Bold, 0, 0, 0, 0));
        stack.Children.Add(buttons);
        dialog.Content = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(21, 29, 36)),
            BorderBrush = gold,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(22),
            Cursor = Cursors.SizeAll,
            Child = stack
        };
        dialog.MouseLeftButtonDown += (_, args) =>
        {
            if (FindButton(args.OriginalSource as DependencyObject) != null)
                return;
            try { dialog.DragMove(); }
            catch (InvalidOperationException) { }
        };
        dialog.Closed += (_, _) =>
        {
            _sharedGuide = null;
            _sharedGuideContinue = null;
        };
        _sharedGuide = dialog;
        _sharedGuideContinue = continueButton;
        dialog.Show();
    }

    private static TextBlock GuideLine(string text, Brush foreground, FontWeight weight, double left, double top, double right, double bottom) => new()
    {
        Text = text,
        TextWrapping = TextWrapping.Wrap,
        Foreground = foreground,
        FontFamily = new FontFamily("Segoe UI"),
        FontSize = 14,
        FontWeight = weight,
        LineHeight = 22,
        Margin = new Thickness(left, top, right, bottom)
    };

    private static Button? FindButton(DependencyObject? node)
    {
        while (node != null)
        {
            if (node is Button button)
                return button;
            node = ParentOf(node);
        }

        return null;
    }

    private void ShareList_Click(object sender, RoutedEventArgs e)
    {
        if (_resolution == null)
        {
            Log("Click Check mods before sharing the mod list.");
            return;
        }

        var window = new ShareModListWindow { Owner = this };
        if (window.ShowDialog() == true)
            SaveSharedListFile();
    }

    private void SaveSharedListFile()
    {
        if (_resolution == null)
        {
            Log("Click Check mods before sharing the mod list.");
            return;
        }

        var dialog = new SaveFileDialog
        {
            Filter = "Mod list|*.json",
            FileName = "coodsz-mod-list.json"
        };
        if (dialog.ShowDialog() != true)
            return;

        var entries = new List<SharedModEntry>();
        foreach (var module in _resolution.All)
        {
            var version = "";
            if (!string.IsNullOrWhiteSpace(module.Directory))
            {
                var xmlPath = Path.Combine(module.Directory, "SubModule.xml");
                if (File.Exists(xmlPath) && SubModuleXml.TryRead(File.ReadAllText(xmlPath), out var info, out _) && info != null)
                    version = info.Version ?? "";
            }

            entries.Add(new SharedModEntry(module.ModuleId, module.Name, version, SharedModList.WorkshopIdFor(module.WorkshopId, module.Directory)));
        }

        SharedModList.Write(dialog.FileName, entries);
        Log("Shared mod list saved. A friend clicks Import mod list.");
    }

    private void OpenSharedList_Click(object sender, RoutedEventArgs e) =>
        ShowSharedListGuide("Continue to open", () => _ = OpenSharedListFileAsync());

    // Import a friend's (or the server's) list: make an editable setup when the current one keeps its
    // order, add the list's extra mods to it, download the Workshop ones this PC does not have, then
    // match the order and switches. A window at the end says what happened.
    private async Task OpenSharedListFileAsync()
    {
        var dialog = new OpenFileDialog { Filter = "Mod list|*.json" };
        if (dialog.ShowDialog() != true)
            return;

        IReadOnlyList<SharedModEntry> shared;
        try
        {
            shared = SharedModList.Read(File.ReadAllText(dialog.FileName));
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Text.Json.JsonException or IOException)
        {
            Log("That file is not a Coodsz mod list.");
            return;
        }

        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in shared)
            names[entry.Id] = string.IsNullOrWhiteSpace(entry.Name) ? entry.Id : entry.Name;
        string Label(string id) => names.TryGetValue(id, out var name) ? name : id;
        var summary = new List<string>();

        var setup = _settings.Current();
        if (setup.Locked || DefaultClientSet.IsDefault(setup.Name))
        {
            if (!ConfirmLeaveSetup())
                return;
            var from = setup.Name;
            setup = new ClientSetup
            {
                Name = _settings.NextSetupName(),
                CampaignMap = setup.CampaignMap,
                RemovedModules = DefaultClientSet.NotOnNewSetup.ToList()
            };
            _settings.Setups.Add(setup);
            _settings.ActiveSetup = setup.Name;
            FillSetupList();
            ApplySetupToForm(setup);
            _settings.Save(SettingsPath);
            await RefreshAsync();
            RememberSavedMods();
            Log(setup.Name + " was made for this list, because " + from + " keeps its mod order.");
            summary.Add("Made a new setup for this list: " + setup.Name + ".");
        }

        // Add the list's extra mods to this setup. A mod already on this PC keeps where it is installed;
        // one that is not here needs a Workshop link to be downloaded.
        var gameRoot = _gameRoot ?? GameRoot.Normalize(GamePathBox.Text, _files);
        IReadOnlyList<FoundMod> onThisPc;
        // The bundled mods are written out first, so a copy an older version left beside the launcher is listed once.
        await ToolPayload.FolderAsync(ToolPayload.BundledMods);
        try
        {
            onThisPc = CustomModCatalog.Scan(AppContext.BaseDirectory, ReadWorkshops(), gameRoot == null ? null : Path.Combine(gameRoot, "Modules"), ToolPayload.Peek(ToolPayload.BundledMods));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            onThisPc = Array.Empty<FoundMod>();
        }

        var known = new HashSet<string>(_rows.Select(row => row.ModuleId), StringComparer.OrdinalIgnoreCase);
        known.UnionWith(DefaultClientSet.Fixed);
        known.Add("DedicatedServer.Windows");
        if (_resolution != null)
            known.UnionWith(_resolution.BaseModules.Select(module => module.ModuleId));
        var added = new List<string>();
        var noLink = new List<string>();
        var pending = PendingMods(setup);
        foreach (var entry in shared)
        {
            if (known.Contains(entry.Id) || setup.CustomMods.Any(mod => mod.ModuleId.Equals(entry.Id, StringComparison.OrdinalIgnoreCase)))
                continue;
            var found = onThisPc.FirstOrDefault(mod => mod.ModuleId.Equals(entry.Id, StringComparison.OrdinalIgnoreCase));
            var workshop = found?.WorkshopId ?? (SharedModList.IsWorkshopId(entry.WorkshopId) ? entry.WorkshopId : "");
            if (found == null && workshop.Length == 0)
            {
                noLink.Add(Label(entry.Id));
                continue;
            }

            setup.CustomMods.Add(new CustomMod { Name = Label(entry.Id), ModuleId = entry.Id, WorkshopId = workshop.Length == 0 ? "0" : workshop });
            setup.RemovedModules.RemoveAll(id => id.Equals(entry.Id, StringComparison.OrdinalIgnoreCase));
            pending.Add(entry.Id);
            added.Add(Label(entry.Id));
            if (found is { WorkshopId: "0" } && gameRoot != null)
                ModuleZipInstaller.InstallDirectory(found.Directory, gameRoot, found.ModuleId);
        }

        if (added.Count > 0)
        {
            Log("Added to " + setup.Name + ": " + string.Join(", ", added) + ".");
            summary.Add("Added to " + setup.Name + ": " + string.Join(", ", added) + ".");
        }

        await RefreshAsync();
        if (_resolution == null)
        {
            Log("The mod list could not be read yet.");
            return;
        }

        // Download the Workshop mods from the list that this PC does not have yet.
        var listed = new HashSet<string>(shared.Select(entry => entry.Id), StringComparer.OrdinalIgnoreCase);
        var download = _resolution.Modules
            .Where(module => listed.Contains(module.ModuleId) && module.State is ModuleState.Missing or ModuleState.UpdatePending
                && SharedModList.IsWorkshopId(module.WorkshopId))
            .ToList();
        if (download.Count > 0)
        {
            var ids = download.Select(module => module.WorkshopId!).Distinct().ToList();
            var subscribed = false;
            await RunBusyAsync(async cancellationToken =>
            {
                await EnsureSteamAsync(_profile, cancellationToken);
                if ((await SubscribeAndDownloadAsync(ids, cancellationToken)).Used)
                {
                    subscribed = true;
                    await LoadPackAsync(cancellationToken);
                    return;
                }

                foreach (var id in ids)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    Log("Subscribe in Steam and wait for the download. Workshop item " + id + ".");
                    if (!ShellOpen.TryOpen(ShellOpen.SteamWorkshopPage(id), out _))
                    {
                        Log(ShellOpen.SteamPageProblem);
                        break;
                    }

                    await Task.Delay(500, cancellationToken);
                }
            });
            var got = string.Join(", ", download.Select(module => Label(module.ModuleId)));
            if (subscribed)
                summary.Add("Downloaded from the Steam Workshop: " + got + ".");
            else
                summary.Add("Opened the Workshop pages to subscribe to: " + got + ".");
            await RefreshAsync();
        }

        // Match the order and the on/off switches of the extra mods.
        if (_rows.Count > 0)
        {
            var result = SharedModList.ApplyExtras(
                _rows.Select(row => row.ModuleId).ToList(),
                _rows.Where(row => row.IsOn).Select(row => row.ModuleId).ToList(),
                shared.Select(entry => entry.Id).ToList(),
                DefaultClientSet.Fixed,
                new[] { "DedicatedServer.Windows" });
            setup.ModuleOrder = result.Order.ToList();
            foreach (var id in result.TurnedOn)
                setup.DisabledModules.RemoveAll(item => item.Equals(id, StringComparison.OrdinalIgnoreCase));
            foreach (var id in result.TurnedOff)
            {
                if (!setup.DisabledModules.Contains(id, StringComparer.OrdinalIgnoreCase))
                    setup.DisabledModules.Add(id);
            }

            foreach (var id in result.TurnedOn.Concat(result.TurnedOff))
                pending.Add(id);
            if (result.TurnedOn.Count > 0)
            {
                Log("Turned on: " + string.Join(", ", result.TurnedOn.Select(Label)) + ".");
                summary.Add("Turned on: " + string.Join(", ", result.TurnedOn.Select(Label)) + ".");
            }
            if (result.TurnedOff.Count > 0)
            {
                Log("Turned off: " + string.Join(", ", result.TurnedOff.Select(Label)) + ".");
                summary.Add("Turned off: " + string.Join(", ", result.TurnedOff.Select(Label)) + ".");
            }
        }

        foreach (var entry in shared)
        {
            var local = _resolution.BaseModules.Concat(_resolution.Modules)
                .FirstOrDefault(module => module.ModuleId.Equals(entry.Id, StringComparison.OrdinalIgnoreCase));
            if (local == null || string.IsNullOrWhiteSpace(local.Directory))
                continue;
            var xmlPath = Path.Combine(local.Directory, "SubModule.xml");
            if (!File.Exists(xmlPath) || !SubModuleXml.TryRead(File.ReadAllText(xmlPath), out var info, out _) || info == null)
                continue;
            var warning = SharedModList.VersionWarning(local.Name, entry.Version, info.Version ?? "");
            if (warning != null)
            {
                Log(warning);
                summary.Add(warning);
            }
        }

        if (noLink.Count > 0)
        {
            Log("Not on this PC and the list has no Workshop link, so get these yourself: " + string.Join(", ", noLink) + ".");
            summary.Add("Not on this PC and the list has no Workshop link, so get these yourself: " + string.Join(", ", noLink) + ".");
        }

        if (pending.Count > 0)
        {
            ShowPendingSave();
            Log("Make sure to press Save mods. The imported list is not kept until you do.");
            summary.Add("Press Save mods to keep it.");
        }
        else if (summary.Count == 0)
            summary.Add("This setup already matches that list.");

        Coodsz.Ui.UiLanguage.Message(string.Join("\n\n", summary), "Import mod list", MessageBoxButton.OK, MessageBoxImage.Information, this);
    }
}
