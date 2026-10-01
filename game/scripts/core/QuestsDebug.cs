using Godot;
using ImGuiNET;
using System.Linq;
using Vec2 = System.Numerics.Vector2;

/// <summary>
/// "debugui quests" window: every quest in <see cref="ProgressManager.currentQuests"/> with its text, objective
/// progress bars and rewards, plus a Complete button (<see cref="ProgressManager.CompleteQuest"/>), and the
/// completed quest ids. Local only, like ProgressManager itself. Driven from Console._Process.
/// </summary>
public static class QuestsDebug
{
    public static bool displayQuestsDebugInfo = false;

    public static void Process()
    {
        if (!displayQuestsDebugInfo)
        {
            return;
        }
        ImGui.SetNextWindowPos(new Vec2(460, 480), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSize(new Vec2(420, 360), ImGuiCond.FirstUseEver);
        ImGui.Begin("debugui quests", ref displayQuestsDebugInfo);

        ProgressManager progress = ProgressManager.instance;
        if (progress == null)
        {
            ImGui.TextDisabled("No ProgressManager.");
            ImGui.End();
            return;
        }

        ImGui.Text($"Current: {progress.currentQuests.Count} | Completed: {progress.completedQuests.Count} | Defined: {ProgressManager.allQuests.Count}");
        ImGui.Separator();

        // Copy: Complete changes currentQuests mid-loop.
        foreach (var (questID, quest) in progress.currentQuests.ToList())
        {
            ImGui.PushID(questID);
            bool open = ImGui.CollapsingHeader($"{quest.questName ?? questID}###{questID}", ImGuiTreeNodeFlags.DefaultOpen);
            ImGui.SameLine(ImGui.GetContentRegionAvail().X - 60);
            if (ImGui.SmallButton("Complete"))
            {
                progress.CompleteQuest(questID);
            }
            if (open)
            {
                RenderQuest(questID, quest);
            }
            ImGui.PopID();
        }
        if (progress.currentQuests.Count == 0)
        {
            ImGui.TextDisabled("No current quests.");
        }

        if (ImGui.CollapsingHeader($"Completed ({progress.completedQuests.Count})"))
        {
            foreach (string questID in progress.completedQuests)
            {
                ImGui.BulletText(questID);
            }
        }

        ImGui.End();
    }

    private static void RenderQuest(string questID, Quest quest)
    {
        ImGui.TextDisabled($"{questID} | active: {quest.isActive}");
        if (!string.IsNullOrEmpty(quest.questText))
        {
            ImGui.TextWrapped(quest.questText);
        }

        foreach (var (itemID, required) in quest.itemSubmissionObjectives)
        {
            quest.itemSubmissionProgress.TryGetValue(itemID, out int turnedIn);
            string name = ItemInfo.Fetch(itemID)?.displayName ?? itemID;
            float fraction = required > 0 ? Mathf.Clamp((float)turnedIn / required, 0f, 1f) : 0f;
            ImGui.ProgressBar(fraction, new Vec2(-1, 0), $"{name}  {turnedIn} / {required}");
        }

        if (quest.onCompleteFreeItems.Count > 0)
        {
            ImGui.TextDisabled("Rewards: " + string.Join(", ", quest.onCompleteFreeItems.Select(kv => $"{kv.Value}x {kv.Key}")));
        }
        if (quest.onCompleteUnlockedItems.Count > 0)
        {
            ImGui.TextDisabled("Unlocks items: " + string.Join(", ", quest.onCompleteUnlockedItems));
        }
        if (quest.onCompleteUnlockQuests.Count > 0)
        {
            ImGui.TextDisabled("Unlocks quests: " + string.Join(", ", quest.onCompleteUnlockQuests));
        }
    }
}
