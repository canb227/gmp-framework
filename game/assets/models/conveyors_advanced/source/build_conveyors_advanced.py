"""
Advanced conveyors: the basic conveyor set rebuilt with refurbished facility parts. The belt, stringers and
guide lips match the basic pieces exactly (so they tile with them and share collider layouts); the guards are
clean facility panels ADV_GUARD_TOP (0.5 m) above the belt instead of 0.25 m, on dark posts under a steel top
rail with a light strip, for more throughput without spills.

  conveyor_adv_straight.glb    1x1x1
  conveyor_adv_turn_right.glb  1x1x1 (enters at the back, leaves through +X)   conveyor_adv_turn_left.glb (mirror)
  conveyor_adv_slope.glb       1x2x2 like conveyor_slope (4 m run, 2 m rise)
  conveyor_adv_loader.glb      1x1x1 like conveyor_loader (lip 0.35 m above belt height)

Nodes: Belt (scrolling belt material), Frame. Run: tools/blender/run.py build conveyors_advanced
"""
import bpy, bmesh, math, random, os
from mathutils import Vector, Matrix

_HERE = (os.path.dirname(os.path.abspath(__file__)) if "__file__" in globals()
         else r"C:\Users\steph\OneDrive\Documents\godot\projects\gmp-framework\game\assets\models\conveyors_advanced\source")
_LIB = os.path.normpath(os.path.join(_HERE, "..", "..", "shared", "salvage_lib.py"))
_S = {"__name__": "salvage_lib", "__file__": _LIB}
exec(compile(open(_LIB, encoding="utf-8").read(), _LIB, "exec"), _S)
_SKIP = {"HERE", "OUT_DIR", "PIECES", "build_all", "ICONS", "export", "CONV"}
globals().update({k: v for k, v in _S.items() if not k.startswith("__") and k not in _SKIP})
HERE = _HERE
OUT_DIR = os.path.dirname(HERE)

ADV_GUARD_TOP = 0.5          # guard height above the belt top (basic: GUARD_TOP = 0.25)
POST_X = (0.855, 0.965)      # joint posts and top rail, across the belt

def span(a, b, lo, hi):
    return max(lo, a), min(hi, b)

def adv_side(B, path, side, guard=True, panel_pitch=0.66, drive_at=None):
    """Stringer, lips, bolts and (optionally) refurbished guards along one side (side = +1 right, -1 left)."""
    L = path.L
    sx = lambda a, b: tuple(sorted((side * a, side * b)))
    B.sweep(path, 0, L, *sx(*STR_X), STR_O[0], STR_O[1], "metal", C_FRAME, 0.12, rust=0.04)
    B.sweep(path, 0, L, *sx(*LIP_X), LIP_O[0], LIP_O[1], "steel", C_WEAR, 0.1)
    n = max(1, int(round(L / 0.25)))
    for i in range(n):
        s = (i + 0.5) * L / n
        p = path.point(s, side * STR_X[1], -0.07); sd = path.frame(s)[2]
        B.cyl(p, p + sd * side * 0.01, 0.01, 6, "steel", C_STEEL, 0.1)
    if not guard:
        return
    n = max(1, int(round(L / panel_pitch)))
    inner = GUARD_X[0]
    for i in range(n):
        a = L * i / n + (0.022 if i > 0 else 0.0)
        b = L * (i + 1) / n - (0.022 if i < n - 1 else 0.0)
        B.sweep(path, a, b, *sx(*GUARD_X), STR_O[1], ADV_GUARD_TOP, "panel", C_FACILITY, 0.1)
        # recessed seam and a dark kick strip along the bottom on the belt side
        B.sweep(path, a + 0.02, b - 0.02, *sx(inner - 0.004, inner), ADV_GUARD_TOP - 0.12, ADV_GUARD_TOP - 0.108, "metal", (0.25, 0.25, 0.25), 0.1)
        B.sweep(path, a + 0.01, b - 0.01, *sx(inner - 0.006, inner), STR_O[1], STR_O[1] + 0.07, "metal", C_DARK, 0.1)
    for i in range(n + 1):
        s0, s1 = span(L * i / n - 0.028, L * i / n + 0.028, 0.0, L)
        B.sweep(path, s0, s1, *sx(*POST_X), STR_O[0], ADV_GUARD_TOP + 0.03, "metal", C_DARK, 0.12)
    B.sweep(path, 0, L, *sx(POST_X[0], POST_X[1]), ADV_GUARD_TOP, ADV_GUARD_TOP + 0.03, "steel", C_STEEL, 0.1, rust=0.03)
    B.sweep(path, 0.04, L - 0.04, *sx(POST_X[0] - 0.008, POST_X[0]), ADV_GUARD_TOP - 0.04, ADV_GUARD_TOP - 0.022, "lamp", C_LAMP, 0.02)
    if drive_at is not None:
        # compact drive module clipped to the outside of the guard: white housing, vents, status LEDs
        bas = frame_basis(path, drive_at); _, _, sd, up = path.frame(drive_at)
        c = path.point(drive_at, side * 0.935, 0.2)
        B.box(c + sd * side * 0.01, (0.07, 0.44, 0.3), bas, "panel", C_WHITE, 0.2)
        for k in range(5):
            B.box(c + sd * side * 0.047 + up * (0.08 - k * 0.035), (0.004, 0.3, 0.012), bas, "metal", (0.05, 0.05, 0.05), 0.1)
        B.box(c + sd * side * 0.047 + up * 0.12 + path.frame(drive_at)[1] * 0.16, (0.004, 0.03, 0.02), bas, "cyan", C_CYAN, 0.05)
        B.box(c + sd * side * 0.047 + up * 0.12 + path.frame(drive_at)[1] * 0.11, (0.004, 0.03, 0.02), bas, "glow", C_AMBER, 0.05)

