"""
Scrap grabber (front = +Y, origin at the anchor cell centre, floor z = -1). Same 1x1x2 footprint and joint layout
as filter_arm.glb, but bodged together from salvage: an oil drum bolted to a pallet, a car wheel hub for a
turntable, a cordless drill driving the shoulder, a scaffold-pipe upper arm, an I-beam forearm and a security
camera taped to the wrist.

  scrap_grabber.glb   "Frame" (static base mesh) + the "Skeleton" armature, which drives one rigidly skinned
                      mesh, "Arm". Bind pose = arm straight up with every joint at zero rotation:
      Turret      yaw about local Y (world up)                   pivot (0, 0, 0)
      Shoulder    pitch about local X (+ leans back toward -Y)   pivot (0, 0, SHOULDER_Z)
      Elbow       pitch about local X                             UPPER_LEN further along
      Wrist       pitch about local X (carries the camera)        FORE_LEN further along
      WristRoll   roll about local Y (along the head)
      ClawL/ClawR swing about local Z (ClawL +, ClawR - opens)
      Grip        fixed: the point between the claw pads, attach held items here
  Clips (reference motion to replace in Godot; Godot strips "-loop" and imports it as a looping "idle"):
  idle-loop (folded, scanning), reach (folded -> down onto the
  front neighbour's belt, claws open), grab (claws close), drop (lift, swing round to the back, lower, release).

Run in Blender: exec(open(r"...\\build_scrap_grabber.py").read(), {"__name__": "__main__"}) builds without
exporting; build_all(True) also writes the GLB and the blueprint icon.
"""
import bpy, bmesh, math, random, os
from mathutils import Vector, Matrix

_HERE = (os.path.dirname(os.path.abspath(__file__)) if "__file__" in globals()
         else r"C:\Users\steph\OneDrive\Documents\godot\projects\gmp-framework\game\assets\models\machines\processing\source")
_LIB = os.path.normpath(os.path.join(_HERE, "..", "..", "..", "shared", "salvage_lib.py"))
_S = {"__name__": "salvage_lib", "__file__": _LIB}
exec(compile(open(_LIB, encoding="utf-8").read(), _LIB, "exec"), _S)
_SKIP = {"HERE", "OUT_DIR", "PIECES", "build_all", "ICONS", "export", "CONV"}
globals().update({k: v for k, v in _S.items() if not k.startswith("__") and k not in _SKIP})
HERE = _HERE
OUT_DIR = os.path.dirname(HERE)
_ICON_LIB = os.path.normpath(os.path.join(_HERE, "..", "..", "..", "props", "source", "render_icons.py"))

SHARP_BELOW = 0.035
FOLD = {"tape": "rubber", "wood": "rubber"}

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

# ---------- salvage palette ----------
C_DRUM = (0.12, 0.2, 0.3)          # faded blue oil drum
C_PRIMER = (0.33, 0.11, 0.07)      # red-oxide I-beam
C_GALV = (0.48, 0.49, 0.47)        # galvanised scaffold pipe
C_OLIVE = (0.22, 0.25, 0.15)
C_CONCRETE = (0.42, 0.41, 0.38)
C_SIGN_RED = (0.55, 0.05, 0.04)
C_STRAP = (0.8, 0.3, 0.05)         # ratchet strap
C_DRILL = (0.8, 0.55, 0.05)

# ---------- small helpers ----------
def stud(B, p, n, r=0.011, h=0.01, c=C_STEEL, rust=0.3):
    """Hex bolt head standing on a surface along n."""
    n = Vector(n).normalized()
    B.cyl(p, p + n * h, r, 6, "steel", c, 0.15, rust)

def bar(B, p0, p1, w, d, mat="steel", c=C_STEEL, rust=0.4, var=0.2, bevel=None):
    """Box from p0 to p1 (w across local x, d across local y)."""
    p0, p1 = Vector(p0), Vector(p1)
    B.box((p0 + p1) / 2, (w, d, (p1 - p0).length), rot_to(p1 - p0), mat, c, var, rust, bevel)

def band(B, center, axis, r, width=0.03, mat="tape", c=C_TAPE, thick=0.006):
    """Strap / tape / hose clamp wrapped round something of radius r."""
    ring(B, Vector(center), axis, r, r + thick, width, 24 if r > 0.2 else 14, mat, c, 0.25)

