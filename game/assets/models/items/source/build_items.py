"""
Resource and product item models (world items: the physical pieces that ride the belts). Origin at the item's
centre of mass; Blender Z is Godot Y. Each model matches its collider in tools/items/gen_items.py:

  box (x, y, z)      -> the model spans Blender (x, z, y) around the origin
  sphere r           -> a lump of radius ~r
  capsule r, h       -> an upright cylinder / crystal of radius r and total height h (Godot Y = Blender Z)

Metals: iron / copper ingots, rods and plates; scrap balls and scrap ingots.
Base resources: coal, salt, floatstone, frost crystal, lodestone, quartz, latex resin, sulfur, quicksilver,
voltaic crystal. Products: coke briquette, glass pane, quartz shards, rubber ball, blast charge, battery cell,
magnet core, aerogel tile.
"""
import bpy, bmesh, math, random, os
from mathutils import Vector, Matrix

_HERE = (os.path.dirname(os.path.abspath(__file__)) if "__file__" in globals()
         else r"C:\Users\steph\OneDrive\Documents\godot\projects\gmp-framework\game\assets\models\items\source")
_LIB = os.path.normpath(os.path.join(_HERE, "..", "..", "shared", "salvage_lib.py"))
_S = {"__name__": "salvage_lib", "__file__": _LIB}
exec(compile(open(_LIB, encoding="utf-8").read(), _LIB, "exec"), _S)
_SKIP = {"HERE", "OUT_DIR", "PIECES", "build_all", "ICONS", "export", "CONV"}
globals().update({k: v for k, v in _S.items() if not k.startswith("__") and k not in _SKIP})
HERE = _HERE
OUT_DIR = os.path.dirname(HERE)

C_IRON = (0.5, 0.52, 0.56)
C_IRON_DARK = (0.3, 0.31, 0.34)
C_COAL = (0.05, 0.05, 0.055)
C_SALT = (0.9, 0.86, 0.84)
C_PUMICE = (0.78, 0.74, 0.62)
C_FROST = (0.72, 0.9, 1.0)
C_LODE = (0.14, 0.13, 0.15)
C_QUARTZ = (0.88, 0.9, 0.95)
C_LATEX = (0.85, 0.55, 0.12)
C_SULFUR = (0.95, 0.82, 0.12)
C_MERCURY = (0.8, 0.82, 0.86)
C_VOLTAIC = (0.45, 0.25, 0.95)
C_RUBBER_RED = (0.75, 0.08, 0.06)

def jitter(c, k=0.15):
    f = 1 + random.uniform(-k, k)
    return [min(1.0, ci * f) for ci in c]

def item(name, fn):
    random.seed(sum(map(ord, name)))
    coll = clear_collection(f"Item_{name}")
    B = Builder()
    fn(B, coll)
    if B.bm.faces:
        finish(B, "Mesh", coll)
    return coll

# ---------------------------------------------------------------------------- metals
def ingot(B, mat, col, mottle=None):
    """0.8 x 0.4 x 0.3 trapezoid ingot (Godot box 0.8, 0.3, 0.4)."""
    B.tube_rings([quad_ring(0, 0, -0.15, 0.4, 0.2), quad_ring(0, 0, 0.15, 0.34, 0.15)], mat, col, 0.12, 0.0, cap=True, smooth=False)
    B.box(Vector((0, 0, 0.152)), (0.26, 0.1, 0.006), I3, mat, [c * 0.7 for c in col], 0.05)            # foundry stamp
    for k in range(3):
        B.box(Vector((-0.07 + k * 0.07, 0, 0.157)), (0.03, 0.05, 0.006), I3, mat, [c * 0.5 for c in col], 0.05)
    if mottle:
        for k in range(14):                                                  # scrap: melted-in patches of other metals
            p = Vector((random.uniform(-0.3, 0.3), random.uniform(-0.14, 0.14), 0.151))
            B.box(p, (random.uniform(0.04, 0.12), random.uniform(0.03, 0.08), 0.006), Matrix.Rotation(random.uniform(0, 3), 3, 'Z'),
                  random.choice(("copper", "steel", "metal")), jitter(random.choice(mottle)), 0.1)

