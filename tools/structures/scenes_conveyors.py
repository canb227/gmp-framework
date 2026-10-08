"""
Scenes for the conveyor families: basic conveyors and additions (splitter, switch splitter, loader), advanced
conveyors and magnetic conveyors (floor / wall / ceiling forms). Colliders follow build_conveyors.py,
build_conveyor_extras.py, build_conveyors_advanced.py and build_conveyors_magnetic.py. Unlike machines' belts,
these belts expose their return run underneath (ret=True).

The floor conveyors have no separate turn pieces: each is one ConveyorStructure scene holding a straight and two
turns as variants, and the build grid picks the form from the belts feeding it (BuildGrid.BeltShapes.cs). The wall
and ceiling magnetic conveyors keep their own left and right turns.
"""
import math, re
from scenegen import *

BASIC_IMPORT = "res://game/assets/models/conveyors/conveyor_import.gd"
SALVAGE_IMPORT = "res://game/assets/models/shared/salvage_import.gd"
BASIC_SPEED, ADV_SPEED, MAG_SPEED = 2.0, 3.0, 2.0
BASIC_WALL, ADV_WALL = 0.25, 0.53          # guard top above the belt (advanced includes its top rail)
LOADER = dict(total_y=1.95, rise=0.35, R=1.2, th=math.radians(20.0))
# The kicker's lip isn't on the front face, so the belt's end isn't a port; this one, where a straight conveyor's
# output is, makes neighbours treat the loader like a straight (a conveyor across its front turns, others snap to it).
LOADER_OUT = ("out", (0, 0, 0), "Front", (2, 1))

def lips(sc, prof, st, sides=(-1, 1)):
    for side in sides:
        tag = "L" if side < 0 else "R"
        for i in range(len(st) - 1):
            seg_b(sc, f"Lip{tag}{i}", prof, st[i], st[i + 1], side * sum(LIP_X) / 2, LIP_X[1] - LIP_X[0],
                  LIP_O[0], LIP_O[1], friction=WALL_FRICTION)

def straight_cols(sc, speed, wall_top):
    prof = straight_profile()
    belt_along(sc, prof, [0, 2], speed, ret=True)
    for side in (-1, 1):
        walls_along(sc, prof, [0, 2], wall_top, side)

def belt_speed(sc, speed, mirrored=False):
    """Instance parameters of the model's belt mesh (ConveyorBeltLoop.gdshader): its speed, and whether the model is
    a mirrored build whose belt u runs right to left across the flow (the left turns), so the line pattern can
    match it to its neighbours."""
    props = [f"instance_shader_parameters/belt_speed = {ff(speed)}" if speed != 2.0 else None,
             "instance_shader_parameters/belt_mirror_u = 1.0" if mirrored else None]
    if any(props):
        sc.model_prop("Belt", *props)

# ---------------------------------------------------------------------------- basic additions
def splitter_common(sc, front_h):
    prof = straight_profile()
    belt_along(sc, prof, [0, 2], BASIC_SPEED, ret=True)
    lips(sc, prof, [0, 2])
    for side in (-1, 1):
        walls_along(sc, prof, [0, 1], BASIC_WALL, side, lips=False)          # guards on the back half only
    sc.box_b("FrontWall", (0, 0.955, BELT_TOP + front_h / 2 - 0.02), (1.9, 0.07, front_h + 0.04), friction=WALL_FRICTION)

def splitter():
    sc = Scene("ConveyorSplitter", "res://game/assets/models/conveyors/conveyor_splitter.glb", BASIC_IMPORT)
    sc.root("structure", "blueprint_conveyor_splitter", layers=1, tags=(TAG_RUBBER,))
    sc.model()
    splitter_common(sc, 0.22)
    z0, z1 = BELT_TOP + 0.015, BELT_TOP + 0.34
    tip = (0.0, -0.25)
    for tag, end in (("L", (-0.84, 0.9)), ("R", (0.84, 0.9))):
        d = (end[0] - tip[0], end[1] - tip[1])
        ln = math.hypot(*d); t = (d[0] / ln, d[1] / ln)
        n_in = (-t[1], t[0]) if tag == "R" else (t[1], -t[0])               # into the wedge
        c = ((tip[0] + end[0]) / 2 + n_in[0] * 0.03, (tip[1] + end[1]) / 2 + n_in[1] * 0.03, (z0 + z1) / 2)
        sc.obox_b(f"Wedge{tag}", c, (d[0], d[1], 0), (n_in[0] * 0.06, n_in[1] * 0.06, 0), (0, 0, z1 - z0), friction=WALL_FRICTION)
    return sc.write("game/scenes/structures/conveyors/basic/ConveyorSplitter.tscn")