def weld(B, pts, r=0.009):
    B.pipe([Vector(p) for p in pts], r, 5, "steel", (0.2, 0.19, 0.18), 0.3)

# ======================================================================================
SHOULDER_Z = 0.28
UPPER_LEN, FORE_LEN = 1.2, 1.0
ELBOW_Z = SHOULDER_Z + UPPER_LEN
WRIST_Z = ELBOW_Z + FORE_LEN
ROLL_Z = WRIST_Z + 0.16
CLAW_X, CLAW_Z = 0.13, ROLL_Z + 0.14
GRIP_Z = ROLL_Z + 0.33
BONES = [  # name, head, tail, parent
    ("Turret", (0, 0, 0), (0, 0, SHOULDER_Z), None),
    ("Shoulder", (0, 0, SHOULDER_Z), (0, 0, ELBOW_Z), "Turret"),
    ("Elbow", (0, 0, ELBOW_Z), (0, 0, WRIST_Z), "Shoulder"),
    ("Wrist", (0, 0, WRIST_Z), (0, 0, ROLL_Z), "Elbow"),
    ("WristRoll", (0, 0, ROLL_Z), (0, 0, CLAW_Z), "Wrist"),
    ("ClawL", (-CLAW_X, 0, CLAW_Z), (-CLAW_X, 0, CLAW_Z + 0.22), "WristRoll"),
    ("ClawR", (CLAW_X, 0, CLAW_Z), (CLAW_X, 0, CLAW_Z + 0.22), "WristRoll"),
    ("Grip", (0, 0, GRIP_Z), (0, 0, GRIP_Z + 0.1), "WristRoll"),
]

