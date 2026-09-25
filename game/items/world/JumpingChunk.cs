using Godot;
using System;

/// <summary>
/// A test item that hops around on its own: every <see cref="minInterval"/>-<see cref="maxInterval"/> seconds
/// it gets a random upward-and-sideways impulse. Only the item's authority simulates it, so only the authority
/// pushes it; everyone else sees the hops through its state updates.
/// </summary>
public partial class JumpingChunk : PhysicalFactoryItem
{
    [Export] public double minInterval = 1.0;
    [Export] public double maxInterval = 3.0;
    /// <summary>Vertical speed change per hop, m/s (the impulse is scaled by mass).</summary>
    [Export] public float hopSpeed = 5f;
    /// <summary>Largest sideways speed change per hop, m/s.</summary>
    [Export] public float sidewaysSpeed = 1f;

    double untilNextHop;

    public override void _PhysicsProcess(double delta)
    {
        base._PhysicsProcess(delta);
        if (id == 0 || authority != Lobby.selfPeerID) return;

        untilNextHop -= delta;
        if (untilNextHop > 0) return;
        untilNextHop = minInterval + Random.Shared.NextDouble() * (maxInterval - minInterval);

        float angle = (float)(Random.Shared.NextDouble() * Math.Tau);
        Vector3 dv = new(Mathf.Cos(angle) * sidewaysSpeed, hopSpeed, Mathf.Sin(angle) * sidewaysSpeed);
        float mass = Call("get_mass").AsSingle();
        Call("apply_central_impulse", [dv * mass]);
    }
}
