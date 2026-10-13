using Godot;

public enum ConveyorShape
{
    Straight,
    TurnLeft,
    TurnRight,
}

/// <summary>
/// A belt: the straight conveyor, its two turns, the slopes and the loader. Where items go in and out is in its
/// cells' face tags, like any structure (Structure.Ends.cs).
/// <para>
/// A belt whose blueprint is a <see cref="ConveyorBlueprint"/> picks its own shape, like Factorio: its output never
/// changes, and its input bends toward whatever feeds it (<see cref="ShapeAt"/>). Whenever a structure arrives or
/// leaves, the belts its outputs deliver into re-check their shape, and a belt's authority swaps it for the right scene
/// if it's wrong. The turn scenes are authored in the straight belt's frame (same cells, output out of the front, -Z)
/// and only take items in from their left (-X) or right (+X) side instead of the back, so a swap keeps the anchor and
/// rotation. Slopes and the loader have no other shapes.
/// </para>
/// </summary>
public partial class Conveyor : Structure
{
    [Export]
    public Godot.Collections.Array<GMPOBox3DBody> beltSurfaces;

    [Export]
    public ConveyorShape shape = ConveyorShape.Straight;

    public override void AfterInit()
    {
        base.AfterInit();
        // Deferred, so a level or save that spawns many structures at once is complete before this looks at its neighbours.
        Callable.From(CheckShape).CallDeferred();
    }

    /// <summary>On this belt's authority: if what feeds it calls for a different shape, replaces it with that shape's scene.</summary>
    public void CheckShape()
    {
        // Skips belts that were despawned or cleared away (session end) since the check was queued.
        if (!GameWorld.syncedObjs.TryGetValue(id, out GMPObject registered) || registered != this)
        {
            return;
        }
        if (authority != Lobby.selfPeerID)
        {
            return;
        }
        // A restore (always while frozen) rebuilds the saved shapes; swapping now would spawn while other peers are
        // still restoring, and their restore would then wipe the new belt.
        if (GameWorld.frozen)
        {
            return;
        }
        if (ItemInfo.Fetch(blueprintItemID) is not ConveyorBlueprint blueprint)
        {
            return;
        }
        ConveyorShape wanted = ShapeAt(blueprint, anchor, quarterTurns);
        if (wanted == shape)
        {
            return;
        }
        GameWorld.DespawnObject(id);
        StructureState state = new() { anchor = anchor, quarterTurns = quarterTurns };
        Transform3D pose = PlacementTransform(anchor, quarterTurns);
        GMPOInitData init = new() { owner = owner };
        GameWorld.SpawnScene(blueprint.SceneFor(wanted), pose.Origin, pose.Basis.GetEuler(), init, GMPObject.serializer.Serialize(state));
    }

    /// <summary>
    /// The shape a belt of <paramref name="blueprint"/> built at <paramref name="anchor"/> with
    /// <paramref name="quarterTurns"/> should take: straight if something feeds the straight belt's input, else the
    /// turn whose input is fed if exactly one is, else straight.
    /// </summary>
    public static ConveyorShape ShapeAt(ConveyorBlueprint blueprint, Vector3I anchor, int quarterTurns)
    {
        if (IsFed(blueprint.structureScene, anchor, quarterTurns))
        {
            return ConveyorShape.Straight;
        }
        bool left = blueprint.turnLeftScene != null && IsFed(blueprint.turnLeftScene, anchor, quarterTurns);
        bool right = blueprint.turnRightScene != null && IsFed(blueprint.turnRightScene, anchor, quarterTurns);
        if (left && !right)
        {
            return ConveyorShape.TurnLeft;
        }
        if (right && !left)
        {
            return ConveyorShape.TurnRight;
        }
        return ConveyorShape.Straight;
    }

    // Whether a placed structure's output delivers onto one of scene's inputs, were it built here.
    static bool IsFed(PackedScene scene, Vector3I anchor, int quarterTurns)
    {
        foreach (CellEnd input in SceneInputEnds(scene))
        {
            if (FeederOf(input.Placed(anchor, quarterTurns)) != null)
            {
                return true;
            }
        }
        return false;
    }
}
