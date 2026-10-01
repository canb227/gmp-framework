using Godot;
using ImGuiNET;
using System;
using System.Collections.Generic;
using System.Linq;
using Vec3 = System.Numerics.Vector3;

/// <summary>
/// "debugui graphics" window: live tweaking of the active WorldEnvironment (tonemap, glow, GI/SSAO/SSR, fog,
/// volumetric fog, ambient, adjustments), every Light3D in the scene, global shadow quality, and viewport
/// AA / scaling / debug-draw settings. Changes are runtime-only; nothing is written back to scene files.
/// Driven from Console._Process.
/// </summary>
public static class GraphicsDebug
{
    public static bool displayGraphicsDebugInfo = false;

    // Lights are found by scanning the tree, so only rescan periodically (and on demand).
    private const double lightRescanInterval = 2.0;
    private static double lightRescanTimer = lightRescanInterval;
    private static List<Light3D> lights = new();

    // RenderingServer has setters but no getters for these, so mirror them, seeded from project settings.
    private static bool shadowStateLoaded = false;
    private static int directionalAtlasSize;
    private static bool directionalAtlas16Bits;
    private static RenderingServer.ShadowQuality directionalSoftQuality;
    private static RenderingServer.ShadowQuality positionalSoftQuality;
    private static readonly int[] atlasSizes = { 512, 1024, 2048, 4096, 8192, 16384 };

    public static void Process(Node owner, double delta)
    {
        if (!displayGraphicsDebugInfo)
        {
            return;
        }

        lightRescanTimer += delta;
        if (lightRescanTimer >= lightRescanInterval)
        {
            RescanLights(owner);
        }
        if (!shadowStateLoaded)
        {
            LoadShadowState();
        }
        Render(owner);
    }

    private static void RescanLights(Node owner)
    {
        lightRescanTimer = 0;
        lights = owner.GetTree().Root.FindChildren("*", "Light3D", true, false).OfType<Light3D>().ToList();
    }

    private static void LoadShadowState()
    {
        shadowStateLoaded = true;
        directionalAtlasSize = (int)ProjectSettings.GetSetting("rendering/lights_and_shadows/directional_shadow/size", 4096);
        directionalAtlas16Bits = (bool)ProjectSettings.GetSetting("rendering/lights_and_shadows/directional_shadow/16_bits", true);
        directionalSoftQuality = (RenderingServer.ShadowQuality)(int)ProjectSettings.GetSetting("rendering/lights_and_shadows/directional_shadow/soft_shadow_filter_quality", 2);
        positionalSoftQuality = (RenderingServer.ShadowQuality)(int)ProjectSettings.GetSetting("rendering/lights_and_shadows/positional_shadow/soft_shadow_filter_quality", 2);
    }

    private static void Render(Node owner)
    {
        ImGui.SetNextWindowPos(new System.Numerics.Vector2(10, 420), ImGuiCond.FirstUseEver);
        ImGui.Begin("debugui graphics", ref displayGraphicsDebugInfo);
        Viewport viewport = owner.GetViewport();

        if (ImGui.CollapsingHeader("Environment"))
        {
            RenderEnvironment(viewport);
        }
        if (ImGui.CollapsingHeader("Lights"))
        {
            RenderLights(owner);
        }
        if (ImGui.CollapsingHeader("Shadows"))
        {
            RenderShadows(viewport);
        }
        if (ImGui.CollapsingHeader("Viewport"))
        {
            RenderViewport(viewport);
        }

        ImGui.End();
    }

    // ---- sections --------------------------------------------------------------

