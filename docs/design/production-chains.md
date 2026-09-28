# Designing a production chain around an item tag

A tag-themed chain is 4–6 steps long. Its middle product carries a hazard, and the player's job is to move that
product from the step that creates it to the step that stabilises it without losing it.

## Recipe

1. **A benign raw input** (difficulty 1) that is easy to move, so the first step is a pure logistics warm-up.
   Examples: storm sand, chalk.
2. **A transformation event** that creates the hazard, ideally with a timing or location constraint. Examples:
   the storm collector's 6 s bolt; tar as it seeps from the pit.
3. **The hazardous intermediate** (difficulty 4–5): 2–4 verbs that interact (see principles.md §1), plus a
   **decay clock** so buffering is punished.
4. **Counter-tools** that manage the hazard in transit: insulated belt, belt scraper, cold storage, spacing.
5. **A stabiliser** that ends the hazard by consuming a second, cheap input: capacitor press + copper plate;
   coating drum + chalk. The ratio of the second input is itself a small puzzle.
6. **A useful, calm product** (difficulty 1–2): capacitor cell (stable power), bitumen brick (high-friction
   building material). Ideally it feeds back as a counter to something else.
7. **Named failure products**: spent fulgurite, tar rock. Each needs a void, recycle or grind path.

## Shipped chain 1: electric (CHARGED), Room 5, the Storm Cage

```
storm sand ─► [storm collector: bolt every 6 s, pad must be occupied at the strike]
          ─► fulgurite (CHARGED, FRAGILE; leaks in ~60 s)
          ─► [insulated belt] ─► [capacitor press + copper plate] ─► capacitor cell (CHARGED but INSULATED: stable)
failures:  grounded, dropped or late ─► spent fulgurite ─► back under the storm
```
The hazards interact: repulsion forbids piles (so no hopper buffer), the leak forbids long routes, grounding
forbids ordinary metal belts and fragility forbids drops. Each rules out a different "easy" fix, which is why
it works as a difficulty-5 item.

## Shipped chain 2: sticky (STICKY), Room 6, the Tar Pit

```
tar blob (STICKY; cures to tar rock in ~2 min) + chalk nodule
          ─► [belt scraper at the belt end] ─► [coating drum: tumbles with chalk]
          ─► coated pellet (dry, free-rolling) ─► [press] ─► bitumen brick (friction 0.9)
counter:   cryo vent cold storage pauses curing and stickiness
failures:  jams (a glued plug in a chute), cured ─► tar rock ─► void or grind
```
Stickiness breaks things differently from charge: it defeats **gravity-based** logistics (belt ends, chutes,
hoppers), where charge defeats **grouping**. Put two such chains side by side and the player learns two different
toolkits.

## Ideas not yet built (same recipe)

- **Magnetic chain:** lodestone clumps iron. Heating past the Curie point switches its field off while hot, which
  gives a timed window to separate them, and a magnet core is the stabilised product.
- **Volatile chain:** sulfur + coal → blast charge. Charges detonate on HOT, CHARGED or a hard impact, and the
  product is launcher propellant, so the hazard *is* the payoff.
- **Liquid chain:** quicksilver beads merge on contact and split on impact; the product amalgam coats copper.
  The puzzle is metering bead size.
- **Darkness chain** (after Tenebris): items only visible or sortable while glowing, and the glow decays.

## Implementation checklist

- Items: add rows to `ITEMS` in `tools/items/gen_items.py` (physics, tags, difficulty, source, behaviour,
  challenge). Add models to `build_items.py` (`PIECES_SPEC`), then run `run.py build items`,
  `run.py icons items --only ...` and `gen_items.py`.
- Tags: append to `ItemTags.cs` only (the values are stored in scenes). Add pair rules to `TagInteractions.cs`
  (they log until implemented).
- Machines: models in a family under `game/assets/models/machines/...`, scenes in `tools/structures/scenes_*.py`
  (register them in `gen_scenes.py`), then run the collider check.
- Room: see puzzle-rooms.md.
