// Coodsz Server Tools. The command registration, the item commands and the hero development
// commands are ported from CoopKeep (https://github.com/Aderimo/bannerlord-coopkeep),
// src/CoopKeep.ServerTools, translated from Turkish and changed for Coodsz Host (coodsz.* names,
// one hero profile command, gold, influence, renown, health, age, troop and catalog commands).
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
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace CoodszServerTools;

/// <summary>
/// Adds the coodsz.* console commands to the dedicated server.
/// </summary>
/// <remarks>
/// <para>
/// Coodsz Host sends them through server-data\console-queue (DedicatedServer.Windows' Launcher
/// Cheats runs any group.name command for the host). Players cannot run them. Every command
/// answers on its own lines, fields split by <c>|</c>, and ends with <c>COODSZ_END</c>. A change
/// answers <c>COODSZ_SET hero=... what=... old=... new=...</c> with the value read back after the
/// change, so the host shows what the server really holds. A refusal answers
/// <c>COODSZ_ERROR ...</c>.
/// </para>
/// <para>
/// The module is server only (its id starts with DedicatedServer.), stores nothing in the save
/// and never touches Coop's files. Changes go through the game's own methods on the server, the
/// same ones the campaign uses, so Coop sends them to the players like any other change.
/// </para>
/// </remarks>
public sealed class ToolsSubModule : MBSubModuleBase
{
    public const string Version = "1";

    /// <summary>The one Harmony owner id of every patch this module makes (clan cap, server rules, MLW recruit).</summary>
    public const string HarmonyId = "coodsz.servertools";

    protected override void OnSubModuleLoad()
    {
        base.OnSubModuleLoad();
        Register();
        if (GameNetwork.IsDedicatedServer) ClanCapSafely(ClanCap.PatchGame);
        if (GameNetwork.IsDedicatedServer) RulesSafely(ServerRules.PatchGame);
    }

    protected override void OnBeforeInitialModuleScreenSetAsRoot()
    {
        base.OnBeforeInitialModuleScreenSetAsRoot();
        Register();
        ClanCapSafely(ClanCap.PatchCoop);
        RulesSafely(ServerRules.PatchCoop);
        MlwRecruitSafely();
        SettingsSyncSafely();
    }

    public override void OnGameInitializationFinished(TaleWorlds.Core.Game game)
    {
        base.OnGameInitializationFinished(game);
        ClanCapSafely(ClanCap.PatchCoop);
        RulesSafely(ServerRules.PatchCoop);
        MlwRecruitSafely();
        SettingsSyncSafely();
    }

    protected override void OnApplicationTick(float dt)
    {
        base.OnApplicationTick(dt);
        // Coop builds its handlers once, when the server starts hosting, and a slow server hosts before the
        // module screen callbacks above come. Made on the first tick, the loader is there in time (its Tick keeps
        // trying until the Coop probe passes). One try here, so a failure logs once.
        if (_settingsSync == null && !_settingsSyncTried && GameNetwork.IsDedicatedServer)
        {
            _settingsSyncTried = true;
            SettingsSyncSafely();
        }
        _settingsSync?.Tick(dt);
        if (GameNetwork.IsDedicatedServer) RulesSafely(() => ServerRules.Tick(dt));
    }

    // Settings sync: sends the host's MCM settings to players with the Coodsz client add on. Lives in
    // CoodszSettingsSync.Coop.dll next to this one, loaded only after the probe finds the Coop types it uses.
    private static CoodszSettingsSync.SettingsSyncLoader? _settingsSync;
    private static bool _settingsSyncTried;

    /// <summary>The loaded settings sync, which also sends the PvP countdown (PvpRules); null on a client.</summary>
    internal static CoodszSettingsSync.SettingsSyncLoader? SettingsSync => _settingsSync;

    private static void SettingsSyncSafely()
    {
        if (!GameNetwork.IsDedicatedServer) return;
        try
        {
            if (_settingsSync == null)
            {
                string bin = System.IO.Path.GetDirectoryName(typeof(ToolsSubModule).Assembly.Location) ?? ".";
                string module = System.IO.Path.GetFullPath(System.IO.Path.Combine(bin, "..", ".."));
                _settingsSync = new CoodszSettingsSync.SettingsSyncLoader(false, bin, module,
                    line => { try { Console.WriteLine("[Coodsz Server Tools] " + line); } catch { } });
            }
            _settingsSync.TryLoad();
        }
        catch (Exception e)
        {
            try { Console.WriteLine("[Coodsz Server Tools] Settings sync disabled: " + e.GetType().Name + ": " + e.Message); } catch { }
        }
    }

    // Max clans per kingdom must never stop the server or the commands: if Harmony is missing or a
    // patch fails (a JIT or type load error can come before ClanCap's own try), log it and go on.
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static void ClanCapSafely(Action patch)
    {
        try { patch(); }
        catch (Exception e)
        {
            try { Console.WriteLine("[Coodsz Server Tools] Max clans per kingdom is not active: " + e.GetType().Name + ": " + e.Message); } catch { }
        }
    }

