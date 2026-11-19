// Coodsz Coop Link: the only two message types the link puts on Coop's wire.
//
// Their full names (Coodsz.CoopLink.ToClient and Coodsz.CoopLink.ToServer) and the member numbers below are
// the protocol forever. Coop finds a message type by a hash of its full name, so renaming or moving either
// class, or renumbering a member, breaks every player and server on an older copy. Add new members with new
// numbers only. Never add a third message type: new features are new channels.
#nullable enable
using System.Collections.Generic;
using Common.Messaging;
using ProtoBuf;

namespace Coodsz.CoopLink
{
    /// <summary>Server to players. Ignored by a process where Coop's ModInformation.IsServer is true.</summary>
    [ProtoContract(SkipConstructor = true)]
    public sealed class ToClient : ICommand
    {
        [ProtoMember(1)] public int Version { get; set; }
        [ProtoMember(2)] public string? Channel { get; set; }
        [ProtoMember(3)] public string? Key { get; set; }
        [ProtoMember(4)] public int Seq { get; set; }
        [ProtoMember(5)] public List<string>? Args { get; set; }
        [ProtoMember(6)] public byte[]? Blob { get; set; }
    }

    /// <summary>A player to the server. Ignored by a process where Coop's ModInformation.IsServer is false.</summary>
    [ProtoContract(SkipConstructor = true)]
    public sealed class ToServer : ICommand
    {
        [ProtoMember(1)] public int Version { get; set; }
        [ProtoMember(2)] public string? Channel { get; set; }
        [ProtoMember(3)] public string? Key { get; set; }
        [ProtoMember(4)] public int Seq { get; set; }
        [ProtoMember(5)] public List<string>? Args { get; set; }
        [ProtoMember(6)] public byte[]? Blob { get; set; }
    }
}
