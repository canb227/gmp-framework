using Godot;
using System;
using System.Collections.Generic;

public enum BodyTypeEnum
{
    Static = 0,
    Kinematic = 1,
    Dynamic = 2
}


/// <summary>
/// GMPObject base for physics bodies that are nodes: structures, tools, buttons and the like (loose items are not
/// nodes, see <see cref="PhysicsWorld"/>). Attach a script deriving from this to a <c>GMPBody</c> node (our C++ body,
/// native/src/physics_body.h; C# sees it as a Node3D), with <c>GMPShape</c> nodes under it for its colliders.
/// <para>
/// GMPBody's methods are called by name (C# can't see the C++ class); each call costs about a microsecond.
/// </para>
/// </summary>
public partial class GMPOBody : Node3D, GMPObject
{
    // Godot only reads [Export] on the node class, so each GMPO base repeats this block.
    [ExportGroup("Configuration")]
    [Export]
    public int priority { get; set; }
    [Export]
    public bool pauseable { get; set; } = true;

    [ExportGroup("Hover")]
    /// <summary>Name shown under the crosshair while a player looks at this body; empty shows none.</summary>
    [Export]
    public string hoverName { get; set; }
    /// <summary>Line shown below <see cref="hoverName"/>, e.g. "Press F to pick up!"; empty shows none.</summary>
    [Export]
    public string hoverText { get; set; }

    [ExportGroup("READONLY")]
    [Export]
    public ulong id { get; set; }
    [Export]
    public ulong authority { get; set; }
    [Export]
    public ulong owner { get; set; }
    [Export]
    public int priorityAccumulator { get; set; }
    public byte[] desiredState { get; set; }

    /// <summary>The body type set in the scene; restored whenever this peer becomes the authority.</summary>
    public BodyTypeEnum authoredBodyType { get; private set; }

    public virtual void AfterInit()
    {
        authoredBodyType = bodyType;
        // A spawn-time state (a restored save, a late join) puts the body exactly where it was, on every peer.
        if (desiredState != null && desiredState.Length > 0)
        {
            BodySyncState s = BodySyncState.Unpack(desiredState);
            Teleport(new Transform3D(new Basis(s.rot), s.pos));
        }
        if (authority != Lobby.selfPeerID)
        {
            bodyType = BodyTypeEnum.Kinematic;
        }
    }

    /// <summary>
    /// The authority simulates with the authored body type; everyone else follows as Kinematic. A new authority carries
    /// on at the velocity it was following the body at. A new follower holds still until the new authority's states
    /// arrive, then eases onto them.
    /// </summary>
    public virtual void OnAuthorityChanged()
    {
        desiredState = null;
        // A new authority's followers need its resting pose again, even if this peer sent it once before.
        lastBuiltResting = false;
        // Their ticks were counted by the old authority's clock.
        snapshots?.Clear();
        if (authority == Lobby.selfPeerID)
        {
            bodyType = authoredBodyType;
            if (authoredBodyType == BodyTypeEnum.Dynamic)
            {
                // A follower is only ever placed, so the body has no velocity of its own: give it the followed one.
                SetLinearVelocity(followVelocity);
                SetAngularVelocity(followAngularVelocity);
            }
        }
        else
        {
            // Kinematic: the body is driven to wherever the follower puts the node.
            bodyType = BodyTypeEnum.Kinematic;
        }
        // Dropped from the followers list at the next playback (see PlayFollowers).
        following = false;
    }

    public virtual void OnDespawned(DespawnReason reason)
    {
    }

    // ---- following ----
    // A non-authority copy plays the authority's states back from a short buffer instead of chasing the newest one:
    // it shows the authority's tick GameWorld.PlaybackTick, about GameWorld.InterpolationDelay ticks behind the newest
    // state heard, interpolating between the states either side of it. That smooths over packets arriving unevenly.
    // Past the newest state (packets late) it carries that one on at its velocity, briefly.
    //
    // Playback runs from one loop (PlayFollowers, called by GameWorld each physics tick) rather than each body's own
    // _PhysicsProcess: Godot calling into C# costs a few microseconds per node per tick, and followers cost about 6 µs
    // each per tick that way (14 ms ticks at 2300 followers, measured). The base class has no _PhysicsProcess, so
    // bodies only get physics processing if a subclass adds its own.

