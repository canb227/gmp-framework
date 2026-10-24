using Godot;
using System.Collections.Generic;

/// <summary>
/// One way of working the <see cref="GravityGun"/>, picked from its radial menu: what right click holds, how long it
/// keeps holding, and what a shot does with nothing held (a shot with something held always launches it). Modes keep
/// no state of their own (the gun does), so one of each serves every gun; a mode's numbers live here, and anything
/// the player's upgrades change is read from <see cref="GravityGun.player"/> where it's used.
/// <para>A new mode: a class here, added to <see cref="GravityGun.modes"/>.</para>
/// </summary>
public abstract class GravityGunMode
{
    /// <summary>Shown in the radial menu.</summary>
    public abstract string name { get; }
    /// <summary>The coil's glow while this mode is selected.</summary>
    public abstract Color color { get; }

    /// <summary>Whether <paramref name="player"/> can pick this mode; locked modes show greyed out in the radial menu.</summary>
    public virtual bool IsUnlocked(FactoryPlayer player)
    {
        return true;
    }

    /// <summary>True when it holds one item, which is synced by id; otherwise every copy catches for itself in <see cref="UpdateCatch"/>.</summary>
    public virtual bool HoldsOne()
    {
        return false;
    }

    /// <summary>Holder's peer, right click pressed: catches what it starts with. False holds nothing.</summary>
    public abstract bool StartGrab(GravityGun gun);

    /// <summary>Every copy, every tick while holding: tops up the catch.</summary>
    public virtual void UpdateCatch(GravityGun gun, double delta)
    {
    }

    /// <summary>Holder's peer, every tick while holding: false lets go.</summary>
    public virtual bool KeepHolding(GravityGun gun, float delta)
    {
        return true;
    }

    /// <summary>
    /// Holder's peer: a shot with nothing held, pushing <paramref name="impulse"/> (N·s, already charged and upgraded);
    /// <paramref name="charge"/> (0 to 1) is how long it was charged, for anything else that grows with it. Adds what
    /// it pushes to <paramref name="pushes"/>.
    /// </summary>
    public abstract void FireEmpty(GravityGun gun, float impulse, float charge, List<GunPush> pushes);
}

/// <summary>Holds the one item aimed at; a shot with nothing held punts whatever is aimed at.</summary>
public class SingleMode : GravityGunMode
{
    /// <summary>A grab lets go if the item stays further than this from the hold point...</summary>
    public float breakDistance = 2.5f;
    /// <summary>...for this long (s), e.g. when it's pinned behind a wall.</summary>
    public float breakTime = 0.5f;
    /// <summary>How far a punt reaches (m).</summary>
    public float puntRange = 12f;

    public override string name => "Single";
    public override Color color => new Color(0.12f, 1.69f, 1.6f);

    public override bool HoldsOne()
    {
        return true;
    }

    public override bool StartGrab(GravityGun gun)
    {
        ItemRecord item = gun.player.pickItem;
        if (item == null || !item.canBeGrabbed) return false;
        // Hold it where it is (distance-wise) rather than yanking it to a fixed point.
        Camera3D camera = gun.player.camera;
        gun.holdDistance = Mathf.Clamp((item.position - camera.GlobalPosition).Dot(-camera.GlobalTransform.Basis.Z), gun.minHoldDistance, gun.maxHoldDistance);
        gun.caught.Add(item.id);
        return true;
    }

    public override bool KeepHolding(GravityGun gun, float delta)
    {
        ulong item = gun.caught[0];
        if (PhysicsWorld.BodyPosition(item).DistanceTo(gun.target) > breakDistance)
        {
            gun.strainedFor += delta;
        }
        else
        {
            gun.strainedFor = 0;
        }
        // Heavy items trail far behind a quick turn: give them longer to catch up before letting go.
        return gun.strainedFor <= breakTime / gun.Lightness(PhysicsWorld.BodyMass(item));
    }

