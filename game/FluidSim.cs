using Godot;
using Godot.Collections;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

[GlobalClass]
public partial class FluidSim : GMPOSingleton
{

    public override void _Ready()
    {

    }

    public override void _Process(double delta)
    {

    }

    public override void _PhysicsProcess(double delta)
    {

    }

    public override void AfterInit()
    {
        RenderingDevice rd = RenderingServer.CreateLocalRenderingDevice();
        int x_axis = 4096;
        int y_axis = 4096;
        FastNoiseLite noise = new FastNoiseLite();
        noise.Frequency = 0.005f;
        //noise.Seed = 1;
        noise.FractalType = FastNoiseLite.FractalTypeEnum.None;
        noise.DomainWarpEnabled = false;
        noise.Offset = new Vector3(0.0f, 0.0f, 0.0f);
        Stopwatch sw = Stopwatch.StartNew();
        Image noiseImage = Godot.Image.CreateEmpty(x_axis, y_axis, false, Image.Format.Rf);
        for (int i = 0; i < x_axis; i++)
        {
            for (int j = 0; j < y_axis; j++)
            {
                // Get the noise value at the world position
                float noiseValue = noise.GetNoise2D(i, j);

                // Set the pixel in the image
                noiseImage.SetPixel(i, j, new Color(noiseValue, 0, 0, 0));
            }
        }
        sw.Stop();
        GD.Print("Noise: " + sw.ElapsedMilliseconds);
        sw.Reset();
        sw.Start();
        //setup compute shader
        var shaderFile = GD.Load<RDShaderFile>("res://test.glsl");
        RDShaderSpirV blendShaderBytecode = shaderFile.GetSpirV();
        Rid blendShader = rd.ShaderCreateFromSpirV(blendShaderBytecode);

        //Setup Noise Image
        RDSamplerState noiseSamplerState = new RDSamplerState();
        Rid noiseSampler = rd.SamplerCreate(noiseSamplerState);
        RDTextureFormat noiseInputFmt = new RDTextureFormat();
        noiseInputFmt.Width = (uint)noiseImage.GetWidth();
        noiseInputFmt.Height = (uint)noiseImage.GetHeight();
        noiseInputFmt.Format = RenderingDevice.DataFormat.R32Sfloat;
        noiseInputFmt.UsageBits = RenderingDevice.TextureUsageBits.CanCopyFromBit | RenderingDevice.TextureUsageBits.SamplingBit | RenderingDevice.TextureUsageBits.CanUpdateBit;
        RDTextureView noiseInputView = new RDTextureView();
        byte[] noiseInputImageData = noiseImage.GetData();
        Godot.Collections.Array<byte[]> noiseData = new Godot.Collections.Array<byte[]>
            {
                noiseInputImageData
            };
        Rid noiseTex = rd.TextureCreate(noiseInputFmt, noiseInputView, noiseData);
        RDUniform noiseSamplerUniform = new RDUniform();
        noiseSamplerUniform.UniformType = RenderingDevice.UniformType.SamplerWithTexture;
        noiseSamplerUniform.Binding = 0;
        noiseSamplerUniform.AddId(noiseSampler);
        noiseSamplerUniform.AddId(noiseTex);

        //Setup Output Image 
        RDTextureFormat blendfmt = new RDTextureFormat();
        blendfmt.Width = (uint)x_axis;
        blendfmt.Height = (uint)y_axis;
        blendfmt.Format = RenderingDevice.DataFormat.R32Sfloat;
        blendfmt.UsageBits = RenderingDevice.TextureUsageBits.StorageBit | RenderingDevice.TextureUsageBits.CanUpdateBit | RenderingDevice.TextureUsageBits.CanCopyFromBit;
        RDTextureView blendview = new RDTextureView();
        Image blend_image = Godot.Image.CreateEmpty(x_axis, y_axis, false, Image.Format.Rf);
        byte[] blendOutputImageData = blend_image.GetData();
        Godot.Collections.Array<byte[]> blendTempData = new Godot.Collections.Array<byte[]>
            {
                blendOutputImageData
            };
        Rid blendOutputTex = rd.TextureCreate(blendfmt, blendview, blendTempData);
        RDUniform blendOutputTexUniform = new RDUniform();
        blendOutputTexUniform.UniformType = RenderingDevice.UniformType.Image;
        blendOutputTexUniform.Binding = 1;
        blendOutputTexUniform.AddId(blendOutputTex);

        int imageWidth = noiseImage.GetWidth();
        int imageHeight = noiseImage.GetHeight();
        byte[] imageDimensionsBytes = new byte[sizeof(int) * 2];
        Buffer.BlockCopy(BitConverter.GetBytes(imageWidth), 0, imageDimensionsBytes, 0, sizeof(int));
        Buffer.BlockCopy(BitConverter.GetBytes(imageHeight), 0, imageDimensionsBytes, sizeof(int), sizeof(int));
        Rid imageDimensionsBuffer = rd.StorageBufferCreate((uint)imageDimensionsBytes.Length, imageDimensionsBytes);

        RDUniform imageDimensionsUniform = new RDUniform()
        {
            UniformType = RenderingDevice.UniformType.StorageBuffer,
            Binding = 2
        };
        imageDimensionsUniform.AddId(imageDimensionsBuffer);

        // Create the uniformSet
        Rid blenduniformSet = rd.UniformSetCreate(new Array<RDUniform> { noiseSamplerUniform, blendOutputTexUniform, imageDimensionsUniform }, blendShader, 0);

        // Create a compute pipeline
        Rid blendpipeline = rd.ComputePipelineCreate(blendShader);

        var blendcomputeList = rd.ComputeListBegin();
        rd.ComputeListBindComputePipeline(blendcomputeList, blendpipeline);
        rd.ComputeListBindUniformSet(blendcomputeList, blenduniformSet, 0);
        int blendthreadsPerGroup = 32;
        uint blendxGroups = (uint)(noiseImage.GetWidth() + blendthreadsPerGroup - 1) / (uint)blendthreadsPerGroup;
        uint blendyGroups = (uint)(noiseImage.GetHeight() + blendthreadsPerGroup - 1) / (uint)blendthreadsPerGroup;
        rd.ComputeListDispatch(blendcomputeList, blendxGroups, blendyGroups, 1);
        rd.ComputeListEnd();
        sw.Stop();
        GD.Print("Prep: " + sw.ElapsedMilliseconds);
        sw.Reset();
        sw.Start();
        rd.Submit();
        rd.Sync();

        sw.Stop();
        GD.Print("Render: " + sw.ElapsedMilliseconds);
        sw.Reset();
        sw.Start();
        var blendbyteData = rd.TextureGetData(blendOutputTex, 0);
        sw.Stop();
        GD.Print("Data Pull: " + sw.ElapsedMilliseconds);
        sw.Reset();
        sw.Start();
        Image final_img = Image.CreateFromData(x_axis, y_axis, false, Image.Format.Rf, blendbyteData);
        final_img.SavePng("test.png");
        sw.Stop();
        GD.Print("Image Save: " + sw.ElapsedMilliseconds);
        rd.FreeRid(blendShader);
        rd.FreeRid(noiseSampler);
        rd.FreeRid(noiseTex);
        rd.FreeRid(blendOutputTex);
        rd.FreeRid(imageDimensionsBuffer);
    }

    public override void ApplyStateUpdate(byte[] update)
    {

    }

    public override byte[] GenerateStateUpdate()
    {
        return null;
    }
}

