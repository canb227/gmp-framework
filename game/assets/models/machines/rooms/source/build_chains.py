"""
Machines for the two tag-themed production chains, shown in the Puzzle Rooms wing (rooms 5 and 6). Same frame
as the other families: origin at the anchor cell centre, floor z = -1, +Y front (flow).

Electric chain (Room 5, the Storm Cage):
  storm_collector.glb   1x2x3  lightning mast over an insulated strike pad; the belt runs across the pad and
                               whatever is on it when the bolt lands is fused (sand -> charged fulgurite)   Bolt, Glow
  insulated_belt.glb    1x1x1  belt on ceramic stand-offs with rubber skirts: carries charged items without
                               grounding them
  capacitor_press.glb   1x1x1  pass-through press that seals a fulgurite's charge into a copper-wrapped cell
                               before it leaks away                                                         Ram, Coil

Sticky chain (Room 6, the Tar Pit):
  belt_scraper.glb      1x1x1  belt whose head roller has a sprung blade that peels stuck blobs off the belt  Blade
  coating_drum.glb      (rotating part) 6 m drum with lifter bars, origin on its axis (+X), radius 1.4
  drum_cradle.glb       static cradle, trunnion rollers and motor for the drum, origin on the drum axis
"""
import bpy, bmesh, math, random, os
from mathutils import Vector, Matrix

_HERE = (os.path.dirname(os.path.abspath(__file__)) if "__file__" in globals()
         else r"C:\\Users\\steph\\OneDrive\\Documents\\godot\\projects\\gmp-framework\\game\\assets\\models\\machines\\rooms\\source")
_CON = os.path.normpath(os.path.join(_HERE, "..", "..", "concepts", "source", "build_concepts.py"))
_S = {"__name__": "concepts_lib", "__file__": _CON}
exec(compile(open(_CON, encoding="utf-8").read(), _CON, "exec"), _S)
_SKIP = {"HERE", "OUT_DIR", "PIECES", "build_all", "ICONS", "export", "CONV"}
globals().update({k: v for k, v in _S.items() if not k.startswith("__") and k not in _SKIP})
HERE = (os.path.dirname(os.path.abspath(__file__)) if "__file__" in globals() else _HERE)   # (the exec above rebinds _HERE)
OUT_DIR = os.path.dirname(HERE)

C_CERAMIC = (0.85, 0.45, 0.12)
C_TAR = (0.06, 0.05, 0.045)
DRUM_R, DRUM_L = 1.4, 6.0
TYRE_X = DRUM_L / 2 - 0.3                     # riding tyres (and the trunnion rollers under them)
GEAR_R = (DRUM_R + 0.1, DRUM_R + 0.245)      # girth gear: on the shell, out to its tooth tips
PINION = Vector((0, -0.8, -1.59))            # drive pinion centre (meshes with the girth gear)

# The concepts family (exec'd above) supplies the detail helpers used here: stud, bolt_row, bolt_circle, decal,
# streak, seam, warn_plate, grille, cable, gusset and motor.

def insulators(B, xs=(-0.93, 0.93), ys=(-0.6, 0.6)):
    for x in xs:
        for y in ys:
            for k in range(3):                                               # ribbed ceramic stand-off
                B.cyl(Vector((x, y, -0.97 + k * 0.07)), Vector((x, y, -0.93 + k * 0.07)), 0.07 - 0.01 * (k % 2), 12, "panel", C_CERAMIC, 0.1)

def band(B, x, w, r_in, r_out, seg, mat, col, var=0.12, rust=0.0, teeth=False, inner=False, paint=None):
    """Band round the X axis (a drum tyre, mouth ring or toothed girth gear): both side faces and the outer face,
    plus the inner face when it can be seen (inner=True). teeth drops every other outer vertex to the tooth root."""
    def loop(xx, r, toothed=False):
        root = r_in + (r - r_in) * 0.6
        return [Vector((xx, math.cos(k / seg * math.tau), math.sin(k / seg * math.tau))) * 1.0 for k in range(seg)], \
               [(r if (not toothed or k % 2) else root) for k in range(seg)]
    def pts(xx, r, toothed=False):
        dirs, rs = loop(xx, r, toothed)
        return [Vector((xx, d.y * rr, d.z * rr)) for d, rr in zip(dirs, rs)]
    rings = [pts(x - w / 2, r_in), pts(x - w / 2, r_out, teeth), pts(x + w / 2, r_out, teeth), pts(x + w / 2, r_in)]
    if inner:
        rings.append(pts(x - w / 2, r_in))
    fs = B.tube_rings(rings, mat, col, var, rust, cap=False, smooth=False)
    for f in fs:                                                             # outer face smooth, sides flat
        f.normal_update()
        f.smooth = abs(f.normal.x) < 0.5 and not teeth
    if paint:
        for k, f in enumerate(fs):
            B.paint([f], paint(k % seg), var)
    return fs

