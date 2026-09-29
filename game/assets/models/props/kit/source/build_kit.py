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

Tiling rules: the pieces are instanced hundreds of times, so they stay lean (one-sided cards for decals, open
backs where a part sits on something). Anything that meets a neighbour at the module edge (slabs, backings,
skirtings, channels, rails, ducts) is sharp-edged or open-ended there and stops exactly on the edge, so runs of
pieces join flush with no overlap to z-fight. Rail posts sit 3 cm in from the ends, so two neighbours make a
doubled post at the joint instead of two coincident ones.
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
C_STRIPE = (0.12, 0.3, 0.42)          # the facility's blue wayfinding band

def kit(name):
    random.seed(sum(map(ord, name)))
    return clear_collection(f"Kit_{name}")

# ---------------------------------------------------------------------------------------- lean parts
def bar(B, p0, p1, w, h, mat="metal", col=C_DARK, bev=0.01, caps=True, up=ZV, var=0.2, rust=0.0):
    """Chamfered rectangular bar from p0 to p1 (w across, h along `up`); caps=False leaves the ends open where
    they butt against a neighbour or bury in something. 16 tris, or 8 with bev=0."""
    p0, p1 = Vector(p0), Vector(p1)
    d = (p1 - p0).normalized()
    s = d.cross(Vector(up)); s = s.normalized() if s.length > 1e-4 else d.cross(V(1, 0, 0)).normalized()
    u = s.cross(d)
    b = min(bev, 0.3 * w, 0.3 * h)
    sec = ([(-w / 2, -h / 2), (w / 2, -h / 2), (w / 2, h / 2), (-w / 2, h / 2)] if b <= 0 else
           [(-w / 2 + b, -h / 2), (w / 2 - b, -h / 2), (w / 2, -h / 2 + b), (w / 2, h / 2 - b),
            (w / 2 - b, h / 2), (-w / 2 + b, h / 2), (-w / 2, h / 2 - b), (-w / 2, -h / 2 + b)])
    rings_ = [[p + s * x + u * y for x, y in sec] for p in (p0, p1)]
    return B.tube_rings(rings_, mat, col, var, rust, cap=caps, smooth=False)

def rivets(B, c, n, w, h, col=(0.3, 0.3, 0.29), mat="panel", r=0.018):
    """Four rivet dots near the corners of a w x h panel face (cards)."""
    R = facing_basis(n)
    for sx in (-1, 1):
        for sy in (-1, 1):
            decal(B, Vector(c) + R @ V(sx * (w / 2 - 0.06), sy * (h / 2 - 0.06), 0), n, r, r, col, mat, off=0.002)

# ---------------------------------------------------------------------------------------- floor
def floor(B, cracked=False):
    flat(B, Vector((0, 0, -0.15)), (M4, M4, 0.3), I3, "rock", C_CONC_D, 0.12)                               # slab: sharp, butts flush
    for i in range(4):
        for j in range(4):
            if cracked and (i, j) in ((3, 0), (2, 0), (3, 1)):
                continue                                                     # broken-out tiles
            c = Vector((-1.5 + i, -1.5 + j, 0.0075))
            n = ZV
            if cracked and (i + j) % 3 == 0:                                 # lifted, tilted tiles
                n = Matrix.Rotation(random.uniform(-0.04, 0.04), 3, 'X') @ Matrix.Rotation(random.uniform(-0.03, 0.03), 3, 'Y') @ ZV
                c += V(0, 0, 0.01)
            plate(B, c, n, 0.96, 0.96, 0.015, "rock", jit(C_CONC, 0.06), 0.1, bev=0.008, seam=False)
    plate(B, V(1.5, 1.5, 0.017), ZV, 0.3, 0.3, 0.004, "rock", C_DARK, 0.2, rust=0.6, seam=False)          # floor drain
    for k in range(4):
        decal(B, V(1.5, 1.39 + k * 0.075, 0.02), ZV, 0.22, 0.03, (0.02, 0.02, 0.02), "rock", off=0.0)
    blob(B, V(1.5, 1.5, 0.0155), 0.35, (0.26, 0.25, 0.22), 8, seed=1.0, off=0.001)                        # grime ring round it
    if cracked:
        blob(B, V(1.35, -1.25, 0.002), 0.75, (0.16, 0.16, 0.13), 10, seed=3.0)                            # wet dirt in the hole
        for k in range(3):
            chunk(B, Vector((1.3 + random.uniform(-0.3, 0.3), -1.3 + random.uniform(-0.4, 0.4), 0.05)), 0.18, 500 + k)
        moss(B, Vector((1.4, -1.2, -0.05)), 0.6, 505)
        fern(B, Vector((1.35, -1.35, -0.1)), 506, 0.7)
        for k in range(4):                                                   # cracks running out of the hole
            crack(B, V(0.9 - k * 0.3, -1.0 + k * 0.5, 0.012), ZV, random.uniform(0.6, 1.2), 510 + k, w=0.018, heading=math.pi * (0.6 + 0.15 * k))
        for k in range(4):
            grass(B, V(random.choice((-1.0, 0.0, 1.0)), random.uniform(-1.6, 1.6), 0.0), 515 + k, 4, 0.25)   # weeds in the joints

