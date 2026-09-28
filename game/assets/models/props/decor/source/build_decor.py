"""
Decorative props for the facility's lower level: the vast, dilapidated, overgrown and crumbling laboratories and
factories under the test facility. Non-functional; some carry a looping "idle-loop" animation.

Frame: Blender Z up, fronts (screens, doors, open sides) face -Y (Godot +Z), origin on the FLOOR at the prop's footprint centre (z = 0 is the ground; unlike
the grid structures, whose origin is a cell centre). The decor scenes (tools/decor/gen_decor.py) give the solid
ones a bounding-box collider.

Ruin props (a few metres):
  cracked_pillar, collapsed_pillar, rubble_pile, moss_mound, overgrown_tree, fern_cluster, hanging_vines*,
  collapsed_panel_wall, broken_catwalk, cable_drapes, lab_bench, flicker_terminal*, monitor_bank*,
  filing_cabinets, cryo_pod*, observation_booth, pipe_cluster*, hanging_lamp*, ceiling_fan*, rusted_barrels,
  crate_stack, puddle_debris, tipped_barriers, elevator_ruin
Superstructures (tens of metres; placed far from the main area):
  cooling_tower*, gantry_crane*, reactor_sphere*, arcology_spire, sky_bridge, panel_arm_wall*
(* animated)
"""
import bpy, bmesh, math, random, os
from mathutils import Vector, Matrix

_HERE = (os.path.dirname(os.path.abspath(__file__)) if "__file__" in globals()
         else r"C:\\Users\\steph\\OneDrive\\Documents\\godot\\projects\\gmp-framework\\game\\assets\\models\\props\\decor\\source")
_LIB = os.path.normpath(os.path.join(_HERE, "..", "..", "..", "shared", "salvage_lib.py"))
_S = {"__name__": "salvage_lib", "__file__": _LIB}
exec(compile(open(_LIB, encoding="utf-8").read(), _LIB, "exec"), _S)
_SKIP = {"HERE", "OUT_DIR", "PIECES", "build_all", "ICONS", "export", "CONV"}
globals().update({k: v for k, v in _S.items() if not k.startswith("__") and k not in _SKIP})
HERE = (os.path.dirname(os.path.abspath(__file__)) if "__file__" in globals() else _HERE)
OUT_DIR = os.path.dirname(HERE)

C_CONC = (0.55, 0.54, 0.5)
C_CONC_D = (0.33, 0.32, 0.3)
C_RUST = (0.38, 0.18, 0.07)
C_MOSS = (0.17, 0.3, 0.08)
C_LEAF = (0.2, 0.42, 0.12)
C_LEAF2 = (0.3, 0.5, 0.16)
C_BARK = (0.26, 0.18, 0.1)
C_PAPER = (0.82, 0.8, 0.72)
C_WATER = (0.1, 0.14, 0.13)

def jit(c, k=0.15):
    f = 1 + random.uniform(-k, k)
    return tuple(min(1.0, x * f) for x in c)

def concrete(p, n):
    return jit(C_MOSS if n.z > 0.6 and random.random() < 0.35 else random.choice([C_CONC, C_CONC, C_CONC_D]), 0.12)

def chunk(B, c, r, seed, squash=(1, 1, 0.8)):
    rock(B, c, r, seed, 1, 0.35, "rock", concrete, squash)

def moss(B, c, r, seed):
    rock(B, c, r, seed, 2, 0.25, "rubber", lambda p, n: jit(random.choice([C_MOSS, C_LEAF, (0.22, 0.34, 0.1)]), 0.15), (1, 1, 0.25))

def leaves(B, c, r, seed):
    rock(B, c, r, seed, 1, 0.45, "rubber", lambda p, n: jit(random.choice([C_LEAF, C_LEAF2, C_MOSS]), 0.15), (1, 1, 0.8))

def rebar(B, base, d, length):
    d = Vector(d).normalized()
    mid = base + d * length * 0.6
    B.pipe([base, mid, mid + (d + Vector((random.uniform(-.6, .6), random.uniform(-.6, .6), 0))).normalized() * length * 0.4],
           0.018, 5, "steel", C_RUST)

def vine(B, top, length, seed):
    rng = random.Random(seed)
    pts = [top + Vector((math.sin(k * 0.9 + seed) * 0.06, math.cos(k * 0.7 + seed) * 0.06, -k * length / 10)) for k in range(11)]
    B.pipe(pts, 0.025, 5, "rubber", (0.22, 0.28, 0.1))
    for k in range(2, 11):
        p = pts[k]
        a = rng.uniform(0, math.tau)
        B.box(p + Vector((math.cos(a) * 0.07, math.sin(a) * 0.07, 0)), (0.14, 0.02, 0.1), Matrix.Rotation(a, 3, 'Z') @ Matrix.Rotation(0.4, 3, 'X'),
              "rubber", jit(rng.choice([C_LEAF, C_LEAF2]), 0.1), 0.1)

def fern(B, c, seed, size=1.0):
    rng = random.Random(seed)
    for k in range(7):
        a = k / 7 * math.tau + rng.uniform(-0.2, 0.2)
        d = Vector((math.cos(a), math.sin(a), 0))
        tip = c + d * 0.7 * size + Vector((0, 0, 0.35 * size))
        B.pipe([c, c + d * 0.35 * size + Vector((0, 0, 0.45 * size)), tip], 0.015, 4, "rubber", C_LEAF)
        for t in (0.3, 0.5, 0.7, 0.9):
            p = c.lerp(tip, t) + Vector((0, 0, 0.15 * size * (1 - abs(t - 0.5) * 2)))
            B.box(p, (0.05 * size, 0.28 * size * (1.1 - t), 0.01), Matrix.Rotation(a, 3, 'Z'), "rubber", jit(C_LEAF2, 0.1), 0.1)

def new(name):
    random.seed(sum(map(ord, name)))
    return clear_collection(f"Decor_{name}")

# ======================================================================================== ruin props
def build_cracked_pillar():
    coll = new("CrackedPillar"); B = Builder()
    h = 5.2
    B.box(Vector((0, 0, h / 2)), (1.0, 1.0, h), I3, "rock", C_CONC, 0.15, rust=0.1)
    for k in range(6):                                                       # jagged broken top
        chunk(B, Vector((random.uniform(-0.3, 0.3), random.uniform(-0.3, 0.3), h + random.uniform(0.0, 0.3))), random.uniform(0.2, 0.35), 3 + k)
    for (x, y) in ((-0.35, -0.35), (0.35, -0.35), (0.35, 0.35), (-0.35, 0.35)):
        rebar(B, Vector((x, y, h - 0.1)), (x * 0.5, y * 0.5, 1), 0.9)
    for k in range(3):                                                       # chunks knocked off the faces
        B.box(Vector((0.51, random.uniform(-0.3, 0.3), random.uniform(1, 4))), (0.04, 0.3, 0.4), I3, "rock", C_CONC_D, 0.1)
    B.box(Vector((0, 0, 1.0)), (1.02, 1.02, 0.25), I3, "panel", C_YELLOW, 0.3, rust=0.5)                   # faded hazard band
    B.box(Vector((0, 0, 0.1)), (1.4, 1.4, 0.2), I3, "rock", C_CONC_D, 0.15)
    for k in range(4):
        moss(B, Vector((random.uniform(-0.8, 0.8), random.uniform(-0.8, 0.8), 0.1)), 0.4, 20 + k)
    for k in range(4):
        vine(B, Vector((0.52 * random.choice((-1, 1)), random.uniform(-0.4, 0.4), h - 0.2)), random.uniform(2.0, 3.5), k)
    finish(B, "Mesh", coll); return coll

