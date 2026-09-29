"""
Sorting machines (front = +Y, origin at the anchor cell centre, floor z = -1):

  filter_basic.glb  1x1x2 (z -1..3). Bottom cell: a belt entering at the back; items that match the sample go
                    out through the open right side (+X) of the front half, the rest carry on out the front.
                    Top cell: an open basket players drop sample items into. Nodes: Belt, Frame, Pusher (the
                    paddle and its carriage; slide it along +X, rest x = 0, stroke PUSH_STROKE), plus markers
                    Basket (centre of the basket floor) and Scan (the scanner's eye).
  filter_arm.glb    1x1x2 (z -1..3). A pedestal in the lower half of the bottom cell carrying a scrappy grabber
                    arm, as a node chain: Turret (yaw about Z) > UpperArm (pitch about X) > Forearm (pitch about X)
                    > Head (pitch about X, carries the camera) > ClawL / ClawR (swing about Y to open / close),
                    with a Grip marker between the claws.
"""
import bpy, bmesh, math, random, os
from mathutils import Vector, Matrix

_HERE = (os.path.dirname(os.path.abspath(__file__)) if "__file__" in globals()
         else r"C:\Users\steph\OneDrive\Documents\godot\projects\gmp-framework\game\assets\models\machines\sorting\source")
_LIB = os.path.normpath(os.path.join(_HERE, "..", "..", "..", "shared", "salvage_lib.py"))
_S = {"__name__": "salvage_lib", "__file__": _LIB}
exec(compile(open(_LIB, encoding="utf-8").read(), _LIB, "exec"), _S)
_SKIP = {"HERE", "OUT_DIR", "PIECES", "build_all", "ICONS", "export", "CONV"}
globals().update({k: v for k, v in _S.items() if not k.startswith("__") and k not in _SKIP})
HERE = _HERE
OUT_DIR = os.path.dirname(HERE)

# ---------- lean rules for these many-times-placed pieces ----------
SHARP_BELOW = 0.035                            # boxes thinner than this stay sharp: a < 7 mm chamfer only costs tris
FOLD = {"tape": "rubber", "wood": "rubber"}    # look-alike surfaces share one material (one draw call fewer)

class Builder(Builder):
    def box(self, center, size, basis=I3, mat="metal", c=C_FRAME, var=0.18, rust=0.0, bevel=None):
        if bevel is None and min(size) < SHARP_BELOW:
            bevel = 0
        return super().box(center, size, basis, mat, c, var, rust, bevel)
    def to_object(self, name, coll, material_keys=None):
        fold = {MI[a]: MI[b] for a, b in FOLD.items()}
        for f in self.bm.faces:
            f.material_index = fold.get(f.material_index, f.material_index)
        return super().to_object(name, coll, material_keys)

def stud(B, p, n, r=0.011, h=0.01, mat="steel", c=C_STEEL, rust=0.3):
    """Bolt head standing on a surface along n: six sides and a top, no hidden bottom (16 tris)."""
    n = Vector(n).normalized()
    u = n.cross(ZV if abs(n.z) < 0.9 else Vector((1, 0, 0))).normalized(); w = n.cross(u)
    rim = [(u * math.cos(k / 6 * math.tau) + w * math.sin(k / 6 * math.tau)) * r for k in range(6)]
    v0 = [B.bm.verts.new(p + d) for d in rim]; v1 = [B.bm.verts.new(p + d + n * h) for d in rim]
    fs = [B.quad([v0[k], v0[(k + 1) % 6], v1[(k + 1) % 6], v1[k]], mat) for k in range(6)] + [B.quad(v1, mat)]
    B.paint(fs, c, 0.15, rust)
    return fs

def decal(B, pts, n, mat="panel", c=C_DARK, var=0.1):
    """Flat painted shape (one n-gon) facing n, e.g. a flow arrow on a guard."""
    f = B.quad([B.bm.verts.new(Vector(p)) for p in pts], mat)
    if f.normal.dot(Vector(n)) < 0:
        f.normal_flip()
    B.paint([f], c, var)
    return f

