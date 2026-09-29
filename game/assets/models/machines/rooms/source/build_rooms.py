"""
Puzzle-room machines and fixtures for the museum's Puzzle Rooms wing. Frame as the other families: Blender Z up,
+Y front (Godot -Z), origin at the anchor cell centre with the floor at z = -1 unless noted. The colliders in
tools/structures/scenes_rooms.py (and place_museum.py for the moving floors) are built from the same numbers.

  turntable.glb          Carousel floor: 9 m radius disc, origin at its axis, top face at z = +0.4
  sweep_arm.glb          Carousel machine: pylon + boom holding a rubber blade 4 cm over the turntable,
                         which scrapes riding items outward off the disc                       (Structure)
  slot_sieve.glb         Scree machine: 3x2-cell bar grate (0.54 m slots) over a hopper that drains sideways;
                         laid on the slope, small pebbles drop through and slabs slide over     (Structure)
  terrace_catcher.glb    Scree machine: 3x1-cell catch trough with a padded back wall and a drag floor that
                         empties out of its open end                                           (Structure)
  balance_floor.glb      Scales floor: 20 x 8 m deck on a hinge barrel, origin at the hinge axis, deck top z +0.2
  counterweight_sled.glb Scales machine: heavy sled on rails that shifts to rebalance the deck (visual)
  tilt_gauge.glb         Scales machine: pendulum level gauge on a post that reads the deck's tilt (visual)

Detail (bolts, seams, rust runs, vents, cables) is added only on faces nothing slides over; the working surfaces
(disc top, grate bars, hopper and trough floors, deck top) keep their heights and stay flat.
"""
import bpy, bmesh, math, random, os
from mathutils import Vector, Matrix

_HERE = (os.path.dirname(os.path.abspath(__file__)) if "__file__" in globals()
         else r"C:\\Users\\steph\\OneDrive\\Documents\\godot\\projects\\gmp-framework\\game\\assets\\models\\machines\\rooms\\source")
_LIB = os.path.normpath(os.path.join(_HERE, "..", "..", "..", "shared", "salvage_lib.py"))
_S = {"__name__": "salvage_lib", "__file__": _LIB}
exec(compile(open(_LIB, encoding="utf-8").read(), _LIB, "exec"), _S)
_SKIP = {"HERE", "OUT_DIR", "PIECES", "build_all", "ICONS", "export", "CONV"}
globals().update({k: v for k, v in _S.items() if not k.startswith("__") and k not in _SKIP})
HERE = _HERE
OUT_DIR = os.path.dirname(HERE)

TT_R = 9.0
SIEVE_BARS = [-0.32 + 0.62 * k for k in range(9)]
ARM_A, ARM_B = Vector((-1.5, 3.0)), Vector((-0.2, 11.0))     # sweep blade ends (outer, inner), pylon at the origin
C_GRIME = (0.1, 0.09, 0.08)
C_RUSTRUN = (0.27, 0.13, 0.06)

# ---------------------------------------------------------------------------- detail helpers
def stud(B, p, n, r=0.016, h=0.012, seg=5, mat="steel", c=C_STEEL, rust=0.4):
    """Bolt head / rivet: a short open-bottomed prism standing on the surface at p along n."""
    n = Vector(n).normalized(); R = rot_to(n)
    pts = [R @ Vector((math.cos(a) * r, math.sin(a) * r, 0)) for a in [(k + 0.5) / seg * math.tau for k in range(seg)]]
    lo = [B.bm.verts.new(Vector(p) + q) for q in pts]
    hi = [B.bm.verts.new(Vector(p) + n * h + q * 0.8) for q in pts]
    fs = [B.quad([lo[k], lo[(k + 1) % seg], hi[(k + 1) % seg], hi[k]], mat) for k in range(seg)]
    fs.append(B.quad(hi, mat))
    bmesh.ops.recalc_face_normals(B.bm, faces=fs)
    B.paint(fs, c, 0.15, rust)
    return fs

def bolt_row(B, p0, p1, n, normal, r=0.016, h=0.012, rust=0.4, streaks=0.0):
    """n bolt heads evenly from p0 to p1 on a face with the given normal; streaks > 0 hangs rust runs under some."""
    for k in range(n):
        p = Vector(p0).lerp(Vector(p1), k / max(1, n - 1))
        stud(B, p, normal, r, h, rust=rust)
        if streaks and abs(Vector(normal).z) < 0.5 and random.random() < streaks:
            streak(B, p - Vector((0, 0, r)), normal, r * 1.6, random.uniform(0.12, 0.3))

def decal(B, c, n, w, h, mat, col, var=0.15, rust=0.0, up=None, lift=0.002):
    """One flat quad (w across, h up) just off a surface at c facing n: seams, runs, labels, painted marks."""
    n = Vector(n).normalized()
    R = facing_basis(n) if up is None else Matrix((Vector(up).cross(n).normalized(), Vector(up).normalized(), n)).transposed()
    c = Vector(c) + n * lift
    vs = [B.bm.verts.new(c + R @ Vector((x * w / 2, y * h / 2, 0))) for x, y in ((-1, -1), (1, -1), (1, 1), (-1, 1))]
    f = B.quad(vs, mat); f.normal_update()
    if f.normal.dot(n) < 0:
        f.normal_flip()
    B.paint([f], col, var, rust)
    return f

