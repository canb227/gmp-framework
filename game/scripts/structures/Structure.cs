using Godot;
using PolyType;
using System;
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

/// <summary>
/// A building on the build grid. Every structure scene is authored the same way: the root (0,0,0) is the
/// left (-X), bottom (-Y), back (+Z) corner of its anchor cell, and the structure grows toward +X, +Y and -Z
/// (its front faces -Z). A 2x2x2 structure fits in x 0..2, y 0..2, z -2..0, with cellOffsets in 0..1, 0..1, -1..0.
/// </summary>
public partial class Structure : GMPOBox3DBody, Interactable
{
    /// <summary>
    /// Cells this structure occupies, relative to its anchor cell before rotation: always (0,0,0), and otherwise
    /// x and y of 0 or more and z of 0 or less (see the class summary).
    /// </summary>
    [Export]
    public Godot.Collections.Array<Vector3I> cellOffsets = [Vector3I.Zero];

    /// <summary>The blueprint given back when this structure is deconstructed.</summary>
    [Export]
    public string blueprintItemID;

    [Export]
    public bool interactable = true;

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

    public virtual void onInteract(ulong playerID)
    {
        if (!interactable)
        {
            Logging.Log("this structure has interact disabled.", "Structure");
            return;
        }
        FactoryPlayer player = (FactoryPlayer)GameWorld.syncedObjs[playerID];
        if (!player.inventory.HasRoomFor(blueprintItemID))
        {
            Logging.Log("interaction failed i dunno", "Structure");
            return;
        }
        // Arbitrated by the item's authority so two players can't both pick it up.
        BuildGrid.RequestDeconstruct(player, this);
    }

    public Structure()
    {

    }

    /// <summary>Where the root sits relative to its anchor cell's centre, before rotation: the cell's left-bottom-back corner.</summary>
    public static readonly Vector3 RootCorner = new Vector3(-0.5f, -0.5f, 0.5f) * BuildGrid.CellSize;

    /// <summary>
    /// The root's world transform when anchored at <paramref name="anchor"/> with <paramref name="quarterTurns"/>.
    /// It turns about the anchor cell's centre, the same as <see cref="cellOffsets"/> do (<see cref="BuildGrid.RotateOffset"/>).
    /// </summary>
    public static Transform3D PlacementTransform(Vector3I anchor, int quarterTurns)
    {
        Basis basis = BuildGrid.QuarterTurnBasis(quarterTurns);
        return new Transform3D(basis, BuildGrid.CellToWorld(anchor) + basis * RootCorner);
    }

    /// <summary>The grid pose nearest to <paramref name="global"/>: yaw rounded to a quarter turn, and the anchor cell its root's corner belongs to.</summary>
    public static Transform3D NearestGridTransform(Transform3D global, out Vector3I anchor, out int quarterTurns)
    {
        quarterTurns = Mathf.PosMod(Mathf.RoundToInt(global.Basis.GetEuler().Y / (Mathf.Pi / 2)), 4);
        Basis basis = BuildGrid.QuarterTurnBasis(quarterTurns);
        anchor = BuildGrid.WorldToCell(global.Origin - basis * RootCorner);
        return PlacementTransform(anchor, quarterTurns);
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
        priority = -1; // never sends state updates
        if (string.IsNullOrEmpty(hoverName)) hoverName = ItemInfo.Fetch(blueprintItemID)?.displayName ?? Name;
        if (string.IsNullOrEmpty(hoverText)) hoverText = "Press F to deconstruct!";
        if (!hasPlacement)
        {
            // Placed by hand in a level rather than built: derive the cell from where it stands.
            Transform3D snapped = NearestGridTransform(GlobalTransform, out Vector3I a, out int q);
            anchor = a;
            quarterTurns = q;
            if (!isGridAligned)
            {
                Logging.Warn($"{GetPath()} is placed off the build grid at {GlobalPosition}, yaw {Mathf.RadToDeg(GlobalRotation.Y):0.#}°. "
                    + $"Move it to global position {snapped.Origin}, yaw {q * 90}° (cell {anchor})", "Structure");
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

        // Structures never follow synced state, so only machines with their own _PhysicsProcess keep processing on.
        RefreshPhysicsProcess();
    }

    public override void OnAuthorityChanged()
    {
    }

    public override void _ExitTree()
    {
        BuildGrid.Vacate(this);

    }
}
