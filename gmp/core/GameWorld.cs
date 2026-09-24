using Godot;
using ImGuiNET;
using Nerdbank.MessagePack;
using PolyType;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

[GenerateShape]
public partial record WorldTickMessage
{
    public ulong tick = 0;
    public List<(ulong, byte[])> updates = new();
}

[GenerateShapeFor<List<(ulong,byte[])>>]
public partial class Witness;

[GenerateShapeFor<Type>]
public partial class Witness;

public partial class GameWorld : Node3D
{
    public static GameWorld instance;
    public static Node3D b3droot; 
    public static Dictionary<ulong, GMPObject> syncedObjs = new();
    public static WorldTickMessage pendingOutgoingTick = new();
    public static List<WorldTickMessage> pendingIncomingTicks = new();

    public static Dictionary<ulong, WorldTickMessage> mostRecentUpdates = new();

    public static MessagePackSerializer pack = new();
    
    private static int maxTickSize = 1024 //1kb
                            *50 //50kb
                            / Engine.PhysicsTicksPerSecond; //per second 
    private static int waitTicks = 0 ;
    private static int waitTickCount = 0;
    private static ulong mostRecentInboundTick;
    private static bool started = false;
    public static ulong tickNum = 0;
    public static bool displaySyncedObjectDebugInfo = false;
    public static MeshInstance3D gridMesh = new();
    public static int GridSize = 200;      // Total size of the grid edge
    public static float CellSize = 2f;  // Distance between lines
    public static Color GridColor = new Color(0.5f, 0.5f, 0.5f, 0.5f); // Gray with alpha
    public override void _Process(double delta)
    {
        if (displaySyncedObjectDebugInfo)
        {
            ImGui.Begin("Synced Objects");
            ImGui.Text($"Synced Objects: {syncedObjs.Count}");
            ImGui.Text($"Synced Objects with Auth: {syncedObjs.Count(o => o.Value.authority == Lobby.selfPeerID)}");
            foreach (var kvp in syncedObjs.OrderByDescending(e => e.Value.priorityAccumulator).ToList())
            {
                ImGui.Text($"ID: {kvp.Key} | Auth: {kvp.Value.authority} | Owner: {kvp.Value.owner} | Priority: {kvp.Value.priority} | Accumulator: {kvp.Value.priorityAccumulator}");
            }
            ImGui.End();
        }

    }
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
            
