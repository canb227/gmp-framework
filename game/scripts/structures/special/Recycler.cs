using Godot;
using PolyType;
using System.Collections.Generic;

/// <summary>What a save keeps of a <see cref="Recycler"/>: its banked points and where its two clocks stand.</summary>
[GenerateShape]
public partial record struct RecyclerSave
{
    public int points;
    public double untilBase;
    public double untilBonus;
}

/// <summary>
/// The Scrap Recycler: a huge, permanent machine that spawns an item (picked from <see cref="itemWeights"/>) at its
/// first output every <see cref="baseInterval"/> seconds, and grinds what players drop on its rollers into points that
/// pay for a bonus item every <see cref="bonusInterval"/> seconds at its second output.
/// <para>
/// Items bounce in the roller valley (real physics: the <see cref="rollers"/> are kinematic bodies this turns on every
/// peer, so whichever peer simulates an item sees them) until the bonus clock is ready and the bank is empty;
/// then the item that has been in the valley longest, for at least <see cref="minDwell"/> seconds, is eaten for its
/// <see cref="itemPoints"/> (1 if it isn't listed) and one point is spent at once. So at most one item is eaten per
/// bonus, and the rest wait their turn on the rollers. Runs on the authority, which despawns and spawns for everyone.
/// </para>
/// <para>Never deconstructed: interacting does nothing. Same race as <see cref="Grinder"/>: an item grabbed at the
/// instant it's eaten is decided separately by its own authority. Held items are skipped.</para>
/// </summary>
public partial class Recycler : Structure
{
    /// <summary>Item ids to spawn and their relative weights.</summary>
    [Export] public Godot.Collections.Dictionary<string, float> itemWeights = new() { { "scrap_ball", 1f } };
    /// <summary>Points each eaten item is worth, by item id; anything not listed is worth 1.</summary>
    [Export] public Godot.Collections.Dictionary<string, int> itemPoints = new();
    /// <summary>Seconds between the free items it always spawns.</summary>
    [Export] public double baseInterval = 1.0;
    /// <summary>Seconds between bonus items paid for with points.</summary>
    [Export] public double bonusInterval = 1.0;
    /// <summary>Seconds an item must have been in the valley before it can be eaten, so it churns a while first.</summary>
    [Export] public double minDwell = 2.0;
    /// <summary>The kinematic roller bodies, each turning about its own local X axis.</summary>
    [Export] public Godot.Collections.Array<Node3D> rollers = [];
    [Export] public float rollerDegreesPerSecond = 30f;

    /// <summary>Items eaten / spawned so far (authority only).</summary>
    public int consumedCount { get; private set; }
    public int producedCount { get; private set; }

    int points;
    double untilBase;
    double untilBonus;
    // When each item now in the valley was first seen there (authority only, not saved: a load restarts the dwell).
    Dictionary<ulong, ulong> firstSeen = new();

    // Each roller's rest pose, and which way it turns so its top rolls toward the middle of the valley.
    Basis[] rollerRest;
    float[] rollerTurn;
    float rollerAngle;

    public override void AfterInit()
    {
        hoverText = "Feed it scrap.";
        base.AfterInit();
        rollerRest = new Basis[rollers.Count];
        rollerTurn = new float[rollers.Count];
        for (int i = 0; i < rollers.Count; i++)
        {
            rollerRest[i] = rollers[i].Basis;
            // A positive turn about +X moves a roller's top toward +Z, so rollers behind the middle (+Z) turn the
            // other way; a roller yawed round has its local X flipped, which flips it again.
            rollerTurn[i] = -Mathf.Sign(rollers[i].Position.Z) * Mathf.Sign(rollers[i].Basis.X.X);
        }
    }

    public override void onInteract(ulong playerID) { }

    public override byte[] SaveState()
    {
        return GMPObject.serializer.Serialize(new RecyclerSave { points = points, untilBase = untilBase, untilBonus = untilBonus });
    }

    public override void LoadState(byte[] state)
    {
        RecyclerSave s = GMPObject.serializer.Deserialize<RecyclerSave>(state);
        points = s.points;
        untilBase = s.untilBase;
        untilBonus = s.untilBonus;
    }

    public override void _PhysicsProcess(double delta)
    {
        SpinRollers(delta);
        if (!runsMachineLogic) return;

        untilBase -= delta;
        if (untilBase <= 0)
        {
            untilBase += baseInterval;
            SpawnOne(0);
        }

        PhysicalFactoryItem ready = OldestReady();
        // The bonus clock waits at zero until there's a point to spend.
        untilBonus = Mathf.Max(0, untilBonus - delta);
        if (untilBonus > 0) return;
        if (points == 0 && ready != null)
        {
            points += itemPoints.TryGetValue(ready.itemID ?? "", out int p) ? p : 1;
            firstSeen.Remove(ready.id);
            GameWorld.DespawnObject(ready.id, DespawnReason.Consumed);
            consumedCount++;
        }
        if (points > 0)
        {
            points--;
            SpawnOne(1);
            untilBonus = bonusInterval;
        }
    }

    // Runs on every peer, each on its own clock: turning a kinematic body's node is what moves it in Box3D, and
    // whoever simulates an item sees the rollers it's touching turn.
    void SpinRollers(double delta)
    {
        if (rollerRest == null) return;
        rollerAngle = Mathf.Wrap(rollerAngle + Mathf.DegToRad(rollerDegreesPerSecond) * (float)delta, 0f, Mathf.Tau);
        for (int i = 0; i < rollers.Count; i++)
        {
            rollers[i].Basis = rollerRest[i] * new Basis(Vector3.Right, rollerAngle * rollerTurn[i]);
        }
    }

    // Updates when each item in the valley was first seen, and returns the one there longest if it has dwelt long enough.
    PhysicalFactoryItem OldestReady()
    {
        ulong now = Time.GetTicksMsec();
        Dictionary<ulong, ulong> seen = new();
        PhysicalFactoryItem oldest = null;
        ulong oldestSince = ulong.MaxValue;
        foreach (PhysicalFactoryItem item in ItemsAtInputs())
        {
            if (GameWorld.heldBy.ContainsKey(item.id)) continue;
            ulong since = firstSeen.TryGetValue(item.id, out ulong t) ? t : now;
            seen[item.id] = since;
            if (since < oldestSince)
            {
                oldest = item;
                oldestSince = since;
            }
        }
        firstSeen = seen;
        return oldest != null && now - oldestSince >= minDwell * 1000 ? oldest : null;
    }

    // Spawns one item at the output'th output port (0: the regular stream, 1: the bonus items).
    void SpawnOne(int output)
    {
        string itemID = ItemSpawner.PickWeighted(itemWeights);
        if (itemID == null) return;
        ItemInfo.SpawnInWorld(itemID, OutputPosition(output), GlobalRotation);
        producedCount++;
    }
}
