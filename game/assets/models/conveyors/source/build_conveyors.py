"""
Builds the salvage conveyor set (straight, turn left/right, slope) and exports one .glb per piece.

Run inside Blender (Text Editor > Run Script, or exec(open(path).read())). Re-running rebuilds everything
from scratch in the "Conveyors" scene collections and re-exports to ../ (game/assets/models/conveyors).

Frame conventions (Blender, Z up; glTF export turns these into Godot's Y up):
  - The origin is the centre of the anchor 2 m build cell; its floor is z = -1.
  - Items flow toward +Y here, which is Godot's -Z (a structure's front).
  - The belt's top run is BELT_TOP (0.15 m above the cell floor); its return run is exposed underneath,
    running the opposite way, BELT_H lower. Nothing crosses under the belt, so the underside stays clear.
  - The underside doubles as a working conveyor when gravity flips: the return run is recessed up into the
    frame between two guide lips (LIP_X / LIP_O), finished as worn wear strips so it reads as ordinary
    construction rather than a second conveyor. Everything stays inside the cell (nothing below the floor).
  - Every piece meets the cell faces with the same rail/stringer/cable cross-section so pieces tile.

Belt UVs: u runs across the belt (0..1), v runs around the loop in metres of belt, scaled per piece so each
run is a whole number of RIB_PITCH; a shader that scrolls v by TIME * speed therefore moves the top run
forward and the return run backward, and the rib pattern lines up across cell seams.
"""
import bpy, bmesh, math, random, os
from mathutils import Vector, Matrix, noise

HERE = os.path.dirname(bpy.data.filepath) if bpy.data.filepath else None
OUT_DIR = r"C:\Users\steph\OneDrive\Documents\godot\projects\gmp-framework\game\assets\models\conveyors"

# ---------- layout constants (metres) ----------
CELL = 2.0
FLOOR = -1.0
BELT_TOP = -0.85          # top of the carrying run (Structure.BeltTopHeight above the floor)
BELT_H = 0.085            # top run to return run (recessed LIP_DEPTH up into the frame)
BELT_W = 0.84             # belt half-width
BELT_RC = 0.006           # corner radius where the belt wraps at a cell face (small: it's the visible seam)
STR_X = (0.84, 0.95)      # side stringer, across the belt (either side)
STR_O = (-0.15, 0.012)    # stringer, relative to the belt top (bottom is flush with the cell floor)
LIP_X = (0.812, 0.842)    # underside guide lips, just inside the stringers, covering the return run's edges
LIP_O = (-0.15, -0.095)   # ...from the cell floor up to just under the return run
GUARD_X = (0.868, 0.902)  # salvaged panels standing on the stringer
GUARD_TOP = 0.25          # nominal guard height above the belt
CABLE_X = -0.975          # cable run along the left stringer
CABLE_O = -0.06
RIB_PITCH = 0.25          # belt pattern repeat; every run length is a multiple of this

# slope: 4 m run, 2 m rise, tangent-arc-tangent profile so both ends meet flat conveyors flush
SLOPE_R = 1.0
SLOPE_TH = math.radians(30.0)
SLOPE_LS = (4.0 - 2 * SLOPE_R * math.sin(SLOPE_TH)) / math.cos(SLOPE_TH)   # straight part, 3.464

# ---------- paths: frame(s) -> (p, t, side, up) ----------
class Path:
    def __init__(self, length, fn, stations):
        self.L, self.fn, self.stations = length, fn, stations
    def frame(self, s):
        return self.fn(min(max(s, 0.0), self.L))
    def point(self, s, x, o, a=0.0):
        p, t, sd, up = self.frame(s)
        return p + t * a + sd * x + up * o
    def samples(self, s0, s1, per_m=None):
        n = max(1, int(math.ceil(abs(s1 - s0) * (per_m or self.stations))))
        return [s0 + (s1 - s0) * i / n for i in range(n + 1)]

def straight_fn(s):
    return (Vector((0, -1 + s, BELT_TOP)), Vector((0, 1, 0)), Vector((1, 0, 0)), Vector((0, 0, 1)))

def turn_right_fn(s):
    th = s  # centreline radius 1 about the pivot (1, -1): enters on y = -1, leaves on x = +1
    p = Vector((1 - math.cos(th), -1 + math.sin(th), BELT_TOP))
    t = Vector((math.sin(th), math.cos(th), 0))
    sd = Vector((math.cos(th), -math.sin(th), 0))
    return (p, t, sd, Vector((0, 0, 1)))

