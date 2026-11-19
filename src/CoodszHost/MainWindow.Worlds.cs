using System.Windows;
using System.Windows.Controls;
using BannerlordCoopLauncher.Core.Configuration;
using BannerlordCoopLauncher.Core.Keep;
using BannerlordCoopLauncher.Core.Updates;
using Coodsz.Ui;
using CoodszHost.Keep;
using CoodszHost.Services;
using TaomCoop.Core;

namespace CoodszHost;

// The world preset cards on the Settings page and the new world wizard (Coodsz Host only). A preset
// only fills in the page's Coop settings, and Save settings keeps them as before. The wizard uses
// + New save (or Load single-player save) and keeps its settings for the new save, the same store
// Save settings writes, which goes into mod-config.json each time that save starts.
public partial class MainWindow
{
    private readonly List<RadioButton> _presetCards = new();

    /// <summary>Called once from StartKeepPages.</summary>
    private void StartPresets()
    {
        foreach (var preset in WorldPresets.All)
        {
            var text = new StackPanel();
            text.Children.Add(new TextBlock { Text = preset.Title, FontWeight = FontWeights.SemiBold, FontSize = 15 });
            text.Children.Add(new TextBlock
            {
                Text = preset.Description,
                Style = (Style)FindResource("Keep.Dim"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 0),
            });
            var card = new RadioButton
            {
                Style = (Style)FindResource("Keep.Choice"),
                GroupName = "WorldPreset",
                ToolTip = "Fills in the difficulty and life and death settings below from this preset. Nothing is written until you press Save settings.",
                Content = text,
                Tag = preset.Id,
                Margin = new Thickness(0, 0, 10, 0),
                VerticalContentAlignment = VerticalAlignment.Top,
            };
            // Click, not Checked: showing the matching card must not apply it again.
            card.Click += (_, _) => UsePresetOnPage(preset);
            _presetCards.Add(card);
            PresetButtons.Children.Add(card);
        }

        foreach (var box in SettingsCombos())
            box.SelectionChanged += (_, _) => ShowPresetOnPage();
        ShowPresetOnPage();
    }

    /// <summary>The Coop settings the page's dropdowns show (the typed numbers are not needed to match a preset).</summary>
    private Dictionary<string, object> PresetValuesFromPage()
    {
        var values = ModSettingsCatalog.Defaults(ServerCoopVersion());
        foreach (var (box, id) in CatalogueCombos())
            values[id] = SettingFromCombo(box, id);
        foreach (var (id, control) in _moreCoopSettings)
            if (control is ComboBox combo)
                values[id] = SettingFromCombo(combo, id);
        values[PreferredHostsId] = _preferredHosts.ToArray();
        return values;
    }

    private void UsePresetOnPage(WorldPreset preset)
    {
        NewBadges.Seen(NewFeatures.WorldPresets);
        var values = PresetValuesFromPage();
        foreach (var (box, id) in CatalogueTexts())
            values[id] = ModSettingsCatalog.ParseText(ModSettingsCatalog.Find(id)!, box.Text) ?? DefaultOf(ModSettingsCatalog.Find(id)!);
        foreach (var (id, control) in _moreCoopSettings)
            if (control is TextBox text)
                values[id] = ModSettingsCatalog.ParseText(ModSettingsCatalog.Find(id)!, text.Text) ?? DefaultOf(ModSettingsCatalog.Find(id)!);
        values[BattleSizeId] = CoopModConfig.ClampBattleSize(BattleSizeSlider.Value, BattleMode);
        WorldPresets.Apply(values, preset);
        FillCoopSettings(values);
        RefreshSettingsPulse();
        ShowPresetOnPage();
    }

    private void ShowPresetOnPage()
    {
        if (_presetCards.Count == 0)
            return;
        var match = WorldPresets.Detect(PresetValuesFromPage());
        foreach (var card in _presetCards)
            card.IsChecked = match != null && (string)card.Tag == match.Id;
        PresetNote.Text = match == null
            ? "Custom: the settings below match no preset."
            : "The settings below match " + match.Title + ". Press Save settings to keep a change.";
    }

    /// <summary>Create a world on Saves and in the first run steps: the wizard, then + New save or Load single-player save with its settings.</summary>
    private void OpenNewWorld()
    {
        NewBadges.Seen(NewFeatures.CreateWorld);
        if (_controlBusy)
            return;
        var existing = SaveCatalog.NamesIn(ServerSession.SavesPath);
        var plan = new NewWorldPlan(CoopSettingsFor(null, out _), ServerCoopVersion()) { Password = PasswordBox.Text, BattleMode = BattleMode };
        var window = new NewWorldWindow(plan, existing, Streamer) { Owner = this };
        if (window.ShowDialog() != true)
            return;
        CreateWorld(window.Plan);
    }

    private void CreateWorld(NewWorldPlan plan)
    {
        var name = plan.Name;
        NewSaveNameBox.Text = name;
        if (plan.FromSinglePlayer)
            LoadSinglePlayerSave_Click(this, new RoutedEventArgs());
        else
            NewSave_Click(this, new RoutedEventArgs());
        if (!string.Equals(_settings.Current().SaveName, name, StringComparison.OrdinalIgnoreCase))
        {
            // The file dialog was closed, or the save could not be made; that code already said why.
            NewSaveNameBox.Text = "";
            return;
        }

        KeepSaveProfile(name, plan.Values);
        if (PasswordBox.Text != plan.Password)
        {
            PasswordBox.Text = plan.Password;
            _settings.Current().Password = plan.Password;
            WriteServerPassword(plan.Password);
        }

        HostSettings.Save(SettingsPath, _settings);
        FillCoopSettings(CoopSettingsFor(name, out var kept));
        ShowSaveProfileNote(name, kept);
        RememberSettingsSnapshot();
        var note = plan.FromSinglePlayer
            ? name + " is ready. Its settings are kept and used each time it starts. Click Start server to load it."
            : name + " is ready. Its settings are kept and used each time it starts. Click Start server to create it.";
        _actions.Note("World", "New world " + name + " on " + _settings.Current().Name + (plan.FromSinglePlayer ? " from a single-player save" : ""));
        Append(note);
        SetControl(note, done: true);
        ShowPage("Dashboard");
    }
}
