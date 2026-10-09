"""
Decorative props for the facility's lower level: the vast, dilapidated, overgrown and crumbling laboratories and
factories under the test facility. Non-functional; some carry a looping "idle-loop" animation.

Frame: Blender Z up, fronts (screens, doors, open sides) face -Y (Godot +Z), origin on the FLOOR at the prop's footprint centre (z = 0 is the ground; unlike
the grid structures, whose origin is a cell centre). The decor scenes give the solid
ones a bounding-box collider.

Ruin props (a few metres):
  cracked_pillar, collapsed_pillar, rubble_pile, moss_mound, overgrown_tree, fern_cluster, hanging_vines*,
  collapsed_panel_wall, broken_catwalk, cable_drapes, lab_bench, flicker_terminal*, monitor_bank*,
  filing_cabinets, cryo_pod*, observation_booth, pipe_cluster*, hanging_lamp*, ceiling_fan*, rusted_barrels,
  crate_stack, puddle_debris, tipped_barriers, elevator_ruin
Superstructures (tens of metres; placed far from the main area):
  cooling_tower*, gantry_crane*, reactor_sphere*, arcology_spire, sky_bridge, panel_arm_wall*
(* animated)

Detail conventions (see the helpers below): foliage, stains, cracks and paper are one-sided cards (the materials
export double-sided), thin or tiling parts are sharp boxes (`flat`), and `done` finishes a static node: it folds
the rough dielectric materials into "rock" and raw steel into "metal" (fewer surfaces), then darkens everything
toward the floor with damp grime (`grime`).
"""
import bpy, bmesh, math, random, os
from mathutils import Vector, Matrix, noise

_HERE = (os.path.dirname(os.path.abspath(__file__)) if "__file__" in globals()
         else r"C:\\Users\\steph\\OneDrive\\Documents\\godot\\projects\\gmp-framework\\game\\assets\\models\\props\\decor\\source")
_LIB = os.path.normpath(os.path.join(_HERE, "..", "..", "..", "shared", "salvage_lib.py"))
_S = {"__name__": "salvage_lib", "__file__": _LIB}
exec(compile(open(_LIB, encoding="utf-8").read(), _LIB, "exec"), _S)
_SKIP = {"HERE", "OUT_DIR", "PIECES", "build_all", "ICONS", "export", "CONV"}
globals().update({k: v for k, v in _S.items() if not k.startswith("__") and k not in _SKIP})
HERE = (os.path.dirname(os.path.abspath(__file__)) if "__file__" in globals() else _HERE)
OUT_DIR = os.path.dirname(HERE)

C_CONC = (0.55, 0.54, 0.5)
C_CONC_D = (0.33, 0.32, 0.3)
C_RUST = (0.38, 0.18, 0.07)
C_MOSS = (0.17, 0.3, 0.08)
C_LEAF = (0.2, 0.42, 0.12)
C_LEAF2 = (0.3, 0.5, 0.16)
C_BARK = (0.26, 0.18, 0.1)
C_PAPER = (0.82, 0.8, 0.72)
C_WATER = (0.1, 0.14, 0.13)
C_DEAD = (0.42, 0.34, 0.14)          # dry, yellowed leaves
C_DAMP = (0.07, 0.085, 0.05)         # what the floor grime blends toward
C_STAIN = (0.22, 0.21, 0.18)         # water run-off on concrete
C_CRACK = (0.06, 0.06, 0.055)
C_VINE = (0.22, 0.28, 0.1)
C_GLASS = (0.6, 0.9, 1.0)
C_GREY = (0.45, 0.47, 0.44)

V = lambda *a: Vector(a)

def jit(c, k=0.15):
    f = 1 + random.uniform(-k, k)
    return tuple(min(1.0, x * f) for x in c)

def concrete(p, n):
    return jit(C_MOSS if n.z > 0.6 and random.random() < 0.35 else random.choice([C_CONC, C_CONC, C_CONC_D]), 0.12)

# ======================================================================================== detail helpers
MAT_MERGE = {"rubber": "rock", "wood": "rock", "tape": "rock", "steel": "metal"}   # same look, fewer surfaces
NO_GRIME = ("glow", "lamp", "cyan", "glass", "field_zp", "field_cold", "violet")

def flat(B, c, size, basis=I3, mat="rock", col=C_CONC, var=0.15, rust=0.0):
    """Sharp box (no chamfer): thin, tiny, buried or tiling parts."""
    return B.box(Vector(c), size, basis, mat, col, var, rust, bevel=0)

def card(B, pts, col, mat="rock", var=0.1):
    """One-sided polygon (the exported materials are double-sided): leaves, decals, stains, paper."""
    f = B.bm.faces.new([B.bm.verts.new(Vector(p)) for p in pts])
    f.material_index = MI[mat]; f.smooth = False
    B.paint([f], col, var)
    return f

def leaf(B, p, d, l, w, col):
    """Folded leaf card from p along d (2 tris)."""
    d = Vector(d).normalized(); s = d.cross(ZV)
    s = s.normalized() if s.length > 1e-3 else V(1, 0, 0)
    m = p + d * l * 0.45 + ZV * l * 0.08
    return card(B, [p, m + s * w / 2, p + d * l - ZV * l * 0.25, m - s * w / 2], jit(col, 0.12))

def decal(B, c, n, w, h, col, mat="rock", spin=0.0, off=0.004):
    """Flat quad lying on a surface with normal n (stains, plates, seams)."""
    n = Vector(n).normalized()
    R = facing_basis(n) @ Matrix.Rotation(spin, 3, 'Z')
    c = Vector(c) + n * off
    return card(B, [c + R @ V(sx * w / 2, sy * h / 2, 0) for sx, sy in ((-1, -1), (1, -1), (1, 1), (-1, 1))], col, mat, 0.05)

def blob(B, c, r, col, seg=9, mat="rock", seed=0.0, off=0.004, n=ZV):
    """Irregular flat patch (puddle stain, dust, moss film) on a surface."""
    n = Vector(n).normalized(); R = facing_basis(n)
    pts = [Vector(c) + n * off + R @ V(math.cos(a) * r * (1 + 0.3 * noise.noise(V(math.cos(a), math.sin(a), seed))),
                                       math.sin(a) * r * (1 + 0.3 * noise.noise(V(math.sin(a), seed, math.cos(a)))), 0)
           for a in (k / seg * math.tau for k in range(seg))]
    return card(B, pts, col, mat, 0.08)

def streak(B, p, n, length, w=0.06, col=C_RUST, fade=C_CONC, mat="rock"):
    """Run-off below p on a vertical face with normal n: a tapering quad, full colour at the top, fading down."""
    n = Vector(n).normalized(); u = ZV.cross(n).normalized(); p = Vector(p) + n * 0.004
    f = card(B, [p - u * w / 2, p + u * w / 2, p + u * w * 0.12 - ZV * length, p - u * w * 0.12 - ZV * length], col, mat, 0.05)
    lo = [c + (fc - c) * 0.7 for c, fc in zip(col, fade)]
    for l in f.loops:
        if l.vert.co.z < p.z - length / 2:
            l[B.col] = (lo[0], lo[1], lo[2], 1.0)
    return f

def crack(B, p, n, length, seed, w=0.014, col=C_CRACK, steps=5, mat="rock", heading=None):
    """Zigzag crack drawn on a surface with normal n, starting at p."""
    rng = random.Random(seed); n = Vector(n).normalized(); R = facing_basis(n)
    a = rng.uniform(0, math.tau) if heading is None else heading
    pts = [Vector(p) + n * 0.004]
    for k in range(steps):
        a += rng.uniform(-0.7, 0.7)
        pts.append(pts[-1] + R @ V(math.cos(a), math.sin(a), 0) * length / steps)
    for i in range(steps):
        s = (pts[i + 1] - pts[i]).cross(n).normalized() * w * (1 - i / steps * 0.7) / 2
        card(B, [pts[i] - s, pts[i] + s, pts[i + 1] + s * 0.75, pts[i + 1] - s * 0.75], col, mat, 0.05)

def stud(B, p, n, r=0.025, h=0.015, col=C_DARK, mat="metal", seg=6):
    """Bolt head / rivet sitting on a surface with normal n."""
    n = Vector(n).normalized(); R = rot_to(n)
    ring_ = lambda d: [Vector(p) + n * d + R @ V(math.cos(a) * r, math.sin(a) * r, 0) for a in (k / seg * math.tau for k in range(seg))]
    return B.tube_rings([ring_(-0.002), ring_(h)], mat, col, 0.1, 0.0, cap=True, smooth=False)

def plate(B, c, n, w, h, t=0.035, mat="panel", col=C_WHITE, var=0.25, rust=0.0, bev=0.012, seam=True, spin=0.0):
    """Wall panel facing n with a chamfered face and no back (it sits on a frame): 5 quads, plus a seam line."""
    n = Vector(n).normalized(); R = facing_basis(n) @ Matrix.Rotation(spin, 3, 'Z'); c = Vector(c)
    b = min(bev, 0.6 * t, 0.2 * min(w, h))
    lp = ((-1, -1), (1, -1), (1, 1), (-1, 1))
    vb = [B.bm.verts.new(c + R @ V(sx * w / 2, sy * h / 2, -t / 2)) for sx, sy in lp]
    vf = [B.bm.verts.new(c + R @ V(sx * (w / 2 - b), sy * (h / 2 - b), t / 2)) for sx, sy in lp]
    front = B.quad(vf, mat)
    sides = [B.quad([vb[i], vb[(i + 1) % 4], vf[(i + 1) % 4], vf[i]], mat) for i in range(4)]
    back = B.quad(vb[::-1], mat)
    bmesh.ops.recalc_face_normals(B.bm, faces=[front, back] + sides)
    bmesh.ops.delete(B.bm, geom=[back], context='FACES_ONLY')
    for f in [front] + sides:
        f.smooth = False
    seed = random.random() * 50
    B.paint([front], col, var, rust, seed)
    B.paint(sides, tuple(a + (bb - a) * 0.35 for a, bb in zip(col, C_WEAR)), var * 0.6, rust * 0.5, seed)
    if seam and h > 0.3:
        decal(B, c + R @ V(0, h / 2 - 0.07, t / 2), n, w - 2 * b - 0.03, 0.014, (0.25, 0.25, 0.24), mat, spin, off=0.002)
    return front

def merge_mats(B):
    remap = {MI[a]: MI[b] for a, b in MAT_MERGE.items()}
    for f in B.bm.faces:
        f.material_index = remap.get(f.material_index, f.material_index)

def grime(B, h=0.8, k=0.4, z0=0.0, stain=0.0, cut=True):
    """Damp grime toward the floor (z0): big faces are cut at z0 + h so the gradient stays near the ground;
    stain adds streaky vertical run-off darkening (for tall concrete)."""
    bm = B.bm
    skip = {MI[m] for m in NO_GRIME if m in MI}
    zc = z0 + h
    if cut and h > 0:
        fs = []
        for f in bm.faces:
            if f.material_index in skip:
                continue
            zs = [v.co.z for v in f.verts]
            if min(zs) < zc - 0.03 and max(zs) > zc + 0.03 and f.calc_area() > h * h * 0.3:
                fs.append(f)
        if fs:
            es = list({e for f in fs for e in f.edges}); vs = list({v for f in fs for v in f.verts})
            bmesh.ops.bisect_plane(bm, geom=vs + es + fs, plane_co=(0, 0, zc), plane_no=(0, 0, 1))
    for f in bm.faces:
        if f.material_index in skip:
            continue
        for l in f.loops:
            p = l.vert.co
            t = min(1.0, max(0.0, 1 - (p.z - z0) / h)) ** 1.6 if h > 0 else 0.0
            kk = k * t * (0.7 + 0.6 * (noise.noise(p * 1.7) * 0.5 + 0.5))
            if stain:
                kk += stain * max(0.0, noise.noise(V(p.x * 0.35, p.y * 0.35, p.z * 0.03 + 7.0)) + 0.1)
            kk = min(0.85, kk)
            if kk <= 0:
                continue
            c = l[B.col]
            l[B.col] = (c[0] + (C_DAMP[0] - c[0]) * kk, c[1] + (C_DAMP[1] - c[1]) * kk, c[2] + (C_DAMP[2] - c[2]) * kk, 1.0)

def done(B, name, coll, h=0.8, k=0.4, z0=0.0, stain=0.0, pivot=None):
    """Finish a static node: fewer surfaces, grime toward the floor."""
    merge_mats(B)
    if h or stain:
        grime(B, h, k, z0, stain)
    return finish(B, name, coll, pivot)

def mnode(B, name, coll, pivot=None):
    """node() for moving parts, with the same material folding."""
    merge_mats(B)
    return node(B, name, coll, pivot)

# ---------------------------------------------------------------------------------------- overgrowth and debris
def chunk(B, c, r, seed, squash=(1, 1, 0.8)):
    rock(B, c, r, seed, 1, 0.35, "rock", concrete, squash)

def moss(B, c, r, seed):
    rock(B, c, r, seed, 2 if r > 0.8 else 1, 0.25, "rock", lambda p, n: jit(random.choice([C_MOSS, C_LEAF, (0.22, 0.34, 0.1)]), 0.15), (1, 1, 0.25))

def leaves(B, c, r, seed):
    rock(B, c, r, seed, 1, 0.45, "rock", lambda p, n: jit(random.choice([C_LEAF, C_LEAF2, C_MOSS]), 0.15), (1, 1, 0.8))

def rebar(B, base, d, length):
    d = Vector(d).normalized()
    mid = base + d * length * 0.6
    B.pipe([base, mid, mid + (d + Vector((random.uniform(-.6, .6), random.uniform(-.6, .6), 0))).normalized() * length * 0.4],
           0.018, 4, "rock", C_RUST)

def vine(B, top, length, seed):
    """Hanging vine: a wavering 4-sided stem with pairs of leaf cards."""
    rng = random.Random(seed)
    pts = [top + Vector((math.sin(k * 0.9 + seed) * 0.06, math.cos(k * 0.7 + seed) * 0.06, -k * length / 10)) for k in range(11)]
    B.pipe(pts, 0.022, 4, "rock", C_VINE)
    for k in range(1, 11):
        for j in range(2 if k > 2 else 1):
            a = rng.uniform(0, math.tau)
            col = C_DEAD if rng.random() < 0.12 else rng.choice([C_LEAF, C_LEAF2, C_LEAF])
            leaf(B, pts[k], (math.cos(a), math.sin(a), -0.2), rng.uniform(0.13, 0.2), rng.uniform(0.07, 0.11), col)

def fern(B, c, seed, size=1.0, fronds=7):
    """Arching fronds: a 3-sided stem each, with paired leaflet cards."""
    rng = random.Random(seed)
    for k in range(fronds):
        a = k / fronds * math.tau + rng.uniform(-0.25, 0.25)
        d = Vector((math.cos(a), math.sin(a), 0)); s = d.cross(ZV)
        lift = rng.uniform(0.8, 1.15)
        m = c + d * 0.35 * size + ZV * 0.5 * size * lift
        tip = c + d * 0.75 * size + ZV * 0.25 * size * lift
        bez = lambda t: c * (1 - t) ** 2 + m * 2 * (1 - t) * t + tip * t * t
        B.pipe([bez(t) for t in (0, 0.35, 0.7, 1.0)], 0.012 * size, 3, "rock", C_LEAF)
        col = C_DEAD if rng.random() < 0.15 else C_LEAF2
        for t in (0.3, 0.45, 0.6, 0.75, 0.9):
            p = bez(t); ln = 0.2 * size * (1.15 - t)
            for sd in (-1, 1):
                leaf(B, p, s * sd + d * 0.6, ln, 0.06 * size, col)

