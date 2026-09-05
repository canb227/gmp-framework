using Godot;
using Godot.Collections;
using System.Collections.Generic;
using System.Linq;

// Point-to-point transport backed by Godot's built-in low-level ENet
// (ENetConnection / ENetPacketPeer). It knows nothing about lobby topology: it can
// bind a listen socket, dial an individual peer, and exchange raw byte payloads with
// peers identified by a ulong peerID. Building a full mesh (dialing every member) is
// the Lobby's job — this class only ever talks to one peer at a time.
//
// IP addresses and ports live *entirely inside this class*. Callers address peers by
// ulong peerID only. To make Connect(peerID) work for a peer this node has never
// directly met, the transport keeps an internal endpoint directory (peerID -> ip:port)
// and gossips it: whenever a link comes up, each side sends the other its full
// directory. A Connect(peerID) whose endpoint isn't known yet is deferred and fires
// automatically once the directory supplies it, so it is robust to message ordering.
//
// The one ENet gotcha (see IMPLEMENTATION_GUIDE.md): a single ENetConnection can
// connect_to_host exactly once, so we keep one *bound* connection for inbound links
// plus one *fresh* ENetConnection per outbound dial, and Service() every one of them
// each frame. Each side announces its peerID (and its own listen endpoint) in a small
// NET_Handshake the instant a link opens; that maps a raw ENetPacketPeer to a peerID.
public class ENetNetwork : Network
{
    // ENet needs a fixed channel count on both ends of every link. The Channel enum
    // uses ids up to 15, so 16 channels cover it.
    private const int ChannelCount = 64;
    private const int MaxInbound = 32;
    private const Channel DirChannel = Channel.NET_tba; // (2) transport endpoint directory

    private readonly ulong _selfPeerID;
    private readonly int _listenPort;
    private readonly string _selfIp;

    private ENetConnection _server;                          // bound: accepts inbound
    private readonly List<ENetConnection> _outbound = new(); // one per outbound dial

    // A single live link to one peer.
    private class Link
    {
        public ENetPacketPeer Peer;
        public ulong PeerID;        // 0 until the NET_Handshake identifies it
        public string Ip = "";
        public int ListenPort;      // the peer's own listening port, from its handshake
        public bool Identified;
    }

    private readonly List<Link> _links = new();
    private readonly System.Collections.Generic.Dictionary<ulong, Link> _byInstance = new(); // peer.GetInstanceId() -> Link
    private readonly System.Collections.Generic.Dictionary<ulong, Link> _byId = new();        // peerID -> Link
    private readonly System.Collections.Generic.Dictionary<ulong, (string ip, int port)> _endpoints = new();
    private readonly HashSet<ulong> _pendingConnects = new(); // Connect() requests awaiting an endpoint

    public event Network.MessageSent MessageSentEvent;
    public event Network.MessageReceived MessageReceivedEvent;
    public event Network.PeerConnected PeerConnectedEvent;
    public event Network.PeerDisconnected PeerDisconnectedEvent;

    public ulong SelfPeerID => _selfPeerID;
    public int ListenPort => _listenPort;

    public ENetNetwork(ulong selfPeerID, int listenPort, string selfIp = "127.0.0.1")
    {
        _selfPeerID = selfPeerID;
        _listenPort = listenPort;
        _selfIp = selfIp;
        _endpoints[selfPeerID] = (selfIp, listenPort); // so the directory we gossip includes us
    }

    // Bind the local listen socket. Both hosts and joiners must do this — a joiner
    // still has to accept inbound mesh dials from later members.
    public Error Host()
    {
        _server = new ENetConnection();
        Error err = _server.CreateHostBound("*", _listenPort, MaxInbound, ChannelCount);
        if (err != Error.Ok)
        {
            Logging.Error($"failed to bind listen port {_listenPort}: {err}", "NetworkSession");
            _server = null;
            return err;
        }
        Logging.Log($"listening on port {_listenPort} (peer {_selfPeerID})", "NetworkSession");
        return Error.Ok;
    }

    // Register how to reach a peerID. Part of the Network contract; Lobby no longer
    // needs it (endpoints propagate via the directory) but a transport-aware caller
    // may still seed a mapping directly. Resolves any pending Connect for that peer.
    public void AddPeerIDToIPMapping(ulong peerID, string ip, int port)
    {
        LearnEndpoint(peerID, ip, port);
    }

    // Dial a peer by id. If we already know its endpoint, dial now; otherwise defer
    // until the directory supplies it (or AddPeerIDToIPMapping seeds it).
    public Error Connect(ulong peerID)
    {
        if (_byId.ContainsKey(peerID) || peerID == _selfPeerID)
            return Error.Ok; // already linked (or self) — nothing to do
        if (_endpoints.TryGetValue(peerID, out var ep))
            return Dial(ep.ip, ep.port, peerID);
        _pendingConnects.Add(peerID);
        Logging.Log($"Connect({peerID}) deferred: endpoint unknown, awaiting directory", "NetworkSession");
        return Error.Ok;
    }