def spring(B, p0, p1, r, turns, wire=0.008, seg=4, col=C_YELLOW):
    """Coil spring from p0 to p1 (a helical wire)."""
    ax = (p1 - p0); L_ = ax.length; ax.normalize(); R = rot_to(ax)
    n = int(turns * 8)
    pts = [p0 + ax * (L_ * i / n) + R @ Vector((math.cos(i / 8 * math.tau) * r, math.sin(i / 8 * math.tau) * r, 0)) for i in range(n + 1)]
    B.pipe(pts, wire, seg, "panel", col)

# ====================================================================================== electric
def build_storm_collector():
    rng = random.Random(1401); random.seed(1401)
    coll = clear_collection("Chain_StormCollector")
    B, G = Builder(), Builder()
    # strike pad: an insulated plate the length of the machine (belt height), glass cheeks, copper grid inlay
    B.box(Vector((0, 1.0, (BELT_TOP - 1) / 2)), (1.9, 3.96, BELT_TOP + 1), I3, "metal", C_FRAME, 0.2, rust=0.3)
    B.box(Vector((0, 1.0, BELT_TOP - 0.005)), (1.7, 3.8, 0.01), I3, "rubber", (0.1, 0.1, 0.11), 0.1)
    for k in range(7):
        B.box(Vector((0, -0.6 + k * 0.53, BELT_TOP + 0.002)), (1.5, 0.03, 0.006), I3, "copper", C_COPPER, 0.1)
    for sx in (-1, 1):                                                       # pad side: grounding bus bar and its lugs
        B.box(Vector((sx * 0.955, 1.0, -0.93)), (0.012, 3.7, 0.04), I3, "copper", C_COPPER, 0.1)
        bolt_row(B, Vector((sx * 0.961, -0.6, -0.93)), Vector((sx * 0.961, 2.6, -0.93)), 5, (sx, 0, 0), 0.014, 0.01)
    insulators(B, ys=(-0.7, 0.9, 2.7))
    for sx in (-1, 1):
        G.box(Vector((sx * 0.94, 1.0, (BELT_TOP + 0.6) / 2)), (0.02, 3.6, 0.6 - BELT_TOP), I3, "glass", (0.55, 0.9, 1.0), 0.02)
        B.box(Vector((sx * 0.94, 1.0, 0.62)), (0.06, 3.9, 0.06), I3, "metal", C_DARK, 0.15)
        for y in (-0.8, 1.0, 2.8):                                           # glass retaining clips
            B.box(Vector((sx * 0.94, y, 0.575)), (0.05, 0.08, 0.04), I3, "steel", C_STEEL, 0.15, rust=0.3)
    # cage arch: four posts to a ring beam, a mast from a cross beam to the rod tip at z 4.8
    for (x, y) in ((-0.92, -0.92), (0.92, -0.92), (0.92, 2.92), (-0.92, 2.92)):
        B.box(Vector((x, y, 0.45)), (0.12, 0.12, 2.9), I3, "metal", C_FRAME, 0.2, rust=0.3)
        sx, sy = -math.copysign(1, x), -math.copysign(1, y)
        gusset(B, Vector((x, y + sy * 0.06, 1.84)), (0, 0, -1), (0, sy, 0), 0.22)       # beam-to-post gussets
        gusset(B, Vector((x + sx * 0.06, y, 1.84)), (0, 0, -1), (sx, 0, 0), 0.22)
        stud(B, Vector((x, y, 1.96)), (0, 0, 1), 0.02, 0.012)
    for y in (-0.92, 2.92):
        B.box(Vector((0, y, 1.9)), (1.96, 0.12, 0.12), I3, "metal", C_DARK, 0.15)
    for x in (-0.92, 0.92):
        B.box(Vector((x, 1.0, 1.9)), (0.12, 3.96, 0.12), I3, "metal", C_DARK, 0.15)
    for k in range(9):                                                       # Faraday cage bars over the pad
        B.box(Vector((0, -0.6 + k * 0.45, 1.9)), (1.84, 0.03, 0.03), I3, "copper", C_COPPER, 0.1)
    B.box(Vector((0, 1.0, 1.99)), (1.96, 0.14, 0.1), I3, "metal", C_DARK, 0.15)                            # mast cross beam
    B.cyl(Vector((0, 1.0, 2.04)), Vector((0, 1.0, 2.08)), 0.2, 12, "metal", C_DARK, 0.15)                  # mast flange
    bolt_circle(B, Vector((0, 1.0, 2.08)), (0, 0, 1), 0.15, 6, 0.016, 0.012)
    B.cyl(Vector((0, 1.0, 1.9)), Vector((0, 1.0, 4.7)), 0.08, 12, "steel", C_STEEL)
    for z in (2.4, 3.1, 3.8):                                                # insulator stacks: a big disc and two small ribs
        ring(B, Vector((0, 1.0, z)), (0, 0, 1), 0.08, 0.28, 0.06, 16, "panel", C_CERAMIC, 0.1)
        for dz in (-0.1, 0.1):
            ring(B, Vector((0, 1.0, z + dz)), (0, 0, 1), 0.08, 0.17, 0.04, 12, "panel", C_CERAMIC, 0.1)
    B.cyl(Vector((0, 1.0, 4.7)), Vector((0, 1.0, 4.95)), 0.03, 8, "copper", C_COPPER)                      # rod tip
    cable(B, [Vector((0.09, 1.0, 2.2)), Vector((0.3, 1.0, 2.1)), Vector((0.86, 1.0, 2.06)), Vector((0.86, 2.86, 2.0)),
              Vector((0.86, 2.86, 0.5)), Vector((0.86, 2.86, -0.8))], 0.016, clips=(2, 4), col=(0.55, 0.3, 0.12), mat="copper")   # down-conductor
    for sx in (-1, 1):                                                       # charge lamps
        B.box(Vector((sx * 0.98, -0.95, 0.3)), (0.04, 0.04, 0.3), I3, "violet", C_VIOLET, 0.05)
    warn_plate(B, Vector((0, -0.98, 1.9)), (0, -1, 0), 0.2)
    hazard(B, Vector((-0.95, -1.0, BELT_TOP - 0.12)), (1, 0, 0), (0, 0, 1), 1.9, 0.1, (0, -1, 0), pitch=0.1)
    for k in range(4):
        streak(B, Vector((rng.uniform(-0.8, 0.8), -0.99, -0.87)), (0, -1, 0), 0.04, rng.uniform(0.05, 0.1))
    finish(B, "Frame", coll); finish(G, "Glass", coll)
    # the bolt: a jagged glowing zig-zag from the rod tip to the pad, flashing every 6 s
    Bo = Builder()
    pts = [Vector((0, 0, 0))]
    for k in range(1, 9):
        pts.append(Vector((rng.uniform(-0.25, 0.25), rng.uniform(-0.25, 0.25), -k * (4.7 - BELT_TOP) / 8)))
    for a, b in zip(pts, pts[1:]):
        Bo.cyl(a, b, 0.04, 6, "violet", (0.85, 0.75, 1.0), 0.02)
    bolt = node(Bo, "Bolt", coll, Vector((0, 1.0, 4.7)))
    keys(bolt, (1, 170, 171, 173, 175, 176, 181), "scale",
         [Vector((0.01, 0.01, 0.01)), Vector((0.01, 0.01, 0.01)), Vector((1, 1, 1)), Vector((1.4, 1.4, 1)), Vector((1, 1, 1)),
          Vector((0.01, 0.01, 0.01)), Vector((0.01, 0.01, 0.01))])
    Gl = Builder()
    Gl.box(Vector((0, 0, 0)), (1.7, 3.8, 0.4), I3, "field_ag", C_VIOLET, 0.02)
    glow = node(Gl, "Glow", coll, Vector((0, 1.0, BELT_TOP + 0.2)))
    keys(glow, (1, 170, 172, 180, 181), "scale", [Vector((1, 1, 0.01)), Vector((1, 1, 0.01)), Vector((1, 1, 1.2)),
                                                   Vector((1, 1, 0.1)), Vector((1, 1, 0.01))])
    return coll

