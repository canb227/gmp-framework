"""
Concept structures: proof-of-concept models (with looping "idle-loop" animations) for physics-driven machines.
Origin at the anchor cell centre, floor z = -1, front = +Y, as every other family.

  concept_gravity_inverter.glb      1x1x1  floor plate that flips gravity above it     Orbits, Field
  concept_tag_gate.glb              1x1x1  belt through a gate that opens per item tag  Belt, DoorL, DoorR
  concept_bounce_pad.glb            1x1x1  tunable rubber spring pad                    Pad, Springs, Dial
  concept_vortex_funnel.glb         2x2x1  spinning bowl, drops items out of its centre Bowl
  concept_tube_straight/bend/junction/receiver.glb  1x1x1 pneumatic tube network     Pulse, Flap, Bellows
  concept_heat_lamp.glb             1x1x1  belt under a heating lamp (tags HOT)          Belt, Cone, Coil
  concept_cryo_vent.glb             1x1x1  belt under a cold vent (tags COLD)            Belt, Cone, Fan
  concept_counterweight_elevator.glb 1x1x3 two cages on one cable over a pulley          CageA, CageB, Pulley
  concept_rail_gun.glb              1x4x1  metal-only coil launcher                      Sled, Charge
  concept_tipping_bucket.glb        1x1x2  seesaw bucket that alternates left / right    Bucket
  concept_assembly_chamber.glb      3x3x3  zero-g assembly cube (anchored on its centre)  Field, Parts, Emitters
  concept_screw_elevator.glb        1x1x3  Archimedes screw in a glass tube              Screw
  concept_platform_elevator.glb     1x2x3  paternoster: level platforms on a chain loop  Platform0-5, Sprockets
"""
import bpy, bmesh, math, random, os
from mathutils import Vector, Matrix

_HERE = (os.path.dirname(os.path.abspath(__file__)) if "__file__" in globals()
         else r"C:\Users\steph\OneDrive\Documents\godot\projects\gmp-framework\game\assets\models\machines\concepts\source")
_LIB = os.path.normpath(os.path.join(_HERE, "..", "..", "..", "shared", "salvage_lib.py"))
_S = {"__name__": "salvage_lib", "__file__": _LIB}
exec(compile(open(_LIB, encoding="utf-8").read(), _LIB, "exec"), _S)
_SKIP = {"HERE", "OUT_DIR", "PIECES", "build_all", "ICONS", "export", "CONV"}
globals().update({k: v for k, v in _S.items() if not k.startswith("__") and k not in _SKIP})
HERE = _HERE
OUT_DIR = os.path.dirname(HERE)

def disc_ring(c, r, n=24, axis='Z'):
    pts = []
    for k in range(n):
        a = k / n * math.tau
        v = {'Z': Vector((math.cos(a) * r, math.sin(a) * r, 0)), 'Y': Vector((math.cos(a) * r, 0, math.sin(a) * r)),
             'X': Vector((0, math.cos(a) * r, math.sin(a) * r))}[axis]
        pts.append(c + v)
    return pts

def glass_tube(B, p0, p1, r, n=24):
    ax = (p1 - p0).normalized()
    ref = Vector((0, 0, 1)) if abs(ax.z) < 0.9 else Vector((1, 0, 0))
    u = ax.cross(ref).normalized(); w = ax.cross(u)
    ring_ = lambda p: [p + (u * math.cos(k / n * math.tau) + w * math.sin(k / n * math.tau)) * r for k in range(n)]
    B.tube_rings([ring_(p0), ring_(p1)], "glass", (0.55, 0.9, 1.0), 0.02, 0.0, cap=False, smooth=True)

def helix(B, z0, z1, turns, r0, r1, thick, steps, mat, c, phase=0.0):
    """Screw flight: a thin helical ribbon between radii r0 and r1."""
    bm = B.bm
    rows = []
    for i in range(steps + 1):
        t = i / steps
        a = phase + t * turns * math.tau
        z = z0 + (z1 - z0) * t
        d = Vector((math.cos(a), math.sin(a), 0))
        rows.append([bm.verts.new(d * r + Vector((0, 0, z + dz))) for r, dz in ((r0, 0), (r1, 0), (r1, thick), (r0, thick))])
    fs = []
    for i in range(steps):
        a, b = rows[i], rows[i + 1]
        for k in range(4):
            f = bm.faces.new((a[k], a[(k + 1) % 4], b[(k + 1) % 4], b[k])); f.material_index = MI[mat]; f.smooth = True
            fs.append(f)
    bmesh.ops.recalc_face_normals(bm, faces=fs)
    B.paint(fs, c, 0.15)

def belt_cell(coll, rng, B, guards=True):
    path = PATHS["straight"]
    build_belt(path, "Belt", coll, 2.0)
    for side in (-1, 1):
        build_side(B, path, side, rng, guards=guards)
    build_cable(B, path)
    return path

# ======================================================================================
def build_gravity_inverter():
    rng = random.Random(901); random.seed(901)
    coll = clear_collection("Concept_GravityInverter")
    B = Builder()
    B.box(Vector((0, 0, -0.95)), (1.94, 1.94, 0.1), I3, "metal", C_FRAME, 0.2, rust=0.3)
    B.box(Vector((0, 0, -0.895)), (1.5, 1.5, 0.02), I3, "metal", (0.05, 0.05, 0.06), 0.1)
    for k in range(5):
        o = -0.6 + k * 0.3
        B.box(Vector((o, 0, -0.883)), (0.02, 1.4, 0.004), I3, "violet", C_VIOLET, 0.05)
        B.box(Vector((0, o, -0.883)), (1.4, 0.02, 0.004), I3, "violet", C_VIOLET, 0.05)
    for (org, u, n) in ((Vector((-0.85, -0.92, -0.9)), (1, 0, 0), (0, -1, 0)), (Vector((0.85, 0.92, -0.9)), (-1, 0, 0), (0, 1, 0)),
                        (Vector((0.92, -0.85, -0.9)), (0, 1, 0), (1, 0, 0)), (Vector((-0.92, 0.85, -0.9)), (0, -1, 0), (-1, 0, 0))):
        hazard(B, org, u, (0, 0, 1), 1.7, 0.08, n, pitch=0.1)
    for (x, y) in ((-0.82, -0.82), (0.82, -0.82), (0.82, 0.82), (-0.82, 0.82)):
        B.cyl(Vector((x, y, -0.9)), Vector((x, y, -0.15)), 0.07, 12, "metal", C_DARK, 0.15)
        B.cyl(Vector((x, y, -0.7)), Vector((x, y, -0.35)), 0.075, 12, "panel", C_WHITE, 0.3)
        ring(B, Vector((x, y, -0.2)), (0, 0, 1), 0.07, 0.1, 0.04, 12, "violet", C_VIOLET, 0.05)
        B.cyl(Vector((x, y, -0.15)), Vector((x, y, -0.1)), 0.05, 12, "violet", C_VIOLET, 0.05)
    for s in (-1, 1):                                                        # "up" chevrons painted on the plate
        vprism(B, [(-0.25, s * 0.25 - 0.15), (0.25, s * 0.25 - 0.15), (0.0, s * 0.25 + 0.15)], -0.886, -0.88, "panel", C_YELLOW, 0.2)
    cb = Vector((0.82, -0.82, -0.55))
    B.box(cb + Vector((0, -0.1, 0)), (0.14, 0.06, 0.18), I3, "panel", C_WHITE, 0.3)
    B.box(cb + Vector((0, -0.132, 0.04)), (0.05, 0.004, 0.03), I3, "violet", C_VIOLET, 0.05)
    finish(B, "Frame", coll)
    O = Builder()
    for k in range(4):
        a = k / 4 * math.tau
        p = Vector((math.cos(a) * 0.55, math.sin(a) * 0.55, 0))
        rock(O, p, 0.06, 40 + k, 1, 0.1, "violet", lambda q, n: C_VIOLET)
    orb = node(O, "Orbits", coll, Vector((0, 0, -0.45)))
    spin(orb, 2, 1, 90)
    cycle(orb, "location", [Vector((0, 0, -0.45)), Vector((0, 0, 0.35))], 90)
    F = Builder()
    F.box(Vector((0, 0, 0.9)), (1.8, 1.8, 1.8), I3, "field_ag", C_VIOLET, 0.02)
    fld = node(F, "Field", coll, Vector((0, 0, -0.9)))
    cycle(fld, "scale", [Vector((1, 1, 1)), Vector((0.96, 0.96, 1.04))], 90)
    return coll

