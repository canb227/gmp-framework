"""
Chutes: gravity channels for solid items, in two tiers.

  basic   (chute_*.glb)      salvaged: sheet-steel channel, patchwork facility cladding, free rollers
  advanced (chute_adv_*.glb) refurbished: clean panels, glass windows, and a "Power" node holding the cyan
                             linear-motor emitters (show it / drive the push force when the chute is powered)

Pieces (all origin at the anchor cell centre, floor z = -1, front = +Y):
  h_straight      1x1x1  closed channel along +Y (lidded, open only at its ends), floor at belt height (z -0.85),
                         lid at z 0.35
  h_turn_right    1x1x1  enters at the back, leaves through +X (h_turn_left is the mirror)
  v_straight      1x1x1  vertical square bore (1.5 x 1.5 m) open at top and bottom
  v_turn          1x1x1  elbow: enters through the top face, leaves through the front face at channel height
  hopper_up       1x1x1  catching funnel: 1.9 m mouth on top, bore out of the bottom face
  dropper_down    1x1x1  bore from the top face with two trapdoor leaves ("DoorL"/"DoorR", hinged about Y at
                         z -0.15, open by turning +/-80 deg) that hold items until opened; "Lever" (tilts about X)
                         on the +X face is what players flip
  hopper_2x2      2x2x2  (footprint x -1..3, y -1..3, z -1..3) funnel into a bore out of the anchor cell's bottom
  hopper_3x3      3x3x2  anchored on its centre cell (x -3..3, y -3..3, z -1..3), bore out of the centre bottom

Channel section: interior x -0.75..0.75. Run: tools/blender/run.py build chutes
"""
import bpy, bmesh, math, random, os
from mathutils import Vector, Matrix

_HERE = (os.path.dirname(os.path.abspath(__file__)) if "__file__" in globals()
         else r"C:\Users\steph\OneDrive\Documents\godot\projects\gmp-framework\game\assets\models\chutes\source")
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

def streak(B, p, n, length=0.14, w=0.011, c=C_RUST):
    """Rust run washed down a surface (facing n) from a bolt at p: one tapering painted quad."""
    n = Vector(n).normalized(); dn = -ZV + n * n.z
    if dn.length < 1e-3:
        return
    dn.normalize(); u = n.cross(dn)
    p = p + n * 0.0015
    return decal(B, [p + u * w, p + dn * length + u * w * 0.2, p + dn * length - u * w * 0.2, p - u * w], n, "steel", c, 0.3)

EXPORT_KEEP.update({"Power", "Lever", "DoorL", "DoorR"})     # switchable / moving parts stay their own nodes

IN = 0.75            # interior half-width
WT = 0.08            # wall thickness
FLOOR_TOP = BELT_TOP # channel floor = belt height, so chutes and conveyors hand over level
H_WALL = 1.2         # open channel wall height above the floor
BORE_TOP = 1.5       # closed elbow: interior height
DOOR_Z = -0.15       # dropper trapdoor hinge height

def sx(side, a, b):
    return tuple(sorted((side * a, side * b)))

# ======================================================================================
# path based channels (horizontal straight / turn, vertical elbow)
# ======================================================================================
def vturn_path():
    """Floor (outer wall face) of the elbow: down from the top face, a quarter bend of centreline radius R,
    out through the front face. Frame 'up' points into the channel (toward the bend centre)."""
    R, zc = 0.9, 0.8
    s_v, s_a = 1.0 - zc, R * math.pi / 2
    def fn(s):
        if s <= s_v:
            ph, c = 0.0, Vector((0, 0, 1 - s))
        elif s <= s_v + s_a:
            ph = (s - s_v) / R
            c = Vector((0, R - R * math.cos(ph), zc - R * math.sin(ph)))
        else:
            ph = math.pi / 2
            c = Vector((0, R + (s - s_v - s_a), zc - R))
        t = Vector((0, math.sin(ph), -math.cos(ph))); up = Vector((0, math.cos(ph), math.sin(ph)))
        return (c - up * IN, t, Vector((1, 0, 0)), up)
    return Path(s_v + s_a + (1.0 - R), fn, 14)

def ribs_at(L, pitch):
    n = max(1, int(round(L / pitch)))
    return [min(max(L * i / n, 0.03), L - 0.03) for i in range(n + 1)]