def grass(B, c, seed, n=6, h=0.35):
    """A tuft of blade cards."""
    rng = random.Random(seed)
    for k in range(n):
        a = rng.uniform(0, math.tau); d = V(math.cos(a), math.sin(a), 0)
        p = c + d * rng.uniform(0, 0.08)
        tip = p + d * rng.uniform(0.05, 0.18) + ZV * h * rng.uniform(0.6, 1.1)
        s = d.cross(ZV) * 0.025
        card(B, [p - s, p + s, tip], jit(rng.choice([C_LEAF2, C_DEAD, C_LEAF]), 0.1))

def new(name):
    random.seed(sum(map(ord, name)))
    return clear_collection(f"Decor_{name}")

# ======================================================================================== ruin props
def build_cracked_pillar():
    coll = new("CrackedPillar"); B = Builder()
    h = 5.2
    B.box(Vector((0, 0, h / 2)), (1.0, 1.0, h), I3, "rock", C_CONC, 0.15, rust=0.1, bevel=0.04)
    for k in range(6):                                                       # jagged broken top
        chunk(B, Vector((random.uniform(-0.3, 0.3), random.uniform(-0.3, 0.3), h + random.uniform(0.0, 0.3))), random.uniform(0.2, 0.35), 3 + k)
    for (x, y) in ((-0.35, -0.35), (0.35, -0.35), (0.35, 0.35), (-0.35, 0.35)):
        rebar(B, Vector((x, y, h - 0.1)), (x * 0.5, y * 0.5, 1), 0.9)
        streak(B, V(x, -0.5, h - 0.02) if y < 0 else V(0.5, y, h - 0.02), (0, -1, 0) if y < 0 else (1, 0, 0),
               random.uniform(0.9, 1.6), 0.07, C_RUST, C_CONC)                  # rust bleeding from the rebar
    for k in range(3):                                                       # spalled faces with bars showing
        z = random.uniform(1.5, 4); y = random.uniform(-0.25, 0.25)
        flat(B, V(0.505, y, z), (0.02, 0.32, 0.42), I3, "rock", C_CONC_D, 0.2)
        for dz in (-0.08, 0.1):
            B.cyl(V(0.52, y - 0.17, z + dz), V(0.52, y + 0.17, z + dz), 0.014, 4, "rock", C_RUST, 0.1, cap=False)
    for (x, z) in ((-0.2, 3.3), (0.25, 1.7)):
        flat(B, V(x, -0.505, z), (0.36, 0.02, 0.3), I3, "rock", C_CONC_D, 0.2)
        B.cyl(V(x - 0.19, -0.52, z), V(x + 0.19, -0.52, z), 0.014, 4, "rock", C_RUST, 0.1, cap=False)
    for k, (p, n) in enumerate(((V(0.1, -0.5, 4.4), (0, -1, 0)), (V(-0.3, -0.5, 2.6), (0, -1, 0)), (V(0.3, -0.5, 1.2), (0, -1, 0)),
                                (V(0.5, 0.2, 3.0), (1, 0, 0)), (V(-0.5, 0.0, 2.2), (-1, 0, 0)))):
        crack(B, p, n, random.uniform(0.6, 1.1), 10 + k, heading=-math.pi / 2 + random.uniform(-0.5, 0.5))
    for x in (-0.25, 0.15):                                                  # water stains from the broken top
        streak(B, V(x, -0.5, h - 0.05), (0, -1, 0), random.uniform(2.0, 3.2), 0.28, C_STAIN, C_CONC)
    B.box(Vector((0, 0, 1.0)), (1.02, 1.02, 0.25), I3, "rock", C_YELLOW, 0.3, rust=0.5, bevel=0.008)       # faded hazard band
    flat(B, V(-0.18, -0.52, 2.1), (0.34, 0.012, 0.22), Matrix.Rotation(0.12, 3, 'Y'), "metal", (0.62, 0.62, 0.58), 0.2, rust=0.5)  # grid sign
    stud(B, V(-0.3, -0.527, 2.18), (0, -1, 0), 0.015, 0.01)
    B.box(Vector((0, 0, 0.1)), (1.4, 1.4, 0.2), I3, "rock", C_CONC_D, 0.15, bevel=0.04)
    for k in range(4):
        moss(B, Vector((random.uniform(-0.8, 0.8), random.uniform(-0.8, 0.8), 0.1)), 0.4, 20 + k)
    moss(B, V(0.2, 0.1, h + 0.05), 0.35, 25)
    for k in range(4):
        vine(B, Vector((0.52 * random.choice((-1, 1)), random.uniform(-0.4, 0.4), h - 0.2)), random.uniform(2.0, 3.5), k)
    fern(B, V(-0.5, -0.7, 0.2), 26, 0.55, 6)
    grass(B, V(0.45, -0.6, 0.2), 27)
    done(B, "Mesh", coll, h=1.4, k=0.5); return coll

def build_collapsed_pillar():
    coll = new("CollapsedPillar"); B = Builder()
    tilt = Matrix.Rotation(math.radians(88), 3, 'Y') @ Matrix.Rotation(0.2, 3, 'X')
    tilt2 = Matrix.Rotation(math.radians(80), 3, 'Y') @ Matrix.Rotation(0.5, 3, 'Z')
    c1, c2 = Vector((-1.4, 0, 0.55)), Vector((1.5, 0.3, 0.5))
    B.box(c1, (1.0, 1.0, 2.6), tilt, "rock", C_CONC, 0.15, bevel=0.04)
    B.box(c2, (1.0, 1.0, 2.2), tilt2, "rock", C_CONC, 0.15, bevel=0.04)
    B.box(c1 + tilt @ V(0, 0, -0.5), (1.02, 1.02, 0.25), tilt, "rock", C_YELLOW, 0.3, rust=0.5, bevel=0.008)   # hazard band
    for k in range(9):
        chunk(B, Vector((random.uniform(-0.6, 0.6), random.uniform(-1, 1), 0.15)), random.uniform(0.15, 0.35), 40 + k)
    for k in range(5):
        rebar(B, Vector((random.uniform(-0.2, 0.3), random.uniform(-0.4, 0.4), 0.6)), (random.uniform(-1, 1), random.uniform(-1, 1), 0.6), 0.8)
    for k, (c, T) in enumerate(((c1, tilt), (c2, tilt2))):                  # cracks on the long faces
        for j, (lx, lz) in enumerate(((0.0, -0.6), (0.1, 0.5))):
            crack(B, c + T @ V(0.505, lx, lz), T @ V(1, 0, 0), 0.9, 44 + k * 3 + j)
            crack(B, c + T @ V(lx, -0.505, lz), T @ V(0, -1, 0), 0.7, 49 + k * 3 + j)
    for k in range(3):
        moss(B, Vector((random.uniform(-2, 2), random.uniform(-0.6, 0.6), 0.9)), 0.4, 50 + k)
    blob(B, V(0.0, -0.3, 0), 1.0, (0.3, 0.29, 0.26), 11, seed=3.0, off=0.006)   # dust spread around the break
    fern(B, Vector((0.2, -1.1, 0.0)), 7, 0.8)
    grass(B, V(-2.2, 0.5, 0), 57); grass(B, V(0.6, 0.9, 0), 58, 5)
    done(B, "Mesh", coll, h=0.6, k=0.4); return coll

def bent_panel(B, hinge, a, tilt, bend, w=1.2, h=0.8, t=0.08, col=C_WHITE):
    """A wall panel folded in two about a vertical crease at hinge."""
    R1 = Matrix.Rotation(a, 3, 'Z') @ Matrix.Rotation(tilt, 3, 'X')
    R2 = R1 @ Matrix.Rotation(bend, 3, 'Z')
    B.box(hinge + R1 @ V(-w / 4, 0, 0), (w / 2, t, h), R1, "panel", col, 0.25, rust=0.3)
    B.box(hinge + R2 @ V(w / 4, 0, 0), (w / 2, t, h), R2, "panel", col, 0.25, rust=0.4)

def build_rubble_pile():
    coll = new("RubblePile"); B = Builder()
    for k in range(22):
        a = random.uniform(0, math.tau); r = random.uniform(0, 1.6)
        z = max(0.1, 0.9 - r * 0.5)
        chunk(B, Vector((math.cos(a) * r, math.sin(a) * r, z * random.uniform(0.4, 1.0))), random.uniform(0.2, 0.45), 60 + k)
    for k in range(4):                                                       # broken white wall panels, bent in the fall
        a = random.uniform(0, math.tau)
        bent_panel(B, Vector((math.cos(a) * 1.0, math.sin(a) * 1.0, 0.5)), a, random.uniform(-0.6, 0.6), random.uniform(0.2, 0.5))
    for k in range(6):
        rebar(B, Vector((random.uniform(-1, 1), random.uniform(-1, 1), 0.6)), (random.uniform(-1, 1), random.uniform(-1, 1), 1), 1.0)
    B.pipe([V(-0.9, -0.9, 0.1), V(-0.4, -0.6, 0.35), V(0.3, -0.7, 0.3)], 0.06, 6, "metal", C_GREY)           # torn conduit
    moss(B, Vector((0.6, -0.5, 0.7)), 0.5, 70)
    fern(B, Vector((-1.3, 0.8, 0.1)), 71, 0.7)
    blob(B, V(0, 0, 0), 1.3, (0.3, 0.29, 0.26), 12, seed=5.0, off=0.006)
    grass(B, V(1.3, -0.9, 0), 72); grass(B, V(-1.5, -0.4, 0), 73, 5)
    done(B, "Mesh", coll, h=0.5, k=0.35); return coll

def build_moss_mound():
    coll = new("MossMound"); B = Builder()
    rock(B, Vector((0, 0, 0.3)), 1.4, 81.0, 2, 0.3, "rock", lambda p, n: jit(random.choice([C_MOSS, C_LEAF, (0.24, 0.36, 0.12)]), 0.15), (1.2, 1.0, 0.45))
    for k in range(3):
        chunk(B, Vector((random.uniform(-1, 1), random.uniform(-0.8, 0.8), 0.7)), 0.3, 82 + k)
    for k in range(4):
        fern(B, Vector((random.uniform(-1.2, 1.2), random.uniform(-1, 1), 0.5)), 85 + k, 0.6)
    R = Matrix.Rotation(0.5, 3, 'Y')                                          # a machine corner poking out
    B.box(Vector((0.9, 0.2, 0.7)), (0.1, 0.8, 0.5), R, "metal", C_RUST, 0.2, rust=0.8)
    for k in range(4):                                                        # its vent louvres
        flat(B, V(0.9, 0.2, 0.7) + R @ V(-0.055, 0, -0.16 + k * 0.1), (0.012, 0.6, 0.035), R @ Matrix.Rotation(0.5, 3, 'Y'), "metal", C_DARK, 0.1)
    for sy in (-1, 1):
        stud(B, V(0.9, 0.2, 0.7) + R @ V(-0.05, sy * 0.34, 0.2), R @ V(-1, 0, 0), 0.02, 0.012, C_RUST)
    for k in range(3):
        grass(B, V(random.uniform(-1.3, 1.3), random.uniform(-1.1, 1.1), 0.1), 89 + k, 5, 0.3)
    done(B, "Mesh", coll, h=0); return coll

def build_overgrown_tree():
    coll = new("OvergrownTree"); B = Builder()
    rings_ = []
    for k in range(9):
        z = k * 0.9; r = 0.45 * (1 - k / 10) + 0.08 + (0.25 if k == 0 else 0)          # flared root collar
        c = Vector((math.sin(k * 0.5) * 0.15, math.cos(k * 0.4) * 0.1, z))
        rings_.append([c + Vector((math.cos(a) * r * (1 + 0.08 * math.sin(a * 3 + k)), math.sin(a) * r, 0)) for a in [i / 10 * math.tau for i in range(10)]])
    B.tube_rings(rings_, "rock", C_BARK, 0.2, 0.0, cap=True, smooth=True)
    tips = []
    for k in range(6):                                                       # branches and canopy
        a = k / 6 * math.tau + random.uniform(-0.3, 0.3); z = random.uniform(4.0, 6.5)
        base = Vector((0, 0, z)); tip = base + Vector((math.cos(a) * 1.8, math.sin(a) * 1.8, random.uniform(0.3, 1.2)))
        B.pipe([base, (base + tip) / 2 + Vector((0, 0, 0.3)), tip], 0.08, 6, "rock", C_BARK)
        leaves(B, tip, random.uniform(0.9, 1.3), 90 + k)
        tips.append(tip)
    leaves(B, Vector((0, 0, 7.8)), 1.6, 99)
    for k in range(6):                                                       # roots through cracked floor tiles
        a = k / 6 * math.tau
        B.pipe([Vector((0, 0, 0.4)), Vector((math.cos(a) * 0.8, math.sin(a) * 0.8, 0.15)), Vector((math.cos(a) * 1.6, math.sin(a) * 1.6, 0.02))],
               0.1, 6, "rock", C_BARK)
        B.box(Vector((math.cos(a + 0.5) * 1.3, math.sin(a + 0.5) * 1.3, 0.12)), (0.9, 0.9, 0.12), Matrix.Rotation(a, 3, 'Z') @ Matrix.Rotation(random.uniform(0.15, 0.4), 3, 'X'),
              "rock", C_CONC, 0.15, bevel=0.02)
    for k in (0, 2, 4):                                                       # vines trailing off the branches
        vine(B, tips[k] * 0.8 + V(0, 0, -0.3), random.uniform(2.0, 3.2), 95 + k)
    for k in range(5):                                                        # moss patches up the trunk
        a = random.uniform(0, math.tau); z = random.uniform(0.5, 3.5)
        blob(B, V(math.cos(a) * 0.42, math.sin(a) * 0.42, z), 0.2, jit(C_MOSS, 0.15), 6, seed=k, n=V(math.cos(a), math.sin(a), 0), off=0.03)
    fern(B, V(0.9, -0.9, 0.05), 97, 0.6, 6)
    grass(B, V(-1.0, 0.6, 0.05), 98)
    done(B, "Mesh", coll, h=0.5, k=0.3); return coll

def build_fern_cluster():
    coll = new("FernCluster"); B = Builder()
    for k in range(6):
        fern(B, Vector((random.uniform(-1, 1), random.uniform(-1, 1), 0)), 100 + k, random.uniform(0.7, 1.2), 8)
    for k in range(3):
        chunk(B, Vector((random.uniform(-1, 1), random.uniform(-1, 1), 0.1)), 0.2, 110 + k)
    for k in range(6):
        grass(B, V(random.uniform(-1.1, 1.1), random.uniform(-1.1, 1.1), 0), 113 + k, 7, 0.4)
    blob(B, V(0, 0, 0), 1.1, (0.2, 0.19, 0.13), 11, seed=2.0)                 # damp soil
    done(B, "Mesh", coll, h=0); return coll

