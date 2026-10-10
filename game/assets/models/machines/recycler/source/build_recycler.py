"""
The Scrap Recycler: a huge, permanent machine (8 x 8 x 4 m, 8x8x4 one-metre cells). A bed of counter-rotating
toothed rollers on top churns the items dropped on it; three output mouths along the front, the third welded shut.

Unlike the other families, the origin is the centre of the footprint on the floor (the scene's placementOffset puts
it there), so the model spans x, y in [-4, 4] and z in [0, 4]. +Y is the front (Godot -Z), as everywhere.

  recycler_body.glb     Housing, tray, grate, drives and the three mouths. Static: everything merges into Body, but
                        Belt1 / Belt2 (the open mouths' output belts, scrolling belt material) stay their own nodes.
                        Out1 / Out2 markers: where the open mouths drop items onto their belts.
  recycler_roller.glb   One toothed roller along X, origin on its axis. The scene places ROLLERS copies at
                        (0, ROLLER_Y[k], ROLLER_ZS[k]), every other one yawed 180 degrees so neighbours' teeth interleave.
                        They step down toward the middle, a valley that keeps items churning in the centre:
                        spin them so their tops turn toward the valley.
  recycler_broken.glb   The same housing wrecked: rollers missing, snapped and jammed, scrap heaped in the tray,
                        the open mouth plated over, lamps dead. Static.

Numbers the scenes rely on (keep them in step with Recycler.tscn / RecyclerBroken.tscn):
  tray inside   x in [-TRAY_X, TRAY_X], y in [-TRAY_Y, TRAY_Y], from the grate (TRAY_FLOOR) to the lip (HEIGHT)
  rollers       axis along X at z = ROLLER_ZS, y = ROLLER_Y; core radius CORE_R, tooth tips TOOTH_TIP
  mouths        at x = MOUTH_XS, each x +- OPEN_W / 2, z in [0, OPEN_Z1], y from HALF - OPEN_DEPTH out to the
                footprint edge; the first OPEN_MOUTHS are open, the rest plated shut
  output belts  along +Y from y = HALF - OPEN_DEPTH to HALF at each open mouth's x, top Structure.BeltTopHeight (0.15)
                off the floor, standard width: each ends on the footprint face, where a conveyor joins it
"""
import bpy, bmesh, math, random, os
from mathutils import Vector, Matrix

_HERE = (os.path.dirname(os.path.abspath(__file__)) if "__file__" in globals()
         else r"C:\Users\steph\OneDrive\Documents\godot\projects\gmp-framework\game\assets\models\machines\recycler\source")
_LIB = os.path.normpath(os.path.join(_HERE, "..", "..", "..", "shared", "salvage_lib.py"))
_S = {"__name__": "salvage_lib", "__file__": _LIB}
exec(compile(open(_LIB, encoding="utf-8").read(), _LIB, "exec"), _S)
_SKIP = {"HERE", "OUT_DIR", "PIECES", "build_all", "ICONS", "export", "CONV"}
globals().update({k: v for k, v in _S.items() if not k.startswith("__") and k not in _SKIP})
HERE = _HERE
OUT_DIR = os.path.dirname(HERE)

# ---------- dimensions (metres) ----------
HALF = 4.0                     # half the footprint
HEIGHT = 4.0
WALL = 3.9                     # outer face of the housing (the ribs stand proud of it, up to HALF)
TRAY_X, TRAY_Y = 3.3, 3.1
TRAY_FLOOR = 1.4
ROLLERS = 6
ROLLER_Y = [-2.5 + k * 1.0 for k in range(ROLLERS)]
ROLLER_ZS = [3.0, 2.55, 2.1, 2.1, 2.55, 3.0]   # a valley; the lowest tooth tips clear the grate bars (TRAY_FLOOR + 0.1)
ROLLER_HALF = 3.25             # half the roller's length
CORE_R = 0.32
TOOTH_TIP = 0.54
RING_STEP = 0.9
RING_X0 = -2.925               # chosen so a roller yawed 180 degrees has its rings exactly between this one's
MOUTH_XS = [-3.0, 0.0, 3.0]                             # centred on 2-cell ports
OPEN_MOUTHS = 2                                         # the first two run belts out; the third is plated shut
OPEN_W, OPEN_Z1, OPEN_DEPTH = 1.76, 1.3, 2.0            # each mouth fits a standard belt (1.68 m)

