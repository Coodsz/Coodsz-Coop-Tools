Coodsz.CoopLink.dll
===================

One small library our compat modules use to send their own data between the Coop server and the players'
PCs, over Coop's own network. Coop has no mod API for this; the link rides on Coop's public message broker
and network object. It is not a submodule: a mod ships it in its own bin folders (Win64_Shipping_Client and
Win64_Shipping_Server) and loads it by hand.

The rule that never changes
---------------------------
The public API is additive from version 1 on. No public member is ever changed or removed, only added.
When two mods ship this DLL the first copy loaded wins, and its assembly version is always 1.0.0.0, so a
mod built against a newer copy reads Link.Version first and only calls what that version has.
Two builds of the same source are not byte identical (a build in another folder records another path,
and the Coop DLLs it was built against can differ), so never compare the DLL files or their hashes to tell
copies apart: compare Link.Version.
The two wire types, Coodsz.CoopLink.ToClient and Coodsz.CoopLink.ToServer, keep their names and member
numbers forever. New features are new channels, never new types.

Loading
-------
1. Compile src\LinkProbe.cs into your mod as source (it names no Coop type). If your mod also references
   the DLL, the compiler warns CS0436 (the same public class twice) and uses your source copy. That is
   what you want: suppress it in the file that names the link (#pragma warning disable CS0436).
2. From a game tick (not inside an AssemblyLoad callback, not while Coop builds its session), call
   LinkProbe.Check(). An empty list means every Coop member the link needs is there. A list that starts
   with "Coop's Common assembly" means Coop is not loaded yet, so ask again on a later tick. Any other
   list: log it and leave your link features off. The link DLL is never loaded in that case, which is why
   "probe failed" is answered by the probe and not by Link.State.
3. LinkProbe.Load(yourBinFolder, log) returns the link assembly: the copy another mod already loaded, else
   yours, else one found by simple name. Then call Link.Start(). Keep the code that names Link types in a
   class of its own that you touch only after this, so your submodule DLL never needs the link to load.

Calls (all static on Coodsz.CoopLink.Link; none throws, a failure is false plus one line through Log)
-----------------------------------------------------------------------------------------------
int Version                                  API level of the loaded copy (1).
Action<string> Log                           Add yours with +=.
Func<object> NetworkProvider                 Returns Coop's INetwork for this session, or null. Used by the
                                             built in handshake. Resolve it fresh each call.
bool UseCoopGameThread                       Default true, see Threading.
bool Start()                                 Subscribes on Coop's broker. A second call does nothing.
LinkState State                              NotStarted, StartFailed, Started, InSession, PeerAnswered.
PeerLinkState PeerState                      NoSession, Waiting, Answered, NoAnswer (client: the server;
                                             server: Answered once any player said hello).
int ServerLinkVersion                        Client: the server's link version, 0 until it answered.
int GetPeerLinkVersion(object peer)          Server: that player's link version, 0 if unknown.
void SessionStarted() / SessionEnded()       Tell the link when your own session detection fires.
event Action<bool> SessionChanged            True on start, false on end, raised by the calls above.
bool Register(prefix, Action<LinkMessage>)   Game thread callback for channels starting with prefix
                                             (for example "rot." or "chaos."). Longest prefix wins. Registering
                                             a prefix again replaces it. "link." is the link's own.
bool RegisterOnNetworkThread(prefix, Func<LinkMessage, bool>)
                                             Runs on Coop's network thread before the message is queued.
                                             Return true to take it. Only to store an immutable object or to
                                             answer with ToPlayer(immediate: true). Never touch the game here.
int Drain(int maxItems)                      Hands queued messages to their callbacks, oldest first.
bool ToServer(network, channel, key, seq, args, blob)          Player's PC to the server.
bool ToAll(network, channel, key, seq, args, blob)             Server to every connected player.
bool ToPlayer(network, peer, immediate, channel, key, seq, args, blob)   Server to one player.
bool TrySubscribeByName(fullTypeName, key)   Forwards a Coop message on channel "link.event" with your key and
                                             the message's source as Sender. Events are routed by key, so the
                                             key must start with your prefix. False while the type is not
                                             loaded yet: call it again later. Calling again is quiet: a
                                             missing type and a failed subscribe are logged once per
                                             name, a type is subscribed once and the same key is added
                                             (and logged) once.
int UnknownChannelCount                      Messages no prefix matched (the first 64 unknown routes are logged, then one line says more came).

"network" is Coop's Common.Network.INetwork for the current session, passed as object on every call
(GameInterface.ContainerProvider.TryResolve<INetwork>). Never keep it across sessions.
LinkMessage has Version, Channel, Key, Seq, Args (never null), Blob (never null), Sender and FromServer.
Sender is the LiteNetLib NetPeer that sent it: on the server the player's peer (map it to a player with
Coop's IPlayerManager), on a player's PC the server's peer. It is kept through the queue.

What each send is in Coop
-------------------------
ToServer on a player's PC       INetwork.SendAll(message). Refused on the server.
ToAll on the server             INetwork.SendAll(message), which goes through Coop's join gate per player.
ToPlayer immediate: false       INetwork.Send(peer, message), through the join gate: dropped while that player
                                validates or creates a character, held and replayed in order once the join
                                save snapshot is taken, passed straight after the player is in.
ToPlayer immediate: true        INetwork.SendImmediate(peer, message), skips the gate (what Coop uses for its
                                own handshake). Use it for answers a joining player needs before the save.
Something a late joiner needs: put it in your campaign behaviour's save data (the join save carries it) or
send it again when the player enters the campaign.

Threading
---------
Call Link.Drain(64) or so from your submodule's OnApplicationTick on the game thread, every frame. Callbacks
from Register then run on the game thread in the order the messages arrived. When Coop has
GameThread.RunSafe (it does today) and UseCoopGameThread is true, the link also hands each message to Coop's
own game thread queue, so your callback runs in order with Coop's game thread work for the messages before
it; Drain picks up whatever that queue did not run. Either way callbacks run on the game thread, oldest
first, each one once. Send from the game thread; from a network thread callback only answer with
ToPlayer(immediate: true) or ToServer.

Limits
------
One message: channel, key and every arg as UTF-8 plus the blob, at most 256 KB (LinkRules.MaxMessageBytes),
and at most 4096 args. A bigger one is refused with a log line, never split. Above that, split it into
several messages yourself (Seq helps) or send less.
Why 256 KB: Coop sends a message too big for one datagram bare on LiteNetLib's reliable ordered channel 0
(Coop.Core\Common\Network\CoopNetworkBase.cs, ReliableMessageBatcher), and LiteNetLib splits it into at
most 65535 fragments of 1014 bytes or more (about 63 MB). 256 KB is about 260 fragments: it shares channel 0
with world sync, so far bigger messages would hold the world stream back and count toward Coop's overload
pause at 10000 queued packets (NetworkConfig.MaxPacketsInQueue). Coop has no message size check of its own.
The receive queue holds at most 20000 messages; past that new ones are dropped with one log line.

Handshake
---------
After SessionStarted a player's PC says hello on channel "link.hello" with its version (also at once when
Coop's module check answer arrives) and repeats every 3 seconds, up to 20 times, until the server answers.
The server answers with its version straight away (SendImmediate). Any link message from the other side
also counts as an answer. NetworkProvider must be set on both sides for this.

Example
-------
    // In your submodule, after LinkProbe.Check() was empty and LinkProbe.Load(...) returned the assembly:
    Link.Log += line => MyLog.Write(line);
    Link.NetworkProvider = () => MyCoop.Resolve("Common.Network.INetwork");
    Link.Start();
    Link.Register("rot.", m => { if (m.Key == "choice") Story.Apply(m.Sender, m.Seq, m.Args[0]); });
    Link.TrySubscribeByName("Coop.Core.Server.Connections.Messages.NetworkPlayerCampaignEntered", "rot.entered");
    // Every frame, on the game thread:
    Link.Drain(64);
    // Server: open a popup for everyone.
    Link.ToAll(MyCoop.Resolve("Common.Network.INetwork"), "rot.event", "open", seq, new[] { "DragonSighted" }, null);