def build_kit_floor():
    coll = kit("Floor"); B = Builder(); floor(B); done(B, "Mesh", coll, h=0); return coll

def build_kit_floor_cracked():
    coll = kit("FloorCracked"); B = Builder(); floor(B, True); done(B, "Mesh", coll, h=0); return coll

# ---------------------------------------------------------------------------------------- walls
def wall(B, kind="clean", T=I3, off=Vector((0, 0, 0)), flush_back=False):
    """A 4 m wall centred on y = 0; T / off rotate and move it (for hallway sides). The -y face is the "front"
    (the side a hallway looks at). flush_back keeps everything on the +y side inside the 0.3 m thickness (a
    hallway's outer face sits exactly on the module edge)."""
    P = lambda v: T @ Vector(v) + off
    N = lambda v: T @ Vector(v)
    flat(B, P((0, 0, H / 2)), (M4, WT - 0.1, H), T, "metal", C_DARK, 0.2, rust=0.3)                       # frame / backing
    for s in (-1, 1):
        if flush_back and s > 0:
            t, yc = 0.05, WT / 2 - 0.025                                     # outer panels stop on the module edge
        else:
            t, yc = 0.0675, WT / 2 - 0.0662                                  # panel faces at +-0.1675 as before
        for i in range(2):
            for j in range(2):
                if kind == "damaged" and s < 0 and (i, j) in ((1, 1), (1, 0)):
                    continue
                if kind == "window" and j == 1:
                    continue
                c = P((-1 + 2 * i, s * yc, 1 + 2 * j))
                plate(B, c, N((0, s, 0)), 1.94, 1.94, t, "panel", jit(C_WHITE, 0.05), 0.3, rust=0.15 if j == 0 else 0.05)
                rivets(B, c + N((0, s * t / 2, 0)), N((0, s, 0)), 1.94 - 0.024, 1.94 - 0.024)
        # skirting and top trim: sharp ends (they continue into the next wall)
        y0, y1 = (-WT / 2 - 0.02, WT / 2) if flush_back else (-WT / 2 - 0.02, WT / 2 + 0.02)
        if s < 0:
            for zc, hh in ((0.1, 0.2), (H - 0.05, 0.1)):
                flat(B, P((0, (y0 + y1) / 2, zc)), (M4, y1 - y0, hh), T, "metal", C_DARK, 0.15, rust=0.3)
    if kind != "window":
        decal(B, P((0, -WT / 2 - 0.0675 + 0.0662 - 0.002, 1.25)), N((0, -1, 0)), M4, 0.08, C_STRIPE, "panel", off=0.0)   # wayfinding band
    for k, x in enumerate((-1.02, 0.98) if kind != "window" else ()):     # grime run-off from the top trim
        streak(B, P((x + random.uniform(-0.3, 0.3), -0.1675, H - 0.1)), N((0, -1, 0)), random.uniform(0.6, 1.1), 0.14, (0.5, 0.48, 0.42), C_WHITE, "panel")
    if kind == "damaged":
        for x in (0.7, 1.3):
            bar(B, P((x, -0.12, 0.2)), P((x, -0.12, H - 0.1)), 0.08, 0.08, "metal", C_RUST, 0.01, caps=False, up=N((0, 1, 0)), rust=0.7)   # exposed studs
        for z in (1.0, 2.6):
            flat(B, P((1.0, -0.115, z)), (1.9, 0.02, 0.35), T, "rock", (0.72, 0.62, 0.3), 0.3)             # torn insulation
        bent = T @ Matrix.Rotation(1.35, 3, 'X') @ Matrix.Rotation(0.3, 3, 'Y')
        B.box(P((1.0, -0.9, 0.12)), (1.8, 0.05, 1.0), bent, "panel", C_WHITE, 0.2, rust=0.4)                # the fallen panel
        plate(B, P((0.62, -0.2, 1.05)), N((-0.25, -1, 0.1)), 0.9, 1.94, 0.04, "panel", C_WHITE, 0.3, rust=0.35)   # one hanging askew
        B.pipe([P((1.9, -0.13, 3.6)), P((1.5, -0.2, 3.1)), P((1.35, -0.25, 2.2)), P((1.5, -0.35, 1.7))], 0.02, 4, "rubber", C_BLACK)   # loose cable
        for k in range(3):
            vine(B, P((random.uniform(-1.8, 1.8), -WT / 2 - 0.02, H - 0.1)), random.uniform(1.5, 3.2), 520 + k)
        for k in range(3):
            chunk(B, P((1.0 + random.uniform(-0.6, 0.6), -0.6, 0.12)), 0.2, 525 + k)
        crack(B, P((-0.3, -0.17, 2.5)), N((0, -1, 0)), 1.0, 528, mat="panel")

