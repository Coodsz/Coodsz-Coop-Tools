// Coodsz Settings Sync, server side: what the players already on the server have been sent.
//
// Engine free; the launcher's tests compile this file as is.
#nullable enable
using System;
using System.Collections.Generic;

namespace CoodszSettingsSync
{
    /// <summary>
    /// The last snapshot of each settings id the players already on the server got from a broadcast. A join answer goes to
    /// the joining players only, so it never counts here: before it goes out, <see cref="TakeChanged"/> hands the change
    /// check what those players have not had yet, and the answer then sends the joining players everything.
    /// </summary>
    public sealed class SentLedger
    {
        private readonly Dictionary<string, SettingsSnapshotData> _sent = new Dictionary<string, SettingsSnapshotData>(StringComparer.Ordinal);

        /// <summary>The snapshots that differ from what the players on the server last got, now marked as sent to them.</summary>
        public List<SettingsSnapshotData> TakeChanged(IEnumerable<SettingsSnapshotData> snapshots)
        {
            var changed = new List<SettingsSnapshotData>();
            foreach (SettingsSnapshotData snapshot in snapshots)
            {
                if (_sent.TryGetValue(snapshot.SettingsId, out SettingsSnapshotData? previous) && previous.SameAs(snapshot)) continue;
                _sent[snapshot.SettingsId] = snapshot;
                changed.Add(snapshot);
            }
            return changed;
        }

        public void Clear() => _sent.Clear();
    }
}
