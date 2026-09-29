"""
Concept handheld tools, in the magnet tool's frame (build_props.build_magnet): origin at the grip, barrel toward
+Y (Godot -Z), about half a metre long. Each carries a looping "idle-loop" animation.

  tool_tether.glb           gravity tether gun: cable reel on top, three-prong emitter claw   Reel, Prongs
  tool_tag_painter.glb      tag spray gun: carousel of six tag canisters feeding a nozzle     Carousel
  tool_blueprint_stamp.glb  structure copier: projector casting a hologram of what it copied  Hologram, Beam
"""
import bpy, bmesh, math, random, os
from mathutils import Vector, Matrix

_HERE = (os.path.dirname(os.path.abspath(__file__)) if "__file__" in globals()
         else r"C:\Users\steph\OneDrive\Documents\godot\projects\gmp-framework\game\assets\models\tools\source")
_LIB = os.path.normpath(os.path.join(_HERE, "..", "..", "shared", "salvage_lib.py"))
_S = {"__name__": "salvage_lib", "__file__": _LIB}
exec(compile(open(_LIB, encoding="utf-8").read(), _LIB, "exec"), _S)
_SKIP = {"HERE", "OUT_DIR", "PIECES", "build_all", "ICONS", "export", "CONV"}
globals().update({k: v for k, v in _S.items() if not k.startswith("__") and k not in _SKIP})
HERE = _HERE
OUT_DIR = os.path.dirname(HERE)

TILT = Matrix.Rotation(math.radians(-14), 3, 'X')
C_BLUEPRINT = (0.12, 0.28, 0.62)

class Builder(Builder):
    """Hand-tool scale: a 3 mm chamfer on housings; parts under 2 cm and glowing bits stay sharp."""
    bevel = 0.003

    def box(self, center, size, basis=Matrix.Identity(3), mat="metal", c=C_FRAME, var=0.18, rust=0.0, bevel=None):
        if bevel is None and (mat in ("glow", "cyan", "violet", "glass") or max(size) < 0.02
                              or min(self.bevel, 0.2 * min(size)) < 0.0015):
            bevel = 0
        return super().box(center, size, basis, mat, c, var, rust, bevel)

def screw(B, p, n, r=0.0045, seg=6):
    """Tiny countersunk screw head on a housing face (a flat hexagon, 4 tris)."""
    n = Vector(n).normalized(); R = rot_to(n)
    vs = [B.bm.verts.new(p + n * 0.0008 + R @ Vector((math.cos(a) * r, math.sin(a) * r, 0))) for a in [k / seg * math.tau for k in range(seg)]]
    f = B.bm.faces.new(vs); f.material_index = MI["metal"]; f.normal_update()
    if f.normal.dot(n) < 0:
        f.normal_flip()
    B.paint([f], C_STEEL, 0.1)

def line(B, p0, p1, n, w=0.0025, col=(0.04, 0.04, 0.045)):
    """Dark seam line from p0 to p1 on a face with normal n."""
    p0, p1, n = Vector(p0), Vector(p1), Vector(n).normalized()
    side = (p1 - p0).cross(n).normalized() * w / 2
    o = n * 0.0008
    vs = [B.bm.verts.new(v + o) for v in (p0 - side, p1 - side, p1 + side, p0 + side)]
    f = B.bm.faces.new(vs); f.material_index = MI["metal"]; f.normal_update()
    if f.normal.dot(n) < 0:
        f.normal_flip()
    B.paint([f], col, 0.1)

