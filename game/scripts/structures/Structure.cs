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

    /// <summary>
    /// Where items go in and come out, besides the ends of its belts, which are ports already (see
    /// <see cref="StructurePorts"/>). Snapping and the preview's input/output arrows go by these.
    /// </summary>
    [Export]
    public Godot.Collections.Array<StructurePort> ports = [];
    /// <summary>Extra arrows for the placement preview, for flows the ports don't show. Display only.</summary>
    [Export]
    public Godot.Collections.Array<StructureArrow> arrows = [];
    /// <summary>Tags of the structure itself; its first material tag is how it sounds when struck (<see cref="ImpactSounds"/>).</summary>
    [Export]
    public Godot.Collections.Array<ItemTags> tags;

    [Export]
    public bool interactable = true;

    /// <summary>Height of a conveyor belt's top above the floor of its cell.</summary>
    public const float BeltTopHeight = 0.15f;

    /// <summary>Every port (declared and from the belts) in the root's frame; worked out on first use.</summary>
    public IReadOnlyList<PortShape> localPorts => portCache ??= StructurePorts.Collect(this);
    private List<PortShape> portCache;
    /// <summary>
    /// Every port any of its forms could have (a self-turning conveyor's three inputs), each once: what it connects
    /// by when built, and snaps by when placed. The same as <see cref="localPorts"/> for most structures.
    /// </summary>
    public IReadOnlyList<PortShape> allLocalPorts => allPortCache ??= StructurePorts.Collect(this, true);
    private List<PortShape> allPortCache;
    /// <summary>Drops the cached <see cref="localPorts"/>, after the structure's belts change.</summary>
    protected void RefreshPorts() => portCache = null;
    // Scenes whose ports have been checked, so each warns once however many are built.
    private static readonly HashSet<string> validatedScenes = new();

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
        // Machines poll this every tick: dispose the Godot wrappers here rather than leave them to the garbage
        // collector's finalizer queue, and index rather than foreach, which allocates an enumerator.
        using Variant result = trigger.Call(Box3DNames.getOverlappingBodies);
        Godot.Collections.Array<Node> bodies = result.AsGodotArray<Node>();
        using Godot.Collections.Array untyped = (Godot.Collections.Array)bodies; // the typed array isn't IDisposable
        for (int i = 0; i < bodies.Count; i++)
        {
            if (bodies[i] is PhysicalFactoryItem item && GameWorld.syncedObjs.ContainsKey(item.id))
            {
                yield return item;
            }
        }
    }

    /// <summary>
    /// The synced items in the volumes of its input ports (<see cref="StructurePort.volume"/>), each volume read once
    /// however many ports share it: what a machine takes in.
    /// </summary>
    protected IEnumerable<PhysicalFactoryItem> ItemsAtInputs()
    {
        FindPortNodes();
        for (int v = 0; v < inputVolumes.Count; v++)
        {
            foreach (PhysicalFactoryItem item in ItemsInTrigger(inputVolumes[v]))
            {
                yield return item;
            }
        }
    }

    /// <summary>
    /// Where its <paramref name="output"/>th output port that has a <see cref="StructurePort.spawn"/> puts an item, in
    /// world space: the spawn node's position, or a random point in one of its boxes if it's a volume. The structure's
    /// own position if it has no such port.
    /// </summary>
    protected Vector3 OutputPosition(int output = 0)
    {
        FindPortNodes();
        if (output >= outputSpawns.Count)
        {
            return GlobalPosition;
        }
        var (node, boxes, totalWeight) = outputSpawns[output];
        if (boxes.Length == 0)
        {
            return node.GlobalPosition;
        }
        // A box picked by volume, then a point in it.
        float pick = Random.Shared.NextSingle() * totalWeight;
        var box = boxes[^1];
        foreach (var b in boxes)
        {
            if ((pick -= b.weight) <= 0) { box = b; break; }
        }
        Vector3 local = new((Random.Shared.NextSingle() * 2 - 1) * box.half.X, (Random.Shared.NextSingle() * 2 - 1) * box.half.Y,
            (Random.Shared.NextSingle() * 2 - 1) * box.half.Z);
        return node.GlobalTransform * (box.xf * local);
    }

    private List<Node3D> inputVolumes;
    private List<(Node3D node, (Transform3D xf, Vector3 half, float weight)[] boxes, float totalWeight)> outputSpawns;

    // The nodes the declared ports link to, looked up once (StructurePorts.Validate reports bad links).
    private void FindPortNodes()
    {
        if (inputVolumes != null)
        {
            return;
        }
        inputVolumes = new();
        outputSpawns = new();
        foreach (StructurePort port in ports)
        {
            if (port == null) continue;
            if (port.kind == PortKind.Input && port.volume?.IsEmpty == false && GetNodeOrNull<Node3D>(port.volume) is Node3D volume
                && !inputVolumes.Contains(volume))
            {
                inputVolumes.Add(volume);
            }
            else if (port.kind == PortKind.Output && port.spawn?.IsEmpty == false && GetNodeOrNull<Node3D>(port.spawn) is Node3D spawn)
            {
                var boxes = StructurePorts.SpawnBoxes(spawn);
                float total = 0;
                foreach (var b in boxes) total += b.weight;
                outputSpawns.Add((spawn, boxes, total));
            }
        }
    }

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
        if (string.IsNullOrEmpty(hoverName)) hoverName = ItemInfo.Fetch(blueprintItemID)?.displayName ?? Name;
        if (string.IsNullOrEmpty(hoverText)) hoverText = "Press F to deconstruct!";
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
        if (validatedScenes.Add(SceneFilePath))
        {
            foreach (string problem in StructurePorts.Validate(this))
            {
                Logging.Warn($"{SceneFilePath}: {problem}", "StructurePorts");
            }
        }
        // Ports first: they settle a self-turning conveyor's form, and so which of its belts joins.
        BuildGrid.RegisterPorts(this);
        // Its belts have no colliders of their own: they join the merged belt lines.
        BuildGrid.RegisterBelts(this);
        // Structures never follow synced state, so only machines with their own _PhysicsProcess keep processing on.
        RefreshPhysicsProcess();
    }

    public override void OnAuthorityChanged()
    {
    }

    public override void _ExitTree()
    {
        BuildGrid.Vacate(this);
        BuildGrid.UnregisterPorts(this);
        BuildGrid.UnregisterBelts(this);
    }
}