def build_collapsed_pillar():
    coll = new("CollapsedPillar"); B = Builder()
    tilt = Matrix.Rotation(math.radians(88), 3, 'Y') @ Matrix.Rotation(0.2, 3, 'X')
    B.box(Vector((-1.4, 0, 0.55)), (1.0, 1.0, 2.6), tilt, "rock", C_CONC, 0.15)
    B.box(Vector((1.5, 0.3, 0.5)), (1.0, 1.0, 2.2), Matrix.Rotation(math.radians(80), 3, 'Y') @ Matrix.Rotation(0.5, 3, 'Z'), "rock", C_CONC, 0.15)
    for k in range(9):
        chunk(B, Vector((random.uniform(-0.6, 0.6), random.uniform(-1, 1), 0.15)), random.uniform(0.15, 0.35), 40 + k)
    for k in range(5):
        rebar(B, Vector((random.uniform(-0.2, 0.3), random.uniform(-0.4, 0.4), 0.6)), (random.uniform(-1, 1), random.uniform(-1, 1), 0.6), 0.8)
    for k in range(3):
        moss(B, Vector((random.uniform(-2, 2), random.uniform(-0.6, 0.6), 0.9)), 0.4, 50 + k)
    fern(B, Vector((0.2, -1.1, 0.0)), 7, 0.8)
    finish(B, "Mesh", coll); return coll

def build_rubble_pile():
    coll = new("RubblePile"); B = Builder()
    for k in range(22):
        a = random.uniform(0, math.tau); r = random.uniform(0, 1.6)
        z = max(0.1, 0.9 - r * 0.5)
        chunk(B, Vector((math.cos(a) * r, math.sin(a) * r, z * random.uniform(0.4, 1.0))), random.uniform(0.2, 0.45), 60 + k)
    for k in range(4):                                                       # broken white wall panels in the pile
        a = random.uniform(0, math.tau)
        B.box(Vector((math.cos(a) * 1.0, math.sin(a) * 1.0, 0.5)), (1.2, 0.08, 0.8), Matrix.Rotation(a, 3, 'Z') @ Matrix.Rotation(random.uniform(-0.6, 0.6), 3, 'X'),
              "panel", C_WHITE, 0.25, rust=0.3)
    for k in range(6):
        rebar(B, Vector((random.uniform(-1, 1), random.uniform(-1, 1), 0.6)), (random.uniform(-1, 1), random.uniform(-1, 1), 1), 1.0)
    moss(B, Vector((0.6, -0.5, 0.7)), 0.5, 70)
    fern(B, Vector((-1.3, 0.8, 0.1)), 71, 0.7)
    finish(B, "Mesh", coll); return coll

def build_moss_mound():
    coll = new("MossMound"); B = Builder()
    rock(B, Vector((0, 0, 0.3)), 1.4, 81.0, 2, 0.3, "rubber", lambda p, n: jit(random.choice([C_MOSS, C_LEAF, (0.24, 0.36, 0.12)]), 0.15), (1.2, 1.0, 0.45))
    for k in range(3):
        chunk(B, Vector((random.uniform(-1, 1), random.uniform(-0.8, 0.8), 0.7)), 0.3, 82 + k)
    for k in range(4):
        fern(B, Vector((random.uniform(-1.2, 1.2), random.uniform(-1, 1), 0.5)), 85 + k, 0.6)
    B.box(Vector((0.9, 0.2, 0.7)), (0.1, 0.8, 0.5), Matrix.Rotation(0.5, 3, 'Y'), "metal", C_RUST, 0.2, rust=0.8)   # machine corner poking out
    finish(B, "Mesh", coll); return coll

def build_overgrown_tree():
    coll = new("OvergrownTree"); B = Builder()
    rings_ = []
    for k in range(9):
        z = k * 0.9; r = 0.45 * (1 - k / 10) + 0.08
        c = Vector((math.sin(k * 0.5) * 0.15, math.cos(k * 0.4) * 0.1, z))
        rings_.append([c + Vector((math.cos(a) * r, math.sin(a) * r, 0)) for a in [i / 10 * math.tau for i in range(10)]])
    B.tube_rings(rings_, "rock", C_BARK, 0.2, 0.0, cap=True, smooth=True)
    for k in range(6):                                                       # branches and canopy
        a = k / 6 * math.tau + random.uniform(-0.3, 0.3); z = random.uniform(4.0, 6.5)
        base = Vector((0, 0, z)); tip = base + Vector((math.cos(a) * 1.8, math.sin(a) * 1.8, random.uniform(0.3, 1.2)))
        B.pipe([base, (base + tip) / 2 + Vector((0, 0, 0.3)), tip], 0.08, 6, "rock", C_BARK)
        leaves(B, tip, random.uniform(0.9, 1.3), 90 + k)
    leaves(B, Vector((0, 0, 7.8)), 1.6, 99)
    for k in range(6):                                                       # roots through cracked floor tiles
        a = k / 6 * math.tau
        B.pipe([Vector((0, 0, 0.4)), Vector((math.cos(a) * 0.8, math.sin(a) * 0.8, 0.15)), Vector((math.cos(a) * 1.6, math.sin(a) * 1.6, 0.02))],
               0.1, 6, "rock", C_BARK)
        B.box(Vector((math.cos(a + 0.5) * 1.3, math.sin(a + 0.5) * 1.3, 0.12)), (0.9, 0.9, 0.12), Matrix.Rotation(a, 3, 'Z') @ Matrix.Rotation(random.uniform(0.15, 0.4), 3, 'X'),
              "rock", C_CONC, 0.15)
    finish(B, "Mesh", coll); return coll

def build_fern_cluster():
    coll = new("FernCluster"); B = Builder()
    for k in range(6):
        fern(B, Vector((random.uniform(-1, 1), random.uniform(-1, 1), 0)), 100 + k, random.uniform(0.7, 1.2))
    for k in range(3):
        chunk(B, Vector((random.uniform(-1, 1), random.uniform(-1, 1), 0.1)), 0.2, 110 + k)
    finish(B, "Mesh", coll); return coll

def build_hanging_vines():
    coll = new("HangingVines"); B = Builder()
    B.box(Vector((0, 0, 6.0)), (4.0, 0.3, 0.3), I3, "metal", C_RUST, 0.2, rust=0.7)                        # overhead beam
    finish(B, "Beam", coll)
    for g in range(3):                                                       # three curtains swaying out of step
        V = Builder()
        for k in range(5):
            vine(V, Vector((-0.6 + k * 0.3, 0, 0)), random.uniform(2.5, 4.8), g * 10 + k)
        leaves(V, Vector((0, 0, 0.1)), 0.35, 120 + g)
        ob = node(V, f"Vines{g}", coll, Vector((-1.3 + g * 1.3, 0, 5.85)))
        cycle(ob, "rotation_euler", [(0.05 + 0.02 * g, 0, 0), (-0.06, 0, 0.03)], 90 + g * 30)
    return coll

