using Godot;
using PolyType;
using System;
using System.Collections.Generic;

/// <summary>One impact on the wire: where, the two surfaces (<see cref="ItemTags"/> values) and how hard.</summary>
[GenerateShape]
public partial record struct ImpactEvent
{
    public Vector3 point;
    public byte self;
    public byte other;
    public float speed;
}

/// <summary>The impacts one peer detected in one physics frame.</summary>
[GenerateShape]
public partial record ImpactBatch
{
    public List<ImpactEvent> impacts = new();
}

/// <summary>
/// Plays impact sounds for physical factory items (see <see cref="ImpactSounds"/> for what they sound like).
/// <para>
/// Hits come from the Box3D world's <c>contact_hit</c>, which only the peer simulating an item sees (everyone else
/// moves it kinematically). So each hit is claimed by the authority of the lower-id item in the pair: that peer
/// plays it at once and sends the frame's impacts to everyone else in one unreliable message on
/// <see cref="Channel.GAME_Effects"/>. Hits between two non-items (structure on level) are ignored.
/// </para>
/// <para>
/// A per-item cooldown, a per-frame cap and a fixed voice pool (oldest voice reused) keep settling piles and
/// busy belts from flooding the mix.
/// </para>
/// </summary>
public partial class ImpactAudio : Node3D
{
    public static ImpactAudio instance { get; private set; }

    const int MaxVoices = 24;
    const ulong ItemCooldownMs = 80;
    const int MaxPerFrame = 8;
    /// <summary>Random pitch spread so repeated hits don't sound identical.</summary>
    const float PitchJitter = 0.08f;

    /// <summary>Impacts played on this peer (used by the headless multiplayer test).</summary>
    public int playedCount { get; private set; }

    readonly List<AudioStreamPlayer3D> voices = new();
    int nextVoice;
    readonly Dictionary<ulong, ulong> lastHitMs = new();
    readonly ImpactBatch outgoing = new();
    int claimedThisFrame;
    Network listeningTo;