def streak(B, c, n, w=0.03, l=0.25, mat="metal"):
    """Rust run-off hanging down from c on the face n."""
    return decal(B, Vector(c) - Vector((0, 0, l / 2)), n, w, l, mat, C_RUSTRUN, 0.35, 0.6, lift=0.0015)

def seam(B, p0, p1, n, w=0.012, mat="metal", col=(0.05, 0.05, 0.055)):
    """Dark joint line / weld bead from p0 to p1 on the face n."""
    d = Vector(p1) - Vector(p0)
    return decal(B, (Vector(p0) + Vector(p1)) / 2, n, w, d.length, mat, col, 0.1, up=d)

def grime(B, c, n, w, h, k=0.7):
    """Dark soot / dirt wash on a face (floor-level grime, corners)."""
    return decal(B, c, n, w, h, "metal", C_GRIME, 0.3, 0.2, lift=0.0012)

def warn_plate(B, c, n, s=0.3):
    """Yellow warning sign with a black triangle."""
    decal(B, c, n, s, s * 0.8, "panel", C_YELLOW, 0.2, lift=0.003)
    R = facing_basis(n); nn = Vector(n).normalized()
    for k, (sc, col) in enumerate(((0.3, C_BLACK), (0.17, C_YELLOW))):
        o = Vector(c) + nn * (0.0045 + 0.0008 * k)
        tri = [B.bm.verts.new(o + R @ Vector((x * s * sc, y * s * sc - s * 0.03, 0))) for x, y in ((-1, -0.7), (1, -0.7), (0, 1))]
        f = B.bm.faces.new(tri); f.material_index = MI["panel"]; f.normal_update()
        if f.normal.dot(nn) < 0:
            f.normal_flip()
        B.paint([f], col, 0.1)

def grille(B, c, n, w, h, slats=5, mat="metal", col=C_DARK):
    """Louvred vent: a dark recess with angled slats across it."""
    decal(B, c, n, w, h, mat, (0.02, 0.02, 0.022), 0.05, lift=0.002)
    R = facing_basis(n); nn = Vector(n).normalized()
    for k in range(slats):
        y = -h / 2 + (k + 0.5) * h / slats
        a = [Vector(c) + R @ Vector((x * w / 2, y - h / slats * 0.35, 0)) + nn * 0.004 for x in (-1, 1)]
        b = [Vector(c) + R @ Vector((x * w / 2, y + h / slats * 0.35, 0)) + nn * 0.016 for x in (-1, 1)]
        f = B.quad([B.bm.verts.new(v) for v in (a[0], a[1], b[1], b[0])], mat); f.normal_update()
        if f.normal.dot(nn + Vector((0, 0, 1)) * 0.2) < 0:
            f.normal_flip()
        B.paint([f], col, 0.15)

def cable(B, pts, r=0.02, clips=(), col=(0.03, 0.03, 0.032), mat="rubber"):
    """Cable through pts with a clamp block at each point index in clips."""
    pts = [Vector(p) for p in pts]
    B.pipe(pts, r, 6, mat, col)
    for i in clips:
        d = (pts[min(i + 1, len(pts) - 1)] - pts[max(i - 1, 0)]).normalized()
        B.box(pts[i], (r * 3.2, r * 3.2, r * 3.2), rot_to(d), "steel", C_STEEL, 0.2, rust=0.3, bevel=0)

def open_box(B, center, size, basis=I3, mat="metal", c=C_FRAME, var=0.18, rust=0.0, bevel=None, drop=(0, 0, -1)):
    """B.box without the face that points along drop (a bottom on the floor, a back against a wall)."""
    fs = B.box(center, size, basis, mat, c, var, rust, bevel)
    d = basis @ Vector(drop)
    for f in fs:
        f.normal_update()
    hid = max(fs, key=lambda f: f.normal.dot(d))
    bmesh.ops.delete(B.bm, geom=[hid], context='FACES_ONLY')
    return [f for f in fs if f is not hid]

def seg_box(B, a, b, z0, z1, t, mat, c, var=0.15, rust=0.0, bevel=None):
    """Box from a to b (2D, Blender XY) between heights z0..z1, thickness t."""
    d = b - a
    ang = math.atan2(d.y, d.x)
    B.box(Vector(((a.x + b.x) / 2, (a.y + b.y) / 2, (z0 + z1) / 2)), (d.length, t, z1 - z0), Matrix.Rotation(ang, 3, 'Z'), mat, c, var,
          rust=rust, bevel=bevel)

