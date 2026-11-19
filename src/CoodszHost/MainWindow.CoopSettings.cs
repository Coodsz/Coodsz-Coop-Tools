using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BannerlordCoopLauncher.Core.Configuration;
using BannerlordCoopLauncher.Core.IO;
using BannerlordCoopLauncher.Core.Keep;
using BannerlordCoopLauncher.Core.Localization;
using BannerlordCoopLauncher.Core.Modules;
using CoodszHost.Services;
using TaomCoop.Core;

namespace CoodszHost;

// The Coop gameplay settings on the Settings page come from one list, ModSettingsCatalog:
// the boxes on the page are tied to a catalogue setting each, the rest sit under
// "Show more Coop settings", and Save settings keeps them per campaign save so the save's
// own settings are written into mod-config.json right before it starts.
public partial class MainWindow
{
    private const string TrueText = "True";
    private const string FalseText = "False";

    private readonly Dictionary<string, Control> _moreCoopSettings = new(StringComparer.Ordinal);
    private readonly List<(ModSettingDefinition Setting, UIElement Input, TextBlock Note)> _gatedCoopSettings = new();
    private bool _fillingCoopSettings;

    private const string PreferredHostsId = "modOptions.preferredBattleHosts";
    private readonly List<string> _preferredHosts = new();
    private ListBox? _preferredHostsList;
    private ComboBox? _preferredHostsPick;
    private TextBox? _preferredHostsPaste;
    private StackPanel? _preferredHostsPasteRow;
    private TextBlock? _preferredHostsError;

    // Per save settings live next to host.json, never in the server's save folder.
    private static WorldProfileStore SaveProfiles => new(ToolData.File("save-settings"));

    private (ComboBox Box, string Id)[] CatalogueCombos() =>
    [
        (PlayerDamage, "difficulty.playerReceivedDamage"),
        (TroopDamage, "difficulty.playerTroopsReceivedDamage"),
        (CombatAi, "difficulty.combatAIDifficulty"),
        (Recruitment, "difficulty.recruitmentDifficulty"),
        (MapMovement, "difficulty.playerMapMovementSpeed"),
        (Stealth, "difficulty.stealthAndDisguiseDifficulty"),
        (Persuasion, "difficulty.persuasionSuccessChance"),
        (ClanDeath, "difficulty.clanMemberDeathChance"),
        (BattleDeath, "difficulty.battleDeath"),
        (AutoPerks, "difficulty.autoAllocateClanMemberPerks"),
        (BirthDeathBox, "difficulty.birthAndDeath"),
        (FastForwardBox, "modOptions.fastForwardEnabled"),
        (AutoPauseBox, "modOptions.autoPauseEnabled"),
        (ClientCheatsBox, "modOptions.clientsCanUseCheats"),
        (SpeedLimitBox, "modOptions.speedLimitWhilePlayersInBattle"),
        (SettlementEconomyBox, "modOptions.goldFoodInfluenceChangeInSettlements"),
        (DisconnectedEconomyBox, "modOptions.goldFoodInfluenceChangeForDisconnectedPlayers"),
        (WandererScaleBox, "modOptions.wandererLimitScalesWithPlayers"),
        (SmithOutsideBox, "modOptions.smithingStaminaRecoveryOutsideSettlements"),
        (HeroExecutionsBox, "modOptions.enableHeroExecutions"),
        (ClanExecutionsBox, "modOptions.enablePlayerClanMemberExecutions"),
        (NameplatesBox, "modOptions.showPlayerNameplates"),
        (BattleEconomyBox, "modOptions.goldFoodInfluenceChangeInBattles"),
        (DefectionBox, "modOptions.lordDefectionRetries"),
    ];

    private (TextBox Box, string Id)[] CatalogueTexts() =>
    [
        (AiWindowBox, "modOptions.playerBattleAiJoinWindowHours"),
        (WandererBox, "modOptions.wandererLimit"),
        (ClanTierBox, "modOptions.playerKingdomClanTierRequired"),
        (SmithBox, "modOptions.smithingStaminaRecoveryMultiplier"),
        (LooterCountBox, "modOptions.maximumLootersMultiplier"),
        (LooterSizeBox, "modOptions.looterPartySizeMultiplier"),
    ];

