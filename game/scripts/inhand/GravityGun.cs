using Godot;
using Nerdbank.MessagePack;
using PolyType;
using System.Collections.Generic;
using Fmod = FMOD;
using FmodStudio = FMOD.Studio;

/// <summary>What the other peers' copies of a gravity gun need each tick (<see cref="GravityGun.GenerateSyncState"/>).</summary>
/// <remarks>Sent as an array of values; see the note on <see cref="WorldTickMessage"/> about [Key] numbers.</remarks>
[GenerateShape]
public partial record struct GravityGunSync
{
    /// <summary>The selected mode: an index into <see cref="GravityGun.modes"/>.</summary>
    [Key(0)] public byte mode;
    [Key(1)] public bool holding;
    /// <summary>Where the gun pulls to, and how fast that point moves.</summary>
    [Key(2)] public Vector3 target;
    [Key(3)] public Vector3 targetVelocity;
    /// <summary>The item a single grab holds; 0 in area mode, where every copy catches the items around the target itself.</summary>
    [Key(4)] public ulong itemId;
}

/// <summary>One body a shot pushes, and how hard (an impulse, N·s). Sent to every peer in <see cref="FactoryPlayer._GunPush"/>.</summary>
[GenerateShape]
public partial record struct GunPush
{
    [Key(0)] public ulong id;
    [Key(1)] public Vector3 impulse;
}

[GenerateShapeFor<GunPush[]>]
public partial class GunPushWitness { }

/// <summary>
/// The tool for moving things by hand; every player has one, pinned to the first hotbar slot. Hold right click to hold
/// things, hold left click to charge a shot and let go to fire it. What right click holds and what a shot does with
/// nothing held depend on the mode (<see cref="GravityGunMode"/>), picked from a radial menu while Q is held. Upgrades (<see cref="GravityGunUpgrades"/>) belong to the player.
/// <para>
/// Multiplayer: the gun's authority (the peer driving it) sends the mode, the hold point and (single) the item; it rides
/// along with its player's state. Every peer's copy of the gun holds the same items and pulls the ones its own peer
/// simulates, so pulls from several guns on one item add up: two players can lift what one can't. Items know nothing
/// of this; a held item is an item like any other. A shot is a one-off, so it goes to every peer as an RPC
/// (<see cref="FactoryPlayer._GunPush"/>), and whoever simulates each body pushes it.
/// </para>
/// <para>
/// Authority: the host simulates every item, held or not, so only its copy of each gun does any pulling; a client's own
/// held items trail its gun by about a round trip. To keep that small, a client shows what its gun holds at the host's
/// newest state rather than the usual interpolation delay behind (<see cref="PhysicsWorld.ShowBodyNewest"/>), and the hold
/// point marker moves with the camera every frame.
/// </para>
/// <para>
/// The pull works in accelerations, then scales by the item's mass, so anything can be lifted: a position loop picks a
/// velocity that closes the gap (plus the hold point's own velocity, so held items keep up while you move), a velocity
/// loop turns that into an acceleration (never correcting more than a fraction of the difference per tick, which keeps
/// light items from oscillating), and gravity is cancelled in full. Heavy items get a lower pull speed and acceleration
/// (<see cref="Lightness"/>), so they feel slow and swing wide but always move a little. Spin is damped while held. In
/// area mode every item is pulled to the same point, and their collisions pack them into a ball.
/// </para>
/// </summary>
public partial class GravityGun : HeldItem
{
    /// <summary>Every mode, in radial menu order (clockwise from the top). The synced mode is an index into this.</summary>
    public static readonly GravityGunMode[] modes = [new SingleMode(), new AreaMode()];

    [ExportGroup("Hold distance")]
    /// <summary>How far in front of the camera things are held; a single grab holds its item where it was, within the limits.</summary>
    [Export] public float holdDistance = 3f;
    [Export] public float minHoldDistance = 1f;
    [Export] public float maxHoldDistance = 8f;

