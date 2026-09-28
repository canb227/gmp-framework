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

def insulators(B, xs=(-0.93, 0.93), ys=(-0.6, 0.6)):
    for x in xs:
        for y in ys:
            for k in range(3):                                               # ribbed ceramic stand-off
                B.cyl(Vector((x, y, -0.97 + k * 0.07)), Vector((x, y, -0.93 + k * 0.07)), 0.07 - 0.01 * (k % 2), 12, "panel", C_CERAMIC, 0.1)

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
    insulators(B, ys=(-0.7, 0.9, 2.7))
    for sx in (-1, 1):
        G.box(Vector((sx * 0.94, 1.0, (BELT_TOP + 0.6) / 2)), (0.02, 3.6, 0.6 - BELT_TOP), I3, "glass", (0.55, 0.9, 1.0), 0.02)
        B.box(Vector((sx * 0.94, 1.0, 0.62)), (0.06, 3.9, 0.06), I3, "metal", C_DARK, 0.15)
    # cage arch: four posts to a ring beam, a mast from the ring to the rod tip at z 4.8
    for (x, y) in ((-0.92, -0.92), (0.92, -0.92), (0.92, 2.92), (-0.92, 2.92)):
        B.box(Vector((x, y, 0.45)), (0.12, 0.12, 2.9), I3, "metal", C_FRAME, 0.2, rust=0.3)
    for y in (-0.92, 2.92):
        B.box(Vector((0, y, 1.9)), (1.96, 0.12, 0.12), I3, "metal", C_DARK, 0.15)
    for x in (-0.92, 0.92):
        B.box(Vector((x, 1.0, 1.9)), (0.12, 3.96, 0.12), I3, "metal", C_DARK, 0.15)
    for k in range(9):                                                       # Faraday cage bars over the pad
        B.box(Vector((0, -0.6 + k * 0.45, 1.9)), (1.84, 0.03, 0.03), I3, "copper", C_COPPER, 0.1)
    B.cyl(Vector((0, 1.0, 1.9)), Vector((0, 1.0, 4.7)), 0.08, 10, "steel", C_STEEL)
    for z in (2.4, 3.1, 3.8):
        ring(B, Vector((0, 1.0, z)), (0, 0, 1), 0.08, 0.28, 0.06, 16, "panel", C_CERAMIC, 0.1)            # insulator discs
    B.cyl(Vector((0, 1.0, 4.7)), Vector((0, 1.0, 4.95)), 0.03, 8, "copper", C_COPPER)                      # rod tip
    for sx in (-1, 1):                                                       # charge lamps
        B.box(Vector((sx * 0.98, -0.95, 0.3)), (0.04, 0.04, 0.3), I3, "violet", C_VIOLET, 0.05)
    hazard(B, Vector((-0.95, -1.0, BELT_TOP - 0.12)), (1, 0, 0), (0, 0, 1), 1.9, 0.1, (0, -1, 0), pitch=0.1)
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
    B.box(Vector((0, 0, 0.85)), (1.96, 1.4, 0.3), I3, "metal", C_DARK, 0.15)
    B.box(Vector((1.05, 0.0, -0.25)), (0.14, 0.9, 0.3), I3, "metal", C_BLUE, 0.2)                         # copper plate feed port
    B.box(Vector((1.13, 0.0, -0.25)), (0.02, 0.8, 0.14), I3, "copper", C_COPPER, 0.1)
    for sx in (-1, 1):
        B.box(Vector((sx * 0.99, -0.72, 0.5)), (0.03, 0.03, 0.4), I3, "violet", C_VIOLET, 0.05)
    finish(B, "Frame", coll)
    Ra = Builder()
    Ra.box(Vector((0, 0, 0)), (1.3, 1.1, 0.18), I3, "steel", C_STEEL, 0.15)
    Ra.cyl(Vector((0, 0, 0.09)), Vector((0, 0, 0.8)), 0.12, 12, "steel", C_STEEL)
    ram = node(Ra, "Ram", coll, Vector((0, 0, 0.1)))
    keys(ram, (1, 60, 66, 75, 90, 121), "location", [Vector((0, 0, 0.1)), Vector((0, 0, 0.1)), Vector((0, 0, -0.45)),
                                                    Vector((0, 0, -0.45)), Vector((0, 0, 0.1)), Vector((0, 0, 0.1))])
    Co = Builder()
    for k in range(6):
        ring(Co, Vector((0, 0, k * 0.07)), (0, 0, 1), 0.2, 0.26, 0.04, 16, "copper", C_COPPER, 0.08)
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
    B.box(Vector((0, 0.95, BELT_TOP - 0.28)), (1.8, 0.1, 0.06), I3, "metal", C_DARK, 0.15)
    for k in range(5):                                                       # scraped-off tar smears
        B.box(Vector((-0.6 + k * 0.3, 0.97, BELT_TOP - 0.35 - 0.05 * (k % 2))), (0.12, 0.02, 0.1), I3, "rubber", C_TAR, 0.1)
    B.box(Vector((0.96, 0.6, -0.4)), (0.06, 0.3, 0.3), I3, "metal", C_BLUE, 0.2)
    finish(B, "Frame", coll)
    Bl = Builder()                                                           # sprung blade, twitching as blobs pass
    Bl.box(Vector((0, 0.08, -0.12)), (1.76, 0.02, 0.26), Matrix.Rotation(math.radians(-35), 3, 'X'), "steel", (0.55, 0.56, 0.6), 0.1)
    Bl.box(Vector((0, 0.16, -0.22)), (1.76, 0.03, 0.04), I3, "panel", C_YELLOW, 0.1)
    blade = node(Bl, "Blade", coll, Vector((0, 0.95, BELT_TOP - 0.02)))
    keys(blade, (1, 20, 24, 40, 60, 64, 81), "rotation_euler", [(0, 0, 0), (0, 0, 0), (0.25, 0, 0), (0, 0, 0), (0, 0, 0), (0.18, 0, 0), (0, 0, 0)])
    return coll