# ======================================================================================
def build_tag_gate():
    rng = random.Random(911); random.seed(911)
    coll = clear_collection("Concept_TagGate")
    B = Builder()
    belt_cell(coll, rng, B)
    top = 0.45
    for sx in (-1, 1):
        B.box(Vector((sx * 0.93, 0, (BELT_TOP + top) / 2)), (0.12, 0.22, top - BELT_TOP), I3, "metal", C_DARK, 0.15)
        panel_face(B, Vector((sx * 0.93, -0.112, -0.2)), (0, -1, 0), 0.1, 1.0, C_WHITE, seam=False)
        B.box(Vector((sx * 0.865, 0, -0.2)), (0.01, 0.12, 1.1), I3, "cyan", C_CYAN, 0.05)          # detector strips
    B.box(Vector((0, 0, top + 0.08)), (1.98, 0.24, 0.16), I3, "metal", C_DARK, 0.15)
    panel_face(B, Vector((0, -0.121, top + 0.08)), (0, -1, 0), 1.7, 0.14, C_WHITE, seam=False)
    for k, col in enumerate((C_AMBER, C_CYAN, (1.0, 0.15, 0.1), C_COPPER)):                         # tag lamps
        B.box(Vector((-0.45 + k * 0.3, -0.125, top + 0.08)), (0.16, 0.01, 0.1), I3, "glow" if k != 1 else "cyan", col, 0.05)
    B.box(Vector((0, 0.0, top - 0.02)), (1.6, 0.06, 0.03), I3, "cyan", C_CYAN, 0.05)                 # scan bar
    finish(B, "Frame", coll)
    for name, sx in (("DoorL", -1), ("DoorR", 1)):
        D = Builder()
        D.box(Vector((-sx * 0.42, 0, 0.45)), (0.84, 0.04, 0.9), I3, "panel", C_WHITE, 0.3)
        D.box(Vector((-sx * 0.42, 0.021, 0.55)), (0.5, 0.004, 0.25), I3, "glass", (0.55, 0.9, 1.0), 0.02)
        hazard(D, Vector((-sx * 0.84 if sx > 0 else 0.0, -0.021, 0.02)), (1, 0, 0), (0, 0, 1), 0.84, 0.1, (0, -1, 0), pitch=0.08)
        D.cyl(Vector((0, 0, 0)), Vector((0, 0, 0.92)), 0.025, 8, "steel", C_STEEL)
        door = node(D, name, coll, Vector((sx * 0.86, 0, BELT_TOP + 0.02)))
        keys(door, (1, 30, 42, 90, 102, 121), "rotation_euler",
             [(0, 0, 0), (0, 0, 0), (0, 0, -sx * math.radians(80)), (0, 0, -sx * math.radians(80)), (0, 0, 0), (0, 0, 0)])
    return coll

# ======================================================================================
def build_bounce_pad():
    random.seed(921)
    coll = clear_collection("Concept_BouncePad")
    B = Builder()
    B.box(Vector((0, 0, -0.8)), (1.9, 1.9, 0.4), I3, "metal", C_FRAME, 0.2, rust=0.3)
    for n in (Vector((1, 0, 0)), Vector((-1, 0, 0)), Vector((0, 1, 0)), Vector((0, -1, 0))):
        panel_face(B, n * 0.955 + Vector((0, 0, -0.8)), n, 1.6, 0.3, C_WHITE)
    hazard(B, Vector((-0.8, -0.96, -0.98)), (1, 0, 0), (0, 0, 1), 1.6, 0.06, (0, -1, 0), pitch=0.08)
    B.box(Vector((0, 0, -0.6)), (1.7, 1.7, 0.02), I3, "steel", C_STEEL, 0.2, rust=0.4)
    dial = Vector((0.7, -0.96, -0.78))
    B.cyl(dial, dial + Vector((0, -0.02, 0)), 0.1, 16, "metal", C_DARK)
    B.cyl(dial + Vector((0, -0.02, 0)), dial + Vector((0, -0.025, 0)), 0.085, 16, "panel", (0.85, 0.84, 0.8), 0.1)
    finish(B, "Frame", coll)
    Sp = Builder()
    for (x, y) in ((-0.55, -0.55), (0.55, -0.55), (0.55, 0.55), (-0.55, 0.55)):
        pts = [Vector((x + math.cos(t) * 0.12, y + math.sin(t) * 0.12, t / (6 * math.tau) * 0.25)) for t in [i * 0.35 for i in range(int(6 * math.tau / 0.35) + 1)]]
        Sp.pipe(pts, 0.018, 6, "steel", (0.55, 0.55, 0.55))
    springs = node(Sp, "Springs", coll, Vector((0, 0, -0.59)))
    P = Builder()
    P.box(Vector((0, 0, 0)), (1.7, 1.7, 0.06), I3, "steel", C_STEEL, 0.2, rust=0.3)
    P.box(Vector((0, 0, 0.06)), (1.6, 1.6, 0.06), I3, "rubber", C_BLACK, 0.1)
    for (org, u, n) in ((Vector((-0.85, -0.851, -0.03)), (1, 0, 0), (0, -1, 0)), (Vector((0.85, 0.851, -0.03)), (-1, 0, 0), (0, 1, 0))):
        hazard(P, org, u, (0, 0, 1), 1.7, 0.06, n, pitch=0.08)
    P.box(Vector((0, 0, 0.092)), (0.5, 0.5, 0.004), I3, "panel", C_YELLOW, 0.2)
    pad = node(P, "Pad", coll, Vector((0, 0, -0.3)))
    fr = (1, 40, 46, 56, 66, 91)
    keys(pad, fr, "location", [Vector((0, 0, -0.3)), Vector((0, 0, -0.38)), Vector((0, 0, -0.14)), Vector((0, 0, -0.33)),
                                Vector((0, 0, -0.29)), Vector((0, 0, -0.3))])
    keys(springs, fr, "scale", [Vector((1, 1, 1)), Vector((1, 1, 0.7)), Vector((1, 1, 1.65)), Vector((1, 1, 0.88)),
                                 Vector((1, 1, 1.04)), Vector((1, 1, 1))])
    N = Builder()
    N.box(Vector((0.035, 0, 0)), (0.07, 0.004, 0.01), I3, "glow", (1.0, 0.15, 0.1), 0.05)
    needle = node(N, "Dial", coll, dial + Vector((0, -0.03, 0)))
    cycle(needle, "rotation_euler", [(0, 0.4, 0), (0, -1.2, 0)], 90)
    return coll

