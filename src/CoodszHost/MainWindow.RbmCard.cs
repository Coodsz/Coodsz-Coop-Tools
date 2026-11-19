using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BannerlordCoopLauncher.Core.Updates;
using Coodsz.Ui;
using CoodszHost.Services;

namespace CoodszHost;

// The RBM settings card on the Settings page: RBM settings for this PC's game. Coodsz Host only, and
// only while RBM is on in the setup. It reads RBM's config.xml when it shows and "Save RBM settings"
// edits only the card's values in that file (RbmConfigFile). The Coop server never runs RBM's code
// (DedicatedServerType none), so the file is the one this PC's own game reads and nothing here needs
// a server restart. Players get the coop friendly values from Coop and Friends on Connect. The AD1259
// server tool shows only a note about RBM (RbmPartsGroup).
public partial class MainWindow
{
    private RbmConfigRead? _rbmRead;
    private string _rbmShown = "";

    private static readonly SolidColorBrush RbmError = new(Color.FromRgb(0xFF, 0xB4, 0xA8));
    private static readonly SolidColorBrush RbmGood = new(Color.FromRgb(0xB6, 0xE7, 0xC2));

    private (RbmOption Option, CheckBox Box)[] RbmSwitchBoxes =>
    [
        (RbmOptions.Combat, RbmCardCombat), (RbmOptions.Ai, RbmCardAi), (RbmOptions.Tournament, RbmCardTournament),
        (RbmOptions.Campaign, RbmCardCampaign), (RbmOptions.LordEquipment, RbmLordEquipment),
        (RbmOptions.KingdomCaravans, RbmKingdomCaravans), (RbmOptions.CaravanInvestment, RbmCaravanInvestment),
        (RbmOptions.DeserterRaiders, RbmDeserterRaiders), (RbmOptions.RecruitStock, RbmRecruitStock),
        (RbmOptions.UpgradeSupplyTown, RbmUpgradeSupply),
    ];

    private (RbmOption Option, TextBox Box, string Label)[] RbmNumberBoxes =>
    [
        (RbmOptions.WorkshopProduction, RbmWorkshopBox, "Workshop production multiplier"),
        (RbmOptions.VillageProduction, RbmVillageBox, "Village production multiplier"),
        (RbmOptions.BuildingCost, RbmBuildingBox, "Building cost multiplier"),
        (RbmOptions.StartingGold, RbmGoldBox, "Starting gold multiplier"),
    ];

    // Called once from the constructor.
    private void StartRbmCard()
    {
        if (Edition.IsAD1259)
        {
            RbmCard.Visibility = Visibility.Collapsed;
            return;
        }

        RbmPartsGroup.Visibility = Visibility.Collapsed;
        RbmFilePath.Tag = UiLanguage.NoTranslate;
        NewBadges.Attach(RbmCardHeading, NewFeatures.RbmSettings, BadgePlace.After);
        NewBadges.Attach(RbmSaveButton, NewFeatures.RbmSettings);
    }

    private bool RbmCardDirty => _rbmRead is { Usable: true } && RbmCardSnapshot() != _rbmShown;

    /// <summary>Shows or hides the card for the setup's enabled mods; reads the file when it comes into view.</summary>
    private void RefreshRbmCard(bool reload = false, bool force = false)
    {
        if (Edition.IsAD1259)
            return;
        var show = RbmSwitches.ShowsCard(Edition.IsAD1259, _modules.Where(row => row.Enabled).Select(row => row.Id));
        var wasShown = RbmCard.Visibility == Visibility.Visible;
        RbmCard.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        // Unsaved changes in the card stay until Save or Read the file again, except on a setup switch.
        if (show && (force || ((!wasShown || reload) && !RbmCardDirty)))
            LoadRbmCard();
    }

