using System.Collections.Generic;
using Godot;
using PolyType;

[GenerateShape]
public partial record struct ItemSpammerSave
{
    public bool hasSpawned;
}

public partial class ItemSpammer : ItemSpawner
{
    bool hasSpawned;
    [Export] BoxShape3D eventTrigger; //STEPHEN TODO
    [Export] double delay = 15.0;

    public override void AfterInit()
    {
        base.AfterInit();
    }

    double deltaCount = 0.0;
    public override void _PhysicsProcess(double delta)
    {
        if(hasSpawned)
        {
            return;
        }

        deltaCount += delta;
        if(deltaCount > delay)
        {
            deltaCount = 0.0;
            FireSpawn();
        }
        if(spawning)
        {
            base._PhysicsProcess(delta);
            spawning = false;
            hasSpawned = true;
        }
    }

    public void FireSpawn()
    {
        spawning = true;
    }

    protected override bool TryStartProcess()
    {
        return !hasSpawned;
    }

    protected override void FinishProcess()
    {
        if (!TrySpawn(PickWeighted(itemWeights), 0))
        {
        }
    }

    protected override byte[] SaveSpawnerState()
    {
        // An owed bonus is saved as the point it cost.
        return GMPObject.serializer.Serialize(new ItemSpammerSave{hasSpawned = hasSpawned});
    }

    protected override void LoadSpawnerState(byte[] state)
    {
        var save = GMPObject.serializer.Deserialize<ItemSpammerSave>(state);
        hasSpawned = save.hasSpawned;
    }
}
