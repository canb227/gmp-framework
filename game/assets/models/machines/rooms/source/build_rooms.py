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

def seg_box(B, a, b, z0, z1, t, mat, c, var=0.15, rust=0.0):
    """Box from a to b (2D, Blender XY) between heights z0..z1, thickness t."""
    d = b - a
    ang = math.atan2(d.y, d.x)
    B.box(Vector(((a.x + b.x) / 2, (a.y + b.y) / 2, (z0 + z1) / 2)), (d.length, t, z1 - z0), Matrix.Rotation(ang, 3, 'Z'), mat, c, var, rust=rust)

def build_turntable():
    random.seed(1301)
    coll = clear_collection("Room_Turntable")
    B = Builder()
    B.cyl(Vector((0, 0, 0)), Vector((0, 0, 0.4)), TT_R, 64, "steel", (0.42, 0.43, 0.46), 0.1, rust=0.2)
    for k in range(16):                                                      # painted spokes
        a = k / 16 * math.tau
        B.box(Vector((math.cos(a) * 5, math.sin(a) * 5, 0.402)), (7.6, 0.18, 0.004), Matrix.Rotation(a, 3, 'Z'),
              "panel", C_YELLOW if k % 2 else C_BLACK, 0.1)
    for r in (3.0, 6.0):
        ring(B, Vector((0, 0, 0.402)), (0, 0, 1), r - 0.06, r + 0.06, 0.004, 64, "panel", C_WHITE, 0.1)
    B.cyl(Vector((0, 0, 0.402)), Vector((0, 0, 0.406)), 1.2, 32, "panel", C_DARK, 0.1)
    ring(B, Vector((0, 0, 0.2)), (0, 0, 1), TT_R - 0.02, TT_R + 0.01, 0.38, 64, "cyan", C_CYAN, 0.05)       # rim glow band
    finish(B, "Disc", coll)
    return coll

def build_sweep_arm():
    random.seed(1311)
    coll = clear_collection("Room_SweepArm")
    B = Builder()
    B.box(Vector((0, 0, 1.0)), (0.8, 0.8, 4.0), I3, "metal", C_FRAME, 0.2, rust=0.3)                       # pylon
    panel_face(B, Vector((0, 0.401, 0.6)), (0, 1, 0), 0.6, 2.4, C_WHITE)
    B.box(Vector((0, 0, 3.05)), (1.0, 1.0, 0.1), I3, "metal", C_DARK, 0.15)
    hazard(B, Vector((-0.4, 0.402, -0.95)), (1, 0, 0), (0, 0, 1), 0.8, 0.12, (0, 1, 0), pitch=0.1)
    top = 2.8
    seg_box(B, Vector((0, 0)), ARM_A, top - 0.12, top + 0.12, 0.24, "metal", C_BLUE, rust=0.15)          # boom
    seg_box(B, ARM_A, ARM_B, top - 0.12, top + 0.12, 0.24, "metal", C_BLUE, rust=0.15)
    for t in (0.0, 0.33, 0.66, 1.0):                                         # hangers
        p = ARM_A.lerp(ARM_B, t)
        B.box(Vector((p.x, p.y, (0.6 + top) / 2)), (0.08, 0.08, top - 0.6), I3, "steel", C_STEEL, 0.1)
    seg_box(B, ARM_A, ARM_B, 0.2, 0.6, 0.1, "panel", C_WHITE)               # blade
    seg_box(B, ARM_A, ARM_B, 0.04, 0.2, 0.12, "rubber", C_BLACK)             # squeegee edge
    seg_box(B, ARM_A, ARM_B, 0.45, 0.52, 0.11, "panel", C_YELLOW)
    B.cyl(Vector((ARM_B.x, ARM_B.y, top + 0.12)), Vector((ARM_B.x, ARM_B.y, top + 0.2)), 0.12, 12, "glow", C_AMBER, 0.05)
    finish(B, "Frame", coll)
    return coll