    /// <summary>Every body following right now, played back by <see cref="PlayFollowers"/>.</summary>
    static readonly List<GMPOBody> followers = new();

    /// <summary>Whether this body is in <see cref="followers"/>: it can stop and start following again before the list drops it.</summary>
    bool inFollowers;

    /// <summary>The body's scale when following started, kept while placing it (followers are placed by whole transform).</summary>
    Vector3 followScale = Vector3.One;

    /// <summary>One received state, by the authority's physics tick.</summary>
    struct Snapshot
    {
        public ulong tick;
        public Vector3 pos;
        public Quaternion rot;
        public Vector3 vel;
        /// <summary>World space (rad/s about each axis).</summary>
        public Vector3 angVel;
    }

    /// <summary>Received states, oldest first: the newest one at or before playback, then the ones after it.</summary>
    List<Snapshot> snapshots;

    /// <summary>States kept at most (half a second at 30 Hz).</summary>
    const int MaxSnapshots = 16;

    /// <summary>True while this non-authority copy is being placed from <see cref="snapshots"/>.</summary>
    bool following;

    /// <summary>The velocities at the last playback, for carrying on with should this peer become the authority.</summary>
    Vector3 followVelocity;
    Vector3 followAngularVelocity;

    /// <summary>
    /// How long a follower keeps carrying the newest state on at its velocity once playback has passed it. Long enough
    /// to cover late packets, short enough that a body whose updates stop doesn't drift off.
    /// </summary>
    const float MaxExtrapolation = 0.25f;

    /// <summary>
    /// Where the body is shown relative to the played-back pose: taken when following starts, from wherever the body
    /// was (e.g. simulated here as the authority before a hand-off), then faded out, so the body slides onto the
    /// authority's pose instead of jumping.
    /// </summary>
    Vector3 errorPos;
    Quaternion errorRot = Quaternion.Identity;
    /// <summary>Take <see cref="errorPos"/>/<see cref="errorRot"/> from the body's pose at the next playback.</summary>
    bool blendIn;
    /// <summary>The fade is done: <see cref="errorPos"/> and <see cref="errorRot"/> are zero.</summary>
    bool blended = true;

    /// <summary>Time constant (s) of the fade: most of the gap is gone in about three times this.</summary>
    const float ErrorBlendTime = 0.1f;

    /// <summary>
    /// Within these the fade counts as done: position in metres, rotation as 1 - |w| of the quaternion between the two
    /// poses (1e-6 is about 0.16°). Not an angle: acos near 1 is too noisy in float to compare against a small threshold.
    /// </summary>
    const float ArrivedDistance = 0.0005f, ArrivedRotation = 1e-6f;

    /// <summary>
    /// Ticks this follower is shown ahead of its authority's playback clock: 0, or up to
    /// <see cref="GameWorld.InterpolationDelay"/> (the newest state heard) while <see cref="ShowNewest"/> is called.
    /// Eased, so playback never jumps.
    /// </summary>
    double playbackLead;
    /// <summary>The physics frame <see cref="ShowNewest"/> was last called on.</summary>
    ulong showNewestFrame;
    /// <summary>
    /// How fast <see cref="playbackLead"/> eases, in ticks per second: the whole delay in about half a second, during
    /// which the body plays back a little fast or slow.
    /// </summary>
    static readonly double PlaybackLeadRate = GameWorld.InterpolationDelay * 2;

    /// <summary>
    /// Running totals for the perf window (PerfDebug): physics ticks of following by all bodies, and the Stopwatch ticks
    /// spent in <see cref="PlayFollowers"/>.
    /// </summary>
    public static long followTicks, followCost;
    /// <summary>Stopwatch ticks spent in <see cref="UpdateInterpolation"/>, for the perf log.</summary>
    public static long interpolationCost;
    /// <summary>States received for this body as a follower (counted when added to playback), for followstats.</summary>
    public int statesReceived;
    /// <summary>The bodies following now (read only), for followstats.</summary>
    public static IReadOnlyList<GMPOBody> Followers => followers;