SWITCH_PIVOT_B = (0.0, 0.35, BELT_TOP)
SWITCH_ANGLE = math.radians(33.0)

def splitter_switch():
    sc = Scene("ConveyorSplitterSwitch", "res://game/assets/models/conveyors/conveyor_splitter_switch.glb", BASIC_IMPORT)
    sc.root("structure", "blueprint_conveyor_splitter_switch", layers=1, tags=(TAG_RUBBER,))
    sc.model()
    splitter_common(sc, 0.3)
    # the paddle is its own static body so a switch script can turn it with Model/Arm (+33 deg right, -33 left)
    sc.body("ArmBody", b2g(SWITCH_PIVOT_B), rot_y(SWITCH_ANGLE))
    sc.box("Paddle", (1.94, 0.3, 0.06), (0, 0.19, 0), parent="ArmBody", friction=WALL_FRICTION)
    # the handle players flip (Lever tilts Model/Lever)
    gb = (0.35, 0.95, BELT_TOP + 0.38)
    sc.body("LeverBody", b2g((gb[0] + 0.1, gb[1], gb[2] + 0.1)), size=(0.45, 0.5, 0.12), script_key="lever",
            extra=('handle = NodePath("../Model/Lever")', 'displayName = "Splitter Switch"'), paths=("handle",))
    return sc.write("game/scenes/structures/conveyors/basic/ConveyorSplitterSwitch.tscn")

def loader_cols(sc, speed, wall_top):
    prof, L, flat, ls = bend_up_profile(**LOADER)
    bend = flat + LOADER["R"] * LOADER["th"]
    st = stations([(0, flat, 1), (flat, bend, 6), (bend, L, 1)])
    belt_along(sc, prof, st, speed, y_bounds=(-1, 1), ret=True, end_port=False)     # the kicker lifts items over a wall
    for side in (-1, 1):
        walls_along(sc, prof, stations([(0, flat, 1), (flat, bend, 3), (bend, L, 1)]), wall_top, side, y_bounds=(-1, 1))
    return prof, L

def loader():
    sc = Scene("ConveyorLoader", "res://game/assets/models/conveyors/conveyor_loader.glb", BASIC_IMPORT)
    sc.root("structure", "blueprint_conveyor_loader", layers=1, tags=(TAG_RUBBER,), ports=(LOADER_OUT,))
    sc.model()
    loader_cols(sc, BASIC_SPEED, BASIC_WALL)
    return sc.write("game/scenes/structures/conveyors/basic/ConveyorLoader.tscn")

# ---------------------------------------------------------------------------- turns
def turn_scene(name, model, imp, blueprint, left, speed, wall_top, xf=None, post_h=None):
    """A quarter turn on its own (the wall and ceiling magnetic conveyors): see turn_parts."""
    sc = Scene(name, model, imp, xf=xf)
    sc.root("structure", blueprint, tags=(TAG_RUBBER,))
    turn_parts(sc, left, speed, wall_top, post_h)
    return sc

def turn_parts(sc, left, speed, wall_top, post_h=None):
    """A quarter turn's model, belt and colliders: the belt follows the radius-1 arc from the back face to the side
    face (left or right), and the guard arcs and lips are boxes."""
    mx = -1 if left else 1
    P = lambda r, th, y: (mx * (1 - r * math.cos(th)), y, 1 - r * math.sin(th))
    sc.model()
    belt_speed(sc, speed, mirrored=left)    # the left turn models are built mirrored
    arc_pts = [P(1, math.pi / 2 * i / 64, BELT_TOP) for i in range(65)]
    sc.belt(simplify(arc_pts), speed, ret=True)
    def arc(prefix, r0, r1, y0, y1, n):
        rc = (r0 + r1) / 2
        for i in range(n):
            tc = math.pi / 2 * (i + 0.5) / n
            ln = 2 * r1 * math.sin(math.pi / 4 / n) + 0.02                               # overlap at the joints
            if i in (0, n - 1):                                                           # square ends stay in the cell
                trim = 0.02 + (r1 - r0) * math.tan(math.pi / 4 / n) + 0.004
                ln -= trim
                tc += (trim / 2 / rc) * (1 if i == 0 else -1)
            x, y, z = P(rc, tc, (y0 + y1) / 2)
            ry = mx * (math.pi - tc) if mx > 0 else -(math.pi - tc)
            sc.box(f"{prefix}{i}", (r1 - r0, y1 - y0, ln), (x, y, z), rot_y(ry), friction=WALL_FRICTION)
    arc("WallOuter", 1 + WALL_X[0], 1 + WALL_X[1], BELT_TOP - 0.15, BELT_TOP + wall_top, 8)
    arc("LipOuter", 1 + LIP_X[0], 1 + LIP_X[1], BELT_TOP + LIP_O[0], BELT_TOP + LIP_O[1], 8)
    arc("LipInner", 1 - LIP_X[1], 1 - LIP_X[0], BELT_TOP + LIP_O[0], BELT_TOP + LIP_O[1], 3)
    ph = post_h or wall_top
    sc.box("PivotPost", (0.12, ph + 0.15, 0.12), (mx * 0.93, BELT_TOP + (ph - 0.15) / 2, 0.93), friction=WALL_FRICTION)

