using Godot;
using Nerdbank.MessagePack;
using PolyType;
using System;
using System.Collections.Generic;
using System.Linq;

[GenerateShape]
public partial record WorldTickMessage
{
    /// <summary>When it was sent (<see cref="GameWorld.netTime"/>).</summary>
    public ulong sendTime = 0;
    public List<(ulong, byte[])> updates = new();
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

    /// <summary>Newest received state per object, applied (and cleared) at the start of the next physics tick.</summary>
    private static readonly Dictionary<ulong, (ulong sender, byte[] state)> pendingStates = new();

    /// <summary>Send time of the newest state accepted per object, used to drop out-of-order updates. Forgotten when an object changes authority.</summary>
    private static readonly Dictionary<ulong, ulong> lastStateTime = new();

    public static MessagePackSerializer pack = new();

    /// <summary>Outbound state bytes allowed per physics tick: 500 KB/s spread over the physics rate.</summary>
    private static readonly int baseMaxTickSize = 1024 * 500 / Engine.PhysicsTicksPerSecond;

    /// <summary>This session's per-tick budget; the host gets 4× (set in <see cref="Preload"/>).</summary>
    private static int maxTickSize = baseMaxTickSize;

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
    }

    private static void OnMessageReceived(ulong from, Channel ch, byte[] msg)
    {
        if (ch != Channel.GAME_State)
        {
            return;
        }
        WorldTickMessage tickmsg = pack.Deserialize<WorldTickMessage>(msg);
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
            pendingStates[id] = (from, state);
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        // Paused (loading, or frozen for a resync): the world isn't moving, so there is nothing to send or apply.
        // Received states wait in pendingStates; a restore clears them.
        if (!started || GetTree().Paused)
        {
            return;
        }
        CheckLeases();
        foreach ((ulong id, (ulong sender, byte[] state)) in pendingStates)
        {
            // Only the object's current authority may move it (drops stragglers after a Claim).
            if (syncedObjs.TryGetValue(id, out GMPObject gmpo) && gmpo.authority == sender)
            {
                gmpo.ApplyStateUpdate(state);
            }
        }
        pendingStates.Clear();
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
        byte[] packet = pack.Serialize<WorldTickMessage>(pendingOutgoingTick);
        Lobby.SendToAllExceptSelf(Channel.GAME_State, packet, Network.k_nSteamNetworkingSend_Unreliable | Network.k_nSteamNetworkingSend_NoNagle);
        pendingOutgoingTick.updates.Clear();
    }
}