def build_slot_sieve():
    random.seed(1321)
    coll = clear_collection("Room_SlotSieve")
    B = Builder()
    B.box(Vector((4.95, 1.0, -0.6)), (0.1, 4.0, 0.8), I3, "metal", C_FRAME, 0.2, rust=0.3)                # closed side
    B.box(Vector((-0.95, 1.0, -0.3)), (0.1, 4.0, 0.2), I3, "metal", C_FRAME, 0.2, rust=0.3)               # open side: rail only
    for y in (-0.9, 2.9):
        B.box(Vector((-0.95, y, -0.65)), (0.1, 0.2, 0.7), I3, "metal", C_FRAME, 0.2, rust=0.3)
    for x in SIEVE_BARS:
        B.box(Vector((x, 1.0, -0.25)), (0.08, 4.0, 0.1), I3, "steel", (0.55, 0.56, 0.6), 0.1, rust=0.3)
    for y in (0.0, 2.0):                                                     # cross ties under the bars
        B.box(Vector((2.0, y, -0.34)), (5.8, 0.06, 0.06), I3, "metal", C_DARK, 0.15)
    B.box(Vector((2.0, 1.0, -0.975)), (6.0, 4.0, 0.05), I3, "steel", L["C_WEAR"], 0.15)                    # hopper floor
    for y in (-0.975, 2.975):
        B.box(Vector((2.0, y, -0.65)), (6.0, 0.05, 0.7), I3, "metal", C_FRAME, 0.2, rust=0.3)
    panel_face(B, Vector((4.99, 1.0, -0.6)), (1, 0, 0), 3.6, 0.6, C_WHITE)
    for k in range(5):                                                       # drag-chain slats on the hopper floor
        B.box(Vector((2.0, 0.2 + k * 0.7, -0.94)), (5.8, 0.05, 0.03), I3, "metal", C_DARK, 0.1)
    hazard(B, Vector((-1.0, -1.0, -0.2)), (1, 0, 0), (0, 1, 0), 6.0, 0.12, (0, 0, 1), pitch=0.15)
    finish(B, "Frame", coll)
    return coll

def build_terrace_catcher():
    random.seed(1331)
    coll = clear_collection("Room_TerraceCatcher")
    B = Builder()
    B.box(Vector((2.0, 0, -0.95)), (6.0, 2.0, 0.1), I3, "steel", L["C_WEAR"], 0.15, rust=0.4)            # floor
    for k in range(8):
        B.box(Vector((-0.6 + k * 0.75, 0, -0.895)), (0.05, 1.8, 0.02), I3, "metal", C_DARK, 0.1)          # drag slats
    B.box(Vector((2.0, -0.95, -0.3)), (6.0, 0.1, 1.2), I3, "metal", C_FRAME, 0.2, rust=0.3)                # back wall
    B.box(Vector((2.0, -0.88, -0.2)), (5.8, 0.05, 0.8), I3, "rubber", C_BLACK, 0.1)                        # impact pad
    B.box(Vector((2.0, 0.95, -0.75)), (6.0, 0.1, 0.3), I3, "metal", C_FRAME, 0.2, rust=0.3)                # low front lip
    hazard(B, Vector((-1.0, 1.001, -0.85)), (1, 0, 0), (0, 0, 1), 6.0, 0.2, (0, 1, 0), pitch=0.12)
    B.box(Vector((-0.95, 0, -0.55)), (0.1, 2.0, 0.8), I3, "metal", C_FRAME, 0.2, rust=0.3)                 # closed end
    panel_face(B, Vector((2.0, -1.001, -0.3)), (0, -1, 0), 5.6, 1.0, C_WHITE)
    for x in (4.6, 5.0):                                                     # exit marker at the open end
        B.box(Vector((x, 0.95, -0.4)), (0.05, 0.12, 0.4), I3, "glow", C_AMBER, 0.05)
    finish(B, "Frame", coll)
    return coll