    // ---- physics interpolation only where it shows ----
    // With physics interpolation on, every rendered frame Godot works out and draws an in-between transform for each
    // body that moved this tick: about 1.3 µs a body a frame, so 3000 moving bodies took 3.8 ms of every frame at 144 fps
    // (measured). Only bodies near the camera and in its view are interpolated; the rest are drawn where the physics puts
    // them, in even 60 Hz steps, which hardly shows at a distance and not at all off screen. Every body is still moved
    // every tick: one moved only every other tick would freeze for a tick and then jump, which shows as a stutter.

    /// <summary>Bodies within this many metres of the camera, and in its view, are interpolated.</summary>
    const float InterpolateDistance = 25;
    /// <summary>How far outside the camera's view (m) a body still counts as in it: about a body's size.</summary>
    const float ViewMargin = 2;
    /// <summary>Bodies checked per tick: a pass over 5000 takes 10 ticks, so one coming into view is smooth within 0.2 s.</summary>
    const int InterpolationChecksPerTick = 500;

    /// <summary>Whether this body is interpolated now (it is when spawned, as everything is by default).</summary>
    bool interpolated = true;

    static readonly List<GMPOBody> interpolationChecks = new();
    static int nextInterpolationCheck;
    static int interpolatedThisPass;
    /// <summary>Bodies interpolated as of the last full pass, for the perf log.</summary>
    public static int interpolatedCount { get; private set; }
    static readonly Plane[] viewPlanes = new Plane[6];

    /// <summary>
    /// Each physics tick (from <see cref="GameWorld"/>), on every peer: turns physics interpolation on for some bodies
    /// near the camera and in view and off for the rest, a few hundred a tick. <paramref name="camera"/> is the camera the
    /// world is drawn from; without one nothing changes.
    /// </summary>
    public static void UpdateInterpolation(Camera3D camera, ICollection<GMPObject> objects)
    {
        if (camera == null || !IsInstanceValid(camera))
        {
            return;
        }
        long costStart = System.Diagnostics.Stopwatch.GetTimestamp();
        if (nextInterpolationCheck >= interpolationChecks.Count)
        {
            interpolatedCount = interpolatedThisPass;
            interpolatedThisPass = 0;
            nextInterpolationCheck = 0;
            interpolationChecks.Clear();
            foreach (GMPObject obj in objects)
            {
                if (obj is GMPOBody body)
                {
                    interpolationChecks.Add(body);
                }
            }
            if (interpolationChecks.Count == 0)
            {
                interpolationCost += System.Diagnostics.Stopwatch.GetTimestamp() - costStart;
                return;
            }
        }
        Vector3 eye = camera.GlobalPosition;
        Godot.Collections.Array<Plane> planes = camera.GetFrustum();
        for (int i = 0; i < viewPlanes.Length && i < planes.Count; i++)
        {
            viewPlanes[i] = planes[i];
        }
        int end = Math.Min(nextInterpolationCheck + InterpolationChecksPerTick, interpolationChecks.Count);
        for (; nextInterpolationCheck < end; nextInterpolationCheck++)
        {
            GMPOBody body = interpolationChecks[nextInterpolationCheck];
            if (!IsInstanceValid(body) || body.IsQueuedForDeletion())
            {
                continue;
            }
            // Bodies sit under the world root at the origin, so their Transform is their world transform (one cheap read).
            Vector3 at = body.Transform.Origin;
            bool near = at.DistanceSquaredTo(eye) <= InterpolateDistance * InterpolateDistance;
            for (int i = 0; i < viewPlanes.Length && near; i++)
            {
                if (viewPlanes[i].DistanceTo(at) > ViewMargin)
                {
                    near = false;
                }
            }
            if (near)
            {
                interpolatedThisPass++;
            }
            if (near == body.interpolated)
            {
                continue;
            }
            body.interpolated = near;
            if (near)
            {
                body.PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Inherit;
                // Start from where it is now, not from wherever it was when interpolation went off.
                body.ResetPhysicsInterpolation();
            }
            else
            {
                body.PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off;
            }
        }
        interpolationCost += System.Diagnostics.Stopwatch.GetTimestamp() - costStart;
    }

    // ---- sending ----
    // Building a state costs about 1 µs per call into Box3D (is_awake, each velocity) but only about 0.05 µs to read the
    // node's transform (measured, bench_state). So the transform is read once, and a subclass that already knows
    // whether the body is asleep says so (IsResting) instead of asking Box3D.
    //
    // A body at rest stops sending. Once its resting pose has gone out RestingSends times it has nothing new to say
    // until it moves again; each send checks that by comparing its transform with the last one built (that cheap read
    // again), so a body knocked awake goes out with the very next send. RestingHeartbeatFrames resends the pose now and
    // then in case every copy of it was lost. Followers stop playing a body back once they've placed it at rest.

