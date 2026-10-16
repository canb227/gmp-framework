using Godot;
using Nerdbank.MessagePack;
using PolyType;
using System;
using System.Collections.Generic;
using System.Linq;
using Stopwatch = System.Diagnostics.Stopwatch;

/// <summary>One packet of state updates. Sent as an array of values; see the note on <see cref="TransformSyncState"/> about [Key] numbers.</summary>
[GenerateShape]
public partial record WorldTickMessage
{
    /// <summary>When it was sent (<see cref="GameWorld.netTime"/>).</summary>
    [Key(0)] public ulong sendTime = 0;
    /// <summary>The sender's physics tick it was sent on, which followers interpolate by (<see cref="GameWorld.PlaybackTick"/>).</summary>
    [Key(1)] public ulong tick = 0;
    [Key(2)] public List<(ulong, byte[])> updates = new();
}

[GenerateShapeFor<List<(ulong,byte[])>>]
public partial class Witness;

[GenerateShapeFor<Type>]
public partial class Witness;

/// <summary>
/// GameWorld: per-tick state replication. Owns the <see cref="WorldTickMessage"/> wire format,
/// inbound/outbound tick buffers, and the physics-tick loop that applies remote updates and sends
/// out this peer's own updates for the objects it has authority over.
/// </summary>
public partial class GameWorld
{
    public static WorldTickMessage pendingOutgoingTick = new();

    /// <summary>The objects that may send this time, reused between sends.</summary>
    private static readonly List<GMPObject> candidates = new();
    private static readonly Comparison<GMPObject> ByAccumulatorDescending = (a, b) => b.priorityAccumulator.CompareTo(a.priorityAccumulator);

    /// <summary>Received states in arrival order, applied (and cleared) at the start of the next physics tick.</summary>
    private static readonly List<(ulong id, ulong sender, ulong tick, byte[] state)> pendingStates = new();

    /// <summary>
    /// The sender tick of the state being applied, inside <see cref="GMPObject.ApplyStateUpdate"/>; 0 outside one,
    /// e.g. for a spawn's initState.
    /// </summary>
    public static ulong stateTick { get; private set; }

    /// <summary>
    /// The physics frame of the latest send (<see cref="Engine.GetPhysicsFrames"/>, read once per send). For
    /// <see cref="GMPObject.UpdateSyncPriority"/>, which runs for every object each send, to use instead of asking the
    /// engine itself.
    /// </summary>
    public static ulong sendFrame { get; private set; }

    /// <summary>
    /// States sent per object per second. Physics ticks faster than this (60 Hz) for better collisions, and each object
    /// sends on every <see cref="SendInterval"/>th tick only, which halves the bandwidth. The objects take turns (by id),
    /// so every tick sends some: building thousands of states all on one tick made the host's worst ticks twice as long.
    /// </summary>
    public const int SendRate = 30;

    /// <summary>Physics ticks between an object's states: 2 at 60 Hz physics, 1 at 30 Hz.</summary>
    public static readonly int SendInterval = Math.Max(1, Engine.PhysicsTicksPerSecond / SendRate);

    /// <summary>
    /// Followers play other peers' states back this many ticks behind the newest one received (two sends), so there is
    /// nearly always a newer state to interpolate toward even when packets arrive unevenly.
    /// </summary>
    public static readonly double InterpolationDelay = 2 * SendInterval;

    /// <summary>
    /// Per sending peer: the newest tick heard from it, and the (fractional) tick of its states that followers show now.
    /// Playback runs a little slow while the buffer is short and a little fast while it is long, which keeps it about
    /// <see cref="InterpolationDelay"/> behind.
    /// </summary>
    private class PlaybackClock
    {
        public ulong newestTick;
        public double playbackTick;
    }

    private static readonly Dictionary<ulong, PlaybackClock> playbackClocks = new();

    /// <summary>
    /// How much faster or slower playback runs per tick of buffer above or below <see cref="InterpolationDelay"/>: 0.1
    /// per send's worth of ticks.
    /// </summary>
    private static readonly double PlaybackCorrection = 0.1 / SendInterval;
    /// <summary>Slowest playback runs while the buffer runs dry (it never stops: a quiet sender would hold everything up).</summary>
    private const double MinPlaybackRate = 0.75;
    private const double MaxPlaybackRate = 1.1;
    /// <summary>Further behind than this many ticks, 8 sends (after a pause, a stall, or a sender gone quiet), playback jumps ahead.</summary>
    private static readonly double MaxPlaybackLag = 8 * SendInterval;

    /// <summary>Send time of the newest state accepted per object, used to drop out-of-order updates. Forgotten when an object changes authority.</summary>
    private static readonly Dictionary<ulong, ulong> lastStateTime = new();

    public static MessagePackSerializer pack = new();

