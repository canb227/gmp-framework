"""
Scenes for the conveyor families: basic additions (splitter, switch splitter, loader), advanced conveyors and
magnetic conveyors (floor / wall / ceiling forms). Colliders follow build_conveyor_extras.py,
build_conveyors_advanced.py and build_conveyors_magnetic.py.
"""
import math
from scenegen import *

BASIC_IMPORT = "res://game/assets/models/conveyors/conveyor_import.gd"
SALVAGE_IMPORT = "res://game/assets/models/shared/salvage_import.gd"
BASIC_SPEED, ADV_SPEED, MAG_SPEED = 2.0, 3.0, 2.0
BASIC_WALL, ADV_WALL = 0.25, 0.53          # guard top above the belt (advanced includes its top rail)
LOADER = dict(total_y=1.95, rise=0.35, R=1.2, th=math.radians(20.0))

def lips(sc, prof, st, sides=(-1, 1)):
    for side in sides:
        tag = "L" if side < 0 else "R"
        for i in range(len(st) - 1):
            seg_b(sc, f"Lip{tag}{i}", prof, st[i], st[i + 1], side * sum(LIP_X) / 2, LIP_X[1] - LIP_X[0],
                  LIP_O[0], LIP_O[1], friction=WALL_FRICTION)

def straight_cols(sc, speed, wall_top):
    prof = straight_profile()
    belt_along(sc, prof, [0, 2], speed)
    for side in (-1, 1):
        walls_along(sc, prof, [0, 2], wall_top, side)

def belt_speed(sc, speed):
    if speed != 2.0:
        sc.model_prop("Belt", f"instance_shader_parameters/belt_speed = {f(speed)}")

# ---------------------------------------------------------------------------- basic additions
def splitter_common(sc, front_h):
    prof = straight_profile()
    belt_along(sc, prof, [0, 2], BASIC_SPEED)
    lips(sc, prof, [0, 2])
    for side in (-1, 1):
        walls_along(sc, prof, [0, 1], BASIC_WALL, side, lips=False)          # guards on the back half only
    sc.box_b("FrontWall", (0, 0.955, BELT_TOP + front_h / 2 - 0.02), (1.9, 0.07, front_h + 0.04), friction=WALL_FRICTION)

def splitter():
    sc = Scene("ConveyorSplitter", "res://game/assets/models/conveyors/conveyor_splitter.glb", BASIC_IMPORT)
    sc.root("structure", "blueprint_conveyor_splitter", tags=(TAG_RUBBER,))
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
    return sc.write("game/scenes/structures/conveyors/ConveyorSplitter.tscn")

SWITCH_PIVOT_B = (0.0, 0.35, BELT_TOP)
SWITCH_ANGLE = math.radians(33.0)

def splitter_switch():
    sc = Scene("ConveyorSplitterSwitch", "res://game/assets/models/conveyors/conveyor_splitter_switch.glb", BASIC_IMPORT)
    sc.root("structure", "blueprint_conveyor_splitter_switch", tags=(TAG_RUBBER,))
    sc.model()
    splitter_common(sc, 0.3)
    # the paddle is its own static body so a switch script can turn it with Model/Arm (+33 deg right, -33 left)
    sc.body("ArmBody", b2g(SWITCH_PIVOT_B), rot_y(SWITCH_ANGLE))
    sc.box("Paddle", (1.94, 0.3, 0.06), (0, 0.19, 0), parent="ArmBody", friction=WALL_FRICTION)
    # the handle players flip (Lever tilts Model/Lever)
    gb = (0.35, 0.95, BELT_TOP + 0.38)
    sc.body("LeverBody", b2g((gb[0] + 0.1, gb[1], gb[2] + 0.1)), size=(0.45, 0.5, 0.12), script_key="lever",
            extra=('handle = NodePath("../Model/Lever")', 'displayName = "Splitter Switch"'), paths=("handle",))
    return sc.write("game/scenes/structures/conveyors/ConveyorSplitterSwitch.tscn")

def loader_cols(sc, speed, wall_top):
    prof, L, flat, ls = bend_up_profile(**LOADER)
    bend = flat + LOADER["R"] * LOADER["th"]
    st = stations([(0, flat, 1), (flat, bend, 6), (bend, L, 1)])
    belt_along(sc, prof, st, speed, y_bounds=(-1, 1))
    for side in (-1, 1):
        walls_along(sc, prof, stations([(0, flat, 1), (flat, bend, 3), (bend, L, 1)]), wall_top, side, y_bounds=(-1, 1))
    return prof, L