def build_frame(coll):
    """Static base: pallet, oil drum, braces, battery, slewing ring."""
    rng = random.Random(907)
    B = Builder()
    for x in (-0.68, 0.02, 0.7):                                                   # pallet stringers
        B.box(Vector((x, 0, -0.955)), (0.1, 1.72, 0.09), I3, "wood", C_WOOD, 0.3)
    for k in range(7):                                                              # deck boards, one missing
        if k == 5:
            continue
        y = -0.78 + k * 0.26
        L = 1.72 - rng.uniform(0, 0.14)
        col = tuple(c * rng.uniform(0.7, 1.1) for c in C_WOOD)
        B.box(Vector((rng.uniform(-0.05, 0.05), y, -0.897)), (L, 0.17, 0.025), Matrix.Rotation(rng.uniform(-0.04, 0.04), 3, 'Z'),
              "wood", col, 0.35)
    B.box(Vector((0.03, 0.02, -0.875)), (0.92, 0.88, 0.02), Matrix.Rotation(0.12, 3, 'Z'), "steel", C_STEEL, 0.25, rust=0.8)
    for a in (0.5, 1.9, 3.3, 4.9):                                                   # plate bolts through the deck
        stud(B, Vector((math.cos(a) * 0.52, math.sin(a) * 0.5, -0.865)), (0, 0, 1), 0.02, 0.02, rust=0.6)
    # oil drum pedestal
    B.cyl(Vector((0, 0, -0.865)), Vector((0, 0, -0.07)), 0.36, 24, "metal", C_DRUM, 0.3, rust=0.55)
    for z in (-0.62, -0.32):
        ring(B, Vector((0, 0, z)), (0, 0, 1), 0.355, 0.375, 0.03, 24, "metal", C_DRUM, 0.2, rust=0.7)
    ring(B, Vector((0, 0, -0.08)), (0, 0, 1), 0.33, 0.372, 0.03, 24, "metal", C_DRUM, 0.2, rust=0.7)
    # patched hole: a yellow panel welded on crooked, plus a drum-wrapping tape band
    R = facing_basis((0.8, 0.6, 0)) @ Matrix.Rotation(0.15, 3, 'Z')
    ctr = Vector((0.8, 0.6, 0)).normalized() * 0.365 + Vector((0, 0, -0.47))
    B.box(ctr, (0.26, 0.2, 0.012), R, "panel", C_YELLOW, 0.35, rust=0.5)
    for dx, dy in ((-0.11, -0.08), (0.11, -0.08), (-0.11, 0.08), (0.11, 0.08)):
        stud(B, ctr + R @ Vector((dx, dy, 0.006)), R @ Vector((0, 0, 1)), 0.008, 0.006)
    band(B, (0, 0, -0.2), (0, 0, 1), 0.362, 0.05, thick=0.008)
    # mismatched braces from the pallet to the drum
    bar(B, (0.62, 0.62, -0.88), (0.24, 0.24, -0.4), 0.05, 0.05, "steel", C_STEEL, rust=0.7)             # angle iron
    bar(B, (0.64, 0.64, -0.88), (0.26, 0.26, -0.4), 0.012, 0.06, "steel", C_STEEL, rust=0.7)
    B.cyl(Vector((-0.62, 0.64, -0.88)), Vector((-0.25, 0.25, -0.35)), 0.018, 6, "steel", C_STEEL, rust=0.4)   # threaded rod
    bar(B, (-0.64, -0.6, -0.88), (-0.26, -0.24, -0.45), 0.09, 0.03, "wood", C_WOOD)                   # plank strut
    bar(B, (0.6, -0.64, -0.88), (0.23, -0.27, -0.3), 0.04, 0.04, "metal", C_YELLOW, rust=0.6)         # old railing
    for p in ((0.24, 0.24, -0.4), (0.25, 0.25, -0.35), (0.23, -0.27, -0.3)):
        weld(B, [Vector(p) + Vector((0, 0, -0.03)), Vector(p) + Vector((0.02, 0.02, 0.03))])
    # car battery and its cables up the drum
    bat = Vector((0.58, -0.35, -0.78))
    B.box(bat, (0.3, 0.18, 0.2), Matrix.Rotation(0.3, 3, 'Z'), "metal", (0.05, 0.05, 0.05), 0.15)
    B.box(bat + Vector((0, 0, 0.101)), (0.28, 0.16, 0.004), Matrix.Rotation(0.3, 3, 'Z'), "panel", C_WHITE, 0.2)
    for dx, col in ((-0.09, (0.6, 0.05, 0.03)), (0.09, C_BLACK)):
        t = bat + Matrix.Rotation(0.3, 3, 'Z') @ Vector((dx, 0, 0.12))
        B.cyl(t - Vector((0, 0, 0.02)), t + Vector((0, 0, 0.01)), 0.014, 8, "steel", C_STEEL)
        B.pipe([t + Vector((0, 0, 0.01)), t + Vector((-0.1, 0.05, 0.12)), Vector((0.34 + dx * 0.3, -0.12, -0.5)),
                Vector((0.35 + dx * 0.3, -0.1, -0.12)), Vector((0.2 + dx * 0.3, -0.06, -0.075))], 0.011, 5, "rubber", col)
    B.box(Vector((0.36, -0.12, -0.28)), (0.1, 0.05, 0.13), Matrix.Rotation(-0.3, 3, 'Z'), "metal", C_DARK, 0.2)  # junction box
    B.box(Vector((0.37, -0.15, -0.25)), (0.025, 0.004, 0.02), Matrix.Rotation(-0.3, 3, 'Z'), "glow", C_AMBER, 0.05)
    # slewing ring the wheel hub turns on
    ring(B, Vector((0, 0, -0.035)), (0, 0, 1), 0.2, 0.4, 0.07, 24, "metal", C_DARK, 0.2, rust=0.4)
    for k in range(8):
        a = (k + 0.3) / 8 * math.tau
        stud(B, Vector((math.cos(a) * 0.37, math.sin(a) * 0.37, 0.0)), (0, 0, 1), 0.012, 0.008, rust=0.5)
    finish(B, "Frame", coll)