    /// <summary>Times a resting pose is sent before the body goes quiet (each is unreliable).</summary>
    const int RestingSends = 3;
    /// <summary>A quiet resting body still sends its pose this often: 10 s.</summary>
    static readonly ulong RestingHeartbeatFrames = (ulong)(10 * Engine.PhysicsTicksPerSecond);

    /// <summary>The transform (and its rotation), and whether the body was at rest, as of the last state built.</summary>
    Transform3D lastBuiltTransform;
    Quaternion lastBuiltRotation;
    bool lastBuiltResting;
    /// <summary>Resting sends still to go before <see cref="AtRestAndSent"/>.</summary>
    int restingSendsLeft;
    /// <summary>The send (<see cref="GameWorld.sendFrame"/>) the last state was built for.</summary>
    ulong lastBuiltFrame;

    /// <summary>
    /// Whether the body is at rest now (asleep in Box3D): its state then carries no velocity, so followers don't drift
    /// on whatever velocity it went to sleep with.
    /// </summary>
    protected virtual bool IsResting()
    {
        return !IsAwake();
    }

    /// <summary>
    /// For <see cref="UpdateSyncPriority"/>: true while this body is at rest, its resting pose has gone out
    /// <see cref="RestingSends"/> times, and it hasn't moved since, so a send would only repeat it. Give it priority -1
    /// then (never sends) to skip building it at all.
    /// </summary>
    protected bool AtRestAndSent()
    {
        if (!lastBuiltResting || restingSendsLeft > 0)
        {
            return false;
        }
        if (GameWorld.sendFrame - lastBuiltFrame >= RestingHeartbeatFrames)
        {
            return false;
        }
        return Transform == lastBuiltTransform;
    }

    /// <summary>
    /// Velocities are worked out from the last state built when it's at most this many physics ticks old (two sends);
    /// older than that the average would be too smooth, so Box3D is asked.
    /// </summary>
    static readonly ulong DerivedVelocityTicks = (ulong)(2 * GameWorld.SendInterval);

    public virtual byte[] GenerateStateUpdate()
    {
        Transform3D transform = Transform;
        // Worked out in C# from the transform already read: no sin or cos, and no call into the engine (Rotation, 0.2 µs).
        Quaternion rotation = transform.Basis.GetRotationQuaternion();
        bool resting = IsResting();
        Vector3 velocity = Vector3.Zero;
        Vector3 angularVelocity = Vector3.Zero;
        ulong ticksSinceBuilt = GameWorld.sendFrame - lastBuiltFrame;
        if (!resting && lastBuiltFrame != 0 && ticksSinceBuilt > 0 && ticksSinceBuilt <= DerivedVelocityTicks)
        {
            // Sent recently (the usual case when the budget isn't tight): how far it moved since, over the time.
            // About ten times cheaper than asking Box3D for both velocities (1 µs per call, measured).
            float seconds = (float)ticksSinceBuilt / Engine.PhysicsTicksPerSecond;
            velocity = (transform.Origin - lastBuiltTransform.Origin) / seconds;
            Quaternion turn = rotation * lastBuiltRotation.Inverse();
            if (turn.W < 0)
            {
                turn = -turn;
            }
            Vector3 axis = new Vector3(turn.X, turn.Y, turn.Z);
            float sinHalf = axis.Length();
            if (sinHalf > 1e-6f)
            {
                angularVelocity = axis / sinHalf * (2 * Mathf.Atan2(sinHalf, turn.W) / seconds);
            }
        }
        else if (!resting)
        {
            velocity = GetLinearVelocity();
            angularVelocity = GetAngularVelocity();
        }
        // Counted as built, which is nearly always sent: only the one state that doesn't fit a send isn't.
        if (resting && lastBuiltResting && transform == lastBuiltTransform)
        {
            restingSendsLeft = Math.Max(0, restingSendsLeft - 1);
        }
        else if (resting)
        {
            restingSendsLeft = RestingSends - 1;
        }
        lastBuiltTransform = transform;
        lastBuiltRotation = rotation;
        lastBuiltResting = resting;
        lastBuiltFrame = GameWorld.sendFrame;
        return new BodySyncState { pos = transform.Origin, rot = rotation, vel = velocity, angVel = angularVelocity }.Pack();
    }

