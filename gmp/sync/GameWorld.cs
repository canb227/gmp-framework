using Godot;
using FmodStudio = FMOD.Studio;

/// <summary>
/// Per-peer game-world singleton (each peer is authority for its own objects): owns the synced-object registry, the
/// physics world and per-tick network replication for GMPObjects during a running game session. This file holds core
/// lifecycle (autoload singleton, physics world init), Lobby transition event wiring, the kill plane, and the
/// framework-level Preload/Init/ResetSession entry points called during the lobby → game transition (game-specific
/// bootstrap logic lives in <see cref="GameBootstrap"/>).
/// </summary>
public partial class GameWorld : Node3D
{
    public static GameWorld instance;

    /// <summary>
    /// Our Box3D world (the GMPPhysicsWorld node, native/src/physics_world.h): every spawned scene goes under it, and
    /// <see cref="PhysicsWorld"/> calls into it. Stepped and synced from <see cref="_PhysicsProcess"/>.
    /// </summary>
    public static Node3D physics;

    /// <summary>
    /// After the physics world has stepped, each physics tick of a running game (paused: not at all). For game code that
    /// handles what happened in the step, e.g. item touches.
    /// </summary>
    public static event System.Action<double> PhysicsStepped;

    /// <summary>After <see cref="ClearWorld"/> has emptied the world (session end, or a restore about to rebuild it).</summary>
    public static event System.Action WorldCleared;

    /// <summary>True once every peer has loaded and the game is running; false in the lobby and menus.</summary>
    public static bool started { get; private set; }

    /// <summary>
    /// The clock every peer stamps its messages with: real time in milliseconds (UTC). Machines whose clocks set
    /// themselves from the internet agree to within tens of milliseconds, which is all "the newest message wins"
    /// needs. Every peer compares the same stamps, so they always agree on the winner even if a clock is off.
    /// </summary>
    public static ulong netTime => (ulong)System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    /// <summary>Solver threads for the physics world (Box3D starts and owns them).</summary>
    const int PhysicsWorkers = 4;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        instance = this;
        Lobby.ConnectedToHostEvent += Instance_ConnectedToHostEvent;
        Lobby.LobbyDoneLoadingEvent += Instance_LobbyDoneLoadingEvent;
        Lobby.LobbyDonePreloadingEvent += Instance_LobbyDonePreloadingEvent;
        Lobby.PeerConnectedEvent += OnPeerLinked;
        Lobby.PeerDisconnectedEvent += OnPeerUnlinked;
        Lobby.PeerRepairedEvent += OnPeerRepaired;

        // Full (gen2) collections then run in the background instead of stopping the game; gen0/gen1 collections
        // still pause it, so per-frame allocations should stay low. Needs background GC, which is on by default.
        System.Runtime.GCSettings.LatencyMode = System.Runtime.GCLatencyMode.SustainedLowLatency;
        Logging.Log($"GC latency mode: {System.Runtime.GCSettings.LatencyMode}", "GameWorld");
        if (!ClassDB.ClassExists("GMPPhysicsWorld"))
        {
            Logging.Error("GMPPhysicsWorld isn't registered: the addons/gmp_box3d extension didn't load (build native/).", "GameWorld");
            return;
        }
        physics = ClassDB.Instantiate("GMPPhysicsWorld").As<Node3D>();
        physics.Name = "Physics";
        // Read when it enters the tree, which makes the Box3D world. Box3D does best on performance cores only, so
        // stay well under the logical core count: with ~2700 awake on belts the host's step took 7 ms on one (2026-10-15).
        physics.Set("worker_count", PhysicsWorkers);
        physics.Set("item_layer", PhysicsWorld.ItemLayer);
        // GameWorld itself runs while paused (it carries the network); it steps the world only while the tree runs.
        AddChild(physics);
        Logging.Log($"Physics world made, {PhysicsWorkers} solver workers", "GameWorld");
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
    /// Bodies this peer simulates that fall below this height are despawned: anything that leaves the level (pushed
    /// through a floor, off an edge) would otherwise fall forever, awake, simulated and synced. Below any level's lowest
    /// floor (the test facility's chasm is about 40 m deep).
    /// </summary>
    public static float KillPlaneY = -200;