# ---- skinned parts: each builder is one bone's rigid chunk, built in the bind (straight-up) pose ----
def part_turret():
    B = Builder()
    B.cyl(Vector((0, 0, 0.0)), Vector((0, 0, 0.07)), 0.42, 24, "steel", (0.22, 0.22, 0.22), 0.25, rust=0.5)   # wheel hub
    B.cyl(Vector((0, 0, 0.07)), Vector((0, 0, 0.09)), 0.3, 24, "steel", C_STEEL, 0.2, rust=0.6)             # brake disc
    B.cyl(Vector((0, 0, 0.09)), Vector((0, 0, 0.13)), 0.1, 12, "metal", C_DARK, 0.2)
    for k in range(5):
        a = k / 5 * math.tau
        stud(B, Vector((math.cos(a) * 0.16, math.sin(a) * 0.16, 0.09)), (0, 0, 1), 0.018, 0.03)
    # yoke: two plates that never matched
    B.box(Vector((-0.2, 0, 0.26)), (0.05, 0.34, 0.42), I3, "metal", C_YELLOW, 0.3, rust=0.55)
    B.box(Vector((0.2, 0.01, 0.27)), (0.035, 0.26, 0.44), Matrix.Rotation(0.05, 3, 'Y'), "steel", (0.3, 0.3, 0.3), 0.3, rust=0.5)
    for x in (-0.2, 0.2):
        weld(B, [Vector((x - 0.03, -0.14, 0.1)), Vector((x - 0.03, 0.14, 0.1))])
        weld(B, [Vector((x + 0.03, -0.14, 0.1)), Vector((x + 0.03, 0.14, 0.1))])
    B.cyl(Vector((-0.25, 0, SHOULDER_Z)), Vector((0.25, 0, SHOULDER_Z)), 0.035, 10, "steel", C_STEEL, rust=0.3)  # axle
    B.cyl(Vector((-0.27, 0, SHOULDER_Z)), Vector((-0.23, 0, SHOULDER_Z)), 0.06, 6, "steel", C_STEEL, rust=0.3)
    # cordless drill direct-driving the shoulder axle, taped and clamped to the yoke
    B.cyl(Vector((0.22, 0, SHOULDER_Z)), Vector((0.46, 0, SHOULDER_Z)), 0.055, 12, "metal", C_DRILL, 0.25, rust=0.1)
    B.cyl(Vector((0.46, 0, SHOULDER_Z)), Vector((0.5, 0, SHOULDER_Z)), 0.045, 12, "metal", C_BLACK, 0.1)
    B.box(Vector((0.4, 0, SHOULDER_Z - 0.12)), (0.06, 0.07, 0.2), Matrix.Rotation(0.2, 3, 'Y'), "metal", C_BLACK, 0.15)
    B.box(Vector((0.42, 0, 0.1)), (0.11, 0.1, 0.07), I3, "metal", C_DRILL, 0.25)
    for x in (0.28, 0.4):
        band(B, (x, 0, SHOULDER_Z), (1, 0, 0), 0.056, 0.025, "steel", C_STEEL, 0.004)
    B.box(Vector((0.28, 0, 0.17)), (0.03, 0.05, 0.18), I3, "tape", C_TAPE, 0.3)                           # tape strap down to hub
    B.pipe([Vector((0.42, 0.05, 0.1)), Vector((0.36, 0.2, 0.1)), Vector((0.1, 0.3, 0.1)), Vector((-0.1, 0.25, 0.09))], 0.01, 5)
    # counterweight: a cinder block under a ratchet strap
    cb = Vector((0, -0.28, 0.2))
    B.box(cb, (0.38, 0.18, 0.19), I3, "panel", C_CONCRETE, 0.35, rust=0.1)
    for x in (-0.09, 0.09):
        B.box(cb + Vector((x, 0, 0.096)), (0.12, 0.1, 0.004), I3, "metal", (0.12, 0.12, 0.11), 0.1)
    B.box(cb + Vector((0.1, 0, 0)), (0.04, 0.19, 0.2), I3, "tape", C_STRAP, 0.2)
    B.box(cb + Vector((0.1, -0.1, 0)), (0.05, 0.02, 0.06), I3, "steel", C_STEEL, 0.2, rust=0.5)             # ratchet
    B.box(Vector((0, -0.2, 0.11)), (0.3, 0.04, 0.04), I3, "steel", C_STEEL, 0.25, rust=0.7)                  # bracket
    # warning beacon on a bent rod
    B.pipe([Vector((-0.2, -0.16, 0.45)), Vector((-0.22, -0.2, 0.55)), Vector((-0.28, -0.26, 0.6))], 0.008, 5, "steel", C_STEEL)
    B.cyl(Vector((-0.28, -0.26, 0.6)), Vector((-0.28, -0.26, 0.66)), 0.03, 10, "glow", C_AMBER, 0.05)
    return B

