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

# ---------- lean rules for these many-times-placed pieces ----------
SHARP_BELOW = 0.035                            # boxes thinner than this stay sharp: a < 7 mm chamfer only costs tris
FOLD = {"tape": "rubber", "wood": "rubber"}    # look-alike surfaces share one material (one draw call fewer)

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

def lean_path(path, straight, max_per_m=None):
    """The same path, but its default sampling leaves out the stations inside the straight spans [(s0, s1)]:
    a straight run needs no rings in its middle. Explicit per_m (sagging cables) still samples uniformly, at
    most max_per_m stations per metre when given."""
    lp = Path(path.L, path.fn, path.stations)
    uniform = lp.samples
    def samples(s0, s1, per_m=None):
        if per_m is not None:
            return uniform(s0, s1, min(per_m, max_per_m or per_m))
        ss = uniform(s0, s1)
        lo, hi = min(s0, s1), max(s0, s1)
        ss = [lo, hi] + [s for s in ss if not any(a + 1e-6 < s < b - 1e-6 for a, b in straight)]
        ss += [e for a, b in straight for e in (a, b) if lo + 1e-4 < e < hi - 1e-4]
        out = []
        for s in sorted(ss):
            if not out or s - out[-1] > 1e-4:
                out.append(s)
        return out[::-1] if s0 > s1 else out
    lp.samples = samples
    return lp

def lean_slope():
    a1 = SLOPE_R * SLOPE_TH
    return lean_path(PATHS["slope"], [(a1, a1 + SLOPE_LS)])

def lean_bend_up(max_per_m=None, **kw):
    path, flat, ls = bend_up_path(**kw)
    return lean_path(path, [(0.0, flat), (path.L - ls, path.L)], max_per_m), flat, ls

def mirror(src_name, dst_name):
    """mirror_collection plus the weighted normals the copies would otherwise lose."""
    dst = mirror_collection(bpy.data.collections[src_name], dst_name)
    for ob in dst.objects:
        weighted_normals(ob)
    return dst

def stud(B, p, n, r=0.011, h=0.01, mat="steel", c=C_STEEL, rust=0.3):
    """Bolt head standing on a surface along n: six sides and a top, no hidden bottom (16 tris)."""
    n = Vector(n).normalized()
    u = n.cross(ZV if abs(n.z) < 0.9 else Vector((1, 0, 0))).normalized(); w = n.cross(u)
    rim = [(u * math.cos(k / 6 * math.tau) + w * math.sin(k / 6 * math.tau)) * r for k in range(6)]
    v0 = [B.bm.verts.new(p + d) for d in rim]; v1 = [B.bm.verts.new(p + d + n * h) for d in rim]
    fs = [B.quad([v0[k], v0[(k + 1) % 6], v1[(k + 1) % 6], v1[k]], mat) for k in range(6)] + [B.quad(v1, mat)]
    B.paint(fs, c, 0.15, rust)
    return fs

def decal(B, pts, n, mat="panel", c=C_DARK, var=0.1):
    """Flat painted shape (one n-gon) facing n, e.g. a flow arrow on a guard."""
    f = B.quad([B.bm.verts.new(Vector(p)) for p in pts], mat)
    if f.normal.dot(Vector(n)) < 0:
        f.normal_flip()
    B.paint([f], c, var)
    return f

def chevron(B, path, s, side, x, o, size=0.07, c=(0.3, 0.3, 0.32)):
    """Flow-direction arrow painted on a face at x across the path (outer faces), pointing along +s."""
    q = lambda ds, do: path.point(s + ds * size, x, o + do * size)
    pts = [q(-1, 1), q(-0.4, 1), q(0.6, 0), q(-0.4, -1), q(-1, -1), q(0, 0)]
    return decal(B, pts, path.frame(s)[2] * side, "panel", c)

ADV_GUARD_TOP = 0.5          # guard height above the belt top (basic: GUARD_TOP = 0.25)
POST_X = (0.855, 0.965)      # joint posts and top rail, across the belt

def span(a, b, lo, hi):
    return max(lo, a), min(hi, b)

