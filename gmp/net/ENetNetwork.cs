using Godot;
using Godot.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Stopwatch = System.Diagnostics.Stopwatch;

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
// plus one *fresh* ENetConnection per outbound dial, and Service() every one of them.
// Each side announces its peerID (and its own listen endpoint) in a small
// NET_Handshake the instant a link opens; that maps a raw ENetPacketPeer to a peerID.
//
// Threading: ENet is serviced on its own thread, about every millisecond, as Steam does its networking on its own
// threads. Serviced once a frame from the main thread, the socket's receive buffer (64 KB, Windows' default; Godot's
// ENet doesn't enlarge it) had to hold everything that arrived in a frame, and above about 16 Mbps of state the tail of
// each burst was thrown away by Windows, until ENet timed the link out. Every ENet object is used under _lock: the
// thread holds it while servicing, and the main thread takes it to send, dial and read stats. What the thread hears
// (messages, links made, lost and repaired, log lines) is queued in _events and handed out on the main thread by
// service(), which Lobby calls each frame, so everyone above this class still runs on the main thread.
//
// Pacing: reading often isn't enough on its own. ENet sends everything queued at once, and a whole state send (about
// 400 KB at 100 Mbps) arrived in well under a millisecond, overflowing the receiver's 64 KB before it could read. So,
// as Steam does, sends wait in a queue per peer and the thread hands them to ENet at a fixed rate: GameWorld.SendBudget
// plus a quarter (Steam's send rate, SteamNetwork.Host), a couple of milliseconds' worth at a time.
//
// Repair: a link that drops without the peer leaving (an ENet timeout) isn't reported at once. The side that dialed it
// dials again every second, and the other side waits, for up to RepairWindowMs. If the peer comes back the new link
// takes over and PeerRepairedEvent is raised instead of a disconnect and a connect (the host then resyncs the world,
// as anything reliable sent in the gap was lost). A peer that leaves on purpose says so (LeavingCode) and is reported
// at once.
public class ENetNetwork : Network
{
    // ENet needs a fixed channel count on both ends of every link. The Channel enum
    // uses ids up to 16; 64 leaves headroom.
    private const int ChannelCount = 64;
    private const int MaxInbound = 32;
    private const Channel DirChannel = Channel.NET_tba; // (2) transport endpoint directory

    /// <summary>Sent with the disconnect when this peer leaves on purpose; a timeout carries 0.</summary>
    private const int LeavingCode = 1;
    /// <summary>
    /// How long a dropped link has to come back before the peer is reported gone, ms. Long enough for the other end to
    /// notice the drop too (up to TimeoutMaxMs after it can hear again) and dial back.
    /// </summary>
    private const double RepairWindowMs = 30000;
    /// <summary>How often the side that dialed a dropped link dials it again, ms.</summary>
    private const double RedialIntervalMs = 1000;
    /// <summary>
    /// ENet gives up on a link when a reliable packet goes this long unacknowledged (ENet's defaults are 5 to 30 s;
    /// the maximum is lowered so a dead link is noticed, and repaired, sooner).
    /// </summary>
    private const int TimeoutMinMs = 5000;
    private const int TimeoutMaxMs = 10000;

    private readonly ulong _selfPeerID;
    private readonly int _listenPort;
    private readonly string _selfIp;

    private ENetConnection _server;                          // bound: accepts inbound
    private readonly List<ENetConnection> _outbound = new(); // one per outbound dial
    /// <summary>Outbound hosts whose link has gone, destroyed after the current servicing pass.</summary>
    private readonly List<ENetConnection> _deadHosts = new();

    // A single live link to one peer.
    private class Link
    {
        public ENetPacketPeer Peer;
        public ulong PeerID;        // 0 until the NET_Handshake identifies it
        public string Ip = "";
        public int ListenPort;      // the peer's own listening port, from its handshake
        public bool Identified;
        public ENetConnection Host; // the outbound host this link was dialed from; null for inbound links
    }