def i_beam(B, a, b, z0, z1, w, mat, c, rust=0.15, fl=0.04, web=0.04):
    """I-section beam from a to b (2D) between z0 and z1, flange width w: the same envelope as a seg_box."""
    seg_box(B, a, b, z1 - fl, z1, w, mat, c, rust=rust)
    seg_box(B, a, b, z0, z0 + fl, w, mat, c, rust=rust)
    seg_box(B, a, b, z0 + fl, z1 - fl, web, mat, c, rust=rust)

def annulus(B, c, r0, r1, seg, mat, col, var=0.1, z_up=True, a0=0.0, a1=math.tau, paint=None):
    """Flat ring of quads (a painted stripe on a disc) from angle a0 to a1; paint(k) -> colour per segment."""
    full = abs(a1 - a0 - math.tau) < 1e-6
    n = seg if full else seg + 1
    ang = [a0 + (a1 - a0) * k / seg for k in range(n)]
    ins = [B.bm.verts.new(c + Vector((math.cos(a) * r0, math.sin(a) * r0, 0))) for a in ang]
    out = [B.bm.verts.new(c + Vector((math.cos(a) * r1, math.sin(a) * r1, 0))) for a in ang]
    fs = []
    for k in range(seg):
        k1 = (k + 1) % n
        f = B.quad([ins[k], out[k], out[k1], ins[k1]], mat); f.normal_update()
        if (f.normal.z < 0) == z_up:
            f.normal_flip()
        B.paint([f], paint(k) if paint else col, var)
        fs.append(f)
    return fs

# ---------------------------------------------------------------------------- the carousel
def build_turntable():
    """9 m disc: a steel side wall with a floor-grime band and a glowing rim, a top of 16 x 3 tread plates in
    slightly different tones (scuffed where items land at 3 m), painted spokes and rings, and a bolted hub."""
    random.seed(1301)
    coll = clear_collection("Room_Turntable")
    B = Builder()
    seg = 64
    circ = lambda r, z: [Vector((math.cos(k / seg * math.tau) * r, math.sin(k / seg * math.tau) * r, z)) for k in range(seg)]
    side = B.tube_rings([circ(TT_R, 0.0), circ(TT_R, 0.08), circ(TT_R, 0.4)], "steel", (0.42, 0.43, 0.46), 0.1, 0.2, cap=False, smooth=True)
    B.paint(side[:seg], mix3((0.42, 0.43, 0.46), C_GRIME, 0.55), 0.15, 0.3)   # bottom has no face: it sits in its pit
    radii = [1.2, 3.0, 6.0, TT_R]
    rings = [[B.bm.verts.new(p) for p in circ(r, 0.4)] for r in radii]
    for i in range(len(radii) - 1):                                           # tread plates, 4 segments each
        for p in range(16):
            tone = jit3((0.44, 0.45, 0.48) if p % 2 else (0.39, 0.4, 0.43), 0.05)
            if i == 1:
                tone = mix3(tone, (0.3, 0.3, 0.3), 0.35)                        # the landing band: scuffed darker
            fs = []
            for k in range(p * 4, p * 4 + 4):
                k1 = (k + 1) % seg
                f = B.quad([rings[i][k], rings[i + 1][k], rings[i + 1][k1], rings[i][k1]], "steel"); f.normal_update()
                if f.normal.z < 0: f.normal_flip()
                fs.append(f)
            B.paint(fs, tone, 0.12, 0.15)
    hub = B.bm.faces.new(rings[0]); hub.material_index = MI["steel"]; hub.normal_update()
    if hub.normal.z < 0: hub.normal_flip()
    B.paint([hub], (0.4, 0.41, 0.44), 0.1)
    for k in range(16):                                                       # painted spokes (flat decals)
        a = k / 16 * math.tau
        decal(B, Vector((math.cos(a) * 5, math.sin(a) * 5, 0.4)), (0, 0, 1), 0.18, 7.6, "panel", C_YELLOW if k % 2 else C_BLACK,
              0.1, up=Vector((math.cos(a), math.sin(a), 0)), lift=0.002)
    for r in (3.0, 6.0):
        annulus(B, Vector((0, 0, 0.403)), r - 0.06, r + 0.06, seg, "panel", C_WHITE, 0.1)
    B.cyl(Vector((0, 0, 0.402)), Vector((0, 0, 0.406)), 1.2, 32, "panel", C_DARK, 0.1)
    annulus(B, Vector((0, 0, 0.4065)), 1.05, 1.16, 32, "panel", C_YELLOW, 0.1, paint=lambda k: C_YELLOW if k % 4 < 2 else C_BLACK)
    for k in range(12):                                                       # hub bolt circle and centre cap
        a = (k + 0.5) / 12 * math.tau
        stud(B, Vector((math.cos(a) * 0.9, math.sin(a) * 0.9, 0.406)), (0, 0, 1), 0.04, 0.02)
    B.cyl(Vector((0, 0, 0.406)), Vector((0, 0, 0.42)), 0.35, 16, "steel", C_STEEL, 0.1, rust=0.2)
    for p in range(16):                                                       # countersunk plate bolts on the seams
        a = p / 16 * math.tau
        for r in (3.0, 6.0):
            for da in (-0.03, 0.03):
                stud(B, Vector((math.cos(a + da * 3 / r) * r, math.sin(a + da * 3 / r) * r, 0.4)), (0, 0, 1), 0.03, 0.004, seg=5, rust=0.2)
    B.tube_rings([circ(TT_R + 0.01, 0.02), circ(TT_R + 0.01, 0.38)], "cyan", C_CYAN, 0.05, 0.0, cap=False, smooth=True)   # rim glow band
    finish(B, "Disc", coll)
    return coll