def part_upper():
    B = Builder()
    z0, z1 = SHOULDER_Z, ELBOW_Z
    B.cyl(Vector((-0.165, 0, z0)), Vector((0.165, 0, z0)), 0.085, 14, "metal", C_DARK, 0.2, rust=0.3)       # shoulder hub
    for x in (-0.07, 0.07):                                                                     # scaffold pipes
        B.cyl(Vector((x, 0, z0 + 0.05)), Vector((x, 0, z1 - 0.06)), 0.034, 10, "steel", C_GALV, 0.25, rust=0.35)
    for z in (z0 + 0.3, z1 - 0.35):                                                              # couplers
        B.box(Vector((0, 0, z)), (0.22, 0.085, 0.07), I3, "steel", (0.26, 0.26, 0.25), 0.2, rust=0.5)
        for x in (-0.07, 0.07):
            stud(B, Vector((x, 0.043, z)), (0, 1, 0), 0.012, 0.015)
    pts = []                                                                                     # rebar zigzag
    for k in range(9):
        pts.append(Vector((0.06 if k % 2 else -0.06, 0.0, z0 + 0.12 + k * (z1 - z0 - 0.25) / 8)))
    B.pipe(pts, 0.009, 5, "steel", C_RUST)
    # wooden splint lashed on with tape
    B.box(Vector((0, -0.055, (z0 + z1) / 2)), (0.11, 0.025, 0.72), Matrix.Rotation(0.03, 3, 'Y'), "wood", C_WOOD, 0.35)
    for z in (z0 + 0.42, z1 - 0.22):
        B.box(Vector((0, -0.01, z)), (0.2, 0.11, 0.05), I3, "tape", C_TAPE, 0.3)
    # cable up the front with zip ties
    B.pipe([Vector((0.03, 0.06, z0 + 0.1)), Vector((0.05, 0.075, z0 + 0.5)), Vector((0.02, 0.07, z1 - 0.3)), Vector((0.03, 0.06, z1 - 0.1))],
           0.011, 5, "rubber", C_BLACK)
    for z in (z0 + 0.25, z0 + 0.65, z1 - 0.2):
        B.box(Vector((0.035, 0.07, z)), (0.05, 0.03, 0.008), I3, "rubber", (0.9, 0.9, 0.88), 0.1)
    # elbow fork: two cut plates welded to the pipes
    for x, col in ((-0.12, (0.3, 0.3, 0.3)), (0.12, C_YELLOW)):
        B.box(Vector((x, 0, z1 - 0.06)), (0.028, 0.17, 0.26), I3, "steel", col, 0.3, rust=0.6)
        weld(B, [Vector((x * 0.6, -0.03, z1 - 0.17)), Vector((x * 0.6, 0.03, z1 - 0.17))])
    return B

def part_fore():
    B = Builder()
    z0, z1 = ELBOW_Z, WRIST_Z
    B.cyl(Vector((-0.1, 0, z0)), Vector((0.1, 0, z0)), 0.068, 12, "metal", C_DARK, 0.2, rust=0.3)
    # bike chainring bolted to the hub
    B.cyl(Vector((0.083, 0, z0)), Vector((0.092, 0, z0)), 0.105, 20, "steel", C_STEEL, 0.2, rust=0.4)
    for k in range(20):
        a = k / 20 * math.tau
        B.box(Vector((0.0875, math.cos(a) * 0.113, z0 + math.sin(a) * 0.113)), (0.009, 0.012, 0.014),
              Matrix.Rotation(a, 3, 'X'), "steel", C_STEEL, 0.2, rust=0.4)
    # I-beam, red-oxide primer, rusting through
    zb0, zb1 = z0 + 0.06, z1 - 0.1
    L, zm = zb1 - zb0, (zb0 + zb1) / 2
    for y in (-0.05, 0.05):
        B.box(Vector((0, y, zm)), (0.12, 0.012, L), I3, "steel", C_PRIMER, 0.3, rust=0.65)
    B.box(Vector((0, 0, zm)), (0.012, 0.1, L), I3, "steel", C_PRIMER, 0.3, rust=0.65)
    for x in (-0.05, 0.05):                                                                       # gussets to the hub
        bar(B, (x, 0, z0 + 0.04), (x * 0.6, 0, zb0 + 0.14), 0.012, 0.1, "steel", C_STEEL, rust=0.6)
    # a road sign offcut riveted across the front flange
    sc = Vector((0.0, 0.058, zm + 0.05))
    B.box(sc, (0.17, 0.006, 0.32), Matrix.Rotation(0.06, 3, 'Y'), "panel", C_WHITE, 0.3, rust=0.3)
    B.box(sc + Vector((0.01, 0.004, 0.1)), (0.13, 0.003, 0.04), Matrix.Rotation(0.06, 3, 'Y'), "panel", C_SIGN_RED, 0.2)
    for dz in (-0.13, 0.13):
        for dx in (-0.06, 0.06):
            stud(B, sc + Vector((dx, 0.003, dz)), (0, 1, 0), 0.007, 0.005)
    # drain hose along the side with zip ties
    B.pipe([Vector((-0.07, -0.02, z0 + 0.1)), Vector((-0.085, -0.03, zm)), Vector((-0.07, -0.02, z1 - 0.12))], 0.016, 6, "rubber", (0.08, 0.1, 0.08))
    for z in (zm - 0.25, zm + 0.2):
        B.box(Vector((-0.075, -0.02, z)), (0.05, 0.05, 0.008), I3, "rubber", (0.9, 0.9, 0.88), 0.1)
    # wrist fork
    for x in (-0.1, 0.1):
        B.box(Vector((x, 0, z1 - 0.07)), (0.024, 0.13, 0.2), I3, "steel", (0.3, 0.3, 0.3), 0.3, rust=0.5)
    B.box(Vector((0, 0, z1 - 0.16)), (0.22, 0.13, 0.03), I3, "steel", (0.3, 0.3, 0.3), 0.3, rust=0.5)
    return B

