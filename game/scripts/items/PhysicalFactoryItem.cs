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

    /// <summary>
    /// Whether this item's meshes cast shadows. Off by default: with thousands of items, their shadow passes cost more
    /// render time than they're worth on small things.
    /// </summary>
    [Export]
    public bool castsShadows = false;

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

    /// <summary>
    /// Times the priority of an item inside any player's view cone (see <see cref="ViewConeCos"/>): with the budget
    /// full, what someone is looking at is updated more often than what's behind them. Moderate, so items out of view
    /// still update steadily and are fresh enough when someone turns round.
    /// </summary>
    const float ViewMultiplier = 2.5f;
    /// <summary>Cosine of the view cone's half angle (about 57°, a little wider than the camera sees side to side).</summary>
    const float ViewConeCos = 0.55f;

    /// <summary>Every player's position as of <see cref="playerPositionsFrame"/>: read once per send, for all the items.</summary>
    static readonly List<Vector3> playerPositions = new();
    /// <summary>Every player's eye and look direction (from its camera, which carries the synced head pitch), read with the positions.</summary>
    static readonly List<(Vector3 eye, Vector3 look)> playerViews = new();
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

    /// <summary>Whether this item reports its touches for TagInteractions (it has a watcher tag; see TagInteractions).</summary>
    public bool reportsTouches { get; private set; }

    // ---- counts ----
    // Every item in the world on this peer, kept up as items enter and leave the tree (which also catches a whole
    // world being cleared, where OnDespawned isn't called). Spawners read them for their limits (ItemSpawner).

    /// <summary>How many items there are in the world, of any kind.</summary>
    public static int totalCount { get; private set; }
    static readonly Dictionary<string, int> countByItem = new();
    /// <summary>The item id this item is counted under in <see cref="countByItem"/> (null: none).</summary>
    string countedAs;

    /// <summary>How many of <paramref name="itemID"/> there are in the world.</summary>
    public static int CountOf(string itemID)
    {
        return countByItem.GetValueOrDefault(itemID, 0);
    }

    public override void _EnterTree()
    {
        base._EnterTree();
        totalCount++;
        CountAs(itemID);
    }

    public override void _ExitTree()
    {
        base._ExitTree();
        totalCount--;
        CountAs(null);
    }

    /// <summary>Moves this item's count to <paramref name="newItemID"/> (null: uncounted). Call when <see cref="itemID"/> changes while in the tree.</summary>
    protected void CountAs(string newItemID)
    {
        if (countedAs != null)
        {
            countByItem[countedAs]--;
        }
        countedAs = newItemID;
        if (newItemID != null)
        {
            countByItem[newItemID] = CountOf(newItemID) + 1;
        }
    }

    public override void _Ready()
    {
        base._Ready();
        // Items without a definition resource (e.g. test props) fall back to their id.
        if (string.IsNullOrEmpty(hoverName)) hoverName = ItemInfo.Fetch(itemID)?.displayName ?? itemID;
        if (canBePickedUp && string.IsNullOrEmpty(hoverText)) hoverText = "Press F to pick up!";
        if (!castsShadows)
        {
            foreach (Node node in FindChildren("*", "GeometryInstance3D", true, false))
            {
                ((GeometryInstance3D)node).CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
            }
        }
        RefreshTouchReporting();
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

    /// <summary>
    /// Only items that can start a reaction report their touches (TagInteractions' watcher tags), and they run the
    /// other item's side too. Every report is a call into C# per new contact, which cost a few milliseconds a physics
    /// tick in a tumbling pile of scrap when every tagged item reported. Material-only tags don't need it: impact sounds
    /// come from the world's hit events. Call again after changing <see cref="tags"/> (e.g. a rule that makes an item HOT).
    /// </summary>
    public void RefreshTouchReporting()
    {
        bool watch = tags != null && TagInteractions.IsWatcher(tags);
        if (watch == reportsTouches)
        {
            return;
        }
        reportsTouches = watch;
        contactMonitor = watch;
        if (watch)
        {
            ContactStarted += OnTouched;
        }
        else
        {
            ContactStarted -= OnTouched;
        }
    }

    // The authority tracks sleep through Box3D's sleep signal (and GameWorld.WatchForWake), so building a state
    // needn't ask Box3D. Just woken but not yet noticed, a state goes out without its velocity; its position is right.
    protected override bool IsResting()
    {
        return asleep;
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
        // At rest with its pose already sent: nothing to send until it moves (see GMPOBox3DBody.AtRestAndSent).
        if (AtRestAndSent())
        {
            priority = -1;
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
            playerViews.Clear();
            foreach (FactoryPlayer player in FactoryPlayer.all)
            {
                playerPositions.Add(player.GlobalPosition);
                Transform3D view = player.camera.GlobalTransform;
                playerViews.Add((view.Origin, -view.Basis.Z.Normalized()));
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
        foreach ((Vector3 eye, Vector3 look) in playerViews)
        {
            Vector3 toItem = position - eye;
            float distance = toItem.Length();
            if (distance > 0 && look.Dot(toItem) >= ViewConeCos * distance)
            {
                multiplier *= ViewMultiplier;
                break;
            }
        }
        priority = Mathf.RoundToInt(unscaled * multiplier);
        if (GameWorld.sendFrame < heldUntilFrame && priority < HeldPriority)
        {
            priority = HeldPriority;
        }
    }

    // Reactions run on this item's authority, which is the peer simulating it. An item that doesn't report its own
    // touches has its side of the rules run here, by the watcher that touched it, if this peer simulates it too.
    void OnTouched(Node other)
    {
        if (id != 0 && authority == Lobby.selfPeerID)
        {
            if (other is PhysicalFactoryItem otherItem && otherItem.id != 0)
            {
                TagInteractions.OnTouch(this, otherItem);
                if (!otherItem.reportsTouches && otherItem.authority == Lobby.selfPeerID)
                {
                    TagInteractions.OnTouch(otherItem, this);
                }
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