            b3droot = ClassDB.Instantiate("Box3DWorld").As<Node3D>();
            b3droot.Set("debug_draw", true);
            AddChild(b3droot);  
        }
    }
    public static Godot.Collections.Dictionary<string, Variant> Raycast(Vector3 from, Vector3 to)
    {
        return b3droot.Call("raycast", [from, to]).AsGodotDictionary<string, Variant>();
    }

    public static Godot.Collections.Array<Node> OverlapSphere(Vector3 center, float radius, int collisionMask = -1, int collisionLayer = -1)
    {
        return b3droot.Call("overlap_sphere", [center, radius, collisionMask, collisionLayer]).AsGodotArray<Node>();
    }
    private static void Instance_LobbyDonePreloadingEvent()
    {
        Init();

    }

    private static void Instance_LobbyDoneLoadingEvent()
    {
        instance.GetTree().Paused = false;
        started = true;
        if (Lobby.isHost)
        {
            //JumpAround();
        }
    }

    private static void Instance_ConnectedToHostEvent(ulong HostID)
    {
        Lobby.network.MessageReceivedEvent += OnMessageReceived;
    }



    private static void OnMessageReceived(ulong from, Channel ch, byte[] msg)
    {
        //we may need to batch these and apply them all at the start of the next tick idk
        if (ch!=Channel.GAME_State)
        {
            return;
        }
        WorldTickMessage tickmsg = pack.Deserialize<WorldTickMessage>(msg);
        if (mostRecentUpdates.TryGetValue(from, out WorldTickMessage currentRecentTick))
        {

            if (tickmsg.tick >= mostRecentUpdates[from].tick)
            {

                mostRecentUpdates[from] = tickmsg;
            }
            else
            {
                Logging.Warn("Mistimed tick, discarding! (If this is getting spammed lag compensation is broken)", "GameWorld");
                //mistimed tick, discard it I guess?
            }
        }
        else
        {
            mostRecentUpdates[from] = tickmsg;
        }


       // Logging.Log($"Got a tick for tick# {tick.tick}", "help");


    }
    private static GMPOInitData initDataNormalizer(GMPOInitData initData)
    {

        if (initData.id == 0)
        {
            initData.id = GenRandomID();
        }
        if (initData.authority == 0)
        {
            initData.authority = Lobby.selfPeerID;
        }
        if (initData.owner == 0)
        {
            initData.owner = Lobby.selfPeerID;
        }
        return initData;
    }

    private static Dictionary<string,GMPOInitData> childGMPORegisterGenerator(Node node, GMPOInitData initData)
    {
        Dictionary<string, GMPOInitData> childInit = new();
        foreach (Node c in node.FindChildren("*").ToList())
        {
            if (c is GMPObject gmpoChild)
            {
                childInit[node.GetPathTo(c)] = new GMPOInitData(
                    GenRandomID(), initData.authority, initData.owner,
                    gmpoChild.priority, gmpoChild.pauseable);
            }
        }
        return childInit;
    }
    public static ulong SpawnScene(string scenePath, Vector3 position = default, Vector3 rotation = default, GMPOInitData initData = new(), byte[] initState = null)
    {
        PackedScene pck = ResourceLoader.Load<PackedScene>(scenePath);
        Node node = pck.Instantiate();
        initData = initDataNormalizer(initData);
        Dictionary<string, GMPOInitData> childInit = childGMPORegisterGenerator(node, initData);
        node.Free();
        RPCManager.RPC(instance, "_SpawnScene", [scenePath, position, rotation, initData, initState, childInit]);
        return initData.id;
    }

    public static ulong SpawnNode(Type nodeType, Vector3 position = default, Vector3 rotation = default, GMPOInitData initData = new(), byte[] initState = null)
    {
        Node node = (Node)Activator.CreateInstance(nodeType);
        initData = initDataNormalizer(initData);

        Dictionary<string, GMPOInitData> childInit = childGMPORegisterGenerator(node, initData);
        node.Free();

        RPCManager.RPC(instance, "_SpawnNode", [nodeType,position, rotation, initData, initState, childInit]);

        return initData.id;
    }

    private void _SpawnNode(Type nodeType, Vector3 position, Vector3 rotation, GMPOInitData initData, byte[] initState = null, Dictionary<string, GMPOInitData> childInit = null)
    {
        Node node = (Node)Activator.CreateInstance(nodeType);
        _SpawnInternal(node, position, rotation, initData, initState, childInit);
    }

    private void _SpawnScene(string scenePath, Vector3 position, Vector3 rotation, GMPOInitData initData, byte[] initState = null, Dictionary<string, GMPOInitData> childInit = null)
    {
        Node node = ResourceLoader.Load<PackedScene>(scenePath).Instantiate();
        _SpawnInternal(node, position, rotation, initData, initState, childInit);
    }

    private void _SpawnInternal(Node node, Vector3 position, Vector3 rotation, GMPOInitData initData, byte[] initState = null, Dictionary<string, GMPOInitData> childInit = null)
    {
        node.Name = node.GetType().ToString() + initData.id.ToString();

            if (b3droot == null)
            {
                Logging.Error("attempted to spawn b3dbody with no b3droot!", "GameWorld");
            }
            else
            {
                b3droot.AddChild(node);
                if (node is GMPOBox3DBody box)
                {
                    box.Call("teleport", [new Transform3D(Basis.FromEuler(rotation), position)]);
                }
                else if (node is Node3D n)
                {
                    n.Position = position;

                    n.Rotation = rotation;

                }

            
        }



        if (node is GMPObject gmpo)
        {
            gmpo.Init(initData, initState);
            syncedObjs.Add(gmpo.id, gmpo);
        }
        else
        {
            Logging.Warn($"Spawned node of type {node.GetType()} is not a GMPObject. It will not be synced.", "GameWorld");
        }
        if (node is Node3D n3d)
        {
          //  GD.Print($"current pos {n3d.Position}, setting to {position}");
            n3d.Position = position;
            n3d.Rotation = rotation;
        }
        if (childInit != null)
        {
            foreach (Node c in node.FindChildren("*").ToList())
            {
                if (c is not GMPObject gmpoChild)
                {
                    continue;
                }
                string rel = node.GetPathTo(c);
                if (!childInit.TryGetValue(rel, out GMPOInitData ci))
                {
                    Logging.Warn($"No init data for child GMPObject at '{rel}' under {node}; it will not be synced.", "GameWorld");
                    continue;
                }
                gmpoChild.Init(ci, null);
                syncedObjs.Add(gmpoChild.id, gmpoChild);
            }
        }

    }
    public static void DespawnObject(ulong id)
    {
        RPCManager.RPC(instance, "_DespawnObject", [id]);
    }

    private void _DespawnObject(ulong id)
    {
        if (syncedObjs.TryGetValue(id, out GMPObject gmpo))
        {
            syncedObjs.Remove(id);
            (gmpo as Node)?.QueueFree();
        }
    }

    private static ulong GenRandomID()
    {
        return (ulong)Random.Shared.NextInt64();
    }


    public override void _PhysicsProcess(double delta)
    {
        if (waitTicks < waitTickCount)
        {
            waitTicks++;
            return;
        }
        else
        {
            waitTicks = 0;
        }
        if (started)
        {
            tickNum++;
        }
        else
        {
            return;
        }
        foreach (var kv in mostRecentUpdates)
        {
            foreach (var item in kv.Value.updates)
            {
                if (syncedObjs.TryGetValue(item.Item1, out GMPObject? gmpo))
                {
                    gmpo.ApplyStateUpdate(item.Item2);
                }
                else
                {
                    Logging.Warn("sync message for unknown object, ordering issue!", "GameWorld");
                }
            }
            mostRecentUpdates.Remove(kv.Key);
        }


        int tickSize = 0;
        List<GMPObject> toBeSynced = new();
        foreach (var entity in syncedObjs.Values)
        {
            if (entity.authority!=Lobby.selfPeerID)
            {
                //not mine hands off
                continue;
            }
            else if (entity.priority <=-1)
            {
                //sleepy boi hands off
                continue;
            }
            else
            {
                entity.priorityAccumulator += entity.priority;
            }
        }
        List<GMPObject> temp = syncedObjs.Values.OrderByDescending(e => e.priorityAccumulator).ToList();
        foreach (GMPObject e in temp)
        {
            if (e.authority == Lobby.selfPeerID && e.priorityAccumulator > 0)
            {
                byte[] update = e.GenerateStateUpdate();
                if (update == null || update.Length ==0)
                {
                    e.priorityAccumulator = 0;
                    continue;
                }
                if (tickSize + update.Length <= maxTickSize)
                {
                    tickSize += update.Length;
                    pendingOutgoingTick.updates.Add((e.id, update));
                    e.priorityAccumulator = 0;
                }
                else
                {
                    break;
                   Logging.Warn($"This update of size {update.Length} would exceed our tick size budget (at {tickSize} of {maxTickSize}. Stopping at {pendingOutgoingTick.updates.Count} updates. (If this is getting spammed something is broken)", "GameWorld");
                }
            }
        }
        if (pendingOutgoingTick.updates.Count > 0)
        {
            pendingOutgoingTick.tick = tickNum;
            Lobby.SendToAllExceptSelf(Channel.GAME_State, pack.Serialize<WorldTickMessage>(pendingOutgoingTick));
            pendingOutgoingTick = new();
        }

    }

    internal static void Preload(GameInfo gameInfo)
    {
        //This runs on all players. Use it to preload the shit from the gameinfo you know you'll need.
        //PackedScene pck = ResourceLoader.Load<PackedScene>("res://gmp/examples/SyncedLevelColors.tscn");
        //instance.AddChild(pck.Instantiate());
        //instance.GetTree().Paused = true;


        if (Lobby.isHost)
        {
            maxTickSize *= 4;
            string levelPath = GameResources.LevelsList[gameInfo.levelIdx].levelPath;
            SpawnScene(levelPath);


        }
    }
     
    internal static void Init()
    {
        //When this is called all players have completed the Preload function and are sitting staring at a loading screen.

        //Use it to slam a bunch of RPCs and other networked stuff before anyone else has a chance to do anything.
        PlayerSync init = new PlayerSync();
        init.controllingPeerID = Lobby.selfPeerID;
        init.isHuman = true;
        ulong pid = SpawnScene("res://game/player/FactoryPlayer.tscn", new Vector3(Random.Shared.Next(5), Random.Shared.Next(2,5), Random.Shared.Next(5)),default,default,GMPObject.serializer.Serialize(init));
        Lobby.SendToAllAndSelf(Channel.LOBBY_Control, [(byte)LobbyControlCode.DoneLoading]);
        

    }

    internal static void Claim(PhysicalFactoryItem item)
    {
        //throw new NotImplementedException();
    }
    public static void InitGrid()
    {
       
        // 1. Create a basic unlit material so the lines are visible without lighting
        var material = new OrmMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            VertexColorUseAsAlbedo = true,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha
        };

        // 2. Initialize the ImmediateMesh
        var immediateMesh = new ImmediateMesh();
        gridMesh.Mesh = immediateMesh;

        // 3. Begin drawing lines
        immediateMesh.SurfaceBegin(Mesh.PrimitiveType.Lines, material);

        float halfSize = (GridSize * CellSize) / 2.0f;

        // Draw parallel lines across the grid
        for (int i = 0; i <= GridSize; i++)
        {
            float offset = -halfSize + (i * CellSize);

            // Lines parallel to the Z axis (varying Z, constant X)
            immediateMesh.SurfaceSetColor(GridColor);
            immediateMesh.SurfaceAddVertex(new Vector3(offset, 0, -halfSize));
            immediateMesh.SurfaceAddVertex(new Vector3(offset, 0, halfSize));

            // Lines parallel to the X axis (varying X, constant Z)
            immediateMesh.SurfaceSetColor(GridColor);
            immediateMesh.SurfaceAddVertex(new Vector3(-halfSize, 0, offset));
            immediateMesh.SurfaceAddVertex(new Vector3(halfSize, 0, offset));
        }

        immediateMesh.SurfaceEnd();
        b3droot.AddChild(gridMesh);
    }
    public static void DrawGrid()
    {

    }
}