"""
Level-building kit for the ruined facility: modular pieces on a 4 m module (two 2 m grid cells), in the same
salvaged-facility look as the structures and the decor props (white panels on dark frames, concrete, rust,
moss). Origin on the floor at the piece's centre; +Y (Godot -Z) runs along corridors and catwalks. The
colliders live in tools/decor/gen_decor.py (KIT) and use the same numbers.

  kit_floor, kit_floor_cracked              4x4 m floor slab (top at z 0, 0.3 thick)
  kit_wall, kit_wall_damaged, kit_wall_window, kit_doorway
                                            4 m wide, 4 m tall, 0.3 thick wall centred on y = 0
  kit_hallway, kit_hallway_corner, kit_hallway_broken
                                            4x4x4 m corridor section (floor, walls, ceiling with lights)
  kit_catwalk, kit_catwalk_corner, kit_catwalk_stairs, kit_catwalk_support
                                            1.6 m grated catwalk (deck top at z 0); stairs rise 2 m over 4 m;
                                            the support is a 4 m post that holds a deck at its top
  kit_stairs                                concrete stair: 2 m rise over 4 m, 2.4 m wide
  kit_column                                0.8 m column, 4 m tall
  kit_railing                               4 m safety railing
"""
import bpy, bmesh, math, random, os
from mathutils import Vector, Matrix

_HERE = (os.path.dirname(os.path.abspath(__file__)) if "__file__" in globals()
         else r"C:\\Users\\steph\\OneDrive\\Documents\\godot\\projects\\gmp-framework\\game\\assets\\models\\props\\kit\\source")
_DEC = os.path.normpath(os.path.join(_HERE, "..", "..", "decor", "source", "build_decor.py"))
_S = {"__name__": "decor_lib", "__file__": _DEC}
exec(compile(open(_DEC, encoding="utf-8").read(), _DEC, "exec"), _S)
_SKIP = {"HERE", "OUT_DIR", "PIECES", "build_all", "ICONS", "export", "CONV"}
globals().update({k: v for k, v in _S.items() if not k.startswith("__") and k not in _SKIP})
HERE = (os.path.dirname(os.path.abspath(__file__)) if "__file__" in globals() else _HERE)
OUT_DIR = os.path.dirname(HERE)

M4, WT, H = 4.0, 0.3, 4.0

def kit(name):
    random.seed(sum(map(ord, name)))
    return clear_collection(f"Kit_{name}")

def floor(B, cracked=False):
    B.box(Vector((0, 0, -0.15)), (M4, M4, 0.3), I3, "rock", C_CONC_D, 0.12)
    for i in range(4):
        for j in range(4):
            if cracked and (i, j) in ((3, 0), (2, 0), (3, 1)):
                continue                                                     # broken-out tiles
            tilt = Matrix.Rotation(random.uniform(-0.04, 0.04), 3, 'X') if cracked and (i + j) % 3 == 0 else I3
            B.box(Vector((-1.5 + i, -1.5 + j, 0.005)), (0.96, 0.96, 0.02), tilt, "rock", jit(C_CONC, 0.06), 0.1)
    if cracked:
        for k in range(3):
            chunk(B, Vector((1.3 + random.uniform(-0.3, 0.3), -1.3 + random.uniform(-0.4, 0.4), 0.05)), 0.18, 500 + k)
        moss(B, Vector((1.4, -1.2, -0.05)), 0.6, 505)
        fern(B, Vector((1.5, -1.5, -0.1)), 506, 0.7)
        for k in range(3):
            B.box(Vector((random.uniform(-1.5, 1.0), random.uniform(-1.5, 1.5), 0.017)), (random.uniform(0.6, 1.2), 0.02, 0.004), Matrix.Rotation(random.uniform(0, 3), 3, 'Z'),
                  "rock", (0.12, 0.12, 0.12), 0.05)                          # cracks