def slope_fn(s):
    R, th, Ls = SLOPE_R, SLOPE_TH, SLOPE_LS
    a1 = R * th
    if s <= a1:                                   # concave bend at the bottom
        ph = s / R
        y, z = -1 + R * math.sin(ph), BELT_TOP + R * (1 - math.cos(ph))
    elif s <= a1 + Ls:                            # straight incline
        ph = th
        y0, z0 = -1 + R * math.sin(th), BELT_TOP + R * (1 - math.cos(th))
        d = s - a1
        y, z = y0 + d * math.cos(th), z0 + d * math.sin(th)
    else:                                         # convex bend at the top, mirror of the bottom one
        u = (a1 + Ls + a1) - s
        ph = u / R
        y, z = 3 - R * math.sin(ph), BELT_TOP + 2 - R * (1 - math.cos(ph))
    t = Vector((0, math.cos(ph), math.sin(ph)))
    return (Vector((0, y, z)), t, Vector((1, 0, 0)), Vector((0, -math.sin(ph), math.cos(ph))))

PATHS = {
    "straight": Path(2.0, straight_fn, 1),
    "turn": Path(math.pi / 2, turn_right_fn, 16),
    "slope": Path(2 * SLOPE_R * SLOPE_TH + SLOPE_LS, slope_fn, 10),
}

# ---------- materials (albedo lives in the vertex colours; base colour stays white) ----------
def vc_mat(name, rough, metal=0.0, emit=None, emit_str=0.0):
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    m.use_nodes = True; nt = m.node_tree; N, L = nt.nodes, nt.links; N.clear()
    o = N.new("ShaderNodeOutputMaterial"); o.location = (400, 0)
    b = N.new("ShaderNodeBsdfPrincipled"); b.location = (100, 0); L.new(b.outputs[0], o.inputs[0])
    b.inputs["Roughness"].default_value = rough; b.inputs["Metallic"].default_value = metal
    ca = N.new("ShaderNodeVertexColor"); ca.layer_name = "Grime"; ca.location = (-300, 0)
    L.new(ca.outputs["Color"], b.inputs["Base Color"])
    if emit:
        b.inputs["Emission Color"].default_value = (*emit, 1); b.inputs["Emission Strength"].default_value = emit_str
    return m

def belt_mat():
    """Preview-only rubber belt (ribs from UV v). Godot swaps it for the scrolling belt shader by name."""
    m = bpy.data.materials.get("M_ConvBelt") or bpy.data.materials.new("M_ConvBelt")
    m.use_nodes = True; nt = m.node_tree; N, L = nt.nodes, nt.links; N.clear()
    o = N.new("ShaderNodeOutputMaterial"); o.location = (600, 0)
    b = N.new("ShaderNodeBsdfPrincipled"); b.location = (300, 0); L.new(b.outputs[0], o.inputs[0])
    b.inputs["Roughness"].default_value = 0.85
    uv = N.new("ShaderNodeUVMap"); uv.location = (-700, 0)
    sep = N.new("ShaderNodeSeparateXYZ"); sep.location = (-500, 0); L.new(uv.outputs[0], sep.inputs[0])
    mul = N.new("ShaderNodeMath"); mul.operation = 'MULTIPLY'; mul.inputs[1].default_value = 1.0 / RIB_PITCH
    mul.location = (-300, 0); L.new(sep.outputs[1], mul.inputs[0])
    fr = N.new("ShaderNodeMath"); fr.operation = 'FRACT'; fr.location = (-150, 0); L.new(mul.outputs[0], fr.inputs[0])
    ramp = N.new("ShaderNodeValToRGB"); ramp.location = (0, 0); L.new(fr.outputs[0], ramp.inputs[0])
    ramp.color_ramp.elements[0].position = 0.0; ramp.color_ramp.elements[0].color = (0.05, 0.05, 0.05, 1)
    ramp.color_ramp.elements[1].position = 0.12; ramp.color_ramp.elements[1].color = (0.025, 0.025, 0.027, 1)
    L.new(ramp.outputs[0], b.inputs["Base Color"])
    return m

MATS = {}
def mats():
    MATS.update(
        panel=vc_mat("M_ConvPanel", 0.42),
        metal=vc_mat("M_ConvMetal", 0.45, 0.8),
        steel=vc_mat("M_ConvRawSteel", 0.6, 0.7),
        rubber=vc_mat("M_ConvRubber", 0.85),
        wood=vc_mat("M_ConvWood", 0.85),
        tape=vc_mat("M_ConvTape", 0.7),
        glow=vc_mat("M_ConvGlow", 0.3, 0.0, (1.0, 0.55, 0.12), 4.0),
        belt=belt_mat(),
    )
