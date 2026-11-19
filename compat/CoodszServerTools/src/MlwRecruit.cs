// Coodsz Server Tools: My Little Warband custom recruits on a Coop server with CoopModPatch. The cart rewrite and
// the swap are in MlwRecruitRules.cs; this file patches Coop's recruit handler and hands them Coop's objects by id.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using Coodsz.ServerRules;

namespace CoodszServerTools;

/// <summary>
/// Lets players recruit My Little Warband custom troops on a Coop server running CoopModPatch.
/// </summary>
/// <remarks>
/// <para>
/// CoopModPatch shows each player custom troops in the recruit screen, but only in that player's game: the server's
/// volunteer slots still hold the normal troops, so Coop's TroopRosterInterface.HandleOnRecruitmentDone skipped every
/// custom entry in the cart. A prefix puts the slot's normal troop back into each custom entry
/// (MlwRecruitRules.Rewrite), Coop recruits as usual, and a postfix swaps each normal troop Coop really added for the
/// custom one with the recruit xp it got (MlwRecruitRules.Swap), the way My Little Warband's own ClanRecruitPatch2
/// does in single player. Coop sends the roster changes to the players like any other.
/// </para>
/// <para>
/// Gold: Coop charges the normal troop's price, not the custom price the recruit screen showed. Left that way.
/// </para>
/// <para>
/// The custom troops exist only when CoopModPatch is loaded, so without it nothing resolves and nothing changes
/// (one log line the first time a cart asks for one). A missing Coop member is one log line and no patch.
/// </para>
/// </remarks>
internal static class MlwRecruit
{
    private const string Handler = "GameInterface.Services.TroopRosters.Interfaces.TroopRosterInterface";
    private const string TroopInfo = "GameInterface.Services.TroopRosters.Messages.TroopInfo";
    private const string ObjectManager = "GameInterface.Services.ObjectManager.IObjectManager";

    private static bool _patched;
    private static ConstructorInfo _newTroopInfo = null!;
    private static FieldInfo _heroId = null!;
    private static FieldInfo _troopId = null!;
    private static FieldInfo _index = null!;
    private static bool _unknownLogged;
    private static bool _errorLogged;

    /// <summary>One call's rewrite, carried from the prefix to the postfix in Harmony's __state.</summary>
    private sealed record State(World World, PartyRoster Roster, List<MlwMapped> Mapped, Dictionary<string, (int Count, int Xp)> Before);

    /// <summary>Coop's recruit handler: patched once every module is loaded.</summary>
    public static void PatchCoop()
    {
        if (_patched) return;
        // Coop loads before this module; until its GameInterface is there, there is nothing to patch.
        var handler = AccessTools.TypeByName(Handler);
        if (handler is null) return;
        _patched = true;
        var troopInfo = AccessTools.TypeByName(TroopInfo);
        var target = troopInfo is null ? null : AccessTools.Method(handler, "HandleOnRecruitmentDone", new[] { typeof(string), troopInfo.MakeArrayType() });
        var create = troopInfo is null ? null : AccessTools.Constructor(troopInfo, new[] { typeof(string), typeof(string), typeof(int) });
        var heroId = troopInfo is null ? null : AccessTools.Field(troopInfo, "RecruiterHeroId");
        var troopId = troopInfo is null ? null : AccessTools.Field(troopInfo, "CharacterObjectId");
        var index = troopInfo is null ? null : AccessTools.Field(troopInfo, "TroopIndex");
        var objects = AccessTools.TypeByName(ObjectManager);
        var tryGetId = objects is null ? null : AccessTools.Method(objects, "TryGetId", new[] { typeof(object), typeof(string).MakeByRefType() });
        var missing = MlwRecruitRules.FirstMissing(
            ("Coop's " + ObjectManager + ".TryGetId(object, out string)", tryGetId),
            ("Coop's " + TroopInfo, troopInfo),
            ("TroopRosterInterface.HandleOnRecruitmentDone(string, TroopInfo[])", target),
            ("TroopInfo(string, string, int)", create),
            ("TroopInfo.RecruiterHeroId", heroId),
            ("TroopInfo.CharacterObjectId", troopId),
            ("TroopInfo.TroopIndex", index));
        if (missing is null && !(heroId!.FieldType == typeof(string) && troopId!.FieldType == typeof(string) && index!.FieldType == typeof(int)))
            missing = "TroopInfo with string, string and int fields";
        if (missing != null)
        {
            CoopBridge.Log("My Little Warband recruits are off: " + missing + " was not found in Coop, so Coop recruits as before.");
            return;
        }

        _newTroopInfo = create!;
        _heroId = heroId!;
        _troopId = troopId!;
        _index = index!;
        new Harmony(ToolsSubModule.HarmonyId).Patch(target,
            prefix: new HarmonyMethod(typeof(MlwRecruit), nameof(Prefix)),
            postfix: new HarmonyMethod(typeof(MlwRecruit), nameof(Postfix)));
    }

