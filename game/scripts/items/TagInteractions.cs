using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// Rules for what happens when items with particular <see cref="ItemTags"/> touch. A rule is keyed by
/// (this item's tag, the other item's tag) and runs from this item's point of view, on the host, which simulates
/// every loose item (see <c>ItemWorld.OnPhysicsStepped</c>). Both items of a contact are checked, so a HOT/COLD touch
/// runs the (HOT, COLD) rule for the hot item and the (COLD, HOT) rule for the cold one. Anything a rule changes
/// must therefore be replicated (an RPC or state update), since the other peers don't run it. Box3D reports every
/// bounce as a new touch, so each pair is debounced.
/// <para>
/// Only items with a <see cref="watchers"/> tag report their touches (<see cref="ItemWorld.TagsChanged"/>): common
/// materials (METAL, FERROUS...) would report every bounce in a pile. Both items' sides of the rules run for every
/// reported touch, so every rule needs a watcher tag on at least one side; a rule without one is reported at startup.
/// </para>
/// </summary>
public static class TagInteractions
{
    /// <summary>Minimum time between reactions for the same pair of items.</summary>
    const ulong CooldownMs = 1000;

    static readonly Dictionary<(ItemTags self, ItemTags other), Action<ItemRecord, ItemRecord>> rules = new()
    {
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

    /// <summary>
    /// The tags whose items report their touches: the rarer, active side of each rule (what heats, charges, sticks,
    /// pulls, dissolves or flows), so that ordinary materials don't. Every rule has one on at least one side.
    /// </summary>
    static readonly HashSet<ItemTags> watchers = new()
    {
        ItemTags.HOT,
        ItemTags.CHARGED,
        ItemTags.MAGNETIC,
        ItemTags.STICKY,
        ItemTags.SOLUBLE,
        ItemTags.LIQUID,
    };

    static readonly Dictionary<(ulong, ulong), ulong> lastReaction = new();

    static TagInteractions()
    {
        foreach ((ItemTags self, ItemTags other) key in rules.Keys)
        {
            if (!watchers.Contains(key.self) && !watchers.Contains(key.other))
            {
                GD.PushError($"TagInteractions: rule ({key.self}, {key.other}) has no watcher tag, so it never runs. Add one of them to watchers.");
            }
        }
    }

    /// <summary>Whether an item with <paramref name="tags"/> reports its touches (has a <see cref="watchers"/> tag).</summary>
    public static bool IsWatcher(IEnumerable<ItemTags> tags)
    {
        foreach (ItemTags tag in tags)
        {
            if (watchers.Contains(tag)) return true;
        }
        return false;
    }

    /// <summary>Called on the host when <paramref name="self"/> touches <paramref name="other"/>.</summary>
    public static void OnTouch(ItemRecord self, ItemRecord other)
    {
        if (other.tags.Count == 0) return;

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
    static void ReportReaction(ItemRecord self, ItemRecord other, string effect)
    {
        reactions++;
    }
}