# ======================================================================================
def build_vortex():
    random.seed(931)
    coll = clear_collection("Concept_VortexFunnel")
    C_ = Vector((1, 1, 0))
    B = Builder()
    for (x, y) in ((-0.9, -0.9), (2.9, -0.9), (2.9, 2.9), (-0.9, 2.9)):
        B.box(Vector((x, y, -0.1)), (0.12, 0.12, 1.8), I3, "metal", C_FRAME, 0.2, rust=0.3)
    ring(B, C_ + Vector((0, 0, 0.85)), (0, 0, 1), 1.88, 1.97, 0.1, 48, "metal", C_DARK, 0.2)
    ring(B, C_ + Vector((0, 0, 0.9)), (0, 0, 1), 1.9, 1.95, 0.02, 48, "cyan", C_CYAN, 0.05)
    for (a, b) in (((-0.9, -0.9), (2.9, -0.9)), ((2.9, -0.9), (2.9, 2.9)), ((2.9, 2.9), (-0.9, 2.9)), ((-0.9, 2.9), (-0.9, -0.9))):
        B.box(Vector(((a[0] + b[0]) / 2, (a[1] + b[1]) / 2, 0.82)), (abs(b[0] - a[0]) + 0.12, abs(b[1] - a[1]) + 0.12, 0.08), I3, "metal", C_DARK, 0.2)
    B.cyl(C_ + Vector((0, 0, -1.0)), C_ + Vector((0, 0, -0.45)), 0.42, 20, "metal", C_DARK, 0.15)            # outlet sleeve
    B.box(C_ + Vector((1.0, 0, -0.75)), (0.5, 0.4, 0.4), I3, "metal", C_BLUE, 0.25, rust=0.15)               # drive motor
    B.pipe([C_ + Vector((0.75, 0, -0.7)), C_ + Vector((0.45, 0, -0.55))], 0.05, 8, "steel", C_STEEL)
    hazard(B, Vector((-0.8, -0.961, -0.98)), (1, 0, 0), (0, 0, 1), 3.6, 0.08, (0, -1, 0), pitch=0.12)
    finish(B, "Frame", coll)
    Bw = Builder()
    rs = [(1.85, 0.85), (1.7, 0.72), (1.3, 0.35), (0.85, -0.05), (0.45, -0.4)]
    Bw.tube_rings([disc_ring(Vector((0, 0, z)), r, 40) for r, z in rs], "steel", L["C_WEAR"], 0.12, 0.1, cap=False, smooth=True)
    Bw.tube_rings([disc_ring(Vector((0, 0, z - 0.07)), r + 0.03, 40) for r, z in rs][::-1], "metal", C_DARK, 0.12, 0.1, cap=False, smooth=True)
    for k in range(6):                                                       # spiral vanes
        pts = []
        for i in range(9):
            t = i / 8
            r = 1.8 - 1.3 * t
            a = k / 6 * math.tau + t * 2.2
            z = 0.84 - 1.2 * t - 0.02 + 0.03
            pts.append(Vector((math.cos(a) * r, math.sin(a) * r, max(z, -0.37 + 0.03))))
        Bw.pipe(pts, 0.03, 5, "panel", C_YELLOW if k % 2 else C_DARK)
    ring(Bw, Vector((0, 0, -0.4)), (0, 0, 1), 0.42, 0.48, 0.05, 24, "cyan", C_CYAN, 0.05)
    bowl = node(Bw, "Bowl", coll, C_)
    spin(bowl, 2, 1, 60)
    return coll

# ======================================================================================
TUBE_R = 0.72
def tube_collars(B, pts_dirs):
    for p, d in pts_dirs:
        ring(B, p, d, TUBE_R, TUBE_R + 0.12, 0.12, 28, "metal", C_DARK, 0.15, rust=0.2)
        ring(B, p + Vector(d) * 0.07, d, TUBE_R + 0.01, TUBE_R + 0.06, 0.02, 28, "cyan", C_CYAN, 0.05)

def tube_leg(B, p):
    B.box(Vector((p.x, p.y, (-1 + p.z - TUBE_R) / 2)), (0.12, 0.12, p.z - TUBE_R + 1), I3, "metal", C_FRAME, 0.2, rust=0.3)
    B.box(Vector((p.x, p.y, p.z - TUBE_R - 0.02)), (1.4, 0.16, 0.06), I3, "metal", C_DARK, 0.2)
    B.box(Vector((p.x, p.y, -0.99)), (0.5, 0.4, 0.02), I3, "steel", C_STEEL, 0.2, rust=0.5)

def pulse_node(coll, name, path_pts, length=30):
    """A glowing ring that races along the tube (location + orientation keyed along path_pts)."""
    P = Builder()
    ring(P, Vector((0, 0, 0)), (0, 1, 0), TUBE_R - 0.08, TUBE_R - 0.02, 0.05, 28, "cyan", C_CYAN, 0.05)
    ob = node(P, name, coll, path_pts[0][0])
    frames = [1 + round(length * i / (len(path_pts) - 1)) for i in range(len(path_pts))]
    keys(ob, frames, "location", [p for p, r in path_pts], linear=True)
    keys(ob, frames, "rotation_euler", [r for p, r in path_pts], linear=True)
    return ob

TUBE_Z = 0.0
def build_tube_straight():
    random.seed(941)
    coll = clear_collection("Concept_TubeStraight")
    B, G = Builder(), Builder()
    glass_tube(G, Vector((0, -0.98, TUBE_Z)), Vector((0, 0.98, TUBE_Z)), TUBE_R)
    tube_collars(B, [(Vector((0, -0.92, TUBE_Z)), (0, 1, 0)), (Vector((0, 0.92, TUBE_Z)), (0, 1, 0))])
    ring(B, Vector((0, 0, TUBE_Z)), (0, 1, 0), TUBE_R, TUBE_R + 0.1, 0.3, 28, "panel", C_WHITE, 0.3)       # booster band
    B.box(Vector((0, 0, TUBE_Z + TUBE_R + 0.14)), (0.3, 0.24, 0.14), I3, "metal", C_BLUE, 0.2)
    B.box(Vector((0.08, -0.13, TUBE_Z + TUBE_R + 0.16)), (0.05, 0.004, 0.03), I3, "cyan", C_CYAN, 0.05)
    tube_leg(B, Vector((0, 0, TUBE_Z)))
    finish(B, "Frame", coll); finish(G, "Glass", coll)
    pulse_node(coll, "Pulse", [(Vector((0, -0.9, TUBE_Z)), (0, 0, 0)), (Vector((0, 0.9, TUBE_Z)), (0, 0, 0))])
    return coll

def build_tube_bend():
    random.seed(943)
    coll = clear_collection("Concept_TubeBend")
    B, G = Builder(), Builder()
    piv = Vector((1, -1, TUBE_Z))
    n = 12
    rings_ = []
    for i in range(n + 1):
        th = math.pi / 2 * i / n
        c = piv + Vector((-math.cos(th), math.sin(th), 0))
        t = Vector((math.sin(th), math.cos(th), 0))
        u = Vector((0, 0, 1)); w = t.cross(u)
        rings_.append([c + (w * math.cos(k / 24 * math.tau) + u * math.sin(k / 24 * math.tau)) * TUBE_R for k in range(24)])
    G.tube_rings(rings_, "glass", (0.55, 0.9, 1.0), 0.02, 0.0, cap=False, smooth=True)
    tube_collars(B, [(Vector((0, -0.92, TUBE_Z)), (0, 1, 0)), (Vector((0.92, 0, TUBE_Z)), (1, 0, 0))])
    mid = piv + Vector((-math.cos(math.pi / 4), math.sin(math.pi / 4), 0))
    ring(B, mid, (1, 1, 0), TUBE_R, TUBE_R + 0.1, 0.25, 28, "panel", C_WHITE, 0.3)
    tube_leg(B, mid)
    finish(B, "Frame", coll); finish(G, "Glass", coll)
    pts = []
    for i in range(7):
        th = math.pi / 2 * i / 6
        pts.append((piv + Vector((-math.cos(th), math.sin(th), 0)), (0, 0, -th)))
    pulse_node(coll, "Pulse", pts)
    return coll