    private readonly List<Link> _links = new();
    private readonly System.Collections.Generic.Dictionary<ulong, Link> _byInstance = new(); // peer.GetInstanceId() -> Link
    private readonly System.Collections.Generic.Dictionary<ulong, Link> _byId = new();        // peerID -> Link
    private readonly System.Collections.Generic.Dictionary<ulong, (string ip, int port)> _endpoints = new();
    private readonly HashSet<ulong> _pendingConnects = new(); // Connect() requests awaiting an endpoint

    /// <summary>Peers whose link dropped without them leaving, and until when (ms) it may come back.</summary>
    private readonly System.Collections.Generic.Dictionary<ulong, double> _repairDeadline = new();
    /// <summary>The dropped links this side dialed, and when (ms) to dial each again.</summary>
    private readonly System.Collections.Generic.Dictionary<ulong, double> _nextRedial = new();
    private readonly List<ulong> _repairScratch = new();

    /// <summary>Most bytes waiting to go to one peer before sends fail, as Steam's send buffer (SteamNetwork).</summary>
    private const int SendQueueLimit = 32 * 1024 * 1024;
    /// <summary>After a quiet spell, at most this many milliseconds' worth of sending goes out at once (at least 16 KB).</summary>
    private const double MaxBurstMs = 4;

    private struct OutPacket
    {
        public int channel;
        public byte[] data;
        public int flags;
    }

    /// <summary>What's waiting to go to one peer, in order, and how many bytes it may send now.</summary>
    private class SendQueue
    {
        public readonly Queue<OutPacket> packets = new();
        public int bytes;
        public double allowance;
        public double lastRefillMs;
    }

    private readonly System.Collections.Generic.Dictionary<ulong, SendQueue> _sendQueues = new();

    public event Network.MessageReceived MessageReceivedEvent;
    public event Network.PeerConnected PeerConnectedEvent;
    public event Network.PeerDisconnected PeerDisconnectedEvent;
    public event Network.PeerRepaired PeerRepairedEvent;

    // ---- the network thread ----------------------------------------------------

    private readonly object _lock = new();
    private System.Threading.Thread _thread;
    private volatile bool _running;
    /// <summary>Times the thread has serviced ENet, for the stats line.</summary>
    private long _loops;
    private long _lastStatsLoops;

    private enum NetEventType { Message, Connected, Disconnected, Repaired, Log, Error }

    private struct NetEvent
    {
        public NetEventType type;
        public ulong peerID;
        public Channel channel;
        public byte[] data;
        public string text;
    }

    private readonly ConcurrentQueue<NetEvent> _events = new();

    private static double NowMs => Stopwatch.GetTimestamp() * 1000.0 / Stopwatch.Frequency;

    private void Log(string text)
    {
        _events.Enqueue(new NetEvent { type = NetEventType.Log, text = text });
    }

    private void LogError(string text)
    {
        _events.Enqueue(new NetEvent { type = NetEventType.Error, text = text });
    }

    public ENetNetwork(ulong selfPeerID, int listenPort, string selfIp = "127.0.0.1")
    {
        _selfPeerID = selfPeerID;
        _listenPort = listenPort;
        _selfIp = selfIp;
        _endpoints[selfPeerID] = (selfIp, listenPort); // so the directory we gossip includes us
    }

    // Bind the local listen socket and start servicing. Both hosts and joiners must do
    // this — a joiner still has to accept inbound mesh dials from later members.
    public Error Host()
    {
        Error err;
        lock (_lock)
        {
            _server = new ENetConnection();
            err = _server.CreateHostBound("*", _listenPort, MaxInbound, ChannelCount);
            if (err != Error.Ok)
            {
                LogError($"failed to bind listen port {_listenPort}: {err}");
                _server = null;
            }
            else
            {
                Log($"listening on port {_listenPort} (peer {_selfPeerID})");
            }
        }
        if (_thread == null)
        {
            _running = true;
            _thread = new System.Threading.Thread(ThreadLoop);
            _thread.Name = "ENetNetwork";
            _thread.IsBackground = true;
            _thread.Start();
        }
        return err;
    }

