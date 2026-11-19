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
    private TextBlock? _sharedGuideHeading;
    private TextBlock? _sharedGuideStatus;

    // The guide is titled after the button that opened it, as in Coop and Friends.
    private static string SharedGuideTitle(bool sharing) => sharing ? "Share mod list" : "Import mod list";

    private void ShowSharedListGuide(bool sharing, string continueLabel, Action continueAction)
    {
        _sharedGuideAction = continueAction;
        if (_sharedGuide != null)
        {
            if (_sharedGuideContinue != null)
                _sharedGuideContinue.Content = continueLabel;
            _sharedGuide.Title = SharedGuideTitle(sharing);
            if (_sharedGuideHeading != null)
                _sharedGuideHeading.Text = SharedGuideTitle(sharing);
            if (_sharedGuideStatus != null)
                _sharedGuideStatus.Visibility = Visibility.Collapsed;
            _sharedGuide.Activate();
            return;
        }

        var ivory = new SolidColorBrush(Color.FromRgb(213, 222, 230));
        var muted = new SolidColorBrush(Color.FromRgb(142, 160, 174));
        var bronze = new SolidColorBrush(Color.FromRgb(42, 56, 68));
        var gold = new SolidColorBrush(Color.FromRgb(226, 194, 122));
        var dialog = new Window
        {
            Title = SharedGuideTitle(sharing),
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
        var continueButton = Make(continueLabel, true, "Opens the file window for the mod list file.");
        continueButton.Click += (_, _) => _sharedGuideAction?.Invoke();
        var close = Make("Close", false, "Closes this window without saving or opening a file.");
        close.Click += (_, _) => dialog.Close();
        buttons.Children.Add(continueButton);
        buttons.Children.Add(close);
        var stack = new StackPanel();
        stack.Children.Add(_sharedGuideHeading = new TextBlock
        {
            Text = SharedGuideTitle(sharing),
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
        if (Edition.IsAD1259)
        {
            // The AD1259 client launcher has no mod list buttons; it sets the AD1259 mods itself.
            stack.Children.Add(GuideLine(SharedModList.ServerToServer, new SolidColorBrush(Color.FromRgb(143, 224, 208)), FontWeights.Bold, 0, 4, 0, 4));
            stack.Children.Add(GuideLine(SharedModList.ServerToServerSteps(Edition.Name), muted, FontWeights.Normal, 0, 0, 0, 12));
            stack.Children.Add(GuideLine(SharedModList.ClientSetsItsOwnMods(ClientToolName), muted, FontWeights.Normal, 0, 0, 0, 12));
        }
        else
        {
            stack.Children.Add(GuideLine(SharedModList.ClientToServer, new SolidColorBrush(Color.FromRgb(143, 224, 208)), FontWeights.Bold, 0, 4, 0, 4));
            stack.Children.Add(GuideLine(SharedModList.ClientStepsFor(ClientToolName, Edition.Name), muted, FontWeights.Normal, 0, 0, 0, 12));
            stack.Children.Add(GuideLine(SharedModList.ServerToClient, new SolidColorBrush(Color.FromRgb(183, 196, 240)), FontWeights.Bold, 0, 4, 0, 4));
            stack.Children.Add(GuideLine(SharedModList.ServerStepsFor(Edition.Name, ClientToolName), muted, FontWeights.Normal, 0, 0, 0, 12));
            stack.Children.Add(GuideLine(SharedModList.SendToSomeone, new SolidColorBrush(Color.FromRgb(240, 168, 184)), FontWeights.Bold, 0, 4, 0, 8));
        }
        stack.Children.Add(GuideLine(SharedModList.ExtraModsServer, new SolidColorBrush(Color.FromRgb(240, 168, 184)), FontWeights.Bold, 0, 0, 0, 0));
        // Messages for the step the guide just tried, such as a locked setup, show here and not only in the log.
        _sharedGuideStatus = GuideLine("", gold, FontWeights.SemiBold, 0, 12, 0, 0);
        _sharedGuideStatus.Visibility = Visibility.Collapsed;
        stack.Children.Add(_sharedGuideStatus);
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
            _sharedGuideHeading = null;
            _sharedGuideStatus = null;
        };
        _sharedGuide = dialog;
        _sharedGuideContinue = continueButton;
        dialog.Show();
    }

    private void ShowSharedGuideStatus(string message)
    {
        if (_sharedGuideStatus == null)
            return;
        _sharedGuideStatus.Text = message;
        _sharedGuideStatus.Visibility = Visibility.Visible;
        _sharedGuide?.Activate();
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

    private void ShareList_Click(object sender, RoutedEventArgs e) =>
        ShowSharedListGuide(true, "Continue to save", SaveSharedListFile);

    private void SaveSharedListFile()
    {
        var dialog = new SaveFileDialog
        {
            Filter = "Mod list|*.json",
            FileName = "coodsz-mod-list.json"
        };
        if (dialog.ShowDialog() != true)
            return;
        // The server copies have no Workshop path, so the ids come from the Workshop mods on this PC;
        // with them a player's import can download what they are missing.
        var workshopIds = SharedModList.WorkshopIdsIn(ExtraModScan.WorkshopContentRoots());
        var entries = _modules
            .Where(row => row.Enabled)
            .Select(row => new SharedModEntry(row.Id, row.Name, row.Version, workshopIds.TryGetValue(row.Id, out var id) ? id : ""))
            .ToList();
        SharedModList.Write(dialog.FileName, entries);
        Append(Edition.IsAD1259
            ? "Shared mod list saved. Another AD1259 server tool opens it with Import mod list."
            : $"Shared mod list saved. A friend clicks Import mod list in {ClientToolName}.");
    }

    private void OpenSharedList_Click(object sender, RoutedEventArgs e) =>
        ShowSharedListGuide(false, "Continue to open", OpenSharedListFile);

    private void OpenSharedListFile()
    {
        if (RefuseWhileModListRuns())
            return;
        var profile = _settings.Current();
        if (profile.Locked)
        {
            Append(SharedModList.LockedImportRefused);
            ShowSharedGuideStatus(SharedModList.LockedImportRefused);
            return;
        }

        var dialog = new OpenFileDialog { Filter = "Mod list|*.json" };
        if (dialog.ShowDialog() != true)
            return;
        // The file dialog waits for the host, and a restart after a crash may have started the server meanwhile. Then
        // nothing changes, and the guide says the list can be imported after the server stops (not to press a Save that is off).
        if (ModListRunning)
        {
            ApplyModListBlock();
            Append(SharedModList.ImportAfterStop);
            ShowSharedGuideStatus(SharedModList.ImportAfterStop);
            return;
        }

        IReadOnlyList<SharedModEntry> shared;
        try
        {
            shared = SharedModList.Read(File.ReadAllText(dialog.FileName));
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Text.Json.JsonException or IOException)
        {
            Append("That file is not a Coodsz mod list.");
            return;
        }

        var installed = _modules.Select(row => row.Id).ToList();
        var enabled = _modules.Where(row => row.Enabled).Select(row => row.Id).ToList();
        var stayOff = StableCoop.DisabledFor(profile).Where(id => !StableCoop.ShouldEnable(profile, id, installed)).ToList();
        var result = SharedModList.ApplyExtras(installed, enabled, shared.Select(entry => entry.Id).ToList(), StableCoop.Order, stayOff);
        var byId = _modules.ToDictionary(row => row.Id, StringComparer.OrdinalIgnoreCase);
        var enabledSet = new HashSet<string>(result.Enabled, StringComparer.OrdinalIgnoreCase);
        _modules.Clear();
        foreach (var id in result.Order)
        {
            if (!byId.TryGetValue(id, out var row))
                continue;
            if (!row.Required)
                row.Enabled = enabledSet.Contains(row.Id);
            byId.Remove(row.Id);
            _modules.Add(row);
        }

        foreach (var row in byId.Values)
            _modules.Add(row);
        _repairedWorkingOrder = RestoreWorkingOrder();
        StampModuleOrder();
        var changed = new HashSet<string>(result.TurnedOn.Concat(result.TurnedOff), StringComparer.OrdinalIgnoreCase);
        foreach (var row in _modules)
            row.IsNew = changed.Contains(row.Id);
        ModuleList.Items.Refresh();
        if (_modules.FirstOrDefault(row => row.IsNew) is ModuleRow highlighted)
            ModuleList.ScrollIntoView(highlighted);
        RefreshModuleSaveHint();
        if (changed.Count > 0)
        {
            SetModuleSaveHint("Changed mods stay highlighted until you click Save module config, then restart the server.");
            ModuleSaveHint.Visibility = Visibility.Visible;
        }
        var names = shared.ToDictionary(entry => entry.Id, entry => string.IsNullOrWhiteSpace(entry.Name) ? entry.Id : entry.Name, StringComparer.OrdinalIgnoreCase);
        string Label(string id) => names.TryGetValue(id, out var name) ? name : id;
        if (result.TurnedOn.Count > 0)
            Append("Turned on: " + string.Join(", ", result.TurnedOn.Select(Label)) + ".");
        if (result.TurnedOff.Count > 0)
            Append("Turned off: " + string.Join(", ", result.TurnedOff.Select(Label)) + ".");
        if (result.Missing.Count > 0)
            Append("Not on this server yet: " + string.Join(", ", result.Missing.Select(Label)) + ". Add those mods first. Bannerlord Coop is not copied from the game.");
        if (result.LeftOff.Count > 0)
            Append("Left off on the server: " + string.Join(", ", result.LeftOff.Select(Label)) + ".");
        if (result.TurnedOn.Count == 0 && result.TurnedOff.Count == 0 && result.Missing.Count == 0)
            Append("The extra mods already match that list. The working mods were left where they are.");
        else
            Append("Click Save module config, then restart the server.");
        _actions.Note("Modules", "Imported shared list. On=" + result.TurnedOn.Count
            + " Off=" + result.TurnedOff.Count
            + " Missing=" + result.Missing.Count
            + " LeftOff=" + result.LeftOff.Count);
    }
}