def build_tube_junction():
    random.seed(945)
    coll = clear_collection("Concept_TubeJunction")
    B, G = Builder(), Builder()
    glass_tube(G, Vector((0, -0.98, TUBE_Z)), Vector((0, 0.98, TUBE_Z)), TUBE_R)
    glass_tube(G, Vector((0.3, 0, TUBE_Z)), Vector((0.98, 0, TUBE_Z)), TUBE_R * 0.95)
    tube_collars(B, [(Vector((0, -0.92, TUBE_Z)), (0, 1, 0)), (Vector((0, 0.92, TUBE_Z)), (0, 1, 0)), (Vector((0.92, 0, TUBE_Z)), (1, 0, 0))])
    B.box(Vector((0, 0, TUBE_Z + TUBE_R + 0.12)), (0.6, 0.6, 0.18), I3, "metal", C_DARK, 0.15)              # switch housing
    panel_face(B, Vector((0, -0.301, TUBE_Z + TUBE_R + 0.12)), (0, -1, 0), 0.5, 0.14, C_WHITE, seam=False)
    B.box(Vector((0.15, -0.305, TUBE_Z + TUBE_R + 0.12)), (0.06, 0.004, 0.04), I3, "glow", C_AMBER, 0.05)
    B.box(Vector((-0.15, -0.305, TUBE_Z + TUBE_R + 0.12)), (0.06, 0.004, 0.04), I3, "cyan", C_CYAN, 0.05)
    B.cyl(Vector((0, 0, TUBE_Z + TUBE_R)), Vector((0, 0, TUBE_Z + TUBE_R + 0.05)), 0.05, 10, "steel", C_STEEL)
    tube_leg(B, Vector((-0.2, 0, TUBE_Z)))
    finish(B, "Frame", coll); finish(G, "Glass", coll)
    Fl = Builder()                                                           # diverter flap, hinged at the centre
    Fl.box(Vector((0, 0.35, 0)), (0.04, 0.7, 1.2), I3, "panel", C_WHITE, 0.3)
    Fl.box(Vector((0.021, 0.35, 0)), (0.004, 0.6, 0.12), I3, "panel", C_YELLOW, 0.2)
    Fl.cyl(Vector((0, 0, -0.6)), Vector((0, 0, 0.6)), 0.03, 8, "steel", C_STEEL)
    flap = node(Fl, "Flap", coll, Vector((0, 0, TUBE_Z)))
    keys(flap, (1, 30, 40, 80, 90, 121), "rotation_euler",
         [(0, 0, 0), (0, 0, 0), (0, 0, -0.75), (0, 0, -0.75), (0, 0, 0), (0, 0, 0)])
    pulse_node(coll, "Pulse", [(Vector((0, -0.9, TUBE_Z)), (0, 0, 0)), (Vector((0, 0.9, TUBE_Z)), (0, 0, 0))])
    return coll

def build_tube_receiver():
    random.seed(947)
    coll = clear_collection("Concept_TubeReceiver")
    B, G = Builder(), Builder()
    glass_tube(G, Vector((0, -0.98, TUBE_Z)), Vector((0, -0.25, TUBE_Z)), TUBE_R)
    tube_collars(B, [(Vector((0, -0.92, TUBE_Z)), (0, 1, 0))])
    # hood the tube empties into, dropping items down a slide onto a belt in front at belt height
    B.box(Vector((0, 0.1, -0.025)), (1.8, 0.7, 1.95), I3, "metal", C_FRAME, 0.2, rust=0.3)
    for n in (Vector((1, 0, 0)), Vector((-1, 0, 0))):
        panel_face(B, Vector((n.x * 0.905, 0.1, 0.1)), n, 0.6, 1.5, C_WHITE)
    B.box(Vector((0, 0.5, -0.6)), (1.4, 0.04, 0.5), I3, "metal", (0.02, 0.02, 0.02), 0.05)                 # mouth
    for sx in (-1, 1):
        B.box(Vector((sx * 0.74, 0.52, -0.55)), (0.08, 0.08, 0.66), I3, "steel", C_CYAN, 0.1)
    B.box(Vector((0, 0.52, -0.2)), (1.56, 0.08, 0.08), I3, "steel", C_CYAN, 0.1)
    B.box(Vector((0, 0.75, -0.83)), (1.4, 0.5, 0.03), Matrix.Rotation(math.radians(-12), 3, 'X'), "steel", L["C_WEAR"], 0.15)
    hazard(B, Vector((-0.9, 0.451, 0.95)), (1, 0, 0), (0, 0, 1), 1.8, 0.1, (0, 1, 0), pitch=0.1)
    finish(B, "Frame", coll); finish(G, "Glass", coll)
    Be = Builder()                                                           # bellows on top: the pump
    for k in range(5):
        Be.box(Vector((0, 0, k * 0.07)), (0.7 - (k % 2) * 0.08, 0.5 - (k % 2) * 0.06, 0.05), I3, "rubber", C_BLACK, 0.1)
    Be.box(Vector((0, 0, 0.36)), (0.74, 0.54, 0.05), I3, "metal", C_DARK, 0.15)
    bel = node(Be, "Bellows", coll, Vector((0, 0.1, 0.95)))
    cycle(bel, "scale", [Vector((1, 1, 1)), Vector((1, 1, 0.55))], 40)
    return coll

# ======================================================================================
def emitter_cell(name, kind):
    rng = random.Random(951 if kind == "heat" else 953); random.seed(rng.random())
    coll = clear_collection(name)
    B = Builder()
    belt_cell(coll, rng, B)
    for sx in (-1, 1):
        B.box(Vector((sx * 0.96, 0, (BELT_TOP + 0.62) / 2)), (0.07, 0.14, 0.62 - BELT_TOP), I3, "metal", C_DARK, 0.15)
    B.box(Vector((0, 0, 0.62)), (1.98, 0.16, 0.1), I3, "metal", C_DARK, 0.15)
    hazard(B, Vector((-0.99, -0.081, 0.58)), (1, 0, 0), (0, 0, 1), 1.98, 0.06, (0, -1, 0), pitch=0.1)
    head = Vector((0, 0, 0.42))
    hood = lambda z, r: disc_ring(head + Vector((0, 0, z)), r, 28)
    if kind == "heat":
        B.tube_rings([hood(0.15, 0.2), hood(0.05, 0.32), hood(-0.1, 0.42)], "steel", (0.7, 0.68, 0.62), 0.1, 0.0, cap=False, smooth=True)
        B.cyl(head + Vector((0, 0, 0.15)), head + Vector((0, 0, 0.22)), 0.2, 20, "metal", C_DARK, 0.15)
        B.box(head + Vector((0, 0, 0.2)), (0.5, 0.1, 0.1), I3, "metal", C_DARK, 0.15)
        B.pipe([head + Vector((0.2, 0, 0.2)), Vector((0.6, 0, 0.64)), Vector((0.9, 0, 0.66))], 0.02, 6)
        tank = None
    else:
        B.box(head + Vector((0, 0, 0.05)), (0.7, 0.5, 0.3), I3, "panel", C_WHITE, 0.3)
        B.box(head + Vector((0, 0, -0.11)), (0.62, 0.42, 0.02), I3, "metal", (0.08, 0.1, 0.12), 0.1)
        for k in range(5):
            B.box(head + Vector((-0.24 + k * 0.12, 0, -0.125)), (0.03, 0.4, 0.012), I3, "cyan", (0.6, 0.85, 1.0), 0.05)
        tank = Vector((-0.75, 0.0, 0.3))
        B.cyl(tank, tank + Vector((0, 0, 0.28)), 0.1, 14, "panel", (0.75, 0.8, 0.85), 0.1)
        ring(B, tank + Vector((0, 0, 0.14)), (0, 0, 1), 0.1, 0.105, 0.03, 14, "cyan", C_CYAN, 0.05)
        B.pipe([tank + Vector((0.05, 0, 0.28)), head + Vector((-0.2, 0, 0.3)), head + Vector((-0.2, 0, 0.2))], 0.02, 6, "steel", C_STEEL)
    finish(B, "Frame", coll)
    Cn = Builder()
    cone = lambda z, r: disc_ring(Vector((0, 0, z)), r, 28)
    Cn.tube_rings([cone(0.0, 0.3), cone(-1.15, 0.85)], "field_heat" if kind == "heat" else "field_cold",
                  (1.0, 0.45, 0.1) if kind == "heat" else (0.6, 0.85, 1.0), 0.02, 0.0, cap=False, smooth=True)
    cn = node(Cn, "Cone", coll, head + Vector((0, 0, -0.1)))
    cycle(cn, "scale", [Vector((1, 1, 1)), Vector((1.08, 1.08, 0.97))], 60)
    if kind == "heat":
        Co = Builder()
        for k in range(3):
            ring(Co, Vector((0, 0, -0.02 * k)), (0, 0, 1), 0.08 + k * 0.08, 0.1 + k * 0.08, 0.02, 24, "molten", C_MOLTEN, 0.05)
        co = node(Co, "Coil", coll, head + Vector((0, 0, 0.0)))
        cycle(co, "scale", [Vector((1, 1, 1)), Vector((1.12, 1.12, 1))], 30)
    else:
        Fn = Builder()
        for k in range(5):
            a = k / 5 * math.tau
            Fn.box(Vector((math.cos(a) * 0.1, math.sin(a) * 0.1, 0)), (0.16, 0.05, 0.01), Matrix.Rotation(a, 3, 'Z') @ Matrix.Rotation(0.4, 3, 'X'), "metal", (0.2, 0.2, 0.21), 0.1)
        Fn.cyl(Vector((0, 0, -0.01)), Vector((0, 0, 0.01)), 0.04, 10, "metal", C_DARK)
        fn = node(Fn, "Fan", coll, head + Vector((0, 0.0, 0.215)))
        spin(fn, 2, 4, 60)
    return coll

