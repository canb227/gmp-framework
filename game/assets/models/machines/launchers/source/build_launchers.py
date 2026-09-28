"""
Launchers (all 1 cell wide, front = +Y, origin at the anchor cell centre, floor z = -1):

  launch_ramp.glb  1x3x2 (x -1..1, y -1..5, z -1..3), basic conveyor style. A fast belt, flat for the first
                   RAMP.flat metres at belt height, then bending up to RAMP.th and throwing items off a kicker
                   lip at the front face. Nodes: Belt (run its shader fast), Frame, Flywheel (spins about X),
                   Beacon (spins about Z).
  cannon.glb       1x2x2 (x -1..1, y -1..3, z -1..3), advanced conveyor style. An intake belt at the back feeds
                   the breech; the barrel is elevated CANNON_ELEV. Nodes: Belt, Frame, Barrel (pivot on the
                   trunnions; slide it back along its local -Y for recoil) with a Muzzle marker at the bore exit,
                   Intake marker where items are taken in.
  catapult.glb     1x3x2 (x -1..1, y -1..5, z -1..3), basic conveyor style. The bucket rests at the back at belt
                   height, so items roll in from a conveyor behind it; the arm swings up and over to fling them
                   forward. Nodes: Frame, Arm (turns about X on the axle: rest CATAPULT_REST, thrown
                   CATAPULT_THROWN, in radians about +X), Winch (spins about X).
"""
import bpy, bmesh, math, random, os
from mathutils import Vector, Matrix

_HERE = (os.path.dirname(os.path.abspath(__file__)) if "__file__" in globals()
         else r"C:\Users\steph\OneDrive\Documents\godot\projects\gmp-framework\game\assets\models\machines\launchers\source")
_LIB = os.path.normpath(os.path.join(_HERE, "..", "..", "..", "shared", "salvage_lib.py"))
_S = {"__name__": "salvage_lib", "__file__": _LIB}
exec(compile(open(_LIB, encoding="utf-8").read(), _LIB, "exec"), _S)
_SKIP = {"HERE", "OUT_DIR", "PIECES", "build_all", "ICONS", "export", "CONV"}
globals().update({k: v for k, v in _S.items() if not k.startswith("__") and k not in _SKIP})
HERE = _HERE
OUT_DIR = os.path.dirname(HERE)
_ADV = os.path.normpath(os.path.join(_HERE, "..", "..", "..", "conveyors_advanced", "source", "build_conveyors_advanced.py"))

RAMP = dict(total_y=5.8, rise=3.0, R=1.5, th=math.radians(35.0))
CANNON_ELEV = math.radians(40.0)
CANNON_TRUNNION = Vector((0.0, 0.35, 0.1))
CATAPULT_AXLE = Vector((0.0, 2.0, 0.2))
CATAPULT_REST = math.radians(20.0)
CATAPULT_THROWN = math.radians(-100.0)

def adv_lib():
    """The advanced conveyor script's helpers (adv_side, conduit) for the cannon's intake."""
    ns = {"__name__": "adv_lib", "__file__": _ADV}
    exec(compile(open(_ADV, encoding="utf-8").read(), _ADV, "exec"), ns)
    return ns

def pitch_len(L):
    return max(RIB_PITCH, round(L / RIB_PITCH) * RIB_PITCH)