    /// <summary>Creates the singleton under <see cref="GameWorld"/> if needed and binds it to the current network.</summary>
    public static void Ensure()
    {
        if (instance == null || !IsInstanceValid(instance))
        {
            instance = new ImpactAudio { Name = "ImpactAudio" };
            GameWorld.instance.AddChild(instance);
        }
        instance.BindNetwork();
    }

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Pausable;
        for (int i = 0; i < MaxVoices; i++)
        {
            var voice = new AudioStreamPlayer3D { UnitSize = 4f, MaxDistance = 45f, MaxPolyphony = 1 };
            voices.Add(voice);
            AddChild(voice);
        }
        if (GameWorld.b3droot != null) GameWorld.b3droot.ContactHit += OnContactHit;
        BindNetwork();
    }

    public override void _ExitTree()
    {
        if (GameWorld.b3droot != null) GameWorld.b3droot.ContactHit -= OnContactHit;
        if (listeningTo != null) listeningTo.MessageReceivedEvent -= OnMessageReceived;
        listeningTo = null;
        if (instance == this) instance = null;
    }

    // The lobby creates a new Network per session, so follow it.
    void BindNetwork()
    {
        if (listeningTo == Lobby.network) return;
        if (listeningTo != null) listeningTo.MessageReceivedEvent -= OnMessageReceived;
        listeningTo = Lobby.network;
        if (listeningTo != null) listeningTo.MessageReceivedEvent += OnMessageReceived;
    }

    public override void _PhysicsProcess(double delta)
    {
        claimedThisFrame = 0;
        if (outgoing.impacts.Count == 0) return;
        Lobby.SendToAllExceptSelf(Channel.GAME_Effects, GMPObject.serializer.Serialize(outgoing), Network.k_nSteamNetworkingSend_UnreliableNoDelay);
        outgoing.impacts.Clear();
    }

    void OnContactHit(Godot.Collections.Dictionary hit)
    {
        Node a = hit["body_a"].As<Node>(), b = hit["body_b"].As<Node>();
        var itemA = a as PhysicalFactoryItem;
        var itemB = b as PhysicalFactoryItem;
        if (itemA != null && itemA.id == 0) itemA = null;
        if (itemB != null && itemB.id == 0) itemB = null;
        PhysicalFactoryItem owner = itemA == null ? itemB : itemB == null ? itemA : (itemA.id < itemB.id ? itemA : itemB);
        if (owner == null || owner.authority != Lobby.selfPeerID || claimedThisFrame >= MaxPerFrame) return;

        ulong now = Time.GetTicksMsec();
        if (lastHitMs.TryGetValue(owner.id, out ulong last) && now - last < ItemCooldownMs) return;
        lastHitMs[owner.id] = now;
        claimedThisFrame++;

        bool ownerIsA = owner == itemA;
        ItemTags self = SurfaceOf(ownerIsA ? a : b, hit[ownerIsA ? "user_material_a" : "user_material_b"].AsInt64());
        ItemTags other = SurfaceOf(ownerIsA ? b : a, hit[ownerIsA ? "user_material_b" : "user_material_a"].AsInt64());
        var impact = new ImpactEvent { point = hit["point"].AsVector3(), self = (byte)self, other = (byte)other, speed = hit["approach_speed"].AsSingle() };
        Play(impact);
        outgoing.impacts.Add(impact);
    }

    /// <summary>
    /// A shape's user_material_id if it holds a material tag (e.g. a conveyor's metal rails), else the first
    /// material tag of the item or structure the body belongs to, else <see cref="ImpactSounds.DefaultSurface"/>.
    /// </summary>
    static ItemTags SurfaceOf(Node body, long userMaterial)
    {
        if (userMaterial > 0 && userMaterial <= byte.MaxValue && ImpactSounds.IsMaterial((ItemTags)userMaterial)) return (ItemTags)userMaterial;
        for (Node n = body; n != null && n != GameWorld.b3droot; n = n.GetParent())
        {
            ItemTags? tag = n switch
            {
                PhysicalFactoryItem item => ImpactSounds.MaterialOf(item.tags),
                Structure structure => ImpactSounds.MaterialOf(structure.tags),
                _ => null,
            };
            if (tag != null) return tag.Value;
            if (n is PhysicalFactoryItem or Structure) break;
        }
        return ImpactSounds.DefaultSurface;
    }

    void OnMessageReceived(ulong from, Channel ch, byte[] msg)
    {
        if (ch != Channel.GAME_Effects) return;
        ImpactBatch batch = GMPObject.serializer.Deserialize<ImpactBatch>(msg);
        foreach (ImpactEvent impact in batch.impacts) Play(impact);
    }

    void Play(ImpactEvent impact)
    {
        float t = Mathf.Clamp((impact.speed - ImpactSounds.MinSpeed) / (ImpactSounds.FullSpeed - ImpactSounds.MinSpeed), 0f, 1f);
        bool heavy = impact.speed >= ImpactSounds.HeavySpeed;
        float loudness = ImpactSounds.Loudness(impact.speed);
        float pitch = Mathf.Lerp(1.06f, 0.94f, t); // harder hits sit a little lower
        foreach ((ItemTags material, float gain) in ImpactSounds.Layers((ItemTags)impact.self, (ItemTags)impact.other))
        {
            AudioStream stream = ImpactSounds.Pick(material, heavy);
            if (stream == null) continue;
            AudioStreamPlayer3D voice = voices[nextVoice];
            nextVoice = (nextVoice + 1) % voices.Count;
            voice.Stream = stream;
            voice.GlobalPosition = impact.point;
            voice.VolumeDb = Mathf.LinearToDb(gain * loudness);
            voice.PitchScale = pitch * (1f + (float)(Random.Shared.NextDouble() * 2 - 1) * PitchJitter);
            voice.Play();
        }
        playedCount++;
    }
}
