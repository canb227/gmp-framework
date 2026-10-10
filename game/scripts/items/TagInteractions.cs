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
        { (ItemTags.HOT, ItemTags.COLD), (self, other) => temperatureContacts++ },
        { (ItemTags.COLD, ItemTags.HOT), (self, other) => temperatureContacts++ },

        // Resource behaviours (design stubs: they log the intended effect). Each reaction runs on the item it changes.
        { (ItemTags.FUEL, ItemTags.HOT), (self, other) => ReportReaction(self, other, "ignites: starts burning and becomes HOT") },
        { (ItemTags.VOLATILE, ItemTags.HOT), (self, other) => ReportReaction(self, other, "detonates: blast impulse to neighbours, destroyed") },
        { (ItemTags.VOLATILE, ItemTags.CHARGED), (self, other) => ReportReaction(self, other, "sparked: detonates") },
        { (ItemTags.FERROUS, ItemTags.MAGNETIC), (self, other) => ReportReaction(self, other, "pulled toward the magnet and clings to it") },
        { (ItemTags.MAGNETIC, ItemTags.MAGNETIC), (self, other) => ReportReaction(self, other, "snaps pole to pole with the other magnet") },
        { (ItemTags.MAGNETIC, ItemTags.HOT), (self, other) => ReportReaction(self, other, "heated past its Curie point: field off for a while") },
        { (ItemTags.STICKY, ItemTags.METAL), (self, other) => ReportReaction(self, other, "sticks to it") },
        { (ItemTags.STICKY, ItemTags.ROCK), (self, other) => ReportReaction(self, other, "sticks to it") },
        { (ItemTags.STICKY, ItemTags.STICKY), (self, other) => ReportReaction(self, other, "merges into a bigger clump") },
        { (ItemTags.STICKY, ItemTags.COLD), (self, other) => ReportReaction(self, other, "chilled: hardens, stops sticking") },
        { (ItemTags.SOLUBLE, ItemTags.WET), (self, other) => ReportReaction(self, other, "dissolves") },
        { (ItemTags.COLD, ItemTags.SOLUBLE), (self, other) => ReportReaction(self, other, "melted by salt: becomes WET slush") },
        { (ItemTags.LIQUID, ItemTags.LIQUID), (self, other) => ReportReaction(self, other, "merges into one bigger bead") },
        { (ItemTags.LIQUID, ItemTags.CONDUCTIVE), (self, other) => ReportReaction(self, other, "amalgamates onto the copper") },
        { (ItemTags.CHARGED, ItemTags.CHARGED), (self, other) => ReportReaction(self, other, "repels the other charged item") },
        { (ItemTags.CHARGED, ItemTags.CONDUCTIVE), (self, other) => ReportReaction(self, other, "arcs into the metal and loses its charge") },
    };

    static readonly Dictionary<(ulong, ulong), ulong> lastReaction = new();

    /// <summary>Whether any rule involves one of <paramref name="tags"/> (such items need touch reports).</summary>
    public static bool HasRulesFor(IEnumerable<ItemTags> tags)
    {
        foreach (ItemTags tag in tags)
        {
            foreach ((ItemTags self, ItemTags other) key in rules.Keys)
            {
                if (key.self == tag || key.other == tag) return true;
            }
        }
        return false;
    }

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

    /// <summary>Reactions reported on this peer (the resource behaviours are logged until they're implemented).</summary>
    public static int reactions { get; private set; }

    // A stub: only counts the reaction. <paramref name="effect"/> documents what the rule is meant to do.
    static void ReportReaction(PhysicalFactoryItem self, PhysicalFactoryItem other, string effect)
    {
        reactions++;
    }
}