def rib_bolts(B, path, s, top, rng, basic=True):
    """Bolts down the outer faces of a hoop rib (rusty on the salvage tier, with the odd rust run)."""
    sd = path.frame(s)[2]
    x = IN + WT + (0.1 if basic else 0.07)
    for side in (-1, 1):
        for o in (0.08, top * 0.5, top - 0.08):
            p = path.point(s, side * x, o)
            if max(abs(p.x), abs(p.y)) > 0.98:                                  # tight inner corner of a turn
                continue
            if basic:
                stud(B, p, sd * side, 0.013, 0.009, rust=0.5)
                if rng.random() < 0.3:
                    streak(B, p, sd * side, 0.16)
            else:
                stud(B, p, sd * side, 0.01, 0.006, "metal", (0.3, 0.3, 0.32), rust=0.0)

def channel_basic(B, path, rng, height, closed=False, rollers=False):
    L = path.L
    top = height
    # floor: wear plate (rollers ride just above it on the straight)
    if rollers:
        B.sweep(path, 0, L, -IN, IN, -0.1, -0.06, "steel", C_WEAR, 0.15, rust=0.3)
    else:
        B.sweep(path, 0, L, -IN, IN, -0.06, 0.0, "steel", C_WEAR, 0.15, rust=0.25)
    B.sweep(path, 0, L, -IN - WT, IN + WT, -0.13, -0.1 if rollers else -0.06, "metal", C_FRAME, 0.2, rust=0.4)
    if closed:
        B.sweep(path, 0, L, -IN - WT, IN + WT, top, top + WT, "steel", (0.3, 0.3, 0.3), 0.25, rust=0.5)
    for side in (-1, 1):
        B.sweep(path, 0, L, *sx(side, IN, IN + WT), -0.14, top, "steel", (0.3, 0.3, 0.3), 0.25, rust=0.55)
        B.sweep(path, 0, L, *sx(side, IN - 0.006, IN), 0.0, min(0.35, top), "steel", C_WEAR, 0.12, rust=0.1)   # liner
        # patchwork cladding panels on the outside
        cuts = panel_splits(L, 0.55, 1.0, rng)
        for i in range(len(cuts) - 1):
            a, b = cuts[i] + 0.015, cuts[i + 1] - 0.015
            r = rng.random()
            if r < 0.12:
                continue                                                        # missing: bare sheet shows
            col, mat = ((C_WHITE, "panel") if r < 0.72 else (C_DARK, "metal") if r < 0.86 else (C_STEEL, "steel"))
            o0, o1 = 0.02, top - (0.12 if not closed else 0.06)
            B.sweep(path, a, b, *sx(side, IN + WT, IN + WT + 0.02), o0, o1, mat, col, 0.3, rust=0.6 if mat == "steel" else 0.0)
        if not closed:
            B.sweep(path, 0, L, *sx(side, IN - 0.03, IN + WT + 0.05), top, top + 0.05, "metal", C_DARK, 0.2, rust=0.4)   # rolled rim
            if path is PATHS["straight"]:
                x = side * (IN + WT + 0.021)
                hazard(B, Vector((x, -1.0 if side > 0 else 1.0, FLOOR_TOP + top - 0.12)), (0, side, 0), (0, 0, 1), 2.0, 0.1, (side, 0, 0), pitch=0.12)
    # hoop ribs
    for s in ribs_at(L, 0.7):
        s0, s1 = max(0, s - 0.03), min(L, s + 0.03)
        for side in (-1, 1):
            B.sweep(path, s0, s1, *sx(side, IN + WT, IN + WT + 0.1), -0.14, top + 0.04, "metal", C_FRAME, 0.2, rust=0.4)
        B.sweep(path, s0, s1, -IN - WT - 0.1, IN + WT + 0.1, -0.14, -0.1, "metal", C_FRAME, 0.2, rust=0.4)
        if closed:
            B.sweep(path, s0, s1, -IN - WT - 0.1, IN + WT + 0.1, top + WT, top + WT + 0.05, "metal", C_FRAME, 0.2, rust=0.4)
        rib_bolts(B, path, s, top, rng, basic=True)
    if rollers:
        for s in [0.1 + 0.2 * k for k in range(10)]:
            p = path.point(s, 0, -0.04)
            B.cyl(p + Vector((-IN + 0.01, 0, 0)), p + Vector((IN - 0.01, 0, 0)), 0.04, 10, "steel", (0.4, 0.4, 0.4), 0.15, rust=0.3)
            for side in (-1, 1):
                B.cyl(p + Vector((side * (IN - 0.01), 0, 0)), p + Vector((side * (IN + 0.001), 0, 0)), 0.018, 6, "metal", C_DARK)