    public virtual void UpdateSyncPriority()
    {
    }

    public virtual byte[] SaveState()
    {
        return null;
    }

    public virtual void LoadState(byte[] state)
    {
    }

    public virtual void ApplyStateUpdate(byte[] update)
    {
        // Resting objects keep being re-sent unchanged; a copy that has already arrived there has nothing to do.
        bool unchanged = update != null && desiredState != null && update.AsSpan().SequenceEqual(desiredState);
        desiredState = update;
        // stateTick 0 is a spawn-time state, which AfterInit puts the body on.
        if (update == null || update.Length == 0 || GameWorld.stateTick == 0 || authority == Lobby.selfPeerID)
        {
            return;
        }
        if (unchanged && !following)
        {
            return;
        }
        if (snapshots == null)
        {
            snapshots = new List<Snapshot>();
        }
        if (snapshots.Count > 0 && snapshots[snapshots.Count - 1].tick >= GameWorld.stateTick)
        {
            return;
        }
        BodySyncState s = BodySyncState.Unpack(update);
        snapshots.Add(new Snapshot { tick = GameWorld.stateTick, pos = s.pos, rot = s.rot, vel = s.vel, angVel = s.angVel });
        statesReceived++;
        if (snapshots.Count > MaxSnapshots)
        {
            snapshots.RemoveAt(0);
        }
        if (!following)
        {
            StartFollowing();
        }
    }

    void StartFollowing()
    {
        following = true;
        blendIn = true;
        if (!inFollowers)
        {
            inFollowers = true;
            followers.Add(this);
        }
    }

    /// <summary>
    /// Shows this follower at its authority's newest state instead of <see cref="GameWorld.InterpolationDelay"/> ticks
    /// behind, for as long as this keeps being called each tick: less lag, for more guessing ahead when packets are
    /// late. Eased in and out (see <see cref="playbackLead"/>). E.g. what the local player's magnet rod holds.
    /// </summary>
    public void ShowNewest()
    {
        showNewestFrame = Engine.GetPhysicsFrames();
    }

    /// <summary>
    /// Each physics tick (from <see cref="GameWorld"/>, after the received states are applied): places every follower
    /// (far and unseen ones every other tick), and drops the ones that have stopped following or are gone.
    /// <paramref name="camera"/> is the camera the world is drawn from, if any.
    /// </summary>
    public static void PlayFollowers(double delta)
    {
        long start = System.Diagnostics.Stopwatch.GetTimestamp();
        followTicks += followers.Count;
        ulong frame = Engine.GetPhysicsFrames();
        // Backwards, so a dropped body can be swapped for the last one, which has already been played.
        for (int i = followers.Count - 1; i >= 0; i--)
        {
            GMPOBody body = followers[i];
            bool stillFollowing = false;
            if (IsInstanceValid(body) && !body.IsQueuedForDeletion())
            {
                stillFollowing = body.Follow(frame, delta);
            }
            if (!stillFollowing)
            {
                if (IsInstanceValid(body))
                {
                    body.inFollowers = false;
                }
                followers[i] = followers[followers.Count - 1];
                followers.RemoveAt(followers.Count - 1);
            }
        }
        followCost += System.Diagnostics.Stopwatch.GetTimestamp() - start;
    }

    /// <summary>Clears the followers list, with the rest of the world (<see cref="GameWorld.ClearWorld"/>).</summary>
    public static void ClearFollowers()
    {
        followers.Clear();
    }

