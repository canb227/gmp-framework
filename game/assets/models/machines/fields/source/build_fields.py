"""
Field projectors, 1x1x1, projecting out of the front face (+Y). Origin at the cell centre, floor z = -1.

  antigrav_projector.glb    salvaged facility cube with an emitter dish. Nodes: Frame, Rings (spin about Y),
                            Field: a 1.9 x 1.9 m violet volume from the front face (y = 1) one cell (2 m) long;
                            scale it along its local Y (Godot -Z) by the field length in cells.
  zeropoint_projector.glb   refurbished coil emitter. Nodes: Frame, Rings (spin about Y), Field: a cyan beam
                            tube (radius ZP_RADIUS) from the front face, 2 m long, scaled the same way; its
                            ForceField bands drift outward to show the slow push along the line.
Both have an Emitter marker at the centre of the front face.
"""
import bpy, bmesh, math, random, os
from mathutils import Vector, Matrix

_HERE = (os.path.dirname(os.path.abspath(__file__)) if "__file__" in globals()
         else r"C:\Users\steph\OneDrive\Documents\godot\projects\gmp-framework\game\assets\models\machines\fields\source")
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

AG_HALF = 0.95               # antigravity field half-size (across x and z)
ZP_RADIUS = 0.8

def housing(B, rng, tier, front=0.8):
    """Cube housing on a dark frame, panels on every side except the front (the block ends at y = front)."""
    B.box(Vector((0, (-0.9 + front) / 2, 0)), (1.8, front + 0.9, 1.8), I3, "metal", C_FRAME, 0.2, rust=0.3 if tier == "basic" else 0.02)
    for (x, z) in ((-0.93, -0.93), (0.93, -0.93), (-0.93, 0.93), (0.93, 0.93)):
        B.box(Vector((x, 0, z)), (0.12, 1.96, 0.12), I3, "metal", C_DARK, 0.2, rust=0.3 if tier == "basic" else 0.0)
    for (x, y) in ((-0.93, -0.93), (0.93, -0.93), (-0.93, 0.93), (0.93, 0.93)):
        B.box(Vector((x, y, 0)), (0.12, 0.12, 1.96), I3, "metal", C_DARK, 0.2, rust=0.3 if tier == "basic" else 0.0)
    for n in (Vector((1, 0, 0)), Vector((-1, 0, 0)), Vector((0, -1, 0)), Vector((0, 0, 1))):
        for k in range(2):
            for j in range(2):
                u = facing_basis(n) @ Vector((1, 0, 0)); v = facing_basis(n) @ Vector((0, 1, 0))
                c = n * 0.9 + u * (-0.43 + k * 0.86) + v * (-0.43 + j * 0.86)
                if tier == "basic":
                    r = rng.random()
                    if r < 0.12:
                        continue
                    panel_face(B, c, n, 0.8, 0.8, C_WHITE if r < 0.75 else C_DARK)
                    for du, dv in ((-0.36, 0.36), (0.36, -0.36)):              # two surviving rivets per panel
                        p = c + n * 0.018 + u * du + v * dv
                        stud(B, p, n, 0.012, 0.007, rust=0.5)
                        if dv > 0 and n.z == 0 and r < 0.5:
                            streak(B, p, n, 0.2)
                else:
                    panel_face(B, c, n, 0.82, 0.82, C_FACILITY)
                    for du in (-0.37, 0.37):                                     # panel fixings
                        stud(B, c + n * 0.018 + u * du + v * 0.37, n, 0.009, 0.005, "metal", (0.3, 0.3, 0.32))
    B.box(Vector((0, 0, -0.97)), (1.96, 1.96, 0.06), I3, "steel" if tier == "basic" else "metal", C_STEEL, 0.25,
          rust=0.6 if tier == "basic" else 0.05)                               # adv: no extra steel surface
    for (x, y) in ((-0.93, -0.93), (0.93, -0.93), (-0.93, 0.93), (0.93, 0.93)):   # anchor bolts at the posts
        for d in (Vector((0.1, 0, 0)), Vector((0, 0.1, 0))):
            stud(B, Vector((x, y, -0.94)) - Vector((d.x * (1 if x > 0 else -1), d.y * (1 if y > 0 else -1), 0)), ZV,
                 0.014, 0.01, "steel" if tier == "basic" else "metal", C_STEEL, rust=0.5 if tier == "basic" else 0.0)

def field_box(coll):
    F = Builder()
    F.box(Vector((0, 1.0, 0)), (2 * AG_HALF, 2.0, 2 * AG_HALF), I3, "field_ag", C_VIOLET, 0.02, bevel=0)   # scaled in Godot: keep it a plain box
    return node(F, "Field", coll, Vector((0, 1.0, 0)))          # local y 0..2: origin on the front face

