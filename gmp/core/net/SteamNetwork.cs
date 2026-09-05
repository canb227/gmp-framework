using Godot;
using Steamworks;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

public class SteamNetwork :  Network
{
         // Channel enum ids 0..15
    private const int MaxRecvPerChannel = 64;

    private bool _started;
    private readonly HashSet<ulong> _connected = new();
    private readonly IntPtr[] _msgPtrs = new IntPtr[MaxRecvPerChannel];

    private Callback<SteamNetworkingMessagesSessionRequest_t> _onSessionRequest;
    private Callback<SteamNetworkingMessagesSessionFailed_t> _onSessionFailed;

    public event Network.MessageSent MessageSentEvent;
    public event Network.MessageReceived MessageReceivedEvent;
    public event Network.PeerConnected PeerConnectedEvent;
    public event Network.PeerDisconnected PeerDisconnectedEvent;

    private static ulong SelfId => SteamUser.GetSteamID().m_SteamID;

    // No socket to bind — just wire up session callbacks and ensure the relay is up.
    public Error Host()
    {
        if (!Global.bIsSteamConnected)
        {
            Logging.Error("cannot start Steam networking: Steam not connected", "NetworkSession");
            return Error.Unavailable;
        }
        _onSessionRequest = Callback<SteamNetworkingMessagesSessionRequest_t>.Create(OnSessionRequest);
        _onSessionFailed = Callback<SteamNetworkingMessagesSessionFailed_t>.Create(OnSessionFailed);
        SteamNetworkingUtils.InitRelayNetworkAccess();
        _started = true;
        Logging.Log($"Steam messaging ready (SteamID {SelfId})", "NetworkSession");
        return Error.Ok;
    }

    // SteamIDs are directly addressable, so there is nothing to map.
    public void AddPeerIDToIPMapping(ulong peerID, string ip, int port) { }

    // Open a session to a peer by SteamID. We nudge it with a handshake; the peer
    // accepts the session and replies, at which point both sides fire PeerConnected.
    public Error Connect(ulong peerID)
    {
        if (peerID == SelfId || peerID == 0)
            return Error.Ok;
        return SendRaw(peerID, (int)Channel.NET_Handshake, new byte[] { 1 }, Network.k_nSteamNetworkingSend_Reliable);
    }

    public Error Disconnect()
    {
        foreach (ulong id in _connected)
        {
            var ident = IdentityFor(id);
            SteamNetworkingMessages.CloseSessionWithUser(ref ident);
        }
        _connected.Clear();
        _onSessionRequest?.Dispose(); _onSessionRequest = null;
        _onSessionFailed?.Dispose(); _onSessionFailed = null;
        _started = false;
        return Error.Ok;
    }

    public Error Send(ulong peerID, Channel ch, byte[] msg, int SENDFLAGS = Network.k_nSteamNetworkingSend_Reliable)
    {
        return SendRaw(peerID, (int)ch, msg, SENDFLAGS);
    }

    public Error Broadcast(List<ulong> peerIDs, Channel ch, byte[] msg, int SENDFLAGS = Network.k_nSteamNetworkingSend_Reliable)
    {
        Error result = Error.Ok;
        foreach (ulong id in peerIDs)
            if (Send(id, ch, msg, SENDFLAGS) != Error.Ok)
                result = Error.Failed;
        return result;
    }

    public void service()
    {
        if (!_started)
            return;
        SteamAPI.RunCallbacks(); // pretty sure I don't need this, test later
        foreach (byte ch in Enum.GetValues(typeof(Channel)))
            ReceiveOn(ch);
    }

    // ---- internals -------------------------------------------------------------

    private Error SendRaw(ulong peerID, int channel, byte[] msg, int sendFlags)
    {
        var ident = IdentityFor(peerID);
        IntPtr buf = Marshal.AllocHGlobal(msg.Length);
        try
        {
            Marshal.Copy(msg, 0, buf, msg.Length);
            EResult r = SteamNetworkingMessages.SendMessageToUser(ref ident, buf, (uint)msg.Length, sendFlags, channel);
            if (r != EResult.k_EResultOK)
            {
                Logging.Error($"SendMessageToUser({peerID}) -> {r}", "NetworkWire");
                return Error.Failed;
            }
            MessageSentEvent?.Invoke(peerID, (Channel)channel, msg);
            return Error.Ok;
        }
        finally
        {
            
            Marshal.FreeHGlobal(buf);
        }
    }

    private void ReceiveOn(int channel)
    {
        int count = SteamNetworkingMessages.ReceiveMessagesOnChannel(channel, _msgPtrs, MaxRecvPerChannel);
        for (int i = 0; i < count; i++)
        {
            SteamNetworkingMessage_t msg = SteamNetworkingMessage_t.FromIntPtr(_msgPtrs[i]);
            byte[] data = new byte[msg.m_cbSize];
            Marshal.Copy(msg.m_pData, data, 0, msg.m_cbSize);
            ulong from = msg.m_identityPeer.GetSteamID64();
            SteamNetworkingMessage_t.Release(_msgPtrs[i]);
            Dispatch(from, channel, data);
        }
    }

    private void Dispatch(ulong from, int channel, byte[] data)
    {
        if ((Channel)channel == Channel.NET_Handshake)
        {
            OnHandshake(from);
            return; // transport-internal
        }
        MessageReceivedEvent?.Invoke(from, (Channel)channel, data);
    }

    private void OnHandshake(ulong from)
    {
        if (_connected.Add(from))
        {
            Logging.Log($"linked to peer {from}", "NetworkSession");
            SendRaw(from, (int)Channel.NET_Handshake, new byte[] { 1 }, Network.k_nSteamNetworkingSend_Reliable); // reply
            PeerConnectedEvent?.Invoke(from);
        }
    }

    private void OnSessionRequest(SteamNetworkingMessagesSessionRequest_t e)
    {
        var ident = e.m_identityRemote;
        SteamNetworkingMessages.AcceptSessionWithUser(ref ident); // allow their messages through
        Logging.Log($"accepted session from {ident.GetSteamID64()}", "NetworkSession");
    }

    private void OnSessionFailed(SteamNetworkingMessagesSessionFailed_t e)
    {
        ulong id = e.m_info.m_identityRemote.GetSteamID64();
        if (_connected.Remove(id))
            PeerDisconnectedEvent?.Invoke(id);
    }

    private static SteamNetworkingIdentity IdentityFor(ulong steamId)
    {
        var ident = new SteamNetworkingIdentity();
        ident.SetSteamID64(steamId);
        return ident;
    }
}