    private const string BattleSizeId = "modOptions.battleSize";

    // Short notes for the settings under "Show more Coop settings".
    private static string MoreSettingHint(string id) => id switch
    {
        "modOptions.voiceEnabled" => "Proximity voice chat for everyone on the server.",
        "modOptions.spawnRateWanderers" => "Coop's default is 1. Below 1 means no new wanderers.",
        "modOptions.ensureUnaffiliatedWanderers" => "On: the wanderer limit counts free wanderers only.",
        "modOptions.blockAiWarDeclarationsOnOfflinePlayers" => "On: AI kingdoms do not declare war on players who are offline.",
        "modOptions.coopClansEnabled" => "On: players can join another player's clan or marry another player.",
        "modOptions.enablePlayerExecutions" => "On: players can execute each other. Does nothing while Hero executions is Off.",
        "modOptions.playerWoundedBattleEntry" => "On: wounded players can still join battles.",
        "modOptions.postBattleAiAmnestySeconds" => "Seconds AI parties leave a player alone after a battle. 0 is off.",
        "modOptions.clansAfterKingdomDestruction" => "Keep clans: clans of a destroyed kingdom stay alive as independent clans.",
        "modOptions.kingdomDecisionVoteTimeoutSeconds" => "Seconds players have to vote on kingdom decisions.",
        "voice.mapFullVolumeDistance" => "Full volume up to this distance on the campaign map.",
        "voice.mapMaximumDistance" => "Nobody is heard past this distance on the campaign map.",
        "voice.sceneFullVolumeDistance" => "Full volume up to this distance in battles and towns.",
        "voice.sceneMaximumDistance" => "Nobody is heard past this distance in battles and towns.",
        _ => ""
    };

