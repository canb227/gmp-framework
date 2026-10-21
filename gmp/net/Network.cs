using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// What a message is for; every message travels on one. The numbers go on the wire, so every peer must run a build
/// with the same ones.
/// </summary>
public enum Channel : byte
{
    /// <summary>A transport's own hello when it opens a link (Steam).</summary>
    NET_Handshake = 1,
    /// <summary>ENet's directory of peer addresses, so peers can mesh (ENetNetwork only, never handed to the game).</summary>
    NET_Directory = 2,

    LOBBY_GameInfo = 5,
    LOBBY_Roster = 6,
    LOBBY_Chat = 7,
    LOBBY_Control = 8,

    /// <summary>GMPObject state updates (GameWorld.StateSync.cs).</summary>
    GAME_State = 11,
    /// <summary>Junk traffic from the <c>net_flood</c> console command, for testing bandwidth; ignored on arrival.</summary>
    GAME_Flood = 14,
    /// <summary>Synced body states of the native world (GameWorld.NativeSync.cs).</summary>
    GAME_Items = 15,

    /// <summary>RPCs (<see cref="RPCManager"/>).</summary>
    RPC_Main = 16,
}

/// <summary>
/// A transport: how peers reach each other (<see cref="SteamNetwork"/> or <see cref="ENetNetwork"/>). <see cref="Lobby"/>
/// drives it and the rest of the game only talks to Lobby. Peers are known only by their peer id: how one is addressed
/// (an ip and port for ENet, nothing for Steam) is the transport's own business.
/// </summary>
public interface Network
{
    // Send flags, with Steam's values (ENet maps them to its own).
    public const int k_nSteamNetworkingSend_NoNagle = 1;
    public const int k_nSteamNetworkingSend_Unreliable = 0;
    public const int k_nSteamNetworkingSend_Reliable = 8;

    /// <summary>A message arrived (on the main thread, from <see cref="service"/>): sender, channel, bytes.</summary>
    public event Action<ulong, Channel, byte[]> MessageReceivedEvent;

    /// <summary>A direct link to a peer is up and its identity is known.</summary>
    public event Action<ulong> PeerConnectedEvent;

    /// <summary>The link to a peer is gone for good (it left, or timed out).</summary>
    public event Action<ulong> PeerDisconnectedEvent;

    /// <summary>
    /// A link that dropped without the peer leaving has been made again. The peer was never reported disconnected, but
    /// anything sent to or from it in the gap was lost.
    /// </summary>
    public event Action<ulong> PeerRepairedEvent;

    /// <summary>Sends to one peer; sending to this peer's own id delivers it straight back, before this returns.</summary>
    public Error Send(ulong peerID, Channel ch, byte[] msg, int SENDFLAGS = k_nSteamNetworkingSend_Reliable);

    /// <summary>Sends the same message to each of <paramref name="peerIDs"/> (this peer's own id included, if listed).</summary>
    public Error Broadcast(List<ulong> peerIDs, Channel ch, byte[] msg, int SENDFLAGS = k_nSteamNetworkingSend_Reliable);

    /// <summary>Starts listening so other peers can link to this one (both the host and joiners do).</summary>
    public Error Host();

    /// <summary>Opens a link to a peer; <see cref="PeerConnectedEvent"/> fires once it's up.</summary>
    public Error Connect(ulong peerID);

    /// <summary>Closes every link and stops listening.</summary>
    public Error Disconnect();

    /// <summary>Called by Lobby every frame: hands out what has arrived (raising the events above).</summary>
    public void service();
}
