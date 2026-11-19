// Coodsz Server Tools: skills, focus, attributes and unspent points. Ported from CoopKeep
// (https://github.com/Aderimo/bannerlord-coopkeep), src/CoopKeep.ServerTools/HeroAndWorldCommands.cs,
// translated from Turkish and changed for Coodsz Host.
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

using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.ObjectSystem;
using static CoodszServerTools.Text;

namespace CoodszServerTools;

/// <summary>
/// Skills, attributes and focus change through the game's own HeroDeveloper. Coop patches that
/// class and sends the change to the players.
/// </summary>
internal static class DevelopmentCommands
{
    private static IEnumerable<SkillObject> Skills => MBObjectManager.Instance.GetObjectTypeList<SkillObject>();
    private static IEnumerable<CharacterAttribute> Attributes => MBObjectManager.Instance.GetObjectTypeList<CharacterAttribute>();

    private static SkillObject? FindSkill(string id) =>
        Skills.FirstOrDefault(s => string.Equals(s.StringId, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// <c>coodsz.skills &lt;id&gt;</c>: <c>DEV|level|total xp|unspent attribute|unspent focus</c>,
    /// <c>ATTR|id|name|value</c>, <c>SKILL|id|name|level|focus|xp toward next level|attribute id</c>.
    /// </summary>
    public static string SkillsOf(Hero hero)
    {
        var dev = hero.HeroDeveloper;
        var lines = new List<string>
        {
            Line("DEV", I(hero.Level), I(dev.TotalXp), I(dev.UnspentAttributePoints), I(dev.UnspentFocusPoints)),
        };

        foreach (var a in Attributes)
            lines.Add(Line("ATTR", a.StringId, Clean(a.Name?.ToString()), I(hero.GetAttributeValue(a))));

        foreach (var s in Skills)
        {
            int focus = 0, progress = 0;
            try { focus = dev.GetFocus(s); progress = dev.GetSkillXpProgress(s); } catch (Exception) { }
            lines.Add(Line("SKILL", s.StringId, Clean(s.Name?.ToString()), I(hero.GetSkillValue(s)), I(focus), I(progress),
                s.Attributes is { Length: > 0 } at ? at[0].StringId : ""));
        }

        return Lines(lines);
    }

    public static string SkillsCommand(List<string> args)
    {
        if (!TryHero(args, "coodsz.skills <hero_id>", out var hero, out var error)) return error;
        return SkillsOf(hero!);
    }

    /// <summary>
    /// <c>coodsz.skill &lt;id&gt; &lt;skill&gt; &lt;n&gt;</c>: changes the skill level directly (negative lowers it), 0 to 330.
    /// </summary>
    /// <remarks>Skill XP barely moves a high skill, so the level buttons use this.</remarks>
    public static string Skill(List<string> args)
    {
        if (!TryHero(args, "coodsz.skill <hero_id> <skill_id> <count>", out var hero, out var error)) return error;
        var skill = args.Count > 1 ? FindSkill(args[1]) : null;
        if (skill is null) return "COODSZ_ERROR skill '" + Clean(args.Count > 1 ? args[1] : "") + "' not found.";
        if (!TryInt(args, 2, -330, 330, out var n) || n == 0) return "COODSZ_ERROR count must be between -330 and 330, not 0";

        var before = hero!.GetSkillValue(skill);
        var target = Clamp(before + n, 0, 330);
        // ChangeSkillLevel raises the game's events but ignores going down, so that is written directly.
        if (target > before) hero.HeroDeveloper.ChangeSkillLevel(skill, target - before, false);
        if (hero.GetSkillValue(skill) != target) hero.SetSkillValue(skill, target);
        return Set(hero, "skill:" + skill.StringId, before, hero.GetSkillValue(skill));
    }

    /// <summary><c>coodsz.skill_xp &lt;id&gt; &lt;skill&gt; &lt;xp&gt;</c>, with the game's own learning rate.</summary>
    public static string SkillXp(List<string> args)
    {
        if (!TryHero(args, "coodsz.skill_xp <hero_id> <skill_id> <xp>", out var hero, out var error)) return error;
        var skill = args.Count > 1 ? FindSkill(args[1]) : null;
        if (skill is null) return "COODSZ_ERROR skill '" + Clean(args.Count > 1 ? args[1] : "") + "' not found.";
        if (!TryInt(args, 2, 1, 1_000_000, out var xp)) return "COODSZ_ERROR xp must be between 1 and 1000000";

        var before = hero!.GetSkillValue(skill);
        // A host edit reaches an offline player too (the offline XP freeze lets it through).
        ServerRules.Allow(() => { hero.HeroDeveloper.AddSkillXp(skill, xp, false, true); return true; });
        return Set(hero, "skill_xp:" + skill.StringId, before, hero.GetSkillValue(skill));
    }

    /// <summary><c>coodsz.focus &lt;id&gt; &lt;skill&gt; &lt;n&gt;</c> (negative takes focus back).</summary>
    public static string Focus(List<string> args)
    {
        if (!TryHero(args, "coodsz.focus <hero_id> <skill_id> <count>", out var hero, out var error)) return error;
        var skill = args.Count > 1 ? FindSkill(args[1]) : null;
        if (skill is null) return "COODSZ_ERROR skill '" + Clean(args.Count > 1 ? args[1] : "") + "' not found.";
        if (!TryInt(args, 2, -10, 10, out var n) || n == 0) return "COODSZ_ERROR count must be between -10 and 10, not 0";

        var dev = hero!.HeroDeveloper;
        var before = dev.GetFocus(skill);
        var max = Campaign.Current.Models.CharacterDevelopmentModel.MaxFocusPerSkill;
        var target = Clamp(before + n, 0, max);
        if (target != before) dev.AddFocus(skill, target - before, false);
        return Set(hero, "focus:" + skill.StringId, before, dev.GetFocus(skill));
    }

    /// <summary><c>coodsz.attribute &lt;id&gt; &lt;attribute&gt; &lt;n&gt;</c> (negative takes points back), 1 to 10.</summary>
    public static string Attribute(List<string> args)
    {
        if (!TryHero(args, "coodsz.attribute <hero_id> <attribute_id> <count>", out var hero, out var error)) return error;
        var attr = args.Count > 1 ? Attributes.FirstOrDefault(a => string.Equals(a.StringId, args[1], StringComparison.OrdinalIgnoreCase)) : null;
        if (attr is null) return "COODSZ_ERROR attribute '" + Clean(args.Count > 1 ? args[1] : "") + "' not found.";
        if (!TryInt(args, 2, -10, 10, out var n) || n == 0) return "COODSZ_ERROR count must be between -10 and 10, not 0";

        var before = hero!.GetAttributeValue(attr);
        var target = Clamp(before + n, 1, 10);
        if (target != before) hero.HeroDeveloper.AddAttribute(attr, target - before, false);
        return Set(hero, "attribute:" + attr.StringId, before, hero.GetAttributeValue(attr));
    }

    /// <summary><c>coodsz.unspent &lt;id&gt; &lt;attribute|focus&gt; &lt;n&gt;</c>: gives or takes unspent points.</summary>
    public static string Unspent(List<string> args)
    {
        if (!TryHero(args, "coodsz.unspent <hero_id> <attribute|focus> <count>", out var hero, out var error)) return error;
        if (!TryInt(args, 2, -50, 50, out var n) || n == 0) return "COODSZ_ERROR count must be between -50 and 50, not 0";

        var dev = hero!.HeroDeveloper;
        switch (args.Count > 1 ? args[1] : "")
        {
            case "attribute":
            {
                var before = dev.UnspentAttributePoints;
                dev.UnspentAttributePoints = Math.Max(0, before + n);
                return Set(hero, "unspent:attribute", before, dev.UnspentAttributePoints);
            }
            case "focus":
            {
                var before = dev.UnspentFocusPoints;
                dev.UnspentFocusPoints = Math.Max(0, before + n);
                return Set(hero, "unspent:focus", before, dev.UnspentFocusPoints);
            }
            default:
                return "COODSZ_ERROR kind must be attribute or focus";
        }
    }
}