def build_collapsed_panel_wall():
    coll = new("CollapsedPanelWall"); B = Builder()
    for x in (-3.0, -1.0, 1.0, 3.0):                                         # exposed framework
        B.box(Vector((x, 0, 2.5)), (0.12, 0.2, 5.0), I3, "metal", C_DARK, 0.2, rust=0.5)
    B.box(Vector((0, 0, 4.9)), (6.3, 0.2, 0.15), I3, "metal", C_DARK, 0.2, rust=0.5)
    for i in range(3):
        for j in range(5):
            if (i, j) in ((1, 3), (1, 4), (2, 4), (0, 2)):
                continue                                                     # fallen panels leave holes
            panel_face(B, Vector((-2.0 + i * 2.0, -0.11, 0.5 + j * 1.0)), (0, -1, 0), 1.9, 0.95, jit(C_WHITE, 0.08))
    for k, (x, z, a) in enumerate(((0.2, 0.3, 1.2), (1.4, 0.2, -0.9), (2.3, 0.4, 0.4))):                   # the fallen ones
        B.box(Vector((x, -1.0 - k * 0.4, z)), (1.9, 0.06, 0.95), Matrix.Rotation(a, 3, 'Z') @ Matrix.Rotation(1.35, 3, 'X'), "panel", C_WHITE, 0.2, rust=0.3)
    for k in range(5):
        vine(B, Vector((random.uniform(-3, 3), -0.2, 4.8)), random.uniform(2, 4), 130 + k)
    for k in range(3):
        moss(B, Vector((random.uniform(-3, 3), -0.4, 0.05)), 0.6, 140 + k)
    finish(B, "Mesh", coll); return coll

def build_broken_catwalk():
    coll = new("BrokenCatwalk"); B = Builder()
    z = 3.0
    for x in (-3.5, -0.5):
        for y in (-0.6, 0.6):
            B.box(Vector((x, y, z / 2)), (0.12, 0.12, z), I3, "metal", C_DARK, 0.2, rust=0.6)
    B.box(Vector((-2.0, 0, z)), (4.0, 1.4, 0.08), I3, "metal", C_RUST, 0.2, rust=0.7)                       # intact deck
    tilt = Matrix.Rotation(math.radians(-28), 3, 'Y')
    B.box(Vector((1.6, 0, z - 0.95)), (3.6, 1.4, 0.08), tilt, "metal", C_RUST, 0.2, rust=0.8)               # collapsed span
    for y in (-0.7, 0.7):
        B.box(Vector((-2.0, y, z + 0.5)), (4.0, 0.05, 0.05), I3, "panel", C_YELLOW, 0.3, rust=0.4)
        B.box(Vector((1.6, y, z - 0.45)), (3.6, 0.05, 0.05), tilt @ Matrix.Rotation(0.2, 3, 'X'), "panel", C_YELLOW, 0.3, rust=0.5)
        for x in (-3.8, -2.6, -1.4, -0.2):
            B.box(Vector((x, y, z + 0.25)), (0.04, 0.04, 0.5), I3, "metal", C_DARK, 0.2)
    rubble = Vector((3.2, 0, 0))
    for k in range(5):
        chunk(B, rubble + Vector((random.uniform(-0.6, 0.6), random.uniform(-0.8, 0.8), 0.1)), 0.25, 150 + k)
    for k in range(3):
        vine(B, Vector((-3 + k, 0.7, z)), 2.2, 155 + k)
    finish(B, "Mesh", coll); return coll

def build_cable_drapes():
    coll = new("CableDrapes"); B = Builder()
    for x in (-3.0, 3.0):
        B.box(Vector((x, 0, 2.5)), (0.25, 0.25, 5.0), I3, "metal", C_DARK, 0.2, rust=0.5)
    for k in range(6):                                                       # sagging cables
        z = 4.6 - k * 0.12; sag = 1.2 + k * 0.25
        pts = [Vector((-2.9 + 5.8 * t, (k - 3) * 0.06, z - sag * 4 * t * (1 - t))) for t in [i / 12 for i in range(13)]]
        B.pipe(pts, 0.03, 5, "rubber", random.choice([C_BLACK, (0.5, 0.08, 0.05), (0.1, 0.1, 0.4)]))
    for k in range(3):                                                       # snapped ends hanging loose
        p = Vector((-2.9, 0.15 + k * 0.08, 4.3 - k * 0.2))
        B.pipe([p, p + Vector((0.2, 0, -0.8)), p + Vector((0.25, 0.05, -1.8 - k * 0.3))], 0.03, 5, "rubber", C_BLACK)
    finish(B, "Mesh", coll); return coll

def build_lab_bench():
    coll = new("LabBench"); B = Builder()
    B.box(Vector((0, 0, 0.9)), (3.0, 0.9, 0.06), I3, "panel", C_WHITE, 0.25, rust=0.3)
    for x in (-1.4, 1.4):
        B.box(Vector((x, 0, 0.45)), (0.1, 0.8, 0.9), I3, "metal", C_DARK, 0.2)
    B.box(Vector((-0.8, 0, 0.45)), (1.2, 0.8, 0.85), I3, "panel", C_WHITE, 0.3, rust=0.4)                   # cupboard
    B.box(Vector((-0.8, -0.41, 0.5)), (0.5, 0.02, 0.5), Matrix.Rotation(0.8, 3, 'Z'), "panel", C_WHITE, 0.3)  # door hanging open
    for k in range(6):                                                       # glassware
        p = Vector((0.2 + k * 0.2, random.uniform(-0.25, 0.25), 0.93))
        if k % 3 == 2:
            B.box(p + Vector((0, 0, 0.03)), (0.25, 0.06, 0.06), Matrix.Rotation(random.uniform(0, 3), 3, 'Z'), "glass", (0.6, 0.9, 1.0), 0.02)   # knocked over
        else:
            B.cyl(p, p + Vector((0, 0, 0.25)), 0.05, 8, "glass", (0.6, 0.9, 1.0), 0.02)
            B.cyl(p, p + Vector((0, 0, 0.08)), 0.045, 8, "cyan", random.choice([C_CYAN, (0.4, 1.0, 0.3)]), 0.02)
    B.box(Vector((-0.6, 0.15, 1.15)), (0.5, 0.35, 0.4), I3, "panel", C_WHITE, 0.3)                          # old monitor
    B.box(Vector((-0.6, -0.03, 1.17)), (0.4, 0.01, 0.28), I3, "metal", (0.05, 0.07, 0.07), 0.1)
    for k in range(5):
        B.box(Vector((random.uniform(-1.2, 1.2), random.uniform(-0.3, 0.3), 0.935)), (0.21, 0.3, 0.004), Matrix.Rotation(random.uniform(0, 3), 3, 'Z'),
              "panel", C_PAPER, 0.1)
    moss(B, Vector((1.1, 0.2, 0.93)), 0.3, 160)
    for k in range(2):
        vine(B, Vector((1.45, 0.4 - k * 0.8, 0.93)), 0.9, 161 + k)
    finish(B, "Mesh", coll); return coll

