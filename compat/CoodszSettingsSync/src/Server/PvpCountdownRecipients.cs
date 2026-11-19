// Coodsz Settings Sync, server side: the players whose client add on asked for the PvP countdown. A player
// leaves the list when Coop says they disconnected and whenever they are found not connected, so a later
// player can never take over a leaver's place, even if LiteNetLib hands out the same peer object again.
//
// Engine free; the launcher's tests compile this file as is.
#nullable enable
using System;
using System.Collections.Generic;

namespace CoodszSettingsSync
{
    public sealed class PvpCountdownRecipients<TPeer> where TPeer : class
    {
        private readonly List<TPeer> _peers = new List<TPeer>();
        private readonly Func<TPeer, bool> _connected;
        private readonly int _most;
        private readonly object _gate = new object();

        public PvpCountdownRecipients(Func<TPeer, bool> connected, int most)
        {
            _connected = connected;
            _most = most;
        }

        public int Count { get { lock (_gate) return _peers.Count; } }

        /// <summary>A player whose request asked for the countdown. Players no longer connected are dropped first.</summary>
        public bool Add(TPeer peer)
        {
            lock (_gate)
            {
                _peers.RemoveAll(p => !_connected(p));
                if (!_connected(peer) || Contains(peer)) return false;
                if (_peers.Count >= _most) return false;
                _peers.Add(peer);
                return true;
            }
        }

        /// <summary>Coop's PlayerDisconnected for this player.</summary>
        public void Remove(TPeer peer)
        {
            lock (_gate) _peers.RemoveAll(p => ReferenceEquals(p, peer));
        }

        /// <summary>True when the player asked and is still connected; a player found not connected leaves the list.</summary>
        public bool Wants(TPeer peer)
        {
            lock (_gate)
            {
                if (!Contains(peer)) return false;
                if (_connected(peer)) return true;
                _peers.RemoveAll(p => ReferenceEquals(p, peer));
                return false;
            }
        }

        public void Clear()
        {
            lock (_gate) _peers.Clear();
        }

        private bool Contains(TPeer peer)
        {
            foreach (TPeer p in _peers)
                if (ReferenceEquals(p, peer)) return true;
            return false;
        }
    }
}
