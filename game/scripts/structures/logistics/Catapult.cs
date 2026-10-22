using Godot;
using PolyType;

[GenerateShape]
public partial record struct CatapultSave
{
    public int strength;
}

/// <summary>
/// A catapult: items fed into its bucket (from a belt on its left) are flung forward once they've sat there a moment.
/// Its arm is posed by an AnimationPlayer: "Throw" swings it up until the beam hits the stop bar, where the items fly
/// on out of the bucket, then "Reset" winds it back down. The arm's colliders are a kinematic GMPBody under the arm
/// node, so Box3D hands the arm's speed to whatever is in the bucket. How hard it throws is <see cref="strength"/>,
/// set by looking at it and scrolling, and shown on the gauge: "Throw" plays faster the stronger it is.
/// <para>
/// During the swing items slide out along the arm (centrifugal, about twice the push of the floor by the time it lets
/// go) and pile against the bucket's back wall. That wall is frictionless, or it drags them down to the arm's speed
/// as the arm brakes, and it leans back, or it swings into their path in the last few degrees before the stop bar.
/// </para>
/// <para>
/// The host decides when to fire (it simulates every loose item, so only its arm really throws anything) and tells
/// every peer with <see cref="Fire"/>, which waits on each client until its playback clock reaches the tick it was
/// sent on: the arm then swings in step with the items as that client shows them. Any peer can change the strength.
/// </para>
/// </summary>
public partial class Catapult : Structure
{
    [Export] public AnimationPlayer animationPlayer;
    /// <summary>A sensor shape filling the bucket.</summary>
    [Export] public Node3D loadSensor;
    [Export] public Node3D gaugeNeedle;

    /// <summary>How many strength settings there are; 1 is the weakest.</summary>
    [Export] public int strengthLevels = 10;
    /// <summary>1 to <see cref="strengthLevels"/>.</summary>
    [Export] public int strength = 5;
    /// <summary>How long (s) items have to sit in the bucket before it throws, so a belt's worth can gather.</summary>
    [Export] public float loadDelay = 0.5f;
    /// <summary>The gauge needle's turn about X at the weakest setting; the strongest is the negative of it.</summary>
    [Export] public float gaugeSweepDegrees = 100f;

    // "Throw"'s speed scale at the weakest and strongest settings: the bucket lets go at about 5.5 and 11.5 m/s, and
    // items land about 6 and 26 m away (on flat ground, measured from the bucket).
    const float MinThrowSpeed = 1f;
    const float MaxThrowSpeed = 2.1f;

    static readonly StringName getOverlappingName = "get_overlapping";
    static readonly StringName throwAnimation = "Throw";
    static readonly StringName resetAnimation = "Reset";

    /// <summary>Every peer: true from <see cref="Fire"/> until the arm is back at rest.</summary>
    public bool throwing { get; private set; }
    /// <summary>Host only: how long (s) the bucket has had something in it.</summary>
    float loadedFor;
    /// <summary>When the strength now set was sent (<see cref="GameWorld.netTime"/>), so an older change arriving late is ignored.</summary>
    ulong strengthSentAt;

    public override void AfterInit()
    {
        base.AfterInit();
        animationPlayer.AnimationFinished += OnAnimationFinished;
        ShowStrength();
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!runsMachineLogic || throwing)
        {
            return;
        }
        if (ItemsInBucket() > 0)
        {
            loadedFor += (float)delta;
        }
        else
        {
            loadedFor = 0;
        }
        if (loadedFor >= loadDelay)
        {
            loadedFor = 0;
            RPCManager.RPC(this, nameof(Fire), [strength]);
        }
    }

    /// <summary>How many loose items the bucket's sensor overlaps.</summary>
    public int ItemsInBucket()
    {
        int count = 0;
        foreach (long id in loadSensor.Call(getOverlappingName).AsInt64Array())
        {
            if (ItemWorld.Get((ulong)id) != null)
            {
                count++;
            }
        }
        return count;
    }

    /// <summary>Every peer: swing the arm at the speed for <paramref name="level"/> (the host's strength when it fired).</summary>
    [RPC(requireAuthority = true, playbackDelayed = true)]
    void Fire(int level)
    {
        throwing = true;
        animationPlayer.Play(throwAnimation, -1, ThrowSpeed(level));
    }

    void OnAnimationFinished(StringName animation)
    {
        if (animation == throwAnimation)
        {
            animationPlayer.Play(resetAnimation);
        }
        else if (animation == resetAnimation)
        {
            throwing = false;
        }
    }

    /// <summary>
    /// How fast "Throw" plays at strength <paramref name="level"/> (1 to <see cref="strengthLevels"/>): its speed scale.
    /// At 1 the bucket leaves the arm at about 5 m/s, and the speed it leaves at grows in proportion.
    /// </summary>
    float ThrowSpeed(int level)
    {
        float t = (level - 1f) / Mathf.Max(1, strengthLevels - 1);
        return Mathf.Lerp(MinThrowSpeed, MaxThrowSpeed, t);
    }

    // ---- strength ----

    /// <summary>Scrolling while looking at it: up is stronger. Always taken, so the hotbar doesn't move.</summary>
    public override bool HandleScroll(int step)
    {
        int level = Mathf.Clamp(strength - step, 1, strengthLevels);
        if (level != strength)
        {
            SendStrength(level);
        }
        return true;
    }

    /// <summary>Sets the strength on every peer (this one included).</summary>
    public void SendStrength(int level)
    {
        RPCManager.RPC(this, nameof(SetStrength), [level]);
    }

    [RPC]
    void SetStrength(int level)
    {
        // Two peers scrolling at once: everyone ends up on whichever was sent last.
        if (RPCManager.sendTime < strengthSentAt)
        {
            return;
        }
        strengthSentAt = RPCManager.sendTime;
        strength = Mathf.Clamp(level, 1, strengthLevels);
        ShowStrength();
    }

    /// <summary>Turns the gauge needle and updates the hover text to <see cref="strength"/>.</summary>
    void ShowStrength()
    {
        float t = (strength - 1f) / Mathf.Max(1, strengthLevels - 1);
        float sweep = Mathf.DegToRad(gaugeSweepDegrees);
        gaugeNeedle.Rotation = new Vector3(Mathf.Lerp(sweep, -sweep, t), 0, 0);
        hoverText = $"Strength {strength}/{strengthLevels}\nScroll to adjust";
    }

    // ---- saving ----

    public override byte[] SaveState()
    {
        return GMPObject.serializer.Serialize(new CatapultSave { strength = strength });
    }

    public override void LoadState(byte[] state)
    {
        strength = GMPObject.serializer.Deserialize<CatapultSave>(state).strength;
        ShowStrength();
    }
}