def build_hanging_vines():
    coll = new("HangingVines"); B = Builder()
    for dz in (-0.14, 0.14):                                                  # overhead I-beam
        B.box(Vector((0, 0, 6.0 + dz)), (4.0, 0.3, 0.03), I3, "metal", C_RUST, 0.2, rust=0.7, bevel=0.005)
    B.box(Vector((0, 0, 6.0)), (4.0, 0.03, 0.26), I3, "metal", C_RUST, 0.2, rust=0.8, bevel=0)
    for x in (-1.9, -0.65, 0.65, 1.9):                                        # web stiffeners
        for sy in (-1, 1):
            flat(B, V(x, sy * 0.075, 6.0), (0.02, 0.12, 0.25), I3, "metal", C_RUST, 0.2, rust=0.8)
    done(B, "Beam", coll, h=0)
    for g in range(3):                                                       # three curtains swaying out of step
        Vn = Builder()
        for k in range(5):
            vine(Vn, Vector((-0.6 + k * 0.3, 0, 0)), random.uniform(2.5, 4.8), g * 10 + k)
        leaves(Vn, Vector((0, 0, 0.1)), 0.35, 120 + g)
        ob = mnode(Vn, f"Vines{g}", coll, Vector((-1.3 + g * 1.3, 0, 5.85)))
        cycle(ob, "rotation_euler", [(0.05 + 0.02 * g, 0, 0), (-0.06, 0, 0.03)], 90 + g * 30)
    return coll

def build_collapsed_panel_wall():
    coll = new("CollapsedPanelWall"); B = Builder()
    for x in (-3.0, -1.0, 1.0, 3.0):                                         # exposed framework: channel posts
        B.box(Vector((x, 0, 2.5)), (0.12, 0.2, 5.0), I3, "metal", C_DARK, 0.2, rust=0.5)
        for sx in (-1, 1):
            flat(B, V(x + sx * 0.07, -0.02, 2.5), (0.02, 0.16, 5.0), I3, "metal", C_DARK, 0.2, rust=0.6)
        B.box(V(x, 0, 0.03), (0.4, 0.4, 0.06), I3, "metal", C_RUST, 0.2, rust=0.8)                       # foot plates
        for sx, sy in ((-1, -1), (1, -1), (1, 1), (-1, 1)):
            stud(B, V(x + sx * 0.15, sy * 0.15, 0.06), ZV, 0.02, 0.02, C_RUST)
        streak(B, V(x, -0.105, 4.8), (0, -1, 0), random.uniform(1.2, 2.2), 0.06, C_RUST, C_DARK)
    B.box(Vector((0, 0, 4.9)), (6.3, 0.2, 0.15), I3, "metal", C_DARK, 0.2, rust=0.5)
    for j in range(1, 5):                                                    # horizontal girts behind the panels
        flat(B, V(0, 0.02, 0.02 + j * 1.0), (6.1, 0.12, 0.06), I3, "metal", C_RUST, 0.2, rust=0.7)
    for i in range(3):
        for j in range(5):
            if (i, j) in ((1, 3), (1, 4), (2, 4), (0, 2)):
                continue                                                     # fallen panels leave holes
            if (i, j) == (2, 3):                                             # one hanging by a corner
                plate(B, V(1.7, -0.2, 3.35), V(0, -1, 0.15), 1.9, 0.95, 0.035, "panel", jit(C_WHITE, 0.08), rust=0.4, spin=0.35)
                continue
            plate(B, Vector((-2.0 + i * 2.0, -0.11, 0.5 + j * 1.0)), (0, -1, 0), 1.9, 0.95, 0.035, "panel", jit(C_WHITE, 0.08), rust=0.15 + 0.1 * (4 - j))
            for sx in (-1, 1):
                stud(B, V(-2.0 + i * 2.0 + sx * 0.9, -0.13, 0.95 + j * 1.0), (0, -1, 0), 0.018, 0.01, (0.3, 0.3, 0.3))
    for k, (x, z, a) in enumerate(((0.2, 0.3, 1.2), (1.4, 0.2, -0.9), (2.3, 0.4, 0.4))):                   # the fallen ones, bent
        bent_panel(B, Vector((x, -1.0 - k * 0.4, z)), a, 1.35, 0.25 + 0.1 * k, 1.9, 0.95, 0.06)
    crack(B, V(-1.8, -0.13, 1.2), (0, -1, 0), 0.8, 131, mat="panel")
    crack(B, V(2.2, -0.13, 2.1), (0, -1, 0), 0.6, 132, mat="panel")
    hazard(B, Vector((-2.6, -0.132, 1.72)), (1, 0, 0), (0, 0, 1), 0.6, 0.14, (0, -1, 0), pitch=0.08)
    B.pipe([V(-3.1, -0.14, 4.75), V(-1.0, -0.2, 4.55), V(0.2, -0.16, 4.7), V(1.2, -0.25, 3.9), V(1.35, -0.3, 3.2)], 0.025, 5, "rock", C_BLACK)  # torn cable
    for k in range(5):
        vine(B, Vector((random.uniform(-3, 3), -0.2, 4.8)), random.uniform(2, 4), 130 + k)
    for k in range(3):
        moss(B, Vector((random.uniform(-3, 3), -0.4, 0.05)), 0.6, 140 + k)
    grass(B, V(-2.4, -0.4, 0), 143); grass(B, V(0.6, -0.5, 0), 144, 5)
    done(B, "Mesh", coll, h=1.0, k=0.45); return coll

def grate(B, c, sx, sy, R=I3, pitch=0.2, col=C_DARK):
    """See-through grated deck (top at c.z in the frame R): two edge channels, end bars and cross slats."""
    P = lambda x, y, z: c + R @ V(x, y, z)
    for s in (-1, 1):
        B.box(P(s * (sx / 2 - 0.03), 0, -0.07), (0.06, sy, 0.14), R, "metal", col, 0.2, rust=0.4)
        flat(B, P(0, s * (sy / 2 - 0.025), -0.05), (sx - 0.12, 0.05, 0.1), R, "metal", col, 0.2, rust=0.4)
    for k in range(int((sy - 0.1) / pitch)):
        flat(B, P(0, -sy / 2 + 0.05 + pitch / 2 + k * pitch, -0.02), (sx - 0.12, 0.035, 0.04), R, "metal", C_RUST, 0.2, rust=0.6)

def build_broken_catwalk():
    coll = new("BrokenCatwalk"); B = Builder()
    z = 3.0
    for x in (-3.5, -0.5):
        for y in (-0.6, 0.6):
            B.box(Vector((x, y, z / 2)), (0.12, 0.12, z), I3, "metal", C_DARK, 0.2, rust=0.6)
            B.box(V(x, y, 0.02), (0.3, 0.3, 0.04), I3, "metal", C_RUST, 0.2, rust=0.8)
        flat(B, V(x, 0, z / 2), (0.05, 1.2, 0.05), Matrix.Rotation(math.atan2(z * 0.8, 1.2), 3, 'X'), "metal", C_DARK, 0.2, rust=0.5)   # cross brace
    grate(B, V(-2.0, 0, z + 0.04), 1.4, 4.0, Matrix.Rotation(math.pi / 2, 3, 'Z'))   # intact deck (top 3.04)
    tilt = Matrix.Rotation(math.radians(-28), 3, 'Y')
    grate(B, V(1.6, 0, z - 0.95) + tilt @ V(0, 0, 0.04), 1.4, 3.6, tilt @ Matrix.Rotation(math.pi / 2, 3, 'Z'), col=C_RUST)   # collapsed span
    for y in (-0.7, 0.7):
        B.box(Vector((-2.0, y, z + 0.5 + 0.04)), (4.0, 0.05, 0.05), I3, "panel", C_YELLOW, 0.3, rust=0.4)
        flat(B, V(-2.0, y, z + 0.1), (4.0, 0.01, 0.12), I3, "metal", C_YELLOW, 0.3, rust=0.6)            # toe plate
        B.box(Vector((1.6, y, z - 0.45)), (3.6, 0.05, 0.05), tilt @ Matrix.Rotation(0.2, 3, 'X'), "panel", C_YELLOW, 0.3, rust=0.5)
        for x in (-3.8, -2.6, -1.4, -0.2):
            B.box(Vector((x, y, z + 0.29)), (0.04, 0.04, 0.5), I3, "metal", C_DARK, 0.2)
    B.pipe([V(-0.02, 0.7, z + 0.54), V(0.4, 0.75, z + 0.3), V(0.55, 0.8, z - 0.3)], 0.025, 5, "panel", C_YELLOW)     # snapped rail
    for k in range(4):                                                       # torn deck bars at the break
        rebar(B, V(-0.02, -0.5 + k * 0.33, z), (1, 0, -0.8), 0.45)
    rubble = Vector((3.2, 0, 0))
    for k in range(5):
        chunk(B, rubble + Vector((random.uniform(-0.6, 0.6), random.uniform(-0.8, 0.8), 0.1)), 0.25, 150 + k)
    for k in range(3):
        vine(B, Vector((-3 + k, 0.7, z)), 2.2, 155 + k)
    grass(B, V(-3.4, -0.5, 0), 159)
    done(B, "Mesh", coll, h=0.8, k=0.4); return coll

def build_cable_drapes():
    coll = new("CableDrapes"); B = Builder()
    for x in (-3.0, 3.0):
        B.box(Vector((x, 0, 2.5)), (0.25, 0.25, 5.0), I3, "metal", C_DARK, 0.2, rust=0.5)
        B.box(V(x, 0, 4.72), (0.12, 0.7, 0.1), I3, "metal", C_DARK, 0.2, rust=0.5)                       # cross arm
        B.box(V(x, -0.16, 1.4), (0.3, 0.12, 0.4), I3, "metal", C_GREY, 0.2, rust=0.4)                    # junction box
        B.cyl(V(x, -0.16, 1.6), V(x, -0.16, 4.5), 0.03, 6, "metal", C_GREY, 0.1, cap=False)             # its conduit
        for k in range(3):
            B.box(V(x, 0, 1.0 + k * 1.4), (0.27, 0.27, 0.05), I3, "metal", C_DARK, 0.2, rust=0.6, bevel=0.004)   # banding
        B.box(V(x, 0, 0.04), (0.5, 0.5, 0.08), I3, "metal", C_RUST, 0.2, rust=0.8)
        streak(B, V(x, -0.13, 4.6), (0, -1, 0), 1.4, 0.08, C_RUST, C_DARK)
    for k in range(6):                                                       # sagging cables
        z = 4.6 - k * 0.12; sag = 1.2 + k * 0.25
        pts = [Vector((-2.9 + 5.8 * t, (k - 3) * 0.06, z - sag * 4 * t * (1 - t))) for t in [i / 12 for i in range(13)]]
        B.pipe(pts, 0.03, 5, "rubber", random.choice([C_BLACK, (0.5, 0.08, 0.05), (0.1, 0.1, 0.4)]))
    for sx in (-1, 1):                                                       # clamp plates where they meet the posts
        flat(B, V(sx * 2.88, -0.02, 4.3), (0.04, 0.44, 0.7), I3, "metal", C_GREY, 0.2, rust=0.5)
    for k in range(3):                                                       # snapped ends hanging loose
        p = Vector((-2.9, 0.15 + k * 0.08, 4.3 - k * 0.2))
        B.pipe([p, p + Vector((0.2, 0, -0.8)), p + Vector((0.25, 0.05, -1.8 - k * 0.3))], 0.03, 5, "rubber", C_BLACK)
    vine(B, V(3.0, -0.14, 4.7), 3.2, 161)
    done(B, "Mesh", coll, h=0.8); return coll

def build_lab_bench():
    coll = new("LabBench"); B = Builder()
    B.box(Vector((0, 0, 0.9)), (3.0, 0.9, 0.06), I3, "panel", C_WHITE, 0.25, rust=0.3)
    for x in (-1.4, 1.4):
        B.box(Vector((x, 0, 0.45)), (0.1, 0.8, 0.9), I3, "metal", C_DARK, 0.2)
    B.box(Vector((0.3, 0.3, 0.12)), (2.7, 0.05, 0.08), I3, "metal", C_DARK, 0.2)                           # foot rail
    B.box(Vector((-0.8, 0, 0.45)), (1.2, 0.8, 0.85), I3, "panel", C_WHITE, 0.3, rust=0.4)                   # cupboard
    B.box(Vector((-0.8, -0.41, 0.5)), (0.5, 0.02, 0.5), Matrix.Rotation(0.8, 3, 'Z'), "panel", C_WHITE, 0.3)  # door hanging open
    flat(B, V(-0.98, -0.42, 0.45), (0.46, 0.012, 0.62), I3, "metal", (0.04, 0.04, 0.04), 0.1)              # the dark opening
    for k in range(2):                                                       # drawers with pulls
        plate(B, V(-0.5, -0.4, 0.65 - k * 0.3), (0, -1, 0), 0.55, 0.26, 0.02, "panel", jit(C_WHITE, 0.05), seam=False)
        flat(B, V(-0.5, -0.425, 0.72 - k * 0.3), (0.2, 0.02, 0.025), I3, "metal", C_STEEL, 0.1)
    for k in range(6):                                                       # glassware
        p = Vector((0.2 + k * 0.2, random.uniform(-0.25, 0.25), 0.93))
        if k % 3 == 2:
            B.box(p + Vector((0, 0, 0.03)), (0.25, 0.06, 0.06), Matrix.Rotation(random.uniform(0, 3), 3, 'Z'), "glass", C_GLASS, 0.02, bevel=0)   # knocked over
        else:
            B.cyl(p, p + Vector((0, 0, 0.25)), 0.05, 8, "glass", C_GLASS, 0.02)
            B.cyl(p, p + Vector((0, 0, 0.08)), 0.045, 8, "cyan", random.choice([C_CYAN, (0.4, 1.0, 0.3)]), 0.02)
    B.box(Vector((-0.6, 0.15, 1.15)), (0.5, 0.35, 0.4), I3, "panel", C_WHITE, 0.3)                          # old monitor
    B.box(Vector((-0.6, -0.03, 1.17)), (0.4, 0.01, 0.28), I3, "metal", (0.05, 0.07, 0.07), 0.1, bevel=0)
    for k in range(4):
        flat(B, V(-0.6, 0.33, 1.05 + k * 0.07), (0.3, 0.01, 0.02), I3, "metal", C_DARK, 0.1)                 # vents on its back
    crack(B, V(-0.62, -0.036, 1.22), (0, -1, 0), 0.18, 163, w=0.006, col=(0.8, 0.85, 0.85), steps=3, mat="metal")
    B.pipe([V(-0.6, 0.33, 1.0), V(-0.55, 0.45, 0.93), V(-0.3, 0.46, 0.5), V(-0.2, 0.5, 0.02)], 0.012, 4, "rubber", C_BLACK)  # its cable
    B.box(V(1.0, 0.18, 0.96), (0.5, 0.4, 0.05), I3, "metal", C_STEEL, 0.15)                               # sink
    flat(B, V(1.0, 0.18, 0.987), (0.42, 0.32, 0.01), I3, "metal", (0.1, 0.12, 0.11), 0.1)
    B.pipe([V(1.0, 0.38, 0.93), V(1.0, 0.38, 1.18), V(1.0, 0.24, 1.2)], 0.015, 5, "steel", C_STEEL)       # tap
    for k in range(5):
        a = random.uniform(0, 3); p = V(random.uniform(-1.2, 1.2), random.uniform(-0.3, 0.3), 0.934)
        decal(B, p, ZV, 0.21, 0.3, jit(C_PAPER, 0.08), spin=a, off=0.0)
    for k in range(4):                                                       # papers blown under the bench
        decal(B, V(random.uniform(-1.2, 1.2), random.uniform(-0.3, 0.3), 0.0), ZV, 0.21, 0.3, jit(C_PAPER, 0.1), spin=random.uniform(0, 3))
    moss(B, Vector((1.1, 0.2, 0.93)), 0.3, 160)
    for k in range(2):
        vine(B, Vector((1.45, 0.4 - k * 0.8, 0.93)), 0.9, 161 + k)
    grass(B, V(1.45, -0.35, 0), 164, 5)
    done(B, "Mesh", coll, h=0.5, k=0.35); return coll