def channel_adv(B, P, path, height, closed=False, drive=True, roof="panel"):
    L = path.L
    top = height
    B.sweep(path, 0, L, -IN, IN, -0.05, 0.0, "metal", (0.1, 0.1, 0.11), 0.1)                            # dark motor deck
    B.sweep(path, 0, L, -IN - WT, IN + WT, -0.14, -0.05, "metal", C_FRAME, 0.12)
    if closed and roof == "glass":
        # glass lid between panel edges, so the powered run stays visible and sealed
        for side in (-1, 1):
            B.sweep(path, 0, L, *sx(side, IN - 0.25, IN + WT), top, top + WT, "panel", C_FACILITY, 0.1)
        B.sweep(path, 0, L, -IN + 0.25, IN - 0.25, top + 0.02, top + 0.05, "glass", (0.55, 0.9, 1.0), 0.02)
    elif closed:
        B.sweep(path, 0, L, -IN - WT, IN + WT, top, top + WT, "panel", C_FACILITY, 0.1)
    for side in (-1, 1):
        B.sweep(path, 0, L, *sx(side, IN, IN + WT), -0.14, 0.3, "panel", C_FACILITY, 0.1)               # lower wall
        B.sweep(path, 0, L, *sx(side, IN + 0.02, IN + 0.05), 0.3, top - 0.12, "glass", (0.55, 0.9, 1.0), 0.02)  # window
        B.sweep(path, 0, L, *sx(side, IN, IN + WT), top - 0.12, top, "panel", C_FACILITY, 0.1)            # upper wall
        B.sweep(path, 0, L, *sx(side, IN - 0.004, IN), 0.02, 0.1, "metal", C_DARK, 0.1)                    # kick strip
        if not closed:
            B.sweep(path, 0, L, *sx(side, IN - 0.02, IN + WT + 0.04), top, top + 0.035, "steel", C_STEEL, 0.1)
            B.sweep(path, 0.04, L - 0.04, *sx(side, IN - 0.028, IN - 0.02), top - 0.05, top - 0.03, "lamp", C_LAMP, 0.02)
        # powered: emitter rails along the deck edges
        P.sweep(path, 0.05, L - 0.05, *sx(side, IN - 0.12, IN - 0.09), 0.0, 0.006, "cyan", C_CYAN, 0.05)
    for s in ribs_at(L, 0.5):
        s0, s1 = max(0, s - 0.025), min(L, s + 0.025)
        for side in (-1, 1):
            B.sweep(path, s0, s1, *sx(side, IN + WT - 0.01, IN + WT + 0.07), -0.14, top + 0.04, "metal", C_DARK, 0.1)
        B.sweep(path, s0, s1, -IN - WT - 0.07, IN + WT + 0.07, -0.14, -0.1, "metal", C_DARK, 0.1)
        if closed:
            B.sweep(path, s0, s1, -IN - WT - 0.07, IN + WT + 0.07, top + WT, top + WT + 0.04, "metal", C_DARK, 0.1)
        rib_bolts(B, path, s, top, None, basic=False)
        # emitter bar across the deck at each rib
        P.sweep(path, max(0, s - 0.012), min(L, s + 0.012), -IN + 0.1, IN - 0.1, 0.0, 0.006, "cyan", C_CYAN, 0.05)

# ======================================================================================
# vertical bores and funnels
# ======================================================================================
def slab(B, quad, t, mat, c, var=0.2, rust=0.0, outward_from=None):
    """Plate of thickness t on a planar quad, grown along the quad's normal (flipped to point away from
    outward_from when given)."""
    q = [Vector(p) for p in quad]
    n = (q[1] - q[0]).cross(q[3] - q[0]).normalized()
    if outward_from is not None and n.dot((q[0] + q[2]) / 2 - Vector(outward_from)) < 0:
        n = -n
    pts = q + [p + n * t for p in q]
    return solid(B, pts, [[0, 1, 2, 3], [7, 6, 5, 4], [0, 4, 5, 1], [1, 5, 6, 2], [2, 6, 7, 3], [3, 7, 4, 0]], mat, c, var, rust)

