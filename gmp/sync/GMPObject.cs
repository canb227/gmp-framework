using Godot;
using Nerdbank.MessagePack;
using Nerdbank.MessagePack.Godot;
using PolyType;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
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
    /// This object's priority within the per-tick state-update byte budget. Accumulates into
    /// <see cref="priorityAccumulator"/> every tick; whichever objects have the highest accumulator
    /// send first once the budget is reached. A value of -1 or lower means this object never sends
    /// a state update at all.
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

    /// <summary>Running per-tick accumulation of <see cref="priority"/>, used to decide send order within the state-update byte budget. Reset to 0 when this object's update is sent, or when <see cref="GenerateStateUpdate"/> returns nothing.</summary>
    public int priorityAccumulator { get; set; }

    /// <summary>The most recent state stored by <see cref="ApplyStateUpdate"/>. Non-authority peers consume it in <c>_PhysicsProcess</c>; the authority reads it once in <c>AfterInit</c> to move to its spawn state.</summary>
    public byte[] desiredState { get; set; }


    public static MessagePackSerializer serializer = new MessagePackSerializer().WithGodotConverters();

    /// <summary>Called on the authority peer, in priority order, for each object whose accumulator is above 0, until the tick's byte budget is full (the first update that doesn't fit is discarded and sending stops for that tick). Serializes this object's current state for every other peer to apply via <see cref="ApplyStateUpdate"/>. Return null/empty to send nothing.</summary>
    public byte[] GenerateStateUpdate();

    /// <summary>Called on non-authority peers when a state update arrives from the authority, and on <b>every</b> peer (authority included) by <see cref="Init"/> with the spawn-time <c>initState</c>. The transform-synced bases just store it into <see cref="desiredState"/>; overrides may apply it immediately (e.g. FactoryPlayer).</summary>
    public void ApplyStateUpdate(byte[] update);

    /// <summary>Sets ProcessMode from <c>pauseable</c>. Named so it can never collide with Godot's <c>_Ready</c>.</summary>
    public virtual void ApplyProcessMode()
    {
        if (pauseable)
        {
            (this as Node).ProcessMode = ProcessModeEnum.Pausable;
        }
        else
        {
            (this as Node).ProcessMode = ProcessModeEnum.Always;
        }
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
        if (this.priority == 0 && init.priority!=0)
        {
            this.priority = init.priority;
        }
        else if (this.priority==0)
        {
            this.priority = 1;
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
    /// Called on every peer after <see cref="authority"/> changes hands (see <c>GameWorld.Claim</c>).
    /// Override to switch between simulating (authority) and following replicated state.
    /// </summary>
    public virtual void OnAuthorityChanged()
    {
    }

    /// <summary>
    /// This is called on all GMPO objects after they have been fully constructed and configured. The initState byte[] has already been applied.
    /// </summary>
    protected void AfterInit();

}

