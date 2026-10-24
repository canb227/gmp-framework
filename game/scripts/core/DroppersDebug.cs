using ImGuiNET;
using Vec2 = System.Numerics.Vector2;

/// <summary>
/// "debugui droppers" window: every <see cref="ItemDropper"/> in the level, with its seed and items, and the buttons for
/// baking piles: Spawn (fire it now), Clear (despawn its items), Save (write its pile file; with "capture", every item in
/// its capture volume) and Keep seed. Host only, as the host owns the items. Driven from Console._Process.
/// </summary>
public static class DroppersDebug
{
    public static bool displayDroppersDebugInfo = false;
    static bool capture = false;

    public static void Process()
    {
        if (!displayDroppersDebugInfo)
        {
            return;
        }
        ImGui.SetNextWindowPos(new Vec2(460, 10), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSize(new Vec2(560, 300), ImGuiCond.FirstUseEver);
        ImGui.Begin("debugui droppers", ref displayDroppersDebugInfo);

        if (!Lobby.isHost)
        {
            ImGui.TextDisabled("Host only: the host owns the items.");
            ImGui.End();
            return;
        }
        ImGui.Checkbox("Save captures everything in the capture volume", ref capture);
        ImGui.Separator();
        if (ItemDropper.all.Count == 0)
        {
            ImGui.TextDisabled("No droppers in this level.");
        }

        foreach (ItemDropper dropper in ItemDropper.all)
        {
            ImGui.PushID(dropper.GetInstanceId().GetHashCode());
            int alive = 0;
            foreach (ulong id in dropper.ownedItems)
            {
                if (ItemWorld.Get(id) != null)
                {
                    alive++;
                }
            }
            string state = dropper.mode.ToString();
            if (dropper.running)
            {
                state += " (on)";
            }
            if (dropper.itemsLeft > 0)
            {
                state += $" ({dropper.itemsLeft} to go)";
            }
            ImGui.Text($"{dropper.Name}  {state}  seed {dropper.seed}  items {alive}");
            ImGui.TextDisabled(dropper.PilePath() ?? "(not in a saved scene)");
            if (ImGui.Button("Spawn"))
            {
                dropper.Start();
            }
            ImGui.SameLine();
            if (ImGui.Button("Clear"))
            {
                dropper.ClearItems();
            }
            ImGui.SameLine();
            if (ImGui.Button("Save"))
            {
                dropper.SavePile(capture);
            }
            ImGui.SameLine();
            if (ImGui.Button("Keep seed"))
            {
                dropper.KeepSeed();
            }
            ImGui.Separator();
            ImGui.PopID();
        }
        ImGui.End();
    }
}