def mix3(a, b, t):
    return tuple(x + (y - x) * t for x, y in zip(a, b))

def jit3(c, k=0.1):
    f = 1 + random.uniform(-k, k)
    return tuple(min(1.0, x * f) for x in c)

def build_sweep_arm():
    """Pylon with base plate, vents and a cable riser; an I-beam boom with a bolted splice at the bend; hangers
    with clevis blocks; a white blade whose squeegee clamp strip is bolted along its length."""
    random.seed(1311)
    coll = clear_collection("Room_SweepArm")
    B = Builder()
    open_box(B, Vector((0, 0, 1.0)), (0.8, 0.8, 4.0), I3, "metal", C_FRAME, 0.2, rust=0.3)                  # pylon
    open_box(B, Vector((0, 0, -0.97)), (1.0, 1.0, 0.06), I3, "steel", C_STEEL, 0.2, rust=0.5, bevel=0.015)   # base flange
    for sx in (-1, 1):
        for sy in (-1, 1):
            stud(B, Vector((sx * 0.45, sy * 0.45, -0.94)), (0, 0, 1), 0.03, 0.025)
    panel_face(B, Vector((0, 0.401, 0.6)), (0, 1, 0), 0.6, 2.4, C_WHITE)
    for sx in (-1, 1):                                                        # side vents, seams, bolt lines
        n = Vector((sx, 0, 0))
        grille(B, Vector((sx * 0.4, 0, 0.2)), n, 0.4, 0.5, 6)
        seam(B, Vector((sx * 0.4, -0.35, 1.2)), Vector((sx * 0.4, 0.35, 1.2)), n)
        bolt_row(B, Vector((sx * 0.4, -0.3, 1.3)), Vector((sx * 0.4, 0.3, 1.3)), 4, n, streaks=0.6)
    warn_plate(B, Vector((0, 0.44, 2.0)), (0, 1, 0), 0.3)
    B.box(Vector((0, 0, 3.05)), (1.0, 1.0, 0.1), I3, "metal", C_DARK, 0.15)
    bolt_row(B, Vector((-0.4, -0.4, 3.1)), Vector((0.4, -0.4, 3.1)), 4, (0, 0, 1))
    hazard(B, Vector((-0.4, 0.402, -0.95)), (1, 0, 0), (0, 0, 1), 0.8, 0.12, (0, 1, 0), pitch=0.1)
    grime(B, Vector((0, 0.402, -0.75)), (0, 1, 0), 0.78, 0.25)
    cable(B, [Vector((0.25, -0.43, -0.95)), Vector((0.25, -0.43, 0.5)), Vector((0.25, -0.43, 2.4)), Vector((0.1, -0.43, 2.75))], 0.025, clips=(1, 2))
    top = 2.8
    i_beam(B, Vector((0, 0)), ARM_A, top - 0.12, top + 0.12, 0.24, "metal", C_BLUE)                       # boom
    i_beam(B, ARM_A, ARM_B, top - 0.12, top + 0.12, 0.24, "metal", C_BLUE)
    B.box(Vector((ARM_A.x, ARM_A.y, top)), (0.32, 0.32, 0.26), I3, "metal", C_DARK, 0.15)                   # splice block at the bend
    for sz in (-1, 1):
        stud(B, Vector((ARM_A.x, ARM_A.y, top + sz * 0.13)), (0, 0, sz), 0.04, 0.02)
    d = (ARM_B - ARM_A).normalized(); nrm = Vector((-d.y, d.x, 0))
    for t in (0.0, 0.33, 0.66, 1.0):                                         # hangers with clevis blocks
        p = ARM_A.lerp(ARM_B, t)
        B.box(Vector((p.x, p.y, (0.6 + top) / 2)), (0.08, 0.08, top - 0.6), I3, "steel", C_STEEL, 0.1)
        for z in (0.66, top - 0.18):
            B.box(Vector((p.x, p.y, z)), (0.14, 0.14, 0.12), I3, "metal", C_DARK, 0.15, bevel=0)
    seg_box(B, ARM_A, ARM_B, 0.2, 0.6, 0.1, "panel", C_WHITE)               # blade
    seg_box(B, ARM_A, ARM_B, 0.04, 0.2, 0.12, "rubber", C_BLACK)             # squeegee edge
    seg_box(B, ARM_A, ARM_B, 0.45, 0.52, 0.11, "panel", C_YELLOW)
    for s in (-1, 1):                                                         # clamp bolts along both faces of the blade
        off = nrm * s * 0.055
        a3 = Vector((ARM_A.x, ARM_A.y, 0.25)) + Vector((off.x, off.y, 0)) + Vector((d.x, d.y, 0)) * 0.15
        b3 = Vector((ARM_B.x, ARM_B.y, 0.25)) + Vector((off.x, off.y, 0)) - Vector((d.x, d.y, 0)) * 0.15
        bolt_row(B, a3, b3, 6, Vector((nrm.x * s, nrm.y * s, 0)), 0.018, 0.01, streaks=0.3)
    B.cyl(Vector((ARM_B.x, ARM_B.y, top + 0.12)), Vector((ARM_B.x, ARM_B.y, top + 0.2)), 0.12, 12, "glow", C_AMBER, 0.05)
    finish(B, "Frame", coll)
    return coll

