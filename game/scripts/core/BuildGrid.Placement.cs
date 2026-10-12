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
/// </summary>
public partial class BuildGrid
{
    record PendingPlacement(PlacementProbe probe, string blueprintItemID, bool alternate, ulong requester, ulong playerObjectId);
    static readonly List<PendingPlacement> pendingPlacements = new(); // host only

    /// <summary>Drops the host's undecided place requests. Called from <see cref="GameWorld.ResetSession"/>.</summary>
    public static void ResetSession()
    {
        foreach (PendingPlacement p in pendingPlacements)
        {
            p.probe.Free();
        }
        pendingPlacements.Clear();
       // ResetBeltLines();
    }

    /// <summary>True if the grid cells are free and the probe overlaps none of the level's static colliders.</summary>
    public static bool IsPlacementAllowed(PlacementProbe probe, out List<Node> levelOverlaps)
    {
        levelOverlaps = probe.LevelOverlaps();
        return CanPlace(probe.anchor, probe.structure.cells.Keys, probe.quarterTurns)
            && levelOverlaps.Count == 0;
    }

    /// <summary>
    /// Consumes one <paramref name="blueprint"/> from <paramref name="player"/>'s active hotbar slot and asks
    /// the host to build its structure at <paramref name="anchor"/>, in its <see cref="BlueprintItem.alternateStructureScene"/>
    /// form if <paramref name="alternate"/>. The blueprint is refunded if the host refuses.
    /// </summary>
    public static void RequestPlace(FactoryPlayer player, BlueprintItem blueprint, Vector3I anchor, int quarterTurns, bool alternate = false)
    {
        InventorySlot slot = player.inventory.GetSlot(player.inventory.ActiveHotbarSlot);
        if (slot.IsEmpty || slot.itemID != blueprint.itemID)
        {
            return;
        }
        player.inventory.RemoveFromSlot(player.inventory.ActiveHotbarSlot, 1);
        player.UpdateEquippedItem();
        RPCManager.RPCTo(Lobby.hostID, instance, nameof(_RequestPlace), [blueprint.itemID, anchor, quarterTurns, alternate, player.id]);
    }

    /// <summary>Asks the host to remove <paramref name="structure"/> and give its blueprint to <paramref name="player"/>.</summary>
    public static void RequestDeconstruct(FactoryPlayer player, Structure structure)
    {
        RPCManager.RPCTo(Lobby.hostID, instance, nameof(_RequestDeconstruct), [structure.id, player.id]);
    }

    // Runs on the host: quick grid check now, collision check once the probe has settled (_PhysicsProcess).
    [RPC]
    private void _RequestPlace(string blueprintItemID, Vector3I anchor, int quarterTurns, bool alternate, ulong playerObjectId)
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
        if (probe == null || !CanPlace(anchor, probe.structure.cells.Keys, quarterTurns))
        {
            probe?.Free();
            Refund(blueprintItemID, RPCManager.sender, playerObjectId);
            return;
        }
        probe.MoveTo(anchor, Mathf.PosMod(quarterTurns, 4));
        pendingPlacements.Add(new PendingPlacement(probe, blueprintItemID, alternate, RPCManager.sender, playerObjectId));
    }

    public override void _PhysicsProcess(double delta)
    {
        UpdateHighlight();
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
        if (!IsPlacementAllowed(probe, out List<Node> levelOverlaps))
        {
            foreach (Node hit in levelOverlaps)
            {
                Logging.Log($"Refused {p.blueprintItemID} at {probe.anchor}: overlaps {hit.GetPath()}", "BuildGrid");
            }
            Refund(p.blueprintItemID, p.requester, p.playerObjectId);
            return;
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
        if (!GameWorld.syncedObjs.TryGetValue(structureId, out GMPObject gmpo) || gmpo is not Structure structure)
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
