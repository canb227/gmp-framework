# Test Facility hub (Godot 4)

Dilapidated test chamber hub, inspired by the decayed/overgrown chambers of Portal 2 (original design:
no Valve logos, signage or characters). Same 2 m build grid as the Mine Cavern.

## Files
- test_facility.glb: meshes, vertex-colour materials (no textures needed), 46 lights, collision hints
- test_facility_import.gd: post-import script
- test_facility.blend: source; text blocks fac_helpers.py (layout constants) and export_glb.py (re-export)

## Godot setup
1. Copy the .glb and .gd into your project, select the .glb, Import dock:
   Meshes > Light Baking: Static Lightmaps; Import Script: test_facility_import.gd; Reimport.
2. Instance it, add a WorldEnvironment (near-black ambient, a little volumetric fog looks great in the light shaft),
   add a LightmapGI node and bake. The sun (Sun_Breach) only reaches the room through the ceiling breach.
3. "-col" nodes get trimesh collision; "-colonly" nodes are invisible collision proxies (walls, ceiling, core).
4. PlayerStart (empty) marks the spawn point, facing the core.

## Layout (metres; origin on a grid corner; north = +Y)
Chamber interior x -24..24, y -20..24, ceiling z=20 (10 cells).

| Area | Extent | Floor z |
|---|---|---|
| Near side (start side) | y -20..~2 | 0 |
| Chasm (splits the chamber, ~8 m / 4 cells wide, ~40 m deep) | y ~2..~10 | open |
| Far side | y ~10..24 | 2 |
| Tubes A platform (raised) | x -24..-12, y -20..-8 | 2 |
| Ramp A -> floor | x -12..-6, y -13..-9 | 2 -> 0 |
| Sunken flooded area | x 14..24, y -8..0 | -2 |
| R3 plinth | x -2..2, y -8..-4 | 4 |

- Core: centre (0, 1), radius ~5.5, runs from the chasm depths (z -40) up through the ceiling breach.
  Halo rings at z 9 (r 8.6) and 15.5 (r 7.4, tilted, broken) - both above a 4-cell (8 m) build height.
- Receptacles (deposit points): R1 (-9, -3, floor 0), R2 (9, -3, floor 0), R3 (0, -6.5) on top of the 4 m plinth.
  Each occupies one build cell. R1/R2 feed the core through floor conduits that have to be bridged or routed around.
- Spawn tubes A (glass, cyan): x -21, y -17 / -13 / -9 on the raised platform, landing pads under each.
- Drop chutes B (square, amber): x -7 / -3 / 1 / 5, y 23.2 on the far side; catch pads at y ~22.
- Doors (separate nodes so they can be animated/opened):
  - Door1 (lab door, 2x2 cells): south wall of platform A, x -20..-16, z 2..6. Left leaf jammed open, right leaf off its track.
  - Door2 (bay door, 3x3 cells): west wall, far side, y 14..20, z 2..8. Shutter jammed crooked.
  - Door3 (bay door, 3x3 cells): east wall, far side, y 14..20, z 2..8. Shutter torn open at the bottom.
  - Each has a short dark corridor stub ending in a collapse (the areas beyond are not modelled).
- Space pressure: SE ceiling-collapse rubble mound (~x 8..18, y -18..-9), fallen slabs near the start,
  conduits, sunken flooded pit, far-side wreckage (fallen gantry, rubble at the east chasm lip).
  Only broken catwalk stubs (x -16) reach over the chasm: the first bridge is the player's to build.

## Decisions made while you were away
- Start point moved to (-3, -15) (slightly off-axis) so the R3 plinth doesn't hide the core from spawn.
- Ceiling height 20 m (10 cells) to give the core and halos room; the core continues up a rock shaft
  through the breach, which is also where the only daylight comes from.
- Lighting: daylight shaft, cold cyan core glow (plus faint glow far down the chasm), 4 surviving fluorescent
  fixtures (the rest dead, one dangling), red emergency lamps at every door, cyan tube nozzles, amber chute lamps.
- Materials are vertex-colour based instead of baked textures (crisper panels, smaller file); rock uses
  computed vertex colours for the concrete slab band, strata and depth darkening.
- Walls/ceiling/core use simple grid-aligned proxy collision rather than per-panel trimesh.
- Foliage is excluded from lightmap UV2/baking and lit dynamically (see the import script).
- File is ~49 MB, mostly the thousands of individual panel/tile/frame boxes. If that's a problem, the
  panels could be merged per wall and the frames simplified.