def loader():
    sc = Scene("ConveyorLoader", "res://game/assets/models/conveyors/conveyor_loader.glb", BASIC_IMPORT)
    sc.root("structure", "blueprint_conveyor_loader", arrow=1, tags=(TAG_RUBBER,))
    sc.model()
    loader_cols(sc, BASIC_SPEED, BASIC_WALL)
    return sc.write("game/scenes/structures/conveyors/ConveyorLoader.tscn")

# ---------------------------------------------------------------------------- turns (curved belt as a mesh shape)
def turn_scene(name, model, imp, blueprint, left, speed, wall_top, xf=None, post_h=None):
    """As gen_colliders.turn(): the root is a mesh shape whose triangles each carry their own tangent velocity
    along the arc; the guard arcs and lips are boxes on a child Rails body."""
    mx = -1 if left else 1
    N = 16
    r_in, r_out = 1 - BELT_W, 1 + BELT_W
    P = lambda r, th, y: (mx * (1 - r * math.cos(th)), y, 1 - r * math.sin(th))
    verts, idx, mats, surf = [], [], [], []
    R = xf or IDENT
    for layer, (y, sign) in enumerate(((BELT_TOP, 1), (RET_Y, -1))):
        for i in range(N):
            t0, t1 = math.pi / 2 * i / N, math.pi / 2 * (i + 1) / N
            tm = (t0 + t1) / 2
            b = len(verts)
            verts += [P(r_in, t0, y), P(r_out, t0, y), P(r_out, t1, y), P(r_in, t1, y)]
            for tri in ((0, 1, 2), (0, 2, 3)):
                a, c, d = (verts[b + j] for j in tri)
                n_y = (c[2] - a[2]) * (d[0] - a[0]) - (c[0] - a[0]) * (d[2] - a[2])
                tri = tri if (n_y > 0) == (sign > 0) else (tri[0], tri[2], tri[1])
                idx += [b + j for j in tri]
                mats.append(layer * N + i)
            vx, vz = speed * math.sin(tm), -speed * math.cos(tm)
            surf.append(mat_vec(R, (mx * vx * sign, 0, vz * sign)))
    verts = [mat_vec(R, v) for v in verts]
    sm = ", ".join('{"custom_color": 0, "friction": %s, "restitution": 0.0, "rolling_resistance": 0.0, "tangent_velocity": %s, "user_material_id": %d}'
                   % (f(BELT_FRICTION), v3(t), TAG_RUBBER) for t in surf)
    sc = Scene(name, model, imp, xf=xf)
    sc.root("structure", blueprint, arrow=0 if xf else (2 if left else 3), tags=(TAG_RUBBER,), extra=[
        "shape_type = 6",
        "mesh_vertices = PackedVector3Array(" + ", ".join(f"{f(a)}, {f(b)}, {f(c)}" for a, b, c in verts) + ")",
        "mesh_indices = PackedInt32Array(" + ", ".join(str(i) for i in idx) + ")",
        "mesh_materials = PackedByteArray(" + ", ".join(str(m) for m in mats) + ")",
        f"surface_materials = Array[Dictionary]([{sm}])"])
    sc.model()
    belt_speed(sc, speed)
    sc.body("Rails")
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
            sc.box(f"{prefix}{i}", (r1 - r0, y1 - y0, ln), (x, y, z), rot_y(ry), parent="Rails", friction=WALL_FRICTION)
    arc("WallOuter", 1 + WALL_X[0], 1 + WALL_X[1], BELT_TOP - 0.15, BELT_TOP + wall_top, 8)
    arc("LipOuter", 1 + LIP_X[0], 1 + LIP_X[1], BELT_TOP + LIP_O[0], BELT_TOP + LIP_O[1], 8)
    arc("LipInner", 1 - LIP_X[1], 1 - LIP_X[0], BELT_TOP + LIP_O[0], BELT_TOP + LIP_O[1], 3)
    ph = post_h or wall_top
    sc.box("PivotPost", (0.12, ph + 0.15, 0.12), (mx * 0.93, BELT_TOP + (ph - 0.15) / 2, 0.93), parent="Rails", friction=WALL_FRICTION)
    return sc

