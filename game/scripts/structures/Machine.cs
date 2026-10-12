using Godot;
using PolyType;

[GenerateShape]
public partial record struct MachineSave
{
    public bool isOperating;
    public float processTimeRemaining;
    /// <summary>Whatever the machine itself adds (<see cref="Machine.SaveMachineState"/>), or null.</summary>
    public byte[] extra;
}

/// <summary>
/// A structure that works in cycles: when idle it tries to start one (<see cref="TryStartProcess"/>, usually by
/// taking an item from its <see cref="inputVolumes"/>), then after <see cref="processTime"/> (sped up by
/// <see cref="speedMultiplier"/>) it finishes it (<see cref="FinishProcess"/>, usually by spawning items at its
/// <see cref="outputLocations"/>). The cycle runs on the machine's authority; every peer is told when it starts
/// and stops operating (<see cref="OnOperatingChanged"/>) for sounds and visuals.
/// </summary>
public partial class Machine : Structure
{
    /// <summary>Sensor bodies items are taken in from.</summary>
    [Export] public Godot.Collections.Array<Node3D> inputVolumes = new();
    /// <summary>Where outputs spawn (see <see cref="OutputPosition"/>).</summary>
    [Export] public Godot.Collections.Array<Node3D> outputLocations = new();
    /// <summary>Seconds one cycle takes at a speed multiplier of 1.</summary>
    [Export] public float processTime = 1f;
    [Export] public float speedMultiplier = 1f;

    /// <summary>Seconds left in the current cycle (authority only).</summary>
    public float processTimeRemaining = 0f;
    /// <summary>Whether a cycle is running, on every peer.</summary>
    public bool isOperating { get; private set; }

    public override void _PhysicsProcess(double delta)
    {
        if (!runsMachineLogic) return;

        if (isOperating)
        {
            processTimeRemaining -= (float)delta * speedMultiplier;
            if (processTimeRemaining <= 0)
            {
                FinishProcess();
                RPCManager.RPC(this, nameof(SetOperating), [false]);
            }
        }
        else if (TryStartProcess())
        {
            processTimeRemaining = processTime;
            RPCManager.RPC(this, nameof(SetOperating), [true]);
        }
    }

    /// <summary>Authority only, while idle: start a cycle if there's something to do (take the input) and return true.</summary>
    protected virtual bool TryStartProcess()
    {
        return false;
    }

    /// <summary>Authority only: the current cycle is done (spawn the outputs).</summary>
    protected virtual void FinishProcess()
    {
    }

    /// <summary>Every peer, when <see cref="isOperating"/> changes: sounds, animation, particles.</summary>
    protected virtual void OnOperatingChanged()
    {
    }

    [RPC(requireAuthority = true)]
    void SetOperating(bool operating)
    {
        isOperating = operating;
        OnOperatingChanged();
    }

    /// <summary>Extra state a machine saves on top of its cycle (e.g. the item it's holding), or null.</summary>
    protected virtual byte[] SaveMachineState()
    {
        return null;
    }

    /// <summary>Reads back what <see cref="SaveMachineState"/> returned (never called with null). Must be silent.</summary>
    protected virtual void LoadMachineState(byte[] state)
    {
    }

    public override byte[] SaveState()
    {
        return GMPObject.serializer.Serialize(new MachineSave { isOperating = isOperating, processTimeRemaining = processTimeRemaining, extra = SaveMachineState() });
    }

    public override void LoadState(byte[] state)
    {
        MachineSave s = GMPObject.serializer.Deserialize<MachineSave>(state);
        isOperating = s.isOperating;
        processTimeRemaining = s.processTimeRemaining;
        if (s.extra != null)
        {
            LoadMachineState(s.extra);
        }
    }

    /// <summary>
    /// The synced items in the input volumes, skipping players, structures, items being held and items already
    /// despawning.
    /// </summary>
    protected System.Collections.Generic.List<PhysicalFactoryItem> ItemsInInputs()
    {
        System.Collections.Generic.List<PhysicalFactoryItem> items = new();
        foreach (Node3D volume in inputVolumes)
        {
            // Dispose the Godot wrappers here rather than leave them to the garbage collector.
            using Variant result = volume.Call(Box3DNames.getOverlappingBodies);
            using Godot.Collections.Array bodies = result.AsGodotArray();
            for (int i = 0; i < bodies.Count; i++)
            {
                if (bodies[i].AsGodotObject() is PhysicalFactoryItem item && GameWorld.syncedObjs.ContainsKey(item.id) && !GameWorld.heldBy.ContainsKey(item.id))
                {
                    items.Add(item);
                }
            }
        }
        return items;
    }

    /// <summary>Where the output'th output spawns; the machine's own position (with a warning) if it has no such output.</summary>
    protected Vector3 OutputPosition(int output = 0)
    {
        if (output >= outputLocations.Count)
        {
            Logging.Warn($"{Name} has no output location {output}; spawning at its root", "Machine");
            return GlobalPosition;
        }
        return outputLocations[output].GlobalPosition;
    }
}