def build_heat_lamp():
    return emitter_cell("Concept_HeatLamp", "heat")

def build_cryo_vent():
    return emitter_cell("Concept_CryoVent", "cold")

# ======================================================================================
def build_counterweight_elevator():
    random.seed(961)
    coll = clear_collection("Concept_CounterweightElevator")
    B = Builder()
    top = 4.9
    for (x, y) in ((-0.92, -0.92), (0.92, -0.92), (0.92, 0.92), (-0.92, 0.92)):
        B.box(Vector((x, y, (top - 1) / 2)), (0.1, 0.1, top + 1), I3, "metal", C_FRAME, 0.2, rust=0.3)
    for z in (1.0, 3.0):
        for sx in (-1, 1):
            B.cyl(Vector((sx * 0.92, -0.92, z - 0.9)), Vector((sx * 0.92, 0.92, z + 0.9)), 0.022, 6, "steel", C_STEEL, rust=0.5)
    B.box(Vector((0, 0, top)), (1.98, 1.98, 0.14), I3, "metal", C_DARK, 0.15)
    for sx in (-1, 1):
        B.box(Vector((sx * 0.45, 0, top - 0.3)), (0.08, 0.3, 0.5), I3, "metal", C_DARK, 0.15)                 # pulley cheeks
    for x in (-0.45, 0.45):                                                  # cables the cages ride on
        for y in (-0.08, 0.08):
            B.cyl(Vector((x + (0.45 if x < 0 else -0.45) * 0.0, y, -0.95)), Vector((x, y, top - 0.35)), 0.012, 5, "steel", (0.2, 0.2, 0.2))
    B.box(Vector((0, 0, -0.97)), (1.98, 1.98, 0.06), I3, "steel", C_STEEL, 0.25, rust=0.6)
    hazard(B, Vector((-0.9, -0.99, -0.95)), (1, 0, 0), (0, 0, 1), 1.8, 0.08, (0, -1, 0), pitch=0.1)
    finish(B, "Frame", coll)
    Pu = Builder()
    ring(Pu, Vector((0, 0, 0)), (1, 0, 0), 0.3, 0.46, 0.14, 32, "steel", (0.3, 0.3, 0.31), 0.2, rust=0.4)
    for k in range(6):
        a = k / 6 * math.tau
        Pu.box(Vector((0, math.cos(a), math.sin(a))) * 0.18, (0.05, 0.3, 0.04), Matrix.Rotation(a, 3, 'X'), "steel", (0.3, 0.3, 0.31), 0.2)
    Pu.cyl(Vector((-0.9, 0, 0)), Vector((0.9, 0, 0)), 0.05, 10, "steel", C_STEEL)
    Pu.box(Vector((0.08, 0, 0.44)), (0.02, 0.08, 0.04), I3, "panel", C_YELLOW, 0.2)
    pul = node(Pu, "Pulley", coll, Vector((0, 0, top - 0.3)))
    hi_z, lo_z = 3.1, -0.9
    for name, sx, start in (("CageA", -1, hi_z), ("CageB", 1, lo_z)):
        Cg = Builder()
        Cg.box(Vector((0, 0, 0)), (0.84, 1.7, 0.06), I3, "steel", C_STEEL, 0.2, rust=0.4)
        Cg.box(Vector((0, 0, 0.9)), (0.84, 1.7, 0.05), I3, "metal", C_DARK, 0.2)
        for y in (-0.82, 0.82):
            for x in (-0.4, 0.4):
                Cg.box(Vector((x, y, 0.45)), (0.04, 0.04, 0.9), I3, "metal", C_DARK, 0.2)
        Cg.box(Vector((-sx * 0.4, 0, 0.45)), (0.02, 1.6, 0.8), I3, "panel", C_WHITE, 0.3)
        hazard(Cg, Vector((-0.42, -0.851, 0.02)), (1, 0, 0), (0, 0, 1), 0.84, 0.06, (0, -1, 0), pitch=0.08)
        if name == "CageA":                                                  # its load: a couple of crates
            Cg.box(Vector((0, -0.3, 0.2)), (0.5, 0.5, 0.35), I3, "panel", C_WHITE, 0.3)
            Cg.box(Vector((0.05, 0.35, 0.18)), (0.45, 0.45, 0.3), I3, "metal", C_DARK, 0.2, rust=0.4)
        cg = node(Cg, name, coll, Vector((sx * 0.45, 0, start)))
        other = lo_z if start == hi_z else hi_z
        keys(cg, (1, 30, 75, 105, 150, 151), "location",
             [Vector((sx * 0.45, 0, start)), Vector((sx * 0.45, 0, start)), Vector((sx * 0.45, 0, other)),
              Vector((sx * 0.45, 0, other)), Vector((sx * 0.45, 0, start)), Vector((sx * 0.45, 0, start))])
    turns = (hi_z - lo_z) / (math.tau * 0.46)
    keys(pul, (1, 30, 75, 105, 150, 151), "rotation_euler",
         [(0, 0, 0), (0, 0, 0), (turns * math.tau, 0, 0), (turns * math.tau, 0, 0), (0, 0, 0), (0, 0, 0)])
    return coll

