using Godot;
using PolyType;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

[GenerateShapeFor<NodePath>]
public partial class Witness;


[GlobalClass]
public partial class AudioManager : Node
{
    public static AudioManager instance;

    /// <summary>When off, no sounds play: hit events are ignored and the play methods do nothing (return false).</summary>
    public static bool enabled = false; // off for testing; set back to true to restore audio
    List<(bool inUse, AudioStreamPlayer3D stream)> streamPool = new();
    private int numStreams = 25;

    public override void _Ready()
    {
        instance = this;
        for (int i = 0; i < numStreams; i++)
        {
            AudioStreamPlayer3D newStream = new();
            instance.AddChild(newStream);
            streamPool.Add( (false, newStream));
        }
        GameWorld.b3droot.ContactHit += B3droot_ContactHit;
    }

    /// <summary>
    /// Impact sounds broadcast per physics tick at most. A falling pile reports dozens of hits a tick, each of which
    /// would be an RPC to every peer, for a pool of only <see cref="numStreams"/> players; the rest are dropped.
    /// </summary>
    const int MaxHitSoundsPerTick = 3;
    ulong hitTick;
    int hitSoundsThisTick;

    private void B3droot_ContactHit(Godot.Collections.Dictionary obj)
    {
        if (!enabled) return;
        ulong tick = Engine.GetPhysicsFrames();
        if (tick != hitTick)
        {
            hitTick = tick;
            hitSoundsThisTick = 0;
        }
        if (hitSoundsThisTick >= MaxHitSoundsPerTick) return;
        hitSoundsThisTick++;
      //  GD.Print(obj);
        AudioManager.playRandomSound(obj[Box3DKeys.point].AsVector3(), ["res://game/assets/audio/impacts/impactWood_heavy_000.ogg", "res://game/assets/audio/impacts/impactWood_heavy_001.ogg", "res://game/assets/audio/impacts/impactWood_heavy_002.ogg", "res://game/assets/audio/impacts/impactWood_heavy_003.ogg", "res://game/assets/audio/impacts/impactWood_heavy_004.ogg"], -40);
    }

    public static bool playRandomSound(string target, List<string> soundPaths, float volumeAdjust = 0)
    {
        if (!enabled) return false;

        string soundPath = soundPaths[Random.Shared.Next(soundPaths.Count)];

        if (soundPath == null || soundPath == "") { return false; }
        if (instance.GetNodeOrNull(target) == null) { return false; }

        RPCManager.RPC(instance, nameof(_playSound), [target, soundPath, volumeAdjust]);
        return true;
    }

    public static bool playRandomSound(Vector3 target, List<string> soundPaths, float volumeAdjust = 0)
    {
        if (!enabled) return false;

        string soundPath = soundPaths[Random.Shared.Next(soundPaths.Count)];

        if (soundPath == null || soundPath == "") { return false; }


        RPCManager.RPC(instance, nameof(_playSoundAtLocation), [target, soundPath, volumeAdjust]);
        return true;
    }

    public static bool playSound(string target, string soundPath, float volumeAdjust = 0)
    {
        if (!enabled) return false;
        if (soundPath == null || soundPath == "") { return false; }
        if (instance.GetNodeOrNull(target) == null) { return false; }
        RPCManager.RPC(instance, nameof(_playSound), [target, soundPath,volumeAdjust]);
        return true;
    }

    [RPC]
    private bool _playSound(string target, string soundPath, float volumeAdjust = 0)
    {

        AudioStream sound = ResourceLoader.Load<AudioStream>(soundPath);
        if (sound == null) { return false; }

        if (GetNodeOrNull(target) == null) { return false; }

        for (int i = 0; i < numStreams; i++)
        {

            if (streamPool[i].inUse == false)
            {

                AudioStreamPlayer3D stream = new();

                GetNodeOrNull(target).AddChild(stream);


                stream.ResetPhysicsInterpolation();
                stream.Stream = sound;
                stream.VolumeDb = volumeAdjust;
                stream.Play();
                stream.Finished += () =>
                {
                    Stream_Finished(i);
                };

                return true;
            }
        }
        return false;
    }

    [RPC]
    private bool _playSoundAtLocation(Vector3 target, string soundPath, float volumeAdjust = 0)
    {

        AudioStream sound = ResourceLoader.Load<AudioStream>(soundPath);
        if (sound == null) { return false; }
        for (int i = 0; i < numStreams; i++)
        {

            if (streamPool[i].inUse == false)
            {

                AudioStreamPlayer3D stream = streamPool[i].stream;
                if (stream.IsQueuedForDeletion() || !stream.IsInsideTree())
                {
                    return false;
                }
                streamPool[i] = (true, streamPool[i].stream);

                stream.GlobalPosition = target;
                stream.ResetPhysicsInterpolation();
                stream.Stream = sound;
                stream.VolumeDb = volumeAdjust;
                stream.Play();
                stream.Finished += () =>
                {
                    Stream_Finished(i);
                };

                return true;
            }
        }
        return false;
    }

    private void Stream_Finished(int idx)
    {
        streamPool[idx] = (false, streamPool[idx].stream);
    }
}