def build_balance_floor():
    random.seed(1341)
    coll = clear_collection("Room_BalanceFloor")
    B = Builder()
    B.box(Vector((0, 0, 0)), (20.0, 8.0, 0.4), I3, "metal", C_FRAME, 0.2, rust=0.3)
    for k in range(20):                                                      # deck plates
        B.box(Vector((-9.5 + k, 0, 0.202)), (0.96, 7.8, 0.004), I3, "steel", (0.45, 0.46, 0.5) if k % 2 else (0.4, 0.41, 0.45), 0.1)
    for y in (-3.9, 3.9):                                                    # side rails
        B.box(Vector((0, y, 0.35)), (20.0, 0.2, 0.3), I3, "panel", C_WHITE, 0.3)
        hazard(B, Vector((-10, y + (0.101 if y > 0 else -0.101), 0.21)), (1, 0, 0), (0, 0, 1), 20.0, 0.28, (0, 1 if y > 0 else -1, 0), pitch=0.3)
    for x in (-9.9, 9.9):                                                    # open ends, marked
        B.box(Vector((x, 0, 0.205)), (0.2, 7.6, 0.01), I3, "panel", C_YELLOW, 0.1)
    B.box(Vector((0, 0, 0.203)), (0.1, 7.6, 0.004), I3, "panel", C_BLACK, 0.1)                             # centre line
    B.cyl(Vector((0, -4.0, -0.35)), Vector((0, 4.0, -0.35)), 0.25, 20, "steel", C_STEEL)                   # hinge barrel
    for y in (-3.6, 0, 3.6):
        B.box(Vector((0, y, -0.25)), (0.7, 0.3, 0.3), I3, "metal", C_DARK, 0.15)
    finish(B, "Deck", coll)
    return coll

def build_counterweight_sled():
    random.seed(1351)
    coll = clear_collection("Room_CounterweightSled")
    B = Builder()
    for y in (-1.2, 1.2):
        B.box(Vector((0, y, -0.95)), (3.2, 0.12, 0.1), I3, "steel", C_STEEL, 0.1, rust=0.4)               # rails
    B.box(Vector((0, 0, -0.5)), (2.0, 2.6, 0.8), I3, "metal", (0.18, 0.19, 0.2), 0.2, rust=0.4)           # the weight
    for k in range(4):
        B.box(Vector((-0.75 + k * 0.5, 0, -0.08)), (0.4, 2.4, 0.04), I3, "metal", C_DARK, 0.1)             # stacked slabs
    hazard(B, Vector((-1.0, -1.301, -0.85)), (1, 0, 0), (0, 0, 1), 2.0, 0.2, (0, -1, 0), pitch=0.12)
    for x in (-0.8, 0.8):
        for y in (-1.2, 1.2):
            B.cyl(Vector((x, y - 0.08, -0.82)), Vector((x, y + 0.08, -0.82)), 0.12, 12, "steel", C_STEEL)   # wheels
    B.box(Vector((1.02, 0, -0.5)), (0.04, 0.8, 0.3), I3, "metal", C_BLUE, 0.15)
    B.box(Vector((1.045, 0, -0.5)), (0.004, 0.5, 0.08), I3, "glow", C_AMBER, 0.05)
    finish(B, "Frame", coll)
    return coll

def build_tilt_gauge():
    random.seed(1361)
    coll = clear_collection("Room_TiltGauge")
    B = Builder()
    B.box(Vector((0, 0, 0.5)), (0.25, 0.25, 3.0), I3, "metal", C_FRAME, 0.2, rust=0.3)
    c = Vector((0, -0.2, 2.2))
    B.cyl(c, c + Vector((0, -0.08, 0)), 0.8, 32, "metal", C_DARK)
    B.cyl(c + Vector((0, -0.08, 0)), c + Vector((0, -0.09, 0)), 0.72, 32, "panel", (0.85, 0.84, 0.8), 0.1)
    for k in range(-5, 6):                                                   # scale: green at level, red at the stops
        a = math.radians(k * 9)
        col = (0.2, 0.8, 0.3) if abs(k) <= 1 else (C_YELLOW if abs(k) <= 3 else (0.85, 0.15, 0.1))
        p = c + Vector((math.sin(a) * 0.62, -0.095, -math.cos(a) * 0.62))
        B.box(p, (0.03, 0.004, 0.1 if k % 5 else 0.16), Matrix.Rotation(-a, 3, 'Y'), "panel", col, 0.05)
    B.box(c + Vector((0, -0.1, -0.3)), (0.03, 0.004, 0.62), I3, "glow", (1.0, 0.15, 0.1), 0.05)            # pendulum needle
    B.cyl(c + Vector((0, -0.09, 0)), c + Vector((0, -0.12, 0)), 0.06, 12, "steel", C_STEEL)
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
