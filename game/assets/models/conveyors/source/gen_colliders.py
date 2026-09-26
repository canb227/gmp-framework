"""
Writes the salvage conveyor structure scenes (game/machines/structures/conveyors/salvage/*.tscn) with Box3D
colliders that follow the models built by build_conveyors.py. Plain Python: python gen_colliders.py

Godot frame: root at the anchor cell's centre, cell floor y = -1, items flow toward -Z. Every piece has:
  - a top belt surface (BELT_TOP) moving items forward, and a return-run surface underneath (RET_Y, facing
    down) moving them backward, for when gravity flips;
  - side walls (stringer + guard panels) and, under the belt, the guide lips either side of the return run.
Nothing leaves the cell. Keep the constants in step with build_conveyors.py.
"""
import math, os

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.normpath(os.path.join(HERE, "../../../../machines/structures/conveyors/salvage"))
MODELS = "res://game/assets/models/conveyors/"

BELT_TOP = -0.85
BELT_H = 0.085                 # top run to return run
RET_Y = BELT_TOP - BELT_H      # return run surface (faces down)
BELT_W = 0.84                  # belt half-width
TOP_T = 0.04                   # top belt collider thickness
RET_T = 0.03                   # return belt collider thickness
WALL_X = (0.84, 0.95)          # stringer + guard panels
WALL_O = (-0.15, 0.25)         # relative to the belt top
LIP_X = (0.812, 0.842)
LIP_O = (-0.15, -0.095)
SPEED = 2.0
SLOPE_SPEED = 3.0              # along the incline (the placeholder's 3.5 horizontal push was ~3.1 along it); bends blend from SPEED
BELT_FRICTION = 0.8
SLOPE_FRICTION = 1.2           # grippier incline belt: a 30 deg climb needs more than the flats' 0.8 (cubes default to 0.6)
WALL_FRICTION = 0.1
# Impact-sound surfaces (ItemTags values): the belt is the structure's RUBBER tag; frame shapes are tagged METAL.
TAG_METAL = 3
TAG_RUBBER = 5

SLOPE_R, SLOPE_TH = 1.0, math.radians(30.0)
SLOPE_LS = (4.0 - 2 * SLOPE_R * math.sin(SLOPE_TH)) / math.cos(SLOPE_TH)
SLOPE_L = 2 * SLOPE_R * SLOPE_TH + SLOPE_LS

def f(x):
    s = f"{x:.5f}".rstrip("0").rstrip(".")
    return "0" if s in ("-0", "") else s

def v3(v): return f"Vector3({f(v[0])}, {f(v[1])}, {f(v[2])})"

def uid_of(glb):
    txt = open(os.path.join(HERE, "..", glb + ".import"), encoding="utf-8").read()
    return txt.split('uid="', 1)[1].split('"', 1)[0]

# ---------- scene text helpers ----------
class Scene:
    def __init__(self, glb):
        self.head = ['[gd_scene format=3]', '',
                     '[ext_resource type="Script" uid="uid://4e0tnsui2eu8" path="res://game/machines/Structure.cs" id="1_r"]',
                     f'[ext_resource type="PackedScene" uid="{uid_of(glb)}" path="{MODELS}{glb}" id="2_model"]', '']
        self.nodes = []
    def node(self, header, *props):
        self.nodes += [header] + [p for p in props if p] + [""]
    def box(self, name, parent, size, pos, rot_x=0.0, rot_y=0.0, tangent=None, friction=None, material=None):
        self.node(f'[node name="{name}" type="Box3DCollisionShape" parent="{parent}"]',
                  f"box_size = {v3(size)}",
                  f"friction = {f(friction)}" if friction is not None else None,
                  f"user_material_id = {material}" if material else None,
                  f"tangent_velocity = {v3(tangent)}" if tangent else None,
                  f"position = {v3(pos)}",
                  f"rotation = {v3((rot_x, rot_y, 0))}" if (rot_x or rot_y) else None)
    def write(self, name):
        open(os.path.join(OUT, name + ".tscn"), "w", newline="\n").write("\n".join(self.head + self.nodes).rstrip() + "\n")

def root(sc, name, blueprint, arrow, extra=()):
    sc.node(f'[node name="{name}" type="Box3DBody"]', "body_type = 0", *extra, 'script = ExtResource("1_r")',
            f'blueprintItemID = "{blueprint}"', f"flowArrow = {arrow}", f"tags = Array[int]([{TAG_RUBBER}])")