MAT_ORDER = ["panel", "metal", "steel", "rubber", "wood", "tape", "glow"]
MI = {k: i for i, k in enumerate(MAT_ORDER)}

# ---------- palette ----------
C_WHITE = (0.74, 0.74, 0.71)
C_DARK = (0.075, 0.08, 0.085)
C_FRAME = (0.13, 0.135, 0.14)
C_STEEL = (0.36, 0.35, 0.33)
C_WEAR = (0.5, 0.5, 0.48)
C_RUST = (0.32, 0.15, 0.06)
C_YELLOW = (0.85, 0.62, 0.06)
C_BLACK = (0.02, 0.02, 0.02)
C_TAPE = (0.42, 0.42, 0.4)
C_WOOD = (0.42, 0.28, 0.15)
C_AMBER = (1.0, 0.6, 0.15)

def grimy(c, pos, var=0.18, rust=0.0, seed=0.0):
    n = noise.noise(pos * 3.1 + Vector((seed, seed * 0.7, 0))) * 0.5 + 0.5
    n2 = noise.noise(pos * 11.0 + Vector((0, seed, 4.2))) * 0.5 + 0.5
    k = 1.0 - var * n - 0.08 * n2
    low = max(0.0, 1.0 - (pos.z - FLOOR) / 0.6) * 0.12      # dirtier right at floor level
    k -= low
    col = [c[i] * k for i in range(3)]
    if rust > 0:
        r = max(0.0, (noise.noise(pos * 5.3 + Vector((seed, 2.0, 1.0))) + 0.15)) * rust
        r = min(1.0, r)
        col = [col[i] * (1 - r) + C_RUST[i] * r for i in range(3)]
    return col

# ---------- bmesh builder ----------
class Builder:
    def __init__(self):
        self.bm = bmesh.new()
        self.col = self.bm.loops.layers.color.new("Grime")
    def paint(self, faces, c, var=0.18, rust=0.0, seed=None):
        seed = random.random() * 50 if seed is None else seed
        for f in faces:
            ctr = f.calc_center_median()
            cc = grimy(c, ctr, var, rust, seed)
            for l in f.loops:
                l[self.col] = (cc[0], cc[1], cc[2], 1.0)
    def quad(self, vs, mat):
        f = self.bm.faces.new(vs); f.material_index = MI[mat]; return f
    def box(self, center, size, basis=Matrix.Identity(3), mat="metal", c=C_FRAME, var=0.18, rust=0.0):
        hx, hy, hz = size[0] / 2, size[1] / 2, size[2] / 2
        pts = [center + basis @ Vector((sx * hx, sy * hy, sz * hz)) for sx in (-1, 1) for sy in (-1, 1) for sz in (-1, 1)]
        v = [self.bm.verts.new(p) for p in pts]
        idx = [(0, 1, 3, 2), (4, 6, 7, 5), (0, 4, 5, 1), (2, 3, 7, 6), (0, 2, 6, 4), (1, 5, 7, 3)]
        fs = [self.quad([v[i] for i in q], mat) for q in idx]
        bmesh.ops.recalc_face_normals(self.bm, faces=fs)
        self.paint(fs, c, var, rust); return fs
    def tube_rings(self, rings, mat, c, var=0.15, rust=0.0, cap=True, smooth=True):
        vs = [[self.bm.verts.new(p) for p in r] for r in rings]
        fs = []
        n = len(rings[0])
        for i in range(len(vs) - 1):
            for k in range(n):
                fs.append(self.quad([vs[i][k], vs[i][(k + 1) % n], vs[i + 1][(k + 1) % n], vs[i + 1][k]], mat))
        if cap:
            fs.append(self.quad(vs[0][::-1], mat)); fs.append(self.quad(vs[-1], mat))
        bmesh.ops.recalc_face_normals(self.bm, faces=fs)
        for f in fs:
            f.smooth = smooth and len(f.verts) == 4
        self.paint(fs, c, var, rust); return fs
    def cyl(self, p0, p1, r, seg=8, mat="steel", c=C_STEEL, var=0.15, rust=0.0, cap=True):
        ax = (p1 - p0).normalized()
        ref = Vector((0, 0, 1)) if abs(ax.z) < 0.9 else Vector((1, 0, 0))
        u = ax.cross(ref).normalized(); w = ax.cross(u)
        ring = lambda p: [p + (u * math.cos(k / seg * math.tau) + w * math.sin(k / seg * math.tau)) * r for k in range(seg)]
        return self.tube_rings([ring(p0), ring(p1)], mat, c, var, rust, cap)
    def pipe(self, pts, r, seg=6, mat="rubber", c=C_BLACK, var=0.1):
        rings = []
        for i, p in enumerate(pts):
            t = (pts[min(i + 1, len(pts) - 1)] - pts[max(i - 1, 0)]).normalized()
            ref = Vector((0, 0, 1)) if abs(t.z) < 0.9 else Vector((1, 0, 0))
            u = t.cross(ref).normalized(); w = t.cross(u)
            rings.append([p + (u * math.cos(k / seg * math.tau) + w * math.sin(k / seg * math.tau)) * r for k in range(seg)])
        return self.tube_rings(rings, mat, c, var, 0.0, cap=True)
    def sweep(self, path, s0, s1, x0, x1, o0, o1, mat, c, var=0.18, rust=0.0, per_m=None, lean=0.0):
        """Closed box-section tube following the path between s0 and s1 (x across, o along the path's up).
        lean shifts the top edge across, for panels sagging off their brackets."""
        rings = []
        for s in path.samples(s0, s1, per_m):
            rings.append([path.point(s, x0, o0), path.point(s, x1, o0), path.point(s, x1 + lean, o1), path.point(s, x0 + lean, o1)])
        return self.tube_rings(rings, mat, c, var, rust, cap=True, smooth=False)
    def to_object(self, name, coll, material_keys=MAT_ORDER):
        me = bpy.data.meshes.new(name)
        self.bm.normal_update()
        self.bm.to_mesh(me); self.bm.free()
        set_active_colors(me)
        for k in material_keys:
            me.materials.append(MATS[k])
        ob = bpy.data.objects.new(name, me); coll.objects.link(ob)
        return ob