    // Dial a raw endpoint whose peerID we don't know yet (the very first host dial from
    // the LAN UI). The host's real peerID arrives in its NET_Handshake reply.
    public Error ConnectByEndpoint(string ip, int port)
    {
        return Dial(ip, port, 0);
    }

    private Error Dial(string ip, int port, ulong expectedPeerID)
    {
        var client = new ENetConnection();
        Error err = client.CreateHost(1, ChannelCount);
        if (err != Error.Ok)
        {
            Logging.Error($"could not create outbound host for {ip}:{port}: {err}", "NetworkSession");
            return err;
        }
        ENetPacketPeer peer = client.ConnectToHost(ip, port, ChannelCount);
        if (peer == null)
        {
            Logging.Error($"ConnectToHost({ip}:{port}) returned null", "NetworkSession");
            return Error.CantConnect;
        }
        _outbound.Add(client);
        var link = new Link { Peer = peer, Ip = ip, ListenPort = port, PeerID = expectedPeerID };
        _links.Add(link);
        _byInstance[peer.GetInstanceId()] = link;
        if (expectedPeerID != 0)
            _byId[expectedPeerID] = link;
        Logging.Log($"dialing {ip}:{port}" + (expectedPeerID != 0 ? $" (peer {expectedPeerID})" : ""), "NetworkSession");
        return Error.Ok;
    }

    public Error Disconnect()
    {
        foreach (var l in _links)
            l.Peer?.PeerDisconnectNow(0);
        _server?.Destroy();
        _server = null;
        foreach (var c in _outbound)
            c.Destroy();
        _outbound.Clear();
        _links.Clear();
        _byInstance.Clear();
        _byId.Clear();
        _pendingConnects.Clear();
        return Error.Ok;
    }

    public Error Send(ulong peerID, Channel ch, byte[] msg, int SENDFLAGS = Network.k_nSteamNetworkingSend_Reliable)
    {
        if (peerID == SelfPeerID)
        {
            //Logging.Log($"I ({SelfPeerID}) am trying to send a message to myself", "NetworkWire");
            MessageSentEvent?.Invoke(peerID, ch, msg);
            MessageReceivedEvent?.Invoke(peerID, ch, msg);
            return Error.Ok;
        }
        if (!_byId.TryGetValue(peerID, out var link) || !link.Identified)
        {
            Logging.Error($"Send: no identified link to peer {peerID}", "NetworkWire");
            return Error.DoesNotExist;
        }
        int flags = (SENDFLAGS & Network.k_nSteamNetworkingSend_Reliable) != 0
            ? (int)ENetPacketPeer.FlagReliable
            : 0;
        Error err = link.Peer.Send((int)ch, msg, flags);
        MessageSentEvent?.Invoke(peerID, ch, msg);
        return err;
    }

    public Error Broadcast(List<ulong> peerIDs, Channel ch, byte[] msg, int SENDFLAGS = Network.k_nSteamNetworkingSend_Reliable)
    {
        Error result = Error.Ok;
        foreach (ulong id in peerIDs)
        {
            Error err = Send(id, ch, msg, SENDFLAGS);
            if (err != Error.Ok)
                result = err;
        }
        return result;
    }

    // Pump every ENet host (bound + every outbound) once per frame. Lobby calls this
    // from _Process. Snapshot the outbound list — Dialing a deferred Connect while
    // servicing can append to it.
    public void service()
    {
        if (_server != null)
            Pump(_server);
        foreach (var c in _outbound.ToArray())
            Pump(c);
    }

    private void Pump(ENetConnection host)
    {
        while (true)
        {
            Array ev = host.Service();
            if (ev.Count < 4)
                break;
            var type = (ENetConnection.EventType)ev[0].AsInt32();
            if (type == ENetConnection.EventType.None || type == ENetConnection.EventType.Error)
                break;
            var peer = ev[1].As<ENetPacketPeer>();
            int channel = ev[3].AsInt32();
            switch (type)
            {
                case ENetConnection.EventType.Connect: OnConnect(peer); break;
                case ENetConnection.EventType.Receive: OnReceive(peer, channel); break;
                case ENetConnection.EventType.Disconnect: OnDisconnect(peer); break;
            }
        }
    }

    private Link LinkFor(ENetPacketPeer peer)
    {
        ulong inst = peer.GetInstanceId();
        if (_byInstance.TryGetValue(inst, out var link))
            return link;
        // Inbound peer we never dialed: create a placeholder until its handshake lands.
        link = new Link { Peer = peer, Ip = peer.GetRemoteAddress() };
        _links.Add(link);
        _byInstance[inst] = link;
        return link;
    }