def build_kit_wall():
    coll = kit("Wall"); B = Builder(); wall(B); done(B, "Mesh", coll, h=0.9, k=0.4); return coll

def build_kit_wall_damaged():
    coll = kit("WallDamaged"); B = Builder(); wall(B, "damaged"); done(B, "Mesh", coll, h=0.9, k=0.45); return coll

def build_kit_wall_window():
    coll = kit("WallWindow"); B = Builder(); G = Builder()
    wall(B, "window")
    for x in (-2.0 + 0.05, 0, 2.0 - 0.05):
        B.box(Vector((x, 0, 3.0)), (0.1, WT + 0.02, 2.0), I3, "metal", C_DARK, 0.15)
    flat(B, Vector((0, 0, 2.05)), (M4, WT + 0.04, 0.1), I3, "metal", C_DARK, 0.15)                          # sill (butts the next wall)
    for s in (-1, 1):
        flat(B, V(0, s * 0.12, 3.97), (M4, 0.06, 0.06), I3, "metal", C_DARK, 0.15)                          # glazing beads
    G.box(Vector((-1.0, 0, 3.0)), (1.9, 0.03, 1.9), I3, "glass", C_GLASS, 0.02, bevel=0)
    G.box(Vector((1.0, 0, 3.0)), (1.9, 0.03, 1.9), I3, "glass", C_GLASS, 0.02, bevel=0)
    for k in range(3):                                                       # a cracked pane
        crack(B, V(1.0, -0.016, 3.1), (0, -1, 0), 0.7, 530 + k, w=0.006, col=(0.85, 0.9, 0.9), steps=4, mat="panel")
    done(B, "Mesh", coll, h=0.9, k=0.4); done(G, "Glass", coll, h=0); return coll