def part_wrist():
    B = Builder()
    z0 = WRIST_Z
    B.cyl(Vector((-0.085, 0, z0)), Vector((0.085, 0, z0)), 0.052, 12, "metal", C_DARK, 0.2)
    B.box(Vector((0, 0, z0 + 0.09)), (0.15, 0.13, 0.12), I3, "metal", C_OLIVE, 0.25, rust=0.3)             # gearbox
    B.cyl(Vector((0.075, 0.02, z0 + 0.09)), Vector((0.1, 0.02, z0 + 0.09)), 0.025, 8, "steel", C_STEEL)
    # security camera taped to the side, looking down the claws
    cam = Vector((0, -0.125, z0 + 0.1))
    B.box(cam, (0.08, 0.075, 0.15), Matrix.Rotation(-0.08, 3, 'X'), "panel", C_WHITE, 0.3, rust=0.15)
    tip = cam + Vector((0, -0.006, 0.075))
    B.cyl(tip, tip + Vector((0, -0.003, 0.025)), 0.03, 12, "metal", C_DARK, 0.1)
    B.cyl(tip + Vector((0, -0.003, 0.025)), tip + Vector((0, -0.003, 0.029)), 0.019, 12, "cyan", C_CYAN, 0.05)
    B.box(cam + Vector((0.028, -0.039, 0.05)), (0.012, 0.004, 0.01), I3, "glow", (1.0, 0.15, 0.1), 0.05)
    for z in (-0.03, 0.035):
        B.box(cam + Vector((0, 0.035, z)), (0.1, 0.23, 0.035), I3, "tape", C_TAPE, 0.3)
    B.pipe([cam + Vector((0, 0.02, -0.075)), cam + Vector((0.02, 0.05, -0.12)), Vector((0.05, -0.04, z0 - 0.02))], 0.006, 5)
    return B

def part_roll():
    B = Builder()
    z0 = ROLL_Z
    B.cyl(Vector((0, 0, z0)), Vector((0, 0, z0 + 0.03)), 0.1, 18, "steel", C_STEEL, 0.2, rust=0.5)             # brake disc flange
    for k in range(6):
        a = k / 6 * math.tau
        stud(B, Vector((math.cos(a) * 0.075, math.sin(a) * 0.075, z0 + 0.03)), (0, 0, 1), 0.009, 0.008)
    B.box(Vector((0, 0, z0 + 0.065)), (0.36, 0.075, 0.04), I3, "steel", C_GALV, 0.25, rust=0.4)               # claw mount strap
    B.box(Vector((0.02, 0.0, z0 + 0.05)), (0.06, 0.08, 0.035), Matrix.Rotation(0.4, 3, 'Z'), "steel", C_STEEL, 0.2, rust=0.6)
    for x in (-CLAW_X, CLAW_X):                                                                     # pivot lugs
        B.box(Vector((x, 0, CLAW_Z - 0.035)), (0.05, 0.08, 0.06), I3, "steel", (0.28, 0.28, 0.27), 0.2, rust=0.5)
    return B