# ---------------------------------------------------------------------------- self-turning floor conveyors
# The forms a floor conveyor switches between, as (name, left turn?, turn about the root). ConveyorStructure.cs
# takes the first as the default. Every form takes items in through a different face and lets them out through the
# front: the turns are the back-to-side turn models given a quarter turn, so the side they turn toward faces forward.
VARIANTS = (("Straight", None, None),
            ("TurnFromLeft", True, rot_y(-math.pi / 2)),     # the left turn: back -> left becomes left -> front
            ("TurnFromRight", False, rot_y(math.pi / 2)))    # the right turn: back -> right becomes right -> front
OUTPUT_END = (0.0, BELT_TOP, -1.0)
# Under the return run, at floor level: the root keeps one collider of its own, since a Box3D body without any
# falls back to a zero-size box at its origin, which would sit in the items' way above the belt.
PAD_SIZE, PAD_Y = (0.3, 0.02, 0.3), -0.985

def auto_conveyor(name, path, imp, blueprint, straight_model, turn_models, speed, wall_top, post_h=None, layers=2):
    """One floor conveyor holding its straight and both turns (see VARIANTS); `turn_models` is (right, left)."""
    sc = Scene(name, None, imp)
    sc.root("conveyor", blueprint, tags=(TAG_RUBBER,), layers=layers)
    sc.box("Pad", PAD_SIZE, (0, PAD_Y, 0), friction=WALL_FRICTION, material=TAG_RUBBER)
    for vname, left, xf in VARIANTS:
        with sc.variant(vname, straight_model if left is None else turn_models[left], imp, xf, active=left is None):
            if left is None:
                sc.model()
                belt_speed(sc, speed)
                straight_cols(sc, speed, wall_top)
            else:
                turn_parts(sc, left, speed, wall_top, post_h)
    # Switching form must never move the belt's output end: the forms are picked from the outputs feeding each belt,
    # so if an output could move, one belt's form could change another's.
    ends = re.findall(r'\[node name="Belt" type="Node3D" parent="(\w+)"\]\n[^\n]*\npoints = PackedVector3Array\(([^)]*)\)',
                      "\n".join(sc.nodes))
    assert [v for v, _ in ends] == [v for v, _, _ in VARIANTS], ends
    for vname, pts in ends:
        last = tuple(float(x) for x in pts.split(", ")[-3:])
        assert all(abs(a - b) < 1e-4 for a, b in zip(last, OUTPUT_END)), (name, vname, last)
    return sc.write(path)

# ---------------------------------------------------------------------------- slopes
def slope_scene(name, model, imp, blueprint, down, speed, wall_top):
    """Up slope (items climb toward the front) or its down form (the up slope turned half round about the
    footprint centre, belt reversed), as gen_colliders.slope(). L-shaped: the module over the low end is empty (the
    belt is only 1 m up where it leaves it), so it isn't part of the footprint."""
    xf, xo = (rot_y(math.pi), (0, 0, -2)) if down else (None, (0, 0, 0))
    sc = Scene(name, model, imp, xf=xf, xo=xo)
    upper = (0, 1, 0) if down else (0, 1, -1)          # the module over the high end
    sc.root("structure", blueprint, cells=((0, 0, 0), (0, 0, -1), upper), tags=(TAG_RUBBER,))
    sc.model()
    if down or speed != 2.0:
        sc.model_prop("Belt", f"instance_shader_parameters/belt_speed = {ff(-speed if down else speed)}")
    prof, L = slope_profile()
    a1 = SLOPE_R * SLOPE_TH
    # grippier, and the climb pushes up to half as fast again on the 30 deg incline (the down slope just runs back)
    belt_along(sc, prof, [0, L], speed, friction=SLOPE_FRICTION, climb=None if down else speed * 1.5, reverse=down, ret=True)
    wst = stations([(0, a1, 3), (a1, a1 + SLOPE_LS, 1), (a1 + SLOPE_LS, L, 3)])
    for side in (-1, 1):
        walls_along(sc, prof, wst, wall_top, side, y_bounds=(-1, 3))
    return sc

