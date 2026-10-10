# Building a puzzle room

Puzzle rooms live in the museum's Puzzle Rooms wing (west of the original floor, x -150..-40, z -60..60), in
`archive/museum/museum/PuzzleRooms.tscn`. The museum is archived (not loaded by Godot), so this wing is reference only.

## What every room needs

1. **One room-wide feature**: the constraint, preferably real physics (a spinning floor, a tilting deck, a
   slope, a rotating room, a sticky floor, a periodic strike).
2. **1–2 new resources** that react to that feature differently from each other (see principles.md §3).
3. **1–2 new machines** themed on managing the feature: a sweep arm, a sieve, a catcher, a scraper, a drum.
4. **A live line**: a real `ItemSpawner` (with `itemWeights` for a mix, a slow `interval` and `outputPoint`
   placed where items should appear), real belts and structures, and **real `ItemVoid`s** at every exit so
   items never accumulate forever.
5. **A frozen tableau**: model-only items and machines (instanced .glb models, no scripts) that show the parts
   not implemented yet: products, jams, failures. Label it honestly as a tableau.
6. **Signs**: a title plus a story panel (what happens, what the PUZZLE is, what's NEW) at the entrance, and
   short callouts over each resource and machine.
7. **An enclosure** with an entry gap, and a way out for the player. The Tumbler needed doorways that line up
   whenever it stops.

## The six rooms and what each taught

| # | Room | Feature | Lesson |
|---|---|---|---|
| 1 | Tumbler | a whole room turns 90° every 10 s | Only a tableau works until magnetic hold is scripted. Plan player entry and exit around the motion first. |
| 2 | Carousel | a continuous turntable | Real friction sorting for free (r_max = μg/ω²). A static blade hovering over a moving floor is a great scraper. Sorting by flinging is statistical, so the exits come out partly mixed; that imperfection is the puzzle. |
| 3 | Scales | a dynamic deck on a hinge | Counterweight plus ballast gives an emergent see-saw loop with no scripting. Keep the deck density low (0.3) so single items matter. |
| 4 | Scree Slope | a 25° static slope | The cheapest room to make fully real. Slope angle against friction gives a clean sorting threshold, and the sieve sorts by size. |
| 5 | Storm Cage | a periodic strike | A timing constraint drives layout. The tableau carries the hazards that need scripts (repel, ground, leak). |
| 6 | Tar Pit | sticky items and a spinning drum | A kinematic tube with lifters is a real tumbler, and gravity feeding into it needs a raised deck. A high-friction floor patch is a free, real "sticky" zone. |

## Recipes

- **Moving floors and rooms:** a kinematic `Box3DBody` (`body_type = 1`), moved by setting its node's transform
  every physics tick from a script (see `Recycler.SpinRollers`); Box3D takes the motion from the node and carries
  contacts. Setting its angular velocity does nothing: Box3D drives kinematic bodies from their node transform, so
  a body whose node stays put stays put. Every collider must be a child `Box3DCollisionShape` of that body, and
  meshes are plain children. Structure scenes can't ride on it (they are separate static bodies), so put
  model-only instances plus child colliders on the moving body.
- **The root body's own shape:** every `Box3DBody` has one at its origin. For a moving room, set it to a tiny sphere
  (`shape_type = 1`, `sphere_radius = 0.05`) hidden inside a wall or the disc thickness, never in walkable space.
- **Discs from boxes:** 16 radial planks of width `2R·tan(π/16)` cover a disc of radius R. The drum uses 12 staves
  in a ring plus 4 lifter bars.
- **Tilting deck:** a dynamic body (`body_type = 2`, low density, `angular_damping` about 1.5) plus a
  `Box3DHingeJoint` with `body_a` only (anchored to the world), `limit_enabled` and ±10° limits. The hinge
  axis is assumed to be the joint's local Z (unverified).
- **Rotated static geometry:** use `rblock()` (a static box with any basis) for slopes, chutes and ring
  segments. `Hall.block()` is axis-aligned only.
- **Conveying static surfaces:** set `tangent_velocity` on a static collider (a ring trough moving items
  round to exits; a drag floor in a catcher or hopper).
- **Spawning from above:** mount an `ItemSpawner` on a static gantry and set `outputPoint` below its body so
  items drop onto moving or tilted floors.

## Layout notes

The wing currently holds Tumbler (-80, -30), Carousel (-80, 30), Scales (-125, 0), Scree (-125, -35), Storm Cage
(-81, -1) and Tar Pit (-125, 37). Free space is scarce. Check the enclosures in `PuzzleRooms.tscn` before adding
a room, or extend the wing.
