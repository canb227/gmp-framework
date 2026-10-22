using Godot;

/// <summary>
/// A structure that has to be repaired before it works: switched by an <see cref="GMPOActivator"/> (usually a
/// <see cref="PricedButton"/>), it replaces itself with <see cref="repairedScene"/> in the same cells. Activators
/// switch it on every peer, but only its authority (the host, for a level structure) does the swap, through the
/// usual despawn and spawn broadcasts. The wrench can't take it apart, so it can't be traded for a free blueprint.
/// </summary>
public partial class BrokenStructure : Structure, Triggerable
{
    /// <summary>The working structure this becomes once repaired.</summary>
    [Export] public PackedScene repairedScene;

    // Values set in a constructor don't survive an inherited scene that swaps this script in for its base's
    // (e.g. BrokenSmelter over Smelter): Godot copies the old script's values across. So the defaults live in code here.
    public override void AfterInit()
    {
        hoverText = "Broken.";
        base.AfterInit();
    }

    // Never deconstructed: that would hand out the working structure's blueprint for free.
    public override bool canBeDeconstructed => false;

    public void OnTrigger(bool active)
    {
        if (active && runsMachineLogic)
        {
            Replace();
        }
    }

    /// <summary>Authority only: swaps this for <see cref="repairedScene"/>, same anchor and turns, for every peer.</summary>
    public void Replace()
    {
        if (repairedScene == null)
        {
            Logging.Warn($"{GetPath()} has no repairedScene to become", "BrokenStructure");
            return;
        }
        Transform3D pose = PlacementTransform(anchor, quarterTurns);
        StructureState state = new() { anchor = anchor, quarterTurns = quarterTurns };
        GameWorld.DespawnObject(id);
        GameWorld.SpawnScene(repairedScene, pose.Origin, pose.Basis.GetEuler(), new GMPOInitData(), GMPObject.serializer.Serialize(state));
    }
}
