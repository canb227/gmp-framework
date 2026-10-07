"""
Machine scenes: launchers, sorting, field projectors, inline processing machines, and the smelter / spawn tube
props. Colliders follow build_launchers.py, build_sorting.py, build_fields.py, build_processing.py and
build_props.py.

Machines that transform items reuse Grinder.cs (items inside the trigger are consumed and, after processTime,
come out at outputPoint, mapped through recipes); the recipe tables start empty until ingot / plate / rod
items exist, so items pass through unchanged. Moving parts that other machine scripts will drive have their own
static sub-bodies (ArmBody, PusherBody, RamBody, BarrelBody) next to the matching model node.
"""
import math
from scenegen import *
from scenes_conveyors import lips, BASIC_SPEED, ADV_SPEED, BASIC_WALL, ADV_WALL, belt_speed

IMPORT = "res://game/assets/models/shared/salvage_import.gd"
PROPS_IMPORT = "res://game/assets/models/props/props_import.gd"
M = "res://game/assets/models/machines/"

def cells_1xn(n, h=1):
    return [(0, y, -z) for y in range(h) for z in range(n)]

def grinder_root(sc, blueprint, cells, output_b, process_time):
    """A press on a 2 m-wide belt through its footprint (Grinder.cs): its belt's ends are its ports, declared here so
    they carry the Trigger it reads items from and the OutputSpawn (output_b, Blender frame) it puts them at."""
    sc.root("grinder", blueprint, cells=cells, extra=[
        "recipes = Dictionary[String, String]({})",
        f"processTime = {f(process_time)}"],
        ports=(("in", (0, 0, 1), "Back", (2, 1), {"volume": "Trigger"}),
               ("out", (0, 0, -2), "Front", (2, 1), {"spawn": "OutputSpawn"})))
    sc.marker("OutputSpawn", b2g(output_b))

def post_b(sc, name, p0, p1, r=0.03):
    """Thin box along a scaffold pipe (Blender end points)."""
    d = sub(p1, p0)
    a = norm(d)
    ref = (0, 0, 1) if abs(a[2]) < 0.9 else (1, 0, 0)
    u = norm(cross(a, ref)); v = norm(cross(a, u))
    sc.obox_b(name, mul(add(p0, p1), 0.5), mul(u, 2 * r), mul(v, 2 * r), d)

# ---------------------------------------------------------------------------- launchers
RAMP = dict(total_y=5.8, rise=3.0, R=1.5, th=math.radians(35.0))
RAMP_SPEED = 8.0

def launch_ramp():
    sc = Scene("LaunchRamp", M + "launchers/launch_ramp.glb", IMPORT)
    sc.root("structure", "blueprint_launch_ramp", cells=cells_1xn(3, 2), tags=(TAG_RUBBER,))
    sc.model()
    belt_speed(sc, RAMP_SPEED)
    sc.spin("Flywheel", (1, 0, 0), 14.0)
    sc.spin("Beacon", (0, 1, 0), 4.0)
    prof, L, flat, ls = bend_up_profile(**RAMP)
    bend = flat + RAMP["R"] * RAMP["th"]
    st = stations([(0, flat, 1), (flat, bend, 6), (bend, L, 2)])
    belt_along(sc, prof, st, RAMP_SPEED, friction=SLOPE_FRICTION, y_bounds=(-1, 5), end_port=False)   # throws items off its lip
    for side in (-1, 1):
        walls_along(sc, prof, stations([(0, flat, 1), (flat, bend, 3), (bend, L, 2)]), BASIC_WALL, side, y_bounds=(-1, 5))
    for k, fr in enumerate((0.2, 0.55, 0.9)):
        (y, z), _ = prof(bend + ls * fr)
        for side in (-1, 1):
            sc.box_b(f"Leg{'LR'[side > 0]}{k}", (side * 0.965, y, (-1 + z - 0.15) / 2), (0.05, 0.05, z - 0.15 + 1 - 0.01))
    return sc.write("game/scenes/structures/launchers/LaunchRamp.tscn")

CANNON_TRUNNION = (0.0, 0.35, 0.1)
CANNON_ELEV = math.radians(40.0)

