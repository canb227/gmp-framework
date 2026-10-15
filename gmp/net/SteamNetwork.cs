using Godot;
using Steamworks;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

public class SteamNetwork :  Network
{
    /// <summary>Messages read per call; a channel is read until empty.</summary>
    private const int RecvBatchSize = 64;

    /// <summary>
    /// Bytes waiting to go out to one peer before every send fails: room for a whole restore (sent in 256 KB parts,
    /// GameWorld.Resync.cs) going out at once. State shouldn't wait here at all, since Steam's send rate stays above
    /// <see cref="GameWorld.SendBudget"/>; a bigger buffer would only hide it for longer if it did.
    /// </summary>
    private const int SendBufferSize = 32 * 1024 * 1024;
    /// <summary>
    /// Bytes / messages received from one peer and waiting to be read before Steam drops packets: a few seconds of
    /// traffic, to ride out a hitch. Must be bigger than the biggest message, 512 KB. Much more would only mean a pile
    /// of stale states to work through all at once after a long hitch.
    /// </summary>
    private const int RecvBufferSize = 16 * 1024 * 1024;
    private const int RecvBufferMessages = 10000;
    /// <summary>The biggest UDP payload Steam will send, bytes (also its default and maximum).</summary>
    private const int MaxPacketSize = 1300;

    private bool _started;
    private readonly HashSet<ulong> _connected = new();
    private readonly IntPtr[] _msgPtrs = new IntPtr[RecvBatchSize];

