using Godot;

/// <summary>
/// A tool lying in the world (a dropped magnet rod, a wrench): unlike loose items it stays a node, as there are few of
/// them and they may carry more than a mesh. Attach it to the root GMPBody of the tool's dropped scene (its ItemInfo's
/// droppedScene; <see cref="ItemInfo.SpawnInWorld"/> spawns such scenes as nodes).
/// <para>
/// The host simulates every dropped tool, like loose items, and decides who picks one up.
/// </para>
/// </summary>
public partial class DroppedTool : GMPOBody, Interactable
{
    [Export]
    public string itemID;

    [Export]
    public bool canBePickTarget = true;

    [Export]
    public bool canBeGrabbed;

    [Export]
    public bool canBePickedUp = true;

    /// <summary>Set once a pickup has been granted, so later simultaneous requests lose.</summary>
    bool taken;

    public override void _Ready()
    {
        ItemInfo info = ItemInfo.Fetch(itemID);
        // Its definition's tags are the whole tool's, as for a loose item, on top of the body's own (TOOL, from its scene).
        if (info != null && info.tags != SurfaceTags.None)
        {
            tags = tags | (ulong)info.tags;
        }
        if (string.IsNullOrEmpty(hoverName))
        {
            if (info != null)
            {
                hoverName = info.displayName;
            }
            else
            {
                hoverName = itemID;
            }
        }
        if (canBePickedUp && string.IsNullOrEmpty(hoverText))
        {
            hoverText = "Press F to pick up!";
        }
    }

    public void onInteract(ulong playerID)
    {
        if (!canBePickedUp)
        {
            return;
        }
        FactoryPlayer player = (FactoryPlayer)GameWorld.syncedObjs[playerID];
        if (player.inventory.HasRoomFor(itemID))
        {
            // Arbitrated by the tool's authority so two players can't both pick it up.
            RPCManager.RequestFromAuthority(this, nameof(_RequestPickup), [player.id]);
        }
    }

    // Runs on this tool's authority.
    [RPC]
    private void _RequestPickup(ulong playerObjectId)
    {
        if (taken || !canBePickedUp || authority != Lobby.selfPeerID)
        {
            return;
        }
        // The requester must actually control the player it names.
        if (!GameWorld.syncedObjs.TryGetValue(playerObjectId, out GMPObject p) || p.authority != RPCManager.sender)
        {
            return;
        }
        taken = true;
        RPCManager.RPC(id, nameof(_ApplyPickup), [playerObjectId]);
    }

    [RPC(requireAuthority = true)]
    private void _ApplyPickup(ulong playerObjectId)
    {
        taken = true;
        if (GameWorld.syncedObjs.TryGetValue(playerObjectId, out GMPObject p) && p is FactoryPlayer player && player.isLocal)
        {
            player.ReceiveItem(itemID);
        }
        GameWorld.RemoveLocal(id, DespawnReason.PickedUp);
    }
}