    [ExportGroup("Pull")]
    /// <summary>How fast the gap to the hold point closes (1/s).</summary>
    [Export] public float positionGain = 8f;
    /// <summary>How fast an item's velocity follows the wanted velocity (1/s).</summary>
    [Export] public float velocityGain = 20f;
    /// <summary>Fastest an item is pulled toward the hold point (m/s), on top of following it.</summary>
    [Export] public float maxPullSpeed = 12f;
    /// <summary>
    /// Smoothing time (s) for the hold point's velocity. Mouse look and movement move the point between physics
    /// ticks, in uneven steps; unsmoothed, those steps would jolt held items.
    /// </summary>
    [Export] public float followSmoothing = 0.06f;
    /// <summary>
    /// Hardest a light item is accelerated (m/s²), apart from holding it up. Heavier items get less of this and a lower
    /// <see cref="maxPullSpeed"/>, by their <see cref="Lightness"/>.
    /// </summary>
    [Export] public float maxAcceleration = 150f;
    /// <summary>An item this heavy (kg) moves at half the speed and acceleration of a light one; twice as heavy, a third.</summary>
    [Export] public float heavyMass = 50f;
    /// <summary>However heavy an item is, it still gets at least this fraction of the speed and acceleration.</summary>
    [Export] public float minLightness = 0.05f;
    /// <summary>How fast a held item's spin is damped (1/s).</summary>
    [Export] public float spinDamping = 6f;

    [ExportGroup("Shot")]
    /// <summary>
    /// The push (N·s) of a tap, and of a full charge. The same for any mass, so heavy things go less far: a full charge
    /// sends a scrap piece (25 kg) off at about 360 m/s and a scrap ball (89 kg) at about 100 m/s.
    /// </summary>
    [Export] public float minImpulse = 1500f;
    [Export] public float maxImpulse = 9000f;
    /// <summary>Seconds of holding left click to charge fully.</summary>
    [Export] public float chargeTime = 1f;
    /// <summary>A player pushed by a shot moves as if this heavy (kg): a full charge shoves them at about 15 m/s.</summary>
    [Export] public float playerKnockbackMass = 600f;

    /// <summary>Shown only while the gun is pulling (the coil's glow); hidden otherwise.</summary>
    [Export] public Node3D activeIndicator;

    // On every copy: set by input on the holder's peer, by its synced state on the others.
    public int modeIndex;
    public bool holding;
    public Vector3 target;
    Vector3 targetVelocity;
    /// <summary>The items held (ids); once caught, an item stays until the gun lets go.</summary>
    public readonly List<ulong> caught = new();
    /// <summary>Area mode: seconds until the next search for items to catch (each copy searches for itself).</summary>
    public double untilRescan;

    // The holder's peer only.
    Vector3 lastTarget;
    /// <summary>Single mode: how long the item has been too far from the hold point.</summary>
    public float strainedFor;
    /// <summary>Right click was used up by a launch: nothing is caught again until it's pressed anew.</summary>
    bool grabSpent;
    bool charging;
    /// <summary>0 to 1: how far the shot is charged.</summary>
    float charge;
    MeshInstance3D ballMarker;
    GpuParticles3D particles;
    GravityGunRadial radial;

    /// <summary>The coil's glow, this copy's own (the scene's is shared by every copy), tinted by the mode.</summary>
    StandardMaterial3D glow;

    FmodStudio.EventInstance hold_audio;
    [Export] AnimationPlayer animationPlayer;

    static readonly long[] none = new long[0];

    static readonly Vector3 gravity =ProjectSettings.GetSetting("physics/3d/default_gravity_vector").AsVector3()
        * ProjectSettings.GetSetting("physics/3d/default_gravity").AsSingle();

    public GravityGunMode mode => modes[modeIndex];

    public override void Equip(ItemInfo item, FactoryPlayer player)
    {
        base.Equip(item, player);
        MeshInstance3D body = GetNode<MeshInstance3D>("gravgun/Body");
        glow = (StandardMaterial3D)body.Mesh.SurfaceGetMaterial(0).Duplicate();
        body.SetSurfaceOverrideMaterial(0, glow);
        if (!isLocal) return;
        modeIndex = player.gunMode;
        ballMarker = GetNode<MeshInstance3D>("BallMarker");
        particles = GetNode<GpuParticles3D>("BallParticles");
        ballMarker.TopLevel = true;
        ballMarker.Visible = false;
        particles.Emitting = false;
        // Above the HUD.
        CanvasLayer layer = new CanvasLayer();
        layer.Layer = 50;
        AddChild(layer);
        radial = new GravityGunRadial();
        radial.gun = this;
        layer.AddChild(radial);
    }

    /// <summary>Put away (or its player gone): whatever it held touches the player again at once.</summary>
    public override void _ExitTree()
    {
        if (IsInstanceValid(player))
        {
            player.ForgetIgnoredBodies();
        }
        hold_audio.stop(FmodStudio.STOP_MODE.ALLOWFADEOUT);
    }

