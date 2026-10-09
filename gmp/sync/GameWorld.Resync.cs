using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>What a freeze is for: who restores, and from what.</summary>
public enum SyncMode : byte
{
    /// <summary>Every peer restores from the host's world (recovery from a desync).</summary>
    Resync,
    /// <summary>A peer joined a running game: only it restores, from the host's world.</summary>
    Join,
    /// <summary>Every peer restores from a save file.</summary>
    Load,
}

/// <summary>
/// GameWorld: freezing every peer to hand them the host's world, for mid-game joins, desync recovery and loading a
/// save. The host drives it:
/// <list type="number">
/// <item>The host sends <c>_Freeze</c>; every peer pauses the tree and broadcasts <c>_FrozenAck</c>.</item>
/// <item>Once every peer has acked to the host, the host snapshots (or reads the save) and sends <c>_Restore</c> to
/// the peers that rebuild.</item>
/// <item>A restoring peer waits until it is linked to every peer and has their ack (so nothing they sent before
/// freezing can arrive after it rebuilt), rebuilds, and sends <c>_RestoreDone</c>.</item>
/// <item>Once all are done, the host sends <c>_Unfreeze</c>; each peer makes sure it has a player and unpauses.</item>
/// </list>
/// Acks go to every peer, each carrying the freeze's number, and a peer that links up with another mid-freeze
/// (a joiner meshing in) acks to it too. Messages are reliable and ordered per link, so a peer's ack arrives after
/// everything it sent before freezing. Saving to a file needs no freeze: it's the host's own view.
/// </summary>
public partial class GameWorld
{
    /// <summary>Raised on every peer when a freeze starts (true) and ends (false).</summary>
    public static event Action<bool> FrozenChanged;

    /// <summary>True on the host while a freeze it started is under way.</summary>
    public static bool syncing { get; private set; }

    /// <summary>The freeze this peer is in (0 when not frozen); numbers count up per session on the host.</summary>
    static ulong frozenSyncId;
    static ulong nextSyncId;

    // Host
    static SyncMode syncMode;
    static ulong joiningPeer;
    static byte[] loadData;
    static bool restoreSent;
    static readonly HashSet<ulong> awaitingDone = new();
    static readonly Queue<ulong> pendingJoins = new();

    // Every peer
    /// <summary>Peers this one has a direct link to.</summary>
    static readonly HashSet<ulong> linkedPeers = new();
    /// <summary>The newest freeze each peer has acked to this one.</summary>
    static readonly Dictionary<ulong, ulong> ackedSync = new();
    /// <summary>A received restore waiting for its peers' acks: the data and the peers frozen for it.</summary>
    static byte[] pendingRestore;
    static ulong[] pendingRestorePeers;

    static void ResetResync()
    {
        syncing = false;
        restoreSent = false;
        frozenSyncId = 0;
        nextSyncId = 0;
        loadData = null;
        awaitingDone.Clear();
        pendingJoins.Clear();
        linkedPeers.Clear();
        ackedSync.Clear();
        pendingRestore = null;
        pendingRestorePeers = null;
    }

    // ---- host entry points ------------------------------------------------------

    /// <summary>Host: every peer rebuilds its world from the host's. False if one can't start now.</summary>
    public static bool Resync()
    {
        return BeginFreeze(SyncMode.Resync, 0);
    }

    /// <summary>Host: every peer rebuilds its world from save <paramref name="name"/>. False if it can't be read.</summary>
    public static bool LoadFromFile(string name)
    {
        byte[] data = ReadSaveFile(name);
        if (data == null)
        {
            return false;
        }
        loadData = data;
        return BeginFreeze(SyncMode.Load, 0);
    }

    /// <summary>Host: writes the current world to a new save, named by the time; returns the name, or null on failure.</summary>
    public static string SaveToFile()
    {
        if (!Lobby.isHost || !started)
        {
            return null;
        }
        string name = "save_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
        return WriteSaveFile(name, GameSave.Capture(false)) ? name : null;
    }

    static bool BeginFreeze(SyncMode mode, ulong joiner)
    {
        if (!Lobby.isHost || !started)
        {
            Logging.Warn($"{mode} needs a running game, started by the host", "GameWorld");
            return false;
        }
        if (syncing)
        {
            Logging.Warn($"{mode} refused: a freeze is already under way", "GameWorld");
            return false;
        }
        syncing = true;
        restoreSent = false;
        syncMode = mode;
        joiningPeer = joiner;
        Logging.Log($"Freezing every peer for {mode}", "GameWorld");
        RPCManager.RPC(instance, nameof(_Freeze), [++nextSyncId]);
        return true;
    }

    // ---- every peer ---------------------------------------------------------------

    [RPC(requireAuthority = true)]
    private void _Freeze(ulong syncId)
    {
        frozenSyncId = syncId;
        GetTree().Paused = true;
        FrozenChanged?.Invoke(true);
        RPCManager.RPC(instance, nameof(_FrozenAck), [syncId]);
    }

    [RPC]
    private void _FrozenAck(ulong syncId)
    {
        ackedSync[RPCManager.sender] = Math.Max(ackedSync.GetValueOrDefault(RPCManager.sender), syncId);
        CheckHostProgress();
        TryFinishRestore();
    }