# ---------------------------------------------------------------------------- the scree slope
def build_slot_sieve():
    """Bar grate over a drag-floor hopper. Bars, hopper floor and walls keep their collider numbers; the detail is
    bolted end walls, stiffener ribs and weld beads, a vented drive side and grime where the pebbles pour through."""
    random.seed(1321)
    coll = clear_collection("Room_SlotSieve")
    B = Builder()
    B.box(Vector((4.95, 1.0, -0.6)), (0.1, 4.0, 0.8), I3, "metal", C_FRAME, 0.2, rust=0.3)                # closed side
    B.box(Vector((-0.95, 1.0, -0.3)), (0.1, 4.0, 0.2), I3, "metal", C_FRAME, 0.2, rust=0.3)               # open side: rail only
    for y in (-0.9, 2.9):
        open_box(B, Vector((-0.95, y, -0.65)), (0.1, 0.2, 0.7), I3, "metal", C_FRAME, 0.2, rust=0.3)
        stud(B, Vector((-1.0, y, -0.45)), (-1, 0, 0), 0.025, 0.015)
    for x in SIEVE_BARS:
        B.box(Vector((x, 1.0, -0.25)), (0.08, 4.0, 0.1), I3, "steel", (0.55, 0.56, 0.6), 0.1, rust=0.3)
    for y in (0.0, 2.0):                                                     # cross ties under the bars
        B.box(Vector((2.0, y, -0.34)), (5.8, 0.06, 0.06), I3, "metal", C_DARK, 0.15)
    open_box(B, Vector((2.0, 1.0, -0.975)), (6.0, 4.0, 0.05), I3, "steel", L["C_WEAR"], 0.15, rust=0.2)     # hopper floor
    for y in (-0.975, 2.975):
        B.box(Vector((2.0, y, -0.65)), (6.0, 0.05, 0.7), I3, "metal", C_FRAME, 0.2, rust=0.3)
        n = Vector((0, 1 if y < 0 else -1, 0))
        for k in range(5):                                                   # stiffener ribs on the inner faces
            x = 0.2 + k * 1.2
            open_box(B, Vector((x, y + n.y * 0.035, -0.62)), (0.04, 0.02, 0.5), I3, "metal", C_FRAME, 0.2, rust=0.3, bevel=0, drop=(0, -n.y, 0))
        seam(B, Vector((-0.95, y + n.y * 0.026, -0.945)), Vector((4.9, y + n.y * 0.026, -0.945)), n, 0.02, col=(0.08, 0.07, 0.06))
        o = Vector((0, -n.y, 0))                                             # bolts along the outer top edge
        bolt_row(B, Vector((-0.8, y - n.y * 0.026, -0.36)), Vector((4.8, y - n.y * 0.026, -0.36)), 8, o, streaks=0.35)
    panel_face(B, Vector((4.99, 1.0, -0.6)), (1, 0, 0), 3.6, 0.6, C_WHITE)
    grille(B, Vector((5.0, -0.5, -0.62)), (1, 0, 0), 0.4, 0.35, 4)
    grille(B, Vector((5.0, 2.5, -0.62)), (1, 0, 0), 0.4, 0.35, 4)
    warn_plate(B, Vector((5.03, 1.0, -0.3)), (1, 0, 0), 0.2)
    for k in range(5):                                                       # drag-chain slats on the hopper floor
        B.box(Vector((2.0, 0.2 + k * 0.7, -0.94)), (5.8, 0.05, 0.03), I3, "metal", C_DARK, 0.1)
    for k in range(4):                                                       # grime where the fines land
        grime(B, Vector((random.uniform(0.5, 3.5), random.uniform(-0.2, 2.2), -0.95)), (0, 0, 1), random.uniform(0.8, 1.6), random.uniform(0.5, 1.0))
    hazard(B, Vector((-1.0, -1.0, -0.2)), (1, 0, 0), (0, 1, 0), 6.0, 0.12, (0, 0, 1), pitch=0.15)
    finish(B, "Frame", coll)
    return coll

