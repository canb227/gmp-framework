using ENet;
using Godot;
using Nerdbank.MessagePack;
using PolyType;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

/*
 * Lobby flow
 * Host clicks Start Game in a Lobby 
 * Lobby.SendStartGame() is called on the host, a GameInfo update is sent out, then a START message is sent to everyone (including self)
 * The START message triggers the Lobby.OnStartGame() method on all peers, which starts GameWorld.Preload(), loading the assets we know we'll
 *      need (from gameInfo) and sends a DonePreloading message to everyone (including self) when done loading
 * Once all players have finished Preloading, the Lobby.LobbyDonePreloadingEvent is triggered, which the GameWorld listens for to run GameWorld.Init()
 * Everyone starts spawning shit. The host spawns the placeholders from the scene, players spawn their own player controller.
 * Once everyone has finished spawning shit, the Lobby.LobbyDoneLoadingEvent is triggered, at which point the game starts for real (is unpaused) and the lobby UI is dropped.
 */


public enum LobbyControlCode : byte
{
    START = 0,
    DonePreloading = 1,
    UNREADY = 2,
    DoneLoading = 3,
}
public partial class Lobby : Node
{
    public static Lobby instance;
    
    public static GameInfo gameInfo = new();
    public static ulong hostID = 0;
    public static Network network;

    public static ulong selfPeerID;
    public static bool isHost;

    // Every known member, including self, keyed by peerID.
    public static Dictionary<ulong, PlayerInfo> members = new();

    private static string _selfName = "Peer";

    private static List<(ulong, int)> outgoingTracker = new();
    private static List<(ulong, int)> incomingTracker = new();
    public static int incomingBandwidth = 0;
    public static int outgoingBandwidth = 0;

    public delegate void NewPeerConnected(ulong peerID);
    public delegate void PeerDisconnected(ulong peerID);
    public delegate void DisconnectedFromHost(ulong HostID);
    public delegate void ConnectedToHost(ulong HostID);
    public delegate void LobbyMembersChanged();
    public delegate void LobbyMessage(ulong from, Channel ch, byte[] msg);
    public delegate void LobbyGameInfoChanged();
    public delegate void LobbyDoneLoading();
    public delegate void LobbyDonePreloading();

    public static event NewPeerConnected PeerConnectedEvent;
    public static event PeerDisconnected PeerDisconnectedEvent;
    public static event ConnectedToHost ConnectedToHostEvent;
    public static event DisconnectedFromHost DisconnectedFromHostEvent;
    public static event LobbyMembersChanged LobbyMembersChangedEvent;   // any add/remove/rename
    public static event LobbyMessage LobbyMessageEvent;      // app payloads (chat, game data)
    public static event LobbyGameInfoChanged LobbyGameInfoChangedEvent;

    public static event LobbyDoneLoading LobbyDoneLoadingEvent;
    public static event LobbyDonePreloading LobbyDonePreloadingEvent;

