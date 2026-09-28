"""
Inline processing machines. Each carries a basic salvage conveyor through it: items enter at the back face
(y = -1) of the anchor column at belt height and leave through the front face. Origin at the anchor cell
centre, floor z = -1, cells grow toward +Y (and +X for the polisher).

  plate_press.glb    1x2x1 (x -1..1, y -1..3, z -1..1). Press frame over the cell boundary (y = 1).
                     Nodes: Belt, Frame, Ram (the platen; lower it along -Z by RAM_STROKE to stamp).
  rod_extruder.glb   1x2x1. Housing over y -0.2..2.3 with a heated barrel; rods leave through the die plate.
                     Nodes: Belt, Frame, RollerA / RollerB (pull rollers, spin about X), Screw (feed screw
                     coupling on the gearbox, spins about Y).
  polisher.glb       2x2x1 (x -1..3, y -1..3, z -1..1). Polishing tunnel over the anchor column's belt, drive
                     and dust extraction in the +X column. Nodes: Belt, Frame, BrushA / BrushB (spin about X),
                     Buffer (side buffing wheel, spins about Z), Fan (extractor, spins about Z).
Each also has In / Out markers at the belt entry and exit.
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

RAM_STROKE = 0.5
RAM_REST_Z = -0.15           # platen underside at rest
LONG = Path(4.0, straight_fn, 1)   # belt over two cells along +Y

def io_markers(coll):
    marker("In", coll, Vector((0, -1.0, BELT_TOP + 0.3)))
    marker("Out", coll, Vector((0, 3.0, BELT_TOP + 0.3)))

def guarded_belt(coll, B, rng, open_spans):
    """4 m belt; guards everywhere except the (s0, s1) spans covered by the machine."""
    build_belt(LONG, "Belt", coll, 4.0)
    cuts = [0.0]
    for a, b in open_spans:
        cuts += [a, b]
    cuts.append(4.0)
    for i in range(0, len(cuts), 2):
        a, b = cuts[i], cuts[i + 1]
        if b - a > 0.05:
            for side in (-1, 1):
                build_side(B, sub_path(LONG, a, b), side, rng, panel_len=(0.45, 0.8))
    for a, b in open_spans:
        for side in (-1, 1):
            build_side(B, sub_path(LONG, a, b), side, rng, guards=False, hubs=False)
    build_cable(B, LONG, clip_every=0.8)

# ======================================================================================
def build_press():
    rng = random.Random(801); random.seed(801)
    coll = clear_collection("Machine_PlatePress")
    B = Builder()
    guarded_belt(coll, B, rng, [(1.3, 2.7)])
    # columns either side of the belt at the cell boundary, with facility cladding
    for sx_ in (-1, 1):
        x = sx_ * 0.93
        B.box(Vector((x, 1.0, 0.0)), (0.12, 1.2, 1.96), I3, "metal", C_FRAME, 0.2, rust=0.35)
        panel_face(B, Vector((sx_ * 0.992, 1.0, -0.1)), (sx_, 0, 0), 1.0, 1.4, C_WHITE)
        hazard(B, Vector((sx_ * 0.999, 1.6 if sx_ > 0 else 0.4, -0.95)), (0, -sx_, 0), (0, 0, 1), 1.2, 0.12, (sx_, 0, 0), pitch=0.1)
        for y in (0.45, 1.55):
            B.box(Vector((x, y, 0.0)), (0.13, 0.1, 1.98), I3, "metal", C_DARK, 0.2, rust=0.3)
        # bed rails under the platen, beside the belt
        B.box(Vector((sx_ * 0.87, 1.0, BELT_TOP - 0.05)), (0.05, 1.2, 0.2), I3, "steel", C_WEAR, 0.15, rust=0.3)
    # crown with the hydraulic cylinder
    B.box(Vector((0, 1.0, 0.72)), (1.98, 1.25, 0.46), I3, "metal", C_FRAME, 0.2, rust=0.3)
    for n in (Vector((0, 1, 0)), Vector((0, -1, 0))):
        panel_face(B, Vector((0, 1.0, 0.72)) + n * 0.63, n, 1.7, 0.36, C_WHITE)
    B.cyl(Vector((0, 1.0, 0.2)), Vector((0, 1.0, 0.95)), 0.26, 20, "steel", (0.3, 0.3, 0.3), 0.2, rust=0.4)
    ring(B, Vector((0, 1.0, 0.3)), (0, 0, 1), 0.26, 0.3, 0.06, 20, "metal", C_DARK, 0.2)
    # hydraulic pump and hoses on the crown, gauges and warning lamp
    pm = Vector((-0.5, 1.2, 0.97))
    B.box(pm, (0.5, 0.4, 0.06), I3, "metal", C_BLUE, 0.25, rust=0.2)
    for k, x in enumerate((0.45, 0.7)):
        gp = Vector((x, 0.36, 0.72))
        B.cyl(gp, gp - Vector((0, 0.01, 0)), 0.09, 14, "metal", C_DARK, 0.15)
        B.cyl(gp - Vector((0, 0.01, 0)), gp - Vector((0, 0.014, 0)), 0.075, 14, "panel", (0.85, 0.84, 0.8), 0.1)
        B.box(gp - Vector((0, 0.018, 0)) + Vector((0.02, 0, 0.02)), (0.06, 0.003, 0.008), Matrix.Rotation(0.6 + k, 3, 'Y'), "glow", (1.0, 0.15, 0.1), 0.05)
    for dx in (-0.1, 0.1):
        B.pipe([pm + Vector((0.25, dx, 0)), Vector((0.2, 1.0 + dx, 0.99)), Vector((0.3, 1.0 + dx, 0.85)), Vector((0.26, 1.0 + dx, 0.7))], 0.02, 6)
    B.cyl(Vector((0.75, 1.5, 0.95)), Vector((0.75, 1.5, 0.99)), 0.06, 12, "metal", C_DARK)
    B.cyl(Vector((0.75, 1.5, 0.99)), Vector((0.75, 1.5, 1.08)), 0.05, 12, "glow", C_AMBER, 0.05)
    # light curtain at the entry
    for sx_ in (-1, 1):
        B.box(Vector((sx_ * 0.87, 0.35, BELT_TOP + 0.3)), (0.04, 0.04, 0.6), I3, "metal", C_DARK, 0.1)
        B.box(Vector((sx_ * 0.848, 0.35, BELT_TOP + 0.3)), (0.004, 0.02, 0.56), I3, "cyan", C_CYAN, 0.05)
    finish(B, "Frame", coll)
    # the ram: rod, platen and die (origin at the platen underside)
    R_ = Builder()
    R_.cyl(Vector((0, 0, 0.2)), Vector((0, 0, 0.75)), 0.12, 16, "steel", C_WEAR, 0.1, rust=0.05)
    R_.box(Vector((0, 0, 0.12)), (1.6, 1.0, 0.16), I3, "metal", C_DARK, 0.2, rust=0.3)
    R_.box(Vector((0, 0, 0.025)), (1.3, 0.8, 0.05), I3, "steel", C_STEEL, 0.15, rust=0.2)
    for sy in (-1, 1):
        hazard(R_, Vector((-0.8, sy * 0.501, 0.05)), (1, 0, 0), (0, 0, 1), 1.6, 0.14, (0, sy, 0), pitch=0.1)
    for sx_ in (-1, 1):
        R_.box(Vector((sx_ * 0.82, 0, 0.12)), (0.06, 0.3, 0.2), I3, "steel", C_WEAR, 0.1)                   # guide shoes
    node(R_, "Ram", coll, Vector((0, 1.0, RAM_REST_Z)))
    io_markers(coll)
    return coll

# ======================================================================================
def build_extruder():
    rng = random.Random(811); random.seed(811)
    coll = clear_collection("Machine_RodExtruder")
    B = Builder()
    y0, y1 = -0.2, 2.3
    guarded_belt(coll, B, rng, [(y0 + 1, y1 + 1)])
    # housing tunnel over the belt
    for sx_ in (-1, 1):
        B.box(Vector((sx_ * 0.94, (y0 + y1) / 2, -0.35)), (0.1, y1 - y0, 1.3), I3, "metal", C_FRAME, 0.2, rust=0.3)
        for k in range(3):
            yy = y0 + (y1 - y0) * (k + 0.5) / 3
            panel_face(B, Vector((sx_ * 0.992, yy, -0.35)), (sx_, 0, 0), (y1 - y0) / 3 - 0.05, 1.2, C_WHITE if (k + sx_) % 3 else C_DARK)
    B.box(Vector((0, (y0 + y1) / 2, 0.35)), (1.98, y1 - y0, 0.12), I3, "metal", C_FRAME, 0.2, rust=0.3)
    # entry mouth
    B.box(Vector((0, y0 + 0.03, -0.1)), (1.8, 0.06, 0.5), I3, "metal", C_DARK, 0.2)
    for sx_ in (-1, 1):
        B.box(Vector((sx_ * 0.86, y0, -0.5)), (0.08, 0.08, 0.72), I3, "steel", C_AMBER, 0.2, rust=0.2)
    B.box(Vector((0, y0, -0.14)), (1.8, 0.08, 0.08), I3, "steel", C_AMBER, 0.2, rust=0.2)
    hazard(B, Vector((-0.9, y0 - 0.001, 0.02)), (1, 0, 0), (0, 0, 1), 1.8, 0.1, (0, -1, 0), pitch=0.1)
    # heated barrel on top, along Y, with heater bands
    B.cyl(Vector((0, y0 + 0.15, 0.68)), Vector((0, y1 - 0.25, 0.68)), 0.27, 24, "steel", (0.3, 0.3, 0.3), 0.2, rust=0.4)
    for k in range(4):
        yy = y0 + 0.45 + k * 0.5
        ring(B, Vector((0, yy, 0.68)), (0, 1, 0), 0.27, 0.31, 0.16, 24, "metal", C_DARK, 0.15, rust=0.2)
        ring(B, Vector((0, yy, 0.68)), (0, 1, 0), 0.31, 0.315, 0.1, 24, "molten", C_MOLTEN, 0.05)
    for sx_ in (-1, 1):
        B.box(Vector((sx_ * 0.3, 1.0, 0.5)), (0.08, 2.0, 0.2), I3, "metal", C_DARK, 0.2)                     # barrel saddles
    # gearbox at the back end of the barrel
    gbx = Vector((0, y0 + 0.05, 0.68))
    B.box(gbx, (0.6, 0.3, 0.55), I3, "metal", C_BLUE, 0.25, rust=0.2)
    B.box(gbx + Vector((0, -0.155, 0.1)), (0.3, 0.01, 0.14), I3, "panel", C_WHITE, 0.3)
    B.box(gbx + Vector((0.08, -0.162, 0.13)), (0.04, 0.004, 0.02), I3, "glow", C_AMBER, 0.05)
    # die plate at the exit: glowing orifices
    dp = Vector((0, y1, -0.35))
    B.box(dp, (1.8, 0.1, 0.9), I3, "metal", C_DARK, 0.2, rust=0.3)
    for i in range(3):
        for j in range(2):
            c = dp + Vector((-0.4 + i * 0.4, 0.05, -0.25 + j * 0.28))
            B.cyl(c, c + Vector((0, 0.01, 0)), 0.07, 12, "steel", C_STEEL, 0.15)
            B.cyl(c + Vector((0, 0.01, 0)), c + Vector((0, 0.014, 0)), 0.045, 12, "molten", C_MOLTEN, 0.05)
    for (x, z) in ((-0.8, -0.72), (0.8, -0.72), (-0.8, 0.02), (0.8, 0.02)):
        B.cyl(Vector((x, y1 + 0.05, z)), Vector((x, y1 + 0.07, z)), 0.03, 6, "steel", C_STEEL, rust=0.4)
    # pull roller stand
    for sx_ in (-1, 1):
        B.box(Vector((sx_ * 0.88, y1 + 0.35, -0.3)), (0.08, 0.2, 1.0), I3, "steel", C_STEEL, 0.2, rust=0.5)
    finish(B, "Frame", coll)
    for name, z in (("RollerA", BELT_TOP + 0.5), ("RollerB", BELT_TOP + 0.75)):
        Rl = Builder()
        Rl.cyl(Vector((-0.8, 0, 0)), Vector((0.8, 0, 0)), 0.09, 14, "rubber", C_BLACK, 0.1)
        for k in range(4):
            ring(Rl, Vector((-0.6 + k * 0.4, 0, 0)), (1, 0, 0), 0.085, 0.1, 0.05, 14, "steel", C_STEEL, 0.1)
        Rl.box(Vector((0.82, 0, 0.09)), (0.02, 0.03, 0.03), I3, "panel", C_YELLOW, 0.2)
        node(Rl, name, coll, Vector((0, y1 + 0.35, z)))
    Sc = Builder()
    Sc.cyl(Vector((0, -0.1, 0)), Vector((0, 0.05, 0)), 0.14, 14, "metal", C_DARK, 0.2)
    for k in range(4):
        a = k / 4 * math.tau
        Sc.box(Vector((math.cos(a) * 0.1, -0.05, math.sin(a) * 0.1)), (0.04, 0.1, 0.04), I3, "panel", C_YELLOW if k == 0 else C_STEEL, 0.2)
    node(Sc, "Screw", coll, gbx + Vector((0, -0.16, 0)))
    io_markers(coll)
    return coll

# ======================================================================================
def build_polisher():
    rng = random.Random(821); random.seed(821)
    coll = clear_collection("Machine_Polisher")
    B = Builder()
    y0, y1 = -0.1, 2.1
    guarded_belt(coll, B, rng, [(y0 + 1, y1 + 1)])
    B.box(Vector((1.0, 1.0, -0.97)), (3.96, 3.96, 0.06), I3, "steel", C_STEEL, 0.25, rust=0.65)            # shared skid
    # tunnel over the anchor column: dark frame, glass windows on the -X side, roof
    for y in (y0, 1.0, y1):
        for sx_ in (-1, 1):
            B.box(Vector((sx_ * 0.94, y, -0.2)), (0.1, 0.1, 1.6), I3, "metal", C_FRAME, 0.2, rust=0.3)
        B.box(Vector((0, y, 0.55)), (1.98, 0.1, 0.1), I3, "metal", C_FRAME, 0.2, rust=0.3)
    B.box(Vector((0, 1.0, 0.64)), (1.96, y1 - y0, 0.08), I3, "metal", C_DARK, 0.2, rust=0.3)
    panel_face(B, Vector((0, 1.0, 0.69)), (0, 0, 1), 1.8, 2.0, C_WHITE)
    for k, (ya, yb) in enumerate(((y0 + 0.05, 0.95), (1.05, y1 - 0.05))):
        B.box(Vector((-0.95, (ya + yb) / 2, -0.25)), (0.03, yb - ya, 0.9), I3, "glass", (0.55, 0.9, 1.0), 0.02)
        B.box(Vector((-0.95, (ya + yb) / 2, 0.3)), (0.05, yb - ya, 0.4), I3, "panel", C_WHITE, 0.3)
    hazard(B, Vector((-0.9, y0 - 0.051, 0.35)), (1, 0, 0), (0, 0, 1), 1.8, 0.12, (0, -1, 0), pitch=0.1)
    B.box(Vector((0, y0 - 0.05, 0.1)), (1.8, 0.02, 0.4), I3, "rubber", C_BLACK, 0.1)                           # entry flaps
    for k in range(6):
        B.box(Vector((-0.75 + k * 0.3, y0 - 0.05, -0.3)), (0.28, 0.015, 0.45), I3, "rubber", (0.05, 0.05, 0.05), 0.1)
    # brush axle bearings on the +X tunnel wall
    for y in (0.45, 1.55):
        B.cyl(Vector((0.86, y, -0.35)), Vector((0.99, y, -0.35)), 0.09, 12, "metal", C_DARK, 0.2)
    # +X column: motor and belt drive, extractor, fluid tank, control panel
    base = Vector((2.0, 1.0, 0.0))
    B.box(Vector((2.0, 1.0, -0.5)), (1.8, 3.6, 0.9), I3, "metal", C_FRAME, 0.2, rust=0.3)
    for n, w in ((Vector((1, 0, 0)), 3.4), (Vector((0, 1, 0)), 1.6), (Vector((0, -1, 0)), 1.6)):
        c = Vector((2.0, 1.0, -0.5)) + Vector((n.x * 0.9, n.y * 1.8, 0))
        panel_face(B, c, n, w, 0.75, C_WHITE)
    motor = Vector((1.5, 1.0, 0.2))
    B.box(motor, (0.6, 0.9, 0.5), I3, "metal", C_BLUE, 0.25, rust=0.15)
    B.cyl(motor + Vector((-0.3, 0, 0)), motor + Vector((-0.4, 0, 0)), 0.1, 12, "steel", C_STEEL, rust=0.3)
    for y in (0.45, 1.55):                                                                         # drive belts
        B.pipe([motor + Vector((-0.42, 0, 0.08)), Vector((1.0, y, -0.28)), Vector((0.99, y, -0.35))], 0.02, 6)
    # dust extractor: drum with a duct into the tunnel roof
    ex = Vector((2.4, 2.4, 0.0))
    B.cyl(ex + Vector((0, 0, -0.05)), ex + Vector((0, 0, 0.75)), 0.4, 24, "steel", (0.3, 0.3, 0.3), 0.2, rust=0.45)
    ring(B, ex + Vector((0, 0, 0.75)), (0, 0, 1), 0.3, 0.42, 0.06, 24, "metal", C_DARK, 0.2)
    B.pipe([ex + Vector((-0.3, 0, 0.55)), Vector((1.4, 2.0, 0.75)), Vector((0.5, 1.6, 0.75)), Vector((0.3, 1.5, 0.7))], 0.1, 10, "metal", (0.12, 0.12, 0.12))
    # polish fluid tank (glass) and control panel
    tk = Vector((2.45, 0.1, 0.0))
    B.cyl(tk + Vector((0, 0, -0.05)), tk + Vector((0, 0, 0.1)), 0.3, 20, "metal", C_DARK, 0.2)
    B.cyl(tk + Vector((0, 0, 0.1)), tk + Vector((0, 0, 0.7)), 0.28, 20, "glass", (0.55, 0.9, 1.0), 0.02)
    B.cyl(tk + Vector((0, 0, 0.1)), tk + Vector((0, 0, 0.45)), 0.26, 20, "cyan", C_CYAN, 0.05)
    B.cyl(tk + Vector((0, 0, 0.7)), tk + Vector((0, 0, 0.78)), 0.3, 20, "metal", C_DARK, 0.2)
    B.pipe([tk + Vector((-0.3, 0, 0.2)), Vector((1.2, 0.2, 0.3)), Vector((0.9, 0.5, 0.45))], 0.02, 6)
    cp = Vector((2.9, 1.2, -0.3))
    B.box(cp + Vector((0.01, 0, 0.1)), (0.02, 0.6, 0.35), I3, "metal", (0.02, 0.03, 0.03), 0.05)
    for k in range(3):
        B.box(cp + Vector((0.022, -0.15 + k * 0.15, 0.15)), (0.004, 0.1, 0.06), I3, "glow", C_AMBER, 0.05)
    B.box(cp + Vector((0.022, 0, 0.0)), (0.004, 0.4, 0.02), I3, "cyan", C_CYAN, 0.05)
    hazard(B, Vector((2.999, -0.8, -0.95)), (0, 1, 0), (0, 0, 1), 3.6, 0.08, (1, 0, 0), pitch=0.12)
    finish(B, "Frame", coll)
    for name, y in (("BrushA", 0.45), ("BrushB", 1.55)):
        Br = Builder()
        Br.cyl(Vector((-0.86, 0, 0)), Vector((0.86, 0, 0)), 0.05, 10, "steel", C_STEEL, 0.1)
        for k in range(12):
            a = k / 12 * math.tau
            d = Vector((0, math.cos(a), math.sin(a)))
            Br.box(d * 0.18, (1.6, 0.03, 0.26), Matrix.Rotation(a, 3, 'X'), "rubber", (0.55, 0.12, 0.08) if k % 2 else (0.35, 0.08, 0.05), 0.15)
        node(Br, name, coll, Vector((0, y, -0.35)))
    Bu = Builder()
    Bu.cyl(Vector((0, 0, -0.2)), Vector((0, 0, 0.2)), 0.3, 20, "wood", (0.8, 0.78, 0.7), 0.2)
    Bu.cyl(Vector((0, 0, 0.2)), Vector((0, 0, 0.3)), 0.06, 10, "steel", C_STEEL, 0.1)
    node(Bu, "Buffer", coll, Vector((-0.55, 1.0, -0.45)))
    Fn = Builder()
    for k in range(6):
        a = k / 6 * math.tau
        Fn.box(Vector((math.cos(a) * 0.17, math.sin(a) * 0.17, 0)), (0.26, 0.08, 0.012), Matrix.Rotation(a, 3, 'Z') @ Matrix.Rotation(0.4, 3, 'X'), "metal", (0.2, 0.2, 0.21), 0.15)
    Fn.cyl(Vector((0, 0, -0.02)), Vector((0, 0, 0.02)), 0.06, 10, "metal", C_DARK)
    node(Fn, "Fan", coll, ex + Vector((0, 0, 0.74)))
    io_markers(coll)
    return coll

PIECES = [
    (build_press, "plate_press.glb"),
    (build_extruder, "rod_extruder.glb"),
    (build_polisher, "polisher.glb"),
]
ICONS = [
    ("Machine_PlatePress", "blueprints/blueprint_plate_press.png", "blueprint", (1.5, 0.9, 0.8)),
    ("Machine_RodExtruder", "blueprints/blueprint_rod_extruder.png", "blueprint", (1.5, 0.9, 0.8)),
    ("Machine_Polisher", "blueprints/blueprint_polisher.png", "blueprint", (1.3, 1.0, 1.0)),
]

def build_all(do_export=True):
    return build_family(PIECES, OUT_DIR, do_export)

if __name__ == "__main__":
    build_all(do_export=False)