def grip(B, trigger=C_AMBER):
    """Pistol grip, trigger and a salvaged facility housing (as the magnet tool)."""
    B.box(Vector((0, -0.03, -0.075)), (0.045, 0.065, 0.13), TILT, "rubber", C_BLACK, 0.1)
    for k in range(4):
        B.box(Vector((0, 0.004, -0.03 - k * 0.028)), (0.047, 0.006, 0.012), TILT, "rubber", (0.06, 0.06, 0.06), 0.1)
    B.box(Vector((0, 0.03, -0.025)), (0.016, 0.018, 0.04), TILT, "panel", trigger, 0.1)
    B.box(Vector((0, 0.03, 0.02)), (0.075, 0.2, 0.075), I3, "panel", C_WHITE, 0.3)
    B.box(Vector((0, 0.03, -0.012)), (0.078, 0.17, 0.02), I3, "metal", C_DARK, 0.2)
    B.box(Vector((0, -0.005, -0.03)), (0.05, 0.05, 0.012), I3, "metal", C_DARK, 0.2)                     # grip collar
    B.box(Vector((0, 0.03, -0.045)), (0.012, 0.06, 0.006), TILT, "metal", C_DARK, 0.2)                  # trigger guard
    for k in range(4):
        B.box(Vector((0.039, -0.02 + k * 0.022, 0.03)), (0.004, 0.012, 0.035), I3, "metal", (0.05, 0.05, 0.05), 0.1)
    for sx in (-1, 1):                                                        # housing screws, seams and a label
        for (y, z) in ((-0.06, 0.05), (0.12, 0.05), (-0.06, 0.0), (0.12, 0.0)):
            screw(B, Vector((sx * 0.0375, y, z)), (sx, 0, 0))
        line(B, Vector((sx * 0.0375, 0.07, -0.001)), Vector((sx * 0.0375, 0.07, 0.056)), (sx, 0, 0))
        line(B, Vector((sx * 0.0375, -0.068, 0.045)), Vector((sx * 0.0375, 0.128, 0.045)), (sx, 0, 0), 0.002)
    B.box(Vector((-0.0385, 0.1, 0.022)), (0.002, 0.04, 0.02), I3, "panel", C_YELLOW, 0.2)
    B.box(Vector((-0.0392, 0.1, 0.022)), (0.001, 0.028, 0.004), I3, "panel", C_BLACK, 0.1)