def cannon():
    sc = Scene("Cannon", M + "launchers/cannon.glb", IMPORT)
    sc.root("structure", "blueprint_cannon", cells=cells_1xn(2, 2))
    sc.model()
    belt_speed(sc, ADV_SPEED)
    prof = straight_profile()
    belt_along(sc, prof, [0, 1.1], ADV_SPEED, end_port=False)                     # feeds the barrel
    for side in (-1, 1):
        walls_along(sc, prof, [0, 1.1], ADV_WALL, side)
    sc.box_b("Breech", (0, 1.05, -0.45), (1.9, 1.3, 1.1))
    for side in (-1, 1):
        sc.box_b(f"Tower{'LR'[side > 0]}", (side * 0.93, CANNON_TRUNNION[1], 0.1), (0.1, 0.5, 0.4))
    sc.box_b("Capacitors", (0.25, 2.55, -0.66), (0.9, 0.5, 0.68))
    sc.body("BarrelBody", b2g(CANNON_TRUNNION), rot_x(CANNON_ELEV))
    sc.box("Barrel", (1.72, 1.72, 2.95), (0, 0, -1.125), parent="BarrelBody")
    sc.sensor_b("Trigger", (0, -0.35, BELT_TOP + 0.5), (1.6, 1.1, 0.9))
    return sc.write("game/scenes/structures/launchers/Cannon.tscn")

CATAPULT_AXLE = (0.0, 2.0, 0.2)
CATAPULT_REST = math.radians(20.0)

def catapult():
    sc = Scene("Catapult", M + "launchers/catapult.glb", IMPORT)
    sc.root("structure", "blueprint_catapult", cells=cells_1xn(3, 2))
    sc.model()
    ax = CATAPULT_AXLE
    for side in (-1, 1):
        x = side * 0.82; t = "LR"[side > 0]
        sc.box_b(f"Skid{t}", (x, ax[1], -0.95), (0.14, 2.8, 0.1))
        post_b(sc, f"LegBack{t}", (x, ax[1] - 1.25, -0.9), (x, ax[1], ax[2] + 0.05), 0.045)
        post_b(sc, f"LegFront{t}", (x, ax[1] + 1.25, -0.9), (x, ax[1], ax[2] + 0.05), 0.045)
        post_b(sc, f"StopPost{t}", (x, ax[1] + 1.25, -0.9), (x, ax[1] + 0.45, 1.8), 0.035)
    sc.box_b("Axle", ax, (1.9, 0.1, 0.1))
    sc.box_b("StopBeam", (0, ax[1] + 0.39, 1.8), (0.9, 0.12, 0.16))
    sc.box_b("RestPlate", (0, -0.3, -0.98), (1.6, 1.2, 0.04))
    # the arm and bucket, posed at rest; Model/Arm turns about X with it
    sc.body("ArmBody", b2g(ax), rot_x(CATAPULT_REST))
    for name, c, s in (("Beam", (0, -1.15, 0), (0.16, 2.6, 0.14)), ("ShortEnd", (0, 0.45, 0), (0.16, 0.9, 0.14)),
                       ("Counterweight", (0, 0.9, -0.1), (0.6, 0.5, 0.5)), ("BucketFloor", (0, -2.45, -0.07), (1.3, 1.1, 0.06)),
                       ("BucketL", (-0.63, -2.45, 0.11), (0.04, 1.1, 0.34)), ("BucketR", (0.63, -2.45, 0.11), (0.04, 1.1, 0.34)),
                       ("BucketWall", (0, -1.92, 0.15), (1.3, 0.04, 0.42)), ("BucketLip", (0, -2.98, -0.01), (1.3, 0.04, 0.1))):
        sc.box(name, (s[0], s[2], s[1]), b2g(c), parent="ArmBody")
    sc.body("Bucket", b2g((0, -2.45, 0.12)), sensor=True, size=(1.2, 0.3, 1.0), parent="ArmBody")
    return sc.write("game/scenes/structures/launchers/Catapult.tscn")

