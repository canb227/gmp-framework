using Godot;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using Fmod = FMOD;
using FmodStudio = FMOD.Studio;

public partial class AudioManager : Node
{
    public static AudioManager Instance { get; private set; }

    private FmodStudio.System _studio;
    private Fmod.System _core;
    private FmodStudio.Bank _master, _strings;


    private static bool _resolverSet;
    private static string _libDir;

    // False when the FMOD libraries for this platform couldn't be loaded: the game runs without sound.
    private bool _audioOn;

    private static void SetupFmodResolver()
    {
        if (_resolverSet) return; // SetDllImportResolver throws if called twice (e.g. editor hot reload)
        _resolverSet = true;

        if (OS.HasFeature("editor"))
        {
            if (OS.GetName() == "Windows")
                _libDir = ProjectSettings.GlobalizePath("res://addons/fmod-engine/lib/windows");
            else
                _libDir = ProjectSettings.GlobalizePath("res://addons/fmod-engine/lib/linux");
        }
        else
        {
            _libDir = OS.GetExecutablePath().GetBaseDir();
        }

        NativeLibrary.SetDllImportResolver(typeof(AudioManager).Assembly, ResolveFmod);
    }

    // FMOD's file name for a library: "fmodstudio" is fmodstudio.dll on Windows, libfmodstudio.so on Linux.
    private static string FmodFileName(string name)
    {
        if (OS.GetName() == "Windows")
            return $"{name}.dll";
        return $"lib{name}.so";
    }

    private static nint ResolveFmod(string name, Assembly asm, DllImportSearchPath? searchPath)
    {
        if (!name.StartsWith("fmod")) return IntPtr.Zero;

        // Load core first so studio's dependency on the core library is already satisfied
        if (name.StartsWith("fmodstudio"))
        {
            string core = _libDir.PathJoin(FmodFileName(name.EndsWith("L") ? "fmodL" : "fmod"));
            NativeLibrary.TryLoad(core, out _);
        }

        return NativeLibrary.TryLoad(_libDir.PathJoin(FmodFileName(name)), out IntPtr handle)
            ? handle
            : IntPtr.Zero; // falls back to default probing
    }



    private void OnHitstreamUpdated()
    {
        Camera3D camera = GetViewport().GetCamera3D();
        if (camera == null)
            return; 

        foreach (ContactHit hit in HitStream.hits)
        {
            // What met, by tags (ItemTags): a player is always side A. The normal points from A into B.
            ItemTags a = (ItemTags)hit.tagsA;
            ItemTags b = (ItemTags)hit.tagsB;

            if ((a & ItemTags.PLAYER) != 0)
            {
                // A player landing, running into something, or hit by something; b is what it met.
                if (hit.speed >= 7.5f)
                    PlayOneShot("event:/Impacts/Player_Impact", 1.0f, hit.position, new Dictionary<string, float> { ["Force"] = hit.speed / 15.0f, ["Damage"] = 0.0f });
            }
            else
            {

                PlayOneShot("event:/Impacts/Solid_Metal", 1.0f, hit.position, new Dictionary<string, float> { ["Force"] = hit.speed / 10.0f });
            }
        }
    }