def rod(B, mat, col):
    """Round bar, radius 0.1, 1.2 long, upright (Godot capsule r 0.1, h 1.2)."""
    B.cyl(Vector((0, 0, -0.56)), Vector((0, 0, 0.56)), 0.1, 16, mat, col, 0.08)
    for z in (-0.6, 0.56):                                                   # chamfered ends
        B.cyl(Vector((0, 0, z)), Vector((0, 0, z + 0.04)), 0.085, 16, mat, [c * 0.85 for c in col], 0.05)
    for k in range(5):                                                       # rolling marks
        ring(B, Vector((0, 0, -0.4 + k * 0.2)), (0, 0, 1), 0.098, 0.103, 0.01, 16, mat, [c * 0.8 for c in col], 0.05)

def plate(B, mat, col):
    """0.8 x 0.8 x 0.08 checker plate (Godot box 0.8, 0.08, 0.8)."""
    B.box(Vector((0, 0, -0.01)), (0.8, 0.8, 0.06), I3, mat, col, 0.1)
    for i in range(6):
        for j in range(6):
            a = 0.6 if (i + j) % 2 else -0.6
            B.box(Vector((-0.3 + i * 0.12, -0.3 + j * 0.12, 0.025)), (0.08, 0.02, 0.012), Matrix.Rotation(a, 3, 'Z'), mat, [c * 0.9 for c in col], 0.05)

def scrap_ball(B, coll):
    """Crushed bale of scrap, radius ~0.36."""
    cols = [(0.45, 0.46, 0.5), (0.35, 0.18, 0.08), C_COPPER, (0.25, 0.26, 0.28), (0.55, 0.5, 0.4)]
    rock(B, Vector((0, 0, 0)), 0.32, 7.0, 2, 0.35, "metal", lambda p, n: jitter(random.choice(cols), 0.2), (1, 1, 1))
    for k in range(12):                                                      # bent strips poking out of the bale
        d = Vector((random.uniform(-1, 1), random.uniform(-1, 1), random.uniform(-1, 1))).normalized()
        R = rot_to(d)
        B.box(d * 0.32, (0.03, random.uniform(0.08, 0.14), random.uniform(0.12, 0.2)), R @ Matrix.Rotation(random.uniform(0, 3), 3, 'Z'),
              random.choice(("steel", "copper", "metal")), jitter(random.choice(cols)), 0.1, rust=0.4)

# ---------------------------------------------------------------------------- base resources
def coal(B, coll):
    rock(B, Vector((0, 0, 0)), 0.32, 3.0, 1, 0.4, "gem", lambda p, n: jitter(C_COAL, 0.4), (1.1, 0.95, 0.9))
    for k in range(3):
        d = Vector((random.uniform(-1, 1), random.uniform(-1, 1), random.uniform(-0.5, 0.8))).normalized()
        rock(B, d * 0.24, 0.14, 11 + k, 1, 0.4, "gem", lambda p, n: jitter(C_COAL, 0.4))

def salt(B, coll):
    """Halite cube with stepped hopper faces (Godot box 0.5)."""
    B.box(Vector((0, 0, 0)), (0.5, 0.5, 0.5), I3, "gem", C_SALT, 0.06)
    for n in (Vector((1, 0, 0)), Vector((-1, 0, 0)), Vector((0, 1, 0)), Vector((0, -1, 0)), Vector((0, 0, 1)), Vector((0, 0, -1))):
        R = facing_basis(n)
        for k, s in enumerate((0.36, 0.22, 0.1)):                            # hopper steps sunk into each face
            B.box(n * (0.249 - k * 0.012), (s, s, 0.004), R, "gem", jitter((0.95, 0.8, 0.8) if k == 1 else C_SALT, 0.05), 0.04)

def floatstone(B, coll):
    rock(B, Vector((0, 0, 0)), 0.38, 5.0, 2, 0.22, "rock", lambda p, n: jitter(C_PUMICE, 0.12), (1.1, 1.0, 0.9))
    for k in range(26):                                                      # vesicles
        d = Vector((random.uniform(-1, 1), random.uniform(-1, 1), random.uniform(-1, 1))).normalized()
        B.cyl(d * 0.36, d * 0.3, random.uniform(0.025, 0.05), 6, "rock", (0.25, 0.22, 0.18), 0.1)

def frost(B, coll):
    """Cluster of ice prisms round a frozen core (Godot box 0.5)."""
    rock(B, Vector((0, 0, -0.05)), 0.17, 9.0, 1, 0.2, "gem", lambda p, n: jitter(C_FROST, 0.08), (1, 1, 1))
    for k in range(9):
        d = Vector((random.uniform(-1, 1), random.uniform(-1, 1), random.uniform(-0.4, 1))).normalized()
        prism(B, d * 0.08, d * random.uniform(0.2, 0.26), random.uniform(0.05, 0.08), 6, 0.35, "gem", jitter(C_FROST, 0.1), 0.05)
    B.cyl(Vector((0, 0, -0.08)), Vector((0, 0, 0.0)), 0.08, 8, "cyan", C_CYAN, 0.02)