    // My Little Warband recruits (MlwRecruit.cs) work the same way: a failure is one log line and Coop
    // recruits as before. Server only, like the patch itself.
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static void MlwRecruitSafely()
    {
        if (!GameNetwork.IsDedicatedServer) return;
        try { MlwRecruit.PatchCoop(); }
        catch (Exception e)
        {
            try { Console.WriteLine("[Coodsz Server Tools] My Little Warband recruits are off: " + e.GetType().Name + ": " + e.Message); } catch { }
        }
    }

    // The server rules (coop.unstuck for players, offline XP freeze) work the same way: one failure is
    // one log line, then they stay off for this run instead of logging on every tick.
    private static bool _rulesBroken;

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static void RulesSafely(Action run)
    {
        if (_rulesBroken) return;
        try { run(); }
        catch (Exception e)
        {
            _rulesBroken = true;
            try { Console.WriteLine("[Coodsz Server Tools] Server rules are not active: " + e.GetType().Name + ": " + e.Message); } catch { }
        }
    }

    /// <summary>
    /// Adds the commands to the game's command table. The table can be collected before this
    /// module loads, so missing entries are added straight into it.
    /// </summary>
    private static void Register()
    {
        // The last command added is there, so all of them are.
        if (CommandLineFunctionality.HasFunctionForCommand("coodsz.clancap")) return;

        try { CommandLineFunctionality.CollectCommandLineFunctions(); }
        catch (Exception) { /* already collected: a duplicate key can throw */ }

        var field = typeof(CommandLineFunctionality).GetField("AllFunctions", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
        var functionType = typeof(CommandLineFunctionality).GetNestedType("CommandLineFunction", BindingFlags.NonPublic | BindingFlags.Public);
        if (!(field?.GetValue(null) is IDictionary all) || functionType is null) return;

        void Add(string name, Func<List<string>, string> f)
        {
            if (all.Contains(name)) return;
            all[name] = Activator.CreateInstance(functionType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null, new object[] { f }, CultureInfo.InvariantCulture);
        }

        Add("coodsz.ping", Safe(_ => "COODSZ_TOOLS v" + Version));
        Add("coodsz.heroes", Safe(HeroCommands.Heroes));
        Add("coodsz.hero", Safe(HeroCommands.Profile));
        Add("coodsz.skills", Safe(DevelopmentCommands.SkillsCommand));
        Add("coodsz.equipment", Safe(HeroCommands.Equipment));
        Add("coodsz.inventory", Safe(ItemCommands.Inventory));
        Add("coodsz.roster", Safe(TroopCommands.Roster));
        Add("coodsz.gold", Safe(HeroCommands.Gold));
        Add("coodsz.set_gold", Safe(HeroCommands.SetGold));
        Add("coodsz.influence", Safe(HeroCommands.Influence));
        Add("coodsz.renown", Safe(HeroCommands.Renown));
        Add("coodsz.clan_tier", Safe(HeroCommands.ClanTier));
        Add("coodsz.health", Safe(HeroCommands.Health));
        Add("coodsz.age", Safe(HeroCommands.Age));
        Add("coodsz.skill", Safe(DevelopmentCommands.Skill));
        Add("coodsz.skill_xp", Safe(DevelopmentCommands.SkillXp));
        Add("coodsz.focus", Safe(DevelopmentCommands.Focus));
        Add("coodsz.attribute", Safe(DevelopmentCommands.Attribute));
        Add("coodsz.unspent", Safe(DevelopmentCommands.Unspent));
        Add("coodsz.troops", Safe(TroopCommands.Troops));
        Add("coodsz.heal_troops", Safe(TroopCommands.HealTroops));
        Add("coodsz.troop_xp", Safe(TroopCommands.TroopXp));
        Add("coodsz.give_item", Safe(args => ItemCommands.ChangeItem(args, +1)));
        Add("coodsz.take_item", Safe(args => ItemCommands.ChangeItem(args, -1)));
        Add("coodsz.items", Safe(ItemCommands.Items));
        Add("coodsz.troop_types", Safe(TroopCommands.TroopTypes));
        Add("coodsz.world", Safe(WorldCommands.World));
        Add("coodsz.map", Safe(MapCommands.Map));
        Add("coodsz.terrain", Safe(TerrainCommands.Terrain));
        Add("coodsz.clancap", Safe(ClanCap.Command));
    }

    /// <summary>A command that throws answers with the reason instead of taking the console queue down.</summary>
    private static Func<List<string>, string> Safe(Func<List<string>, string> command) => args =>
    {
        try { return command(args ?? new List<string>()); }
        catch (Exception e) { return "COODSZ_ERROR " + Text.Clean(e.GetType().Name + ": " + e.Message); }
    };
}
