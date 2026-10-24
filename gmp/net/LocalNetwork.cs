using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// A transport with nobody else on it: what this peer sends to itself comes straight back, and nothing else goes
/// anywhere. For a session that runs on this machine only (the main menu's backdrop, <see cref="MenuBackdrop"/>), so
/// spawns and RPCs work the same as in a hosted game.
/// </summary>
public class LocalNetwork : Network
{
    public event Action<ulong, Channel, byte[]> MessageReceivedEvent;
    // Never raised: no other peer ever links.
#pragma warning disable CS0067
    public event Action<ulong> PeerConnectedEvent;
    public event Action<ulong> PeerDisconnectedEvent;
    public event Action<ulong> PeerRepairedEvent;
#pragma warning restore CS0067

    readonly ulong selfPeerID;

    public LocalNetwork(ulong selfPeerID)
    {
        this.selfPeerID = selfPeerID;
    }

    public Error Send(ulong peerID, Channel ch, byte[] msg, int SENDFLAGS = Network.k_nSteamNetworkingSend_Reliable)
    {
        if (peerID == selfPeerID)
        {
            MessageReceivedEvent?.Invoke(peerID, ch, msg);
        }
        return Error.Ok;
    }

    public Error Broadcast(List<ulong> peerIDs, Channel ch, byte[] msg, int SENDFLAGS = Network.k_nSteamNetworkingSend_Reliable)
    {
        if (peerIDs.Contains(selfPeerID))
        {
            MessageReceivedEvent?.Invoke(selfPeerID, ch, msg);
        }
        return Error.Ok;
    }

    public Error Host() => Error.Ok;
    public Error Connect(ulong peerID) => Error.Ok;
    public Error Disconnect() => Error.Ok;
    public void service() { }
}