def build_insulated_belt():
    rng = random.Random(1411); random.seed(1411)
    coll = clear_collection("Chain_InsulatedBelt")
    B = Builder()
    belt_cell(coll, rng, B, guards=False)
    for sx in (-1, 1):                                                       # rubber skirts instead of steel guards
        B.box(Vector((sx * 0.9, 0, BELT_TOP + 0.2)), (0.06, 1.96, 0.4), I3, "rubber", (0.12, 0.1, 0.09), 0.1)
        B.box(Vector((sx * 0.9, 0, BELT_TOP + 0.41)), (0.08, 1.96, 0.03), I3, "panel", C_CERAMIC, 0.1)
        B.box(Vector((sx * 0.935, 0, BELT_TOP + 0.33)), (0.012, 1.9, 0.05), I3, "steel", C_STEEL, 0.15, rust=0.3)   # clamp strip
        bolt_row(B, Vector((sx * 0.941, -0.8, BELT_TOP + 0.33)), Vector((sx * 0.941, 0.8, BELT_TOP + 0.33)), 5, (sx, 0, 0), 0.013, 0.01)
        decal(B, Vector((sx * 0.931, 0.45, BELT_TOP + 0.16)), (sx, 0, 0), 0.34, 0.12, "panel", C_CERAMIC, 0.15)          # "insulated" label
        for k in range(3):
            decal(B, Vector((sx * 0.9315, 0.36 + k * 0.08, BELT_TOP + 0.16)), (sx, 0, 0), 0.05, 0.07, "panel", C_WHITE, 0.1)
    insulators(B)
    finish(B, "Frame", coll)
    return coll