def bore(B, P, tier, rng, z0, z1, cx=0.0, cy=0.0, collars=True):
    """Vertical square bore (interior +-IN) from z0 to z1, walls, corner posts, cladding."""
    h = z1 - z0; zc = (z0 + z1) / 2
    for side in (-1, 1):
        B.box(Vector((cx + side * (IN + WT / 2), cy, zc)), (WT, 2 * IN + 2 * WT, h), I3,
              "steel" if tier == "basic" else "panel", (0.3, 0.3, 0.3) if tier == "basic" else C_FACILITY, 0.2,
              rust=0.5 if tier == "basic" else 0.0, bevel=0)                  # edges hide under the corner posts
        B.box(Vector((cx, cy + side * (IN + WT / 2), zc)), (2 * IN, WT, h), I3,
              "steel" if tier == "basic" else "panel", (0.3, 0.3, 0.3) if tier == "basic" else C_FACILITY, 0.2,
              rust=0.5 if tier == "basic" else 0.0, bevel=0)
    for (px, py) in ((-1, -1), (1, -1), (1, 1), (-1, 1)):
        B.box(Vector((cx + px * (IN + WT + 0.03), cy + py * (IN + WT + 0.03), zc)), (0.1, 0.1, h), I3, "metal", C_FRAME, 0.2, rust=0.3)
    faces = [(Vector((1, 0, 0)), Vector((0, 1, 0))), (Vector((-1, 0, 0)), Vector((0, 1, 0))),
             (Vector((0, 1, 0)), Vector((1, 0, 0))), (Vector((0, -1, 0)), Vector((1, 0, 0)))]
    rows = max(1, int(round(h / 1.0)))
    for n, u in faces:
        base = Vector((cx, cy, 0)) + n * (IN + WT)
        for r in range(rows):
            za, zb = z0 + h * r / rows + 0.03, z0 + h * (r + 1) / rows - 0.03
            c = base + Vector((0, 0, (za + zb) / 2))
            if tier == "basic":
                k = rng.random()
                if k < 0.18:                                           # grille instead of a panel: see things fall
                    for j in range(7):
                        o = -0.66 + j * 0.22
                        B.box(c + u * o + n * 0.02, (0.03, zb - za, 0.03), facing_basis(n), "steel", C_STEEL, 0.15, rust=0.4)
                    continue
                col = C_WHITE if k < 0.78 else C_DARK
                B.box(c + n * 0.012, (1.5, zb - za, 0.024), facing_basis(n), "panel", col, 0.3)
                for du, dz in ((-0.69, 1), (0.69, 1), (-0.69, -1), (0.69, -1)):   # corner rivets
                    p = c + n * 0.024 + u * du + Vector((0, 0, dz * ((zb - za) / 2 - 0.05)))
                    stud(B, p, n, 0.011, 0.007, rust=0.4)
                    if dz > 0 and rng.random() < 0.35:
                        streak(B, p, n, 0.18)
            else:
                B.box(c + n * 0.012 + Vector((0, 0, -(zb - za) * 0.25)), (1.5, (zb - za) * 0.5, 0.024), facing_basis(n), "panel", C_FACILITY, 0.1)
                for du in (-0.69, 0.69):                                         # panel fixings
                    stud(B, c + n * 0.024 + u * du + Vector((0, 0, -(zb - za) * 0.5 + 0.06)), n, 0.009, 0.005, "metal", C_DARK)
                B.box(c + n * 0.03 + Vector((0, 0, (zb - za) * 0.25)), (1.4, (zb - za) * 0.45, 0.01), facing_basis(n), "glass", (0.55, 0.9, 1.0), 0.02)
    if collars:
        for z in (z0 + 0.04, z1 - 0.04):
            for side in (-1, 1):
                B.box(Vector((cx + side * (IN + WT + 0.04), cy, z)), (0.08, 2 * IN + 2 * WT + 0.16, 0.08), I3, "metal", C_DARK, 0.2, rust=0.3)
                B.box(Vector((cx, cy + side * (IN + WT + 0.04), z)), (2 * IN + 2 * WT, 0.08, 0.08), I3, "metal", C_DARK, 0.2, rust=0.3)
    if tier == "adv":
        k = max(1, int(round(h / 0.7)))
        for j in range(k):
            z = z0 + h * (j + 0.5) / k
            for side in (-1, 1):
                P.box(Vector((cx + side * (IN - 0.004), cy, z)), (0.008, 2 * IN - 0.1, 0.03), I3, "cyan", C_CYAN, 0.05)
                P.box(Vector((cx, cy + side * (IN - 0.004), z)), (2 * IN - 0.1, 0.008, 0.03), I3, "cyan", C_CYAN, 0.05)