# ======================================================================================
def build_ramp():
    rng = random.Random(401); random.seed(401)
    coll = clear_collection("Launcher_Ramp")
    path, flat, ls = bend_up_path(**RAMP)
    build_belt(path, "Belt", coll, pitch_len(path.L))
    B = Builder()
    for side in (-1, 1):
        build_side(B, path, side, rng, panel_len=(0.6, 1.1))
    build_cable(B, path, clip_every=0.8)
    # scaffold legs under the incline, outside the stringers, with braces
    stations = [flat + RAMP["R"] * RAMP["th"] + ls * f for f in (0.2, 0.55, 0.9)]
    for side in (-1, 1):
        x = side * 0.972
        tops = []
        for s in stations:
            top = path.point(s, x, -0.13)
            B.cyl(Vector((x, top.y, FLOOR + 0.01)), top + Vector((0, 0, 0.03)), 0.028, 8, "steel", C_STEEL, rust=0.55)
            B.cyl(top - Vector((0, 0, 0.03)), top + Vector((0, 0, 0.05)), 0.04, 8, "steel", (0.22, 0.22, 0.22), rust=0.4)
            B.box(Vector((x - side * 0.01, top.y, FLOOR + 0.005)), (0.06, 0.14, 0.01), I3, "steel", C_STEEL, rust=0.7)
            tops.append(top)
        for a, b in zip(tops, tops[1:]):
            B.cyl(Vector((x, a.y, FLOOR + 0.3)), b - Vector((0, 0, 0.15)), 0.018, 6, "steel", C_STEEL, rust=0.6)
        B.cyl(Vector((x, tops[0].y, FLOOR + 0.3)), Vector((x, tops[-1].y, FLOOR + 0.3)), 0.018, 6, "steel", C_STEEL, rust=0.6)
    for s in stations:                                                              # cross ties under the belt
        c = path.point(s, 0, -0.2)
        B.cyl(Vector((-0.97, c.y, c.z)), Vector((0.97, c.y, c.z)), 0.02, 6, "steel", C_STEEL, rust=0.5)
    # speed chevrons on the outside of the stringers along the incline
    for side in (-1, 1):
        for k in range(6):
            s = flat + 0.3 + k * 0.75
            p = path.point(s, side * 0.955, -0.07); t = path.frame(s)[1]
            R = Matrix((t, Vector((0, 0, 1)).cross(t).normalized() * -side, Vector((side, 0, 0)))).transposed()
            B.box(p + Vector((side * 0.003, 0, 0)), (0.12, 0.05, 0.004), Matrix((t, path.frame(s)[3], Vector((side, 0, 0)))).transposed() @ Matrix.Rotation(0.6, 3, 'Z'), "panel", C_YELLOW, 0.2)
    # kicker lip at the front edge
    lip = path.point(path.L, 0, 0); t = path.frame(path.L)[1]; up = path.frame(path.L)[3]
    bas = frame_basis(path, path.L)
    B.box(lip - t * 0.04 + up * 0.03, (1.72, 0.06, 0.08), bas, "steel", C_WEAR, 0.15, rust=0.2)
    B.box(lip - t * 0.03 - up * 0.2, (1.72, 0.05, 0.36), bas, "metal", C_DARK, 0.2, rust=0.3)
    # drive: gearmotor on the floor under the incline, V-belt up to the head drum, flywheel alongside
    m = Vector((0.2, 3.3, -0.78))
    B.box(m, (0.5, 0.45, 0.4), I3, "metal", C_BLUE, 0.25, rust=0.15)
    B.box(m + Vector((0, 0, 0.24)), (0.3, 0.3, 0.08), I3, "panel", C_WHITE, 0.3)
    B.box(m + Vector((0.05, 0.1, 0.285)), (0.05, 0.05, 0.012), I3, "glow", C_AMBER, 0.05)
    B.box(Vector((0, 3.3, -0.985)), (1.2, 0.7, 0.03), I3, "steel", C_STEEL, 0.25, rust=0.7)
    hazard(B, Vector((-0.6, 2.95 - 0.001, -0.97)), (1, 0, 0), (0, 0, 1), 1.2, 0.03, (0, -1, 0), pitch=0.08)
    fw = Vector((-0.45, 3.3, -0.55))
    B.cyl(Vector((-0.2, 3.3, -0.55)), Vector((-0.05, 3.3, -0.55)), 0.06, 10, "steel", C_STEEL, rust=0.3)
    B.cyl(fw + Vector((0.1, 0, -0.44)), fw + Vector((0.1, 0, 0.0)), 0.05, 8, "metal", C_DARK, rust=0.3)     # bearing stand
    B.pipe([m + Vector((-0.26, 0.1, 0.1)), path.point(path.L * 0.72, -0.5, -0.2), path.point(path.L * 0.74, -0.2, -0.15)], 0.016, 6)
    finish(B, "Frame", coll)
    F = Builder()
    ring(F, Vector((0, 0, 0)), (1, 0, 0), 0.26, 0.34, 0.07, 24, "steel", (0.26, 0.26, 0.27), 0.2, rust=0.5)
    F.cyl(Vector((-0.04, 0, 0)), Vector((0.05, 0, 0)), 0.07, 12, "metal", C_DARK, rust=0.2)
    for k in range(5):
        a = k / 5 * math.tau
        F.box(Vector((0, math.cos(a), math.sin(a))) * 0.17, (0.03, 0.2, 0.05), Matrix.Rotation(a, 3, 'X'), "steel", (0.26, 0.26, 0.27), 0.2, rust=0.4)
    F.box(Vector((0.04, 0, 0.3)), (0.02, 0.08, 0.03), I3, "panel", C_YELLOW, 0.2)
    node(F, "Flywheel", coll, fw)
    # warning beacon on the right side of the lip
    top = path.point(path.L - 0.2, 0.93, GUARD_TOP)
    Bb = Builder()
    Bb.cyl(top, top + Vector((0, 0, 0.05)), 0.06, 12, "metal", C_DARK)
    Bb.cyl(top + Vector((0, 0, 0.05)), top + Vector((0, 0, 0.17)), 0.05, 12, "glow", C_AMBER, 0.05)
    B2 = Builder()
    B2.box(Vector((0, 0.02, 0)), (0.06, 0.01, 0.08), I3, "steel", (0.8, 0.8, 0.8), 0.05)
    # merge the beacon base into its own small node so Frame stays one object
    node(Bb, "BeaconBase", coll)
    node(B2, "Beacon", coll, top + Vector((0, 0, 0.11)))
    return coll

