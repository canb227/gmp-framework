"""
Resource and product item models (world items: the physical pieces that ride the belts). Origin at the item's
centre of mass; Blender Z is Godot Y. Each model matches its collider in the item's world scene (game/scenes/items/world/):

  box (x, y, z)      -> the model spans Blender (x, z, y) around the origin
  sphere r           -> a lump of radius ~r
  capsule r, h       -> an upright cylinder / crystal of radius r and total height h (Godot Y = Blender Z)

Items are spawned in the hundreds, so each one is a single mesh node, at most 700 triangles and (nearly always)
at most two material surfaces. Detail comes from silhouette and vertex colour rather than many small parts:
small studs and filings are open-bottomed, spikes and crystals are built from as few faces as read.

Metals: iron / copper ingots, rods and plates; scrap balls and scrap ingots.
Base resources: coal, salt, floatstone, frost crystal, lodestone, quartz, latex resin, sulfur, quicksilver,
voltaic crystal. Products: coke briquette, glass pane, quartz shards, rubber ball, blast charge, battery cell,
magnet core, aerogel tile. Puzzle-room resources: scree, shale, slickstone puck, burr seed, ballast shot.
Production chains: storm sand, fulgurite (charged and spent), capacitor cell; tar blob, chalk nodule, coated
pellet, tar rock, bitumen brick.
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
C_SCALE = (0.33, 0.34, 0.37)                 # mill scale on hot-rolled iron
C_PATINA = (0.3, 0.55, 0.45)                 # copper verdigris
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
C_SAND = (0.62, 0.55, 0.38)
C_CHALK = (0.86, 0.85, 0.78)
C_TAR = (0.02, 0.018, 0.016)
C_N, C_S = (0.75, 0.1, 0.08), (0.1, 0.2, 0.7)  # magnet pole paint

def jitter(c, k=0.15):
    f = 1 + random.uniform(-k, k)
    return [min(1.0, ci * f) for ci in c]

def mix(a, b, t):
    return [x + (y - x) * t for x, y in zip(a, b)]

def rand_dir(zlo=-1.0, zhi=1.0):
    return Vector((random.uniform(-1, 1), random.uniform(-1, 1), random.uniform(zlo, zhi))).normalized()

def item(name, fn):
    random.seed(sum(map(ord, name)))
    coll = clear_collection(f"Item_{name}")
    B = Builder()
    fn(B, coll)
    if B.bm.faces:
        finish(B, "Mesh", coll)
    return coll

# ---------------------------------------------------------------------------- geometry helpers
def set_col(B, faces, col):
    """Flat vertex colour on faces (no grime noise)."""
    for f in faces:
        for l in f.loops:
            l[B.col] = (col[0], col[1], col[2], 1.0)

def rr_ring(z, hx, hy, k, cx=0.0, cy=0.0):
    """Rectangle loop with its four corners cut at 45 degrees by k (8 points, counter-clockwise)."""
    return [(cx + hx - k, cy - hy, z), (cx + hx, cy - hy + k, z), (cx + hx, cy + hy - k, z), (cx + hx - k, cy + hy, z),
            (cx - hx + k, cy + hy, z), (cx - hx, cy + hy - k, z), (cx - hx, cy - hy + k, z), (cx - hx + k, cy - hy, z)]

def slab(B, z0, z1, bot, top, mat, col, var=0.12, rust=0.0, e=0.015, k=0.03, dish=None):
    """Chamfered block from a bot=(hx, hy) footprint at z0 to a top=(hx, hy) at z1 (a draft angle when they
    differ); every edge carries an e chamfer and the upright corners are cut by k. dish=(depth, inset) sinks a
    shallow tray into the top face (the shrink cavity of a cast ingot). Returns (faces, rings-per-band n=8)."""
    rings = [rr_ring(z0, bot[0] - e, bot[1] - e, k * 0.6), rr_ring(z0 + e, bot[0], bot[1], k),
             rr_ring(z1 - e, top[0], top[1], k), rr_ring(z1, top[0] - e, top[1] - e, k * 0.6)]
    if dish:
        d, ins = dish
        rings.append(rr_ring(z1 - d, top[0] - e - ins, top[1] - e - ins, k * 0.4))
    return B.tube_rings([[Vector(p) for p in r] for r in rings], mat, col, var, rust, cap=True, smooth=False)

def stud(B, c, size, R=None, mat="metal", col=C_DARK, var=0.05):
    """Small raised block without a bottom face (it sits on a surface): 10 triangles."""
    R = I3 if R is None else R
    h = [s / 2 for s in size]
    v = [B.bm.verts.new(c + R @ Vector((sx * h[0], sy * h[1], sz * h[2]))) for sx in (-1, 1) for sy in (-1, 1) for sz in (-1, 1)]
    # vertex i: sx = i // 4, sy = i // 2 % 2, sz = i % 2; top, -x, +x, -y, +y (no bottom)
    fs = [B.quad([v[i] for i in q], mat) for q in ((1, 3, 7, 5), (0, 1, 3, 2), (4, 6, 7, 5), (0, 4, 5, 1), (2, 3, 7, 6))]
    bmesh.ops.recalc_face_normals(B.bm, faces=fs)
    B.paint(fs, col, var)
    return fs

def ridge(B, c, length, width, height, ang, mat, col, var=0.05):
    """Checker-plate lug: a roof-shaped ridge (no base) along angle ang in the XY plane: 6 triangles."""
    R = Matrix.Rotation(ang, 3, 'Z')
    hl, hw, e = length / 2, width / 2, width / 2
    b = [B.bm.verts.new(c + R @ Vector(p)) for p in ((-hl, -hw, 0), (hl, -hw, 0), (hl, hw, 0), (-hl, hw, 0))]
    t = [B.bm.verts.new(c + R @ Vector(p)) for p in ((-hl + e, 0, height), (hl - e, 0, height))]
    fs = [B.quad([b[0], b[1], t[1], t[0]], mat), B.quad([b[2], b[3], t[0], t[1]], mat)]
    for tri in ((b[1], b[2], t[1]), (b[3], b[0], t[0])):
        f = B.bm.faces.new(tri); f.material_index = MI[mat]; fs.append(f)
    for f in fs:
        f.normal_update()
        if f.normal.z < -1e-4 or (f.normal.dot(f.calc_center_median() - c) < 0):
            f.normal_flip()
    B.paint(fs, col, var)
    return fs

def spike(B, pts, r, sides, mat, col, var=0.08, tip_col=None, twist=0.0):
    """Tapered spine through pts ending in a point, open at its (buried) base: sides*(2*len(pts)-3) triangles."""
    n = len(pts)
    rings = []
    for i, p in enumerate(pts[:-1]):
        t = (pts[min(i + 1, n - 1)] - pts[max(i - 1, 0)]).normalized()
        ref = Vector((0, 0, 1)) if abs(t.z) < 0.9 else Vector((1, 0, 0))
        u = t.cross(ref).normalized(); w = t.cross(u)
        rr = r * (1 - i / (n - 1))
        rings.append([B.bm.verts.new(p + (u * math.cos(a) + w * math.sin(a)) * rr)
                      for a in [twist + k / sides * math.tau for k in range(sides)]])
    tip = B.bm.verts.new(pts[-1])
    fs = []
    for i in range(len(rings) - 1):
        for k in range(sides):
            fs.append(B.quad([rings[i][k], rings[i][(k + 1) % sides], rings[i + 1][(k + 1) % sides], rings[i + 1][k]], mat))
    last = []
    for k in range(sides):
        f = B.bm.faces.new([rings[-1][k], rings[-1][(k + 1) % sides], tip]); f.material_index = MI[mat]; last.append(f)
    fs += last
    for f in fs:
        f.smooth = False
    bmesh.ops.recalc_face_normals(B.bm, faces=fs)
    B.paint(fs, col, var)
    if tip_col:
        B.paint(last, tip_col, var)
    return fs

def bipyramid(B, c, axis, r, h0, h1, sides, mat, col, var=0.06, spin=0.0):
    """Double-pointed crystal (sulfur, shards): a ring of radius r at c, tips h0 below and h1 above: 2*sides tris."""
    ax = Vector(axis).normalized(); R = rot_to(ax)
    ring_ = [B.bm.verts.new(c + R @ Vector((math.cos(a) * r, math.sin(a) * r * 0.8, 0)))
             for a in [spin + k / sides * math.tau for k in range(sides)]]
    lo, hi = B.bm.verts.new(c - ax * h0), B.bm.verts.new(c + ax * h1)
    fs = []
    for k in range(sides):
        a, b = ring_[k], ring_[(k + 1) % sides]
        for tri in ((a, b, hi), (b, a, lo)):
            f = B.bm.faces.new(tri); f.material_index = MI[mat]; f.smooth = False; fs.append(f)
    bmesh.ops.recalc_face_normals(B.bm, faces=fs)
    B.paint(fs, col, var)
    return fs

def column(B, p0, p1, r, sides, mat, col, var=0.06, tip=0.3, flat_tip=False, taper=1.0):
    """Hexagonal crystal column from p0 (open base, buried) to a pointed (or flat-topped) termination."""
    ax = (p1 - p0); L_ = ax.length; ax.normalize(); R = rot_to(ax)
    ring_ = lambda p, rr: [p + R @ Vector((math.cos(a) * rr, math.sin(a) * rr, 0)) for a in [k / sides * math.tau for k in range(sides)]]
    body = p0 + ax * L_ * (1 - tip)
    rings = [ring_(p0, r), ring_(body, r * taper)]
    if flat_tip:
        rings.append(ring_(p1, r * taper * 0.55))
        fs = B.tube_rings(rings, mat, col, var, 0.0, cap=True, smooth=False)
        return fs
    vs = [[B.bm.verts.new(p) for p in rg] for rg in rings]
    tipv = B.bm.verts.new(p1)
    fs = [B.quad([vs[0][k], vs[0][(k + 1) % sides], vs[1][(k + 1) % sides], vs[1][k]], mat) for k in range(sides)]
    for k in range(sides):
        f = B.bm.faces.new([vs[1][k], vs[1][(k + 1) % sides], tipv]); f.material_index = MI[mat]; fs.append(f)
    for f in fs:
        f.smooth = False
    bmesh.ops.recalc_face_normals(B.bm, faces=fs)
    B.paint(fs, col, var)
    return fs

def lump(B, center, r, seed, subdiv=2, amp=0.28, mat="rock", color_fn=None, squash=(1, 1, 0.85), smooth=False, detail=0.35):
    """Displaced icosphere (the props rock() with smooth shading as an option). subdiv 1/2/3 = 20/80/320 triangles.
    Returns (faces, surf) where surf(d) is the surface point in direction d, for placing detail on it."""
    off = Vector((seed * 3.1, seed * 1.7, seed * 0.9))
    def radius(d):
        return r * (1 + noise.noise(d * 1.6 + off) * amp + noise.noise(d * 4.0 + off) * amp * detail)
    def surf(d):
        d = Vector(d).normalized()
        return center + Vector((d.x * squash[0], d.y * squash[1], d.z * squash[2])) * radius(d)
    res = bmesh.ops.create_icosphere(B.bm, subdivisions=subdiv, radius=r)
    vs = res["verts"]
    for v in vs:
        v.co = surf(v.co)
    fs = list({f for v in vs for f in v.link_faces})
    for f in fs:
        f.material_index = MI[mat]; f.smooth = smooth
    bmesh.ops.recalc_face_normals(B.bm, faces=fs)
    B.bm.normal_update()
    for f in fs:
        col = color_fn(f.calc_center_median(), f.normal) if color_fn else (0.3, 0.3, 0.3)
        for l in f.loops:
            l[B.col] = (col[0], col[1], col[2], 1.0)
    return fs, surf

def ball(B, r, u, v, mat, zlo=1.0, zhi=1.0, center=Vector((0, 0, 0))):
    """Smooth UV sphere (u*(v-1)*2 triangles), optionally flattened below / above the equator."""
    res = bmesh.ops.create_uvsphere(B.bm, u_segments=u, v_segments=v, radius=r)
    for vv in res["verts"]:
        vv.co.z *= zhi if vv.co.z > 0 else zlo
        vv.co += center
    fs = list({f for vv in res["verts"] for f in vv.link_faces})
    for f in fs:
        f.material_index = MI[mat]; f.smooth = True
    return fs

def pits(B, surf, n, r_lo, r_hi, depth, mat, col, var=0.1, sides=5, zlo=-1.0):
    """Dark conical pits pressed into a lump (vesicles, pores): sides triangles each, facing out."""
    out = []
    for _ in range(n):
        d = rand_dir(zlo)
        p = surf(d); R = rot_to(d); r = random.uniform(r_lo, r_hi)
        rim = [B.bm.verts.new(p + d * 0.004 + R @ Vector((math.cos(a) * r, math.sin(a) * r, 0))) for a in [k / sides * math.tau for k in range(sides)]]
        bot = B.bm.verts.new(p - d * depth)
        for k in range(sides):
            f = B.bm.faces.new([rim[k], rim[(k + 1) % sides], bot]); f.material_index = MI[mat]; f.smooth = False
            f.normal_update()
            if f.normal.dot(d) < 0:
                f.normal_flip()
            out.append(f)
    B.paint(out, col, var)
    return out

def rod_body(B, rings, seg, mat, col, var=0.06, cap=True):
    """Upright round body from [(z, r)] (end rings capped unless cap=False)."""
    rs = [[Vector((math.cos(k / seg * math.tau) * r, math.sin(k / seg * math.tau) * r, z)) for k in range(seg)] for z, r in rings]
    fs = B.tube_rings(rs, mat, col, var, 0.0, cap=cap, smooth=True)
    bands = [fs[i * seg:(i + 1) * seg] for i in range(len(rings) - 1)]
    return fs, bands, fs[-2:]

# ---------------------------------------------------------------------------- metals
def ingot(B, mat, col, stamp_col, bloom=None, rust=0.0):
    """0.8 x 0.4 x 0.3 cast ingot (Godot box 0.8, 0.3, 0.4): drafted sides, chamfered edges, a shrink dish in the
    open top with the foundry stamp in it and the rough pour mark at one end."""
    fs = slab(B, -0.15, 0.15, (0.4, 0.2), (0.34, 0.15), mat, col, 0.1, rust, e=0.016, k=0.035, dish=(0.016, 0.03))
    if bloom:                                                                 # foot chamfer: oxide / patina bloom
        B.paint(fs[:8], mix(col, bloom, 0.4), 0.2)
    B.paint(fs[16:24], mix(col, C_WEAR, 0.25), 0.08)                          # handled top edge
    z = 0.15 - 0.016
    stud(B, Vector((-0.05, 0, z + 0.003)), (0.2, 0.07, 0.006), None, mat, stamp_col, 0.05)
    for k in range(3):
        stud(B, Vector((-0.11 + k * 0.06, 0, z + 0.008)), (0.028, 0.04, 0.006), None, mat, [c * 0.6 for c in stamp_col], 0.05)
    B.cyl(Vector((0.2, 0, z)), Vector((0.2, 0, z + 0.008)), 0.04, 7, mat, [min(1, c * 1.15) for c in col], 0.25)   # pour mark

def scrap_ingot(B, coll):
    """Mixed-metal ingot: lumpy dish, casting flash round its foot and half-melted inclusions."""
    col = C_IRON_DARK
    fs = slab(B, -0.14, 0.15, (0.39, 0.19), (0.33, 0.145), "metal", col, 0.25, 0.25, e=0.02, k=0.04, dish=(0.022, 0.025))
    for v in {v for f in fs for v in f.verts if v.co.z > 0.1}:                    # uneven pour
        v.co.z += noise.noise(v.co * 9.0) * 0.012
    B.paint(fs[:8], mix(col, C_RUST, 0.5), 0.2)
    flash = slab(B, -0.15, -0.14, (0.4, 0.2), (0.4, 0.2), "metal", mix(col, C_WEAR, 0.3), 0.3, 0.2, e=0.003, k=0.05)
    for v in {v for f in flash for v in f.verts}:                               # ragged flash edge
        v.co.x *= 1 - abs(noise.noise(v.co * 7.0)) * 0.04; v.co.y *= 1 - abs(noise.noise(v.co * 7.0 + Vector((3, 0, 0)))) * 0.08
    for k in range(9):
        p = Vector((random.uniform(-0.26, 0.26), random.uniform(-0.09, 0.09), 0.132))
        m = random.choice(("copper", "copper", "metal"))
        c = jitter(C_COPPER if m == "copper" else random.choice([(0.6, 0.6, 0.62), (0.55, 0.3, 0.12)]))
        stud(B, p, (random.uniform(0.04, 0.1), random.uniform(0.03, 0.07), random.uniform(0.008, 0.02)),
             Matrix.Rotation(random.uniform(0, 3), 3, 'Z'), m, c, 0.15)
    stud(B, Vector((0.385, 0.05, 0.02)), (0.02, 0.05, 0.06), None, "copper", jitter(C_COPPER), 0.1)   # a wire end sticking out

def rod(B, mat, col, end_col, band_col):
    """Round bar, radius 0.1, 1.2 long, upright (Godot capsule r 0.1, h 1.2): chamfered saw-cut ends, a paint-dipped
    end and a mill colour-code band."""
    seg = 12
    rings = [(-0.6, 0.078), (-0.582, 0.1), (-0.5, 0.1), (-0.44, 0.1), (-0.4, 0.1), (0.4, 0.1), (0.46, 0.1), (0.582, 0.1), (0.6, 0.078)]
    fs, bands, caps = rod_body(B, rings, seg, mat, col, 0.08)
    for i in (0, 1):                                                          # dipped end
        B.paint(bands[i], end_col, 0.08)
    B.paint(bands[3], band_col, 0.05)                                         # colour-code band
    B.paint(bands[5], mix(col, C_BLACK, 0.3), 0.1)                            # hand-worn grip mark
    B.paint(bands[7] + [caps[1]], mix(col, C_WEAR, 0.5), 0.08)                # bright saw cut
    B.paint([caps[0]], end_col, 0.05)
    return fs

def checker_plate(B, mat, col):
    """0.8 x 0.8 x 0.08 checker plate (Godot box 0.8, 0.08, 0.8): chamfered sheet with 6x6 alternating lugs."""
    B.box(Vector((0, 0, -0.01)), (0.8, 0.8, 0.06), I3, mat, col, 0.12, rust=0.12)
    for i in range(6):
        for j in range(6):
            a = 0.6 if (i + j) % 2 else -0.6
            ridge(B, Vector((-0.3 + i * 0.12, -0.3 + j * 0.12, 0.02)), 0.085, 0.022, 0.011, a, mat, [c * 0.92 for c in col])

def sheet_plate(B, mat, col):
    """0.8 x 0.8 x 0.07 rolled sheet (Godot box 0.8, 0.08, 0.8): chamfered edges, brushed grain, mill stamp."""
    B.box(Vector((0, 0, -0.005)), (0.8, 0.8, 0.07), I3, mat, col, 0.06, bevel=0.016)
    z = 0.0301
    for k in range(7):                                                        # rolling grain: faint lengthwise strips
        y = -0.33 + k * 0.11 + random.uniform(-0.02, 0.02)
        vs = [B.bm.verts.new(Vector(p)) for p in ((-0.37, y - 0.02, z), (0.37, y - 0.02, z), (0.37, y + 0.02, z), (-0.37, y + 0.02, z))]
        f = B.quad(vs, mat); f.normal_update()
        if f.normal.z < 0: f.normal_flip()
        B.paint([f], [c * random.uniform(0.92, 1.06) for c in col], 0.03)
    stud(B, Vector((0.24, -0.28, z + 0.002)), (0.18, 0.08, 0.004), None, mat, mix(col, C_PATINA, 0.35), 0.05)
    for k in range(2):
        stud(B, Vector((0.2 + k * 0.07, -0.28, z + 0.006)), (0.035, 0.05, 0.004), None, mat, [c * 0.55 for c in col], 0.05)

def scrap_ball(B, coll):
    """Crushed bale of scrap, radius ~0.36, cinched with a steel strap."""
    cols = [(0.45, 0.46, 0.5), (0.35, 0.18, 0.08), (0.25, 0.26, 0.28), (0.55, 0.5, 0.4), (0.3, 0.33, 0.3)]
    fs, surf = lump(B, Vector((0, 0, 0)), 0.31, 7.0, 3, 0.3, "metal", lambda p, n: jitter(random.choice(cols), 0.25), (1, 1, 0.95))
    for f in random.sample(fs, 40):                                           # copper wire and pipe bits in the crush
        f.material_index = MI["copper"]
        set_col(B, [f], jitter(C_COPPER, 0.2))
    for k in range(10):                                                       # bent strips poking out of the bale
        d = rand_dir()
        p = surf(d) - d * 0.03
        stud(B, p + d * 0.04, (0.018, random.uniform(0.07, 0.12), random.uniform(0.1, 0.14)),
             rot_to(d) @ Matrix.Rotation(random.uniform(0, 3), 3, 'Z') @ Matrix.Rotation(math.pi / 2, 3, 'X'),
             random.choice(("metal", "copper")), jitter(random.choice(cols + [C_COPPER])), 0.1)
    ring(B, Vector((0, 0, 0.02)), (0.15, 0.1, 1), 0.3, 0.335, 0.035, 12, "metal", (0.55, 0.56, 0.58), 0.1, rust=0.3)  # strap

# ---------------------------------------------------------------------------- base resources
def coal(B, coll):
    """Bituminous coal: blocky lump with alternating bright (vitrain) and dull bands, and a split-off chip."""
    def band(p, n):
        s = math.sin(p.z * 38 + p.x * 6)
        return jitter(C_COAL if s > 0.2 else (0.1, 0.1, 0.1), 0.25)
    fs, surf = lump(B, Vector((0.02, 0, 0)), 0.28, 3.0, 2, 0.32, "gem", band, (1.1, 0.95, 0.85), detail=0.8)
    for f in fs:                                                              # dull bands get the matte material
        p = f.calc_center_median()
        if math.sin(p.z * 38 + p.x * 6) <= 0.2:
            f.material_index = MI["rock"]
    for k in range(2):
        d = Vector((random.uniform(-1, 1), random.uniform(-1, 1), random.uniform(-0.3, 0.6))).normalized()
        lump(B, surf(d) * 0.66, 0.12, 11 + k, 2, 0.35, "gem", lambda p, n: jitter(C_COAL, 0.3), (1, 1, 0.8))

def salt(B, coll):
    """Halite cube with stepped hopper terraces on each face (Godot box 0.5)."""
    B.box(Vector((0, 0, 0)), (0.49, 0.49, 0.49), I3, "gem", C_SALT, 0.06, bevel=0.018)
    for n in (Vector((1, 0, 0)), Vector((-1, 0, 0)), Vector((0, 1, 0)), Vector((0, -1, 0)), Vector((0, 0, 1)), Vector((0, 0, -1))):
        R = facing_basis(n)
        steps = [(0.2, 0.0), (0.2, 0.004), (0.14, 0.004), (0.14, 0.008), (0.075, 0.008)]
        rings = [[n * (0.245 + h) + R @ Vector((sx * s, sy * s, 0)) for sx, sy in ((-1, -1), (1, -1), (1, 1), (-1, 1))] for s, h in steps]
        fs = B.tube_rings(rings, "gem", C_SALT, 0.05, 0.0, cap=False, smooth=False)
        f = B.quad([B.bm.verts.new(p) for p in rings[-1]], "gem"); f.normal_update()
        if f.normal.dot(n) < 0: f.normal_flip()
        fs.append(f)
        for i, band in enumerate([fs[j * 4:(j + 1) * 4] for j in range(4)] + [[f]]):
            B.paint(band, mix(C_SALT, (0.95, 0.78, 0.78), 0.5 if i in (2, 3) else 0.1 * i), 0.04)

def floatstone(B, coll):
    """Pumice: rounded, pale and riddled with vesicles."""
    fs, surf = lump(B, Vector((0, 0, 0)), 0.36, 5.0, 3, 0.16, "rock", lambda p, n: jitter(mix(C_PUMICE, (0.6, 0.58, 0.52), max(0, -p.z * 2)), 0.1),
                    (1.05, 1.0, 0.88), detail=0.6)
    pits(B, surf, 34, 0.02, 0.05, 0.05, "rock", (0.28, 0.25, 0.2), 0.15)

def frost(B, coll):
    """Cluster of flat-topped ice columns round a glowing frozen core (Godot box 0.5)."""
    lump(B, Vector((0, 0, -0.06)), 0.14, 9.0, 2, 0.2, "gem", lambda p, n: jitter(C_FROST, 0.08), (1, 1, 0.9))
    for k in range(11):
        d = Vector((random.uniform(-1, 1), random.uniform(-1, 1), random.uniform(-0.3, 1))).normalized()
        L_ = random.uniform(0.18, 0.3)
        tip = d * L_
        tip = Vector((max(-0.24, min(0.24, tip.x)), max(-0.24, min(0.24, tip.y)), max(-0.24, min(0.24, tip.z))))
        fs = column(B, d * 0.06, tip, random.uniform(0.045, 0.075), 6, "gem", jitter(C_FROST, 0.1), 0.05, tip=0.18, flat_tip=True)
        B.paint(fs[-7:], (0.93, 0.97, 1.0), 0.03)                           # frosted terminations
    B.cyl(Vector((0, 0, -0.1)), Vector((0, 0, 0.0)), 0.07, 7, "cyan", C_CYAN, 0.02)

def lodestone(B, coll):
    """Dark magnetite with painted poles and iron filings bristling along its field."""
    def pole(p, n):
        if p.z > 0.27: return jitter(mix(C_LODE, C_N, 0.6), 0.1)
        if p.z < -0.27: return jitter(mix(C_LODE, C_S, 0.6), 0.1)
        return jitter(C_LODE, 0.25)
    fs, surf = lump(B, Vector((0, 0, 0)), 0.34, 13.0, 3, 0.22, "rock", pole, (1.0, 1.05, 0.95), detail=0.7)
    for k in range(50):                                                       # filings: dipole field lines leaving the poles
        e = random.uniform(0.3, 1.0) * random.choice((-1, 1)); a = random.uniform(0, math.tau)
        d = Vector((math.cos(a) * math.sqrt(1 - e * e), math.sin(a) * math.sqrt(1 - e * e), e))
        p = surf(d) - d * 0.01
        field = (d * 2 * abs(d.z) + Vector((0, 0, 1)) * (d.z if abs(d.z) < 0.6 else 0) * 0.8).normalized()
        L_ = random.uniform(0.07, 0.11)
        spike(B, [p, p + field * L_], 0.01, 3, "steel", (0.72, 0.72, 0.76), 0.1)

def quartz(B, coll):
    """Doubly-terminated quartz point with striated faces, milky base and two sister crystals (capsule r 0.18, h 0.9)."""
    seg = 6
    ring_ = lambda z, r: [Vector((math.cos(k / seg * math.tau) * r, math.sin(k / seg * math.tau) * r, z)) for k in range(seg)]
    zs = [(-0.45, 0.0), (-0.24, 0.165), (-0.14, 0.17), (-0.02, 0.168), (0.06, 0.165), (0.16, 0.16), (0.2, 0.158), (0.45, 0.0)]
    fs = B.tube_rings([ring_(z, max(r, 0.002)) for z, r in zs], "gem", C_QUARTZ, 0.04, 0.0, cap=False, smooth=False)
    for i in range(len(zs) - 1):                                              # striations and a milky lower half
        band = fs[i * seg:(i + 1) * seg]
        base = mix(C_QUARTZ, (0.7, 0.66, 0.62), 0.6) if zs[i][0] < -0.2 else C_QUARTZ
        B.paint(band, [c * (0.93 if i % 2 else 1.0) for c in base], 0.03)
    for k in range(2):
        a = k * 2.6 + 0.5
        b = Vector((math.cos(a) * 0.06, math.sin(a) * 0.06, -0.14))
        d = Vector((math.cos(a) * 0.45, math.sin(a) * 0.45, 1)).normalized()
        column(B, b, b + d * random.uniform(0.2, 0.26), 0.045, 6, "gem", (0.95, 0.86, 0.95), 0.04, tip=0.35)

def latex(B, coll):
    """Sticky amber blob, smooth and glossy, sagging into drips (radius ~0.33)."""
    fs, surf = lump(B, Vector((0, 0, 0.02)), 0.3, 21.0, 3, 0.12, "gem", lambda p, n: jitter(mix(C_LATEX, (0.55, 0.3, 0.06), max(0, -p.z * 2.5)), 0.05),
                    (1.1, 1.05, 0.78), smooth=True)
    for k in range(4):
        a = k / 4 * math.tau + random.uniform(-0.4, 0.4)
        d = Vector((math.cos(a), math.sin(a), -0.35)).normalized()
        p = surf(d) - d * 0.03
        drop = random.uniform(0.07, 0.11)
        pts = [p, p + Vector((d.x * 0.03, d.y * 0.03, -drop * 0.5)), p + Vector((d.x * 0.035, d.y * 0.035, -drop))]
        rings = [[q + Vector((math.cos(t) * r, math.sin(t) * r, 0)) for t in [j / 6 * math.tau for j in range(6)]]
                 for q, r in zip(pts, (0.045, 0.028, 0.034))]
        dfs = B.tube_rings(rings, "gem", mix(C_LATEX, (0.55, 0.3, 0.06), 0.5), 0.05, 0.0, cap=True, smooth=True)

def sulfur(B, coll):
    """Pale crust bearing a druse of bright orthorhombic (double-pointed) sulfur crystals."""
    lump(B, Vector((0, 0, -0.07)), 0.26, 23.0, 2, 0.28, "rock", lambda p, n: jitter((0.7, 0.64, 0.3) if n.z > 0.2 else (0.45, 0.42, 0.3), 0.15), (1.15, 1.05, 0.72))
    for k in range(16):
        a = random.uniform(0, math.tau); rr = random.uniform(0, 0.2)
        b = Vector((math.cos(a) * rr, math.sin(a) * rr, 0.04 - rr * 0.35))
        d = Vector((random.uniform(-0.6, 0.6), random.uniform(-0.6, 0.6), 1)).normalized()
        s = random.uniform(0.045, 0.075)
        bipyramid(B, b + d * s * 1.2, d, s, s * 1.5, s * 2.1, 4, "gem", jitter(C_SULFUR, 0.08), 0.05, spin=random.uniform(0, 1))

def quicksilver(B, coll):
    """A bead of liquid metal (radius 0.28), flattened by its weight, darker where it meets the floor."""
    fs = ball(B, 0.28, 24, 13, "steel", zlo=0.84, zhi=0.9)
    for f in fs:
        z = f.calc_center_median().z
        set_col(B, [f], mix(C_MERCURY, (0.35, 0.36, 0.4), max(0.0, -z * 3)))

def voltaic(B, coll):
    """Violet crystal spray on a dark matrix, with glowing charge in its cores (Godot box 0.5, 0.7, 0.5)."""
    lump(B, Vector((0, 0, -0.24)), 0.16, 27.0, 2, 0.2, "gem", lambda p, n: jitter((0.12, 0.1, 0.16), 0.2), (1.25, 1.25, 0.6))
    for k in range(8):
        d = Vector((random.uniform(-0.7, 0.7), random.uniform(-0.7, 0.7), 1)).normalized()
        b = Vector((random.uniform(-0.07, 0.07), random.uniform(-0.07, 0.07), -0.2))
        tip = b + d * random.uniform(0.4, 0.56)
        tip.z = min(tip.z, 0.34)
        fs = column(B, b, tip, random.uniform(0.05, 0.075), 6, "gem", jitter(C_VOLTAIC, 0.1), 0.05, tip=0.3)
        B.paint(fs[6:], mix(C_VOLTAIC, (0.85, 0.75, 1.0), 0.45), 0.04)        # lighter terminations
        B.cyl(b, b + (tip - b) * 0.62, 0.022, 4, "violet", C_VIOLET, 0.02)

# ---------------------------------------------------------------------------- products
def coke(B, coll):
    """Pressed coke pillow (Godot box 0.45, 0.3, 0.45) with a mould seam and embers glowing in its pores."""
    rings = [rr_ring(z, 0.225 * s, 0.225 * s, 0.07 * s) for z, s in ((-0.15, 0.74), (-0.11, 0.94), (-0.004, 1.0), (0.004, 1.0), (0.11, 0.94), (0.15, 0.74))]
    fs = B.tube_rings([[Vector(p) for p in r] for r in rings], "rock", (0.16, 0.16, 0.17), 0.2, 0.0, cap=True, smooth=True)
    B.paint(fs[16:24], (0.3, 0.3, 0.31), 0.1)                                 # pressing seam
    for sgn in (-1, 1):
        for k in range(6):
            p = Vector((random.uniform(-0.12, 0.12), random.uniform(-0.12, 0.12), sgn * 0.1505))
            r = random.uniform(0.012, 0.024)
            vs = [B.bm.verts.new(p + Vector((math.cos(a) * r, math.sin(a) * r, 0))) for a in [j / 5 * math.tau for j in range(5)]]
            f = B.bm.faces.new(vs); f.material_index = MI["molten"]; f.normal_update()
            if f.normal.z * sgn < 0: f.normal_flip()
            B.paint([f], C_MOLTEN, 0.2)

def glass_pane(B, coll):
    """Float-glass pane (Godot box 0.9, 0.05, 0.9): ground edges, a green edge tint and a sheen streak."""
    B.box(Vector((0, 0, 0)), (0.86, 0.86, 0.05), I3, "glass", (0.6, 0.9, 0.95), 0.02, bevel=0.01)
    for n in (Vector((1, 0, 0)), Vector((-1, 0, 0)), Vector((0, 1, 0)), Vector((0, -1, 0))):
        B.box(n * 0.44, (0.02 if n.x else 0.9, 0.02 if n.y else 0.9, 0.05), I3, "cyan", (0.45, 0.95, 0.8), 0.02, bevel=0.005)
    for off, w in ((0.0, 0.05), (0.09, 0.02)):                                # diagonal reflection streaks
        R = Matrix.Rotation(math.radians(40), 3, 'Z')
        vs = [B.bm.verts.new(R @ Vector((x, off + y, 0.0255))) for x, y in ((-0.45, -w), (0.45, -w), (0.45, w), (-0.45, w))]
        for v in vs:
            v.co.x = max(-0.42, min(0.42, v.co.x)); v.co.y = max(-0.42, min(0.42, v.co.y))
        f = B.quad(vs, "glass"); f.normal_update()
        if f.normal.z < 0: f.normal_flip()
        B.paint([f], (0.72, 0.93, 0.97), 0.01)

def quartz_shards(B, coll):
    """Broken slivers of quartz: sharp, irregular bipyramids piled together (Godot sphere r 0.22)."""
    for k in range(10):
        d = rand_dir()
        c = d * random.uniform(0.04, 0.1)
        ax = (d + rand_dir() * 0.6).normalized()
        s = random.uniform(0.04, 0.065)
        bipyramid(B, c, ax, s, random.uniform(0.05, 0.08), random.uniform(0.1, 0.13), random.choice((3, 4)), "gem",
                  mix(C_QUARTZ, (0.95, 0.85, 0.95), random.uniform(0, 0.6)), 0.04, spin=random.uniform(0, 3))

def rubber_ball(B, coll):
    """Cured rubber ball: red with a yellow equator band and a dark moulding sprue at the pole."""
    fs = ball(B, 0.3, 20, 12, "rubber")
    for f in fs:
        c = f.calc_center_median()
        B.paint([f], C_YELLOW if abs(c.z) < 0.05 else ((0.5, 0.06, 0.05) if c.z > 0.285 else C_RUBBER_RED), 0.05)

def blast_charge(B, coll):
    """Sulfur-coal charge in a painted steel can (Godot capsule r 0.2, h 0.7): rolled rims, hazard band, fuse."""
    seg = 16
    rings = [(-0.35, 0.17), (-0.335, 0.2), (-0.29, 0.2), (-0.28, 0.188), (-0.16, 0.188), (-0.07, 0.188), (0.06, 0.188), (0.21, 0.188),
             (0.22, 0.2), (0.265, 0.2), (0.28, 0.17)]
    fs, bands, caps = rod_body(B, rings, seg, "panel", (0.7, 0.1, 0.06), 0.1)
    for i in (0, 1, 7, 8, 9):
        B.paint(bands[i], C_DARK, 0.15)                                       # rolled rims
    for k, f in enumerate(bands[4]):                                          # hazard band
        B.paint([f], C_YELLOW if k % 2 else C_BLACK, 0.1)
    B.paint(bands[5], (0.82, 0.8, 0.74), 0.05)                                # label
    B.paint(caps, C_DARK, 0.15)
    B.cyl(Vector((0, 0, 0.28)), Vector((0, 0, 0.31)), 0.05, 8, "panel", C_STEEL, 0.1)
    B.pipe([Vector((0, 0, 0.31)), Vector((0.03, 0, 0.34)), Vector((0.07, 0.02, 0.33))], 0.012, 5, "panel", (0.25, 0.18, 0.1))
    B.cyl(Vector((0.07, 0.02, 0.33)), Vector((0.085, 0.02, 0.33)), 0.016, 5, "molten", C_MOLTEN, 0.05)

def battery(B, coll):
    """Voltaic cell (Godot capsule r 0.22, h 0.6): copper can, black insulating wrap, a glowing charge window."""
    seg = 16
    rings = [(-0.3, 0.15), (-0.285, 0.17), (-0.27, 0.21), (-0.12, 0.21), (-0.1, 0.215), (-0.02, 0.215), (0.04, 0.215), (0.1, 0.215),
             (0.12, 0.21), (0.23, 0.21), (0.245, 0.18)]
    fs, bands, caps = rod_body(B, rings, seg, "copper", C_COPPER, 0.08)
    for i in (0, 1):
        B.paint(bands[i], C_STEEL, 0.05)                                      # negative base
    for i in (3, 4, 5, 6):
        B.paint(bands[i], (0.04, 0.04, 0.045), 0.05)                          # insulating wrap
    B.paint([caps[0]], C_STEEL, 0.05)
    ring(B, Vector((0, 0, 0.01)), (0, 0, 1), 0.214, 0.222, 0.05, 16, "violet", C_VIOLET, 0.02)
    B.cyl(Vector((0, 0, 0.245)), Vector((0, 0, 0.3)), 0.07, 10, "copper", C_STEEL, 0.05)       # positive terminal
    B.cyl(Vector((0, 0, 0.245)), Vector((0, 0, 0.255)), 0.12, 10, "copper", (0.1, 0.1, 0.1), 0.05)

def magnet_core(B, coll):
    """Pressed lodestone core wound with copper, poles painted (Godot box 0.5)."""
    B.box(Vector((0, 0, 0)), (0.46, 0.44, 0.44), I3, "metal", C_LODE, 0.15, bevel=0.02)
    for sx, col in ((1, C_N), (-1, C_S)):                                   # pole caps
        B.box(Vector((sx * 0.24, 0, 0)), (0.02, 0.5, 0.5), I3, "metal", col, 0.08, bevel=0.006)
        stud(B, Vector((sx * 0.251, 0, 0)), (0.12, 0.06, 0.004), Matrix.Rotation(sx * math.pi / 2, 3, 'Y'), "metal", (0.9, 0.9, 0.86), 0.02)
    rings = []
    x = -0.2
    for k in range(7):                                                       # the winding: one bumpy square coil
        rings.append(rr_ring(0, 0.235, 0.235, 0.05))
        rings.append(rr_ring(0, 0.248, 0.248, 0.055))
    xs = [-0.2 + i * 0.4 / (len(rings) - 1) for i in range(len(rings))]
    loops = [[Vector((xx, p[0], p[1])) for p in r] for xx, r in zip(xs, rings)]
    fs = B.tube_rings(loops, "copper", C_COPPER, 0.08, 0.0, cap=False, smooth=True)
    for i in range(len(loops) - 1):
        if i % 2 == 1:
            B.paint(fs[i * 8:(i + 1) * 8], [c * 0.6 for c in C_COPPER], 0.05)

def aerogel(B, coll):
    """Frozen-smoke tile (Godot box 0.8, 0.1, 0.8): soft-edged haze round a brighter core."""
    B.box(Vector((0, 0, 0)), (0.8, 0.8, 0.1), I3, "glass", (0.6, 0.75, 1.0), 0.02, bevel=0.03)
    B.box(Vector((0, 0, 0)), (0.66, 0.66, 0.06), I3, "cyan", (0.35, 0.5, 0.9), 0.02, bevel=0.02)

# ---------------------------------------------------------------------------- puzzle-room resources
def scree(B, coll):
    """A clump of five smooth water-worn pebbles (Godot sphere r 0.25): rolls freely."""
    pal = [(0.55, 0.5, 0.42), (0.35, 0.33, 0.3), (0.6, 0.45, 0.32), (0.48, 0.47, 0.45)]
    lump(B, Vector((0, 0, 0)), 0.17, 41.0, 2, 0.1, "rock", lambda p, n: jitter((0.48, 0.44, 0.38), 0.08), (1.05, 1, 0.82), smooth=True)
    for k in range(4):
        a = k / 4 * math.tau + 0.4
        d = Vector((math.cos(a), math.sin(a), random.uniform(-0.3, 0.3))).normalized()
        c = random.choice(pal)
        lump(B, d * 0.13, random.uniform(0.08, 0.11), 42 + k, 2, 0.1, "rock", lambda p, n, c=c: jitter(c, 0.06),
             (1.1, 1, 0.75), smooth=True)

def shale(B, coll):
    """Stacked shale leaves with broken outlines (Godot box 0.9, 0.14, 0.8): slides only on steep slopes."""
    for k, (dz, s) in enumerate(((-0.05, 1.0), (-0.017, 0.95), (0.017, 0.9), (0.05, 0.82))):
        n = 10
        pts = []
        for j in range(n):
            a = j / n * math.tau + 0.3
            ca, sa = math.cos(a), math.sin(a)
            k_ = 1 / max(abs(ca) / 0.45, abs(sa) / 0.4)                       # on the 0.9 x 0.8 rectangle
            f = s * random.uniform(0.88, 1.0)
            pts.append((ca * k_ * f, sa * k_ * f))
        cx, cy = random.uniform(-0.015, 0.015), random.uniform(-0.015, 0.015)
        rings = [[Vector((max(-0.45, min(0.45, cx + x)), max(-0.4, min(0.4, cy + y)), dz + h)) for x, y in pts] for h in (-0.017, 0.017)]
        col = jitter((0.22, 0.25, 0.3), 0.1)
        fs = B.tube_rings(rings, "rock", col, 0.12, 0.0, cap=True, smooth=False)
        B.paint(fs[:n], mix(col, (0.5, 0.48, 0.44), 0.35), 0.15)               # weathered broken edges
    fos = [B.bm.verts.new(Vector((0.12 + math.cos(a) * r, -0.08 + math.sin(a) * r, 0.0675)))           # a fossil imprint on top
           for a, r in [(j * 0.9, 0.012 + j * 0.006) for j in range(9)]]
    for a, b in zip(fos, fos[1:]):
        B.bm.edges.new((a, b))
    f = B.bm.faces.new(fos); f.material_index = MI["rock"]; f.normal_update()
    if f.normal.z < 0: f.normal_flip()
    B.paint([f], (0.5, 0.5, 0.48), 0.05)

def puck(B, coll):
    """Polished slickstone disc with a rounded rim and a glowing inlay (Godot box 0.7, 0.16, 0.7)."""
    seg = 24
    fs, bands, caps = rod_body(B, [(-0.08, 0.31), (-0.065, 0.342), (-0.03, 0.35), (0.03, 0.35), (0.065, 0.342), (0.08, 0.31)], seg, "gem",
                               (0.08, 0.1, 0.14), 0.05)
    for f in fs:
        f.smooth = True
    ring(B, Vector((0, 0, 0)), (0, 0, 1), 0.345, 0.352, 0.03, seg, "cyan", C_CYAN, 0.02)
    B.cyl(Vector((0, 0, 0.08)), Vector((0, 0, 0.084)), 0.12, 12, "cyan", C_CYAN, 0.02)

def burr(B, coll):
    """Hooked seed burr (Godot sphere r 0.3): a fibrous core bristling with hooked spines."""
    lump(B, Vector((0, 0, 0)), 0.16, 51.0, 2, 0.12, "rock", lambda p, n: jitter((0.35, 0.28, 0.12), 0.15), (1, 1, 1))
    N = 36
    for k in range(N):
        z = 1 - 2 * (k + 0.5) / N; r = math.sqrt(1 - z * z); a = k * 2.4
        d = Vector((math.cos(a) * r, math.sin(a) * r, z))
        side = d.cross(Vector((0.3, 0.2, 1)) if abs(d.z) < 0.9 else Vector((1, 0, 0))).normalized()
        spike(B, [d * 0.14, d * 0.24, d * 0.3 + side * 0.03, d * 0.28 + side * 0.055], 0.018, 3, "rock",
              jitter((0.5, 0.42, 0.18), 0.1), 0.05, tip_col=(0.78, 0.72, 0.5))

def ballast(B, coll):
    """Cast ballast shot (Godot sphere r 0.35): heavy iron ball with a parting-line ridge, painted band and sprue."""
    fs = ball(B, 0.35, 20, 12, "metal")
    B.paint(fs, (0.2, 0.2, 0.22), 0.1, rust=0.3)
    ring(B, Vector((0, 0, 0)), (0, 0, 1), 0.34, 0.354, 0.05, 20, "panel", C_YELLOW, 0.05)
    B.cyl(Vector((0, 0, 0.33)), Vector((0, 0, 0.352)), 0.05, 7, "metal", (0.3, 0.29, 0.28), 0.2, rust=0.4)   # ground-off sprue

# ---------------------------------------------------------------------------- electric chain (Room 5)
def storm_sand(B, coll):
    """Clotted storm-flat sand (Godot sphere r 0.3): a crumbly clod with quartz grains glinting in it."""
    fs, surf = lump(B, Vector((0, 0, 0)), 0.25, 61.0, 2, 0.22, "rock", lambda p, n: jitter(mix(C_SAND, (0.35, 0.34, 0.38), max(0, -p.z * 2)), 0.12),
                    (1.1, 1.05, 0.9), detail=0.9)
    for k in range(4):
        d = rand_dir(-0.5, 0.8)
        lump(B, surf(d) * 0.9, random.uniform(0.07, 0.1), 62 + k, 1, 0.2, "rock", lambda p, n: jitter(C_SAND, 0.15), (1, 1, 0.85))
    for k in range(12):
        d = rand_dir()
        bipyramid(B, surf(d), d, random.uniform(0.012, 0.02), 0.01, random.uniform(0.02, 0.035), 3, "gem", (0.85, 0.85, 0.8), 0.05,
                  spin=random.uniform(0, 3))

def fulgurite(B, coll, charged=True):
    """Lightning-fused glass tube (Godot capsule r 0.14, h 0.8): a knobbly, crusted tube with a hollow glassy bore
    and a side branch; charged ones glow violet in the bore and along their cracks."""
    random.seed(71)                                                           # charged and spent share one shape
    seg = 8
    rings, pts = [], []
    for i in range(10):
        z = -0.4 + i * 0.8 / 9
        c = Vector((math.sin(i * 1.7) * 0.015, math.cos(i * 1.3) * 0.015, z))
        r = 0.105 + noise.noise(Vector((i * 0.7, 3.0, 1.0))) * 0.025 + (0.012 if i % 3 == 1 else 0.0)
        r *= 0.75 if i in (0, 9) else 1.0
        pts.append(c)
        rings.append([c + Vector((math.cos(k / seg * math.tau) * r * random.uniform(0.85, 1.12), math.sin(k / seg * math.tau) * r * random.uniform(0.85, 1.12), 0))
                      for k in range(seg)])
    body = (0.36, 0.33, 0.36) if charged else (0.33, 0.3, 0.26)
    fs = B.tube_rings(rings, "gem", body, 0.25, 0.0, cap=False, smooth=False)
    for f in fs:
        if random.random() < 0.35:
            B.paint([f], mix(body, C_SAND, 0.5), 0.2)                         # clinging fused sand
    top, bot = rings[-1], rings[0]
    inner = lambda rg, dz: [pts[-1] + (p - pts[-1]) * 0.55 + Vector((0, 0, dz)) for p in rg]
    glow = "violet" if charged else "gem"
    gcol = C_VIOLET if charged else (0.12, 0.11, 0.1)
    bore = B.tube_rings([top, inner(top, -0.01), inner(top, -0.07)], "gem", (0.55, 0.55, 0.6), 0.05, 0.0, cap=False, smooth=False)
    f = B.quad([B.bm.verts.new(p) for p in inner(top, -0.07)], glow); f.normal_update()
    if f.normal.z < 0: f.normal_flip()
    B.paint([f], gcol, 0.02)
    f = B.quad([B.bm.verts.new(p) for p in bot], "gem"); f.normal_update()
    if f.normal.z > 0: f.normal_flip()
    B.paint([f], (0.2, 0.18, 0.18), 0.1)
    b0 = pts[5] + Vector((0.06, 0, 0))                                        # side branch, kept inside the capsule
    spike(B, [b0, b0 + Vector((0.05, 0.02, 0.06)), b0 + Vector((0.06, 0.03, 0.14))], 0.04, 5, "gem", body, 0.2)
    if charged:                                                               # glowing cracks running up the tube
        for j, k0 in enumerate((1, 5)):
            for i in range(1, 8, 2):
                k = (k0 + (i // 2)) % seg
                a, b = rings[i][k], rings[i + 1][(k + 1) % seg]
                n = ((a + b) / 2 - pts[i]).normalized()
                t = (b - a).normalized(); s = t.cross(n).normalized() * 0.008
                vs = [B.bm.verts.new(p + n * 0.006) for p in (a - s, a + s, b + s, b - s)]
                cf = B.quad(vs, "violet"); cf.normal_update()
                if cf.normal.dot(n) < 0: cf.normal_flip()
                B.paint([cf], C_VIOLET, 0.02)

def capacitor_cell(B, coll):
    """Fulgurite charge sealed in a copper-wound can (Godot box 0.5, 0.6, 0.5): dark anodised can, ribbed winding,
    ceramic-orange end rims, two terminals and a glowing charge window."""
    seg = 14
    rings = [(-0.3, 0.2), (-0.285, 0.235), (-0.24, 0.235), (-0.23, 0.225), (0.2, 0.225), (0.21, 0.235), (0.255, 0.235), (0.27, 0.2)]
    fs, bands, caps = rod_body(B, rings, seg, "copper", (0.06, 0.06, 0.07), 0.08)
    for i in (0, 1, 5, 6):
        B.paint(bands[i], (0.8, 0.42, 0.12), 0.1)                             # insulating rims
    wz = [(-0.175 + i * 0.04, 0.245 if i % 2 else 0.226) for i in range(9)]
    wfs, wb, _ = rod_body(B, wz, seg, "copper", C_COPPER, 0.08, cap=False)
    for i, band in enumerate(wb):
        if i % 2:
            B.paint(band, [c * 0.65 for c in C_COPPER], 0.05)
    for sx in (-1, 1):
        B.cyl(Vector((sx * 0.09, 0, 0.27)), Vector((sx * 0.09, 0, 0.3)), 0.035, 8, "copper", C_STEEL, 0.05)
    ring(B, Vector((0, 0, 0.185)), (0, 0, 1), 0.226, 0.232, 0.02, seg, "violet", C_VIOLET, 0.02)
    B.cyl(Vector((0, 0, 0.27)), Vector((0, 0, 0.273)), 0.05, 8, "violet", C_VIOLET, 0.02)

# ---------------------------------------------------------------------------- sticky chain (Room 6)
def tar_blob(B, coll):
    """Warm, glossy tar (Godot sphere r 0.32): a sagging smooth blob with gas blisters and a trailing string."""
    fs, surf = lump(B, Vector((0, 0, -0.02)), 0.285, 81.0, 3, 0.1, "gem", lambda p, n: jitter(mix(C_TAR, (0.12, 0.07, 0.03), max(0, n.z) * 0.4), 0.2),
                    (1.12, 1.06, 0.74), smooth=True)
    for k in range(4):
        d = rand_dir(0.2, 1.0)
        lump(B, surf(d) - d * 0.015, random.uniform(0.03, 0.05), 90 + k, 1, 0.05, "gem", lambda p, n: (0.06, 0.04, 0.03), (1, 1, 0.8), smooth=True)
    d = Vector((1, 0.3, -0.2)).normalized(); p = surf(d)
    spike(B, [p - d * 0.02, p + Vector((0.03, 0.01, -0.06)), p + Vector((0.035, 0.01, -0.13))], 0.03, 5, "gem", C_TAR, 0.1)

def chalk_nodule(B, coll):
    """A soft white nodule (Godot sphere r 0.22): knobbly, powdery, with a grey flint core showing at a chip."""
    fs, surf = lump(B, Vector((0, 0, 0)), 0.19, 101.0, 3, 0.2, "rock", lambda p, n: jitter(C_CHALK, 0.06), (1.05, 1, 0.92), detail=1.2)
    for f in fs:
        c = f.calc_center_median()
        if c.x > 0.1 and c.z > 0.05:
            set_col(B, [f], jitter((0.3, 0.31, 0.33), 0.1))                   # chipped: flint shows through
        elif c.z < -0.08:
            set_col(B, [f], jitter(mix(C_CHALK, (0.55, 0.52, 0.45), 0.4), 0.05))

def coated_pellet(B, coll):
    """A tar blob rolled in chalk (Godot sphere r 0.33): a round, dry-looking ball, tar showing through the dust."""
    fs, surf = lump(B, Vector((0, 0, 0)), 0.31, 111.0, 3, 0.05, "rock", lambda p, n: jitter(C_CHALK, 0.1), (1, 1, 0.97), smooth=True)
    off = Vector((5.0, 2.0, 1.0))
    for f in fs:
        c = f.calc_center_median()
        n = noise.noise(c * 6.0 + off)
        if n > 0.42:
            f.material_index = MI["gem"]
            set_col(B, [f], jitter((0.1, 0.09, 0.08), 0.3))
        elif n > 0.15:
            set_col(B, [f], jitter(mix(C_CHALK, (0.35, 0.33, 0.3), 0.45), 0.05))
        else:
            set_col(B, [f], jitter(C_CHALK, 0.05))

def tar_rock(B, coll):
    """Cured tar (Godot sphere r 0.34): a dull, angular, cracked lump with a grey oxidised bloom."""
    off = Vector((1.0, 7.0, 3.0))
    def col(p, n):
        crack = abs(noise.noise(p * 5.0 + off))
        if crack < 0.08:
            return (0.005, 0.005, 0.005)
        return jitter(mix((0.06, 0.06, 0.06), (0.3, 0.3, 0.29), max(0.0, n.z) * 0.5), 0.15)
    lump(B, Vector((0, 0, 0)), 0.285, 121.0, 3, 0.26, "rock", col, (1.0, 0.95, 0.85), detail=1.0)

def bitumen_brick(B, coll):
    """Pellets pressed into a brick (Godot box 0.8, 0.3, 0.4): chamfered block with a recessed frog on top and
    chalk-dusted pellet faces showing on its sides."""
    fs = slab(B, -0.15, 0.15, (0.4, 0.2), (0.4, 0.2), "rock", (0.05, 0.05, 0.05), 0.2, 0.0, e=0.02, k=0.025, dish=(0.03, 0.05))
    B.paint(fs[-1:], (0.12, 0.12, 0.12), 0.05)                                # frog floor
    for sy in (-1, 1):
        for i in range(6):
            for j in range(2):
                p = Vector((-0.3 + i * 0.12 + (0.06 if j else 0), sy * 0.2005, -0.06 + j * 0.12))
                r = 0.035
                vs = [B.bm.verts.new(p + Vector((math.cos(a) * r, 0, math.sin(a) * r))) for a in [q / 6 * math.tau for q in range(6)]]
                f = B.bm.faces.new(vs); f.material_index = MI["rock"]; f.normal_update()
                if f.normal.y * sy < 0: f.normal_flip()
                B.paint([f], jitter(mix(C_CHALK, (0.3, 0.3, 0.28), 0.5), 0.2), 0.1)
    stud(B, Vector((0, 0, 0.15 - 0.03 + 0.003)), (0.3, 0.012, 0.006), None, "rock", (0.2, 0.2, 0.2), 0.05)

PIECES_SPEC = [
    ("IronIngot", "iron_ingot", lambda B, c: ingot(B, "steel", C_IRON, (0.4, 0.42, 0.46), bloom=C_RUST, rust=0.1)),
    ("IronRod", "iron_rod", lambda B, c: rod(B, "steel", C_SCALE, (0.7, 0.12, 0.08), (0.9, 0.9, 0.86))),
    ("IronPlate", "iron_plate", lambda B, c: checker_plate(B, "steel", C_IRON)),
    ("CopperIngot", "copper_ingot", lambda B, c: ingot(B, "copper", C_COPPER, (0.6, 0.28, 0.12), bloom=C_PATINA)),
    ("CopperRod", "copper_rod", lambda B, c: rod(B, "copper", C_COPPER, (0.95, 0.6, 0.4), (0.15, 0.35, 0.8))),
    ("CopperPlate", "copper_plate", lambda B, c: sheet_plate(B, "copper", C_COPPER)),
    ("ScrapBall", "scrap_ball", scrap_ball),
    ("ScrapIngot", "scrap_ingot", scrap_ingot),
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
    ("StormSand", "storm_sand", storm_sand),
    ("Fulgurite", "fulgurite", lambda B, c: fulgurite(B, c, True)),
    ("SpentFulgurite", "spent_fulgurite", lambda B, c: fulgurite(B, c, False)),
    ("CapacitorCell", "capacitor_cell", capacitor_cell),
    ("TarBlob", "tar_blob", tar_blob),
    ("ChalkNodule", "chalk_nodule", chalk_nodule),
    ("CoatedPellet", "coated_pellet", coated_pellet),
    ("TarRock", "tar_rock", tar_rock),
    ("BitumenBrick", "bitumen_brick", bitumen_brick),
]
PIECES = [((lambda n=n, fn=fn: item(n, fn)), f"{file}.glb") for n, file, fn in PIECES_SPEC]
ICONS = [(f"Item_{n}", f"items/{file}.png", "item", (1.2, 1.3, 0.9)) for n, file, fn in PIECES_SPEC]

def build_all(do_export=True):
    return build_family(PIECES, OUT_DIR, do_export)

if __name__ == "__main__":
    build_all(do_export=False)