# ======================================================================================
def build_rail_gun():
    random.seed(971)
    coll = clear_collection("Concept_RailGun")
    B = Builder()
    y0, y1 = -0.95, 6.95
    B.box(Vector((0, (y0 + y1) / 2, -0.95)), (1.9, y1 - y0, 0.1), I3, "steel", C_STEEL, 0.25, rust=0.6)
    for k in range(8):
        y = y0 + 0.5 + k * (y1 - y0 - 1) / 7
        B.box(Vector((0, y, -0.65)), (1.2, 0.2, 0.5), I3, "metal", C_FRAME, 0.2, rust=0.3)                # rail supports
    for sx in (-1, 1):
        B.box(Vector((sx * 0.38, (y0 + y1) / 2, -0.3)), (0.14, y1 - y0 - 0.2, 0.2), I3, "steel", (0.3, 0.3, 0.31), 0.2, rust=0.3)
        B.box(Vector((sx * 0.3, (y0 + y1) / 2, -0.3)), (0.02, y1 - y0 - 0.3, 0.16), I3, "copper", C_COPPER, 0.15)
        for k in range(6):                                                   # capacitor banks along the side
            c = Vector((sx * 0.8, 0.2 + k * 1.1, -0.55))
            B.cyl(c, c + Vector((0, 0, 0.5)), 0.12, 14, "panel", C_FACILITY, 0.1)
            ring(B, c + Vector((0, 0, 0.35)), (0, 0, 1), 0.12, 0.125, 0.03, 14, "cyan", C_CYAN, 0.05)
    for k in range(6):                                                       # accelerator coils
        y = 0.6 + k * 1.1
        ring(B, Vector((0, y, -0.25)), (0, 1, 0), 0.55, 0.68, 0.22, 28, "copper", C_COPPER, 0.15)
        ring(B, Vector((0, y, -0.25)), (0, 1, 0), 0.68, 0.72, 0.26, 28, "metal", C_DARK, 0.15)
    B.box(Vector((0, -0.6, -0.4)), (1.2, 0.7, 0.5), I3, "metal", C_BLUE, 0.2)                              # breech / loader
    B.box(Vector((0, -0.6, -0.12)), (0.9, 0.6, 0.04), I3, "steel", L["C_WEAR"], 0.12)
    hazard(B, Vector((-0.6, -0.951, -0.6)), (1, 0, 0), (0, 0, 1), 1.2, 0.08, (0, -1, 0), pitch=0.1)
    ring(B, Vector((0, y1 - 0.1, -0.25)), (0, 1, 0), 0.5, 0.7, 0.18, 28, "metal", C_DARK, 0.15)
    ring(B, Vector((0, y1 - 0.02, -0.25)), (0, 1, 0), 0.5, 0.54, 0.03, 28, "cyan", C_CYAN, 0.05)
    finish(B, "Frame", coll)
    Sl = Builder()
    Sl.box(Vector((0, 0, 0)), (0.62, 0.5, 0.16), I3, "copper", C_COPPER, 0.15)
    Sl.box(Vector((0, 0, 0.1)), (0.5, 0.42, 0.04), I3, "metal", C_DARK, 0.1)
    Sl.box(Vector((0, 0.26, 0.1)), (0.5, 0.02, 0.16), I3, "cyan", C_CYAN, 0.05)
    sled = node(Sl, "Sled", coll, Vector((0, -0.3, -0.22)))
    keys(sled, (1, 60, 66, 70, 120, 121), "location",
         [Vector((0, -0.3, -0.22)), Vector((0, -0.3, -0.22)), Vector((0, 6.4, -0.22)), Vector((0, 6.4, -0.22)),
          Vector((0, -0.3, -0.22)), Vector((0, -0.3, -0.22))])
    Ch = Builder()
    for k in range(6):
        Ch.box(Vector((0, k * 0.12, 0.2)), (0.02, 0.08, 0.4), I3, "cyan", C_CYAN, 0.05)
    chg = node(Ch, "Charge", coll, Vector((0.61, -0.95, -0.55)))
    keys(chg, (1, 58, 64, 121), "scale", [Vector((1, 1, 0.05)), Vector((1, 1, 1)), Vector((1, 1, 0.05)), Vector((1, 1, 0.05))])
    return coll

# ======================================================================================
def build_tipping_bucket():
    random.seed(981)
    coll = clear_collection("Concept_TippingBucket")
    B = Builder()
    piv = Vector((0, 0, 0.9))
    for sy in (-1, 1):
        for sx in (-1, 1):
            B.cyl(Vector((sx * 0.8, sy * 0.85, -0.95)), Vector((0, sy * 0.85, piv.z + 0.1)), 0.04, 8, "steel", C_STEEL, rust=0.5)
        B.cyl(Vector((0, sy * 0.85, piv.z - 0.08)), Vector((0, sy * 0.85, piv.z + 0.08)), 0.1, 12, "metal", C_DARK, rust=0.3)
    B.box(Vector((0, 0, -0.97)), (1.98, 1.98, 0.06), I3, "steel", C_STEEL, 0.25, rust=0.6)
    for sx in (-1, 1):                                                       # exit slides, one each side
        B.box(Vector((sx * 0.6, 0, -0.55)), (0.8, 1.5, 0.04), Matrix.Rotation(sx * math.radians(18), 3, 'Y'), "steel", L["C_WEAR"], 0.15)
        B.box(Vector((sx * 0.6, 0.75, -0.45)), (0.8, 0.04, 0.3), I3, "panel", C_WHITE, 0.3)
        B.box(Vector((sx * 0.6, -0.75, -0.45)), (0.8, 0.04, 0.3), I3, "panel", C_WHITE, 0.3)
    fz = 2.3                                                                 # intake funnel on top
    B.tube_rings([quad_ring(0, 0, fz + 0.65, 0.9, 0.9), quad_ring(0, 0, fz, 0.3, 0.3)], "steel", (0.3, 0.3, 0.3), 0.2, 0.5, cap=False)
    B.tube_rings([quad_ring(0, 0, fz - 0.02, 0.3, 0.3), quad_ring(0, 0, fz + 0.63, 0.92, 0.92)], "steel", (0.3, 0.3, 0.3), 0.2, 0.5, cap=False)
    for (x, y) in ((-0.9, -0.9), (0.9, -0.9), (0.9, 0.9), (-0.9, 0.9)):
        B.box(Vector((x, y, (fz + 0.65 + piv.z) / 2)), (0.08, 0.08, fz + 0.65 - piv.z), I3, "metal", C_FRAME, 0.2, rust=0.3)
    hazard(B, Vector((-0.9, -0.941, fz + 0.5)), (1, 0, 0), (0, 0, 1), 1.8, 0.12, (0, -1, 0), pitch=0.1)
    finish(B, "Frame", coll)
    Bk = Builder()
    Bk.cyl(Vector((0, -0.85, 0)), Vector((0, 0.85, 0)), 0.05, 10, "steel", C_STEEL)
    for sx in (-1, 1):                                                       # two scoops back to back
        Bk.box(Vector((sx * 0.4, 0, -0.18)), (0.8, 1.4, 0.04), I3, "steel", (0.3, 0.3, 0.3), 0.2, rust=0.5)
        Bk.box(Vector((sx * 0.79, 0, -0.02)), (0.04, 1.4, 0.36), I3, "panel", C_WHITE, 0.3)
        for sy in (-1, 1):
            Bk.box(Vector((sx * 0.4, sy * 0.7, -0.02)), (0.8, 0.04, 0.36), I3, "panel", C_WHITE, 0.3)
        hazard(Bk, Vector((sx * 0.811, -0.6 * sx, -0.12)), (0, sx, 0), (0, 0, 1), 1.2, 0.06, (sx, 0, 0), pitch=0.08)
    Bk.box(Vector((0, 0, 0.05)), (0.06, 1.4, 0.5), I3, "metal", C_DARK, 0.2)                               # centre divider
    bk = node(Bk, "Bucket", coll, piv)
    t = math.radians(28)
    keys(bk, (1, 45, 52, 105, 112, 121), "rotation_euler",
         [(0, t, 0), (0, t, 0), (0, -t, 0), (0, -t, 0), (0, t, 0), (0, t, 0)])
    return coll

