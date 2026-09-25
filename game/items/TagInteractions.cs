using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// Rules for what happens when items with particular <see cref="ItemTags"/> touch. A rule is keyed by
/// (this item's tag, the other item's tag) and runs from this item's point of view, on this item's authority
/// (see <c>PhysicalFactoryItem.OnBodyEntered</c>). Both items of a contact are checked, so a HOT/COLD touch
/// runs the (HOT, COLD) rule for the hot item and the (COLD, HOT) rule for the cold one, each on its own
/// authority. Anything a rule changes must therefore be replicated (an RPC or state update), since the
/// other peers don't run it. Box3D reports every bounce as a new touch, so each pair is debounced.
/// </summary>
public static class TagInteractions
{
    /// <summary>Minimum time between reactions for the same pair of items.</summary>
    const ulong CooldownMs = 1000;

    static readonly Dictionary<(ItemTags self, ItemTags other), Action<PhysicalFactoryItem, PhysicalFactoryItem>> rules = new()
    {
        { (ItemTags.HOT, ItemTags.COLD), (self, other) => ReportTemperatureContact(self, other, "hot", "cold") },
        { (ItemTags.COLD, ItemTags.HOT), (self, other) => ReportTemperatureContact(self, other, "cold", "hot") },
    };

    static readonly Dictionary<(ulong, ulong), ulong> lastReaction = new();

    /// <summary>HOT/COLD contacts reported on this peer (used by the headless multiplayer test).</summary>
    public static int temperatureContacts { get; private set; }

    /// <summary>Called when <paramref name="self"/> touches <paramref name="other"/>, on <paramref name="self"/>'s authority.</summary>
    public static void OnTouch(PhysicalFactoryItem self, PhysicalFactoryItem other)
    {
        if (self.tags == null || other.tags == null || other.tags.Count == 0) return;

        ulong now = Time.GetTicksMsec();
        if (lastReaction.TryGetValue((self.id, other.id), out ulong last) && now - last < CooldownMs) return;

        bool reacted = false;
        foreach (ItemTags mine in self.tags)
        {
            foreach (ItemTags theirs in other.tags)
            {
                if (rules.TryGetValue((mine, theirs), out var rule))
                {
                    rule(self, other);
                    reacted = true;
                }
            }
        }
        if (reacted)
        {
            lastReaction[(self.id, other.id)] = now;
        }
    }

    static void ReportTemperatureContact(PhysicalFactoryItem self, PhysicalFactoryItem other, string mine, string theirs)
    {
        temperatureContacts++;
        Logging.Log($"TAG TEST: {self.itemID} ({mine}) touched {other.itemID} ({theirs})", "TagInteractions");
    }
}
