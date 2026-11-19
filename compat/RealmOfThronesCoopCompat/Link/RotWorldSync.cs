#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.Settlements.Locations;
using TaleWorlds.ObjectSystem;

namespace RealmOfThronesCoopCompat
{
    // World writes Coop does not send (plan item S16): settlement cultures, hero cultures and the
    // notables' volunteer types that Realm of Thrones' hosted code writes on the server (the White
    // Walkers taking or losing a fortification, the Night King's death, a captive turned). The
    // server guards wrap each write site (RotGuards): before the outermost one this takes the values
    // of what that write can touch, after it (a finalizer, so a write that throws still ends) it
    // sends what changed on rot.world "set" to every player. A player who entered the campaign
    // gets a snapshot on rot.world "snapshot": every settlement's culture (no save keeps it) and the
    // current values of every hero a write touched in this session, split in parts under the link's
    // limit and only when it differs from the one that player got last (Coop says "entered" again
    // after every battle). A player's PC applies each value on the game thread and writes the
    // fields by reflection, outside every method Coop's patches rewrite.
    //
    // What a write can touch, from Realm of Thrones 8.1.8's ROTOthersCampaignBehavior:
    // ChangeToOthersCulture and RevertBackFromOthersCulture write the settlement's culture and its
    // bound villages', and call ChangeNotableCulture (each one's notables: culture, race and a fresh
    // empty volunteer array) and, for a town, EvacuateHeroes (the town's wanderers without a party:
    // moved, turned White Walker or killed). Their callers loop: the Night King's collapse (our
    // RotGuardsPatches.Collapse) over every White Walker fortification, and RoT's OnGameLoaded over
    // every settlement a White Walker clan holds. So the capture is that settlement, its villages,
    // their notables and the town's wanderers, taken once per outermost call, never the whole world.
    //
    // The same writes set each such hero's race (CharacterObject.Race, the White Walker look), sent
    // here with his culture: Coop sends a race change live itself (it syncs BasicCharacterObject.Race),
    // so the live value is mostly "already so" on a PC, but no save keeps a race outside Realm of
    // Thrones' own race map, so the snapshot carries it. Two more writes come from the story
    // (RotStory): King's Landing's scene names for the Battle of the Blackwater and back
    // (BlackwaterSiegeEvent.ToggleKingsLandingScene sets the four of its center), and Renly's and
    // Stannis' successions taking their clan off the campaign's clan list (CampaignObjectManager._clans,
    // written straight, as Realm of Thrones does). A PC sets a scene with Location.SetSceneName and
    // takes the clan off the same list; Coop patches neither.
    internal static class RotWorldSync
    {
        private static readonly FieldInfo SettlementCultureField = typeof(Settlement).GetField(nameof(Settlement.Culture));
        private static readonly FieldInfo HeroCultureField = typeof(Hero).GetField(nameof(Hero.Culture));
        private static readonly FieldInfo VolunteersField = typeof(Hero).GetField(nameof(Hero.VolunteerTypes));
        // Coop's auto sync patches the Race setter, so a PC writes the property's field.
        private static readonly FieldInfo RaceField = typeof(TaleWorlds.Core.BasicCharacterObject).GetField("<Race>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo SceneNamesField = typeof(Location).GetField("_sceneNames", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo ClansField = typeof(CampaignObjectManager).GetField("_clans", BindingFlags.Instance | BindingFlags.NonPublic);

        // Server: the heroes a hosted write touched in this session, by id; their current values go
        // in each snapshot.
        private static readonly Dictionary<string, Hero> Touched = new Dictionary<string, Hero>(StringComparer.Ordinal);
        // Server: the locations whose scenes a hosted write set ("settlement/location"), and the
        // clans a succession took off the list, for each snapshot.
        private static readonly Dictionary<string, Location> TouchedScenes = new Dictionary<string, Location>(StringComparer.Ordinal);
        private static readonly SortedSet<string> RemovedClans = new SortedSet<string>(StringComparer.Ordinal);
        private static Dictionary<string, string> _scenesBefore;
        private static Dictionary<string, Location> _sceneScope;
        private static List<string> _clansBefore;
        // Server: the snapshot hash each player's connection got last; a rejoin is a new peer.
        private static readonly Dictionary<object, string> SentHash = new Dictionary<object, string>(new SameObject());
        private static readonly WriteDepth Depth = new WriteDepth();
        // PC: lines said once per session.
        private static readonly HashSet<string> Said = new HashSet<string>();
        private static Scope _scope;
        private static int _snapshots;

        // ---- server ------------------------------------------------------------------------------

        // Before a hosted culture write (RotGuardsPatches); settlement is the writer's first argument.
        // Nested writers count as one. Never throws.
        public static void BeginWrite(Settlement settlement)
        {
            if (!Depth.Begin())
                return;
            try
            {
                _scope = Scope.Of(settlement);
            }
            catch (Exception ex)
            {
                _scope = null;
                SubModule.Log(RotLinkRules.Loud + "world values could not be read before a culture write: " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        // After it, also when it threw. site names the write for the log. Never throws.
        public static void EndWrite(string site)
        {
            if (!Depth.End())
                return;
            var scope = _scope;
            _scope = null;
            if (scope == null)
                return;
            try
            {
                Record(site, RotSyncRules.Changes(scope.Before, scope.Read()), scope.Heroes);
            }
            catch (Exception ex)
            {
                SubModule.Log(RotLinkRules.Loud + "what " + site + " changed could not be read or sent: " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        // A hero whose culture a guard saw change (OthersPatches.patch7's captives). Never throws.
        public static void HeroWritten(string site, Hero hero)
        {
            try
            {
                if (hero?.StringId == null)
                    return;
                var changes = new List<string>();
                AddHero(changes, hero);
                Record(site, changes, new[] { hero });
            }
            catch (Exception ex)
            {
                SubModule.Log(RotLinkRules.Loud + "what " + site + " changed could not be sent: " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        private static void Record(string site, List<string> changes, IEnumerable<Hero> heroes)
        {
            if (changes.Count == 0)
                return;
            var byId = (heroes ?? new Hero[0]).Where(hero => hero?.StringId != null).GroupBy(hero => hero.StringId).ToDictionary(group => group.Key, group => group.First());
            int settlements = 0, cultures = 0, volunteers = 0, races = 0, scenes = 0, clans = 0;
            foreach (var change in changes)
            {
                if (!RotSyncRules.TryParse(change, out var kind, out var id, out _))
                    continue;
                switch (kind)
                {
                    case RotSyncRules.SettlementCulture: settlements++; continue;
                    case RotSyncRules.LocationScene: scenes++; continue;
                    case RotSyncRules.ClanRemoved: clans++; continue;
                    case RotSyncRules.HeroCulture: cultures++; break;
                    case RotSyncRules.HeroRace: races++; break;
                    default: volunteers++; break;
                }
                if (byId.TryGetValue(id, out var hero))
                    Touched[id] = hero;
            }
            var sent = RotLink.InSession && Coop.OnServer ? SendParts(null, RotLinkRules.SetKey, 0, changes) : "no Coop session";
            SubModule.Log(RotLinkRules.Log + "world: " + site + " changed " + settlements + " settlement culture(s), " + cultures + " hero culture(s), " + volunteers
                + " volunteer set(s), " + races + " race(s), " + scenes + " scene name(s) and " + clans + " clan(s) off the list; " + sent + ".");
        }

        // Before ToggleKingsLandingScene (RotStory): the scene names of every location of that
        // settlement. Never throws.
        public static void BeginScenes(Settlement settlement)
        {
            try
            {
                _sceneScope = null;
                _scenesBefore = null;
                if (settlement?.StringId == null || settlement.LocationComplex == null)
                    return;
                _sceneScope = new Dictionary<string, Location>(StringComparer.Ordinal);
                foreach (var location in settlement.LocationComplex.GetListOfLocations())
                {
                    if (location?.StringId != null)
                        _sceneScope[settlement.StringId + "/" + location.StringId] = location;
                }
                _scenesBefore = ReadScenes(_sceneScope);
            }
            catch (Exception ex)
            {
                _sceneScope = null;
                SubModule.Log(RotLinkRules.Loud + "scene names could not be read before a scene write: " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        // After it, also when it threw. Never throws.
        public static void EndScenes(string site)
        {
            var scope = _sceneScope;
            var before = _scenesBefore;
            _sceneScope = null;
            _scenesBefore = null;
            if (scope == null || before == null)
                return;
            try
            {
                var changes = RotSyncRules.Changes(before, ReadScenes(scope));
                foreach (var change in changes)
                {
                    if (RotSyncRules.TryParse(change, out _, out var id, out _) && RotSyncRules.TryParseSceneId(id, out var settlement, out var location, out _))
                        TouchedScenes[settlement + "/" + location] = scope[settlement + "/" + location];
                }
                Record(site, changes, null);
            }
            catch (Exception ex)
            {
                SubModule.Log(RotLinkRules.Loud + "what " + site + " changed could not be read or sent: " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        // Keyed by RotSyncRules.KeyOf: one entry per location and level.
        private static Dictionary<string, string> ReadScenes(Dictionary<string, Location> locations)
        {
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var pair in locations)
            {
                var names = SceneNamesField?.GetValue(pair.Value) as string[];
                if (names == null)
                    continue;
                var at = pair.Key.LastIndexOf('/');
                for (var level = 0; level < Math.Min(names.Length, RotSyncRules.SceneLevels); level++)
                    values[RotSyncRules.LocationScene + "\t" + RotSyncRules.SceneId(pair.Key.Substring(0, at), pair.Key.Substring(at + 1), level)] = names[level] ?? "";
            }
            return values;
        }

        // Before a succession (RotStory): the ids on the campaign's clan list. Never throws.
        public static void BeginClanList()
        {
            try { _clansBefore = Clan.All.Where(clan => clan?.StringId != null).Select(clan => clan.StringId).ToList(); }
            catch { _clansBefore = null; }
        }

        // After it, also when it threw or was skipped. Never throws.
        public static void EndClanList(string site)
        {
            var before = _clansBefore;
            _clansBefore = null;
            if (before == null)
                return;
            try
            {
                var removed = RotSyncRules.RemovedClans(before, Clan.All.Where(clan => clan?.StringId != null).Select(clan => clan.StringId));
                foreach (var id in removed)
                    RemovedClans.Add(id);
                Record(site ?? "a succession", removed.Select(id => RotSyncRules.Entry(RotSyncRules.ClanRemoved, id, RotSyncRules.Removed)).ToList(), null);
            }
            catch (Exception ex)
            {
                SubModule.Log(RotLinkRules.Loud + "the clans " + site + " took off the list could not be read or sent: " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        // Coop's "entered the campaign" for one player's connection.
        public static void Entered(object peer)
        {
            if (peer == null || Campaign.Current == null)
                return;
            var entries = new List<string>();
            foreach (var settlement in Settlement.All)
            {
                if (settlement?.StringId != null && settlement.Culture != null)
                    entries.Add(RotSyncRules.Entry(RotSyncRules.SettlementCulture, settlement.StringId, settlement.Culture.StringId));
            }
            var settlements = entries.Count;
            var heroes = 0;
            foreach (var hero in Touched.Values)
            {
                if (hero?.StringId == null)
                    continue;
                AddHero(entries, hero);
                heroes++;
            }
            var before = entries.Count;
            foreach (var pair in ReadScenes(TouchedScenes))
                entries.Add(pair.Key + "\t" + pair.Value);
            var scenes = entries.Count - before;
            foreach (var id in RemovedClans)
                entries.Add(RotSyncRules.Entry(RotSyncRules.ClanRemoved, id, RotSyncRules.Removed));
            var hash = RotSyncRules.Hash(entries);
            if (SentHash.TryGetValue(peer, out var sent) && sent == hash)
                return;
            var number = ++_snapshots;
            var result = SendParts(peer, RotLinkRules.SnapshotKey, number, entries);
            if (result.StartsWith("sent", StringComparison.Ordinal))
                SentHash[peer] = hash;
            SubModule.Log(RotLinkRules.Log + "world snapshot " + number + " for a player who entered the campaign: " + entries.Count + " values (" + settlements
                + " settlement cultures, " + heroes + " touched heroes, " + scenes + " scene names and " + RemovedClans.Count + " clans off the list); " + result + ".");
        }

        // A second world in the same server process starts clean, and a stuck count cannot mute the sync.
        public static void SessionEnded()
        {
            SentHash.Clear();
            Touched.Clear();
            TouchedScenes.Clear();
            RemovedClans.Clear();
            _sceneScope = null;
            _scenesBefore = null;
            _clansBefore = null;
            Depth.Reset();
            _scope = null;
            Said.Clear();
        }

        // Sends the entries in parts under the link's limit: to every player (peer null) or one.
        // Says what happened, with the reason, for the log line.
        private static string SendParts(object peer, string key, int seq, List<string> entries)
        {
            var parts = RotSyncRules.Split(entries, RotLinkRules.PartBytes, out var skipped);
            var bytes = 0;
            for (var i = 0; i < parts.Count; i++)
            {
                var blob = RotSyncRules.Pack(parts[i]);
                var args = new[] { (i + 1).ToString(CultureInfo.InvariantCulture), parts.Count.ToString(CultureInfo.InvariantCulture) };
                string reason;
                var ok = peer == null
                    ? RotLink.ToAll(RotLinkRules.WorldChannel, key, seq, args, blob, out reason)
                    : RotLink.ToPlayer(peer, RotLinkRules.WorldChannel, key, seq, args, blob, out reason);
                if (!ok)
                    return "not sent (part " + (i + 1) + " of " + parts.Count + ": " + reason + ")";
                bytes += blob.Length;
            }
            return "sent in " + parts.Count + " part(s), " + bytes + " bytes" + (skipped > 0 ? ", " + skipped + " value(s) too long to send" : "");
        }

        private static void AddHero(List<string> entries, Hero hero)
        {
            entries.Add(RotSyncRules.Entry(RotSyncRules.HeroCulture, hero.StringId, hero.Culture?.StringId ?? ""));
            if (hero.VolunteerTypes != null)
                entries.Add(RotSyncRules.Entry(RotSyncRules.Volunteers, hero.StringId, RotSyncRules.VolunteerValue(hero.VolunteerTypes.Select(slot => slot?.StringId))));
            if (hero.CharacterObject != null)
                entries.Add(RotSyncRules.Entry(RotSyncRules.HeroRace, hero.StringId, hero.CharacterObject.Race.ToString(CultureInfo.InvariantCulture)));
        }

        // The settlements and heroes one outermost write can touch, with their values before it.
        // The heroes are listed before the write, since EvacuateHeroes moves the wanderers away.
        private sealed class Scope
        {
            private readonly List<Settlement> _settlements = new List<Settlement>();

            public List<Hero> Heroes { get; } = new List<Hero>();
            public Dictionary<string, string> Before { get; private set; }

            public static Scope Of(Settlement settlement)
            {
                var scope = new Scope();
                if (settlement != null)
                {
                    scope._settlements.Add(settlement);
                    foreach (var village in settlement.BoundVillages)
                    {
                        if (village?.Settlement != null)
                            scope._settlements.Add(village.Settlement);
                    }
                    foreach (var place in scope._settlements)
                        scope.Heroes.AddRange(place.Notables.Where(hero => hero != null));
                    scope.Heroes.AddRange(settlement.HeroesWithoutParty.Where(hero => hero != null && hero.IsWanderer));
                }
                scope.Before = scope.Read();
                return scope;
            }

            // Keyed by RotSyncRules.KeyOf.
            public Dictionary<string, string> Read()
            {
                var values = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var settlement in _settlements)
                {
                    if (settlement.StringId != null)
                        values[RotSyncRules.SettlementCulture + "\t" + settlement.StringId] = settlement.Culture?.StringId ?? "";
                }
                foreach (var hero in Heroes)
                {
                    if (hero.StringId == null)
                        continue;
                    values[RotSyncRules.HeroCulture + "\t" + hero.StringId] = hero.Culture?.StringId ?? "";
                    if (hero.VolunteerTypes != null)
                        values[RotSyncRules.Volunteers + "\t" + hero.StringId] = RotSyncRules.VolunteerValue(hero.VolunteerTypes.Select(slot => slot?.StringId));
                    if (hero.CharacterObject != null)
                        values[RotSyncRules.HeroRace + "\t" + hero.StringId] = hero.CharacterObject.Race.ToString(CultureInfo.InvariantCulture);
                }
                return values;
            }
        }

        // ---- player's PC -------------------------------------------------------------------------

        // A "set" or a snapshot part from the server, on the game thread. Any other key is ignored.
        public static void Received(string key, int seq, IList<string> rest, byte[] blob)
        {
            if (key != RotLinkRules.SetKey && key != RotLinkRules.SnapshotKey)
            {
                if (Said.Add(key ?? ""))
                    SubModule.Log(RotLinkRules.UnknownLine(RotLinkRules.WorldChannel, key));
                return;
            }
            var entries = RotSyncRules.Unpack(blob);
            var what = key == RotLinkRules.SnapshotKey
                ? "world snapshot " + seq + " part " + (rest.Count > 0 ? rest[0] : "?") + " of " + (rest.Count > 1 ? rest[1] : "?")
                : "world values from the server";
            if (Campaign.Current == null || MBObjectManager.Instance == null)
            {
                SubModule.Log(RotLinkRules.Log + what + " not applied: no campaign here (" + entries.Count + " values).");
                return;
            }
            int changed = 0, notFound = 0, unreadable = 0;
            foreach (var entry in entries)
            {
                if (!RotSyncRules.TryParse(entry, out var kind, out var id, out var value))
                {
                    unreadable++;
                    continue;
                }
                switch (Apply(kind, id, value))
                {
                    case 1: changed++; break;
                    case -1: notFound++; break;
                    case -2: unreadable++; break;
                }
            }
            SubModule.Log(RotLinkRules.Log + what + " applied: " + entries.Count + " values, " + changed + " changed here"
                + (notFound > 0 ? ", " + notFound + " not found here" : "") + (unreadable > 0 ? ", " + unreadable + " unreadable" : "") + ".");
        }

        // 1 changed, 0 already so, -1 an object not found here, -2 an unknown kind or value.
        private static int Apply(char kind, string id, string value)
        {
            switch (kind)
            {
                case RotSyncRules.SettlementCulture:
                {
                    var settlement = Settlement.Find(id);
                    var culture = MBObjectManager.Instance.GetObject<CultureObject>(value);
                    if (settlement == null || culture == null)
                        return -1;
                    if (settlement.Culture == culture)
                        return 0;
                    SettlementCultureField.SetValue(settlement, culture);
                    return 1;
                }
                case RotSyncRules.HeroCulture:
                {
                    var hero = Hero.Find(id);
                    var culture = MBObjectManager.Instance.GetObject<CultureObject>(value);
                    if (hero == null || culture == null)
                        return -1;
                    if (hero.Culture == culture)
                        return 0;
                    HeroCultureField.SetValue(hero, culture);
                    return 1;
                }
                case RotSyncRules.HeroRace:
                {
                    if (!RotSyncRules.TryRace(value, TaleWorlds.Core.FaceGen.GetRaceCount(), out var race) || RaceField == null)
                        return Unusable("race", RaceField == null ? "BasicCharacterObject has no Race field here" : null);
                    var character = Hero.Find(id)?.CharacterObject;
                    if (character == null)
                        return -1;
                    if (character.Race == race)
                        return 0;
                    RaceField.SetValue(character, race);
                    return 1;
                }
                case RotSyncRules.LocationScene:
                {
                    if (!RotSyncRules.TryParseSceneId(id, out var settlementId, out var locationId, out var level) || !RotSyncRules.IsSceneName(value) || SceneNamesField == null)
                        return Unusable("scene name", SceneNamesField == null ? "Location has no _sceneNames field here" : null);
                    var location = Settlement.Find(settlementId)?.LocationComplex?.GetLocationWithId(locationId);
                    if (location == null)
                        return -1;
                    var names = SceneNamesField.GetValue(location) as string[];
                    if (names == null || level >= names.Length)
                        return -2;
                    if (names[level] == value)
                        return 0;
                    location.SetSceneName(level, value);
                    return 1;
                }
                case RotSyncRules.ClanRemoved:
                {
                    if (value != RotSyncRules.Removed || ClansField == null)
                        return Unusable("clan list", ClansField == null ? "CampaignObjectManager has no _clans field here" : null);
                    var clan = MBObjectManager.Instance.GetObject<Clan>(id);
                    var list = ClansField.GetValue(Campaign.Current.CampaignObjectManager) as System.Collections.IList;
                    if (clan == null || list == null)
                        return -1;
                    if (!list.Contains(clan))
                        return 0;
                    list.Remove(clan);
                    return 1;
                }
                case RotSyncRules.Volunteers:
                {
                    var slots = RotSyncRules.VolunteerSlots(value);
                    if (slots == null)
                        return -2;
                    var hero = Hero.Find(id);
                    if (hero == null)
                        return -1;
                    var troops = new CharacterObject[slots.Length];
                    for (var i = 0; i < slots.Length; i++)
                    {
                        if (slots[i] == null)
                            continue;
                        troops[i] = MBObjectManager.Instance.GetObject<CharacterObject>(slots[i]);
                        if (troops[i] == null)
                            return -1;
                    }
                    var current = hero.VolunteerTypes;
                    if (current != null && current.Length == troops.Length)
                    {
                        var changed = 0;
                        for (var i = 0; i < troops.Length; i++)
                        {
                            if (current[i] == troops[i])
                                continue;
                            current[i] = troops[i];
                            changed = 1;
                        }
                        return changed;
                    }
                    VolunteersField.SetValue(hero, troops);
                    return 1;
                }
                default:
                    return -2;
            }
        }

        // A value this PC cannot apply: -2, and when this game lacks the field, one line per session.
        private static int Unusable(string what, string missing)
        {
            if (missing != null && Said.Add(what))
                SubModule.Log(RotLinkRules.Loud + what + " values from the server are not applied here: " + missing + " (said once).");
            return -2;
        }

        private sealed class SameObject : IEqualityComparer<object>
        {
            public new bool Equals(object x, object y) => ReferenceEquals(x, y);

            public int GetHashCode(object obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
        }
    }
}