# ======================================================================================
def build_assembly_chamber():
    random.seed(991)
    coll = clear_collection("Concept_AssemblyChamber")
    B, G = Builder(), Builder()
    lo, hi = Vector((-2.95, -2.95, -1.0)), Vector((2.95, 2.95, 4.95))
    ctr = (lo + hi) / 2
    for x in (lo.x, hi.x):                                                   # the twelve edges
        for y in (lo.y, hi.y):
            B.box(Vector((x * 0.99, y * 0.99, ctr.z)), (0.16, 0.16, hi.z - lo.z), I3, "metal", C_DARK, 0.15)
    for z in (lo.z + 0.08, hi.z - 0.08, 1.0):
        for y in (lo.y, hi.y):
            B.box(Vector((0, y * 0.99, z)), (5.9, 0.16, 0.16), I3, "metal", C_DARK, 0.15)
        for x in (lo.x, hi.x):
            B.box(Vector((x * 0.99, 0, z)), (0.16, 5.9, 0.16), I3, "metal", C_DARK, 0.15)
    for n in (Vector((1, 0, 0)), Vector((-1, 0, 0)), Vector((0, 1, 0)), Vector((0, -1, 0))):
        base = n * 2.93
        panel_face(B, base + Vector((0, 0, 0.0)), n, 5.6, 1.9, C_FACILITY)                                   # solid lower band
        R = facing_basis(n)
        for k in range(3):
            G.box(base + R @ Vector((-1.9 + k * 1.9, 2.95 - 0.0, 0)) + Vector((0, 0, 0)), (1.8, 3.7, 0.02), R, "glass", (0.55, 0.9, 1.0), 0.02)
            B.box(base + R @ Vector((-2.85 + k * 1.9, 2.95, 0.02)), (0.08, 3.8, 0.04), R, "panel", C_WHITE, 0.3)
    B.box(Vector((0, 0, hi.z - 0.05)), (5.9, 5.9, 0.1), I3, "panel", C_FACILITY, 0.1)                         # roof
    for k in range(4):
        B.box(Vector((-1.5 + k, 0, hi.z + 0.02)), (0.6, 4.0, 0.06), I3, "metal", C_DARK, 0.1)
    B.box(Vector((0, 0, lo.z + 0.03)), (5.9, 5.9, 0.06), I3, "metal", (0.06, 0.06, 0.07), 0.1)
    ring(B, Vector((0, 0, lo.z + 0.07)), (0, 0, 1), 1.6, 1.7, 0.02, 48, "cyan", C_CYAN, 0.05)
    for s in (Vector((1, 0, 0)), Vector((-1, 0, 0)), Vector((0, 1, 0)), Vector((0, -1, 0))):                  # intake ports
        p = s * 2.95 + Vector((0, 0, 0.3))
        ring(B, p, s, 0.55, 0.72, 0.3, 28, "metal", C_DARK, 0.15)
        ring(B, p - s * 0.1, s, 0.55, 0.58, 0.04, 28, "cyan", C_CYAN, 0.05)
    hatch = Vector((0, 2.96, -0.55))
    B.box(hatch, (1.6, 0.08, 0.8), I3, "metal", (0.02, 0.02, 0.02), 0.05)
    for sx in (-1, 1):
        B.box(Vector((sx * 0.82, 2.98, -0.55)), (0.08, 0.06, 0.9), I3, "panel", C_YELLOW, 0.2)
    finish(B, "Frame", coll); finish(G, "Glass", coll)
    Em = Builder()
    for x in (-2.6, 2.6):
        for y in (-2.6, 2.6):
            for z in (-0.6, 4.55):
                p = Vector((x, y, z))
                Em.box(p, (0.35, 0.35, 0.3), I3, "panel", C_WHITE, 0.3)
                d = (ctr - p).normalized()
                Em.cyl(p + d * 0.18, p + d * 0.26, 0.1, 12, "cyan", C_CYAN, 0.05)
    finish(Em, "Emitters", coll)
    F = Builder()
    F.box(Vector((0, 0, 0)), (5.5, 5.5, 5.5), I3, "field_zp", C_CYAN, 0.02)
    fld = node(F, "Field", coll, ctr)
    cycle(fld, "scale", [Vector((1, 1, 1)), Vector((0.97, 0.97, 0.97))], 120)
    Pa = Builder()                                                           # a frame mid-assembly: plates and rods drifting
    for k in range(4):
        a = k / 4 * math.tau
        Pa.box(Vector((math.cos(a) * 0.9, math.sin(a) * 0.9, 0.2 * (k % 2))), (0.9, 0.9, 0.05), Matrix.Rotation(a + 0.3, 3, 'Z') @ Matrix.Rotation(0.3 * (k - 1.5), 3, 'X'),
               "steel", (0.55, 0.55, 0.58), 0.15)
    for k in range(3):
        a = k / 3 * math.tau + 0.5
        Pa.cyl(Vector((math.cos(a) * 0.4, math.sin(a) * 0.4, -0.6)), Vector((math.cos(a) * 0.6, math.sin(a) * 0.6, 0.7)), 0.05, 8, "copper", C_COPPER, 0.15)
    Pa.box(Vector((0, 0, 0.1)), (0.8, 0.8, 0.8), I3, "metal", C_DARK, 0.2)
    for k in range(4):
        a = k / 4 * math.tau
        Pa.box(Vector((math.cos(a) * 0.42, math.sin(a) * 0.42, 0.1)), (0.06, 0.06, 0.82), I3, "cyan", C_CYAN, 0.05)
    parts = node(Pa, "Parts", coll, ctr + Vector((0, 0, -0.3)))
    keys(parts, (1, 121, 241), "rotation_euler", [(0, 0, 0), (0.15, 0.1, math.pi / 2), (0, 0, math.pi)], linear=True)
    keys(parts, (1, 61, 121, 181, 241), "location", [ctr + Vector((0, 0, -0.3)), ctr + Vector((0, 0, 0.1)), ctr + Vector((0, 0, -0.3)),
                                                      ctr + Vector((0, 0, 0.1)), ctr + Vector((0, 0, -0.3))])
    return coll

# ======================================================================================
def build_screw_elevator():
    random.seed(1001)
    coll = clear_collection("Concept_ScrewElevator")
    B, G = Builder(), Builder()
    z0, z1, r = -0.55, 4.7, 0.72
    glass_tube(G, Vector((0, 0, z0)), Vector((0, 0, z1)), r)
    for z in (z0 + 0.05, 1.0, 3.0, z1 - 0.05):
        ring(B, Vector((0, 0, z)), (0, 0, 1), r, r + 0.12, 0.12, 28, "metal", C_DARK, 0.15, rust=0.2)
    for (x, y) in ((-0.8, -0.8), (0.8, -0.8), (0.8, 0.8), (-0.8, 0.8)):
        B.box(Vector((x, y, (z1 - 1) / 2 + 0.1)), (0.08, 0.08, z1 + 1.1), I3, "metal", C_FRAME, 0.2, rust=0.3)
    B.box(Vector((0, 0, -0.78)), (1.9, 1.9, 0.44), I3, "metal", C_FRAME, 0.2, rust=0.3)                     # drive base
    panel_face(B, Vector((0, 0.955, -0.78)), (0, 1, 0), 1.7, 0.36, C_WHITE)
    B.box(Vector((0.55, 0.6, -0.4)), (0.5, 0.5, 0.32), I3, "metal", C_BLUE, 0.2, rust=0.15)
    # intake at the back at belt height, spout at the top front
    B.box(Vector((0, -0.85, -0.45)), (0.9, 0.3, 0.5), I3, "metal", C_DARK, 0.15)
    B.box(Vector((0, -0.97, -0.5)), (0.7, 0.04, 0.34), I3, "metal", (0.02, 0.02, 0.02), 0.05)
    hazard(B, Vector((-0.45, -1.0, -0.2)), (1, 0, 0), (0, 0, 1), 0.9, 0.05, (0, -1, 0), pitch=0.08)
    B.box(Vector((0, 0.72, 4.2)), (0.8, 0.5, 0.5), I3, "metal", C_DARK, 0.15)
    B.box(Vector((0, 0.9, 3.95)), (0.7, 0.3, 0.04), Matrix.Rotation(math.radians(-20), 3, 'X'), "steel", L["C_WEAR"], 0.12)
    B.cyl(Vector((0, 0, z1)), Vector((0, 0, z1 + 0.25)), 0.5, 20, "metal", C_DARK, 0.15)                      # gearbox cap
    B.cyl(Vector((0, 0, z1 + 0.25)), Vector((0, 0, z1 + 0.28)), 0.3, 20, "glow", C_AMBER, 0.05)
    finish(B, "Frame", coll); finish(G, "Glass", coll)
    Sc = Builder()
    Sc.cyl(Vector((0, 0, 0)), Vector((0, 0, z1 - z0)), 0.08, 12, "steel", C_STEEL)
    helix(Sc, 0.1, z1 - z0 - 0.1, 5.0, 0.08, r - 0.04, 0.03, 200, "steel", (0.45, 0.45, 0.47))
    helix(Sc, 0.1 + 0.03, z1 - z0 - 0.1 + 0.03, 5.0, r - 0.1, r - 0.04, 0.012, 200, "panel", C_YELLOW)
    sc_ = node(Sc, "Screw", coll, Vector((0, 0, z0)))
    spin(sc_, 2, -1, 45)
    return coll

