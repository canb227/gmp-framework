using Godot;
using PolyType;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

[GenerateShapeFor<Dictionary<string, (string, int)>>]
public partial class Witness;

[GenerateShape]
public partial struct ProgressData
{
    public List<string> completedQuests;

    public Dictionary<string, (string objItemID, int current)> currentQuests;

    public ProgressData(List<string> completedQuests, Dictionary<string, (string objItemID, int current)> currentQuests)
    {
        this.completedQuests = completedQuests;
        this.currentQuests = currentQuests;
    }
}

public partial class ProgressManager : Node
{
    public static Dictionary<string,Quest> allQuests = new();
    public List<string> completedQuests = new();

    public Dictionary<string, Quest> currentQuests = new();
    public static ProgressManager instance;

    public static event Action QuestStateUpdated;
    public static event Action<string> QuestCompleted;

    public override void _Ready()
    {
        instance = this;
        ItemVoid.AnyItemDespawned += OnItemDespawned;
        loadQuestsFromFile();
        // load game progress from file
        // fix any discrepencies between quests in file and quests in game
        currentQuests.Add("quest_intro_00",allQuests["quest_intro_00"]);
        QuestStateUpdated?.Invoke();
    }

    private void OnItemDespawned(ItemVoid @void, string itemID)
    {

        foreach (var kvp in currentQuests.ToList())
        {
            Quest quest = kvp.Value;
            if (quest.itemSubmissionObjectives.TryGetValue(itemID, out int required))
            {

                quest.itemSubmissionProgress.TryGetValue(itemID, out int turnedIn);
                quest.itemSubmissionProgress[itemID] = Math.Min(turnedIn + 1, required);
                if (AllObjectivesMet(quest))
                {
                    CompleteQuest(kvp.Key);
                }
                QuestStateUpdated?.Invoke();
            }
        }
    }


    public static bool AllObjectivesMet(Quest quest)
    {
        foreach (var (itemID, required) in quest.itemSubmissionObjectives)
        {
            quest.itemSubmissionProgress.TryGetValue(itemID, out int turnedIn);
            if (turnedIn < required)
            {
                return false;
            }
        }
        return true;
    }


    public void CompleteQuest(string questID)
    {
        if (!currentQuests.TryGetValue(questID, out Quest quest))
        {
            return;
        }
        foreach (var (itemID, required) in quest.itemSubmissionObjectives)
        {
            quest.itemSubmissionProgress[itemID] = required;
        }
        completedQuests.Add(questID);
        QuestCompleted?.Invoke(questID);
        currentQuests.Remove(questID);
        foreach (var unlockQuestID in quest.onCompleteUnlockQuests)
        {
            if (allQuests.TryGetValue(unlockQuestID, out Quest unlocked) && !completedQuests.Contains(unlockQuestID))
            {
                currentQuests.TryAdd(unlockQuestID, unlocked);
            }
        }
        foreach (var unlockItemID in quest.onCompleteUnlockedItems)
        {
            ShopUI.AddAvailableItem(unlockItemID);
        }

        QuestStateUpdated?.Invoke();
    }

    public void loadQuestsFromFile()
    {
        allQuests.Clear();
        string[] questFiles = ResourceLoader.ListDirectory("res://game/definitions/quests/");
        foreach (string questFile in questFiles)
        {
            if (questFile.EndsWith(".tres"))
            {
                Quest quest = ResourceLoader.Load<Quest>("res://game/definitions/quests/" + questFile);
                if (quest != null)
                {
                    allQuests.Add(quest.questID, quest);
                }
            }
        }

    }

}