    /// <summary>Places this follower for physics frame <paramref name="frame"/>; false once it has stopped following.</summary>
    bool Follow(ulong frame, double delta)
    {
        if (!following || snapshots == null || snapshots.Count == 0)
        {
            following = false;
            return false;
        }

        // Asked for this tick or the last: bodies and whatever asks run in no fixed order within a tick.
        bool showingNewest = showNewestFrame + 1 >= frame;

        double elapsed = delta;

        double wantedLead = 0;
        if (showingNewest)
        {
            wantedLead = GameWorld.InterpolationDelay;
        }
        playbackLead = Mathf.MoveToward(playbackLead, wantedLead, PlaybackLeadRate * elapsed);

        bool done = PlayBack(GameWorld.PlaybackTick(authority) + playbackLead, out Vector3 pos, out Quaternion rot);
        if (blendIn)
        {
            blendIn = false;
            Transform3D current = Transform;
            errorPos = current.Origin - pos;
            errorRot = current.Basis.GetRotationQuaternion() * rot.Inverse();
            followScale = current.Basis.Scale;
            blended = false;
        }
        else if (!blended)
        {
            float keep = Mathf.Exp(-(float)elapsed / ErrorBlendTime);
            errorPos *= keep;
            errorRot = Quaternion.Identity.Slerp(errorRot, keep);
        }
        if (!blended && errorPos.Length() < ArrivedDistance && 1f - Mathf.Abs(errorRot.W) < ArrivedRotation)
        {
            blended = true;
            errorPos = Vector3.Zero;
            errorRot = Quaternion.Identity;
        }
        // The poses are world space, set as the parent-relative transform: this assumes the body sits under the world
        // root (as every spawned object does), at the world origin. Set whole, in one call into the engine.
        // Once blended in at scale 1 (nearly always) the rotation is used as it is: the error and scale maths cost a
        // quarter of a follower's time (measured).
        Basis basis;
        if (blended && followScale.IsEqualApprox(Vector3.One))
        {
            basis = new Basis(rot);
        }
        else
        {
            basis = new Basis((errorRot * rot).Normalized()).ScaledLocal(followScale);
        }
        Transform = new Transform3D(basis, pos + errorPos);
        if (done && blended)
        {
            following = false;
        }
        return following;
    }

    /// <summary>
    /// The authority's pose at <paramref name="playbackTick"/>, from <see cref="snapshots"/>; also sets
    /// <see cref="followVelocity"/>. True once there's nothing left to play: the newest state is at rest, or has been
    /// carried on for <see cref="MaxExtrapolation"/>.
    /// </summary>
    bool PlayBack(double playbackTick, out Vector3 pos, out Quaternion rot)
    {
        // Drop the states playback has moved past, keeping the newest one at or before it.
        while (snapshots.Count >= 2 && snapshots[1].tick <= playbackTick)
        {
            snapshots.RemoveAt(0);
        }
        Snapshot from = snapshots[0];
        if (playbackTick < from.tick)
        {
            // Every state is still ahead of playback (it has only just started): wait at the first.
            pos = from.pos;
            rot = from.rot;
            followVelocity = from.vel;
            followAngularVelocity = from.angVel;
            return false;
        }
        if (snapshots.Count >= 2)
        {
            Snapshot to = snapshots[1];
            float t = (float)((playbackTick - from.tick) / (to.tick - from.tick));
            pos = from.pos.Lerp(to.pos, t);
            rot = from.rot.Slerp(to.rot, t);
            followVelocity = from.vel.Lerp(to.vel, t);
            followAngularVelocity = from.angVel.Lerp(to.angVel, t);
            return false;
        }

        // Playback has passed the newest state: carry it on at its velocity, for a little while.
        float ahead = (float)((playbackTick - from.tick) / Engine.PhysicsTicksPerSecond);
        float carried = Mathf.Min(ahead, MaxExtrapolation);
        pos = from.pos + from.vel * carried;
        rot = from.rot;
        Vector3 spin = from.angVel * carried;
        float angle = spin.Length();
        // Below this the turn doesn't show, and the axis of so small a spin can't be normalized reliably in float
        // (Quaternion throws on an axis that isn't).
        if (angle > 1e-5f)
        {
            // Angular velocity is world space, so the turn goes on the left (before the body's own rotation).
            rot = new Quaternion(spin.Normalized(), angle) * from.rot;
        }
        bool atRest = from.vel == Vector3.Zero && from.angVel == Vector3.Zero;
        if (atRest || ahead >= MaxExtrapolation)
        {
            followVelocity = Vector3.Zero;
            followAngularVelocity = Vector3.Zero;
            return true;
        }
        followVelocity = from.vel;
        followAngularVelocity = from.angVel;
        return false;
    }

    // ---- GMPBody ----
    // Its properties and methods, called by name with cached names (C# can't see the C++ class). A plain Get/Set would
    // first be matched against every property of this C# script.