def build_kit_doorway():
    coll = kit("Doorway"); B = Builder()
    for x, w in ((-1.5, 1.0), (1.5, 1.0)):                                   # jambs either side of a 2 m opening
        flat(B, Vector((x, 0, H / 2)), (w, WT - 0.1, H), I3, "metal", C_DARK, 0.2, rust=0.3)
        for s in (-1, 1):
            plate(B, Vector((x, s * (WT / 2 - 0.0662), 2.0)), (0, s, 0), 0.94, 3.9, 0.0675, "panel", jit(C_WHITE, 0.05), 0.3, rust=0.15)
            flat(B, V(x, s * (WT / 2 + 0.01), 0.1), (w, 0.02, 0.2), I3, "metal", C_DARK, 0.15)             # skirting
    flat(B, Vector((0, 0, 3.5)), (2.0, WT - 0.1, 1.0), I3, "metal", C_DARK, 0.2)                           # lintel (3 m clear)
    for s in (-1, 1):
        plate(B, Vector((0, s * (WT / 2 - 0.0662), 3.5)), (0, s, 0), 1.94, 0.94, 0.0675, "panel", jit(C_WHITE, 0.05), 0.3)
        hazard(B, Vector((-1.0, s * (WT / 2 + 0.035), 2.9)), (1, 0, 0), (0, 0, 1), 2.0, 0.1, (0, s, 0), pitch=0.1)
        decal(B, V(0, s * 0.169, 3.62), (0, s, 0), 0.7, 0.18, (0.1, 0.1, 0.1), "panel", off=0.001)          # sign plate
        decal(B, V(0, s * 0.17, 3.62), (0, s, 0), 0.5, 0.08, (0.8, 0.8, 0.76), "panel", off=0.001)
    for sx in (-1, 1):                                                       # door frame: a chamfered casing round the opening
        bar(B, V(sx * 1.04, 0, 0.0), V(sx * 1.04, 0, 3.0), 0.08, WT + 0.06, "metal", C_GREY, 0.015, caps=False, up=V(0, 1, 0), rust=0.4)
    bar(B, V(-1.08, 0, 3.04), V(1.08, 0, 3.04), 0.08, WT + 0.06, "metal", C_GREY, 0.015, caps=True, up=V(0, 1, 0), rust=0.4)
    flat(B, V(0, 0, 0.005), (2.0, WT + 0.06, 0.01), I3, "metal", C_STEEL, 0.2, rust=0.4)                   # threshold plate
    B.box(Vector((1.25, -WT / 2 - 0.05, 2.4)), (0.2, 0.04, 0.2), I3, "glow", C_AMBER, 0.05, bevel=0)       # door lamp
    B.box(V(1.25, -WT / 2 - 0.035, 1.3), (0.14, 0.03, 0.2), I3, "metal", C_GREY, 0.2)                      # keypad
    decal(B, V(1.25, -WT / 2 - 0.05, 1.34), (0, -1, 0), 0.08, 0.05, (0.3, 1.0, 0.4), "glow", off=0.001)
    streak(B, V(-1.5, -0.1675, 2.9), (0, -1, 0), 0.9, 0.08, C_RUST, C_WHITE, "panel")
    done(B, "Mesh", coll, h=0.9, k=0.4); return coll

