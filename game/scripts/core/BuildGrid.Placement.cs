using Godot;
using System.Collections.Generic;

/// <summary>
/// BuildGrid: building and deconstructing structures. Both are decided by the lobby host so that
/// simultaneous attempts on the same cells (or the same structure) resolve to one winner everywhere:
/// the requester asks the host, the host checks and spawns/despawns for every peer, and each peer's grid
/// updates as the structure spawns or leaves the tree.
/// <para>
/// A placement must pass two checks (<see cref="IsPlacementAllowed"/>): its cells are free in the grid,
/// and its colliders don't overlap the level's static colliders, found with a <see cref="PlacementProbe"/>.
/// Other structures' colliders don't matter, only their cells. The probe needs a physics step or two to report, so the host holds each
/// place request until its probe settles, then re-checks the grid at decision time: of several requests
/// for the same cells, the first decided wins and the rest are refunded.
/// </para>
/// <para>
/// A straight or turn belt can be built over another of the same blueprint (<see cref="ReplaceableBelt"/>), replacing
/// it, e.g. to turn it round. A replacement costs no blueprint, since the old belt's would come straight back.
/// </para>
/// </summary>
public partial class BuildGrid
{
    record PendingPlacement(PlacementProbe probe, string blueprintItemID, bool alternate, bool replace, ulong requester, ulong playerObjectId);
    static readonly List<PendingPlacement> pendingPlacements = new(); // host only

    /// <summary>Drops the host's undecided place requests. Called from <see cref="GameWorld.ResetSession"/>.</summary>
    public static void ResetSession()
    {
        foreach (PendingPlacement p in pendingPlacements)
        {
            p.probe.Free();
        }
        pendingPlacements.Clear();
        ResetFill();
       // ResetBeltLines();
    }

    /// <summary>
    /// True if the probe overlaps none of the level's static colliders and its cells can take it: for a replacement
    /// (<paramref name="replace"/>), its anchor cell holds a belt it replaces (<see cref="ReplaceableBelt"/>); otherwise
    /// the footprint is clear (<see cref="FootprintClear"/>: free grid cells, none blocked by level geometry).
    /// </summary>
    public static bool IsPlacementAllowed(PlacementProbe probe, bool replace, out List<Node> levelOverlaps)
    {
        levelOverlaps = probe.LevelOverlaps();
        if (levelOverlaps.Count > 0)
        {
            return false;
        }
        if (replace)
        {
            return ReplaceableBelt(probe.structure.blueprintItemID, FootprintCells(probe.anchor, probe.structure.cells.Keys, probe.quarterTurns)) != null;
        }
        return FootprintClear(probe.anchor, probe.structure.cells.Keys, probe.quarterTurns);
    }

    /// <summary>
    /// The belt that a belt of <paramref name="blueprintItemID"/> built over <paramref name="footprint"/> replaces rather
    /// than being blocked by: one straight or turn belt (a <see cref="ConveyorBlueprint"/>'s) of the same blueprint
    /// filling every cell of it, so exactly those cells. Null if there is none.
    /// </summary>
    public static Conveyor ReplaceableBelt(string blueprintItemID, IEnumerable<Vector3I> footprint)
    {
        if (ItemInfo.Fetch(blueprintItemID) is not ConveyorBlueprint)
        {
            return null;
        }
        Conveyor belt = null;
        foreach (Vector3I cell in footprint)
        {
            if (GetStructureAt(cell) is not Conveyor c || c.blueprintItemID != blueprintItemID || (belt != null && c != belt))
            {
                return null;
            }
            belt = c;
        }
        return belt;
    }