def build_flicker_terminal():
    coll = new("FlickerTerminal"); B = Builder()
    B.box(Vector((0, 0, 0.6)), (0.8, 0.6, 1.2), I3, "panel", C_WHITE, 0.25, rust=0.4)
    B.box(Vector((0, 0, 0.04)), (0.84, 0.64, 0.08), I3, "metal", C_DARK, 0.2)                               # plinth
    B.box(Vector((0, -0.05, 1.45)), (0.9, 0.5, 0.6), Matrix.Rotation(-0.25, 3, 'X'), "panel", C_WHITE, 0.25, rust=0.3)
    kb = Matrix.Rotation(0.3, 3, 'X')
    B.box(Vector((0, -0.2, 1.1)), (0.7, 0.3, 0.05), kb, "metal", C_DARK, 0.15)                               # keyboard
    for r in range(3):
        flat(B, V(0, -0.2, 1.1) + kb @ V(0, -0.08 + r * 0.08, 0.028), (0.6, 0.05, 0.008), kb, "metal", (0.3, 0.3, 0.3), 0.2)
    for k in range(5):                                                        # vents on the side
        flat(B, V(0.402, 0.05, 0.3 + k * 0.08), (0.01, 0.4, 0.03), I3, "metal", C_DARK, 0.1)
    plate(B, V(0, -0.3, 0.55), (0, -1, 0), 0.6, 0.7, 0.02, "panel", jit(C_WHITE, 0.05))                    # service hatch
    for k in range(3):
        B.box(Vector((random.uniform(-0.3, 0.3), -0.29, 1.4 + random.uniform(-0.1, 0.1))), (0.3, 0.004, 0.01), Matrix.Rotation(random.uniform(-1, 1), 3, 'Y'),
              "panel", (0.9, 0.9, 0.9), 0.05, bevel=0)                      # screen cracks
    B.pipe([V(0.2, 0.3, 0.3), V(0.25, 0.33, 0.1), V(0.3, 0.36, 0.02)], 0.02, 5, "rubber", C_BLACK)            # power cable
    streak(B, V(-0.25, -0.3, 1.2), (0, -1, 0), 0.5, 0.05, C_RUST, C_WHITE)
    moss(B, Vector((0.3, 0.1, 1.72)), 0.2, 170)
    done(B, "Mesh", coll, h=0.5, k=0.35)
    S = Builder()
    S.box(Vector((0, 0, 0)), (0.7, 0.01, 0.42), I3, "cyan", (0.3, 1.0, 0.6), 0.05)
    scr = mnode(S, "Screen", coll, Vector((0, -0.28, 1.45)))
    scr.rotation_euler = (-0.25, 0, 0)
    keys(scr, (1, 20, 21, 23, 24, 50, 51, 53, 60, 61, 91), "scale",
         [Vector((1, 1, 1)), Vector((1, 1, 1)), Vector((0.01, 1, 0.01)), Vector((1, 1, 1)), Vector((0.01, 1, 0.01)), Vector((0.01, 1, 0.01)),
          Vector((1, 1, 1)), Vector((1, 1, 0.3)), Vector((1, 1, 1)), Vector((1, 1, 1)), Vector((1, 1, 1))])
    return coll

def build_monitor_bank():
    coll = new("MonitorBank"); B = Builder()
    B.box(Vector((0, 0.3, 1.5)), (4.0, 0.4, 3.0), I3, "metal", C_DARK, 0.2, rust=0.4)
    for j in range(4):                                                       # shelf rails between the rows
        flat(B, V(0, 0.1, 0.33 + j * 0.8), (3.8, 0.36, 0.06), I3, "metal", C_GREY, 0.2, rust=0.5)
    for x in (-1.95, 1.95):
        B.box(V(x, 0.1, 1.5), (0.1, 0.4, 3.0), I3, "metal", C_GREY, 0.2, rust=0.5)
    for k in range(8):                                                       # vents in the cabinet top
        decal(B, V(-1.4 + k * 0.4, 0.3, 3.0), ZV, 0.25, 0.3, (0.02, 0.02, 0.02), "metal")
    screens = []
    for i in range(5):
        for j in range(3):
            c = Vector((-1.6 + i * 0.8, 0.08, 0.7 + j * 0.8))
            if (i, j) == (3, 0):                                             # one fell off the shelf
                screens.append(c)
                continue
            plate(B, c, (0, -1, 0), 0.7, 0.6, 0.3, "panel", jit(C_WHITE, 0.1), 0.2, rust=0.3, bev=0.02, seam=False)   # open-backed housing
            decal(B, c + V(0.28, -0.15, -0.26), (0, -1, 0), 0.03, 0.02, C_AMBER if (i + j) % 2 else (0.3, 1.0, 0.4), "glow", off=0.002)   # power LEDs
            screens.append(c)
    c = screens[9]                                                           # its empty slot: bracket and torn leads
    flat(B, c + V(0, 0.1, -0.28), (0.3, 0.3, 0.03), Matrix.Rotation(0.2, 3, 'X'), "metal", C_GREY, 0.2, rust=0.6)
    B.pipe([c + V(-0.1, 0.12, 0.1), c + V(-0.12, 0.0, -0.1), c + V(-0.05, -0.05, -0.25)], 0.012, 4, "rubber", C_BLACK)
    B.pipe([c + V(0.1, 0.12, 0.05), c + V(0.14, 0.02, -0.15)], 0.012, 4, "rubber", (0.5, 0.08, 0.05))
    for k in range(4):                                                       # cable bundles hanging below the shelves
        x = -1.7 + k * 1.1
        B.pipe([V(x, 0.2, 0.3), V(x + 0.15, 0.0, 0.18), V(x + 0.3, 0.15, 0.3)], 0.025, 5, "rubber", random.choice([C_BLACK, (0.1, 0.1, 0.35)]))
    vine(B, V(-1.9, -0.05, 3.0), 1.8, 182); vine(B, V(1.2, -0.05, 3.0), 1.1, 183)
    done(B, "Mesh", coll, h=0.6, k=0.35)
    D = Builder()                                                            # dead screens
    L = Builder()                                                            # lit screens
    for k, c in enumerate(screens):
        if k == 9:
            continue
        (L if k % 3 == 0 else D).box(c + Vector((0, -0.16, 0)), (0.56, 0.01, 0.44), I3, "cyan" if k % 3 == 0 else "metal",
                                     (0.3, 0.9, 1.0) if k % 3 == 0 else (0.05, 0.06, 0.07), 0.05, bevel=0)
    done(D, "Dead", coll, h=0)
    lit = mnode(L, "Lit", coll, Vector((0, 0, 0)))
    keys(lit, (1, 40, 41, 44, 45, 121), "scale", [Vector((1, 1, 1)), Vector((1, 1, 1)), Vector((1, 1, 0.97)), Vector((1, 1, 0.97)), Vector((1, 1, 1)), Vector((1, 1, 1))])
    Sp = Builder()                                                           # one sparking screen
    for k in range(6):
        a = k / 6 * math.tau
        Sp.box(Vector((math.cos(a) * 0.15, -0.2, math.sin(a) * 0.15)), (0.12, 0.01, 0.02), Matrix.Rotation(-a, 3, 'Y'), "glow", C_AMBER, 0.05, bevel=0)
    spark = mnode(Sp, "Spark", coll, screens[7] + Vector((0, -0.05, 0)))
    keys(spark, (1, 30, 32, 35, 36, 80, 82, 84, 121), "scale", [Vector((0.01, 0.01, 0.01))] * 2 + [Vector((1, 1, 1)), Vector((0.01, 0.01, 0.01))] * 2 +
         [Vector((1.3, 1, 1.3)), Vector((0.01, 0.01, 0.01)), Vector((0.01, 0.01, 0.01))])
    return coll

def build_filing_cabinets():
    coll = new("FilingCabinets"); B = Builder()
    for k in range(3):
        x = -1.0 + k * 0.7
        B.box(Vector((x, 0, 0.7)), (0.6, 0.7, 1.4), I3, "metal", jit(C_GREY, 0.06), 0.2, rust=0.5)
        for j in range(3):
            d = 0.25 if (k, j) in ((0, 1), (2, 0)) else 0
            B.box(Vector((x, -0.36 - d, 0.3 + j * 0.42)), (0.52, 0.04 + d * 2, 0.36), I3, "metal", (0.5, 0.52, 0.5), 0.2, rust=0.4)
            flat(B, V(x, -0.385 - d * 2, 0.4 + j * 0.42), (0.18, 0.02, 0.03), I3, "metal", C_STEEL, 0.1)     # pull
            decal(B, V(x, -0.38 - d * 2, 0.34 + j * 0.42), (0, -1, 0), 0.12, 0.05, (0.78, 0.76, 0.66), "metal", off=0.001)  # label slot
        streak(B, V(x - 0.15, -0.35, 1.4), (0, -1, 0), 0.6, 0.05, C_RUST, C_GREY, "metal")
    B.box(Vector((1.4, -0.4, 0.3)), (1.4, 0.7, 0.6), Matrix.Rotation(0.3, 3, 'Z'), "metal", C_GREY, 0.2, rust=0.6)   # toppled
    dr = Matrix.Rotation(0.9, 3, 'Z')
    B.box(V(0.7, -1.1, 0.13), (0.52, 0.6, 0.26), dr, "metal", (0.5, 0.52, 0.5), 0.2, rust=0.5)             # a drawer pulled out and dropped
    flat(B, V(0.7, -1.1, 0.26), (0.46, 0.54, 0.01), dr, "metal", (0.1, 0.1, 0.1), 0.1)
    for k in range(14):
        decal(B, Vector((random.uniform(-1.4, 2.2), random.uniform(-1.6, -0.4), 0.0)), ZV, 0.21, 0.3, jit(C_PAPER, 0.1), spin=random.uniform(0, 3), off=0.003 + k * 0.0004)
    grass(B, V(-1.35, -0.3, 0), 186, 5)
    done(B, "Mesh", coll, h=0.5, k=0.35); return coll

def build_cryo_pod():
    coll = new("CryoPod"); B = Builder(); G = Builder()
    B.box(Vector((0, 0.1, 0.2)), (1.4, 1.2, 0.4), I3, "metal", C_DARK, 0.2, rust=0.3)
    B.tube_rings([quad_ring(0, 0, 0.4, 0.6, 0.5), quad_ring(0, 0, 2.3, 0.6, 0.5)], "panel", C_WHITE, 0.2, 0.3, cap=False)
    B.box(Vector((0, 0.1, 2.45)), (1.4, 1.2, 0.3), I3, "panel", C_WHITE, 0.2, rust=0.2)
    for sx in (-1, 1):                                                       # door frame and side ribs
        B.box(V(sx * 0.55, -0.5, 1.35), (0.1, 0.06, 1.9), I3, "metal", C_DARK, 0.2)
        for z in (0.9, 1.8):
            flat(B, V(sx * 0.605, 0, z), (0.02, 0.9, 0.08), I3, "metal", C_GREY, 0.2, rust=0.3)
    for z in (0.43, 2.27):
        B.box(V(0, -0.5, z), (1.2, 0.06, 0.08), I3, "metal", C_DARK, 0.2)
    for z in (0.7, 1.9):                                                     # hinges
        B.cyl(V(0.62, -0.52, z - 0.08), V(0.62, -0.52, z + 0.08), 0.03, 6, "steel", C_STEEL)
    G.box(Vector((0, -0.49, 1.35)), (1.0, 0.02, 1.7), I3, "glass", C_GLASS, 0.02, bevel=0)
    for k in range(4):                                                       # cracks in the glass
        B.box(Vector((random.uniform(-0.3, 0.3), -0.5, random.uniform(0.9, 1.9))), (0.4, 0.004, 0.012), Matrix.Rotation(random.uniform(-1, 1), 3, 'Y'),
              "panel", (0.95, 0.95, 0.95), 0.05, bevel=0)
    B.box(V(0.35, -0.52, 0.28), (0.4, 0.04, 0.16), I3, "metal", C_DARK, 0.2)                              # status panel on the base
    for k, col in enumerate(((1.0, 0.15, 0.05), C_AMBER, (0.3, 1.0, 0.4))):
        flat(B, V(0.22 + k * 0.12, -0.542, 0.28), (0.06, 0.004, 0.05), I3, "glow", col, 0.05)
    hazard(B, Vector((-0.65, -0.502, 0.08)), (1, 0, 0), (0, 0, 1), 0.7, 0.12, (0, -1, 0), pitch=0.07)
    decal(B, V(0, -0.505, 2.45), (0, -1, 0), 0.5, 0.1, (0.12, 0.3, 0.45), "panel")                        # faded name band
    B.pipe([Vector((0.6, 0.4, 0.3)), Vector((1.1, 0.8, 0.1)), Vector((1.6, 0.9, 0.05))], 0.06, 8, "rubber", C_BLACK)
    for k in range(3):                                                       # hose clamps
        ring(B, V(0.6, 0.4, 0.3).lerp(V(1.1, 0.8, 0.1), 0.3 + k * 0.3), (1, 0.8, -0.4), 0.06, 0.068, 0.03, 8, "steel", C_STEEL)
    B.pipe([V(-0.55, 0.55, 2.3), V(-0.55, 0.62, 1.3), V(-0.5, 0.62, 0.45)], 0.03, 6, "metal", C_GREY)    # coolant line down the back
    streak(B, V(-0.2, -0.504, 2.3), (0, -1, 0), 0.8, 0.06, C_RUST, C_WHITE, "panel")
    for k in range(3):
        vine(B, Vector((random.uniform(-0.6, 0.6), 0.5, 2.6)), random.uniform(1.0, 2.0), 180 + k)
    moss(B, Vector((0, 0.3, 2.6)), 0.4, 185)
    blob(B, V(1.0, 0.5, 0), 0.45, (0.16, 0.2, 0.22), 8, seed=1.0)          # coolant puddle under the hose
    done(B, "Mesh", coll, h=0.5, k=0.35); done(G, "Glass", coll, h=0)
    I = Builder()
    I.box(Vector((0, 0, 0)), (0.9, 0.8, 1.6), I3, "field_zp", C_CYAN, 0.02, bevel=0)
    inner = mnode(I, "Frost", coll, Vector((0, 0.05, 1.35)))
    keys(inner, (1, 60, 62, 64, 66, 121), "scale", [Vector((1, 1, 1)), Vector((1, 1, 1)), Vector((0.2, 0.2, 0.2)), Vector((1, 1, 1)), Vector((0.2, 0.2, 0.2)), Vector((1, 1, 1))])
    return coll