# ---------- straight ----------
def straight():
    sc = Scene("conveyor_straight.glb")
    root(sc, "Conveyor", "blueprint_conveyor", 1, ["box_size = Vector3(0, 0, 0)"])
    sc.node('[node name="Model" parent="." instance=ExtResource("2_model")]')
    sc.box("BeltTop", ".", (2 * BELT_W, TOP_T, 2), (0, BELT_TOP - TOP_T / 2, 0), tangent=(0, 0, -SPEED), friction=BELT_FRICTION)
    sc.box("BeltReturn", ".", (2 * LIP_X[0], RET_T, 2), (0, RET_Y + RET_T / 2, 0), tangent=(0, 0, SPEED), friction=BELT_FRICTION)
    for side, tag in ((-1, "L"), (1, "R")):
        sc.box(f"Wall{tag}", ".", (WALL_X[1] - WALL_X[0], WALL_O[1] - WALL_O[0], 2),
               (side * sum(WALL_X) / 2, BELT_TOP + sum(WALL_O) / 2, 0), friction=WALL_FRICTION, material=TAG_METAL)
        sc.box(f"Lip{tag}", ".", (LIP_X[1] - LIP_X[0], LIP_O[1] - LIP_O[0], 2),
               (side * sum(LIP_X) / 2, BELT_TOP + sum(LIP_O) / 2, 0), friction=WALL_FRICTION, material=TAG_METAL)
    sc.write("Conveyor")

# ---------- slope ----------
def slope_point(s):
    """Belt-top profile, Godot (z, y) and the incline angle, s along the belt from the low (back) end."""
    R, th, Ls = SLOPE_R, SLOPE_TH, SLOPE_LS
    a1 = R * th
    if s <= a1:
        ph = s / R; y, z = -1 + R * math.sin(ph), BELT_TOP + R * (1 - math.cos(ph))
    elif s <= a1 + Ls:
        ph = th; d = s - a1
        y = -1 + R * math.sin(th) + d * math.cos(th); z = BELT_TOP + R * (1 - math.cos(th)) + d * math.sin(th)
    else:
        ph = (2 * a1 + Ls - s) / R; y, z = 3 - R * math.sin(ph), BELT_TOP + 2 - R * (1 - math.cos(ph))
    return (-y, z), ph          # Blender y -> Godot -z, Blender z -> Godot y

def slope_stations(per_bend):
    """per_bend segments round each bend, one for the straight incline between them."""
    a1 = SLOPE_R * SLOPE_TH
    return [a1 * i / per_bend for i in range(per_bend + 1)] + [a1 + SLOPE_LS + a1 * i / per_bend for i in range(per_bend + 1)]

def offset(pt, ph, o):
    (z, y) = pt                   # normal (up the belt) in Godot (z, y): flow is (-cos, sin) -> normal (sin, cos)
    return (z + o * math.sin(ph), y + o * math.cos(ph))

def seg_box(sc, name, parent, p0, p1, x, width, o0, o1, ph0, ph1, tangent=None, friction=None, xf=None, ref_top=True, material=None):
    """Box spanning offsets o0..o1 (along the normal) between stations p0 -> p1, across x +- width/2. Its ref_top
    (else bottom) face runs exactly through the offset profile points, so consecutive segments' working faces meet."""
    a0, a1_ = offset(p0, ph0, o0), offset(p1, ph1, o0)
    b0, b1 = offset(p0, ph0, o1), offset(p1, ph1, o1)
    ref0, ref1 = (b0, b1) if ref_top else (a0, a1_)
    dz, dy = ref1[0] - ref0[0], ref1[1] - ref0[1]
    ln = math.hypot(dz, dy)
    rx = math.atan2(dy, -dz)      # rotation about X that turns local -Z onto the chord
    nz, ny = math.sin(rx), math.cos(rx)
    thick = o1 - o0
    cz = (ref0[0] + ref1[0]) / 2 - (nz * thick / 2 if ref0 is b0 else -nz * thick / 2)
    cy = (ref0[1] + ref1[1]) / 2 - (ny * thick / 2 if ref0 is b0 else -ny * thick / 2)
    pos, ry = (x, cy, cz), 0.0
    if xf:                        # down slope: turned half round about the footprint centre (z = -1)
        pos, ry = (-x, cy, -2 - cz), math.pi
    sc.box(name, parent, (width, thick, ln), pos, rx, ry, tangent, friction, material)