def streak(B, p, n, length=0.14, w=0.011, c=C_RUST):
    """Rust run washed down a surface (facing n) from a bolt at p: one tapering painted quad."""
    n = Vector(n).normalized(); dn = -ZV + n * n.z
    if dn.length < 1e-3:
        return
    dn.normalize(); u = n.cross(dn)
    p = p + n * 0.0015
    return decal(B, [p + u * w, p + dn * length + u * w * 0.2, p + dn * length - u * w * 0.2, p - u * w], n, "steel", c, 0.3)

def louvre(B, c, n, w, h, slats=4, col=C_DARK):
    """Vent on a face centred at c facing n: dark recess with angled slats (sharp boxes, cheap)."""
    R = facing_basis(n)
    B.box(c, (w, h, 0.01), R, "metal", (0.02, 0.02, 0.02), 0.05)
    for k in range(slats):
        y = -h / 2 + h * (k + 0.5) / slats
        B.box(c + R @ Vector((0, y, 0.008)), (w - 0.02, 0.012, 0.012), R @ Matrix.Rotation(-0.5, 3, 'X'), "metal", col, 0.1)

EXPORT_KEEP.update({"Pusher"})                          # slides in the scene: keep it its own node

PUSH_STROKE = 1.45           # pusher travel toward +X
PUSHER_REST_X = -0.78
DECK_Z = 1.0                 # basket floor level (bottom of the top cell)

