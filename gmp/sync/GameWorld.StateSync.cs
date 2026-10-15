using Godot;
using Nerdbank.MessagePack;
using PolyType;
using System;
using System.Collections.Generic;
using System.Linq;

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

    /// <summary>This tick's objects with an update due, reused between ticks.</summary>
    private static readonly List<GMPObject> dueUpdates = new();
    private static readonly Comparison<GMPObject> ByAccumulatorDescending = (a, b) => b.priorityAccumulator.CompareTo(a.priorityAccumulator);

    /// <summary>Received states in arrival order, applied (and cleared) at the start of the next physics tick.</summary>
    private static readonly List<(ulong id, ulong sender, ulong tick, byte[] state)> pendingStates = new();

    /// <summary>
    /// The sender tick of the state being applied, inside <see cref="GMPObject.ApplyStateUpdate"/>; 0 outside one,
    /// e.g. for a spawn's initState.
    /// </summary>
    public static ulong stateTick { get; private set; }

    /// <summary>
    /// Followers play other peers' states back this many ticks behind the newest one received, so there is nearly always
    /// a newer state to interpolate toward even when packets arrive unevenly.
    /// </summary>
    public const double InterpolationDelay = 2;

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

    /// <summary>How much faster or slower playback runs per tick of buffer above or below <see cref="InterpolationDelay"/>.</summary>
    private const double PlaybackCorrection = 0.1;
    /// <summary>Slowest playback runs while the buffer runs dry (it never stops: a quiet sender would hold everything up).</summary>
    private const double MinPlaybackRate = 0.75;
    private const double MaxPlaybackRate = 1.1;
    /// <summary>Further behind than this many ticks (after a pause, a stall, or a sender gone quiet), playback jumps ahead.</summary>
    private const double MaxPlaybackLag = 8;

    /// <summary>Send time of the newest state accepted per object, used to drop out-of-order updates. Forgotten when an object changes authority.</summary>
    private static readonly Dictionary<ulong, ulong> lastStateTime = new();

    public static MessagePackSerializer pack = new();

    /// <summary>
    /// Outbound state bytes allowed per physics tick: 10 MB/s spread over the physics rate. Keep it within the
    /// transport's send rate (<see cref="SteamNetwork.Host"/>), or Steam queues the excess until its sends fail.
    /// </summary>
    private static readonly int maxTickSize = 10 * 1024 * 1024 / Engine.PhysicsTicksPerSecond;

    /// <summary>
    /// An object's update is due once its <see cref="GMPObject.priorityAccumulator"/> reaches this. Priority accumulates
    /// every tick, so at 30 Hz a priority of 30 or more sends every tick, 10 every third tick and 1 about once a second.
    /// </summary>
    public const int SendThreshold = 30;

    /// <summary>
    /// State goes out unreliable (a lost update is replaced by a newer one anyway), in packets of about this many bytes:
    /// a bigger packet is split into fragments, and losing any one fragment loses all of it.
    /// </summary>
    private const int MaxPacketSize = 1100;

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
    }

    public override void _PhysicsProcess(double delta)
    {
        // Paused (loading, or frozen for a resync): the world isn't moving, so there is nothing to send or apply.
        if (!started || GetTree().Paused)
        {
            return;
        }
        CheckLeases();
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
        AdvancePlaybackClocks();
        CheckSleepers();

        // Nobody to send to (solo, or the last peer left): skip building updates, which costs per object per tick.
        if (Lobby.MemberCount <= 1)
        {
            return;
        }

        int tickSize = 0;
        int packetSize = 0;
        dueUpdates.Clear();
        foreach (var entity in syncedObjs.Values)
        {
            if (entity.authority!=Lobby.selfPeerID)
            {
                //not mine hands off
                continue;
            }
            if (entity.priority > -1)
            {
                // priority -1 (sleepy boi) never accumulates, but still sends anything queued for it
                entity.priorityAccumulator += entity.priority;
            }
            if (entity.priorityAccumulator >= SendThreshold)
            {
                dueUpdates.Add(entity);
            }
        }
        // Highest accumulated priority first, sorted in place (no per-tick allocation).
        dueUpdates.Sort(ByAccumulatorDescending);
        foreach (GMPObject e in dueUpdates)
        {
            byte[] update = e.GenerateStateUpdate();
            if (update == null || update.Length ==0)
            {
                e.priorityAccumulator = 0;
                continue;
            }
            if (tickSize + update.Length > maxTickSize)
            {
                break;
            }
            if (packetSize > 0 && packetSize + update.Length > MaxPacketSize)
            {
                SendPendingTick();
                packetSize = 0;
            }
            tickSize += update.Length;
            packetSize += update.Length + 10; // plus the id and framing
            pendingOutgoingTick.updates.Add((e.id, update));
            e.priorityAccumulator = 0;
        }
        if (pendingOutgoingTick.updates.Count > 0)
        {
            SendPendingTick();
        }
    }

    private static void SendPendingTick()
    {
        pendingOutgoingTick.sendTime = netTime;
        pendingOutgoingTick.tick = Engine.GetPhysicsFrames();
        byte[] packet = pack.Serialize<WorldTickMessage>(pendingOutgoingTick);
        Lobby.SendToAllExceptSelf(Channel.GAME_State, packet, Network.k_nSteamNetworkingSend_Unreliable | Network.k_nSteamNetworkingSend_NoNagle);
        pendingOutgoingTick.updates.Clear();
    }
}