    private static void RenderEnvironment(Viewport viewport)
    {
        // WorldEnvironment assigns World3D.Environment; a camera-assigned environment overrides it.
        Godot.Environment env = viewport.GetCamera3D()?.Environment;
        string source = "Camera3D";
        if (env == null)
        {
            env = viewport.FindWorld3D()?.Environment;
            source = "WorldEnvironment";
        }
        if (env == null)
        {
            ImGui.TextDisabled("No active Environment.");
            return;
        }
        ImGui.TextDisabled($"Source: {source} ({(string.IsNullOrEmpty(env.ResourcePath) ? "embedded" : env.ResourcePath)})");

        if (ImGui.TreeNode("Background & ambient"))
        {
            Slider("Background energy", env.BackgroundEnergyMultiplier, v => env.BackgroundEnergyMultiplier = v, 0f, 4f);
            Combo("Ambient source", env.AmbientLightSource, v => env.AmbientLightSource = v);
            ColorEdit("Ambient color", env.AmbientLightColor, v => env.AmbientLightColor = v);
            Slider("Ambient energy", env.AmbientLightEnergy, v => env.AmbientLightEnergy = v, 0f, 8f);
            Slider("Ambient sky contribution", env.AmbientLightSkyContribution, v => env.AmbientLightSkyContribution = v, 0f, 1f);
            Combo("Reflected light source", env.ReflectedLightSource, v => env.ReflectedLightSource = v);
            ImGui.TreePop();
        }

        if (ImGui.TreeNode("Tonemap"))
        {
            Combo("Tonemapper", env.TonemapMode, v => env.TonemapMode = v);
            Slider("Exposure", env.TonemapExposure, v => env.TonemapExposure = v, 0f, 4f);
            Slider("White", env.TonemapWhite, v => env.TonemapWhite = v, 0f, 16f);
            ImGui.TreePop();
        }

        if (ImGui.TreeNode("Glow"))
        {
            Checkbox("Enabled##glow", env.GlowEnabled, v => env.GlowEnabled = v);
            Slider("Intensity##glow", env.GlowIntensity, v => env.GlowIntensity = v, 0f, 8f);
            Slider("Strength##glow", env.GlowStrength, v => env.GlowStrength = v, 0f, 2f);
            Slider("Bloom", env.GlowBloom, v => env.GlowBloom = v, 0f, 1f);
            Slider("HDR threshold", env.GlowHdrThreshold, v => env.GlowHdrThreshold = v, 0f, 4f);
            Combo("Blend mode", env.GlowBlendMode, v => env.GlowBlendMode = v);
            ImGui.TreePop();
        }

        if (ImGui.TreeNode("Global illumination & reflections"))
        {
            Checkbox("SSAO", env.SsaoEnabled, v => env.SsaoEnabled = v);
            Slider("SSAO radius", env.SsaoRadius, v => env.SsaoRadius = v, 0.01f, 16f);
            Slider("SSAO intensity", env.SsaoIntensity, v => env.SsaoIntensity = v, 0f, 16f);
            Slider("SSAO power", env.SsaoPower, v => env.SsaoPower = v, 0f, 8f);
            ImGui.Separator();
            Checkbox("SSIL", env.SsilEnabled, v => env.SsilEnabled = v);
            Slider("SSIL radius", env.SsilRadius, v => env.SsilRadius = v, 0.01f, 16f);
            Slider("SSIL intensity", env.SsilIntensity, v => env.SsilIntensity = v, 0f, 16f);
            ImGui.Separator();
            Checkbox("SSR", env.SsrEnabled, v => env.SsrEnabled = v);
            SliderInt("SSR max steps", env.SsrMaxSteps, v => env.SsrMaxSteps = v, 1, 512);
            ImGui.Separator();
            Checkbox("SDFGI", env.SdfgiEnabled, v => env.SdfgiEnabled = v);
            Slider("SDFGI energy", env.SdfgiEnergy, v => env.SdfgiEnergy = v, 0f, 8f);
            ImGui.TreePop();
        }

        if (ImGui.TreeNode("Fog"))
        {
            Checkbox("Enabled##fog", env.FogEnabled, v => env.FogEnabled = v);
            Combo("Mode##fog", env.FogMode, v => env.FogMode = v);
            ColorEdit("Light color##fog", env.FogLightColor, v => env.FogLightColor = v);
            Slider("Light energy##fog", env.FogLightEnergy, v => env.FogLightEnergy = v, 0f, 16f);
            Slider("Sun scatter", env.FogSunScatter, v => env.FogSunScatter = v, 0f, 1f);
            Slider("Density##fog", env.FogDensity, v => env.FogDensity = v, 0f, 0.2f, "%.4f");
            Slider("Sky affect", env.FogSkyAffect, v => env.FogSkyAffect = v, 0f, 1f);
            Slider("Height", env.FogHeight, v => env.FogHeight = v, -100f, 100f);
            Slider("Height density", env.FogHeightDensity, v => env.FogHeightDensity = v, -1f, 1f, "%.4f");
            ImGui.TreePop();
        }

        if (ImGui.TreeNode("Volumetric fog"))
        {
            Checkbox("Enabled##vfog", env.VolumetricFogEnabled, v => env.VolumetricFogEnabled = v);
            Slider("Density##vfog", env.VolumetricFogDensity, v => env.VolumetricFogDensity = v, 0f, 0.5f, "%.4f");
            ColorEdit("Albedo", env.VolumetricFogAlbedo, v => env.VolumetricFogAlbedo = v);
            ColorEdit("Emission", env.VolumetricFogEmission, v => env.VolumetricFogEmission = v);
            Slider("Emission energy", env.VolumetricFogEmissionEnergy, v => env.VolumetricFogEmissionEnergy = v, 0f, 16f);
            Slider("Anisotropy", env.VolumetricFogAnisotropy, v => env.VolumetricFogAnisotropy = v, -0.9f, 0.9f);
            Slider("Length", env.VolumetricFogLength, v => env.VolumetricFogLength = v, 0f, 1024f);
            Slider("GI inject", env.VolumetricFogGIInject, v => env.VolumetricFogGIInject = v, 0f, 16f);
            Slider("Ambient inject", env.VolumetricFogAmbientInject, v => env.VolumetricFogAmbientInject = v, 0f, 16f);
            Slider("Sky affect##vfog", env.VolumetricFogSkyAffect, v => env.VolumetricFogSkyAffect = v, 0f, 1f);
            Checkbox("Temporal reprojection", env.VolumetricFogTemporalReprojectionEnabled, v => env.VolumetricFogTemporalReprojectionEnabled = v);
            ImGui.TreePop();
        }

        if (ImGui.TreeNode("Adjustments"))
        {
            Checkbox("Enabled##adj", env.AdjustmentEnabled, v => env.AdjustmentEnabled = v);
            Slider("Brightness", env.AdjustmentBrightness, v => env.AdjustmentBrightness = v, 0.01f, 4f);
            Slider("Contrast", env.AdjustmentContrast, v => env.AdjustmentContrast = v, 0.01f, 4f);
            Slider("Saturation", env.AdjustmentSaturation, v => env.AdjustmentSaturation = v, 0.01f, 4f);
            ImGui.TreePop();
        }
    }