def lodestone(B, coll):
    """Dark magnetite with iron filings standing up along its field."""
    rock(B, Vector((0, 0, 0)), 0.36, 13.0, 2, 0.25, "rock", lambda p, n: jitter(C_LODE, 0.25), (1.0, 1.05, 0.95))
    for pole, col in ((Vector((0, 0, 1)), (0.7, 0.1, 0.08)), (Vector((0, 0, -1)), (0.1, 0.2, 0.7))):
        rock(B, pole * 0.3, 0.1, 17 + pole.z, 1, 0.2, "rock", lambda p, n, c=col: jitter(c, 0.2))
    for k in range(40):                                                      # filings bristling at the poles
        a = random.uniform(0, math.tau); e = random.uniform(0.35, 1.0) * random.choice((-1, 1))
        d = Vector((math.cos(a) * math.sqrt(1 - e * e), math.sin(a) * math.sqrt(1 - e * e), e))
        B.box(d * 0.36, (0.015, 0.015, 0.15), rot_to(d), "steel", (0.6, 0.6, 0.64), 0.1)

def quartz(B, coll):
    """Doubly-terminated quartz point (Godot capsule r 0.18, h 0.9)."""
    prism(B, Vector((0, 0, -0.1)), Vector((0, 0, 0.45)), 0.17, 6, 0.3, "gem", C_QUARTZ, 0.04)
    prism(B, Vector((0, 0, -0.1)), Vector((0, 0, -0.45)), 0.17, 6, 0.55, "gem", C_QUARTZ, 0.04)
    for k in range(3):
        d = Vector((random.uniform(-0.5, 0.5), random.uniform(-0.5, 0.5), 1)).normalized()
        b = Vector((random.uniform(-0.08, 0.08), random.uniform(-0.08, 0.08), -0.05))
        prism(B, b, b + d * 0.25, 0.05, 6, 0.4, "gem", (0.95, 0.85, 0.95), 0.04)

def latex(B, coll):
    """Sticky amber blob with drips (radius ~0.33)."""
    rock(B, Vector((0, 0, 0)), 0.3, 21.0, 3, 0.12, "gem", lambda p, n: jitter(C_LATEX, 0.08), (1.1, 1.05, 0.8))
    for k in range(5):
        a = k / 5 * math.tau + random.uniform(-0.3, 0.3)
        p = Vector((math.cos(a) * 0.3, math.sin(a) * 0.3, -0.02))
        drop = 0.08 + random.uniform(0, 0.05)
        B.cyl(p, p + Vector((math.cos(a) * 0.02, math.sin(a) * 0.02, -drop)), 0.03, 8, "gem", jitter(C_LATEX, 0.1), 0.05)
        rock(B, p + Vector((math.cos(a) * 0.02, math.sin(a) * 0.02, -drop - 0.02)), 0.04, 30 + k, 1, 0.1, "gem", lambda p, n: jitter(C_LATEX, 0.1), (1, 1, 1))

def sulfur(B, coll):
    rock(B, Vector((0, 0, -0.05)), 0.26, 23.0, 2, 0.3, "rock", lambda p, n: jitter((0.8, 0.7, 0.2), 0.2), (1.1, 1.0, 0.8))
    for k in range(10):                                                      # bright crystals on top
        d = Vector((random.uniform(-0.7, 0.7), random.uniform(-0.7, 0.7), 1)).normalized()
        b = Vector((random.uniform(-0.12, 0.12), random.uniform(-0.12, 0.12), 0.1))
        prism(B, b, b + d * random.uniform(0.12, 0.2), random.uniform(0.04, 0.06), 4, 0.5, "gem", jitter(C_SULFUR, 0.08), 0.05)

def quicksilver(B, coll):
    """A bead of liquid metal (radius 0.28), slightly flattened by its weight."""
    bm = B.bm
    res = bmesh.ops.create_uvsphere(bm, u_segments=28, v_segments=16, radius=0.28)
    for v in res["verts"]:
        v.co.z *= 0.9 if v.co.z > 0 else 0.84
    fs = list({f for v in res["verts"] for f in v.link_faces})
    for f in fs:
        f.material_index = MI["steel"]; f.smooth = True
    B.paint(fs, C_MERCURY, 0.03)