def build_terrace_catcher():
    """Catch trough: bolted impact pad, a coping rail and rib-stiffened back wall, exit chevrons and a lit exit."""
    random.seed(1331)
    coll = clear_collection("Room_TerraceCatcher")
    B = Builder()
    open_box(B, Vector((2.0, 0, -0.95)), (6.0, 2.0, 0.1), I3, "steel", L["C_WEAR"], 0.15, rust=0.4)        # floor
    for k in range(8):
        B.box(Vector((-0.6 + k * 0.75, 0, -0.895)), (0.05, 1.8, 0.02), I3, "metal", C_DARK, 0.1)          # drag slats
    B.box(Vector((2.0, -0.95, -0.3)), (6.0, 0.1, 1.2), I3, "metal", C_FRAME, 0.2, rust=0.3)                # back wall
    B.box(Vector((2.0, -0.95, 0.28)), (6.0, 0.14, 0.04), I3, "steel", C_STEEL, 0.15, rust=0.3)             # coping rail
    B.box(Vector((2.0, -0.88, -0.2)), (5.8, 0.05, 0.8), I3, "rubber", C_BLACK, 0.1)                        # impact pad
    for z in (-0.56, 0.16):                                                  # pad clamp bars and their bolts
        B.box(Vector((2.0, -0.85, z)), (5.8, 0.012, 0.05), I3, "steel", C_STEEL, 0.15, rust=0.3)
        bolt_row(B, Vector((-0.8, -0.844, z)), Vector((4.8, -0.844, z)), 8, (0, 1, 0), 0.015, 0.01)
    for k in range(6):                                                       # scuffs on the pad where the slabs hit
        decal(B, Vector((random.uniform(-0.5, 4.5), -0.8545, random.uniform(-0.4, 0.0))), (0, 1, 0), random.uniform(0.3, 0.7), 0.08,
              "rubber", (0.12, 0.11, 0.1), 0.2, up=Vector((0.3, 0, 1)))
    B.box(Vector((2.0, 0.95, -0.75)), (6.0, 0.1, 0.3), I3, "metal", C_FRAME, 0.2, rust=0.3)                # low front lip
    hazard(B, Vector((-1.0, 1.001, -0.85)), (1, 0, 0), (0, 0, 1), 6.0, 0.2, (0, 1, 0), pitch=0.12)
    bolt_row(B, Vector((-0.8, 0.95, -0.6)), Vector((4.8, 0.95, -0.6)), 6, (0, 0, 1), 0.016, 0.01)
    B.box(Vector((-0.95, 0, -0.55)), (0.1, 2.0, 0.8), I3, "metal", C_FRAME, 0.2, rust=0.3)                 # closed end
    grille(B, Vector((-1.0, 0.3, -0.55)), (-1, 0, 0), 0.5, 0.35, 4)
    panel_face(B, Vector((2.0, -1.001, -0.3)), (0, -1, 0), 5.6, 1.0, C_WHITE)
    bolt_row(B, Vector((-0.8, -1.02, 0.18)), Vector((4.8, -1.02, 0.18)), 6, (0, -1, 0), streaks=0.5)
    for x in (4.6, 5.0):                                                     # exit marker at the open end
        B.box(Vector((x, 0.95, -0.4)), (0.05, 0.12, 0.4), I3, "glow", C_AMBER, 0.05)
    for k in range(3):                                                       # painted exit chevrons on the floor
        x = 3.4 + k * 0.45
        for s in (-1, 1):
            decal(B, Vector((x - 0.14, s * 0.14, -0.9)), (0, 0, 1), 0.1, 0.42, "panel", C_YELLOW, 0.2, up=Vector((-1.0, s * 1.0, 0)), lift=0.0015)
    grime(B, Vector((1.0, -0.6, -0.9)), (0, 0, 1), 3.0, 0.5)
    finish(B, "Frame", coll)
    return coll