def set_active_colors(me):
    """The glTF exporter only writes COLOR_0 from the active/render colour attribute."""
    if "Grime" in me.color_attributes:
        me.color_attributes.active_color_name = "Grime"
        me.color_attributes.render_color_index = me.color_attributes.find("Grime")

def frame_basis(path, s):
    p, t, sd, up = path.frame(s)
    return Matrix((sd, t, up)).transposed()   # local x = side, y = along, z = up

# ---------- belt loop ----------
def build_belt(path, name, coll, target_len):
    """Closed belt loop: top run, wrap at the front face, return run underneath, wrap at the back face."""
    bm = bmesh.new(); uvl = bm.loops.layers.uv.new("UVMap")
    L, rc, H = path.L, BELT_RC, BELT_H
    run = lambda s: target_len * (s - rc) / (L - 2 * rc)     # run length -> belt v
    cap_v = RIB_PITCH / 2                     # each wrap counts as half a pitch, so the loop closes on a pitch
    arc = [(rc * math.sin(a), rc * math.cos(a)) for a in [i / 4 * math.pi / 2 for i in range(5)]]
    # front wrap, as (along, up) offsets from the belt top at the front face (s = L)
    front = [(-rc + a, -rc + b) for a, b in arc] + [(-rc + b, -H + rc - a) for a, b in arc]
    flen = [0.0]
    for i in range(1, len(front)):
        flen.append(flen[-1] + (Vector(front[i]) - Vector(front[i - 1])).length)
    back = [(-a, b) for a, b in reversed(front)]
    loop = []                                 # (base s, along offset, up offset, v)
    for s in path.samples(rc, L - rc):
        loop.append((s, 0.0, 0.0, run(s)))
    for i in range(1, len(front)):
        loop.append((L, front[i][0], front[i][1], target_len + cap_v * flen[i] / flen[-1]))
    for s in reversed(path.samples(rc, L - rc)[:-1]):
        loop.append((s, 0.0, -H, target_len + cap_v + (target_len - run(s))))
    for i in range(1, len(back)):
        loop.append((0.0, back[i][0], back[i][1], 2 * target_len + cap_v + cap_v * flen[i] / flen[-1]))
    rows = [([bm.verts.new(path.point(s, x, b, a)) for x in (-BELT_W, BELT_W)], v) for s, a, b, v in loop]
    fs = []
    for i in range(len(rows) - 1):
        (va, v0), (vb, v1) = rows[i], rows[i + 1]
        f = bm.faces.new((va[0], va[1], vb[1], vb[0])); f.material_index = 0
        for l, uv in zip(f.loops, [(0, v0), (1, v0), (1, v1), (0, v1)]):
            l[uvl].uv = uv
        fs.append(f)
    bm.normal_update()
    _, _, _, up = path.frame(loop[0][0])
    if fs[0].normal.dot(up) < 0:
        bmesh.ops.reverse_faces(bm, faces=fs)
    me = bpy.data.meshes.new(name); bm.to_mesh(me); bm.free()
    me.materials.append(MATS["belt"])
    ob = bpy.data.objects.new(name, me); coll.objects.link(ob)
    return ob

