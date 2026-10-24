using System.Collections.Generic;
using Godot;

/// <summary>
/// Drops loose items into a level: a one-shot burst or a continuous stream, scattered through a box
/// (<see cref="spawnVolume"/>) or launched from <see cref="outputs"/>, picked from <see cref="itemWeights"/>, repeatable
/// with a <see cref="seed"/>. It starts with the level, after a delay, or when a button or lever triggers it
/// (<see cref="Triggerable"/>).
/// <para>
/// It can also hold a baked pile: items settled in game and saved from the "debugui droppers" window (Spawn, Clear,
/// Save; see DroppersDebug). The pile lives in a file named after the dropper's place in its scene (<see cref="PilePath"/>)
/// and is spawned asleep on every peer when the level starts, with no messages: every peer has the file, and works out
/// the same ids. In the editor the dropper draws its pile, so moving or turning the dropper moves the pile.
/// </para>
/// <para>
/// A dropper with a pile doesn't also start by itself (LevelStart, Delay): the pile is what it puts in the level.
/// Triggers and the debug window still drop more.
/// </para>
/// <para>
/// Spawning is the host's (it simulates every loose item); each tick's spawns go out in one message
/// (<see cref="ItemWorld.SpawnBatch"/>). Nothing happens while a save is restored or a peer joins
/// (<see cref="GameWorld.frozen"/>): the restore brings back the items as they were.
/// </para>
/// </summary>
[Tool]
[GlobalClass]
public partial class ItemDropper : Node3D, Triggerable
{
    public enum DropMode
    {
        /// <summary>Each firing drops <see cref="count"/> items, <see cref="itemsPerTick"/> at a time, then stops.</summary>
        OneShot,
        /// <summary>While on, drops <see cref="itemsPerInterval"/> every <see cref="interval"/> seconds, until <see cref="count"/> (0: forever).</summary>
        Continuous,
    }

    public enum StartMode
    {
        /// <summary>Fires (or turns on) as soon as the level starts.</summary>
        LevelStart,
        /// <summary>Fires (or turns on) <see cref="delaySeconds"/> after the level starts.</summary>
        Delay,
        /// <summary>Only when triggered (<see cref="OnTrigger"/>) or from the debug window.</summary>
        Trigger,
    }

    /// <summary>Items to drop and their relative weights.</summary>
    [Export] public Godot.Collections.Dictionary<ItemInfo, float> itemWeights = new();
    [Export] public DropMode mode = DropMode.OneShot;
    [Export] public StartMode startOn = StartMode.Trigger;
    [Export] public double delaySeconds = 5.0;
    /// <summary>Items per firing (one-shot), or in all (continuous; 0: no end).</summary>
    [Export] public int count = 100;
    /// <summary>Most items a one-shot drops in one physics tick, so a big drop isn't one huge spike.</summary>
    [Export] public int itemsPerTick = 50;
    /// <summary>Continuous: seconds between drops.</summary>
    [Export] public double interval = 1.0;
    /// <summary>Continuous: items in each drop.</summary>
    [Export] public int itemsPerInterval = 1;
    /// <summary>
    /// Seeds the item picks, positions, rotations and launch spread, so every firing drops the same way. -1 picks a
    /// random seed on each firing and shows it here (and in the log); type it in to keep it, or use Keep seed.
    /// </summary>
    [Export] public long seed = -1;

    [ExportGroup("Where")]
    /// <summary>
    /// A child CollisionShape3D with a BoxShape3D: the box items are scattered through (only its size and place are
    /// used). Ignored while there are <see cref="outputs"/>.
    /// </summary>
    [Export] public CollisionShape3D spawnVolume;
    /// <summary>Items in the volume land on whatever is below them (a ray down through the box) instead of anywhere in it.</summary>
    [Export] public bool dropToGround = false;
    /// <summary>With <see cref="dropToGround"/>: only on surfaces with one of these tags (none: any surface).</summary>
    [Export] public SurfaceTags groundTags;
    /// <summary>
    /// Points the items take turns appearing at instead of the volume (each launched along its -Z). With neither,
    /// items appear at the dropper itself.
    /// </summary>
    [Export] public Godot.Collections.Array<Node3D> outputs = new();
    /// <summary>
    /// Metres per second items are launched at: along an output's -Z, or for items from the volume (or the dropper
    /// itself) along the dropper's -Z. 0 drops them in place.
    /// </summary>
    [Export] public float launchSpeed = 0f;
    /// <summary>Degrees a launched item may stray from its launch direction, at most.</summary>
    [Export] public float launchSpreadDegrees = 0f;

