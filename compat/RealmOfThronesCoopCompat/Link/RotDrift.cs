#nullable disable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;

namespace RealmOfThronesCoopCompat
{
    // The world drift probe (plan item S70). Once per in game day the server sends a digest of its
    // world on rot.probe: the wars between kingdoms and clans outside a kingdom (bandits left out),
    // the owner clan and culture of every town and castle, each Realm of Thrones story event's state
    // and the clan and kingdom counts (RotSyncRules). A player's PC builds the same digest from its
    // own world when it arrives and logs one line: no difference, or the differences. Until the
    // story's popups reach players (S41) the story events differ once an event ran on the server.
    // The first day the server sees only sets the day; the first digest goes at the next day change.
    // A compare that finds differences looks again about 5 seconds later and reports only what is
    // still different, "(after a second look)": Coop's own messages of that moment may not have
    // landed yet.
    internal static class RotDrift
    {
        private const BindingFlags AnyInstance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags AnyStatic = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        // The digest unpacked; far above the few dozen KB it is.
        private const int MaxUnpackedBytes = 1024 * 1024;

        private static int _lastDay = -1;
        private static bool _tooBigSaid;

        // From RotLink's tick on the server in a session.
        public static void ServerTick()
        {
            // Every frame: the day is cheap to read, Coop's world state only on a new day.
            if (Campaign.Current == null)
                return;
            var day = (int)CampaignTime.Now.ToDays;
            if (day == _lastDay || !Coop.WorldLive)
                return;
            var first = _lastDay < 0 || day < _lastDay;
            _lastDay = day;
            if (first)
                return;
            try
            {
                var entries = Build();
                var blob = RotSyncRules.Compress(entries);
                if (blob.Length > RotSyncRules.MaxDigestBytes)
                {
                    if (!_tooBigSaid)
                        SubModule.Log(RotLinkRules.Loud + "drift probe day " + day + " not sent: " + blob.Length + " bytes packed, over " + RotSyncRules.MaxDigestBytes + " (said once).");
                    _tooBigSaid = true;
                    return;
                }
                if (RotLink.ToAll(RotLinkRules.ProbeChannel, RotLinkRules.DayKey, day, null, blob, out _))
                    SubModule.Log(RotSyncRules.DriftSentLine(day, entries.Count, blob.Length));
            }
            catch (Exception ex)
            {
                SubModule.Log(RotLinkRules.Loud + "drift probe day " + day + " could not be built: " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        public static void SessionEnded()
        {
            _lastDay = -1;
            _pending = null;
        }

        // On a player's PC, on the game thread, in order with Coop's world messages before it.
        public static void Received(int day, byte[] blob)
        {
            var server = RotSyncRules.Decompress(blob, MaxUnpackedBytes);
            if (server == null)
            {
                SubModule.Log(RotLinkRules.Loud + "drift probe day " + day + " could not be read (" + (blob?.Length ?? 0) + " bytes).");
                return;
            }
            if (Campaign.Current == null || !Coop.WorldLive)
            {
                SubModule.Log(RotLinkRules.Log + "drift probe day " + day + " not compared: the server's world is not loaded here yet.");
                return;
            }
            _pending = null;
            Compare(day, server, false);
        }

        // A digest that showed differences, kept for one more compare (RotSyncRules.SecondLookMs).
        private static List<string> _pending;
        private static int _pendingDay;
        private static int _firstLook;
        private static long _pendingDue;
        private static readonly System.Diagnostics.Stopwatch Clock = System.Diagnostics.Stopwatch.StartNew();

        // From RotLink's tick on a player's PC, on the game thread.
        public static void ClientTick()
        {
            if (_pending == null || !RotSyncRules.SecondLookDue(Clock.ElapsedMilliseconds, _pendingDue))
                return;
            var server = _pending;
            _pending = null;
            if (Campaign.Current == null || !Coop.WorldLive)
            {
                SubModule.Log(RotLinkRules.Log + "drift probe day " + _pendingDay + " not compared again: the server's world is not loaded here any more.");
                return;
            }
            Compare(_pendingDay, server, true);
        }

        private static void Compare(int day, List<string> server, bool secondLook)
        {
            var hereDay = (int)CampaignTime.Now.ToDays;
            if (hereDay != day)
            {
                SubModule.Log(RotSyncRules.DriftDayLine(day, hereDay));
                return;
            }
            var differences = RotSyncRules.Differences(server, Build());
            if (RotSyncRules.LooksAgain(differences.Count, secondLook))
            {
                _pending = server;
                _pendingDay = day;
                _firstLook = differences.Count;
                _pendingDue = Clock.ElapsedMilliseconds + RotSyncRules.SecondLookMs;
                return;
            }
            SubModule.Log(RotSyncRules.DriftLine(day, server.Count, differences, secondLook, _firstLook));
        }

        // The same digest on both sides, from the world as it is now.
        private static List<string> Build()
        {
            var entries = new List<string>();
            foreach (var settlement in Settlement.All)
            {
                if (settlement == null || !(settlement.IsTown || settlement.IsCastle))
                    continue;
                entries.Add(RotSyncRules.Entry(RotSyncRules.Owner, settlement.StringId, settlement.OwnerClan?.StringId ?? "none"));
                entries.Add(RotSyncRules.Entry(RotSyncRules.FortCulture, settlement.StringId, settlement.Culture?.StringId ?? "none"));
            }
            var factions = Kingdom.All.Where(kingdom => kingdom != null && !kingdom.IsEliminated).Cast<IFaction>()
                .Concat(Clan.All.Where(clan => clan != null && !clan.IsEliminated && clan.Kingdom == null && !clan.IsBanditFaction))
                .ToList();
            for (var i = 0; i < factions.Count; i++)
            {
                for (var j = i + 1; j < factions.Count; j++)
                {
                    if (factions[i].IsAtWarWith(factions[j]))
                        entries.Add(RotSyncRules.WarEntry(factions[i].StringId, factions[j].StringId));
                }
            }
            entries.Add(RotSyncRules.Entry(RotSyncRules.Count, "clans", Clan.All.Count + " all, " + Clan.All.Count(clan => clan != null && !clan.IsEliminated) + " alive"));
            entries.Add(RotSyncRules.Entry(RotSyncRules.Count, "kingdoms", Kingdom.All.Count(kingdom => kingdom != null && !kingdom.IsEliminated) + " alive"));
            AddStoryEvents(entries);
            return entries;
        }

        // ROT.SubModule.EventBehavior's events (Realm of Thrones loads them on both sides), each with
        // EventBase.IsActive and TotalEvents, which its save keeps. One "*" entry when unreadable.
        private static void AddStoryEvents(List<string> entries)
        {
            try
            {
                var behavior = Coop.FindType(RotHostTables.SubModuleType)?.GetField("EventBehavior", AnyStatic)?.GetValue(null);
                var events = behavior?.GetType().GetField("_events", AnyInstance)?.GetValue(behavior) as IEnumerable;
                if (events == null)
                {
                    entries.Add(RotSyncRules.Entry(RotSyncRules.StoryEvent, RotSyncRules.Unreadable, "no ROTEventBehavior"));
                    return;
                }
                foreach (var item in events)
                {
                    if (item == null)
                        continue;
                    var type = item.GetType();
                    var active = type.GetProperty("IsActive", AnyInstance)?.GetValue(item, null);
                    var total = type.GetProperty("TotalEvents", AnyInstance)?.GetValue(item, null);
                    entries.Add(RotSyncRules.Entry(RotSyncRules.StoryEvent, type.Name, (active is bool on && on ? "active" : "idle") + "/" + (total ?? "?")));
                }
            }
            catch (Exception ex)
            {
                entries.RemoveAll(entry => entry.Length > 0 && entry[0] == RotSyncRules.StoryEvent);
                entries.Add(RotSyncRules.Entry(RotSyncRules.StoryEvent, RotSyncRules.Unreadable, ex.GetType().Name));
            }
        }
    }
}