def build_flicker_terminal():
    coll = new("FlickerTerminal"); B = Builder()
    B.box(Vector((0, 0, 0.6)), (0.8, 0.6, 1.2), I3, "panel", C_WHITE, 0.25, rust=0.4)
    B.box(Vector((0, -0.05, 1.45)), (0.9, 0.5, 0.6), Matrix.Rotation(-0.25, 3, 'X'), "panel", C_WHITE, 0.25, rust=0.3)
    B.box(Vector((0, -0.2, 1.1)), (0.7, 0.3, 0.05), Matrix.Rotation(0.3, 3, 'X'), "metal", C_DARK, 0.15)     # keyboard
    for k in range(3):
        B.box(Vector((random.uniform(-0.3, 0.3), -0.29, 1.4 + random.uniform(-0.1, 0.1))), (0.3, 0.004, 0.01), Matrix.Rotation(random.uniform(-1, 1), 3, 'Y'),
              "panel", (0.9, 0.9, 0.9), 0.05)                               # screen cracks
    moss(B, Vector((0.3, 0.1, 1.72)), 0.2, 170)
    finish(B, "Mesh", coll)
    S = Builder()
    S.box(Vector((0, 0, 0)), (0.7, 0.01, 0.42), I3, "cyan", (0.3, 1.0, 0.6), 0.05)
    scr = node(S, "Screen", coll, Vector((0, -0.28, 1.45)))
    scr.rotation_euler = (-0.25, 0, 0)
    keys(scr, (1, 20, 21, 23, 24, 50, 51, 53, 60, 61, 91), "scale",
         [Vector((1, 1, 1)), Vector((1, 1, 1)), Vector((0.01, 1, 0.01)), Vector((1, 1, 1)), Vector((0.01, 1, 0.01)), Vector((0.01, 1, 0.01)),
          Vector((1, 1, 1)), Vector((1, 1, 0.3)), Vector((1, 1, 1)), Vector((1, 1, 1)), Vector((1, 1, 1))])
    return coll

def build_monitor_bank():
    coll = new("MonitorBank"); B = Builder()
    B.box(Vector((0, 0.3, 1.5)), (4.0, 0.4, 3.0), I3, "metal", C_DARK, 0.2, rust=0.4)
    screens = []
    for i in range(5):
        for j in range(3):
            c = Vector((-1.6 + i * 0.8, 0.08, 0.7 + j * 0.8))
            B.box(c, (0.7, 0.3, 0.6), I3, "panel", jit(C_WHITE, 0.1), 0.2, rust=0.3)
            screens.append(c)
    finish(B, "Mesh", coll)
    D = Builder()                                                            # dead screens
    L = Builder()                                                            # lit screens
    for k, c in enumerate(screens):
        (L if k % 3 == 0 else D).box(c + Vector((0, -0.16, 0)), (0.56, 0.01, 0.44), I3, "cyan" if k % 3 == 0 else "metal",
                                     (0.3, 0.9, 1.0) if k % 3 == 0 else (0.05, 0.06, 0.07), 0.05)
    finish(D, "Dead", coll)
    lit = node(L, "Lit", coll, Vector((0, 0, 0)))
    keys(lit, (1, 40, 41, 44, 45, 121), "scale", [Vector((1, 1, 1)), Vector((1, 1, 1)), Vector((1, 1, 0.97)), Vector((1, 1, 0.97)), Vector((1, 1, 1)), Vector((1, 1, 1))])
    Sp = Builder()                                                           # one sparking screen
    for k in range(6):
        a = k / 6 * math.tau
        Sp.box(Vector((math.cos(a) * 0.15, -0.2, math.sin(a) * 0.15)), (0.12, 0.01, 0.02), Matrix.Rotation(-a, 3, 'Y'), "glow", C_AMBER, 0.05)
    spark = node(Sp, "Spark", coll, screens[7] + Vector((0, -0.05, 0)))
    keys(spark, (1, 30, 32, 35, 36, 80, 82, 84, 121), "scale", [Vector((0.01, 0.01, 0.01))] * 2 + [Vector((1, 1, 1)), Vector((0.01, 0.01, 0.01))] * 2 +
         [Vector((1.3, 1, 1.3)), Vector((0.01, 0.01, 0.01)), Vector((0.01, 0.01, 0.01))])
    return coll

def build_filing_cabinets():
    coll = new("FilingCabinets"); B = Builder()
    for k in range(3):
        x = -1.0 + k * 0.7
        B.box(Vector((x, 0, 0.7)), (0.6, 0.7, 1.4), I3, "metal", (0.45, 0.47, 0.44), 0.2, rust=0.5)
        for j in range(3):
            d = 0.25 if (k, j) in ((0, 1), (2, 0)) else 0
            B.box(Vector((x, -0.36 - d, 0.3 + j * 0.42)), (0.52, 0.04 + d * 2, 0.36), I3, "metal", (0.5, 0.52, 0.5), 0.2, rust=0.4)
    B.box(Vector((1.4, -0.4, 0.3)), (1.4, 0.7, 0.6), Matrix.Rotation(0.3, 3, 'Z'), "metal", (0.45, 0.47, 0.44), 0.2, rust=0.6)   # toppled
    for k in range(14):
        B.box(Vector((random.uniform(-1.4, 2.2), random.uniform(-1.6, -0.4), 0.005)), (0.21, 0.3, 0.004), Matrix.Rotation(random.uniform(0, 3), 3, 'Z'),
              "panel", jit(C_PAPER, 0.1), 0.1)
    finish(B, "Mesh", coll); return coll

def build_cryo_pod():
    coll = new("CryoPod"); B = Builder(); G = Builder()
    B.box(Vector((0, 0.1, 0.2)), (1.4, 1.2, 0.4), I3, "metal", C_DARK, 0.2, rust=0.3)
    B.tube_rings([quad_ring(0, 0, 0.4, 0.6, 0.5), quad_ring(0, 0, 2.3, 0.6, 0.5)], "panel", C_WHITE, 0.2, 0.3, cap=False)
    B.box(Vector((0, 0.1, 2.45)), (1.4, 1.2, 0.3), I3, "panel", C_WHITE, 0.2, rust=0.2)
    G.box(Vector((0, -0.49, 1.35)), (1.0, 0.02, 1.7), I3, "glass", (0.6, 0.9, 1.0), 0.02)
    for k in range(4):                                                       # cracks in the glass
        B.box(Vector((random.uniform(-0.3, 0.3), -0.5, random.uniform(0.9, 1.9))), (0.4, 0.004, 0.012), Matrix.Rotation(random.uniform(-1, 1), 3, 'Y'),
              "panel", (0.95, 0.95, 0.95), 0.05)
    B.pipe([Vector((0.6, 0.4, 0.3)), Vector((1.1, 0.8, 0.1)), Vector((1.6, 0.9, 0.05))], 0.06, 8, "rubber", C_BLACK)
    for k in range(3):
        vine(B, Vector((random.uniform(-0.6, 0.6), 0.5, 2.6)), random.uniform(1.0, 2.0), 180 + k)
    moss(B, Vector((0, 0.3, 2.6)), 0.4, 185)
    finish(B, "Mesh", coll); finish(G, "Glass", coll)
    I = Builder()
    I.box(Vector((0, 0, 0)), (0.9, 0.8, 1.6), I3, "field_zp", C_CYAN, 0.02)
    inner = node(I, "Frost", coll, Vector((0, 0.05, 1.35)))
    keys(inner, (1, 60, 62, 64, 66, 121), "scale", [Vector((1, 1, 1)), Vector((1, 1, 1)), Vector((0.2, 0.2, 0.2)), Vector((1, 1, 1)), Vector((0.2, 0.2, 0.2)), Vector((1, 1, 1))])
    return coll

