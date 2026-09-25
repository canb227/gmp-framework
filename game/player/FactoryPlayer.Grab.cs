using Godot;
using System;

/// <summary>
/// FactoryPlayer: the physics "grab" tool. With an empty hand, holding primary on a grabbable item
/// pulls it toward a grab point in front of the camera with a spring-like force; the scroll wheel
/// moves that point nearer or further (see <c>HandleScrollWheel</c>). Grabbing first claims the item
/// (<see cref="GameWorld.Claim"/>) so this peer simulates it and no one else can grab it meanwhile.
/// </summary>
public partial class FactoryPlayer
{
    [ExportGroup("Grab: hold distance")]
    [Export] float grabMinimumDistance = -1f;
    [Export] float grabDistanceIncrement = .25f;
    [Export] float grabMaximumDistance = -4f;

    [ExportGroup("Grab: force")]
    [Export] float grabForceBase = 25f;
    [Export] float grabForceExponent = 1.2f;
    [Export] float grabDamping = 10f;
    [Export] float grabNearDamping = 5f;
    [Export] float grabMaxForce = 400f;

    PhysicalFactoryItem grabTarget;
    bool isGrabbing => grabTarget != null;

    Node3D grabNode;
    float defaultGrabLocation;

    /// <summary>Claims this player has requested and is waiting on, by object id.</summary>
    readonly System.Collections.Generic.Dictionary<ulong, PhysicalFactoryItem> pendingGrabClaims = new();

    void ReadyGrab()
    {
        grabNode = GetNode<Node3D>("%grabNode");
        defaultGrabLocation = grabNode.Position.Z;
    }

    // Subscribed from AfterInit on the local player; unsubscribed from _ExitTree.
    void SubscribeGrabClaims() => GameWorld.ClaimGrantedEvent += OnClaimGranted;
    void UnsubscribeGrabClaims() => GameWorld.ClaimGrantedEvent -= OnClaimGranted;

    void OnClaimGranted(ulong id, ulong newAuthority)
    {
        if (!pendingGrabClaims.Remove(id, out PhysicalFactoryItem item))
        {
            return;
        }
        if (newAuthority != Lobby.selfPeerID)
        {
            return; // someone else got it first
        }
        // Granted — but if the button was released or we moved on in the meantime, hand it back.
        if (!isGrabbing && Input.IsActionPressed("primary"))
        {
            StartGrab(item);
        }
        else
        {
            GameWorld.Release(item);
        }
    }

    void HandleGrabInput(InputEvent @event)
    {
        if (@event.IsActionPressed("primary"))
        {
            if (inventory.ActiveHotbarSlot == -1 || inventory.slots[inventory.ActiveHotbarSlot].IsEmpty)
            {
                Logging.Log($"empty click: {inventory.ActiveHotbarSlot}", "GameWorld");
                if (pickTarget is PhysicalFactoryItem item && item.canBeGrabbed)
                {
                    pendingGrabClaims[item.id] = item;
                    GameWorld.Claim(item); // grab starts in OnClaimGranted
                }
            }
        }
        if (@event.IsActionReleased("primary") && isGrabbing)
        {
            EndGrab();
        }
    }

    /// <summary>Moves the grab point one increment nearer (+1) or further (-1), within limits.</summary>
    void MoveGrabPoint(int step)
    {
        float z = step > 0
            ? Math.Min(grabNode.Position.Z + grabDistanceIncrement, grabMinimumDistance)
            : Math.Max(grabNode.Position.Z - grabDistanceIncrement, grabMaximumDistance);
        SetGrabPoint(z);
    }

    /// <summary>Pulls the held item toward the grab point. Runs each physics tick on the local player.</summary>
    void ApplyGrabForce()
    {
        if (!isGrabbing) return;
        if (!IsInstanceValid(grabTarget))
        {
            // Despawned while held (e.g. picked up by its holder).
            grabTarget = null;
            SetGrabPoint(defaultGrabLocation);
            return;
        }

        Vector3 displacement = grabNode.GlobalPosition - grabTarget.GlobalPosition;
        float distance = displacement.Length();
        if (distance <= 0.001f) return;

        Vector3 forceDir = displacement / distance;
        float forceMagnitude = Mathf.Min(grabForceBase * (Mathf.Exp(grabForceExponent * distance) - 1f), grabMaxForce);
        Vector3 attractionForce = forceDir * forceMagnitude;

        Vector3 velocity = (Vector3)grabTarget.Get("linear_velocity");
        float dampingScale = grabDamping * (1f + grabNearDamping / Mathf.Max(distance, 0.1f));
        Vector3 dampingForce = -velocity * dampingScale;

        grabTarget.ApplyCentralForce(attractionForce + dampingForce);
    }

    void StartGrab(PhysicalFactoryItem item)
    {
        Logging.Log($"starting grab on {item.Name}", "Player");
        grabTarget = item;
        grabTarget.Set("gravity_scale", 0.1f);
        grabTarget.Set("linear_damping", 5f);
        grabTarget.Set("angular_damping", 4f);
    }

    void EndGrab()
    {
        grabTarget.Set("gravity_scale", 1f);
        grabTarget.Set("linear_damping", 0f);
        grabTarget.Set("angular_damping", 0f);
        Logging.Log($"ending grab on {grabTarget.Name}", "Player");
        SetGrabPoint(defaultGrabLocation);
        GameWorld.Release(grabTarget);
        grabTarget = null;
    }

    void SetGrabPoint(float z)
    {
        grabNode.Position = new Vector3(grabNode.Position.X, grabNode.Position.Y, z);
    }
}