def conduit(B, path, x=-0.99, o=-0.1):
    """Power conduit along the outside of the left stringer (a tidier cable run than the basic zip-tied one)."""
    L = path.L
    B.pipe([path.point(s, x + 0.005, o) for s in path.samples(0, L, 10)], 0.012, 6, "metal", C_DARK)
    for s in [L * (i + 0.5) / max(1, int(round(L / 0.5))) for i in range(max(1, int(round(L / 0.5))))]:
        B.box(path.point(s, x + 0.005, o), (0.02, 0.03, 0.04), frame_basis(path, s), "steel", C_STEEL, 0.1)

# ======================================================================================
def build_straight():
    random.seed(101)
    coll = clear_collection("ConveyorAdv_Straight")
    path = PATHS["straight"]
    build_belt(path, "Belt", coll, 2.0)
    B = Builder()
    adv_side(B, path, 1, drive_at=1.2)
    adv_side(B, path, -1)
    conduit(B, path)
    B.to_object("Frame", coll)
    return coll

def build_turn():
    random.seed(103)
    coll = clear_collection("ConveyorAdv_TurnRight")
    path = PATHS["turn"]
    build_belt(path, "Belt", coll, 1.5)
    B = Builder()
    adv_side(B, path, -1, panel_pitch=0.5, drive_at=path.L / 2)
    adv_side(B, path, 1, panel_pitch=2.0)
    conduit(B, path)
    # pivot column: dark post with a lamp band, capped
    piv = Vector((1 - 0.07, -1 + 0.07, 0))
    B.cyl(piv + Vector((0, 0, FLOOR)), piv + Vector((0, 0, BELT_TOP + ADV_GUARD_TOP + 0.04)), 0.05, 12, "metal", C_DARK, 0.1)
    ring(B, piv + Vector((0, 0, BELT_TOP + ADV_GUARD_TOP - 0.03)), (0, 0, 1), 0.045, 0.056, 0.02, 12, "lamp", C_LAMP, 0.02)
    B.cyl(piv + Vector((0, 0, BELT_TOP + ADV_GUARD_TOP + 0.04)), piv + Vector((0, 0, BELT_TOP + ADV_GUARD_TOP + 0.06)), 0.06, 12, "steel", C_STEEL)
    B.to_object("Frame", coll)
    return coll