def funnel(B, P, tier, rng, top, bot, z_top, z_bot, legs=True):
    """Four planar funnel walls from rectangle top=(x0,x1,y0,y1) at z_top down to bot at z_bot, plus rim,
    cladding and legs. Walls are grown outward from the funnel's axis."""
    (tx0, tx1, ty0, ty1), (bx0, bx1, by0, by1) = top, bot
    ctr = Vector(((tx0 + tx1 + bx0 + bx1) / 4, (ty0 + ty1 + by0 + by1) / 4, (z_top + z_bot) / 2))
    walls = [
        [(tx0, ty0, z_top), (tx0, ty1, z_top), (bx0, by1, z_bot), (bx0, by0, z_bot)],   # -X
        [(tx1, ty1, z_top), (tx1, ty0, z_top), (bx1, by0, z_bot), (bx1, by1, z_bot)],   # +X
        [(tx1, ty0, z_top), (tx0, ty0, z_top), (bx0, by0, z_bot), (bx1, by0, z_bot)],   # -Y
        [(tx0, ty1, z_top), (tx1, ty1, z_top), (bx1, by1, z_bot), (bx0, by1, z_bot)],   # +Y
    ]
    wall_mat, wall_col, wall_rust = (("steel", (0.3, 0.3, 0.3), 0.55) if tier == "basic" else ("panel", C_FACILITY, 0.0))
    for q in walls:
        slab(B, q, WT, wall_mat, wall_col, 0.2, wall_rust, outward_from=ctr)
        # cladding / window panels on the outside: an inset copy of the wall quad, raised off it
        qv = [Vector(p) for p in q]
        n = (qv[1] - qv[0]).cross(qv[3] - qv[0]).normalized()
        if n.dot((qv[0] + qv[2]) / 2 - ctr) < 0: n = -n
        mid = sum(qv, Vector()) / 4
        inset = [mid + (p - mid) * 0.86 + n * (WT + 0.004) for p in qv]
        if tier == "basic":
            k = rng.random()
            col = C_WHITE if k < 0.7 else C_DARK if k < 0.85 else C_STEEL
            slab(B, inset, 0.02, "panel" if col != C_STEEL else "steel", col, 0.3, 0.6 if col == C_STEEL else 0.0, outward_from=ctr)
        else:
            slab(B, inset, 0.02, "panel", C_FACILITY, 0.1, outward_from=ctr)
            wmid = mid + ((qv[0] + qv[1]) / 2 - mid) * 0.35
            win = [wmid + (p - mid) * 0.5 + n * (WT + 0.03) for p in qv]
            slab(B, win, 0.01, "glass", (0.55, 0.9, 1.0), 0.02, outward_from=ctr)
    # rim with a hazard band
    for (c, s) in ((Vector(((tx0 + tx1) / 2, ty0 - 0.02, z_top + 0.03)), (tx1 - tx0 + 0.12, 0.12, 0.06)),
                   (Vector(((tx0 + tx1) / 2, ty1 + 0.02, z_top + 0.03)), (tx1 - tx0 + 0.12, 0.12, 0.06)),
                   (Vector((tx0 - 0.02, (ty0 + ty1) / 2, z_top + 0.03)), (0.12, ty1 - ty0 + 0.12, 0.06)),
                   (Vector((tx1 + 0.02, (ty0 + ty1) / 2, z_top + 0.03)), (0.12, ty1 - ty0 + 0.12, 0.06))):
        B.box(c, s, I3, "metal", C_DARK if tier == "basic" else C_STEEL, 0.2, rust=0.4 if tier == "basic" else 0.0)
        ln = max(s[0], s[1]); ax = Vector((1, 0, 0)) if s[0] > s[1] else Vector((0, 1, 0))
        k = max(2, int(round(ln / 0.6)))
        for j in range(k):                                                     # rim bolts along the top
            p = c + ax * (-ln / 2 + 0.1 + (ln - 0.2) * (j + 0.5) / k) + Vector((0, 0, 0.03))
            if tier == "basic":
                stud(B, p, ZV, 0.014, 0.01, rust=0.5)
            else:
                stud(B, p, ZV, 0.011, 0.006, "metal", (0.3, 0.3, 0.32))
    for (org, u, n, w) in ((Vector((tx0, ty0 - 0.081, z_top - 0.12)), (1, 0, 0), (0, -1, 0), tx1 - tx0),
                           (Vector((tx1, ty1 + 0.081, z_top - 0.12)), (-1, 0, 0), (0, 1, 0), tx1 - tx0)):
        hazard(B, org, u, (0, 0, 1), w, 0.1, n, pitch=0.12)
    if tier == "adv":
        for (a, b) in (((tx0, ty0), (tx1, ty0)), ((tx1, ty0), (tx1, ty1)), ((tx1, ty1), (tx0, ty1)), ((tx0, ty1), (tx0, ty0))):
            P.cyl(Vector((a[0], a[1], z_top - 0.05)), Vector((b[0], b[1], z_top - 0.05)), 0.018, 6, "cyan", C_CYAN, 0.05)
    if legs:
        corners = [(tx0 + 0.02, ty0 + 0.02), (tx1 - 0.02, ty0 + 0.02), (tx1 - 0.02, ty1 - 0.02), (tx0 + 0.02, ty1 - 0.02)]
        for (x, y) in corners:
            B.box(Vector((x, y, (FLOOR + z_top) / 2)), (0.1, 0.1, z_top - FLOOR), I3, "metal", C_FRAME, 0.2, rust=0.3 if tier == "basic" else 0.0)
            B.box(Vector((x, y, FLOOR + 0.01)), (0.2, 0.2, 0.02), I3, "steel", C_STEEL, 0.2, rust=0.5 if tier == "basic" else 0.0)
        for i in range(4):
            (x0, y0), (x1, y1) = corners[i], corners[(i + 1) % 4]
            zb = FLOOR + 0.45
            if tier == "basic":
                B.cyl(Vector((x0, y0, zb)), Vector((x1, y1, zb)), 0.025, 8, "steel", C_STEEL, rust=0.55)
                B.cyl(Vector((x0, y0, zb)), Vector((x1, y1, min(z_top - 0.3, zb + 1.4))), 0.02, 8, "steel", C_STEEL, rust=0.55)
            else:
                B.box((Vector((x0, y0, zb)) + Vector((x1, y1, zb))) / 2, (abs(x1 - x0) + 0.05, abs(y1 - y0) + 0.05, 0.05), I3, "metal", C_DARK, 0.1)

