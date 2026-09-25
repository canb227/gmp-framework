using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

public partial class PhysicalFactoryItem : GMPOBox3DBody
{

    [Export]
    public string itemID;

    [Export]
    public bool canBePickTarget;

    [Export]
    public bool canBeGrabbed;

    [Export]
    public bool canBePickedUp;

    [Export]
    public bool canBeInteractedWith;
    [Export]
    public Godot.Collections.Array<ItemTags> tags;

    /// <summary>Set once a pickup has been granted, so later simultaneous requests lose.</summary>
    bool taken;

    public override void _Ready()
    {
        base._Ready();
        // Tagged items report touches so TagInteractions can react (Box3D then signals both bodies of a contact).
        if (tags != null && tags.Count > 0)
        {
            Set("contact_monitor", true);
            Connect("body_entered", Callable.From<Node>(OnBodyEntered));
        }
    }

    // Reactions run on this item's authority, which is the peer simulating it.
    void OnBodyEntered(Node other)
    {
        if (id != 0 && authority == Lobby.selfPeerID && other is PhysicalFactoryItem otherItem && otherItem.id != 0)
        {
            TagInteractions.OnTouch(this, otherItem);
        }
    }

    /// <summary>
    /// Asks this item's authority to let <paramref name="player"/> pick it up. If several players try at
    /// once, the authority accepts only the first; everyone then sees the same player get it.
    /// </summary>
    public void RequestPickup(FactoryPlayer player)
    {
        RPCManager.RequestFromAuthority(this, nameof(_RequestPickup), [player.id]);
    }

    // Runs on this item's authority.
    [RPC]
    private void _RequestPickup(ulong playerObjectId)
    {
        if (taken || !canBePickedUp || authority != Lobby.selfPeerID)
        {
            return;
        }
        // Someone else is holding it (grab tool).
        if (GameWorld.heldBy.TryGetValue(id, out ulong holder) && holder != RPCManager.sender)
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
        GameWorld.RemoveLocal(id);
    }
}

