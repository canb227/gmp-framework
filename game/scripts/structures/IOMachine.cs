using Godot;
using System.Collections.Generic;

/// <summary>
/// A machine that takes items in through its <see cref="inputVolumes"/> (tracked in <see cref="inputItems"/>) and
/// puts them out at its <see cref="outputLocations"/>. Each machine decides what it takes and spawns in
/// <see cref="Machine.TryStartProcess"/> and <see cref="Machine.FinishProcess"/>.
/// </summary>
public abstract partial class IOMachine : Machine
{
    /// <summary>Sensor shapes (GMPShape with is_sensor) items are taken in from. Assumed not to overlap.</summary>
    [Export] public Godot.Collections.Array<Node3D> inputVolumes = new();
    [Export] public Godot.Collections.Array<Node3D> outputLocations = new();

    /// <summary>Authority only: an item entered or left an input volume (<see cref="inputItems"/> is already updated).</summary>
    protected virtual void OnInputEntered(ItemRecord item) { }
    protected virtual void OnInputExited(ItemRecord item) { }

    /// <summary>Authority only: the items in the input volumes, oldest first.</summary>
    public List<ItemRecord> inputItems = new();

    static readonly StringName enteredName = "entered";
    static readonly StringName exitedName = "exited";
    static readonly StringName getOverlappingName = "get_overlapping";

    public override void _Ready()
    {
        base._Ready();
        foreach (Node3D volume in inputVolumes)
        {
            volume.Connect(enteredName, Callable.From<long>(InputVolumeEntered));
            volume.Connect(exitedName, Callable.From<long>(InputVolumeExited));
        }
        // An item despawned while inside (picked up, eaten elsewhere) might never send an exit.
        ItemWorld.ItemRemoved += OnItemRemoved;
    }

    public override void _ExitTree()
    {
        base._ExitTree();
        ItemWorld.ItemRemoved -= OnItemRemoved;
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

    // ---- inputs ----

    void InputVolumeEntered(long id)
    {
        if (!runsMachineLogic) return;
        ItemRecord item = ItemWorld.Get((ulong)id);
        if (item == null || inputItems.Contains(item)) return;
        inputItems.Add(item);
        OnInputEntered(item);
    }

    void InputVolumeExited(long id)
    {
        if (!runsMachineLogic) return;
        ItemRecord item = ItemWorld.Get((ulong)id);
        if (item == null) return;
        inputItems.Remove(item);
        OnInputExited(item);
    }

    void OnItemRemoved(ItemRecord item, DespawnReason reason)
    {
        inputItems.Remove(item);
    }

    // Starts over from what's in the volumes now, as items already inside never send an enter.
    void CheckInputs()
    {
        inputItems.Clear();
        if (!runsMachineLogic) return;

        foreach (Node3D volume in inputVolumes)
        {
            foreach (long id in volume.Call(getOverlappingName).AsInt64Array())
            {
                ItemRecord item = ItemWorld.Get((ulong)id);
                if (item != null && !inputItems.Contains(item))
                {
                    inputItems.Add(item);
                }
            }
        }
    }

    /// <summary>Authority only: takes <paramref name="item"/> out of the inputs and despawns it.</summary>
    protected void ConsumeInputItem(ItemRecord item)
    {
        inputItems.Remove(item);
        ItemWorld.Despawn(item.id, DespawnReason.Consumed);
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