def build_turn_left():
    return mirror_collection(bpy.data.collections["ConveyorAdv_TurnRight"], "ConveyorAdv_TurnLeft")

def build_slope():
    random.seed(107)
    coll = clear_collection("ConveyorAdv_Slope")
    path = PATHS["slope"]
    build_belt(path, "Belt", coll, 4.5)
    B = Builder()
    adv_side(B, path, 1, panel_pitch=0.9, drive_at=0.7)
    adv_side(B, path, -1, panel_pitch=0.9)
    conduit(B, path)
    # square section legs with foot plates outside each stringer, braced
    for side in (-1, 1):
        x = side * 0.975
        feet = []
        for s in (2.15, path.L - 0.35):
            top = path.point(s, x, STR_O[0] + 0.02)
            h = top.z - FLOOR
            B.box(Vector((x, top.y, FLOOR + h / 2)), (0.045, 0.06, h), I3, "metal", C_DARK, 0.12)
            B.box(Vector((x - side * 0.005, top.y, FLOOR + 0.006)), (0.05, 0.16, 0.012), I3, "steel", C_STEEL, 0.1)
            feet.append(top)
        B.cyl(Vector((x, feet[0].y, FLOOR + 0.3)), Vector((x, feet[1].y, feet[1].z - 0.25)), 0.014, 6, "steel", C_STEEL, 0.1)
    B.to_object("Frame", coll)
    return coll

def build_loader():
    random.seed(109)
    coll = clear_collection("ConveyorAdv_Loader")
    path, flat, ls = bend_up_path(**LOADER)
    build_belt(path, "Belt", coll, 2.0)
    B = Builder()
    adv_side(B, path, 1, panel_pitch=0.5, drive_at=0.45)
    adv_side(B, path, -1, panel_pitch=0.5)
    conduit(B, path)
    for side in (-1, 1):
        x = side * 0.975
        top = path.point(path.L - 0.3, x, STR_O[0] + 0.02)
        h = top.z - FLOOR
        B.box(Vector((x, top.y, FLOOR + h / 2)), (0.045, 0.06, h), I3, "metal", C_DARK, 0.12)
        B.box(Vector((x - side * 0.005, top.y, FLOOR + 0.006)), (0.05, 0.16, 0.012), I3, "steel", C_STEEL, 0.1)
    lip = path.point(path.L, 0, 0)
    B.box(Vector((0, lip.y - 0.03, lip.z - 0.06)), (1.72, 0.05, 0.1), I3, "steel", C_WEAR, 0.1)
    B.box(Vector((0, lip.y - 0.03, lip.z - 0.22)), (1.72, 0.05, 0.22), I3, "metal", C_DARK, 0.12)
    hazard(B, Vector((-0.86, lip.y - 0.004, lip.z - 0.28)), (1, 0, 0), (0, 0, 1), 1.72, 0.14, (0, 1, 0), pitch=0.1)
    B.to_object("Frame", coll)
    return coll

PIECES = [
    (build_straight, "conveyor_adv_straight.glb"),
    (build_turn, "conveyor_adv_turn_right.glb"),
    (build_turn_left, "conveyor_adv_turn_left.glb"),
    (build_slope, "conveyor_adv_slope.glb"),
    (build_loader, "conveyor_adv_loader.glb"),
]
ICONS = [
    ("ConveyorAdv_Straight", "blueprints/blueprint_conveyor_adv.png", "blueprint", (1.3, 1.0, 1.05)),
    ("ConveyorAdv_TurnRight", "blueprints/blueprint_conveyor_adv_turn.png", "blueprint", (-0.35, -0.9, 1.6)),
    ("ConveyorAdv_Slope", "blueprints/blueprint_conveyor_adv_slope.png", "blueprint", (1.6, 0.9, 1.0)),
    ("ConveyorAdv_Loader", "blueprints/blueprint_conveyor_adv_loader.png", "blueprint", (1.6, 0.9, 1.0)),
]

def build_all(do_export=True):
    return build_family(PIECES, OUT_DIR, do_export)

if __name__ == "__main__":
    build_all(do_export=False)