# ======================================================================================
def build_tether():
    random.seed(1101)
    coll = clear_collection("Tool_Tether")
    B = Builder()
    grip(B)
    ax = Vector((0, 1, 0))
    B.cyl(Vector((0, 0.12, 0.02)), Vector((0, 0.36, 0.02)), 0.026, 12, "metal", C_DARK, 0.15)             # barrel
    for y in (0.16, 0.22, 0.28):
        ring(B, Vector((0, y, 0.02)), ax, 0.026, 0.036, 0.012, 16, "violet", C_VIOLET, 0.05)
    B.cyl(Vector((0, 0.33, 0.02)), Vector((0, 0.36, 0.02)), 0.05, 16, "metal", C_DARK, 0.15)
    B.cyl(Vector((0, 0.126, 0.02)), Vector((0, 0.14, 0.02)), 0.034, 12, "metal", C_DARK, 0.15)            # barrel collar
    for k in range(6):                                                        # emitter flange screws
        a = (k + 0.5) / 6 * math.tau
        screw(B, Vector((math.cos(a) * 0.04, 0.3295, 0.02 + math.sin(a) * 0.04)), (0, -1, 0), 0.004)
    for k in range(4):                                                        # cooling fins between the coils
        a = k / 4 * math.tau + 0.785
        B.box(Vector((math.cos(a) * 0.03, 0.305, 0.02 + math.sin(a) * 0.03)), (0.012, 0.035, 0.003),
              Matrix.Rotation(-a, 3, 'Y'), "metal", C_DARK, 0.15)
    # reel housing on top, battery strapped under the barrel
    B.box(Vector((0, 0.0, 0.09)), (0.09, 0.1, 0.02), I3, "metal", C_DARK, 0.2)
    for sx in (-1, 1):
        B.box(Vector((sx * 0.048, -0.005, 0.125)), (0.008, 0.07, 0.07), I3, "panel", C_WHITE, 0.3)
    B.box(Vector((0, 0.22, -0.035)), (0.05, 0.12, 0.04), I3, "panel", C_WHITE, 0.3)
    B.box(Vector((0, 0.22, -0.035)), (0.056, 0.02, 0.046), I3, "tape", C_TAPE, 0.25)
    B.box(Vector((0.02, 0.26, -0.013)), (0.012, 0.012, 0.006), I3, "violet", C_VIOLET, 0.05)
    for y in (0.18, 0.26):                                                    # battery clamps round the barrel
        B.box(Vector((0, y, -0.0555)), (0.058, 0.008, 0.004), I3, "metal", C_DARK, 0.15)
        for sx in (-1, 1):
            B.box(Vector((sx * 0.0285, y, -0.03)), (0.003, 0.008, 0.05), I3, "metal", C_DARK, 0.15)
    for k in range(3):                                                        # charge pips
        B.box(Vector((-0.0255, 0.19 + k * 0.012, -0.03)), (0.002, 0.008, 0.008), I3, "violet" if k < 2 else "panel", C_VIOLET if k < 2 else C_DARK, 0.05)
    B.box(Vector((0, -0.005, 0.105)), (0.06, 0.06, 0.012), I3, "metal", C_DARK, 0.2)                        # reel bracket
    for sx in (-1, 1):
        screw(B, Vector((sx * 0.0525, -0.005, 0.125)), (sx, 0, 0), 0.005)
    B.pipe([Vector((0, -0.01, 0.12)), Vector((0, 0.08, 0.1)), Vector((0, 0.16, 0.05))], 0.006, 5, "violet", C_VIOLET)   # tether line
    finish(B, "Body", coll)
    R = Builder()
    R.cyl(Vector((-0.035, 0, 0)), Vector((0.035, 0, 0)), 0.03, 16, "violet", C_VIOLET, 0.05)
    for sx in (-1, 1):
        R.cyl(Vector((sx * 0.035, 0, 0)), Vector((sx * 0.042, 0, 0)), 0.045, 16, "metal", C_DARK, 0.15)
    R.box(Vector((0.045, 0, 0.035)), (0.004, 0.015, 0.01), I3, "panel", C_YELLOW, 0.2)
    for k in range(5):                                                        # wraps of tether line on the drum
        R.cyl(Vector((-0.028 + k * 0.014, 0, 0)), Vector((-0.02 + k * 0.014, 0, 0)), 0.034, 16, "violet", (0.5, 0.3, 0.85), 0.05)
    for sx in (-1, 1):
        for k in range(3):
            a = k / 3 * math.tau
            R.box(Vector((sx * 0.0425, math.cos(a) * 0.025, math.sin(a) * 0.025)), (0.002, 0.03, 0.006),
                  Matrix.Rotation(a + math.pi / 2, 3, 'X'), "metal", C_DARK, 0.15)
    reel = node(R, "Reel", coll, Vector((0, -0.005, 0.125)))
    spin(reel, 0, 1, 60)
    P = Builder()
    for k in range(3):                                                        # three claw prongs round the emitter
        a = k / 3 * math.tau
        d = Vector((math.cos(a), 0, math.sin(a)))
        P.pipe([d * 0.035, d * 0.055 + Vector((0, 0.04, 0)), d * 0.04 + Vector((0, 0.08, 0))], 0.007, 5, "steel", C_STEEL)
    rock(P, Vector((0, 0.04, 0)), 0.018, 7, 1, 0.1, "violet", lambda q, n: C_VIOLET)
    P.cyl(Vector((0, 0.0, 0)), Vector((0, 0.012, 0)), 0.042, 12, "steel", C_STEEL)                         # prong hub
    for k in range(3):
        a = k / 3 * math.tau
        d = Vector((math.cos(a), 0, math.sin(a)))
        P.box(d * 0.04 + Vector((0, 0.006, 0)), (0.014, 0.014, 0.014), I3, "steel", C_STEEL, 0.15)       # knuckles
    pr = node(P, "Prongs", coll, Vector((0, 0.36, 0.02)))
    spin(pr, 1, 1 / 3, 90)
    return coll

# ======================================================================================
TAG_COLOURS = [C_AMBER, C_CYAN, (1.0, 0.15, 0.1), C_COPPER, C_VIOLET, (0.9, 0.9, 0.88)]

