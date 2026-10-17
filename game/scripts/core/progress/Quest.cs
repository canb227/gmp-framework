using Godot;

[GlobalClass]
public partial class Quest : Resource
{
    [Export]
    public string questID;

    [Export] 
    public string questName;

    [Export] 
    public string questText;

    [Export]
    public bool isActive;

    [Export]
    public Godot.Collections.Dictionary<string, int> itemSubmissionObjectives = new();

    [Export]
    public Godot.Collections.Dictionary<string, int> itemSubmissionProgress = new();

    /// <summary>Currency to earn while the quest is in progress: what deposited items are worth counts, spending doesn't take it back.</summary>
    [Export]
    public Godot.Collections.Dictionary<Currency, int> currencyObjectives = new();

    [Export]
    public Godot.Collections.Dictionary<Currency, int> currencyProgress = new();

    /// <summary>Items every player gets on completion (dropped at their feet if they don't fit).</summary>
    [Export]
    public Godot.Collections.Dictionary<string, int> onCompleteFreeItems = new();

    /// <summary>Currency added to the banks on completion.</summary>
    [Export]
    public Godot.Collections.Dictionary<Currency, int> onCompleteCurrency = new();

    [Export]
    public Godot.Collections.Array<string> onCompleteUnlockedItems = new();

    [Export]
    public Godot.Collections.Array<string> onCompleteUnlockQuests = new();
}