    // Rewrites the custom entries onto their slots' normal troops. Entries that do not map stay as they are, so Coop's
    // own checks and its skipped line behave as before. Any failure puts the cart back as it came.
    private static void Prefix(object __instance, object[] __args, out object? __state)
    {
        __state = null;
        if (__args.Length < 2 || __args[0] is not string partyId || __args[1] is not Array cart || cart.Length == 0) return;
        var original = (Array)cart.Clone();
        try
        {
            var entries = new MlwCartEntry[cart.Length];
            for (var i = 0; i < cart.Length; i++)
            {
                var entry = cart.GetValue(i)!;
                entries[i] = new MlwCartEntry(_heroId.GetValue(entry) as string, _troopId.GetValue(entry) as string, (int)_index.GetValue(entry)!);
            }

            var world = new World(__instance);
            var mapped = new List<MlwMapped>();
            MlwRecruitRules.Rewrite(entries, world, mapped, out var unknownCustom);
            if (unknownCustom && !_unknownLogged)
            {
                _unknownLogged = true;
                CoopBridge.Log("My Little Warband recruits: a cart asked for a troop the server does not know that looks like a custom troop; most likely CoopModPatch is not loaded on the server, so Coop skips it.");
            }

            if (mapped.Count == 0) return;
            var party = CoopBridge.CoopObject<MobileParty>(__instance, partyId);
            if (party?.MemberRoster is null) return;
            var roster = new PartyRoster(party.Name?.ToString() ?? "a party", party.MemberRoster, world.Troops);
            var before = mapped.Select(m => m.NativeId).Distinct().ToDictionary(id => id, id => (roster.Count(id), roster.Xp(id)));
            foreach (var m in mapped)
                cart.SetValue(_newTroopInfo.Invoke(new object[] { m.HeroId, m.NativeId, m.Index }), m.Position);
            __state = new State(world, roster, mapped, before);
        }
        catch (Exception e)
        {
            Array.Copy(original, cart, cart.Length);
            __state = null;
            LogErrorOnce(e, "that cart went to Coop as it came");
        }
    }

    // Swaps the normal troops Coop really recruited for the custom ones. The state lives only in this call's
    // __state, so nested or repeated calls never see each other's entries.
    private static void Postfix(object? __state)
    {
        if (__state is not State state) return;
        try
        {
            var swapped = MlwRecruitRules.Swap(state.Mapped, state.Before, state.World, state.Roster);
            if (swapped > 0)
                CoopBridge.Log("My Little Warband recruits: " + state.Roster.PartyName + " got " + swapped + " custom troop(s) in place of the normal ones.");
        }
        catch (Exception e)
        {
            LogErrorOnce(e, "the party keeps the normal troops Coop gave it");
        }
    }

    private static void LogErrorOnce(Exception e, string result)
    {
        if (_errorLogged) return;
        _errorLogged = true;
        CoopBridge.Log("My Little Warband recruits failed, so " + result + ": " + e.GetType().Name + ": " + e.Message);
    }

    /// <summary>
    /// Coop's object manager and the notables' slots. Cart and hero ids are resolved to objects through Coop only,
    /// custom is read from the troop object's StringId, and a slot troop's id is asked from Coop. Remembers every
    /// troop it resolved under the Coop id it came with, for the roster.
    /// </summary>
    private sealed class World : IMlwRecruitWorld
    {
        private readonly object _handler;
        private readonly Dictionary<string, Hero?> _heroes = new Dictionary<string, Hero?>();
        public readonly Dictionary<string, CharacterObject> Troops = new Dictionary<string, CharacterObject>();

        public World(object handler) => _handler = handler;

        public bool TryCartTroop(string troopId, out bool custom)
        {
            var troop = Troops.TryGetValue(troopId, out var known) ? known : CoopBridge.CoopObject<CharacterObject>(_handler, troopId);
            custom = troop != null && MlwRecruitRules.IsCustomId(troop.StringId);
            if (troop is null) return false;
            Troops[troopId] = troop;
            return true;
        }

        public int SlotCount(string heroId) => Hero(heroId)?.VolunteerTypes?.Length ?? 0;

        public bool TrySlotTroop(string heroId, int index, out string? troopId, out bool custom)
        {
            var troop = Slot(heroId, index);
            troopId = troop is null ? null : CoopBridge.CoopId(_handler, troop);
            custom = troop != null && MlwRecruitRules.IsCustomId(troop.StringId);
            if (troopId != null) Troops[troopId] = troop!;
            return troop != null;
        }

        public bool SameTroop(string troopId, string heroId, int index) =>
            Troops.TryGetValue(troopId, out var troop) && ReferenceEquals(troop, Slot(heroId, index));

        private CharacterObject? Slot(string heroId, int index)
        {
            var slots = Hero(heroId)?.VolunteerTypes;
            return slots != null && index >= 0 && index < slots.Length ? slots[index] : null;
        }

        private Hero? Hero(string heroId)
        {
            if (!_heroes.TryGetValue(heroId, out var hero)) _heroes[heroId] = hero = CoopBridge.CoopObject<Hero>(_handler, heroId);
            return hero;
        }
    }

    /// <summary>The party's member roster: each Coop troop id goes to the object World resolved for it.</summary>
    private sealed class PartyRoster : IMlwRoster
    {
        private readonly TroopRoster _roster;
        private readonly Dictionary<string, CharacterObject> _troops;

        public PartyRoster(string partyName, TroopRoster roster, Dictionary<string, CharacterObject> troops)
        {
            PartyName = partyName;
            _roster = roster;
            _troops = troops;
        }

        public string PartyName { get; }

        public int Count(string troopId) => _roster.GetTroopCount(_troops[troopId]);

        public int Xp(string troopId) => _roster.GetElementXp(_troops[troopId]);

        // The game clamps a stack's xp between 0 and what its troops can use, after the count change.
        public void Add(string troopId, int count, int xpChange) => _roster.AddToCounts(_troops[troopId], count, false, 0, xpChange, true, -1);
    }
}