C_HULL = (0.36, 0.4, 0.31)    # olive drab plate
C_HULL2 = (0.25, 0.28, 0.22)

V = lambda x, y, z: Vector((x, y, z))

def stud(B, p, n, r=0.03, h=0.02, c=C_STEEL, rust=0.3):
    """Rivet or bolt head standing on a surface along n (16 tris)."""
    n = Vector(n).normalized()
    u = n.cross(ZV if abs(n.z) < 0.9 else Vector((1, 0, 0))).normalized(); w = n.cross(u)
    rim = [(u * math.cos(k / 6 * math.tau) + w * math.sin(k / 6 * math.tau)) * r for k in range(6)]
    v0 = [B.bm.verts.new(p + d) for d in rim]; v1 = [B.bm.verts.new(p + d + n * h) for d in rim]
    fs = [B.quad([v0[k], v0[(k + 1) % 6], v1[(k + 1) % 6], v1[k]], "steel") for k in range(6)] + [B.quad(v1, "steel")]
    B.paint(fs, c, 0.15, rust)

def louvre(B, c, n, w, h, slats=5, col=C_DARK):
    """Vent on a face centred at c facing n: dark recess with angled slats."""
    R = facing_basis(n)
    B.box(c, (w, h, 0.02), R, "metal", (0.02, 0.02, 0.02), 0.05, bevel=0)
    for k in range(slats):
        y = -h / 2 + h * (k + 0.5) / slats
        B.box(c + R @ Vector((0, y, 0.02)), (w - 0.04, 0.03, 0.03), R @ Matrix.Rotation(-0.5, 3, 'X'), "metal", col, 0.1, bevel=0)

# ======================================================================================
def roller(B, x0=-ROLLER_HALF, x1=ROLLER_HALF, rng=None, keep=1.0, rust=0.3):
    """Toothed crushing roller along X centred on the origin (or the part of it between x0 and x1): a core, toothed
    rings every RING_STEP, end flanges. keep < 1 drops teeth at random (broken)."""
    B.cyl(V(x0, 0, 0), V(x1, 0, 0), CORE_R, 16, "steel", C_STEEL, 0.15, rust)
    for j in range(8):
        x = RING_X0 + j * RING_STEP
        if not (x0 + 0.1 < x < x1 - 0.1):
            continue
        ring(B, V(x, 0, 0), (1, 0, 0), CORE_R - 0.02, CORE_R + 0.06, 0.2, 16, "metal", C_DARK, 0.15, rust)
        for k in range(5):
            if rng and rng.random() > keep:
                continue
            a = k / 5 * math.tau + j * 0.63
            R = Matrix.Rotation(a, 3, 'X')
            mid = (CORE_R + 0.04 + TOOTH_TIP) / 2
            B.box(R @ V(x, 0, mid), (0.17, 0.2, TOOTH_TIP - CORE_R - 0.04), R, "steel", C_WEAR, 0.2, rust * 1.4)
    for x in (x0, x1):
        if abs(abs(x) - ROLLER_HALF) < 1e-3:
            s = 1 if x > 0 else -1
            ring(B, V(x - s * 0.04, 0, 0), (1, 0, 0), 0.1, CORE_R + 0.03, 0.08, 16, "metal", C_YELLOW, 0.2, rust)
            B.cyl(V(x, 0, 0), V(x + s * 0.05, 0, 0), 0.12, 10, "steel", C_STEEL, 0.1, rust)

def build_roller():
    random.seed(901)
    coll = clear_collection("Recycler_Roller")
    B = Builder()
    roller(B)
    finish(B, "Roller", coll)
    return coll