def build_capacitor_press():
    rng = random.Random(1421); random.seed(1421)
    coll = clear_collection("Chain_CapacitorPress")
    B = Builder()
    belt_cell(coll, rng, B, guards=False)
    for sx in (-1, 1):
        B.box(Vector((sx * 0.9, 0, 0.0)), (0.16, 1.4, 1.8), I3, "metal", C_FRAME, 0.2, rust=0.3)
        panel_face(B, Vector((sx * 0.982, 0, 0.1)), (sx, 0, 0), 1.2, 1.3, C_WHITE)
        grille(B, Vector((sx * 1.0, -0.3, -0.42)), (sx, 0, 0), 0.4, 0.22, 4)
        grille(B, Vector((sx * 1.0, 0.3, -0.42)), (sx, 0, 0), 0.4, 0.22, 4)
        for y in (-0.7, 0.7):                                                # column faces: bolt lines where the beam lands
            bolt_row(B, Vector((sx * 0.9 - 0.05, y + (0.001 if y > 0 else -0.001), 0.6)), Vector((sx * 0.9 + 0.05, y + (0.001 if y > 0 else -0.001), 0.6)),
                     2, (0, 1 if y > 0 else -1, 0), 0.016, 0.012)
            streak(B, Vector((sx * 0.9, y + (0.002 if y > 0 else -0.002), 0.58)), (0, 1 if y > 0 else -1, 0), 0.04, 0.3)
    warn_plate(B, Vector((-0.9, -0.702, 0.2)), (0, -1, 0), 0.14)
    B.box(Vector((0, 0, 0.85)), (1.96, 1.4, 0.3), I3, "metal", C_DARK, 0.15)
    for y in (-0.7, 0.7):
        seam(B, Vector((-0.8, y, 0.85)), Vector((0.8, y, 0.85)), (0, 1 if y > 0 else -1, 0), 0.015)
    for sx in (-1, 1):                                                       # hydraulic hoses from the cylinder head to the pump
        cable(B, [Vector((sx * 0.3, 0.72, 0.9)), Vector((sx * 0.3, 0.8, 0.8)), Vector((sx * 0.75, 0.8, 0.8)), Vector((sx * 0.75, 0.8, -0.2))],
              0.018, clips=(2,), col=(0.05, 0.05, 0.05), mat="rubber")
    B.box(Vector((1.05, 0.0, -0.25)), (0.14, 0.9, 0.3), I3, "metal", C_BLUE, 0.2)                         # copper plate feed port
    B.box(Vector((1.13, 0.0, -0.25)), (0.02, 0.8, 0.14), I3, "copper", C_COPPER, 0.1)
    for sx in (-1, 1):
        B.box(Vector((sx * 0.99, -0.72, 0.5)), (0.03, 0.03, 0.4), I3, "violet", C_VIOLET, 0.05)
    finish(B, "Frame", coll)
    Ra = Builder()
    Ra.box(Vector((0, 0, 0)), (1.3, 1.1, 0.18), I3, "steel", C_STEEL, 0.15)
    Ra.cyl(Vector((0, 0, 0.09)), Vector((0, 0, 0.8)), 0.12, 12, "steel", C_STEEL)
    Ra.cyl(Vector((0, 0, 0.09)), Vector((0, 0, 0.13)), 0.2, 12, "steel", C_DARK, 0.1)                     # rod clevis flange
    bolt_circle(Ra, Vector((0, 0, 0.13)), (0, 0, 1), 0.16, 6, 0.016, 0.012)
    ram = node(Ra, "Ram", coll, Vector((0, 0, 0.1)))
    keys(ram, (1, 60, 66, 75, 90, 121), "location", [Vector((0, 0, 0.1)), Vector((0, 0, 0.1)), Vector((0, 0, -0.45)),
                                                    Vector((0, 0, -0.45)), Vector((0, 0, 0.1)), Vector((0, 0, 0.1))])
    Co = Builder()
    for k in range(6):
        ring(Co, Vector((0, 0, k * 0.07)), (0, 0, 1), 0.2, 0.26, 0.04, 16, "copper", C_COPPER if k % 2 else [c * 0.8 for c in C_COPPER], 0.08)
    coil = node(Co, "Coil", coll, Vector((0, 0, 0.35)))
    spin(coil, 2, 1, 120)
    return coll