    /// <summary>Bodies checked against <see cref="KillPlaneY"/> per tick; a pass over 5000 takes under half a second.</summary>
    const int KillChecksPerTick = 200;
    static readonly System.Collections.Generic.List<GMPOBody> killChecks = new();
    static readonly System.Collections.Generic.List<(ulong id, Vector3 position)> killed = new();
    static int nextKillCheck;

    /// <summary>
    /// Despawns a few of this peer's dynamic node bodies each tick if they've fallen out of the world. (Loose items,
    /// which aren't GMPObjects, are the game's to despawn: see PhysicsWorld.ItemsBelow.)
    /// </summary>
    static void CheckKillPlane()
    {
        if (nextKillCheck >= killChecks.Count)
        {
            nextKillCheck = 0;
            killChecks.Clear();
            foreach (GMPObject obj in syncedObjs.Values)
            {
                if (obj is GMPOBody body && body.authority == Lobby.selfPeerID && body.bodyType == BodyTypeEnum.Dynamic)
                {
                    killChecks.Add(body);
                }
            }
        }
        int end = Mathf.Min(nextKillCheck + KillChecksPerTick, killChecks.Count);
        for (; nextKillCheck < end; nextKillCheck++)
        {
            GMPOBody body = killChecks[nextKillCheck];
            if (IsInstanceValid(body) && !body.IsQueuedForDeletion() && body.authority == Lobby.selfPeerID && body.GlobalPosition.Y < KillPlaneY)
            {
                killed.Add((body.id, body.GlobalPosition));
            }
        }
        foreach ((ulong id, Vector3 position) in killed)
        {
            // Where it is now (x and z tell where it fell out): a body that leaves the level in play points at a hole.
            Logging.Warn($"Despawned {(syncedObjs[id] as Node)?.Name}: it fell below the kill plane (y {KillPlaneY}), now at {position}", "GameWorld");
            DespawnObject(id);
        }
        killed.Clear();
    }

    /// <summary>
    /// Collision layer for bodies that world queries should never hit. Excluded from <see cref="PhysicsWorld.Raycast"/>
    /// and the overlap tests by default.
    /// </summary>
    public const long QueryHiddenLayer = 1L << 30;

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
        if (Lobby.isHost && !string.IsNullOrEmpty(Lobby.gameInfo.savePath))
        {
            LoadFromFile(Lobby.gameInfo.savePath);
        }
    }

    private static void Instance_ConnectedToHostEvent(ulong HostID)
    {
        Lobby.network.MessageReceivedEvent += OnMessageReceived;
    }

    internal static void Preload(GameInfo gameInfo)
    {
        // Runs on every peer when the host starts the game: load what the game info says will be needed.
        // The physics world needs to know whose items are whose before the level (and anything in it) spawns.
        physics.Call(setSelfPeerName, Lobby.selfPeerID);
        physics.Call(setSendIntervalName, SendInterval);
        GameBootstrap.Preload(gameInfo);
    }

    internal static void Init()
    {
        // Every peer has finished Preload and is on the loading screen.
        GameBootstrap.Init();
        Lobby.SendToAllAndSelf(Channel.LOBBY_Control, [(byte)LobbyControlCode.DoneLoading]);
    }

    /// <summary>
    /// Clears all per-session world state (spawned nodes, registries, pending sync data) and the game's
    /// (<see cref="GameBootstrap.ResetSession"/>), and re-pauses the tree, so the next lobby/game in this process
    /// starts clean. Called from
    /// <see cref="Lobby.LeaveLobby"/>.
    /// </summary>
    public static void ResetSession()
    {
        ClearWorld();
        ResetResync();
        GameBootstrap.ResetSession();
        started = false;
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
        if (physics != null)
        {
            PhysicsWorld.ClearItems();
        }
        BuildGrid.ResetSession();
        ResetStateSync();
        WorldCleared?.Invoke();
    }
}
