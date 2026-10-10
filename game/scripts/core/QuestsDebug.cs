using Godot;
using ImGuiNET;
using System.Linq;
using Vec2 = System.Numerics.Vector2;

/// <summary>
/// "debugui quests" window: the quests in <see cref="ProgressManager.currentQuests"/> with their text, objective
/// progress bars and rewards, each with a Complete button (<see cref="ProgressManager.CompleteQuest"/>), and the
/// completed quest ids. "Show all quests" lists every defined quest instead, tagged active / completed / locked.
/// Local only, like ProgressManager itself. Driven from Console._Process.
/// </summary>
public static class QuestsDebug
{
    public static bool displayQuestsDebugInfo = false;
    private static bool showAllQuests = false;

    public static void Process()
    {
        if (!displayQuestsDebugInfo)
        {
            return;
        }
        ImGui.SetNextWindowPos(new Vec2(460, 480), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSize(new Vec2(420, 360), ImGuiCond.FirstUseEver);
        ImGui.Begin("debugui quests", ref displayQuestsDebugInfo);

        if (ProgressManager.instance == null)
        {
            ImGui.TextDisabled("No ProgressManager.");
            ImGui.End();
            return;
        }

        ImGui.Checkbox("Show all quests", ref showAllQuests);
        ImGui.Text($"Current: {ProgressManager.currentQuests.Count} | Completed: {ProgressManager.completedQuests.Count} | Defined: {ProgressManager.allQuests.Count}");
        ImGui.Separator();

        if (showAllQuests)
        {
            RenderAllQuests();
        }
        else
        {
            RenderCurrentAndCompleted();
        }

        ImGui.End();
    }

    private static void RenderCurrentAndCompleted()
    {
        // Copy: Complete changes currentQuests mid-loop.
        foreach (var (questID, quest) in ProgressManager.currentQuests.ToList())
        {
            ImGui.PushID(questID);
            if (ImGui.CollapsingHeader($"{quest.questName ?? questID}###{questID}", ImGuiTreeNodeFlags.DefaultOpen))
            {
                RenderQuest(questID, quest, active: true);
            }
            ImGui.PopID();
        }
        if (ProgressManager.currentQuests.Count == 0)
        {
            ImGui.TextDisabled("No current quests.");
        }

        if (ImGui.CollapsingHeader($"Completed ({ProgressManager.completedQuests.Count})"))
        {
            foreach (string questID in ProgressManager.completedQuests)
            {
                ImGui.BulletText(questID);
            }
        }
    }

    private static void RenderAllQuests()
    {
        foreach (var (questID, quest) in ProgressManager.allQuests.OrderBy(kv => kv.Key).ToList())
        {
            bool active = ProgressManager.currentQuests.ContainsKey(questID);
            string status = active ? "ACTIVE" : ProgressManager.completedQuests.Contains(questID) ? "COMPLETED" : "LOCKED";
            ImGui.PushID(questID);
            if (ImGui.CollapsingHeader($"{quest.questName ?? questID}  [{status}]###{questID}"))
            {
                RenderQuest(questID, quest, active);
            }
            ImGui.PopID();
        }
        if (ProgressManager.allQuests.Count == 0)
        {
            ImGui.TextDisabled("No quests defined.");
        }
    }

    /// <summary>One quest's details; the Complete button only works on <paramref name="active"/> (current) quests.</summary>
    private static void RenderQuest(string questID, Quest quest, bool active)
    {
        ImGui.Indent();
        ImGui.BeginDisabled(!active);
        if (ImGui.Button("Complete quest"))
        {
            ProgressManager.CompleteQuest(questID);
        }
        ImGui.EndDisabled();
        ImGui.SameLine();
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
        ImGui.Unindent();
        ImGui.Spacing();
    }
}