def wall(B, kind="clean", T=I3, off=Vector((0, 0, 0))):
    """A 4 m wall centred on y = 0; T / off rotate and move it (for hallway sides)."""
    P = lambda v: T @ Vector(v) + off
    B.box(P((0, 0, H / 2)), (M4, WT - 0.1, H), T, "metal", C_DARK, 0.2, rust=0.3)                         # frame / backing
    for i in range(2):
        for j in range(2):
            if kind == "damaged" and (i, j) in ((1, 1), (1, 0)):
                continue
            if kind == "window" and j == 1:
                continue
            for s in (-1, 1):
                panel_face(B, P((-1 + 2 * i, s * WT / 2, 1 + 2 * j)), T @ Vector((0, s, 0)), 1.94, 1.94, jit(C_WHITE, 0.05))
    B.box(P((0, 0, 0.1)), (M4, WT + 0.04, 0.2), T, "metal", C_DARK, 0.15)                                 # skirting
    B.box(P((0, 0, H - 0.05)), (M4, WT + 0.04, 0.1), T, "metal", C_DARK, 0.15)
    if kind == "damaged":
        for x in (0.7, 1.3):
            B.box(P((x, 0, 2.0)), (0.08, 0.08, 4.0), T, "metal", C_RUST, 0.2, rust=0.7)                     # exposed studs
        B.box(P((1.2, -0.9, 0.12)), (1.9, 0.05, 1.0), T @ Matrix.Rotation(1.35, 3, 'X') @ Matrix.Rotation(0.3, 3, 'Y'), "panel", C_WHITE, 0.2, rust=0.4)
        for k in range(3):
            vine(B, P((random.uniform(-1.8, 1.8), -WT / 2 - 0.02, H - 0.1)), random.uniform(1.5, 3.2), 520 + k)
        for k in range(3):
            chunk(B, P((1.0 + random.uniform(-0.6, 0.6), -0.6, 0.12)), 0.2, 525 + k)

def build_kit_floor():
    coll = kit("Floor"); B = Builder(); floor(B); finish(B, "Mesh", coll); return coll

def build_kit_floor_cracked():
    coll = kit("FloorCracked"); B = Builder(); floor(B, True); finish(B, "Mesh", coll); return coll

def build_kit_wall():
    coll = kit("Wall"); B = Builder(); wall(B); finish(B, "Mesh", coll); return coll

def build_kit_wall_damaged():
    coll = kit("WallDamaged"); B = Builder(); wall(B, "damaged"); finish(B, "Mesh", coll); return coll

def build_kit_wall_window():
    coll = kit("WallWindow"); B = Builder(); G = Builder()
    wall(B, "window")
    for x in (-2.0 + 0.05, 0, 2.0 - 0.05):
        B.box(Vector((x, 0, 3.0)), (0.1, WT + 0.02, 2.0), I3, "metal", C_DARK, 0.15)
    B.box(Vector((0, 0, 2.05)), (M4, WT + 0.04, 0.1), I3, "metal", C_DARK, 0.15)
    G.box(Vector((-1.0, 0, 3.0)), (1.9, 0.03, 1.9), I3, "glass", (0.6, 0.9, 1.0), 0.02)
    G.box(Vector((1.0, 0, 3.0)), (1.9, 0.03, 1.9), I3, "glass", (0.6, 0.9, 1.0), 0.02)
    finish(B, "Mesh", coll); finish(G, "Glass", coll); return coll

def build_kit_doorway():
    coll = kit("Doorway"); B = Builder()
    for x, w in ((-1.5, 1.0), (1.5, 1.0)):                                   # jambs either side of a 2 m opening
        B.box(Vector((x, 0, H / 2)), (w, WT, H), I3, "metal", C_DARK, 0.2, rust=0.3)
        for s in (-1, 1):
            panel_face(B, Vector((x, s * WT / 2, 2.0)), (0, s, 0), 0.94, 3.9, jit(C_WHITE, 0.05))
    B.box(Vector((0, 0, 3.5)), (2.0, WT, 1.0), I3, "metal", C_DARK, 0.2)                                   # lintel (3 m clear)
    for s in (-1, 1):
        panel_face(B, Vector((0, s * WT / 2, 3.5)), (0, s, 0), 1.94, 0.94, jit(C_WHITE, 0.05))
        hazard(B, Vector((-1.0, s * (WT / 2 + 0.01), 2.9)), (1, 0, 0), (0, 0, 1), 2.0, 0.1, (0, s, 0), pitch=0.1)
    B.box(Vector((1.25, -WT / 2 - 0.02, 2.4)), (0.2, 0.02, 0.2), I3, "glow", C_AMBER, 0.05)               # door lamp
    finish(B, "Mesh", coll); return coll

