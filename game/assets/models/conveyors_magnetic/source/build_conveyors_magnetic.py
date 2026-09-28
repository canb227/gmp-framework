"""
Magnetic conveyors: the basic salvage conveyors with electromagnets bolted on, so items stay stuck to the belt
when the conveyor is mounted on a wall or a ceiling. Same belt / stringer / guard layout as the basic pieces
(same colliders), plus:
  - electromagnet coil modules standing on the stringers, cyan field strips along both belt edges
  - a power box feeding the coils, and anchor plates on the underside (the face that meets the wall or ceiling)

  conveyor_mag_straight.glb, conveyor_mag_turn_right.glb, conveyor_mag_turn_left.glb (mirror)

The models are built floor-mounted; the wall and ceiling scenes turn them. Nodes: Belt, Frame, Glow (the cyan
parts, to show only while the magnets are energised).
"""
import bpy, bmesh, math, random, os
from mathutils import Vector, Matrix

_HERE = (os.path.dirname(os.path.abspath(__file__)) if "__file__" in globals()
         else r"C:\Users\steph\OneDrive\Documents\godot\projects\gmp-framework\game\assets\models\conveyors_magnetic\source")
_LIB = os.path.normpath(os.path.join(_HERE, "..", "..", "shared", "salvage_lib.py"))
_S = {"__name__": "salvage_lib", "__file__": _LIB}
exec(compile(open(_LIB, encoding="utf-8").read(), _LIB, "exec"), _S)
_SKIP = {"HERE", "OUT_DIR", "PIECES", "build_all", "ICONS", "export", "CONV"}
globals().update({k: v for k, v in _S.items() if not k.startswith("__") and k not in _SKIP})
HERE = _HERE
OUT_DIR = os.path.dirname(HERE)

def coil(B, G, path, s, side):
    """An electromagnet module on the stringer: dark yoke, copper winding, cyan pole cap (in Glow)."""
    bas = frame_basis(path, s); _, t, sd, up = path.frame(s)
    c = path.point(s, side * 0.915, 0.12)
    B.box(c - up * 0.1, (0.13, 0.16, 0.06), bas, "metal", C_DARK, 0.2, rust=0.3)
    B.cyl(c - up * 0.07, c + up * 0.12, 0.06, 12, "copper", C_COPPER, 0.15)
    for k in range(5):
        ring(B, c + up * (-0.05 + k * 0.035), up, 0.058, 0.064, 0.008, 12, "copper", (0.45, 0.2, 0.08), 0.1)
    B.cyl(c + up * 0.12, c + up * 0.15, 0.07, 12, "metal", C_DARK, 0.15)
    G.cyl(c + up * 0.15, c + up * 0.157, 0.045, 12, "cyan", C_CYAN, 0.05)
    B.box(c + up * 0.02 + t * 0.075, (0.1, 0.02, 0.1), bas, "tape", C_TAPE, 0.25)

def mag_frame(B, G, path, rng, coils_at, panel_len=(0.5, 0.95), inner_guard=True):
    for side in (-1, 1):
        guard = side < 0 or inner_guard
        build_side(B, path, side, rng, panel_len=panel_len if guard else (2.0, 2.0), brackets=guard, hubs=guard)
        # field strip along the belt edge, on the inside of the stringer lip
        G.sweep(path, 0.02, path.L - 0.02, *sorted((side * 0.842, side * 0.848)), -0.02, 0.008, "cyan", C_CYAN, 0.05)
    for s, side in coils_at:
        coil(B, G, path, s, side)
    # anchor plates on the underside at the four corners of the stringers
    for s in (0.12, path.L - 0.12):
        for side in (-1, 1):
            p = path.point(s, side * 0.895, 0)
            B.box(Vector((p.x, p.y, FLOOR + 0.012)), (0.16, 0.16, 0.024), I3, "steel", C_STEEL, 0.2, rust=0.6)
            B.box(Vector((p.x, p.y, (FLOOR + BELT_TOP - 0.15) / 2 + 0.01)), (0.05, 0.05, BELT_TOP - 0.15 - FLOOR), I3, "steel", C_STEEL, 0.2, rust=0.5)
            for dx, dy in ((-0.05, -0.05), (0.05, 0.05)):
                B.cyl(Vector((p.x + dx, p.y + dy, FLOOR + 0.024)), Vector((p.x + dx, p.y + dy, FLOOR + 0.034)), 0.012, 6, "steel", C_STEEL, rust=0.4)