def build_coating_drum():
    random.seed(1441)
    coll = clear_collection("Chain_CoatingDrum")
    B = Builder()
    R, L_ = DRUM_R, DRUM_L
    ring_ = lambda x, r: [Vector((x, math.cos(k / 24 * math.tau) * r, math.sin(k / 24 * math.tau) * r)) for k in range(24)]
    B.tube_rings([ring_(-L_ / 2, R + 0.1), ring_(L_ / 2, R + 0.1)], "metal", (0.3, 0.26, 0.2), 0.2, 0.4, cap=False, smooth=True)
    B.tube_rings([ring_(L_ / 2, R), ring_(-L_ / 2, R)], "steel", (0.35, 0.33, 0.3), 0.2, 0.5, cap=False, smooth=True)   # inner face
    for x in (-L_ / 2 + 0.3, 0, L_ / 2 - 0.3):                              # riding tyres
        ring(B, Vector((x, 0, 0)), (1, 0, 0), R + 0.1, R + 0.22, 0.3, 32, "steel", C_STEEL, 0.1)
    for k in range(4):                                                       # lifter bars inside
        a = k / 4 * math.tau
        B.box(Vector((0, math.cos(a) * (R - 0.12), math.sin(a) * (R - 0.12))), (L_ - 0.2, 0.08, 0.26), Matrix.Rotation(a + math.pi / 2, 3, 'X'),
              "metal", C_DARK, 0.15)
    for k in range(6):                                                       # chalk dust streaks on the inner face
        a = k / 6 * math.tau + 0.3
        B.box(Vector((random.uniform(-2, 2), math.cos(a) * (R - 0.01), math.sin(a) * (R - 0.01))), (1.2, 0.3, 0.004),
              Matrix.Rotation(a + math.pi / 2, 3, 'X'), "panel", (0.85, 0.84, 0.8), 0.1)
    for s in (-1, 1):                                                        # mouth rings, hazard painted
        ring(B, Vector((s * L_ / 2, 0, 0)), (1, 0, 0), R - 0.05, R + 0.25, 0.12, 32, "panel", C_YELLOW, 0.1)
    finish(B, "Drum", coll)
    return coll

def build_drum_cradle():
    random.seed(1451)
    coll = clear_collection("Chain_DrumCradle")
    B = Builder()
    R, L_ = DRUM_R, DRUM_L
    for x in (-L_ / 2 + 0.3, L_ / 2 - 0.3):
        for s in (-1, 1):                                                    # trunnion rollers under each tyre
            c = Vector((x, s * 0.9, -R - 0.05))
            B.cyl(c + Vector((-0.2, 0, 0)), c + Vector((0.2, 0, 0)), 0.3, 16, "steel", C_STEEL)
            B.box(Vector((x, s * 0.9, -R - 0.6)), (0.5, 0.4, 0.9), I3, "metal", C_FRAME, 0.2, rust=0.3)
        B.box(Vector((x, 0, -R - 1.0)), (0.7, 2.6, 0.2), I3, "metal", C_DARK, 0.15)
    B.box(Vector((0, -1.6, -R - 0.6)), (1.0, 0.6, 0.8), I3, "metal", C_BLUE, 0.2, rust=0.15)               # drive motor
    B.pipe([Vector((0, -1.3, -R - 0.4)), Vector((0, -0.9, -R + 0.1))], 0.06, 8, "rubber", C_BLACK)
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