def build_antigrav():
    rng = random.Random(601); random.seed(601)
    coll = clear_collection("Field_Antigrav")
    B = Builder()
    housing(B, rng, "basic")
    # front: emitter dish in a square bezel
    B.box(Vector((0, 0.9, 0)), (1.84, 0.08, 1.84), I3, "metal", C_DARK, 0.2, rust=0.3)
    for (c, s) in ((Vector((0, 0.95, 0.86)), (1.84, 0.06, 0.12)), (Vector((0, 0.95, -0.86)), (1.84, 0.06, 0.12)),
                   (Vector((0.86, 0.95, 0)), (0.12, 0.06, 1.84)), (Vector((-0.86, 0.95, 0)), (0.12, 0.06, 1.84))):
        B.box(c, s, I3, "panel", C_WHITE, 0.3)
    hazard(B, Vector((-0.8, 0.981, -0.92)), (1, 0, 0), (0, 0, 1), 1.6, 0.05, (0, 1, 0), pitch=0.08)
    for r0, r1, mat, col in ((0.72, 0.78, "metal", C_DARK), (0.6, 0.62, "violet", C_VIOLET), (0.44, 0.5, "metal", C_DARK),
                             (0.3, 0.32, "violet", C_VIOLET), (0.12, 0.2, "metal", C_DARK)):
        ring(B, Vector((0, 0.95, 0)), (0, 1, 0), r0, r1, 0.04, 40, mat, col, 0.1)
    B.cyl(Vector((0, 0.92, 0)), Vector((0, 0.97, 0)), 0.12, 20, "violet", C_VIOLET, 0.05)             # lens
    for k in range(6):                                                                          # spokes
        a = k / 6 * math.tau
        B.box(Vector((math.cos(a) * 0.46, 0.95, math.sin(a) * 0.46)), (0.62, 0.03, 0.03), Matrix.Rotation(-a, 3, 'Y'), "steel", C_STEEL, 0.15, rust=0.3)
    # top: cooling fins and a status box; back: power feed
    for k in range(7):
        B.box(Vector((0, -0.45 + k * 0.1, 0.97)), (1.2, 0.03, 0.06), I3, "metal", C_DARK, 0.15, rust=0.3)
    sb = Vector((0.55, -0.55, 0.94))
    B.box(sb, (0.3, 0.3, 0.1), I3, "panel", C_WHITE, 0.3)
    B.box(sb + Vector((0, 0, 0.052)), (0.1, 0.1, 0.004), I3, "violet", C_VIOLET, 0.05)
    B.box(Vector((0, -0.945, -0.3)), (0.5, 0.03, 0.4), I3, "metal", C_BLUE, 0.2, rust=0.2)
    for dx in (-0.1, 0.0, 0.1):
        B.pipe([Vector((dx, -0.96, -0.45)), Vector((dx * 1.5, -0.985, -0.8)), Vector((dx * 2, -0.9, -0.98))], 0.018, 6)
    B.box(Vector((-0.5, -0.955, 0.4)), (0.5, 0.012, 0.1), I3, "tape", C_TAPE, 0.25)
    finish(B, "Frame", coll)
    # gyroscope rings in front of the dish (spin about Y)
    R_ = Builder()
    for tilt, r in ((0.0, 0.7), (0.5, 0.64)):
        pts = [Matrix.Rotation(tilt, 3, 'Z') @ Vector((math.cos(a) * r, 0, math.sin(a) * r)) for a in [k / 40 * math.tau for k in range(40)]]
        R_.pipe(pts + [pts[0]], 0.018, 6, "steel", C_STEEL)
    for k in range(4):
        a = k / 4 * math.tau + 0.4
        R_.box(Vector((math.cos(a) * 0.7, 0, math.sin(a) * 0.7)), (0.07, 0.05, 0.07), I3, "violet", C_VIOLET, 0.05)
    node(R_, "Rings", coll, Vector((0, 0.9, 0)))
    field_box(coll)
    marker("Emitter", coll, Vector((0, 1.0, 0)))
    return coll