def build_observation_booth():
    coll = new("ObservationBooth"); B = Builder(); G = Builder()
    for (x, y) in ((-1.5, -1.0), (1.5, -1.0), (1.5, 1.0), (-1.5, 1.0)):
        B.box(Vector((x, y, 1.75)), (0.15, 0.15, 3.5), I3, "metal", C_DARK, 0.2, rust=0.4)
    B.box(Vector((0, 0, 3.5)), (3.2, 2.2, 0.2), I3, "panel", C_WHITE, 0.25, rust=0.3)
    B.box(Vector((0, 0, 0.5)), (3.2, 2.2, 1.0), I3, "panel", C_WHITE, 0.25, rust=0.3)
    G.box(Vector((-0.75, -1.0, 2.25)), (1.4, 0.03, 2.3), I3, "glass", (0.6, 0.9, 1.0), 0.02)               # one pane left
    for k in range(8):                                                       # shattered glass on the floor
        a = random.uniform(0, math.tau)
        G.box(Vector((0.7 + random.uniform(-0.6, 0.6), -1.6 + random.uniform(-0.5, 0.4), 0.01)), (random.uniform(0.1, 0.3), random.uniform(0.1, 0.3), 0.01),
              Matrix.Rotation(a, 3, 'Z'), "glass", (0.6, 0.9, 1.0), 0.02)
    for k in range(4):
        B.box(Vector((0.75 + random.uniform(-0.6, 0.6), -1.0, 1.1 + random.uniform(0, 0.3))), (0.2, 0.03, 0.12), Matrix.Rotation(random.uniform(-1, 1), 3, 'Y'),
              "glass", (0.6, 0.9, 1.0), 0.02)                                # jagged shards in the frame
    B.box(Vector((0, 0.5, 1.1)), (2.4, 0.6, 0.1), I3, "metal", C_DARK, 0.15)                               # console
    for k in range(3):
        vine(B, Vector((random.uniform(-1.4, 1.4), -1.0, 3.4)), 2.5, 190 + k)
    finish(B, "Mesh", coll); finish(G, "Glass", coll); return coll

def build_pipe_cluster():
    coll = new("PipeCluster"); B = Builder()
    for k in range(5):
        y = -0.1 * k; r = 0.12 + 0.04 * (k % 3)
        z = 0.5 + k * 0.45
        B.cyl(Vector((-3, y, z)), Vector((3, y, z)), r, 12, "metal", jit(C_RUST if k % 2 else (0.4, 0.42, 0.4), 0.1), 0.2, rust=0.7)
        for x in (-2.0, 0.5, 2.5):
            ring(B, Vector((x, y, z)), (1, 0, 0), r, r + 0.04, 0.12, 12, "metal", C_DARK, 0.2, rust=0.5)
    for x in (-2.5, 2.5):
        B.box(Vector((x, 0.1, 1.4)), (0.15, 0.15, 2.8), I3, "metal", C_DARK, 0.2, rust=0.6)
    B.cyl(Vector((1.0, -0.2, 1.4)), Vector((1.0, -0.5, 1.4)), 0.05, 8, "steel", C_STEEL)                      # valve wheel
    ring(B, Vector((1.0, -0.52, 1.4)), (0, 1, 0), 0.18, 0.22, 0.03, 16, "panel", (0.7, 0.1, 0.05), 0.1)
    B.cyl(Vector((-1.0, -0.3, 1.85)), Vector((-1.0, -0.3, 1.7)), 0.12, 10, "metal", C_RUST, 0.2, rust=0.9)    # the broken stub
    B.box(Vector((-1.0, -0.3, 0.005)), (0.9, 0.7, 0.01), I3, "glass", C_WATER, 0.05)                         # puddle under it
    moss(B, Vector((1.8, -0.1, 0.35)), 0.45, 200)
    finish(B, "Mesh", coll)
    for k in range(2):                                                       # drips
        D = Builder()
        D.cyl(Vector((0, 0, -0.03)), Vector((0, 0, 0.03)), 0.025, 6, "glass", (0.5, 0.8, 0.9), 0.02)
        ob = node(D, f"Drip{k}", coll, Vector((-1.0, -0.3, 1.65)))
        o = 1 + k * 30
        keys(ob, (1, o, o + 18, o + 19, 61), "location",
             [Vector((-1.0, -0.3, 1.65))] * 2 + [Vector((-1.0, -0.3, 0.02)), Vector((-1.0, -0.3, 1.65)), Vector((-1.0, -0.3, 1.65))], linear=True)
    return coll

def build_hanging_lamp():
    coll = new("HangingLamp"); B = Builder()
    B.box(Vector((0, 0, 6.0)), (0.6, 0.6, 0.1), I3, "metal", C_DARK, 0.2)
    finish(B, "Mount", coll)
    L = Builder()
    L.pipe([Vector((0, 0, 0)), Vector((0, 0, -2.5))], 0.02, 5, "rubber", C_BLACK)
    L.tube_rings([[Vector((math.cos(a) * r, math.sin(a) * r, z)) for a in [i / 16 * math.tau for i in range(16)]] for r, z in ((0.15, -2.5), (0.5, -2.85))],
                 "metal", (0.3, 0.33, 0.3), 0.2, 0.5, cap=False, smooth=True)
    L.cyl(Vector((0, 0, -2.6)), Vector((0, 0, -2.75)), 0.12, 12, "lamp", C_LAMP, 0.02)
    lamp = node(L, "Lamp", coll, Vector((0, 0, 5.95)))
    cycle(lamp, "rotation_euler", [(0.12, 0.04, 0), (-0.1, -0.05, 0)], 120)
    return coll

def build_ceiling_fan():
    coll = new("CeilingFan"); B = Builder()
    ring(B, Vector((0, 0, 6.0)), (0, 0, 1), 2.1, 2.3, 0.3, 32, "metal", C_RUST, 0.2, rust=0.6)
    for a in (0, math.pi / 2, math.pi, 3 * math.pi / 2):
        B.box(Vector((math.cos(a) * 1.05, math.sin(a) * 1.05, 6.0)), (2.1, 0.08, 0.08), Matrix.Rotation(a, 3, 'Z'), "metal", C_DARK, 0.2)
    finish(B, "Frame", coll)
    F = Builder()
    F.cyl(Vector((0, 0, -0.15)), Vector((0, 0, 0.15)), 0.3, 12, "metal", C_DARK)
    for k in range(5):                                                       # one of six blades missing
        a = k / 6 * math.tau
        F.box(Vector((math.cos(a) * 1.0, math.sin(a) * 1.0, 0)), (1.6, 0.4, 0.03), Matrix.Rotation(a, 3, 'Z') @ Matrix.Rotation(0.3, 3, 'X'),
              "metal", jit((0.4, 0.4, 0.38), 0.1), 0.2, rust=0.5)
    fan = node(F, "Fan", coll, Vector((0, 0, 6.0)))
    spin(fan, 2, 1, 240)
    return coll

