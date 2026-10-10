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

    [Export]
    public Godot.Collections.Dictionary<string, int> onCompleteFreeItems = new();

    [Export]
    public Godot.Collections.Array<string> onCompleteUnlockedItems = new();

    [Export]
    public Godot.Collections.Array<string> onCompleteUnlockQuests = new();
}