    private void OnConnect(ENetPacketPeer peer)
    {
        var link = LinkFor(peer);
        if (string.IsNullOrEmpty(link.Ip))
            link.Ip = peer.GetRemoteAddress();
        // Introduce ourselves so the peer can map this raw link to our peerID.
        SendHandshake(peer);
    }

    private void OnReceive(ENetPacketPeer peer, int channel)
    {
        byte[] raw = peer.GetPacket();
        var ch = (Channel)channel;
        if (ch == Channel.NET_Handshake) { OnHandshake(peer, raw); return; } // transport-internal
        if (ch == DirChannel) { OnDirectory(raw); return; }                  // transport-internal
        var link = LinkFor(peer);
        ulong from = link.Identified ? link.PeerID : 0;
        MessageReceivedEvent?.Invoke(from, ch, raw);
    }

    private void OnDisconnect(ENetPacketPeer peer)
    {
        ulong inst = peer.GetInstanceId();
        if (!_byInstance.TryGetValue(inst, out var link))
            return;
        _byInstance.Remove(inst);
        _links.Remove(link);
        if (link.Identified && _byId.TryGetValue(link.PeerID, out var byIdLink) && byIdLink == link)
        {
            _byId.Remove(link.PeerID);
            Logging.Log($"lost peer {link.PeerID}", "NetworkSession");
            PeerDisconnectedEvent?.Invoke(link.PeerID);
        }
    }

    private void SendHandshake(ENetPacketPeer peer)
    {
        var d = new Dictionary
        {
            { "id", (long)_selfPeerID },
            { "port", _listenPort },
            { "ip", _selfIp },
        };
        peer.Send((int)Channel.NET_Handshake, GD.VarToBytes(d), (int)ENetPacketPeer.FlagReliable);
    }

    private void OnHandshake(ENetPacketPeer peer, byte[] raw)
    {
        var d = GD.BytesToVar(raw).AsGodotDictionary();
        ulong peerID = (ulong)d["id"].AsInt64();
        int listenPort = d["port"].AsInt32();

        var link = LinkFor(peer);
        // If we dialed this link with a provisional (0) or wrong id, re-key it.
        if (link.PeerID != peerID)
        {
            if (link.PeerID != 0 && _byId.TryGetValue(link.PeerID, out var prev) && prev == link)
                _byId.Remove(link.PeerID);
            link.PeerID = peerID;
        }
        link.ListenPort = listenPort;
        if (string.IsNullOrEmpty(link.Ip))
            link.Ip = peer.GetRemoteAddress();
        _byId[peerID] = link;
        _endpoints[peerID] = (link.Ip, listenPort);
        _pendingConnects.Remove(peerID); // now directly linked

        if (!link.Identified)
        {
            link.Identified = true;
            Logging.Log($"linked to peer {peerID} ({link.Ip}:{listenPort})", "NetworkSession");
            SendDirectory(peer);              // share every endpoint we know so they can mesh
            PeerConnectedEvent?.Invoke(peerID);
        }
    }

    // ---- endpoint directory (transport-internal addressing) --------------------

    private void SendDirectory(ENetPacketPeer to)
    {
        var entries = new Godot.Collections.Array();
        foreach (var kv in _endpoints)
        {
            entries.Add(new Dictionary
            {
                { "id", (long)kv.Key },
                { "ip", kv.Value.ip },
                { "port", kv.Value.port },
            });
        }
        var d = new Dictionary { { "t", "dir" }, { "entries", entries } };
        to.Send((int)DirChannel, GD.VarToBytes(d), (int)ENetPacketPeer.FlagReliable);
    }

    private void OnDirectory(byte[] raw)
    {
        var d = GD.BytesToVar(raw).AsGodotDictionary();
        var entries = d["entries"].AsGodotArray();
        foreach (var ev in entries)
        {
            var e = ev.AsGodotDictionary();
            LearnEndpoint((ulong)e["id"].AsInt64(), e["ip"].AsString(), e["port"].AsInt32());
        }
    }

    // Record an endpoint for a peerID and, if a Connect was waiting on it, dial now.
    private void LearnEndpoint(ulong id, string ip, int port)
    {
        if (id == _selfPeerID || string.IsNullOrEmpty(ip) || port == 0)
            return;
        if (!_endpoints.ContainsKey(id))
            _endpoints[id] = (ip, port);
        var ep = _endpoints[id];
        if (_pendingConnects.Contains(id) && !_byId.ContainsKey(id))
        {
            _pendingConnects.Remove(id);
            Dial(ep.ip, ep.port, id);
        }
    }
}