# ======================================================================================
def build_filter():
    rng = random.Random(501); random.seed(501)
    coll = clear_collection("Filter_Basic")
    full = PATHS["straight"]
    back, front = sub_path(full, 0.0, 1.0), sub_path(full, 1.0, 2.0)
    build_belt(full, "Belt", coll, 2.0)
    B = Builder()
    build_side(B, full, -1, rng, panel_len=(0.5, 0.9))                 # left guarded all along (pusher side)
    build_side(B, back, 1, rng, panel_len=(0.45, 0.55))                # right guarded on the back half only
    build_side(B, front, 1, rng, guards=False, hubs=False)
    B.box(Vector((0.93, -0.03, BELT_TOP + 0.05)), (0.07, 0.07, 0.42), I3, "steel", C_STEEL, 0.2, rust=0.5)
    B.box(Vector((0.93, -0.03, BELT_TOP + 0.2)), (0.075, 0.075, 0.06), I3, "panel", C_YELLOW, 0.2)
    build_cable(B, full)
    # corner posts carrying the machine deck and the basket
    for (x, y) in ((-0.94, -0.94), (0.94, -0.94), (-0.94, 0.94), (0.94, 0.94)):
        z0 = BELT_TOP + 0.02 if abs(x) > 0.9 else FLOOR
        B.box(Vector((x, y, (z0 + 2.2) / 2)), (0.08, 0.08, 2.2 - z0), I3, "metal", C_FRAME, 0.2, rust=0.35)
    # machine housing between the cells (clear of 1 m items on the belt)
    B.box(Vector((0, 0, 0.72)), (1.84, 1.84, 0.5), I3, "metal", C_FRAME, 0.2, rust=0.3)
    for n in (Vector((0, 1, 0)), Vector((0, -1, 0)), Vector((1, 0, 0)), Vector((-1, 0, 0))):
        panel_face(B, n * 0.925 + Vector((0, 0, 0.72)), n, 1.6, 0.42, C_WHITE)
        u = facing_basis(n) @ Vector((1, 0, 0))
        for du in (-0.76, 0.76):                                                # panel screws
            for dz in (-0.17, 0.13):
                stud(B, n * 0.943 + u * du + Vector((0, 0, 0.72 + dz)), n, 0.009, 0.005, rust=0.2)
    for n in (Vector((0, -1, 0)), Vector((-1, 0, 0))):                           # cooling vents (away from the display)
        u = facing_basis(n) @ Vector((1, 0, 0))
        louvre(B, n * 0.943 + u * 0.45 + Vector((0, 0, 0.7)), n, 0.4, 0.2, 4)
    # front display: what the filter is holding
    B.box(Vector((0.3, 0.945, 0.72)), (0.6, 0.012, 0.28), I3, "metal", (0.02, 0.03, 0.03), 0.05)
    for k in range(3):
        B.box(Vector((0.14 + k * 0.12, 0.952, 0.76)), (0.08, 0.004, 0.08), I3, "glow", C_AMBER, 0.05)
    B.box(Vector((0.3, 0.952, 0.64)), (0.4, 0.004, 0.02), I3, "cyan", C_CYAN, 0.05)
    hazard(B, Vector((-0.92, 0.936, 0.47)), (1, 0, 0), (0, 0, 1), 1.84, 0.05, (0, 1, 0), pitch=0.08)
    # right-exit arrow plate and hazard band over the side exit
    B.box(Vector((0.925, 0.5, 0.52)), (0.02, 0.9, 0.06), I3, "panel", C_YELLOW, 0.2)
    # pusher rail under the housing, across the front half
    B.box(Vector((0, 0.5, 0.42)), (1.84, 0.12, 0.06), I3, "steel", C_STEEL, 0.2, rust=0.4)
    B.box(Vector((-0.8, 0.5, 0.33)), (0.2, 0.3, 0.12), I3, "metal", C_BLUE, 0.25, rust=0.15)                  # actuator
    B.pipe([Vector((-0.8, 0.35, 0.36)), Vector((-0.6, 0.1, 0.45)), Vector((-0.3, -0.2, 0.46))], 0.014, 5)
    # scanner arch over the back half: a salvaged security camera eye and a light curtain
    for sx_ in (-1, 1):
        B.box(Vector((sx_ * 0.9, -0.55, 0.0)), (0.08, 0.1, 0.9), I3, "metal", C_DARK, 0.2)
        B.box(Vector((sx_ * 0.855, -0.55, -0.15)), (0.01, 0.02, 1.1), I3, "cyan", C_CYAN, 0.05)
    B.box(Vector((0, -0.55, 0.43)), (1.84, 0.12, 0.06), I3, "metal", C_DARK, 0.2)
    cam = Vector((0, -0.62, 0.36))
    B.box(cam, (0.22, 0.18, 0.16), I3, "panel", C_WHITE, 0.3)
    B.cyl(cam + Vector((0, 0.0, -0.08)), cam + Vector((0, 0.0, -0.1)), 0.05, 12, "metal", C_DARK, 0.1)
    B.cyl(cam + Vector((0, 0.0, -0.1)), cam + Vector((0, 0.0, -0.105)), 0.03, 12, "cyan", C_CYAN, 0.05)
    B.box(cam + Vector((0, 0.07, 0.06)), (0.08, 0.04, 0.06), I3, "steel", C_STEEL, 0.2, rust=0.4)      # clamp to the arch
    B.pipe([cam + Vector((0.11, 0.02, 0.02)), Vector((0.4, -0.58, 0.45)), Vector((0.86, -0.56, 0.45))], 0.01, 5, "metal", C_BLACK)
    # deck + basket: open-top salvaged crate on the housing, wire-grille walls, scan tray floor
    B.box(Vector((0, 0, DECK_Z)), (1.9, 1.9, 0.06), I3, "metal", C_DARK, 0.2, rust=0.3)
    B.box(Vector((0, 0, DECK_Z + 0.035)), (1.5, 1.5, 0.01), I3, "steel", C_WEAR, 0.15, rust=0.2)
    for k in range(4):                                                           # tray screws
        a = k / 4 * math.tau + math.pi / 4
        stud(B, Vector((math.cos(a) * 0.98, math.sin(a) * 0.98, DECK_Z + 0.04)), ZV, 0.012, 0.006, rust=0.3)
    for (a, b) in (((-0.74, -0.74), (0.74, -0.74)), ((0.74, -0.74), (0.74, 0.74)), ((0.74, 0.74), (-0.74, 0.74)), ((-0.74, 0.74), (-0.74, -0.74))):
        B.cyl(Vector((a[0], a[1], DECK_Z + 0.045)), Vector((b[0], b[1], DECK_Z + 0.045)), 0.012, 6, "cyan", C_CYAN, 0.05)
    for n, u in ((Vector((1, 0, 0)), Vector((0, 1, 0))), (Vector((-1, 0, 0)), Vector((0, 1, 0))),
                 (Vector((0, 1, 0)), Vector((1, 0, 0))), (Vector((0, -1, 0)), Vector((1, 0, 0)))):
        base = n * 0.9
        B.box(base + Vector((0, 0, DECK_Z + 0.2)), (0.04 if n.x else 1.84, 0.04 if n.y else 1.84, 0.3), I3, "panel", C_WHITE, 0.3)
        for k in range(9):                                                            # grille bars
            o = -0.8 + k * 0.2
            B.box(base + u * o + Vector((0, 0, DECK_Z + 0.75)), (0.03, 0.03, 0.8), I3, "steel", C_STEEL, 0.15, rust=0.45)
        B.box(base + Vector((0, 0, DECK_Z + 1.17)), (0.07 if n.x else 1.9, 0.07 if n.y else 1.9, 0.06), I3, "metal", C_DARK, 0.2, rust=0.3)
    hazard(B, Vector((-0.92, 0.922, DECK_Z + 0.07)), (1, 0, 0), (0, 0, 1), 1.84, 0.06, (0, 1, 0), pitch=0.08)
    B.box(Vector((0.5, -0.922, DECK_Z + 0.25)), (0.5, 0.012, 0.12), I3, "tape", C_TAPE, 0.25)
    finish(B, "Frame", coll)
    marker("Basket", coll, Vector((0, 0, DECK_Z + 0.04)))
    marker("Scan", coll, cam + Vector((0, 0, -0.105)))
    # pusher: paddle hanging from its carriage (origin at the carriage, on the rail)
    Pu = Builder()
    Pu.box(Vector((0, 0, 0)), (0.2, 0.3, 0.1), I3, "metal", C_BLUE, 0.2, rust=0.15)
    Pu.box(Vector((0.02, 0, -0.35)), (0.05, 0.06, 0.6), I3, "steel", C_STEEL, 0.2, rust=0.3)
    Pu.box(Vector((0.06, 0, -0.9)), (0.05, 0.86, 0.6), I3, "panel", C_WHITE, 0.3)
    Pu.box(Vector((0.09, 0, -0.9)), (0.02, 0.9, 0.62), I3, "rubber", C_BLACK, 0.1)
    hazard(Pu, Vector((0.101, -0.43, -0.7)), (0, 1, 0), (0, 0, 1), 0.86, 0.08, (1, 0, 0), pitch=0.08)
    node(Pu, "Pusher", coll, Vector((PUSHER_REST_X, 0.5, 0.36)))
    return coll

