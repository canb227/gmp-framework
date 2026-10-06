using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Wall screen listing the current quests from <see cref="ProgressManager"/>: one row per item objective (quest
/// name, item icon + name, "turned in / required" and a progress bar), up to <see cref="maxRows"/> rows, filled in
/// quest order. Redraws on <see cref="ProgressManager.QuestStateUpdated"/>. Local display only.
/// The screen is a Control UI rendered in the UIViewport SubViewport and shown on the Display quad. It holds one
/// template row (%RowTemplate, laid out in the editor); the other rows are duplicates of it.
/// </summary>
public partial class GoalScreen : Node3D
{
    public const int maxRows = 4;

    private readonly List<Control> rows = new();
    private Control emptyLabel;
    private Label activeCount;

    public override void _Ready()
    {
        Control rowTemplate = GetNode<Control>("%RowTemplate");
        Control rowContainer = GetNode<Control>("%Rows");
        emptyLabel = GetNode<Control>("%EmptyLabel");
        activeCount = GetNode<Label>("%ActiveCount");

        rows.Add(rowTemplate);
        for (int i = 1; i < maxRows; i++)
        {
            Control row = (Control)rowTemplate.Duplicate();
            row.Name = $"Row{i}";
            rowContainer.AddChild(row);
            rows.Add(row);
        }
        ProgressManager.QuestStateUpdated += ProgressManager_QuestStateUpdated;
        Refresh();
    }

    public override void _ExitTree()
    {
        ProgressManager.QuestStateUpdated -= ProgressManager_QuestStateUpdated;
    }

    private void ProgressManager_QuestStateUpdated()
    {
        Refresh();
    }

    /// <summary>One displayed row: an item objective of a current quest.</summary>
    private record struct Objective(string questName, string itemID, int turnedIn, int required);

    private static IEnumerable<Objective> CurrentObjectives()
    {
        if (ProgressManager.currentQuests == null)
        {
            yield break;
        }
        foreach (Quest quest in ProgressManager.currentQuests.Values)
        {
            if (quest == null)
            {
                continue;
            }
            foreach (var (itemID, required) in quest.itemSubmissionObjectives)
            {
                quest.itemSubmissionProgress.TryGetValue(itemID, out int turnedIn);
                yield return new Objective(quest.questName ?? quest.questID, itemID, turnedIn, required);
            }
        }
    }

    public void Refresh()
    {
        List<Objective> all = CurrentObjectives().ToList();
        List<Objective> shown = all.Take(maxRows).ToList();
        for (int i = 0; i < rows.Count; i++)
        {
            rows[i].Visible = i < shown.Count;
            if (i < shown.Count)
            {
                FillRow(rows[i], shown[i]);
            }
        }
        emptyLabel.Visible = shown.Count == 0;
        activeCount.Text = all.Count > maxRows
            ? $"{maxRows} OF {all.Count} OBJECTIVES"
            : $"{all.Count} OBJECTIVE{(all.Count == 1 ? "" : "S")}";
    }

    private static void FillRow(Control row, Objective objective)
    {
        ItemInfo item = ItemInfo.Fetch(objective.itemID);
        row.GetNode<TextureRect>("Line/IconWell/Icon").Texture = item?.icon;
        row.GetNode<Label>("Line/Info/QuestLabel").Text = (objective.questName ?? "").ToUpperInvariant();
        row.GetNode<Label>("Line/Info/NameRow/ItemLabel").Text = item?.displayName ?? objective.itemID;
        row.GetNode<Label>("Line/Info/NameRow/CountLabel").Text = $"{objective.turnedIn} / {objective.required}";
        row.GetNode<ProgressBar>("Line/Info/Bar").Value =
            objective.required > 0 ? Math.Clamp((double)objective.turnedIn / objective.required, 0, 1) : 0;
    }
}
