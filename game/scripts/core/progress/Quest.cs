using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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