    [ExportGroup("Limits")]
    /// <summary>Holds while this many items of any kind exist anywhere (0: no limit).</summary>
    [Export] public int maxTotalItems = 0;
    /// <summary>Holds while this many items are within <see cref="nearbyRadius"/> of the dropper (0: no limit).</summary>
    [Export] public int maxNearby = 0;
    [Export] public float nearbyRadius = 3f;

    [ExportGroup("Pile")]
    /// <summary>A child CollisionShape3D with a BoxShape3D: Save with "capture" on saves every item inside it.</summary>
    [Export] public CollisionShape3D captureVolume;
    /// <summary>Spawns the pile awake, to settle again (a pile moved somewhere it wasn't baked). Asleep, it stays exactly as baked until touched.</summary>
    [Export] public bool settleOnLoad = false;
    /// <summary>Warns in the editor when there's no pile file (a renamed or moved dropper loses its file).</summary>
    [Export] public bool expectPile = false;
    /// <summary>A pile to load instead of this dropper's own file (droppers sharing one pile). Save still writes its own file.</summary>
    [Export] public ItemPileData pileOverride;

    /// <summary>Every dropper in the game, for the debug window.</summary>
    public static readonly List<ItemDropper> all = new();

    /// <summary>Host: the items it dropped, or loaded or captured as its pile (some may be gone since).</summary>
    public readonly HashSet<ulong> ownedItems = new();
    /// <summary>Host: continuous and switched on.</summary>
    public bool running { get; private set; }
    /// <summary>Host: items left to drop in this firing (one-shot) or in all (continuous with a count).</summary>
    public int itemsLeft { get; private set; }

    System.Random random = new();
    double untilStart = -1;
    double untilNextDrop;
    int nextOutput;
    string holdingFor;

    public override void _Ready()
    {
        if (Engine.IsEditorHint())
        {
            RebuildPreview();
            return;
        }
        all.Add(this);
        if (GameWorld.frozen)
        {
            return;
        }
        WarnAboutSetup();
        // A baked pile is what the dropper puts in the level when it starts; only a trigger (or the debug window) drops more.
        bool loadedPile = LoadPile();
        if (Lobby.isHost && !loadedPile)
        {
            if (startOn == StartMode.LevelStart)
            {
                untilStart = 0;
            }
            else if (startOn == StartMode.Delay)
            {
                untilStart = delaySeconds;
            }
        }
    }

    public override void _ExitTree()
    {
        all.Remove(this);
    }