# ======================================================================================
def build_cannon():
    random.seed(411)
    A = adv_lib()
    adv_side, conduit = A["adv_side"], A["conduit"]
    coll = clear_collection("Launcher_Cannon")
    # intake belt across the back cell up to the breech mouth
    intake = sub_path(PATHS["straight"], 0.0, 1.1)
    build_belt(intake, "Belt", coll, 1.0)
    B = Builder()
    adv_side(B, intake, 1, panel_pitch=0.55)
    adv_side(B, intake, -1, panel_pitch=0.55)
    conduit(B, PATHS["straight"])
    # breech housing: facility panelled block, mouth open at the back at belt height
    hb = Vector((0, 0.95, -0.45))
    B.box(hb + Vector((0, 0.1, 0)), (1.9, 1.3, 1.1), I3, "metal", C_FRAME, 0.12)
    for sx_ in (-1, 1):
        for k, (y0, y1) in enumerate(((0.3, 0.95), (0.95, 1.6))):
            panel_face(B, Vector((sx_ * 0.965, (y0 + y1) / 2, -0.45)), (sx_, 0, 0), y1 - y0 - 0.04, 1.0, C_FACILITY)
    B.box(Vector((0, 0.34, -0.63)), (1.7, 0.02, 0.44), I3, "metal", (0.02, 0.02, 0.02), 0.05)              # dark intake mouth
    for sx_ in (-1, 1):
        B.box(Vector((sx_ * 0.9, 0.33, -0.55)), (0.08, 0.06, 0.62), I3, "metal", C_DARK, 0.1)
    B.box(Vector((0, 0.33, -0.22)), (1.88, 0.06, 0.08), I3, "metal", C_DARK, 0.1)
    B.box(Vector((0, 0.325, -0.2)), (1.6, 0.012, 0.02), I3, "lamp", C_LAMP, 0.02)
    hazard(B, Vector((-0.8, 0.319, -0.3)), (1, 0, 0), (0, 0, 1), 1.6, 0.05, (0, -1, 0), pitch=0.08)
    # trunnion towers either side of the barrel, capacitor bank and conduit
    for sx_ in (-1, 1):
        x = sx_ * 0.93
        B.box(Vector((x, CANNON_TRUNNION.y, (0.1 + CANNON_TRUNNION.z) / 2)), (0.1, 0.5, CANNON_TRUNNION.z + 0.3), I3, "metal", C_DARK, 0.1)
        B.cyl(Vector((sx_ * 0.87, CANNON_TRUNNION.y, CANNON_TRUNNION.z)), Vector((sx_ * 0.99, CANNON_TRUNNION.y, CANNON_TRUNNION.z)), 0.12, 16, "steel", C_STEEL, 0.1)
        ring(B, Vector((sx_ * 0.99, CANNON_TRUNNION.y, CANNON_TRUNNION.z)), (1, 0, 0), 0.05, 0.09, 0.01, 16, "cyan", C_CYAN, 0.05)
    for k in range(3):                                                                           # capacitor cans
        c = Vector((0.55 - k * 0.3, 2.55, -0.62))
        B.cyl(c - Vector((0, 0, 0.37)), c + Vector((0, 0, 0.25)), 0.12, 14, "panel", C_FACILITY, 0.1)
        B.cyl(c + Vector((0, 0, 0.25)), c + Vector((0, 0, 0.29)), 0.1, 14, "metal", C_DARK, 0.1)
        ring(B, c + Vector((0, 0, 0.1)), (0, 0, 1), 0.12, 0.125, 0.03, 14, "cyan", C_CYAN, 0.05)
    B.box(Vector((0, 2.55, -0.97)), (1.2, 0.5, 0.06), I3, "metal", C_DARK, 0.1)
    for k in range(3):
        c = Vector((0.55 - k * 0.3, 2.55, -0.33))
        B.pipe([c, c + Vector((0, -0.2, 0.25)), Vector((c.x * 0.3, 1.6, 0.05))], 0.018, 6, "metal", C_DARK)
    finish(B, "Frame", coll)
    # barrel (local +Y along the bore, origin on the trunnion axis)
    Br = Builder()
    y0, y1, r_in, r_out = -0.35, 2.6, 0.76, 0.86
    def rings_at(y, r):
        return [Vector((math.cos(a) * r, y, math.sin(a) * r)) for a in [k / 32 * math.tau for k in range(32)]]
    Br.tube_rings([rings_at(y0, r_out), rings_at(y1, r_out)], "metal", C_FRAME, 0.12, 0.0, cap=False, smooth=True)
    Br.tube_rings([rings_at(y1, r_in), rings_at(y0, r_in)], "metal", (0.05, 0.05, 0.055), 0.1, 0.0, cap=False, smooth=True)
    ring(Br, Vector((0, y1, 0)), (0, 1, 0), r_in, r_out, 0.01, 32, "metal", C_DARK, 0.1)
    for k in range(3):                                                                           # shroud panels
        yy = 0.05 + k * 0.62
        for j in range(8):
            a = (j + 0.5) / 8 * math.tau
            nrm = Vector((math.cos(a), 0, math.sin(a))); tan = Vector((-math.sin(a), 0, math.cos(a)))
            R = Matrix((tan, Vector((0, 1, 0)), nrm)).transposed()
            Br.box(nrm * (r_out + 0.015) + Vector((0, yy + 0.26, 0)), (0.62, 0.04, 0.035), R, "metal", C_DARK, 0.1)
        for j in range(8):
            a = (j + 0.5) / 8 * math.tau
            nrm = Vector((math.cos(a), 0, math.sin(a))); tan = Vector((-math.sin(a), 0, math.cos(a)))
            R = Matrix((tan, Vector((0, 1, 0)), nrm)).transposed()
            Br.box(nrm * (r_out + 0.015) + Vector((0, yy, 0)), (0.62, 0.52, 0.03), R, "panel", C_FACILITY, 0.1)
    for yy in (0.02, 0.64, 1.26, 1.88):                                                          # accelerator coils
        ring(Br, Vector((0, yy + 0.3, 0)), (0, 1, 0), r_out + 0.02, r_out + 0.06, 0.06, 32, "metal", C_DARK, 0.1)
        ring(Br, Vector((0, yy + 0.3, 0)), (0, 1, 0), r_in - 0.01, r_in, 0.04, 32, "cyan", C_CYAN, 0.05)
    ring(Br, Vector((0, y1 - 0.12, 0)), (0, 1, 0), r_out, r_out + 0.08, 0.24, 32, "metal", C_DARK, 0.1)    # muzzle brake
    for j in range(6):
        a = j / 6 * math.tau
        Br.box(Vector((math.cos(a) * (r_out + 0.085), y1 - 0.12, math.sin(a) * (r_out + 0.085))), (0.05, 0.14, 0.02),
               Matrix.Rotation(-a + math.pi / 2, 3, 'Y'), "metal", (0.02, 0.02, 0.02), 0.05)
    ring(Br, Vector((0, y1 - 0.02, 0)), (0, 1, 0), r_in, r_in + 0.03, 0.02, 32, "cyan", C_CYAN, 0.05)
    Br.cyl(Vector((0, y0 - 0.02, 0)), Vector((0, y0, 0)), r_out, 32, "metal", C_DARK, 0.1)               # breech plug
    for sx_ in (-1, 1):
        Br.cyl(Vector((sx_ * r_out, 0, 0)), Vector((sx_ * 0.87, 0, 0)), 0.1, 12, "metal", C_DARK, 0.1)   # trunnion pins
    barrel = node(Br, "Barrel", coll, CANNON_TRUNNION)
    barrel.rotation_euler = (CANNON_ELEV, 0, 0)
    marker("Muzzle", coll, Vector((0, y1 + 0.1, 0)), parent=barrel)
    marker("Intake", coll, Vector((0, 0.1, BELT_TOP + 0.4)))
    return coll

