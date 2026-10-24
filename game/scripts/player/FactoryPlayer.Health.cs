using Godot;
using System.Collections.Generic;

/// <summary>
/// FactoryPlayer: health. Low and quick to come back: a few seconds without damage and it refills over a few more, so
/// many small knocks wear a player down while one hard hit downs them at once. A downed player is just their head, a
/// dynamic ball (<see cref="PlayerBall"/>) with the camera orbiting it, until they press jump to stand back up.
/// <para>
/// Each hit is judged once, by the peer that reported it (the one simulating what hit: the host for loose items, the
/// player's own peer for its landings and bumps), and the damage goes reliably to the player's peer, which alone
/// holds its health. Heard hits (other peers' reports) are left out: their sender judges them.
/// </para>
/// </summary>
public partial class FactoryPlayer
{
    const string BallScene = "res://game/scenes/player/PlayerBall.tscn";

    [ExportGroup("Health")]
    [Export] public float maxHealth = 100f;
    /// <summary>Seconds without damage before health starts coming back.</summary>
    [Export] public float regenDelay = 3f;
    /// <summary>Seconds regen takes from empty to full.</summary>
    [Export] public float regenTime = 5f;
    /// <summary>Seconds downed before jump stands the player back up.</summary>
    [Export] public float reviveDelay = 3f;
    /// <summary>How much of a downing hit's speed the ball sets off with.</summary>
    [Export] public float downedPushScale = 0.5f;
    [Export] public float ballCameraDistance = 4f;

    /// <summary>Only on the player's own peer; elsewhere it stays full.</summary>
    public float health = 100f;
    float sinceDamaged = 100;

    /// <summary>The ball this player is while downed, or 0.</summary>
    ulong ballID;
    public bool downed => ballID != 0;
    float downedTime;
    /// <summary>Where the player went down: they stand up there again if the ball is lost (fell out of the world).</summary>
    Vector3 downedAt;
    float orbitYaw;
    float orbitPitch;

    /// <summary>This player's body id, as hits name it.</summary>
    ulong bodyID;

    void InitHealth()
    {
        bodyID = PhysicsWorld.BodyIdOf(this);
        health = maxHealth;
        if (!judgeHooked)
        {
            HitStream.Updated += JudgeHits;
            judgeHooked = true;
        }
    }

    /// <summary>Local player, every physics tick: regen and the HUD bar.</summary>
    void UpdateHealth(float delta)
    {
        sinceDamaged += delta;
        // K: down at once, to try the ball out.
        if (!downed && !UIManager.BlocksGameplayInput && Input.IsActionJustPressed(InputActions.knockOut))
        {
            TakeDamage(health, GlobalBasis * new Vector3(0, 2, -3));
        }
        if (!downed && sinceDamaged >= regenDelay)
        {
            health = Mathf.MoveToward(health, maxHealth, maxHealth / regenTime * delta);
        }
        UIManager.SetHealth(health, maxHealth);
    }

    /// <summary>
    /// Takes damage on the player's own peer (anything may call it there; other peers send <see cref="_TakeDamage"/>).
    /// <paramref name="push"/> is how fast the ball sets off if this downs the player.
    /// </summary>
    public void TakeDamage(float amount, Vector3 push)
    {
        if (!isLocal || downed || noclip || piloting != null || amount <= 0)
        {
            return;
        }
        health -= amount;
        sinceDamaged = 0;
        if (health <= 0)
        {
            health = 0;
            GoDown(push);
        }
    }

    [RPC]
    public void _TakeDamage(float amount, Vector3 push)
    {
        TakeDamage(amount, push);
    }

    // ---- downed: a ball ---------------------------------------------------

    /// <summary>Becomes a ball at the head, flying off with the player's motion and the hit's push.</summary>
    public void GoDown(Vector3 push)
    {
        Vector3 head = camera.GlobalPosition;
        downedAt = GlobalPosition;
        PlayerBallState state = new PlayerBallState
        {
            playerID = id,
            velocity = velocity + push,
        };
        // The capsule touches nothing from here on (on every peer, through the player's state), so the ball is free.
        SetNoclip(true);
        ballID = GameWorld.SpawnScene(BallScene, head, Vector3.Zero, new GMPOInitData(), GMPObject.serializer.Serialize(state));
        downedTime = 0;
        orbitYaw = GlobalRotation.Y;
        orbitPitch = Mathf.DegToRad(-25f);
        UIManager.SetHoverInfo(null, null);
    }