# ---------- frame parts shared by every piece ----------
def panel_splits(length, lo, hi, rng):
    cuts = [0.0]
    while length - cuts[-1] > hi:
        cuts.append(cuts[-1] + rng.uniform(lo, hi))
    if length - cuts[-1] < lo * 0.6 and len(cuts) > 1:
        cuts[-1] = (cuts[-2] + length) / 2
    cuts.append(length)
    return cuts

def build_side(B, path, side, rng, panel_len=(0.5, 0.95), guards=True, brackets=True, hubs=True):
    """Stringer + salvaged guard panels + brackets/bolts + roller hubs on one side (side = +1 right, -1 left)."""
    L = path.L
    x0, x1 = sorted((side * STR_X[0], side * STR_X[1]))
    B.sweep(path, 0, L, x0, x1, STR_O[0], STR_O[1], "metal", C_FRAME, 0.2, rust=0.25)
    # underside guide lip: bright where loads have worn it, with a row of countersunk fasteners on the inner face
    lx0, lx1 = sorted((side * LIP_X[0], side * LIP_X[1]))
    B.sweep(path, 0, L, lx0, lx1, LIP_O[0], LIP_O[1], "steel", C_WEAR, 0.12, rust=0.05)
    n = int(round(L / 0.25))
    for i in range(n):
        s = (i + 0.5) * L / n
        p = path.point(s, side * LIP_X[0], (LIP_O[0] + LIP_O[1]) / 2); _, _, sd, _ = path.frame(s)
        B.cyl(p + sd * side * 0.002, p - sd * side * 0.003, 0.008, 6, "steel", (0.2, 0.2, 0.2), 0.1)
    # lip bolts along the outside of the stringer
    n = int(round(L / 0.25))
    for i in range(n):
        s = (i + 0.5) * L / n
        p = path.point(s, side * STR_X[1], -0.07); _, _, sd, _ = path.frame(s)
        B.cyl(p, p + sd * side * 0.012, 0.011, 6, "steel", C_STEEL, rust=0.5)
    if hubs:
        for s in (0.22, L / 2, L - 0.22):
            p = path.point(s, side * STR_X[1], -0.068); _, _, sd, _ = path.frame(s)
            B.cyl(p, p + sd * side * 0.02, 0.036, 10, "steel", (0.2, 0.2, 0.2), rust=0.3)
            B.cyl(p, p + sd * side * 0.034, 0.012, 6, "steel", C_STEEL, rust=0.2)
    if not guards:
        return
    gx0, gx1 = sorted((side * GUARD_X[0], side * GUARD_X[1]))
    inner_x = gx0 if side > 0 else gx1          # the panel face the belt sees
    cuts = panel_splits(L, panel_len[0], panel_len[1], rng)
    heights = []
    for i in range(len(cuts) - 1):
        internal = 0 < i < len(cuts) - 2
        a = cuts[i] + (0.012 if i > 0 else 0.0)
        b = cuts[i + 1] - (0.012 if i < len(cuts) - 2 else 0.0)
        r = rng.random()
        h = GUARD_TOP + rng.uniform(-0.035, 0.03)
        jx = rng.uniform(-0.006, 0.006)
        heights.append(h)
        if r < 0.1 and internal and brackets:
            # panel missing: two scaffold pipes clamped between the neighbouring brackets instead
            for o in (0.09, h - 0.04):
                B.pipe([path.point(s, side * 0.915, o) for s in path.samples(a - 0.03, b + 0.03, 8)], 0.018, 8, "steel", C_STEEL)
            continue
        lean = side * rng.uniform(0.025, 0.05) if (r > 0.9 and brackets) else 0.0
        if r < 0.62:
            B.sweep(path, a, b, gx0 + jx, gx1 + jx, STR_O[1], h, "panel", C_WHITE, 0.3, lean=lean)
            # facility panel seam line on the belt-facing side
            sx = inner_x + jx - side * 0.003 + lean * (h - 0.06) / h
            B.sweep(path, a + 0.01, b - 0.01, min(sx, sx - side * 0.004), max(sx, sx - side * 0.004), h - 0.075, h - 0.062,
                    "metal", (0.25, 0.25, 0.25), 0.2)
        elif r < 0.8:
            B.sweep(path, a, b, gx0 + jx, gx1 + jx, STR_O[1], h, "metal", C_DARK, 0.2, rust=0.1, lean=lean)
        else:
            # hazard-striped plate: slanted stripes painted as strips on the outer face
            B.sweep(path, a, b, gx0 + jx, gx1 + jx, STR_O[1], h, "panel", C_BLACK, 0.2)
            face_x = (gx1 if side > 0 else gx0) + jx + side * 0.002
            nstr = max(2, int((b - a) / 0.09))
            for k in range(nstr):
                s0 = a + (b - a) * k / nstr; s1 = a + (b - a) * (k + 0.5) / nstr
                sk = 0.06
                vs = [B.bm.verts.new(path.point(s, face_x, o)) for s, o in
                      ((s0, STR_O[1] + 0.02), (s1, STR_O[1] + 0.02), (min(b, s1 + sk), h - 0.02), (min(b, s0 + sk), h - 0.02))]
                f = B.quad(vs if side > 0 else vs[::-1], "panel")
                B.paint([f], C_YELLOW, 0.3)
    # brackets at the panel joints; the odd joint is just taped
    for i in range(1, len(cuts) - 1):
        if not brackets:
            break
        s = cuts[i]
        h = max(heights[i - 1], heights[i])
        bas = frame_basis(path, s)
        if rng.random() < 0.2:
            c = path.point(s, side * (GUARD_X[0] + GUARD_X[1]) / 2, h - 0.06)
            B.box(c, (0.06, 0.07, 0.07), bas, "tape", C_TAPE, 0.25)
        else:
            c = path.point(s, side * 0.93, (STR_O[0] + h) / 2 - 0.01)
            B.box(c, (0.05, 0.055, h - STR_O[0] - 0.04), bas, "steel", C_STEEL, 0.2, rust=0.6)
            _, _, sd, _ = path.frame(s)
            for o in (-0.1, h - 0.08):
                p = path.point(s, side * 0.955, o)
                B.cyl(p, p + sd * side * 0.012, 0.012, 6, "steel", C_STEEL, rust=0.4)
    # patch plates bolted over holes in the stringer
    for _ in range(rng.randint(1, 2) if brackets else 0):
        ln = rng.uniform(0.16, 0.3)
        s0 = rng.uniform(0.15, L - 0.15 - ln)
        px0, px1 = sorted((side * STR_X[1], side * (STR_X[1] + 0.007)))
        B.sweep(path, s0, s0 + ln, px0, px1, -0.135, -0.02, "steel", C_STEEL, 0.25, rust=0.8)
        _, _, sd, _ = path.frame(s0)
        for s in (s0 + 0.025, s0 + ln - 0.025):
            for o in (-0.115, -0.04):
                p = path.point(s, side * (STR_X[1] + 0.007), o)
                B.cyl(p, p + path.frame(s)[2] * side * 0.008, 0.009, 6, "steel", C_STEEL, rust=0.3)

