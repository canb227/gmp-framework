using Godot;
using System;

/// <summary>
/// The quests in progress (<see cref="ProgressManager.currentQuests"/>) in the top right corner: each quest's name,
/// then one line per objective (icon, name, "turned in / required" and a thin progress bar). Rebuilt on
/// <see cref="ProgressManager.QuestStateUpdated"/>. Local display only.
/// <para>
/// Lives on its own canvas layer above the menu screens (see <see cref="UIManager.BindHud"/>), so it shows in the
/// pause menu, shop and inventory too. The arrow collapses it to its header; it can only be clicked while the mouse
/// is free. While a screen is open it sits lower (<see cref="SetUnderScreen"/>) to clear the screens' headers.
/// </para>
/// </summary>
public partial class QuestTracker : MarginContainer
{
    const int TopMargin = 40;
    // Below a screen's header row (the shop's EXIT button sits in the top right).
    const int TopMarginUnderScreen = 200;

    static readonly Color AccentColor = new(1f, 0.55f, 0.05f);
    static readonly Color DimColor = new(0.5f, 0.5f, 0.53f);

    /// <summary>Kept for the whole run, so a collapsed tracker stays collapsed in the next game.</summary>
    static bool collapsed;

    Label count;
    Button collapseButton;
    Control body;
    VBoxContainer rows;

    public override void _Ready()
    {
        count = GetNode<Label>("%Count");
        collapseButton = GetNode<Button>("%CollapseButton");
        body = GetNode<Control>("%Body");
        rows = GetNode<VBoxContainer>("%Rows");

        collapseButton.Pressed += OnCollapsePressed;
        ProgressManager.QuestStateUpdated += Rebuild;
        ProgressManager.QuestCompleted += OnQuestCompleted;
        ApplyCollapsed();
        Rebuild();
    }

    public override void _ExitTree()
    {
        ProgressManager.QuestStateUpdated -= Rebuild;
        ProgressManager.QuestCompleted -= OnQuestCompleted;
    }

    /// <summary>From UIManager: a menu screen is open (true) or all are closed.</summary>
    public void SetUnderScreen(bool underScreen)
    {
        int top = TopMargin;
        if (underScreen)
        {
            top = TopMarginUnderScreen;
        }
        AddThemeConstantOverride("margin_top", top);
    }

    void OnCollapsePressed()
    {
        collapsed = !collapsed;
        ApplyCollapsed();
    }

    void ApplyCollapsed()
    {
        body.Visible = !collapsed;
        if (collapsed)
        {
            collapseButton.Text = "▾";
        }
        else
        {
            collapseButton.Text = "▴";
        }
    }

    /// <summary>A quest was just completed (on every peer). Runs before the quests it unlocks join currentQuests.</summary>
    void OnQuestCompleted(string questID)
    {
        // TODO(human): should a collapsed tracker pop back open here, so players notice the new quest?
        // Set `collapsed` and call ApplyCollapsed() to change it.
    }

    void Rebuild()
    {
        foreach (Node child in rows.GetChildren())
        {
            rows.RemoveChild(child);
            child.QueueFree();
        }

        count.Text = ProgressManager.currentQuests.Count.ToString();
        if (ProgressManager.currentQuests.Count == 0)
        {
            Label empty = MakeLabel("No active quests", 22, DimColor);
            rows.AddChild(empty);
            return;
        }

        foreach (Quest quest in ProgressManager.currentQuests.Values)
        {
            VBoxContainer questBox = new() { MouseFilter = MouseFilterEnum.Ignore };
            questBox.AddThemeConstantOverride("separation", 6);
            string questName = quest.questName ?? quest.questID;
            questBox.AddChild(MakeLabel(questName.ToUpperInvariant(), 22, AccentColor));

            foreach (var (itemID, required) in quest.itemSubmissionObjectives)
            {
                quest.itemSubmissionProgress.TryGetValue(itemID, out int turnedIn);
                ItemInfo item = ItemInfo.Fetch(itemID);
                Texture2D icon = null;
                string name = itemID;
                if (item != null)
                {
                    icon = item.icon;
                    name = item.displayName;
                }
                AddObjective(questBox, icon, name, turnedIn, required);
            }
            foreach (var (currency, required) in quest.currencyObjectives)
            {
                quest.currencyProgress.TryGetValue(currency, out int earned);
                CurrencyInfo info = Shop.Currencies[currency];
                AddObjective(questBox, info.icon, info.name, earned, required);
            }
            rows.AddChild(questBox);
        }
    }

    static void AddObjective(VBoxContainer questBox, Texture2D icon, string name, int progress, int required)
    {
        HBoxContainer line = new() { MouseFilter = MouseFilterEnum.Ignore };
        line.AddThemeConstantOverride("separation", 10);
        line.AddChild(new TextureRect
        {
            Texture = icon,
            CustomMinimumSize = new Vector2(32, 32),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
            MouseFilter = MouseFilterEnum.Ignore,
        });
        Label nameLabel = MakeLabel(name, 24, new Color(0.88f, 0.88f, 0.9f));
        nameLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        line.AddChild(nameLabel);
        line.AddChild(MakeLabel($"{progress} / {required}", 24, new Color(0.88f, 0.88f, 0.9f)));
        questBox.AddChild(line);

        double fraction = 0;
        if (required > 0)
        {
            fraction = Math.Clamp((double)progress / required, 0, 1);
        }
        questBox.AddChild(new ProgressBar
        {
            MinValue = 0,
            MaxValue = 1,
            Step = 0,
            Value = fraction,
            ShowPercentage = false,
            CustomMinimumSize = new Vector2(0, 6),
            MouseFilter = MouseFilterEnum.Ignore,
        });
    }

    static Label MakeLabel(string text, int fontSize, Color color)
    {
        Label label = new()
        {
            Text = text,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        label.AddThemeFontSizeOverride("font_size", fontSize);
        label.AddThemeColorOverride("font_color", color);
        return label;
    }
}