def voltaic(B, coll):
    """Violet crystal spray with glowing cores (Godot box 0.5, 0.7, 0.5)."""
    rock(B, Vector((0, 0, -0.22)), 0.16, 27.0, 1, 0.2, "rock", lambda p, n: jitter((0.15, 0.12, 0.2), 0.2), (1.2, 1.2, 0.7))
    for k in range(8):
        d = Vector((random.uniform(-0.6, 0.6), random.uniform(-0.6, 0.6), 1)).normalized()
        b = Vector((random.uniform(-0.08, 0.08), random.uniform(-0.08, 0.08), -0.18))
        tip = b + d * random.uniform(0.35, 0.52)
        prism(B, b, tip, random.uniform(0.05, 0.08), 6, 0.3, "gem", jitter(C_VOLTAIC, 0.1), 0.05)
        B.cyl(b, b + (tip - b) * 0.6, 0.02, 6, "violet", C_VIOLET, 0.02)

# ---------------------------------------------------------------------------- products
def coke(B, coll):
    """Pillow briquette (Godot box 0.45, 0.3, 0.45) with embers in its pores."""
    rings_ = [quad_ring(0, 0, z, 0.225 * s, 0.225 * s) for z, s in ((-0.15, 0.78), (-0.1, 0.96), (0.0, 1.0), (0.1, 0.96), (0.15, 0.78))]
    B.tube_rings(rings_, "rock", (0.16, 0.16, 0.17), 0.15, 0.0, cap=True, smooth=True)
    for k in range(10):
        p = Vector((random.uniform(-0.15, 0.15), random.uniform(-0.15, 0.15), 0.152))
        B.cyl(p, p + Vector((0, 0, 0.004)), random.uniform(0.012, 0.025), 6, "molten", C_MOLTEN, 0.1)

def glass_pane(B, coll):
    """Float-glass pane (Godot box 0.9, 0.05, 0.9)."""
    B.box(Vector((0, 0, 0)), (0.86, 0.86, 0.05), I3, "glass", (0.6, 0.9, 0.95), 0.02)
    for n in (Vector((1, 0, 0)), Vector((-1, 0, 0)), Vector((0, 1, 0)), Vector((0, -1, 0))):
        B.box(n * 0.44, (0.02 if n.x else 0.9, 0.02 if n.y else 0.9, 0.05), I3, "cyan", (0.5, 0.95, 0.9), 0.02)

def quartz_shards(B, coll):
    for k in range(7):
        d = Vector((random.uniform(-1, 1), random.uniform(-1, 1), random.uniform(-1, 1))).normalized()
        p = d * random.uniform(0.04, 0.12)
        prism(B, p, p + Vector((random.uniform(-1, 1), random.uniform(-1, 1), random.uniform(-1, 1))).normalized() * random.uniform(0.12, 0.2),
              random.uniform(0.03, 0.05), 6, 0.5, "gem", C_QUARTZ, 0.04)

def rubber_ball(B, coll):
    bm = B.bm
    res = bmesh.ops.create_uvsphere(bm, u_segments=24, v_segments=14, radius=0.3)
    fs = list({f for v in res["verts"] for f in v.link_faces})
    for f in fs:
        f.material_index = MI["rubber"]; f.smooth = True
        c = f.calc_center_median()
        B.paint([f], C_YELLOW if abs(c.z) < 0.05 else C_RUBBER_RED, 0.05)

def blast_charge(B, coll):
    """Sulfur-coal charge in a steel canister (Godot capsule r 0.2, h 0.7)."""
    B.cyl(Vector((0, 0, -0.3)), Vector((0, 0, 0.26)), 0.19, 18, "panel", (0.7, 0.1, 0.06), 0.1)
    for z in (-0.3, 0.2):
        B.cyl(Vector((0, 0, z)), Vector((0, 0, z + 0.06)), 0.2, 18, "metal", C_DARK, 0.1)
    hazard(B, Vector((0.19, -0.1, -0.12)), (0, 1, 0), (0, 0, 1), 0.2, 0.12, (1, 0, 0), pitch=0.05)
    B.cyl(Vector((0, 0, 0.26)), Vector((0, 0, 0.3)), 0.06, 10, "steel", C_STEEL)
    B.pipe([Vector((0, 0, 0.3)), Vector((0.03, 0, 0.34)), Vector((0.07, 0.02, 0.33))], 0.012, 5, "rubber", (0.2, 0.15, 0.1))
    B.cyl(Vector((0.07, 0.02, 0.33)), Vector((0.08, 0.02, 0.33)), 0.015, 6, "molten", C_MOLTEN, 0.05)