    // ---- input (holder's peer) ----------------------------------------------------

    public override bool HandleInput(InputEvent @event)
    {
        if (@event.IsActionPressed(InputActions.drop))
        {
            radial.Open();
            return true;
        }
        if (@event.IsActionReleased(InputActions.drop))
        {
            if (radial.isOpen)
            {
                int picked = radial.Close();
                if (picked >= 0 && modes[picked].IsUnlocked(player))
                {
                    SelectMode(picked);
                }
            }
            return true;
        }
        if (radial.isOpen)
        {
            // Nothing else while picking a mode (mouse look is frozen too, see FactoryPlayer.HandleMouseInput).
            return @event.IsAction(InputActions.primary) || @event.IsAction(InputActions.secondary);
        }
        if (@event.IsActionPressed(InputActions.secondary))
        {
            if (!holding && !grabSpent && mode.StartGrab(this))
            {
                StartHolding();
            }
            return true;
        }
        if (@event.IsActionReleased(InputActions.secondary))
        {
            grabSpent = false;
            return true;
        }
        if (@event.IsActionPressed(InputActions.primary))
        {
            charging = true;
            charge = 0;
            return true;
        }
        // Letting go of either button is noticed in _PhysicsProcess, which also covers losing focus mid-hold.
        return @event.IsActionReleased(InputActions.primary);
    }

    /// <summary>Holder's peer: the radial menu wants the mouse while it's open.</summary>
    public bool CapturesMouse()
    {
        return radial != null && radial.isOpen;
    }

    /// <summary>Holder's peer: mouse movement while the radial menu is open.</summary>
    public void RadialMouse(Vector2 screenRelative)
    {
        radial.Move(screenRelative);
    }

    /// <summary>Holder's peer: switches mode (letting go of anything held).</summary>
    public void SelectMode(int index)
    {
        if (index == modeIndex) return;
        StopHolding();
        modeIndex = index;
        // Kept on the player, so putting the gun away and back keeps the mode.
        player.gunMode = index;
    }

    void StartHolding()
    {
        holding = true;
        target = HoldPoint();
        lastTarget = target;
        targetVelocity = Vector3.Zero;
        strainedFor = 0;
        untilRescan = 0;
        StartHoldEffects();
    }

    void StopHolding()
    {
        holding = false;
        caught.Clear();
    }

    void StartHoldEffects()
    {
        //audio
        hold_audio = (FmodStudio.EventInstance)AudioManager.Instance.CreateInstance("event:/Items/GravGun_Hold");
        hold_audio.setVolume(1.0f);
        hold_audio.start();

        //animation
        animationPlayer.Play("hold");
    }

    public Vector3 HoldPoint()
    {
        Camera3D camera = player.camera;
        return camera.GlobalPosition - camera.GlobalTransform.Basis.Z * holdDistance;
    }

    // ---- shots (holder's peer) ----------------------------------------------------

    /// <summary>How much the player's upgrades multiply every shot's push by.</summary>
    public float ForceScale()
    {
        float scale = 1f;
        if (player.HasGunUpgrade(GravityGunUpgrades.DoubleForce))
        {
            scale *= 2f;
        }
        return scale;
    }

    /// <summary>Holder's peer, for testing (the console's gun_fire): fires at once with this much charge (0 to 1). Returns how many bodies it pushed.</summary>
    public int FireCharged(float amount)
    {
        charge = Mathf.Clamp(amount, 0f, 1f);
        return Fire();
    }

    /// <summary>Left click let go: launches what's held, or else the mode's own shot. Returns how many bodies it pushed.</summary>
    int Fire()
    {
        float impulse = Mathf.Lerp(minImpulse, maxImpulse, charge) * ForceScale();
        Vector3 aim = -player.camera.GlobalTransform.Basis.Z;
        List<GunPush> pushes = new();
        if (holding && caught.Count > 0)
        {
            foreach (ulong item in caught)
            {
                pushes.Add(new GunPush { id = item, impulse = aim * impulse });
            }
            StopHolding();
            // Still holding right click would catch them straight back.
            grabSpent = Input.IsActionPressed(InputActions.secondary);
        }
        else
        {
            mode.FireEmpty(this, impulse, charge, pushes);
        }
        if (pushes.Count > 0)
        {
            RPCManager.RPC(player.id, nameof(FactoryPlayer._GunPush), [pushes.ToArray()]);
        }
        return pushes.Count;
    }