# ---------------------------------------------------------------------------- sorting
def filter_basic():
    sc = Scene("FilterBasic", M + "sorting/filter_basic.glb", IMPORT)
    sc.root("structure", "blueprint_filter_basic", cells=[(0, 0, 0), (0, 1, 0)], tags=(TAG_RUBBER,))
    sc.model()
    prof = straight_profile()
    belt_along(sc, prof, [0, 2], BASIC_SPEED)
    lips(sc, prof, [0, 2])
    walls_along(sc, prof, [0, 2], BASIC_WALL, -1, lips=False)
    walls_along(sc, prof, [0, 1], BASIC_WALL, 1, lips=False)
    for i, (x, y) in enumerate(((-0.94, -0.94), (0.94, -0.94), (-0.94, 0.94), (0.94, 0.94))):
        sc.box_b(f"Post{i}", (x, y, (BELT_TOP + 0.02 + 2.2) / 2), (0.08, 0.08, 2.2 - BELT_TOP - 0.02))
    sc.box_b("Housing", (0, 0, 0.72), (1.84, 1.84, 0.5))
    sc.box_b("Deck", (0, 0, 1.0), (1.9, 1.9, 0.06))
    for side in (-1, 1):
        t = "LR"[side > 0]
        sc.box_b(f"BasketX{t}", (side * 0.9, 0, 1.615), (0.06, 1.84, 1.17), friction=WALL_FRICTION)
        sc.box_b(f"BasketY{'BF'[side > 0]}", (0, side * 0.9, 1.615), (1.84, 0.06, 1.17), friction=WALL_FRICTION)
        sc.box_b(f"ScanPost{t}", (side * 0.9, -0.55, 0.0), (0.08, 0.1, 0.9))
    sc.box_b("ScanBeam", (0, -0.55, 0.43), (1.84, 0.12, 0.06))
    # the pusher sweeps matching items out through the right side: PusherBody and Model/Pusher slide along +X
    sc.body("PusherBody", b2g((-0.78, 0.5, 0.36)))
    sc.box("Paddle", (0.05, 0.6, 0.86), (0.06, -0.9, 0), parent="PusherBody", friction=WALL_FRICTION)
    sc.sensor_b("BasketTrigger", (0, 0, 1.6), (1.7, 1.7, 1.1))
    sc.sensor_b("ScanTrigger", (0, -0.4, BELT_TOP + 0.4), (1.6, 0.7, 0.7))
    return sc.write("game/scenes/structures/sorting/FilterBasic.tscn")

def filter_arm():
    sc = Scene("FilterArm", M + "sorting/filter_arm.glb", IMPORT)
    sc.root("structure", "blueprint_filter_arm", cells=[(0, 0, 0), (0, 1, 0)])
    sc.model()
    sc.box_b("Skid", (0, 0, -0.97), (1.9, 1.9, 0.06))
    sc.box_b("Pedestal", (0, 0, -0.5), (1.2, 1.2, 0.88))
    sc.box_b("Turntable", (0, 0, 0.04), (0.88, 0.88, 0.1))
    # idle: scanning the belt beside it
    sc.oscillate("Turret", "rotationDegrees = Vector3(0, 70, 0)", "period = 7.0")
    sc.oscillate("Turret/UpperArm", "rotationDegrees = Vector3(-18, 0, 0)", "period = 5.3", "phase = 0.3")
    sc.oscillate("Turret/UpperArm/Forearm/Head", "rotationDegrees = Vector3(20, 0, 0)", "period = 3.1", "phase = 0.6")
    sc.oscillate("Turret/UpperArm/Forearm/Head/ClawL", "rotationDegrees = Vector3(0, 0, 22)", "period = 2.4")
    sc.oscillate("Turret/UpperArm/Forearm/Head/ClawR", "rotationDegrees = Vector3(0, 0, -22)", "period = 2.4")
    return sc.write("game/scenes/structures/sorting/FilterArm.tscn")

# ---------------------------------------------------------------------------- fields
def projector(name, glb, blueprint, cells_long, cross, spin):
    sc = Scene(name, M + "fields/" + glb, IMPORT)
    sc.root("structure", blueprint)
    sc.model()
    sc.model_prop("Field", f"scale = Vector3(1, 1, {cells_long})")
    sc.spin("Rings", (0, 0, 1), spin)
    sc.box_b("Housing", (0, 0, 0), (1.96, 1.96, 1.96))
    # the field volume in front of the emitter (a sensor, so it may run past the structure's own cell)
    sc.sensor_b("FieldTrigger", (0, 1.0 + cells_long, 0), (cross, 2.0 * cells_long, cross))
    return sc.write(f"game/scenes/structures/fields/{name}.tscn")