# ======================================================================================
def housing(B, rng, broken=False):
    """Everything static: plinth, housing, tray and grate, walls and lip, ribs, drives, mouths, decor."""
    rust = 0.55 if broken else 0.25
    # plinth and the lower housing, with the mouths cut out of the front, down to the floor
    core_y1 = HALF - OPEN_DEPTH
    fy = (core_y1 + WALL) / 2
    B.box(V(0, (-HALF + core_y1) / 2, 0.15), (2 * HALF, HALF + core_y1, 0.3), I3, "metal", C_DARK, 0.15, rust)
    B.box(V(0, (-WALL + core_y1) / 2, (0.3 + TRAY_FLOOR) / 2), (2 * WALL, WALL + core_y1, TRAY_FLOOR - 0.3), I3, "metal", C_HULL, 0.2, rust)
    edges = [-HALF] + [e for x in MOUTH_XS for e in (x - OPEN_W / 2, x + OPEN_W / 2)] + [HALF]
    for a, b in zip(edges[0::2], edges[1::2]):                  # the front between the mouths
        if b - a < 0.2:                                         # the outer cheeks beside the end mouths
            B.box(V((a + b) / 2, (core_y1 + HALF) / 2, OPEN_Z1 / 2), (b - a, OPEN_DEPTH, OPEN_Z1), I3, "steel", C_YELLOW, 0.2, rust)
            continue
        B.box(V((a + b) / 2, (core_y1 + HALF) / 2, 0.15), (b - a, OPEN_DEPTH, 0.3), I3, "metal", C_DARK, 0.15, rust)
        B.box(V((a + b) / 2, fy, (0.3 + TRAY_FLOOR) / 2), (b - a, WALL - core_y1, TRAY_FLOOR - 0.3), I3, "metal", C_HULL, 0.2, rust)
    for x in MOUTH_XS:
        B.box(V(x, fy, (OPEN_Z1 + TRAY_FLOOR) / 2), (OPEN_W, WALL - core_y1, TRAY_FLOOR - OPEN_Z1), I3, "metal", C_HULL, 0.2, rust, bevel=0)

    # upper walls round the tray, and the lip
    wh = HEIGHT - TRAY_FLOOR
    zc = (TRAY_FLOOR + HEIGHT) / 2
    for s in (-1, 1):
        B.box(V(0, s * (TRAY_Y + WALL) / 2, zc), (2 * WALL, WALL - TRAY_Y, wh), I3, "metal", C_HULL, 0.2, rust)
        B.box(V(s * (TRAY_X + WALL) / 2, 0, zc), (WALL - TRAY_X, 2 * TRAY_Y, wh), I3, "metal", C_HULL, 0.2, rust)
        B.box(V(0, s * (TRAY_Y + 0.06), HEIGHT - 0.03), (2 * TRAY_X + 0.24, 0.12, 0.06), I3, "steel", C_WEAR, 0.15, rust)
        B.box(V(s * (TRAY_X + 0.06), 0, HEIGHT - 0.03), (0.12, 2 * TRAY_Y, 0.06), I3, "steel", C_WEAR, 0.15, rust)
    # inner faces of the lip: hazard bands
    for s in (-1, 1):
        hazard(B, V(-TRAY_X, s * TRAY_Y, HEIGHT - 0.32), (1, 0, 0), (0, 0, 1), 2 * TRAY_X, 0.25, (0, -s, 0), pitch=0.3)
        hazard(B, V(s * TRAY_X, -TRAY_Y, HEIGHT - 0.32), (0, 1, 0), (0, 0, 1), 2 * TRAY_Y, 0.25, (-s, 0, 0), pitch=0.3)

    # the grate: a black pit with bars across it
    B.box(V(0, 0, TRAY_FLOOR - 0.02), (2 * TRAY_X, 2 * TRAY_Y, 0.04), I3, "metal", (0.015, 0.015, 0.015), 0.05, bevel=0)
    for k in range(26):
        x = -TRAY_X + 0.13 + k * (2 * TRAY_X - 0.26) / 25
        B.box(V(x, 0, TRAY_FLOOR + 0.05), (0.06, 2 * TRAY_Y, 0.1), I3, "steel", C_STEEL, 0.15, rust, bevel=0)
    for y in (-TRAY_Y + 0.1, 0.0, TRAY_Y - 0.1):
        B.box(V(0, y, TRAY_FLOOR + 0.03), (2 * TRAY_X, 0.08, 0.06), I3, "metal", C_DARK, 0.1, rust, bevel=0)

    # roller bearings on the side walls' inner faces, drive motors and a chain guard on their outer faces
    for k, y in enumerate(ROLLER_Y):
        z = ROLLER_ZS[k]
        for s in (-1, 1):
            ring(B, V(s * (TRAY_X - 0.02), y, z), (1, 0, 0), 0.12, 0.26, 0.05, 12, "metal", C_DARK, 0.15, rust)
            if broken and s > 0 and k == 3:
                continue                                          # this motor was torn off
            B.cyl(V(s * (WALL - 0.02), y, z), V(s * (HALF - 0.04), y, z), 0.3, 14, "metal", C_BLUE, 0.2, rust)
            B.cyl(V(s * (HALF - 0.04), y, z), V(s * (HALF - 0.01), y, z), 0.2, 12, "steel", C_STEEL, 0.15, rust)
        if broken and k == 3:
            B.box(V(WALL + 0.005, y, z), (0.01, 0.62, 0.62), I3, "metal", (0.01, 0.01, 0.01), 0.05, bevel=0)
            for d in (-0.12, 0.1):
                B.pipe([V(WALL, y + d, z), V(HALF - 0.05, y + d * 2, z - 0.4), V(HALF - 0.08, y + d * 3, z - 1.1)], 0.025, 6)
    for s in (-1, 1):
        B.box(V(s * (WALL + 0.02), 0, min(ROLLER_ZS) - 0.45), (0.06, 2 * TRAY_Y, 0.16), I3, "metal", C_DARK, 0.15, rust)

    # corner posts and ribs, proud of the walls
    for sx in (-1, 1):
        for sy in (-1, 1):
            z0 = OPEN_Z1 + 0.2 if sy > 0 else 0.3                     # the end mouths take the front posts' feet
            B.box(V(sx * (HALF - 0.25), sy * (HALF - 0.25), (z0 + 4.0) / 2), (0.5, 0.5, 4.0 - z0), I3, "metal", C_HULL2, 0.2, rust)
            B.box(V(sx * (HALF - 0.25), sy * (HALF - 0.25), HEIGHT - 0.05), (0.54, 0.54, 0.1), I3, "steel", C_YELLOW, 0.2, rust)
    for t in (-1.5, 1.5):
        B.box(V(t, HALF - 0.05, 2.15), (0.3, 0.1, 3.7), I3, "metal", C_HULL2, 0.2, rust)          # front, between mouths
    for t in (-2.0, 0.0, 2.0):
        B.box(V(t, -HALF + 0.05, 2.15), (0.3, 0.1, 3.7), I3, "metal", C_HULL2, 0.2, rust)         # back
    for t in (-1.5, 1.5):
        for s in (-1, 1):
            B.box(V(s * (HALF - 0.05), t, 1.4), (0.1, 0.3, 2.2), I3, "metal", C_HULL2, 0.2, rust)  # sides, below the motors
    # rivet lines along the top of the walls
    for k in range(15):
        t = -3.5 + k * 0.5
        stud(B, V(t, -WALL, 3.75), (0, -1, 0), rust=rust)
        for s in (-1, 1):
            stud(B, V(s * WALL, t, 2.4), (s, 0, 0), rust=rust)
    # hazard band across the front over the mouths, and a white nameplate
    hazard(B, V(-WALL, WALL + 0.001, 2.45), (1, 0, 0), (0, 0, 1), 2 * WALL, 0.22, (0, 1, 0), pitch=0.35)
    panel_face(B, V(0.0, WALL + 0.02, 3.2), (0, 1, 0), 1.6, 0.5, C_WHITE)
    for k in range(3):
        B.box(V(-0.55 + k * 0.55, WALL + 0.045, 3.2), (0.4, 0.01, 0.07), I3, "panel", C_DARK, 0.1, bevel=0)

    # the mouths: a lintel, a dark back and rails beside the belt; the open ones get flaps and a lamp, the rest (and
    # all of them when broken) a welded plate
    for k, x in enumerate(MOUTH_XS):
        B.box(V(x, WALL + 0.06, OPEN_Z1 + 0.1), (OPEN_W + 0.24, 0.12, 0.2), I3, "steel", C_YELLOW, 0.2, rust)
        B.box(V(x, core_y1 + 0.01, OPEN_Z1 / 2), (OPEN_W, 0.02, OPEN_Z1), I3, "metal", (0.02, 0.02, 0.02), 0.05, bevel=0)
        for s in (-1, 1):
            B.box(V(x + s * (OPEN_W / 2 - 0.02), (core_y1 + HALF) / 2, 0.2), (0.04, OPEN_DEPTH, 0.14), I3, "steel", C_WEAR, 0.2, rust)
        if k < OPEN_MOUTHS and not broken:
            for s in (-1, 1):
                B.box(V(x + s * 0.3, core_y1 + 0.3, OPEN_Z1 - 0.2), (0.26, 0.02, 0.4), I3, "rubber", C_BLACK, 0.1)   # flap strips
            B.cyl(V(x, WALL + 0.12, OPEN_Z1 + 0.32), V(x, WALL + 0.12, OPEN_Z1 + 0.5), 0.09, 12, "metal", C_DARK)
            B.cyl(V(x, WALL + 0.12, OPEN_Z1 + 0.5), V(x, WALL + 0.12, OPEN_Z1 + 0.65), 0.08, 12, "glow", C_AMBER, 0.05)
            continue
        c = V(x, WALL + 0.035, OPEN_Z1 / 2)
        B.box(c, (OPEN_W, 0.05, OPEN_Z1), I3, "steel", C_STEEL, 0.25, rust + 0.3)
        for s in (-1, 1):
            B.box(c + V(0, 0.03, s * (OPEN_Z1 / 2 - 0.02)), (OPEN_W, 0.02, 0.03), I3, "steel", C_WEAR, 0.3, rust, bevel=0)   # weld beads
            B.box(c + V(0, 0.04, 0), (OPEN_W * 1.15, 0.02, 0.1), Matrix.Rotation(s * 0.6, 3, 'Y'), "metal", C_DARK, 0.2, rust)
        if broken and k < OPEN_MOUTHS:
            B.box(c + V(0.05, 0.06, 0.02), (OPEN_W + 0.1, 0.03, 0.5), Matrix.Rotation(0.18, 3, 'Y'), "steel", C_RUST, 0.3, 0.8)

    # back: vents and a pipe run
    for x in (-2.0, 2.0):
        louvre(B, V(x, -WALL - 0.012, 1.5), (0, -1, 0), 1.4, 1.0)
    for z in (2.85, 3.05):
        B.pipe([V(-3.6, -WALL - 0.12, z), V(3.6, -WALL - 0.12, z)], 0.07, 8, "steel", C_RUST if broken else C_STEEL)
    for x in (-3.0, -1.0, 1.0, 3.0):
        B.box(V(x, -WALL - 0.06, 2.95), (0.1, 0.12, 0.4), I3, "metal", C_DARK, 0.1, rust)

    # ladder up the -X side to the lip
    for s in (-1, 1):
        B.box(V(-HALF + 0.06, 2.6 + s * 0.25, 2.0), (0.06, 0.06, 4.0), I3, "steel", C_YELLOW, 0.2, rust)
    for k in range(13):
        B.cyl(V(-HALF + 0.06, 2.35, 0.3 + k * 0.3), V(-HALF + 0.06, 2.85, 0.3 + k * 0.3), 0.022, 6, "steel", C_STEEL, 0.15, rust)

    # warning beacons on the front corners
    for sx in (-1, 1):
        p = V(sx * (HALF - 0.25), HALF - 0.25, HEIGHT)
        B.cyl(p, p + V(0, 0, 0.08), 0.12, 12, "metal", C_DARK)
        B.cyl(p + V(0, 0, 0.08), p + V(0, 0, 0.22), 0.1, 12, "metal" if broken else "glow", C_DARK if broken else C_AMBER, 0.05)