    public override void _Ready()
    {
        SetupFmodResolver();
        Instance = this;
        HitStream.Updated += OnHitstreamUpdated;
        try
        {
            Check(FmodStudio.System.create(out _studio));
        }
        catch (DllNotFoundException)
        {
            GD.PushError($"FMOD libraries not found in {_libDir}: running without sound.");
            return;
        }
        _audioOn = true;
        Check(_studio.getCoreSystem(out _core));

        // Godot is right-handed; this saves you from flipping axes manually
        Check(_studio.initialize(
            512,
            FmodStudio.INITFLAGS.NORMAL,
            Fmod.INITFLAGS._3D_RIGHTHANDED,
            IntPtr.Zero));

        string bankDir = OS.HasFeature("editor")
            ? ProjectSettings.GlobalizePath("res://game/assets/audio/banks")
            : OS.GetExecutablePath().GetBaseDir().PathJoin("banks");

        GD.Print($"FMOD bank dir: {bankDir}");

        Check(_studio.loadBankFile(bankDir.PathJoin("Master.bank"), FmodStudio.LOAD_BANK_FLAGS.NORMAL, out _master));
        Check(_studio.loadBankFile(bankDir.PathJoin("Master.strings.bank"), FmodStudio.LOAD_BANK_FLAGS.NORMAL, out _strings));

        // Copies started by the test harness (--testctl) run in the background while someone uses the computer: silent.
        if (System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--testctl") >= 0)
        {
            Check(_studio.getBus("bus:/", out FmodStudio.Bus masterBus));
            Check(masterBus.setVolume(0));
            GD.Print("Test copy: sound muted");
        }
    }



    public override void _Process(double delta)
    {
        if (!_audioOn) return;
        _studio.update(); // must be called every frame
        var cam = GetViewport().GetCamera3D();
        if (cam != null)
        {
            var t = cam.GlobalTransform;
            var attrs = new Fmod.ATTRIBUTES_3D
            {
                position = new Fmod.VECTOR { x = t.Origin.X, y = t.Origin.Y, z = t.Origin.Z },
                forward  = new Fmod.VECTOR { x = -t.Basis.Z.X, y = -t.Basis.Z.Y, z = -t.Basis.Z.Z },
                up       = new Fmod.VECTOR { x =  t.Basis.Y.X, y =  t.Basis.Y.Y, z =  t.Basis.Y.Z },
                velocity = default
            };
            _studio.setListenerAttributes(0, attrs);
        }
    }

    public override void _ExitTree()
    {
        if (!_audioOn) return;
        _strings.unload();
        _master.unload();
        _studio.release();
    }

    // --- Playing events ---

    /// Fire-and-forget (footsteps, gunshots, UI clicks)
    public void PlayOneShot(string eventPath, float volume = 1.0f, Vector3? position = null, Dictionary<string, float> parameters = null)
    {
        if (!_audioOn) return;
        if (_studio.getEvent(eventPath, out FmodStudio.EventDescription desc) != Fmod.RESULT.OK)
        {
            GD.PushWarning($"FMOD event not found: {eventPath}");
            return;
        }

        desc.createInstance(out FmodStudio.EventInstance inst);

        if (position.HasValue)
        {
            inst.set3DAttributes(To3D(position.Value));
        }

        if (parameters != null)
        {
            foreach (var kvp in parameters)
            {
                var result = inst.setParameterByName(kvp.Key, kvp.Value);
                if (result != Fmod.RESULT.OK)
                {
                    GD.PushWarning($"FMOD param '{kvp.Key}' failed on {eventPath}: {result}");
                }
            }
        }

        inst.setVolume(volume);
        inst.start();
        inst.release(); // instance is freed automatically once it stops
    }

    /// For events you need to control later (music, loops, parameters)
    public FmodStudio.EventInstance? CreateInstance(string eventPath)
    {
        if (!_audioOn) return null;
        if (_studio.getEvent(eventPath, out FmodStudio.EventDescription desc) != Fmod.RESULT.OK)
            return null;
        desc.createInstance(out FmodStudio.EventInstance inst);
        return inst;
    }

    public static Fmod.ATTRIBUTES_3D To3D(Vector3 pos) => new Fmod.ATTRIBUTES_3D 
    {
        position = new Fmod.VECTOR { x = pos.X, y = pos.Y, z = pos.Z },
        forward  = new Fmod.VECTOR { x = 0, y = 0, z = -1 },
        up       = new Fmod.VECTOR { x = 0, y = 1, z = 0 },
        velocity = default
    };

    public static void Check(Fmod.RESULT result)
    {
        if (result != Fmod.RESULT.OK)
            GD.PushError($"FMOD error: {result}");
    }
}
