using System.Collections.Generic;
using Godot;
using PolyType;

[GenerateShape]
public partial record struct RecyclerSave
{
    public int points;
}

/// <summary>
/// An <see cref="ItemSpawner"/> that also eats items from its valley (oldest first) for points, and each cycle spends
/// a point on a bonus item at its <see cref="bonusOutput"/>. The bonus waits, like the free items, while a limit is reached.
/// </summary>
public partial class Recycler : ItemSpawner
{
    /// <summary>Points each eaten item is worth, by item id; anything not listed is worth 1.</summary>
    [Export] public Godot.Collections.Dictionary<string, int> itemPoints = new();

    /// <summary>The output (index into <see cref="IOMachine.outputLocations"/>) the bonus items come out of.</summary>
    [Export] public int bonusOutput = 1;

    int points;
    /// <summary>A bonus item that's been paid for but not spawned yet (a limit was reached when its cycle finished).</summary>
    bool bonusOwed;

    public override void AfterInit()
    {
        AudioManager.Instance.PlayOneShot("event:/Machines/Crusher/Constant_Hum", 1.0f, OutputPosition(0), new Dictionary<string, float> {});
        animationPlayer.Play("process");
        base.AfterInit();
    }

    public override void _PhysicsProcess(double delta)
    {
        if (runsMachineLogic && bonusOwed)
        {
            if (TrySpawn(PickWeighted(itemWeights), bonusOutput))
            {
                bonusOwed = false;
            }
        }
        base._PhysicsProcess(delta);
    }

    protected override bool TryStartProcess()
    {
        // Stops eating while a bonus is still owed, so the valley backs up too.
        if (bonusOwed || inputItems.Count == 0) return false;
        ItemRecord ready = inputItems[0];
        int p = 1;
        if (itemPoints.TryGetValue(ready.itemID ?? "", out int listed))
        {
            p = listed;
        }
        if (points < int.MaxValue - 10000)
        {
            points += p;
        }
        ConsumeInputItem(ready);

        if (points <= 0) return false;
        points--;
        processTimeRemaining = 1f;
        return true;
    }

    protected override void FinishProcess()
    {
        if (!TrySpawn(PickWeighted(itemWeights), bonusOutput))
        {
            bonusOwed = true;
        }
    }

    protected override byte[] SaveSpawnerState()
    {
        // An owed bonus is saved as the point it cost.
        int saved = points;
        if (bonusOwed)
        {
            saved++;
        }
        return GMPObject.serializer.Serialize(new RecyclerSave { points = saved });
    }

    protected override void LoadSpawnerState(byte[] state)
    {
        points = GMPObject.serializer.Deserialize<RecyclerSave>(state).points;
    }
}