def build_rusted_barrels():
    coll = new("RustedBarrels"); B = Builder()
    for (x, y) in ((-0.5, 0), (0.2, 0.45)):
        B.cyl(Vector((x, y, 0)), Vector((x, y, 1.1)), 0.35, 16, "metal", jit(random.choice([C_RUST, (0.2, 0.3, 0.45), (0.5, 0.35, 0.1)]), 0.1), 0.2, rust=0.8)
        for z in (0.3, 0.8):
            ring(B, Vector((x, y, z)), (0, 0, 1), 0.35, 0.37, 0.04, 16, "metal", C_DARK, 0.2)
    B.cyl(Vector((0.4, -0.5, 0.35)), Vector((1.5, -0.8, 0.35)), 0.35, 16, "metal", C_RUST, 0.2, rust=0.9)     # tipped
    B.box(Vector((1.9, -0.9, 0.005)), (1.2, 0.9, 0.01), I3, "glow", (0.5, 0.9, 0.2), 0.1)                   # something glowing leaked out
    hazard(B, Vector((-0.85, -0.36, 0.5)), (1, 0, 0), (0, 0, 1), 0.7, 0.2, (0, -1, 0), pitch=0.08)
    moss(B, Vector((-0.3, 0.6, 0.02)), 0.4, 210)
    finish(B, "Mesh", coll); return coll

def build_crate_stack():
    coll = new("CrateStack"); B = Builder()
    for (x, y, z, s) in ((0, 0, 0.5, 1.0), (1.1, 0.1, 0.45, 0.9), (0.5, 0.05, 1.45, 0.9), (-1.0, 0.4, 0.4, 0.8)):
        B.box(Vector((x, y, z)), (s, s, s), Matrix.Rotation(random.uniform(-0.1, 0.1), 3, 'Z'), "wood", jit((0.45, 0.32, 0.18), 0.12), 0.25)
        for d in (-1, 1):
            B.box(Vector((x, y - s / 2 - 0.01, z + d * s * 0.35)), (s, 0.02, 0.1), I3, "wood", (0.3, 0.2, 0.1), 0.2)
    for k in range(6):                                                       # a broken one: planks spilled
        B.box(Vector((-1.8 + random.uniform(-0.4, 0.4), -0.6 + random.uniform(-0.4, 0.4), 0.03)), (0.9, 0.15, 0.03), Matrix.Rotation(random.uniform(0, 3), 3, 'Z'),
              "wood", jit((0.45, 0.32, 0.18), 0.12), 0.2)
    fern(B, Vector((-1.8, -0.4, 0)), 220, 0.6)
    finish(B, "Mesh", coll); return coll

def build_puddle_debris():
    coll = new("PuddleDebris"); B = Builder()
    rings_ = [[Vector((math.cos(a) * r * (1 + 0.2 * math.sin(3 * a)), math.sin(a) * r * 0.7, 0.01)) for a in [i / 24 * math.tau for i in range(24)]] for r in (2.0,)]
    B.tube_rings(rings_ + [[p + Vector((0, 0, 0.005)) for p in rings_[0]]], "glass", C_WATER, 0.02, 0.0, cap=True, smooth=False)
    for k in range(10):
        B.box(Vector((random.uniform(-1.5, 1.5), random.uniform(-1, 1), 0.02)), (0.12, 0.08, 0.005), Matrix.Rotation(random.uniform(0, 3), 3, 'Z'),
              "rubber", jit(random.choice([C_LEAF, (0.5, 0.4, 0.15)]), 0.1), 0.1)
    for k in range(3):
        chunk(B, Vector((random.uniform(-1.5, 1.5), random.uniform(-1, 1), 0.08)), 0.18, 230 + k)
    finish(B, "Mesh", coll); return coll

def build_tipped_barriers():
    coll = new("TippedBarriers"); B = Builder()
    for k, (x, a, up) in enumerate(((-1.2, 0.2, True), (0.2, -0.4, False), (1.5, 1.2, False))):
        rot = Matrix.Rotation(a, 3, 'Z') @ (I3 if up else Matrix.Rotation(1.45, 3, 'X'))
        base = Vector((x, 0, 0.5 if up else 0.12))
        B.box(base, (1.2, 0.1, 0.25), rot, "panel", C_WHITE, 0.2, rust=0.3)
        for d in (-0.45, 0.45):
            B.box(base + rot @ Vector((d, 0, -0.3)), (0.06, 0.4, 0.6), rot, "metal", C_DARK, 0.2)
    for k in range(3):                                                       # cones
        p = Vector((random.uniform(-1.5, 1.5), -1.0 + random.uniform(-0.3, 0.3), 0))
        B.tube_rings([[p + Vector((math.cos(t) * r, math.sin(t) * r, z)) for t in [i / 12 * math.tau for i in range(12)]] for r, z in ((0.22, 0.02), (0.03, 0.6))],
                     "panel", (0.9, 0.4, 0.05), 0.1, 0.2, cap=True, smooth=True)
    finish(B, "Mesh", coll); return coll

def build_elevator_ruin():
    coll = new("ElevatorRuin"); B = Builder(); G = Builder()
    for k in range(6):                                                       # glass tube, top half shattered away
        a = k / 6 * math.tau
        B.box(Vector((math.cos(a) * 1.3, math.sin(a) * 1.3, 3.0)), (0.12, 0.12, 6.0), I3, "metal", C_DARK, 0.2, rust=0.4)
    for z in (0.1, 3.0):
        ring(B, Vector((0, 0, z)), (0, 0, 1), 1.25, 1.4, 0.2, 24, "panel", C_WHITE, 0.2, rust=0.3)
    G.tube_rings([[Vector((math.cos(a) * 1.28, math.sin(a) * 1.28, z)) for a in [i / 24 * math.tau for i in range(24)]] for z in (0.2, 2.2)],
                 "glass", (0.6, 0.9, 1.0), 0.02, 0.0, cap=False, smooth=True)
    B.cyl(Vector((0, 0, 0.9)), Vector((0, 0, 1.0)), 1.1, 24, "panel", C_WHITE, 0.2, rust=0.4)
    B.box(Vector((0.3, 0.2, 1.4)), (1.8, 1.8, 0.08), Matrix.Rotation(0.35, 3, 'X'), "metal", C_DARK, 0.2, rust=0.5)   # tilted platform
    for k in range(4):
        vine(B, Vector((math.cos(k) * 1.3, math.sin(k) * 1.3, 5.9)), random.uniform(3, 5), 240 + k)
    leaves(B, Vector((0, 0, 6.0)), 1.1, 245)
    finish(B, "Mesh", coll); finish(G, "Glass", coll); return coll

