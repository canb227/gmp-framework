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
        ProgressManager.QuestStateUpdated += Refresh;
        Refresh();
    }

    public override void _ExitTree()
    {
        ProgressManager.QuestStateUpdated -= Refresh;
    }

    /// <summary>One displayed row: an item or currency objective of a current quest.</summary>
    private record struct Objective(string questName, Texture2D icon, string name, int progress, int required);

    private static IEnumerable<Objective> CurrentObjectives()
    {
        foreach (Quest quest in ProgressManager.currentQuests.Values)
        {
            string questName = quest.questName ?? quest.questID;
            foreach (var (itemID, required) in quest.itemSubmissionObjectives)
            {
                quest.itemSubmissionProgress.TryGetValue(itemID, out int turnedIn);
                ItemInfo item = ItemInfo.Fetch(itemID);
                yield return new Objective(questName, item?.icon, item?.displayName ?? itemID, turnedIn, required);
            }
            foreach (var (currency, required) in quest.currencyObjectives)
            {
                quest.currencyProgress.TryGetValue(currency, out int earned);
                CurrencyInfo info = Shop.Currencies[currency];
                yield return new Objective(questName, info.icon, info.name, earned, required);
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
        row.GetNode<TextureRect>("Line/IconWell/Icon").Texture = objective.icon;
        row.GetNode<Label>("Line/Info/QuestLabel").Text = (objective.questName ?? "").ToUpperInvariant();
        row.GetNode<Label>("Line/Info/NameRow/ItemLabel").Text = objective.name;
        row.GetNode<Label>("Line/Info/NameRow/CountLabel").Text = $"{objective.progress} / {objective.required}";
        row.GetNode<ProgressBar>("Line/Info/Bar").Value =
            objective.required > 0 ? Math.Clamp((double)objective.progress / objective.required, 0, 1) : 0;
    }
}
