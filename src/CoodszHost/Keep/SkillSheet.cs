// The skill sheet follows CoopKeep's SkillSheet (https://github.com/Aderimo/bannerlord-coopkeep,
// src/CoopKeep.App/ViewModels/SkillSheet.cs), ported from Avalonia MVVM to WPF and changed for Coodsz Host.
//
// Copyright (c) 2026 aderimo
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

using System.Globalization;
using System.Windows.Media;
using BannerlordCoopLauncher.Core.Keep;
using BannerlordCoopLauncher.Core.Localization;

namespace CoodszHost.Keep;

/// <summary>
/// The game's character screen in Coodsz Host: 6 attributes, 18 skills, focus points, level and
/// XP. A clicked skill gets level, focus and XP buttons; a clicked attribute gets points.
/// </summary>
/// <remarks>
/// Values come from the server (<c>coodsz.skills</c>) and are read again after every change, so
/// the numbers on screen are what the server holds. Skill XP uses the game's own learning rate
/// and barely moves a high skill, so the level buttons change the level directly. The same sheet
/// is used in the player profile and on the Cheats page Character tab.
/// </remarks>
public sealed class SkillSheet : Observable
{
    private static readonly Brush[] Tints =
    {
        new SolidColorBrush(Color.FromRgb(0x8E, 0x3B, 0x2E)), // vigor
        new SolidColorBrush(Color.FromRgb(0x3B, 0x6E, 0x3E)), // control
        new SolidColorBrush(Color.FromRgb(0x7A, 0x5A, 0x2A)), // endurance
        new SolidColorBrush(Color.FromRgb(0x5A, 0x3F, 0x7A)), // cunning
        new SolidColorBrush(Color.FromRgb(0x2E, 0x5A, 0x86)), // social
        new SolidColorBrush(Color.FromRgb(0x2E, 0x6E, 0x6E)), // intelligence
    };

    private readonly Func<KeepTarget, string, string, Func<ToolsChange, string?>?, Task<bool>> _run;
    private KeepTarget? _hero;
    private HeroDevelopment? _stats;
    private bool _loading;
    private bool _busy;
    private string _error = "";
    private SkillCell? _selectedSkill;
    private AttributeColumn? _selectedAttribute;
    private string _xpAmount = "1000";

    /// <param name="run">
    /// Sends a change for the hero and writes it to the admin log: (hero, command, log text, the
    /// command that undoes a confirmed change) to whether the server confirmed it.
    /// </param>
    public SkillSheet(Func<KeepTarget, string, string, Func<ToolsChange, string?>?, Task<bool>> run)
    {
        _run = run;
        Attributes = SkillLayout.Columns.Select((c, i) =>
        {
            foreach (var tint in Tints)
                tint.Freeze();
            return new AttributeColumn(c.Attribute, c.Short, c.Name, c.Skills.Select(s => new SkillCell(s, Tints[i])).ToList());
        }).ToList();
    }

    public IReadOnlyList<AttributeColumn> Attributes { get; }

    private IEnumerable<SkillCell> AllSkills => Attributes.SelectMany(a => a.Skills);

    public KeepTarget? Hero
    {
        get => _hero;
        set
        {
            if (ReferenceEquals(_hero, value))
                return;
            _hero = value;
            Stats = null;
            Error = "";
            SelectedSkill = null;
            SelectedAttribute = null;
            OnChanged();
        }
    }

    public HeroDevelopment? Stats
    {
        get => _stats;
        private set
        {
            _stats = value;
            foreach (var name in new[] { nameof(Stats), nameof(HasStats), nameof(LevelText), nameof(TotalXpText), nameof(UnspentAttributeText),
                         nameof(UnspentFocusText), nameof(SelectionText) })
                OnChanged(name);
        }
    }

    public bool Loading { get => _loading; private set => Set(ref _loading, value); }
    public bool Busy { get => _busy; private set => Set(ref _busy, value); }

    public string Error
    {
        get => _error;
        private set
        {
            if (Set(ref _error, value))
                OnChanged(nameof(HasError));
        }
    }

    public string XpAmount { get => _xpAmount; set => Set(ref _xpAmount, value); }

    public bool HasStats => _stats != null;
    public bool HasError => _error.Length > 0;
    public bool HasSelectedSkill => _selectedSkill != null;
    public bool HasSelectedAttribute => _selectedAttribute != null;
    public bool HasSelection => HasSelectedSkill || HasSelectedAttribute;

    public string LevelText => _stats is { } s ? s.Level.ToString(CultureInfo.CurrentCulture) : "—";
    public string TotalXpText => _stats is { } s ? KeepFormat.N(s.TotalXp) : "—";
    public string UnspentAttributeText => _stats?.UnspentAttributePoints.ToString(CultureInfo.CurrentCulture) ?? "—";
    public string UnspentFocusText => _stats?.UnspentFocusPoints.ToString(CultureInfo.CurrentCulture) ?? "—";

    /// <summary>"Leadership · level 209 · focus 2/5 · stored XP 5,000" (English; the window translates it).</summary>
    public string SelectionText => _selectedSkill is { } k
        ? SkillLayout.SkillName(k.Id) + " · level " + k.Value + " · focus " + k.Focus + "/5 · stored XP " + KeepFormat.N(k.XpProgress)
        : _selectedAttribute is { } a
            ? a.EnglishName + " · " + a.Value + " points"
            : "Click a skill or an attribute.";

