using Godot;
using System.Collections.Generic;

/// <summary>
/// A machine that takes items in through its <see cref="inputVolumes"/> (tracked in <see cref="inputItems"/>) and
/// puts them out at its <see cref="outputLocations"/>. Each machine decides what it takes and spawns in
/// <see cref="Machine.TryStartProcess"/> and <see cref="Machine.FinishProcess"/>.
/// </summary>
public abstract partial class IOMachine : Machine
{
    /// <summary>Sensor bodies items are taken in from. Assumed not to overlap.</summary>
    [Export] public Godot.Collections.Array<Node3D> inputVolumes = new();
    [Export] public Godot.Collections.Array<Node3D> outputLocations = new();

    /// <summary>Authority only: a body entered or left an input volume (<see cref="inputItems"/> is already updated).</summary>
    protected virtual void OnInputEntered(Node3D body) { }
    protected virtual void OnInputExited(Node3D body) { }

    /// <summary>Authority only: the items in the input volumes, oldest first.</summary>
    public List<PhysicalFactoryItem> inputItems = new();


    public override void _Ready()
    {
        base._Ready();
        foreach (Node3D volume in inputVolumes)
        {
            volume.Connect(Box3DNames.areaEntered, Callable.From<Node3D>(InputVolumeEntered));
            volume.Connect(Box3DNames.areaExited, Callable.From<Node3D>(InputVolumeExited));
        }
    }

    public override void AfterInit()
    {
        base.AfterInit();
        CheckInputs();
    }

    public override void OnAuthorityChanged()
    {
        base.OnAuthorityChanged();
        CheckInputs();
    }

    public override void _PhysicsProcess(double delta)
    {
        if (runsMachineLogic)
        {
            // An item despawned while inside (picked up, eaten elsewhere) might never send an exit.
            inputItems.RemoveAll(item => !GameWorld.syncedObjs.ContainsKey(item.id));

        }
        base._PhysicsProcess(delta);
    }

    // ---- inputs ----

    void InputVolumeEntered(Node3D body)
    {
        if (!runsMachineLogic) return;
        if (body is PhysicalFactoryItem item && GameWorld.syncedObjs.ContainsKey(item.id) && !inputItems.Contains(item))
        {
            inputItems.Add(item);
        }
        OnInputEntered(body);
    }

    void InputVolumeExited(Node3D body)
    {
        if (!runsMachineLogic) return;
        if (body is PhysicalFactoryItem item) inputItems.Remove(item);
        OnInputExited(body);
    }

    // Starts over from what's in the volumes now, as items already inside never send an enter.
    void CheckInputs()
    {
        inputItems.Clear();
        if (!runsMachineLogic) return;

        foreach (Node3D volume in inputVolumes)
        {
            // Dispose the Godot wrappers here rather than leave them to the garbage collector.
            using Variant result = volume.Call(Box3DNames.getOverlappingBodies);
            using Godot.Collections.Array bodies = result.AsGodotArray();
            for (int i = 0; i < bodies.Count; i++)
            {
                if (bodies[i].AsGodotObject() is PhysicalFactoryItem item && GameWorld.syncedObjs.ContainsKey(item.id) && !inputItems.Contains(item))
                {
                    inputItems.Add(item);
                }
            }
        }
    }

    /// <summary>Authority only: takes <paramref name="item"/> out of the inputs and despawns it.</summary>
    protected void ConsumeInputItem(PhysicalFactoryItem item)
    {
        inputItems.Remove(item);
        GameWorld.DespawnObject(item.id, DespawnReason.Consumed);
    }

    // ---- outputs ----

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