def slope(down):
    name = "ConveyorSlopeDown" if down else "ConveyorSlope"
    sc = Scene("conveyor_slope.glb")
    root(sc, name, "blueprint_conveyor_slope", 4 if down else 1, [
        "box_size = Vector3(0, 0, 0)",
        "cellOffsets = Array[Vector3i]([Vector3i(0, 0, 0), Vector3i(0, 0, -1), Vector3i(0, 1, 0), Vector3i(0, 1, -1)])"])
    sc.node('[node name="Model" parent="." instance=ExtResource("2_model")]',
            "transform = Transform3D(-1, 0, 0, 0, 1, 0, 0, 0, -1, 0, 0, -2)" if down else None)
    if down:
        sc.node('[node name="Belt" parent="Model" index="0"]', f"instance_shader_parameters/belt_speed = {f(-SPEED)}")
    st = slope_stations(6)
    pts = [slope_point(s) for s in st]
    for i in range(len(st) - 1):
        (p0, ph0), (p1, ph1) = pts[i], pts[i + 1]
        mid = (ph0 + ph1) / 2
        v = SPEED if down else SPEED + (SLOPE_SPEED - SPEED) * mid / SLOPE_TH
        # Box3D shapes have no frame of their own (their geometry is baked into the body's), so a tangent
        # velocity is in the structure's frame: point it along this segment's chord, uphill for the up slope,
        # downhill for the down slope (whose segments are the up slope's turned half round).
        dz, dy = p1[0] - p0[0], p1[1] - p0[1]
        ln = math.hypot(dz, dy)
        uz, uy = dz / ln, dy / ln                       # uphill, up-slope frame
        top_t = (0, -v * uy, v * uz) if down else (0, v * uy, v * uz)
        ret_t = tuple(-c for c in top_t)
        seg_box(sc, f"BeltTop{i}", ".", p0, p1, 0, 2 * BELT_W, -TOP_T, 0, ph0, ph1, top_t, SLOPE_FRICTION, down)
        seg_box(sc, f"BeltReturn{i}", ".", p0, p1, 0, 2 * LIP_X[0], -BELT_H, -BELT_H + RET_T, ph0, ph1,
                ret_t, SLOPE_FRICTION, down, ref_top=False)
    wst = slope_stations(3)
    wpts = [slope_point(s) for s in wst]
    for i in range(len(wst) - 1):
        (p0, ph0), (p1, ph1) = wpts[i], wpts[i + 1]
        for side, tag in ((-1, "L"), (1, "R")):
            seg_box(sc, f"Wall{tag}{i}", ".", p0, p1, side * sum(WALL_X) / 2, WALL_X[1] - WALL_X[0], WALL_O[0], WALL_O[1], ph0, ph1,
                    friction=WALL_FRICTION, xf=down, material=TAG_METAL)
            seg_box(sc, f"Lip{tag}{i}", ".", p0, p1, side * sum(LIP_X) / 2, LIP_X[1] - LIP_X[0], LIP_O[0], LIP_O[1], ph0, ph1,
                    friction=WALL_FRICTION, xf=down, material=TAG_METAL)
    # scaffold legs outside the stringers (as build_conveyors.py places them)
    for side in (-1, 1):
        for k, s in enumerate((2.15, SLOPE_L - 0.35)):
            pt, ph = slope_point(s)
            z, y = offset(pt, ph, WALL_O[0] + 0.02)
            h = y + 1.0
            pos = (side * 0.972, -1.0 + h / 2, z)
            if down: pos = (-pos[0], pos[1], -2 - pos[2])
            sc.box(f"Leg{'LR'[side > 0]}{k}", ".", (0.05, h, 0.05), pos, material=TAG_METAL)
    sc.write(name)