# ---------------------------------------------------------------------------- the scales
def build_balance_floor():
    """20 x 8 m deck: tread plates, bolted seams near the rails, stanchioned side rails, cross girders under the
    deck (seen when it tips), and a hinge barrel in three bolted bearing blocks."""
    random.seed(1341)
    coll = clear_collection("Room_BalanceFloor")
    B = Builder()
    B.box(Vector((0, 0, 0)), (20.0, 8.0, 0.4), I3, "metal", C_FRAME, 0.2, rust=0.3)
    for k in range(20):                                                      # deck plates
        B.box(Vector((-9.5 + k, 0, 0.202)), (0.96, 7.8, 0.004), I3, "steel", jit3((0.45, 0.46, 0.5) if k % 2 else (0.4, 0.41, 0.45), 0.04), 0.1)
        if k:
            for y in (-3.5, 3.5):
                stud(B, Vector((-10 + k, y, 0.204)), (0, 0, 1), 0.02, 0.008, seg=5, rust=0.3)
    for k in range(5):                                                       # scuffed lanes where the shot rolls
        decal(B, Vector((random.uniform(-7, 7), random.uniform(-2.5, 2.5), 0.2045)), (0, 0, 1), 0.35, random.uniform(3, 6),
              "steel", (0.37, 0.37, 0.39), 0.1, up=Vector((1, 0, 0)), lift=0.0005)
    for y in (-3.9, 3.9):                                                    # side rails
        s = 1 if y > 0 else -1
        B.box(Vector((0, y, 0.35)), (20.0, 0.2, 0.3), I3, "panel", C_WHITE, 0.3)
        hazard(B, Vector((-10, y + s * 0.101, 0.21)), (1, 0, 0), (0, 0, 1), 20.0, 0.28, (0, s, 0), pitch=0.3)
        for k in range(11):                                                  # rail stanchion plates and their bolts
            x = -9.6 + k * 1.92
            open_box(B, Vector((x, y + s * 0.104, 0.0)), (0.12, 0.008, 0.36), I3, "metal", C_DARK, 0.15, rust=0.3, bevel=0, drop=(0, -s, 0))
    for x in (-9.9, 9.9):                                                    # open ends, marked
        B.box(Vector((x, 0, 0.205)), (0.2, 7.6, 0.01), I3, "panel", C_YELLOW, 0.1)
    B.box(Vector((0, 0, 0.203)), (0.1, 7.6, 0.004), I3, "panel", C_BLACK, 0.1)                             # centre line
    for x in (-7.5, -4.5, 4.5, 7.5):                                         # cross girders under the deck
        B.box(Vector((x, 0, -0.26)), (0.2, 7.2, 0.12), I3, "metal", C_DARK, 0.15, rust=0.3)
    for y in (-2.5, 2.5):                                                    # long stringers, stopped either side of the hinge
        for sx in (-1, 1):
            B.box(Vector((sx * 4.95, y, -0.26)), (9.1, 0.2, 0.12), I3, "metal", C_DARK, 0.15, rust=0.3)
    B.cyl(Vector((0, -4.0, -0.35)), Vector((0, 4.0, -0.35)), 0.25, 20, "steel", C_STEEL, 0.15, rust=0.2)    # hinge barrel
    for sy in (-1, 1):
        ring(B, Vector((0, sy * 3.95, -0.35)), (0, 1, 0), 0.2, 0.26, 0.1, 20, "steel", C_STEEL, 0.1)
    for y in (-3.6, 0, 3.6):                                                 # bearing blocks
        B.box(Vector((0, y, -0.25)), (0.7, 0.3, 0.3), I3, "metal", C_DARK, 0.15)
        for sx in (-1, 1):
            stud(B, Vector((sx * 0.28, y, -0.1)), (0, 0, 1), 0.025, 0.015)
            stud(B, Vector((sx * 0.28, y - 0.15, -0.3)), (0, -1, 0), 0.02, 0.012)
    finish(B, "Deck", coll)
    return coll

def build_counterweight_sled():
    """A cast weight on flanged wheels: stacked slabs with lifting eyes, bolted axle boxes, rails on sleepers,
    a stencilled load plate and rust where the slabs meet."""
    random.seed(1351)
    coll = clear_collection("Room_CounterweightSled")
    B = Builder()
    for y in (-1.2, 1.2):
        B.box(Vector((0, y, -0.95)), (3.2, 0.12, 0.1), I3, "steel", C_STEEL, 0.1, rust=0.4)               # rails
    for x in (-1.3, -0.45, 0.45, 1.3):                                       # sleepers
        open_box(B, Vector((x, 0, -0.99)), (0.2, 2.56, 0.02), I3, "wood", C_WOOD, 0.25, bevel=0)
    B.box(Vector((0, 0, -0.5)), (2.0, 2.6, 0.8), I3, "metal", (0.18, 0.19, 0.2), 0.2, rust=0.4, bevel=0.03)  # the weight
    for z in (-0.7, -0.38):                                                  # cast lines between the stacked blocks
        for y in (-1.3, 1.3):
            seam(B, Vector((-1.0, y, z)), Vector((1.0, y, z)), (0, 1 if y > 0 else -1, 0), 0.015)
    for k in range(4):
        B.box(Vector((-0.75 + k * 0.5, 0, -0.08)), (0.4, 2.4, 0.04), I3, "metal", C_DARK, 0.1)             # stacked slabs
        for y in (-0.9, 0.9):
            stud(B, Vector((-0.75 + k * 0.5, y, -0.06)), (0, 0, 1), 0.03, 0.02)
    hazard(B, Vector((-1.0, -1.301, -0.85)), (1, 0, 0), (0, 0, 1), 2.0, 0.2, (0, -1, 0), pitch=0.12)
    decal(B, Vector((0, -1.302, -0.45)), (0, -1, 0), 0.6, 0.3, "panel", C_WHITE, 0.2)                     # load plate
    for k in range(3):
        decal(B, Vector((-0.15 + k * 0.15, -1.3045, -0.45)), (0, -1, 0), 0.08, 0.16, "panel", C_BLACK, 0.1)
    for x in (-0.8, 0.8):
        for y in (-1.2, 1.2):
            s = 1 if y > 0 else -1
            B.cyl(Vector((x, y - 0.08, -0.82)), Vector((x, y + 0.08, -0.82)), 0.12, 12, "steel", C_STEEL)   # wheels
            B.cyl(Vector((x, y + s * 0.08, -0.82)), Vector((x, y + s * 0.1, -0.82)), 0.05, 6, "steel", C_STEEL)  # hub cap
    for k in range(5):
        streak(B, Vector((random.uniform(-0.9, 0.9), -1.3, -0.15)), (0, -1, 0), 0.05, random.uniform(0.2, 0.45))
    B.box(Vector((1.02, 0, -0.5)), (0.04, 0.8, 0.3), I3, "metal", C_BLUE, 0.15)
    B.box(Vector((1.045, 0, -0.5)), (0.004, 0.5, 0.08), I3, "glow", C_AMBER, 0.05)
    finish(B, "Frame", coll)
    return coll

