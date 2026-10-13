using Godot;
using System;
using System.Collections.Generic;

public partial class PhysicalFactoryItem : GMPOBox3DBody, Interactable
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

    /// <summary>What this is worth when turned in to the void, by currency (empty: nothing, though the deposit still counts).</summary>
    [Export]
    public Godot.Collections.Dictionary<Currency, int> currencyValue = new();

    [ExportGroup("Sync")]
    /// <summary>
    /// Sync <see cref="GMPObject.priority"/> while Box3D has this item asleep (1: about once a second). While it's
    /// awake the item uses its normal priority (<see cref="DefaultPriority"/> unless the scene sets one). One last
    /// update is always queued as the item falls asleep, so every peer sees where it came to rest; the slow resends
    /// after that heal any update the network lost.
    /// </summary>
    [Export]
    public int sleepingPriority = 1;

    /// <summary>
    /// Seconds a client keeps simulating a loose item it just touched (dropped it, or let go of it with a magnet rod)
    /// before it goes back to the host, which simulates all loose items: a leased claim (<see cref="GameWorld.Claim"/>).
    /// It makes the drop or throw feel instant on that client and lets it play out there.
    /// </summary>
    public const double TouchLeaseSeconds = 3.0;

    /// <summary>Sync priority of an awake item unless its scene sets its own: every tick (<see cref="GameWorld.SendThreshold"/>).</summary>
    public const int DefaultPriority = GameWorld.SendThreshold;

    /// <summary>Set once a pickup has been granted, so later simultaneous requests lose.</summary>
    bool taken;

    int awakePriority;
    /// <summary>Whether this item is asleep in this peer's simulation (tracked on the authority only).</summary>
    public bool asleep { get; private set; }

    public PhysicalFactoryItem()
    {
        priority = DefaultPriority; // scene/spawn values still override (see GMPObject.Init)
    }

    public override void _Ready()
    {
        base._Ready();
        // Items without a definition resource (e.g. test props) fall back to their id.
        if (string.IsNullOrEmpty(hoverName)) hoverName = ItemInfo.Fetch(itemID)?.displayName ?? itemID;
        if (canBePickedUp && string.IsNullOrEmpty(hoverText)) hoverText = "Press F to pick up!";
        // Items with interaction tags report touches so TagInteractions can react (Box3D then signals both bodies
        // of a contact). Material-only tags don't need it: impact sounds come from the world's hit events.
        if (tags != null && TagInteractions.HasRulesFor(tags))
        {
            contactMonitor = true;
            Connect("body_entered", Callable.From<Node>(OnBodyEntered));
        }
    }

    public override void AfterInit()
    {
        base.AfterInit();
        awakePriority = priority; // Init has resolved the scene/spawn priority by now
        // Level items can settle while the level loads, before this point, and Box3D won't report it again.
        if (authority == Lobby.selfPeerID && !this.IsAwake()) OnFellAsleep();
        // A small nudge for a fresh spawn, not for a restore (which must leave everything where it was saved).
        if (authority == Lobby.selfPeerID && !GameWorld.frozen)
        {
            this.ApplyCentralForce(new Vector3(Random.Shared.Next(-5, 5), Random.Shared.Next(-5, 5), Random.Shared.Next(-5, 5)));
        }
    }

    public override void OnAuthorityChanged()
    {
        base.OnAuthorityChanged();
        // Start awake on the new authority, and wake the body so Box3D reports it when it settles again.
        SetAwake();
        if (authority == Lobby.selfPeerID && id != 0) this.SetAwake(true);
    }

    // Only the authority simulates the item (everyone else has it Kinematic), so only its sleep state counts.
    public override void OnFellAsleep()
    {
        if (id == 0 || authority != Lobby.selfPeerID || awakePriority <= 0 || asleep) return;
        asleep = true;

        priority = sleepingPriority;
        // Queue one more update so peers get the resting pose, not the last one sent while it settled.
        priorityAccumulator = Math.Max(priorityAccumulator, GameWorld.SendThreshold);
        GameWorld.WatchForWake(this);
    }

    public override void OnWoke()
    {
        SetAwake();
    }

    void SetAwake()
    {

        asleep = false;
        if (awakePriority > 0) priority = awakePriority;
    }

    // Reactions run on this item's authority, which is the peer simulating it.
    void OnBodyEntered(Node other)
    {
        if (id != 0 && authority == Lobby.selfPeerID)
        {
            if (other is PhysicalFactoryItem otherItem && otherItem.id != 0)
            {
                TagInteractions.OnTouch(this, otherItem);
            }
            else
            {
                //AudioManager.playRandomSound(this.GetPath(), ["res://game/assets/audio/impacts/impactWood_heavy_000.ogg", "res://game/assets/audio/impacts/impactWood_heavy_001.ogg", "res://game/assets/audio/impacts/impactWood_heavy_002.ogg", "res://game/assets/audio/impacts/impactWood_heavy_003.ogg"], -35);
            }
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

    public void onInteract(ulong playerID)
    {
        if (!canBePickedUp) return;
        FactoryPlayer player = (FactoryPlayer)GameWorld.syncedObjs[playerID];
        if (player.inventory.HasRoomFor(itemID))
        {
            // Arbitrated by the item's authority so two players can't both pick it up.
            RequestPickup(player);
        }
    }

    public override void OnDespawned(DespawnReason reason)
    {

    }
}