def build_cable(B, path, x=CABLE_X, sag=0.035, clip_every=0.66):
    """Power cable zip-tied along the outside of a stringer; enters and leaves each cell at the same point."""
    L = path.L
    n = max(1, int(round(L / clip_every)))
    pts = []
    for s in path.samples(0, L, 12):
        seg = (s / L) * n
        f = seg - math.floor(seg) if s < L else 0.0
        pts.append(path.point(s, x, CABLE_O - sag * math.sin(math.pi * f)))
    B.pipe(pts, 0.013, 6, "rubber", C_BLACK)
    for i in range(1, n):
        s = L * i / n
        B.box(path.point(s, x, CABLE_O), (0.035, 0.012, 0.04), frame_basis(path, s), "rubber", C_BLACK, 0.05)
    # clip plate at the start of the cell so each cell reads as its own section
    B.box(path.point(0.05, x + 0.012, CABLE_O), (0.01, 0.03, 0.05), frame_basis(path, 0.05), "steel", C_STEEL, rust=0.5)

def status_light(B, path, s, side):
    """A salvaged facility status beacon bolted onto the guard: white housing, amber lens."""
    bas = frame_basis(path, s)
    _, _, sd, up = path.frame(s)
    c = path.point(s, side * 0.945, GUARD_TOP - 0.02)
    B.box(c, (0.06, 0.11, 0.09), bas, "panel", C_WHITE, 0.3)
    B.box(c + up * 0.055, (0.05, 0.08, 0.025), bas, "glow", C_AMBER, 0.05)
    B.pipe([c - up * 0.045, c - up * 0.09 + sd * side * 0.03, path.point(s + 0.1, side * 0.975, CABLE_O)], 0.006, 5)