def build_tilt_gauge():
    """Pendulum level gauge: bolted foot plate, braced post, a bezel-ringed dial and a cable to the deck."""
    random.seed(1361)
    coll = clear_collection("Room_TiltGauge")
    B = Builder()
    open_box(B, Vector((0, 0, 0.5)), (0.25, 0.25, 3.0), I3, "metal", C_FRAME, 0.2, rust=0.3)
    open_box(B, Vector((0, 0, -0.985)), (0.5, 0.25, 0.03), I3, "steel", C_STEEL, 0.2, rust=0.5, bevel=0.01)
    for sx in (-1, 1):
        for sy in (-1, 1):
            stud(B, Vector((sx * 0.19, sy * 0.07, -0.97)), (0, 0, 1), 0.02, 0.02)
    grime(B, Vector((0, -0.126, -0.8)), (0, -1, 0), 0.24, 0.35)
    c = Vector((0, -0.2, 2.2))
    B.cyl(c, c + Vector((0, -0.08, 0)), 0.8, 32, "metal", C_DARK)
    ring(B, c + Vector((0, -0.085, 0)), (0, 1, 0), 0.72, 0.8, 0.03, 32, "steel", C_STEEL, 0.1, rust=0.2)     # bezel
    B.cyl(c + Vector((0, -0.08, 0)), c + Vector((0, -0.09, 0)), 0.72, 32, "panel", (0.85, 0.84, 0.8), 0.1)
    for k in range(8):
        a = (k + 0.5) / 8 * math.tau
        stud(B, c + Vector((math.cos(a) * 0.76, -0.1, math.sin(a) * 0.76)), (0, -1, 0), 0.015, 0.01, rust=0.2)
    for k in range(-5, 6):                                                   # scale: green at level, red at the stops
        a = math.radians(k * 9)
        col = (0.2, 0.8, 0.3) if abs(k) <= 1 else (C_YELLOW if abs(k) <= 3 else (0.85, 0.15, 0.1))
        p = c + Vector((math.sin(a) * 0.62, -0.095, -math.cos(a) * 0.62))
        B.box(p, (0.03, 0.004, 0.1 if k % 5 else 0.16), Matrix.Rotation(-a, 3, 'Y'), "panel", col, 0.05)
    B.box(c + Vector((0, -0.1, -0.3)), (0.03, 0.004, 0.62), I3, "glow", (1.0, 0.15, 0.1), 0.05)            # pendulum needle
    B.cyl(c + Vector((0, -0.09, 0)), c + Vector((0, -0.12, 0)), 0.06, 12, "steel", C_STEEL)
    B.box(Vector((0, -0.16, 1.9)), (0.12, 0.1, 0.5), I3, "metal", C_FRAME, 0.2, rust=0.3)                   # dial bracket
    for sx in (-1, 1):                                                       # braces from the post to the dial rim
        B.pipe([Vector((sx * 0.1, -0.13, 1.2)), Vector((sx * 0.45, -0.19, 1.62))], 0.025, 6, "metal", C_FRAME)
    B.box(Vector((0, -0.14, 0.9)), (0.14, 0.04, 0.2), I3, "metal", C_BLUE, 0.15)                            # sensor box
    cable(B, [Vector((0, -0.16, 0.8)), Vector((0, -0.16, -0.4)), Vector((0, -0.2, -0.95)), Vector((0, -0.3, -0.98))], 0.015, clips=(1,))
    warn_plate(B, Vector((0, -0.127, 0.3)), (0, -1, 0), 0.18)
    finish(B, "Frame", coll)
    return coll

PIECES = [
    (build_turntable, "turntable.glb"),
    (build_sweep_arm, "sweep_arm.glb"),
    (build_slot_sieve, "slot_sieve.glb"),
    (build_terrace_catcher, "terrace_catcher.glb"),
    (build_balance_floor, "balance_floor.glb"),
    (build_counterweight_sled, "counterweight_sled.glb"),
    (build_tilt_gauge, "tilt_gauge.glb"),
]
ICONS = []

def build_all(do_export=True):
    return build_family(PIECES, OUT_DIR, do_export)

if __name__ == "__main__":
    build_all(do_export=False)