# ======================================================================================
def new(name):
    return clear_collection(name), Builder(), Builder()

def done(coll, B, P, tier):
    B.to_object("Frame", coll)
    if tier == "adv":
        P.to_object("Power", coll)
    else:
        P.bm.free()
    return coll

def prefix(tier):
    return "Chute_" if tier == "basic" else "ChuteAdv_"

def build_h_straight(tier):
    rng = random.Random(201 if tier == "basic" else 301); random.seed(rng.random())
    coll, B, P = new(prefix(tier) + "HStraight")
    if tier == "basic":
        channel_basic(B, PATHS["straight"], rng, H_WALL, closed=True, rollers=True)
    else:
        channel_adv(B, P, PATHS["straight"], H_WALL, closed=True, roof="glass")
    return done(coll, B, P, tier)

def build_h_turn(tier):
    rng = random.Random(203 if tier == "basic" else 303); random.seed(rng.random())
    coll, B, P = new(prefix(tier) + "HTurnRight")
    if tier == "basic":
        channel_basic(B, PATHS["turn"], rng, H_WALL, closed=True)
    else:
        channel_adv(B, P, PATHS["turn"], H_WALL, closed=True, roof="glass")
    return done(coll, B, P, tier)

def build_v_straight(tier):
    rng = random.Random(205 if tier == "basic" else 305); random.seed(rng.random())
    coll, B, P = new(prefix(tier) + "VStraight")
    bore(B, P, tier, rng, -1.0, 1.0)
    return done(coll, B, P, tier)