# ---------------------------------------------------------------------------- processing
def long_belt(sc, open_span):
    prof = straight_profile()
    belt_along(sc, prof, [0, 4], BASIC_SPEED)
    lips(sc, prof, [0, 4])
    for side in (-1, 1):
        walls_along(sc, prof, [0, open_span[0]], BASIC_WALL, side, lips=False, prefix="In")
        walls_along(sc, prof, [open_span[1], 4], BASIC_WALL, side, lips=False, prefix="Out")

def plate_press():
    sc = Scene("PlatePress", M + "processing/plate_press.glb", IMPORT)
    grinder_root(sc, "blueprint_plate_press", cells_1xn(2), (0, 2.6, BELT_TOP + 0.3), 1.5)
    sc.model()
    sc.oscillate("Ram", "offset = Vector3(0, -0.5, 0)", "period = 2.5", "motion = 1")
    long_belt(sc, (1.3, 2.7))
    for side in (-1, 1):
        t = "LR"[side > 0]
        sc.box_b(f"Column{t}", (side * 0.93, 1.0, 0.0), (0.12, 1.2, 1.96))
        sc.box_b(f"Bed{t}", (side * 0.87, 1.0, BELT_TOP - 0.05), (0.05, 1.2, 0.2))
    sc.box_b("Crown", (0, 1.0, 0.72), (1.98, 1.25, 0.46))
    sc.body("RamBody", b2g((0, 1.0, -0.15)))
    sc.box("Platen", (1.6, 0.16, 1.0), (0, 0.12, 0), parent="RamBody")
    sc.sensor_b("Trigger", (0, 1.0, BELT_TOP + 0.3), (1.5, 1.0, 0.5))
    return sc.write("game/scenes/structures/processing/PlatePress.tscn")

def rod_extruder():
    sc = Scene("RodExtruder", M + "processing/rod_extruder.glb", IMPORT)
    grinder_root(sc, "blueprint_rod_extruder", cells_1xn(2), (0, 2.75, BELT_TOP + 0.3), 2.0)
    sc.model()
    sc.spin("RollerA", (1, 0, 0), 4.0)
    sc.spin("RollerB", (1, 0, 0), -4.0)
    sc.spin("Screw", (0, 0, 1), 3.0)
    long_belt(sc, (0.8, 3.3))
    for side in (-1, 1):
        sc.box_b(f"Housing{'LR'[side > 0]}", (side * 0.94, 1.05, -0.35), (0.1, 2.5, 1.3))
        sc.box_b(f"RollerStand{'LR'[side > 0]}", (side * 0.88, 2.65, -0.3), (0.08, 0.2, 1.0))
    sc.box_b("Roof", (0, 1.05, 0.35), (1.98, 2.5, 0.12))
    sc.box_b("Barrel", (0, 1.0, 0.66), (0.64, 2.3, 0.64))
    sc.box_b("Lintel", (0, -0.2, -0.1), (1.8, 0.06, 0.5))
    sc.box_b("DiePlate", (0, 2.3, -0.35), (1.8, 0.1, 0.9))
    sc.sensor_b("Trigger", (0, 1.0, BELT_TOP + 0.35), (1.6, 2.0, 0.6))
    return sc.write("game/scenes/structures/processing/RodExtruder.tscn")

def polisher():
    sc = Scene("Polisher", M + "processing/polisher.glb", IMPORT)
    grinder_root(sc, "blueprint_polisher", [(0, 0, 0), (1, 0, 0), (0, 0, -1), (1, 0, -1)], (0, 2.6, BELT_TOP + 0.3), 2.5)
    sc.model()
    sc.spin("BrushA", (1, 0, 0), 9.0)
    sc.spin("BrushB", (1, 0, 0), -9.0)
    sc.spin("Buffer", (0, 1, 0), 7.0)
    sc.spin("Fan", (0, 1, 0), 12.0)
    long_belt(sc, (0.9, 3.1))
    walls_along(sc, straight_profile(), [0.9, 3.1], BASIC_WALL, 1, lips=False, prefix="Tunnel")
    sc.box_b("Window", (-0.95, 1.0, -0.2), (0.06, 2.2, 1.6))
    sc.box_b("Roof", (0, 1.0, 0.64), (1.96, 2.2, 0.08))
    sc.box_b("Machinery", (2.0, 1.0, -0.5), (1.8, 3.6, 0.9))
    sc.box_b("Motor", (1.5, 1.0, 0.2), (0.6, 0.9, 0.5))
    sc.box_b("Extractor", (2.4, 2.4, 0.35), (0.8, 0.8, 0.8))
    sc.box_b("Tank", (2.45, 0.1, 0.35), (0.6, 0.6, 0.8))
    sc.sensor_b("Trigger", (0, 1.0, BELT_TOP + 0.3), (1.6, 2.0, 0.5))
    return sc.write("game/scenes/structures/processing/Polisher.tscn")

