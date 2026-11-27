// The player profile follows CoopKeep's PlayerProfile view and its view model
// (https://github.com/Aderimo/bannerlord-coopkeep, src/CoopKeep.App/Views/PlayerProfile.axaml and
// ViewModels/MainViewModel.Profile.cs), ported from Avalonia to WPF and changed for Coodsz Host.

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using BannerlordCoopLauncher.Core.Keep;
using BannerlordCoopLauncher.Core.Localization;

namespace CoodszHost.Keep;

/// <summary>
/// The player profile, opened from the Players box and the Cheats page. The header and stat tiles
/// come from <c>coodsz.hero</c>; skills, equipment, inventory and army from the other coodsz.*
/// reads. With the server stopped or without the module it says why instead.
/// </summary>
public partial class PlayerProfileWindow : Window, INotifyPropertyChanged
{
    private readonly KeepContext _keep;
    private bool _loading;
    private string _note = "";
    private string _status = "";

    public PlayerProfileWindow(KeepContext keep, KeepTarget target)
    {
        _keep = keep;
        Target = target;
        Sheet = new SkillSheet((hero, command, audit, undo) => RunAsync(hero, command, audit, undo));
        Sheet.Hero = target;
        InitializeComponent();
        Coodsz.Ui.WindowPlacement.KeepOnScreen(this);
        Owner = keep.Owner;
        DataContext = this;
        Target.PropertyChanged += (_, _) => NotifyStats();
        _keep.Status += OnStatus;
        Closed += (_, _) => _keep.Status -= OnStatus;
        Loaded += async (_, _) => await LoadAsync();
    }

    public KeepTarget Target { get; }
    public SkillSheet Sheet { get; }
    public ObservableCollection<EquipmentCard> BattleGear { get; } = new();
    public ObservableCollection<EquipmentCard> CivilianGear { get; } = new();
    public ObservableCollection<InventoryCard> Inventory { get; } = new();
    public ObservableCollection<ArmyCard> Army { get; } = new();
    public ObservableCollection<ArmyCard> Prisoners { get; } = new();

    public bool Loading { get => _loading; private set { _loading = value; OnChanged(); } }
    public string Note { get => _note; private set { _note = value; OnChanged(); OnChanged(nameof(HasNote)); } }
    public bool HasNote => _note.Length > 0;
    public string Status { get => _status; private set { _status = value; OnChanged(); } }

    public bool HasGear => BattleGear.Count + CivilianGear.Count > 0;
    public bool NoGear => !HasGear && !Loading;
    public bool HasPrisoners => Prisoners.Count > 0;

    /// <summary>"Osticos · Northern Empire · Steam ••••1234 · Argoron".</summary>
    public string SubLine
    {
        get
        {
            var parts = new List<string>();
            if (Target.PlaceLine.Length > 0)
                parts.Add(Target.PlaceLine);
            if (StreamerMask.SteamLabel(Target.SteamId, KeepContext.StreamerMode) is { Length: > 0 } steam)
                parts.Add(steam);
            // The Steam name and the hero id ("Rowan · Player2863"), so it is clear who this is.
            if (Target.HasDetail)
                parts.Add(Target.Detail);
            if (Target.Profile is { PlaceName.Length: > 0 } p && !Target.PlaceLine.Contains(p.PlaceName, StringComparison.Ordinal))
                parts.Add(p.PlaceName);
            return string.Join("  ·  ", parts);
        }
    }

    public string LevelText => Target.Profile is { } p ? p.Level.ToString(CultureInfo.CurrentCulture) : Sheet.LevelText;
    public string GoldText => Target.GoldText;
    public string HealthText => Target.HealthText;
    public string AgeText => Target.Profile is { } p ? ((int)p.Age).ToString(CultureInfo.CurrentCulture) : "—";
    public string InfluenceText => Target.InfluenceText;
    public string RenownText => Target.RenownText;
    public string TroopsText => Target.TroopsText;

    /// <summary>"200 troops · 0 wounded" (English; the window translates it).</summary>
    public string ArmyLine => Army.Count == 0 && !Loading
        ? "No troops were read. The hero may have no party right now."
        : TroopCatalog.ArmyLine(Army.Sum(a => a.Troop.Number), Army.Sum(a => a.Troop.Wounded));

    public string InventoryLine => Inventory.Count == 0 && !Loading
        ? "The inventory is empty, or it was not read."
        : Inventory.Sum(i => i.Item.Count) + " items worth " + KeepFormat.N((double)Inventory.Sum(i => (long)i.Item.Value * i.Item.Count)) + " gold";

    private void NotifyStats()
    {
        foreach (var name in new[] { nameof(SubLine), nameof(LevelText), nameof(GoldText), nameof(HealthText), nameof(AgeText), nameof(InfluenceText),
                     nameof(RenownText), nameof(TroopsText) })
            OnChanged(name);
    }