def clear_collection(name):
    coll = bpy.data.collections.get(name)
    if coll:
        for ob in list(coll.objects):
            me = ob.data
            bpy.data.objects.remove(ob, do_unlink=True)
            if me and me.users == 0:
                bpy.data.meshes.remove(me)
    else:
        coll = bpy.data.collections.new(name); bpy.context.scene.collection.children.link(coll)
    return coll

# ---------- pieces ----------
def build_straight():
    rng = random.Random(11); random.seed(11)
    coll = clear_collection("Conveyor_Straight")
    path = PATHS["straight"]
    build_belt(path, "Belt", coll, 2.0)
    B = Builder()
    build_side(B, path, +1, rng)
    build_side(B, path, -1, rng)
    build_cable(B, path)
    status_light(B, path, 1.35, +1)
    B.to_object("Frame", coll)
    return coll

def build_turn():
    rng = random.Random(23); random.seed(23)
    coll = clear_collection("Conveyor_TurnRight")
    path = PATHS["turn"]
    build_belt(path, "Belt", coll, 1.5)
    B = Builder()
    # outer side (left of travel): full stringer, guards and a cable
    build_side(B, path, -1, rng, panel_len=(0.34, 0.5))
    build_cable(B, path, clip_every=0.5)
    # inner side wraps round the pivot corner: stringer and a stub guard, no brackets
    build_side(B, path, +1, rng, panel_len=(2.0, 2.0), brackets=False, hubs=False)
    # pivot post: a scavenged scaffold pipe, capped, holding the inner edge down
    piv = Vector((1 - 0.07, -1 + 0.07, 0))
    B.cyl(piv + Vector((0, 0, FLOOR)), piv + Vector((0, 0, BELT_TOP + GUARD_TOP + 0.05)), 0.045, 10, "steel", C_STEEL, rust=0.5)
    B.cyl(piv + Vector((0, 0, BELT_TOP + GUARD_TOP + 0.05)), piv + Vector((0, 0, BELT_TOP + GUARD_TOP + 0.075)), 0.055, 10, "rubber", C_BLACK)
    # drive: a direct-drive gearmotor on the outer stringer's mid-arc roller, radial and kept inside the arc's
    # corner so the turn stays a clean quarter round (nothing out in the cell corner)
    sm = path.L / 2
    _, _, sd, up = path.frame(sm)
    out = -sd                                                   # outer side is the left of travel
    hub = path.point(sm, -STR_X[1], -0.068)
    ctr = hub + out * 0.1
    bas = Matrix((out, path.frame(sm)[1], up)).transposed()     # local x = outward, y = along the arc
    B.box(hub + out * 0.035, (0.07, 0.16, 0.13), bas, "metal", C_DARK, 0.2, rust=0.2)          # gearbox
    B.cyl(hub + out * 0.07, hub + out * 0.2, 0.058, 12, "metal", (0.16, 0.2, 0.26), 0.25, rust=0.15)  # motor can
    B.cyl(hub + out * 0.2, hub + out * 0.215, 0.05, 12, "metal", C_DARK, 0.1)                  # fan cowl
    B.box(ctr + up * 0.075 + out * 0.035, (0.1, 0.09, 0.03), bas, "panel", C_WHITE, 0.3)      # salvaged driver board
    B.box(ctr + up * 0.092 + out * 0.06, (0.025, 0.02, 0.008), bas, "glow", C_AMBER, 0.05)     # status LED
    B.box(ctr + up * 0.02 + out * 0.03, (0.09, 0.13, 0.02), bas, "tape", C_TAPE, 0.2)         # tape strap round the can
    B.pipe([ctr + up * 0.075, ctr + up * 0.07 + path.frame(sm - 0.12)[1] * -0.1,
            path.point(sm - 0.2, CABLE_X, CABLE_O)], 0.008, 5)
    B.to_object("Frame", coll)
    return coll

