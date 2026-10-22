using Godot;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using Stopwatch = System.Diagnostics.Stopwatch;

/// <summary>
/// GameWorld: stepping the physics world (<see cref="physics"/>) and networking its loose items. The items'
/// states are built, applied and played back in C++ (native/src/physics_world_sync.cpp); this side moves their packets
/// on <see cref="Channel.GAME_Items"/>, gives them what is left of each send's budget after the GMPObjects, and keeps
/// the playback clock per sending peer, which the GMPObjects' followers use too.
/// </summary>
public partial class GameWorld
{
    /// <summary>Item packets in arrival order, applied (and cleared) at the next physics tick.</summary>
    static readonly List<(ulong from, byte[] packet)> pendingItemPackets = new();

    static readonly StringName stepName = "step";
    static readonly StringName setSelfPeerName = "set_self_peer";
    static readonly StringName setSendIntervalName = "set_send_interval";
    static readonly StringName setPlaybackTickName = "set_playback_tick";
    static readonly StringName applyBodyStatesName = "apply_body_states";
    static readonly StringName buildBodyStatesName = "build_body_states";

    /// <summary>From <see cref="OnStateMessage"/>: an item packet, kept for the next physics tick.</summary>
    static void ReceiveItemPacket(ulong from, byte[] packet)
    {
        // Send time (8 bytes), then the sender's physics tick (8 bytes), then the states.
        if (packet.Length < 16)
        {
            return;
        }
        long receiveStart = Stopwatch.GetTimestamp();
        receivedBytes += packet.Length;
        ulong tick = BinaryPrimitives.ReadUInt64LittleEndian(packet.AsSpan(8));
        HeardTick(from, tick);
        pendingItemPackets.Add((from, packet));
        receiveCost += Stopwatch.GetTimestamp() - receiveStart;
    }

    /// <summary>
    /// Every character's view, for the bodies' sync priorities (what someone is near or looking at is sent more often)
    /// and which hits are sent. Where they look from, not where they stand: someone piloting a crane far away is near
    /// what the crane does.
    /// </summary>
    static void SendViewers()
    {
        viewerPositions.Clear();
        viewerEyes.Clear();
        viewerLooks.Clear();
        foreach (GMPObject obj in syncedObjs.Values)
        {
            if (obj is GMPOCharacter character)
            {
                Transform3D view = character.ViewTransform();
                viewerPositions.Add(view.Origin);
                viewerEyes.Add(view.Origin);
                viewerLooks.Add(-view.Basis.Z);
            }
        }
        PhysicsWorld.SetViewers(viewerPositions.ToArray(), viewerEyes.ToArray(), viewerLooks.ToArray());
    }
    static readonly List<Vector3> viewerPositions = new();
    static readonly List<Vector3> viewerEyes = new();
    static readonly List<Vector3> viewerLooks = new();

    /// <summary>
    /// Each physics tick, after the playback clocks have moved on: applies the item packets that came in, tells the
    /// physics world where each sender's playback is, and steps it.
    /// </summary>
    static void StepNativeWorld(double delta)
    {
        if (physics == null)
        {
            pendingItemPackets.Clear();
            return;
        }
        SendViewers();
        long applyStart = Stopwatch.GetTimestamp();
        foreach ((ulong from, byte[] packet) in pendingItemPackets)
        {
            physics.Call(applyBodyStatesName, packet, from);
        }
        pendingItemPackets.Clear();
        applyCost += Stopwatch.GetTimestamp() - applyStart;
        foreach ((ulong peer, PlaybackClock clock) in playbackClocks)
        {
            physics.Call(setPlaybackTickName, peer, clock.playbackTick);
        }
        physics.Call(stepName, (float)delta);
    }

    /// <summary>
    /// Builds and sends the item states for this tick (their turn is <paramref name="turn"/>), within
    /// <paramref name="maxBytes"/>; returns the bytes sent to each peer.
    /// </summary>
    static int SendItemStates(int maxBytes, ulong turn, ulong frame)
    {
        if (physics == null || maxBytes <= 0)
        {
            return 0;
        }
        Godot.Collections.Array packets = physics.Call(buildBodyStatesName, maxBytes, MaxPacketSize, (int)turn, frame, netTime).AsGodotArray();
        long sendStart = Stopwatch.GetTimestamp();
        int bytes = 0;
        foreach (Variant packet in packets)
        {
            byte[] packetBytes = packet.AsByteArray();
            bytes += packetBytes.Length;
            sentBytes += packetBytes.Length * (Lobby.MemberCount - 1);
            Lobby.SendToAllExceptSelf(Channel.GAME_Items, packetBytes, Network.k_nSteamNetworkingSend_Unreliable | Network.k_nSteamNetworkingSend_NoNagle);
        }
        sendCost += Stopwatch.GetTimestamp() - sendStart;
        return bytes;
    }
}