def battery(B, coll):
    """Voltaic cell (Godot capsule r 0.22, h 0.6)."""
    B.cyl(Vector((0, 0, -0.27)), Vector((0, 0, 0.23)), 0.21, 20, "copper", C_COPPER, 0.08)
    B.cyl(Vector((0, 0, -0.1)), Vector((0, 0, 0.12)), 0.215, 20, "panel", C_BLACK, 0.05)
    ring(B, Vector((0, 0, 0.01)), (0, 0, 1), 0.214, 0.22, 0.06, 20, "violet", C_VIOLET, 0.02)
    B.cyl(Vector((0, 0, 0.23)), Vector((0, 0, 0.28)), 0.08, 12, "steel", C_STEEL)
    B.cyl(Vector((0, 0, -0.3)), Vector((0, 0, -0.27)), 0.15, 16, "steel", C_STEEL)

def magnet_core(B, coll):
    """Pressed lodestone core wound with copper (Godot box 0.5)."""
    B.box(Vector((0, 0, 0)), (0.46, 0.46, 0.46), I3, "metal", C_LODE, 0.15)
    for k in range(7):                                                       # square copper windings round the core
        x = -0.18 + k * 0.06
        for (c, size) in (((x, 0.245, 0), (0.04, 0.03, 0.52)), ((x, -0.245, 0), (0.04, 0.03, 0.52)),
                          ((x, 0, 0.245), (0.04, 0.52, 0.03)), ((x, 0, -0.245), (0.04, 0.52, 0.03))):
            B.box(Vector(c), size, I3, "copper", C_COPPER, 0.08)
    B.box(Vector((0.24, 0, 0)), (0.02, 0.44, 0.44), I3, "panel", (0.75, 0.1, 0.08), 0.05)                  # poles at the coil ends
    B.box(Vector((-0.24, 0, 0)), (0.02, 0.44, 0.44), I3, "panel", (0.1, 0.2, 0.7), 0.05)

def aerogel(B, coll):
    """Frozen-smoke tile (Godot box 0.8, 0.1, 0.8)."""
    B.box(Vector((0, 0, 0)), (0.8, 0.8, 0.1), I3, "glass", (0.6, 0.75, 1.0), 0.02)
    B.box(Vector((0, 0, 0)), (0.7, 0.7, 0.06), I3, "cyan", (0.35, 0.5, 0.9), 0.02)

# ---------------------------------------------------------------------------- puzzle-room resources
def scree(B, coll):
    """A clump of five water-worn pebbles (Godot sphere r 0.25): rolls freely."""
    rock(B, Vector((0, 0, 0)), 0.17, 41.0, 2, 0.12, "rock", lambda p, n: jitter((0.48, 0.44, 0.38), 0.15), (1, 1, 0.95))
    for k in range(4):
        a = k / 4 * math.tau + 0.4
        d = Vector((math.cos(a), math.sin(a), random.uniform(-0.3, 0.3))).normalized()
        rock(B, d * 0.13, random.uniform(0.08, 0.11), 42 + k, 2, 0.12, "rock",
             lambda p, n: jitter(random.choice([(0.55, 0.5, 0.42), (0.35, 0.33, 0.3), (0.6, 0.45, 0.32)]), 0.1), (1, 1, 1))

def shale(B, coll):
    """Stacked flat shale leaves (Godot box 0.9, 0.14, 0.8): slides only on steep slopes."""
    for k, (dz, s) in enumerate(((-0.045, 1.0), (0.0, 0.94), (0.045, 0.86))):
        rings_ = [quad_ring(random.uniform(-0.02, 0.02), random.uniform(-0.02, 0.02), dz + h, 0.45 * s + random.uniform(-0.02, 0.02),
                            0.4 * s + random.uniform(-0.02, 0.02)) for h in (-0.022, 0.022)]
        B.tube_rings(rings_, "rock", jitter((0.22, 0.25, 0.3), 0.12), 0.15, 0.0, cap=True, smooth=False)

