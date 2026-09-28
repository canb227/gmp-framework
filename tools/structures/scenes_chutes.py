"""
Chute scenes (basic chute_* and advanced chute_adv_* models share collider layouts). Colliders follow
build_chutes.py: interior +-IN, walls WT thick, channel floor at belt height.

Advanced chutes are "powered": their Floor shape is where a push goes (set its tangent_velocity toward the exit,
e.g. Vector3(0, 0, -0.6) on a straight) and Model/Power holds the emitters to light while powered.
The dropper's doors are static child bodies at their hinges (DoorLBody / DoorRBody): opening turns DoorLBody
and Model/DoorL by -80 deg about Z, DoorRBody and Model/DoorR by +80 deg. LeverBody is the handle players flip.
"""
import math
from scenegen import *

IMPORT = "res://game/assets/models/shared/salvage_import.gd"
IN, WT = 0.75, 0.08
FLOOR_TOP = BELT_TOP
H_WALL = 1.2
DOOR_Z = -0.15

def floor_friction(tier):
    return 0.05 if tier == "basic" else 0.1          # free rollers / polished motor deck

def arc_walls(sc, prefix, r0, r1, z0, z1, n, left=False):
    """Walls along a quarter arc about the right turn's pivot (Godot x = 1, z = 1), as the conveyor turns."""
    mx = -1 if left else 1
    P = lambda r, th, y: (mx * (1 - r * math.cos(th)), y, 1 - r * math.sin(th))
    rc = (r0 + r1) / 2
    for i in range(n):
        tc = math.pi / 2 * (i + 0.5) / n
        ln = 2 * r1 * math.sin(math.pi / 4 / n) + 0.02
        if i in (0, n - 1):
            trim = 0.02 + (r1 - r0) * math.tan(math.pi / 4 / n) + 0.004
            ln -= trim
            tc += (trim / 2 / rc) * (1 if i == 0 else -1)
        x, y, z = P(rc, tc, (z0 + z1) / 2)
        ry = mx * (math.pi - tc) if mx > 0 else -(math.pi - tc)
        sc.box(f"{prefix}{i}", (r1 - r0, z1 - z0, ln), (x, y, z), rot_y(ry), friction=WALL_FRICTION)

def bore_walls(sc, z0, z1, cx=0.0, cy=0.0, prefix="Bore"):
    h, zc = z1 - z0, (z0 + z1) / 2
    for side, tag in ((-1, "L"), (1, "R")):
        sc.box_b(f"{prefix}X{tag}", (cx + side * (IN + WT / 2), cy, zc), (WT, 2 * IN + 2 * WT, h), friction=WALL_FRICTION)
    for side, tag in ((-1, "B"), (1, "F")):
        sc.box_b(f"{prefix}Y{tag}", (cx, cy + side * (IN + WT / 2), zc), (2 * IN, WT, h), friction=WALL_FRICTION)

def funnel_walls(sc, top, bot, z_top, z_bot):
    """One oriented box per funnel wall, lying in the wall's plane and grown outward (as build_chutes.funnel)."""
    (tx0, tx1, ty0, ty1), (bx0, bx1, by0, by1) = top, bot
    ctr = ((tx0 + tx1 + bx0 + bx1) / 4, (ty0 + ty1 + by0 + by1) / 4, (z_top + z_bot) / 2)
    walls = {
        "FunnelXL": ((tx0, ty0, z_top), (tx0, ty1, z_top), (bx0, by1, z_bot), (bx0, by0, z_bot)),
        "FunnelXR": ((tx1, ty1, z_top), (tx1, ty0, z_top), (bx1, by0, z_bot), (bx1, by1, z_bot)),
        "FunnelYB": ((tx1, ty0, z_top), (tx0, ty0, z_top), (bx0, by0, z_bot), (bx1, by0, z_bot)),
        "FunnelYF": ((tx0, ty1, z_top), (tx1, ty1, z_top), (bx1, by1, z_bot), (bx0, by1, z_bot)),
    }
    for name, (a, b, c, d) in walls.items():
        u = norm(sub(b, a))                                    # along the top edge
        top_mid, bot_mid = mul(add(a, b), 0.5), mul(add(c, d), 0.5)
        v = sub(bot_mid, top_mid)
        v = sub(v, mul(u, dot(v, u)))                          # down the slope, square to the top edge
        n = norm(cross(u, v))
        if dot(n, sub(top_mid, ctr)) < 0:
            n = mul(n, -1)
        # u-extent covering both the top and bottom edges
        us = [dot(sub(p, a), u) for p in (a, b, c, d)]
        u0, u1 = min(us), max(us)
        centre = add(add(add(a, mul(u, (u0 + u1) / 2)), mul(v, 0.5)), mul(n, WT / 2))
        sc.obox_b(name, centre, mul(u, u1 - u0), v, mul(n, WT), friction=WALL_FRICTION)

