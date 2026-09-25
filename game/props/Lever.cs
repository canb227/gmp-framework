using Godot;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// A two-position level prop: each accepted interact flips it between pulled and pushed, and its target
/// spawners keep spawning while it's pulled (<see cref="ObjectSpawner.spawning"/>). Like <see cref="BasicButton"/>
/// it's decided by the lobby host (level props aren't GMPObjects): the host flips the state, ignoring interacts
/// within <see cref="toggleCooldown"/>, and tells every peer the new state, so everyone agrees on it.
/// <para>
/// Scene: the root is a static <c>Box3DBody</c> whose collider covers the lever (it's what the player aims at),
/// with <see cref="handle"/> a pivot node that tilts between <see cref="pushedAngle"/> and <see cref="pulledAngle"/>.
/// </para>
/// </summary>
public partial class Lever : Node3D
{
    [Export] public string displayName = "Lever";
    /// <summary>Pivot that tilts about its local X axis to show the state.</summary>
    [Export] public Node3D handle;
    /// <summary>Handle tilt (degrees about X) when pushed (off) and when pulled (on).</summary>
    [Export] public float pushedAngle = -35f;
    [Export] public float pulledAngle = 35f;
    [Export] public Godot.Collections.Array<ObjectSpawner> targetObjectSpawner;
    /// <summary>Seconds after an accepted flip during which further interacts are ignored.</summary>
    [Export] public double toggleCooldown = 0.5;

    public bool pulled { get; private set; }
    /// <summary>Number of flips that took effect (used by the headless multiplayer test).</summary>
    public int acceptedToggles;

    List<ObjectSpawner> targetSpawnerList;
    ulong lastAcceptedToggleMs; // host only
    Tween handleTween;

    public override void _Ready()
    {
        targetSpawnerList = targetObjectSpawner?.ToList() ?? [];
        if (handle != null) handle.RotationDegrees = new Vector3(pushedAngle, 0, 0);
    }

    /// <summary>Called by the interacting player; asks the host to flip the lever.</summary>
    public void OnPressed()
    {
        RPCManager.RPCTo(Lobby.hostID, this, nameof(_RequestToggle), []);
    }

    // Runs on the host. First interact wins; interacts inside the cooldown are dropped.
    [RPC]
    private void _RequestToggle()
    {
        ulong now = Time.GetTicksMsec();
        if (lastAcceptedToggleMs != 0 && now - lastAcceptedToggleMs < toggleCooldown * 1000)
        {
            return;
        }
        lastAcceptedToggleMs = now;
        RPCManager.RPC(this, nameof(_ApplyToggle), [!pulled]);
    }

    // Carries the new state rather than "flip", so every peer ends up on the host's state.
    [RPC(requireAuthority = true)]
    private void _ApplyToggle(bool nowPulled)
    {
        acceptedToggles++;
        pulled = nowPulled;
        foreach (ObjectSpawner spawner in targetSpawnerList)
        {
            spawner.spawning = pulled;
        }
        if (handle != null)
        {
            handleTween?.Kill();
            handleTween = CreateTween();
            handleTween.TweenProperty(handle, "rotation_degrees:x", pulled ? pulledAngle : pushedAngle, 0.25)
                .SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        }
    }
}