# ======================================================================================== superstructures
def build_cooling_tower():
    coll = new("CoolingTower"); B = Builder()
    H, n = 42.0, 40
    def rad(z):
        t = z / H
        return 18.0 * (1 - 0.55 * math.sin(t * math.pi * 0.85)) + 2.0 * t
    rings_ = []
    for k in range(13):
        z = H * k / 12
        pts = []
        for i in range(n):
            a = i / n * math.tau
            dent = 0.5 if (k in (7, 8) and 5 <= i <= 9) else 1.0            # a collapsed breach
            pts.append(Vector((math.cos(a) * rad(z) * dent, math.sin(a) * rad(z) * dent, z)))
        rings_.append(pts)
    B.tube_rings(rings_, "rock", C_CONC, 0.18, 0.2, cap=False, smooth=True)
    for k in range(20):                                                      # support legs round the base
        a = k / 20 * math.tau
        B.box(Vector((math.cos(a) * 18.2, math.sin(a) * 18.2, 1.5)), (1.0, 1.0, 3.2), Matrix.Rotation(a, 3, 'Z') @ Matrix.Rotation(0.2, 3, 'Y'), "rock", C_CONC_D, 0.15)
    for k in range(30):                                                      # ivy climbing
        a = random.uniform(0, math.tau); z = random.uniform(0, 30)
        rock(B, Vector((math.cos(a) * (rad(z) + 0.4), math.sin(a) * (rad(z) + 0.4), z)), random.uniform(1.0, 2.5), 300 + k, 1, 0.4, "rubber",
             lambda p, nn: jit(random.choice([C_LEAF, C_MOSS, C_LEAF2]), 0.15), (1, 1, 1.3))
    for k in range(10):
        chunk(B, Vector((math.cos(0.9) * 22 + random.uniform(-4, 4), math.sin(0.9) * 22 + random.uniform(-4, 4), 0.8)), random.uniform(0.8, 1.8), 330 + k)
    finish(B, "Shell", coll)
    for k in range(3):                                                       # steam plume, rising and fading
        St = Builder()
        rock(St, Vector((0, 0, 0)), 6.0, 340 + k, 1, 0.3, "field_cold", lambda p, nn: (0.8, 0.9, 1.0), (1, 1, 0.7))
        ob = node(St, f"Steam{k}", coll, Vector((0, 0, H)))
        o = 1 + k * 80
        keys(ob, (1, o, o + 239, o + 240, 241 + 240), "location", [Vector((0, 0, H)), Vector((0, 0, H)), Vector((2, 1, H + 30)), Vector((0, 0, H)), Vector((0, 0, H))], linear=True)
        keys(ob, (1, o, o + 120, o + 239, 481), "scale", [Vector((0.3, 0.3, 0.3)), Vector((0.3, 0.3, 0.3)), Vector((1.2, 1.2, 1.2)), Vector((0.05, 0.05, 0.05)), Vector((0.3, 0.3, 0.3))])
    return coll

def build_gantry_crane():
    coll = new("GantryCrane"); B = Builder()
    H, S = 32.0, 50.0
    for sx in (-1, 1):
        for sy in (-1, 1):                                                   # legs, braced
            B.box(Vector((sx * S / 2, sy * 6, H / 2)), (1.4, 1.4, H), I3, "metal", C_YELLOW, 0.25, rust=0.7)
        for k in range(6):
            z = 3 + k * 5
            B.box(Vector((sx * S / 2, 0, z)), (0.5, 12.0, 0.5), Matrix.Rotation(0.35 * (1 if k % 2 else -1), 3, 'X'), "metal", C_RUST, 0.2, rust=0.8)
        B.box(Vector((sx * S / 2, 0, 0.6)), (4.0, 16.0, 1.2), I3, "metal", C_DARK, 0.2, rust=0.6)            # bogies
    for sy in (-1, 1):                                                       # main girders
        B.box(Vector((0, sy * 6, H)), (S + 2, 1.6, 2.4), I3, "metal", C_YELLOW, 0.25, rust=0.7)
        for k in range(16):
            B.box(Vector((-S / 2 + k * S / 15, sy * 6, H)), (0.25, 1.7, 2.3), Matrix.Rotation(0.6 * (1 if k % 2 else -1), 3, 'Y'), "metal", C_RUST, 0.2, rust=0.8)
    B.box(Vector((-S / 2 - 3, 0, H + 1.5)), (5.0, 8.0, 3.0), I3, "metal", C_DARK, 0.2, rust=0.5)             # machinery house
    for k in range(6):
        vine(B, Vector((random.uniform(-S / 2, S / 2), random.choice((-6.8, 6.8)), H - 1.2)), random.uniform(5, 12), 350 + k)
    finish(B, "Frame", coll)
    T = Builder()                                                            # trolley and hook, travelling the span
    T.box(Vector((0, 0, 0)), (4.0, 14.0, 2.0), I3, "metal", C_DARK, 0.2, rust=0.5)
    T.pipe([Vector((0, 0, -1)), Vector((0, 0, -18))], 0.12, 6, "steel", C_STEEL)
    T.box(Vector((0, 0, -18.5)), (1.4, 1.0, 1.2), I3, "metal", C_YELLOW, 0.2, rust=0.5)
    T.pipe([Vector((0, 0, -19)), Vector((0.5, 0, -19.8)), Vector((0, 0, -20.5)), Vector((-0.4, 0, -20.0))], 0.2, 8, "steel", C_STEEL)
    tr = node(T, "Trolley", coll, Vector((-15, 0, H + 2.2)))
    keys(tr, (1, 150, 240, 390, 481), "location", [Vector((-15, 0, H + 2.2)), Vector((15, 0, H + 2.2)), Vector((15, 0, H + 2.2)),
                                                   Vector((-15, 0, H + 2.2)), Vector((-15, 0, H + 2.2))])
    return coll

def build_reactor_sphere():
    coll = new("ReactorSphere"); B = Builder()
    R = 14.0
    bm = B.bm
    res = bmesh.ops.create_uvsphere(bm, u_segments=40, v_segments=24, radius=R)
    for v in res["verts"]:
        v.co.z += R + 4
    fs = list({f for v in res["verts"] for f in v.link_faces})
    for f in fs:
        f.material_index = MI["panel"]; f.smooth = True
    B.paint(fs, C_WHITE, 0.2, rust=0.4)
    for k in range(8):                                                       # support legs
        a = k / 8 * math.tau
        B.box(Vector((math.cos(a) * R * 0.75, math.sin(a) * R * 0.75, 4.5)), (1.5, 1.5, 9.0), Matrix.Rotation(a, 3, 'Z') @ Matrix.Rotation(-0.25, 3, 'Y'),
              "metal", C_DARK, 0.2, rust=0.5)
    ring(B, Vector((0, 0, R + 4)), (0, 0, 1), R - 0.2, R + 0.25, 1.2, 64, "cyan", C_CYAN, 0.05)            # glowing equator seam
    for k in range(20):
        a = random.uniform(0, math.tau); e = random.uniform(-0.2, 0.9)
        rock(B, Vector((math.cos(a) * math.cos(e) * R, math.sin(a) * math.cos(e) * R, R + 4 + math.sin(e) * R)), random.uniform(1.0, 2.2), 360 + k, 1, 0.4,
             "rubber", lambda p, nn: jit(random.choice([C_LEAF, C_MOSS]), 0.15), (1, 1, 0.6))
    finish(B, "Sphere", coll)
    for k, (tilt, speed) in enumerate(((0.35, 1), (-0.6, -1), (1.1, 1))):   # containment rings, each on its own tilt
        Rg = Builder()
        ring(Rg, Vector((0, 0, 0)), (0, 0, 1), R + 2.0 + k * 1.3, R + 2.8 + k * 1.3, 0.8, 64, "metal", C_DARK, 0.2, rust=0.4)
        ring(Rg, Vector((0, 0, 0.45)), (0, 0, 1), R + 2.2 + k * 1.3, R + 2.6 + k * 1.3, 0.1, 64, "cyan", C_CYAN, 0.05)
        ob = node(Rg, f"Ring{k}", coll, Vector((0, 0, R + 4)))
        spin(ob, 2, speed, 480, rest=(tilt, 0.3 * k, 0))
    return coll