# ---------- turns ----------
def turn(left):
    name = "ConveyorTurnLeft" if left else "ConveyorTurnRight"
    mx = -1 if left else 1                     # left turn = right turn mirrored across x
    N = 16
    r_in, r_out = 1 - BELT_W, 1 + BELT_W
    verts, idx, mats, surf = [], [], [], []
    def P(r, th, y):                           # right turn: pivot (1, 1), enters on z = +1, leaves on x = +1
        return (mx * (1 - r * math.cos(th)), y, 1 - r * math.sin(th))
    for layer, (y, sign) in enumerate(((BELT_TOP, 1), (RET_Y, -1))):
        for i in range(N):
            t0, t1 = math.pi / 2 * i / N, math.pi / 2 * (i + 1) / N
            tm = (t0 + t1) / 2
            b = len(verts)
            verts += [P(r_in, t0, y), P(r_out, t0, y), P(r_out, t1, y), P(r_in, t1, y)]
            quad = [(0, 1, 2), (0, 2, 3)]
            tris = []
            for tri in quad:
                a, c, d = (verts[b + j] for j in tri)
                n_y = (c[2] - a[2]) * (d[0] - a[0]) - (c[0] - a[0]) * (d[2] - a[2])   # ((c-a) x (d-a)).y
                want_up = sign > 0
                tris.append(tri if (n_y > 0) == want_up else (tri[0], tri[2], tri[1]))
            for tri in tris:
                idx += [b + j for j in tri]
                mats.append(layer * N + i)
            vx, vz = SPEED * math.sin(tm), -SPEED * math.cos(tm)                     # along the arc, forward
            surf.append({"custom_color": 0, "friction": 1, "restitution": 0.0, "rolling_resistance": 0.0,
                         "tangent_velocity": (mx * vx * sign, 0, vz * sign), "user_material_id": 0})
    # materials must be listed top 0..N-1 then return N..2N-1, matching mats
    sc = Scene(f"conveyor_turn_{'left' if left else 'right'}.glb")
    sm = ", ".join('{"custom_color": 0, "friction": %s, "restitution": 0.0, "rolling_resistance": 0.0, "tangent_velocity": %s, "user_material_id": 0}'
                   % (f(BELT_FRICTION), v3(m["tangent_velocity"])) for m in surf)
    root(sc, name, "blueprint_conveyor_turn", 2 if left else 3, [
        "shape_type = 6",
        "mesh_vertices = PackedVector3Array(" + ", ".join(f"{f(a)}, {f(b)}, {f(c)}" for a, b, c in verts) + ")",
        "mesh_indices = PackedInt32Array(" + ", ".join(str(i) for i in idx) + ")",
        "mesh_materials = PackedByteArray(" + ", ".join(str(m) for m in mats) + ")",
        f"surface_materials = Array[Dictionary]([{sm}])"])
    sc.node('[node name="Model" parent="." instance=ExtResource("2_model")]')
    sc.node('[node name="Rails" type="Box3DBody" parent="."]', "body_type = 0", "box_size = Vector3(0, 0, 0)")
    def arc(prefix, r0, r1, y0, y1, n):
        rc = (r0 + r1) / 2
        for i in range(n):
            tc = math.pi / 2 * (i + 0.5) / n
            ln = 2 * r1 * math.sin(math.pi / 4 / n) + 0.02
            x, y, z = P(rc, tc, (y0 + y1) / 2)
            sc.box(f"{prefix}{i}", "Rails", (r1 - r0, y1 - y0, ln), (x, y, z), 0, mx * (math.pi - tc) if mx > 0 else -(math.pi - tc),
                   friction=WALL_FRICTION, material=TAG_METAL)
    arc("WallOuter", 1 + WALL_X[0], 1 + WALL_X[1], BELT_TOP + WALL_O[0], BELT_TOP + WALL_O[1], 8)
    arc("LipOuter", 1 + LIP_X[0], 1 + LIP_X[1], BELT_TOP + LIP_O[0], BELT_TOP + LIP_O[1], 8)
    arc("LipInner", 1 - LIP_X[1], 1 - LIP_X[0], BELT_TOP + LIP_O[0], BELT_TOP + LIP_O[1], 3)
    sc.box("PivotPost", "Rails", (0.12, WALL_O[1] - WALL_O[0], 0.12), (mx * 0.94, BELT_TOP + sum(WALL_O) / 2, 0.94), friction=WALL_FRICTION, material=TAG_METAL)
    sc.write(name)

if __name__ == "__main__":
    straight(); slope(False); slope(True); turn(False); turn(True)
    print("wrote", sorted(os.listdir(OUT)))
