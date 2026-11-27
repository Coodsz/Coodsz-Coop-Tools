// The new world wizard follows CoopKeep's NewWorldWizard view (https://github.com/Aderimo/bannerlord-coopkeep,
// src/CoopKeep.App/Views/NewWorldWizard.axaml), ported from Avalonia to WPF and changed for Coodsz
// Host: its steps fill in the settings + New save, Load single-player save and Save settings already use.

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using BannerlordCoopLauncher.Core.Configuration;
using BannerlordCoopLauncher.Core.Keep;
using BannerlordCoopLauncher.Core.Localization;
using BannerlordCoopLauncher.Core.Modules;

namespace CoodszHost.Keep;

/// <summary>
/// The new world wizard. It only fills in <see cref="Plan"/>; MainWindow creates the world when
/// the dialog closes with true, with the code of + New save, Load single-player save and Save settings.
/// </summary>
public partial class NewWorldWindow : Window
{
    private readonly IReadOnlyList<string> _existingSaves;
    private readonly bool _streamer;
    private readonly List<Border> _bars = new();
    private readonly List<TextBlock> _barTitles = new();
    private readonly Dictionary<string, CheckBox> _rules = new(StringComparer.Ordinal);
    private NewWorldStep _step = NewWorldStep.Start;
    private bool _filling;

    public NewWorldWindow(NewWorldPlan plan, IReadOnlyList<string> existingSaves, bool streamer)
    {
        InitializeComponent();
        Coodsz.Ui.WindowPlacement.KeepOnScreen(this);
        // Set here, not as a Tag in the XAML, so the hint is translated.
        PasswordText.Tag = Translator.T("Leave empty for no password");
        Plan = plan;
        _existingSaves = existingSaves;
        _streamer = streamer;
        BuildBars();
        BuildPresets();
        BuildRules();
        _filling = true;
        NameBox.Text = plan.Name;
        // With Limitless and its Coop patch on the server the slider goes up to BigBattles.Max.
        if (plan.BattleMode == BigBattleMode.Unlocked)
        {
            BattleSizeSlider.Maximum = BigBattles.Max;
            BattleSizeSlider.TickFrequency = 100;
            BattleSizeSlider.ToolTip = BigBattles.Tooltip;
        }
        BattleSizeSlider.Value = plan.BattleSize;
        ShowBattleSizeHint();
        BattleSizeLabel.Text = plan.BattleSize.ToString();
        PasswordText.Text = plan.Password;
        PasswordHidden.Password = plan.Password;
        _filling = false;
        PasswordText.Visibility = streamer ? Visibility.Collapsed : Visibility.Visible;
        PasswordHidden.Visibility = streamer ? Visibility.Visible : Visibility.Collapsed;
        StreamerNote.Visibility = streamer ? Visibility.Visible : Visibility.Collapsed;
        ShowStep();
    }

    public NewWorldPlan Plan { get; }

    private void BuildBars()
    {
        foreach (var step in NewWorldPlan.Steps)
        {
            var bar = new Border { Height = 4, CornerRadius = new CornerRadius(2), Margin = new Thickness(0, 0, 6, 0) };
            var title = new TextBlock { Text = NewWorldPlan.StepTitle(step), FontSize = 12, Margin = new Thickness(0, 6, 0, 0) };
            var cell = new StackPanel();
            cell.Children.Add(bar);
            cell.Children.Add(title);
            StepBars.Children.Add(cell);
            _bars.Add(bar);
            _barTitles.Add(title);
        }
    }

