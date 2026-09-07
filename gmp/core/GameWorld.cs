using Godot;
using ImGuiNET;
using Nerdbank.MessagePack;
using PolyType;
using System;
using System.Collections.Generic;
using System.Linq;
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

public partial class GameWorld : Node3D
{
    public static GameWorld instance;
    public static Dictionary<ulong, GMPObject> syncedObjs = new();
    public static WorldTickMessage pendingOutgoingTick = new();
    public static List<WorldTickMessage> pendingIncomingTicks = new();

    public static Dictionary<ulong, WorldTickMessage> mostRecentUpdates = new();

    public static MessagePackSerializer pack = new();
    
    private static int maxTickSize = 1024 //1kb
                            * 256 //256kb
                            / Engine.PhysicsTicksPerSecond; //per second 
    private static int waitTicks = 0 ;
    private static int waitTickCount = 0;
    private static ulong mostRecentInboundTick;
    private static bool started = false;
    public static ulong tickNum = 0;
    public override void _Process(double delta)
    {
       ImGui.Begin("Synced Objects");
       ImGui.Text($"Synced Objects: {syncedObjs.Count}");
       ImGui.Text($"Synced Objects with Auth: {syncedObjs.Count(o => o.Value.authority == Lobby.selfPeerID)}");
       foreach(var kvp in syncedObjs.OrderByDescending(e => e.Value.priorityAccumulator).ToList())
        {
            ImGui.Text($"ID: {kvp.Key} | Auth: {kvp.Value.authority} | Owner: {kvp.Value.owner} | Priority: {kvp.Value.priority} | Accumulator: {kvp.Value.priorityAccumulator}");
        }
       ImGui.End();
    }
    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        instance = this;
        Lobby.ConnectedToHostEvent += Instance_ConnectedToHostEvent;
        Lobby.LobbyDoneLoadingEvent += Instance_LobbyDoneLoadingEvent;
        Lobby.LobbyDonePreloadingEvent += Instance_LobbyDonePreloadingEvent;
        GetTree().Paused = true;
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


    private static ulong GenRandomID()
    {
        return (ulong)Random.Shared.NextInt64();
    }

    public static ulong Spawn3DSceneAt(string scenePath, Vector3 position = default, Vector3 rotation = default, GMPOInitData initData = new(), byte[] initState = null)
    {
        if (position == default)
        {
            position = Vector3.Zero;
        }
        if (rotation == default)
        {
            rotation = Vector3.Zero;
        }
        if (initData.id == 0)
        {
            initData.id = GenRandomID();
        }
        if (initData.authority==0)
        {
            initData.authority = Lobby.selfPeerID;
        }
        if (initData.owner==0)
        {
            initData.owner = Lobby.selfPeerID;
        }

        var childInit = new Dictionary<string, GMPOInitData>();
        Node probe = ResourceLoader.Load<PackedScene>(scenePath).Instantiate<Node>();
        foreach (Node c in probe.FindChildren("*").ToList())
        {
            if (c is GMPObject gmpoChild)
            {
                childInit[probe.GetPathTo(c)] = new GMPOInitData(
                    GenRandomID(), initData.authority, initData.owner,
                    gmpoChild.priority, gmpoChild.pauseable);
            }
        }
        probe.Free();

        RPCManager.RPC(instance, "_Spawn3DSceneAt",
            [scenePath, position, rotation, initData, initState, childInit]);
        return initData.id;
    }

    private static void _Spawn3DSceneAt(string scenePath, Vector3 position, Vector3 rotation, GMPOInitData initData, byte[] initState = null, Dictionary<string, GMPOInitData> childInit = null)
    {
        PackedScene pck = ResourceLoader.Load<PackedScene>(scenePath);
        Node n = pck.Instantiate();
        instance.AddChild(n);
        if (n is Node3D n3d)
        {
            n3d.Position = position;
            n3d.Rotation = rotation;
        }
        if (n is GMPObject gmpo)
        {
            gmpo.Init(initData,initState);
            syncedObjs.Add(gmpo.id, gmpo);
        }
        else
        {
            Logging.Warn($"Spawned scene at {scenePath} is not a GMPObject. It will not be synced.", "GameWorld");
        }
        if (childInit != null)
        {
            foreach (Node c in n.FindChildren("*").ToList())
            {
                if (c is not GMPObject gmpoChild)
                {
                    continue;
                }
                string rel = n.GetPathTo(c);
                if (!childInit.TryGetValue(rel, out GMPOInitData ci))
                {
                    Logging.Warn($"No init data for child GMPObject at '{rel}' under {scenePath}; it will not be synced.", "GameWorld");
                    continue;
                }
                gmpoChild.Init(ci, null);
                syncedObjs.Add(gmpoChild.id, gmpoChild);
            }
        }
    }

    private void _Register(string NodePath, GMPOInitData InitData, byte[] initState)
    {
        Node node = instance.GetNodeOrNull(NodePath);
        if (node == null)
        {
            Logging.Warn($"Registration failed: no node at {NodePath} (not spawned yet?). It will not be synced.", "GameWorld");
            return;
        }
        if (node is GMPObject gmpo)
        {
            gmpo.Init(InitData, initState);
            syncedObjs.Add(gmpo.id, gmpo);
        }
        else
        {
            Logging.Warn($"Registration Failed! Node at {NodePath} is not a GMPObject. (type is {node.GetType().Name}) It will not be synced.", "GameWorld");
        }
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
            string levelPath = gameInfo.LevelPath;
            Spawn3DSceneAt(levelPath);
        }
    }
     
    internal static void Init()
    {
        //When this is called all players have completed the Preload function and are sitting staring at a loading screen.

        //Use it to slam a bunch of RPCs and other networked stuff before anyone else has a chance to do anything.
        GodSync init = new GodSync();
        init.controllingPeerID = Lobby.selfPeerID;
        init.isHuman = true;
        ulong pid = Spawn3DSceneAt("res://game/God.tscn",default,default,default,GMPObject.serializer.Serialize(init));
        Lobby.SendToAllAndSelf(Channel.LOBBY_Control, [(byte)LobbyControlCode.DoneLoading]);
        

    }


}