# ======================================================================================
PT_RUN = (0.0, 4.0)          # sprocket centres (z)
PT_R = 0.9                   # loop radius; runs at y = 1 -+ PT_R
def pt_loop(u):
    """Point on the chain loop (x = 0), u in [0, 1): up the back run, over the top, down the front run."""
    y0, zc0, zc1, r = 1.0, PT_RUN[0], PT_RUN[1], PT_R
    straight = zc1 - zc0
    arc = math.pi * r
    total = 2 * straight + 2 * arc
    s = (u % 1.0) * total
    if s < straight:
        return Vector((0, y0 - r, zc0 + s))
    s -= straight
    if s < arc:
        a = s / r
        return Vector((0, y0 - r * math.cos(a), zc1 + r * math.sin(a)))
    s -= arc
    if s < straight:
        return Vector((0, y0 + r, zc1 - s))
    s -= straight
    a = s / r
    return Vector((0, y0 + r * math.cos(a), zc0 - r * math.sin(a)))

def build_platform_elevator():
    random.seed(1011)
    coll = clear_collection("Concept_PlatformElevator")
    B = Builder()
    for sx in (-1, 1):
        x = sx * 0.92
        for y in (-0.9, 2.9):
            B.box(Vector((x, y, 2.0)), (0.1, 0.1, 6.0), I3, "metal", C_FRAME, 0.2, rust=0.3)
        for z in (-0.95, 1.5, 3.5, 4.95):
            B.box(Vector((x, 1.0, z)), (0.1, 3.9, 0.1), I3, "metal", C_FRAME, 0.2, rust=0.3)
        for (ya, yb, za, zb) in ((-0.9, 2.9, -0.9, 1.4), (-0.9, 2.9, 3.6, 1.6)):
            B.cyl(Vector((x, ya, za)), Vector((x, yb, zb)), 0.02, 6, "steel", C_STEEL, rust=0.5)
        chain = [pt_loop(i / 64) + Vector((x * 0.9, 0, 0)) for i in range(64)]
        B.pipe(chain + [chain[0]], 0.025, 5, "steel", (0.2, 0.2, 0.2))
        panel_face(B, Vector((sx * 0.975, 1.0, -0.5)), (sx, 0, 0), 3.6, 0.7, C_WHITE)
    B.box(Vector((0, 1.0, -0.97)), (1.98, 3.96, 0.06), I3, "steel", C_STEEL, 0.25, rust=0.6)
    B.box(Vector((0.7, -0.5, -0.7)), (0.4, 0.5, 0.5), I3, "metal", C_BLUE, 0.2, rust=0.15)
    hazard(B, Vector((-0.9, -0.99, -0.94)), (1, 0, 0), (0, 0, 1), 1.8, 0.08, (0, -1, 0), pitch=0.1)
    hazard(B, Vector((0.9, 2.99, -0.94)), (-1, 0, 0), (0, 0, 1), 1.8, 0.08, (0, 1, 0), pitch=0.1)
    finish(B, "Frame", coll)
    for name, zc in (("SprocketBottom", PT_RUN[0]), ("SprocketTop", PT_RUN[1])):
        Sp = Builder()
        for sx in (-1, 1):
            ring(Sp, Vector((sx * 0.83, 0, 0)), (1, 0, 0), PT_R - 0.12, PT_R + 0.02, 0.05, 32, "steel", (0.3, 0.3, 0.31), 0.2, rust=0.4)
            for k in range(12):
                a = k / 12 * math.tau
                Sp.box(Vector((sx * 0.83, math.cos(a) * (PT_R + 0.04), math.sin(a) * (PT_R + 0.04))), (0.05, 0.06, 0.06),
                       Matrix.Rotation(a, 3, 'X'), "steel", (0.3, 0.3, 0.31), 0.2)
        Sp.cyl(Vector((-0.9, 0, 0)), Vector((0.9, 0, 0)), 0.06, 10, "steel", C_STEEL)
        sp = node(Sp, name, coll, Vector((0, 1.0, zc)))
        spin(sp, 0, 1.0 * (2 * (PT_RUN[1] - PT_RUN[0]) + 2 * math.pi * PT_R) / (math.tau * PT_R) / 2, 180)
    length = 180
    for k in range(6):
        Pl = Builder()
        Pl.box(Vector((0, 0, 0)), (1.5, 0.75, 0.05), I3, "steel", C_STEEL, 0.2, rust=0.4)
        Pl.box(Vector((0, 0.36, 0.06)), (1.5, 0.03, 0.12), I3, "panel", C_YELLOW, 0.2)
        Pl.box(Vector((0, -0.36, 0.06)), (1.5, 0.03, 0.12), I3, "panel", C_YELLOW, 0.2)
        for sx in (-1, 1):
            Pl.box(Vector((sx * 0.78, 0, 0.12)), (0.06, 0.06, 0.3), I3, "metal", C_DARK, 0.2)
        ob = node(Pl, f"Platform{k}", coll, pt_loop(k / 6))
        n = 30
        frames = [1 + round(length * i / n) for i in range(n + 1)]
        keys(ob, frames, "location", [pt_loop(k / 6 + i / n) for i in range(n + 1)], linear=True)
    return coll

PIECES = [
    (build_gravity_inverter, "concept_gravity_inverter.glb"),
    (build_tag_gate, "concept_tag_gate.glb"),
    (build_bounce_pad, "concept_bounce_pad.glb"),
    (build_vortex, "concept_vortex_funnel.glb"),
    (build_tube_straight, "concept_tube_straight.glb"),
    (build_tube_bend, "concept_tube_bend.glb"),
    (build_tube_junction, "concept_tube_junction.glb"),
    (build_tube_receiver, "concept_tube_receiver.glb"),
    (build_heat_lamp, "concept_heat_lamp.glb"),
    (build_cryo_vent, "concept_cryo_vent.glb"),
    (build_counterweight_elevator, "concept_counterweight_elevator.glb"),
    (build_rail_gun, "concept_rail_gun.glb"),
    (build_tipping_bucket, "concept_tipping_bucket.glb"),
    (build_assembly_chamber, "concept_assembly_chamber.glb"),
    (build_screw_elevator, "concept_screw_elevator.glb"),
    (build_platform_elevator, "concept_platform_elevator.glb"),
]
ICONS = []

def build_all(do_export=True):
    return build_family(PIECES, OUT_DIR, do_export)

if __name__ == "__main__":
    build_all(do_export=False)
