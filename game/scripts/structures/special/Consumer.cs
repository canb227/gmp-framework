using Godot;
using Godot.Collections;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;



public partial class Consumer : Structure, IOMachine
{

    [Export] Node3D inputVolume;

    private bool _isOperating = false;

    public float processTime = 1.0f;
    public float processTimeRemaining = 0.0f;

    private float speedMultiplier = 1.0f;

    public override void _PhysicsProcess(double delta)
    {
        if (isOperating())
        {
            //DoLocalOperationEffects();
        }
        if (!runsMachineLogic) return;

        if (processTimeRemaining > 0)
        {
            processTimeRemaining -= (float)delta;
            if (processTimeRemaining <= 0)
            {
                _isOperating = false;
                //DoAuthFinishProcessingEffects();
            }
        }
        else
        {
            //DoAuthStartProcessingEffects();
            PhysicalFactoryItem item = FirstItemInInput();
            if (item != null)
            {
                processTimeRemaining = processTime;
                _isOperating = true;

                // Read what's needed before despawning, which may free the item.
                string itemID = item.itemID;
                var value = new System.Collections.Generic.Dictionary<Currency, int>(item.currencyValue);
                GameWorld.DespawnObject(item.id, DespawnReason.Consumed);
                ProgressManager.RequestDeposit(itemID, value);
            }
        }
    }

    public bool isOperating()
    {
        return _isOperating;
    }



    /// <summary>
    /// The first synced item in the input volume (a sensor body), or null. Players, structures and anything else
    /// in there are skipped, as are items already despawning.
    /// </summary>
    PhysicalFactoryItem FirstItemInInput()
    {
        // Runs every tick: dispose the Godot wrappers here rather than leave them to the garbage collector.
        using Variant result = inputVolume.Call(Box3DNames.getOverlappingBodies);
        using Godot.Collections.Array bodies = result.AsGodotArray();
        for (int i = 0; i < bodies.Count; i++)
        {
            if (bodies[i].AsGodotObject() is PhysicalFactoryItem item && GameWorld.syncedObjs.ContainsKey(item.id))
            {
                return item;
            }
        }
        return null;
    }

    public float getSpeedMultiplier()
    {
        return speedMultiplier;
    }

    public void setSpeedMultiplier(float multiplier)
    {
        speedMultiplier = multiplier;
    }
}