def build_observation_booth():
    coll = new("ObservationBooth"); B = Builder(); G = Builder()
    for (x, y) in ((-1.5, -1.0), (1.5, -1.0), (1.5, 1.0), (-1.5, 1.0)):
        B.box(Vector((x, y, 1.75)), (0.15, 0.15, 3.5), I3, "metal", C_DARK, 0.2, rust=0.4)
    B.box(Vector((0, 0, 3.5)), (3.2, 2.2, 0.2), I3, "panel", C_WHITE, 0.25, rust=0.3)
    B.box(V(0, 0, 3.64), (3.0, 2.0, 0.08), I3, "metal", C_DARK, 0.2, rust=0.5)                          # roof deck
    B.box(V(0.9, 0.4, 3.78), (0.6, 0.5, 0.25), I3, "metal", C_GREY, 0.2, rust=0.5)                      # vent unit
    for k in range(4):
        flat(B, V(0.9, 0.144, 3.7 + k * 0.05), (0.5, 0.01, 0.02), I3, "metal", C_DARK, 0.1)
    B.box(Vector((0, 0, 0.5)), (3.2, 2.2, 1.0), I3, "panel", C_WHITE, 0.25, rust=0.3)
    for x in (-1.5, -0.75, 0, 0.75, 1.5):                                    # panel seams on the front of the base
        flat(B, V(x, -1.102, 0.5), (0.015, 0.004, 0.96), I3, "panel", (0.3, 0.3, 0.3), 0.1)
    B.box(V(0, -1.0, 1.04), (3.1, 0.2, 0.08), I3, "metal", C_DARK, 0.2)                                  # window sill
    B.box(V(0, -1.0, 3.36), (3.1, 0.12, 0.08), I3, "metal", C_DARK, 0.2)                                 # head
    B.box(V(0, -1.0, 2.25), (0.08, 0.1, 2.3), I3, "metal", C_DARK, 0.2)                                  # mullion
    G.box(Vector((-0.75, -1.0, 2.25)), (1.4, 0.03, 2.3), I3, "glass", C_GLASS, 0.02, bevel=0)           # one pane left
    for k in range(8):                                                       # shattered glass on the floor
        a = random.uniform(0, math.tau)
        G.box(Vector((0.7 + random.uniform(-0.6, 0.6), -1.6 + random.uniform(-0.5, 0.4), 0.01)), (random.uniform(0.1, 0.3), random.uniform(0.1, 0.3), 0.01),
              Matrix.Rotation(a, 3, 'Z'), "glass", C_GLASS, 0.02, bevel=0)
    for k in range(4):
        B.box(Vector((0.75 + random.uniform(-0.6, 0.6), -1.0, 1.1 + random.uniform(0, 0.3))), (0.2, 0.03, 0.12), Matrix.Rotation(random.uniform(-1, 1), 3, 'Y'),
              "glass", C_GLASS, 0.02, bevel=0)                               # jagged shards in the frame
    B.box(Vector((0, 0.5, 1.1)), (2.4, 0.6, 0.1), I3, "metal", C_DARK, 0.15)                               # console
    ck = Matrix.Rotation(0.35, 3, 'X')
    B.box(V(0, 0.55, 1.28), (2.2, 0.35, 0.25), ck, "panel", C_GREY, 0.2, rust=0.3)
    for k in range(9):
        col = random.choice([C_AMBER, (1.0, 0.15, 0.05), (0.3, 1.0, 0.4), (0.05, 0.05, 0.05), (0.05, 0.05, 0.05)])
        flat(B, V(-0.9 + k * 0.22, 0.55, 1.28) + ck @ V(0, 0, 0.127), (0.1, 0.07, 0.006), ck, "glow" if sum(col) > 0.5 else "metal", col, 0.05)
    for sx in (-1, 1):
        streak(B, V(sx * 1.2, -1.105, 3.4), (0, -1, 0), 1.0, 0.07, C_RUST, C_WHITE, "panel")
    for k in range(3):
        vine(B, Vector((random.uniform(-1.4, 1.4), -1.0, 3.4)), 2.5, 190 + k)
    moss(B, V(-1.2, 0.6, 3.66), 0.35, 194)
    grass(B, V(1.3, -1.4, 0), 195)
    done(B, "Mesh", coll, h=0.6, k=0.35); done(G, "Glass", coll, h=0); return coll

def build_pipe_cluster():
    coll = new("PipeCluster"); B = Builder()
    for k in range(5):
        y = -0.1 * k; r = 0.12 + 0.04 * (k % 3)
        z = 0.5 + k * 0.45
        B.cyl(Vector((-3, y, z)), Vector((3, y, z)), r, 12, "metal", jit(C_RUST if k % 2 else (0.4, 0.42, 0.4), 0.1), 0.2, rust=0.7)
        for x in (-2.0, 0.5, 2.5):
            ring(B, Vector((x, y, z)), (1, 0, 0), r, r + 0.04, 0.12, 12, "metal", C_DARK, 0.2, rust=0.5)
            for a in (0.8, 2.4, 3.9, 5.5):                                   # flange bolts
                stud(B, V(x - 0.061, y + math.cos(a) * (r + 0.02), z + math.sin(a) * (r + 0.02)), (-1, 0, 0), 0.014, 0.012, C_STEEL, "metal", 4)
            streak(B, V(x, y - r - 0.04, z), (0, -1, 0), 0.25 + 0.1 * (k % 2), 0.05, C_RUST, C_DARK, "metal")
        for x in (-2.5, 2.5):                                                # clamp and standoff to the posts
            ring(B, V(x, y, z), (1, 0, 0), r, r + 0.015, 0.05, 12, "metal", C_GREY, 0.2, rust=0.4)
            flat(B, V(x, (y + r + 0.1) / 2 + 0.01, z), (0.05, 0.1 - y - r + 0.02, 0.05), I3, "metal", C_GREY, 0.2, rust=0.4)
    for x in (-2.5, 2.5):
        B.box(Vector((x, 0.1, 1.4)), (0.15, 0.15, 2.8), I3, "metal", C_DARK, 0.2, rust=0.6)
        B.box(V(x, 0.1, 0.02), (0.35, 0.35, 0.04), I3, "metal", C_RUST, 0.2, rust=0.8)
    B.cyl(Vector((1.0, -0.2, 1.4)), Vector((1.0, -0.5, 1.4)), 0.05, 8, "steel", C_STEEL)                      # valve wheel
    ring(B, Vector((1.0, -0.52, 1.4)), (0, 1, 0), 0.18, 0.22, 0.03, 16, "panel", (0.7, 0.1, 0.05), 0.1)
    for a in (0, math.pi / 2):
        flat(B, V(1.0, -0.52, 1.4), (0.38, 0.02, 0.03), Matrix.Rotation(a, 3, 'Y'), "panel", (0.7, 0.1, 0.05), 0.1)
    decal(B, V(-0.3, -0.425, 1.85 + 0.0), (0, -1, 0), 0.5, 0.08, (0.2, 0.45, 0.2), "metal")                 # colour band
    B.cyl(Vector((-1.0, -0.3, 1.85)), Vector((-1.0, -0.3, 1.7)), 0.12, 10, "metal", C_RUST, 0.2, rust=0.9)    # the broken stub
    B.box(Vector((-1.0, -0.3, 0.005)), (0.9, 0.7, 0.01), I3, "glass", C_WATER, 0.05, bevel=0)                 # puddle under it
    blob(B, V(-1.0, -0.3, 0), 0.42, (0.12, 0.1, 0.07), 10, seed=4.0, off=0.002)                                # rust-stained floor
    moss(B, Vector((1.8, -0.1, 0.35)), 0.45, 200)
    grass(B, V(-2.6, -0.3, 0), 201)
    done(B, "Mesh", coll, h=0.5, k=0.35)
    for k in range(2):                                                       # drips
        D = Builder()
        D.cyl(Vector((0, 0, -0.03)), Vector((0, 0, 0.03)), 0.025, 6, "glass", (0.5, 0.8, 0.9), 0.02)
        ob = mnode(D, f"Drip{k}", coll, Vector((-1.0, -0.3, 1.65)))
        o = 1 + k * 30
        keys(ob, (1, o, o + 18, o + 19, 61), "location",
             [Vector((-1.0, -0.3, 1.65))] * 2 + [Vector((-1.0, -0.3, 0.02)), Vector((-1.0, -0.3, 1.65)), Vector((-1.0, -0.3, 1.65))], linear=True)
    return coll

def build_hanging_lamp():
    coll = new("HangingLamp"); B = Builder()
    B.box(Vector((0, 0, 6.0)), (0.6, 0.6, 0.1), I3, "metal", C_DARK, 0.2)
    B.cyl(V(0, 0, 5.95), V(0, 0, 5.85), 0.05, 6, "metal", C_DARK)
    done(B, "Mount", coll, h=0)
    L = Builder()
    L.pipe([Vector((0, 0, 0)), Vector((0, 0, -2.5))], 0.02, 5, "rubber", C_BLACK)
    L.cyl(V(0, 0, -2.42), V(0, 0, -2.52), 0.06, 8, "metal", C_DARK)                                         # strain relief
    L.tube_rings([[Vector((math.cos(a) * r, math.sin(a) * r, z)) for a in [i / 16 * math.tau for i in range(16)]] for r, z in ((0.15, -2.5), (0.42, -2.75), (0.5, -2.85))],
                 "metal", (0.3, 0.33, 0.3), 0.2, 0.5, cap=False, smooth=True)
    L.cyl(Vector((0, 0, -2.6)), Vector((0, 0, -2.75)), 0.12, 12, "lamp", C_LAMP, 0.02)
    lamp = mnode(L, "Lamp", coll, Vector((0, 0, 5.95)))
    cycle(lamp, "rotation_euler", [(0.12, 0.04, 0), (-0.1, -0.05, 0)], 120)
    return coll

def build_ceiling_fan():
    coll = new("CeilingFan"); B = Builder()
    ring(B, Vector((0, 0, 6.0)), (0, 0, 1), 2.1, 2.3, 0.3, 32, "metal", C_RUST, 0.2, rust=0.6)
    for a in (0, math.pi / 2, math.pi, 3 * math.pi / 2):
        B.box(Vector((math.cos(a) * 1.05, math.sin(a) * 1.05, 6.0)), (2.1, 0.08, 0.08), Matrix.Rotation(a, 3, 'Z'), "metal", C_DARK, 0.2)
        flat(B, V(math.cos(a) * 2.2, math.sin(a) * 2.2, 6.0), (0.3, 0.26, 0.34), Matrix.Rotation(a, 3, 'Z'), "metal", C_DARK, 0.2, rust=0.5)  # mount lugs
    for k in range(4):                                                       # a guard grille across the duct
        flat(B, V(0, -1.5 + k * 1.0, 6.2), (4.2 * math.sqrt(max(0.05, 1 - ((-1.5 + k * 1.0) / 2.1) ** 2)), 0.03, 0.03), I3, "metal", C_DARK, 0.2)
    done(B, "Frame", coll, h=0)
    F = Builder()
    F.cyl(Vector((0, 0, -0.15)), Vector((0, 0, 0.15)), 0.3, 12, "metal", C_DARK)
    F.cyl(Vector((0, 0, -0.25)), Vector((0, 0, -0.15)), 0.18, 8, "metal", C_GREY)                          # hub cap
    for k in range(5):                                                       # one of six blades missing
        a = k / 6 * math.tau
        F.box(Vector((math.cos(a) * 1.0, math.sin(a) * 1.0, 0)), (1.6, 0.4, 0.03), Matrix.Rotation(a, 3, 'Z') @ Matrix.Rotation(0.3, 3, 'X'),
              "metal", jit((0.4, 0.4, 0.38), 0.1), 0.2, rust=0.5)
    flat(F, V(math.cos(5 / 6 * math.tau) * 0.4, math.sin(5 / 6 * math.tau) * 0.4, 0), (0.25, 0.3, 0.04), Matrix.Rotation(5 / 6 * math.tau, 3, 'Z'), "metal", C_RUST, 0.2)  # sheared root
    fan = mnode(F, "Fan", coll, Vector((0, 0, 6.0)))
    spin(fan, 2, 1, 240)
    return coll

def barrel(B, p, q, r, col, rust=0.8):
    """Drum from p to q: body, rolling hoops, rim and lid with a bung."""
    ax = (q - p).normalized()
    B.cyl(p, q, r, 16, "metal", jit(col, 0.1), 0.2, rust=rust)
    for t in (0.3, 0.7):
        ring(B, p.lerp(q, t), ax, r, r + 0.02, 0.04, 16, "metal", C_DARK, 0.2)
    ring(B, q - ax * 0.015, ax, r - 0.02, r + 0.01, 0.03, 16, "metal", col, 0.2, rust=rust)
    R = rot_to(ax)
    stud(B, q + R @ V(r * 0.55, 0, 0), ax, 0.035, 0.015, C_DARK)                                         # bung

def build_rusted_barrels():
    coll = new("RustedBarrels"); B = Builder()
    for (x, y) in ((-0.5, 0), (0.2, 0.45)):
        barrel(B, V(x, y, 0), V(x, y, 1.1), 0.35, random.choice([C_RUST, (0.2, 0.3, 0.45), (0.5, 0.35, 0.1)]))
        for a in (-2.0, -1.2):
            streak(B, V(x + math.cos(a) * 0.352, y + math.sin(a) * 0.352, 1.08), (math.cos(a), math.sin(a), 0), 0.5, 0.06, C_RUST, C_DARK, "metal")
    barrel(B, V(0.4, -0.5, 0.35), V(1.5, -0.8, 0.35), 0.35, C_RUST, 0.9)                                        # tipped
    B.box(Vector((1.9, -0.9, 0.005)), (1.2, 0.9, 0.01), I3, "glow", (0.5, 0.9, 0.2), 0.1, bevel=0)             # something glowing leaked out
    blob(B, V(1.9, -0.9, 0), 0.45, (0.2, 0.3, 0.08), 10, seed=6.0, off=0.001)
    hazard(B, Vector((-0.85, -0.36, 0.5)), (1, 0, 0), (0, 0, 1), 0.7, 0.2, (0, -1, 0), pitch=0.08)
    moss(B, Vector((-0.3, 0.6, 0.02)), 0.4, 210)
    grass(B, V(0.6, 0.7, 0), 211, 5)
    done(B, "Mesh", coll, h=0.5, k=0.4); return coll