    private static void RenderLights(Node owner)
    {
        lights.RemoveAll(l => !GodotObject.IsInstanceValid(l));
        ImGui.Text($"{lights.Count} lights");
        ImGui.SameLine();
        if (ImGui.SmallButton("Rescan"))
        {
            RescanLights(owner);
        }
        ImGui.SameLine();
        if (ImGui.SmallButton("All shadows on"))
        {
            lights.ForEach(l => l.ShadowEnabled = true);
        }
        ImGui.SameLine();
        if (ImGui.SmallButton("All shadows off"))
        {
            lights.ForEach(l => l.ShadowEnabled = false);
        }

        foreach (Light3D light in lights)
        {
            ImGui.PushID((int)light.GetInstanceId());
            if (ImGui.TreeNode($"{light.Name} ({light.GetClass()}){(light.Visible ? "" : " [hidden]")}"))
            {
                ImGui.TextDisabled(light.GetPath());
                Checkbox("Visible", light.Visible, v => light.Visible = v);
                ColorEdit("Color", light.LightColor, v => light.LightColor = v);
                Slider("Energy", light.LightEnergy, v => light.LightEnergy = v, 0f, 16f);
                Slider("Indirect energy", light.LightIndirectEnergy, v => light.LightIndirectEnergy = v, 0f, 16f);
                Slider("Volumetric fog energy", light.LightVolumetricFogEnergy, v => light.LightVolumetricFogEnergy = v, 0f, 16f);
                Slider("Specular", light.LightSpecular, v => light.LightSpecular = v, 0f, 16f);

                switch (light)
                {
                    case DirectionalLight3D dir:
                        // Pitch/yaw in degrees so the sun can be swung around live.
                        Vector3 rot = dir.RotationDegrees;
                        Slider("Pitch", rot.X, v => dir.RotationDegrees = new Vector3(v, rot.Y, rot.Z), -180f, 180f);
                        Slider("Yaw", rot.Y, v => dir.RotationDegrees = new Vector3(rot.X, v, rot.Z), -180f, 180f);
                        Combo("Sky mode", dir.SkyMode, v => dir.SkyMode = v);
                        break;
                    case OmniLight3D omni:
                        Slider("Range", omni.OmniRange, v => omni.OmniRange = v, 0f, 100f);
                        Slider("Attenuation", omni.OmniAttenuation, v => omni.OmniAttenuation = v, -4f, 4f);
                        Combo("Shadow mode", omni.OmniShadowMode, v => omni.OmniShadowMode = v);
                        break;
                    case SpotLight3D spot:
                        Slider("Range", spot.SpotRange, v => spot.SpotRange = v, 0f, 100f);
                        Slider("Angle", spot.SpotAngle, v => spot.SpotAngle = v, 0f, 90f);
                        Slider("Angle attenuation", spot.SpotAngleAttenuation, v => spot.SpotAngleAttenuation = v, -4f, 4f);
                        break;
                }

                ImGui.Separator();
                Checkbox("Shadows", light.ShadowEnabled, v => light.ShadowEnabled = v);
                Slider("Shadow bias", light.ShadowBias, v => light.ShadowBias = v, 0f, 2f, "%.3f");
                Slider("Shadow normal bias", light.ShadowNormalBias, v => light.ShadowNormalBias = v, 0f, 10f);
                Slider("Shadow blur", light.ShadowBlur, v => light.ShadowBlur = v, 0f, 10f);
                Slider("Shadow opacity", light.ShadowOpacity, v => light.ShadowOpacity = v, 0f, 1f);
                if (light is DirectionalLight3D d)
                {
                    Combo("Shadow mode", d.DirectionalShadowMode, v => d.DirectionalShadowMode = v);
                    Slider("Shadow max distance", d.DirectionalShadowMaxDistance, v => d.DirectionalShadowMaxDistance = v, 0f, 1000f);
                    Checkbox("Blend splits", d.DirectionalShadowBlendSplits, v => d.DirectionalShadowBlendSplits = v);
                }
                ImGui.TreePop();
            }
            ImGui.PopID();
        }
    }

