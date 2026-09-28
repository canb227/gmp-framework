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

## Object Museum: Structure Hall

`python3 tools/structures/place_museum.py` (re-runnable) builds the Structure Hall onto `ObjectMuseum.tscn`: a
new walled floor east of the original museum (x 40..136, z -60..60) entered through a wide gap in its west
wall. One of every new structure stands in signed category bays (north: conveyors, magnetic conveyors,
launchers; south: chutes, powered chutes; east: sorting, fields, processing, utilities), each exhibit with a
name tag. The middle holds demo production lines: smelting & plates, sorted rods, launch & catch, cannon &
chute tower, catapult, field projectors and a magnetic tunnel. Lines that can run with today's behaviour
(conveyors, Grinder-based machines, the ramp) have slow item spawners; the rest are static displays. The
spawn tube in the utilities bay sits on an item void and drops into it.

`python3 tools/structures/view_hall.py <out.png> [top | camera:target]` renders the hall for review.

## Concept Lab

Proof-of-concept pieces for visual reference; their behaviour is at most a rough approximation.

- **Models:** `python3 tools/blender/run.py build concepts` (machines/concepts) and `build tools` (models/tools).
  Moving parts are keyframed in Blender and exported as one looping clip, `idle-loop`, which the scenes autoplay.
- **Scenes:** `game/scenes/structures/concepts/Concept*.tscn` come from `scenes_concepts.py`, run by `gen_scenes.py`.
  They have no blueprints yet.

| Scene | Cells | Animation | Rough behaviour |
|---|---|---|---|
| GravityInverter | 1×1×1 | orbiting emitters, field | trigger sensor only |
| TagGate | 1×1×1 | belt, doors | working belt, scan sensor |
| BouncePad | 1×1×1 | pad, springs, dial | pad restitution 1.25 |
| VortexFunnel | 2×2×1 | swirling bowl | bowl pushes items tangentially, so they spiral to the hole |
| TubeStraight / Bend / Junction / Receiver | 1×1×1 | pulse rings, flap, bellows | tube walls push along the tube at 6 m/s |
| HeatLamp / CryoVent | 1×1×1 | belt, lamp coil / fan | working belt, zone sensor |
| CounterweightElevator | 1×1×3 | cages trading places, pulley | static cage floors |
| ScrewElevator | 1×1×3 | rotating helix | walls lift at 1.5 m/s |
| PlatformElevator | 1×2×3 | six platforms on a chain loop | none |
| RailGun | 1×4×1 | sled, charge rings | loader sensor |
| TippingBucket | 1×1×2 | bucket tipping side to side | static tilted bucket, slides |
| AssemblyChamber | 3×3×3, centred | floating parts, emitters | chamber sensor |

**Heaters and coolers:** `run.py build thermal` (`machines/concepts/source/build_thermal.py`). Each one is built
around a different mechanism, so each poses its own automation puzzle. They stand in a second row of bays
behind the lab's spine wall.

| Scene | Cells | In → out | Mechanism / puzzle | Rough behaviour |
|---|---|---|---|---|
| TunnelFurnace | 1×2×1 | back belt → front belt | heat = time inside; belt speed sets the dose | slow 0.8 m/s belt, heat sensor |
| MagmaBath | 2×2×1 | dropped in from above → front weir (floaters) / right port (sinkers) | heats and sorts by density; sizing the drop-in and splitting the two outputs | floor drags sunk items to the port; bath sensor |
| ImpactForge | 1×1×2 | launched into the upper front window → bottom of the same face | heat = impact energy, so it needs launchers aimed at the anvil | dead anvil (restitution 0.05), exit slide, impact sensor |
| QuenchTank | 1×2×1 | dropped in from above → front belt | plain quench bath; lift belt drags items out to drip-dry | 1 m/s lift and output belts, quench sensor |
| SpiralRadiator | 1×1×3 | top hopper → bottom front | cooling = ride length down a finned helix; items must be lifted 6 m first | 36-segment helix slide with lip, run-out guide |
| CounterflowExchanger | 2×2×1 | lane A back → front, lane B front → back | no power: a hot and a cold stream swap heat through a copper wall, so both flows must be balanced | two opposed 1 m/s belts, lane sensors |

- **Tools:** tether gun (spinning reel), tag painter (carousel that steps 60°) and blueprint stamp (hologram).
  Each is a `.glb` only.
- **In the museum:** `place_museum.py` adds the Concept Lab south of the original museum floor
  (x -40..40, z -120..-60), open to the museum on the north side. The front row has three bays (tubes & routing,
  elevators, thermal/launch/assembly), and the tools turn on plinths by the entrance. Behind the spine wall
  (z -92) sits the thermal processing row: heaters and coolers. `view_hall.py <out.png> lab` renders it.
- **Resource ids:** existing `hall_` ext resource ids are kept on re-runs, because hand-placed museum nodes use them.