    private void ThreadLoop()
    {
        while (_running)
        {
            lock (_lock)
            {
                if (NowMs >= blackoutUntilMs)
                {
                    ReleaseDelayed();
                    Pace();
                    if (_server != null)
                        Pump(_server);
                    _pumpSnapshot.Clear();
                    _pumpSnapshot.AddRange(_outbound);
                    foreach (var c in _pumpSnapshot)
                        Pump(c);
                    _pumpSnapshot.Clear();
                    foreach (ENetConnection dead in _deadHosts)
                    {
                        _outbound.Remove(dead);
                        dead.Destroy();
                    }
                    _deadHosts.Clear();
                    CheckRepairs();
                }
                _loops++;
            }
            System.Threading.Thread.Sleep(1);
        }
    }

    // Hands out on the main thread what the network thread heard since the last frame. Lobby calls this from _Process.
    public void service()
    {
        while (_events.TryDequeue(out NetEvent e))
        {
            switch (e.type)
            {
                case NetEventType.Message: MessageReceivedEvent?.Invoke(e.peerID, e.channel, e.data); break;
                case NetEventType.Connected: PeerConnectedEvent?.Invoke(e.peerID); break;
                case NetEventType.Disconnected: PeerDisconnectedEvent?.Invoke(e.peerID); break;
                case NetEventType.Repaired: PeerRepairedEvent?.Invoke(e.peerID); break;
                case NetEventType.Log: Logging.Log(e.text, "NetworkSession"); break;
                case NetEventType.Error: Logging.Error(e.text, "NetworkSession"); break;
            }
        }
    }

    // Dial a peer by id. If we already know its endpoint, dial now; otherwise defer
    // until the directory supplies it.
    public Error Connect(ulong peerID)
    {
        lock (_lock)
        {
            if (_byId.ContainsKey(peerID) || peerID == _selfPeerID)
                return Error.Ok; // already linked (or self) — nothing to do
            if (_endpoints.TryGetValue(peerID, out var ep))
                return Dial(ep.ip, ep.port, peerID);
            _pendingConnects.Add(peerID);
            Log($"Connect({peerID}) deferred: endpoint unknown, awaiting directory");
            return Error.Ok;
        }
    }

    // Dial a raw endpoint whose peerID we don't know yet (the very first host dial from
    // the LAN UI). The host's real peerID arrives in its NET_Handshake reply.
    public Error ConnectByEndpoint(string ip, int port)
    {
        lock (_lock)
        {
            return Dial(ip, port, 0);
        }
    }

    // Called with _lock held.
    private Error Dial(string ip, int port, ulong expectedPeerID)
    {
        var client = new ENetConnection();
        Error err = client.CreateHost(1, ChannelCount);
        if (err != Error.Ok)
        {
            LogError($"could not create outbound host for {ip}:{port}: {err}");
            return err;
        }
        ENetPacketPeer peer = client.ConnectToHost(ip, port, ChannelCount);
        if (peer == null)
        {
            LogError($"ConnectToHost({ip}:{port}) returned null");
            return Error.CantConnect;
        }
        _outbound.Add(client);
        var link = new Link { Peer = peer, Ip = ip, ListenPort = port, PeerID = expectedPeerID, Host = client };
        _links.Add(link);
        _byInstance[peer.GetInstanceId()] = link;
        if (expectedPeerID != 0)
            _byId[expectedPeerID] = link;
        Log($"dialing {ip}:{port}" + (expectedPeerID != 0 ? $" (peer {expectedPeerID})" : ""));
        return Error.Ok;
    }

