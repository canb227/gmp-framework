# Test Facility hub (Godot 4) - v2

Dilapidated test chamber hub, inspired by the decayed/overgrown chambers of Portal 2 (original design:
no Valve logos, signage or characters). Same 2 m build grid as the Mine Cavern.
v1 (48 x 44 m) is kept in v1_backup/.

## Files
- test_facility.glb: meshes, vertex-colour materials (no textures), 66 lights, collision hints (~52 MB)
- test_facility_import.gd: post-import script
- test_facility.blend: source; text blocks fac_helpers.py (all layout constants) and export_glb.py (re-export)

## Godot setup
1. Copy the .glb and .gd into your project, select the .glb, Import dock:
   Meshes > Light Baking: Static Lightmaps; Import Script: test_facility_import.gd; Reimport.
2. Instance it, add a WorldEnvironment (near-black ambient; light volumetric fog makes the daylight shaft and
   the camera's scan beam read), add a LightmapGI node and bake.
3. "-col" nodes get trimesh collision; "-colonly" nodes are invisible proxies (walls, ceiling, core, tree trunk).
4. PlayerStart (empty) marks the spawn point, facing the core.

## Layout (metres; origin on a grid corner; north = +Y)
Chamber interior x -34..34, y -28..34 (68 x 62 m = ~4,200 m2, double v1). Ceiling z=24 (12 cells).

| Area | Extent | Floor z |
|---|---|---|
| Near side (start/core side) | y -28..~4 | 0 |
| Chasm (~8 m / 4 cells wide, ~40 m deep) | y ~4..~12 | open |
| Far side | y ~12..34 | 2 |
| Tubes A platform (raised) | x -34..-20, y -28..-14 | 2 |
| Ramp A -> floor | x -20..-14, y -21..-17 | 2 -> 0 |
| Sunken flooded area | x 22..34, y -10..0 | -2 |
| R3 plinth | x -2..2, y -6..-2 | 4 |

- Core: centre (0, 3), radius ~5.5, from the chasm depths (z -40) up through the ceiling breach into a rock shaft.
  Halos at z 10 (r 8.6) and 17.5 (r 7.4, tilted/broken) - above a 4-cell build height.
- Receptacles: R1 (-11, -1), R2 (11, -1) at floor level; R3 (0, -4.5) on top of the plinth. Floor conduits R1/R2 -> core.
- Great tree: trunk at (-12, -9), burst through the floor between the core and Tubes A. Trunk ~4 m wide at the base,
  11 surface roots over heaved tiles out to ~7-9 m, canopy to ~17-20 m leaning toward the daylight shaft.
- Spawn tubes A (glass, cyan): x -31, y -25 / -21 / -17. Drop chutes B (amber): x -7 / -3 / 1 / 5, y 33.2.
- Far side obstacles: TilePile_West (-13, 21, r~2.6 m, ~2.4 m tall), TilePile_East (14, 27, r~2.3 m, ~2 m tall),
  fallen gantry near Door 2, rubble + leaning slab at the east chasm lip.
- Doors: Door1 lab door (2x2 cells) south wall of platform A x -28..-24; Door2 / Door3 bay doors (3x3 cells) on the
  west / east walls of the far side, y 22..28, z 2..8. Each dead-ends in a short collapsed corridor.

## Visual flair (all unreachable)
- Derelict rooms seen through missing panels ("peek zones", wall panels mostly gone, no substructure behind):
  - South wall x 4..16, z 5..15: abandoned office level (desks, toppling filing cabinets, papers, dangling lamp).
  - North wall x 14..28, z 7..19: deep machinery hall (tanks, pipes, catwalk, distant red warning lamps).
  - East wall y -22..-12, z 3..13: collapsed stairwell with a missing flight and a green-lit crack above.
  - West wall y 24..32, z 9..17 (above Door 2): archive of dead server racks with a few blinking lights.
- Frosted observation window: west wall, y -12..-2, z 10..14, slanted glass in 5 panes (one cracked, one blown out,
  shards on the floor below), lit control room behind it with consoles and toppled chairs.
- Automated camera: SE corner of the near side, ~18 m up. Camera_Mount > Camera_Yaw > Camera_Head with pivots set
  so it can be animated to track the player; red eye (Camera_Eye) and a faint red scan beam (Camera_ScanBeam).

## Decisions made while you were away (v2)
- Grew the room by ~1.41x in each direction (68 x 62 m) and raised the ceiling to 24 m, rebuilding everything from the
  layout constants so all floors stay on the 2 m grid. The chasm, core, halos, doors and tube areas were moved to keep
  their v1 relationships.
- Tree placed at (-12, -9) so it sits between Tubes A and the core/R1 - the most natural conveyor path - and forces a
  detour. It gets box proxy collision (trunk + mound); roots/canopy are visual only.
- Player start moved to (-4, -17).
- Added two abandoned tripod work lights beside the tile piles: without them the far side was too dark to read
  those obstacles. They double as a bit of story (someone was sorting salvage).
- Camera scaled 1.6x after a first look, so it reads as large from across the room.
- Size optimisation: removed bevels on wall panels/floor tiles and deleted faces that can never be seen (backs of
  perimeter panels, tile bottoms, ceiling tops). 85 MB -> 52 MB with no visible change from inside the room.
- Everything else (materials as vertex colours, proxy collision, foliage excluded from lightmaps) as in v1.