    /// <summary>
    /// State bytes this peer sends per second to all other peers together (updates with their ids and framing; not
    /// the transport's own packet headers): split between the connected peers, which changes as they join and leave,
    /// and evenly over the physics ticks (each sends the objects whose turn it is, see <see cref="SendRate"/>). Every
    /// tick fills its share with the objects of highest
    /// <see cref="GMPObject.priorityAccumulator"/>.
    /// <para>
    /// 8 Mbps: what actually got through over Steam in testing (2026-10-15), though both ends had well over 100 Mbps.
    /// Sending 200 Mbps worth, about 1 MB/s of state reached the client and the rest was dropped on the way, without
    /// Steam noticing (its send rate is pinned, below). Sending more than arrives only wastes the host's time building
    /// states, and lets the network pick at random which ones are lost instead of the priorities. Steam's send rate is
    /// set from this when hosting or joining (<see cref="SteamNetwork.Host"/>), so set it before then.
    /// </para>
    /// </summary>
    public static int SendBudget = 8 * 1000 * 1000 / 8;

    /// <summary>
    /// State goes out unreliable (a lost update is replaced by a newer one anyway), in packets of about this many bytes:
    /// a bigger packet is split into fragments, and losing any one fragment loses all of it.
    /// </summary>
    private const int MaxPacketSize = 1100;

    /// <summary>
    /// The most a <see cref="WorldTickMessage"/> adds around its updates: its array header (1), sendTime and tick (up
    /// to 9 each) and the updates list header (up to 3).
    /// </summary>
    private const int PacketHeaderSize = 22;

    /// <summary>
    /// The most each update adds to a packet besides its own bytes: its (id, bytes) pair header (1), the id (9, as ids
    /// are random 64-bit numbers) and the bytes' length header (up to 3).
    /// </summary>
    private const int UpdateOverhead = 13;

    // ---- cost, for the perf window and log (PerfDebug) ----
    // Running totals since startup, in Stopwatch ticks and states; PerfDebug turns them into per-second figures.

    /// <summary>Applying received states at the start of each physics tick.</summary>
    public static long applyCost;
    public static long appliedCount;
    /// <summary>Working out every object's priority and generating the states of those that are sent, each send.</summary>
    public static long buildCost;
    public static long builtCount;
    /// <summary>Serializing the packets and handing them to the network, each send.</summary>
    public static long sendCost;
    public static long sentCount;
    /// <summary>Reading received state packets (as they arrive, in <see cref="Lobby"/>'s _Process).</summary>
    public static long receiveCost;
    public static long receivedCount;
    /// <summary>State packet bytes handed to the network (once per peer sent to) and received, without transport headers.</summary>
    public static long sentBytes;
    public static long receivedBytes;

    private static void ResetStateSync()
    {
        pendingOutgoingTick = new();
        pendingStates.Clear();
        lastStateTime.Clear();
        playbackClocks.Clear();
    }

    /// <summary>
    /// Which tick of <paramref name="peer"/>'s states followers should show now: fractional, about
    /// <see cref="InterpolationDelay"/> behind the newest one received. 0 if nothing has been heard from it.
    /// </summary>
    public static double PlaybackTick(ulong peer)
    {
        if (playbackClocks.TryGetValue(peer, out PlaybackClock clock))
        {
            return clock.playbackTick;
        }
        return 0;
    }

    /// <summary>Each physics tick: moves every peer's playback on by about one tick (see <see cref="PlaybackClock"/>).</summary>
    private static void AdvancePlaybackClocks()
    {
        foreach (PlaybackClock clock in playbackClocks.Values)
        {
            double buffered = clock.newestTick - clock.playbackTick;
            if (buffered > InterpolationDelay + MaxPlaybackLag)
            {
                clock.playbackTick = clock.newestTick - InterpolationDelay;
                continue;
            }
            double rate = 1 + (buffered - InterpolationDelay) * PlaybackCorrection;
            rate = Math.Clamp(rate, MinPlaybackRate, MaxPlaybackRate);
            clock.playbackTick += rate;
        }
    }

    private static void OnMessageReceived(ulong from, Channel ch, byte[] msg)
    {
        if (ch != Channel.GAME_State)
        {
            return;
        }
        // Paused (loading, or frozen for a resync): nothing is applied until the world moves again, and a restore
        // replaces everything anyway, so don't pile states up. Moving objects resend every tick; resting ones soon.
        if (!started || instance.GetTree().Paused)
        {
            return;
        }
        long receiveStart = Stopwatch.GetTimestamp();
        receivedBytes += msg.Length;
        WorldTickMessage tickmsg = pack.Deserialize<WorldTickMessage>(msg);
        if (!playbackClocks.TryGetValue(from, out PlaybackClock clock))
        {
            clock = new PlaybackClock { newestTick = tickmsg.tick, playbackTick = tickmsg.tick - InterpolationDelay };
            playbackClocks[from] = clock;
        }
        if (tickmsg.tick > clock.newestTick)
        {
            clock.newestTick = tickmsg.tick;
        }
        // Merge per object: a tick only carries the objects that won the sender's priority budget, so
        // keeping just the newest tick per sender would lose updates carried only by an older one.
        foreach ((ulong id, byte[] state) in tickmsg.updates)
        {
            // Only the current authority's updates count (drops stragglers from the one before).
            if (!syncedObjs.TryGetValue(id, out GMPObject gmpo) || gmpo.authority != from)
            {
                continue;
            }
            if (lastStateTime.TryGetValue(id, out ulong last) && tickmsg.sendTime < last)
            {
                continue;
            }
            lastStateTime[id] = tickmsg.sendTime;
            pendingStates.Add((id, from, tickmsg.tick, state));
        }
        receivedCount += tickmsg.updates.Count;
        receiveCost += Stopwatch.GetTimestamp() - receiveStart;
    }