def build_body():
    rng = random.Random(911); random.seed(911)
    coll = clear_collection("Recycler_Body")
    B = Builder()
    housing(B, rng)
    finish(B, "Body", coll)
    for k, x in enumerate(MOUTH_XS[:OPEN_MOUTHS]):
        belt = build_belt(Path(OPEN_DEPTH, straight_fn, 1), f"Belt{k + 1}", coll, OPEN_DEPTH)   # y -1..1 at belt height, as a conveyor
        belt.location = V(x, HALF - OPEN_DEPTH / 2, 1.0)
        marker(f"Out{k + 1}", coll, V(x, HALF - OPEN_DEPTH + 0.5, 0.65))
    return coll

def build_broken():
    rng = random.Random(921); random.seed(921)
    coll = clear_collection("Recycler_Broken")
    B = Builder()
    housing(B, rng, broken=True)
    # scrap heaped in the tray: lumps, bent plates and bars
    for k in range(14):
        p = V(rng.uniform(-2.8, 2.8), rng.uniform(-2.6, 2.6), rng.uniform(TRAY_FLOOR + 0.3, 2.6))
        rock(B, p, rng.uniform(0.18, 0.35), k * 1.3, 1, 0.35, "metal", lambda c, n: (0.2 + 0.1 * rng.random(), 0.12, 0.07))
    for k in range(10):
        a = Matrix.Rotation(rng.uniform(-0.6, 0.6), 3, 'X') @ Matrix.Rotation(rng.uniform(0, math.tau), 3, 'Z')
        p = V(rng.uniform(-2.8, 2.8), rng.uniform(-2.6, 2.6), rng.uniform(TRAY_FLOOR + 0.4, 2.7))
        B.box(p, (rng.uniform(0.5, 1.2), rng.uniform(0.3, 0.6), 0.03), a, "steel", C_RUST, 0.3, 0.8, bevel=0)
    # caution tape strung across the top between the corner posts
    for a, b in ((V(-3.75, 3.75, 4.05), V(3.75, -3.75, 3.85)), (V(-3.75, -3.75, 4.05), V(3.75, 3.75, 3.9))):
        d = (b - a); n = d.length; d.normalize()
        B.box((a + b) / 2, (n, 0.12, 0.005), Matrix.Rotation(math.atan2(d.y, d.x), 3, 'Z') @ Matrix.Rotation(-math.asin(d.z), 3, 'Y'), "tape", C_YELLOW, 0.2, bevel=0)
    finish(B, "Body", coll)
    # rollers: 0 and 4 gone, 1 dropped at one end, 2 snapped in two, 3 and 5 missing teeth
    def place(name, y, rot, x0=-ROLLER_HALF, x1=ROLLER_HALF, at=None, keep=0.6):
        R_ = Builder()
        roller(R_, x0, x1, rng, keep, rust=0.8)
        ob = node(R_, name, coll, at)
        ob.rotation_euler = rot
    place("Roller1", ROLLER_Y[1], (0, -0.12, 0.05), at=V(0, ROLLER_Y[1], ROLLER_ZS[1] - 0.3))
    place("Roller2a", ROLLER_Y[2], (0, 0.3, 0), -ROLLER_HALF, -0.4, at=V(0, ROLLER_Y[2], ROLLER_ZS[2] - 0.2))
    place("Roller2b", ROLLER_Y[2], (0.4, -0.35, 0.1), 0.2, ROLLER_HALF, at=V(0.2, ROLLER_Y[2], ROLLER_ZS[2] - 0.3))
    place("Roller3", ROLLER_Y[3], (0.5, 0, 0), at=V(0, ROLLER_Y[3], ROLLER_ZS[3]), keep=0.5)
    place("Roller5", ROLLER_Y[5], (1.2, 0, 0), at=V(0, ROLLER_Y[5], ROLLER_ZS[5]), keep=0.4)
    return coll

PIECES = [
    (build_body, "recycler_body.glb"),
    (build_roller, "recycler_roller.glb"),
    (build_broken, "recycler_broken.glb"),
]

def build_all(do_export=True):
    return build_family(PIECES, OUT_DIR, do_export)

if __name__ == "__main__":
    build_all(do_export=False)