def build_tag_painter():
    random.seed(1111)
    coll = clear_collection("Tool_TagPainter")
    B = Builder()
    grip(B, trigger=(1.0, 0.15, 0.1))
    B.cyl(Vector((0, 0.13, 0.02)), Vector((0, 0.34, 0.02)), 0.018, 10, "steel", C_STEEL)                   # spray line
    for y in (0.305, 0.325):                                                  # nozzle coupling
        B.cyl(Vector((0, y, 0.02)), Vector((0, y + 0.012, 0.02)), 0.024, 6, "metal", C_DARK, 0.15)
    B.pipe([Vector((0.02, 0.12, 0.05)), Vector((0.05, 0.2, 0.12)), Vector((0.03, 0.33, 0.04))], 0.005, 6, "rubber", C_BLACK)   # feed hose
    B.box(Vector((0, 0.22, 0.103)), (0.012, 0.18, 0.012), I3, "metal", C_DARK, 0.15)                     # carousel yoke
    for y in (0.14, 0.3):
        B.box(Vector((0, y, 0.07)), (0.012, 0.012, 0.07), I3, "metal", C_DARK, 0.15)
    nz = lambda y, r: [Vector((math.cos(a) * r, y, 0.02 + math.sin(a) * r)) for a in [k / 16 * math.tau for k in range(16)]]
    B.tube_rings([nz(0.34, 0.02), nz(0.4, 0.035), nz(0.43, 0.012)], "metal", C_DARK, 0.15, 0.0, cap=True)   # nozzle
    B.cyl(Vector((0, 0.43, 0.02)), Vector((0, 0.44, 0.02)), 0.006, 8, "glow", C_AMBER, 0.05)
    B.cyl(Vector((0, 0.15, 0.02)), Vector((0, 0.16, 0.02)), 0.085, 20, "metal", C_DARK, 0.15)              # carousel plates
    B.cyl(Vector((0, 0.285, 0.02)), Vector((0, 0.295, 0.02)), 0.085, 20, "metal", C_DARK, 0.15)
    B.box(Vector((0, 0.22, 0.117)), (0.03, 0.14, 0.02), I3, "panel", C_WHITE, 0.3)                          # selector window
    for y in (0.16, 0.28):
        screw(B, Vector((0, y, 0.127)), (0, 0, 1), 0.004)
    B.box(Vector((0, 0.22, 0.126)), (0.02, 0.04, 0.004), I3, "glow", C_AMBER, 0.05)
    for k, col in enumerate(TAG_COLOURS[:4]):                                  # paint splats on the housing
        B.box(Vector((0.0385, -0.04 + k * 0.03, 0.045 - (k % 2) * 0.02)), (0.002, 0.018, 0.012), I3, "panel", col, 0.2)
    finish(B, "Body", coll)
    C = Builder()
    for k, col in enumerate(TAG_COLOURS):
        a = k / 6 * math.tau
        d = Vector((math.cos(a), 0, math.sin(a))) * 0.058
        C.cyl(d + Vector((0, -0.055, 0)), d + Vector((0, 0.05, 0)), 0.02, 10, "panel", (0.75, 0.75, 0.72), 0.2)
        C.cyl(d + Vector((0, 0.05, 0)), d + Vector((0, 0.058, 0)), 0.021, 10, "glow" if k != 1 else "cyan", col, 0.05)
        C.box(d + Vector((0, 0, 0)), (0.022, 0.04, 0.022), Matrix.Rotation(-a, 3, 'Y'), "panel", col, 0.2)
        C.cyl(d + Vector((0, -0.062, 0)), d + Vector((0, -0.055, 0)), 0.012, 8, "panel", (0.45, 0.45, 0.44))          # valve cap
        for y in (-0.035, 0.035):                                             # can seams
            ring(C, d + Vector((0, y, 0)), (0, 1, 0), 0.02, 0.0215, 0.003, 10, "panel", (0.55, 0.55, 0.53), 0.1)
    car = node(C, "Carousel", coll, Vector((0, 0.222, 0.02)))
    step = math.tau / 6
    fr, ps = [], []                                                           # click round one canister at a time
    for k in range(6):
        fr += [1 + k * 20 + 14, 1 + (k + 1) * 20]
        ps += [(0, k * step, 0), (0, (k + 1) * step, 0)]
    keys(car, [1] + fr, "rotation_euler", [(0, 0, 0)] + ps)
    return coll