# ======================================================================================
def build_catapult():
    rng = random.Random(421); random.seed(421)
    coll = clear_collection("Launcher_Catapult")
    B = Builder()
    P = CATAPULT_AXLE
    # base skids and A-frames either side of the arm
    for sx_ in (-1, 1):
        x = sx_ * 0.82
        B.box(Vector((x, P.y, FLOOR + 0.05)), (0.14, 2.8, 0.1), I3, "steel", C_STEEL, 0.25, rust=0.7)
        for (fy, fz) in ((P.y - 1.25, FLOOR + 0.1), (P.y + 1.25, FLOOR + 0.1)):
            B.cyl(Vector((x, fy, fz)), Vector((x, P.y, P.z + 0.05)), 0.045, 10, "steel", C_STEEL, 0.2, rust=0.55)
        B.cyl(Vector((x, P.y - 0.7, -0.5)), Vector((x, P.y + 0.7, -0.5)), 0.03, 8, "steel", C_STEEL, rust=0.6)
        B.cyl(Vector((x, P.y, P.z - 0.12)), Vector((x, P.y, P.z + 0.12)), 0.11, 12, "metal", C_DARK, rust=0.3)  # bearing block
        B.box(Vector((x, P.y, P.z + 0.18)), (0.16, 0.26, 0.06), I3, "tape", C_TAPE, 0.25)
    B.cyl(Vector((-0.95, P.y, P.z)), Vector((0.95, P.y, P.z)), 0.05, 12, "steel", C_STEEL, rust=0.3)          # axle
    # stop beam the arm slams into: padded cross bar up front
    stop = Vector((0.0, P.y + 0.45, 1.8))
    for sx_ in (-1, 1):
        B.cyl(Vector((sx_ * 0.82, P.y + 1.25, FLOOR + 0.1)), Vector((sx_ * 0.82, stop.y, stop.z)), 0.035, 8, "steel", C_STEEL, rust=0.55)
    B.cyl(Vector((-0.86, stop.y, stop.z)), Vector((0.86, stop.y, stop.z)), 0.05, 10, "steel", C_STEEL, rust=0.5)
    B.box(stop + Vector((0, -0.06, 0)), (0.9, 0.12, 0.16), I3, "rubber", C_BLACK, 0.1)
    for k in range(3):
        B.box(stop + Vector((-0.3 + k * 0.3, -0.07, 0)), (0.12, 0.13, 0.17), I3, "tape", C_TAPE, 0.2)
    hazard(B, Vector((-0.45, stop.y - 0.125, stop.z - 0.08)), (1, 0, 0), (0, 0, 1), 0.9, 0.05, (0, -1, 0), pitch=0.08)
    # winch housing and release trigger box on the right frame
    wb = Vector((0.82, P.y - 0.85, -0.55))
    B.box(wb, (0.16, 0.4, 0.34), I3, "metal", C_BLUE, 0.25, rust=0.2)
    B.box(wb + Vector((0.085, 0, 0.1)), (0.01, 0.2, 0.08), I3, "panel", C_WHITE, 0.3)
    B.box(wb + Vector((0.09, 0.05, 0.1)), (0.004, 0.04, 0.03), I3, "glow", C_AMBER, 0.05)
    B.box(Vector((0.0, -0.3, FLOOR + 0.02)), (1.6, 1.2, 0.04), I3, "steel", C_STEEL, 0.25, rust=0.7)          # rest plate
    hazard(B, Vector((-0.8, -0.901, FLOOR + 0.041)), (1, 0, 0), (0, 1, 0), 1.6, 0.08, (0, 0, 1), pitch=0.1)
    finish(B, "Frame", coll)
    # the arm: built pointing back (-Y) horizontally from the axle; the node's X rotation poses it
    A = Builder()
    A.box(Vector((0, -1.15, 0)), (0.16, 2.6, 0.14), I3, "steel", (0.3, 0.3, 0.3), 0.25, rust=0.6)
    A.box(Vector((0, -1.15, 0.075)), (0.1, 2.3, 0.01), I3, "steel", C_STEEL, 0.25, rust=0.4)
    for k in range(5):
        y = -0.2 - k * 0.5
        for sx_ in (-1, 1):
            A.cyl(Vector((sx_ * 0.08, y, 0.04)), Vector((sx_ * 0.09, y, 0.04)), 0.012, 6, "steel", C_STEEL, rust=0.4)
    A.box(Vector((0, 0.45, 0)), (0.16, 0.9, 0.14), I3, "steel", (0.3, 0.3, 0.3), 0.25, rust=0.6)            # short end
    A.cyl(Vector((-0.12, 0, 0)), Vector((0.12, 0, 0)), 0.1, 12, "metal", C_DARK, rust=0.3)                   # hub
    # bucket: a salvaged facility crate cut open, low lip at the back so items roll in
    bc = Vector((0, -2.45, 0.05))
    A.box(bc + Vector((0, 0, -0.12)), (1.3, 1.1, 0.06), I3, "steel", C_STEEL, 0.25, rust=0.6)                 # floor
    for sx_ in (-1, 1):
        A.box(bc + Vector((sx_ * 0.63, 0, 0.06)), (0.04, 1.1, 0.34), I3, "panel", C_WHITE, 0.3)
        hazard(A, bc + Vector((sx_ * 0.651, -0.55 * sx_, 0.12)), (0, sx_, 0), (0, 0, 1), 1.1, 0.08, (sx_, 0, 0), pitch=0.08)
    A.box(bc + Vector((0, 0.53, 0.1)), (1.3, 0.04, 0.42), I3, "panel", C_WHITE, 0.3)                          # arm-side wall
    A.box(bc + Vector((0, -0.53, -0.06)), (1.3, 0.04, 0.1), I3, "steel", C_WEAR, 0.15, rust=0.2)              # low back lip
    for (x, y) in ((-0.63, -0.53), (0.63, -0.53), (-0.63, 0.53), (0.63, 0.53)):
        A.box(bc + Vector((x, y, 0.03)), (0.07, 0.07, 0.36), I3, "metal", C_FRAME, 0.2, rust=0.3)
    A.box(bc + Vector((0, 0.1, -0.155)), (0.5, 0.6, 0.01), I3, "rubber", C_BLACK, 0.1)
    # counterweight: junk crate strapped with tape, on the short end
    cw = Vector((0, 0.9, -0.1))
    A.box(cw, (0.6, 0.5, 0.5), I3, "metal", C_DARK, 0.25, rust=0.6)
    for k in range(2):
        A.box(cw + Vector((0, -0.12 + k * 0.24, 0)), (0.62, 0.06, 0.52), I3, "tape", C_TAPE, 0.25)
    for k in range(4):
        rock(A, cw + Vector((-0.18 + k * 0.12, 0.05 * (k % 2), 0.28)), 0.08, 30 + k, 1, 0.3, "rock",
             lambda q, n: [c * random.uniform(0.8, 1.2) for c in (0.3, 0.25, 0.22)])
    arm = node(A, "Arm", coll, P)
    arm.rotation_euler = (CATAPULT_REST, 0, 0)
    marker("Release", coll, bc + Vector((0, 0, 0.3)), parent=arm)
    # winch drum (cable reels the arm down to cock it)
    W = Builder()
    W.cyl(Vector((-0.07, 0, 0)), Vector((0.07, 0, 0)), 0.13, 16, "steel", C_STEEL, rust=0.4)
    for sx_ in (-1, 1):
        W.cyl(Vector((sx_ * 0.07, 0, 0)), Vector((sx_ * 0.085, 0, 0)), 0.17, 16, "metal", C_DARK, rust=0.3)
    ring(W, Vector((0, 0, 0)), (1, 0, 0), 0.13, 0.145, 0.12, 16, "rubber", C_BLACK, 0.1)
    W.box(Vector((0.09, 0, 0.15)), (0.01, 0.05, 0.03), I3, "panel", C_YELLOW, 0.2)
    node(W, "Winch", coll, wb + Vector((-0.1, 0, 0.25)))
    return coll

PIECES = [
    (build_ramp, "launch_ramp.glb"),
    (build_cannon, "cannon.glb"),
    (build_catapult, "catapult.glb"),
]
ICONS = [
    ("Launcher_Ramp", "blueprints/blueprint_launch_ramp.png", "blueprint", (1.6, 0.6, 0.7)),
    ("Launcher_Cannon", "blueprints/blueprint_cannon.png", "blueprint", (1.5, 0.6, 0.6)),
    ("Launcher_Catapult", "blueprints/blueprint_catapult.png", "blueprint", (1.6, 0.6, 0.7)),
]

def build_all(do_export=True):
    return build_family(PIECES, OUT_DIR, do_export)

if __name__ == "__main__":
    build_all(do_export=False)
