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

    // ---- sync priority ----
    // An item's sync priority is worked out on every send (UpdateSyncPriority): its basePriority, +1 while awake,
    // times up to ProximityMultiplier the nearer a player is, and at least HeldPriority while a magnet rod holds it
    // and for a moment after. It runs for every item on every send, so it keeps calls into the engine to one per item.

    /// <summary>Up to this many times the priority right next to a player, falling evenly to 1x at <see cref="ProximityRange"/>.</summary>
    const float ProximityMultiplier = 10f;
    /// <summary>Metres from the nearest player (any peer's) beyond which an item gets no proximity bonus.</summary>
    const float ProximityRange = 30f;
    /// <summary>Priority of an item a magnet rod holds: above any proximity bonus, below the players.</summary>
    public const int HeldPriority = 100;
    /// <summary>How long an item keeps <see cref="HeldPriority"/> after a rod lets go, so throws and drops are followed closely: 1 s.</summary>
    public static readonly ulong HeldLingerFrames = (ulong)Engine.PhysicsTicksPerSecond;
    /// <summary>Added to the accumulator as the item falls asleep, so its resting pose goes out with the next send or so.</summary>
    const int RestingPoseBump = 100;

    /// <summary>Until which physics frame this item has <see cref="HeldPriority"/>; kept up by the magnet rods holding it.</summary>
    public ulong heldUntilFrame;

    /// <summary>Every player's position as of <see cref="playerPositionsFrame"/>: read once per send, for all the items.</summary>
    static readonly List<Vector3> playerPositions = new();
    /// <summary>The send (<see cref="GameWorld.sendFrame"/>) <see cref="playerPositions"/> were read for.</summary>
    static ulong playerPositionsFrame = ulong.MaxValue;

    /// <summary>
    /// Sync priority before the awake, proximity and held bonuses; -1 or lower never sends. Set here rather than in
    /// <see cref="GMPObject.priority"/>, which is recalculated on every send (and saved as whatever it was).
    /// </summary>
    [ExportGroup("Sync")]
    [Export]
    public int basePriority = 1;

    /// <summary>Set once a pickup has been granted, so later simultaneous requests lose.</summary>
    bool taken;

    /// <summary>Whether this item is asleep in this peer's simulation (tracked on the authority only).</summary>
    public bool asleep { get; private set; }

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
        if (id == 0 || authority != Lobby.selfPeerID || basePriority <= -1 || asleep) return;
        asleep = true;
        // Peers should soon get the resting pose, not the last one sent while it settled.
        priorityAccumulator += RestingPoseBump;
        GameWorld.WatchForWake(this);
    }

    public override void OnWoke()
    {
        SetAwake();
    }

    void SetAwake()
    {
        asleep = false;
    }

    public override void UpdateSyncPriority()
    {
        if (basePriority <= -1)
        {
            return;
        }
        int unscaled = basePriority;
        if (!asleep)
        {
            unscaled += 1;
        }
        // The first item to get here in a send reads the players' positions for all of them.
        if (playerPositionsFrame != GameWorld.sendFrame)
        {
            playerPositionsFrame = GameWorld.sendFrame;
            playerPositions.Clear();
            foreach (FactoryPlayer player in FactoryPlayer.all)
            {
                playerPositions.Add(player.GlobalPosition);
            }
        }
        Vector3 position = GlobalPosition;
        float nearestSquared = ProximityRange * ProximityRange;
        foreach (Vector3 playerPosition in playerPositions)
        {
            float distanceSquared = position.DistanceSquaredTo(playerPosition);
            if (distanceSquared < nearestSquared)
            {
                nearestSquared = distanceSquared;
            }
        }
        float nearest = Mathf.Sqrt(nearestSquared);
        float multiplier = 1f + (ProximityMultiplier - 1f) * (1f - nearest / ProximityRange);
        priority = Mathf.RoundToInt(unscaled * multiplier);
        if (GameWorld.sendFrame < heldUntilFrame && priority < HeldPriority)
        {
            priority = HeldPriority;
        }
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