def build_v_turn(tier):
    rng = random.Random(207 if tier == "basic" else 307); random.seed(rng.random())
    coll, B, P = new(prefix(tier) + "VTurn")
    path = vturn_path()
    if tier == "basic":
        channel_basic(B, path, rng, 2 * IN, closed=True)
    else:
        channel_adv(B, P, path, 2 * IN, closed=True)
    return done(coll, B, P, tier)

def build_hopper_up(tier):
    rng = random.Random(209 if tier == "basic" else 309); random.seed(rng.random())
    coll, B, P = new(prefix(tier) + "HopperUp")
    funnel(B, P, tier, rng, (-0.89, 0.89, -0.89, 0.89), (-IN, IN, -IN, IN), 0.92, -0.2, legs=True)
    bore(B, P, tier, rng, -1.0, -0.2, collars=False)
    return done(coll, B, P, tier)

def build_dropper_down(tier):
    rng = random.Random(211 if tier == "basic" else 311); random.seed(rng.random())
    coll, B, P = new(prefix(tier) + "DropperDown")
    bore(B, P, tier, rng, -1.0, 1.0)
    # hinge rails for the leaves, and a door-state lamp
    for side in (-1, 1):
        B.cyl(Vector((side * (IN - 0.03), -IN + 0.02, DOOR_Z)), Vector((side * (IN - 0.03), IN - 0.02, DOOR_Z)), 0.03, 8, "steel", C_STEEL, rust=0.4 if tier == "basic" else 0.0)
    # lever box on the +X face
    lb = Vector((IN + WT + 0.1, 0.0, 0.3))
    B.box(lb, (0.08, 0.3, 0.36), I3, "metal", C_BLUE if tier == "basic" else C_DARK, 0.2, rust=0.2 if tier == "basic" else 0.0)
    B.box(lb + Vector((0.042, 0, 0.12)), (0.004, 0.2, 0.05), I3, "panel", C_WHITE, 0.2)
    B.box(lb + Vector((0.045, -0.05, 0.12)), (0.004, 0.03, 0.02), I3, "glow", C_AMBER, 0.05)
    B.box(lb + Vector((0.045, 0.05, 0.12)), (0.004, 0.03, 0.02), I3, "glow" if tier == "basic" else "cyan",
          C_AMBER if tier == "basic" else C_CYAN, 0.05)                         # salvage tier: one lamp material
    hazard(B, lb + Vector((0.041, -0.15, -0.17)), (0, 1, 0), (0, 0, 1), 0.3, 0.06, (1, 0, 0), pitch=0.06)
    B.pipe([lb + Vector((0, 0, -0.18)), Vector((IN + WT + 0.06, 0.0, DOOR_Z + 0.05)), Vector((IN + WT + 0.02, 0.4, DOOR_Z))], 0.012, 5, "metal", C_BLACK)
    for dy in (-0.12, 0.12):                                                     # box screwed to the bore
        for dz in (-0.14, 0.14):
            stud(B, lb + Vector((0.04, dy, dz)), (1, 0, 0), 0.008, 0.005, rust=0.3 if tier == "basic" else 0.0)
    done(coll, B, P, tier)
    Lv = Builder()
    Lv.cyl(Vector((0, -0.03, 0)), Vector((0, 0.03, 0)), 0.03, 10, "metal", C_DARK)
    Lv.cyl(Vector((0.0, 0, 0)), Vector((0.0, 0, 0.24)), 0.013, 6, "steel", C_STEEL)
    Lv.box(Vector((0.0, 0, 0.26)), (0.05, 0.06, 0.07), I3, "rubber", (0.8, 0.12, 0.08), 0.1)
    lever = node(Lv, "Lever", coll, lb + Vector((0.045, 0, -0.05)))
    lever.rotation_euler = (0, 0, 0)
    # trapdoor leaves: hinged along Y at x = +-(IN - 0.03), closed flat, meeting in the middle
    for name, side in (("DoorL", -1), ("DoorR", 1)):
        D = Builder()
        w = IN - 0.03
        D.box(Vector((-side * w / 2, 0, -0.02)), (w - 0.01, 2 * IN - 0.04, 0.04), I3,
              "steel" if tier == "basic" else "panel", (0.32, 0.32, 0.32) if tier == "basic" else C_FACILITY, 0.2,
              rust=0.5 if tier == "basic" else 0.0)
        for k in range(3):                                                           # stiffening ribs underneath
            D.box(Vector((-side * w / 2, -0.5 + k * 0.5, -0.06)), (w - 0.05, 0.04, 0.05), I3, "metal", C_DARK, 0.2)
        hazard(D, Vector((-side * (w - 0.005), -IN + 0.03, 0.001)), (0, 1, 0), (side, 0, 0), 2 * IN - 0.06, 0.08, (0, 0, 1), pitch=0.1)
        node(D, name, coll, Vector((side * (IN - 0.03), 0, DOOR_Z)))
    return coll

