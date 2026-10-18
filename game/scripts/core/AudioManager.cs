using Godot;
using System;
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

    private static void SetupFmodResolver()
    {
        if (_resolverSet) return; // SetDllImportResolver throws if called twice (e.g. editor hot reload)
        _resolverSet = true;

        _libDir = OS.HasFeature("editor")
            ? ProjectSettings.GlobalizePath("res://addons/fmod-engine/lib/linux")
            : OS.GetExecutablePath().GetBaseDir();

        NativeLibrary.SetDllImportResolver(typeof(AudioManager).Assembly, ResolveFmod);
    }

    private static nint ResolveFmod(string name, Assembly asm, DllImportSearchPath? searchPath)
    {
        if (!name.StartsWith("fmod")) return IntPtr.Zero;

        // Load core first so studio's dependency on libfmod.so.13 is already satisfied
        if (name.StartsWith("fmodstudio"))
        {
            string core = _libDir.PathJoin(name.EndsWith("L") ? "libfmodL.so" : "libfmod.so");
            NativeLibrary.TryLoad(core, out _);
        }

        return NativeLibrary.TryLoad(_libDir.PathJoin($"lib{name}.so"), out IntPtr handle)
            ? handle
            : IntPtr.Zero; // falls back to default probing
    }

    public override void _Ready()
    {
        SetupFmodResolver();
        Instance = this;

        Check(FmodStudio.System.create(out _studio));
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
    }

    public override void _Process(double delta)
    {
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
        _strings.unload();
        _master.unload();
        _studio.release();
    }

    // --- Playing events ---

    /// Fire-and-forget (footsteps, gunshots, UI clicks)
    public void PlayOneShot(string eventPath, float volume = 1.0f, Vector3? position = null, string param_name = "", float? param_val = 0.0f)
    {
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
        if (param_val.HasValue && param_name.Length > 0)
        {
            inst.setParameterByName(param_name, param_val.Value);
        }
        inst.setVolume(volume);
        //GD.Print("Play the sound at: " + position.Value.ToString());
        inst.start();
        inst.release(); // instance is freed automatically once it stops
    }

    /// For events you need to control later (music, loops, parameters)
    public FmodStudio.EventInstance? CreateInstance(string eventPath)
    {
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