    private void LoadRbmCard()
    {
        var path = RbmSwitches.ConfigPath();
        _rbmRead = RbmConfigFile.Read(path);
        RbmFilePath.Text = path;
        var usable = _rbmRead.Usable;
        RbmOptionsPanel.Visibility = usable ? Visibility.Visible : Visibility.Collapsed;
        RbmSaveButton.IsEnabled = usable;
        RbmCoopFriendlyButton.IsEnabled = usable;
        RbmNote.Text = "";
        if (!_rbmRead.Exists)
        {
            RbmFileLine.Text = "RBM has not made its config.xml on this PC yet. RBM makes it the first time your game runs with RBM. Play once with RBM on this PC, then press Read the file again.";
            RbmFileLine.Foreground = new SolidColorBrush(Color.FromRgb(0xE2, 0xC2, 0x7A));
            _rbmShown = "";
            return;
        }

        if (_rbmRead.Problem != null)
        {
            RbmFileLine.Text = _rbmRead.Problem;
            RbmFileLine.Foreground = RbmError;
            _rbmShown = "";
            return;
        }

        RbmFileLine.Text = "Values read from RBM's config.xml.";
        RbmFileLine.Foreground = new SolidColorBrush(Color.FromRgb(0x8E, 0xA0, 0xAE));
        // Every value, the four parts included, is what this PC's RBM config.xml says.
        FillRbmCard(_rbmRead.Values);
        _rbmShown = RbmCardSnapshot();
    }

    private void FillRbmCard(IReadOnlyDictionary<RbmOption, string> values)
    {
        foreach (var (option, box) in RbmSwitchBoxes)
            box.IsChecked = RbmConfigFile.IsOn(values[option]);
        foreach (var (option, box, _) in RbmNumberBoxes)
            box.Text = values[option];
    }

    private Dictionary<RbmOption, string> RbmCardShownText()
    {
        var values = new Dictionary<RbmOption, string>();
        foreach (var (option, box) in RbmSwitchBoxes)
            values[option] = RbmConfigFile.SwitchValue(box.IsChecked == true);
        foreach (var (option, box, _) in RbmNumberBoxes)
            values[option] = box.Text.Trim();
        return values;
    }

    private string RbmCardSnapshot() => string.Join("|", RbmCardShownText().Select(pair => pair.Key.Key + "=" + pair.Value));

    private void RbmReload_Click(object sender, RoutedEventArgs e)
    {
        LoadRbmCard();
        if (_rbmRead is { Usable: true })
        {
            RbmNote.Text = "Read RBM's config.xml again.";
            RbmNote.Foreground = Brushes.White;
        }
    }

    private void RbmCoopFriendly_Click(object sender, RoutedEventArgs e)
    {
        NewBadges.Seen(NewFeatures.RbmSettings);
        var wasOn = RbmLordEquipment.IsChecked == true;
        FillRbmCard(RbmOptions.CoopFriendly(RbmCardShownText()));
        RbmNote.Text = wasOn
            ? "Lords buy equipment is off now. Press Save RBM settings to keep it."
            : "Lords buy equipment is already off. Coop friendly changes nothing else.";
        RbmNote.Foreground = new SolidColorBrush(Color.FromRgb(0xE2, 0xC2, 0x7A));
    }

    private void RbmSave_Click(object sender, RoutedEventArgs e)
    {
        if (_rbmRead is not { Usable: true } read)
            return;
        var wanted = new Dictionary<RbmOption, string>();
        foreach (var (option, box) in RbmSwitchBoxes)
            wanted[option] = RbmConfigFile.SwitchValue(box.IsChecked == true);
        foreach (var (option, box, label) in RbmNumberBoxes)
        {
            if (!RbmConfigFile.TryNormalize(option, box.Text, label, out var value, out var error))
            {
                RbmNote.Text = error;
                RbmNote.Foreground = RbmError;
                return;
            }

            wanted[option] = value;
        }

        // Only what differs from the file is written, so untouched values keep their own spelling.
        var edits = wanted.Where(pair => !RbmConfigFile.SameValue(pair.Key, pair.Value, read.Values[pair.Key]))
            .ToDictionary(pair => pair.Key, pair => pair.Value);
        var path = RbmSwitches.ConfigPath();
        IReadOnlyList<string> changed;
        try
        {
            changed = edits.Count == 0 ? [] : RbmConfigFile.Save(path, edits);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or System.Xml.XmlException)
        {
            RbmNote.Text = "RBM's settings were not saved (" + ex.Message + ").";
            RbmNote.Foreground = RbmError;
            return;
        }

        LoadRbmCard();
        if (changed.Count == 0)
        {
            RbmNote.Text = "Nothing changed. RBM's config.xml already has these settings.";
            RbmNote.Foreground = Brushes.White;
            return;
        }

        Append("RBM settings saved to " + path + ": " + string.Join(", ", changed) + ".");
        RbmNote.Text = "Saved. Your game on this PC uses these RBM settings the next time it starts. Other players set theirs in their own game.";
        RbmNote.Foreground = RbmGood;
    }
}
