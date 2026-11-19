using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace RealmOfThronesCoopCompat
{
    // The bodies of the server guards (RotGuards puts them in). Each one asks "is this any
    // player's hero or clan" (Coop.IsPlayerHero, Coop.IsPlayerClan) where Realm of Thrones asks
    // for the one local player, who on the server is the server's own hero and clan. Realm of
    // Thrones' members are reached by name: RotGuards fills the handles below before it patches.
    // Nothing throws out of a patch: a guard that fails inside leaves players alone and logs once.
    internal static class RotGuardsPatches
    {
        // Realm of Thrones' own rules, called inside a guard.
        internal static Func<PartyBase, Hero, bool> RotCapture;
        internal static Func<IFaction, IFaction, DeclareWarAction.DeclareWarDetail, bool> RotWarRule;
        internal static Func<IFaction, IFaction, int, int, MakePeaceAction.MakePeaceDetail, bool> RotPeaceRule;
        // Coop's own player party tests, run before Realm of Thrones' prefixes that would skip them.
        internal static Func<MobileParty, bool> CoopAiThinkGuard;
        internal static Func<MobileParty, bool> CoopPrisonerGuard;

        // Realm of Thrones' settings, objects and members.
        internal static Func<bool> KingsProtectNightsWatch;
        internal static Func<bool> WarbandSetting;
        internal static Func<Kingdom> NightsWatch;
        internal static Func<Kingdom> FreeFolk;
        internal static Func<Clan> WhiteWalkers;
        internal static Func<Settlement> TheWall;
        internal static Func<Settlement> CastleBlack;
        internal static Func<Settlement> KingsLanding;
        internal static Func<Settlement> RedKeep;
        internal static Func<Vec2, bool> IsBeyondTheWall;
        internal static PropertyInfo Defenders;
        internal static FieldInfo UnmarriedHeroes;
        internal static PropertyInfo NightKing;
        internal static FieldInfo InvasionStarted;
        internal static MethodInfo ResolveHeir;
        internal static MethodInfo ChangeToOthersCulture;
        internal static MethodInfo RevertFromOthersCulture;
        internal static FieldInfo Recruiter;
        internal static MethodInfo ClearSettlementCache;

        private static readonly HashSet<string> Logged = new HashSet<string>(StringComparer.Ordinal);
        private static List<Hero> _filteredList;
        [ThreadStatic] private static bool _peaceVetoed;

        // A line that would repeat every tick or for every party is logged once.
        private static void Once(string key, string line)
        {
            lock (Logged)
            {
                if (!Logged.Add(key))
                    return;
            }
            SubModule.Log(line);
        }

        private static void Failed(string guard, Exception ex) =>
            Once("fail " + guard, RotGuardsRules.Loud + guard + " failed inside (logged once), so players were left alone there: " + RotHost.Short(ex));

        // The server's own hero and clan, which Realm of Thrones exempts as the player's. Reading
        // Hero.MainHero throws while the server has no player character.
        private static Hero ServerHero()
        {
            try { return Hero.MainHero; }
            catch { return null; }
        }

        private static Clan ServerClan()
        {
            try { return Clan.PlayerClan; }
            catch { return null; }
        }

        private static bool IsWhiteWalkerCulture(CultureObject culture) => culture != null && culture.StringId == JoinRules.WhiteWalkerCulture;

        private static bool IsWhiteWalkerOrWight(Hero hero)
        {
            var race = hero?.CharacterObject?.Race ?? 0;
            return race == RotGuardsRules.WhiteWalkerRace || race == RotGuardsRules.WightRace;
        }

        private static bool IsPlayerKingdom(IFaction faction) => faction is Kingdom kingdom && Coop.IsPlayerClan(kingdom.RulingClan);

        // ROTStorylineWars.OnWarDeclared, replaced: the kings protect the Night's Watch from every
        // kingdom a player's clan rules, not from the server's own clan.
        internal static bool WarDeclaredPrefix(object __instance, IFaction __0, IFaction __1)
        {
            try
            {
                var nightsWatch = NightsWatch();
                var freeFolk = FreeFolk();
                var side = RotGuardsRules.ProtectedPlayerSide(KingsProtectNightsWatch(), nightsWatch != null && __0 == nightsWatch, nightsWatch != null && __1 == nightsWatch,
                    IsPlayerKingdom(__0), IsPlayerKingdom(__1), freeFolk != null && __0 == freeFolk, freeFolk != null && __1 == freeFolk);
                if (side == 0)
                    return false;
                var protectedKingdom = side == 1 ? __0 : __1;
                var declared = 0;
                foreach (var defender in Defenders.GetValue(__instance, null) as Kingdom[] ?? new Kingdom[0])
                {
                    if (defender == null || defender == protectedKingdom)
                        continue;
                    DeclareWarAction.ApplyByDefault(defender, __0);
                    declared++;
                }
                SubModule.Log(RotGuardsRules.Prefix + "the kings protect the Night's Watch: " + __0?.StringId + " and " + __1?.StringId + " went to war, " + protectedKingdom?.StringId
                    + " is ruled by a player's clan, so " + declared + " kingdoms declare war on " + __0?.StringId + ".");
            }
            catch (Exception ex)
            {
                Failed("The Night's Watch guard", ex);
            }
            return false;
        }

        // ROTRelationshipsBehavior: players' heroes and their clans' members are no partners for
        // Realm of Thrones' NPC marriages, and a player's clan never looks for one.
        internal static void UnmarriedPostfix(object __instance)
        {
            try
            {
                if (UnmarriedHeroes.GetValue(__instance) is List<Hero> list)
                    KeepPlayersOut(list);
            }
            catch (Exception ex)
            {
                Failed("The marriage guard", ex);
            }
        }

        internal static bool MarriagePrefix(object __instance, Clan __0)
        {
            try
            {
                if (__0 == null || Coop.IsPlayerClan(__0))
                    return false;
                // A loaded save brings its own list, written before this guard ran.
                if (UnmarriedHeroes.GetValue(__instance) is List<Hero> list && !ReferenceEquals(list, _filteredList))
                    KeepPlayersOut(list);
                return true;
            }
            catch (Exception ex)
            {
                Failed("The marriage guard", ex);
                return false;
            }
        }

        private static void KeepPlayersOut(List<Hero> list)
        {
            var clans = new Dictionary<Clan, bool>();
            bool PlayerClan(Clan clan)
            {
                if (clan == null)
                    return false;
                if (!clans.TryGetValue(clan, out var player))
                    clans[clan] = player = Coop.IsPlayerClan(clan);
                return player;
            }
            list.RemoveAll(hero => hero == null || PlayerClan(hero.Clan) || Coop.IsPlayerHero(hero));
            _filteredList = list;
        }

        // ROTCoreBehavior.DailyTickClan, replaced: Realm of Thrones' daily gold for poor clans
        // (RotGuardsRules.DailyGold), never for a player's clan. Its own test reads Hero.MainHero,
        // which the server does not have, so it is not run.
        internal static bool GoldPrefix(Clan __0)
        {
            try
            {
                var clan = __0;
                var gold = clan == null ? 0 : RotGuardsRules.DailyGold(clan.Leader != null, Coop.IsPlayerClan(clan), clan == ServerClan(), clan.Gold, clan.IsBanditFaction,
                    clan.IsClanTypeMercenary, clan.StringId, clan.Tier);
                if (gold > 0)
                    GiveGoldAction.ApplyBetweenCharacters(null, clan.Leader, gold, true);
            }
            catch (Exception ex)
            {
                Failed("The gold guard", ex);
            }
            return false;
        }

        // ROTTroopRecruiter.IsHeroManageable, replaced: Realm of Thrones' rule with every player
        // in it, without reading the server's own hero unguarded.
        internal static bool ManageablePrefix(Hero __0, ref bool __result)
        {
            try
            {
                var hero = __0;
                var party = hero?.PartyBelongedTo;
                var kingdom = hero?.MapFaction as Kingdom;
                var serverClan = ServerClan();
                var warband = WarbandSetting();
                __result = RotGuardsRules.RecruiterManages(hero != null, party != null && hero.IsPartyLeader && party.IsCaravan, Coop.IsPlayerHero(hero),
                    hero != null && Coop.IsPlayerClan(hero.Clan), hero != null && hero == ServerHero(), hero?.Clan != null && hero.Clan == serverClan,
                    warband, warband && kingdom != null && Coop.IsPlayerHero(kingdom.Leader));
            }
            catch (Exception ex)
            {
                __result = false;
                Failed("The troop recruiter guard", ex);
            }
            return false;
        }

        // ROTTroopRecruiter.IsGarrisonManageable: a player's fief keeps its garrison.
        internal static bool GarrisonPrefix(Settlement __0, ref bool __result)
        {
            try
            {
                if (__0 == null || !Coop.IsPlayerClan(__0.OwnerClan))
                    return true;
            }
            catch (Exception ex)
            {
                Failed("The garrison guard", ex);
            }
            __result = false;
            return false;
        }

        // ROTOthersCampaignBehavior.OnHourlyTick, replaced: the Night King's death collapse with
        // every player spared, then the leash on White Walker war parties before the invasion.
        // Its two branches for a White Walker or wight player (peace with the White Walkers and
        // the enlistment popup) are left out: that start is off in co-op. The collapse starts
        // NightKingDeathEvent through RotStory, as Realm of Thrones' tick does.
        internal static bool HourlyPrefix(object __instance)
        {
            try
            {
                Collapse(__instance);
            }
            catch (Exception ex)
            {
                Failed("The Night King death guard", ex);
            }
            try
            {
                Leash(__instance);
            }
            catch (Exception ex)
            {
                Failed("The White Walker leash", ex);
            }
            return false;
        }

        private static void Collapse(object behavior)
        {
            var whiteWalkers = WhiteWalkers();
            if (whiteWalkers == null || whiteWalkers.IsEliminated)
                return;
            var nightKing = NightKing.GetValue(behavior, null) as Hero;
            if (nightKing != null && nightKing.IsAlive)
                return;
            int handed = 0, reverted = 0, executed = 0, spared = 0;
            foreach (var settlement in whiteWalkers.Settlements.Where(settlement => settlement.IsFortification).Reverse().ToList())
            {
                // Realm of Thrones' heir: the previous owner, the Night's Watch for its two castles,
                // else the nearest fitting clan, never a player's (FindHeirPrefix).
                if (ResolveHeir.Invoke(behavior, new object[] { settlement }) is Clan heir)
                {
                    settlement.Town.OwnerClan = heir;
                    handed++;
                }
                if (settlement.Culture == whiteWalkers.Culture)
                {
                    RevertFromOthersCulture.Invoke(behavior, new object[] { settlement });
                    reverted++;
                }
            }
            var serverClan = ServerClan();
            foreach (var hero in Hero.AllAliveHeroes.Where(hero => IsWhiteWalkerOrWight(hero) && hero.Clan != whiteWalkers && !hero.IsNotable).Reverse().ToList())
            {
                if (RotGuardsRules.SparedAtCollapse(Coop.IsPlayerHero(hero), Coop.IsPlayerClan(hero.Clan), serverClan != null && hero.Clan == serverClan))
                {
                    spared++;
                    continue;
                }
                KillCharacterAction.ApplyByExecution(hero, null, false, false);
                executed++;
            }
            DestroyClanAction.Apply(whiteWalkers);
            SubModule.Log(RotGuardsRules.Prefix + "the Night King is dead: " + handed + " White Walker fortifications went to their heirs (" + reverted + " back to their own culture), "
                + executed + " White Walker and wight heroes executed, " + spared + " spared as players' or the server's; the White Walker clan is "
                + (whiteWalkers.IsEliminated ? "destroyed" : "STILL NOT ELIMINATED") + "; " + RotStory.StartNightKingDeath() + ".");
        }

        private static void Leash(object behavior)
        {
            if (InvasionStarted.GetValue(behavior) is bool started && started)
                return;
            var whiteWalkers = WhiteWalkers();
            var home = whiteWalkers?.HomeSettlement;
            if (home == null)
                return;
            foreach (var component in whiteWalkers.WarPartyComponents.ToList())
            {
                var party = component?.MobileParty;
                if (party != null && !IsBeyondTheWall(party.GetPosition2D))
                    party.Position = home.GatePosition;
            }
        }

        // ROTOthersCampaignBehavior.FindNearestViableClan, replaced: Realm of Thrones' nearest heir
        // clan search, with every player's clan left out as it leaves out the player's.
        internal static bool FindHeirPrefix(Settlement __0, Func<Clan, bool> __1, bool __2, ref Clan __result)
        {
            try
            {
                var position = __0.GetPosition2D;
                var beyondTheWall = IsBeyondTheWall(position);
                var serverClan = ServerClan();
                __result = Clan.All.Where(clan => clan != null && !clan.IsEliminated && !clan.IsBanditFaction && !IsWhiteWalkerCulture(clan.Culture) && clan != serverClan
                        && !clan.IsClanTypeMercenary && clan.HomeSettlement != null && (!__2 || IsBeyondTheWall(clan.HomeSettlement.GetPosition2D) == beyondTheWall)
                        && (__1 == null || __1(clan)) && !Coop.IsPlayerClan(clan))
                    .OrderBy(clan => clan.HomeSettlement.GetPosition2D.Distance(position)).FirstOrDefault();
            }
            catch (Exception ex)
            {
                __result = null;
                Failed("The heir guard", ex);
            }
            return false;
        }

        // ROTOthersCampaignBehavior.OnSettlementOwnerChanged, replaced: the Wall and Castle Black
        // keep one owner; King's Landing and the Red Keep too, unless a player's clan takes one,
        // as Realm of Thrones lets the player take them one by one; a fortification a White
        // Walker clan takes turns White Walker and turns back when it is lost. Null owners no
        // longer throw inside the game's owner change.
        internal static bool OwnerChangedPrefix(object __instance, Settlement __0, Hero __2, Hero __3)
        {
            try
            {
                var settlement = __0;
                var newOwner = __2;
                if (settlement == null)
                    return false;
                if (newOwner != null)
                {
                    Twin(settlement, newOwner, TheWall(), CastleBlack(), false);
                    var playerTakes = Coop.IsPlayerClan(settlement.OwnerClan) || (settlement.OwnerClan != null && settlement.OwnerClan == ServerClan());
                    Twin(settlement, newOwner, KingsLanding(), RedKeep(), playerTakes);
                }
                var whiteWalkers = WhiteWalkers()?.Culture;
                if (whiteWalkers == null)
                    return false;
                if (IsWhiteWalkerCulture(newOwner?.Clan?.Culture) && settlement.Culture != whiteWalkers)
                {
                    ChangeToOthersCulture.Invoke(__instance, new object[] { settlement, __3?.Clan });
                    var recruiter = Recruiter.GetValue(null);
                    if (recruiter != null)
                        ClearSettlementCache.Invoke(recruiter, new object[] { settlement });
                }
                else if (IsWhiteWalkerCulture(__3?.Clan?.Culture) && settlement.Culture == whiteWalkers)
                    RevertFromOthersCulture.Invoke(__instance, new object[] { settlement });
            }
            catch (Exception ex)
            {
                Failed("The settlement owner guard", ex);
            }
            return false;
        }

        // Two settlements that keep one owner: when one changes hands, the other follows.
        private static void Twin(Settlement settlement, Hero newOwner, Settlement first, Settlement second, bool playerTakes)
        {
            if (first == null || second == null || playerTakes || (settlement != first && settlement != second) || first.MapFaction == second.MapFaction)
                return;
            ChangeOwnerOfSettlementAction.ApplyByDefault(newOwner, settlement == first ? second : first);
        }

        // ROTOthersCampaignBehavior's culture writes, which Coop does not send to players (plan
        // item S16): the link sends what each one changed (RotWorldSync), and the postfix logs
        // each write's site. The finalizer runs also when the write threw, so the link's count of
        // open writes always ends. RotWorldSync never throws.
        internal static void CultureWritePrefix(object[] __args) => RotWorldSync.BeginWrite(__args.Length > 0 ? __args[0] as Settlement : null);

        internal static void CultureWriteFinalizer(MethodBase __originalMethod) => RotWorldSync.EndWrite("ROTOthersCampaignBehavior." + __originalMethod?.Name);

        internal static void CultureWrittenPostfix(MethodBase __originalMethod, object[] __args)
        {
            try
            {
                var name = __originalMethod?.Name;
                // EvacuateHeroes turns wanderers into White Walkers only when it empties a town for them.
                if (name == "EvacuateHeroes" && !(__args.Length > 1 && __args[1] is bool others && others))
                    return;
                var settlement = __args.Length > 0 ? __args[0] as Settlement : null;
                SubModule.Log(RotGuardsRules.CultureWriteLine("ROTOthersCampaignBehavior." + name, settlement?.StringId, settlement?.Culture?.StringId));
            }
            catch (Exception ex)
            {
                Failed("The culture write log", ex);
            }
        }

        // OthersPatches.patch7 inside a guard: a player's hero and his clan's heroes go through the
        // game's capture and Coop's (true lets the next prefixes run). Coop's prefix only remembers
        // a captured player's party, so skipping it for anybody else loses nothing.
        internal static bool CapturePrefix(PartyBase __0, Hero __1)
        {
            try
            {
                var prisoner = __1;
                if (prisoner == null)
                    return true;
                if (RotGuardsRules.CaptureLeftToCoop(Coop.IsPlayerHero(prisoner), Coop.IsPlayerClan(prisoner.Clan)))
                    return true;
                var culture = prisoner.Culture;
                var result = RotCapture(__0, prisoner);
                if (prisoner.Culture != culture)
                {
                    SubModule.Log(RotGuardsRules.HeroCultureWriteLine("OthersPatches.patch7", prisoner.StringId, prisoner.Culture?.StringId));
                    RotWorldSync.HeroWritten("OthersPatches.patch7", prisoner);
                }
                return result;
            }
            catch (Exception ex)
            {
                Failed("The White Walker captive guard", ex);
                return true;
            }
        }

        // The Diplomacy DeclareWarActionPatch inside a guard: a war of a player's clan, or of a
        // kingdom a player's clan rules, is never refused. It runs before Coop's prefix, so a
        // refused war is never sent to players.
        internal static bool WarPrefix(IFaction __0, IFaction __1, DeclareWarAction.DeclareWarDetail __2)
        {
            try
            {
                if ((__0 is Clan clan1 && Coop.IsPlayerClan(clan1)) || (__1 is Clan clan2 && Coop.IsPlayerClan(clan2)) || IsPlayerKingdom(__0))
                    return true;
                if (RotWarRule(__0, __1, __2))
                    return true;
                Once("war " + __0?.StringId + " " + __1?.StringId, RotGuardsRules.Prefix + "war of " + __0?.StringId + " on " + __1?.StringId
                    + " refused: the Night's Watch and the Free Folk stay out of wars (Realm of Thrones' rule; logged once for this pair).");
                return false;
            }
            catch (Exception ex)
            {
                Failed("The Night's Watch war guard", ex);
                return true;
            }
        }

        // OthersPatches.patch27 inside a guard: nobody makes peace with the White Walkers. A
        // vetoed peace also skips Coop's postfix (PeaceSentPrefix), which would send it anyway.
        internal static bool PeacePrefix(IFaction __0, IFaction __1, int __2, int __3, MakePeaceAction.MakePeaceDetail __4)
        {
            _peaceVetoed = false;
            try
            {
                if (RotPeaceRule(__0, __1, __2, __3, __4))
                    return true;
                _peaceVetoed = true;
                Once("peace " + __0?.StringId + " " + __1?.StringId, RotGuardsRules.Prefix + "peace of " + __0?.StringId + " with " + __1?.StringId
                    + " refused: nobody makes peace with the White Walkers (Realm of Thrones' rule; not sent to players; logged once for this pair).");
                return false;
            }
            catch (Exception ex)
            {
                Failed("The White Walker peace guard", ex);
                return true;
            }
        }

        internal static bool PeaceSentPrefix() => !_peaceVetoed;

        internal static void PeaceDonePostfix() => _peaceVetoed = false;

        // Coop's own player party tests, put before Realm of Thrones' prefixes on the same game
        // methods, which return false for every party and would skip Coop's.
        internal static bool AiThinkPrefix(MobileParty __0) => CoopFirst(CoopAiThinkGuard, __0, "Coop's party AI guard");

        internal static bool PrisonerPrefix(MobileParty __0) => CoopFirst(CoopPrisonerGuard, __0, "Coop's prisoner recruit guard");

        private static bool CoopFirst(Func<MobileParty, bool> guard, MobileParty party, string name)
        {
            try
            {
                // Without a session there are no players, and Coop's test has no players to ask about.
                return guard == null || !Coop.InSession || guard(party);
            }
            catch (Exception ex)
            {
                Failed(name, ex);
                return true;
            }
        }

        // CompanionsCampaignBehavior.CreateCompanionAndAddToSettlement: Coop's spawner places a new
        // wanderer, and none is made in a White Walker town. Coop's loop then picks its next town
        // with its own per town limit, so no town fills past it.
        internal static bool CompanionTownPrefix(Settlement __0)
        {
            try
            {
                if (__0 == null || !IsWhiteWalkerCulture(__0.Culture))
                    return true;
                Once("companion town", RotGuardsRules.Prefix + "no new wanderer in the White Walker town " + __0.StringId
                    + " (Realm of Thrones keeps wanderers out of White Walker towns; logged once).");
                return false;
            }
            catch (Exception ex)
            {
                Failed("The wanderer town guard", ex);
                return true;
            }
        }

        // The events of a behavior whose guard failed are blocked, so it keeps its saved data and
        // runs nothing.
        internal static bool NoEvents() => false;
    }
}