def build_slope():
    rng = random.Random(37); random.seed(37)
    coll = clear_collection("Conveyor_Slope")
    path = PATHS["slope"]
    build_belt(path, "Belt", coll, 4.5)
    B = Builder()
    build_side(B, path, +1, rng, panel_len=(0.7, 1.2))
    build_side(B, path, -1, rng, panel_len=(0.7, 1.2))
    build_cable(B, path, clip_every=0.75)
    # legs: scaffold pipes on the outside of each stringer (never under the belt, so the return run stays clear)
    for side in (-1, 1):
        x = side * 0.972
        tops = []
        for s in (2.15, path.L - 0.35):
            top = path.point(s, x, STR_O[0] + 0.02)
            foot = Vector((top.x, top.y, FLOOR))
            shim = side > 0 and s > 3
            fz = FLOOR + (0.05 if shim else 0.0)
            B.cyl(Vector((x, top.y, fz + 0.01)), top + Vector((0, 0, 0.04)), 0.026, 8, "steel", C_STEEL, rust=0.55)
            B.cyl(top - Vector((0, 0, 0.03)), top + Vector((0, 0, 0.05)), 0.036, 8, "steel", (0.22, 0.22, 0.22), rust=0.4)   # clamp
            B.box(Vector((x - side * 0.01, top.y, fz + 0.005)), (0.05, 0.13, 0.01), Matrix.Identity(3), "steel", C_STEEL, rust=0.7)
            if shim:
                B.box(Vector((x - side * 0.012, top.y, FLOOR + 0.025)), (0.05, 0.16, 0.05), Matrix.Rotation(0.08, 3, 'Z'), "wood", C_WOOD, 0.3)
            tops.append((top, Vector((x, top.y, fz + 0.12))))
        # diagonal brace in the side plane + a horizontal tie near the floor
        (t0, b0), (t1, b1) = tops
        B.cyl(b0 + Vector((0, 0, 0.0)), t1 - Vector((0, 0, 0.12)), 0.017, 6, "steel", C_STEEL, rust=0.6)
        B.cyl(Vector((x, b0.y, FLOOR + 0.35)), Vector((x, b1.y, FLOOR + 0.35)), 0.017, 6, "steel", C_STEEL, rust=0.6)
    status_light(B, path, 1.3, -1)
    B.to_object("Frame", coll)
    return coll

def mirror_to(src_coll_name, dst_coll_name):
    """Left turn = the right turn mirrored across x (winding flipped so normals stay outward)."""
    src = bpy.data.collections[src_coll_name]
    dst = clear_collection(dst_coll_name)
    for ob in src.objects:
        me = ob.data.copy()
        bm = bmesh.new(); bm.from_mesh(me)
        bmesh.ops.transform(bm, matrix=Matrix.Scale(-1, 4, Vector((1, 0, 0))), verts=bm.verts)
        bmesh.ops.reverse_faces(bm, faces=bm.faces)
        bm.to_mesh(me); bm.free()
        set_active_colors(me)
        dst.objects.link(bpy.data.objects.new(ob.name.split(".")[0], me))
    return dst

def export(coll, filename):
    bpy.ops.object.select_all(action='DESELECT')
    for ob in coll.objects:
        ob.select_set(True)
    # glTF needs unique object names across the file; rename per piece on export
    bpy.ops.export_scene.gltf(filepath=os.path.join(OUT_DIR, filename), export_format='GLB', use_selection=True,
                              export_yup=True, export_apply=True, export_vertex_color='ACTIVE',
                              export_all_vertex_colors=False, export_normals=True, export_texcoords=True,
                              export_materials='EXPORT', export_extras=False, export_cameras=False, export_lights=False)

def build_all(do_export=True):
    mats()
    pieces = [
        (build_straight(), "conveyor_straight.glb", "Straight"),
        (build_turn(), "conveyor_turn_right.glb", "TurnR"),
        (build_slope(), "conveyor_slope.glb", "Slope"),
    ]
    left = mirror_to("Conveyor_TurnRight", "Conveyor_TurnLeft")
    pieces.insert(2, (left, "conveyor_turn_left.glb", "TurnL"))
    for coll, fn, tag in pieces:
        for ob in coll.objects:
            base = ob.name.split(".")[0].split("_")[-1]
            ob.name = f"{tag}_{base}"
            ob.data.name = ob.name
    if do_export:
        for coll, fn, tag in pieces:
            # export with plain node names ("Belt", "Frame") so every scene finds its parts the same way
            for ob in coll.objects: ob.name = ob.name.split("_", 1)[1]
            export(coll, fn)
            for ob in coll.objects: ob.name = f"{tag}_{ob.name}"
    return pieces

if __name__ != "conveyor_lib":   # other build scripts exec this file as a library
    build_all(do_export=False)