def part_claw(sx):
    """ClawL (sx = -1): two bent rebar rods; ClawR (sx = +1): bent flat bar. Both have tyre-rubber pads."""
    B = Builder()
    P = Vector((sx * CLAW_X, 0, CLAW_Z))
    B.cyl(P + Vector((0, -0.05, 0)), P + Vector((0, 0.05, 0)), 0.02, 8, "steel", C_STEEL, rust=0.3)
    knee = P + Vector((sx * 0.03, 0, 0.14))
    tip = P + Vector((-sx * 0.09, 0, 0.25))
    if sx < 0:
        for dy in (-0.025, 0.025):
            o = Vector((0, dy, 0))
            B.pipe([P + o, knee + o, tip + o], 0.011, 6, "steel", C_RUST)
    else:
        bar(B, P, knee, 0.02, 0.07, "steel", (0.3, 0.3, 0.3), rust=0.6)
        bar(B, knee, tip, 0.02, 0.07, "steel", (0.3, 0.3, 0.3), rust=0.6)
    d = (tip - knee).normalized()
    pc = knee + (tip - knee) * 0.55 + Vector((0, 0, 1)).cross(Vector((0, 1, 0))) * sx * 0.018   # on the inner (-sx) face
    pc.z -= 0.012
    B.box(pc, (0.03, 0.065, 0.1), rot_to(d), "rubber", C_BLACK, 0.1)
    return B

PARTS = [("Turret", part_turret), ("Shoulder", part_upper), ("Elbow", part_fore), ("Wrist", part_wrist),
         ("WristRoll", part_roll), ("ClawL", lambda: part_claw(-1)), ("ClawR", lambda: part_claw(1))]

def build_rig(coll):
    arm_data = bpy.data.armatures.new("Skeleton")
    rig = bpy.data.objects.new("Skeleton", arm_data)
    coll.objects.link(rig)
    rig.show_in_front = True
    arm_data.display_type = 'STICK'
    view = bpy.context.view_layer
    view.objects.active = rig
    bpy.ops.object.mode_set(mode='EDIT')
    for name, h, t, parent in BONES:
        eb = arm_data.edit_bones.new(name)
        eb.head, eb.tail, eb.roll = Vector(h), Vector(t), 0.0
        if parent:
            eb.parent = arm_data.edit_bones[parent]
        eb.use_deform = name != "Grip"
    bpy.ops.object.mode_set(mode='OBJECT')
    for pb in rig.pose.bones:
        pb.rotation_mode = 'XYZ'
    # skinned mesh: every part rigidly weighted to its bone
    obs = []
    for bone, fn in PARTS:
        ob = fn().to_object("Arm_" + bone, coll)
        vg = ob.vertex_groups.new(name=bone)
        vg.add(list(range(len(ob.data.vertices))), 1.0, 'REPLACE')
        ob.modifiers.clear()
        obs.append(ob)
    with bpy.context.temp_override(active_object=obs[0], object=obs[0], selected_objects=obs, selected_editable_objects=obs):
        bpy.ops.object.join()
    arm = obs[0]
    arm.name = arm.data.name = "Arm"
    trim_materials(arm.data)
    weighted_normals(arm)
    arm.parent = rig
    mod = arm.modifiers.new("Armature", 'ARMATURE')
    mod.object = rig
    return rig, arm

def trim_materials(me):
    used = sorted({p.material_index for p in me.polygons})
    remap = {old: new for new, old in enumerate(used)}
    mats = [me.materials[i] for i in used]
    idx = [remap[p.material_index] for p in me.polygons]
    me.materials.clear()
    for m in mats:
        me.materials.append(m)
    me.polygons.foreach_set("material_index", idx)
    me.update()

# ---------- reference clips ----------
def pose(yaw=0, sh=0, el=0, wr=0, roll=0, claw=10):
    """Euler degrees per bone: yaw about Turret Y, pitches about X, roll about WristRoll Y, claw opening."""
    return {"Turret": (0, yaw, 0), "Shoulder": (sh, 0, 0), "Elbow": (el, 0, 0), "Wrist": (wr, 0, 0),
            "WristRoll": (0, roll, 0), "ClawL": (0, 0, claw), "ClawR": (0, 0, -claw), "Grip": (0, 0, 0)}

FOLDED = dict(sh=20, el=-110, wr=-90)             # upper arm leaning back, forearm level, head pointing down
REACH = dict(sh=-78.6, el=-45.9, wr=-55.5)        # grip on an item on the front neighbour's belt (y 2, z -0.55)
OPEN, SHUT = 28, 3