# ======================================================================================
ARM_BASE_TOP = 0.0           # top of the pedestal (half the bottom cell)
UPPER_LEN, FORE_LEN = 1.2, 1.0
REST = dict(upper=math.radians(-12.0), fore=math.radians(70.0), head=math.radians(60.0), claw=math.radians(18.0))

def beam(B, l, w=0.12, col=(0.3, 0.3, 0.3), panel=True):
    """Box beam along local +Z from 0 to l, salvaged: rusty box section with a facility panel riveted on."""
    B.box(Vector((0, 0, l / 2)), (w, w, l), I3, "steel", col, 0.25, rust=0.5)
    if panel:
        B.box(Vector((w / 2 + 0.008, 0, l / 2)), (0.016, w * 0.9, l * 0.7), I3, "panel", C_WHITE, 0.3)
        for z in (l * 0.2, l * 0.5, l * 0.8):
            for dy in (-w * 0.3, w * 0.3):
                stud(B, Vector((w / 2 + 0.016, dy, z)), (1, 0, 0), 0.01, 0.007, rust=0.4)

def joint(B, r=0.1, w=0.26, col=C_DARK):
    B.cyl(Vector((-w / 2, 0, 0)), Vector((w / 2, 0, 0)), r, 14, "metal", col, 0.2, rust=0.3)
    for sx_ in (-1, 1):
        B.cyl(Vector((sx_ * w / 2, 0, 0)), Vector((sx_ * (w / 2 + 0.015), 0, 0)), r * 0.6, 10, "steel", C_STEEL, rust=0.3)