def legs(sc, corners, z_top):
    for i, (x, y) in enumerate(corners):
        sc.box_b(f"Leg{i}", (x, y, (-1 + z_top) / 2), (0.1, 0.1, z_top + 1))

# ---------------------------------------------------------------------------- pieces
def names(tier):
    return ("Chute" if tier == "basic" else "ChuteAdv"), ("chute_" if tier == "basic" else "chute_adv_")

def h_straight(tier):
    N, P = names(tier)
    sc = Scene(f"{N}HStraight", f"res://game/assets/models/chutes/{P}h_straight.glb", IMPORT)
    sc.root("structure", f"blueprint_{P}h_straight", arrow=1)
    sc.model()
    sc.box_b("Floor", (0, 0, FLOOR_TOP - 0.03), (2 * IN, 2.0, 0.06), friction=floor_friction(tier))
    for side, tag in ((-1, "L"), (1, "R")):
        sc.box_b(f"Wall{tag}", (side * (IN + WT / 2), 0, (FLOOR_TOP - 0.14 + FLOOR_TOP + H_WALL) / 2), (WT, 2.0, H_WALL + 0.14), friction=WALL_FRICTION)
    return sc

def h_turn(tier, left):
    N, P = names(tier)
    side = "Left" if left else "Right"
    sc = Scene(f"{N}HTurn{side}", f"res://game/assets/models/chutes/{P}h_turn_{side.lower()}.glb", IMPORT)
    sc.root("structure", f"blueprint_{P}h_turn", arrow=2 if left else 3)
    sc.model()
    sc.box_b("Floor", (0, 0, FLOOR_TOP - 0.03), (1.98, 1.98, 0.06), friction=floor_friction(tier))
    y0, y1 = FLOOR_TOP - 0.14, FLOOR_TOP + H_WALL
    arc_walls(sc, "WallOuter", 1 + IN, 1 + IN + WT, y0, y1, 8, left)
    arc_walls(sc, "WallInner", 1 - IN - WT, 1 - IN, y0, y1, 2, left)
    return sc

def v_straight(tier):
    N, P = names(tier)
    sc = Scene(f"{N}VStraight", f"res://game/assets/models/chutes/{P}v_straight.glb", IMPORT)
    sc.root("structure", f"blueprint_{P}v_straight")
    sc.model()
    bore_walls(sc, -0.99, 0.99)
    return sc

def vturn_profile():
    """Outer-wall face of the elbow (build_chutes.vturn_path) as a belt-style profile: ((y, z), angle)."""
    R, zc = 0.9, 0.8
    s_v, s_a = 1.0 - zc, R * math.pi / 2
    def prof(s):
        if s <= s_v:
            ph, c = 0.0, (0.0, 1 - s)
        elif s <= s_v + s_a:
            ph = (s - s_v) / R
            c = (R - R * math.cos(ph), zc - R * math.sin(ph))
        else:
            ph = math.pi / 2
            c = (R + (s - s_v - s_a), zc - R)
        up = (math.cos(ph), math.sin(ph))
        return (c[0] - up[0] * IN, c[1] - up[1] * IN), ph - math.pi / 2
    return prof, s_v + s_a + (1.0 - R), s_v, s_a

def v_turn(tier):
    N, P = names(tier)
    sc = Scene(f"{N}VTurn", f"res://game/assets/models/chutes/{P}v_turn.glb", IMPORT)
    sc.root("structure", f"blueprint_{P}v_turn")
    sc.model()
    prof, L, s_v, s_a = vturn_profile()
    st = stations([(0, s_v, 1), (s_v, s_v + s_a, 8), (s_v + s_a, L, 1)])
    for i in range(len(st) - 1):
        seg_b(sc, f"Outer{i}", prof, st[i], st[i + 1], 0, 2 * IN + 2 * WT, -WT, 0, friction=floor_friction(tier), y_bounds=(-1, 1))
        seg_b(sc, f"Inner{i}", prof, st[i], st[i + 1], 0, 2 * IN + 2 * WT, 2 * IN, 2 * IN + WT, ref_top=False, friction=WALL_FRICTION, y_bounds=(-1, 1))
    for side, tag in ((-1, "L"), (1, "R")):                   # flat side plates covering the whole elbow
        sc.box_b(f"Side{tag}", (side * (IN + WT / 2), 0, 0), (WT, 1.98, 1.98), friction=WALL_FRICTION)
    return sc