# ---------------------------------------------------------------------------------------- hallways
def hallway(B, broken=False, corner=False):
    floor(B, cracked=broken)
    sides = ((-1, 0), (0, 1)) if corner else ((-1, 0), (1, 0))               # walls: left, and right (straight) or far end (corner)
    for (sx, sy) in sides:
        T = Matrix.Rotation(math.pi / 2, 3, 'Z') if sx else I3
        # wall faces inward: the damaged side (-y of the wall) must face the corridor
        T = T @ Matrix.Rotation(math.pi, 3, "Z") if sx > 0 else T
        wall(B, "damaged" if broken and sx > 0 else "clean", T, Vector((sx * (M4 / 2 - WT / 2), sy * (M4 / 2 - WT / 2), 0)), flush_back=True)
    if broken:                                                               # a hole torn in the ceiling
        hx0, hx1, hy0, hy1 = 0.2, 1.4, 0.2, 1.8
        for (x0, x1, y0, y1) in ((-2, hx0, -2, 2), (hx1, 2, -2, 2), (hx0, hx1, -2, hy0), (hx0, hx1, hy1, 2)):
            flat(B, V((x0 + x1) / 2, (y0 + y1) / 2, H + 0.15), (x1 - x0, y1 - y0, 0.3), I3, "metal", C_DARK, 0.2, rust=0.3)
        for k in range(5):                                                   # bent deck and rebar round the hole
            rebar(B, V(random.uniform(hx0, hx1), random.choice((hy0, hy1)), H + 0.05), (random.uniform(-0.3, 0.3), 0, -1), 0.5)
    else:
        flat(B, Vector((0, 0, H + 0.15)), (M4, M4, 0.3), I3, "metal", C_DARK, 0.2, rust=0.3)                 # ceiling
    for x in (-1.0, 1.0):                                                    # light fittings: housing and diffuser
        if broken and x > 0:
            R = Matrix.Rotation(0.9, 3, 'X')
            B.box(Vector((x, 0.5, H - 0.9)), (0.3, 1.6, 0.08), R, "metal", C_GREY, 0.2, rust=0.5)
            B.box(Vector((x, 0.5, H - 0.9)) + R @ V(0, 0, -0.045), (0.2, 1.5, 0.02), R, "lamp", C_LAMP, 0.02, bevel=0)   # light fallen and hanging
            B.pipe([V(x, 1.3, H - 0.03), V(x, 1.2, H - 0.4), V(x + 0.05, 1.15, H - 0.55)], 0.012, 4, "rubber", C_BLACK)
        else:
            flat(B, Vector((x, 0, H - 0.04)), (0.3, 3.7, 0.08), I3, "metal", C_GREY, 0.2, rust=0.3)
            flat(B, Vector((x, 0, H - 0.085)), (0.2, 3.6, 0.01), I3, "lamp", C_LAMP, 0.02)
    # services along the walls: a duct on the left (turning with a corner), a cable tray on the right
    duct = [V(-1.55, -2, 3.62), V(-1.55, 2, 3.62)] if not corner else [V(-1.55, -2, 3.62), V(-1.55, 1.55, 3.62), V(2, 1.55, 3.62)]
    for a, b in zip(duct, duct[1:]):
        B.cyl(a, b, 0.12, 8, "metal", C_GREY, 0.2, rust=0.4, cap=False)
    if corner:
        B.cyl(V(-1.55, 1.55, 3.62) + V(0, -0.14, 0), V(-1.55, 1.55, 3.62) + V(0.14, 0, 0), 0.13, 8, "metal", C_GREY, 0.2, rust=0.4)   # elbow collar
    for p in ([V(-1.55, y, 3.62) for y in (-1.0, 1.0)] if not corner else [V(-1.55, -1.0, 3.62), V(0.5, 1.55, 3.62)]):
        flat(B, p + V(0, 0, 0.19), (0.05, 0.05, 0.26), I3, "metal", C_DARK, 0.2)                           # hangers
    if not corner:
        if broken:                                                           # the tray has come down at one end
            R = Matrix.Rotation(-0.25, 3, 'X')
            flat(B, V(1.5, -0.2, 3.3), (0.3, 3.6, 0.03), R, "metal", C_GREY, 0.2, rust=0.6)
            for dx in (-0.06, 0.06):
                B.cyl(V(1.5 + dx, -2.0, 3.72), V(1.5 + dx, -1.95, 3.72), 0.03, 5, "rubber", C_BLACK, cap=False)
        else:
            flat(B, V(1.5, 0, 3.7), (0.3, M4, 0.03), I3, "metal", C_GREY, 0.2, rust=0.4)
            for dx, col in ((-0.06, C_BLACK), (0.06, (0.5, 0.08, 0.05))):
                B.cyl(V(1.5 + dx, -2, 3.745), V(1.5 + dx, 2, 3.745), 0.03, 5, "rubber", col, cap=False)
            for y in (-1.0, 1.0):
                flat(B, V(1.5, y, 3.85), (0.05, 0.05, 0.3), I3, "metal", C_DARK, 0.2)
        B.box(V(-1.69, 1.2, 2.6), (0.12, 0.3, 0.16), I3, "metal", C_DARK, 0.2)                             # emergency lamp
        decal(B, V(-1.63, 1.2, 2.6), V(1, 0, 0), 0.24, 0.08, C_LAMP, "lamp", off=0.001)
    if broken:                                                               # vines through the hole
        for k in range(4):
            vine(B, Vector((0.8 + random.uniform(-0.5, 0.5), 1.0 + random.uniform(-0.7, 0.7), H + 0.1)), random.uniform(1.5, 3.2), 540 + k)
        for k in range(5):
            chunk(B, Vector((0.5 + random.uniform(-0.8, 0.8), 1.0 + random.uniform(-0.8, 0.8), 0.15)), 0.25, 545 + k)
        blob(B, V(0.8, 1.0, 0.02), 0.6, (0.1, 0.12, 0.1), 9, seed=4.0, off=0.0)                            # puddle under the hole

