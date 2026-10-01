using Godot;
using ImGuiNET;
using System;

/// <summary>
/// "debugui perf" window: FPS, frame-time graph + stats, engine timings, render / physics / object counts,
/// and memory (engine static + .NET GC). Driven from Console._Process, which runs while the tree is paused.
/// </summary>
public static class PerfDebug
{
    public static bool displayPerfDebugInfo = false;

    // Ring buffer of recent frame times (ms) for the graph and min/avg/max/1% low.
    private const int historySize = 240;
    private static readonly float[] frameTimes = new float[historySize];
    private static readonly float[] sortScratch = new float[historySize];
    private static int historyHead = 0;
    private static int historyCount = 0;

    // Stats are recomputed a few times a second so the text is readable.
    private const double statsInterval = 0.25;
    private static double statsTimer = 0;
    private static float minMs, avgMs, maxMs, lowFps1;

    public static void Process(double delta)
    {
        // Sample even when hidden so the graph is already full when opened.
        frameTimes[historyHead] = (float)(delta * 1000.0);
        historyHead = (historyHead + 1) % historySize;
        historyCount = Math.Min(historyCount + 1, historySize);

        if (!displayPerfDebugInfo)
        {
            return;
        }

        statsTimer += delta;
        if (statsTimer >= statsInterval)
        {
            statsTimer = 0;
            RecomputeStats();
        }
        Render();
    }

    private static void RecomputeStats()
    {
        if (historyCount == 0)
        {
            return;
        }
        float sum = 0;
        for (int i = 0; i < historyCount; i++)
        {
            sortScratch[i] = frameTimes[i];
            sum += frameTimes[i];
        }
        Array.Sort(sortScratch, 0, historyCount);
        minMs = sortScratch[0];
        maxMs = sortScratch[historyCount - 1];
        avgMs = sum / historyCount;

        // 1% low = average FPS of the slowest 1% of frames.
        int worstCount = Math.Max(1, historyCount / 100);
        float worstSum = 0;
        for (int i = historyCount - worstCount; i < historyCount; i++)
        {
            worstSum += sortScratch[i];
        }
        lowFps1 = 1000f / (worstSum / worstCount);
    }

    private static double Mon(Performance.Monitor m) => Performance.GetMonitor(m);

    private static string Mb(double bytes) => $"{bytes / (1024.0 * 1024.0):0.0} MB";

    private static void Render()
    {
        ImGui.SetNextWindowPos(new System.Numerics.Vector2(10, 10), ImGuiCond.FirstUseEver);
        ImGui.Begin("debugui perf", ref displayPerfDebugInfo);

        double fps = Mon(Performance.Monitor.TimeFps);
        ImGui.Text($"FPS: {fps:0} | 1% low: {lowFps1:0} | Max FPS: {Engine.MaxFps} | VSync: {DisplayServer.WindowGetVsyncMode()}");
        ImGui.Text($"Frame ms  min: {minMs:0.00} | avg: {avgMs:0.00} | max: {maxMs:0.00}");

        // PlotLines wants the buffer in chronological order starting at the offset.
        ImGui.PlotLines("##frametimes", ref frameTimes[0], historySize, historyHead,
            $"{avgMs:0.00} ms", 0f, Math.Max(33.4f, maxMs * 1.1f), new System.Numerics.Vector2(0, 60));

        if (ImGui.CollapsingHeader("Timings", ImGuiTreeNodeFlags.DefaultOpen))
        {
            ImGui.Text($"Process: {Mon(Performance.Monitor.TimeProcess) * 1000:0.00} ms");
            ImGui.Text($"Physics process: {Mon(Performance.Monitor.TimePhysicsProcess) * 1000:0.00} ms (tick {Engine.PhysicsTicksPerSecond} Hz)");
            ImGui.Text($"Navigation: {Mon(Performance.Monitor.TimeNavigationProcess) * 1000:0.00} ms");
            ImGui.Text($"Time scale: {Engine.TimeScale:0.00} | Frames drawn: {Engine.GetFramesDrawn()}");
        }

        if (ImGui.CollapsingHeader("Rendering", ImGuiTreeNodeFlags.DefaultOpen))
        {
            ImGui.Text($"Draw calls: {Mon(Performance.Monitor.RenderTotalDrawCallsInFrame):0}");
            ImGui.Text($"Objects: {Mon(Performance.Monitor.RenderTotalObjectsInFrame):0} | Primitives: {Mon(Performance.Monitor.RenderTotalPrimitivesInFrame):0}");
            ImGui.Text($"Video mem: {Mb(Mon(Performance.Monitor.RenderVideoMemUsed))} (tex {Mb(Mon(Performance.Monitor.RenderTextureMemUsed))}, buf {Mb(Mon(Performance.Monitor.RenderBufferMemUsed))})");
            ImGui.Text($"Adapter: {RenderingServer.GetVideoAdapterName()}");
        }

        if (ImGui.CollapsingHeader("Physics", ImGuiTreeNodeFlags.DefaultOpen))
        {
            ImGui.Text($"3D active bodies: {Mon(Performance.Monitor.Physics3DActiveObjects):0}");
            ImGui.Text($"3D collision pairs: {Mon(Performance.Monitor.Physics3DCollisionPairs):0} | Islands: {Mon(Performance.Monitor.Physics3DIslandCount):0}");
        }

        if (ImGui.CollapsingHeader("Objects & memory", ImGuiTreeNodeFlags.DefaultOpen))
        {
            ImGui.Text($"Nodes: {Mon(Performance.Monitor.ObjectNodeCount):0} | Orphan nodes: {Mon(Performance.Monitor.ObjectOrphanNodeCount):0}");
            ImGui.Text($"Objects: {Mon(Performance.Monitor.ObjectCount):0} | Resources: {Mon(Performance.Monitor.ObjectResourceCount):0}");
            ImGui.Text($"Engine static mem: {Mb(Mon(Performance.Monitor.MemoryStatic))} (peak {Mb(Mon(Performance.Monitor.MemoryStaticMax))})");
            ImGui.Text($".NET managed heap: {Mb(GC.GetTotalMemory(false))}");
            ImGui.Text($"GC collections  gen0: {GC.CollectionCount(0)} | gen1: {GC.CollectionCount(1)} | gen2: {GC.CollectionCount(2)}");
        }

        ImGui.End();
    }
}