def power_box(B, G, path, s, side):
    bas = frame_basis(path, s); _, t, sd, up = path.frame(s)
    c = path.point(s, side * 0.955, -0.04)
    B.box(c, (0.07, 0.34, 0.2), bas, "metal", C_BLUE, 0.25, rust=0.2)
    B.box(c + sd * side * 0.037, (0.004, 0.2, 0.1), bas, "panel", C_WHITE, 0.3)
    G.box(c + sd * side * 0.04 + up * 0.03, (0.004, 0.06, 0.025), bas, "cyan", C_CYAN, 0.05)
    B.box(c + sd * side * 0.04 - up * 0.03, (0.004, 0.06, 0.025), bas, "glow", C_AMBER, 0.05)
    hazard(B, c + sd * side * 0.0355 - t * 0.17 - up * 0.1, t, up, 0.34, 0.03, sd * side, pitch=0.06)

def build_straight():
    rng = random.Random(701); random.seed(701)
    coll = clear_collection("ConveyorMag_Straight")
    path = PATHS["straight"]
    build_belt(path, "Belt", coll, 2.0)
    B, G = Builder(), Builder()
    mag_frame(B, G, path, rng, [(0.45, 1), (1.55, 1), (0.45, -1), (1.55, -1)])
    build_cable(B, path)
    power_box(B, G, path, 1.0, 1)
    B.to_object("Frame", coll); G.to_object("Glow", coll)
    return coll

def build_turn():
    rng = random.Random(703); random.seed(703)
    coll = clear_collection("ConveyorMag_TurnRight")
    path = PATHS["turn"]
    build_belt(path, "Belt", coll, 1.5)
    B, G = Builder(), Builder()
    mag_frame(B, G, path, rng, [(0.35, -1), (path.L / 2, -1), (path.L - 0.35, -1)], panel_len=(0.34, 0.5), inner_guard=False)
    build_cable(B, path, clip_every=0.5)
    piv = Vector((1 - 0.07, -1 + 0.07, 0))
    B.cyl(piv + Vector((0, 0, FLOOR)), piv + Vector((0, 0, BELT_TOP + GUARD_TOP + 0.05)), 0.045, 10, "steel", C_STEEL, rust=0.5)
    B.cyl(piv + Vector((0, 0, BELT_TOP + GUARD_TOP + 0.05)), piv + Vector((0, 0, BELT_TOP + GUARD_TOP + 0.09)), 0.07, 12, "copper", C_COPPER, 0.15)
    G.cyl(piv + Vector((0, 0, BELT_TOP + GUARD_TOP + 0.09)), piv + Vector((0, 0, BELT_TOP + GUARD_TOP + 0.097)), 0.05, 12, "cyan", C_CYAN, 0.05)
    power_box(B, G, path, path.L * 0.25, -1)
    B.to_object("Frame", coll); G.to_object("Glow", coll)
    return coll

def build_turn_left():
    return mirror_collection(bpy.data.collections["ConveyorMag_TurnRight"], "ConveyorMag_TurnLeft")

PIECES = [
    (build_straight, "conveyor_mag_straight.glb"),
    (build_turn, "conveyor_mag_turn_right.glb"),
    (build_turn_left, "conveyor_mag_turn_left.glb"),
]
ICONS = [
    ("ConveyorMag_Straight", "blueprints/blueprint_conveyor_mag.png", "blueprint", (1.3, 1.0, 1.05)),
    ("ConveyorMag_TurnRight", "blueprints/blueprint_conveyor_mag_turn.png", "blueprint", (-0.35, -0.9, 1.6)),
]

def build_all(do_export=True):
    return build_family(PIECES, OUT_DIR, do_export)

if __name__ == "__main__":
    build_all(do_export=False)