# ---------------------------------------------------------------------------- families
def basic():
    """The salvage conveyors (models from build_conveyors.py): the self-turning conveyor and slopes."""
    out = []
    M = "res://game/assets/models/conveyors/"
    D = "game/scenes/structures/conveyors/basic/"
    out.append(auto_conveyor("Conveyor", D + "Conveyor.tscn", BASIC_IMPORT, "blueprint_conveyor", M + "conveyor_straight.glb",
                             (M + "conveyor_turn_right.glb", M + "conveyor_turn_left.glb"), BASIC_SPEED, BASIC_WALL, layers=1))
    for down in (False, True):
        nm = "ConveyorSlopeDown" if down else "ConveyorSlope"
        sc = slope_scene(nm, M + "conveyor_slope.glb", BASIC_IMPORT, "blueprint_conveyor_slope", down, BASIC_SPEED, BASIC_WALL)
        out.append(sc.write(D + nm + ".tscn"))
    return out

def basic_extras():
    return [splitter(), splitter_switch(), loader()]

def advanced():
    out = []
    M = "res://game/assets/models/conveyors_advanced/"
    D = "game/scenes/structures/conveyors/advanced/"
    out.append(auto_conveyor("ConveyorAdvanced", D + "ConveyorAdvanced.tscn", SALVAGE_IMPORT, "blueprint_conveyor_adv",
                             M + "conveyor_adv_straight.glb", (M + "conveyor_adv_turn_right.glb", M + "conveyor_adv_turn_left.glb"),
                             ADV_SPEED, ADV_WALL))
    for down in (False, True):
        nm = "ConveyorAdvancedSlopeDown" if down else "ConveyorAdvancedSlope"
        sc = slope_scene(nm, M + "conveyor_adv_slope.glb", SALVAGE_IMPORT, "blueprint_conveyor_adv_slope", down, ADV_SPEED, ADV_WALL)
        out.append(sc.write(D + nm + ".tscn"))
    sc = Scene("ConveyorAdvancedLoader", M + "conveyor_adv_loader.glb", SALVAGE_IMPORT)
    sc.root("structure", "blueprint_conveyor_adv_loader", tags=(TAG_RUBBER,), ports=(LOADER_OUT,))
    sc.model(); belt_speed(sc, ADV_SPEED)
    loader_cols(sc, ADV_SPEED, ADV_WALL)
    out.append(sc.write(D + "ConveyorAdvancedLoader.tscn"))
    return out

# mounts: the models are floor-mounted; turning about the flow axis (Godot Z) puts the belt on the -X wall or
# the ceiling of the cell, with items pressed onto the belt by the magnets
MOUNTS = [("", None, ""), ("Wall", rot_z(-math.pi / 2), "_wall"), ("Ceiling", rot_z(math.pi), "_ceiling")]

def magnetic():
    out = []
    M = "res://game/assets/models/conveyors_magnetic/"
    D = "game/scenes/structures/conveyors/magnetic/"
    turns = (M + "conveyor_mag_turn_right.glb", M + "conveyor_mag_turn_left.glb")
    for mount, xf, bp in MOUNTS:
        if not mount:
            out.append(auto_conveyor("ConveyorMagnetic", D + "ConveyorMagnetic.tscn", SALVAGE_IMPORT, "blueprint_conveyor_mag",
                                     M + "conveyor_mag_straight.glb", turns, MAG_SPEED, BASIC_WALL, post_h=BASIC_WALL + 0.1))
            continue
        sc = Scene(f"ConveyorMagnetic{mount}", M + "conveyor_mag_straight.glb", SALVAGE_IMPORT, xf=xf)
        sc.root("structure", f"blueprint_conveyor_mag{bp}", tags=(TAG_RUBBER,))
        sc.model()
        straight_cols(sc, MAG_SPEED, BASIC_WALL)
        out.append(sc.write(D + f"ConveyorMagnetic{mount}.tscn"))
        for left in (False, True):
            side = "Left" if left else "Right"
            sc = turn_scene(f"ConveyorMagnetic{mount}Turn{side}", turns[left], SALVAGE_IMPORT,
                            f"blueprint_conveyor_mag_turn{bp}", left, MAG_SPEED, BASIC_WALL, xf=xf, post_h=BASIC_WALL + 0.1)
            out.append(sc.write(D + f"ConveyorMagnetic{mount}Turn{side}.tscn"))
    return out