    private void NotifyLists()
    {
        foreach (var name in new[] { nameof(HasGear), nameof(NoGear), nameof(HasPrisoners), nameof(ArmyLine), nameof(InventoryLine) })
            OnChanged(name);
    }

    private void OnStatus(string message, bool ok) => Status = message;

    private async Task<bool> RunAsync(KeepTarget hero, string command, string audit, Func<ToolsChange, string?>? undo)
    {
        var result = await _keep.ChangeAsync(hero, command, audit, undo: undo);
        if (result.Ok)
            await RefreshHeaderAsync();
        return result.Ok;
    }

    private async Task RefreshHeaderAsync()
    {
        if (!Target.HasHero || !_keep.CanSend)
            return;
        if (await _keep.Client.ProfileAsync(Target.HeroId) is { } profile)
            Target.Profile = profile;
    }

    private async Task LoadAsync()
    {
        BattleGear.Clear();
        CivilianGear.Clear();
        Inventory.Clear();
        Army.Clear();
        Prisoners.Clear();
        NotifyLists();

        if (!Target.HasHero)
        {
            Note = Target.Shown + " is connected, but the server has not shown which hero is theirs yet. Wait until they are on the map, then press Refresh.";
            return;
        }

        var blocked = _keep.BlockReason();
        if (blocked.Length > 0)
        {
            Note = blocked;
            return;
        }

        Loading = true;
        try
        {
            if (_keep.Client.ToolsReady != true && !await _keep.Client.CheckToolsAsync())
            {
                Note = KeepClient.NoTools;
                return;
            }

            Note = "";
            var profile = await _keep.Client.ProfileAsync(Target.HeroId);
            if (profile == null)
            {
                Note = "The server could not find this hero. It may have died or left the campaign.";
                return;
            }

            Target.Profile = profile;
            await Sheet.LoadAsync();

            foreach (var piece in await _keep.Client.EquipmentAsync(Target.HeroId))
                (piece.Set == "civilian" ? CivilianGear : BattleGear).Add(new EquipmentCard(piece));

            if (await _keep.Client.RosterAsync(Target.HeroId) is { } roster)
            {
                foreach (var troop in roster.Members.Where(m => !m.IsHero).OrderByDescending(m => m.Tier).ThenByDescending(m => m.Number))
                    Army.Add(new ArmyCard(troop));
                foreach (var troop in roster.Prisoners)
                    Prisoners.Add(new ArmyCard(troop));
            }

            foreach (var item in (await _keep.Client.InventoryAsync(Target.HeroId)).OrderByDescending(i => (long)i.Value * i.Count))
                Inventory.Add(new InventoryCard(item));
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            Note = ex.Message;
        }
        finally
        {
            Loading = false;
            NotifyLists();
            NotifyStats();
        }
    }

    private void Tab_Checked(object sender, RoutedEventArgs e)
    {
        if (SkillsPanel == null)
            return;
        SkillsPanel.Visibility = TabSkills.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        GearPanel.Visibility = TabGear.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        InventoryPanel.Visibility = TabInventory.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        ArmyPanel.Visibility = TabArmy.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Header_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed && e.OriginalSource is not Button)
            DragMove();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await LoadAsync();

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void OpenCheats_Click(object sender, RoutedEventArgs e)
    {
        _keep.OpenInCheats?.Invoke(Target);
        Close();
    }

    private void Items_Click(object sender, RoutedEventArgs e)
    {
        if (!Target.HasHero)
        {
            Status = Note.Length > 0 ? Note : "No hero is known for this player yet.";
            return;
        }

        new ItemsWindow(_keep, new[] { Target }).Show();
    }

    /// <summary>Show on map: the Map page, centred on this player's party.</summary>
    private void ShowOnMap_Click(object sender, RoutedEventArgs e)
    {
        if (_keep.ShowOnMap == null)
            return;
        if (!Target.HasHero)
        {
            Status = Note.Length > 0 ? Note : "No hero is known for this player yet.";
            return;
        }

        _keep.ShowOnMap(Target.HeroId, Target.Name);
        Owner?.Activate();
    }

    private void Ban_Click(object sender, RoutedEventArgs e)
    {
        if (_keep.Ban == null)
            return;
        if (Target.SteamId.Length == 0)
        {
            Status = Target.Shown + " has no SteamID64 yet, so the ban list cannot store them.";
            return;
        }

        var answer = MessageBox.Show(this, Translator.T("Ban " + Target.Shown + "? They cannot join again until you unban them."),
            Translator.T("Ban player"), MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (answer == MessageBoxResult.Yes)
            Status = _keep.Ban(Target.SteamId, Target.Name);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
