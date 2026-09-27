using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// FactoryPlayer: the physics "grab" tool. With an empty hand, holding primary on a grabbable item holds it at
/// a point in front of the camera; the scroll wheel moves that point nearer or further (see <c>HandleScrollWheel</c>).
/// Grabbing first claims the item (<see cref="GameWorld.Claim"/>) so this peer simulates it and no one else can
/// grab it meanwhile.
/// <para>
/// The hold is a mass-independent controller, so a feather and a lead weight respond alike: a position loop
/// picks a velocity that closes the gap (plus the grab point's own velocity, so held items keep up while you
/// move), a velocity loop turns that into an acceleration (never correcting more than a fraction of the
/// difference per tick, which is what keeps light objects from oscillating), gravity is cancelled, and the
/// result is scaled by the item's mass and capped at <see cref="grabStrength"/>, so heavy items sag and lag.
/// Spin is damped while held.
/// </para>
/// </summary>
public partial class FactoryPlayer
{
    [ExportGroup("Grab: hold distance")]
    [Export] float grabMinDistance = 1f;
    [Export] float grabMaxDistance = 4f;
    [Export] float grabScrollStep = 0.25f;

    [ExportGroup("Grab: feel")]
    /// <summary>How fast the gap to the grab point closes (1/s).</summary>
    [Export] float grabPositionGain = 8f;
    /// <summary>How fast the item's velocity follows the wanted velocity (1/s).</summary>
    [Export] float grabVelocityGain = 20f;
    /// <summary>Fastest the item is pulled toward the grab point (m/s), on top of following it.</summary>
    [Export] float grabMaxPullSpeed = 12f;
    /// <summary>
    /// Smoothing time (s) for the grab point's velocity. Mouse look and movement move the point between physics
    /// ticks, in uneven steps; unsmoothed, those steps would jolt the held item.
    /// </summary>
    [Export] float grabFollowSmoothing = 0.06f;
    /// <summary>Force cap (N), including holding the item up against gravity.</summary>
    [Export] float grabStrength = 400f;
    /// <summary>How fast the item's spin is damped while held (1/s).</summary>
    [Export] float grabSpinDamping = 6f;
    /// <summary>The grab lets go if the item stays further than this from the grab point...</summary>
    [Export] float grabBreakDistance = 2.5f;
    /// <summary>...for this long (s), e.g. when it's pinned behind a wall.</summary>
    [Export] float grabBreakTime = 0.5f;

    PhysicalFactoryItem grabTarget;
    bool isGrabbing => grabTarget != null;

    Node3D grabNode;
    float grabDistance;
    Vector3 lastGrabPoint;
    Vector3 grabPointVelocity;
    float grabStrainedFor;

    /// <summary>Claims this player has requested and is waiting on, by object id.</summary>
    readonly Dictionary<ulong, PhysicalFactoryItem> pendingGrabClaims = new();

    static readonly Vector3 grabGravity = ProjectSettings.GetSetting("physics/3d/default_gravity_vector").AsVector3()
        * ProjectSettings.GetSetting("physics/3d/default_gravity").AsSingle();

    /// <summary>Holds the grab button down regardless of input (used by the headless multiplayer test).</summary>
    public bool forceGrabHeld;
    public PhysicalFactoryItem grabbedItem => grabTarget;
    /// <summary>Where a grabbed item is held.</summary>
    public Vector3 grabPoint => grabNode.GlobalPosition;

    void ReadyGrab()
    {
        grabNode = GetNode<Node3D>("%grabNode");
    }

    // Subscribed from AfterInit on the local player; unsubscribed from _ExitTree.
    void SubscribeGrabClaims() => GameWorld.ClaimGrantedEvent += OnClaimGranted;
    void UnsubscribeGrabClaims() => GameWorld.ClaimGrantedEvent -= OnClaimGranted;

    void HandleGrabInput(InputEvent @event)
    {
        if (@event.IsActionPressed("primary"))
        {
            bool emptyHand = inventory.ActiveHotbarSlot == -1 || inventory.slots[inventory.ActiveHotbarSlot].IsEmpty;
            if (emptyHand && pickTarget is PhysicalFactoryItem item && item.canBeGrabbed)
            {
                RequestGrab(item);
            }
        }
        if (@event.IsActionReleased("primary") && isGrabbing)
        {
            EndGrab();
        }
    }