def build_kit_hallway():
    coll = kit("Hallway"); B = Builder(); hallway(B); done(B, "Mesh", coll, h=0.9, k=0.4); return coll

def build_kit_hallway_corner():
    coll = kit("HallwayCorner"); B = Builder(); hallway(B, corner=True); done(B, "Mesh", coll, h=0.9, k=0.4); return coll

def build_kit_hallway_broken():
    coll = kit("HallwayBroken"); B = Builder(); hallway(B, broken=True); done(B, "Mesh", coll, h=0.9, k=0.5); return coll

# ---------------------------------------------------------------------------------------- catwalks
def grate(B, c, sx, sy, pitch=0.14):
    """Grated deck sx by sy (top at c.z): open-ended edge channels along y, cross slats you can see through."""
    for s in (-1, 1):
        bar(B, c + V(s * (sx / 2 - 0.03), -sy / 2, -0.06), c + V(s * (sx / 2 - 0.03), sy / 2, -0.06), 0.06, 0.12, "metal", C_DARK, 0.012, caps=False, rust=0.4)
    n = int(round(sy / pitch))
    for k in range(n):
        y = -sy / 2 + (k + 0.5) * sy / n
        bar(B, c + V(-sx / 2 + 0.06, y, -0.02), c + V(sx / 2 - 0.06, y, -0.02), 0.07, 0.04, "metal", C_RUST, 0.0, caps=False, rust=0.6)

def rails(B, a, b, z0=0.0, inset=0.03, toe=True):
    """Handrail and knee rail from a to b (chamfered, open-ended), posts inset from the ends, a toe plate."""
    a, b = Vector(a), Vector(b)
    d = b - a; L_ = d.length; t = d / L_
    for h_ in (0.5, 1.0):
        bar(B, a + V(0, 0, z0 + h_), b + V(0, 0, z0 + h_), 0.05, 0.05, "panel", C_YELLOW, 0.012, caps=False, rust=0.4)
    n = max(2, int(L_ / 1.3) + 1)
    for k in range(n):
        p = (a + t * inset).lerp(b - t * inset, k / (n - 1))
        flat(B, p + Vector((0, 0, z0 + 0.52)), (0.05, 0.05, 1.04), Matrix.Rotation(math.atan2(t.y, t.x), 3, 'Z'), "metal", C_DARK, 0.2)
    if toe:
        flat(B, (a + b) / 2 + V(0, 0, z0 + 0.06), (L_, 0.012, 0.12), Matrix.Rotation(math.atan2(t.y, t.x), 3, 'Z'), "metal", C_YELLOW, 0.3, rust=0.6)

def build_kit_catwalk():
    coll = kit("Catwalk"); B = Builder()
    grate(B, Vector((0, 0, 0)), 1.6, M4)
    for x in (-0.8, 0.8):
        rails(B, (x, -2, 0), (x, 2, 0))
    flat(B, Vector((0, 0, -0.2)), (1.6, 0.12, 0.2), I3, "metal", C_DARK, 0.2)                                # mid beam
    for x in (-0.6, 0.6):
        streak(B, V(x, -0.061, -0.1), (0, -1, 0), 0.18, 0.06, C_RUST, C_DARK, "metal")
    done(B, "Mesh", coll, h=0); return coll

def build_kit_catwalk_corner():
    coll = kit("CatwalkCorner"); B = Builder()
    grate(B, Vector((0, 0, 0)), 1.6, 1.6)
    rails(B, (-0.8, -0.8, 0), (-0.8, 0.8, 0)); rails(B, (-0.8, 0.8, 0), (0.8, 0.8, 0))
    flat(B, V(0, 0, -0.2), (1.6, 0.12, 0.2), I3, "metal", C_DARK, 0.2)
    done(B, "Mesh", coll, h=0); return coll

