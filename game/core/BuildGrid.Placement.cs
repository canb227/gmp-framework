using Godot;
using System.Collections.Generic;

/// <summary>
/// BuildGrid: building and deconstructing structures. Both are decided by the lobby host so that
/// simultaneous attempts on the same cells (or the same structure) resolve to one winner everywhere:
/// the requester asks the host, the host checks and spawns/despawns for every peer, and each peer's grid
/// updates as the structure spawns or leaves the tree.
/// <para>
/// A placement must pass two checks (<see cref="IsPlacementAllowed"/>): its cells are free in the grid,
/// and its colliders don't overlap anything in <see cref="placementBlockers"/>, found with a
/// <see cref="PlacementProbe"/>. The probe needs a physics step or two to report, so the host holds each
/// place request until its probe settles, then re-checks the grid at decision time: of several requests
/// for the same cells, the first decided wins and the rest are refunded.
/// </para>
/// </summary>
public partial class BuildGrid
{
    /// <summary>
    /// Which kinds of overlap block a placement. Only level geometry for now; add
    /// <see cref="PlacementBlockers.DynamicItems"/>, <see cref="PlacementBlockers.StaticObjects"/> or
    /// <see cref="PlacementBlockers.Players"/> to make those block too. Must match on every peer
    /// (the ghost's tint and the host's decision both use it).
    /// </summary>
    public static PlacementBlockers placementBlockers = PlacementBlockers.LevelGeometry;

    /// <summary>Place requests the host refused because of a physical overlap (used by the headless multiplayer test).</summary>
    public static int collisionRefusals;

    record PendingPlacement(PlacementProbe probe, string blueprintItemID, ulong requester, ulong playerObjectId);
    static readonly List<PendingPlacement> pendingPlacements = new(); // host only

    /// <summary>Drops the host's undecided place requests. Called from <see cref="GameWorld.ResetSession"/>.</summary>
    public static void ResetSession()
    {
        foreach (PendingPlacement p in pendingPlacements)
        {
            p.probe.Free();
        }
        pendingPlacements.Clear();
        collisionRefusals = 0;
    }

    /// <summary>True if the grid cells are free and the probe's overlaps include nothing in <see cref="placementBlockers"/>.</summary>
    public static bool IsPlacementAllowed(PlacementProbe probe, out PlacementBlockers overlaps)
    {
        overlaps = probe.Overlaps();
        return CanPlace(probe.anchor, probe.structure.cellOffsets, probe.quarterTurns)
            && (overlaps & placementBlockers) == 0;
    }

    /// <summary>
    /// Consumes one <paramref name="blueprint"/> from <paramref name="player"/>'s active hotbar slot and asks
    /// the host to build its structure at <paramref name="anchor"/>. The blueprint is refunded if the host refuses.
    /// </summary>
    public static void RequestPlace(FactoryPlayer player, BlueprintItem blueprint, Vector3I anchor, int quarterTurns)
    {
        InventorySlot slot = player.inventory.GetSlot(player.inventory.ActiveHotbarSlot);
        if (slot.IsEmpty || slot.itemID != blueprint.itemID)
        {
            return;
        }
        player.inventory.RemoveFromSlot(player.inventory.ActiveHotbarSlot, 1);
        player.UpdateEquippedItem();
        RPCManager.RPCTo(Lobby.hostID, instance, nameof(_RequestPlace), [blueprint.itemID, anchor, quarterTurns, player.id]);
    }

    /// <summary>Asks the host to remove <paramref name="structure"/> and give its blueprint to <paramref name="player"/>.</summary>
    public static void RequestDeconstruct(FactoryPlayer player, Structure structure)
    {
        RPCManager.RPCTo(Lobby.hostID, instance, nameof(_RequestDeconstruct), [structure.id, player.id]);
    }

    // Runs on the host: quick grid check now, collision check once the probe has settled (_PhysicsProcess).
    [RPC]
    private void _RequestPlace(string blueprintItemID, Vector3I anchor, int quarterTurns, ulong playerObjectId)
    {
        if (!RequesterControls(playerObjectId))
        {
            return;
        }
        if (FactoryItem.Fetch(blueprintItemID) is not BlueprintItem blueprint || blueprint.structureScene == null)
        {
            Logging.Warn($"Place request for {blueprintItemID}, which is not a blueprint with a structure scene", "BuildGrid");
            return;
        }
        PlacementProbe probe = PlacementProbe.Create(blueprint.structureScene, false);
        if (probe == null || !CanPlace(anchor, probe.structure.cellOffsets, quarterTurns))
        {
            probe?.Free();
            Refund(blueprintItemID, RPCManager.sender, playerObjectId);
            return;
        }
        probe.MoveTo(anchor, Mathf.PosMod(quarterTurns, 4));
        pendingPlacements.Add(new PendingPlacement(probe, blueprintItemID, RPCManager.sender, playerObjectId));
    }

    public override void _PhysicsProcess(double delta)
    {
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
        if (!IsPlacementAllowed(probe, out PlacementBlockers overlaps))
        {
            if ((overlaps & placementBlockers) != 0)
            {
                collisionRefusals++;
                Logging.Log($"Refused {p.blueprintItemID} at {probe.anchor}: overlaps {probe.DescribeOverlaps(placementBlockers)}", "BuildGrid");
            }
            Refund(p.blueprintItemID, p.requester, p.playerObjectId);
            return;
        }

        StructureState state = new() { anchor = probe.anchor, quarterTurns = probe.quarterTurns };
        GMPOInitData init = new() { owner = p.requester };
        Transform3D pose = probe.structure.PlacementTransform(probe.anchor, probe.quarterTurns);
        BlueprintItem blueprint = (BlueprintItem)FactoryItem.Fetch(p.blueprintItemID);
        GameWorld.SpawnScene(blueprint.structureScene, pose.Origin, pose.Basis.GetEuler(), init, GMPObject.serializer.Serialize(state));
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
