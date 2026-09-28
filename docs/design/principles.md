# Core principles for physics-automation challenges

The game is a physics-driven automation game: items are real rigid bodies (Box3D) on 2 m grid structures. A
good challenge comes from an item's physical nature fighting the default way of moving things. The player
solves it with layout, timing and machine choice, not with a recipe screen.

## 1. Every item needs one physical "verb" that breaks the default belt

The default line is spawner → flat belt → machine → belt. Each new item should break that line in one clear way:

| Verb | Example | What breaks |
|---|---|---|
| rolls | scree pebbles, rods, ballast shot, quicksilver | stops, slopes, turns (items roll off or across) |
| slides / won't be carried | frost crystal (friction 0.02), slickstone puck, glass pane | belts slip under it; it overshoots stops |
| grips / won't let go | tar blob, latex, burr seed | won't leave belt ends; clumps; rides spinning floors |
| drifts | floatstone, aerogel (low density, low gravity scale, damping) | blown off by fans and launches; needs roofs |
| too heavy | ballast shot, quicksilver (density 13–20) | tips balances; overpowers slopes; weak launches fall short |
| fragile | quartz, fulgurite, glass pane | drops more than one cell and launchers are forbidden |
| repels / attracts | voltaic and fulgurite (CHARGED), lodestone (MAGNETIC) | can't pile or buffer; clumps with iron |
| decays on a clock | fulgurite (charge leaks), tar (cures) | buffers become waste; distance becomes cost |
| reacts on contact | sulfur (VOLATILE), coal (FUEL), salt (SOLUBLE) | routing must keep certain pairs apart |

One strong verb per item beats several weak ones. Hard items stack two or three verbs (fulgurite: repels,
grounds out, cracks and decays), and those are the late-game puzzles.

## 2. Spread difficulty deliberately (1–5)

Rate every item (see `gen_items.py`, field `difficulty`) and keep a spread. Around half should be 1–2 (just
move it). Some should be 3 (one design constraint, e.g. no drops). A few should be 4–5 (several constraints
that interact). Colour the display by difficulty so players read the curve at a glance.

## 3. Sorting by physics is the most satisfying mechanic

The best rooms sort a mixed stream by a physical property instead of a filter machine:

| Property | Sorter |
|---|---|
| friction | a spinning turntable (Carousel): low-friction items fly off, grippy ones ride |
| size | a slot sieve with bars 0.54 m apart (Scree): 0.5 m pebbles drop through, 0.8 m slabs slide over |
| slope angle vs friction | on a terrace, shale stops below about 19°; pebbles never stop |
| density | a magma bath with floaters over the weir and sinkers dredged out; the Scales tip with weight |
| magnetism | a magnetic belt lifts iron and lets copper pass |

When you design a pair of resources, make them differ on exactly one axis and give the room a device that
reads that axis.

## 4. Give each hazard a counter-machine, and each counter a cost

Charge has the insulated belt, grounding with copper, and the capacitor press. Stickiness has the belt
scraper, cold storage and the coating drum. Fragility has rubber padding, elevators and slow belts. Heat has
the cryo vent and aerogel insulation. A counter that has no downside ends the puzzle, so each one costs space,
speed, another input (chalk, copper) or a timing window.

## 5. Make the failure visible and useful

Failures should leave a physical trace the player can count:
- shattered quartz becomes quartz shards (still smeltable, at half yield);
- flat fulgurite becomes spent fulgurite (recyclable under the storm again);
- cured tar becomes tar rock (waste to void or grind).

A byproduct count is a score for how rough the handling was, and a loop opportunity. The tableaux show
failures on purpose: a short circuit, a pile-up, a glued plug, a reject bin.

## 6. Prefer real physics over scripted rules

Box3D already gives friction, restitution, rolling resistance, damping, gravity scale, tangent velocity (belts),
kinematic motion and joints. A rule built from those works today, emerges in combinations and needs no code:
the turntable sorter, the sieve, the balance deck and the tar floor all do. Save tag scripts for effects
physics can't do: charge, decay, ignition, dissolving. Even then, the physical half of the design should
already be interesting without the script.

## 7. Write the challenge down next to the thing

Every item and room carries two sentences: what it does physically, and the problem it poses. Writing that
second sentence is the best test of a design. If you can't say what goes wrong, the item is filler.