    /// <summary>
    /// Every peer: a shot's pushes. Whoever simulates each body pushes it (the host, for loose items); a player is pushed
    /// by its own peer, and never the shooter.
    /// </summary>
    public static void ApplyPushes(FactoryPlayer shooter, GunPush[] pushes)
    {
        float tick = 1f / Engine.PhysicsTicksPerSecond;
        foreach (GunPush push in pushes)
        {
            if (PhysicsWorld.BodyNode(push.id) is FactoryPlayer pushed)
            {
                if (pushed.isLocal && pushed != shooter)
                {
                    float mass = 600f;
                    if (shooter.heldItem is GravityGun gun)
                    {
                        mass = gun.playerKnockbackMass;
                    }
                    pushed.Knockback(push.impulse / mass);
                }
                continue;
            }
            // A force for one step is an impulse; it does nothing where the body isn't simulated.
            PhysicsWorld.ApplyBodyForce(push.id, push.impulse / tick);
            // Sent often for a moment, so everyone sees it fly off at once.
            PhysicsWorld.HoldBody(push.id);
        }
    }

    // ---- sync ---------------------------------------------------------------------

    public override byte[] GenerateSyncState()
    {
        GravityGunSync sync = new() { mode = (byte)modeIndex, holding = holding, target = target, targetVelocity = targetVelocity };
        if (holding && mode.HoldsOne() && caught.Count > 0)
        {
            sync.itemId = caught[0];
        }
        return GMPObject.serializer.Serialize(sync);
    }

    public override void ApplySyncState(byte[] state)
    {
        if (state == null || state.Length == 0) return;
        GravityGunSync sync = GMPObject.serializer.Deserialize<GravityGunSync>(state);
        if (sync.mode < modes.Length && sync.mode != modeIndex)
        {
            modeIndex = sync.mode;
            caught.Clear();
        }
        if (sync.holding != holding)
        {
            holding = sync.holding;
            caught.Clear();
            // Catch at once, as the holder's copy does (StartHolding): a quick tap may last only a few ticks here.
            untilRescan = 0;
            if (holding) StartHoldEffects();
        }
        target = sync.target;
        targetVelocity = sync.targetVelocity;
        if (holding && mode.HoldsOne())
        {
            if (caught.Count != 1 || caught[0] != sync.itemId)
            {
                caught.Clear();
                if (ItemWorld.items.ContainsKey(sync.itemId)) caught.Add(sync.itemId);
            }
        }
    }

    // ---- every tick, every copy ---------------------------------------------------

    public override void _PhysicsProcess(double delta)
    {
        caught.RemoveAll(IsGone);
        if (isLocal)
        {
            UpdateHolder(delta);
        }
        if (activeIndicator != null) activeIndicator.Visible = holding;
        if (!holding)
        {
            caught.Clear();
            // Still every tick, so what it let go of starts touching the player again after a moment.
            player.IgnoreBodies(none);
            hold_audio.stop(FmodStudio.STOP_MODE.ALLOWFADEOUT);
            if(animationPlayer.AssignedAnimation != "idle")
            {
                animationPlayer.Stop();
                animationPlayer.Play("idle");
            }
            return;
        }
        else
        {
            //audio position update
            hold_audio.set3DAttributes(AudioManager.To3D(GlobalPosition));
        }
        mode.UpdateCatch(this, delta);
        // What the gun holds passes through its own player (not the others), so it can't be stood on or shoved by them.
        long[] held = new long[caught.Count];
        for (int i = 0; i < caught.Count; i++)
        {
            held[i] = (long)caught[i];
        }
        player.IgnoreBodies(held);
        foreach (ulong item in caught)
        {
            // Held items sync ahead of everything but the players (counts on the item's authority, the host).
            PhysicsWorld.HoldBody(item);
            if (isLocal)
            {
                // Show what this player holds as close to now as the host's states allow (nothing on the host).
                PhysicsWorld.ShowBodyNewest(item);
            }
            Pull(item, (float)delta);
        }
    }