    private static void RenderShadows(Viewport viewport)
    {
        int atlasIndex = Math.Max(0, Array.IndexOf(atlasSizes, directionalAtlasSize));
        string[] atlasNames = atlasSizes.Select(s => s.ToString()).ToArray();
        bool atlasChanged = ImGui.Combo("Directional atlas size", ref atlasIndex, atlasNames, atlasNames.Length);
        atlasChanged |= ImGui.Checkbox("Directional atlas 16-bit", ref directionalAtlas16Bits);
        if (atlasChanged)
        {
            directionalAtlasSize = atlasSizes[atlasIndex];
            RenderingServer.DirectionalShadowAtlasSetSize(directionalAtlasSize, directionalAtlas16Bits);
        }
        Combo("Directional soft filter", directionalSoftQuality, v =>
        {
            directionalSoftQuality = v;
            RenderingServer.DirectionalSoftShadowFilterSetQuality(v);
        });
        Combo("Positional soft filter", positionalSoftQuality, v =>
        {
            positionalSoftQuality = v;
            RenderingServer.PositionalSoftShadowFilterSetQuality(v);
        });

        int posIndex = Math.Max(0, Array.IndexOf(atlasSizes, viewport.PositionalShadowAtlasSize));
        if (ImGui.Combo("Positional atlas size", ref posIndex, atlasNames, atlasNames.Length))
        {
            viewport.PositionalShadowAtlasSize = atlasSizes[posIndex];
        }
        Checkbox("Positional atlas 16-bit", viewport.PositionalShadowAtlas16Bits, v => viewport.PositionalShadowAtlas16Bits = v);
    }

