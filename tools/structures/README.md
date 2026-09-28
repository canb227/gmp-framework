# Structure pipeline

Models, scenes, blueprints and icons for the factory structures are all generated from scripts, so a change
to a layout is re-run rather than hand-edited.

| Step | Command | Writes |
|---|---|---|
| Models | `python3 tools/blender/run.py build <family> [--only Coll]` | `game/assets/models/<family>/*.glb` (and `--save-blend` the family's `.blend`) |
| Model preview | `python3 tools/blender/run.py preview <family> <dir> [--hide Node]` | one PNG per model |
| Scenes | `python3 tools/structures/gen_scenes.py` | `game/scenes/structures/**.tscn`, the models' `.glb.import` files; then checks no solid collider leaves its cells (`--check` only checks) |
| Collider review | `python3 tools/structures/view_colliders.py <out.png> <scene.tscn> [x,y,z]` | the model with its colliders drawn over it (red solid, blue sensor) |
| Icons | `python3 tools/blender/run.py icons <family>` | `game/assets/icons/blueprints/*.png` (the family's `ICONS`) |
| Blueprints | `python3 tools/structures/gen_blueprints.py` | `game/definitions/blueprints/<set>/*.tres`, icon `.import` files, `game/scripts/core/BlueprintSets.cs` |

The Blender steps need Blender as a Python module: `pip install bpy==4.5.14` (Python 3.11). The build
scripts also still run from Blender's text editor.

Families (`run.py list`): `conveyors`, `conveyor_extras`, `conveyors_advanced`, `conveyors_magnetic`, `chutes`,
`launchers`, `sorting`, `fields`, `processing`, `props`. New families load `game/assets/models/shared/salvage_lib.py`
(the conveyor and prop helpers) and import through `shared/salvage_import.gd`.

Frames: models are built in Blender with Z up and +Y as the front; the origin is the anchor cell's centre,
floor z = -1. In Godot that is Y up with the front at -Z. Multi-cell structures grow toward +X / front / up from
the anchor (the 3x3 hopper is anchored on its centre cell).

Blueprints: the new ones don't all fit the starting inventory. In game, open the console and run
`blueprints <set>` (conveyors, advanced, magnetic, chutes, chutes_advanced, launchers, sorting, fields,
processing) to get a stack of each blueprint in a set.

## Catalogue

Cells are W x L x H (x, along the flow, up). Nodes are inside each scene's `Model`.

| Structure | Scene | Cells | Notes |
|---|---|---|---|
| Splitter | conveyors/ConveyorSplitter | 1x1x1 | wedge sends items out of both sides of the front half |
| Switchable splitter | conveyors/ConveyorSplitterSwitch | 1x1x1 | `Arm` / `ArmBody` turn +-33 deg about Y (right / left); `LeverBody` (Lever.cs) tilts `Lever` |
| Loader | conveyors/ConveyorLoader | 1x1x1 | end lip 0.35 m above belt height |
| Advanced conveyor, turn L/R, slope up/down, loader | conveyors_advanced/* | as basic | 0.5 m walls, belt 3 m/s |
| Magnetic conveyor, turn L/R | conveyors_magnetic/ConveyorMagnetic[Wall/Ceiling][Turn*] | 1x1x1 | wall form on the cell's -X side, ceiling form on its top; `Glow` shows the magnets |
| Chute set (basic / advanced) | chutes/Chute[Adv]* | see below | floor at belt height, 1.5 m bore; advanced `Power` node + `Floor` tangent for the push |
| Horizontal straight / turn L/R | ChuteHStraight, ChuteHTurn* | 1x1x1 | open top, walls to 1.2 m |
| Vertical straight / elbow | ChuteVStraight, ChuteVTurn | 1x1x1 | elbow: in through the top, out through the front |
| Hopper up / dropper down | ChuteHopperUp, ChuteDropperDown | 1x1x1 | dropper: `DoorLBody`/`DoorL` -80 deg, `DoorRBody`/`DoorR` +80 deg about Z to open; `LeverBody`; `Hold` sensor |
| Hopper 2x2 / 3x3 | ChuteHopper2x2, ChuteHopper3x3 | 2x2x2 / 3x3x2 | outlet under the anchor (2x2) / centre (3x3) cell |
| Launch ramp | launchers/LaunchRamp | 1x3x2 | belt 8 m/s; `Flywheel`, `Beacon` spin |
| Cannon | launchers/Cannon | 1x2x2 | intake `Trigger`; `BarrelBody` / `Barrel` elevated 40 deg; `Barrel/Muzzle` marker |
| Catapult | launchers/Catapult | 1x3x2 | `ArmBody` / `Arm` rest +20 deg, thrown -100 deg about X; `ArmBody/Bucket` sensor |
| Basic filter | sorting/FilterBasic | 1x1x2 | `BasketTrigger` (samples), `ScanTrigger`; `PusherBody` / `Pusher` slide +1.45 m along X |
| Filter arm | sorting/FilterArm | 1x1x2 | `Turret > UpperArm > Forearm > Head > ClawL/ClawR`, idling via Oscillator |
| Antigravity / zero point projector | fields/*Projector | 1x1x1 | `FieldTrigger` sensor 4 / 8 cells out of the front; `Field` scaled to match |
| Plate press / rod extruder / polisher | processing/* | 1x2x1 / 1x2x1 / 2x2x1 | Grinder.cs: `Trigger` consumes, output at the front end of the belt |
| Smelter | Smelter | 2x2x2 | Grinder.cs: hopper `Trigger`, output in front of the anchor column |
| Spawn tube (dev only) | SpawnTube | 1x1x2 | ItemSpawner.cs; items appear at the `SpawnPoint` height and slide out the front |

Behaviour still to write: the switch, dropper doors, filter, arm, launchers, field projectors and the
magnetic hold have their trigger sensors, sub-bodies and nodes in place but no scripts yet; the processing
machines and smelter work through Grinder.cs with empty recipe tables until ingot / plate / rod items exist.

## Object Museum gallery

`python3 tools/structures/place_museum.py` (re-runnable) places one of every new structure in
`ObjectMuseum.tscn` under `StructureGallery`, each with a floating name tag, grid-aligned and facing the middle
of the museum: the conveyor family along the north end, chutes along the south end, machines down the east
side. The spawn tube feeds an item void so its test cubes don't pile up; the gallery's field projectors are
shortened to 2-cell fields so they stay clear of the display alcoves.
