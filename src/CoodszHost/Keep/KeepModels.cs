using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using BannerlordCoopLauncher.Core.Keep;
using BannerlordCoopLauncher.Core.Localization;

namespace CoodszHost.Keep;

public abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;
        field = value;
        OnChanged(name);
        return true;
    }
}

internal static class KeepFormat
{
    public static string N(int value) => value.ToString("N0", CultureInfo.CurrentCulture);
    public static string N(double value) => ((int)Math.Round(value)).ToString("N0", CultureInfo.CurrentCulture);
}

/// <summary>A row of the Who? panel: a hero the host can act on, with its last read stats.</summary>
public sealed class KeepTarget : Observable
{
    private bool _isTarget;
    private bool _isFocused;
    private HeroProfile? _profile;

    public KeepTarget(KeepPlayer player) => Player = player;

    public KeepPlayer Player { get; private set; }
    public string Name => Player.Name;
    public string HeroId => Player.HeroId;
    public bool HasHero => Player.HeroId.Length > 0;
    public string Initial => Player.Initial;

    /// <summary>What the screen shows: the name, or "Player 1" in streamer mode (numbered like the other pages).</summary>
    public string Shown => KeepContext.StreamerMode && Player.Name.Length > 0 ? Translator.T(StreamerNames.Alias(Player.Name)) : Player.Name;
    public string ShownInitial => KeepContext.StreamerMode ? (Shown.Length > 0 ? Shown[..1].ToUpperInvariant() : "?") : Initial;

    /// <summary>The small line under the name: the Steam name (hidden in streamer mode) and the hero id.</summary>
    public string Detail => Player.Detail(KeepContext.StreamerMode);
    public bool HasDetail => Detail.Length > 0;

    /// <summary>Streamer mode was switched: the shown name changes.</summary>
    public void StreamerChanged()
    {
        OnChanged(nameof(Shown));
        OnChanged(nameof(ShownInitial));
        OnChanged(nameof(Detail));
        OnChanged(nameof(HasDetail));
    }

    public bool Online => Player.Online;
    public string SteamId => Player.SteamId;

    public bool IsTarget { get => _isTarget; set => Set(ref _isTarget, value); }
    public bool IsFocused { get => _isFocused; set => Set(ref _isFocused, value); }

    /// <summary>The last <c>coodsz.hero</c> answer.</summary>
    public HeroProfile? Profile
    {
        get => _profile;
        set
        {
            _profile = value;
            OnChanged();
            Notify();
        }
    }

    public void Update(KeepPlayer player)
    {
        Player = player;
        OnChanged(nameof(Player));
        Notify();
    }

    private void Notify()
    {
        foreach (var name in new[] { nameof(Name), nameof(Shown), nameof(ShownInitial), nameof(Detail), nameof(HasDetail), nameof(HasHero), nameof(Online), nameof(GoldText), nameof(HealthText), nameof(HealthShort),
                     nameof(PlaceLine), nameof(HealthRatio), nameof(TroopsText), nameof(InfluenceText), nameof(RenownText), nameof(Gold), nameof(HitPoints), nameof(AgeText), nameof(HasAge) })
            OnChanged(name);
    }

    public int? Gold => _profile?.Gold ?? Player.Hero?.Gold;
    public int? HitPoints => _profile?.HitPoints ?? Player.Hero?.HitPoints;
    public int? MaxHitPoints => _profile?.MaxHitPoints ?? Player.Hero?.MaxHitPoints;

    public string GoldText => Gold is { } g ? KeepFormat.N(g) : "—";
    public string HealthShort => HitPoints is { } h ? KeepFormat.N(h) : "—";
    public string HealthText => HitPoints is { } h && MaxHitPoints is { } m ? h + " / " + m : "—";
    public double HealthRatio => HitPoints is { } h && MaxHitPoints is { } m && m > 0 ? Math.Clamp(h / (double)m, 0, 1) : 0;
    public string TroopsText => _profile is { } p ? KeepFormat.N(p.Troops) : "—";
    public string InfluenceText => _profile is { } p ? KeepFormat.N(p.Influence) : "—";
    public string RenownText => _profile is { } p ? KeepFormat.N(p.Renown) : "—";

    /// <summary>"Age 31" on the Cheats page player cards, when the server said it.</summary>
    public string AgeText => _profile is { Age: > 0 } p ? "Age " + ((int)p.Age).ToString(CultureInfo.InvariantCulture) : "";
    public bool HasAge => AgeText.Length > 0;

    /// <summary>"Osticos · Northern Empire", the clan and kingdom (or where the hero is).</summary>
    public string PlaceLine
    {
        get
        {
            var clan = _profile?.ClanName ?? Player.Hero?.ClanName ?? "";
            var kingdom = _profile?.KingdomName ?? Player.Hero?.KingdomName ?? "";
            var parts = new[] { clan, kingdom }.Where(p => p.Length > 0).ToList();
            if (parts.Count == 0 && (_profile?.PlaceName ?? Player.Hero?.Place ?? "") is { Length: > 0 } place)
                parts.Add(place);
            return string.Join(" · ", parts);
        }
    }
}

/// <summary>One skill on the character sheet: icon, level, focus dots and progress bar.</summary>
public sealed class SkillCell : Observable
{
    private int _value;
    private int _focus;
    private int _xpProgress;
    private bool _isSelected;

    public SkillCell(string id, Brush tint)
    {
        Id = id;
        Tint = tint;
        Icon = GameImages.Get(GameImageKind.Skill, id);
    }

    public string Id { get; }
    public string Name => Translator.T(SkillLayout.SkillName(Id));
    public string Monogram => SkillLayout.Monogram(Id);
    public Brush Tint { get; }
    public GameImage Icon { get; }