CLIPS = {
    "idle-loop": [(1, pose(**FOLDED)), (31, pose(yaw=12, roll=10, claw=14, **{**FOLDED, "wr": -82})),
                  (61, pose(**FOLDED, claw=6)), (91, pose(yaw=-10, roll=-8, claw=14, **{**FOLDED, "wr": -97})),
                  (121, pose(**FOLDED))],
    "reach": [(1, pose(**FOLDED)), (18, pose(sh=-35, el=-85, wr=-60, claw=20)), (36, pose(**REACH, claw=OPEN))],
    "grab": [(1, pose(**REACH, claw=OPEN)), (15, pose(**REACH, claw=SHUT))],
    "drop": [(1, pose(**REACH, claw=SHUT)), (18, pose(**FOLDED, claw=SHUT)), (45, pose(yaw=180, **FOLDED, claw=SHUT)),
             (63, pose(yaw=180, **REACH, claw=SHUT)), (75, pose(yaw=180, **REACH, claw=OPEN))],
}

def apply_pose(rig, p):
    for name, e in p.items():
        rig.pose.bones[name].rotation_euler = [math.radians(a) for a in e]

def build_clips(rig):
    ad = rig.animation_data_create()
    for t in list(ad.nla_tracks):
        ad.nla_tracks.remove(t)
    for name, keyed in CLIPS.items():
        old = bpy.data.actions.get(name)
        if old:
            bpy.data.actions.remove(old)
        act = bpy.data.actions.new(name)
        act.use_fake_user = True
        ad.action = act
        for fr, p in keyed:
            apply_pose(rig, p)
            for bone in p:
                rig.keyframe_insert(f'pose.bones["{bone}"].rotation_euler', frame=fr, group=bone)
        slot = ad.action_slot if hasattr(ad, "action_slot") else None
        ad.action = None
        track = ad.nla_tracks.new()
        track.name = name
        strip = track.strips.new(name, 1, act)
        if slot is not None and hasattr(strip, "action_slot"):
            strip.action_slot = slot
    apply_pose(rig, pose())

def export(coll, rig, path):
    sc = bpy.context.scene
    sc.render.fps = ANIM_FPS
    bpy.ops.object.select_all(action='DESELECT')
    for ob in coll.objects:
        ob.select_set(True)
    for t in rig.animation_data.nla_tracks:
        t.mute = False
    bpy.ops.export_scene.gltf(filepath=path, export_format='GLB', use_selection=True, export_yup=True, export_apply=True,
                              export_vertex_color='ACTIVE', export_all_vertex_colors=False, export_normals=True,
                              export_texcoords=True, export_materials='EXPORT', export_extras=False, export_cameras=False,
                              export_lights=False, export_skins=True, export_def_bones=False, export_rest_position_armature=True,
                              export_animations=True, export_animation_mode='NLA_TRACKS', export_force_sampling=True,
                              export_anim_slide_to_zero=True)

def show_folded(rig):
    """Leave the .blend (and the icon) showing the folded pose rather than the bind pose."""
    for t in rig.animation_data.nla_tracks:
        t.mute = True
    apply_pose(rig, pose(**FOLDED))
    bpy.context.view_layer.update()

ICONS = [("Scrap_Grabber", "blueprints/blueprint_scrap_grabber.png", "blueprint", (1.3, 1.0, 0.5))]

def render_icon():
    ns = {"__name__": "icon_lib"}
    exec(compile(open(_ICON_LIB, encoding="utf-8").read(), _ICON_LIB, "exec"), ns)
    ns["ICONS"] = ICONS
    return ns["render_icons"]()

def build_all(do_export=True):
    salvage_mats()
    random.seed(913)
    old = bpy.data.collections.get("Scrap_Grabber")
    for ob in list(old.objects) if old else []:            # clear_collection only knows mesh data
        if ob.type == 'ARMATURE':
            data = ob.data
            bpy.data.objects.remove(ob, do_unlink=True)
            bpy.data.armatures.remove(data)
    coll = clear_collection("Scrap_Grabber")
    build_frame(coll)
    rig, arm = build_rig(coll)
    build_clips(rig)
    if do_export:
        export(coll, rig, os.path.join(OUT_DIR, "scrap_grabber.glb"))
    show_folded(rig)
    if do_export:
        render_icon()
    return rig

if __name__ == "__main__":
    build_all(do_export=False)
