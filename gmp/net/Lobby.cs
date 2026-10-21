using Godot;
using ImGuiNET;
using Nerdbank.MessagePack;
using PolyType;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>What a <see cref="Channel.LOBBY_Control"/> message says (its first byte).</summary>
public enum LobbyControlCode : byte
{
    START = 0,
    DonePreloading = 1,
    DoneLoading = 3,
}

/// <summary>
/// The lobby (autoload): who is in the session and how to reach them, over a <see cref="Network"/> transport. Every
/// peer links directly to every other (a mesh): the host hands each newcomer the roster, and the newcomer dials the rest.
/// <para>
/// Starting a game: the host's <see cref="SendStartGame"/> sends the <see cref="GameInfo"/>, then START to everyone. Each
/// peer runs <c>GameWorld.Preload</c> (the host spawns the level) and says DonePreloading. Once every peer has,
/// <see cref="LobbyDonePreloadingEvent"/> runs <c>GameWorld.Init</c> (each peer spawns its player) and each says
/// DoneLoading. Once every peer has, <see cref="LobbyDoneLoadingEvent"/> starts the game (unpauses, drops the lobby UI).
/// A peer that joins a running game skips all that: the host sends it the world (GameWorld.Resync.cs).
/// </para>
/// </summary>
public partial class Lobby : Node
{
    public static bool displayLobbyDebugInfo = false;
    public static Lobby instance;

    /// <summary>The settings of the game being set up or played (level, starter items, save to load); the host's, sent to everyone.</summary>
    public static GameInfo gameInfo = new();
    /// <summary>The host's peer id (this peer's own when hosting); 0 until a joiner has linked to the host.</summary>
    public static ulong hostID = 0;
    /// <summary>The transport in use, or null outside a lobby.</summary>
    public static Network network;

    /// <summary>This peer's id: its Steam id, or a made-up one on LAN.</summary>
    public static ulong selfPeerID;
    /// <summary>This peer hosts the lobby: it decides the game settings and is the default authority (and the shop's and quests' only writer).</summary>
    public static bool isHost;

    /// <summary>Every known member, including this peer, by peer id.</summary>
    public static Dictionary<ulong, PlayerInfo> members = new();

    /// <summary>
    /// Running total of main-thread time in the transport's service() (Stopwatch ticks), for PerfDebug: receiving and
    /// handling every message (state receive is also counted on its own, GameWorld.receiveCost), and for ENet all the
    /// socket work, including actually sending what was queued since the last frame. Steam does its sending on its own
    /// threads.
    /// </summary>
    public static long serviceCost;

    /// <summary>A direct link to a peer is up (it is in <see cref="members"/> now).</summary>
    public static event Action<ulong> PeerConnectedEvent;
    /// <summary>A peer has gone (left, or its link timed out); it is out of <see cref="members"/> already.</summary>
    public static event Action<ulong> PeerDisconnectedEvent;
    /// <summary>A link to this peer dropped and was made again; it stayed a member, but messages in the gap were lost.</summary>
    public static event Action<ulong> PeerRepairedEvent;
    /// <summary>This peer is linked to the host (raised at once when hosting): the network can be listened to now.</summary>
    public static event Action<ulong> ConnectedToHostEvent;
    /// <summary>The host has gone.</summary>
    public static event Action<ulong> DisconnectedFromHostEvent;
    /// <summary>A member was added, removed or renamed.</summary>
    public static event Action LobbyMembersChangedEvent;
    /// <summary>The host sent new <see cref="gameInfo"/>.</summary>
    public static event Action LobbyGameInfoChangedEvent;
    /// <summary>Every peer has spawned its part of a new game, or a joiner has been sent the world: the game starts.</summary>
    public static event Action LobbyDoneLoadingEvent;
    /// <summary>Every peer has preloaded a new game: time to spawn (GameWorld.Init).</summary>
    public static event Action LobbyDonePreloadingEvent;