    public static int MemberCount => members.Count;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        instance = this;
    }

    public double timerMax = 2;
    public double timer = 0;
    public override void _Process(double delta)
    {
        network?.service();
        foreach (var item in outgoingTracker.ToList())
        {
            if (Time.GetTicksMsec()-item.Item1> timer * 1000)
            {
                outgoingTracker.Remove(item);
            }
        }
        foreach (var item in incomingTracker.ToList())
        {
            if (Time.GetTicksMsec() - item.Item1 > timer*1000)
            {
                incomingTracker.Remove(item);
            }
        }
        incomingBandwidth = incomingTracker.Sum(kv  => kv.Item2);
        outgoingBandwidth = outgoingTracker.Sum(kv => kv.Item2);

        timer += delta;
        if (timer>timerMax)
        {
            Logging.Log($"{selfPeerID}: in: {incomingBandwidth/ (int)timerMax /1000} KB/s | out: {outgoingBandwidth/ (int)timerMax /1000} KB/s", "Lobby");
            timer = 0;
        }

    }

    // ---- lifecycle -------------------------------------------------------------

    private static void UseNetwork(Network net, ulong selfID, string selfName)
    {
        network = net;
        selfPeerID = selfID;
        _selfName = selfName;

        net.MessageReceivedEvent += OnIncomingNetworkMessage;
        net.PeerConnectedEvent += OnPeerConnected;
        net.PeerDisconnectedEvent += OnPeerDisconnected;

        members.Clear();

        AddOrUpdateMember(selfID, selfName, isHost: false);
    }

    public static Error StartHost(Network net, ulong selfID, string selfName)
    {
        isHost = true;
        UseNetwork(net, selfID, selfName);
        hostID = selfID;
        PlayerInfo self = new();
        self.PeerID = selfID;
        self.Name= selfName;
        self.IsHost = isHost;
        members[selfID] = self;
        Error err = net.Host();
        LobbyMembersChangedEvent?.Invoke();
        Logging.Log($"hosting lobby as {selfName} (peer {selfID})", "NetworkSession");
        BlastRoster();
        ConnectedToHostEvent?.Invoke(selfID);
        return err;
    }


    public static Error StartJoin(Network net, ulong selfID, string selfName)
    {
        isHost = false;
        hostID = 0;
        UseNetwork(net, selfID, selfName);
        Error err = net.Host(); // bind so later members can dial us
        LobbyMembersChangedEvent?.Invoke();
        Logging.Log($"joining lobby as {selfName} (peer {selfID})", "NetworkSession");
        return err;
    }


    public static void LeaveLobby()
    {
        if (network != null)
        {
            network.MessageReceivedEvent -= OnIncomingNetworkMessage;
            network.PeerConnectedEvent -= OnPeerConnected;
            network.PeerDisconnectedEvent -= OnPeerDisconnected;
            network.Disconnect();
            network = null;
        }

        members.Clear();
        hostID = 0;
        isHost = false;
        selfPeerID = 0;
        Logging.Log("left lobby — network and roster state reset", "NetworkSession");
    }



    private static void OnPeerConnected(ulong peerID)
    {
        bool discoveredHost = false;
        if (!isHost && hostID == 0)
        {
            // For a joiner, the first link established is to the host we dialed.
            hostID = peerID;
            discoveredHost = true;
        }

        AddOrUpdateMember(peerID, NameFor(peerID), isHost: peerID == hostID);

        // Introduce ourselves (display name) on every direct link so names propagate
        // even across links the host never brokered.
        SendRoster(peerID);
        // The host answers a new link with the full roster so the newcomer can mesh.
        if (isHost)
            SendRoster(peerID);

        PeerConnectedEvent?.Invoke(peerID);
        if (discoveredHost)
            ConnectedToHostEvent?.Invoke(peerID);
        LobbyMembersChangedEvent?.Invoke();
    }

    private static void OnPeerDisconnected(ulong peerID)
    {
        members.Remove(peerID);
        if (peerID == hostID)
            DisconnectedFromHostEvent?.Invoke(peerID);
        PeerDisconnectedEvent?.Invoke(peerID);
        LobbyMembersChangedEvent?.Invoke();
    }

    private static void OnIncomingNetworkMessage(ulong from, Channel ch, byte[] msg)
    {
        incomingTracker.Add((Time.GetTicksMsec(), msg.Length));

        switch (ch)
        {
            case Channel.LOBBY_Roster:
                OnIncomingRosterNetMessage(from, msg);
                break;
            case Channel.LOBBY_GameInfo:
                OnIncomingGameInfoNetMessage(from, msg);
                break;
            case Channel.LOBBY_Control:
                OnIncomingLobbyControlNetMessage(from, msg);
                break;
            default:
                break;
        }
    }

    private static void OnIncomingLobbyControlNetMessage(ulong from, byte[] msg)
    {
        switch ((LobbyControlCode)msg[0])
        {
            case LobbyControlCode.DoneLoading:
                OnDoneLoading(from, msg);
                break;
            case LobbyControlCode.DonePreloading:
                OnDonePreloading(from, msg);
                break;
            case LobbyControlCode.START:
                OnStartGame(from, msg);
                break;
            default:
                Logging.Log($"Unknown lobby control code {msg[0]} from {from}", "Lobby");
                break;
        }
    }

    private static void OnDoneLoading(ulong from, byte[] msg)
    {
        //members[from].doneLoading = true;
        //foreach (var member in members)
        //{
        //    if (!member.Value.doneLoading)
        //    {
        //        return;
        //    }
        //}
        Logging.Log($"All loading complete, releasing controls and dropping UI", "Lobby");
        LobbyDoneLoadingEvent?.Invoke();
    }



    private static void OnDonePreloading(ulong from, byte[] data)
    {
        members[from].donePreloading = true;
        foreach (var member in members)
        {
            if (!member.Value.donePreloading)
            {
                Logging.Log($"Waiting for {NameFor(member.Key)} to finish preloading", "Lobby");    
                return;
            }
        }
        Logging.Log($"All peers are preloaded", "Lobby");
        LobbyDonePreloadingEvent?.Invoke();
    }

    private static void OnStartGame(ulong from, byte[] data)
    {
        //start preloading the game scene and pass the game info to it
        GameWorld.Preload(gameInfo);


        //now we're done preloading - the world should be ready to accept RPCs
        Logging.Log($"Peer {selfPeerID} is done preloading", "Lobby");
        SendToAllAndSelf(Channel.LOBBY_Control, [(byte)LobbyControlCode.DonePreloading]);
    }  

    private static void OnIncomingGameInfoNetMessage(ulong from, byte[] data)
    {
        MessagePackSerializer s = new();
        GameInfo gi = s.Deserialize<GameInfo>(data);
        gameInfo = gi;
        LobbyGameInfoChangedEvent?.Invoke();
    }

    private static void OnIncomingRosterNetMessage(ulong from, byte[] data)
    {
        MessagePackSerializer s = new();
        PlayerInfo[] members =s.Deserialize<PlayerInfo[],Witness>(data);
        foreach (var m in members)
        {
            if (m.PeerID == selfPeerID)
                continue;
            AddOrUpdateMember(m.PeerID, m.Name, m.IsHost);
            network.Connect(m.PeerID);
        }
        LobbyMembersChangedEvent?.Invoke();
    }

    public static void SendStartGame()
    {
        //gameInfo.Level = GameResources.LevelsList[0];
        Logging.Log($"Host is starting the game", "Lobby");
        SendGameInfoUpdate();
        instance.GetTree().CreateTimer(1f).Connect("timeout", Callable.From(() =>
        {
            SendToAllAndSelf(Channel.LOBBY_Control, [(byte)LobbyControlCode.START]);
        }));

    }

    public static void SendGameInfoUpdate()
    {
        if (network == null)
            return;
        MessagePackSerializer s = new();
        byte[] data = s.Serialize(gameInfo);
        SendToAllExceptSelf(Channel.LOBBY_GameInfo, data);
    }

    private static void SendRoster(ulong to)
    {
        MessagePackSerializer s = new();
        byte[] data = s.Serialize<PlayerInfo[],Witness>(members.Values.ToArray());
        Send(to, Channel.LOBBY_Roster, data);
    }

    private static void BlastRoster()
    {
        MessagePackSerializer s = new();
        byte[] data = s.Serialize<PlayerInfo[], Witness>(members.Values.ToArray());
        SendToAllExceptSelf(Channel.LOBBY_Roster, data);
    }

    // ---- app-facing send -------------------------------------------------------

    public static Error SendToAllAndSelf(Channel ch, byte[] msg, int sendFlags = Network.k_nSteamNetworkingSend_Reliable, double fakelagSeconds = 0)
    {
        if (network == null || members.Count == 0)
            return Error.Unconfigured;
        outgoingTracker.Add((Time.GetTicksMsec(),msg.Length * members.Count));
        return network.Broadcast(new List<ulong>(members.Keys), ch, msg, sendFlags);
    }

    public static Error SendToAllExceptSelf(Channel ch, byte[] msg, int sendFlags = Network.k_nSteamNetworkingSend_Reliable, double fakelagSeconds = 0)
    {
        if (network == null || members.Count <= 1)
            return Error.Unconfigured;
        outgoingTracker.Add((Time.GetTicksMsec(), msg.Length * (members.Count-1)));
        return network.Broadcast(members.Keys.Where(id => id != selfPeerID).ToList(), ch, msg, sendFlags);
    }

    public static Error Send(ulong to, Channel ch, byte[] msg, int sendFlags = Network.k_nSteamNetworkingSend_Reliable, double fakelagSeconds = 0)
    {
        if (network == null)
            return Error.Unconfigured;
        outgoingTracker.Add((Time.GetTicksMsec(), msg.Length));
        return network.Send(to, ch, msg, sendFlags);
    }

    // ---- helpers ---------------------------------------------------------------

    private static void AddOrUpdateMember(ulong id, string name, bool isHost)
    {
        PlayerInfo m = new();
        m.PeerID = id;
        m.Name = name;
        m.IsHost = isHost;
        members[id] = m;
        if (m.IsHost)
        {
            hostID = id;
        }
        
    }

    private static string NameFor(ulong id)
    {
        return members.TryGetValue(id, out var m) && !string.IsNullOrEmpty(m.Name) ? m.Name : "";
    }
}