    private void BuildPresets()
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
                GroupName = "preset",
                Content = text,
                Tag = preset.Id,
                Margin = new Thickness(0, 0, 10, 0),
                VerticalContentAlignment = VerticalAlignment.Top,
                IsChecked = preset.Id == Plan.PresetId,
                ToolTip = preset.Description,
            };
            card.Checked += (_, _) =>
            {
                if (!_filling)
                    Plan.UsePreset(preset.Id);
            };
            PresetCards.Children.Add(card);
        }
    }

    // A short note under each rule, so the box says what it does.
    private static string RuleHint(string id) => id switch
    {
        "modOptions.clientsCanUseCheats" => "Players can add troops, gear, and teleport.",
        "modOptions.fastForwardEnabled" => "The fast-forward button works for the host and for every player.",
        "modOptions.autoPauseEnabled" => "Pauses when every player is in a battle or a settlement.",
        "modOptions.showPlayerNameplates" => "Clients can show names above allied players.",
        "modOptions.coopClansEnabled" => "Players can join another player's clan or marry another player.",
        "modOptions.voiceEnabled" => "Proximity voice chat for everyone on the server.",
        _ => "",
    };

    private void BuildRules()
    {
        foreach (var id in NewWorldPlan.RuleIds)
        {
            var setting = ModSettingsCatalog.Find(id)!;
            var box = new CheckBox
            {
                Style = (Style)FindResource("Keep.Check"),
                Content = setting.Label,
                IsChecked = Plan.Rule(id),
                Margin = new Thickness(0, 6, 0, 0),
                ToolTip = RuleHint(id).Length > 0 ? RuleHint(id) : "Turns this rule on or off for the new world.",
            };
            // Checked and Unchecked, not Click: a rule ticked by keyboard focus tools or a screen
            // reader (no Click event) was shown on but saved off.
            box.Checked += (_, _) => Plan.SetRule(id, true);
            box.Unchecked += (_, _) => Plan.SetRule(id, false);
            _rules[id] = box;
            RuleBoxes.Children.Add(box);
            var hint = RuleHint(id);
            if (hint.Length > 0)
                RuleBoxes.Children.Add(new TextBlock { Text = hint, Style = (Style)FindResource("Keep.Dim"), Margin = new Thickness(29, 2, 0, 4), TextWrapping = TextWrapping.Wrap });
        }
    }

    private void ShowStep()
    {
        var index = ((IList<NewWorldStep>)NewWorldPlan.Steps).IndexOf(_step);
        for (var i = 0; i < _bars.Count; i++)
        {
            _bars[i].Background = (Brush)FindResource(i <= index ? "Keep.Accent" : "Keep.BorderDim");
            _barTitles[i].Foreground = (Brush)FindResource(i <= index ? "Keep.TextMain" : "Keep.TextDim");
        }

        StartStep.Visibility = _step == NewWorldStep.Start ? Visibility.Visible : Visibility.Collapsed;
        NameStep.Visibility = _step == NewWorldStep.Name ? Visibility.Visible : Visibility.Collapsed;
        DifficultyStep.Visibility = _step == NewWorldStep.Difficulty ? Visibility.Visible : Visibility.Collapsed;
        RulesStep.Visibility = _step == NewWorldStep.Rules ? Visibility.Visible : Visibility.Collapsed;
        ServerStep.Visibility = _step == NewWorldStep.Server ? Visibility.Visible : Visibility.Collapsed;
        BackButton.Visibility = NewWorldPlan.Back(_step) == null ? Visibility.Hidden : Visibility.Visible;
        var last = NewWorldPlan.Next(_step) == null;
        NextText.Text = last ? "Create world" : "Continue";
        if (_step == NewWorldStep.Name)
            Dispatcher.BeginInvoke(() => NameBox.Focus(), System.Windows.Threading.DispatcherPriority.Input);
        if (_step == NewWorldStep.Server)
            SummaryText.Text = Summary();
    }

    private string Summary()
    {
        var preset = WorldPresets.Find(Plan.PresetId);
        var parts = new List<string>
        {
            Plan.Name,
            Translator.T(Plan.FromSinglePlayer ? "Continue a single-player campaign" : "Start a fresh campaign"),
        };
        if (preset != null)
            parts.Add(Translator.T(preset.Title));
        parts.AddRange(WorldPresets.Chips(Plan.Values).Skip(1).Select(Translator.T));
        return string.Join("  ·  ", parts);
    }

    private void Name_Changed(object sender, TextChangedEventArgs e)
    {
        if (_filling)
            return;
        Plan.Name = NameBox.Text.Trim();
        NameProblem.Visibility = Visibility.Collapsed;
    }

    private void BattleSize_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (BattleSizeLabel == null || _filling)
            return;
        Plan.BattleSize = (int)BattleSizeSlider.Value;
        BattleSizeLabel.Text = Plan.BattleSize.ToString();
        ShowBattleSizeHint();
    }

    private void ShowBattleSizeHint()
    {
        if (Plan.BattleMode != BigBattleMode.Unlocked)
            return;
        BattleSizeHint.Text = Translator.T("Total soldiers on the field, both sides, from 200 to 10000.") + " "
            + Translator.T(BigBattles.TierText(Plan.BattleSize)) + ". " + Translator.T(BigBattles.RamText(Plan.BattleSize));
    }

    private void Next_Click(object sender, RoutedEventArgs e)
    {
        if (_step == NewWorldStep.Start)
            Plan.FromSinglePlayer = SinglePlayerChoice.IsChecked == true;
        var problem = Plan.Problem(_step, _existingSaves);
        if (problem.Length > 0)
        {
            NameProblem.Text = problem;
            NameProblem.Visibility = Visibility.Visible;
            return;
        }

        if (NewWorldPlan.Next(_step) is { } next)
        {
            _step = next;
            ShowStep();
            return;
        }

        Plan.Password = _streamer ? PasswordHidden.Password : PasswordText.Text;
        DialogResult = true;
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        if (NewWorldPlan.Back(_step) is { } back)
        {
            _step = back;
            ShowStep();
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Header_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }
}
