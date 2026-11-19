// Coodsz Settings Sync messages. This one file is compiled into both CoodszSettingsSync.Coop builds (the
// server one in DedicatedServer.CoodszTools, the client one in DedicatedServer.CoodszJoinPassword).
// Parts adapted from ModderLords by Ryvius (@PlueRyvius), used with permission.
// The request and snapshot message shapes and the wire note follow his.
//
// WIRE NOTE: Coop gives each message its wire id from a hash of the type's full name, so the namespace and
// the class names below are the protocol between the server and the players. Never rename or move them; add
// new members with new tags and bump SettingsWire.ProtocolVersion when old members change meaning.
//
// The server to client messages implement ICommand only. Coop v0.1.7 also had IServerToClientCommand, which
// only made its server drop such a message coming from a player; Coop removed it on 2026-10-30 (d46eddef5),
// so a reference to it would not load on newer Coop. ICommand is in both, and the wire id comes from the type
// name, so old and new add ons still understand each other. The server only ever handles
// NetworkModSettingsRequest, so a player sending one of the others to the server reaches no handler.
#nullable enable
using System.Collections.Generic;
using Common.Messaging;
using ProtoBuf;

namespace CoodszSettingsSync.Messages
{
    /// <summary>Client to server: send me the host's mod settings (sent once Coop has checked the modules).</summary>
    [ProtoContract(SkipConstructor = true)]
    public sealed class NetworkModSettingsRequest : ICommand
    {
        [ProtoMember(1)] public int ProtocolVersion { get; set; }

        /// <summary>
        /// This add on also takes TAOM's War of the Ring state (NetworkWarOfTheRingSnapshot). An older add on
        /// leaves it false, so the server never sends it a message type it does not know.
        /// </summary>
        [ProtoMember(2)] public bool WantsWarOfTheRing { get; set; }

        /// <summary>
        /// This add on also shows the PvP response timer's countdown (NetworkPvpCountdown and NetworkPvpCountdownStop).
        /// An older add on leaves it false, so the server never sends it those message types.
        /// </summary>
        [ProtoMember(3)] public bool WantsPvpCountdown { get; set; }

        /// <summary>
        /// This add on takes NetworkModSettingsNone when the host has no mod settings to share. An older add on
        /// leaves it false, so the server never sends it that message type.
        /// </summary>
        [ProtoMember(4)] public bool WantsNoSettingsAnswer { get; set; }
    }

    /// <summary>
    /// Server to client: the answer to a request when the host has no mod settings to share, so the player's
    /// game stops waiting. Only sent to players whose request set WantsNoSettingsAnswer.
    /// </summary>
    [ProtoContract(SkipConstructor = true)]
    public sealed class NetworkModSettingsNone : ICommand
    {
        [ProtoMember(1)] public int ProtocolVersion { get; set; }
    }

    /// <summary>
    /// Server to client: the host's values for one MCM settings object. The server has no handler for it, so a
    /// player can never push settings to the host.
    /// </summary>
    [ProtoContract(SkipConstructor = true)]
    public sealed class NetworkModSettingsSnapshot : ICommand
    {
        [ProtoMember(1)] public int ProtocolVersion { get; set; }

        /// <summary>MCM's settings id.</summary>
        [ProtoMember(2)] public string SettingsId { get; set; } = "";

        /// <summary>A <see cref="SettingsScope"/> value: Global or PerCampaign.</summary>
        [ProtoMember(3)] public int Scope { get; set; }

        /// <summary>Value lines, see <see cref="SettingsWire.Encode"/>.</summary>
        [ProtoMember(4)] public List<string>? Values { get; set; }

        /// <summary>True when the server read the values from its settings files instead of the live MCM.</summary>
        [ProtoMember(5)] public bool FromFile { get; set; }
    }

    /// <summary>
    /// Server to client: TAOM's War of the Ring phase, outcome and momentum save data, sent to players whose
    /// request set WantsWarOfTheRing, when they join and whenever it changes. See WotrWire for the limits.
    /// </summary>
    [ProtoContract(SkipConstructor = true)]
    public sealed class NetworkWarOfTheRingSnapshot : ICommand
    {
        [ProtoMember(1)] public int ProtocolVersion { get; set; }
        [ProtoMember(2)] public int Phase { get; set; }
        [ProtoMember(3)] public int Outcome { get; set; }

        /// <summary>The momentum store's save keys; Values holds the value at the same index.</summary>
        [ProtoMember(4)] public List<string>? Keys { get; set; }
        [ProtoMember(5)] public List<string>? Values { get; set; }
    }

    /// <summary>
    /// Server to client: the PvP response timer started for a meeting this player is in. The player's game counts
    /// down by itself from SecondsLeft, so nothing more is sent each second. Only sent to players whose request set
    /// WantsPvpCountdown. See PvpCountdownWire for the limits.
    /// </summary>
    [ProtoContract(SkipConstructor = true)]
    public sealed class NetworkPvpCountdown : ICommand
    {
        [ProtoMember(1)] public int ProtocolVersion { get; set; }

        /// <summary>Coop's session id for the meeting.</summary>
        [ProtoMember(2)] public string SessionId { get; set; } = "";

        /// <summary>Whole seconds left when the server sent it.</summary>
        [ProtoMember(3)] public int SecondsLeft { get; set; }

        /// <summary>1 for the caught player, 2 for the player who caught them (PvpCountdownRole).</summary>
        [ProtoMember(4)] public int Role { get; set; }
    }

    /// <summary>Server to client: the countdown for this meeting is over (an answer, Leave, a timeout or another end, peace, the attacker's turn, or the timer turned off).</summary>
    [ProtoContract(SkipConstructor = true)]
    public sealed class NetworkPvpCountdownStop : ICommand
    {
        [ProtoMember(1)] public int ProtocolVersion { get; set; }
        [ProtoMember(2)] public string SessionId { get; set; } = "";
    }
}