    public override void _Process(double delta)
    {
        if (Engine.IsEditorHint())
        {
            WatchPileFile(delta);
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (Engine.IsEditorHint() || !Lobby.isHost || !GameWorld.started || GameWorld.frozen)
        {
            return;
        }
        if (untilStart >= 0)
        {
            untilStart -= delta;
            if (untilStart < 0)
            {
                Start();
            }
        }

        if (mode == DropMode.OneShot)
        {
            if (itemsLeft > 0)
            {
                Drop(Mathf.Min(itemsLeft, itemsPerTick));
            }
            return;
        }
        if (!running)
        {
            return;
        }
        untilNextDrop -= delta;
        if (untilNextDrop <= 0)
        {
            int amount = itemsPerInterval;
            if (count > 0)
            {
                amount = Mathf.Min(amount, itemsLeft);
            }
            // Held at 0 while a limit is reached, so it drops the moment there's room.
            if (Drop(amount) > 0)
            {
                untilNextDrop += interval;
            }
            if (count > 0 && itemsLeft <= 0)
            {
                running = false;
            }
        }
    }

    // ---- setup mistakes ----

    /// <summary>What's likely set up wrong: shown on the node in the editor, and logged once when the level starts.</summary>
    string[] SetupProblems()
    {
        List<string> problems = new();
        foreach (Node3D output in outputs)
        {
            if (output is CollisionShape3D)
            {
                problems.Add($"Output {output.Name} is a CollisionShape3D: every item appears at its centre, on top of each other. A box to scatter items through goes in Spawn Volume.");
            }
        }
        if (spawnVolume != null && spawnVolume.Shape is not BoxShape3D)
        {
            problems.Add("Spawn Volume needs a BoxShape3D.");
        }
        if (captureVolume != null && captureVolume.Shape is not BoxShape3D)
        {
            problems.Add("Capture Volume needs a BoxShape3D.");
        }
        return problems.ToArray();
    }

    public override string[] _GetConfigurationWarnings()
    {
        return SetupProblems();
    }

    void WarnAboutSetup()
    {
        foreach (string problem in SetupProblems())
        {
            Logging.Warn($"{Name}: {problem}", "ItemDropper");
        }
    }

    // ---- firing ----

    /// <summary>
    /// Every peer, in the same order, once the host accepts an activation; only the host acts. A one-shot fires on
    /// each "on"; a continuous dropper runs while its trigger is on (a lever, not a push button).
    /// </summary>
    public void OnTrigger(bool active)
    {
        if (!Lobby.isHost)
        {
            return;
        }
        if (mode == DropMode.OneShot)
        {
            if (active)
            {
                Start();
            }
            return;
        }
        if (active)
        {
            Start();
        }
        else
        {
            running = false;
        }
    }

    /// <summary>Host: fires a one-shot, or turns a continuous dropper on, starting its seeded random numbers over.</summary>
    public void Start()
    {
        long useSeed = seed;
        ItemPileData pile = LoadPileData();
        if (useSeed == -1 && pile != null && pile.seed != -1)
        {
            useSeed = pile.seed;
        }
        if (useSeed == -1)
        {
            // Shown in the inspector's remote tree and the debug window, so a drop worth keeping can be kept.
            seed = System.Random.Shared.Next();
            useSeed = seed;
            Logging.Log($"{Name}: seed {seed}", "ItemDropper");
        }
        random = new System.Random((int)(useSeed & int.MaxValue));
        nextOutput = 0;
        itemsLeft = count;
        if (mode == DropMode.Continuous)
        {
            running = true;
            untilNextDrop = 0;
        }
    }

    /// <summary>
    /// Host: drops up to <paramref name="amount"/> items in one message, unless a limit is reached. Returns how many
    /// it dropped.
    /// </summary>
    int Drop(int amount)
    {
        if (!HasRoom())
        {
            return 0;
        }
        List<ItemSave> spawns = new();
        // A point the ray misses (or that lands on the wrong surface) is tried again, up to a few times the amount.
        int tries = 0;
        while (spawns.Count < amount && tries < amount * 4)
        {
            tries++;
            ItemInfo item = PickWeighted();
            if (item == null)
            {
                break;
            }
            if (!TryPickSpot(out Vector3 position, out Vector3 velocity))
            {
                continue;
            }
            Vector3 rotation = new Vector3(random.NextSingle(), random.NextSingle(), random.NextSingle()) * Mathf.Tau;
            spawns.Add(new ItemSave
            {
                id = (ulong)System.Random.Shared.NextInt64(),
                itemID = item.itemID,
                position = position,
                rotation = Basis.FromEuler(rotation).GetRotationQuaternion(),
                linearVelocity = velocity,
            });
        }
        if (spawns.Count == 0)
        {
            return 0;
        }
        foreach (ItemSave spawn in spawns)
        {
            ownedItems.Add(spawn.id);
        }
        // Items placed on the ground get no nudge, or they'd hop off it.
        ItemWorld.SpawnBatch(spawns.ToArray(), !dropToGround);
        itemsLeft -= spawns.Count;
        return spawns.Count;
    }

    bool HasRoom()
    {
        string limit = null;
        if (maxTotalItems > 0 && ItemWorld.totalCount >= maxTotalItems)
        {
            limit = $"{maxTotalItems} items in the world";
        }
        else if (maxNearby > 0 && PhysicsWorld.OverlapSphere(GlobalPosition, nearbyRadius, PhysicsWorld.ItemLayer).Length >= maxNearby)
        {
            limit = $"{maxNearby} items within {nearbyRadius} m";
        }
        // Each hold is logged once, for tuning the limits.
        if (limit != null && holdingFor != limit)
        {
            Logging.Log($"{Name} holding: {limit}", "ItemDropper");
        }
        holdingFor = limit;
        return limit == null;
    }

    /// <summary>Picks an item at random in proportion to its weight; null if none has a positive weight.</summary>
    ItemInfo PickWeighted()
    {
        float total = 0;
        foreach (float weight in itemWeights.Values)
        {
            total += Mathf.Max(0f, weight);
        }
        if (total <= 0)
        {
            return null;
        }
        float roll = random.NextSingle() * total;
        ItemInfo last = null;
        foreach (KeyValuePair<ItemInfo, float> entry in itemWeights)
        {
            if (entry.Value <= 0 || entry.Key == null)
            {
                continue;
            }
            last = entry.Key;
            roll -= entry.Value;
            if (roll < 0)
            {
                return entry.Key;
            }
        }
        return last; // rounding at the very top of the range
    }

    /// <summary>
    /// Where the next item appears and how fast it leaves: the next output (launched along its -Z), or a point in the
    /// volume or the dropper itself (launched along the dropper's -Z).
    /// </summary>
    bool TryPickSpot(out Vector3 position, out Vector3 velocity)
    {
        if (outputs.Count > 0)
        {
            Node3D output = outputs[nextOutput % outputs.Count];
            nextOutput = (nextOutput + 1) % outputs.Count;
            position = output.GlobalPosition;
            velocity = LaunchVelocity(output);
            return true;
        }
        velocity = LaunchVelocity(this);
        if (spawnVolume == null || spawnVolume.Shape is not BoxShape3D box)
        {
            position = GlobalPosition;
            return true;
        }
        Vector3 half = box.Size / 2;
        Vector3 local = new Vector3(
            (random.NextSingle() * 2 - 1) * half.X,
            (random.NextSingle() * 2 - 1) * half.Y,
            (random.NextSingle() * 2 - 1) * half.Z
        );
        Transform3D volume = spawnVolume.GlobalTransform;
        position = volume * local;
        if (!dropToGround)
        {
            return true;
        }
        // Down through the whole box, from its top to its bottom, at this point's x and z.
        Vector3 top = volume * new Vector3(local.X, half.Y, local.Z);
        Vector3 bottom = volume * new Vector3(local.X, -half.Y, local.Z);
        RayHit hit = PhysicsWorld.Raycast(top, bottom, ~(GameWorld.QueryHiddenLayer | PhysicsWorld.ItemLayer));
        if (!hit.hit)
        {
            return false;
        }
        if (groundTags != 0 && ((SurfaceTags)hit.tags & groundTags) == 0)
        {
            return false;
        }
        position = hit.position + Vector3.Up * 0.3f;
        return true;
    }

    /// <summary><see cref="launchSpeed"/> along <paramref name="from"/>'s forward (-Z), turned a random angle of up to <see cref="launchSpreadDegrees"/> away from it.</summary>
    Vector3 LaunchVelocity(Node3D from)
    {
        if (launchSpeed <= 0)
        {
            return Vector3.Zero;
        }
        Basis basis = from.GlobalBasis.Orthonormalized();
        Vector3 forward = -basis.Z;
        if (launchSpreadDegrees <= 0)
        {
            return forward * launchSpeed;
        }
        // Tilt away from forward by up to the spread, around an axis at a random angle about forward. Picking the
        // cosine evenly (not the angle) spreads the directions evenly over the cone instead of bunching them in the middle.
        float maxCos = Mathf.Cos(Mathf.DegToRad(launchSpreadDegrees));
        float tilt = Mathf.Acos(Mathf.Lerp(1f, maxCos, random.NextSingle()));
        Vector3 axis = basis.X.Rotated(forward, random.NextSingle() * Mathf.Tau);
        return forward.Rotated(axis, tilt) * launchSpeed;
    }

    // ---- the debug window's buttons (host) ----

    /// <summary>Host: despawns every item it owns that's still around.</summary>
    public void ClearItems()
    {
        int cleared = 0;
        foreach (ulong id in ownedItems)
        {
            if (ItemWorld.Get(id) != null)
            {
                ItemWorld.Despawn(id);
                cleared++;
            }
        }
        ownedItems.Clear();
        itemsLeft = 0;
        running = false;
        Logging.Log($"{Name}: cleared {cleared} items", "ItemDropper");
    }

    /// <summary>
    /// Host: saves the pile to <see cref="PilePath"/>: its own items that are still around, or with
    /// <paramref name="capture"/> every item in <see cref="captureVolume"/> (which become its own). Keeps the file's
    /// seed.
    /// </summary>
    public void SavePile(bool capture)
    {
        string path = PilePath();
        if (path == null)
        {
            Logging.Error($"{Name}: not in a saved scene, so its pile has no file", "ItemDropper");
            return;
        }
        List<ulong> ids = new();
        if (capture)
        {
            if (captureVolume == null || captureVolume.Shape is not BoxShape3D box)
            {
                Logging.Error($"{Name}: capture needs a captureVolume with a BoxShape3D", "ItemDropper");
                return;
            }
            Transform3D volume = captureVolume.GlobalTransform;
            foreach (long id in PhysicsWorld.OverlapBox(volume, box.Size * volume.Basis.Scale, PhysicsWorld.ItemLayer))
            {
                if (ItemWorld.Get((ulong)id) != null)
                {
                    ids.Add((ulong)id);
                }
            }
            ownedItems.Clear();
            foreach (ulong id in ids)
            {
                ownedItems.Add(id);
            }
        }
        else
        {
            foreach (ulong id in ownedItems)
            {
                if (ItemWorld.Get(id) != null)
                {
                    ids.Add(id);
                }
            }
        }

        ItemPileData pile = new ItemPileData();
        ItemPileData old = LoadOwnPileFile();
        if (old != null)
        {
            pile.seed = old.seed;
        }
        Transform3D toLocal = GlobalTransform.AffineInverse();
        List<int> kinds = new();
        foreach (ulong id in ids)
        {
            string itemID = ItemWorld.Get(id).itemID;
            int kind = pile.itemIDs.IndexOf(itemID);
            if (kind < 0)
            {
                kind = pile.itemIDs.Count;
                pile.itemIDs.Add(itemID);
            }
            kinds.Add(kind);
            pile.transforms.Add(toLocal * PhysicsWorld.BodyTransform(id));
        }
        pile.kinds = kinds.ToArray();
        WritePile(pile, path);
        Logging.Log($"{Name}: saved {ids.Count} items to {path}", "ItemDropper");
    }

    /// <summary>Host: puts the seed it used last into its pile file (made empty if there's none yet), so it's used whenever its own seed is -1.</summary>
    public void KeepSeed()
    {
        string path = PilePath();
        if (path == null || seed == -1)
        {
            return;
        }
        ItemPileData pile = LoadOwnPileFile();
        if (pile == null)
        {
            pile = new ItemPileData();
        }
        pile.seed = seed;
        WritePile(pile, path);
        Logging.Log($"{Name}: kept seed {seed} in {path}", "ItemDropper");
    }

    static void WritePile(ItemPileData pile, string path)
    {
        DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath(path.GetBaseDir()));
        Error err = ResourceSaver.Save(pile, path);
        if (err != Error.Ok)
        {
            Logging.Error($"Saving {path} failed: {err}", "ItemDropper");
        }
    }

