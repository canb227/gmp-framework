using Godot;
using PolyType;
using System.Collections.Generic;

/// <summary>Spawn-time state of a placed structure: where it sits on the build grid.</summary>
[GenerateShape]
public partial record struct StructureState
{
    public Vector3I anchor;
    /// <summary>Quarter turns of positive yaw (counter-clockwise seen from above), 0-3; see <see cref="BuildGrid.RotateOffset"/>.</summary>
    public int quarterTurns;
}

/// <summary>
/// Root script of every placeable building.
/// <para>
/// <b>Scene requirement:</b> the root node of a structure scene is always a <c>Box3DBody</c> node (static)
/// with this script attached. Box3D isn't exposed to C#, so this class derives from
/// <see cref="GMPOBox3DBody"/> (a Node3D to C#) and reaches the body through <c>Call</c>/<c>Get</c>/<c>Set</c>.
/// Give the root several colliders with <c>Box3DCollisionShape</c> children (each can carry its own
/// friction and tangent velocity; a body with shape children uses only those). Extra child <c>Box3DBody</c>
/// nodes are allowed too, e.g. walls beside a mesh-shaped root, which can't also have shape children.
/// Placement tests all of these against the world (see <see cref="PlacementProbe"/>); child bodies authored
/// as sensors are machine triggers, which placement ignores; put them on
/// <see cref="GameWorld.QueryHiddenLayer"/> so raycasts pass through them.
/// </para>
/// <para>
/// Conventions: "front" is local -Z and conveyors carry items toward it; belts sit low, their top 0.15 m
/// above the cell floor (<see cref="BeltTopHeight"/>), so slopes connect to flat belts. A slope's high end
/// finishes a few centimetres above the next belt so items drop onto it (a box tilted over the crest would
/// otherwise catch the next belt's edge).
/// </para>
/// <para>
/// Conveyor turns: one tangent velocity can't bend a path, so a turn's root body is a flat triangle-mesh
/// shape (<c>mesh_vertices</c>/<c>mesh_indices</c>) split into a grid of quads, each with its own entry in
/// <c>surface_materials</c> (chosen per triangle by <c>mesh_materials</c>) whose tangent velocity points
/// along the arc about the turn's inner corner. Friction steers items round as they cross the quads, and
/// the outer walls (child bodies, since a mesh-shaped body can't also have shape children) catch any drift.
/// This data is baked into the turn scenes.
/// </para>
/// <para>
/// The root is placed at the centre of the anchor cell plus <see cref="placementOffset"/> (rotated with
/// the structure), so a collider that sits low in its cell (a conveyor) or spans several cells can still be
/// the root body. A structure never moves, so it sends no state updates; its grid position arrives once in
/// the spawn <see cref="StructureState"/>. Every peer registers the occupied cells in <see cref="BuildGrid"/>
/// on spawn and frees them when the node leaves the tree. Place and deconstruct through
/// <see cref="BuildGrid.RequestPlace"/> / <see cref="BuildGrid.RequestDeconstruct"/>.
/// </para>
/// </summary>
public partial class Structure : GMPOBox3DBody
{
    /// <summary>Cells this structure occupies, relative to its anchor cell before rotation. Always includes (0,0,0).</summary>
    [Export]
    public Godot.Collections.Array<Vector3I> cellOffsets = [Vector3I.Zero];

    /// <summary>Where the root sits relative to the anchor cell's centre, in the structure's own (unrotated) frame.</summary>
    [Export]
    public Vector3 placementOffset;

    /// <summary>The blueprint given back when this structure is deconstructed.</summary>
    [Export]
    public string blueprintItemID;
    /// <summary>Direction arrow drawn above this structure's placement preview (not the built structure); see <see cref="FlowArrowMesh"/>.</summary>
    [Export]
    public FlowArrow flowArrow = FlowArrow.None;

    public string displayName => FactoryItem.Fetch(blueprintItemID)?.displayName ?? Name;

    /// <summary>Height of a conveyor belt's top above the floor of its cell.</summary>
    public const float BeltTopHeight = 0.15f;

    public Vector3I anchor { get; private set; }
    public int quarterTurns { get; private set; }
    bool hasPlacement;

    /// <summary>
    /// True on the peer that runs this structure's machine logic: it has been spawned or registered as part
    /// of a level (placement previews and probes never are) and this peer is its authority.
    /// </summary>
    protected bool runsMachineLogic => id != 0 && authority == Lobby.selfPeerID;

    /// <summary>The synced items a trigger sensor currently overlaps.</summary>
    protected static IEnumerable<PhysicalFactoryItem> ItemsInTrigger(Node3D trigger)
    {
        foreach (Node n in trigger.Call("get_overlapping_bodies").AsGodotArray<Node>())
        {
            if (n is PhysicalFactoryItem item && GameWorld.syncedObjs.ContainsKey(item.id))
            {
                yield return item;
            }
        }
    }

    public Structure()
    {
        priority = -1; // never sends state updates
    }

    /// <summary>The root's world transform when anchored at <paramref name="anchor"/> with <paramref name="quarterTurns"/>.</summary>
    public Transform3D PlacementTransform(Vector3I anchor, int quarterTurns)
    {
        Basis basis = BuildGrid.QuarterTurnBasis(quarterTurns);
        return new Transform3D(basis, BuildGrid.CellToWorld(anchor) + basis * placementOffset);
    }

    /// <summary>Whether the root stands exactly where <see cref="PlacementTransform"/> puts it for its anchor and turns.</summary>
    public bool isGridAligned
    {
        get
        {
            Transform3D expected = PlacementTransform(anchor, quarterTurns);
            return GlobalPosition.DistanceTo(expected.Origin) < 0.01f && GlobalBasis.IsEqualApprox(expected.Basis);
        }
    }

    public override byte[] GenerateStateUpdate() => null;

    public override void ApplyStateUpdate(byte[] update)
    {
        if (update == null || update.Length == 0) return;
        StructureState s = GMPObject.serializer.Deserialize<StructureState>(update);
        anchor = s.anchor;
        quarterTurns = s.quarterTurns;
        hasPlacement = true;
    }

    // Deliberately doesn't call base: a structure stays static on every peer instead of following as Kinematic.
    public override void AfterInit()
    {
        if (!hasPlacement)
        {
            // Placed by hand in a level rather than built: derive the cell from where it stands.
            quarterTurns = Mathf.PosMod(Mathf.RoundToInt(GlobalRotation.Y / (Mathf.Pi / 2)), 4);
            anchor = BuildGrid.WorldToCell(GlobalPosition - BuildGrid.QuarterTurnBasis(quarterTurns) * placementOffset);
            if (!isGridAligned)
            {
                Logging.Warn($"{Name} is placed off the build grid (nearest: cell {anchor}, {quarterTurns} quarter turns); snap it in the editor", "Structure");
            }
        }
        if (!BuildGrid.Occupy(this))
        {
            Logging.Warn($"{Name} overlaps an occupied cell at {anchor}; it is not registered in the build grid", "Structure");
        }
    }

    public override void OnAuthorityChanged()
    {
    }

    public override void _ExitTree()
    {
        BuildGrid.Vacate(this);
    }

    // Static: nothing to follow.
    public override void _PhysicsProcess(double delta)
    {
    }
}
