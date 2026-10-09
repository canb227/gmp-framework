using Godot;
using PolyType;
using System;
using System.Collections.Generic;
using System.Linq;

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

/// <summary>
/// Quests: every defined quest (<see cref="allQuests"/>), the ones in progress (<see cref="currentQuests"/>) with
/// their turn-in progress, and the ones done (<see cref="completedQuests"/>).
/// <para>
/// Multiplayer: the lobby host owns quest state. Only the host turns items in and completes quests, broadcasting
/// each change to every peer, itself included; it also puts unlocked blueprints on sale (the <see cref="Shop"/>
/// shares that itself). A peer that connects later is sent the whole state.
/// </para>
/// </summary>
public partial class ProgressManager : Node
{
    public static Dictionary<string, Quest> allQuests = new();
    public static List<string> completedQuests = new();
    public static Dictionary<string, Quest> currentQuests = new();
    public static ProgressManager instance;

    public static event Action QuestStateUpdated;
    public static event Action<string> QuestCompleted;

    public override void _Ready()
    {
        instance = this;
        Lobby.PeerConnectedEvent += SendStateTo;
        loadQuestsFromFile();
        currentQuests.Add("quest_intro_00", allQuests["quest_intro_00"]);
        QuestStateUpdated?.Invoke();
    }

    public override void _ExitTree()
    {
        Lobby.PeerConnectedEvent -= SendStateTo;
    }

    public static void loadQuestsFromFile()
    {
        allQuests.Clear();
        foreach (string questFile in ResourceLoader.ListDirectory("res://game/definitions/quests/"))
        {
            if (questFile.EndsWith(".tres") && ResourceLoader.Load<Quest>("res://game/definitions/quests/" + questFile) is Quest quest)
            {
                allQuests.Add(quest.questID, quest);
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

    // ---- turn-ins ---------------------------------------------------------------

    /// <summary>Host only: counts one <paramref name="itemID"/> toward every quest in progress that wants it, for everyone.</summary>
    internal static void TurnInForQuest(string itemID)
    {
        foreach (var (questID, quest) in currentQuests.ToList())
        {
            if (!quest.itemSubmissionObjectives.TryGetValue(itemID, out int required)) continue;

            quest.itemSubmissionProgress.TryGetValue(itemID, out int turnedIn);
            if (turnedIn < required)
            {
                RPCManager.RPC(instance, nameof(_SetProgress), [questID, itemID, turnedIn + 1]);
            }
            if (AllObjectivesMet(quest))
            {
                CompleteQuest(questID);
            }
        }
    }

    [RPC(requireAuthority = true)]
    private void _SetProgress(string questID, string itemID, int turnedIn)
    {
        if (currentQuests.TryGetValue(questID, out Quest quest))
        {
            quest.itemSubmissionProgress[itemID] = turnedIn;
            QuestStateUpdated?.Invoke();
        }
    }

    // ---- completion -------------------------------------------------------------

    /// <summary>
    /// Host only: completes <paramref name="questID"/> for everyone (if it's in progress), whatever its objectives
    /// say, and puts its unlocked blueprints on sale.
    /// </summary>
    public static void CompleteQuest(string questID)
    {
        if (!currentQuests.TryGetValue(questID, out Quest quest)) return;

        RPCManager.RPC(instance, nameof(_Complete), [questID]);
        foreach (string unlockItemID in quest.onCompleteUnlockedItems)
        {
            Shop.AddAvailableItem(unlockItemID);
        }
    }

    [RPC(requireAuthority = true)]
    private void _Complete(string questID)
    {
        if (!currentQuests.Remove(questID, out Quest quest)) return;

        foreach (var (itemID, required) in quest.itemSubmissionObjectives)
        {
            quest.itemSubmissionProgress[itemID] = required;
        }
        completedQuests.Add(questID);
        QuestCompleted?.Invoke(questID);
        foreach (string unlockQuestID in quest.onCompleteUnlockQuests)
        {
            if (allQuests.TryGetValue(unlockQuestID, out Quest unlocked) && !completedQuests.Contains(unlockQuestID))
            {
                currentQuests.TryAdd(unlockQuestID, unlocked);
            }
        }
        QuestStateUpdated?.Invoke();
    }

    // ---- saves -----------------------------------------------------------------

    /// <summary>
    /// Replaces quest state with a restored save's (see <see cref="GameSave"/>): the completed quests, and each quest in
    /// progress with its turn-ins. Runs on each restoring peer, all with the host's data.
    /// </summary>
    public static void LoadSave(List<string> completed, Dictionary<string, Dictionary<string, int>> progress)
    {
        completedQuests.Clear();
        completedQuests.AddRange(completed);
        currentQuests.Clear();
        foreach (var (questID, turnedIn) in progress)
        {
            if (!allQuests.TryGetValue(questID, out Quest quest)) continue;

            quest.itemSubmissionProgress.Clear();
            foreach (var (itemID, count) in turnedIn)
            {
                quest.itemSubmissionProgress[itemID] = count;
            }
            currentQuests[questID] = quest;
        }
        QuestStateUpdated?.Invoke();
    }

    // ---- late joiners -----------------------------------------------------------

    // Host: the quest lists, then each quest's progress.
    void SendStateTo(ulong peerID)
    {
        if (!Lobby.isHost) return;
        RPCManager.RPCTo(peerID, this, nameof(_SetQuestLists), [completedQuests.ToArray(), currentQuests.Keys.ToArray()]);
        foreach (var (questID, quest) in currentQuests)
        {
            foreach (var (itemID, turnedIn) in quest.itemSubmissionProgress)
            {
                RPCManager.RPCTo(peerID, this, nameof(_SetProgress), [questID, itemID, turnedIn]);
            }
        }
    }

    [RPC(requireAuthority = true)]
    private void _SetQuestLists(string[] completed, string[] current)
    {
        completedQuests.Clear();
        completedQuests.AddRange(completed);
        currentQuests.Clear();
        foreach (string questID in current)
        {
            if (allQuests.TryGetValue(questID, out Quest quest))
            {
                quest.itemSubmissionProgress.Clear(); // the host sends the real progress next
                currentQuests[questID] = quest;
            }
        }
        QuestStateUpdated?.Invoke();
    }
}