def build_arcology_spire():
    coll = new("ArcologySpire"); B = Builder()
    H = 72.0
    B.tube_rings([[Vector((math.cos(a) * r, math.sin(a) * r, z)) for a in [i / 12 * math.tau for i in range(12)]] for r, z in ((7, 0), (5, 20), (4.5, 50), (3, H))],
                 "rock", C_CONC, 0.18, 0.2, cap=True, smooth=False)
    for k, z in enumerate((12, 26, 40, 54, 66)):                             # ringed platforms, one snapped
        if k == 2:
            for j in range(3):
                a = j * 0.5
                B.box(Vector((math.cos(a) * 9, math.sin(a) * 9, z)), (6, 3, 0.6), Matrix.Rotation(a, 3, 'Z'), "rock", C_CONC_D, 0.15)
        else:
            ring(B, Vector((0, 0, z)), (0, 0, 1), 4.5, 12.0 - k, 0.8, 24, "rock", C_CONC_D, 0.15, rust=0.2)
            ring(B, Vector((0, 0, z + 0.9)), (0, 0, 1), 11.6 - k, 12.0 - k, 1.0, 24, "panel", C_WHITE, 0.2, rust=0.3)
    for k in range(40):
        a = random.uniform(0, math.tau); z = random.uniform(2, 60)
        rock(B, Vector((math.cos(a) * 5.5, math.sin(a) * 5.5, z)), random.uniform(1.0, 2.4), 380 + k, 1, 0.4, "rubber",
             lambda p, nn: jit(random.choice([C_LEAF, C_MOSS, C_LEAF2]), 0.15), (1, 1, 1.4))
    for k in range(10):
        vine(B, Vector((math.cos(k) * 11, math.sin(k) * 11, 26)), random.uniform(6, 14), 420 + k)
    finish(B, "Mesh", coll); return coll

def build_sky_bridge():
    coll = new("SkyBridge"); B = Builder()
    for x in (-40, 40):                                                      # towers
        B.box(Vector((x, 0, 20)), (8, 8, 40), I3, "rock", C_CONC, 0.18, rust=0.2)
        for z in (10, 20, 30):
            B.box(Vector((x, -4.05, z)), (6, 0.1, 1.5), I3, "cyan", (0.2, 0.3, 0.35), 0.1)
    for (x0, x1, droop) in ((-36, -8, 0.0), (8, 36, 0.0)):                   # surviving spans with a conveyor deck
        B.box(Vector(((x0 + x1) / 2, 0, 30)), (x1 - x0, 5, 1.5), I3, "metal", C_DARK, 0.2, rust=0.6)
        B.box(Vector(((x0 + x1) / 2, 0, 30.85)), (x1 - x0, 3.4, 0.2), I3, "rubber", C_BLACK, 0.1)
        for sy in (-1, 1):
            B.box(Vector(((x0 + x1) / 2, sy * 2.5, 32.0)), (x1 - x0, 0.2, 2.2), I3, "panel", C_WHITE, 0.2, rust=0.4)
    tilt = Matrix.Rotation(math.radians(55), 3, 'Y')                         # the fallen middle span
    B.box(Vector((-2, 0, 14)), (20, 5, 1.5), tilt, "metal", C_DARK, 0.2, rust=0.8)
    for k in range(14):
        chunk(B, Vector((random.uniform(-8, 8), random.uniform(-6, 6), 0.8)), random.uniform(0.8, 1.8), 440 + k)
    for k in range(8):
        vine(B, Vector((random.choice((-8, 8)) + random.uniform(-2, 2), random.uniform(-2.5, 2.5), 29.3)), random.uniform(6, 16), 460 + k)
    finish(B, "Mesh", coll); return coll

def build_panel_arm_wall():
    coll = new("PanelArmWall"); B = Builder()
    W, H, n = 36.0, 24.0, 6
    B.box(Vector((0, 1.5, H / 2)), (W + 2, 1.0, H + 2), I3, "metal", C_DARK, 0.2, rust=0.5)                 # backing frame
    finish(B, "Frame", coll)
    s = W / n
    for i in range(n):
        for j in range(4):
            P = Builder()
            broken = (i, j) in ((1, 3), (4, 0))
            P.box(Vector((0, 0, 0)), (s - 0.3, 0.3, s - 0.3), I3, "panel", jit(C_WHITE, 0.06), 0.15, rust=0.5 if broken else 0.2)
            P.box(Vector((0, 0.9, 0)), (0.5, 1.6, 0.5), I3, "metal", C_DARK, 0.2)       # piston arm to the frame
            if broken:
                P.box(Vector((0, -0.2, 0)), (s * 0.7, 0.05, 0.1), Matrix.Rotation(0.6, 3, 'Y'), "rock", C_MOSS, 0.2)
            c = Vector((-W / 2 + s / 2 + i * s, -0.5, 1.0 + s / 2 + j * s))
            ob = node(P, f"Panel{i}_{j}", coll, c)
            o = 1 + ((i * 7 + j * 3) % 10) * 20
            out = c + Vector((0, -2.5 if (i + j) % 2 else -1.2, 0))
            keys(ob, (1, o, o + 30, o + 150, o + 180, 481), "location", [c, c, out, out, c, c])
    return coll

PIECES = [(globals()[f"build_{n}"], f"{n}.glb") for n in (
    "cracked_pillar", "collapsed_pillar", "rubble_pile", "moss_mound", "overgrown_tree", "fern_cluster", "hanging_vines",
    "collapsed_panel_wall", "broken_catwalk", "cable_drapes", "lab_bench", "flicker_terminal", "monitor_bank", "filing_cabinets",
    "cryo_pod", "observation_booth", "pipe_cluster", "hanging_lamp", "ceiling_fan", "rusted_barrels", "crate_stack",
    "puddle_debris", "tipped_barriers", "elevator_ruin",
    "cooling_tower", "gantry_crane", "reactor_sphere", "arcology_spire", "sky_bridge", "panel_arm_wall")]
ICONS = []

def build_all(do_export=True):
    return build_family(PIECES, OUT_DIR, do_export)

if __name__ == "__main__":
    build_all(do_export=False)