# ---------------------------------------------------------------------------- slopes
def slope_scene(name, model, imp, blueprint, down, speed, wall_top):
    """Up slope (items climb toward the front) or its down form (the up slope turned half round about the
    footprint centre, belt reversed), as gen_colliders.slope()."""
    xf, xo = (rot_y(math.pi), (0, 0, -2)) if down else (None, (0, 0, 0))
    sc = Scene(name, model, imp, xf=xf, xo=xo)
    sc.root("structure", blueprint, cells=((0, 0, 0), (0, 0, -1), (0, 1, 0), (0, 1, -1)), arrow=4 if down else 1, tags=(TAG_RUBBER,))
    sc.model()
    if down or speed != 2.0:
        sc.model_prop("Belt", f"instance_shader_parameters/belt_speed = {f(-speed if down else speed)}")
    prof, L = slope_profile()
    a1 = SLOPE_R * SLOPE_TH
    st = stations([(0, a1, 6), (a1, a1 + SLOPE_LS, 1), (a1 + SLOPE_LS, L, 6)])
    for i in range(len(st) - 1):
        mid_ph = (prof(st[i])[1] + prof(st[i + 1])[1]) / 2
        v = speed if down else speed * (1 + 0.5 * mid_ph / SLOPE_TH)       # grippier, faster climb (as the basic slope)
        v = -v if down else v
        seg_b(sc, f"BeltTop{i}", prof, st[i], st[i + 1], 0, 2 * BELT_W, -TOP_T, 0, speed=v, friction=SLOPE_FRICTION,
              material=TAG_RUBBER, y_bounds=(-1, 3))
        seg_b(sc, f"BeltReturn{i}", prof, st[i], st[i + 1], 0, 2 * LIP_X[0], -BELT_H, -BELT_H + RET_T, ref_top=False,
              speed=-v, friction=SLOPE_FRICTION, material=TAG_RUBBER, y_bounds=(-1, 3))
    wst = stations([(0, a1, 3), (a1, a1 + SLOPE_LS, 1), (a1 + SLOPE_LS, L, 3)])
    for side in (-1, 1):
        walls_along(sc, prof, wst, wall_top, side, y_bounds=(-1, 3))
    return sc

# ---------------------------------------------------------------------------- families
def basic_extras():
    return [splitter(), splitter_switch(), loader()]

def advanced():
    out = []
    M = "res://game/assets/models/conveyors_advanced/"
    D = "game/scenes/structures/conveyors_advanced/"
    sc = Scene("ConveyorAdvanced", M + "conveyor_adv_straight.glb", SALVAGE_IMPORT)
    sc.root("structure", "blueprint_conveyor_adv", arrow=1, tags=(TAG_RUBBER,))
    sc.model(); belt_speed(sc, ADV_SPEED)
    straight_cols(sc, ADV_SPEED, ADV_WALL)
    out.append(sc.write(D + "ConveyorAdvanced.tscn"))
    for left in (False, True):
        side = "Left" if left else "Right"
        sc = turn_scene(f"ConveyorAdvancedTurn{side}", M + f"conveyor_adv_turn_{side.lower()}.glb", SALVAGE_IMPORT,
                        "blueprint_conveyor_adv_turn", left, ADV_SPEED, ADV_WALL)
        out.append(sc.write(D + f"ConveyorAdvancedTurn{side}.tscn"))
    for down in (False, True):
        nm = "ConveyorAdvancedSlopeDown" if down else "ConveyorAdvancedSlope"
        sc = slope_scene(nm, M + "conveyor_adv_slope.glb", SALVAGE_IMPORT, "blueprint_conveyor_adv_slope", down, ADV_SPEED, ADV_WALL)
        out.append(sc.write(D + nm + ".tscn"))
    sc = Scene("ConveyorAdvancedLoader", M + "conveyor_adv_loader.glb", SALVAGE_IMPORT)
    sc.root("structure", "blueprint_conveyor_adv_loader", arrow=1, tags=(TAG_RUBBER,))
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
    D = "game/scenes/structures/conveyors_magnetic/"
    for mount, xf, bp in MOUNTS:
        sc = Scene(f"ConveyorMagnetic{mount}", M + "conveyor_mag_straight.glb", SALVAGE_IMPORT, xf=xf)
        sc.root("structure", f"blueprint_conveyor_mag{bp}", arrow=0 if xf else 1, tags=(TAG_RUBBER,))
        sc.model()
        straight_cols(sc, MAG_SPEED, BASIC_WALL)
        out.append(sc.write(D + f"ConveyorMagnetic{mount}.tscn"))
        for left in (False, True):
            side = "Left" if left else "Right"
            sc = turn_scene(f"ConveyorMagnetic{mount}Turn{side}", M + f"conveyor_mag_turn_{side.lower()}.glb", SALVAGE_IMPORT,
                            f"blueprint_conveyor_mag_turn{bp}", left, MAG_SPEED, BASIC_WALL, xf=xf, post_h=BASIC_WALL + 0.1)
            out.append(sc.write(D + f"ConveyorMagnetic{mount}Turn{side}.tscn"))
    return out