    public Error Disconnect()
    {
        _running = false;
        _thread?.Join();
        _thread = null;
        lock (_lock)
        {
            // Tells each peer this is a leave, not a drop, so it doesn't wait to repair the link.
            foreach (var l in _links)
                l.Peer?.PeerDisconnectNow(LeavingCode);
            _server?.Destroy();
            _server = null;
            foreach (var c in _outbound)
                c.Destroy();
            _outbound.Clear();
            _deadHosts.Clear();
            _links.Clear();
            _byInstance.Clear();
            _byId.Clear();
            _pendingConnects.Clear();
            _repairDeadline.Clear();
            _nextRedial.Clear();
            _delayed.Clear();
            _lastReliableRelease.Clear();
            _sendQueues.Clear();
        }
        return Error.Ok;
    }

    public Error Send(ulong peerID, Channel ch, byte[] msg, int SENDFLAGS = Network.k_nSteamNetworkingSend_Reliable)
    {
        if (peerID == _selfPeerID)
        {
            MessageReceivedEvent?.Invoke(peerID, ch, msg);
            return Error.Ok;
        }
        lock (_lock)
        {
            // Waiting for a dropped link to come back: what's sent meanwhile is lost (the host resyncs after a repair).
            if (_repairDeadline.ContainsKey(peerID))
                return Error.Unavailable;
            if (!_byId.TryGetValue(peerID, out var link) || !link.Identified)
            {
                LogError($"Send: no identified link to peer {peerID}");
                return Error.DoesNotExist;
            }
            // Unreliable goes unsequenced, as Steam's does: packets can arrive out of order, and an older one is still
            // delivered after a newer one (ENet's default drops it). GameWorld drops stale states per object itself.
            bool reliable = (SENDFLAGS & Network.k_nSteamNetworkingSend_Reliable) != 0;
            int flags = (int)ENetPacketPeer.FlagUnsequenced;
            if (reliable)
                flags = (int)ENetPacketPeer.FlagReliable;
            // Queued; the network thread sends it as the pace allows (Pace).
            if (simLagMs == 0 && simJitterMs == 0 && simLossPercent == 0)
                return Enqueue(peerID, (int)ch, msg, flags);
            return SendSimulated(peerID, ch, msg, flags, reliable);
        }
    }

    // ---- simulated network conditions (console: net_sim, net_blackout) ----------
    // Loopback has no latency or loss, so LAN tests on one machine can add some: every packet this peer sends waits
    // simLagMs (plus or minus up to simJitterMs) before it goes to ENet, and simLossPercent of unreliable packets are
    // dropped. Reliable packets keep their order (each waits for the one before it), as ENet and Steam deliver them;
    // unreliable ones may overtake each other. Packets are released by the network thread, so the delay is accurate to
    // about a millisecond. Set on each peer: it delays only what that peer sends.

    /// <summary>One-way delay added to every packet sent, ms (round trip grows by twice this with both peers set).</summary>
    public static int simLagMs;
    /// <summary>Random extra delay, from -this to +this ms, per packet.</summary>
    public static int simJitterMs;
    /// <summary>Percentage of unreliable packets dropped.</summary>
    public static float simLossPercent;
    /// <summary>
    /// Until when (on the network thread's clock, ms) ENet isn't serviced at all: nothing is sent, received or
    /// acknowledged, so the other end times the link out, as if the network went down. For testing link repair.
    /// </summary>
    public static double blackoutUntilMs;

    /// <summary>Stops servicing ENet for this many seconds (the console's net_blackout).</summary>
    public static void Blackout(double seconds)
    {
        blackoutUntilMs = NowMs + seconds * 1000;
    }

    private struct DelayedPacket
    {
        public double releaseMs;
        public ulong peerID;
        public int channel;
        public byte[] data;
        public int flags;
    }

    private readonly List<DelayedPacket> _delayed = new();
    /// <summary>Per peer, when the newest delayed reliable packet goes out; the next one can't go before it.</summary>
    private readonly System.Collections.Generic.Dictionary<ulong, double> _lastReliableRelease = new();
    private int _simDropped;