    /// <summary>Claims <paramref name="item"/>; the grab starts when the claim is granted, if primary is still held.</summary>
    public void RequestGrab(PhysicalFactoryItem item)
    {
        pendingGrabClaims[item.id] = item;
        GameWorld.Claim(item);
    }

    public void ReleaseGrab()
    {
        if (isGrabbing) EndGrab();
    }

    void OnClaimGranted(ulong id, ulong newAuthority)
    {
        if (!pendingGrabClaims.Remove(id, out PhysicalFactoryItem item))
        {
            return;
        }
        if (newAuthority != Lobby.selfPeerID || !IsInstanceValid(item))
        {
            return; // someone else got it first
        }
        // Granted, but if the button was released or we moved on in the meantime, hand it back.
        if (!isGrabbing && (Input.IsActionPressed("primary") || forceGrabHeld))
        {
            StartGrab(item);
        }
        else
        {
            GameWorld.Release(item);
        }
    }

    void StartGrab(PhysicalFactoryItem item)
    {
        grabTarget = item;
        // Hold it where it is (distance-wise) rather than yanking it to a fixed point.
        float distance = (item.GlobalPosition - camera.GlobalPosition).Dot(-camera.GlobalTransform.Basis.Z);
        SetGrabDistance(distance);
        lastGrabPoint = grabPoint;
        grabPointVelocity = Vector3.Zero;
        grabStrainedFor = 0;
    }

    void EndGrab()
    {
        GameWorld.Release(grabTarget);
        grabTarget = null;
    }

    /// <summary>Moves the grab point one step nearer (+1) or further (-1), within limits.</summary>
    void MoveGrabPoint(int step)
    {
        SetGrabDistance(grabDistance - step * grabScrollStep);
    }

    void SetGrabDistance(float distance)
    {
        grabDistance = Mathf.Clamp(distance, grabMinDistance, grabMaxDistance);
        grabNode.Position = new Vector3(grabNode.Position.X, grabNode.Position.Y, -grabDistance);
    }

    /// <summary>Drives the held item toward the grab point. Runs each physics tick on the local player.</summary>
    void ApplyGrabForce(double delta)
    {
        if (!isGrabbing) return;
        if (!IsInstanceValid(grabTarget) || grabTarget.authority != Lobby.selfPeerID)
        {
            // Despawned (e.g. picked up, or fell in a void) or taken over while held.
            grabTarget = null;
            return;
        }

        float dt = (float)delta;
        Vector3 target = grabPoint;
        Vector3 rawTargetVelocity = (target - lastGrabPoint) / dt;
        lastGrabPoint = target;
        grabPointVelocity = grabPointVelocity.Lerp(rawTargetVelocity, 1f - Mathf.Exp(-dt / grabFollowSmoothing));
        Vector3 targetVelocity = grabPointVelocity;

        Vector3 error = target - grabTarget.GlobalPosition;
        Vector3 pull = error * grabPositionGain;
        if (pull.Length() > grabMaxPullSpeed) pull = pull.Normalized() * grabMaxPullSpeed;
        Vector3 wantedVelocity = targetVelocity + pull;

        Vector3 velocity = grabTarget.Call("get_linear_velocity").AsVector3();
        // Correct at most 90% of the velocity difference per tick, whatever the gain: this keeps the loop stable.
        float response = Mathf.Min(grabVelocityGain, 0.9f / dt);
        Vector3 accel = (wantedVelocity - velocity) * response - grabGravity;

        float mass = grabTarget.Call("get_mass").AsSingle();
        Vector3 force = accel * mass;
        if (force.Length() > grabStrength) force = force.Normalized() * grabStrength;
        grabTarget.ApplyCentralForce(force);

        DampGrabSpin(dt);

        grabStrainedFor = error.Length() > grabBreakDistance ? grabStrainedFor + dt : 0;
        if (grabStrainedFor > grabBreakTime)
        {
            EndGrab();
        }
    }

    // Removes a fraction of the held item's spin each tick (an angular impulse of I * delta-omega).
    void DampGrabSpin(float dt)
    {
        Vector3 spin = grabTarget.Call("get_angular_velocity").AsVector3();
        if (spin.LengthSquared() < 1e-6f) return;
        Basis inertia = grabTarget.Call("get_inertia_tensor").AsBasis();
        Vector3 deltaSpin = -spin * Mathf.Min(1f, grabSpinDamping * dt);
        grabTarget.Call("apply_angular_impulse", [inertia * deltaSpin]);
    }
}