    /// <summary>Stands back up where the ball is, at full health.</summary>
    public void Revive()
    {
        Node3D ball = BallNode();
        Vector3 at = downedAt;
        if (ball != null)
        {
            // Feet at the bottom of the ball.
            at = ball.GlobalPosition + Vector3.Down * 0.22f;
            GameWorld.DespawnObject(ballID);
        }
        ballID = 0;
        SetNoclip(false);
        Rotation = new Vector3(0, orbitYaw, 0);
        lookPitch = 0;
        TeleportCharacter(new Transform3D(GlobalBasis, at));
        health = maxHealth;
        sinceDamaged = regenDelay;
        PoseCamera(false);
    }

    /// <summary>
    /// Local player: back at their own spawn point (<see cref="spawnIndex"/>), stopped, standing and at full health,
    /// for falling out of the world or getting stuck. Their inventory stays as it is.
    /// </summary>
    public void Respawn()
    {
        if (downed)
        {
            if (BallNode() != null)
            {
                GameWorld.DespawnObject(ballID);
            }
            ballID = 0;
            SetNoclip(false);
        }
        Transform3D spawn = GameBootstrap.SpawnTransform(spawnIndex);
        Rotation = spawn.Basis.GetEuler();
        lookPitch = 0;
        Teleport(spawn.Origin);
        health = maxHealth;
        sinceDamaged = regenDelay;
        PoseCamera(false);
    }

    Node3D BallNode()
    {
        if (GameWorld.syncedObjs.TryGetValue(ballID, out GMPObject obj))
        {
            return obj as Node3D;
        }
        return null;
    }

    /// <summary>Local player, downed, every physics tick: rides inside the ball and waits for jump.</summary>
    void UpdateDowned(float delta)
    {
        downedTime += delta;
        Node3D ball = BallNode();
        if (ball == null)
        {
            // Gone (fell out of the world, or otherwise despawned): back at their spawn point.
            Respawn();
            return;
        }
        // The body sits around the ball, so what the player is near (item sync, views) is where the ball is.
        TeleportCharacter(new Transform3D(GlobalBasis, ball.GlobalPosition + Vector3.Down * 0.9f));
        bool jump = !UIManager.BlocksGameplayInput && Input.IsActionJustPressed(InputActions.jump);
        if (testJump)
        {
            testJump = false;
            jump = true;
        }
        if (jump && downedTime >= reviveDelay)
        {
            Revive();
        }
    }