    // Called with _lock held.
    private Error SendSimulated(ulong peerID, Channel ch, byte[] msg, int flags, bool reliable)
    {
        if (!reliable && System.Random.Shared.NextDouble() * 100 < simLossPercent)
        {
            _simDropped++;
            return Error.Ok;
        }
        double release = NowMs + simLagMs + (System.Random.Shared.NextDouble() * 2 - 1) * simJitterMs;
        if (reliable)
        {
            if (_lastReliableRelease.TryGetValue(peerID, out double last) && last > release)
                release = last;
            _lastReliableRelease[peerID] = release;
        }
        _delayed.Add(new DelayedPacket { releaseMs = release, peerID = peerID, channel = (int)ch, data = msg, flags = flags });
        return Error.Ok;
    }

    /// <summary>Hands ENet the delayed packets whose time has come, in the order they were sent. Called with _lock held.</summary>
    private void ReleaseDelayed()
    {
        if (_delayed.Count == 0)
            return;
        double now = NowMs;
        int kept = 0;
        for (int i = 0; i < _delayed.Count; i++)
        {
            DelayedPacket packet = _delayed[i];
            if (packet.releaseMs <= now)
            {
                Enqueue(packet.peerID, packet.channel, packet.data, packet.flags);
            }
            else
            {
                _delayed[kept] = packet;
                kept++;
            }
        }
        _delayed.RemoveRange(kept, _delayed.Count - kept);
    }

    // ---- pacing ----------------------------------------------------------------

    /// <summary>Adds a packet to its peer's send queue. Called with _lock held.</summary>
    private Error Enqueue(ulong peerID, int channel, byte[] data, int flags)
    {
        if (!_sendQueues.TryGetValue(peerID, out SendQueue queue))
        {
            queue = new SendQueue { lastRefillMs = NowMs };
            _sendQueues[peerID] = queue;
        }
        if (queue.bytes + data.Length > SendQueueLimit)
            return Error.Busy;
        queue.packets.Enqueue(new OutPacket { channel = channel, data = data, flags = flags });
        queue.bytes += data.Length;
        return Error.Ok;
    }

    /// <summary>
    /// Hands each peer's queued packets to ENet at the send rate, in order. The rate is read each time, so send_budget
    /// takes effect at once. A peer with no link (gone, or waiting for a repair) has its queue thrown away: the host
    /// resyncs after a repair. Called with _lock held, on the network thread.
    /// </summary>
    private void Pace()
    {
        double now = NowMs;
        double bytesPerMs = (GameWorld.SendBudget + GameWorld.SendBudget / 4) / 1000.0;
        double maxAllowance = System.Math.Max(16 * 1024, bytesPerMs * MaxBurstMs);
        foreach ((ulong peerID, SendQueue queue) in _sendQueues)
        {
            queue.allowance = System.Math.Min(maxAllowance, queue.allowance + (now - queue.lastRefillMs) * bytesPerMs);
            queue.lastRefillMs = now;
            if (!_byId.TryGetValue(peerID, out Link link) || !link.Identified)
            {
                queue.packets.Clear();
                queue.bytes = 0;
                continue;
            }
            while (queue.allowance > 0 && queue.packets.Count > 0)
            {
                OutPacket packet = queue.packets.Dequeue();
                queue.bytes -= packet.data.Length;
                queue.allowance -= packet.data.Length;
                link.Peer.Send(packet.channel, packet.data, packet.flags);
            }
        }
    }

    // ---- connection stats (perf log) --------------------------------------------

    private double _lastStatsMs;