def puck(B, coll):
    """Polished slickstone disc (Godot box 0.7, 0.16, 0.7): almost no friction."""
    B.cyl(Vector((0, 0, -0.08)), Vector((0, 0, 0.08)), 0.35, 32, "gem", (0.08, 0.1, 0.14), 0.05)
    ring(B, Vector((0, 0, 0)), (0, 0, 1), 0.35, 0.355, 0.1, 32, "cyan", C_CYAN, 0.02)
    B.cyl(Vector((0, 0, 0.08)), Vector((0, 0, 0.085)), 0.12, 20, "cyan", C_CYAN, 0.02)

def burr(B, coll):
    """Hooked seed burr (Godot sphere r 0.3): clings, rides, never rolls."""
    rock(B, Vector((0, 0, 0)), 0.17, 51.0, 2, 0.1, "rock", lambda p, n: jitter((0.35, 0.28, 0.12), 0.15), (1, 1, 1))
    for k in range(34):
        z = 1 - 2 * (k + 0.5) / 34; r = math.sqrt(1 - z * z); a = k * 2.4
        d = Vector((math.cos(a) * r, math.sin(a) * r, z))
        prism(B, d * 0.15, d * 0.3, 0.022, 4, 0.6, "rock", jitter((0.5, 0.42, 0.18), 0.1), 0.05)

def ballast(B, coll):
    """Cast ballast shot (Godot sphere r 0.35): very heavy, rolls on the slightest tilt."""
    bm = B.bm
    res = bmesh.ops.create_uvsphere(bm, u_segments=24, v_segments=14, radius=0.35)
    fs = list({f for v in res["verts"] for f in v.link_faces})
    for f in fs:
        f.material_index = MI["metal"]; f.smooth = True
    B.paint(fs, (0.2, 0.2, 0.22), 0.1, rust=0.3)
    ring(B, Vector((0, 0, 0)), (0, 0, 1), 0.345, 0.355, 0.05, 24, "panel", C_YELLOW, 0.05)

PIECES_SPEC = [
    ("IronIngot", "iron_ingot", lambda B, c: ingot(B, "steel", C_IRON)),
    ("IronRod", "iron_rod", lambda B, c: rod(B, "steel", C_IRON)),
    ("IronPlate", "iron_plate", lambda B, c: plate(B, "steel", C_IRON)),
    ("CopperIngot", "copper_ingot", lambda B, c: ingot(B, "copper", C_COPPER)),
    ("CopperRod", "copper_rod", lambda B, c: rod(B, "copper", C_COPPER)),
    ("CopperPlate", "copper_plate", lambda B, c: plate(B, "copper", C_COPPER)),
    ("ScrapBall", "scrap_ball", scrap_ball),
    ("ScrapIngot", "scrap_ingot", lambda B, c: ingot(B, "metal", C_IRON_DARK, mottle=[C_COPPER, (0.55, 0.3, 0.12), (0.6, 0.6, 0.62)])),
    ("Coal", "coal", coal),
    ("Salt", "salt_crystal", salt),
    ("Floatstone", "floatstone", floatstone),
    ("Frost", "frost_crystal", frost),
    ("Lodestone", "lodestone", lodestone),
    ("Quartz", "quartz_crystal", quartz),
    ("Latex", "latex_resin", latex),
    ("Sulfur", "sulfur", sulfur),
    ("Quicksilver", "quicksilver", quicksilver),
    ("Voltaic", "voltaic_crystal", voltaic),
    ("Coke", "coke_briquette", coke),
    ("GlassPane", "glass_pane", glass_pane),
    ("QuartzShards", "quartz_shards", quartz_shards),
    ("RubberBall", "rubber_ball", rubber_ball),
    ("BlastCharge", "blast_charge", blast_charge),
    ("Battery", "battery_cell", battery),
    ("MagnetCore", "magnet_core", magnet_core),
    ("Aerogel", "aerogel_tile", aerogel),
    ("Scree", "scree_pebbles", scree),
    ("Shale", "shale_slab", shale),
    ("Puck", "slickstone_puck", puck),
    ("Burr", "burr_seed", burr),
    ("Ballast", "ballast_shot", ballast),
]
PIECES = [((lambda n=n, fn=fn: item(n, fn)), f"{file}.glb") for n, file, fn in PIECES_SPEC]
ICONS = [(f"Item_{n}", f"items/{file}.png", "item", (1.2, 1.3, 0.9)) for n, file, fn in PIECES_SPEC]

def build_all(do_export=True):
    return build_family(PIECES, OUT_DIR, do_export)

if __name__ == "__main__":
    build_all(do_export=False)
