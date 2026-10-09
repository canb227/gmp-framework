using Godot;

/// <summary>
/// Per-peer game-world singleton (each peer is authority for its own objects): owns the synced-object registry, Box3D physics root, and
/// per-tick network replication for GMPObjects during a running game session. This file holds
/// core lifecycle (autoload singleton, Box3D init), Lobby transition event wiring, spatial queries
/// (Raycast/OverlapSphere), and the framework-level Preload/Init/ResetSession entry points called during
/// the lobby → game transition (game-specific bootstrap logic lives in <see cref="GameBootstrap"/>).
/// </summary>
public partial class GameWorld : Node3D
{
    public static GameWorld instance;
    public static GMPOBox3DWorld b3droot;

    private static bool started = false;
    public static ulong tickNum = 0;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        instance = this;
        Lobby.ConnectedToHostEvent += Instance_ConnectedToHostEvent;
        Lobby.LobbyDoneLoadingEvent += Instance_LobbyDoneLoadingEvent;
        Lobby.LobbyDonePreloadingEvent += Instance_LobbyDonePreloadingEvent;
        Lobby.PeerConnectedEvent += OnPeerLinked;
        Lobby.PeerDisconnectedEvent += OnPeerUnlinked;
        GetTree().Paused = true;
        // Full (gen2) collections then run in the background instead of stopping the game; gen0/gen1 collections
        // still pause it, so per-frame allocations should stay low. Needs background GC, which is on by default.
        System.Runtime.GCSettings.LatencyMode = System.Runtime.GCLatencyMode.SustainedLowLatency;
        Logging.Log($"GC latency mode: {System.Runtime.GCSettings.LatencyMode}", "GameWorld");
        Logging.Log($"Atempting b3d init: {ClassDB.ClassExists("Box3DWorld")}", "GameWorld");
        if (ClassDB.ClassExists("Box3DWorld"))
        {
            // Attach the typed wrapper script, then re-fetch the object: the C# instance changes with the script.
            Node3D world = ClassDB.Instantiate("Box3DWorld").As<Node3D>();
            ulong worldId = world.GetInstanceId();
            world.SetScript(GD.Load<Script>("res://gmp/objects/GMPOBox3DWorld.cs"));
            b3droot = (GMPOBox3DWorld)InstanceFromId(worldId);
            b3droot.debugDraw = false;
            // GameWorld itself runs while paused (it carries the network); physics stops with the tree.
            b3droot.ProcessMode = ProcessModeEnum.Pausable;
            // Solve each step on Box3D's step thread while the engine renders; results land at the next tick.
            // Any Box3D call (body API, raycasts, queries) waits for an in-flight step, so make them from
            // _PhysicsProcess, where the step has had a whole frame to finish, not from _Process.
            b3droot.asyncStep = false;
            // Solver threads (Box3D starts and owns them). Box3D does best on performance cores only, so stay well
            // under the logical core count; at 2000 bodies the solver was too light (~2 ms) to measure a gain.
            b3droot.workerCount = 1;
            AddChild(b3droot);
            Logging.Log($"Box3D solver workers: {b3droot.workerCount}", "GameWorld");
            // Box3D reports sleep per world, not per body; hand it to the body (it has no matching wake signal).
            b3droot.BodyFellAsleep += body => (body as GMPOBox3DBody)?.OnFellAsleep();
        }
    }

    /// <summary>Seconds between the scheduled gen0 collections (see <see cref="_Process"/>).</summary>
    public const double GcInterval = 2.0;
    double gcTimer;

    public override void _Process(double delta)
    {
        // A gen0 pause grows with the Godot wrappers created since the last collection: every wrapper (GodotObject,
        // StringName, Array, Variant) registers a WeakReference in GodotSharp's DisposablesTracker, and the GC
        // clears those inside the pause, disposed or not. Left to itself gen0 fills its ~15 MB budget about every
        // 30 s and pauses 10-20 ms; collecting every couple of seconds keeps each pause under a millisecond.
        if (started)
        {
            gcTimer += delta;
            if (gcTimer >= GcInterval)
            {
                gcTimer = 0;
                System.GC.Collect(0, System.GCCollectionMode.Forced, blocking: true, compacting: false);
            }
        }
        TryFinishRestore();
        DrawDebugUI();
    }

    /// <summary>
    /// Collision layer for bodies that world queries should never hit (e.g. placement-preview sensors).
    /// Excluded from <see cref="Raycast"/> by default.
    /// </summary>
    public const long QueryHiddenLayer = 1L << 30;

    /// <summary>Raycasts against the Box3D physics world. By default ignores <see cref="QueryHiddenLayer"/>.</summary>
    public static RayHit Raycast(Vector3 from, Vector3 to, long collisionMask = ~QueryHiddenLayer)
    {
        return b3droot.Raycast(from, to, collisionMask);
    }

    /// <summary>Queries the Box3D physics world for nodes overlapping a sphere.</summary>
    public static Godot.Collections.Array<Node3D> OverlapSphere(Vector3 center, float radius, int collisionMask = -1, int collisionLayer = -1)
    {
        return b3droot.OverlapSphere(center, radius, collisionMask, collisionLayer);
    }
    private static void Instance_LobbyDonePreloadingEvent()
    {
        Init();
    }

    private static void Instance_LobbyDoneLoadingEvent()
    {
        // Collect the loading garbage now, while the loading screen is still up, so play starts with a clean heap
        // instead of paying for it in the first collection mid-game.
        System.GC.Collect(System.GC.MaxGeneration, System.GCCollectionMode.Forced, blocking: true, compacting: true);
        // Stay paused if the host already froze everyone (loading a save can start before this peer finished).
        instance.GetTree().Paused = frozenSyncId != 0;
        started = true;
        // Started from a save (chosen in the lobby): the host loads it into everyone now that all peers are in.
        if (Lobby.isHost && !string.IsNullOrEmpty(Lobby.gameInfo.saveName))
        {
            LoadFromFile(Lobby.gameInfo.saveName);
        }
    }

    private static void Instance_ConnectedToHostEvent(ulong HostID)
    {
        Lobby.network.MessageReceivedEvent += OnMessageReceived;
    }

    internal static void Preload(GameInfo gameInfo)
    {
        //This runs on all players. Use it to preload the shit from the gameinfo you know you'll need.
        maxTickSize = Lobby.isHost ? baseMaxTickSize * 4 : baseMaxTickSize;
        GameBootstrap.Preload(gameInfo);
    }

    internal static void Init()
    {
        //When this is called all players have completed the Preload function and are sitting staring at a loading screen.
        GameBootstrap.Init();
        Lobby.SendToAllAndSelf(Channel.LOBBY_Control, [(byte)LobbyControlCode.DoneLoading]);
    }

    /// <summary>
    /// Clears all per-session world state (spawned nodes, registries, pending sync data, tick counter)
    /// and re-pauses the tree, so the next lobby/game in this process starts clean. Called from
    /// <see cref="Lobby.LeaveLobby"/>.
    /// </summary>
    public static void ResetSession()
    {
        ClearWorld();
        ResetResync();
        tickNum = 0;
        started = false;
        maxTickSize = baseMaxTickSize;
        if (instance != null)
        {
            instance.GetTree().Paused = true;
        }
    }

    /// <summary>
    /// Frees every spawned root and clears the registries and pending sync data, leaving the network alone. Roots
    /// leave the tree at once (and are freed later), so a restore can respawn the same names in the same frame.
    /// </summary>
    public static void ClearWorld()
    {
        // Newest first: a root spawned under another root goes before its parent, and removing a parent's last
        // child is cheap.
        for (int i = spawnedRoots.Count - 1; i >= 0; i--)
        {
            Node root = spawnedRoots[i];
            if (IsInstanceValid(root))
            {
                root.GetParent()?.RemoveChild(root);
                root.QueueFree();
            }
        }
        spawnedRoots.Clear();
        syncedObjs.Clear();
        heldBy.Clear();
        BuildGrid.ResetSession();
        ResetStateSync();
    }
}
