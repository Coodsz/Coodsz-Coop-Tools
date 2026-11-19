using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using BannerlordCoopLauncher.Core.Configuration;
using BannerlordCoopLauncher.Core.Localization;
using BannerlordCoopLauncher.Core.Modules;
using CoodszHost.Services;
using TaomCoop.Core;

namespace CoodszHost;

// Big battles with Limitless. The battle size range follows the server's own mod list (the
// modules turned on in the Modules page, which Save module config writes to
// modded-server-modules.txt for Start-ModdedServer.ps1): 200 to 1000 by default, up to
// BigBattles.Max with Limitless and Limitless.CoopCompat both on.
public partial class MainWindow
{
    private const string NormalRangeText = "Battle size for coop battles, from 200 to 1000. Coop's default is 1000. ";
    private const string BigRangeText = "Total soldiers on the field, both sides, from 200 to 10000. Limitless is on. ";

    private static readonly Brush SmoothBrush = new SolidColorBrush(Color.FromRgb(0x8F, 0xCB, 0x9B));
    private static readonly Brush BigBrush = new SolidColorBrush(Color.FromRgb(0xE2, 0xC2, 0x7A));
    private static readonly Brush EpicBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xB4, 0xA8));

    // The saved battle size that was brought back to 1000 because Limitless left the mod list; 0 for none.
    private int _battleSizeClampedFrom;
    // True while RefreshBattleSizeMode moves the slider itself, so that is not taken for the host's hand.
    private bool _settingBattleSizeMode;
    private int? _suggestedBattleSize;

    private BigBattleMode BattleMode =>
        BigBattles.Detect(_modules.Where(row => row.Enabled).Select(row => row.Id));

    /// <summary>Sets the slider's range for the mod list. A battle size above the range comes back
    /// to its top, and the host is told why.</summary>
    private void RefreshBattleSizeMode()
    {
        if (BattleSizeSlider == null)
            return;
        var mode = BattleMode;
        var before = (int)Math.Round(BattleSizeSlider.Value);
        var size = BigBattles.ClampBack(before, mode, out var clamped);
        if (clamped)
            _battleSizeClampedFrom = before;
        else if (mode == BigBattleMode.Unlocked)
            _battleSizeClampedFrom = 0;

        var unlocked = mode == BigBattleMode.Unlocked;
        _settingBattleSizeMode = true;
        try
        {
            BattleSizeSlider.Maximum = BigBattles.MaxFor(mode);
            BattleSizeSlider.TickFrequency = unlocked ? 100 : 10;
            BattleSizeSlider.Value = size;
        }
        finally
        {
            _settingBattleSizeMode = false;
        }
        BattleSizeSlider.ToolTip = unlocked ? BigBattles.Tooltip : null;
        BattleSizeLabel.Text = size.ToString(CultureInfo.InvariantCulture);
        BattleSizeRange.Text = Translator.T(unlocked ? BigRangeText : NormalRangeText);
        BigBattlePanel.Visibility = unlocked ? Visibility.Visible : Visibility.Collapsed;
        ShowBattleSizeTier();

        var notes = new List<string>();
        if (_battleSizeClampedFrom > 0)
            notes.Add(BigBattles.ClampedNote(_battleSizeClampedFrom));
        var patch = _modules.FirstOrDefault(row => row.Id.Equals(BigBattles.PatchId, StringComparison.OrdinalIgnoreCase));
        if (mode == BigBattleMode.NeedsPatch)
            notes.Add(Translator.T(BigBattles.NeedsPatchNote));
        BattleSizeNote.Text = string.Join(" ", notes);
        AddLimitlessPatchButton.ToolTip = BigBattles.AddPatchTip;
        AddLimitlessPatchButton.Visibility = mode == BigBattleMode.NeedsPatch && patch != null && !_settings.Current().Locked
            ? Visibility.Visible
            : Visibility.Collapsed;
        BattleSizeNotePanel.Visibility = notes.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>The label, RAM estimate and suggestion under the unlocked slider.</summary>
    private void ShowBattleSizeTier()
    {
        if (BigBattlePanel == null || BigBattlePanel.Visibility != Visibility.Visible)
            return;
        var size = (int)Math.Round(BattleSizeSlider.Value);
        BattleSizeTier.Text = BigBattles.TierText(size);
        BattleSizeTier.Foreground = BigBattles.Tier(size) switch
        {
            BigBattleTier.Smooth => SmoothBrush,
            BigBattleTier.Big => BigBrush,
            _ => EpicBrush,
        };
        BattleSizeRam.Text = BigBattles.RamText(size);
        _suggestedBattleSize ??= BigBattles.ThisPcRamGb() is var ram and > 0 ? BigBattles.SuggestedSize(ram) : 0;
        BattleSizeSuggested.Text = _suggestedBattleSize > 0 ? BigBattles.SuggestedText(_suggestedBattleSize.Value) : "";
        BattleSizeSuggested.Visibility = _suggestedBattleSize > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // Turns on Limitless Coop Compat right after Limitless and saves the module list, like Save module config.
    private void AddLimitlessPatch_Click(object sender, RoutedEventArgs e)
    {
        var patch = _modules.FirstOrDefault(row => row.Id.Equals(BigBattles.PatchId, StringComparison.OrdinalIgnoreCase));
        var limitless = _modules.FirstOrDefault(row => row.Id.Equals(BigBattles.LimitlessId, StringComparison.OrdinalIgnoreCase));
        if (patch == null || limitless == null || _settings.Current().Locked)
            return;
        try
        {
            patch.Enabled = true;
            var from = _modules.IndexOf(patch);
            var after = _modules.IndexOf(limitless);
            if (from < after)
                _modules.Move(from, after);
            PersistProfile();
            ModuleList.Items.Refresh();
            RefreshModuleSaveHint();
            UpdateModuleSummary();
            RefreshBattleSizeMode();
            _actions.Note("Big battles", "Turned on " + BigBattles.PatchId + " after " + BigBattles.LimitlessId + " and saved the module list.");
            Append("Turned on Limitless Coop Compat in the server's mod list. Restart the server so bigger battles apply. Players need it too.");
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            BattleSizeNote.Text = ex.Message;
        }
    }

    /// <summary>Right before Start: a battle size above what the mod list allows comes back to the top
    /// of the range in mod-config.json, so Start-ModdedServer.ps1 accepts it.</summary>
    private void ApplyBattleSizeLimit()
    {
        try
        {
            var path = ServerSession.ModConfigPath;
            if (!File.Exists(path))
                return;
            var json = Services.JsoncEditor.Read(path);
            if (CoopModConfig.ReadSavedBattleSize(json) is not { } saved)
                return;
            var mode = BattleMode;
            var size = BigBattles.ClampBack((int)Math.Round(saved), mode, out var clamped);
            if (!clamped)
                return;
            Services.JsoncEditor.Write(path, CoopModConfig.SetBattleSize(json, size, mode));
            Append(BigBattles.ClampedNote((int)Math.Round(saved)));
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            Append("Could not check the battle size in mod-config.json. " + ex.Message);
        }
    }
}