    public override void FireEmpty(GravityGun gun, float impulse, float charge, List<GunPush> pushes)
    {
        Camera3D camera = gun.player.camera;
        Vector3 aim = -camera.GlobalTransform.Basis.Z;
        // Starts outside the player's own capsule.
        Vector3 from = camera.GlobalPosition + aim * 0.6f;
        RayHit hit = PhysicsWorld.Raycast(from, from + aim * puntRange);
        if (!hit.hit) return;
        pushes.Add(new GunPush { id = hit.id, impulse = aim * impulse });
    }
}

/// <summary>
/// Pulls the nearest loose items around the hold point into a ball. A shot with nothing held blasts a cone of force
/// forward from just behind the player (so what's beside them and at their feet is caught too), wider the longer it
/// was charged.
/// </summary>
public class AreaMode : GravityGunMode
{
    /// <summary>Items within this distance of the hold point are caught.</summary>
    public float captureRadius = 3f;
    public int maxItems = 5;
    /// <summary>Seconds between searches for more items.</summary>
    public double rescanInterval = 0.25;
    /// <summary>How far the blast reaches (m), from the cone's tip.</summary>
    public float blastRange = 9f;
    /// <summary>How far behind the eye the cone's tip is (m).</summary>
    public float blastTipBehind = 1f;
    /// <summary>Half the cone's angle (degrees) for a tap, and for a full charge.</summary>
    public float minBlastHalfAngle = 20f;
    public float maxBlastHalfAngle = 60f;

    public override string name => "Area";
    public override Color color => new Color(1.7f, 0.55f, 0.1f);

    public override bool StartGrab(GravityGun gun)
    {
        return true;
    }

    public override void UpdateCatch(GravityGun gun, double delta)
    {
        gun.untilRescan -= delta;
        if (gun.untilRescan <= 0)
        {
            gun.untilRescan = rescanInterval;
            gun.CatchNearbyItems(captureRadius, maxItems);
        }
    }

    public override void FireEmpty(GravityGun gun, float impulse, float charge, List<GunPush> pushes)
    {
        Camera3D camera = gun.player.camera;
        Vector3 aim = -camera.GlobalTransform.Basis.Z;
        Vector3 origin = camera.GlobalPosition - aim * blastTipBehind;
        float halfAngle = Mathf.Lerp(minBlastHalfAngle, maxBlastHalfAngle, charge);
        float minCos = Mathf.Cos(Mathf.DegToRad(halfAngle));
        ulong self = PhysicsWorld.BodyIdOf(gun.player);
        foreach (long found in PhysicsWorld.OverlapSphere(origin, blastRange))
        {
            ulong id = (ulong)found;
            if (id == self) continue;
            Vector3 offset = PhysicsWorld.BodyPosition(id) - origin;
            float distance = offset.Length();
            if (distance < 0.01f || distance > blastRange) continue;
            Vector3 direction = offset / distance;
            float along = direction.Dot(aim);
            if (along < minCos) continue;
            float strength = BlastFalloff(distance / blastRange, along, minCos);
            if (strength <= 0) continue;
            pushes.Add(new GunPush { id = id, impulse = direction * impulse * strength });
        }
    }

    /// <summary>
    /// How much of the blast's full push (0 to 1) reaches a body <paramref name="distanceFraction"/> of the way to the
    /// blast's range (0 at the cone's tip, 1 at the edge), whose direction from the tip has a cosine of <paramref name="along"/>
    /// with the aim (1 dead ahead, down to <paramref name="minCos"/> at the cone's edge).
    /// </summary>
    static float BlastFalloff(float distanceFraction, float along, float minCos)
    {
        // Full push out to 30% of the range, then fading to a quarter at the far end: point blank is dependable, and
        // the far edge of a pile still gets a shove.
        float byDistance = 1f;
        if (distanceFraction > 0.3f)
        {
            byDistance = Mathf.Lerp(1f, 0.25f, (distanceFraction - 0.3f) / 0.7f);
        }
        // Half the push at the cone's rim, all of it dead ahead, so aiming still matters in a wide blast.
        float byAngle = Mathf.Lerp(0.5f, 1f, Mathf.InverseLerp(minCos, 1f, along));
        return byDistance * byAngle;
    }
}