    private static void RenderViewport(Viewport viewport)
    {
        Combo("MSAA 3D", viewport.Msaa3D, v => viewport.Msaa3D = v);
        Combo("Screen-space AA", viewport.ScreenSpaceAA, v => viewport.ScreenSpaceAA = v);
        Checkbox("TAA", viewport.UseTaa, v => viewport.UseTaa = v);
        Checkbox("Debanding", viewport.UseDebanding, v => viewport.UseDebanding = v);
        Checkbox("Occlusion culling", viewport.UseOcclusionCulling, v => viewport.UseOcclusionCulling = v);
        ImGui.Separator();
        Combo("3D scaling mode", viewport.Scaling3DMode, v => viewport.Scaling3DMode = v);
        Slider("3D scale", viewport.Scaling3DScale, v => viewport.Scaling3DScale = v, 0.25f, 2f);
        Slider("FSR sharpness", viewport.FsrSharpness, v => viewport.FsrSharpness = v, 0f, 2f);
        Slider("Mesh LOD threshold", viewport.MeshLodThreshold, v => viewport.MeshLodThreshold = v, 0f, 1024f);
        ImGui.Separator();
        Combo("Debug draw", viewport.DebugDraw, v => viewport.DebugDraw = v);
        Combo("VSync", DisplayServer.WindowGetVsyncMode(), v => DisplayServer.WindowSetVsyncMode(v));
        SliderInt("Max FPS (0 = unlimited)", Engine.MaxFps, v => Engine.MaxFps = v, 0, 360);
    }

    // ---- widgets ----------------------------------------------------------------
    // Godot properties can't be passed by ref, so each widget takes the current value and a setter.

    private static void Checkbox(string label, bool value, Action<bool> set)
    {
        if (ImGui.Checkbox(label, ref value))
        {
            set(value);
        }
    }

    private static void Slider(string label, float value, Action<float> set, float min, float max, string format = "%.2f")
    {
        if (ImGui.SliderFloat(label, ref value, min, max, format))
        {
            set(value);
        }
    }

    private static void SliderInt(string label, int value, Action<int> set, int min, int max)
    {
        if (ImGui.SliderInt(label, ref value, min, max))
        {
            set(value);
        }
    }

    private static void ColorEdit(string label, Color value, Action<Color> set)
    {
        Vec3 c = new(value.R, value.G, value.B);
        if (ImGui.ColorEdit3(label, ref c, ImGuiColorEditFlags.Float))
        {
            set(new Color(c.X, c.Y, c.Z, value.A));
        }
    }

    // Combo over any Godot enum; skips the trailing "Max" sentinel most of them carry.
    private static void Combo<T>(string label, T value, Action<T> set) where T : struct, Enum
    {
        T[] values = Enum.GetValues<T>().Where(v => v.ToString() != "Max").ToArray();
        string[] names = values.Select(v => v.ToString()).ToArray();
        int index = Array.IndexOf(values, value);
        if (ImGui.Combo(label, ref index, names, names.Length) && index >= 0)
        {
            set(values[index]);
        }
    }
}
