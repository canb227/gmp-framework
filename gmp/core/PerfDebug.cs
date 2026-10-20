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

    // ---- where the frame goes, sampled once a second ----
    // Network sync and follower costs come from running totals (GameWorld.StateSync, GMPOBody) as ms of
    // main-thread time per second of play: 1000 ms/s is one whole core. Written to the log every few seconds during a
    // game, so a test's logs from every peer show it afterwards.
    private const double sampleInterval = 1.0;
    private const double logInterval = 5.0;
    private static double sampleTimer = 0;
    private static double logTimer = 0;
    private static bool measuringRenderTime = false;

    private static long lastApplyCost, lastBuildCost, lastSendCost, lastReceiveCost, lastServiceCost;
    private static long lastApplied, lastBuilt, lastSent, lastReceived, lastSentBytes, lastReceivedBytes;
    private static double applyMs, buildMs, sendMs, receiveMs, followMs, serviceMs;
    /// <summary>The physics world's own running totals for its loose items (states built and applied, ms of playback), as last read.</summary>
    private static double lastItemFollowMs;
    private static long lastItemsBuilt, lastItemsApplied;
    private static double appliedPerSecond, builtPerSecond, sentPerSecond, receivedPerSecond, followers, sentKBps, receivedKBps;
    private static double renderCpuMs, renderGpuMs, physicsTickMs, processMs, stepMs;

    // ---- average physics tick ----
    // Godot's physics monitor is the worst tick of each second. These time every tick from the first node's physics
    // callback to the last one's (PhysicsTimer nodes at the lowest and highest process_physics_priority, made by
    // Console): every _PhysicsProcess, Box3D's step and its writing transforms to the nodes. Not included: what the
    // engine does between ticks, such as physics interpolation's bookkeeping.
    public static long physicsTickStart, physicsTickCost, physicsTicks;
    private static long lastPhysicsTickCost, lastPhysicsTicks;
    private static double physicsTickAvgMs;
    private static int awakeBodies;

    public static void Process(double delta)
    {
        // Sample even when hidden so the graph is already full when opened.
        frameTimes[historyHead] = (float)(delta * 1000.0);
        historyHead = (historyHead + 1) % historySize;
        historyCount = Math.Min(historyCount + 1, historySize);

        if (!measuringRenderTime)
        {
            measuringRenderTime = true;
            RenderingServer.ViewportSetMeasureRenderTime(((SceneTree)Engine.GetMainLoop()).Root.GetViewportRid(), true);
        }
        sampleTimer += delta;
        if (sampleTimer >= sampleInterval)
        {
            Sample(sampleTimer);
            sampleTimer = 0;
        }
        logTimer += delta;
        if (logTimer >= logInterval)
        {
            logTimer = 0;
            if (GameWorld.syncedObjs.Count > 0)
            {
                RecomputeStats();
                LogSample();
            }
        }

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

    /// <summary>Turns the running totals into per-second figures over the last <paramref name="seconds"/>.</summary>
    private static void Sample(double seconds)
    {
        double msPerTick = 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        applyMs = (GameWorld.applyCost - lastApplyCost) * msPerTick / seconds;
        buildMs = (GameWorld.buildCost - lastBuildCost) * msPerTick / seconds;
        sendMs = (GameWorld.sendCost - lastSendCost) * msPerTick / seconds;
        receiveMs = (GameWorld.receiveCost - lastReceiveCost) * msPerTick / seconds;
        serviceMs = (Lobby.serviceCost - lastServiceCost) * msPerTick / seconds;
        sentKBps = (GameWorld.sentBytes - lastSentBytes) / 1024.0 / seconds;
        receivedKBps = (GameWorld.receivedBytes - lastReceivedBytes) / 1024.0 / seconds;
        appliedPerSecond = (GameWorld.appliedCount - lastApplied) / seconds;
        builtPerSecond = (GameWorld.builtCount - lastBuilt) / seconds;
        sentPerSecond = (GameWorld.sentCount - lastSent) / seconds;
        receivedPerSecond = (GameWorld.receivedCount - lastReceived) / seconds;
        // Bodies are built, applied and played back in C++ (its build and apply time is in GameWorld's totals already,
        // as GameWorld makes those calls): add their counts and their playback.
        if (GodotObject.IsInstanceValid(GameWorld.physics))
        {
            using Godot.Collections.Dictionary stats = PhysicsWorld.Stats();
            double itemFollowMs = stats["follow_ms_total"].AsDouble();
            long itemsBuilt = stats["states_built"].AsInt64();
            long itemsApplied = stats["states_applied"].AsInt64();
            followMs = (itemFollowMs - lastItemFollowMs) / seconds;
            builtPerSecond += (itemsBuilt - lastItemsBuilt) / seconds;
            appliedPerSecond += (itemsApplied - lastItemsApplied) / seconds;
            followers = stats["followers"].AsInt32();
            lastItemFollowMs = itemFollowMs;
            lastItemsBuilt = itemsBuilt;
            lastItemsApplied = itemsApplied;
        }

        lastApplyCost = GameWorld.applyCost;
        lastBuildCost = GameWorld.buildCost;
        lastSendCost = GameWorld.sendCost;
        lastReceiveCost = GameWorld.receiveCost;
        lastServiceCost = Lobby.serviceCost;
        lastSentBytes = GameWorld.sentBytes;
        lastReceivedBytes = GameWorld.receivedBytes;
        lastApplied = GameWorld.appliedCount;
        lastBuilt = GameWorld.builtCount;
        lastSent = GameWorld.sentCount;
        lastReceived = GameWorld.receivedCount;

        Rid viewport = ((SceneTree)Engine.GetMainLoop()).Root.GetViewportRid();
        renderCpuMs = RenderingServer.ViewportGetMeasuredRenderTimeCpu(viewport) + RenderingServer.GetFrameSetupTimeCpu();
        renderGpuMs = RenderingServer.ViewportGetMeasuredRenderTimeGpu(viewport);
        processMs = Mon(Performance.Monitor.TimeProcess) * 1000;
        physicsTickMs = Mon(Performance.Monitor.TimePhysicsProcess) * 1000;
        long ticks = physicsTicks - lastPhysicsTicks;
        physicsTickAvgMs = 0;
        if (ticks > 0)
            physicsTickAvgMs = (physicsTickCost - lastPhysicsTickCost) * msPerTick / ticks;
        lastPhysicsTickCost = physicsTickCost;
        lastPhysicsTicks = physicsTicks;
        stepMs = 0;
        awakeBodies = 0;
        if (GodotObject.IsInstanceValid(GameWorld.physics))
        {
            using Godot.Collections.Dictionary stats = PhysicsWorld.Stats();
            stepMs = stats["step_ms"].AsSingle();
            awakeBodies = stats["awake"].AsInt32();
        }
    }

    private static void LogSample()
    {
        Logging.Log($"fps {Mon(Performance.Monitor.TimeFps):0} (frame avg {avgMs:0.0} max {maxMs:0.0} ms)"
            + $" | process {processMs:0.0} ms | physics tick avg {physicsTickAvgMs:0.0} worst {physicsTickMs:0.0} ms (Box3D step {stepMs:0.0} ms, awake {awakeBodies})"
            + $" | render cpu {renderCpuMs:0.0} gpu {renderGpuMs:0.0} ms, draw calls {Mon(Performance.Monitor.RenderTotalDrawCallsInFrame):0}, objects {Mon(Performance.Monitor.RenderTotalObjectsInFrame):0}"
            + $" | synced {GameWorld.syncedObjs.Count}, followers {followers:0} ({followMs:0} ms/s)"
            + $" | sync ms/s: build {buildMs:0} ({builtPerSecond:0}/s), send {sendMs:0} ({sentPerSecond:0}/s), receive {receiveMs:0} ({receivedPerSecond:0}/s), apply {applyMs:0} ({appliedPerSecond:0}/s)"
            + $" | transport service {serviceMs:0} ms/s | state out {sentKBps:0} KB/s, in {receivedKBps:0} KB/s",
            "Perf");
        if (Lobby.network is SteamNetwork steam)
        {
            steam.LogConnectionStatus();
        }
        if (Lobby.network is ENetNetwork enet)
        {
            Logging.Log(enet.ConnectionStatus(), "Perf");
        }
    }

    /// <summary>Logs the latest second's sample now (the console's perf command), as the log does every few seconds.</summary>
    public static void LogNow()
    {
        RecomputeStats();
        LogSample();
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
            ImGui.Text($"Physics process: {Mon(Performance.Monitor.TimePhysicsProcess) * 1000:0.00} ms worst, {physicsTickAvgMs:0.00} ms avg (tick {Engine.PhysicsTicksPerSecond} Hz)");
            ImGui.Text($"Navigation: {Mon(Performance.Monitor.TimeNavigationProcess) * 1000:0.00} ms");
            ImGui.Text($"Time scale: {Engine.TimeScale:0.00} | Frames drawn: {Engine.GetFramesDrawn()}");
            ImGui.Text($"Render: cpu {renderCpuMs:0.00} ms | gpu {renderGpuMs:0.00} ms");
        }

        // ms of main-thread time per second of play (1000 = one whole core), over the last second.
        if (ImGui.CollapsingHeader("Network sync cost", ImGuiTreeNodeFlags.DefaultOpen))
        {
            ImGui.Text($"Build (priority + state): {buildMs:0.0} ms/s | {builtPerSecond:0} objects/s");
            ImGui.Text($"Send (serialize + Steam): {sendMs:0.0} ms/s | {sentPerSecond:0} states/s");
            ImGui.Text($"Receive (deserialize): {receiveMs:0.0} ms/s | {receivedPerSecond:0} states/s");
            ImGui.Text($"Apply: {applyMs:0.0} ms/s | {appliedPerSecond:0} states/s");
            ImGui.Text($"Followers: {followers:0} | {followMs:0.0} ms/s");
            ImGui.Text($"Transport service: {serviceMs:0.0} ms/s | state out {sentKBps:0} KB/s, in {receivedKBps:0} KB/s");
        }

        if (ImGui.CollapsingHeader("Rendering", ImGuiTreeNodeFlags.DefaultOpen))
        {
            ImGui.Text($"Draw calls: {Mon(Performance.Monitor.RenderTotalDrawCallsInFrame):0}");
            ImGui.Text($"Objects: {Mon(Performance.Monitor.RenderTotalObjectsInFrame):0} | Primitives: {Mon(Performance.Monitor.RenderTotalPrimitivesInFrame):0}");
            ImGui.Text($"Video mem: {Mb(Mon(Performance.Monitor.RenderVideoMemUsed))} (tex {Mb(Mon(Performance.Monitor.RenderTextureMemUsed))}, buf {Mb(Mon(Performance.Monitor.RenderBufferMemUsed))})");
            ImGui.Text($"Adapter: {RenderingServer.GetVideoAdapterName()}");
        }

        // Godot's Physics3D monitors read 0: the engine runs the Dummy physics server and Box3D owns the simulation.
        if (ImGui.CollapsingHeader("Physics (Box3D)", ImGuiTreeNodeFlags.DefaultOpen))
        {
            if (!GodotObject.IsInstanceValid(GameWorld.physics))
            {
                ImGui.Text("No physics world");
            }
            else
            {
                Godot.Collections.Dictionary stats = PhysicsWorld.Stats();
                ImGui.Text($"Step: {stats["step_ms"].AsDouble():0.00} ms | events {stats["events_ms"].AsDouble():0.00} ms | item drawing {stats["render_ms"].AsDouble():0.00} ms");
                ImGui.Text($"Items: {stats["items"]} | awake bodies: {stats["awake"]} | following: {stats["followers"]}");
                if (ImGui.TreeNodeEx("Step profile (ms) and counters", ImGuiTreeNodeFlags.DefaultOpen))
                {
                    foreach (var (key, value) in PhysicsWorld.Profile())
                    {
                        ImGui.Text($"{key}: {value}");
                    }
                    ImGui.TreePop();
                }

            }
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

/// <summary>
/// Marks the start (lowest process_physics_priority, runs first) or end (highest, runs last) of each physics tick for
/// <see cref="PerfDebug"/>'s average physics tick. Console adds one of each.
/// </summary>
public partial class PhysicsTimer : Node
{
    readonly bool isStart;

    public PhysicsTimer(bool start)
    {
        isStart = start;
        if (start)
        {
            Name = "PhysicsTimerStart";
            ProcessPhysicsPriority = int.MinValue;
        }
        else
        {
            Name = "PhysicsTimerEnd";
            ProcessPhysicsPriority = int.MaxValue;
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (isStart)
        {
            PerfDebug.physicsTickStart = System.Diagnostics.Stopwatch.GetTimestamp();
            return;
        }
        if (PerfDebug.physicsTickStart != 0)
        {
            PerfDebug.physicsTickCost += System.Diagnostics.Stopwatch.GetTimestamp() - PerfDebug.physicsTickStart;
            PerfDebug.physicsTicks++;
        }
    }
}