    /// <summary>Host: moves the freeze on once its step is complete. Called whenever an ack, a done or a departure comes in.</summary>
    static void CheckHostProgress()
    {
        if (!Lobby.isHost || !syncing)
        {
            return;
        }
        if (!restoreSent)
        {
            if (syncMode == SyncMode.Join && !Lobby.members.ContainsKey(joiningPeer))
            {
                RPCManager.RPC(instance, nameof(_Unfreeze), []); // the joiner left before it got the world
            }
            else if (AllAcked(Lobby.members.Keys))
            {
                SendRestore();
            }
        }
        else if (awaitingDone.Count == 0)
        {
            RPCManager.RPC(instance, nameof(_Unfreeze), []);
        }
    }

    /// <summary>True once every listed peer still in the lobby is linked to this one and has acked the current freeze.</summary>
    static bool AllAcked(IEnumerable<ulong> peers)
    {
        foreach (ulong peer in peers)
        {
            if (!Lobby.members.ContainsKey(peer))
            {
                continue;
            }
            if ((peer != Lobby.selfPeerID && !linkedPeers.Contains(peer)) || ackedSync.GetValueOrDefault(peer) < frozenSyncId)
            {
                return false;
            }
        }
        return true;
    }

    // Host: everyone is frozen and all their earlier messages are in, so the host's world is final.
    static void SendRestore()
    {
        restoreSent = true;
        byte[] data = syncMode == SyncMode.Load ? GameSave.PrepareLoaded(loadData) : GameSave.Capture(syncMode == SyncMode.Resync);
        loadData = null;
        if (data == null)
        {
            Logging.Error($"{syncMode} abandoned: nothing to restore from", "GameWorld");
            RPCManager.RPC(instance, nameof(_Unfreeze), []);
            return;
        }
        List<ulong> restorers = syncMode == SyncMode.Join ? [joiningPeer] : Lobby.members.Keys.ToList();
        ulong[] peers = Lobby.members.Keys.ToArray();
        byte[] blob = Compress(data);
        Logging.Log($"{syncMode}: sending {blob.Length / 1024} KB ({data.Length / 1024} KB raw) to {restorers.Count} peer(s)", "GameWorld");
        awaitingDone.UnionWith(restorers);
        foreach (ulong peer in restorers)
        {
            RPCManager.RPCTo(peer, instance, nameof(_Restore), [blob, peers]);
        }
    }

    [RPC(requireAuthority = true)]
    private void _Restore(byte[] blob, ulong[] peers)
    {
        pendingRestore = blob;
        pendingRestorePeers = peers;
        TryFinishRestore();
    }

    /// <summary>Rebuilds from a received restore once every frozen peer is linked and has acked (polled from _Process too).</summary>
    static void TryFinishRestore()
    {
        if (pendingRestore == null || !AllAcked(pendingRestorePeers))
        {
            return;
        }
        byte[] blob = pendingRestore;
        pendingRestore = null;
        pendingRestorePeers = null;
        ulong start = Time.GetTicksMsec();
        GameSave.Apply(Decompress(blob), !started);
        Logging.Log($"Restored the world in {Time.GetTicksMsec() - start} ms", "GameWorld");
        RPCManager.RPCTo(Lobby.hostID, instance, nameof(_RestoreDone), []);
    }

    [RPC]
    private void _RestoreDone()
    {
        if (!Lobby.isHost || !syncing)
        {
            return;
        }
        awaitingDone.Remove(RPCManager.sender);
        CheckHostProgress();
    }

    [RPC(requireAuthority = true)]
    private void _Unfreeze()
    {
        if (frozenSyncId == 0)
        {
            // Not part of this freeze, e.g. a second joiner that linked up during it and waits its turn.
            return;
        }
        frozenSyncId = 0;
        pendingRestore = null;
        pendingRestorePeers = null;
        // Every restore is in, so a player spawned now reaches peers that have already rebuilt.
        GameSave.EnsureLocalPlayer();
        FrozenChanged?.Invoke(false);
        if (!started)
        {
            // A joiner: this is its game starting (drops the lobby UI, unpauses).
            Lobby.FinishJoining();
        }
        else
        {
            GetTree().Paused = false;
        }
        if (Lobby.isHost)
        {
            syncing = false;
            Logging.Log("Unfroze every peer", "GameWorld");
            StartNextJoin();
        }
    }

    // ---- links ----------------------------------------------------------------------

    private static void OnPeerLinked(ulong peer)
    {
        linkedPeers.Add(peer);
        if (frozenSyncId != 0)
        {
            // Linked mid-freeze (a joiner meshing in): it never got this peer's ack, which went out before the link.
            RPCManager.RPCTo(peer, instance, nameof(_FrozenAck), [frozenSyncId]);
        }
        if (Lobby.isHost && started)
        {
            pendingJoins.Enqueue(peer);
            StartNextJoin();
        }
    }

    private static void OnPeerUnlinked(ulong peer)
    {
        linkedPeers.Remove(peer);
        // Don't wait on a peer that has left.
        awaitingDone.Remove(peer);
        CheckHostProgress();
        TryFinishRestore();
    }

    static void StartNextJoin()
    {
        while (!syncing && pendingJoins.Count > 0)
        {
            ulong joiner = pendingJoins.Dequeue();
            if (Lobby.members.ContainsKey(joiner))
            {
                BeginFreeze(SyncMode.Join, joiner);
            }
        }
    }
}
