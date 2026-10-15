using Godot;
using PolyType;
using System.Collections.Generic;

[GenerateShape]
public partial record struct RecipeMachineSave
{
    /// <summary>Index in <see cref="RecipeMachine.recipes"/> of the recipe being made, or -1.</summary>
    public int recipeIndex;
    /// <summary>Item ids finished but not spawned yet.</summary>
    public List<string> outputQueue;
}

/// <summary>
/// The usual base for machines: it takes items in through its <see cref="inputVolumes"/> (tracked in
/// <see cref="inputItems"/>) and puts them out at its <see cref="outputLocations"/>. By default it makes
/// <see cref="recipes"/>: when idle it starts the first one whose inputs are all there, taking them oldest first,
/// and when the recipe's time is up its outputs spawn one at a time at the first output location. Machines that
/// work some other way override <see cref="TryStartProcess"/> and <see cref="FinishProcess"/>.
/// </summary>
public partial class RecipeMachine : Machine
{
    /// <summary>Sensor bodies items are taken in from. Assumed not to overlap.</summary>
    [Export] public Godot.Collections.Array<Node3D> inputVolumes = new();
    [Export] public Godot.Collections.Array<Node3D> outputLocations = new();
    /// <summary>What this machine can make, in order of preference.</summary>
    [Export] public Godot.Collections.Array<Recipe> recipes = new();
    /// <summary>Seconds between spawning queued outputs, so they don't land on top of each other.</summary>
    [Export] public float outputInterval = 0.25f;

    /// <summary>Every peer: the recipe being made, or last made (null before the first).</summary>
    public Recipe currentRecipe { get; private set; }

    /// <summary>Authority only: the items in the input volumes, oldest first.</summary>
    public List<PhysicalFactoryItem> inputItems = new();
    /// <summary>Authority only: item ids finished and waiting to spawn.</summary>
    protected List<string> outputQueue = new();
    float untilNextOutput;

    /// <summary>Authority only: a body entered or left an input volume (<see cref="inputItems"/> is already updated).</summary>
    protected virtual void OnInputEntered(Node3D body) { }
    protected virtual void OnInputExited(Node3D body) { }

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

            untilNextOutput -= (float)delta;
            if (outputQueue.Count > 0 && untilNextOutput <= 0)
            {
                ItemInfo.SpawnInWorld(outputQueue[0], OutputPosition(), GlobalRotation);
                outputQueue.RemoveAt(0);
                untilNextOutput = outputInterval;
            }
        }
        base._PhysicsProcess(delta);
    }

    protected override bool TryStartProcess()
    {
        foreach (Recipe recipe in recipes)
        {
            // Are all of the recipe's inputs here?
            bool hasInputs = true;
            foreach (var (item, count) in recipe.inputs)
            {
                int found = 0;
                foreach (PhysicalFactoryItem input in inputItems)
                {
                    if (input.itemID == item.itemID) found++;
                }
                if (found < count) hasInputs = false;
            }
            if (!hasInputs) continue;

            // Take them, oldest first. Consuming removes the item from the list, so only step past items we keep.
            foreach (var (item, count) in recipe.inputs)
            {
                int left = count;
                int i = 0;
                while (i < inputItems.Count && left > 0)
                {
                    if (inputItems[i].itemID == item.itemID)
                    {
                        ConsumeInputItem(inputItems[i]);
                        left--;
                    }
                    else
                    {
                        i++;
                    }
                }
            }
            processTimeRemaining = recipe.baseTime;
            RPCManager.RPC(this, nameof(SetRecipe), [recipes.IndexOf(recipe)]);
            return true;
        }
        return false;
    }

    protected override void FinishProcess()
    {
        if (currentRecipe == null) return; // a save from before recipes were saved
        foreach (var (item, count) in currentRecipe.outputs)
        {
            for (int i = 0; i < count; i++)
            {
                outputQueue.Add(item.itemID);
            }
        }
    }

    [RPC(requireAuthority = true)]
    protected void SetRecipe(int recipeIndex)
    {
        currentRecipe = null;
        if (recipeIndex >= 0) currentRecipe = recipes[recipeIndex];
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

    // ---- saving ----

    protected override byte[] SaveMachineState()
    {
        return GMPObject.serializer.Serialize(new RecipeMachineSave { recipeIndex = recipes.IndexOf(currentRecipe), outputQueue = outputQueue });
    }

    protected override void LoadMachineState(byte[] state)
    {
        RecipeMachineSave s = GMPObject.serializer.Deserialize<RecipeMachineSave>(state);
        currentRecipe = null;
        if (s.recipeIndex >= 0 && s.recipeIndex < recipes.Count) currentRecipe = recipes[s.recipeIndex];
        outputQueue = s.outputQueue ?? new();
    }
}