def build_crate_stack():
    coll = new("CrateStack"); B = Builder()
    for (x, y, z, s) in ((0, 0, 0.5, 1.0), (1.1, 0.1, 0.45, 0.9), (0.5, 0.05, 1.45, 0.9), (-1.0, 0.4, 0.4, 0.8)):
        R = Matrix.Rotation(random.uniform(-0.1, 0.1), 3, 'Z')
        c = Vector((x, y, z))
        B.box(c, (s, s, s), R, "wood", jit((0.45, 0.32, 0.18), 0.12), 0.25, bevel=0.02)
        for d in (-1, 1):                                                    # battens on the front and sides
            flat(B, c + R @ V(0, -s / 2 - 0.01, d * s * 0.35), (s, 0.02, 0.1), R, "wood", (0.3, 0.2, 0.1), 0.2)
            flat(B, c + R @ V(d * (s / 2 + 0.01), 0, 0), (0.02, s * 0.9, 0.1), R, "wood", (0.3, 0.2, 0.1), 0.2)
        decal(B, c + R @ V(0, -s / 2 - 0.001, 0), R @ V(0, -1, 0), s * 0.4, s * 0.18, (0.12, 0.1, 0.08), spin=0.0)   # stencil
        for sx in (-1, 1):
            for sz in (-1, 1):
                stud(B, c + R @ V(sx * s * 0.44, -s / 2 - 0.02, sz * s * 0.35), R @ V(0, -1, 0), 0.012, 0.008, C_DARK, "metal", 4)
    for k in range(6):                                                       # a broken one: planks spilled
        B.box(Vector((-1.8 + random.uniform(-0.4, 0.4), -0.6 + random.uniform(-0.4, 0.4), 0.03)), (0.9, 0.15, 0.03), Matrix.Rotation(random.uniform(0, 3), 3, 'Z'),
              "wood", jit((0.45, 0.32, 0.18), 0.12), 0.2, bevel=0.005)
    fern(B, Vector((-1.8, -0.4, 0)), 220, 0.6)
    done(B, "Mesh", coll, h=0.5, k=0.4); return coll

def build_puddle_debris():
    coll = new("PuddleDebris"); B = Builder()
    rings_ = [[Vector((math.cos(a) * r * (1 + 0.2 * math.sin(3 * a)), math.sin(a) * r * 0.7, 0.01)) for a in [i / 24 * math.tau for i in range(24)]] for r in (2.0,)]
    B.tube_rings(rings_ + [[p + Vector((0, 0, 0.005)) for p in rings_[0]]], "glass", C_WATER, 0.02, 0.0, cap=True, smooth=False)
    card(B, [V(math.cos(a) * 2.25 * (1 + 0.2 * math.sin(3 * a)), math.sin(a) * 1.65, 0.004) for a in [i / 24 * math.tau for i in range(24)]],
         (0.12, 0.13, 0.1), "rock", 0.1)                                      # wet dark rim around the water
    for k in range(10):
        a = random.uniform(0, 3)
        decal(B, V(random.uniform(-1.5, 1.5), random.uniform(-1, 1), 0.02), ZV, 0.12, 0.08,
              jit(random.choice([C_LEAF, (0.5, 0.4, 0.15)]), 0.1), spin=a, off=0.0)
    for k in range(3):
        chunk(B, Vector((random.uniform(-1.5, 1.5), random.uniform(-1, 1), 0.08)), 0.18, 230 + k)
    decal(B, V(0.6, 0.3, 0.018), ZV, 0.21, 0.3, (0.7, 0.68, 0.6), spin=0.7, off=0.0)                     # soaked paper
    grass(B, V(2.0, 0.4, 0), 233); grass(B, V(-2.0, -0.5, 0), 234, 5)
    done(B, "Mesh", coll, h=0); return coll

def build_tipped_barriers():
    coll = new("TippedBarriers"); B = Builder()
    for k, (x, a, up) in enumerate(((-1.2, 0.2, True), (0.2, -0.4, False), (1.5, 1.2, False))):
        rot = Matrix.Rotation(a, 3, 'Z') @ (I3 if up else Matrix.Rotation(1.45, 3, 'X'))
        base = Vector((x, 0, 0.5 if up else 0.12))
        B.box(base, (1.2, 0.1, 0.25), rot, "panel", C_WHITE, 0.2, rust=0.3)
        hazard(B, base + rot @ V(-0.58, -0.051, -0.1), rot @ V(1, 0, 0), rot @ V(0, 0, 1), 1.16, 0.2, rot @ V(0, -1, 0), pitch=0.12)
        for d in (-0.45, 0.45):
            flat(B, base + rot @ Vector((d, 0, -0.3)), (0.06, 0.4, 0.6), rot, "metal", C_DARK, 0.2)
            flat(B, base + rot @ Vector((d, 0, -0.58)), (0.1, 0.5, 0.04), rot, "metal", C_DARK, 0.2)       # feet
    for k in range(3):                                                       # cones with a reflective band
        p = Vector((random.uniform(-1.5, 1.5), -1.0 + random.uniform(-0.3, 0.3), 0))
        rr = lambda r, z: [p + Vector((math.cos(t) * r, math.sin(t) * r, z)) for t in [i / 10 * math.tau for i in range(10)]]
        flat(B, p + V(0, 0, 0.015), (0.46, 0.46, 0.03), I3, "panel", (0.9, 0.4, 0.05), 0.1, 0.2)
        B.tube_rings([rr(0.22, 0.03), rr(0.14, 0.3)], "panel", (0.9, 0.4, 0.05), 0.1, 0.2, cap=False, smooth=True)
        B.tube_rings([rr(0.14, 0.3), rr(0.1, 0.42)], "panel", (0.85, 0.85, 0.82), 0.1, 0.1, cap=False, smooth=True)
        B.tube_rings([rr(0.1, 0.42), rr(0.03, 0.6)], "panel", (0.9, 0.4, 0.05), 0.1, 0.2, cap=True, smooth=True)
    done(B, "Mesh", coll, h=0.3, k=0.3); return coll

def build_elevator_ruin():
    coll = new("ElevatorRuin"); B = Builder(); G = Builder()
    for k in range(6):                                                       # glass tube, top half shattered away
        a = k / 6 * math.tau
        B.box(Vector((math.cos(a) * 1.3, math.sin(a) * 1.3, 3.0)), (0.12, 0.12, 6.0), Matrix.Rotation(a, 3, 'Z'), "metal", C_DARK, 0.2, rust=0.4)
        streak(B, V(math.cos(a) * 1.365, math.sin(a) * 1.365, 5.9), (math.cos(a), math.sin(a), 0), 1.6, 0.07, C_RUST, C_DARK, "metal")
    for z in (0.1, 3.0):
        ring(B, Vector((0, 0, z)), (0, 0, 1), 1.25, 1.4, 0.2, 24, "panel", C_WHITE, 0.2, rust=0.3)
    ring(B, V(0, 0, 5.95), (0, 0, 1), 1.25, 1.4, 0.12, 24, "metal", C_DARK, 0.2, rust=0.6)                # crown ring
    G.tube_rings([[Vector((math.cos(a) * 1.28, math.sin(a) * 1.28, z)) for a in [i / 24 * math.tau for i in range(24)]] for z in (0.2, 2.2)],
                 "glass", C_GLASS, 0.02, 0.0, cap=False, smooth=True)
    B.cyl(Vector((0, 0, 0.9)), Vector((0, 0, 1.0)), 1.1, 24, "panel", C_WHITE, 0.2, rust=0.4)
    B.box(Vector((0.3, 0.2, 1.4)), (1.8, 1.8, 0.08), Matrix.Rotation(0.35, 3, 'X'), "metal", C_DARK, 0.2, rust=0.5)   # tilted platform
    for k in range(3):                                                       # snapped hoist cables
        x = -0.3 + k * 0.3
        B.pipe([V(x, 0.2, 5.95), V(x + 0.05, 0.25, 4.0), V(x + 0.2, 0.3, 2.2)], 0.018, 4, "steel", C_STEEL)
    for k in range(6):                                                       # shards scattered round the base
        a = random.uniform(0, math.tau); r = random.uniform(0.9, 1.2)
        G.box(V(math.cos(a) * r, math.sin(a) * r, 0.21), (random.uniform(0.1, 0.25), random.uniform(0.1, 0.2), 0.01),
              Matrix.Rotation(random.uniform(0, 3), 3, 'Z'), "glass", C_GLASS, 0.02, bevel=0)
    for k in range(4):
        vine(B, Vector((math.cos(k) * 1.3, math.sin(k) * 1.3, 5.9)), random.uniform(3, 5), 240 + k)
    leaves(B, Vector((0, 0, 6.0)), 1.1, 245)
    done(B, "Mesh", coll, h=0.6, k=0.35); done(G, "Glass", coll, h=0); return coll

# ======================================================================================== superstructures
def ivy(B, c, r, seed, squash=(1, 1, 1.3)):
    rock(B, c, r, seed, 1, 0.4, "rock", lambda p, nn: jit(random.choice([C_LEAF, C_MOSS, C_LEAF2]), 0.15), squash)

def build_cooling_tower():
    coll = new("CoolingTower"); B = Builder()
    H, n = 42.0, 48
    def rad(z):
        t = z / H
        return 18.0 * (1 - 0.55 * math.sin(t * math.pi * 0.85)) + 2.0 * t
    rings_ = []
    NR = 16
    for k in range(NR + 1):
        z = H * k / NR
        pts = []
        for i in range(n):
            a = i / n * math.tau
            bz = 1 - max(0.0, 1 - abs(k - 10) / 1.6)                         # a collapsed breach
            ba = max(0.0, 1 - abs(i - 8.5) / 3.5)
            dent = 1 - 0.5 * (1 - bz) * ba
            pts.append(Vector((math.cos(a) * rad(z) * dent, math.sin(a) * rad(z) * dent, z)))
        rings_.append(pts)
    B.tube_rings(rings_, "rock", C_CONC, 0.18, 0.2, cap=False, smooth=True)
    ring(B, V(0, 0, H - 0.6), (0, 0, 1), rad(H) - 0.1, rad(H) + 0.6, 1.2, n, "rock", C_CONC_D, 0.15)      # thickened lip
    ring(B, V(0, 0, 3.4), (0, 0, 1), rad(3.4) - 0.1, rad(3.4) + 0.5, 0.8, n, "rock", C_CONC_D, 0.15)       # ring beam over the legs
    for k in range(24):                                                      # raking V-legs round the base
        a = k / 24 * math.tau
        for s in (-1, 1):
            p0 = V(math.cos(a) * 18.4, math.sin(a) * 18.4, 0.0)
            a1 = a + s * 0.07
            p1 = V(math.cos(a1) * rad(3.0), math.sin(a1) * rad(3.0), 3.2)
            d = p1 - p0
            B.box((p0 + p1) / 2, (0.9, 0.9, d.length), rot_to(d), "rock", C_CONC_D, 0.15, bevel=0.08)
        B.box(V(math.cos(a) * 18.4, math.sin(a) * 18.4, 0.3), (1.6, 1.6, 0.6), Matrix.Rotation(a, 3, 'Z'), "rock", C_CONC_D, 0.15, bevel=0.08)   # footing
    ring(B, V(0, 0, 0.5), (0, 0, 1), 19.6, 20.2, 1.0, 40, "rock", C_CONC_D, 0.15)                         # basin wall
    a0 = 2.4                                                                  # service ladder and riser up one side
    lad = [V(math.cos(a0) * (rad(z) + 0.5), math.sin(a0) * (rad(z) + 0.5), z) for z in [H * k / NR for k in range(NR + 1)]]
    for s in (-1, 1):
        off = V(-math.sin(a0), math.cos(a0), 0) * 0.35 * s
        B.pipe([p + off for p in lad], 0.05, 4, "metal", C_RUST)
    for k in range(0, len(lad) - 1):
        for t in (0.25, 0.75):
            p = lad[k].lerp(lad[k + 1], t)
            flat(B, p, (0.7, 0.06, 0.06), Matrix.Rotation(a0 + math.pi / 2, 3, 'Z'), "metal", C_RUST, 0.2)
    a1 = 3.3
    B.pipe([V(math.cos(a1) * (rad(z) + 1.0), math.sin(a1) * (rad(z) + 1.0), z) for z in [H * k / NR for k in range(NR + 1)]], 0.35, 8, "metal", C_GREY)
    for k in range(4):                                                       # big intake pipes into the basin
        a = 0.3 + k * 1.6
        B.cyl(V(math.cos(a) * 23.5, math.sin(a) * 23.5, 1.2), V(math.cos(a) * 19.8, math.sin(a) * 19.8, 1.2), 1.0, 14, "metal", C_RUST, 0.2, rust=0.8)
        ring(B, V(math.cos(a) * 22, math.sin(a) * 22, 1.2), (math.cos(a), math.sin(a), 0), 1.0, 1.15, 0.4, 14, "metal", C_DARK, 0.2)
    for k in range(8):                                                       # rebar hanging out of the breach
        a = (6 + k * 0.7) / n * math.tau; z = H * (9 + (k % 3) * 0.6) / NR
        base = V(math.cos(a) * rad(z) * 0.62, math.sin(a) * rad(z) * 0.62, z)
        B.pipe([base, base + V(math.cos(a), math.sin(a), -0.6) * 1.2, base + V(math.cos(a) * 1.5, math.sin(a) * 1.5, -2.2)], 0.08, 4, "rock", C_RUST)
    for k in range(30):                                                      # ivy climbing
        a = random.uniform(0, math.tau); z = random.uniform(0, 30)
        ivy(B, Vector((math.cos(a) * (rad(z) + 0.4), math.sin(a) * (rad(z) + 0.4), z)), random.uniform(1.0, 2.5), 300 + k)
    for k in range(10):
        chunk(B, Vector((math.cos(0.9) * 22 + random.uniform(-4, 4), math.sin(0.9) * 22 + random.uniform(-4, 4), 0.8)), random.uniform(0.8, 1.8), 330 + k)
    for k in range(8):
        vine(B, V(math.cos(k * 0.8) * (rad(H) + 0.7), math.sin(k * 0.8) * (rad(H) + 0.7), H - 0.8), random.uniform(6, 12), 360 + k)
    done(B, "Shell", coll, h=6.0, k=0.5, stain=0.35)
    for k in range(3):                                                       # steam plume, rising and fading
        St = Builder()
        rock(St, Vector((0, 0, 0)), 6.0, 340 + k, 1, 0.3, "field_cold", lambda p, nn: (0.8, 0.9, 1.0), (1, 1, 0.7))
        ob = mnode(St, f"Steam{k}", coll, Vector((0, 0, H)))
        o = 1 + k * 80
        keys(ob, (1, o, o + 239, o + 240, 241 + 240), "location", [Vector((0, 0, H)), Vector((0, 0, H)), Vector((2, 1, H + 30)), Vector((0, 0, H)), Vector((0, 0, H))], linear=True)
        keys(ob, (1, o, o + 120, o + 239, 481), "scale", [Vector((0.3, 0.3, 0.3)), Vector((0.3, 0.3, 0.3)), Vector((1.2, 1.2, 1.2)), Vector((0.05, 0.05, 0.05)), Vector((0.3, 0.3, 0.3))])
    return coll

