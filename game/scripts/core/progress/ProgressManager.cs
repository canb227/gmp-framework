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

/// <summary>
/// Quests: every defined quest (<see cref="allQuests"/>), the ones in progress (<see cref="currentQuests"/>) with
/// their turn-in progress, and the ones done (<see cref="completedQuests"/>).
/// <para>
/// Multiplayer: quest state is shared and the lobby host owns it, like <see cref="Shop"/>. Turn-ins
/// (<see cref="TurnInForQuest"/>) and completions (<see cref="CompleteQuest"/>) from any peer are requests to the
/// host, which counts them against its own state and broadcasts each change to every peer, itself included. Only
/// the host puts unlocked blueprints on sale (the shop shares that itself). A peer that connects later gets the
/// whole state from the host.
/// </para>
/// </summary>
public partial class ProgressManager : Node
{
    public static Dictionary<string,Quest> allQuests = new();
    public static List<string> completedQuests = new();

    public static Dictionary<string, Quest> currentQuests = new();
    public static ProgressManager instance;

    public static event Action QuestStateUpdated;
    public static event Action<string> QuestCompleted;

    public override void _Ready()
    {
        instance = this;
        ItemVoid.AnyItemDespawned += OnItemDespawned;
        Lobby.PeerConnectedEvent += OnPeerConnected;
        loadQuestsFromFile();
        // load game progress from file
        // fix any discrepencies between quests in file and quests in game
        currentQuests.Add("quest_intro_00",allQuests["quest_intro_00"]);
        QuestStateUpdated?.Invoke();
    }

    public override void _ExitTree()
    {
        ItemVoid.AnyItemDespawned -= OnItemDespawned;
        Lobby.PeerConnectedEvent -= OnPeerConnected;
    }

    static void RequestFromHost(string method, object[] args) => RPCManager.RPCTo(Lobby.hostID, instance, method, args);

    static void Broadcast(string method, object[] args) => RPCManager.RPC(instance, method, args);

    private void OnItemDespawned(ItemVoid @void, string itemID)
    {
    //    bool usedForQuest = false;
    //    foreach (var kvp in currentQuests.ToList())
    //    {
    //        Quest quest = kvp.Value;
    //        if (quest.itemSubmissionObjectives.TryGetValue(itemID, out int required))
    //        {
    //            usedForQuest = true;
    //            quest.itemSubmissionProgress.TryGetValue(itemID, out int turnedIn);
    //            quest.itemSubmissionProgress[itemID] = Math.Min(turnedIn + 1, required);
    //            if (AllObjectivesMet(quest))
    //            {
    //                CompleteQuest(kvp.Key);
    //            }
    //            QuestStateUpdated?.Invoke();
    //        }
    //    }
    //    if (!usedForQuest || usedForQuest)
    //    {
    //        Shop.AddResource(itemID, 1);
    //    }
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


    /// <summary>Completes <paramref name="questID"/> for everyone (if it's in progress), whatever its objectives say.</summary>
    public static void CompleteQuest(string questID)
    {
        RequestFromHost(nameof(_RequestComplete), [questID]);
    }

    [RPC]
    private void _RequestComplete(string questID)
    {
        if (Lobby.isHost && currentQuests.ContainsKey(questID))
        {
            Broadcast(nameof(_Complete), [questID]);
        }
    }

    [RPC(requireAuthority = true)]
    private void _Complete(string questID)
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
        if (Lobby.isHost)
        {
            foreach (var unlockItemID in quest.onCompleteUnlockedItems)
            {
                Shop.AddAvailableItem(unlockItemID);
            }
        }

        QuestStateUpdated?.Invoke();
    }

    public static void loadQuestsFromFile()
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

    /// <summary>Counts one <paramref name="itemID"/> turned in toward every quest in progress that wants it, for everyone.</summary>
    internal static void TurnInForQuest(string itemID)
    {
        RequestFromHost(nameof(_RequestTurnIn), [itemID]);
    }

    [RPC]
    private void _RequestTurnIn(string itemID)
    {
        if (!Lobby.isHost)
        {
            return;
        }
        foreach (var (questID, quest) in currentQuests.ToList())
        {
            if (!quest.itemSubmissionObjectives.TryGetValue(itemID, out int required))
            {
                continue;
            }
            quest.itemSubmissionProgress.TryGetValue(itemID, out int turnedIn);
            if (turnedIn < required)
            {
                Broadcast(nameof(_SetProgress), [questID, itemID, turnedIn + 1]);
            }
            if (AllObjectivesMet(quest))
            {
                Broadcast(nameof(_Complete), [questID]);
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

    // ---- late joiners ---------------------------------------------------------

    // Host: send the whole quest state to a peer that just connected.
    void OnPeerConnected(ulong peerID)
    {
        if (!Lobby.isHost)
        {
            return;
        }
        List<string> progressQuests = new(), progressItems = new();
        List<int> progressCounts = new();
        foreach (var (questID, quest) in currentQuests)
        {
            foreach (var (itemID, count) in quest.itemSubmissionProgress)
            {
                progressQuests.Add(questID);
                progressItems.Add(itemID);
                progressCounts.Add(count);
            }
        }
        RPCManager.RPCTo(peerID, instance, nameof(_SyncState), [completedQuests.ToArray(), currentQuests.Keys.ToArray(),
            progressQuests.ToArray(), progressItems.ToArray(), progressCounts.ToArray()]);
    }

    // Replaces this peer's quest state with the host's. Progress comes as parallel arrays: (quest, item, count).
    [RPC(requireAuthority = true)]
    private void _SyncState(string[] completed, string[] current, string[] progressQuests, string[] progressItems, int[] progressCounts)
    {
        completedQuests.Clear();
        completedQuests.AddRange(completed);
        currentQuests.Clear();
        foreach (string questID in current)
        {
            if (allQuests.TryGetValue(questID, out Quest quest))
            {
                quest.itemSubmissionProgress.Clear();
                currentQuests[questID] = quest;
            }
        }
        for (int i = 0; i < progressQuests.Length && i < progressItems.Length && i < progressCounts.Length; i++)
        {
            if (currentQuests.TryGetValue(progressQuests[i], out Quest quest))
            {
                quest.itemSubmissionProgress[progressItems[i]] = progressCounts[i];
            }
        }
        QuestStateUpdated?.Invoke();
    }
}