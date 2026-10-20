using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// This physics tick's impacts near the local camera, read once after the step and shared by everything that shows
/// them (sounds, particles). The same on every peer: the hits of what this peer simulates, and those the other peers
/// sent, as their playback reaches them. For visuals and sounds only: some hits never reach some peers (packet loss,
/// distance), so nothing here may change game state.
/// </summary>
public static class HitStream
{
    /// <summary>Hits further than this from the camera are left out (m), the same range peers send hits within.</summary>
    public const float Range = 60;

    /// <summary>This tick's hits, refilled every physics tick. To see each tick's once, read them in <see cref="Updated"/>.</summary>
    public static readonly List<ContactHit> hits = new();

    /// <summary>Physics ticks so far: the tick <see cref="hits"/> are from.</summary>
    public static ulong tick { get; private set; }

    /// <summary>Raised once each physics tick, right after <see cref="hits"/> is filled.</summary>
    public static event Action Updated;

    /// <summary>Called by GameWorld right after the physics step.</summary>
    public static void Update(Camera3D camera)
    {
        tick++;
        if (camera == null || GameWorld.physics == null)
        {
            hits.Clear();
        }
        else
        {
            PhysicsWorld.Hits(hits, camera.GlobalPosition, Range, 0);
        }
        Updated?.Invoke();
    }
}
