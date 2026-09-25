using ImGuiNET;
using System.Linq;

/// <summary>
/// GameWorld: ImGui debug overlay listing synced objects and their sync priority state.
/// </summary>
public partial class GameWorld
{
    public static bool displaySyncedObjectDebugInfo = false;
    public override void _Process(double delta)
    {
        if (displaySyncedObjectDebugInfo)
        {
            ImGui.Begin("Synced Objects");
            ImGui.Text($"Synced Objects: {syncedObjs.Count}");
            ImGui.Text($"Synced Objects with Auth: {syncedObjs.Count(o => o.Value.authority == Lobby.selfPeerID)}");
            foreach (var kvp in syncedObjs.OrderByDescending(e => e.Value.priorityAccumulator).ToList())
            {
                ImGui.Text($"ID: {kvp.Key} | Auth: {kvp.Value.authority} | Owner: {kvp.Value.owner} | Priority: {kvp.Value.priority} | Accumulator: {kvp.Value.priorityAccumulator}");
            }
            ImGui.End();
        }

    }
}