# ====================================================================================== sticky
def build_belt_scraper():
    rng = random.Random(1431); random.seed(1431)
    coll = clear_collection("Chain_BeltScraper")
    B = Builder()
    belt_cell(coll, rng, B)
    for sx in (-1, 1):                                                       # blade brackets at the head roller
        B.box(Vector((sx * 0.92, 0.9, BELT_TOP - 0.1)), (0.1, 0.2, 0.5), I3, "metal", C_DARK, 0.15)
        for dz in (-0.25, 0.05):
            stud(B, Vector((sx * 0.97, 0.9, BELT_TOP + dz)), (sx, 0, 0), 0.016, 0.012)
        spring(B, Vector((sx * 0.8, 0.94, BELT_TOP - 0.34)), Vector((sx * 0.8, 0.94, BELT_TOP - 0.14)), 0.03, 3.5, seg=3)   # blade tensioners
    B.box(Vector((0, 0.95, BELT_TOP - 0.28)), (1.8, 0.1, 0.06), I3, "metal", C_DARK, 0.15)
    for k in range(5):                                                       # scraped-off tar smears
        B.box(Vector((-0.6 + k * 0.3, 0.97, BELT_TOP - 0.35 - 0.05 * (k % 2))), (0.12, 0.02, 0.1), I3, "rubber", C_TAR, 0.1)
    for k in range(4):                                                       # tar drips pooled on the floor under the head
        decal(B, Vector((-0.5 + k * 0.33 + rng.uniform(-0.05, 0.05), 0.85, -1.0)), (0, 0, 1), rng.uniform(0.12, 0.22), rng.uniform(0.1, 0.18),
              "rubber", C_TAR, 0.1, lift=0.003)
    B.box(Vector((0.96, 0.6, -0.4)), (0.06, 0.3, 0.3), I3, "metal", C_BLUE, 0.2)
    warn_plate(B, Vector((0.991, 0.6, -0.4)), (1, 0, 0), 0.14)
    finish(B, "Frame", coll)
    Bl = Builder()                                                           # sprung blade, twitching as blobs pass
    Bl.box(Vector((0, 0.08, -0.12)), (1.76, 0.02, 0.26), Matrix.Rotation(math.radians(-35), 3, 'X'), "steel", (0.55, 0.56, 0.6), 0.1)
    Bl.box(Vector((0, 0.16, -0.22)), (1.76, 0.03, 0.04), I3, "panel", C_YELLOW, 0.1)
    for k in range(6):                                                       # blade clamp bolts
        stud(Bl, Vector((-0.75 + k * 0.3, 0.176, -0.22)), (0, 1, 0), 0.012, 0.008, mat="panel", c=C_DARK)
    blade = node(Bl, "Blade", coll, Vector((0, 0.95, BELT_TOP - 0.02)))
    keys(blade, (1, 20, 24, 40, 60, 64, 81), "rotation_euler", [(0, 0, 0), (0, 0, 0), (0.25, 0, 0), (0, 0, 0), (0, 0, 0), (0.18, 0, 0), (0, 0, 0)])
    return coll

