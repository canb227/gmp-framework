using Godot;
using System.Collections.Generic;

/// <summary>
/// Tool: while primary is held, pulls nearby loose items into a floating ball in front of the camera that
/// follows the cursor; the scroll wheel moves the ball nearer or further while active.
/// <para>
/// Multiplayer: like the grab tool, the magnet claims each item it catches (<see cref="GameWorld.Claim"/>),
/// making this peer the item's authority, so this peer simulates the pull and everyone else sees the item move
/// through its state updates. Items someone else holds are refused. Every item is released when primary is let
/// go or the rod is put away.
/// </para>
/// <para>
/// Each caught item gets a spring-damper acceleration toward the ball centre plus gravity cancellation, with
/// the total force capped at <see cref="maxForce"/>: light items snap in, heavy ones lag and sag. The items
/// collide with each other, which packs them into a ball.
/// </para>
/// </summary>
public partial class MagnetRod : HeldItem
{
    [ExportGroup("Reach")]
    /// <summary>Items within this distance of the ball centre are caught.</summary>
    [Export] public float captureRadius = 5f;
    [Export] public int maxItems = 30;
    /// <summary>Seconds between searches for new items while active.</summary>
    [Export] public double rescanInterval = 0.25;

    [ExportGroup("Ball")]
    [Export] public float holdDistance = 3f;
    [Export] public float minHoldDistance = 1.5f;
    [Export] public float maxHoldDistance = 8f;
    [Export] public float scrollStep = 0.5f;

    [ExportGroup("Force")]
    /// <summary>Spring stiffness toward the ball centre (acceleration per metre, 1/s²).</summary>
    [Export] public float spring = 30f;
    /// <summary>Velocity damping (1/s).</summary>
    [Export] public float damping = 8f;
    /// <summary>Force cap per item (N), including gravity cancellation.</summary>
    [Export] public float maxForce = 600f;

    /// <summary>Holds the magnet on regardless of input (used by the headless multiplayer test).</summary>
    public bool forceActive;

    /// <summary>Items currently being pulled (this peer is their authority and holder).</summary>
    public IReadOnlyCollection<PhysicalFactoryItem> captured => capturedItems;

    readonly HashSet<PhysicalFactoryItem> capturedItems = new();
    readonly Dictionary<ulong, PhysicalFactoryItem> pendingClaims = new();
    bool active;
    double untilRescan;
    MeshInstance3D ballMarker;
    static readonly Vector3 gravity = ProjectSettings.GetSetting("physics/3d/default_gravity_vector").AsVector3()
        * ProjectSettings.GetSetting("physics/3d/default_gravity").AsSingle();

    public override void Equip(ItemInfo item, FactoryPlayer player)
    {
        base.Equip(item, player);
        if (!isLocal) return;
        GameWorld.ClaimGrantedEvent += OnClaimGranted;
        ballMarker = GetNode<MeshInstance3D>("BallMarker");
        ballMarker.TopLevel = true;
        ballMarker.Visible = false;
    }

    public override void _ExitTree()
    {
        if (!isLocal) return;
        SetActive(false);
        GameWorld.ClaimGrantedEvent -= OnClaimGranted;
    }

    public override bool HandleInput(InputEvent @event)
    {
        if (@event.IsActionPressed("primary"))
        {
            SetActive(true);
            return true;
        }
        if (@event.IsActionReleased("primary"))
        {
            return true; // released in _PhysicsProcess, which also covers losing focus mid-hold
        }
        return false;
    }

    public override bool HandleScroll(int step)
    {
        if (!active) return false;
        holdDistance = Mathf.Clamp(holdDistance - step * scrollStep, minHoldDistance, maxHoldDistance);
        return true;
    }

    Vector3 BallCentre()
    {
        Camera3D camera = player.camera;
        return camera.GlobalPosition - camera.GlobalTransform.Basis.Z * holdDistance;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!isLocal) return;
        if (active && !forceActive && !Input.IsActionPressed("primary"))
        {
            SetActive(false);
        }
        else if (!active && forceActive)
        {
            SetActive(true);
        }
        if (!active) return;

        Vector3 centre = BallCentre();
        ballMarker.GlobalPosition = centre;

        untilRescan -= delta;
        if (untilRescan <= 0)
        {
            untilRescan = rescanInterval;
            CatchNearbyItems(centre);
        }
        Pull(centre);
    }

    void SetActive(bool on)
    {
        if (active == on) return;
        active = on;
        untilRescan = 0;
        if (ballMarker != null) ballMarker.Visible = on;
        if (!on)
        {
            foreach (PhysicalFactoryItem item in capturedItems)
            {
                if (IsInstanceValid(item)) GameWorld.Release(item);
            }
            capturedItems.Clear();
            // Claims still in flight are released as they are granted (OnClaimGranted).
        }
    }

    void CatchNearbyItems(Vector3 centre)
    {
        foreach (Node n in GameWorld.OverlapSphere(centre, captureRadius, (int)~GameWorld.QueryHiddenLayer))
        {
            if (capturedItems.Count + pendingClaims.Count >= maxItems) return;
            if (n is not PhysicalFactoryItem item || !item.canBeGrabbed || item.authoredBodyType != BodyTypeEnum.Dynamic) continue;
            if (item.id == 0 || capturedItems.Contains(item) || pendingClaims.ContainsKey(item.id)) continue;
            if (GameWorld.heldBy.TryGetValue(item.id, out ulong holder) && holder != Lobby.selfPeerID) continue;
            pendingClaims[item.id] = item;
            GameWorld.Claim(item);
        }
    }

    void OnClaimGranted(ulong id, ulong newAuthority)
    {
        if (!pendingClaims.Remove(id, out PhysicalFactoryItem item)) return;
        if (newAuthority != Lobby.selfPeerID || !IsInstanceValid(item)) return;
        if (active)
            capturedItems.Add(item);
        else
            GameWorld.Release(item);
    }

    void Pull(Vector3 centre)
    {
        capturedItems.RemoveWhere(item => !IsInstanceValid(item) || item.authority != Lobby.selfPeerID);
        foreach (PhysicalFactoryItem item in capturedItems)
        {
            float mass = item.Call("get_mass").AsSingle();
            Vector3 velocity = item.Call("get_linear_velocity").AsVector3();
            Vector3 accel = (centre - item.GlobalPosition) * spring - velocity * damping - gravity;
            Vector3 force = accel * mass;
            if (force.Length() > maxForce) force = force.Normalized() * maxForce;
            item.ApplyCentralForce(force);
        }
    }
}
