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
        GetTree().Paused = true;
        Logging.Log($"Atempting b3d init: {ClassDB.ClassExists("Box3DWorld")}", "GameWorld");
        if (ClassDB.ClassExists("Box3DWorld"))
        {

            // Attach the typed wrapper script, then re-fetch the object: the C# instance changes with the script.
            Node3D world = ClassDB.Instantiate("Box3DWorld").As<Node3D>();
            ulong worldId = world.GetInstanceId();
            world.SetScript(GD.Load<Script>("res://gmp/objects/GMPOBox3DWorld.cs"));
            b3droot = (GMPOBox3DWorld)InstanceFromId(worldId);
            b3droot.debugDraw = false;
            AddChild(b3droot);
            // Box3D reports sleep per world, not per body; hand it to the body (it has no matching wake signal).
            b3droot.BodyFellAsleep += body => (body as GMPOBox3DBody)?.OnFellAsleep();
        }
    }

    /// <summary>
    /// Collision layer for bodies that world queries should never hit (e.g. placement-preview sensors).
    /// Excluded from <see cref="Raycast"/> by default.
    /// </summary>
    public const long QueryHiddenLayer = 1L << 30;

    /// <summary>Raycasts against the Box3D physics world. By default ignores <see cref="QueryHiddenLayer"/>.</summary>
    public static Godot.Collections.Dictionary Raycast(Vector3 from, Vector3 to, long collisionMask = ~QueryHiddenLayer)
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
        instance.GetTree().Paused = false;
        started = true;
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
        foreach (Node root in spawnedRoots)
        {
            if (IsInstanceValid(root))
            {
                root.QueueFree();
            }
        }
        spawnedRoots.Clear();
        syncedObjs.Clear();
        heldBy.Clear();
        BuildGrid.ResetSession();
        ResetStateSync();
        tickNum = 0;
        started = false;
        maxTickSize = baseMaxTickSize;
        if (instance != null)
        {
            instance.GetTree().Paused = true;
        }
    }

}