    /// <summary>How many members there are, this peer included.</summary>
    public static int MemberCount => members.Count;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        instance = this;
    }

    // Quitting the game (closing the window, or the console's quit) skips LeaveLobby: still tell the transport, so the
    // other peers hear this one left instead of waiting for its link to time out.
    public override void _ExitTree()
    {
        network?.Disconnect();
    }

    public override void _Process(double delta)
    {
        if (displayLobbyDebugInfo)
        {
            renderLobbyDebugInfo();
        }
        long serviceStart = System.Diagnostics.Stopwatch.GetTimestamp();
        network?.service();
        serviceCost += System.Diagnostics.Stopwatch.GetTimestamp() - serviceStart;
    }

    private void renderLobbyDebugInfo()
    {
        ImGui.Begin("debugui lobby", ref displayLobbyDebugInfo);
        ImGui.Text($"Network mode: {network?.GetType()}");
        ImGui.Text($"My Peer ID: {Lobby.selfPeerID}");
        ImGui.Text($"Lobby Host: {hostID} | Lobby Members: {members?.Count}");
        foreach(var member in members)
        {
            ImGui.Text($"   Name: {member.Value.Name} | ID: {member.Value.PeerID}");
        }
        ImGui.End();
    }

    // ---- lifecycle -------------------------------------------------------------

    private static void UseNetwork(Network net, ulong selfID, string selfName)
    {
        network = net;
        selfPeerID = selfID;

        net.MessageReceivedEvent += OnIncomingNetworkMessage;
        net.PeerConnectedEvent += OnPeerConnected;
        net.PeerDisconnectedEvent += OnPeerDisconnected;
        net.PeerRepairedEvent += OnPeerRepaired;

        members.Clear();

        AddOrUpdateMember(selfID, selfName, isHost: false);
    }

    /// <summary>Hosts a lobby over <paramref name="net"/> as <paramref name="selfID"/> (shown to others as <paramref name="selfName"/>).</summary>
    public static Error StartHost(Network net, ulong selfID, string selfName)
    {
        isHost = true;
        UseNetwork(net, selfID, selfName);
        AddOrUpdateMember(selfID, selfName, isHost: true);
        Error err = net.Host();
        LobbyMembersChangedEvent?.Invoke();
        Logging.Log($"hosting lobby as {selfName} (peer {selfID})", "NetworkSession");
        SendToAllExceptSelf(Channel.LOBBY_Roster, RosterBytes());
        ConnectedToHostEvent?.Invoke(selfID);
        return err;
    }

    /// <summary>Gets ready to join a lobby (listens, so later members can dial in); then <c>network.Connect</c> the host.</summary>
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

    /// <summary>Disconnects, clears the roster and resets the game world so a new lobby can be hosted or joined.</summary>
    public static void LeaveLobby()
    {
        GameWorld.ResetSession();
        if (network != null)
        {
            network.MessageReceivedEvent -= OnIncomingNetworkMessage;
            network.PeerConnectedEvent -= OnPeerConnected;
            network.PeerDisconnectedEvent -= OnPeerDisconnected;
            network.PeerRepairedEvent -= OnPeerRepaired;
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

        // Introduce ourselves on every direct link so names propagate even across links the host never
        // brokered. From the host this is the full roster, so the newcomer can mesh.
        Send(peerID, Channel.LOBBY_Roster, RosterBytes());

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

    private static void OnPeerRepaired(ulong peerID)
    {
        Logging.Log($"link to {NameFor(peerID)} ({peerID}) repaired", "Lobby");
        PeerRepairedEvent?.Invoke(peerID);
    }

    private static void OnIncomingNetworkMessage(ulong from, Channel ch, byte[] msg)
    {
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
        if (!members.TryGetValue(from, out PlayerInfo sender))
        {
            Logging.Warn($"DoneLoading from {from}, who isn't a member", "Lobby");
            return;
        }
        sender.doneLoading = true;
        foreach (var member in members)
        {
            if (!member.Value.doneLoading)
            {
                Logging.Log($"Waiting for {NameFor(member.Key)} to finish loading", "Lobby");
                return;
            }
        }
        Logging.Log($"All loading complete, releasing controls and dropping UI", "Lobby");
        // Every preload/load message for this game has now arrived, so it's safe to clear the flags for
        // the next game. (Clearing them on START would race: another peer's DonePreloading can arrive
        // before the host's START.)
        foreach (PlayerInfo member in members.Values)
        {
            member.donePreloading = false;
            member.doneLoading = false;
        }
        LobbyDoneLoadingEvent?.Invoke();
    }



    private static void OnDonePreloading(ulong from, byte[] data)
    {
        if (!members.TryGetValue(from, out PlayerInfo sender))
        {
            Logging.Warn($"DonePreloading from {from}, who isn't a member", "Lobby");
            return;
        }
        sender.donePreloading = true;
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
        gameInfo = serializer.Deserialize<GameInfo>(data);
        LobbyGameInfoChangedEvent?.Invoke();
    }

    private static void OnIncomingRosterNetMessage(ulong from, byte[] data)
    {
        PlayerInfo[] roster = serializer.Deserialize<PlayerInfo[], Witness>(data);
        foreach (var m in roster)
        {
            if (m.PeerID == selfPeerID)
                continue;
            AddOrUpdateMember(m.PeerID, m.Name, m.IsHost);
            network.Connect(m.PeerID);
        }
        LobbyMembersChangedEvent?.Invoke();
    }

    /// <summary>A peer that joined a running game has been sent the world (see GameWorld.Resync.cs): start it like a finished load.</summary>
    public static void FinishJoining()
    {
        LobbyDoneLoadingEvent?.Invoke();
    }

    /// <summary>Host: starts the game for every member with the current <see cref="gameInfo"/> (see the class summary).</summary>
    public static void SendStartGame()
    {
        Logging.Log($"Host is starting the game", "Lobby");
        SendGameInfoUpdate();
        instance.GetTree().CreateTimer(1f).Connect("timeout", Callable.From(() =>
        {
            SendToAllAndSelf(Channel.LOBBY_Control, [(byte)LobbyControlCode.START]);
        }));
    }

    /// <summary>Host: sends <see cref="gameInfo"/> to every other member (call after changing it).</summary>
    public static void SendGameInfoUpdate()
    {
        if (network == null)
            return;
        byte[] data = serializer.Serialize(gameInfo);
        SendToAllExceptSelf(Channel.LOBBY_GameInfo, data);
    }

    private static byte[] RosterBytes()
    {
        return serializer.Serialize<PlayerInfo[], Witness>(members.Values.ToArray());
    }

    // ---- app-facing send -------------------------------------------------------

    /// <summary>Sends to every member including this peer; the local copy is delivered synchronously, before this returns.</summary>
    public static Error SendToAllAndSelf(Channel ch, byte[] msg, int sendFlags = Network.k_nSteamNetworkingSend_Reliable)
    {
        if (network == null || members.Count == 0)
            return Error.Unconfigured;
        return BroadcastToMembers(true, ch, msg, sendFlags);
    }

    /// <summary>Sends to every member except this peer.</summary>
    public static Error SendToAllExceptSelf(Channel ch, byte[] msg, int sendFlags = Network.k_nSteamNetworkingSend_Reliable)
    {
        if (network == null || members.Count <= 1)
            return Error.Unconfigured;
        return BroadcastToMembers(false, ch, msg, sendFlags);
    }

    // Recipient lists, reused rather than allocated per send (state sync broadcasts every tick). One per nesting
    // level: delivering to this peer runs its handlers synchronously, and they may broadcast again mid-send.
    private static readonly List<List<ulong>> recipientBuffers = new();
    private static int recipientDepth;

    private static Error BroadcastToMembers(bool includeSelf, Channel ch, byte[] msg, int sendFlags)
    {
        if (recipientDepth == recipientBuffers.Count)
        {
            recipientBuffers.Add(new List<ulong>());
        }
        List<ulong> recipients = recipientBuffers[recipientDepth++];
        try
        {
            foreach (ulong id in members.Keys)
            {
                if (includeSelf || id != selfPeerID) recipients.Add(id);
            }
            return network.Broadcast(recipients, ch, msg, sendFlags);
        }
        finally
        {
            recipients.Clear();
            recipientDepth--;
        }
    }

    /// <summary>Sends to one member (sending to this peer's own id delivers synchronously).</summary>
    public static Error Send(ulong to, Channel ch, byte[] msg, int sendFlags = Network.k_nSteamNetworkingSend_Reliable)
    {
        if (network == null)
            return Error.Unconfigured;
        return network.Send(to, ch, msg, sendFlags);
    }

    // ---- helpers ---------------------------------------------------------------

    /// <summary>For the lobby's own messages (roster, game info).</summary>
    private static readonly MessagePackSerializer serializer = new();

    // Updates an existing entry in place: replacing it would wipe its loading flags whenever a roster arrives mid-load.
    private static void AddOrUpdateMember(ulong id, string name, bool isHost)
    {
        if (!members.TryGetValue(id, out PlayerInfo m))
        {
            m = new();
            m.PeerID = id;
            members[id] = m;
        }
        m.Name = name;
        m.IsHost = isHost;
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
