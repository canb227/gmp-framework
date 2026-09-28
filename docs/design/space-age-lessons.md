# Lessons from Factorio: Space Age (and its modded planets)

Researched for the tag-themed chains. The planets are useful because each is built around one constraint
that changes how every factory on it is laid out. Below: the constraint, why it works, and how it translates
to a game where items are physical bodies.

## Official planets

**Fulgora: power in bursts, and output you don't control.** Lightning is plentiful but arrives as short bursts
that must be caught (rods, collectors) and stored (accumulators) or lost. Scrap recycling produces random
outputs, and every possible output needs a path (use, store or void) or the line jams.
- *Physical translation:* a **timed event** machine. The storm collector strikes every 6 s, and only items on
  the pad at that instant are converted. This gives a timing and batching puzzle: meter items to the pulse,
  don't flood the pad.
- *Also:* **random mixed outputs force sorting and overflow design.** Any machine with several possible outputs
  (scrap, a shattering item) needs every exit to go somewhere.

**Gleba: things spoil, so buffers are waste.** Biological products decay into spoilage on a timer. The guides
agree: no big buffers, loop belts instead of dead ends, and process right next to the source.
- *Physical translation:* **decay clocks** on items. Fulgurite leaks its charge in about a minute and tar cures
  in a couple of minutes. The winning layouts are short, fast and just-in-time, and a hopper full of product
  is a hopper full of waste.
- *Counter-play:* a **pause** mechanic (cooling slows curing: the cryo vent as cold storage) and a
  **stabilising** step (capacitor press, coating drum) that ends the clock.

**Vulcanus: a hostile medium you exploit.** Lava is both the hazard and the raw material (cast directly into
metal).
- *Physical translation:* a room feature that is dangerous and useful at once: the magma bath (heats and
  sorts by density), and the tar floor (friction 3 bogs things down but could hold items in place).

**Aquilo: machines freeze without heat.** Everything needs a heat network, so the environment taxes every
building.
- *Physical translation:* an **environmental tax** on every machine in a room: must be heated, insulated
  (aerogel) or powered (capacitor cells). A good late-game room modifier.

## Modded planets (Space Age mod scene)

- **Corrundum:** sulfur chemistry under dense clouds that build enormous electric potential (lightning storms)
  and high pressure. *Lesson:* pair a **volatile resource with an electrical hazard** in the same place. That is
  why sulfur detonates on CHARGED contact, and why the Storm Cage keeps fulgurite and sulfur apart.
- **Maraxsis:** a fully submerged planet with pressure domes and submarines; standard machines don't work
  underwater. *Lesson:* a room can **invalidate the standard toolkit** so the player needs a room-specific
  variant (insulated belts in the Storm Cage; magnetic belts in the Tumbler).
- **Cerys:** a moon where radiative towers heat at range (no air to conduct). *Lesson:* **effects at a
  distance** (radiation, charge fields, magnet pull) create spacing puzzles, not just adjacency ones. The
  charge-repel rule is this.
- **Moshine** (hot metal-rich desert, harvestable probes), **Secretas/Frozeta** (dim, frozen wreck-strewn moon),
  **Tenebris** (fully dark, acidic, no cargo drops). *Lesson:* constraints can also be **informational and
  logistical** (darkness, no deliveries). These are not used yet; they are candidates for a future room ("the
  dark room": items only visible when lit or glowing).

## The pattern to reuse

1. Pick **one constraint** that touches every item in the area (timing pulse, decay clock, hostile medium,
   environment tax, action at a distance).
2. Make it **physical**: a machine that pulses, a floor that grips or spins, an item that repels.
3. Give it **one stabiliser** that ends the constraint for a price (a press, a coating, a cage).
4. Add **one counter-tool** that manages it without ending it (insulated belt, cold storage, scraper).
5. Make the **failure product visible** (spent fulgurite, tar rock) and loopable.

## Sources

- Space Age planets: https://wiki.factorio.com/Space_Age
- Fulgora recycling and lightning: https://factorioguides.com/space-age/fulgora/fulgora-recycling-guide/ and
  https://factorio.com/blog/post/fff-399
- Gleba spoilage and no-buffer layouts: https://www.holy.gg/en/post/factorio-gleba-survival-spoilage-resources-blueprints-base-tips
- Maraxsis: https://mods.factorio.com/mod/maraxsis
- Corrundum: https://mods.factorio.com/mod/corrundum
- Cerys: https://mods.factorio.com/mod/Cerys-Moon-of-Fulgora
- Moshine: https://mods.factorio.com/mod/Moshine; Secretas: https://mods.factorio.com/mod/secretas;
  Tenebris: https://mods.factorio.com/mod/tenebris