    // ---- the pile ----

    /// <summary>
    /// Where this dropper's pile is saved: named after its scene and its path in that scene, so it needs no setting
    /// (game/piles/dev_area/Droppers_DoorPile.res). Null outside a saved scene.
    /// </summary>
    public string PilePath()
    {
        if (Owner == null || string.IsNullOrEmpty(Owner.SceneFilePath))
        {
            return null;
        }
        string sceneName = Owner.SceneFilePath.GetFile().GetBaseName();
        string nodePath = Owner.GetPathTo(this).ToString().Replace("/", "_");
        return $"res://game/piles/{sceneName}/{nodePath}.res";
    }

    /// <summary>The pile file at <see cref="PilePath"/>, read fresh from disk (a Save may have just written it); null if there's none.</summary>
    ItemPileData LoadOwnPileFile()
    {
        string path = PilePath();
        if (path == null || !FileAccess.FileExists(path))
        {
            return null;
        }
        return ResourceLoader.Load<ItemPileData>(path, null, ResourceLoader.CacheMode.Ignore);
    }

    /// <summary>The pile it loads: <see cref="pileOverride"/>, else its own file.</summary>
    ItemPileData LoadPileData()
    {
        if (pileOverride != null)
        {
            return pileOverride;
        }
        return LoadOwnPileFile();
    }

