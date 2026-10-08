using Godot;
using System.Collections.Generic;

public enum Channel : byte
{
    ERROR = 0,

    NET_Handshake = 1,
    NET_tba = 2,
    NET_tba2 = 3,
    NET_tba3 = 4,

    LOBBY_GameInfo = 5,
    LOBBY_Roster = 6,
    LOBBY_Chat = 7,
    LOBBY_Control = 8,
    LOBBY_tba = 9,

    GAME_Input = 10,
    GAME_State = 11,
    GAME_Voice = 12,
    GAME_Effects = 13,
    GAME_tba2 = 14,
    GAME_tba3 = 15,

    RPC_Main = 16,

    CHANNEL_MAX = 254
}

public interface Network
{

    public const int k_nSteamNetworkingSend_NoNagle = 1;
    public const int k_nSteamNetworkingSend_NoDelay = 4;
    public const int k_nSteamNetworkingSend_Unreliable = 0;
    public const int k_nSteamNetworkingSend_Reliable = 8;


    public delegate void MessageReceived(ulong from, Channel ch, byte[] msg);
    public event MessageReceived MessageReceivedEvent;

    // Raised by the transport once a direct link to a peer is established and the
    // peer's identity is known. Reported purely by peerID — how a peer is addressed
    // (ip/port for ENet, nothing for Steam) is the transport's own concern and never
    // surfaces here, so Lobby can manage roster/mesh membership by peerID alone.
    public delegate void PeerConnected(ulong peerID);
    public delegate void PeerDisconnected(ulong peerID);
    public event PeerConnected PeerConnectedEvent;
    public event PeerDisconnected PeerDisconnectedEvent;

    public Error Send(ulong peerID, Channel ch, byte[] msg, int SENDFLAGS = k_nSteamNetworkingSend_Reliable);
    public Error Broadcast(List<ulong> peerIDs, Channel ch, byte[] msg, int SENDFLAGS = k_nSteamNetworkingSend_Reliable);
    public Error Host();
    public Error Connect(ulong peerID);
    public Error Disconnect();
    public void service();
}