def build_arm():
    random.seed(511)
    coll = clear_collection("Filter_Arm")
    B = Builder()
    # pedestal: lower half of the bottom cell
    B.box(Vector((0, 0, -0.97)), (1.9, 1.9, 0.06), I3, "steel", C_STEEL, 0.25, rust=0.7)
    B.box(Vector((0, 0, -0.5)), (1.2, 1.2, 0.88), I3, "metal", C_FRAME, 0.2, rust=0.3)
    for n in (Vector((0, 1, 0)), Vector((0, -1, 0)), Vector((1, 0, 0)), Vector((-1, 0, 0))):
        panel_face(B, n * 0.6 + Vector((0, 0, -0.52)), n, 1.0, 0.7, C_WHITE)
    for (x, y) in ((-0.85, -0.85), (0.85, -0.85), (-0.85, 0.85), (0.85, 0.85)):
        B.box(Vector((x, y, -0.9)), (0.16, 0.16, 0.08), I3, "steel", C_STEEL, 0.2, rust=0.5)
        stud(B, Vector((x, y, -0.86)), ZV, 0.03, 0.03, rust=0.4)
        B.cyl(Vector((x * 0.8, y * 0.8, -0.9)), Vector((x * 0.55, y * 0.55, -0.3)), 0.025, 6, "steel", C_STEEL, rust=0.5)
    ring(B, Vector((0, 0, -0.04)), (0, 0, 1), 0.45, 0.58, 0.08, 32, "metal", C_DARK, 0.2, rust=0.3)
    for n in (Vector((1, 0, 0)), Vector((-1, 0, 0))):                            # motor vents on the pedestal flanks
        louvre(B, n * 0.623 + Vector((0, 0.2, -0.45)), n, 0.36, 0.2, 4)
    hazard(B, Vector((-0.6, 0.601, -0.94)), (1, 0, 0), (0, 0, 1), 1.2, 0.06, (0, 1, 0), pitch=0.08)
    B.box(Vector((0.35, 0.61, -0.3)), (0.3, 0.012, 0.18), I3, "metal", (0.02, 0.03, 0.03), 0.05)
    B.box(Vector((0.3, 0.618, -0.28)), (0.05, 0.004, 0.03), I3, "glow", C_AMBER, 0.05)
    B.box(Vector((0.4, 0.618, -0.28)), (0.05, 0.004, 0.03), I3, "cyan", C_CYAN, 0.05)
    B.pipe([Vector((-0.6, -0.3, -0.7)), Vector((-0.9, -0.5, -0.95)), Vector((-0.97, 0.2, -0.96))], 0.02, 6)
    finish(B, "Frame", coll)
    # turret: turntable and shoulder yoke
    T = Builder()
    T.cyl(Vector((0, 0, 0.0)), Vector((0, 0, 0.08)), 0.44, 28, "metal", C_DARK, 0.2, rust=0.3)
    T.cyl(Vector((0, 0, 0.08)), Vector((0, 0, 0.1)), 0.4, 28, "panel", C_WHITE, 0.3)
    for k in range(12):                                                          # slewing-ring bolts
        a = (k + 0.5) / 12 * math.tau
        stud(T, Vector((math.cos(a) * 0.42, math.sin(a) * 0.42, 0.08)), ZV, 0.012, 0.01, rust=0.3)
    for sx_ in (-1, 1):
        T.box(Vector((sx_ * 0.2, 0, 0.28)), (0.06, 0.3, 0.4), I3, "steel", (0.3, 0.3, 0.3), 0.25, rust=0.5)
    T.box(Vector((0, -0.28, 0.25)), (0.32, 0.2, 0.3), I3, "metal", C_BLUE, 0.25, rust=0.15)                   # shoulder motor
    T.box(Vector((0, -0.385, 0.3)), (0.2, 0.01, 0.12), I3, "panel", C_WHITE, 0.3)
    T.box(Vector((0.05, -0.392, 0.33)), (0.03, 0.004, 0.02), I3, "glow", C_AMBER, 0.05)
    turret = node(T, "Turret", coll, Vector((0, 0, ARM_BASE_TOP)))
    # upper arm: pivot on the yoke, built along +Z
    U = Builder()
    joint(U, 0.11, 0.34)
    beam(U, UPPER_LEN, 0.16)
    U.pipe([Vector((0.08, 0.06, 0.1)), Vector((0.12, 0.08, UPPER_LEN * 0.5)), Vector((0.08, 0.06, UPPER_LEN - 0.1))], 0.012, 5)
    U.box(Vector((0, 0.07, UPPER_LEN * 0.5)), (0.14, 0.02, 0.18), I3, "tape", C_TAPE, 0.25)
    upper = node(U, "UpperArm", coll, Vector((0, 0, 0.28)), parent=turret)
    upper.rotation_euler = (REST["upper"], 0, 0)
    # forearm
    F = Builder()
    joint(F, 0.09, 0.26)
    beam(F, FORE_LEN, 0.13, (0.28, 0.28, 0.29), panel=True)
    F.box(Vector((0, -0.08, 0.2)), (0.12, 0.08, 0.18), I3, "metal", C_BLUE, 0.25, rust=0.15)                    # elbow servo
    fore = node(F, "Forearm", coll, Vector((0, 0, UPPER_LEN)), parent=upper)
    fore.rotation_euler = (REST["fore"], 0, 0)
    # head: wrist joint, repurposed security camera, claw mount (built along +Z)
    H = Builder()
    joint(H, 0.07, 0.2)
    H.box(Vector((0, 0, 0.14)), (0.2, 0.16, 0.16), I3, "metal", C_DARK, 0.2, rust=0.3)
    cam = Vector((0, -0.14, 0.16))
    H.box(cam, (0.16, 0.14, 0.13), I3, "panel", C_WHITE, 0.3)
    H.cyl(cam + Vector((0, -0.07, 0)), cam + Vector((0, -0.1, 0)), 0.045, 12, "metal", C_DARK, 0.1)
    H.cyl(cam + Vector((0, -0.1, 0)), cam + Vector((0, -0.105, 0)), 0.028, 12, "cyan", C_CYAN, 0.05)
    H.box(cam + Vector((0.06, -0.071, 0.045)), (0.02, 0.004, 0.012), I3, "glow", (1.0, 0.15, 0.1), 0.05)
    H.box(Vector((0, 0, 0.24)), (0.3, 0.1, 0.04), I3, "steel", C_STEEL, 0.2, rust=0.4)
    head = node(H, "Head", coll, Vector((0, 0, FORE_LEN)), parent=fore)
    head.rotation_euler = (REST["head"], 0, 0)
    for name, sx_ in (("ClawL", -1), ("ClawR", 1)):
        C = Builder()
        C.cyl(Vector((0, -0.04, 0)), Vector((0, 0.04, 0)), 0.025, 8, "steel", C_STEEL, rust=0.3)
        C.box(Vector((0, 0, 0.12)), (0.025, 0.07, 0.24), I3, "steel", (0.3, 0.3, 0.3), 0.25, rust=0.5)
        C.box(Vector((-sx_ * 0.03, 0, 0.26)), (0.07, 0.07, 0.04), Matrix.Rotation(sx_ * 0.6, 3, 'Y'), "steel", (0.3, 0.3, 0.3), 0.25, rust=0.5)
        C.box(Vector((-sx_ * 0.02, 0, 0.2)), (0.012, 0.06, 0.12), I3, "rubber", C_BLACK, 0.1)
        claw = node(C, name, coll, Vector((sx_ * 0.13, 0, 0.26)), parent=head)
        claw.rotation_euler = (0, sx_ * REST["claw"], 0)
    marker("Grip", coll, Vector((0, 0, 0.45)), parent=head)
    return coll

PIECES = [
    (build_filter, "filter_basic.glb"),
    (build_arm, "filter_arm.glb"),
]
ICONS = [
    ("Filter_Basic", "blueprints/blueprint_filter_basic.png", "blueprint", (1.2, 1.3, 0.8)),
    ("Filter_Arm", "blueprints/blueprint_filter_arm.png", "blueprint", (1.3, 1.0, 0.5)),
]

def build_all(do_export=True):
    return build_family(PIECES, OUT_DIR, do_export)

if __name__ == "__main__":
    build_all(do_export=False)