    /// <summary>
    /// Every peer, when the level starts: spawns the pile here, each item with an id every peer works out the same.
    /// Returns whether there was a pile.
    /// </summary>
    bool LoadPile()
    {
        ItemPileData pile = LoadPileData();
        if (pile == null || pile.transforms.Count == 0)
        {
            return false;
        }
        string idSource = PilePath();
        for (int i = 0; i < pile.transforms.Count; i++)
        {
            ulong id = PileItemId(idSource, i);
            string itemID = pile.itemIDs[pile.kinds[i]];
            ItemWorld.SpawnLocal(id, itemID, GlobalTransform * pile.transforms[i], Vector3.Zero, Vector3.Zero, settleOnLoad);
            if (Lobby.isHost)
            {
                ownedItems.Add(id);
            }
        }
        Logging.Log($"{Name}: loaded a pile of {pile.transforms.Count} items", "ItemDropper");
        return true;
    }

    /// <summary>
    /// The id of the <paramref name="index"/>th item of the pile saved at <paramref name="pilePath"/>: the same on every
    /// peer and every run, never 0, and (in practice) never another item's.
    /// </summary>
    static ulong PileItemId(string pilePath, int index)
    {
        // 64-bit FNV-1a over the path's bytes, then the index's: the same everywhere, unlike string.GetHashCode,
        // which .NET randomizes per process.
        ulong hash = 14695981039346656037;
        foreach (byte b in System.Text.Encoding.UTF8.GetBytes(pilePath))
        {
            hash = unchecked((hash ^ b) * 1099511628211);
        }
        foreach (byte b in System.BitConverter.GetBytes(index))
        {
            hash = unchecked((hash ^ b) * 1099511628211);
        }
        if (hash == 0)
        {
            hash = 1;
        }
        return hash;
    }