# ---------------------------------------------------------------------------- props: smelter, spawn tube
def smelter():
    sc = Scene("Smelter", "res://game/assets/models/props/smelter.glb", PROPS_IMPORT)
    grinder_root(sc, "blueprint_smelter", [(x, y, z) for y in (0, 1) for z in (0, -1) for x in (0, 1)], (0, 3.6, -0.3), 3.0)
    sc.model()
    sc.spin("Fan", (0, 0, 1), 6.0)
    sc.spin("Beacon", (0, 1, 0), 4.0)
    sc.box_b("Housing", (1, 1, -0.02), (3.96, 3.96, 1.94))
    sc.box_b("Deck", (1, 1, 1.0), (3.96, 3.96, 0.1))
    for side in (-1, 1):
        sc.box_b(f"HopperX{'LR'[side > 0]}", (1 + side * 1.9, 0.0, 1.625), (0.13, 1.98, 1.15), friction=WALL_FRICTION)
    sc.box_b("HopperFront", (1, 0.925, 1.625), (3.96, 0.13, 1.15), friction=WALL_FRICTION)
    sc.box_b("IntakeLip", (1, -0.925, 1.11), (3.96, 0.13, 0.12), friction=WALL_FRICTION)
    sc.box_b("Kiln", (1.3, 2.0, 1.75), (1.84, 1.84, 1.4))
    sc.box_b("Chimney", (2.55, 2.5, 1.98), (0.4, 0.4, 1.86))
    sc.box_b("Console", (-0.5, 2.25, 1.55), (0.74, 0.94, 1.04))
    sc.sensor_b("Trigger", (1, 0, 1.6), (3.6, 1.6, 1.0))
    return sc.write("game/scenes/structures/Smelter.tscn")

def spawn_tube():
    sc = Scene("SpawnTube", "res://game/assets/models/props/spawn_tube.glb", PROPS_IMPORT)
    sc.root("spawner", "", cells=[(0, 0, 0), (0, 1, 0)], extra=[
        "itemWeights = Dictionary[String, float]({\"test_1x1x1cube\": 1.0})",
        "interval = 2.0",
        f"outputPoint = {v3(b2g((0, 0, 1.8)))}"])
    sc.model()
    sc.spin("Rings", (0, 1, 0), 1.5)
    for k in range(8):
        a = k / 8 * math.tau
        d = (math.cos(a), math.sin(a), 0); t = (-math.sin(a), math.cos(a), 0)
        sc.obox_b(f"Tube{k}", (d[0] * 0.8, d[1] * 0.8, 0.64), mul(t, 0.66), mul(d, 0.04), (0, 0, 3.24), friction=WALL_FRICTION)
    sc.box_b("Hood", (0, 0, 2.64), (1.8, 1.8, 0.56))
    return sc.write("game/scenes/structures/SpawnTube.tscn")

# ---------------------------------------------------------------------------- families
def launchers():
    return [launch_ramp(), cannon(), catapult()]

def sorting():
    return [filter_basic(), filter_arm()]

def fields():
    return [projector("AntigravProjector", "antigrav_projector.glb", "blueprint_antigrav_projector", 4, 1.9, 1.2),
            projector("ZeroPointProjector", "zeropoint_projector.glb", "blueprint_zeropoint_projector", 8, 1.6, -0.8)]

def processing():
    return [plate_press(), rod_extruder(), polisher()]

def props():
    return [smelter(), spawn_tube()]