    private Callback<SteamNetworkingMessagesSessionRequest_t> _onSessionRequest;
    private Callback<SteamNetworkingMessagesSessionFailed_t> _onSessionFailed;

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
        // Values and limits are from Valve's GameNetworkingSockets source (csteamnetworkingsockets.cpp,
        // steamnetworkingsockets_snp.cpp); defaults in brackets. The buffers are limits, not memory set aside.
        // Steam sends to each peer at a fixed rate [256 KB/s] (it doesn't estimate bandwidth, so min and max must
        // match; at most 100 MB/s), spacing packets out evenly. Anything sent faster queues up, and once the send
        // buffer [512 KB] is full every send fails. Rate: state sync's whole budget plus a quarter, for Steam's packet
        // headers (about 8%) and everything else (RPCs, restores). State sync splits that budget between the peers
        // itself, so each connection only gets its share; the full rate per connection just lets a burst (a late
        // joiner's restore) through.
        int sendRate = GameWorld.SendBudget + GameWorld.SendBudget / 4;
        SetGlobalConfigInt(ESteamNetworkingConfigValue.k_ESteamNetworkingConfig_SendRateMin, sendRate);
        SetGlobalConfigInt(ESteamNetworkingConfigValue.k_ESteamNetworkingConfig_SendRateMax, sendRate);
        SetGlobalConfigInt(ESteamNetworkingConfigValue.k_ESteamNetworkingConfig_SendBufferSize, SendBufferSize);
        // A receiver drops packets once more than this many bytes [1 MB] or messages [1000] are waiting to be read.
        SetGlobalConfigInt(ESteamNetworkingConfigValue.k_ESteamNetworkingConfig_RecvBufferSize, RecvBufferSize);
        SetGlobalConfigInt(ESteamNetworkingConfigValue.k_ESteamNetworkingConfig_RecvBufferMessages, RecvBufferMessages);
        // Biggest UDP packet Steam sends [1300, also the maximum]; a message bigger than about 1127 bytes after Steam's
        // headers is split over several (GameWorld.MaxPacketSize stays under that).
        SetGlobalConfigInt(ESteamNetworkingConfigValue.k_ESteamNetworkingConfig_MTU_PacketSize, MaxPacketSize);
        // RecvMaxMessageSize [512 KB] stays: no peer can send a bigger message anyway.
        LogConfig();
        _started = true;
        Logging.Log($"Steam messaging ready (SteamID {SelfId})", "NetworkSession");
        return Error.Ok;
    }

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
        // Steam won't deliver a message you address to yourself, so loop it back in
        // process — matches ENetNetwork.Send() so Lobby.SendToAllAndSelf behaves the
        // same on both transports.
        if (peerID == SelfId)
        {
            MessageReceivedEvent?.Invoke(peerID, ch, msg);
            return Error.Ok;
        }
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
        foreach (Channel ch in AllChannels)
            ReceiveOn((byte)ch);
    }

    // Enum.GetValues allocates (and boxing each value in the foreach did too); this runs every frame.
    private static readonly Channel[] AllChannels = Enum.GetValues<Channel>();

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
            return Error.Ok;
        }
        finally
        {
            Marshal.FreeHGlobal(buf);
        }
    }

    private void ReceiveOn(int channel)
    {
        // Read until the channel is empty: a fixed number per frame caps how fast a peer can be heard, and the backlog
        // past that grows until Steam's receive buffer is full and it drops packets.
        int count = RecvBatchSize;
        while (count == RecvBatchSize)
        {
            count = SteamNetworkingMessages.ReceiveMessagesOnChannel(channel, _msgPtrs, RecvBatchSize);
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

    private static void SetGlobalConfigInt(ESteamNetworkingConfigValue setting, int value)
    {
        IntPtr arg = Marshal.AllocHGlobal(sizeof(int));
        try
        {
            Marshal.WriteInt32(arg, value);
            if (!SteamNetworkingUtils.SetConfigValue(setting, ESteamNetworkingConfigScope.k_ESteamNetworkingConfig_Global, IntPtr.Zero, ESteamNetworkingConfigDataType.k_ESteamNetworkingConfig_Int32, arg))
            {
                Logging.Error($"Steam refused config {setting} = {value}", "NetworkSession");
            }
        }
        finally
        {
            Marshal.FreeHGlobal(arg);
        }
    }

    /// <summary>
    /// Logs the config the Steam client actually holds, read back after setting it: the Steam client runs its own
    /// copy of the networking code, so its defaults and limits could differ from the open-source library's.
    /// </summary>
    private static void LogConfig()
    {
        int sendRateMin = GetGlobalConfigInt(ESteamNetworkingConfigValue.k_ESteamNetworkingConfig_SendRateMin);
        int sendRateMax = GetGlobalConfigInt(ESteamNetworkingConfigValue.k_ESteamNetworkingConfig_SendRateMax);
        int sendBuffer = GetGlobalConfigInt(ESteamNetworkingConfigValue.k_ESteamNetworkingConfig_SendBufferSize);
        int recvBuffer = GetGlobalConfigInt(ESteamNetworkingConfigValue.k_ESteamNetworkingConfig_RecvBufferSize);
        int recvMessages = GetGlobalConfigInt(ESteamNetworkingConfigValue.k_ESteamNetworkingConfig_RecvBufferMessages);
        int recvMaxMessage = GetGlobalConfigInt(ESteamNetworkingConfigValue.k_ESteamNetworkingConfig_RecvMaxMessageSize);
        int mtuPacket = GetGlobalConfigInt(ESteamNetworkingConfigValue.k_ESteamNetworkingConfig_MTU_PacketSize);
        // Read globally this is the packet size minus 100; a connection also takes off its encryption and headers.
        int mtuData = GetGlobalConfigInt(ESteamNetworkingConfigValue.k_ESteamNetworkingConfig_MTU_DataSize);
        int nagle = GetGlobalConfigInt(ESteamNetworkingConfigValue.k_ESteamNetworkingConfig_NagleTime);
        Logging.Log($"Steam config: send rate {sendRateMin}-{sendRateMax} B/s, send buffer {sendBuffer} B, recv buffer {recvBuffer} B / {recvMessages} msgs, recv max message {recvMaxMessage} B, MTU packet {mtuPacket} B / data {mtuData} B, Nagle {nagle} us", "NetworkSession");
    }

    /// <summary>Reads a global Steam networking config value; -1 if Steam won't give it.</summary>
    private static int GetGlobalConfigInt(ESteamNetworkingConfigValue setting)
    {
        IntPtr result = Marshal.AllocHGlobal(sizeof(int));
        try
        {
            ESteamNetworkingConfigDataType dataType;
            ulong size = sizeof(int);
            ESteamNetworkingGetConfigValueResult r = SteamNetworkingUtils.GetConfigValue(setting, ESteamNetworkingConfigScope.k_ESteamNetworkingConfig_Global, IntPtr.Zero, out dataType, result, ref size);
            if (r != ESteamNetworkingGetConfigValueResult.k_ESteamNetworkingGetConfigValue_OK && r != ESteamNetworkingGetConfigValueResult.k_ESteamNetworkingGetConfigValue_OKInherited)
            {
                Logging.Error($"Steam won't read config {setting}: {r}", "NetworkSession");
                return -1;
            }
            return Marshal.ReadInt32(result);
        }
        finally
        {
            Marshal.FreeHGlobal(result);
        }
    }

    private static SteamNetworkingIdentity IdentityFor(ulong steamId)
    {
        var ident = new SteamNetworkingIdentity();
        ident.SetSteamID64(steamId);
        return ident;
    }
}
