"""
Concept handheld tools, in the magnet tool's frame (build_props.build_magnet): origin at the grip, barrel toward
+Y (Godot -Z), about half a metre long. Each carries a looping "idle-loop" animation.

  tool_tether.glb           gravity tether gun: cable reel on top, three-prong emitter claw   Reel, Prongs
  tool_tag_painter.glb      tag spray gun: carousel of six tag canisters feeding a nozzle     Carousel
  tool_blueprint_stamp.glb  structure copier: projector casting a hologram of what it copied  Hologram
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

def grip(B, trigger=C_AMBER):
    """Pistol grip, trigger and a salvaged facility housing (as the magnet tool)."""
    B.box(Vector((0, -0.03, -0.075)), (0.045, 0.065, 0.13), TILT, "rubber", C_BLACK, 0.1)
    for k in range(4):
        B.box(Vector((0, 0.004, -0.03 - k * 0.028)), (0.047, 0.006, 0.012), TILT, "rubber", (0.06, 0.06, 0.06), 0.1)
    B.box(Vector((0, 0.03, -0.025)), (0.016, 0.018, 0.04), TILT, "panel", trigger, 0.1)
    B.box(Vector((0, 0.03, 0.02)), (0.075, 0.2, 0.075), I3, "panel", C_WHITE, 0.3)
    B.box(Vector((0, 0.03, -0.012)), (0.078, 0.17, 0.02), I3, "metal", C_DARK, 0.2)
    for k in range(4):
        B.box(Vector((0.039, -0.02 + k * 0.022, 0.03)), (0.004, 0.012, 0.035), I3, "metal", (0.05, 0.05, 0.05), 0.1)

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
    # reel housing on top, battery strapped under the barrel
    B.box(Vector((0, 0.0, 0.09)), (0.09, 0.1, 0.02), I3, "metal", C_DARK, 0.2)
    for sx in (-1, 1):
        B.box(Vector((sx * 0.048, -0.005, 0.125)), (0.008, 0.07, 0.07), I3, "panel", C_WHITE, 0.3)
    B.box(Vector((0, 0.22, -0.035)), (0.05, 0.12, 0.04), I3, "panel", C_WHITE, 0.3)
    B.box(Vector((0, 0.22, -0.035)), (0.056, 0.02, 0.046), I3, "tape", C_TAPE, 0.25)
    B.box(Vector((0.02, 0.26, -0.013)), (0.012, 0.012, 0.006), I3, "violet", C_VIOLET, 0.05)
    B.pipe([Vector((0, -0.01, 0.12)), Vector((0, 0.08, 0.1)), Vector((0, 0.16, 0.05))], 0.006, 5, "violet", C_VIOLET)   # tether line
    finish(B, "Body", coll)
    R = Builder()
    R.cyl(Vector((-0.035, 0, 0)), Vector((0.035, 0, 0)), 0.03, 16, "violet", C_VIOLET, 0.05)
    for sx in (-1, 1):
        R.cyl(Vector((sx * 0.035, 0, 0)), Vector((sx * 0.042, 0, 0)), 0.045, 16, "metal", C_DARK, 0.15)
    R.box(Vector((0.045, 0, 0.035)), (0.004, 0.015, 0.01), I3, "panel", C_YELLOW, 0.2)
    reel = node(R, "Reel", coll, Vector((0, -0.005, 0.125)))
    spin(reel, 0, 1, 60)
    P = Builder()
    for k in range(3):                                                        # three claw prongs round the emitter
        a = k / 3 * math.tau
        d = Vector((math.cos(a), 0, math.sin(a)))
        P.pipe([d * 0.035, d * 0.055 + Vector((0, 0.04, 0)), d * 0.04 + Vector((0, 0.08, 0))], 0.007, 5, "steel", C_STEEL)
    rock(P, Vector((0, 0.04, 0)), 0.018, 7, 1, 0.1, "violet", lambda q, n: C_VIOLET)
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
    nz = lambda y, r: [Vector((math.cos(a) * r, y, 0.02 + math.sin(a) * r)) for a in [k / 16 * math.tau for k in range(16)]]
    B.tube_rings([nz(0.34, 0.02), nz(0.4, 0.035), nz(0.43, 0.012)], "metal", C_DARK, 0.15, 0.0, cap=True)   # nozzle
    B.cyl(Vector((0, 0.43, 0.02)), Vector((0, 0.44, 0.02)), 0.006, 8, "glow", C_AMBER, 0.05)
    B.cyl(Vector((0, 0.15, 0.02)), Vector((0, 0.16, 0.02)), 0.085, 20, "metal", C_DARK, 0.15)              # carousel plates
    B.cyl(Vector((0, 0.285, 0.02)), Vector((0, 0.295, 0.02)), 0.085, 20, "metal", C_DARK, 0.15)
    B.box(Vector((0, 0.22, 0.115)), (0.03, 0.14, 0.02), I3, "panel", C_WHITE, 0.3)                          # selector window
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
    cone = lambda y, r: [Vector((math.cos(a) * r, y, 0.035 + math.sin(a) * r)) for a in [k / 20 * math.tau for k in range(20)]]
    B.tube_rings([cone(0.26, 0.035), cone(0.42, 0.11)], "field_zp", C_CYAN, 0.02, 0.0, cap=False, smooth=True)
    finish(B, "Body", coll)
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