def lattice(B, p0, p1, w, n, col, bar=0.25, mat="metal", rust=0.8, side=V(1, 0, 0)):
    """Zigzag bracing between two parallel chords from p0 to p1 (offset w along side)."""
    side = Vector(side).normalized()
    for k in range(n):
        a = p0.lerp(p1, k / n); b = p0.lerp(p1, (k + 1) / n) + side * w
        if k % 2:
            a, b = a + side * w, p0.lerp(p1, (k + 1) / n)
        d = b - a
        B.box((a + b) / 2, (bar, bar, d.length), rot_to(d), mat, col, 0.2, rust=rust, bevel=0.04)

def build_gantry_crane():
    coll = new("GantryCrane"); B = Builder()
    H, S = 32.0, 50.0
    for sx in (-1, 1):
        for sy in (-1, 1):                                                   # legs, braced
            B.box(Vector((sx * S / 2, sy * 6, H / 2)), (1.4, 1.4, H), I3, "metal", C_YELLOW, 0.25, rust=0.7, bevel=0.1)
            for z in (8, 16, 24):                                            # splice plates
                B.box(V(sx * S / 2, sy * 6, z), (1.5, 1.5, 0.4), I3, "metal", C_YELLOW, 0.25, rust=0.8, bevel=0.05)
            B.box(V(sx * S / 2, sy * 6, 1.5), (1.8, 1.8, 0.4), I3, "metal", C_DARK, 0.2, rust=0.6, bevel=0.05)
        for k in range(6):
            z = 3 + k * 5
            B.box(Vector((sx * S / 2, 0, z)), (0.5, 12.0, 0.5), Matrix.Rotation(0.35 * (1 if k % 2 else -1), 3, 'X'), "metal", C_RUST, 0.2, rust=0.8, bevel=0.05)
        B.box(Vector((sx * S / 2, 0, 0.6)), (4.0, 16.0, 1.2), I3, "metal", C_DARK, 0.2, rust=0.6, bevel=0.08)            # bogies
        for sy in (-1, 1):
            hazard(B, V(sx * S / 2 - 2.0, sy * 8.0 + sy * 0.001, 0.2), (1, 0, 0), (0, 0, 1), 4.0, 0.8, (0, sy, 0), pitch=0.5)
            for sy2 in (-1, 1):                                              # wheels
                B.cyl(V(sx * S / 2 - 2.05, sy * 8 - sy * 1.2 + sy2 * 0.6, 0.55), V(sx * S / 2 + 2.05, sy * 8 - sy * 1.2 + sy2 * 0.6, 0.55), 0.5, 12, "metal", C_DARK, 0.2)
        flat(B, V(sx * S / 2, 0, 0.06), (0.5, 17.0, 0.12), I3, "metal", C_STEEL, 0.2, rust=0.6)             # floor rail
    for sy in (-1, 1):                                                       # main girders
        B.box(Vector((0, sy * 6, H)), (S + 2, 1.6, 2.4), I3, "metal", C_YELLOW, 0.25, rust=0.7, bevel=0.1)
        flat(B, V(0, sy * 6, H + 1.25), (S + 2, 0.3, 0.1), I3, "metal", C_STEEL, 0.2, rust=0.4)              # trolley rail
        for k in range(16):
            B.box(Vector((-S / 2 + k * S / 15, sy * 6, H)), (0.25, 1.7, 2.3), Matrix.Rotation(0.6 * (1 if k % 2 else -1), 3, 'Y'), "metal", C_RUST, 0.2, rust=0.8, bevel=0.04)
        for k in range(10):                                                  # rust run-off under the girder
            streak(B, V(-S / 2 + 2 + k * 5.1, sy * 6 - sy * 0.0, H - 1.2) + V(0, -0.805 if sy < 0 else 0.805, 0), (0, -1 if sy < 0 else 1, 0),
                   random.uniform(1.5, 4.0), 0.3, C_RUST, C_YELLOW, "metal")
        for k in range(6):                                                   # festoon cable loops
            x0 = -S / 2 + 4 + k * 7.5
            B.pipe([V(x0 + 7.5 * t, sy * 6.9, H - 1.1 - 1.6 * 4 * t * (1 - t)) for t in (0, 0.25, 0.5, 0.75, 1)], 0.08, 5, "rubber", C_BLACK)
    for sx in (-1, 1):                                                       # end ties between the girders
        B.box(V(sx * (S / 2 + 0.2), 0, H), (1.4, 12.0, 1.6), I3, "metal", C_YELLOW, 0.25, rust=0.7, bevel=0.08)
    B.box(Vector((-S / 2 - 3, 0, H + 1.5)), (5.0, 8.0, 3.0), I3, "metal", C_DARK, 0.2, rust=0.5, bevel=0.08)             # machinery house
    for k in range(6):
        flat(B, V(-S / 2 - 5.52, -3 + k * 1.2, H + 1.8), (0.04, 0.8, 1.6), I3, "metal", C_GREY, 0.2, rust=0.4)          # louvre doors
    B.box(V(-S / 2 + 1.5, -7.6, H - 3.2), (3.0, 2.4, 2.6), I3, "metal", C_YELLOW, 0.25, rust=0.6, bevel=0.06)          # operator cab
    for k in range(3):
        flat(B, V(-S / 2 + 0.5 + k * 1.0, -8.81, H - 3.0), (0.85, 0.02, 1.2), I3, "cyan", (0.05, 0.12, 0.14), 0.1)
    flat(B, V(-S / 2 + 1.5, -7.6, H - 1.85), (3.2, 2.6, 0.1), I3, "metal", C_DARK, 0.2)
    for s in (-1, 1):                                                        # ladder up the front leg
        B.box(V(-S / 2 + s * 0.35, -6.9, H / 2 - 1), (0.08, 0.08, H - 2), I3, "metal", C_DARK, 0.2, bevel=0)
    for k in range(int((H - 3) / 0.5)):
        card(B, [V(-S / 2 - 0.35, -6.9 - 0.03, 1 + k * 0.5), V(-S / 2 + 0.35, -6.9 - 0.03, 1 + k * 0.5),
                 V(-S / 2 + 0.35, -6.9 + 0.03, 1 + k * 0.5 + 0.03), V(-S / 2 - 0.35, -6.9 + 0.03, 1 + k * 0.5 + 0.03)], C_DARK, "metal")
    for k in range(6):
        vine(B, Vector((random.uniform(-S / 2, S / 2), random.choice((-6.8, 6.8)), H - 1.2)), random.uniform(5, 12), 350 + k)
    for sx in (-1, 1):
        for sy in (-1, 1):
            ivy(B, V(sx * S / 2 + 0.6, sy * 6 - sy * 0.6, 1.8), 1.3, 356 + sx + sy * 2)
    done(B, "Frame", coll, h=4.0, k=0.5)
    T = Builder()                                                            # trolley and hook, travelling the span
    T.box(Vector((0, 0, 0)), (4.0, 14.0, 2.0), I3, "metal", C_DARK, 0.2, rust=0.5, bevel=0.08)
    for sy in (-1, 1):
        for sx in (-1, 1):
            T.cyl(V(sx * 1.3, sy * 6 - 0.35, -0.9), V(sx * 1.3, sy * 6 + 0.35, -0.9), 0.45, 12, "metal", C_GREY)    # rail wheels
    T.box(V(0, 0, 1.4), (3.0, 4.0, 0.8), I3, "metal", C_YELLOW, 0.25, rust=0.6, bevel=0.05)               # hoist drum housing
    T.cyl(V(-1.2, 0, 1.4), V(1.2, 0, 1.4), 0.8, 14, "metal", C_GREY, 0.2)
    T.pipe([Vector((0, 0, -1)), Vector((0, 0, -18))], 0.12, 6, "steel", C_STEEL)
    T.box(Vector((0, 0, -18.5)), (1.4, 1.0, 1.2), I3, "metal", C_YELLOW, 0.2, rust=0.5, bevel=0.05)
    hazard(T, V(-0.7, -0.501, -18.9), (1, 0, 0), (0, 0, 1), 1.4, 0.3, (0, -1, 0), pitch=0.2)
    T.pipe([Vector((0, 0, -19)), Vector((0.5, 0, -19.8)), Vector((0, 0, -20.5)), Vector((-0.4, 0, -20.0))], 0.2, 8, "steel", C_STEEL)
    tr = mnode(T, "Trolley", coll, Vector((-15, 0, H + 2.2)))
    keys(tr, (1, 150, 240, 390, 481), "location", [Vector((-15, 0, H + 2.2)), Vector((15, 0, H + 2.2)), Vector((15, 0, H + 2.2)),
                                                   Vector((-15, 0, H + 2.2)), Vector((-15, 0, H + 2.2))])
    return coll

def build_reactor_sphere():
    coll = new("ReactorSphere"); B = Builder()
    R = 14.0; Z = R + 4
    bm = B.bm
    res = bmesh.ops.create_uvsphere(bm, u_segments=48, v_segments=28, radius=R)
    for v in res["verts"]:
        v.co.z += Z
    fs = list({f for v in res["verts"] for f in v.link_faces})
    for f in fs:
        f.material_index = MI["panel"]; f.smooth = True
    B.paint(fs, C_WHITE, 0.2, rust=0.4)
    for e in (-0.9, -0.55, -0.25, 0.3, 0.6, 0.95):                          # welded panel seams: latitude bands
        ring(B, V(0, 0, Z + math.sin(e) * R), (0, 0, 1), math.cos(e) * R - 0.05, math.cos(e) * R + 0.12, 0.25, 48, "panel", (0.55, 0.55, 0.52), 0.2, rust=0.4)
    for k in range(12):                                                      # and meridian ribs
        a = k / 12 * math.tau
        B.pipe([V(math.cos(a) * math.cos(e) * (R + 0.06), math.sin(a) * math.cos(e) * (R + 0.06), Z + math.sin(e) * (R + 0.06))
                for e in [(-0.95 + i * 0.19) for i in range(11)]], 0.1, 4, "panel", (0.6, 0.6, 0.57))
    ring(B, V(0, 0, 6.2), (0, 0, 1), 6.0, 6.8, 1.2, 32, "metal", C_DARK, 0.2, rust=0.5)                 # support collar
    for k in range(8):                                                       # support legs
        a = k / 8 * math.tau
        T = Matrix.Rotation(a, 3, 'Z') @ Matrix.Rotation(-0.25, 3, 'Y')
        B.box(Vector((math.cos(a) * R * 0.75, math.sin(a) * R * 0.75, 4.5)), (1.5, 1.5, 9.0), T, "metal", C_DARK, 0.2, rust=0.5, bevel=0.08)
        B.box(V(math.cos(a) * R * 0.86, math.sin(a) * R * 0.86, 0.2), (2.6, 2.6, 0.4), Matrix.Rotation(a, 3, 'Z'), "rock", C_CONC_D, 0.15, bevel=0.06)  # foot pad
        a2 = a + math.tau / 16                                               # cross braces between the legs
        p0 = V(math.cos(a) * R * 0.8, math.sin(a) * R * 0.8, 2.0); p1 = V(math.cos(a + math.tau / 8) * R * 0.7, math.sin(a + math.tau / 8) * R * 0.7, 6.5)
        d = p1 - p0
        B.box((p0 + p1) / 2, (0.4, 0.4, d.length), rot_to(d), "metal", C_RUST, 0.2, rust=0.8, bevel=0.03)
        streak(B, V(math.cos(a2) * (R * 0.99), math.sin(a2) * (R * 0.99), Z - 1.0), V(math.cos(a2), math.sin(a2), -0.2), 4.0, 0.5, C_RUST, C_WHITE, "panel")
    ring(B, Vector((0, 0, Z)), (0, 0, 1), R - 0.2, R + 0.25, 1.2, 64, "cyan", C_CYAN, 0.05)                # glowing equator seam
    for s in (-1, 1):
        ring(B, V(0, 0, Z + s * 0.75), (0, 0, 1), R - 0.1, R + 0.35, 0.3, 64, "metal", C_DARK, 0.2)          # its retaining bands
    for k in range(4):                                                       # pipes from the underside into the ground
        a = k / 4 * math.tau + 0.4
        B.pipe([V(math.cos(a) * 5, math.sin(a) * 5, 5.2), V(math.cos(a) * 7, math.sin(a) * 7, 2.0), V(math.cos(a) * 9, math.sin(a) * 9, 0.6), V(math.cos(a) * 12, math.sin(a) * 12, 0.6)], 0.5, 10, "metal", C_GREY)
    hz = V(0, -R, Z + 3.5)                                                   # access hatch and ladder on the front
    B.box(hz + V(0, -0.1, 0), (2.4, 0.4, 2.4), I3, "metal", C_DARK, 0.2, rust=0.5, bevel=0.08)
    hazard(B, hz + V(-1.2, -0.31, -1.2), (1, 0, 0), (0, 0, 1), 2.4, 0.35, (0, -1, 0), pitch=0.3)
    for k in range(4):                                                       # peeled-off panels
        a = random.uniform(0, math.tau); e = random.uniform(0.1, 0.8)
        n = V(math.cos(a) * math.cos(e), math.sin(a) * math.cos(e), math.sin(e))
        plate(B, V(0, 0, Z) + n * (R + 0.4), n, 2.4, 2.0, 0.12, "panel", C_WHITE, 0.2, rust=0.6, bev=0.05, spin=0.3)
        blob(B, V(0, 0, Z) + n * R, 1.3, (0.12, 0.12, 0.11), 7, "panel", seed=k, off=0.03, n=n)          # the dark hole behind
    for k in range(20):
        a = random.uniform(0, math.tau); e = random.uniform(-0.2, 0.9)
        rock(B, Vector((math.cos(a) * math.cos(e) * R, math.sin(a) * math.cos(e) * R, Z + math.sin(e) * R)), random.uniform(1.0, 2.2), 360 + k, 1, 0.4,
             "rock", lambda p, nn: jit(random.choice([C_LEAF, C_MOSS]), 0.15), (1, 1, 0.6))
    for k in range(6):
        a = k * 1.1
        vine(B, V(math.cos(a) * (R - 0.4), math.sin(a) * (R - 0.4), Z - 0.9), random.uniform(4, 8), 385 + k)
    done(B, "Sphere", coll, h=5.0, k=0.45, stain=0.15)
    for k, (tilt, speed) in enumerate(((0.35, 1), (-0.6, -1), (1.1, 1))):   # containment rings, each on its own tilt
        Rg = Builder()
        r0, r1 = R + 2.0 + k * 1.3, R + 2.8 + k * 1.3
        ring(Rg, Vector((0, 0, 0)), (0, 0, 1), r0, r1, 0.8, 64, "metal", C_DARK, 0.2, rust=0.4)
        ring(Rg, Vector((0, 0, 0.45)), (0, 0, 1), R + 2.2 + k * 1.3, R + 2.6 + k * 1.3, 0.1, 64, "cyan", C_CYAN, 0.05)
        for j in range(8):                                                   # magnet clamps round each ring
            a = j / 8 * math.tau
            Rg.box(V(math.cos(a) * (r0 + r1) / 2, math.sin(a) * (r0 + r1) / 2, 0), (1.3, 1.0, 1.1), Matrix.Rotation(a, 3, 'Z'), "metal", C_GREY, 0.2, rust=0.5, bevel=0.06)
        ob = mnode(Rg, f"Ring{k}", coll, Vector((0, 0, R + 4)))
        spin(ob, 2, speed, 480, rest=(tilt, 0.3 * k, 0))
    return coll