    // Called once from FillDifficulties, before WatchSettingsPage, so these boxes are watched too.
    private void BuildMoreCoopSettings()
    {
        var shown = CatalogueCombos().Select(item => item.Id)
            .Concat(CatalogueTexts().Select(item => item.Id))
            .Append(BattleSizeId)
            .ToHashSet(StringComparer.Ordinal);
        // The kingdom protection dropdown in Advanced: server rules owns Coop's offline war option.
        shown.Add(BannerlordCoopLauncher.Core.Server.ServerRulesFile.CoopOfflineWarOption);
        var groups = new[]
        {
            (ModSettingsCatalog.DifficultySection, "Difficulty"),
            (ModSettingsCatalog.OptionsSection, "Mod options"),
            (ModSettingsCatalog.VoiceSection, "Voice chat distances")
        };
        foreach (var (section, title) in groups)
        {
            // The AD1259 server never runs the Coop nightly, so its nightly only rows would stay greyed for good.
            var settings = ModSettingsCatalog.All.Where(d => d.Section == section && !shown.Contains(d.Id)
                && !(Edition.IsAD1259 && d.Since == "v0.1.8")).ToList();
            if (settings.Count == 0)
                continue;
            MoreCoopSettingsPanel.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, MoreCoopSettingsPanel.Children.Count == 0 ? 0 : 10, 0, 4)
            });
            foreach (var setting in settings)
                MoreCoopSettingsPanel.Children.Add(setting.Kind == ModSettingKind.SteamIdList ? PreferredHostsRow(setting) : MoreSettingRow(setting));
        }

        MoreCoopSettingsPanel.Children.Add(new TextBlock
        {
            Text = "These are the other settings Coop reads from mod-config.json. Coop's default is used for any you leave alone.",
            Foreground = new SolidColorBrush(Color.FromRgb(0x8E, 0xA0, 0xAE)),
            FontSize = 13,
            Margin = new Thickness(0, 10, 0, 0),
            TextWrapping = TextWrapping.Wrap
        });
    }

    private UIElement MoreSettingRow(ModSettingDefinition setting)
    {
        var grid = new Grid { Margin = new Thickness(0, 3, 0, 3) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(340) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(new TextBlock { Text = setting.Label, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap });

        Control control;
        if (setting.Kind is ModSettingKind.Bool or ModSettingKind.Choice or ModSettingKind.Level)
        {
            var box = new ComboBox { Style = (Style)FindResource("DifficultyBox"), Width = 160, HorizontalAlignment = HorizontalAlignment.Left, ToolTip = "Picks the value Coop uses for this setting. Press Save settings to keep it." };
            box.RequestBringIntoView += KeepDropdownSelection;
            SettingChoiceLabel.SetChoiceLabels(box, true);
            foreach (var choice in setting.Kind == ModSettingKind.Bool ? new[] { TrueText, FalseText } : setting.Choices?.ToArray() ?? Array.Empty<string>())
                box.Items.Add(choice);
            control = box;
        }
        else
        {
            control = new TextBox { Width = 160, HorizontalAlignment = HorizontalAlignment.Left };
        }

        control.Name = "More_" + setting.Id.Replace('.', '_');
        Grid.SetColumn(control, 1);
        grid.Children.Add(control);
        // The note and the range are separate runs, so each is translated on its own.
        var hint = MoreSettingHint(setting.Id);
        var range = setting.Kind is ModSettingKind.Int or ModSettingKind.Number ? RangeHint(setting) : "";
        var notes = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        if (setting.Since != null)
            notes.Children.Add(SinceNote(setting, control));
        if (hint.Length > 0 || range.Length > 0)
        {
            var note = new TextBlock
            {
                Foreground = new SolidColorBrush(Color.FromRgb(0x8E, 0xA0, 0xAE)),
                TextWrapping = TextWrapping.Wrap
            };
            if (hint.Length > 0)
                note.Inlines.Add(new System.Windows.Documents.Run(hint));
            if (hint.Length > 0 && range.Length > 0)
                note.Inlines.Add(new System.Windows.Documents.Run(" "));
            if (range.Length > 0)
                note.Inlines.Add(new System.Windows.Documents.Run(range));
            notes.Children.Add(note);
        }

        if (notes.Children.Count > 0)
        {
            Grid.SetColumn(notes, 2);
            grid.Children.Add(notes);
        }

        _moreCoopSettings[setting.Id] = control;
        return grid;
    }

    // A key added by a newer Coop than the server runs: its input is greyed and this note says why. The file
    // never gets the key either (ModConfigStore.Write with the server's version).
    private TextBlock SinceNote(ModSettingDefinition setting, UIElement input)
    {
        var note = new TextBlock
        {
            Text = setting.Since == "v0.1.7" ? "Needs Coop 0.1.7" : "Nightly only",
            Foreground = new SolidColorBrush(Color.FromRgb(0xE2, 0xC2, 0x7A)),
            Visibility = Visibility.Collapsed,
            TextWrapping = TextWrapping.Wrap
        };
        _gatedCoopSettings.Add((setting, input, note));
        return note;
    }

    private void ShowCoopSettingSupport()
    {
        var version = ServerCoopVersion();
        foreach (var (setting, input, note) in _gatedCoopSettings)
        {
            var supported = ModSettingsCatalog.Supports(setting, version);
            input.IsEnabled = supported;
            note.Visibility = supported ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    // --- preferred battle hosts ---------------------------------------------

    // Coop's preferredBattleHosts, a list of SteamID64s in order. It sits in the more settings card with its own
    // list; players are picked from the ones online now and the ones in the picked save's .json, or pasted.
    private UIElement PreferredHostsRow(ModSettingDefinition setting)
    {
        var dim = new SolidColorBrush(Color.FromRgb(0x8E, 0xA0, 0xAE));
        var grid = new Grid { Margin = new Thickness(0, 6, 0, 3) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(340) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(new TextBlock { Text = setting.Label, TextWrapping = TextWrapping.Wrap });

        var editor = new StackPanel();
        var panel = new StackPanel();
        panel.Children.Add(SinceNote(setting, editor));
        panel.Children.Add(editor);
        Grid.SetColumn(panel, 1);
        grid.Children.Add(panel);

        editor.Children.Add(new TextBlock
        {
            Text = "The first listed player who has finished loading hosts the battle. The server waits up to 5 seconds for a higher one.",
            Foreground = dim,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 4)
        });
        // Player names and SteamID64s, never translated.
        _preferredHostsList = new ListBox { Width = 420, Height = 96, HorizontalAlignment = HorizontalAlignment.Left, Tag = Coodsz.Ui.UiLanguage.NoTranslate };
        editor.Children.Add(_preferredHostsList);

        var order = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
        order.Children.Add(HostsButton("Move up", "Moves the picked player up the list.", () => MovePreferredHost(-1)));
        order.Children.Add(HostsButton("Move down", "Moves the picked player down the list.", () => MovePreferredHost(1)));
        order.Children.Add(HostsButton("Remove", "Takes the picked player off the list.", RemovePreferredHost));
        editor.Children.Add(order);

        var pick = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        _preferredHostsPick = new ComboBox { Width = 260, HorizontalAlignment = HorizontalAlignment.Left, Tag = Coodsz.Ui.UiLanguage.NoTranslate, ToolTip = Translator.T("Players online now and players in the picked save.") };
        _preferredHostsPick.DropDownOpened += (_, _) => FillHostPick();
        pick.Children.Add(_preferredHostsPick);
        pick.Children.Add(HostsButton("Add player", "Adds the player picked on the left to the end of the list.", AddPickedHost));
        editor.Children.Add(pick);

        var paste = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        _preferredHostsPaste = new TextBox { Width = 260, ToolTip = "Paste Steam IDs, separated by commas or spaces." };
        paste.Children.Add(_preferredHostsPaste);
        paste.Children.Add(HostsButton("Add Steam IDs", "Adds the pasted Steam IDs to the end of the list.", AddPastedHosts));
        editor.Children.Add(paste);
        _preferredHostsPasteRow = paste;
        _preferredHostsError = new TextBlock { Foreground = new SolidColorBrush(Color.FromRgb(0xE2, 0xC2, 0x7A)), TextWrapping = TextWrapping.Wrap };
        editor.Children.Add(_preferredHostsError);
        return grid;
    }

    // Streamer mode: the list shows Player N again, the pick list is built fresh when it opens, and the paste box
    // (pasted Steam IDs would show as typed) is hidden and emptied. Players can still be added from the pick list.
    private void StreamerPreferredHosts(bool on)
    {
        ShowPreferredHosts();
        if (_preferredHostsPick != null)
            _preferredHostsPick.ItemsSource = null;
        if (_preferredHostsPaste != null && on)
            _preferredHostsPaste.Text = "";
        if (_preferredHostsPasteRow != null)
            _preferredHostsPasteRow.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
    }

    private static Button HostsButton(string text, string tip, Action click)
    {
        var button = new Button { Content = text, ToolTip = tip, Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(6, 0, 0, 0) };
        button.Click += (_, _) => click();
        return button;
    }

    // SteamID64 to name: the save's players by Steam name, then the players online now by the name they show.
    private Dictionary<string, string> KnownPlayers()
    {
        var known = new Dictionary<string, string>(StringComparer.Ordinal);
        if (SettingsSaveBox.SelectedItem is string save && save.Length > 0 && ServerSession.SavesPath.Length > 0)
            foreach (var player in KeepRoster.ReadSession(Path.Combine(ServerSession.SavesPath, save + ".json")))
                if (ModSettingsCatalog.IsSteamId(player.ControllerId.Trim()) && player.PlatformName.Length > 0)
                    known[player.ControllerId.Trim()] = player.PlatformName;
        foreach (var player in _livePlayers)
            if (ModSettingsCatalog.IsSteamId(player.SteamId) && player.Name.Length > 0)
                known[player.SteamId] = player.Name;
        return known;
    }

    // Streamer mode shows "Player N" and no SteamID64, like the Players page.
    private string HostLine(string steamId, IReadOnlyDictionary<string, string> known) =>
        known.TryGetValue(steamId, out var name)
            ? StreamerMask.PlayerLine(Streamer ? Translator.T(StreamerNames.Alias(name)) : name, steamId, Streamer)
            : StreamerMask.SteamId64(steamId, Streamer);

    private void ShowPreferredHosts()
    {
        if (_preferredHostsList == null)
            return;
        var known = KnownPlayers();
        var selected = _preferredHostsList.SelectedIndex;
        _preferredHostsList.ItemsSource = _preferredHosts.Select(id => HostLine(id, known)).ToList();
        _preferredHostsList.SelectedIndex = Math.Min(selected, _preferredHosts.Count - 1);
    }

    private void SetPreferredHosts(IEnumerable<string> ids, int select = -1)
    {
        var list = (string[])ModSettingsCatalog.Normalize(ModSettingsCatalog.Find(PreferredHostsId)!, ids.ToArray());
        _preferredHosts.Clear();
        _preferredHosts.AddRange(list);
        ShowPreferredHosts();
        if (_preferredHostsList != null && select >= 0)
            _preferredHostsList.SelectedIndex = Math.Min(select, _preferredHosts.Count - 1);
        RefreshSettingsPulse();
    }

    private void FillHostPick()
    {
        if (_preferredHostsPick == null)
            return;
        var known = KnownPlayers();
        _preferredHostsPick.ItemsSource = known.Keys.Where(id => !_preferredHosts.Contains(id))
            .Select(id => new HostChoice(id, HostLine(id, known))).ToList();
    }

    private void AddPickedHost()
    {
        if (_preferredHostsPick?.SelectedItem is HostChoice choice)
            SetPreferredHosts(_preferredHosts.Append(choice.SteamId), _preferredHosts.Count);
    }

    private void AddPastedHosts()
    {
        if (_preferredHostsPaste == null || _preferredHostsError == null)
            return;
        if (ModSettingsCatalog.ParseText(ModSettingsCatalog.Find(PreferredHostsId)!, _preferredHostsPaste.Text) is not string[] ids)
        {
            _preferredHostsError.Text = "A Steam ID is a 17 digit number.";
            return;
        }

        _preferredHostsError.Text = "";
        _preferredHostsPaste.Text = "";
        SetPreferredHosts(_preferredHosts.Concat(ids), _preferredHosts.Count);
    }

    private void MovePreferredHost(int step)
    {
        var index = _preferredHostsList?.SelectedIndex ?? -1;
        var target = index + step;
        if (index < 0 || target < 0 || target >= _preferredHosts.Count)
            return;
        var list = _preferredHosts.ToList();
        (list[index], list[target]) = (list[target], list[index]);
        SetPreferredHosts(list, target);
    }

    private void RemovePreferredHost()
    {
        var index = _preferredHostsList?.SelectedIndex ?? -1;
        if (index >= 0 && index < _preferredHosts.Count)
            SetPreferredHosts(_preferredHosts.Where((_, i) => i != index), index);
    }

    // The pick list shows a name (or "Player N") and keeps the SteamID64 behind it.
    private sealed record HostChoice(string SteamId, string Shown)
    {
        public override string ToString() => Shown;
    }

    private static string RangeHint(ModSettingDefinition setting) =>
        "From " + Number(setting.Min) + " to " + Number(setting.Max) + ".";

    private static string Number(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    private void MoreCoopSettings_Click(object sender, RoutedEventArgs e)
    {
        var show = MoreCoopSettingsCard.Visibility != Visibility.Visible;
        MoreCoopSettingsCard.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        MoreCoopSettingsButton.Content = show ? "Hide more Coop settings" : "Show more Coop settings";
    }

    // --- filling the page -------------------------------------------------

    /// <summary>
    /// The Coop version of the server this tool starts (its engine\Modules\Coop), or null when it is not known. A key the
    /// settings leave out gets that Coop's default (ModSettingsCatalog.Defaults): Coop 0.1.7 changed some.
    /// </summary>
    private string? ServerCoopVersion()
    {
        try
        {
            var xml = Path.Combine(ServerRoot, "engine", "Modules", "Coop", "SubModule.xml");
            return ServerRoot.Length > 0 && File.Exists(xml) && ModuleIdentityReader.Read(xml).Version is { Length: > 0 } version ? version : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    // A setting's default for this tool's server: what its Coop uses for a missing, empty or garbled value.
    private object DefaultOf(ModSettingDefinition setting) => ModSettingsCatalog.Default(setting, ServerCoopVersion());

    /// <summary>The values to show for this save: its kept settings, or mod-config.json.</summary>
    private Dictionary<string, object> CoopSettingsFor(string? saveName, out bool fromProfile)
    {
        fromProfile = false;
        Dictionary<string, object> file;
        try
        {
            file = new ModConfigStore(ServerSession.ModConfigPath).Read(ServerCoopVersion());
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            // The health check already said what is wrong with the file.
            file = ModSettingsCatalog.Defaults(ServerCoopVersion());
        }

        // A setting an older kept profile doesn't have shows mod-config.json's value, not Coop's default (release-reviewer).
        if (!string.IsNullOrWhiteSpace(saveName) && SaveProfiles.Load(saveName, ServerCoopVersion(), file) is { } kept)
        {
            fromProfile = true;
            return kept;
        }

        return file;
    }

    private void FillCoopSettings(IReadOnlyDictionary<string, object> values)
    {
        _fillingCoopSettings = true;
        try
        {
            foreach (var (box, id) in CatalogueCombos())
                SelectSetting(box, id, values);
            foreach (var (box, id) in CatalogueTexts())
                box.Text = SettingText(id, values);
            foreach (var (id, control) in _moreCoopSettings)
            {
                if (control is ComboBox combo)
                    SelectSetting(combo, id, values);
                else if (control is TextBox text)
                    text.Text = SettingText(id, values);
            }

            SetPreferredHosts(values.TryGetValue(PreferredHostsId, out var hosts) && hosts is string[] ids ? ids : []);
            ShowCoopSettingSupport();

            // The widest range first, so a saved big battle size is not lost before the mod list is checked.
            BattleSizeSlider.Maximum = BigBattles.Max;
            BattleSizeSlider.Value = values.TryGetValue(BattleSizeId, out var battle) && battle is int size
                ? size
                : ModSettingsCatalog.BattleSizeDefault;
            _battleSizeClampedFrom = 0;
            RefreshBattleSizeMode();
            ShowKingdomProtection(values);
        }
        finally
        {
            _fillingCoopSettings = false;
        }
    }

    private void SelectSetting(ComboBox box, string id, IReadOnlyDictionary<string, object> values)
    {
        if (ModSettingsCatalog.Find(id) is not { } setting)
            return;
        var value = values.TryGetValue(id, out var v) ? v : DefaultOf(setting);
        var item = value switch
        {
            bool flag => flag ? TrueText : FalseText,
            string text when ReferenceEquals(box, BattleEconomyBox) => text switch
            {
                "Enabled" => "True",
                "OneDayMax" => "One day max",
                _ => "False"
            },
            string text => text,
            _ => null
        };
        if (item != null && box.Items.Contains(item))
            box.SelectedItem = item;
    }

    private string SettingText(string id, IReadOnlyDictionary<string, object> values)
    {
        if (ModSettingsCatalog.Find(id) is not { } setting)
            return "";
        var value = values.TryGetValue(id, out var v) ? v : DefaultOf(setting);
        return value switch
        {
            int whole => whole.ToString(CultureInfo.InvariantCulture),
            double number => number.ToString("0.0#", CultureInfo.InvariantCulture),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? ""
        };
    }

    // --- reading the page -------------------------------------------------

    /// <summary>Every catalogue setting from the page. Throws with a plain message for a bad number.</summary>
    private Dictionary<string, object> CoopSettingsFromPage()
    {
        var values = ModSettingsCatalog.Defaults(ServerCoopVersion());
        foreach (var (box, id) in CatalogueCombos())
            values[id] = SettingFromCombo(box, id);
        foreach (var (box, id) in CatalogueTexts())
            values[id] = SettingFromText(box, id);
        foreach (var (id, control) in _moreCoopSettings)
            values[id] = control is ComboBox combo ? SettingFromCombo(combo, id) : SettingFromText((TextBox)control, id);
        values[PreferredHostsId] = _preferredHosts.ToArray();
        values[BattleSizeId] = CoopModConfig.ClampBattleSize(BattleSizeSlider.Value, BattleMode);
        values[BannerlordCoopLauncher.Core.Server.ServerRulesFile.CoopOfflineWarOption] =
            BannerlordCoopLauncher.Core.Server.ServerRulesFile.CoopOfflineWarValue(KingdomProtectionFromPage());
        return values;
    }

    private object SettingFromCombo(ComboBox box, string id)
    {
        var setting = ModSettingsCatalog.Find(id)!;
        var item = box.SelectedItem as string;
        if (setting.Kind == ModSettingKind.Bool)
            return item == null ? DefaultOf(setting) : item == TrueText;
        if (ReferenceEquals(box, BattleEconomyBox))
            return item switch
            {
                "True" => "Enabled",
                "One day max" => "OneDayMax",
                _ => "Disabled"
            };
        return ModSettingsCatalog.Normalize(setting, item);
    }

    private object SettingFromText(TextBox box, string id)
    {
        var setting = ModSettingsCatalog.Find(id)!;
        var text = box.Text.Trim();
        if (text.Length == 0)
            return DefaultOf(setting);
        if (ModSettingsCatalog.ParseText(setting, text) is { } value)
            return value;
        throw new InvalidOperationException(setting.Kind == ModSettingKind.Int
            ? setting.Label + " must be a whole number from " + Number(setting.Min) + " to " + Number(setting.Max) + "."
            : setting.Label + " must be a number from " + Number(setting.Min) + " to " + Number(setting.Max) + ".");
    }

    // --- per save settings ------------------------------------------------

    private void SettingsSave_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || _fillingCoopSettings || SettingsSaveBox.SelectedItem is not string name || name.Length == 0)
            return;
        FillCoopSettings(CoopSettingsFor(name, out var fromProfile));
        ShowSaveProfileNote(name, fromProfile);
        RefreshGameSpeed();
        if (!Equals(SaveBox.SelectedItem, name))
            SaveBox.SelectedItem = name;
    }

    private void ShowSaveProfileNote(string? saveName, bool kept)
    {
        if (string.IsNullOrWhiteSpace(saveName))
        {
            SaveProfileNote.Text = "";
            return;
        }

        SaveProfileNote.Text = kept
            ? "The Coop settings below are kept for " + saveName + " and written to mod-config.json each time it starts."
            : "Save settings keeps the Coop settings below for " + saveName + ", and they are written to mod-config.json each time it starts.";
    }

    private void KeepSaveProfile(string? saveName, IReadOnlyDictionary<string, object> values)
    {
        if (string.IsNullOrWhiteSpace(saveName) || !BannerlordCoopLauncher.Core.Saves.SaveSet.IsValidSaveName(saveName))
            return;
        SaveProfiles.Save(saveName, values);
        ShowSaveProfileNote(saveName, kept: true);
    }

    /// <summary>Right before Start: the settings kept for this save go into mod-config.json.
    /// Runs after the health check, which fixes a capital True first.</summary>
    private void ApplySaveProfile(string? saveName)
    {
        if (string.IsNullOrWhiteSpace(saveName) || !SaveProfiles.Exists(saveName))
            return;
        try
        {
            var store = new ModConfigStore(ServerSession.ModConfigPath);
            if (!store.Exists)
                return;
            // A setting an older kept profile doesn't have keeps mod-config.json's own value (release-reviewer): a host's
            // hand set Preferred battle hosts or vote time is never reset to Coop's default on Start server.
            if (SaveProfiles.Load(saveName, ServerCoopVersion(), store.Read(ServerCoopVersion())) is not { } kept)
                return;
            var changed = store.Write(kept, ServerCoopVersion());
            if (changed > 0)
                Append("Used the Coop settings kept for " + saveName + ". " + changed + " settings in mod-config.json were changed to match.");
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            Append("Could not use the Coop settings kept for " + saveName + ". " + ex.Message);
        }
    }

    private static void ForgetSaveProfile(string saveName)
    {
        try
        {
            if (BannerlordCoopLauncher.Core.Saves.SaveSet.IsValidSaveName(saveName))
                SaveProfiles.Delete(saveName);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
