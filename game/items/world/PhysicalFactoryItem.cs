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

    [ExportGroup("Sync")]
    /// <summary>
    /// Sync <see cref="GMPObject.priority"/> while Box3D has this item asleep. While it's awake the item uses its
    /// normal priority (<see cref="DefaultPriority"/> unless the scene sets one), so moving items win the
    /// state-update budget over resting ones. One last update is always queued as the item falls asleep, so every
    /// peer sees where it came to rest.
    /// </summary>
    [Export]
    public int sleepingPriority = 1;

    /// <summary>Sync priority of an awake item unless its scene sets its own.</summary>
    public const int DefaultPriority = 10;
    /// <summary>
    /// How often (s) a sleeping item checks whether it has woken. Box3D signals falling asleep but not waking, so
    /// waking is polled, and only while asleep; until it's noticed the item just syncs at its sleeping priority.
    /// </summary>
    const double WakeCheckInterval = 0.2;

    /// <summary>Set once a pickup has been granted, so later simultaneous requests lose.</summary>
    bool taken;

    int awakePriority;
    double untilWakeCheck;
    /// <summary>Whether this item is asleep in this peer's simulation (tracked on the authority only).</summary>
    public bool asleep { get; private set; }

    public PhysicalFactoryItem()
    {
        priority = DefaultPriority; // scene/spawn values still override (see GMPObject.Init)
    }

    public override void _Ready()
    {
        base._Ready();
        // Items with interaction tags report touches so TagInteractions can react (Box3D then signals both bodies
        // of a contact). Material-only tags don't need it: impact sounds come from the world's hit events.
        if (tags != null && TagInteractions.HasRulesFor(tags))
        {
            Set("contact_monitor", true);
            Connect("body_entered", Callable.From<Node>(OnBodyEntered));
        }
    }

    public override void AfterInit()
    {
        base.AfterInit();
        awakePriority = priority; // Init has resolved the scene/spawn priority by now
        // Level items can settle while the level loads, before this point, and Box3D won't report it again.
        if (authority == Lobby.selfPeerID && !Call("is_awake").AsBool()) OnFellAsleep();
    }

    public override void OnAuthorityChanged()
    {
        base.OnAuthorityChanged();
        // Start awake on the new authority, and wake the body so Box3D reports it when it settles again.
        SetAwake();
        if (authority == Lobby.selfPeerID && id != 0) Call("set_awake", [true]);
    }

    // Only the authority simulates the item (everyone else has it Kinematic), so only its sleep state counts.
    public override void OnFellAsleep()
    {
        if (id == 0 || authority != Lobby.selfPeerID || awakePriority <= 0 || asleep) return;
        asleep = true;
        untilWakeCheck = WakeCheckInterval;
        priority = sleepingPriority;
        // Queue one more update so peers get the resting pose, not the last one sent while it settled.
        priorityAccumulator = Math.Max(priorityAccumulator, awakePriority);
    }

    public override void _PhysicsProcess(double delta)
    {
        base._PhysicsProcess(delta);
        if (!asleep) return;
        untilWakeCheck -= delta;
        if (untilWakeCheck > 0) return;
        untilWakeCheck = WakeCheckInterval;
        if (authority != Lobby.selfPeerID || Call("is_awake").AsBool()) SetAwake();
    }

    void SetAwake()
    {
        asleep = false;
        if (awakePriority > 0) priority = awakePriority;
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