def build_kit_catwalk_stairs():
    coll = kit("CatwalkStairs"); B = Builder()
    n = 10
    for k in range(n):
        y = -2 + (k + 0.5) * M4 / n; z = (k + 1) * 2.0 / n
        flat(B, Vector((0, y, z - 0.03)), (1.5, M4 / n - 0.04, 0.06), I3, "metal", C_RUST, 0.2, rust=0.6)    # tread
        decal(B, V(0, y - M4 / n / 2 + 0.05, z), ZV, 1.46, 0.035, C_YELLOW, "metal", off=0.001)             # nosing paint
    for x in (-0.78, 0.78):
        bar(B, V(x, -2.05, 0.02), V(x, 2.05, 2.07), 0.06, 0.25, "metal", C_DARK, 0.012, caps=True)       # stringers
        a, b = Vector((x, -2, 0)), Vector((x, 2, 2))
        for h_ in (0.5, 1.0):
            bar(B, a + V(0, 0, h_), b + V(0, 0, h_), 0.05, 0.05, "panel", C_YELLOW, 0.012, caps=False, rust=0.4)
        for t in (0.03, 0.5, 0.97):                                          # posts (were missing: the rails floated)
            p = a.lerp(b, t)
            flat(B, p + V(0, 0, 0.5), (0.05, 0.05, 1.0), I3, "metal", C_DARK, 0.2)
    done(B, "Mesh", coll, h=0); return coll

def build_kit_catwalk_support():
    coll = kit("CatwalkSupport"); B = Builder()
    for s in (-1, 1):                                                        # I-section post
        flat(B, V(s * 0.09, 0, -2.0), (0.02, 0.2, 3.8), I3, "metal", C_DARK, 0.2, rust=0.5)
    flat(B, V(0, 0, -2.0), (0.16, 0.02, 3.8), I3, "metal", C_DARK, 0.2, rust=0.5)
    for s in (-1, 1):                                                        # and cross head
        flat(B, V(0, s * 0.09, -0.2), (1.8, 0.02, 0.2), I3, "metal", C_DARK, 0.2, rust=0.5)
    flat(B, V(0, 0, -0.2), (1.8, 0.16, 0.02), I3, "metal", C_DARK, 0.2, rust=0.5)
    flat(B, V(0, 0, -0.105), (0.24, 0.24, 0.01), I3, "metal", C_DARK, 0.2, rust=0.5)                        # cap plate
    for s in (-1, 1):
        B.box(Vector((s * 0.4, 0, -0.6)), (0.08, 0.08, 0.9), Matrix.Rotation(s * 0.7, 3, 'Y'), "metal", C_DARK, 0.2)
    B.box(Vector((0, 0, -3.95)), (0.6, 0.6, 0.1), I3, "metal", C_RUST, 0.2, rust=0.7)
    for sx, sy in ((-1, -1), (1, -1), (1, 1), (-1, 1)):
        stud(B, V(sx * 0.22, sy * 0.22, -3.9), ZV, 0.025, 0.03, C_DARK, "metal", 6)
    streak(B, V(0.0, -0.101, -0.3), (0, -1, 0), 1.2, 0.05, C_RUST, C_DARK, "metal")
    vine(B, Vector((0.1, -0.1, -0.1)), 2.5, 560)
    done(B, "Mesh", coll, h=0.6, k=0.4, z0=-4.0); return coll

# ---------------------------------------------------------------------------------------- stairs, column, railing
def stair_block(B, n, w, run, rise, mat, col):
    """One solid concrete flight: a sawtooth profile extruded across x (no bottom face)."""
    prof = [(-run / 2, 0.0)]
    for k in range(n):
        y0 = -run / 2 + k * run / n
        prof += [(y0, (k + 1) * rise / n), (y0 + run / n, (k + 1) * rise / n)]
    prof.append((run / 2, 0.0))
    bm = B.bm
    L_ = [bm.verts.new(V(-w / 2, y, z)) for y, z in prof]
    R_ = [bm.verts.new(V(w / 2, y, z)) for y, z in prof]
    fs = [B.quad(L_[::-1], mat), B.quad(R_, mat)]
    for i in range(len(prof) - 1):
        fs.append(B.quad([L_[i], L_[i + 1], R_[i + 1], R_[i]], mat))
    bmesh.ops.recalc_face_normals(bm, faces=fs)
    for f in fs:
        f.smooth = False
    B.paint(fs, col, 0.1)
    return fs