    public int Value { get => _value; set { if (Set(ref _value, value)) OnChanged(nameof(BarWidth)); } }
    public int Focus { get => _focus; set { if (Set(ref _focus, value)) OnChanged(nameof(FocusDots)); } }
    public int XpProgress { get => _xpProgress; set => Set(ref _xpProgress, value); }
    public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }

    public IReadOnlyList<bool> FocusDots => Enumerable.Range(0, SkillLayout.MaxFocus).Select(i => i < Focus).ToList();

    /// <summary>The progress bar: level out of 330, on a 120 pixel track.</summary>
    public double BarWidth => Math.Clamp(Value / (double)SkillLayout.MaxSkill, 0, 1) * 120;

    public void Rename() => OnChanged(nameof(Name));
}

/// <summary>An attribute box (VIG 4) and the three skills under it.</summary>
public sealed class AttributeColumn : Observable
{
    private int _value;
    private bool _isSelected;

    public AttributeColumn(string id, string shortName, string name, IReadOnlyList<SkillCell> skills)
    {
        Id = id;
        Short = shortName;
        EnglishName = name;
        Skills = skills;
    }

    public string Id { get; }
    public string Short { get; }
    public string EnglishName { get; }
    public string Name => Translator.T(EnglishName);
    public IReadOnlyList<SkillCell> Skills { get; }
    public int Value { get => _value; set => Set(ref _value, value); }
    public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }
}

/// <summary>A worn item, with its slot.</summary>
public sealed class EquipmentCard
{
    public EquipmentCard(EquipmentPiece piece)
    {
        Piece = piece;
        Image = GameImages.Get(GameImageKind.Item, piece.ItemId);
    }

    public EquipmentPiece Piece { get; }
    public string Name => Piece.Name;
    public GameImage Image { get; }

    public string SlotName => Piece.Slot.StartsWith("Weapon", StringComparison.Ordinal) && Piece.Slot.Length == 7 && char.IsDigit(Piece.Slot[6])
        ? Translator.T("Weapon") + " " + (Piece.Slot[6] - '0' + 1)
        : Translator.T(Piece.Slot switch
    {
        "Banner" => "Banner",
        "Head" => "Head",
        "Body" => "Body",
        "Leg" => "Legs",
        "Gloves" => "Hands",
        "Cape" => "Shoulders",
        "Horse" => "Horse",
        "HorseHarness" => "Harness",
        _ => Piece.Slot,
    });
}

/// <summary>A troop kind in a party, with its count badge.</summary>
public sealed class ArmyCard
{
    public ArmyCard(RosterTroop troop)
    {
        Troop = troop;
        Image = GameImages.Get(GameImageKind.Troop, troop.Id);
    }

    public RosterTroop Troop { get; }
    public string Name => Troop.Name;
    public GameImage Image { get; }
    public string Stars => TroopCatalog.Stars(Troop.Tier);
    public string CountText => Troop.Wounded > 0 ? KeepFormat.N(Troop.Number) + " (" + Troop.Wounded + "✚)" : KeepFormat.N(Troop.Number);
}

/// <summary>An inventory line, with its count badge.</summary>
public sealed class InventoryCard
{
    public InventoryCard(InventoryItem item)
    {
        Item = item;
        Image = GameImages.Get(GameImageKind.Item, item.Id);
    }

    public InventoryItem Item { get; }
    public string Name => Item.Name;
    public GameImage Image { get; }
    public string CountText => "×" + KeepFormat.N(Item.Count);
}

/// <summary>A troop kind in the Army tab picker.</summary>
public sealed class TroopCard
{
    public TroopCard(TroopType troop)
    {
        Troop = troop;
        Image = GameImages.Get(GameImageKind.Troop, troop.Id);
    }

    public TroopType Troop { get; }
    public string Name => Troop.Name;
    public string Stars => TroopCatalog.Stars(Troop.Tier);
    public string ClassName => Translator.T(TroopCatalog.ClassName(Troop.Class));
    public string CultureName => Troop.CultureName;
    public GameImage Image { get; }
}

/// <summary>An item card of the Items window.</summary>
public sealed class ItemCard
{
    public ItemCard(GameItem item)
    {
        Item = item;
        Image = GameImages.Get(GameImageKind.Item, item.Id);
    }

    public GameItem Item { get; }
    public string Name => Item.Name;
    public string Culture => Translator.T(ItemCatalog.CultureLabel(Item));
    public string ValueText => KeepFormat.N(Item.Value);
    public bool IsCivilian => Item.Civilian;
    public string CivilianText => Translator.T("Civilian");
    public string Stars => Item.Tier > 0 ? TroopCatalog.Stars(Item.Tier) : "";
    public GameImage Image { get; }
}

/// <summary>A line of the Items window basket (give) or of the target's inventory (take).</summary>
public sealed class BasketLine : Observable
{
    private int _count;

    public BasketLine(ItemCard card, int count, int have = 0)
    {
        Card = card;
        _count = count;
        Have = have;
    }

    public ItemCard Card { get; }
    public int Have { get; }

    public int Count
    {
        get => _count;
        set
        {
            if (Set(ref _count, Math.Max(0, value)))
                OnChanged(nameof(CountText));
        }
    }

    public string CountText => Count + "×";
}

/// <summary>A category of the Items window sidebar.</summary>
public sealed class CategoryRow
{
    public CategoryRow(ItemGroup? group, int count)
    {
        Group = group;
        Count = count;
    }

    public ItemGroup? Group { get; }
    public int Count { get; }
    public string Name => Translator.T(Group is { } g ? ItemCatalog.GroupName(g) : "All items");
}
