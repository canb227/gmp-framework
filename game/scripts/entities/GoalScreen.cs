using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Placeholder wall screen listing the current quests from <see cref="ProgressManager"/>: one row per item
/// objective (quest name, item icon + name, a "turned in / required" bar), up to <see cref="maxRows"/> rows,
/// filled in quest order. Redraws on <see cref="ProgressManager.QuestStateUpdated"/>. Local display only.
/// The scene holds a single template row (<see cref="rowTemplate"/>, laid out in the editor); the rest are
/// duplicates of it stepped down by <see cref="rowSpacing"/>.
/// </summary>
public partial class GoalScreen : Node3D
{
    public const int maxRows = 4;

    /// <summary>Template row with children Icon, QuestLabel, ItemLabel, BarFill and CountLabel.</summary>
    [Export] public Node3D rowTemplate;
    [Export] public float rowSpacing = 0.56f;
    /// <summary>Shown instead of the rows when there are no current objectives.</summary>
    [Export] public Label3D emptyLabel;
    /// <summary>Left edge and full width (in row space) the unit-wide BarFill quad grows across.</summary>
    [Export] public float barLeft = -0.8f;
    [Export] public float barWidth = 1.8f;

    private readonly List<Node3D> rows = new();

    public override void _Ready()
    {
        rows.Add(rowTemplate);
        for (int i = 1; i < maxRows; i++)
        {
            Node3D row = (Node3D)rowTemplate.Duplicate();
            row.Name = $"Row{i}";
            row.Position = rowTemplate.Position + Vector3.Down * rowSpacing * i;
            rowTemplate.GetParent().AddChild(row);
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
        if (ProgressManager.instance?.currentQuests == null)
        {
            yield break;
        }
        foreach (Quest quest in ProgressManager.instance.currentQuests.Values)
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
        List<Objective> objectives = CurrentObjectives().Take(maxRows).ToList();
        for (int i = 0; i < rows.Count; i++)
        {
            rows[i].Visible = i < objectives.Count;
            if (i < objectives.Count)
            {
                FillRow(rows[i], objectives[i]);
            }
        }
        emptyLabel.Visible = objectives.Count == 0;
    }

    private void FillRow(Node3D row, Objective objective)
    {
        ItemInfo item = ItemInfo.Fetch(objective.itemID);
        row.GetNode<Sprite3D>("Icon").Texture = item?.icon;
        row.GetNode<Label3D>("QuestLabel").Text = (objective.questName ?? "").ToUpperInvariant();
        row.GetNode<Label3D>("ItemLabel").Text = item?.displayName ?? objective.itemID;
        row.GetNode<Label3D>("CountLabel").Text = $"{objective.turnedIn} / {objective.required}";

        MeshInstance3D barFill = row.GetNode<MeshInstance3D>("BarFill");
        float fraction = objective.required > 0 ? Math.Clamp((float)objective.turnedIn / objective.required, 0f, 1f) : 0f;
        barFill.Visible = fraction > 0f;
        barFill.Scale = new Vector3(barWidth * fraction, 1f, 1f);
        barFill.Position = new Vector3(barLeft + barWidth * fraction * 0.5f, barFill.Position.Y, barFill.Position.Z);
    }
}