    /// <summary>
    /// Asks the host to build <paramref name="blueprint"/>'s structure at each of <paramref name="placements"/>' anchors in
    /// order, all turned <paramref name="quarterTurns"/>, in its <see cref="BlueprintItem.alternateStructureScene"/> form if
    /// <paramref name="alternate"/>. Each one that isn't a replacement takes a blueprint from <paramref name="player"/>'s
    /// active hotbar slot, refunded if the host refuses it; nothing is sent unless the slot holds enough.
    /// </summary>
    public static void RequestPlace(FactoryPlayer player, BlueprintItem blueprint, List<(Vector3I anchor, bool replace)> placements, int quarterTurns, bool alternate = false)
    {
        int needed = 0;
        foreach (var placement in placements)
        {
            if (!placement.replace)
            {
                needed++;
            }
        }
        if (needed > 0)
        {
            InventorySlot slot = player.inventory.GetSlot(player.inventory.ActiveHotbarSlot);
            if (slot.IsEmpty || slot.itemID != blueprint.itemID || slot.Count < needed)
            {
                return;
            }
            player.inventory.RemoveFromSlot(player.inventory.ActiveHotbarSlot, needed);
            player.UpdateEquippedItem();
        }
        foreach (var placement in placements)
        {
            RPCManager.RPCTo(Lobby.hostID, instance, nameof(_RequestPlace), [blueprint.itemID, placement.anchor, quarterTurns, alternate, placement.replace, player.id]);
        }
    }

    /// <summary>Asks the host to remove <paramref name="structure"/> and give its blueprint to <paramref name="player"/>.</summary>
    public static void RequestDeconstruct(FactoryPlayer player, Structure structure)
    {
        RPCManager.RPCTo(Lobby.hostID, instance, nameof(_RequestDeconstruct), [structure.id, player.id]);
    }

    // Runs on the host: quick grid check now, collision check once the probe has settled (_PhysicsProcess).
    [RPC]
    private void _RequestPlace(string blueprintItemID, Vector3I anchor, int quarterTurns, bool alternate, bool replace, ulong playerObjectId)
    {
        if (!RequesterControls(playerObjectId))
        {
            return;
        }
        if (ItemInfo.Fetch(blueprintItemID) is not BlueprintItem blueprint || blueprint.structureScene == null)
        {
            Logging.Warn($"Place request for {blueprintItemID}, which is not a blueprint with a structure scene", "BuildGrid");
            return;
        }
        PlacementProbe probe = PlacementProbe.Create(blueprint.StructureScene(alternate), false);
        bool cellsFree = false;
        if (probe != null)
        {
            if (replace)
            {
                cellsFree = ReplaceableBelt(blueprintItemID, FootprintCells(anchor, probe.structure.cells.Keys, quarterTurns)) != null;
            }
            else
            {
                cellsFree = CanPlace(anchor, probe.structure.cells.Keys, quarterTurns);
            }
        }
        if (!cellsFree)
        {
            probe?.Free();
            if (!replace)
            {
                Refund(blueprintItemID, RPCManager.sender, playerObjectId);
            }
            return;
        }
        probe.MoveTo(anchor, Mathf.PosMod(quarterTurns, 4));
        pendingPlacements.Add(new PendingPlacement(probe, blueprintItemID, alternate, replace, RPCManager.sender, playerObjectId));
    }

    public override void _PhysicsProcess(double delta)
    {
        UpdateHighlight();
        UpdateFill();
        //UpdateBeltLines();
        // Decide settled requests in arrival order.
        for (int i = 0; i < pendingPlacements.Count; i++)
        {
            PendingPlacement p = pendingPlacements[i];
            if (!p.probe.settled)
            {
                continue;
            }
            pendingPlacements.RemoveAt(i--);
            DecidePlacement(p);
            p.probe.Free();
        }
    }

