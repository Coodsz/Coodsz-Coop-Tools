using System.IO;
using System.Windows;
using BannerlordCoopLauncher.Core.Modules;
using BannerlordCoopLauncher.Core.Updates;
using CoodszHost.Services;

namespace BannerlordCoopLauncher;

// Keep RBM coop friendly. The Coop server never runs RBM's code (RBM's SubModule.xml has
// DedicatedServerType none), so RBM's campaign rules run in each player's own game and read this
// PC's Documents\Mount and Blade II Bannerlord\Configs\RBM\config.xml. While RBM is in the setup,
// Connect turns off Lords buy equipment there (it empties the town markets in coop) with the same
// safe edit as the Coodsz Host RBM card and the AD1259 client launcher (RbmSwitches.ApplyForPlayer).
public partial class MainWindow
{
    private bool _fillingRbm;

    private static bool RbmInSetup(ModuleResolution resolution) =>
        RbmSwitches.InUse(resolution.All.Select(module => module.ModuleId));

    // Called from BindModules, so the box follows the setup's mods.
    private void ShowRbmCoopFriendly(ModuleResolution resolution)
    {
        _fillingRbm = true;
        RbmCoopFriendlyBox.IsChecked = _settings.KeepRbmCoopFriendly;
        _fillingRbm = false;
        RbmCoopFriendlyPanel.Visibility = RbmInSetup(resolution) ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RbmCoopFriendly_Changed(object sender, RoutedEventArgs e)
    {
        if (_fillingRbm || !IsLoaded)
            return;
        // NewBadges clears on Click; a toggle by keyboard or UI automation can raise only Checked.
        Coodsz.Ui.NewBadges.Seen(NewFeatures.RbmCoopFriendly);
        _settings.KeepRbmCoopFriendly = RbmCoopFriendlyBox.IsChecked == true;
        _settings.Save(SettingsPath);
        Log(_settings.KeepRbmCoopFriendly
            ? "Keep RBM coop friendly is on. Connect turns off RBM's Lords buy equipment in your own RBM settings."
            : "Keep RBM coop friendly is off. Connect leaves your own RBM settings as they are.");
    }

    // Before the game starts, on Connect and on Test to main menu.
    private void ApplyRbmCoopFriendly()
    {
        if (_resolution == null)
            return;
        try
        {
            var line = RbmSwitches.ApplyForPlayer(_resolution.All.Select(module => module.ModuleId), _settings.KeepRbmCoopFriendly);
            if (line != null)
                Log(line);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException or InvalidOperationException or InvalidDataException)
        {
            Log("Could not write RBM's settings (" + ex.Message + "). RBM uses whatever its config.xml already says.");
        }
    }
}