def hallway(B, broken=False, corner=False):
    floor(B, cracked=broken)
    sides = ((-1, 0), (0, 1)) if corner else ((-1, 0), (1, 0))               # walls: left, and right (straight) or far end (corner)
    for (sx, sy) in sides:
        T = Matrix.Rotation(math.pi / 2, 3, 'Z') if sx else I3
        # wall faces inward: the damaged side (-y of the wall) must face the corridor
        T = T @ Matrix.Rotation(math.pi, 3, "Z") if sx > 0 else T
        wall(B, "damaged" if broken and sx > 0 else "clean", T, Vector((sx * (M4 / 2 - WT / 2), sy * (M4 / 2 - WT / 2), 0)))
    B.box(Vector((0, 0, H + 0.15)), (M4, M4, 0.3), I3, "metal", C_DARK, 0.2, rust=0.3)                     # ceiling
    for x in (-1.0, 1.0):
        if broken and x > 0:
            B.box(Vector((x, 0.5, H - 0.9)), (0.2, 1.6, 0.06), Matrix.Rotation(0.9, 3, 'X'), "lamp", C_LAMP, 0.02)   # light fallen and hanging
        else:
            B.box(Vector((x, 0, H - 0.03)), (0.2, 3.6, 0.06), I3, "lamp", C_LAMP, 0.02)
    if broken:                                                                # a hole in the ceiling with vines through it
        for k in range(4):
            vine(B, Vector((0.8 + random.uniform(-0.5, 0.5), 1.0 + random.uniform(-0.8, 0.8), H)), random.uniform(1.5, 3.2), 540 + k)
        for k in range(5):
            chunk(B, Vector((0.5 + random.uniform(-0.8, 0.8), 1.0 + random.uniform(-0.8, 0.8), 0.15)), 0.25, 545 + k)

def build_kit_hallway():
    coll = kit("Hallway"); B = Builder(); hallway(B); finish(B, "Mesh", coll); return coll

def build_kit_hallway_corner():
    coll = kit("HallwayCorner"); B = Builder(); hallway(B, corner=True); finish(B, "Mesh", coll); return coll

def build_kit_hallway_broken():
    coll = kit("HallwayBroken"); B = Builder(); hallway(B, broken=True); finish(B, "Mesh", coll); return coll

def grate(B, c, sx, sy):
    """Grated deck sx by sy (top at c.z)."""
    B.box(c + Vector((0, 0, -0.06)), (sx, sy, 0.08), I3, "metal", C_DARK, 0.2, rust=0.4)
    for k in range(int(sy / 0.15)):
        B.box(c + Vector((0, -sy / 2 + 0.075 + k * 0.15, -0.005)), (sx - 0.1, 0.04, 0.01), I3, "steel", C_RUST, 0.2, rust=0.6)

def rails(B, a, b, z0=0.0):
    a, b = Vector(a), Vector(b)
    d = b - a; L_ = d.length
    ang = math.atan2(d.y, d.x)
    R = Matrix.Rotation(ang, 3, 'Z')
    for h_ in (0.5, 1.0):
        B.box((a + b) / 2 + Vector((0, 0, z0 + h_)), (L_, 0.05, 0.05), R, "panel", C_YELLOW, 0.2, rust=0.4)
    n = max(2, int(L_ / 1.0) + 1)
    for k in range(n):
        p = a.lerp(b, k / (n - 1))
        B.box(p + Vector((0, 0, z0 + 0.5)), (0.05, 0.05, 1.0), I3, "metal", C_DARK, 0.2)

def build_kit_catwalk():
    coll = kit("Catwalk"); B = Builder()
    grate(B, Vector((0, 0, 0)), 1.6, M4)
    for x in (-0.8, 0.8):
        rails(B, (x, -2, 0), (x, 2, 0))
    B.box(Vector((0, 0, -0.2)), (1.6, 0.12, 0.2), I3, "metal", C_DARK, 0.2)                                # mid beam
    finish(B, "Mesh", coll); return coll

def build_kit_catwalk_corner():
    coll = kit("CatwalkCorner"); B = Builder()
    grate(B, Vector((0, 0, 0)), 1.6, 1.6)
    rails(B, (-0.8, -0.8, 0), (-0.8, 0.8, 0)); rails(B, (-0.8, 0.8, 0), (0.8, 0.8, 0))
    finish(B, "Mesh", coll); return coll