def hopper_up(tier):
    N, P = names(tier)
    sc = Scene(f"{N}HopperUp", f"res://game/assets/models/chutes/{P}hopper_up.glb", IMPORT)
    sc.root("structure", f"blueprint_{P}hopper_up")
    sc.model()
    funnel_walls(sc, (-0.89, 0.89, -0.89, 0.89), (-IN, IN, -IN, IN), 0.92, -0.2)
    bore_walls(sc, -0.99, -0.2)
    return sc

def dropper_down(tier):
    N, P = names(tier)
    sc = Scene(f"{N}DropperDown", f"res://game/assets/models/chutes/{P}dropper_down.glb", IMPORT)
    sc.root("structure", f"blueprint_{P}dropper_down")
    sc.model()
    bore_walls(sc, -0.99, 0.99)
    hinge = IN - 0.03
    for tag, side in (("L", -1), ("R", 1)):
        sc.body(f"Door{tag}Body", b2g((side * hinge, 0, DOOR_Z)))
        sc.box("Leaf", (hinge - 0.01, 0.04, 2 * IN - 0.04), (-side * hinge / 2, -0.02, 0), parent=f"Door{tag}Body")
    lb = (IN + WT + 0.1, 0.0, 0.3)
    sc.body("LeverBody", b2g((lb[0] - 0.02, lb[1], lb[2] + 0.06)), size=(0.12, 0.52, 0.34), script_key="lever",
            extra=('handle = NodePath("../Model/Lever")', 'displayName = "Dropper Door"'), paths=("handle",))
    sc.sensor_b("Hold", (0, 0, (DOOR_Z + 0.99) / 2 + 0.02), (2 * IN - 0.05, 2 * IN - 0.05, 0.99 - DOOR_Z - 0.04))
    return sc

def hopper_2x2(tier):
    N, P = names(tier)
    sc = Scene(f"{N}Hopper2x2", f"res://game/assets/models/chutes/{P}hopper_2x2.glb", IMPORT)
    sc.root("structure", f"blueprint_{P}hopper_2x2",
            cells=[(x, y, z) for y in (0, 1) for z in (0, -1) for x in (0, 1)])
    sc.model()
    funnel_walls(sc, (-0.89, 2.89, -0.89, 2.89), (-IN, IN, -IN, IN), 2.92, 0.2)
    bore_walls(sc, -0.99, 0.2)
    legs(sc, [(-0.87, -0.87), (2.87, -0.87), (2.87, 2.87), (-0.87, 2.87)], 2.92)
    return sc

def hopper_3x3(tier):
    N, P = names(tier)
    sc = Scene(f"{N}Hopper3x3", f"res://game/assets/models/chutes/{P}hopper_3x3.glb", IMPORT)
    sc.root("structure", f"blueprint_{P}hopper_3x3",
            cells=[(x, y, z) for y in (0, 1) for z in (1, 0, -1) for x in (-1, 0, 1)])
    sc.model()
    funnel_walls(sc, (-2.89, 2.89, -2.89, 2.89), (-IN, IN, -IN, IN), 2.92, 0.4)
    bore_walls(sc, -0.99, 0.4)
    legs(sc, [(-2.87, -2.87), (2.87, -2.87), (2.87, 2.87), (-2.87, 2.87)], 2.92)
    return sc

def _family(tier):
    out = []
    folder = "game/scenes/structures/chutes/"
    for sc in (h_straight(tier), h_turn(tier, False), h_turn(tier, True), v_straight(tier), v_turn(tier),
               hopper_up(tier), dropper_down(tier), hopper_2x2(tier), hopper_3x3(tier)):
        out.append(sc.write(folder + sc.name + ".tscn"))
    return out

def basic():
    return _family("basic")

def advanced():
    return _family("adv")