def build_coating_drum():
    """The rotating drum: a 32-sided shell with welded seams, two bolted riding tyres, a toothed girth gear in the
    middle (driven by the cradle's pinion), hazard-painted mouth rings, lifter bars and chalk streaks inside."""
    random.seed(1441)
    coll = clear_collection("Chain_CoatingDrum")
    B = Builder()
    R, L_ = DRUM_R, DRUM_L
    seg = 32
    ring_ = lambda x, r: [Vector((x, math.cos(k / seg * math.tau) * r, math.sin(k / seg * math.tau) * r)) for k in range(seg)]
    B.tube_rings([ring_(-L_ / 2, R + 0.1), ring_(L_ / 2, R + 0.1)], "metal", (0.3, 0.26, 0.2), 0.2, 0.4, cap=False, smooth=True)
    B.tube_rings([ring_(L_ / 2, R), ring_(-L_ / 2, R)], "steel", (0.35, 0.33, 0.3), 0.2, 0.5, cap=False, smooth=True)   # inner face
    for k in range(3):                                                       # longitudinal weld seams on the shell
        a = k / 3 * math.tau + 0.4
        n = Vector((0, math.cos(a), math.sin(a)))
        for x0, x1 in ((-TYRE_X + 0.2, -0.2), (0.2, TYRE_X - 0.2)):
            seam(B, n * (R + 0.1) + Vector((x0, 0, 0)), n * (R + 0.1) + Vector((x1, 0, 0)), n, 0.025, col=(0.2, 0.17, 0.13))
    for s in (-1, 1):                                                        # riding tyres, bolted on their outer side
        band(B, s * TYRE_X, 0.3, R + 0.1, R + 0.22, seg, "steel", C_STEEL, 0.1)
        bolt_circle(B, Vector((s * (TYRE_X + 0.15), 0, 0)), (s, 0, 0), R + 0.16, 12, 0.02, 0.014)
    band(B, 0.0, 0.2, GEAR_R[0], GEAR_R[1], 64, "steel", (0.3, 0.3, 0.3), 0.1, rust=0.2, teeth=True)       # girth gear
    for k in range(4):                                                       # lifter bars inside
        a = k / 4 * math.tau
        B.box(Vector((0, math.cos(a) * (R - 0.12), math.sin(a) * (R - 0.12))), (L_ - 0.2, 0.08, 0.26), Matrix.Rotation(a + math.pi / 2, 3, 'X'),
              "metal", C_DARK, 0.15)
    for k in range(6):                                                       # chalk dust streaks on the inner face
        a = k / 6 * math.tau + 0.3
        B.box(Vector((random.uniform(-2, 2), math.cos(a) * (R - 0.01), math.sin(a) * (R - 0.01))), (1.2, 0.3, 0.004),
              Matrix.Rotation(a + math.pi / 2, 3, 'X'), "panel", (0.85, 0.84, 0.8), 0.1)
    for s in (-1, 1):                                                        # mouth rings, hazard painted
        band(B, s * L_ / 2, 0.12, R - 0.05, R + 0.25, seg, "panel", C_YELLOW, 0.1,
             inner=True, paint=lambda k: C_YELLOW if k % 4 < 2 else C_BLACK)
    finish(B, "Drum", coll)
    return coll

