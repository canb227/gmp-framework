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
    }

    public static bool playRandomSound(string target, List<string> soundPaths, float volumeAdjust = 0)
    {

        string soundPath = soundPaths[Random.Shared.Next(soundPaths.Count)];

        if (soundPath == null || soundPath == "") { return false; }
        if (instance.GetNodeOrNull(target) == null) { return false; }

        RPCManager.RPC(instance, nameof(_playSound), [target, soundPath, volumeAdjust]);
        return true;
    }

    public static bool playSound(string target, string soundPath, float volumeAdjust = 0)
    {
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

                AudioStreamPlayer3D stream = streamPool[i].stream;
                streamPool[i] = (true, streamPool[i].stream);
                
                stream.Reparent(GetNodeOrNull(target), false);
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

