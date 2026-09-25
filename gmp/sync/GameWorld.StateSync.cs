using Godot;
using Nerdbank.MessagePack;
using PolyType;
using System;
using System.Collections.Generic;
using System.Linq;

[GenerateShape]
public partial record WorldTickMessage
{
    public ulong tick = 0;
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

    /// <summary>Newest received state per object, applied (and cleared) at the start of the next physics tick.</summary>
    private static readonly Dictionary<ulong, (ulong sender, byte[] state)> pendingStates = new();

    /// <summary>Sender and tick of the newest state accepted per object, used to drop out-of-order updates.</summary>
    private static readonly Dictionary<ulong, (ulong sender, ulong tick)> lastStateTick = new();

    public static MessagePackSerializer pack = new();

    /// <summary>Outbound state bytes allowed per physics tick: 500 KB/s spread over the physics rate.</summary>
    private static readonly int baseMaxTickSize = 1024 * 500 / Engine.PhysicsTicksPerSecond;

    /// <summary>This session's per-tick budget; the host gets 4× (set in <see cref="Preload"/>).</summary>
    private static int maxTickSize = baseMaxTickSize;
    /// <summary>Count of GAME_State tick messages received from remote peers (used by the headless smoke test).</summary>
    public static int inboundTickCount = 0;

    private static void ResetStateSync()
    {
        pendingOutgoingTick = new();
        pendingStates.Clear();
        lastStateTick.Clear();
        inboundTickCount = 0;
    }

    private static void OnMessageReceived(ulong from, Channel ch, byte[] msg)
    {
        if (ch != Channel.GAME_State)
        {
            return;
        }
        inboundTickCount++;
        WorldTickMessage tickmsg = pack.Deserialize<WorldTickMessage>(msg);
        // Merge per object: a tick only carries the objects that won the sender's priority budget, so
        // keeping just the newest tick per sender would lose updates carried only by an older one.
        foreach ((ulong id, byte[] state) in tickmsg.updates)
        {
            // Tick numbers are per sender; after an authority change the new sender's ticks start fresh.
            if (lastStateTick.TryGetValue(id, out var last) && last.sender == from && tickmsg.tick < last.tick)
            {
                continue;
            }
            lastStateTick[id] = (from, tickmsg.tick);
            pendingStates[id] = (from, state);
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (started)
        {
            tickNum++;
        }
        else
        {
            return;
        }
        foreach ((ulong id, (ulong sender, byte[] state)) in pendingStates)
        {
            if (syncedObjs.TryGetValue(id, out GMPObject gmpo))
            {
                // Only the object's current authority may move it (drops stragglers after a Claim).
                if (gmpo.authority == sender)
                {
                    gmpo.ApplyStateUpdate(state);
                }
            }
            else
            {
                //Logging.Warn($"State update for unknown object {id} (not spawned yet, or already despawned)", "GameWorld");
            }
        }
        pendingStates.Clear();


        int tickSize = 0;
        foreach (var entity in syncedObjs.Values)
        {
            if (entity.authority!=Lobby.selfPeerID)
            {
                //not mine hands off
                continue;
            }
            else if (entity.priority <=-1)
            {
                //sleepy boi hands off
                continue;
            }
            else
            {
                entity.priorityAccumulator += entity.priority;
            }
        }
        List<GMPObject> temp = syncedObjs.Values.OrderByDescending(e => e.priorityAccumulator).ToList();
        foreach (GMPObject e in temp)
        {
            if (e.authority == Lobby.selfPeerID && e.priorityAccumulator > 0)
            {
                byte[] update = e.GenerateStateUpdate();
                if (update == null || update.Length ==0)
                {
                    e.priorityAccumulator = 0;
                    continue;
                }
                if (tickSize + update.Length <= maxTickSize)
                {
                    tickSize += update.Length;
                    pendingOutgoingTick.updates.Add((e.id, update));
                    e.priorityAccumulator = 0;
                }
                else
                {
                    break;
                }
            }
        }
        if (pendingOutgoingTick.updates.Count > 0)
        {
            pendingOutgoingTick.tick = tickNum;
            Lobby.SendToAllExceptSelf(Channel.GAME_State, pack.Serialize<WorldTickMessage>(pendingOutgoingTick));
            pendingOutgoingTick = new();
        }

    }
}