    private static void DecidePlacement(PendingPlacement p)
    {
        PlacementProbe probe = p.probe;
        if (!IsPlacementAllowed(probe, p.replace, out List<Node> levelOverlaps))
        {
            foreach (Node hit in levelOverlaps)
            {
                Logging.Log($"Refused {p.blueprintItemID} at {probe.anchor}: overlaps {hit.GetPath()}", "BuildGrid");
            }
            if (levelOverlaps.Count == 0)
            {
                Logging.Log($"Refused {p.blueprintItemID} at {probe.anchor}: its cells are taken or blocked", "BuildGrid");
            }
            if (!p.replace)
            {
                Refund(p.blueprintItemID, p.requester, p.playerObjectId);
            }
            return;
        }

        if (p.replace)
        {
            // Despawning frees the old belt's cells at once (Structure.OnDespawned) for the new one. The items riding
            // it stay put: lifting them like below would throw them off whenever a belt is turned.
            GameWorld.DespawnObject(ReplaceableBelt(p.blueprintItemID, FootprintCells(probe.anchor, probe.structure.cells.Keys, probe.quarterTurns)).id);
        }
        else
        {
            // Loose items inside the new structure would be stuck in it. The host simulates them all, so it lifts each one
            // by the structure's height (plus a little), which puts them on top in the same arrangement, to fall back down.
            int height = 0;
            foreach (Vector3I cell in probe.structure.cells.Keys)
            {
                height = Mathf.Max(height, cell.Y + 1);
            }
            foreach (ulong item in probe.ItemOverlaps())
            {
                // Teleporting wakes it, so it doesn't hang in the air.
                Transform3D transform = PhysicsWorld.BodyTransform(item);
                transform.Origin += Vector3.Up * (height * CellSize + 0.5f);
                PhysicsWorld.TeleportBody(item, transform);
            }
        }

        StructureState state = new() { anchor = probe.anchor, quarterTurns = probe.quarterTurns };
        GMPOInitData init = new() { owner = p.requester };
        Transform3D pose = Structure.PlacementTransform(probe.anchor, probe.quarterTurns);
        BlueprintItem blueprint = (BlueprintItem)ItemInfo.Fetch(p.blueprintItemID);
        PackedScene scene = blueprint.StructureScene(p.alternate);
        // A belt is built straight in the shape what feeds it calls for, rather than straight and then swapped.
        if (blueprint is ConveyorBlueprint conveyorBlueprint)
        {
            scene = conveyorBlueprint.SceneFor(Conveyor.ShapeAt(conveyorBlueprint, probe.anchor, probe.quarterTurns));
        }
        GameWorld.SpawnScene(scene, pose.Origin, pose.Basis.GetEuler(), init, GMPObject.serializer.Serialize(state));
    }

    // Runs on the host. A structure already despawned by an earlier request is simply not found.
    [RPC]
    private void _RequestDeconstruct(ulong structureId, ulong playerObjectId)
    {
        if (!RequesterControls(playerObjectId))
        {
            return;
        }
        if (!GameWorld.syncedObjs.TryGetValue(structureId, out GMPObject gmpo) || gmpo is not Structure structure || !structure.canBeDeconstructed)
        {
            return;
        }
        string refund = structure.blueprintItemID;
        GameWorld.DespawnObject(structureId);
        if (refund != null)
        {
            Refund(refund, RPCManager.sender, playerObjectId);
        }
    }

    private static void Refund(string itemID, ulong peer, ulong playerObjectId)
    {
        RPCManager.RPCTo(peer, instance, nameof(_GiveItem), [itemID, playerObjectId]);
    }

    // Runs on the requesting peer: a refused blueprint or a deconstructed structure's blueprint.
    [RPC(requireAuthority = true)]
    private void _GiveItem(string itemID, ulong playerObjectId)
    {
        if (GameWorld.syncedObjs.TryGetValue(playerObjectId, out GMPObject p) && p is FactoryPlayer player && player.isLocal)
        {
            player.ReceiveItem(itemID);
        }
    }

    // The sender of the current request must control the player it names.
    private static bool RequesterControls(ulong playerObjectId)
    {
        return GameWorld.syncedObjs.TryGetValue(playerObjectId, out GMPObject p) && p is FactoryPlayer && p.authority == RPCManager.sender;
    }
}
