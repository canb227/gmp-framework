using Godot;
using Nerdbank.MessagePack;
using Nerdbank.MessagePack.Godot;
using PolyType;
using System;
using static Godot.Node;

[GenerateShape]
public partial record struct GMPOInitData : IEquatable<GMPOInitData>
{
    public ulong id;
    public ulong authority;
    public ulong owner;
    public int priority;
    public bool pauseable;  

    public GMPOInitData(ulong id, ulong authority, ulong owner, int priority, bool pauseable)
    {
        this.id = id;
        this.authority = authority;
        this.owner = owner;
        this.priority = priority;
        this.pauseable = pauseable;
    }
}


/// <summary>
/// Contract for a networked, spawnable object. Godot never sees this interface directly — it is a
/// plain C# interface, so `[Export]` here has no effect and Godot never calls its default methods.
/// Each concrete GMPO base (Node3D/Box3D wrapper) re-declares its own `[Export]`ed backing members.
/// </summary>
[GenerateShape]
public partial interface GMPObject
{
    /// <summary>
    /// How much this object's state matters, relative to others. Added to <see cref="priorityAccumulator"/> on every
    /// send (30 a second, <c>GameWorld.SendRate</c>), and each send fills its byte budget (<c>GameWorld.SendBudget</c>)
    /// with the highest accumulators first. With budget to spare everything sends every time; when it is short, an
    /// object with priority 30 sends about 30 times as often as one with 1. A value of -1 or lower means this object
    /// never sends a state update at all.
    /// </summary>
    public int priority { get; set; }

    /// <summary>Whether this object's <see cref="Godot.Node.ProcessMode"/> should stay <c>Pausable</c> (true) or <c>Always</c> (false). Applied by <see cref="ApplyProcessMode"/>.</summary>
    public bool pauseable { get; set; }

    /// <summary>The network-wide unique id assigned to this object when it was spawned.</summary>
    public ulong id { get; set; }

    /// <summary>The peer id that owns this object's simulation. The authority's <see cref="GenerateStateUpdate"/> output is sent to every other peer.</summary>
    public ulong authority { get; set; }

    /// <summary>The peer id this object is considered to belong to (e.g. the controlling player), independent of simulation authority.</summary>
    public ulong owner { get; set; }

    /// <summary>Running per-send accumulation of <see cref="priority"/>, used to decide send order within the state-update byte budget. Reset to 0 when this object's update is sent, or when <see cref="GenerateStateUpdate"/> returns nothing.</summary>
    public int priorityAccumulator { get; set; }

    /// <summary>The most recent state stored by <see cref="ApplyStateUpdate"/>. Non-authority peers consume it in <c>_PhysicsProcess</c>; the authority reads it once in <c>AfterInit</c> to move to its spawn state.</summary>
    public byte[] desiredState { get; set; }


    public static MessagePackSerializer serializer = new MessagePackSerializer().WithGodotConverters();

    /// <summary>Called on the authority peer on every send, highest <see cref="priorityAccumulator"/> first, for each object that may send (an update that doesn't fit what is left of the budget is discarded, and the object keeps its accumulator for next time). Serializes this object's current state for every other peer to apply via <see cref="ApplyStateUpdate"/>. Return null/empty to send nothing.</summary>
    public byte[] GenerateStateUpdate();

    /// <summary>
    /// Called on the authority peer on every send, just before <see cref="priority"/> is added to
    /// <see cref="priorityAccumulator"/>: a chance to set <see cref="priority"/> from what is happening now (how near a
    /// player is, being held). Does nothing unless overridden.
    /// </summary>
    public void UpdateSyncPriority()
    {
    }

    /// <summary>Called on non-authority peers when a state update arrives from the authority, and on <b>every</b> peer (authority included) by <see cref="Init"/> with the spawn-time <c>initState</c>. The transform-synced bases just store it into <see cref="desiredState"/>; overrides may apply it immediately (e.g. FactoryPlayer).</summary>
    public void ApplyStateUpdate(byte[] update);

    /// <summary>
    /// Runtime state a save must keep beyond the regular state update, e.g. a machine's progress or a lever's
    /// position; null (the default) for none. Saves already keep <see cref="GenerateStateUpdate"/>'s output (handed back
    /// as the spawn <c>initState</c>), so this is only what that leaves out. Read back by <see cref="LoadState"/>.
    /// </summary>
    public virtual byte[] SaveState()
    {
        return null;
    }

    /// <summary>
    /// Called on every restoring peer with what <see cref="SaveState"/> returned, after the whole saved world has been
    /// spawned (so other saved objects can be looked up). Not called when <see cref="SaveState"/> returned null.
    /// Set state silently: no event hooks, sounds, animations or spawns, which would replay on every load and join
    /// (and a spawn would race the other peers' restores). Snap visuals straight to the restored state.
    /// </summary>
    public virtual void LoadState(byte[] state)
    {
    }

    /// <summary>Sets ProcessMode from <c>pauseable</c>. Named so it can never collide with Godot's <c>_Ready</c>.</summary>
    public virtual void ApplyProcessMode()
    {
        (this as Node).ProcessMode = pauseable ? ProcessModeEnum.Pausable : ProcessModeEnum.Always;
    }


    /// <summary>
    /// Runs on every peer right after this object is spawned. Applies the fields from <paramref name="init"/>,
    /// applies <paramref name="initState"/> via <see cref="ApplyStateUpdate"/> if one was provided, then calls
    /// <see cref="AfterInit"/>.
    /// </summary>
    public virtual void Init(GMPOInitData init, byte[] initState = null)
    {
        this.id = init.id;
        this.authority = init.authority;
        if (this.priority == 0)
        {
            this.priority = init.priority != 0 ? init.priority : 1;
        }

        this.pauseable = init.pauseable;
        this.owner = init.owner;
        ApplyProcessMode();

        if (initState != null)
        {
            ApplyStateUpdate(initState);
        }
        AfterInit();
    }

    /// <summary>
    /// Called on every peer after <see cref="authority"/> changes hands (see <c>GameWorld.Claim</c>), including when
    /// a peer leaves and its objects go to the host.
    /// Override to switch between simulating (authority) and following replicated state.
    /// </summary>
    public virtual void OnAuthorityChanged()
    {
    }

    /// <summary>Called on every peer just before this object is freed by a despawn, with why it was despawned.</summary>
    public virtual void OnDespawned(DespawnReason reason)
    {
    }

    /// <summary>
    /// This is called on all GMPO objects after they have been fully constructed and configured. The initState byte[] has already been applied.
    /// </summary>
    protected void AfterInit();

}