def build_hopper_2x2(tier):
    rng = random.Random(213 if tier == "basic" else 313); random.seed(rng.random())
    coll, B, P = new(prefix(tier) + "Hopper2x2")
    funnel(B, P, tier, rng, (-0.89, 2.89, -0.89, 2.89), (-IN, IN, -IN, IN), 2.92, 0.2)
    bore(B, P, tier, rng, -1.0, 0.2)
    return done(coll, B, P, tier)

def build_hopper_3x3(tier):
    rng = random.Random(215 if tier == "basic" else 315); random.seed(rng.random())
    coll, B, P = new(prefix(tier) + "Hopper3x3")
    funnel(B, P, tier, rng, (-2.89, 2.89, -2.89, 2.89), (-IN, IN, -IN, IN), 2.92, 0.4)
    bore(B, P, tier, rng, -1.0, 0.4)
    return done(coll, B, P, tier)

def mirror_turn(tier):
    return mirror(prefix(tier) + "HTurnRight", prefix(tier) + "HTurnLeft")

PIECES = []
ICONS = []
for _tier, _p in (("basic", "chute_"), ("adv", "chute_adv_")):
    PIECES += [
        (lambda t=_tier: build_h_straight(t), _p + "h_straight.glb"),
        (lambda t=_tier: build_h_turn(t), _p + "h_turn_right.glb"),
        (lambda t=_tier: mirror_turn(t), _p + "h_turn_left.glb"),
        (lambda t=_tier: build_v_straight(t), _p + "v_straight.glb"),
        (lambda t=_tier: build_v_turn(t), _p + "v_turn.glb"),
        (lambda t=_tier: build_hopper_up(t), _p + "hopper_up.glb"),
        (lambda t=_tier: build_dropper_down(t), _p + "dropper_down.glb"),
        (lambda t=_tier: build_hopper_2x2(t), _p + "hopper_2x2.glb"),
        (lambda t=_tier: build_hopper_3x3(t), _p + "hopper_3x3.glb"),
    ]
    _c, _b = prefix(_tier), "blueprints/blueprint_" + _p
    ICONS += [
        (_c + "HStraight", _b + "h_straight.png", "blueprint", (1.3, 1.0, 1.05)),
        (_c + "HTurnRight", _b + "h_turn.png", "blueprint", (-0.35, -0.9, 1.6)),
        (_c + "VStraight", _b + "v_straight.png", "blueprint", (1.2, 1.3, 0.8)),
        (_c + "VTurn", _b + "v_turn.png", "blueprint", (1.4, 0.8, 0.8)),
        (_c + "HopperUp", _b + "hopper_up.png", "blueprint", (1.2, 1.3, 1.0)),
        (_c + "DropperDown", _b + "dropper_down.png", "blueprint", (1.4, 1.1, 0.7)),
        (_c + "Hopper2x2", _b + "hopper_2x2.png", "blueprint", (1.2, 1.3, 1.0)),
        (_c + "Hopper3x3", _b + "hopper_3x3.png", "blueprint", (1.2, 1.3, 1.0)),
    ]

def build_all(do_export=True):
    return build_family(PIECES, OUT_DIR, do_export)

if __name__ == "__main__":
    build_all(do_export=False)