def build_zeropoint():
    rng = random.Random(611); random.seed(611)
    coll = clear_collection("Field_ZeroPoint")
    B = Builder()
    housing(B, rng, "adv", front=0.3)
    # front: coil stack in an open bezel around a bright core
    B.box(Vector((0, 0.32, 0)), (1.76, 0.04, 1.76), I3, "metal", C_DARK, 0.1)
    for (c, s) in ((Vector((0, 0.95, 0.86)), (1.84, 0.06, 0.12)), (Vector((0, 0.95, -0.86)), (1.84, 0.06, 0.12)),
                   (Vector((0.86, 0.95, 0)), (0.12, 0.06, 1.84)), (Vector((-0.86, 0.95, 0)), (0.12, 0.06, 1.84))):
        B.box(c, s, I3, "panel", C_FACILITY, 0.1)
    for (a, b) in (((-0.8, 0.8), (0.8, 0.8)), ((0.8, 0.8), (0.8, -0.8)), ((0.8, -0.8), (-0.8, -0.8)), ((-0.8, -0.8), (-0.8, 0.8))):
        B.cyl(Vector((a[0], 0.985, a[1])), Vector((b[0], 0.985, b[1])), 0.012, 6, "lamp", C_LAMP, 0.02)
    for k, y in enumerate((0.55, 0.7, 0.85)):
        ring(B, Vector((0, y, 0)), (0, 1, 0), 0.5 - k * 0.06, 0.66 - k * 0.06, 0.08, 40, "copper", C_COPPER, 0.15)
        ring(B, Vector((0, y + 0.05, 0)), (0, 1, 0), 0.48 - k * 0.06, 0.5 - k * 0.06, 0.02, 40, "cyan", C_CYAN, 0.05)
    B.cyl(Vector((0, 0.3, 0)), Vector((0, 0.95, 0)), 0.14, 20, "metal", C_DARK, 0.1)
    B.cyl(Vector((0, 0.95, 0)), Vector((0, 0.99, 0)), 0.1, 20, "cyan", C_CYAN, 0.05)
    # top: heat sink and two capacitor cans; sides: conduit
    for k in range(9):
        B.box(Vector((0, -0.7 + k * 0.13, 0.97)), (1.4, 0.03, 0.06), I3, "metal", C_DARK, 0.1)
    for sx_ in (-1, 1):
        c = Vector((sx_ * 0.6, -0.6, 0.9))
        B.cyl(c, c + Vector((0, 0, 0.1)), 0.12, 14, "panel", C_FACILITY, 0.1)
        ring(B, c + Vector((0, 0, 0.06)), (0, 0, 1), 0.12, 0.125, 0.02, 14, "cyan", C_CYAN, 0.05)
        B.pipe([Vector((sx_ * 0.985, -0.8, -0.8)), Vector((sx_ * 0.985, 0.0, -0.8)), Vector((sx_ * 0.985, 0.6, -0.4))], 0.02, 6, "metal", C_DARK)
    finish(B, "Frame", coll)
    R_ = Builder()
    for tilt, r in ((0.0, 0.76), (math.pi / 2, 0.72)):
        pts = [Matrix.Rotation(tilt, 3, 'Y') @ Vector((math.cos(a) * r, 0.0, math.sin(a) * r)) for a in [k / 40 * math.tau for k in range(40)]]
        pts = [Matrix.Rotation(0.35, 3, 'Z') @ p for p in pts]
        R_.pipe(pts + [pts[0]], 0.02, 6, "panel", C_FACILITY)
    for k in range(6):
        a = k / 6 * math.tau
        R_.box(Vector((math.cos(a) * 0.76, 0, math.sin(a) * 0.76)), (0.05, 0.04, 0.05), I3, "cyan", C_CYAN, 0.05)
    node(R_, "Rings", coll, Vector((0, 0.7, 0)))
    F = Builder()
    tube = lambda y, r: [Vector((math.cos(a) * r, y, math.sin(a) * r)) for a in [k / 32 * math.tau for k in range(32)]]
    F.tube_rings([tube(0.0, ZP_RADIUS), tube(2.0, ZP_RADIUS)], "field_zp", C_CYAN, 0.02, 0.0, cap=False, smooth=True)
    F.tube_rings([tube(0.0, ZP_RADIUS * 0.35), tube(2.0, ZP_RADIUS * 0.35)], "field_zp", C_CYAN, 0.02, 0.0, cap=False, smooth=True)
    node(F, "Field", coll, Vector((0, 1.0, 0)))
    marker("Emitter", coll, Vector((0, 1.0, 0)))
    return coll

PIECES = [
    (build_antigrav, "antigrav_projector.glb"),
    (build_zeropoint, "zeropoint_projector.glb"),
]
ICONS = [
    ("Field_Antigrav", "blueprints/blueprint_antigrav_projector.png", "blueprint", (1.2, 1.3, 0.8)),
    ("Field_ZeroPoint", "blueprints/blueprint_zeropoint_projector.png", "blueprint", (1.2, 1.3, 0.8)),
]

def build_all(do_export=True):
    return build_family(PIECES, OUT_DIR, do_export)

if __name__ == "__main__":
    build_all(do_export=False)