def build_kit_catwalk_stairs():
    coll = kit("CatwalkStairs"); B = Builder()
    n = 10
    for k in range(n):
        y = -2 + (k + 0.5) * M4 / n; z = (k + 1) * 2.0 / n
        B.box(Vector((0, y, z - 0.03)), (1.5, M4 / n - 0.04, 0.06), I3, "steel", C_RUST, 0.2, rust=0.6)
    for x in (-0.78, 0.78):
        B.box(Vector((x, 0, 1.0)), (0.06, M4 * 1.12, 0.25), Matrix.Rotation(math.atan2(2, 4), 3, 'X'), "metal", C_DARK, 0.2)
        a, b = Vector((x, -2, 0)), Vector((x, 2, 2))
        for h_ in (0.5, 1.0):
            d = b - a
            B.box((a + b) / 2 + Vector((0, 0, h_)), (0.05, d.length, 0.05), Matrix.Rotation(math.atan2(2, 4), 3, 'X'), "panel", C_YELLOW, 0.2, rust=0.4)
    finish(B, "Mesh", coll); return coll

def build_kit_catwalk_support():
    coll = kit("CatwalkSupport"); B = Builder()
    B.box(Vector((0, 0, -2.0)), (0.2, 0.2, 4.0), I3, "metal", C_DARK, 0.2, rust=0.5)
    B.box(Vector((0, 0, -0.2)), (1.8, 0.2, 0.2), I3, "metal", C_DARK, 0.2, rust=0.5)
    for s in (-1, 1):
        B.box(Vector((s * 0.4, 0, -0.6)), (0.08, 0.08, 0.9), Matrix.Rotation(s * 0.7, 3, 'Y'), "metal", C_DARK, 0.2)
    B.box(Vector((0, 0, -3.95)), (0.6, 0.6, 0.1), I3, "metal", C_RUST, 0.2, rust=0.7)
    vine(B, Vector((0.1, -0.1, -0.1)), 2.5, 560)
    finish(B, "Mesh", coll); return coll

def build_kit_stairs():
    coll = kit("Stairs"); B = Builder()
    n = 10
    for k in range(n):
        y = -2 + (k + 0.5) * M4 / n; z = (k + 1) * 2.0 / n
        B.box(Vector((0, y, z / 2)), (2.4, M4 / n, z), I3, "rock", jit(C_CONC, 0.05), 0.1)
        B.box(Vector((0, y - M4 / n / 2 + 0.03, z - 0.01)), (2.3, 0.06, 0.02), I3, "panel", C_YELLOW, 0.2, rust=0.4)
    for x in (-1.2, 1.2):
        a, b = Vector((x, -2, 0.2)), Vector((x, 2, 2.2))
        d = b - a
        B.box((a + b) / 2 + Vector((0, 0, 0.9)), (0.05, d.length, 0.05), Matrix.Rotation(math.atan2(2, 4), 3, 'X'), "metal", C_DARK, 0.2)
    moss(B, Vector((0.9, -1.8, 0.2)), 0.3, 570)
    finish(B, "Mesh", coll); return coll

def build_kit_column():
    coll = kit("Column"); B = Builder()
    B.box(Vector((0, 0, 2.0)), (0.8, 0.8, 4.0), I3, "rock", C_CONC, 0.12)
    for z in (0.15, 3.85):
        B.box(Vector((0, 0, z)), (1.0, 1.0, 0.3), I3, "metal", C_DARK, 0.2, rust=0.4)
    B.box(Vector((0, 0, 1.0)), (0.82, 0.82, 0.2), I3, "panel", C_YELLOW, 0.3, rust=0.5)
    finish(B, "Mesh", coll); return coll

def build_kit_railing():
    coll = kit("Railing"); B = Builder()
    rails(B, (-2, 0, 0), (2, 0, 0))
    B.box(Vector((0, 0, 0.05)), (4.0, 0.1, 0.1), I3, "metal", C_DARK, 0.2)
    finish(B, "Mesh", coll); return coll

PIECES = [(globals()[f"build_{n}"], f"{n}.glb") for n in (
    "kit_floor", "kit_floor_cracked", "kit_wall", "kit_wall_damaged", "kit_wall_window", "kit_doorway",
    "kit_hallway", "kit_hallway_corner", "kit_hallway_broken", "kit_catwalk", "kit_catwalk_corner",
    "kit_catwalk_stairs", "kit_catwalk_support", "kit_stairs", "kit_column", "kit_railing")]
ICONS = []

def build_all(do_export=True):
    return build_family(PIECES, OUT_DIR, do_export)

if __name__ == "__main__":
    build_all(do_export=False)