def build_kit_stairs():
    coll = kit("Stairs"); B = Builder()
    n = 10
    stair_block(B, n, 2.4, M4, 2.0, "rock", jit(C_CONC, 0.05))
    for k in range(n):
        y = -2 + k * M4 / n; z = (k + 1) * 2.0 / n
        decal(B, V(0, y + 0.03, z), ZV, 2.3, 0.06, C_YELLOW, "rock", off=0.002)                           # nosing paint
        decal(B, V(0, y + 0.22, z), ZV, 1.0, 0.25, (0.42, 0.41, 0.38), "rock", off=0.001)                 # worn tread
    for x in (-1.2, 1.2):                                                    # handrails, now on posts
        a, b = Vector((x, -2, 0.2)), Vector((x, 2, 2.2))
        bar(B, a + V(0, 0, 0.9), b + V(0, 0, 0.9), 0.05, 0.05, "metal", C_DARK, 0.012, caps=True)
        for t in (0.03, 0.5, 0.97):
            p = a.lerp(b, t)
            flat(B, p + V(0, 0, 0.45), (0.05, 0.05, 0.9), I3, "metal", C_DARK, 0.2)
            flat(B, p + V(0, 0, 0.005), (0.12, 0.12, 0.01), I3, "metal", C_DARK, 0.2)
    crack(B, V(1.201, -1.2, 0.5), V(1, 0, 0), 0.8, 571, heading=math.pi * 0.3)
    crack(B, V(-1.201, 0.6, 0.6), V(-1, 0, 0), 0.7, 572, heading=math.pi * 0.2)
    moss(B, Vector((0.9, -1.8, 0.2)), 0.3, 570)
    done(B, "Mesh", coll, h=0.5, k=0.35); return coll

def build_kit_column():
    coll = kit("Column"); B = Builder()
    bar(B, V(0, 0, 0.3), V(0, 0, 3.7), 0.8, 0.8, "rock", C_CONC, 0.04, caps=False, up=V(0, 1, 0))       # chamfered shaft, ends buried
    for z in (0.15, 3.85):
        flat(B, Vector((0, 0, z)), (1.0, 1.0, 0.3), I3, "metal", C_DARK, 0.2, rust=0.4)
    flat(B, Vector((0, 0, 1.0)), (0.82, 0.82, 0.2), I3, "panel", C_YELLOW, 0.3, rust=0.5)
    for sx, sy in ((-1, -1), (1, -1), (1, 1), (-1, 1)):
        stud(B, V(sx * 0.43, sy * 0.43, 0.3), ZV, 0.025, 0.025, C_STEEL, "metal", 4)
    streak(B, V(0.15, -0.4, 3.7), (0, -1, 0), 1.3, 0.1, C_STAIN, C_CONC)
    crack(B, V(0.4, 0.1, 2.3), V(1, 0, 0), 0.7, 575, heading=-math.pi / 2)
    done(B, "Mesh", coll, h=0.8, k=0.4); return coll

def build_kit_railing():
    coll = kit("Railing"); B = Builder()
    rails(B, (-2, 0, 0), (2, 0, 0), toe=False)
    flat(B, Vector((0, 0, 0.05)), (4.0, 0.1, 0.1), I3, "metal", C_DARK, 0.2)
    done(B, "Mesh", coll, h=0); return coll

PIECES = [(globals()[f"build_{n}"], f"{n}.glb") for n in (
    "kit_floor", "kit_floor_cracked", "kit_wall", "kit_wall_damaged", "kit_wall_window", "kit_doorway",
    "kit_hallway", "kit_hallway_corner", "kit_hallway_broken", "kit_catwalk", "kit_catwalk_corner",
    "kit_catwalk_stairs", "kit_catwalk_support", "kit_stairs", "kit_column", "kit_railing")]
ICONS = []

def build_all(do_export=True):
    return build_family(PIECES, OUT_DIR, do_export)

if __name__ == "__main__":
    build_all(do_export=False)