    static readonly StringName bodyTypeName = "body_type";
    static readonly StringName enabledName = "enabled";
    static readonly StringName contactMonitorName = "contact_monitor";
    static readonly StringName collisionLayerName = "collision_layer";
    static readonly StringName collisionMaskName = "collision_mask";
    static readonly StringName gravityScaleName = "gravity_scale";
    static readonly StringName teleportName = "teleport";
    static readonly StringName getLinearVelocityName = "get_linear_velocity";
    static readonly StringName setLinearVelocityName = "set_linear_velocity";
    static readonly StringName getAngularVelocityName = "get_angular_velocity";
    static readonly StringName setAngularVelocityName = "set_angular_velocity";
    static readonly StringName applyCentralImpulseName = "apply_central_impulse";
    static readonly StringName applyImpulseName = "apply_impulse";
    static readonly StringName applyCentralForceName = "apply_central_force";
    static readonly StringName applyAngularImpulseName = "apply_angular_impulse";
    static readonly StringName getMassName = "get_mass";
    static readonly StringName isAwakeName = "is_awake";
    static readonly StringName setAwakeName = "set_awake";

    public BodyTypeEnum bodyType
    {
        get => (BodyTypeEnum)ClassDB.ClassGetProperty(this, bodyTypeName).AsInt32();
        set => ClassDB.ClassSetProperty(this, bodyTypeName, (int)value);
    }

    /// <summary>Off: no body in the world at all.</summary>
    public bool enabled
    {
        get => ClassDB.ClassGetProperty(this, enabledName).AsBool();
        set => ClassDB.ClassSetProperty(this, enabledName, value);
    }

    /// <summary>On: the GMPBody signals body_touched / item_touched when something starts touching it.</summary>
    public bool contactMonitor
    {
        get => ClassDB.ClassGetProperty(this, contactMonitorName).AsBool();
        set => ClassDB.ClassSetProperty(this, contactMonitorName, value);
    }

    public long collisionLayer
    {
        get => ClassDB.ClassGetProperty(this, collisionLayerName).AsInt64();
        set => ClassDB.ClassSetProperty(this, collisionLayerName, value);
    }

    public long collisionMask
    {
        get => ClassDB.ClassGetProperty(this, collisionMaskName).AsInt64();
        set => ClassDB.ClassSetProperty(this, collisionMaskName, value);
    }

    public float gravityScale
    {
        get => ClassDB.ClassGetProperty(this, gravityScaleName).AsSingle();
        set => ClassDB.ClassSetProperty(this, gravityScaleName, value);
    }

    /// <summary>Moves the node and its body there at once.</summary>
    public void Teleport(Transform3D transform)
    {
        Call(teleportName, transform);
    }

    public void Teleport(Vector3 position, Vector3 rotation)
    {
        Teleport(new Transform3D(Basis.FromEuler(rotation), position));
    }

    public void Teleport(Vector3 position)
    {
        Teleport(new Transform3D(Basis, position));
    }

    public Vector3 GetLinearVelocity()
    {
        return Call(getLinearVelocityName).AsVector3();
    }

    public void SetLinearVelocity(Vector3 velocity)
    {
        Call(setLinearVelocityName, velocity);
    }

    public Vector3 GetAngularVelocity()
    {
        return Call(getAngularVelocityName).AsVector3();
    }

    public void SetAngularVelocity(Vector3 velocity)
    {
        Call(setAngularVelocityName, velocity);
    }

    public void ApplyCentralImpulse(Vector3 impulse)
    {
        Call(applyCentralImpulseName, impulse);
    }

    public void ApplyImpulse(Vector3 impulse, Vector3 point)
    {
        Call(applyImpulseName, impulse, point);
    }

    public void ApplyCentralForce(Vector3 force)
    {
        Call(applyCentralForceName, force);
    }

    public void ApplyAngularImpulse(Vector3 impulse)
    {
        Call(applyAngularImpulseName, impulse);
    }

    public float GetMass()
    {
        return Call(getMassName).AsSingle();
    }

    public bool IsAwake()
    {
        return Call(isAwakeName).AsBool();
    }

    public void SetAwake(bool awake)
    {
        Call(setAwakeName, awake);
    }
}
