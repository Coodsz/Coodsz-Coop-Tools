// Coodsz Settings Sync, server side: how often a player may ask for the host's settings. A player asks
// once per join; anything faster is dropped, so a misbehaving client cannot make the server read and send
// every settings object over and over.
//
// Engine free; the launcher's tests compile this file as is.
#nullable enable
using System;
using System.Collections.Generic;

namespace CoodszSettingsSync
{
    public sealed class RequestGate<TPeer> where TPeer : notnull
    {
        public static readonly TimeSpan Interval = TimeSpan.FromSeconds(10);
        public const int MaxQueued = 64;

        private readonly Dictionary<int, DateTime> _lastAsked = new Dictionary<int, DateTime>();
        private readonly HashSet<int> _queuedIds = new HashSet<int>();
        private readonly List<TPeer> _queue = new List<TPeer>();
        private readonly Func<TPeer, int> _id;
        private readonly object _gate = new object();

        public RequestGate(Func<TPeer, int> id) => _id = id;

        public int Queued { get { lock (_gate) return _queue.Count; } }

        /// <summary>
        /// Queues a request: one per player every 10 seconds, at most one waiting per player, and at most 64
        /// waiting in all. Returns false when it was dropped. onQueued runs only for a queued request, inside the
        /// same lock TakeAll takes, so whatever it notes for the request is there before TakeAll can return it.
        /// </summary>
        public bool TryQueue(TPeer peer, DateTime now, Action? onQueued = null)
        {
            int id = _id(peer);
            lock (_gate)
            {
                if (_queuedIds.Contains(id) || _queue.Count >= MaxQueued) return false;
                if (_lastAsked.TryGetValue(id, out DateTime last) && now - last < Interval) return false;
                _lastAsked[id] = now;
                onQueued?.Invoke();
                _queuedIds.Add(id);
                _queue.Add(peer);
                // Old entries are dropped so the table stays small on a long running server.
                if (_lastAsked.Count > 1024)
                {
                    var stale = new List<int>();
                    foreach (KeyValuePair<int, DateTime> pair in _lastAsked)
                        if (now - pair.Value >= Interval) stale.Add(pair.Key);
                    foreach (int key in stale) _lastAsked.Remove(key);
                }
                return true;
            }
        }

        /// <summary>Takes every waiting request.</summary>
        public List<TPeer> TakeAll()
        {
            lock (_gate)
            {
                var taken = new List<TPeer>(_queue);
                _queue.Clear();
                _queuedIds.Clear();
                return taken;
            }
        }

        public void Clear()
        {
            lock (_gate)
            {
                _queue.Clear();
                _queuedIds.Clear();
                _lastAsked.Clear();
            }
        }
    }
}