# ======================================================================================
def build_blueprint_stamp():
    random.seed(1121)
    coll = clear_collection("Tool_BlueprintStamp")
    B = Builder()
    grip(B, trigger=C_CYAN)
    B.box(Vector((0, 0.17, 0.035)), (0.1, 0.14, 0.1), I3, "panel", C_BLUEPRINT, 0.2)                      # projector body
    for k in range(4):                                                        # blueprint grid lines
        B.box(Vector((0.051, 0.12 + k * 0.03, 0.035)), (0.002, 0.004, 0.09), I3, "panel", (0.6, 0.75, 1.0), 0.1)
        B.box(Vector((0.051, 0.17, 0.0 + k * 0.025)), (0.002, 0.13, 0.004), I3, "panel", (0.6, 0.75, 1.0), 0.1)
    B.box(Vector((0, 0.245, 0.035)), (0.1, 0.012, 0.1), I3, "metal", C_DARK, 0.15)                          # lens plate
    for i in range(3):
        for j in range(2):
            p = Vector((-0.03 + i * 0.03, 0.252, 0.015 + j * 0.04))
            B.cyl(p, p + Vector((0, 0.008, 0)), 0.011, 10, "cyan", C_CYAN, 0.05)
    B.box(Vector((0, 0.13, 0.095)), (0.06, 0.07, 0.02), I3, "metal", (0.02, 0.03, 0.04), 0.05)             # little screen
    B.box(Vector((0, 0.13, 0.106)), (0.045, 0.05, 0.002), I3, "cyan", C_CYAN, 0.05)
    for sx in (-1, 1):                                                        # projector body screws and edge rails
        for (y, z) in ((0.105, -0.01), (0.235, -0.01), (0.105, 0.08), (0.235, 0.08)):
            screw(B, Vector((sx * 0.0505 if sx < 0 else 0.0525, y, z)), (sx, 0, 0), 0.004)
        B.box(Vector((sx * 0.049, 0.17, 0.086)), (0.006, 0.142, 0.006), I3, "metal", C_DARK, 0.15)
        B.box(Vector((sx * 0.049, 0.17, -0.016)), (0.006, 0.142, 0.006), I3, "metal", C_DARK, 0.15)
    for i in range(3):                                                        # lens rims
        for j in range(2):
            p = Vector((-0.03 + i * 0.03, 0.252, 0.015 + j * 0.04))
            ring(B, p + Vector((0, 0.004, 0)), (0, 1, 0), 0.011, 0.0135, 0.008, 10, "metal", C_DARK, 0.15)
    B.cyl(Vector((0.025, 0.09, 0.105)), Vector((0.025, 0.09, 0.15)), 0.003, 5, "metal", C_STEEL)          # antenna
    B.cyl(Vector((0.025, 0.09, 0.15)), Vector((0.025, 0.09, 0.156)), 0.005, 6, "cyan", C_CYAN, 0.05)
    finish(B, "Body", coll)
    Bm = Builder()                                                            # projection beam: a field shell, no shadow
    cone = lambda y, r: [Vector((math.cos(a) * r, y, 0.035 + math.sin(a) * r)) for a in [k / 20 * math.tau for k in range(20)]]
    Bm.tube_rings([cone(0.26, 0.035), cone(0.42, 0.11)], "field_zp", C_CYAN, 0.02, 0.0, cap=False, smooth=True)
    finish(Bm, "Beam", coll)
    H = Builder()
    e = 0.09                                                                 # a copied "structure": a wire cube round a tiny conveyor
    corners = [Vector((sx * e, sy * e, sz * e)) for sx in (-1, 1) for sy in (-1, 1) for sz in (-1, 1)]
    for i, a in enumerate(corners):
        for b in corners[i + 1:]:
            if sum(abs(a[k] - b[k]) > 1e-6 for k in range(3)) == 1:
                H.cyl(a, b, 0.004, 5, "cyan", C_CYAN, 0.05)
    H.box(Vector((0, 0, -0.06)), (0.1, 0.14, 0.02), I3, "glass", (0.55, 0.9, 1.0), 0.02)
    for sx in (-1, 1):
        H.box(Vector((sx * 0.055, 0, -0.045)), (0.01, 0.14, 0.03), I3, "glass", (0.55, 0.9, 1.0), 0.02)
    H.box(Vector((0, 0, 0.0)), (0.05, 0.05, 0.05), I3, "cyan", C_CYAN, 0.05)
    holo = node(H, "Hologram", coll, Vector((0, 0.5, 0.06)))
    spin(holo, 2, 1, 120)
    cycle(holo, "location", [Vector((0, 0.5, 0.06)), Vector((0, 0.5, 0.085))], 60)
    return coll

PIECES = [
    (build_tether, "tool_tether.glb"),
    (build_tag_painter, "tool_tag_painter.glb"),
    (build_blueprint_stamp, "tool_blueprint_stamp.glb"),
]
ICONS = []

def build_all(do_export=True):
    return build_family(PIECES, OUT_DIR, do_export)

if __name__ == "__main__":
    build_all(do_export=False)