    // ---- editor preview ----

    /// <summary>Editor: what draws the pile (internal children, never saved in the scene).</summary>
    Node3D preview;
    double untilFileCheck;
    string previewPath;
    ulong previewModifiedTime;

    /// <summary>Editor: rebuilds the preview when the pile file changes (a Save in game) or the dropper is renamed or moved.</summary>
    void WatchPileFile(double delta)
    {
        untilFileCheck -= delta;
        if (untilFileCheck > 0)
        {
            return;
        }
        untilFileCheck = 1.0;
        // Exported fields changing don't tell the editor to look again, so the warnings are refreshed here too.
        UpdateConfigurationWarnings();
        string path = PilePath();
        ulong modified = 0;
        if (path != null && FileAccess.FileExists(path))
        {
            modified = FileAccess.GetModifiedTime(path);
        }
        if (path != previewPath || modified != previewModifiedTime)
        {
            RebuildPreview();
        }
    }

    /// <summary>Editor: one multimesh per mesh part per item kind, holding every item of that kind.</summary>
    void RebuildPreview()
    {
        if (preview != null)
        {
            preview.QueueFree();
            preview = null;
        }
        previewPath = PilePath();
        previewModifiedTime = 0;
        if (previewPath != null && FileAccess.FileExists(previewPath))
        {
            previewModifiedTime = FileAccess.GetModifiedTime(previewPath);
        }
        preview = new Node3D();
        AddChild(preview, false, InternalMode.Front);

        ItemPileData pile = LoadPileData();
        if (pile == null)
        {
            if (expectPile)
            {
                Label3D warning = new Label3D();
                warning.Text = $"No pile at {previewPath}";
                warning.Billboard = BaseMaterial3D.BillboardModeEnum.Enabled;
                warning.Modulate = Colors.OrangeRed;
                preview.AddChild(warning);
            }
            return;
        }

        for (int kind = 0; kind < pile.itemIDs.Count; kind++)
        {
            List<Transform3D> placed = new();
            for (int i = 0; i < pile.kinds.Length; i++)
            {
                if (pile.kinds[i] == kind)
                {
                    placed.Add(pile.transforms[i]);
                }
            }
            ItemInfo info = ItemInfo.Fetch(pile.itemIDs[kind]);
            PackedScene scene = info?.droppedScene;
            if (scene == null)
            {
                scene = GD.Load<PackedScene>("res://game/scenes/items/DefaultDroppedBox.tscn");
            }
            Godot.Collections.Dictionary description = ItemWorld.DescribeKind(scene, info);
            foreach (Godot.Collections.Dictionary part in description["parts"].AsGodotArray<Godot.Collections.Dictionary>())
            {
                Transform3D partOffset = part["transform"].AsTransform3D();
                MultiMesh multimesh = new MultiMesh();
                multimesh.TransformFormat = MultiMesh.TransformFormatEnum.Transform3D;
                multimesh.Mesh = part["mesh"].As<Mesh>();
                multimesh.InstanceCount = placed.Count;
                for (int i = 0; i < placed.Count; i++)
                {
                    multimesh.SetInstanceTransform(i, placed[i] * partOffset);
                }
                MultiMeshInstance3D drawn = new MultiMeshInstance3D();
                drawn.Multimesh = multimesh;
                if (part.ContainsKey("material_override"))
                {
                    drawn.MaterialOverride = part["material_override"].As<Material>();
                }
                preview.AddChild(drawn);
            }
        }
    }
}