    /// <summary>
    /// The coil's colour (every copy), and on the holder's peer the hold point marker, every frame rather than every
    /// physics tick, so it moves with the camera at once even though held items (simulated by the host) trail behind it.
    /// </summary>
    public override void _Process(double delta)
    {
        float brightness = 1f;
        if (holding)
        {
            brightness = 1.6f;
        }
        glow.AlbedoColor = mode.color * brightness;
        if (!isLocal) return;
        // While charging with nothing held, the marker shows the charge instead.
        bool showMarker = holding || charging;
        ballMarker.Visible = showMarker;
        particles.Emitting = holding;
        if (!showMarker) return;
        ballMarker.GlobalPosition = HoldPoint();
        float size = 1f;
        if (charging)
        {
            size = 0.5f + charge;
        }
        ballMarker.Scale = Vector3.One * size;
        particles.GlobalPosition = ballMarker.GlobalPosition;
    }

    /// <summary>Holder's peer: charges and fires, lets go when right click is up, moves the hold point, and asks the mode whether to keep holding.</summary>
    void UpdateHolder(double delta)
    {
        float dt = (float)delta;
        if (charging)
        {
            charge = Mathf.Min(1f, charge + dt / chargeTime);
            if (!Input.IsActionPressed(InputActions.primary))
            {
                charging = false;
                Fire();
            }
        }
        if (holding && !Input.IsActionPressed(InputActions.secondary))
        {
            StopHolding();
        }
        if (holding && mode.HoldsOne() && caught.Count == 0)
        {
            StopHolding(); // the item is gone (picked up, eaten by a machine...)
        }
        if (!holding) return;

        Vector3 point = HoldPoint();
        Vector3 rawVelocity = (point - lastTarget) / dt;
        lastTarget = point;
        targetVelocity = targetVelocity.Lerp(rawVelocity, 1f - Mathf.Exp(-dt / followSmoothing));
        target = point;

        if (!mode.KeepHolding(this, dt))
        {
            StopHolding();
        }
    }

    /// <summary>Tops the catch up to <paramref name="maxItems"/> with the nearest loose items within <paramref name="radius"/> of the hold point.</summary>
    public void CatchNearbyItems(float radius, int maxItems)
    {
        if (caught.Count >= maxItems) return;
        List<(ulong id, float distanceSquared)> inReach = new();
        foreach (long found in PhysicsWorld.OverlapSphere(target, radius, PhysicsWorld.ItemLayer))
        {
            ulong id = (ulong)found;
            ItemRecord item = ItemWorld.Get(id);
            if (item != null && item.canBeGrabbed && !caught.Contains(id))
            {
                inReach.Add((id, PhysicsWorld.BodyPosition(id).DistanceSquaredTo(target)));
            }
        }
        inReach.Sort((a, b) => a.distanceSquared.CompareTo(b.distanceSquared));
        foreach ((ulong id, float _) in inReach)
        {
            if (caught.Count >= maxItems) return;
            caught.Add(id);
        }
    }

    static bool IsGone(ulong item)
    {
        return !ItemWorld.items.ContainsKey(item);
    }

    /// <summary>Drives <paramref name="item"/> toward the hold point, if this peer simulates it (the host; otherwise the host's copy of this gun does).</summary>
    void Pull(ulong item, float dt)
    {
        if (!Lobby.isHost) return;

        float mass = PhysicsWorld.BodyMass(item);
        float lightness = Lightness(mass);

        float pullSpeed = maxPullSpeed * lightness;
        Vector3 pull = (target - PhysicsWorld.BodyPosition(item)) * positionGain;
        if (pull.Length() > pullSpeed) pull = pull.Normalized() * pullSpeed;
        Vector3 wantedVelocity = targetVelocity + pull;

        // Correct at most 90% of the velocity difference per tick, whatever the gain: this keeps the loop stable.
        float response = Mathf.Min(velocityGain, 0.9f / dt);
        Vector3 accel = (wantedVelocity - PhysicsWorld.BodyLinearVelocity(item)) * response;
        float accelLimit = maxAcceleration * lightness;
        if (accel.Length() > accelLimit) accel = accel.Normalized() * accelLimit;
        // Gravity is always cancelled in full, so nothing is too heavy to lift. With several guns on one item each
        // cancels all of it; the velocity loop takes up the extra lift.
        PhysicsWorld.ApplyBodyForce(item, (accel - gravity) * mass);

        // Take a fraction of its spin away.
        PhysicsWorld.DampBodySpin(item, Mathf.Min(1f, spinDamping * dt));
    }

    /// <summary>1 for a light item, falling toward <see cref="minLightness"/> as it gets heavier (0.5 at <see cref="heavyMass"/>).</summary>
    public float Lightness(float mass)
    {
        return Mathf.Max(minLightness, 1f / (1f + mass / heavyMass));
    }
}