def build_arcology_spire():
    coll = new("ArcologySpire"); B = Builder()
    H = 72.0; n = 16
    prof = ((7.2, 0), (7, 1.5), (5, 20), (4.5, 50), (3, H))
    ringz = lambda r, z: [Vector((math.cos(a) * r, math.sin(a) * r, z)) for a in [i / n * math.tau + math.pi / n for i in range(n)]]
    B.tube_rings([ringz(r, z) for r, z in prof], "rock", C_CONC, 0.18, 0.2, cap=True, smooth=False)
    rz = lambda z: next(r0 + (r1 - r0) * (z - z0) / (z1 - z0) for (r0, z0), (r1, z1) in zip(prof, prof[1:]) if z0 <= z <= z1)
    for z in range(4, 70, 3):                                                # window bands, a few still lit
        r = rz(z + 0.6) + 0.03
        for i in range(n):
            if (i * 7 + z) % 5 == 0:
                continue                                                     # blown-out / missing
            a0, a1 = i / n * math.tau + math.pi / n, (i + 1) / n * math.tau + math.pi / n
            p0, p1 = V(math.cos(a0), math.sin(a0), 0), V(math.cos(a1), math.sin(a1), 0)
            lit = (i * 13 + z * 3) % 29 == 0
            q0, q1 = p0.lerp(p1, 0.1) * r, p0.lerp(p1, 0.9) * r
            card(B, [q0 + V(0, 0, z), q1 + V(0, 0, z), q1 + V(0, 0, z + 1.2), q0 + V(0, 0, z + 1.2)],
                 (0.9, 0.8, 0.5) if lit else (0.05, 0.06, 0.07), "glow" if lit else "metal", 0.1)
    for k in range(4):                                                       # buttress fins up the base
        a = k / 4 * math.tau + 0.4
        u = V(math.cos(a), math.sin(a), 0)
        solid(B, [u * 6.8 + V(0, 0, 0), u * 11 + V(0, 0, 0), u * 5.2 + V(0, 0, 18), u * 6.8 + V(0, 0, 0) + V(-u.y, u.x, 0) * 0.8,
                  u * 11 + V(-u.y, u.x, 0) * 0.8, u * 5.2 + V(0, 0, 18) + V(-u.y, u.x, 0) * 0.8],
              [(0, 1, 2), (3, 5, 4), (0, 3, 4, 1), (1, 4, 5, 2), (2, 5, 3, 0)], "rock", C_CONC_D, 0.15)
    for k, z in enumerate((12, 26, 40, 54, 66)):                             # ringed platforms, one snapped
        if k == 2:
            for j in range(3):
                a = j * 0.5
                B.box(Vector((math.cos(a) * 9, math.sin(a) * 9, z)), (6, 3, 0.6), Matrix.Rotation(a, 3, 'Z'), "rock", C_CONC_D, 0.15, bevel=0.05)
                for q in range(3):
                    rebar(B, V(math.cos(a) * 12, math.sin(a) * 12, z) + V(-math.sin(a), math.cos(a), 0) * (q - 1) * 0.8, (math.cos(a), math.sin(a), -0.5), 1.4)
        else:
            ring(B, Vector((0, 0, z)), (0, 0, 1), 4.5, 12.0 - k, 0.8, 32, "rock", C_CONC_D, 0.15, rust=0.2)
            card(B, [V(math.cos(a) * (12.0 - k), math.sin(a) * (12.0 - k), z + 0.4) for a in [i / 32 * math.tau for i in range(32)]], C_CONC_D, "rock", 0.15)   # deck top
            ring(B, Vector((0, 0, z + 0.9)), (0, 0, 1), 11.6 - k, 12.0 - k, 1.0, 32, "panel", C_WHITE, 0.2, rust=0.3)
            for j in range(3):                                               # sky-garden trees on the decks
                a = j * 2.1 + k
                p = V(math.cos(a) * (9.5 - k), math.sin(a) * (9.5 - k), z + 0.4)
                B.cyl(p, p + V(0, 0, 2.5), 0.2, 5, "rock", C_BARK, cap=False)
                leaves(B, p + V(0, 0, 3.0), 1.3, 400 + k * 3 + j)
            for j in range(2):
                vine(B, V(math.cos(j * 3 + k) * (12.2 - k), math.sin(j * 3 + k) * (12.2 - k), z + 1.3), random.uniform(4, 9), 430 + k * 2 + j)
    B.cyl(V(0, 0, H), V(0, 0, H + 10), 0.35, 6, "metal", C_GREY, cap=True)                             # antenna mast
    for z in (H + 3, H + 6):
        ring(B, V(0, 0, z), (0, 0, 1), 0.3, 1.4 - (z - H) * 0.12, 0.2, 12, "metal", C_DARK, 0.2)
    flat(B, V(0, 0, H + 10.1), (0.3, 0.3, 0.3), I3, "glow", (1.0, 0.2, 0.1), 0.05)                     # dead aircraft warning lamp
    for k in range(30):
        a = random.uniform(0, math.tau); z = random.uniform(2, 60)
        ivy(B, Vector((math.cos(a) * 5.5, math.sin(a) * 5.5, z)), random.uniform(1.0, 2.4), 380 + k, (1, 1, 1.4))
    for k in range(6):
        vine(B, Vector((math.cos(k) * 11, math.sin(k) * 11, 26)), random.uniform(6, 14), 420 + k)
    done(B, "Mesh", coll, h=6.0, k=0.45, stain=0.3); return coll

def build_sky_bridge():
    coll = new("SkyBridge"); B = Builder()
    for x in (-40, 40):                                                      # towers
        B.box(Vector((x, 0, 20)), (8, 8, 40), I3, "rock", C_CONC, 0.18, rust=0.2, bevel=0.15)
        for sx in (-1, 1):                                                   # corner pilasters
            for sy in (-1, 1):
                B.box(V(x + sx * 3.8, sy * 3.8, 20), (1.0, 1.0, 40.4), I3, "rock", C_CONC_D, 0.15, bevel=0.08)
        for z in (10, 20, 30):
            B.box(Vector((x, -4.05, z)), (6, 0.1, 1.5), I3, "cyan", (0.2, 0.3, 0.35), 0.1, bevel=0)
            for s in (-1, 1):
                flat(B, V(x, s * 4.05, z - 0.9), (6.4, 0.3, 0.2), I3, "rock", C_CONC_D, 0.15)            # sills
        for z in (5, 15, 25, 35):                                            # dark window slots on the sides
            for s in (-1, 1):
                flat(B, V(x + s * 4.03, 0, z), (0.06, 5.0, 1.2), I3, "metal", (0.05, 0.06, 0.07), 0.1)
        B.box(V(x, 0, 40.4), (8.6, 8.6, 0.8), I3, "rock", C_CONC_D, 0.15, bevel=0.08)                    # parapet cap
        for k in range(3):
            streak(B, V(x - 2 + k * 2, -4.0, 39.9), (0, -1, 0), random.uniform(8, 18), 0.8, C_STAIN, C_CONC)
    for (x0, x1, droop) in ((-36, -8, 0.0), (8, 36, 0.0)):                   # surviving spans with a conveyor deck
        cx, L_ = (x0 + x1) / 2, x1 - x0
        B.box(Vector((cx, 0, 30)), (L_, 5, 1.5), I3, "metal", C_DARK, 0.2, rust=0.6, bevel=0.06)
        B.box(Vector((cx, 0, 30.85)), (L_, 3.4, 0.2), I3, "rubber", C_BLACK, 0.1, bevel=0)
        for sy in (-1, 1):
            B.box(Vector((cx, sy * 2.5, 32.0)), (L_, 0.2, 2.2), I3, "panel", C_WHITE, 0.2, rust=0.4, bevel=0.03)
            lattice(B, V(x0, sy * 2.6, 29.3), V(x1, sy * 2.6, 29.3), 1.3, 12, C_RUST, 0.3, side=V(0, 0, -1))   # under-truss
            flat(B, V(cx, sy * 2.6, 28.0), (L_, 0.4, 0.4), I3, "metal", C_RUST, 0.2, rust=0.8)
            for k in range(int(L_ / 4)):                                     # panel seams and roller stands
                flat(B, V(x0 + 2 + k * 4, sy * 2.61, 32.0), (0.08, 0.04, 2.1), I3, "panel", (0.3, 0.3, 0.3), 0.1)
        xe = x1 if x0 < 0 else x0                                            # the snapped end
        for k in range(6):
            rebar(B, V(xe, -2 + k * 0.8, 30.2), (1 if x0 < 0 else -1, 0, -0.7), 2.5)
        B.pipe([V(xe, 1.5, 30.9), V(xe + (2 if x0 < 0 else -2), 1.6, 27), V(xe + (2.5 if x0 < 0 else -2.5), 1.4, 20)], 0.12, 5, "rubber", C_BLACK)   # dangling cables
    tilt = Matrix.Rotation(math.radians(55), 3, 'Y')                         # the fallen middle span
    B.box(Vector((-2, 0, 14)), (20, 5, 1.5), tilt, "metal", C_DARK, 0.2, rust=0.8, bevel=0.06)
    for sy in (-1, 1):
        B.box(V(-2, 0, 14) + tilt @ V(0, sy * 2.5, 1.85), (19, 0.2, 2.2), tilt @ Matrix.Rotation(sy * 0.15, 3, 'X'), "panel", C_WHITE, 0.2, rust=0.6, bevel=0.03)
    for k in range(14):
        chunk(B, Vector((random.uniform(-8, 8), random.uniform(-6, 6), 0.8)), random.uniform(0.8, 1.8), 440 + k)
    for k in range(8):
        vine(B, Vector((random.choice((-8, 8)) + random.uniform(-2, 2), random.uniform(-2.5, 2.5), 29.3)), random.uniform(6, 16), 460 + k)
    for x in (-40, 40):
        for k in range(4):
            ivy(B, V(x + random.uniform(-4.4, 4.4), random.choice((-4.4, 4.4)), random.uniform(1, 14)), random.uniform(1.0, 2.0), 470 + k + x)
    done(B, "Mesh", coll, h=6.0, k=0.45, stain=0.25); return coll

def build_panel_arm_wall():
    coll = new("PanelArmWall"); B = Builder()
    W, H, n = 36.0, 24.0, 6
    B.box(Vector((0, 1.5, H / 2)), (W + 2, 1.0, H + 2), I3, "metal", C_DARK, 0.2, rust=0.5, bevel=0.1)       # backing frame
    s = W / n
    for i in range(n + 1):                                                   # grid of stiffening beams on its face
        B.box(V(-W / 2 + i * s, 0.9, H / 2), (0.5, 0.2, H + 2), I3, "metal", C_GREY, 0.2, rust=0.6, bevel=0.04)
    for j in range(5):
        B.box(V(0, 0.9, 1.0 + j * s), (W + 2, 0.2, 0.5), I3, "metal", C_GREY, 0.2, rust=0.6, bevel=0.04)
    for i in range(n):
        for j in range(4):
            c = V(-W / 2 + s / 2 + i * s, 0.78, 1.0 + s / 2 + j * s)
            ring(B, c, (0, 1, 0), 0.4, 0.75, 0.1, 10, "metal", C_DARK, 0.2)                             # arm sockets
            streak(B, c + V(0.9, -0.03, -0.9), (0, -1, 0), random.uniform(1.0, 3.0), 0.25, C_RUST, C_GREY, "metal")
    B.box(V(0, 1.0, 0.3), (W + 3, 2.6, 0.6), I3, "rock", C_CONC_D, 0.15, bevel=0.06)                     # plinth
    for k in range(6):
        vine(B, V(random.uniform(-W / 2, W / 2), 0.9, H + 0.8), random.uniform(5, 10), 490 + k)
    for k in range(5):
        ivy(B, V(random.uniform(-W / 2, W / 2), 0.2, 0.6), random.uniform(0.8, 1.6), 496 + k, (1.3, 0.8, 0.8))
    done(B, "Frame", coll, h=3.0, k=0.45)
    for i in range(n):
        for j in range(4):
            P = Builder()
            broken = (i, j) in ((1, 3), (4, 0))
            col = jit(C_WHITE, 0.06)
            P.box(Vector((0, 0, 0)), (s - 0.3, 0.3, s - 0.3), I3, "panel", col, 0.15, rust=0.5 if broken else 0.2, bevel=0.08)
            for sx in (-1, 1):                                               # seams dividing the face into four
                flat(P, V(0, -0.152, 0), (0.06, 0.006, s - 0.5), Matrix.Rotation(math.pi / 2 * (sx > 0), 3, 'Y'), "panel", (0.3, 0.3, 0.3), 0.1)
            for sx in (-1, 1):
                for sz in (-1, 1):
                    decal(P, V(sx * (s / 2 - 0.5), -0.15, sz * (s / 2 - 0.5)), (0, -1, 0), 0.25, 0.25, (0.3, 0.3, 0.3), "panel", off=0.003)
            P.cyl(V(0, 0.15, 0), V(0, 0.9, 0), 0.4, 8, "panel", C_DARK, 0.2, cap=False)               # piston sleeve
            P.cyl(V(0, 0.9, 0), V(0, 1.7, 0), 0.22, 8, "panel", (0.5, 0.5, 0.48), 0.1, cap=False)      # rod
            if broken:
                P.box(Vector((0, -0.2, 0)), (s * 0.7, 0.05, 0.1), Matrix.Rotation(0.6, 3, 'Y'), "panel", C_MOSS, 0.2, bevel=0)
                crack(P, V(-0.5, -0.155, 0.8), (0, -1, 0), 2.5, 500 + i, w=0.06, mat="panel")
            c = Vector((-W / 2 + s / 2 + i * s, -0.5, 1.0 + s / 2 + j * s))
            ob = mnode(P, f"Panel{i}_{j}", coll, c)
            o = 1 + ((i * 7 + j * 3) % 10) * 20
            out = c + Vector((0, -2.5 if (i + j) % 2 else -1.2, 0))
            keys(ob, (1, o, o + 30, o + 150, o + 180, 481), "location", [c, c, out, out, c, c])
    return coll

PIECES = [(globals()[f"build_{n}"], f"{n}.glb") for n in (
    "cracked_pillar", "collapsed_pillar", "rubble_pile", "moss_mound", "overgrown_tree", "fern_cluster", "hanging_vines",
    "collapsed_panel_wall", "broken_catwalk", "cable_drapes", "lab_bench", "flicker_terminal", "monitor_bank", "filing_cabinets",
    "cryo_pod", "observation_booth", "pipe_cluster", "hanging_lamp", "ceiling_fan", "rusted_barrels", "crate_stack",
    "puddle_debris", "tipped_barriers", "elevator_ruin",
    "cooling_tower", "gantry_crane", "reactor_sphere", "arcology_spire", "sky_bridge", "panel_arm_wall")]
ICONS = []

def build_all(do_export=True):
    return build_family(PIECES, OUT_DIR, do_export)

if __name__ == "__main__":
    build_all(do_export=False)
