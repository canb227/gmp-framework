using Godot;
using PolyType;
using System.Collections.Generic;
using System.Linq;

/// <summary>Spawn-time state of a placed structure: where it sits on the build grid.</summary>
[GenerateShape]
public partial record struct StructureState
{
    public Vector3I anchor;
    /// <summary>Quarter turns of positive yaw (counter-clockwise seen from above), 0-3; see <see cref="BuildGrid.RotateOffset"/>.</summary>
    public int quarterTurns;
}


public partial class Structure : GMPOBox3DBody, Interactable
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
    /// <summary>Tags of the structure itself; its first material tag is how it sounds when struck (<see cref="ImpactSounds"/>).</summary>
    [Export]
    public Godot.Collections.Array<ItemTags> tags;

    [Export]
    public bool interactable = true;

    public string displayName => ItemInfo.Fetch(blueprintItemID)?.displayName ?? Name;

    /// <summary>Height of a conveyor belt's top above the floor of its cell.</summary>
    public const float BeltTopHeight = 0.15f;

    public Vector3I anchor { get; private set; }
    public int quarterTurns { get; private set; }
    bool hasPlacement;

    [Export]
    public Godot.Collections.Array<Vector3I> occupiedCells;

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

    public void onInteract(ulong playerID)
    {
        if (!interactable)
        {
            Logging.Log("this structure has interact disabled.", "Structure");
            return;
        }
        FactoryPlayer player = (FactoryPlayer)GameWorld.syncedObjs[playerID];
        bool invCheck = player.inventory.HasRoomFor(blueprintItemID);
    //    Logging.Log($"invcheck: {invCheck}", "Structure");
        if (invCheck)
        {
           // Logging.Log("requesting deconstruct", "Structure");
            // Arbitrated by the item's authority so two players can't both pick it up.
            BuildGrid.RequestDeconstruct(player,this);
            return;
        }
        Logging.Log("interaction failed i dunno", "Structure");
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

    /// <summary>
    /// The grid pose nearest to <paramref name="global"/> for a structure with <paramref name="placementOffset"/>:
    /// yaw rounded to a quarter turn, anchor the cell holding the offset-adjusted origin. Static so the editor
    /// snap plugin can use it on nodes whose Structure script isn't instanced in the editor.
    /// </summary>
    public static Transform3D NearestGridTransform(Transform3D global, Vector3 placementOffset, out Vector3I anchor, out int quarterTurns)
    {
        quarterTurns = Mathf.PosMod(Mathf.RoundToInt(global.Basis.GetEuler().Y / (Mathf.Pi / 2)), 4);
        Basis basis = BuildGrid.QuarterTurnBasis(quarterTurns);
        anchor = BuildGrid.WorldToCell(global.Origin - basis * placementOffset);
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
            Transform3D snapped = NearestGridTransform(GlobalTransform, placementOffset, out Vector3I a, out int q);
            anchor = a;
            quarterTurns = q;
            if (!isGridAligned)
            {
                Logging.Warn($"{GetPath()} is placed off the build grid at {GlobalPosition}, yaw {Mathf.RadToDeg(GlobalRotation.Y):0.#}°. "
                    + $"Move it to global position {snapped.Origin}, yaw {q * 90}° (cell {anchor}), or use Project > Tools > Snap Structures To Build Grid", "Structure");
            }
        }
        if (!BuildGrid.Occupy(this, out List<Vector3I> placedIn))
        {
            Logging.Warn($"{Name} overlaps an occupied cell at {anchor}; it is not registered in the build grid", "Structure");
        }
        else
        {
            this.occupiedCells = new Godot.Collections.Array<Vector3I>(placedIn);
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