    public override void _PhysicsProcess(double delta)
    {
        // Paused (loading, or frozen for a resync): the world isn't moving, so there is nothing to send or apply.
        if (!started || GetTree().Paused)
        {
            return;
        }
        CheckLeases();
        long applyStart = Stopwatch.GetTimestamp();
        appliedCount += pendingStates.Count;
        foreach ((ulong id, ulong sender, ulong tick, byte[] state) in pendingStates)
        {
            // Only the object's current authority may move it (drops stragglers after a Claim).
            if (syncedObjs.TryGetValue(id, out GMPObject gmpo) && gmpo.authority == sender)
            {
                stateTick = tick;
                gmpo.ApplyStateUpdate(state);
            }
        }
        stateTick = 0;
        pendingStates.Clear();
        applyCost += Stopwatch.GetTimestamp() - applyStart;
        AdvancePlaybackClocks();
        GMPOBox3DBody.PlayFollowers(delta, GetViewport().GetCamera3D());
        CheckSleepers();
        CheckKillPlane();

        // Nobody to send to (solo, or the last peer left): skip building updates, which costs per object per tick.
        if (Lobby.MemberCount <= 1)
        {
            return;
        }
        // Each object sends on every SendInterval'th tick, the ones whose id gives this tick's turn, so every tick sends
        // some of them (received ones are applied every tick, above).
        ulong frame = Engine.GetPhysicsFrames();
        ulong turn = frame % (ulong)SendInterval;
        sendFrame = frame;
        long buildStart = Stopwatch.GetTimestamp();
        // Packets go out part way through; their cost is counted in sendCost, so it is taken off buildCost below.
        long sendCostBefore = sendCost;

        // Every object whose turn it is gains its priority, then they send highest accumulator first until this tick's
        // share of the budget is used up. A sent object starts again from 0; the rest keep what they have, so they
        // rise up the order until they get sent.
        int sendSize = 0;
        int packetSize = PacketHeaderSize;
        // Every peer is sent the same packets, so each one's share of the budget is what one tick may hold.
        int peers = Lobby.MemberCount - 1;
        int maxSendSize = SendBudget / (SendRate * SendInterval) / peers;
        candidates.Clear();
        foreach (var entity in syncedObjs.Values)
        {
            if (entity.authority != Lobby.selfPeerID || entity.id % (ulong)SendInterval != turn)
            {
                continue;
            }
            entity.UpdateSyncPriority();
            if (entity.priority <= -1)
            {
                // never sends state
                continue;
            }
            entity.priorityAccumulator += entity.priority;
            candidates.Add(entity);
        }
        // Sorted in place (no per-tick allocation).
        candidates.Sort(ByAccumulatorDescending);
        foreach (GMPObject e in candidates)
        {
            byte[] update = e.GenerateStateUpdate();
            builtCount++;
            if (update == null || update.Length == 0)
            {
                e.priorityAccumulator = 0;
                continue;
            }
            int entrySize = update.Length + UpdateOverhead;
            if (sendSize + entrySize > maxSendSize)
            {
                // This send is full. Building the states of everything further down only to drop them cost the host
                // most of its frame with thousands of objects, so they wait for the next send.
                break;
            }
            if (pendingOutgoingTick.updates.Count > 0 && packetSize + entrySize > MaxPacketSize)
            {
                SendPendingTick();
                packetSize = PacketHeaderSize;
            }
            sendSize += entrySize;
            packetSize += entrySize;
            pendingOutgoingTick.updates.Add((e.id, update));
            e.priorityAccumulator = 0;
        }
        if (pendingOutgoingTick.updates.Count > 0)
        {
            SendPendingTick();
        }
        buildCost += Stopwatch.GetTimestamp() - buildStart - (sendCost - sendCostBefore);
    }

    private static void SendPendingTick()
    {
        long sendStart = Stopwatch.GetTimestamp();
        sentCount += pendingOutgoingTick.updates.Count;
        pendingOutgoingTick.sendTime = netTime;
        pendingOutgoingTick.tick = Engine.GetPhysicsFrames();
        byte[] packet = pack.Serialize<WorldTickMessage>(pendingOutgoingTick);
        sentBytes += packet.Length * (Lobby.MemberCount - 1);
        Lobby.SendToAllExceptSelf(Channel.GAME_State, packet, Network.k_nSteamNetworkingSend_Unreliable | Network.k_nSteamNetworkingSend_NoNagle);
        pendingOutgoingTick.updates.Clear();
        sendCost += Stopwatch.GetTimestamp() - sendStart;
    }
}