    public SkillCell? SelectedSkill
    {
        get => _selectedSkill;
        private set
        {
            _selectedSkill = value;
            foreach (var s in AllSkills)
                s.IsSelected = ReferenceEquals(s, value);
            OnChanged();
            OnChanged(nameof(HasSelectedSkill));
            OnChanged(nameof(HasSelection));
            OnChanged(nameof(SelectionText));
        }
    }

    public AttributeColumn? SelectedAttribute
    {
        get => _selectedAttribute;
        private set
        {
            _selectedAttribute = value;
            foreach (var a in Attributes)
                a.IsSelected = ReferenceEquals(a, value);
            OnChanged();
            OnChanged(nameof(HasSelectedAttribute));
            OnChanged(nameof(HasSelection));
            OnChanged(nameof(SelectionText));
        }
    }

    public void SelectSkill(SkillCell skill)
    {
        SelectedAttribute = null;
        SelectedSkill = ReferenceEquals(SelectedSkill, skill) ? null : skill;
    }

    public void SelectAttribute(AttributeColumn attribute)
    {
        SelectedSkill = null;
        SelectedAttribute = ReferenceEquals(SelectedAttribute, attribute) ? null : attribute;
    }

    /// <summary>Reads the hero's development from the server.</summary>
    public async Task LoadAsync()
    {
        if (Hero is not { HasHero: true } hero || KeepContext.Current is not { } keep)
            return;

        Loading = true;
        try
        {
            var stats = await keep.Client.DevelopmentAsync(hero.HeroId);
            if (!ReferenceEquals(Hero, hero))
                return;
            if (stats == null)
            {
                Error = keep.Client.ToolsReady == false ? KeepClient.NoTools : "The server did not send the skills. Try Refresh.";
                return;
            }

            Apply(stats);
            Error = "";
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            Error = ex.Message;
        }
        finally
        {
            Loading = false;
        }
    }

    private void Apply(HeroDevelopment stats)
    {
        foreach (var a in Attributes)
            a.Value = stats.Attributes.FirstOrDefault(x => x.Id.Equals(a.Id, StringComparison.OrdinalIgnoreCase))?.Value ?? 0;

        foreach (var s in AllSkills)
        {
            var src = stats.Skills.FirstOrDefault(x => x.Id.Equals(s.Id, StringComparison.OrdinalIgnoreCase));
            s.Value = src?.Value ?? 0;
            s.Focus = src?.Focus ?? 0;
            s.XpProgress = src?.XpProgress ?? 0;
        }

        Stats = stats;
    }

    public Task ChangeSkill(int levels) =>
        SelectedSkill is { } s
            ? ApplyAsync(h => ToolsCommand.Skill(h.HeroId, s.Id, levels), SkillLayout.SkillName(s.Id) + " level " + SkillLayout.Signed(levels),
                c => c.Changed ? ToolsCommand.Skill(c.HeroId, s.Id, c.Old - c.New) : null)
            : Task.CompletedTask;

    public Task ChangeFocus(int points) =>
        SelectedSkill is { } s
            ? ApplyAsync(h => ToolsCommand.Focus(h.HeroId, s.Id, points), SkillLayout.SkillName(s.Id) + " focus " + SkillLayout.Signed(points),
                c => c.Changed ? ToolsCommand.Focus(c.HeroId, s.Id, c.Old - c.New) : null)
            : Task.CompletedTask;

    public Task GiveXp()
    {
        var xp = AmountOf(XpAmount);
        if (SelectedSkill is not { } s || xp <= 0)
        {
            Error = "Type the skill XP to give, a number above 0.";
            return Task.CompletedTask;
        }

        return ApplyAsync(h => ToolsCommand.SkillXp(h.HeroId, s.Id, xp), SkillLayout.SkillName(s.Id) + " XP +" + KeepFormat.N(xp), null);
    }

    public Task ChangeAttribute(int points) =>
        SelectedAttribute is { } a
            ? ApplyAsync(h => ToolsCommand.Attribute(h.HeroId, a.Id, points), a.EnglishName + " " + SkillLayout.Signed(points),
                c => c.Changed ? ToolsCommand.Attribute(c.HeroId, a.Id, c.Old - c.New) : null)
            : Task.CompletedTask;

    public Task ChangeUnspent(bool attribute, int points) =>
        ApplyAsync(h => ToolsCommand.Unspent(h.HeroId, attribute, points),
            attribute ? "unspent attribute points " + SkillLayout.Signed(points) : "unspent focus points " + SkillLayout.Signed(points),
            c => c.Changed ? ToolsCommand.Unspent(c.HeroId, attribute, c.Old - c.New) : null);

    private async Task ApplyAsync(Func<KeepTarget, string> command, string audit, Func<ToolsChange, string?>? undo)
    {
        if (Hero is not { HasHero: true } hero || Busy)
            return;

        Busy = true;
        try
        {
            string text;
            try
            {
                text = command(hero);
            }
            catch (ArgumentException ex)
            {
                Error = ex.Message;
                return;
            }

            if (await _run(hero, text, audit, undo))
                await LoadAsync();
        }
        finally
        {
            Busy = false;
        }
    }

    public static int AmountOf(string? text)
    {
        var clean = (text ?? "").Replace(",", "").Replace(".", "").Replace(" ", "").Replace(" ", "").Trim();
        return int.TryParse(clean, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 0;
    }

    /// <summary>The language changed: skill and attribute names follow.</summary>
    public void Rename()
    {
        foreach (var s in AllSkills)
            s.Rename();
        OnChanged(nameof(SelectionText));
    }
}
