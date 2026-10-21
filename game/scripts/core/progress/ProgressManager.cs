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
/// shares that itself). A peer that joins a running game gets the whole state with the world (<see cref="SaveGame"/>).
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


    /// <summary>The quest every game starts with.</summary>
    public const string FirstQuest = "quest_intro_00";

    public override void _Ready()
    {
        instance = this;
        loadQuestsFromFile();
        ResetSession();
    }

    /// <summary>Back to the start for the next game: no quests done, only <see cref="FirstQuest"/> in progress, nothing turned in.</summary>
    public static void ResetSession()
    {
        completedQuests.Clear();
        currentQuests.Clear();
        // Progress lives on the quest resources themselves, which stay loaded between games.
        foreach (Quest quest in allQuests.Values)
        {
            quest.itemSubmissionProgress.Clear();
            quest.currencyProgress.Clear();
        }
        currentQuests.Add(FirstQuest, allQuests[FirstQuest]);
        QuestStateUpdated?.Invoke();
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
        foreach (var (currency, required) in quest.currencyObjectives)
        {
            quest.currencyProgress.TryGetValue(currency, out int earned);
            if (earned < required)
            {
                return false;
            }
        }
        return true;
    }

    // ---- turn-ins ---------------------------------------------------------------

    /// <summary>
    /// Any peer: an item was turned in (e.g. eaten by a <see cref="Consumer"/>). Sends it to the host, which deposits
    /// <paramref name="value"/> in the <see cref="Shop"/> and counts the item and its value toward quests.
    /// </summary>
    public static void RequestDeposit(string itemID, Dictionary<Currency, int> value)
    {
        RPCManager.RPCTo(Lobby.hostID, instance, nameof(_Deposit), [itemID, value]);
    }

    // Host (only ever sent there).
    [RPC]
    private void _Deposit(string itemID, Dictionary<Currency, int> value)
    {
        Shop.Deposit(itemID, value);
        TurnInForQuest(itemID, value);
    }

    /// <summary>
    /// Host only: counts one <paramref name="itemID"/> and what it's worth (<paramref name="value"/>, may be null)
    /// toward every quest in progress that wants either, for everyone. Quests this completes unlock theirs after
    /// the count, so the same deposit never counts toward them.
    /// </summary>
    internal static void TurnInForQuest(string itemID, Dictionary<Currency, int> value)
    {
        foreach (var (questID, quest) in currentQuests.ToList())
        {
            bool counted = false;

            if (quest.itemSubmissionObjectives.TryGetValue(itemID, out int required))
            {
                counted = true;
                quest.itemSubmissionProgress.TryGetValue(itemID, out int turnedIn);
                if (turnedIn < required)
                {
                    RPCManager.RPC(instance, nameof(_SetProgress), [questID, itemID, turnedIn + 1]);
                }
            }

            if (value != null)
            {
                foreach (var (currency, amount) in value)
                {
                    if (amount <= 0 || !quest.currencyObjectives.TryGetValue(currency, out int needed)) continue;

                    counted = true;
                    quest.currencyProgress.TryGetValue(currency, out int earned);
                    if (earned < needed)
                    {
                        RPCManager.RPC(instance, nameof(_SetCurrencyProgress), [questID, (int)currency, Math.Min(earned + amount, needed)]);
                    }
                }
            }

            if (counted && AllObjectivesMet(quest))
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

    [RPC(requireAuthority = true)]
    private void _SetCurrencyProgress(string questID, int currency, int earned)
    {
        if (currentQuests.TryGetValue(questID, out Quest quest))
        {
            quest.currencyProgress[(Currency)currency] = earned;
            QuestStateUpdated?.Invoke();
        }
    }

    // ---- completion -------------------------------------------------------------

    /// <summary>
    /// Host only: completes <paramref name="questID"/> for everyone (if it's in progress), whatever its objectives
    /// say, puts its unlocked blueprints on sale and pays its currency reward into the banks. Every player gets
    /// its free items (in <see cref="_Complete"/>).
    /// </summary>
    public static void CompleteQuest(string questID)
    {
        if (!currentQuests.TryGetValue(questID, out Quest quest)) return;

        RPCManager.RPC(instance, nameof(_Complete), [questID]);
        foreach (string unlockItemID in quest.onCompleteUnlockedItems)
        {
            Shop.AddAvailableItem(unlockItemID);
        }
        foreach (var (currency, amount) in quest.onCompleteCurrency)
        {
            if (amount > 0)
            {
                Shop.AddCurrency(currency, amount);
            }
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
        foreach (var (currency, required) in quest.currencyObjectives)
        {
            quest.currencyProgress[currency] = required;
        }
        completedQuests.Add(questID);
        GiveFreeItems(quest);
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

    // Every peer, for its own player: the quest's free items go into the local player's inventory, and what
    // doesn't fit is dropped above them.
    private static void GiveFreeItems(Quest quest)
    {
        if (quest.onCompleteFreeItems.Count == 0) return;

        FactoryPlayer player = GameWorld.syncedObjs.Values.OfType<FactoryPlayer>().FirstOrDefault(p => p.isLocal);
        if (player == null) return;

        foreach (var (itemID, count) in quest.onCompleteFreeItems)
        {
            int leftover = player.inventory.AddItem(itemID, count);
            for (int i = 0; i < leftover; i++)
            {
                ItemInfo.SpawnInWorld(itemID, player.GlobalPosition + Vector3.Up * (2 + i * 0.5f));
            }
        }
        player.UpdateEquippedItem();
    }

    // ---- saves -----------------------------------------------------------------

    /// <summary>
    /// Replaces quest state with a restored save's (see <see cref="SaveGame"/>): the completed quests, and each quest in
    /// progress with its turn-ins and currency earned (by currency name; unknown names are dropped). Runs on each
    /// restoring peer, all with the host's data.
    /// </summary>
    public static void LoadSave(List<string> completed, Dictionary<string, Dictionary<string, int>> progress, Dictionary<string, Dictionary<string, int>> currencyProgress)
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
            quest.currencyProgress.Clear();
            if (currencyProgress != null && currencyProgress.TryGetValue(questID, out Dictionary<string, int> earned))
            {
                foreach (var (name, amount) in earned)
                {
                    if (Enum.TryParse(name, out Currency currency))
                    {
                        quest.currencyProgress[currency] = amount;
                    }
                }
            }
            currentQuests[questID] = quest;
        }
        QuestStateUpdated?.Invoke();
    }
}
