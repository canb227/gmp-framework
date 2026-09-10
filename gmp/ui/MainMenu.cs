using Godot;
using Godot.NativeInterop;
using System;
using System.Linq;

// Entry scene. Routes to the shared debug lobby (in LAN or Steam mode), options, or
// quit. Also honours the command-line driving flags so the headless verification path
// still works: --steam (with or without --host/--join) opens the lobby in Steam mode,
// --host/--join/--lan opens it in LAN mode, and the lobby's own HandleCmdline then
// auto-hosts/joins from the same args.
public partial class MainMenu : Control
{
    private const string LobbyScene = "res://gmp/ui/lobby_debug.tscn";
    private const string OptionsScene = "res://gmp/ui/options_menu.tscn";

    public override void _Ready()
    {
        Button("StartSteamButton").Pressed += () => OpenLobby(LobbyMode.Steam);
        Button("StartLANButton").Pressed += () => OpenLobby(LobbyMode.Lan);
        Button("OptionsButton").Pressed += () => Open(OptionsScene);
        Button("QuitButton").Pressed += () => GetTree().Quit();

        CallDeferred(nameof(HandleCmdline));

        RenderingDevice rd = RenderingServer.CreateLocalRenderingDevice();
        var shaderFile = GD.Load<RDShaderFile>("res://test.glsl");
        var shaderBytecode = shaderFile.GetSpirV();
        var shader = rd.ShaderCreateFromSpirV(shaderBytecode);
        // Prepare our data. We use floats in the shader, so we need 32 bit.
        int xSize = 1024;
        int ySize = 1024;
        int zSize = 3;
        float[] floats = new float[xSize*ySize*zSize];
        for (int z = 0; z < zSize; z++)
        {
            for (int y = 0; y < ySize; y++)
            {
                for (int x = 0; x < xSize; x++)
                {
                    floats[x + (y * xSize) + (z * xSize * ySize)] = 1;
                }
            }
        }
        int totalBytes = floats.Length * sizeof(float);
        byte[] byteArray = new byte[totalBytes];
        Buffer.BlockCopy(floats, 0, byteArray, 0, totalBytes);

        // Create a storage buffer that can hold our float values.
        // Each float has 4 bytes (32 bit) so 10 x 4 = 40 bytes
        var buffer = rd.StorageBufferCreate((uint)byteArray.Length, byteArray);


        // Create a uniform to assign the buffer to the rendering device
        var uniform = new RDUniform
        {
            UniformType = RenderingDevice.UniformType.StorageBuffer,
            Binding = 0
        };
        uniform.AddId(buffer);
        var uniformSet = rd.UniformSetCreate([uniform], shader, 0);

        // Create a compute pipeline
        var pipeline = rd.ComputePipelineCreate(shader);
        var computeList = rd.ComputeListBegin();
        rd.ComputeListBindComputePipeline(computeList, pipeline);
        rd.ComputeListBindUniformSet(computeList, uniformSet, 0);
        rd.ComputeListDispatch(computeList, xGroups: (uint)xSize/32, yGroups: (uint)ySize/32, zGroups: 3);
        rd.ComputeListEnd();

        // Submit to GPU and wait for sync
        rd.Submit();
        rd.Sync();

        // Read back the data from the buffers
        var outputBytes = rd.BufferGetData(buffer);
        var output = new float[xSize * ySize * zSize];
        Buffer.BlockCopy(outputBytes, 0, output, 0, outputBytes.Length);
        float total = 0;
        foreach( var item in output )
        {
            total += item;
        }
        GD.Print($" expected: {xSize*ySize*zSize*2} vs actual: {total}");
    }

    

    private Button Button(string name) =>
        GetNode<Button>($"CenterContainer/VBoxContainer/ButtonList/{name}");

    private void Open(string path) => GetTree().ChangeSceneToFile(path);

    private void OpenLobby(LobbyMode mode)
    {
        LobbyDebug.NextMode = mode;
        Open(LobbyScene);
    }

    private void HandleCmdline()
    {
        bool steam = false, lan = false;
        foreach (string a in OS.GetCmdlineUserArgs())
        {
            if (a == "--steam") steam = true;
            if (a == "--host" || a == "--join" || a == "--lan") lan = true;
        }
        if (steam) OpenLobby(LobbyMode.Steam);
        else if (lan) OpenLobby(LobbyMode.Lan);
    }
}