    /// <summary>
    /// One line about the links, for the perf log (as SteamNetwork.LogConnectionStatus): per peer ENet's round trip
    /// time, its packet loss estimate and its throttle (32 is none: below that ENet drops unreliable packets on purpose
    /// as it thinks the link is congested), then the bytes and packets on the wire since the last call, how often the
    /// network thread serviced ENet, and the simulated conditions.
    /// </summary>
    public string ConnectionStatus()
    {
        lock (_lock)
        {
            string line = "";
            foreach (Link link in _links)
            {
                if (!link.Identified)
                    continue;
                double rtt = link.Peer.GetStatistic(ENetPacketPeer.PeerStatistic.RoundTripTime);
                double loss = link.Peer.GetStatistic(ENetPacketPeer.PeerStatistic.PacketLoss) / ENetPacketPeer.PacketLossScale * 100;
                double throttle = link.Peer.GetStatistic(ENetPacketPeer.PeerStatistic.PacketThrottle);
                int queued = 0;
                if (_sendQueues.TryGetValue(link.PeerID, out SendQueue queue))
                    queued = queue.bytes;
                line += $"peer {link.PeerID}: rtt {rtt:0} ms, loss {loss:0.0}%, throttle {throttle:0}/32, queued {queued / 1024} KB | ";
            }
            foreach (ulong id in _repairDeadline.Keys)
            {
                line += $"peer {id}: repairing | ";
            }
            // Host statistics count since they were last read, over every ENet host (the bound one and each dial).
            double sentBytes = 0, receivedBytes = 0, sentPackets = 0, receivedPackets = 0;
            _pumpSnapshot.Clear();
            if (_server != null)
                _pumpSnapshot.Add(_server);
            _pumpSnapshot.AddRange(_outbound);
            foreach (ENetConnection host in _pumpSnapshot)
            {
                sentBytes += host.PopStatistic(ENetConnection.HostStatistic.SentData);
                receivedBytes += host.PopStatistic(ENetConnection.HostStatistic.ReceivedData);
                sentPackets += host.PopStatistic(ENetConnection.HostStatistic.SentPackets);
                receivedPackets += host.PopStatistic(ENetConnection.HostStatistic.ReceivedPackets);
            }
            _pumpSnapshot.Clear();
            double now = NowMs;
            double seconds = (now - _lastStatsMs) / 1000.0;
            _lastStatsMs = now;
            double loopsPerSecond = (_loops - _lastStatsLoops) / seconds;
            _lastStatsLoops = _loops;
            line += $"wire out {sentBytes / seconds / 1024:0} KB/s ({sentPackets / seconds:0} pkt/s), in {receivedBytes / seconds / 1024:0} KB/s ({receivedPackets / seconds:0} pkt/s)";
            line += $" | net thread {loopsPerSecond:0} services/s";
            line += $" | sim lag {simLagMs}±{simJitterMs} ms, loss {simLossPercent}%, held {_delayed.Count}, dropped {_simDropped}";
            return line;
        }
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

    // ---- servicing (network thread, _lock held) -------------------------------------

    private readonly List<ENetConnection> _pumpSnapshot = new();

    private void Pump(ENetConnection host)
    {
        while (true)
        {
            // Disposed right away: each Service() call returns a new Godot Array, and undisposed ones pile up as
            // finalizable garbage that lengthens the next GC pause.
            using Array ev = host.Service();
            if (ev.Count < 4)
                break;
            var type = (ENetConnection.EventType)ev[0].AsInt32();
            if (type == ENetConnection.EventType.None || type == ENetConnection.EventType.Error)
                break;
            var peer = ev[1].As<ENetPacketPeer>();
            int data = ev[2].AsInt32();
            int channel = ev[3].AsInt32();
            switch (type)
            {
                case ENetConnection.EventType.Connect: OnConnect(peer); break;
                case ENetConnection.EventType.Receive: OnReceive(peer, channel); break;
                case ENetConnection.EventType.Disconnect: OnDisconnect(peer, data); break;
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
        // ENet's throttle never slows down (deceleration 0). Otherwise, whenever round trips vary, ENet decides the link
        // is congested and drops unreliable packets on purpose, at times nearly all the state for seconds. Steam has no
        // such throttle (its send rate is pinned), and state sync already keeps to GameWorld.SendBudget itself.
        // Interval and acceleration are ENet's defaults.
        peer.ThrottleConfigure(5000, 2, 0);
        peer.SetTimeout(32, TimeoutMinMs, TimeoutMaxMs);
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
        _events.Enqueue(new NetEvent { type = NetEventType.Message, peerID = from, channel = ch, data = raw });
    }

    private void OnDisconnect(ENetPacketPeer peer, int data)
    {
        ulong inst = peer.GetInstanceId();
        if (!_byInstance.TryGetValue(inst, out var link))
            return;
        _byInstance.Remove(inst);
        _links.Remove(link);
        if (link.Host != null)
            _deadHosts.Add(link.Host);
        if (!_byId.TryGetValue(link.PeerID, out var byIdLink) || byIdLink != link)
            return; // a link that was already replaced
        _byId.Remove(link.PeerID);
        // A dial that never got through: during a repair the next redial tries again.
        if (!link.Identified)
            return;
        ulong id = link.PeerID;
        if (data == LeavingCode)
        {
            Log($"peer {id} left");
            _events.Enqueue(new NetEvent { type = NetEventType.Disconnected, peerID = id });
            return;
        }
        // Dropped without leaving: give it a while to come back before telling anyone.
        _repairDeadline[id] = NowMs + RepairWindowMs;
        if (link.Host != null)
            _nextRedial[id] = NowMs;
        string who = "waiting for it to dial back";
        if (link.Host != null)
            who = "dialing it again";
        Log($"link to peer {id} dropped, {who} for {RepairWindowMs / 1000:0} s");
    }

    /// <summary>Redials dropped links this side dialed, and gives up on any that ran out of time.</summary>
    private void CheckRepairs()
    {
        if (_repairDeadline.Count == 0)
            return;
        double now = NowMs;
        _repairScratch.Clear();
        _repairScratch.AddRange(_repairDeadline.Keys);
        foreach (ulong id in _repairScratch)
        {
            if (now > _repairDeadline[id])
            {
                _repairDeadline.Remove(id);
                _nextRedial.Remove(id);
                Log($"gave up on peer {id}: its link didn't come back");
                _events.Enqueue(new NetEvent { type = NetEventType.Disconnected, peerID = id });
                continue;
            }
            // A dial still under way is in _byId until it links or fails.
            if (_nextRedial.TryGetValue(id, out double next) && now >= next && !_byId.ContainsKey(id) && _endpoints.TryGetValue(id, out var ep))
            {
                _nextRedial[id] = now + RedialIntervalMs;
                Dial(ep.ip, ep.port, id);
            }
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
        // Back after a drop: either this side already noticed (it's waiting to repair), or the peer noticed first and
        // dialed again while this side still had the old link, which is dead too.
        bool repaired = _repairDeadline.ContainsKey(peerID);
        if (_byId.TryGetValue(peerID, out var old) && old != link && old.Identified)
        {
            repaired = true;
            old.Peer.PeerDisconnectNow(0); // raises no event
            _byInstance.Remove(old.Peer.GetInstanceId());
            _links.Remove(old);
            if (old.Host != null)
                _deadHosts.Add(old.Host);
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
            SendDirectory(peer);              // share every endpoint we know so they can mesh
            if (repaired)
            {
                _repairDeadline.Remove(peerID);
                _nextRedial.Remove(peerID);
                // Whatever piled up for the old link is stale (the host resyncs now).
                _sendQueues.Remove(peerID);
                Log($"link to peer {peerID} repaired ({link.Ip}:{listenPort})");
                _events.Enqueue(new NetEvent { type = NetEventType.Repaired, peerID = peerID });
            }
            else
            {
                Log($"linked to peer {peerID} ({link.Ip}:{listenPort})");
                _events.Enqueue(new NetEvent { type = NetEventType.Connected, peerID = peerID });
            }
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