def adv_side(B, path, side, guard=True, panel_pitch=0.66, drive_at=None, trim=True):
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
        if trim and b - a > 0.3:                                                # flow arrow on the outer face
            chevron(B, path, (a + b) / 2, side, side * (GUARD_X[1] + 0.002), 0.28)
    for i in range(n + 1):
        s0, s1 = span(L * i / n - 0.028, L * i / n + 0.028, 0.0, L)
        B.sweep(path, s0, s1, *sx(*POST_X), STR_O[0], ADV_GUARD_TOP + 0.03, "metal", C_DARK, 0.12)
        sm = (s0 + s1) / 2; sd = path.frame(sm)[2]
        for o in (-0.07, ADV_GUARD_TOP - 0.1) if trim else ():                  # post fixings, outside face
            stud(B, path.point(sm, side * POST_X[1], o), sd * side, 0.01, 0.008, rust=0.05)
    B.sweep(path, 0, L, *sx(POST_X[0], POST_X[1]), ADV_GUARD_TOP, ADV_GUARD_TOP + 0.03, "steel", C_STEEL, 0.1, rust=0.03)
    B.sweep(path, 0.04, L - 0.04, *sx(POST_X[0] - 0.008, POST_X[0]), ADV_GUARD_TOP - 0.04, ADV_GUARD_TOP - 0.022, "lamp", C_LAMP, 0.02)
    if drive_at is not None:
        # compact drive module clipped to the outside of the guard: white housing, vents, status LEDs
        bas = frame_basis(path, drive_at); _, _, sd, up = path.frame(drive_at)
        c = path.point(drive_at, side * 0.935, 0.2)
        B.box(c + sd * side * 0.01, (0.07, 0.44, 0.3), bas, "panel", C_WHITE, 0.2)
        for k in range(5):
            B.box(c + sd * side * 0.047 + up * (0.08 - k * 0.035), (0.004, 0.3, 0.012), bas, "metal", (0.05, 0.05, 0.05), 0.1)
        for dt in (0.16, 0.11):                     # status LEDs share the light-strip material (no extra draw calls)
            B.box(c + sd * side * 0.047 + up * 0.12 + path.frame(drive_at)[1] * dt, (0.004, 0.03, 0.02), bas, "lamp", C_LAMP, 0.05)
        for dt in (-0.19, 0.19):                    # housing screws
            for do in (-0.12, 0.12):
                stud(B, c + sd * side * 0.045 + up * do + path.frame(drive_at)[1] * dt, sd * side, 0.008, 0.005, rust=0.0)

def conduit(B, path, x=-0.99, o=-0.1):
    """Power conduit along the outside of the left stringer (a tidier cable run than the basic zip-tied one)."""
    L = path.L
    B.pipe([path.point(s, x + 0.005, o) for s in path.samples(0, L)], 0.012, 6, "metal", C_DARK)
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
    adv_side(B, path, 1, panel_pitch=2.0, trim=False)             # the tight inner side: no arrows / bolts
    conduit(B, path)
    # pivot column: dark post with a lamp band, capped
    piv = Vector((1 - 0.07, -1 + 0.07, 0))
    B.cyl(piv + Vector((0, 0, FLOOR)), piv + Vector((0, 0, BELT_TOP + ADV_GUARD_TOP + 0.04)), 0.05, 12, "metal", C_DARK, 0.1)
    ring(B, piv + Vector((0, 0, BELT_TOP + ADV_GUARD_TOP - 0.03)), (0, 0, 1), 0.045, 0.056, 0.02, 12, "lamp", C_LAMP, 0.02)
    B.cyl(piv + Vector((0, 0, BELT_TOP + ADV_GUARD_TOP + 0.04)), piv + Vector((0, 0, BELT_TOP + ADV_GUARD_TOP + 0.06)), 0.06, 12, "steel", C_STEEL)
    B.to_object("Frame", coll)
    return coll

def build_turn_left():
    return mirror("ConveyorAdv_TurnRight", "ConveyorAdv_TurnLeft")

def build_slope():
    random.seed(107)
    coll = clear_collection("ConveyorAdv_Slope")
    path = lean_slope()
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
        for f in feet:                                                          # anchor bolts on the foot plates
            for dy in (-0.055, 0.055):
                stud(B, Vector((x - side * 0.005, f.y + dy, FLOOR + 0.012)), ZV, 0.012, 0.01, rust=0.1)
    for f in feet:                                                              # tie bar between the leg pairs
        B.box(Vector((0, f.y, FLOOR + 0.3)), (1.9, 0.04, 0.05), I3, "metal", C_DARK, 0.12)
    B.to_object("Frame", coll)
    return coll

def build_loader():
    random.seed(109)
    coll = clear_collection("ConveyorAdv_Loader")
    path, flat, ls = lean_bend_up(**LOADER)
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