    /// <summary>Downed: the mouse orbits the camera around the ball, the wheel zooms.</summary>
    void HandleDownedInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton mb && mb.Pressed)
        {
            UIManager.RefreshMouseMode();
        }
        if (@event is InputEventMouseMotion motion && Input.MouseMode == Input.MouseModeEnum.Captured)
        {
            float sensitivity = MouseSensitivity * GameSettings.mouseSensitivity;
            float invert = 1;
            if (GameSettings.invertMouseY)
            {
                invert = -1;
            }
            orbitYaw -= motion.ScreenRelative.X * sensitivity;
            orbitPitch = Mathf.Clamp(orbitPitch - motion.ScreenRelative.Y * sensitivity * invert, Mathf.DegToRad(-85f), Mathf.DegToRad(60f));
        }
        if (@event.IsActionPressed(InputActions.scrollUp))
        {
            ballCameraDistance = Mathf.Max(1.5f, ballCameraDistance - 0.5f);
        }
        if (@event.IsActionPressed(InputActions.scrollDown))
        {
            ballCameraDistance = Mathf.Min(12f, ballCameraDistance + 0.5f);
        }
    }

    /// <summary>Every frame while downed: the camera orbits the ball as it's drawn (interpolated).</summary>
    void PoseBallCamera()
    {
        if (!cameraPosed || !downed)
        {
            return;
        }
        Node3D ball = BallNode();
        if (ball == null)
        {
            return;
        }
        Vector3 target = ball.GetGlobalTransformInterpolated().Origin;
        Basis look = Basis.FromEuler(new Vector3(orbitPitch, orbitYaw, 0));
        camera.GlobalTransform = new Transform3D(look, target + look * new Vector3(0, 0, ballCameraDistance));
    }

    // ---- judging hits (every peer, for the hits it reported) --------------

    static bool judgeHooked;
    /// <summary>Further than any level reaches (m): every hit this peer reported, wherever the player is.</summary>
    const float AnyDistance = 100000;
    /// <summary>Hits slower than this are never looked at (m/s).</summary>
    const float DamageMinSpeed = 3f;
    static readonly List<ContactHit> damageHits = new();
    static readonly StringName bodyTypeName = "body_type";

    /// <summary>Once per physics tick, after the step: damage from this tick's hits, sent to each hit player's peer.</summary>
    static void JudgeHits()
    {
        if (all.Count == 0 || GameWorld.physics == null)
        {
            return;
        }
        PhysicsWorld.Hits(damageHits, Vector3.Zero, AnyDistance, DamageMinSpeed);
        foreach (ContactHit hit in damageHits)
        {
            if (hit.source == ContactHit.Heard)
            {
                continue;
            }
            // Side A is pushed against the normal, side B along it.
            JudgeSide(hit, hit.idA, hit.idB, (SurfaceTags)hit.tagsB, -hit.normal);
            JudgeSide(hit, hit.idB, hit.idA, (SurfaceTags)hit.tagsA, hit.normal);
        }
        foreach (FactoryPlayer player in all)
        {
            if (player.pendingDamage <= 0)
            {
                continue;
            }
            if (player.isLocal)
            {
                player.TakeDamage(player.pendingDamage, player.pendingPush);
            }
            else
            {
                RPCManager.RPCTo(player.authority, player.id, nameof(_TakeDamage), [player.pendingDamage, player.pendingPush]);
            }
            player.pendingDamage = 0;
            player.pendingPush = Vector3.Zero;
        }
    }

    /// <summary>This tick's damage so far from the hits this peer judged, and the hardest push among them.</summary>
    float pendingDamage;
    Vector3 pendingPush;

    static void JudgeSide(ContactHit hit, ulong playerBodyID, ulong otherID, SurfaceTags otherTags, Vector3 pushDirection)
    {
        if (playerBodyID == 0)
        {
            return;
        }
        FactoryPlayer player = null;
        foreach (FactoryPlayer p in all)
        {
            if (p.bodyID == playerBodyID)
            {
                player = p;
                break;
            }
        }
        if (player == null)
        {
            return;
        }
        // Landing on or bumping something simulated: that body's simulator saw the same impact as a contact.
        if (hit.source == ContactHit.FromMover && IsSimulated(otherID))
        {
            return;
        }
        float damage = DamageFromHit(hit.speed, otherTags);
        if (damage <= 0)
        {
            return;
        }
        player.pendingDamage += damage;
        Vector3 push = pushDirection * hit.speed * player.downedPushScale;
        if (push.LengthSquared() > player.pendingPush.LengthSquared())
        {
            player.pendingPush = push;
        }
    }

    /// <summary>A loose item or a dynamic node body: something the physics simulates (and reports its contacts).</summary>
    static bool IsSimulated(ulong id)
    {
        if (id == 0)
        {
            return false;
        }
        if (ItemWorld.Get(id) != null)
        {
            return true;
        }
        Node3D node = PhysicsWorld.BodyNode(id);
        return node != null && ClassDB.ClassGetProperty(node, bodyTypeName).AsInt32() == (int)BodyTypeEnum.Dynamic;
    }

    /// <summary>
    /// How much damage a hit does: <paramref name="speed"/> is how fast the two sides met (m/s, at least
    /// <see cref="DamageMinSpeed"/>), <paramref name="otherTags"/> what the player was hit by (or landed on).
    /// </summary>
    static float DamageFromHit(float speed, SurfaceTags otherTags)
    {
        // Squared over a threshold: jump landings and nudges do nothing, a 3 m fall or a tumbling item stings (~20),
        // a 5 m fall about halves health and anything at 12 m/s or more downs the player at once.
        float over = speed - DamageSafeSpeed;
        if (over <= 0)
        {
            return 0;
        }
        float damage = over * over * DamagePerSpeedSquared;
        // Room to weigh what was hit: bumping into each other never hurts, rubber is soft.
        if (otherTags.HasFlag(SurfaceTags.PLAYER))
        {
            return 0;
        }
        if (otherTags.HasFlag(SurfaceTags.RUBBER))
        {
            damage *= 0.5f;
        }
        return damage;
    }

    /// <summary>Impacts up to this speed (m/s) do no damage.</summary>
    const float DamageSafeSpeed = 4f;
    /// <summary>Damage per (m/s over the safe speed) squared: 8 m/s over is 100.</summary>
    const float DamagePerSpeedSquared = 1.56f;
}