def build_drum_cradle():
    """Static cradle: trunnion rollers in pillow blocks on pedestals under each tyre, and a motor driving a
    gearbox whose pinion meshes with the drum's girth gear."""
    random.seed(1451)
    coll = clear_collection("Chain_DrumCradle")
    B = Builder()
    R = DRUM_R
    rz = -math.sqrt((R + 0.22 + 0.3) ** 2 - 0.9 ** 2)                        # roller centre height: touching the tyre
    for x in (-TYRE_X, TYRE_X):
        for s in (-1, 1):                                                    # trunnion rollers under each tyre
            c = Vector((x, s * 0.9, rz))
            B.cyl(c + Vector((-0.2, 0, 0)), c + Vector((0.2, 0, 0)), 0.3, 16, "steel", C_STEEL, 0.1)
            B.cyl(c + Vector((-0.34, 0, 0)), c + Vector((0.34, 0, 0)), 0.06, 8, "steel", C_STEEL, 0.1)      # axle
            for dx in (-0.29, 0.29):                                         # pillow blocks
                B.box(c + Vector((dx, 0, -0.06)), (0.1, 0.24, 0.2), I3, "metal", C_BLUE, 0.2, rust=0.1, bevel=0)
            ph = (c.z - 0.16) - (-R - 1.0 + 0.1)                             # pedestal from the base beam to the blocks
            B.box(Vector((x, s * 0.9, -R - 0.9 + ph / 2)), (0.7, 0.4, ph), I3, "metal", C_FRAME, 0.2, rust=0.3)
            for sy in (-1, 1):
                gusset(B, Vector((x - 0.35, s * 0.9 + sy * 0.2, -R - 0.9)), (0, 0, 1), (0, sy, 0), 0.18)
        slab(B, Vector((x, 0, -R - 1.0)), (0.7, 2.6, 0.2), I3, "metal", C_DARK, 0.15)
        for sx in (-1, 1):
            for sy in (-1, 1):
                stud(B, Vector((x + sx * 0.28, sy * 1.2, -R - 0.9)), (0, 0, 1), 0.02, 0.015)
    # drive: base plate, motor (axis Y) -> coupling -> gearbox -> pinion on the girth gear
    slab(B, Vector((0, -1.05, -R - 1.05)), (0.9, 1.5, 0.1), I3, "metal", C_DARK, 0.15)
    gb_top = PINION.z - 0.12
    B.box(Vector((0, -0.8, (gb_top + (-R - 1.0)) / 2)), (0.5, 0.45, gb_top + R + 1.0), I3, "metal", C_BLUE, 0.2, rust=0.15)   # gearbox
    grille(B, Vector((0.251, -0.8, -2.2)), (1, 0, 0), 0.3, 0.2, 3)
    B.cyl(PINION + Vector((-0.12, 0, 0)), PINION + Vector((0.12, 0, 0)), 0.17, 12, "steel", (0.3, 0.3, 0.3), 0.1, rust=0.2)   # pinion
    B.cyl(PINION + Vector((-0.26, 0, 0)), PINION + Vector((0.26, 0, 0)), 0.05, 8, "steel", C_STEEL, 0.1)
    motor(B, Vector((0, -1.52, -2.12)), (0, -1, 0), 0.22, 0.5)
    B.cyl(Vector((0, -1.27, -2.12)), Vector((0, -1.02, -2.12)), 0.06, 8, "steel", C_STEEL, 0.1)                      # coupling
    B.cyl(Vector((0, -1.18, -2.12)), Vector((0, -1.1, -2.12)), 0.1, 10, "metal", C_YELLOW, 0.1)                      # coupling guard
    cable(B, [Vector((0, -1.52, -1.86)), Vector((0.25, -1.6, -1.95)), Vector((0.4, -1.7, -2.3)), Vector((0.42, -1.72, -2.39))], 0.02,
          clips=(2,))
    warn_plate(B, Vector((0, -1.026, -2.0)), (0, -1, 0), 0.16)
    finish(B, "Frame", coll)
    return coll

PIECES = [
    (build_storm_collector, "storm_collector.glb"),
    (build_insulated_belt, "insulated_belt.glb"),
    (build_capacitor_press, "capacitor_press.glb"),
    (build_belt_scraper, "belt_scraper.glb"),
    (build_coating_drum, "coating_drum.glb"),
    (build_drum_cradle, "drum_cradle.glb"),
]
ICONS = []

def build_all(do_export=True):
    return build_family(PIECES, OUT_DIR, do_export)

if __name__ == "__main__":
    build_all(do_export=False)
